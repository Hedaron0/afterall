using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using AfterAll.Environment;
using AfterAll.Items;

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

        /// <summary>
        /// Horizontal probe spacing. This is the size of the smallest dark patch the field can
        /// describe, so it is what decides whether an object lying in an unlit corner reads dark.
        ///
        /// It was 4m, which put room10's 5000m² floor on a 26x14 lattice — measured, its darkest
        /// probe of 1092 was 0.33 and room4's darkest of 150 was 0.58, i.e. the field contained no
        /// darkness anywhere in the kit and every dynamic object read "average room light" wherever
        /// it stood. Halving it roughly doubles the probe count (kit ~2.2k → ~4.7k, still ~230KB)
        /// and costs bake time, which is the trade being made deliberately.
        /// </summary>
        private const float ProbeSpacingM = 2.5f;

        /// <summary>Keeps the outermost probes off the walls, so trilinear sampling near a wall never
        /// blends in a probe sitting inside (or outside) the shell, where the bake is black.</summary>
        private const float ProbeWallInsetM = 0.8f;

        /// <summary>Height of the lowest probe layer above the walkable floor. Loot lies ON the floor
        /// and samples get clamped to this layer, so this is the height a dropped item is really lit
        /// at — keep it low, but far enough up that <see cref="ProbeBuriedRadiusM"/> does not touch
        /// the floor collider and report every ground-level probe as buried.</summary>
        private const float ProbeFloorOffsetM = 0.3f;

        /// <summary>Clearance kept below the top of the room's bounds. The bounds top is the OUTSIDE of
        /// the ceiling slab, so the layers are pulled down far enough to stay in open air rather than
        /// inside the slab, where the bake is black.</summary>
        private const float ProbeCeilingInsetM = 0.9f;

        /// <summary>Vertical spacing between probe layers. Three fixed layers stretched to fit meant a
        /// tall room sampled floor light from a probe a metre and a half up.</summary>
        private const float ProbeVerticalSpacingM = 1.5f;

        /// <summary>Never lay fewer than this many layers, however low the ceiling.</summary>
        private const int MinProbeLayers = 3;

        /// <summary>Probe is treated as sitting inside solid geometry if anything overlaps this radius.
        /// Small on purpose: it must clear the floor from <see cref="ProbeFloorOffsetM"/> while still
        /// catching a probe swallowed by a pillar or partition, which are far thicker than this.</summary>
        private const float ProbeBuriedRadiusM = 0.15f;

        /// <summary>Matches RoomStaticMeshCombiner's generated shell child.</summary>
        private const string CombinedChildName = "CombinedStatic";

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
            var probeVariants = new List<RoomLightProbeData.Variant>(presetNames.Length);
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
                SetDoorWallsToProbeLighting(instance.transform);

                // The instance sits at the origin unrotated, so its local space and the bake scene's
                // world space are the same thing — probe positions can be used as either.
                ProbeGrid grid = BuildProbeGrid(instance);
                CreateProbeGroup(grid);

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

                RoomLightProbeData.Variant probes = CaptureProbeVariant(grid, presetName);
                if (probes != null)
                    probeVariants.Add(probes);
                else
                    Debug.LogWarning($"[RoomLightmapBaker] {roomName} preset '{presetName}': " +
                                     "bake produced no light probes — dynamic props will stay unlit.");

                EditorSceneManager.SaveScene(scene, scenePath);
            }

            if (baked.Count == 0)
            {
                Debug.LogError($"[RoomLightmapBaker] {roomName}: nothing stored. Is Contribute GI set " +
                               "on the shell? Run 'Combine Static Shell - Apply' first.");
                return;
            }

            StoreOnPrefab(prefabPath, baked, mode, probeVariants);
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

        /// <summary>A regular probe lattice over one room, in bake-scene space (== room-local).</summary>
        private class ProbeGrid
        {
            public Vector3 Origin;
            public Vector3 CellSize;
            public Vector3Int Dimensions;

            public int Count => Dimensions.x * Dimensions.y * Dimensions.z;

            public Vector3 PositionAt(int x, int y, int z) => Origin + new Vector3(
                CellSize.x * x, CellSize.y * y, CellSize.z * z);
        }

        /// <summary>
        /// Lays a lattice across the room's interior: <see cref="ProbeSpacingM"/> horizontally, three
        /// height layers, inset from the shell so no probe ends up inside or behind a wall where the
        /// bake is black (a single such probe would drag every nearby object dark through trilinear
        /// interpolation).
        /// </summary>
        private static ProbeGrid BuildProbeGrid(GameObject instance)
        {
            Bounds bounds = default;
            bool any = false;
            foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer.enabled)
                    continue;

                if (!any)
                {
                    bounds = renderer.bounds;
                    any = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            if (!any)
                bounds = new Bounds(instance.transform.position, Vector3.one * 4f);

            float floorY = instance.TryGetComponent(out RoomInstance room)
                ? room.GetWalkableFloorY()
                : bounds.min.y;

            float minY = floorY + ProbeFloorOffsetM;
            float maxY = Mathf.Max(minY, bounds.max.y - ProbeCeilingInsetM);

            float minX = bounds.min.x + ProbeWallInsetM;
            float maxX = bounds.max.x - ProbeWallInsetM;
            float minZ = bounds.min.z + ProbeWallInsetM;
            float maxZ = bounds.max.z - ProbeWallInsetM;

            // A room narrower than two insets collapses to a single column rather than inverting.
            if (maxX < minX)
                minX = maxX = bounds.center.x;
            if (maxZ < minZ)
                minZ = maxZ = bounds.center.z;

            int countX = Mathf.Max(2, Mathf.CeilToInt((maxX - minX) / ProbeSpacingM) + 1);
            int countZ = Mathf.Max(2, Mathf.CeilToInt((maxZ - minZ) / ProbeSpacingM) + 1);
            int countY = Mathf.Max(MinProbeLayers,
                Mathf.CeilToInt((maxY - minY) / ProbeVerticalSpacingM) + 1);

            return new ProbeGrid
            {
                Origin = new Vector3(minX, minY, minZ),
                CellSize = new Vector3(
                    (maxX - minX) / (countX - 1),
                    (maxY - minY) / (countY - 1),
                    (maxZ - minZ) / (countZ - 1)),
                Dimensions = new Vector3Int(countX, countY, countZ),
            };
        }

        /// <summary>Adds the LightProbeGroup the bake needs — without one Unity bakes no probes at all.</summary>
        private static void CreateProbeGroup(ProbeGrid grid)
        {
            var positions = new Vector3[grid.Count];
            int i = 0;
            for (int z = 0; z < grid.Dimensions.z; z++)
            for (int y = 0; y < grid.Dimensions.y; y++)
            for (int x = 0; x < grid.Dimensions.x; x++)
                positions[i++] = grid.PositionAt(x, y, z);

            var go = new GameObject("BakeProbes");
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            go.AddComponent<LightProbeGroup>().probePositions = positions;
        }

        /// <summary>
        /// Reads the baked field back out at exactly the lattice points, flattening L0+L1 into the
        /// layout RoomLightProbeData re-interpolates at runtime. Sampling through
        /// GetInterpolatedProbe rather than reading LightProbes.bakedProbes directly keeps this
        /// independent of how Unity ordered or tetrahedralised the group.
        /// </summary>
        private static RoomLightProbeData.Variant CaptureProbeVariant(ProbeGrid grid, string presetName)
        {
            if (LightmapSettings.lightProbes == null || LightmapSettings.lightProbes.count == 0)
                return null;

            var coefficients = new float[grid.Count * RoomLightProbeData.CoefficientsPerProbe];

            for (int z = 0; z < grid.Dimensions.z; z++)
            for (int y = 0; y < grid.Dimensions.y; y++)
            for (int x = 0; x < grid.Dimensions.x; x++)
            {
                LightProbes.GetInterpolatedProbe(grid.PositionAt(x, y, z), null, out SphericalHarmonicsL2 sh);

                int probe = x + grid.Dimensions.x * (y + grid.Dimensions.y * z);
                int start = probe * RoomLightProbeData.CoefficientsPerProbe;
                for (int channel = 0; channel < 3; channel++)
                for (int k = 0; k < 4; k++)
                    coefficients[start + channel * 4 + k] = sh[channel, k];
            }

            int repaired = RepairDeadProbes(grid.Dimensions, coefficients, FindBuriedProbes(grid));
            if (repaired > 0)
                Debug.Log($"[RoomLightmapBaker] preset '{presetName}': repaired {repaired} of " +
                          $"{grid.Count} probe(s) that landed inside geometry.");

            return new RoomLightProbeData.Variant
            {
                presetName  = presetName,
                originLocal = grid.Origin,
                cellSize    = grid.CellSize,
                dimensions  = grid.Dimensions,
                coefficients = coefficients,
            };
        }

        /// <summary>Below this L0 luminance a probe has effectively no light on it.</summary>
        private const float DeadProbeLuminance = 0.01f;

        /// <summary>
        /// Marks the probes that sit inside solid geometry, by asking physics rather than by looking
        /// at how dark they baked.
        ///
        /// This is the half of the buried-probe test that used to be missing, and its absence is why
        /// the kit ended up with no dark places in it: darkness and burial look identical in the
        /// coefficients (both are black), so a luminance-only test cannot tell a probe stuck inside a
        /// pillar from a probe standing in an unlit corner, and the repair flood-filled both.
        ///
        /// Colliders are live here — RoomStaticMeshCombiner disables the original Renderers but not
        /// their GameObjects, precisely so collision survives the combine.
        /// </summary>
        private static bool[] FindBuriedProbes(ProbeGrid grid)
        {
            // Edit-mode physics does not auto-sync, and everything in this scene was moved into place
            // moments ago (preset activation, RefreshOpenWalls), so the query would otherwise run
            // against stale collider poses.
            Physics.SyncTransforms();

            var buried = new bool[grid.Count];
            for (int z = 0; z < grid.Dimensions.z; z++)
            for (int y = 0; y < grid.Dimensions.y; y++)
            for (int x = 0; x < grid.Dimensions.x; x++)
            {
                int index = x + grid.Dimensions.x * (y + grid.Dimensions.y * z);
                buried[index] = Physics.CheckSphere(
                    grid.PositionAt(x, y, z), ProbeBuriedRadiusM, ~0, QueryTriggerInteraction.Ignore);
            }

            return buried;
        }

        /// <summary>
        /// Replaces probes that baked black because they landed inside solid geometry.
        ///
        /// The lattice is regular, so some probes inevitably end up inside an interior partition, a
        /// pillar, or a preset prop — measured: room10 and room1 both contain probes at 0.000 despite
        /// carrying 231 and 12 panels. Those are not dark corners, they are probes with no line of
        /// sight to anything. Sampling near one drags the interpolation toward zero, which is what
        /// made an object dropped in certain spots go instantly pitch black while its neighbours a
        /// metre away looked fine.
        ///
        /// The lattice cannot simply lose those entries — the runtime indexes it as a dense grid — so
        /// each dead probe is flood-filled from its live 6-neighbours instead, repeatedly, until the
        /// whole field is live. That is the standard fix for buried probes and it also softens the
        /// field, since a repaired probe is by construction the average of what surrounds it.
        ///
        /// A probe is only repaired when it is black AND <paramref name="buried"/> says geometry is
        /// sitting on it. Black-but-in-open-air is a genuinely unlit corner: leaving it alone is the
        /// whole point, because that darkness is what makes a dropped item disappear into an unlit
        /// part of the floor instead of glowing there. An earlier version keyed off luminance alone
        /// and therefore erased every dark spot in the kit.
        /// </summary>
        internal static int RepairDeadProbes(Vector3Int dimensions, float[] coefficients, bool[] buried)
        {
            int count = dimensions.x * dimensions.y * dimensions.z;
            var alive = new bool[count];
            int aliveCount = 0;

            for (int i = 0; i < count; i++)
            {
                int b = i * RoomLightProbeData.CoefficientsPerProbe;
                float luminance = 0.2126f * coefficients[b] +
                                  0.7152f * coefficients[b + 4] +
                                  0.0722f * coefficients[b + 8];
                alive[i] = luminance >= DeadProbeLuminance || !buried[i];
                if (alive[i])
                    aliveCount++;
            }

            int dead = count - aliveCount;
            if (dead == 0 || aliveCount == 0)
            {
                if (aliveCount == 0)
                    Debug.LogWarning("[RoomLightmapBaker] Every probe in this room baked black — the " +
                                     "grid may be outside the room, or the room has no lights.");
                return 0;
            }

            var accumulator = new float[RoomLightProbeData.CoefficientsPerProbe];

            // Bounded by the grid diameter: each pass grows the live region by one cell.
            int maxPasses = dimensions.x + dimensions.y + dimensions.z;
            for (int pass = 0; pass < maxPasses && aliveCount < count; pass++)
            {
                var filledThisPass = new System.Collections.Generic.List<int>();

                for (int z = 0; z < dimensions.z; z++)
                for (int y = 0; y < dimensions.y; y++)
                for (int x = 0; x < dimensions.x; x++)
                {
                    int index = x + dimensions.x * (y + dimensions.y * z);
                    if (alive[index])
                        continue;

                    System.Array.Clear(accumulator, 0, accumulator.Length);
                    int contributors = 0;

                    AccumulateNeighbour(dimensions, coefficients, alive, x - 1, y, z, accumulator, ref contributors);
                    AccumulateNeighbour(dimensions, coefficients, alive, x + 1, y, z, accumulator, ref contributors);
                    AccumulateNeighbour(dimensions, coefficients, alive, x, y - 1, z, accumulator, ref contributors);
                    AccumulateNeighbour(dimensions, coefficients, alive, x, y + 1, z, accumulator, ref contributors);
                    AccumulateNeighbour(dimensions, coefficients, alive, x, y, z - 1, accumulator, ref contributors);
                    AccumulateNeighbour(dimensions, coefficients, alive, x, y, z + 1, accumulator, ref contributors);

                    if (contributors == 0)
                        continue;

                    int b = index * RoomLightProbeData.CoefficientsPerProbe;
                    for (int c = 0; c < RoomLightProbeData.CoefficientsPerProbe; c++)
                        coefficients[b + c] = accumulator[c] / contributors;

                    filledThisPass.Add(index);
                }

                if (filledThisPass.Count == 0)
                    break;

                // Marked alive only after the pass, so a probe filled this pass cannot feed another
                // one in the same pass — that would smear one value across a whole buried region.
                foreach (int index in filledThisPass)
                {
                    alive[index] = true;
                    aliveCount++;
                }
            }

            return dead;
        }

        private static void AccumulateNeighbour(
            Vector3Int dimensions, float[] coefficients, bool[] alive,
            int x, int y, int z, float[] accumulator, ref int contributors)
        {
            if (x < 0 || y < 0 || z < 0 ||
                x >= dimensions.x || y >= dimensions.y || z >= dimensions.z)
                return;

            int index = x + dimensions.x * (y + dimensions.y * z);
            if (!alive[index])
                return;

            int b = index * RoomLightProbeData.CoefficientsPerProbe;
            for (int c = 0; c < RoomLightProbeData.CoefficientsPerProbe; c++)
                accumulator[c] += coefficients[b + c];

            contributors++;
        }

        /// <summary>
        /// Gives every renderer the bake left out of the lightmap a <see cref="ProbeLitRenderer"/>, so
        /// it reads the room's probe field instead of falling back to near-black ambient.
        ///
        /// "Left out of the lightmap" is taken from the bake result itself — the union of renderer
        /// paths across every preset variant — rather than by re-deriving the combiner's
        /// contribute-GI rules here. That way the two can never drift: whatever ApplyGiFlags decides
        /// not to bake automatically becomes probe-lit, including door walls now that they receive
        /// from probes.
        ///
        /// Fluorescent panels are the one exception. They are emissive fixtures driving their own
        /// per-instance MaterialPropertyBlock for flicker and the hunter blackout, and probe light
        /// would both fight that block and be wrong for a surface that is its own light source.
        /// </summary>
        private static void AttachProbeLitRenderers(Transform root, List<BakedVariant> baked)
        {
            var lightmapped = new HashSet<string>();
            foreach (BakedVariant entry in baked)
                foreach (string path in entry.RendererPaths)
                    lightmapped.Add(path);

            int attached = 0;
            foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                Transform t = renderer.transform;
                if (t.name == CombinedChildName || (t.parent != null && t.parent.name == CombinedChildName))
                    continue;
                if (renderer.GetComponent<FluorescentLight>() != null)
                    continue;
                if (lightmapped.Contains(GetHierarchyPath(t, root)))
                    continue;

                var lit = renderer.GetComponent<ProbeLitRenderer>() ?? renderer.gameObject.AddComponent<ProbeLitRenderer>();

                // Only things that actually travel need the per-frame check: a carried Echo has to
                // dim as the player walks it out of a lit room, while a preset pillar or a door-wall
                // piece is positioned once during the build and then never moves again.
                bool moves = renderer.GetComponentInParent<WorldItem>() != null;
                lit.ConfigureForBake(trackMovement: moves, includeChildren: false);
                attached++;
            }

            if (attached > 0)
                Debug.Log($"[RoomLightmapBaker] {root.name}: {attached} renderer(s) wired to probe lighting.");
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

        private static void StoreOnPrefab(
            string prefabPath,
            List<BakedVariant> baked,
            LightmapsMode mode,
            List<RoomLightProbeData.Variant> probeVariants)
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

                if (probeVariants.Count > 0)
                {
                    var probeData = root.GetComponent<RoomLightProbeData>()
                        ?? root.AddComponent<RoomLightProbeData>();
                    probeData.StoreVariants(probeVariants.ToArray());
                }

                // Everything the bake deliberately left out of the lightmap now has a light source:
                // the probe field above. Wiring it here (rather than by hand on 11 prefabs) keeps the
                // two halves of the decision — "excluded from GI" and "lit by probes" — in step.
                AttachProbeLitRenderers(root.transform, baked);

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
                // Measure from live geometry before touching anything, exactly as RoomFootprintBaker
                // does. Play mode rebuilds the baseline on every access, but the editor trusts the
                // serialized one — and a stale baseline puts the seam somewhere the wall isn't, so
                // opening the gap "slides" the pieces to a position derived from that wrong seam.
                // room7 shipped 42m off and had four of its five door walls thrown clean out of the
                // room by this call, where they baked black against the void.
                wall.RecacheBaseline();

                if (wall.OpeningMode != WallOpeningMode.StandardGap)
                    continue;

                wall.ConfigureOpening(true, WallGapController.GetWallCenterGapOffset(wall));
                opened++;
            }

            if (opened > 0)
                Debug.Log($"[RoomLightmapBaker] {root.name}: opened {opened} doorway(s) for the bake so " +
                          "the reveal faces receive light.");
        }

        /// <summary>
        /// Forces the door walls in this bake scene to receive from probes rather than lightmaps.
        ///
        /// RoomStaticMeshCombiner already writes this onto the prefab, but only when Combine is
        /// re-run — and re-combining 11 rooms just to flip a flag is a chore that would silently be
        /// skipped, leaving the walls lightmapped and stretched again. Setting it on the throwaway
        /// instance makes every bake correct on its own terms; the prefab-side flag stays as the
        /// asset's own record of the same decision.
        /// </summary>
        private static void SetDoorWallsToProbeLighting(Transform root)
        {
            int changed = 0;
            foreach (WallGapController wall in root.GetComponentsInChildren<WallGapController>(true))
            {
                foreach (MeshRenderer renderer in wall.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (RoomStaticMeshCombiner.SetReceiveGI(
                            renderer, RoomStaticMeshCombiner.ReceiveGiLightProbes))
                        changed++;
                }
            }

            if (changed > 0)
                Debug.Log($"[RoomLightmapBaker] {root.name}: {changed} door-wall renderer(s) switched to " +
                          "probe lighting for this bake (they still contribute GI / cast shadows).");
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
