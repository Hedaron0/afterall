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
            TwoChunkEast
        }

        private BackroomsMapConfig _config;
        private ChunkData _result;
        private ChunkPreviewResult _pairResult;
        private Texture2D _preview;
        private Vector2 _scroll;
        private int _previewScale = 4;
        private PreviewMode _previewMode = PreviewMode.SingleChunk;
        private bool _showVents = true;
        private bool _showLights = true;
        private int _seed = 42;

        private static readonly Color WallColor = new(0.08f, 0.08f, 0.08f);
        private static readonly Color FloorColor = new(0.55f, 0.55f, 0.52f);
        private static readonly Color RoomColor = new(0.92f, 0.92f, 0.88f);
        private static readonly Color PillarColor = new(0.35f, 0.35f, 0.38f);
        private static readonly Color ConnectorColor = new(0.2f, 0.75f, 0.95f);
        private static readonly Color VentColor = new(0.95f, 0.45f, 0.15f);
        private static readonly Color LightColor = new(1f, 0.95f, 0.35f);

        [MenuItem("AfterAll/Backrooms Lab", false, 0)]
        public static void Open()
        {
            var window = GetWindow<BackroomsLabWindow>("Backrooms Lab");
            window.minSize = new Vector2(560, 680);
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

            EditorGUILayout.LabelField("Backrooms Proc-Gen v3 Lab", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                $"Each pixel = 1 cell = {_config?.CellWorldSize ?? 2f}m wide/deep (top-down only; height not shown). " +
                "Cyan = connectors, orange = vents, yellow = lights.",
                MessageType.Info);

            _config = (BackroomsMapConfig)EditorGUILayout.ObjectField("Config", _config, typeof(BackroomsMapConfig), false);

            if (_config == null)
            {
                if (GUILayout.Button("Create BackroomsMapConfig"))
                    _config = CreateDefaultAssets();
                EditorGUILayout.EndScrollView();
                return;
            }

            DrawToolbar();
            DrawPreview();
            DrawStats();
            DrawConfigEditor();

            EditorGUILayout.EndScrollView();

            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.R)
            {
                RandomizeSeed();
                Event.current.Use();
            }
        }

        private void DrawToolbar()
        {
            EditorGUILayout.Space(4);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Regenerate (R)", GUILayout.Height(28)))
                    Regenerate();

                if (GUILayout.Button("Random Seed", GUILayout.Height(28)))
                    RandomizeSeed();
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                _seed = EditorGUILayout.IntField("Seed", _seed);
                _previewScale = EditorGUILayout.IntSlider("Zoom", _previewScale, 2, 12);
            }

            _previewMode = (PreviewMode)EditorGUILayout.EnumPopup("Preview", _previewMode);
            _showVents = EditorGUILayout.Toggle("Show vent paths", _showVents);
            _showLights = EditorGUILayout.Toggle("Show lights", _showLights);

            if (GUILayout.Button("Apply Locked Preset (32x32 @ 2m)"))
            {
                ApplyPreset(32, 2f);
                Regenerate();
            }
        }

        private void ApplyPreset(int chunkSize, float cellWorldSize)
        {
            Undo.RecordObject(_config, "Apply chunk preset");
            var so = new SerializedObject(_config);
            so.FindProperty("_chunkSize").intValue = chunkSize;
            so.FindProperty("_cellWorldSize").floatValue = cellWorldSize;
            so.ApplyModifiedProperties();
        }

        private void DrawPreview()
        {
            EditorGUILayout.Space(8);
            DrawTexture(_preview);
        }

        private void DrawTexture(Texture2D tex)
        {
            if (tex == null)
            {
                EditorGUILayout.HelpBox("No preview — click Regenerate.", MessageType.Warning);
                return;
            }

            float w = tex.width * _previewScale;
            float h = tex.height * _previewScale;
            var rect = GUILayoutUtility.GetRect(w, h, GUILayout.ExpandWidth(false));
            EditorGUI.DrawPreviewTexture(rect, tex);
        }

        private void DrawStats()
        {
            if (_result == null) return;

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Stats", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"Grid: {_config.ChunkSize}x{_config.ChunkSize} @ {_config.CellWorldSize}m ({_config.ChunkSizeMetres:F0}m chunk)");
            EditorGUILayout.LabelField($"Seed: {_result.Seed}");
            EditorGUILayout.LabelField($"Zones: {_result.ZoneCount}");
            EditorGUILayout.LabelField($"Floor coverage: {_result.FloorFraction():P1}");
            EditorGUILayout.LabelField($"Connector points: {_result.ConnectorPoints.Count}");
            EditorGUILayout.LabelField($"Vents: {_result.Vents.Count}");
            EditorGUILayout.LabelField($"Door openings: {_result.DoorOpenings.Count}");
            EditorGUILayout.LabelField($"Exit: {(_result.Exit.HasValue ? _result.Exit.Value.Dir.ToString() : "none")}");
            EditorGUILayout.LabelField($"Accessibility: {_result.AccessibilityCorridors} corridor(s), {_result.AccessibilityWalled} cell(s) walled");
            EditorGUILayout.LabelField($"Lights: {_result.Lights.Count}");

            if (_previewMode == PreviewMode.TwoChunkEast && _pairResult != null)
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("2-chunk East/West stitch", EditorStyles.boldLabel);
                EditorGUILayout.LabelField($"Aligned connectors: {_pairResult.ConnectorsMatch}");
                EditorGUILayout.LabelField($"Shared edge walkable: {_pairResult.BothEdgesWalkable}");

                if (!_pairResult.ConnectorsMatch || !_pairResult.BothEdgesWalkable)
                    EditorGUILayout.HelpBox("Connector stitch failed for this seed — try another seed.", MessageType.Warning);
                else
                    EditorGUILayout.HelpBox("Connector stitch OK.", MessageType.Info);
            }
        }

        private void DrawConfigEditor()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Config", EditorStyles.boldLabel);
            var editor = Editor.CreateEditor(_config);
            editor.OnInspectorGUI();
        }

        private void Regenerate()
        {
            if (_config == null) return;

            if (_previewMode == PreviewMode.TwoChunkEast)
            {
                _pairResult = ChunkPreviewBuilder.BuildEastPair(_config, _seed);
                _result = _pairResult.Primary;
                RebuildTexture(_pairResult.Primary, _pairResult.Neighbor, _config.ChunkSize, ref _preview);
            }
            else
            {
                _pairResult = null;
                _result = BackroomsMapGenerator.Generate(_config, 0, 0, _seed);
                RebuildTexture(_result, null, _config.ChunkSize, ref _preview);
            }

            Repaint();
        }

        private void RandomizeSeed()
        {
            _seed = UnityEngine.Random.Range(1, int.MaxValue);
            Regenerate();
        }

        private void RebuildTexture(
            CellType[,] cells,
            ChunkData neighbor,
            int chunkSize,
            ref Texture2D tex)
        {
            if (cells == null) return;

            int w = cells.GetLength(1);
            int h = cells.GetLength(0);

            if (tex == null || tex.width != w || tex.height != h)
            {
                if (tex != null)
                    UnityEngine.Object.DestroyImmediate(tex);

                tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp
                };
            }

            var connectorPixels = BuildConnectorMask(w, h, _result.ConnectorPoints, neighbor?.ConnectorPoints, chunkSize);
            var ventPixels = _showVents
                ? BuildVentMask(w, h, _result.Vents, neighbor?.Vents, chunkSize)
                : null;
            var lightPixels = _showLights
                ? BuildLightMask(w, h, _result.Lights, neighbor?.Lights, chunkSize)
                : null;

            var pixels = new Color[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int idx = y * w + x;
                if (lightPixels != null && lightPixels[idx])
                    pixels[idx] = LightColor;
                else if (ventPixels != null && ventPixels[idx])
                    pixels[idx] = VentColor;
                else if (connectorPixels[idx])
                    pixels[idx] = ConnectorColor;
                else
                    pixels[idx] = ColorForCell(cells[y, x]);
            }

            tex.SetPixels(pixels);
            tex.Apply();
        }

        private void RebuildTexture(ChunkData chunk, ChunkData neighbor, int chunkSize, ref Texture2D tex)
        {
            if (_previewMode == PreviewMode.TwoChunkEast && neighbor != null)
            {
                var merged = ChunkPreviewBuilder.MergeEastWest(chunk, neighbor);
                RebuildTexture(merged, neighbor, chunkSize, ref tex);
            }
            else
            {
                RebuildTexture(chunk.Cells, null, chunkSize, ref tex);
            }
        }

        private static bool[] BuildLightMask(
            int w, int h,
            System.Collections.Generic.List<(int x, int y)> primaryLights,
            System.Collections.Generic.List<(int x, int y)> neighborLights,
            int chunkSize)
        {
            var mask = new bool[w * h];

            void Mark(System.Collections.Generic.List<(int x, int y)> lights, int offsetX)
            {
                if (lights == null) return;
                foreach (var (lx, ly) in lights)
                {
                    int x = lx + offsetX;
                    if (x >= 0 && x < w && ly >= 0 && ly < h)
                        mask[ly * w + x] = true;
                }
            }

            Mark(primaryLights, 0);
            if (neighborLights != null)
                Mark(neighborLights, chunkSize);

            return mask;
        }

        private static bool[] BuildVentMask(
            int w, int h,
            System.Collections.Generic.List<VentSpec> primaryVents,
            System.Collections.Generic.List<VentSpec> neighborVents,
            int chunkSize)
        {
            var mask = new bool[w * h];

            void Mark(System.Collections.Generic.List<VentSpec> vents, int offsetX)
            {
                if (vents == null) return;
                foreach (var vent in vents)
                {
                    foreach (var (vx, vy) in vent.Path)
                    {
                        int x = vx + offsetX;
                        if (x >= 0 && x < w && vy >= 0 && vy < h)
                            mask[vy * w + x] = true;
                    }
                }
            }

            Mark(primaryVents, 0);
            if (neighborVents != null)
                Mark(neighborVents, chunkSize);

            return mask;
        }

        private static bool[] BuildConnectorMask(
            int w, int h,
            System.Collections.Generic.List<ConnectorPoint> primaryConnectors,
            System.Collections.Generic.List<ConnectorPoint> neighborConnectors,
            int chunkSize)
        {
            var mask = new bool[w * h];

            void Mark(System.Collections.Generic.List<ConnectorPoint> points, int offsetX)
            {
                if (points == null) return;
                foreach (var p in points)
                {
                    int x = p.X + offsetX;
                    int y = p.Y;
                    if (x >= 0 && x < w && y >= 0 && y < h)
                        mask[y * w + x] = true;
                }
            }

            Mark(primaryConnectors, 0);
            if (neighborConnectors != null)
                Mark(neighborConnectors, chunkSize);

            return mask;
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
