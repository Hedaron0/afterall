using UnityEngine;

namespace AfterAll.Generation.BackroomsMap
{
    /// <summary>
    /// Single source of truth for 2D lab grid → 3D world (XZ floor plane).
    /// Lab: +X = east, +Y grid row = north (+world Z). Walkable top = Y 0.
    /// </summary>
    public static class MapGridConvention
    {
        public static Vector3 CellCenter(int gridX, int gridY, float cellSize) =>
            new((gridX + 0.5f) * cellSize, 0f, (gridY + 0.5f) * cellSize);

        /// <summary>World-unit direction from door cell toward the adjacent corridor cell.</summary>
        public static Vector3 FacingToWorld(CardinalDir facing) => facing switch
        {
            CardinalDir.N => Vector3.forward,
            CardinalDir.S => Vector3.back,
            CardinalDir.E => Vector3.right,
            CardinalDir.W => Vector3.left,
            _ => Vector3.forward
        };

        /// <summary>
        /// Rotation for door / frame: +Z local faces the corridor (player side), -Z into wall.
        /// </summary>
        public static Quaternion RotationFacingCorridor(CardinalDir facing)
        {
            Vector3 intoWall = FacingToWorld(facing);
            return Quaternion.LookRotation(-intoWall, Vector3.up);
        }

        /// <summary>
        /// Center of the thin frame slab on the corridor-facing side of the door cell.
        /// </summary>
        public static Vector3 DoorFrameWorldPosition(
            int gridX, int gridY, float cellSize, CardinalDir facing, float frameDepth)
        {
            float inset = cellSize * 0.5f - frameDepth * 0.5f;
            return CellCenter(gridX, gridY, cellSize) + FacingToWorld(facing) * inset;
        }
    }
}
