using System.Collections.Generic;
using AfterAll.Generation;

namespace AfterAll.Generation.BackroomsMap
{
    public static class LightPlacementPass
    {
        public static List<(int x, int y)> Place(CellType[,] cells, BackroomsMapConfig config, Rng rng)
        {
            int w = cells.GetLength(1);
            int h = cells.GetLength(0);
            var lights = new List<(int x, int y)>();
            var covered = new bool[w, h];
            var candidates = CollectWalkable(cells, w, h);

            Shuffle(candidates, rng);

            foreach (var (x, y) in candidates)
            {
                if (covered[x, y])
                    continue;
                if (!IsLitSurface(cells, x, y))
                    continue;

                lights.Add((x, y));
                FloodCover(cells, covered, x, y, w, h, config.LightRange);
            }

            return lights;
        }

        private static void FloodCover(
            CellType[,] cells, bool[,] covered, int sx, int sy, int w, int h, int lightRange)
        {
            var queue = new Queue<(int x, int y, int depth)>();
            queue.Enqueue((sx, sy, 0));
            covered[sx, sy] = true;

            while (queue.Count > 0)
            {
                var (x, y, depth) = queue.Dequeue();
                if (depth >= lightRange)
                    continue;

                TryVisit(cells, covered, queue, x + 1, y, w, h, depth + 1);
                TryVisit(cells, covered, queue, x - 1, y, w, h, depth + 1);
                TryVisit(cells, covered, queue, x, y + 1, w, h, depth + 1);
                TryVisit(cells, covered, queue, x, y - 1, w, h, depth + 1);
            }
        }

        private static void TryVisit(
            CellType[,] cells, bool[,] covered, Queue<(int x, int y, int depth)> queue,
            int x, int y, int w, int h, int depth)
        {
            if (x < 0 || y < 0 || x >= w || y >= h)
                return;
            if (covered[x, y])
                return;
            if (!IsLitSurface(cells, x, y))
                return;

            covered[x, y] = true;
            queue.Enqueue((x, y, depth));
        }

        private static bool IsLitSurface(CellType[,] cells, int x, int y) =>
            cells[y, x] is CellType.Room or CellType.Floor;

        private static List<(int x, int y)> CollectWalkable(CellType[,] cells, int w, int h)
        {
            var list = new List<(int x, int y)>();
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (IsLitSurface(cells, x, y))
                    list.Add((x, y));
            }

            return list;
        }

        private static void Shuffle(List<(int x, int y)> list, Rng rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
