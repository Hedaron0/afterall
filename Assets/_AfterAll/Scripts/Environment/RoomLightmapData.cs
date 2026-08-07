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

        /// <summary>Maps an already-registered color texture to its index in LightmapSettings.lightmaps.</summary>
        private static readonly Dictionary<Texture2D, int> RegisteredLightmaps = new Dictionary<Texture2D, int>();

        public bool HasBakedData => _variants.Length > 0;

        private void Awake()
        {
            // Content activation happens a moment later in the build sequence; apply something now so
            // the room is never briefly unlit, then ApplyVariant corrects it once the preset is known.
            if (_variants.Length > 0)
                Apply(_variants[0]);
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

        private static int[] RegisterLightmaps(Variant variant)
        {
            var lightmaps = new List<LightmapData>(LightmapSettings.lightmaps);
            var localToGlobal = new int[variant.lightmapColors.Length];
            bool changed = false;

            for (int i = 0; i < variant.lightmapColors.Length; i++)
            {
                Texture2D color = variant.lightmapColors[i];
                if (color == null)
                {
                    localToGlobal[i] = -1;
                    continue;
                }

                // A cached index can outlive its slot when a scene load resets LightmapSettings or the
                // editor reloads the domain, so validate before trusting it.
                if (RegisteredLightmaps.TryGetValue(color, out int existing)
                    && existing < lightmaps.Count
                    && lightmaps[existing].lightmapColor == color)
                {
                    localToGlobal[i] = existing;
                    continue;
                }

                lightmaps.Add(new LightmapData
                {
                    lightmapColor = color,
                    lightmapDir   = i < variant.lightmapDirs.Length ? variant.lightmapDirs[i] : null,
                    shadowMask    = i < variant.shadowMasks.Length ? variant.shadowMasks[i] : null,
                });

                int index = lightmaps.Count - 1;
                RegisteredLightmaps[color] = index;
                localToGlobal[i] = index;
                changed = true;
            }

            if (changed)
                LightmapSettings.lightmaps = lightmaps.ToArray();

            return localToGlobal;
        }

        /// <summary>Drops the dedup cache — call when LightmapSettings is reset out from under us.</summary>
        public static void ClearRegistry() => RegisteredLightmaps.Clear();

#if UNITY_EDITOR
        public void StoreVariants(Variant[] variants, LightmapsMode lightmapsMode)
        {
            _variants      = variants;
            _lightmapsMode = lightmapsMode;
        }
#endif
    }
}
