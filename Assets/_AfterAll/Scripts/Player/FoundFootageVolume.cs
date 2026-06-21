using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace AfterAll.Player
{
    /// <summary>
    /// Drives VHS / found-footage post-processing effects via a URP Volume.
    /// Attach to the same GameObject as the Global Volume.
    /// Sprint panic and random static bursts are driven at runtime.
    /// </summary>
    [RequireComponent(typeof(Volume))]
    public class FoundFootageVolume : MonoBehaviour
    {
        [Header("Master")]
        [SerializeField] private bool _active = true;
        [SerializeField] [Range(0f, 1f)] private float _masterIntensity = 1f;

        [Header("Film Grain")]
        [SerializeField] private bool _grainEnabled = true;
        [SerializeField] private FilmGrainLookup _grainType = FilmGrainLookup.Medium1;
        [SerializeField] [Range(0f, 1f)] private float _grainIntensity = 0.38f;
        [SerializeField] [Range(0f, 1f)] private float _grainLuminance = 0.75f;

        [Header("Chromatic Aberration")]
        [SerializeField] private bool _chromaticEnabled = true;
        [SerializeField] [Range(0f, 1f)] private float _chromaticBase = 0.14f;
        [SerializeField] [Range(0f, 0.5f)] private float _chromaticPulseAmplitude = 0.05f;
        [SerializeField] [Range(0.05f, 2f)] private float _chromaticPulseHz = 0.3f;

        [Header("Lens Distortion")]
        [SerializeField] private bool _lensDistortionEnabled = true;
        [SerializeField] [Range(-1f, 0f)] private float _lensDistortionBase = -0.08f;
        [SerializeField] [Range(0f, 0.5f)] private float _lensDistortionPulse = 0.02f;
        [SerializeField] [Range(0.05f, 2f)] private float _lensDistortionPulseHz = 0.15f;

        [Header("Color Grading (VHS tint)")]
        [SerializeField] private bool _colorGradingEnabled = true;
        [Tooltip("Negative = desaturate. Range -100 to 100.")]
        [SerializeField] [Range(-100f, 0f)] private float _saturation = -22f;
        [Tooltip("Slight green-yellow VHS hue shift.")]
        [SerializeField] [Range(-180f, 180f)] private float _hueShift = 6f;
        [Tooltip("Slight exposure pull-down for that dim tape look.")]
        [SerializeField] [Range(-2f, 2f)] private float _postExposure = -0.12f;

        [Header("Sprint Panic")]
        [SerializeField] private bool _panicEnabled = true;
        [SerializeField] [Range(0f, 1f)] private float _panicChromaticAdd = 0.25f;
        [SerializeField] [Range(0f, 0.5f)] private float _panicLensAdd = 0.12f;
        [SerializeField] [Range(0f, 0.3f)] private float _panicGrainAdd = 0.15f;
        [SerializeField] [Range(1f, 10f)] private float _panicSmoothing = 4f;

        [Header("Static Bursts")]
        [SerializeField] private bool _staticBurstsEnabled = true;
        [SerializeField] [Range(0f, 30f)] private float _burstIntervalMin = 5f;
        [SerializeField] [Range(0f, 60f)] private float _burstIntervalMax = 20f;
        [SerializeField] [Range(0f, 0.5f)] private float _burstChromaticStrength = 0.4f;
        [SerializeField] [Range(0f, 0.3f)] private float _burstLensStrength = 0.15f;
        [SerializeField] [Range(0.05f, 0.5f)] private float _burstDuration = 0.12f;

        // ── runtime refs ──────────────────────────────────────────────────────
        private Volume _volume;
        private ChromaticAberration _chromatic;
        private FilmGrain _filmGrain;
        private LensDistortion _lensDistortion;
        private ColorAdjustments _colorAdjustments;

        private PlayerMovement _movement;

        // ── state ─────────────────────────────────────────────────────────────
        private float _pulseTimer;
        private float _lensPulseTimer;
        private float _currentPanicT;

        private float _burstTimer;
        private float _nextBurstTime;
        private float _burstElapsed;
        private bool _inBurst;

        private void Awake()
        {
            _volume = GetComponent<Volume>();
            _movement = GetComponentInParent<PlayerMovement>() ?? FindAnyObjectByType<PlayerMovement>();

            // Clone the shared profile so runtime writes never dirty the asset on disk.
            var profile = Instantiate(_volume.sharedProfile);
            _volume.profile = profile;
            if (profile == null) return;

            Acquire<ChromaticAberration>(ref _chromatic, profile);
            Acquire<FilmGrain>(ref _filmGrain, profile);
            Acquire<LensDistortion>(ref _lensDistortion, profile);
            Acquire<ColorAdjustments>(ref _colorAdjustments, profile);

            ApplyStaticSettings();
            ScheduleNextBurst();
        }

        private void ApplyStaticSettings()
        {
            // Film grain — static values set once; only intensity changes at runtime if needed.
            if (_filmGrain != null)
            {
                _filmGrain.active = _grainEnabled;
                _filmGrain.type.overrideState = true;
                _filmGrain.type.value = _grainType;
                _filmGrain.intensity.overrideState = true;
                _filmGrain.intensity.value = _grainIntensity * _masterIntensity;
                _filmGrain.response.overrideState = true;
                _filmGrain.response.value = _grainLuminance;
            }

            // Color adjustments — static VHS tint, not animated.
            if (_colorAdjustments != null)
            {
                _colorAdjustments.active = _colorGradingEnabled;
                _colorAdjustments.saturation.overrideState = true;
                _colorAdjustments.saturation.value = _saturation;
                _colorAdjustments.hueShift.overrideState = true;
                _colorAdjustments.hueShift.value = _hueShift;
                _colorAdjustments.postExposure.overrideState = true;
                _colorAdjustments.postExposure.value = _postExposure;
            }

            if (_chromatic != null)
                _chromatic.active = _chromaticEnabled;

            if (_lensDistortion != null)
                _lensDistortion.active = _lensDistortionEnabled;
        }

        private void Update()
        {
            if (!_active) return;

            float sprintT = (_panicEnabled && _movement != null) ? _movement.SprintT : 0f;
            _currentPanicT = Mathf.Lerp(_currentPanicT, sprintT, _panicSmoothing * Time.deltaTime);

            _pulseTimer += Time.deltaTime;
            _lensPulseTimer += Time.deltaTime;

            UpdateBurst();
            UpdateChromatic();
            UpdateLensDistortion();
            UpdateGrainPanic();
        }

        private void UpdateChromatic()
        {
            if (_chromatic == null) return;

            float pulse = Mathf.Sin(_pulseTimer * Mathf.PI * 2f * _chromaticPulseHz) * _chromaticPulseAmplitude;
            float panicAdd = _currentPanicT * _panicChromaticAdd;
            float burstAdd = _inBurst ? _burstChromaticStrength * BurstEnvelope() : 0f;

            _chromatic.intensity.value = Mathf.Clamp01(
                (_chromaticBase + pulse + panicAdd + burstAdd) * _masterIntensity
            );
        }

        private void UpdateLensDistortion()
        {
            if (_lensDistortion == null) return;

            float pulse = Mathf.Sin(_lensPulseTimer * Mathf.PI * 2f * _lensDistortionPulseHz) * _lensDistortionPulse;
            float panicAdd = _currentPanicT * _panicLensAdd;
            float burstAdd = _inBurst ? _burstLensStrength * BurstEnvelope() : 0f;

            _lensDistortion.intensity.overrideState = true;
            _lensDistortion.intensity.value = Mathf.Clamp(
                (_lensDistortionBase - pulse - panicAdd - burstAdd) * _masterIntensity,
                -1f, 1f
            );
        }

        private void UpdateGrainPanic()
        {
            if (_filmGrain == null) return;

            float panicAdd = _currentPanicT * _panicGrainAdd;
            _filmGrain.intensity.value = Mathf.Clamp01((_grainIntensity + panicAdd) * _masterIntensity);
        }

        // ── static bursts ──────────────────────────────────────────────────────

        private void UpdateBurst()
        {
            if (!_staticBurstsEnabled) return;

            _burstTimer += Time.deltaTime;

            if (!_inBurst && _burstTimer >= _nextBurstTime)
            {
                _inBurst = true;
                _burstElapsed = 0f;
            }

            if (_inBurst)
            {
                _burstElapsed += Time.deltaTime;
                if (_burstElapsed >= _burstDuration)
                {
                    _inBurst = false;
                    _burstTimer = 0f;
                    ScheduleNextBurst();
                }
            }
        }

        // Triangle envelope: ramps up to peak at mid-burst then back down — avoids hard pop.
        private float BurstEnvelope()
        {
            float t = Mathf.Clamp01(_burstElapsed / _burstDuration);
            return 1f - Mathf.Abs(t * 2f - 1f);
        }

        private void ScheduleNextBurst()
        {
            _nextBurstTime = Random.Range(_burstIntervalMin, _burstIntervalMax);
        }

        // ── helpers ────────────────────────────────────────────────────────────

        private static void Acquire<T>(ref T field, VolumeProfile profile) where T : VolumeComponent
        {
            if (!profile.TryGet(out field))
                field = profile.Add<T>(false);
            field.active = true;
        }

        // ── public API ─────────────────────────────────────────────────────────

        public void SetMasterIntensity(float t)
        {
            _masterIntensity = Mathf.Clamp01(t);
            ApplyStaticSettings();
        }

        public void TriggerBurst()
        {
            _inBurst = true;
            _burstElapsed = 0f;
        }
    }
}
