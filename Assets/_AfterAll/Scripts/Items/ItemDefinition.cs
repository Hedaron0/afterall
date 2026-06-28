using UnityEngine;

namespace AfterAll.Items
{
    [CreateAssetMenu(menuName = "AfterAll/Item Definition", fileName = "NewItem")]
    public sealed class ItemDefinition : ScriptableObject
    {
        [SerializeField] private string _displayName = "Item";
        [SerializeField] private ItemCategory _category = ItemCategory.Hotbar;
        [SerializeField] private Sprite _icon;
        [SerializeField] private GameObject _heldPrefab;
        [SerializeField] private Color _slotColor = new(0.85f, 0.85f, 0.85f, 1f);
        [SerializeField] private AudioClip _equipSound;
        [SerializeField] private AudioClip _pickupSound;
        [SerializeField] private string _pickupPrompt = "Pick up";

        public string DisplayName => _displayName;
        public ItemCategory Category => _category;
        public Sprite Icon => _icon;
        public GameObject HeldPrefab => _heldPrefab;
        public Color SlotColor => _slotColor;
        public AudioClip EquipSound => _equipSound;
        public AudioClip PickupSound => _pickupSound;

        /// <summary>Hotbar + key items occupy a slot. Consumable / ammo use receivers later.</summary>
        public bool UsesHotbar => _category is ItemCategory.Hotbar or ItemCategory.KeyItem;

        /// <summary>Spawn a visual under Hand when this slot is selected.</summary>
        public bool ShowsInHand => UsesHotbar && _heldPrefab != null;

        public string PickupPrompt => string.IsNullOrWhiteSpace(_pickupPrompt)
            ? $"Pick up {_displayName}"
            : _pickupPrompt;
    }
}
