using UnityEngine;

namespace AfterAll.Environment
{
  /// <summary>
  /// Marks a direct child under Content/Random with an independent spawn chance.
  /// </summary>
  public class RoomContentPickable : MonoBehaviour
  {
    [SerializeField, Range(0f, 1f)] private float _spawnChance = 1f;

    private float _runtimeChanceMultiplier = 1f;

    public float SpawnChance => Mathf.Clamp01(_spawnChance * _runtimeChanceMultiplier);

    /// <summary>Scales the authored SpawnChance at runtime (e.g. loot depth weighting). Resets
    /// implicitly every floor build since this component is on a freshly instantiated prefab.</summary>
    public void SetRuntimeChanceMultiplier(float multiplier) => _runtimeChanceMultiplier = Mathf.Max(0f, multiplier);
  }
}
