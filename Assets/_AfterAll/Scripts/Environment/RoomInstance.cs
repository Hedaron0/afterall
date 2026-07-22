using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AfterAll.Environment
{
    public class RoomInstance : MonoBehaviour
    {
        public struct SocketValidationReport
        {
            public int missingContractCount;
            public int duplicateDirectionCount;
        }

        private WallGapController[] _walls = System.Array.Empty<WallGapController>();
        private readonly HashSet<WallGapController> _connectedWalls = new();
        private readonly Dictionary<WallGapController, RoomInstance> _wallNeighbors = new();
        private readonly List<RoomInstance> _connectedRooms = new();
        private RoomFootprint _footprint;

        public IReadOnlyList<WallGapController> Walls => _walls;
        public IReadOnlyCollection<WallGapController> ConnectedWalls => _connectedWalls;
        public IReadOnlyList<RoomInstance> ConnectedRooms => _connectedRooms;
        public bool IsHub { get; private set; }
        public int GraphDepth { get; private set; } = -1;
        public string PrefabId { get; set; } = string.Empty;

        private void Awake() => CacheWalls();

        public void MarkAsHub()
        {
            IsHub = true;
            GraphDepth = 0;
        }

        public void SetGraphDepth(int depth) => GraphDepth = depth;

        /// <summary>Baked design-time shape used by <see cref="ContainsPointXZ"/> for accurate
        /// point→room classification. Wired by RoomPoolSpawner right after instantiation.</summary>
        public void SetFootprint(RoomFootprint footprint) => _footprint = footprint;

        public bool IsDeadEnd() => _connectedRooms.Count <= 1;

        public bool IsJunction() => _connectedRooms.Count >= 2;

        public void CacheWalls()
        {
            _walls = GetComponentsInChildren<WallGapController>(true);
        }

        public WallGapController GetWall(string wallName)
        {
            foreach (WallGapController wall in _walls)
            {
                if (wall.gameObject.name == wallName)
                    return wall;
            }

            return null;
        }

        public void SealAllWalls()
        {
            foreach (WallGapController wall in _walls)
                wall.ConfigureOpening(false, false, 0f);
        }

        public void OpenWall(WallGapController wall, bool spawnFrame)
        {
            if (wall == null)
                return;

            float offset = WallGapController.GetWallCenterGapOffset(wall);
            wall.ConfigureOpening(true, spawnFrame, offset);
        }

        public void OpenWall(WallGapController wall, float offsetMeters, bool spawnFrame)
        {
            if (wall == null)
                return;

            wall.ConfigureOpening(true, spawnFrame, offsetMeters);
        }

        public bool IsWallConnected(WallGapController wall) => _connectedWalls.Contains(wall);

        /// <summary>
        /// True when the wall at <paramref name="wallIndex"/> has a runtime gap opening.
        /// Used by WallDecor markers to skip walls opened for room connections.
        /// </summary>
        public bool IsWallOpen(int wallIndex)
        {
            if (wallIndex < 0)
                return false;

            foreach (WallGapController wall in _walls)
            {
                if (wall == null || !wall.TryGetBakedSocket(out RoomSocket socket))
                    continue;

                if (socket.WallIndex == wallIndex)
                    return wall.hasOpening;
            }

            return false;
        }

        public void MarkWallConnected(WallGapController wall, RoomInstance neighbor)
        {
            if (wall == null || neighbor == null)
                return;

            _connectedWalls.Add(wall);
            _wallNeighbors[wall] = neighbor;

            if (!_connectedRooms.Contains(neighbor))
                _connectedRooms.Add(neighbor);
        }

        /// <summary>
        /// Clears all connection bookkeeping (connected walls, neighbors, socket flags) WITHOUT
        /// touching wall meshes. Used on the persistent elevator cabin before each floor rebuild:
        /// its old neighbors are destroyed with the floor, but its doorway geometry stays valid
        /// and gets re-linked to the new hub by the normal ApplyPlannedConnection path.
        /// </summary>
        public void ResetConnections()
        {
            foreach (WallGapController wall in _connectedWalls)
            {
                if (wall != null && wall.TryGetSocket(out RoomSocket socket))
                    socket.IsConnected = false;
            }

            _connectedWalls.Clear();
            _wallNeighbors.Clear();
            _connectedRooms.Clear();
        }

        public void UnlinkNeighbor(RoomInstance neighbor)
        {
            if (neighbor == null)
                return;

            _connectedRooms.Remove(neighbor);

            var wallsToClose = new List<WallGapController>();
            foreach (KeyValuePair<WallGapController, RoomInstance> entry in _wallNeighbors)
            {
                if (entry.Value == neighbor)
                    wallsToClose.Add(entry.Key);
            }

            foreach (WallGapController wall in wallsToClose)
            {
                _wallNeighbors.Remove(wall);
                _connectedWalls.Remove(wall);
                wall.ConfigureOpening(false, false, 0f);

                if (wall.TryGetSocket(out RoomSocket socket))
                    socket.IsConnected = false;
            }
        }

        /// <summary>World position of the doorway (socket) leading to a directly-connected
        /// neighbor. False if the rooms aren't neighbors or the socket is missing.</summary>
        public bool TryGetDoorwayTo(RoomInstance neighbor, out Vector3 doorWorldPos)
        {
            doorWorldPos = default;
            if (neighbor == null)
                return false;

            foreach (KeyValuePair<WallGapController, RoomInstance> entry in _wallNeighbors)
            {
                if (entry.Value != neighbor || entry.Key == null)
                    continue;

                if (entry.Key.TryGetSocket(out RoomSocket socket) && socket != null)
                {
                    doorWorldPos = socket.transform.position;
                    return true;
                }
            }

            return false;
        }

        /// <summary>True when the XZ of a world point falls inside this room's true baked
        /// footprint (Y ignored — floors are flat and height-aligned). Used for point→room
        /// lookups (nav graph pathing). Tests against the baked <see cref="RoomFootprint"/>
        /// rectangle when wired, so it stays correct even when two rooms' loose renderer AABBs
        /// (inflated by wall thickness / frame props, or a planner overlap-tolerance edge case)
        /// happen to overlap — falls back to the old renderer-bounds check only for rooms with
        /// no footprint reference (e.g. hand-placed test-scene rooms).</summary>
        public bool ContainsPointXZ(Vector3 worldPos)
        {
            if (_footprint != null)
                return PointInFootprintXZ(worldPos);

            Bounds bounds = GetWorldBounds();
            return worldPos.x >= bounds.min.x && worldPos.x <= bounds.max.x
                && worldPos.z >= bounds.min.z && worldPos.z <= bounds.max.z;
        }

        private bool PointInFootprintXZ(Vector3 worldPos)
        {
            Vector2 min = _footprint.BoundsMin;
            Vector2 max = _footprint.BoundsMax;

            Vector3[] corners =
            {
                transform.TransformPoint(new Vector3(min.x, 0f, min.y)),
                transform.TransformPoint(new Vector3(max.x, 0f, min.y)),
                transform.TransformPoint(new Vector3(max.x, 0f, max.y)),
                transform.TransformPoint(new Vector3(min.x, 0f, max.y)),
            };

            // Point-in-quad via edge cross-product sign consistency — correct for any yaw,
            // not just the cardinal 0/90/180/270 rotations the planner currently uses.
            bool? inside = null;
            for (int i = 0; i < 4; i++)
            {
                Vector3 a = corners[i];
                Vector3 b = corners[(i + 1) % 4];
                float cross = (b.x - a.x) * (worldPos.z - a.z) - (b.z - a.z) * (worldPos.x - a.x);
                bool positive = cross >= 0f;
                if (inside == null)
                    inside = positive;
                else if (inside != positive)
                    return false;
            }

            return true;
        }

        public IEnumerable<WallGapController> GetOpenUnconnectedWalls()
        {
            foreach (WallGapController wall in _walls)
            {
                if (wall.hasOpening && !_connectedWalls.Contains(wall))
                    yield return wall;
            }
        }

        public Vector3 GetApproximateCenter()
        {
            return GetWorldBounds().center;
        }

        /// <summary>True when worldPos falls inside a sealed-off compartment (see
        /// <see cref="EntitySpawnBlockZone"/>) — entity spawn/patrol-target picking must skip it.</summary>
        public bool IsPointBlockedForEntities(Vector3 worldPos)
        {
            foreach (EntitySpawnBlockZone zone in GetComponentsInChildren<EntitySpawnBlockZone>(true))
            {
                if (zone != null && zone.ContainsPoint(worldPos))
                    return true;
            }

            return false;
        }

        public Vector3 GetSpawnPosition(float heightAboveFloor = 1.0f)
        {
            Bounds bounds = GetWorldBounds();
            Vector3 position = bounds.center;
            position.y = bounds.min.y + heightAboveFloor;
            return position;
        }

        public Bounds GetWorldBounds()
        {
            Renderer[] renderers = GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                return new Bounds(transform.position, Vector3.one);

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            return bounds;
        }

        /// <summary>XZ footprint from floor renderers when available.</summary>
        public Bounds GetFloorFootprintBounds()
        {
            Renderer[] renderers = GetComponentsInChildren<Renderer>();
            Renderer[] floors = System.Array.FindAll(renderers, IsFloorRenderer);

            if (floors.Length > 0)
                return FlattenFootprint(BuildBounds(floors));

            return FlattenFootprint(GetWorldBounds());
        }

        /// <summary>Top of floor meshes — walkable surface used to keep rooms coplanar.</summary>
        public float GetWalkableFloorY()
        {
            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            float top = float.NegativeInfinity;
            bool any = false;

            foreach (Renderer renderer in renderers)
            {
                if (!IsFloorRenderer(renderer))
                    continue;

                top = Mathf.Max(top, renderer.bounds.max.y);
                any = true;
            }

            if (any)
                return top;

            return GetWorldBounds().min.y;
        }

        /// <summary>Walkable interior used for parent penetration checks.</summary>
        public Bounds GetInteriorFootprintBounds(float insetPerSide = 0.2f)
        {
            Bounds footprint = GetFloorFootprintBounds();
            footprint.Expand(new Vector3(-insetPerSide * 2f, 0f, -insetPerSide * 2f));

            if (footprint.size.x < 0.5f)
                footprint.size = new Vector3(0.5f, footprint.size.y, footprint.size.z);
            if (footprint.size.z < 0.5f)
                footprint.size = new Vector3(footprint.size.x, footprint.size.y, 0.5f);

            return footprint;
        }

        private static Bounds BuildBounds(Renderer[] renderers)
        {
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            return bounds;
        }

        private static Bounds FlattenFootprint(Bounds world)
        {
            float floorY = world.min.y;
            Vector3 center = world.center;
            center.y = floorY;
            Vector3 size = world.size;
            size.y = 0.01f;
            return new Bounds(center, size);
        }

        private static bool IsFloorRenderer(Renderer renderer)
        {
            if (renderer == null)
                return false;

            string objectName = renderer.gameObject.name;
            if (objectName.StartsWith("Cube", System.StringComparison.OrdinalIgnoreCase))
                return false;

            return objectName.IndexOf("Floor", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public IEnumerable<WallGapController> GetClosedWalls()
        {
            foreach (WallGapController wall in _walls)
            {
                if (!wall.hasOpening)
                    yield return wall;
            }
        }

        public SocketValidationReport ValidateSocketContracts(bool logWarnings)
        {
            var report = new SocketValidationReport();
            var usedContracts = new HashSet<string>();

            foreach (WallGapController wall in _walls)
            {
                if (!wall.TryGetBakedSocket(out RoomSocket socket))
                {
                    report.missingContractCount++;
                    if (logWarnings)
                        Debug.LogWarning($"[RoomInstance] No baked socket on {name}/{wall.name}");
                    continue;
                }

                if (!socket.HasValidContract)
                {
                    report.missingContractCount++;
                    if (logWarnings)
                        Debug.LogWarning($"[RoomInstance] Missing socket contract on {name}/{socket.name}");
                    continue;
                }

                if (!usedContracts.Add(socket.DebugContractLabel()))
                {
                    report.duplicateDirectionCount++;
                    if (logWarnings)
                        Debug.LogWarning($"[RoomInstance] Duplicate socket contract {socket.DebugContractLabel()} on {name}");
                }
            }

            return report;
        }
    }
}
