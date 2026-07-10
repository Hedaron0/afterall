using System;
using System.Collections.Generic;
using UnityEngine;

namespace AfterAll.Environment
{
    /// <summary>
    /// Pure-data path-network layout planner (no Instantiate).
    /// Phase 1: N spines from hub. Phase 2: frontier infill. No proximity links.
    /// </summary>
    public static class PathNetworkPlanner
    {
        private const float OverlapEpsilon = 0.05f;
        /// <summary>
        /// Floor AABBs often extend slightly past wall seams. Without inset, seam-snapped
        /// neighbors always "overlap" and the planner never places a second room.
        /// </summary>
        private const float OverlapInsetM = 0.2f;
        private const int MaxInfillAttempts = 800;

        private struct PlannedRoom
        {
            public RoomFootprint footprint;
            public Vector2 positionXZ;
            public float yawRadians;
            public List<PlannedWall> walls;
            public Vector2 boundsMin;
            public Vector2 boundsMax;
        }

        private struct PlannedWall
        {
            public string name;
            public Vector2 seam;
            public Vector2 axis;
            public Vector2 start;
            public Vector2 end;
            public Vector2 outward;
            public SocketDirection direction;
            public bool doorValid;
            public float lengthM;
            public bool isConnected;
            public float gapWidthM;
        }

        public static LayoutPlan Generate(
            IReadOnlyList<RoomFootprint> library,
            int seed,
            int roomCount,
            int pathCount,
            bool randomGapOffset = false,
            GapOffsetPolicy gapPolicy = default)
        {
            var plan = new LayoutPlan
            {
                seed = seed,
                roomCount = Mathf.Max(1, roomCount),
                pathCount = Mathf.Max(1, pathCount),
                randomGapOffset = randomGapOffset
            };

            if (library == null || library.Count == 0)
            {
                plan.notes = "Empty footprint library.";
                return plan;
            }

            if (gapPolicy.edgeMarginM <= 0f && gapPolicy.spanFraction <= 0f)
                gapPolicy = GapOffsetPolicy.Default;

            gapPolicy.randomGapOffset = randomGapOffset;

            var rng = new System.Random(seed);
            var placed = new List<PlannedRoom>();
            int failed = 0;

            RoomFootprint hubFootprint = PickWeighted(library, rng);
            PlannedRoom hub = CreateRoom(hubFootprint, Vector2.zero, 0f);
            placed.Add(hub);

            int roomsPerPath = Mathf.Max(1, plan.roomCount / plan.pathCount);

            for (int path = 0; path < plan.pathCount && placed.Count < plan.roomCount; path++)
            {
                int lastIndex = 0;
                for (int step = 0; step < roomsPerPath && placed.Count < plan.roomCount; step++)
                {
                    if (!TryGrowFrom(
                            placed,
                            lastIndex,
                            library,
                            rng,
                            gapPolicy,
                            plan,
                            out int childIndex,
                            ref failed))
                        break;

                    lastIndex = childIndex;
                }
            }

            var frontier = new List<int>();
            for (int i = 0; i < placed.Count; i++)
                frontier.Add(i);

            int infillAttempts = 0;
            while (placed.Count < plan.roomCount && frontier.Count > 0 && infillAttempts < MaxInfillAttempts)
            {
                infillAttempts++;
                int frontierIdx = rng.Next(frontier.Count);
                int parentIndex = frontier[frontierIdx];

                if (!HasOpenDoorWall(placed[parentIndex]))
                {
                    frontier.RemoveAt(frontierIdx);
                    continue;
                }

                if (TryGrowFrom(
                        placed,
                        parentIndex,
                        library,
                        rng,
                        gapPolicy,
                        plan,
                        out int childIndex,
                        ref failed))
                {
                    frontier.Add(childIndex);
                }
            }

            for (int i = 0; i < placed.Count; i++)
            {
                PlannedRoom room = placed[i];
                plan.placements.Add(new LayoutPlanPlacement
                {
                    index = i,
                    prefabId = room.footprint.PrefabId,
                    positionXZ = room.positionXZ,
                    yawDegrees = Mathf.Round(room.yawRadians * Mathf.Rad2Deg / 90f) * 90f
                });
            }

            plan.failedAttempts = failed;
            plan.notes =
                $"PathNetwork placed={plan.PlacedCount}/{plan.roomCount}, paths={plan.pathCount}, " +
                $"failed={failed}, randomGap={randomGapOffset}";
            return plan;
        }

        private static bool TryGrowFrom(
            List<PlannedRoom> placed,
            int parentIndex,
            IReadOnlyList<RoomFootprint> library,
            System.Random rng,
            GapOffsetPolicy gapPolicy,
            LayoutPlan plan,
            out int childIndex,
            ref int failed)
        {
            childIndex = -1;
            PlannedRoom parent = placed[parentIndex];
            List<int> openWallIndices = CollectOpenDoorWallIndices(parent);
            if (openWallIndices.Count == 0)
                return false;

            Shuffle(openWallIndices, rng);

            var prefabOrder = new List<int>(library.Count);
            for (int i = 0; i < library.Count; i++)
            {
                if (library[i] != null)
                    prefabOrder.Add(i);
            }

            Shuffle(prefabOrder, rng);

            // Bias: try a few weighted picks first, then the rest.
            for (int w = 0; w < Mathf.Min(3, prefabOrder.Count); w++)
            {
                RoomFootprint weighted = PickWeighted(library, rng);
                int weightedIndex = prefabOrder.FindIndex(i => library[i] == weighted);
                if (weightedIndex > 0)
                {
                    (prefabOrder[0], prefabOrder[weightedIndex]) = (prefabOrder[weightedIndex], prefabOrder[0]);
                }
            }

            foreach (int parentWallIndex in openWallIndices)
            {
                PlannedWall parentWall = parent.walls[parentWallIndex];
                float parentOffset = SampleGapOffset(parentWall, rng, gapPolicy);

                foreach (int prefabIndex in prefabOrder)
                {
                    RoomFootprint childFootprint = library[prefabIndex];
                    List<int> childWallIndices = CollectDoorWallIndices(childFootprint);
                    Shuffle(childWallIndices, rng);

                    foreach (int childWallIndex in childWallIndices)
                    {
                        RoomFootprint.Wall childWallDef = childFootprint.Walls[childWallIndex];
                        if (!childWallDef.doorValid)
                            continue;

                        float childOffset = SampleGapOffset(
                            ToPlannedWallLocal(childWallDef, childFootprint.GapWidthM),
                            rng,
                            gapPolicy);

                        if (!TrySnap(
                                parent,
                                parentWallIndex,
                                parentOffset,
                                childFootprint,
                                childWallDef.name,
                                childOffset,
                                out PlannedRoom candidate))
                        {
                            failed++;
                            continue;
                        }

                        if (OverlapsAny(candidate, placed))
                        {
                            failed++;
                            continue;
                        }

                        childIndex = placed.Count;
                        placed.Add(candidate);

                        PlannedRoom updatedParent = placed[parentIndex];
                        PlannedWall pw = updatedParent.walls[parentWallIndex];
                        pw.isConnected = true;
                        updatedParent.walls[parentWallIndex] = pw;
                        placed[parentIndex] = updatedParent;

                        PlannedRoom updatedChild = placed[childIndex];
                        int childPlannedWallIndex = FindWallIndex(updatedChild, childWallDef.name);
                        if (childPlannedWallIndex >= 0)
                        {
                            PlannedWall cw = updatedChild.walls[childPlannedWallIndex];
                            cw.isConnected = true;
                            updatedChild.walls[childPlannedWallIndex] = cw;
                            placed[childIndex] = updatedChild;
                        }

                        plan.connections.Add(new LayoutPlanConnection
                        {
                            parentIndex = parentIndex,
                            parentWall = parentWall.name,
                            childIndex = childIndex,
                            childWall = childWallDef.name,
                            parentGapOffsetM = parentOffset,
                            childGapOffsetM = childOffset
                        });

                        return true;
                    }
                }
            }

            return false;
        }

        private static bool TrySnap(
            PlannedRoom parent,
            int parentWallIndex,
            float parentOffset,
            RoomFootprint childFootprint,
            string childWallName,
            float childOffset,
            out PlannedRoom child)
        {
            child = default;
            PlannedWall parentWall = parent.walls[parentWallIndex];
            if (!childFootprint.TryGetWall(childWallName, out RoomFootprint.Wall childWallDef))
                return false;

            Vector2 parentSeam = SeamWithOffset(parentWall, parentOffset);
            Vector2 parentOutward = parentWall.outward;

            Vector2 childSeamLocal = SeamWithOffsetLocal(childWallDef, childFootprint.GapWidthM, childOffset);
            Vector2 childOutwardLocal = childWallDef.outwardLocal.normalized;

            float targetAngle = Mathf.Atan2(-parentOutward.x, -parentOutward.y);
            float localAngle = Mathf.Atan2(childOutwardLocal.x, childOutwardLocal.y);
            float theta = QuantizeHalfPi(targetAngle - localAngle);

            Vector2 childOutwardWorld = Rotate(childOutwardLocal, theta);
            if (Vector2.Dot(childOutwardWorld, parentOutward) > -0.5f)
                return false;

            Vector2 rotatedChildSeam = Rotate(childSeamLocal, theta);
            Vector2 position = parentSeam - rotatedChildSeam;

            child = CreateRoom(childFootprint, position, theta);
            return true;
        }

        private static PlannedRoom CreateRoom(RoomFootprint footprint, Vector2 positionXZ, float yawRadians)
        {
            var walls = new List<PlannedWall>(footprint.Walls.Length);
            foreach (RoomFootprint.Wall wall in footprint.Walls)
            {
                walls.Add(new PlannedWall
                {
                    name = wall.name,
                    seam = TransformPoint(wall.seamLocal, positionXZ, yawRadians),
                    axis = Rotate(wall.axisLocal.normalized, yawRadians),
                    start = TransformPoint(wall.startLocal, positionXZ, yawRadians),
                    end = TransformPoint(wall.endLocal, positionXZ, yawRadians),
                    outward = Rotate(wall.outwardLocal.normalized, yawRadians),
                    direction = RotateDirection(wall.direction, yawRadians),
                    doorValid = wall.doorValid,
                    lengthM = wall.lengthM,
                    isConnected = false,
                    gapWidthM = footprint.GapWidthM
                });
            }

            Vector2 min = TransformPoint(footprint.BoundsMin, positionXZ, yawRadians);
            Vector2 max = TransformPoint(footprint.BoundsMax, positionXZ, yawRadians);
            Vector2 boundsMin = Vector2.Min(min, max);
            Vector2 boundsMax = Vector2.Max(min, max);

            // Rotate AABB corners for correct world bounds.
            Vector2 c0 = TransformPoint(new Vector2(footprint.BoundsMin.x, footprint.BoundsMin.y), positionXZ, yawRadians);
            Vector2 c1 = TransformPoint(new Vector2(footprint.BoundsMin.x, footprint.BoundsMax.y), positionXZ, yawRadians);
            Vector2 c2 = TransformPoint(new Vector2(footprint.BoundsMax.x, footprint.BoundsMin.y), positionXZ, yawRadians);
            Vector2 c3 = TransformPoint(new Vector2(footprint.BoundsMax.x, footprint.BoundsMax.y), positionXZ, yawRadians);
            boundsMin = Vector2.Min(Vector2.Min(c0, c1), Vector2.Min(c2, c3));
            boundsMax = Vector2.Max(Vector2.Max(c0, c1), Vector2.Max(c2, c3));

            return new PlannedRoom
            {
                footprint = footprint,
                positionXZ = positionXZ,
                yawRadians = yawRadians,
                walls = walls,
                boundsMin = boundsMin,
                boundsMax = boundsMax
            };
        }

        private static bool OverlapsAny(PlannedRoom candidate, List<PlannedRoom> placed)
        {
            for (int i = 0; i < placed.Count; i++)
            {
                if (AabbOverlap(
                        candidate.boundsMin,
                        candidate.boundsMax,
                        placed[i].boundsMin,
                        placed[i].boundsMax,
                        OverlapInsetM))
                    return true;
            }

            return false;
        }

        private static bool AabbOverlap(
            Vector2 aMin,
            Vector2 aMax,
            Vector2 bMin,
            Vector2 bMax,
            float inset)
        {
            float insetClamped = Mathf.Max(0f, inset);
            Vector2 aMinI = aMin + Vector2.one * insetClamped;
            Vector2 aMaxI = aMax - Vector2.one * insetClamped;
            Vector2 bMinI = bMin + Vector2.one * insetClamped;
            Vector2 bMaxI = bMax - Vector2.one * insetClamped;

            // Degenerate after inset → treat as non-overlapping (too thin to matter).
            if (aMinI.x >= aMaxI.x - OverlapEpsilon || aMinI.y >= aMaxI.y - OverlapEpsilon)
                return false;
            if (bMinI.x >= bMaxI.x - OverlapEpsilon || bMinI.y >= bMaxI.y - OverlapEpsilon)
                return false;

            return aMinI.x < bMaxI.x - OverlapEpsilon &&
                   aMaxI.x > bMinI.x + OverlapEpsilon &&
                   aMinI.y < bMaxI.y - OverlapEpsilon &&
                   aMaxI.y > bMinI.y + OverlapEpsilon;
        }

        private static float SampleGapOffset(PlannedWall wall, System.Random rng, GapOffsetPolicy policy)
        {
            if (!TryGetOffsetRange(wall.lengthM, wall.gapWidthM, policy, out float min, out float max))
                return 0f;

            if (!policy.randomGapOffset || rng == null || Mathf.Abs(max - min) < 0.0001f)
                return (min + max) * 0.5f;

            return min + (float)rng.NextDouble() * (max - min);
        }

        private static bool TryGetOffsetRange(
            float wallLengthM,
            float gapWidthM,
            GapOffsetPolicy policy,
            out float minOffset,
            out float maxOffset)
        {
            minOffset = 0f;
            maxOffset = 0f;

            float edgeMargin = Mathf.Max(0f, policy.edgeMarginM);
            float spanFraction = Mathf.Clamp(policy.spanFraction > 0f ? policy.spanFraction : 1f, 0f, 1f);
            const float safetyM = 0.05f;

            float maxGap = Mathf.Max(0f, wallLengthM - edgeMargin * 2f - safetyM);
            float effectiveGap = Mathf.Min(gapWidthM, maxGap);
            if (effectiveGap < 0.05f)
                return false;

            float usableSpan = Mathf.Max(0f, wallLengthM - effectiveGap);
            float clampedSpan = usableSpan * spanFraction;
            minOffset = edgeMargin + (usableSpan - clampedSpan) * 0.5f;
            maxOffset = minOffset + clampedSpan;
            if (maxOffset < minOffset)
            {
                float center = usableSpan * 0.5f;
                minOffset = center;
                maxOffset = center;
            }

            return true;
        }

        private static Vector2 SeamWithOffset(PlannedWall wall, float offsetMeters)
        {
            if (!TryGetOffsetRange(wall.lengthM, wall.gapWidthM, GapOffsetPolicy.Default, out float min, out float max))
                return wall.seam;

            float center = (min + max) * 0.5f;
            float delta = offsetMeters - center;
            return wall.seam + wall.axis.normalized * delta;
        }

        private static Vector2 SeamWithOffsetLocal(RoomFootprint.Wall wall, float gapWidthM, float offsetMeters)
        {
            var planned = ToPlannedWallLocal(wall, gapWidthM);
            return SeamWithOffset(planned, offsetMeters);
        }

        private static PlannedWall ToPlannedWallLocal(RoomFootprint.Wall wall, float gapWidthM) =>
            new PlannedWall
            {
                name = wall.name,
                seam = wall.seamLocal,
                axis = wall.axisLocal.normalized,
                outward = wall.outwardLocal.normalized,
                lengthM = wall.lengthM,
                gapWidthM = gapWidthM,
                doorValid = wall.doorValid,
                direction = wall.direction
            };

        private static List<int> CollectOpenDoorWallIndices(PlannedRoom room)
        {
            var list = new List<int>();
            for (int i = 0; i < room.walls.Count; i++)
            {
                PlannedWall wall = room.walls[i];
                if (!wall.isConnected && wall.doorValid)
                    list.Add(i);
            }

            return list;
        }

        private static bool HasOpenDoorWall(PlannedRoom room) => CollectOpenDoorWallIndices(room).Count > 0;

        private static List<int> CollectDoorWallIndices(RoomFootprint footprint)
        {
            var list = new List<int>();
            for (int i = 0; i < footprint.Walls.Length; i++)
            {
                if (footprint.Walls[i].doorValid)
                    list.Add(i);
            }

            return list;
        }

        private static int FindWallIndex(PlannedRoom room, string wallName)
        {
            for (int i = 0; i < room.walls.Count; i++)
            {
                if (room.walls[i].name == wallName)
                    return i;
            }

            return -1;
        }

        private static RoomFootprint PickWeighted(IReadOnlyList<RoomFootprint> library, System.Random rng)
        {
            int total = 0;
            for (int i = 0; i < library.Count; i++)
            {
                if (library[i] != null)
                    total += library[i].SpawnWeight;
            }

            if (total <= 0)
                return library[0];

            int roll = rng.Next(total);
            int cumulative = 0;
            for (int i = 0; i < library.Count; i++)
            {
                RoomFootprint entry = library[i];
                if (entry == null)
                    continue;

                cumulative += entry.SpawnWeight;
                if (roll < cumulative)
                    return entry;
            }

            return library[library.Count - 1];
        }

        private static void Shuffle(List<int> values, System.Random rng)
        {
            for (int i = values.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (values[i], values[j]) = (values[j], values[i]);
            }
        }

        private static float QuantizeHalfPi(float radians)
        {
            float halfPi = Mathf.PI * 0.5f;
            return Mathf.Round(radians / halfPi) * halfPi;
        }

        private static Vector2 Rotate(Vector2 value, float radians)
        {
            float c = Mathf.Cos(radians);
            float s = Mathf.Sin(radians);
            return new Vector2(value.x * c - value.y * s, value.x * s + value.y * c);
        }

        private static Vector2 TransformPoint(Vector2 local, Vector2 position, float yawRadians) =>
            position + Rotate(local, yawRadians);

        private static SocketDirection RotateDirection(SocketDirection direction, float yawRadians)
        {
            if (direction == SocketDirection.Unknown)
                return SocketDirection.Unknown;

            int steps = Mathf.RoundToInt(yawRadians / (Mathf.PI * 0.5f));
            int index = direction switch
            {
                SocketDirection.North => 0,
                SocketDirection.East => 1,
                SocketDirection.South => 2,
                SocketDirection.West => 3,
                _ => 0
            };

            int rotated = ((index + steps) % 4 + 4) % 4;
            return rotated switch
            {
                0 => SocketDirection.North,
                1 => SocketDirection.East,
                2 => SocketDirection.South,
                3 => SocketDirection.West,
                _ => SocketDirection.Unknown
            };
        }
    }
}
