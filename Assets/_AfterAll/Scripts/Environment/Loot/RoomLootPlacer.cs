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

        /// <summary>How far an item may lean off flat when it spawns. Small — the Rigidbody drops it
        /// the last few centimetres, and this only has to break the "everything at the same angle"
        /// look, not simulate a throw.</summary>
        private const float MaxTiltDegrees = 12f;

        /// <summary>Rest rotation per pickup prefab, measured once. See <see cref="ResolveRestRotation"/>.</summary>
        private static readonly Dictionary<GameObject, Quaternion> RestRotations =
            new Dictionary<GameObject, Quaternion>();

        public static int Populate(
            RoomInstance room, RoomContentSettings settings, System.Random rng)
        {
            if (room == null || settings == null)
                return 0;

            LootTable table = settings.LootTable;
            if (table == null)
            {
                Debug.LogWarning(
                    "[RoomLoot] No LootTable assigned on the room content settings — no room can " +
                    "spawn loot until one is.", settings);
                return 0;
            }

            // Inactive points belong to a preset that lost, so includeInactive stays false — this is
            // the whole of the preset integration.
            var points = new List<LootSpawnPoint>(
                room.GetComponentsInChildren<LootSpawnPoint>(false));

            // Every return below this line used to be silent, which made the first authoring pass
            // unreadable: a room whose points all sat under a losing preset looked exactly like a room
            // with no points and a room whose count rolled to zero, and none of the three logged
            // anything. The reason is the only thing worth logging here.
            int pointCount = points.Count;
            if (pointCount == 0)
            {
                if (settings.LogActivation)
                    Debug.Log($"[RoomLoot] {room.name}: no ACTIVE spawn points.", room);
                return 0;
            }

            int count = ResolveCount(room, settings, pointCount, rng);
            if (count <= 0)
            {
                if (settings.LogActivation)
                    Debug.Log(
                        $"[RoomLoot] {room.name}: 0 items from {pointCount} point(s) at depth " +
                        $"{room.GraphDepth} — count rolled to zero.", room);
                return 0;
            }

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

                Object.Instantiate(
                    item.WorldPickupPrefab,
                    point.ResolveSpawnPosition(),
                    ResolveRotation(item.WorldPickupPrefab, rng),
                    container);
                spawned++;
            }

            if (settings.LogActivation)
                Debug.Log(
                    $"[RoomLoot] {room.name}: {spawned} item(s) across {pointCount} " +
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

            float expected = rolled * multiplier;

            // Round by chance, not by value. Rooms are authored with a handful of points, so the
            // expected count is routinely a fraction below 1 — room7 with 4 points at depth 0 comes out
            // at 0.4, and rounding that turned a working system into a guaranteed zero with nothing in
            // the console to say so. Carrying the fraction as a probability keeps the average intact
            // and lets a thin room still hold one item some of the time.
            int count = Mathf.FloorToInt(expected);
            if (rng.NextDouble() < expected - count)
                count++;

            return Mathf.Clamp(count, 0, pointCount);
        }

        /// <summary>
        /// How an item is oriented when it lands: laid flat, spun to a random heading, and leaned a
        /// few degrees so a shelf of loot doesn't read as a row of placed props.
        ///
        /// The old version was yaw-only, on the reasoning that a full random rotation looks like the
        /// item fell from a height. That was right about the rotation and wrong about the starting
        /// pose — the pickup prefabs are authored standing on their thinnest axis (Book's collider is
        /// 2.02 x 1.14 x 0.29, Tape's 2.77 x 4.94 x 0.64, both thin in Z), so yaw-only left every book
        /// on the floor balanced on its edge.
        /// </summary>
        private static Quaternion ResolveRotation(GameObject prefab, System.Random rng)
        {
            // Yaw is applied last so it also rotates which way the lean points.
            return Quaternion.AngleAxis((float)rng.NextDouble() * 360f, Vector3.up)
                   * Quaternion.AngleAxis(
                       (float)rng.NextDouble() * MaxTiltDegrees, Vector3.forward)
                   * ResolveRestRotation(prefab);
        }

        /// <summary>
        /// The rotation that puts a prefab's thinnest axis vertical — i.e. lays it down on the face a
        /// real object of that shape would rest on.
        ///
        /// Measured off the meshes rather than authored per item, so a new entry in the LootTable needs
        /// no extra setup and a re-modelled prop can't drift out of sync with a hand-typed value. The
        /// result only depends on the prefab, so it is cached: a floor build spawns loot for every room
        /// inside one frame.
        /// </summary>
        private static Quaternion ResolveRestRotation(GameObject prefab)
        {
            if (RestRotations.TryGetValue(prefab, out Quaternion cached))
                return cached;

            Quaternion rest = Quaternion.identity;

            if (TryMeasureLocalSize(prefab, out Vector3 size))
            {
                if (size.x <= size.y && size.x <= size.z)
                    rest = Quaternion.FromToRotation(Vector3.right, Vector3.up);
                else if (size.z <= size.y)
                    rest = Quaternion.FromToRotation(Vector3.forward, Vector3.up);
                // else Y is already the thinnest axis and the prefab is authored lying down.
            }

            RestRotations[prefab] = rest;
            return rest;
        }

        /// <summary>
        /// Bounding size of every mesh in a prefab, expressed in the prefab root's own space.
        ///
        /// Walks the corners through each child's transform rather than reading world bounds, because a
        /// prefab asset is never actually placed in the world and its renderers have no meaningful
        /// world bounds to read.
        /// </summary>
        private static bool TryMeasureLocalSize(GameObject prefab, out Vector3 size)
        {
            size = Vector3.zero;

            Transform root = prefab.transform;
            var bounds = new Bounds();
            bool measured = false;

            foreach (MeshFilter filter in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh mesh = filter.sharedMesh;
                if (mesh == null)
                    continue;

                Bounds local = mesh.bounds;

                for (int corner = 0; corner < 8; corner++)
                {
                    var offset = new Vector3(
                        (corner & 1) == 0 ? local.min.x : local.max.x,
                        (corner & 2) == 0 ? local.min.y : local.max.y,
                        (corner & 4) == 0 ? local.min.z : local.max.z);

                    Vector3 point = root.InverseTransformPoint(filter.transform.TransformPoint(offset));

                    if (!measured)
                    {
                        bounds = new Bounds(point, Vector3.zero);
                        measured = true;
                    }
                    else
                    {
                        bounds.Encapsulate(point);
                    }
                }
            }

            size = bounds.size;
            return measured && size.sqrMagnitude > 0f;
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
