using UnityEngine;
using UnityEngine.Rendering;

namespace AfterAll.Environment
{
    /// <summary>
    /// Lights a non-lightmapped renderer from the room probe field (see <see cref="RoomLightProbeData"/>).
    ///
    /// Anything whose position isn't fixed at bake time is deliberately excluded from the lightmap by
    /// RoomStaticMeshCombiner.ApplyGiFlags — loot, fluorescent panels, props under a nested
    /// WeightedRandomGroup, and the door-wall pieces WallGapController scales at runtime. Without this
    /// component those renderers have lightmapIndex -1 and, since the game scene has no baked probe
    /// volume at all, nothing else to sample: they fall back to ambient and read as near-black.
    ///
    /// LightProbeUsage.CustomProvided is the supported way in: Unity skips its own (empty) probe
    /// lookup and uses the SH we put in the renderer's MaterialPropertyBlock instead.
    ///
    /// Re-samples whenever the object has moved far enough, which is what makes a carried item darken
    /// as the player walks it out of a lit room — the effect Harun asked for. Objects that never move
    /// pay one sample on enable and nothing after that.
    /// </summary>
    [DisallowMultipleComponent]
    public class ProbeLitRenderer : MonoBehaviour
    {
        [Tooltip("Re-sample once the object has moved this far from its last sample point. Smaller " +
                 "reacts sooner across a lighting boundary; larger samples less often.")]
        [SerializeField, Min(0.01f)] private float _resampleDistanceM = 0.4f;

        [Tooltip("Off for geometry that is placed once and never moves again (door-wall pieces are " +
                 "positioned during the build, then stay put) — saves the per-frame distance check.")]
        [SerializeField] private bool _trackMovement = true;

        [Tooltip("Include renderers on children. Off when a parent already covers them.")]
        [SerializeField] private bool _includeChildren = true;

        /// <summary>Frames an unsampled object keeps retrying before giving up. A room's probe field
        /// only becomes readable once RoomContentManager has picked its preset, which is several
        /// frames after the room spawns — but a room with no probe bake at all must not re-scan the
        /// whole level every frame forever.</summary>
        private const int SampleRetryFrames = 120;

        private Renderer[] _renderers;
        private MaterialPropertyBlock _block;
        private readonly SphericalHarmonicsL2[] _sh = new SphericalHarmonicsL2[1];
        private Vector3 _lastSamplePosition;
        private bool _sampled;
        private int _retriesLeft;

        private void OnEnable()
        {
            CacheRenderers();
            _sampled = false;
            _retriesLeft = SampleRetryFrames;
            Resample();
        }

        private void LateUpdate()
        {
            if (!_sampled)
            {
                if (--_retriesLeft <= 0)
                {
                    enabled = false;
                    return;
                }

                Resample();
                return;
            }

            // Placed once and then still: nothing left to do, so drop off the update list entirely
            // rather than pay a call per object per frame on a floor's worth of props.
            if (!_trackMovement)
            {
                enabled = false;
                return;
            }

            if ((GetSamplePosition() - _lastSamplePosition).sqrMagnitude >=
                _resampleDistanceM * _resampleDistanceM)
                Resample();
        }

        /// <summary>Re-samples every probe-lit renderer under <paramref name="root"/>. Called once a
        /// floor's rooms have their preset (and therefore their probe grid) resolved.</summary>
        public static void RefreshAll(Transform root)
        {
            if (root == null)
                return;

            foreach (ProbeLitRenderer lit in root.GetComponentsInChildren<ProbeLitRenderer>(true))
            {
                lit._retriesLeft = SampleRetryFrames;
                lit.enabled = true;
                lit.Resample();
            }
        }

        /// <summary>Forces a sample now — call after teleporting or re-parenting the object.</summary>
        public void Resample()
        {
            if (_renderers == null || _renderers.Length == 0)
                return;

            Vector3 position = GetSamplePosition();
            if (!RoomLightProbeData.TryFindSample(position, out SphericalHarmonicsL2 sh))
                return;

            _lastSamplePosition = position;
            _sampled = true;
            _sh[0] = sh;
            _block ??= new MaterialPropertyBlock();

            foreach (Renderer renderer in _renderers)
            {
                if (renderer == null)
                    continue;

                // Read the existing block back first: FluorescentLight and friends drive per-instance
                // material properties through the same channel, and SetPropertyBlock replaces it whole.
                renderer.GetPropertyBlock(_block);
                _block.CopySHCoefficientArraysFrom(_sh);
                renderer.SetPropertyBlock(_block);

                // Only now is CustomProvided safe. Switching earlier means that if the sample ever
                // fails, Unity uses the SH we never wrote — all zeroes — and the object renders pure
                // black. That is strictly worse than having no component at all, so the usage flag
                // only moves once there is real data behind it.
                renderer.lightProbeUsage = LightProbeUsage.CustomProvided;
            }
        }

#if UNITY_EDITOR
        /// <summary>Bake-time wiring from RoomLightmapBaker (fields are private + serialized).</summary>
        public void ConfigureForBake(bool trackMovement, bool includeChildren)
        {
            _trackMovement = trackMovement;
            _includeChildren = includeChildren;
        }
#endif

        /// <summary>
        /// Where in the world to read the light from: the centre of the geometry, not the pivot.
        ///
        /// Pivots in this kit are not reliably inside their own mesh — room7's wall pivots sit tens
        /// of metres from the wall they drive — so sampling at transform.position can read the light
        /// of a completely different part of the room, or of no room at all. Renderer bounds are
        /// world-space and always wrap the visible surface, which is what should be lit.
        /// </summary>
        private Vector3 GetSamplePosition()
        {
            Bounds bounds = default;
            bool any = false;

            foreach (Renderer renderer in _renderers)
            {
                if (renderer == null)
                    continue;

                if (!any)
                {
                    bounds = renderer.bounds;
                    any = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return any ? bounds.center : transform.position;
        }

        /// <summary>Re-reads the renderer list — call after adding or removing renderers at runtime.</summary>
        public void CacheRenderers()
        {
            _renderers = _includeChildren
                ? GetComponentsInChildren<Renderer>(true)
                : GetComponents<Renderer>();

            foreach (Renderer renderer in _renderers)
            {
                // Safe default until a sample lands: BlendProbes with no baked probe volume falls
                // back to RenderSettings.ambientProbe, which ProbeLightingDirector keeps set to the
                // light around the player. Wrong-but-plausible beats black.
                if (renderer != null && renderer.lightProbeUsage != LightProbeUsage.CustomProvided)
                    renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
            }
        }
    }
}
