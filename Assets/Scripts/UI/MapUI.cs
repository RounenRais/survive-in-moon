using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// M opens the map: a top-down picture of the terrain (rendered once), with you, the base and its life support
// ring, the "safe range" ring (how far you can walk out and still get back with the oxygen you have now),
// the ship wreck, the rover and the resource fields.
public class MapUI : UIWindow
{
    const float MapSize = 880f;
    public KeyCode toggleKey = KeyCode.M;

    private static MapUI instance;
    public static bool IsOpen => instance != null && instance.window != null && instance.window.activeSelf;

    private GameObject window;
    private RawImage picture;
    private RectTransform mapRect, player, baseRing, safeRing, ship, rover, goal, mark, homeIcon;
    static readonly Color YouColor = new Color(0.4f, 1f, 0.6f);
    static readonly Color WaypointColor = new Color(0.45f, 0.85f, 1f);
    static readonly Color WreckColor = new Color(0.9f, 0.9f, 0.95f);
    private TMP_Text info;
    private Vector3 worldMin, worldSize;
    private bool rendered;
    private readonly List<RectTransform> fieldMarkers = new List<RectTransform>();

    static readonly (string id, string label, Color color)[] Fields =
    {
        ("moon_rock", "Regolith", new Color(0.78f, 0.78f, 0.76f)),
        ("scrap", "Debris", new Color(0.95f, 0.6f, 0.3f)),
        ("ilmenite", "Ilmenite", new Color(0.55f, 0.5f, 0.75f)),
        ("ice", "Water ice", new Color(0.55f, 0.85f, 1f)),
        ("helium3", "Helium-3", new Color(0.45f, 1f, 0.85f)),
    };

    void Awake() => instance = this;

    void Start()
    {
        RectTransform root = CreateCanvas("Map UI", 60).GetComponent<RectTransform>();
        window = WindowFrame(root, new Vector2(MapSize + 380f, MapSize + 64f), out RectTransform box).gameObject;

        mapRect = Rect("Map", box, new Vector2(32f, 32f), new Vector2(MapSize, MapSize));
        Rounded(mapRect, UITheme.Well, 10);
        picture = Rect("Picture", mapRect, Vector2.zero, new Vector2(MapSize, MapSize)).gameObject.AddComponent<RawImage>();
        picture.raycastTarget = false;

        safeRing = Ring("Safe range", new Color(0.45f, 0.78f, 1f, 0.55f));
        baseRing = Ring("Life support", new Color(UITheme.Accent.r, UITheme.Accent.g, UITheme.Accent.b, 0.9f));
        homeIcon = IconMarker("Base", UITheme.Icon("base"), Color.white, 30f, "Base");
        ship = IconMarker("Ship", UITheme.Icon("ship"), UITheme.Accent, 34f, "Crashed ship");
        rover = IconMarker("Rover", UITheme.Icon("rover"), Color.white, 26f, "Rover");
        goal = IconMarker("Objective", UITheme.Icon("flag"), UITheme.Accent, 30f, null);
        mark = IconMarker("Waypoint", UITheme.Icon("pin"), WaypointColor, 30f, null);
        player = IconMarker("You", UITheme.Icon("arrow"), YouColor, 30f, null);

        // Legend
        float x = 32f + MapSize + 28f, y = 40f;
        Text(Rect("Title", box, new Vector2(x, y), new Vector2(300f, 40f)), "Map", 30f, true, TextAlignmentOptions.MidlineLeft);
        y += 60f;
        Legend(box, x, ref y, "You", UITheme.Icon("arrow"), YouColor);
        Legend(box, x, ref y, "Current objective", UITheme.Icon("flag"), UITheme.Accent);
        Legend(box, x, ref y, "Your mark", UITheme.Icon("pin"), WaypointColor);
        Legend(box, x, ref y, "Base", UITheme.Icon("base"), Color.white);
        Legend(box, x, ref y, "Life support edge", null, UITheme.Accent);
        Legend(box, x, ref y, "Walkable with your air", null, new Color(0.45f, 0.78f, 1f));
        Legend(box, x, ref y, "Wreck to salvage", UITheme.Icon("crate"), WreckColor);
        Legend(box, x, ref y, "Crashed ship", UITheme.Icon("ship"), UITheme.Accent);
        Legend(box, x, ref y, "Rover", UITheme.Icon("rover"), Color.white);
        y += 12f;
        foreach (var f in Fields) Legend(box, x, ref y, f.label, FieldIcon(f.id), Color.white);
        y += 14f;
        info = Text(Rect("Info", box, new Vector2(x, y), new Vector2(310f, 160f)), "", 16f, false, TextAlignmentOptions.TopLeft);
        info.textWrappingMode = TextWrappingModes.Normal;
        info.overflowMode = TextOverflowModes.Overflow;
        info.color = UITheme.TextDim;

        window.SetActive(false);
    }

    void Update()
    {
        bool otherMenu = (InventoryUI.Instance != null && InventoryUI.Instance.IsOpen) || CrewUI.IsOpen || BuildMenuUI.IsOpen || CraftingUI.IsOpen;
        if (Input.GetKeyDown(toggleKey) && !otherMenu) SetOpen(!IsOpen);
        else if (IsOpen && Input.GetKeyDown(KeyCode.Escape)) SetOpen(false);
        if (IsOpen)
        {
            // Left click marks a waypoint (shown on the compass), right click removes it
            if ((Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1)) &&
                RectTransformUtility.ScreenPointToLocalPointInRectangle(mapRect, Input.mousePosition, null, out Vector2 local) &&
                local.x >= 0f && local.x <= MapSize && local.y <= 0f && local.y >= -MapSize)
            {
                if (Input.GetMouseButtonDown(1)) CompassUI.Waypoint = null;
                else
                {
                    var p = new Vector3(worldMin.x + local.x / MapSize * worldSize.x, 0f, worldMin.z + (1f + local.y / MapSize) * worldSize.z);
                    CompassUI.Waypoint = BaseArea.GroundPoint(p);
                }
            }
            Refresh();
        }
    }

    public static void Open(bool open) { if (instance != null) instance.SetOpen(open); }

    void SetOpen(bool open)
    {
        if (open && !rendered) RenderTerrain();
        window.SetActive(open);
        if (open) PlaceFields();
    }

    // One orthographic shot straight down over the whole terrain
    void RenderTerrain()
    {
        rendered = true;
        Terrain terrain = Terrain.activeTerrain;
        if (terrain == null) { worldMin = new Vector3(-1500f, 0f, -1500f); worldSize = new Vector3(3000f, 100f, 3000f); return; }
        worldMin = terrain.transform.position;
        worldSize = terrain.terrainData.size;

        var go = new GameObject("Map Camera");
        Camera cam = go.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = worldSize.z * 0.5f;
        cam.aspect = worldSize.x / worldSize.z;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        cam.nearClipPlane = 1f;
        cam.farClipPlane = worldSize.y + 4000f;
        cam.transform.position = worldMin + new Vector3(worldSize.x * 0.5f, worldSize.y + 2000f, worldSize.z * 0.5f);
        cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        var rt = new RenderTexture(1024, 1024, 24) { name = "Map" };
        cam.targetTexture = rt;
        cam.Render();
        cam.targetTexture = null;
        Destroy(go);
        picture.texture = rt;
    }

    Vector2 ToMap(Vector3 world)
    {
        float u = (world.x - worldMin.x) / worldSize.x, v = (world.z - worldMin.z) / worldSize.z;
        return new Vector2(u * MapSize, -(1f - v) * MapSize);
    }

    float ToMapLength(float world) => world / worldSize.x * MapSize;

    void Place(RectTransform marker, Vector3 world)
    {
        marker.anchoredPosition = ToMap(world);
    }

    void Refresh()
    {
        SurvivalStats stats = SurvivalStats.Player;
        BaseArea area = BaseArea.Main;
        if (stats != null)
        {
            Place(player, stats.transform.position);
            player.localRotation = Quaternion.Euler(0f, 0f, -stats.transform.eulerAngles.y); // Points where you face
        }
        if (area != null)
        {
            Place(baseRing, area.transform.position);
            Place(homeIcon, area.transform.position);
            baseRing.sizeDelta = Vector2.one * ToMapLength(area.radius * 2f);
            float range = SafeRange(stats);
            safeRing.gameObject.SetActive(range > 0f);
            Place(safeRing, area.transform.position);
            safeRing.sizeDelta = Vector2.one * ToMapLength((area.radius + range) * 2f);
        }
        goal.gameObject.SetActive(ObjectiveUI.Target != null);
        if (ObjectiveUI.Target != null) Place(goal, ObjectiveUI.Target.position);
        mark.gameObject.SetActive(CompassUI.Waypoint.HasValue);
        if (CompassUI.Waypoint.HasValue) Place(mark, CompassUI.Waypoint.Value);
        ShipWreck wreck = ShipWreck.Instance;
        ship.gameObject.SetActive(wreck != null);
        if (wreck != null) Place(ship, wreck.transform.position);
        MoonRover r = MoonRover.Active;
        rover.gameObject.SetActive(r != null && r.Driver == null);
        if (r != null) Place(rover, r.transform.position);

        string text = "";
        if (stats != null) text += stats.InLifeSupport ? "You are inside life support.\n" : $"{stats.DistanceFromBase:0} m outside the base.\n";
        if (wreck != null && area != null)
            text += $"Ship: {Vector3.Distance(wreck.transform.position, area.transform.position) - area.radius:0} m from the base edge, {wreck.Progress * 100f:0}% repaired.\n";
        info.text = text;
    }

    // How far beyond the base edge you can walk out and back with the oxygen you have (rough, at walking speed)
    float SafeRange(SurvivalStats stats)
    {
        if (stats == null) return 0f;
        var move = stats.GetComponent<PlayerMovement>();
        float speed = (move != null ? move.speed : 8f) * stats.SpeedMultiplier;
        float oxygen = stats.InLifeSupport ? stats.OxygenCapacity : stats.Oxygen;
        float d = 0f;
        for (int i = 0; i < 400; i++)
        {
            float next = d + 10f;
            // Out and back at average distance next/2
            float drain = stats.oxygenBaseDrain + stats.oxygenDistanceDrain * (next * 0.5f) / 1000f;
            if (2f * next / speed * drain > oxygen * 0.9f) break;
            d = next;
        }
        return d;
    }

    // Average spot of every kind of resource field (clusters close to each other become one marker)
    void PlaceFields()
    {
        foreach (RectTransform m in fieldMarkers) Destroy(m.gameObject);
        fieldMarkers.Clear();
        foreach (SupplyCrate wreck in FindObjectsByType<SupplyCrate>(FindObjectsSortMode.None))
        {
            if (wreck.isStarterCrate) continue;
            RectTransform w = IconMarker("Wreck", UITheme.Icon("crate"), wreck.IsEmpty ? new Color(0.5f, 0.5f, 0.55f) : WreckColor, 26f, wreck.Title);
            w.SetSiblingIndex(safeRing.GetSiblingIndex() + 2);
            Place(w, wreck.transform.position);
            fieldMarkers.Add(w);
        }
        foreach (var f in Fields)
        {
            var groups = new List<(Vector3 sum, int count)>();
            foreach (ResourceNode node in ResourceNode.All)
            {
                if (node.item == null || node.item.id != f.id) continue;
                Vector3 p = node.transform.position;
                int g = groups.FindIndex(x => Vector3.Distance(x.sum / x.count, p) < 120f);
                if (g < 0) groups.Add((p, 1));
                else groups[g] = (groups[g].sum + p, groups[g].count + 1);
            }
            foreach (var g in groups)
            {
                RectTransform m = IconMarker(f.label, FieldIcon(f.id), Color.white, 26f, null, true);
                m.SetSiblingIndex(safeRing.GetSiblingIndex()); // Under the rings and the player
                Place(m, g.sum / g.count);
                fieldMarkers.Add(m);
            }
        }
    }

    RectTransform Marker(string name, Color color, float size, bool square)
    {
        RectTransform m = Rect(name, mapRect, Vector2.zero, new Vector2(size, size));
        m.anchorMin = m.anchorMax = new Vector2(0f, 1f);
        m.pivot = new Vector2(0.5f, 0.5f);
        Rounded(m, color, square ? 3 : Mathf.RoundToInt(size * 0.5f));
        return m;
    }

    // An icon on the map (centered on its spot), with an optional name under it and an optional dark backing disc
    RectTransform IconMarker(string name, Sprite icon, Color color, float size, string label, bool backing = false)
    {
        RectTransform m = Rect(name, mapRect, Vector2.zero, new Vector2(size, size));
        m.anchorMin = m.anchorMax = new Vector2(0f, 1f);
        m.pivot = new Vector2(0.5f, 0.5f);
        if (backing) Rounded(Rect("Back", m, new Vector2(-3f, -3f), new Vector2(size + 6f, size + 6f)), new Color(0f, 0f, 0f, 0.55f), Mathf.RoundToInt(size * 0.5f + 3f));
        Image img = Picture(Rect("Icon", m, Vector2.zero, new Vector2(size, size)), icon);
        img.color = color;
        if (!string.IsNullOrEmpty(label))
        {
            TMP_Text t = Text(Rect("Label", m, new Vector2(-60f, size), new Vector2(size + 120f, 18f)), label, 13f, true, TextAlignmentOptions.Center);
            t.color = color;
            t.outlineWidth = 0.25f;
            t.outlineColor = new Color32(0, 0, 0, 200);
        }
        return m;
    }

    static Sprite FieldIcon(string itemId)
    {
        foreach (ResourceNode n in ResourceNode.All)
            if (n.item != null && n.item.id == itemId) return n.item.icon;
        var item = Resources.Load<ItemDefinition>("Items/" + itemId);
        return item != null ? item.icon : null;
    }

    RectTransform Ring(string name, Color color)
    {
        RectTransform ring = Rect(name, mapRect, Vector2.zero, new Vector2(100f, 100f));
        ring.anchorMin = ring.anchorMax = new Vector2(0f, 1f);
        ring.pivot = new Vector2(0.5f, 0.5f);
        RawImage img = ring.gameObject.AddComponent<RawImage>();
        img.texture = RingTexture();
        img.color = color;
        img.raycastTarget = false;
        return ring;
    }

    void Legend(RectTransform box, float x, ref float y, string label, Sprite icon, Color color)
    {
        if (icon != null) Picture(Rect("Icon", box, new Vector2(x, y), new Vector2(24f, 24f)), icon).color = color;
        else
        {
            // A ring sample
            RawImage ringImg = Rect("Ring", box, new Vector2(x, y), new Vector2(24f, 24f)).gameObject.AddComponent<RawImage>();
            ringImg.texture = RingTexture();
            ringImg.color = color;
        }
        Text(Rect(label, box, new Vector2(x + 34f, y), new Vector2(280f, 24f)), label, 16f, false, TextAlignmentOptions.MidlineLeft);
        y += 30f;
    }

    private static Texture2D ring;
    static Texture2D RingTexture()
    {
        if (ring != null) return ring;
        const int size = 256;
        ring = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(size / 2f, size / 2f));
                float a = Mathf.Clamp01(1.5f - Mathf.Abs(d - (size / 2f - 3f))); // A 3-pixel line at the edge
                ring.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        ring.Apply();
        return ring;
    }
}
