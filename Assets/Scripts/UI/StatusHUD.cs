using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Top right of the screen: the player's vitals as thin bars (health, oxygen, food, water, warmth, stamina,
// radiation dose), where you are (in the base / how far out, sun or shade), an incoming solar storm,
// active boosts, and the base's own needs (power balance, the crew's food / water / oxygen stock).
public class StatusHUD : UIWindow
{
    const float Width = 250f, RowHeight = 28f, Pad = 16f, IconSize = 20f;

    class Row
    {
        public SurvivalStats.Stat stat;
        public Image fill;
        public TMP_Text value;
        public Color color;
    }

    private readonly List<Row> rows = new List<Row>();
    private TMP_Text place, storm, boosts, power, crewFood, crewWater, crewOxygen;
    private RectTransform panel;
    private float nextRefresh;

    void Start()
    {
        SolarStorms.Announce += GameSystems.Notify;
        if (SurvivalStats.Player != null) SurvivalStats.Player.Notice += GameSystems.Notify;
        RectTransform root = CreateCanvas("Status HUD", 5).GetComponent<RectTransform>();

        panel = Rect("Panel", root, Vector2.zero, new Vector2(Width, 100f));
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(1f, 1f);
        panel.anchoredPosition = new Vector2(-24f, -24f);
        Rounded(panel, new Color(UITheme.Window.r, UITheme.Window.g, UITheme.Window.b, 0.82f), 14);

        float y = Pad;
        AddRow("health", SurvivalStats.Stat.Health, new Color(0.95f, 0.42f, 0.45f), ref y);
        AddRow("oxygen", SurvivalStats.Stat.Oxygen, new Color(0.45f, 0.78f, 1f), ref y);
        AddRow("food", SurvivalStats.Stat.Food, new Color(0.96f, 0.7f, 0.36f), ref y);
        AddRow("water", SurvivalStats.Stat.Water, new Color(0.4f, 0.6f, 1f), ref y);
        AddRow("warmth", SurvivalStats.Stat.Warmth, new Color(1f, 0.55f, 0.3f), ref y);
        AddRow("stamina", SurvivalStats.Stat.Stamina, new Color(0.6f, 0.9f, 0.55f), ref y);
        AddRow("radiation", SurvivalStats.Stat.Radiation, new Color(0.85f, 0.9f, 0.3f), ref y);

        y += 6f;
        place = Line(ref y, UITheme.TextDim);
        storm = Line(ref y, UITheme.Accent);
        boosts = Line(ref y, new Color(0.7f, 0.85f, 1f));

        y += 8f;
        TMP_Text baseTitle = Text(Rect("Base", panel, new Vector2(Pad, y), new Vector2(Width - Pad * 2f, 20f)), "Base", 15f, true, TextAlignmentOptions.MidlineLeft);
        baseTitle.color = UITheme.Text;
        y += 24f;
        power = IconLine("power", ref y);
        panel.sizeDelta = new Vector2(Width, y + Pad - 4f);
    }

    void OnDestroy()
    {
        SolarStorms.Announce -= GameSystems.Notify;
        if (SurvivalStats.Player != null) SurvivalStats.Player.Notice -= GameSystems.Notify;
    }

    void AddRow(string icon, SurvivalStats.Stat stat, Color color, ref float y)
    {
        Icon(icon, new Vector2(Pad, y), color);
        RectTransform track = Rect("Track", panel, new Vector2(Pad + IconSize + 12f, y + 7f), new Vector2(Width - Pad * 2f - IconSize - 12f - 46f, 6f));
        Rounded(track, new Color(1f, 1f, 1f, 0.08f), 3);
        RectTransform fillRect = Rect("Fill", track, Vector2.zero, track.sizeDelta);
        Image fill = Rounded(fillRect, color, 3);
        TMP_Text value = Text(Rect("Value", panel, new Vector2(Width - Pad - 42f, y), new Vector2(42f, 20f)), "", 15f, false, TextAlignmentOptions.MidlineRight);
        rows.Add(new Row { stat = stat, fill = fill, value = value, color = color });
        y += RowHeight;
    }

    Image Icon(string name, Vector2 position, Color color)
    {
        Image image = Picture(Rect(name, panel, position, new Vector2(IconSize, IconSize)), UITheme.Icon(name));
        image.color = color;
        return image;
    }

    // An icon (dim) with a line of text next to it
    TMP_Text IconLine(string icon, ref float y)
    {
        Icon(icon, new Vector2(Pad, y), UITheme.TextDim);
        TMP_Text t = Text(Rect("Line", panel, new Vector2(Pad + IconSize + 12f, y), new Vector2(Width - Pad * 2f - IconSize - 12f, 20f)), "", 14f, false, TextAlignmentOptions.MidlineLeft);
        t.color = UITheme.TextDim;
        y += 24f;
        return t;
    }

    TMP_Text Line(ref float y, Color color)
    {
        TMP_Text t = Text(Rect("Line", panel, new Vector2(Pad, y), new Vector2(Width - Pad * 2f, 20f)), "", 14f, false, TextAlignmentOptions.MidlineLeft);
        t.color = color;
        y += 21f;
        return t;
    }

    void Update()
    {
        if (Time.unscaledTime < nextRefresh || panel == null) return;
        nextRefresh = Time.unscaledTime + 0.1f;
        SurvivalStats stats = SurvivalStats.Player;
        panel.gameObject.SetActive(stats != null && !InteractionSystem.MenuOpen && !MapUI.IsOpen);
        if (stats == null) return;

        foreach (Row row in rows)
        {
            float value = stats.Get(row.stat), max = stats.Max(row.stat);
            float t = Mathf.Clamp01(value / max);
            float trackWidth = ((RectTransform)row.fill.transform.parent).sizeDelta.x;
            ((RectTransform)row.fill.transform).sizeDelta = new Vector2(Mathf.Max(6f, trackWidth * t), 6f);
            row.fill.enabled = t > 0.001f;
            bool danger = row.stat == SurvivalStats.Stat.Radiation ? value > 60f : t < 0.25f;
            row.fill.color = danger ? UITheme.Bad : row.color;
            row.value.text = row.stat == SurvivalStats.Stat.Radiation ? $"{value:0}%" : $"{Mathf.CeilToInt(value)}";
            row.value.color = danger ? UITheme.Bad : UITheme.Text;
        }

        // Where we are
        if (stats.InLifeSupport) place.text = "Inside life support";
        else
        {
            float s = stats.OxygenSecondsLeft;
            string air = float.IsInfinity(s) ? "" : $" · air {Mathf.FloorToInt(s / 60f)}:{Mathf.FloorToInt(s % 60f):00}";
            place.text = $"{stats.DistanceFromBase:0} m out{air}{(stats.InShadow ? " · shade" : "")}";
        }
        place.color = !stats.InLifeSupport && stats.OxygenSecondsLeft < 60f ? UITheme.Bad : UITheme.TextDim;

        // Solar storm
        if (SolarStorms.Warning && SolarStorms.Current != null)
            storm.text = $"Solar flare {SolarStorms.Current.classType} · storm in {Mathf.CeilToInt(SolarStorms.SecondsToStorm)} s";
        else if (SolarStorms.Strength > 0.01f && SolarStorms.Current != null)
            storm.text = $"Radiation storm · {SolarStorms.Current.classType}";
        else storm.text = "";
        storm.color = SolarStorms.Strength > 0.01f ? UITheme.Bad : UITheme.Accent;

        // Boosts
        string b = "";
        foreach (BoostType type in new[] { BoostType.Stamina, BoostType.Metabolism, BoostType.Warmth })
            if (stats.HasBoost(type)) b += (b.Length > 0 ? " · " : "") + $"{BoostName(type)} {Clock(stats.BoostSecondsLeft(type))}";
        boosts.text = b;

        // Base
        BaseSupplies supplies = BaseSupplies.Instance;
        if (supplies != null)
        {
            power.text = $"{supplies.PowerProduced:0} kW made · {supplies.PowerUsed:0} kW used";
            power.color = supplies.HasPower ? UITheme.TextDim : UITheme.Bad;
        }
    }

    static void Stock(TMP_Text line, string label, float units, float minutes)
    {
        string time = float.IsInfinity(minutes) ? "" : $" · {Mathf.FloorToInt(minutes)} min";
        line.text = $"{label}  {units:0}{time}";
        line.color = units <= 0f ? UITheme.Bad : minutes < 5f ? UITheme.Accent : UITheme.TextDim;
    }

    static string BoostName(BoostType type) => type switch
    {
        BoostType.Stamina => "Stim",
        BoostType.Metabolism => "Nutrients",
        _ => "Heat pack"
    };

    static string Clock(float seconds) => $"{Mathf.FloorToInt(seconds / 60f)}:{Mathf.FloorToInt(seconds % 60f):00}";
}
