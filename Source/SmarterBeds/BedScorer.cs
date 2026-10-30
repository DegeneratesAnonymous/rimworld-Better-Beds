using RimWorld;
using Verse;
using Verse.AI;

namespace SmarterBeds
{
    /// <summary>
    /// Pure bed-choice logic. No Harmony here, so it can be reasoned about on its own.
    /// Scores are in mood points (see RoomComfort), so every weight below is a number of mood.
    /// A bedless colonist gets the nicest free bed they can reach.
    /// </summary>
    public static class BedScorer
    {
        /// <summary>True when this mod should decide, false to leave it to vanilla.</summary>
        public static bool ShouldHandle(Pawn sleeper, Pawn traveler)
        {
            if (sleeper == null || traveler == null) return false;
            if (!sleeper.Spawned || sleeper.Map == null) return false;
            if (!sleeper.IsColonist || sleeper.IsPrisoner || sleeper.IsSlave) return false;
            if (sleeper.HostFaction != null) return false;                                   // guests
            if ((sleeper.DevelopmentalStage & DevelopmentalStage.Adult) == 0) return false;  // babies/children
            if (sleeper.ownership?.OwnedBed != null) return false;                           // already has a bed
            if (HealthAIUtility.ShouldSeekMedicalRest(sleeper)) return false;                // medical beds
            return true;
        }

        // A bedless colonist asks for a bed every time they think about sleeping, and each answer means
        // scoring every free bed in the colony. The best bed does not change in a few seconds, so the
        // answer (including "none free") is kept for 180 ticks and checked again before it is used.
        private class Remembered { public int tick; public Building_Bed bed; }
        private static readonly System.Collections.Generic.Dictionary<int, Remembered> remembered = new System.Collections.Generic.Dictionary<int, Remembered>();
        private const int RememberTicks = 180;

        public static void ClearCache() => remembered.Clear();

        public static Building_Bed FindBest(Pawn sleeper, Pawn traveler)
        {
            int now = Find.TickManager.TicksGame;
            bool cacheable = sleeper == traveler;
            if (cacheable && remembered.TryGetValue(sleeper.thingIDNumber, out var memo) && now >= memo.tick && now - memo.tick < RememberTicks)
            {
                if (memo.bed == null) return null;
                if (IsCandidate(memo.bed, sleeper, traveler)) return memo.bed;
            }

            var found = Search(sleeper, traveler);
            if (cacheable)
            {
                if (remembered.Count > 200) remembered.Clear();
                remembered[sleeper.thingIDNumber] = new Remembered { tick = now, bed = found };
            }
            return found;
        }

        private static Building_Bed Search(Pawn sleeper, Pawn traveler)
        {
            var s = SmarterBedsMod.Settings;
            Building_Bed best = null;
            float bestScore = float.MinValue;

            foreach (var bed in sleeper.Map.listerBuildings.AllBuildingsColonistOfClass<Building_Bed>())
            {
                if (!IsCandidate(bed, sleeper, traveler)) continue;

                float score = Score(bed, sleeper, traveler, s);
                if (s.verboseLogging)
                    Log.Message($"[Smarter Beds] {sleeper.LabelShort}: {bed.LabelShort} at {bed.Position} scored {score:0.0}");

                if (score > bestScore)
                {
                    bestScore = score;
                    best = bed;
                }
            }

            if (s.verboseLogging)
                Log.Message($"[Smarter Beds] {sleeper.LabelShort} -> {(best == null ? "vanilla choice" : best.LabelShort + " at " + best.Position)}");

            return best;
        }

        /// <summary>Best quality among free beds this pawn could claim now; MinValue when there are none.</summary>
        public static float BestFreeQuality(Pawn pawn, SmarterBedsSettings s)
        {
            float best = float.MinValue;
            foreach (var bed in pawn.Map.listerBuildings.AllBuildingsColonistOfClass<Building_Bed>())
            {
                if (!IsCandidate(bed, pawn, pawn)) continue;
                float q = Quality(bed, pawn, s);
                if (q > best) best = q;
            }
            return best;
        }

        /// <summary>
        /// A free bed this pawn may use. The final word goes to the game's own IsValidBedFor, which
        /// every other mod that cares about beds patches (Hospitality keeps guest beds away from
        /// colonists, gene mods change who can sleep where, and so on). Anything they forbid, so do we.
        /// </summary>
        public static bool IsCandidate(Building_Bed bed, Pawn sleeper, Pawn traveler)
        {
            if (!bed.Spawned || bed.Destroyed || bed.IsBurning()) return false;
            if (bed.Medical || !bed.ForColonists) return false;
            // Only fully unowned beds: joining a half-claimed bed is vanilla's business (couples).
            if (bed.OwnersForReading.Count > 0) return false;
            if (!RestUtility.CanUseBedEver(sleeper, bed.def)) return false;
            return RestUtility.IsValidBedFor(bed, sleeper, traveler, true, false, false, sleeper.GuestStatus);
        }

        /// <summary>
        /// How good this bed is for this pawn, in mood points, ignoring distance.
        /// Used by the bedtime search and by room negotiation.
        /// </summary>
        public static float Quality(Building_Bed bed, Pawn pawn, SmarterBedsSettings s, Pawn leaving = null) =>
            RoomComfort.Points(bed, pawn, s, company: true, leaving: leaving);

        // Distance does not count against a bed: a colonist takes the nicest bed they can reach, however
        // far it is. It only breaks ties, so that of two equally good beds the nearer one wins.
        private const float TieBreakPerCell = 0.0001f;

        private static float Score(Building_Bed bed, Pawn sleeper, Pawn traveler, SmarterBedsSettings s)
        {
            float score = Quality(bed, sleeper, s);
            score -= (bed.Position - traveler.Position).LengthHorizontal * TieBreakPerCell;
            return score;
        }
    }
}
