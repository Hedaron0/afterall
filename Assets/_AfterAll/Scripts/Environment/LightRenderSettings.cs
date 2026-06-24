using UnityEngine;

namespace AfterAll.Environment
{
    [CreateAssetMenu(
        fileName = "LightRenderSettings",
        menuName = "AfterAll/Environment/Light Render Settings")]
    public class LightRenderSettings : ScriptableObject
    {
        [Header("Player bubble")]
        [Tooltip("Panels inside this radius around the player always compete for the bubble light budget.")]
        [Min(1f)] public float bubbleRadius = 18f;

        [Tooltip("Inner radius — closest panels in the bubble can flicker.")]
        [Min(1f)] public float bubbleFlickerRadius = 8f;

        [Tooltip("Max panels with dynamic lights (Point, or Spot+Point) in the bubble.")]
        [Min(1)] public int maxBubblePanels = 16;

        [Header("Spot lights")]
        [Tooltip("Panels within this distance from the player get Spot + Point. Beyond = Point only. Applies to bubble and view-ray lights.")]
        [Min(1f)] public float spotLightMaxDistance = 30f;

        [Header("View and rays")]
        [Tooltip("Half-angle of the POV cone (degrees). Rays spread across 2× this angle.")]
        [Range(15f, 75f)] public float viewHalfAngle = 45f;

        [Tooltip("Number of rays cast from the camera through the POV cone.")]
        [Min(3)] public int rayCount = 12;

        [Tooltip("Maximum length of each visibility ray.")]
        [Min(10f)] public float rayMaxDistance = 55f;

        [Tooltip("Wall colliders block rays and panel line-of-sight. Use Wall layer only (layer 6).")]
        public LayerMask wallOcclusionMask = 1 << 6;

        [Header("Wall occlusion")]
        [Tooltip("Require clear line-of-sight before assigning dynamic lights outside the bubble.")]
        public bool requireLineOfSight = true;

        [Tooltip("Eye height added to the player position for bubble occlusion checks (metres above feet).")]
        [Min(0.5f)] public float lineOfSightEyeHeight = 1.6f;

        [Tooltip("Ray stops this far before the panel so thin fixture geometry does not false-block.")]
        [Min(0.05f)] public float occlusionEndSlack = 0.35f;

        [Tooltip("Horizontal wall check at this height — blocks lights in the next room bleeding at the floor/wall seam.")]
        [Min(0.1f)] public float floorOcclusionHeight = 0.5f;

        [Header("Anchors")]
        [Tooltip("First anchor only spawns when the sample point is at least this far from the player.")]
        [Min(1f)] public float anchorStartDistance = 18f;

        [Tooltip("Distance between anchors along an open ray (~one ceiling troffer).")]
        [Min(2f)] public float anchorSpacing = 4f;

        [Tooltip("Panels within this radius of an anchor can receive corridor lights.")]
        [Min(1f)] public float anchorInfluenceRadius = 2.5f;

        [Tooltip("Max dynamic-light panels per anchor.")]
        [Min(1)] public int maxPanelsPerAnchor = 2;

        [Tooltip("Total dynamic-light panels from view rays across all anchors.")]
        [Min(1)] public int maxCorridorPanels = 10;

        [Header("Behind you")]
        [Tooltip("Panels behind the camera beyond this distance get no dynamic lights.")]
        [Min(0f)] public float rearCutoffDistance = 3f;

        [Tooltip("Show dim ceiling emission on panels far behind the camera. Off recommended — avoids glow through walls.")]
        public bool allowRearEmission = false;

        [Range(0.1f, 0.6f)] public float rearEmissionQuality = 0.3f;

        [Header("Distance quality")]
        [Tooltip("Corridor lights beyond this distance from the player use minFarQuality.")]
        [Min(1f)] public float corridorQualityFadeDistance = 40f;

        [Tooltip("Minimum brightness scale at rayMaxDistance.")]
        [Range(0.2f, 1f)] public float minFarQuality = 0.45f;

        [Header("Debug gizmos")]
        public bool gizmoShowBubble = true;
        public bool gizmoShowSpotRadius = true;
        public bool gizmoShowViewCone = true;
        public bool gizmoShowRays = true;
        public bool gizmoShowAnchors = true;
        public bool gizmoShowAnchorRadii = true;
        public bool gizmoShowPanelStates = true;
        public bool gizmoShowLabels = false;

        [Header("Performance")]
        [Min(2f)] public float spatialCellSize = 6f;

        [Min(0.05f)] public float tickInterval = 0.15f;

        private void OnValidate()
        {
            if (bubbleFlickerRadius > bubbleRadius)
                bubbleFlickerRadius = bubbleRadius;

            if (anchorStartDistance < bubbleRadius)
                anchorStartDistance = bubbleRadius;

            if (corridorQualityFadeDistance < spotLightMaxDistance)
                corridorQualityFadeDistance = spotLightMaxDistance + 5f;

            if (wallOcclusionMask.value == -1)
                wallOcclusionMask = 1 << 6;
        }
    }
}
