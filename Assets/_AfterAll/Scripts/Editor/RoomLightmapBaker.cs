using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using AfterAll.Environment;

namespace AfterAll.EditorTools
{
    /// <summary>
    /// Bakes a room prefab's lighting and stores the result on the prefab so it survives runtime
    /// instantiation (see RoomLightmapData).
    ///
    /// A room is baked once per content preset option. The preset's pillars and half-walls have to be
    /// present at bake time — otherwise they receive no light and, worse, cast no shadow onto the
    /// floor, which is what makes props read as glued on rather than standing there. Every option gets
    /// its own bake scene because Lightmapping writes its output next to the scene file and a second
    /// bake into the same scene would overwrite the first option's lightmaps.
    ///
    /// Each room is a closed box, so baking it in isolation loses almost nothing: the only light that
    /// would cross a doorway is a narrow spill the neighbour's own panels cover anyway.
    /// </summary>
    public static class RoomLightmapBaker
    {
        private const string RoomPrefabFolder = "Assets/_AfterAll/Prefabs/Rooms";
        private const string BakeSceneFolder  = "Assets/_AfterAll/Data/RoomLightmaps";
        private const string FinalSettings    = "Assets/_AfterAll/Settings/Lighting/Bake_Final.lighting";

        [MenuItem("AfterAll/Lighting/Bake Room Lightmaps (Selected Prefab)")]
        private static void BakeSelected()
        {
            string path = ResolvePrefabPath();
            if (path == null)
                return;

            string restoreScene = SceneManager.GetActiveScene().path;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            try
            {
                BakeRoom(path);
            }
            finally
            {
                RestoreScene(restoreScene);
            }
        }

        [MenuItem("AfterAll/Lighting/Bake Room Lightmaps (All Rooms)")]
        private static void BakeAll()
        {
            string[] prefabs = AssetDatabase.FindAssets("t:Prefab", new[] { RoomPrefabFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(p => p)
                .ToArray();

            if (prefabs.Length == 0)
            {
                Debug.LogError($"[RoomLightmapBaker] No prefabs found under {RoomPrefabFolder}.");
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    "Bake all room lightmaps",
                    $"Bakes {prefabs.Length} rooms, once per preset option each. This takes a long " +
                    "while (room10 especially) and the editor is blocked throughout.\n\nContinue?",
                    "Bake", "Cancel"))
                return;

            string restoreScene = SceneManager.GetActiveScene().path;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            try
            {
                for (int i = 0; i < prefabs.Length; i++)
                {
                    EditorUtility.DisplayProgressBar(
                        "Baking room lightmaps",
                        $"{Path.GetFileNameWithoutExtension(prefabs[i])} ({i + 1}/{prefabs.Length})",
                        i / (float)prefabs.Length);
                    BakeRoom(prefabs[i]);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                RestoreScene(restoreScene);
            }
        }

        private static void BakeRoom(string prefabPath)
        {
            string roomName = Path.GetFileNameWithoutExtension(prefabPath);

            if (!AssetDatabase.IsValidFolder(BakeSceneFolder))
                AssetDatabase.CreateFolder("Assets/_AfterAll/Data", "RoomLightmaps");

            string[] presetNames = GetPresetOptionNames(prefabPath);
            var baked = new List<BakedVariant>(presetNames.Length);
            LightmapsMode mode = LightmapsMode.NonDirectional;

            foreach (string presetName in presetNames)
            {
                string suffix    = string.IsNullOrEmpty(presetName) ? string.Empty : "_" + presetName;
                string scenePath = $"{BakeSceneFolder}/{roomName}_Bake{suffix}.unity";

                Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                ConfigureBakeEnvironment();

                var prefab   = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                ActivatePreset(instance.transform, presetName);
                OpenDoorwaysForBake(instance.transform);

                // Lightmapping writes its output next to a saved scene, so the scene has to exist on
                // disk before Bake() rather than after.
                EditorSceneManager.SaveScene(scene, scenePath);

                var settings = AssetDatabase.LoadAssetAtPath<LightingSettings>(FinalSettings);
                if (settings != null)
                    Lightmapping.SetLightingSettingsForScene(scene, settings);

                Lightmapping.Bake();
                mode = LightmapSettings.lightmapsMode;

                BakedVariant captured = CaptureVariant(instance, presetName);
                if (captured != null)
                    baked.Add(captured);
                else
                    Debug.LogWarning($"[RoomLightmapBaker] {roomName} preset '{presetName}': " +
                                     "bake produced no lightmapped renderers.");

                EditorSceneManager.SaveScene(scene, scenePath);
            }

            if (baked.Count == 0)
            {
                Debug.LogError($"[RoomLightmapBaker] {roomName}: nothing stored. Is Contribute GI set " +
                               "on the shell? Run 'Combine Static Shell - Apply' first.");
                return;
            }

            StoreOnPrefab(prefabPath, baked, mode);
            // (OpenDoorwaysForBake only touched the throwaway scene instances, never the prefab.)
            Debug.Log($"[RoomLightmapBaker] {roomName}: stored {baked.Count} lightmap variant(s) " +
                      $"[{string.Join(", ", baked.Select(b => string.IsNullOrEmpty(b.Variant.presetName) ? "<none>" : b.Variant.presetName))}].");
        }

        /// <summary>A finished bake plus the hierarchy paths its renderers had in the bake scene.</summary>
        private class BakedVariant
        {
            public RoomLightmapData.Variant Variant;
            public string[] RendererPaths;
        }

        /// <summary>Preset option names, or a single empty entry when the room has no preset group.</summary>
        private static string[] GetPresetOptionNames(string prefabPath)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                Transform preset = root.transform.Find("Content/Preset");
                if (preset == null || preset.childCount == 0)
                    return new[] { string.Empty };

                var names = new string[preset.childCount];
                for (int i = 0; i < preset.childCount; i++)
                    names[i] = preset.GetChild(i).name;
                return names;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>Enables exactly the named preset option, matching what WeightedRandomGroup does at runtime.</summary>
        private static void ActivatePreset(Transform root, string presetName)
        {
            Transform preset = root.Find("Content/Preset");
            if (preset == null)
                return;

            foreach (Transform option in preset)
                option.gameObject.SetActive(option.name == presetName);
        }

        private static BakedVariant CaptureVariant(GameObject instance, string presetName)
        {
            LightmapData[] sceneLightmaps = LightmapSettings.lightmaps;

            var paths         = new List<string>();
            var localIndices  = new List<int>();
            var scaleOffsets  = new List<Vector4>();
            var usedLightmaps = new List<int>();

            foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                int sceneIndex = renderer.lightmapIndex;
                if (sceneIndex < 0 || sceneIndex >= sceneLightmaps.Length)
                    continue;

                int local = usedLightmaps.IndexOf(sceneIndex);
                if (local < 0)
                {
                    usedLightmaps.Add(sceneIndex);
                    local = usedLightmaps.Count - 1;
                }

                paths.Add(GetHierarchyPath(renderer.transform, instance.transform));
                localIndices.Add(local);
                scaleOffsets.Add(renderer.lightmapScaleOffset);
            }

            if (paths.Count == 0)
                return null;

            return new BakedVariant
            {
                RendererPaths = paths.ToArray(),
                Variant = new RoomLightmapData.Variant
                {
                    presetName           = presetName,
                    // renderers are rebound against the prefab in StoreOnPrefab
                    lightmapIndices      = localIndices.ToArray(),
                    lightmapScaleOffsets = scaleOffsets.ToArray(),
                    lightmapColors       = usedLightmaps.Select(i => sceneLightmaps[i].lightmapColor).ToArray(),
                    lightmapDirs         = usedLightmaps.Select(i => sceneLightmaps[i].lightmapDir).ToArray(),
                    shadowMasks          = usedLightmaps.Select(i => sceneLightmaps[i].shadowMask).ToArray(),
                },
            };
        }

        private static void StoreOnPrefab(string prefabPath, List<BakedVariant> baked, LightmapsMode mode)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                foreach (BakedVariant entry in baked)
                {
                    RoomLightmapData.Variant variant = entry.Variant;
                    string[] paths = entry.RendererPaths;
                    var renderers = new List<Renderer>(paths.Length);
                    var indices   = new List<int>(paths.Length);
                    var offsets   = new List<Vector4>(paths.Length);

                    for (int i = 0; i < paths.Length; i++)
                    {
                        Transform target = string.IsNullOrEmpty(paths[i])
                            ? root.transform
                            : root.transform.Find(paths[i]);

                        if (target == null || !target.TryGetComponent(out Renderer renderer))
                        {
                            Debug.LogWarning($"[RoomLightmapBaker] {Path.GetFileName(prefabPath)}: could not " +
                                             $"rebind baked renderer '{paths[i]}' onto the prefab.");
                            continue;
                        }

                        renderers.Add(renderer);
                        indices.Add(variant.lightmapIndices[i]);
                        offsets.Add(variant.lightmapScaleOffsets[i]);
                    }

                    variant.renderers            = renderers.ToArray();
                    variant.lightmapIndices      = indices.ToArray();
                    variant.lightmapScaleOffsets = offsets.ToArray();
                }

                var data = root.GetComponent<RoomLightmapData>() ?? root.AddComponent<RoomLightmapData>();
                data.StoreVariants(baked.Select(b => b.Variant).ToArray(), mode);

                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>
        /// Dark, skybox-free environment: rooms are enclosed and should be lit by their own panels.
        /// A default skybox would pour daylight through every doorway and open wall and wash the bake
        /// out. Runtime ambient is a separate setting and still lights non-baked geometry.
        /// </summary>
        /// <summary>
        /// Cuts a centred doorway into every StandardGap wall before the bake.
        ///
        /// A door wall is two pieces, WallLeft and WallRight, and in the prefab they sit flush against
        /// each other forming one solid slab. Baking that state lights the faces looking into the room
        /// but leaves the faces where the two pieces meet buried inside the slab, so they bake pure
        /// black. At runtime WallGapController slides the pieces apart along the wall axis to open the
        /// doorway — and those buried faces are exactly what becomes the doorway's side reveal. Hence
        /// the black jambs framing every opening, worse or better per room depending on which wall the
        /// bake came from.
        ///
        /// Opening the gap here exposes those faces to the room's own light instead. The offset is
        /// centred rather than random on purpose: at runtime the pieces get scaled and repositioned to
        /// wherever the real gap lands, but that only stretches the existing uv2 mapping (the same
        /// stretch the wallpaper already rides), so the reveal keeps the one light value it was baked
        /// with wherever the doorway ends up.
        ///
        /// FullWall and OpenEnd walls are deliberately left closed: opening those hides the pieces'
        /// renderers entirely, so they would be captured with no lightmap at all and then show up
        /// unlit on any floor where that wall ends up unconnected and therefore visible.
        /// </summary>
        private static void OpenDoorwaysForBake(Transform root)
        {
            int opened = 0;
            foreach (WallGapController wall in root.GetComponentsInChildren<WallGapController>(true))
            {
                if (wall.OpeningMode != WallOpeningMode.StandardGap)
                    continue;

                wall.ConfigureOpening(true, WallGapController.GetWallCenterGapOffset(wall));
                opened++;
            }

            if (opened > 0)
                Debug.Log($"[RoomLightmapBaker] {root.name}: opened {opened} doorway(s) for the bake so " +
                          "the reveal faces receive light.");
        }

        private static void ConfigureBakeEnvironment()
        {
            RenderSettings.skybox                  = null;
            RenderSettings.ambientMode             = AmbientMode.Flat;
            RenderSettings.ambientLight            = new Color(0.02f, 0.02f, 0.025f, 1f);
            RenderSettings.ambientIntensity        = 1f;
            RenderSettings.defaultReflectionMode   = DefaultReflectionMode.Custom;
            RenderSettings.customReflectionTexture = null;
        }

        private static string GetHierarchyPath(Transform target, Transform root)
        {
            if (target == root)
                return string.Empty;

            var parts = new List<string>();
            for (Transform t = target; t != null && t != root; t = t.parent)
                parts.Add(t.name);

            parts.Reverse();
            return string.Join("/", parts);
        }

        private static void RestoreScene(string scenePath)
        {
            if (!string.IsNullOrEmpty(scenePath) && File.Exists(scenePath))
                EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            else
                EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        }

        private static string ResolvePrefabPath()
        {
            // See RoomStaticMeshCombiner.ResolvePrefabPath: Prefab Mode's selection doesn't resolve via
            // the instance/asset paths below, and isn't needed anyway — BakeRoom loads its own copy.
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null && !string.IsNullOrEmpty(stage.assetPath))
                return stage.assetPath;

            GameObject selected = Selection.activeGameObject;
            if (selected == null)
            {
                Debug.LogError("[RoomLightmapBaker] Select a room prefab in the Project window first.");
                return null;
            }

            string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(selected);
            if (string.IsNullOrEmpty(path))
                path = AssetDatabase.GetAssetPath(selected);

            if (string.IsNullOrEmpty(path) || !path.EndsWith(".prefab"))
            {
                Debug.LogError("[RoomLightmapBaker] Could not resolve a .prefab asset from the selection " +
                                "(Project window, Hierarchy instance, or Prefab Mode).");
                return null;
            }

            return path;
        }
    }
}
