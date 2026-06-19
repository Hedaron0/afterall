using AfterAll.Audio;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AfterAll.Player
{
    [RequireComponent(typeof(CharacterController))]
    public class PlayerMovement : MonoBehaviour
    {
        [SerializeField] private float moveSpeed = 4f;
        [SerializeField] private float gravity = -9.81f;
        [SerializeField] private InputActionReference moveAction;

        public float MoveSpeed => moveSpeed;

        private CharacterController _controller;
        private FootstepAudio _footsteps;
        private Vector3 _velocity;

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _footsteps = GetComponent<FootstepAudio>();
        }

        private void OnEnable()  => moveAction.action.Enable();
        private void OnDisable() => moveAction.action.Disable();

        private void Update()
        {
            Vector2 input = moveAction.action.ReadValue<Vector2>();
            Vector3 move = transform.right * input.x + transform.forward * input.y;
            Vector3 horizontalMove = move.normalized * moveSpeed * Time.deltaTime;
            _controller.Move(horizontalMove);

            if (_footsteps != null)
                _footsteps.RegisterMovement(horizontalMove.magnitude, _controller.isGrounded);

            if (_controller.isGrounded && _velocity.y < 0f)
                _velocity.y = 0f;

            _velocity.y += gravity * Time.deltaTime;
            _controller.Move(_velocity * Time.deltaTime);
        }
    }
}
