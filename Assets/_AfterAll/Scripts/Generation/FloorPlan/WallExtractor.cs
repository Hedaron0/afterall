using System.Collections.Generic;

namespace AfterAll.Generation.FloorPlan
{
  /// <summary>
  /// Greedy rectangle merge on Wall cells — each cell belongs to exactly one block.
  /// </summary>
  public static class WallExtractor
  {
    public static List<WallBlockSpec> Extract(FloorPlanGrid grid)
    {
      int w = grid.Width;
      int h = grid.Height;
      var visited = new bool[w * h];
      var result = new List<WallBlockSpec>(256);
      float cell = grid.CellSize;

      for (int y = 0; y < h; y++)
      {
        for (int x = 0; x < w; x++)
        {
          int idx = y * w + x;
          if (visited[idx] || grid.Get(x, y) != CellState.Wall)
            continue;

          int width = 0;
          while (x + width < w &&
                 grid.Get(x + width, y) == CellState.Wall &&
                 !visited[y * w + x + width])
            width++;

          int height = 1;
          bool canGrow = true;
          while (canGrow && y + height < h)
          {
            for (int dx = 0; dx < width; dx++)
            {
              int check = (y + height) * w + x + dx;
              if (grid.Get(x + dx, y + height) != CellState.Wall || visited[check])
              {
                canGrow = false;
                break;
              }
            }

            if (canGrow)
              height++;
          }

          for (int dy = 0; dy < height; dy++)
          {
            for (int dx = 0; dx < width; dx++)
              visited[(y + dy) * w + x + dx] = true;
          }

          result.Add(new WallBlockSpec(x, y, width, height, cell));
        }
      }

      return result;
    }

    public static List<PillarBlockSpec> ExtractPillars(FloorPlanGrid grid)
    {
      var result = new List<PillarBlockSpec>(64);
      float cell = grid.CellSize;

      for (int y = 0; y < grid.Height; y++)
      {
        for (int x = 0; x < grid.Width; x++)
        {
          if (grid.Get(x, y) == CellState.Pillar)
            result.Add(new PillarBlockSpec(x, y, cell));
        }
      }

      return result;
    }
  }
}
