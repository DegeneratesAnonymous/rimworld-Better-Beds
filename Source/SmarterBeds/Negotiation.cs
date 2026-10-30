using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SmarterBeds
{
    /// <summary>
    /// Room negotiation. A pawn who owns a bed and has a sleep-related mood debuff (or an unmet title
    /// requirement) can pick "negotiate for a better room" as a recreation activity (see
    /// JoyGiver_NegotiateRoom). They walk over, talk, and a contested roll (see NegotiationRoll)
    /// decides it. A winner takes the loser's bed; the loser is left bedless to find a room at
    /// their own bedtime.
    /// </summary>
    public class GameComponent_BedNegotiation : GameComponent
    {
        public static GameComponent_BedNegotiation Instance => Current.Game?.GetComponent<GameComponent_BedNegotiation>();

        // Saved: "lowId:highId" -> tick when these two may negotiate with each other again.
        private Dictionary<string, int> pairCooldownUntil = new Dictionary<string, int>();

        // Saved: "loserId>winnerId" -> tick until which the loser may not challenge that winner again.
        private Dictionary<string, int> rematchUntil = new Dictionary<string, int>();
        private const int RematchDays = 15;
        private const float MinWinChance = 0.1f;

        // Mood effect of a negotiation = MaxMoodEffect x (0.25 x relationship + 0.25 x room quality),
        // both measured 0..1. Relationship is the loser's opinion of the asker / 100; room quality is
        // the quality gain of the room that changed hands / QualityForFullEffect.
        private const float MaxMoodEffect = 20f;
        private const float QualityForFullEffect = 500f;

        // Experience for the asker. A failed attempt is worth half.
        private const float SocialXpOnWin = 60f;

        // Not saved: stops a pawn from re-running the search every time the joy system asks.
        private readonly Dictionary<int, int> lastChecked = new Dictionary<int, int>();
        private const int RecheckTicks = GenDate.TicksPerHour;

        public GameComponent_BedNegotiation(Game game) { }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref pairCooldownUntil, "pairCooldownUntil", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref rematchUntil, "rematchUntil", LookMode.Value, LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (pairCooldownUntil == null) pairCooldownUntil = new Dictionary<string, int>();
                if (rematchUntil == null) rematchUntil = new Dictionary<string, int>();
            }
        }

        private static int Now => Find.TickManager.TicksGame;

        private static string PairKey(Pawn a, Pawn b) =>
            a.thingIDNumber < b.thingIDNumber ? a.thingIDNumber + ":" + b.thingIDNumber : b.thingIDNumber + ":" + a.thingIDNumber;

        private static string RematchKey(Pawn loser, Pawn winner) => loser.thingIDNumber + ">" + winner.thingIDNumber;

        private bool PairOnCooldown(Pawn a, Pawn b) =>
            pairCooldownUntil.TryGetValue(PairKey(a, b), out int until) && Now < until;

        private void StartPairCooldown(Pawn a, Pawn b) =>
            pairCooldownUntil[PairKey(a, b)] = Now + (int)(SmarterBedsMod.Settings.negotiationCooldownDays * GenDate.TicksPerDay);

        // Thoughts that mean "the place I slept was bad". SleepDisturbed is noise, not the room.
        private static readonly List<ThoughtDef> SleepDebuffs = new List<ThoughtDef>
        {
            ThoughtDefOf.SleptInBarracks, ThoughtDefOf.SleptOutside, ThoughtDefOf.SleptOnGround,
            ThoughtDefOf.SleptInCold, ThoughtDefOf.SleptInHeat, ThoughtDefOf.SleptInRoomWithSlave
        };

        public static bool HasSleepDebuff(Pawn p)
        {
            var memories = p.needs?.mood?.thoughts?.memories;
            if (memories == null) return false;
            foreach (var def in SleepDebuffs)
            {
                if (def != null && memories.GetFirstMemoryOfDef(def) != null) return true;
            }
            return false;
        }

        /// <summary>Common checks for both sides: a settled adult colonist who is the only owner of their bed.</summary>
        private static bool Eligible(Pawn p)
        {
            if (p == null || !p.Spawned || p.Dead || !p.IsColonist || p.IsPrisoner || p.IsSlave) return false;
            if (p.HostFaction != null) return false;
            if ((p.DevelopmentalStage & DevelopmentalStage.Adult) == 0) return false;
            if (HealthAIUtility.ShouldSeekMedicalRest(p)) return false;
            var bed = p.ownership?.OwnedBed;
            if (bed == null || bed.Medical || !bed.Spawned) return false;
            // Shared beds belong to two people (usually a couple); leave those to vanilla.
            // A double bed with a single owner is fine.
            if (bed.OwnersForReading.Count != 1) return false;
            return true;
        }

        private static bool AreLovePartners(Pawn a, Pawn b)
        {
            foreach (var rel in LovePartnerRelationUtility.ExistingLovePartners(a, false))
            {
                if (rel.otherPawn == b) return true;
            }
            return false;
        }

        /// <summary>Cheap check: is this pawn in a position where negotiating makes sense at all?</summary>
        public bool WantsToNegotiate(Pawn a)
        {
            var s = SmarterBedsMod.Settings;
            if (!s.enabled || !s.negotiationEnabled) return false;
            if (!Eligible(a)) return false;
            // Only negotiate when something is actually wrong with how they sleep.
            return Requirements.Unmet(a, a.ownership.OwnedBed.GetRoom()) > 0 || HasSleepDebuff(a);
        }

        /// <summary>Finds who this pawn would negotiate with, or null. Throttled to once an hour per pawn.</summary>
        public Pawn FindNegotiationTarget(Pawn a)
        {
            if (!WantsToNegotiate(a)) return null;
            if (lastChecked.TryGetValue(a.thingIDNumber, out int last) && Now - last < RecheckTicks) return null;
            lastChecked[a.thingIDNumber] = Now;

            try { return FindLoser(a, a.ownership.OwnedBed, SmarterBedsMod.Settings, out _, out _); }
            catch (System.Exception e)
            {
                Log.Error($"[Smarter Beds] finding a negotiation target failed: {e}");
                return null;
            }
        }

        /// <summary>
        /// End of the conversation: re-checks everything (the world moved while they walked over),
        /// rolls, and swaps on a win. The two are on cooldown with each other afterwards either way.
        /// </summary>
        public void ResolveNegotiation(Pawn a, Pawn b)
        {
            var s = SmarterBedsMod.Settings;
            try
            {
                if (WantsToNegotiate(a) && Eligible(b))
                {
                    var target = FindLoser(a, a.ownership.OwnedBed, s, out float gain, out var contest, only: b);
                    if (target == b)
                    {
                        bool won = NegotiationRoll.Roll(contest, out int askerTotal, out int defenderTotal);
                        GiveSocialExperience(a, won);
                        if (contest.approach == NegotiationApproach.Intimidate) Resent(b, a);
                        if (won) Swap(a, b, gain, s, contest, askerTotal, defenderTotal);
                        else ReportFailure(a, b, contest, askerTotal, defenderTotal, s);
                    }
                }
            }
            catch (System.Exception e)
            {
                Log.Error($"[Smarter Beds] negotiation failed: {e}");
            }
            finally
            {
                StartPairCooldown(a, b);
            }
        }

        private static void GiveSocialExperience(Pawn asker, bool won)
        {
            // Practice either way; half as much when turned down.
            asker.skills?.Learn(SkillDefOf.Social, won ? SocialXpOnWin : SocialXpOnWin / 2f);
        }

        /// <summary>Someone who was leaned on remembers it, win or lose.</summary>
        private static void Resent(Pawn victim, Pawn bully)
        {
            victim.needs?.mood?.thoughts?.memories?.TryGainMemory(
                DefDatabase<ThoughtDef>.GetNamed("SmarterBeds_PushedAround"), bully);
        }

        /// <summary>
        /// The loser's mood after a successful negotiation, scaled 0.25 x relationship + 0.25 x room quality.
        /// Intimidation is always a debuff. Persuasion is a buff when the loser likes the asker;
        /// someone who doesn't gives the room up with no particular feeling either way.
        /// </summary>
        private static void ApplyMoodEffect(Pawn loser, Pawn winner, NegotiationContest c, float gain, SmarterBedsSettings s)
        {
            if (!s.negotiationMoodEffects || loser.needs?.mood?.thoughts?.memories == null) return;

            float relationship = loser.relations != null ? Mathf.Clamp(loser.relations.OpinionOf(winner) / 100f, -1f, 1f) : 0f;
            float quality = Mathf.Clamp01(gain / QualityForFullEffect);

            ThoughtDef def;
            float magnitude;
            if (c.approach == NegotiationApproach.Intimidate)
            {
                def = DefDatabase<ThoughtDef>.GetNamed("SmarterBeds_BulliedOutOfRoom");
                magnitude = MaxMoodEffect * (0.25f * Mathf.Abs(relationship) + 0.25f * quality);
            }
            else
            {
                if (relationship <= 0f) return;
                def = DefDatabase<ThoughtDef>.GetNamed("SmarterBeds_GaveUpRoomHappily");
                magnitude = MaxMoodEffect * (0.25f * relationship + 0.25f * quality);
            }

            var thought = (Thought_Memory)ThoughtMaker.MakeThought(def);
            thought.moodPowerFactor = Mathf.Max(1f, magnitude); // the stage is +/-1, so this is the mood points
            loser.needs.mood.thoughts.memories.TryGainMemory(thought, winner);

            if (s.verboseLogging)
                Log.Message($"[Smarter Beds] {loser.LabelShort} mood effect {(c.approach == NegotiationApproach.Intimidate ? "-" : "+")}{thought.moodPowerFactor:0.0} (relationship {relationship:0.00}, quality {quality:0.00})");
        }

        private static void ReportFailure(Pawn asker, Pawn defender, NegotiationContest c, int askerTotal, int defenderTotal, SmarterBedsSettings s)
        {
            if (s.verboseLogging)
                Log.Message($"[Smarter Beds] negotiation failed: {asker.LabelShort} vs {defender.LabelShort} ({c.approach}) {askerTotal} vs {defenderTotal}");
            if (s.negotiationMessages)
                Messages.Message("SmarterBeds_RoomFailed".Translate(asker.LabelShort, defender.LabelShort, askerTotal, defenderTotal),
                    new LookTargets(asker, defender), MessageTypeDefOf.NeutralEvent, false);
        }

        private Pawn FindLoser(Pawn a, Building_Bed bedA, SmarterBedsSettings s, out float bestGain, out NegotiationContest bestContest, Pawn only = null)
        {
            bestGain = 0f;
            bestContest = default;
            float bestValue = 0f;
            int unmetNow = Requirements.Unmet(a, bedA.GetRoom());
            Pawn best = null;

            float currentA = BedScorer.Quality(bedA, a, s);
            // A free room that is at least as good for A makes taking someone's room pointless.
            float bestFree = BedScorer.BestFreeQuality(a, s);

            foreach (var b in a.Map.mapPawns.FreeColonistsSpawned)
            {
                if (b == a || (only != null && b != only) || !Eligible(b)) continue;
                if (PairOnCooldown(a, b)) continue;
                if (AreLovePartners(a, b)) continue; // never split a couple's rooms
                if (rematchUntil.TryGetValue(RematchKey(a, b), out int rematch) && Now < rematch) continue;

                var bedB = b.ownership.OwnedBed;
                if (bedA.GetRoom() == bedB.GetRoom()) continue;
                // The owner must not be using it right now.
                if (!a.CanReserveAndReach(bedB, PathEndMode.OnCell, Danger.Some, bedB.SleepingSlotsCount)) continue;

                float gain = BedScorer.Quality(bedB, a, s) - currentA;
                if (gain <= 0f) continue;
                if (bestFree >= currentA + gain) continue;

                // Anyone may try; rank only matters as a number in the roll.
                int unmetAfter = Requirements.Unmet(a, bedB.GetRoom());
                bool requirementCase = unmetNow > unmetAfter;
                bool qualityCase = s.qualityNegotiationEnabled
                    && gain >= s.qualityNegotiationThreshold
                    && unmetNow == 0 && unmetAfter == 0;
                if (!requirementCase && !qualityCase) continue;

                var contest = NegotiationRoll.Build(a, b, s);
                // Don't spend a conversation on something with almost no chance.
                if (!contest.possible || contest.winChance < MinWinChance) continue;

                float value = gain * contest.winChance;
                if (value > bestValue)
                {
                    bestValue = value;
                    bestGain = gain;
                    bestContest = contest;
                    best = b;
                }
            }
            return best;
        }

        private bool Swap(Pawn winner, Pawn loser, float gain, SmarterBedsSettings s, NegotiationContest contest, int winnerTotal, int loserTotal)
        {
            var wonBed = loser.ownership.OwnedBed;
            var oldBed = winner.ownership.OwnedBed;

            loser.ownership.UnclaimBed();
            winner.ownership.UnclaimBed();
            if (!winner.ownership.ClaimBedIfNonMedical(wonBed))
            {
                // Could not claim: put everyone back exactly as they were.
                winner.ownership.ClaimBedIfNonMedical(oldBed);
                loser.ownership.ClaimBedIfNonMedical(wonBed);
                return false;
            }

            // The loser can't just turn around and take it back.
            rematchUntil[RematchKey(loser, winner)] = Now + RematchDays * GenDate.TicksPerDay;
            PruneCooldowns();

            if (s.verboseLogging)
                Log.Message($"[Smarter Beds] negotiation: {winner.LabelShort} took {loser.LabelShort}'s bed at {wonBed.Position} (gain {gain:0.0})");

            ApplyMoodEffect(loser, winner, contest, gain, s);

            if (s.negotiationMessages)
            {
                string key = contest.approach == NegotiationApproach.Intimidate ? "SmarterBeds_RoomWonIntimidate" : "SmarterBeds_RoomWonPersuade";
                Messages.Message(key.Translate(winner.LabelShort, loser.LabelShort, winnerTotal, loserTotal),
                    new LookTargets(winner, loser), MessageTypeDefOf.NeutralEvent, false);
            }
            return true;
        }

        private void PruneCooldowns()
        {
            int now = Now;
            foreach (var key in pairCooldownUntil.Where(kv => kv.Value <= now).Select(kv => kv.Key).ToList())
                pairCooldownUntil.Remove(key);
            foreach (var key in rematchUntil.Where(kv => kv.Value <= now).Select(kv => kv.Key).ToList())
                rematchUntil.Remove(key);
        }
    }
}
