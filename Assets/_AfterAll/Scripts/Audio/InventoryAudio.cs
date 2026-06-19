using AfterAll.Inventories;
using UnityEngine;

namespace AfterAll.Audio
{
    [RequireComponent(typeof(Inventory))]
    public class InventoryAudio : MonoBehaviour
    {
        [SerializeField] private AudioClip _keyEquipClip;
        [SerializeField] private float _keyEquipVolume = 0.35f;

        private Inventory _inventory;

        private void Awake()
        {
            _inventory = GetComponent<Inventory>();
        }

        private void OnEnable()
        {
            if (_inventory != null)
                _inventory.OnSelectionChanged += OnSelectionChanged;
        }

        private void OnDisable()
        {
            if (_inventory != null)
                _inventory.OnSelectionChanged -= OnSelectionChanged;
        }

        private void OnSelectionChanged()
        {
            if (_inventory.SelectedItem != ItemType.Key)
                return;

            if (_keyEquipClip == null)
                return;

            AudioSource.PlayClipAtPoint(_keyEquipClip, transform.position, _keyEquipVolume);
        }
    }
}
