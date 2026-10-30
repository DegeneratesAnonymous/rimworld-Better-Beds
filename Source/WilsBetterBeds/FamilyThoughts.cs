using RimWorld;
using Verse;

namespace WilsBetterBeds
{
    /// <summary>
    /// A situational thought whose mood is the matching setting, so the slider changes what the
    /// colonist actually feels and not only how beds are ranked.
    /// </summary>
    public class Thought_SettingMood : Thought_Situational
    {
        /// <summary>
        /// The game fills {0} in a description with the colonist's own name, not the reason, so the
        /// reason (the neighbour or relative) is put in here.
        /// </summary>
        public override string Description
        {
            get
            {
                string text = CurStage?.description ?? def.description;
                if (string.IsNullOrEmpty(text) || !text.Contains("{0}")) return text;
                return text.Replace("{0}", string.IsNullOrEmpty(reason) ? "someone" : reason);
            }
        }

        public override float MoodOffset()
        {
            var s = WilsBetterBedsMod.Settings;
            switch (def.defName)
            {
                case "WilsBetterBeds_RoomFarFromFamily": return -s.familyFarPenalty;
                case "WilsBetterBeds_RoomNearDislikedFamily": return -s.familyHostilePenalty;
                case "WilsBetterBeds_RoomNearDislikedNeighbor": return -s.neighborHostilePenalty;
                case "WilsBetterBeds_RoomNearPrisoners": return -s.prisonerNeighborPenalty;
                case "WilsBetterBeds_RoomAmongOwnIdeo": return s.ideoNeighborBonus;
                case "WilsBetterBeds_RoomAmongSimilarGenes": return s.geneNeighborBonus;
                default: return base.MoodOffset();
            }
        }
    }

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
                    return Family.IsFarFromFamily(p, WilsBetterBedsMod.Settings) ? ThoughtState.ActiveAtStage(0) : ThoughtState.Inactive;
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
                    var relative = Family.NearbyDislikedRelative(p, WilsBetterBedsMod.Settings);
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
                    var other = Family.FirstNear(p, Family.HostileNeighbors(p), WilsBetterBedsMod.Settings);
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

    /// <summary>A bed with a prisoner in it is within the family radius of the colonist's bed.</summary>
    public class ThoughtWorker_RoomNearPrisoners : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            try
            {
                using (PerfWatch.Measure("family thoughts"))
                {
                    var s = WilsBetterBedsMod.Settings;
                    if (!s.neighborsEnabled || s.prisonerNeighborPenalty <= 0f || !Family.IsAdultColonist(p)) return ThoughtState.Inactive;
                    var bed = Family.BedOf(p);
                    return bed != null && Family.PrisonersNear(bed, s) ? ThoughtState.ActiveAtStage(0) : ThoughtState.Inactive;
                }
            }
            catch (System.Exception e)
            {
                SafeLog.Once("sleeping near prisoners thought", e);
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
                    return Family.FirstNear(p, Family.SameIdeoNeighbors(p), WilsBetterBedsMod.Settings) != null && WilsBetterBedsMod.Settings.ideoNeighborBonus > 0f
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
                    return Family.FirstNear(p, Family.SameGeneNeighbors(p), WilsBetterBedsMod.Settings) != null && WilsBetterBedsMod.Settings.geneNeighborBonus > 0f
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
