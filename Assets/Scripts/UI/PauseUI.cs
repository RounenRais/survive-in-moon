using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

// Esc pauses the game: Resume or go back to the main menu. If another window is open, Esc closes that one
// first (this only opens when nothing else was open in the previous frame).
public class PauseUI : UIWindow
{
    private static PauseUI instance;
    public static bool IsOpen => instance != null && instance.window != null && instance.window.activeSelf;

    private GameObject window;
    private bool otherMenuWasOpen;

    void Awake() => instance = this;

    void Start()
    {
        RectTransform root = CreateCanvas("Pause UI", 85).GetComponent<RectTransform>();
        RectTransform veil = Rect("Veil", root, Vector2.zero, Vector2.zero);
        veil.anchorMin = Vector2.zero;
        veil.anchorMax = Vector2.one;
        veil.offsetMin = veil.offsetMax = Vector2.zero;
        Image(veil, new Color(0f, 0f, 0f, 0.7f)).raycastTarget = true;

        RectTransform box = Centered("Box", veil, new Vector2(420f, 300f));
        Rounded(box, UITheme.Window, 16);
        Text(Rect("Title", box, new Vector2(0f, 28f), new Vector2(420f, 50f)), "Paused", 40f, true, TextAlignmentOptions.Center);
        LabelButton(box, new Vector2(60f, 110f), new Vector2(300f, 58f), UITheme.Accent, UITheme.OnAccent, "Resume", 22f, Resume);
        LabelButton(box, new Vector2(60f, 184f), new Vector2(300f, 58f), new Color(1f, 1f, 1f, 0.08f), UITheme.Text, "Main menu", 22f, MainMenu);
        window = veil.gameObject;
        window.SetActive(false);
    }

    void Update()
    {
        if (window == null) return;
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (IsOpen) Resume();
            else if (!otherMenuWasOpen && !InteractionSystem.MenuOpen) Open();
        }
    }

    // Remembered after every window had its Update, so the Esc that closes a window doesn't also pause
    void LateUpdate() => otherMenuWasOpen = InteractionSystem.MenuOpen && !IsOpen;

    void Open()
    {
        window.SetActive(true);
        Time.timeScale = 0f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    void Resume()
    {
        window.SetActive(false);
        Time.timeScale = 1f;
    }

    void MainMenu()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene("MainMenu");
    }
}
