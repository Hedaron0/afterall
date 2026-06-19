#if UNITY_EDITOR
using AfterAll.Audio;
using AfterAll.Items;
using AfterAll.Player;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AfterAll.EditorTools
{
    public static class SfxWiring
    {
        const string SfxFolder = "Assets/Audio/SFX";
        const string SfxPackFolder = "Assets/Audio/SFX/pack";

        [MenuItem("AfterAll/Wire Audio")]
        public static void WireAudio()
        {
            AssetDatabase.Refresh();

            WireFootsteps();
            WireInventoryAudio();
            WireKeyPickup();
            WireDoors();
            WireAmbience();

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("[AfterAll] Audio wired. Save scene (Ctrl+S).");
        }

        static void WireFootsteps()
        {
            var player = GameObject.Find("Player");
            if (player == null)
            {
                Debug.LogWarning("[AfterAll] Player not found — skipped footsteps.");
                return;
            }

            Ensure2DClipInFolder(SfxPackFolder, "sfx100v2_footstep_wood_02.ogg");
            Ensure2DClipInFolder(SfxPackFolder, "sfx100v2_footstep_wood_03.ogg");

            if (player.GetComponent<AudioSource>() == null)
                player.AddComponent<AudioSource>();

            var movement = player.GetComponent<PlayerMovement>();
            if (movement == null)
            {
                Debug.LogWarning("[AfterAll] PlayerMovement not found — skipped footsteps.");
                return;
            }

            var so = new SerializedObject(movement);
            so.FindProperty("_footstepClips").arraySize = 2;
            so.FindProperty("_footstepClips").GetArrayElementAtIndex(0).objectReferenceValue =
                LoadClipFromFolder(SfxPackFolder, "sfx100v2_footstep_wood_02.ogg");
            so.FindProperty("_footstepClips").GetArrayElementAtIndex(1).objectReferenceValue =
                LoadClipFromFolder(SfxPackFolder, "sfx100v2_footstep_wood_03.ogg");
            so.FindProperty("_stepDistance").floatValue = 1.35f;
            so.FindProperty("_footstepVolume").floatValue = 0.6f;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void WireInventoryAudio()
        {
            var player = GameObject.Find("Player");
            if (player == null)
                return;

            var audio = player.GetComponent<InventoryAudio>();
            if (audio == null)
                audio = player.AddComponent<InventoryAudio>();

            var so = new SerializedObject(audio);
            so.FindProperty("_keyEquipClip").objectReferenceValue = LoadClip("key_equip.ogg");
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void WireKeyPickup()
        {
            var key = GameObject.Find("Key");
            if (key == null)
            {
                Debug.LogWarning("[AfterAll] Key not found — skipped key pickup sound.");
                return;
            }

            var pickup = key.GetComponent<KeyPickup>();
            if (pickup == null)
                return;

            var so = new SerializedObject(pickup);
            so.FindProperty("_pickupClip").objectReferenceValue = LoadClip("key_pickup.ogg");
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void WireDoors()
        {
            var unlock = LoadClip("door_unlock.ogg");
            var open = LoadClip("door_open.ogg");
            var close = LoadClip("door_close.ogg");

            foreach (var door in Object.FindObjectsByType<global::AfterAll.Door.Door>())
            {
                var so = new SerializedObject(door);
                so.FindProperty("_unlockClip").objectReferenceValue = unlock;
                so.FindProperty("_openClip").objectReferenceValue = open;
                so.FindProperty("_closeClip").objectReferenceValue = close;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        static void WireAmbience()
        {
            var ambience = GameObject.Find("Ambience");
            if (ambience == null)
            {
                ambience = new GameObject("Ambience");
                ambience.AddComponent<AudioSource>();
                ambience.AddComponent<AmbienceLoop>();
            }

            if (ambience.GetComponent<AudioSource>() == null)
                ambience.AddComponent<AudioSource>();

            var loop = ambience.GetComponent<AmbienceLoop>();
            if (loop == null)
                loop = ambience.AddComponent<AmbienceLoop>();

            var so = new SerializedObject(loop);
            so.FindProperty("_clip").objectReferenceValue = LoadClip("ambience_fluorescent.ogg");
            so.FindProperty("_volume").floatValue = 0.18f;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static AudioClip LoadClip(string fileName)
        {
            return AssetDatabase.LoadAssetAtPath<AudioClip>($"{SfxFolder}/{fileName}");
        }

        static AudioClip LoadClipFromFolder(string folder, string fileName)
        {
            return AssetDatabase.LoadAssetAtPath<AudioClip>($"{folder}/{fileName}");
        }

        static void Ensure2DClipInFolder(string folder, string fileName)
        {
            string path = $"{folder}/{fileName}";
            var importer = AssetImporter.GetAtPath(path) as AudioImporter;
            if (importer == null)
                return;

            var settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.DecompressOnLoad;
            importer.defaultSampleSettings = settings;
            importer.forceToMono = true;
            importer.loadInBackground = false;
            importer.SaveAndReimport();
        }
    }
}
#endif
