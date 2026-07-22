using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AfterAll.Environment
{
    /// <summary>
    /// Attach to any GameObject to pick exactly one direct child by weight (mutually exclusive).
    /// Same component drives room content presets (Content root, NumericNamedChildren filter)
    /// and prop placement alternatives (e.g. a "DuckPropPositions" group, AllChildren filter).
    /// Groups nest freely — activating a parent group does not auto-activate nested groups;
    /// callers walk GetComponentsInChildren and Activate each one explicitly (see RoomContentManager).
    /// </summary>
    public class WeightedRandomGroup : MonoBehaviour
    {
        public enum CandidateFilter { AllChildren, NumericNamedChildren }

        [Serializable]
        public struct Option
        {
            public string label;
            [Range(0f, 1f)] public float weight;
        }

        [SerializeField] private CandidateFilter _candidateFilter = CandidateFilter.AllChildren;
        [SerializeField] private List<Option> _options = new List<Option>();

        [Tooltip("-1 = weighted random. Otherwise forces the candidate at this index (editor/debug testing).")]
        [SerializeField] private int _forceIndex = -1;

        public IReadOnlyList<Option> Options => _options;

        public List<Transform> GetCandidates()
        {
            var result = new List<Transform>();
            for (int i = 0; i < transform.childCount; i++)
            {
                Transform child = transform.GetChild(i);
                if (_candidateFilter == CandidateFilter.NumericNamedChildren && !int.TryParse(child.name, out _))
                    continue;
                result.Add(child);
            }

            if (_candidateFilter == CandidateFilter.NumericNamedChildren)
                result.Sort((a, b) => int.Parse(a.name).CompareTo(int.Parse(b.name)));

            return result;
        }

        /// <summary>Rebuilds _options to match the current candidate children, preserving weights by label where possible.</summary>
        public void SyncOptions()
        {
            List<Transform> candidates = GetCandidates();
            var synced = new List<Option>(candidates.Count);
            foreach (Transform c in candidates)
            {
                int existing = _options.FindIndex(o => o.label == c.name);
                float weight = existing >= 0
                    ? _options[existing].weight
                    : (candidates.Count > 0 ? 1f / candidates.Count : 1f);
                synced.Add(new Option { label = c.name, weight = weight });
            }
            _options = synced;
        }

        public void SetOptions(List<Option> options) => _options = options;

        public Transform Pick(System.Random rng, ISet<string> excludeLabels = null)
        {
            List<Transform> candidates = GetCandidates();
            if (candidates.Count == 0)
                return null;

            if (_forceIndex >= 0 && _forceIndex < candidates.Count)
                return candidates[_forceIndex];

            List<Transform> pool = excludeLabels != null && excludeLabels.Count > 0
                ? candidates.Where(c => !excludeLabels.Contains(c.name)).ToList()
                : candidates;
            if (pool.Count == 0)
                pool = candidates;

            float totalWeight = 0f;
            var effectiveWeights = new float[pool.Count];
            for (int i = 0; i < pool.Count; i++)
            {
                int optIdx = _options.FindIndex(o => o.label == pool[i].name);
                float w = optIdx >= 0 && _options[optIdx].weight > 0f ? _options[optIdx].weight : 1f;
                effectiveWeights[i] = w;
                totalWeight += w;
            }

            double roll = rng.NextDouble() * totalWeight;
            double cumulative = 0;
            for (int i = 0; i < pool.Count; i++)
            {
                cumulative += effectiveWeights[i];
                if (roll <= cumulative)
                    return pool[i];
            }
            return pool[pool.Count - 1];
        }

        /// <summary>Picks a candidate and SetActive-toggles all candidates accordingly. Returns the winner (or null if none).</summary>
        public Transform Activate(System.Random rng, ISet<string> excludeLabels = null)
        {
            Transform selected = Pick(rng, excludeLabels);
            foreach (Transform child in GetCandidates())
                child.gameObject.SetActive(child == selected);
            return selected;
        }

        public Transform Activate(int seed, ISet<string> excludeLabels = null)
            => Activate(new System.Random(seed), excludeLabels);

        /// <summary>Editor-only convenience pick using UnityEngine.Random, for the inspector's preview button.</summary>
        public Transform PreviewPickInEditor()
            => Activate(new System.Random(UnityEngine.Random.Range(int.MinValue, int.MaxValue)));

        private void OnValidate()
        {
            if (_options.Count != GetCandidates().Count)
                SyncOptions();
        }
    }
}
