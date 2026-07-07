using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AfterAll.Environment
{
    /// <summary>
    /// Stateless activation for authored Content folders (presets + Random pool).
    /// </summary>
    public static class RoomContentActivation
    {
        public static void Apply(Transform contentRoot, RoomContentSettings settings, int seed, RoomInstance room = null)
        {
            if (contentRoot == null || settings == null)
                return;

            var layout = DiscoverLayout(contentRoot);
            if (layout.presets.Count == 0 && layout.randomPool == null)
                return;

            var rng = new System.Random(seed);
            List<Bounds> corridors = RoomOpeningConflict.GetOpenCorridors(room);
            (Transform selectedPreset, string presetRetryNote) = PickPresetWithRetry(
                layout.presets,
                settings,
                rng,
                contentRoot,
                room,
                corridors);

            foreach (Transform preset in layout.presets)
                preset.gameObject.SetActive(preset == selectedPreset);

            int pickCount = GetRandomPickCount(settings, rng);
            List<Transform> selectedRandom = PickRandomItems(layout.randomPool, rng, pickCount, corridors);

            if (layout.randomPool != null)
            {
                bool anyPicked = selectedRandom.Count > 0;
                layout.randomPool.gameObject.SetActive(anyPicked);

                for (int i = 0; i < layout.randomPool.childCount; i++)
                {
                    Transform child = layout.randomPool.GetChild(i);
                    child.gameObject.SetActive(selectedRandom.Contains(child));
                }
            }

            if (settings.LogActivation)
            {
                string roomName = room != null ? room.name : contentRoot.name;
                string presetName = selectedPreset != null ? selectedPreset.name : "none";
                string randomNames = selectedRandom.Count > 0
                    ? string.Join(", ", selectedRandom.Select(t => t.name))
                    : "none";
                string retrySuffix = string.IsNullOrEmpty(presetRetryNote) ? string.Empty : $" ({presetRetryNote})";
                Debug.Log(
                    $"[RoomContent] {roomName} preset={presetName}{retrySuffix} random=[{randomNames}] seed={seed}",
                    contentRoot);
            }
        }

        private static Layout DiscoverLayout(Transform contentRoot)
        {
            var presets = new List<Transform>();
            Transform randomPool = null;

            for (int i = 0; i < contentRoot.childCount; i++)
            {
                Transform child = contentRoot.GetChild(i);
                if (int.TryParse(child.name, out _))
                    presets.Add(child);
                else if (child.name == "Random")
                    randomPool = child;
            }

            presets.Sort((a, b) => int.Parse(a.name).CompareTo(int.Parse(b.name)));
            return new Layout(presets, randomPool);
        }

        private static (Transform preset, string retryNote) PickPresetWithRetry(
            IReadOnlyList<Transform> presets,
            RoomContentSettings settings,
            System.Random rng,
            Transform contentRoot,
            RoomInstance room,
            List<Bounds> corridors)
        {
            if (presets.Count == 0)
                return (null, null);

            List<Transform> candidates = BuildPresetCandidateOrder(presets, settings, rng, contentRoot);
            Transform firstChoice = candidates[0];

            if (room == null || corridors.Count == 0)
                return (firstChoice, null);

            foreach (Transform preset in candidates)
            {
                if (!RoomOpeningConflict.PresetConflicts(corridors, preset))
                {
                    string note = preset != firstChoice
                        ? $"retried from {firstChoice.name}"
                        : null;
                    return (preset, note);
                }
            }

            Debug.LogWarning(
                $"[RoomContent] All presets conflict with openings on {contentRoot.name}. Using {firstChoice.name}.",
                contentRoot);
            return (firstChoice, "all presets conflicted");
        }

        private static List<Transform> BuildPresetCandidateOrder(
            IReadOnlyList<Transform> presets,
            RoomContentSettings settings,
            System.Random rng,
            Transform contentRoot)
        {
            var order = new List<Transform>();
            var remaining = new List<Transform>(presets);

            int forced = settings.ForcePresetIndex;
            if (forced >= 0)
            {
                Transform forcedPreset = remaining.FirstOrDefault(p => int.Parse(p.name) == forced);
                if (forcedPreset != null)
                {
                    order.Add(forcedPreset);
                    remaining.Remove(forcedPreset);
                }
                else
                {
                    Debug.LogWarning(
                        $"[RoomContent] Force preset {forced} not found on {contentRoot.name}. Using weighted pick.",
                        contentRoot);
                }
            }

            while (remaining.Count > 0)
            {
                Transform pick = PickWeightedPreset(remaining, settings, rng);
                order.Add(pick);
                remaining.Remove(pick);
            }

            return order;
        }

        private static Transform PickWeightedPreset(
            IReadOnlyList<Transform> presets,
            RoomContentSettings settings,
            System.Random rng)
        {
            float[] weights = settings.PresetWeights;
            float totalWeight = 0f;
            var effectiveWeights = new float[presets.Count];

            for (int i = 0; i < presets.Count; i++)
            {
                float weight = i < weights.Length && weights[i] > 0f ? weights[i] : 1f;
                effectiveWeights[i] = weight;
                totalWeight += weight;
            }

            float roll = (float)rng.NextDouble() * totalWeight;
            float cumulative = 0f;

            for (int i = 0; i < presets.Count; i++)
            {
                cumulative += effectiveWeights[i];
                if (roll <= cumulative)
                    return presets[i];
            }

            return presets[presets.Count - 1];
        }

        private static int GetRandomPickCount(RoomContentSettings settings, System.Random rng)
        {
            int min = settings.RandomPickMin;
            int max = settings.RandomPickMax;
            if (max < min)
                (min, max) = (max, min);

            if (min == max)
                return min;

            return rng.Next(min, max + 1);
        }

        private static List<Transform> PickRandomItems(
            Transform randomPool,
            System.Random rng,
            int pickCount,
            List<Bounds> corridors)
        {
            var selected = new List<Transform>();
            if (randomPool == null || pickCount <= 0)
                return selected;

            var candidates = new List<Transform>();
            for (int i = 0; i < randomPool.childCount; i++)
                candidates.Add(randomPool.GetChild(i));

            Shuffle(candidates, rng);

            foreach (Transform candidate in candidates)
            {
                if (selected.Count >= pickCount)
                    break;

                float chance = 1f;
                if (candidate.TryGetComponent(out RoomContentPickable pickable))
                    chance = pickable.SpawnChance;

                if (rng.NextDouble() > chance)
                    continue;

                if (corridors.Count > 0 && RoomOpeningConflict.ItemConflicts(corridors, candidate))
                    continue;

                selected.Add(candidate);
            }

            return selected;
        }

        private static void Shuffle<T>(IList<T> list, System.Random rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        private readonly struct Layout
        {
            public readonly List<Transform> presets;
            public readonly Transform randomPool;

            public Layout(List<Transform> presets, Transform randomPool)
            {
                this.presets = presets;
                this.randomPool = randomPool;
            }
        }
    }
}
