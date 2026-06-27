using AfterAll.Generation;
using System.Collections.Generic;

namespace AfterAll.Generation.BackroomsMap
{
    public static class ArchetypeAssigner
    {
        public static List<ZoneSpec> Assign(List<ZoneRect> zones, int varietyLevel, Rng rng)
        {
            var result = new List<ZoneSpec>(zones.Count);
            foreach (var zone in zones)
                result.Add(new ZoneSpec(zone, PickArchetype(varietyLevel, rng)));

            return result;
        }

        private static ZoneArchetype PickArchetype(int varietyLevel, Rng rng)
        {
            int pillarHall = varietyLevel == 0 ? 3 : 12;
            int voidRoom = varietyLevel == 0 ? 2 : 8;
            int denseMaze = varietyLevel >= 2 ? 10 : 4;

            var weights = new (ZoneArchetype type, int weight)[]
            {
                (ZoneArchetype.StandardRoom, 34),
                (ZoneArchetype.Corridor, 26),
                (ZoneArchetype.Organic, 18),
                (ZoneArchetype.PillarHall, pillarHall),
                (ZoneArchetype.VoidRoom, voidRoom),
                (ZoneArchetype.DenseMaze, denseMaze)
            };

            int total = 0;
            foreach (var (_, w) in weights)
                total += w;

            float roll = rng.Value() * total;
            int cumulative = 0;
            foreach (var (type, weight) in weights)
            {
                cumulative += weight;
                if (roll < cumulative)
                    return type;
            }

            return ZoneArchetype.StandardRoom;
        }
    }
}
