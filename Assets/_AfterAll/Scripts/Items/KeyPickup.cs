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
            _inventory = FindFirstObjectByType<Inventory>();
        }

        public void Interact()
        {
            if (_inventory == null)
                return;

            if (_inventory.TryAddItem(ItemType.Key))
            {
                GameFeedbackUI.Show("Key picked up.");
                gameObject.SetActive(false);
            }
            else
            {
                GameFeedbackUI.Show("Inventory full.");
            }
        }
    }
}
