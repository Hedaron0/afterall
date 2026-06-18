#if UNITY_EDITOR
using AfterAll.Door;
using AfterAll.Inventories;
using AfterAll.Interaction;
using AfterAll.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace AfterAll.EditorTools
{
  public static class PrototypeRoomWiring
  {
    const string InputActionsPath = "Assets/InputSystem_Actions.inputactions";

    [MenuItem("AfterAll/Wire Prototype Room Scene")]
    public static void WireScene()
    {
      var inventory = Object.FindFirstObjectByType<Inventory>();
      var interactor = Object.FindFirstObjectByType<PlayerInteractor>();
      var hud = GameObject.Find("HUD")?.transform;
      var canvas = Object.FindFirstObjectByType<Canvas>();

      if (inventory == null || interactor == null || hud == null || canvas == null)
      {
        Debug.LogError("[AfterAll] Wire failed — need Player (Inventory, PlayerInteractor), Canvas, and HUD in scene.");
        return;
      }

      WireInventory(inventory, hud);
      WirePrompt(interactor, hud);
      WireFeedback(hud);
      WireCrosshair(interactor, hud);
      WireHeldItem(inventory);
      WireAllDoors();
      FixCanvas(canvas);

      EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
      Debug.Log("[AfterAll] Prototype room wired. Save the scene (Ctrl+S).");
    }

    [MenuItem("AfterAll/Duplicate Unlocked Test Door")]
    public static void DuplicateUnlockedTestDoor()
    {
      var lockedHinge = GameObject.Find("DoorHinge");
      if (lockedHinge == null)
      {
        Debug.LogError("[AfterAll] DoorHinge not found.");
        return;
      }

      if (GameObject.Find("DoorHinge_Open") != null)
      {
        Debug.Log("[AfterAll] DoorHinge_Open already exists — select it in the hierarchy.");
        Selection.activeGameObject = GameObject.Find("DoorHinge_Open");
        return;
      }

      var copy = Object.Instantiate(lockedHinge, lockedHinge.transform.parent);
      copy.name = "DoorHinge_Open";
      copy.transform.SetPositionAndRotation(
        lockedHinge.transform.position + new Vector3(-8f, 0f, 0f),
        lockedHinge.transform.rotation);

      var lockedDoor = copy.GetComponent<LockedDoor>();
      if (lockedDoor != null)
        Object.DestroyImmediate(lockedDoor);

      var hingeDoor = copy.GetComponent<HingeDoor>();
      var door = copy.GetComponent<AfterAll.Door.Door>();
      if (door == null)
        door = copy.AddComponent<AfterAll.Door.Door>();

      var so = new SerializedObject(door);
      so.FindProperty("_hinge").objectReferenceValue = hingeDoor;
      so.ApplyModifiedPropertiesWithoutUndo();

      SetupDoorHierarchy(copy);

      Selection.activeGameObject = copy;
      EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
      Debug.Log("[AfterAll] Created DoorHinge_Open (no key). Save scene (Ctrl+S).");
    }

    [MenuItem("AfterAll/Setup Door Colliders")]
    public static void SetupDoorCollidersMenu()
    {
      WireAllDoors();
      EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
      Debug.Log("[AfterAll] Door colliders + interact zones updated.");
    }

    [MenuItem("AfterAll/Create Door Prefabs")]
    public static void CreateDoorPrefabs()
    {
      const string prefabFolder = "Assets/_AfterAll/Prefabs";
      EnsureFolder("Assets/_AfterAll", "Prefabs");

      WireAllDoors();

      SaveDoorPrefab("DoorHinge", $"{prefabFolder}/LockedDoor.prefab");
      SaveDoorPrefab("DoorHinge_Open", $"{prefabFolder}/Door.prefab");

      AssetDatabase.SaveAssets();
      AssetDatabase.Refresh();
      Debug.Log("[AfterAll] Door prefabs saved to Assets/_AfterAll/Prefabs/");
    }

    static void EnsureFolder(string parent, string child)
    {
      string path = $"{parent}/{child}";
      if (!AssetDatabase.IsValidFolder(path))
        AssetDatabase.CreateFolder(parent, child);
    }

    static void SaveDoorPrefab(string sceneObjectName, string assetPath)
    {
      var hingeObject = GameObject.Find(sceneObjectName);
      if (hingeObject == null)
      {
        Debug.LogWarning($"[AfterAll] Skipped prefab — {sceneObjectName} not in scene.");
        return;
      }

      SetupDoorHierarchy(hingeObject);
      PrefabUtility.SaveAsPrefabAsset(hingeObject, assetPath, out bool success);
      if (success)
        Debug.Log($"[AfterAll] Saved prefab: {assetPath}");
      else
        Debug.LogError($"[AfterAll] Failed to save prefab: {assetPath}");
    }

    static void WireInventory(Inventory inventory, Transform hud)
    {
      WireSlotActions(inventory);

      var panel = hud.Find("InventoryPanel");
      if (panel == null)
      {
        Debug.LogError("[AfterAll] HUD/InventoryPanel not found.");
        return;
      }

      var panelRect = panel.GetComponent<RectTransform>();
      panelRect.anchorMin = new Vector2(1f, 0f);
      panelRect.anchorMax = new Vector2(1f, 0f);
      panelRect.pivot = new Vector2(1f, 0f);
      panelRect.anchoredPosition = new Vector2(-24f, 24f);
      panelRect.sizeDelta = new Vector2(300f, 100f);

      var slots = new InventorySlotUI[Inventory.SlotCount];
      for (int i = 0; i < Inventory.SlotCount; i++)
      {
        var slotTransform = panel.Find($"Slot{i + 1}");
        if (slotTransform == null)
        {
          Debug.LogError($"[AfterAll] Slot{i + 1} not found under InventoryPanel.");
          continue;
        }

        var fillImage = slotTransform.GetComponent<Image>();
        var highlight = EnsureHighlight(slotTransform);

        var slotUi = slotTransform.GetComponent<InventorySlotUI>();
        if (slotUi == null)
          slotUi = slotTransform.gameObject.AddComponent<InventorySlotUI>();

        SetSlotUi(slotUi, inventory, i, fillImage, highlight);
        slots[i] = slotUi;
      }

      var inventoryUi = panel.GetComponent<InventoryUI>();
      if (inventoryUi == null)
        inventoryUi = panel.gameObject.AddComponent<InventoryUI>();

      SetInventoryUi(inventoryUi, inventory, slots);
    }

    static Image EnsureHighlight(Transform slotTransform)
    {
      var existing = slotTransform.Find("Highlight");
      if (existing != null)
        return existing.GetComponent<Image>();

      var highlightGo = new GameObject("Highlight", typeof(RectTransform), typeof(Image));
      highlightGo.transform.SetParent(slotTransform, false);

      var rect = highlightGo.GetComponent<RectTransform>();
      rect.anchorMin = Vector2.zero;
      rect.anchorMax = Vector2.one;
      rect.offsetMin = Vector2.zero;
      rect.offsetMax = Vector2.zero;

      var image = highlightGo.GetComponent<Image>();
      image.color = new Color(1f, 1f, 1f, 0.35f);
      image.raycastTarget = false;
      image.enabled = false;
      return image;
    }

    static void SetSlotUi(
      InventorySlotUI slotUi,
      Inventory inventory,
      int index,
      Image fillImage,
      Image highlightImage)
    {
      var so = new SerializedObject(slotUi);
      so.FindProperty("_inventory").objectReferenceValue = inventory;
      so.FindProperty("_slotIndex").intValue = index;
      so.FindProperty("_fillImage").objectReferenceValue = fillImage;
      so.FindProperty("_highlightImage").objectReferenceValue = highlightImage;
      so.ApplyModifiedPropertiesWithoutUndo();
    }

    static void SetInventoryUi(InventoryUI inventoryUi, Inventory inventory, InventorySlotUI[] slots)
    {
      var so = new SerializedObject(inventoryUi);
      so.FindProperty("_inventory").objectReferenceValue = inventory;
      var slotsProp = so.FindProperty("_slots");
      slotsProp.arraySize = slots.Length;
      for (int i = 0; i < slots.Length; i++)
        slotsProp.GetArrayElementAtIndex(i).objectReferenceValue = slots[i];
      so.ApplyModifiedPropertiesWithoutUndo();
    }

    static void WireSlotActions(Inventory inventory)
    {
      var asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath);
      if (asset == null)
      {
        Debug.LogWarning("[AfterAll] Could not load input actions — wire Slot1/2/3 on Inventory manually.");
        return;
      }

      var map = asset.FindActionMap("Player");
      var refs = new InputActionReference[Inventory.SlotCount];
      refs[0] = InputActionReference.Create(map.FindAction("Slot1"));
      refs[1] = InputActionReference.Create(map.FindAction("Slot2"));
      refs[2] = InputActionReference.Create(map.FindAction("Slot3"));

      var so = new SerializedObject(inventory);
      var actionsProp = so.FindProperty("_slotSelectActions");
      actionsProp.arraySize = Inventory.SlotCount;
      for (int i = 0; i < Inventory.SlotCount; i++)
        actionsProp.GetArrayElementAtIndex(i).objectReferenceValue = refs[i];
      so.ApplyModifiedPropertiesWithoutUndo();
    }

    static void WirePrompt(PlayerInteractor interactor, Transform hud)
    {
      var promptPanel = hud.Find("PromptPanel");
      if (promptPanel == null)
      {
        Debug.LogError("[AfterAll] HUD/PromptPanel not found.");
        return;
      }

      var promptRect = promptPanel.GetComponent<RectTransform>();
      promptRect.anchorMin = new Vector2(0.5f, 0f);
      promptRect.anchorMax = new Vector2(0.5f, 0f);
      promptRect.pivot = new Vector2(0.5f, 0f);
      promptRect.anchoredPosition = new Vector2(0f, 120f);
      promptRect.sizeDelta = new Vector2(360f, 56f);

      var promptText = promptPanel.GetComponentInChildren<TextMeshProUGUI>(true);
      if (promptText == null)
      {
        var textGo = new GameObject("PromptText", typeof(RectTransform), typeof(TextMeshProUGUI));
        textGo.transform.SetParent(promptPanel, false);
        var textRect = textGo.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        promptText = textGo.GetComponent<TextMeshProUGUI>();
        promptText.fontSize = 28;
        promptText.alignment = TextAlignmentOptions.Center;
        promptText.color = Color.white;
      }

      var group = promptPanel.GetComponent<CanvasGroup>();
      if (group == null)
        group = promptPanel.gameObject.AddComponent<CanvasGroup>();
      group.alpha = 0f;

      var oldOnPanel = promptPanel.GetComponent<InteractPromptUI>();
      if (oldOnPanel != null)
        Object.DestroyImmediate(oldOnPanel);

      var promptUi = hud.GetComponent<InteractPromptUI>();
      if (promptUi == null)
        promptUi = hud.gameObject.AddComponent<InteractPromptUI>();

      var so = new SerializedObject(promptUi);
      so.FindProperty("_interactor").objectReferenceValue = interactor;
      so.FindProperty("_promptGroup").objectReferenceValue = group;
      so.FindProperty("_promptText").objectReferenceValue = promptText;
      so.ApplyModifiedPropertiesWithoutUndo();
    }

    static void WireFeedback(Transform hud)
    {
      var feedbackPanel = hud.Find("FeedbackPanel");
      if (feedbackPanel == null)
      {
        var go = new GameObject("FeedbackPanel", typeof(RectTransform));
        go.transform.SetParent(hud, false);
        feedbackPanel = go.transform;

        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0f);
        rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.anchoredPosition = new Vector2(0f, 72f);
        rect.sizeDelta = new Vector2(420f, 40f);
      }

      var feedbackText = feedbackPanel.GetComponentInChildren<TextMeshProUGUI>(true);
      if (feedbackText == null)
      {
        var textGo = new GameObject("FeedbackText", typeof(RectTransform), typeof(TextMeshProUGUI));
        textGo.transform.SetParent(feedbackPanel, false);
        var textRect = textGo.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        feedbackText = textGo.GetComponent<TextMeshProUGUI>();
        feedbackText.fontSize = 24;
        feedbackText.alignment = TextAlignmentOptions.Center;
        feedbackText.color = new Color(1f, 0.92f, 0.55f);
        feedbackText.enabled = false;
      }

      var feedbackUi = feedbackPanel.GetComponent<GameFeedbackUI>();
      if (feedbackUi == null)
        feedbackUi = feedbackPanel.gameObject.AddComponent<GameFeedbackUI>();

      var so = new SerializedObject(feedbackUi);
      so.FindProperty("_feedbackText").objectReferenceValue = feedbackText;
      so.ApplyModifiedPropertiesWithoutUndo();
    }

    static void WireCrosshair(PlayerInteractor interactor, Transform hud)
    {
      var crosshair = hud.Find("Crosshair");
      if (crosshair == null)
      {
        var go = new GameObject("Crosshair", typeof(RectTransform), typeof(Image));
        go.layer = 5;
        go.transform.SetParent(hud, false);
        crosshair = go.transform;

        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(14f, 14f);
        rect.anchoredPosition = Vector2.zero;

        var image = go.GetComponent<Image>();
        image.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        image.type = Image.Type.Simple;
        image.color = new Color(1f, 1f, 1f, 0.85f);
        image.raycastTarget = false;
      }
      else
      {
        crosshair.gameObject.layer = 5;
        var image = crosshair.GetComponent<Image>();
        if (image != null && image.sprite == null)
        {
          image.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
          image.raycastTarget = false;
        }
      }

      var crosshairUi = crosshair.GetComponent<CrosshairUI>();
      if (crosshairUi == null)
        crosshairUi = crosshair.gameObject.AddComponent<CrosshairUI>();

      var so = new SerializedObject(crosshairUi);
      so.FindProperty("_interactor").objectReferenceValue = interactor;
      so.FindProperty("_crosshair").objectReferenceValue = crosshair.GetComponent<Image>();
      so.ApplyModifiedPropertiesWithoutUndo();
    }

    static void WireHeldItem(Inventory inventory)
    {
      var camera = Camera.main;
      if (camera == null)
      {
        Debug.LogWarning("[AfterAll] Main Camera not found — skipped HeldItemDisplay.");
        return;
      }

      var held = camera.GetComponent<HeldItemDisplay>();
      if (held == null)
        held = camera.gameObject.AddComponent<HeldItemDisplay>();

      var so = new SerializedObject(held);
      so.FindProperty("_inventory").objectReferenceValue = inventory;
      so.FindProperty("_holdAnchor").objectReferenceValue = camera.transform;
      so.ApplyModifiedPropertiesWithoutUndo();
    }

    static void WireAllDoors()
    {
      SetupDoorHierarchy(GameObject.Find("DoorHinge"));
      SetupDoorHierarchy(GameObject.Find("DoorHinge_Open"));

      var lockedHinge = GameObject.Find("DoorHinge");
      if (lockedHinge != null)
      {
        var hingeDoor = lockedHinge.GetComponent<HingeDoor>();
        var locked = lockedHinge.GetComponent<LockedDoor>();
        if (locked == null)
          locked = lockedHinge.AddComponent<LockedDoor>();

        var lockedSo = new SerializedObject(locked);
        lockedSo.FindProperty("_hinge").objectReferenceValue = hingeDoor;
        lockedSo.ApplyModifiedPropertiesWithoutUndo();
      }

      var openHinge = GameObject.Find("DoorHinge_Open");
      if (openHinge != null)
      {
        if (openHinge.GetComponent<LockedDoor>() != null)
          Object.DestroyImmediate(openHinge.GetComponent<LockedDoor>());

        var hingeDoor = openHinge.GetComponent<HingeDoor>();
        var door = openHinge.GetComponent<AfterAll.Door.Door>();
        if (door == null)
          door = openHinge.AddComponent<AfterAll.Door.Door>();

        var doorSo = new SerializedObject(door);
        doorSo.FindProperty("_hinge").objectReferenceValue = hingeDoor;
        doorSo.ApplyModifiedPropertiesWithoutUndo();
      }
    }

    static void SetupDoorHierarchy(GameObject hingeObject)
    {
      if (hingeObject == null)
        return;

      Transform hinge = hingeObject.transform;
      Transform doorChild = hinge.Find("Door");
      if (doorChild == null)
      {
        Debug.LogWarning($"[AfterAll] {hinge.name} is missing a Door child.");
        return;
      }

      var blockCollider = doorChild.GetComponent<BoxCollider>();
      if (blockCollider == null)
      {
        blockCollider = doorChild.gameObject.AddComponent<BoxCollider>();
        blockCollider.size = new Vector3(1f, 1f, 1f);
        blockCollider.center = Vector3.zero;
      }
      blockCollider.isTrigger = false;

      Transform interactZone = hinge.Find("InteractZone");
      if (interactZone != null)
        Object.DestroyImmediate(interactZone.gameObject);

      var hingeDoor = hingeObject.GetComponent<HingeDoor>();
      if (hingeDoor == null)
        hingeDoor = hingeObject.AddComponent<HingeDoor>();

      var hingeSo = new SerializedObject(hingeDoor);
      hingeSo.FindProperty("_doorCollider").objectReferenceValue = blockCollider;
      hingeSo.ApplyModifiedPropertiesWithoutUndo();
    }

    static void FixCanvas(Canvas canvas)
    {
      var additionalChannels = AdditionalCanvasShaderChannels.TexCoord1
        | AdditionalCanvasShaderChannels.Normal
        | AdditionalCanvasShaderChannels.Tangent;
      canvas.additionalShaderChannels |= additionalChannels;

      var scaler = canvas.GetComponent<CanvasScaler>();
      if (scaler != null)
      {
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
      }
    }
  }
}
#endif
