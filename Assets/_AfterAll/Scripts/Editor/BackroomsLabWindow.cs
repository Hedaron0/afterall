using AfterAll.Generation.BackroomsMap;
using UnityEditor;
using UnityEngine;

namespace AfterAll.EditorTools
{
    public sealed class BackroomsLabWindow : EditorWindow
    {
        private BackroomsMapConfig _config;
        private ChunkData _result;
        private ChunkData _compareResult;
        private Texture2D _preview;
        private Texture2D _comparePreview;
        private Vector2 _scroll;
        private int _previewScale = 6;
        private bool _showDimCompare;
        private int _seed = 42;

        private static readonly Color WallColor = new(0.08f, 0.08f, 0.08f);
        private static readonly Color FloorColor = new(0.55f, 0.55f, 0.52f);
        private static readonly Color RoomColor = new(0.92f, 0.92f, 0.88f);
        private static readonly Color PillarColor = new(0.35f, 0.35f, 0.38f);

        [MenuItem("AfterAll/Backrooms Lab")]
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
                "Zone BSP + archetypes (data only). Compare chunk dimensions before locking config. Press R to regenerate.",
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
                _previewScale = EditorGUILayout.IntSlider("Zoom", _previewScale, 2, 16);
            }

            _showDimCompare = EditorGUILayout.Toggle("Compare 26×26@2m vs 64×64@0.5m", _showDimCompare);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Apply Brief Preset (26×26 @ 2m)"))
                {
                    ApplyPreset(26, 2f);
                    Regenerate();
                }

                if (GUILayout.Button("Apply v2 Preset (64×64 @ 0.5m)"))
                {
                    ApplyPreset(64, 0.5f);
                    Regenerate();
                }
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

            if (_showDimCompare && _comparePreview != null)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.BeginVertical();
                    EditorGUILayout.LabelField($"Primary ({_config.ChunkSize}×{_config.ChunkSize} @ {_config.CellWorldSize}m)", EditorStyles.miniLabel);
                    DrawTexture(_preview);
                    EditorGUILayout.EndVertical();

                    EditorGUILayout.BeginVertical();
                    EditorGUILayout.LabelField("Compare (64×64 @ 0.5m)", EditorStyles.miniLabel);
                    DrawTexture(_comparePreview);
                    EditorGUILayout.EndVertical();
                }
            }
            else
            {
                DrawTexture(_preview);
            }
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
            EditorGUI.DrawPreviewTexture(rect, tex, ScaleMode.ScaleToFit);
        }

        private void DrawStats()
        {
            if (_result == null) return;

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Stats", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"Seed: {_result.Seed}");
            EditorGUILayout.LabelField($"Zones: {_result.ZoneCount}");
            EditorGUILayout.LabelField($"Floor coverage: {_result.FloorFraction():P1}");
            EditorGUILayout.LabelField($"Chunk world size: {_config.ChunkSizeMetres:F1} m");

            if (_showDimCompare && _compareResult != null)
            {
                EditorGUILayout.LabelField($"Compare floor coverage: {_compareResult.FloorFraction():P1}");
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

            _result = BackroomsMapGenerator.Generate(_config, 0, 0, _seed);
            RebuildTexture(_result, ref _preview);

            if (_showDimCompare)
            {
                var compareConfig = CreateTransientCompareConfig();
                _compareResult = BackroomsMapGenerator.Generate(compareConfig, 0, 0, _seed);
                RebuildTexture(_compareResult, ref _comparePreview);
                DestroyImmediate(compareConfig);
            }
            else
            {
                _compareResult = null;
                if (_comparePreview != null)
                {
                    DestroyImmediate(_comparePreview);
                    _comparePreview = null;
                }
            }

            Repaint();
        }

        private static BackroomsMapConfig CreateTransientCompareConfig()
        {
            var cfg = ScriptableObject.CreateInstance<BackroomsMapConfig>();
            var so = new SerializedObject(cfg);
            so.FindProperty("_worldSeed").intValue = 42;
            so.FindProperty("_chunkSize").intValue = 64;
            so.FindProperty("_cellWorldSize").floatValue = 0.5f;
            so.FindProperty("_zoneDepth").intValue = 4;
            so.FindProperty("_varietyLevel").intValue = 1;
            so.ApplyModifiedPropertiesWithoutUndo();
            return cfg;
        }

        private void RandomizeSeed()
        {
            _seed = Random.Range(1, int.MaxValue);
            Regenerate();
        }

        private static void RebuildTexture(ChunkData data, ref Texture2D tex)
        {
            if (data?.Cells == null) return;

            int w = data.Width;
            int h = data.Height;

            if (tex == null || tex.width != w || tex.height != h)
            {
                if (tex != null)
                    DestroyImmediate(tex);
                tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp
                };
            }

            var pixels = new Color[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                pixels[y * w + x] = ColorForCell(data.Cells[y, x]);

            tex.SetPixels(pixels);
            tex.Apply();
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
