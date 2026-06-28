using AfterAll.Inventories;
using AfterAll.Interaction;
using AfterAll.UI;
using UnityEngine;

namespace AfterAll.Items
{
    /// <summary>
    /// Generic world pickup. Routes items to hotbar or any IItemReceiver on the player.
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

        public string Prompt
        {
            get
            {
                if (_item == null)
                    return string.Empty;

                if (!CanPickUp())
                    return _fullPromptText;

                return _item.PickupPrompt;
            }
        }

        private void Awake()
        {
            if (_inventory == null)
                _inventory = FindAnyObjectByType<Inventory>();

            CacheReceivers();
        }

        public void Interact()
        {
            if (_item == null || _inventory == null)
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
