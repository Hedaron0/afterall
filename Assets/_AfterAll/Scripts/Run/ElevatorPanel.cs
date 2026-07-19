using System.Collections;
using AfterAll.Interaction;
using AfterAll.UI;
using UnityEngine;

namespace AfterAll.Run
{
    /// <summary>
    /// Elevator arrow button. One instance per button (Down / Up) — mesh + collider
    /// live directly on this GameObject, which recesses itself on press.
    /// </summary>
    public class ElevatorPanel : MonoBehaviour, IInteractable
    {
        private enum Mode
        {
            Down,
            Up
        }

        [SerializeField] private Mode _mode = Mode.Down;

        [Header("Button Press")]
        [Tooltip("Local-space direction the button moves on press (into the panel). Normalized at use.")]
        [SerializeField] private Vector3 _recessLocalDirection = Vector3.back;
        [SerializeField] private float _recessDistanceM = 0.02f;
        [SerializeField] private float _recessDurationS = 0.15f;

        [Header("Press SFX")]
        [SerializeField] private AudioClip _pressClip;
        [SerializeField, Range(0f, 1f)] private float _pressVolume = 0.7f;

        private RunDirector _runDirector;
        private Vector3 _restLocalPos;
        private Coroutine _pressRoutine;

        public string Prompt => _mode == Mode.Down ? "Go Down" : "Go Up (Extract)";

        private void Awake()
        {
            _runDirector = FindAnyObjectByType<RunDirector>();
            if (_runDirector == null)
                Debug.LogError("[ElevatorPanel] No RunDirector found in scene.", this);

            _restLocalPos = transform.localPosition;
        }

        public void Interact()
        {
            if (_runDirector == null)
                return;

            if (_runDirector.State != RunState.InElevator)
                return;

            PlayPressAnim();
            PlayPressSfx();

            if (_mode == Mode.Down)
            {
                GameFeedbackUI.Show("Descending...");
                _runDirector.GoDown();
            }
            else
            {
                GameFeedbackUI.Show("Extracting...");
                _runDirector.GoUp();
            }
        }

        private void PlayPressAnim()
        {
            if (_pressRoutine != null)
                StopCoroutine(_pressRoutine);

            _pressRoutine = StartCoroutine(PressRoutine());
        }

        private void PlayPressSfx()
        {
            if (_pressClip != null)
                AudioSource.PlayClipAtPoint(_pressClip, transform.position, _pressVolume);
        }

        private IEnumerator PressRoutine()
        {
            Vector3 pressedLocalPos = _restLocalPos - _recessLocalDirection.normalized * _recessDistanceM;
            float half = _recessDurationS * 0.5f;

            float t = 0f;
            while (t < half)
            {
                t += Time.deltaTime;
                transform.localPosition = Vector3.Lerp(_restLocalPos, pressedLocalPos, t / half);
                yield return null;
            }

            t = 0f;
            while (t < half)
            {
                t += Time.deltaTime;
                transform.localPosition = Vector3.Lerp(pressedLocalPos, _restLocalPos, t / half);
                yield return null;
            }

            transform.localPosition = _restLocalPos;
            _pressRoutine = null;
        }
    }
}
