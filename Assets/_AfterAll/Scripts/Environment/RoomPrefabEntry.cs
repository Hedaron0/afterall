using System;
using UnityEngine;

namespace AfterAll.Environment
{
    [Serializable]
    public class RoomPrefabEntry
    {
        [SerializeField] private GameObject _prefab;

        public GameObject Prefab => _prefab;
        public bool IsValid => _prefab != null;

        public RoomPrefabEntry()
        {
        }

        public RoomPrefabEntry(GameObject prefab)
        {
            _prefab = prefab;
        }
    }
}
