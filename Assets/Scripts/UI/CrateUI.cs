using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The window of an opened crate: every item inside with its amount and a Take button, plus Take all.
public class CrateUI : UIWindow
{
    const float Width = 560f, RowH = 64f, Pad = 24f;

    private static CrateUI instance;
    public static bool IsOpen => instance != null && instance.window != null && instance.window.activeSelf;

    private GameObject window;
    private RectTransform box, list;
    private TMP_Text title, message;
    private SupplyCrate crate;
    private Inventory inventory;

    public static void Open(SupplyCrate crate, Inventory inventory)
    {
        if (instance == null)
        {
            GameObject root = CreateCanvas("Crate UI", 55);
            instance = root.AddComponent<CrateUI>();
            instance.Build(root.transform);
        }
        instance.crate = crate;
        instance.inventory = inventory;
        instance.message.text = "";
        instance.window.SetActive(true);
        instance.Refresh();
    }

    public static void Close()
    {
        if (instance != null) instance.window.SetActive(false);
    }

    void Build(Transform root)
    {
        window = WindowFrame(root, new Vector2(Width, 560f), out box).gameObject;
        Header(box, Width, "", "", Close);
        title = box.Find("Title").GetComponent<TMP_Text>();
        list = Rect("Items", box, new Vector2(Pad, 92f), new Vector2(Width - Pad * 2f, 380f));
        message = Text(Rect("Message", box, new Vector2(Pad, 560f - 76f), new Vector2(260f, 52f)), "", 16f, false, TextAlignmentOptions.MidlineLeft);
        message.color = UITheme.TextDim;
        LabelButton(box, new Vector2(Width - Pad - 200f, 560f - 80f), new Vector2(200f, 54f), UITheme.Accent, UITheme.OnAccent, "Take all", 20f, TakeAll);
        window.SetActive(false);
    }

    void Update()
    {
        if (IsOpen && Input.GetKeyDown(KeyCode.Escape)) Close();
    }

    void Refresh()
    {
        title.text = crate != null ? crate.Title : "Crate";
        Clear(list);
        if (crate == null) return;
        float y = 0f;
        for (int i = 0; i < crate.contents.Count; i++)
        {
            ItemAmount a = crate.contents[i];
            if (a.item == null || a.quantity <= 0) continue;
            int index = i;
            RectTransform row = Rect(a.item.displayName, list, new Vector2(0f, y), new Vector2(Width - Pad * 2f, RowH - 8f));
            Rounded(row, UITheme.Card, 10);
            RectTransform well = Rect("Well", row, new Vector2(6f, 6f), new Vector2(44f, 44f));
            Rounded(well, UITheme.Well, 8);
            Picture(Rect("Icon", well, new Vector2(3f, 3f), new Vector2(38f, 38f)), a.item.icon);
            Text(Rect("Name", row, new Vector2(62f, 0f), new Vector2(280f, RowH - 8f)), a.item.displayName, 17f, true, TextAlignmentOptions.MidlineLeft);
            Text(Rect("Amount", row, new Vector2(300f, 0f), new Vector2(60f, RowH - 8f)), $"x{a.quantity}", 17f, false, TextAlignmentOptions.MidlineRight).color = UITheme.TextDim;
            LabelButton(row, new Vector2(Width - Pad * 2f - 110f, 8f), new Vector2(100f, 40f), UITheme.Card, UITheme.Text, "Take", 16f, () => Take(index));
            y += RowH;
        }
        if (y == 0f)
            Text(Rect("Empty", list, Vector2.zero, new Vector2(Width - Pad * 2f, 40f)), "Empty", 18f, false, TextAlignmentOptions.MidlineLeft).color = UITheme.TextDim;
    }

    void Take(int index)
    {
        int n = crate.Take(index, inventory);
        message.text = n > 0 ? "" : "Inventory full";
        message.color = UITheme.Bad;
        Refresh();
    }

    void TakeAll()
    {
        bool full = false;
        for (int i = 0; i < crate.contents.Count; i++)
        {
            crate.Take(i, inventory);
            if (crate.contents[i].quantity > 0) full = true;
        }
        Refresh();
        if (full) { message.text = "Inventory full"; message.color = UITheme.Bad; }
        else Close();
    }
}
