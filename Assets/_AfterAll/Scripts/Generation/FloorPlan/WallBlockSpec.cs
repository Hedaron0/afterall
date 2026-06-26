namespace AfterAll.Generation.FloorPlan
{
  /// <summary>
  /// Axis-aligned wall volume from greedy rectangle merge on Wall cells.
  /// Positions are in chunk-local metres (origin = chunk min corner).
  /// </summary>
  public readonly struct WallBlockSpec
  {
    public readonly int CellX;
    public readonly int CellY;
    public readonly int CellWidth;
    public readonly int CellHeight;
    public readonly float MinX;
    public readonly float MinZ;
    public readonly float MaxX;
    public readonly float MaxZ;

    public WallBlockSpec(int cellX, int cellY, int cellWidth, int cellHeight, float cellSize)
    {
      CellX = cellX;
      CellY = cellY;
      CellWidth = cellWidth;
      CellHeight = cellHeight;
      MinX = cellX * cellSize;
      MinZ = cellY * cellSize;
      MaxX = (cellX + cellWidth) * cellSize;
      MaxZ = (cellY + cellHeight) * cellSize;
    }

    public float WidthMetres => MaxX - MinX;
    public float DepthMetres => MaxZ - MinZ;
    public float CenterX => (MinX + MaxX) * 0.5f;
    public float CenterZ => (MinZ + MaxZ) * 0.5f;
  }
}
