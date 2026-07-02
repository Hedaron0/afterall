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
            public bool SpawnDoor;
            public int TotalAttempts;
        }

        private enum BuildExitReason
        {
            TargetReached,
            FrontierEmpty,
            GlobalBudgetExhausted,
            OpeningsExhausted
        }

        [SerializeField] private RoomConnector _connector;
        [SerializeField] private GameObject[] _roomPrefabs = System.Array.Empty<GameObject>();
        [SerializeField] private int _roomCount = 5;
        [Header("Connection Rules")]
        [SerializeField] private bool _forceDoorsOnConnections;
        [SerializeField, Range(0f, 1f)] private float _doorChance = 0.35f;
        [SerializeField, Range(0f, 1f)] private float _extraOpeningChance = 0.5f;
        [SerializeField, Min(1)] private int _minOpeningsPerRoom = 1;
        [SerializeField, Min(1)] private int _maxOpeningsPerRoom = 2;

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
        [Header("Player Spawn")]
        [SerializeField] private Transform _player;
        [SerializeField] private float _playerSpawnHeight = 1.0f;
        [SerializeField] private bool _repositionPlayerAfterBuild = true;

        private readonly Queue<OpeningWorkItem> _primaryQueue = new();
        private readonly Queue<OpeningWorkItem> _retryQueue = new();
        private readonly List<string> _deadOpenings = new();
        private Coroutine _buildRoutine;
        private System.Random _rng;

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
            Debug.Log(
                $"[RoomPoolSpawner] Seed={_lastUsedSeed}, Rooms={_roomCount}, " +
                $"DoorChance={_doorChance:F2}, ExtraOpeningChance={_extraOpeningChance:F2}, " +
                $"Openings={_minOpeningsPerRoom}-{_maxOpeningsPerRoom}, " +
                $"AttemptsPerVisit={_attemptsPerOpening}, MaxRetryPasses={_maxRetryPasses}, " +
                $"MaxAttemptsPerOpening={MaxTotalAttemptsPerOpening}, GlobalBudget={_maxGlobalConnectAttempts}, " +
                $"OffsetSearch={_offsetSearchEnabled}, Samples={_offsetSamplesPerWall}");

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

            QueueOpeningsForRoom(first);

            int count = 1;
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
            int postBuildOverlaps = ValidatePlacedRoomOverlaps();
            if (_repositionPlayerAfterBuild)
                PlacePlayerAfterBuild(startRoom);

            LogBuildSummary(
                count,
                passNumber,
                globalConnectAttempts,
                exitReason,
                stats,
                validationTotals,
                postBuildOverlaps);

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
            int postBuildOverlaps)
        {
            var summary = new StringBuilder();
            summary.AppendLine(
                $"[RoomPoolSpawner] Placed {placedCount}/{_roomCount} rooms. " +
                $"Seed={_lastUsedSeed}, Passes={passNumber}, GlobalAttempts={globalConnectAttempts}.");

            if (placedCount < _roomCount)
            {
                summary.AppendLine($"[RoomPoolSpawner] Build stopped early: {exitReason}.");
                switch (exitReason)
                {
                    case BuildExitReason.FrontierEmpty:
                        summary.AppendLine(
                            "[RoomPoolSpawner] Primary and retry queues are empty — no viable openings left.");
                        break;
                    case BuildExitReason.GlobalBudgetExhausted:
                        summary.AppendLine(
                            $"[RoomPoolSpawner] Global connect attempt budget exhausted " +
                            $"(limit={_maxGlobalConnectAttempts}).");
                        break;
                    case BuildExitReason.OpeningsExhausted:
                        summary.AppendLine(
                            $"[RoomPoolSpawner] {_deadOpenings.Count} opening(s) exceeded max attempts " +
                            $"(limit={MaxTotalAttemptsPerOpening}):");
                        foreach (string dead in _deadOpenings)
                            summary.AppendLine($"  - {dead}");
                        break;
                }

                if (_deadOpenings.Count > 0 && exitReason != BuildExitReason.OpeningsExhausted)
                {
                    summary.AppendLine(
                        $"[RoomPoolSpawner] Also had {_deadOpenings.Count} opening(s) marked dead before exit.");
                }
            }

            summary.Append(
                $"[RoomPoolSpawner] Rejects(NoCompatible={stats.noCompatibleSocket}, Gap={stats.gapMismatch}, " +
                $"Overlap={stats.overlapRejected}), " +
                $"OffsetSearch(Retried={stats.offsetRetried}, SolvedOverlap={stats.offsetSolvedOverlap}, " +
                $"Exhausted={stats.offsetSearchExhausted}), FallbackUsed={stats.fallbackUsed}, " +
                $"Contracts(Missing={validationTotals.missingContractCount}, " +
                $"DuplicateDir={validationTotals.duplicateDirectionCount}), PostBuildOverlaps={postBuildOverlaps}.");

            Debug.Log(summary.ToString());
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
                RoomInstance child = _connector.Connect(item.Room, item.Wall, prefab, item.SpawnDoor);
                attemptsUsed++;
                if (child != null)
                    return (child, attemptsUsed);
            }

            return (null, attemptsUsed);
        }

        private void QueueOpeningsForRoom(RoomInstance room)
        {
            List<WallGapController> closed = room.GetClosedWalls().ToList();
            if (closed.Count == 0)
                return;

            Shuffle(closed);

            int minOpenings = Mathf.Clamp(_minOpeningsPerRoom, 1, closed.Count);
            int maxOpenings = Mathf.Clamp(_maxOpeningsPerRoom, minOpenings, closed.Count);
            int targetOpenings = NextInt(minOpenings, maxOpenings + 1);
            int queued = 0;

            foreach (WallGapController wall in closed)
            {
                bool required = queued < minOpenings;
                bool reachedTarget = queued >= targetOpenings;
                if (!required && reachedTarget)
                    break;

                if (required || Chance(_extraOpeningChance))
                {
                    _primaryQueue.Enqueue(new OpeningWorkItem
                    {
                        Room = room,
                        Wall = wall,
                        SpawnDoor = ShouldSpawnDoor(),
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
                    SpawnDoor = ShouldSpawnDoor(),
                    TotalAttempts = 0
                });
            }
        }

        private bool ShouldSpawnDoor()
        {
            return _forceDoorsOnConnections || Chance(_doorChance);
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
