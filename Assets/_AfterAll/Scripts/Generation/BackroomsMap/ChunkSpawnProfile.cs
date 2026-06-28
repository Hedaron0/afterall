using UnityEngine;

namespace AfterAll.Generation.BackroomsMap
{
    [CreateAssetMenu(fileName = "ChunkSpawnProfile", menuName = "AfterAll/Generation/Chunk Spawn Profile")]
    public sealed class ChunkSpawnProfile : ScriptableObject
    {
        [Header("Prefabs")]
        [SerializeField] private GameObject _wallBlockPrefab;
        [SerializeField] private GameObject _floorPrefab;
        [SerializeField] private GameObject _ceilingPrefab;
        [Tooltip("Preferred for proc-gen. Falls back to Ceiling Light Prefab if empty.")]
        [SerializeField] private GameObject _lightPanelPrefab;
        [SerializeField] private GameObject _ceilingLightPrefab;
        [SerializeField] private GameObject _doorPrefab;

        [Header("Door Opening")]
        [Tooltip("Clear width of the door hole in the wall.")]
        [SerializeField] [Min(0.5f)] private float _doorWidth = 1f;
        [Tooltip("Clear height of the door hole (floor to top of opening).")]
        [SerializeField] [Min(1f)] private float _doorHeight = 2.1f;
        [Tooltip("Depth of door trim + panel (slightly thicker than the door mesh).")]
        [SerializeField] [Min(0.05f)] private float _frameDepth = 0.15f;

        [Header("Room Dimensions")]
        [Tooltip("Walkable floor top surface is Y = 0. Slab sits below that.")]
        [SerializeField] [Min(0.1f)] private float _roomHeight = 4f;
        [SerializeField] [Min(0.01f)] private float _floorSlabThickness = 0.2f;
        [SerializeField] [Min(0.01f)] private float _ceilingSlabThickness = 0.2f;
        [Tooltip("How far below the ceiling underside the light panel sits.")]
        [SerializeField] [Min(0f)] private float _lightInsetBelowCeiling = 0.08f;

        public GameObject WallBlockPrefab => _wallBlockPrefab;
        public GameObject FloorPrefab => _floorPrefab;
        public GameObject CeilingPrefab => _ceilingPrefab;
        public GameObject LightPanelPrefab => _lightPanelPrefab;
        public GameObject CeilingLightPrefab => _ceilingLightPrefab;
        public GameObject DoorPrefab => _doorPrefab;
        public float DoorWidth => _doorWidth;
        public float DoorHeight => _doorHeight;
        public float FrameDepth => _frameDepth;

        public float RoomHeight => _roomHeight;
        public float FloorSlabThickness => _floorSlabThickness;
        public float CeilingSlabThickness => _ceilingSlabThickness;
        public float LightInsetBelowCeiling => _lightInsetBelowCeiling;

        public float LightFixtureY => _roomHeight - _lightInsetBelowCeiling;
        /// <summary>Walkable floor top — walls and door bottoms align here.</summary>
        public float FloorTopY => 0f;
        public float WallBaseY => FloorTopY;
        public float WallCenterY => WallBaseY + _roomHeight * 0.5f;
        /// <summary>Floor slab sits entirely below <see cref="FloorTopY"/>.</summary>
        public float FloorSlabCenterY => FloorTopY - _floorSlabThickness * 0.5f;
        public float CeilingSlabCenterY => _roomHeight + _ceilingSlabThickness * 0.5f;

        public bool HasWallPrefab => _wallBlockPrefab != null;
        public bool HasFloorPrefab => _floorPrefab != null;
        public bool HasCeilingPrefab => _ceilingPrefab != null;
        public bool HasLightPrefab => _lightPanelPrefab != null || _ceilingLightPrefab != null;
    }
}
