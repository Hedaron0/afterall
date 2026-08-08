using System;
using System.Collections.Generic;
using UnityEngine;

namespace AfterAll.Environment
{
    /// <summary>
    /// Carries a room prefab's baked lightmaps with it, one set per content preset.
    ///
    /// Unity's lightmap data belongs to a scene, but rooms are instantiated at runtime by
    /// RoomPoolSpawner, so a normal bake never reaches them. RoomLightmapBaker bakes each room once
    /// per preset option — the preset's pillars and half-walls have to be present at bake time or
    /// they cast no shadow and receive no light — and stores every result here. Once
    /// RoomContentManager has picked a preset it calls <see cref="ApplyVariant"/> with the winner's
    /// name and the matching set is pushed into the global lightmap array.
    ///
    /// Registration is deduplicated by texture reference, so twenty instances of the same prefab and
    /// every later floor rebuild reuse one set of entries instead of growing the array.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class RoomLightmapData : MonoBehaviour
    {
        /// <summary>One bake: the room lit with a single preset option active.</summary>
        [Serializable]
        public class Variant
        {
            public string      presetName = string.Empty;
            public Renderer[]  renderers = new Renderer[0];
            public int[]       lightmapIndices = new int[0];
            public Vector4[]   lightmapScaleOffsets = new Vector4[0];
            public Texture2D[] lightmapColors = new Texture2D[0];
            public Texture2D[] lightmapDirs = new Texture2D[0];
            public Texture2D[] shadowMasks = new Texture2D[0];

            public bool IsValid => renderers.Length > 0 && lightmapColors.Length > 0;
        }

        [SerializeField] private Variant[] _variants = new Variant[0];
        [SerializeField] private LightmapsMode _lightmapsMode = LightmapsMode.NonDirectional;

        /// <summary>Our mirror of LightmapSettings.lightmaps, so registration never reads it back.</summary>
        private static readonly List<LightmapData> Registry = new List<LightmapData>();

        /// <summary>Maps an already-registered color texture to its index in <see cref="Registry"/>.</summary>
        private static readonly Dictionary<Texture2D, int> RegisteredLightmaps = new Dictionary<Texture2D, int>();

        private static int _batchDepth;
        private static bool _registryDirty;

        /// <summary>Last variant pushed for this instance, so a floor rebuild can restore it.</summary>
        private Variant _applied;

        public bool HasBakedData => _variants.Length > 0;

        private void Awake()
        {
            // The prefab's renderers still carry the lightmapIndex the bake scene gave them, and that
            // index means something else entirely in the running game — it would point at whichever
            // room happens to occupy that slot. Applying a variant here just to overwrite it would
            // register a whole set of textures that ApplyVariant discards moments later, so blank the
            // indices instead and let the room ride on ambient until its preset is known.
            if (_variants.Length == 0)
                return;

            foreach (Variant variant in _variants)
                foreach (Renderer renderer in variant.renderers)
                    if (renderer != null)
                        renderer.lightmapIndex = -1;
        }

        /// <summary>Applies the bake for <paramref name="presetName"/>, or the first one if unmatched.</summary>
        public void ApplyVariant(string presetName)
        {
            if (_variants.Length == 0)
                return;

            foreach (Variant variant in _variants)
            {
                if (variant.presetName == presetName)
                {
                    Apply(variant);
                    return;
                }
            }

            Debug.LogWarning(
                $"[RoomLightmapData] {name}: no bake for preset '{presetName}' — falling back to " +
                $"'{_variants[0].presetName}'. Re-run Bake Room Lightmaps after changing preset options.",
                this);
            Apply(_variants[0]);
        }

        private void Apply(Variant variant)
        {
            if (variant == null || !variant.IsValid)
                return;

            // Directionality is a global setting, so a game scene left on Combined Directional would
            // have renderers sampling a direction map these bakes never produced.
            if (LightmapSettings.lightmapsMode != _lightmapsMode)
                LightmapSettings.lightmapsMode = _lightmapsMode;

            int[] localToGlobal = RegisterLightmaps(variant);
            _applied = variant;

            for (int i = 0; i < variant.renderers.Length; i++)
            {
                Renderer renderer = variant.renderers[i];
                if (renderer == null)
                    continue;

                int local = variant.lightmapIndices[i];
                if (local < 0 || local >= localToGlobal.Length || localToGlobal[local] < 0)
                    continue;

                renderer.lightmapIndex       = localToGlobal[local];
                renderer.lightmapScaleOffset = variant.lightmapScaleOffsets[i];
            }
        }

        /// <summary>
        /// Holds off pushing the lightmap array to Unity until <see cref="EndBatch"/>.
        ///
        /// Assigning LightmapSettings.lightmaps makes Unity re-resolve the binding for every renderer
        /// in the scene and pull in any texture it hasn't loaded yet. Doing that once per room means
        /// twenty of those passes over a growing array while a floor is built, each one dragging in
        /// multi-megabyte lightmaps — enough to stall the editor for a long time on a full floor.
        /// One assignment at the end costs the same as the first one did.
        /// </summary>
        public static void BeginBatch()
        {
            if (_batchDepth == 0)
                EnsureRegistrySynced();

            _batchDepth++;
        }

        /// <summary>Ends a <see cref="BeginBatch"/> scope and pushes the array if this was the last one.</summary>
        public static void EndBatch()
        {
            if (_batchDepth > 0)
                _batchDepth--;

            if (_batchDepth == 0)
                Flush();
        }

        private static void Flush()
        {
            if (!_registryDirty)
                return;

            LightmapSettings.lightmaps = Registry.ToArray();
            _registryDirty = false;
        }

        /// <summary>
        /// Drops the registry if it no longer describes LightmapSettings.
        ///
        /// These are statics, and with domain reload turned off in Play Mode Options they survive into
        /// the next play session while LightmapSettings starts empty — every cached index would then
        /// point at a slot that no longer exists. Only meaningful outside a batch, where the two are
        /// expected to match.
        /// </summary>
        private static void EnsureRegistrySynced()
        {
            if (LightmapSettings.lightmaps.Length == Registry.Count)
                return;

            Registry.Clear();
            RegisteredLightmaps.Clear();
            _registryDirty = true;
        }

        private static int[] RegisterLightmaps(Variant variant)
        {
            if (_batchDepth == 0)
                EnsureRegistrySynced();

            var localToGlobal = new int[variant.lightmapColors.Length];

            for (int i = 0; i < variant.lightmapColors.Length; i++)
            {
                Texture2D color = variant.lightmapColors[i];
                if (color == null)
                {
                    localToGlobal[i] = -1;
                    continue;
                }

                if (RegisteredLightmaps.TryGetValue(color, out int existing))
                {
                    localToGlobal[i] = existing;
                    continue;
                }

                Registry.Add(new LightmapData
                {
                    lightmapColor = color,
                    lightmapDir   = i < variant.lightmapDirs.Length ? variant.lightmapDirs[i] : null,
                    shadowMask    = i < variant.shadowMasks.Length ? variant.shadowMasks[i] : null,
                });

                int index = Registry.Count - 1;
                RegisteredLightmaps[color] = index;
                localToGlobal[i] = index;
                _registryDirty = true;
            }

            if (_batchDepth == 0)
                Flush();

            return localToGlobal;
        }

        /// <summary>
        /// Empties the global lightmap array before a floor rebuild and re-registers only the rooms
        /// that outlive it.
        ///
        /// Nothing ever removed entries: every room appended its textures on spawn and the array kept
        /// them after the floor was destroyed, so a run leaked one floor's worth of lightmap VRAM per
        /// rebuild. <paramref name="destroyedRoot"/> is the subtree about to be torn down — its rooms
        /// are skipped by transform rather than by Destroy having run, because Destroy only takes
        /// effect at the end of the frame and they would otherwise re-register themselves. Rooms
        /// outside it (the persistent elevator cabin) have to be re-applied, since wiping the array
        /// invalidates the lightmapIndex their renderers are still holding.
        /// </summary>
        public static void ResetForNewFloor(Transform destroyedRoot)
        {
            RegisteredLightmaps.Clear();
            Registry.Clear();
            _batchDepth    = 0;
            _registryDirty = true;

            BeginBatch();
            foreach (RoomLightmapData survivor in
                     FindObjectsByType<RoomLightmapData>(FindObjectsSortMode.None))
            {
                if (survivor._applied == null)
                    continue;
                if (destroyedRoot != null && survivor.transform.IsChildOf(destroyedRoot))
                    continue;

                survivor.Apply(survivor._applied);
            }
            EndBatch();
        }

#if UNITY_EDITOR
        public void StoreVariants(Variant[] variants, LightmapsMode lightmapsMode)
        {
            _variants      = variants;
            _lightmapsMode = lightmapsMode;
        }
#endif
    }
}
