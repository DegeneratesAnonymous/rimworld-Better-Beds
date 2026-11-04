using System.Collections.Generic;
using RimWorld;
using Verse;

namespace WilsBetterBeds
{
    /// <summary>
    /// How far someone really has to walk between two places: round walls and through doors, not through them. A bed on the other side of
    /// a wall is close as the crow flies but far for a child who has to go all the way round. The distance is found once from a parent's
    /// bed outward and kept for a while, so scoring every bed in the colony costs one search per parent, not one per bed.
    /// Steps along a straight line cost 1 and diagonals 1.4, so open ground measures the same as the old straight-line distance.
    /// </summary>
    public static class WalkDistance
    {
        private const int Straight = 10;
        private const int Diagonal = 14;
        private const int FieldLifetimeTicks = 1500;
        private const int MaxFields = 8;
        private const int Unreached = int.MaxValue;

        private sealed class Field
        {
            public Map Map;
            public int Origin;
            public int Tick;
            public int Limit;
            public int[] Cost;
        }

        private static readonly List<Field> Fields = new List<Field>();

        public static void ClearCache() => Fields.Clear();

        /// <summary>The walking distance in cells from <paramref name="origin"/> to <paramref name="target"/>, or infinity when there is no way within <paramref name="limitCells"/>.</summary>
        public static float To(Map map, IntVec3 origin, IntVec3 target, float limitCells)
        {
            if (map == null || !origin.InBounds(map) || !target.InBounds(map)) return float.PositiveInfinity;
            int limit = (int)(limitCells * Straight) + 1;
            var field = FieldFor(map, origin, limit);
            int cost = field.Cost[CellIndex(map, target)];
            return cost == Unreached ? float.PositiveInfinity : cost / (float)Straight;
        }

        private static int CellIndex(Map map, IntVec3 c) => c.z * map.Size.x + c.x;

        private static Field FieldFor(Map map, IntVec3 origin, int limit)
        {
            int now = Find.TickManager.TicksGame;
            int originIndex = CellIndex(map, origin);
            for (int i = 0; i < Fields.Count; i++)
            {
                var f = Fields[i];
                if (f.Map != map || f.Origin != originIndex || f.Limit < limit) continue;
                if (now >= f.Tick && now - f.Tick < FieldLifetimeTicks) return f;
                Fields.RemoveAt(i);
                break;
            }
            Field built;
            using (PerfWatch.Measure("walking distances")) built = Build(map, origin, limit, now);
            if (Fields.Count >= MaxFields) Fields.RemoveAt(0);
            Fields.Add(built);
            return built;
        }

        private static Field Build(Map map, IntVec3 origin, int limit, int now)
        {
            int width = map.Size.x, height = map.Size.z;
            var cost = new int[width * height];
            for (int i = 0; i < cost.Length; i++) cost[i] = Unreached;

            // Cheapest-first search; the queue holds (cost, cell) packed into one number.
            var heap = new List<long>();
            int start = CellIndex(map, origin);
            cost[start] = 0;
            Push(heap, 0L << 32 | (uint)start);
            while (heap.Count > 0)
            {
                long top = Pop(heap);
                int here = (int)(top & 0xFFFFFFFF);
                int hereCost = (int)(top >> 32);
                if (hereCost > cost[here]) continue;           // an old entry: this cell was reached more cheaply since
                int x = here % width, z = here / width;
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dz == 0) continue;
                        int nx = x + dx, nz = z + dz;
                        if (nx < 0 || nz < 0 || nx >= width || nz >= height) continue;
                        var cell = new IntVec3(nx, 0, nz);
                        if (!cell.Walkable(map)) continue;
                        bool diagonal = dx != 0 && dz != 0;
                        // Nobody cuts the corner of a wall: a diagonal step needs both of the cells beside it to be open.
                        if (diagonal && (!new IntVec3(x + dx, 0, z).Walkable(map) || !new IntVec3(x, 0, z + dz).Walkable(map))) continue;
                        int next = hereCost + (diagonal ? Diagonal : Straight);
                        if (next > limit) continue;
                        int index = nz * width + nx;
                        if (next >= cost[index]) continue;
                        cost[index] = next;
                        Push(heap, (long)next << 32 | (uint)index);
                    }
            }
            return new Field { Map = map, Origin = start, Tick = now, Limit = limit, Cost = cost };
        }

        // A small binary min-heap, so this does not depend on a collection newer than the oldest game version supports.
        private static void Push(List<long> heap, long value)
        {
            heap.Add(value);
            int i = heap.Count - 1;
            while (i > 0)
            {
                int parent = (i - 1) / 2;
                if (heap[parent] <= heap[i]) break;
                long swap = heap[parent]; heap[parent] = heap[i]; heap[i] = swap;
                i = parent;
            }
        }

        private static long Pop(List<long> heap)
        {
            long top = heap[0];
            int last = heap.Count - 1;
            heap[0] = heap[last];
            heap.RemoveAt(last);
            int i = 0;
            while (true)
            {
                int left = 2 * i + 1, right = left + 1, smallest = i;
                if (left < heap.Count && heap[left] < heap[smallest]) smallest = left;
                if (right < heap.Count && heap[right] < heap[smallest]) smallest = right;
                if (smallest == i) break;
                long swap = heap[smallest]; heap[smallest] = heap[i]; heap[i] = swap;
                i = smallest;
            }
            return top;
        }
    }
}
