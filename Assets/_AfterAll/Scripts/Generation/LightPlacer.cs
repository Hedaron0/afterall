using System.Collections.Generic;
using UnityEngine;

namespace AfterAll.Generation
{
    /// <summary>
    /// Places FluorescentPanel prefabs on a world-space grid locked to the ceiling tile pattern.
    /// Skips grid cells that fall inside a wall or outside all rooms.
    /// </summary>
    public static class LightPlacer
    {
        // Panel prefab scale ~1.3 m — keep centre this far from wall centre-lines.
        private const float kPanelHalfExtent = 0.65f;

        public static void Place(
            ChunkSpec spec, MapConfig config, Rng rng,
            Vector2 worldOrigin, Transform parent, List<GameObject> spawned)
        {
            if (config.LightPanelPrefab == null)
            {
                Debug.LogWarning("[LightPlacer] LightPanelPrefab is not assigned on MapConfig.");
                return;
            }

            float spacing = config.LightSpacing;
            float anchorX = config.LightGridOffsetX;
            float anchorZ = config.LightGridOffsetZ;
            Rect  bounds  = spec.ChunkBounds;
            float panelY    = spec.WallHeight;
            float halfT     = config.WallThickness * 0.5f;
            float roomInset = config.LightRoomInset;

            float worldXMin = worldOrigin.x + bounds.xMin;
            float worldXMax = worldOrigin.x + bounds.xMax;
            float worldZMin = worldOrigin.y + bounds.yMin;
            float worldZMax = worldOrigin.y + bounds.yMax;

            int nMin = Mathf.CeilToInt ((worldXMin - anchorX) / spacing);
            int nMax = Mathf.FloorToInt((worldXMax - anchorX) / spacing);
            int mMin = Mathf.CeilToInt ((worldZMin - anchorZ) / spacing);
            int mMax = Mathf.FloorToInt((worldZMax - anchorZ) / spacing);

            for (int n = nMin; n <= nMax; n++)
            {
                for (int m = mMin; m <= mMax; m++)
                {
                    if (rng.Chance(config.LightDarkChance))
                        continue;

                    float wx = anchorX + n * spacing;
                    float wz = anchorZ + m * spacing;

                    float localX = wx - worldOrigin.x;
                    float localZ = wz - worldOrigin.y;

                    if (!IsValidLightPosition(localX, localZ, spec, halfT, roomInset))
                        continue;

                    var go = Object.Instantiate(
                        config.LightPanelPrefab,
                        new Vector3(wx, panelY, wz),
                        Quaternion.identity,
                        parent);

                    go.name = "Light";
                    spawned.Add(go);
                }
            }
        }

        private static bool IsValidLightPosition(
            float localX, float localZ, ChunkSpec spec, float halfT, float roomInset)
        {
            if (!IsInsideAnyRoom(localX, localZ, spec, roomInset))
                return false;

            foreach (var wall in spec.Walls)
            {
                if (IntersectsSolidWall(localX, localZ, wall, halfT))
                    return false;
            }

            return true;
        }

        private static bool IsInsideAnyRoom(
            float localX, float localZ, ChunkSpec spec, float inset)
        {
            foreach (var room in spec.Rooms)
            {
                var b = room.Bounds;
                if (localX >= b.xMin + inset && localX <= b.xMax - inset &&
                    localZ >= b.yMin + inset && localZ <= b.yMax - inset)
                    return true;
            }

            return false;
        }

        private static bool IntersectsSolidWall(
            float px, float pz, WallSpec wall, float halfT)
        {
            float margin = halfT + kPanelHalfExtent;
            var b = wall.Boundary;

            if (b.IsHorizontal)
            {
                float wallZ = b.Start.y;
                if (Mathf.Abs(pz - wallZ) > margin)
                    return false;

                float wallStart = Mathf.Min(b.Start.x, b.End.x);
                float along     = px - wallStart;
                if (along < -kPanelHalfExtent || along > b.Length + kPanelHalfExtent)
                    return false;

                return !IsInOpening(along, wall.Openings);
            }

            float wallX = b.Start.x;
            if (Mathf.Abs(px - wallX) > margin)
                return false;

            float zStart = Mathf.Min(b.Start.y, b.End.y);
            float alongZ = pz - zStart;
            if (alongZ < -kPanelHalfExtent || alongZ > b.Length + kPanelHalfExtent)
                return false;

            return !IsInOpening(alongZ, wall.Openings);
        }

        private static bool IsInOpening(float along, IReadOnlyList<OpeningSpec> openings)
        {
            foreach (var o in openings)
            {
                if (along >= o.Offset && along <= o.EndOffset)
                    return true;
            }

            return false;
        }
    }
}
