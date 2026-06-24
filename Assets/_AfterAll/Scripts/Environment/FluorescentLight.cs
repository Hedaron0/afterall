using System.Collections;
using UnityEngine;
using UnityEngine.Serialization;

namespace AfterAll.Environment
{
    /// <summary>
    /// Fluorescent troffer — drives spot + point child lights and panel emission together.
    /// One Range value controls culling distance and sets both lights' Light.range when active.
    /// </summary>
    public class FluorescentLight : MonoBehaviour
    {
        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        [Header("References")]
        [FormerlySerializedAs("_light")]
        [SerializeField] private Light    _spot;
        [SerializeField] private Light    _point;
        [SerializeField] private Renderer _panel;

        [Header("Flicker")]
        [SerializeField] private bool  _flickerEnabled = true;
        [SerializeField] private float _minIdleSeconds = 4f;
        [SerializeField] private float _maxIdleSeconds = 14f;

        [Header("Distance")]
        [Tooltip("Panel and all lights turn on within this distance (metres). Sets Light.range on both.")]
        [FormerlySerializedAs("_activationRange")]
        [FormerlySerializedAs("_lightRange")]
        [SerializeField] [Min(1f)] private float _range = 40f;

        [Tooltip("Disable the panel beyond Range to save GPU.")]
        [SerializeField] private bool _useDistanceActivation = true;

        private Light[]  _lights;
        private float[]  _baseIntensities;
        private Material _panelMaterial;
        private Color    _baseEmission;
        private Coroutine _flickerRoutine;
        private bool     _hasEmission;
        private bool     _activeByDistance;

        private void Awake()
        {
            ResolveLights();

            if (_panel == null)
                _panel = GetComponent<Renderer>();

            _lights          = CollectLights();
            _baseIntensities = new float[_lights.Length];

            for (int i = 0; i < _lights.Length; i++)
            {
                _baseIntensities[i] = _lights[i].intensity;
                _lights[i].intensity = 0f;
                _lights[i].range     = _range;
                _lights[i].enabled   = false;
            }

            CacheEmission();
        }

        private void ResolveLights()
        {
            if (_spot != null && _point != null)
                return;

            foreach (var l in GetComponentsInChildren<Light>(true))
            {
                if (_spot == null && l.type == LightType.Spot)
                    _spot = l;
                else if (_point == null && l.type == LightType.Point)
                    _point = l;
            }
        }

        private Light[] CollectLights()
        {
            var list = new System.Collections.Generic.List<Light>(2);
            if (_spot  != null) list.Add(_spot);
            if (_point != null) list.Add(_point);
            return list.ToArray();
        }

        private void OnEnable()
        {
            FluorescentLightManager.EnsureExists().Register(this);

            if (!_useDistanceActivation)
            {
                _activeByDistance = true;
                SetActive(true);
                if (_flickerEnabled && _lights.Length > 0)
                    _flickerRoutine = StartCoroutine(FlickerLoop());
            }
            else
            {
                _activeByDistance = false;
                SetActive(false);
            }
        }

        private void OnDisable()
        {
            if (FluorescentLightManager.Instance != null)
                FluorescentLightManager.Instance.Unregister(this);

            if (_flickerRoutine != null)
            {
                StopCoroutine(_flickerRoutine);
                _flickerRoutine = null;
            }

            SetActive(true);
        }

        public void RefreshCullState(Vector3 playerPos)
        {
            if (!_useDistanceActivation)
                return;

            bool inRange = (playerPos - transform.position).sqrMagnitude <= _range * _range;
            if (inRange == _activeByDistance)
                return;

            _activeByDistance = inRange;

            if (inRange)
            {
                SetActive(true);
                if (_flickerEnabled && _lights.Length > 0 && _flickerRoutine == null)
                    _flickerRoutine = StartCoroutine(FlickerLoop());
            }
            else
            {
                if (_flickerRoutine != null)
                {
                    StopCoroutine(_flickerRoutine);
                    _flickerRoutine = null;
                }

                SetActive(false);
            }
        }

        public void TriggerFlicker()
        {
            if (_lights.Length == 0 || !isActiveAndEnabled || !_activeByDistance)
                return;

            StartCoroutine(FlickerBurst());
        }

        private void CacheEmission()
        {
            if (_panel == null)
                return;

            _panelMaterial = _panel.material;
            if (_panelMaterial != null && _panelMaterial.HasProperty(EmissionColorId))
            {
                _baseEmission = _panelMaterial.GetColor(EmissionColorId);
                _hasEmission  = _baseEmission.maxColorComponent > 0f;
            }
        }

        private IEnumerator FlickerLoop()
        {
            var wait = new WaitForSeconds(0.5f);

            while (true)
            {
                if (!ShouldRunFlicker())
                {
                    ApplyIntensity(_activeByDistance ? 1f : 0f);
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
                ApplyIntensity(Random.Range(0.2f, 0.75f));
                yield return new WaitForSeconds(Random.Range(0.04f, 0.14f));
            }

            if (Random.value < 0.15f)
            {
                ApplyIntensity(0f);
                yield return new WaitForSeconds(Random.Range(0.05f, 0.12f));
            }

            ApplyIntensity(1f);
        }

        private bool ShouldRunFlicker() => _flickerEnabled && _activeByDistance;

        private void SetActive(bool active)
        {
            for (int i = 0; i < _lights.Length; i++)
            {
                var l = _lights[i];
                if (l == null) continue;

                l.enabled   = active;
                l.range     = _range;
                l.intensity = active ? _baseIntensities[i] : 0f;
            }

            if (_panel != null)
                _panel.enabled = active;

            ApplyIntensity(active ? 1f : 0f);
        }

        private void ApplyIntensity(float normalized)
        {
            normalized = Mathf.Clamp01(normalized);

            for (int i = 0; i < _lights.Length; i++)
            {
                if (_lights[i] != null && _lights[i].enabled)
                    _lights[i].intensity = _baseIntensities[i] * normalized;
            }

            if (!_hasEmission || _panelMaterial == null)
                return;

            _panelMaterial.SetColor(EmissionColorId, _baseEmission * normalized);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.95f, 0.4f, 0.3f);
            Gizmos.DrawWireSphere(transform.position, _range);
        }
#endif
    }
}
