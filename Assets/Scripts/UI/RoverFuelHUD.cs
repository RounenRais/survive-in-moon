using TMPro;
using UnityEngine;
using UnityEngine.UI;

// While driving: the rover's fuel as a bar at the bottom left (fuel cells refill it when you get in).
public class RoverFuelHUD : UIWindow
{
    private RectTransform panel;
    private Image fill;
    private TMP_Text label;

    void Start()
    {
        RectTransform root = CreateCanvas("Rover HUD", 5).GetComponent<RectTransform>();
        panel = Rect("Panel", root, Vector2.zero, new Vector2(300f, 64f));
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0f, 0f);
        panel.anchoredPosition = new Vector2(24f, 150f);
        Rounded(panel, new Color(UITheme.Window.r, UITheme.Window.g, UITheme.Window.b, 0.82f), 14);
        label = Text(Rect("Label", panel, new Vector2(16f, 8f), new Vector2(268f, 22f)), "", 15f, false, TextAlignmentOptions.MidlineLeft);
        RectTransform track = Rect("Track", panel, new Vector2(16f, 40f), new Vector2(268f, 8f));
        Rounded(track, new Color(1f, 1f, 1f, 0.08f), 4);
        fill = Rounded(Rect("Fill", track, Vector2.zero, new Vector2(268f, 8f)), UITheme.Accent, 4);
    }

    void Update()
    {
        if (panel == null) return;
        MoonRover rover = MoonRover.Active;
        bool driving = rover != null && rover.Driver != null;
        panel.gameObject.SetActive(driving && !MapUI.IsOpen);
        if (!driving) return;
        float t = rover.FuelRatio;
        ((RectTransform)fill.transform).sizeDelta = new Vector2(Mathf.Max(8f, 268f * t), 8f);
        fill.color = t < 0.2f ? UITheme.Bad : UITheme.Accent;
        label.text = $"Rover fuel  {t * 100f:0}%   ·   {Mathf.Abs(rover.ForwardSpeed):0} u/s";
        label.color = t <= 0f ? UITheme.Bad : UITheme.Text;
    }
}
