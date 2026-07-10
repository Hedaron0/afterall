using System;
using UnityEngine;

namespace AfterAll.Environment
{
    /// <summary>
    /// Baked 2D layout data for a room prefab (walls + floor AABB in prefab-local XZ).
    /// </summary>
    [CreateAssetMenu(fileName = "RoomFootprint", menuName = "AfterAll/Generation/Room Footprint")]
    public class RoomFootprint : ScriptableObject
    {
        public const float DefaultGapWidthM = 1.3f;

        [Serializable]
        public struct Wall
        {
            public string name;
            public Vector2 seamLocal;
            public Vector2 axisLocal;
            public Vector2 startLocal;
            public Vector2 endLocal;
            public float lengthM;
            public Vector2 outwardLocal;
            public SocketDirection direction;
            public bool doorValid;
        }

        [SerializeField] private GameObject _prefab;
        [SerializeField, Min(1)] private int _spawnWeight = 10;
        [SerializeField] private Vector2 _boundsMin;
        [SerializeField] private Vector2 _boundsMax;
        [SerializeField] private Wall[] _walls = Array.Empty<Wall>();
        [SerializeField] private float _gapWidthM = DefaultGapWidthM;

        public GameObject Prefab => _prefab;
        public string PrefabId => _prefab != null ? _prefab.name : name;
        public int SpawnWeight => Mathf.Max(1, _spawnWeight);
        public Vector2 BoundsMin => _boundsMin;
        public Vector2 BoundsMax => _boundsMax;
        public Vector2 BoundsSize => _boundsMax - _boundsMin;
        public Vector2 BoundsCenter => (_boundsMin + _boundsMax) * 0.5f;
        public Wall[] Walls => _walls;
        public float GapWidthM => _gapWidthM > 0.05f ? _gapWidthM : DefaultGapWidthM;

        public void SetBakedData(
            GameObject prefab,
            int spawnWeight,
            Vector2 boundsMin,
            Vector2 boundsMax,
            Wall[] walls,
            float gapWidthM = DefaultGapWidthM)
        {
            _prefab = prefab;
            _spawnWeight = Mathf.Max(1, spawnWeight);
            _boundsMin = boundsMin;
            _boundsMax = boundsMax;
            _walls = walls ?? Array.Empty<Wall>();
            _gapWidthM = gapWidthM > 0.05f ? gapWidthM : DefaultGapWidthM;
        }

        public bool TryGetWall(string wallName, out Wall wall)
        {
            for (int i = 0; i < _walls.Length; i++)
            {
                if (_walls[i].name == wallName)
                {
                    wall = _walls[i];
                    return true;
                }
            }

            wall = default;
            return false;
        }
    }
}
