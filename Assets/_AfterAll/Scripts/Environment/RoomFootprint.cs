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
    /// Layout role for settlement-spine picking (Hub / Room / Corridor).
    /// Auto classifies from footprint bounds.
    /// </summary>
    public enum RoomRole
    {
        Auto = 0,
        Hub = 1,
        Room = 2,
        Corridor = 3
    }

    /// <summary>
    /// Baked 2D layout data for a room prefab (walls + floor AABB in prefab-local XZ).
    /// Role drives settlement vs corridor phases — no weight-based mix.
    /// </summary>
    [CreateAssetMenu(fileName = "RoomFootprint", menuName = "AfterAll/Generation/Room Footprint")]
    public class RoomFootprint : ScriptableObject
    {
        public const float DefaultGapWidthM = 1.3f;
        public const float CorridorAspectRatio = 2.5f;
        public const float CorridorMaxMinSideM = 4f;
        public const float HubMinAreaM2 = 300f;

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
        [SerializeField, HideInInspector] private RoomRole _role = RoomRole.Auto;
        [SerializeField] private Vector2 _boundsMin;
        [SerializeField] private Vector2 _boundsMax;
        [SerializeField] private Wall[] _walls = Array.Empty<Wall>();
        [SerializeField] private float _gapWidthM = DefaultGapWidthM;

        public GameObject Prefab => _prefab;
        public string PrefabId => _prefab != null ? _prefab.name : name;
        public RoomRole Role => _role;
        public RoomRole ResolvedRole => ResolveRole(_role, BoundsSize, BoundsAreaM2);
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

        public static RoomRole ClassifyFromBounds(Vector2 boundsSize, float areaM2)
        {
            float width = Mathf.Abs(boundsSize.x);
            float depth = Mathf.Abs(boundsSize.y);
            float minSide = Mathf.Min(width, depth);
            float maxSide = Mathf.Max(width, depth);
            float aspect = minSide > 0.01f ? maxSide / minSide : 1f;
            float area = Mathf.Max(0.5f, areaM2);

            if (aspect >= CorridorAspectRatio || minSide < CorridorMaxMinSideM)
                return RoomRole.Corridor;

            if (area >= HubMinAreaM2)
                return RoomRole.Hub;

            return RoomRole.Room;
        }

        public static RoomRole ResolveRole(RoomRole role, Vector2 boundsSize, float areaM2) =>
            role == RoomRole.Auto ? ClassifyFromBounds(boundsSize, areaM2) : role;

        public RoomRole RecomputeRoleFromBounds(bool forceAuto = false)
        {
            if (forceAuto || _role == RoomRole.Auto)
                _role = RoomRole.Auto;
            return ResolvedRole;
        }

        public void SetRole(RoomRole role) => _role = role;

        public void SetBakedData(
            GameObject prefab,
            Vector2 boundsMin,
            Vector2 boundsMax,
            Wall[] walls,
            float gapWidthM = DefaultGapWidthM,
            RoomRole role = RoomRole.Auto)
        {
            _prefab = prefab;
            _boundsMin = boundsMin;
            _boundsMax = boundsMax;
            _walls = walls ?? Array.Empty<Wall>();
            _gapWidthM = gapWidthM > 0.05f ? gapWidthM : DefaultGapWidthM;
            _role = role;
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
