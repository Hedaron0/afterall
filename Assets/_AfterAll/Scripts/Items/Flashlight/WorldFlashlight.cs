using UnityEngine;

namespace AfterAll.Items.Flashlight
{
    /// <summary>
    /// The dropped/thrown form of a flashlight. The held viewmodel is destroyed the moment the item
    /// leaves the hand (ItemHolder.ClearHeld), so the world pickup has to carry the on/off state
    /// itself: throw it lit and it keeps shining where it lands, switch it off first and it stays
    /// dark.
    ///
    /// The component is OPTIONAL. Flashlight_World's root is an instance of the imported GLTF model
    /// prefab, and Unity refuses to save a component added onto a prefab instance root from script
    /// ("Can't save a Prefab instance"), so the same work is available as <see cref="ApplyTo"/> on a
    /// spawned instance. Add the component by hand only when a specific Light or emissive renderer
    /// needs picking out — otherwise the static path resolves them the same way.
    ///
    /// Emission is driven through a MaterialPropertyBlock, never by writing to the renderer's
    /// material. Touching the shared material asset is what made FluorescentLight rewrite its own
    /// asset on every Play session (2026-08-16) — a per-renderer block cannot leak into the project.
    /// The lit colour is whatever the material was authored with, so the look stays Harun's call and
    /// needs no values typed in here.
    /// </summary>
    [DisallowMultipleComponent]
    public class WorldFlashlight : MonoBehaviour
    {
        [Tooltip("Left empty, the first Light anywhere under this prefab is used.")]
        [SerializeField] private Light _light;

        [Tooltip("Left empty, the renderer whose material has a non-black authored emission is used.")]
        [SerializeField] private Renderer _emissiveRenderer;

        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private static MaterialPropertyBlock _sharedBlock;

        /// <summary>Current lamp state of this dropped flashlight.</summary>
        public bool IsOn { get; private set; }

        public void SetOn(bool on)
        {
            IsOn = on;
            Apply(_light != null ? _light : GetComponentInChildren<Light>(true),
                  _emissiveRenderer != null ? _emissiveRenderer : FindEmissiveRenderer(gameObject),
                  on);
        }

        /// <summary>
        /// Sets the lamp state on a spawned pickup that has no WorldFlashlight component. No-ops on
        /// anything without a Light, which is what keeps it safe to call on every dropped item
        /// rather than having to ask first whether the item is a flashlight.
        /// </summary>
        public static void ApplyTo(GameObject spawned, bool on)
        {
            if (spawned == null)
                return;

            if (spawned.TryGetComponent(out WorldFlashlight existing))
            {
                existing.SetOn(on);
                return;
            }

            Light light = spawned.GetComponentInChildren<Light>(true);
            if (light == null)
                return;

            Apply(light, FindEmissiveRenderer(spawned), on);
        }

        private static void Apply(Light light, Renderer emissive, bool on)
        {
            if (light != null)
                light.enabled = on;

            if (emissive == null || emissive.sharedMaterial == null)
                return;

            _sharedBlock ??= new MaterialPropertyBlock();
            emissive.GetPropertyBlock(_sharedBlock);
            // Read the lit colour off the material every time rather than caching it: the block is
            // shared across callers, and the authored value is the single source of truth for "on".
            Color lit = emissive.sharedMaterial.GetColor(EmissionColorId);
            _sharedBlock.SetColor(EmissionColorId, on ? lit : Color.black);
            emissive.SetPropertyBlock(_sharedBlock);
        }

        /// <summary>
        /// Most of the prefab's materials expose _EmissionColor and leave it black — the body and the
        /// lens covers all do. Picking the first one that merely HAS the property would grab an unlit
        /// part and toggling it would visibly do nothing, so a non-black authored emission is what
        /// identifies the bulb.
        /// </summary>
        private static Renderer FindEmissiveRenderer(GameObject root)
        {
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r.sharedMaterial == null || !r.sharedMaterial.HasProperty(EmissionColorId))
                    continue;

                if (r.sharedMaterial.GetColor(EmissionColorId).maxColorComponent > 0.001f)
                    return r;
            }

            return null;
        }
    }
}
