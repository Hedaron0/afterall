using System.Collections.Generic;
using UnityEngine;

namespace AfterAll.Generation
{
    /// <summary>
    /// Owns the geometry for one 32×32m chunk.
    /// Drives the Generate → Despawn lifecycle; all spawned objects are children
    /// of this GameObject so pooling/unloading is a single SetActive / Destroy call.
    ///
    /// Usage (test):
    ///   1. Add this component to an empty GameObject in the scene.
    ///   2. Assign a MapConfig asset (make sure it has the three prefabs set).
    ///   3. Hit Play — or right-click the component → Generate.
    /// </summary>
    [AddComponentMenu("AfterAll/Generation/Chunk")]
    public class Chunk : MonoBehaviour
    {
        [SerializeField] private MapConfig _config;

        [Tooltip("Override seed for quick iteration. −1 = use MapConfig.Seed.")]
        [SerializeField] private int _seedOverride = -1;

        [Tooltip("World-space XZ position of this chunk's (0,0) local corner.")]
        [SerializeField] private Vector2 _worldOrigin = Vector2.zero;

        [Tooltip("Generate automatically when the scene starts (useful for the single-chunk test).")]
        [SerializeField] private bool _generateOnStart = true;

        private readonly List<GameObject> _spawned = new();

        // ──────────────────────────────────────────────────────────────────────────
        //  Unity lifecycle
        // ──────────────────────────────────────────────────────────────────────────

        private void Start()
        {
            if (_generateOnStart)
                Generate();
        }

        private void OnDestroy() => Despawn();

        // ──────────────────────────────────────────────────────────────────────────
        //  Public API (also accessible via right-click context menu in Inspector)
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

            int seed = _seedOverride >= 0 ? _seedOverride : _config.Seed;

            // BSP uses the raw chunk seed.
            var chunkBounds = new UnityEngine.Rect(0f, 0f, _config.ChunkSize, _config.ChunkSize);
            var bsp         = BspPartitioner.Partition(chunkBounds, _config, seed);

            // Each sub-system gets its own derived seed — changing one won't shift the others.
            var rootRng  = new Rng(seed);
            var wallRng  = rootRng.Derive(1); // wall openings
            var lightRng = rootRng.Derive(2); // light dark-patches

            var spec = WallLayout.Build(bsp, _config, wallRng);
            _spawned.AddRange(GeometrySpawner.Spawn(spec, _config, _worldOrigin, transform));
            LightPlacer.Place(spec, _config, lightRng, _worldOrigin, transform, _spawned);

            Debug.Log($"[Chunk] Generated seed={seed}: {bsp.Rooms.Count} rooms, " +
                      $"{bsp.Boundaries.Count} boundaries, {_spawned.Count} objects.", this);
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
    }
}
