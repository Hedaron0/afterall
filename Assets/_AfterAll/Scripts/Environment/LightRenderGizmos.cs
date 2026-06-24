#if UNITY_EDITOR

using UnityEditor;

#endif

using UnityEngine;



namespace AfterAll.Environment

{

    public static class LightRenderGizmos

    {

        public static void Draw(LightRenderSettings settings, LightRenderSnapshot snapshot, bool hasSnapshot)

        {

            if (settings == null)

                return;



            float y = snapshot.PlayerPosition.y;



            if (settings.gizmoShowBubble)

                DrawBubble(snapshot.PlayerPosition, settings);



            if (hasSnapshot && settings.gizmoShowViewCone)

                DrawViewCone(snapshot, settings, y);



            if (hasSnapshot && settings.gizmoShowRays)

                DrawRays(snapshot, settings);



            if (hasSnapshot && settings.gizmoShowAnchors)

                DrawAnchors(snapshot, settings);



            if (hasSnapshot && settings.gizmoShowPanelStates)

                DrawPanelStates(snapshot, settings);

        }



        static void DrawBubble(Vector3 playerPos, LightRenderSettings settings)

        {

            Gizmos.color = new Color(0.55f, 0.35f, 0.15f, 0.55f);

            DrawHorizontalCircle(playerPos, settings.bubbleRadius, 48);



            Gizmos.color = new Color(0.35f, 0.85f, 1f, 0.45f);

            DrawHorizontalCircle(playerPos, settings.bubbleFlickerRadius, 36);



            if (settings.gizmoShowSpotRadius)

            {

                Gizmos.color = new Color(0.2f, 0.95f, 0.55f, 0.35f);

                DrawHorizontalCircle(playerPos, settings.spotLightMaxDistance, 64);

            }

        }



        static void DrawViewCone(LightRenderSnapshot snapshot, LightRenderSettings settings, float y)

        {

            Vector3 origin = snapshot.CameraPosition;

            origin.y = y + 0.05f;



            Vector3 fwd = snapshot.CameraForwardFlat;

            if (fwd.sqrMagnitude < 0.0001f)

                return;



            float half = settings.viewHalfAngle;

            float len = settings.rayMaxDistance;

            Vector3 left = Quaternion.Euler(0f, -half, 0f) * fwd;

            Vector3 right = Quaternion.Euler(0f, half, 0f) * fwd;



            Gizmos.color = new Color(0.65f, 0.65f, 0.65f, 0.75f);

            Gizmos.DrawLine(origin, origin + left * len);

            Gizmos.DrawLine(origin, origin + right * len);

        }



        static void DrawRays(LightRenderSnapshot snapshot, LightRenderSettings settings)

        {

            if (!settings.gizmoShowRays)

                return;



            for (int i = 0; i < snapshot.RaySegments.Count; i++)

            {

                var seg = snapshot.RaySegments[i];

                Gizmos.color = seg.HitWall

                    ? new Color(1f, 0.3f, 0.15f, 0.9f)

                    : new Color(0.2f, 0.85f, 1f, 0.85f);

                Gizmos.DrawLine(seg.Start, seg.End);

            }

        }



        static void DrawAnchors(LightRenderSnapshot snapshot, LightRenderSettings settings)

        {

            float radius = settings.anchorInfluenceRadius;



            for (int i = 0; i < snapshot.Anchors.Count; i++)

            {

                var anchor = snapshot.Anchors[i];

                Vector3 pos = anchor.Position;



                Gizmos.color = new Color(1f, 0.92f, 0.15f, 0.95f);

                Gizmos.DrawWireSphere(pos, 0.25f);



                if (settings.gizmoShowAnchorRadii)

                {

#if UNITY_EDITOR

                    Handles.color = new Color(1f, 0.95f, 0.2f, 0.12f);

                    Handles.DrawSolidDisc(pos, Vector3.up, radius);

                    Handles.color = new Color(1f, 0.95f, 0.2f, 0.55f);

                    Handles.DrawWireDisc(pos, Vector3.up, radius);

#else

                    Gizmos.color = new Color(1f, 0.95f, 0.2f, 0.35f);

                    DrawHorizontalCircle(pos, radius, 24);

#endif

                }

            }

        }



        static void DrawPanelStates(LightRenderSnapshot snapshot, LightRenderSettings settings)

        {

            foreach (var kv in snapshot.Assignments)

            {

                var panel = kv.Key;

                if (panel == null)

                    continue;



                Vector3 pos = panel.WorldPosition + Vector3.up * 0.15f;

                float size = 0.18f;

                var assignment = kv.Value;



                if (assignment.Tier == FluorescentLightTier.Full)

                {

                    Gizmos.color = assignment.IsViewRay

                        ? new Color(0.15f, 0.95f, 0.75f, 0.95f)

                        : new Color(0.2f, 1f, 0.35f, 0.95f);

                }

                else switch (assignment.Tier)

                {

                    case FluorescentLightTier.CorridorPartial:

                        Gizmos.color = assignment.IsViewRay

                            ? new Color(0.35f, 0.75f, 1f, 0.9f)

                            : new Color(0.4f, 0.95f, 0.55f, 0.9f);

                        break;

                    case FluorescentLightTier.EmissionOnly:

                        Gizmos.color = new Color(0.55f, 0.55f, 0.65f, 0.65f);

                        size = 0.12f;

                        break;

                    default:

                        Gizmos.color = new Color(0.35f, 0.35f, 0.55f, 0.45f);

                        size = 0.1f;

                        break;

                }



                Gizmos.DrawSphere(pos, size);



#if UNITY_EDITOR

                if (settings.gizmoShowLabels && assignment.Tier != FluorescentLightTier.Off)

                {

                    string label = assignment.UseSpot ? "Spot+Point" : "Point";

                    if (assignment.IsViewRay)

                        label += " (ray)";

                    Handles.Label(pos + Vector3.up * 0.25f, label);

                }

#endif

            }

        }



        static void DrawHorizontalCircle(Vector3 center, float radius, int segments)

        {

            float step = 360f / segments;

            Vector3 prev = center + new Vector3(radius, 0f, 0f);



            for (int i = 1; i <= segments; i++)

            {

                float a = step * i * Mathf.Deg2Rad;

                var next = center + new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);

                Gizmos.DrawLine(prev, next);

                prev = next;

            }

        }

    }

}


