using System.Collections.Generic;
using UnityEngine;

namespace AfterAll.Environment
{
    public enum RoomSpawnCategory
    {
        Prop = 0,
        Pillar = 1,
        CeilingLight = 2,
        WallDecor = 3
    }

    /// <summary>
    /// Marker placed on empty GameObjects (or invisible editor cubes) inside room prefabs.
    /// RoomPoolSpawner calls <see cref="SpawnForRoom"/> after generation + reachability pass.
    /// Harun authors marker layout in prefabs; runtime picks a random subset per seed.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("AfterAll/Environment/Room Spawn Point")]
    public class RoomSpawnPoint : MonoBehaviour
    {
        [SerializeField] private RoomSpawnCategory _category = RoomSpawnCategory.Prop;
        [SerializeField] private GameObject[] _prefabOptions = System.Array.Empty<GameObject>();
        [SerializeField, Range(0f, 1f)] private float _spawnChance = 0.5f;
        [SerializeField] private bool _randomYaw = true;
        [SerializeField] private bool _alignToMarkerRotation = true;
        [Tooltip("Optional. Only one marker with the same non-empty group id will spawn per room.")]
        [SerializeField] private string _groupId;
        [Tooltip("Disable renderers/colliders on this marker object after a successful spawn.")]
        [SerializeField] private bool _hideMarkerAfterSpawn = true;

        public RoomSpawnCategory Category => _category;
        public float SpawnChance => _spawnChance;
        public bool HasPrefabs => _prefabOptions.Length > 0;
        public string GroupId => _groupId;

        /// <summary>
        /// Spawns at all eligible markers under the room. Returns spawned instance count.
        /// </summary>
        public static int SpawnForRoom(RoomInstance room, System.Random rng)
        {
            if (room == null || rng == null)
                return 0;

            RoomSpawnPoint[] markers = room.GetComponentsInChildren<RoomSpawnPoint>(true);
            if (markers.Length == 0)
                return 0;

            var usedGroups = new HashSet<string>();
            int spawned = 0;

            foreach (RoomSpawnPoint marker in markers)
            {
                if (marker == null || !marker.isActiveAndEnabled)
                    continue;

                if (marker.TrySpawn(rng, room.transform, usedGroups))
                    spawned++;
            }

            return spawned;
        }

        /// <summary>
        /// Rolls spawn chance, picks a prefab, instantiates at marker transform.
        /// </summary>
        public bool TrySpawn(System.Random rng, Transform roomRoot, HashSet<string> usedGroups)
        {
            if (rng == null || roomRoot == null || !HasPrefabs)
                return false;

            if (!string.IsNullOrWhiteSpace(_groupId))
            {
                if (usedGroups.Contains(_groupId))
                    return false;
            }

            if (rng.NextDouble() > _spawnChance)
                return false;

            GameObject prefab = _prefabOptions[rng.Next(0, _prefabOptions.Length)];
            if (prefab == null)
                return false;

            Quaternion rotation = _alignToMarkerRotation
                ? transform.rotation
                : Quaternion.identity;

            if (_randomYaw && _category is RoomSpawnCategory.Prop or RoomSpawnCategory.Pillar)
                rotation = Quaternion.Euler(0f, rng.Next(0, 4) * 90f, 0f);

            GameObject instance = Instantiate(prefab, transform.position, rotation, roomRoot);
            instance.name = $"{prefab.name}_{_category}";

            if (!string.IsNullOrWhiteSpace(_groupId))
                usedGroups.Add(_groupId);

            if (_hideMarkerAfterSpawn)
                HideMarkerVisuals();

            return true;
        }

        private void HideMarkerVisuals()
        {
            foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
            {
                if (renderer != null)
                    renderer.enabled = false;
            }

            foreach (Collider collider in GetComponentsInChildren<Collider>(true))
            {
                if (collider != null)
                    collider.enabled = false;
            }
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            Color color = CategoryColor(_category);
            color.a = 0.35f;
            Gizmos.color = color;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawCube(Vector3.zero, Vector3.one * 0.35f);
            Gizmos.DrawWireCube(Vector3.zero, Vector3.one * 0.35f);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = CategoryColor(_category);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireSphere(Vector3.zero, 0.25f);

            if (_prefabOptions.Length > 0 && _prefabOptions[0] != null)
            {
                Gizmos.color = new Color(1f, 1f, 1f, 0.2f);
                Gizmos.DrawLine(transform.position, transform.position + transform.forward * 0.6f);
            }
        }

        private static Color CategoryColor(RoomSpawnCategory category)
        {
            return category switch
            {
                RoomSpawnCategory.Prop => new Color(0.3f, 0.85f, 0.45f),
                RoomSpawnCategory.Pillar => new Color(0.55f, 0.55f, 0.55f),
                RoomSpawnCategory.CeilingLight => new Color(1f, 0.92f, 0.35f),
                RoomSpawnCategory.WallDecor => new Color(0.45f, 0.65f, 1f),
                _ => Color.white
            };
        }
#endif
    }
}
