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
        [HideInInspector]
        [SerializeField] private int _worldSeed = 42;

        [Header("Chunk Grid")]
        [SerializeField] [Min(8)] private int _chunkSize = 32;
        [SerializeField] [Min(0.1f)] private float _cellWorldSize = 2f;

        [Header("Zone BSP")]
        [SerializeField] [Range(2, 7)] private int _zoneDepth = 4;
        [SerializeField] [Range(0, 2)] private int _varietyLevel = 1;

        [Header("Chunk Outline")]
        [Tooltip("Black outline ring preserved at each chunk edge (cells). Used in lab docs.")]
        [SerializeField] [Range(2, 12)] private int _chunkOutlineMargin = 5;

        [Header("Room Shapes")]
        [Tooltip("Chance StandardRoom / VoidRoom uses L, corner, or T template instead of plain rectangle.")]
        [SerializeField] [Range(0f, 1f)] private float _shapedRoomChance = 0.65f;
        [SerializeField] [Range(0, 100)] private int _lShapeWeight = 30;
        [SerializeField] [Range(0, 100)] private int _cornerCutWeight = 15;
        [SerializeField] [Range(0, 100)] private int _tShapeWeight = 10;

        [Header("Layout Phase")]
        [Tooltip("Floor-plan only — no doors, exits, or chunk-edge connector carving.")]
        [SerializeField] private bool _layoutOnlyMode = true;

        [Header("Passes")]
        [SerializeField] [Range(0f, 1f)] private float _doorChance = 0.4f;
        [SerializeField] [Min(1)] private int _exitDensity = 3;
        [SerializeField] [Min(1)] private int _lightRange = 6;

        [Header("Streaming")]
        [Tooltip("Square half-extent in chunks (1 = 3×3, 2 = 5×5).")]
        [SerializeField] [Min(0)] private int _loadRadius = 2;

        public int WorldSeed => _worldSeed;
        public int ChunkSize => _chunkSize;
        public float CellWorldSize => _cellWorldSize;
        public float ChunkSizeMetres => _chunkSize * _cellWorldSize;
        public int ZoneDepth => _zoneDepth;
        public int VarietyLevel => _varietyLevel;
        public int ChunkOutlineMargin => _chunkOutlineMargin;
        public float ShapedRoomChance => _shapedRoomChance;
        public int LShapeWeight => _lShapeWeight;
        public int CornerCutWeight => _cornerCutWeight;
        public int TShapeWeight => _tShapeWeight;
        public int RectangleShapeWeight =>
            System.Math.Max(1, 100 - _lShapeWeight - _cornerCutWeight - _tShapeWeight);
        public bool LayoutOnlyMode => _layoutOnlyMode;
        public float DoorChance => _doorChance;
        public int ExitDensity => _exitDensity;
        public int LightRange => _lightRange;
        public int LoadRadius => _loadRadius;
    }
}
