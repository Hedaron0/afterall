using System.Collections.Generic;
using AfterAll.Generation;

namespace AfterAll.Generation.BackroomsMap
{
    /// <summary>
    /// Layer 1 vent paths — random walks starting on floor, routed through wall cells.
    /// Stored separately from the walkable grid (3D L1 tunnel mesh later).
    /// </summary>
    public static class VentGraphPass
    {
        private static readonly (int dx, int dy)[] Directions =
        {
            (0, 1),  // North (+Z)
            (0, -1), // South
            (1, 0),  // East
            (-1, 0)  // West
        };

        public static List<VentSpec> Generate(CellType[,] cells, BackroomsMapConfig config, Rng rng)
        {
            int w = cells.GetLength(1);
            int h = cells.GetLength(0);
            var walkable = CollectWalkable(cells, w, h);
            if (walkable.Count == 0)
                return new List<VentSpec>();

            int target = rng.Range(config.VentsPerChunkMin, config.VentsPerChunkMax + 1);
            var vents = new List<VentSpec>(target);

            Shuffle(walkable, rng.Derive(91));

            int attempts = 0;
            int maxAttempts = target * 4;

            for (int i = 0; walkable.Count > 0 && vents.Count < target && attempts < maxAttempts; attempts++)
            {
                var start = walkable[i % walkable.Count];
                i++;

                var ventRng = rng.Derive(100 + attempts);
                var vent = TryBuildVent(cells, w, h, start.x, start.y, ventRng);
                if (vent == null)
                    continue;

                vents.Add(vent);
            }

            return vents;
        }

        private static VentSpec TryBuildVent(CellType[,] cells, int w, int h, int sx, int sy, Rng rng)
        {
            var wallDirs = new List<int>();
            for (int d = 0; d < Directions.Length; d++)
            {
                int nx = sx + Directions[d].dx;
                int ny = sy + Directions[d].dy;
                if (InBounds(nx, ny, w, h) && cells[ny, nx] == CellType.Wall)
                    wallDirs.Add(d);
            }

            if (wallDirs.Count == 0)
                return null;

            int dir = wallDirs[rng.Range(0, wallDirs.Count)];
            var (kind, steps) = RollKindAndSteps(rng);
            var connType = RollConnectionType(rng);
            bool hasLadder = rng.Chance(0.12f);

            var path = new List<(int x, int y)> { (sx, sy) };
            int x = sx;
            int y = sy;

            for (int step = 0; step < steps; step++)
            {
                if (rng.Chance(0.3f))
                    dir = rng.Range(0, Directions.Length);

                x += Directions[dir].dx;
                y += Directions[dir].dy;

                if (!InBounds(x, y, w, h))
                    break;

                if (cells[y, x] == CellType.Pillar)
                    break;

                path.Add((x, y));

                if (cells[y, x].IsWalkable() && step > 0)
                    break;
            }

            if (path.Count < 2)
                return null;

            return new VentSpec
            {
                Path = path,
                Kind = kind,
                ConnType = connType,
                HasLadder = hasLadder
            };
        }

        private static (VentKind kind, int steps) RollKindAndSteps(Rng rng)
        {
            float roll = rng.Value();
            if (roll < 0.35f)
                return (VentKind.Short, 4 + (int)(rng.Value() * 6));
            if (roll < 0.75f)
                return (VentKind.Long, 12 + (int)(rng.Value() * 16));
            return (VentKind.Unrelated, 18 + (int)(rng.Value() * 22));
        }

        private static VentConnectionType RollConnectionType(Rng rng)
        {
            float endRoll = rng.Value();
            if (endRoll < 0.25f)
                return VentConnectionType.DeadEnd;
            if (endRoll < 0.6f)
                return VentConnectionType.WallMid;
            return VentConnectionType.Multi;
        }

        private static List<(int x, int y)> CollectWalkable(CellType[,] cells, int w, int h)
        {
            var list = new List<(int x, int y)>();
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (cells[y, x].IsWalkable())
                    list.Add((x, y));
            }

            return list;
        }

        private static void Shuffle(List<(int x, int y)> list, Rng rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        private static bool InBounds(int x, int y, int w, int h) =>
            x >= 0 && y >= 0 && x < w && y < h;
    }
}
