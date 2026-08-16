using UnityEngine;
using UnityEngine.Rendering;

namespace AfterAll.Environment
{
    /// <summary>
    /// Holds RenderSettings ambient at a fixed level, as the floor under <see cref="ProbeLitRenderer"/>.
    ///
    /// This used to sample the room probe field at the PLAYER's position every frame and write that
    /// into RenderSettings.ambientProbe, so that an object with no probe sample of its own was lit as
    /// if it stood where the player stands. That is wrong in a way that shows: ambient is global, so
    /// it can only ever describe one place at a time, and the place it described was the camera's.
    /// Looking at a lit object from a dark corridor dimmed the object; walking into light brightened
    /// objects across the room. An object's brightness must not depend on where it is looked at from.
    ///
    /// A constant cannot be right everywhere either, but it is at least stable and viewer-independent,
    /// and it is only ever seen by renderers that failed to get their own sample — everything on
    /// LightProbeUsage.CustomProvided and everything lightmapped ignores it entirely. Treat a visible
    /// change here as a signal that something is falling through to the fallback that should not be.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class ProbeLightingDirector : MonoBehaviour
    {
        [Tooltip("Ambient given to anything that has no light of its own. Deliberately dim — this is " +
                 "a floor that stops an unsampled object rendering pure black, not a light source.")]
        [SerializeField] private Color _fallbackAmbient = new Color(0.12f, 0.12f, 0.13f, 1f);

        private AmbientMode _originalMode;
        private Color _originalAmbient;
        private SphericalHarmonicsL2 _originalProbe;
        private bool _captured;

        private void OnEnable()
        {
            _originalMode = RenderSettings.ambientMode;
            _originalAmbient = RenderSettings.ambientLight;
            _originalProbe = RenderSettings.ambientProbe;
            _captured = true;

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = _fallbackAmbient;
        }

        private void OnDisable()
        {
            if (!_captured)
                return;

            RenderSettings.ambientMode = _originalMode;
            RenderSettings.ambientLight = _originalAmbient;
            RenderSettings.ambientProbe = _originalProbe;
        }

    }
}
