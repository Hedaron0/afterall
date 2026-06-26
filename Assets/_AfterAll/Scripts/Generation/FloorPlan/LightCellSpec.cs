namespace AfterAll.Generation.FloorPlan
{
  /// <summary>
  /// Ceiling light position derived from the floor grid (chunk-local metres).
  /// </summary>
  public readonly struct LightCellSpec
  {
    public readonly int CellX;
    public readonly int CellY;
    public readonly float LocalX;
    public readonly float LocalZ;

    public LightCellSpec(int cellX, int cellY, float localX, float localZ)
    {
      CellX = cellX;
      CellY = cellY;
      LocalX = localX;
      LocalZ = localZ;
    }
  }
}
