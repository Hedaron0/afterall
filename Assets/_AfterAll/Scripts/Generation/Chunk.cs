using System.Collections.Generic;
using UnityEngine;

namespace AfterAll.Generation
{
    /// <summary>
    /// Owns the geometry for one chunk region.
    /// Driven by ChunkManager for streaming, or used standalone for single-chunk tests.
    /// </summary>
    [AddComponentMenu("AfterAll/Generation/Chunk")]
    public class Chunk : MonoBehaviour
    {
        [SerializeField] private MapConfig _config;

        [Tooltip("Override seed for standalone testing only. −1 = derive from grid coord or MapConfig.")]
        [SerializeField] private int _seedOverride = -1;

        [Tooltip("Standalone mode: world XZ of local (0,0). Ignored when managed by ChunkManager.")]
        [SerializeField] private Vector2 _worldOrigin = Vector2.zero;

        [Tooltip("Standalone mode only — auto-generate on Start when not managed by ChunkManager.")]
        [SerializeField] private bool _generateOnStart = true;

        private readonly List<GameObject> _spawned = new();

        private ChunkCoord _coord;
        private bool       _managed;

        public ChunkCoord Coord     => _coord;
        public bool       IsManaged => _managed;

        // ──────────────────────────────────────────────────────────────────────────
        //  Unity lifecycle
        // ──────────────────────────────────────────────────────────────────────────

        private void Start()
        {
            if (!_managed && _generateOnStart && !IsStreamingActive())
                Generate();
        }

        private static bool IsStreamingActive() =>
            FindAnyObjectByType<ChunkManager>() != null;

        private void OnDestroy() => Despawn();

        // ──────────────────────────────────────────────────────────────────────────
        //  ChunkManager API
        // ──────────────────────────────────────────────────────────────────────────

        /// <summary>Called by ChunkManager before the first Generate().</summary>
        public void SetupManaged(MapConfig config, ChunkCoord coord, Transform parent)
        {
            _config  = config;
            _coord   = coord;
            _managed = true;

            transform.SetParent(parent, false);
            name = $"Chunk_{coord.X}_{coord.Z}";
        }

        // ──────────────────────────────────────────────────────────────────────────
        //  Generation
        // ──────────────────────────────────────────────────────────────────────────

        [ContextMenu("Generate")]
        public void Generate()
        {
            if (_config == null)
            {
                Debug.LogError("[Chunk] MapConfig is not assigned.", this);
                return;
            }

            Despawn();

            float chunkSize = _config.ChunkSize;
            Vector2 origin  = _managed
                ? _coord.WorldOrigin(chunkSize)
                : _worldOrigin;

            transform.position = new Vector3(origin.x, 0f, origin.y);

            int seed = ResolveSeed();

            var chunkBounds = new Rect(0f, 0f, chunkSize, chunkSize);
            var bsp         = BspPartitioner.Partition(chunkBounds, _config, seed);

            var rootRng  = new Rng(seed);
            var wallRng  = rootRng.Derive(1);
            var lightRng = rootRng.Derive(2);

            ChunkCoord? stitchCoord = _managed ? _coord : (ChunkCoord?)null;
            var spec = WallLayout.Build(bsp, _config, wallRng, stitchCoord);
            spec = ConnectivityPass.Apply(bsp, spec, _config.OpeningMinWidth);

            _spawned.AddRange(GeometrySpawner.Spawn(spec, _config, origin, transform));
            LightPlacer.Place(spec, _config, lightRng, origin, transform, _spawned);

            int lightCount = 0;
            for (int i = 0; i < _spawned.Count; i++)
            {
                if (_spawned[i] != null && _spawned[i].name == "Light")
                    lightCount++;
            }

            Debug.Log($"[Chunk] Generated {(_managed ? _coord.ToString() : "standalone")} " +
                      $"seed={seed}: {bsp.Rooms.Count} rooms, {_spawned.Count} objects, {lightCount} lights.", this);
        }

        [ContextMenu("Despawn")]
        public void Despawn()
        {
            foreach (var go in _spawned)
            {
                if (go == null) continue;
#if UNITY_EDITOR
                if (!Application.isPlaying)
                    DestroyImmediate(go);
                else
#endif
                    Destroy(go);
            }

            _spawned.Clear();
        }

        private int ResolveSeed()
        {
            if (_seedOverride >= 0)
                return _seedOverride;

            if (_managed)
                return new Rng(_config.Seed).Derive(_coord.X, _coord.Z).Seed;

            return _config.Seed;
        }
    }
}
