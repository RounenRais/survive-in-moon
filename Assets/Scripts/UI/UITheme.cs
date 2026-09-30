using System.Collections.Generic;
using UnityEngine;

// The look shared by all the game's UI: colors, the font (Titillium Web, in Assets/Resources/Fonts)
// and rounded panel sprites. TextMeshPro texts get the font through the TMP default font asset
// (set by: Survive In Moon > Apply Game Font); the simple on-screen texts (OnGUI) use GuiFont.
public static class UITheme
{
    // Neutral graphite surfaces with a warm amber accent (matches the orange markings on the buildings)
    public static readonly Color Window = new Color(0.085f, 0.087f, 0.094f, 0.98f);
    public static readonly Color Panel = new Color(0.118f, 0.12f, 0.13f, 1f);
    public static readonly Color Card = new Color(0.16f, 0.163f, 0.175f, 1f);
    public static readonly Color CardSelected = new Color(0.24f, 0.2f, 0.15f, 1f);
    public static readonly Color Well = new Color(0.07f, 0.072f, 0.08f, 1f);
    public static readonly Color Line = new Color(1f, 1f, 1f, 0.07f);
    public static readonly Color Accent = new Color(1f, 0.68f, 0.32f);
    public static readonly Color AccentSoft = new Color(0.33f, 0.25f, 0.16f, 1f);
    public static readonly Color Text = new Color(0.95f, 0.95f, 0.94f);
    public static readonly Color TextDim = new Color(0.66f, 0.66f, 0.68f);
    public static readonly Color Good = new Color(0.56f, 0.88f, 0.6f);
    public static readonly Color Bad = new Color(1f, 0.5f, 0.45f);
    public static readonly Color OnAccent = new Color(0.12f, 0.08f, 0.03f);
    public static readonly Color Strip = new Color(0.085f, 0.087f, 0.094f, 0.82f); // Behind on-screen texts in the world

    private static Font guiFont;
    public static Font GuiFont
    {
        get
        {
            if (guiFont == null) guiFont = Resources.Load<Font>("Fonts/TitilliumWeb-SemiBold");
            return guiFont;
        }
    }

    // --------------------------------------------------------------- On-screen hint (call from OnGUI)

    private static GUIStyle hintTitle, hintDetail;

    // A panel at the bottom middle of the screen: a title line and a smaller line under it
    public static void DrawHint(string title, string detail, Color detailColor)
    {
        if (hintTitle == null)
        {
            hintTitle = new GUIStyle(GUI.skin.label)
            {
                font = GuiFont, fontSize = 18, alignment = TextAnchor.MiddleCenter, normal = { textColor = Text }
            };
            hintDetail = new GUIStyle(hintTitle) { fontSize = 14 };
        }
        const float width = 560f, height = 66f;
        var rect = new Rect((Screen.width - width) / 2f, Screen.height - height - 24f, width, height);
        GUI.DrawTexture(rect, Pixel(Strip));
        GUI.DrawTexture(new Rect(rect.x, rect.y, width, 2f), Pixel(Accent));
        GUI.Label(new Rect(rect.x, rect.y + 8f, width, 26f), title, hintTitle);
        hintDetail.normal.textColor = detailColor;
        GUI.Label(new Rect(rect.x, rect.y + 34f, width, 22f), detail, hintDetail);
    }

    // --------------------------------------------------------------- Textures

    private static readonly Dictionary<Color, Texture2D> pixels = new Dictionary<Color, Texture2D>();

    // A 1x1 texture of one color (backgrounds of the OnGUI texts)
    public static Texture2D Pixel(Color color)
    {
        if (pixels.TryGetValue(color, out Texture2D t) && t != null) return t;
        t = new Texture2D(1, 1) { hideFlags = HideFlags.DontSave };
        t.SetPixel(0, 0, color);
        t.Apply();
        pixels[color] = t;
        return t;
    }

    private static readonly Dictionary<int, Sprite> rounded = new Dictionary<int, Sprite>();

    // A white rectangle with round corners (radius in pixels), made for Image.type = Sliced:
    // the corners keep their shape at any size. Tint it with Image.color.
    public static Sprite Rounded(int radius)
    {
        if (rounded.TryGetValue(radius, out Sprite s) && s != null) return s;
        int size = radius * 2 + 4;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave
        };
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            // Distance outside the inner rectangle, minus the radius = distance to the rounded edge
            float dx = Mathf.Max(0f, Mathf.Abs(x + 0.5f - size * 0.5f) - (size * 0.5f - radius));
            float dy = Mathf.Max(0f, Mathf.Abs(y + 0.5f - size * 0.5f) - (size * 0.5f - radius));
            float alpha = Mathf.Clamp01(radius - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f); // Soft 1-pixel edge
            tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
        }
        tex.Apply();
        float border = radius + 1;
        s = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                          SpriteMeshType.FullRect, new Vector4(border, border, border, border));
        s.hideFlags = HideFlags.DontSave;
        rounded[radius] = s;
        return s;
    }
}
