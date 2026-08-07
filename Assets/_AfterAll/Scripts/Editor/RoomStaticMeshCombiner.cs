using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using AfterAll.Environment;

namespace AfterAll.EditorTools
{
    /// <summary>
    /// Combines a room prefab's invariant shell (plain wall segments, floor, ceiling) into a single
    /// mesh with one freshly unwrapped UV2, so the lightmapper bakes one continuous chart instead of
    /// many per-object charts that show a visible seam at every piece boundary. Also collapses the
    /// shell to a handful of draw calls.
    ///
    /// Selection is structural, never name-based: everything under Content, everything owned by a
    /// WallGapController (door walls are resized at runtime, so they cannot hold a static chart) and
    /// everything under a WeightedRandomGroup (runtime-variable content) is excluded.
    ///
    /// Originals are disabled, never destroyed — Colliders stay intact and Revert restores them.
    /// </summary>
    public static class RoomStaticMeshCombiner
    {
        private const string OutputFolder = "Assets/_AfterAll/Data/CombinedRoomMeshes";
        private const string CombinedChildName = "CombinedStatic";

        private const StaticEditorFlags ShellFlags =
            StaticEditorFlags.ContributeGI
            | StaticEditorFlags.BatchingStatic
            | StaticEditorFlags.OccluderStatic
            | StaticEditorFlags.OccludeeStatic
            | StaticEditorFlags.ReflectionProbeStatic;

        [MenuItem("AfterAll/Lighting/Combine Static Shell - Preview (Selected)")]
        private static void PreviewSelected() => RunOnSelection(preview: true);

        [MenuItem("AfterAll/Lighting/Combine Static Shell - Apply (Selected)")]
        private static void ApplySelected() => RunOnSelection(preview: false);

        [MenuItem("AfterAll/Lighting/Combine Static Shell - Revert (Selected)")]
        private static void RevertSelected()
        {
            string path = ResolvePrefabPath();
            if (path == null)
                return;

            var root = PrefabUtility.LoadPrefabContents(path);
            var existing = root.transform.Find(CombinedChildName);

            if (existing == null)
            {
                Debug.Log($"[Combiner] {Path.GetFileName(path)}: no {CombinedChildName} to revert.");
                PrefabUtility.UnloadPrefabContents(root);
                return;
            }

            int restored = RestoreExisting(root.transform);

            PrefabUtility.SaveAsPrefabAsset(root, path);
            PrefabUtility.UnloadPrefabContents(root);

            Debug.Log($"[Combiner] {Path.GetFileName(path)}: reverted, {restored} renderer(s) re-enabled.");
        }

        /// <summary>Re-enables a previous pass's source renderers and removes the generated child.</summary>
        private static int RestoreExisting(Transform root)
        {
            var existing = root.Find(CombinedChildName);
            if (existing == null)
                return 0;

            int restored = 0;
            var group = existing.GetComponent<CombinedStaticGroup>();
            if (group != null)
            {
                foreach (var mr in group.SourceRenderers)
                {
                    if (mr == null)
                        continue;
                    mr.enabled = true;
                    restored++;
                }
            }

            Object.DestroyImmediate(existing.gameObject);
            return restored;
        }

        private static void RunOnSelection(bool preview)
        {
            string path = ResolvePrefabPath();
            if (path == null)
                return;

            var root = PrefabUtility.LoadPrefabContents(path);

            // Re-runnable: a previous pass left the shell renderers disabled, which would make the
            // collector below see nothing. Restore first so Apply/Preview always start from source.
            RestoreExisting(root.transform);

            var shell = CollectShellRenderers(root.transform);

            if (preview)
            {
                var names = string.Join("\n  ", shell.Select(r => GetPath(r.transform, root.transform)));
                Debug.Log($"[Combiner] PREVIEW {Path.GetFileName(path)}: {shell.Count} shell renderer(s) would be combined:\n  {names}");
                PrefabUtility.UnloadPrefabContents(root);
                return;
            }

            if (shell.Count < 2)
            {
                Debug.LogWarning($"[Combiner] {Path.GetFileName(path)}: only {shell.Count} shell renderer(s), nothing to combine.");
                PrefabUtility.UnloadPrefabContents(root);
                return;
            }

            Combine(root, shell, path);
            ApplyGiFlags(root.transform);

            PrefabUtility.SaveAsPrefabAsset(root, path);
            PrefabUtility.UnloadPrefabContents(root);
        }

        /// <summary>Invariant shell only: skips Content, WallGapController-owned walls and WeightedRandomGroup subtrees.</summary>
        private static List<MeshRenderer> CollectShellRenderers(Transform root)
        {
            var result = new List<MeshRenderer>();
            Walk(root, root, result);
            return result;
        }

        private static void Walk(Transform t, Transform root, List<MeshRenderer> result)
        {
            if (t != root)
            {
                if (t.name == "Content" || t.name == CombinedChildName)
                    return;
                if (t.GetComponent<WallGapController>() != null)
                    return;
                if (t.GetComponent<WeightedRandomGroup>() != null)
                    return;
                if (t.GetComponent<RoomSocket>() != null)
                    return;

                var mr = t.GetComponent<MeshRenderer>();
                var mf = t.GetComponent<MeshFilter>();
                if (mr != null && mr.enabled && mf != null && mf.sharedMesh != null)
                    result.Add(mr);
            }

            for (int i = 0; i < t.childCount; i++)
                Walk(t.GetChild(i), root, result);
        }

        private static void Combine(GameObject root, List<MeshRenderer> shell, string prefabPath)
        {
            var byMaterial = new Dictionary<Material, List<CombineInstance>>();
            foreach (var mr in shell)
            {
                var mesh = mr.GetComponent<MeshFilter>().sharedMesh;
                var mats = mr.sharedMaterials;

                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    var mat = sub < mats.Length ? mats[sub] : mats.LastOrDefault();
                    if (mat == null)
                        continue;

                    if (!byMaterial.TryGetValue(mat, out var list))
                    {
                        list = new List<CombineInstance>();
                        byMaterial[mat] = list;
                    }

                    list.Add(new CombineInstance
                    {
                        mesh = mesh,
                        subMeshIndex = sub,
                        transform = root.transform.worldToLocalMatrix * mr.transform.localToWorldMatrix
                    });
                }
            }

            var materials = byMaterial.Keys.ToList();
            var perMaterial = new List<CombineInstance>();
            foreach (var mat in materials)
            {
                var materialMesh = new Mesh { indexFormat = IndexFormat.UInt32 };
                materialMesh.CombineMeshes(byMaterial[mat].ToArray(), true, true);
                perMaterial.Add(new CombineInstance { mesh = materialMesh, subMeshIndex = 0, transform = Matrix4x4.identity });
            }

            var raw = new Mesh { indexFormat = IndexFormat.UInt32 };
            raw.CombineMeshes(perMaterial.ToArray(), false, false);

            var combined = BuildStitchedShellMesh(raw);
            combined.name = Path.GetFileNameWithoutExtension(prefabPath) + "_Shell";
            Object.DestroyImmediate(raw);

            if (!AssetDatabase.IsValidFolder(OutputFolder))
                AssetDatabase.CreateFolder("Assets/_AfterAll/Data", "CombinedRoomMeshes");

            string meshPath = $"{OutputFolder}/{combined.name}.asset";
            AssetDatabase.DeleteAsset(meshPath);
            AssetDatabase.CreateAsset(combined, meshPath);

            var existing = root.transform.Find(CombinedChildName);
            if (existing != null)
                Object.DestroyImmediate(existing.gameObject);

            var go = new GameObject(CombinedChildName);
            go.transform.SetParent(root.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = combined;

            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = materials.ToArray();
            GameObjectUtility.SetStaticEditorFlags(go, ShellFlags);

            var so = new SerializedObject(renderer);
            so.FindProperty("m_ReceiveGI").enumValueIndex = 0;      // Lightmaps
            so.FindProperty("m_StitchLightmapSeams").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();

            foreach (var mr in shell)
                mr.enabled = false;

            go.AddComponent<CombinedStaticGroup>().SourceRenderers = shell.ToArray();

            Debug.Log($"[Combiner] {Path.GetFileName(prefabPath)}: combined {shell.Count} renderer(s) / " +
                      $"{materials.Count} material(s) into {CombinedChildName} " +
                      $"({combined.vertexCount} verts). Originals disabled, colliders intact.");
        }

        /// <summary>
        /// Rebuilds the combined shell with a seam-free UV2.
        ///
        /// The room kit butts separate wall boxes together, and each box samples its own region of
        /// the wallpaper atlas — so coincident vertices agree on position and normal but disagree on
        /// uv0. Welding the real mesh would therefore tear the wallpaper, and leaving it unwelded
        /// leaves the pieces topologically disconnected, which is what makes the unwrapper emit a
        /// separate lightmap chart per box and produce the visible seam.
        ///
        /// So the unwrap runs on a throwaway proxy welded on position+normal only (uv0 ignored):
        /// coplanar neighbours become one continuous surface there and land in a single chart.
        ///
        /// The resulting uv2 is transferred back per TRIANGLE CORNER, not per vertex. That matters:
        /// GenerateSecondaryUVSet splits a vertex wherever a chart boundary runs through it, giving
        /// one position+normal two legitimate uv2 values. Keying the transfer on position+normal
        /// would collapse those two and hand every triangle whichever value was written first, so
        /// triangles on the far side of a boundary end up sampling an unrelated patch of the
        /// lightmap — which shows up as bright/dark streaks smeared across the walls. Walking corners
        /// instead keeps each triangle on its own side of the boundary, and the output mesh
        /// re-splits vertices on (source vertex, uv2) so both values survive.
        /// </summary>
        private static Mesh BuildStitchedShellMesh(Mesh source, float positionTolerance = 1e-4f)
        {
            var verts   = source.vertices;
            var normals = source.normals;
            var uv0     = source.uv;
            float posScale = 1f / positionTolerance;

            var remap      = new int[verts.Length];
            var lookup     = new Dictionary<(int, int, int, int, int, int), int>(verts.Length);
            var proxyVerts = new List<Vector3>(verts.Length);
            var proxyNorms = new List<Vector3>(verts.Length);

            for (int i = 0; i < verts.Length; i++)
            {
                var p = verts[i];
                var n = normals[i];
                var key = (
                    Mathf.RoundToInt(p.x * posScale), Mathf.RoundToInt(p.y * posScale), Mathf.RoundToInt(p.z * posScale),
                    Mathf.RoundToInt(n.x * 1000f),    Mathf.RoundToInt(n.y * 1000f),    Mathf.RoundToInt(n.z * 1000f));

                if (!lookup.TryGetValue(key, out int index))
                {
                    index = proxyVerts.Count;
                    lookup.Add(key, index);
                    proxyVerts.Add(p);
                    proxyNorms.Add(n);
                }
                remap[i] = index;
            }

            // One flat corner list in submesh order, so proxy corner i always describes source corner i.
            var sourceCorners  = new List<int>();
            var submeshLengths = new int[source.subMeshCount];
            for (int sub = 0; sub < source.subMeshCount; sub++)
            {
                var tris = source.GetTriangles(sub);
                submeshLengths[sub] = tris.Length;
                sourceCorners.AddRange(tris);
            }

            var proxyCorners = new int[sourceCorners.Count];
            for (int i = 0; i < sourceCorners.Count; i++)
                proxyCorners[i] = remap[sourceCorners[i]];

            var proxy = new Mesh { indexFormat = IndexFormat.UInt32 };
            proxy.SetVertices(proxyVerts);
            proxy.SetNormals(proxyNorms);
            proxy.SetTriangles(proxyCorners, 0);

            // hardAngle stays near Unity's default so 90 degree corners still split into their own
            // charts — merging them would bleed wall light onto the floor.
            //
            // packMargin is deliberately well above Unity's default: the kit contains 0.25m-wide
            // pillar and wall-edge faces, which are only a couple of texels across once packed, so a
            // default margin lets neighbouring charts bleed into them and streak the walls.
            UnwrapParam.SetDefaults(out var unwrap);
            unwrap.hardAngle  = 88f;
            unwrap.packMargin = 0.02f;
            Unwrapping.GenerateSecondaryUVSet(proxy, unwrap);

            var unwrappedCorners = proxy.triangles;
            var proxyUv2         = proxy.uv2;

            if (unwrappedCorners.Length != sourceCorners.Count || proxyUv2 == null || proxyUv2.Length == 0)
            {
                Debug.LogError("[Combiner] Unwrap changed the triangle list; cannot transfer lightmap UVs. " +
                               "Falling back to unwrapping the combined mesh directly (per-object seams remain).");
                Object.DestroyImmediate(proxy);
                Unwrapping.GenerateSecondaryUVSet(source, unwrap);
                return Object.Instantiate(source);
            }

            var outVerts = new List<Vector3>(verts.Length);
            var outNorms = new List<Vector3>(verts.Length);
            var outUv0   = new List<Vector2>(verts.Length);
            var outUv2   = new List<Vector2>(verts.Length);
            var cornerLookup = new Dictionary<(int, int, int), int>(verts.Length);
            var outSubmeshes = new List<int>[source.subMeshCount];

            int cursor = 0;
            for (int sub = 0; sub < source.subMeshCount; sub++)
            {
                var indices = new List<int>(submeshLengths[sub]);
                for (int i = 0; i < submeshLengths[sub]; i++)
                {
                    int corner       = cursor + i;
                    int sourceVertex = sourceCorners[corner];
                    Vector2 uv       = proxyUv2[unwrappedCorners[corner]];

                    var key = (sourceVertex, Mathf.RoundToInt(uv.x * 100000f), Mathf.RoundToInt(uv.y * 100000f));
                    if (!cornerLookup.TryGetValue(key, out int index))
                    {
                        index = outVerts.Count;
                        cornerLookup.Add(key, index);
                        outVerts.Add(verts[sourceVertex]);
                        outNorms.Add(normals[sourceVertex]);
                        outUv0.Add(uv0 != null && uv0.Length == verts.Length ? uv0[sourceVertex] : Vector2.zero);
                        outUv2.Add(uv);
                    }
                    indices.Add(index);
                }

                outSubmeshes[sub] = indices;
                cursor += submeshLengths[sub];
            }

            var dst = new Mesh { indexFormat = IndexFormat.UInt32, subMeshCount = source.subMeshCount };
            dst.SetVertices(outVerts);
            dst.SetNormals(outNorms);
            dst.SetUVs(0, outUv0);
            dst.SetUVs(1, outUv2);
            for (int sub = 0; sub < source.subMeshCount; sub++)
                dst.SetTriangles(outSubmeshes[sub], sub);

            dst.RecalculateBounds();
            dst.RecalculateTangents();

            Object.DestroyImmediate(proxy);
            return dst;
        }

        /// <summary>
        /// The combined shell plus the door walls contribute GI; genuinely runtime-variable geometry
        /// (content presets, loot, panels) does not.
        ///
        /// Door walls are kept in the bake even though WallGapController repositions them at runtime.
        /// In the prefab the Left/Right pair sits side by side forming a closed wall — no overlap —
        /// so the closed-state bake is geometrically valid, and opening the gap merely stretches the
        /// baked lighting along the wall axis exactly as it already stretches the wallpaper's uv0.
        /// Excluding them instead leaves them lit by ambient alone, which reads as near-black next to
        /// the lightmapped shell.
        /// </summary>
        private static void ApplyGiFlags(Transform root)
        {
            int cleared = 0;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == CombinedChildName || t.parent != null && t.parent.name == CombinedChildName)
                    continue;
                if (t.GetComponentInParent<WallGapController>() != null)
                    continue;

                var flags = GameObjectUtility.GetStaticEditorFlags(t.gameObject);
                if ((flags & StaticEditorFlags.ContributeGI) == 0)
                    continue;

                GameObjectUtility.SetStaticEditorFlags(t.gameObject, flags & ~StaticEditorFlags.ContributeGI);
                cleared++;
            }

            if (cleared > 0)
                Debug.Log($"[Combiner] Contribute GI cleared on {cleared} runtime-variable object(s) (door walls, content, props).");
        }

        private static string ResolvePrefabPath()
        {
            var selected = Selection.activeGameObject;
            if (selected == null)
            {
                Debug.LogError("[Combiner] Select a room prefab in the Project window first.");
                return null;
            }

            string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(selected);
            if (string.IsNullOrEmpty(path))
                path = AssetDatabase.GetAssetPath(selected);

            if (string.IsNullOrEmpty(path) || !path.EndsWith(".prefab"))
            {
                Debug.LogError("[Combiner] Could not resolve a .prefab asset from the selection.");
                return null;
            }

            return path;
        }

        private static string GetPath(Transform t, Transform root)
        {
            var parts = new List<string>();
            while (t != null && t != root)
            {
                parts.Add(t.name);
                t = t.parent;
            }
            parts.Reverse();
            return string.Join("/", parts);
        }
    }
}
