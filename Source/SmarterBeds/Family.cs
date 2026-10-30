using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace SmarterBeds
{
    /// <summary>
    /// Who is family, who is a couple, and how far apart their beds are. Everything here only looks
    /// at colonists who are on the same map, since that is who can actually be housed near each other.
    /// </summary>
    public static class Family
    {
        public static bool IsAdultColonist(Pawn p) =>
            p != null && p.Spawned && p.Map != null && !p.Dead && p.IsColonist && !p.IsPrisoner && !p.IsSlave
            && p.HostFaction == null && (p.DevelopmentalStage & DevelopmentalStage.Adult) != 0;

        private static bool Lives(Pawn p, Pawn r) =>
            r != null && r != p && !r.Dead && r.Spawned && r.Map == p.Map && r.IsColonist && !r.IsPrisoner && !r.IsSlave;

        /// <summary>Parents, children and siblings (half-siblings too) who are colonists on the same map.</summary>
        public static List<Pawn> CloseRelatives(Pawn p)
        {
            var result = new List<Pawn>();
            if (p?.relations == null) return result;

            void Add(Pawn r) { if (Lives(p, r) && !result.Contains(r)) result.Add(r); }

            Pawn mother = p.GetMother();
            Pawn father = p.GetFather();
            Add(mother);
            Add(father);
            foreach (var child in p.relations.Children) Add(child);
            foreach (var parent in new[] { mother, father })
            {
                if (parent?.relations == null) continue;
                foreach (var sibling in parent.relations.Children) Add(sibling);
            }
            return result;
        }

        /// <summary>Negative relations: the pawn's opinion of this relative is at or below the threshold.</summary>
        public static bool IsHostile(Pawn p, Pawn relative, SmarterBedsSettings s) =>
            p.relations != null && p.relations.OpinionOf(relative) <= s.familyHostileOpinion;

        public static Building_Bed BedOf(Pawn p) => p.ownership?.OwnedBed;

        public static float Distance(Building_Bed a, Building_Bed b) => (a.Position - b.Position).LengthHorizontal;

        /// <summary>The pawn's current love partners (spouse, fiance, lover) who are alive.</summary>
        public static List<Pawn> Lovers(Pawn p)
        {
            var result = new List<Pawn>();
            if (p?.relations == null) return result;
            // The game returns a shared list, so copy out of it straight away.
            foreach (var rel in LovePartnerRelationUtility.ExistingLovePartners(p, false))
            {
                if (rel.otherPawn != null && !rel.otherPawn.Dead) result.Add(rel.otherPawn);
            }
            return result;
        }

        /// <summary>The one partner a pawn has, or null if they have none or several.</summary>
        public static Pawn SoleLover(Pawn p)
        {
            var lovers = Lovers(p);
            return lovers.Count == 1 ? lovers[0] : null;
        }

        /// <summary>Beds of relatives the pawn gets on with.</summary>
        public static List<Building_Bed> FriendlyRelativeBeds(Pawn p, SmarterBedsSettings s)
        {
            var beds = new List<Building_Bed>();
            foreach (var r in CloseRelatives(p))
            {
                var bed = BedOf(r);
                if (bed != null && !IsHostile(p, r, s)) beds.Add(bed);
            }
            return beds;
        }

        /// <summary>
        /// A couple with family in the colony, whose own bed is further than the family radius from
        /// every one of those relatives. This is the situation the "room far from family" thought describes.
        /// </summary>
        public static bool IsFarFromFamily(Pawn p, SmarterBedsSettings s)
        {
            if (!s.familyEnabled || !IsAdultColonist(p)) return false;
            var bed = BedOf(p);
            if (bed == null || SoleLover(p) == null) return false;
            return IsFarFromAll(bed, FriendlyRelativeBeds(p, s), s);
        }

        /// <summary>True when there is at least one bed in the list and every one is out of range.</summary>
        public static bool IsFarFromAll(Building_Bed bed, List<Building_Bed> relativeBeds, SmarterBedsSettings s)
        {
            if (relativeBeds.Count == 0) return false;
            foreach (var other in relativeBeds)
            {
                if (other == bed || Distance(bed, other) <= s.familyRadius) return false;
            }
            return true;
        }

        /// <summary>A relative the pawn has negative relations with whose bed is within the family radius.</summary>
        public static Pawn NearbyDislikedRelative(Pawn p, SmarterBedsSettings s)
        {
            if (!s.familyEnabled || !IsAdultColonist(p)) return null;
            var bed = BedOf(p);
            if (bed == null) return null;
            foreach (var r in CloseRelatives(p))
            {
                var theirs = BedOf(r);
                if (theirs == null || !IsHostile(p, r, s)) continue;
                if (theirs == bed || Distance(bed, theirs) <= s.familyRadius) return r;
            }
            return null;
        }

        /// <summary>
        /// A pair who live in a room of their own: both own a bed in the same indoor room, and nobody
        /// else owns a bed in it.
        /// </summary>
        public static bool IsInCoupleRoom(Pawn p)
        {
            var lover = SoleLover(p);
            if (lover == null) return false;
            var mine = BedOf(p);
            var theirs = BedOf(lover);
            if (mine == null || theirs == null) return false;

            Room room = mine.GetRoom();
            if (room == null || room.PsychologicallyOutdoors || room != theirs.GetRoom()) return false;

            foreach (var bed in room.ContainedBeds)
            {
                foreach (var owner in bed.OwnersForReading)
                {
                    if (owner != p && owner != lover) return false;
                }
            }
            return true;
        }
    }
}
