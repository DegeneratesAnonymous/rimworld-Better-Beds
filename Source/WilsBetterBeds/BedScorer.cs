using RimWorld;
using Verse;
using Verse.AI;

namespace WilsBetterBeds
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
            bool baby = Family.IsBaby(sleeper);
            // A baby being carried to a crib is not spawned; the person carrying it is.
            if (!baby && (!sleeper.Spawned || sleeper.Map == null)) return false;
            if (baby && (sleeper.Map ?? traveler.Map) == null) return false;
            if (!sleeper.IsColonist || sleeper.IsPrisoner || sleeper.IsSlave) return false;
            if (sleeper.HostFaction != null) return false;                                   // guests
            if ((sleeper.DevelopmentalStage & DevelopmentalStage.Adult) == 0 && !ShouldHandleYoung(sleeper)) return false;
            if (sleeper.ownership?.OwnedBed != null) return false;                           // already has a bed
            if (HealthAIUtility.ShouldSeekMedicalRest(sleeper)) return false;                // medical beds
            return true;
        }

        /// <summary>Babies and children are ours only when their settings are on (cribs near the mother, beds near the parents).</summary>
        private static bool ShouldHandleYoung(Pawn p)
        {
            var s = WilsBetterBedsMod.Settings;
            if (Family.IsBaby(p)) return s.cribNearMotherEnabled;
            if (Family.IsChild(p)) return s.childrenEnabled;
            return false;
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

        /// <summary>A baby's bed: the free crib nearest its mother's bed (her father's, if she is not here). Null leaves it to the game.</summary>
        private static Building_Bed SearchCrib(Pawn baby, Pawn traveler)
        {
            var map = baby.Map ?? traveler.Map;
            if (!Family.NurseryAnchor(baby, map, out IntVec3 anchor)) return null;

            Building_Bed best = null;
            float bestDistance = float.MaxValue;
            foreach (var bed in map.listerBuildings.AllBuildingsColonistOfClass<Building_Bed>())
            {
                if (!Family.IsCrib(bed) || !IsCandidate(bed, baby, traveler)) continue;
                float distance = Family.WalkOrLineDistance(map, anchor, bed.Position, WilsBetterBedsMod.Settings);
                if (distance < bestDistance) { bestDistance = distance; best = bed; }
            }
            if (WilsBetterBedsMod.Settings.verboseLogging)
                Log.Message($"[Wil's Better Beds] {baby.LabelShort} (baby) -> {(best == null ? "vanilla choice" : "crib at " + best.Position + ", " + bestDistance.ToString("0") + " from the nursery")}");
            return best;
        }

        private static Building_Bed Search(Pawn sleeper, Pawn traveler)
        {
            if (Family.IsBaby(sleeper)) return SearchCrib(sleeper, traveler);
            var s = WilsBetterBedsMod.Settings;
            Building_Bed best = null;
            float bestScore = float.MinValue;

            foreach (var bed in sleeper.Map.listerBuildings.AllBuildingsColonistOfClass<Building_Bed>())
            {
                if (!IsCandidate(bed, sleeper, traveler)) continue;

                float score = Score(bed, sleeper, traveler, s);
                if (s.verboseLogging)
                    Log.Message($"[Wil's Better Beds] {sleeper.LabelShort}: {bed.LabelShort} at {bed.Position} scored {score:0.0}");

                if (score > bestScore)
                {
                    bestScore = score;
                    best = bed;
                }
            }

            if (s.verboseLogging)
                Log.Message($"[Wil's Better Beds] {sleeper.LabelShort} -> {(best == null ? "vanilla choice" : best.LabelShort + " at " + best.Position)}");

            return best;
        }

        /// <summary>Best quality among free beds this pawn could claim now; MinValue when there are none.</summary>
        public static float BestFreeQuality(Pawn pawn, WilsBetterBedsSettings s)
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
            if (bed.Medical || !bed.ForColonists || InnBedCompat.IsInn(bed)) return false;      // an inn bed is the market's
            // Only fully unowned beds: joining a half-claimed bed is vanilla's business (couples).
            if (bed.OwnersForReading.Count > 0) return false;
            if (!RestUtility.CanUseBedEver(sleeper, bed.def)) return false;
            return RestUtility.IsValidBedFor(bed, sleeper, traveler, true, false, false, sleeper.GuestStatus);
        }

        /// <summary>
        /// How good this bed is for this pawn, in mood points, ignoring distance.
        /// Used by the bedtime search and by room negotiation.
        /// </summary>
        public static float Quality(Building_Bed bed, Pawn pawn, WilsBetterBedsSettings s, Pawn leaving = null) =>
            RoomComfort.Points(bed, pawn, s, company: true, leaving: leaving);

        // Distance does not count against a bed: a colonist takes the nicest bed they can reach, however
        // far it is. It only breaks ties, so that of two equally good beds the nearer one wins.
        private const float TieBreakPerCell = 0.0001f;

        private static float Score(Building_Bed bed, Pawn sleeper, Pawn traveler, WilsBetterBedsSettings s)
        {
            float score = Quality(bed, sleeper, s);
            score -= (bed.Position - traveler.Position).LengthHorizontal * TieBreakPerCell;
            return score;
        }
    }
}
