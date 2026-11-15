using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The short story before the game starts: a small card with a picture and one line per page, Next to go on.
// Shown once per game start; the game is paused meanwhile.
public class IntroUI : UIWindow
{
    const float W = 760f, H = 600f;

    private static bool shown;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetShown() => shown = false;
    private static IntroUI instance;
    public static bool IsOpen => instance != null && instance.gameObject.activeSelf;

    struct Page
    {
        public Texture picture;
        public Color tint;
        public string title;
        public string text;
    }

    private Page[] pages;
    private int page;
    private RawImage picture;
    private Image warning;
    private TMP_Text title, line, nextLabel;
    private Image[] dots;

    public static void ShowOnce()
    {
        if (shown) return;
        shown = true;
        GameObject root = CreateCanvas("Intro UI", 95);
        instance = root.AddComponent<IntroUI>();
        instance.Build(root.GetComponent<RectTransform>());
        Time.timeScale = 0f;
    }

    void Build(RectTransform root)
    {
        Texture earth = NasaData.EarthPhoto;
        Texture rocket = Resources.Load<Texture2D>("Intro/ship");
        Texture hub = Resources.Load<Texture2D>("Intro/base");
        Texture rover = Resources.Load<Texture2D>("Intro/rover");
        Texture moon = Resources.Load<Texture2D>("Intro/moon");
        Color red = new Color(1f, 0.45f, 0.4f);
        pages = new[]
        {
            new Page { picture = earth, tint = Color.white, title = "Day 1 · Launch",
                       text = "Our crew of three leaves Earth for a lunar survey. The rocket carries a base module, a rover kit and six months of supplies." },
            new Page { picture = rocket, tint = Color.white, title = "Day 3 · Lunar orbit",
                       text = "Three quiet days later the Moon fills the windows. We aim for the south pole, where there is ice in the dark craters." },
            new Page { picture = moon, tint = Color.white, title = "Day 4 · Descent",
                       text = "Landing burn. Fifteen kilometres up, the main engine starts to shake. Then the alarms." },
            new Page { picture = rocket, tint = red, title = "Engine failure",
                       text = "The engine dies. We come down hard, far from the landing site. Cargo is scattered across the craters." },
            new Page { picture = hub, tint = Color.white, title = "The crash site",
                       text = "The base module survived. It has air and power - for now. Food, oxygen and parts have to be found." },
            new Page { picture = rover, tint = Color.white, title = "The way home",
                       text = "Salvage the wrecks, grow food, build a rover and repair the ship. Then we fly home." },
        };

        RectTransform veil = Rect("Veil", root, Vector2.zero, Vector2.zero);
        veil.anchorMin = Vector2.zero;
        veil.anchorMax = Vector2.one;
        veil.offsetMin = veil.offsetMax = Vector2.zero;
        Image(veil, new Color(0f, 0f, 0f, 0.9f)).raycastTarget = true;

        RectTransform box = Centered("Card", veil, new Vector2(W, H));
        Rounded(box, UITheme.Window, 16);
        RectTransform stage = Rect("Stage", box, new Vector2(24f, 24f), new Vector2(W - 48f, 380f));
        Rounded(stage, new Color(0.02f, 0.02f, 0.03f, 1f), 12);
        picture = Rect("Picture", stage, new Vector2(20f, 10f), new Vector2(W - 88f, 360f)).gameObject.AddComponent<RawImage>();
        picture.raycastTarget = false;
        warning = Picture(Rect("Warning", stage, new Vector2(W - 48f - 76f, 16f), new Vector2(56f, 56f)), UITheme.Icon("wreck"));
        warning.color = UITheme.Bad;

        title = Text(Rect("Title", box, new Vector2(40f, 414f), new Vector2(W - 80f, 34f)), "", 26f, true, TextAlignmentOptions.Center);
        title.color = UITheme.Accent;
        line = Text(Rect("Line", box, new Vector2(50f, 452f), new Vector2(W - 100f, 60f)), "", 19f, false, TextAlignmentOptions.Top);
        line.textWrappingMode = TextWrappingModes.Normal;

        dots = new Image[pages.Length];
        float dotsX = W / 2f - pages.Length * 9f;
        for (int i = 0; i < pages.Length; i++)
            dots[i] = Rounded(Rect("Dot", box, new Vector2(dotsX + i * 18f, H - 64f), new Vector2(10f, 10f)), UITheme.TextDim, 5);

        LabelButton(box, new Vector2(24f, H - 82f), new Vector2(120f, 48f), new Color(1f, 1f, 1f, 0.06f), UITheme.TextDim, "Skip", 17f, Finish);
        Button next = LabelButton(box, new Vector2(W - 24f - 180f, H - 82f), new Vector2(180f, 48f), UITheme.Accent, UITheme.OnAccent, "Next", 20f, Next);
        nextLabel = next.GetComponentInChildren<TMP_Text>();
        ShowPage();
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space)) Next();
    }

    void ShowPage()
    {
        Page p = pages[page];
        picture.texture = p.picture;
        picture.color = p.tint;
        picture.enabled = p.picture != null;
        // Keep the picture's shape inside the stage
        if (p.picture != null)
        {
            float maxW = W - 88f, maxH = 360f, aspect = p.picture.width / (float)p.picture.height;
            float w = Mathf.Min(maxW, maxH * aspect), h = w / aspect;
            picture.rectTransform.sizeDelta = new Vector2(w, h);
            picture.rectTransform.anchoredPosition = new Vector2(20f + (maxW - w) / 2f, -10f - (maxH - h) / 2f);
        }
        warning.enabled = p.tint != Color.white;
        title.text = p.title;
        line.text = p.text;
        for (int i = 0; i < dots.Length; i++) dots[i].color = i == page ? UITheme.Accent : new Color(1f, 1f, 1f, 0.2f);
        nextLabel.text = page == pages.Length - 1 ? "Start" : "Next";
    }

    void Next()
    {
        if (page >= pages.Length - 1) { Finish(); return; }
        page++;
        ShowPage();
    }

    void Finish()
    {
        Time.timeScale = 1f;
        Destroy(gameObject);
        instance = null;
    }
}
