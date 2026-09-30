using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Builds the inventory window (canvas, astronaut preview on the left with room for status bars later, 6x4 grid,
// slot prefab, DragLayer canvas with the drag copy and tooltip),
// saves it as prefabs and puts it in the open scene, with an EventSystem if there is none.
// Runs once by itself when the game scene is open without one; running the menu again rebuilds everything.
// Menu: Survive In Moon > Create Inventory UI
[InitializeOnLoad]
public static class InventoryUIBuilder
{
    const string ScenePath = "Assets/Scenes/SampleScene.unity";
    static string DoneKey => "SurviveInMoon.InventoryUIPlaced." + Application.dataPath;

    static InventoryUIBuilder()
    {
        EditorApplication.delayCall += () =>
        {
            if (EditorPrefs.GetBool(DoneKey, false) || EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (EditorSceneManager.GetActiveScene().path != ScenePath) return;
            if (Object.FindFirstObjectByType<InventoryUI>(FindObjectsInactive.Include) == null)
            {
                Create();
                EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            }
            EditorPrefs.SetBool(DoneKey, true);
        };
    }

    const string Folder = "Assets/Prefabs/UI";
    const string SlotPath = Folder + "/InventorySlot.prefab";
    const string WindowPath = Folder + "/InventoryUI.prefab";

    const int Columns = 6, Rows = 4;
    const float Cell = 88f, Spacing = 8f, Padding = 22f, Header = 58f, CharacterWidth = 300f, Gap = 22f;

    // The shared UI colors (UITheme), so the inventory looks like the other windows
    static readonly Color WindowColor = UITheme.Window;
    static readonly Color PanelColor = UITheme.Panel;
    static readonly Color LineColor = UITheme.Line;
    static readonly Color AccentColor = UITheme.Accent;
    static readonly Color TooltipColor = new Color(UITheme.Well.r, UITheme.Well.g, UITheme.Well.b, 0.97f);

    [MenuItem("Survive In Moon/Create Inventory UI")]
    public static void Create()
    {
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Prefabs", "UI");

        SlotUI slotPrefab = BuildSlotPrefab();

        foreach (InventoryUI old in Object.FindObjectsByType<InventoryUI>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            Undo.DestroyObjectImmediate(old.gameObject);
        GameObject root = BuildWindow(slotPrefab);
        PrefabUtility.SaveAsPrefabAssetAndConnect(root, WindowPath, InteractionMode.AutomatedAction);
        Undo.RegisterCreatedObjectUndo(root, "Create Inventory UI");

        if (Object.FindFirstObjectByType<EventSystem>() == null)
        {
            var eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            Undo.RegisterCreatedObjectUndo(eventSystem, "Create EventSystem");
        }

        EditorSceneManager.MarkSceneDirty(root.scene);
        Selection.activeGameObject = root;
        Debug.Log("Inventory UI created (Tab opens it in play mode).");
    }

    // --------------------------------------------------------------- Slot

    static SlotUI BuildSlotPrefab()
    {
        RectTransform slot = NewUI("InventorySlot", null);
        slot.sizeDelta = new Vector2(Cell, Cell);
        Image background = AddImage(slot, Color.white);

        RectTransform iconRect = NewUI("Icon", slot);
        Stretch(iconRect, 8f);
        Image icon = AddImage(iconRect, Color.white, sliced: false);
        icon.sprite = null;
        icon.preserveAspect = true;
        icon.raycastTarget = false;

        RectTransform quantityRect = NewUI("Quantity", slot);
        quantityRect.anchorMin = quantityRect.anchorMax = quantityRect.pivot = new Vector2(1f, 0f);
        quantityRect.anchoredPosition = new Vector2(-6f, 3f);
        quantityRect.sizeDelta = new Vector2(Cell - 12f, 26f);
        TMP_Text quantity = AddText(quantityRect, "", 20f, FontStyles.Bold, TextAlignmentOptions.BottomRight);

        RectTransform barRect = NewUI("CategoryBar", slot);
        barRect.anchorMin = new Vector2(0f, 0f);
        barRect.anchorMax = new Vector2(1f, 0f);
        barRect.pivot = new Vector2(0.5f, 0f);
        barRect.offsetMin = new Vector2(12f, 5f);
        barRect.offsetMax = new Vector2(-12f, 8f);
        Image bar = AddImage(barRect, Color.white, sliced: false);
        bar.raycastTarget = false;

        SlotUI slotUI = slot.gameObject.AddComponent<SlotUI>();
        slotUI.background = background;
        slotUI.icon = icon;
        slotUI.quantityText = quantity;
        slotUI.categoryBar = bar;

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(slot.gameObject, SlotPath);
        Object.DestroyImmediate(slot.gameObject);
        return prefab.GetComponent<SlotUI>();
    }

    // --------------------------------------------------------------- Window

    static GameObject BuildWindow(SlotUI slotPrefab)
    {
        // Root canvas (always active; the window inside is what opens and closes)
        RectTransform root = NewUI("Inventory UI", null);
        Canvas canvas = root.gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;
        CanvasScaler scaler = root.gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        root.gameObject.AddComponent<GraphicRaycaster>();

        float gridWidth = Columns * Cell + (Columns - 1) * Spacing;
        float gridHeight = Rows * Cell + (Rows - 1) * Spacing;

        RectTransform window = NewUI("Window", root);
        window.sizeDelta = new Vector2(Padding * 2f + CharacterWidth + Gap + gridWidth, Header + gridHeight + Padding);
        AddImage(window, WindowColor);

        RectTransform accent = NewUI("Accent", window); // Thin colored line on top of the window
        accent.anchorMin = new Vector2(0f, 1f);
        accent.anchorMax = Vector2.one;
        accent.pivot = new Vector2(0.5f, 1f);
        accent.offsetMin = new Vector2(Padding, -3f);
        accent.offsetMax = new Vector2(-Padding, 0f);
        AddImage(accent, AccentColor, sliced: false).raycastTarget = false;

        RectTransform title = NewUI("Title", window);
        TopBar(title);
        TMP_Text titleText = AddText(title, "INVENTORY", 24f, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
        titleText.characterSpacing = 8f;

        RectTransform divider = NewUI("Divider", window);
        divider.anchorMin = new Vector2(0f, 1f);
        divider.anchorMax = Vector2.one;
        divider.pivot = new Vector2(0.5f, 1f);
        divider.offsetMin = new Vector2(Padding, -Header + 11f);
        divider.offsetMax = new Vector2(-Padding, -Header + 12f);
        AddImage(divider, LineColor, sliced: false).raycastTarget = false;

        // Left: the astronaut, with a (still empty) column at the bottom for status bars later (oxygen, hunger, ...)
        RectTransform character = NewUI("Character", window);
        character.anchorMin = character.anchorMax = character.pivot = new Vector2(0f, 1f);
        character.anchoredPosition = new Vector2(Padding, -Header);
        character.sizeDelta = new Vector2(CharacterWidth, gridHeight);
        AddImage(character, PanelColor).raycastTarget = false;

        RectTransform view = NewUI("CharacterView", character);
        Stretch(view, 4f);
        view.gameObject.AddComponent<RawImage>();
        view.gameObject.AddComponent<CharacterPreview>().background = PanelColor;

        RectTransform stats = NewUI("Stats", character);
        stats.anchorMin = new Vector2(0f, 0f);
        stats.anchorMax = new Vector2(1f, 0f);
        stats.pivot = new Vector2(0.5f, 0f);
        stats.offsetMin = new Vector2(14f, 14f);
        stats.offsetMax = new Vector2(-14f, 14f);
        VerticalLayoutGroup statsLayout = stats.gameObject.AddComponent<VerticalLayoutGroup>();
        statsLayout.spacing = 6f;
        statsLayout.childControlWidth = statsLayout.childControlHeight = true;
        statsLayout.childForceExpandHeight = false;
        stats.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        RectTransform grid = NewUI("Grid", window);
        grid.anchorMin = grid.anchorMax = grid.pivot = new Vector2(0f, 1f);
        grid.anchoredPosition = new Vector2(Padding + CharacterWidth + Gap, -Header);
        grid.sizeDelta = new Vector2(gridWidth, gridHeight);
        GridLayoutGroup layout = grid.gameObject.AddComponent<GridLayoutGroup>();
        layout.cellSize = new Vector2(Cell, Cell);
        layout.spacing = new Vector2(Spacing, Spacing);
        layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        layout.constraintCount = Columns;

        // Preview slots so the layout can be seen in the editor; replaced by the real ones at start
        for (int i = 0; i < Columns * Rows; i++) PrefabUtility.InstantiatePrefab(slotPrefab.gameObject, grid);

        // DragLayer: its own canvas drawn above everything; no raycaster, so it never catches the pointer
        RectTransform dragLayer = NewUI("DragLayer", root);
        Stretch(dragLayer, 0f);
        Canvas dragCanvas = dragLayer.gameObject.AddComponent<Canvas>();
        dragCanvas.overrideSorting = true;
        dragCanvas.sortingOrder = 100;
        CanvasGroup group = dragLayer.gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;

        RectTransform dragIconRect = NewUI("DragIcon", dragLayer);
        dragIconRect.sizeDelta = new Vector2(Cell - 16f, Cell - 16f);
        Image dragIcon = AddImage(dragIconRect, new Color(1f, 1f, 1f, 0.6f), sliced: false);
        dragIcon.sprite = null;
        dragIcon.preserveAspect = true;
        dragIcon.raycastTarget = false;
        dragIconRect.gameObject.SetActive(false);

        ItemTooltip tooltip = BuildTooltip(dragLayer);

        InventoryUI ui = root.gameObject.AddComponent<InventoryUI>();
        ui.slotPrefab = slotPrefab;
        ui.grid = grid;
        ui.window = window.gameObject;
        ui.tooltip = tooltip;
        ui.dragIcon = dragIcon;
        return root.gameObject;
    }

    static ItemTooltip BuildTooltip(Transform parent)
    {
        RectTransform tooltip = NewUI("Tooltip", parent);
        tooltip.anchorMin = tooltip.anchorMax = Vector2.zero;
        tooltip.pivot = new Vector2(0f, 1f); // Top-left corner sits at the pointer
        tooltip.sizeDelta = new Vector2(320f, 0f);
        AddImage(tooltip, TooltipColor).raycastTarget = false;

        VerticalLayoutGroup layout = tooltip.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(14, 14, 10, 12);
        layout.spacing = 4f;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        ContentSizeFitter fitter = tooltip.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        TMP_Text nameText = AddText(NewUI("Name", tooltip), "", 22f, FontStyles.Bold, TextAlignmentOptions.TopLeft);
        TMP_Text category = AddText(NewUI("Category", tooltip), "", 14f, FontStyles.Bold, TextAlignmentOptions.TopLeft);
        category.characterSpacing = 4f;
        TMP_Text description = AddText(NewUI("Description", tooltip), "", 17f, FontStyles.Normal, TextAlignmentOptions.TopLeft);
        description.color = UITheme.TextDim;

        ItemTooltip itemTooltip = tooltip.gameObject.AddComponent<ItemTooltip>();
        itemTooltip.nameText = nameText;
        itemTooltip.categoryText = category;
        itemTooltip.descriptionText = description;
        tooltip.gameObject.SetActive(false);
        return itemTooltip;
    }

    // --------------------------------------------------------------- Helpers

    static RectTransform NewUI(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = LayerMask.NameToLayer("UI");
        var rect = (RectTransform)go.transform;
        if (parent != null) rect.SetParent(parent, false);
        return rect;
    }

    // Full width strip at the top of the window, as tall as the header
    static void TopBar(RectTransform rect)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 1f);
        rect.offsetMin = new Vector2(Padding, -Header + 12f);
        rect.offsetMax = new Vector2(-Padding, 0f);
    }

    static void Stretch(RectTransform rect, float inset)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(inset, inset);
        rect.offsetMax = new Vector2(-inset, -inset);
    }

    static Image AddImage(RectTransform rect, Color color, bool sliced = true)
    {
        Image image = rect.gameObject.AddComponent<Image>();
        if (sliced)
        {
            image.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            image.type = Image.Type.Sliced;
        }
        image.color = color;
        return image;
    }

    static TMP_Text AddText(RectTransform rect, string text, float size, FontStyles style, TextAlignmentOptions alignment)
    {
        TextMeshProUGUI tmp = rect.gameObject.AddComponent<TextMeshProUGUI>();
        if (TMP_Settings.defaultFontAsset != null) tmp.font = TMP_Settings.defaultFontAsset;
        tmp.text = text;
        tmp.fontSize = size;
        tmp.fontStyle = style;
        tmp.alignment = alignment;
        tmp.textWrappingMode = TextWrappingModes.Normal;
        tmp.raycastTarget = false;
        return tmp;
    }
}
