using AfterAll.Generation;
using System.Collections.Generic;

namespace AfterAll.Generation.BackroomsMap
{
    public static class BspPartitioner
    {
        private const int DefaultMinLeafSize = 8;

        public static List<ZoneRect> Partition(int width, int height, int depth, Rng rng)
            => Partition(width, height, depth, rng, DefaultMinLeafSize);

        public static List<ZoneRect> Partition(int width, int height, int depth, Rng rng, int minLeafSize)
        {
            var root = new ZoneRect(0, 0, width, height);
            var leaves = new List<ZoneRect>();
            Split(depth, root, leaves, rng, minLeafSize);
            return leaves;
        }

        private static void Split(int depth, ZoneRect rect, List<ZoneRect> leaves, Rng rng, int minLeafSize)
        {
            if (depth <= 0 || rect.Width < minLeafSize || rect.Height < minLeafSize)
            {
                leaves.Add(rect);
                return;
            }

            bool splitVertical;
            if (rect.Width > rect.Height)
                splitVertical = true;
            else if (rect.Height > rect.Width)
                splitVertical = false;
            else
                splitVertical = rng.Chance(0.5f);

            float cutRatio = 0.35f + rng.Value() * 0.3f;

            if (splitVertical)
            {
                int cut = rect.X + (int)(rect.Width * cutRatio);
                cut = System.Math.Clamp(cut, rect.X + minLeafSize / 2, rect.X + rect.Width - minLeafSize / 2);

                if (cut - rect.X < minLeafSize || rect.X + rect.Width - cut < minLeafSize)
                {
                    leaves.Add(rect);
                    return;
                }

                var left = new ZoneRect(rect.X, rect.Y, cut - rect.X, rect.Height);
                var right = new ZoneRect(cut, rect.Y, rect.X + rect.Width - cut, rect.Height);
                Split(depth - 1, left, leaves, rng, minLeafSize);
                Split(depth - 1, right, leaves, rng, minLeafSize);
            }
            else
            {
                int cut = rect.Y + (int)(rect.Height * cutRatio);
                cut = System.Math.Clamp(cut, rect.Y + minLeafSize / 2, rect.Y + rect.Height - minLeafSize / 2);

                if (cut - rect.Y < minLeafSize || rect.Y + rect.Height - cut < minLeafSize)
                {
                    leaves.Add(rect);
                    return;
                }

                var top = new ZoneRect(rect.X, rect.Y, rect.Width, cut - rect.Y);
                var bottom = new ZoneRect(rect.X, cut, rect.Width, rect.Y + rect.Height - cut);
                Split(depth - 1, top, leaves, rng, minLeafSize);
                Split(depth - 1, bottom, leaves, rng, minLeafSize);
            }
        }
    }
}
