using System.Collections.Generic;
using AfterAll.Environment;
using UnityEditor;
using UnityEngine;

namespace AfterAll.Editor
{
    public class LayoutTopViewWindow : EditorWindow
    {
        private const string FootprintFolder = "Assets/_AfterAll/Data/RoomFootprints";
        private const float GapWidthFallback = 1.3f;

        private readonly List<RoomFootprint> _library = new();
        private RoomFootprint _elevatorFootprint;
        private LayoutPlan _plan;
        private int _inspectIndex;
        private int _seed = 12345;
        private int _roomCount = 20;
        private bool _randomGapOffset;
        private float _gapEdgeMarginM = 0.15f;
        private float _zoom = 6f;
        private Vector2 _pan;
        private bool _isDragging;
        private Vector2 _dragStart;
        private Vector2 _panAtDragStart;
        private string _status = "Bake footprints, then Random.";

        [MenuItem("AfterAll/Generation/Layout Top View")]
        private static void Open()
        {
            var window = GetWindow<LayoutTopViewWindow>("Layout Top View");
            window.minSize = new Vector2(720f, 480f);
            window.ReloadLibrary();
        }

        private void OnEnable() => ReloadLibrary();

        private void OnGUI()
        {
            HandleHotkeys();
            DrawToolbar();
            Rect canvas = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            HandleCanvasInput(canvas);
            DrawCanvas(canvas);
            DrawFooter();
        }

        private void HandleHotkeys()
        {
            Event e = Event.current;
            if (e.type != EventType.KeyDown)
                return;

            if (EditorGUIUtility.editingTextField)
                return;

            if (e.keyCode == KeyCode.R)
            {
                RandomizeSeedAndGenerate();
                e.Use();
            }
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            if (GUILayout.Button("Reload", EditorStyles.toolbarButton, GUILayout.Width(60f)))
                ReloadLibrary();

            if (GUILayout.Button("Bake", EditorStyles.toolbarButton, GUILayout.Width(50f)))
            {
                EditorApplication.ExecuteMenuItem("AfterAll/Generation/Bake Room Footprints");
                ReloadLibrary();
            }

            _seed = EditorGUILayout.IntField(_seed, GUILayout.Width(80f));

            if (GUILayout.Button(new GUIContent("Random", "New seed + generate (hotkey: R)"), EditorStyles.toolbarButton, GUILayout.Width(64f)))
                RandomizeSeedAndGenerate();

            EditorGUILayout.LabelField("Rooms", GUILayout.Width(42f));
            _roomCount = Mathf.Clamp(EditorGUILayout.IntField(_roomCount, GUILayout.Width(40f)), 8, 80);

            if (GUILayout.Button("Generate", EditorStyles.toolbarButton, GUILayout.Width(70f)))
                GeneratePlan();

            if (GUILayout.Button(new GUIContent("Push → Play", "Write seed/rooms/footprints to RoomPoolSpawner and enter Play Mode"), EditorStyles.toolbarButton, GUILayout.Width(90f)))
                PushSeedAndEnterPlay();

            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField($"Lib:{_library.Count}", GUILayout.Width(50f));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            string[] names = BuildInspectNames();
            _inspectIndex = EditorGUILayout.Popup("Inspect footprint", _inspectIndex, names);
            if (GUILayout.Button("Show Selected Only", GUILayout.Width(140f)))
            {
                _plan = null;
                _status = names.Length > 0 ? $"Inspecting {names[Mathf.Clamp(_inspectIndex, 0, names.Length - 1)]}" : "No footprints.";
                Repaint();
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawFooter()
        {
            EditorGUILayout.HelpBox(_status, MessageType.Info);
        }

        private void RandomizeSeedAndGenerate()
        {
            _seed = Random.Range(1, int.MaxValue);
            GeneratePlan();
        }

        private void GeneratePlan()
        {
            if (_library.Count == 0)
            {
                _status = "No footprints. Run AfterAll → Generation → Bake Room Footprints.";
                return;
            }

            PaintGrowthConfig config = BuildConfig();
            _plan = PaintGrowthPlanner.Generate(_library, _seed, config, _elevatorFootprint);
            LayoutSilhouetteReport silhouette = LayoutSilhouetteMetrics.Evaluate(_plan, _library);
            string softPass = LayoutSilhouetteMetrics.MeetsSoftClusterTargets(silhouette)
                ? "soft-pass"
                : "soft-fail";
            _status = $"{_plan.notes}\n{silhouette.ToStatusLine()} [{softPass}] " +
                      $"(targets: aspect≤{LayoutSilhouetteMetrics.TargetMaxHullAspect:F1}, " +
                      $"fill≥{LayoutSilhouetteMetrics.TargetMinPackingFill:P0}, " +
                      $"cluster≥{LayoutSilhouetteMetrics.TargetMinClusterScore:F2})";
            FramePlan();
            Repaint();
        }

        private PaintGrowthConfig BuildConfig()
        {
            var policy = new GapOffsetPolicy
            {
                randomGapOffset = _randomGapOffset,
                edgeMarginM = _gapEdgeMarginM,
                spanFraction = 1f
            };
            return PaintGrowthConfig.FromTargetRoomCount(_roomCount, _randomGapOffset, policy);
        }

        private void PushSeedAndEnterPlay()
        {
            RoomPoolSpawner spawner = Object.FindFirstObjectByType<RoomPoolSpawner>();
            if (spawner == null)
            {
                _status = "No RoomPoolSpawner in open scenes. Open Test.unity first.";
                return;
            }

            Undo.RecordObject(spawner, "Set Paint Growth Seed");
            spawner.ConfigurePaintGrowthFromEditor(_seed, BuildConfig(), _library.ToArray(), _elevatorFootprint);
            EditorUtility.SetDirty(spawner);
            if (spawner.gameObject.scene.IsValid())
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(spawner.gameObject.scene);

            _status = $"Pushed seed={_seed}, rooms={_roomCount} → entering Play…";
            Repaint();
            EditorApplication.isPlaying = true;
        }

        private void ReloadLibrary()
        {
            _library.Clear();
            _elevatorFootprint = null;
            if (!AssetDatabase.IsValidFolder(FootprintFolder))
            {
                _status = $"Missing folder {FootprintFolder}. Bake first.";
                return;
            }

            string[] guids = AssetDatabase.FindAssets("t:RoomFootprint", new[] { FootprintFolder });
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                RoomFootprint footprint = AssetDatabase.LoadAssetAtPath<RoomFootprint>(path);
                if (footprint == null)
                    continue;

                // Elevator never joins the general pool — Generate() attaches it separately.
                if (footprint.IsElevator)
                {
                    _elevatorFootprint ??= footprint;
                    continue;
                }

                _library.Add(footprint);
            }

            _library.Sort((a, b) => string.CompareOrdinal(a.PrefabId, b.PrefabId));
            _inspectIndex = Mathf.Clamp(_inspectIndex, 0, Mathf.Max(0, _library.Count - 1));
            _status = $"Loaded {_library.Count} footprints" +
                       (_elevatorFootprint != null ? $" (+ elevator: {_elevatorFootprint.PrefabId})." : ".");
        }

        private string[] BuildInspectNames()
        {
            var names = new string[_library.Count];
            for (int i = 0; i < _library.Count; i++)
            {
                string shape = _library[i].IsCorridorShape ? "passage" : "room";
                names[i] =
                    $"{_library[i].PrefabId} [{shape}] " +
                    $"(area={_library[i].BoundsAreaM2:F0}m², walls={_library[i].Walls.Length})";
            }

            return names;
        }

        private void HandleCanvasInput(Rect canvas)
        {
            Event e = Event.current;
            if (!canvas.Contains(e.mousePosition))
                return;

            if (e.type == EventType.ScrollWheel)
            {
                _zoom = Mathf.Clamp(_zoom * (e.delta.y > 0f ? 0.9f : 1.1f), 1f, 40f);
                e.Use();
                Repaint();
            }

            if (e.type == EventType.MouseDown && e.button == 0)
            {
                _isDragging = true;
                _dragStart = e.mousePosition;
                _panAtDragStart = _pan;
                e.Use();
            }

            if (e.type == EventType.MouseDrag && _isDragging)
            {
                _pan = _panAtDragStart + (e.mousePosition - _dragStart);
                e.Use();
                Repaint();
            }

            if (e.type == EventType.MouseUp && e.button == 0)
            {
                _isDragging = false;
                e.Use();
            }
        }

        private void DrawCanvas(Rect canvas)
        {
            EditorGUI.DrawRect(canvas, new Color(0.12f, 0.12f, 0.14f));
            if (Event.current.type != EventType.Repaint)
                return;

            Handles.BeginGUI();
            Vector2 origin = canvas.center + _pan;

            Handles.color = new Color(1f, 1f, 1f, 0.08f);
            Handles.DrawLine(new Vector3(canvas.xMin, origin.y), new Vector3(canvas.xMax, origin.y));
            Handles.DrawLine(new Vector3(origin.x, canvas.yMin), new Vector3(origin.x, canvas.yMax));

            if (_plan != null && _plan.PlacedCount > 0)
                DrawPlan(origin);
            else if (_library.Count > 0)
                DrawSingleFootprint(origin, _library[Mathf.Clamp(_inspectIndex, 0, _library.Count - 1)]);

            Handles.EndGUI();
        }

        private void DrawSingleFootprint(Vector2 origin, RoomFootprint footprint)
        {
            DrawFootprintAt(origin, footprint, Vector2.zero, 0f, new Color(0.35f, 0.55f, 0.85f, 0.35f), true);
        }

        private void DrawPlan(Vector2 origin)
        {
            Dictionary<string, RoomFootprint> byId = BuildLookup();

            foreach (LayoutPlanConnection connection in _plan.connections)
            {
                if (connection.parentIndex < 0 || connection.childIndex < 0)
                    continue;
                if (connection.parentIndex >= _plan.placements.Count || connection.childIndex >= _plan.placements.Count)
                    continue;

                LayoutPlanPlacement parent = _plan.placements[connection.parentIndex];
                LayoutPlanPlacement child = _plan.placements[connection.childIndex];
                Vector2 a = WorldToCanvas(origin, parent.positionXZ);
                Vector2 b = WorldToCanvas(origin, child.positionXZ);
                Handles.color = new Color(1f, 0.85f, 0.2f, 0.8f);
                Handles.DrawLine(a, b);
            }

            for (int i = 0; i < _plan.placements.Count; i++)
            {
                LayoutPlanPlacement placement = _plan.placements[i];
                if (!byId.TryGetValue(placement.prefabId, out RoomFootprint footprint))
                    continue;

                Color fill;
                if (i == _plan.elevatorIndex)
                    fill = new Color(0.95f, 0.85f, 0.15f, 0.55f);
                else if (i == 0)
                    fill = new Color(0.9f, 0.35f, 0.2f, 0.4f);
                else if (i == _plan.exitIndex)
                    fill = new Color(0.85f, 0.25f, 0.75f, 0.4f);
                else if (footprint.IsCorridorShape)
                    fill = new Color(0.35f, 0.45f, 0.75f, 0.35f);
                else
                    fill = new Color(0.3f, 0.65f, 0.45f, 0.35f);

                DrawFootprintAt(origin, footprint, placement.positionXZ, placement.yawDegrees * Mathf.Deg2Rad, fill, false);
            }

            foreach (LayoutPlanConnection connection in _plan.connections)
                DrawConnectionDoors(origin, byId, connection);
        }

        private void DrawConnectionDoors(
            Vector2 origin,
            Dictionary<string, RoomFootprint> byId,
            LayoutPlanConnection connection)
        {
            if (connection.parentIndex < 0 || connection.parentIndex >= _plan.placements.Count)
                return;

            LayoutPlanPlacement parent = _plan.placements[connection.parentIndex];
            if (!byId.TryGetValue(parent.prefabId, out RoomFootprint footprint))
                return;
            if (!footprint.TryGetWall(connection.parentWall, out RoomFootprint.Wall wall))
                return;

            float yaw = parent.yawDegrees * Mathf.Deg2Rad;
            Vector2 seamLocal = OffsetSeamLocal(wall, footprint.GapWidthM, connection.parentGapOffsetM);
            Vector2 seamWorld = parent.positionXZ + Rotate(seamLocal, yaw);
            Vector2 axisWorld = Rotate(wall.axisLocal.normalized, yaw);
            float gap = footprint.GapWidthM > 0.05f ? footprint.GapWidthM : GapWidthFallback;
            Vector2 a = seamWorld - axisWorld * (gap * 0.5f);
            Vector2 b = seamWorld + axisWorld * (gap * 0.5f);
            Handles.color = Color.cyan;
            Handles.DrawLine(WorldToCanvas(origin, a), WorldToCanvas(origin, b));
        }

        private void DrawFootprintAt(
            Vector2 origin,
            RoomFootprint footprint,
            Vector2 positionXZ,
            float yawRadians,
            Color fill,
            bool labelWalls)
        {
            Vector2 c0 = WorldToCanvas(origin, positionXZ + Rotate(new Vector2(footprint.BoundsMin.x, footprint.BoundsMin.y), yawRadians));
            Vector2 c1 = WorldToCanvas(origin, positionXZ + Rotate(new Vector2(footprint.BoundsMin.x, footprint.BoundsMax.y), yawRadians));
            Vector2 c2 = WorldToCanvas(origin, positionXZ + Rotate(new Vector2(footprint.BoundsMax.x, footprint.BoundsMax.y), yawRadians));
            Vector2 c3 = WorldToCanvas(origin, positionXZ + Rotate(new Vector2(footprint.BoundsMax.x, footprint.BoundsMin.y), yawRadians));

            Handles.DrawSolidRectangleWithOutline(
                new[] { (Vector3)c0, (Vector3)c1, (Vector3)c2, (Vector3)c3 },
                fill,
                new Color(1f, 1f, 1f, 0.5f));

            foreach (RoomFootprint.Wall wall in footprint.Walls)
            {
                Vector2 start = WorldToCanvas(origin, positionXZ + Rotate(wall.startLocal, yawRadians));
                Vector2 end = WorldToCanvas(origin, positionXZ + Rotate(wall.endLocal, yawRadians));
                Handles.color = wall.doorValid ? new Color(0.95f, 0.95f, 0.95f, 0.95f) : new Color(0.6f, 0.6f, 0.6f, 0.6f);
                Handles.DrawLine(start, end);

                if (wall.doorValid)
                {
                    Vector2 axis = Rotate(wall.axisLocal.normalized, yawRadians);
                    float gap = footprint.GapWidthM > 0.05f ? footprint.GapWidthM : GapWidthFallback;
                    Vector2 seamWorld = positionXZ + Rotate(wall.seamLocal, yawRadians);
                    Vector2 a = WorldToCanvas(origin, seamWorld - axis * (gap * 0.5f));
                    Vector2 b = WorldToCanvas(origin, seamWorld + axis * (gap * 0.5f));
                    Handles.color = Color.cyan;
                    Handles.DrawLine(a, b);

                    if (labelWalls)
                    {
                        Vector2 outward = Rotate(wall.outwardLocal.normalized, yawRadians);
                        Vector2 labelPos = WorldToCanvas(origin, seamWorld + outward * 0.8f);
                        Handles.Label(labelPos, $"{wall.name}\n{wall.direction}");
                    }
                }
            }

            Handles.color = Color.white;
            Handles.Label(WorldToCanvas(origin, positionXZ), footprint.PrefabId);
        }

        private void FramePlan()
        {
            if (_plan == null || _plan.PlacedCount == 0)
                return;

            Vector2 min = _plan.placements[0].positionXZ;
            Vector2 max = min;
            foreach (LayoutPlanPlacement placement in _plan.placements)
            {
                min = Vector2.Min(min, placement.positionXZ);
                max = Vector2.Max(max, placement.positionXZ);
            }

            Vector2 center = (min + max) * 0.5f;
            _pan = new Vector2(-center.x * _zoom, center.y * _zoom);
        }

        private Vector2 WorldToCanvas(Vector2 origin, Vector2 worldXZ) =>
            origin + new Vector2(worldXZ.x, -worldXZ.y) * _zoom;

        private static Vector2 Rotate(Vector2 value, float radians)
        {
            float c = Mathf.Cos(radians);
            float s = Mathf.Sin(radians);
            return new Vector2(value.x * c - value.y * s, value.x * s + value.y * c);
        }

        private static Vector2 OffsetSeamLocal(RoomFootprint.Wall wall, float gapWidthM, float offsetMeters)
        {
            float edgeMargin = 0.15f;
            float effectiveGap = Mathf.Min(gapWidthM, Mathf.Max(0.05f, wall.lengthM - edgeMargin * 2f - 0.05f));
            float usable = Mathf.Max(0f, wall.lengthM - effectiveGap);
            float center = usable * 0.5f;
            float delta = offsetMeters - center;
            return wall.seamLocal + wall.axisLocal.normalized * delta;
        }

        private Dictionary<string, RoomFootprint> BuildLookup()
        {
            var map = new Dictionary<string, RoomFootprint>();
            foreach (RoomFootprint footprint in _library)
            {
                if (footprint != null && !map.ContainsKey(footprint.PrefabId))
                    map.Add(footprint.PrefabId, footprint);
            }

            if (_elevatorFootprint != null && !map.ContainsKey(_elevatorFootprint.PrefabId))
                map.Add(_elevatorFootprint.PrefabId, _elevatorFootprint);

            return map;
        }
    }
}
