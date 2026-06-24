using System.Collections.Generic;
using UnityEngine;

namespace AfterAll.Environment
{
    public struct LightPanelAssignment
    {
        public FluorescentLight     Panel;
        public FluorescentLightTier Tier;
        public float                Quality;
        public bool                 UseSpot;
        public bool                 UseFlicker;
        public bool                 IsViewRay;

        public LightPanelAssignment(
            FluorescentLight panel,
            FluorescentLightTier tier,
            float quality,
            bool useSpot,
            bool useFlicker,
            bool isViewRay = false)
        {
            Panel      = panel;
            Tier       = tier;
            Quality    = quality;
            UseSpot    = useSpot;
            UseFlicker = useFlicker;
            IsViewRay  = isViewRay;
        }

        public static LightPanelAssignment Off(FluorescentLight panel) =>
            new(panel, FluorescentLightTier.Off, 1f, false, false, false);
    }

    public sealed class LightRenderSnapshot
    {
        public readonly Dictionary<FluorescentLight, LightPanelAssignment> Assignments = new(128);
        public readonly List<LightRenderAnchor> Anchors = new(64);
        public readonly List<LightRenderRaySegment> RaySegments = new(16);

        public Vector3 PlayerPosition;
        public Vector3 CameraPosition;
        public Vector3 CameraForwardFlat;

        public int CountSpot;
        public int CountPointOnly;
        public int CountViewRay;
        public int CountEmission;
        public int CountOff;
        public int CountBlocked;

        public void Clear()
        {
            Assignments.Clear();
            Anchors.Clear();
            RaySegments.Clear();
            CountSpot = CountPointOnly = CountViewRay = CountEmission = CountOff = CountBlocked = 0;
        }

        public void Tally()
        {
            CountSpot = CountPointOnly = CountViewRay = CountEmission = CountOff = 0;

            foreach (var kv in Assignments)
            {
                var a = kv.Value;

                if (a.IsViewRay)
                    CountViewRay++;

                switch (a.Tier)
                {
                    case FluorescentLightTier.Full:
                        CountSpot++;
                        break;
                    case FluorescentLightTier.CorridorPartial:
                        CountPointOnly++;
                        break;
                    case FluorescentLightTier.EmissionOnly:
                        CountEmission++;
                        break;
                    default:
                        CountOff++;
                        break;
                }
            }
        }
    }

    public static class LightRenderBudget
    {
        readonly struct ScoredPanel
        {
            public readonly FluorescentLight Panel;
            public readonly float            Distance;
            public readonly float            Quality;

            public ScoredPanel(FluorescentLight panel, float distance, float quality)
            {
                Panel    = panel;
                Distance = distance;
                Quality  = quality;
            }
        }

        readonly struct CorridorCandidate
        {
            public readonly FluorescentLight Panel;
            public readonly float            Distance;
            public readonly float            Quality;
            public readonly int              AnchorIndex;
            public readonly float            AnchorDistSqr;

            public CorridorCandidate(
                FluorescentLight panel,
                float distance,
                float quality,
                int anchorIndex,
                float anchorDistSqr)
            {
                Panel         = panel;
                Distance      = distance;
                Quality       = quality;
                AnchorIndex   = anchorIndex;
                AnchorDistSqr = anchorDistSqr;
            }
        }

        public static void Compute(
            Vector3 playerPos,
            Camera cam,
            LightRenderSettings settings,
            IReadOnlyList<FluorescentLight> nearby,
            LightRenderSnapshot snapshot)
        {
            snapshot.Clear();

            if (settings == null)
                return;

            snapshot.PlayerPosition = playerPos;
            snapshot.CameraPosition = cam != null ? cam.transform.position : playerPos;

            Vector3 camForwardFlat = Vector3.forward;
            if (cam != null)
            {
                camForwardFlat = cam.transform.forward;
                camForwardFlat.y = 0f;
                if (camForwardFlat.sqrMagnitude > 0.0001f)
                    camForwardFlat.Normalize();
            }

            snapshot.CameraForwardFlat = camForwardFlat;

            if (cam != null)
                LightRenderProbe.Probe(playerPos, cam, settings, snapshot.Anchors, snapshot.RaySegments);

            float bubbleSqr = settings.bubbleRadius * settings.bubbleRadius;
            float influenceSqr = settings.anchorInfluenceRadius * settings.anchorInfluenceRadius;

            var resolved = snapshot.Assignments;
            var bubbleCandidates = new List<ScoredPanel>(32);
            var wrapCandidates = new List<ScoredPanel>(16);
            var corridorCandidates = new List<CorridorCandidate>(32);

            for (int i = 0; i < nearby.Count; i++)
            {
                var panel = nearby[i];
                if (panel == null || !panel.isActiveAndEnabled)
                    continue;

                float distSqr = FluorescentLight.HorizontalDistanceSqr(playerPos, panel.HorizontalPosition);
                float dist = Mathf.Sqrt(distSqr);
                float quality = ComputeQuality(dist, settings);

                if (distSqr <= bubbleSqr)
                {
                    bubbleCandidates.Add(new ScoredPanel(panel, dist, quality));
                    continue;
                }

                if (cam == null)
                {
                    resolved[panel] = LightPanelAssignment.Off(panel);
                    continue;
                }

                ProcessCorridorPanel(
                    panel, playerPos, dist, quality,
                    cam, camForwardFlat, settings, snapshot, snapshot.Anchors, influenceSqr,
                    wrapCandidates, corridorCandidates, resolved);
            }

            AssignBubbleBudget(bubbleCandidates, settings, playerPos, snapshot, resolved);

            for (int i = 0; i < bubbleCandidates.Count; i++)
            {
                var panel = bubbleCandidates[i].Panel;
                if (resolved.ContainsKey(panel))
                    continue;

                Vector3 eye = PlayerEye(playerPos, settings);
                if (!CanActivatePanel(panel, eye, settings))
                {
                    resolved[panel] = LightPanelAssignment.Off(panel);
                    continue;
                }

                resolved[panel] = new LightPanelAssignment(
                    panel, FluorescentLightTier.EmissionOnly, bubbleCandidates[i].Quality,
                    false, false);
            }

            AssignWrapBudget(wrapCandidates, settings, cam, snapshot, resolved);
            AssignCorridorBudget(corridorCandidates, playerPos, snapshot.Anchors, settings, cam, snapshot, resolved);
        }

        static Vector3 PlayerEye(Vector3 playerPos, LightRenderSettings settings) =>
            LightRenderOcclusion.EyePosition(playerPos, settings.lineOfSightEyeHeight);

        static bool CanActivatePanel(
            FluorescentLight panel,
            Vector3 observer,
            LightRenderSettings settings)
        {
            if (!settings.requireLineOfSight)
                return true;

            return LightRenderOcclusion.HasClearLineOfSight(
                observer,
                panel.WorldPosition,
                settings.wallOcclusionMask,
                settings.occlusionEndSlack,
                settings.floorOcclusionHeight);
        }

        static void MarkBlocked(LightRenderSnapshot snapshot)
        {
            snapshot.CountBlocked++;
        }

        static void AssignBubbleBudget(
            List<ScoredPanel> candidates,
            LightRenderSettings settings,
            Vector3 playerPos,
            LightRenderSnapshot snapshot,
            Dictionary<FluorescentLight, LightPanelAssignment> resolved)
        {
            candidates.Sort((a, b) => a.Distance.CompareTo(b.Distance));

            Vector3 eye = PlayerEye(playerPos, settings);
            int pick = Mathf.Min(settings.maxBubblePanels, candidates.Count);
            int assigned = 0;

            for (int i = 0; i < candidates.Count && assigned < pick; i++)
            {
                var c = candidates[i];
                if (!CanActivatePanel(c.Panel, eye, settings))
                {
                    MarkBlocked(snapshot);
                    continue;
                }

                resolved[c.Panel] = MakeDynamicAssignment(
                    c.Panel, c.Distance, c.Quality, settings, isViewRay: false);
                assigned++;
            }
        }

        static void AssignWrapBudget(
            List<ScoredPanel> candidates,
            LightRenderSettings settings,
            Camera cam,
            LightRenderSnapshot snapshot,
            Dictionary<FluorescentLight, LightPanelAssignment> resolved)
        {
            if (candidates.Count == 0)
                return;

            candidates.Sort((a, b) => a.Distance.CompareTo(b.Distance));

            Vector3 observer = cam.transform.position;
            int pick = Mathf.Min(4, candidates.Count);
            int assigned = 0;

            for (int i = 0; i < candidates.Count && assigned < pick; i++)
            {
                var c = candidates[i];
                if (resolved.ContainsKey(c.Panel))
                    continue;

                if (!CanActivatePanel(c.Panel, observer, settings))
                {
                    MarkBlocked(snapshot);
                    continue;
                }

                resolved[c.Panel] = MakeDynamicAssignment(
                    c.Panel, c.Distance, c.Quality, settings, isViewRay: false);
                assigned++;
            }
        }

        static void ProcessCorridorPanel(
            FluorescentLight panel,
            Vector3 playerPos,
            float playerDist,
            float quality,
            Camera cam,
            Vector3 camForwardFlat,
            LightRenderSettings settings,
            LightRenderSnapshot snapshot,
            IReadOnlyList<LightRenderAnchor> anchors,
            float influenceSqr,
            List<ScoredPanel> wrapCandidates,
            List<CorridorCandidate> corridorCandidates,
            Dictionary<FluorescentLight, LightPanelAssignment> resolved)
        {
            Vector3 camPos = cam.transform.position;

            float signedForward = LightRenderProbe.SignedForwardDistance(
                camPos, camForwardFlat, panel.HorizontalPosition);

            if (signedForward < -settings.rearCutoffDistance)
            {
                resolved[panel] = LightPanelAssignment.Off(panel);
                return;
            }

            if (signedForward < 0f)
            {
                wrapCandidates.Add(new ScoredPanel(panel, playerDist, quality * 0.85f));
                return;
            }

            if (playerDist > settings.rayMaxDistance)
            {
                resolved[panel] = LightPanelAssignment.Off(panel);
                return;
            }

            if (!CanActivatePanel(panel, camPos, settings))
            {
                MarkBlocked(snapshot);
                resolved[panel] = LightPanelAssignment.Off(panel);
                return;
            }

            bool inViewCone = LightRenderProbe.IsInViewCone(
                camPos, camForwardFlat, panel.HorizontalPosition, settings.viewHalfAngle);

            if (!inViewCone)
            {
                resolved[panel] = LightPanelAssignment.Off(panel);
                return;
            }

            int anchorIndex = LightRenderProbe.FindNearestAnchorIndex(panel.HorizontalPosition, anchors);
            if (anchorIndex >= 0)
            {
                float dx = panel.HorizontalPosition.x - anchors[anchorIndex].Position.x;
                float dz = panel.HorizontalPosition.z - anchors[anchorIndex].Position.z;
                float anchorDistSqr = dx * dx + dz * dz;

                if (anchorDistSqr <= influenceSqr)
                {
                    corridorCandidates.Add(new CorridorCandidate(
                        panel, playerDist, quality, anchorIndex, anchorDistSqr));
                    return;
                }
            }

            resolved[panel] = new LightPanelAssignment(
                panel, FluorescentLightTier.EmissionOnly, quality, false, false);
        }

        static void AssignCorridorBudget(
            List<CorridorCandidate> candidates,
            Vector3 playerPos,
            IReadOnlyList<LightRenderAnchor> anchors,
            LightRenderSettings settings,
            Camera cam,
            LightRenderSnapshot snapshot,
            Dictionary<FluorescentLight, LightPanelAssignment> resolved)
        {
            if (candidates.Count == 0)
                return;

            candidates.Sort((a, b) =>
            {
                int anchorCompare = anchors[a.AnchorIndex].DistanceFromCamera
                    .CompareTo(anchors[b.AnchorIndex].DistanceFromCamera);
                if (anchorCompare != 0)
                    return anchorCompare;

                return a.AnchorDistSqr.CompareTo(b.AnchorDistSqr);
            });

            var anchorCounts = new Dictionary<int, int>();
            int assigned = 0;
            Vector3 observer = cam.transform.position;

            for (int i = 0; i < candidates.Count; i++)
            {
                if (assigned >= settings.maxCorridorPanels)
                    break;

                var c = candidates[i];
                if (resolved.ContainsKey(c.Panel))
                    continue;

                if (!CanActivatePanel(c.Panel, observer, settings))
                {
                    MarkBlocked(snapshot);
                    continue;
                }

                anchorCounts.TryGetValue(c.AnchorIndex, out int count);
                if (count >= settings.maxPanelsPerAnchor)
                    continue;

                float corridorQuality = c.Quality;
                if (c.Distance > settings.corridorQualityFadeDistance)
                    corridorQuality *= settings.minFarQuality;

                resolved[c.Panel] = MakeDynamicAssignment(
                    c.Panel, c.Distance, corridorQuality, settings, isViewRay: true);

                anchorCounts[c.AnchorIndex] = count + 1;
                assigned++;
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                var panel = candidates[i].Panel;
                if (resolved.ContainsKey(panel))
                    continue;

                if (!CanActivatePanel(panel, observer, settings))
                {
                    MarkBlocked(snapshot);
                    resolved[panel] = LightPanelAssignment.Off(panel);
                    continue;
                }

                resolved[panel] = new LightPanelAssignment(
                    panel, FluorescentLightTier.EmissionOnly, candidates[i].Quality,
                    false, false);
            }
        }

        static LightPanelAssignment MakeDynamicAssignment(
            FluorescentLight panel,
            float distanceFromPlayer,
            float quality,
            LightRenderSettings settings,
            bool isViewRay)
        {
            bool useSpot = distanceFromPlayer <= settings.spotLightMaxDistance;
            bool useFlicker = distanceFromPlayer <= settings.bubbleFlickerRadius;
            var tier = useSpot ? FluorescentLightTier.Full : FluorescentLightTier.CorridorPartial;

            return new LightPanelAssignment(panel, tier, quality, useSpot, useFlicker, isViewRay);
        }

        public static float ComputeQuality(float distFromPlayer, LightRenderSettings settings)
        {
            if (distFromPlayer <= settings.bubbleRadius)
                return 1f;

            float t = Mathf.InverseLerp(settings.bubbleRadius, settings.rayMaxDistance, distFromPlayer);
            return Mathf.Lerp(1f, settings.minFarQuality, t);
        }
    }
}
