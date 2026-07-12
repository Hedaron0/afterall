using System;
using UnityEngine;

namespace AfterAll.Environment
{
    public enum ExitBiasDirection
    {
        East = 0,
        North = 1,
        West = 2,
        South = 3
    }

    /// <summary>
    /// Settlement-spine layout knobs (Top View + RoomPoolSpawner).
    /// Spawn hub → settlement clusters → corridor bridges → exit cluster.
    /// </summary>
    [Serializable]
    public struct SettlementSpineConfig
    {
        [Min(1)] public int settlementCount;
        [Min(1)] public int roomsPerSettlement;
        [Min(1)] public int corridorRoomsPerBridge;
        [Min(0)] public int stubBudget;
        public ExitBiasDirection exitBias;
        public bool randomGapOffset;
        public GapOffsetPolicy gapPolicy;

        public static SettlementSpineConfig Default => new SettlementSpineConfig
        {
            settlementCount = 3,
            roomsPerSettlement = 5,
            corridorRoomsPerBridge = 2,
            stubBudget = 1,
            exitBias = ExitBiasDirection.East,
            randomGapOffset = false,
            gapPolicy = GapOffsetPolicy.Default
        };

        public Vector2 ExitBiasVector => exitBias switch
        {
            ExitBiasDirection.East => new Vector2(1f, 0f),
            ExitBiasDirection.North => new Vector2(0f, 1f),
            ExitBiasDirection.West => new Vector2(-1f, 0f),
            ExitBiasDirection.South => new Vector2(0f, -1f),
            _ => new Vector2(1f, 0f)
        };

        public int EstimatedRoomCount =>
            1 +
            Mathf.Max(1, settlementCount) * Mathf.Max(1, roomsPerSettlement) +
            Mathf.Max(0, settlementCount - 1) * Mathf.Max(1, corridorRoomsPerBridge) +
            Mathf.Max(0, stubBudget) * Mathf.Max(1, settlementCount);

        public void Clamp()
        {
            settlementCount = Mathf.Max(1, settlementCount);
            roomsPerSettlement = Mathf.Max(1, roomsPerSettlement);
            corridorRoomsPerBridge = Mathf.Max(1, corridorRoomsPerBridge);
            stubBudget = Mathf.Max(0, stubBudget);
        }
    }
}
