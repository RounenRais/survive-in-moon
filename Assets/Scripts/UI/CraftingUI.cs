using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The workbench window: recipes in four tabs (Materials, Survival, Equipment, Ship parts) on the left,
// the chosen recipe on the right with its ingredients (have / need) and the Craft button.
public class CraftingUI : UIWindow
{
    const float Width = 1180f, Height = 720f, ListWidth = 430f, Pad = 24f;
    const float DetailWidth = Width - 64f - ListWidth - 20f;

    static readonly (string name, ItemCategory[] categories)[] Tabs =
    {
        ("Materials", new[] { ItemCategory.BasicResource, ItemCategory.RareResource, ItemCategory.TechComponent, ItemCategory.Other }),
        ("Survival", new[] { ItemCategory.Consumable }),
        ("Equipment", new[] { ItemCategory.Equipment, ItemCategory.Tool }),
        ("Ship parts", new[] { ItemCategory.ShipPart }),
    };

    private static CraftingUI instance;
    public static bool IsOpen => instance != null && instance.window.activeSelf;

    public static void Open(Inventory inventory)
    {
        if (instance == null)
        {
            GameObject root = CreateCanvas("Crafting UI", 50);
            instance = root.AddComponent<CraftingUI>();
            instance.BuildWindow(root.transform);
        }
        instance.inventory = inventory;
        instance.window.SetActive(true);
        instance.ShowTab(instance.tab);
    }

    public static void Close()
    {
        if (instance != null) instance.window.SetActive(false);
    }

    private GameObject window;
    private Inventory inventory;
    private int tab;
    private RecipeDefinition selected;
    private RectTransform list, ingredients;
    private readonly List<(Image background, TMP_Text label)> tabButtons = new List<(Image, TMP_Text)>();
    private readonly List<(RecipeDefinition recipe, Image background, TMP_Text name)> rows = new List<(RecipeDefinition, Image, TMP_Text)>();
    private Image picture;
    private TMP_Text title, description, owned, message;
    private Button craftButton;
    private float nextRefresh;

    void Update()
    {
        if (!IsOpen) return;
        if (Input.GetKeyDown(KeyCode.Escape)) { Close(); return; }
        if (Time.unscaledTime >= nextRefresh) Refresh();
    }

    void BuildWindow(Transform root)
    {
        window = WindowFrame(root, new Vector2(Width, Height), out RectTransform box).gameObject;
        Header(box, Width, "", "Workbench", Close);

        float top = 96f, columnHeight = Height - top - 32f;
        RectTransform left = Rect("Recipes", box, new Vector2(32f, top), new Vector2(ListWidth, columnHeight));
        Rounded(left, UITheme.Panel, 18);

        // Tabs
        float tabWidth = (ListWidth - Pad * 2f - 3f * 6f) / 4f;
        for (int i = 0; i < Tabs.Length; i++)
        {
            int index = i;
            Button b = Button(left, new Vector2(Pad + i * (tabWidth + 6f), Pad), new Vector2(tabWidth, 38f), UITheme.Card, () => ShowTab(index));
            TMP_Text label = Text(Rect("Label", b.transform, Vector2.zero, new Vector2(tabWidth, 38f)), Tabs[i].name, 15f, true, TextAlignmentOptions.Center);
            tabButtons.Add((b.targetGraphic as Image, label));
        }
        list = Rect("List", left, new Vector2(Pad, Pad + 54f), new Vector2(ListWidth - Pad * 2f, columnHeight - Pad * 2f - 54f));

        // Details
        RectTransform right = Rect("Details", box, new Vector2(32f + ListWidth + 20f, top), new Vector2(DetailWidth, columnHeight));
        Rounded(right, UITheme.Panel, 18);
        RectTransform stage = Rect("Stage", right, new Vector2(Pad, Pad), new Vector2(170f, 170f));
        Rounded(stage, UITheme.Well, 16);
        picture = Picture(Rect("Picture", stage, new Vector2(14f, 14f), new Vector2(142f, 142f)), null);
        float infoX = Pad + 170f + 24f, infoWidth = DetailWidth - infoX - Pad;
        title = Text(Rect("Name", right, new Vector2(infoX, Pad), new Vector2(infoWidth, 40f)), "", 30f, true, TextAlignmentOptions.MidlineLeft);
        description = Text(Rect("Description", right, new Vector2(infoX, Pad + 48f), new Vector2(infoWidth, 110f)), "", 17f, false, TextAlignmentOptions.TopLeft);
        description.color = UITheme.TextDim;
        description.textWrappingMode = TextWrappingModes.Normal;
        description.overflowMode = TextOverflowModes.Overflow;

        Section(right, "Ingredients", Pad, 222f);
        ingredients = Rect("Ingredients", right, new Vector2(Pad, 252f), new Vector2(DetailWidth - Pad * 2f, 150f));

        float bottom = columnHeight - Pad - 56f;
        owned = Text(Rect("Owned", right, new Vector2(Pad, bottom), new Vector2(260f, 56f)), "", 17f, false, TextAlignmentOptions.MidlineLeft);
        owned.color = UITheme.TextDim;
        message = Text(Rect("Message", right, new Vector2(Pad + 200f, bottom), new Vector2(DetailWidth - Pad * 2f - 200f - 240f, 56f)), "", 16f, false, TextAlignmentOptions.MidlineRight);
        craftButton = LabelButton(right, new Vector2(DetailWidth - Pad - 220f, bottom), new Vector2(220f, 56f), UITheme.Accent, UITheme.OnAccent, "Craft", 22f, Craft);

        window.SetActive(false);
    }

    void ShowTab(int index)
    {
        tab = index;
        for (int i = 0; i < tabButtons.Count; i++)
        {
            tabButtons[i].background.color = i == tab ? UITheme.CardSelected : UITheme.Card;
            tabButtons[i].label.color = i == tab ? UITheme.Accent : UITheme.TextDim;
        }

        Clear(list);
        rows.Clear();
        RecipeDatabase db = RecipeDatabase.Instance;
        float y = 0f;
        if (db != null)
            foreach (RecipeDefinition recipe in db.recipes)
            {
                ItemDefinition output = recipe != null ? recipe.outputItem.item : null;
                if (output == null || System.Array.IndexOf(Tabs[tab].categories, output.category) < 0) continue;
                RecipeDefinition r = recipe;
                Button row = Button(list, new Vector2(0f, y), new Vector2(ListWidth - Pad * 2f, 56f), UITheme.Card, () => Select(r));
                RectTransform well = Rect("Well", row.transform, new Vector2(6f, 6f), new Vector2(44f, 44f));
                Rounded(well, UITheme.Well, 10);
                Picture(Rect("Icon", well, new Vector2(4f, 4f), new Vector2(36f, 36f)), output.icon);
                string amount = recipe.outputItem.quantity > 1 ? $"  x{recipe.outputItem.quantity}" : "";
                TMP_Text name = Text(Rect("Name", row.transform, new Vector2(62f, 0f), new Vector2(ListWidth - Pad * 2f - 72f, 56f)),
                                     output.displayName + amount, 17f, true, TextAlignmentOptions.MidlineLeft);
                rows.Add((recipe, row.targetGraphic as Image, name));
                y += 62f;
            }

        if (selected == null || rows.FindIndex(r => r.recipe == selected) < 0)
            selected = rows.Count > 0 ? rows[0].recipe : null;
        message.text = "";
        Refresh();
    }

    void Select(RecipeDefinition recipe)
    {
        selected = recipe;
        message.text = "";
        Refresh();
    }

    void Refresh()
    {
        nextRefresh = Time.unscaledTime + 0.4f;
        foreach (var row in rows)
        {
            bool can = inventory != null && row.recipe.HasInputs(inventory);
            row.background.color = row.recipe == selected ? UITheme.CardSelected : UITheme.Card;
            row.name.color = can ? UITheme.Text : UITheme.TextDim;
        }

        Clear(ingredients);
        if (selected == null)
        {
            title.text = "Nothing here yet";
            description.text = owned.text = "";
            picture.enabled = false;
            craftButton.interactable = false;
            return;
        }

        ItemDefinition output = selected.outputItem.item;
        picture.sprite = output.icon;
        picture.enabled = output.icon != null;
        title.text = output.displayName;
        description.text = output.description;
        owned.text = inventory != null ? $"In your bag: {inventory.CountOf(output)}" : "";

        const float chipWidth = 160f, chipGap = 12f;
        int index = 0;
        foreach (ItemAmount input in selected.inputItems)
        {
            if (input.item == null) continue;
            int column = index % 3, line = index / 3;
            index++;
            RectTransform chip = Rect(input.item.displayName, ingredients, new Vector2(column * (chipWidth + chipGap), line * 72f), new Vector2(chipWidth, 64f));
            Rounded(chip, UITheme.Card, 14);
            RectTransform well = Rect("Well", chip, new Vector2(8f, 8f), new Vector2(48f, 48f));
            Rounded(well, UITheme.Well, 10);
            Picture(Rect("Icon", well, new Vector2(4f, 4f), new Vector2(40f, 40f)), input.item.icon);
            int have = inventory != null ? inventory.CountOf(input.item) : 0;
            TMP_Text amount = Text(Rect("Amount", chip, new Vector2(66f, 6f), new Vector2(chipWidth - 76f, 30f)),
                                   $"{have} / {input.quantity}", 20f, true, TextAlignmentOptions.MidlineLeft);
            amount.color = have >= input.quantity ? UITheme.Good : UITheme.Bad;
            Text(Rect("Name", chip, new Vector2(66f, 34f), new Vector2(chipWidth - 76f, 22f)), input.item.displayName,
                 13f, false, TextAlignmentOptions.MidlineLeft).color = UITheme.TextDim;
        }
        craftButton.interactable = inventory != null && selected.HasInputs(inventory);
    }

    void Craft()
    {
        if (selected == null || inventory == null) return;
        ItemDefinition output = selected.outputItem.item;
        if (!selected.HasInputs(inventory)) { Say("Not enough materials", UITheme.Bad); return; }
        if (inventory.SpaceFor(output) < selected.outputItem.quantity) { Say("Inventory full", UITheme.Bad); return; }
        foreach (ItemAmount input in selected.inputItems) inventory.RemoveItem(input.item, input.quantity);
        inventory.TryAddItem(output, selected.outputItem.quantity);
        Say($"Made {output.displayName}" + (selected.outputItem.quantity > 1 ? $" x{selected.outputItem.quantity}" : ""), UITheme.Good);
        ShowTab(tab); // Names turn grey/white as ingredients run out
    }

    void Say(string text, Color color)
    {
        message.text = text;
        message.color = color;
        Refresh();
    }
}
