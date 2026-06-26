using System.Collections.Generic;
using UnityEngine;

namespace AfterAll.Generation.FloorPlan
{
  /// <summary>
  /// Spawns scaled primitive cubes for floor, ceiling, merged wall blocks, pillars, and lights.
  /// </summary>
  public static class FloorPlanGeometrySpawner
  {
    public static List<GameObject> Spawn(
      FloorPlanResult result, FloorPlanConfig config, Vector2 worldOrigin, Transform parent)
    {
      var spawned = new List<GameObject>(result.WallBlocks.Count + 32);
      float chunkSize = config.ChunkSizeMetres;

      SpawnSlab(config.FloorPrefab, config.FloorMaterial, "Floor",
        worldOrigin, chunkSize, config.SlabThickness, -config.SlabThickness, parent, spawned, keepCollider: true);

      SpawnSlab(config.CeilingPrefab, config.CeilingMaterial, "Ceiling",
        worldOrigin, chunkSize, config.SlabThickness, config.WallHeight, parent, spawned, keepCollider: false);

      foreach (var wall in result.WallBlocks)
        SpawnWallBlock(wall, config, worldOrigin, parent, spawned);

      foreach (var pillar in result.Pillars)
        SpawnPillar(pillar, config, worldOrigin, parent, spawned);

      if (result.Lights != null)
      {
        foreach (var light in result.Lights)
          SpawnLight(light, config, worldOrigin, parent, spawned);
      }

      return spawned;
    }

    private static void SpawnSlab(
      GameObject prefab, Material material, string label,
      Vector2 worldOrigin, float chunkSize, float thickness, float bottomY,
      Transform parent, List<GameObject> spawned, bool keepCollider)
    {
      float centerX = worldOrigin.x + chunkSize * 0.5f;
      float centerZ = worldOrigin.y + chunkSize * 0.5f;
      float centerY = bottomY + thickness * 0.5f;

      var go = CreateBlock(prefab, material, parent, keepCollider);
      go.name = label;
      go.transform.position = new Vector3(centerX, centerY, centerZ);
      go.transform.localScale = new Vector3(chunkSize, thickness, chunkSize);
      spawned.Add(go);
    }

    private static void SpawnWallBlock(
      WallBlockSpec wall, FloorPlanConfig config, Vector2 worldOrigin,
      Transform parent, List<GameObject> spawned)
    {
      var go = CreateBlock(config.WallBlockPrefab, config.WallMaterial, parent, keepCollider: true);
      go.name = "Wall";
      go.transform.position = new Vector3(
        worldOrigin.x + wall.CenterX,
        config.WallHeight * 0.5f,
        worldOrigin.y + wall.CenterZ);
      go.transform.localScale = new Vector3(
        wall.WidthMetres,
        config.WallHeight,
        wall.DepthMetres);
      spawned.Add(go);
    }

    private static void SpawnPillar(
      PillarBlockSpec pillar, FloorPlanConfig config, Vector2 worldOrigin,
      Transform parent, List<GameObject> spawned)
    {
      float size = config.PillarFootprint;
      var go = CreateBlock(config.PillarPrefab ?? config.WallBlockPrefab, config.WallMaterial, parent, keepCollider: true);
      go.name = "Pillar";
      go.transform.position = new Vector3(
        worldOrigin.x + pillar.CenterX,
        config.WallHeight * 0.5f,
        worldOrigin.y + pillar.CenterZ);
      go.transform.localScale = new Vector3(size, config.WallHeight, size);
      spawned.Add(go);
    }

    private static void SpawnLight(
      LightCellSpec light, FloorPlanConfig config, Vector2 worldOrigin,
      Transform parent, List<GameObject> spawned)
    {
      if (config.LightPanelPrefab == null)
        return;

      var go = Object.Instantiate(
        config.LightPanelPrefab,
        new Vector3(
          worldOrigin.x + light.LocalX,
          config.WallHeight,
          worldOrigin.y + light.LocalZ),
        Quaternion.identity,
        parent);
      go.name = "Light";
      spawned.Add(go);
    }

    private static GameObject CreateBlock(GameObject prefab, Material material, Transform parent, bool keepCollider)
    {
      GameObject go;
      if (prefab != null)
      {
        go = Object.Instantiate(prefab, parent);
      }
      else
      {
        go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.transform.SetParent(parent, false);
        if (!keepCollider)
        {
          var col = go.GetComponent<Collider>();
          if (col != null)
            Object.Destroy(col);
        }
      }

      if (material != null)
      {
        var renderer = go.GetComponent<Renderer>();
        if (renderer != null)
          renderer.sharedMaterial = material;
      }

      return go;
    }
  }
}
