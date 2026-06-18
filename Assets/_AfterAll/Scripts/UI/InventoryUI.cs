using UnityEngine;
using UnityEngine.UI;
using AfterAll.Inventories;

namespace AfterAll.UI
{
    public class InventoryUI : MonoBehaviour
    {
        [SerializeField] private Inventory _inventory;
        [SerializeField] private InventorySlotUI[] _slots;

        private void Awake()
        {
            if (_inventory == null)
                _inventory = FindFirstObjectByType<Inventory>();
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
            for (int i = 0; i < _slots.Length; i++)
                _slots[i].Refresh(_inventory.GetSlot(i), i == _inventory.SelectedSlot);
        }
    }
}
