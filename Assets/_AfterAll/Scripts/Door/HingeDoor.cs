using System.Collections;
using UnityEngine;

namespace AfterAll.Door
{
    /// <summary>
    /// Toggle open/close on a hinge transform.
    /// Door BoxCollider stays solid — blocks the panel while it swings; walk through the
    /// doorway gap once the door is open, not through the door itself.
    /// </summary>
    public class HingeDoor : MonoBehaviour
    {
        [SerializeField] private float _openAngle = 90f;
        [SerializeField] private float _swingSpeed = 3f;
        [SerializeField] private Collider _doorCollider;

        private bool _isOpen;
        private Coroutine _swingRoutine;
        private Quaternion _closedRotation;
        private Quaternion _openRotation;

        public bool IsOpen => _isOpen;

        private void Awake()
        {
            _closedRotation = transform.localRotation;
            _openRotation = Quaternion.Euler(
                transform.localEulerAngles + new Vector3(0f, _openAngle, 0f));

            if (_doorCollider == null)
            {
                Transform door = transform.Find("Door");
                if (door != null)
                    _doorCollider = door.GetComponent<Collider>();
            }

            EnsureSolidCollider();
        }

        public void Toggle()
        {
            SetOpen(!_isOpen);
        }

        public void SetOpen(bool open)
        {
            if (_isOpen == open && _swingRoutine == null)
                return;

            _isOpen = open;
            EnsureSolidCollider();
            BeginSwing(open ? _openRotation : _closedRotation);
        }

        private void BeginSwing(Quaternion target)
        {
            if (_swingRoutine != null)
                StopCoroutine(_swingRoutine);

            _swingRoutine = StartCoroutine(SwingTo(target));
        }

        private IEnumerator SwingTo(Quaternion target)
        {
            while (Quaternion.Angle(transform.localRotation, target) > 0.5f)
            {
                transform.localRotation = Quaternion.Slerp(
                    transform.localRotation, target, _swingSpeed * Time.deltaTime);
                yield return null;
            }

            transform.localRotation = target;
            _swingRoutine = null;
        }

        private void EnsureSolidCollider()
        {
            if (_doorCollider == null)
                return;

            _doorCollider.enabled = true;
            _doorCollider.isTrigger = false;
        }
    }
}
