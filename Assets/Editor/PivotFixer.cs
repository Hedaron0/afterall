using UnityEngine;
using UnityEditor;

/// <summary>
/// Sketchfab / import edilen modellerde pivot noktasi yanlis yerde oldugunda
/// hierarchy'de secili objeye (veya objelere) sag tiklayip pivotu duzeltmek icin.
/// Empty bir parent GameObject olusturup pivotu istenen noktaya tasir,
/// orijinal mesh objesini onun altina child yapar (world pozisyonu bozulmadan).
/// </summary>
public static class PivotFixer
{
    private enum PivotMode { Center, Bottom }

    [MenuItem("GameObject/Fix Pivot/To Geometry Center", false, 0)]
    private static void FixPivotCenterMenu(MenuCommand command) => FixPivot(PivotMode.Center);

    [MenuItem("GameObject/Fix Pivot/To Bottom Center", false, 1)]
    private static void FixPivotBottomMenu(MenuCommand command) => FixPivot(PivotMode.Bottom);

    [MenuItem("GameObject/Fix Pivot/To Geometry Center", true)]
    [MenuItem("GameObject/Fix Pivot/To Bottom Center", true)]
    private static bool ValidateSelection() => Selection.gameObjects.Length > 0;

    private static void FixPivot(PivotMode mode)
    {
        GameObject[] selected = Selection.gameObjects;
        if (selected.Length == 0) return;

        Undo.SetCurrentGroupName("Fix Pivot");
        int undoGroup = Undo.GetCurrentGroup();

        int processed = 0;
        var newSelection = new System.Collections.Generic.List<GameObject>();

        foreach (GameObject obj in selected)
        {
            Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                Debug.LogWarning($"[PivotFixer] '{obj.name}' icinde Renderer bulunamadi, atlandi.");
                continue;
            }

            // Tum renderer'lari kapsayan world-space bounds
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            Vector3 pivotWorldPos = bounds.center;
            if (mode == PivotMode.Bottom)
                pivotWorldPos.y = bounds.min.y;

            Transform originalParent = obj.transform.parent;
            int siblingIndex = obj.transform.GetSiblingIndex();
            string originalName = obj.name;

            // Yeni pivot (empty) objesi - orijinal objenin yerini ve ismini alir
            GameObject pivotGO = new GameObject(originalName);
            Undo.RegisterCreatedObjectUndo(pivotGO, "Fix Pivot");

            Undo.SetTransformParent(pivotGO.transform, originalParent, "Fix Pivot");
            pivotGO.transform.position = pivotWorldPos;
            pivotGO.transform.rotation = obj.transform.rotation;
            pivotGO.transform.localScale = Vector3.one;
            pivotGO.transform.SetSiblingIndex(siblingIndex);

            // Mesh objesini yeni pivotun altina al, world transform korunur
            Undo.SetTransformParent(obj.transform, pivotGO.transform, "Fix Pivot");
            Undo.RecordObject(obj, "Fix Pivot Rename");
            obj.name = originalName + "_Mesh";

            newSelection.Add(pivotGO);
            processed++;
        }

        Undo.CollapseUndoOperations(undoGroup);

        if (newSelection.Count > 0)
            Selection.objects = newSelection.ToArray();

        Debug.Log($"[PivotFixer] {processed} obje icin pivot duzeltildi ({mode}).");
    }
}
