using UnityEngine;
using AfterAll.Inventories;
using AfterAll.UI;

namespace AfterAll.Items
{
    [RequireComponent(typeof(Collider))]
    public class KeyPickup : MonoBehaviour, AfterAll.Interaction.IInteractable
    {
        [SerializeField] private string _promptText = "Pick up Key";
        [SerializeField] private string _fullPromptText = "Inventory full";
        [SerializeField] private AudioClip _pickupClip;
        [SerializeField] private float _pickupVolume = 0.55f;

        private Inventory _inventory;

        public string Prompt
        {
            get
            {
                if (_inventory != null && !_inventory.TryCanAdd())
                    return _fullPromptText;
                return _promptText;
            }
        }

        private void Awake()
        {
            _inventory = FindAnyObjectByType<Inventory>();
        }

        public void Interact()
        {
            if (_inventory == null)
                return;

            if (_inventory.TryAddItem(ItemType.Key))
            {
                GameFeedbackUI.Show("Key picked up.");
                if (_pickupClip != null)
                    AudioSource.PlayClipAtPoint(_pickupClip, transform.position, _pickupVolume);
                gameObject.SetActive(false);
            }
            else
            {
                GameFeedbackUI.Show("Inventory full.");
            }
        }
    }
}
