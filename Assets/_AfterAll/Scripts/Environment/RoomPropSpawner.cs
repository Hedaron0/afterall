using UnityEngine;

namespace AfterAll.Environment
{
    /// <summary>
    /// Legacy floor-grid prop scatter. Prefer <see cref="RoomSpawnPoint"/> markers on room prefabs.
    /// </summary>
    public class RoomPropSpawner : MonoBehaviour
    {
        [SerializeField] private GameObject[] _propPrefabs = System.Array.Empty<GameObject>();
        [SerializeField] private float _cellSize = 2f;
        [SerializeField, Range(0f, 1f)] private float _density = 0.15f;
        [SerializeField] private float _socketClearance = 2f;
        [SerializeField] private int _randomSeed;
        [SerializeField] private bool _spawnOnStart;

        public bool HasPropPrefabs => _propPrefabs.Length > 0;

        private void Start()
        {
            if (!_spawnOnStart)
                return;

            System.Random rng = _randomSeed != 0
                ? new System.Random(_randomSeed ^ transform.position.GetHashCode())
                : new System.Random();

            SpawnProps(rng);
        }

        /// <summary>
        /// Called by RoomPoolSpawner after generation + reachability pass. Returns spawned prop count.
        /// </summary>
        public int SpawnProps(System.Random rng)
        {
            if (_propPrefabs.Length == 0 || rng == null)
                return 0;

            Renderer floor = FindFloorRenderer();
            if (floor == null)
                return 0;

            Bounds bounds = floor.bounds;
            int gridX = Mathf.Max(1, Mathf.FloorToInt(bounds.size.x / _cellSize));
            int gridZ = Mathf.Max(1, Mathf.FloorToInt(bounds.size.z / _cellSize));
            var occupied = new bool[gridX, gridZ];
            int spawned = 0;

            for (int x = 1; x < gridX - 1; x++)
            for (int z = 1; z < gridZ - 1; z++)
            {
                if (occupied[x, z])
                    continue;

                if (rng.NextDouble() > _density)
                    continue;

                Vector3 position = new(
                    bounds.min.x + x * _cellSize + _cellSize * 0.5f,
                    bounds.min.y,
                    bounds.min.z + z * _cellSize + _cellSize * 0.5f);

                if (IsNearAnySocket(position))
                    continue;

                int prefabIndex = rng.Next(0, _propPrefabs.Length);
                float yaw = rng.Next(0, 4) * 90f;
                Instantiate(_propPrefabs[prefabIndex], position, Quaternion.Euler(0f, yaw, 0f), transform);
                occupied[x, z] = true;
                spawned++;
            }

            return spawned;
        }

        private Renderer FindFloorRenderer()
        {
            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null)
                    continue;

                string objectName = renderer.gameObject.name;
                if (objectName.StartsWith("Cube", System.StringComparison.OrdinalIgnoreCase))
                    continue;

                if (objectName.IndexOf("Floor", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return renderer;
            }

            return null;
        }

        private bool IsNearAnySocket(Vector3 position)
        {
            RoomSocket[] sockets = GetComponentsInChildren<RoomSocket>(true);
            Vector3 flatPosition = new(position.x, 0f, position.z);

            foreach (RoomSocket socket in sockets)
            {
                if (socket == null)
                    continue;

                Vector3 socketFlat = socket.transform.position;
                socketFlat.y = 0f;
                if (Vector3.Distance(flatPosition, socketFlat) < _socketClearance)
                    return true;
            }

            return false;
        }
    }
}
