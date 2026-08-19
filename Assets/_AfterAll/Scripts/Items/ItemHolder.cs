using AfterAll.Environment;
using AfterAll.Items.Flashlight;
using AfterAll.Inventories;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

namespace AfterAll.Items
{
    /// <summary>
    /// Spawns the selected item's held prefab under the hand anchor (Main Camera/Hand).
    /// Also handles putting the currently-selected hotbar item down (same Drop action as
    /// BulkyCarrier — mutually exclusive states, hands can't be full of both at once): removes it
    /// from the inventory slot and instantiates its WorldPickupPrefab at its feet, laid out the way
    /// authored floor loot is. This used to be a forward throw; throwing hard is BulkyCarrier's job
    /// on Attack, and having both do it left no way to simply set something down.
    /// </summary>
    public sealed class ItemHolder : MonoBehaviour
    {
        [SerializeField] private Inventory _inventory;
        [SerializeField] private Transform _handAnchor;

        [Header("Drop (G)")]
        [Tooltip("Player/Drop. This is a place-it-down, not a throw — BulkyCarrier keeps the hard throw on Attack.")]
        [FormerlySerializedAs("_throwAction")]
        [SerializeField] private InputActionReference _dropAction;
        [Tooltip("How far in front of the eyes the item appears. Short, so it lands at your feet rather than being lobbed.")]
        [SerializeField] private float _dropForwardOffset = 0.45f;
        [Tooltip("How far below eye level it appears. Roughly waist height, so it has a short fall.")]
        [SerializeField] private float _dropDownOffset = 0.55f;
        [Tooltip("Gentle push so it clears the player's own collider instead of resting against their legs.")]
        [SerializeField] private float _dropNudgeSpeed = 0.6f;
        [Tooltip("Noise a set-down makes. Much quieter than a throw — the hunter should not hear you tidying up.")]
        [SerializeField] private float _dropNoiseRadius = 4f;

        /// <summary>Matches RoomLootPlacer's authored-loot lean so dropped and spawned items read alike.</summary>
        private const float DropTiltDegrees = 12f;

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
            if (_dropAction != null)
                _dropAction.action.Enable();

            if (_inventory == null)
                return;

            _inventory.OnInventoryChanged += Refresh;
            _inventory.OnSelectionChanged += Refresh;
            Refresh();
        }

        private void OnDisable()
        {
            if (_dropAction != null)
                _dropAction.action.Disable();

            if (_inventory != null)
            {
                _inventory.OnInventoryChanged -= Refresh;
                _inventory.OnSelectionChanged -= Refresh;
            }

            ClearHeld();
        }

        private void Update()
        {
            if (_dropAction != null && _dropAction.action.WasPressedThisFrame())
                TryDropSelected();
        }

        /// <summary>
        /// Sets the selected hotbar item down rather than throwing it. It appears just in front of
        /// and below the eyes already turned onto the face it would naturally rest on, with a small
        /// lean and a random heading, then falls the short remaining distance under gravity — the
        /// same treatment authored floor loot gets, so a dropped book reads identically to one that
        /// was always lying there. No torque: spin is what made this look like a throw.
        ///
        /// The hard forward throw deliberately stays on BulkyCarrier/Attack; this is the quiet half.
        /// </summary>
        private void TryDropSelected()
        {
            if (_inventory == null)
                return;

            ItemDefinition item = _inventory.SelectedItem;
            if (item == null || item.WorldPickupPrefab == null)
                return;

            if (!_inventory.TryConsumeSelected())
                return;

            Transform eye = _camera != null ? _camera.transform : transform;
            Vector3 forward = Vector3.ProjectOnPlane(eye.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.01f)
                forward = transform.forward;

            Vector3 spawnPos = eye.position + forward * _dropForwardOffset + Vector3.down * _dropDownOffset;

            Quaternion rest = Quaternion.AngleAxis(Random.Range(0f, 360f), Vector3.up)
                              * Quaternion.AngleAxis(Random.Range(0f, DropTiltDegrees), Vector3.forward)
                              * RoomLootPlacer.ResolveRestRotation(item.WorldPickupPrefab);

            GameObject spawned = Instantiate(item.WorldPickupPrefab, spawnPos, rest);
            Entities.NoiseEvents.Report(spawnPos, _dropNoiseRadius);

            // A flashlight keeps shining where it lands if it was on when it left the hand, with the
            // same beam it had in hand. No-ops for anything without a Light, so this costs a lookup
            // and no branching on item type.
            WorldFlashlight.ApplyTo(spawned,
                                    FlashlightController.IsOnFor(item),
                                    FlashlightController.SettingsFor(item));

            if (spawned.TryGetComponent(out Rigidbody rb))
                rb.linearVelocity = forward * _dropNudgeSpeed;
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
