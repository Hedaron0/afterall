#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem.OnScreen;
using UnityEngine.UI;

namespace AfterAll.EditorTools
{
    public static class MobileWiring
    {
        [MenuItem("AfterAll/Setup Mobile HUD")]
        public static void SetupMobileHUD()
        {
            bool changed = false;

            // ── 1. Verify / wire the joystick ─────────────────────────────────

            var joystickHandle = GameObject.Find("Joystick_Handle");
            var joystickBG     = GameObject.Find("Joystick_BG");

            if (joystickHandle == null || joystickBG == null)
            {
                Debug.LogError("[AfterAll] Joystick_BG / Joystick_Handle not found. " +
                               "Make sure the joystick GameObjects exist in the scene.");
                return;
            }

            var stick = joystickHandle.GetComponent<OnScreenStick>();
            if (stick == null)
                stick = joystickHandle.AddComponent<OnScreenStick>();

            var stickSO = new SerializedObject(stick);
            stickSO.FindProperty("m_ControlPath").stringValue = "<Gamepad>/leftStick";
            stickSO.ApplyModifiedPropertiesWithoutUndo();
            changed = true;
            Debug.Log("[AfterAll] OnScreenStick → <Gamepad>/leftStick wired on Joystick_Handle.");

            // ── 2. Find HUD ───────────────────────────────────────────────────

            var hud = GameObject.Find("HUD");
            if (hud == null)
            {
                Debug.LogError("[AfterAll] HUD object not found in scene.");
                return;
            }

            // ── 3. Find or create the Interact button ─────────────────────────

            var existingBtn = hud.transform.Find("InteractButton");
            GameObject interactBtn = existingBtn != null
                ? existingBtn.gameObject
                : CreateInteractButton(hud);

            // Ensure Button component for visual press feedback
            if (interactBtn.GetComponent<Button>() == null)
                interactBtn.AddComponent<Button>();

            // Wire OnScreenButton → <Keyboard>/e (simulates E key → triggers Interact action)
            var onScreenBtn = interactBtn.GetComponent<OnScreenButton>();
            if (onScreenBtn == null)
                onScreenBtn = interactBtn.AddComponent<OnScreenButton>();

            var onScreenBtnSO = new SerializedObject(onScreenBtn);
            onScreenBtnSO.FindProperty("m_ControlPath").stringValue = "<Keyboard>/e";
            onScreenBtnSO.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log("[AfterAll] OnScreenButton → <Keyboard>/e wired on InteractButton.");

            // ── 4. Wire MobileHUD ─────────────────────────────────────────────

            var mobileHUD = hud.GetComponent<AfterAll.UI.MobileHUD>();
            if (mobileHUD == null)
                mobileHUD = hud.AddComponent<AfterAll.UI.MobileHUD>();

            var mobileHUDSO = new SerializedObject(mobileHUD);
            mobileHUDSO.FindProperty("_joystickRoot").objectReferenceValue        = joystickBG;
            mobileHUDSO.FindProperty("_interactButtonRoot").objectReferenceValue  = interactBtn;
            mobileHUDSO.ApplyModifiedPropertiesWithoutUndo();
            changed = true;
            Debug.Log("[AfterAll] MobileHUD wired on HUD.");

            // ── 5. Save hint ──────────────────────────────────────────────────

            if (changed)
            {
                EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
                Debug.Log("[AfterAll] Mobile HUD setup complete. Save the scene (Ctrl+S).");
            }
        }

        // ── helpers ───────────────────────────────────────────────────────────

        private static GameObject CreateInteractButton(GameObject hud)
        {
            var btn = new GameObject("InteractButton");
            btn.transform.SetParent(hud.transform, false);

            var rect = btn.AddComponent<RectTransform>();
            // Anchor: bottom-right, above the joystick knob area
            rect.anchorMin        = new Vector2(1f, 0f);
            rect.anchorMax        = new Vector2(1f, 0f);
            rect.pivot            = new Vector2(1f, 0f);
            rect.anchoredPosition = new Vector2(-30f, 30f);
            rect.sizeDelta        = new Vector2(130f, 130f);

            // Semi-transparent circle-ish background
            var bg = btn.AddComponent<Image>();
            bg.color = new Color(1f, 1f, 1f, 0.20f);

            // Label
            var labelObj  = new GameObject("Label");
            labelObj.transform.SetParent(btn.transform, false);

            var labelRect            = labelObj.AddComponent<RectTransform>();
            labelRect.anchorMin      = Vector2.zero;
            labelRect.anchorMax      = Vector2.one;
            labelRect.sizeDelta      = Vector2.zero;
            labelRect.anchoredPosition = Vector2.zero;

            var label           = labelObj.AddComponent<TextMeshProUGUI>();
            label.text          = "E";
            label.alignment     = TextAlignmentOptions.Center;
            label.fontSize      = 40f;
            label.color         = Color.white;
            label.fontStyle     = FontStyles.Bold;
            label.raycastTarget = false; // parent Image handles raycasts

            Debug.Log("[AfterAll] InteractButton created under HUD.");
            return btn;
        }
    }
}
#endif
