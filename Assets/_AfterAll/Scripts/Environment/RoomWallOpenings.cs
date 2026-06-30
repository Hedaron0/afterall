using UnityEngine;

namespace AfterAll.Environment
{
    /// <summary>
    /// Applies split-wall openings on all four walls of a hand-crafted room prefab.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class RoomWallOpenings : MonoBehaviour
    {
        [SerializeField] private SplitWallOpening _north;
        [SerializeField] private SplitWallOpening _south;
        [SerializeField] private SplitWallOpening _east;
        [SerializeField] private SplitWallOpening _west;

        public SplitWallOpening North => _north;
        public SplitWallOpening South => _south;
        public SplitWallOpening East => _east;
        public SplitWallOpening West => _west;

        [ContextMenu("Apply All Openings")]
        public void ApplyAll()
        {
            ApplyIfPresent(_north);
            ApplyIfPresent(_south);
            ApplyIfPresent(_east);
            ApplyIfPresent(_west);
        }

        /// <summary>
        /// Configures the built-in visual test matrix from the session plan.
        /// </summary>
        [ContextMenu("Apply Test Matrix")]
        public void ApplyTestMatrix()
        {
            ConfigureWall(_north, hasOpening: true, doorOffset: 0f, placeFrame: false);
            ConfigureWall(_south, hasOpening: true, doorOffset: -1f, placeFrame: false);
            ConfigureWall(_east, hasOpening: true, doorOffset: -2f, placeFrame: true);
            ConfigureWall(_west, hasOpening: false, doorOffset: 0f, placeFrame: false);
            ApplyAll();
        }

        private void ConfigureWall(SplitWallOpening wall, bool hasOpening, float doorOffset, bool placeFrame)
        {
            if (wall == null)
                return;

            var length = wall.WallLength;
            var resolvedOffset = doorOffset switch
            {
                -1f => Mathf.Max(0f, (length - SplitWallOpening.GapWidth) * 0.5f),
                -2f => Mathf.Max(0f, length - SplitWallOpening.GapWidth),
                _ => doorOffset
            };

            wall.Configure(hasOpening, resolvedOffset, placeFrame);
        }

        private static void ApplyIfPresent(SplitWallOpening wall)
        {
            if (wall != null)
                wall.Apply();
        }

        private void OnValidate()
        {
            if (Application.isPlaying)
                return;

            ApplyAll();
        }
    }
}
