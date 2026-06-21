using System.Collections.Generic;
using AfterAll.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

namespace AfterAll.Player
{
    /// <summary>
    /// Invisible virtual stick on the left screen zone — no joystick UI.
    /// Touches on UI (crouch strip) are ignored so they don't steal movement.
    /// </summary>
    [RequireComponent(typeof(PlayerMovement))]
    public class MobileTouchMove : MonoBehaviour
    {
        [SerializeField] private float _stickRadius  = 90f;
        [SerializeField] private float _stickDeadzone = 0.12f;

        private PlayerMovement _movement;
        private int _moveFingerId = -1;
        private Vector2 _stickOrigin;
        private readonly List<RaycastResult> _raycastResults = new();

        private void Awake() => _movement = GetComponent<PlayerMovement>();

        private void OnEnable()  => EnhancedTouchSupport.Enable();
        private void OnDisable() => EnhancedTouchSupport.Disable();

        private void Update()
        {
            if (!MobileInput.IsActive)
            {
                _movement.SetMobileMove(Vector2.zero);
                return;
            }

            float zoneEdge = Screen.width * MobileInput.MoveZoneWidth;
            Vector2 move = Vector2.zero;

            foreach (var touch in Touch.activeTouches)
            {
                if (touch.phase == TouchPhase.Began &&
                    _moveFingerId == -1 &&
                    touch.screenPosition.x < zoneEdge &&
                    !IsTouchOverUI(touch.screenPosition))
                {
                    _moveFingerId = touch.finger.index;
                    _stickOrigin  = touch.screenPosition;
                }
            }

            if (_moveFingerId != -1)
            {
                bool found = false;
                foreach (var touch in Touch.activeTouches)
                {
                    if (touch.finger.index != _moveFingerId)
                        continue;

                    found = true;
                    if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
                    {
                        _moveFingerId = -1;
                        break;
                    }

                    Vector2 offset = touch.screenPosition - _stickOrigin;
                    move = Vector2.ClampMagnitude(offset / _stickRadius, 1f);
                    if (move.magnitude < _stickDeadzone)
                        move = Vector2.zero;
                    break;
                }

                if (!found)
                    _moveFingerId = -1;
            }

            _movement.SetMobileMove(move);
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
    }
}
