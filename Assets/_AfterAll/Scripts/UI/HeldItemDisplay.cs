using UnityEngine;
using AfterAll.Inventories;

namespace AfterAll.UI
{
    /// <summary>
    /// Shows a simple held-item mesh in front of the camera for the selected slot.
    /// </summary>
    public class HeldItemDisplay : MonoBehaviour
    {
        [SerializeField] private Inventory _inventory;
        [SerializeField] private Transform _holdAnchor;
        [SerializeField] private Vector3 _keyLocalPosition = new Vector3(0.25f, -0.2f, 0.4f);
        [SerializeField] private Vector3 _keyLocalScale = new Vector3(0.08f, 0.08f, 0.25f);

        private GameObject _heldVisual;

        private void Awake()
        {
            if (_inventory == null)
                _inventory = FindAnyObjectByType<Inventory>();

            if (_holdAnchor == null)
                _holdAnchor = transform;
        }

        private void OnEnable()
        {
            _inventory.OnInventoryChanged += Refresh;
            _inventory.OnSelectionChanged += Refresh;
            Refresh();
        }

        private void OnDisable()
        {
            _inventory.OnInventoryChanged -= Refresh;
            _inventory.OnSelectionChanged -= Refresh;
        }

        private void Refresh()
        {
            if (_heldVisual != null)
            {
                Destroy(_heldVisual);
                _heldVisual = null;
            }

            if (_inventory.SelectedItem != ItemType.Key)
                return;

            _heldVisual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _heldVisual.name = "HeldKey";
            Destroy(_heldVisual.GetComponent<Collider>());

            _heldVisual.transform.SetParent(_holdAnchor, false);
            _heldVisual.transform.localPosition = _keyLocalPosition;
            _heldVisual.transform.localRotation = Quaternion.Euler(0f, 45f, 0f);
            _heldVisual.transform.localScale = _keyLocalScale;

            var renderer = _heldVisual.GetComponent<Renderer>();
            if (renderer != null)
                renderer.material.color = new Color(1f, 0.85f, 0.2f);
        }
    }
}
