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

        // Not saved: stops a pawn from re-running the search every time the game asks for a bed.
        private readonly Dictionary<int, int> lastChecked = new Dictionary<int, int>();
        private const int RecheckTicks = GenDate.TicksPerHour;

        public GameComponent_BedNegotiation(Game game) { }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref cooldownUntil, "cooldownUntil", LookMode.Value, LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && cooldownUntil == null)
                cooldownUntil = new Dictionary<int, int>();
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

            try { return FindLoser(a, a.ownership.OwnedBed, SmarterBedsMod.Settings, out _); }
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
                    var target = FindLoser(a, a.ownership.OwnedBed, s, out float gain, only: b);
                    if (target == b) Swap(a, b, gain, s);
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

        private void SetCooldown(Pawn p)
        {
            cooldownUntil[p.thingIDNumber] = Now + (int)(SmarterBedsMod.Settings.negotiationCooldownDays * GenDate.TicksPerDay);
        }

        private Pawn FindLoser(Pawn a, Building_Bed bedA, SmarterBedsSettings s, out float bestGain, Pawn only = null)
        {
            bestGain = 0f;
            int unmetNow = Requirements.Unmet(a, bedA.GetRoom());
            Pawn best = null;

            float currentA = BedScorer.Quality(bedA, a, s);
            // A free room that is at least as good for A makes taking someone's room pointless.
            float bestFree = BedScorer.BestFreeQuality(a, s);

            foreach (var b in a.Map.mapPawns.FreeColonistsSpawned)
            {
                if (b == a || (only != null && b != only) || !Eligible(b) || OnCooldown(b)) continue;
                if (AreLovePartners(a, b)) continue; // never split a couple's rooms

                var bedB = b.ownership.OwnedBed;
                if (bedA.GetRoom() == bedB.GetRoom()) continue;
                // The owner must not be using it right now.
                if (!a.CanReserveAndReach(bedB, PathEndMode.OnCell, Danger.Some, bedB.SleepingSlotsCount)) continue;

                float gain = BedScorer.Quality(bedB, a, s) - currentA;
                if (gain <= 0f) continue;
                if (bestFree >= currentA + gain) continue;
                if (!HasPriorityOver(a, b)) continue;

                int unmetAfter = Requirements.Unmet(a, bedB.GetRoom());
                bool requirementCase = unmetNow > unmetAfter && Requirements.Rank(a) > Requirements.Rank(b);
                bool qualityCase = s.qualityNegotiationEnabled
                    && Requirements.Rank(a) == Requirements.Rank(b)
                    && gain >= s.qualityNegotiationThreshold
                    && unmetNow == 0 && unmetAfter == 0;

                if (!requirementCase && !qualityCase) continue;
                if (gain > bestGain)
                {
                    bestGain = gain;
                    best = b;
                }
            }
            return best;
        }

        private bool Swap(Pawn winner, Pawn loser, float gain, SmarterBedsSettings s)
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
            PruneCooldowns();

            if (s.verboseLogging)
                Log.Message($"[Smarter Beds] negotiation: {winner.LabelShort} took {loser.LabelShort}'s bed at {wonBed.Position} (gain {gain:0.0})");

            if (s.lostRoomThought && loser.needs?.mood?.thoughts?.memories != null)
                loser.needs.mood.thoughts.memories.TryGainMemory(DefDatabase<ThoughtDef>.GetNamed("SmarterBeds_LostRoom"));

            if (s.negotiationMessages)
                Messages.Message("SmarterBeds_RoomTaken".Translate(winner.LabelShort, loser.LabelShort),
                    new LookTargets(winner, loser), MessageTypeDefOf.NeutralEvent, false);
            return true;
        }

        private void PruneCooldowns()
        {
            int now = Now;
            foreach (var key in cooldownUntil.Where(kv => kv.Value <= now).Select(kv => kv.Key).ToList())
                cooldownUntil.Remove(key);
        }
    }
}
