using AfterAll.Generation;

namespace AfterAll.Generation.BackroomsMap
{
    public static class ZoneCarver
    {
        public static void CarveZone(CellType[,] cells, ZoneSpec zone, BackroomsMapConfig config, Rng rng)
        {
            switch (zone.Archetype)
            {
                case ZoneArchetype.StandardRoom:
                    CarveStandardRoom(cells, zone.Rect, config, rng, wallOnly: false);
                    break;
                case ZoneArchetype.VoidRoom:
                    CarveVoidRoom(cells, zone.Rect, config, rng, padding: 1, wallOnly: false);
                    break;
                case ZoneArchetype.PillarHall:
                    CarvePillarHall(cells, zone.Rect, rng);
                    break;
                case ZoneArchetype.DenseMaze:
                    CarveDenseMaze(cells, zone.Rect, rng);
                    break;
                case ZoneArchetype.Organic:
                    CarveOrganic(cells, zone.Rect, rng);
                    break;
                case ZoneArchetype.Corridor:
                    CarveCorridor(cells, zone.Rect, rng, wallOnly: false);
                    break;
            }
        }

        /// <summary>
        /// Carves inside void pockets — only writes to Wall cells, never overwrites existing walkable geometry.
        /// </summary>
        public static void CarveZoneInVoid(CellType[,] cells, ZoneSpec zone, BackroomsMapConfig config, Rng rng)
        {
            switch (zone.Archetype)
            {
                case ZoneArchetype.StandardRoom:
                    CarveStandardRoom(cells, zone.Rect, config, rng, wallOnly: true);
                    break;
                case ZoneArchetype.VoidRoom:
                    CarveVoidRoom(cells, zone.Rect, config, rng, padding: 1, wallOnly: true);
                    break;
                case ZoneArchetype.Corridor:
                    CarveCorridor(cells, zone.Rect, rng, wallOnly: true);
                    break;
            }
        }

        public static void WriteRoomRect(CellType[,] cells, int x, int y, int w, int h, bool wallOnly) =>
            CarveRect(cells, x, y, w, h, CellType.Room, wallOnly);

        public static bool InBounds(CellType[,] cells, int x, int y) =>
            x >= 0 && y >= 0 && x < cells.GetLength(1) && y < cells.GetLength(0);

        private static void CarveStandardRoom(
            CellType[,] cells, ZoneRect zone, BackroomsMapConfig config, Rng rng, bool wallOnly)
        {
            if (config != null && rng.Chance(config.ShapedRoomChance))
            {
                if (RoomShapeCarver.TryCarve(cells, zone, config, rng, wallOnly, padding: 1))
                    return;
            }

            float sizeRatio = 0.5f + rng.Value() * 0.4f;
            int roomW = System.Math.Max(2, (int)(zone.Width * sizeRatio));
            int roomH = System.Math.Max(2, (int)(zone.Height * sizeRatio));

            int maxX = zone.X + zone.Width - roomW - 2;
            int maxY = zone.Y + zone.Height - roomH - 2;
            int minX = zone.X + 2;
            int minY = zone.Y + 2;

            if (maxX < minX || maxY < minY)
            {
                CarveRect(cells, zone.X + 1, zone.Y + 1,
                    System.Math.Max(2, zone.Width - 2), System.Math.Max(2, zone.Height - 2),
                    CellType.Room, wallOnly);
                return;
            }

            int rx = rng.Range(minX, maxX + 1);
            int ry = rng.Range(minY, maxY + 1);
            CarveRect(cells, rx, ry, roomW, roomH, CellType.Room, wallOnly);
        }

        private static void CarveVoidRoom(
            CellType[,] cells, ZoneRect zone, BackroomsMapConfig config, Rng rng, int padding, bool wallOnly)
        {
            if (config != null && zone.Width >= 6 && zone.Height >= 6 && rng.Chance(config.ShapedRoomChance))
            {
                if (RoomShapeCarver.TryCarve(cells, zone, config, rng, wallOnly, padding))
                    return;
            }

            CarveRect(cells, zone.X + padding, zone.Y + padding,
                System.Math.Max(1, zone.Width - padding * 2),
                System.Math.Max(1, zone.Height - padding * 2), CellType.Room, wallOnly);
        }

        private static void CarvePillarHall(CellType[,] cells, ZoneRect zone, Rng rng)
        {
            CarveRect(cells, zone.X + 1, zone.Y + 1,
                System.Math.Max(1, zone.Width - 2),
                System.Math.Max(1, zone.Height - 2), CellType.Room, wallOnly: false);

            int spacing = rng.Range(4, 6);
            for (int y = zone.Y + spacing; y < zone.Y + zone.Height - 1; y += spacing)
            for (int x = zone.X + spacing; x < zone.X + zone.Width - 1; x += spacing)
            {
                if (InBounds(cells, x, y) && cells[y, x] == CellType.Room)
                    cells[y, x] = CellType.Pillar;
            }
        }

        private static void CarveDenseMaze(CellType[,] cells, ZoneRect zone, Rng rng)
        {
            const float fillTarget = 0.34f;

            for (int y = zone.Y; y < zone.Y + zone.Height; y++)
            for (int x = zone.X; x < zone.X + zone.Width; x++)
            {
                if (!InBounds(cells, x, y)) continue;
                cells[y, x] = rng.Chance(fillTarget) ? CellType.Floor : CellType.Wall;
            }
        }

        private static void CarveOrganic(CellType[,] cells, ZoneRect zone, Rng rng)
        {
            const float fillTarget = 0.45f;
            int w = zone.Width;
            int h = zone.Height;
            var local = new bool[w, h];

            for (int ly = 0; ly < h; ly++)
            for (int lx = 0; lx < w; lx++)
                local[lx, ly] = rng.Chance(fillTarget);

            for (int pass = 0; pass < 4; pass++)
                local = SmoothPass(local, w, h);

            for (int ly = 0; ly < h; ly++)
            for (int lx = 0; lx < w; lx++)
            {
                int wx = zone.X + lx;
                int wy = zone.Y + ly;
                if (!InBounds(cells, wx, wy)) continue;
                cells[wy, wx] = local[lx, ly] ? CellType.Room : CellType.Wall;
            }
        }

        private static bool[,] SmoothPass(bool[,] grid, int w, int h)
        {
            var next = new bool[w, h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int open = CountOpenNeighbors(grid, w, h, x, y);
                next[x, y] = open >= 5;
            }

            return next;
        }

        private static int CountOpenNeighbors(bool[,] grid, int w, int h, int cx, int cy)
        {
            int count = 0;
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                int nx = cx + dx;
                int ny = cy + dy;
                if (nx < 0 || ny < 0 || nx >= w || ny >= h)
                    count++;
                else if (grid[nx, ny])
                    count++;
            }

            return count;
        }

        private static void CarveCorridor(CellType[,] cells, ZoneRect zone, Rng rng, bool wallOnly)
        {
            int cx = zone.CenterX;
            int cy = zone.CenterY;
            int steps = (int)((zone.Width + zone.Height) * 1.4f);

            int x = cx;
            int y = cy;
            int dir = rng.Range(0, 4);

            for (int i = 0; i < steps; i++)
            {
                if (x >= zone.X && x < zone.X + zone.Width &&
                    y >= zone.Y && y < zone.Y + zone.Height &&
                    InBounds(cells, x, y))
                {
                    if (wallOnly)
                    {
                        if (cells[y, x] == CellType.Wall)
                            cells[y, x] = CellType.Floor;
                    }
                    else
                    {
                        cells[y, x] = CellType.Floor;
                    }
                }

                if (rng.Chance(0.3f))
                    dir = rng.Range(0, 4);

                switch (dir)
                {
                    case 0: y--; break;
                    case 1: y++; break;
                    case 2: x--; break;
                    case 3: x++; break;
                }
            }
        }

        private static void CarveRect(CellType[,] cells, int x, int y, int w, int h, CellType type, bool wallOnly)
        {
            for (int dy = 0; dy < h; dy++)
            for (int dx = 0; dx < w; dx++)
            {
                int wx = x + dx;
                int wy = y + dy;
                if (!InBounds(cells, wx, wy))
                    continue;
                if (wallOnly && cells[wy, wx] != CellType.Wall)
                    continue;
                cells[wy, wx] = type;
            }
        }
    }
}
