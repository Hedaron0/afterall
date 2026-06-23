using UnityEngine;

namespace AfterAll.Player
{
    [CreateAssetMenu(fileName = "StaminaSettings", menuName = "AfterAll/Settings/Stamina")]
    public class StaminaSettings : ScriptableObject
    {
        [Header("Pool")]
        [Tooltip("Maximum stamina value.")]
        [SerializeField] [Min(1f)] public float MaxStamina = 100f;

        [Header("Drain")]
        [Tooltip("Stamina lost per second while actively sprinting and moving.")]
        [SerializeField] [Min(0f)] public float DrainPerSecond = 18f;

        [Header("Regen")]
        [Tooltip("Stamina recovered per second when not draining.")]
        [SerializeField] [Min(0f)] public float RegenPerSecond = 10f;

        [Tooltip("Multiplier applied to regen while crouching.")]
        [SerializeField] [Min(1f)] public float CrouchRegenMultiplier = 2.2f;

        [Tooltip("Seconds after sprinting stops before regen begins.")]
        [SerializeField] [Min(0f)] public float RegenDelay = 0.6f;

        [Tooltip("Stamina must reach this value before a new sprint can begin after exhaustion.")]
        [SerializeField] [Range(0f, 1f)] public float MinNormalizedToResume = 0.15f;
    }
}
