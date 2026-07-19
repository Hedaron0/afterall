using System;
using System.Collections.Generic;
using AfterAll.Items;
using UnityEngine;

namespace AfterAll.Items.Loot
{
    /// <summary>
    /// M2a placeholder carry receiver for Loot-category pickups (Echoes) — unlimited capacity,
    /// no UI. S3 replaces this with the real pockets/elevator-stash IItemReceiver + capacity rules.
    /// </summary>
    public class EchoPocket : MonoBehaviour, IItemReceiver
    {
        private readonly List<ItemDefinition> _carried = new();

        public IReadOnlyList<ItemDefinition> Carried => _carried;

        public event Action<ItemDefinition, int> ItemReceived;

        public bool CanReceive(ItemDefinition item) =>
            item != null && item.Category == ItemCategory.Loot;

        public bool TryReceive(ItemDefinition item, int amount = 1)
        {
            if (amount < 1 || !CanReceive(item))
                return false;

            for (int i = 0; i < amount; i++)
                _carried.Add(item);

            ItemReceived?.Invoke(item, amount);
            return true;
        }
    }
}
