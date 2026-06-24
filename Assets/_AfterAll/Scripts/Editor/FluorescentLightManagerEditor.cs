#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace AfterAll.Environment.Editor
{
    [CustomEditor(typeof(FluorescentLightManager))]
    public class FluorescentLightManagerEditor : UnityEditor.Editor
    {
        UnityEditor.Editor _settingsEditor;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(serializedObject.FindProperty("_renderSettings"));

            var manager = (FluorescentLightManager)target;
            var settings = manager.RenderSettings;

            if (settings != null)
            {
                EditorGUILayout.Space(8);
                EditorGUILayout.LabelField("Light Render Settings", EditorStyles.boldLabel);

                if (_settingsEditor == null || _settingsEditor.target != settings)
                    _settingsEditor = CreateEditor(settings);

                _settingsEditor.OnInspectorGUI();

                EditorGUILayout.Space(8);
                EditorGUILayout.LabelField("Debug", EditorStyles.boldLabel);

                if (GUILayout.Button("Open Settings Asset"))
                    Selection.activeObject = settings;

                if (Application.isPlaying)
                {
                    if (GUILayout.Button("Force Refresh Budget"))
                        manager.ForceRefresh();

                    if (manager.HasSnapshot)
                    {
                        var snap = manager.LastSnapshot;
                        EditorGUILayout.HelpBox(
                            $"Spot+Point: {snap.CountSpot}  |  Point only: {snap.CountPointOnly}  |  " +
                            $"View-ray: {snap.CountViewRay}  |  Emission: {snap.CountEmission}  |  " +
                            $"Blocked: {snap.CountBlocked}  |  Off: {snap.CountOff}\n" +
                            $"Anchors: {snap.Anchors.Count}  |  Rays: {snap.RaySegments.Count}",
                            MessageType.Info);
                    }
                }
                else
                {
                    EditorGUILayout.HelpBox(
                        "Enter Play mode to see live tier counts and ray/anchor gizmos.",
                        MessageType.None);
                }
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "Assign Light Render Settings (e.g. FluorescentLightBudget_PC) to tune the lighting system.",
                    MessageType.Warning);
            }

            serializedObject.ApplyModifiedProperties();
        }

        void OnDisable()
        {
            if (_settingsEditor != null)
                DestroyImmediate(_settingsEditor);
        }
    }
}
#endif
