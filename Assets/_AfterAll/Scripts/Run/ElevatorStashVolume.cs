using System.Collections.Generic;
using AfterAll.Items;
using AfterAll.Items.Loot;
using AfterAll.Player;
using UnityEngine;

namespace AfterAll.Run
{
    /// <summary>
    /// S3 ambient deposit zone (2026-07-20 — replaces the earlier interact-to-deposit
    /// ElevatorStash): any Loot WorldItem physically resting inside this trigger counts toward
    /// the bank total automatically. Walk in, drop it anywhere inside (Harun sizes the collider
    /// to cover the elevator's interior floor), it's counted; carry it back out and it isn't.
    /// This naturally covers a BulkyCarrier-held item too — it stays a live Rigidbody+Collider
    /// while carried, so it enters/exits _contained exactly when its own collider crosses the
    /// boundary (holding it out through the doorway doesn't count it; pushing it fully inside
    /// does). Don't also add BulkyCarrier.PeekValue() anywhere — that would double-count it.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class ElevatorStashVolume : MonoBehaviour
    {
        private readonly HashSet<WorldItem> _contained = new();

        public int CurrentValue { get; private set; }

        /// <summary>True while the player's own collider is inside the cabin — gates counting
        /// abstractly-carried (EchoPocket) value, which has no physical presence to sweep.</summary>
        public bool PlayerInside { get; private set; }

        private void Awake()
        {
            GetComponent<Collider>().isTrigger = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (other.GetComponentInParent<PlayerMovement>() != null)
            {
                PlayerInside = true;
                return;
            }

            WorldItem item = other.GetComponentInParent<WorldItem>();
            if (item != null && IsLoot(item) && _contained.Add(item))
                Recalculate();
        }

        private void OnTriggerExit(Collider other)
        {
            if (other.GetComponentInParent<PlayerMovement>() != null)
            {
                PlayerInside = false;
                return;
            }

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

        /// <summary>
        /// Extract, interim economy (no shop yet — Core Design §5.6/§7): totals AND physically
        /// destroys every Loot WorldItem currently inside the cabin, not just the trigger-tracked
        /// set. A fresh physics sweep (not _contained) so a BulkyCarrier item released the same
        /// frame extract is pressed — before its own OnTriggerEnter has fired — is still caught.
        /// Once the Uncanny Shop exists this becomes "move to shop inventory" instead of destroy.
        /// </summary>
        public int CollectAndDestroyAll()
        {
            Collider volume = GetComponent<Collider>();
            Bounds bounds = volume.bounds;
            Collider[] hits = Physics.OverlapBox(
                bounds.center, bounds.extents, Quaternion.identity,
                ~0, QueryTriggerInteraction.Ignore);

            int total = 0;
            var destroyed = new HashSet<WorldItem>();
            foreach (Collider hit in hits)
            {
                WorldItem item = hit.GetComponentInParent<WorldItem>();
                if (item == null || !IsLoot(item) || !destroyed.Add(item))
                    continue;

                if (EchoDefinition.TryGetFor(item.Item, out EchoDefinition def))
                    total += def.Value;

                Destroy(item.gameObject);
            }

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
