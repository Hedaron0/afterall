using AfterAll.Generation.BackroomsMap;
using UnityEditor;
using UnityEngine;

namespace AfterAll.EditorTools
{
    [CustomEditor(typeof(BackroomsMapConfig))]
    public sealed class BackroomsMapConfigEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox(
                "World Seed is edited only in AfterAll → Backrooms Lab (not here).",
                MessageType.Info);

            DrawPropertiesExcluding(serializedObject, "_worldSeed");
            serializedObject.ApplyModifiedProperties();
        }
    }
}
