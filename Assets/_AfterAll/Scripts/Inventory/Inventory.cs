using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AfterAll.Inventories
{
    public enum ItemType { None, Key }

    public class Inventory : MonoBehaviour
    {
        public const int SlotCount = 3;

        public event Action OnInventoryChanged;
        public event Action OnSelectionChanged;

        [SerializeField] private InputActionReference[] _slotSelectActions = new InputActionReference[SlotCount];

        private readonly ItemType[] _slots = new ItemType[SlotCount];
        private readonly System.Action<InputAction.CallbackContext>[] _slotCallbacks =
            new System.Action<InputAction.CallbackContext>[SlotCount];

        public int SelectedSlot { get; private set; }

        public ItemType SelectedItem => _slots[SelectedSlot];

        private void OnEnable()
        {
            if (_slotSelectActions == null)
                return;

            for (int i = 0; i < _slotSelectActions.Length; i++)
            {
                if (_slotSelectActions[i] == null)
                    continue;

                int slotIndex = i;
                _slotCallbacks[i] = _ => SetSelectedSlot(slotIndex);
                _slotSelectActions[i].action.performed += _slotCallbacks[i];
                _slotSelectActions[i].action.Enable();
            }
        }

        private void OnDisable()
        {
            if (_slotSelectActions == null)
                return;

            for (int i = 0; i < _slotSelectActions.Length; i++)
            {
                if (_slotSelectActions[i] == null)
                    continue;

                if (_slotCallbacks[i] != null)
                    _slotSelectActions[i].action.performed -= _slotCallbacks[i];

                _slotSelectActions[i].action.Disable();
            }
        }

        public void SetSelectedSlot(int index)
        {
            if (index < 0 || index >= SlotCount || index == SelectedSlot)
                return;

            SelectedSlot = index;
            OnSelectionChanged?.Invoke();
        }

        public bool TryCanAdd()
        {
            foreach (ItemType slot in _slots)
                if (slot == ItemType.None) return true;
            return false;
        }

        public bool TryAddItem(ItemType item)
        {
            for (int i = 0; i < SlotCount; i++)
            {
                if (_slots[i] != ItemType.None)
                    continue;

                _slots[i] = item;
                OnInventoryChanged?.Invoke();
                return true;
            }
            return false;
        }

        public bool HasItem(ItemType item)
        {
            foreach (ItemType slot in _slots)
                if (slot == item) return true;
            return false;
        }

        public bool SelectedHas(ItemType item) => _slots[SelectedSlot] == item;

        public bool TryConsumeSelected()
        {
            if (_slots[SelectedSlot] == ItemType.None)
                return false;

            _slots[SelectedSlot] = ItemType.None;
            OnInventoryChanged?.Invoke();
            return true;
        }

        public bool TryConsumeSelectedIf(ItemType item)
        {
            if (_slots[SelectedSlot] != item)
                return false;

            return TryConsumeSelected();
        }

        public ItemType GetSlot(int index) => _slots[index];
    }

}
