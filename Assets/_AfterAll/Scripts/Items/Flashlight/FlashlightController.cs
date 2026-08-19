using System.Collections.Generic;
using AfterAll.Inventories;
using AfterAll.Items;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AfterAll.Items.Flashlight
{
    public sealed class FlashlightController : MonoBehaviour, IHeldItemBehaviour
    {
        [SerializeField] private FlashlightSettings _settings;
        [SerializeField] private Transform _beamAnchor;
        [SerializeField] private string _beamAnchorName = "BeamAnchor";

        private Inventory _inventory;
        private Camera _camera;
        private ItemDefinition _item;
        private Light _light;
        private AudioSource _humSource;
        private InputAction _toggleAction;

        /// <summary>
        /// On/off has to live outside this component. ItemHolder.Refresh Instantiates the held
        /// viewmodel fresh on every equip and ClearHeld Destroys it on every unequip, so an
        /// instance field would reset each time the hotbar moved — which is exactly why a
        /// flashlight switched off came back on after a slot change. Keyed by ItemDefinition so
        /// each flashlight item keeps its own state, and AutoOnWhenEquipped only applies the very
        /// first time that item is drawn.
        /// </summary>
        private static readonly Dictionary<ItemDefinition, bool> PersistedOnState = new Dictionary<ItemDefinition, bool>();

        /// <summary>The settings asset the held viewmodel was using, kept for the dropped world form.</summary>
        private static readonly Dictionary<ItemDefinition, FlashlightSettings> PersistedSettings =
            new Dictionary<ItemDefinition, FlashlightSettings>();

        private bool _isOn;
        private float _baseIntensity;
        private float _flickerSeed;
        private float _dropoutTimer;
        private bool _equipped;

        private void Awake()
        {
            if (_beamAnchor == null && !string.IsNullOrEmpty(_beamAnchorName))
            {
                var found = transform.Find(_beamAnchorName);
                if (found != null)
                    _beamAnchor = found;
            }

            if (_beamAnchor != null)
                _light = _beamAnchor.GetComponentInChildren<Light>(true);

            _flickerSeed = Random.value * 100f;
            ResolveToggleAction();
        }

        public void OnEquipped(Inventory inventory, Camera camera, ItemDefinition item)
        {
            _inventory = inventory;
            _camera = camera;
            _item = item;
            _equipped = true;

            ApplySettingsToLight();

            bool remembered;
            _isOn = _item != null && PersistedOnState.TryGetValue(_item, out remembered)
                ? remembered
                : _settings != null && _settings.AutoOnWhenEquipped;

            UpdateLightState(force: true);

            _toggleAction?.Enable();
        }

        public void OnUnequipped()
        {
            RememberOnState();
            _equipped = false;
            SetLightEnabled(false);
            StopHum();
            _toggleAction?.Disable();
        }

        private void RememberOnState()
        {
            if (_item == null)
                return;

            PersistedOnState[_item] = _isOn;
            // Remembered alongside the state so the dropped world pickup can be lit with exactly the
            // beam the player was just holding, instead of whatever the world prefab's Light happens
            // to be authored with.
            PersistedSettings[_item] = _settings;
        }

        /// <summary>
        /// The remembered lamp state for a flashlight item, for whoever spawns its world pickup —
        /// the held viewmodel is already destroyed by then, so it cannot be asked directly.
        /// Falls back to AutoOnWhenEquipped-free "off" for an item that has never been drawn.
        /// </summary>
        public static bool IsOnFor(ItemDefinition item) =>
            item != null && PersistedOnState.TryGetValue(item, out bool on) && on;

        /// <summary>The beam settings this item was last held with, or null if it never was.</summary>
        public static FlashlightSettings SettingsFor(ItemDefinition item) =>
            item != null && PersistedSettings.TryGetValue(item, out FlashlightSettings s) ? s : null;

        private void Update()
        {
            if (!_equipped || _inventory == null || _item == null)
                return;

            if (_inventory.SelectedItem != _item)
            {
                SetLightEnabled(false);
                StopHum();
                return;
            }

            if (_toggleAction != null && _toggleAction.WasPressedThisFrame())
                Toggle();

            if (_isOn && _inventory.SelectedItem == _item)
                UpdateLightState(force: false);
        }

        private void LateUpdate()
        {
            if (!_equipped || _camera == null || _beamAnchor == null)
                return;

            _beamAnchor.rotation = _camera.transform.rotation;
        }

        private void Toggle()
        {
            _isOn = !_isOn;
            RememberOnState();
            UpdateLightState(force: true);
            PlayToggleSound(_isOn);
        }

        private void ApplySettingsToLight()
        {
            if (_light == null || _settings == null)
                return;

            _light.type = LightType.Spot;
            _light.color = _settings.Color;
            _baseIntensity = _settings.Intensity;
            _light.intensity = _baseIntensity;
            _light.range = _settings.Range;
            _light.spotAngle = _settings.SpotAngle;
            _light.innerSpotAngle = _settings.InnerSpotAngle;
            _light.shadows = _settings.Shadows;
            _light.shadowStrength = _settings.ShadowStrength;
            _light.enabled = false;
        }

        private void UpdateLightState(bool force)
        {
            bool shouldEmit = _isOn && _equipped && _inventory != null && _inventory.SelectedItem == _item;
            if (!shouldEmit)
            {
                SetLightEnabled(false);
                StopHum();
                return;
            }

            SetLightEnabled(true);
            UpdateFlicker();
            UpdateHum(force);
        }

        private void UpdateFlicker()
        {
            if (_light == null || _settings == null)
                return;

            if (_dropoutTimer > 0f)
            {
                _dropoutTimer -= Time.deltaTime;
                _light.intensity = 0f;
                return;
            }

            if (Random.value < _settings.DropoutChance * Time.deltaTime * 60f)
                _dropoutTimer = _settings.DropoutDuration;

            float noise = Mathf.PerlinNoise(_flickerSeed, Time.time * _settings.FlickerSpeed);
            float flicker = 1f + (noise - 0.5f) * 2f * _settings.FlickerAmount;
            _light.intensity = _baseIntensity * flicker;
        }

        private void SetLightEnabled(bool enabled)
        {
            if (_light != null)
                _light.enabled = enabled;
        }

        private void UpdateHum(bool force)
        {
            if (_settings == null || _settings.HumLoopClip == null)
                return;

            if (_humSource == null)
            {
                _humSource = gameObject.AddComponent<AudioSource>();
                _humSource.clip = _settings.HumLoopClip;
                _humSource.loop = true;
                _humSource.spatialBlend = 0f;
                _humSource.playOnAwake = false;
            }

            _humSource.volume = _settings.HumVolume;

            if (force || !_humSource.isPlaying)
                _humSource.Play();
        }

        private void StopHum()
        {
            if (_humSource != null && _humSource.isPlaying)
                _humSource.Stop();
        }

        private void PlayToggleSound(bool turningOn)
        {
            if (_settings == null)
                return;

            AudioClip clip = turningOn ? _settings.ToggleOnClip : _settings.ToggleOffClip;
            if (clip == null)
                return;

            AudioSource.PlayClipAtPoint(clip, transform.position, _settings.ClickVolume);
        }

        private void ResolveToggleAction()
        {
            foreach (var asset in Resources.FindObjectsOfTypeAll<InputActionAsset>())
            {
                if (asset.name != "InputSystem_Actions")
                    continue;

                _toggleAction = asset.FindActionMap("Player")?.FindAction("FlashlightToggle");
                return;
            }
        }
    }
}
