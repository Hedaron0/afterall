using AfterAll.Inventories;
using UnityEngine;

namespace AfterAll.Audio
{
    [RequireComponent(typeof(Inventory))]
    public class InventoryAudio : MonoBehaviour
    {
        [SerializeField] private float _equipVolume = 0.35f;

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
            var clip = _inventory.SelectedItem?.EquipSound;
            if (clip == null)
                return;

            AudioSource.PlayClipAtPoint(clip, transform.position, _equipVolume);
        }
    }
}
