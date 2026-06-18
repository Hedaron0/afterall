using UnityEngine;
using AfterAll.Inventories;
using AfterAll.UI;

namespace AfterAll.Door
{
    public class LockedDoor : MonoBehaviour, AfterAll.Interaction.IInteractable
    {
        [Header("References")]
        [SerializeField] private HingeDoor _hinge;

        [Header("Prompts")]
        [SerializeField] private string _lockedPrompt = "Locked — select key in hand";
        [SerializeField] private string _useKeyPrompt = "Use key";
        [SerializeField] private string _openPrompt = "Open door";
        [SerializeField] private string _closePrompt = "Close door";

        private bool _isUnlocked;
        private Inventory _inventory;

        public string Prompt
        {
            get
            {
                if (!_isUnlocked)
                {
                    if (_inventory != null && _inventory.SelectedHas(ItemType.Key))
                        return _useKeyPrompt;
                    return _lockedPrompt;
                }

                return _hinge.IsOpen ? _closePrompt : _openPrompt;
            }
        }

        private void Awake()
        {
            _inventory = FindFirstObjectByType<Inventory>();
            if (_hinge == null)
                _hinge = GetComponent<HingeDoor>();
        }

        public void Interact()
        {
            if (!_isUnlocked)
            {
                if (_inventory == null || !_inventory.SelectedHas(ItemType.Key))
                {
                    GameFeedbackUI.Show("Need a key in your selected slot.");
                    return;
                }

                if (!_inventory.TryConsumeSelectedIf(ItemType.Key))
                    return;

                _isUnlocked = true;
                GameFeedbackUI.Show("Door unlocked — press E to open.");
                return;
            }

            bool willOpen = !_hinge.IsOpen;
            _hinge.Toggle();
            GameFeedbackUI.Show(willOpen ? "Door opened." : "Door closed.");
        }
    }
}
