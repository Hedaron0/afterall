using System.Collections.Generic;

namespace AfterAll.Generation.BackroomsMap
{
    public static class ZoneConnector
    {
        public static void ConnectZones(CellType[,] cells, IReadOnlyList<ZoneSpec> zones, Rng rng)
        {
            if (zones.Count < 2)
                return;

            for (int i = 1; i < zones.Count; i++)
            {
                var from = zones[i - 1];
                var to = zones[i];
                CarveLCorridor(cells, from.CenterX, from.CenterY, to.CenterX, to.CenterY, rng);
            }
        }

        private static void CarveLCorridor(CellType[,] cells, int x0, int y0, int x1, int y1, Rng rng)
        {
            bool xFirst = rng.Chance(0.5f);

            if (xFirst)
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
                if (x >= 0 && x < w && y >= 0 && y < h)
                    SetWalkable(cells, x, y);

                if (x == x1 && y == y1)
                    break;

                if (x != x1) x += dx;
                else if (y != y1) y += dy;
            }
        }

        private static void SetWalkable(CellType[,] cells, int x, int y)
        {
            if (cells[y, x] == CellType.Wall)
                cells[y, x] = CellType.Floor;
        }
    }
}
