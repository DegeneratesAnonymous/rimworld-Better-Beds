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
            // Other adult colonists who are not family and the pawn dislikes, and those the pawn does not
            // dislike who share their ideoligion or are of their xenotype (family included).
            public readonly List<Pawn> hostileOthers = new List<Pawn>();
            public readonly List<Pawn> sameIdeo = new List<Pawn>();
            public readonly List<Pawn> sameGenes = new List<Pawn>();
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

                BuildNeighbors(p, snap);
            }
            catch (System.Exception e)
            {
                // Relations are the game's, and a mod can leave odd data in them; none is the safe answer.
                SafeLog.Once("reading family relations", e);
                snap.relatives.Clear();
                snap.opinion.Clear();
                snap.lovers.Clear();
                snap.hostileOthers.Clear();
                snap.sameIdeo.Clear();
                snap.sameGenes.Clear();
            }
            return snap;
        }

        /// <summary>Who in the colony the pawn dislikes, shares their ideoligion with, or is the same kind of person as.</summary>
        private static void BuildNeighbors(Pawn p, Snapshot snap)
        {
            var s = SmarterBedsMod.Settings;
            if (!s.neighborsEnabled || !IsAdultColonist(p)) return;

            var ideo = ModsConfig.IdeologyActive ? p.Ideo : null;
            string genes = GeneKey(p);
            foreach (var other in new List<Pawn>(p.Map.mapPawns.FreeColonistsSpawned))
            {
                if (other == p || !IsAdultColonist(other)) continue;
                bool relative = snap.opinion.TryGetValue(other, out int known);
                int opinion = relative ? known : p.relations.OpinionOf(other);
                if (opinion <= s.familyHostileOpinion)
                {
                    if (!relative) snap.hostileOthers.Add(other);
                    continue; // nobody they dislike counts as "someone like me"
                }
                if (ideo != null && other.Ideo == ideo) snap.sameIdeo.Add(other);
                if (genes != null && GeneKey(other) == genes) snap.sameGenes.Add(other);
            }
        }

        /// <summary>
        /// A name for the kind of person this is, or null when there is nothing to match on. Baseliners
        /// are left out on purpose: in a colony of baseliners everyone would match everyone, which tells
        /// the pawn nothing about where to sleep. A pawn with their own one-off set of genes matches nobody.
        /// </summary>
        private static string GeneKey(Pawn p)
        {
            try
            {
                var genes = p.genes;
                if (!ModsConfig.BiotechActive || genes == null || genes.UniqueXenotype) return null;
                string key = genes.Xenotype?.defName ?? genes.XenotypeLabel;
                return string.IsNullOrEmpty(key) || key == "Baseliner" ? null : key;
            }
            catch { return null; }
        }

        public static List<Pawn> HostileNeighbors(Pawn p) => p?.relations == null ? NoPawns : Get(p).hostileOthers;
        public static List<Pawn> SameIdeoNeighbors(Pawn p) => p?.relations == null ? NoPawns : Get(p).sameIdeo;
        public static List<Pawn> SameGeneNeighbors(Pawn p) => p?.relations == null ? NoPawns : Get(p).sameGenes;

        /// <summary>How many in the list (stopping at <paramref name="cap"/>) sleep within the family radius of this bed.</summary>
        public static int CountNear(Pawn p, List<Pawn> others, Building_Bed bed, SmarterBedsSettings s, Pawn leaving, int cap)
        {
            int count = 0;
            foreach (var r in others)
            {
                if (r == leaving || !Lives(p, r)) continue;
                var theirs = BedOf(r);
                if (theirs == null) continue;
                if (theirs == bed || Distance(bed, theirs) <= s.familyRadius)
                {
                    if (++count >= cap) break;
                }
            }
            return count;
        }

        /// <summary>The first one in the list who sleeps within the radius of the pawn's own bed.</summary>
        public static Pawn FirstNear(Pawn p, List<Pawn> others, SmarterBedsSettings s)
        {
            if (!s.neighborsEnabled || !IsAdultColonist(p)) return null;
            var bed = BedOf(p);
            if (bed == null) return null;
            foreach (var r in others)
            {
                if (!Lives(p, r)) continue;
                var theirs = BedOf(r);
                if (theirs != null && (theirs == bed || Distance(bed, theirs) <= s.familyRadius)) return r;
            }
            return null;
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
