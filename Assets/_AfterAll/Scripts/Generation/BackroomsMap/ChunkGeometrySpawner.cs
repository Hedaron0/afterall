using AfterAll.Environment;
using UnityEngine;
using DoorBehaviour = AfterAll.Door.Door;

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
            SpawnDoorWallSurrounds(root, data, cell, profile);
            SpawnDoors(root, data, cell, profile);
        }

        private static Vector3 DoorAnchorPosition(int gridX, int gridY, float cell, CardinalDir facing, float frameDepth) =>
            MapGridConvention.DoorFrameWorldPosition(gridX, gridY, cell, facing, frameDepth);

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

        private static void SpawnDoorWallSurrounds(
            Transform root, ChunkData data, float cell, ChunkSpawnProfile profile)
        {
            if (!profile.HasWallPrefab || data.DoorOpenings == null)
                return;

            float wallBase = profile.WallBaseY;
            float roomHeight = profile.RoomHeight;
            float doorWidth = profile.DoorWidth;
            float doorHeight = profile.DoorHeight;
            float frameDepth = profile.FrameDepth;
            float jambWidth = (cell - doorWidth) * 0.5f;
            float headerHeight = roomHeight - doorHeight;

            foreach (var opening in data.DoorOpenings)
            {
                var anchor = new GameObject("DoorSurround").transform;
                anchor.SetParent(root, false);
                anchor.localPosition = DoorAnchorPosition(opening.X, opening.Y, cell, opening.Facing, frameDepth);
                anchor.localRotation = MapGridConvention.RotationFacingCorridor(opening.Facing);

                if (jambWidth > 0.01f)
                {
                    float jambCenterX = (cell + doorWidth) * 0.25f;
                    float jambCenterY = wallBase + doorHeight * 0.5f;
                    SpawnScaledWallBlock(
                        anchor, profile.WallBlockPrefab,
                        new Vector3(-jambCenterX, jambCenterY, 0f),
                        new Vector3(jambWidth, doorHeight, frameDepth));
                    SpawnScaledWallBlock(
                        anchor, profile.WallBlockPrefab,
                        new Vector3(jambCenterX, jambCenterY, 0f),
                        new Vector3(jambWidth, doorHeight, frameDepth));
                }

                if (headerHeight > 0.01f)
                {
                    float headerCenterY = wallBase + doorHeight + headerHeight * 0.5f;
                    SpawnScaledWallBlock(
                        anchor, profile.WallBlockPrefab,
                        new Vector3(0f, headerCenterY, 0f),
                        new Vector3(cell, headerHeight, frameDepth));
                }
            }
        }

        private static void SpawnScaledWallBlock(
            Transform parent, GameObject prefab, Vector3 localPos, Vector3 localScale)
        {
            var instance = Object.Instantiate(prefab, parent);
            instance.transform.localPosition = localPos;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = localScale;
        }

        private static void SpawnDoors(Transform root, ChunkData data, float cell, ChunkSpawnProfile profile)
        {
            if (profile.DoorPrefab == null || data.DoorOpenings == null)
                return;

            foreach (var opening in data.DoorOpenings)
            {
                var anchorPos = DoorAnchorPosition(opening.X, opening.Y, cell, opening.Facing, profile.FrameDepth);
                var instance = Object.Instantiate(profile.DoorPrefab, root);
                instance.transform.localPosition = anchorPos;
                instance.transform.localRotation = MapGridConvention.RotationFacingCorridor(opening.Facing);

                if (instance.TryGetComponent<DoorBehaviour>(out var door))
                {
                    float panelDepth = profile.FrameDepth * 0.85f;
                    door.ApplyProcGenLayout(profile.DoorWidth, profile.DoorHeight, panelDepth, profile.WallBaseY);
                }
            }
        }

        private static Vector3 CellCenter(int x, int y, float cell) =>
            MapGridConvention.CellCenter(x, y, cell);

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
