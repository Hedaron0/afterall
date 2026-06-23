namespace AfterAll.Generation
{
    /// <summary>
    /// A gap (doorway) punched into a wall boundary.
    /// Offset is measured from the boundary's Start point in metres.
    /// </summary>
    public readonly struct OpeningSpec
    {
        /// <summary>Distance from the boundary Start to the near edge of this gap (metres).</summary>
        public readonly float Offset;

        /// <summary>Width of the gap in metres.</summary>
        public readonly float Width;

        /// <summary>Distance from the boundary Start to the far edge of this gap (metres).</summary>
        public float EndOffset => Offset + Width;

        public OpeningSpec(float offset, float width)
        {
            Offset = offset;
            Width  = width;
        }

        public override string ToString() =>
            $"Opening(offset={Offset:F2}m, width={Width:F2}m)";
    }
}
