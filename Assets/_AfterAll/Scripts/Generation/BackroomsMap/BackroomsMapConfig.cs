using UnityEngine;

namespace AfterAll.Generation.BackroomsMap
{
    /// <summary>
    /// Runtime-deterministic Backrooms map generation settings.
    /// Chunk world size = ChunkSize * CellWorldSize metres.
    /// </summary>
    [CreateAssetMenu(fileName = "BackroomsMapConfig", menuName = "AfterAll/Generation/Backrooms Map Config")]
    public sealed class BackroomsMapConfig : ScriptableObject
    {
        [Header("World")]
        [SerializeField] private int _worldSeed = 42;

        [Header("Chunk Grid")]
        [SerializeField] [Min(8)] private int _chunkSize = 26;
        [SerializeField] [Min(0.1f)] private float _cellWorldSize = 2f;

        [Header("Zone BSP")]
        [SerializeField] [Range(2, 7)] private int _zoneDepth = 4;
        [SerializeField] [Range(0, 2)] private int _varietyLevel = 1;

        [Header("Later Passes")]
        [SerializeField] [Range(0f, 1f)] private float _doorChance = 0.4f;
        [SerializeField] [Min(1)] private int _exitDensity = 3;
        [SerializeField] [Min(1)] private int _lightRange = 6;

        [Header("Streaming")]
        [SerializeField] [Min(0)] private int _loadRadius = 1;

        public int WorldSeed => _worldSeed;
        public int ChunkSize => _chunkSize;
        public float CellWorldSize => _cellWorldSize;
        public float ChunkSizeMetres => _chunkSize * _cellWorldSize;
        public int ZoneDepth => _zoneDepth;
        public int VarietyLevel => _varietyLevel;
        public float DoorChance => _doorChance;
        public int ExitDensity => _exitDensity;
        public int LightRange => _lightRange;
        public int LoadRadius => _loadRadius;
    }
}
