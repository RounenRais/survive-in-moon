using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Base class for the windows that are built from code (build menu, crew): the canvas, the window frame and the
// small pieces (rectangles, rounded panels, pictures, texts, buttons) in the shared look of UITheme.
// Positions are given from the parent's top-left corner, with y going down, like on a drawing.
public abstract class UIWindow : MonoBehaviour
{
    // A screen canvas that scales with the screen (designed for 1920 x 1080), plus an EventSystem if missing
    protected static GameObject CreateCanvas(string name, int sortingOrder)
    {
        var root = new GameObject(name);
        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;
        var scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        root.AddComponent<GraphicRaycaster>();
        if (FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
        {
            var events = new GameObject("EventSystem");
            events.AddComponent<UnityEngine.EventSystems.EventSystem>();
            events.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
        }
        return root;
    }

    // Dark veil over the whole screen (catches the clicks) with the window box in the middle.
    // Returns the veil (show/hide it to open/close the window) and the box to put things in.
    protected static RectTransform WindowFrame(Transform root, Vector2 size, out RectTransform box)
    {
        var veil = new GameObject("Veil", typeof(RectTransform)).GetComponent<RectTransform>();
        veil.SetParent(root, false);
        veil.anchorMin = Vector2.zero;
        veil.anchorMax = Vector2.one;
        veil.offsetMin = veil.offsetMax = Vector2.zero;
        Image(veil, new Color(0f, 0f, 0f, 0.5f)).raycastTarget = true;

        Rounded(Centered("Edge", veil, size + new Vector2(2f, 2f)), new Color(1f, 1f, 1f, 0.08f), 22); // 1-pixel light edge
        box = Centered("Window", veil, size);
        Rounded(box, UITheme.Window, 22);
        return veil;
    }

    // Small colored word over a big title, and the round x button at the top right
    protected static void Header(RectTransform box, float width, string kicker, string title, UnityEngine.Events.UnityAction onClose)
    {
        TMP_Text small = Text(Rect("Kicker", box, new Vector2(32f, 26f), new Vector2(400f, 20f)), kicker, 13f, true, TextAlignmentOptions.MidlineLeft);
        small.color = UITheme.Accent;
        small.characterSpacing = 8f;
        Text(Rect("Title", box, new Vector2(32f, 48f), new Vector2(600f, 44f)), title, 34f, true, TextAlignmentOptions.MidlineLeft);
        Button close = Button(box, new Vector2(width - 32f - 44f, 30f), new Vector2(44f, 44f), UITheme.Card, onClose);
        Text(Rect("X", close.transform, Vector2.zero, new Vector2(44f, 44f)), "×", 28f, false, TextAlignmentOptions.Center);
    }

    // Small spaced-out capital letters that name a part of the window
    protected static void Section(RectTransform parent, string text, float x, float y)
    {
        TMP_Text header = Text(Rect(text, parent, new Vector2(x, y), new Vector2(300f, 22f)), text, 12f, true, TextAlignmentOptions.MidlineLeft);
        header.color = UITheme.TextDim;
        header.characterSpacing = 8f;
    }

    protected static RectTransform Rect(string name, Transform parent, Vector2 position, Vector2 size)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(position.x, -position.y);
        rect.sizeDelta = size;
        return rect;
    }

    protected static RectTransform Centered(string name, Transform parent, Vector2 size)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        return rect;
    }

    protected static Image Image(RectTransform rect, Color color)
    {
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    protected static Image Rounded(RectTransform rect, Color color, int radius)
    {
        Image image = Image(rect, color);
        image.sprite = UITheme.Rounded(radius);
        image.type = UnityEngine.UI.Image.Type.Sliced;
        return image;
    }

    // A picture that keeps its shape inside the box
    protected static Image Picture(RectTransform rect, Sprite sprite)
    {
        Image image = Image(rect, Color.white);
        image.sprite = sprite;
        image.preserveAspect = true;
        image.enabled = sprite != null;
        return image;
    }

    // An accent outline around a selected card: a slightly bigger rounded shape just behind it
    protected static Image Frame(RectTransform card, float width, float height)
    {
        RectTransform frame = Rect("Frame", card, new Vector2(-2f, -2f), new Vector2(width + 4f, height + 4f));
        Image image = Rounded(frame, UITheme.Accent, 14);
        frame.SetParent(card.parent, true);
        frame.SetSiblingIndex(card.GetSiblingIndex());
        return image;
    }

    // One line (or a fixed box): anything longer ends with "..."
    protected static TMP_Text Text(RectTransform rect, string text, float size, bool bold, TextAlignmentOptions alignment)
    {
        // A line of Titillium Web needs ~1.55 x the font size in height. In a lower box TextMeshPro hides the
        // whole line (it doesn't "fit"), so the box grows around its middle and the text stays where it was.
        float needed = Mathf.Ceil(size * 1.6f);
        if (rect.sizeDelta.y < needed)
        {
            float extra = needed - rect.sizeDelta.y;
            rect.sizeDelta = new Vector2(rect.sizeDelta.x, needed);
            if (alignment != TextAlignmentOptions.TopLeft) rect.anchoredPosition += new Vector2(0f, extra * 0.5f);
        }

        var tmp = rect.gameObject.AddComponent<TextMeshProUGUI>();
        if (TMP_Settings.defaultFontAsset != null) tmp.font = TMP_Settings.defaultFontAsset;
        tmp.text = text;
        tmp.fontSize = size;
        tmp.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
        tmp.alignment = alignment;
        tmp.color = UITheme.Text;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.overflowMode = TextOverflowModes.Ellipsis;
        tmp.raycastTarget = false;
        return tmp;
    }

    protected static Button Button(Transform parent, Vector2 position, Vector2 size, Color color, UnityEngine.Events.UnityAction onClick)
    {
        RectTransform rect = Rect("Button", parent, position, size);
        Image image = Rounded(rect, color, 14);
        image.raycastTarget = true;
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        ColorBlock colors = button.colors;
        colors.highlightedColor = new Color(1.18f, 1.18f, 1.18f, 1f);
        colors.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
        colors.selectedColor = Color.white;
        colors.disabledColor = new Color(0.6f, 0.6f, 0.6f, 0.55f);
        button.colors = colors;
        button.onClick.AddListener(onClick);
        return button;
    }

    // A button with a centered word on it
    protected static Button LabelButton(Transform parent, Vector2 position, Vector2 size, Color color, Color textColor,
                                        string label, float fontSize, UnityEngine.Events.UnityAction onClick)
    {
        Button button = Button(parent, position, size, color, onClick);
        Text(Rect("Label", button.transform, Vector2.zero, size), label, fontSize, true, TextAlignmentOptions.Center).color = textColor;
        return button;
    }

    protected static void Clear(Transform parent)
    {
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            GameObject child = parent.GetChild(i).gameObject;
            child.SetActive(false); // Gone from the layout right away; Destroy happens at the end of the frame
            Destroy(child);
        }
    }
}
