using System;
using System.Collections.Generic;

namespace AfterAll.Generation.FloorPlan
{
  public sealed class FloorPlanRegion
  {
    public int Id;
    public RegionType Type;
    public List<(int x, int y)> Cells = new();
    public int PillarCount;
    public int MinX, MinY, MaxX, MaxY;

    public int Width => MaxX - MinX + 1;
    public int Height => MaxY - MinY + 1;
    public int Area => Cells.Count;

    public float AspectRatio
    {
      get
      {
        int w = Math.Max(1, Width);
        int h = Math.Max(1, Height);
        return w > h ? (float)w / h : (float)h / w;
      }
    }
  }

  public sealed class FloorPlanResult
  {
    public FloorPlanGrid Grid;
    public IReadOnlyList<FloorPlanRegion> Regions;
    public int Seed;
    public int ChunkX;
    public int ChunkZ;
    public float FloorPercent;
    public int RegionCount;
    public bool IsFullyConnected;
    public int StampPlacements;
    public int ConnectivityPunches;
  }
}
