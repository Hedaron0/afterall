using UnityEngine;
using UnityEngine.InputSystem;

namespace AfterAll.Player
{
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(AudioSource))]
    public class PlayerMovement : MonoBehaviour
    {
        [Header("Movement")]
        [SerializeField] private float moveSpeed          = 4f;
        [SerializeField] private float sprintSpeed        = 7.5f;
        [SerializeField] private float crouchSpeed        = 2f;
        [SerializeField] private float sprintAcceleration = 3f;
        [SerializeField] private float acceleration       = 12f;
        [SerializeField] private float deceleration       = 9f;

        [Header("Gravity")]
        [SerializeField] private float gravity            = -18f;

        [Header("Jump")]
        [SerializeField] private float jumpHeight         = 1.1f;
        [Tooltip("How much ground acceleration applies in the air (0 = no air steering, 1 = full).")]
        [SerializeField] [Range(0f, 1f)] private float airControl = 0.4f;

        [Header("Crouch")]
        [SerializeField] private float crouchHeight           = 1.1f;
        [SerializeField] private float crouchSmoothTime       = 0.12f;
        private float _standHeight;
        private float _standCenterY;
        private float _crouchCenterY;
        private float _crouchTVelocity;

        [Header("Actions")]
        [SerializeField] private InputActionReference moveAction;
        [SerializeField] private InputActionReference sprintAction;
        [SerializeField] private InputActionReference jumpAction;
        [SerializeField] private InputActionReference crouchAction;

        [Header("Footsteps")]
        [SerializeField] private AudioClip[] _footstepClips;
        [SerializeField] private float _stepDistance       = 1.35f;
        [SerializeField] private float _sprintStepDistance = 1.85f;
        [SerializeField] private float _footstepVolume     = 0.6f;

        // ── Public state ──────────────────────────────────────────────────────
        public float   SprintT       { get; private set; }
        public float   MoveMagnitude { get; private set; }
        public float   CrouchT       { get; private set; }
        public bool    IsCrouching   { get; private set; }
        public bool    IsGrounded    => _controller.isGrounded || CheckGroundRay();
        public float   MoveSpeed     => moveSpeed;
        public Vector2 StrafeInput   { get; private set; }
        public bool    JustLanded    { get; private set; }
        public bool    JustJumped    { get; private set; }

        private CharacterController _controller;
        private AudioSource         _footstepSource;
        private Stamina             _stamina;
        private Vector3 _horizontalVelocity;
        private float   _verticalVelocity;
        private float   _distanceSinceStep;
        private int     _nextClipIndex;
        private bool    _wasGrounded;
        private Vector2 _mobileMove;

        public void SetMobileMove(Vector2 input) => _mobileMove = input;

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _footstepSource = GetComponent<AudioSource>();
            _stamina = GetComponent<Stamina>();
            _footstepSource.playOnAwake = false;
            _footstepSource.loop = false;
            _footstepSource.spatialBlend = 0f;

            if (_footstepClips != null && _footstepClips.Length > 0)
                _footstepSource.clip = _footstepClips[0];

            _standHeight   = _controller.height;
            _standCenterY  = _controller.center.y;
            _crouchCenterY = _standCenterY - (_standHeight - crouchHeight) * 0.5f;
        }

        private void OnEnable()
        {
            moveAction.action.Enable();
            if (sprintAction != null) sprintAction.action.Enable();
            if (jumpAction   != null) jumpAction.action.Enable();
            if (crouchAction != null) crouchAction.action.Enable();
        }

        private void OnDisable()
        {
            moveAction.action.Disable();
            if (sprintAction != null) sprintAction.action.Disable();
            if (jumpAction   != null) jumpAction.action.Disable();
            if (crouchAction != null) crouchAction.action.Disable();
        }

        private void Update()
        {
            bool grounded = IsGrounded;

            JustLanded  = !_wasGrounded && grounded && _verticalVelocity < -1f;
            JustJumped  = false;
            _wasGrounded = grounded;

            UpdateCrouch();

            bool pcSprint     = sprintAction != null && sprintAction.action.IsPressed();
            bool mobileSprint = AfterAll.UI.MobileSprintBridge.WantsSprint;
            bool wantSprint   = !IsCrouching
                && (pcSprint || mobileSprint)
                && (_stamina == null || _stamina.CanSprint);

            SprintT = Mathf.MoveTowards(SprintT, wantSprint ? 1f : 0f, sprintAcceleration * Time.deltaTime);

            bool isSprinting = SprintT > 0.01f;
            bool isMoving    = MoveMagnitude > 0.1f;
            _stamina?.Tick(isSprinting, isMoving, IsCrouching);

            float topSpeed = IsCrouching
                ? crouchSpeed
                : Mathf.Lerp(moveSpeed, sprintSpeed, SprintT);

            Vector2 input = AfterAll.UI.MobileInput.IsActive
                ? _mobileMove
                : moveAction.action.ReadValue<Vector2>();
            StrafeInput = input;

            Vector3 wishDir = transform.right * input.x + transform.forward * input.y;
            if (wishDir.sqrMagnitude > 1f) wishDir.Normalize();

            Vector3 targetHorizontal = wishDir * topSpeed;

            // Same acceleration/deceleration as ground but scaled by airControl in the air.
            float controlScale = grounded ? 1f : airControl;
            float rate = wishDir.sqrMagnitude > 0.01f ? acceleration : deceleration;
            _horizontalVelocity = Vector3.MoveTowards(
                _horizontalVelocity, targetHorizontal, rate * controlScale * Time.deltaTime);

            MoveMagnitude = _horizontalVelocity.magnitude;

            // Vertical
            if (grounded && _verticalVelocity < 0f)
                _verticalVelocity = -2f;

            if (grounded && !IsCrouching &&
                jumpAction != null && jumpAction.action.WasPressedThisFrame())
            {
                _verticalVelocity = Mathf.Sqrt(2f * Mathf.Abs(gravity) * jumpHeight);
                JustJumped = true;
            }

            // Past the apex (v < 2): apply 1.8× gravity so the top of the arc
            // doesn't linger. Ascent is untouched — only the hang-point + descent snap.
            float gMult = _verticalVelocity < 2f ? 1.8f : 1f;
            _verticalVelocity += gravity * gMult * Time.deltaTime;

            _controller.Move((_horizontalVelocity + Vector3.up * _verticalVelocity) * Time.deltaTime);

            float effectiveStepDist = IsCrouching
                ? _stepDistance * 1.5f
                : Mathf.Lerp(_stepDistance, _sprintStepDistance, SprintT);
            UpdateFootsteps(MoveMagnitude * Time.deltaTime, effectiveStepDist);
        }

        private void UpdateCrouch()
        {
            bool wantCrouch = crouchAction != null && crouchAction.action.IsPressed();

            if (IsCrouching && !wantCrouch && CeilingCheck())
                wantCrouch = true;

            IsCrouching = wantCrouch;

            float target = IsCrouching ? 1f : 0f;
            CrouchT = Mathf.SmoothDamp(CrouchT, target, ref _crouchTVelocity, crouchSmoothTime);

            _controller.height = Mathf.Lerp(_standHeight,  crouchHeight,  CrouchT);
            _controller.center = new Vector3(0f, Mathf.Lerp(_standCenterY, _crouchCenterY, CrouchT), 0f);
        }

        private bool CeilingCheck()
        {
            float radius = _controller.radius * 0.9f;
            Vector3 origin = transform.position + Vector3.up * (crouchHeight + 0.05f);
            return Physics.SphereCast(origin, radius, Vector3.up, out _, _standHeight - crouchHeight);
        }

        private void UpdateFootsteps(float distanceMoved, float stepDist)
        {
            if (!IsGrounded || distanceMoved <= 0f || _footstepClips == null || _footstepClips.Length == 0)
            {
                if (!IsGrounded) _distanceSinceStep = 0f;
                return;
            }

            _distanceSinceStep += distanceMoved;
            while (_distanceSinceStep >= stepDist)
            {
                _distanceSinceStep -= stepDist;
                PlayFootstep();
            }
        }

        private bool CheckGroundRay()
        {
            float rayLength = (_controller.height * 0.5f) + 0.15f;
            return Physics.Raycast(transform.position, Vector3.down, rayLength);
        }

        private void PlayFootstep()
        {
            AudioClip clip = _footstepClips[_nextClipIndex % _footstepClips.Length];
            _nextClipIndex++;
            _footstepSource.PlayOneShot(clip, _footstepVolume);
        }
    }
}
