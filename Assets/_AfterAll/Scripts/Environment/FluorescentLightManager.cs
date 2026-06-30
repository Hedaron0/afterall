using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Serialization;

namespace AfterAll.Environment
{
    [AddComponentMenu("AfterAll/Environment/Fluorescent Light Manager")]
    public class FluorescentLightManager : MonoBehaviour
    {
        private static FluorescentLightManager s_instance;

        private readonly List<FluorescentLight> _panels = new(256);
        private readonly LightRenderSpatialGrid _grid = new();
        private readonly List<FluorescentLight> _nearby = new(128);
        private readonly LightRenderSnapshot _snapshot = new();
        private readonly HashSet<FluorescentLight> _activePanels = new(128);

        private Transform _playerTarget;
        private Camera    _camera;
        private float     _nextTargetSearch;
        private float     _nextTick;
        private bool      _refreshPending;
        private bool      _hasSnapshot;

        [FormerlySerializedAs("_settings")]
        [SerializeField] private LightRenderSettings _renderSettings;

        public static FluorescentLightManager Instance => s_instance;
        public LightRenderSettings RenderSettings => _renderSettings;
        public LightRenderSnapshot LastSnapshot => _snapshot;
        public bool HasSnapshot => _hasSnapshot;

        public static FluorescentLightManager EnsureExists()
        {
            if (s_instance != null)
                return s_instance;

            var existing = FindAnyObjectByType<FluorescentLightManager>();
            if (existing != null)
            {
                s_instance = existing;
                return s_instance;
            }

            var go = new GameObject("FluorescentLightManager");
            s_instance = go.AddComponent<FluorescentLightManager>();
            return s_instance;
        }

        private void Awake()
        {
            if (s_instance != null && s_instance != this)
            {
                Destroy(gameObject);
                return;
            }

            s_instance = this;
        }

        private void OnDestroy()
        {
            if (s_instance == this)
                s_instance = null;
        }

        public void Register(FluorescentLight panel)
        {
            if (panel == null)
                return;

            if (!_panels.Contains(panel))
            {
                _panels.Add(panel);
                _grid.Add(panel, GetSettings().spatialCellSize);
            }

            _refreshPending = true;
        }

        public void Unregister(FluorescentLight panel)
        {
            if (panel == null)
                return;

            _panels.Remove(panel);
            _grid.Remove(panel, GetSettings().spatialCellSize);
            _activePanels.Remove(panel);
        }

        public void ForceRefresh()
        {
            _refreshPending = true;
            TryRefresh(forceSearch: true);
        }

        private void Update()
        {
            var settings = GetSettings();
            if (_refreshPending || Time.time >= _nextTick)
            {
                _nextTick = Time.time + settings.tickInterval;
                _refreshPending = false;
                TryRefresh(forceSearch: false);
            }
        }

        private void TryRefresh(bool forceSearch)
        {
            var playerPos = GetPlayerPosition(forceSearch);
            if (!playerPos.HasValue)
                return;

            ResolveCamera(forceSearch);
            RefreshBudget(playerPos.Value, _camera);
        }

        private void RefreshBudget(Vector3 playerPos, Camera cam)
        {
            Profiler.BeginSample("FluorescentLightManager.RefreshBudget");

            var settings = GetSettings();
            float queryRadius = Mathf.Max(
                settings.bubbleRadius,
                settings.spotLightMaxDistance,
                settings.rayMaxDistance);

            _nearby.Clear();
            _grid.Query(playerPos, queryRadius, settings.spatialCellSize, _nearby);

            LightRenderBudget.Compute(playerPos, cam, settings, _nearby, _snapshot);

            for (int i = 0; i < _panels.Count; i++)
            {
                var panel = _panels[i];
                if (panel == null || _snapshot.Assignments.ContainsKey(panel))
                    continue;

                _snapshot.Assignments[panel] = LightPanelAssignment.Off(panel);
            }

            _snapshot.Tally();

            ApplyAssignments(_snapshot);
            _hasSnapshot = true;

            PruneNullPanels(settings.spatialCellSize);

            Profiler.EndSample();
        }

        private void ApplyAssignments(LightRenderSnapshot snapshot)
        {
            var newActive = new HashSet<FluorescentLight>();

            foreach (var kv in snapshot.Assignments)
            {
                var panel = kv.Key;
                var assignment = kv.Value;

                if (panel == null)
                    continue;

                panel.ApplyTier(
                    assignment.Tier,
                    assignment.Quality,
                    assignment.UseSpot,
                    assignment.UseFlicker);

                if (assignment.Tier != FluorescentLightTier.Off)
                    newActive.Add(panel);
            }

            var toDeactivate = new List<FluorescentLight>();
            foreach (var panel in _activePanels)
            {
                if (!newActive.Contains(panel))
                    toDeactivate.Add(panel);
            }

            for (int i = 0; i < toDeactivate.Count; i++)
                toDeactivate[i].ApplyTier(FluorescentLightTier.Off, 1f, false, false);

            _activePanels.Clear();
            foreach (var panel in newActive)
                _activePanels.Add(panel);
        }

        private void PruneNullPanels(float cellSize)
        {
            for (int i = _panels.Count - 1; i >= 0; i--)
            {
                if (_panels[i] != null)
                    continue;

                _panels.RemoveAt(i);
            }

            _grid.Rebuild(_panels, cellSize);
        }

        private LightRenderSettings GetSettings()
        {
            if (_renderSettings != null)
                return _renderSettings;

            return GetDefaultSettings();
        }

        private static LightRenderSettings s_defaultSettings;

        private static LightRenderSettings GetDefaultSettings()
        {
            if (s_defaultSettings != null)
                return s_defaultSettings;

            s_defaultSettings = ScriptableObject.CreateInstance<LightRenderSettings>();
            s_defaultSettings.hideFlags = HideFlags.HideAndDontSave;
            return s_defaultSettings;
        }

        private Vector3? GetPlayerPosition(bool forceSearch)
        {
            var target = ResolvePlayerTarget(forceSearch);
            return target != null ? target.position : null;
        }

        private void ResolveCamera(bool forceSearch)
        {
            if (_camera != null && _camera.isActiveAndEnabled)
                return;

            if (!forceSearch && Time.time < _nextTargetSearch)
                return;

            _camera = Camera.main;
        }

        private Transform ResolvePlayerTarget(bool forceSearch)
        {
            if (_playerTarget != null)
                return _playerTarget;

            if (!forceSearch && Time.time < _nextTargetSearch)
                return null;

            _nextTargetSearch = Time.time + 0.5f;

            var movement = FindAnyObjectByType<AfterAll.Player.PlayerMovement>();
            if (movement != null)
            {
                _playerTarget = movement.transform;
                return _playerTarget;
            }

            var cam = Camera.main;
            if (cam != null)
                _playerTarget = cam.transform;

            return _playerTarget;
        }

        private void OnDrawGizmosSelected()
        {
            DrawDebugGizmos();
        }

        private void DrawDebugGizmos()
        {
            var settings = _renderSettings;
            if (settings == null)
                return;

            if (!_hasSnapshot || !Application.isPlaying)
            {
                var preview = new LightRenderSnapshot();
                var playerPos = ResolveGizmoPlayerPosition();
                preview.PlayerPosition = playerPos;
                preview.CameraPosition = Camera.main != null
                    ? Camera.main.transform.position
                    : playerPos;

                var cam = Camera.main;
                if (cam != null)
                {
                    var fwd = cam.transform.forward;
                    fwd.y = 0f;
                    preview.CameraForwardFlat = fwd.sqrMagnitude > 0.0001f ? fwd.normalized : Vector3.forward;

                    if (Application.isPlaying)
                        LightRenderProbe.Probe(playerPos, cam, settings, preview.Anchors, preview.RaySegments);
                }

                LightRenderGizmos.Draw(settings, preview, preview.Anchors.Count > 0 || settings.gizmoShowBubble);
                return;
            }

            LightRenderGizmos.Draw(settings, _snapshot, true);
        }

        private Vector3 ResolveGizmoPlayerPosition()
        {
            if (Application.isPlaying)
            {
                var pos = GetPlayerPosition(forceSearch: true);
                if (pos.HasValue)
                    return pos.Value;
            }

            return transform.position;
        }
    }
}
