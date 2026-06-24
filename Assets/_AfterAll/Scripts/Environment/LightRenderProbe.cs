using System.Collections.Generic;
using UnityEngine;

namespace AfterAll.Environment
{
    public struct LightRenderAnchor
    {
        public Vector3 Position;
        public float   DistanceFromCamera;
        public float   DistanceFromPlayer;
        public int     RayIndex;

        public LightRenderAnchor(Vector3 position, float distanceFromCamera, float distanceFromPlayer, int rayIndex)
        {
            Position           = position;
            DistanceFromCamera = distanceFromCamera;
            DistanceFromPlayer = distanceFromPlayer;
            RayIndex           = rayIndex;
        }
    }

    public struct LightRenderRaySegment
    {
        public Vector3 Start;
        public Vector3 End;
        public bool    HitWall;

        public LightRenderRaySegment(Vector3 start, Vector3 end, bool hitWall)
        {
            Start   = start;
            End     = end;
            HitWall = hitWall;
        }
    }

    public static class LightRenderProbe
    {
        public static void Probe(
            Vector3 playerPos,
            Camera cam,
            LightRenderSettings settings,
            List<LightRenderAnchor> anchors,
            List<LightRenderRaySegment> segments)
        {
            anchors.Clear();
            segments.Clear();

            if (cam == null || settings == null)
                return;

            Vector3 origin = cam.transform.position;
            Vector3 forwardFlat = cam.transform.forward;
            forwardFlat.y = 0f;

            if (forwardFlat.sqrMagnitude < 0.0001f)
                forwardFlat = Vector3.forward;
            else
                forwardFlat.Normalize();

            int rayCount = settings.rayCount;
            float halfFov = settings.viewHalfAngle;
            float maxDist = settings.rayMaxDistance;
            float spacing = settings.anchorSpacing;
            float anchorStart = settings.anchorStartDistance;
            int wallMask = settings.wallOcclusionMask.value;

            for (int i = 0; i < rayCount; i++)
            {
                float t = rayCount <= 1 ? 0f : i / (float)(rayCount - 1);
                float yaw = Mathf.Lerp(-halfFov, halfFov, t);
                Vector3 dir = Quaternion.Euler(0f, yaw, 0f) * forwardFlat;

                float blockDistance = maxDist;
                bool hitWall = false;

                if (wallMask != 0
                    && Physics.Raycast(origin, dir, out RaycastHit hit, maxDist, wallMask, QueryTriggerInteraction.Ignore))
                {
                    blockDistance = hit.distance;
                    hitWall = true;
                    segments.Add(new LightRenderRaySegment(origin, hit.point, true));
                }
                else
                {
                    segments.Add(new LightRenderRaySegment(origin, origin + dir * maxDist, false));
                }

                float dist = spacing;
                while (dist < blockDistance - 0.05f)
                {
                    Vector3 sample = origin + dir * dist;
                    float playerDist = HorizontalDistance(playerPos, sample);

                    if (playerDist >= anchorStart)
                    {
                        anchors.Add(new LightRenderAnchor(
                            sample, dist, playerDist, i));
                    }

                    dist += spacing;
                }

                if (blockDistance >= spacing * 0.5f)
                {
                    float endDist = hitWall ? blockDistance : maxDist;
                    Vector3 endSample = origin + dir * endDist;
                    float endPlayerDist = HorizontalDistance(playerPos, endSample);

                    if (endPlayerDist >= anchorStart && !HasAnchorNear(anchors, i, endDist, spacing))
                        anchors.Add(new LightRenderAnchor(endSample, endDist, endPlayerDist, i));
                }
            }
        }

        public static bool IsWallBlocked(Vector3 from, Vector3 to, LayerMask wallMask, float endSlack = 0.35f) =>
            !LightRenderOcclusion.HasClearLineOfSight(from, to, wallMask, endSlack);

        public static bool IsInViewCone(Vector3 cameraPos, Vector3 cameraForwardFlat, Vector3 target, float halfAngle)
        {
            Vector3 toTarget = target - cameraPos;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude < 0.0001f)
                return true;

            return Vector3.Angle(cameraForwardFlat, toTarget.normalized) <= halfAngle;
        }

        public static float SignedForwardDistance(Vector3 cameraPos, Vector3 cameraForwardFlat, Vector3 target)
        {
            Vector3 toTarget = target - cameraPos;
            toTarget.y = 0f;
            return Vector3.Dot(toTarget, cameraForwardFlat);
        }

        public static int FindNearestAnchorIndex(Vector3 panelPosition, IReadOnlyList<LightRenderAnchor> anchors)
        {
            int best = -1;
            float bestSqr = float.MaxValue;

            for (int i = 0; i < anchors.Count; i++)
            {
                float dx = panelPosition.x - anchors[i].Position.x;
                float dz = panelPosition.z - anchors[i].Position.z;
                float sqr = dx * dx + dz * dz;
                if (sqr >= bestSqr)
                    continue;

                bestSqr = sqr;
                best = i;
            }

            return best;
        }

        static bool HasAnchorNear(List<LightRenderAnchor> anchors, int rayIndex, float dist, float spacing)
        {
            for (int a = anchors.Count - 1; a >= 0 && a >= anchors.Count - 6; a--)
            {
                if (a < 0) break;
                if (anchors[a].RayIndex == rayIndex
                    && Mathf.Abs(anchors[a].DistanceFromCamera - dist) < spacing * 0.4f)
                    return true;
            }

            return false;
        }

        static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }
    }
}
