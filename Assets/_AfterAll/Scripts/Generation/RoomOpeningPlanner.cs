using System.Collections.Generic;
using UnityEngine;

namespace AfterAll.Generation
{
    /// <summary>
    /// Picks which BSP partition lines get a doorway. Each wall line receives at most
    /// one opening; extra openings are spread across different walls per room.
    /// </summary>
    public static class RoomOpeningPlanner
    {
        private const float kEpsilon = 0.05f;

        public static HashSet<BoundaryKey> PlanOpenBoundaries(
            BspResult bsp, MapConfig config, Rng rng)
        {
            var open = new HashSet<BoundaryKey>();
            var boundaryList = bsp.Boundaries;

            foreach (var room in bsp.Rooms)
            {
                var candidates = GetAdjacentBoundaries(room, boundaryList);
                if (candidates.Count == 0)
                    continue;

                int target = OpeningGenerator.ResolveRoomOpeningCount(config, rng);
                target = Mathf.Clamp(target, 0, candidates.Count);

                OpeningGenerator.Shuffle(candidates, rng);

                for (int i = 0; i < target; i++)
                    open.Add(candidates[i]);
            }

            return open;
        }

        static List<BoundaryKey> GetAdjacentBoundaries(
            RoomSpec room, IReadOnlyList<BspBoundary> boundaries)
        {
            var result = new List<BoundaryKey>(4);
            var rb = room.Bounds;

            for (int i = 0; i < boundaries.Count; i++)
            {
                if (!RoomTouchesBoundary(rb, boundaries[i]))
                    continue;

                result.Add(new BoundaryKey(boundaries[i]));
            }

            return result;
        }

        static bool RoomTouchesBoundary(Rect rb, BspBoundary boundary)
        {
            if (boundary.IsVertical)
            {
                float vx = boundary.Start.x;
                float z1 = Mathf.Min(boundary.Start.y, boundary.End.y);
                float z2 = Mathf.Max(boundary.Start.y, boundary.End.y);

                if (rb.yMin >= z2 - kEpsilon || rb.yMax <= z1 + kEpsilon)
                    return false;

                return Mathf.Abs(rb.xMax - vx) < kEpsilon || Mathf.Abs(rb.xMin - vx) < kEpsilon;
            }

            float sz = boundary.Start.y;
            float x1 = Mathf.Min(boundary.Start.x, boundary.End.x);
            float x2 = Mathf.Max(boundary.Start.x, boundary.End.x);

            if (rb.xMin >= x2 - kEpsilon || rb.xMax <= x1 + kEpsilon)
                return false;

            return Mathf.Abs(rb.yMax - sz) < kEpsilon || Mathf.Abs(rb.yMin - sz) < kEpsilon;
        }

        public readonly struct BoundaryKey : System.IEquatable<BoundaryKey>
        {
            private readonly int _ax, _ay, _bx, _by;

            public BoundaryKey(BspBoundary b)
            {
                _ax = Q(b.Start.x); _ay = Q(b.Start.y);
                _bx = Q(b.End.x);   _by = Q(b.End.y);
            }

            static int Q(float v) => Mathf.RoundToInt(v * 1000f);

            public bool Equals(BoundaryKey other) =>
                _ax == other._ax && _ay == other._ay && _bx == other._bx && _by == other._by;

            public override bool Equals(object obj) => obj is BoundaryKey k && Equals(k);

            public override int GetHashCode() =>
                System.HashCode.Combine(_ax, _ay, _bx, _by);
        }
    }
}
