using System.Collections.Generic;
using UnityEngine;

namespace AfterAll.Generation.FloorPlan
{
  /// <summary>
  /// Seed-driven ceiling light grid on walkable floor cells.
  /// World-space anchor aligns lights across streamed chunks.
  /// </summary>
  public static class LightPlacementPass
  {
    public static List<LightCellSpec> Apply(FloorPlanGrid grid, FloorPlanConfig config, Rng rng)
    {
      var result = new List<LightCellSpec>(128);
      float spacing = config.LightSpacing;
      float anchorX = config.LightGridOffsetX;
      float anchorZ = config.LightGridOffsetZ;
      float inset = config.LightRoomInsetCells;
      float chunkW = grid.Width * grid.CellSize;
      float chunkH = grid.Height * grid.CellSize;
      float chunkOriginX = grid.OriginChunkX * config.ChunkSizeMetres;
      float chunkOriginZ = grid.OriginChunkZ * config.ChunkSizeMetres;

      int nMin = Mathf.CeilToInt((chunkOriginX - anchorX) / spacing);
      int nMax = Mathf.FloorToInt((chunkOriginX + chunkW - anchorX) / spacing);
      int mMin = Mathf.CeilToInt((chunkOriginZ - anchorZ) / spacing);
      int mMax = Mathf.FloorToInt((chunkOriginZ + chunkH - anchorZ) / spacing);

      for (int n = nMin; n <= nMax; n++)
      {
        for (int m = mMin; m <= mMax; m++)
        {
          if (rng.Chance(config.LightDarkChance))
            continue;

          float worldX = anchorX + n * spacing;
          float worldZ = anchorZ + m * spacing;
          float localX = worldX - chunkOriginX;
          float localZ = worldZ - chunkOriginZ;

          if (localX < 0f || localX >= chunkW || localZ < 0f || localZ >= chunkH)
            continue;

          if (!TrySnapToFloorCell(grid, localX, localZ, inset, out int cx, out int cy))
            continue;

          result.Add(new LightCellSpec(cx, cy, localX, localZ));
        }
      }

      return result;
    }

    private static bool TrySnapToFloorCell(
      FloorPlanGrid grid, float localX, float localZ, float insetCells,
      out int cellX, out int cellY)
    {
      cellX = Mathf.FloorToInt(localX / grid.CellSize);
      cellY = Mathf.FloorToInt(localZ / grid.CellSize);

      if (!grid.InBounds(cellX, cellY))
        return false;

      if (grid.Get(cellX, cellY) != CellState.Floor)
        return false;

      if (!HasWallClearance(grid, cellX, cellY, insetCells))
        return false;

      return true;
    }

    private static bool HasWallClearance(FloorPlanGrid grid, int cx, int cy, float insetCells)
    {
      int radius = Mathf.CeilToInt(insetCells);
      for (int dy = -radius; dy <= radius; dy++)
      {
        for (int dx = -radius; dx <= radius; dx++)
        {
          int nx = cx + dx;
          int ny = cy + dy;
          if (!grid.InBounds(nx, ny))
            return false;

          if (grid.Get(nx, ny) == CellState.Wall)
          {
            float dist = Mathf.Sqrt(dx * dx + dy * dy);
            if (dist < insetCells)
              return false;
          }
        }
      }

      return true;
    }
  }
}
