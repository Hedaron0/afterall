using UnityEngine;

namespace AfterAll.Player
{
    [RequireComponent(typeof(CharacterController))]
    public class PlayerMovement : MonoBehaviour
    {
        [SerializeField] private float moveSpeed = 4f;
        [SerializeField] private float gravity = -9.81f;

        private CharacterController _controller;
        private Vector3 _velocity;

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
        }

        private void Update()
        {
            float horizontal = Input.GetAxisRaw("Horizontal");
            float vertical = Input.GetAxisRaw("Vertical");

            Vector3 move = transform.right * horizontal + transform.forward * vertical;
            _controller.Move(move.normalized * moveSpeed * Time.deltaTime);

            if (_controller.isGrounded && _velocity.y < 0f)
                _velocity.y = 0f;

            _velocity.y += gravity * Time.deltaTime;
            _controller.Move(_velocity * Time.deltaTime);
        }
    }
}
