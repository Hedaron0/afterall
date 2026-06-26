namespace AfterAll.Generation.FloorPlan
{
  /// <summary>
  /// Column at a Pillar grid cell centre (chunk-local metres).
  /// </summary>
  public readonly struct PillarBlockSpec
  {
    public readonly int CellX;
    public readonly int CellY;
    public readonly float CenterX;
    public readonly float CenterZ;

    public PillarBlockSpec(int cellX, int cellY, float cellSize)
    {
      CellX = cellX;
      CellY = cellY;
      CenterX = (cellX + 0.5f) * cellSize;
      CenterZ = (cellY + 0.5f) * cellSize;
    }
  }
}
