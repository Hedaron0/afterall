using System;
using UnityEngine;

namespace AfterAll.Environment
{
    [Serializable]
    public class RoomPrefabEntry
    {
        [SerializeField] private GameObject _prefab;
        [SerializeField, Min(1)] private int _weight = 10;
        [SerializeField] private RoomRole _role = RoomRole.Auto;

        public GameObject Prefab => _prefab;
        public int Weight => _weight;
        public RoomRole Role => _role;
        /// <summary>Auto resolves to Room when no footprint bounds are available.</summary>
        public RoomRole ResolvedRole => _role == RoomRole.Auto ? RoomRole.Room : _role;

        public bool IsValid => _prefab != null && _weight > 0;

        public RoomPrefabEntry()
        {
        }

        public RoomPrefabEntry(GameObject prefab, int weight, RoomRole role = RoomRole.Auto)
        {
            _prefab = prefab;
            _weight = Mathf.Max(1, weight);
            _role = role;
        }

        public void SetWeight(int weight) => _weight = Mathf.Max(1, weight);

        public void SetRole(RoomRole role) => _role = role;

        public void SetResolvedRoleFromFootprint(RoomRole resolvedRole) =>
            _role = resolvedRole == RoomRole.Auto ? RoomRole.Room : resolvedRole;
    }
}
