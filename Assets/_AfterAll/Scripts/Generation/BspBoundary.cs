using UnityEngine;

namespace AfterAll.Generation
{
    /// <summary>
    /// A partition line produced by a single BSP split.
    /// Stored in the chunk's local XZ plane (same coordinate space as RoomSpec.Bounds).
    /// WallLayout reads these to decide where to place wall segments and punch openings.
    /// </summary>
    public readonly struct BspBoundary
    {
        /// <summary>Start point of the partition line in local XZ space.</summary>
        public readonly Vector2 Start;

        /// <summary>End point of the partition line in local XZ space.</summary>
        public readonly Vector2 End;

        public BspBoundary(Vector2 start, Vector2 end)
        {
            Start = start;
            End = end;
        }

        public float Length => (End - Start).magnitude;

        /// <summary>True when the line runs left-right (constant Z, Y-axis split).</summary>
        public bool IsHorizontal => Mathf.Approximately(Start.y, End.y);

        /// <summary>True when the line runs top-bottom (constant X, X-axis split).</summary>
        public bool IsVertical => Mathf.Approximately(Start.x, End.x);

        public override string ToString() =>
            $"Boundary({Start.x:F1},{Start.y:F1} → {End.x:F1},{End.y:F1})";
    }
}
