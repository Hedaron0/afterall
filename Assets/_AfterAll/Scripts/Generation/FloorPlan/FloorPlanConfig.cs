using System;
using System.Collections.Generic;
using UnityEngine;

namespace AfterAll.Generation.FloorPlan
{
  public enum RegionType
  {
    Unknown,
    Corridor,
    Hall,
    PillarField,
    PolygonPocket,
    Closet,
  }

  public enum BorderMergeMode
  {
    LowerSeedWins,
    HigherSeedWins,
    PreferFloor,
  }

  /// <summary>
  /// All knobs for the hybrid-chaotic Backrooms floor-plan generator.
  /// Drives Floor Plan Lab preview and 3D chunk spawning.
  /// </summary>
  [CreateAssetMenu(fileName = "FloorPlanConfig", menuName = "AfterAll/Generation/Floor Plan Config")]
  public sealed class FloorPlanConfig : ScriptableObject
  {
    [Header("World")]
    [SerializeField] private int _worldSeed = 1;

    [Tooltip("Metres per grid cell.")]
    [SerializeField] [Min(0.25f)] private float _cellSize = 0.5f;

    [Tooltip("Cells per chunk edge (64 × 0.5 m = 32 m chunk).")]
    [SerializeField] [Range(16, 128)] private int _chunkCells = 64;

    [Header("Maze Overlay")]
    [Tooltip("How many independent Prim maze passes to stack (reference uses ~1000; tune down for perf).")]
    [SerializeField] [Range(1, 500)] private int _mazeOverlayCount = 80;

    [Tooltip("Target fraction of cells that are walkable after maze passes.")]
    [SerializeField] [Range(0.35f, 0.95f)] private float _mazeFillTarget = 0.62f;

    [Tooltip("When a maze step would collide with existing floor, probability to stop expanding.")]
    [SerializeField] [Range(0f, 1f)] private float _mazeCollisionStopProbability = 0.45f;

    [Header("Room Stamps")]
    [SerializeField] private List<StampPoolEntry> _stampPool = new();

    [Tooltip("Extra stamp placement attempts per chunk.")]
    [SerializeField] [Range(0, 24)] private int _stampPlacementAttempts = 8;

    [SerializeField] [Range(0f, 2f)] private float _globalStampWeightMultiplier = 1f;

    [Header("Connectivity")]
    [SerializeField] private bool _autoPunchGaps = true;
    [SerializeField] [Range(1, 16)] private int _maxConnectivityPunches = 6;

    [Header("Border Stitching")]
    [SerializeField] private BorderMergeMode _borderMergeMode = BorderMergeMode.LowerSeedWins;

    [Header("3D Geometry")]
    [SerializeField] [Min(0.1f)] private float _wallHeight = 2.7f;
    [SerializeField] [Min(0.05f)] private float _slabThickness = 0.4f;
    [SerializeField] [Min(0.1f)] private float _pillarFootprint = 0.4f;

    [Tooltip("Optional cube prefab with wall material. If empty, a primitive cube is created.")]
    [SerializeField] private GameObject _wallBlockPrefab;

    [Tooltip("Optional floor slab prefab. If empty, primitive cube + FloorMaterial.")]
    [SerializeField] private GameObject _floorPrefab;

    [Tooltip("Optional ceiling slab prefab. If empty, primitive cube + CeilingMaterial.")]
    [SerializeField] private GameObject _ceilingPrefab;

    [Tooltip("Optional pillar prefab. If empty, primitive cube + WallMaterial.")]
    [SerializeField] private GameObject _pillarPrefab;

    [SerializeField] private Material _wallMaterial;
    [SerializeField] private Material _floorMaterial;
    [SerializeField] private Material _ceilingMaterial;

    [Header("Lights")]
    [SerializeField] private GameObject _lightPanelPrefab;
    [SerializeField] [Range(0f, 0.9f)] private float _lightDarkChance = 0.15f;
    [SerializeField] [Min(0.1f)] private float _lightRoomInset = 0.8f;
    [SerializeField] [Min(0.1f)] private float _ceilingTileSize = 1.327f;
    [SerializeField] [Range(1, 8)] private int _lightSpacingTiles = 3;
    [SerializeField] private float _lightGridOffsetX = 0.221f;
    [SerializeField] private float _lightGridOffsetZ = 0.483f;

    [Header("Streaming")]
    [SerializeField] [Range(1, 4)] private int _loadRadius = 1;

    [Header("Lab Preview")]
    [SerializeField] private bool _showRegions = true;
    [SerializeField] private bool _showChunkGrid = true;
    [SerializeField] private bool _showLightsLayer = true;
    [SerializeField] [Range(0, 2)] private int _chunkPreviewRadius = 0;

    [Header("Golden Seeds")]
    [SerializeField] private List<int> _goldenSeeds = new();

    public int WorldSeed => _worldSeed;
    public float CellSize => _cellSize;
    public int ChunkCells => _chunkCells;
    public float ChunkSizeMetres => _chunkCells * _cellSize;
    public int MazeOverlayCount => _mazeOverlayCount;
    public float MazeFillTarget => _mazeFillTarget;
    public float MazeCollisionStopProbability => _mazeCollisionStopProbability;
    public IReadOnlyList<StampPoolEntry> StampPool => _stampPool;
    public int StampPlacementAttempts => _stampPlacementAttempts;
    public float GlobalStampWeightMultiplier => _globalStampWeightMultiplier;
    public bool AutoPunchGaps => _autoPunchGaps;
    public int MaxConnectivityPunches => _maxConnectivityPunches;
    public BorderMergeMode BorderMerge => _borderMergeMode;
    public float WallHeight => _wallHeight;
    public float SlabThickness => _slabThickness;
    public float PillarFootprint => _pillarFootprint;
    public GameObject WallBlockPrefab => _wallBlockPrefab;
    public GameObject FloorPrefab => _floorPrefab;
    public GameObject CeilingPrefab => _ceilingPrefab;
    public GameObject PillarPrefab => _pillarPrefab;
    public Material WallMaterial => _wallMaterial;
    public Material FloorMaterial => _floorMaterial;
    public Material CeilingMaterial => _ceilingMaterial;
    public GameObject LightPanelPrefab => _lightPanelPrefab;
    public float LightDarkChance => _lightDarkChance;
    public float LightRoomInset => _lightRoomInset;
    public float LightRoomInsetCells => _lightRoomInset / _cellSize;
    public float CeilingTileSize => _ceilingTileSize;
    public int LightSpacingTiles => _lightSpacingTiles;
    public float LightSpacing => _ceilingTileSize * _lightSpacingTiles;
    public float LightGridOffsetX => _lightGridOffsetX;
    public float LightGridOffsetZ => _lightGridOffsetZ;
    public int LoadRadius => _loadRadius;
    public bool ShowRegions => _showRegions;
    public bool ShowChunkGrid => _showChunkGrid;
    public bool ShowLightsLayer => _showLightsLayer;
    public int ChunkPreviewRadius => _chunkPreviewRadius;
    public IReadOnlyList<int> GoldenSeeds => _goldenSeeds;

    public void SetWorldSeed(int seed) => _worldSeed = seed;

    public void AddGoldenSeed(int seed)
    {
      if (!_goldenSeeds.Contains(seed))
        _goldenSeeds.Add(seed);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
      _lightRoomInset = Mathf.Max(0.1f, _lightRoomInset);
    }
#endif
  }
}
