using System;
using System.Collections.Generic;
using UnityEngine;

namespace AfterAll.Environment
{
    /// <summary>
    /// Objective Top View silhouette scores for comparing planners (strip vs packed cluster).
    /// Pure data — no Instantiate. Use after any LayoutPlan generate.
    /// </summary>
    public readonly struct LayoutSilhouetteReport
    {
        public readonly int roomCount;
        public readonly int connectionCount;
        public readonly float hullWidthM;
        public readonly float hullDepthM;
        /// <summary>max/min hull side. ~1 = blob; ≥~3 = strip-like.</summary>
        public readonly float hullAspect;
        /// <summary>Sum of room AABB areas / hull AABB area (0–1+). Higher = tighter pack.</summary>
        public readonly float packingFill;
        /// <summary>2 * connections / rooms. Clustered graphs tend toward ≥1.2.</summary>
        public readonly float meanDegree;
        public readonly float corridorFraction;
        /// <summary>packingFill / hullAspect. Higher = denser + less strip-like.</summary>
        public readonly float clusterScore;
        public readonly bool hasMissingFootprints;

        public LayoutSilhouetteReport(
            int roomCount,
            int connectionCount,
            float hullWidthM,
            float hullDepthM,
            float hullAspect,
            float packingFill,
            float meanDegree,
            float corridorFraction,
            float clusterScore,
            bool hasMissingFootprints)
        {
            this.roomCount = roomCount;
            this.connectionCount = connectionCount;
            this.hullWidthM = hullWidthM;
            this.hullDepthM = hullDepthM;
            this.hullAspect = hullAspect;
            this.packingFill = packingFill;
            this.meanDegree = meanDegree;
            this.corridorFraction = corridorFraction;
            this.clusterScore = clusterScore;
            this.hasMissingFootprints = hasMissingFootprints;
        }

        public string ToStatusLine()
        {
            string warn = hasMissingFootprints ? " ⚠missing fp" : string.Empty;
            return
                $"Silhouette: rooms={roomCount} conn={connectionCount} " +
                $"aspect={hullAspect:F2} fill={packingFill:P0} " +
                $"deg={meanDegree:F2} corr={corridorFraction:P0} " +
                $"cluster={clusterScore:F2}{warn}";
        }
    }

    public static class LayoutSilhouetteMetrics
    {
        /// <summary>
        /// Soft visual-sign-off targets for packed-cluster layouts (not hard fails).
        /// Next planner pass should beat PaintGrowth on these for Rooms≈20.
        /// </summary>
        public const float TargetMaxHullAspect = 2.5f;
        public const float TargetMinPackingFill = 0.35f;
        public const float TargetMinClusterScore = 0.2f;

        public static LayoutSilhouetteReport Evaluate(
            LayoutPlan plan,
            IReadOnlyDictionary<string, RoomFootprint> libraryByPrefabId)
        {
            if (plan == null || plan.PlacedCount == 0 || libraryByPrefabId == null)
            {
                return new LayoutSilhouetteReport(
                    0, 0, 0f, 0f, 1f, 0f, 0f, 0f, 0f, false);
            }

            float hullMinX = float.PositiveInfinity;
            float hullMinZ = float.PositiveInfinity;
            float hullMaxX = float.NegativeInfinity;
            float hullMaxZ = float.NegativeInfinity;
            float roomAreaSum = 0f;
            int corridorCount = 0;
            bool missing = false;

            for (int i = 0; i < plan.placements.Count; i++)
            {
                LayoutPlanPlacement placement = plan.placements[i];
                if (string.IsNullOrEmpty(placement.prefabId) ||
                    !libraryByPrefabId.TryGetValue(placement.prefabId, out RoomFootprint footprint) ||
                    footprint == null)
                {
                    missing = true;
                    continue;
                }

                GetWorldAabb(
                    footprint,
                    placement.positionXZ,
                    placement.yawDegrees * Mathf.Deg2Rad,
                    out Vector2 min,
                    out Vector2 max);

                hullMinX = Mathf.Min(hullMinX, min.x);
                hullMinZ = Mathf.Min(hullMinZ, min.y);
                hullMaxX = Mathf.Max(hullMaxX, max.x);
                hullMaxZ = Mathf.Max(hullMaxZ, max.y);
                roomAreaSum += footprint.BoundsAreaM2;
                if (footprint.IsCorridorShape)
                    corridorCount++;
            }

            int rooms = plan.PlacedCount;
            int connections = plan.connections?.Count ?? 0;
            if (float.IsInfinity(hullMinX))
            {
                return new LayoutSilhouetteReport(
                    rooms, connections, 0f, 0f, 1f, 0f,
                    rooms > 0 ? 2f * connections / rooms : 0f,
                    0f, 0f, missing);
            }

            float hullW = Mathf.Max(0.01f, hullMaxX - hullMinX);
            float hullD = Mathf.Max(0.01f, hullMaxZ - hullMinZ);
            float shortSide = Mathf.Min(hullW, hullD);
            float longSide = Mathf.Max(hullW, hullD);
            float aspect = longSide / shortSide;
            float hullArea = hullW * hullD;
            float fill = roomAreaSum / hullArea;
            float meanDegree = rooms > 0 ? 2f * connections / rooms : 0f;
            float corridorFraction = rooms > 0 ? (float)corridorCount / rooms : 0f;
            float clusterScore = fill / Mathf.Max(1f, aspect);

            return new LayoutSilhouetteReport(
                rooms,
                connections,
                hullW,
                hullD,
                aspect,
                fill,
                meanDegree,
                corridorFraction,
                clusterScore,
                missing);
        }

        public static LayoutSilhouetteReport Evaluate(
            LayoutPlan plan,
            IReadOnlyList<RoomFootprint> library)
        {
            var map = new Dictionary<string, RoomFootprint>(StringComparer.Ordinal);
            if (library != null)
            {
                for (int i = 0; i < library.Count; i++)
                {
                    RoomFootprint footprint = library[i];
                    if (footprint == null || string.IsNullOrEmpty(footprint.PrefabId))
                        continue;
                    if (!map.ContainsKey(footprint.PrefabId))
                        map.Add(footprint.PrefabId, footprint);
                }
            }

            return Evaluate(plan, map);
        }

        public static bool MeetsSoftClusterTargets(in LayoutSilhouetteReport report) =>
            report.roomCount > 0 &&
            report.hullAspect <= TargetMaxHullAspect &&
            report.packingFill >= TargetMinPackingFill &&
            report.clusterScore >= TargetMinClusterScore;

        private static void GetWorldAabb(
            RoomFootprint footprint,
            Vector2 positionXZ,
            float yawRadians,
            out Vector2 min,
            out Vector2 max)
        {
            Vector2 bMin = footprint.BoundsMin;
            Vector2 bMax = footprint.BoundsMax;
            Vector2 c0 = positionXZ + Rotate(new Vector2(bMin.x, bMin.y), yawRadians);
            Vector2 c1 = positionXZ + Rotate(new Vector2(bMin.x, bMax.y), yawRadians);
            Vector2 c2 = positionXZ + Rotate(new Vector2(bMax.x, bMax.y), yawRadians);
            Vector2 c3 = positionXZ + Rotate(new Vector2(bMax.x, bMin.y), yawRadians);

            min = Vector2.Min(Vector2.Min(c0, c1), Vector2.Min(c2, c3));
            max = Vector2.Max(Vector2.Max(c0, c1), Vector2.Max(c2, c3));
        }

        private static Vector2 Rotate(Vector2 value, float radians)
        {
            float c = Mathf.Cos(radians);
            float s = Mathf.Sin(radians);
            return new Vector2(value.x * c - value.y * s, value.x * s + value.y * c);
        }
    }
}
