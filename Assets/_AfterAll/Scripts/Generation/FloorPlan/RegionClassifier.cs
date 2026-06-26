using System.Collections.Generic;

namespace AfterAll.Generation.FloorPlan
{
  public static class RegionClassifier
  {
    public static List<FloorPlanRegion> Classify(FloorPlanGrid grid)
    {
      var visited = new bool[grid.Width * grid.Height];
      var regions = new List<FloorPlanRegion>();
      int id = 0;

      for (int y = 0; y < grid.Height; y++)
      {
        for (int x = 0; x < grid.Width; x++)
        {
          int idx = y * grid.Width + x;
          if (visited[idx] || !grid.IsWalkable(x, y))
            continue;

          var region = FloodRegion(grid, x, y, visited, id++);
          region.Type = ClassifyRegion(region);
          regions.Add(region);
        }
      }

      return regions;
    }

    private static FloorPlanRegion FloodRegion(FloorPlanGrid grid, int sx, int sy, bool[] visited, int id)
    {
      var region = new FloorPlanRegion { Id = id, MinX = sx, MinY = sy, MaxX = sx, MaxY = sy };
      var queue = new Queue<(int x, int y)>();
      queue.Enqueue((sx, sy));
      visited[sy * grid.Width + sx] = true;

      while (queue.Count > 0)
      {
        var (x, y) = queue.Dequeue();
        region.Cells.Add((x, y));

        if (x < region.MinX) region.MinX = x;
        if (y < region.MinY) region.MinY = y;
        if (x > region.MaxX) region.MaxX = x;
        if (y > region.MaxY) region.MaxY = y;

        var state = grid.Get(x, y);
        if (state == CellState.Pillar)
          region.PillarCount++;

        Enqueue(grid, x + 1, y, visited, queue);
        Enqueue(grid, x - 1, y, visited, queue);
        Enqueue(grid, x, y + 1, visited, queue);
        Enqueue(grid, x, y - 1, visited, queue);
      }

      return region;
    }

    private static void Enqueue(FloorPlanGrid grid, int x, int y, bool[] visited, Queue<(int x, int y)> queue)
    {
      if (!grid.InBounds(x, y)) return;
      int idx = y * grid.Width + x;
      if (visited[idx] || !grid.IsWalkable(x, y)) return;
      visited[idx] = true;
      queue.Enqueue((x, y));
    }

    private static RegionType ClassifyRegion(FloorPlanRegion region)
    {
      if (region.PillarCount > region.Area * 0.15f)
        return RegionType.PillarField;

      if (region.Area >= 120)
        return RegionType.Hall;

      if (region.AspectRatio >= 3.5f && region.Area < 80)
        return RegionType.Corridor;

      if (region.Area <= 12)
        return RegionType.Closet;

      return RegionType.Unknown;
    }
  }
}
