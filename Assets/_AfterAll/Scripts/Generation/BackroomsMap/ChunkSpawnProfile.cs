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

        [Header("Room Dimensions")]
        [Tooltip("Walkable floor surface (Y=0) up to the underside of the ceiling slab.")]
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

        public float RoomHeight => _roomHeight;
        public float FloorSlabThickness => _floorSlabThickness;
        public float CeilingSlabThickness => _ceilingSlabThickness;
        public float LightInsetBelowCeiling => _lightInsetBelowCeiling;

        public float LightFixtureY => _roomHeight - _lightInsetBelowCeiling;
        public float WallCenterY => _roomHeight * 0.5f;
        public float FloorSlabCenterY => _floorSlabThickness * 0.5f;
        public float CeilingSlabCenterY => _roomHeight + _ceilingSlabThickness * 0.5f;

        public bool HasWallPrefab => _wallBlockPrefab != null;
        public bool HasFloorPrefab => _floorPrefab != null;
        public bool HasCeilingPrefab => _ceilingPrefab != null;
        public bool HasLightPrefab => _lightPanelPrefab != null || _ceilingLightPrefab != null;
    }
}
