using System.Collections.Generic;

namespace AfterAll.Generation
{
    /// <summary>
    /// A BSP partition boundary with its openings resolved.
    /// GeometrySpawner reads this to spawn wall segments, leaving gaps for doorways.
    /// </summary>
    public sealed class WallSpec
    {
        /// <summary>The partition line this wall sits on (local XZ chunk space).</summary>
        public BspBoundary Boundary { get; }

        /// <summary>
        /// Doorway gaps, sorted by Offset (ascending).
        /// Empty = solid wall across the full boundary.
        /// </summary>
        public IReadOnlyList<OpeningSpec> Openings { get; }

        public WallSpec(BspBoundary boundary, IReadOnlyList<OpeningSpec> openings)
        {
            Boundary = boundary;
            Openings = openings;
        }
    }
}
