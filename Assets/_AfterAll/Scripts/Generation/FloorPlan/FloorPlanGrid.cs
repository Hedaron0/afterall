using System;

namespace AfterAll.Generation.FloorPlan
{
  /// <summary>
  /// 2D cell field for one chunk (or stitched multi-chunk preview).
  /// </summary>
  public sealed class FloorPlanGrid
  {
    public readonly int Width;
    public readonly int Height;
    public readonly float CellSize;
    public readonly int OriginChunkX;
    public readonly int OriginChunkZ;
    public readonly int CellsPerChunk;

    private readonly CellState[] _cells;

    public FloorPlanGrid(int width, int height, float cellSize, int cellsPerChunk, int originChunkX = 0, int originChunkZ = 0)
    {
      Width = width;
      Height = height;
      CellSize = cellSize;
      CellsPerChunk = cellsPerChunk;
      OriginChunkX = originChunkX;
      OriginChunkZ = originChunkZ;
      _cells = new CellState[width * height];
    }

    public static FloorPlanGrid ForSingleChunk(FloorPlanConfig config, int chunkX = 0, int chunkZ = 0)
    {
      int n = config.ChunkCells;
      return new FloorPlanGrid(n, n, config.CellSize, n, chunkX, chunkZ);
    }

    public static FloorPlanGrid ForChunkPreview(FloorPlanConfig config, int radius)
    {
      int n = config.ChunkCells;
      int side = n * (radius * 2 + 1);
      return new FloorPlanGrid(side, side, config.CellSize, n, -radius, -radius);
    }

    public bool InBounds(int x, int y) => x >= 0 && x < Width && y >= 0 && y < Height;

    public CellState Get(int x, int y) => _cells[y * Width + x];

    public void Set(int x, int y, CellState state) => _cells[y * Width + x] = state;

    public bool IsWalkable(int x, int y)
    {
      if (!InBounds(x, y)) return false;
      var c = Get(x, y);
      return c == CellState.Floor || c == CellState.Pillar;
    }

    public int CountCells(Func<CellState, bool> predicate)
    {
      int count = 0;
      for (int i = 0; i < _cells.Length; i++)
      {
        if (predicate(_cells[i])) count++;
      }
      return count;
    }

    public float FloorFraction()
    {
      int walkable = CountCells(c => c == CellState.Floor || c == CellState.Pillar);
      return (float)walkable / _cells.Length;
    }

    public void Fill(CellState state)
    {
      for (int i = 0; i < _cells.Length; i++)
        _cells[i] = state;
    }

    public (int localX, int localY) WorldToLocalChunkCell(int worldCellX, int worldCellY)
    {
      int ox = OriginChunkX * CellsPerChunk;
      int oz = OriginChunkZ * CellsPerChunk;
      return (worldCellX - ox, worldCellY - oz);
    }

    public FloorPlanGrid Clone()
    {
      var copy = new FloorPlanGrid(Width, Height, CellSize, CellsPerChunk, OriginChunkX, OriginChunkZ);
      Array.Copy(_cells, copy._cells, _cells.Length);
      return copy;
    }
  }
}
