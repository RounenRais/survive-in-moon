using TMPro;
using UnityEngine;
using UnityEngine.UI;

// A compass strip at the top of the screen: N / E / S / W, the current objective (amber, with its distance),
// the base, and your own waypoint (set by clicking on the map). Markers slide along with where the camera looks.
public class CompassUI : UIWindow
{
    const float Width = 640f, Height = 46f, Span = 200f; // Degrees shown across the strip

    public static Vector3? Waypoint;

    private RectTransform strip;
    private RectTransform[] letters;
    private Marker objective, home, waypoint;

    class Marker
    {
        public RectTransform root;
        public Image icon;
        public TMP_Text label;
    }

    void Start()
    {
        RectTransform root = CreateCanvas("Compass UI", 5).GetComponent<RectTransform>();
        strip = Rect("Compass", root, Vector2.zero, new Vector2(Width, Height));
        strip.anchorMin = strip.anchorMax = strip.pivot = new Vector2(0.5f, 1f);
        strip.anchoredPosition = new Vector2(0f, -18f);
        Rounded(strip, new Color(UITheme.Window.r, UITheme.Window.g, UITheme.Window.b, 0.7f), 12);
        strip.gameObject.AddComponent<RectMask2D>();
        Image(Rect("Center", strip, new Vector2(Width / 2f - 1f, 0f), new Vector2(2f, 8f)), new Color(1f, 1f, 1f, 0.5f));

        string[] names = { "N", "E", "S", "W" };
        letters = new RectTransform[4];
        for (int i = 0; i < 4; i++)
        {
            TMP_Text t = Text(Rect(names[i], strip, Vector2.zero, new Vector2(30f, Height)), names[i], 17f, true, TextAlignmentOptions.Center);
            t.color = i == 0 ? UITheme.Accent : UITheme.TextDim;
            letters[i] = t.rectTransform;
        }
        home = MakeMarker("base", UITheme.Text);
        waypoint = MakeMarker("navigation", new Color(0.45f, 0.85f, 1f));
        objective = MakeMarker("navigation", UITheme.Accent);
    }

    Marker MakeMarker(string icon, Color color)
    {
        var m = new Marker { root = Rect("Marker", strip, Vector2.zero, new Vector2(70f, Height)) };
        m.icon = Picture(Rect("Icon", m.root, new Vector2(26f, 3f), new Vector2(18f, 18f)), UITheme.Icon(icon));
        m.icon.color = color;
        m.label = Text(Rect("Distance", m.root, new Vector2(0f, 22f), new Vector2(70f, 20f)), "", 12f, true, TextAlignmentOptions.Center);
        m.label.color = color;
        return m;
    }

    void Update()
    {
        Camera cam = Camera.main;
        SurvivalStats player = SurvivalStats.Player;
        bool show = cam != null && player != null && !InteractionSystem.MenuOpen;
        if (strip == null) return;
        strip.gameObject.SetActive(show);
        if (!show) return;

        float yaw = cam.transform.eulerAngles.y;
        for (int i = 0; i < 4; i++)
        {
            float a = Mathf.DeltaAngle(yaw, i * 90f);
            letters[i].gameObject.SetActive(Mathf.Abs(a) <= Span / 2f);
            Place(letters[i], a, 15f);
        }

        Vector3 me = player.transform.position;
        Show(home, BaseArea.Main != null ? BaseArea.Main.transform.position : (Vector3?)null, me, yaw);
        Show(waypoint, Waypoint, me, yaw);
        Show(objective, ObjectiveUI.Target != null ? ObjectiveUI.Target.position : (Vector3?)null, me, yaw);
    }

    void Show(Marker m, Vector3? target, Vector3 me, float yaw)
    {
        m.root.gameObject.SetActive(target.HasValue);
        if (!target.HasValue) return;
        Vector3 to = Vector3.ProjectOnPlane(target.Value - me, Vector3.up);
        float bearing = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
        float angle = Mathf.Clamp(Mathf.DeltaAngle(yaw, bearing), -Span / 2f, Span / 2f); // Off the strip: pinned to its edge
        Place(m.root, angle, 35f);
        float d = to.magnitude;
        m.label.text = d < 30f ? "here" : $"{d:0} m";
    }

    void Place(RectTransform rt, float angle, float halfWidth)
    {
        rt.anchoredPosition = new Vector2(Width / 2f + angle / (Span / 2f) * (Width / 2f - 30f) - halfWidth, 0f);
    }
}
