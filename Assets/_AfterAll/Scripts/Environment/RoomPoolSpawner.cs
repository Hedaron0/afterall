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
        [SerializeField] private bool _spawnFrames;

        private readonly Queue<(RoomInstance room, WallGapController wall)> _openings = new();

        private void Start()
        {
            HideHandPlacedRooms();
            Build();
        }

        public void Build()
        {
            _openings.Clear();

            if (_connector == null)
                _connector = GetComponent<RoomConnector>();

            if (_connector == null || _roomPrefabs.Length == 0)
            {
                Debug.LogError("[RoomPoolSpawner] Need RoomConnector + room prefabs.");
                return;
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
                return;
            }

            WallGapController entry = walls[Random.Range(0, walls.Count)];
            first.OpenWall(entry, _spawnFrames);
            _openings.Enqueue((first, entry));

            int count = 1;
            while (count < _roomCount && _openings.Count > 0)
            {
                var (parentRoom, parentWall) = _openings.Dequeue();
                if (parentRoom.IsWallConnected(parentWall))
                    continue;

                GameObject prefab = _roomPrefabs[Random.Range(0, _roomPrefabs.Length)];
                RoomInstance child = _connector.Connect(parentRoom, parentWall, prefab, _spawnFrames);
                if (child == null)
                    continue;

                count++;
                AddNextOpening(child);
            }

            Debug.Log($"[RoomPoolSpawner] Placed {count} rooms.");
        }

        private void AddNextOpening(RoomInstance room)
        {
            List<WallGapController> closed = room.GetClosedWalls().ToList();
            if (closed.Count == 0)
                return;

            WallGapController next = closed[Random.Range(0, closed.Count)];
            room.OpenWall(next, _spawnFrames);
            _openings.Enqueue((room, next));
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
            foreach (RoomInstance room in FindObjectsByType<RoomInstance>(FindObjectsSortMode.None))
            {
                if (root != null && room.transform.IsChildOf(root))
                    continue;

                room.gameObject.SetActive(false);
            }
        }
    }
}
