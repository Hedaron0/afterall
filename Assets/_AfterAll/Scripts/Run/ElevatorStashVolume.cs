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
    /// while carried, so it's counted exactly when its own collider is inside the cabin (holding
    /// it out through the doorway doesn't count it; pushing it fully inside does). Don't also add
    /// BulkyCarrier.PeekValue() anywhere — that would double-count it.
    /// CurrentValue/PlayerInside are recomputed from a fresh Physics.OverlapBox sweep every
    /// frame rather than tracked incrementally via OnTriggerEnter/Exit — repeated grab/throw
    /// cycles on the same item desynced the incremental HashSet (2026-07-21: exactly that caused
    /// a value-doubling exploit). A same-frame ground-truth sweep can't drift.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class ElevatorStashVolume : MonoBehaviour
    {
        private Collider _volume;
        private readonly Collider[] _overlapBuffer = new Collider[64];

        public int CurrentValue { get; private set; }

        /// <summary>True while the player's own collider is inside the cabin — gates counting
        /// abstractly-carried (EchoPocket) value, which has no physical presence to sweep.</summary>
        public bool PlayerInside { get; private set; }

        private void Awake()
        {
            _volume = GetComponent<Collider>();
            _volume.isTrigger = true;
        }

        private void Update() => Recalculate();

        /// <summary>
        /// Extract, interim economy (no shop yet — Core Design §5.6/§7): totals AND physically
        /// destroys every Loot WorldItem currently inside the cabin. Once the Uncanny Shop exists
        /// this becomes "move to shop inventory" instead of destroy.
        /// </summary>
        public int CollectAndDestroyAll()
        {
            int total = 0;
            var seen = new HashSet<WorldItem>();
            foreach (Collider hit in Overlap())
            {
                WorldItem item = hit.GetComponentInParent<WorldItem>();
                if (item == null || !IsLoot(item) || !seen.Add(item))
                    continue;

                if (EchoDefinition.TryGetFor(item.Item, out EchoDefinition def))
                    total += def.Value;

                Destroy(item.gameObject);
            }

            CurrentValue = 0;
            return total;
        }

        /// <summary>Loses the running count without banking it. Call on player death.</summary>
        public void ClearOnDeath() => CurrentValue = 0;

        private void Recalculate()
        {
            int total = 0;
            bool playerInside = false;
            var seen = new HashSet<WorldItem>();

            foreach (Collider hit in Overlap())
            {
                if (!playerInside && hit.GetComponentInParent<PlayerMovement>() != null)
                {
                    playerInside = true;
                    continue;
                }

                WorldItem item = hit.GetComponentInParent<WorldItem>();
                if (item == null || !IsLoot(item) || !seen.Add(item))
                    continue;

                if (EchoDefinition.TryGetFor(item.Item, out EchoDefinition def))
                    total += def.Value;
            }

            CurrentValue = total;
            PlayerInside = playerInside;
        }

        private IEnumerable<Collider> Overlap()
        {
            Bounds bounds = _volume.bounds;
            int count = Physics.OverlapBoxNonAlloc(
                bounds.center, bounds.extents, _overlapBuffer, Quaternion.identity,
                ~0, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
                yield return _overlapBuffer[i];
        }

        private static bool IsLoot(WorldItem item) =>
            item.Item != null && item.Item.Category == ItemCategory.Loot;
    }
}
