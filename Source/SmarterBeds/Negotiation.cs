using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace SmarterBeds
{
    /// <summary>
    /// Room negotiation. A pawn who owns a bed and has a sleep-related mood debuff (or an unmet title
    /// requirement) can pick "negotiate for a better room" as a recreation activity (see
    /// JoyGiver_NegotiateRoom). If they get to talk to someone they outrank, the winner takes the
    /// loser's bed and the loser is left bedless to find a room at their own bedtime.
    /// </summary>
    public class GameComponent_BedNegotiation : GameComponent
    {
        public static GameComponent_BedNegotiation Instance => Current.Game?.GetComponent<GameComponent_BedNegotiation>();

        // Saved: pawn thingIDNumber -> tick when they may negotiate again after a swap.
        private Dictionary<int, int> cooldownUntil = new Dictionary<int, int>();

        // Saved: "loserId>winnerId" -> tick until which the loser may not challenge that winner again.
        private Dictionary<string, int> rematchUntil = new Dictionary<string, int>();
        private const int RematchDays = 15;
        private const float MinWinChance = 0.1f;

        // Not saved: stops a pawn from re-running the search every time the game asks for a bed.
        private readonly Dictionary<int, int> lastChecked = new Dictionary<int, int>();
        private const int RecheckTicks = GenDate.TicksPerHour;

        public GameComponent_BedNegotiation(Game game) { }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref cooldownUntil, "cooldownUntil", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref rematchUntil, "rematchUntil", LookMode.Value, LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (cooldownUntil == null) cooldownUntil = new Dictionary<int, int>();
                if (rematchUntil == null) rematchUntil = new Dictionary<string, int>();
            }
        }

        private static int Now => Find.TickManager.TicksGame;

        private bool OnCooldown(Pawn p) =>
            cooldownUntil.TryGetValue(p.thingIDNumber, out int until) && Now < until;

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

        /// <summary>
        /// Total order used to break ties between equal-rank pawns: longer time in the colony wins,
        /// then the lower id. Because it is strict, two pawns can never take the same room back and
        /// forth.
        /// </summary>
        private static bool HasPriorityOver(Pawn a, Pawn b)
        {
            int rankA = Requirements.Rank(a), rankB = Requirements.Rank(b);
            if (rankA != rankB) return rankA > rankB;
            int tenureA = a.records.GetAsInt(RecordDefOf.TimeAsColonistOrColonyAnimal);
            int tenureB = b.records.GetAsInt(RecordDefOf.TimeAsColonistOrColonyAnimal);
            if (tenureA != tenureB) return tenureA > tenureB;
            return a.thingIDNumber < b.thingIDNumber;
        }

        /// <summary>Common checks for both sides: a settled adult colonist alone in a single bed.</summary>
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
            if (!Eligible(a) || OnCooldown(a)) return false;
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
        /// End of the conversation: re-checks everything (the world moved while they walked over) and,
        /// if it still holds, swaps. The initiator is on cooldown afterwards whether or not it worked.
        /// </summary>
        public void ResolveNegotiation(Pawn a, Pawn b)
        {
            var s = SmarterBedsMod.Settings;
            try
            {
                if (WantsToNegotiate(a) && Eligible(b) && !OnCooldown(b))
                {
                    var target = FindLoser(a, a.ownership.OwnedBed, s, out float gain, out var contest, only: b);
                    if (target == b)
                    {
                        if (!s.useNegotiationRoll)
                        {
                            Swap(a, b, gain, s, null, 0, 0);
                        }
                        else
                        {
                            bool won = NegotiationRoll.Roll(contest, out int askerTotal, out int defenderTotal);
                            GiveSocialExperience(a, won);
                            if (contest.approach == NegotiationApproach.Intimidate) Resent(b, a);
                            if (won) Swap(a, b, gain, s, contest, askerTotal, defenderTotal);
                            else ReportFailure(a, b, contest, askerTotal, defenderTotal, s);
                        }
                    }
                }
            }
            catch (System.Exception e)
            {
                Log.Error($"[Smarter Beds] negotiation failed: {e}");
            }
            finally
            {
                SetCooldown(a);
            }
        }

        private static void GiveSocialExperience(Pawn asker, bool won)
        {
            // Winning is more instructive than being turned down, but both are practice.
            asker.skills?.Learn(SkillDefOf.Social, won ? 60f : 35f);
        }

        /// <summary>Someone who was leaned on remembers it.</summary>
        private static void Resent(Pawn victim, Pawn bully)
        {
            victim.needs?.mood?.thoughts?.memories?.TryGainMemory(
                DefDatabase<ThoughtDef>.GetNamed("SmarterBeds_PushedAround"), bully);
        }

        private static void ReportFailure(Pawn asker, Pawn defender, NegotiationContest c, int askerTotal, int defenderTotal, SmarterBedsSettings s)
        {
            if (s.verboseLogging)
                Log.Message($"[Smarter Beds] negotiation failed: {asker.LabelShort} vs {defender.LabelShort} ({c.approach}) {askerTotal} vs {defenderTotal}");
            if (s.negotiationMessages)
                Messages.Message("SmarterBeds_RoomFailed".Translate(asker.LabelShort, defender.LabelShort, askerTotal, defenderTotal),
                    new LookTargets(asker, defender), MessageTypeDefOf.NeutralEvent, false);
        }

        private void SetCooldown(Pawn p)
        {
            cooldownUntil[p.thingIDNumber] = Now + (int)(SmarterBedsMod.Settings.negotiationCooldownDays * GenDate.TicksPerDay);
        }

        private Pawn FindLoser(Pawn a, Building_Bed bedA, SmarterBedsSettings s, out float bestGain, out NegotiationContest bestContest, Pawn only = null)
        {
            bestGain = 0f;
            bestContest = default;
            float bestValue = 0f;
            bool roll = s.useNegotiationRoll;
            int unmetNow = Requirements.Unmet(a, bedA.GetRoom());
            Pawn best = null;

            float currentA = BedScorer.Quality(bedA, a, s);
            // A free room that is at least as good for A makes taking someone's room pointless.
            float bestFree = BedScorer.BestFreeQuality(a, s);

            foreach (var b in a.Map.mapPawns.FreeColonistsSpawned)
            {
                if (b == a || (only != null && b != only) || !Eligible(b) || OnCooldown(b)) continue;
                if (AreLovePartners(a, b)) continue; // never split a couple's rooms
                if (rematchUntil.TryGetValue(RematchKey(a, b), out int rematch) && Now < rematch) continue;

                var bedB = b.ownership.OwnedBed;
                if (bedA.GetRoom() == bedB.GetRoom()) continue;
                // The owner must not be using it right now.
                if (!a.CanReserveAndReach(bedB, PathEndMode.OnCell, Danger.Some, bedB.SleepingSlotsCount)) continue;

                float gain = BedScorer.Quality(bedB, a, s) - currentA;
                if (gain <= 0f) continue;
                if (bestFree >= currentA + gain) continue;
                // Strict rules: higher title, then longer-serving, decides before anyone talks.
                // Roll mode lets anyone try; the title is just one of the numbers in the roll.
                if (!roll && !HasPriorityOver(a, b)) continue;

                int unmetAfter = Requirements.Unmet(a, bedB.GetRoom());
                bool requirementCase = unmetNow > unmetAfter && (roll || Requirements.Rank(a) > Requirements.Rank(b));
                bool qualityCase = s.qualityNegotiationEnabled
                    && (roll || Requirements.Rank(a) == Requirements.Rank(b))
                    && gain >= s.qualityNegotiationThreshold
                    && unmetNow == 0 && unmetAfter == 0;

                if (!requirementCase && !qualityCase) continue;

                var contest = default(NegotiationContest);
                float value = gain;
                if (roll)
                {
                    contest = NegotiationRoll.Build(a, b, s);
                    // Don't spend three days on a conversation that has almost no chance.
                    if (!contest.possible || contest.winChance < MinWinChance) continue;
                    value = gain * contest.winChance;
                }

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

        private bool Swap(Pawn winner, Pawn loser, float gain, SmarterBedsSettings s, NegotiationContest? contest, int winnerTotal, int loserTotal)
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

            SetCooldown(winner);
            SetCooldown(loser);
            // The loser can't just turn around and take it back.
            rematchUntil[RematchKey(loser, winner)] = Now + RematchDays * GenDate.TicksPerDay;
            PruneCooldowns();

            if (s.verboseLogging)
                Log.Message($"[Smarter Beds] negotiation: {winner.LabelShort} took {loser.LabelShort}'s bed at {wonBed.Position} (gain {gain:0.0})");

            if (s.lostRoomThought && loser.needs?.mood?.thoughts?.memories != null)
                loser.needs.mood.thoughts.memories.TryGainMemory(DefDatabase<ThoughtDef>.GetNamed("SmarterBeds_LostRoom"));

            if (s.negotiationMessages)
            {
                string text;
                if (contest == null)
                    text = "SmarterBeds_RoomTaken".Translate(winner.LabelShort, loser.LabelShort);
                else if (contest.Value.approach == NegotiationApproach.Intimidate)
                    text = "SmarterBeds_RoomWonIntimidate".Translate(winner.LabelShort, loser.LabelShort, winnerTotal, loserTotal);
                else
                    text = "SmarterBeds_RoomWonPersuade".Translate(winner.LabelShort, loser.LabelShort, winnerTotal, loserTotal);
                Messages.Message(text, new LookTargets(winner, loser), MessageTypeDefOf.NeutralEvent, false);
            }
            return true;
        }

        private static string RematchKey(Pawn loser, Pawn winner) => loser.thingIDNumber + ">" + winner.thingIDNumber;

        private void PruneCooldowns()
        {
            int now = Now;
            foreach (var key in cooldownUntil.Where(kv => kv.Value <= now).Select(kv => kv.Key).ToList())
                cooldownUntil.Remove(key);
            foreach (var key in rematchUntil.Where(kv => kv.Value <= now).Select(kv => kv.Key).ToList())
                rematchUntil.Remove(key);
        }
    }
}
