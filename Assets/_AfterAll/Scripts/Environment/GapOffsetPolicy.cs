namespace AfterAll.Environment
{
    public struct GapOffsetPolicy
    {
        public bool randomGapOffset;
        public float edgeMarginM;
        public float spanFraction;

        public static GapOffsetPolicy Default => new GapOffsetPolicy
        {
            randomGapOffset = true,
            edgeMarginM = 0.15f,
            spanFraction = 1f
        };
    }
}
