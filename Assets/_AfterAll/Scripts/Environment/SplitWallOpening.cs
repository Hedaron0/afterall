using UnityEngine;

namespace AfterAll.Environment
{
    /// <summary>
    /// Scales two pivot-aligned wall halves to create a fixed-width opening (gap or framed door).
    /// Wall_Left pivot: right edge (fixed left end). Wall_Right pivot: left edge (fixed right end).
    /// </summary>
    [ExecuteAlways]
    public class SplitWallOpening : MonoBehaviour
    {
        [SerializeField] private Transform _wallLeft;
        [SerializeField] private Transform _wallRight;
        [SerializeField] private Transform _frameAnchor;
        [SerializeField] private GameObject _framePrefab;

        [SerializeField] private float _wallLength = 8f;
        [SerializeField] private float _referenceSegmentLength = 4f;
        [SerializeField] private float _openingWidth = 1.3f;

        [SerializeField] private bool _hasOpening;
        [SerializeField] private float _doorOffset;
        [SerializeField] private bool _placeFrame;
        [SerializeField] private bool _invertScaleAxis;

        private GameObject _spawnedFrame;

        private void OnEnable()
        {
            ApplyLayout();
        }

        private void Reset()
        {
            ApplyLayout();
        }

        private void OnValidate()
        {
            ApplyLayout();
        }

        [ContextMenu("Apply Layout")]
        public void ApplyLayout()
        {
            if (_wallLeft == null || _wallRight == null || _referenceSegmentLength <= 0f || _wallLength <= 0f)
                return;

            float openingWidth = _hasOpening ? Mathf.Max(0f, _openingWidth) : 0f;
            float maxOffset = Mathf.Max(0f, _wallLength - openingWidth);
            float doorOffset = _hasOpening ? Mathf.Clamp(_doorOffset, 0f, maxOffset) : 0f;

            float leftLength;
            float rightLength;
            float rightPosition;
            float framePosition;

            if (_hasOpening)
            {
                leftLength = doorOffset;
                rightLength = _wallLength - doorOffset - openingWidth;
                rightPosition = doorOffset + openingWidth;
                framePosition = doorOffset + openingWidth * 0.5f;
            }
            else
            {
                leftLength = _wallLength * 0.5f;
                rightLength = _wallLength * 0.5f;
                rightPosition = leftLength;
                framePosition = 0f;
            }

            float leftScale = leftLength / _referenceSegmentLength;
            float rightScale = rightLength / _referenceSegmentLength;
            float metersToLocal = 1f / Mathf.Max(GetAxisLossyScale(), 0.0001f);

            SetAxisScale(_wallLeft, leftScale);
            SetAxisPosition(_wallLeft, 0f);
            SetAxisScale(_wallRight, rightScale);
            SetAxisPosition(_wallRight, rightPosition * metersToLocal);

            if (_frameAnchor != null)
            {
                SetAxisPosition(_frameAnchor, framePosition * metersToLocal);
                UpdateFrame(_hasOpening && _placeFrame);
            }
            else
            {
                UpdateFrame(false);
            }
        }

        private void SetAxisScale(Transform target, float scale)
        {
            Vector3 localScale = target.localScale;
            if (_invertScaleAxis)
                localScale.z = scale;
            else
                localScale.x = scale;

            target.localScale = localScale;
        }

        private void SetAxisPosition(Transform target, float position)
        {
            Vector3 localPosition = target.localPosition;
            if (_invertScaleAxis)
                localPosition.z = position;
            else
                localPosition.x = position;

            target.localPosition = localPosition;
        }

        private float GetAxisLossyScale()
        {
            Vector3 lossyScale = transform.lossyScale;
            return _invertScaleAxis ? lossyScale.z : lossyScale.x;
        }

        private void UpdateFrame(bool shouldPlace)
        {
            if (_spawnedFrame != null)
            {
                if (Application.isPlaying)
                    Destroy(_spawnedFrame);
                else
                    DestroyImmediate(_spawnedFrame);

                _spawnedFrame = null;
            }

            if (!shouldPlace || _framePrefab == null || _frameAnchor == null)
                return;

            _spawnedFrame = Instantiate(_framePrefab, _frameAnchor);
            _spawnedFrame.name = _framePrefab.name;
            _spawnedFrame.transform.localPosition = Vector3.zero;
            _spawnedFrame.transform.localRotation = Quaternion.identity;
            _spawnedFrame.transform.localScale = Vector3.one;
        }
    }
}
