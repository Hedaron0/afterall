using UnityEngine;

namespace AfterAll.Environment
{
    /// <summary>
    /// Marker left on a room's generated CombinedStatic child. Holds the renderers that were
    /// disabled when the shell was combined, so the operation stays fully reversible from the
    /// editor tool (see RoomStaticMeshCombiner). Pure data — no runtime behaviour.
    /// </summary>
    public class CombinedStaticGroup : MonoBehaviour
    {
        [SerializeField] private MeshRenderer[] _sourceRenderers = new MeshRenderer[0];

        public MeshRenderer[] SourceRenderers
        {
            get => _sourceRenderers;
            set => _sourceRenderers = value;
        }
    }
}
