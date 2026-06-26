using System.Collections.Generic;

namespace AfterAll.Generation.FloorPlan
{
  /// <summary>
  /// Stacks many Prim maze carves on a wall-filled grid (davidpcahill-style density).
  /// </summary>
  public static class MazeOverlayPass
  {
    public static void Apply(FloorPlanGrid grid, FloorPlanConfig config, Rng rng)
    {
      int cols = grid.Width;
      int rows = grid.Height;
      int targetWalkable = (int)(cols * rows * config.MazeFillTarget);

      grid.Fill(CellState.Wall);

      for (int pass = 0; pass < config.MazeOverlayCount; pass++)
      {
        if (grid.CountCells(c => c == CellState.Floor) >= targetWalkable)
          break;

        var passRng = rng.Derive(pass + 100);
        RunPrimOverlay(grid, cols, rows, config.MazeCollisionStopProbability, passRng);
      }
    }

    private static void RunPrimOverlay(FloorPlanGrid grid, int cols, int rows, float stopProb, Rng rng)
    {
      var visited = new HashSet<(int x, int y)>();
      int startX = rng.Range(0, cols);
      int startY = rng.Range(0, rows);
      visited.Add((startX, startY));

      var frontier = new List<(int x, int y)> { (startX, startY) };
      grid.Set(startX, startY, CellState.Floor);

      while (frontier.Count > 0)
      {
        int idx = rng.Range(0, frontier.Count);
        var (x, y) = frontier[idx];
        frontier.RemoveAt(idx);

        var neighbors = new List<(int nx, int ny, int mx, int my)>();

        TryNeighbor(x, y, x - 2, y, x - 1, y, cols, rows, visited, neighbors);
        TryNeighbor(x, y, x + 2, y, x + 1, y, cols, rows, visited, neighbors);
        TryNeighbor(x, y, x, y - 2, x, y - 1, cols, rows, visited, neighbors);
        TryNeighbor(x, y, x, y + 2, x, y + 1, cols, rows, visited, neighbors);

        if (neighbors.Count == 0)
          continue;

        var pick = neighbors[rng.Range(0, neighbors.Count)];
        int nx = pick.nx, ny = pick.ny, mx = pick.mx, my = pick.my;

        bool midIsFloor = grid.Get(mx, my) == CellState.Floor;
        if (midIsFloor && rng.Chance(stopProb))
          continue;

        grid.Set(nx, ny, CellState.Floor);
        grid.Set(mx, my, CellState.Floor);
        visited.Add((nx, ny));
        frontier.Add((nx, ny));
      }
    }

    private static void TryNeighbor(
      int x, int y, int nx, int ny, int mx, int my,
      int cols, int rows,
      HashSet<(int x, int y)> visited,
      List<(int nx, int ny, int mx, int my)> neighbors)
    {
      if (nx < 0 || ny < 0 || nx >= cols || ny >= rows)
        return;
      if (visited.Contains((nx, ny)))
        return;
      neighbors.Add((nx, ny, mx, my));
    }
  }
}
