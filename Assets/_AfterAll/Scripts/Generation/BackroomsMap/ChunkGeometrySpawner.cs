using AfterAll.Environment;
using UnityEngine;

namespace AfterAll.Generation.BackroomsMap
{
    public static class ChunkGeometrySpawner
    {
        private const string GeometryRootName = "Geometry";

        public static Transform EnsureGeometryRoot(Transform chunkRoot)
        {
            var existing = chunkRoot.Find(GeometryRootName);
            if (existing != null)
                return existing;

            var go = new GameObject(GeometryRootName);
            go.transform.SetParent(chunkRoot, false);
            return go.transform;
        }

        public static void Clear(Transform chunkRoot)
        {
            var root = chunkRoot.Find(GeometryRootName);
            if (root == null)
                return;

            if (Application.isPlaying)
                Object.Destroy(root.gameObject);
            else
                Object.DestroyImmediate(root.gameObject);
        }

        public static void Spawn(
            Transform chunkRoot,
            ChunkData data,
            BackroomsMapConfig config,
            ChunkSpawnProfile profile)
        {
            if (data?.Cells == null || profile == null)
                return;

            Clear(chunkRoot);
            var root = EnsureGeometryRoot(chunkRoot);

            float cell = config.CellWorldSize;
            int w = data.Width;
            int h = data.Height;
            float chunkWorld = config.ChunkSizeMetres;
            var chunkCenterXZ = chunkWorld * 0.5f;

            SpawnChunkSlab(
                root, profile.FloorPrefab ?? profile.WallBlockPrefab,
                new Vector3(chunkCenterXZ, profile.FloorSlabCenterY, chunkCenterXZ),
                new Vector3(chunkWorld, profile.FloorSlabThickness, chunkWorld));

            SpawnChunkSlab(
                root, profile.CeilingPrefab ?? profile.WallBlockPrefab,
                new Vector3(chunkCenterXZ, profile.CeilingSlabCenterY, chunkCenterXZ),
                new Vector3(chunkWorld, profile.CeilingSlabThickness, chunkWorld));

            if (profile.HasWallPrefab)
            {
                var wallMask = BuildWallMask(data.Cells, w, h);
                SpawnMergedSlabs(
                    root, profile.WallBlockPrefab, wallMask, w, h, cell,
                    profile.RoomHeight, profile.WallCenterY);
            }

            SpawnLights(root, data, config, profile);
            SpawnDoors(root, data, cell, profile);
        }

        private static void SpawnChunkSlab(Transform root, GameObject prefab, Vector3 localPos, Vector3 localScale)
        {
            if (prefab == null)
                return;

            var instance = Object.Instantiate(prefab, root);
            instance.transform.localPosition = localPos;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = localScale;
        }

        private static bool[,] BuildWallMask(CellType[,] cells, int w, int h)
        {
            var mask = new bool[w, h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var cell = cells[y, x];
                mask[x, y] = cell is CellType.Wall or CellType.Pillar;
            }

            return mask;
        }

        private static void SpawnLights(
            Transform root, ChunkData data, BackroomsMapConfig config, ChunkSpawnProfile profile)
        {
            if (!profile.HasLightPrefab || data.Lights == null)
                return;

            GameObject prefab = profile.LightPanelPrefab != null
                ? profile.LightPanelPrefab
                : profile.CeilingLightPrefab;

            float cell = config.CellWorldSize;
            float lightRange = config.LightRange * cell;
            float lightY = profile.LightFixtureY;

            foreach (var (x, y) in data.Lights)
            {
                var localPos = new Vector3((x + 0.5f) * cell, lightY, (y + 0.5f) * cell);
                PlaceLightFixture(root, prefab, localPos, lightRange);
            }
        }

        private static void PlaceLightFixture(Transform root, GameObject prefab, Vector3 localPos, float lightRange)
        {
            var instance = Object.Instantiate(prefab, root);
            instance.transform.localPosition = localPos;
            instance.transform.localRotation = Quaternion.identity;

            StripBundledCeilingTiles(instance);

            var fixture = instance.GetComponentInChildren<FluorescentLight>(true);
            if (fixture != null)
            {
                fixture.transform.SetParent(root, false);
                fixture.transform.localPosition = localPos;
                fixture.transform.localRotation = Quaternion.identity;

                if (fixture.transform != instance.transform)
                    Object.Destroy(instance);
            }
            else
            {
                ResetChildOffsets(instance.transform);
            }

            Transform placed = fixture != null ? fixture.transform : instance.transform;
            foreach (var light in placed.GetComponentsInChildren<Light>(true))
                light.range = lightRange;
        }

        private static void StripBundledCeilingTiles(GameObject instance)
        {
            var transform = instance.transform;
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i);
                if (child.name.Contains("Ceiling"))
                    Object.Destroy(child.gameObject);
            }
        }

        private static void ResetChildOffsets(Transform root)
        {
            foreach (Transform child in root)
                child.localPosition = Vector3.zero;
        }

        private static void SpawnDoors(Transform root, ChunkData data, float cell, ChunkSpawnProfile profile)
        {
            if (profile.DoorPrefab == null || data.DoorOpenings == null)
                return;

            foreach (var opening in data.DoorOpenings)
            {
                var localPos = CellCenter(opening.X, opening.Y, cell);
                PlaceDoor(root, profile.DoorPrefab, localPos, YawForFacing(opening.Facing));
            }
        }

        private static void PlaceDoor(Transform root, GameObject prefab, Vector3 localPos, float yaw)
        {
            var instance = Object.Instantiate(prefab, root);
            instance.transform.localPosition = localPos;
            instance.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);

            foreach (Transform child in instance.transform)
            {
                var pos = child.localPosition;
                child.localPosition = new Vector3(0f, pos.y, 0f);
            }
        }

        private static float YawForFacing(CardinalDir facing) => facing switch
        {
            CardinalDir.N => 0f,
            CardinalDir.E => 90f,
            CardinalDir.S => 180f,
            CardinalDir.W => 270f,
            _ => 0f
        };

        private static Vector3 CellCenter(int x, int y, float cell) =>
            new((x + 0.5f) * cell, 0f, (y + 0.5f) * cell);

        private static void SpawnMergedSlabs(
            Transform root,
            GameObject prefab,
            bool[,] mask,
            int w,
            int h,
            float cell,
            float height,
            float yCenter)
        {
            var consumed = new bool[w, h];

            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (!mask[x, y] || consumed[x, y])
                    continue;

                int width = 1;
                while (x + width < w && mask[x + width, y] && !consumed[x + width, y])
                    width++;

                int depth = 1;
                while (y + depth < h)
                {
                    bool rowFull = true;
                    for (int ix = x; ix < x + width; ix++)
                    {
                        if (!mask[ix, y + depth] || consumed[ix, y + depth])
                        {
                            rowFull = false;
                            break;
                        }
                    }

                    if (!rowFull)
                        break;

                    depth++;
                }

                for (int dy = 0; dy < depth; dy++)
                for (int dx = 0; dx < width; dx++)
                    consumed[x + dx, y + dy] = true;

                float worldW = width * cell;
                float worldD = depth * cell;
                var instance = Object.Instantiate(prefab, root);
                instance.transform.localPosition = new Vector3(
                    (x + width * 0.5f) * cell,
                    yCenter,
                    (y + depth * 0.5f) * cell);
                instance.transform.localRotation = Quaternion.identity;
                instance.transform.localScale = new Vector3(worldW, height, worldD);
            }
        }
    }
}
