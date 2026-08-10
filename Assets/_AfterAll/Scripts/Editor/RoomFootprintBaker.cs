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
            RoomFootprint elevatorFootprint = null;
            foreach (string guid in guids)
            {
                RoomFootprint footprint = AssetDatabase.LoadAssetAtPath<RoomFootprint>(AssetDatabase.GUIDToAssetPath(guid));
                if (footprint == null)
                    continue;

                // Elevator never joins the general pool — RoomPoolSpawner attaches it separately.
                if (footprint.IsElevator)
                {
                    elevatorFootprint ??= footprint;
                    continue;
                }

                footprints.Add(footprint);
            }

            Undo.RecordObject(spawner, "Assign Room Footprints");
            spawner.SetSettlementFootprints(footprints.ToArray());
            if (elevatorFootprint != null)
                spawner.SetElevatorFootprint(elevatorFootprint);
            EditorUtility.SetDirty(spawner);
            Debug.Log($"[RoomFootprintBaker] Assigned {footprints.Count} footprints to {spawner.name}" +
                       (elevatorFootprint != null ? $", elevator={elevatorFootprint.PrefabId}." : "."));
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

                // A WallGapController only exists where a doorway can go, so the hull above traces
                // the door walls and nothing else. Rooms whose perimeter is not fully covered by
                // them — room10 and room4 both have a wing with no door on it — end up with a
                // footprint that stops short of their own floor: measured 9.6m and 3.35m of real,
                // walled room sitting outside its own AABB. The planner reads only this AABB, so it
                // happily parks a neighbour in that space and the wing spawns inside the other room.
                // Union in the plain structural walls so the hull describes the whole perimeter.
                if (TryGetLocalStructuralWallBounds(root.transform, out Vector2 shellMin, out Vector2 shellMax))
                {
                    wallMin = Vector2.Min(wallMin, shellMin);
                    wallMax = Vector2.Max(wallMax, shellMax);
                }

                Vector2 boundsMin = wallMin;
                Vector2 boundsMax = wallMax;
                if (TryGetLocalFloorBounds(root.transform, out Vector2 floorMin, out Vector2 floorMax))
                {
                    Vector2 expand = new Vector2(0.15f, 0.15f);
                    Vector2 clampedMin = Vector2.Max(floorMin, wallMin - expand);
                    Vector2 clampedMax = Vector2.Min(floorMax, wallMax + expand);
                    bool degenerate = clampedMin.x >= clampedMax.x || clampedMin.y >= clampedMax.y;
                    // Single-wall rooms (e.g. the elevator, which only needs a door on one side)
                    // have wall-hull data along just that one edge — clamping the opposite axis
                    // against it collapses the footprint to a sliver. Fall back to raw floor
                    // bounds whenever the wall-hull clamp produces an implausibly thin box.
                    bool tooThin = (clampedMax.x - clampedMin.x) < 1f || (clampedMax.y - clampedMin.y) < 1f;
                    if (degenerate || tooThin)
                    {
                        boundsMin = floorMin;
                        boundsMax = floorMax;
                    }
                    else
                    {
                        boundsMin = clampedMin;
                        boundsMax = clampedMax;
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
                asset.SetBakedData(prefabAsset, boundsMin, boundsMax, bakedWalls.ToArray(), gapWidth);
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

        /// <summary>
        /// XZ hull of the room's plain structural walls, in root-local space.
        ///
        /// Selection is by height, not by name: a wall is tall (the kit runs 4m), a floor or ceiling
        /// slab is 0.25m. Keeping the flat pieces out is deliberate — they are what the floor clamp
        /// downstream is guarding against, since floor trim routinely overhangs the wall line by a
        /// few centimetres and folding that into the hull would inflate every footprint.
        ///
        /// Skipped: Content (runtime-chosen props), WeightedRandomGroup subtrees (a prop that lands
        /// in one of several spots has no fixed extent), and the combined shell, whose single mesh
        /// merges floor, ceiling and walls and would therefore drag the flat pieces back in. The
        /// renderers it was built from are only disabled, never removed, so they are still walked.
        /// </summary>
        private static bool TryGetLocalStructuralWallBounds(Transform root, out Vector2 min, out Vector2 max)
        {
            const float WallMinHeightM = 1f;

            min = default;
            max = default;
            bool any = false;

            foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (renderer.bounds.size.y < WallMinHeightM)
                    continue;
                if (renderer.GetComponentInParent<WeightedRandomGroup>() != null)
                    continue;

                bool excluded = false;
                for (Transform cursor = renderer.transform; cursor != null && cursor != root.parent; cursor = cursor.parent)
                {
                    if (cursor.name == "Content" || cursor.name == "CombinedStatic")
                    {
                        excluded = true;
                        break;
                    }
                }
                if (excluded)
                    continue;

                Bounds b = renderer.bounds;
                for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                for (int z = -1; z <= 1; z += 2)
                {
                    Vector3 world = b.center + Vector3.Scale(b.extents, new Vector3(x, y, z));
                    Vector3 local = root.InverseTransformPoint(world);
                    var xz = new Vector2(local.x, local.z);

                    if (!any)
                    {
                        min = xz;
                        max = xz;
                        any = true;
                    }
                    else
                    {
                        min = Vector2.Min(min, xz);
                        max = Vector2.Max(max, xz);
                    }
                }
            }

            return any;
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
