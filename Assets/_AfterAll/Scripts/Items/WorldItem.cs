using AfterAll.Inventories;
using AfterAll.Interaction;
using AfterAll.Items.Loot;
using AfterAll.UI;
using UnityEngine;

namespace AfterAll.Items
{
    /// <summary>
    /// Generic world pickup. Routes items to hotbar or any IItemReceiver on the player.
    /// Bulky Loot items are special-cased to BulkyCarrier's physical grab instead — they stay
    /// live in the world (re-parented/spring-held, not deactivated) rather than being consumed
    /// into abstract inventory data. See AfterAll.Items.Loot.BulkyCarrier.
    /// </summary>
    public sealed class WorldItem : MonoBehaviour, IInteractable
    {
        [SerializeField] private ItemDefinition _item;
        [SerializeField] private Inventory _inventory;
        [SerializeField] private string _fullPromptText = "Inventory full";
        [SerializeField] private bool _selectOnPickup = true;
        [SerializeField] private int _amount = 1;
        [SerializeField] private float _pickupVolume = 0.55f;

        private IItemReceiver[] _receivers;
        private BulkyCarrier _bulkyCarrier;

        public ItemDefinition Item => _item;

        public string Prompt
        {
            get
            {
                if (_item == null)
                    return string.Empty;

                if (IsBulkyLoot() && _bulkyCarrier != null)
                    return _bulkyCarrier.IsCarrying ? "Hands full" : _item.PickupPrompt;

                if (!CanPickUp())
                    return _fullPromptText;

                return _item.PickupPrompt;
            }
        }

        private void Awake()
        {
            if (_inventory == null)
                _inventory = FindAnyObjectByType<Inventory>();

            _bulkyCarrier = FindAnyObjectByType<BulkyCarrier>();

            CacheReceivers();
        }

        public void Interact()
        {
            if (_item == null)
                return;

            if (IsBulkyLoot())
            {
                // Grabbed object stays live in the world (BulkyCarrier holds it directly) —
                // no SetActive(false)/pickup sound-and-vanish like the abstract pickup path below.
                _bulkyCarrier?.TryGrab(this);
                return;
            }

            if (_inventory == null)
                return;

            if (!TryPickUp())
            {
                GameFeedbackUI.Show("Inventory full.");
                return;
            }

            GameFeedbackUI.Show($"{_item.DisplayName} picked up.");

            var clip = _item.PickupSound;
            if (clip != null)
                AudioSource.PlayClipAtPoint(clip, transform.position, _pickupVolume);

            gameObject.SetActive(false);
        }

        private bool IsBulkyLoot() =>
            _item != null &&
            _item.Category == ItemCategory.Loot &&
            EchoDefinition.TryGetFor(_item, out EchoDefinition def) &&
            def.SizeClass == EchoSizeClass.Bulky;

        private void CacheReceivers()
        {
            if (_inventory == null)
            {
                _receivers = System.Array.Empty<IItemReceiver>();
                return;
            }

            _receivers = _inventory.GetComponents<IItemReceiver>();
        }

        private bool CanPickUp()
        {
            if (_receivers == null || _receivers.Length == 0)
                CacheReceivers();

            foreach (var receiver in _receivers)
            {
                if (receiver.CanReceive(_item))
                    return true;
            }

            return false;
        }

        private bool TryPickUp()
        {
            if (_receivers == null || _receivers.Length == 0)
                CacheReceivers();

            foreach (var receiver in _receivers)
            {
                if (!receiver.CanReceive(_item))
                    continue;

                if (!receiver.TryReceive(_item, _amount))
                    continue;

                if (_selectOnPickup && _item.UsesHotbar)
                    _inventory.SelectSlotContaining(_item);

                return true;
            }

            return false;
        }
    }
}
