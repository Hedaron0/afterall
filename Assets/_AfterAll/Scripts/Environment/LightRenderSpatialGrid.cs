using System.Collections.Generic;
using UnityEngine;

namespace AfterAll.Environment
{
    public sealed class LightRenderSpatialGrid
    {
        readonly Dictionary<long, List<FluorescentLight>> _cells = new(256);

        public void Add(FluorescentLight panel, float cellSize)
        {
            var key = CellKey(panel.HorizontalPosition, cellSize);
            if (!_cells.TryGetValue(key, out var list))
            {
                list = new List<FluorescentLight>(4);
                _cells[key] = list;
            }

            if (!list.Contains(panel))
                list.Add(panel);
        }

        public void Remove(FluorescentLight panel, float cellSize)
        {
            var key = CellKey(panel.HorizontalPosition, cellSize);
            if (!_cells.TryGetValue(key, out var list))
                return;

            list.Remove(panel);
            if (list.Count == 0)
                _cells.Remove(key);
        }

        public void Query(Vector3 center, float radius, float cellSize, List<FluorescentLight> results)
        {
            results.Clear();

            int minX = Mathf.FloorToInt((center.x - radius) / cellSize);
            int maxX = Mathf.FloorToInt((center.x + radius) / cellSize);
            int minZ = Mathf.FloorToInt((center.z - radius) / cellSize);
            int maxZ = Mathf.FloorToInt((center.z + radius) / cellSize);

            for (int x = minX; x <= maxX; x++)
            {
                for (int z = minZ; z <= maxZ; z++)
                {
                    long key = PackCell(x, z);
                    if (!_cells.TryGetValue(key, out var list))
                        continue;

                    for (int i = 0; i < list.Count; i++)
                    {
                        var panel = list[i];
                        if (panel != null && !results.Contains(panel))
                            results.Add(panel);
                    }
                }
            }
        }

        public void Rebuild(List<FluorescentLight> panels, float cellSize)
        {
            _cells.Clear();
            for (int i = 0; i < panels.Count; i++)
            {
                if (panels[i] != null)
                    Add(panels[i], cellSize);
            }
        }

        static long CellKey(Vector3 position, float cellSize)
        {
            int x = Mathf.FloorToInt(position.x / cellSize);
            int z = Mathf.FloorToInt(position.z / cellSize);
            return PackCell(x, z);
        }

        static long PackCell(int x, int z) => ((long)x << 32) | (uint)z;
    }
}
