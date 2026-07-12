using System;
using System.Collections.Generic;
using UnityEngine;

namespace AfterAll.Environment
{
    /// <summary>
    /// Paint-growth layout knobs. Only room budget is user-facing;
    /// breathe/wander behavior is internal to <see cref="PaintGrowthPlanner"/>.
    /// </summary>
    [Serializable]
    public struct PaintGrowthConfig
    {
        public int targetRoomCount;
        public bool randomGapOffset;
        public GapOffsetPolicy gapPolicy;

        public static PaintGrowthConfig Default => FromTargetRoomCount(20);

        public static PaintGrowthConfig FromTargetRoomCount(
            int targetRoomCount,
            bool randomGapOffset = false,
            GapOffsetPolicy gapPolicy = default)
        {
            if (gapPolicy.edgeMarginM <= 0f && gapPolicy.spanFraction <= 0f)
                gapPolicy = GapOffsetPolicy.Default;

            var config = new PaintGrowthConfig
            {
                targetRoomCount = Mathf.Clamp(targetRoomCount, 8, 80),
                randomGapOffset = randomGapOffset,
                gapPolicy = gapPolicy
            };
            config.gapPolicy.randomGapOffset = randomGapOffset;
            return config;
        }

        public void Clamp()
        {
            targetRoomCount = Mathf.Clamp(targetRoomCount, 8, 80);
        }
    }
}
