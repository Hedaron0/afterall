using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

namespace AfterAll.Player
{
    public class PlayerLook : MonoBehaviour
    {
        [SerializeField] private Transform cameraPivot;
        [SerializeField] private float mouseSensitivity = 0.15f;
        [SerializeField] private float touchSensitivity = 0.1f;
        [SerializeField] private float smoothing = 12f;
        [SerializeField] private float minPitch = -80f;
        [SerializeField] private float maxPitch = 80f;
        [SerializeField] private InputActionReference lookAction;

        private float _pitch;
        private float _currentRoll;
        private Vector2 _currentDelta;
        private Vector2 _targetDelta;

        // Index of the finger currently driving look (-1 = none).
        private int _lookFingerId = -1;

        // Reusable list for UI raycasting — avoids per-frame alloc.
        private readonly List<RaycastResult> _raycastResults = new List<RaycastResult>();

        private PlayerMovement _movement;

        private void Start()
        {
            _movement = GetComponent<PlayerMovement>();

            // Application.isMobilePlatform is false in the Editor even when the
            // Build Target is Android/iOS, so cursor lock always works in-editor.
            if (!Application.isMobilePlatform)
                Cursor.lockState = CursorLockMode.Locked;
        }

        private void OnEnable()
        {
            lookAction.action.Enable();
            EnhancedTouchSupport.Enable();
        }

        private void OnDisable()
        {
            lookAction.action.Disable();
            EnhancedTouchSupport.Disable();
        }

        private void Update()
        {
            // Application.isMobilePlatform is false in the Editor regardless of
            // Build Target, so mouse look always works during editor play.
            _targetDelta = Application.isMobilePlatform
                ? GetTouchLookDelta()
                : lookAction.action.ReadValue<Vector2>() * mouseSensitivity;

            _currentDelta = Vector2.Lerp(_currentDelta, _targetDelta, smoothing * Time.deltaTime);
            ApplyLook(_currentDelta);
            _targetDelta = Vector2.zero;
        }

        // ── touch look ────────────────────────────────────────────────────────

        private Vector2 GetTouchLookDelta()
        {
            float halfWidth = Screen.width * 0.5f;

            foreach (var touch in Touch.activeTouches)
            {
                // Pick up a new look finger: right-half start, not over any UI element.
                if (touch.phase == TouchPhase.Began &&
                    _lookFingerId == -1 &&
                    touch.screenPosition.x >= halfWidth &&
                    !IsTouchOverUI(touch.screenPosition))
                {
                    _lookFingerId = touch.finger.index;
                }
            }

            if (_lookFingerId == -1)
                return Vector2.zero;

            // Find the tracked finger and read its delta.
            foreach (var touch in Touch.activeTouches)
            {
                if (touch.finger.index != _lookFingerId)
                    continue;

                if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
                {
                    _lookFingerId = -1;
                    return Vector2.zero;
                }

                return touch.delta * touchSensitivity;
            }

            // Finger disappeared without an Ended/Canceled phase.
            _lookFingerId = -1;
            return Vector2.zero;
        }

        /// <summary>Returns true if the screen position is over any UI element.</summary>
        private bool IsTouchOverUI(Vector2 screenPosition)
        {
            if (EventSystem.current == null)
                return false;

            var pointerData = new PointerEventData(EventSystem.current)
            {
                position = screenPosition
            };

            _raycastResults.Clear();
            EventSystem.current.RaycastAll(pointerData, _raycastResults);
            return _raycastResults.Count > 0;
        }

        // ── shared ────────────────────────────────────────────────────────────

        private void ApplyLook(Vector2 delta)
        {
            transform.Rotate(Vector3.up * delta.x);
            _pitch -= delta.y;
            _pitch = Mathf.Clamp(_pitch, minPitch, maxPitch);

            // Z-axis roll based on horizontal strafe input — gives the camera a
            // weighted lean that reads as real body mass during lateral movement.
            float strafeX = _movement != null ? _movement.StrafeInput.x : 0f;
            float targetRoll = -strafeX * 1.5f;
            _currentRoll = Mathf.Lerp(_currentRoll, targetRoll, 6f * Time.deltaTime);

            cameraPivot.localRotation = Quaternion.Euler(_pitch, 0f, _currentRoll);
        }
    }
}
