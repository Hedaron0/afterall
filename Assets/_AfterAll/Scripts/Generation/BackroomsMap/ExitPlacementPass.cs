using System.Collections.Generic;
using AfterAll.Generation;

namespace AfterAll.Generation.BackroomsMap
{
    public static class ExitPlacementPass
    {
        public static ExitSpec? TryPlace(CellType[,] cells, BackroomsMapConfig config, Rng rng)
        {
            if (!rng.Chance(1f / config.ExitDensity))
                return null;

            int size = config.ChunkSize;
            var edges = new List<(CardinalDir dir, int x, int y)>();

            CollectEdgeWalkable(cells, size, CardinalDir.S, 0, edges);
            CollectEdgeWalkable(cells, size, CardinalDir.N, size - 1, edges);
            CollectEdgeWalkable(cells, size, CardinalDir.W, 0, edges, vertical: true);
            CollectEdgeWalkable(cells, size, CardinalDir.E, size - 1, edges, vertical: true);

            if (edges.Count == 0)
                return null;

            var pick = edges[rng.Range(0, edges.Count)];
            cells[pick.y, pick.x] = CellType.Exit;
            return new ExitSpec(pick.x, pick.y, pick.dir);
        }

        private static void CollectEdgeWalkable(
            CellType[,] cells, int size, CardinalDir dir, int fixedCoord,
            List<(CardinalDir dir, int x, int y)> edges, bool vertical = false)
        {
            for (int i = 1; i < size - 1; i++)
            {
                int x = vertical ? fixedCoord : i;
                int y = vertical ? i : fixedCoord;
                if (cells[y, x].IsWalkable())
                    edges.Add((dir, x, y));
            }
        }
    }
}
