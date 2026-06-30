#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AfterAll.Environment.Editor
{
    [InitializeOnLoad]
    public static class RoomPrototypeAutoSetup
    {
        const string PrefabPath = "Assets/_AfterAll/Prefabs/Rooms/RoomPrototype.prefab";

        static RoomPrototypeAutoSetup()
        {
            EditorApplication.delayCall += EnsurePrefabExists;
        }

        static void EnsurePrefabExists()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            var hadPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null;
            if (!hadPrefab)
                RoomPrototypeBuilder.CreateRoomPrototypePrefab();

            if (GameObject.Find("Environment/RoomPrototype") == null)
                PlaceInSceneWithTestMatrix();
        }

        [MenuItem("AfterAll/Place Room Prototype In Scene (Test Matrix)")]
        public static void PlaceInSceneWithTestMatrix()
        {
            EnsurePrefabExists();

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                EditorUtility.DisplayDialog("Room Prototype", "Prefab missing. Run Create Room Prototype Prefab first.", "OK");
                return;
            }

            var environment = GameObject.Find("Environment");
            if (environment == null)
            {
                environment = new GameObject("Environment");
            }

            var existing = environment.transform.Find("RoomPrototype");
            if (existing != null)
                Object.DestroyImmediate(existing.gameObject);

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, environment.transform);
            instance.name = "RoomPrototype";
            instance.transform.localPosition = new Vector3(20f, 0f, 0f);

            var openings = instance.GetComponent<RoomWallOpenings>();
            if (openings != null)
                openings.ApplyTestMatrix();

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Selection.activeGameObject = instance;
        }

        /// <summary>
        /// Unity batchmode entry point for automated prefab + scene wiring.
        /// </summary>
        public static void BatchSetup()
        {
            var scenePath = "Assets/Scenes/Prototype_Room.unity";
            EditorSceneManager.OpenScene(scenePath);
            RoomPrototypeBuilder.CreateRoomPrototypePrefab();
            PlaceInSceneWithTestMatrix();
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
        }
    }
}
#endif
