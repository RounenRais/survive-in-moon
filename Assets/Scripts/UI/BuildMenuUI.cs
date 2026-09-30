using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The build terminal's screen, picture first. Opened with F at the terminal (BuildMenuUI.Open()); built from
// code the first time, with the shared look in UITheme.
//   left:  a grid of picture cards, one per buildable (BuildManager.catalog)
//   right: a big picture of the chosen one, its materials as icon chips (have / need), the build time,
//          and the crew as photo cards: click one to give them the job
//   Place: closes the screen and starts placing it at the base (BuildManager)
// Esc or the x closes it. Every text has a fixed box: long texts end with "..." instead of spilling out.
public class BuildMenuUI : UIWindow
{
    const float Width = 1280f, Height = 860f;
    const float Pad = 28f;
    const float GridWidth = 440f;
    const float DetailWidth = Width - GridWidth - 32f * 2f - 20f;
    const float Inner = DetailWidth - Pad * 2f; // Usable width inside the details panel

    private static BuildMenuUI instance;
    public static bool IsOpen => instance != null && instance.window.activeSelf;

    public static void Open()
    {
        if (instance == null) instance = Create();
        instance.Show();
    }

    public static void Close()
    {
        if (instance != null) instance.window.SetActive(false);
    }

    private GameObject window;
    private RectTransform grid, costs, builders;
    private Image detailPicture;
    private TMP_Text detailName, detailDescription, detailTime, placeHint;
    private Button placeButton;
    private BuildableDefinition selected;
    private CrewMember selectedBuilder;
    private float nextRefresh;
    private string buildersShown; // What the crew cards show now; rebuilt only when it changes
    private readonly List<Tile> tiles = new List<Tile>();

    private class Tile
    {
        public BuildableDefinition buildable;
        public Image background, frame;
        public readonly List<(ItemAmount part, TMP_Text count)> parts = new List<(ItemAmount, TMP_Text)>();
    }

    // --------------------------------------------------------------- Open / close / refresh

    void Show()
    {
        window.SetActive(true);
        buildersShown = null;
        BuildGrid();
        if (selected == null && tiles.Count > 0) selected = tiles[0].buildable;
        if (selectedBuilder == null || selectedBuilder.IsBusy) selectedBuilder = FirstFreeBuilder();
        Refresh();
    }

    void Update()
    {
        if (!IsOpen) return;
        if (Input.GetKeyDown(KeyCode.Escape)) { Close(); return; }
        if (Time.unscaledTime >= nextRefresh) Refresh(); // Inventory and crew change while the screen is open
    }

    void Refresh()
    {
        nextRefresh = Time.unscaledTime + 0.5f;
        Inventory inventory = BuildManager.Instance != null ? BuildManager.Instance.PlayerInventory : null;

        foreach (Tile tile in tiles)
        {
            bool isSelected = tile.buildable == selected;
            tile.background.color = isSelected ? UITheme.CardSelected : UITheme.Card;
            tile.frame.enabled = isSelected;
            foreach (var (part, count) in tile.parts)
            {
                bool enough = inventory != null && inventory.HasItem(part.item, part.quantity);
                count.color = enough ? UITheme.TextDim : UITheme.Bad;
            }
        }

        Clear(costs);
        if (selected == null)
        {
            detailName.text = "Nothing to build";
            detailDescription.text = detailTime.text = placeHint.text = "";
            detailPicture.enabled = false;
            placeButton.interactable = false;
            return;
        }

        detailPicture.sprite = selected.icon;
        detailPicture.enabled = selected.icon != null;
        detailName.text = selected.displayName;
        detailDescription.text = selected.description;
        int seconds = Mathf.RoundToInt(selected.buildTime);
        detailTime.text = $"{seconds / 60}:{seconds % 60:00}";

        // Material chips: item picture, have / need
        if (selected.IsFree)
        {
            RectTransform chip = Rect("Free", costs, Vector2.zero, new Vector2(Inner, 64f));
            Rounded(chip, UITheme.Card, 14);
            Text(Rect("Label", chip, new Vector2(20f, 0f), new Vector2(Inner - 40f, 64f)), "Free to build, no materials needed",
                 19f, true, TextAlignmentOptions.MidlineLeft).color = UITheme.Good;
        }
        const float chipWidth = 158f, chipGap = 12f;
        int index = 0;
        foreach (ItemAmount part in selected.cost)
        {
            if (part.item == null) continue;
            RectTransform chip = Rect(part.item.displayName, costs, new Vector2(index++ * (chipWidth + chipGap), 0f), new Vector2(chipWidth, 64f));
            Rounded(chip, UITheme.Card, 14);
            RectTransform well = Rect("Well", chip, new Vector2(8f, 8f), new Vector2(48f, 48f));
            Rounded(well, UITheme.Well, 10);
            Picture(Rect("Icon", well, new Vector2(4f, 4f), new Vector2(40f, 40f)), part.item.icon);
            int have = inventory != null ? inventory.CountOf(part.item) : 0;
            TMP_Text amount = Text(Rect("Amount", chip, new Vector2(66f, 6f), new Vector2(chipWidth - 76f, 30f)),
                                   $"{have} / {part.quantity}", 21f, true, TextAlignmentOptions.MidlineLeft);
            amount.color = have >= part.quantity ? UITheme.Good : UITheme.Bad;
            Text(Rect("Name", chip, new Vector2(66f, 34f), new Vector2(chipWidth - 76f, 22f)), part.item.displayName,
                 13f, false, TextAlignmentOptions.MidlineLeft).color = UITheme.TextDim;
        }

        RefreshBuilders();

        // Can we place it?
        string problem = null;
        if (!selected.CanAfford(inventory)) problem = "Not enough materials";
        else if (selectedBuilder == null) problem = "Choose who builds it";
        else if (selectedBuilder.IsBusy) problem = $"{selectedBuilder.displayName} is busy";
        placeButton.interactable = problem == null;
        placeHint.text = problem ?? (selectedBuilder.isPlayer ? "You will build it yourself" : $"{selectedBuilder.displayName} will build it");
        placeHint.color = problem == null ? UITheme.TextDim : UITheme.Bad;
    }

    // One photo card per crew member. Rebuilt only when something on them changed: a button that is destroyed
    // under the mouse would lose the click.
    void RefreshBuilders()
    {
        IReadOnlyList<CrewMember> crew = CrewManager.Instance != null ? CrewManager.Instance.Members : null;
        string state = selectedBuilder != null ? selectedBuilder.displayName : "";
        if (crew != null)
            foreach (CrewMember member in crew) state += "|" + member.displayName + member.Status;
        if (state == buildersShown) return;
        buildersShown = state;

        Clear(builders);
        if (crew == null || crew.Count == 0)
        {
            Text(Rect("None", builders, Vector2.zero, new Vector2(400f, 40f)), "No crew", 19f, false, TextAlignmentOptions.MidlineLeft).color = UITheme.Bad;
            return;
        }
        const float gap = 16f, cardHeight = 236f;
        float cardWidth = (Inner - gap * (crew.Count - 1)) / crew.Count;
        for (int i = 0; i < crew.Count; i++)
        {
            CrewMember member = crew[i];
            bool isSelected = member == selectedBuilder;
            Button card = Button(builders, new Vector2(i * (cardWidth + gap), 0f), new Vector2(cardWidth, cardHeight),
                                 isSelected ? UITheme.CardSelected : UITheme.Card, () => { selectedBuilder = member; Refresh(); });
            card.interactable = !member.IsBusy;

            // The photo on a soft backdrop; the selected one gets an accent frame
            RectTransform photoArea = Rect("Photo", card.transform, new Vector2(8f, 8f), new Vector2(cardWidth - 16f, 150f));
            Rounded(photoArea, isSelected ? UITheme.AccentSoft : UITheme.Well, 12);
            Sprite portrait = member.Character != null ? member.Character.icon : null;
            if (portrait != null) Picture(Rect("Portrait", photoArea, new Vector2(4f, 4f), new Vector2(cardWidth - 24f, 146f)), portrait);
            else Text(Rect("Initial", photoArea, Vector2.zero, new Vector2(cardWidth - 16f, 150f)), member.displayName.Substring(0, 1),
                      60f, true, TextAlignmentOptions.Center).color = UITheme.TextDim;
            if (member.isPlayer)
            {
                RectTransform badge = Rect("You", photoArea, new Vector2(8f, 8f), new Vector2(48f, 24f));
                Rounded(badge, UITheme.Accent, 12);
                Text(Rect("Label", badge, Vector2.zero, new Vector2(48f, 24f)), "YOU", 12f, true, TextAlignmentOptions.Center).color = UITheme.OnAccent;
            }

            string title = member.isPlayer ? (member.Character != null ? member.Character.displayName : "You") : member.displayName;
            float w = cardWidth - 28f;
            Text(Rect("Name", card.transform, new Vector2(14f, 166f), new Vector2(w, 28f)), title, 18f, true, TextAlignmentOptions.MidlineLeft);
            RectTransform pill = Rect("Status", card.transform, new Vector2(14f, 198f), new Vector2(w, 26f));
            Color statusColor = member.IsBusy ? UITheme.Bad : UITheme.Good;
            Rounded(pill, new Color(statusColor.r, statusColor.g, statusColor.b, 0.14f), 13);
            Text(Rect("Label", pill, new Vector2(10f, 0f), new Vector2(w - 20f, 26f)), member.Status, 13f, true, TextAlignmentOptions.MidlineLeft).color = statusColor;
            if (isSelected) Frame(card.GetComponent<RectTransform>(), cardWidth, cardHeight);
        }
    }

    void BuildGrid()
    {
        Clear(grid);
        tiles.Clear();
        if (BuildManager.Instance == null) return;
        const float gap = 12f, tileHeight = 176f;
        float tileWidth = (GridWidth - Pad * 2f + 16f - gap) / 2f;
        foreach (BuildableDefinition buildable in BuildManager.Instance.catalog)
        {
            if (buildable == null) continue;
            int column = tiles.Count % 2, row = tiles.Count / 2;
            Button button = Button(grid, new Vector2(column * (tileWidth + gap), row * (tileHeight + gap)), new Vector2(tileWidth, tileHeight),
                                   UITheme.Card, () => { selected = buildable; Refresh(); });
            var tile = new Tile { buildable = buildable, background = button.GetComponent<Image>() };

            RectTransform well = Rect("Picture", button.transform, new Vector2(8f, 8f), new Vector2(tileWidth - 16f, 110f));
            Rounded(well, UITheme.Well, 12);
            Picture(Rect("Icon", well, new Vector2(8f, 6f), new Vector2(tileWidth - 32f, 98f)), buildable.icon);
            if (buildable.IsFree)
            {
                RectTransform badge = Rect("Free", well, new Vector2(tileWidth - 16f - 58f, 8f), new Vector2(50f, 22f));
                Rounded(badge, UITheme.Good, 11);
                Text(Rect("Label", badge, Vector2.zero, new Vector2(50f, 22f)), "FREE", 11f, true, TextAlignmentOptions.Center).color = UITheme.OnAccent;
            }

            Text(Rect("Name", button.transform, new Vector2(12f, 122f), new Vector2(tileWidth - 24f, 26f)), buildable.displayName, 17f, true, TextAlignmentOptions.MidlineLeft);
            // Cost as small item pictures with the amount next to each
            if (buildable.IsFree)
                Text(Rect("Cost", button.transform, new Vector2(12f, 148f), new Vector2(tileWidth - 24f, 22f)), "No materials", 13f, false, TextAlignmentOptions.MidlineLeft)
                    .color = UITheme.Good;
            foreach (ItemAmount part in buildable.cost)
            {
                if (part.item == null) continue;
                float x = 12f + tile.parts.Count * 56f;
                Picture(Rect("Icon", button.transform, new Vector2(x, 149f), new Vector2(20f, 20f)), part.item.icon);
                TMP_Text count = Text(Rect("Count", button.transform, new Vector2(x + 23f, 145f), new Vector2(30f, 28f)), part.quantity.ToString(), 14f, true, TextAlignmentOptions.MidlineLeft);
                tile.parts.Add((part, count));
            }
            tile.frame = Frame(button.GetComponent<RectTransform>(), tileWidth, tileHeight);
            tiles.Add(tile);
        }
    }

    CrewMember FirstFreeBuilder()
    {
        if (CrewManager.Instance == null) return null;
        CrewMember fallback = null;
        foreach (CrewMember member in CrewManager.Instance.Members)
        {
            if (member.IsBusy) continue;
            if (!member.isPlayer) return member; // Prefer a crew mate, so the player can keep playing
            fallback = member;
        }
        return fallback;
    }

    void Place()
    {
        if (selected == null || selectedBuilder == null || BuildManager.Instance == null) return;
        Close();
        BuildManager.Instance.StartPlacement(selected, selectedBuilder);
    }

    // --------------------------------------------------------------- Building the window

    static BuildMenuUI Create()
    {
        GameObject root = CreateCanvas("Build Menu UI", 50);
        var menu = root.AddComponent<BuildMenuUI>();
        menu.BuildWindow(root.transform);
        return menu;
    }

    void BuildWindow(Transform root)
    {
        window = WindowFrame(root, new Vector2(Width, Height), out RectTransform box).gameObject;
        Header(box, Width, "BASE TERMINAL", "Construction", Close);

        // Left: the picture grid
        float top = 110f, columnHeight = Height - top - 32f;
        RectTransform left = Rect("Catalog", box, new Vector2(32f, top), new Vector2(GridWidth, columnHeight));
        Rounded(left, UITheme.Panel, 18);
        Section(left, "STRUCTURES", Pad, 20f);
        grid = Rect("Grid", left, new Vector2(Pad - 8f, 52f), new Vector2(GridWidth - Pad * 2f + 16f, columnHeight - 64f));

        // Right: the chosen building
        RectTransform right = Rect("Details", box, new Vector2(32f + GridWidth + 20f, top), new Vector2(DetailWidth, columnHeight));
        Rounded(right, UITheme.Panel, 18);
        RectTransform stage = Rect("Stage", right, new Vector2(Pad, Pad), new Vector2(200f, 170f));
        Rounded(stage, UITheme.Well, 16);
        detailPicture = Picture(Rect("Picture", stage, new Vector2(10f, 8f), new Vector2(180f, 154f)), null);
        float infoX = Pad + 200f + 24f, infoWidth = DetailWidth - infoX - Pad;
        detailName = Text(Rect("Name", right, new Vector2(infoX, Pad + 4f), new Vector2(infoWidth, 40f)), "", 30f, true, TextAlignmentOptions.MidlineLeft);
        detailDescription = Text(Rect("Description", right, new Vector2(infoX, Pad + 52f), new Vector2(infoWidth, 56f)), "", 17f, false, TextAlignmentOptions.TopLeft);
        detailDescription.color = UITheme.TextDim;
        detailDescription.textWrappingMode = TextWrappingModes.Normal;
        RectTransform timeChip = Rect("Time", right, new Vector2(infoX, Pad + 124f), new Vector2(190f, 40f));
        Rounded(timeChip, UITheme.Card, 20);
        Text(Rect("Label", timeChip, new Vector2(16f, 0f), new Vector2(90f, 40f)), "Build time", 14f, false, TextAlignmentOptions.MidlineLeft).color = UITheme.TextDim;
        detailTime = Text(Rect("Value", timeChip, new Vector2(104f, 0f), new Vector2(72f, 40f)), "", 19f, true, TextAlignmentOptions.MidlineRight);

        Section(right, "MATERIALS", Pad, 222f);
        costs = Rect("Materials", right, new Vector2(Pad, 250f), new Vector2(Inner, 64f));
        Section(right, "ASSIGN TO", Pad, 336f);
        builders = Rect("Crew", right, new Vector2(Pad, 364f), new Vector2(Inner, 236f));

        // Bottom: hint + Place
        float bottom = columnHeight - Pad - 56f;
        Image(Rect("Divider", right, new Vector2(Pad, bottom - 18f), new Vector2(Inner, 1f)), UITheme.Line);
        placeHint = Text(Rect("Hint", right, new Vector2(Pad, bottom), new Vector2(Inner - 250f, 56f)), "", 17f, false, TextAlignmentOptions.MidlineLeft);
        placeButton = LabelButton(right, new Vector2(DetailWidth - Pad - 220f, bottom), new Vector2(220f, 56f), UITheme.Accent, UITheme.OnAccent, "Place", 22f, Place);

        window.SetActive(false);
    }
}
