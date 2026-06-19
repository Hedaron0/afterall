using UnityEngine;
using UnityEngine.InputSystem;

namespace AfterAll.Player
{
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(AudioSource))]
    public class PlayerMovement : MonoBehaviour
    {
        [SerializeField] private float moveSpeed = 4f;
        [SerializeField] private float gravity = -9.81f;
        [SerializeField] private InputActionReference moveAction;

        [Header("Footsteps")]
        [SerializeField] private AudioClip[] _footstepClips;
        [SerializeField] private float _stepDistance = 1.35f;
        [SerializeField] private float _footstepVolume = 0.6f;

        public float MoveSpeed => moveSpeed;

        private CharacterController _controller;
        private AudioSource _footstepSource;
        private Vector3 _velocity;
        private float _distanceSinceStep;
        private int _nextClipIndex;

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _footstepSource = GetComponent<AudioSource>();
            _footstepSource.playOnAwake = false;
            _footstepSource.loop = false;
            _footstepSource.spatialBlend = 0f;
        }

        private void OnEnable() => moveAction.action.Enable();
        private void OnDisable() => moveAction.action.Disable();

        private void Update()
        {
            Vector2 input = moveAction.action.ReadValue<Vector2>();
            Vector3 move = transform.right * input.x + transform.forward * input.y;
            Vector3 horizontalMove = move.normalized * moveSpeed * Time.deltaTime;
            _controller.Move(horizontalMove);

            UpdateFootsteps(horizontalMove.magnitude);

            if (_controller.isGrounded && _velocity.y < 0f)
                _velocity.y = 0f;

            _velocity.y += gravity * Time.deltaTime;
            _controller.Move(_velocity * Time.deltaTime);
        }

        private void UpdateFootsteps(float distanceMoved)
        {
            if (!IsGrounded() || distanceMoved <= 0f || _footstepClips == null || _footstepClips.Length == 0)
            {
                if (!IsGrounded())
                    _distanceSinceStep = 0f;
                return;
            }

            _distanceSinceStep += distanceMoved;
            while (_distanceSinceStep >= _stepDistance)
            {
                _distanceSinceStep -= _stepDistance;
                PlayFootstep();
            }
        }

        private bool IsGrounded()
        {
            if (_controller.isGrounded)
                return true;

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
