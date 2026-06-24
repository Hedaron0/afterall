using System;
using System.Collections.Generic;

namespace AfterAll.Generation
{
    /// <summary>
    /// Deterministic doorway placement on chunk borders so adjacent chunks agree
    /// on where passages line up across seams.
    /// </summary>
    public static class EdgeStitcher
    {
        public enum Border { South, North, West, East }

        private const float kMinStubBetweenOpenings = 0.5f;

        /// <summary>
        /// Stable seed for one infinite-world border line. Neighbouring chunks
        /// that share the same physical edge always get the same seed.
        /// </summary>
        public static int BorderSeed(int worldSeed, int cx, int cz, Border border)
        {
            var rng = new Rng(worldSeed);
            return border switch
            {
                // South border of (cx,cz) == north border of (cx,cz-1)
                Border.South => rng.Derive(cx, cz).Derive(10).Seed,
                // North border of (cx,cz) == south border of (cx,cz+1)
                Border.North => rng.Derive(cx, cz + 1).Derive(10).Seed,
                // West border of (cx,cz) == east border of (cx-1,cz)
                Border.West  => rng.Derive(cx, cz).Derive(11).Seed,
                // East border of (cx,cz) == west border of (cx+1,cz)
                Border.East  => rng.Derive(cx + 1, cz).Derive(11).Seed,
                _            => worldSeed
            };
        }

        /// <summary>
        /// Generates openings along a full chunk edge [0, edgeLength].
        /// Both neighbours clip this list to their room-face segments.
        /// </summary>
        public static IReadOnlyList<OpeningSpec> GenerateBorderOpenings(
            float edgeLength, int borderSeed, MapConfig config)
        {
            var rng = new Rng(borderSeed);
            return GenerateOpeningsAlongEdge(edgeLength, config, rng);
        }

        /// <summary>
        /// Clips full-edge openings to a room-face segment [faceStart, faceEnd].
        /// Returns offsets relative to faceStart.
        /// </summary>
        public static IReadOnlyList<OpeningSpec> ClipToFaceSegment(
            IReadOnlyList<OpeningSpec> edgeOpenings,
            float faceStart,
            float faceEnd)
        {
            if (edgeOpenings.Count == 0) return edgeOpenings;

            var result = new List<OpeningSpec>(edgeOpenings.Count);

            foreach (var o in edgeOpenings)
            {
                float openStart = o.Offset;
                float openEnd   = o.Offset + o.Width;

                float clipStart = Math.Max(openStart, faceStart);
                float clipEnd   = Math.Min(openEnd,   faceEnd);

                if (clipEnd - clipStart >= 0.1f)
                {
                    result.Add(new OpeningSpec(
                        clipStart - faceStart,
                        clipEnd   - clipStart,
                        o.Type));
                }
            }

            return result;
        }

        private static IReadOnlyList<OpeningSpec> GenerateOpeningsAlongEdge(
            float length, MapConfig config, Rng rng)
        {
            float margin = config.OpeningEdgeMargin;
            float usableStart = margin;
            float usableEnd   = length - margin;

            if (usableEnd - usableStart < config.OpeningMinWidth)
            {
                float centre = length * 0.5f;
                float halfW  = Math.Min(config.OpeningMinWidth * 0.5f, length * 0.35f);
                return new[] { new OpeningSpec(centre - halfW, halfW * 2f) };
            }

            int count = rng.Range(config.MinOpeningsPerBoundary, config.MaxOpeningsPerBoundary + 1);
            count = Math.Max(1, count);

            var openings = new List<OpeningSpec>(count);
            float cursor  = usableStart;

            for (int i = 0; i < count; i++)
            {
                float remaining = usableEnd - cursor;
                if (remaining < config.OpeningMinWidth) break;

                float maxWidth  = Math.Min(config.OpeningMaxWidth, remaining);
                float width     = rng.Range(config.OpeningMinWidth, maxWidth);

                float gapRoom   = remaining - width;
                float gapBefore = gapRoom > 0f ? rng.Range(0f, gapRoom) : 0f;

                openings.Add(new OpeningSpec(cursor + gapBefore, width));
                cursor = cursor + gapBefore + width + kMinStubBetweenOpenings;
            }

            return openings;
        }
    }
}
