#if UNITY_EDITOR
using AfterAll.Player;
using AfterAll.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace AfterAll.EditorTools
{
    public static class StaminaWiring
    {
        private const string SettingsFolder    = "Assets/_AfterAll/Settings";
        private const string SettingsAssetPath = SettingsFolder + "/StaminaSettings.asset";

        // ── Main entry ────────────────────────────────────────────────────────

        [MenuItem("AfterAll/Wire Stamina System")]
        public static void WireStaminaSystem()
        {
            var settings = EnsureStaminaSettings();
            WireStaminaOnPlayer(settings);
            var sprintBtn = CreateSprintButtonUI();

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

            Debug.Log(
                "[AfterAll] Stamina system wired.\n" +
                $"  StaminaSettings → {SettingsAssetPath}\n" +
                "  Stamina component added to Player.\n" +
                $"  SprintButton GO created: '{sprintBtn.name}' — parent it under InventoryPanel and position/scale to taste.");
        }

        // ── Stamina settings asset ─────────────────────────────────────────────

        private static StaminaSettings EnsureStaminaSettings()
        {
            var existing = AssetDatabase.LoadAssetAtPath<StaminaSettings>(SettingsAssetPath);
            if (existing != null) return existing;

            if (!AssetDatabase.IsValidFolder(SettingsFolder))
            {
                if (!AssetDatabase.IsValidFolder("Assets/_AfterAll"))
                    AssetDatabase.CreateFolder("Assets", "_AfterAll");
                AssetDatabase.CreateFolder("Assets/_AfterAll", "Settings");
            }

            var asset = ScriptableObject.CreateInstance<StaminaSettings>();
            AssetDatabase.CreateAsset(asset, SettingsAssetPath);
            AssetDatabase.SaveAssets();
            return asset;
        }

        // ── Add Stamina to Player ──────────────────────────────────────────────

        private static void WireStaminaOnPlayer(StaminaSettings settings)
        {
            var player = GameObject.FindGameObjectWithTag("Player") ?? GameObject.Find("Player");
            if (player == null)
            {
                Debug.LogError("[AfterAll] Player not found in scene.");
                return;
            }

            var stamina = player.GetComponent<Stamina>();
            if (stamina == null)
                stamina = player.AddComponent<Stamina>();

            var so = new SerializedObject(stamina);
            so.FindProperty("_settings").objectReferenceValue = settings;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ── Sprint button UI ───────────────────────────────────────────────────
        //
        //  Visual trick — two layers, no custom sprite needed:
        //
        //   SprintButton  (root, transparent Image for raycasts + Button)
        //     ├── StaminaRing  (full-size yellow disc, Radial360 = pie that drains)
        //     └── ButtonFill   (slightly smaller dark square on top, covers the centre)
        //
        //  The dark square hides the centre of the disc.
        //  Only the outer arc shows → looks like a border ring.
        //  As stamina drains, the yellow arc shrinks clockwise from the top.

        private const float ButtonSize  = 80f;
        private const float BorderWidth = 6f;   // width of the visible arc ring

        private static GameObject CreateSprintButtonUI()
        {
            // ── Root (button) ─────────────────────────────────────────────────
            var btnGo = new GameObject("SprintButton", typeof(RectTransform));
            var rect  = btnGo.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(ButtonSize, ButtonSize);

            // Transparent root image — needed so Button has a raycast target
            var rootImg = btnGo.AddComponent<Image>();
            rootImg.color = Color.clear;

            var btn = btnGo.AddComponent<Button>();
            var cols = btn.colors;
            cols.normalColor = Color.white;
            cols.pressedColor = new Color(0.75f, 0.75f, 0.75f, 1f);
            btn.colors = cols;
            btn.targetGraphic = rootImg;

            // ── Layer 0: StaminaRing (bottom, yellow Radial360 disc) ──────────
            var ringGo   = new GameObject("StaminaRing", typeof(RectTransform));
            var ringRect = ringGo.GetComponent<RectTransform>();
            ringRect.SetParent(btnGo.transform, false);
            StretchFull(ringRect);      // same size as root = full 80x80

            var ringImg           = ringGo.AddComponent<Image>();
            ringImg.color         = new Color(1f, 0.85f, 0.2f, 0.9f);
            ringImg.type          = Image.Type.Filled;
            ringImg.fillMethod    = Image.FillMethod.Radial360;
            ringImg.fillOrigin    = (int)Image.Origin360.Top;
            ringImg.fillClockwise = true;
            ringImg.fillAmount    = 1f;
            ringImg.raycastTarget = false;

            // ── Layer 1: ButtonFill (top, dark square covering the centre) ────
            var fillGo   = new GameObject("ButtonFill", typeof(RectTransform));
            var fillRect = fillGo.GetComponent<RectTransform>();
            fillRect.SetParent(btnGo.transform, false);

            // Inset by BorderWidth on every side so the ring arc shows as border
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = new Vector2(BorderWidth, BorderWidth);
            fillRect.offsetMax = new Vector2(-BorderWidth, -BorderWidth);
            fillRect.pivot     = new Vector2(0.5f, 0.5f);

            var fillImg       = fillGo.AddComponent<Image>();
            fillImg.color     = new Color(0.12f, 0.12f, 0.12f, 0.85f);
            fillImg.raycastTarget = false;

            // ── SprintButtonUI component ──────────────────────────────────────
            var uiComp = btnGo.AddComponent<SprintButtonUI>();
            var uiSo   = new SerializedObject(uiComp);
            uiSo.FindProperty("_staminaRing").objectReferenceValue = ringImg;
            uiSo.FindProperty("_fillImage").objectReferenceValue   = fillImg;
            uiSo.ApplyModifiedPropertiesWithoutUndo();

            // ── Parent into InventoryPanel as LAST child (right of slots) ─────
            var inventoryPanel = GameObject.Find("InventoryPanel");
            if (inventoryPanel != null)
            {
                btnGo.transform.SetParent(inventoryPanel.transform, false);
                btnGo.transform.SetAsLastSibling();
                Debug.Log("[AfterAll] SprintButton placed RIGHT of inventory slots. Scale/position to taste in the Hierarchy.");
            }
            else
            {
                var canvas = Object.FindAnyObjectByType<Canvas>();
                if (canvas != null)
                    btnGo.transform.SetParent(canvas.transform, false);
                Debug.LogWarning("[AfterAll] InventoryPanel not found — SprintButton placed on Canvas root. Drag under InventoryPanel manually.");
            }

            return btnGo;
        }

        private static void StretchFull(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.pivot     = new Vector2(0.5f, 0.5f);
        }
    }
}
#endif
