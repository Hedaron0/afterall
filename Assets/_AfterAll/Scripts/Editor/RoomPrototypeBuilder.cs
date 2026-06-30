#if UNITY_EDITOR
using AfterAll.Environment;
using UnityEditor;
using UnityEngine;

namespace AfterAll.Environment.Editor
{
    public static class RoomPrototypeBuilder
    {
        const float RoomWidth = 8f;
        const float RoomDepth = 8f;
        const float WallHeight = 3f;
        const float WallMeshMetersPerScaleX = 0.004f;

        [MenuItem("AfterAll/Create Room Prototype Prefab")]
        public static void CreateRoomPrototypePrefabMenu()
        {
            CreateRoomPrototypePrefab(showDialog: true);
        }

        public static void CreateRoomPrototypePrefab(bool showDialog = false)
        {
            var wallPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/_AfterAll/Prefabs/Backrooms_Modular/Wall.prefab");
            var floorPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/_AfterAll/Prefabs/Backrooms_Modular/Floor.prefab");
            var ceilingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/_AfterAll/Prefabs/Backrooms_Modular/Ceiling.prefab");
            var framePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/_AfterAll/Prefabs/Backrooms_Modular/Frame.prefab");

            if (wallPrefab == null)
            {
                EditorUtility.DisplayDialog("Room Prototype", "Wall prefab not found.", "OK");
                return;
            }

            EnsureFolder("Assets/_AfterAll/Prefabs/Rooms");

            var roomRoot = new GameObject("RoomPrototype");
            var roomOpenings = roomRoot.AddComponent<RoomWallOpenings>();

            if (floorPrefab != null)
                PrefabUtility.InstantiatePrefab(floorPrefab, roomRoot.transform);

            if (ceilingPrefab != null)
            {
                var ceiling = (GameObject)PrefabUtility.InstantiatePrefab(ceilingPrefab, roomRoot.transform);
                ceiling.transform.localPosition = new Vector3(0f, WallHeight, 0f);
            }

            var halfWidth = RoomWidth * 0.5f;
            var halfDepth = RoomDepth * 0.5f;
            var halfSegment = RoomWidth * 0.5f;
            var segmentScaleX = halfSegment / WallMeshMetersPerScaleX;

            roomOpenings.GetType(); // keep reference used

            var north = CreateWall(roomRoot.transform, "Wall_North", new Vector3(0f, 0f, halfDepth), Quaternion.identity,
                RoomWidth, wallPrefab, framePrefab, segmentScaleX);
            var south = CreateWall(roomRoot.transform, "Wall_South", new Vector3(0f, 0f, -halfDepth), Quaternion.Euler(0f, 180f, 0f),
                RoomWidth, wallPrefab, framePrefab, segmentScaleX);
            var east = CreateWall(roomRoot.transform, "Wall_East", new Vector3(halfWidth, 0f, 0f), Quaternion.Euler(0f, 90f, 0f),
                RoomDepth, wallPrefab, framePrefab, RoomDepth * 0.5f / WallMeshMetersPerScaleX);
            var west = CreateWall(roomRoot.transform, "Wall_West", new Vector3(-halfWidth, 0f, 0f), Quaternion.Euler(0f, -90f, 0f),
                RoomDepth, wallPrefab, framePrefab, RoomDepth * 0.5f / WallMeshMetersPerScaleX);

            SetPrivateField(roomOpenings, "_north", north);
            SetPrivateField(roomOpenings, "_south", south);
            SetPrivateField(roomOpenings, "_east", east);
            SetPrivateField(roomOpenings, "_west", west);

            var prefabPath = "Assets/_AfterAll/Prefabs/Rooms/RoomPrototype.prefab";
            PrefabUtility.SaveAsPrefabAsset(roomRoot, prefabPath);
            Object.DestroyImmediate(roomRoot);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            var saved = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Selection.activeObject = saved;
            if (showDialog)
                EditorUtility.DisplayDialog("Room Prototype", $"Saved {prefabPath}", "OK");
        }

        static SplitWallOpening CreateWall(
            Transform parent,
            string wallName,
            Vector3 localPosition,
            Quaternion localRotation,
            float wallLength,
            GameObject wallPrefab,
            GameObject framePrefab,
            float segmentScaleX)
        {
            var wallRoot = new GameObject(wallName);
            wallRoot.transform.SetParent(parent, false);
            wallRoot.transform.localPosition = localPosition;
            wallRoot.transform.localRotation = localRotation;

            var opening = wallRoot.AddComponent<SplitWallOpening>();

            var left = CreateSegment(wallRoot.transform, "Wall_Left", wallPrefab, segmentScaleX, pivotAtRight: true, wallLength * 0.5f);
            var right = CreateSegment(wallRoot.transform, "Wall_Right", wallPrefab, segmentScaleX, pivotAtRight: false, wallLength * 0.5f, wallLength * 0.5f);

            var frameSlot = new GameObject("FrameSlot");
            frameSlot.transform.SetParent(wallRoot.transform, false);

            opening.GetType();
            SetPrivateField(opening, "_wallLeft", left);
            SetPrivateField(opening, "_wallRight", right);
            SetPrivateField(opening, "_frameAnchor", frameSlot.transform);
            SetPrivateField(opening, "_framePrefab", framePrefab);
            SetPrivateField(opening, "_wallLength", wallLength);
            SetPrivateField(opening, "_referenceSegmentLength", wallLength * 0.5f);

            opening.Apply();
            return opening;
        }

        static Transform CreateSegment(
            Transform parent,
            string segmentName,
            GameObject wallPrefab,
            float scaleX,
            bool pivotAtRight,
            float segmentWidth,
            float anchorX = 0f)
        {
            var segmentRoot = new GameObject(segmentName);
            segmentRoot.transform.SetParent(parent, false);

            var mesh = (GameObject)PrefabUtility.InstantiatePrefab(wallPrefab, segmentRoot.transform);
            mesh.name = "Mesh";

            var meshScale = mesh.transform.localScale;
            meshScale.x = scaleX;
            mesh.transform.localScale = meshScale;

            var meshWidth = scaleX * WallMeshMetersPerScaleX;
            mesh.transform.localPosition = pivotAtRight
                ? new Vector3(-meshWidth, mesh.transform.localPosition.y, mesh.transform.localPosition.z)
                : Vector3.zero;

            segmentRoot.transform.localPosition = new Vector3(anchorX, 0f, 0f);
            return segmentRoot.transform;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;

            var parent = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/');
            var leaf = System.IO.Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent);

            AssetDatabase.CreateFolder(parent, leaf);
        }

        static void SetPrivateField(Object target, string fieldName, Object value)
        {
            var field = target.GetType().GetField(fieldName,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            field?.SetValue(target, value);
        }

        static void SetPrivateField(Object target, string fieldName, float value)
        {
            var field = target.GetType().GetField(fieldName,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            field?.SetValue(target, value);
        }
    }
}
#endif
