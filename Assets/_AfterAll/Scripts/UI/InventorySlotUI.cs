using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using AfterAll.Inventories;

namespace AfterAll.UI
{
    [RequireComponent(typeof(Image))]
    public class InventorySlotUI : MonoBehaviour, IPointerClickHandler, IPointerDownHandler
    {
        [SerializeField] private Inventory _inventory;
        [SerializeField] private int _slotIndex;
        [SerializeField] private Image _fillImage;
        [SerializeField] private Image _highlightImage;

        [Header("Colors")]
        [SerializeField] private Color _emptyColor = new Color(0.15f, 0.15f, 0.15f, 0.75f);
        [SerializeField] private Color _keyColor = new Color(1f, 0.85f, 0.2f, 1f);
        [SerializeField] private Color _selectedTint = new Color(1f, 1f, 1f, 0.35f);

        private void Awake()
        {
            if (_inventory == null)
                _inventory = FindAnyObjectByType<Inventory>();

            if (_fillImage == null)
                _fillImage = GetComponent<Image>();
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            _inventory.SetSelectedSlot(_slotIndex);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            _inventory.SetSelectedSlot(_slotIndex);
        }

        public void Refresh(ItemType item, bool selected)
        {
            _fillImage.color = item switch
            {
                ItemType.Key => _keyColor,
                _            => _emptyColor,
            };

            if (_highlightImage != null)
                _highlightImage.enabled = selected;
        }
    }
}
