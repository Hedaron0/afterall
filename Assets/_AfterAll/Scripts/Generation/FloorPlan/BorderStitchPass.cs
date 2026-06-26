namespace AfterAll.Generation.FloorPlan
{
  /// <summary>
  /// Merges shared edges between neighbouring chunk grids for multi-chunk preview.
  /// </summary>
  public static class BorderStitchPass
  {
    public static void StitchHorizontal(FloorPlanGrid left, FloorPlanGrid right, FloorPlanConfig config, int leftSeed, int rightSeed)
    {
      if (left.Height != right.Height)
        return;

      int edgeLeft = left.Width - 1;
      for (int y = 0; y < left.Height; y++)
      {
        var leftCell = left.Get(edgeLeft, y);
        var rightCell = right.Get(0, y);
        var merged = MergeCells(leftCell, rightCell, config.BorderMerge, leftSeed, rightSeed, y);
        left.Set(edgeLeft, y, merged);
        right.Set(0, y, merged);
      }
    }

    public static void StitchVertical(FloorPlanGrid top, FloorPlanGrid bottom, FloorPlanConfig config, int topSeed, int bottomSeed)
    {
      if (top.Width != bottom.Width)
        return;

      int edgeTop = top.Height - 1;
      for (int x = 0; x < top.Width; x++)
      {
        var topCell = top.Get(x, edgeTop);
        var bottomCell = bottom.Get(x, 0);
        var merged = MergeCells(topCell, bottomCell, config.BorderMerge, topSeed, bottomSeed, x);
        top.Set(x, edgeTop, merged);
        bottom.Set(x, 0, merged);
      }
    }

    private static CellState MergeCells(
      CellState a, CellState b,
      BorderMergeMode mode,
      int seedA, int seedB,
      int coord)
    {
      if (a == b) return a;

      switch (mode)
      {
        case BorderMergeMode.PreferFloor:
          if (IsWalkable(a) || IsWalkable(b))
            return CellState.Floor;
          return CellState.Wall;

        case BorderMergeMode.HigherSeedWins:
          return (seedA ^ coord) >= (seedB ^ coord) ? a : b;

        case BorderMergeMode.LowerSeedWins:
        default:
          return (seedA ^ coord) <= (seedB ^ coord) ? a : b;
      }
    }

    private static bool IsWalkable(CellState c) =>
      c == CellState.Floor || c == CellState.Pillar;
  }
}
