using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The mission debrief: how the decisions played out, with real reference numbers. Built into the launch screen
// (GameOverUI) and the death screen (DeathUI). The grade weighs the ship repair, the radiation dose against
// NASA's career limit, how steady the power was, how well the crew was fed and how often you died.
public static class DebriefUI
{
    public static int Score(bool launched)
    {
        float ship = launched ? 1f : ShipWreck.Instance != null ? ShipWreck.Instance.Progress : 0f;
        float dose = Mathf.Clamp01(1f - MissionLog.DoseMsv / (MissionLog.CareerLimitMsv * 0.1f)); // 60 mSv = 0
        float power = MissionLog.PowerUptime;
        float crew = MissionLog.SecondsPlayed > 0f ? Mathf.Clamp01(1f - MissionLog.CrewHungrySeconds / MissionLog.SecondsPlayed) : 1f;
        float lives = Mathf.Clamp01(1f - MissionLog.Deaths * 0.34f);
        return Mathf.RoundToInt(ship * 40f + dose * 20f + power * 15f + crew * 15f + lives * 10f);
    }

    public static string Grade(int score) => score >= 90 ? "A" : score >= 75 ? "B" : score >= 60 ? "C" : score >= 45 ? "D" : "F";

    // Rows of icon + label + value + a one-line explanation. Returns the height used.
    public static float Build(RectTransform parent, float width, bool launched)
    {
        float y = 0f;
        int score = Score(launched);
        Row(parent, width, ref y, "ship", "Ship repair", $"{(launched ? 100f : (ShipWreck.Instance != null ? ShipWreck.Instance.Progress * 100f : 0f)):0}%",
            launched ? "Repaired and launched" : "Parts still missing", UITheme.Accent);
        float pct = MissionLog.DoseMsv / MissionLog.CareerLimitMsv * 100f;
        Row(parent, width, ref y, "radiation", "Radiation dose", $"{MissionLog.DoseMsv:0.0} mSv",
            $"{pct:0.0}% of NASA's 600 mSv career limit · lunar data: LRO/CRaTER", new Color(0.85f, 0.9f, 0.3f));
        Row(parent, width, ref y, "power", "Power uptime", $"{MissionLog.PowerUptime * 100f:0}%",
            MissionLog.PowerUptime < 0.9f ? "Outages slow life support and stop the greenhouse" : "The grid held", new Color(1f, 0.85f, 0.35f));
        Row(parent, width, ref y, "food", "Food", $"{MissionLog.CropsHarvested} crops",
            $"{MissionLog.MealsGivenToCrew} meals given to the crew", new Color(0.96f, 0.7f, 0.36f));
        string storms = MissionLog.Storms.Count == 0 ? "None yet" : string.Join(", ", MissionLog.Storms.GetRange(0, Mathf.Min(3, MissionLog.Storms.Count)));
        Row(parent, width, ref y, "radiation", "Solar storms", $"{MissionLog.Storms.Count}",
            $"Real NASA DONKI flares: {storms}", new Color(0.78f, 0.36f, 0.84f));
        Row(parent, width, ref y, "health", "Lives lost", $"{MissionLog.Deaths}",
            $"Mission day {Mathf.Max(1, Mathf.CeilToInt(MissionLog.Days))}", new Color(0.95f, 0.42f, 0.45f));

        // Grade
        RectTransform g = Make("Grade", parent, new Vector2(0f, y + 6f), new Vector2(width, 54f));
        var gi = g.gameObject.AddComponent<Image>();
        gi.sprite = UITheme.SkinSprite(UITheme.Skin.Card);
        gi.type = Image.Type.Sliced;
        gi.color = UITheme.Card;
        Label(g, new Vector2(18f, 0f), new Vector2(200f, 54f), "Mission grade", 18f, true, UITheme.Text, TextAlignmentOptions.MidlineLeft);
        Label(g, new Vector2(width - 200f, 0f), new Vector2(182f, 54f), $"{Grade(score)}  ·  {score}/100", 26f, true, UITheme.Accent, TextAlignmentOptions.MidlineRight);
        return y + 66f;
    }

    static void Row(RectTransform parent, float width, ref float y, string icon, string label, string value, string note, Color color)
    {
        RectTransform row = Make(label, parent, new Vector2(0f, y), new Vector2(width, 48f));
        RectTransform ic = Make("Icon", row, new Vector2(0f, 6f), new Vector2(22f, 22f));
        var img = ic.gameObject.AddComponent<Image>();
        img.sprite = UITheme.Icon(icon);
        img.preserveAspect = true;
        img.color = color;
        img.raycastTarget = false;
        Label(row, new Vector2(34f, 0f), new Vector2(220f, 26f), label, 17f, true, UITheme.Text, TextAlignmentOptions.MidlineLeft);
        Label(row, new Vector2(width - 180f, 0f), new Vector2(180f, 26f), value, 17f, true, UITheme.Text, TextAlignmentOptions.MidlineRight);
        Label(row, new Vector2(34f, 24f), new Vector2(width - 34f, 20f), note, 13f, false, UITheme.TextDim, TextAlignmentOptions.MidlineLeft);
        y += 52f;
    }

    static RectTransform Make(string name, Transform parent, Vector2 pos, Vector2 size)
    {
        var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(pos.x, -pos.y);
        rt.sizeDelta = size;
        return rt;
    }

    static void Label(RectTransform parent, Vector2 pos, Vector2 size, string text, float fontSize, bool bold, Color color, TextAlignmentOptions align)
    {
        var t = Make("Text", parent, pos, size).gameObject.AddComponent<TextMeshProUGUI>();
        if (TMP_Settings.defaultFontAsset != null) t.font = TMP_Settings.defaultFontAsset;
        t.text = text;
        t.fontSize = fontSize;
        t.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
        t.color = color;
        t.alignment = align;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.overflowMode = TextOverflowModes.Ellipsis;
        t.raycastTarget = false;
    }
}
