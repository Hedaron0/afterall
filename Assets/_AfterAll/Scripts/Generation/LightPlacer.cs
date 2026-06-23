using System.Collections.Generic;
using UnityEngine;

namespace AfterAll.Generation
{
    /// <summary>
    /// Places FluorescentPanel prefabs in a uniform grid across the ceiling.
    /// Some positions are randomly culled to create the dark-patch Backrooms feel.
    ///
    /// Uses a dedicated child Rng (Derive(2) in Chunk) so the light pattern is
    /// fully independent of BSP splits and wall openings.
    /// </summary>
    public static class LightPlacer
    {
        public static void Place(
            ChunkSpec spec, MapConfig config, Rng rng,
            Vector2 worldOrigin, Transform parent, List<GameObject> spawned)
        {
            if (config.LightPanelPrefab == null)
            {
                Debug.LogWarning("[LightPlacer] LightPanelPrefab is not assigned on MapConfig.");
                return;
            }

            float spacing = config.LightSpacing;
            Rect  bounds  = spec.ChunkBounds;

            // Panel sits just below the ceiling slab (panel Y scale is ~0.007m, negligible).
            float panelY = spec.WallHeight - 0.004f;

            // Fit as many lights as possible, then centre the grid in the chunk.
            int countX = Mathf.Max(1, Mathf.FloorToInt(bounds.width  / spacing));
            int countZ = Mathf.Max(1, Mathf.FloorToInt(bounds.height / spacing));

            float startX = bounds.xMin + (bounds.width  - (countX - 1) * spacing) * 0.5f;
            float startZ = bounds.yMin + (bounds.height - (countZ - 1) * spacing) * 0.5f;

            for (int ix = 0; ix < countX; ix++)
            {
                for (int iz = 0; iz < countZ; iz++)
                {
                    if (rng.Chance(config.LightDarkChance))
                        continue; // leave this spot dark

                    float wx = worldOrigin.x + startX + ix * spacing;
                    float wz = worldOrigin.y + startZ + iz * spacing;

                    var go = Object.Instantiate(
                        config.LightPanelPrefab,
                        new Vector3(wx, panelY, wz),
                        Quaternion.identity,
                        parent);

                    go.name = "Light";
                    spawned.Add(go);
                }
            }
        }
    }
}
