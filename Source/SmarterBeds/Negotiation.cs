using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace SmarterBeds
{
    /// <summary>
    /// Periodic room negotiation between two pawns who both own a bed.
    /// The winner takes the loser's bed; the loser is left bedless and finds a room at bedtime.
    /// </summary>
    public class GameComponent_BedNegotiation : GameComponent
    {
        private const int CheckIntervalTicks = GenDate.TicksPerHour;

        // pawn thingIDNumber -> tick when they may negotiate again
        private Dictionary<int, int> cooldownUntil = new Dictionary<int, int>();

        public GameComponent_BedNegotiation(Game game) { }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref cooldownUntil, "cooldownUntil", LookMode.Value, LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && cooldownUntil == null)
                cooldownUntil = new Dictionary<int, int>();
        }

        public override void GameComponentTick()
        {
            if (Find.TickManager.TicksGame % CheckIntervalTicks != 0) return;
            var s = SmarterBedsMod.Settings;
            if (!s.enabled || !s.negotiationEnabled) return;

            PruneCooldowns();
            foreach (var map in Find.Maps)
            {
                if (!map.IsPlayerHome) continue;
                try { TryNegotiate(map, s); }
                catch (System.Exception e) { Log.Error($"[Smarter Beds] negotiation failed: {e}"); }
            }
        }

        private void PruneCooldowns()
        {
            int now = Find.TickManager.TicksGame;
            foreach (var key in cooldownUntil.Where(kv => kv.Value <= now).Select(kv => kv.Key).ToList())
                cooldownUntil.Remove(key);
        }

        /// <summary>
        /// Total order used to break ties between equal-rank pawns: longer time in the colony wins,
        /// then the lower id. Because it is strict and never changes in a pair's favor, two pawns can
        /// never take the same room back and forth.
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

        private bool OnCooldown(Pawn p) =>
            cooldownUntil.TryGetValue(p.thingIDNumber, out int until) && Find.TickManager.TicksGame < until;

        private static bool Eligible(Pawn p)
        {
            if (p == null || !p.Spawned || p.Dead || !p.IsColonist || p.IsPrisoner || p.IsSlave) return false;
            if (p.HostFaction != null) return false;
            if (!p.Awake()) return false; // never take a bed out from under a sleeper
            if ((p.DevelopmentalStage & DevelopmentalStage.Adult) == 0) return false;
            if (HealthAIUtility.ShouldSeekMedicalRest(p)) return false;
            var bed = p.ownership?.OwnedBed;
            if (bed == null || bed.Medical || !bed.Spawned) return false;
            // Single sleeper in a single-slot bed: leaves couples and shared beds to vanilla.
            if (bed.SleepingSlotsCount != 1 || bed.OwnersForReading.Count != 1) return false;
            return true;
        }

        private void TryNegotiate(Map map, SmarterBedsSettings s)
        {
            var pawns = map.mapPawns.FreeColonistsSpawned.Where(p => Eligible(p) && !OnCooldown(p)).ToList();
            if (pawns.Count < 2) return;

            Pawn bestWinner = null, bestLoser = null;
            float bestGain = 0f;

            foreach (var a in pawns)
            {
                var bedA = a.ownership.OwnedBed;
                float currentA = BedScorer.Quality(bedA, a, s);
                int unmetNow = Requirements.Unmet(a, bedA.GetRoom());
                // A free room that is at least as good for A makes taking someone's room pointless.
                float bestFree = BedScorer.BestFreeQuality(a, s);

                foreach (var b in pawns)
                {
                    if (a == b) continue;
                    var bedB = b.ownership.OwnedBed;
                    if (bedA.GetRoom() == bedB.GetRoom()) continue;

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
                        bestWinner = a;
                        bestLoser = b;
                    }
                }
            }

            if (bestWinner == null) return;
            Swap(bestWinner, bestLoser, bestGain, s);
        }

        private void Swap(Pawn winner, Pawn loser, float gain, SmarterBedsSettings s)
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
                return;
            }

            int until = Find.TickManager.TicksGame + (int)(s.negotiationCooldownDays * GenDate.TicksPerDay);
            cooldownUntil[winner.thingIDNumber] = until;
            cooldownUntil[loser.thingIDNumber] = until;

            if (s.verboseLogging)
                Log.Message($"[Smarter Beds] negotiation: {winner.LabelShort} took {loser.LabelShort}'s bed at {wonBed.Position} (gain {gain:0.0})");

            if (s.lostRoomThought && loser.needs?.mood?.thoughts?.memories != null)
                loser.needs.mood.thoughts.memories.TryGainMemory(DefDatabase<ThoughtDef>.GetNamed("SmarterBeds_LostRoom"));

            if (s.negotiationMessages)
                Messages.Message("SmarterBeds_RoomTaken".Translate(winner.LabelShort, loser.LabelShort),
                    new LookTargets(winner, loser), MessageTypeDefOf.NeutralEvent, false);
        }
    }
}
