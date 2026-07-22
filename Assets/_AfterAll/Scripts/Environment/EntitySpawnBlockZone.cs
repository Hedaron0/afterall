using UnityEngine;

namespace AfterAll.Environment
{
    /// <summary>
    /// Marks a sealed interior volume (a compartment closed off on every side) that entities must
    /// never be placed or targeted inside — e.g. a decorative nook Harun walled shut with no
    /// doorway. Drop one into the room prefab covering that volume; EntityDirector spawn picking
    /// and HunterEntity patrol-target picking both skip any candidate point inside one.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class EntitySpawnBlockZone : MonoBehaviour
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

        public bool ContainsPoint(Vector3 worldPos)
        {
            if (_collider == null)
                _collider = GetComponent<BoxCollider>();
            if (_collider == null)
                return false;

            Vector3 local = transform.InverseTransformPoint(worldPos) - _collider.center;
            Vector3 half = _collider.size * 0.5f;
            return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (_collider == null)
                _collider = GetComponent<BoxCollider>();
            if (_collider == null)
                return;

            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(0.9f, 0.1f, 0.1f, 0.35f);
            Gizmos.DrawCube(_collider.center, _collider.size);
            Gizmos.color = new Color(0.9f, 0.1f, 0.1f, 0.9f);
            Gizmos.DrawWireCube(_collider.center, _collider.size);
        }
#endif
    }
}
