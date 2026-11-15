using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

// Top left: the goal of the game and the current step, which doubles as a short hands-on tutorial. Each step
// knows its target in the world (shown on the compass and the map) and finishes itself when it's done:
// open the crate -> build a greenhouse -> gather regolith -> craft a drill -> salvage the cargo pod (rover
// blueprint) -> build the garage -> drive to the crashed ship -> repair it -> launch.
// The first steps also show the controls.
public class ObjectiveUI : UIWindow
{
    class Step
    {
        public string title;
        public Func<string> hint;
        public Func<Transform> target;
        public Func<bool> done;
    }

    public static Transform Target { get; private set; }
    public static string TargetName { get; private set; }

    private readonly List<Step> steps = new List<Step>();
    private int index;
    private RectTransform panel, controls;
    private TMP_Text stepTitle, stepHint;
    private float nextCheck;
    private Inventory inventory;

    void Start()
    {
        RectTransform root = CreateCanvas("Objective UI", 5).GetComponent<RectTransform>();
        panel = Rect("Panel", root, Vector2.zero, new Vector2(400f, 112f));
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0f, 1f);
        panel.anchoredPosition = new Vector2(24f, -24f);
        Rounded(panel, new Color(UITheme.Window.r, UITheme.Window.g, UITheme.Window.b, 0.82f), 14);
        Text(Rect("Goal", panel, new Vector2(16f, 12f), new Vector2(368f, 22f)), "Repair the crashed ship and fly home", 15f, false, TextAlignmentOptions.MidlineLeft).color = UITheme.TextDim;
        stepTitle = Text(Rect("Step", panel, new Vector2(16f, 38f), new Vector2(368f, 28f)), "", 21f, true, TextAlignmentOptions.MidlineLeft);
        stepTitle.color = UITheme.Accent;
        stepHint = Text(Rect("Hint", panel, new Vector2(16f, 70f), new Vector2(368f, 36f)), "", 15f, false, TextAlignmentOptions.TopLeft);
        stepHint.textWrappingMode = TextWrappingModes.Normal;
        stepHint.color = UITheme.Text;

        // Controls, shown for the first steps only
        controls = Rect("Controls", root, Vector2.zero, new Vector2(400f, 150f));
        controls.anchorMin = controls.anchorMax = controls.pivot = new Vector2(0f, 1f);
        controls.anchoredPosition = new Vector2(24f, -148f);
        Rounded(controls, new Color(UITheme.Window.r, UITheme.Window.g, UITheme.Window.b, 0.7f), 14);
        string[,] keys = { { "WASD", "Move" }, { "Shift", "Run" }, { "F", "Use / pick up" }, { "Tab", "Inventory · right click eats" }, { "M", "Map · click to mark" }, { "Right mouse", "Look around" } };
        for (int i = 0; i < keys.GetLength(0); i++)
        {
            float y = 12f + i * 21f;
            Text(Rect("Key", controls, new Vector2(16f, y), new Vector2(110f, 20f)), keys[i, 0], 14f, true, TextAlignmentOptions.MidlineLeft).color = UITheme.Accent;
            Text(Rect("Does", controls, new Vector2(126f, y), new Vector2(260f, 20f)), keys[i, 1], 14f, false, TextAlignmentOptions.MidlineLeft);
        }

        BuildSteps();
        Show();
    }

    void BuildSteps()
    {
        steps.Add(new Step { title = "Open the supply crate", hint = () => "It's next to the workbench. Walk up and press F.",
                             target = () => Find<SupplyCrate>(c => c.isStarterCrate), done = () => Progress.CrateOpened });
        steps.Add(new Step { title = "Build a greenhouse", hint = () => "The crate had the materials. Use the build terminal (F), pick Greenhouse, place it.",
                             target = () => Find<BuildTerminal>(null), done = () => Exists<Greenhouse>() || Placing("Greenhouse") });
        steps.Add(new Step { title = "Gather 3 regolith rocks", hint = () => $"Grey rock piles around the base. {Count("moon_rock")}/3",
                             target = NearestNode("moon_rock"), done = () => Count("moon_rock") >= 3 || Count("drill") > 0 });
        steps.Add(new Step { title = "Craft a drill", hint = () => "At the workbench: Equipment > Drill. Ilmenite and ice need it.",
                             target = () => Find<CraftingStation>(null), done = () => Count("drill") > 0 });
        steps.Add(new Step { title = "Salvage the cargo pod", hint = () => "A pod from the crash came down nearby. Its blueprint lets you build a rover.",
                             target = () => Find<SupplyCrate>(c => c.unlocksRover), done = () => Progress.RoverUnlocked });
        steps.Add(new Step { title = "Build the rover garage", hint = () => "On the build terminal now. Scrap from wrecks makes plates (workbench).",
                             target = () => Find<BuildTerminal>(null), done = () => Exists<RoverGarage>() });
        steps.Add(new Step { title = "Drive to the crashed ship", hint = () => "Too far to walk - the rover's cabin keeps you breathing. Fuel cells refill it.",
                             target = () => ShipWreck.Instance != null ? ShipWreck.Instance.transform : null,
                             done = () => ShipWreck.Instance != null && SurvivalStats.Player != null &&
                                          Vector3.Distance(ShipWreck.Instance.transform.position, SurvivalStats.Player.transform.position) < 120f });
        steps.Add(new Step { title = "Repair the ship", hint = () => ShipWreck.Instance != null ? $"{ShipWreck.Instance.Current?.name}: {ShipWreck.Instance.StageNeeds()}" : "",
                             target = () => ShipWreck.Instance != null ? ShipWreck.Instance.transform : null,
                             done = () => ShipWreck.Instance != null && ShipWreck.Instance.Repaired });
        steps.Add(new Step { title = "Launch", hint = () => "Press F at the ship. Time to go home.",
                             target = () => ShipWreck.Instance != null ? ShipWreck.Instance.transform : null, done = () => false });
    }

    void Update()
    {
        if (panel == null) return;
        panel.gameObject.SetActive(!InteractionSystem.MenuOpen);
        controls.gameObject.SetActive(index < 2 && !InteractionSystem.MenuOpen);
        if (Time.unscaledTime < nextCheck) return;
        nextCheck = Time.unscaledTime + 0.3f;

        // Skip every step that is already done (e.g. the player did things out of order)
        bool advanced = false;
        while (index < steps.Count - 1 && steps[index].done())
        {
            index++;
            advanced = true;
        }
        if (advanced) GameSystems.Notify("Next: " + steps[index].title);
        Show();
    }

    void Show()
    {
        Step s = steps[index];
        stepTitle.text = s.title;
        stepHint.text = s.hint();
        Target = s.target();
        TargetName = s.title;
    }

    // --------------------------------------------------------------- Helpers

    Inventory Bag
    {
        get
        {
            if (inventory == null && SurvivalStats.Player != null) inventory = SurvivalStats.Player.GetComponent<Inventory>();
            return inventory;
        }
    }

    int Count(string id)
    {
        if (Bag == null) return 0;
        int n = 0;
        foreach (InventorySlot slot in Bag.Slots)
            if (!slot.IsEmpty && slot.item.id == id) n += slot.quantity;
        return n;
    }

    static Transform Find<T>(Func<T, bool> where) where T : Component
    {
        foreach (T c in FindObjectsByType<T>(FindObjectsSortMode.None))
            if (where == null || where(c)) return c.transform;
        return null;
    }

    static bool Exists<T>() where T : Component => FindFirstObjectByType<T>() != null;

    static bool Placing(string buildableId)
    {
        foreach (ConstructionSite site in FindObjectsByType<ConstructionSite>(FindObjectsSortMode.None))
            if (site.Buildable != null && site.Buildable.id == buildableId) return true;
        return false;
    }

    static Func<Transform> NearestNode(string itemId) => () =>
    {
        SurvivalStats p = SurvivalStats.Player;
        Transform best = null;
        float bestD = float.MaxValue;
        foreach (ResourceNode n in ResourceNode.All)
        {
            if (n.IsEmpty || n.item == null || n.item.id != itemId || p == null) continue;
            float d = Vector3.Distance(n.transform.position, p.transform.position);
            if (d < bestD) { bestD = d; best = n.transform; }
        }
        return best;
    };
}
