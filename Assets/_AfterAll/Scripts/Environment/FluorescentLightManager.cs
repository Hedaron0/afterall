using System.Collections.Generic;
using UnityEngine;

namespace AfterAll.Environment
{
    /// <summary>
    /// One central tick for all fluorescent panels — avoids hundreds of per-panel Update calls.
    /// </summary>
    [AddComponentMenu("AfterAll/Environment/Fluorescent Light Manager")]
    public class FluorescentLightManager : MonoBehaviour
    {
        private static FluorescentLightManager s_instance;

        private readonly List<FluorescentLight> _panels = new(256);

        private Transform _target;
        private float       _nextTargetSearch;
        private float       _nextTick;

        private const float kTickInterval = 0.2f;

        public static FluorescentLightManager Instance => s_instance;

        public static FluorescentLightManager EnsureExists()
        {
            if (s_instance != null)
                return s_instance;

            var go = new GameObject("FluorescentLightManager");
            s_instance = go.AddComponent<FluorescentLightManager>();
            DontDestroyOnLoad(go);
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
            if (!_panels.Contains(panel))
                _panels.Add(panel);
        }

        public void Unregister(FluorescentLight panel) => _panels.Remove(panel);

        private void Update()
        {
            if (Time.time < _nextTick)
                return;

            _nextTick = Time.time + kTickInterval;

            var target = GetTarget();
            if (target == null)
                return;

            Vector3 pos = target.position;

            for (int i = _panels.Count - 1; i >= 0; i--)
            {
                var panel = _panels[i];
                if (panel == null)
                {
                    _panels.RemoveAt(i);
                    continue;
                }

                panel.RefreshCullState(pos);
            }
        }

        private Transform GetTarget()
        {
            if (_target != null)
                return _target;

            if (Time.time < _nextTargetSearch)
                return null;

            _nextTargetSearch = Time.time + 1f;

            var player = FindAnyObjectByType<AfterAll.Player.PlayerMovement>();
            if (player != null)
            {
                _target = player.transform;
                return _target;
            }

            var cam = Camera.main;
            if (cam != null)
                _target = cam.transform;

            return _target;
        }
    }
}
