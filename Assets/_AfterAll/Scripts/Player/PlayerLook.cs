using UnityEngine;

namespace AfterAll.Player
{
    public class PlayerLook : MonoBehaviour
    {
        [SerializeField] private Transform cameraPivot;
        [SerializeField] private float mouseSensitivity = 2f;
        [SerializeField] private float minPitch = -80f;
        [SerializeField] private float maxPitch = 80f;

        private float _pitch;

        private void Start()
        {
            Cursor.lockState = CursorLockMode.Locked;
        }

        private void Update()
        {
            float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
            float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;

            transform.Rotate(Vector3.up * mouseX);

            _pitch -= mouseY;
            _pitch = Mathf.Clamp(_pitch, minPitch, maxPitch);
            cameraPivot.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }
    }
}
