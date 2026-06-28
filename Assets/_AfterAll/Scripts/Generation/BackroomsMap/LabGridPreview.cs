using System.Collections.Generic;

namespace AfterAll.Generation.BackroomsMap
{
    /// <summary>
    /// Merges multiple chunks for the Backrooms Lab top-down view.
    /// </summary>
    public sealed class LabGridPreview
    {
        public int CenterChunkX { get; private set; }
        public int CenterChunkZ { get; private set; }
        public int Radius { get; private set; }
        public int ChunkSize { get; private set; }
        public float ChunkWorldMetres { get; private set; }
        public float CellWorldSize { get; private set; }
        public readonly Dictionary<(int x, int z), ChunkData> Chunks = new();

        public int ChunksPerSide => Radius * 2 + 1;
        public int MergedWidth => ChunkSize * ChunksPerSide;
        public int MergedHeight => ChunkSize * ChunksPerSide;

        public ChunkData CenterChunk => Chunks[(CenterChunkX, CenterChunkZ)];

        public static LabGridPreview Build(BackroomsMapConfig config, int centerChunkX, int centerChunkZ, int radius)
        {
            var preview = new LabGridPreview
            {
                CenterChunkX = centerChunkX,
                CenterChunkZ = centerChunkZ,
                Radius = radius,
                ChunkSize = config.ChunkSize,
                ChunkWorldMetres = config.ChunkSizeMetres,
                CellWorldSize = config.CellWorldSize
            };

            for (int dz = -radius; dz <= radius; dz++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    int cx = centerChunkX + dx;
                    int cz = centerChunkZ + dz;
                    preview.Chunks[(cx, cz)] = BackroomsMapGenerator.Generate(config, cx, cz);
                }
            }

            return preview;
        }

        public CellType[,] BuildMergedCells()
        {
            int w = MergedWidth;
            int h = MergedHeight;
            var merged = new CellType[h, w];

            foreach (var kv in Chunks)
            {
                var (cx, cz) = kv.Key;
                var cells = kv.Value.Cells;
                int ox = (cx - (CenterChunkX - Radius)) * ChunkSize;
                int oy = (cz - (CenterChunkZ - Radius)) * ChunkSize;

                for (int y = 0; y < ChunkSize; y++)
                for (int x = 0; x < ChunkSize; x++)
                    merged[oy + y, ox + x] = cells[y, x];
            }

            return merged;
        }

        public bool TryGetChunkAtMergedCell(int mergedX, int mergedY, out int chunkX, out int chunkZ)
        {
            if (mergedX < 0 || mergedY < 0 || mergedX >= MergedWidth || mergedY >= MergedHeight)
            {
                chunkX = chunkZ = 0;
                return false;
            }

            int localChunkX = mergedX / ChunkSize;
            int localChunkZ = mergedY / ChunkSize;
            chunkX = CenterChunkX - Radius + localChunkX;
            chunkZ = CenterChunkZ - Radius + localChunkZ;
            return true;
        }

        public ChunkData GetChunk(int chunkX, int chunkZ) =>
            Chunks.TryGetValue((chunkX, chunkZ), out var data) ? data : null;
    }
}
