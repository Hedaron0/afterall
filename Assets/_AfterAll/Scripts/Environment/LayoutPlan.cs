using System;
using System.Collections.Generic;
using UnityEngine;

namespace AfterAll.Environment
{
    [Serializable]
    public class LayoutPlanPlacement
    {
        public int index;
        public string prefabId;
        public Vector2 positionXZ;
        public float yawDegrees;
    }

    [Serializable]
    public class LayoutPlanConnection
    {
        public int parentIndex;
        public string parentWall;
        public int childIndex;
        public string childWall;
        public float parentGapOffsetM;
        public float childGapOffsetM;
    }

    [Serializable]
    public class LayoutPlan
    {
        public int seed;
        public int roomCount;
        public int settlementCount;
        public int roomsPerSettlement;
        public int corridorRoomsPerBridge;
        public int stubBudget;
        public int exitIndex = -1;
        public int elevatorIndex = -1;
        public bool randomGapOffset;
        public List<LayoutPlanPlacement> placements = new();
        public List<LayoutPlanConnection> connections = new();
        public int failedAttempts;
        public string notes;

        public int PlacedCount => placements?.Count ?? 0;
    }
}
