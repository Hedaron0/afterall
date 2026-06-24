namespace AfterAll.Generation
{
    /// <summary>
    /// What kind of passage this opening represents.
    /// GeometrySpawner and future passes read this to decide what to place in the gap.
    /// </summary>
    public enum OpeningType
    {
        /// <summary>Plain open archway — no frame spawned.</summary>
        Open,

        /// <summary>Reserved: door frame + door mesh will be spawned.</summary>
        Door,

        /// <summary>Reserved: decorative arch mesh will be spawned.</summary>
        Arch,
    }

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

        /// <summary>What kind of passage this opening is.</summary>
        public readonly OpeningType Type;

        /// <summary>Distance from the boundary Start to the far edge of this gap (metres).</summary>
        public float EndOffset => Offset + Width;

        /// <summary>Creates an Open-type opening.</summary>
        public OpeningSpec(float offset, float width)
            : this(offset, width, OpeningType.Open) { }

        public OpeningSpec(float offset, float width, OpeningType type)
        {
            Offset = offset;
            Width  = width;
            Type   = type;
        }

        public override string ToString() =>
            $"Opening({Type} offset={Offset:F2}m, width={Width:F2}m)";
    }
}
