using System;
using System.Collections.Generic;
using UnityEngine;

namespace AfterAll.Environment
{
    /// <summary>
    /// Role-aware weighted picking: corridors are connectors (streak + budget caps),
    /// spawn weights apply only within the allowed role set.
    /// </summary>
    public static class RoomRolePicker
    {
        public const float DefaultCorridorBudgetFraction = 0.2f;
        public const int MaxCorridorStreak = 1;

        public static bool AllowsCorridor(
            RoomRole lastRole,
            int corridorPlaced,
            int totalPlaced,
            int targetRoomCount,
            float corridorBudgetFraction = DefaultCorridorBudgetFraction)
        {
            if (lastRole == RoomRole.Corridor)
                return false;

            int budget = Mathf.Max(0, Mathf.RoundToInt(Mathf.Max(1, targetRoomCount) * Mathf.Clamp01(corridorBudgetFraction)));
            // Hub counts as placed; corridors among remaining.
            return corridorPlaced < budget;
        }

        public static RoomFootprint PickHub(IReadOnlyList<RoomFootprint> library, System.Random rng)
        {
            if (library == null || library.Count == 0)
                return null;

            var hubs = new List<RoomFootprint>();
            RoomFootprint largest = null;
            float largestArea = -1f;

            for (int i = 0; i < library.Count; i++)
            {
                RoomFootprint entry = library[i];
                if (entry == null)
                    continue;

                if (entry.ResolvedRole == RoomRole.Hub)
                    hubs.Add(entry);

                float area = entry.BoundsAreaM2;
                if (area > largestArea)
                {
                    largestArea = area;
                    largest = entry;
                }
            }

            if (hubs.Count > 0)
                return PickWeightedFootprints(hubs, rng);

            return largest ?? library[0];
        }

        public static RoomFootprint PickForContext(
            IReadOnlyList<RoomFootprint> library,
            System.Random rng,
            RoomRole lastRole,
            int corridorPlaced,
            int totalPlaced,
            int targetRoomCount,
            float corridorBudgetFraction = DefaultCorridorBudgetFraction)
        {
            if (library == null || library.Count == 0)
                return null;

            bool allowCorridor = AllowsCorridor(
                lastRole, corridorPlaced, totalPlaced, targetRoomCount, corridorBudgetFraction);

            var allowed = new List<RoomFootprint>(library.Count);
            for (int i = 0; i < library.Count; i++)
            {
                RoomFootprint entry = library[i];
                if (entry == null)
                    continue;

                RoomRole role = entry.ResolvedRole;
                if (role == RoomRole.Corridor && !allowCorridor)
                    continue;

                // Hubs are start-biased; keep them rare during growth.
                if (role == RoomRole.Hub && totalPlaced > 0)
                    continue;

                allowed.Add(entry);
            }

            if (allowed.Count == 0)
            {
                // Fallback: any non-corridor, then anything.
                for (int i = 0; i < library.Count; i++)
                {
                    RoomFootprint entry = library[i];
                    if (entry != null && entry.ResolvedRole != RoomRole.Corridor)
                        allowed.Add(entry);
                }
            }

            if (allowed.Count == 0)
            {
                for (int i = 0; i < library.Count; i++)
                {
                    if (library[i] != null)
                        allowed.Add(library[i]);
                }
            }

            return PickWeightedFootprints(allowed, rng);
        }

        public static void BuildContextPrefabOrder(
            IReadOnlyList<RoomFootprint> library,
            System.Random rng,
            RoomRole lastRole,
            int corridorPlaced,
            int totalPlaced,
            int targetRoomCount,
            List<int> prefabOrder,
            float corridorBudgetFraction = DefaultCorridorBudgetFraction)
        {
            prefabOrder.Clear();
            if (library == null)
                return;

            bool allowCorridor = AllowsCorridor(
                lastRole, corridorPlaced, totalPlaced, targetRoomCount, corridorBudgetFraction);

            var allowed = new List<int>();
            var fallback = new List<int>();

            for (int i = 0; i < library.Count; i++)
            {
                RoomFootprint entry = library[i];
                if (entry == null)
                    continue;

                RoomRole role = entry.ResolvedRole;
                bool isAllowed = true;
                if (role == RoomRole.Corridor && !allowCorridor)
                    isAllowed = false;
                if (role == RoomRole.Hub && totalPlaced > 0)
                    isAllowed = false;

                if (isAllowed)
                    allowed.Add(i);
                else
                    fallback.Add(i);
            }

            Shuffle(allowed, rng);
            Shuffle(fallback, rng);

            // Bias front with a few context-weighted picks.
            for (int w = 0; w < Mathf.Min(3, allowed.Count); w++)
            {
                RoomFootprint weighted = PickForContext(
                    library, rng, lastRole, corridorPlaced, totalPlaced, targetRoomCount, corridorBudgetFraction);
                if (weighted == null)
                    break;

                int weightedIndex = -1;
                for (int a = 0; a < allowed.Count; a++)
                {
                    if (library[allowed[a]] == weighted)
                    {
                        weightedIndex = a;
                        break;
                    }
                }

                if (weightedIndex > 0)
                    (allowed[0], allowed[weightedIndex]) = (allowed[weightedIndex], allowed[0]);
            }

            prefabOrder.AddRange(allowed);
            // Last-resort: disallowed roles only if nothing in allowed can snap.
            prefabOrder.AddRange(fallback);
        }

        public static RoomPrefabEntry PickHubEntry(IReadOnlyList<RoomPrefabEntry> pool, System.Random rng)
        {
            if (pool == null || pool.Count == 0)
                return null;

            var hubs = new List<RoomPrefabEntry>();
            for (int i = 0; i < pool.Count; i++)
            {
                RoomPrefabEntry entry = pool[i];
                if (entry != null && entry.IsValid && entry.ResolvedRole == RoomRole.Hub)
                    hubs.Add(entry);
            }

            if (hubs.Count > 0)
                return PickWeightedEntries(hubs, rng);

            return PickWeightedEntries(pool, rng);
        }

        public static RoomPrefabEntry PickEntryForContext(
            IReadOnlyList<RoomPrefabEntry> pool,
            System.Random rng,
            RoomRole lastRole,
            int corridorPlaced,
            int totalPlaced,
            int targetRoomCount,
            float corridorBudgetFraction = DefaultCorridorBudgetFraction)
        {
            if (pool == null || pool.Count == 0)
                return null;

            bool allowCorridor = AllowsCorridor(
                lastRole, corridorPlaced, totalPlaced, targetRoomCount, corridorBudgetFraction);

            var allowed = new List<RoomPrefabEntry>(pool.Count);
            for (int i = 0; i < pool.Count; i++)
            {
                RoomPrefabEntry entry = pool[i];
                if (entry == null || !entry.IsValid)
                    continue;

                RoomRole role = entry.ResolvedRole;
                if (role == RoomRole.Corridor && !allowCorridor)
                    continue;
                if (role == RoomRole.Hub && totalPlaced > 0)
                    continue;

                allowed.Add(entry);
            }

            if (allowed.Count == 0)
            {
                for (int i = 0; i < pool.Count; i++)
                {
                    RoomPrefabEntry entry = pool[i];
                    if (entry != null && entry.IsValid && entry.ResolvedRole != RoomRole.Corridor)
                        allowed.Add(entry);
                }
            }

            if (allowed.Count == 0)
            {
                for (int i = 0; i < pool.Count; i++)
                {
                    if (pool[i] != null && pool[i].IsValid)
                        allowed.Add(pool[i]);
                }
            }

            return PickWeightedEntries(allowed, rng);
        }

        private static RoomFootprint PickWeightedFootprints(IReadOnlyList<RoomFootprint> list, System.Random rng)
        {
            int total = 0;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] != null)
                    total += list[i].SpawnWeight;
            }

            if (total <= 0)
                return list[0];

            int roll = rng.Next(total);
            int cumulative = 0;
            for (int i = 0; i < list.Count; i++)
            {
                RoomFootprint entry = list[i];
                if (entry == null)
                    continue;

                cumulative += entry.SpawnWeight;
                if (roll < cumulative)
                    return entry;
            }

            return list[list.Count - 1];
        }

        private static RoomPrefabEntry PickWeightedEntries(IReadOnlyList<RoomPrefabEntry> list, System.Random rng)
        {
            int total = 0;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] != null && list[i].IsValid)
                    total += list[i].Weight;
            }

            if (total <= 0)
                return list[0];

            int roll = rng != null ? rng.Next(total) : UnityEngine.Random.Range(0, total);
            int cumulative = 0;
            for (int i = 0; i < list.Count; i++)
            {
                RoomPrefabEntry entry = list[i];
                if (entry == null || !entry.IsValid)
                    continue;

                cumulative += entry.Weight;
                if (roll < cumulative)
                    return entry;
            }

            return list[list.Count - 1];
        }

        private static void Shuffle(List<int> values, System.Random rng)
        {
            for (int i = values.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (values[i], values[j]) = (values[j], values[i]);
            }
        }
    }
}
