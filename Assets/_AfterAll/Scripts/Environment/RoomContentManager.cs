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

            // Every room registers its lightmaps here; without the batch each one would push the whole
            // (growing) array to Unity and force a scene-wide rebind plus texture load. A throw would
            // leave the depth raised, but ResetForNewFloor zeroes it on the next build.
            RoomLightmapData.BeginBatch();

            foreach (RoomInstance room in _connector.LevelRoot.GetComponentsInChildren<RoomInstance>())
            {
                Transform content = room.transform.Find("Content");
                Transform selectedPreset = null;

                // Rooms without a Content root (the elevator cabin: no presets, no loot) skip preset
                // selection entirely, but still fall through to the lightmap apply below — they must,
                // since RoomLightmapData.Awake() only blanks its renderers' lightmapIndex and waits for
                // this loop to call ApplyVariant. An earlier version of this method returned early on
                // `content == null`, which silently left the elevator riding on ambient forever, baked
                // data or not.
                if (content != null)
                {
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

                    // Prop placement alternatives (e.g. "DuckPropPositions") nested anywhere under the
                    // winning preset. Same mechanic as the preset pick, walked in hierarchy order so the
                    // shared rng is consumed deterministically for a given seed.
                    if (selectedPreset != null)
                    {
                        foreach (WeightedRandomGroup nested in selectedPreset.GetComponentsInChildren<WeightedRandomGroup>(true))
                            nested.Activate(rng);
                    }

                    // Loot last of the three, because it reads the spawn points that the preset pick
                    // and the nested prop alternatives just decided the existence of — a point under
                    // a losing alternative is inactive by now and correctly ignored.
                    RoomLootPlacer.Populate(room, _settings, rng);

                    if (_settings.LogActivation)
                    {
                        string presetName = selectedPreset != null ? selectedPreset.name : "none";
                        Debug.Log($"[RoomContent] {room.name} preset={presetName} seed={roomSeed}", content);
                    }
                }

                // The room is baked once per preset option, so the lightmap can only be chosen after
                // the winner is known — until this call the room rides on ambient. Runs for every room
                // under LevelRoot, Content root or not.
                string variantName = selectedPreset != null ? selectedPreset.name : string.Empty;
                if (room.TryGetComponent(out RoomLightmapData lightmaps) && lightmaps.HasBakedData)
                    lightmaps.ApplyVariant(variantName);

                // Same reasoning for the probe field: it is baked per preset too, and everything the
                // lightmap deliberately excludes (loot, props, the runtime-scaled door walls) reads
                // its light from here.
                if (room.TryGetComponent(out RoomLightProbeData probes) && probes.HasBakedData)
                    probes.ApplyVariant(variantName);
            }

            RoomLightmapData.EndBatch();

            RefreshOpenWalls();

            // Last: the probe grids are now resolved AND RefreshOpenWalls has moved every door-wall
            // piece to its final position, so a sample taken here is the first one that is actually
            // correct. Objects sampled earlier (on spawn) would have used the prefab-local pose.
            ProbeLitRenderer.RefreshAll(_connector.LevelRoot);
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
