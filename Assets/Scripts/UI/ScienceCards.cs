using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Short science cards that explain what just happened and the real science / NASA data behind it
// (first solar storm, first power shortage, first harvest...). Each card shows once per game, bottom left,
// without pausing; they queue up if several come at once.
public class ScienceCards : UIWindow
{
    const float Width = 460f, Seconds = 14f;

    struct Card { public string title, body, source; }

    private static ScienceCards instance;
    private static readonly HashSet<string> shown = new HashSet<string>();
    private readonly Queue<Card> queue = new Queue<Card>();

    private RectTransform panel;
    private TMP_Text title, body, source;
    private float hideAt;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetShown() => shown.Clear();

    public static void ResetForNewGame() => shown.Clear();

    // Shows a card from the catalog, once per game
    public static void Show(string id)
    {
        if (!Application.isPlaying || !shown.Add(id)) return;
        Enqueue(Get(id));
    }

    // F1 in the game (presentation): the next card of the catalog, even if it was seen already
    public static void ShowNextDemo()
    {
        demoIndex = (demoIndex + 1) % Ids.Length;
        if (instance != null) { instance.queue.Clear(); instance.hideAt = 0f; }
        Enqueue(Get(Ids[demoIndex]));
    }

    private static int demoIndex = -1;

    static void Enqueue(Card c)
    {
        if (instance == null) instance = new GameObject("Science Cards").AddComponent<ScienceCards>();
        instance.queue.Enqueue(c);
    }

    // --------------------------------------------------------------- The cards (text and source in one place)

    static readonly string[] Ids = { "outside", "storm", "dose", "power", "harvest", "crew", "shield" };

    static Card Get(string id)
    {
        switch (id)
        {
            case "outside":
                return new Card { title = "Leaving life support",
                    body = "Outside the base your suit carries all your oxygen. Every metre out is a metre you must walk back - plan the round trip before you go.",
                    source = "NASA spacesuit (xEMU) life support" };
            case "storm":
            {
                SolarStorms.Flare f = SolarStorms.Current;
                if (f == null && SolarStorms.Flares.Count == 0) SolarStorms.LoadFlares();
                if (f == null && SolarStorms.Flares.Count > 0) f = SolarStorms.Flares[0];
                string cls = f != null ? f.classType : "X";
                string when = f != null ? f.Date : "";
                string where = f != null && !string.IsNullOrEmpty(f.sourceLocation) ? $" (source region {f.sourceLocation})" : "";
                return new Card { title = $"Solar flare {cls}",
                    body = $"This storm is a real flare from {when}{where}. Flares hurl energetic particles that reach the Moon in minutes - there is no atmosphere or magnetic field to stop them. Get under shielding.",
                    source = "NASA DONKI space weather database" };
            }
            case "dose":
                return new Card { title = "Radiation adds up",
                    body = $"On the lunar surface LRO measured about {(NasaData.HasRadiation ? NasaData.Info.moonDoseMedian : 13.2f):0.0} µGy every hour - about 2.5x the ISS. Dose builds up over a mission; shelter and shielding keep it low.",
                    source = "NASA OSDR RadLab, LRO/CRaTER" };
            case "power":
                return new Card { title = "Power shortage",
                    body = "Life support, the greenhouse and the garage all need power. On the Moon a 14-day night makes solar alone risky - missions mix solar with stored or nuclear power.",
                    source = "NASA Fission Surface Power / Artemis base planning" };
            case "harvest":
                return new Card { title = "Space-grown food",
                    body = "In 2015 astronauts on the ISS ate the first lettuce grown in space, in NASA's Veggie chamber. Fresh crops add vitamins and cut how much food must be launched.",
                    source = "NASA OSDR · Veggie (VEG-01)" };
            case "crew":
                return new Card { title = "A hungry crew",
                    body = "Astronauts need about 2,500-3,000 kcal a day. Underfed crews get tired and make mistakes - here they build much slower. Walk up and feed them.",
                    source = "NASA Human Research Program" };
            default: // "shield"
                return new Card { title = "Regolith shielding",
                    body = "Packed lunar soil is one of the best radiation shields we have - and it's already there. A couple of metres of regolith cut storm doses dramatically.",
                    source = "NASA radiation protection studies" };
        }
    }

    void Start()
    {
        RectTransform root = CreateCanvas("Science Cards", 6).GetComponent<RectTransform>();
        panel = Rect("Card", root, Vector2.zero, new Vector2(Width, 170f));
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0f, 0f);
        panel.anchoredPosition = new Vector2(24f, 150f);
        Rounded(panel, new Color(UITheme.Window.r, UITheme.Window.g, UITheme.Window.b, 0.92f), 14);
        Image(Rect("Accent", panel, new Vector2(0f, 14f), new Vector2(4f, 142f)), UITheme.Accent);

        TMP_Text kicker = Text(Rect("Kicker", panel, new Vector2(20f, 14f), new Vector2(Width - 90f, 18f)), "Science note", 13f, true, TextAlignmentOptions.MidlineLeft);
        kicker.color = UITheme.Accent;
        title = Text(Rect("Title", panel, new Vector2(20f, 34f), new Vector2(Width - 40f, 26f)), "", 20f, true, TextAlignmentOptions.MidlineLeft);
        body = Text(Rect("Body", panel, new Vector2(20f, 64f), new Vector2(Width - 40f, 70f)), "", 15f, false, TextAlignmentOptions.TopLeft);
        body.textWrappingMode = TextWrappingModes.Normal;
        body.overflowMode = TextOverflowModes.Overflow;
        body.color = UITheme.Text;
        source = Text(Rect("Source", panel, new Vector2(20f, 140f), new Vector2(Width - 120f, 20f)), "", 12f, false, TextAlignmentOptions.MidlineLeft);
        source.color = UITheme.TextDim;
        LabelButton(panel, new Vector2(Width - 92f, 128f), new Vector2(76f, 30f), UITheme.Card, UITheme.Text, "Got it", 13f, () => hideAt = 0f);
        panel.gameObject.SetActive(false);
    }

    void Update()
    {
        if (panel == null) return;
        bool blocked = InteractionSystem.MenuOpen;
        if (panel.gameObject.activeSelf && (Time.unscaledTime >= hideAt)) panel.gameObject.SetActive(false);
        if (!panel.gameObject.activeSelf && queue.Count > 0 && !blocked)
        {
            Card c = queue.Dequeue();
            title.text = c.title;
            body.text = c.body;
            source.text = "Source: " + c.source;
            // Grow the card to fit the text
            body.ForceMeshUpdate();
            float h = Mathf.Max(60f, body.preferredHeight);
            body.rectTransform.sizeDelta = new Vector2(Width - 40f, h);
            source.rectTransform.anchoredPosition = new Vector2(20f, -(64f + h + 10f));
            ((RectTransform)panel.Find("Button")).anchoredPosition = new Vector2(Width - 92f, -(64f + h + 4f));
            panel.sizeDelta = new Vector2(Width, 64f + h + 46f);
            ((RectTransform)panel.Find("Accent")).sizeDelta = new Vector2(4f, panel.sizeDelta.y - 28f);
            panel.gameObject.SetActive(true);
            hideAt = Time.unscaledTime + Seconds;
        }
        else if (panel.gameObject.activeSelf && blocked) hideAt += Time.unscaledDeltaTime; // Wait while a menu covers it
    }
}
