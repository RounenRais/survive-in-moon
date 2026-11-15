using TMPro;
using UnityEngine;

// "You died" with the mission debrief so far; Respawn wakes you up in the base with your inventory intact.
public class DeathUI : UIWindow
{
    const float W = 640f;

    private static DeathUI instance;
    public static bool IsOpen => instance != null && instance.gameObject.activeSelf;

    private RectTransform box, report;

    public static void Show()
    {
        if (instance == null)
        {
            GameObject root = CreateCanvas("Death UI", 90);
            instance = root.AddComponent<DeathUI>();
            instance.Build(root.GetComponent<RectTransform>());
        }
        instance.Refresh();
        instance.gameObject.SetActive(true);
        Time.timeScale = 0f;
    }

    void Build(RectTransform root)
    {
        RectTransform veil = Rect("Veil", root, Vector2.zero, Vector2.zero);
        veil.anchorMin = Vector2.zero;
        veil.anchorMax = Vector2.one;
        veil.offsetMin = veil.offsetMax = Vector2.zero;
        Image(veil, new Color(0.05f, 0f, 0f, 0.88f)).raycastTarget = true;
        box = Centered("Box", veil, new Vector2(W, 600f));
        Rounded(box, UITheme.Window, 16);
    }

    // Rebuilt every time: the numbers change between deaths
    void Refresh()
    {
        Clear(box);
        Text(Rect("Title", box, new Vector2(0f, 22f), new Vector2(W, 60f)), "You died", 48f, true, TextAlignmentOptions.Center);
        TMP_Text line = Text(Rect("Line", box, new Vector2(0f, 82f), new Vector2(W, 26f)), "Your items are safe in your bag.", 17f, false, TextAlignmentOptions.Center);
        line.color = UITheme.TextDim;
        report = Rect("Report", box, new Vector2(40f, 124f), new Vector2(W - 80f, 420f));
        float h = DebriefUI.Build(report, W - 80f, false);
        box.sizeDelta = new Vector2(W, 124f + h + 100f);
        LabelButton(box, new Vector2(W / 2f - 140f, 124f + h + 24f), new Vector2(280f, 56f), UITheme.Accent, UITheme.OnAccent, "Respawn", 22f, Respawn);
    }

    void Respawn()
    {
        Time.timeScale = 1f;
        gameObject.SetActive(false);
        if (SurvivalStats.Player != null) SurvivalStats.Player.Respawn();
    }
}
