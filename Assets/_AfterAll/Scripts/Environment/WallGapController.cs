using UnityEngine;

namespace AfterAll.Environment
{
    /// <summary>
    /// Opens a gap by moving each half's pivot apart and scaling it shorter.
    /// All math is in world space — works on any wall orientation / FBX export. No manual axis setup.
    /// </summary>
    [ExecuteAlways]
    public class WallGapController : MonoBehaviour
    {
        public float gapWidth = 1.3f;
        public bool hasOpening;
        public bool randomizeOffset = true;
        public float gapOffset;

        public Transform wallLeft;
        public Transform wallRight;

        [HideInInspector] [SerializeField] private float _wallLengthM;
        [HideInInspector] [SerializeField] private float _leftExtentM;
        [HideInInspector] [SerializeField] private float _rightExtentM;
        [HideInInspector] [SerializeField] private Vector3 _axisWorld;
        [HideInInspector] [SerializeField] private Vector3 _seamWorld;
        [HideInInspector] [SerializeField] private int _leftScaleAxis;
        [HideInInspector] [SerializeField] private int _rightScaleAxis;
        [HideInInspector] [SerializeField] private bool _baselineCached;

        private void Awake()
        {
            if (Application.isPlaying)
                RebuildBaseline();
        }

        private void Reset()
        {
            RebuildBaseline();
            ApplyGap();
        }

        private void OnEnable()
        {
            AutoFindChildren();
            if (!_baselineCached)
                RebuildBaseline();

#if UNITY_EDITOR
            if (!Application.isPlaying)
                ApplyGap();
#endif
        }

        private void Start() => ApplyGap();

        public void AutoFindChildren()
        {
            if (wallLeft != null && wallRight != null)
                return;

            foreach (Transform child in transform)
            {
                if (wallLeft == null && child.name.Contains("WallLeft"))
                    wallLeft = child;
                else if (wallRight == null && child.name.Contains("WallRight"))
                    wallRight = child;
            }
        }

        [ContextMenu("Recache Baseline")]
        public void RecacheBaseline()
        {
            RebuildBaseline();
            ApplyGap();
        }

        [ContextMenu("Apply Gap")]
        public void ApplyGap()
        {
            AutoFindChildren();
            if (wallLeft == null || wallRight == null)
                return;

            if (!_baselineCached)
                RebuildBaseline();

            if (_wallLengthM < 0.5f)
                return;

            if (!hasOpening)
            {
                RestoreClosed();
                return;
            }

            float maxD = Mathf.Max(0f, _wallLengthM - gapWidth);
            float d = randomizeOffset
                ? Random.Range(0f, maxD)
                : Mathf.Clamp(gapOffset, 0f, maxD);

            // t = meters along wall axis from seam; left outer edge is at -_leftExtentM.
            float gapLeftT = -_leftExtentM + d;
            float gapRightT = gapLeftT + gapWidth;

            float leftScale = _leftExtentM > 0.0001f ? d / _leftExtentM : 0f;
            float rightLen = _wallLengthM - d - gapWidth;
            float rightScale = _rightExtentM > 0.0001f ? rightLen / _rightExtentM : 0f;

            PlacePivot(wallLeft, gapLeftT, _leftScaleAxis, leftScale);
            PlacePivot(wallRight, gapRightT, _rightScaleAxis, rightScale);
        }

        private void RebuildBaseline()
        {
            AutoFindChildren();
            RestoreClosed();
            _baselineCached = CacheFromClosedMesh();
        }

        private void RestoreClosed()
        {
            if (wallLeft != null)
            {
                wallLeft.localPosition = Vector3.zero;
                wallLeft.localScale = Vector3.one;
            }

            if (wallRight != null)
            {
                wallRight.localPosition = Vector3.zero;
                wallRight.localScale = Vector3.one;
            }
        }

        private bool CacheFromClosedMesh()
        {
            if (wallLeft == null || wallRight == null)
                return false;

            Renderer leftR = wallLeft.GetComponentInChildren<Renderer>();
            Renderer rightR = wallRight.GetComponentInChildren<Renderer>();
            if (leftR == null || rightR == null)
                return false;

            _seamWorld = (wallLeft.position + wallRight.position) * 0.5f;

            if (!TryGetWallAxis(leftR.bounds, rightR.bounds, out _axisWorld))
                return false;

            Bounds combined = leftR.bounds;
            combined.Encapsulate(rightR.bounds);
            ProjectBounds(combined, _seamWorld, _axisWorld, out float minT, out float maxT);

            if (maxT - minT < 0.5f)
                return false;

            // Ensure axis points left-to-right (min side = left outer edge).
            if (minT > maxT)
            {
                _axisWorld = -_axisWorld;
                (minT, maxT) = (maxT, minT);
            }

            _leftExtentM = -minT;
            _rightExtentM = maxT;
            _wallLengthM = maxT - minT;

            _leftScaleAxis = FindScaleAxis(wallLeft, _axisWorld);
            _rightScaleAxis = FindScaleAxis(wallRight, _axisWorld);

            return _leftExtentM > 0.05f && _rightExtentM > 0.05f;
        }

        private void PlacePivot(Transform piece, float tMeters, int scaleAxis, float scaleFactor)
        {
            Vector3 worldPos = _seamWorld + _axisWorld * tMeters;
            piece.localPosition = transform.InverseTransformPoint(worldPos);

            Vector3 scale = piece.localScale;
            SetAxis(ref scale, scaleAxis, Mathf.Max(0f, scaleFactor));
            piece.localScale = scale;
        }

        private static void SetAxis(ref Vector3 vector, int axis, float value)
        {
            switch (axis)
            {
                case 1: vector.y = value; break;
                case 2: vector.z = value; break;
                default: vector.x = value; break;
            }
        }

        private static bool TryGetWallAxis(Bounds left, Bounds right, out Vector3 axisWorld)
        {
            Bounds combined = left;
            combined.Encapsulate(right);
            Vector3 size = combined.size;

            int worldAxis = 0;
            float best = size.x;
            if (size.y > best) { best = size.y; worldAxis = 1; }
            if (size.z > best) worldAxis = 2;

            axisWorld = worldAxis switch
            {
                1 => Vector3.up,
                2 => Vector3.forward,
                _ => Vector3.right
            };

            // Sign: left mesh center should be behind right mesh center on this axis.
            Vector3 leftCenter = left.center;
            Vector3 rightCenter = right.center;
            float delta = Vector3.Dot(rightCenter - leftCenter, axisWorld);
            if (Mathf.Abs(delta) < 0.01f)
                return false;

            if (delta < 0f)
                axisWorld = -axisWorld;

            return true;
        }

        private static int FindScaleAxis(Transform piece, Vector3 axisWorld)
        {
            int best = 0;
            float bestDot = 0f;

            for (int i = 0; i < 3; i++)
            {
                Vector3 localDir = piece.TransformDirection(Axis(i)).normalized;
                float dot = Mathf.Abs(Vector3.Dot(localDir, axisWorld));
                if (dot > bestDot)
                {
                    bestDot = dot;
                    best = i;
                }
            }

            return best;
        }

        private static void ProjectBounds(Bounds bounds, Vector3 origin, Vector3 axis, out float minT, out float maxT)
        {
            minT = float.PositiveInfinity;
            maxT = float.NegativeInfinity;
            Vector3 c = bounds.center;
            Vector3 e = bounds.extents;

            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                Vector3 corner = c + Vector3.Scale(e, new Vector3(x, y, z));
                float t = Vector3.Dot(corner - origin, axis);
                minT = Mathf.Min(minT, t);
                maxT = Mathf.Max(maxT, t);
            }
        }

        private static Vector3 Axis(int index) =>
            index switch { 1 => Vector3.up, 2 => Vector3.forward, _ => Vector3.right };

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (!Application.isPlaying)
            {
                UnityEditor.EditorApplication.delayCall += () =>
                {
                    if (this != null)
                        ApplyGap();
                };
            }
        }
#endif
    }
}
