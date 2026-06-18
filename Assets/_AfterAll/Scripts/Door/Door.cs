using UnityEngine;
using AfterAll.UI;

namespace AfterAll.Door
{
    /// <summary>
    /// Standard door — toggle open/close, no key required.
    /// </summary>
    public class Door : MonoBehaviour, AfterAll.Interaction.IInteractable
    {
        [SerializeField] private HingeDoor _hinge;
        [SerializeField] private string _openPrompt = "Open door";
        [SerializeField] private string _closePrompt = "Close door";

        public string Prompt => _hinge.IsOpen ? _closePrompt : _openPrompt;

        private void Awake()
        {
            if (_hinge == null)
                _hinge = GetComponent<HingeDoor>();
        }

        public void Interact()
        {
            bool willOpen = !_hinge.IsOpen;
            _hinge.Toggle();
            GameFeedbackUI.Show(willOpen ? "Door opened." : "Door closed.");
        }
    }
}
