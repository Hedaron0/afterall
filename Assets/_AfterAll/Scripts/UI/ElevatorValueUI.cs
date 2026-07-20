using AfterAll.Items.Loot;
using AfterAll.Run;
using TMPro;
using UnityEngine;

namespace AfterAll.UI
{
    /// <summary>
    /// Running total of what GoUp() would actually bank right now: everything physically resting
    /// in ElevatorStashVolume (this already includes a BulkyCarrier-held item the instant its
    /// collider is inside the cabin — no separate carrier tracking needed, and adding one would
    /// double-count it) plus EchoPocket's abstract value, which only counts once the player
    /// themselves is physically standing inside (pockets have no physical presence to sweep).
    /// Placeholder styling — Harun's planned upgrade is an in-world LCD prop above the elevator's
    /// backrooms-side doorway gap, with animation/SFX ramping on value increase (gambling-feedback
    /// feel). See Core Design future note, 2026-07-20.
    /// </summary>
    public class ElevatorValueUI : MonoBehaviour
    {
        [SerializeField] private TMP_Text _valueText;
        [SerializeField] private string _format = "Value: {0}";
        [Tooltip("On: text only shows while the player is physically inside the cabin (HUD placeholder behavior). Off: always on — for the in-world LCD prop.")]
        [SerializeField] private bool _onlyWhileInElevator = true;
        [SerializeField] private RunDirector _runDirector;
        [SerializeField] private EchoPocket _pocket;

        private void Awake()
        {
            if (_runDirector == null) _runDirector = FindAnyObjectByType<RunDirector>();
            if (_pocket == null) _pocket = FindAnyObjectByType<EchoPocket>();
        }

        private void Update()
        {
            if (_valueText == null || _runDirector == null)
                return;

            ElevatorStashVolume stash = _runDirector.GetCurrentElevatorStashVolume();
            bool playerInside = stash != null && stash.PlayerInside;

            bool show = !_onlyWhileInElevator || playerInside;
            if (_valueText.enabled != show)
                _valueText.enabled = show;

            if (!show)
                return;

            int stashValue = stash?.CurrentValue ?? 0;
            int pocketValue = playerInside ? (_pocket?.PeekValue() ?? 0) : 0;
            _valueText.text = string.Format(_format, stashValue + pocketValue);
        }
    }
}
