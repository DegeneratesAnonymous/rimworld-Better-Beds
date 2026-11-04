using System.Collections.Generic;
using RimWorld;
using Verse;

namespace WilsBetterBeds
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
        public static void ClearCache() { cache.Clear(); prisonerBeds.Clear(); }

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
            var s = WilsBetterBedsMod.Settings;
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

        // Beds with a prisoner assigned, per map, remembered for a few seconds: asked for every bed scored.
        private class PrisonerBedList { public int tick; public readonly List<Building_Bed> beds = new List<Building_Bed>(); }
        private static readonly Dictionary<int, PrisonerBedList> prisonerBeds = new Dictionary<int, PrisonerBedList>();
        private const int PrisonerBedTicks = 250;

        private static List<Building_Bed> OccupiedPrisonerBeds(Map map)
        {
            int now = Find.TickManager.TicksGame;
            if (!prisonerBeds.TryGetValue(map.uniqueID, out var list))
                prisonerBeds[map.uniqueID] = list = new PrisonerBedList { tick = -1000000 };
            if (now >= list.tick && now - list.tick < PrisonerBedTicks) return list.beds;

            list.tick = now;
            list.beds.Clear();
            foreach (var bed in map.listerBuildings.AllBuildingsColonistOfClass<Building_Bed>())
            {
                if (bed.Spawned && bed.ForPrisoners && bed.OwnersForReading.Count > 0) list.beds.Add(bed);
            }
            return list.beds;
        }

        /// <summary>Is a bed with a prisoner assigned to it within the family distance of this bed?</summary>
        public static bool PrisonersNear(Building_Bed bed, WilsBetterBedsSettings s)
        {
            if (bed == null || bed.Map == null) return false;
            foreach (var prisonBed in OccupiedPrisonerBeds(bed.Map))
            {
                if (prisonBed != bed && Distance(bed, prisonBed) <= s.familyRadius) return true;
            }
            return false;
        }

        public static List<Pawn> HostileNeighbors(Pawn p) => p?.relations == null ? NoPawns : Get(p).hostileOthers;
        public static List<Pawn> SameIdeoNeighbors(Pawn p) => p?.relations == null ? NoPawns : Get(p).sameIdeo;
        public static List<Pawn> SameGeneNeighbors(Pawn p) => p?.relations == null ? NoPawns : Get(p).sameGenes;

        /// <summary>How many in the list (stopping at <paramref name="cap"/>) sleep within the family radius of this bed.</summary>
        public static int CountNear(Pawn p, List<Pawn> others, Building_Bed bed, WilsBetterBedsSettings s, Pawn leaving, int cap)
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
        public static Pawn FirstNear(Pawn p, List<Pawn> others, WilsBetterBedsSettings s)
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

        private static bool IsFreeColonistKind(Pawn p) =>
            p != null && !p.Dead && p.IsColonist && !p.IsPrisoner && !p.IsSlave && p.HostFaction == null;

        /// <summary>A colonist baby or newborn. A baby being carried is not spawned, so that is not required.</summary>
        public static bool IsBaby(Pawn p) =>
            IsFreeColonistKind(p) && (p.DevelopmentalStage & (DevelopmentalStage.Newborn | DevelopmentalStage.Baby)) != 0;

        /// <summary>A colonist child: past the crib, not yet an adult (or a teenager).</summary>
        public static bool IsChild(Pawn p) =>
            IsFreeColonistKind(p) && p.Spawned && p.Map != null && p.DevelopmentalStage == DevelopmentalStage.Child;

        /// <summary>A bed for babies (a crib, or whatever a mod adds that is for human babies).</summary>
        public static bool IsCrib(Building_Bed bed) => bed != null && (bed.ForHumanBabies || bed.def.building.bed_crib);

        /// <summary>
        /// Where a baby's family sleeps: the mother's bed (or, with no bed, where she is), else the father's. False when neither
        /// is a colonist on this map.
        /// </summary>
        public static bool NurseryAnchor(Pawn baby, Map map, out IntVec3 position)
        {
            position = IntVec3.Invalid;
            if (baby == null || map == null) return false;
            foreach (var parent in new[] { baby.GetMother(), baby.GetFather() })
            {
                if (parent == null || parent == baby || parent.Dead || !parent.Spawned || parent.Map != map || !parent.IsColonist || parent.IsPrisoner || parent.IsSlave) continue;
                var bed = BedOf(parent);
                position = bed != null && bed.Spawned ? bed.Position : parent.Position;
                return true;
            }
            return false;
        }

        /// <summary>The child's mother and father who are colonists on the child's map.</summary>
        public static List<Pawn> Parents(Pawn child)
        {
            var list = new List<Pawn>(2);
            if (child == null) return list;
            foreach (var parent in new[] { child.GetMother(), child.GetFather() })
                if (Lives(child, parent) && !list.Contains(parent)) list.Add(parent);
            return list;
        }

        /// <summary>
        /// How near this bed is to a parent's bed, from 1 (the same room) down to 0 (at the family radius or further),
        /// taking the nearest of the child's parents.
        /// </summary>
        public static float ParentCloseness(Building_Bed bed, Pawn child, WilsBetterBedsSettings s)
        {
            if (bed == null || s.familyRadius <= 0f) return 0f;
            float best = 0f;
            Room room = bed.GetRoom();
            bool indoors = room != null && !room.PsychologicallyOutdoors;
            foreach (var parent in Parents(child))
            {
                var theirs = BedOf(parent);
                if (theirs == null || !theirs.Spawned) continue;
                float closeness;
                if (theirs == bed || (indoors && theirs.GetRoom() == room)) closeness = 1f;
                else closeness = UnityEngine.Mathf.Clamp01(1f - WalkOrLineDistance(bed.Map, theirs.Position, bed.Position, s) / s.familyRadius);
                if (closeness > best) best = closeness;
            }
            return best;
        }

        public static bool IsAdultColonist(Pawn p) =>
            p != null && p.Spawned && p.Map != null && !p.Dead && p.IsColonist && !p.IsPrisoner && !p.IsSlave
            && p.HostFaction == null && (p.DevelopmentalStage & DevelopmentalStage.Adult) != 0;

        private static bool Lives(Pawn p, Pawn r) =>
            r != null && r != p && !r.Dead && r.Spawned && r.Map == p.Map && r.IsColonist && !r.IsPrisoner && !r.IsSlave;

        /// <summary>Parents, children and siblings (half-siblings too) who are colonists on the same map. Do not modify the list.</summary>
        public static List<Pawn> CloseRelatives(Pawn p) => p?.relations == null ? NoPawns : Get(p).relatives;

        /// <summary>Negative relations: the pawn's opinion of this relative is at or below the threshold.</summary>
        public static bool IsHostile(Pawn p, Pawn relative, WilsBetterBedsSettings s)
        {
            if (p.relations == null) return false;
            int opinion = Get(p).opinion.TryGetValue(relative, out int known) ? known : p.relations.OpinionOf(relative);
            return opinion <= s.familyHostileOpinion;
        }

        public static Building_Bed BedOf(Pawn p) => p.ownership?.OwnedBed;

        public static float Distance(Building_Bed a, Building_Bed b) => (a.Position - b.Position).LengthHorizontal;

        /// <summary>
        /// How far a child is from a parent's bed for the purpose of sleeping near them: the shortest way to walk, round walls and through
        /// doors, when that is switched on. A bed with no way to it within reach counts as too far to matter (the old straight-line distance,
        /// plus the family distance), so it is never preferred over one that can be reached.
        /// </summary>
        public static float WalkOrLineDistance(Map map, IntVec3 from, IntVec3 to, WilsBetterBedsSettings s)
        {
            float line = (from - to).LengthHorizontal;
            if (!s.walkingDistanceEnabled || map == null) return line;
            float walked = WalkDistance.To(map, from, to, System.Math.Max(s.familyRadius * 1.5f, line + 10f));
            return float.IsInfinity(walked) ? line + s.familyRadius * 2f : walked;
        }

        /// <summary>The pawn's current love partners (spouse, fiance, lover) who are alive. Do not modify the list.</summary>
        public static List<Pawn> Lovers(Pawn p) => p?.relations == null ? NoPawns : Get(p).lovers;

        /// <summary>
        /// True when the pawn has exactly one living love partner. Asks the game directly, without building a
        /// snapshot, so it is cheap enough to ask about every colonist.
        /// </summary>
        public static bool HasSingleLover(Pawn p)
        {
            if (p?.relations == null) return false;
            int count = 0;
            foreach (var rel in LovePartnerRelationUtility.ExistingLovePartners(p, false))
            {
                if (rel.otherPawn != null && !rel.otherPawn.Dead && ++count > 1) return false;
            }
            return count == 1;
        }

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
        public static bool IsFarFromFamily(Pawn p, WilsBetterBedsSettings s)
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
        public static Pawn NearbyDislikedRelative(Pawn p, WilsBetterBedsSettings s)
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
