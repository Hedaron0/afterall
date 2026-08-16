using UnityEngine;

namespace AfterAll.Environment
{
    /// <summary>
    /// Optional override for how much loot one room prefab gets. Add it only to rooms whose default
    /// is wrong — a room with no budget component uses the automatic count and needs no authoring.
    ///
    /// The automatic count is derived from how many <see cref="LootSpawnPoint"/>s the room actually
    /// has, not from its floor area. Point count is already the author's own statement of how big and
    /// how furnished a room is: a hall gets thirty markers, a closet gets two. Area would have to be
    /// re-tuned every time a room's interior changed, and would say a mostly-empty 5000m² hall should
    /// be as full of loot as a dense one.
    /// </summary>
    [DisallowMultipleComponent]
    public class RoomLootBudget : MonoBehaviour
    {
        [Tooltip("Fewest items this room may spawn, before the depth bonus.")]
        [SerializeField, Min(0)] private int _minLoot = 1;

        [Tooltip("Most items this room may spawn, before the depth bonus. Still capped by how many " +
                 "spawn points the active preset actually has.")]
        [SerializeField, Min(0)] private int _maxLoot = 3;

        public int MinLoot => Mathf.Min(_minLoot, _maxLoot);

        public int MaxLoot => Mathf.Max(_minLoot, _maxLoot);
    }
}
