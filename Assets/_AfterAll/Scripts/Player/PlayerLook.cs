using System.Collections.Generic;
using AfterAll.Interaction;
using AfterAll.UI;
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

        private int _lookFingerId = -1;
        private Vector2 _lookStartPos;
        private float _lookStartTime;
        private float _lookTotalDrag;
        private bool _lookDragActive;

        private readonly List<RaycastResult> _raycastResults = new();

        private PlayerMovement _movement;
        private PlayerInteractor _interactor;

        private void Start()
        {
            _movement   = GetComponent<PlayerMovement>();
            _interactor = GetComponent<PlayerInteractor>();
            UpdateCursorState();
        }

        private void OnEnable()
        {
            lookAction.action.Enable();
            EnhancedTouchSupport.Enable();
            UpdateCursorState();
        }

        private void OnDisable()
        {
            lookAction.action.Disable();
            UpdateCursorState();
        }

        private void Update()
        {
            UpdateCursorState();

            _targetDelta = MobileInput.IsActive
                ? ProcessMobileLookTouch()
                : lookAction.action.ReadValue<Vector2>() * mouseSensitivity;

            _currentDelta = Vector2.Lerp(_currentDelta, _targetDelta, smoothing * Time.deltaTime);
            ApplyLook(_currentDelta);
            _targetDelta = Vector2.zero;
        }

        // ── mobile: drag = look, tap = interact ─────────────────────────────

        private Vector2 ProcessMobileLookTouch()
        {
            float zoneEdge = Screen.width * MobileInput.MoveZoneWidth;

            foreach (var touch in Touch.activeTouches)
            {
                if (touch.phase == TouchPhase.Began &&
                    _lookFingerId == -1 &&
                    touch.screenPosition.x >= zoneEdge &&
                    !IsTouchOverUI(touch.screenPosition))
                {
                    _lookFingerId    = touch.finger.index;
                    _lookStartPos    = touch.screenPosition;
                    _lookStartTime   = Time.unscaledTime;
                    _lookTotalDrag   = 0f;
                    _lookDragActive  = false;
                }
            }

            if (_lookFingerId == -1)
                return Vector2.zero;

            foreach (var touch in Touch.activeTouches)
            {
                if (touch.finger.index != _lookFingerId)
                    continue;

                if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
                {
                    TryTapInteract();
                    ResetLookTouch();
                    return Vector2.zero;
                }

                _lookTotalDrag += touch.delta.magnitude;

                if (!_lookDragActive &&
                    Vector2.Distance(touch.screenPosition, _lookStartPos) >= MobileInput.TapDeadzonePixels)
                {
                    _lookDragActive = true;
                }

                return _lookDragActive ? touch.delta * touchSensitivity : Vector2.zero;
            }

            ResetLookTouch();
            return Vector2.zero;
        }

        private void TryTapInteract()
        {
            if (_lookDragActive) return;
            if (_lookTotalDrag >= MobileInput.TapDeadzonePixels) return;
            if (Time.unscaledTime - _lookStartTime > MobileInput.MaxTapDuration) return;
            _interactor?.TryInteract();
        }

        private void ResetLookTouch()
        {
            _lookFingerId   = -1;
            _lookDragActive = false;
        }

        private bool IsTouchOverUI(Vector2 screenPosition)
        {
            if (EventSystem.current == null)
                return false;

            var pointerData = new PointerEventData(EventSystem.current) { position = screenPosition };
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

            float strafeX = _movement != null ? _movement.StrafeInput.x : 0f;
            float targetRoll = -strafeX * 1.5f;
            _currentRoll = Mathf.Lerp(_currentRoll, targetRoll, 6f * Time.deltaTime);

            cameraPivot.localRotation = Quaternion.Euler(_pitch, 0f, _currentRoll);
        }

        private void UpdateCursorState()
        {
            if (MobileInput.IsActive)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                return;
            }

            if (!Application.isMobilePlatform)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }
    }
}
