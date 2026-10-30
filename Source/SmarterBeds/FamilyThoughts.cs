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
                var relative = Family.NearbyDislikedRelative(p, SmarterBedsMod.Settings);
                return relative != null ? ThoughtState.ActiveWithReason(relative.LabelShort) : ThoughtState.Inactive;
            }
            catch (System.Exception e)
            {
                SafeLog.Once("too close to relative thought", e);
                return ThoughtState.Inactive;
            }
        }
    }
}
