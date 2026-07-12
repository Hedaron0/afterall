using System;
using System.Collections.Generic;
using UnityEngine;

namespace AfterAll.Environment
{
    /// <summary>
    /// Pure-data settlement-spine layout planner (no Instantiate).
    /// Start hub → settlement clusters → corridor bridges toward exit → optional stubs.
    /// No random frontier infill; no proximity links.
    /// </summary>
    public static class SettlementSpinePlanner
    {
        private const float OverlapEpsilon = 0.05f;
        private const float OverlapInsetM = 0.2f;
        private const float DirectionDotMin = 0.35f;

        private enum PrefabPickMode
        {
            Settlement,
            Corridor,
            Stub
        }

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
            public WallOpeningMode openingMode;
        }

        public static LayoutPlan Generate(
            IReadOnlyList<RoomFootprint> library,
            int seed,
            SettlementSpineConfig config)
        {
            config.Clamp();
            if (config.gapPolicy.edgeMarginM <= 0f && config.gapPolicy.spanFraction <= 0f)
                config.gapPolicy = GapOffsetPolicy.Default;

            config.gapPolicy.randomGapOffset = config.randomGapOffset;

            var plan = new LayoutPlan
            {
                seed = seed,
                roomCount = config.EstimatedRoomCount,
                settlementCount = config.settlementCount,
                roomsPerSettlement = config.roomsPerSettlement,
                corridorRoomsPerBridge = config.corridorRoomsPerBridge,
                stubBudget = config.stubBudget,
                randomGapOffset = config.randomGapOffset,
                exitIndex = -1
            };

            if (library == null || library.Count == 0)
            {
                plan.notes = "Empty footprint library.";
                return plan;
            }

            var rng = new System.Random(seed);
            var placed = new List<PlannedRoom>();
            int failed = 0;
            Vector2 exitBias = config.ExitBiasVector;

            RoomFootprint hubFootprint = PickHub(library);
            placed.Add(CreateRoom(hubFootprint, Vector2.zero, 0f));

            int bridgeTipIndex = 0;
            int corridorPlaced = 0;
            int stubPlaced = 0;

            for (int settlement = 0; settlement < config.settlementCount; settlement++)
            {
                var cluster = new List<int> { bridgeTipIndex };

                GrowSettlement(
                    placed,
                    cluster,
                    library,
                    rng,
                    config.gapPolicy,
                    plan,
                    config.roomsPerSettlement,
                    PrefabPickMode.Settlement,
                    preferDirection: null,
                    ref failed);

                int stubsThisCluster = Mathf.Min(config.stubBudget, 2);
                for (int s = 0; s < stubsThisCluster; s++)
                {
                    if (TryGrowFromCluster(
                            placed,
                            cluster,
                            library,
                            rng,
                            config.gapPolicy,
                            plan,
                            PrefabPickMode.Stub,
                            preferDirection: null,
                            out int stubChild,
                            ref failed))
                    {
                        stubPlaced++;
                        // Stubs do not expand the spine cluster used for the next bridge.
                    }
                }

                if (settlement >= config.settlementCount - 1)
                {
                    bridgeTipIndex = PickFarthestInCluster(placed, cluster, exitBias);
                    break;
                }

                int bridgeStart = PickBestBridgeParent(placed, cluster, exitBias, rng);
                int tip = bridgeStart;
                for (int c = 0; c < config.corridorRoomsPerBridge; c++)
                {
                    if (!TryGrowFrom(
                            placed,
                            tip,
                            library,
                            rng,
                            config.gapPolicy,
                            plan,
                            PrefabPickMode.Corridor,
                            exitBias,
                            out int childIndex,
                            ref failed))
                        break;

                    tip = childIndex;
                    corridorPlaced++;
                    // Corridor rooms are not settlement members.
                }

                bridgeTipIndex = tip;
            }

            plan.exitIndex = bridgeTipIndex;

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
                $"SettlementSpine placed={plan.PlacedCount}, settlements={config.settlementCount}, " +
                $"roomsPerSettlement={config.roomsPerSettlement}, bridge={config.corridorRoomsPerBridge}, " +
                $"corridors={corridorPlaced}, stubs={stubPlaced}, exit={plan.exitIndex}, " +
                $"bias={config.exitBias}, failed={failed}, randomGap={config.randomGapOffset}";
            return plan;
        }

        private static void GrowSettlement(
            List<PlannedRoom> placed,
            List<int> cluster,
            IReadOnlyList<RoomFootprint> library,
            System.Random rng,
            GapOffsetPolicy gapPolicy,
            LayoutPlan plan,
            int roomsPerSettlement,
            PrefabPickMode mode,
            Vector2? preferDirection,
            ref int failed)
        {
            int target = Mathf.Max(1, roomsPerSettlement);
            // Cluster already has the seed room; grow until cluster size hits target.
            int guard = target * 12;
            while (cluster.Count < target && guard-- > 0)
            {
                if (!TryGrowFromCluster(
                        placed,
                        cluster,
                        library,
                        rng,
                        gapPolicy,
                        plan,
                        mode,
                        preferDirection,
                        out int childIndex,
                        ref failed))
                    break;

                cluster.Add(childIndex);
            }
        }

        private static bool TryGrowFromCluster(
            List<PlannedRoom> placed,
            List<int> cluster,
            IReadOnlyList<RoomFootprint> library,
            System.Random rng,
            GapOffsetPolicy gapPolicy,
            LayoutPlan plan,
            PrefabPickMode mode,
            Vector2? preferDirection,
            out int childIndex,
            ref int failed)
        {
            childIndex = -1;
            var parents = new List<int>(cluster);
            Shuffle(parents, rng);

            // Prefer denser packing: parents with fewer open walls first (more "interior"),
            // then shuffle ties via the shuffled list order.
            parents.Sort((a, b) =>
                CollectOpenDoorWallIndices(placed[a]).Count.CompareTo(CollectOpenDoorWallIndices(placed[b]).Count));

            foreach (int parentIndex in parents)
            {
                if (TryGrowFrom(
                        placed,
                        parentIndex,
                        library,
                        rng,
                        gapPolicy,
                        plan,
                        mode,
                        preferDirection,
                        out childIndex,
                        ref failed))
                    return true;
            }

            return false;
        }

        private static bool TryGrowFrom(
            List<PlannedRoom> placed,
            int parentIndex,
            IReadOnlyList<RoomFootprint> library,
            System.Random rng,
            GapOffsetPolicy gapPolicy,
            LayoutPlan plan,
            PrefabPickMode mode,
            Vector2? preferDirection,
            out int childIndex,
            ref int failed)
        {
            childIndex = -1;
            PlannedRoom parent = placed[parentIndex];
            List<int> openWallIndices = CollectOpenDoorWallIndices(parent);
            if (openWallIndices.Count == 0)
                return false;

            OrderWalls(openWallIndices, parent, preferDirection, rng);

            List<int> prefabOrder = BuildPrefabOrder(library, mode, rng);
            if (prefabOrder.Count == 0)
                return false;

            foreach (int parentWallIndex in openWallIndices)
            {
                PlannedWall parentWall = parent.walls[parentWallIndex];
                if (preferDirection.HasValue &&
                    mode == PrefabPickMode.Corridor &&
                    Vector2.Dot(parentWall.outward, preferDirection.Value) < DirectionDotMin)
                {
                    // Soft skip — still allow later as fallback by keeping them at end of OrderWalls.
                }

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

                        float childOpening = ResolveOpeningWidth(childWallDef, childFootprint.GapWidthM);
                        if (!WallGapController.AreOpeningsPairable(
                                parentWall.gapWidthM,
                                parentWall.lengthM,
                                childOpening,
                                childWallDef.lengthM))
                        {
                            failed++;
                            continue;
                        }

                        float childOffset = SampleGapOffset(
                            ToPlannedWallLocal(childWallDef, childOpening),
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

                        // Corridor: require child placement advances along exit bias.
                        if (mode == PrefabPickMode.Corridor && preferDirection.HasValue)
                        {
                            Vector2 delta = candidate.positionXZ - parent.positionXZ;
                            if (delta.sqrMagnitude > 0.01f &&
                                Vector2.Dot(delta.normalized, preferDirection.Value) < 0f)
                            {
                                failed++;
                                continue;
                            }
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

        private static void OrderWalls(
            List<int> wallIndices,
            PlannedRoom parent,
            Vector2? preferDirection,
            System.Random rng)
        {
            Shuffle(wallIndices, rng);
            if (!preferDirection.HasValue)
                return;

            Vector2 dir = preferDirection.Value;
            wallIndices.Sort((a, b) =>
            {
                float da = Vector2.Dot(parent.walls[a].outward, dir);
                float db = Vector2.Dot(parent.walls[b].outward, dir);
                return db.CompareTo(da);
            });
        }

        private static List<int> BuildPrefabOrder(
            IReadOnlyList<RoomFootprint> library,
            PrefabPickMode mode,
            System.Random rng)
        {
            var preferred = new List<int>();
            var fallback = new List<int>();

            for (int i = 0; i < library.Count; i++)
            {
                RoomFootprint fp = library[i];
                if (fp == null)
                    continue;

                RoomRole role = fp.ResolvedRole;
                bool match = mode switch
                {
                    PrefabPickMode.Corridor => role == RoomRole.Corridor,
                    PrefabPickMode.Settlement => role == RoomRole.Room || role == RoomRole.Hub,
                    PrefabPickMode.Stub => role == RoomRole.Room,
                    _ => true
                };

                if (match)
                    preferred.Add(i);
                else if (mode == PrefabPickMode.Settlement && role != RoomRole.Corridor)
                    fallback.Add(i);
                else if (mode == PrefabPickMode.Stub && role != RoomRole.Corridor)
                    fallback.Add(i);
                else if (mode == PrefabPickMode.Corridor && role != RoomRole.Corridor)
                    fallback.Add(i);
            }

            if (mode == PrefabPickMode.Settlement || mode == PrefabPickMode.Stub)
            {
                preferred.Sort((a, b) => library[b].BoundsAreaM2.CompareTo(library[a].BoundsAreaM2));
                // Light shuffle among similar sizes so seeds vary.
                SoftShuffleFront(preferred, rng, Mathf.Min(4, preferred.Count));
            }
            else
            {
                Shuffle(preferred, rng);
            }

            Shuffle(fallback, rng);
            preferred.AddRange(fallback);
            return preferred;
        }

        private static void SoftShuffleFront(List<int> values, System.Random rng, int count)
        {
            for (int i = count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (values[i], values[j]) = (values[j], values[i]);
            }
        }

        private static RoomFootprint PickHub(IReadOnlyList<RoomFootprint> library)
        {
            RoomFootprint bestHub = null;
            float bestHubArea = -1f;
            RoomFootprint largest = null;
            float largestArea = -1f;

            for (int i = 0; i < library.Count; i++)
            {
                RoomFootprint entry = library[i];
                if (entry == null)
                    continue;

                float area = entry.BoundsAreaM2;
                if (area > largestArea)
                {
                    largestArea = area;
                    largest = entry;
                }

                if (entry.ResolvedRole == RoomRole.Hub && area > bestHubArea)
                {
                    bestHubArea = area;
                    bestHub = entry;
                }
            }

            return bestHub ?? largest ?? library[0];
        }

        private static int PickFarthestInCluster(List<PlannedRoom> placed, List<int> cluster, Vector2 exitBias)
        {
            int best = cluster[0];
            float bestScore = float.NegativeInfinity;
            foreach (int index in cluster)
            {
                float score = Vector2.Dot(placed[index].positionXZ, exitBias);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = index;
                }
            }

            return best;
        }

        private static int PickBestBridgeParent(
            List<PlannedRoom> placed,
            List<int> cluster,
            Vector2 exitBias,
            System.Random rng)
        {
            var candidates = new List<int>();
            foreach (int index in cluster)
            {
                PlannedRoom room = placed[index];
                List<int> walls = CollectOpenDoorWallIndices(room);
                foreach (int wallIndex in walls)
                {
                    if (Vector2.Dot(room.walls[wallIndex].outward, exitBias) >= DirectionDotMin)
                    {
                        candidates.Add(index);
                        break;
                    }
                }
            }

            if (candidates.Count == 0)
                return PickFarthestInCluster(placed, cluster, exitBias);

            return candidates[rng.Next(candidates.Count)];
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
                    gapWidthM = ResolveOpeningWidth(wall, footprint.GapWidthM),
                    openingMode = wall.openingMode
                });
            }

            Vector2 c0 = TransformPoint(new Vector2(footprint.BoundsMin.x, footprint.BoundsMin.y), positionXZ, yawRadians);
            Vector2 c1 = TransformPoint(new Vector2(footprint.BoundsMin.x, footprint.BoundsMax.y), positionXZ, yawRadians);
            Vector2 c2 = TransformPoint(new Vector2(footprint.BoundsMax.x, footprint.BoundsMin.y), positionXZ, yawRadians);
            Vector2 c3 = TransformPoint(new Vector2(footprint.BoundsMax.x, footprint.BoundsMax.y), positionXZ, yawRadians);
            Vector2 boundsMin = Vector2.Min(Vector2.Min(c0, c1), Vector2.Min(c2, c3));
            Vector2 boundsMax = Vector2.Max(Vector2.Max(c0, c1), Vector2.Max(c2, c3));

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

            if (aMinI.x >= aMaxI.x - OverlapEpsilon || aMinI.y >= aMaxI.y - OverlapEpsilon)
                return false;
            if (bMinI.x >= bMaxI.x - OverlapEpsilon || bMinI.y >= bMaxI.y - OverlapEpsilon)
                return false;

            return aMinI.x < bMaxI.x - OverlapEpsilon &&
                   aMaxI.x > bMinI.x + OverlapEpsilon &&
                   aMinI.y < bMaxI.y - OverlapEpsilon &&
                   aMaxI.y > bMinI.y + OverlapEpsilon;
        }

        private static float ResolveOpeningWidth(RoomFootprint.Wall wall, float footprintDefault)
        {
            if (wall.openingWidthM > 0.05f)
                return wall.openingWidthM;

            if (wall.openingMode == WallOpeningMode.FullWall || wall.openingMode == WallOpeningMode.OpenEnd)
                return Mathf.Max(0.05f, wall.lengthM - 0.05f);

            return footprintDefault > 0.05f ? footprintDefault : RoomFootprint.DefaultGapWidthM;
        }

        private static float SampleGapOffset(PlannedWall wall, System.Random rng, GapOffsetPolicy policy)
        {
            bool forceCenter = wall.openingMode == WallOpeningMode.FullWall ||
                               wall.openingMode == WallOpeningMode.OpenEnd ||
                               wall.gapWidthM >= wall.lengthM - 0.2f;

            if (!TryGetOffsetRange(wall.lengthM, wall.gapWidthM, policy, out float min, out float max))
                return 0f;

            if (forceCenter || !policy.randomGapOffset || rng == null || Mathf.Abs(max - min) < 0.0001f)
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
                direction = wall.direction,
                openingMode = wall.openingMode
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
