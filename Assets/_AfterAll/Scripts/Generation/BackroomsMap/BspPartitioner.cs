using AfterAll.Generation;
using System.Collections.Generic;

namespace AfterAll.Generation.BackroomsMap
{
    public static class BspPartitioner
    {
        private const int MinLeafSize = 8;

        public static List<ZoneRect> Partition(int width, int height, int depth, Rng rng)
        {
            var root = new ZoneRect(0, 0, width, height);
            var leaves = new List<ZoneRect>();
            Split(depth, root, leaves, rng);
            return leaves;
        }

        private static void Split(int depth, ZoneRect rect, List<ZoneRect> leaves, Rng rng)
        {
            if (depth <= 0 || rect.Width < MinLeafSize || rect.Height < MinLeafSize)
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
                cut = System.Math.Clamp(cut, rect.X + MinLeafSize / 2, rect.X + rect.Width - MinLeafSize / 2);

                if (cut - rect.X < MinLeafSize || rect.X + rect.Width - cut < MinLeafSize)
                {
                    leaves.Add(rect);
                    return;
                }

                var left = new ZoneRect(rect.X, rect.Y, cut - rect.X, rect.Height);
                var right = new ZoneRect(cut, rect.Y, rect.X + rect.Width - cut, rect.Height);
                Split(depth - 1, left, leaves, rng);
                Split(depth - 1, right, leaves, rng);
            }
            else
            {
                int cut = rect.Y + (int)(rect.Height * cutRatio);
                cut = System.Math.Clamp(cut, rect.Y + MinLeafSize / 2, rect.Y + rect.Height - MinLeafSize / 2);

                if (cut - rect.Y < MinLeafSize || rect.Y + rect.Height - cut < MinLeafSize)
                {
                    leaves.Add(rect);
                    return;
                }

                var top = new ZoneRect(rect.X, rect.Y, rect.Width, cut - rect.Y);
                var bottom = new ZoneRect(rect.X, cut, rect.Width, rect.Y + rect.Height - cut);
                Split(depth - 1, top, leaves, rng);
                Split(depth - 1, bottom, leaves, rng);
            }
        }
    }
}
