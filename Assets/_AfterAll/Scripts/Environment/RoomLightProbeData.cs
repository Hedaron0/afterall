using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace AfterAll.Environment
{
    /// <summary>
    /// Carries a room prefab's baked light-probe field with it, one grid per content preset.
    ///
    /// This is the probe counterpart to <see cref="RoomLightmapData"/>, and it exists for the same
    /// reason: Unity's probe data belongs to a scene and stores WORLD-space positions, but rooms are
    /// instantiated at runtime wherever the planner puts them, so the normal probe pipeline never
    /// reaches them. LightmapSettings.lightProbes is empty in the game scene and always will be.
    ///
    /// So the bake stores its own regular grid of spherical harmonics in ROOM-LOCAL space
    /// (RoomLightProbeBaker samples LightProbes.GetInterpolatedProbe across the room right after
    /// Lightmapping.Bake()), and <see cref="TrySample"/> re-interpolates it at runtime. Consumers
    /// push the result into a renderer through LightProbeUsage.CustomProvided — see
    /// <see cref="ProbeLitRenderer"/>.
    ///
    /// Only the L0 and L1 bands are stored (12 floats per probe instead of 27). L1 is a plain vector
    /// band, so rotating the field into the room's runtime yaw is a vector rotation per channel;
    /// rotating L2 correctly would need a real SH rotation and buys very little on geometry lit by
    /// flat ceiling fluorescents.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class RoomLightProbeData : MonoBehaviour
    {
        /// <summary>Floats stored per probe: 3 colour channels x (L0 + 3 L1 coefficients).</summary>
        public const int CoefficientsPerProbe = 12;

        /// <summary>One bake: the room's probe field with a single preset option active.</summary>
        [Serializable]
        public class Variant
        {
            public string presetName = string.Empty;

            [Tooltip("Room-local position of grid cell (0,0,0).")]
            public Vector3 originLocal;

            [Tooltip("Room-local spacing between adjacent cells on each axis.")]
            public Vector3 cellSize = Vector3.one;

            public Vector3Int dimensions = Vector3Int.one;

            [Tooltip("Flattened SH: CoefficientsPerProbe floats per probe, x-major then y then z.")]
            public float[] coefficients = Array.Empty<float>();

            public int ProbeCount => dimensions.x * dimensions.y * dimensions.z;

            public bool IsValid =>
                dimensions.x > 0 && dimensions.y > 0 && dimensions.z > 0 &&
                coefficients.Length == ProbeCount * CoefficientsPerProbe;
        }

        [SerializeField] private Variant[] _variants = Array.Empty<Variant>();

        /// <summary>Live instances, so a dynamic object anywhere in the level can find the room it
        /// is standing in without depending on transform parenting (a held item is parented to the
        /// player, not to a room).</summary>
        private static readonly List<RoomLightProbeData> Active = new List<RoomLightProbeData>();

        private Variant _applied;

        public bool HasBakedData => _variants.Length > 0;

        private void OnEnable() => Active.Add(this);

        private void OnDisable() => Active.Remove(this);

        /// <summary>Selects the grid baked for <paramref name="presetName"/>, mirroring
        /// <see cref="RoomLightmapData.ApplyVariant"/> so both are driven off the same winner.</summary>
        public void ApplyVariant(string presetName)
        {
            if (_variants.Length == 0)
                return;

            foreach (Variant variant in _variants)
            {
                if (variant.presetName == presetName)
                {
                    _applied = variant;
                    return;
                }
            }

            Debug.LogWarning(
                $"[RoomLightProbeData] {name}: no probe bake for preset '{presetName}' — falling back " +
                $"to '{_variants[0].presetName}'. Re-run Bake Room Lightmaps after changing presets.",
                this);
            _applied = _variants[0];
        }

        /// <summary>Beyond this distance from a room's grid footprint that room stops contributing.
        /// Only has to span the gap between two neighbouring grids, which is the bake's wall inset on
        /// both sides plus the shared wall.</summary>
        private const float RoomBlendRadiusM = 6f;

        /// <summary>Softens the inverse-square weight at zero distance. Small, so the room a point is
        /// actually standing in stays overwhelmingly dominant: a neighbour 1m outside contributes
        /// about 8%, one 2m out about 2%.</summary>
        private const float RoomBlendEpsilon = 0.1f;

        /// <summary>
        /// Samples the probe field at <paramref name="worldPosition"/>, blending across every room
        /// near enough to matter.
        ///
        /// This used to return the FIRST room whose footprint contained the point, and the nearest
        /// room otherwise. Both halves of that are hard switches, and rooms do not tile the world
        /// continuously: the bake insets each grid from its walls, so there is a band around every
        /// wall and across every doorway that no grid contains. An object in that band read some
        /// other room's clamped edge value, and moving it a few centimetres flipped which room won —
        /// a jump from lit to black with nothing in between, which is exactly what it looked like.
        ///
        /// Inverse-square weighting removes the switch: the room a point stands in dominates, a
        /// neighbour fades in as the point approaches it, and at a doorway the two cross over evenly.
        /// The result is continuous everywhere, so light on a dynamic object now changes as smoothly
        /// as the object moves.
        /// </summary>
        public static bool TryFindSample(Vector3 worldPosition, out SphericalHarmonicsL2 sh)
        {
            var accumulated = new SphericalHarmonicsL2();
            accumulated.Clear();

            RoomLightProbeData nearest = null;
            float nearestSqr = float.PositiveInfinity;
            float totalWeight = 0f;

            for (int i = 0; i < Active.Count; i++)
            {
                RoomLightProbeData room = Active[i];
                if (room == null || room._applied == null || !room._applied.IsValid)
                    continue;

                float sqr = room.SqrDistanceToGridXZ(worldPosition);
                if (sqr < nearestSqr)
                {
                    nearestSqr = sqr;
                    nearest = room;
                }

                if (sqr > RoomBlendRadiusM * RoomBlendRadiusM)
                    continue;

                if (!room.TrySample(worldPosition, out SphericalHarmonicsL2 roomSh))
                    continue;

                float weight = 1f / (sqr + RoomBlendEpsilon);
                for (int channel = 0; channel < 3; channel++)
                for (int coefficient = 0; coefficient < 9; coefficient++)
                    accumulated[channel, coefficient] += roomSh[channel, coefficient] * weight;

                totalWeight += weight;
            }

            if (totalWeight > 0f)
            {
                float normalise = 1f / totalWeight;
                for (int channel = 0; channel < 3; channel++)
                for (int coefficient = 0; coefficient < 9; coefficient++)
                    accumulated[channel, coefficient] *= normalise;

                sh = accumulated;
                return true;
            }

            // Nothing within blend range — a point far outside the level. Clamped edge light from the
            // closest room is still better than black.
            if (nearest != null)
                return nearest.TrySample(worldPosition, out sh);

            sh = default;
            return false;
        }

        /// <summary>Trilinearly interpolates the applied grid, clamped to its bounds, and rotates the
        /// result out of room-local space into world space.</summary>
        public bool TrySample(Vector3 worldPosition, out SphericalHarmonicsL2 sh)
        {
            sh = default;
            Variant variant = _applied;
            if (variant == null || !variant.IsValid)
                return false;

            Vector3 local = transform.InverseTransformPoint(worldPosition);
            Vector3 cell = new Vector3(
                SafeDivide(local.x - variant.originLocal.x, variant.cellSize.x),
                SafeDivide(local.y - variant.originLocal.y, variant.cellSize.y),
                SafeDivide(local.z - variant.originLocal.z, variant.cellSize.z));

            SplitCell(cell.x, variant.dimensions.x, out int x0, out int x1, out float tx);
            SplitCell(cell.y, variant.dimensions.y, out int y0, out int y1, out float ty);
            SplitCell(cell.z, variant.dimensions.z, out int z0, out int z1, out float tz);

            Span<float> accumulated = stackalloc float[CoefficientsPerProbe];
            for (int i = 0; i < CoefficientsPerProbe; i++)
                accumulated[i] = 0f;

            AccumulateProbe(variant, x0, y0, z0, (1f - tx) * (1f - ty) * (1f - tz), accumulated);
            AccumulateProbe(variant, x1, y0, z0, tx * (1f - ty) * (1f - tz), accumulated);
            AccumulateProbe(variant, x0, y1, z0, (1f - tx) * ty * (1f - tz), accumulated);
            AccumulateProbe(variant, x1, y1, z0, tx * ty * (1f - tz), accumulated);
            AccumulateProbe(variant, x0, y0, z1, (1f - tx) * (1f - ty) * tz, accumulated);
            AccumulateProbe(variant, x1, y0, z1, tx * (1f - ty) * tz, accumulated);
            AccumulateProbe(variant, x0, y1, z1, (1f - tx) * ty * tz, accumulated);
            AccumulateProbe(variant, x1, y1, z1, tx * ty * tz, accumulated);

            sh = BuildRotatedSH(accumulated, transform.rotation);
            return true;
        }

        /// <summary>
        /// Squared XZ distance from the grid's footprint, 0 when the point is over it.
        ///
        /// Deliberately ignores Y. The grid's lowest layer sits half a metre above the floor, so a
        /// Y test would report that a room does not contain an object LYING ON ITS OWN FLOOR — which
        /// is how a dropped item ended up sampling a different room entirely, and flipping to another
        /// one (hard colour jump) as it rolled. Vertical position is handled by clamping inside
        /// <see cref="TrySample"/>, which is the correct treatment: the nearest layer is the best
        /// answer for a point above or below the field.
        ///
        /// Distance is measured to the grid's footprint rather than to its centre, so a 100x50m room
        /// does not lose the point standing inside it to a small room 40m away.
        ///
        /// The footprint is widened by half a cell on each side, because the lattice stops short of
        /// the walls (the bake insets it so no probe ends up inside the shell). Without that, an
        /// object set down against a wall — which is where the dark corners are — measured as OUTSIDE
        /// its own room and could score closer to the room on the far side of that wall.
        /// </summary>
        private float SqrDistanceToGridXZ(Vector3 worldPosition)
        {
            Variant variant = _applied;
            Vector3 local = transform.InverseTransformPoint(worldPosition);
            Vector3 min = variant.originLocal;
            Vector3 max = min + Vector3.Scale(variant.cellSize, variant.dimensions - Vector3Int.one);

            float marginX = variant.cellSize.x * 0.5f;
            float marginZ = variant.cellSize.z * 0.5f;

            float dx = Mathf.Max(min.x - marginX - local.x, 0f, local.x - (max.x + marginX));
            float dz = Mathf.Max(min.z - marginZ - local.z, 0f, local.z - (max.z + marginZ));
            return dx * dx + dz * dz;
        }

        private static void AccumulateProbe(
            Variant variant, int x, int y, int z, float weight, Span<float> accumulated)
        {
            if (weight <= 0f)
                return;

            int probe = x + variant.dimensions.x * (y + variant.dimensions.y * z);
            int start = probe * CoefficientsPerProbe;
            for (int i = 0; i < CoefficientsPerProbe; i++)
                accumulated[i] += variant.coefficients[start + i] * weight;
        }

        /// <summary>
        /// Rebuilds a SphericalHarmonicsL2 from the flat L0/L1 store, rotating the L1 band into world
        /// space. The three L1 coefficients of a channel are the components of a direction vector in
        /// Unity's basis order [1, y, z, x], so the rotation is literally the room's rotation applied
        /// to (x, y, z) — no SH rotation matrix needed at this band.
        /// </summary>
        private static SphericalHarmonicsL2 BuildRotatedSH(ReadOnlySpan<float> flat, Quaternion rotation)
        {
            var sh = new SphericalHarmonicsL2();
            sh.Clear();

            for (int channel = 0; channel < 3; channel++)
            {
                int b = channel * 4;
                Vector3 l1 = rotation * new Vector3(flat[b + 3], flat[b + 1], flat[b + 2]);

                sh[channel, 0] = flat[b];
                sh[channel, 1] = l1.y;
                sh[channel, 2] = l1.z;
                sh[channel, 3] = l1.x;
            }

            return sh;
        }

        private static float SafeDivide(float value, float divisor) =>
            Mathf.Abs(divisor) < 1e-6f ? 0f : value / divisor;

        /// <summary>Clamps a fractional cell coordinate to the grid and splits it into the two
        /// bracketing indices plus the blend factor between them.</summary>
        private static void SplitCell(float coordinate, int count, out int low, out int high, out float t)
        {
            if (count <= 1)
            {
                low = 0;
                high = 0;
                t = 0f;
                return;
            }

            float clamped = Mathf.Clamp(coordinate, 0f, count - 1);
            low = Mathf.Clamp(Mathf.FloorToInt(clamped), 0, count - 2);
            high = low + 1;
            t = clamped - low;
        }

#if UNITY_EDITOR
        public void StoreVariants(Variant[] variants) => _variants = variants;
#endif
    }
}
