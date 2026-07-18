using System;
using System.Collections.Generic;
using UnityEngine;

namespace AfterAll.Environment
{
    /// <summary>
    /// Hub-Centric ClusterSpine planner (no Instantiate).
    ///
    /// Core design:
    ///   • Every cluster SEEDS from a hub room (4+ doors) so the cluster fans
    ///     out in multiple directions instead of chaining.
    ///   • Hub's lateral walls are filled first → star topology, not chain.
    ///   • Heading ALWAYS turns exactly 90° between clusters → no strip.
    ///   • Footprint reuse is limited by area bracket → room10 max 1×,
    ///     mid rooms max 2×, small rooms max 3×.
    ///   • Bridge retries have a hard per-node cap → no infinite loop.
    ///   • All low-level snap/overlap/gap helpers are unchanged.
    /// </summary>
    public static class PaintGrowthPlanner
    {
        // ── Constants ──────────────────────────────────────────────────────────
        private const float OverlapEpsilon      = 0.05f;
        private const float OverlapInsetM       = 0.2f;
        private const float DirectionDotMin     = 0.25f;
        private const int   MaxStall            = 18;
        private const int   MaxBridgeRetries    = 5;   // per spine node

        private const int   MinSpineNodes       = 2;
        private const int   MaxSpineNodes       = 5;
        private const int   MinClusterSize      = 2;
        private const int   MaxClusterSize      = 6;

        private const float BranchProbability   = 0.55f;
        private const int   MaxBranchesPerCluster = 2;
        private const int   MaxBranchRooms      = 2;

        // ── Enums ─────────────────────────────────────────────────────────────
        private enum PrefabPickMode { Hub, FatOrMedium, Corridor, Stub }
        private enum WallPrefer    { Any, AlongHeading, FattenCluster, LateralToHeading, NotAlongHeading }

        // ── Data structures ───────────────────────────────────────────────────
        private struct PlannedRoom
        {
            public RoomFootprint     footprint;
            public Vector2           positionXZ;
            public float             yawRadians;
            public List<PlannedWall> walls;
            public Vector2           boundsMin;
            public Vector2           boundsMax;
            public int               connectionCount;
        }

        private struct PlannedWall
        {
            public string          name;
            public Vector2         seam;
            public Vector2         axis;
            public Vector2         start;
            public Vector2         end;
            public Vector2         outward;
            public SocketDirection direction;
            public bool            doorValid;
            public float           lengthM;
            public bool            isConnected;
            public float           gapWidthM;
            public WallOpeningMode openingMode;
        }

        // ══════════════════════════════════════════════════════════════════════
        //  PUBLIC ENTRY POINT
        // ══════════════════════════════════════════════════════════════════════
        public static LayoutPlan Generate(
            IReadOnlyList<RoomFootprint> library,
            int                          seed,
            PaintGrowthConfig            config,
            RoomFootprint                elevatorFootprint = null)
        {
            config.Clamp();
            if (config.gapPolicy.edgeMarginM <= 0f && config.gapPolicy.spanFraction <= 0f)
                config.gapPolicy = GapOffsetPolicy.Default;
            config.gapPolicy.randomGapOffset = config.randomGapOffset;

            var plan = new LayoutPlan
            {
                seed            = seed,
                roomCount       = config.targetRoomCount,
                randomGapOffset = config.randomGapOffset,
                exitIndex       = -1
            };

            if (library == null || library.Count == 0)
            {
                plan.notes = "Empty footprint library.";
                return plan;
            }

            var rng        = new System.Random(seed);
            var placed     = new List<PlannedRoom>();
            var usedCounts = new Dictionary<string, int>(); // footprint reuse tracking

            int failed = 0, packed = 0, necks = 0, stubs = 0, turns = 0, groups = 0;

            // ── Spine sizing ───────────────────────────────────────────────────
            int spineNodes      = Mathf.Clamp(config.targetRoomCount / 4, MinSpineNodes, MaxSpineNodes);
            int roomsPerCluster = Mathf.Clamp(
                Mathf.Max(MinClusterSize, config.targetRoomCount / spineNodes - 1),
                MinClusterSize, MaxClusterSize);

            // ── Seed first cluster with hub room (most doors wins) ──────────────
            RoomFootprint seedFp = PickHubSeed(library, usedCounts, rng);
            placed.Add(CreateRoom(seedFp, Vector2.zero, 0f));
            TrackUsed(usedCounts, seedFp);

            // Reserve one of the hub's doors for the elevator so normal growth
            // (FillCluster/GrowBranches/Bridge) never consumes it. Attached last.
            int reservedElevatorWall = -1;
            if (elevatorFootprint != null)
            {
                List<int> hubOpenWalls = CollectOpenDoorWallIndices(placed[0]);
                if (hubOpenWalls.Count > 0)
                {
                    reservedElevatorWall = hubOpenWalls[rng.Next(hubOpenWalls.Count)];
                    PlannedRoom hubRoom = placed[0];
                    PlannedWall reservedWall = hubRoom.walls[reservedElevatorWall];
                    reservedWall.isConnected = true; // temp reserve, unmarked before attach
                    hubRoom.walls[reservedElevatorWall] = reservedWall;
                    placed[0] = hubRoom;
                }
            }

            var currentCluster = new List<int> { 0 };
            Vector2 heading    = CardinalFromIndex(rng.Next(4));
            groups             = 1;

            // Fill first cluster (hub-first)
            FillCluster(placed, currentCluster, library, rng,
                        config.gapPolicy, plan,
                        roomsPerCluster, heading, usedCounts,
                        ref failed, ref packed);

            GrowBranches(placed, currentCluster, library, rng,
                         config.gapPolicy, plan,
                         heading, usedCounts, ref failed, ref stubs);

            // ── Spine iteration ────────────────────────────────────────────────
            for (int node = 1; node < spineNodes && placed.Count < config.targetRoomCount; node++)
            {
                // ALWAYS turn 90° at every cluster boundary — kills the strip
                heading = TurnExact90(heading, rng);
                turns++;

                // Bridge with hard per-node retry cap
                int seedIndex = -1;
                for (int retry = 0; retry < MaxBridgeRetries && seedIndex < 0; retry++)
                {
                    seedIndex = BridgeToNextCluster(
                        placed, currentCluster, library, rng,
                        config.gapPolicy, plan,
                        ref heading, ref failed, ref necks, ref packed, ref turns,
                        usedCounts);

                    if (seedIndex < 0)
                    {
                        heading = TurnExact90(heading, rng);
                        turns++;
                    }
                }

                if (seedIndex < 0) continue; // bridge failed → skip node, no infinite loop

                groups++;
                currentCluster = new List<int> { seedIndex };
                TrackUsed(usedCounts, placed[seedIndex].footprint);

                // If bridge landed on a corridor or tiny room, promote to hub
                if (placed[seedIndex].footprint.IsCorridorShape ||
                    placed[seedIndex].footprint.BoundsAreaM2 < 100f)
                {
                    if (placed.Count < plan.roomCount &&
                        TryGrowFrom(placed, seedIndex, library, rng,
                            config.gapPolicy, plan,
                            PrefabPickMode.Hub, WallPrefer.AlongHeading, heading,
                            out int hubChild, ref failed,
                            null, usedCounts))
                    {
                        packed++;
                        TrackUsed(usedCounts, placed[hubChild].footprint);
                        currentCluster = new List<int> { hubChild };
                        seedIndex = hubChild;
                    }
                }

                int remaining = config.targetRoomCount - placed.Count;
                int clusterTarget = Mathf.Clamp(roomsPerCluster, MinClusterSize,
                                        Mathf.Max(MinClusterSize, remaining));

                FillCluster(placed, currentCluster, library, rng,
                            config.gapPolicy, plan,
                            clusterTarget, heading, usedCounts,
                            ref failed, ref packed);

                GrowBranches(placed, currentCluster, library, rng,
                             config.gapPolicy, plan,
                             heading, usedCounts, ref failed, ref stubs);

                // Small chance of extra turn before next bridge (organic feel)
                if (rng.NextDouble() < 0.25)
                {
                    heading = TurnExact90(heading, rng);
                    turns++;
                }
            }

            // ── Overflow: keep filling last cluster if under budget ─────────────
            int overflow = 0;
            while (placed.Count < config.targetRoomCount && overflow < 400)
            {
                overflow++;
                if (!TryGrowOntoBlob(placed, currentCluster, library, rng,
                        config.gapPolicy, plan,
                        PrefabPickMode.FatOrMedium, WallPrefer.Any, heading,
                        out int extra, ref failed, usedCounts))
                    break;
                packed++;
                if (!currentCluster.Contains(extra)) currentCluster.Add(extra);
            }

            // ── Attach elevator to its reserved hub door (last, after everything) ─
            if (elevatorFootprint != null)
            {
                if (reservedElevatorWall >= 0)
                {
                    PlannedRoom hubRoom = placed[0];
                    PlannedWall reservedWall = hubRoom.walls[reservedElevatorWall];
                    reservedWall.isConnected = false; // un-reserve so TryGrowFrom can use it
                    hubRoom.walls[reservedElevatorWall] = reservedWall;
                    placed[0] = hubRoom;
                }

                var elevatorLibrary = new List<RoomFootprint> { elevatorFootprint };
                List<int> forcedWalls = reservedElevatorWall >= 0
                    ? new List<int> { reservedElevatorWall }
                    : null;

                if (TryGrowFrom(placed, 0, elevatorLibrary, rng, config.gapPolicy, plan,
                        PrefabPickMode.Stub, WallPrefer.Any, Vector2.zero,
                        out int elevatorIndex, ref failed, forcedWalls, usedCounts))
                {
                    plan.elevatorIndex = elevatorIndex;
                }
            }

            // ── Emit LayoutPlan ────────────────────────────────────────────────
            plan.exitIndex = PickExitIndex(placed);
            for (int i = 0; i < placed.Count; i++)
            {
                PlannedRoom r = placed[i];
                plan.placements.Add(new LayoutPlanPlacement
                {
                    index      = i,
                    prefabId   = r.footprint.PrefabId,
                    positionXZ = r.positionXZ,
                    yawDegrees = Mathf.Round(r.yawRadians * Mathf.Rad2Deg / 90f) * 90f
                });
            }

            plan.failedAttempts = failed;
            plan.notes =
                $"HubCentric target={config.targetRoomCount}, placed={plan.PlacedCount}, " +
                $"groups={groups}, pack={packed}, neck={necks}, stub={stubs}, turns={turns}, " +
                $"exit={plan.exitIndex}, failed={failed}, elevator={plan.elevatorIndex}" +
                (elevatorFootprint != null && plan.elevatorIndex < 0 ? " (ATTACH FAILED)" : "");
            return plan;
        }

        // ══════════════════════════════════════════════════════════════════════
        //  CLUSTER FILL — hub-first star topology, then expand
        // ══════════════════════════════════════════════════════════════════════
        private static void FillCluster(
            List<PlannedRoom>            placed,
            List<int>                    cluster,
            IReadOnlyList<RoomFootprint> library,
            System.Random                rng,
            GapOffsetPolicy              gapPolicy,
            LayoutPlan                   plan,
            int                          targetSize,
            Vector2                      heading,
            Dictionary<string, int>      usedCounts,
            ref int                      failed,
            ref int                      packed)
        {
            // ── Phase A: fill hub's LATERAL walls first ────────────────────────
            // This fans rooms outward from the hub, avoiding a chain.
            // Walls facing heading direction are reserved for the bridge.
            if (cluster.Count > 0)
            {
                int hubIdx = cluster[0];

                List<int> hubWalls = CollectOpenDoorWallIndices(placed[hubIdx]);

                // Sort ascending by |dot(outward, heading)| → lateral walls first
                hubWalls.Sort((a, b) =>
                {
                    float dotA = Mathf.Abs(Vector2.Dot(placed[hubIdx].walls[a].outward, heading));
                    float dotB = Mathf.Abs(Vector2.Dot(placed[hubIdx].walls[b].outward, heading));
                    return dotA.CompareTo(dotB);
                });

                foreach (int wi in hubWalls)
                {
                    if (cluster.Count >= targetSize || placed.Count >= plan.roomCount) break;

                    // Skip walls strongly facing heading (save for bridge)
                    float dot = Vector2.Dot(placed[hubIdx].walls[wi].outward, heading);
                    if (dot > 0.55f) continue;

                    var forced = new List<int> { wi };
                    if (TryGrowFrom(placed, hubIdx, library, rng, gapPolicy, plan,
                            PrefabPickMode.FatOrMedium, WallPrefer.Any, Vector2.zero,
                            out int child, ref failed, forced, usedCounts))
                    {
                        packed++;
                        if (!cluster.Contains(child)) cluster.Add(child);
                    }
                }
            }

            // ── Phase B: continue expanding from any cluster room ──────────────
            int stall = 0;
            while (cluster.Count < targetSize && placed.Count < plan.roomCount && stall < MaxStall)
            {
                int parentIdx = PickWeightedParent(placed, cluster, rng);
                if (parentIdx < 0) { stall++; continue; }

                Vector2 fattenAxis = ComputeFattenAxis(placed, cluster);

                bool added = TryGrowFrom(placed, parentIdx, library, rng, gapPolicy, plan,
                    PrefabPickMode.FatOrMedium, WallPrefer.FattenCluster, fattenAxis,
                    out int child2, ref failed, null, usedCounts);

                if (!added)
                    added = TryGrowFrom(placed, parentIdx, library, rng, gapPolicy, plan,
                        PrefabPickMode.FatOrMedium, WallPrefer.Any, Vector2.zero,
                        out child2, ref failed, null, usedCounts);

                if (!added) { stall++; continue; }

                stall = 0;
                packed++;
                if (!cluster.Contains(child2)) cluster.Add(child2);
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        //  BRANCH GROWER — lateral dead-end arms
        // ══════════════════════════════════════════════════════════════════════
        private static void GrowBranches(
            List<PlannedRoom>            placed,
            List<int>                    cluster,
            IReadOnlyList<RoomFootprint> library,
            System.Random                rng,
            GapOffsetPolicy              gapPolicy,
            LayoutPlan                   plan,
            Vector2                      heading,
            Dictionary<string, int>      usedCounts,
            ref int                      failed,
            ref int                      stubs)
        {
            if (rng.NextDouble() > BranchProbability) return;

            Vector2 lateralA = new Vector2(-heading.y,  heading.x);
            Vector2 lateralB = new Vector2( heading.y, -heading.x);

            int branchCount = 1 + rng.Next(MaxBranchesPerCluster);
            for (int b = 0; b < branchCount && placed.Count < plan.roomCount; b++)
            {
                Vector2 branchDir    = (rng.NextDouble() < 0.5) ? lateralA : lateralB;
                int     branchParent = PickFrontierRoom(placed, cluster, branchDir);
                if (branchParent < 0) continue;

                int branchLen = 1 + rng.Next(MaxBranchRooms);
                int tip       = branchParent;

                for (int n = 0; n < branchLen && placed.Count < plan.roomCount; n++)
                {
                    PrefabPickMode mode = (rng.NextDouble() < 0.5)
                        ? PrefabPickMode.FatOrMedium : PrefabPickMode.Stub;

                    if (!TryGrowFrom(placed, tip, library, rng, gapPolicy, plan,
                            mode, WallPrefer.AlongHeading, branchDir,
                            out int branchChild, ref failed, null, usedCounts))
                        break;

                    stubs++;
                    tip = branchChild;
                }
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        //  BRIDGE — hub's heading wall → corridor neck → next cluster seed
        // ══════════════════════════════════════════════════════════════════════
        private static int BridgeToNextCluster(
            List<PlannedRoom>            placed,
            List<int>                    cluster,
            IReadOnlyList<RoomFootprint> library,
            System.Random                rng,
            GapOffsetPolicy              gapPolicy,
            LayoutPlan                   plan,
            ref Vector2                  heading,
            ref int                      failed,
            ref int                      necks,
            ref int                      packed,
            ref int                      turns,
            Dictionary<string, int>      usedCounts)
        {
            if (placed.Count >= plan.roomCount) return -1;

            // Find hub's heading-facing walls (prefer using hub for bridge exit)
            int hubIdx = cluster[0];
            var headingWalls = new List<int>();
            foreach (int wi in CollectOpenDoorWallIndices(placed[hubIdx]))
                if (Vector2.Dot(placed[hubIdx].walls[wi].outward, heading) >= 0.5f)
                    headingWalls.Add(wi);

            // Fall back to frontier room if hub has no heading walls
            int sourceIdx = headingWalls.Count > 0 ? hubIdx : PickFrontierRoom(placed, cluster, heading);
            List<int> forcedOnFirst = headingWalls.Count > 0 ? headingWalls : null;

            // 25% chance: direct fat/hub connection (cluster touching cluster)
            if (rng.NextDouble() < 0.25)
            {
                if (TryGrowFrom(placed, sourceIdx, library, rng, gapPolicy, plan,
                        PrefabPickMode.Hub, WallPrefer.AlongHeading, heading,
                        out int fatSeed, ref failed, forcedOnFirst, usedCounts))
                {
                    packed++;
                    TrackUsed(usedCounts, placed[fatSeed].footprint);
                    return fatSeed;
                }
                // Fallback to any fat
                if (TryGrowFrom(placed, sourceIdx, library, rng, gapPolicy, plan,
                        PrefabPickMode.FatOrMedium, WallPrefer.AlongHeading, heading,
                        out int fatSeed2, ref failed, forcedOnFirst, usedCounts))
                {
                    packed++;
                    return fatSeed2;
                }
            }

            // Corridor neck: 1–2 rooms
            int neckLen = 1 + rng.Next(2);
            int tip     = sourceIdx;
            int grown   = 0;

            for (int n = 0; n < neckLen && placed.Count < plan.roomCount; n++)
            {
                List<int> forced = (n == 0) ? forcedOnFirst : null;
                bool useCorridor = rng.NextDouble() < 0.70;
                PrefabPickMode mode = useCorridor ? PrefabPickMode.Corridor : PrefabPickMode.FatOrMedium;

                if (!TryGrowFrom(placed, tip, library, rng, gapPolicy, plan,
                        mode, WallPrefer.AlongHeading, heading,
                        out int child, ref failed, forced, usedCounts))
                {
                    // Fallback: fat room as bridge piece
                    if (!TryGrowFrom(placed, tip, library, rng, gapPolicy, plan,
                            PrefabPickMode.FatOrMedium, WallPrefer.AlongHeading, heading,
                            out child, ref failed, forced, usedCounts))
                        break;
                    packed++;
                }
                else necks++;

                tip = child;
                grown++;

                // Optional mid-bridge turn (keeps path organic)
                if (n == 0 && rng.NextDouble() < 0.35)
                {
                    heading = TurnHeading(heading, rng);
                    turns++;
                }
            }

            if (grown == 0) return -1;

            // Land on a hub/fat seed if bridge tip is a corridor
            if (placed.Count < plan.roomCount && placed[tip].footprint.IsCorridorShape)
            {
                if (TryGrowFrom(placed, tip, library, rng, gapPolicy, plan,
                        PrefabPickMode.Hub, WallPrefer.AlongHeading, heading,
                        out int hubChild, ref failed, null, usedCounts))
                {
                    packed++;
                    TrackUsed(usedCounts, placed[hubChild].footprint);
                    return hubChild;
                }
                if (TryGrowFrom(placed, tip, library, rng, gapPolicy, plan,
                        PrefabPickMode.FatOrMedium, WallPrefer.AlongHeading, heading,
                        out int fatChild, ref failed, null, usedCounts))
                {
                    packed++;
                    return fatChild;
                }
            }

            return tip;
        }

        // ══════════════════════════════════════════════════════════════════════
        //  HUB SEED PICKER — prefers rooms with the most doors
        // ══════════════════════════════════════════════════════════════════════
        /// <summary>Pick the best hub seed: most doors → biggest area, respecting reuse limits.</summary>
        private static RoomFootprint PickHubSeed(
            IReadOnlyList<RoomFootprint> library,
            Dictionary<string, int>      usedCounts,
            System.Random                rng)
        {
            RoomFootprint best      = null;
            float         bestScore = -1f;

            for (int i = 0; i < library.Count; i++)
            {
                RoomFootprint fp = library[i];
                if (fp == null || fp.IsCorridorShape) continue;

                int used     = usedCounts.GetValueOrDefault(fp.PrefabId, 0);
                int maxReuse = GetMaxReuse(fp);
                if (used >= maxReuse) continue;

                int   doors = CountDoorWalls(fp);
                float score = doors * 200f + fp.BoundsAreaM2 * 0.01f
                              + (float)rng.NextDouble() * 30f;
                if (score > bestScore) { bestScore = score; best = fp; }
            }

            return best ?? PickLargestNonCorridor(library);
        }

        // ══════════════════════════════════════════════════════════════════════
        //  REUSE TRACKING & LIMITS
        // ══════════════════════════════════════════════════════════════════════
        /// <summary>
        /// Max times a footprint may appear in one map.
        /// Large rooms (≥1000m²): once. Mid rooms (≥300m²): twice. Small/corridor: 3+.
        /// </summary>
        private static int GetMaxReuse(RoomFootprint fp)
        {
            if (fp.IsCorridorShape)    return 999;
            if (fp.BoundsAreaM2 >= 1000f) return 1;
            if (fp.BoundsAreaM2 >= 300f)  return 2;
            return 3;
        }

        private static void TrackUsed(Dictionary<string, int> used, RoomFootprint fp)
        {
            if (fp == null) return;
            if (!used.ContainsKey(fp.PrefabId)) used[fp.PrefabId] = 0;
            used[fp.PrefabId]++;
        }

        private static int CountDoorWalls(RoomFootprint fp)
        {
            int n = 0;
            for (int i = 0; i < fp.Walls.Length; i++)
                if (fp.Walls[i].doorValid) n++;
            return n;
        }

        // ══════════════════════════════════════════════════════════════════════
        //  PARENT / FRONTIER SELECTION
        // ══════════════════════════════════════════════════════════════════════
        private static int PickWeightedParent(
            List<PlannedRoom> placed, List<int> cluster, System.Random rng)
        {
            var weighted = new List<int>(cluster.Count * 4);
            foreach (int idx in cluster)
            {
                int open = CollectOpenDoorWallIndices(placed[idx]).Count;
                for (int w = 0; w < open; w++) weighted.Add(idx);
            }
            if (weighted.Count == 0) return -1;
            return weighted[rng.Next(weighted.Count)];
        }

        private static int PickFrontierRoom(
            List<PlannedRoom> placed, List<int> cluster, Vector2 direction)
        {
            if (cluster.Count == 0) return -1;
            if (direction.sqrMagnitude < 0.01f) return cluster[0];
            int best = cluster[0]; float bestScore = float.MinValue;
            foreach (int idx in cluster)
            {
                float dot   = Vector2.Dot(placed[idx].positionXZ, direction);
                int   open  = CollectOpenDoorWallIndices(placed[idx]).Count;
                float score = dot + open * 0.5f;
                if (score > bestScore) { bestScore = score; best = idx; }
            }
            return best;
        }

        // ══════════════════════════════════════════════════════════════════════
        //  HEADING UTILITIES
        // ══════════════════════════════════════════════════════════════════════
        /// <summary>Exactly 90° turn — never 0° or 180°. Eliminates strip.</summary>
        private static Vector2 TurnExact90(Vector2 heading, System.Random rng)
        {
            bool left = rng.NextDouble() < 0.5;
            return left
                ? new Vector2(-heading.y,  heading.x)
                : new Vector2( heading.y, -heading.x);
        }

        /// <summary>Soft turn: mostly ±90°, rarely 180°. Used mid-bridge.</summary>
        private static Vector2 TurnHeading(Vector2 heading, System.Random rng)
        {
            if (rng.NextDouble() < 0.15) return -heading;
            bool left = rng.NextDouble() < 0.5;
            return left
                ? new Vector2(-heading.y,  heading.x)
                : new Vector2( heading.y, -heading.x);
        }

        private static Vector2 CardinalFromIndex(int i) => i switch
        {
            0 => new Vector2( 1f,  0f),
            1 => new Vector2( 0f,  1f),
            2 => new Vector2(-1f,  0f),
            _ => new Vector2( 0f, -1f)
        };

        // ══════════════════════════════════════════════════════════════════════
        //  EXIT PICKER
        // ══════════════════════════════════════════════════════════════════════
        private static int PickExitIndex(List<PlannedRoom> placed)
        {
            if (placed.Count == 0) return -1;
            Vector2 origin = placed[0].positionXZ;
            int best = placed.Count - 1; float bestScore = -1f;
            for (int i = 1; i < placed.Count; i++)
            {
                float dist  = (placed[i].positionXZ - origin).sqrMagnitude;
                float bonus = placed[i].connectionCount <= 1 ? 1.25f : 1f;
                float score = dist * bonus;
                if (score > bestScore) { bestScore = score; best = i; }
            }
            return best;
        }

        // ══════════════════════════════════════════════════════════════════════
        //  TRY-GROW ONTO BLOB (overflow helper)
        // ══════════════════════════════════════════════════════════════════════
        private static bool TryGrowOntoBlob(
            List<PlannedRoom>            placed,
            List<int>                    blob,
            IReadOnlyList<RoomFootprint> library,
            System.Random                rng,
            GapOffsetPolicy              gapPolicy,
            LayoutPlan                   plan,
            PrefabPickMode               mode,
            WallPrefer                   wallPrefer,
            Vector2                      heading,
            out int                      childIndex,
            ref int                      failed,
            Dictionary<string, int>      usedCounts)
        {
            childIndex = -1;
            var parents = new List<int>(blob.Count > 0 ? blob : RangeList(placed.Count));
            Shuffle(parents, rng);

            Vector2 fattenAxis   = ComputeFattenAxis(placed, parents);
            Vector2 scoreHeading = wallPrefer == WallPrefer.FattenCluster ? fattenAxis : heading;
            Vector2 lateral      = new Vector2(-heading.y, heading.x);

            parents.Sort((a, b) =>
            {
                int sA = ParentBulkScore(placed[a], scoreHeading, lateral, wallPrefer);
                int sB = ParentBulkScore(placed[b], scoreHeading, lateral, wallPrefer);
                return sB.CompareTo(sA);
            });

            foreach (int pi in parents)
                if (TryGrowFrom(placed, pi, library, rng, gapPolicy, plan,
                        mode, wallPrefer, scoreHeading, out childIndex,
                        ref failed, null, usedCounts))
                    return true;
            return false;
        }

        private static Vector2 ComputeFattenAxis(List<PlannedRoom> placed, List<int> group)
        {
            if (group == null || group.Count == 0) return Vector2.right;
            Vector2 min = placed[group[0]].boundsMin, max = placed[group[0]].boundsMax;
            for (int i = 1; i < group.Count; i++)
            {
                min = Vector2.Min(min, placed[group[i]].boundsMin);
                max = Vector2.Max(max, placed[group[i]].boundsMax);
            }
            Vector2 ext = max - min;
            return ext.x <= ext.y ? new Vector2(1f, 0f) : new Vector2(0f, 1f);
        }

        private static int ParentBulkScore(
            PlannedRoom r, Vector2 scoreHeading, Vector2 lateral, WallPrefer prefer)
        {
            int open = 0, pref = 0;
            for (int i = 0; i < r.walls.Count; i++)
            {
                PlannedWall w = r.walls[i];
                if (!w.doorValid || w.isConnected) continue;
                open++;
                if (prefer == WallPrefer.FattenCluster)
                { if (Mathf.Abs(Vector2.Dot(w.outward, scoreHeading)) >= DirectionDotMin) pref++; }
                else if (Mathf.Abs(Vector2.Dot(w.outward, lateral)) >= DirectionDotMin) pref++;
            }
            int areaBonus = r.footprint.BoundsAreaM2 > 100f ? 5 : 0;
            return pref * 20 + open * 5 + areaBonus;
        }

        private static List<int> RangeList(int count)
        {
            var l = new List<int>(count);
            for (int i = 0; i < count; i++) l.Add(i);
            return l;
        }

        // ══════════════════════════════════════════════════════════════════════
        //  CORE: TRY GROW FROM ONE PARENT
        //  forcedWalls: if provided, only these wall indices are tried.
        //  usedCounts:  footprint reuse limits passed into BuildPrefabOrder.
        // ══════════════════════════════════════════════════════════════════════
        private static bool TryGrowFrom(
            List<PlannedRoom>            placed,
            int                          parentIndex,
            IReadOnlyList<RoomFootprint> library,
            System.Random                rng,
            GapOffsetPolicy              gapPolicy,
            LayoutPlan                   plan,
            PrefabPickMode               mode,
            WallPrefer                   wallPrefer,
            Vector2                      heading,
            out int                      childIndex,
            ref int                      failed,
            List<int>                    forcedWalls  = null,
            Dictionary<string, int>      usedCounts   = null)
        {
            childIndex = -1;
            PlannedRoom parent = placed[parentIndex];

            // Build wall candidate list
            List<int> openWallIndices;
            if (forcedWalls != null && forcedWalls.Count > 0)
            {
                openWallIndices = new List<int>();
                foreach (int wi in forcedWalls)
                    if (wi >= 0 && wi < parent.walls.Count &&
                        !parent.walls[wi].isConnected && parent.walls[wi].doorValid)
                        openWallIndices.Add(wi);
            }
            else
            {
                openWallIndices = CollectOpenDoorWallIndices(parent);
                OrderWalls(openWallIndices, parent, wallPrefer, heading, rng);
            }

            if (openWallIndices.Count == 0) return false;

            List<int> prefabOrder = BuildPrefabOrder(library, mode, rng, usedCounts);
            if (prefabOrder.Count == 0) return false;

            foreach (int parentWallIndex in openWallIndices)
            {
                PlannedWall parentWall   = parent.walls[parentWallIndex];
                float       parentOffset = SampleGapOffset(parentWall, rng, gapPolicy);

                foreach (int prefabIndex in prefabOrder)
                {
                    RoomFootprint      childFootprint   = library[prefabIndex];
                    List<int>          childWallIndices = CollectDoorWallIndices(childFootprint);
                    Shuffle(childWallIndices, rng);

                    foreach (int childWallIndex in childWallIndices)
                    {
                        RoomFootprint.Wall childWallDef = childFootprint.Walls[childWallIndex];
                        if (!childWallDef.doorValid) continue;

                        float childOpening = ResolveOpeningWidth(childWallDef, childFootprint.GapWidthM);
                        if (!WallGapController.AreOpeningsPairable(
                                parentWall.gapWidthM, parentWall.lengthM,
                                childOpening,          childWallDef.lengthM))
                        { failed++; continue; }

                        float childOffset = SampleGapOffset(
                            ToPlannedWallLocal(childWallDef, childOpening), rng, gapPolicy);

                        if (!TrySnap(parent, parentWallIndex, parentOffset,
                                     childFootprint, childWallDef.name, childOffset,
                                     out PlannedRoom candidate))
                        { failed++; continue; }

                        if (OverlapsAny(candidate, placed))
                        { failed++; continue; }

                        if (mode == PrefabPickMode.Corridor && wallPrefer == WallPrefer.AlongHeading)
                        {
                            Vector2 delta = candidate.positionXZ - parent.positionXZ;
                            if (delta.sqrMagnitude > 0.01f &&
                                Vector2.Dot(delta.normalized, heading) < -0.1f)
                            { failed++; continue; }
                        }

                        childIndex = placed.Count;
                        placed.Add(candidate);

                        // Mark parent wall connected
                        PlannedRoom uP = placed[parentIndex];
                        PlannedWall pw = uP.walls[parentWallIndex];
                        pw.isConnected = true;
                        uP.walls[parentWallIndex] = pw;
                        uP.connectionCount++;
                        placed[parentIndex] = uP;

                        // Mark child wall connected
                        PlannedRoom uC = placed[childIndex];
                        int cwi = FindWallIndex(uC, childWallDef.name);
                        if (cwi >= 0)
                        {
                            PlannedWall cw = uC.walls[cwi];
                            cw.isConnected = true;
                            uC.walls[cwi] = cw;
                            uC.connectionCount++;
                            placed[childIndex] = uC;
                        }

                        plan.connections.Add(new LayoutPlanConnection
                        {
                            parentIndex      = parentIndex,
                            parentWall       = parentWall.name,
                            childIndex       = childIndex,
                            childWall        = childWallDef.name,
                            parentGapOffsetM = parentOffset,
                            childGapOffsetM  = childOffset
                        });

                        return true;
                    }
                }
            }
            return false;
        }

        // ══════════════════════════════════════════════════════════════════════
        //  WALL ORDERING / SCORING (unchanged)
        // ══════════════════════════════════════════════════════════════════════
        private static void OrderWalls(
            List<int> wallIndices, PlannedRoom parent,
            WallPrefer wallPrefer, Vector2 heading, System.Random rng)
        {
            Shuffle(wallIndices, rng);
            if (wallPrefer == WallPrefer.Any || heading.sqrMagnitude < 0.01f) return;

            Vector2 lateral = new Vector2(-heading.y, heading.x);
            wallIndices.Sort((a, b) =>
            {
                float sA = WallDirectionScore(parent.walls[a].outward, heading, lateral, wallPrefer);
                float sB = WallDirectionScore(parent.walls[b].outward, heading, lateral, wallPrefer);
                return sB.CompareTo(sA);
            });

            if (wallPrefer == WallPrefer.FattenCluster)
            {
                var kept = new List<int>();
                foreach (int i in wallIndices)
                    if (Mathf.Abs(Vector2.Dot(parent.walls[i].outward, heading)) >= DirectionDotMin)
                        kept.Add(i);
                if (kept.Count > 0) { wallIndices.Clear(); wallIndices.AddRange(kept); }
            }
            else if (wallPrefer == WallPrefer.LateralToHeading || wallPrefer == WallPrefer.NotAlongHeading)
            {
                var kept = new List<int>();
                foreach (int i in wallIndices)
                    if (Vector2.Dot(parent.walls[i].outward, heading) < DirectionDotMin)
                        kept.Add(i);
                if (kept.Count > 0) { wallIndices.Clear(); wallIndices.AddRange(kept); }
            }
        }

        private static float WallDirectionScore(
            Vector2 outward, Vector2 heading, Vector2 lateral, WallPrefer prefer)
        {
            float along = Vector2.Dot(outward, heading);
            float side  = Mathf.Abs(Vector2.Dot(outward, lateral));
            return prefer switch
            {
                WallPrefer.AlongHeading     => along,
                WallPrefer.FattenCluster    => Mathf.Abs(along),
                WallPrefer.LateralToHeading => side * 2f - Mathf.Abs(along),
                WallPrefer.NotAlongHeading  => -along,
                _                           => 0f
            };
        }

        // ══════════════════════════════════════════════════════════════════════
        //  PREFAB ORDERING — respects reuse limits
        // ══════════════════════════════════════════════════════════════════════
        private static List<int> BuildPrefabOrder(
            IReadOnlyList<RoomFootprint> library,
            PrefabPickMode               mode,
            System.Random                rng,
            Dictionary<string, int>      usedCounts = null)
        {
            var preferred = new List<int>();
            var overused  = new List<int>(); // at-limit but still usable as fallback
            var fallback  = new List<int>(); // wrong mode

            for (int i = 0; i < library.Count; i++)
            {
                RoomFootprint fp = library[i];
                if (fp == null) continue;
                bool isCorridor = fp.IsCorridorShape;
                int  doors      = CountDoorWalls(fp);

                bool match = mode switch
                {
                    PrefabPickMode.Hub        => !isCorridor && doors >= 4,
                    PrefabPickMode.FatOrMedium => !isCorridor,
                    PrefabPickMode.Corridor    => isCorridor,
                    PrefabPickMode.Stub        => !isCorridor && fp.BoundsAreaM2 <= 80f,
                    _                          => true
                };

                int used     = usedCounts != null ? usedCounts.GetValueOrDefault(fp.PrefabId, 0) : 0;
                int maxReuse = GetMaxReuse(fp);

                if (!match)          fallback.Add(i);
                else if (used >= maxReuse) overused.Add(i);
                else                 preferred.Add(i);
            }

            // Sort preferred by priority
            if (mode == PrefabPickMode.Hub || mode == PrefabPickMode.FatOrMedium)
            {
                preferred.Sort((a, b) => library[b].BoundsAreaM2.CompareTo(library[a].BoundsAreaM2));
                SoftShuffleFront(preferred, rng, Mathf.Min(4, preferred.Count));
            }
            else if (mode == PrefabPickMode.Stub)
            {
                preferred.Sort((a, b) => library[a].BoundsAreaM2.CompareTo(library[b].BoundsAreaM2));
                SoftShuffleFront(preferred, rng, Mathf.Min(4, preferred.Count));
            }
            else // Corridor
            {
                preferred.Sort((a, b) => library[b].PassageScore.CompareTo(library[a].PassageScore));
                SoftShuffleFront(preferred, rng, Mathf.Min(3, preferred.Count));
            }

            // Overused rooms available as last resort before completely wrong-mode
            Shuffle(overused, rng);
            Shuffle(fallback, rng);
            preferred.AddRange(overused);
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

        // ══════════════════════════════════════════════════════════════════════
        //  SEED PICKER (fallback)
        // ══════════════════════════════════════════════════════════════════════
        private static RoomFootprint PickLargestNonCorridor(IReadOnlyList<RoomFootprint> library)
        {
            RoomFootprint best = null; float bestArea = -1f;
            for (int i = 0; i < library.Count; i++)
            {
                var fp = library[i];
                if (fp == null || fp.IsCorridorShape) continue;
                if (fp.BoundsAreaM2 > bestArea) { bestArea = fp.BoundsAreaM2; best = fp; }
            }
            if (best != null) return best;
            for (int i = 0; i < library.Count; i++)
            {
                var fp = library[i];
                if (fp == null) continue;
                if (fp.BoundsAreaM2 > bestArea) { bestArea = fp.BoundsAreaM2; best = fp; }
            }
            return best ?? library[0];
        }

        // ══════════════════════════════════════════════════════════════════════
        //  SNAP (unchanged)
        // ══════════════════════════════════════════════════════════════════════
        private static bool TrySnap(
            PlannedRoom parent, int parentWallIndex, float parentOffset,
            RoomFootprint childFootprint, string childWallName, float childOffset,
            out PlannedRoom child)
        {
            child = default;
            PlannedWall parentWall = parent.walls[parentWallIndex];
            if (!childFootprint.TryGetWall(childWallName, out RoomFootprint.Wall childWallDef))
                return false;

            Vector2 parentSeam    = SeamWithOffset(parentWall, parentOffset);
            Vector2 parentOutward = parentWall.outward;
            Vector2 childSeamLoc  = SeamWithOffsetLocal(childWallDef, childFootprint.GapWidthM, childOffset);
            Vector2 childOutLoc   = childWallDef.outwardLocal.normalized;

            float targetAngle = Mathf.Atan2(-parentOutward.x, -parentOutward.y);
            float localAngle  = Mathf.Atan2(childOutLoc.x,    childOutLoc.y);
            float theta       = QuantizeHalfPi(targetAngle - localAngle);

            Vector2 childOutWorld = Rotate(childOutLoc, theta);
            if (Vector2.Dot(childOutWorld, parentOutward) > -0.5f) return false;

            Vector2 rotated  = Rotate(childSeamLoc, theta);
            Vector2 position = parentSeam - rotated;

            child = CreateRoom(childFootprint, position, theta);
            return true;
        }

        // ══════════════════════════════════════════════════════════════════════
        //  CREATE ROOM (unchanged)
        // ══════════════════════════════════════════════════════════════════════
        private static PlannedRoom CreateRoom(
            RoomFootprint footprint, Vector2 positionXZ, float yawRadians)
        {
            var walls = new List<PlannedWall>(footprint.Walls.Length);
            foreach (RoomFootprint.Wall wall in footprint.Walls)
            {
                walls.Add(new PlannedWall
                {
                    name        = wall.name,
                    seam        = TransformPoint(wall.seamLocal,           positionXZ, yawRadians),
                    axis        = Rotate(wall.axisLocal.normalized,        yawRadians),
                    start       = TransformPoint(wall.startLocal,          positionXZ, yawRadians),
                    end         = TransformPoint(wall.endLocal,             positionXZ, yawRadians),
                    outward     = Rotate(wall.outwardLocal.normalized,     yawRadians),
                    direction   = RotateDirection(wall.direction,           yawRadians),
                    doorValid   = wall.doorValid,
                    lengthM     = wall.lengthM,
                    isConnected = false,
                    gapWidthM   = ResolveOpeningWidth(wall, footprint.GapWidthM),
                    openingMode = wall.openingMode
                });
            }

            Vector2 c0 = TransformPoint(new Vector2(footprint.BoundsMin.x, footprint.BoundsMin.y), positionXZ, yawRadians);
            Vector2 c1 = TransformPoint(new Vector2(footprint.BoundsMin.x, footprint.BoundsMax.y), positionXZ, yawRadians);
            Vector2 c2 = TransformPoint(new Vector2(footprint.BoundsMax.x, footprint.BoundsMin.y), positionXZ, yawRadians);
            Vector2 c3 = TransformPoint(new Vector2(footprint.BoundsMax.x, footprint.BoundsMax.y), positionXZ, yawRadians);
            Vector2 bMin = Vector2.Min(Vector2.Min(c0, c1), Vector2.Min(c2, c3));
            Vector2 bMax = Vector2.Max(Vector2.Max(c0, c1), Vector2.Max(c2, c3));

            return new PlannedRoom
            {
                footprint       = footprint,
                positionXZ      = positionXZ,
                yawRadians      = yawRadians,
                walls           = walls,
                boundsMin       = bMin,
                boundsMax       = bMax,
                connectionCount = 0
            };
        }

        // ══════════════════════════════════════════════════════════════════════
        //  OVERLAP CHECK (unchanged)
        // ══════════════════════════════════════════════════════════════════════
        private static bool OverlapsAny(PlannedRoom candidate, List<PlannedRoom> placed)
        {
            for (int i = 0; i < placed.Count; i++)
                if (AabbOverlap(candidate.boundsMin, candidate.boundsMax,
                                placed[i].boundsMin, placed[i].boundsMax, OverlapInsetM))
                    return true;
            return false;
        }

        private static bool AabbOverlap(
            Vector2 aMin, Vector2 aMax, Vector2 bMin, Vector2 bMax, float inset)
        {
            float ic = Mathf.Max(0f, inset);
            Vector2 aMinI = aMin + Vector2.one * ic, aMaxI = aMax - Vector2.one * ic;
            Vector2 bMinI = bMin + Vector2.one * ic, bMaxI = bMax - Vector2.one * ic;
            if (aMinI.x >= aMaxI.x - OverlapEpsilon || aMinI.y >= aMaxI.y - OverlapEpsilon) return false;
            if (bMinI.x >= bMaxI.x - OverlapEpsilon || bMinI.y >= bMaxI.y - OverlapEpsilon) return false;
            return aMinI.x < bMaxI.x - OverlapEpsilon && aMaxI.x > bMinI.x + OverlapEpsilon &&
                   aMinI.y < bMaxI.y - OverlapEpsilon && aMaxI.y > bMinI.y + OverlapEpsilon;
        }

        // ══════════════════════════════════════════════════════════════════════
        //  OPENING / GAP HELPERS (unchanged)
        // ══════════════════════════════════════════════════════════════════════
        private static float ResolveOpeningWidth(RoomFootprint.Wall wall, float footprintDefault)
        {
            if (wall.openingWidthM > 0.05f) return wall.openingWidthM;
            if (wall.openingMode == WallOpeningMode.FullWall ||
                wall.openingMode == WallOpeningMode.OpenEnd)
                return Mathf.Max(0.05f, wall.lengthM - 0.05f);
            return footprintDefault > 0.05f ? footprintDefault : RoomFootprint.DefaultGapWidthM;
        }

        private static float SampleGapOffset(PlannedWall wall, System.Random rng, GapOffsetPolicy policy)
        {
            bool forceCenter = wall.openingMode == WallOpeningMode.FullWall ||
                               wall.openingMode == WallOpeningMode.OpenEnd  ||
                               wall.gapWidthM   >= wall.lengthM - 0.2f;
            if (!TryGetOffsetRange(wall.lengthM, wall.gapWidthM, policy, out float min, out float max))
                return 0f;
            if (forceCenter || !policy.randomGapOffset || rng == null ||
                Mathf.Abs(max - min) < 0.0001f)
                return (min + max) * 0.5f;
            return min + (float)rng.NextDouble() * (max - min);
        }

        private static bool TryGetOffsetRange(
            float wallLengthM, float gapWidthM, GapOffsetPolicy policy,
            out float minOffset, out float maxOffset)
        {
            minOffset = 0f; maxOffset = 0f;
            float edgeMargin   = Mathf.Max(0f, policy.edgeMarginM);
            float spanFraction = Mathf.Clamp(policy.spanFraction > 0f ? policy.spanFraction : 1f, 0f, 1f);
            const float safetyM = 0.05f;
            float maxGap       = Mathf.Max(0f, wallLengthM - edgeMargin * 2f - safetyM);
            float effectiveGap = Mathf.Min(gapWidthM, maxGap);
            if (effectiveGap < 0.05f) return false;
            float usableSpan  = Mathf.Max(0f, wallLengthM - effectiveGap);
            float clampedSpan = usableSpan * spanFraction;
            minOffset = edgeMargin + (usableSpan - clampedSpan) * 0.5f;
            maxOffset = minOffset + clampedSpan;
            if (maxOffset < minOffset) { float c = usableSpan * 0.5f; minOffset = c; maxOffset = c; }
            return true;
        }

        private static Vector2 SeamWithOffset(PlannedWall wall, float offsetMeters)
        {
            if (!TryGetOffsetRange(wall.lengthM, wall.gapWidthM,
                    GapOffsetPolicy.Default, out float min, out float max))
                return wall.seam;
            float center = (min + max) * 0.5f;
            return wall.seam + wall.axis.normalized * (offsetMeters - center);
        }

        private static Vector2 SeamWithOffsetLocal(
            RoomFootprint.Wall wall, float gapWidthM, float offsetMeters)
            => SeamWithOffset(ToPlannedWallLocal(wall, gapWidthM), offsetMeters);

        private static PlannedWall ToPlannedWallLocal(RoomFootprint.Wall wall, float gapWidthM) =>
            new PlannedWall
            {
                name = wall.name, seam = wall.seamLocal, axis = wall.axisLocal.normalized,
                outward = wall.outwardLocal.normalized, lengthM = wall.lengthM,
                gapWidthM = gapWidthM, doorValid = wall.doorValid,
                direction = wall.direction, openingMode = wall.openingMode
            };

        // ══════════════════════════════════════════════════════════════════════
        //  WALL INDEX HELPERS (unchanged)
        // ══════════════════════════════════════════════════════════════════════
        private static List<int> CollectOpenDoorWallIndices(PlannedRoom room)
        {
            var list = new List<int>();
            for (int i = 0; i < room.walls.Count; i++)
                if (!room.walls[i].isConnected && room.walls[i].doorValid) list.Add(i);
            return list;
        }

        private static List<int> CollectDoorWallIndices(RoomFootprint footprint)
        {
            var list = new List<int>();
            for (int i = 0; i < footprint.Walls.Length; i++)
                if (footprint.Walls[i].doorValid) list.Add(i);
            return list;
        }

        private static int FindWallIndex(PlannedRoom room, string wallName)
        {
            for (int i = 0; i < room.walls.Count; i++)
                if (room.walls[i].name == wallName) return i;
            return -1;
        }

        // ══════════════════════════════════════════════════════════════════════
        //  MATH HELPERS (unchanged)
        // ══════════════════════════════════════════════════════════════════════
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
            float hp = Mathf.PI * 0.5f;
            return Mathf.Round(radians / hp) * hp;
        }

        private static Vector2 Rotate(Vector2 v, float radians)
        {
            float c = Mathf.Cos(radians), s = Mathf.Sin(radians);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        private static Vector2 TransformPoint(Vector2 local, Vector2 pos, float yaw) =>
            pos + Rotate(local, yaw);

        private static SocketDirection RotateDirection(SocketDirection dir, float yaw)
        {
            if (dir == SocketDirection.Unknown) return SocketDirection.Unknown;
            int steps = Mathf.RoundToInt(yaw / (Mathf.PI * 0.5f));
            int idx = dir switch
            {
                SocketDirection.North => 0, SocketDirection.East => 1,
                SocketDirection.South => 2, SocketDirection.West => 3, _ => 0
            };
            int rotated = ((idx + steps) % 4 + 4) % 4;
            return rotated switch
            {
                0 => SocketDirection.North, 1 => SocketDirection.East,
                2 => SocketDirection.South, 3 => SocketDirection.West,
                _ => SocketDirection.Unknown
            };
        }
    }
}
