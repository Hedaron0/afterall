using System.Collections.Generic;

namespace AfterAll.Generation
{
    /// <summary>
    /// Output of BspPartitioner.Partition().
    /// Contains the flat list of leaf regions and all partition boundaries.
    /// </summary>
    public sealed class BspResult
    {
        /// <summary>All leaf regions (rooms / halls) after the BSP tree is fully resolved.</summary>
        public IReadOnlyList<RoomSpec> Rooms { get; }

        /// <summary>
        /// All partition lines, one per BSP split. WallLayout uses these to place
        /// wall segments and punch doorway openings.
        /// </summary>
        public IReadOnlyList<BspBoundary> Boundaries { get; }

        public BspResult(IReadOnlyList<RoomSpec> rooms, IReadOnlyList<BspBoundary> boundaries)
        {
            Rooms = rooms;
            Boundaries = boundaries;
        }
    }
}
