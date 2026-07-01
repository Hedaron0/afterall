using System.Collections.Generic;
using UnityEngine;

namespace AfterAll.Environment
{
    public class RoomInstance : MonoBehaviour
    {
        private WallGapController[] _walls = System.Array.Empty<WallGapController>();
        private readonly HashSet<WallGapController> _connectedWalls = new();
        private readonly List<RoomInstance> _connectedRooms = new();

        public IReadOnlyList<WallGapController> Walls => _walls;
        public IReadOnlyCollection<WallGapController> ConnectedWalls => _connectedWalls;
        public IReadOnlyList<RoomInstance> ConnectedRooms => _connectedRooms;

        private void Awake() => CacheWalls();

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

        public IEnumerable<WallGapController> GetClosedWalls()
        {
            foreach (WallGapController wall in _walls)
            {
                if (!wall.hasOpening)
                    yield return wall;
            }
        }
    }
}
