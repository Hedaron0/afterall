using System;
using UnityEngine;

namespace AfterAll.Core
{
    /// <summary>
    /// Applies frame-rate policy at startup and exposes an API for a future settings menu.
    /// Unity 6 Android defaults to 30 FPS when targetFrameRate is unset — this fixes that.
    /// </summary>
    [DefaultExecutionOrder(-10000)]
    public class FrameRateController : MonoBehaviour
    {
        private const string PrefsModeKey = "afterall.settings.framerate.mode";
        private const string PrefsCustomKey = "afterall.settings.framerate.custom";

        public static FrameRateController Instance { get; private set; }

        public static event Action<FrameRateMode, int> FrameRateApplied;

        [SerializeField] private FrameRateSettings _settings;

        public FrameRateMode CurrentMode { get; private set; }
        public int TargetFrameRate { get; private set; }
        public int DisplayRefreshRate { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
            LoadAndApply();
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        public void LoadAndApply()
        {
            if (PlayerPrefs.HasKey(PrefsModeKey))
            {
                var mode = (FrameRateMode)PlayerPrefs.GetInt(PrefsModeKey, (int)FrameRateMode.Unlimited);
                var custom = PlayerPrefs.GetInt(PrefsCustomKey, _settings != null ? _settings.CustomFps : 120);
                Apply(mode, custom, save: false);
                return;
            }

            if (_settings != null)
                Apply(_settings.DefaultMode, _settings.CustomFps, save: false);
            else
                Apply(FrameRateMode.Unlimited, 120, save: false);
        }

        public void SetMode(FrameRateMode mode, int customFps = 0, bool save = true)
        {
            var resolvedCustom = customFps > 0
                ? customFps
                : _settings != null ? _settings.CustomFps : 120;

            Apply(mode, resolvedCustom, save);
        }

        public void ResetToDefaults()
        {
            PlayerPrefs.DeleteKey(PrefsModeKey);
            PlayerPrefs.DeleteKey(PrefsCustomKey);
            PlayerPrefs.Save();
            LoadAndApply();
        }

        public static int GetMaxDisplayRefreshRate()
        {
            var max = Mathf.RoundToInt((float)Screen.currentResolution.refreshRateRatio.value);

            foreach (var resolution in Screen.resolutions)
            {
                var hz = Mathf.RoundToInt((float)resolution.refreshRateRatio.value);
                if (hz > max)
                    max = hz;
            }

            return max > 0 ? max : 60;
        }

        public static int ResolveTargetFps(FrameRateMode mode, int customFps)
        {
            return mode switch
            {
                FrameRateMode.Unlimited => GetMaxDisplayRefreshRate(),
                FrameRateMode.Custom => Mathf.Max(1, customFps),
                FrameRateMode.Cap30 => 30,
                FrameRateMode.Cap60 => 60,
                FrameRateMode.Cap90 => 90,
                FrameRateMode.Cap120 => 120,
                _ => GetMaxDisplayRefreshRate(),
            };
        }

        private void Apply(FrameRateMode mode, int customFps, bool save)
        {
            DisplayRefreshRate = GetMaxDisplayRefreshRate();
            CurrentMode = mode;
            TargetFrameRate = ResolveTargetFps(mode, customFps);

            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = TargetFrameRate;

            if (save)
            {
                PlayerPrefs.SetInt(PrefsModeKey, (int)mode);
                PlayerPrefs.SetInt(PrefsCustomKey, customFps);
                PlayerPrefs.Save();
            }

            FrameRateApplied?.Invoke(mode, TargetFrameRate);

#if DEVELOPMENT_BUILD || UNITY_EDITOR
            Debug.Log(
                $"[FrameRate] mode={mode}, target={TargetFrameRate} FPS, display max={DisplayRefreshRate} Hz, vSync=0");
#endif
        }
    }
}
