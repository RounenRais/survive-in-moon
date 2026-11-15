using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

// The end screen: "Lift-off" after launching the repaired ship, or "Mission lost" (F2 in the presentation),
// with the mission debrief and a button back to the main menu. Rebuilt on every show (fresh numbers).
public class GameOverUI : UIWindow
{
    private static GameOverUI instance;
    public static bool IsOpen => instance != null && instance.gameObject.activeSelf;

    public static void Show(bool won)
    {
        if (instance != null) Destroy(instance.gameObject);
        GameObject root = CreateCanvas("Game Over UI", 80);
        instance = root.AddComponent<GameOverUI>();
        instance.Build(root.GetComponent<RectTransform>(), won);
        Time.timeScale = 0f;
    }

    void Build(RectTransform root, bool won)
    {
        var veil = Rect("Veil", root, Vector2.zero, Vector2.zero);
        veil.anchorMin = Vector2.zero;
        veil.anchorMax = Vector2.one;
        veil.offsetMin = veil.offsetMax = Vector2.zero;
        Image(veil, new Color(0f, 0f, 0f, 0.86f)).raycastTarget = true;

        const float W = 640f;
        RectTransform box = Centered("Box", veil, new Vector2(W, 640f));
        Rounded(box, UITheme.Window, 16);
        Text(Rect("Title", box, new Vector2(0f, 24f), new Vector2(W, 60f)), won ? "Lift-off" : "Mission lost", 48f, true, TextAlignmentOptions.Center);
        TMP_Text line = Text(Rect("Line", box, new Vector2(0f, 84f), new Vector2(W, 26f)),
                             won ? "Mission debrief" : "The crew couldn't hold the outpost together", 17f, false, TextAlignmentOptions.Center);
        line.color = UITheme.TextDim;
        RectTransform report = Rect("Report", box, new Vector2(40f, 126f), new Vector2(W - 80f, 420f));
        float h = DebriefUI.Build(report, W - 80f, won);
        box.sizeDelta = new Vector2(W, 126f + h + 100f);
        LabelButton(box, new Vector2(W / 2f - 150f, 126f + h + 24f), new Vector2(300f, 56f), UITheme.Accent, UITheme.OnAccent, "Main menu", 22f, () =>
        {
            Time.timeScale = 1f;
            SceneManager.LoadScene("MainMenu");
        });
    }
}
