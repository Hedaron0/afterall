using System.Collections.Generic;

namespace AfterAll.Generation.FloorPlan
{
  public static class ConnectivityPass
  {
    public static int Apply(FloorPlanGrid grid, FloorPlanConfig config, Rng rng)
    {
      if (!config.AutoPunchGaps)
        return 0;

      var components = FindComponents(grid);
      if (components.Count <= 1)
        return 0;

      int punches = 0;
      var main = components[0];

      for (int i = 1; i < components.Count && punches < config.MaxConnectivityPunches; i++)
      {
        if (TryPunchBetween(grid, main, components[i], rng.Derive(i + 900)))
          punches++;
        else
          main = MergeComponentLists(main, components[i]);
      }

      return punches;
    }

    private static List<HashSet<(int x, int y)>> FindComponents(FloorPlanGrid grid)
    {
      var visited = new bool[grid.Width * grid.Height];
      var components = new List<HashSet<(int x, int y)>>();

      for (int y = 0; y < grid.Height; y++)
      {
        for (int x = 0; x < grid.Width; x++)
        {
          int idx = y * grid.Width + x;
          if (visited[idx] || !grid.IsWalkable(x, y))
            continue;

          var comp = new HashSet<(int x, int y)>();
          var queue = new Queue<(int x, int y)>();
          queue.Enqueue((x, y));
          visited[idx] = true;

          while (queue.Count > 0)
          {
            var (cx, cy) = queue.Dequeue();
            comp.Add((cx, cy));

            TryEnqueue(grid, cx + 1, cy, visited, queue);
            TryEnqueue(grid, cx - 1, cy, visited, queue);
            TryEnqueue(grid, cx, cy + 1, visited, queue);
            TryEnqueue(grid, cx, cy - 1, visited, queue);
          }

          components.Add(comp);
        }
      }

      components.Sort((a, b) => b.Count.CompareTo(a.Count));
      return components;
    }

    private static void TryEnqueue(FloorPlanGrid grid, int x, int y, bool[] visited, Queue<(int x, int y)> queue)
    {
      if (!grid.InBounds(x, y)) return;
      int idx = y * grid.Width + x;
      if (visited[idx] || !grid.IsWalkable(x, y)) return;
      visited[idx] = true;
      queue.Enqueue((x, y));
    }

    private static bool TryPunchBetween(
      FloorPlanGrid grid,
      HashSet<(int x, int y)> a,
      HashSet<(int x, int y)> b,
      Rng rng)
    {
      (int x, int y)? bestA = null;
      (int x, int y)? bestB = null;
      int bestDist = int.MaxValue;

      int samples = 0;
      foreach (var cellA in a)
      {
        if (samples++ > 40) break;
        foreach (var cellB in b)
        {
          int dx = cellA.x - cellB.x;
          int dy = cellA.y - cellB.y;
          int d = dx * dx + dy * dy;
          if (d < bestDist)
          {
            bestDist = d;
            bestA = cellA;
            bestB = cellB;
          }
        }
      }

      if (!bestA.HasValue || !bestB.HasValue)
        return false;

      CarveLine(grid, bestA.Value, bestB.Value);
      foreach (var c in b) a.Add(c);
      return true;
    }

    private static void CarveLine(FloorPlanGrid grid, (int x, int y) from, (int x, int y) to)
    {
      int x = from.x;
      int y = from.y;
      int dx = System.Math.Sign(to.x - from.x);
      int dy = System.Math.Sign(to.y - from.y);

      while (x != to.x || y != to.y)
      {
        if (grid.InBounds(x, y))
          grid.Set(x, y, CellState.Floor);
        if (x != to.x) x += dx;
        else if (y != to.y) y += dy;
      }

      if (grid.InBounds(to.x, to.y))
        grid.Set(to.x, to.y, CellState.Floor);
    }

    private static HashSet<(int x, int y)> MergeComponentLists(
      HashSet<(int x, int y)> a,
      HashSet<(int x, int y)> b)
    {
      foreach (var c in b) a.Add(c);
      return a;
    }

    public static bool IsFullyConnected(FloorPlanGrid grid) =>
      FindComponents(grid).Count <= 1;
  }
}
