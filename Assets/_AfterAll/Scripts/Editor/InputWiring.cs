#if UNITY_EDITOR
using AfterAll.Inventories;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AfterAll.EditorTools
{
    public static class InputWiring
    {
        const string InputAssetPath = "Assets/InputSystem_Actions.inputactions";

        [MenuItem("AfterAll/Wire Input")]
        public static void WireInput()
        {
            WireInventoryInput();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("[AfterAll] Input wired. Save scene (Ctrl+S).");
        }

        static void WireInventoryInput()
        {
            var player = GameObject.Find("Player");
            if (player == null)
            {
                Debug.LogWarning("[AfterAll] Player not found — skipped inventory input.");
                return;
            }

            var inventory = player.GetComponent<Inventory>();
            if (inventory == null)
            {
                Debug.LogWarning("[AfterAll] Inventory not found — skipped inventory input.");
                return;
            }

            var inputAsset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputAssetPath);
            var so = new SerializedObject(inventory);
            so.FindProperty("_inputActions").objectReferenceValue = inputAsset;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
#endif
