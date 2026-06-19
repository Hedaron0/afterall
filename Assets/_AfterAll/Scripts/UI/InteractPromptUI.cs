using UnityEngine;
using TMPro;
using AfterAll.Interaction;

namespace AfterAll.UI
{
    public class InteractPromptUI : MonoBehaviour
    {
        [SerializeField] private PlayerInteractor _interactor;
        [SerializeField] private CanvasGroup _promptGroup;
        [SerializeField] private TextMeshProUGUI _promptText;

        private void Awake()
        {
            if (_interactor == null)
                _interactor = FindAnyObjectByType<PlayerInteractor>();

            if (_promptGroup != null)
                _promptGroup.alpha = 0f;
        }

        private void Update()
        {
            if (_interactor == null || _promptText == null)
                return;

            string prompt = _interactor.CurrentPrompt;
            bool show = !string.IsNullOrEmpty(prompt);

            if (_promptGroup != null)
            {
                _promptGroup.alpha = show ? 1f : 0f;
                _promptGroup.interactable = show;
                _promptGroup.blocksRaycasts = show;
            }

            if (show)
                _promptText.text = prompt;
        }
    }
}
