using System.Collections;
using UnityEngine;

namespace AfterAll.Environment
{
    /// <summary>
    /// Backrooms fluorescent troffer — flickers spot intensity and panel emission together.
    /// Place on the panel root; wires Light child + MeshRenderer on Awake if unset.
    /// </summary>
    public class FluorescentLight : MonoBehaviour
    {
        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        [SerializeField] private Light _light;
        [SerializeField] private Renderer _panel;
        [SerializeField] private float _baseIntensity = 32f;
        [SerializeField] private bool _flickerEnabled = true;
        [SerializeField] private float _minIdleSeconds = 4f;
        [SerializeField] private float _maxIdleSeconds = 14f;
        [SerializeField] private float _mobileCullDistance = 25f;

        private Material _panelMaterial;
        private Color _baseEmission;
        private Coroutine _flickerRoutine;
        private bool _hasEmission;

        private void Awake()
        {
            if (_light == null)
                _light = GetComponentInChildren<Light>();

            if (_panel == null)
                _panel = GetComponent<Renderer>();

            if (_light != null)
                _baseIntensity = _light.intensity;

            CacheEmission();
        }

        private void OnEnable()
        {
            if (!_flickerEnabled || _light == null)
                return;

            _flickerRoutine = StartCoroutine(FlickerLoop());
        }

        private void OnDisable()
        {
            if (_flickerRoutine != null)
            {
                StopCoroutine(_flickerRoutine);
                _flickerRoutine = null;
            }

            ApplyIntensity(1f);
        }

        public void TriggerFlicker()
        {
            if (_light == null || !isActiveAndEnabled)
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
                _hasEmission = _baseEmission.maxColorComponent > 0f;
            }
        }

        private IEnumerator FlickerLoop()
        {
            var wait = new WaitForSeconds(0.5f);

            while (true)
            {
                if (!ShouldRunFlicker())
                {
                    ApplyIntensity(1f);
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

        private bool ShouldRunFlicker()
        {
            if (!_flickerEnabled)
                return false;

            if (!Application.isMobilePlatform)
                return true;

            var cam = Camera.main;
            if (cam == null)
                return true;

            return (cam.transform.position - transform.position).sqrMagnitude
                   <= _mobileCullDistance * _mobileCullDistance;
        }

        private void ApplyIntensity(float normalized)
        {
            normalized = Mathf.Clamp01(normalized);

            if (_light != null)
                _light.intensity = _baseIntensity * normalized;

            if (!_hasEmission || _panelMaterial == null)
                return;

            _panelMaterial.SetColor(EmissionColorId, _baseEmission * normalized);
        }
    }

}
