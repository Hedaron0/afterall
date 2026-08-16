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

        /// <summary>
        /// Prints how dark each room's baked probe field actually gets.
        ///
        /// This is the check on the thing that goes wrong silently: a probe field can be perfectly
        /// valid, apply cleanly, throw no warning, and still describe a room as uniformly lit — at
        /// which point every dropped item reads "average room light" wherever it lies and nothing in
        /// the console says so. The kit measured exactly that before the bake was reworked: room10's
        /// darkest probe of 1092 was 0.33 and room4's darkest of 150 was 0.58.
        ///
        /// Read the min and p05 columns. A room with unlit corners should show a min near zero; a
        /// min that sits close to the median means the field has no darkness left in it.
        /// </summary>
        [MenuItem("AfterAll/Lighting/Report Probe Field Darkness")]
        private static void ReportProbeFieldDarkness()
        {
            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/_AfterAll/Prefabs/Rooms" });
            var report = new System.Text.StringBuilder(
                "[ProbeLightingSetup] Probe field luminance per room (first variant)\n" +
                "room                 grid        probes    min    p05    median   mean     max\n");

            foreach (string guid in guids.OrderBy(g => g))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var data = prefab != null ? prefab.GetComponent<RoomLightProbeData>() : null;
                if (data == null || !data.HasBakedData)
                    continue;

                var serialized = new SerializedObject(data);
                SerializedProperty variants = serialized.FindProperty("_variants");
                if (variants.arraySize == 0)
                    continue;

                SerializedProperty variant = variants.GetArrayElementAtIndex(0);
                Vector3Int dimensions = variant.FindPropertyRelative("dimensions").vector3IntValue;
                SerializedProperty coefficients = variant.FindPropertyRelative("coefficients");

                int probes = coefficients.arraySize / RoomLightProbeData.CoefficientsPerProbe;
                if (probes == 0)
                    continue;

                var luminance = new List<float>(probes);
                float sum = 0f;
                for (int i = 0; i < probes; i++)
                {
                    int b = i * RoomLightProbeData.CoefficientsPerProbe;

                    // L0 only: the flat band is the average light arriving at the probe, which is
                    // what decides whether an object there reads lit or dark.
                    float value =
                        0.2126f * coefficients.GetArrayElementAtIndex(b).floatValue +
                        0.7152f * coefficients.GetArrayElementAtIndex(b + 4).floatValue +
                        0.0722f * coefficients.GetArrayElementAtIndex(b + 8).floatValue;

                    luminance.Add(value);
                    sum += value;
                }

                luminance.Sort();
                report.AppendLine(string.Format(
                    "{0,-20} {1,-11} {2,6}  {3,6:F3} {4,6:F3} {5,7:F3} {6,7:F3} {7,7:F3}",
                    System.IO.Path.GetFileNameWithoutExtension(path),
                    $"{dimensions.x}x{dimensions.y}x{dimensions.z}",
                    probes,
                    luminance[0],
                    luminance[Mathf.Clamp(Mathf.FloorToInt(probes * 0.05f), 0, probes - 1)],
                    luminance[probes / 2],
                    sum / probes,
                    luminance[probes - 1]));
            }

            Debug.Log(report.ToString());
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
