using UnityEngine;
using UnityEngine.Serialization;

namespace AfterAll.Generation
{
    /// <summary>
    /// Central configuration for procedural map generation.
    /// One asset per project; assign it on all generation components.
    /// </summary>
    [CreateAssetMenu(fileName = "MapConfig", menuName = "AfterAll/Generation/Map Config")]
    public class MapConfig : ScriptableObject
    {
        [Header("World")]
        [SerializeField] private int _seed = 42;
        [SerializeField] [Min(8f)] private float _chunkSize = 32f;
        [SerializeField] [Min(0.1f)] private float _wallHeight = 2.7f;

        [Header("BSP Partitioning")]
        [Tooltip("Smallest side length (metres) a region can have before splitting stops.")]
        [SerializeField] [Min(2f)] private float _minRoomSize = 6f;

        [Tooltip("Minimum split position as a fraction of the current rect's dimension.")]
        [SerializeField] [Range(0.15f, 0.45f)] private float _splitMin = 0.25f;

        [Tooltip("Maximum split position as a fraction of the current rect's dimension.")]
        [SerializeField] [Range(0.55f, 0.85f)] private float _splitMax = 0.75f;

        [Tooltip("Probability that a split stops early, producing a larger open region.")]
        [SerializeField] [Range(0f, 0.4f)] private float _earlyStopChance = 0.08f;

        [Tooltip("Hard depth cap for the BSP tree. 6 gives 32–64 leaf regions.")]
        [SerializeField] [Range(2, 8)] private int _maxDepth = 6;

        [Tooltip("Probability of splitting along the non-dominant axis, adding variety.")]
        [SerializeField] [Range(0f, 0.5f)] private float _axisFlipChance = 0.3f;

        [Header("Prefabs")]
        [Tooltip("Wall prefab from Assets/_AfterAll/Prefabs/Backrooms_Modular/Wall.prefab")]
        [SerializeField] private GameObject _wallPrefab;

        [Tooltip("Floor prefab from Assets/_AfterAll/Prefabs/Backrooms_Modular/Floor.prefab")]
        [SerializeField] private GameObject _floorPrefab;

        [Tooltip("Ceiling prefab from Assets/_AfterAll/Prefabs/Backrooms_Modular/Ceiling.prefab")]
        [SerializeField] private GameObject _ceilingPrefab;

        [Header("Wall Geometry")]
        [Tooltip("Wall thickness in metres. Keep thin — Backrooms walls are office partitions.")]
        [SerializeField] [Range(0.02f, 0.5f)] private float _wallThickness = 0.04f;

        [Tooltip("Floor and ceiling slab thickness in metres.")]
        [SerializeField] [Min(0.1f)] private float _slabThickness = 0.4f;

        [Header("Openings")]
        [Tooltip("Each wall line gets at most one doorway. These pick how many different walls per room are open.")]
        [FormerlySerializedAs("_minOpeningsPerBoundary")]
        [SerializeField] [Range(1, 4)] private int _minOpeningsPerRoom = 1;

        [FormerlySerializedAs("_maxOpeningsPerBoundary")]
        [Tooltip("Maximum different walls with a doorway per room. Extra openings always go on another wall, never the same one.")]
        [SerializeField] [Range(1, 4)] private int _maxOpeningsPerRoom = 2;

        [Tooltip("Minimum doorway width in metres.")]
        [SerializeField] [Min(0.5f)] private float _openingMinWidth = 0.9f;

        [Tooltip("Maximum doorway width in metres.")]
        [SerializeField] [Min(0.5f)] private float _openingMaxWidth = 2.2f;

        [Tooltip("Minimum distance from each boundary end to the nearest opening edge, in metres.")]
        [SerializeField] [Min(0f)] private float _openingEdgeMargin = 0.8f;

        [Header("Streaming")]
        [Tooltip("Chunk grid radius around the player (1 = 5 chunks, 2 = 13 chunks circular).")]
        [SerializeField] [Range(1, 4)] private int _loadRadius = 1;

        [Header("Perimeter")]
        [Tooltip("Spawn solid walls along all four chunk edges. Disable when ChunkManager stitches neighbours.")]
        [SerializeField] private bool _addPerimeterWalls = true;

        [Header("Lights — Prefab Reference")]
        [Tooltip("Which FluorescentPanel prefab to spawn. FluorescentLight settings live on that prefab — not here.")]
        [SerializeField] private GameObject _lightPanelPrefab;

        [Tooltip("Probability that a light grid position is left dark (0 = all lit, 1 = all dark).")]
        [SerializeField] [Range(0f, 0.9f)] private float _lightDarkChance = 0.15f;

        [Tooltip("Minimum clearance from room edges when placing lights (metres).")]
        [SerializeField] [Min(0.2f)] private float _lightRoomInset = 0.8f;

        [Header("Lights — Grid Alignment")]
        [Tooltip("World-space size of one ceiling tile (measured from reference panels in scene). " +
                 "Default 1.327 m — re-measure if you change the ceiling shader tiling.")]
        [SerializeField] [Min(0.1f)] private float _ceilingTileSize = 1.327f;

        [Tooltip("How many ceiling tiles between consecutive lights. " +
                 "3 tiles × 1.327 m = ~3.98 m spacing — matches Backrooms troffer rows.")]
        [SerializeField] [Range(1, 8)] private int _lightSpacingTiles = 3;

        [Tooltip("World-space X anchor of the light grid. " +
                 "Measured from scene reference panels so lights sit in ceiling tile centres. " +
                 "Default 0.221 m — retune if ceiling tile size changes.")]
        [SerializeField] private float _lightGridOffsetX = 0.221f;

        [Tooltip("World-space Z anchor of the light grid. " +
                 "Default 0.483 m — retune if ceiling tile size changes.")]
        [SerializeField] private float _lightGridOffsetZ = 0.483f;

        public int Seed              => _seed;
        public float ChunkSize       => _chunkSize;
        public float WallHeight      => _wallHeight;
        public float MinRoomSize     => _minRoomSize;
        public float SplitMin        => _splitMin;
        public float SplitMax        => _splitMax;
        public float EarlyStopChance => _earlyStopChance;
        public int MaxDepth          => _maxDepth;
        public float AxisFlipChance  => _axisFlipChance;

        public GameObject WallPrefab    => _wallPrefab;
        public GameObject FloorPrefab   => _floorPrefab;
        public GameObject CeilingPrefab => _ceilingPrefab;

        public float WallThickness   => _wallThickness;
        public float SlabThickness   => _slabThickness;

        public int MinOpeningsPerRoom  => _minOpeningsPerRoom;
        public int MaxOpeningsPerRoom  => _maxOpeningsPerRoom;
        public float OpeningMinWidth       => _openingMinWidth;
        public float OpeningMaxWidth       => _openingMaxWidth;
        public float OpeningEdgeMargin     => _openingEdgeMargin;

        public int   LoadRadius              => _loadRadius;

        public bool AddPerimeterWalls      => _addPerimeterWalls;

        public GameObject LightPanelPrefab  => _lightPanelPrefab;
        public float LightDarkChance       => _lightDarkChance;
        public float LightRoomInset        => _lightRoomInset;

        public float CeilingTileSize       => _ceilingTileSize;
        public int   LightSpacingTiles     => _lightSpacingTiles;
        /// <summary>Derived light-to-light spacing in metres (= CeilingTileSize × LightSpacingTiles).</summary>
        public float LightSpacing          => _ceilingTileSize * _lightSpacingTiles;
        public float LightGridOffsetX      => _lightGridOffsetX;
        public float LightGridOffsetZ      => _lightGridOffsetZ;

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (_maxOpeningsPerRoom < _minOpeningsPerRoom)
                _maxOpeningsPerRoom = _minOpeningsPerRoom;

            // Prevent accidental inspector drag from blocking all light placement.
            float maxInset = Mathf.Max(0.2f, _minRoomSize * 0.45f);
            _lightRoomInset = Mathf.Clamp(_lightRoomInset, 0.2f, maxInset);
        }
#endif
    }
}
