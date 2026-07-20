using AfterAll.Items.Loot;
using AfterAll.Run;
using TMPro;
using UnityEngine;

namespace AfterAll.UI
{
    /// <summary>
    /// S3: always-on running total shown while RunState == InElevator — sums whatever's resting
    /// in ElevatorStashVolume plus whatever's still carried (EchoPocket/BulkyCarrier), so the
    /// number always matches what GoUp() would actually bank right now.
    /// Placeholder styling — Harun's planned upgrade is an in-world LCD prop above the elevator's
    /// backrooms-side doorway gap, with animation/SFX ramping on value increase (gambling-feedback
    /// feel). See Core Design future note, 2026-07-20.
    /// </summary>
    public class ElevatorValueUI : MonoBehaviour
    {
        [SerializeField] private TMP_Text _valueText;
        [SerializeField] private string _format = "Value: {0}";
        [Tooltip("On: text only shows while RunState == InElevator (HUD placeholder behavior). Off: always on — for the in-world LCD prop.")]
        [SerializeField] private bool _onlyWhileInElevator = true;
        [SerializeField] private RunDirector _runDirector;
        [SerializeField] private EchoPocket _pocket;
        [SerializeField] private BulkyCarrier _carrier;

        private void Awake()
        {
            if (_runDirector == null) _runDirector = FindAnyObjectByType<RunDirector>();
            if (_pocket == null) _pocket = FindAnyObjectByType<EchoPocket>();
            if (_carrier == null) _carrier = FindAnyObjectByType<BulkyCarrier>();
        }

        private void Update()
        {
            if (_valueText == null || _runDirector == null)
                return;

            bool show = !_onlyWhileInElevator || _runDirector.State == RunState.InElevator;
            if (_valueText.enabled != show)
                _valueText.enabled = show;

            if (!show)
                return;

            int stashValue = _runDirector.GetCurrentElevatorStashVolume()?.CurrentValue ?? 0;
            int carriedValue = (_pocket?.PeekValue() ?? 0) + (_carrier?.PeekValue() ?? 0);
            _valueText.text = string.Format(_format, stashValue + carriedValue);
        }
    }
}
