using RimWorld;
using Verse;

namespace SmarterBeds
{
    /// <summary>
    /// "Room far from family": a couple, with family in the colony, whose bed is outside the family
    /// radius (35 tiles by default) of every one of those relatives. Counts as a bedroom problem, so
    /// the colonist may ask for a room closer to them.
    /// </summary>
    public class ThoughtWorker_RoomFarFromFamily : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            try
            {
                using (PerfWatch.Measure("family thoughts"))
                    return Family.IsFarFromFamily(p, SmarterBedsMod.Settings) ? ThoughtState.ActiveAtStage(0) : ThoughtState.Inactive;
            }
            catch (System.Exception e)
            {
                SafeLog.Once("room far from family thought", e);
                return ThoughtState.Inactive;
            }
        }
    }

    /// <summary>
    /// A colonist who has negative relations with a relative and sleeps within the family radius of
    /// that relative's bed. They want a room further away, and may ask for one.
    /// </summary>
    public class ThoughtWorker_RoomNearDislikedFamily : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            try
            {
                using (PerfWatch.Measure("family thoughts"))
                {
                    var relative = Family.NearbyDislikedRelative(p, SmarterBedsMod.Settings);
                    return relative != null ? ThoughtState.ActiveWithReason(relative.LabelShort) : ThoughtState.Inactive;
                }
            }
            catch (System.Exception e)
            {
                SafeLog.Once("too close to relative thought", e);
                return ThoughtState.Inactive;
            }
        }
    }

    /// <summary>A colonist the pawn dislikes (not family) sleeps within the family radius of their bed.</summary>
    public class ThoughtWorker_RoomNearDislikedNeighbor : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            try
            {
                using (PerfWatch.Measure("family thoughts"))
                {
                    var other = Family.FirstNear(p, Family.HostileNeighbors(p), SmarterBedsMod.Settings);
                    return other != null ? ThoughtState.ActiveWithReason(other.LabelShort) : ThoughtState.Inactive;
                }
            }
            catch (System.Exception e)
            {
                SafeLog.Once("too close to colonist thought", e);
                return ThoughtState.Inactive;
            }
        }
    }

    /// <summary>Someone of the pawn's ideoligion sleeps within the family radius of their bed.</summary>
    public class ThoughtWorker_RoomAmongOwnIdeo : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            try
            {
                using (PerfWatch.Measure("family thoughts"))
                    return Family.FirstNear(p, Family.SameIdeoNeighbors(p), SmarterBedsMod.Settings) != null && SmarterBedsMod.Settings.ideoNeighborBonus > 0f
                        ? ThoughtState.ActiveAtStage(0) : ThoughtState.Inactive;
            }
            catch (System.Exception e)
            {
                SafeLog.Once("fellow believers thought", e);
                return ThoughtState.Inactive;
            }
        }
    }

    /// <summary>Someone of the pawn's xenotype sleeps within the family radius of their bed.</summary>
    public class ThoughtWorker_RoomAmongSimilarGenes : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            try
            {
                using (PerfWatch.Measure("family thoughts"))
                    return Family.FirstNear(p, Family.SameGeneNeighbors(p), SmarterBedsMod.Settings) != null && SmarterBedsMod.Settings.geneNeighborBonus > 0f
                        ? ThoughtState.ActiveAtStage(0) : ThoughtState.Inactive;
            }
            catch (System.Exception e)
            {
                SafeLog.Once("people like me thought", e);
                return ThoughtState.Inactive;
            }
        }
    }
}
