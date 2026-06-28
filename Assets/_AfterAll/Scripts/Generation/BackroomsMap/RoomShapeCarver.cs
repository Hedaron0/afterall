using AfterAll.Generation;

namespace AfterAll.Generation.BackroomsMap
{
    public enum RoomShape
    {
        Rectangle,
        LShape,
        CornerCut,
        TShape
    }

    /// <summary>
    /// Carves L, corner-cut, and T room templates inside a zone while preserving wall padding.
    /// </summary>
    public static class RoomShapeCarver
    {
        private const int MinZoneRectangle = 4;
        private const int MinZoneLShape = 5;
        private const int MinZoneCornerCut = 6;
        private const int MinZoneTShape = 7;

        public static bool TryCarve(
            CellType[,] cells,
            ZoneRect zone,
            BackroomsMapConfig config,
            Rng rng,
            bool wallOnly,
            int padding = 1)
        {
            var shape = PickShape(zone, config, rng);
            int ix = zone.X + padding;
            int iy = zone.Y + padding;
            int iw = zone.Width - padding * 2;
            int ih = zone.Height - padding * 2;

            if (iw < 2 || ih < 2)
                return false;

            int rotation = rng.Range(0, 4);

            return shape switch
            {
                RoomShape.LShape => CarveLShape(cells, ix, iy, iw, ih, rotation, rng, wallOnly),
                RoomShape.CornerCut => CarveCornerCut(cells, ix, iy, iw, ih, rotation, rng, wallOnly),
                RoomShape.TShape => CarveTShape(cells, ix, iy, iw, ih, rotation, rng, wallOnly),
                _ => CarveRectangle(cells, ix, iy, iw, ih, rng, wallOnly)
            };
        }

        private static RoomShape PickShape(ZoneRect zone, BackroomsMapConfig config, Rng rng)
        {
            var candidates = new System.Collections.Generic.List<(RoomShape shape, int weight)>
            {
                (RoomShape.Rectangle, config.RectangleShapeWeight),
                (RoomShape.LShape, config.LShapeWeight),
                (RoomShape.CornerCut, config.CornerCutWeight),
                (RoomShape.TShape, config.TShapeWeight)
            };

            int total = 0;
            foreach (var (_, w) in candidates)
                total += w;

            float roll = rng.Value() * total;
            int cumulative = 0;
            RoomShape picked = RoomShape.Rectangle;

            foreach (var (shape, weight) in candidates)
            {
                cumulative += weight;
                if (roll < cumulative)
                {
                    picked = shape;
                    break;
                }
            }

            return FitShapeToZone(picked, zone);
        }

        private static RoomShape FitShapeToZone(RoomShape shape, ZoneRect zone)
        {
            int w = zone.Width;
            int h = zone.Height;

            if (shape == RoomShape.TShape && w >= MinZoneTShape && h >= MinZoneTShape)
                return RoomShape.TShape;
            if (shape == RoomShape.CornerCut && w >= MinZoneCornerCut && h >= MinZoneCornerCut)
                return RoomShape.CornerCut;
            if (shape == RoomShape.LShape && w >= MinZoneLShape && h >= MinZoneLShape)
                return RoomShape.LShape;
            if (w >= MinZoneRectangle && h >= MinZoneRectangle)
                return RoomShape.Rectangle;

            return RoomShape.Rectangle;
        }

        private static bool CarveRectangle(
            CellType[,] cells, int ix, int iy, int iw, int ih, Rng rng, bool wallOnly)
        {
            float sizeRatio = 0.55f + rng.Value() * 0.35f;
            int rw = System.Math.Max(2, (int)(iw * sizeRatio));
            int rh = System.Math.Max(2, (int)(ih * sizeRatio));

            if (rw > iw) rw = iw;
            if (rh > ih) rh = ih;

            int maxX = ix + iw - rw;
            int maxY = iy + ih - rh;
            if (maxX < ix || maxY < iy)
            {
                ZoneCarver.WriteRoomRect(cells, ix, iy, System.Math.Max(2, iw), System.Math.Max(2, ih), wallOnly);
                return true;
            }

            int rx = rng.Range(ix, maxX + 1);
            int ry = rng.Range(iy, maxY + 1);
            ZoneCarver.WriteRoomRect(cells, rx, ry, rw, rh, wallOnly);
            return true;
        }

        private static bool CarveLShape(
            CellType[,] cells, int ix, int iy, int iw, int ih, int rotation, Rng rng, bool wallOnly)
        {
            int armW = System.Math.Max(2, (int)(iw * (0.35f + rng.Value() * 0.2f)));
            int armH = System.Math.Max(2, (int)(ih * (0.3f + rng.Value() * 0.2f)));

            if (armW >= iw) armW = iw - 1;
            if (armH >= ih) armH = ih - 1;

            var rects = new (int x, int y, int w, int h)[]
            {
                (0, 0, armW, ih),
                (0, ih - armH, iw, armH)
            };

            return CarveRotatedRects(cells, ix, iy, iw, ih, rotation, rects, wallOnly);
        }

        private static bool CarveCornerCut(
            CellType[,] cells, int ix, int iy, int iw, int ih, int rotation, Rng rng, bool wallOnly)
        {
            float sizeRatio = 0.65f + rng.Value() * 0.25f;
            int rw = System.Math.Max(3, (int)(iw * sizeRatio));
            int rh = System.Math.Max(3, (int)(ih * sizeRatio));
            if (rw > iw) rw = iw;
            if (rh > ih) rh = ih;

            int ox = ix + (iw - rw) / 2;
            int oy = iy + (ih - rh) / 2;

            ZoneCarver.WriteRoomRect(cells, ox, oy, rw, rh, wallOnly);

            int notchW = System.Math.Max(1, rw / 3);
            int notchH = System.Math.Max(1, rh / 3);

            var notch = RotateLocalRect(0, 0, rw, rh, rw - notchW, rh - notchH, notchW, notchH, rotation);
            WriteWallRect(cells, ox + notch.x, oy + notch.y, notch.w, notch.h, wallOnly);
            return true;
        }

        private static bool CarveTShape(
            CellType[,] cells, int ix, int iy, int iw, int ih, int rotation, Rng rng, bool wallOnly)
        {
            int barH = System.Math.Max(2, (int)(ih * (0.25f + rng.Value() * 0.15f)));
            int stemW = System.Math.Max(2, (int)(iw * (0.3f + rng.Value() * 0.2f)));
            int stemX = (iw - stemW) / 2;

            if (barH >= ih) barH = ih / 2;
            if (stemW >= iw) stemW = iw - 1;

            var rects = new (int x, int y, int w, int h)[]
            {
                (0, 0, iw, barH),
                (stemX, barH, stemW, ih - barH)
            };

            return CarveRotatedRects(cells, ix, iy, iw, ih, rotation, rects, wallOnly);
        }

        private static bool CarveRotatedRects(
            CellType[,] cells,
            int ix, int iy, int iw, int ih,
            int rotation,
            (int x, int y, int w, int h)[] localRects,
            bool wallOnly)
        {
            foreach (var (lx, ly, lw, lh) in localRects)
            {
                var r = RotateLocalRect(0, 0, iw, ih, lx, ly, lw, lh, rotation);
                ZoneCarver.WriteRoomRect(cells, ix + r.x, iy + r.y, r.w, r.h, wallOnly);
            }

            return true;
        }

        private static (int x, int y, int w, int h) RotateLocalRect(
            int ox, int oy, int ow, int oh,
            int lx, int ly, int lw, int lh,
            int rotation)
        {
            return rotation switch
            {
                1 => (oh - ly - lh, lx, lh, lw),
                2 => (ow - lx - lw, oh - ly - lh, lw, lh),
                3 => (ly, ow - lx - lw, lh, lw),
                _ => (lx, ly, lw, lh)
            };
        }

        private static void WriteWallRect(
            CellType[,] cells, int x, int y, int w, int h, bool wallOnly)
        {
            for (int dy = 0; dy < h; dy++)
            for (int dx = 0; dx < w; dx++)
            {
                int wx = x + dx;
                int wy = y + dy;
                if (!ZoneCarver.InBounds(cells, wx, wy))
                    continue;
                if (wallOnly && cells[wy, wx] != CellType.Room && cells[wy, wx] != CellType.Floor)
                    continue;
                cells[wy, wx] = CellType.Wall;
            }
        }
    }
}
