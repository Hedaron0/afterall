using System.Collections.Generic;
using AfterAll.Environment;
using UnityEditor;
using UnityEngine;

namespace AfterAll.EditorTools
{
    /// <summary>
    /// Proportional weight sliders: dragging one option rescales the others so the total stays 100%.
    /// </summary>
    [CustomEditor(typeof(WeightedRandomGroup))]
    public class WeightedRandomGroupEditor : UnityEditor.Editor
    {
        private SerializedProperty _candidateFilter;
        private SerializedProperty _options;
        private SerializedProperty _forceIndex;

        private void OnEnable()
        {
            _candidateFilter = serializedObject.FindProperty("_candidateFilter");
            _options = serializedObject.FindProperty("_options");
            _forceIndex = serializedObject.FindProperty("_forceIndex");
        }

        public override void OnInspectorGUI()
        {
            var group = (WeightedRandomGroup)target;
            serializedObject.Update();

            EditorGUILayout.PropertyField(_candidateFilter);

            if (GUILayout.Button("Sync With Children"))
            {
                Undo.RecordObject(group, "Sync Weighted Options");
                group.SyncOptions();
                EditorUtility.SetDirty(group);
                serializedObject.Update();
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Chances (drag one, the rest rebalance)", EditorStyles.boldLabel);

            if (_options.arraySize == 0)
            {
                EditorGUILayout.HelpBox(
                    "No candidate children found. Add children under this GameObject, then click Sync With Children.",
                    MessageType.Info);
            }

            for (int i = 0; i < _options.arraySize; i++)
            {
                SerializedProperty option = _options.GetArrayElementAtIndex(i);
                SerializedProperty label = option.FindPropertyRelative("label");
                SerializedProperty weight = option.FindPropertyRelative("weight");

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(label.stringValue, GUILayout.Width(140));
                float newWeight = EditorGUILayout.Slider(weight.floatValue, 0f, 1f);
                EditorGUILayout.LabelField($"{newWeight * 100f:0}%", GUILayout.Width(40));
                EditorGUILayout.EndHorizontal();

                if (!Mathf.Approximately(newWeight, weight.floatValue))
                {
                    Undo.RecordObject(group, "Adjust Weight");
                    RebalanceWeights(group, i, newWeight);
                    EditorUtility.SetDirty(group);
                    serializedObject.Update();
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(_forceIndex, new GUIContent("Force Index (-1 = random)"));

            EditorGUILayout.Space();
            if (GUILayout.Button("Preview Random Pick"))
            {
                Undo.RecordObject(group, "Preview Pick");
                Transform picked = group.PreviewPickInEditor();
                Debug.Log($"[WeightedRandomGroup] {group.name} picked: {(picked != null ? picked.name : "none")}", group);
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void RebalanceWeights(WeightedRandomGroup group, int changedIndex, float newValue)
        {
            var options = new List<WeightedRandomGroup.Option>(group.Options);
            if (changedIndex < 0 || changedIndex >= options.Count)
                return;

            newValue = Mathf.Clamp01(newValue);
            float remaining = 1f - newValue;

            float othersSum = 0f;
            for (int i = 0; i < options.Count; i++)
                if (i != changedIndex)
                    othersSum += options[i].weight;

            for (int i = 0; i < options.Count; i++)
            {
                var opt = options[i];
                if (i == changedIndex)
                {
                    opt.weight = newValue;
                }
                else if (othersSum > 0.0001f)
                {
                    opt.weight = opt.weight / othersSum * remaining;
                }
                else
                {
                    opt.weight = options.Count > 1 ? remaining / (options.Count - 1) : 0f;
                }
                options[i] = opt;
            }

            group.SetOptions(options);
        }
    }
}
