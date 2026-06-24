using System.Collections;
using UnityEngine;

namespace AfterAll.Environment
{
    /// <summary>
    /// Fluorescent troffer — flicker settings only. FluorescentLightManager drives emission + lights.
    /// </summary>
    [DefaultExecutionOrder(50)]
    public class FluorescentLight : MonoBehaviour
    {
        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        [Header("Emission")]
        [ColorUsage(false, true)]
        [SerializeField] private Color  _emissionColor = new Color(0.94f, 0.97f, 0.82f, 1f);
        [SerializeField] private float    _emissionIntensity = 7.5f;

        [Header("Flicker")]
        [SerializeField] private bool  _flickerEnabled = true;
        [SerializeField] private float _minIdleSeconds = 4f;
        [SerializeField] private float _maxIdleSeconds = 14f;

        private Light    _spotLight;
        private Light    _pointLight;
        private float    _spotBaseIntensity;
        private float    _pointBaseIntensity;
        private bool     _lightsResolved;

        private Renderer              _panel;
        private MaterialPropertyBlock _propertyBlock;
        private Color                 _baseEmission;
        private Coroutine             _flickerRoutine;
        private bool                  _hasEmission;
        private bool                  _useSpot;
        private bool                  _useFlicker;
        private FluorescentLightTier  _tier = FluorescentLightTier.Off;
        private float                 _quality = 1f;

        public Vector3 WorldPosition => transform.position;
        public Vector3 HorizontalPosition => transform.position;
        public FluorescentLightTier CurrentTier => _tier;

        private void Awake()
        {
            _panel = GetComponent<Renderer>();
            _propertyBlock = new MaterialPropertyBlock();
            ResolveLights();
            SetupEmission();
        }

        private void OnEnable()
        {
            _tier = FluorescentLightTier.Off;
            _quality = 1f;
            ApplyTier(FluorescentLightTier.Off, 1f, false, false);
            FluorescentLightManager.EnsureExists().Register(this);
        }

        private void OnDisable()
        {
            FluorescentLightManager.Instance?.Unregister(this);

            if (_flickerRoutine != null)
            {
                StopCoroutine(_flickerRoutine);
                _flickerRoutine = null;
            }

            RestoreDefaultVisuals();
        }

        public void ApplyTier(FluorescentLightTier tier, float quality, bool useSpot, bool useFlicker)
        {
            quality = Mathf.Clamp(quality, 0.2f, 1f);

            if (_tier == tier
                && Mathf.Approximately(_quality, quality)
                && _useSpot == useSpot
                && _useFlicker == useFlicker)
                return;

            _tier = tier;
            _quality = quality;
            _useSpot = useSpot;
            _useFlicker = useFlicker;

            bool emissionOn = tier != FluorescentLightTier.Off;

            if (_panel != null)
                _panel.enabled = true;

            ApplyLightComponents(tier, quality, useSpot);
            SetPanelEmission(emissionOn ? 1f : 0f);
            UpdateFlickerState();
        }

        public void ApplyTier(FluorescentLightTier tier, float quality) =>
            ApplyTier(tier, quality, tier == FluorescentLightTier.Full, tier == FluorescentLightTier.Full);

        public void TriggerFlicker()
        {
            if (!isActiveAndEnabled || !_useFlicker)
                return;

            StartCoroutine(FlickerBurst());
        }

        public static float HorizontalDistanceSqr(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return dx * dx + dz * dz;
        }

        private void ApplyLightComponents(FluorescentLightTier tier, float quality, bool useSpot)
        {
            ResolveLights();

            bool point = tier == FluorescentLightTier.Full
                      || tier == FluorescentLightTier.CorridorPartial;
            bool spot = point && useSpot;

            float pointScale = tier == FluorescentLightTier.CorridorPartial && !useSpot
                ? quality
                : 1f;

            SetLightState(_spotLight, spot, _spotBaseIntensity, 1f);
            SetLightState(_pointLight, point, _pointBaseIntensity, pointScale);
        }

        private static void SetLightState(Light light, bool active, float baseIntensity, float scale)
        {
            if (light == null)
                return;

            light.enabled   = active;
            light.intensity = active ? baseIntensity * scale : 0f;
            light.shadows   = LightShadows.None;
        }

        private void ResolveLights()
        {
            if (_lightsResolved)
                return;

            foreach (var light in GetComponentsInChildren<Light>(true))
            {
                if (light.type == LightType.Spot)
                {
                    _spotLight = light;
                    _spotBaseIntensity = light.intensity;
                }
                else if (light.type == LightType.Point)
                {
                    _pointLight = light;
                    _pointBaseIntensity = light.intensity;
                }

                light.intensity = 0f;
                light.enabled   = false;
                light.shadows   = LightShadows.None;
            }

            _lightsResolved = true;
        }

        private void SetupEmission()
        {
            if (_panel == null)
                return;

            _baseEmission = _emissionColor * _emissionIntensity;
            _hasEmission  = _baseEmission.maxColorComponent > 0.01f;

            if (!_hasEmission)
                return;

            var shared = _panel.sharedMaterial;
            if (shared == null)
                return;

            shared.EnableKeyword("_EMISSION");
            shared.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;

            if (shared.HasProperty(EmissionColorId))
                shared.SetColor(EmissionColorId, _baseEmission);
        }

        private void UpdateFlickerState()
        {
            if (_useFlicker && _tier != FluorescentLightTier.Off && _tier != FluorescentLightTier.EmissionOnly)
            {
                if (_flickerEnabled && _flickerRoutine == null && isActiveAndEnabled)
                    _flickerRoutine = StartCoroutine(FlickerLoop());
                return;
            }

            if (_flickerRoutine != null)
            {
                StopCoroutine(_flickerRoutine);
                _flickerRoutine = null;
            }
        }

        private IEnumerator FlickerLoop()
        {
            var wait = new WaitForSeconds(0.5f);

            while (true)
            {
                if (!ShouldRunFlicker())
                {
                    ApplyVisualIntensity(1f);
                    SetPanelEmission(1f);
                    yield return wait;
                    continue;
                }

                yield return new WaitForSeconds(Random.Range(_minIdleSeconds, _maxIdleSeconds));
                yield return FlickerBurst();
            }
        }

        private IEnumerator FlickerBurst()
        {
            int steps = Random.Range(2, 5);
            for (int i = 0; i < steps; i++)
            {
                ApplyVisualIntensity(Random.Range(0.2f, 0.75f) * _quality);
                yield return new WaitForSeconds(Random.Range(0.04f, 0.14f));
            }

            if (Random.value < 0.15f)
            {
                ApplyVisualIntensity(0f);
                yield return new WaitForSeconds(Random.Range(0.05f, 0.12f));
            }

            ApplyVisualIntensity(1f);
            SetPanelEmission(1f);
        }

        private bool ShouldRunFlicker() =>
            _flickerEnabled && _useFlicker;

        private void ApplyVisualIntensity(float normalized)
        {
            normalized = Mathf.Clamp01(normalized);

            if (_spotLight != null && _spotLight.enabled)
                _spotLight.intensity = _spotBaseIntensity * normalized;

            if (_pointLight != null && _pointLight.enabled)
            {
                float pointScale = _tier == FluorescentLightTier.CorridorPartial && !_useSpot
                    ? _quality
                    : 1f;
                _pointLight.intensity = _pointBaseIntensity * normalized * pointScale;
            }

            if (_useFlicker)
                SetPanelEmission(normalized);
        }

        private void SetPanelEmission(float normalized)
        {
            if (!_hasEmission || _panel == null)
                return;

            _panel.GetPropertyBlock(_propertyBlock);

            if (normalized <= 0.001f)
            {
                _propertyBlock.SetColor(EmissionColorId, Color.black);
            }
            else
            {
                var color = _baseEmission * normalized;
                _propertyBlock.SetColor(EmissionColorId, color);
            }

            _panel.SetPropertyBlock(_propertyBlock);
        }

        private void RestoreDefaultVisuals()
        {
            SetLightState(_spotLight, false, _spotBaseIntensity, 1f);
            SetLightState(_pointLight, false, _pointBaseIntensity, 1f);

            if (_panel != null)
                _panel.SetPropertyBlock(null);
        }
    }
}
