using UnityEngine;

namespace AfterAll.Environment
{
    /// <summary>
    /// Convenience wrapper for rectangular rooms with four cardinal walls.
    /// </summary>
    [ExecuteAlways]
    public class RoomWallOpenings : MonoBehaviour
    {
        [SerializeField] private SplitWallOpening _north;
        [SerializeField] private SplitWallOpening _south;
        [SerializeField] private SplitWallOpening _east;
        [SerializeField] private SplitWallOpening _west;

        private void OnValidate()
        {
            ApplyAll();
        }

        [ContextMenu("Apply All Walls")]
        public void ApplyAll()
        {
            Apply(_north);
            Apply(_south);
            Apply(_east);
            Apply(_west);
        }

        private static void Apply(SplitWallOpening wall)
        {
            if (wall != null)
                wall.ApplyLayout();
        }
    }
}
