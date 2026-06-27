namespace AfterAll.Generation.BackroomsMap
{
    /// <summary>
    /// Runtime-deterministic single-chunk generator (v3 steps 1–2).
    /// Vents, doors, exits, BFS lights, and connectors land in later passes.
    /// </summary>
    public static class BackroomsMapGenerator
    {
        public static int DeriveChunkSeed(int worldSeed, int chunkX, int chunkZ)
        {
            unchecked
            {
                int h = worldSeed;
                h = h * 31 + chunkX;
                h = h * 31 + chunkZ;
                h ^= h >> 16;
                h *= 0x45d9f3b7;
                h ^= h >> 16;
                return h;
            }
        }

        public static ChunkData Generate(BackroomsMapConfig config, int chunkX = 0, int chunkZ = 0, int? seedOverride = null)
        {
            int seed = seedOverride ?? DeriveChunkSeed(config.WorldSeed, chunkX, chunkZ);
            var rng = new Rng(seed);

            int size = config.ChunkSize;
            var cells = new CellType[size, size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                cells[y, x] = CellType.Wall;

            var zones = BspPartitioner.Partition(size, size, config.ZoneDepth, rng.Derive(1));
            var specs = ArchetypeAssigner.Assign(zones, config.VarietyLevel, rng.Derive(2));

            foreach (var spec in specs)
                ZoneCarver.CarveZone(cells, spec, rng.Derive(3 + spec.CenterX + spec.CenterY * size));

            ZoneConnector.ConnectZones(cells, specs, rng.Derive(4));

            return new ChunkData
            {
                ChunkX = chunkX,
                ChunkZ = chunkZ,
                Seed = seed,
                ZoneCount = specs.Count,
                Cells = cells
            };
        }
    }
}
