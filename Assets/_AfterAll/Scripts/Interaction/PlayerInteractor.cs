using AfterAll.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AfterAll.Interaction
{
    public class PlayerInteractor : MonoBehaviour
    {
        [SerializeField] private float interactRange = 2.5f;
        [SerializeField] private LayerMask interactableMask = ~0;
        [SerializeField] private InputActionReference interactAction;

        public string CurrentPrompt { get; private set; } = string.Empty;
        public bool HasInteractableTarget { get; private set; }

        private Camera _camera;
        private PlayerLook _look;
        private IInteractable _currentInteractable;

        private void Awake()
        {
            _camera = GetComponentInChildren<Camera>();
            if (_camera == null)
                Debug.LogWarning("[AfterAll] PlayerInteractor needs a Camera on a child object.");

            _look = GetComponent<PlayerLook>();
        }

        /// <summary>Pitch+yaw only, no strafe-roll/camera-bob sway — see PlayerLook.Pitch. Falls
        /// back to the raw camera forward if PlayerLook isn't present (e.g. isolated test rigs).</summary>
        private Vector3 AimDirection() =>
            _look != null
                ? (transform.rotation * Quaternion.AngleAxis(_look.Pitch, Vector3.right)) * Vector3.forward
                : _camera.transform.forward;

        private void OnEnable()  => interactAction.action.Enable();
        private void OnDisable() => interactAction.action.Disable();

        private void Update()
        {
            CurrentPrompt = string.Empty;
            HasInteractableTarget = false;
            _currentInteractable = null;

            if (_camera == null)
                return;

            Ray ray = new Ray(_camera.transform.position, AimDirection());
            if (Physics.Raycast(
                ray,
                out RaycastHit hit,
                interactRange,
                interactableMask,
                QueryTriggerInteraction.Collide))
            {
                // Collider is on the door model; Door script sits on the root.
                IInteractable interactable = hit.collider.GetComponentInParent<IInteractable>();
                if (interactable != null)
                {
                    HasInteractableTarget = true;
                    CurrentPrompt = interactable.Prompt;
                    _currentInteractable = interactable;

                    if (interactAction.action.WasPressedThisFrame())
                        interactable.Interact();
                }
            }
        }

        /// <summary>Called by mobile tap-to-interact in the look zone.</summary>
        public void TryInteract()
        {
            if (_currentInteractable != null)
                _currentInteractable.Interact();
        }
    }
}
