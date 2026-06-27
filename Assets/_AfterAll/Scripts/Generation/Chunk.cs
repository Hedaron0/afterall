using AfterAll.Generation.BackroomsMap;
using UnityEngine;

namespace AfterAll.Generation
{
    /// <summary>
    /// Owns layout data for one chunk region. Geometry spawn deferred until v3 step 8.
    /// </summary>
    [AddComponentMenu("AfterAll/Generation/Chunk")]
    public class Chunk : MonoBehaviour
    {
        [SerializeField] private BackroomsMapConfig _config;

        [Tooltip("Override seed for standalone testing only. −1 = derive from grid coord or config.")]
        [SerializeField] private int _seedOverride = -1;

        [Tooltip("Standalone mode: world XZ of local (0,0). Ignored when managed by ChunkManager.")]
        [SerializeField] private Vector2 _worldOrigin = Vector2.zero;

        [Tooltip("Standalone mode only — auto-generate on Start when not managed by ChunkManager.")]
        [SerializeField] private bool _generateOnStart = true;

        private ChunkData _data;
        private ChunkCoord _coord;
        private bool       _managed;

        public ChunkCoord Coord     => _coord;
        public bool       IsManaged => _managed;
        public ChunkData  Data      => _data;

        private void Start()
        {
            if (!_managed && _generateOnStart && !IsStreamingActive())
                Generate();
        }

        private static bool IsStreamingActive() =>
            FindAnyObjectByType<ChunkManager>() != null;

        public void SetupManaged(BackroomsMapConfig config, ChunkCoord coord, Transform parent)
        {
            _config  = config;
            _coord   = coord;
            _managed = true;

            transform.SetParent(parent, false);
            name = $"Chunk_{coord.X}_{coord.Z}";
        }

        [ContextMenu("Generate")]
        public void Generate()
        {
            if (_config == null)
            {
                Debug.LogError("[Chunk] BackroomsMapConfig is not assigned.", this);
                return;
            }

            float chunkSize = _config.ChunkSizeMetres;
            Vector2 origin  = _managed
                ? _coord.WorldOrigin(chunkSize)
                : _worldOrigin;

            transform.position = new Vector3(origin.x, 0f, origin.y);

            int chunkX = _managed ? _coord.X : 0;
            int chunkZ = _managed ? _coord.Z : 0;
            int seed   = ResolveSeed();

            _data = BackroomsMapGenerator.Generate(_config, chunkX, chunkZ, seed);

            Debug.Log($"[Chunk] v3 data generated {(_managed ? _coord.ToString() : "standalone")} " +
                      $"seed={seed}: { _data.ZoneCount} zones, floor={_data.FloorFraction():P0}. " +
                      "3D spawn deferred.", this);
        }

        [ContextMenu("Despawn")]
        public void Despawn()
        {
            _data = null;
        }

        private int ResolveSeed()
        {
            if (_seedOverride >= 0)
                return _seedOverride;

            if (_managed)
                return BackroomsMapGenerator.DeriveChunkSeed(_config.WorldSeed, _coord.X, _coord.Z);

            return _config.WorldSeed;
        }
    }
}
