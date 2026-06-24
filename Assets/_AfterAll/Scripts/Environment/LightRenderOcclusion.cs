using UnityEngine;

namespace AfterAll.Environment
{
    /// <summary>
    /// Line-of-sight checks for ceiling panels. Uses wall layer only so floor/ceiling
    /// slabs (layer 0) do not block; stacked walls stop the first hit.
    /// </summary>
    public static class LightRenderOcclusion
    {
        public static Vector3 EyePosition(Vector3 basePosition, float eyeHeight) =>
            new Vector3(basePosition.x, basePosition.y + eyeHeight, basePosition.z);

        /// <summary>
        /// True when no wall collider blocks the path from observer to panel.
        /// Includes a floor-height sample to stop point-light bleed at wall bases.
        /// </summary>
        public static bool HasClearLineOfSight(
            Vector3 observer,
            Vector3 panelPosition,
            LayerMask wallMask,
            float endSlack = 0.35f,
            float floorHeight = 0.5f)
        {
            if (wallMask.value == 0)
                return true;

            if (!SegmentClear(observer, panelPosition, wallMask, endSlack))
                return false;

            float eyeY = observer.y;
            float panelY = panelPosition.y;

            if (!HorizontalSegmentClear(observer, panelPosition, eyeY, wallMask, endSlack))
                return false;

            if (!Mathf.Approximately(eyeY, panelY)
                && !HorizontalSegmentClear(observer, panelPosition, panelY, wallMask, endSlack))
                return false;

            if (!HorizontalSegmentClear(observer, panelPosition, floorHeight, wallMask, endSlack))
                return false;

            return true;
        }

        public static bool SegmentClear(
            Vector3 from,
            Vector3 to,
            LayerMask wallMask,
            float endSlack)
        {
            if (wallMask.value == 0)
                return true;

            Vector3 delta = to - from;
            float dist = delta.magnitude;
            if (dist < 0.05f)
                return true;

            Vector3 dir = delta / dist;
            float maxCheck = Mathf.Max(0.05f, dist - endSlack);

            return !Physics.Raycast(
                from,
                dir,
                maxCheck,
                wallMask,
                QueryTriggerInteraction.Ignore);
        }

        static bool HorizontalSegmentClear(
            Vector3 observer,
            Vector3 panel,
            float height,
            LayerMask wallMask,
            float endSlack)
        {
            Vector3 from = new Vector3(observer.x, height, observer.z);
            Vector3 to = new Vector3(panel.x, height, panel.z);
            return SegmentClear(from, to, wallMask, endSlack);
        }
    }
}
