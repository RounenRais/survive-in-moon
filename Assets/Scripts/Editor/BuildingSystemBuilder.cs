using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Editor-only tool. Menu: Survive In Moon > Setup Building System
//  1. Builds the building models (rounded boxes and lathe shapes from BuildingMeshes, in a calm NASA-like
//     palette: off-white hull, grey, gunmetal, gold foil; orange only for small markings) and saves them as
//     prefabs in Assets/Prefabs/Buildings
//  2. Creates a BuildableDefinition for each (cost, build time, icon) in Assets/Buildables
//  3. Puts the base into the open scene: "Moon Base" with the Base Hub in the middle, the round base area
//     around it (BaseArea), BuildManager, CrewManager with spawn points and the Build Terminal.
//     An old Moon Base is replaced.
// Safe to run again: models and icons are rebuilt, costs/times you changed in the assets are kept.
// Sizes are in scene units: the astronaut is about 16 units tall.
public static class BuildingSystemBuilder
{
    const string PrefabFolder = "Assets/Prefabs/Buildings";
    const string MaterialFolder = "Assets/Materials/Buildings";
    const string BuildableFolder = "Assets/Buildables";
    const string IconFolder = "Assets/Buildables/Icons";
    const string ItemFolder = "Assets/Items";
    const string BaseName = "Moon Base";

    const float BaseRadius = 300f;    // ~19 astronauts from the hub to the edge
    const float BaseDistance = 200f;  // From the player to the base center: the player starts inside the base
    const float TerminalDistance = 45f; // From the player toward the hub
    const float HubDoorDistance = 47f;  // From the hub center: just past the front module's hatch (hub is scaled 1.6)

    static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();

    [MenuItem("Survive In Moon/Setup Building System")]
    public static void Setup()
    {
        materials.Clear();
        BuildingMeshes.ClearCache();
        EnsureFolder(PrefabFolder);
        EnsureFolder(MaterialFolder);
        EnsureFolder(BuildableFolder);
        EnsureFolder(IconFolder);

        var catalog = new List<BuildableDefinition>
        {
            Buildable("BaseFlag", "Base Flag", "Marks the base. Costs nothing, a good first build.", 30f, BaseFlag()),
            Buildable("StorageCrate", "Storage Crate", "A sealed container for keeping supplies at the base.", 45f, StorageCrate(),
                ("moon_rock", 5)),
            Buildable("SolarPanel", "Solar Panel", "Turns sunlight into power for the base.", 60f, SolarPanel(),
                ("moon_rock", 4), ("circuit", 2)),
            Buildable("HeliumGenerator", "Helium-3 Generator", "A fusion generator that runs on Helium-3.", 75f, HeliumGenerator(),
                ("moon_rock", 4), ("circuit", 2), ("helium3", 3)),
            Buildable("HabitatDome", "Habitat Dome", "A pressurized dome where the crew can live.", 90f, HabitatDome(),
                ("moon_rock", 10), ("circuit", 3), ("helium3", 2)),
        };
        GameObject terminalPrefab = SavePrefab(BuildTerminal(), "BuildTerminal");
        GameObject hubPrefab = SavePrefab(BaseHub(), "BaseHub");
        AssetDatabase.SaveAssets();
        CharacterPortraitRenderer.RenderAll(); // Crew photos for the build screen

        string sceneResult = SetupScene(catalog, terminalPrefab, hubPrefab);
        Debug.Log($"Building system: {catalog.Count} buildables in {BuildableFolder}. {sceneResult}");
    }

    // ------------------------------------------------------------------ Buildable assets

    static BuildableDefinition Buildable(string id, string displayName, string description, float buildTime,
                                         GameObject model, params (string itemId, int amount)[] cost)
    {
        GameObject prefab = SavePrefab(model, id);
        string path = $"{BuildableFolder}/{id}.asset";
        var buildable = AssetDatabase.LoadAssetAtPath<BuildableDefinition>(path);
        if (buildable == null)
        {
            buildable = ScriptableObject.CreateInstance<BuildableDefinition>();
            buildable.id = id;
            buildable.displayName = displayName;
            buildable.buildTime = buildTime;
            foreach (var (itemId, amount) in cost)
            {
                var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>($"{ItemFolder}/{itemId}.asset");
                if (item != null) buildable.cost.Add(new ItemAmount { item = item, quantity = amount });
                else Debug.LogWarning($"Building system: item {itemId} not found for {displayName}");
            }
            AssetDatabase.CreateAsset(buildable, path);
        }
        buildable.description = description;
        buildable.prefab = prefab;
        buildable.icon = ItemIconRenderer.Render(prefab, $"{IconFolder}/{id}.png");
        EditorUtility.SetDirty(buildable);
        return buildable;
    }

    static GameObject SavePrefab(GameObject model, string name)
    {
        model.AddComponent<PlacedBuilding>(); // Takes up ground: nothing can be built on top of it
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(model, $"{PrefabFolder}/{name}.prefab");
        Object.DestroyImmediate(model);
        return prefab;
    }

    // ------------------------------------------------------------------ Scene

    static string SetupScene(List<BuildableDefinition> catalog, GameObject terminalPrefab, GameObject hubPrefab)
    {
        GameObject old = GameObject.Find(BaseName);
        if (old != null) Undo.DestroyObjectImmediate(old);

        var player = Object.FindFirstObjectByType<PlayerMovement>();
        if (player == null) return "No player in the open scene, so the base was not placed.";
        Physics.SyncTransforms();
        Vector3 origin = BaseArea.GroundPoint(player.transform.position);
        Vector3 center = BaseArea.GroundPoint(origin + Vector3.right * BaseDistance);
        Vector3 toPlayer = Vector3.ProjectOnPlane(origin - center, Vector3.up).normalized;
        Quaternion facePlayer = Quaternion.LookRotation(toPlayer);

        var baseObject = new GameObject(BaseName);
        Undo.RegisterCreatedObjectUndo(baseObject, "Create Moon Base");
        baseObject.transform.position = center;
        var area = baseObject.AddComponent<BaseArea>();
        area.radius = BaseRadius;
        var build = baseObject.AddComponent<BuildManager>();
        build.baseArea = area;
        build.catalog = catalog;
        var crew = baseObject.AddComponent<CrewManager>();

        // The hub in the middle, its door (and the modules) turned toward the player
        var hub = (GameObject)PrefabUtility.InstantiatePrefab(hubPrefab, baseObject.transform);
        hub.transform.SetPositionAndRotation(center, facePlayer);

        // The terminal stands a short walk from the player, on the way to the hub, screen toward the player
        Vector3 terminalSpot = origin - toPlayer * TerminalDistance;
        var terminal = (GameObject)PrefabUtility.InstantiatePrefab(terminalPrefab, baseObject.transform);
        terminal.transform.SetPositionAndRotation(BaseArea.GroundPoint(terminalSpot), facePlayer);

        // The crew lives in the hub and comes out here: just past the hatch of the front module, facing out
        var door = new GameObject("Hub Door").transform;
        door.SetParent(baseObject.transform, false);
        door.SetPositionAndRotation(BaseArea.GroundPoint(center + toPlayer * HubDoorDistance), facePlayer);
        crew.hubDoor = door;

        Selection.activeGameObject = baseObject;
        EditorSceneManager.MarkSceneDirty(baseObject.scene);
        return "Moon Base (hub, base area, terminal) placed next to the player. Save the scene (Ctrl+S).";
    }

    // ------------------------------------------------------------------ Models (pivot at the bottom, front = +Z)

    static GameObject BaseFlag()
    {
        var root = new GameObject("BaseFlag");
        Part(root, "Foot", Lathe("Flag_Foot", (0f, 0f), (2.2f, 0f), (2.2f, 0.3f), (2.2f, 0.3f), (1.4f, 0.6f), (0.5f, 0.85f), (0f, 0.9f)), "Gunmetal");
        Part(root, "Pole", Lathe("Flag_Pole", 24, (0f, 0.8f), (0.22f, 0.8f), (0.22f, 0.8f), (0.22f, 23.6f), (0.34f, 23.8f),
                                 (0.44f, 24.1f), (0.34f, 24.4f), (0f, 24.5f)), "HullDark");
        Part(root, "Crossbar", Box("Flag_Crossbar", 7.4f, 0.24f, 0.24f, 0.1f), "Hull", new Vector3(3.7f, 23.2f, 0f));
        Part(root, "Cloth", Box("Flag_Cloth", 7f, 4.4f, 0.08f, 0.035f), "Fabric", new Vector3(3.8f, 20.85f, 0f));
        Part(root, "Canton", Box("Flag_Canton", 2.6f, 2f, 0.1f, 0.04f), "Navy", new Vector3(1.75f, 21.95f, 0f));
        Part(root, "Stripe", Box("Flag_Stripe", 7.02f, 0.26f, 0.1f, 0.04f), "Accent", new Vector3(3.8f, 19.4f, 0f));
        return root;
    }

    static GameObject StorageCrate()
    {
        var root = new GameObject("StorageCrate");
        Part(root, "Skid", Box("Crate_Skid", 12.4f, 0.8f, 9.4f, 0.3f), "Gunmetal", new Vector3(0f, 0.4f, 0f));
        Part(root, "Body", Box("Crate_Body", 12f, 7.4f, 9f, 0.7f), "Hull", new Vector3(0f, 4.4f, 0f));
        Part(root, "Seam", Box("Crate_Seam", 12.08f, 0.22f, 9.08f, 0.1f), "Gunmetal", new Vector3(0f, 6.6f, 0f));
        foreach (float z in new[] { -4.55f, 4.55f })
        foreach (float x in new[] { -3.8f, 0f, 3.8f })
            Part(root, "Rib", Box("Crate_Rib", 0.5f, 5.6f, 0.25f, 0.1f), "HullDark", new Vector3(x, 3.9f, z));
        foreach (float x in new[] { -6.1f, 6.1f })
            Part(root, "Handle", Box("Crate_Handle", 0.4f, 0.4f, 2.6f, 0.15f), "Gunmetal", new Vector3(x, 5.2f, 0f));
        Part(root, "Label", Box("Crate_Label", 2.2f, 1f, 0.1f, 0.05f), "Accent", new Vector3(-1.9f, 5.4f, 4.62f));
        return root;
    }

    static GameObject SolarPanel()
    {
        var root = new GameObject("SolarPanel");
        Part(root, "Foot", Lathe("Solar_Foot", (0f, 0f), (2.4f, 0f), (2.4f, 0.4f), (2.4f, 0.4f), (1.2f, 0.8f), (0.6f, 1f), (0f, 1f)), "Gunmetal");
        Part(root, "Mast", Lathe("Solar_Mast", 24, (0f, 1f), (0.45f, 1f), (0.45f, 1f), (0.45f, 7.2f), (0.45f, 7.2f), (0f, 7.2f)), "HullDark");
        Part(root, "Joint", Box("Solar_Joint", 1.6f, 1.2f, 1.6f, 0.35f), "Gunmetal", new Vector3(0f, 7.5f, 0f));
        Transform panel = Group(root, "Panel", new Vector3(0f, 8.2f, 0f), new Vector3(-25f, 0f, 0f));
        Part(panel, "Frame", Box("Solar_Frame", 18.2f, 0.35f, 10.6f, 0.15f), "HullDark");
        Mesh cell = Box("Solar_Cell", 2.75f, 0.2f, 3.15f, 0.06f);
        for (int x = 0; x < 6; x++)
        for (int z = 0; z < 3; z++)
            Part(panel, "Cell", cell, "Cell", new Vector3(-7.25f + x * 2.9f, 0.14f, -3.3f + z * 3.3f));
        return root;
    }

    static GameObject HeliumGenerator()
    {
        var root = new GameObject("HeliumGenerator");
        Part(root, "Plinth", Lathe("Gen_Plinth", (0f, 0f), (9f, 0f), (9f, 0.9f), (9f, 0.9f), (8.4f, 1.2f), (0f, 1.2f)), "Gunmetal");
        var body = new List<Vector2> { new Vector2(0f, 1.2f), new Vector2(5.2f, 1.2f), new Vector2(5.2f, 1.2f), new Vector2(5.4f, 1.6f), new Vector2(5.4f, 9.2f) };
        body.AddRange(BuildingMeshes.Arc(new Vector2(3.2f, 9.2f), 2.2f, 0f, 90f, 8));
        body.Add(new Vector2(0f, 11.4f));
        Part(root, "Body", BuildingMeshes.Lathe("Gen_Body", body.ToArray()), "Hull");
        Part(root, "Band", Ring("Gen_Band", 5.38f, 5.58f, 3f, 3.6f), "Gunmetal");
        Part(root, "Glow", Ring("Gen_Glow", 5.38f, 5.52f, 6.2f, 6.6f), "Glow");
        Part(root, "Vent", Lathe("Gen_Vent", (0f, 11.2f), (1.8f, 11.2f), (1.8f, 11.2f), (1.8f, 12f), (1.4f, 12.3f), (0f, 12.3f)), "Gunmetal");
        Mesh fin = Box("Gen_Fin", 3f, 6.2f, 0.3f, 0.12f);
        for (int i = 0; i < 6; i++)
        {
            float angle = i * 60f + 30f;
            Vector3 dir = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
            Part(root, "Fin", fin, "HullDark", dir * 6.9f + Vector3.up * 5.2f, new Vector3(0f, angle + 90f, 0f));
        }
        return root;
    }

    static GameObject HabitatDome()
    {
        var root = new GameObject("HabitatDome");
        const float r = 13f, floor = 1.6f;
        Part(root, "Ring", Lathe("Dome_Ring", (0f, 0f), (15f, 0f), (15f, 1.2f), (15f, 1.2f), (14.2f, floor), (0f, floor)), "Gunmetal");
        var dome = new List<Vector2> { new Vector2(r, floor) };
        dome.AddRange(BuildingMeshes.Arc(new Vector2(0f, floor), r, 0f, 90f, 24));
        Part(root, "Dome", BuildingMeshes.Lathe("Dome_Shell", dome.ToArray(), 64), "Hull");
        foreach (float h in new[] { 5.5f, 9.5f })
        {
            float ringRadius = Mathf.Sqrt(r * r - (h - floor) * (h - floor));
            Part(root, "Rib", Ring($"Dome_Rib_{h}", ringRadius - 0.1f, ringRadius + 0.12f, h - 0.15f, h + 0.15f, 64), "HullDark");
        }
        // Windows on the dome, and the airlock facing front
        Mesh window = Box("Dome_Window", 2.6f, 1.3f, 0.3f, 0.12f);
        foreach (float angle in new[] { 45f, 135f, 225f, 315f })
        {
            Vector3 outward = Quaternion.Euler(0f, angle, 0f) * Quaternion.Euler(-20f, 0f, 0f) * Vector3.forward;
            Part(root, "Window", window, "Glass", new Vector3(0f, floor, 0f) + outward * (r - 0.02f), Quaternion.LookRotation(outward).eulerAngles);
        }
        Part(root, "Airlock", Box("Dome_Airlock", 6f, 7f, 6f, 1.1f), "Hull", new Vector3(0f, 5.1f, 12.4f));
        Part(root, "Hatch", Disc("Dome_Hatch", 2.1f, 0.35f), "Gunmetal", new Vector3(0f, 5.1f, 15.35f), new Vector3(90f, 0f, 0f));
        Part(root, "Porthole", Disc("Dome_Porthole", 0.7f, 0.2f), "Glass", new Vector3(0f, 5.8f, 15.62f), new Vector3(90f, 0f, 0f));
        Part(root, "Marking", Box("Dome_Marking", 3.4f, 0.3f, 0.1f, 0.05f), "Accent", new Vector3(0f, 8.1f, 15.42f));
        return root;
    }

    // The main building in the middle of the base: a round core module on a landing pad, three modules
    // around it (one pointing to the front), gold foil insulation, a window band and a dish antenna on top
    static GameObject BaseHub()
    {
        var root = new GameObject("BaseHub");
        Part(root, "Pad", Lathe("Hub_Pad", 72, (0f, 0f), (26f, 0f), (26f, 0.6f), (26f, 0.6f), (25.3f, 0.9f), (0f, 0.9f)), "Gunmetal");
        Part(root, "Pad Ring", Ring("Hub_PadRing", 22.4f, 23.2f, 0.9f, 0.96f, 72), "Accent");
        Part(root, "Pad Line", Ring("Hub_PadLine", 17f, 17.3f, 0.9f, 0.95f, 72), "HullDark");

        var core = new List<Vector2> { new Vector2(0f, 0.9f), new Vector2(11f, 0.9f), new Vector2(11f, 0.9f), new Vector2(11.6f, 1.5f), new Vector2(11.6f, 11.8f) };
        core.AddRange(BuildingMeshes.Arc(new Vector2(6.6f, 11.8f), 5f, 0f, 90f, 12));
        core.Add(new Vector2(0f, 16.8f));
        Part(root, "Core", BuildingMeshes.Lathe("Hub_Core", core.ToArray(), 64), "Hull");
        Part(root, "Foil", Ring("Hub_Foil", 11.6f, 11.78f, 2f, 4.4f, 64), "Foil");
        Part(root, "Windows", Ring("Hub_Windows", 11.6f, 11.72f, 8f, 9.4f, 64), "Glass");
        Part(root, "Roof Ring", Ring("Hub_RoofRing", 8.4f, 8.9f, 15.3f, 15.7f, 64), "HullDark");

        // Side modules: a capsule lying down, pointing out of the core
        var module = new List<Vector2> { new Vector2(0f, 0f) };
        module.AddRange(BuildingMeshes.Arc(new Vector2(2.4f, 2.1f), 2.1f, -90f, 0f, 6));
        module.Add(new Vector2(4.5f, 12.6f));
        module.AddRange(BuildingMeshes.Arc(new Vector2(2.4f, 12.6f), 2.1f, 0f, 90f, 6));
        module.Add(new Vector2(0f, 14.7f));
        Mesh moduleMesh = BuildingMeshes.Lathe("Hub_Module", module.ToArray(), 40);
        Mesh band = Ring("Hub_ModuleBand", 4.48f, 4.62f, 11f, 11.5f, 40);
        Mesh hatch = Disc("Hub_ModuleHatch", 1.7f, 0.3f);
        foreach (float angle in new[] { 0f, 120f, 240f })
        {
            Quaternion around = Quaternion.Euler(0f, angle, 0f);
            Vector3 dir = around * Vector3.forward;
            Vector3 euler = (around * Quaternion.Euler(90f, 0f, 0f)).eulerAngles; // Module axis (Y) -> outward
            Vector3 start = dir * 9.5f + Vector3.up * 5.6f;
            Part(root, "Module", moduleMesh, "Hull", start, euler);
            Part(root, "Module Band", band, "Accent", start, euler);
            Part(root, "Module Hatch", hatch, "Gunmetal", start + dir * 14.6f, euler);
            Part(root, "Module Foot", Box("Hub_ModuleFoot", 1.4f, 1.6f, 3f, 0.3f), "Gunmetal", dir * 20f + Vector3.up * 1.5f, around.eulerAngles);
        }

        // Antenna mast with a dish
        Part(root, "Mast", Lathe("Hub_Mast", 16, (0f, 16.5f), (0.35f, 16.5f), (0.35f, 16.5f), (0.35f, 24f), (0.35f, 24f), (0f, 24f)), "HullDark");
        var dish = new List<Vector2> { new Vector2(0f, 0f) };
        for (int i = 1; i <= 8; i++) { float x = i * 3.2f / 8f; dish.Add(new Vector2(x, 0.11f * x * x)); }
        dish.Add(new Vector2(3.3f, 1.25f));
        dish.Add(new Vector2(0f, 0.25f));
        Transform dishGroup = Group(root, "Dish", new Vector3(0f, 24f, 0f), new Vector3(-35f, 30f, 0f));
        Part(dishGroup, "Dish", BuildingMeshes.Lathe("Hub_Dish", dish.ToArray(), 40), "Hull");
        Part(dishGroup, "Feed", Lathe("Hub_Feed", 12, (0f, 0f), (0.12f, 0f), (0.12f, 2.2f), (0.3f, 2.3f), (0f, 2.5f)), "Gunmetal");
        root.transform.localScale = Vector3.one * 1.6f; // Drawn at astronaut size, shown bigger: ~27 tall, pad ~83 wide
        return root;
    }

    static GameObject BuildTerminal()
    {
        var root = new GameObject("BuildTerminal");
        root.AddComponent<BuildTerminal>();
        Part(root, "Foot", Lathe("Terminal_Foot", (0f, 0f), (2.4f, 0f), (2.4f, 0.35f), (2.4f, 0.35f), (1.5f, 0.7f), (0f, 0.7f)), "Gunmetal");
        Part(root, "Column", Box("Terminal_Column", 1.3f, 8.6f, 1f, 0.35f), "HullDark", new Vector3(0f, 4.9f, 0f));
        Transform head = Group(root, "Head", new Vector3(0f, 10.6f, 0f), new Vector3(-18f, 0f, 0f));
        Part(head, "Housing", Box("Terminal_Housing", 7.4f, 4.8f, 0.9f, 0.4f), "Hull");
        Part(head, "Bezel", Box("Terminal_Bezel", 6.7f, 4.1f, 0.2f, 0.12f), "Gunmetal", new Vector3(0f, 0f, 0.42f));
        Part(head, "Screen", Box("Terminal_Screen", 6.2f, 3.6f, 0.1f, 0.05f), "Screen", new Vector3(0f, 0f, 0.5f));
        Part(head, "Status", Box("Terminal_Status", 0.9f, 0.16f, 0.1f, 0.04f), "Glow", new Vector3(2.5f, -2.18f, 0.48f));
        Part(root, "Shelf", Box("Terminal_Shelf", 4.4f, 0.3f, 1.8f, 0.12f), "Gunmetal", new Vector3(0f, 7.9f, 0.9f), new Vector3(12f, 0f, 0f));
        return root;
    }

    // ------------------------------------------------------------------ Shape helpers

    static Mesh Box(string name, float x, float y, float z, float bevel) => BuildingMeshes.RoundedBox(name, new Vector3(x, y, z), bevel);

    static Mesh Lathe(string name, params (float r, float y)[] points) => Lathe(name, 48, points);
    static Mesh Lathe(string name, int segments, params (float r, float y)[] points) =>
        BuildingMeshes.Lathe(name, points.Select(p => new Vector2(p.r, p.y)).ToArray(), segments);

    // A flat band around the Y axis (a closed ring with a rectangular cross-section)
    static Mesh Ring(string name, float inner, float outer, float y0, float y1, int segments = 48) =>
        BuildingMeshes.Lathe(name, new[]
        {
            new Vector2(inner, y0), new Vector2(outer, y0), new Vector2(outer, y0), new Vector2(outer, y1),
            new Vector2(outer, y1), new Vector2(inner, y1), new Vector2(inner, y1), new Vector2(inner, y0)
        }, segments);

    // A round plate lying on its back (turn it with 90 degrees on X to face forward)
    static Mesh Disc(string name, float radius, float thickness) =>
        BuildingMeshes.Lathe(name, new[]
        {
            new Vector2(0f, 0f), new Vector2(radius, 0f), new Vector2(radius, 0f), new Vector2(radius, thickness * 0.6f),
            new Vector2(radius * 0.9f, thickness), new Vector2(0f, thickness)
        }, 32);

    static Transform Group(GameObject parent, string name, Vector3 position, Vector3 euler) => Group(parent.transform, name, position, euler);

    static Transform Group(Transform parent, string name, Vector3 position, Vector3 euler)
    {
        var group = new GameObject(name).transform;
        group.SetParent(parent, false);
        group.localPosition = position;
        group.localRotation = Quaternion.Euler(euler);
        return group;
    }

    static void Part(GameObject parent, string name, Mesh mesh, string material, Vector3 position = default, Vector3 euler = default) =>
        Part(parent.transform, name, mesh, material, position, euler);

    static void Part(Transform parent, string name, Mesh mesh, string material, Vector3 position = default, Vector3 euler = default)
    {
        var part = new GameObject(name);
        part.transform.SetParent(parent, false);
        part.transform.localPosition = position;
        part.transform.localRotation = Quaternion.Euler(euler);
        part.AddComponent<MeshFilter>().sharedMesh = mesh;
        part.AddComponent<MeshRenderer>().sharedMaterial = Mat(material);
        part.AddComponent<MeshCollider>().sharedMesh = mesh; // Solid: people bump into it, placement sees it
    }

    // ------------------------------------------------------------------ Materials

    static Material Mat(string name)
    {
        if (materials.TryGetValue(name, out Material cached)) return cached;
        Material m = name switch
        {
            "Hull" => CreateMaterial(name, new Color(0.8f, 0.81f, 0.82f), 0.1f, 0.42f),
            "HullDark" => CreateMaterial(name, new Color(0.5f, 0.52f, 0.55f), 0.35f, 0.45f),
            "Gunmetal" => CreateMaterial(name, new Color(0.19f, 0.2f, 0.22f), 0.6f, 0.45f),
            "Foil" => CreateMaterial(name, new Color(0.7f, 0.55f, 0.28f), 0.9f, 0.55f),
            "Accent" => CreateMaterial(name, new Color(0.76f, 0.42f, 0.2f), 0.1f, 0.35f),
            "Cell" => CreateMaterial(name, new Color(0.06f, 0.09f, 0.17f), 0.4f, 0.85f),
            "Glass" => CreateMaterial(name, new Color(0.05f, 0.06f, 0.08f), 0.3f, 0.95f),
            "Fabric" => CreateMaterial(name, new Color(0.86f, 0.86f, 0.84f), 0f, 0.2f),
            "Navy" => CreateMaterial(name, new Color(0.14f, 0.2f, 0.36f), 0f, 0.25f),
            "Glow" => CreateMaterial(name, new Color(0.35f, 0.72f, 0.88f), 0f, 0.8f, new Color(0.18f, 0.5f, 0.7f)),
            "Screen" => CreateMaterial(name, new Color(0.05f, 0.1f, 0.15f), 0f, 0.9f, new Color(0.1f, 0.3f, 0.45f)),
            _ => CreateMaterial(name, Color.magenta, 0f, 0.5f)
        };
        materials[name] = m;
        return m;
    }

    static Material CreateMaterial(string name, Color color, float metallic, float smoothness, Color emission = default)
    {
        string path = $"{MaterialFolder}/{name}.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(material, path);
        }
        material.color = color;
        material.SetColor("_BaseColor", color);
        material.SetFloat("_Metallic", metallic);
        material.SetFloat("_Smoothness", smoothness);
        if (emission.maxColorComponent > 0f)
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", emission);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        else
        {
            material.DisableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", Color.black);
        }
        EditorUtility.SetDirty(material);
        return material;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
    }
}
