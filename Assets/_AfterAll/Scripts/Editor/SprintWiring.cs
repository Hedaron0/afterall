#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AfterAll.EditorTools
{
    public static class SprintWiring
    {
        [MenuItem("AfterAll/Wire Sprint Action")]
        public static void WireSprintAction()
        {
            var asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(
                "Assets/InputSystem_Actions.inputactions");

            if (asset == null)
            {
                Debug.LogError("[AfterAll] InputSystem_Actions.inputactions not found.");
                return;
            }

            var sprintAction = asset.FindActionMap("Player", true).FindAction("Sprint", true);
            if (sprintAction == null)
            {
                Debug.LogError("[AfterAll] Sprint action not found in Player map.");
                return;
            }

            var sprintRef = InputActionReference.Create(sprintAction);

            var movement = Object.FindAnyObjectByType<AfterAll.Player.PlayerMovement>();
            if (movement == null)
            {
                Debug.LogError("[AfterAll] PlayerMovement not found in scene.");
                return;
            }

            var so = new SerializedObject(movement);
            so.FindProperty("sprintAction").objectReferenceValue = sprintRef;
            so.ApplyModifiedProperties();

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("[AfterAll] Sprint action wired on PlayerMovement. Save scene (Ctrl+S).");
        }
    }
}
#endif
