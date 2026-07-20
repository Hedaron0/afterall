using AfterAll.Inventories;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AfterAll.Items
{
    /// <summary>
    /// Spawns the selected item's held prefab under the hand anchor (Main Camera/Hand).
    /// Also handles throwing the currently-selected hotbar item away (same Drop action as
    /// BulkyCarrier — mutually exclusive states, hands can't be full of both at once): removes it
    /// from the inventory slot and instantiates its WorldPickupPrefab with a simple forward
    /// impulse, same feel as BulkyCarrier.TryThrow.
    /// </summary>
    public sealed class ItemHolder : MonoBehaviour
    {
        [SerializeField] private Inventory _inventory;
        [SerializeField] private Transform _handAnchor;

        [Header("Throw")]
        [SerializeField] private InputActionReference _throwAction;
        [SerializeField] private float _throwSpawnDistance = 0.8f;
        [SerializeField] private float _throwImpulse = 4f;
        [SerializeField] private float _throwSpinImpulse = 0.05f;

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
            if (_throwAction != null)
                _throwAction.action.Enable();

            if (_inventory == null)
                return;

            _inventory.OnInventoryChanged += Refresh;
            _inventory.OnSelectionChanged += Refresh;
            Refresh();
        }

        private void OnDisable()
        {
            if (_throwAction != null)
                _throwAction.action.Disable();

            if (_inventory != null)
            {
                _inventory.OnInventoryChanged -= Refresh;
                _inventory.OnSelectionChanged -= Refresh;
            }

            ClearHeld();
        }

        private void Update()
        {
            if (_throwAction != null && _throwAction.action.WasPressedThisFrame())
                TryThrowSelected();
        }

        private void TryThrowSelected()
        {
            if (_inventory == null)
                return;

            ItemDefinition item = _inventory.SelectedItem;
            if (item == null || item.WorldPickupPrefab == null)
                return;

            if (!_inventory.TryConsumeSelected())
                return;

            Vector3 forward = _camera != null ? _camera.transform.forward : transform.forward;
            Vector3 origin = _camera != null ? _camera.transform.position : transform.position;
            Vector3 spawnPos = origin + forward * _throwSpawnDistance;

            GameObject spawned = Instantiate(item.WorldPickupPrefab, spawnPos, Quaternion.identity);
            Entities.NoiseEvents.Report(spawnPos, 10f);

            if (spawned.TryGetComponent(out Rigidbody rb))
            {
                rb.AddForce(forward * _throwImpulse, ForceMode.Impulse);

                Vector3 randomSpinAxis = new Vector3(
                    Random.Range(-1f, 1f),
                    Random.Range(-1f, 1f),
                    Random.Range(-1f, 1f)).normalized;
                rb.AddTorque(randomSpinAxis * _throwSpinImpulse, ForceMode.Impulse);
            }
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
