#if UNITY_EDITOR
using AfterAll.Environment;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AfterAll.EditorTools
{
    public static class LightingWiring
    {
        const string CeilingPrefabPath = "Assets/_AfterAll/Prefabs/Backrooms_Modular/Ceiling.prefab";
        const string OutputPrefabPath = "Assets/_AfterAll/Prefabs/Backrooms_Modular/CeilingWithLight.prefab";
        const string EmissiveMatPath = "Assets/_AfterAll/Materials/fluorescent_emissive.mat";

        [MenuItem("AfterAll/Wire Fluorescent Light")]
        public static void WireFluorescentLight()
        {
            var fluorescent = GameObject.Find("Fluorescent");
            if (fluorescent == null)
            {
                Debug.LogWarning("[AfterAll] No 'Fluorescent' GameObject in scene.");
                return;
            }

            if (fluorescent.GetComponent<FluorescentLight>() == null)
                fluorescent.AddComponent<FluorescentLight>();

            var panel = fluorescent.GetComponent<Renderer>();
            if (panel != null)
            {
                panel.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                panel.receiveShadows = false;
            }

            foreach (var l in fluorescent.GetComponentsInChildren<Light>())
                l.shadows = LightShadows.None;

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("[AfterAll] FluorescentLight wired on 'Fluorescent'. Save scene (Ctrl+S).");
        }

        [MenuItem("AfterAll/Create CeilingWithLight Prefab")]
        public static void CreateCeilingWithLightPrefab()
        {
            var ceilingAsset = AssetDatabase.LoadAssetAtPath<GameObject>(CeilingPrefabPath);
            if (ceilingAsset == null)
            {
                Debug.LogError($"[AfterAll] Missing ceiling prefab at {CeilingPrefabPath}");
                return;
            }

            var emissiveMat = AssetDatabase.LoadAssetAtPath<Material>(EmissiveMatPath);
            var template = GameObject.Find("Fluorescent");
            if (template == null)
            {
                Debug.LogError("[AfterAll] Place and configure a 'Fluorescent' object in the scene first.");
                return;
            }

            var root = new GameObject("CeilingWithLight");

            var ceiling = (GameObject)PrefabUtility.InstantiatePrefab(ceilingAsset, root.transform);
            ceiling.name = "Ceiling";

            var panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            panel.name = "FluorescentPanel";
            panel.transform.SetParent(root.transform, false);

            var templateTransform = template.transform;
            panel.transform.localPosition = new Vector3(0f, templateTransform.localPosition.y, 0f);
            panel.transform.localRotation = templateTransform.localRotation;
            panel.transform.localScale = templateTransform.localScale;

            Object.DestroyImmediate(panel.GetComponent<Collider>());

            var renderer = panel.GetComponent<MeshRenderer>();
            if (emissiveMat != null)
                renderer.sharedMaterial = emissiveMat;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            var templateLight = template.GetComponentInChildren<Light>();
            if (templateLight != null)
            {
                var lightGo = new GameObject("Spot Light");
                lightGo.transform.SetParent(panel.transform, false);
                lightGo.transform.localPosition = templateLight.transform.localPosition;
                lightGo.transform.localRotation = templateLight.transform.localRotation;
                lightGo.transform.localScale = templateLight.transform.localScale;

                var light = lightGo.AddComponent<Light>();
                light.type = templateLight.type;
                light.color = templateLight.color;
                light.intensity = templateLight.intensity;
                light.range = templateLight.range;
                light.spotAngle = templateLight.spotAngle;
                light.innerSpotAngle = templateLight.innerSpotAngle;
                light.shadows = LightShadows.None;

                lightGo.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalLightData>();
            }

            panel.AddComponent<FluorescentLight>();

            PrefabUtility.SaveAsPrefabAsset(root, OutputPrefabPath);
            Object.DestroyImmediate(root);

            AssetDatabase.Refresh();
            Debug.Log($"[AfterAll] Saved {OutputPrefabPath}. Drag into scene to replace ceiling tiles + old Point Lights.");
        }
    }
}
#endif
