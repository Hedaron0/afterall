using AfterAll.Generation;
using System.Collections.Generic;

namespace AfterAll.Generation.BackroomsMap
{
    /// <summary>
    /// Runtime-deterministic chunk generator (v3 steps 1–8 data; geometry via ChunkGeometrySpawner).
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

            bool layoutOnly = config.LayoutOnlyMode;
            var connectorPoints = layoutOnly
                ? new List<ConnectorPoint>()
                : EdgeConnectorPlanner.PlanForChunk(config, chunkX, chunkZ);
            var doorOpenings = new List<DoorOpeningSpec>();

            var cells = new CellType[size, size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                cells[y, x] = CellType.Wall;

            var zones = BspPartitioner.Partition(size, size, config.ZoneDepth, rng.Derive(1));
            var specs = ArchetypeAssigner.Assign(zones, config.VarietyLevel, rng.Derive(2));

            foreach (var spec in specs)
                ZoneCarver.CarveZone(cells, spec, config, rng.Derive(3 + spec.CenterX + spec.CenterY * size));

            float doorChance = layoutOnly ? 0f : config.DoorChance;
            ZoneConnector.ConnectZones(cells, specs, doorChance, rng.Derive(4), doorOpenings);

            if (!layoutOnly)
                ConnectorForceCarver.Apply(cells, connectorPoints, size);

            ExitSpec? exit = layoutOnly ? null : ExitPlacementPass.TryPlace(cells, config, rng.Derive(6));
            var accessibility = AccessibilityPass.EnsureConnected(cells, connectorPoints, rng.Derive(8));
            var lights = layoutOnly
                ? new List<(int x, int y)>()
                : LightPlacementPass.Place(cells, config, rng.Derive(7));

            return new ChunkData
            {
                ChunkX = chunkX,
                ChunkZ = chunkZ,
                Seed = seed,
                ZoneCount = specs.Count,
                Cells = cells,
                DoorOpenings = doorOpenings,
                ConnectorPoints = connectorPoints,
                Lights = lights,
                Exit = exit,
                AccessibilityCorridors = accessibility.IslandsFixed,
                AccessibilityWalled = accessibility.CellsWalled
            };
        }
    }
}
