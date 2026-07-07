using UnityEngine;

namespace AfterAll.Environment
{
    /// <summary>
    /// Marks volume on large props (half walls, pillars) that must not overlap an open wall corridor.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class RoomOpeningBlockZone : MonoBehaviour
    {
        [SerializeField] private BoxCollider _collider;

        private void Reset()
        {
            _collider = GetComponent<BoxCollider>();
            if (_collider != null)
                _collider.isTrigger = true;
        }

        private void Awake()
        {
            if (_collider == null)
                _collider = GetComponent<BoxCollider>();

            if (_collider != null)
                _collider.isTrigger = true;
        }

        public Bounds GetWorldBounds()
        {
            if (_collider == null)
                _collider = GetComponent<BoxCollider>();

            if (_collider == null)
                return new Bounds(transform.position, Vector3.one);

            Vector3 center = _collider.center;
            Vector3 half = _collider.size * 0.5f;
            Bounds bounds = new Bounds(transform.TransformPoint(center), Vector3.zero);

            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
                bounds.Encapsulate(transform.TransformPoint(center + Vector3.Scale(half, new Vector3(x, y, z))));

            return bounds;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Bounds bounds = GetWorldBounds();
            Gizmos.color = new Color(1f, 0.45f, 0.1f, 0.35f);
            Gizmos.DrawCube(bounds.center, bounds.size);
            Gizmos.color = new Color(1f, 0.45f, 0.1f, 0.9f);
            Gizmos.DrawWireCube(bounds.center, bounds.size);
        }
#endif
    }
}
