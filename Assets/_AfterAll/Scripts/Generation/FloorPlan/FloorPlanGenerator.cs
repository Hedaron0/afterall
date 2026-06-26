using System.Collections.Generic;

namespace AfterAll.Generation.FloorPlan
{
  public static class FloorPlanGenerator
  {
    public static FloorPlanResult Generate(FloorPlanConfig config, int chunkX = 0, int chunkZ = 0, int? seedOverride = null)
    {
      int seed = seedOverride ?? DeriveChunkSeed(config.WorldSeed, chunkX, chunkZ);
      var rng = new Rng(seed);

      var grid = FloorPlanGrid.ForSingleChunk(config, chunkX, chunkZ);

      MazeOverlayPass.Apply(grid, config, rng.Derive(1));
      int stamps = StampCarvePass.Apply(grid, config, rng.Derive(2));
      int punches = ConnectivityPass.Apply(grid, config, rng.Derive(3));
      var regions = RegionClassifier.Classify(grid);
      var walls = WallExtractor.Extract(grid);
      var pillars = WallExtractor.ExtractPillars(grid);
      var lights = LightPlacementPass.Apply(grid, config, rng.Derive(4));

      return new FloorPlanResult
      {
        Grid = grid,
        Regions = regions,
        WallBlocks = walls,
        Pillars = pillars,
        Lights = lights,
        Seed = seed,
        ChunkX = chunkX,
        ChunkZ = chunkZ,
        FloorPercent = grid.FloorFraction(),
        RegionCount = regions.Count,
        IsFullyConnected = ConnectivityPass.IsFullyConnected(grid),
        StampPlacements = stamps,
        ConnectivityPunches = punches,
      };
    }

    public static FloorPlanResult GeneratePreview(FloorPlanConfig config, int radius, int? seedOverride = null)
    {
      int baseSeed = seedOverride ?? config.WorldSeed;
      int side = radius * 2 + 1;
      var chunkGrids = new FloorPlanGrid[side, side];
      var chunkSeeds = new int[side, side];

      for (int cz = -radius; cz <= radius; cz++)
      {
        for (int cx = -radius; cx <= radius; cx++)
        {
          int ix = cx + radius;
          int iz = cz + radius;
          int seed = DeriveChunkSeed(baseSeed, cx, cz);
          chunkSeeds[ix, iz] = seed;

          var rng = new Rng(seed);
          var grid = FloorPlanGrid.ForSingleChunk(config, cx, cz);
          MazeOverlayPass.Apply(grid, config, rng.Derive(1));
          StampCarvePass.Apply(grid, config, rng.Derive(2));
          ConnectivityPass.Apply(grid, config, rng.Derive(3));
          chunkGrids[ix, iz] = grid;
        }
      }

      for (int iz = 0; iz < side; iz++)
      {
        for (int ix = 0; ix < side - 1; ix++)
        {
          BorderStitchPass.StitchHorizontal(
            chunkGrids[ix, iz], chunkGrids[ix + 1, iz],
            config, chunkSeeds[ix, iz], chunkSeeds[ix + 1, iz]);
        }
      }

      for (int ix = 0; ix < side; ix++)
      {
        for (int iz = 0; iz < side - 1; iz++)
        {
          BorderStitchPass.StitchVertical(
            chunkGrids[ix, iz], chunkGrids[ix, iz + 1],
            config, chunkSeeds[ix, iz], chunkSeeds[ix, iz + 1]);
        }
      }

      var merged = FloorPlanGrid.ForChunkPreview(config, radius);
      int n = config.ChunkCells;
      CopyChunkIntoMerged(merged, chunkGrids, radius, n);

      var regions = RegionClassifier.Classify(merged);
      var walls = WallExtractor.Extract(merged);
      var pillars = WallExtractor.ExtractPillars(merged);
      var lights = LightPlacementPass.Apply(merged, config, new Rng(baseSeed).Derive(99));

      return new FloorPlanResult
      {
        Grid = merged,
        Regions = regions,
        WallBlocks = walls,
        Pillars = pillars,
        Lights = lights,
        Seed = baseSeed,
        ChunkX = 0,
        ChunkZ = 0,
        FloorPercent = merged.FloorFraction(),
        RegionCount = regions.Count,
        IsFullyConnected = ConnectivityPass.IsFullyConnected(merged),
        StampPlacements = 0,
        ConnectivityPunches = 0,
      };
    }

    public static int DeriveChunkSeed(int worldSeed, int chunkX, int chunkZ) =>
      new Rng(worldSeed).Derive(chunkX, chunkZ).Seed;

    private static void CopyChunkIntoMerged(FloorPlanGrid merged, FloorPlanGrid[,] chunks, int radius, int n)
    {
      int side = radius * 2 + 1;
      for (int iz = 0; iz < side; iz++)
      {
        for (int ix = 0; ix < side; ix++)
        {
          var src = chunks[ix, iz];
          int ox = ix * n;
          int oz = iz * n;
          for (int y = 0; y < n; y++)
          {
            for (int x = 0; x < n; x++)
              merged.Set(ox + x, oz + y, src.Get(x, y));
          }
        }
      }
    }
  }
}
