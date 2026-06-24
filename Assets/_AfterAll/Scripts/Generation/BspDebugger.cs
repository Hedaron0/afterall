using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace AfterAll.Generation
{
    /// <summary>
    /// Editor-only debug visualiser for BSP partitioning and wall layout.
    /// Assign MapConfig — tweak wall thickness, openings, etc. and see the result
    /// in Scene view without spawning meshes.
    /// </summary>
    [AddComponentMenu("AfterAll/Generation/BSP Debugger")]
    public class BspDebugger : MonoBehaviour
    {
        [Header("Config")]
        [SerializeField] private MapConfig _config;

        [Tooltip("Override seed for quick iteration. -1 = use MapConfig.Seed.")]
        [SerializeField] private int _seedOverride = -1;

        [Header("Visibility")]
        [SerializeField] private bool _drawRooms           = true;
        [SerializeField] private bool _drawBoundaries      = false;
        [SerializeField] private bool _drawWalls             = true;
        [SerializeField] private bool _drawOpenings          = true;
        [SerializeField] private bool _drawWallHeight        = true;
        [SerializeField] private bool _applyConnectivityPass = true;
        [SerializeField] private bool _drawLabels            = true;

        [Header("Colours")]
        [SerializeField] private Color _roomShallowColor = new Color(0.25f, 0.85f, 0.35f, 0.30f);
        [SerializeField] private Color _roomDeepColor    = new Color(0.35f, 0.45f, 1.00f, 0.30f);
        [SerializeField] private Color _boundaryColor    = new Color(1.00f, 0.90f, 0.10f, 1.00f);
        [SerializeField] private Color _wallColor          = new Color(0.95f, 0.55f, 0.15f, 0.85f);
        [SerializeField] private Color _openingColor     = new Color(0.20f, 1.00f, 0.90f, 0.70f);
        [SerializeField] private Color _chunkBorderColor = new Color(0.00f, 0.90f, 1.00f, 1.00f);

        // ──────────────────────────────────────────────────────────────────────────
        //  Gizmo drawing
        // ──────────────────────────────────────────────────────────────────────────

        private void OnDrawGizmos()
        {
            if (_config == null) return;

            int seed = _seedOverride >= 0 ? _seedOverride : _config.Seed;
            var chunkBounds = new Rect(0f, 0f, _config.ChunkSize, _config.ChunkSize);
            var result = BspPartitioner.Partition(chunkBounds, _config, seed);

            Vector3 origin = transform.position;

            DrawChunkBorder(origin, _config.ChunkSize);

            if (_drawRooms)
                DrawRooms(origin, result, _config.MaxDepth);

            if (_drawBoundaries)
                DrawBoundaries(origin, result);

            if (_drawWalls)
                DrawWallLayout(origin, result);

#if UNITY_EDITOR
            if (_drawLabels)
                DrawLabels(origin, result, _config.MaxDepth);
#endif
        }

        private void DrawWallLayout(Vector3 origin, BspResult bsp)
        {
            int seed = _seedOverride >= 0 ? _seedOverride : _config.Seed;

            var rootRng = new Rng(seed);
            var wallRng = rootRng.Derive(1);

            var spec = WallLayout.Build(bsp, _config, wallRng);

            if (_applyConnectivityPass)
                spec = ConnectivityPass.Apply(bsp, spec, _config.OpeningMinWidth);

            float wallHeight = _drawWallHeight ? _config.WallHeight : 0f;

            WallGizmoDrawer.DrawChunk(
                origin,
                spec,
                _config.WallThickness,
                _wallColor,
                _openingColor,
                _drawOpenings,
                wallHeight);
        }

        private void DrawChunkBorder(Vector3 origin, float size)
        {
            Gizmos.color = _chunkBorderColor;
            DrawRectWire(origin, new Rect(0f, 0f, size, size));
        }

        private void DrawRooms(Vector3 origin, BspResult result, int maxDepth)
        {
            foreach (var room in result.Rooms)
            {
                float t = maxDepth > 0 ? Mathf.Clamp01(room.Depth / (float)maxDepth) : 0f;
                Gizmos.color = Color.Lerp(_roomShallowColor, _roomDeepColor, t);
                DrawRectFilled(origin, room.Bounds);
                Gizmos.color = Color.Lerp(_roomShallowColor, _roomDeepColor, t) * new Color(1, 1, 1, 3f);
                DrawRectWire(origin, room.Bounds);
            }
        }

        private void DrawBoundaries(Vector3 origin, BspResult result)
        {
            Gizmos.color = _boundaryColor;
            foreach (var boundary in result.Boundaries)
            {
                Gizmos.DrawLine(
                    ToWorld(origin, boundary.Start),
                    ToWorld(origin, boundary.End));
            }
        }

#if UNITY_EDITOR
        private void DrawLabels(Vector3 origin, BspResult result, int maxDepth)
        {
            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize  = 10,
                alignment = TextAnchor.MiddleCenter,
                normal    = { textColor = Color.white }
            };

            foreach (var room in result.Rooms)
            {
                var center = ToWorld(origin, room.Bounds.center);
                string label = $"{room.Width:F1}×{room.Height:F1}\nd={room.Depth}";
                Handles.Label(center, label, style);
            }
        }
#endif

        // ──────────────────────────────────────────────────────────────────────────
        //  Geometry helpers
        // ──────────────────────────────────────────────────────────────────────────

        private void DrawRectWire(Vector3 origin, Rect r)
        {
            var a = ToWorld(origin, new Vector2(r.xMin, r.yMin));
            var b = ToWorld(origin, new Vector2(r.xMax, r.yMin));
            var c = ToWorld(origin, new Vector2(r.xMax, r.yMax));
            var d = ToWorld(origin, new Vector2(r.xMin, r.yMax));
            Gizmos.DrawLine(a, b);
            Gizmos.DrawLine(b, c);
            Gizmos.DrawLine(c, d);
            Gizmos.DrawLine(d, a);
        }

        private void DrawRectFilled(Vector3 origin, Rect r)
        {
            // Gizmos has no native filled-rect; fake it with a flat cube gizmo
            var center = ToWorld(origin, r.center);
            Gizmos.DrawCube(center, new Vector3(r.width, 0.02f, r.height));
        }

        private static Vector3 ToWorld(Vector3 origin, Vector2 localXZ) =>
            origin + new Vector3(localXZ.x, 0f, localXZ.y);
    }
}
