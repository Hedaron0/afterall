using AfterAll.Generation;
using System.Collections.Generic;

namespace AfterAll.Generation.BackroomsMap
{
    public static class ZoneConnector
    {
        private static readonly (int dx, int dy, CardinalDir face)[] Neighbors =
        {
            (0, 1, CardinalDir.N),
            (0, -1, CardinalDir.S),
            (1, 0, CardinalDir.E),
            (-1, 0, CardinalDir.W)
        };

        public static void ConnectPoints(CellType[,] cells, int x0, int y0, int x1, int y1, Rng rng)
        {
            CarveLCorridor(cells, x0, y0, x1, y1, rng);
        }

        public static void ConnectZones(
            CellType[,] cells,
            IReadOnlyList<ZoneSpec> zones,
            float doorChance,
            Rng rng,
            List<DoorOpeningSpec> doorOpenings)
        {
            if (zones.Count < 2)
                return;

            int w = cells.GetLength(1);
            int h = cells.GetLength(0);

            for (int i = 1; i < zones.Count; i++)
            {
                var from = zones[i - 1];
                var to = zones[i];
                var path = CarveLCorridor(cells, from.CenterX, from.CenterY, to.CenterX, to.CenterY, rng);

                if (doorChance > 0f && rng.Chance(doorChance))
                    TryPlaceDoorOnCorridor(cells, w, h, path, doorOpenings);
            }
        }

        private static void TryPlaceDoorOnCorridor(
            CellType[,] cells,
            int w,
            int h,
            List<(int x, int y)> path,
            List<DoorOpeningSpec> doorOpenings)
        {
            for (int i = path.Count - 1; i >= 0; i--)
            {
                var (cx, cy) = path[i];

                foreach (var (dx, dy, face) in Neighbors)
                {
                    int wx = cx + dx;
                    int wy = cy + dy;
                    if (wx < 0 || wy < 0 || wx >= w || wy >= h)
                        continue;

                    if (cells[wy, wx] != CellType.Wall)
                        continue;

                    cells[wy, wx] = CellType.DoorFrame;
                    doorOpenings.Add(new DoorOpeningSpec(wx, wy, Opposite(face)));
                    return;
                }
            }
        }

        private static CardinalDir Opposite(CardinalDir dir) => dir switch
        {
            CardinalDir.N => CardinalDir.S,
            CardinalDir.S => CardinalDir.N,
            CardinalDir.E => CardinalDir.W,
            _ => CardinalDir.E
        };

        private static List<(int x, int y)> CarveLCorridor(
            CellType[,] cells, int x0, int y0, int x1, int y1, Rng rng)
        {
            bool xFirst = rng.Chance(0.5f);
            var path = new List<(int x, int y)>();

            if (xFirst)
            {
                CarveLine(cells, path, x0, y0, x1, y0);
                CarveLine(cells, path, x1, y0, x1, y1);
            }
            else
            {
                CarveLine(cells, path, x0, y0, x0, y1);
                CarveLine(cells, path, x0, y1, x1, y1);
            }

            return path;
        }

        private static void CarveLine(
            CellType[,] cells, List<(int x, int y)> path, int x0, int y0, int x1, int y1)
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
                {
                    SetWalkable(cells, x, y);
                    if (path.Count == 0 || path[path.Count - 1] != (x, y))
                        path.Add((x, y));
                }

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
