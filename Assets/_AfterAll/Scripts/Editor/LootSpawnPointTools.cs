using System.Collections.Generic;
using System.Linq;
using AfterAll.Environment;
using UnityEditor;
using UnityEngine;

namespace AfterAll.EditorTools
{
    /// <summary>
    /// Authoring tools for <see cref="LootSpawnPoint"/>. There will be dozens of these per room and
    /// eleven rooms and counting, so placing one has to cost a keystroke, not a transform edit.
    ///
    /// The expensive part of placing a marker by hand is the Y: getting it to sit just above a desk
    /// means eyeballing a number in the inspector. So the tool reads it off the geometry instead —
    /// select the desk, press the shortcut, and the marker lands on its top face.
    /// </summary>
    public static class LootSpawnPointTools
    {
        private const string PointsParentName = "LootPoints";
        private const string PointNamePrefix = "LootPoint_";

        /// <summary>
        /// Puts a marker on top of every selected object. Multi-select works, so a whole desk row is
        /// one press. With nothing selected, drops one at the scene view's focus point instead.
        /// </summary>
        [MenuItem("AfterAll/Loot/Add Spawn Point On Selection %#l")]
        private static void AddOnSelection()
        {
            GameObject[] selection = Selection.gameObjects;

            if (selection.Length == 0)
            {
                CreateAtSceneViewPivot();
                return;
            }

            var created = new List<GameObject>();

            foreach (GameObject target in selection)
            {
                // A marker parented to the prop would move and scale with it, and scaled markers put
                // items in the wrong place. Siblings under a shared parent stay predictable.
                Transform parent = ResolveParent(target.transform);
                Vector3 position = TopOf(target);

                GameObject point = CreatePoint(parent, position);
                created.Add(point);
            }

            if (created.Count > 0)
            {
                Selection.objects = created.ToArray();
                Debug.Log($"[Loot] Added {created.Count} spawn point(s).");
            }
        }

        /// <summary>Reports rooms whose authored points cannot cover the loot they may be asked for,
        /// and rooms with no points at all — the two ways this system fails silently.</summary>
        [MenuItem("AfterAll/Loot/Validate Spawn Points In Room Prefabs")]
        private static void Validate()
        {
            var report = new System.Text.StringBuilder("[Loot] Spawn point coverage\n");

            foreach (string guid in AssetDatabase
                         .FindAssets("t:Prefab", new[] { "Assets/_AfterAll/Prefabs/Rooms" })
                         .OrderBy(g => g))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                    continue;

                LootSpawnPoint[] all = prefab.GetComponentsInChildren<LootSpawnPoint>(true);
                string room = System.IO.Path.GetFileNameWithoutExtension(path);

                if (all.Length == 0)
                {
                    report.AppendLine($"  {room,-14} no spawn points — this room will never hold loot");
                    continue;
                }

                // Per preset, because only one preset's points are ever live at once and a preset
                // with none is the case that looks fine in the prefab and spawns nothing in play.
                Transform presetRoot = prefab.transform.Find("Content/Preset");
                var perPreset = new List<string>();

                if (presetRoot != null)
                {
                    for (int i = 0; i < presetRoot.childCount; i++)
                    {
                        Transform preset = presetRoot.GetChild(i);
                        int count = preset.GetComponentsInChildren<LootSpawnPoint>(true).Length;
                        perPreset.Add($"{preset.name}:{count}");
                    }
                }

                int shared = all.Length - (presetRoot != null
                    ? presetRoot.GetComponentsInChildren<LootSpawnPoint>(true).Length
                    : 0);

                string presets = perPreset.Count > 0 ? string.Join(" ", perPreset) : "no presets";
                string warning = perPreset.Any(p => p.EndsWith(":0")) ? "   <-- a preset has none" : string.Empty;

                report.AppendLine(
                    $"  {room,-14} {all.Length,3} total | per preset {presets} | shared {shared}{warning}");
            }

            Debug.Log(report.ToString());
        }

        private static void CreateAtSceneViewPivot()
        {
            SceneView view = SceneView.lastActiveSceneView;
            if (view == null)
            {
                Debug.LogWarning("[Loot] Select an object to place a spawn point on, or open a Scene view.");
                return;
            }

            GameObject point = CreatePoint(null, view.pivot);
            Selection.activeGameObject = point;
        }

        /// <summary>
        /// Top face of the object's renderers, which is where something resting on it would sit.
        /// Falls back to the transform for objects with no renderer of their own (an empty grouping a
        /// drawer's contents, say).
        /// </summary>
        private static Vector3 TopOf(GameObject target)
        {
            Renderer[] renderers = target.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                return target.transform.position;

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            return new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);
        }

        /// <summary>
        /// Finds or makes the LootPoints child of whichever preset (or content root) the target lives
        /// under, so markers collect in one place per preset rather than scattering through the prop
        /// hierarchy where they are impossible to review.
        /// </summary>
        private static Transform ResolveParent(Transform target)
        {
            Transform preset = FindPresetAncestor(target) ?? target.parent;
            if (preset == null)
                return null;

            Transform existing = preset.Find(PointsParentName);
            if (existing != null)
                return existing;

            var go = new GameObject(PointsParentName);
            Undo.RegisterCreatedObjectUndo(go, "Create Loot Points Parent");
            go.transform.SetParent(preset, false);
            return go.transform;
        }

        /// <summary>The numbered child of Content/Preset that this transform sits under, if any.</summary>
        private static Transform FindPresetAncestor(Transform target)
        {
            for (Transform t = target; t != null; t = t.parent)
            {
                if (t.parent != null && t.parent.name == "Preset")
                    return t;
            }

            return null;
        }

        private static GameObject CreatePoint(Transform parent, Vector3 worldPosition)
        {
            var go = new GameObject(NextName(parent));
            Undo.RegisterCreatedObjectUndo(go, "Create Loot Spawn Point");

            if (parent != null)
                go.transform.SetParent(parent, false);

            go.transform.position = worldPosition;
            go.AddComponent<LootSpawnPoint>();
            return go;
        }

        /// <summary>Numbers within the parent so names stay stable and sortable however many get
        /// added later — deleting one does not renumber the rest, which would churn the prefab.</summary>
        private static string NextName(Transform parent)
        {
            if (parent == null)
                return PointNamePrefix + "01";

            int highest = 0;
            for (int i = 0; i < parent.childCount; i++)
            {
                string name = parent.GetChild(i).name;
                if (name.StartsWith(PointNamePrefix) &&
                    int.TryParse(name.Substring(PointNamePrefix.Length), out int number))
                    highest = Mathf.Max(highest, number);
            }

            return PointNamePrefix + (highest + 1).ToString("00");
        }
    }
}
