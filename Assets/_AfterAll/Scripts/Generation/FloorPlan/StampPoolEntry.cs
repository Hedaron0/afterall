using System;
using UnityEngine;

namespace AfterAll.Generation.FloorPlan
{
  [Serializable]
  public struct StampPoolEntry
  {
    public RoomStampDefinition Stamp;
    [Min(0f)] public float WeightMultiplier;
  }
}
