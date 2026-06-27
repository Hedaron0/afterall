using System.Collections.Generic;
using AfterAll.Environment;
using AfterAll.Generation.BackroomsMap;
using UnityEngine;

namespace AfterAll.Generation
{
    /// <summary>
    /// Streams procedural chunks around the player using BackroomsMapGenerator.
    /// </summary>
    [AddComponentMenu("AfterAll/Generation/Chunk Manager")]
    [DefaultExecutionOrder(-100)]
    public class ChunkManager : MonoBehaviour
    {
        [SerializeField] private BackroomsMapConfig _config;
        [SerializeField] private ChunkSpawnProfile _spawnProfile;

        [Tooltip("Player transform used to decide which chunks to load. Auto-found if empty.")]
        [SerializeField] private Transform _player;

        [Tooltip("Re-check player chunk every N seconds (0 = every frame).")]
        [SerializeField] [Min(0f)] private float _refreshInterval = 0.25f;

        [SerializeField] private bool _debugLog;

        private readonly Dictionary<ChunkCoord, Chunk> _active = new();
        private readonly Queue<Chunk>                  _pool   = new();

        private ChunkCoord _lastPlayerChunk;
        private float      _nextRefresh;
        private bool       _initialized;

        public IReadOnlyDictionary<ChunkCoord, Chunk> ActiveChunks => _active;
        public Transform PlayerTransform => _player;

        private void Awake()
        {
            DisableStandaloneChunks();
            FluorescentLightManager.EnsureExists();
        }

        private void Start()
        {
            if (_config == null)
            {
                Debug.LogError("[ChunkManager] BackroomsMapConfig is not assigned.", this);
                enabled = false;
                return;
            }

            if (_player == null)
            {
                var movement = FindAnyObjectByType<AfterAll.Player.PlayerMovement>();
                if (movement != null)
                    _player = movement.transform;
            }

            if (_player == null)
            {
                Debug.LogError("[ChunkManager] No player transform assigned or found.", this);
                enabled = false;
                return;
            }

            Refresh(force: true);
            _initialized = true;

            if (_debugLog)
            {
                Debug.Log(
                    $"[ChunkManager] Streaming on. Player chunk {_lastPlayerChunk}, " +
                    $"load radius {_config.LoadRadius} (square), chunk {_config.ChunkSizeMetres}m.",
                    this);
            }
        }

        private void Update()
        {
            if (!_initialized) return;

            if (_refreshInterval <= 0f)
            {
                Refresh(force: false);
                return;
            }

            if (Time.time >= _nextRefresh)
            {
                _nextRefresh = Time.time + _refreshInterval;
                Refresh(force: false);
            }
        }

        private void OnDestroy()
        {
            foreach (var kv in _active)
                ReleaseChunk(kv.Value);

            _active.Clear();

            while (_pool.Count > 0)
            {
                var chunk = _pool.Dequeue();
                if (chunk != null)
                    Destroy(chunk.gameObject);
            }
        }

        [ContextMenu("Regenerate All Active Chunks")]
        public void RegenerateAllActive()
        {
            foreach (var kv in _active)
                kv.Value.Generate();
        }

        [ContextMenu("Refresh Chunks")]
        public void Refresh(bool force)
        {
            var playerChunk = ChunkCoord.FromWorldPosition(_player.position, _config.ChunkSizeMetres);

            if (!force && playerChunk == _lastPlayerChunk)
                return;

            if (_debugLog && playerChunk != _lastPlayerChunk)
            {
                Debug.Log(
                    $"[ChunkManager] Player entered chunk {playerChunk} " +
                    $"(world {_player.position.x:F0}, {_player.position.z:F0}).",
                    this);
            }

            _lastPlayerChunk = playerChunk;

            int radius = _config.LoadRadius;
            var needed = new HashSet<ChunkCoord>();

            for (int dz = -radius; dz <= radius; dz++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                    needed.Add(new ChunkCoord(playerChunk.X + dx, playerChunk.Z + dz));
            }

            var toRemove = new List<ChunkCoord>();
            foreach (var kv in _active)
            {
                if (!needed.Contains(kv.Key))
                    toRemove.Add(kv.Key);
            }

            foreach (var coord in toRemove)
            {
                if (_debugLog)
                    Debug.Log($"[ChunkManager] Unload chunk {coord}.", this);

                ReleaseChunk(_active[coord]);
                _active.Remove(coord);
            }

            foreach (var coord in needed)
            {
                if (_active.ContainsKey(coord)) continue;

                if (_debugLog)
                    Debug.Log($"[ChunkManager] Load chunk {coord}.", this);

                _active[coord] = AcquireChunk(coord);
            }
        }

        private Chunk AcquireChunk(ChunkCoord coord)
        {
            Chunk chunk;

            if (_pool.Count > 0)
            {
                chunk = _pool.Dequeue();
                chunk.gameObject.SetActive(true);
            }
            else
            {
                var go = new GameObject($"Chunk_{coord.X}_{coord.Z}");
                chunk = go.AddComponent<Chunk>();
            }

            chunk.SetupManaged(_config, coord, transform, _spawnProfile);
            chunk.Generate();
            return chunk;
        }

        private void ReleaseChunk(Chunk chunk)
        {
            chunk.Despawn();
            chunk.gameObject.SetActive(false);
            chunk.transform.SetParent(transform, false);
            _pool.Enqueue(chunk);
        }

        private static void DisableStandaloneChunks()
        {
            var chunks = FindObjectsByType<Chunk>();
            foreach (var chunk in chunks)
            {
                if (chunk.IsManaged) continue;
                chunk.enabled = false;
            }
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (_config == null || _player == null) return;

            float size = _config.ChunkSizeMetres;
            int radius = _config.LoadRadius;
            var center = ChunkCoord.FromWorldPosition(_player.position, size);

            Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.35f);
            for (int dz = -radius; dz <= radius; dz++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    var origin = new ChunkCoord(center.X + dx, center.Z + dz).WorldOrigin(size);
                    var gizmoCenter = new Vector3(origin.x + size * 0.5f, 1f, origin.y + size * 0.5f);
                    Gizmos.DrawWireCube(gizmoCenter, new Vector3(size, 2f, size));
                }
            }
        }
#endif
    }
}
