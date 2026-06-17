using UnityEngine;

namespace AfterAll.Interaction
{
    public class Interactable : MonoBehaviour, IInteractable
    {
        [SerializeField] private string prompt = "Interact";
        [SerializeField] private string message = "Interacted!";

        public string Prompt => prompt;

        public void Interact()
        {
            Debug.Log($"[AfterAll] {message} ({gameObject.name})");
        }
    }
}
