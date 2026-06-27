using System.Collections.Generic;
using AfterAll.Generation;

namespace AfterAll.Generation.BackroomsMap
{
    /// <summary>
    /// Plans inter-chunk edge connectors before interior generation.
    /// RNG key is the edge itself: (worldSeed, minCx, minCz, maxCx, maxCz) so order of chunk generation never matters.
    /// </summary>
    public static class EdgeConnectorPlanner
    {
        private const int MinEdgePadding = 1;
        private const int MaxConnectorsPerEdge = 2;

        public static List<ConnectorPoint> PlanForChunk(
            BackroomsMapConfig config, int chunkX, int chunkZ)
        {
            int size = config.ChunkSize;
            var points = new List<ConnectorPoint>(8);

            PlanEdge(config, chunkX, chunkZ, chunkX + 1, chunkZ, size, points);
            PlanEdge(config, chunkX, chunkZ, chunkX - 1, chunkZ, size, points);
            PlanEdge(config, chunkX, chunkZ, chunkX, chunkZ + 1, size, points);
            PlanEdge(config, chunkX, chunkZ, chunkX, chunkZ - 1, size, points);

            return points;
        }

        public static Rng EdgeRng(int worldSeed, int cxA, int czA, int cxB, int czB)
        {
            int loX = cxA < cxB ? cxA : cxB;
            int hiX = cxA < cxB ? cxB : cxA;
            int loZ = czA < czB ? czA : czB;
            int hiZ = czA < czB ? czB : czA;
            return new Rng(worldSeed).Derive(loX, loZ).Derive(hiX, hiZ);
        }

        private static void PlanEdge(
            BackroomsMapConfig config,
            int chunkX, int chunkZ,
            int neighborX, int neighborZ,
            int size,
            List<ConnectorPoint> points)
        {
            var rng = EdgeRng(config.WorldSeed, chunkX, chunkZ, neighborX, neighborZ);
            int count = rng.Range(1, MaxConnectorsPerEdge + 1);

            int minPos = MinEdgePadding;
            int maxPos = size - 1 - MinEdgePadding;
            if (maxPos < minPos)
                return;

            var used = new HashSet<int>();

            for (int i = 0; i < count; i++)
            {
                int pos = rng.Range(minPos, maxPos + 1);
                int attempts = 0;
                while (used.Contains(pos) && attempts < 8)
                {
                    pos = rng.Range(minPos, maxPos + 1);
                    attempts++;
                }

                if (used.Contains(pos))
                    continue;

                used.Add(pos);

                if (neighborX == chunkX + 1)
                    points.Add(new ConnectorPoint(size - 1, pos));
                else if (neighborX == chunkX - 1)
                    points.Add(new ConnectorPoint(0, pos));
                else if (neighborZ == chunkZ + 1)
                    points.Add(new ConnectorPoint(pos, size - 1));
                else if (neighborZ == chunkZ - 1)
                    points.Add(new ConnectorPoint(pos, 0));
            }
        }
    }
}
