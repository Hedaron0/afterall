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
  /// Edited in the Floor Plan Lab and (later) drives 3D chunk spawning.
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

    [Header("Lab Preview")]
    [SerializeField] private bool _showRegions = true;
    [SerializeField] private bool _showChunkGrid = true;
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
    public bool ShowRegions => _showRegions;
    public bool ShowChunkGrid => _showChunkGrid;
    public int ChunkPreviewRadius => _chunkPreviewRadius;
    public IReadOnlyList<int> GoldenSeeds => _goldenSeeds;

    public void SetWorldSeed(int seed) => _worldSeed = seed;

    public void AddGoldenSeed(int seed)
    {
      if (!_goldenSeeds.Contains(seed))
        _goldenSeeds.Add(seed);
    }
  }
}
