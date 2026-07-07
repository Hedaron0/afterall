using UnityEngine;

namespace AfterAll.Environment
{
  /// <summary>
  /// Marks a direct child under Content/Random with an independent spawn chance.
  /// </summary>
  public class RoomContentPickable : MonoBehaviour
  {
    [SerializeField, Range(0f, 1f)] private float _spawnChance = 1f;

    public float SpawnChance => _spawnChance;
  }
}
