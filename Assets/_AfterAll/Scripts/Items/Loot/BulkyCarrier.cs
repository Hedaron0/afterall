using AfterAll.Items;
using AfterAll.Player;
using AfterAll.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AfterAll.Items.Loot
{
    /// <summary>
    /// S3 hands-carry for Bulky Echoes (Core Design §6b): one at a time, R.E.P.O.-style physical
    /// hold — the actual world Rigidbody is grabbed (not consumed into abstract inventory data),
    /// suspended in front of the camera by a spring force (gravity off while held, so it sways
    /// with camera movement but still collides with walls/floor), and thrown with an impulse whose
    /// resulting speed is naturally mass-dependent (fixed impulse / mass = velocity, standard
    /// physics — no manual mass math needed). Blocks sprint + applies a flat move-speed penalty
    /// while carrying. WorldItem special-cases Bulky Loot items to call TryGrab directly instead
    /// of going through the normal IItemReceiver pickup pipeline.
    /// </summary>
    [RequireComponent(typeof(PlayerMovement))]
    public class BulkyCarrier : MonoBehaviour
    {
        [Header("Hold — position")]
        [SerializeField] private float _holdDistance = 1.2f;
        [SerializeField] private float _followStiffness = 140f;
        [SerializeField] private float _followDamping = 14f;

        [Header("Hold — rotation sway (turn-speed-driven tilt)")]
        [Tooltip("Degrees of tilt per (deg/sec) of camera turn speed — how strongly a quick look-turn banks the object.")]
        [SerializeField] private float _tiltSensitivity = 0.12f;
        [Tooltip("Hard clamp — the object can never tilt further than this either direction, however fast you turn.")]
        [SerializeField] private float _maxTiltDeg = 25f;
        [Tooltip("Spring pulling the tilt toward its target/back to neutral — higher = snappier, lower = floatier.")]
        [SerializeField] private float _tiltSpringStiffness = 45f;
        [Tooltip("Damping on the tilt spring — higher = settles faster with less overshoot, lower = more of a pendulum swing.")]
        [SerializeField] private float _tiltSpringDamping = 7f;

        [SerializeField, Range(0f, 1f)] private float _speedMultiplier = 0.6f;

        [Header("Throw")]
        [SerializeField] private float _throwImpulse = 6f;
        [Tooltip("Tiny random tumble nudge on release so a thrown item doesn't fly perfectly flat — the physics itself takes it from there, keep this small (2026-07-20: 0.8 was way too much, 0.15 still too much).")]
        [SerializeField] private float _throwSpinImpulse = 0.05f;
        [Tooltip("Primary throw input — bind to the existing 'Attack' action (left mouse), left free of other uses.")]
        [SerializeField] private InputActionReference throwAction;
        [Tooltip("Secondary throw/drop input — bind to the 'Drop' action (default G).")]
        [SerializeField] private InputActionReference dropAction;

        private PlayerMovement _movement;
        private Camera _camera;
        private Rigidbody _heldBody;
        private WorldItem _heldWorldItem;
        private Quaternion _grabBaseRotation;
        private float _lastCameraYaw;
        private float _currentTiltDeg;
        private float _tiltVelocityDeg;

        public ItemDefinition Carried { get; private set; }
        public bool IsCarrying => Carried != null;

        private void Awake()
        {
            _movement = GetComponent<PlayerMovement>();
            _camera = GetComponentInChildren<Camera>();
        }

        private void OnEnable()
        {
            if (throwAction != null) throwAction.action.Enable();
            if (dropAction != null) dropAction.action.Enable();
        }

        private void OnDisable()
        {
            if (throwAction != null) throwAction.action.Disable();
            if (dropAction != null) dropAction.action.Disable();
        }

        private void Update()
        {
            if (!IsCarrying)
                return;

            bool wantsThrow =
                (throwAction != null && throwAction.action.WasPressedThisFrame()) ||
                (dropAction != null && dropAction.action.WasPressedThisFrame());

            if (wantsThrow)
                TryThrow();
        }

        private void FixedUpdate()
        {
            if (!IsCarrying || _heldBody == null || _camera == null)
                return;

            // Position: spring toward a hold point in front of the camera.
            Vector3 targetPos = _camera.transform.position + _camera.transform.forward * _holdDistance;
            Vector3 toTarget = targetPos - _heldBody.position;
            Vector3 accel = toTarget * _followStiffness - _heldBody.linearVelocity * _followDamping;
            _heldBody.AddForce(accel, ForceMode.Acceleration);

            // Rotation: fully authored/kinematic, not physics torque (an earlier torque-based
            // attempt fought itself and was too subtle/unpredictable — Harun's ask, 2026-07-20).
            // Tilt target = how fast the camera is turning right now, clamped; a critically-ish
            // damped spring eases the visible tilt toward that target and swings back to neutral
            // when you stop turning. MoveRotation overrides physics rotation outright, so there's
            // no fighting and no possibility of tumbling regardless of tuning.
            float yaw = _camera.transform.eulerAngles.y;
            float yawDelta = Mathf.DeltaAngle(_lastCameraYaw, yaw);
            _lastCameraYaw = yaw;
            float yawSpeed = Time.fixedDeltaTime > 0f ? yawDelta / Time.fixedDeltaTime : 0f;

            float targetTilt = Mathf.Clamp(-yawSpeed * _tiltSensitivity, -_maxTiltDeg, _maxTiltDeg);
            float springForce = _tiltSpringStiffness * (targetTilt - _currentTiltDeg) - _tiltSpringDamping * _tiltVelocityDeg;
            _tiltVelocityDeg += springForce * Time.fixedDeltaTime;
            _currentTiltDeg = Mathf.Clamp(_currentTiltDeg + _tiltVelocityDeg * Time.fixedDeltaTime, -_maxTiltDeg, _maxTiltDeg);

            Quaternion tilt = Quaternion.AngleAxis(_currentTiltDeg, _camera.transform.forward);
            _heldBody.angularVelocity = Vector3.zero;
            _heldBody.MoveRotation(tilt * _grabBaseRotation);
        }

        /// <summary>Grabs the live world object (kept active/simulated, not consumed) — hands must be empty and the item Bulky Loot with a Rigidbody.</summary>
        public bool TryGrab(WorldItem worldItem)
        {
            if (IsCarrying || worldItem == null || worldItem.Item == null)
                return false;

            if (!IsBulky(worldItem.Item))
                return false;

            Rigidbody rb = worldItem.GetComponent<Rigidbody>();
            if (rb == null)
            {
                Debug.LogWarning($"[AfterAll] BulkyCarrier: {worldItem.Item.DisplayName} has no Rigidbody, can't be grabbed.");
                return false;
            }

            Carried = worldItem.Item;
            _heldWorldItem = worldItem;
            _heldBody = rb;
            // Once grabbed, the item belongs to the player, not the floor: floor-spawned items are
            // parented under their room, and ClearLevelRoot would destroy them out of the player's
            // hands (or out of the elevator stash) on the next floor rebuild.
            _heldBody.transform.SetParent(null, worldPositionStays: true);
            _heldBody.useGravity = false;
            _heldBody.linearVelocity = Vector3.zero;
            _heldBody.angularVelocity = Vector3.zero;
            _grabBaseRotation = _heldBody.rotation;
            _lastCameraYaw = _camera != null ? _camera.transform.eulerAngles.y : 0f;
            _currentTiltDeg = 0f;
            _tiltVelocityDeg = 0f;

            _movement.SetSpeedModifier(this, _speedMultiplier);
            _movement.SetSprintBlocked(this, true);

            GameFeedbackUI.Show($"{Carried.DisplayName} picked up.");
            return true;
        }

        /// <summary>Throws the carried item with a mass-independent impulse (heavier items naturally end up slower — F=ma).</summary>
        public bool TryThrow()
        {
            if (!IsCarrying)
                return false;

            Vector3 throwDir = _camera != null ? _camera.transform.forward : transform.forward;
            _heldBody.useGravity = true;
            _heldBody.AddForce(throwDir * _throwImpulse, ForceMode.Impulse);

            // The hold phase zeroes angularVelocity every frame (kinematic tilt owns rotation
            // while held), so without this the object leaves the hand with zero spin and flies
            // dead flat/straight — give it a small random tumble impulse so it reads as a thrown
            // physical object, not a projectile on rails.
            Vector3 randomSpinAxis = new Vector3(
                Random.Range(-1f, 1f),
                Random.Range(-1f, 1f),
                Random.Range(-1f, 1f)).normalized;
            _heldBody.AddTorque(randomSpinAxis * _throwSpinImpulse, ForceMode.Impulse);

            GameFeedbackUI.Show($"{Carried.DisplayName} thrown.");
            // S4: hurling a bulky object is loud — the hunter hears it across several rooms.
            Entities.NoiseEvents.Report(transform.position, 14f);
            ReleaseHeld();
            return true;
        }

        /// <summary>Value without clearing — for UI/prompt text.</summary>
        public int PeekValue() =>
            IsCarrying && EchoDefinition.TryGetFor(Carried, out EchoDefinition def) ? def.Value : 0;

        /// <summary>Value of the carried item, then releases it in place (gravity restored, no throw). Call on deposit/extract.</summary>
        public int Bank()
        {
            int value = PeekValue();
            if (_heldBody != null)
                _heldBody.useGravity = true;
            ReleaseHeld();
            return value;
        }

        /// <summary>Drops the carry state without banking or restoring the held object's physics. Call on player death.</summary>
        public void Clear()
        {
            if (_heldBody != null)
                _heldBody.useGravity = true;
            ReleaseHeld();
        }

        private void ReleaseHeld()
        {
            Carried = null;
            _heldBody = null;
            _heldWorldItem = null;
            _movement.ClearSpeedModifier(this);
            _movement.SetSprintBlocked(this, false);
        }

        private static bool IsBulky(ItemDefinition item) =>
            item.Category == ItemCategory.Loot &&
            EchoDefinition.TryGetFor(item, out EchoDefinition def) &&
            def.SizeClass == EchoSizeClass.Bulky;
    }
}
