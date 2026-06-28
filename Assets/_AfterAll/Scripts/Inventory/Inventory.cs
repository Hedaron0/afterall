using System;
using AfterAll.Items;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AfterAll.Inventories
{
    /// <summary>
    /// 3-slot hotbar. Implements IItemReceiver for Hotbar + KeyItem pickups only.
    /// Ammo / consumables will use separate IItemReceiver components later.
    /// </summary>
    public class Inventory : MonoBehaviour, IItemReceiver
    {
        public const int SlotCount = 3;

        public event Action OnInventoryChanged;
        public event Action OnSelectionChanged;

        [SerializeField] private InputActionAsset _inputActions;

        private readonly ItemDefinition[] _slots = new ItemDefinition[SlotCount];
        private readonly Action<InputAction.CallbackContext>[] _slotCallbacks =
            new Action<InputAction.CallbackContext>[SlotCount];

        private InputAction[] _slotActions;

        public int SelectedSlot { get; private set; }

        public ItemDefinition SelectedItem => _slots[SelectedSlot];

        public bool HasFreeSlot
        {
            get
            {
                foreach (ItemDefinition slot in _slots)
                {
                    if (slot == null)
                        return true;
                }

                return false;
            }
        }

        private void Awake()
        {
            ResolveInputActions();
        }

        private void OnEnable()
        {
            ResolveInputActions();
            if (_inputActions == null)
                return;

            var playerMap = _inputActions.FindActionMap("Player", true);
            _slotActions = new[]
            {
                playerMap.FindAction("Slot1", true),
                playerMap.FindAction("Slot2", true),
                playerMap.FindAction("Slot3", true),
            };

            for (int i = 0; i < _slotActions.Length; i++)
            {
                int slotIndex = i;
                _slotCallbacks[i] = _ => SetSelectedSlot(slotIndex);
                _slotActions[i].performed += _slotCallbacks[i];
                _slotActions[i].Enable();
            }
        }

        private void OnDisable()
        {
            if (_slotActions == null)
                return;

            for (int i = 0; i < _slotActions.Length; i++)
            {
                if (_slotCallbacks[i] != null)
                    _slotActions[i].performed -= _slotCallbacks[i];

                _slotActions[i].Disable();
            }
        }

        public void SetSelectedSlot(int index)
        {
            if (index < 0 || index >= SlotCount || index == SelectedSlot)
                return;

            SelectedSlot = index;
            OnSelectionChanged?.Invoke();
        }

        public void SelectSlotContaining(ItemDefinition item)
        {
            if (item == null)
                return;

            for (int i = 0; i < SlotCount; i++)
            {
                if (_slots[i] != item)
                    continue;

                SetSelectedSlot(i);
                return;
            }
        }

        public bool TryAddItem(ItemDefinition item, bool selectAddedSlot = false)
        {
            for (int i = 0; i < SlotCount; i++)
            {
                if (_slots[i] != null)
                    continue;

                _slots[i] = item;
                OnInventoryChanged?.Invoke();

                if (selectAddedSlot)
                    SetSelectedSlot(i);

                return true;
            }

            return false;
        }

        public bool HasItem(ItemDefinition item)
        {
            if (item == null)
                return false;

            foreach (ItemDefinition slot in _slots)
            {
                if (slot == item)
                    return true;
            }

            return false;
        }

        public bool SelectedHas(ItemDefinition item) => _slots[SelectedSlot] == item;

        public bool TryConsumeSelected()
        {
            if (_slots[SelectedSlot] == null)
                return false;

            _slots[SelectedSlot] = null;
            OnInventoryChanged?.Invoke();
            return true;
        }

        public bool TryConsumeSelectedIf(ItemDefinition item)
        {
            if (_slots[SelectedSlot] != item)
                return false;

            return TryConsumeSelected();
        }

        public ItemDefinition GetSlot(int index) => _slots[index];

        bool IItemReceiver.CanReceive(ItemDefinition item) =>
            item != null && item.UsesHotbar && HasFreeSlot;

        bool IItemReceiver.TryReceive(ItemDefinition item, int amount)
        {
            if (amount < 1 || !((IItemReceiver)this).CanReceive(item))
                return false;

            return TryAddItem(item);
        }

        private void ResolveInputActions()
        {
            if (_inputActions != null)
                return;

            foreach (var asset in Resources.FindObjectsOfTypeAll<InputActionAsset>())
            {
                if (asset.name != "InputSystem_Actions")
                    continue;

                _inputActions = asset;
                return;
            }
        }
    }
}
