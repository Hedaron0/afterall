#if UNITY_EDITOR
using AfterAll.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AfterAll.EditorTools
{
    public static class CoreWiring
    {
        private const string SettingsFolder = "Assets/_AfterAll/Settings";
        private const string SettingsAssetPath = SettingsFolder + "/FrameRateSettings.asset";

        [MenuItem("AfterAll/Wire Game Systems")]
        public static void WireGameSystems()
        {
            var settings = EnsureFrameRateSettings();

            var systems = GameObject.Find("GameSystems");
            if (systems == null)
                systems = new GameObject("GameSystems");

            var controller = systems.GetComponent<FrameRateController>();
            if (controller == null)
                controller = systems.AddComponent<FrameRateController>();

            var so = new SerializedObject(controller);
            so.FindProperty("_settings").objectReferenceValue = settings;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

            Debug.Log(
                "[AfterAll] GameSystems wired.\n" +
                $"  FrameRateController → {SettingsAssetPath} (default: Unlimited / native refresh).\n" +
                "  Build APK and check log for [FrameRate] line on device.");
        }

        private static FrameRateSettings EnsureFrameRateSettings()
        {
            var existing = AssetDatabase.LoadAssetAtPath<FrameRateSettings>(SettingsAssetPath);
            if (existing != null)
                return existing;

            if (!AssetDatabase.IsValidFolder(SettingsFolder))
            {
                if (!AssetDatabase.IsValidFolder("Assets/_AfterAll"))
                    AssetDatabase.CreateFolder("Assets", "_AfterAll");
                AssetDatabase.CreateFolder("Assets/_AfterAll", "Settings");
            }

            var asset = ScriptableObject.CreateInstance<FrameRateSettings>();
            AssetDatabase.CreateAsset(asset, SettingsAssetPath);
            AssetDatabase.SaveAssets();
            return asset;
        }
    }
}
#endif
