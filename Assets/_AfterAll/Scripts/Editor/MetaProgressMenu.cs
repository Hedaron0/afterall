using AfterAll.Meta;
using UnityEditor;
using UnityEngine;

namespace AfterAll.EditorTools
{
    /// <summary>
    /// Dev-only access to the persistent save slice. Banked Echoes carry across Play sessions by
    /// design, but with no shop to spend them in yet, old test runs leave a number on the HUD that
    /// looks like a bug on a fresh start.
    /// </summary>
    public static class MetaProgressMenu
    {
        [MenuItem("AfterAll/Meta/Reset Banked Echoes")]
        private static void ResetBanked()
        {
            int before = MetaProgress.BankedEchoes;
            MetaProgress.ResetBanked();
            Debug.Log($"[MetaProgress] Banked Echoes reset: {before} -> {MetaProgress.BankedEchoes}.");
        }

        [MenuItem("AfterAll/Meta/Log Banked Echoes")]
        private static void LogBanked() =>
            Debug.Log($"[MetaProgress] Banked Echoes = {MetaProgress.BankedEchoes} " +
                      $"(save file: {Application.persistentDataPath}/meta_save.json)");
    }
}
