using System.Collections;
using UnityEngine;
using TMPro;

namespace AfterAll.UI
{
    /// <summary>
    /// Short on-screen feedback ("Key picked up", "Door opened").
    /// Place on a persistent HUD object — not the same object it toggles.
    /// </summary>
    public class GameFeedbackUI : MonoBehaviour
    {
        private static GameFeedbackUI _instance;

        [SerializeField] private TextMeshProUGUI _feedbackText;
        [SerializeField] private float _displayDuration = 2f;

        private Coroutine _hideRoutine;

        private void Awake()
        {
            _instance = this;
            if (_feedbackText != null)
                _feedbackText.enabled = false;
        }

        public static void Show(string message)
        {
            if (_instance == null || _instance._feedbackText == null)
            {
                Debug.Log($"[AfterAll] {message}");
                return;
            }

            _instance.Display(message);
        }

        private void Display(string message)
        {
            _feedbackText.text = message;
            _feedbackText.enabled = true;

            if (_hideRoutine != null)
                StopCoroutine(_hideRoutine);

            _hideRoutine = StartCoroutine(HideAfterDelay());
        }

        private IEnumerator HideAfterDelay()
        {
            yield return new WaitForSeconds(_displayDuration);
            _feedbackText.enabled = false;
            _hideRoutine = null;
        }
    }
}
