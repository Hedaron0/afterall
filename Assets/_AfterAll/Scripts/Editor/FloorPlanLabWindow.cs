using System.IO;
using AfterAll.Generation.FloorPlan;
using UnityEditor;
using UnityEngine;

namespace AfterAll.EditorTools
{
  public sealed class FloorPlanLabWindow : EditorWindow
  {
    private FloorPlanConfig _config;
    private FloorPlanResult _result;
    private Texture2D _preview;
    private Vector2 _scroll;
    private int _previewScale = 4;
    private bool _multiChunkPreview;

    private static readonly Color WallColor = new(0.08f, 0.08f, 0.08f);
    private static readonly Color FloorColor = new(0.95f, 0.95f, 0.92f);
    private static readonly Color PillarColor = new(0.45f, 0.45f, 0.45f);
    private static readonly Color GridLineColor = new(1f, 1f, 1f, 0.12f);
    private static readonly Color LightColor = new(1f, 0.92f, 0.2f);

    [MenuItem("AfterAll/Floor Plan Lab")]
    public static void Open()
    {
      var window = GetWindow<FloorPlanLabWindow>("Floor Plan Lab");
      window.minSize = new Vector2(520, 640);
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

      EditorGUILayout.LabelField("Backrooms Floor Plan Lab", EditorStyles.boldLabel);
      EditorGUILayout.HelpBox(
        "2D layout iteration — no Play mode. Tune maze + room stamps until it feels like Level 0, then wire to 3D.",
        MessageType.Info);

      EditorGUI.BeginChangeCheck();
      _config = (FloorPlanConfig)EditorGUILayout.ObjectField("Config", _config, typeof(FloorPlanConfig), false);
      if (EditorGUI.EndChangeCheck() && _config != null)
        Regenerate();

      if (_config == null)
      {
        if (GUILayout.Button("Create FloorPlanConfig"))
          _config = FindOrCreateDefaultConfig();
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

        if (GUILayout.Button("Save Golden Seed", GUILayout.Height(28)))
          SaveGoldenSeed();

        if (GUILayout.Button("Export PNG", GUILayout.Height(28)))
          ExportPng();
      }

      using (new EditorGUILayout.HorizontalScope())
      {
        _multiChunkPreview = GUILayout.Toggle(_multiChunkPreview, "3×3 Chunk Preview");
        _previewScale = EditorGUILayout.IntSlider("Zoom", _previewScale, 1, 12);
      }

      if (_config != null)
      {
        EditorGUI.BeginChangeCheck();
        bool showLights = EditorGUILayout.Toggle("Lights Layer", _config.ShowLightsLayer);
        if (EditorGUI.EndChangeCheck())
        {
          var so = new SerializedObject(_config);
          so.FindProperty("_showLightsLayer").boolValue = showLights;
          so.ApplyModifiedProperties();
          RebuildTexture(_result);
        }
      }
    }

    private void DrawPreview()
    {
      if (_preview == null)
        return;

      EditorGUILayout.Space(6);
      float w = _preview.width * _previewScale;
      float h = _preview.height * _previewScale;
      var rect = GUILayoutUtility.GetRect(w, h, GUILayout.ExpandWidth(false));
      EditorGUI.DrawPreviewTexture(rect, _preview, null, ScaleMode.ScaleToFit);
    }

    private void DrawStats()
    {
      if (_result == null) return;

      EditorGUILayout.Space(4);
      EditorGUILayout.LabelField("Result", EditorStyles.boldLabel);
      EditorGUILayout.LabelField($"Seed: {_result.Seed}");
      EditorGUILayout.LabelField($"Floor: {_result.FloorPercent:P1}  |  Regions: {_result.RegionCount}");
      EditorGUILayout.LabelField($"Stamps: {_result.StampPlacements}  |  Connectivity punches: {_result.ConnectivityPunches}");
      if (_result.WallBlocks != null)
        EditorGUILayout.LabelField($"Wall blocks: {_result.WallBlocks.Count}  |  Lights: {_result.Lights?.Count ?? 0}");
      EditorGUILayout.LabelField(
        _result.IsFullyConnected ? "Connectivity: OK" : "Connectivity: SEALED POCKETS",
        _result.IsFullyConnected ? EditorStyles.label : EditorStyles.boldLabel);
    }

    private void DrawConfigEditor()
    {
      EditorGUILayout.Space(8);
      EditorGUILayout.LabelField("Settings", EditorStyles.boldLabel);

      var so = new SerializedObject(_config);
      so.Update();

      DrawProperty(so, "_worldSeed");
      DrawProperty(so, "_cellSize");
      DrawProperty(so, "_chunkCells");

      EditorGUILayout.Space(4);
      EditorGUILayout.LabelField("Maze Overlay", EditorStyles.miniBoldLabel);
      DrawProperty(so, "_mazeOverlayCount");
      DrawProperty(so, "_mazeFillTarget");
      DrawProperty(so, "_mazeCollisionStopProbability");

      EditorGUILayout.Space(4);
      EditorGUILayout.LabelField("Room Stamps", EditorStyles.miniBoldLabel);
      DrawProperty(so, "_stampPool");
      DrawProperty(so, "_stampPlacementAttempts");
      DrawProperty(so, "_globalStampWeightMultiplier");

      EditorGUILayout.Space(4);
      EditorGUILayout.LabelField("Connectivity & Stitch", EditorStyles.miniBoldLabel);
      DrawProperty(so, "_autoPunchGaps");
      DrawProperty(so, "_maxConnectivityPunches");
      DrawProperty(so, "_borderMergeMode");

      EditorGUILayout.Space(4);
      EditorGUILayout.LabelField("3D Spawn", EditorStyles.miniBoldLabel);
      DrawProperty(so, "_wallHeight");
      DrawProperty(so, "_slabThickness");
      DrawProperty(so, "_pillarFootprint");
      DrawProperty(so, "_wallBlockPrefab");
      DrawProperty(so, "_floorPrefab");
      DrawProperty(so, "_ceilingPrefab");
      DrawProperty(so, "_pillarPrefab");
      DrawProperty(so, "_wallMaterial");
      DrawProperty(so, "_floorMaterial");
      DrawProperty(so, "_ceilingMaterial");

      EditorGUILayout.Space(4);
      EditorGUILayout.LabelField("Lights", EditorStyles.miniBoldLabel);
      DrawProperty(so, "_lightPanelPrefab");
      DrawProperty(so, "_lightDarkChance");
      DrawProperty(so, "_lightRoomInset");
      DrawProperty(so, "_ceilingTileSize");
      DrawProperty(so, "_lightSpacingTiles");
      DrawProperty(so, "_lightGridOffsetX");
      DrawProperty(so, "_lightGridOffsetZ");

      EditorGUILayout.Space(4);
      EditorGUILayout.LabelField("Streaming", EditorStyles.miniBoldLabel);
      DrawProperty(so, "_loadRadius");

      EditorGUILayout.Space(4);
      EditorGUILayout.LabelField("Preview", EditorStyles.miniBoldLabel);
      DrawProperty(so, "_showRegions");
      DrawProperty(so, "_showChunkGrid");
      DrawProperty(so, "_showLightsLayer");
      DrawProperty(so, "_goldenSeeds");

      if (so.ApplyModifiedProperties())
        Regenerate();
    }

    private static void DrawProperty(SerializedObject so, string name)
    {
      var prop = so.FindProperty(name);
      if (prop != null)
        EditorGUILayout.PropertyField(prop, true);
    }

    private void Regenerate()
    {
      if (_config == null) return;

      _result = _multiChunkPreview
        ? FloorPlanGenerator.GeneratePreview(_config, radius: 1, _config.WorldSeed)
        : FloorPlanGenerator.Generate(_config, 0, 0, _config.WorldSeed);

      RebuildTexture(_result);
      Repaint();
    }

    private void RandomizeSeed()
    {
      if (_config == null) return;
      _config.SetWorldSeed(Random.Range(1, int.MaxValue));
      EditorUtility.SetDirty(_config);
      Regenerate();
    }

    private void SaveGoldenSeed()
    {
      if (_config == null || _result == null) return;
      _config.AddGoldenSeed(_result.Seed);
      EditorUtility.SetDirty(_config);
      AssetDatabase.SaveAssets();
      Debug.Log($"[FloorPlanLab] Golden seed saved: {_result.Seed}");
    }

    private void ExportPng()
    {
      if (_preview == null || _result == null) return;

      string dir = Path.Combine(Application.dataPath, "../FloorPlanExports");
      Directory.CreateDirectory(dir);
      string path = Path.Combine(dir, $"floorplan_seed{_result.Seed}.png");
      File.WriteAllBytes(path, _preview.EncodeToPNG());
      Debug.Log($"[FloorPlanLab] Exported {path}");
      EditorUtility.RevealInFinder(path);
    }

    private void RebuildTexture(FloorPlanResult result)
    {
      var grid = result.Grid;
      int w = grid.Width;
      int h = grid.Height;

      if (_preview == null || _preview.width != w || _preview.height != h)
      {
        if (_preview != null) DestroyImmediate(_preview);
        _preview = new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
          filterMode = FilterMode.Point,
          wrapMode = TextureWrapMode.Clamp,
        };
      }

      var regionMap = new int[w * h];
      if (_config.ShowRegions && result.Regions != null)
      {
        foreach (var region in result.Regions)
        {
          foreach (var (cx, cy) in region.Cells)
            regionMap[cy * w + cx] = region.Id + 1;
        }
      }

      int n = _config.ChunkCells;
      for (int y = 0; y < h; y++)
      {
        for (int x = 0; x < w; x++)
        {
          var cell = grid.Get(x, y);
          Color c = cell switch
          {
            CellState.Floor => FloorColor,
            CellState.Pillar => PillarColor,
            _ => WallColor,
          };

          if (_config.ShowRegions && cell != CellState.Wall)
          {
            int rid = regionMap[y * w + x];
            if (rid > 0)
              c = Color.Lerp(c, RegionHue(rid), 0.35f);
          }

          if (_config.ShowChunkGrid && _multiChunkPreview)
          {
            if (x % n == 0 || y % n == 0)
              c = Color.Lerp(c, GridLineColor, 0.5f);
          }

          _preview.SetPixel(x, h - 1 - y, c);
        }
      }

      if (_config.ShowLightsLayer && result.Lights != null)
      {
        float cell = grid.CellSize;
        foreach (var light in result.Lights)
        {
          int px = Mathf.Clamp(Mathf.FloorToInt(light.LocalX / cell), 0, w - 1);
          int py = Mathf.Clamp(Mathf.FloorToInt(light.LocalZ / cell), 0, h - 1);
          _preview.SetPixel(px, h - 1 - py, LightColor);
        }
      }

      _preview.Apply();
    }

    private static Color RegionHue(int id)
    {
      float hue = (id * 0.137f) % 1f;
      return Color.HSVToRGB(hue, 0.55f, 0.95f);
    }

    private static FloorPlanConfig FindOrCreateDefaultConfig()
    {
      var guids = AssetDatabase.FindAssets("t:FloorPlanConfig");
      if (guids.Length > 0)
      {
        string path = AssetDatabase.GUIDToAssetPath(guids[0]);
        return AssetDatabase.LoadAssetAtPath<FloorPlanConfig>(path);
      }

      return CreateDefaultAssets();
    }

    [MenuItem("AfterAll/Create Floor Plan Default Assets")]
    public static FloorPlanConfig CreateDefaultAssets()
    {
      const string dataDir = "Assets/_AfterAll/Data/FloorPlan";
      const string stampDir = "Assets/_AfterAll/Data/FloorPlan/Stamps";

      if (!AssetDatabase.IsValidFolder("Assets/_AfterAll/Data"))
        AssetDatabase.CreateFolder("Assets/_AfterAll", "Data");
      if (!AssetDatabase.IsValidFolder("Assets/_AfterAll/Data/FloorPlan"))
        AssetDatabase.CreateFolder("Assets/_AfterAll/Data", "FloorPlan");
      if (!AssetDatabase.IsValidFolder(stampDir))
        AssetDatabase.CreateFolder("Assets/_AfterAll/Data/FloorPlan", "Stamps");

      var wideHall = CreateStamp(stampDir, "Stamp_WideHall", RoomStampTag.Hall, new Vector2Int(12, 8), new Vector2Int(28, 20));
      var pillarField = CreateStamp(stampDir, "Stamp_PillarField", RoomStampTag.PillarField, new Vector2Int(10, 10), new Vector2Int(22, 22), RoomStampShape.PillarGrid);
      var polygon = CreateStamp(stampDir, "Stamp_PolygonPocket", RoomStampTag.PolygonPocket, new Vector2Int(8, 8), new Vector2Int(16, 16), RoomStampShape.Polygon);

      var config = ScriptableObject.CreateInstance<FloorPlanConfig>();
      AssetDatabase.CreateAsset(config, $"{dataDir}/FloorPlanConfig.asset");

      var so = new SerializedObject(config);
      var pool = so.FindProperty("_stampPool");
      pool.arraySize = 3;
      SetPoolEntry(pool.GetArrayElementAtIndex(0), wideHall, 1.2f);
      SetPoolEntry(pool.GetArrayElementAtIndex(1), pillarField, 0.8f);
      SetPoolEntry(pool.GetArrayElementAtIndex(2), polygon, 0.35f);
      so.ApplyModifiedPropertiesWithoutUndo();

      EditorUtility.SetDirty(config);
      AssetDatabase.SaveAssets();
      AssetDatabase.Refresh();

      Debug.Log("[FloorPlanLab] Created FloorPlanConfig + default stamps.");
      return config;
    }

    private static RoomStampDefinition CreateStamp(
      string dir, string name, RoomStampTag tag,
      Vector2Int min, Vector2Int max,
      RoomStampShape shape = RoomStampShape.Rectangle)
    {
      var stamp = ScriptableObject.CreateInstance<RoomStampDefinition>();
      var so = new SerializedObject(stamp);
      so.FindProperty("_displayName").stringValue = name.Replace("Stamp_", "");
      so.FindProperty("_tag").enumValueIndex = (int)tag;
      so.FindProperty("_shape").enumValueIndex = (int)shape;
      so.FindProperty("_sizeMinCells").vector2IntValue = min;
      so.FindProperty("_sizeMaxCells").vector2IntValue = max;
      so.ApplyModifiedPropertiesWithoutUndo();

      string path = $"{dir}/{name}.asset";
      AssetDatabase.CreateAsset(stamp, path);
      return stamp;
    }

    private static void SetPoolEntry(SerializedProperty entry, RoomStampDefinition stamp, float weight)
    {
      entry.FindPropertyRelative("Stamp").objectReferenceValue = stamp;
      entry.FindPropertyRelative("WeightMultiplier").floatValue = weight;
    }
  }
}
