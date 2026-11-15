using TMPro;
using UnityEngine;

// "You died" over a dark screen; Respawn wakes you up in the base with your inventory intact.
public class DeathUI : UIWindow
{
    private static DeathUI instance;
    public static bool IsOpen => instance != null && instance.gameObject.activeSelf;

    public static void Show()
    {
        if (instance == null)
        {
            GameObject root = CreateCanvas("Death UI", 90);
            instance = root.AddComponent<DeathUI>();
            instance.Build(root.GetComponent<RectTransform>());
        }
        instance.gameObject.SetActive(true);
        Time.timeScale = 0f;
    }

    void Build(RectTransform root)
    {
        RectTransform veil = Rect("Veil", root, Vector2.zero, Vector2.zero);
        veil.anchorMin = Vector2.zero;
        veil.anchorMax = Vector2.one;
        veil.offsetMin = veil.offsetMax = Vector2.zero;
        Image(veil, new Color(0.05f, 0f, 0f, 0.85f)).raycastTarget = true;

        RectTransform box = Centered("Box", veil, new Vector2(600f, 240f));
        Text(Rect("Title", box, new Vector2(0f, 10f), new Vector2(600f, 80f)), "You died", 64f, true, TextAlignmentOptions.Center);
        TMP_Text line = Text(Rect("Line", box, new Vector2(0f, 96f), new Vector2(600f, 28f)), "Your items are safe in your bag.", 19f, false, TextAlignmentOptions.Center);
        line.color = UITheme.TextDim;
        LabelButton(box, new Vector2(160f, 160f), new Vector2(280f, 58f), UITheme.Accent, UITheme.OnAccent, "Respawn", 22f, Respawn);
    }

    void Respawn()
    {
        Time.timeScale = 1f;
        gameObject.SetActive(false);
        if (SurvivalStats.Player != null) SurvivalStats.Player.Respawn();
    }
}
