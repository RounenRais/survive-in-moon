using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// The end screen after launching the repaired ship: a short line and a button back to the main menu.
public class GameOverUI : UIWindow
{
    private static GameOverUI instance;
    public static bool IsOpen => instance != null && instance.gameObject.activeSelf;

    public static void Show(bool won)
    {
        if (instance == null)
        {
            GameObject root = CreateCanvas("Game Over UI", 80);
            instance = root.AddComponent<GameOverUI>();
            instance.Build(root.GetComponent<RectTransform>(), won);
        }
        instance.gameObject.SetActive(true);
        Time.timeScale = 0f;
    }

    void Build(RectTransform root, bool won)
    {
        var veil = Rect("Veil", root, Vector2.zero, Vector2.zero);
        veil.anchorMin = Vector2.zero;
        veil.anchorMax = Vector2.one;
        veil.offsetMin = veil.offsetMax = Vector2.zero;
        Image(veil, new Color(0f, 0f, 0f, 0.82f)).raycastTarget = true;

        RectTransform box = Centered("Box", veil, new Vector2(640f, 260f));
        Text(Rect("Title", box, new Vector2(0f, 20f), new Vector2(640f, 70f)), won ? "Lift-off" : "Mission lost", 56f, true, TextAlignmentOptions.Center);
        TMP_Text line = Text(Rect("Line", box, new Vector2(0f, 100f), new Vector2(640f, 30f)),
                             won ? "The crew is heading home." : "", 20f, false, TextAlignmentOptions.Center);
        line.color = UITheme.TextDim;
        LabelButton(box, new Vector2(170f, 170f), new Vector2(300f, 58f), UITheme.Accent, UITheme.OnAccent, "Main menu", 22f, () =>
        {
            Time.timeScale = 1f;
            SceneManager.LoadScene("MainMenu");
        });
    }
}
