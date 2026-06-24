using System.Collections.Generic;
using UnityEngine;

namespace AfterAll.Generation
{
    /// <summary>
    /// Self-contained data package for one chunk.
    /// Produced by WallLayout; consumed by GeometrySpawner and future passes.
    /// All coordinates are in local chunk space (0,0 → chunkSize,chunkSize).
    /// </summary>
    public sealed class ChunkSpec
    {
        /// <summary>Full XZ footprint of the chunk in local space (origin always 0,0).</summary>
        public Rect ChunkBounds { get; }

        /// <summary>Distance from floor top surface (Y=0) to ceiling bottom surface (metres).</summary>
        public float WallHeight { get; }

        /// <summary>Thickness of the floor and ceiling slabs in metres.</summary>
        public float SlabThickness { get; }

        /// <summary>All wall faces with their doorway openings resolved.</summary>
        public IReadOnlyList<WallSpec> Walls { get; }

        /// <summary>
        /// All leaf rooms produced by BSP.
        /// Future passes (props, room themes, nav-mesh hints) iterate this directly
        /// instead of re-deriving rooms from wall geometry.
        /// </summary>
        public IReadOnlyList<RoomSpec> Rooms { get; }

        public ChunkSpec(Rect chunkBounds, float wallHeight, float slabThickness,
                         IReadOnlyList<WallSpec> walls,
                         IReadOnlyList<RoomSpec> rooms = null)
        {
            ChunkBounds    = chunkBounds;
            WallHeight     = wallHeight;
            SlabThickness  = slabThickness;
            Walls          = walls;
            Rooms          = rooms ?? System.Array.Empty<RoomSpec>();
        }
    }
}
