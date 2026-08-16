using System.Collections;
using UnityEngine;

namespace AfterAll.Environment
{
    /// <summary>
    /// Fluorescent troffer — emissive panel only at runtime, no realtime Light component.
    /// Illumination comes from a baked Area Light child (see FluorescentPanel prefab) that only
    /// exists at bake time; this script destroys it on spawn and drives the panel's emissive
    /// material property block instead (lit on/off + idle flicker).
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

        private Renderer              _panel;
        private MaterialPropertyBlock _propertyBlock;
        private Color                 _baseEmission;
        private Coroutine             _flickerRoutine;
        private bool                  _hasEmission;
        private bool                  _lit = true;

        public Vector3 WorldPosition => transform.position;

        private void Awake()
        {
            _panel = GetComponentInChildren<Renderer>();
            _propertyBlock = new MaterialPropertyBlock();

            var bakeOnlyLight = GetComponentInChildren<Light>(true);
            if (bakeOnlyLight != null)
                Destroy(bakeOnlyLight.gameObject);

            SetupEmission();
        }

        private void OnEnable()
        {
            SetLit(true);
        }

        private void OnDisable()
        {
            if (_flickerRoutine != null)
            {
                StopCoroutine(_flickerRoutine);
                _flickerRoutine = null;
            }

            SetPanelEmission(0f);
        }

        /// <summary>Turns the panel's emissive glow fully on or off (e.g. hunter blackout beat).</summary>
        public void SetLit(bool lit)
        {
            if (_lit == lit)
                return;

            _lit = lit;
            SetPanelEmission(lit ? 1f : 0f);
            UpdateFlickerState();
        }

        public void TriggerFlicker()
        {
            if (!isActiveAndEnabled || !_lit)
                return;

            StartCoroutine(FlickerBurst());
        }

        /// <summary>
        /// Works out this panel's lit emission colour. Deliberately touches nothing on the material.
        ///
        /// This used to write the emission colour AND globalIlluminationFlags onto sharedMaterial,
        /// which is the project asset: every Play session rewrote `fluorescent_emissive` on disk
        /// (measured — the asset's stored emission was exactly _emissionColor * _emissionIntensity),
        /// flipped its GI mode to RealtimeEmissive behind the bake's back, and dirtied the file in
        /// git. A per-instance MaterialPropertyBlock is the supported way to drive this, and
        /// SetPanelEmission already does exactly that — the shared writes were never needed.
        ///
        /// The material must therefore be authored with emission enabled. If it is not, say so once
        /// rather than silently repairing it, because silently repairing it is what caused this.
        /// </summary>
        private void SetupEmission()
        {
            if (_panel == null)
                return;

            _baseEmission = _emissionColor * _emissionIntensity;
            _hasEmission  = _baseEmission.maxColorComponent > 0.01f;

            if (!_hasEmission)
                return;

#if UNITY_EDITOR
            var shared = _panel.sharedMaterial;
            if (shared != null && !shared.IsKeywordEnabled("_EMISSION"))
                Debug.LogWarning(
                    $"[FluorescentLight] Material '{shared.name}' has emission disabled, so the panel " +
                    "will not glow. Enable Emission on the material asset itself.", this);
#endif
        }

        private void UpdateFlickerState()
        {
            if (_flickerEnabled && _lit)
            {
                if (_flickerRoutine == null && isActiveAndEnabled)
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
                if (!_flickerEnabled || !_lit)
                {
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
                SetPanelEmission(Random.Range(0.2f, 0.75f));
                yield return new WaitForSeconds(Random.Range(0.04f, 0.14f));
            }

            if (Random.value < 0.15f)
            {
                SetPanelEmission(0f);
                yield return new WaitForSeconds(Random.Range(0.05f, 0.12f));
            }

            SetPanelEmission(1f);
        }

        private void SetPanelEmission(float normalized)
        {
            if (!_hasEmission || _panel == null)
                return;

            // Play-mode script recompiles reload the domain and re-fire OnEnable without re-running
            // Awake, so this non-serialized field can be null here even though Awake always assigns
            // it on first spawn.
            _propertyBlock ??= new MaterialPropertyBlock();

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
    }
}
