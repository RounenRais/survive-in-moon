using UnityEngine;

// Placeholder UI: "[F] Pick up Moon Rock" floating above the target, and short messages ("Inventory Full")
// in the middle of the screen. Only updates its text when the InteractionSystem says the target changed.
public class InteractionPromptUI : MonoBehaviour
{
    public InteractionSystem system; // Found on the same object if left empty
    public float messageDuration = 1.8f;

    private string prompt;
    private string message;
    private float messageEndTime;
    private GUIStyle keyStyle, labelStyle, messageStyle;

    void OnEnable()
    {
        if (system == null) system = GetComponent<InteractionSystem>();
        if (system == null) return;
        system.TargetChanged += OnTargetChanged;
        system.MessageShown += OnMessage;
        OnTargetChanged(system.CurrentTarget);
    }

    void OnDisable()
    {
        if (system == null) return;
        system.TargetChanged -= OnTargetChanged;
        system.MessageShown -= OnMessage;
    }

    void OnTargetChanged(IInteractable target) => prompt = target?.GetPrompt();

    void OnMessage(string text)
    {
        message = text;
        messageEndTime = Time.time + messageDuration;
    }

    void OnGUI()
    {
        if (keyStyle == null) MakeStyles();

        if (!string.IsNullOrEmpty(prompt) && Camera.main != null)
        {
            Vector3 screen = Camera.main.WorldToScreenPoint(system.TargetPoint);
            if (screen.z > 0f)
            {
                // Key box, then the text on a dark strip; centered above the target together, never wrapped
                const float keySize = 32f, gap = 6f;
                float x = screen.x, y = Screen.height - screen.y - 30f;
                float width = Mathf.Ceil(labelStyle.CalcSize(new GUIContent(prompt)).x) + 2f;
                float left = x - (keySize + gap + width) / 2f;
                GUI.Box(new Rect(left, y - keySize / 2f, keySize, keySize), system.interactKey.ToString(), keyStyle);
                GUI.Label(new Rect(left + keySize + gap, y - keySize / 2f, width, keySize), prompt, labelStyle);
            }
        }

        if (message != null && Time.time < messageEndTime)
            GUI.Label(new Rect(0f, Screen.height * 0.3f, Screen.width, 40f), message, messageStyle);
    }

    void MakeStyles()
    {
        keyStyle = new GUIStyle(GUI.skin.box)
        {
            fontSize = 18, font = UITheme.GuiFont, alignment = TextAnchor.MiddleCenter,
            normal = { background = Texture2D.whiteTexture, textColor = new Color(0.12f, 0.12f, 0.15f) }
        };
        var strip = new Texture2D(1, 1);
        strip.SetPixel(0, 0, UITheme.Strip);
        strip.Apply();
        labelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 17, font = UITheme.GuiFont, alignment = TextAnchor.MiddleLeft,
            wordWrap = false, clipping = TextClipping.Overflow, padding = new RectOffset(10, 10, 0, 0),
            normal = { textColor = Color.white, background = strip }
        };
        messageStyle = new GUIStyle(labelStyle)
        {
            fontSize = 24, alignment = TextAnchor.MiddleCenter,
            normal = { textColor = UITheme.Accent, background = null }
        };
    }
}
