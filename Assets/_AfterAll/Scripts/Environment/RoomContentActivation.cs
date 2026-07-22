using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AfterAll.Environment
{
    /// <summary>
    /// Stateless activation for the authored Random loot pool (independent per-item spawn chance,
    /// multiple items can be picked together). Preset selection and prop-alternative selection are
    /// handled by <see cref="WeightedRandomGroup"/> instead — see RoomContentManager.
    /// </summary>
    public static class RoomContentActivation
    {
        public static List<Transform> ApplyRandomPool(
            Transform contentRoot,
            RoomContentSettings settings,
            System.Random rng)
        {
            var selectedRandom = new List<Transform>();
            if (contentRoot == null || settings == null)
                return selectedRandom;

            Transform randomPool = contentRoot.Find("Random");
            if (randomPool == null)
                return selectedRandom;

            int pickCount = GetRandomPickCount(settings, rng);
            selectedRandom = PickRandomItems(randomPool, rng, pickCount);

            bool anyPicked = selectedRandom.Count > 0;
            randomPool.gameObject.SetActive(anyPicked);

            for (int i = 0; i < randomPool.childCount; i++)
            {
                Transform child = randomPool.GetChild(i);
                child.gameObject.SetActive(selectedRandom.Contains(child));
            }

            if (settings.LogActivation)
            {
                string randomNames = selectedRandom.Count > 0
                    ? string.Join(", ", selectedRandom.Select(t => t.name))
                    : "none";
                Debug.Log($"[RoomContent] {contentRoot.name} random=[{randomNames}]", contentRoot);
            }

            return selectedRandom;
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
            int pickCount)
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
    }
}
