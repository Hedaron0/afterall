using AfterAll.Environment;
using UnityEditor;
using UnityEngine;

namespace AfterAll.EditorTools
{
    /// <summary>
    /// One-shot migration helper: select one or more room prefabs in the Project window, then run
    /// this to add a WeightedRandomGroup (NumericNamedChildren filter) to each prefab's Content root
    /// if missing. Run once per room prefab after authoring its "1"/"2"/"3" preset folders.
    /// </summary>
    public static class WeightedRandomGroupSetupTool
    {
        [MenuItem("AfterAll/Setup/Add WeightedRandomGroup To Selected Room Content")]
        private static void AddToSelectedPrefabs()
        {
            int touched = 0;
            int skipped = 0;

            foreach (Object obj in Selection.objects)
            {
                string path = AssetDatabase.GetAssetPath(obj);
                if (string.IsNullOrEmpty(path) || !path.EndsWith(".prefab"))
                {
                    skipped++;
                    continue;
                }

                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    Transform content = root.transform.Find("Content");
                    if (content == null)
                    {
                        Debug.LogWarning($"[WeightedRandomGroupSetup] {path}: no 'Content' child found, skipped.");
                        skipped++;
                        continue;
                    }

                    var group = content.GetComponent<WeightedRandomGroup>();
                    bool created = group == null;
                    if (created)
                        group = content.gameObject.AddComponent<WeightedRandomGroup>();

                    var so = new SerializedObject(group);
                    so.FindProperty("_candidateFilter").enumValueIndex = (int)WeightedRandomGroup.CandidateFilter.NumericNamedChildren;
                    so.ApplyModifiedPropertiesWithoutUndo();

                    group.SyncOptions();
                    EditorUtility.SetDirty(group);

                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    touched++;
                    Debug.Log($"[WeightedRandomGroupSetup] {path}: {(created ? "added" : "synced")} WeightedRandomGroup on Content ({group.Options.Count} presets found).");
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            Debug.Log($"[WeightedRandomGroupSetup] Done. {touched} prefab(s) updated, {skipped} skipped.");
        }
    }
}
