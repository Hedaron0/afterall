using AfterAll.Items.Loot;
using AfterAll.Meta;
using AfterAll.Run;
using TMPro;
using UnityEngine;

namespace AfterAll.UI
{
    /// <summary>
    /// Always-on run readout: how deep you are, what you are currently carrying, what you have
    /// banked for good, and what the run is actually asking of you. Without this the player has no
    /// signal that descending is doing anything.
    ///
    /// Every text field is optional — leave one unassigned to drop that line from the HUD.
    /// "Carrying" is pocket value plus the bulky item in hand: both are lost on death and neither
    /// is counted anywhere else on screen (ElevatorValueUI shows what is already SAFE in the cabin,
    /// which is a different number on purpose).
    /// </summary>
    public class RunStatusUI : MonoBehaviour
    {
        [SerializeField] private RunDirector _runDirector;
        [SerializeField] private EchoPocket _pocket;
        [SerializeField] private BulkyCarrier _carrier;

        [Header("Refs")]
        [SerializeField] private TMP_Text _depthText;
        [SerializeField] private TMP_Text _carryText;
        [SerializeField] private TMP_Text _bankedText;
        [SerializeField] private TMP_Text _goalText;

        [Header("Format")]
        [SerializeField] private string _depthFormat = "DEPTH {0}";
        [SerializeField] private string _carryFormat = "Carrying: {0}";
        [SerializeField] private string _bankedFormat = "Banked: {0}";
        [SerializeField] private string _goalFormat = "Goal: depth {0} · {1}";

        // Last values pushed to TMP. Rewriting a TMP_Text rebuilds its mesh, so only touch it when
        // the number actually moved — these tick every frame otherwise.
        private int _shownDepth = -1;
        private int _shownCarry = -1;
        private int _shownBanked = -1;

        private void Awake()
        {
            if (_runDirector == null) _runDirector = FindAnyObjectByType<RunDirector>();
            if (_pocket == null) _pocket = FindAnyObjectByType<EchoPocket>();
            if (_carrier == null) _carrier = FindAnyObjectByType<BulkyCarrier>();
        }

        private void Start()
        {
            if (_goalText != null && _runDirector != null)
                _goalText.text = string.Format(_goalFormat, _runDirector.TargetDepth, _runDirector.TargetBankedEchoes);
        }

        private void Update()
        {
            if (_runDirector == null)
                return;

            if (_depthText != null && _runDirector.Depth != _shownDepth)
            {
                _shownDepth = _runDirector.Depth;
                _depthText.text = string.Format(_depthFormat, _shownDepth);
            }

            if (_carryText != null)
            {
                int carried = (_pocket?.PeekValue() ?? 0) + (_carrier?.PeekValue() ?? 0);
                if (carried != _shownCarry)
                {
                    _shownCarry = carried;
                    _carryText.text = string.Format(_carryFormat, carried);
                }
            }

            if (_bankedText != null && MetaProgress.BankedEchoes != _shownBanked)
            {
                _shownBanked = MetaProgress.BankedEchoes;
                _bankedText.text = string.Format(_bankedFormat, _shownBanked);
            }
        }
    }
}
