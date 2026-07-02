using System.Collections.Generic;
using AfterAll.Environment;
using UnityEditor;
using UnityEngine;

namespace AfterAll.Editor
{
    public static class RoomPrefabCleanupEditor
    {
        private const string DefaultRoomPrefabSearchPath = "Assets/_AfterAll/Prefabs/Rooms";
        private static readonly int IgnoreRaycastLayer = LayerMask.NameToLayer("Ignore Raycast");

        [MenuItem("AfterAll/Generation/Disable Decorative Cube Colliders (Room Prefabs)")]
        private static void DisableDecorativeCubeColliders()
        {
            List<string> prefabPaths = CollectTargetPrefabPaths();
            if (prefabPaths.Count == 0)
            {
                Debug.LogWarning(
                    $"[RoomPrefabCleanup] No target prefabs found under {DefaultRoomPrefabSearchPath}.");
                return;
            }

            int prefabsChanged = 0;
            int cubesUpdated = 0;
            int collidersDisabled = 0;

            foreach (string path in prefabPaths)
            {
                GameObject prefabRoot = PrefabUtility.LoadPrefabContents(path);
                if (prefabRoot == null)
                    continue;

                bool changed = false;
                foreach (Transform child in prefabRoot.GetComponentsInChildren<Transform>(true))
                {
                    if (!child.name.StartsWith("Cube"))
                        continue;

                    cubesUpdated++;
                    if (IgnoreRaycastLayer >= 0)
                    {
                        child.gameObject.layer = IgnoreRaycastLayer;
                        changed = true;
                    }

                    Collider[] colliders = child.GetComponentsInChildren<Collider>(true);
                    foreach (Collider collider in colliders)
                    {
                        if (collider == null || !collider.enabled)
                            continue;

                        collider.enabled = false;
                        collidersDisabled++;
                        changed = true;
                    }
                }

                if (changed)
                {
                    PrefabUtility.SaveAsPrefabAsset(prefabRoot, path);
                    prefabsChanged++;
                }

                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log(
                $"[RoomPrefabCleanup] PrefabsChanged={prefabsChanged}, CubesProcessed={cubesUpdated}, CollidersDisabled={collidersDisabled}");
        }

        private static List<string> CollectTargetPrefabPaths()
        {
            var paths = new HashSet<string>();
            Object[] selected = Selection.objects;
            foreach (Object obj in selected)
            {
                string path = AssetDatabase.GetAssetPath(obj);
                if (!string.IsNullOrEmpty(path) && path.EndsWith(".prefab"))
                    paths.Add(path);
            }

            if (paths.Count > 0)
                return new List<string>(paths);

            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { DefaultRoomPrefabSearchPath });
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(path) && path.EndsWith(".prefab"))
                    paths.Add(path);
            }

            return new List<string>(paths);
        }
    }
}
