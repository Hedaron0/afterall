using System.Collections.Generic;
using AfterAll.Environment;
using UnityEditor;
using UnityEngine;

namespace AfterAll.Editor
{
    public static class SocketContractMigrationEditor
    {
        private const string DefaultRoomPrefabSearchPath = "Assets/_AfterAll/Prefabs/Rooms";

        [MenuItem("AfterAll/Generation/Infer Socket Contracts (Selected Prefabs)")]
        private static void InferSocketContractsOnSelectedPrefabs()
        {
            List<string> prefabPaths = CollectTargetPrefabPaths();
            if (prefabPaths.Count == 0)
            {
                Debug.LogWarning(
                    $"[SocketContractMigration] No target prefabs found. Select prefabs or add room prefabs under {DefaultRoomPrefabSearchPath}.");
                return;
            }

            int processedPrefabs = 0;
            int processedWalls = 0;
            int updatedSockets = 0;
            int wallsWithoutMesh = 0;
            int duplicateDirections = 0;

            foreach (string path in prefabPaths)
            {
                GameObject prefabRoot = PrefabUtility.LoadPrefabContents(path);
                if (prefabRoot == null)
                    continue;

                processedPrefabs++;
                bool changed = false;
                var seenContracts = new HashSet<string>();

                RoomInstance room = prefabRoot.GetComponent<RoomInstance>() ?? prefabRoot.AddComponent<RoomInstance>();
                room.CacheWalls();

                WallGapController[] walls = prefabRoot.GetComponentsInChildren<WallGapController>(true);
                if (walls.Length == 0)
                {
                    Debug.LogWarning($"[SocketContractMigration] No WallGapController found in {path}");
                    PrefabUtility.UnloadPrefabContents(prefabRoot);
                    continue;
                }

                foreach (WallGapController wall in walls)
                {
                    processedWalls++;
                    if (!wall.BakeSocketContract())
                    {
                        wallsWithoutMesh++;
                        Debug.LogWarning($"[SocketContractMigration] Could not bake socket on {path}/{wall.name}");
                        continue;
                    }

                    if (!wall.TryGetBakedSocket(out RoomSocket socket))
                        continue;

                    updatedSockets++;
                    changed = true;

                    if (!seenContracts.Add(socket.DebugContractLabel()))
                    {
                        duplicateDirections++;
                        Debug.LogWarning(
                            $"[SocketContractMigration] Duplicate contract {socket.DebugContractLabel()} in {path}. Manual review recommended.");
                    }
                }

                if (changed)
                    PrefabUtility.SaveAsPrefabAsset(prefabRoot, path);

                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log(
                $"[SocketContractMigration] Prefabs={processedPrefabs}, Walls={processedWalls}, UpdatedSockets={updatedSockets}, " +
                $"BakeFailed={wallsWithoutMesh}, DuplicateDirectionWarnings={duplicateDirections}");
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
