using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using AfterAll.Environment;
using UnityEditor;
using UnityEngine;

namespace AfterAll.Editor
{
    /// <summary>
    /// Batch-runs PaintGrowth across fixed seeds and writes silhouette CSV.
    /// Use after a planner rewrite to compare pass rates vs soft cluster targets.
    /// </summary>
    public static class SilhouetteBatchReport
    {
        private const string FootprintFolder = "Assets/_AfterAll/Data/RoomFootprints";
        private const string OutputRelative = "Logs/silhouette_batch.csv";
        private const int DefaultRoomCount = 20;
        private const int DefaultSampleCount = 40;
        private const int BaseSeed = 1000;

        [MenuItem("AfterAll/Generation/Batch Silhouette Report")]
        private static void Run()
        {
            var library = LoadLibrary();
            if (library.Count == 0)
            {
                EditorUtility.DisplayDialog(
                    "Batch Silhouette Report",
                    "No footprints found. Bake Room Footprints first.",
                    "OK");
                return;
            }

            PaintGrowthConfig config = PaintGrowthConfig.FromTargetRoomCount(DefaultRoomCount);
            var csv = new StringBuilder();
            csv.AppendLine(
                "seed,rooms,conn,aspect,fill,deg,corr,cluster,soft_pass");

            int passCount = 0;
            for (int i = 0; i < DefaultSampleCount; i++)
            {
                int seed = BaseSeed + i;
                LayoutPlan plan = PaintGrowthPlanner.Generate(library, seed, config);
                LayoutSilhouetteReport report = LayoutSilhouetteMetrics.Evaluate(plan, library);
                bool softPass = LayoutSilhouetteMetrics.MeetsSoftClusterTargets(report);
                if (softPass)
                    passCount++;

                csv.Append(seed).Append(',')
                    .Append(report.roomCount).Append(',')
                    .Append(report.connectionCount).Append(',')
                    .Append(report.hullAspect.ToString("F3", CultureInfo.InvariantCulture)).Append(',')
                    .Append(report.packingFill.ToString("F3", CultureInfo.InvariantCulture)).Append(',')
                    .Append(report.meanDegree.ToString("F3", CultureInfo.InvariantCulture)).Append(',')
                    .Append(report.corridorFraction.ToString("F3", CultureInfo.InvariantCulture)).Append(',')
                    .Append(report.clusterScore.ToString("F3", CultureInfo.InvariantCulture)).Append(',')
                    .Append(softPass ? "1" : "0")
                    .AppendLine();
            }

            string outPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", OutputRelative));
            Directory.CreateDirectory(Path.GetDirectoryName(outPath) ?? ".");
            File.WriteAllText(outPath, csv.ToString(), Encoding.UTF8);

            Debug.Log(
                $"[SilhouetteBatchReport] Wrote {DefaultSampleCount} samples to {OutputRelative}. " +
                $"soft-pass={passCount}/{DefaultSampleCount} " +
                $"(rooms={DefaultRoomCount}, targets: aspect≤{LayoutSilhouetteMetrics.TargetMaxHullAspect}, " +
                $"fill≥{LayoutSilhouetteMetrics.TargetMinPackingFill:P0}, " +
                $"cluster≥{LayoutSilhouetteMetrics.TargetMinClusterScore:F2})");

            EditorUtility.RevealInFinder(outPath);
        }

        private static List<RoomFootprint> LoadLibrary()
        {
            var library = new List<RoomFootprint>();
            if (!AssetDatabase.IsValidFolder(FootprintFolder))
                return library;

            string[] guids = AssetDatabase.FindAssets("t:RoomFootprint", new[] { FootprintFolder });
            foreach (string guid in guids)
            {
                RoomFootprint footprint =
                    AssetDatabase.LoadAssetAtPath<RoomFootprint>(AssetDatabase.GUIDToAssetPath(guid));
                if (footprint != null)
                    library.Add(footprint);
            }

            library.Sort((a, b) => string.CompareOrdinal(a.PrefabId, b.PrefabId));
            return library;
        }
    }
}
