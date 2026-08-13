using AfterAll.Player;
using UnityEngine;
using UnityEngine.Rendering;

namespace AfterAll.Environment
{
    /// <summary>
    /// Feeds the room probe field into RenderSettings.ambientProbe, following the player.
    ///
    /// This is the safety net under <see cref="ProbeLitRenderer"/>. That component gives an object
    /// light sampled at its own position, but it has to be ON the object — so anything spawned,
    /// dragged in, or authored without it (a loose WorldItem prefab, a debug cube, a future prop)
    /// falls back to RenderSettings, and the bake environment's ambient is near-black by design.
    /// The result is an object that stays pitch black no matter which room you carry it into.
    ///
    /// Writing the player's local light into the ambient probe makes that fallback sane: an unwired
    /// dynamic object is lit as if it were standing where the player is, which for a carried or
    /// nearby object is very close to correct and is never black. Lightmapped geometry is unaffected
    /// — it samples its lightmap, not the ambient probe.
    ///
    /// Ambient mode must be Custom for Unity to use the probe we set rather than recomputing it from
    /// the flat/trilight colours, so this switches it and restores the original on disable.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class ProbeLightingDirector : MonoBehaviour
    {
        [Tooltip("Whose surroundings drive the ambient probe. Falls back to the PlayerMovement in " +
                 "the scene when left empty.")]
        [SerializeField] private Transform _follow;

        [Tooltip("Re-sample once the follow target has moved this far. The probe grid is metres " +
                 "across, so sampling every frame buys nothing.")]
        [SerializeField, Min(0.05f)] private float _resampleDistanceM = 0.5f;

        [Tooltip("Seconds to blend from the previous ambient to the new sample. Stops a hard colour " +
                 "pop on every dynamic object when the player crosses a doorway.")]
        [SerializeField, Min(0f)] private float _blendSeconds = 0.25f;

        private AmbientMode _originalMode;
        private SphericalHarmonicsL2 _originalProbe;
        private SphericalHarmonicsL2 _current;
        private SphericalHarmonicsL2 _target;
        private Vector3 _lastSamplePosition;
        private bool _hasTarget;
        private bool _captured;

        private void OnEnable()
        {
            _originalMode = RenderSettings.ambientMode;
            _originalProbe = RenderSettings.ambientProbe;
            _captured = true;
            _hasTarget = false;
        }

        private void OnDisable()
        {
            if (!_captured)
                return;

            RenderSettings.ambientMode = _originalMode;
            RenderSettings.ambientProbe = _originalProbe;
        }

        private void LateUpdate()
        {
            Transform target = ResolveFollowTarget();
            if (target == null)
                return;

            Vector3 position = target.position;
            if (!_hasTarget ||
                (position - _lastSamplePosition).sqrMagnitude >= _resampleDistanceM * _resampleDistanceM)
            {
                if (RoomLightProbeData.TryFindSample(position, out SphericalHarmonicsL2 sampled))
                {
                    _lastSamplePosition = position;
                    _target = sampled;

                    if (!_hasTarget)
                    {
                        _current = sampled;
                        _hasTarget = true;
                    }
                }
            }

            if (!_hasTarget)
                return;

            _current = _blendSeconds > 0f
                ? BlendTowards(_current, _target, Time.deltaTime / _blendSeconds)
                : _target;

            RenderSettings.ambientMode = AmbientMode.Custom;
            RenderSettings.ambientProbe = _current;
        }

        private Transform ResolveFollowTarget()
        {
            if (_follow != null)
                return _follow;

            PlayerMovement movement = FindAnyObjectByType<PlayerMovement>();
            if (movement != null)
                _follow = movement.transform;

            return _follow;
        }

        /// <summary>Coefficient-wise lerp. SH is linear, so blending the coefficients blends the
        /// lighting they describe.</summary>
        private static SphericalHarmonicsL2 BlendTowards(
            SphericalHarmonicsL2 from, SphericalHarmonicsL2 to, float t)
        {
            t = Mathf.Clamp01(t);
            var result = new SphericalHarmonicsL2();

            for (int channel = 0; channel < 3; channel++)
            for (int coefficient = 0; coefficient < 9; coefficient++)
                result[channel, coefficient] = Mathf.Lerp(
                    from[channel, coefficient], to[channel, coefficient], t);

            return result;
        }
    }
}
