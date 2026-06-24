using System.Collections.Generic;

namespace AfterAll.Generation
{
    /// <summary>
    /// Shared doorway placement — always at most one gap per wall line.
    /// </summary>
    public static class OpeningGenerator
    {
        public static int ResolveRoomOpeningCount(MapConfig config, Rng rng)
        {
            int min = config.MinOpeningsPerRoom;
            int max = config.MaxOpeningsPerRoom;
            return rng.Range(min, max + 1);
        }

        /// <summary>
        /// Places a single doorway along a wall segment of the given length.
        /// </summary>
        public static IReadOnlyList<OpeningSpec> PlaceSingleOpening(
            float length, MapConfig config, Rng rng)
        {
            float margin = config.OpeningEdgeMargin;
            float usableStart = margin;
            float usableEnd = length - margin;

            if (usableEnd - usableStart < config.OpeningMinWidth)
            {
                float centre = length * 0.5f;
                float halfW = System.Math.Min(config.OpeningMinWidth * 0.5f, length * 0.35f);
                return new[] { new OpeningSpec(centre - halfW, halfW * 2f) };
            }

            float maxWidth = System.Math.Min(config.OpeningMaxWidth, usableEnd - usableStart);
            float width = rng.Range(config.OpeningMinWidth, maxWidth);

            float gapRoom = usableEnd - usableStart - width;
            float gapBefore = gapRoom > 0f ? rng.Range(0f, gapRoom) : 0f;

            return new[] { new OpeningSpec(usableStart + gapBefore, width) };
        }

        public static void Shuffle<T>(IList<T> list, Rng rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
