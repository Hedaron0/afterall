using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace AfterAll.Player
{
    public class PlayerLook : MonoBehaviour
    {
        [SerializeField] private Transform cameraPivot;
        [SerializeField] private float mouseSensitivity = 0.15f;
        [SerializeField] private float touchSensitivity = 0.35f;
        [SerializeField] private float smoothing = 8f;
        [SerializeField] private float minPitch = -80f;
        [SerializeField] private float maxPitch = 80f;
        [SerializeField] private InputActionReference lookAction;

        private float _pitch;
        private Vector2 _currentDelta;
        private Vector2 _targetDelta;

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

        private void Start()
        {
            Cursor.lockState = CursorLockMode.Locked;
        }

        private void Update()
        {
            if (Touchscreen.current != null)
                _targetDelta = GetTouchDelta();
            else
                _targetDelta = lookAction.action.ReadValue<Vector2>() * mouseSensitivity;

            _currentDelta = Vector2.Lerp(_currentDelta, _targetDelta, smoothing * Time.deltaTime);

            ApplyLook(_currentDelta);

            // Reset target each frame so camera stops when finger lifts
            _targetDelta = Vector2.zero;
        }

        private Vector2 GetTouchDelta()
        {
            float screenHalfWidth = Screen.width * 0.5f;
            Vector2 delta = Vector2.zero;

            foreach (Touch touch in Touch.activeTouches)
            {
                if (touch.startScreenPosition.x < screenHalfWidth)
                    continue;

                if (touch.phase == UnityEngine.InputSystem.TouchPhase.Moved)
                    delta += touch.delta * touchSensitivity;
            }

            return delta;
        }

        private void ApplyLook(Vector2 delta)
        {
            transform.Rotate(Vector3.up * delta.x);
            _pitch -= delta.y;
            _pitch = Mathf.Clamp(_pitch, minPitch, maxPitch);
            cameraPivot.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }
    }
}
