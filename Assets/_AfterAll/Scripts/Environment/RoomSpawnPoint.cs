using System.Collections.Generic;
using System.Text.RegularExpressions;
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
    /// Marker placed on empty GameObjects inside room prefabs.
    /// RoomPoolSpawner calls <see cref="SpawnForRoom"/> after generation + reachability pass.
    /// See vault: AfterAll — Room Content Spawn Architecture Plan.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("AfterAll/Environment/Room Spawn Point")]
    public class RoomSpawnPoint : MonoBehaviour
    {
        private struct SpawnCandidate
        {
            public RoomSpawnPoint marker;
            public GameObject prefab;
            public Quaternion rotation;
            public RoomSpawnCategory category;
        }

        [SerializeField] private RoomSpawnCategory _category = RoomSpawnCategory.Prop;
        [SerializeField] private GameObject[] _prefabOptions = System.Array.Empty<GameObject>();
        [SerializeField, Range(0f, 1f)] private float _spawnChance = 0.5f;
        [SerializeField] private bool _useRandomYaw = true;
        [SerializeField] private bool _alignToMarkerRotation = true;
        [Tooltip("Optional. Only one marker with the same non-empty group id will spawn per room.")]
        [SerializeField] private string _groupId;
        [Tooltip("WallDecor: explicit wall index. -1 = resolve from parent WallGapController or Wall0X name.")]
        [SerializeField] private int _wallIndex = -1;
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

            RoomContentProfile profile = room.ContentProfile;
            var usedGroups = new HashSet<string>();
            var propCandidates = new List<SpawnCandidate>();
            var pillarCandidates = new List<SpawnCandidate>();
            var lightCandidates = new List<SpawnCandidate>();
            var wallDecorCandidates = new List<SpawnCandidate>();

            foreach (RoomSpawnPoint marker in markers)
            {
                if (marker == null || !marker.isActiveAndEnabled || !marker.HasPrefabs)
                    continue;

                if (!string.IsNullOrWhiteSpace(marker._groupId) && usedGroups.Contains(marker._groupId))
                    continue;

                double chanceRoll = rng.NextDouble();

                if (marker._category == RoomSpawnCategory.WallDecor)
                {
                    int wallIndex = marker.ResolveWallIndex(room);
                    if (wallIndex >= 0 && room.IsWallOpen(wallIndex))
                        continue;
                }

                float effectiveChance = marker.GetEffectiveSpawnChance(profile);
                if (chanceRoll > effectiveChance)
                    continue;

                GameObject prefab = marker._prefabOptions[rng.Next(0, marker._prefabOptions.Length)];
                if (prefab == null)
                    continue;

                SpawnCandidate candidate = new()
                {
                    marker = marker,
                    prefab = prefab,
                    rotation = marker.BuildRotation(rng),
                    category = marker._category
                };

                switch (marker._category)
                {
                    case RoomSpawnCategory.Prop:
                        propCandidates.Add(candidate);
                        break;
                    case RoomSpawnCategory.Pillar:
                        pillarCandidates.Add(candidate);
                        break;
                    case RoomSpawnCategory.CeilingLight:
                        lightCandidates.Add(candidate);
                        break;
                    case RoomSpawnCategory.WallDecor:
                        wallDecorCandidates.Add(candidate);
                        break;
                }
            }

            TrimCandidates(propCandidates, profile?.MaxProps ?? -1, profile?.HasPropCap == true, room.name, RoomSpawnCategory.Prop);
            TrimCandidates(pillarCandidates, profile?.MaxPillars ?? -1, profile?.HasPillarCap == true, room.name, RoomSpawnCategory.Pillar);
            TrimCandidates(lightCandidates, profile?.MaxCeilingLights ?? -1, profile?.HasCeilingLightCap == true, room.name, RoomSpawnCategory.CeilingLight);

            int spawned = 0;
            spawned += CommitCandidates(propCandidates, room.transform, usedGroups);
            spawned += CommitCandidates(pillarCandidates, room.transform, usedGroups);
            spawned += CommitCandidates(lightCandidates, room.transform, usedGroups);
            spawned += CommitCandidates(wallDecorCandidates, room.transform, usedGroups);

            return spawned;
        }

        private static void TrimCandidates(
            List<SpawnCandidate> candidates,
            int maxCount,
            bool hasCap,
            string roomName,
            RoomSpawnCategory category)
        {
            if (!hasCap || candidates.Count <= maxCount)
                return;

            int removed = candidates.Count - maxCount;
            candidates.RemoveRange(maxCount, removed);
            Debug.LogWarning(
                $"[RoomSpawnPoint] {roomName}: trimmed {removed} {category} marker(s) to cap {maxCount}. " +
                "Add more markers or raise RoomContentProfile cap if this room looks sparse.");
        }

        private static int CommitCandidates(
            List<SpawnCandidate> candidates,
            Transform roomRoot,
            HashSet<string> usedGroups)
        {
            int spawned = 0;
            foreach (SpawnCandidate candidate in candidates)
            {
                if (candidate.marker == null || candidate.prefab == null)
                    continue;

                if (candidate.marker.CommitSpawn(candidate.prefab, candidate.rotation, roomRoot, usedGroups))
                    spawned++;
            }

            return spawned;
        }

        private float GetEffectiveSpawnChance(RoomContentProfile profile)
        {
            if (_category == RoomSpawnCategory.Pillar &&
                profile != null &&
                profile.HasPillarChanceOverride)
            {
                return profile.PillarSpawnChanceOverride;
            }

            return _spawnChance;
        }

        private Quaternion BuildRotation(System.Random rng)
        {
            if (_useRandomYaw && _category is RoomSpawnCategory.Prop or RoomSpawnCategory.Pillar)
                return Quaternion.Euler(0f, rng.Next(0, 4) * 90f, 0f);

            return _alignToMarkerRotation ? transform.rotation : Quaternion.identity;
        }

        private int ResolveWallIndex(RoomInstance room)
        {
            if (_wallIndex >= 0)
                return _wallIndex;

            WallGapController wall = GetComponentInParent<WallGapController>();
            if (wall != null && wall.TryGetBakedSocket(out RoomSocket socket))
                return socket.WallIndex;

            return TryParseWallIndexFromName(transform.parent != null ? transform.parent.name : name);
        }

        private static int TryParseWallIndexFromName(string objectName)
        {
            if (string.IsNullOrWhiteSpace(objectName))
                return -1;

            Match match = Regex.Match(objectName, @"Wall(\d+)", RegexOptions.IgnoreCase);
            if (!match.Success || !int.TryParse(match.Groups[1].Value, out int wallNumber))
                return -1;

            return wallNumber - 1;
        }

        private bool CommitSpawn(GameObject prefab, Quaternion rotation, Transform roomRoot, HashSet<string> usedGroups)
        {
            if (!string.IsNullOrWhiteSpace(_groupId) && usedGroups.Contains(_groupId))
                return false;

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
