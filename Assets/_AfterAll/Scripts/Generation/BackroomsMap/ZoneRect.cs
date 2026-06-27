namespace AfterAll.Generation.BackroomsMap
{
    public readonly struct ZoneRect
    {
        public readonly int X;
        public readonly int Y;
        public readonly int Width;
        public readonly int Height;

        public ZoneRect(int x, int y, int width, int height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public int CenterX => X + Width / 2;
        public int CenterY => Y + Height / 2;
    }

    public readonly struct ZoneSpec
    {
        public readonly ZoneRect Rect;
        public readonly ZoneArchetype Archetype;

        public ZoneSpec(ZoneRect rect, ZoneArchetype archetype)
        {
            Rect = rect;
            Archetype = archetype;
        }

        public int CenterX => Rect.CenterX;
        public int CenterY => Rect.CenterY;
    }
}
