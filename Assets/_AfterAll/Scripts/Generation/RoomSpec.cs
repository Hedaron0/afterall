using UnityEngine;

namespace AfterAll.Generation
{
    /// <summary>
    /// A leaf region produced by BSP partitioning.
    /// Bounds are in the chunk's local XZ plane: Rect.x → world X, Rect.y → world Z.
    /// Depth records how deep in the BSP tree this leaf lives (0 = root leaf, unsplit chunk).
    /// RoomId is the stable index of this room within the chunk (matches index in BspResult.Rooms).
    /// </summary>
    public readonly struct RoomSpec
    {
        /// <summary>Footprint of this region in the chunk's local XZ plane.</summary>
        public readonly Rect Bounds;

        /// <summary>BSP tree depth at which this leaf was produced.</summary>
        public readonly int Depth;

        /// <summary>
        /// Stable index of this room within the chunk (= its position in BspResult.Rooms).
        /// Future passes (prop placer, room-theme pass) key on this ID.
        /// -1 means unassigned.
        /// </summary>
        public readonly int RoomId;

        public RoomSpec(Rect bounds, int depth, int roomId = -1)
        {
            Bounds = bounds;
            Depth  = depth;
            RoomId = roomId;
        }

        public Vector2 Center => Bounds.center;
        public float Width     => Bounds.width;
        public float Height    => Bounds.height;

        public override string ToString() =>
            $"Room(id={RoomId} {Bounds.x:F1},{Bounds.y:F1} {Bounds.width:F1}×{Bounds.height:F1} d={Depth})";
    }
}
