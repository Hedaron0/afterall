using System.Collections.Generic;
using AfterAll.Items;
using AfterAll.Items.Loot;
using UnityEngine;

namespace AfterAll.Run
{
    /// <summary>
    /// S3 ambient deposit zone (2026-07-20 — replaces the earlier interact-to-deposit
    /// ElevatorStash): any Loot WorldItem physically resting inside this trigger counts toward
    /// the bank total automatically. Walk in, drop it anywhere inside (Harun sizes the collider
    /// to cover the elevator's interior floor), it's counted; carry it back out and it isn't.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class ElevatorStashVolume : MonoBehaviour
    {
        private readonly HashSet<WorldItem> _contained = new();

        public int CurrentValue { get; private set; }

        private void Awake()
        {
            GetComponent<Collider>().isTrigger = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            WorldItem item = other.GetComponentInParent<WorldItem>();
            if (item != null && IsLoot(item) && _contained.Add(item))
                Recalculate();
        }

        private void OnTriggerExit(Collider other)
        {
            WorldItem item = other.GetComponentInParent<WorldItem>();
            if (item != null && _contained.Remove(item))
                Recalculate();
        }

        /// <summary>Drains the counted value for banking. Does not touch the physical objects — call from RunDirector.GoUp() right before the floor/run resets them.</summary>
        public int Collect()
        {
            int total = CurrentValue;
            _contained.Clear();
            CurrentValue = 0;
            return total;
        }

        /// <summary>Loses the running count without banking it. Call on player death.</summary>
        public void ClearOnDeath()
        {
            _contained.Clear();
            CurrentValue = 0;
        }

        private void Recalculate()
        {
            int total = 0;
            foreach (WorldItem item in _contained)
            {
                if (item != null && item.Item != null && EchoDefinition.TryGetFor(item.Item, out EchoDefinition def))
                    total += def.Value;
            }

            CurrentValue = total;
        }

        private static bool IsLoot(WorldItem item) =>
            item.Item != null && item.Item.Category == ItemCategory.Loot;
    }
}
