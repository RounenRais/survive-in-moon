using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// The title screen (scene MainMenu). The 3D backdrop (moon ground, Earth, sun) is placed in the scene by
// Survive In Moon > Build Main Menu; this script adds the astronaut standing on it, the slow camera drift and
// the UI: the game's name and two buttons, Play (loads the game scene) and Exit.
public class MainMenuUI : UIWindow
{
    public string gameScene = "SampleScene";
    public Transform astronautSpot;       // Where the astronaut stands (placed by the builder)
    public float cameraDriftDegrees = 6f; // How far the camera sways left and right around the astronaut
    public float cameraDriftSeconds = 26f;

    private Camera cam;
    private Vector3 camOffset, pivot;
    private float height = 7f; // Astronaut height, measured when it is spawned; the camera is framed by it
    private bool leaving;

    void Start()
    {
        cam = Camera.main;
        NasaData.UseEarthPhoto();
        SpawnAstronaut();
        if (astronautSpot != null)
        {
            // Camera low and a bit to the left, looking slightly up: the astronaut stands tall on the right half
            camOffset = new Vector3(-0.32f, 0.38f, -1.75f) * height;
            pivot = astronautSpot.position + Vector3.up * height * 0.62f;
        }
        BuildUI();
    }

    void Update()
    {
        // Slow sway around the astronaut, so the screen feels alive
        if (cam != null && astronautSpot != null)
        {
            float angle = Mathf.Sin(Time.time * Mathf.PI * 2f / cameraDriftSeconds) * cameraDriftDegrees;
            cam.transform.position = astronautSpot.position + Quaternion.Euler(0f, angle, 0f) * camOffset;
            // Aim left of the astronaut, so it stands on the right side of the screen (the UI is on the left)
            Vector3 right = Vector3.Cross(Vector3.up, pivot - cam.transform.position).normalized;
            cam.transform.rotation = Quaternion.LookRotation(pivot - right * height * 0.48f - cam.transform.position);
        }
    }

    // The chosen character (or the first one) in the Idle pose, with nothing that would make it walk or collide
    void SpawnAstronaut()
    {
        if (astronautSpot == null) return;
        // The same astronaut the game starts with (the captain)
        CharacterDefinition character = CharacterDatabase.Instance != null ? CharacterDatabase.Instance.Default : CharacterSelection.Current;
        if (character == null && CharacterDatabase.Instance != null && CharacterDatabase.Instance.characters.Count > 0)
            character = CharacterDatabase.Instance.characters[0];
        if (character == null || character.prefab == null) return;

        // The Idle clip writes the model's own root rotation, so turn a holder around it instead (like the player does)
        var holder = new GameObject("Menu Astronaut").transform;
        holder.SetPositionAndRotation(astronautSpot.position, astronautSpot.rotation * Quaternion.Euler(0f, character.yawOffset, 0f));
        GameObject astronaut = Instantiate(character.prefab, holder);
        astronaut.transform.localPosition = Vector3.zero;
        astronaut.transform.localRotation = Quaternion.identity;
        foreach (Collider c in astronaut.GetComponentsInChildren<Collider>()) c.enabled = false;
        Animator animator = astronaut.GetComponentInChildren<Animator>();
        if (animator != null) animator.applyRootMotion = false;
        // Standing still on the terrain: the foot planting would keep nudging the body down during the idle
        foreach (AstronautAnimation a in astronaut.GetComponentsInChildren<AstronautAnimation>()) a.enabled = false;

        Bounds bounds = new Bounds(astronaut.transform.position, Vector3.zero);
        foreach (Renderer r in astronaut.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(r.bounds);
        if (bounds.size.y > 0.5f) height = bounds.size.y;
    }

    void BuildUI()
    {
        RectTransform root = CreateCanvas("Main Menu UI", 10).GetComponent<RectTransform>();

        // Soft dark fade from the left edge, so the text reads well over the 3D scene
        RectTransform shade = new GameObject("Shade", typeof(RectTransform)).GetComponent<RectTransform>();
        shade.SetParent(root, false);
        shade.anchorMin = Vector2.zero;
        shade.anchorMax = new Vector2(0.55f, 1f);
        shade.offsetMin = shade.offsetMax = Vector2.zero;
        var shadeImage = shade.gameObject.AddComponent<RawImage>();
        shadeImage.texture = HorizontalFade(new Color(0.02f, 0.022f, 0.03f, 0.7f));
        shadeImage.raycastTarget = false;

        RectTransform column = Rect("Column", root, Vector2.zero, new Vector2(620f, 520f));
        column.anchorMin = column.anchorMax = new Vector2(0f, 0.5f);
        column.pivot = new Vector2(0f, 0.5f);
        column.anchoredPosition = new Vector2(150f, 0f);

        Text(Rect("Title", column, new Vector2(0f, 60f), new Vector2(620f, 96f)), "Survive in Moon", 76f, true, TextAlignmentOptions.MidlineLeft);

        Button play = MenuButton(column, 250f, "Play", UITheme.Accent, UITheme.OnAccent, Play);
        MenuButton(column, 322f, "Resources", new Color(1f, 1f, 1f, 0.08f), UITheme.Text, () => resources.SetActive(true));
        MenuButton(column, 394f, "Exit", new Color(1f, 1f, 1f, 0.08f), UITheme.Text, Exit);
        play.Select();
        resources = ResourcesPanel(root);
    }

    private GameObject resources;

    void LateUpdate()
    {
        if (resources != null && resources.activeSelf && Input.GetKeyDown(KeyCode.Escape)) resources.SetActive(false);
    }

    // The NASA sources the game is built on (with what was fetched and what each one does in the game),
    // and what keeps the crew alive
    GameObject ResourcesPanel(RectTransform root)
    {
        const float W = 1240f, H = 760f, Left = 760f;
        GameObject veil = WindowFrame(root, new Vector2(W, H), out RectTransform box).gameObject;
        Header(box, W, "", "Resources", () => veil.SetActive(false));

        NasaData.Meta m = NasaData.Info;
        var sources = new System.Collections.Generic.List<(string name, string url, string data, string use)>();
        if (m != null)
        {
            sources.Add(("DONKI · Space Weather Database", "https://ccmc.gsfc.nasa.gov/tools/DONKI/",
                         $"{m.donkiFlares} solar flares, {m.donkiFrom} – {m.donkiTo}",
                         "Every radiation storm in the game is one of these real flares, replayed in order. Its class sets the strength."));
            sources.Add(("RadLab · NASA OSDR", "https://visualization.osdr.nasa.gov/radlab/gui/data-api/",
                         $"Lunar surface dose (LRO / CRaTER): {m.moonDoseMedian:0.0} µGy/h median, ISS {m.issDoseMedian:0.0} µGy/h · {m.moonDoseSamples:N0} hours, {m.moonDoseFrom?.Substring(0, 4)}–{m.moonDoseTo?.Substring(0, 4)}",
                         "Sets the everyday radiation dose outside the base and how much worse it gets in a storm."));
            string studies = m.studies != null && m.studies.Length > 0
                ? string.Join("\n", System.Array.ConvertAll(m.studies, s => $"{s.accession}  {Short(s.title, 70)}").Take(3)) : "";
            sources.Add(("OSDR · Biological Data", "https://osdr.nasa.gov/bio/repo/", studies,
                         "Greenhouse crops follow NASA's plant-growth experiments: lettuce first, potatoes for calories."));
            if (!string.IsNullOrEmpty(m.epicDate))
                sources.Add(("EPIC · api.nasa.gov", "https://api.nasa.gov/", $"DSCOVR photo of Earth, {m.epicDate} UTC",
                             "The Earth in the lunar sky is this real photograph."));
        }

        float y = 100f;
        foreach (var src in sources)
        {
            var (name, url, data, use) = src;
            Button link = Button(box, new Vector2(32f, y), new Vector2(Left - 32f, 30f), new Color(0f, 0f, 0f, 0f), () => Application.OpenURL(url));
            TMP_Text title = Text(Rect("Name", link.transform, Vector2.zero, new Vector2(Left - 32f, 30f)), name, 21f, true, TextAlignmentOptions.MidlineLeft);
            title.color = UITheme.Accent;
            y += 34f;
            if (!string.IsNullOrEmpty(data)) y = Para(box, data, y, UITheme.Text, 16f, Left);
            y = Para(box, use, y, UITheme.TextDim, 16f, Left) + 18f;
        }
        if (m == null) Para(box, "No NASA data yet: Survive In Moon > Fetch NASA Data in the editor.", y, UITheme.Bad, 17f, Left);

        // Right column: the Earth photo and what everyone needs
        float x = Left + 40f, rw = W - x - 32f;
        Texture2D earth = NasaData.EarthPhoto;
        if (earth != null)
        {
            RawImage img = Rect("Earth", box, new Vector2(x + rw / 2f - 90f, 96f), new Vector2(180f, 180f)).gameObject.AddComponent<RawImage>();
            img.texture = earth;
            img.raycastTarget = false;
        }
        float ny = 300f;
        Text(Rect("Needs", box, new Vector2(x, ny), new Vector2(rw, 30f)), "Needs", 21f, true, TextAlignmentOptions.MidlineLeft);
        ny += 38f;
        foreach (var (who, icon, needs) in new[]
        {
            ("You", "person", new[] { "oxygen", "food", "water", "warmth", "stamina", "radiation" }),
            ("Crew", "crew", new[] { "food", "water", "oxygen" }),
            ("Base", "base", new[] { "power" }),
            ("Rover", "rover", new[] { "fuel" }),
            ("Ship", "ship", new[] { "hull", "engine", "navigation", "fuel" }),
        })
        {
            Image badge = Picture(Rect(who + " icon", box, new Vector2(x, ny), new Vector2(26f, 26f)), UITheme.Icon(icon));
            badge.color = UITheme.Text;
            Text(Rect(who, box, new Vector2(x + 36f, ny), new Vector2(110f, 26f)), who, 18f, true, TextAlignmentOptions.MidlineLeft);
            // What it needs, as a row of icons
            for (int i = 0; i < needs.Length; i++)
            {
                Image need = Picture(Rect(needs[i], box, new Vector2(x + 150f + i * 34f, ny + 1f), new Vector2(24f, 24f)), UITheme.Icon(needs[i]));
                need.color = NeedColor(needs[i]);
            }
            ny += 44f;
        }
        if (m != null)
            Text(Rect("Fetched", box, new Vector2(32f, H - 52f), new Vector2(600f, 24f)), $"Data fetched {m.fetchedAt}", 14f, false, TextAlignmentOptions.MidlineLeft).color = UITheme.TextDim;

        veil.SetActive(false);
        return veil;
    }

    // A wrapped paragraph; returns the y below it
    static float Para(RectTransform box, string text, float y, Color color, float size, float width, float x = 32f)
    {
        TMP_Text t = Text(Rect("Text", box, new Vector2(x, y), new Vector2(width - (x == 32f ? 32f : 0f), 24f)), text, size, false, TextAlignmentOptions.TopLeft);
        t.textWrappingMode = TextWrappingModes.Normal;
        t.overflowMode = TextOverflowModes.Overflow;
        t.color = color;
        t.ForceMeshUpdate();
        float h = Mathf.Max(size * 1.4f, t.preferredHeight);
        t.rectTransform.sizeDelta = new Vector2(t.rectTransform.sizeDelta.x, h);
        return y + h + 2f;
    }

    static Color NeedColor(string need) => need switch
    {
        "oxygen" => new Color(0.45f, 0.78f, 1f),
        "food" => new Color(0.96f, 0.7f, 0.36f),
        "water" => new Color(0.4f, 0.6f, 1f),
        "warmth" => new Color(1f, 0.55f, 0.3f),
        "stamina" => new Color(0.6f, 0.9f, 0.55f),
        "radiation" => new Color(0.85f, 0.9f, 0.3f),
        "power" => new Color(1f, 0.85f, 0.35f),
        "fuel" => new Color(0.95f, 0.78f, 0.2f),
        _ => UITheme.TextDim
    };

    static string Short(string s, int n) => s == null ? "" : s.Length <= n ? s : s.Substring(0, n - 1) + "…";

    Button MenuButton(RectTransform parent, float y, string label, Color color, Color textColor, UnityEngine.Events.UnityAction onClick)
    {
        Button button = LabelButton(parent, new Vector2(0f, y), new Vector2(320f, 58f), color, textColor, label, 24f, onClick);
        ColorBlock colors = button.colors;
        colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
        colors.selectedColor = Color.white;
        colors.fadeDuration = 0.08f;
        button.colors = colors;
        return button;
    }

    void Play()
    {
        if (leaving) return;
        leaving = true;
        SceneManager.LoadScene(gameScene);
    }

    void Exit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // A texture that is the given color on the left and fades to clear on the right (smoothly)
    static Texture2D HorizontalFade(Color color)
    {
        const int width = 256;
        var tex = new Texture2D(width, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int x = 0; x < width; x++)
        {
            float t = x / (width - 1f);
            float a = 1f - Mathf.SmoothStep(0.35f, 1f, t);
            tex.SetPixel(x, 0, new Color(color.r, color.g, color.b, color.a * a));
        }
        tex.Apply();
        return tex;
    }
}
