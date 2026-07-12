using System;
using UnityEngine;

namespace AfterAll.Environment
{
    [Serializable]
    public class RoomPrefabEntry
    {
        [SerializeField] private GameObject _prefab;
        [SerializeField] private RoomRole _role = RoomRole.Auto;

        public GameObject Prefab => _prefab;
        public RoomRole Role => _role;
        /// <summary>Auto resolves to Room when no footprint bounds are available.</summary>
        public RoomRole ResolvedRole => _role == RoomRole.Auto ? RoomRole.Room : _role;

        public bool IsValid => _prefab != null;

        public RoomPrefabEntry()
        {
        }

        public RoomPrefabEntry(GameObject prefab, RoomRole role = RoomRole.Auto)
        {
            _prefab = prefab;
            _role = role;
        }

        public void SetRole(RoomRole role) => _role = role;

        public void SetResolvedRoleFromFootprint(RoomRole resolvedRole) =>
            _role = resolvedRole == RoomRole.Auto ? RoomRole.Room : resolvedRole;
    }
}
