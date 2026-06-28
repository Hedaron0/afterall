using System.Collections.Generic;

namespace AfterAll.Generation.BackroomsMap
{
    public sealed class ChunkData
    {
        public int ChunkX;
        public int ChunkZ;
        public int Seed;
        public int ZoneCount;

        public CellType[,] Cells;
        public List<DoorOpeningSpec> DoorOpenings = new();
        public List<ConnectorPoint> ConnectorPoints = new();
        public List<(int x, int y)> Lights = new();
        public ExitSpec? Exit;
        public int AccessibilityCorridors;
        public int AccessibilityWalled;

        public int Width => Cells?.GetLength(1) ?? 0;
        public int Height => Cells?.GetLength(0) ?? 0;

        public float FloorFraction()
        {
            if (Cells == null) return 0f;

            int walkable = 0;
            int total = Width * Height;
            for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                if (Cells[y, x].IsWalkable())
                    walkable++;
            }

            return total > 0 ? walkable / (float)total : 0f;
        }
    }
}
