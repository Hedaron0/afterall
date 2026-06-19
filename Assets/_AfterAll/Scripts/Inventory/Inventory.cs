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

        [SerializeField] private InputActionAsset _inputActions;

        private readonly ItemType[] _slots = new ItemType[SlotCount];
        private readonly Action<InputAction.CallbackContext>[] _slotCallbacks =
            new Action<InputAction.CallbackContext>[SlotCount];

        private InputAction[] _slotActions;

        public int SelectedSlot { get; private set; }

        public ItemType SelectedItem => _slots[SelectedSlot];

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
