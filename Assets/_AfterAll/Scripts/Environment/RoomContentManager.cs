using System;
using System.Collections.Generic;
using AfterAll.Items;
using UnityEngine;

namespace AfterAll.Environment
{
    /// <summary>
    /// Central prop-generation controller on RoomLevelGen. Applies shared settings to every placed room's Content root.
    /// Preset choice and prop placement alternatives are driven by WeightedRandomGroup components authored on the
    /// prefabs themselves (Content root = preset group, any nested group = a prop alternative), all sharing one
    /// deterministic per-room System.Random derived from the level seed.
    /// </summary>
    public class RoomContentManager : MonoBehaviour
    {
        [SerializeField] private RoomConnector _connector;
        [SerializeField] private RoomContentSettings _settings;
        [SerializeField] private bool _activateAfterBuild = true;

        private void Awake()
        {
            if (_connector == null)
                _connector = GetComponent<RoomConnector>();
        }

        public void ActivateAll(int levelSeed)
        {
            if (!_activateAfterBuild || _settings == null)
                return;

            if (_connector == null || _connector.LevelRoot == null)
            {
                Debug.LogWarning("[RoomContent] No LevelRoot found — skipping content activation.", this);
                return;
            }

            var usedPresetsPerPrefab = new Dictionary<string, HashSet<string>>();

            foreach (RoomInstance room in _connector.LevelRoot.GetComponentsInChildren<RoomInstance>())
            {
                Transform content = room.transform.Find("Content");
                if (content == null)
                    continue;

                Vector3 position = room.transform.position;
                int positionKey = HashCode.Combine(
                    Mathf.RoundToInt(position.x * 100f),
                    Mathf.RoundToInt(position.y * 100f),
                    Mathf.RoundToInt(position.z * 100f));
                int roomSeed = HashCode.Combine(levelSeed, positionKey);
                var rng = new System.Random(roomSeed);

                string prefabId = room.PrefabId;
                if (string.IsNullOrEmpty(prefabId))
                    prefabId = room.name.Replace("(Clone)", "").Trim();

                if (!usedPresetsPerPrefab.TryGetValue(prefabId, out HashSet<string> usedPresets))
                {
                    usedPresets = new HashSet<string>();
                    usedPresetsPerPrefab[prefabId] = usedPresets;
                }

                ApplyLootDepthWeighting(content, room.GraphDepth);

                Transform selectedPreset = null;
                if (!content.TryGetComponent(out WeightedRandomGroup presetGroup))
                {
                    Transform presetContainer = content.Find("Preset");
                    if (presetContainer != null)
                        presetContainer.TryGetComponent(out presetGroup);
                }
                if (presetGroup != null)
                {
                    selectedPreset = presetGroup.Activate(rng, usedPresets);
                    if (selectedPreset != null)
                        usedPresets.Add(selectedPreset.name);
                }

                // The room is baked once per preset option, so the lightmap can only be chosen after
                // the winner is known (RoomLightmapData.Awake applied a placeholder on spawn).
                if (room.TryGetComponent(out RoomLightmapData lightmaps) && lightmaps.HasBakedData)
                    lightmaps.ApplyVariant(selectedPreset != null ? selectedPreset.name : string.Empty);

                RoomContentActivation.ApplyRandomPool(content, _settings, rng);

                // Prop placement alternatives (e.g. "DuckPropPositions") nested anywhere under the
                // winning preset. Same mechanic as the preset pick, walked in hierarchy order so the
                // shared rng is consumed deterministically for a given seed.
                if (selectedPreset != null)
                {
                    foreach (WeightedRandomGroup nested in selectedPreset.GetComponentsInChildren<WeightedRandomGroup>(true))
                        nested.Activate(rng);
                }

                if (_settings.LogActivation)
                {
                    string presetName = selectedPreset != null ? selectedPreset.name : "none";
                    Debug.Log($"[RoomContent] {room.name} preset={presetName} seed={roomSeed}", content);
                }
            }

            RefreshOpenWalls();
        }

        /// <summary>
        /// Thin pre-pass over RoomContentActivation: scales Loot-category (e.g. Echo) world-items in
        /// the Random pool by how far this room sits from the hub/elevator (RoomInstance.GraphDepth),
        /// so loot skews rarer/cheaper near the elevator and richer deep in the floor.
        /// </summary>
        private void ApplyLootDepthWeighting(Transform content, int graphDepth)
        {
            Transform randomPool = content.Find("Random");
            if (randomPool == null)
                return;

            int depth = Mathf.Max(0, graphDepth);
            float t = Mathf.Clamp01(depth / (float)_settings.LootChanceFarDepth);
            float multiplier = Mathf.Lerp(_settings.LootChanceNearMultiplier, _settings.LootChanceFarMultiplier, t);

            for (int i = 0; i < randomPool.childCount; i++)
            {
                Transform child = randomPool.GetChild(i);
                if (!child.TryGetComponent(out RoomContentPickable pickable))
                    continue;
                if (!child.TryGetComponent(out WorldItem worldItem))
                    continue;
                if (worldItem.Item == null || worldItem.Item.Category != ItemCategory.Loot)
                    continue;

                pickable.SetRuntimeChanceMultiplier(multiplier);
            }
        }

        private void RefreshOpenWalls()
        {
            if (_connector == null || _connector.LevelRoot == null)
                return;

            foreach (RoomInstance room in _connector.LevelRoot.GetComponentsInChildren<RoomInstance>())
            {
                foreach (WallGapController wall in room.Walls)
                {
                    if (wall != null && wall.hasOpening)
                        wall.ApplyGap();
                }
            }
        }
    }
}
