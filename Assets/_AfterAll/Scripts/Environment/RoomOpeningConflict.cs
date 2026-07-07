using System.Collections.Generic;
using UnityEngine;

namespace AfterAll.Environment
{
    public static class RoomOpeningConflict
    {
        public static List<Bounds> GetOpenCorridors(RoomInstance room, float inwardDepth = 1.5f)
        {
            var corridors = new List<Bounds>();
            if (room == null)
                return corridors;

            foreach (WallGapController wall in room.Walls)
            {
                if (wall == null || !wall.hasOpening)
                    continue;

                if (wall.TryGetOpeningCorridorBounds(wall.gapOffset, out Bounds corridor, inwardDepth))
                    corridors.Add(corridor);
            }

            return corridors;
        }

        public static RoomOpeningBlockZone[] GetZones(Transform root)
        {
            if (root == null)
                return System.Array.Empty<RoomOpeningBlockZone>();

            return root.GetComponentsInChildren<RoomOpeningBlockZone>(true);
        }

        public static bool PresetConflicts(IReadOnlyList<Bounds> corridors, Transform presetRoot)
        {
            if (corridors == null || corridors.Count == 0 || presetRoot == null)
                return false;

            RoomOpeningBlockZone[] zones = GetZones(presetRoot);
            foreach (RoomOpeningBlockZone zone in zones)
            {
                if (zone == null)
                    continue;

                Bounds zoneBounds = zone.GetWorldBounds();
                foreach (Bounds corridor in corridors)
                {
                    if (BoundsOverlapXZ(corridor, zoneBounds))
                        return true;
                }
            }

            return false;
        }

        public static bool ItemConflicts(IReadOnlyList<Bounds> corridors, Transform itemRoot)
        {
            if (corridors == null || corridors.Count == 0 || itemRoot == null)
                return false;

            if (itemRoot.TryGetComponent(out RoomOpeningBlockZone selfZone))
            {
                Bounds zoneBounds = selfZone.GetWorldBounds();
                foreach (Bounds corridor in corridors)
                {
                    if (BoundsOverlapXZ(corridor, zoneBounds))
                        return true;
                }
            }

            RoomOpeningBlockZone[] childZones = itemRoot.GetComponentsInChildren<RoomOpeningBlockZone>(true);
            foreach (RoomOpeningBlockZone zone in childZones)
            {
                if (zone == null || zone.transform == itemRoot)
                    continue;

                Bounds zoneBounds = zone.GetWorldBounds();
                foreach (Bounds corridor in corridors)
                {
                    if (BoundsOverlapXZ(corridor, zoneBounds))
                        return true;
                }
            }

            return false;
        }

        public static bool BoundsOverlapXZ(Bounds a, Bounds b)
        {
            return a.min.x < b.max.x && a.max.x > b.min.x &&
                   a.min.z < b.max.z && a.max.z > b.min.z;
        }
    }
}
