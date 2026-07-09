using System.Collections.Generic;
using UnityEngine;

namespace AfterAll.Environment
{
    /// <summary>
    /// XZ occupancy grid for compact frontier scoring during room pool generation.
    /// </summary>
    public sealed class RoomLayoutOccupancy
    {
        private readonly HashSet<Vector2Int> _occupiedCells = new();
        private readonly List<Vector3> _roomCenters = new();
        private float _cellSize = 6f;
        private float _maxExtentFromCentroid;

        public int RegisteredRoomCount => _roomCenters.Count;

        public void Configure(float cellSize)
        {
            _cellSize = Mathf.Max(1f, cellSize);
        }

        public void Clear()
        {
            _occupiedCells.Clear();
            _roomCenters.Clear();
            _maxExtentFromCentroid = 0f;
        }

        public void Register(RoomInstance room)
        {
            if (room == null)
                return;

            Bounds footprint = room.GetFloorFootprintBounds();
            StampFootprint(footprint);

            Vector3 center = footprint.center;
            _roomCenters.Add(center);

            if (_roomCenters.Count == 1)
            {
                _maxExtentFromCentroid = 0f;
                return;
            }

            Vector3 clusterCentroid = GetClusterCentroid();
            float extent = Vector3.Distance(
                new Vector3(center.x, 0f, center.z),
                new Vector3(clusterCentroid.x, 0f, clusterCentroid.z));
            _maxExtentFromCentroid = Mathf.Max(_maxExtentFromCentroid, extent);
        }

        public Vector3 GetClusterCentroid()
        {
            if (_roomCenters.Count == 0)
                return Vector3.zero;

            Vector3 sum = Vector3.zero;
            foreach (Vector3 center in _roomCenters)
                sum += center;

            return sum / _roomCenters.Count;
        }

        /// <summary>
        /// Counts occupied cells in the 8-neighborhood around <paramref name="worldPosition"/>.
        /// </summary>
        public int GetNeighborOccupancy(Vector3 worldPosition)
        {
            Vector2Int cell = WorldToCell(worldPosition);
            int count = 0;

            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dz == 0)
                    continue;

                if (_occupiedCells.Contains(new Vector2Int(cell.x + dx, cell.y + dz)))
                    count++;
            }

            return count;
        }

        public float GetNormalizedCentroidPull(Vector3 worldPosition)
        {
            if (_roomCenters.Count <= 1)
                return 1f;

            Vector3 centroid = GetClusterCentroid();
            float distance = Vector3.Distance(
                new Vector3(worldPosition.x, 0f, worldPosition.z),
                new Vector3(centroid.x, 0f, centroid.z));

            float denom = Mathf.Max(_maxExtentFromCentroid, _cellSize);
            return 1f - Mathf.Clamp01(distance / denom);
        }

        private void StampFootprint(Bounds footprint)
        {
            Vector3 min = footprint.min;
            Vector3 max = footprint.max;

            Vector2Int minCell = WorldToCell(min);
            Vector2Int maxCell = WorldToCell(max);

            for (int x = minCell.x; x <= maxCell.x; x++)
            for (int z = minCell.y; z <= maxCell.y; z++)
                _occupiedCells.Add(new Vector2Int(x, z));
        }

        private Vector2Int WorldToCell(Vector3 world)
        {
            return new Vector2Int(
                Mathf.FloorToInt(world.x / _cellSize),
                Mathf.FloorToInt(world.z / _cellSize));
        }
    }
}
