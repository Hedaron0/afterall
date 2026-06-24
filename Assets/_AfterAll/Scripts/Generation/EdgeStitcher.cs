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
            return OpeningGenerator.PlaceSingleOpening(edgeLength, config, rng);
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
    }
}
