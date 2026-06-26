using System;
using System.Collections.Generic;
using UnityEngine;

namespace AfterAll.Generation.FloorPlan
{
  /// <summary>
  /// Places authorable RoomStampDefinition footprints on the grid.
  /// </summary>
  public static class StampCarvePass
  {
    public static int Apply(FloorPlanGrid grid, FloorPlanConfig config, Rng rng)
    {
      if (config.StampPool == null || config.StampPool.Count == 0)
        return 0;

      var placements = new List<(int cx, int cy)>();
      var perStampCount = new Dictionary<RoomStampDefinition, int>();
      int placed = 0;

      for (int attempt = 0; attempt < config.StampPlacementAttempts; attempt++)
      {
        var stamp = PickStamp(config, rng.Derive(attempt + 500));
        if (stamp == null)
          continue;

        perStampCount.TryGetValue(stamp, out int count);
        if (count >= stamp.MaxPerChunk)
          continue;

        int w = rng.Range(stamp.SizeMinCells.x, stamp.SizeMaxCells.x + 1);
        int h = rng.Range(stamp.SizeMinCells.y, stamp.SizeMaxCells.y + 1);
        w = Mathf.Clamp(w, 2, grid.Width - 2);
        h = Mathf.Clamp(h, 2, grid.Height - 2);

        int x = rng.Range(1, grid.Width - w - 1);
        int y = rng.Range(1, grid.Height - h - 1);

        if (!IsFarEnough(x + w / 2, y + h / 2, placements, stamp.MinDistanceCells))
          continue;

        if (!StampFits(grid, stamp, x, y, w, h, rng))
          continue;

        placements.Add((x + w / 2, y + h / 2));
        perStampCount[stamp] = count + 1;
        placed++;
      }

      return placed;
    }

    private static RoomStampDefinition PickStamp(FloorPlanConfig config, Rng rng)
    {
      float total = 0f;
      foreach (var entry in config.StampPool)
      {
        if (entry.Stamp == null) continue;
        total += entry.Stamp.SpawnWeight * entry.WeightMultiplier * config.GlobalStampWeightMultiplier;
      }

      if (total <= 0f) return null;

      float roll = rng.Value() * total;
      foreach (var entry in config.StampPool)
      {
        if (entry.Stamp == null) continue;
        float w = entry.Stamp.SpawnWeight * entry.WeightMultiplier * config.GlobalStampWeightMultiplier;
        roll -= w;
        if (roll <= 0f)
          return entry.Stamp;
      }

      return config.StampPool[config.StampPool.Count - 1].Stamp;
    }

    private static bool IsFarEnough(int cx, int cy, List<(int cx, int cy)> placements, int minDist)
    {
      foreach (var p in placements)
      {
        int dx = cx - p.cx;
        int dy = cy - p.cy;
        if (dx * dx + dy * dy < minDist * minDist)
          return false;
      }
      return true;
    }

    private static bool StampFits(FloorPlanGrid grid, RoomStampDefinition stamp, int x, int y, int w, int h, Rng rng)
    {
      switch (stamp.Shape)
      {
        case RoomStampShape.Rectangle:
          CarveRect(grid, x, y, w, h, CellState.Floor);
          return true;

        case RoomStampShape.Ellipse:
          CarveEllipse(grid, x, y, w, h, CellState.Floor);
          return true;

        case RoomStampShape.Polygon:
          CarvePolygon(grid, x + w / 2, y + h / 2, Mathf.Min(w, h) / 2, stamp.PolygonSides, CellState.Floor);
          return true;

        case RoomStampShape.PillarGrid:
          CarveRect(grid, x, y, w, h, CellState.Floor);
          int spacing = rng.Range(stamp.PillarSpacingMin, stamp.PillarSpacingMax + 1);
          for (int py = y; py < y + h; py += spacing)
          {
            for (int px = x; px < x + w; px += spacing)
            {
              if (grid.InBounds(px, py))
                grid.Set(px, py, CellState.Pillar);
            }
          }
          return true;

        default:
          return false;
      }
    }

    private static void CarveRect(FloorPlanGrid grid, int x, int y, int w, int h, CellState state)
    {
      for (int py = y; py < y + h; py++)
      {
        for (int px = x; px < x + w; px++)
        {
          if (grid.InBounds(px, py))
            grid.Set(px, py, state);
        }
      }
    }

    private static void CarveEllipse(FloorPlanGrid grid, int x, int y, int w, int h, CellState state)
    {
      float cx = x + w * 0.5f;
      float cy = y + h * 0.5f;
      float rx = w * 0.5f;
      float ry = h * 0.5f;

      for (int py = y; py < y + h; py++)
      {
        for (int px = x; px < x + w; px++)
        {
          float dx = (px - cx) / rx;
          float dy = (py - cy) / ry;
          if (dx * dx + dy * dy <= 1f && grid.InBounds(px, py))
            grid.Set(px, py, state);
        }
      }
    }

    private static void CarvePolygon(FloorPlanGrid grid, int cx, int cy, int radius, int sides, CellState state)
    {
      var verts = new (float x, float y)[sides];
      float step = (float)(Math.PI * 2 / sides);
      for (int i = 0; i < sides; i++)
      {
        float a = i * step;
        verts[i] = (cx + radius * MathF.Cos(a), cy + radius * MathF.Sin(a));
      }

      int minX = Mathf.Max(0, cx - radius);
      int maxX = Mathf.Min(grid.Width - 1, cx + radius);
      int minY = Mathf.Max(0, cy - radius);
      int maxY = Mathf.Min(grid.Height - 1, cy + radius);

      for (int py = minY; py <= maxY; py++)
      {
        for (int px = minX; px <= maxX; px++)
        {
          if (PointInPolygon(px, py, verts))
            grid.Set(px, py, state);
        }
      }
    }

    private static bool PointInPolygon(float x, float y, (float x, float y)[] verts)
    {
      bool inside = false;
      int n = verts.Length;
      for (int i = 0, j = n - 1; i < n; j = i++)
      {
        var vi = verts[i];
        var vj = verts[j];
        if ((vi.y > y) != (vj.y > y) &&
            x < (vj.x - vi.x) * (y - vi.y) / (vj.y - vi.y + 1e-6f) + vi.x)
          inside = !inside;
      }
      return inside;
    }
  }
}
