using System.Collections.Generic;
using UnityEngine;

namespace AfterAll.Generation
{
    /// <summary>
    /// Recursively splits a rectangle into leaf regions using Binary Space Partitioning.
    ///
    /// Design: "filled-plate BSP" — the whole chunk is a solid floor, and partition lines
    /// become wall boundaries. Rooms are irregular pockets carved by those walls, NOT
    /// separate boxes with gaps between them.
    ///
    /// All coordinates are in the chunk's local XZ plane (Rect.x → X, Rect.y → Z).
    /// This class is stateless; call Partition() to get a fresh BspResult.
    /// </summary>
    public static class BspPartitioner
    {
        /// <summary>
        /// Partition <paramref name="bounds"/> into leaf regions and record all split boundaries.
        /// </summary>
        /// <param name="bounds">The chunk rectangle to partition (local XZ space).</param>
        /// <param name="config">Generation parameters (min room size, split bias, etc.).</param>
        /// <param name="seed">Per-chunk seed — derive from the master seed via Rng.Derive().</param>
        public static BspResult Partition(Rect bounds, MapConfig config, int seed)
        {
            var rng = new Rng(seed);
            var rooms = new List<RoomSpec>(64);
            var boundaries = new List<BspBoundary>(64);

            Split(bounds, rng, config, depth: 0, rooms, boundaries);

            return new BspResult(rooms, boundaries);
        }

        // ──────────────────────────────────────────────────────────────────────────
        //  Core recursive split
        // ──────────────────────────────────────────────────────────────────────────

        private static void Split(
            Rect bounds,
            Rng rng,
            MapConfig config,
            int depth,
            List<RoomSpec> rooms,
            List<BspBoundary> boundaries)
        {
            bool canSplitX = bounds.width  >= config.MinRoomSize * 2f;
            bool canSplitY = bounds.height >= config.MinRoomSize * 2f;

            bool atMaxDepth = depth >= config.MaxDepth;
            bool earlyStop  = rng.Chance(config.EarlyStopChance);

            // Leaf: emit a room and stop
            if (atMaxDepth || earlyStop || (!canSplitX && !canSplitY))
            {
                rooms.Add(new RoomSpec(bounds, depth, roomId: rooms.Count));
                return;
            }

            // Choose split axis: prefer the longer dimension, then optionally flip
            int axis = ChooseAxis(bounds, canSplitX, canSplitY, rng, config.AxisFlipChance);

            // Split position: biased away from center so rooms are uneven
            float splitT = rng.Range(config.SplitMin, config.SplitMax);

            if (axis == 0)
                SplitAlongX(bounds, splitT, rng, config, depth, rooms, boundaries);
            else
                SplitAlongY(bounds, splitT, rng, config, depth, rooms, boundaries);
        }

        private static void SplitAlongX(
            Rect bounds, float splitT,
            Rng rng, MapConfig config, int depth,
            List<RoomSpec> rooms, List<BspBoundary> boundaries)
        {
            float splitX = bounds.xMin + bounds.width * splitT;

            // Record the partition boundary (full line spanning the current rect's Z range)
            boundaries.Add(new BspBoundary(
                new Vector2(splitX, bounds.yMin),
                new Vector2(splitX, bounds.yMax)));

            Split(Rect.MinMaxRect(bounds.xMin, bounds.yMin, splitX,       bounds.yMax),
                  rng, config, depth + 1, rooms, boundaries);
            Split(Rect.MinMaxRect(splitX,      bounds.yMin, bounds.xMax,  bounds.yMax),
                  rng, config, depth + 1, rooms, boundaries);
        }

        private static void SplitAlongY(
            Rect bounds, float splitT,
            Rng rng, MapConfig config, int depth,
            List<RoomSpec> rooms, List<BspBoundary> boundaries)
        {
            float splitY = bounds.yMin + bounds.height * splitT;

            boundaries.Add(new BspBoundary(
                new Vector2(bounds.xMin, splitY),
                new Vector2(bounds.xMax, splitY)));

            Split(Rect.MinMaxRect(bounds.xMin, bounds.yMin, bounds.xMax, splitY),
                  rng, config, depth + 1, rooms, boundaries);
            Split(Rect.MinMaxRect(bounds.xMin, splitY,      bounds.xMax, bounds.yMax),
                  rng, config, depth + 1, rooms, boundaries);
        }

        // ──────────────────────────────────────────────────────────────────────────
        //  Axis selection
        // ──────────────────────────────────────────────────────────────────────────

        private static int ChooseAxis(Rect bounds, bool canSplitX, bool canSplitY, Rng rng, float flipChance)
        {
            // Only one axis is splittable — no choice
            if (canSplitX && !canSplitY) return 0;
            if (canSplitY && !canSplitX) return 1;

            // Both available: default to the longer dimension so rooms stay compact
            int dominant = bounds.width >= bounds.height ? 0 : 1;

            // Random flip for variety (respects Backrooms' irregular proportions)
            if (rng.Chance(flipChance))
                dominant = 1 - dominant;

            return dominant;
        }
    }
}
