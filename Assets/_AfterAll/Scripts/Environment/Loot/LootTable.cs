using System;
using System.Collections.Generic;
using AfterAll.Items;
using UnityEngine;

namespace AfterAll.Environment
{
    /// <summary>
    /// What can spawn, and how often — the other half of <see cref="LootSpawnPoint"/>, which only says
    /// where.
    ///
    /// Splitting the two is the point of the system that replaced the old Random pool. That pool held
    /// pre-placed loot INSTANCES inside each room prefab, so what a room could contain was welded into
    /// its hierarchy: adding an item meant editing every room by hand, and rooms drifted badly (room7
    /// carried 11 candidates, room5 none). Here a room says where loot fits and the table says what
    /// loot is, so a new item is one entry in one asset.
    /// </summary>
    [CreateAssetMenu(fileName = "LootTable", menuName = "AfterAll/Loot Table")]
    public class LootTable : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            public ItemDefinition item;

            [Tooltip("Relative chance against the other entries. 0 disables the entry without " +
                     "deleting it, which is handy while tuning.")]
            [Min(0f)] public float weight;
        }

        [SerializeField] private List<Entry> _entries = new List<Entry>();

        public IReadOnlyList<Entry> Entries => _entries;

        /// <summary>Weighted pick, or null when the table is empty or every entry is disabled.</summary>
        public ItemDefinition Pick(System.Random rng)
        {
            float total = 0f;
            foreach (Entry entry in _entries)
            {
                if (entry.item != null && entry.weight > 0f)
                    total += entry.weight;
            }

            if (total <= 0f)
                return null;

            double roll = rng.NextDouble() * total;
            double cumulative = 0d;

            foreach (Entry entry in _entries)
            {
                if (entry.item == null || entry.weight <= 0f)
                    continue;

                cumulative += entry.weight;
                if (roll <= cumulative)
                    return entry.item;
            }

            // Floating-point drift on the last step only.
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                if (_entries[i].item != null && _entries[i].weight > 0f)
                    return _entries[i].item;
            }

            return null;
        }
    }
}
