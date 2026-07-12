using System.Collections.Generic;
using AfterAll.Environment;
using UnityEditor;
using UnityEngine;

namespace AfterAll.Editor
{
    public static class RoomFootprintBaker
    {
        private const string DefaultRoomPrefabSearchPath = "Assets/_AfterAll/Prefabs/Rooms";
        private const string OutputFolder = "Assets/_AfterAll/Data/RoomFootprints";

        [MenuItem("AfterAll/Generation/Bake Room Footprints")]
        public static void BakeRoomFootprints()
        {
            EnsureOutputFolder();

            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { DefaultRoomPrefabSearchPath });
            int baked = 0;
            int failed = 0;

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefabAsset == null)
                    continue;

                if (prefabAsset.GetComponent<RoomInstance>() == null &&
                    prefabAsset.GetComponentInChildren<WallGapController>(true) == null)
                    continue;

                if (TryBakePrefab(prefabAsset, path, out string error))
                {
                    baked++;
                }
                else
                {
                    failed++;
                    Debug.LogWarning($"[RoomFootprintBaker] Failed {path}: {error}");
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[RoomFootprintBaker] Baked={baked}, Failed={failed}, Output={OutputFolder}");
        }

        [MenuItem("AfterAll/Generation/Assign Footprints To Selected RoomPoolSpawner")]
        private static void AssignFootprintsToSelectedSpawner()
        {
            RoomPoolSpawner spawner = Selection.activeGameObject != null
                ? Selection.activeGameObject.GetComponent<RoomPoolSpawner>()
                : null;

            if (spawner == null)
                spawner = Object.FindFirstObjectByType<RoomPoolSpawner>();

            if (spawner == null)
            {
                Debug.LogWarning("[RoomFootprintBaker] No RoomPoolSpawner selected or in scene.");
                return;
            }

            if (!AssetDatabase.IsValidFolder(OutputFolder))
            {
                Debug.LogWarning("[RoomFootprintBaker] Bake footprints first.");
                return;
            }

            string[] guids = AssetDatabase.FindAssets("t:RoomFootprint", new[] { OutputFolder });
            var footprints = new List<RoomFootprint>();
            foreach (string guid in guids)
            {
                RoomFootprint footprint = AssetDatabase.LoadAssetAtPath<RoomFootprint>(AssetDatabase.GUIDToAssetPath(guid));
                if (footprint != null)
                    footprints.Add(footprint);
            }

            Undo.RecordObject(spawner, "Assign Room Footprints");
            spawner.SetSettlementFootprints(footprints.ToArray());
            EditorUtility.SetDirty(spawner);
            Debug.Log($"[RoomFootprintBaker] Assigned {footprints.Count} footprints to {spawner.name}.");
        }

        [MenuItem("AfterAll/Generation/Recompute Footprint Roles From Bounds")]
        private static void RecomputeAllFootprintRoles()
        {
            // Kept as a no-op-ish refresh: roles are hidden; shapes are geometry-derived at plan time.
            if (!AssetDatabase.IsValidFolder(OutputFolder))
            {
                Debug.LogWarning("[RoomFootprintBaker] Bake footprints first.");
                return;
            }

            string[] guids = AssetDatabase.FindAssets("t:RoomFootprint", new[] { OutputFolder });
            int updated = 0;
            foreach (string guid in guids)
            {
                RoomFootprint footprint = AssetDatabase.LoadAssetAtPath<RoomFootprint>(AssetDatabase.GUIDToAssetPath(guid));
                if (footprint == null)
                    continue;

                footprint.SetRole(RoomRole.Auto);
                EditorUtility.SetDirty(footprint);
                updated++;
                Debug.Log(
                    $"[RoomFootprintBaker] {footprint.PrefabId}: area={footprint.BoundsAreaM2:F0}m², " +
                    $"corridorShape={footprint.IsCorridorShape}");
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[RoomFootprintBaker] Refreshed geometry tags on {updated} footprints.");
        }

        private static bool TryBakePrefab(GameObject prefabAsset, string prefabPath, out string error)
        {
            error = null;
            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            if (root == null)
            {
                error = "Could not load prefab contents.";
                return false;
            }

            try
            {
                RoomInstance room = root.GetComponent<RoomInstance>() ?? root.AddComponent<RoomInstance>();
                room.CacheWalls();

                WallGapController[] walls = root.GetComponentsInChildren<WallGapController>(true);
                if (walls.Length == 0)
                {
                    error = "No WallGapController components.";
                    return false;
                }

                var bakedWalls = new List<RoomFootprint.Wall>();
                float gapWidth = RoomFootprint.DefaultGapWidthM;

                foreach (WallGapController wall in walls)
                {
                    wall.RecacheBaseline();
                    if (!wall.TryGetClosedWallGeometry(
                            out Vector3 seamWorld,
                            out Vector3 axisWorld,
                            out float lengthM,
                            out Vector3 outwardWorld))
                    {
                        Debug.LogWarning($"[RoomFootprintBaker] Skip wall geometry {prefabPath}/{wall.name}");
                        continue;
                    }

                    gapWidth = wall.gapWidth > 0.05f ? wall.gapWidth : gapWidth;
                    bool doorValid = wall.TryGetGapOffsetRange(out _, out _, out _);
                    float openingWidth = wall.EffectiveOpeningWidth;

                    Vector3 startWorld = seamWorld - axisWorld.normalized * (lengthM * 0.5f);
                    Vector3 endWorld = seamWorld + axisWorld.normalized * (lengthM * 0.5f);

                    Vector2 seamLocal = ToLocalXZ(root.transform, seamWorld);
                    Vector2 axisLocal = ToLocalDirXZ(root.transform, axisWorld);
                    Vector2 outwardLocal = ToLocalDirXZ(root.transform, outwardWorld);
                    SocketDirection direction = RoomSocket.DirectionFromForward(outwardWorld);

                    if (wall.BakeSocketContract() && wall.TryGetBakedSocket(out RoomSocket socket) && socket.HasValidContract)
                        direction = socket.Direction;

                    bakedWalls.Add(new RoomFootprint.Wall
                    {
                        name = wall.name,
                        seamLocal = seamLocal,
                        axisLocal = axisLocal.normalized,
                        startLocal = ToLocalXZ(root.transform, startWorld),
                        endLocal = ToLocalXZ(root.transform, endWorld),
                        lengthM = lengthM,
                        outwardLocal = outwardLocal.normalized,
                        direction = direction,
                        doorValid = doorValid,
                        openingMode = wall.OpeningMode,
                        openingWidthM = openingWidth
                    });
                }

                if (bakedWalls.Count == 0)
                {
                    error = "No walls baked.";
                    return false;
                }

                // Prefer wall-hull bounds so floor meshes that overhang seams do not
                // poison overlap tests (false "always overlap" → only 1 room placed).
                Vector2 wallMin = bakedWalls[0].startLocal;
                Vector2 wallMax = bakedWalls[0].endLocal;
                foreach (RoomFootprint.Wall wall in bakedWalls)
                {
                    wallMin = Vector2.Min(wallMin, Vector2.Min(wall.startLocal, wall.endLocal));
                    wallMax = Vector2.Max(wallMax, Vector2.Max(wall.startLocal, wall.endLocal));
                    wallMin = Vector2.Min(wallMin, wall.seamLocal);
                    wallMax = Vector2.Max(wallMax, wall.seamLocal);
                }

                Vector2 boundsMin = wallMin;
                Vector2 boundsMax = wallMax;
                if (TryGetLocalFloorBounds(root.transform, out Vector2 floorMin, out Vector2 floorMax))
                {
                    Vector2 expand = new Vector2(0.15f, 0.15f);
                    boundsMin = Vector2.Max(floorMin, wallMin - expand);
                    boundsMax = Vector2.Min(floorMax, wallMax + expand);
                    if (boundsMin.x >= boundsMax.x || boundsMin.y >= boundsMax.y)
                    {
                        boundsMin = wallMin;
                        boundsMax = wallMax;
                    }
                }

                string assetPath = $"{OutputFolder}/{prefabAsset.name}_Footprint.asset";
                RoomFootprint asset = AssetDatabase.LoadAssetAtPath<RoomFootprint>(assetPath);
                if (asset == null)
                {
                    asset = ScriptableObject.CreateInstance<RoomFootprint>();
                    AssetDatabase.CreateAsset(asset, assetPath);
                }

                float area = Mathf.Max(0.5f, Mathf.Abs((boundsMax.x - boundsMin.x) * (boundsMax.y - boundsMin.y)));
                asset.SetBakedData(prefabAsset, boundsMin, boundsMax, bakedWalls.ToArray(), gapWidth, RoomRole.Auto);
                EditorUtility.SetDirty(asset);
                Debug.Log(
                    $"[RoomFootprintBaker] {prefabAsset.name}: walls={bakedWalls.Count}, " +
                    $"area={area:F1}m², corridorShape={asset.IsCorridorShape}");
                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static bool TryGetLocalFloorBounds(Transform root, out Vector2 min, out Vector2 max)
        {
            min = default;
            max = default;
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            bool any = false;

            foreach (Renderer renderer in renderers)
            {
                if (renderer == null)
                    continue;

                string objectName = renderer.gameObject.name;
                if (objectName.StartsWith("Cube", System.StringComparison.OrdinalIgnoreCase))
                    continue;
                if (objectName.IndexOf("Floor", System.StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                Bounds b = renderer.bounds;
                Vector3[] corners =
                {
                    new Vector3(b.min.x, b.min.y, b.min.z),
                    new Vector3(b.min.x, b.min.y, b.max.z),
                    new Vector3(b.max.x, b.min.y, b.min.z),
                    new Vector3(b.max.x, b.min.y, b.max.z)
                };

                foreach (Vector3 corner in corners)
                {
                    Vector2 local = ToLocalXZ(root, corner);
                    if (!any)
                    {
                        min = local;
                        max = local;
                        any = true;
                    }
                    else
                    {
                        min = Vector2.Min(min, local);
                        max = Vector2.Max(max, local);
                    }
                }
            }

            return any;
        }

        private static Vector2 ToLocalXZ(Transform root, Vector3 world)
        {
            Vector3 local = root.InverseTransformPoint(world);
            return new Vector2(local.x, local.z);
        }

        private static Vector2 ToLocalDirXZ(Transform root, Vector3 worldDir)
        {
            Vector3 local = root.InverseTransformDirection(worldDir);
            local.y = 0f;
            if (local.sqrMagnitude < 0.0001f)
                return Vector2.up;

            local.Normalize();
            return new Vector2(local.x, local.z);
        }

        private static void EnsureOutputFolder()
        {
            if (AssetDatabase.IsValidFolder(OutputFolder))
                return;

            if (!AssetDatabase.IsValidFolder("Assets/_AfterAll/Data"))
                AssetDatabase.CreateFolder("Assets/_AfterAll", "Data");

            AssetDatabase.CreateFolder("Assets/_AfterAll/Data", "RoomFootprints");
        }
    }
}
