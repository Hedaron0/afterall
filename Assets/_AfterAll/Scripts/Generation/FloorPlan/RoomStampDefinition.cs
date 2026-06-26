using UnityEngine;

namespace AfterAll.Generation.FloorPlan
{
  public enum RoomStampShape
  {
    Rectangle,
    Ellipse,
    Polygon,
    PillarGrid,
  }

  public enum RoomStampTag
  {
    Hall,
    Closet,
    PillarField,
    PolygonPocket,
    Custom,
  }

  /// <summary>
  /// Authorable special room template — Harun defines size ranges, shape, and spawn weight.
  /// Used by StampCarvePass in the Floor Plan Lab and (later) 3D chunk generation.
  /// </summary>
  [CreateAssetMenu(fileName = "RoomStamp", menuName = "AfterAll/Generation/Room Stamp")]
  public sealed class RoomStampDefinition : ScriptableObject
  {
    [Header("Identity")]
    [SerializeField] private string _displayName = "Wide Hall";
    [SerializeField] private RoomStampTag _tag = RoomStampTag.Hall;

    [Header("Shape")]
    [SerializeField] private RoomStampShape _shape = RoomStampShape.Rectangle;

    [Tooltip("Minimum footprint in cells (width, height).")]
    [SerializeField] private Vector2Int _sizeMinCells = new(8, 6);

    [Tooltip("Maximum footprint in cells (width, height).")]
    [SerializeField] private Vector2Int _sizeMaxCells = new(24, 18);

    [Tooltip("Polygon side count when Shape = Polygon.")]
    [SerializeField] [Range(3, 12)] private int _polygonSides = 6;

    [Header("Pillar Grid (Shape = PillarGrid)")]
    [SerializeField] [Range(2, 12)] private int _pillarSpacingMin = 2;
    [SerializeField] [Range(2, 12)] private int _pillarSpacingMax = 6;

    [Header("Spawn")]
    [SerializeField] [Min(0f)] private float _spawnWeight = 1f;
    [SerializeField] [Range(0, 8)] private int _maxPerChunk = 2;
    [SerializeField] [Range(0, 32)] private int _minDistanceCells = 4;

    [Header("Preview")]
    [SerializeField] private Color _previewTint = new(0.85f, 0.9f, 1f, 0.35f);

    public string DisplayName => _displayName;
    public RoomStampTag Tag => _tag;
    public RoomStampShape Shape => _shape;
    public Vector2Int SizeMinCells => _sizeMinCells;
    public Vector2Int SizeMaxCells => _sizeMaxCells;
    public int PolygonSides => _polygonSides;
    public int PillarSpacingMin => _pillarSpacingMin;
    public int PillarSpacingMax => _pillarSpacingMax;
    public float SpawnWeight => _spawnWeight;
    public int MaxPerChunk => _maxPerChunk;
    public int MinDistanceCells => _minDistanceCells;
    public Color PreviewTint => _previewTint;
  }
}
