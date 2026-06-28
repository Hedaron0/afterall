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
            _isOn = _settings != null && _settings.AutoOnWhenEquipped;
            UpdateLightState(force: true);

            _toggleAction?.Enable();
        }

        public void OnUnequipped()
        {
            _equipped = false;
            SetLightEnabled(false);
            StopHum();
            _toggleAction?.Disable();
        }

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
