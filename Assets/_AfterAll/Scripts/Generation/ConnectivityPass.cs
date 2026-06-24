using System.Collections.Generic;
using UnityEngine;

namespace AfterAll.Generation
{
    /// <summary>
    /// Guarantees every room in a chunk is reachable from room[0].
    ///
    /// Works with per-room wall segments: when forcing an opening on a BSP boundary,
    /// all collinear wall segments on that boundary are patched together.
    /// </summary>
    public static class ConnectivityPass
    {
        private const float kEpsilon = 0.05f;

        public static ChunkSpec Apply(BspResult bsp, ChunkSpec spec, float minOpeningWidth)
        {
            if (bsp.Rooms.Count <= 1) return spec;

            var rooms     = bsp.Rooms;
            var walls     = new List<WallSpec>(spec.Walls);
            bool anyPatch = false;

            for (int iteration = 0; iteration < rooms.Count; iteration++)
            {
                bool[] reachable = BfsReachable(rooms, walls, bsp.Boundaries);

                int sealedRoom = -1;
                for (int ri = 0; ri < rooms.Count; ri++)
                {
                    if (!reachable[ri]) { sealedRoom = ri; break; }
                }

                if (sealedRoom < 0) break;

                int wi = FindAdjacentInteriorWall(rooms, walls, bsp.Boundaries, sealedRoom);
                if (wi < 0)
                {
                    Debug.LogWarning($"[ConnectivityPass] Sealed room {sealedRoom} has no adjacent " +
                                     "BSP wall — chunk geometry may be unreachable.");
                    break;
                }

                var wallToPatch = walls[wi];
                var forced = FindForcedOpening(wallToPatch.Boundary.Length,
                                               wallToPatch.Openings,
                                               minOpeningWidth);
                if (!forced.HasValue) break;

                // Express the opening in world axis coords so every collinear segment
                // can convert it to its own local offset.
                float worldOpenStart = WallAxisStart(wallToPatch) + forced.Value.Offset;
                float worldOpenWidth = forced.Value.Width;

                var indices = WallLayout.FindWallIndicesOnSameBspBoundary(
                    wallToPatch, walls, bsp.Boundaries);

                foreach (int idx in indices)
                {
                    var w = walls[idx];
                    float localOffset = worldOpenStart - WallAxisStart(w);
                    float localEnd    = localOffset + worldOpenWidth;
                    localOffset = Mathf.Max(0f, localOffset);
                    localEnd    = Mathf.Min(w.Boundary.Length, localEnd);

                    if (localEnd - localOffset < minOpeningWidth * 0.5f) continue;

                    var patch = new OpeningSpec(localOffset, localEnd - localOffset);
                    var extraList   = new List<OpeningSpec> { patch };
                    var newOpenings = WallLayout.MergeOpenings(
                        w.Openings, extraList, w.Boundary.Length);
                    walls[idx] = new WallSpec(w.Boundary, newOpenings, w.RoomId);
                }

                anyPatch = true;

                Debug.Log($"[ConnectivityPass] Sealed room {sealedRoom}: forced opening " +
                          $"{forced.Value} on {indices.Count} wall segment(s).");
            }

            return anyPatch
                ? new ChunkSpec(spec.ChunkBounds, spec.WallHeight, spec.SlabThickness, walls, spec.Rooms)
                : spec;
        }

        // ──────────────────────────────────────────────────────────────────────────
        //  BFS reachability
        // ──────────────────────────────────────────────────────────────────────────

        private static bool[] BfsReachable(
            IReadOnlyList<RoomSpec> rooms,
            List<WallSpec> walls,
            IReadOnlyList<BspBoundary> bspBoundaries)
        {
            int n   = rooms.Count;
            var adj = new List<int>[n];
            for (int i = 0; i < n; i++) adj[i] = new List<int>();

            for (int wi = 0; wi < walls.Count; wi++)
            {
                var wall = walls[wi];
                if (wall.Openings.Count == 0) continue;
                if (!WallLayout.IsInteriorBspWall(wall, bspBoundaries)) continue;

                GetAdjacentRooms(rooms, wall.Boundary, out var sideA, out var sideB);

                foreach (int a in sideA)
                    foreach (int b in sideB)
                    {
                        if (!adj[a].Contains(b)) adj[a].Add(b);
                        if (!adj[b].Contains(a)) adj[b].Add(a);
                    }
            }

            var visited = new bool[n];
            var queue   = new Queue<int>();
            visited[0] = true;
            queue.Enqueue(0);

            while (queue.Count > 0)
            {
                int cur = queue.Dequeue();
                foreach (int next in adj[cur])
                {
                    if (!visited[next])
                    {
                        visited[next] = true;
                        queue.Enqueue(next);
                    }
                }
            }

            return visited;
        }

        // ──────────────────────────────────────────────────────────────────────────
        //  Room ↔ boundary geometry helpers
        // ──────────────────────────────────────────────────────────────────────────

        private static void GetAdjacentRooms(
            IReadOnlyList<RoomSpec> rooms, BspBoundary boundary,
            out List<int> sideA, out List<int> sideB)
        {
            sideA = new List<int>();
            sideB = new List<int>();

            if (boundary.IsVertical)
            {
                float vx = boundary.Start.x;
                float z1 = Mathf.Min(boundary.Start.y, boundary.End.y);
                float z2 = Mathf.Max(boundary.Start.y, boundary.End.y);

                for (int ri = 0; ri < rooms.Count; ri++)
                {
                    var b = rooms[ri].Bounds;
                    if (b.yMin >= z2 - kEpsilon || b.yMax <= z1 + kEpsilon) continue;

                    if (Mathf.Abs(b.xMax - vx) < kEpsilon) sideA.Add(ri);
                    else if (Mathf.Abs(b.xMin - vx) < kEpsilon) sideB.Add(ri);
                }
            }
            else
            {
                float sz = boundary.Start.y;
                float x1 = Mathf.Min(boundary.Start.x, boundary.End.x);
                float x2 = Mathf.Max(boundary.Start.x, boundary.End.x);

                for (int ri = 0; ri < rooms.Count; ri++)
                {
                    var b = rooms[ri].Bounds;
                    if (b.xMin >= x2 - kEpsilon || b.xMax <= x1 + kEpsilon) continue;

                    if (Mathf.Abs(b.yMax - sz) < kEpsilon) sideA.Add(ri);
                    else if (Mathf.Abs(b.yMin - sz) < kEpsilon) sideB.Add(ri);
                }
            }
        }

        private static int FindAdjacentInteriorWall(
            IReadOnlyList<RoomSpec> rooms,
            List<WallSpec> walls,
            IReadOnlyList<BspBoundary> bspBoundaries,
            int sealedRoomIdx)
        {
            var rb = rooms[sealedRoomIdx].Bounds;

            for (int wi = 0; wi < walls.Count; wi++)
            {
                if (!WallLayout.IsInteriorBspWall(walls[wi], bspBoundaries)) continue;

                var bnd = walls[wi].Boundary;

                if (bnd.IsVertical)
                {
                    float vx = bnd.Start.x;
                    float z1 = Mathf.Min(bnd.Start.y, bnd.End.y);
                    float z2 = Mathf.Max(bnd.Start.y, bnd.End.y);

                    bool zOverlap = rb.yMin < z2 - kEpsilon && rb.yMax > z1 + kEpsilon;
                    if (!zOverlap) continue;

                    if (Mathf.Abs(rb.xMax - vx) < kEpsilon ||
                        Mathf.Abs(rb.xMin - vx) < kEpsilon)
                        return wi;
                }
                else
                {
                    float sz = bnd.Start.y;
                    float x1 = Mathf.Min(bnd.Start.x, bnd.End.x);
                    float x2 = Mathf.Max(bnd.Start.x, bnd.End.x);

                    bool xOverlap = rb.xMin < x2 - kEpsilon && rb.xMax > x1 + kEpsilon;
                    if (!xOverlap) continue;

                    if (Mathf.Abs(rb.yMax - sz) < kEpsilon ||
                        Mathf.Abs(rb.yMin - sz) < kEpsilon)
                        return wi;
                }
            }

            return -1;
        }

        private static OpeningSpec? FindForcedOpening(
            float wallLength, IReadOnlyList<OpeningSpec> openings, float minWidth)
        {
            float bestGapStart  = -1f;
            float bestGapLength = 0f;
            float cursor        = 0f;

            for (int i = 0; i <= openings.Count; i++)
            {
                float gapEnd    = i < openings.Count ? openings[i].Offset : wallLength;
                float gapLength = gapEnd - cursor;

                if (gapLength > bestGapLength)
                {
                    bestGapLength = gapLength;
                    bestGapStart  = cursor;
                }

                if (i < openings.Count) cursor = openings[i].EndOffset;
            }

            if (bestGapLength < minWidth) return null;

            float width  = Mathf.Min(minWidth, bestGapLength);
            float offset = bestGapStart + (bestGapLength - width) * 0.5f;
            return new OpeningSpec(offset, width);
        }

        private static float WallAxisStart(WallSpec wall) =>
            wall.Boundary.IsVertical
                ? Mathf.Min(wall.Boundary.Start.y, wall.Boundary.End.y)
                : Mathf.Min(wall.Boundary.Start.x, wall.Boundary.End.x);
    }
}
