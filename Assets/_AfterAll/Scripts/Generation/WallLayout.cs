using System;
using System.Collections.Generic;
using UnityEngine;

namespace AfterAll.Generation
{
    /// <summary>
    /// Converts a BspResult into a ChunkSpec: assigns doorway openings to every
    /// partition boundary and packages the result for GeometrySpawner.
    ///
    /// Use a child Rng derived from the chunk seed so opening placement is
    /// independent of BSP's random sequence (adding/removing splits won't shift openings).
    /// </summary>
    public static class WallLayout
    {
        // Minimum solid stub between consecutive openings so walls aren't paper-thin slivers.
        private const float kMinStubBetweenOpenings = 0.5f;

        public static ChunkSpec Build(BspResult bsp, MapConfig config, Rng rng)
        {
            var walls = new List<WallSpec>(bsp.Boundaries.Count + 4);

            foreach (var boundary in bsp.Boundaries)
            {
                var openings = GenerateOpenings(boundary, config, rng);
                walls.Add(new WallSpec(boundary, openings));
            }

            if (config.AddPerimeterWalls)
                AddPerimeterWalls(walls, config.ChunkSize);

            return new ChunkSpec(
                new Rect(0f, 0f, config.ChunkSize, config.ChunkSize),
                config.WallHeight,
                config.SlabThickness,
                walls);
        }

        // ──────────────────────────────────────────────────────────────────────────
        //  Perimeter walls (solid — no openings until ChunkManager stitches edges)
        // ──────────────────────────────────────────────────────────────────────────

        private static void AddPerimeterWalls(List<WallSpec> walls, float size)
        {
            var none = Array.Empty<OpeningSpec>();

            // Bottom edge  Z=0      (horizontal, IsHorizontal)
            walls.Add(new WallSpec(
                new BspBoundary(new Vector2(0f,    0f),    new Vector2(size,  0f)),    none));
            // Top edge     Z=size   (horizontal, IsHorizontal)
            walls.Add(new WallSpec(
                new BspBoundary(new Vector2(0f,    size),  new Vector2(size,  size)),  none));
            // Left edge    X=0      (vertical, IsVertical)
            walls.Add(new WallSpec(
                new BspBoundary(new Vector2(0f,    0f),    new Vector2(0f,    size)),  none));
            // Right edge   X=size   (vertical, IsVertical)
            walls.Add(new WallSpec(
                new BspBoundary(new Vector2(size,  0f),    new Vector2(size,  size)),  none));
        }

        // ──────────────────────────────────────────────────────────────────────────
        //  Opening generation
        // ──────────────────────────────────────────────────────────────────────────

        private static IReadOnlyList<OpeningSpec> GenerateOpenings(
            BspBoundary boundary, MapConfig config, Rng rng)
        {
            float length = boundary.Length;
            float margin = config.OpeningEdgeMargin;

            // The safe zone for opening placement: at least `margin` metres from each end.
            float usableStart = margin;
            float usableEnd   = length - margin;

            if (usableEnd - usableStart < config.OpeningMinWidth)
            {
                // Boundary too short for even one opening — force a minimal one in the middle.
                float centre = length * 0.5f;
                float halfW  = Mathf.Min(config.OpeningMinWidth * 0.5f, length * 0.35f);
                return new[] { new OpeningSpec(centre - halfW, halfW * 2f) };
            }

            int count = rng.Range(config.MinOpeningsPerBoundary, config.MaxOpeningsPerBoundary + 1);
            count = Mathf.Max(1, count); // always at least one (ConnectivityPass handles sealing later)

            var openings = new List<OpeningSpec>(count);
            float cursor  = usableStart;

            for (int i = 0; i < count; i++)
            {
                float remaining = usableEnd - cursor;
                if (remaining < config.OpeningMinWidth) break;

                float maxWidth = Mathf.Min(config.OpeningMaxWidth, remaining);
                float width    = rng.Range(config.OpeningMinWidth, maxWidth);

                // Random gap before this opening (within the space that still leaves room).
                float gapRoom  = remaining - width;
                float gapBefore = gapRoom > 0f ? rng.Range(0f, gapRoom) : 0f;

                openings.Add(new OpeningSpec(cursor + gapBefore, width));

                // Advance cursor past this opening + required stub.
                cursor = cursor + gapBefore + width + kMinStubBetweenOpenings;
            }

            return openings;
        }
    }
}
