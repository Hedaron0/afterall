using System.Collections.Generic;

namespace AfterAll.Generation
{
    /// <summary>
    /// A wall face with its doorway openings resolved.
    /// GeometrySpawner reads this to spawn wall segments, leaving gaps for doorways.
    ///
    /// RoomId identifies which room this face "belongs to" (-1 = shared/unassigned).
    /// Future passes (door spawner, prop placer) can filter walls by room.
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

        /// <summary>
        /// Which room this wall face primarily serves.
        /// -1 means the wall is shared or the room is unresolved.
        /// Populated by WallLayout; used by future door/prop passes.
        /// </summary>
        public int RoomId { get; }

        public WallSpec(BspBoundary boundary, IReadOnlyList<OpeningSpec> openings, int roomId = -1)
        {
            Boundary = boundary;
            Openings = openings;
            RoomId   = roomId;
        }
    }
}
