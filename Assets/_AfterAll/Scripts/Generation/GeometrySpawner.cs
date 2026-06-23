using System.Collections.Generic;
using UnityEngine;

namespace AfterAll.Generation
{
    /// <summary>
    /// Converts a ChunkSpec into actual scene GameObjects using Harun's prefabs.
    ///
    /// ── Mesh coordinate analysis (Assets/Art/my/backrooms modular.fbx) ──────────
    ///
    /// All prefabs use rotation Euler(−90, 0, 0), mapping local axes to world axes:
    ///   local X → world X    (right)
    ///   local Y → world −Z   (length / depth, mesh extends in −Z from transform.Z)
    ///   local Z → world +Y   (height / thickness, mesh extends upward from transform.Y)
    ///
    /// Measured local mesh extents:
    ///   Floor / Ceiling  — local X: 0.04 u,  local Y: 0.04 u,  local Z: 0.004 u  (thin slab)
    ///   Wall             — local X: 0.004 u (thin), local Y: 0.04 u, local Z: 0.04 u
    ///
    /// Derived scale factors:
    ///   kPlanarUnitsPerMeter  = 25    (1 / 0.04)  — floor/ceil XZ area, wall length + height
    ///   kSlabUnitsPerMeter    = 250   (1 / 0.004) — slab thickness, wall X-thickness
    ///
    /// Horizontal walls (boundary.IsHorizontal) additionally need Euler(−90, 90, 0),
    /// which rotates the local-Y "length" direction from world −Z onto world −X.
    /// ─────────────────────────────────────────────────────────────────────────────
    /// </summary>
    public static class GeometrySpawner
    {
        private const float kPlanarUnitsPerMeter = 25f;   // 1 / 0.04
        private const float kSlabUnitsPerMeter   = 250f;  // 1 / 0.004

        // Euler(−90, 0, 0)  — default for floor, ceiling, vertical walls
        private static readonly Quaternion kFlat     = Quaternion.Euler(-90f, 0f,  0f);
        // Euler(−90, 90, 0) — horizontal walls (runs along world X axis)
        private static readonly Quaternion kFlatY90  = Quaternion.Euler(-90f, 90f, 0f);

        /// <summary>
        /// Instantiates all geometry for one chunk under <paramref name="parent"/>.
        /// </summary>
        /// <param name="spec">Layout data produced by WallLayout.</param>
        /// <param name="config">MapConfig that holds the prefab references.</param>
        /// <param name="worldOrigin">
        /// XZ world-space offset of this chunk's local (0,0) corner.
        /// For the first test chunk pass Vector2.zero.
        /// </param>
        /// <param name="parent">All spawned objects become children of this transform.</param>
        public static List<GameObject> Spawn(
            ChunkSpec spec, MapConfig config, Vector2 worldOrigin, Transform parent)
        {
            var spawned = new List<GameObject>(64);

            if (config.FloorPrefab == null || config.CeilingPrefab == null || config.WallPrefab == null)
            {
                Debug.LogWarning("[GeometrySpawner] One or more prefab references are missing on MapConfig.");
                return spawned;
            }

            SpawnSlab(config.FloorPrefab,
                      spec.ChunkBounds, worldOrigin,
                      bottomY:  -spec.SlabThickness,
                      thickness: spec.SlabThickness,
                      parent, spawned,
                      "Floor");

            SpawnSlab(config.CeilingPrefab,
                      spec.ChunkBounds, worldOrigin,
                      bottomY:  spec.WallHeight,
                      thickness: spec.SlabThickness,
                      parent, spawned,
                      "Ceiling");

            foreach (var wall in spec.Walls)
                SpawnWallSegments(wall, spec.WallHeight, config.WallThickness,
                                  worldOrigin, config.WallPrefab, parent, spawned);

            return spawned;
        }

        // ──────────────────────────────────────────────────────────────────────────
        //  Slab (floor / ceiling)
        // ──────────────────────────────────────────────────────────────────────────

        private static void SpawnSlab(
            GameObject prefab, Rect chunkBounds, Vector2 worldOrigin,
            float bottomY, float thickness,
            Transform parent, List<GameObject> spawned, string label)
        {
            // Local XZ extent → scale
            float w = chunkBounds.width;
            float d = chunkBounds.height; // height of Rect = depth in world Z

            var scale = new Vector3(
                w         * kPlanarUnitsPerMeter,
                d         * kPlanarUnitsPerMeter,
                thickness * kSlabUnitsPerMeter);

            // Mesh origin = min-X, bottom-Y, max-Z corner (extends in +X, +Y, −Z)
            var pos = new Vector3(
                worldOrigin.x + chunkBounds.xMin,
                bottomY,
                worldOrigin.y + chunkBounds.yMax); // yMax = local maxZ; mesh extends −Z from here

            var go = Spawn(prefab, pos, kFlat, scale, parent);
            go.name = label;
            spawned.Add(go);
        }

        // ──────────────────────────────────────────────────────────────────────────
        //  Wall segments (one boundary → 1..N solid pieces around the openings)
        // ──────────────────────────────────────────────────────────────────────────

        private static void SpawnWallSegments(
            WallSpec wall, float wallHeight, float wallThickness,
            Vector2 worldOrigin, GameObject prefab, Transform parent, List<GameObject> spawned)
        {
            float totalLength = wall.Boundary.Length;
            float cursor      = 0f;
            var   openings    = wall.Openings;

            for (int i = 0; i <= openings.Count; i++)
            {
                float segEnd    = i < openings.Count ? openings[i].Offset : totalLength;
                float segLength = segEnd - cursor;

                if (segLength > 0.05f) // skip hairline slivers
                {
                    SpawnWallSegment(wall.Boundary, cursor, segLength,
                                     wallHeight, wallThickness, worldOrigin,
                                     prefab, parent, spawned);
                }

                if (i < openings.Count)
                    cursor = openings[i].EndOffset;
            }
        }

        private static void SpawnWallSegment(
            BspBoundary boundary, float segOffset, float segLength,
            float wallHeight, float wallThickness,
            Vector2 worldOrigin, GameObject prefab, Transform parent, List<GameObject> spawned)
        {
            // Scale is the same formula regardless of wall orientation:
            //   scale.x  ← wall X-thickness   (thin axis, kSlabUnitsPerMeter)
            //   scale.y  ← wall length         (planar, kPlanarUnitsPerMeter)
            //   scale.z  ← wall height         (planar, kPlanarUnitsPerMeter)
            var scale = new Vector3(
                wallThickness * kSlabUnitsPerMeter,
                segLength     * kPlanarUnitsPerMeter,
                wallHeight    * kPlanarUnitsPerMeter);

            Vector3    pos;
            Quaternion rot;

            if (boundary.IsVertical)
            {
                // ── Vertical boundary (runs along Z, constant X) ───────────────
                // Rotation Euler(−90, 0, 0):
                //   scale.x → world X (thickness, centred on splitX)
                //   scale.y → world −Z (length, mesh extends −Z from transform.Z)
                //   scale.z → world +Y (height, from transform.Y upward)
                float splitX  = boundary.Start.x;
                float segZMin = boundary.Start.y + segOffset;
                float segZMax = segZMin + segLength;

                pos = new Vector3(
                    worldOrigin.x + splitX - wallThickness * 0.5f, // centred on boundary line
                    0f,
                    worldOrigin.y + segZMax); // mesh extends −Z from here to segZMin
                rot = kFlat;
            }
            else
            {
                // ── Horizontal boundary (runs along X, constant Z) ────────────
                // Rotation Euler(−90, 90, 0):
                //   scale.x → world −Z (thickness, centred on splitZ)
                //   scale.y → world −X (length, mesh extends −X from transform.X)
                //   scale.z → world +Y (height, from transform.Y upward)
                float splitZ  = boundary.Start.y;
                float segXMin = boundary.Start.x + segOffset;
                float segXMax = segXMin + segLength;

                pos = new Vector3(
                    worldOrigin.x + segXMax,                         // mesh extends −X from here to segXMin
                    0f,
                    worldOrigin.y + splitZ + wallThickness * 0.5f);  // centred on boundary line
                rot = kFlatY90;
            }

            var go = Spawn(prefab, pos, rot, scale, parent);
            go.name = boundary.IsVertical ? "Wall_V" : "Wall_H";
            spawned.Add(go);
        }

        // ──────────────────────────────────────────────────────────────────────────
        //  Helper
        // ──────────────────────────────────────────────────────────────────────────

        private static GameObject Spawn(
            GameObject prefab, Vector3 pos, Quaternion rot, Vector3 scale, Transform parent)
        {
            var go = Object.Instantiate(prefab, pos, rot, parent);
            go.transform.localScale = scale;
            return go;
        }
    }
}
