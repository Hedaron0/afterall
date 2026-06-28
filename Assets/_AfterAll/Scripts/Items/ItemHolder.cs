using AfterAll.Inventories;
using UnityEngine;

namespace AfterAll.Items
{
    /// <summary>
    /// Spawns the selected item's held prefab under the hand anchor (Main Camera/Hand).
    /// </summary>
    public sealed class ItemHolder : MonoBehaviour
    {
        [SerializeField] private Inventory _inventory;
        [SerializeField] private Transform _handAnchor;

        private GameObject _heldInstance;
        private IHeldItemBehaviour[] _heldBehaviours;
        private Camera _camera;

        private void Awake()
        {
            if (_inventory == null)
                _inventory = GetComponent<Inventory>() ?? FindAnyObjectByType<Inventory>();

            if (_handAnchor == null)
                _handAnchor = ResolveHandAnchor();

            _camera = GetComponentInChildren<Camera>();
        }

        private void OnEnable()
        {
            if (_inventory == null)
                return;

            _inventory.OnInventoryChanged += Refresh;
            _inventory.OnSelectionChanged += Refresh;
            Refresh();
        }

        private void OnDisable()
        {
            if (_inventory != null)
            {
                _inventory.OnInventoryChanged -= Refresh;
                _inventory.OnSelectionChanged -= Refresh;
            }

            ClearHeld();
        }

        private Transform ResolveHandAnchor()
        {
            var pivot = transform.Find("CameraPivot");
            if (pivot == null)
                return null;

            var cam = pivot.Find("Main Camera");
            if (cam != null)
            {
                var hand = cam.Find("Hand");
                if (hand != null)
                    return hand;
            }

            var legacyHand = pivot.Find("Hand");
            return legacyHand != null ? legacyHand : pivot;
        }

        private void Refresh()
        {
            ClearHeld();

            if (_inventory == null || _handAnchor == null)
                return;

            ItemDefinition item = _inventory.SelectedItem;
            if (item == null || !item.ShowsInHand)
                return;

            _heldInstance = Instantiate(item.HeldPrefab, _handAnchor, false);
            _heldInstance.name = $"Held_{item.DisplayName}";

            _heldBehaviours = _heldInstance.GetComponentsInChildren<IHeldItemBehaviour>(true);
            if (_camera == null)
                _camera = GetComponentInChildren<Camera>();

            foreach (var behaviour in _heldBehaviours)
                behaviour.OnEquipped(_inventory, _camera, item);
        }

        private void ClearHeld()
        {
            if (_heldBehaviours != null)
            {
                foreach (var behaviour in _heldBehaviours)
                    behaviour.OnUnequipped();

                _heldBehaviours = null;
            }

            if (_heldInstance == null)
                return;

            Destroy(_heldInstance);
            _heldInstance = null;
        }
    }
}
