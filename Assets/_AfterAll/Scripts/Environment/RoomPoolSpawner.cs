using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AfterAll.Player;
using UnityEngine;
using UnityEngine.Serialization;

namespace AfterAll.Environment
{
    /// <summary>
    /// Settlement-spine layout: plan in Layout Top View (or on Play), then apply sockets in-scene.
    /// Generation policy lives in SettlementSpinePlanner + Top View.
    /// </summary>
    public class RoomPoolSpawner : MonoBehaviour
    {
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
        [SerializeField] private RoomPrefabEntry[] _roomPrefabEntries = Array.Empty<RoomPrefabEntry>();
        [SerializeField, HideInInspector] private GameObject[] _roomPrefabs = Array.Empty<GameObject>();
        [SerializeField, Min(1)] private int _roomCount = 40;

        [Header("Settlement Spine")]
        [SerializeField, Min(1)] private int _settlementCount = 3;
        [SerializeField, Min(1)] private int _roomsPerSettlement = 5;
        [SerializeField, Min(1)] private int _corridorRoomsPerBridge = 2;
        [SerializeField, Min(0)] private int _stubBudget = 1;
        [SerializeField] private ExitBiasDirection _exitBias = ExitBiasDirection.East;
        [FormerlySerializedAs("_pathNetworkFootprints")]
        [SerializeField] private RoomFootprint[] _settlementFootprints = Array.Empty<RoomFootprint>();
        [FormerlySerializedAs("_pathNetworkRandomGapOffset")]
        [SerializeField] private bool _randomGapOffset = true;
        [SerializeField, Min(0f)] private float _gapEdgeMarginM = 0.15f;
        [SerializeField, Range(0f, 1f)] private float _gapOffsetSpanFraction = 1f;

        [Header("Door Frames")]
        [SerializeField] private bool _forceFramesOnConnections;
        [SerializeField, Range(0f, 1f)] private float _frameChance = 0.35f;

        [Header("Seed")]
        [SerializeField] private bool _useFixedSeed;
        [SerializeField] private int _fixedSeed = 12345;
        [SerializeField] private bool _randomizeSeedOnPlay = true;
        [SerializeField] private int _lastUsedSeed;

        [Header("Build Pace")]
        [SerializeField, Min(0f)] private float _spawnDelaySeconds = 0.05f;

        [Header("Player Spawn")]
        [SerializeField] private Transform _player;
        [SerializeField] private float _playerSpawnHeight = 1.0f;
        [SerializeField] private bool _repositionPlayerAfterBuild = true;

        [Header("Reachability")]
        [SerializeField] private UnreachableRoomPolicy _unreachableRoomPolicy = UnreachableRoomPolicy.RetryThenDestroy;
        [SerializeField, Min(1)] private int _unreachableRetryAttempts = 1;

        [SerializeField] private RoomContentManager _contentManager;

        private int _placedRoomCount;
        private Coroutine _buildRoutine;
        private System.Random _rng;
        private RoomPrefabEntry[] _activePrefabEntries = Array.Empty<RoomPrefabEntry>();

        public int LastUsedSeed => _lastUsedSeed;
        public int RoomCount => _roomCount;
        public int SettlementCount => _settlementCount;

        public void ConfigureSettlementSpineFromEditor(
            int seed,
            SettlementSpineConfig config,
            RoomFootprint[] footprints)
        {
            config.Clamp();
            _useFixedSeed = true;
            _fixedSeed = seed;
            _randomizeSeedOnPlay = false;
            _settlementCount = config.settlementCount;
            _roomsPerSettlement = config.roomsPerSettlement;
            _corridorRoomsPerBridge = config.corridorRoomsPerBridge;
            _stubBudget = config.stubBudget;
            _exitBias = config.exitBias;
            _randomGapOffset = config.randomGapOffset;
            _roomCount = Mathf.Max(1, config.EstimatedRoomCount);
            if (config.gapPolicy.edgeMarginM > 0f)
                _gapEdgeMarginM = config.gapPolicy.edgeMarginM;
            if (footprints != null && footprints.Length > 0)
                _settlementFootprints = footprints;
        }

        public void SetSettlementFootprints(RoomFootprint[] footprints)
        {
            if (footprints != null)
                _settlementFootprints = footprints;
        }

        public void SyncPrefabRolesFromFootprints(IReadOnlyList<RoomFootprint> footprints)
        {
            if (footprints == null || footprints.Count == 0 || _roomPrefabEntries == null)
                return;

            var roleByName = new Dictionary<string, RoomRole>();
            foreach (RoomFootprint footprint in footprints)
            {
                if (footprint?.Prefab == null)
                    continue;
                roleByName[footprint.Prefab.name] = footprint.ResolvedRole;
            }

            foreach (RoomPrefabEntry entry in _roomPrefabEntries)
            {
                if (entry?.Prefab == null)
                    continue;
                if (roleByName.TryGetValue(entry.Prefab.name, out RoomRole role))
                    entry.SetResolvedRoleFromFootprint(role);
            }
        }

        private SettlementSpineConfig BuildConfig()
        {
            return new SettlementSpineConfig
            {
                settlementCount = _settlementCount,
                roomsPerSettlement = _roomsPerSettlement,
                corridorRoomsPerBridge = _corridorRoomsPerBridge,
                stubBudget = _stubBudget,
                exitBias = _exitBias,
                randomGapOffset = _randomGapOffset,
                gapPolicy = new GapOffsetPolicy
                {
                    randomGapOffset = _randomGapOffset,
                    edgeMarginM = _gapEdgeMarginM,
                    spanFraction = _gapOffsetSpanFraction
                }
            };
        }

        private void Awake()
        {
            if (_contentManager == null)
                _contentManager = GetComponent<RoomContentManager>();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            MigrateLegacyPrefabPool();
        }
#endif

        private void Start()
        {
            HideHandPlacedRooms();
            Build();
        }

        public void Build()
        {
            if (_buildRoutine != null)
                StopCoroutine(_buildRoutine);

            _buildRoutine = StartCoroutine(BuildSettlementSpineRoutine());
        }

        private IEnumerator BuildSettlementSpineRoutine()
        {
            _placedRoomCount = 0;

            if (_connector == null)
                _connector = GetComponent<RoomConnector>();

            if (_connector == null || !TryPreparePrefabPool())
            {
                Debug.LogError("[RoomPoolSpawner] Need RoomConnector + at least one valid room prefab entry.");
                _buildRoutine = null;
                yield break;
            }

            List<RoomFootprint> library = ResolveSettlementLibrary();
            if (library.Count == 0)
            {
                Debug.LogError(
                    "[RoomPoolSpawner] Settlement Spine needs RoomFootprint assets. " +
                    "Run AfterAll → Generation → Bake Room Footprints, then assign them or use Layout Top View → Push Seed.");
                _buildRoutine = null;
                yield break;
            }

            ClearLevelRoot();
            _connector.ResetStats();
            InitializeRng();
            _connector.ConfigureOffsetSearch(false, 1, _lastUsedSeed);
            _connector.ConfigureGapOffset(_randomGapOffset, _gapEdgeMarginM, _gapOffsetSpanFraction);

            SettlementSpineConfig config = BuildConfig();
            LayoutPlan plan = SettlementSpinePlanner.Generate(library, _lastUsedSeed, config);
            _roomCount = Mathf.Max(_roomCount, plan.PlacedCount);

            Debug.Log(
                $"[RoomPoolSpawner] SettlementSpine Seed={_lastUsedSeed}, " +
                $"Settlements={_settlementCount}, Rooms/Settle={_roomsPerSettlement}, " +
                $"Bridge={_corridorRoomsPerBridge}. {plan.notes}");

            yield return null;

            Dictionary<string, GameObject> prefabById = BuildPrefabLookup();
            var roomsByIndex = new Dictionary<int, RoomInstance>(plan.PlacedCount);
            RoomInstance.SocketValidationReport validationTotals = default;
            int connectionsApplied = 0;

            if (plan.placements.Count == 0)
            {
                Debug.LogError("[RoomPoolSpawner] Settlement Spine plan has no placements.");
                _buildRoutine = null;
                yield break;
            }

            LayoutPlanPlacement hubPlacement = plan.placements[0];
            if (!prefabById.TryGetValue(hubPlacement.prefabId, out GameObject hubPrefab) || hubPrefab == null)
            {
                Debug.LogError($"[RoomPoolSpawner] Missing hub prefab '{hubPlacement.prefabId}'.");
                _buildRoutine = null;
                yield break;
            }

            GameObject hubGo = Instantiate(hubPrefab, _connector.LevelRoot);
            hubGo.transform.SetLocalPositionAndRotation(
                Vector3.zero,
                Quaternion.Euler(0f, hubPlacement.yawDegrees, 0f));
            RoomInstance hub = GetRoom(hubGo);
            hub.SealAllWalls();
            AlignRoomWalkableFloorToWorldY(hub, _connector.LevelRoot.position.y);
            float hubWalkableY = hub.GetWalkableFloorY();
            hub.MarkAsHub();
            RoomInstance.SocketValidationReport hubValidation = hub.ValidateSocketContracts(logWarnings: true);
            validationTotals.missingContractCount += hubValidation.missingContractCount;
            validationTotals.duplicateDirectionCount += hubValidation.duplicateDirectionCount;
            roomsByIndex[0] = hub;
            _placedRoomCount = 1;

            if (_spawnDelaySeconds > 0f)
                yield return new WaitForSeconds(_spawnDelaySeconds);
            else
                yield return null;

            foreach (LayoutPlanConnection connection in plan.connections)
            {
                if (connection.parentIndex < 0 || connection.childIndex < 0)
                    continue;
                if (connection.childIndex >= plan.placements.Count || connection.parentIndex >= plan.placements.Count)
                    continue;
                if (!roomsByIndex.TryGetValue(connection.parentIndex, out RoomInstance parent) || parent == null)
                    continue;

                bool childAlreadyPlaced = roomsByIndex.ContainsKey(connection.childIndex);
                RoomInstance child;
                if (!childAlreadyPlaced)
                {
                    LayoutPlanPlacement childPlacement = plan.placements[connection.childIndex];
                    if (!prefabById.TryGetValue(childPlacement.prefabId, out GameObject childPrefab) || childPrefab == null)
                    {
                        Debug.LogError($"[RoomPoolSpawner] Missing prefab '{childPlacement.prefabId}'.");
                        continue;
                    }

                    GameObject childGo = Instantiate(childPrefab, _connector.LevelRoot);
                    childGo.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                    child = GetRoom(childGo);
                    child.SealAllWalls();
                    RoomInstance.SocketValidationReport childValidation = child.ValidateSocketContracts(logWarnings: true);
                    validationTotals.missingContractCount += childValidation.missingContractCount;
                    validationTotals.duplicateDirectionCount += childValidation.duplicateDirectionCount;
                    roomsByIndex[connection.childIndex] = child;
                    _placedRoomCount = roomsByIndex.Count;
                }
                else
                {
                    child = roomsByIndex[connection.childIndex];
                }

                WallGapController parentWall = parent.GetWall(connection.parentWall);
                WallGapController childWall = child.GetWall(connection.childWall);
                if (parentWall == null || childWall == null)
                {
                    Debug.LogWarning(
                        $"[RoomPoolSpawner] Planned wall missing: {connection.parentWall} / {connection.childWall}");
                    continue;
                }

                bool spawnFrame = ShouldSpawnFrame();
                bool snap = !childAlreadyPlaced;
                if (_connector.ApplyPlannedConnection(
                        parent,
                        parentWall,
                        child,
                        childWall,
                        connection.parentGapOffsetM,
                        connection.childGapOffsetM,
                        spawnFrame,
                        snap))
                {
                    connectionsApplied++;
                    if (snap)
                        child.SetGraphDepth(parent.GraphDepth + 1);
                }

                if (snap)
                {
                    if (_spawnDelaySeconds > 0f)
                        yield return new WaitForSeconds(_spawnDelaySeconds);
                    else
                        yield return null;
                }
            }

            foreach (KeyValuePair<int, RoomInstance> pair in roomsByIndex)
            {
                if (pair.Value != null)
                    AlignRoomWalkableFloorToWorldY(pair.Value, hubWalkableY);
            }

            foreach (KeyValuePair<int, RoomInstance> pair in roomsByIndex)
            {
                RoomInstance room = pair.Value;
                if (room == null)
                    continue;

                foreach (WallGapController wall in room.Walls)
                {
                    if (wall == null || !wall.hasOpening)
                        continue;

                    wall.ConfigureOpening(true, false, wall.gapOffset);
                }
            }

            var placedRooms = new List<RoomInstance>(roomsByIndex.Count);
            for (int i = 0; i < plan.placements.Count; i++)
            {
                if (roomsByIndex.TryGetValue(i, out RoomInstance room) && room != null)
                    placedRooms.Add(room);
            }

            RoomInstance startRoom = hub;
            RoomConnector.ConnectionStats stats = _connector.GetStats();
            (ReachabilityAuditResult reachability, int finalPlacedCount) = RunReachabilityAudit(startRoom);
            int postBuildOverlaps = ValidatePlacedRoomOverlaps();
            if (_repositionPlayerAfterBuild)
                PlacePlayerAfterBuild(startRoom);

            _contentManager?.ActivateAll(_lastUsedSeed);

            float minFloorY = float.PositiveInfinity;
            float maxFloorY = float.NegativeInfinity;
            foreach (RoomInstance room in placedRooms)
            {
                if (room == null)
                    continue;
                float floorY = room.GetWalkableFloorY();
                minFloorY = Mathf.Min(minFloorY, floorY);
                maxFloorY = Mathf.Max(maxFloorY, floorY);
            }

            var summary = new StringBuilder();
            summary.AppendLine(
                $"[RoomPoolSpawner] SettlementSpine done. Placed={finalPlacedCount}, " +
                $"Connections={connectionsApplied}/{plan.connections.Count}, Seed={_lastUsedSeed}, " +
                $"ExitIndex={plan.exitIndex}.");
            summary.AppendLine(plan.notes);
            summary.AppendLine(
                $"FloorY range=[{minFloorY:F3}, {maxFloorY:F3}] delta={maxFloorY - minFloorY:F3}m (want ~0).");
            summary.AppendLine(
                $"Reachability: reachable={reachability.reachableCount}, unreachable={reachability.unreachableCount}, " +
                $"salvaged={reachability.salvagedCount}, destroyed={reachability.destroyedCount}.");
            summary.AppendLine(
                $"Validation missingContracts={validationTotals.missingContractCount}, " +
                $"duplicateDirs={validationTotals.duplicateDirectionCount}, postBuildOverlaps={postBuildOverlaps}.");
            summary.AppendLine(
                $"Connector stats: NoCompatible={stats.noCompatibleSocket}, Gap={stats.gapMismatch}, " +
                $"Overlap={stats.overlapRejected}.");
            Debug.Log(summary.ToString());

            _buildRoutine = null;
        }

        private static void AlignRoomWalkableFloorToWorldY(RoomInstance room, float targetWalkableY)
        {
            if (room == null)
                return;

            float current = room.GetWalkableFloorY();
            float delta = targetWalkableY - current;
            if (Mathf.Abs(delta) > 0.0001f)
                room.transform.position += new Vector3(0f, delta, 0f);
        }

        private List<RoomFootprint> ResolveSettlementLibrary()
        {
            var result = new List<RoomFootprint>();
            if (_settlementFootprints == null || _settlementFootprints.Length == 0)
                return result;

            var poolNames = new HashSet<string>();
            foreach (RoomPrefabEntry entry in _activePrefabEntries)
            {
                if (entry?.Prefab != null)
                    poolNames.Add(entry.Prefab.name);
            }

            foreach (RoomFootprint footprint in _settlementFootprints)
            {
                if (footprint == null || footprint.Prefab == null)
                    continue;

                if (poolNames.Count > 0 && !poolNames.Contains(footprint.Prefab.name))
                    continue;

                result.Add(footprint);
            }

            if (result.Count == 0)
            {
                foreach (RoomFootprint footprint in _settlementFootprints)
                {
                    if (footprint != null && footprint.Prefab != null)
                        result.Add(footprint);
                }
            }

            return result;
        }

        private Dictionary<string, GameObject> BuildPrefabLookup()
        {
            var map = new Dictionary<string, GameObject>();
            foreach (RoomPrefabEntry entry in _activePrefabEntries)
            {
                if (entry?.Prefab != null && !map.ContainsKey(entry.Prefab.name))
                    map[entry.Prefab.name] = entry.Prefab;
            }

            if (_settlementFootprints != null)
            {
                foreach (RoomFootprint footprint in _settlementFootprints)
                {
                    if (footprint?.Prefab != null && !map.ContainsKey(footprint.Prefab.name))
                        map[footprint.Prefab.name] = footprint.Prefab;
                }
            }

            return map;
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

        private int CountPlacedRooms() =>
            _connector.LevelRoot.GetComponentsInChildren<RoomInstance>().Length;

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
        }

        private bool TryPreparePrefabPool()
        {
            MigrateLegacyPrefabPool();
            BuildActivePrefabPool();
            return _activePrefabEntries.Length > 0;
        }

        private void MigrateLegacyPrefabPool()
        {
            if (_roomPrefabEntries != null && _roomPrefabEntries.Length > 0)
                return;

            if (_roomPrefabs == null || _roomPrefabs.Length == 0)
                return;

            var migrated = new List<RoomPrefabEntry>(_roomPrefabs.Length);
            foreach (GameObject prefab in _roomPrefabs)
            {
                if (prefab == null)
                    continue;

                migrated.Add(new RoomPrefabEntry(prefab));
            }

            _roomPrefabEntries = migrated.ToArray();
        }

        private void BuildActivePrefabPool()
        {
            if (_roomPrefabEntries == null || _roomPrefabEntries.Length == 0)
            {
                _activePrefabEntries = Array.Empty<RoomPrefabEntry>();
                return;
            }

            if (_settlementFootprints != null && _settlementFootprints.Length > 0)
                SyncPrefabRolesFromFootprints(_settlementFootprints);

            var valid = new List<RoomPrefabEntry>(_roomPrefabEntries.Length);
            foreach (RoomPrefabEntry entry in _roomPrefabEntries)
            {
                if (entry == null || !entry.IsValid)
                    continue;

                valid.Add(entry);
            }

            _activePrefabEntries = valid.ToArray();

            if (_activePrefabEntries.Length == 0)
                Debug.LogWarning("[RoomPoolSpawner] No valid room prefab entries (null prefab).");
        }

        private bool ShouldSpawnFrame() =>
            _forceFramesOnConnections || Chance(_frameChance);

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
