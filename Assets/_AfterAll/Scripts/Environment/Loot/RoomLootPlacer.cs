using System.Collections.Generic;
using AfterAll.Items;
using UnityEngine;

namespace AfterAll.Environment
{
    /// <summary>
    /// Fills one room's loot: decides how many items it gets, picks that many
    /// <see cref="LootSpawnPoint"/>s out of the ones its active preset offers, and instantiates a
    /// rolled item at each.
    ///
    /// Runs off the same per-room System.Random as preset selection, so a seed reproduces a floor's
    /// loot exactly.
    /// </summary>
    public static class RoomLootPlacer
    {
        /// <summary>Name of the child every spawned item is parented under, so loot is easy to find
        /// in the hierarchy and easy to clear on a floor rebuild.</summary>
        private const string ContainerName = "SpawnedLoot";

        /// <summary>Share of a room's spawn points that carry loot on an average run. Points are
        /// meant to outnumber loot heavily — that surplus is what makes two visits to the same room
        /// prefab look different.</summary>
        private const float DefaultFillRatio = 0.35f;

        public static int Populate(
            RoomInstance room, RoomContentSettings settings, System.Random rng)
        {
            if (room == null || settings == null)
                return 0;

            LootTable table = settings.LootTable;
            if (table == null)
                return 0;

            // Inactive points belong to a preset that lost, so includeInactive stays false — this is
            // the whole of the preset integration.
            var points = new List<LootSpawnPoint>(
                room.GetComponentsInChildren<LootSpawnPoint>(false));

            if (points.Count == 0)
                return 0;

            int count = ResolveCount(room, settings, points.Count, rng);
            if (count <= 0)
                return 0;

            Transform container = PrepareContainer(room.transform);
            int spawned = 0;

            for (int i = 0; i < count && points.Count > 0; i++)
            {
                LootSpawnPoint point = TakeWeighted(points, rng);
                if (point == null)
                    break;

                ItemDefinition item = table.Pick(rng);
                if (item == null || item.WorldPickupPrefab == null)
                    continue;

                // Yaw only: an item tipped onto its side by a random full rotation reads as dropped
                // from a height rather than left on a desk. The Rigidbody settles the rest.
                var rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);

                Object.Instantiate(
                    item.WorldPickupPrefab, point.ResolveSpawnPosition(), rotation, container);
                spawned++;
            }

            if (settings.LogActivation)
                Debug.Log(
                    $"[RoomLoot] {room.name}: {spawned} item(s) across {points.Count + spawned} " +
                    $"point(s), depth {room.GraphDepth}.", room);

            return spawned;
        }

        /// <summary>
        /// How many items this room gets: its budget (authored or derived from point count), then
        /// scaled by how deep in the floor it sits, then capped by the points available.
        ///
        /// The depth scaling is the one part carried over from the old Random pool, where it applied
        /// a per-item chance multiplier. It reads better as a count: rooms near the elevator are
        /// thin, and the floor gets worth exploring the further out you go.
        /// </summary>
        private static int ResolveCount(
            RoomInstance room, RoomContentSettings settings, int pointCount, System.Random rng)
        {
            int min;
            int max;

            if (room.TryGetComponent(out RoomLootBudget budget))
            {
                min = budget.MinLoot;
                max = budget.MaxLoot;
            }
            else
            {
                max = Mathf.Max(1, Mathf.RoundToInt(pointCount * DefaultFillRatio));
                min = Mathf.Max(0, Mathf.RoundToInt(max * 0.5f));
            }

            int rolled = max > min ? rng.Next(min, max + 1) : min;

            float t = Mathf.Clamp01(
                Mathf.Max(0, room.GraphDepth) / (float)Mathf.Max(1, settings.LootDepthFarDepth));
            float multiplier = Mathf.Lerp(
                settings.LootDepthNearMultiplier, settings.LootDepthFarMultiplier, t);

            return Mathf.Clamp(Mathf.RoundToInt(rolled * multiplier), 0, pointCount);
        }

        /// <summary>Picks a point by weight and removes it, so no two items land on the same spot.</summary>
        private static LootSpawnPoint TakeWeighted(List<LootSpawnPoint> points, System.Random rng)
        {
            float total = 0f;
            foreach (LootSpawnPoint point in points)
                total += Mathf.Max(0f, point.Weight);

            int index;

            if (total <= 0f)
            {
                index = rng.Next(points.Count);
            }
            else
            {
                double roll = rng.NextDouble() * total;
                double cumulative = 0d;
                index = points.Count - 1;

                for (int i = 0; i < points.Count; i++)
                {
                    cumulative += Mathf.Max(0f, points[i].Weight);
                    if (roll <= cumulative)
                    {
                        index = i;
                        break;
                    }
                }
            }

            LootSpawnPoint chosen = points[index];
            points.RemoveAt(index);
            return chosen;
        }

        /// <summary>
        /// Gets the room's loot container, emptying it first.
        ///
        /// Rooms are pooled across floor rebuilds, so a room reused on floor 2 would otherwise still
        /// be holding floor 1's loot. DestroyImmediate because the whole build pass runs inside one
        /// frame and a deferred Destroy would leave the old items alive while the new ones spawn —
        /// the same deferred-destruction trap that hung ApplyUnreachablePolicy.
        /// </summary>
        private static Transform PrepareContainer(Transform room)
        {
            Transform container = room.Find(ContainerName);
            if (container == null)
            {
                var go = new GameObject(ContainerName);
                go.transform.SetParent(room, false);
                return go.transform;
            }

            for (int i = container.childCount - 1; i >= 0; i--)
                Object.DestroyImmediate(container.GetChild(i).gameObject);

            return container;
        }
    }
}
