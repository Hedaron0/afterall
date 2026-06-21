#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem.OnScreen;
using UnityEngine.UI;

namespace AfterAll.EditorTools
{
    /// <summary>
    /// Mobile jump/crouch zones — parented directly under Canvas so anchors behave.
    /// Run AfterAll → Setup Mobile HUD to reset layout. Zones start semi-visible for tuning.
    /// </summary>
    public static class MobileWiring
    {
        private const float ZoneWidth  = 200f;
        private const float ZoneHeight = 160f;
        private const float EdgePad    = 20f;
        private static readonly Color CrouchDebugColor = new(0.25f, 0.55f, 1f, 0.28f);
        private static readonly Color JumpDebugColor   = new(0.35f, 1f, 0.45f, 0.28f);

        [MenuItem("AfterAll/Setup Mobile HUD")]
        public static void SetupMobileHUD()
        {
            var hud = GameObject.Find("HUD");
            if (hud == null)
            {
                Debug.LogError("[AfterAll] HUD object not found in scene.");
                return;
            }

            DestroyIfExists("Joystick_BG");
            DestroyIfExists("Joystick_Handle");
            DestroyIfExists("InteractButton");

            var canvas = hud.GetComponentInParent<Canvas>();
            if (canvas == null)
                canvas = Object.FindAnyObjectByType<Canvas>();

            if (canvas == null)
            {
                Debug.LogError("[AfterAll] Canvas not found in scene.");
                return;
            }

            // Parent under Canvas (not HUD Transform) — fixes weird anchor positions.
            var mobileControls = GetOrCreateRectChild(canvas.transform, "MobileControls");
            StretchFull(mobileControls);
            mobileControls.SetAsLastSibling();

            DestroyChildIfExists(mobileControls, "MoveZone");
            DestroyChildIfExists(mobileControls, "ActionStack");
            DestroyChildIfExists(hud.transform, "MobileControls");

            SetupCornerZone(mobileControls, "CrouchZone", topLeft: true,
                "<Keyboard>/c", "Crouch", CrouchDebugColor);

            SetupCornerZone(mobileControls, "JumpZone", topLeft: false,
                "<Keyboard>/space", "Jump", JumpDebugColor);

            var player = GameObject.FindGameObjectWithTag("Player") ?? GameObject.Find("Player");
            if (player != null)
            {
                if (player.GetComponent<AfterAll.Player.MobileTouchMove>() == null)
                    player.AddComponent<AfterAll.Player.MobileTouchMove>();
            }
            else
            {
                Debug.LogWarning("[AfterAll] Player not found — add MobileTouchMove manually.");
            }

            var mobileHUD = hud.GetComponent<AfterAll.UI.MobileHUD>();
            if (mobileHUD == null)
                mobileHUD = hud.AddComponent<AfterAll.UI.MobileHUD>();

            var mobileHUDSO = new SerializedObject(mobileHUD);
            mobileHUDSO.FindProperty("_mobileControlsRoot").objectReferenceValue = mobileControls.gameObject;
            mobileHUDSO.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

            Debug.Log(
                "[AfterAll] Mobile zones reset.\n" +
                "  CrouchZone — top-left (blue tint). JumpZone — top-right (green tint).\n" +
                "  Scale via Rect Transform Width/Height or drag handles in Scene (2D).\n" +
                "  When done: Image → Color → A = 0 on each zone to hide.\n" +
                "  MobileHUD → Simulate Mobile In Editor → Play to test.");
        }

        // ── zone layout ───────────────────────────────────────────────────────

        private static void SetupCornerZone(
            RectTransform parent, string name, bool topLeft,
            string controlPath, string labelText, Color debugColor)
        {
            var rect = GetOrCreateRectChild(parent, name);

            if (topLeft)
            {
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot     = new Vector2(0f, 1f);
                rect.anchoredPosition = new Vector2(EdgePad, -EdgePad);
            }
            else
            {
                rect.anchorMin = new Vector2(1f, 1f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot     = new Vector2(1f, 1f);
                rect.anchoredPosition = new Vector2(-EdgePad, -EdgePad);
            }

            rect.sizeDelta = new Vector2(ZoneWidth, ZoneHeight);

            var image = rect.gameObject.GetComponent<Image>();
            if (image == null)
                image = rect.gameObject.AddComponent<Image>();
            image.color = debugColor;
            image.raycastTarget = true;

            if (rect.gameObject.GetComponent<Button>() == null)
                rect.gameObject.AddComponent<Button>();

            WireOnScreenButton(rect.gameObject, controlPath);
            EnsureLabel(rect, labelText);
        }

        // ── helpers ───────────────────────────────────────────────────────────

        private static RectTransform GetOrCreateRectChild(Transform parent, string name)
        {
            var existing = parent.Find(name) as RectTransform;
            if (existing != null)
                return existing;

            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.GetComponent<RectTransform>();
        }

        private static void EnsureLabel(RectTransform parent, string text)
        {
            var labelTr = parent.Find("Label") as RectTransform;
            if (labelTr == null)
            {
                var labelObj = new GameObject("Label");
                labelObj.transform.SetParent(parent, false);
                labelTr = labelObj.AddComponent<RectTransform>();
                StretchFull(labelTr);
            }

            var label = labelTr.GetComponent<TextMeshProUGUI>();
            if (label == null)
                label = labelTr.gameObject.AddComponent<TextMeshProUGUI>();

            label.text = text;
            label.alignment = TextAlignmentOptions.Center;
            label.fontSize = 26f;
            label.color = new Color(1f, 1f, 1f, 0.85f);
            label.fontStyle = FontStyles.Bold;
            label.raycastTarget = false;
        }

        private static void WireOnScreenButton(GameObject go, string controlPath)
        {
            var onScreenBtn = go.GetComponent<OnScreenButton>();
            if (onScreenBtn == null)
                onScreenBtn = go.AddComponent<OnScreenButton>();

            var so = new SerializedObject(onScreenBtn);
            so.FindProperty("m_ControlPath").stringValue = controlPath;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void StretchFull(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.pivot = new Vector2(0.5f, 0.5f);
        }

        private static void DestroyIfExists(string name)
        {
            var go = GameObject.Find(name);
            if (go != null)
                Object.DestroyImmediate(go);
        }

        private static void DestroyChildIfExists(Transform parent, string name)
        {
            var child = parent.Find(name);
            if (child != null)
                Object.DestroyImmediate(child.gameObject);
        }
    }
}
#endif
