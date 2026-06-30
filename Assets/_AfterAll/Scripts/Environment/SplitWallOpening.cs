using UnityEngine;

namespace AfterAll.Environment
{
    /// <summary>
    /// Splits a wall into left/right mesh segments and opens a fixed-width gap via transform scale/position.
    /// Wall run axis is this transform's local +X (left corner at x = 0, right at x = wall length).
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class SplitWallOpening : MonoBehaviour
    {
        public const float GapWidth = 1.3f;

        [SerializeField] private Transform _wallLeft;
        [SerializeField] private Transform _wallRight;
        [SerializeField] private Transform _frameAnchor;
        [SerializeField] private GameObject _framePrefab;
        [SerializeField] private float _wallLength = 8f;
        [SerializeField] private float _referenceSegmentLength = 4f;
        [SerializeField] private bool _hasOpening;
        [SerializeField] private float _doorOffset;
        [SerializeField] private bool _placeFrame;
        [SerializeField] private bool _invertScaleAxis;

        private struct TransformState
        {
            public Vector3 LocalPosition;
            public Vector3 LocalScale;
            public Quaternion LocalRotation;
        }

        private TransformState _leftDefault;
        private TransformState _rightDefault;
        private bool _defaultsCached;
        private GameObject _spawnedFrame;

        public Transform WallLeft => _wallLeft;
        public Transform WallRight => _wallRight;
        public float WallLength => _wallLength;
        public bool HasOpening => _hasOpening;
        public float DoorOffset => _doorOffset;
        public bool PlaceFrame => _placeFrame;

        public void Configure(
            bool hasOpening,
            float doorOffset,
            bool placeFrame,
            float wallLength = -1f)
        {
            _hasOpening = hasOpening;
            _doorOffset = doorOffset;
            _placeFrame = placeFrame;

            if (wallLength > 0f)
                _wallLength = wallLength;

            Apply();
        }

        [ContextMenu("Apply Opening")]
        public void Apply()
        {
            if (_wallLeft == null || _wallRight == null)
                return;

            CacheDefaultsIfNeeded();

            if (!_hasOpening)
            {
                RestoreDefaults();
                ClearFrame();
                return;
            }

            var offset = Mathf.Clamp(_doorOffset, 0f, Mathf.Max(0f, _wallLength - GapWidth));
            var leftWidth = offset;
            var rightWidth = _wallLength - offset - GapWidth;

            ApplySegment(_wallLeft, _leftDefault, leftWidth, _leftEdgePivotAtRight: true, anchorFromLeft: 0f);
            ApplySegment(_wallRight, _rightDefault, rightWidth, _leftEdgePivotAtRight: false, anchorFromLeft: offset + GapWidth);
            UpdateFrame(offset);
        }

        private void ApplySegment(
            Transform segment,
            TransformState defaults,
            float targetWidth,
            bool leftEdgePivotAtRight,
            float anchorFromLeft)
        {
            segment.localRotation = defaults.LocalRotation;

            if (targetWidth <= 0.0001f)
            {
                segment.localScale = Vector3.zero;
                segment.localPosition = defaults.LocalPosition;
                return;
            }

            var scaleFactor = targetWidth / Mathf.Max(0.0001f, _referenceSegmentLength);
            var scale = defaults.LocalScale;
            var axisScale = Mathf.Abs(defaults.LocalScale.x) * scaleFactor;
            scale.x = _invertScaleAxis ? -axisScale : axisScale;
            segment.localScale = scale;

            if (leftEdgePivotAtRight)
                segment.localPosition = new Vector3(anchorFromLeft + targetWidth, defaults.LocalPosition.y, defaults.LocalPosition.z);
            else
                segment.localPosition = new Vector3(anchorFromLeft, defaults.LocalPosition.y, defaults.LocalPosition.z);
        }

        private void RestoreDefaults()
        {
            ApplyState(_wallLeft, _leftDefault);
            ApplyState(_wallRight, _rightDefault);
        }

        private static void ApplyState(Transform target, TransformState state)
        {
            target.localPosition = state.LocalPosition;
            target.localScale = state.LocalScale;
            target.localRotation = state.LocalRotation;
        }

        private void CacheDefaultsIfNeeded()
        {
            if (_defaultsCached)
                return;

            _leftDefault = CaptureState(_wallLeft);
            _rightDefault = CaptureState(_wallRight);
            _defaultsCached = true;

            if (_referenceSegmentLength <= 0.0001f)
                _referenceSegmentLength = _wallLength * 0.5f;
        }

        private static TransformState CaptureState(Transform target)
        {
            return new TransformState
            {
                LocalPosition = target.localPosition,
                LocalScale = target.localScale,
                LocalRotation = target.localRotation
            };
        }

        private void UpdateFrame(float offset)
        {
            ClearFrame();

            if (!_placeFrame || _framePrefab == null)
                return;

            var anchor = _frameAnchor != null ? _frameAnchor : transform;
            var localCenter = new Vector3(offset + GapWidth * 0.5f, 0f, 0f);
            var worldPos = transform.TransformPoint(localCenter);
            var worldRot = transform.rotation;

            _spawnedFrame = Instantiate(_framePrefab, worldPos, worldRot, anchor);
            _spawnedFrame.name = "DoorFrame";
        }

        private void ClearFrame()
        {
            if (_spawnedFrame == null)
                return;

            if (Application.isPlaying)
                Destroy(_spawnedFrame);
            else
                DestroyImmediate(_spawnedFrame);

            _spawnedFrame = null;
        }

        private void OnValidate()
        {
            if (Application.isPlaying)
                return;

            Apply();
        }

        private void OnDrawGizmosSelected()
        {
            if (!_hasOpening)
                return;

            var offset = Mathf.Clamp(_doorOffset, 0f, Mathf.Max(0f, _wallLength - GapWidth));
            var start = transform.TransformPoint(new Vector3(offset, 0f, 0f));
            var end = transform.TransformPoint(new Vector3(offset + GapWidth, 0f, 0f));

            Gizmos.color = Color.green;
            Gizmos.DrawLine(start, end);
            Gizmos.DrawWireSphere(start, 0.08f);
            Gizmos.DrawWireSphere(end, 0.08f);
        }
    }
}
