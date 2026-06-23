using UnityEngine;

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
        [SerializeField] [Range(0.1f, 0.5f)] private float _wallThickness = 0.2f;

        [Tooltip("Floor and ceiling slab thickness in metres.")]
        [SerializeField] [Min(0.1f)] private float _slabThickness = 0.4f;

        [Header("Openings")]
        [Tooltip("Minimum number of openings (doorway gaps) per boundary. 1 = always at least one passage.")]
        [SerializeField] [Range(0, 4)] private int _minOpeningsPerBoundary = 1;

        [Tooltip("Maximum number of openings per boundary.")]
        [SerializeField] [Range(1, 5)] private int _maxOpeningsPerBoundary = 2;

        [Tooltip("Minimum doorway width in metres.")]
        [SerializeField] [Min(0.5f)] private float _openingMinWidth = 0.9f;

        [Tooltip("Maximum doorway width in metres.")]
        [SerializeField] [Min(0.5f)] private float _openingMaxWidth = 2.2f;

        [Tooltip("Minimum distance from each boundary end to the nearest opening edge, in metres.")]
        [SerializeField] [Min(0f)] private float _openingEdgeMargin = 0.8f;

        [Header("Perimeter")]
        [Tooltip("Spawn solid walls along all four chunk edges. Disable when ChunkManager stitches neighbours.")]
        [SerializeField] private bool _addPerimeterWalls = true;

        [Header("Lights")]
        [Tooltip("FluorescentPanel prefab from Assets/_AfterAll/Prefabs/Backrooms_Modular/FluorescentPanel.prefab")]
        [SerializeField] private GameObject _lightPanelPrefab;

        [Tooltip("Grid spacing between ceiling lights in metres. Backrooms feel: ~3.6m.")]
        [SerializeField] [Min(1f)] private float _lightSpacing = 3.6f;

        [Tooltip("Probability that a light grid position is left dark (0 = all lit, 1 = all dark).")]
        [SerializeField] [Range(0f, 0.9f)] private float _lightDarkChance = 0.15f;

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

        public int MinOpeningsPerBoundary  => _minOpeningsPerBoundary;
        public int MaxOpeningsPerBoundary  => _maxOpeningsPerBoundary;
        public float OpeningMinWidth       => _openingMinWidth;
        public float OpeningMaxWidth       => _openingMaxWidth;
        public float OpeningEdgeMargin     => _openingEdgeMargin;

        public bool AddPerimeterWalls      => _addPerimeterWalls;

        public GameObject LightPanelPrefab => _lightPanelPrefab;
        public float LightSpacing          => _lightSpacing;
        public float LightDarkChance       => _lightDarkChance;
    }
}
