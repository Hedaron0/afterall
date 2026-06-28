using AfterAll.Generation;
using AfterAll.Generation.BackroomsMap;
using UnityEditor;
using UnityEngine;

namespace AfterAll.EditorTools
{
    public sealed class BackroomsLabWindow : EditorWindow
    {
        private enum PreviewMode
        {
            SingleChunk,
            Grid3x3
        }

        private BackroomsMapConfig _config;
        private ChunkData _singleResult;
        private LabGridPreview _gridPreview;
        private Texture2D _preview;
        private Vector2 _scroll;
        private int _previewScale = 3;
        private PreviewMode _previewMode = PreviewMode.Grid3x3;
        private bool _showLights = true;
        private bool _showChunkLabels = true;
        private int _focusChunkX;
        private int _focusChunkZ;
        private bool _showGenerationSettings;

        private static readonly Color WallColor = new(0.08f, 0.08f, 0.08f);
        private static readonly Color FloorColor = new(0.55f, 0.55f, 0.52f);
        private static readonly Color RoomColor = new(0.92f, 0.92f, 0.88f);
        private static readonly Color PillarColor = new(0.35f, 0.35f, 0.38f);
        private static readonly Color ConnectorColor = new(0.2f, 0.75f, 0.95f);
        private static readonly Color LightColor = new(1f, 0.95f, 0.35f);
        private static readonly Color ChunkBorderColor = new(1f, 1f, 1f, 0.85f);
        private static readonly Color CenterChunkBorderColor = new(1f, 0.85f, 0.2f, 1f);
        private static readonly Color DoorMarkerColor = new(0.95f, 0.2f, 0.85f);

        [MenuItem("AfterAll/Backrooms Lab", false, 0)]
        public static void Open()
        {
            var window = GetWindow<BackroomsLabWindow>("Backrooms Lab");
            window.minSize = new Vector2(620, 720);
            window.Show();
        }

        private void OnEnable()
        {
            if (_config == null)
                _config = FindOrCreateDefaultConfig();
            Regenerate();
        }

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.LabelField("Backrooms Proc-Gen v3 — Floor Plan Lab", EditorStyles.boldLabel);
            DrawHelp();
            DrawConfigHeader();
            if (_config == null)
            {
                EditorGUILayout.EndScrollView();
                return;
            }

            DrawToolbar();
            DrawPositionReadout();
            DrawPreview();
            DrawLegend();
            DrawStats();

            EditorGUILayout.EndScrollView();

            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.R)
            {
                Regenerate();
                Event.current.Use();
            }
        }

        private void DrawHelp()
        {
            EditorGUILayout.HelpBox(
                "Top-down map: one pixel = one cell (2 m). +X = east (right), +Z north (up).\n" +
                "World Seed is the only seed you set — each chunk uses derive(WorldSeed, chunkX, chunkZ).\n" +
                "Focus Chunk is the chunk you are inspecting (centre of the 3×3 grid).",
                MessageType.Info);
        }

        private void DrawConfigHeader()
        {
            _config = (BackroomsMapConfig)EditorGUILayout.ObjectField("Map Config", _config, typeof(BackroomsMapConfig), false);
            if (_config == null)
            {
                if (GUILayout.Button("Create BackroomsMapConfig"))
                    _config = CreateDefaultAssets();
                return;
            }

            DrawWorldSeed();
            DrawGenerationSettingsFoldout();
        }

        private void DrawWorldSeed()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                int worldSeed = EditorGUILayout.IntField("World Seed", _config.WorldSeed);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(_config, "Change World Seed");
                    var so = new SerializedObject(_config);
                    so.FindProperty("_worldSeed").intValue = worldSeed;
                    so.ApplyModifiedProperties();
                    Regenerate();
                }

                if (GUILayout.Button("Random", GUILayout.Width(64)))
                {
                    Undo.RecordObject(_config, "Random World Seed");
                    var so = new SerializedObject(_config);
                    so.FindProperty("_worldSeed").intValue = Random.Range(1, int.MaxValue);
                    so.ApplyModifiedProperties();
                    Regenerate();
                }
            }
        }

        private void DrawGenerationSettingsFoldout()
        {
            _showGenerationSettings = EditorGUILayout.Foldout(_showGenerationSettings, "Generation settings");
            if (!_showGenerationSettings)
                return;

            var so = new SerializedObject(_config);
            so.Update();
            var prop = so.GetIterator();
            prop.NextVisible(true);
            while (prop.NextVisible(false))
            {
                if (prop.name == "_worldSeed")
                    continue;

                EditorGUILayout.PropertyField(prop, true);
            }

            if (so.ApplyModifiedProperties())
                Regenerate();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("View", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                _focusChunkX = EditorGUILayout.IntField("Focus Chunk X", _focusChunkX);
                _focusChunkZ = EditorGUILayout.IntField("Focus Chunk Z", _focusChunkZ);
                if (EditorGUI.EndChangeCheck())
                    Regenerate();
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Regenerate (R)", GUILayout.Height(26)))
                    Regenerate();

                if (Application.isPlaying && GUILayout.Button("Use Player Chunk", GUILayout.Height(26)))
                    SyncFocusFromPlayer();
            }

            EditorGUI.BeginChangeCheck();
            _previewMode = (PreviewMode)EditorGUILayout.EnumPopup("Preview Mode", _previewMode);
            _previewScale = EditorGUILayout.IntSlider("Zoom", _previewScale, 2, 10);
            _showChunkLabels = EditorGUILayout.Toggle("Chunk coordinate labels", _showChunkLabels);
            _showLights = EditorGUILayout.Toggle("Show lights", _showLights);
            if (EditorGUI.EndChangeCheck())
                Regenerate();
        }

        private void SyncFocusFromPlayer()
        {
            var manager = Object.FindAnyObjectByType<ChunkManager>();
            if (manager == null)
            {
                Debug.LogWarning("[BackroomsLab] No ChunkManager in scene.");
                return;
            }

            var chunk = manager.PlayerChunk;
            _focusChunkX = chunk.X;
            _focusChunkZ = chunk.Z;
            Regenerate();
            Debug.Log($"[BackroomsLab] Focus set to player chunk {chunk}.");
        }

        private void DrawPositionReadout()
        {
            if (_config == null) return;

            float chunkMetres = _config.ChunkSizeMetres;
            float originX = _focusChunkX * chunkMetres;
            float originZ = _focusChunkZ * chunkMetres;

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Where am I?", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"Focus chunk (grid index): ({_focusChunkX}, {_focusChunkZ})");
            EditorGUILayout.LabelField(
                $"World origin (SW corner of focus chunk): X {originX:F0} m, Z {originZ:F0} m");
            EditorGUILayout.LabelField(
                $"World centre of focus chunk: X {originX + chunkMetres * 0.5f:F0} m, Z {originZ + chunkMetres * 0.5f:F0} m");

            if (_previewMode == PreviewMode.Grid3x3 && _gridPreview != null)
            {
                EditorGUILayout.LabelField(
                    $"3×3 view shows chunks X {_focusChunkX - 1}…{_focusChunkX + 1}, " +
                    $"Z {_focusChunkZ - 1}…{_focusChunkZ + 1} (yellow border = focus chunk)");
            }
            else
            {
                EditorGUILayout.LabelField("Single-chunk view — one chunk only at focus coordinates.");
            }
        }

        private void DrawPreview()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Map", EditorStyles.boldLabel);

            if (_preview == null)
            {
                EditorGUILayout.HelpBox("No preview — click Regenerate.", MessageType.Warning);
                return;
            }

            EditorGUILayout.LabelField("↑ North (+Z)     → East (+X)", EditorStyles.miniLabel);

            float w = _preview.width * _previewScale;
            float h = _preview.height * _previewScale;
            var rect = GUILayoutUtility.GetRect(w, h + 4f, GUILayout.ExpandWidth(false));
            EditorGUI.DrawPreviewTexture(rect, _preview);

            if (_showChunkLabels && _previewMode == PreviewMode.Grid3x3 && _gridPreview != null)
                DrawChunkLabels(rect);
        }

        private void DrawChunkLabels(Rect texRect)
        {
            int radius = _gridPreview.Radius;
            int cs = _gridPreview.ChunkSize;
            float scale = _previewScale;

            var labelStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white }
            };

            for (int dz = -radius; dz <= radius; dz++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    int cx = _focusChunkX + dx;
                    int cz = _focusChunkZ + dz;
                    float px = texRect.x + (dx + radius + 0.5f) * cs * scale;
                    float py = texRect.yMax - (dz + radius + 0.5f) * cs * scale;
                    var labelRect = new Rect(px - 40f, py - 8f, 80f, 16f);
                    GUI.Label(labelRect, $"({cx}, {cz})", labelStyle);
                }
            }
        }

        private void DrawLegend()
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Legend", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Brown = door cell | Green = exit | Cyan = connector | Yellow pixel = light | " +
                "White lines = chunk borders | Yellow chunk border = focus chunk");
        }

        private void DrawStats()
        {
            ChunkData focus = _previewMode == PreviewMode.Grid3x3 && _gridPreview != null
                ? _gridPreview.CenterChunk
                : _singleResult;

            if (focus == null) return;

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField($"Focus chunk stats ({_focusChunkX}, {_focusChunkZ})", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"Derived seed (focus chunk only, read-only): {focus.Seed}");
            EditorGUILayout.LabelField(
                $"Grid: {_config.ChunkSize}×{_config.ChunkSize} cells @ {_config.CellWorldSize} m " +
                $"({_config.ChunkSizeMetres:F0} m per chunk)");
            EditorGUILayout.LabelField($"Zones: {focus.ZoneCount}");
            EditorGUILayout.LabelField($"Floor coverage: {focus.FloorFraction():P1}");
            EditorGUILayout.LabelField($"Connectors: {focus.ConnectorPoints.Count}");
            EditorGUILayout.LabelField($"Doors: {focus.DoorOpenings.Count}");
            EditorGUILayout.LabelField($"Lights: {focus.Lights.Count}");
            EditorGUILayout.LabelField(
                $"Exit: {(focus.Exit.HasValue ? focus.Exit.Value.Dir.ToString() : "none")}");
        }

        private void Regenerate()
        {
            if (_config == null) return;

            if (_previewMode == PreviewMode.Grid3x3)
            {
                _gridPreview = LabGridPreview.Build(_config, _focusChunkX, _focusChunkZ, radius: 1);
                _singleResult = _gridPreview.CenterChunk;
                RebuildTexture(_gridPreview, ref _preview);
            }
            else
            {
                _gridPreview = null;
                _singleResult = BackroomsMapGenerator.Generate(_config, _focusChunkX, _focusChunkZ);
                RebuildTexture(_singleResult, null, ref _preview);
            }

            Repaint();
        }

        private void RebuildTexture(LabGridPreview grid, ref Texture2D tex)
        {
            var cells = grid.BuildMergedCells();
            int w = grid.MergedWidth;
            int h = grid.MergedHeight;

            EnsureTexture(ref tex, w, h);
            var pixels = new Color[w * h];

            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int idx = y * w + x;
                pixels[idx] = SampleCellColor(grid, cells, x, y);

                if (IsChunkGridLine(x, y, grid.ChunkSize))
                    pixels[idx] = ChunkBorderColor;

                if (IsCenterChunkBorder(x, y, grid))
                    pixels[idx] = CenterChunkBorderColor;
            }

            MarkDoorFacing(pixels, w, h, grid);
            tex.SetPixels(pixels);
            tex.Apply();
        }

        private void RebuildTexture(ChunkData chunk, ChunkData _, ref Texture2D tex)
        {
            if (chunk?.Cells == null) return;

            int w = chunk.Width;
            int h = chunk.Height;
            EnsureTexture(ref tex, w, h);

            var pixels = new Color[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int idx = y * w + x;
                pixels[idx] = ColorForCell(chunk.Cells[y, x]);

                if (_showLights && ContainsLight(chunk.Lights, x, y))
                    pixels[idx] = LightColor;
                else if (ContainsConnector(chunk.ConnectorPoints, x, y))
                    pixels[idx] = ConnectorColor;
            }

            MarkDoorFacingSingle(pixels, w, h, chunk);
            tex.SetPixels(pixels);
            tex.Apply();
        }

        private Color SampleCellColor(LabGridPreview grid, CellType[,] cells, int x, int y)
        {
            if (!grid.TryGetChunkAtMergedCell(x, y, out int cx, out int cz))
                return WallColor;

            var chunk = grid.GetChunk(cx, cz);
            int lx = x % grid.ChunkSize;
            int ly = y % grid.ChunkSize;

            if (_showLights && ContainsLight(chunk.Lights, lx, ly))
                return LightColor;
            if (ContainsConnector(chunk.ConnectorPoints, lx, ly))
                return ConnectorColor;

            return ColorForCell(cells[y, x]);
        }

        private static void MarkDoorFacing(Color[] pixels, int w, int h, LabGridPreview grid)
        {
            int cs = grid.ChunkSize;
            foreach (var kv in grid.Chunks)
            {
                var (cx, cz) = kv.Key;
                var chunk = kv.Value;
                if (chunk.DoorOpenings == null) continue;

                int ox = (cx - (grid.CenterChunkX - grid.Radius)) * cs;
                int oy = (cz - (grid.CenterChunkZ - grid.Radius)) * cs;

                foreach (var door in chunk.DoorOpenings)
                {
                    int px = ox + door.X;
                    int py = oy + door.Y;
                    PaintDoorCorridorSide(pixels, w, h, px, py, door.Facing);
                }
            }
        }

        private static void MarkDoorFacingSingle(Color[] pixels, int w, int h, ChunkData chunk)
        {
            if (chunk.DoorOpenings == null) return;
            foreach (var door in chunk.DoorOpenings)
                PaintDoorCorridorSide(pixels, w, h, door.X, door.Y, door.Facing);
        }

        private static void PaintDoorCorridorSide(Color[] pixels, int w, int h, int x, int y, CardinalDir facing)
        {
            if (x < 0 || y < 0 || x >= w || y >= h) return;

            int idx = y * w + x;
            pixels[idx] = ColorForCell(CellType.DoorFrame);

            int tx = x + (facing == CardinalDir.E ? 1 : facing == CardinalDir.W ? -1 : 0);
            int ty = y + (facing == CardinalDir.N ? 1 : facing == CardinalDir.S ? -1 : 0);
            if (tx >= 0 && tx < w && ty >= 0 && ty < h)
                pixels[ty * w + tx] = DoorMarkerColor;
        }

        private static bool IsChunkGridLine(int x, int y, int chunkSize) =>
            x % chunkSize == 0 || y % chunkSize == 0;

        private static bool IsCenterChunkBorder(int x, int y, LabGridPreview grid)
        {
            int cs = grid.ChunkSize;
            int min = grid.Radius * cs;
            int max = (grid.Radius + 1) * cs;
            bool onVertical = (x == min || x == max) && y >= min && y <= max;
            bool onHorizontal = (y == min || y == max) && x >= min && x <= max;
            return onVertical || onHorizontal;
        }

        private static void EnsureTexture(ref Texture2D tex, int w, int h)
        {
            if (tex != null && tex.width == w && tex.height == h)
                return;

            if (tex != null)
                DestroyImmediate(tex);

            tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
        }

        private static bool ContainsLight(System.Collections.Generic.List<(int x, int y)> lights, int x, int y)
        {
            if (lights == null) return false;
            foreach (var (lx, ly) in lights)
            {
                if (lx == x && ly == y) return true;
            }

            return false;
        }

        private static bool ContainsConnector(System.Collections.Generic.List<ConnectorPoint> points, int x, int y)
        {
            if (points == null) return false;
            foreach (var p in points)
            {
                if (p.X == x && p.Y == y) return true;
            }

            return false;
        }

        private static Color ColorForCell(CellType cell) => cell switch
        {
            CellType.Wall => WallColor,
            CellType.Floor => FloorColor,
            CellType.Room => RoomColor,
            CellType.Pillar => PillarColor,
            CellType.DoorFrame => new Color(0.7f, 0.5f, 0.2f),
            CellType.Exit => new Color(0.2f, 0.8f, 0.3f),
            _ => WallColor
        };

        private static BackroomsMapConfig FindOrCreateDefaultConfig()
        {
            var guids = AssetDatabase.FindAssets("t:BackroomsMapConfig");
            if (guids.Length > 0)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                return AssetDatabase.LoadAssetAtPath<BackroomsMapConfig>(path);
            }

            return CreateDefaultAssets();
        }

        public static BackroomsMapConfig CreateDefaultAssets()
        {
            const string dataDir = "Assets/_AfterAll/Data/BackroomsMap";

            if (!AssetDatabase.IsValidFolder("Assets/_AfterAll/Data"))
                AssetDatabase.CreateFolder("Assets/_AfterAll", "Data");

            if (!AssetDatabase.IsValidFolder("Assets/_AfterAll/Data/BackroomsMap"))
                AssetDatabase.CreateFolder("Assets/_AfterAll/Data", "BackroomsMap");

            var config = ScriptableObject.CreateInstance<BackroomsMapConfig>();
            AssetDatabase.CreateAsset(config, $"{dataDir}/BackroomsMapConfig.asset");
            AssetDatabase.SaveAssets();
            Debug.Log("[BackroomsLab] Created BackroomsMapConfig.asset");
            return config;
        }
    }
}
