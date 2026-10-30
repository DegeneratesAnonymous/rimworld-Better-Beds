using System.Collections.Generic;
using RimWorld;
using Verse;

namespace SmarterBeds
{
    /// <summary>
    /// Who is family, who is a couple, and how far apart their beds are. Everything here only looks
    /// at colonists who are on the same map, since that is who can actually be housed near each other.
    ///
    /// Working out relatives and opinions walks the relations tables, and the bed score and the two
    /// family thoughts ask for it constantly, so each pawn's answer is remembered for a short while
    /// (a snapshot). Relationships change over days, not seconds, so ten seconds of staleness is
    /// invisible; where it would matter (the hourly couple check) a fresh snapshot is forced.
    /// Beds are always read live, never from the snapshot.
    /// </summary>
    public static class Family
    {
        private class Snapshot
        {
            public int tick;
            public readonly List<Pawn> relatives = new List<Pawn>();
            public readonly Dictionary<Pawn, int> opinion = new Dictionary<Pawn, int>();
            public readonly List<Pawn> lovers = new List<Pawn>();
        }

        private const int SnapshotTicks = 600;
        private const int MaxCached = 500;
        private static readonly Dictionary<int, Snapshot> cache = new Dictionary<int, Snapshot>();
        private static readonly List<Pawn> NoPawns = new List<Pawn>();

        /// <summary>Forget everything. Pawn ids are reused between saves, so this runs whenever a game loads.</summary>
        public static void ClearCache() => cache.Clear();

        /// <summary>Make the next question about this pawn read the game's data again.</summary>
        public static void Refresh(Pawn p)
        {
            if (p != null) cache.Remove(p.thingIDNumber);
        }

        private static Snapshot Get(Pawn p)
        {
            int now = Find.TickManager.TicksGame;
            if (cache.TryGetValue(p.thingIDNumber, out var snap) && now >= snap.tick && now - snap.tick < SnapshotTicks) return snap;

            snap = Build(p, now);
            if (cache.Count > MaxCached) cache.Clear();
            cache[p.thingIDNumber] = snap;
            return snap;
        }

        private static Snapshot Build(Pawn p, int now)
        {
            var snap = new Snapshot { tick = now };
            if (p?.relations == null) return snap;

            void AddRelative(Pawn r)
            {
                if (!Lives(p, r) || snap.relatives.Contains(r)) return;
                snap.relatives.Add(r);
                snap.opinion[r] = p.relations.OpinionOf(r);
            }

            try
            {
                Pawn mother = p.GetMother();
                Pawn father = p.GetFather();
                AddRelative(mother);
                AddRelative(father);
                foreach (var child in p.relations.Children) AddRelative(child);
                foreach (var parent in new[] { mother, father })
                {
                    if (parent?.relations == null) continue;
                    foreach (var sibling in parent.relations.Children) AddRelative(sibling);
                }

                // The game returns a shared list, so copy out of it straight away.
                foreach (var rel in LovePartnerRelationUtility.ExistingLovePartners(p, false))
                {
                    if (rel.otherPawn != null && !rel.otherPawn.Dead) snap.lovers.Add(rel.otherPawn);
                }
            }
            catch (System.Exception e)
            {
                // Relations are the game's, and a mod can leave odd data in them; none is the safe answer.
                SafeLog.Once("reading family relations", e);
                snap.relatives.Clear();
                snap.opinion.Clear();
                snap.lovers.Clear();
            }
            return snap;
        }

        public static bool IsAdultColonist(Pawn p) =>
            p != null && p.Spawned && p.Map != null && !p.Dead && p.IsColonist && !p.IsPrisoner && !p.IsSlave
            && p.HostFaction == null && (p.DevelopmentalStage & DevelopmentalStage.Adult) != 0;

        private static bool Lives(Pawn p, Pawn r) =>
            r != null && r != p && !r.Dead && r.Spawned && r.Map == p.Map && r.IsColonist && !r.IsPrisoner && !r.IsSlave;

        /// <summary>Parents, children and siblings (half-siblings too) who are colonists on the same map. Do not modify the list.</summary>
        public static List<Pawn> CloseRelatives(Pawn p) => p?.relations == null ? NoPawns : Get(p).relatives;

        /// <summary>Negative relations: the pawn's opinion of this relative is at or below the threshold.</summary>
        public static bool IsHostile(Pawn p, Pawn relative, SmarterBedsSettings s)
        {
            if (p.relations == null) return false;
            int opinion = Get(p).opinion.TryGetValue(relative, out int known) ? known : p.relations.OpinionOf(relative);
            return opinion <= s.familyHostileOpinion;
        }

        public static Building_Bed BedOf(Pawn p) => p.ownership?.OwnedBed;

        public static float Distance(Building_Bed a, Building_Bed b) => (a.Position - b.Position).LengthHorizontal;

        /// <summary>The pawn's current love partners (spouse, fiance, lover) who are alive. Do not modify the list.</summary>
        public static List<Pawn> Lovers(Pawn p) => p?.relations == null ? NoPawns : Get(p).lovers;

        /// <summary>The one partner a pawn has, or null if they have none or several.</summary>
        public static Pawn SoleLover(Pawn p)
        {
            var lovers = Lovers(p);
            return lovers.Count == 1 ? lovers[0] : null;
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

            bool anyFriendly = false;
            foreach (var r in CloseRelatives(p))
            {
                var theirs = BedOf(r);
                if (theirs == null || IsHostile(p, r, s)) continue;
                anyFriendly = true;
                if (theirs == bed || Distance(bed, theirs) <= s.familyRadius) return false;
            }
            return anyFriendly;
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
