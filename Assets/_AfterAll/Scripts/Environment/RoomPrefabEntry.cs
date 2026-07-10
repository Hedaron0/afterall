using System;
using UnityEngine;

namespace AfterAll.Environment
{
    [Serializable]
    public class RoomPrefabEntry
    {
        [SerializeField] private GameObject _prefab;
        [SerializeField, Min(1)] private int _weight = 10;

        public GameObject Prefab => _prefab;
        public int Weight => _weight;

        public bool IsValid => _prefab != null && _weight > 0;

        public RoomPrefabEntry()
        {
        }

        public RoomPrefabEntry(GameObject prefab, int weight)
        {
            _prefab = prefab;
            _weight = Mathf.Max(1, weight);
        }

        public void SetWeight(int weight) => _weight = Mathf.Max(1, weight);
    }
}
