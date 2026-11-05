using RimWorld;
using Verse;
using Verse.AI;

namespace WilsBetterBeds
{
    /// <summary>
    /// Keeps the young near their parents.
    /// - A baby in a crib that is further from its mother's bed than a free crib is moves to the nearer crib.
    /// - A child moves to a free bed nearer a parent's when that is clearly better, using the child bonus (see RoomComfort.ChildParentBonus),
    ///   which is strong while they are small and falls to the ordinary family bonus as they grow up.
    /// A bed whose owner the player set is left alone. Looked at once an hour, a few at a time.
    /// </summary>
    public static class ChildHousing
    {
        private const float CribGainCells = 4f;      // a baby only moves when the other crib is clearly nearer

        public static bool Step(Pawn kid, GameComponent_BedNegotiation comp)
        {
            var s = WilsBetterBedsMod.Settings;
            try
            {
                bool baby = Family.IsBaby(kid), child = Family.IsChild(kid);
                if (!(baby && s.cribNearMotherEnabled) && !(child && s.childrenEnabled)) return false;
                if (!kid.Spawned || kid.Map == null || kid.ownership == null) return false;
                var current = kid.ownership.OwnedBed;
                if (current == null || current.Medical || !current.Spawned || InnBedCompat.IsInn(current)) return false;
                if (BedLocks.IsLocked(current, kid)) return false;
                if (kid.InBed() || kid.Downed || kid.Drafted || HealthAIUtility.ShouldSeekMedicalRest(kid)) return false;

                Family.Refresh(kid);
                Building_Bed best;
                using (PerfWatch.Measure("children's beds")) best = baby ? BestCrib(kid, current) : BestForChild(kid, current, s);
                if (best == null)
                {
                    // Nothing free is better. A bed someone already has can still be the one nearest the parents: a parent asks for it.
                    if (child && s.negotiationEnabled && comp != null) return comp.TryNegotiateChildBed(kid, current);
                    return false;
                }

                kid.ownership.UnclaimBed();
                if (!kid.ownership.ClaimBedIfNonMedical(best))
                {
                    kid.ownership.ClaimBedIfNonMedical(current);          // exactly as they were
                    return false;
                }

                if (s.verboseLogging)
                    Log.Message($"[Wil's Better Beds] {kid.LabelShort} moved nearer their {(baby ? "mother" : "parents")}: bed at {best.Position}");
                if (s.negotiationMessages)
                    BriefMessage.Show((baby ? "WilsBetterBeds_BabyMoved" : "WilsBetterBeds_ChildMoved").Translate(kid.LabelShort), new LookTargets(kid), s.messageSeconds);
                return true;
            }
            catch (System.Exception e)
            {
                SafeLog.Once("keeping children near their parents", e);
                return false;
            }
        }

        private static Building_Bed BestCrib(Pawn baby, Building_Bed current)
        {
            if (!Family.NurseryAnchor(baby, baby.Map, out IntVec3 anchor)) return null;
            var s = WilsBetterBedsMod.Settings;
            float now = Family.WalkOrLineDistance(baby.Map, anchor, current.Position, s);
            Building_Bed best = null;
            float bestDistance = now - CribGainCells;
            foreach (var bed in baby.Map.listerBuildings.AllBuildingsColonistOfClass<Building_Bed>())
            {
                if (bed == current || !Family.IsCrib(bed) || !BedScorer.IsCandidate(bed, baby, baby)) continue;
                if ((bed.Position - anchor).LengthHorizontal >= bestDistance + CribGainCells) continue;       // cheap test first: further in a straight line cannot be nearer to walk to
                float distance = Family.WalkOrLineDistance(baby.Map, anchor, bed.Position, s);
                if (distance >= bestDistance) continue;
                if (!baby.CanReserveAndReach(bed, PathEndMode.OnCell, Danger.Some, bed.SleepingSlotsCount)) continue;
                bestDistance = distance;
                best = bed;
            }
            return best;
        }

        private static Building_Bed BestForChild(Pawn child, Building_Bed current, WilsBetterBedsSettings s)
        {
            if (Family.Parents(child).Count == 0) return null;
            float minGain = UnityEngine.Mathf.Max(1.5f, 0.25f * RoomComfort.ChildStrength(child, s));
            float currentQuality = BedScorer.Quality(current, child, s);
            float currentBonus = RoomComfort.ChildParentBonus(current, child, s);

            Building_Bed best = null;
            float bestGain = minGain;
            foreach (var bed in child.Map.listerBuildings.AllBuildingsColonistOfClass<Building_Bed>())
            {
                if (bed == current || !BedScorer.IsCandidate(bed, child, child)) continue;
                // The move has to be about being nearer a parent, not just a nicer room.
                if (RoomComfort.ChildParentBonus(bed, child, s) - currentBonus < minGain * 0.5f) continue;
                float gain = BedScorer.Quality(bed, child, s) - currentQuality;
                if (gain < bestGain) continue;
                if (!child.CanReserveAndReach(bed, PathEndMode.OnCell, Danger.Some, bed.SleepingSlotsCount)) continue;
                bestGain = gain;
                best = bed;
            }
            return best;
        }
    }
}
