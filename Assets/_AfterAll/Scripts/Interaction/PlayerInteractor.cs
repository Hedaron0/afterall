using UnityEngine;
using UnityEngine.InputSystem;

namespace AfterAll.Interaction
{
    public class PlayerInteractor : MonoBehaviour
    {
        [SerializeField] private float interactRange = 2.5f;
        [SerializeField] private LayerMask interactableMask = ~0;
        [SerializeField] private InputActionReference interactAction;

        private Camera _camera;

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
            if (!interactAction.action.WasPressedThisFrame())
                return;

            if (_camera == null)
                return;

            Ray ray = new Ray(_camera.transform.position, _camera.transform.forward);
            if (!Physics.Raycast(ray, out RaycastHit hit, interactRange, interactableMask))
                return;

            IInteractable interactable = hit.collider.GetComponent<IInteractable>();
            if (interactable != null)
                interactable.Interact();
        }
    }
}
