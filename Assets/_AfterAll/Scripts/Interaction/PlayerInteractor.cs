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
        private IInteractable _currentInteractable;

        private void Awake()
        {
            _camera = GetComponentInChildren<Camera>();
            if (_camera == null)
                Debug.LogWarning("[AfterAll] PlayerInteractor needs a Camera on a child object.");
        }

        private void OnEnable()  => interactAction.action.Enable();
        private void OnDisable() => interactAction.action.Disable();

        private void Update()
        {
            CurrentPrompt = string.Empty;
            HasInteractableTarget = false;
            _currentInteractable = null;

            if (_camera == null)
                return;

            Ray ray = new Ray(_camera.transform.position, _camera.transform.forward);
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
