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
        private readonly List<RoomInstance> _connectedRooms = new();

        public IReadOnlyList<WallGapController> Walls => _walls;
        public IReadOnlyCollection<WallGapController> ConnectedWalls => _connectedWalls;
        public IReadOnlyList<RoomInstance> ConnectedRooms => _connectedRooms;
        public bool IsHub { get; private set; }
        public int GraphDepth { get; private set; } = -1;

        private void Awake() => CacheWalls();

        public void MarkAsHub()
        {
            IsHub = true;
            GraphDepth = 0;
        }

        public void SetGraphDepth(int depth) => GraphDepth = depth;

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

        public void MarkWallConnected(WallGapController wall, RoomInstance neighbor)
        {
            if (wall == null || neighbor == null)
                return;

            _connectedWalls.Add(wall);

            if (!_connectedRooms.Contains(neighbor))
                _connectedRooms.Add(neighbor);
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
