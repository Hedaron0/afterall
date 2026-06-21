using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace AfterAll.Player
{
    public class CameraBob : MonoBehaviour
    {
        [Header("References — auto-resolved, override if needed")]
        [SerializeField] private PlayerMovement _movement;
        [SerializeField] private Transform _cameraPivot;
        [SerializeField] private Camera _camera;
        [SerializeField] private Volume _volume;

        [Header("FOV")]
        [SerializeField] private float _baseFOV          = 63f;
        [SerializeField] private float _sprintFOV        = 75f;
        [SerializeField] private float _crouchFOVOffset  = -3f;
        // Ramp-up speed (sprinting). Ramp-down runs at half this value so it
        // feels like the FOV is catching its breath after a sprint.
        [SerializeField] private float _fovSmoothing     = 8f;

        [Header("Head Bob")]
        [SerializeField] private float _walkBobFrequency    = 1.4f;
        [SerializeField] private float _sprintBobFrequency  = 2.2f;
        [SerializeField] private float _crouchBobFrequency  = 0.9f;
        [SerializeField] private float _walkBobHeight       = 0.045f;
        [SerializeField] private float _walkBobSide         = 0.022f;
        [SerializeField] private float _sprintBobMultiplier = 1.5f;
        [SerializeField] private float _crouchBobMultiplier = 0.55f;
        [SerializeField] private float _bobReturnSpeed      = 12f;

        [Header("Crouch Camera")]
        [SerializeField] private float _crouchPivotOffset = -0.55f;

        [Header("Sprint Vignette")]
        [SerializeField] private float _sprintVignetteMax = 0.30f;
        [SerializeField] private float _vignetteSmoothing = 5f;

        private float   _bobTimer;
        private Vector3 _defaultPivotPos;
        private Vector3 _bobOffset;
        private Vector3 _bobVelocity;
        private float   _currentZRot;
        private Vignette _vignette;
        private float   _currentVignetteIntensity;

        private void Awake()
        {
            if (_movement == null)
                _movement = GetComponent<PlayerMovement>();

            if (_camera == null)
                _camera = GetComponentInChildren<Camera>();

            if (_cameraPivot == null)
            {
                var pivot = transform.Find("CameraPivot");
                _cameraPivot = pivot != null ? pivot : (_camera != null ? _camera.transform.parent : null);
            }

            if (_volume == null)
                _volume = FindAnyObjectByType<Volume>();

            if (_cameraPivot != null)
                _defaultPivotPos = _cameraPivot.localPosition;

            SetupVignette();
        }

        private void SetupVignette()
        {
            if (_volume == null || _volume.profile == null) return;

            if (!_volume.profile.TryGet(out _vignette))
                _vignette = _volume.profile.Add<Vignette>(false);

            _vignette.active = true;
            _vignette.intensity.overrideState = true;
            _vignette.intensity.value = 0f;
        }

        private void Update()
        {
            if (_movement == null) return;
            UpdateFOV();
            UpdateBob();
            UpdateVignette();
        }

        // ── FOV ───────────────────────────────────────────────────────────────

        private void UpdateFOV()
        {
            if (_camera == null) return;

            float crouchOffset = _crouchFOVOffset * _movement.CrouchT;
            float target = Mathf.Lerp(_baseFOV, _sprintFOV, _movement.SprintT) + crouchOffset;

            float speed = target > _camera.fieldOfView ? _fovSmoothing : _fovSmoothing * 0.5f;
            _camera.fieldOfView = Mathf.Lerp(_camera.fieldOfView, target, speed * Time.deltaTime);
        }

        // ── Head bob ──────────────────────────────────────────────────────────

        private void UpdateBob()
        {
            if (_cameraPivot == null) return;

            bool isMoving = _movement.MoveMagnitude > 0.1f && _movement.IsGrounded;

            Vector3 targetOffset = Vector3.zero;

            if (isMoving)
            {
                float freq = _movement.IsCrouching
                    ? _crouchBobFrequency
                    : Mathf.Lerp(_walkBobFrequency, _sprintBobFrequency, _movement.SprintT);

                _bobTimer += Time.deltaTime * freq * Mathf.PI * 2f;

                float bobAmt = _movement.IsCrouching
                    ? _crouchBobMultiplier
                    : Mathf.Lerp(1f, _sprintBobMultiplier, _movement.SprintT);

                // Figure-8: vertical at full freq, horizontal at half freq offset by π/2.
                float yOff = Mathf.Sin(_bobTimer) * _walkBobHeight * bobAmt;
                float xOff = Mathf.Sin(_bobTimer * 0.5f + Mathf.PI * 0.5f) * _walkBobSide * bobAmt;
                targetOffset = new Vector3(xOff, yOff, 0f);
            }

            // Underdamped spring — slight overshoot when stopping simulates head weight.
            const float stiffness = 200f;
            const float damping   = 18f;
            Vector3 force = stiffness * (targetOffset - _bobOffset) - damping * _bobVelocity;
            _bobVelocity += force * Time.deltaTime;
            _bobOffset   += _bobVelocity * Time.deltaTime;

            float crouchY = Mathf.Lerp(0f, _crouchPivotOffset, _movement.CrouchT);
            _cameraPivot.localPosition = _defaultPivotPos + _bobOffset + new Vector3(0f, crouchY, 0f);

            // Z sway on camera, not pivot — avoids fighting PlayerLook's strafe roll.
            float targetZRot = isMoving ? Mathf.Sin(_bobTimer * 0.5f) * 0.4f : 0f;
            _currentZRot = Mathf.Lerp(_currentZRot, targetZRot,
                (isMoving ? 8f : _bobReturnSpeed) * Time.deltaTime);

            if (_camera != null)
                _camera.transform.localRotation = Quaternion.Euler(0f, 0f, _currentZRot);
        }

        // ── Vignette ──────────────────────────────────────────────────────────

        private void UpdateVignette()
        {
            if (_vignette == null) return;

            float target = _movement.SprintT * _sprintVignetteMax;
            _currentVignetteIntensity = Mathf.Lerp(
                _currentVignetteIntensity,
                target,
                _vignetteSmoothing * Time.deltaTime
            );
            _vignette.intensity.value = _currentVignetteIntensity;
        }
    }
}
