namespace AfterAll.Generation.BackroomsMap
{
    public enum CardinalDir
    {
        N,
        S,
        E,
        W
    }

    public readonly struct ExitSpec
    {
        public readonly int X;
        public readonly int Y;
        public readonly CardinalDir Dir;

        public ExitSpec(int x, int y, CardinalDir dir)
        {
            X = x;
            Y = y;
            Dir = dir;
        }
    }
}
