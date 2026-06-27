using System.Collections.Generic;

namespace AfterAll.Generation.BackroomsMap
{
    public sealed class ChunkPreviewResult
    {
        public ChunkData Primary;
        public ChunkData Neighbor;
        public bool ConnectorsMatch;
        public bool BothEdgesWalkable;
    }

    public static class ChunkPreviewBuilder
    {
        public static ChunkPreviewResult BuildEastPair(BackroomsMapConfig config, int seed)
        {
            var left = BackroomsMapGenerator.Generate(config, 0, 0, seed);
            var right = BackroomsMapGenerator.Generate(config, 1, 0, seed);

            bool edgesWalkable = ValidateSharedEdge(left, right, config.ChunkSize, eastWest: true);
            bool connectorsMatch = ValidateConnectorAlignment(left, right, config.ChunkSize, eastWest: true);

            return new ChunkPreviewResult
            {
                Primary = left,
                Neighbor = right,
                BothEdgesWalkable = edgesWalkable,
                ConnectorsMatch = connectorsMatch
            };
        }

        public static CellType[,] MergeEastWest(ChunkData left, ChunkData right)
        {
            int h = left.Height;
            int w = left.Width + right.Width;
            var merged = new CellType[h, w];

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < left.Width; x++)
                    merged[y, x] = left.Cells[y, x];

                for (int x = 0; x < right.Width; x++)
                    merged[y, left.Width + x] = right.Cells[y, x];
            }

            return merged;
        }

        private static bool ValidateSharedEdge(ChunkData left, ChunkData right, int size, bool eastWest)
        {
            if (!eastWest)
                return false;

            var leftEast = ConnectorYs(left.ConnectorPoints, size - 1, fixedX: true);
            var rightWest = ConnectorYs(right.ConnectorPoints, 0, fixedX: true);
            leftEast.IntersectWith(rightWest);

            if (leftEast.Count == 0)
                return false;

            foreach (int y in leftEast)
            {
                if (!left.Cells[y, size - 1].IsWalkable())
                    return false;
                if (!right.Cells[y, 0].IsWalkable())
                    return false;
            }

            return true;
        }

        private static HashSet<int> ConnectorYs(
            System.Collections.Generic.List<ConnectorPoint> points, int edgeCoord, bool fixedX)
        {
            var ys = new HashSet<int>();
            foreach (var p in points)
            {
                if (fixedX && p.X == edgeCoord)
                    ys.Add(p.Y);
                else if (!fixedX && p.Y == edgeCoord)
                    ys.Add(p.X);
            }

            return ys;
        }

        private static bool ValidateConnectorAlignment(ChunkData left, ChunkData right, int size, bool eastWest)
        {
            if (!eastWest)
                return true;

            var leftEast = new HashSet<int>();
            foreach (var p in left.ConnectorPoints)
            {
                if (p.X == size - 1)
                    leftEast.Add(p.Y);
            }

            var rightWest = new HashSet<int>();
            foreach (var p in right.ConnectorPoints)
            {
                if (p.X == 0)
                    rightWest.Add(p.Y);
            }

            if (leftEast.Count == 0 || rightWest.Count == 0)
                return false;

            leftEast.IntersectWith(rightWest);
            return leftEast.Count > 0;
        }
    }
}
