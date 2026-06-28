using AfterAll.Inventories;
using AfterAll.Items;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AfterAll.UI
{
    [RequireComponent(typeof(Image))]
    public class InventorySlotUI : MonoBehaviour, IPointerClickHandler
    {
        [SerializeField] private Inventory _inventory;
        [SerializeField] private int _slotIndex;
        [SerializeField] private Image _fillImage;
        [SerializeField] private Image _highlightImage;
        [SerializeField] private Image _iconImage;

        [Header("Colors")]
        [SerializeField] private Color _emptyColor = new Color(0.15f, 0.15f, 0.15f, 0.75f);
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

        public void Refresh(ItemDefinition item, bool selected)
        {
            _fillImage.color = item != null ? item.SlotColor : _emptyColor;

            if (_highlightImage != null)
                _highlightImage.enabled = selected;

            if (_iconImage == null)
                return;

            if (item != null && item.Icon != null)
            {
                _iconImage.enabled = true;
                _iconImage.sprite = item.Icon;
            }
            else
            {
                _iconImage.enabled = false;
                _iconImage.sprite = null;
            }
        }
    }
}
