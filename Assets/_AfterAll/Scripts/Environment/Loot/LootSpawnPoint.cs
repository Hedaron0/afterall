using UnityEngine;

namespace AfterAll.Environment
{
    /// <summary>
    /// Marks a place where loot may appear. Authored as an empty GameObject anywhere inside a room
    /// prefab — on a desk, inside a drawer, on the floor.
    ///
    /// A point does not decide WHETHER it is used; <see cref="RoomLootPlacer"/> picks a few points
    /// per room and leaves the rest empty, so the same room lays its loot out differently each run.
    ///
    /// Which points exist at all comes free from the preset system: a point authored under
    /// Content/Preset/2 is only active when WeightedRandomGroup picks preset 2, and the placer only
    /// ever collects active points. Points placed above the preset group apply to every preset.
    /// </summary>
    [DisallowMultipleComponent]
    public class LootSpawnPoint : MonoBehaviour
    {
        [Tooltip("Relative chance of this point being chosen over the others in the same room. " +
                 "1 is normal; raise it for a spot loot should favour, lower it for a long shot.")]
        [SerializeField, Min(0f)] private float _weight = 1f;

        [Tooltip("Drop the item onto whatever surface is under the marker instead of spawning at the " +
                 "marker itself. Leave on: it means the marker only has to be roughly right, and an " +
                 "item authored a little above a desk still lands ON the desk.")]
        [SerializeField] private bool _snapToSurface = true;

        [Tooltip("How far below the marker to look for that surface. Nothing found within this range " +
                 "and the item spawns at the marker.")]
        [SerializeField, Min(0f)] private float _snapDistanceM = 1.5f;

        [Tooltip("Clearance left above the surface. Items carry a Rigidbody, so a small gap lets them " +
                 "drop the last centimetre and settle at a natural angle rather than looking placed.")]
        [SerializeField, Min(0f)] private float _dropHeightM = 0.04f;

        public float Weight => _weight;

        /// <summary>
        /// Where an item spawned here should actually appear.
        ///
        /// Casts down from a little above the marker so a marker sitting slightly inside a desk still
        /// finds the desk's top face rather than starting below it. Hits on the room's own trigger
        /// volumes are ignored — those are gameplay zones, not surfaces.
        /// </summary>
        public Vector3 ResolveSpawnPosition()
        {
            Vector3 origin = transform.position;
            if (!_snapToSurface || _snapDistanceM <= 0f)
                return origin + Vector3.up * _dropHeightM;

            const float startAboveM = 0.25f;
            if (Physics.Raycast(
                    origin + Vector3.up * startAboveM,
                    Vector3.down,
                    out RaycastHit hit,
                    _snapDistanceM + startAboveM,
                    ~0,
                    QueryTriggerInteraction.Ignore))
                return hit.point + Vector3.up * _dropHeightM;

            return origin + Vector3.up * _dropHeightM;
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            // Drawn unselected and deliberately loud: these are invisible at runtime and there will be
            // dozens per room, so the whole point is seeing the layout at a glance while authoring.
            Gizmos.color = new Color(1f, 0.82f, 0.2f, 0.9f);
            Gizmos.DrawWireSphere(transform.position, 0.12f);

            Vector3 resolved = ResolveSpawnPosition();
            Gizmos.color = new Color(1f, 0.82f, 0.2f, 0.35f);
            Gizmos.DrawLine(transform.position, resolved);
            Gizmos.DrawWireCube(resolved, new Vector3(0.2f, 0.01f, 0.2f));
        }
#endif
    }
}
