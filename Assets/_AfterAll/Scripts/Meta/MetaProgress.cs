using System;
using System.IO;
using UnityEngine;

namespace AfterAll.Meta
{
    /// <summary>
    /// First save slice (S3): banked Echo currency, the only thing that survives a run.
    /// Static + lazy-loaded so any system can read/write it without a scene singleton.
    /// </summary>
    public static class MetaProgress
    {
        [Serializable]
        private class SaveData
        {
            public int bankedEchoes;
        }

        private const string SaveFileName = "meta_save.json";

        private static SaveData _data;

        public static int BankedEchoes => Data.bankedEchoes;

        /// <summary>
        /// Wipes the banked total. There is no currency sink yet (the Uncanny Shop is S7), so the
        /// number only ever grows and a fresh Play session opens showing whatever old test runs
        /// left behind — which reads as a bug. Use AfterAll/Meta/Reset Banked Echoes to clear it.
        /// </summary>
        public static void ResetBanked()
        {
            Data.bankedEchoes = 0;
            Save();
        }

        /// <summary>Adds to the banked total and immediately persists it.</summary>
        public static void AddBanked(int amount)
        {
            if (amount <= 0)
                return;

            Data.bankedEchoes += amount;
            Save();
        }

        private static SaveData Data
        {
            get
            {
                if (_data == null)
                    Load();
                return _data;
            }
        }

        private static string SavePath => Path.Combine(Application.persistentDataPath, SaveFileName);

        private static void Load()
        {
            try
            {
                if (File.Exists(SavePath))
                {
                    _data = JsonUtility.FromJson<SaveData>(File.ReadAllText(SavePath));
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"MetaProgress: failed to load save ({e.Message}), starting fresh.");
            }

            _data ??= new SaveData();
        }

        private static void Save()
        {
            try
            {
                File.WriteAllText(SavePath, JsonUtility.ToJson(_data));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"MetaProgress: failed to save ({e.Message}).");
            }
        }
    }
}
