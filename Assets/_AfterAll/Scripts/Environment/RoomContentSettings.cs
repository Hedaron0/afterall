using UnityEngine;

namespace AfterAll.Environment
{
  [CreateAssetMenu(fileName = "RoomContentSettings", menuName = "AfterAll/Room Content Settings")]
  public class RoomContentSettings : ScriptableObject
  {
    [Header("Preset")]
    [Tooltip("-1 = weighted random. Otherwise forces the preset folder with this number (1, 2, 3, ...).")]
    [SerializeField] private int _forcePresetIndex = -1;

    [Tooltip("Weights aligned to preset folders sorted by numeric name. Empty = equal weights.")]
    [SerializeField] private float[] _presetWeights = System.Array.Empty<float>();

    [Header("Random Pool")]
    [SerializeField, Min(0)] private int _randomPickMin = 2;
    [SerializeField, Min(0)] private int _randomPickMax = 2;

    [Header("Loot Depth Weighting")]
    [Tooltip("Loot-category (e.g. Echo) spawn-chance multiplier at GraphDepth 0 (hub/elevator).")]
    [SerializeField, Range(0f, 2f)] private float _lootChanceNearMultiplier = 0.4f;
    [Tooltip("GraphDepth at and beyond which the far multiplier applies at full strength.")]
    [SerializeField, Min(1)] private int _lootChanceFarDepth = 8;
    [Tooltip("Loot-category spawn-chance multiplier at or beyond Loot Chance Far Depth.")]
    [SerializeField, Range(0f, 2f)] private float _lootChanceFarMultiplier = 1.6f;

    [Header("Debug")]
    [SerializeField] private bool _logActivation;

    public int ForcePresetIndex => _forcePresetIndex;
    public float[] PresetWeights => _presetWeights;
    public int RandomPickMin => _randomPickMin;
    public int RandomPickMax => _randomPickMax;
    public bool LogActivation => _logActivation;
    public float LootChanceNearMultiplier => _lootChanceNearMultiplier;
    public int LootChanceFarDepth => _lootChanceFarDepth;
    public float LootChanceFarMultiplier => _lootChanceFarMultiplier;
  }
}
