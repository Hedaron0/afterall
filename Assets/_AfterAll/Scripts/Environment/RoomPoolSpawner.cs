using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System;
using System.Text;
using AfterAll.Player;
using UnityEngine;

namespace AfterAll.Environment
{
    /// <summary>
    /// Press Play: builds a chain of connected rooms from the prefab pool.
    /// Inspector: assign Room Prefabs + Room Count. Nothing else.
    /// </summary>
    public class RoomPoolSpawner : MonoBehaviour
    {
        private struct OpeningWorkItem
        {
            public RoomInstance Room;
            public WallGapController Wall;
            public bool SpawnFrame;
            public int TotalAttempts;
        }

        private enum BuildExitReason
        {
            TargetReached,
            FrontierEmpty,
            GlobalBudgetExhausted,
            OpeningsExhausted
        }

        private enum UnreachableCause
        {
            ZeroConnections,
            IsolatedSubGraph
        }

        private struct ReachabilityAuditResult
        {
            public int totalPlaced;
            public int reachableCount;
            public int unreachableCount;
            public int zeroConnectionCount;
            public int isolatedSubGraphCount;
            public int retriedCount;
            public int salvagedCount;
            public int destroyedCount;
            public List<string> actionLines;
        }

        private struct UnreachableComponent
        {
            public List<RoomInstance> rooms;
            public UnreachableCause cause;
        }

        [SerializeField] private RoomConnector _connector;
        [SerializeField] private GameObject[] _roomPrefabs = System.Array.Empty<GameObject>();
        [SerializeField] private int _roomCount = 5;
        [Header("Connection Rules")]
        [Tooltip("Reserved for future gameplay doors — does not affect proc-gen gap FrameDoor spawn.")]
        [SerializeField] private bool _forceDoorsOnConnections;
        [Tooltip("Reserved for future gameplay doors — does not affect proc-gen gap FrameDoor spawn.")]
        [SerializeField, Range(0f, 1f)] private float _doorChance = 0.35f;
        [SerializeField] private bool _forceFramesOnConnections;
        [SerializeField, Range(0f, 1f)] private float _frameChance = 0.35f;
        [SerializeField, Range(0f, 1f)] private float _extraOpeningChance = 0.5f;
        [SerializeField, Min(1)] private int _minOpeningsPerRoom = 1;
        [SerializeField, Min(1)] private int _maxOpeningsPerRoom = 2;

        [Header("Graph Policy")]
        [SerializeField, Min(1)] private int _maxBranchDepth = 4;
        [SerializeField, Range(0f, 1f)] private float _deadEndRatio = 0.35f;
        [SerializeField, Range(0f, 0.25f)] private float _deadEndRatioTolerance = 0.05f;
        [SerializeField, Range(0f, 1f)] private float _deadEndBiasStrength = 0.6f;
        [SerializeField, Min(1)] private int _hubMinOpenings = 3;
        [SerializeField, Min(1)] private int _hubMaxOpenings = 4;

        [Header("Seed")]
        [SerializeField] private bool _useFixedSeed;
        [SerializeField] private int _fixedSeed = 12345;
        [SerializeField] private bool _randomizeSeedOnPlay = true;
        [SerializeField] private int _lastUsedSeed;

        [Header("Build Pace")]
        [SerializeField, Min(0f)] private float _spawnDelaySeconds = 0.05f;
        [SerializeField, Min(1)] private int _attemptsPerOpening = 6;
        [SerializeField, Min(1)] private int _maxRetryPasses = 3;
        [SerializeField, Min(1)] private int _maxGlobalConnectAttempts = 150;
        [Header("Offset Search")]
        [SerializeField] private bool _offsetSearchEnabled = true;
        [SerializeField, Min(1)] private int _offsetSamplesPerWall = 5;

        [Header("Gap Offset")]
        [SerializeField] private bool _randomGapOffset = true;
        [SerializeField, Min(0f)] private float _gapEdgeMarginM = 0.15f;
        [SerializeField, Range(0f, 1f)] private float _gapOffsetSpanFraction = 1f;

        [Header("Player Spawn")]
        [SerializeField] private Transform _player;
        [SerializeField] private float _playerSpawnHeight = 1.0f;
        [SerializeField] private bool _repositionPlayerAfterBuild = true;

        [Header("Reachability")]
        [SerializeField] private UnreachableRoomPolicy _unreachableRoomPolicy = UnreachableRoomPolicy.RetryThenDestroy;
        [SerializeField, Min(1)] private int _unreachableRetryAttempts = 1;

        private readonly Queue<OpeningWorkItem> _primaryQueue = new();
        private readonly Queue<OpeningWorkItem> _retryQueue = new();
        private readonly List<string> _deadOpenings = new();
        private readonly List<string> _policyDeadEnds = new();
        private readonly HashSet<RoomInstance> _policyDeadEndRoomsLogged = new();
        private int _placedRoomCount;
        private Coroutine _buildRoutine;
        private System.Random _rng;
        private static bool? _lastLoggedRandomGapOffset;

        private int MaxTotalAttemptsPerOpening => _maxRetryPasses * _attemptsPerOpening;

        private void Start()
        {
            HideHandPlacedRooms();
            Build();
        }

        public void Build()
        {
            if (_buildRoutine != null)
                StopCoroutine(_buildRoutine);

            _buildRoutine = StartCoroutine(BuildRoutine());
        }

        private IEnumerator BuildRoutine()
        {
            _primaryQueue.Clear();
            _retryQueue.Clear();
            _deadOpenings.Clear();
            _policyDeadEnds.Clear();
            _policyDeadEndRoomsLogged.Clear();
            _placedRoomCount = 0;

            if (_connector == null)
                _connector = GetComponent<RoomConnector>();

            if (_connector == null || _roomPrefabs.Length == 0)
            {
                Debug.LogError("[RoomPoolSpawner] Need RoomConnector + room prefabs.");
                _buildRoutine = null;
                yield break;
            }

            ClearLevelRoot();
            _connector.ResetStats();
            InitializeRng();
            _connector.ConfigureOffsetSearch(_offsetSearchEnabled, _offsetSamplesPerWall, _lastUsedSeed);
            _connector.ConfigureGapOffset(_randomGapOffset, _gapEdgeMarginM, _gapOffsetSpanFraction);
            Debug.Log(
                $"[RoomPoolSpawner] Seed={_lastUsedSeed}, Rooms={_roomCount}, " +
                $"FrameChance={_frameChance:F2}, ExtraOpeningChance={_extraOpeningChance:F2}, " +
                $"Openings={_minOpeningsPerRoom}-{_maxOpeningsPerRoom}, " +
                $"AttemptsPerVisit={_attemptsPerOpening}, MaxRetryPasses={_maxRetryPasses}, " +
                $"MaxAttemptsPerOpening={MaxTotalAttemptsPerOpening}, GlobalBudget={_maxGlobalConnectAttempts}, " +
                $"MaxBranchDepth={_maxBranchDepth}, DeadEndRatio={_deadEndRatio:F2}, HubOpenings={_hubMinOpenings}-{_hubMaxOpenings}, " +
                $"OffsetSearch={_offsetSearchEnabled}, Samples={_offsetSamplesPerWall}, " +
                $"GapOffset(Random={_randomGapOffset}, EdgeMargin={_gapEdgeMarginM:F2}m, SpanFraction={_gapOffsetSpanFraction:F2})");

            if (_maxBranchDepth < 3 && _roomCount > 6)
            {
                Debug.LogWarning(
                    $"[RoomPoolSpawner] maxBranchDepth={_maxBranchDepth} with roomCount={_roomCount} " +
                    $"and pool size={_roomPrefabs.Length} often under-fills — depth limits frontier before target is reached.");
            }

            GameObject firstPrefab = _roomPrefabs[NextInt(0, _roomPrefabs.Length)];
            GameObject firstGo = SpawnAtOrigin(firstPrefab);
            RoomInstance first = GetRoom(firstGo);
            first.SealAllWalls();
            RoomInstance.SocketValidationReport validationTotals = first.ValidateSocketContracts(logWarnings: true);

            List<WallGapController> walls = first.GetClosedWalls().ToList();
            if (walls.Count == 0)
            {
                Debug.LogError($"[RoomPoolSpawner] {firstPrefab.name} has no WallGapController walls.");
                _buildRoutine = null;
                yield break;
            }

            first.MarkAsHub();
            QueueOpeningsForRoom(first);

            int count = 1;
            _placedRoomCount = count;
            int passNumber = 1;
            int globalConnectAttempts = 0;
            bool globalBudgetExhausted = false;
            RoomInstance startRoom = first;

            while (count < _roomCount)
            {
                if (_primaryQueue.Count == 0)
                {
                    if (_retryQueue.Count == 0)
                        break;

                    PromoteRetryQueueToPrimary();
                    passNumber++;
                }

                if (globalConnectAttempts >= _maxGlobalConnectAttempts)
                {
                    globalBudgetExhausted = true;
                    break;
                }

                OpeningWorkItem item = _primaryQueue.Dequeue();
                if (item.Room == null || item.Wall == null || item.Room.IsWallConnected(item.Wall))
                    continue;

                (RoomInstance child, int attemptsUsed) = TryConnectWithRetries(item);
                globalConnectAttempts += attemptsUsed;
                item.TotalAttempts += attemptsUsed;

                if (child != null)
                {
                    count++;
                    _placedRoomCount = count;
                    child.SetGraphDepth(item.Room.GraphDepth + 1);
                    RoomInstance.SocketValidationReport childValidation = child.ValidateSocketContracts(logWarnings: true);
                    validationTotals.missingContractCount += childValidation.missingContractCount;
                    validationTotals.duplicateDirectionCount += childValidation.duplicateDirectionCount;
                    QueueOpeningsForRoom(child);

                    if (_spawnDelaySeconds > 0f)
                        yield return new WaitForSeconds(_spawnDelaySeconds);
                    else
                        yield return null;

                    continue;
                }

                if (item.TotalAttempts >= MaxTotalAttemptsPerOpening)
                    _deadOpenings.Add(FormatDeadOpening(item));
                else
                    _retryQueue.Enqueue(item);

                if (_spawnDelaySeconds > 0f)
                    yield return new WaitForSeconds(_spawnDelaySeconds);
                else
                    yield return null;
            }

            BuildExitReason exitReason = DetermineExitReason(count, globalBudgetExhausted);
            RoomConnector.ConnectionStats stats = _connector.GetStats();
            (ReachabilityAuditResult reachability, int finalPlacedCount) = RunReachabilityAudit(startRoom);
            int postBuildOverlaps = ValidatePlacedRoomOverlaps();
            if (_repositionPlayerAfterBuild)
                PlacePlayerAfterBuild(startRoom);

            LogBuildSummary(
                finalPlacedCount,
                passNumber,
                globalConnectAttempts,
                exitReason,
                stats,
                validationTotals,
                postBuildOverlaps,
                reachability);

            _buildRoutine = null;
        }

        private void PromoteRetryQueueToPrimary()
        {
            while (_retryQueue.Count > 0)
                _primaryQueue.Enqueue(_retryQueue.Dequeue());
        }

        private BuildExitReason DetermineExitReason(int placedCount, bool globalBudgetExhausted)
        {
            if (placedCount >= _roomCount)
                return BuildExitReason.TargetReached;

            if (globalBudgetExhausted)
                return BuildExitReason.GlobalBudgetExhausted;

            if (_deadOpenings.Count > 0)
                return BuildExitReason.OpeningsExhausted;

            return BuildExitReason.FrontierEmpty;
        }

        private void LogBuildSummary(
            int placedCount,
            int passNumber,
            int globalConnectAttempts,
            BuildExitReason exitReason,
            RoomConnector.ConnectionStats stats,
            RoomInstance.SocketValidationReport validationTotals,
            int postBuildOverlaps,
            ReachabilityAuditResult reachability)
        {
            var summary = new StringBuilder();
            summary.AppendLine(
                $"[RoomPoolSpawner] Placed {placedCount}/{_roomCount} rooms. " +
                $"Seed={_lastUsedSeed}, Passes={passNumber}, GlobalAttempts={globalConnectAttempts}.");

            if (reachability.destroyedCount > 0)
            {
                summary.AppendLine(
                    "[RoomPoolSpawner] Reachability cleanup reduced final room count — " +
                    "deadEndRatio stats reflect post-cleanup layout.");
            }

            if (placedCount < _roomCount)
            {
                summary.AppendLine($"[RoomPoolSpawner] Build stopped early: {exitReason}.");
                switch (exitReason)
                {
                    case BuildExitReason.FrontierEmpty:
                        summary.AppendLine(
                            "[RoomPoolSpawner] Spatial: primary and retry queues are empty — no viable openings left.");
                        break;
                    case BuildExitReason.GlobalBudgetExhausted:
                        summary.AppendLine(
                            $"[RoomPoolSpawner] Global connect attempt budget exhausted " +
                            $"(limit={_maxGlobalConnectAttempts}).");
                        break;
                    case BuildExitReason.OpeningsExhausted:
                        summary.AppendLine(
                            $"[RoomPoolSpawner] Retry: {_deadOpenings.Count} opening(s) exceeded max attempts " +
                            $"(limit={MaxTotalAttemptsPerOpening}):");
                        foreach (string dead in _deadOpenings)
                            summary.AppendLine($"  - {dead}");
                        break;
                }

                if (_deadOpenings.Count > 0 && exitReason != BuildExitReason.OpeningsExhausted)
                {
                    summary.AppendLine(
                        $"[RoomPoolSpawner] Also had {_deadOpenings.Count} retry-dead opening(s) before exit.");
                }
            }

            AppendGraphPolicySummary(summary, placedCount);
            AppendReachabilitySummary(summary, reachability);
            AppendGapOffsetSummary(summary, stats);

            summary.Append(
                $"[RoomPoolSpawner] Rejects(NoCompatible={stats.noCompatibleSocket}, Gap={stats.gapMismatch}, " +
                $"Overlap={stats.overlapRejected}), " +
                $"OffsetSearch(Retried={stats.offsetRetried}, SolvedOverlap={stats.offsetSolvedOverlap}, " +
                $"Exhausted={stats.offsetSearchExhausted}), FallbackUsed={stats.fallbackUsed}, " +
                $"Contracts(Missing={validationTotals.missingContractCount}, " +
                $"DuplicateDir={validationTotals.duplicateDirectionCount}), PostBuildOverlaps={postBuildOverlaps}.");

            Debug.Log(summary.ToString());
        }

        private void AppendGraphPolicySummary(StringBuilder summary, int placedCount)
        {
            int deadEnds = CountDeadEnds();
            int junctions = CountJunctions();
            float finalRatio = placedCount > 0 ? (float)deadEnds / placedCount : 0f;

            summary.AppendLine(
                $"[RoomPoolSpawner] GraphPolicy: DeadEnds={deadEnds}/{placedCount} " +
                $"({finalRatio:P0}, target {_deadEndRatio:P0}), Junctions={junctions}, " +
                $"PolicyDeadEnds={_policyDeadEnds.Count}, RetryDeadOpenings={_deadOpenings.Count}, " +
                $"MaxDepth={_maxBranchDepth}, GrowthPhase={placedCount < _roomCount}.");

            RoomInstance hub = FindHubRoom();
            if (hub != null)
            {
                summary.AppendLine(
                    $"[RoomPoolSpawner] Hub={hub.name}, depth={hub.GraphDepth}, " +
                    $"connections={hub.ConnectedRooms.Count}.");
            }

            if (_policyDeadEnds.Count > 0)
            {
                summary.AppendLine($"[RoomPoolSpawner] Policy dead-ends ({_policyDeadEnds.Count}):");
                foreach (string entry in _policyDeadEnds)
                    summary.AppendLine($"  - {entry}");
            }
        }

        private RoomInstance FindHubRoom()
        {
            foreach (RoomInstance room in _connector.LevelRoot.GetComponentsInChildren<RoomInstance>())
            {
                if (room != null && room.IsHub)
                    return room;
            }

            return null;
        }

        private int CountDeadEnds()
        {
            int dead = 0;
            foreach (RoomInstance room in _connector.LevelRoot.GetComponentsInChildren<RoomInstance>())
            {
                if (room != null && room.IsDeadEnd())
                    dead++;
            }

            return dead;
        }

        private int CountJunctions()
        {
            int junctions = 0;
            foreach (RoomInstance room in _connector.LevelRoot.GetComponentsInChildren<RoomInstance>())
            {
                if (room != null && room.IsJunction())
                    junctions++;
            }

            return junctions;
        }

        private void RecordPolicyDeadEnd(RoomInstance room, WallGapController wall, string reason)
        {
            if (room == null)
                return;

            if (!_policyDeadEndRoomsLogged.Add(room))
                return;

            string roomName = room.name;
            string wallName = wall != null ? wall.name : "all";
            _policyDeadEnds.Add($"{roomName}/{wallName} ({reason})");
        }

        private float ComputeEffectiveExtraChance(int placedCount)
        {
            if (placedCount <= 0)
                return _extraOpeningChance;

            int eligibleCount = CountBranchEligibleRooms();
            if (eligibleCount <= 0)
                return _extraOpeningChance;

            int deadEnds = CountDeadEndsAmongBranchEligible();
            float currentRatio = (float)deadEnds / eligibleCount;
            float ratioError = _deadEndRatio - currentRatio;
            return Mathf.Clamp(_extraOpeningChance + ratioError * _deadEndBiasStrength, 0f, 1f);
        }

        private int CountBranchEligibleRooms()
        {
            int count = 0;
            foreach (RoomInstance room in _connector.LevelRoot.GetComponentsInChildren<RoomInstance>())
            {
                if (room != null && room.GraphDepth >= 0 && room.GraphDepth < _maxBranchDepth)
                    count++;
            }

            return count;
        }

        private int CountDeadEndsAmongBranchEligible()
        {
            int dead = 0;
            foreach (RoomInstance room in _connector.LevelRoot.GetComponentsInChildren<RoomInstance>())
            {
                if (room == null || room.GraphDepth < 0 || room.GraphDepth >= _maxBranchDepth)
                    continue;

                if (room.IsDeadEnd())
                    dead++;
            }

            return dead;
        }

        private float GetCurrentDeadEndRatioForPolicy()
        {
            int eligibleCount = CountBranchEligibleRooms();
            if (eligibleCount <= 0)
                return 0f;

            return (float)CountDeadEndsAmongBranchEligible() / eligibleCount;
        }

        private static string FormatDeadOpening(OpeningWorkItem item)
        {
            string roomName = item.Room != null ? item.Room.name : "null";
            string wallName = item.Wall != null ? item.Wall.name : "null";
            return $"{roomName}/{wallName} ({item.TotalAttempts} attempts)";
        }

        private int ValidatePlacedRoomOverlaps()
        {
            RoomInstance[] rooms = _connector.LevelRoot.GetComponentsInChildren<RoomInstance>();
            int overlapPairs = 0;

            for (int i = 0; i < rooms.Length; i++)
            {
                for (int j = i + 1; j < rooms.Length; j++)
                {
                    RoomInstance a = rooms[i];
                    RoomInstance b = rooms[j];
                    if (a == null || b == null)
                        continue;

                    if (AreDirectlyConnected(a, b))
                        continue;

                    if (!RoomConnector.RoomsOverlapForPlacement(a, b, false, null, null))
                        continue;

                    overlapPairs++;
                    float area = RoomConnector.ComputeXZOverlapArea(
                        a.GetFloorFootprintBounds(),
                        b.GetFloorFootprintBounds());
                    Debug.LogWarning(
                        $"[RoomPoolSpawner] Post-build floor overlap: {a.name} <-> {b.name} (area={area:F2}m2)");
                }
            }

            return overlapPairs;
        }

        private (ReachabilityAuditResult result, int finalPlacedCount) RunReachabilityAudit(RoomInstance startRoom)
        {
            RoomInstance spawnRoot = PickSpawnRoom(startRoom);
            if (spawnRoot == null)
            {
                Debug.LogError("[RoomPoolSpawner] Reachability hard failure: spawn root is null.");
                int placed = CountPlacedRooms();
                return (CreateEmptyReachabilityResult(placed), placed);
            }

            ReachabilityAuditResult initialAudit = AuditReachability(spawnRoot);
            ReachabilityAuditResult audit = ApplyUnreachablePolicy(startRoom, initialAudit);

            spawnRoot = PickSpawnRoom(startRoom);
            if (spawnRoot == null)
            {
                Debug.LogError(
                    "[RoomPoolSpawner] Reachability hard failure: spawn root is null after policy pass.");
                return (audit, audit.totalPlaced);
            }

            HashSet<RoomInstance> finalReachable = CollectReachableRooms(spawnRoot);
            if (!finalReachable.Contains(spawnRoot))
            {
                Debug.LogError(
                    $"[RoomPoolSpawner] Reachability hard failure: spawn room '{spawnRoot.name}' " +
                    "is not in the reachable set after policy pass.");
            }

            return (audit, audit.totalPlaced);
        }

        private static ReachabilityAuditResult CreateEmptyReachabilityResult(int placed)
        {
            return new ReachabilityAuditResult
            {
                totalPlaced = placed,
                reachableCount = placed,
                actionLines = new List<string>()
            };
        }

        private ReachabilityAuditResult AuditReachability(RoomInstance spawnRoot)
        {
            RoomInstance[] allPlaced = _connector.LevelRoot.GetComponentsInChildren<RoomInstance>();
            HashSet<RoomInstance> reachable = CollectReachableRooms(spawnRoot);
            var unreachable = new List<RoomInstance>();

            foreach (RoomInstance room in allPlaced)
            {
                if (room != null && !reachable.Contains(room))
                    unreachable.Add(room);
            }

            var unreachableSet = new HashSet<RoomInstance>(unreachable);
            List<UnreachableComponent> components = FindUnreachableComponents(unreachable, unreachableSet);

            int zeroConnections = 0;
            int isolatedSubGraph = 0;
            foreach (UnreachableComponent component in components)
            {
                if (component.cause == UnreachableCause.ZeroConnections)
                    zeroConnections += component.rooms.Count;
                else
                    isolatedSubGraph += component.rooms.Count;
            }

            return new ReachabilityAuditResult
            {
                totalPlaced = allPlaced.Length,
                reachableCount = reachable.Count,
                unreachableCount = unreachable.Count,
                zeroConnectionCount = zeroConnections,
                isolatedSubGraphCount = isolatedSubGraph,
                actionLines = new List<string>()
            };
        }

        private ReachabilityAuditResult ApplyUnreachablePolicy(
            RoomInstance startRoom,
            ReachabilityAuditResult initialAudit)
        {
            if (initialAudit.unreachableCount == 0)
            {
                initialAudit.actionLines = new List<string>();
                return initialAudit;
            }

            if (_unreachableRoomPolicy == UnreachableRoomPolicy.LogOnly)
                return ApplyLogOnlyPolicy(startRoom, initialAudit);

            int retriedCount = 0;
            int salvagedCount = 0;
            int destroyedCount = 0;
            var actionLines = new List<string>();

            while (true)
            {
                RoomInstance spawnRoot = PickSpawnRoom(startRoom);
                ReachabilityAuditResult current = AuditReachability(spawnRoot);
                if (current.unreachableCount == 0)
                    break;

                List<UnreachableComponent> components = GetUnreachableComponents(spawnRoot);
                if (components.Count == 0)
                    break;

                UnreachableComponent component = components[0];
                string componentLabel = FormatComponentLabel(component);
                string causeLabel = component.cause == UnreachableCause.ZeroConnections
                    ? "ZeroConnections"
                    : "IsolatedSubGraph";

                bool salvaged = false;
                if (_unreachableRoomPolicy == UnreachableRoomPolicy.RetryThenDestroy)
                {
                    RoomInstance representative = PickComponentRepresentative(component.rooms);
                    salvaged = TrySalvageComponent(representative, ref retriedCount, ref salvagedCount);
                }

                if (salvaged)
                {
                    actionLines.Add(
                        $"- {componentLabel} ({causeLabel}, size={component.rooms.Count}) -> retry succeeded -> salvaged");
                    continue;
                }

                if (_unreachableRoomPolicy == UnreachableRoomPolicy.RetryThenDestroy)
                {
                    actionLines.Add(
                        $"- {componentLabel} ({causeLabel}, size={component.rooms.Count}) -> retry failed -> destroyed");
                }
                else
                {
                    actionLines.Add(
                        $"- {componentLabel} ({causeLabel}, size={component.rooms.Count}) -> destroyed");
                }

                destroyedCount += DestroyUnreachableComponent(component.rooms);
            }

            RoomInstance finalSpawnRoot = PickSpawnRoom(startRoom);
            ReachabilityAuditResult finalAudit = AuditReachability(finalSpawnRoot);
            finalAudit.retriedCount = retriedCount;
            finalAudit.salvagedCount = salvagedCount;
            finalAudit.destroyedCount = destroyedCount;
            finalAudit.actionLines = actionLines;
            return finalAudit;
        }

        private ReachabilityAuditResult ApplyLogOnlyPolicy(
            RoomInstance startRoom,
            ReachabilityAuditResult initialAudit)
        {
            var actionLines = new List<string>();
            List<UnreachableComponent> components = GetUnreachableComponents(PickSpawnRoom(startRoom));

            foreach (UnreachableComponent component in components)
            {
                string causeLabel = component.cause == UnreachableCause.ZeroConnections
                    ? "ZeroConnections"
                    : "IsolatedSubGraph";

                foreach (RoomInstance room in component.rooms)
                {
                    actionLines.Add(
                        $"- {room.name} ({causeLabel}, size={component.rooms.Count}) -> logged only");
                    Debug.LogWarning(
                        $"[RoomPoolSpawner] Unreachable room: {room.name} ({causeLabel}, " +
                        $"component size={component.rooms.Count}).");
                }
            }

            initialAudit.actionLines = actionLines;
            return initialAudit;
        }

        private List<UnreachableComponent> GetUnreachableComponents(RoomInstance spawnRoot)
        {
            HashSet<RoomInstance> reachable = CollectReachableRooms(spawnRoot);
            var unreachable = new List<RoomInstance>();

            foreach (RoomInstance room in _connector.LevelRoot.GetComponentsInChildren<RoomInstance>())
            {
                if (room != null && !reachable.Contains(room))
                    unreachable.Add(room);
            }

            return FindUnreachableComponents(unreachable, new HashSet<RoomInstance>(unreachable));
        }

        private bool TrySalvageComponent(
            RoomInstance representative,
            ref int retriedCount,
            ref int salvagedCount)
        {
            if (representative == null)
                return false;

            List<(RoomInstance room, WallGapController wall)> candidates = CollectLinkCandidates();
            Shuffle(candidates);

            int attempts = 0;
            foreach ((RoomInstance parent, WallGapController wall) in candidates)
            {
                if (attempts >= _unreachableRetryAttempts)
                    break;

                retriedCount++;
                attempts++;

                if (_connector.TryLinkExistingRoom(parent, wall, representative, ShouldSpawnFrame()))
                {
                    salvagedCount++;
                    return true;
                }
            }

            return false;
        }

        private List<(RoomInstance room, WallGapController wall)> CollectLinkCandidates()
        {
            var candidates = new List<(RoomInstance, WallGapController)>();
            RoomInstance spawnRoot = PickSpawnRoom(FindHubRoom());
            HashSet<RoomInstance> reachable = CollectReachableRooms(spawnRoot);

            foreach (RoomInstance room in reachable)
            {
                if (room == null)
                    continue;

                foreach (WallGapController wall in room.GetClosedWalls())
                {
                    if (!room.IsWallConnected(wall))
                        candidates.Add((room, wall));
                }
            }

            return candidates;
        }

        private int DestroyUnreachableComponent(List<RoomInstance> rooms)
        {
            int destroyed = 0;
            foreach (RoomInstance room in rooms.ToList())
            {
                if (room == null)
                    continue;

                DestroyUnreachableRoom(room);
                destroyed++;
            }

            return destroyed;
        }

        private void DestroyUnreachableRoom(RoomInstance room)
        {
            if (room == null)
                return;

            List<RoomInstance> neighbors = room.ConnectedRooms.ToList();
            foreach (RoomInstance neighbor in neighbors)
                neighbor.UnlinkNeighbor(room);

            Destroy(room.gameObject);
        }

        private static RoomInstance PickComponentRepresentative(List<RoomInstance> rooms)
        {
            RoomInstance best = null;
            int bestConnections = int.MaxValue;

            foreach (RoomInstance room in rooms)
            {
                if (room == null)
                    continue;

                int connections = room.ConnectedRooms.Count;
                if (connections < bestConnections)
                {
                    bestConnections = connections;
                    best = room;
                }
            }

            return best ?? rooms.FirstOrDefault();
        }

        private static string FormatComponentLabel(UnreachableComponent component)
        {
            if (component.rooms == null || component.rooms.Count == 0)
                return "empty";

            return component.rooms[0].name;
        }

        private static List<UnreachableComponent> FindUnreachableComponents(
            List<RoomInstance> unreachable,
            HashSet<RoomInstance> unreachableSet)
        {
            var components = new List<UnreachableComponent>();
            var visited = new HashSet<RoomInstance>();

            foreach (RoomInstance start in unreachable)
            {
                if (start == null || visited.Contains(start))
                    continue;

                var componentRooms = new List<RoomInstance>();
                var queue = new Queue<RoomInstance>();
                queue.Enqueue(start);
                visited.Add(start);

                while (queue.Count > 0)
                {
                    RoomInstance current = queue.Dequeue();
                    componentRooms.Add(current);

                    foreach (RoomInstance neighbor in current.ConnectedRooms)
                    {
                        if (neighbor != null && unreachableSet.Contains(neighbor) && visited.Add(neighbor))
                            queue.Enqueue(neighbor);
                    }
                }

                UnreachableCause cause = ClassifyComponent(componentRooms);
                components.Add(new UnreachableComponent
                {
                    rooms = componentRooms,
                    cause = cause
                });
            }

            return components;
        }

        private static UnreachableCause ClassifyComponent(List<RoomInstance> rooms)
        {
            foreach (RoomInstance room in rooms)
            {
                if (room != null && room.ConnectedRooms.Count == 0)
                    return UnreachableCause.ZeroConnections;
            }

            return UnreachableCause.IsolatedSubGraph;
        }

        private static HashSet<RoomInstance> CollectReachableRooms(RoomInstance root)
        {
            var reachable = new HashSet<RoomInstance>();
            if (root == null)
                return reachable;

            var queue = new Queue<RoomInstance>();
            queue.Enqueue(root);
            reachable.Add(root);

            while (queue.Count > 0)
            {
                RoomInstance current = queue.Dequeue();
                foreach (RoomInstance neighbor in current.ConnectedRooms)
                {
                    if (neighbor != null && reachable.Add(neighbor))
                        queue.Enqueue(neighbor);
                }
            }

            return reachable;
        }

        private int CountPlacedRooms()
        {
            return _connector.LevelRoot.GetComponentsInChildren<RoomInstance>().Length;
        }

        private static readonly Dictionary<int, int> CenterOnlyRetryBaselineBySeed = new();

        private void AppendGapOffsetSummary(StringBuilder summary, RoomConnector.ConnectionStats stats)
        {
            summary.AppendLine(
                $"[RoomPoolSpawner] GapOffset: Random={_randomGapOffset}, " +
                $"EdgeMargin={_gapEdgeMarginM:F2}m, SpanFraction={_gapOffsetSpanFraction:F2}.");

            if (_lastLoggedRandomGapOffset.HasValue &&
                _lastLoggedRandomGapOffset.Value != _randomGapOffset)
            {
                summary.AppendLine(
                    "[RoomPoolSpawner] GapOffset mode changed — compare OffsetSearch Retried to prior run.");
            }

            _lastLoggedRandomGapOffset = _randomGapOffset;

            if (!_randomGapOffset)
            {
                CenterOnlyRetryBaselineBySeed[_lastUsedSeed] = stats.offsetRetried;
                return;
            }

            if (CenterOnlyRetryBaselineBySeed.TryGetValue(_lastUsedSeed, out int centerBaseline) &&
                centerBaseline > 0 &&
                stats.offsetRetried > centerBaseline * 1.5f)
            {
                Debug.LogWarning(
                    $"[RoomPoolSpawner] Random gap offset increased OffsetSearch Retried " +
                    $"({stats.offsetRetried} vs center-only baseline {centerBaseline} on seed {_lastUsedSeed}). " +
                    "Consider lowering _offsetSamplesPerWall, _gapOffsetSpanFraction, or scheduling a perf pass.");
            }
        }

        private void AppendReachabilitySummary(StringBuilder summary, ReachabilityAuditResult reachability)
        {
            summary.AppendLine(
                $"[RoomPoolSpawner] Reachability: Placed={reachability.totalPlaced}, " +
                $"Reachable={reachability.reachableCount}, Unreachable={reachability.unreachableCount}, " +
                $"ZeroConnections={reachability.zeroConnectionCount}, " +
                $"IsolatedSubGraph={reachability.isolatedSubGraphCount}, " +
                $"Policy={_unreachableRoomPolicy}, Retried={reachability.retriedCount}, " +
                $"Salvaged={reachability.salvagedCount}, Destroyed={reachability.destroyedCount}.");

            if (reachability.actionLines != null && reachability.actionLines.Count > 0)
            {
                foreach (string line in reachability.actionLines)
                    summary.AppendLine($"  {line}");
            }
        }

        private static bool AreDirectlyConnected(RoomInstance a, RoomInstance b)
        {
            foreach (RoomInstance neighbor in a.ConnectedRooms)
            {
                if (neighbor == b)
                    return true;
            }

            return false;
        }

        private RoomInstance PickSpawnRoom(RoomInstance startRoom)
        {
            RoomInstance[] rooms = _connector.LevelRoot.GetComponentsInChildren<RoomInstance>();
            if (rooms.Length == 0)
                return startRoom;

            RoomInstance best = startRoom;
            int bestConnections = startRoom != null ? startRoom.ConnectedRooms.Count : -1;

            foreach (RoomInstance room in rooms)
            {
                if (room == null)
                    continue;

                int connections = room.ConnectedRooms.Count;
                if (connections > bestConnections)
                {
                    bestConnections = connections;
                    best = room;
                }
            }

            return best != null ? best : startRoom;
        }

        private void PlacePlayerAfterBuild(RoomInstance startRoom)
        {
            Transform player = _player;
            if (player == null)
            {
                PlayerMovement movement = FindAnyObjectByType<PlayerMovement>();
                if (movement != null)
                    player = movement.transform;
            }

            if (player == null || startRoom == null)
                return;

            RoomInstance spawnRoom = PickSpawnRoom(startRoom);
            Vector3 spawnPosition = spawnRoom.GetSpawnPosition(_playerSpawnHeight);

            CharacterController controller = player.GetComponent<CharacterController>();
            if (controller != null)
                controller.enabled = false;

            player.SetPositionAndRotation(spawnPosition, Quaternion.Euler(0f, player.eulerAngles.y, 0f));

            if (controller != null)
                controller.enabled = true;

            Debug.Log(
                $"[RoomPoolSpawner] Player spawn -> {spawnRoom.name} " +
                $"(connections={spawnRoom.ConnectedRooms.Count}) at {spawnPosition}");
        }

        private (RoomInstance child, int attemptsUsed) TryConnectWithRetries(OpeningWorkItem item)
        {
            int maxTotal = MaxTotalAttemptsPerOpening;
            int remainingBudget = maxTotal - item.TotalAttempts;
            int visitLimit = Mathf.Min(_attemptsPerOpening, remainingBudget);
            int attemptsUsed = 0;

            for (int i = 0; i < visitLimit; i++)
            {
                GameObject prefab = _roomPrefabs[NextInt(0, _roomPrefabs.Length)];
                RoomInstance child = _connector.Connect(item.Room, item.Wall, prefab, item.SpawnFrame);
                attemptsUsed++;
                if (child != null)
                    return (child, attemptsUsed);
            }

            return (null, attemptsUsed);
        }

        private void QueueOpeningsForRoom(RoomInstance room)
        {
            if (room == null)
                return;

            if (room.GraphDepth >= 0 && room.GraphDepth >= _maxBranchDepth)
            {
                RecordPolicyDeadEnd(room, null, "depth cap");
                return;
            }

            List<WallGapController> closed = room.GetClosedWalls().ToList();
            if (closed.Count == 0)
                return;

            Shuffle(closed);

            int minOpenings = room.IsHub
                ? Mathf.Clamp(_hubMinOpenings, 1, closed.Count)
                : Mathf.Clamp(_minOpeningsPerRoom, 1, closed.Count);
            int maxOpenings = room.IsHub
                ? Mathf.Clamp(_hubMaxOpenings, minOpenings, closed.Count)
                : Mathf.Clamp(_maxOpeningsPerRoom, minOpenings, closed.Count);

            float currentRatio = GetCurrentDeadEndRatioForPolicy();
            bool growthPhase = _placedRoomCount < _roomCount;
            bool ratioHardCap = !growthPhase &&
                CountBranchEligibleRooms() > 0 &&
                currentRatio >= _deadEndRatio + _deadEndRatioTolerance;
            if (ratioHardCap)
            {
                maxOpenings = minOpenings;
                RecordPolicyDeadEnd(room, null, "ratio hard-cap");
            }

            float effectiveExtraChance = ComputeEffectiveExtraChance(_placedRoomCount);
            int targetOpenings = NextInt(minOpenings, maxOpenings + 1);
            int queued = 0;

            foreach (WallGapController wall in closed)
            {
                bool required = queued < minOpenings;
                bool reachedTarget = queued >= targetOpenings;
                if (!required && reachedTarget)
                    break;

                if (required || Chance(effectiveExtraChance))
                {
                    _primaryQueue.Enqueue(new OpeningWorkItem
                    {
                        Room = room,
                        Wall = wall,
                        SpawnFrame = ShouldSpawnFrame(),
                        TotalAttempts = 0
                    });
                    queued++;
                }
            }

            if (queued == 0)
            {
                _primaryQueue.Enqueue(new OpeningWorkItem
                {
                    Room = room,
                    Wall = closed[0],
                    SpawnFrame = ShouldSpawnFrame(),
                    TotalAttempts = 0
                });
            }
        }

        private bool ShouldSpawnFrame()
        {
            return _forceFramesOnConnections || Chance(_frameChance);
        }

        private void Shuffle<T>(IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = NextInt(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        private void InitializeRng()
        {
            if (_useFixedSeed)
            {
                _lastUsedSeed = _fixedSeed;
            }
            else if (_randomizeSeedOnPlay || _lastUsedSeed == 0)
            {
                _lastUsedSeed = System.Environment.TickCount ^ Guid.NewGuid().GetHashCode();
            }

            _rng = new System.Random(_lastUsedSeed);
        }

        private int NextInt(int minInclusive, int maxExclusive)
        {
            if (_rng == null)
                InitializeRng();

            return _rng.Next(minInclusive, maxExclusive);
        }

        private bool Chance(float probability)
        {
            if (_rng == null)
                InitializeRng();

            return _rng.NextDouble() <= probability;
        }

        private GameObject SpawnAtOrigin(GameObject prefab)
        {
            GameObject go = Instantiate(prefab, _connector.LevelRoot);
            go.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            return go;
        }

        private static RoomInstance GetRoom(GameObject go)
        {
            RoomInstance r = go.GetComponent<RoomInstance>() ?? go.AddComponent<RoomInstance>();
            r.CacheWalls();
            return r;
        }

        private void ClearLevelRoot()
        {
            Transform root = _connector.LevelRoot;
            for (int i = root.childCount - 1; i >= 0; i--)
                Destroy(root.GetChild(i).gameObject);
        }

        private void HideHandPlacedRooms()
        {
            Transform root = _connector != null ? _connector.LevelRoot : null;
            foreach (RoomInstance room in FindObjectsByType<RoomInstance>())
            {
                if (root != null && room.transform.IsChildOf(root))
                    continue;

                room.gameObject.SetActive(false);
            }
        }
    }
}
