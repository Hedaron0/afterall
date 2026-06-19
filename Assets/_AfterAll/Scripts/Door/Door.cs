using System.Collections;
using AfterAll.Interaction;
using AfterAll.Inventories;
using AfterAll.UI;
using UnityEngine;

namespace AfterAll.Door
{
    /// <summary>
    /// Door → Pivot → model (collider on model). One script for locked and unlocked doors.
    /// </summary>
    public class Door : MonoBehaviour, IInteractable
    {
        [SerializeField] private Transform _pivot;
        [SerializeField] private Collider _collider;
        [SerializeField] private bool _startsLocked;
        [SerializeField] private float _openAngle = 90f;
        [SerializeField] private float _swingSpeed = 4f;
        [SerializeField] private AudioClip _unlockClip;
        [SerializeField] private AudioClip _openClip;
        [SerializeField] private AudioClip _closeClip;
        [SerializeField] private float _sfxVolume = 0.7f;

        private bool _isOpen;
        private bool _unlocked;
        private Quaternion _closedRotation;
        private Quaternion _openRotation;
        private Inventory _inventory;
        private Coroutine _swingRoutine;

        public string Prompt
        {
            get
            {
                if (!_unlocked)
                {
                    return _inventory != null && _inventory.SelectedHas(ItemType.Key)
                        ? "Unlock door"
                        : "Locked";
                }

                return _isOpen ? "Close door" : "Open door";
            }
        }

        private void Awake()
        {
            ResolveReferences();
            _unlocked = !_startsLocked;
            _inventory = FindAnyObjectByType<Inventory>();
            _closedRotation = _pivot.localRotation;
            _openRotation = _closedRotation * Quaternion.Euler(0f, _openAngle, 0f);
        }

        public void Interact()
        {
            if (!_unlocked)
            {
                if (_inventory == null || !_inventory.SelectedHas(ItemType.Key))
                {
                    GameFeedbackUI.Show("Need a key in your selected slot.");
                    return;
                }

                if (!_inventory.TryConsumeSelectedIf(ItemType.Key))
                    return;

                _unlocked = true;
                GameFeedbackUI.Show("Door unlocked.");
                PlaySfx(_unlockClip);
                return;
            }

            SetOpen(!_isOpen);
        }

        private void SetOpen(bool open)
        {
            if (_isOpen == open && _swingRoutine == null)
                return;

            _isOpen = open;

            if (_collider != null)
            {
                _collider.enabled = true;
                _collider.isTrigger = false;
            }

            if (_swingRoutine != null)
                StopCoroutine(_swingRoutine);

            _swingRoutine = StartCoroutine(SwingTo(open ? _openRotation : _closedRotation));
            GameFeedbackUI.Show(open ? "Door opened." : "Door closed.");
            PlaySfx(open ? _openClip : _closeClip);
        }

        private void PlaySfx(AudioClip clip)
        {
            if (clip == null)
                return;

            Vector3 position = _collider != null ? _collider.bounds.center : transform.position;
            AudioSource.PlayClipAtPoint(clip, position, _sfxVolume);
        }

        private IEnumerator SwingTo(Quaternion target)
        {
            while (Quaternion.Angle(_pivot.localRotation, target) > 0.5f)
            {
                _pivot.localRotation = Quaternion.Slerp(
                    _pivot.localRotation, target, _swingSpeed * Time.deltaTime);
                yield return null;
            }

            _pivot.localRotation = target;
            _swingRoutine = null;
        }

        private void ResolveReferences()
        {
            if (_pivot == null)
                _pivot = transform.Find("Pivot");

            if (_pivot == null)
            {
                Debug.LogError($"[Door] {name}: missing Pivot child.", this);
                enabled = false;
                return;
            }

            if (_collider != null)
                return;

            foreach (Transform child in _pivot)
            {
                _collider = child.GetComponent<Collider>();
                if (_collider != null)
                    return;
            }

            Debug.LogWarning($"[Door] {name}: no collider under Pivot.", this);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (_pivot == null)
                _pivot = transform.Find("Pivot");

            if (_pivot != null && _collider == null)
            {
                foreach (Transform child in _pivot)
                {
                    _collider = child.GetComponent<Collider>();
                    if (_collider != null)
                        break;
                }
            }
        }
#endif
    }
}
