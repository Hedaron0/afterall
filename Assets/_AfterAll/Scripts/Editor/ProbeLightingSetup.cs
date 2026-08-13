using System.Collections.Generic;
using System.Linq;
using AfterAll.Environment;
using AfterAll.Items;
using UnityEditor;
using UnityEngine;

namespace AfterAll.EditorTools
{
    /// <summary>
    /// Wires <see cref="ProbeLitRenderer"/> onto the item prefabs that live outside room prefabs.
    ///
    /// RoomLightmapBaker only reaches renderers inside a room it is baking, so the standalone item
    /// prefabs — the world pickups and the held viewmodels an ItemDefinition points at — were never
    /// covered. Without the component they have no lightmap and no probe source, which is why a
    /// pickup dragged into a dark room kept its colour: nothing was lighting it at all.
    ///
    /// Re-runnable and idempotent; run it after adding a new item prefab.
    /// </summary>
    public static class ProbeLightingSetup
    {
        [MenuItem("AfterAll/Lighting/Wire Probe Lighting To Item Prefabs")]
        private static void WireItemPrefabs()
        {
            var targets = new HashSet<string>();

            // World representations: anything carrying a WorldItem outside the room kit.
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.Contains("/Prefabs/Rooms/"))
                    continue;

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab != null && prefab.GetComponentInChildren<WorldItem>(true) != null)
                    targets.Add(path);
            }

            // Held viewmodels and drop prefabs referenced by item definitions — these often have no
            // WorldItem of their own, so the scan above misses them.
            foreach (string guid in AssetDatabase.FindAssets("t:ItemDefinition"))
            {
                var definition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (definition == null)
                    continue;

                AddPrefabPath(targets, definition.HeldPrefab);
                AddPrefabPath(targets, definition.WorldPickupPrefab);
            }

            int changed = 0;
            foreach (string path in targets.OrderBy(p => p))
            {
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    if (root.GetComponentsInChildren<Renderer>(true).Length == 0)
                        continue;

                    if (root.GetComponent<ProbeLitRenderer>() != null)
                        continue;

                    // One component on the root covering its children: an item is a single object,
                    // and it always moves, so it re-samples as the player carries it around.
                    ProbeLitRenderer lit = root.AddComponent<ProbeLitRenderer>();
                    lit.ConfigureForBake(trackMovement: true, includeChildren: true);

                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    changed++;
                    Debug.Log($"[ProbeLightingSetup] Wired {path}");
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            Debug.Log($"[ProbeLightingSetup] {targets.Count} item prefab(s) inspected, {changed} newly wired.");
        }

        private static void AddPrefabPath(HashSet<string> targets, GameObject prefab)
        {
            if (prefab == null)
                return;

            string path = AssetDatabase.GetAssetPath(prefab);
            if (!string.IsNullOrEmpty(path) && path.EndsWith(".prefab") && !path.Contains("/Prefabs/Rooms/"))
                targets.Add(path);
        }
    }
}
