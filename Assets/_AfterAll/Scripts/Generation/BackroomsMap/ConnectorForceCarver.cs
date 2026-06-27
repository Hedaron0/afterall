using System.Collections.Generic;

namespace AfterAll.Generation.BackroomsMap
{
    public static class ConnectorForceCarver
    {
        private const int MaxStubLength = 6;

        public static void Apply(CellType[,] cells, IReadOnlyList<ConnectorPoint> connectors, int chunkSize)
        {
            int w = cells.GetLength(1);
            int h = cells.GetLength(0);

            foreach (var point in connectors)
            {
                int dx = 0;
                int dy = 0;

                if (point.X == 0)
                    dx = 1;
                else if (point.X == chunkSize - 1)
                    dx = -1;
                else if (point.Y == 0)
                    dy = 1;
                else if (point.Y == chunkSize - 1)
                    dy = -1;
                else
                    continue;

                CarveStub(cells, w, h, point.X, point.Y, dx, dy);
            }
        }

        private static void CarveStub(CellType[,] cells, int w, int h, int x, int y, int dx, int dy)
        {
            int cx = x;
            int cy = y;

            for (int step = 0; step < MaxStubLength; step++)
            {
                if (cx < 0 || cy < 0 || cx >= w || cy >= h)
                    break;

                if (cells[cy, cx].IsWalkable() && step > 0)
                    break;

                cells[cy, cx] = CellType.Floor;

                int nx = cx + dx;
                int ny = cy + dy;
                if (nx < 0 || ny < 0 || nx >= w || ny >= h)
                    break;

                if (cells[ny, nx].IsWalkable())
                    break;

                cx = nx;
                cy = ny;
            }
        }
    }
}
