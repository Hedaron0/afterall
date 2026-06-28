using UnityEngine;
using UnityEngine.InputSystem;

namespace AfterAll.Items.Flashlight
{
    /// <summary>
    /// Weapon-style lag sway on held items. Headbob comes from parenting under the camera.
    /// </summary>
    public sealed class HeldViewmodelSway : MonoBehaviour
    {
        [SerializeField] private float _swayAmount = 0.025f;
        [SerializeField] private float _swaySmooth = 8f;
        [SerializeField] private float _maxSway = 0.08f;
        [SerializeField] private float _idleBobAmount = 0.004f;
        [SerializeField] private float _idleBobSpeed = 1.2f;

        private Vector3 _defaultLocalPos;
        private Quaternion _defaultLocalRot;
        private Vector3 _swayPos;
        private Vector3 _swayPosVel;
        private Vector2 _lookDelta;
        private InputAction _lookAction;
        private float _idleTimer;

        private void Awake()
        {
            _defaultLocalPos = transform.localPosition;
            _defaultLocalRot = transform.localRotation;
            ResolveLookAction();
        }

        private void OnEnable()
        {
            _lookAction?.Enable();
        }

        private void OnDisable()
        {
            _lookAction?.Disable();
        }

        private void LateUpdate()
        {
            if (_lookAction != null)
                _lookDelta = _lookAction.ReadValue<Vector2>();

            Vector3 targetSway = new Vector3(
                -_lookDelta.x * _swayAmount,
                -_lookDelta.y * _swayAmount,
                0f);
            targetSway = Vector3.ClampMagnitude(targetSway, _maxSway);

            _swayPos = Vector3.SmoothDamp(_swayPos, targetSway, ref _swayPosVel, 1f / _swaySmooth);

            _idleTimer += Time.deltaTime * _idleBobSpeed;
            float idleY = Mathf.Sin(_idleTimer) * _idleBobAmount;
            float idleX = Mathf.Sin(_idleTimer * 0.5f) * _idleBobAmount * 0.5f;

            transform.localPosition = _defaultLocalPos + _swayPos + new Vector3(idleX, idleY, 0f);

            float rotX = -_lookDelta.y * _swayAmount * 8f;
            float rotY = _lookDelta.x * _swayAmount * 8f;
            Quaternion swayRot = Quaternion.Euler(rotX, rotY, 0f);
            transform.localRotation = _defaultLocalRot * swayRot;
        }

        private void ResolveLookAction()
        {
            foreach (var asset in Resources.FindObjectsOfTypeAll<InputActionAsset>())
            {
                if (asset.name != "InputSystem_Actions")
                    continue;

                _lookAction = asset.FindActionMap("Player")?.FindAction("Look");
                return;
            }
        }
    }
}
