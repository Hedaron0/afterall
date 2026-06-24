using System.Collections.Generic;
using AfterAll.Environment;
using UnityEngine;

namespace AfterAll.Generation
{
    /// <summary>
    /// Streams procedural chunks around the player.
    /// Loads a square grid (loadRadius × 2 + 1), pools unloaded chunks, and
    /// stitches border openings so adjacent chunks align at seams.
    /// </summary>
    [AddComponentMenu("AfterAll/Generation/Chunk Manager")]
    [DefaultExecutionOrder(-100)]
    public class ChunkManager : MonoBehaviour
    {
        [SerializeField] private MapConfig _config;

        [Tooltip("Player transform used to decide which chunks to load. Auto-found if empty.")]
        [SerializeField] private Transform _player;

        [Tooltip("Re-check player chunk every N seconds (0 = every frame).")]
        [SerializeField] [Min(0f)] private float _refreshInterval = 0.25f;

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

        // ──────────────────────────────────────────────────────────────────────────
        //  Unity lifecycle
        // ──────────────────────────────────────────────────────────────────────────

        private void Start()
        {
            if (_config == null)
            {
                Debug.LogError("[ChunkManager] MapConfig is not assigned.", this);
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

        // ──────────────────────────────────────────────────────────────────────────
        //  Streaming
        // ──────────────────────────────────────────────────────────────────────────

        [ContextMenu("Regenerate All Active Chunks")]
        public void RegenerateAllActive()
        {
            foreach (var kv in _active)
                kv.Value.Generate();
        }

        [ContextMenu("Refresh Chunks")]
        public void Refresh(bool force)
        {
            var playerChunk = ChunkCoord.FromWorldPosition(_player.position, _config.ChunkSize);

            if (!force && playerChunk == _lastPlayerChunk)
                return;

            _lastPlayerChunk = playerChunk;

            int radius   = _config.LoadRadius;
            int radiusSq = radius * radius;
            var needed   = new HashSet<ChunkCoord>();

            for (int dz = -radius; dz <= radius; dz++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    if (dx * dx + dz * dz > radiusSq)
                        continue;

                    needed.Add(new ChunkCoord(playerChunk.X + dx, playerChunk.Z + dz));
                }
            }

            // Unload chunks outside the needed set.
            var toRemove = new List<ChunkCoord>();
            foreach (var kv in _active)
            {
                if (!needed.Contains(kv.Key))
                    toRemove.Add(kv.Key);
            }

            foreach (var coord in toRemove)
            {
                ReleaseChunk(_active[coord]);
                _active.Remove(coord);
            }

            // Load missing chunks.
            foreach (var coord in needed)
            {
                if (_active.ContainsKey(coord)) continue;
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
                chunk.SetupManaged(_config, coord, transform);
            }
            else
            {
                var go = new GameObject($"Chunk_{coord.X}_{coord.Z}");
                chunk = go.AddComponent<Chunk>();
                chunk.SetupManaged(_config, coord, transform);
            }

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

        /// <summary>
        /// Disables standalone Chunk components in the scene so they don't fight the manager.
        /// </summary>
        private static void DisableStandaloneChunks()
        {
            var chunks = FindObjectsByType<Chunk>();
            foreach (var chunk in chunks)
            {
                if (chunk.IsManaged) continue;
                chunk.enabled = false;
            }
        }
    }
}
