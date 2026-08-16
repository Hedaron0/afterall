using UnityEngine;

namespace AfterAll.Environment
{
  [CreateAssetMenu(fileName = "RoomContentSettings", menuName = "AfterAll/Room Content Settings")]
  public class RoomContentSettings : ScriptableObject
  {
    [Header("Loot")]
    [Tooltip("What can spawn at a LootSpawnPoint. One table for the whole game for now; a per-room " +
             "override is a single extra field on RoomLootBudget when it is wanted.")]
    [SerializeField] private LootTable _lootTable;

    [Header("Loot Depth Weighting")]
    [Tooltip("Loot count multiplier at GraphDepth 0 (hub/elevator) — below 1 makes the rooms you " +
             "start in thin.")]
    [SerializeField, Range(0f, 2f)] private float _lootDepthNearMultiplier = 0.4f;

    [Tooltip("GraphDepth at and beyond which the far multiplier applies at full strength.")]
    [SerializeField, Min(1)] private int _lootDepthFarDepth = 8;

    [Tooltip("Loot count multiplier at or beyond Loot Depth Far Depth — above 1 rewards going deep.")]
    [SerializeField, Range(0f, 2f)] private float _lootDepthFarMultiplier = 1.6f;

    [Header("Debug")]
    [SerializeField] private bool _logActivation;

    public LootTable LootTable => _lootTable;
    public bool LogActivation => _logActivation;
    public float LootDepthNearMultiplier => _lootDepthNearMultiplier;
    public int LootDepthFarDepth => _lootDepthFarDepth;
    public float LootDepthFarMultiplier => _lootDepthFarMultiplier;
  }
}
