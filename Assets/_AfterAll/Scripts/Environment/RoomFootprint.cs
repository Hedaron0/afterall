using System;
using UnityEngine;

namespace AfterAll.Environment
{
    /// <summary>
    /// How a wall opens when connected to another room.
    /// </summary>
    public enum WallOpeningMode
    {
        /// <summary>Classic 1.3m (gapWidth) cut between WallLeft/WallRight.</summary>
        StandardGap = 0,
        /// <summary>Hide entire Left+Right when open — full wall width opening.</summary>
        FullWall = 1,
        /// <summary>Portal only (no end mesh ops). Always socket-based.</summary>
        OpenEnd = 2
    }

    /// <summary>
    /// Baked 2D layout data for a room prefab (walls + floor AABB in prefab-local XZ).
    /// </summary>
    [CreateAssetMenu(fileName = "RoomFootprint", menuName = "AfterAll/Generation/Room Footprint")]
    public class RoomFootprint : ScriptableObject
    {
        public const float DefaultGapWidthM = 1.3f;
        public const float CorridorAspectRatio = 2.5f;
        public const float CorridorMaxMinSideM = 4f;

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
            public WallOpeningMode openingMode;
            /// <summary>Effective opening width when connected (1.3m gap or full wall).</summary>
            public float openingWidthM;
        }

        [SerializeField] private GameObject _prefab;
        [Tooltip("Excluded from the general room pool (Top View / Assign Footprints / auto-load fallback). Attached separately by RoomPoolSpawner as the run's entry room.")]
        [SerializeField] private bool _isElevator;
        [SerializeField] private Vector2 _boundsMin;
        [SerializeField] private Vector2 _boundsMax;
        [SerializeField] private Wall[] _walls = Array.Empty<Wall>();
        [SerializeField] private float _gapWidthM = DefaultGapWidthM;

        public GameObject Prefab => _prefab;
        public string PrefabId => _prefab != null ? _prefab.name : name;
        public bool IsElevator => _isElevator;
        public Vector2 BoundsMin => _boundsMin;
        public Vector2 BoundsMax => _boundsMax;
        public Vector2 BoundsSize => _boundsMax - _boundsMin;
        public Vector2 BoundsCenter => (_boundsMin + _boundsMax) * 0.5f;
        public float BoundsAreaM2
        {
            get
            {
                Vector2 size = BoundsSize;
                return Mathf.Max(0.5f, Mathf.Abs(size.x * size.y));
            }
        }
        public Wall[] Walls => _walls;
        public float GapWidthM => _gapWidthM > 0.05f ? _gapWidthM : DefaultGapWidthM;

        /// <summary>Long/narrow footprint — used as corridor bridge pieces (geometry, not a manual role).</summary>
        public bool IsCorridorShape
        {
            get
            {
                Vector2 size = BoundsSize;
                float width = Mathf.Abs(size.x);
                float depth = Mathf.Abs(size.y);
                float minSide = Mathf.Min(width, depth);
                float maxSide = Mathf.Max(width, depth);
                float aspect = minSide > 0.01f ? maxSide / minSide : 1f;
                return aspect >= CorridorAspectRatio || minSide < CorridorMaxMinSideM;
            }
        }

        /// <summary>Higher = better corridor candidate (long and skinny).</summary>
        public float PassageScore
        {
            get
            {
                Vector2 size = BoundsSize;
                float width = Mathf.Abs(size.x);
                float depth = Mathf.Abs(size.y);
                float minSide = Mathf.Max(0.01f, Mathf.Min(width, depth));
                float maxSide = Mathf.Max(width, depth);
                return maxSide / minSide;
            }
        }

        public void SetBakedData(
            GameObject prefab,
            Vector2 boundsMin,
            Vector2 boundsMax,
            Wall[] walls,
            float gapWidthM = DefaultGapWidthM)
        {
            _prefab = prefab;
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
