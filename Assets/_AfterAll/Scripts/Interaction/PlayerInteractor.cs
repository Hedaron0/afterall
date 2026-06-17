using UnityEngine;

namespace AfterAll.Interaction
{
    public class PlayerInteractor : MonoBehaviour
    {
        [SerializeField] private float interactRange = 2.5f;
        [SerializeField] private LayerMask interactableMask = ~0;

        private Camera _camera;

        private void Awake()
        {
            _camera = GetComponentInChildren<Camera>();
            if (_camera == null)
                Debug.LogWarning("[AfterAll] PlayerInteractor needs a Camera on a child object.");
        }

        private void Update()
        {
            if (!Input.GetKeyDown(KeyCode.E))
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
