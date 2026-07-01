using UnityEngine;

namespace AfterAll.Environment
{
    /// <summary>
    /// Cuts a 1.3m gap between WallLeft/WallRight and exposes a Socket for room connection.
    /// Socket position always comes from live wall transforms — never baked editor world coords.
    /// </summary>
    [ExecuteAlways]
    public class WallGapController : MonoBehaviour
    {
        private const float FrameYawDegrees = 90f;

        public float gapWidth = 1.3f;
        public bool hasOpening;
        public bool randomizeOffset = true;
        public float gapOffset;

        public Transform wallLeft;
        public Transform wallRight;

        [SerializeField] private GameObject _framePrefab;
        [SerializeField, Range(0f, 1f)] private float _frameChance = 0.35f;

        [HideInInspector] [SerializeField] private float _wallLengthM;
        [HideInInspector] [SerializeField] private float _leftExtentM;
        [HideInInspector] [SerializeField] private float _rightExtentM;
        [HideInInspector] [SerializeField] private Vector3 _axisWorld;
        [HideInInspector] [SerializeField] private Vector3 _seamWorld;
        [HideInInspector] [SerializeField] private int _leftScaleAxis;
        [HideInInspector] [SerializeField] private int _rightScaleAxis;
        [HideInInspector] [SerializeField] private bool _baselineCached;

        private bool? _spawnFrameOverride;
        private GameObject _spawnedFrame;
        private RoomSocket _socket;

        public float WallLengthMeters => _wallLengthM;

        public bool TryGetSocket(out RoomSocket socket)
        {
            socket = _socket;
            return hasOpening && _socket != null && _socket.gameObject.activeSelf;
        }

        public static float GetWallCenterGapOffset(WallGapController wall)
        {
            if (wall == null)
                return 0f;

            wall.EnsureBaseline();
            float effective = Mathf.Min(wall.gapWidth, wall._wallLengthM - 0.05f);
            return Mathf.Max(0f, (wall._wallLengthM - effective) * 0.5f);
        }

        public void ConfigureOpening(bool open, bool spawnFrame, float offsetMeters = 0f)
        {
            hasOpening = open;
            _spawnFrameOverride = spawnFrame;
            gapOffset = offsetMeters;
            randomizeOffset = false;
            ApplyGap();
        }

        public void ApplyGap() => ApplyGapInternal(useRandomOffset: randomizeOffset);

        private void Awake()
        {
            if (Application.isPlaying)
                EnsureBaseline();
        }

        private void Start()
        {
            if (Application.isPlaying)
                ApplyGap();
        }

        private void OnEnable()
        {
            AutoFindChildren();
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                if (!_baselineCached)
                    RebuildBaseline();
                ApplyGap();
            }
#endif
        }

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

        private void EnsureBaseline()
        {
            if (Application.isPlaying)
                RebuildBaseline();
            else if (!_baselineCached)
                RebuildBaseline();
        }

        private void ApplyGapInternal(bool useRandomOffset)
        {
            AutoFindChildren();
            ClearSpawnedFrame();

            if (wallLeft == null || wallRight == null)
                return;

            EnsureBaseline();

            if (_wallLengthM < 0.1f)
                return;

            if (!hasOpening)
            {
                HideSocket();
                RestoreClosed();
                return;
            }

            if (!TryComputeGapMetrics(useRandomOffset, out float d, out float effectiveGapWidth))
            {
                HideSocket();
                RestoreClosed();
                return;
            }

            float gapLeftT = -_leftExtentM + d;
            float gapRightT = gapLeftT + effectiveGapWidth;

            PlacePivot(wallLeft, gapLeftT, _leftScaleAxis, _leftExtentM > 0.0001f ? d / _leftExtentM : 0f);
            PlacePivot(
                wallRight,
                gapRightT,
                _rightScaleAxis,
                _rightExtentM > 0.0001f ? (_wallLengthM - d - effectiveGapWidth) / _rightExtentM : 0f);

            UpdateSocketFromLiveGap();
            TrySpawnFrame(effectiveGapWidth);
        }

        /// <summary>Gap center = midpoint between split wall pivots (always correct world position).</summary>
        private void UpdateSocketFromLiveGap()
        {
            Vector3 center = (wallLeft.position + wallRight.position) * 0.5f;
            center.y = GetWallFloorY();

            Vector3 outward = ComputeOutwardForward(center);
            EnsureSocket();
            _socket.Bind(this, gapWidth);
            _socket.AlignAt(center, outward, gapWidth);
        }

        private void EnsureSocket()
        {
            if (_socket != null)
                return;

            Transform roomRoot = GetComponentInParent<RoomInstance>()?.transform ?? transform.root;
            Transform existing = roomRoot.Find("Socket_" + name);
            if (existing != null)
            {
                _socket = existing.GetComponent<RoomSocket>();
                if (_socket != null)
                    return;
            }

            var go = new GameObject("Socket_" + name);
            go.transform.SetParent(roomRoot, false);
            _socket = go.AddComponent<RoomSocket>();
        }

        private void HideSocket()
        {
            if (_socket != null)
                _socket.gameObject.SetActive(false);
        }

        private Vector3 ComputeOutwardForward(Vector3 gapCenter)
        {
            RoomInstance room = GetComponentInParent<RoomInstance>();
            Vector3 origin = room != null ? room.GetApproximateCenter() : transform.position;

            Vector3 outward = gapCenter - origin;
            outward.y = 0f;
            return outward.sqrMagnitude > 0.001f ? outward.normalized : transform.forward;
        }

        private bool TryComputeGapMetrics(bool useRandomOffset, out float gapOffsetM, out float effectiveGapWidth)
        {
            gapOffsetM = 0f;
            effectiveGapWidth = 0f;

            if (_wallLengthM < 0.1f)
                return false;

            effectiveGapWidth = Mathf.Min(gapWidth, _wallLengthM - 0.05f);
            if (effectiveGapWidth < 0.05f)
                return false;

            float maxD = Mathf.Max(0f, _wallLengthM - effectiveGapWidth);
            gapOffsetM = useRandomOffset
                ? Random.Range(0f, maxD)
                : Mathf.Clamp(gapOffset, 0f, maxD);

            return true;
        }

        private void TrySpawnFrame(float effectiveGapWidth)
        {
            if (_framePrefab == null || !ShouldSpawnFrame())
                return;

            Vector3 center = (wallLeft.position + wallRight.position) * 0.5f;
            center.y = GetWallFloorY();
            Vector3 outward = ComputeOutwardForward(center);
            Quaternion rot = Quaternion.LookRotation(outward, Vector3.up) * Quaternion.Euler(0f, FrameYawDegrees, 0f);

            _spawnedFrame = InstantiateFrame();
            _spawnedFrame.transform.SetPositionAndRotation(center, rot);
            _spawnedFrame.transform.SetParent(transform, true);
        }

        private bool ShouldSpawnFrame()
        {
            if (_spawnFrameOverride.HasValue)
                return _spawnFrameOverride.Value;

            return Application.isPlaying && Random.value <= _frameChance;
        }

        private float GetWallFloorY()
        {
            Renderer leftR = wallLeft.GetComponentInChildren<Renderer>();
            Renderer rightR = wallRight.GetComponentInChildren<Renderer>();
            return leftR != null && rightR != null
                ? Mathf.Min(leftR.bounds.min.y, rightR.bounds.min.y)
                : transform.position.y;
        }

        private GameObject InstantiateFrame()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                return (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(_framePrefab);
#endif
            return Instantiate(_framePrefab);
        }

        private void ClearSpawnedFrame()
        {
            if (_spawnedFrame == null)
                return;

#if UNITY_EDITOR
            if (!Application.isPlaying)
                DestroyImmediate(_spawnedFrame);
            else
#endif
                Destroy(_spawnedFrame);

            _spawnedFrame = null;
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

            if (maxT - minT < 0.1f)
                return false;

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
            axisWorld = Vector3.right;
            Vector3 delta = right.center - left.center;
            delta.y = 0f;

            if (delta.sqrMagnitude < 0.0001f)
                return false;

            axisWorld = delta.normalized;
            return true;
        }

        private static int FindScaleAxis(Transform piece, Vector3 axisWorld)
        {
            int best = 0;
            float bestDot = 0f;

            for (int i = 0; i < 3; i++)
            {
                float dot = Mathf.Abs(Vector3.Dot(piece.TransformDirection(Axis(i)).normalized, axisWorld));
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
                float t = Vector3.Dot(c + Vector3.Scale(e, new Vector3(x, y, z)) - origin, axis);
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

        private void OnDrawGizmosSelected()
        {
            if (!hasOpening || _socket == null || !_socket.gameObject.activeSelf)
                return;

            Transform s = _socket.transform;
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(s.position, 0.15f);
            Gizmos.DrawLine(s.position, s.position + s.forward * 1.5f);
        }
#endif
    }
}
