using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AfterAll.Environment
{
    /// <summary>
    /// Press Play: builds a chain of connected rooms from the prefab pool.
    /// Inspector: assign Room Prefabs + Room Count. Nothing else.
    /// </summary>
    public class RoomPoolSpawner : MonoBehaviour
    {
        [SerializeField] private RoomConnector _connector;
        [SerializeField] private GameObject[] _roomPrefabs = System.Array.Empty<GameObject>();
        [SerializeField] private int _roomCount = 5;
        [Header("Connection Rules")]
        [SerializeField] private bool _forceDoorsOnConnections;
        [SerializeField, Range(0f, 1f)] private float _doorChance = 0.35f;
        [SerializeField, Range(0f, 1f)] private float _extraOpeningChance = 0.5f;
        [SerializeField, Min(1)] private int _minOpeningsPerRoom = 1;
        [SerializeField, Min(1)] private int _maxOpeningsPerRoom = 2;

        [Header("Build Pace")]
        [SerializeField, Min(0f)] private float _spawnDelaySeconds = 0.05f;
        [SerializeField, Min(1)] private int _attemptsPerOpening = 6;

        private readonly Queue<(RoomInstance room, WallGapController wall, bool spawnDoor)> _openings = new();
        private Coroutine _buildRoutine;

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
            _openings.Clear();

            if (_connector == null)
                _connector = GetComponent<RoomConnector>();

            if (_connector == null || _roomPrefabs.Length == 0)
            {
                Debug.LogError("[RoomPoolSpawner] Need RoomConnector + room prefabs.");
                _buildRoutine = null;
                yield break;
            }

            ClearLevelRoot();

            GameObject firstPrefab = _roomPrefabs[Random.Range(0, _roomPrefabs.Length)];
            GameObject firstGo = SpawnAtOrigin(firstPrefab);
            RoomInstance first = GetRoom(firstGo);
            first.SealAllWalls();

            List<WallGapController> walls = first.GetClosedWalls().ToList();
            if (walls.Count == 0)
            {
                Debug.LogError($"[RoomPoolSpawner] {firstPrefab.name} has no WallGapController walls.");
                _buildRoutine = null;
                yield break;
            }

            QueueOpeningsForRoom(first);

            int count = 1;
            while (count < _roomCount && _openings.Count > 0)
            {
                var (parentRoom, parentWall, spawnDoor) = _openings.Dequeue();
                if (parentRoom.IsWallConnected(parentWall))
                    continue;

                RoomInstance child = TryConnectWithRetries(parentRoom, parentWall, spawnDoor);
                if (child == null)
                    continue;

                count++;
                QueueOpeningsForRoom(child);

                if (_spawnDelaySeconds > 0f)
                    yield return new WaitForSeconds(_spawnDelaySeconds);
            }

            Debug.Log($"[RoomPoolSpawner] Placed {count} rooms.");
            _buildRoutine = null;
        }

        private RoomInstance TryConnectWithRetries(RoomInstance parentRoom, WallGapController parentWall, bool spawnDoor)
        {
            for (int attempt = 0; attempt < _attemptsPerOpening; attempt++)
            {
                GameObject prefab = _roomPrefabs[Random.Range(0, _roomPrefabs.Length)];
                RoomInstance child = _connector.Connect(parentRoom, parentWall, prefab, spawnDoor);
                if (child != null)
                    return child;
            }

            return null;
        }

        private void QueueOpeningsForRoom(RoomInstance room)
        {
            List<WallGapController> closed = room.GetClosedWalls().ToList();
            if (closed.Count == 0)
                return;

            Shuffle(closed);

            int minOpenings = Mathf.Clamp(_minOpeningsPerRoom, 1, closed.Count);
            int maxOpenings = Mathf.Clamp(_maxOpeningsPerRoom, minOpenings, closed.Count);
            int targetOpenings = Random.Range(minOpenings, maxOpenings + 1);
            int queued = 0;

            foreach (WallGapController wall in closed)
            {
                bool required = queued < minOpenings;
                bool reachedTarget = queued >= targetOpenings;
                if (!required && reachedTarget)
                    break;

                if (required || Random.value <= _extraOpeningChance)
                {
                    _openings.Enqueue((room, wall, ShouldSpawnDoor()));
                    queued++;
                }
            }

            if (queued == 0)
                _openings.Enqueue((room, closed[0], ShouldSpawnDoor()));
        }

        private bool ShouldSpawnDoor()
        {
            return _forceDoorsOnConnections || Random.value <= _doorChance;
        }

        private static void Shuffle<T>(IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
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
