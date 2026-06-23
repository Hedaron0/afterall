using System;
using UnityEngine;

namespace AfterAll.Player
{
    /// <summary>
    /// Manages the sprint stamina pool.
    /// PlayerMovement reads CanSprint and calls Tick() each frame.
    /// SprintButtonUI subscribes to OnStaminaChanged for the ring display.
    /// </summary>
    public class Stamina : MonoBehaviour
    {
        public static event Action<float> OnStaminaChanged;

        [SerializeField] private StaminaSettings _settings;

        public float Current { get; private set; }
        public float Max => _settings != null ? _settings.MaxStamina : 100f;
        public float Normalized => Max > 0f ? Current / Max : 0f;

        public bool CanSprint { get; private set; } = true;

        private float _regenDelayTimer;

        private void Awake()
        {
            if (_settings == null)
                Debug.LogWarning("[Stamina] No StaminaSettings assigned — using defaults.", this);

            Current = Max;
            CanSprint = true;
        }

        /// <summary>
        /// Called by PlayerMovement every Update after sprint/crouch state is resolved.
        /// </summary>
        public void Tick(bool isSprinting, bool isMoving, bool isCrouching)
        {
            var prev = Current;
            var prevCanSprint = CanSprint;

            if (isSprinting && isMoving)
            {
                _regenDelayTimer = _settings != null ? _settings.RegenDelay : 0.6f;
                float drain = (_settings != null ? _settings.DrainPerSecond : 18f) * Time.deltaTime;
                Current = Mathf.Max(0f, Current - drain);

                if (Current <= 0f)
                {
                    CanSprint = false;
                    AfterAll.UI.MobileSprintBridge.SetWantsSprint(false);
                }
            }
            else
            {
                _regenDelayTimer -= Time.deltaTime;

                if (_regenDelayTimer <= 0f)
                {
                    float rate = _settings != null ? _settings.RegenPerSecond : 10f;
                    if (isCrouching)
                        rate *= _settings != null ? _settings.CrouchRegenMultiplier : 2.2f;

                    Current = Mathf.Min(Max, Current + rate * Time.deltaTime);
                }

                float threshold = Max * (_settings != null ? _settings.MinNormalizedToResume : 0.15f);
                if (!CanSprint && Current >= threshold)
                    CanSprint = true;
            }

            if (!Mathf.Approximately(prev, Current) || prevCanSprint != CanSprint)
                OnStaminaChanged?.Invoke(Normalized);
        }

        public StaminaSettings Settings => _settings;
    }
}
