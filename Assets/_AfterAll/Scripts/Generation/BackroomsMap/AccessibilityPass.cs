using System.Collections.Generic;
using AfterAll.Generation;

namespace AfterAll.Generation.BackroomsMap
{
    /// <summary>
    /// Flood-fill from connector stubs; unreachable walkable islands get a carved link or are walled off.
    /// </summary>
    public static class AccessibilityPass
    {
        private const int MaxCarveDistance = 24;
        private const int WallTinyIslandMaxCells = 2;
        private const int MaxFixPasses = 8;

        public readonly struct Result
        {
            public readonly int IslandsFixed;
            public readonly int CellsWalled;

            public Result(int islandsFixed, int cellsWalled)
            {
                IslandsFixed = islandsFixed;
                CellsWalled = cellsWalled;
            }
        }

        public static Result EnsureConnected(
            CellType[,] cells,
            IReadOnlyList<ConnectorPoint> connectors,
            Rng rng)
        {
            int w = cells.GetLength(1);
            int h = cells.GetLength(0);
            int corridors = 0;
            int walled = 0;

            for (int pass = 0; pass < MaxFixPasses; pass++)
            {
                var components = FindComponents(cells, w, h);
                if (components.Count <= 1)
                    break;

                int mainIndex = PickMainComponent(components, connectors, cells);
                var main = components[mainIndex];
                components.RemoveAt(mainIndex);

                int islandIndex = PickIslandToFix(components, main);
                var island = components[islandIndex];

                if (TryWallTinyIsland(cells, island, main))
                {
                    walled += island.Count;
                    continue;
                }

                if (TryCarveToMain(cells, island, main, w, h, rng))
                {
                    corridors++;
                    continue;
                }

                walled += SealIsland(cells, island);
            }

            return new Result(corridors, walled);
        }

        private static int PickIslandToFix(
            List<List<(int x, int y)>> islands,
            List<(int x, int y)> main)
        {
            int best = 0;
            int bestDist = int.MaxValue;

            for (int i = 0; i < islands.Count; i++)
            {
                int dist = MinDistance(islands[i], main);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = i;
                }
            }

            return best;
        }

        private static int PickMainComponent(
            List<List<(int x, int y)>> components,
            IReadOnlyList<ConnectorPoint> connectors,
            CellType[,] cells)
        {
            int best = 0;
            int bestScore = int.MinValue;

            for (int i = 0; i < components.Count; i++)
            {
                int score = components[i].Count;
                foreach (var (x, y) in components[i])
                {
                    foreach (var c in connectors)
                    {
                        if (c.X == x && c.Y == y && cells[y, x].IsWalkable())
                            score += 10_000;
                    }
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    best = i;
                }
            }

            return best;
        }

        private static bool TryWallTinyIsland(
            CellType[,] cells,
            List<(int x, int y)> island,
            List<(int x, int y)> main)
        {
            if (island.Count > WallTinyIslandMaxCells)
                return false;

            if (MinDistance(island, main) <= 2)
                return false;

            foreach (var (x, y) in island)
                cells[y, x] = CellType.Wall;

            return true;
        }

        private static bool TryCarveToMain(
            CellType[,] cells,
            List<(int x, int y)> island,
            List<(int x, int y)> main,
            int w,
            int h,
            Rng rng)
        {
            var (fromX, fromY, toX, toY, dist) = FindClosestPair(island, main);
            if (dist > MaxCarveDistance)
                return false;

            CarveLCorridor(cells, fromX, fromY, toX, toY, rng);
            return true;
        }

        private static int SealIsland(CellType[,] cells, List<(int x, int y)> island)
        {
            foreach (var (x, y) in island)
                cells[y, x] = CellType.Wall;

            return island.Count;
        }

        private static (int fromX, int fromY, int toX, int toY, int dist) FindClosestPair(
            List<(int x, int y)> island,
            List<(int x, int y)> main)
        {
            int bestDist = int.MaxValue;
            int fromX = island[0].x;
            int fromY = island[0].y;
            int toX = main[0].x;
            int toY = main[0].y;

            foreach (var (ax, ay) in island)
            foreach (var (bx, by) in main)
            {
                int dist = System.Math.Abs(ax - bx) + System.Math.Abs(ay - by);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    fromX = ax;
                    fromY = ay;
                    toX = bx;
                    toY = by;
                }
            }

            return (fromX, fromY, toX, toY, bestDist);
        }

        private static int MinDistance(List<(int x, int y)> a, List<(int x, int y)> b)
        {
            int best = int.MaxValue;
            foreach (var (ax, ay) in a)
            foreach (var (bx, by) in b)
            {
                int dist = System.Math.Abs(ax - bx) + System.Math.Abs(ay - by);
                if (dist < best)
                    best = dist;
            }

            return best;
        }

        private static void CarveLCorridor(CellType[,] cells, int x0, int y0, int x1, int y1, Rng rng)
        {
            if (rng.Chance(0.5f))
            {
                CarveLine(cells, x0, y0, x1, y0);
                CarveLine(cells, x1, y0, x1, y1);
            }
            else
            {
                CarveLine(cells, x0, y0, x0, y1);
                CarveLine(cells, x0, y1, x1, y1);
            }
        }

        private static void CarveLine(CellType[,] cells, int x0, int y0, int x1, int y1)
        {
            int dx = x0 < x1 ? 1 : x0 > x1 ? -1 : 0;
            int dy = y0 < y1 ? 1 : y0 > y1 ? -1 : 0;
            int x = x0;
            int y = y0;
            int w = cells.GetLength(1);
            int h = cells.GetLength(0);

            while (true)
            {
                if (x >= 0 && x < w && y >= 0 && y < h && cells[y, x] == CellType.Wall)
                    cells[y, x] = CellType.Floor;

                if (x == x1 && y == y1)
                    break;

                if (x != x1) x += dx;
                else if (y != y1) y += dy;
            }
        }

        private static List<List<(int x, int y)>> FindComponents(CellType[,] cells, int w, int h)
        {
            var visited = new bool[w, h];
            var components = new List<List<(int x, int y)>>();

            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (visited[x, y] || !cells[y, x].IsWalkable())
                    continue;

                var component = new List<(int x, int y)>();
                var queue = new Queue<(int x, int y)>();
                queue.Enqueue((x, y));
                visited[x, y] = true;

                while (queue.Count > 0)
                {
                    var (cx, cy) = queue.Dequeue();
                    component.Add((cx, cy));

                    TryVisit(cells, visited, queue, cx + 1, cy, w, h);
                    TryVisit(cells, visited, queue, cx - 1, cy, w, h);
                    TryVisit(cells, visited, queue, cx, cy + 1, w, h);
                    TryVisit(cells, visited, queue, cx, cy - 1, w, h);
                }

                components.Add(component);
            }

            return components;
        }

        private static void TryVisit(
            CellType[,] cells, bool[,] visited, Queue<(int x, int y)> queue,
            int x, int y, int w, int h)
        {
            if (x < 0 || y < 0 || x >= w || y >= h)
                return;
            if (visited[x, y] || !cells[y, x].IsWalkable())
                return;

            visited[x, y] = true;
            queue.Enqueue((x, y));
        }
    }
}
