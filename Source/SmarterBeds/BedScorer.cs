using RimWorld;
using Verse;
using Verse.AI;

namespace SmarterBeds
{
    /// <summary>
    /// Pure bed-choice logic. No Harmony here, so it can be reasoned about on its own.
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

        public static Building_Bed FindBest(Pawn sleeper, Pawn traveler)
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
                Log.Message($"[Smarter Beds] {sleeper.LabelShort} -> {(best == null ? "vanilla fallback" : best.LabelShort + " at " + best.Position)}");

            return best;
        }

        private static bool IsCandidate(Building_Bed bed, Pawn sleeper, Pawn traveler)
        {
            if (!bed.Spawned || bed.Destroyed) return false;
            if (bed.Faction != Faction.OfPlayer) return false;
            if (bed.Medical || !bed.ForColonists) return false;
            if (!RestUtility.CanUseBedEver(sleeper, bed.def)) return false;
            // v1 only claims fully unowned beds, which sidesteps shared-bed social rules.
            if (bed.OwnersForReading.Count > 0) return false;
            if (bed.IsForbidden(traveler) || bed.IsBurning()) return false;
            if (!traveler.CanReserveAndReach(bed, PathEndMode.OnCell, Danger.Some, bed.SleepingSlotsCount)) return false;
            return true;
        }

        /// <summary>
        /// How good this bed is for this pawn, ignoring distance and the overflow flag.
        /// Used by the bedtime search and by room negotiation.
        /// </summary>
        public static float Quality(Building_Bed bed, Pawn pawn, SmarterBedsSettings s)
        {
            float score = 0f;
            Room room = bed.GetRoom();

            if (room != null && !room.PsychologicallyOutdoors)
            {
                score += room.GetStat(RoomStatDefOf.Impressiveness) * s.impressivenessWeight;
                if (room.Role == RoomRoleDefOf.Bedroom) score += s.privateRoomBonus;
            }

            score += bed.GetStatValue(StatDefOf.BedRestEffectiveness) * s.restEffectivenessWeight;
            score -= Requirements.Unmet(pawn, room) * s.unmetRequirementPenalty;
            return score;
        }

        private static float Score(Building_Bed bed, Pawn sleeper, Pawn traveler, SmarterBedsSettings s)
        {
            float score = Quality(bed, sleeper, s);
            score -= (bed.Position - traveler.Position).LengthHorizontal * s.distanceWeight;

            if (bed.GetComp<CompOverflowBed>()?.isOverflow == true) score -= s.overflowPenalty;

            return score;
        }
    }
}
