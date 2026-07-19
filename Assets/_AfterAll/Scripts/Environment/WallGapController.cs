using UnityEngine;
using System.Collections.Generic;

namespace AfterAll.Environment
{
    /// <summary>
    /// Opens a wall for room connection: StandardGap (1.3m cut), FullWall (hide pieces),
    /// or OpenEnd (socket portal). Socket position comes from live geometry — never baked world coords.
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

        [SerializeField] private WallOpeningMode _openingMode = WallOpeningMode.StandardGap;
        [SerializeField] private GameObject _framePrefab;
        [SerializeField, Range(0f, 1f)] private float _frameChance = 0.35f;

        [Tooltip("Authored per-wall recess: pulls this wall's cut pieces inward (opposite outward normal) " +
            "by this many meters, so a connecting room's coplanar wall doesn't z-fight with this one at the " +
            "seam. 0 = no change (default for ordinary room-to-room walls). Set only on walls where one side " +
            "of a specific connection needs to visually sit back from the other (e.g. the elevator entrance). " +
            "RoomSocket.SnapRoom already leaves up to ~0.1m of natural slop between snapped walls, so this " +
            "must exceed that (e.g. 0.15) to guarantee a real gap instead of accidentally landing exactly " +
            "coplanar with the other side.")]
        [SerializeField] private float _recessDepthM = 0f;

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

        /// <summary>Meters this wall's cut pieces recede inward (opposite outward normal) at the seam.</summary>
        public float RecessDepthM
        {
            get => _recessDepthM;
            set => _recessDepthM = value;
        }

        public WallOpeningMode OpeningMode => _openingMode;
        public bool UsesFullOpening =>
            _openingMode == WallOpeningMode.FullWall || _openingMode == WallOpeningMode.OpenEnd;

        /// <summary>Opening width used when connected (gapWidth or full wall length).</summary>
        public float EffectiveOpeningWidth
        {
            get
            {
                EnsureBaselineIfPossible();
                if (UsesFullOpening)
                    return Mathf.Max(0.05f, _wallLengthM > 0.1f ? _wallLengthM - 0.05f : gapWidth);
                return gapWidth > 0.05f ? gapWidth : RoomFootprint.DefaultGapWidthM;
            }
        }

        public void SetOpeningMode(WallOpeningMode mode) => _openingMode = mode;

        /// <summary>
        /// Slack when checking that one wall's face covers the other side's opening.
        /// Prevents wide FullWall holes with a narrow room leaving void flanks.
        /// </summary>
        public const float OpeningCoverSlackM = 0.35f;

        /// <summary>
        /// True when each connecting face is wide enough to cover the other side's opening.
        /// </summary>
        public static bool AreOpeningsPairable(
            float parentOpeningM,
            float parentWallLengthM,
            float childOpeningM,
            float childWallLengthM,
            float slackM = OpeningCoverSlackM)
        {
            if (parentOpeningM < 0.05f || childOpeningM < 0.05f)
                return false;
            if (parentWallLengthM < 0.05f || childWallLengthM < 0.05f)
                return false;

            // Narrower room must still cover the wider hole — otherwise empty flanks.
            if (childWallLengthM + slackM < parentOpeningM)
                return false;
            if (parentWallLengthM + slackM < childOpeningM)
                return false;

            return true;
        }

        public static bool AreOpeningsPairable(WallGapController parent, WallGapController child) =>
            parent != null &&
            child != null &&
            AreOpeningsPairable(
                parent.EffectiveOpeningWidth,
                parent.WallLengthMeters,
                child.EffectiveOpeningWidth,
                child.WallLengthMeters);

        /// <summary>
        /// Closed-wall geometry for footprint baking / 2D planning (world space).
        /// </summary>
        public bool TryGetClosedWallGeometry(
            out Vector3 seamWorld,
            out Vector3 axisWorld,
            out float lengthMeters,
            out Vector3 outwardWorld)
        {
            seamWorld = default;
            axisWorld = default;
            lengthMeters = 0f;
            outwardWorld = default;

            AutoFindChildren();
            if (!_baselineCached)
                RebuildBaseline();

            if (_openingMode == WallOpeningMode.OpenEnd)
            {
                // Portal: use transform as seam; length from serialized gap or measured pieces if present.
                if (_baselineCached && _wallLengthM >= 0.1f)
                {
                    seamWorld = _seamWorld;
                    axisWorld = _axisWorld;
                    lengthMeters = _wallLengthM;
                }
                else
                {
                    seamWorld = transform.position;
                    axisWorld = Vector3.ProjectOnPlane(transform.right, Vector3.up).normalized;
                    if (axisWorld.sqrMagnitude < 0.0001f)
                        axisWorld = Vector3.right;
                    lengthMeters = Mathf.Max(gapWidth, 1.3f);
                    _seamWorld = seamWorld;
                    _axisWorld = axisWorld;
                    _wallLengthM = lengthMeters;
                    _baselineCached = true;
                }

                outwardWorld = ComputeOutwardForward(seamWorld);
                return outwardWorld.sqrMagnitude > 0.0001f;
            }

            if (!_baselineCached || _wallLengthM < 0.1f)
                return false;

            seamWorld = _seamWorld;
            axisWorld = _axisWorld;
            lengthMeters = _wallLengthM;
            outwardWorld = ComputeOutwardForward(_seamWorld);
            return outwardWorld.sqrMagnitude > 0.0001f;
        }

        public bool TryGetSocket(out RoomSocket socket)
        {
            ResolveSocketReference();
            socket = _socket;
            return hasOpening && _socket != null && _socket.gameObject.activeSelf;
        }

        /// <summary>
        /// Returns a persisted socket child (if baked on prefab), even when the wall is closed.
        /// </summary>
        public bool TryGetBakedSocket(out RoomSocket socket)
        {
            ResolveSocketReference();
            socket = _socket;
            return socket != null;
        }

        /// <summary>
        /// Editor/prefab pass: open wall briefly, create or update socket + contract, then restore wall state.
        /// </summary>
        public bool BakeSocketContract()
        {
            bool wasOpen = hasOpening;
            float previousOffset = gapOffset;
            bool previousRandomize = randomizeOffset;

            float offset = GetWallCenterGapOffset(this, GapOffsetPolicy.Default);
            ConfigureOpening(true, false, offset);

            if (!TryGetSocket(out RoomSocket socket))
            {
                if (!wasOpen)
                    ConfigureOpening(false, false, 0f);
                return false;
            }

            socket.SetContract(
                RoomSocket.DirectionFromForward(socket.transform.forward),
                name,
                socket.SizeClass);
            socket.SetWallIndex(GetWallIndexInRoom());

            if (wasOpen)
            {
                ConfigureOpening(true, false, previousOffset);
                randomizeOffset = previousRandomize;
            }
            else
            {
                ConfigureOpening(false, false, 0f);
            }

            return socket.HasValidContract;
        }

        private int GetWallIndexInRoom()
        {
            RoomInstance room = GetComponentInParent<RoomInstance>();
            if (room == null)
                return -1;

            IReadOnlyList<WallGapController> walls = room.Walls;
            for (int i = 0; i < walls.Count; i++)
            {
                if (walls[i] == this)
                    return i;
            }

            return -1;
        }

        public static float GetWallCenterGapOffset(WallGapController wall, GapOffsetPolicy policy = default)
        {
            if (wall == null)
                return 0f;

            if (policy.edgeMarginM <= 0f && policy.spanFraction <= 0f)
                policy = GapOffsetPolicy.Default;

            wall.EnsureBaseline();
            if (!wall.TryGetGapOffsetRange(policy, out float minOffset, out float maxOffset, out _))
                return 0f;

            return (minOffset + maxOffset) * 0.5f;
        }

        public bool TryGetGapOffsetRange(
            out float minOffset,
            out float maxOffset,
            out float effectiveGapWidth)
        {
            return TryGetGapOffsetRange(GapOffsetPolicy.Default, out minOffset, out maxOffset, out effectiveGapWidth);
        }

        public bool TryGetGapOffsetRange(
            GapOffsetPolicy policy,
            out float minOffset,
            out float maxOffset,
            out float effectiveGapWidth)
        {
            EnsureBaselineIfPossible();
            minOffset = 0f;
            maxOffset = 0f;
            effectiveGapWidth = 0f;

            if (_wallLengthM < 0.1f && _openingMode != WallOpeningMode.OpenEnd)
                return false;

            if (UsesFullOpening)
            {
                effectiveGapWidth = EffectiveOpeningWidth;
                float center = Mathf.Max(0f, (_wallLengthM - effectiveGapWidth) * 0.5f);
                minOffset = center;
                maxOffset = center;
                return effectiveGapWidth >= 0.05f;
            }

            float edgeMargin = Mathf.Max(0f, policy.edgeMarginM);
            float spanFraction = Mathf.Clamp(policy.spanFraction > 0f ? policy.spanFraction : 1f, 0f, 1f);
            const float safetyM = 0.05f;

            float maxGapForWall = Mathf.Max(0f, _wallLengthM - edgeMargin * 2f - safetyM);
            effectiveGapWidth = Mathf.Min(gapWidth, maxGapForWall);
            if (effectiveGapWidth < 0.05f)
                return false;

            float usableSpan = Mathf.Max(0f, _wallLengthM - effectiveGapWidth);
            float clampedSpan = usableSpan * spanFraction;
            minOffset = edgeMargin + (usableSpan - clampedSpan) * 0.5f;
            maxOffset = minOffset + clampedSpan;

            if (maxOffset < minOffset)
            {
                float center = usableSpan * 0.5f;
                minOffset = center;
                maxOffset = center;
            }

            return true;
        }

        public bool TryGetOpeningCorridorBounds(
            float offsetMeters,
            out Bounds corridor,
            float inwardDepth = 1.5f,
            float corridorHeight = 3f)
        {
            corridor = default;
            AutoFindChildren();

            // Do not call EnsureBaseline() here — in play mode it rebuilds via RestoreClosed()
            // without re-applying the gap, which seals every wall after content activation.
            if (!_baselineCached)
                RebuildBaseline();

            if (_wallLengthM < 0.1f)
                return false;

            float effectiveGapWidth = UsesFullOpening
                ? EffectiveOpeningWidth
                : Mathf.Min(gapWidth, _wallLengthM - 0.05f);
            if (effectiveGapWidth < 0.05f)
                return false;

            if (!UsesFullOpening && (wallLeft == null || wallRight == null))
                return false;

            float maxD = Mathf.Max(0f, _wallLengthM - effectiveGapWidth);
            float d = Mathf.Clamp(offsetMeters, 0f, maxD);

            float gapLeftT = -_leftExtentM + d;
            float gapRightT = gapLeftT + effectiveGapWidth;
            float floorY = GetWallFloorY();

            Vector3 leftEdge = _seamWorld + _axisWorld * gapLeftT;
            Vector3 rightEdge = _seamWorld + _axisWorld * gapRightT;
            leftEdge.y = floorY;
            rightEdge.y = floorY;

            Vector3 gapCenter = (leftEdge + rightEdge) * 0.5f;
            Vector3 inward = -ComputeOutwardForward(gapCenter);
            inward.y = 0f;
            if (inward.sqrMagnitude < 0.0001f)
                inward = -Vector3.Cross(Vector3.up, _axisWorld);
            inward.Normalize();

            Vector3 innerLeft = leftEdge + inward * inwardDepth;
            Vector3 innerRight = rightEdge + inward * inwardDepth;

            corridor = new Bounds(gapCenter, Vector3.zero);
            Vector3[] points = { leftEdge, rightEdge, innerLeft, innerRight };
            foreach (Vector3 point in points)
            {
                corridor.Encapsulate(point);
                corridor.Encapsulate(point + Vector3.up * corridorHeight);
            }

            return corridor.size.sqrMagnitude > 0.0001f;
        }

        public static float GetRandomGapOffset(
            WallGapController wall,
            System.Random rng,
            GapOffsetPolicy policy = default)
        {
            if (wall == null || rng == null)
                return GetWallCenterGapOffset(wall, policy);

            if (!wall.TryGetGapOffsetRange(policy, out float minOffset, out float maxOffset, out _))
                return GetWallCenterGapOffset(wall, policy);

            if (Mathf.Abs(maxOffset - minOffset) < 0.0001f)
                return minOffset;

            return minOffset + (float)rng.NextDouble() * (maxOffset - minOffset);
        }

        public static void GetOffsetSamples(
            WallGapController wall,
            int sampleCount,
            System.Random rng,
            List<float> output,
            GapOffsetPolicy policy = default)
        {
            if (output == null)
                return;

            output.Clear();
            if (wall == null)
                return;

            if (!wall.TryGetGapOffsetRange(policy, out float minOffset, out float maxOffset, out _))
            {
                output.Add(GetWallCenterGapOffset(wall, policy));
                return;
            }

            sampleCount = Mathf.Max(1, sampleCount);
            if (sampleCount == 1 || Mathf.Abs(maxOffset - minOffset) < 0.0001f)
            {
                output.Add((minOffset + maxOffset) * 0.5f);
                return;
            }

            if (policy.randomGapOffset && rng != null)
                AddSample(output, GetRandomGapOffset(wall, rng, policy));

            float center = (minOffset + maxOffset) * 0.5f;
            float span = maxOffset - minOffset;
            AddSample(output, center);
            AddSample(output, minOffset + span * 0.25f);
            AddSample(output, minOffset + span * 0.75f);
            AddSample(output, minOffset + span * 0.1f);
            AddSample(output, minOffset + span * 0.9f);

            for (int i = 0; output.Count < sampleCount && i < sampleCount * 3; i++)
            {
                float t = sampleCount > 1 ? i / (float)(sampleCount - 1) : 0f;
                AddSample(output, Mathf.Lerp(minOffset, maxOffset, t));
            }

            while (output.Count > sampleCount)
                output.RemoveAt(output.Count - 1);

            if (rng != null)
                Shuffle(output, rng);
        }

        public static void GetCenterOffsetSample(
            WallGapController wall,
            List<float> output,
            GapOffsetPolicy policy = default)
        {
            if (output == null)
                return;

            output.Clear();
            output.Add(GetWallCenterGapOffset(wall, policy));
        }

        private static void Shuffle(List<float> values, System.Random rng)
        {
            for (int i = values.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (values[i], values[j]) = (values[j], values[i]);
            }
        }

        private static void AddSample(List<float> samples, float candidate)
        {
            foreach (float existing in samples)
            {
                if (Mathf.Abs(existing - candidate) < 0.001f)
                    return;
            }

            samples.Add(candidate);
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

            if (_openingMode == WallOpeningMode.OpenEnd)
            {
                ApplyOpenEndOpening();
                return;
            }

            if (wallLeft == null || wallRight == null)
                return;

            EnsureWallPieceCollision(wallLeft);
            EnsureWallPieceCollision(wallRight);
            EnsureBaseline();

            if (_wallLengthM < 0.1f)
                return;

            if (!hasOpening)
            {
                HideSocket();
                SetWallPiecesVisible(true);
                RestoreClosed();
                return;
            }

            if (_openingMode == WallOpeningMode.FullWall)
            {
                ApplyFullWallOpening();
                return;
            }

            SetWallPiecesVisible(true);

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

            // True gap center from seam + axis, not from wallLeft/wallRight.position — those pieces may be
            // cosmetically recessed (see _recessDepthM), and the socket used to snap the connecting room
            // into place must stay at the real doorway location, or the recess would drag that room's
            // placement back with it and cancel itself out.
            Vector3 trueGapCenter = _seamWorld + _axisWorld * ((gapLeftT + gapRightT) * 0.5f);
            UpdateSocketFromLiveGap(effectiveGapWidth, trueGapCenter);
            TrySpawnFrame(effectiveGapWidth);
        }

        private void ApplyFullWallOpening()
        {
            RestoreClosed();
            SetWallPiecesVisible(false);

            float width = EffectiveOpeningWidth;
            Vector3 center = _seamWorld;
            center.y = GetWallFloorY();
            Vector3 outward = ComputeOutwardForward(center);
            EnsureSocket();
            _socket.Bind(this, width);
            _socket.AlignAt(center, outward, width);
            // No door frames on full-wall openings.
        }

        private void ApplyOpenEndOpening()
        {
            EnsureBaselineIfPossible();

            if (!hasOpening)
            {
                HideSocket();
                SetWallPiecesVisible(true);
                RestoreClosed();
                return;
            }

            // Portal: hide any leftover end pieces if present.
            if (wallLeft != null || wallRight != null)
            {
                RestoreClosed();
                SetWallPiecesVisible(false);
            }

            float width = EffectiveOpeningWidth;
            Vector3 center = _baselineCached ? _seamWorld : transform.position;
            center.y = GetWallFloorY();
            Vector3 outward = ComputeOutwardForward(center);
            EnsureSocket();
            _socket.Bind(this, width);
            _socket.AlignAt(center, outward, width);
        }

        private void SetWallPiecesVisible(bool visible)
        {
            SetPieceActive(wallLeft, visible);
            SetPieceActive(wallRight, visible);
        }

        private static void SetPieceActive(Transform piece, bool visible)
        {
            if (piece == null)
                return;

            // Keep the transform hierarchy; toggle renderers + colliders so rollback is cheap.
            foreach (Renderer renderer in piece.GetComponentsInChildren<Renderer>(true))
                renderer.enabled = visible;

            foreach (Collider col in piece.GetComponentsInChildren<Collider>(true))
                col.enabled = visible;
        }

        private void EnsureBaselineIfPossible()
        {
            AutoFindChildren();
            if (!_baselineCached)
                RebuildBaseline();
        }

        /// <summary>Gap center passed in by the caller — the true (un-recessed) seam position, not wallLeft/wallRight.position.</summary>
        private void UpdateSocketFromLiveGap(float openingWidth, Vector3 center)
        {
            center.y = GetWallFloorY();

            Vector3 outward = ComputeOutwardForward(center);
            EnsureSocket();
            _socket.Bind(this, openingWidth);
            _socket.AlignAt(center, outward, openingWidth);
        }

        private void ResolveSocketReference()
        {
            if (_socket != null)
                return;

            Transform roomRoot = GetComponentInParent<RoomInstance>()?.transform ?? transform.root;
            Transform existing = roomRoot.Find("Socket_" + name);
            if (existing != null)
                _socket = existing.GetComponent<RoomSocket>();
        }

        private void EnsureSocket()
        {
            ResolveSocketReference();
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

            // Prefer geometric wall normal for stable direction on irregular room shapes.
            if (_baselineCached)
            {
                Vector3 wallNormal = Vector3.Cross(Vector3.up, _axisWorld).normalized;
                if (wallNormal.sqrMagnitude > 0.001f)
                {
                    Vector3 toGap = gapCenter - origin;
                    toGap.y = 0f;
                    if (toGap.sqrMagnitude > 0.001f && Vector3.Dot(wallNormal, toGap) < 0f)
                        wallNormal = -wallNormal;

                    return wallNormal;
                }
            }

            Vector3 outward = gapCenter - origin;
            outward.y = 0f;
            if (outward.sqrMagnitude > 0.001f)
                return outward.normalized;

            Vector3 fallback = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            return fallback.sqrMagnitude > 0.001f ? fallback : Vector3.forward;
        }

        private bool TryComputeGapMetrics(bool useRandomOffset, out float gapOffsetM, out float effectiveGapWidth)
        {
            gapOffsetM = 0f;
            effectiveGapWidth = 0f;

            if (_wallLengthM < 0.1f)
                return false;

            if (UsesFullOpening)
            {
                effectiveGapWidth = EffectiveOpeningWidth;
                float maxD = Mathf.Max(0f, _wallLengthM - effectiveGapWidth);
                gapOffsetM = maxD * 0.5f;
                return effectiveGapWidth >= 0.05f;
            }

            effectiveGapWidth = Mathf.Min(gapWidth, _wallLengthM - 0.05f);
            if (effectiveGapWidth < 0.05f)
                return false;

            float maxOffset = Mathf.Max(0f, _wallLengthM - effectiveGapWidth);
            gapOffsetM = useRandomOffset
                ? Random.Range(0f, maxOffset)
                : Mathf.Clamp(gapOffset, 0f, maxOffset);

            return true;
        }

        private void TrySpawnFrame(float effectiveGapWidth)
        {
            if (UsesFullOpening)
                return;

            if (_framePrefab == null || !ShouldSpawnFrame())
                return;

            if (wallLeft == null || wallRight == null)
                return;

            Vector3 center = (wallLeft.position + wallRight.position) * 0.5f;
            center.y = GetWallFloorY();
            Vector3 forward = _socket != null ? _socket.transform.forward : ComputeOutwardForward(center);
            forward = Vector3.ProjectOnPlane(forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.0001f)
                forward = Vector3.forward;

            Quaternion rot = Quaternion.LookRotation(forward, Vector3.up) * Quaternion.Euler(0f, FrameYawDegrees, 0f);

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
            // Keep door sockets on the walkable floor plane so mixed prefab pivots
            // do not produce vertically mismatched openings after floor alignment.
            RoomInstance room = GetComponentInParent<RoomInstance>();
            if (room != null)
                return room.GetWalkableFloorY();

            Renderer leftR = wallLeft != null ? wallLeft.GetComponentInChildren<Renderer>() : null;
            Renderer rightR = wallRight != null ? wallRight.GetComponentInChildren<Renderer>() : null;
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

            if (_openingMode == WallOpeningMode.OpenEnd && (wallLeft == null || wallRight == null))
            {
                SetWallPiecesVisible(true);
                _seamWorld = transform.position;
                _axisWorld = Vector3.ProjectOnPlane(transform.right, Vector3.up).normalized;
                if (_axisWorld.sqrMagnitude < 0.0001f)
                    _axisWorld = Vector3.right;
                _wallLengthM = Mathf.Max(gapWidth, 1.3f);
                _leftExtentM = _wallLengthM * 0.5f;
                _rightExtentM = _wallLengthM * 0.5f;
                _baselineCached = true;
                return;
            }

            RestoreClosed();
            SetWallPiecesVisible(true);
            _baselineCached = CacheFromClosedMesh();
        }

        private void RestoreClosed()
        {
            if (wallLeft != null)
            {
                wallLeft.localPosition = Vector3.zero;
                wallLeft.localScale = Vector3.one;
                EnsureWallPieceCollision(wallLeft);
            }

            if (wallRight != null)
            {
                wallRight.localPosition = Vector3.zero;
                wallRight.localScale = Vector3.one;
                EnsureWallPieceCollision(wallRight);
            }
        }

        private void EnsureWallPieceCollision(Transform piece)
        {
            Collider[] colliders = piece.GetComponentsInChildren<Collider>(true);
            if (colliders.Length > 0)
            {
                foreach (Collider col in colliders)
                {
                    col.enabled = true;
                    col.isTrigger = false;
                }

                return;
            }

            Renderer[] renderers = piece.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
                return;

            BoxCollider box = piece.GetComponent<BoxCollider>();
            if (box == null)
                box = piece.gameObject.AddComponent<BoxCollider>();

            Bounds localBounds = BuildLocalBounds(piece, renderers);
            box.center = localBounds.center;
            box.size = localBounds.size;
            box.isTrigger = false;
            box.enabled = true;
        }

        private static Bounds BuildLocalBounds(Transform root, Renderer[] renderers)
        {
            bool hasPoint = false;
            Vector3 min = Vector3.zero;
            Vector3 max = Vector3.zero;

            foreach (Renderer renderer in renderers)
            {
                Bounds bounds = renderer.bounds;
                Vector3 ext = bounds.extents;

                for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                for (int z = -1; z <= 1; z += 2)
                {
                    Vector3 world = bounds.center + Vector3.Scale(ext, new Vector3(x, y, z));
                    Vector3 local = root.InverseTransformPoint(world);

                    if (!hasPoint)
                    {
                        min = local;
                        max = local;
                        hasPoint = true;
                    }
                    else
                    {
                        min = Vector3.Min(min, local);
                        max = Vector3.Max(max, local);
                    }
                }
            }

            Vector3 size = hasPoint ? Vector3.Max(max - min, Vector3.one * 0.01f) : Vector3.one;
            Vector3 center = hasPoint ? (min + max) * 0.5f : Vector3.zero;
            return new Bounds(center, size);
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
            if (Mathf.Abs(_recessDepthM) > 0.0001f)
                worldPos -= ComputeOutwardForward(worldPos) * _recessDepthM;
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
