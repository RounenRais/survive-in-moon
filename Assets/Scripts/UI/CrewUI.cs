using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Managing the crew from the screen:
//  - a small bar at the bottom left with a photo of each crew mate and a colored dot for what they are doing;
//    click a photo (or press C) to open the crew window
//  - the crew window: a big photo card per crew mate with their status and order buttons:
//    Follow me, Go to a point, Wait here, Back to the hub. A crew mate with a building job can't take orders.
//  - "Go to a point": the window closes, click on the ground to send them there (Esc cancels)
// Created by CrewManager when the game starts.
public class CrewUI : UIWindow
{
    const float Width = 1080f, Height = 720f;
    public KeyCode toggleKey = KeyCode.C;

    private static CrewUI instance;
    public static bool IsOpen => instance != null && instance.window.activeSelf;

    private GameObject window;
    private RectTransform cards, bar;
    private string cardsShown, barShown; // Rebuilt only when something changed (a destroyed button loses its click)
    private float nextRefresh;
    private CrewMember picking;           // Waiting for a click on the ground for this crew mate
    private LineRenderer marker;
    private float markerTime;

    public static void Ensure()
    {
        if (instance != null) return;
        GameObject root = CreateCanvas("Crew UI", 40);
        instance = root.AddComponent<CrewUI>();
        instance.Build(root.transform);
    }

    public static void Open()
    {
        Ensure();
        instance.window.SetActive(true);
        instance.cardsShown = null;
        instance.Refresh();
    }

    public static void Close()
    {
        if (instance != null) instance.window.SetActive(false);
    }

    // --------------------------------------------------------------- Update

    void Update()
    {
        bool otherWindow = BuildMenuUI.IsOpen || (BuildManager.Instance != null && BuildManager.Instance.IsPlacing);
        bar.gameObject.SetActive(!otherWindow && !IsOpen);

        if (picking != null) { UpdatePicking(); return; }
        if (!otherWindow && Input.GetKeyDown(toggleKey)) { if (IsOpen) Close(); else Open(); }
        else if (IsOpen && Input.GetKeyDown(KeyCode.Escape)) Close();

        if (Time.unscaledTime >= nextRefresh) Refresh();
        UpdateMarker();
    }

    void Refresh()
    {
        nextRefresh = Time.unscaledTime + 0.25f;
        IReadOnlyList<CrewMember> crew = Crew();
        string state = "";
        foreach (CrewMember member in crew) state += "|" + member.displayName + member.Status;
        if (state != barShown) { barShown = state; BuildBar(crew); }
        if (IsOpen && state != cardsShown) { cardsShown = state; BuildCards(crew); }
    }

    static IReadOnlyList<CrewMember> Crew()
    {
        var list = new List<CrewMember>();
        if (CrewManager.Instance != null)
            foreach (CrewMember member in CrewManager.Instance.Members)
                if (member != null && !member.isPlayer) list.Add(member);
        return list;
    }

    // --------------------------------------------------------------- Go to a point

    void StartPicking(CrewMember member)
    {
        Close();
        picking = member;
    }

    void UpdatePicking()
    {
        if (Input.GetKeyDown(KeyCode.Escape) || picking == null) { picking = null; return; }
        bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        if (!Input.GetMouseButtonDown(0) || overUI) return;
        if (!BuildManager.MouseOnGround(out Vector3 point)) return;
        picking.MoveTo(point);
        ShowMarker(point);
        picking = null;
    }

    void OnGUI()
    {
        if (picking != null)
            UITheme.DrawHint($"Send {picking.displayName}", "Left click on the ground    Esc to cancel", UITheme.TextDim);
    }

    // A ring on the ground where the crew mate was sent; fades out
    void ShowMarker(Vector3 point)
    {
        if (marker == null)
        {
            var go = new GameObject("Crew Target Marker");
            marker = go.AddComponent<LineRenderer>();
            marker.useWorldSpace = true;
            marker.loop = true;
            marker.widthMultiplier = 0.6f;
            marker.sharedMaterial = BuildGhost.CreateTransparentMaterial(Color.white);
            marker.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            marker.positionCount = 40;
        }
        for (int i = 0; i < 40; i++)
        {
            float a = i * Mathf.PI * 2f / 40f;
            marker.SetPosition(i, point + new Vector3(Mathf.Cos(a) * 4f, 0.3f, Mathf.Sin(a) * 4f));
        }
        marker.gameObject.SetActive(true);
        markerTime = 1.5f;
    }

    void UpdateMarker()
    {
        if (marker == null || !marker.gameObject.activeSelf) return;
        markerTime -= Time.deltaTime;
        Color c = UITheme.Accent;
        c.a = Mathf.Clamp01(markerTime / 1.5f);
        marker.startColor = marker.endColor = c;
        marker.sharedMaterial.SetColor("_BaseColor", c);
        if (markerTime <= 0f) marker.gameObject.SetActive(false);
    }

    // --------------------------------------------------------------- Building the UI

    void Build(Transform root)
    {
        // The bar at the bottom left (outside the window, always there)
        bar = Rect("Crew Bar", root, Vector2.zero, new Vector2(400f, 120f));
        bar.anchorMin = bar.anchorMax = bar.pivot = new Vector2(0f, 0f);
        bar.anchoredPosition = new Vector2(24f, 24f);

        window = WindowFrame(root, new Vector2(Width, Height), out RectTransform box).gameObject;
        Header(box, Width, "BASE CREW", "Crew", Close);
        cards = Rect("Cards", box, new Vector2(32f, 116f), new Vector2(Width - 64f, Height - 148f));
        window.SetActive(false);
    }

    void BuildBar(IReadOnlyList<CrewMember> crew)
    {
        Clear(bar);
        const float size = 76f, gap = 10f;
        for (int i = 0; i < crew.Count; i++)
        {
            CrewMember member = crew[i];
            Button button = Button(bar, new Vector2(i * (size + gap), 120f - size - 22f), new Vector2(size, size), UITheme.Window, Open);
            RectTransform photo = Rect("Photo", button.transform, new Vector2(4f, 4f), new Vector2(size - 8f, size - 8f));
            Rounded(photo, UITheme.Well, 12);
            Picture(Rect("Portrait", photo, new Vector2(2f, 2f), new Vector2(size - 12f, size - 12f)), Portrait(member));
            // Status dot: green = free (in the hub or waiting), amber = on the move, red = building
            RectTransform dot = Rect("Dot", button.transform, new Vector2(size - 20f, size - 20f), new Vector2(14f, 14f));
            Rounded(dot, UITheme.Window, 7);
            Rounded(Rect("Fill", dot, new Vector2(2f, 2f), new Vector2(10f, 10f)), StatusColor(member), 5);
        }
        TMP_Text hint = Text(Rect("Hint", bar, new Vector2(0f, 120f - 18f), new Vector2(300f, 18f)), $"{toggleKey}  Crew", 13f, true, TextAlignmentOptions.MidlineLeft);
        hint.color = UITheme.TextDim;
    }

    void BuildCards(IReadOnlyList<CrewMember> crew)
    {
        Clear(cards);
        if (crew.Count == 0)
        {
            Text(Rect("None", cards, Vector2.zero, new Vector2(400f, 40f)), "No crew yet", 20f, false, TextAlignmentOptions.MidlineLeft).color = UITheme.TextDim;
            return;
        }
        const float gap = 24f;
        float cardWidth = Mathf.Min(480f, (Width - 64f - gap * (crew.Count - 1)) / crew.Count);
        float cardHeight = Height - 148f;
        float start = (Width - 64f - (cardWidth * crew.Count + gap * (crew.Count - 1))) / 2f;
        for (int i = 0; i < crew.Count; i++)
        {
            CrewMember member = crew[i];
            RectTransform card = Rect(member.displayName, cards, new Vector2(start + i * (cardWidth + gap), 0f), new Vector2(cardWidth, cardHeight));
            Rounded(card, UITheme.Panel, 18);

            RectTransform photo = Rect("Photo", card, new Vector2(16f, 16f), new Vector2(cardWidth - 32f, 280f));
            Rounded(photo, UITheme.Well, 14);
            Picture(Rect("Portrait", photo, new Vector2(8f, 8f), new Vector2(cardWidth - 48f, 264f)), Portrait(member));

            Text(Rect("Name", card, new Vector2(24f, 312f), new Vector2(cardWidth - 48f, 36f)), member.displayName, 26f, true, TextAlignmentOptions.MidlineLeft);
            Color statusColor = StatusColor(member);
            RectTransform pill = Rect("Status", card, new Vector2(24f, 354f), new Vector2(cardWidth - 48f, 32f));
            Rounded(pill, new Color(statusColor.r, statusColor.g, statusColor.b, 0.14f), 16);
            Text(Rect("Label", pill, new Vector2(14f, 0f), new Vector2(cardWidth - 76f, 32f)), member.Status, 15f, true, TextAlignmentOptions.MidlineLeft).color = statusColor;

            // Orders: two rows of two buttons
            float bw = (cardWidth - 48f - 12f) / 2f, by = cardHeight - 24f - 56f * 2f - 12f;
            bool free = !member.IsBusy;
            Order(card, new Vector2(24f, by), bw, "Follow me", free && member.Order != CrewOrder.Follow,
                  () => { member.Follow(PlayerTransform()); Close(); });
            Order(card, new Vector2(36f + bw, by), bw, "Go to a point", free, () => StartPicking(member));
            Order(card, new Vector2(24f, by + 68f), bw, "Wait here", free && !member.InHub && member.Order != CrewOrder.Waiting,
                  () => { member.Wait(); Refresh(); });
            Order(card, new Vector2(36f + bw, by + 68f), bw, "Back to the hub", free && !member.InHub && member.Order != CrewOrder.ReturnToHub,
                  () => { member.ReturnToHub(); Refresh(); });
        }
    }

    static void Order(RectTransform card, Vector2 position, float width, string label, bool enabled, UnityEngine.Events.UnityAction onClick)
    {
        Button button = LabelButton(card, position, new Vector2(width, 56f), UITheme.Card, UITheme.Text, label, 17f, onClick);
        button.interactable = enabled;
    }

    static Transform PlayerTransform()
    {
        var movement = FindFirstObjectByType<PlayerMovement>();
        return movement != null ? movement.transform : null;
    }

    static Sprite Portrait(CrewMember member) => member.Character != null ? member.Character.icon : null;

    static Color StatusColor(CrewMember member)
    {
        if (member.IsBusy) return UITheme.Bad;
        if (member.Order == CrewOrder.InHub || member.Order == CrewOrder.Waiting) return UITheme.Good;
        return UITheme.Accent;
    }
}
