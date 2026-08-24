using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AfterAll.Player;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Serialization;

namespace AfterAll.Environment
{
    /// <summary>
    /// Paint-growth layout: plan in Layout Top View (or on Play), then apply sockets in-scene.
    /// Generation policy lives in PaintGrowthPlanner + Top View.
    /// </summary>
    public class RoomPoolSpawner : MonoBehaviour
    {
        private enum UnreachableCause
        {
            ZeroConnections,
            IsolatedSubGraph
        }

        private struct ReachabilityAuditResult
        {
            public int totalPlaced;
            public int reachableCount;
            public int unreachableCount;
            public int zeroConnectionCount;
            public int isolatedSubGraphCount;
            public int retriedCount;
            public int salvagedCount;
            public int destroyedCount;
            public List<string> actionLines;
        }

        private struct UnreachableComponent
        {
            public List<RoomInstance> rooms;
            public UnreachableCause cause;
        }

        [SerializeField] private RoomConnector _connector;
        [SerializeField] private RoomPrefabEntry[] _roomPrefabEntries = Array.Empty<RoomPrefabEntry>();
        [SerializeField, HideInInspector] private GameObject[] _roomPrefabs = Array.Empty<GameObject>();
        [SerializeField, Min(1)] private int _roomCount = 20;
        [Tooltip("Off when an external driver (e.g. RunDirector) owns Build() calls.")]
        [SerializeField] private bool _autoBuildOnStart = true;

        [Header("Paint Growth")]
        [FormerlySerializedAs("_pathNetworkFootprints")]
        [SerializeField] private RoomFootprint[] _settlementFootprints = Array.Empty<RoomFootprint>();
        [Tooltip("Attached last to one reserved door on the auto-picked hub (room 0); player spawns here instead of the hub.")]
        [SerializeField] private RoomFootprint _elevatorFootprint;
        [FormerlySerializedAs("_pathNetworkRandomGapOffset")]
        [SerializeField] private bool _randomGapOffset = true;
        [SerializeField, Min(0f)] private float _gapEdgeMarginM = 0.15f;
        [SerializeField, Range(0f, 1f)] private float _gapOffsetSpanFraction = 1f;

        [Header("Seed")]
        [SerializeField] private bool _useFixedSeed;
        [SerializeField] private int _fixedSeed = 12345;
        [SerializeField] private bool _randomizeSeedOnPlay = true;
        [SerializeField] private int _lastUsedSeed;

        [Header("Build Pace")]
        [SerializeField, Min(0f)] private float _spawnDelaySeconds = 0.05f;

        [Header("Player Spawn")]
        [SerializeField] private Transform _player;
        [SerializeField] private float _playerSpawnHeight = 1.0f;
        [SerializeField] private bool _repositionPlayerAfterBuild = true;

        [Header("Reachability")]
        [SerializeField] private UnreachableRoomPolicy _unreachableRoomPolicy = UnreachableRoomPolicy.RetryThenDestroy;
        [SerializeField, Min(1)] private int _unreachableRetryAttempts = 1;

        [SerializeField] private RoomContentManager _contentManager;

        private NavMeshSurface _navMeshSurface;
        private int _placedRoomCount;
        private Coroutine _buildRoutine;
        private System.Random _rng;
        private RoomPrefabEntry[] _activePrefabEntries = Array.Empty<RoomPrefabEntry>();

        public int LastUsedSeed => _lastUsedSeed;
        public int RoomCount => _roomCount;

        /// <summary>The elevator room of the most recently completed build (null between ClearLevelRoot and rebuild).</summary>
        public RoomInstance CurrentElevatorRoom { get; private set; }

        /// <summary>The elevator cabin kept alive across floor rebuilds. Adopted from the first
        /// build and reparented out of LevelRoot so ClearLevelRoot never destroys it — the player
        /// rides it while floors are torn down and rebuilt around it.</summary>
        private RoomInstance _persistentElevator;

        /// <summary>Fired once a floor finishes building, with the new elevator room (may be null if the plan has none).</summary>
        public event Action<RoomInstance> FloorReady;

        /// <summary>Runtime entry point for the run loop: generate a new floor with a fresh seed/budget; player spawns in the attached elevator room.</summary>
        public void BeginNewFloor(int seed, int roomCount)
        {
            _useFixedSeed = true;
            _fixedSeed = seed;
            _randomizeSeedOnPlay = false;
            _roomCount = Mathf.Max(8, roomCount);
            Build();
        }

#if UNITY_EDITOR
        /// <summary>
        /// Where Layout Top View parks the seed it wants Play to reproduce, for
        /// <see cref="ConsumeSeedOverride"/> to pick up one time.
        ///
        /// SessionState rather than a serialized field on purpose. The seed has to survive exactly one
        /// domain reload — the one entering Play — and nothing beyond it, so writing it into the scene
        /// was wrong twice over: it left Level0 dirty after every push, and it left the pinned seed
        /// sitting in the Inspector afterwards looking like a deliberate setting.
        /// </summary>
        public const string PendingLayoutSeedKey = "AfterAll.PendingLayoutSeed";
#endif

        /// <summary>
        /// The seed the next floor should use: whatever Layout Top View pushed if a push is pending,
        /// otherwise the caller's own.
        ///
        /// A push is consumed once and then gone, so Push → Play shows you the layout you previewed and
        /// every floor after it — and every later Play — is random again.
        ///
        /// This exists because pinning the spawner's own seed fields no longer reaches the run loop at
        /// all: RunDirector.BeginRun drives the first floor through BeginNewFloor, which overwrites
        /// them, so a pushed seed was being discarded a frame after it was set.
        /// </summary>
        public int ConsumeSeedOverride(int fallbackSeed)
        {
#if UNITY_EDITOR
            const int none = int.MinValue;
            int pending = UnityEditor.SessionState.GetInt(PendingLayoutSeedKey, none);
            if (pending != none)
            {
                UnityEditor.SessionState.EraseInt(PendingLayoutSeedKey);
                Debug.Log(
                    $"[RoomPoolSpawner] Using seed {pending} pushed from Layout Top View for this " +
                    "floor. Later floors are random again.");
                return pending;
            }
#endif
            return fallbackSeed;
        }

        public void ConfigurePaintGrowthFromEditor(
            PaintGrowthConfig config,
            RoomFootprint[] footprints,
            RoomFootprint elevatorFootprint = null)
        {
            config.Clamp();
            _randomGapOffset = config.randomGapOffset;
            _roomCount = Mathf.Max(8, config.targetRoomCount);
            if (config.gapPolicy.edgeMarginM > 0f)
                _gapEdgeMarginM = config.gapPolicy.edgeMarginM;
            if (footprints != null && footprints.Length > 0)
            {
                _settlementFootprints = footprints;
                EnsurePrefabEntriesFromFootprints(footprints);
            }
            if (elevatorFootprint != null)
                _elevatorFootprint = elevatorFootprint;
        }

        public void SetSettlementFootprints(RoomFootprint[] footprints)
        {
            if (footprints != null)
            {
                _settlementFootprints = footprints;
                EnsurePrefabEntriesFromFootprints(footprints);
            }
        }

        public void SetElevatorFootprint(RoomFootprint footprint) => _elevatorFootprint = footprint;

        /// <summary>
        /// Keeps the Play prefab pool aligned with footprint prefabs so Push → Play works.
        /// </summary>
        public void EnsurePrefabEntriesFromFootprints(IReadOnlyList<RoomFootprint> footprints)
        {
            if (footprints == null || footprints.Count == 0)
                return;

            var byName = new Dictionary<string, RoomPrefabEntry>();
            if (_roomPrefabEntries != null)
            {
                foreach (RoomPrefabEntry entry in _roomPrefabEntries)
                {
                    if (entry?.Prefab == null)
                        continue;
                    byName[entry.Prefab.name] = entry;
                }
            }

            bool changed = false;
            foreach (RoomFootprint footprint in footprints)
            {
                if (footprint?.Prefab == null)
                    continue;
                if (byName.ContainsKey(footprint.Prefab.name))
                    continue;
                byName[footprint.Prefab.name] = new RoomPrefabEntry(footprint.Prefab);
                changed = true;
            }

            if (!changed && _roomPrefabEntries != null && _roomPrefabEntries.Length == byName.Count)
                return;

            var list = new List<RoomPrefabEntry>(byName.Values);
            _roomPrefabEntries = list.ToArray();
        }

        private PaintGrowthConfig BuildConfig()
        {
            var policy = new GapOffsetPolicy
            {
                randomGapOffset = _randomGapOffset,
                edgeMarginM = _gapEdgeMarginM,
                spanFraction = _gapOffsetSpanFraction
            };
            return PaintGrowthConfig.FromTargetRoomCount(_roomCount, _randomGapOffset, policy);
        }

        private void Awake()
        {
            if (_contentManager == null)
                _contentManager = GetComponent<RoomContentManager>();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            MigrateLegacyPrefabPool();
        }
#endif

        private void Start()
        {
            HideHandPlacedRooms();
            if (_autoBuildOnStart)
                Build();
        }

        public void Build()
        {
            if (_buildRoutine != null)
                StopCoroutine(_buildRoutine);

            _buildRoutine = StartCoroutine(BuildPaintGrowthRoutine());
        }

        private IEnumerator BuildPaintGrowthRoutine()
        {
            _placedRoomCount = 0;

            if (_connector == null)
                _connector = GetComponent<RoomConnector>();

            if (_connector == null)
            {
                Debug.LogError("[RoomPoolSpawner] Need RoomConnector.");
                _buildRoutine = null;
                yield break;
            }

            List<RoomFootprint> library = ResolveSettlementLibrary();
#if UNITY_EDITOR
            if (library.Count == 0)
                library = EditorLoadAllFootprints();
#endif
            if (library.Count == 0)
            {
                Debug.LogError(
                    "[RoomPoolSpawner] Settlement Spine needs RoomFootprint assets. " +
                    "Run AfterAll → Generation → Bake Room Footprints, then assign them or use Layout Top View → Push → Play.");
                _buildRoutine = null;
                yield break;
            }

            // Elevator footprints never join the general pool — pull any stray ones out
            // (e.g. from "Assign Footprints" bulk-assigning the whole baked folder) and
            // auto-adopt one as _elevatorFootprint if none is wired yet.
            for (int i = library.Count - 1; i >= 0; i--)
            {
                if (library[i] == null || !library[i].IsElevator)
                    continue;
                if (_elevatorFootprint == null)
                    _elevatorFootprint = library[i];
                library.RemoveAt(i);
            }

            if (_settlementFootprints == null || _settlementFootprints.Length == 0)
                _settlementFootprints = library.ToArray();
            EnsurePrefabEntriesFromFootprints(library);

            if (!TryPreparePrefabPool())
            {
                Debug.LogError("[RoomPoolSpawner] Need at least one valid room prefab entry (sync from footprints failed).");
                _buildRoutine = null;
                yield break;
            }

            ClearLevelRoot();
            _connector.ResetStats();
            InitializeRng();
            _connector.ConfigureOffsetSearch(false, 1, _lastUsedSeed);
            _connector.ConfigureGapOffset(_randomGapOffset, _gapEdgeMarginM, _gapOffsetSpanFraction);

            PaintGrowthConfig config = BuildConfig();
            LayoutPlan plan = PaintGrowthPlanner.Generate(library, _lastUsedSeed, config, _elevatorFootprint);
            _lastUsedSeed = plan.seed; // reflect the seed that actually produced this plan (may have been reseeded)

            Debug.Log(
                $"[RoomPoolSpawner] PaintGrowth Seed={_lastUsedSeed}, TargetRooms={config.targetRoomCount}, " +
                $"Placed={plan.PlacedCount}. {plan.notes}");

            if (_elevatorFootprint != null && plan.elevatorIndex < 0)
            {
                Debug.LogError(
                    "[RoomPoolSpawner] Elevator attach failed even after the planner's internal " +
                    "reseed attempts — refusing to build this floor (the persistent cabin can't be " +
                    $"correlated with the generated rooms). {plan.notes}");
                _buildRoutine = null;
                yield break;
            }

            yield return null;

            Dictionary<string, GameObject> prefabById = BuildPrefabLookup();
            Dictionary<string, RoomFootprint> footprintById = BuildFootprintLookup();
            var roomsByIndex = new Dictionary<int, RoomInstance>(plan.PlacedCount);
            RoomInstance.SocketValidationReport validationTotals = default;
            int connectionsApplied = 0;

            if (plan.placements.Count == 0)
            {
                Debug.LogError("[RoomPoolSpawner] Settlement Spine plan has no placements.");
                _buildRoutine = null;
                yield break;
            }

            // Rebuilds keep the previous floor's cabin alive (it lives outside LevelRoot), so the
            // whole new layout must be rigidly pre-aligned to land its elevator placement exactly
            // on the cabin BEFORE anything spawns — the player standing inside never sees the
            // floor build displaced and then snap into place.
            // elevatorIndex < 0 already aborted the build above, so whenever a persistent
            // cabin exists the plan is guaranteed to have a valid elevator placement here.
            bool reuseElevatorCabin = _persistentElevator != null
                && plan.elevatorIndex > 0
                && plan.elevatorIndex < plan.placements.Count;
            if (reuseElevatorCabin)
                AlignLevelRootToPersistentElevator(plan);

            LayoutPlanPlacement hubPlacement = plan.placements[0];
            if (!prefabById.TryGetValue(hubPlacement.prefabId, out GameObject hubPrefab) || hubPrefab == null)
            {
                Debug.LogError($"[RoomPoolSpawner] Missing hub prefab '{hubPlacement.prefabId}'.");
                _buildRoutine = null;
                yield break;
            }

            GameObject hubGo = Instantiate(hubPrefab, _connector.LevelRoot);
            hubGo.transform.SetLocalPositionAndRotation(
                Vector3.zero,
                Quaternion.Euler(0f, hubPlacement.yawDegrees, 0f));
            RoomInstance hub = GetRoom(hubGo);
            hub.PrefabId = hubPlacement.prefabId;
            if (footprintById.TryGetValue(hub.PrefabId, out RoomFootprint hubFootprint))
                hub.SetFootprint(hubFootprint);
            hub.SealAllWalls();
            AlignRoomWalkableFloorToWorldY(hub, _connector.LevelRoot.position.y);
            float hubWalkableY = hub.GetWalkableFloorY();
            hub.MarkAsHub();
            RoomInstance.SocketValidationReport hubValidation = hub.ValidateSocketContracts(logWarnings: true);
            validationTotals.missingContractCount += hubValidation.missingContractCount;
            validationTotals.duplicateDirectionCount += hubValidation.duplicateDirectionCount;
            roomsByIndex[0] = hub;
            _placedRoomCount = 1;

            if (reuseElevatorCabin)
            {
                // Pre-seed the cabin as an already-placed room: the connection loop's
                // childAlreadyPlaced path then links it to the new hub without snapping or
                // re-instantiating it. Its doorway is deterministic (single doorValid wall,
                // non-random gap offsets), so the existing opening already matches the plan —
                // only the connection bookkeeping from the destroyed floor must be cleared.
                _persistentElevator.ResetConnections();
                roomsByIndex[plan.elevatorIndex] = _persistentElevator;
                _placedRoomCount = roomsByIndex.Count;
            }

            if (_spawnDelaySeconds > 0f)
                yield return new WaitForSeconds(_spawnDelaySeconds);
            else
                yield return null;

            foreach (LayoutPlanConnection connection in plan.connections)
            {
                if (connection.parentIndex < 0 || connection.childIndex < 0)
                    continue;
                if (connection.childIndex >= plan.placements.Count || connection.parentIndex >= plan.placements.Count)
                    continue;
                if (!roomsByIndex.TryGetValue(connection.parentIndex, out RoomInstance parent) || parent == null)
                    continue;

                bool childAlreadyPlaced = roomsByIndex.ContainsKey(connection.childIndex);
                RoomInstance child;
                if (!childAlreadyPlaced)
                {
                    LayoutPlanPlacement childPlacement = plan.placements[connection.childIndex];
                    if (!prefabById.TryGetValue(childPlacement.prefabId, out GameObject childPrefab) || childPrefab == null)
                    {
                        Debug.LogError($"[RoomPoolSpawner] Missing prefab '{childPlacement.prefabId}'.");
                        continue;
                    }

                    GameObject childGo = Instantiate(childPrefab, _connector.LevelRoot);
                    childGo.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                    child = GetRoom(childGo);
                    child.PrefabId = childPlacement.prefabId;
                    if (footprintById.TryGetValue(child.PrefabId, out RoomFootprint childFootprint))
                        child.SetFootprint(childFootprint);
                    child.SealAllWalls();
                    RoomInstance.SocketValidationReport childValidation = child.ValidateSocketContracts(logWarnings: true);
                    validationTotals.missingContractCount += childValidation.missingContractCount;
                    validationTotals.duplicateDirectionCount += childValidation.duplicateDirectionCount;
                    roomsByIndex[connection.childIndex] = child;
                    _placedRoomCount = roomsByIndex.Count;
                }
                else
                {
                    child = roomsByIndex[connection.childIndex];
                }

                WallGapController parentWall = parent.GetWall(connection.parentWall);
                WallGapController childWall = child.GetWall(connection.childWall);
                if (parentWall == null || childWall == null)
                {
                    Debug.LogWarning(
                        $"[RoomPoolSpawner] Planned wall missing: {connection.parentWall} / {connection.childWall}");
                    continue;
                }

                bool snap = !childAlreadyPlaced;
                if (_connector.ApplyPlannedConnection(
                        parent,
                        parentWall,
                        child,
                        childWall,
                        connection.parentGapOffsetM,
                        connection.childGapOffsetM,
                        snap))
                {
                    connectionsApplied++;
                    if (snap)
                        child.SetGraphDepth(parent.GraphDepth + 1);
                }

                if (snap)
                {
                    if (_spawnDelaySeconds > 0f)
                        yield return new WaitForSeconds(_spawnDelaySeconds);
                    else
                        yield return null;
                }
            }

            foreach (KeyValuePair<int, RoomInstance> pair in roomsByIndex)
            {
                if (pair.Value != null)
                    AlignRoomWalkableFloorToWorldY(pair.Value, hubWalkableY);
            }

            foreach (KeyValuePair<int, RoomInstance> pair in roomsByIndex)
            {
                RoomInstance room = pair.Value;
                if (room == null)
                    continue;

                foreach (WallGapController wall in room.Walls)
                {
                    if (wall == null || !wall.hasOpening)
                        continue;

                    wall.ConfigureOpening(true, wall.gapOffset);
                }
            }

            var placedRooms = new List<RoomInstance>(roomsByIndex.Count);
            for (int i = 0; i < plan.placements.Count; i++)
            {
                if (roomsByIndex.TryGetValue(i, out RoomInstance room) && room != null)
                    placedRooms.Add(room);
            }

            RoomInstance startRoom = hub;
            roomsByIndex.TryGetValue(plan.elevatorIndex, out RoomInstance elevatorRoom);

            if (reuseElevatorCabin && elevatorRoom != null && elevatorRoom.ConnectedRooms.Count == 0)
                Debug.LogError(
                    "[RoomPoolSpawner] Persistent cabin failed to re-connect to the new hub — its " +
                    "doorway leads nowhere. Wall pairing or gap-offset determinism broke.");

            CurrentElevatorRoom = elevatorRoom;
            RoomConnector.ConnectionStats stats = _connector.GetStats();
            // Resolve overlaps BEFORE the reachability audit: destroying the losing room of an
            // overlapping pair can orphan whatever hung off it, and the audit below (with its
            // existing retry/salvage/destroy policy) is what's supposed to catch that — not a
            // second, separate cleanup pass.
            // Every phase below is synchronous and scales with room count / total geometry, so any one
            // of them can stall the editor hard enough to look like a crash ("Unity does nothing, have
            // to End Task"). Time them all and print the breakdown with the summary, so the next stall
            // names its own culprit instead of needing a guess. The yields between phases also give
            // the editor a frame to repaint, which is what separates "slow" from "hung" on screen.
            var phase = new System.Diagnostics.Stopwatch();
            var timings = new StringBuilder();

            phase.Restart();
            int postBuildOverlaps = ResolvePlacedRoomOverlaps();
            timings.Append($" overlaps={phase.ElapsedMilliseconds}ms");
            yield return null;

            phase.Restart();
            (ReachabilityAuditResult reachability, int finalPlacedCount) = RunReachabilityAudit(startRoom);
            timings.Append($" reachability={phase.ElapsedMilliseconds}ms");
            yield return null;

            phase.Restart();
            int resealedOrphanWalls = ResealOrphanOpenWalls();
            timings.Append($" reseal={phase.ElapsedMilliseconds}ms");
            // On rebuilds the layout was pre-aligned onto the cabin the player is standing in —
            // teleporting the player here would snap them away from it.
            if (_repositionPlayerAfterBuild && !reuseElevatorCabin)
                PlacePlayerAfterBuild(startRoom, elevatorRoom);

            yield return null;

            phase.Restart();
            _contentManager?.ActivateAll(_lastUsedSeed);
            timings.Append($" content+lightmaps={phase.ElapsedMilliseconds}ms");
            yield return null;

            // Prime suspect for the "editor does nothing" stall: this walks every static MeshFilter
            // under LevelRoot (room10 alone carries 626 renderers, room7 337) and merges them, so its
            // cost scales with total floor geometry rather than room count.
            phase.Restart();
            int combinedStaticObjects = CombineStaticRoomGeometry();
            timings.Append($" staticBatch={phase.ElapsedMilliseconds}ms");
            yield return null;

            // Rooms are placed at runtime, so NavMesh can't be pre-baked — bake it fresh over just
            // this floor's geometry (LevelRoot only: player/loot/hunter live outside it, so they're
            // excluded automatically) after content spawn, so pillars/props count as obstacles too.
            // This runs while the elevator door is still closed (RunDirector.TransitionRoutine), so
            // the (synchronous — this package version has no async surface bake) hitch is never seen
            // by the player. NOTE: measured ~7.8s on a 16-room floor (2026-07-22) — the FIRST floor
            // build has no door to hide behind, so this hitch is fully exposed on every Play start.
            // Worth a dedicated pass (NavMeshSurface voxel size/quality, or async bake) later.
            phase.Restart();
            EnsureNavMeshSurface().BuildNavMesh();
            timings.Append($" navmesh={phase.ElapsedMilliseconds}ms");

            float minFloorY = float.PositiveInfinity;
            float maxFloorY = float.NegativeInfinity;
            foreach (RoomInstance room in placedRooms)
            {
                if (room == null)
                    continue;
                float floorY = room.GetWalkableFloorY();
                minFloorY = Mathf.Min(minFloorY, floorY);
                maxFloorY = Mathf.Max(maxFloorY, floorY);
            }

            var summary = new StringBuilder();
            summary.AppendLine(
                $"[RoomPoolSpawner] PaintGrowth done. Placed={finalPlacedCount}, " +
                $"Connections={connectionsApplied}/{plan.connections.Count}, Seed={_lastUsedSeed}, " +
                $"ExitIndex={plan.exitIndex}.");
            summary.AppendLine(plan.notes);
            summary.AppendLine(
                $"FloorY range=[{minFloorY:F3}, {maxFloorY:F3}] delta={maxFloorY - minFloorY:F3}m (want ~0).");
            summary.AppendLine(
                $"Reachability: reachable={reachability.reachableCount}, unreachable={reachability.unreachableCount}, " +
                $"salvaged={reachability.salvagedCount}, destroyed={reachability.destroyedCount}.");
            summary.AppendLine(
                $"Validation missingContracts={validationTotals.missingContractCount}, " +
                $"duplicateDirs={validationTotals.duplicateDirectionCount}, postBuildOverlaps={postBuildOverlaps}, " +
                $"resealedOrphanWalls={resealedOrphanWalls}.");
            summary.AppendLine($"Phase times:{timings}");
            summary.AppendLine(
                $"Connector stats: NoCompatible={stats.noCompatibleSocket}, Gap={stats.gapMismatch}, " +
                $"Overlap={stats.overlapRejected}.");
            summary.AppendLine($"Static batching: combined {combinedStaticObjects} renderers.");
            Debug.Log(summary.ToString());

            // First build with an elevator: adopt the cabin as persistent. It leaves LevelRoot so
            // ClearLevelRoot never destroys it; every later floor is pre-aligned onto it instead.
            if (elevatorRoom != null && _persistentElevator == null)
            {
                _persistentElevator = elevatorRoom;
                elevatorRoom.transform.SetParent(null, worldPositionStays: true);
            }

            _buildRoutine = null;
            FloorReady?.Invoke(elevatorRoom);
        }

        /// <summary>
        /// Rigidly poses LevelRoot BEFORE any room spawns so the plan's elevator placement lands
        /// exactly on the persistent cabin's current world pose. Plan space == LevelRoot local
        /// space (the hub spawns at local zero with its plan yaw), so the root transform that maps
        /// the planned elevator pose onto the cabin is computable straight from plan data.
        /// </summary>
        private void AlignLevelRootToPersistentElevator(LayoutPlan plan)
        {
            Transform root = _connector.LevelRoot;
            LayoutPlanPlacement elevator = plan.placements[plan.elevatorIndex];
            Transform cabin = _persistentElevator.transform;

            float deltaYaw = Mathf.DeltaAngle(elevator.yawDegrees, cabin.eulerAngles.y);
            Quaternion rootRotation = Quaternion.Euler(0f, deltaYaw, 0f);
            Vector3 planLocal = new Vector3(elevator.positionXZ.x, 0f, elevator.positionXZ.y);
            Vector3 rotatedOffset = rootRotation * planLocal;
            root.SetPositionAndRotation(
                new Vector3(cabin.position.x - rotatedOffset.x, root.position.y, cabin.position.z - rotatedOffset.z),
                rootRotation);
        }

        private static void AlignRoomWalkableFloorToWorldY(RoomInstance room, float targetWalkableY)
        {
            if (room == null)
                return;

            float current = room.GetWalkableFloorY();
            float delta = targetWalkableY - current;
            if (Mathf.Abs(delta) > 0.0001f)
                room.transform.position += new Vector3(0f, delta, 0f);
        }

        private List<RoomFootprint> ResolveSettlementLibrary()
        {
            var result = new List<RoomFootprint>();
            if (_settlementFootprints == null || _settlementFootprints.Length == 0)
                return result;

            var poolNames = new HashSet<string>();
            foreach (RoomPrefabEntry entry in _activePrefabEntries)
            {
                if (entry?.Prefab != null)
                    poolNames.Add(entry.Prefab.name);
            }

            foreach (RoomFootprint footprint in _settlementFootprints)
            {
                if (footprint == null || footprint.Prefab == null)
                    continue;

                if (poolNames.Count > 0 && !poolNames.Contains(footprint.Prefab.name))
                    continue;

                result.Add(footprint);
            }

            if (result.Count == 0)
            {
                foreach (RoomFootprint footprint in _settlementFootprints)
                {
                    if (footprint != null && footprint.Prefab != null)
                        result.Add(footprint);
                }
            }

            return result;
        }

#if UNITY_EDITOR
        private static List<RoomFootprint> EditorLoadAllFootprints()
        {
            const string folder = "Assets/_AfterAll/Data/RoomFootprints";
            var result = new List<RoomFootprint>();
            if (!UnityEditor.AssetDatabase.IsValidFolder(folder))
                return result;

            string[] guids = UnityEditor.AssetDatabase.FindAssets("t:RoomFootprint", new[] { folder });
            foreach (string guid in guids)
            {
                RoomFootprint footprint = UnityEditor.AssetDatabase.LoadAssetAtPath<RoomFootprint>(
                    UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
                if (footprint != null && footprint.Prefab != null)
                    result.Add(footprint);
            }

            return result;
        }
#endif

        private Dictionary<string, GameObject> BuildPrefabLookup()
        {
            var map = new Dictionary<string, GameObject>();
            foreach (RoomPrefabEntry entry in _activePrefabEntries)
            {
                if (entry?.Prefab != null && !map.ContainsKey(entry.Prefab.name))
                    map[entry.Prefab.name] = entry.Prefab;
            }

            if (_settlementFootprints != null)
            {
                foreach (RoomFootprint footprint in _settlementFootprints)
                {
                    if (footprint?.Prefab != null && !map.ContainsKey(footprint.Prefab.name))
                        map[footprint.Prefab.name] = footprint.Prefab;
                }
            }

            if (_elevatorFootprint?.Prefab != null && !map.ContainsKey(_elevatorFootprint.Prefab.name))
                map[_elevatorFootprint.Prefab.name] = _elevatorFootprint.Prefab;

            return map;
        }

        /// <summary>
        /// Combines every fixed-geometry renderer under LevelRoot into fewer draw calls via
        /// StaticBatchingUtility (runtime-callable, unlike lightmap baking). Only GameObjects
        /// still flagged isStatic on their source prefab are eligible — WallGapController's
        /// WallLeft/WallRight pieces and RoomSocket markers must stay non-static since they get
        /// repositioned/toggled at runtime to open doorway gaps, and the persistent elevator
        /// cabin's prefab is entirely non-static since it's reused and re-linked every floor.
        /// Runs once per floor build, after openings are finalized and unreachable rooms are
        /// destroyed, so nothing combined here moves again this floor.
        /// </summary>
        private int CombineStaticRoomGeometry()
        {
            var staticObjects = new List<GameObject>();
            foreach (MeshFilter filter in _connector.LevelRoot.GetComponentsInChildren<MeshFilter>(false))
            {
                if (filter.gameObject.isStatic)
                    staticObjects.Add(filter.gameObject);
            }

            if (staticObjects.Count == 0)
                return 0;

            StaticBatchingUtility.Combine(staticObjects.ToArray(), _connector.LevelRoot.gameObject);
            return staticObjects.Count;
        }

        private NavMeshSurface EnsureNavMeshSurface()
        {
            if (_navMeshSurface != null)
                return _navMeshSurface;

            GameObject root = _connector.LevelRoot.gameObject;
            _navMeshSurface = root.GetComponent<NavMeshSurface>() ?? root.AddComponent<NavMeshSurface>();
            _navMeshSurface.collectObjects = CollectObjects.Children;
            // RenderMeshes measured ~8-23s to bake (scales with room count — 16.4M source
            // triangles from decorative room geometry on a 20-room floor). PhysicsColliders reads
            // the room kit's existing colliders instead (~1k tris, mostly BoxColliders) — 53ms.
            // Anything decorative that stands inside a doorway must therefore be a trigger collider
            // or it physically seals the connection for pathing (this cost ~30% of doorways back
            // when the door-frame system still spawned solid-collider frames into open gaps).
            _navMeshSurface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            return _navMeshSurface;
        }

        private Dictionary<string, RoomFootprint> BuildFootprintLookup()
        {
            var map = new Dictionary<string, RoomFootprint>();

            if (_settlementFootprints != null)
            {
                foreach (RoomFootprint footprint in _settlementFootprints)
                {
                    if (footprint != null && !map.ContainsKey(footprint.PrefabId))
                        map[footprint.PrefabId] = footprint;
                }
            }

            if (_elevatorFootprint != null && !map.ContainsKey(_elevatorFootprint.PrefabId))
                map[_elevatorFootprint.PrefabId] = _elevatorFootprint;

            return map;
        }

        private RoomInstance FindHubRoom()
        {
            foreach (RoomInstance room in _connector.LevelRoot.GetComponentsInChildren<RoomInstance>())
            {
                if (room != null && room.IsHub)
                    return room;
            }

            return null;
        }

        /// <summary>
        /// PaintGrowthPlanner's own overlap avoidance is plan-space only (do NOT rewrite it —
        /// CLAUDE.md proc-gen contract) and RoomConnector.ApplyPlannedConnection (the main
        /// build-loop path, unlike the salvage/retry path) never re-validates against already
        /// -placed rooms — so a planner edge case can still land two non-adjacent branches on top
        /// of each other. This is the runtime safety net: destroy the less-central room of each
        /// overlapping pair (reusing the same DestroyUnreachableRoom the reachability audit uses),
        /// then let that audit's existing retry/salvage/destroy policy handle anything its removal
        /// orphans. Never touches the hub or the persistent elevator cabin.
        /// </summary>
        private int ResolvePlacedRoomOverlaps()
        {
            RoomInstance[] levelRootRooms = _connector.LevelRoot.GetComponentsInChildren<RoomInstance>();
            // The persistent cabin lives outside LevelRoot from the second floor onward
            // (RoomPoolSpawner.Build reparents it out so ClearLevelRoot can't destroy it) — without
            // adding it back in here, every rebuild after the first silently stops checking new
            // rooms against the elevator at all.
            RoomInstance[] rooms = _persistentElevator != null && !levelRootRooms.Contains(_persistentElevator)
                ? levelRootRooms.Append(_persistentElevator).ToArray()
                : levelRootRooms;

            var toDestroy = new HashSet<RoomInstance>();

            for (int i = 0; i < rooms.Length; i++)
            {
                for (int j = i + 1; j < rooms.Length; j++)
                {
                    RoomInstance a = rooms[i];
                    RoomInstance b = rooms[j];
                    if (a == null || b == null || toDestroy.Contains(a) || toDestroy.Contains(b))
                        continue;

                    if (AreDirectlyConnected(a, b))
                        continue;

                    if (!RoomConnector.RoomsOverlapForPlacement(a, b, false, null, null))
                        continue;

                    float area = RoomConnector.ComputeXZOverlapArea(
                        a.GetFloorFootprintBounds(),
                        b.GetFloorFootprintBounds());

                    // The elevator-attach guard in BuildPaintGrowthRoutine makes this pairing
                    // structurally impossible now — an occurrence here means that guard regressed.
                    // Log loudly and leave the cabin alone rather than destroy it.
                    if (a == _persistentElevator || b == _persistentElevator)
                    {
                        Debug.LogError(
                            $"[RoomPoolSpawner] Post-build floor overlap involving the elevator: " +
                            $"{a.name} <-> {b.name} (area={area:F2}m2). Elevator-attach guard regressed.");
                        continue;
                    }

                    RoomInstance loser = PickOverlapLoser(a, b);
                    Debug.LogWarning(
                        $"[RoomPoolSpawner] Post-build floor overlap: {a.name} <-> {b.name} " +
                        $"(area={area:F2}m2) -> destroying {loser.name} (less central of the two).");
                    toDestroy.Add(loser);
                }
            }

            int removed = 0;
            foreach (RoomInstance room in toDestroy)
            {
                if (DestroyUnreachableRoom(room))
                    removed++;
            }

            return removed;
        }

        /// <summary>Picks which side of an overlapping pair to remove: never the hub, otherwise
        /// prefer the room farther from the hub (higher GraphDepth), then the one with fewer
        /// connections (more likely a leaf, so removing it orphans less of the layout).</summary>
        private static RoomInstance PickOverlapLoser(RoomInstance a, RoomInstance b)
        {
            if (a.IsHub)
                return b;
            if (b.IsHub)
                return a;
            if (a.GraphDepth != b.GraphDepth)
                return a.GraphDepth > b.GraphDepth ? a : b;
            return a.ConnectedRooms.Count <= b.ConnectedRooms.Count ? a : b;
        }

        /// <summary>
        /// Post-build safety net: a wall can end up with hasOpening=true but no registered neighbor
        /// (e.g. a connection attempt opened both sides then failed a later step, or a neighbor was
        /// destroyed through a path that didn't route through DestroyUnreachableRoom/UnlinkNeighbor).
        /// Every normal connect/destroy path already reseals both sides symmetrically, so this should
        /// rarely fire — but when it does, an open gap with nothing behind it is worse than a closed
        /// wall, so reseal it rather than leave a hole into nothing.
        /// </summary>
        private int ResealOrphanOpenWalls()
        {
            int resealed = 0;
            foreach (RoomInstance room in _connector.LevelRoot.GetComponentsInChildren<RoomInstance>())
            {
                foreach (WallGapController wall in room.GetOpenUnconnectedWalls().ToList())
                {
                    Debug.LogWarning(
                        $"[RoomPoolSpawner] Orphan open wall: {room.name}/{wall.name} has no connected " +
                        "neighbor — resealing.");
                    wall.ConfigureOpening(false);
                    resealed++;
                }
            }

            return resealed;
        }

        private (ReachabilityAuditResult result, int finalPlacedCount) RunReachabilityAudit(RoomInstance startRoom)
        {
            RoomInstance spawnRoot = PickSpawnRoom(startRoom);
            if (spawnRoot == null)
            {
                Debug.LogError("[RoomPoolSpawner] Reachability hard failure: spawn root is null.");
                int placed = CountPlacedRooms();
                return (CreateEmptyReachabilityResult(placed), placed);
            }

            ReachabilityAuditResult initialAudit = AuditReachability(spawnRoot);
            ReachabilityAuditResult audit = ApplyUnreachablePolicy(startRoom, initialAudit);

            spawnRoot = PickSpawnRoom(startRoom);
            if (spawnRoot == null)
            {
                Debug.LogError(
                    "[RoomPoolSpawner] Reachability hard failure: spawn root is null after policy pass.");
                return (audit, audit.totalPlaced);
            }

            HashSet<RoomInstance> finalReachable = CollectReachableRooms(spawnRoot);
            if (!finalReachable.Contains(spawnRoot))
            {
                Debug.LogError(
                    $"[RoomPoolSpawner] Reachability hard failure: spawn room '{spawnRoot.name}' " +
                    "is not in the reachable set after policy pass.");
            }

            return (audit, audit.totalPlaced);
        }

        private static ReachabilityAuditResult CreateEmptyReachabilityResult(int placed)
        {
            return new ReachabilityAuditResult
            {
                totalPlaced = placed,
                reachableCount = placed,
                actionLines = new List<string>()
            };
        }

        private ReachabilityAuditResult AuditReachability(RoomInstance spawnRoot)
        {
            RoomInstance[] allPlaced = _connector.LevelRoot.GetComponentsInChildren<RoomInstance>();
            HashSet<RoomInstance> reachable = CollectReachableRooms(spawnRoot);
            var unreachable = new List<RoomInstance>();

            foreach (RoomInstance room in allPlaced)
            {
                if (room != null && !reachable.Contains(room))
                    unreachable.Add(room);
            }

            var unreachableSet = new HashSet<RoomInstance>(unreachable);
            List<UnreachableComponent> components = FindUnreachableComponents(unreachable, unreachableSet);

            int zeroConnections = 0;
            int isolatedSubGraph = 0;
            foreach (UnreachableComponent component in components)
            {
                if (component.cause == UnreachableCause.ZeroConnections)
                    zeroConnections += component.rooms.Count;
                else
                    isolatedSubGraph += component.rooms.Count;
            }

            return new ReachabilityAuditResult
            {
                totalPlaced = allPlaced.Length,
                reachableCount = reachable.Count,
                unreachableCount = unreachable.Count,
                zeroConnectionCount = zeroConnections,
                isolatedSubGraphCount = isolatedSubGraph,
                actionLines = new List<string>()
            };
        }

        private ReachabilityAuditResult ApplyUnreachablePolicy(
            RoomInstance startRoom,
            ReachabilityAuditResult initialAudit)
        {
            if (initialAudit.unreachableCount == 0)
            {
                initialAudit.actionLines = new List<string>();
                return initialAudit;
            }

            if (_unreachableRoomPolicy == UnreachableRoomPolicy.LogOnly)
                return ApplyLogOnlyPolicy(startRoom, initialAudit);

            int retriedCount = 0;
            int salvagedCount = 0;
            int destroyedCount = 0;
            var actionLines = new List<string>();

            // Every iteration must either salvage or remove at least one component, so the number
            // of placed rooms is a natural bound. This cap exists purely so that a future bug in
            // that invariant degrades into a logged, finished build instead of a silent editor
            // hang — the failure mode that made this loop unfixable to debug (you can't read a
            // console that never comes back). Keep it: a wrong floor beats a dead editor.
            int maxIterations = initialAudit.totalPlaced + 8;
            int iterations = 0;

            while (true)
            {
                if (++iterations > maxIterations)
                {
                    Debug.LogError(
                        $"[RoomPoolSpawner] Reachability policy hit its {maxIterations}-iteration cap " +
                        "without converging — a component is neither salvageable nor removable. " +
                        "Aborting the pass and shipping the floor as-is; unreachable rooms may remain.");
                    break;
                }

                RoomInstance spawnRoot = PickSpawnRoom(startRoom);
                ReachabilityAuditResult current = AuditReachability(spawnRoot);
                if (current.unreachableCount == 0)
                    break;

                List<UnreachableComponent> components = GetUnreachableComponents(spawnRoot);
                if (components.Count == 0)
                    break;

                UnreachableComponent component = components[0];
                string componentLabel = FormatComponentLabel(component);
                string causeLabel = component.cause == UnreachableCause.ZeroConnections
                    ? "ZeroConnections"
                    : "IsolatedSubGraph";

                bool salvaged = false;
                if (_unreachableRoomPolicy == UnreachableRoomPolicy.RetryThenDestroy)
                    salvaged = TrySalvageComponent(component.rooms, ref retriedCount, ref salvagedCount);

                if (salvaged)
                {
                    actionLines.Add(
                        $"- {componentLabel} ({causeLabel}, size={component.rooms.Count}) -> retry succeeded -> salvaged");
                    continue;
                }

                if (_unreachableRoomPolicy == UnreachableRoomPolicy.RetryThenDestroy)
                {
                    actionLines.Add(
                        $"- {componentLabel} ({causeLabel}, size={component.rooms.Count}) -> retry failed -> destroyed");
                }
                else
                {
                    actionLines.Add(
                        $"- {componentLabel} ({causeLabel}, size={component.rooms.Count}) -> destroyed");
                }

                int removed = DestroyUnreachableComponent(component.rooms);
                destroyedCount += removed;

                // Neither salvaged nor removed: re-auditing would hand back the same component
                // forever. Every earlier version of this loop only ever left via "unreachableCount
                // == 0", which is precisely how it could spin without end.
                if (removed == 0)
                {
                    Debug.LogError(
                        $"[RoomPoolSpawner] Unreachable component '{componentLabel}' " +
                        $"(size={component.rooms.Count}) could be neither salvaged nor removed — " +
                        "stopping the policy pass so the build can finish.");
                    break;
                }
            }

            RoomInstance finalSpawnRoot = PickSpawnRoom(startRoom);
            ReachabilityAuditResult finalAudit = AuditReachability(finalSpawnRoot);
            finalAudit.retriedCount = retriedCount;
            finalAudit.salvagedCount = salvagedCount;
            finalAudit.destroyedCount = destroyedCount;
            finalAudit.actionLines = actionLines;
            return finalAudit;
        }

        private ReachabilityAuditResult ApplyLogOnlyPolicy(
            RoomInstance startRoom,
            ReachabilityAuditResult initialAudit)
        {
            var actionLines = new List<string>();
            List<UnreachableComponent> components = GetUnreachableComponents(PickSpawnRoom(startRoom));

            foreach (UnreachableComponent component in components)
            {
                string causeLabel = component.cause == UnreachableCause.ZeroConnections
                    ? "ZeroConnections"
                    : "IsolatedSubGraph";

                foreach (RoomInstance room in component.rooms)
                {
                    actionLines.Add(
                        $"- {room.name} ({causeLabel}, size={component.rooms.Count}) -> logged only");
                    Debug.LogWarning(
                        $"[RoomPoolSpawner] Unreachable room: {room.name} ({causeLabel}, " +
                        $"component size={component.rooms.Count}).");
                }
            }

            initialAudit.actionLines = actionLines;
            return initialAudit;
        }

        private List<UnreachableComponent> GetUnreachableComponents(RoomInstance spawnRoot)
        {
            HashSet<RoomInstance> reachable = CollectReachableRooms(spawnRoot);
            var unreachable = new List<RoomInstance>();

            foreach (RoomInstance room in _connector.LevelRoot.GetComponentsInChildren<RoomInstance>())
            {
                if (room != null && !reachable.Contains(room))
                    unreachable.Add(room);
            }

            return FindUnreachableComponents(unreachable, new HashSet<RoomInstance>(unreachable));
        }

        /// <summary>
        /// Re-attaches a single stranded room to the reachable graph.
        ///
        /// Deliberately refuses multi-room components: TryLinkExistingRoom snaps the room it is
        /// given onto the parent's socket — it MOVES it — while the rest of that component stays
        /// put. Salvaging a component of 2+ that way teleports one room away from neighbours that
        /// still consider themselves connected to it, leaving doorways opening onto empty space.
        /// A stranded cluster is destroyed whole instead; only a lone room can be relocated safely.
        /// </summary>
        private bool TrySalvageComponent(
            List<RoomInstance> component,
            ref int retriedCount,
            ref int salvagedCount)
        {
            if (component == null || component.Count != 1)
                return false;

            RoomInstance representative = component[0];
            if (representative == null)
                return false;

            List<(RoomInstance room, WallGapController wall)> candidates = CollectLinkCandidates();
            Shuffle(candidates);

            int attempts = 0;
            foreach ((RoomInstance parent, WallGapController wall) in candidates)
            {
                if (attempts >= _unreachableRetryAttempts)
                    break;

                retriedCount++;
                attempts++;

                if (_connector.TryLinkExistingRoom(parent, wall, representative))
                {
                    salvagedCount++;
                    return true;
                }
            }

            return false;
        }

        private List<(RoomInstance room, WallGapController wall)> CollectLinkCandidates()
        {
            var candidates = new List<(RoomInstance, WallGapController)>();
            RoomInstance spawnRoot = PickSpawnRoom(FindHubRoom());
            HashSet<RoomInstance> reachable = CollectReachableRooms(spawnRoot);

            foreach (RoomInstance room in reachable)
            {
                if (room == null)
                    continue;

                foreach (WallGapController wall in room.GetClosedWalls())
                {
                    if (!room.IsWallConnected(wall))
                        candidates.Add((room, wall));
                }
            }

            return candidates;
        }

        /// <summary>Removes every room in the component; returns how many actually went away, so
        /// the caller can tell "made progress" from "refused" and stop instead of looping.</summary>
        private int DestroyUnreachableComponent(List<RoomInstance> rooms)
        {
            int destroyed = 0;
            foreach (RoomInstance room in rooms.ToList())
            {
                if (room != null && DestroyUnreachableRoom(room))
                    destroyed++;
            }

            return destroyed;
        }

        /// <summary>
        /// Removes a room from the floor *immediately as far as this build is concerned*.
        ///
        /// Object.Destroy is deferred to the end of the frame, but the whole post-build pass
        /// (overlap resolve → reachability audit → reseal) runs synchronously inside a single
        /// frame — so a plain Destroy leaves the room alive, non-null, and still returned by
        /// every GetComponentsInChildren&lt;RoomInstance&gt;() for the rest of that pass. Unlinking
        /// it from its neighbours then makes it permanently unreachable, and
        /// <see cref="ApplyUnreachablePolicy"/> would keep re-finding the same corpse, failing to
        /// salvage it, and "destroying" it again forever: an infinite loop that hangs the editor
        /// with no exception and no log (2026-08-13 root cause of the intermittent Play freeze).
        ///
        /// Deactivating first is what makes the removal visible to those enumerations, since they
        /// all use the default includeInactive:false overload. The unparent additionally keeps it
        /// out of LevelRoot-scoped walks (static batching, NavMesh, content activation) that would
        /// otherwise still pick up a room that is about to vanish.
        /// </summary>
        /// <returns>True when the room was actually removed; false when it was refused.</returns>
        private bool DestroyUnreachableRoom(RoomInstance room)
        {
            if (room == null)
                return false;

            // The player is standing in the cabin and the hub is the graph's root — removing
            // either turns a bad floor into a broken run. Both are structurally unreachable-proof
            // (the build aborts if the elevator can't attach), so this only fires on a regression.
            if (room == _persistentElevator || room.IsHub)
            {
                Debug.LogError(
                    $"[RoomPoolSpawner] Refusing to destroy {room.name} — it is the " +
                    $"{(room.IsHub ? "hub" : "persistent elevator cabin")}. Reachability or " +
                    "elevator-attach logic regressed; shipping the floor with it intact.");
                return false;
            }

            List<RoomInstance> neighbors = room.ConnectedRooms.ToList();
            foreach (RoomInstance neighbor in neighbors)
                neighbor.UnlinkNeighbor(room);

            room.ResetConnections();
            room.gameObject.SetActive(false);
            room.transform.SetParent(null, worldPositionStays: true);
            Destroy(room.gameObject);
            return true;
        }

        private static string FormatComponentLabel(UnreachableComponent component)
        {
            if (component.rooms == null || component.rooms.Count == 0)
                return "empty";

            return component.rooms[0].name;
        }

        private static List<UnreachableComponent> FindUnreachableComponents(
            List<RoomInstance> unreachable,
            HashSet<RoomInstance> unreachableSet)
        {
            var components = new List<UnreachableComponent>();
            var visited = new HashSet<RoomInstance>();

            foreach (RoomInstance start in unreachable)
            {
                if (start == null || visited.Contains(start))
                    continue;

                var componentRooms = new List<RoomInstance>();
                var queue = new Queue<RoomInstance>();
                queue.Enqueue(start);
                visited.Add(start);

                while (queue.Count > 0)
                {
                    RoomInstance current = queue.Dequeue();
                    componentRooms.Add(current);

                    foreach (RoomInstance neighbor in current.ConnectedRooms)
                    {
                        if (neighbor != null && unreachableSet.Contains(neighbor) && visited.Add(neighbor))
                            queue.Enqueue(neighbor);
                    }
                }

                UnreachableCause cause = ClassifyComponent(componentRooms);
                components.Add(new UnreachableComponent
                {
                    rooms = componentRooms,
                    cause = cause
                });
            }

            return components;
        }

        private static UnreachableCause ClassifyComponent(List<RoomInstance> rooms)
        {
            foreach (RoomInstance room in rooms)
            {
                if (room != null && room.ConnectedRooms.Count == 0)
                    return UnreachableCause.ZeroConnections;
            }

            return UnreachableCause.IsolatedSubGraph;
        }

        private static HashSet<RoomInstance> CollectReachableRooms(RoomInstance root)
        {
            var reachable = new HashSet<RoomInstance>();
            if (root == null)
                return reachable;

            var queue = new Queue<RoomInstance>();
            queue.Enqueue(root);
            reachable.Add(root);

            while (queue.Count > 0)
            {
                RoomInstance current = queue.Dequeue();
                foreach (RoomInstance neighbor in current.ConnectedRooms)
                {
                    if (neighbor != null && reachable.Add(neighbor))
                        queue.Enqueue(neighbor);
                }
            }

            return reachable;
        }

        private int CountPlacedRooms() =>
            _connector.LevelRoot.GetComponentsInChildren<RoomInstance>().Length;

        private static bool AreDirectlyConnected(RoomInstance a, RoomInstance b)
        {
            foreach (RoomInstance neighbor in a.ConnectedRooms)
            {
                if (neighbor == b)
                    return true;
            }

            return false;
        }

        private RoomInstance PickSpawnRoom(RoomInstance startRoom)
        {
            RoomInstance[] rooms = _connector.LevelRoot.GetComponentsInChildren<RoomInstance>();
            if (rooms.Length == 0)
                return startRoom;

            RoomInstance best = startRoom;
            int bestConnections = startRoom != null ? startRoom.ConnectedRooms.Count : -1;

            foreach (RoomInstance room in rooms)
            {
                if (room == null)
                    continue;

                int connections = room.ConnectedRooms.Count;
                if (connections > bestConnections)
                {
                    bestConnections = connections;
                    best = room;
                }
            }

            return best != null ? best : startRoom;
        }

        private void PlacePlayerAfterBuild(RoomInstance startRoom, RoomInstance elevatorRoom)
        {
            Transform player = _player;
            if (player == null)
            {
                PlayerMovement movement = FindAnyObjectByType<PlayerMovement>();
                if (movement != null)
                    player = movement.transform;
            }

            if (player == null || startRoom == null)
                return;

            RoomInstance spawnRoom = elevatorRoom != null ? elevatorRoom : PickSpawnRoom(startRoom);
            Vector3 spawnPosition = spawnRoom.GetSpawnPosition(_playerSpawnHeight);

            CharacterController controller = player.GetComponent<CharacterController>();
            if (controller != null)
                controller.enabled = false;

            player.SetPositionAndRotation(spawnPosition, Quaternion.Euler(0f, player.eulerAngles.y, 0f));

            if (controller != null)
                controller.enabled = true;
        }

        private bool TryPreparePrefabPool()
        {
            MigrateLegacyPrefabPool();
            BuildActivePrefabPool();
            return _activePrefabEntries.Length > 0;
        }

        private void MigrateLegacyPrefabPool()
        {
            if (_roomPrefabEntries != null && _roomPrefabEntries.Length > 0)
                return;

            if (_roomPrefabs == null || _roomPrefabs.Length == 0)
                return;

            var migrated = new List<RoomPrefabEntry>(_roomPrefabs.Length);
            foreach (GameObject prefab in _roomPrefabs)
            {
                if (prefab == null)
                    continue;

                migrated.Add(new RoomPrefabEntry(prefab));
            }

            _roomPrefabEntries = migrated.ToArray();
        }

        private void BuildActivePrefabPool()
        {
            if (_roomPrefabEntries == null || _roomPrefabEntries.Length == 0)
            {
                _activePrefabEntries = Array.Empty<RoomPrefabEntry>();
                return;
            }

            var valid = new List<RoomPrefabEntry>(_roomPrefabEntries.Length);
            foreach (RoomPrefabEntry entry in _roomPrefabEntries)
            {
                if (entry == null || !entry.IsValid)
                    continue;

                valid.Add(entry);
            }

            _activePrefabEntries = valid.ToArray();

            if (_activePrefabEntries.Length == 0)
                Debug.LogWarning("[RoomPoolSpawner] No valid room prefab entries (null prefab).");
        }

        private void Shuffle<T>(IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = NextInt(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        private void InitializeRng()
        {
            if (_useFixedSeed)
            {
                _lastUsedSeed = _fixedSeed;
            }
            else if (_randomizeSeedOnPlay || _lastUsedSeed == 0)
            {
                _lastUsedSeed = System.Environment.TickCount ^ Guid.NewGuid().GetHashCode();
            }

            _rng = new System.Random(_lastUsedSeed);
        }

        private int NextInt(int minInclusive, int maxExclusive)
        {
            if (_rng == null)
                InitializeRng();

            return _rng.Next(minInclusive, maxExclusive);
        }

        private static RoomInstance GetRoom(GameObject go)
        {
            RoomInstance r = go.GetComponent<RoomInstance>() ?? go.AddComponent<RoomInstance>();
            r.CacheWalls();
            return r;
        }

        private void ClearLevelRoot()
        {
            Transform root = _connector.LevelRoot;
            // Must happen before the Destroy calls: the rooms are still alive this frame and the
            // reset reads the surviving ones off the hierarchy.
            RoomLightmapData.ResetForNewFloor(root);
            for (int i = root.childCount - 1; i >= 0; i--)
                Destroy(root.GetChild(i).gameObject);
        }

        private void HideHandPlacedRooms()
        {
            Transform root = _connector != null ? _connector.LevelRoot : null;
            foreach (RoomInstance room in FindObjectsByType<RoomInstance>())
            {
                if (root != null && room.transform.IsChildOf(root))
                    continue;

                room.gameObject.SetActive(false);
            }
        }
    }
}
