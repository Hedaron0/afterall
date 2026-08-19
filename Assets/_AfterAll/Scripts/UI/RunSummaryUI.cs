using System.Text;
using AfterAll.Run;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AfterAll.UI
{
    /// <summary>
    /// The screen the run ends on. Subscribes to RunDirector.RunConcluded, which fires for both
    /// death and extract, and holds the player in the cabin until the continue button is pressed.
    ///
    /// Put this component on a persistent HUD object and point _panelRoot at the panel it toggles —
    /// never at its own GameObject, or it would switch itself off and never hear the next run end
    /// (same pattern as GameFeedbackUI).
    ///
    /// Deliberately does NOT touch Time.timeScale: the next floor generates in the background while
    /// this panel is up, and freezing time would stall it mid-build.
    /// </summary>
    public class RunSummaryUI : MonoBehaviour
    {
        [SerializeField] private RunDirector _runDirector;

        [Header("Refs")]
        [Tooltip("The panel this component shows and hides. Must be a different GameObject than this one.")]
        [SerializeField] private GameObject _panelRoot;
        [SerializeField] private TMP_Text _titleText;
        [SerializeField] private TMP_Text _bodyText;
        [SerializeField] private TMP_Text _goalText;
        [SerializeField] private Button _continueButton;
        [SerializeField] private TMP_Text _continueLabel;

        [Header("Titles")]
        [SerializeField] private string _diedTitle = "YOU DIED";
        [SerializeField] private string _extractedTitle = "EXTRACTED";
        [SerializeField] private string _completedTitle = "RUN COMPLETE";

        [Header("Title Colors")]
        [SerializeField] private Color _diedColor = new Color(0.78f, 0.16f, 0.16f);
        [SerializeField] private Color _extractedColor = new Color(0.92f, 0.92f, 0.90f);
        [SerializeField] private Color _completedColor = new Color(0.95f, 0.78f, 0.30f);

        [Header("Body")]
        [SerializeField] private string _depthLineFormat = "Depth reached: {0}";
        [SerializeField] private string _bankedLineFormat = "Banked this run: {0}";
        [SerializeField] private string _lostLine = "Everything you carried is gone.";
        [SerializeField] private string _totalLineFormat = "Total banked: {0}";
        [SerializeField] private string _goalFormat = "Goal: extract from depth {0} with {1}";

        [Header("Continue")]
        [SerializeField] private string _continueLabelAfterDeath = "TRY AGAIN";
        [SerializeField] private string _continueLabelAfterExtract = "NEXT RUN";

        private readonly StringBuilder _body = new();

        private void Awake()
        {
            if (_runDirector == null)
                _runDirector = FindAnyObjectByType<RunDirector>();

            if (_panelRoot != null)
                _panelRoot.SetActive(false);
        }

        private void OnEnable()
        {
            if (_runDirector != null)
                _runDirector.RunConcluded += Show;

            if (_continueButton != null)
                _continueButton.onClick.AddListener(Continue);
        }

        private void OnDisable()
        {
            if (_runDirector != null)
                _runDirector.RunConcluded -= Show;

            if (_continueButton != null)
                _continueButton.onClick.RemoveListener(Continue);
        }

        private void Show(RunSummary summary)
        {
            if (_titleText != null)
            {
                _titleText.text = TitleFor(summary.Outcome);
                _titleText.color = TitleColorFor(summary.Outcome);
            }

            if (_bodyText != null)
                _bodyText.text = BuildBody(summary);

            if (_goalText != null)
            {
                // Once the goal is met there is nothing left to chase on this screen.
                bool showGoal = summary.Outcome != RunOutcome.Completed;
                _goalText.gameObject.SetActive(showGoal);
                if (showGoal)
                    _goalText.text = string.Format(_goalFormat, summary.TargetDepth, summary.TargetBankedEchoes);
            }

            if (_continueLabel != null)
            {
                _continueLabel.text = summary.Outcome == RunOutcome.Died
                    ? _continueLabelAfterDeath
                    : _continueLabelAfterExtract;
            }

            if (_panelRoot != null)
                _panelRoot.SetActive(true);

            // Mouse is already free here: RunDirector switches PlayerLook off before firing this,
            // which stops it re-locking the cursor every frame.
            if (_continueButton != null)
                _continueButton.Select();
        }

        private string TitleFor(RunOutcome outcome) => outcome switch
        {
            RunOutcome.Died => _diedTitle,
            RunOutcome.Completed => _completedTitle,
            _ => _extractedTitle
        };

        private Color TitleColorFor(RunOutcome outcome) => outcome switch
        {
            RunOutcome.Died => _diedColor,
            RunOutcome.Completed => _completedColor,
            _ => _extractedColor
        };

        private string BuildBody(RunSummary summary)
        {
            _body.Clear();
            _body.AppendFormat(_depthLineFormat, summary.DepthReached);

            if (summary.Outcome == RunOutcome.Died)
                _body.Append('\n').Append(_lostLine);
            else
                _body.Append('\n').AppendFormat(_bankedLineFormat, summary.BankedThisRun);

            _body.Append('\n').AppendFormat(_totalLineFormat, summary.TotalBanked);
            return _body.ToString();
        }

        private void Continue()
        {
            if (_panelRoot != null)
                _panelRoot.SetActive(false);

            if (_runDirector != null)
                _runDirector.AcknowledgeRunSummary();
        }
    }
}
