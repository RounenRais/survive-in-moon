using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Second half of GameContentBuilder: buildings, the crashed ship, resource fields and the scene setup.
public static partial class GameContentBuilder
{
    class Prefabs
    {
        public GameObject workbench, locker, ship;
        public readonly Dictionary<string, GameObject> nodes = new Dictionary<string, GameObject>();
    }

    // =============================================================== Buildings

    static Prefabs BuildBuildings()
    {
        var p = new Prefabs();

        // Workbench + supply locker stand at the hub from the start
        p.workbench = SaveBuilding(Workbench(), "Workbench", -1f);
        p.locker = SaveBuilding(Locker(), "SupplyLocker", 0f);

        // New buildables
        GameObject greenhouse = SaveBuilding(GreenhouseModel(), "Greenhouse", -5f);
        GameObject garage = SaveBuilding(GarageModel(), "RoverGarage", -2f);
        var newBuildables = new List<BuildableDefinition>
        {
            Buildable("Greenhouse", "Greenhouse", "Grows lettuce and potatoes under regolith glass. Needs power and water from the base stock.",
                      90f, greenhouse, ("glass", 6), ("metal_plate", 4)),
            Buildable("RoverGarage", "Rover Garage", "Builds and houses the lunar rover. Fuel cells keep it running.",
                      120f, garage, ("metal_plate", 6), ("battery", 2), ("circuit", 2)),
        };

        // Power on the existing buildings, and costs that use the new materials
        SetPower("SolarPanel", 6f);
        SetPower("HeliumGenerator", 18f);
        SetPower("HabitatDome", -3f);
        SetPower("BaseHub", 6f);
        SetCost("StorageCrate", ("moon_rock", 4), ("metal_plate", 2));
        SetCost("SolarPanel", ("glass", 2), ("metal_plate", 2), ("circuit", 1));
        SetCost("HeliumGenerator", ("metal_plate", 4), ("circuit", 2), ("helium3", 3));
        SetCost("HabitatDome", ("metal_plate", 6), ("glass", 4), ("circuit", 2));

        BuildManager manager = Object.FindFirstObjectByType<BuildManager>();
        if (manager != null)
        {
            foreach (BuildableDefinition b in newBuildables)
                if (!manager.catalog.Contains(b)) manager.catalog.Add(b);
            EditorUtility.SetDirty(manager);
        }
        return p;
    }

    static GameObject SaveBuilding(GameObject model, string name, float kilowatts)
    {
        if (model.GetComponent<PlacedBuilding>() == null) model.AddComponent<PlacedBuilding>();
        if (kilowatts != 0f) model.AddComponent<PowerNode>().kilowatts = kilowatts;
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(model, $"{BuildingPrefabFolder}/{name}.prefab");
        Object.DestroyImmediate(model);
        return prefab;
    }

    static BuildableDefinition Buildable(string id, string name, string description, float time, GameObject prefab,
                                         params (string item, int n)[] cost)
    {
        string path = $"{BuildableFolder}/{id}.asset";
        var b = AssetDatabase.LoadAssetAtPath<BuildableDefinition>(path);
        if (b == null)
        {
            b = ScriptableObject.CreateInstance<BuildableDefinition>();
            AssetDatabase.CreateAsset(b, path);
        }
        b.id = id;
        b.displayName = name;
        b.description = description;
        b.buildTime = time;
        b.prefab = prefab;
        b.cost = cost.Select(c => new ItemAmount { item = I(c.item), quantity = c.n }).ToList();
        b.icon = ItemIconRenderer.Render(prefab, $"{BuildableIconFolder}/{id}.png");
        EditorUtility.SetDirty(b);
        return b;
    }

    static void SetPower(string prefabName, float kilowatts)
    {
        string path = $"{BuildingPrefabFolder}/{prefabName}.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) return;
        GameObject contents = PrefabUtility.LoadPrefabContents(path);
        PowerNode node = contents.GetComponent<PowerNode>();
        if (node == null) node = contents.AddComponent<PowerNode>();
        node.kilowatts = kilowatts;
        PrefabUtility.SaveAsPrefabAsset(contents, path);
        PrefabUtility.UnloadPrefabContents(contents);
    }

    static void SetCost(string buildableId, params (string item, int n)[] cost)
    {
        var b = AssetDatabase.LoadAssetAtPath<BuildableDefinition>($"{BuildableFolder}/{buildableId}.asset");
        if (b == null) return;
        b.cost = cost.Select(c => new ItemAmount { item = I(c.item), quantity = c.n }).ToList();
        EditorUtility.SetDirty(b);
    }

    // --------------------------------------------------------------- Building models (front = +Z, pivot on the ground)

    static GameObject Workbench()
    {
        var root = new GameObject("Workbench");
        Transform t = root.transform;
        ContentKit.Part(t, "Top", ContentKit.Box(16f, 0.8f, 7f, 0.25f), ContentKit.HullDark, new Vector3(0f, 7.6f, 0f), default, null, true);
        foreach (float x in new[] { -7f, 7f })
            foreach (float z in new[] { -2.8f, 2.8f })
                ContentKit.Part(t, "Leg", ContentKit.Box(0.8f, 7.2f, 0.8f, 0.15f), ContentKit.Gunmetal, new Vector3(x, 3.6f, z), default, null, true);
        ContentKit.Part(t, "Shelf", ContentKit.Box(15f, 0.4f, 6f, 0.15f), ContentKit.Gunmetal, new Vector3(0f, 2.2f, 0f));
        ContentKit.Part(t, "Board", ContentKit.Box(16f, 7f, 0.5f, 0.2f), ContentKit.Hull, new Vector3(0f, 11.5f, -3.3f), default, null, true);
        ContentKit.Part(t, "Screen", ContentKit.Box(5f, 3.2f, 0.2f, 0.1f), ContentKit.Screen, new Vector3(-4.5f, 12f, -3f));
        ContentKit.Part(t, "Stripe", ContentKit.Box(16.05f, 0.5f, 0.55f, 0.1f), ContentKit.Accent, new Vector3(0f, 14.6f, -3.3f));
        // A few tools and parts on the top
        ContentKit.Part(t, "Vise", ContentKit.Box(1.6f, 1.4f, 1.2f, 0.2f), ContentKit.Gunmetal, new Vector3(5.5f, 8.7f, 1f));
        ContentKit.Part(t, "Plate", ContentKit.Box(3f, 0.2f, 2f, 0.05f), ContentKit.Mat("Steel", new Color(0.62f, 0.64f, 0.67f), 0.85f, 0.55f), new Vector3(0.5f, 8.1f, 0.5f), new Vector3(0f, 12f, 0f));
        ContentKit.Part(t, "Canister", ContentKit.Cylinder(0.5f, 1.8f), ContentKit.Hull, new Vector3(-6f, 8f, 1.5f));
        root.AddComponent<CraftingStation>();
        return root;
    }

    static GameObject Locker()
    {
        var root = new GameObject("SupplyLocker");
        Transform t = root.transform;
        ContentKit.Part(t, "Body", ContentKit.Box(8f, 11f, 4.5f, 0.4f), ContentKit.Hull, new Vector3(0f, 5.5f, 0f), default, null, true);
        ContentKit.Part(t, "DoorL", ContentKit.Box(3.7f, 9.6f, 0.2f, 0.1f), ContentKit.HullDark, new Vector3(-1.95f, 5.6f, 2.3f));
        ContentKit.Part(t, "DoorR", ContentKit.Box(3.7f, 9.6f, 0.2f, 0.1f), ContentKit.HullDark, new Vector3(1.95f, 5.6f, 2.3f));
        ContentKit.Part(t, "Band", ContentKit.Box(8.05f, 0.6f, 4.55f, 0.1f), ContentKit.Accent, new Vector3(0f, 9.8f, 0f));
        ContentKit.Part(t, "Panel", ContentKit.Box(1.6f, 1f, 0.15f, 0.05f), ContentKit.Screen, new Vector3(0f, 8.6f, 2.45f));
        root.AddComponent<SupplyLocker>();
        return root;
    }

    static GameObject GreenhouseModel()
    {
        var root = new GameObject("Greenhouse");
        Transform t = root.transform;
        ContentKit.Part(t, "Floor", ContentKit.Box(26f, 1f, 16f, 0.3f), ContentKit.Gunmetal, new Vector3(0f, 0.5f, 0f), default, null, true);
        // Arched glass roof from slanted panes on a frame
        Material glass = ContentKit.Mat("GreenhouseGlass", new Color(0.62f, 0.82f, 0.9f), 0.2f, 0.95f);
        for (int i = 0; i < 6; i++)
        {
            float a = Mathf.Lerp(-70f, 70f, i / 5f);
            Vector3 pos = new Vector3(0f, 1f, 0f) + Quaternion.Euler(0f, 0f, a) * new Vector3(0f, 9f, 0f);
            ContentKit.Part(t, "Pane", ContentKit.Box(5.2f, 0.25f, 15.6f, 0.08f), glass, pos, new Vector3(0f, 0f, a), null, true);
        }
        foreach (float z in new[] { -7.8f, 7.8f })
            for (int i = 0; i <= 6; i++)
            {
                float a = Mathf.Lerp(-84f, 84f, i / 6f);
                Vector3 pos = new Vector3(0f, 1f, z) + Quaternion.Euler(0f, 0f, a) * new Vector3(0f, 9f, 0f);
                ContentKit.Part(t, "Rib", ContentKit.Box(0.5f, 0.5f, 0.5f, 0.1f), ContentKit.Hull, pos);
            }
        // Planter beds with plants (the plants grow taller as the tray fills)
        Material soil = ContentKit.Mat("Soil", new Color(0.3f, 0.22f, 0.15f), 0f, 0.1f);
        Material green = ContentKit.Mat("LettuceGreen", new Color(0.35f, 0.62f, 0.28f), 0f, 0.35f);
        Transform plants = ContentKit.Group(t, "Plants", new Vector3(0f, 2.2f, 0f));
        foreach (float x in new[] { -6f, 0f, 6f })
        {
            ContentKit.Part(t, "Bed", ContentKit.Box(4f, 1.2f, 13f, 0.2f), soil, new Vector3(x, 1.6f, 0f));
            for (int z = -5; z <= 5; z += 2)
                ContentKit.Part(plants, "Plant", ContentKit.Sphere, green, new Vector3(x, 0.8f, z), default, new Vector3(1.6f, 1.6f, 1.6f));
        }
        Material grow = ContentKit.Mat("GrowLight", new Color(0.9f, 0.4f, 0.9f), 0f, 0.8f, new Color(0.6f, 0.2f, 0.7f));
        ContentKit.Part(t, "Lamp", ContentKit.Box(20f, 0.3f, 1f, 0.1f), grow, new Vector3(0f, 8.5f, 0f));
        var gh = root.AddComponent<Greenhouse>();
        gh.fastCrop = I("lettuce");
        gh.slowCrop = I("potato");
        gh.plants = plants;
        return root;
    }

    static GameObject GarageModel()
    {
        var root = new GameObject("RoverGarage");
        Transform t = root.transform;
        ContentKit.Part(t, "Pad", ContentKit.Box(36f, 0.8f, 30f, 0.3f), ContentKit.Gunmetal, new Vector3(0f, 0.4f, 0f), default, null, true);
        foreach (float x in new[] { -17f, 17f })
            ContentKit.Part(t, "Wall", ContentKit.Box(1.5f, 16f, 28f, 0.3f), ContentKit.Hull, new Vector3(x, 8.4f, -1f), default, null, true);
        ContentKit.Part(t, "Back", ContentKit.Box(35f, 16f, 1.5f, 0.3f), ContentKit.Hull, new Vector3(0f, 8.4f, -14.5f), default, null, true);
        ContentKit.Part(t, "Roof", ContentKit.Box(37f, 1.4f, 30f, 0.4f), ContentKit.HullDark, new Vector3(0f, 17f, -1f), default, null, true);
        ContentKit.Part(t, "Frame", ContentKit.Box(36f, 1.6f, 1.6f, 0.3f), ContentKit.Accent, new Vector3(0f, 16f, 13.6f));
        ContentKit.Part(t, "Lamp", ContentKit.Box(10f, 0.4f, 0.8f, 0.1f), ContentKit.Screen, new Vector3(0f, 16.2f, 12.5f));
        root.AddComponent<RoverGarage>().rolloutOffset = new Vector3(0f, 0f, 30f);
        return root;
    }

    // --------------------------------------------------------------- The crashed ship

    static GameObject ShipModel()
    {
        var root = new GameObject("Crashed Ship");
        Transform t = root.transform;
        // Lying on its side a little, nose dug in
        Transform body = ContentKit.Group(t, "Body", new Vector3(0f, 7f, 0f), new Vector3(0f, 0f, 8f));
        ContentKit.Part(body, "Fuselage", ContentKit.Cylinder(7f, 46f, 32), ContentKit.Hull, new Vector3(-23f, 0f, 0f), new Vector3(0f, 0f, -90f), null, true);
        ContentKit.Part(body, "Nose", ContentKit.Sphere, ContentKit.Hull, new Vector3(-23f, 0f, 0f), default, new Vector3(16f, 14f, 14f), true);
        ContentKit.Part(body, "Band1", ContentKit.Cylinder(7.2f, 1.5f, 32), ContentKit.Accent, new Vector3(-8f, 0f, 0f), new Vector3(0f, 0f, -90f));
        ContentKit.Part(body, "Band2", ContentKit.Cylinder(7.2f, 1.5f, 32), ContentKit.Gunmetal, new Vector3(12f, 0f, 0f), new Vector3(0f, 0f, -90f));
        ContentKit.Part(body, "Window", ContentKit.Box(5f, 3f, 0.6f, 0.3f), ContentKit.Screen, new Vector3(-20f, 3.5f, 6.2f), new Vector3(0f, 0f, 0f));
        // A torn hole and a bent wing
        ContentKit.Part(body, "Hole", ContentKit.Box(9f, 6f, 1f, 0.4f), ContentKit.Mat("Scorch", new Color(0.08f, 0.08f, 0.09f), 0.2f, 0.2f), new Vector3(2f, 2f, 6.7f), new Vector3(0f, 0f, 12f));
        ContentKit.Part(body, "Wing", ContentKit.Box(16f, 0.8f, 10f, 0.3f), ContentKit.HullDark, new Vector3(10f, -2f, 11f), new Vector3(20f, -15f, -10f), null, true);
        ContentKit.Part(body, "WingBroken", ContentKit.Box(8f, 0.8f, 9f, 0.3f), ContentKit.HullDark, new Vector3(4f, -6f, -12f), new Vector3(-35f, 25f, 40f), null, true);
        // Engines at the back (dead)
        foreach (float y in new[] { -3f, 3f })
            ContentKit.Part(body, "Engine", ContentKit.Bell(1.5f, 3.4f, 5f), ContentKit.Gunmetal, new Vector3(25f, y, 0f), new Vector3(0f, 0f, 90f), null, true);
        // Debris around
        for (int i = 0; i < 6; i++)
        {
            float a = i * 60f + 15f;
            Vector3 pos = Quaternion.Euler(0f, a, 0f) * new Vector3(0f, 0.5f, 26f + i * 3f);
            ContentKit.Part(t, "Debris", ContentKit.Box(3f + i % 3, 0.5f, 2f + i % 2, 0.1f), i % 2 == 0 ? ContentKit.Hull : ContentKit.HullDark, pos, new Vector3(10f * i, a, 15f));
        }

        // What shows up as the stages get repaired
        var stages = new GameObject[4];
        stages[0] = ContentKit.Group(body, "Repaired Hull").gameObject;
        ContentKit.Part(stages[0].transform, "Patch", ContentKit.Box(10f, 7f, 1.2f, 0.4f), ContentKit.Hull, new Vector3(2f, 2f, 6.8f), new Vector3(0f, 0f, 12f));
        ContentKit.Part(stages[0].transform, "PatchEdge", ContentKit.Box(10.2f, 0.6f, 1.3f, 0.1f), ContentKit.Accent, new Vector3(2f, 5.5f, 6.8f), new Vector3(0f, 0f, 12f));
        stages[1] = ContentKit.Group(body, "Repaired Engines").gameObject;
        Material flame = ContentKit.Mat("EngineGlow", new Color(0.4f, 0.7f, 1f), 0f, 0.8f, new Color(0.6f, 1.2f, 2.4f));
        foreach (float y in new[] { -3f, 3f })
            ContentKit.Part(stages[1].transform, "Glow", ContentKit.Cylinder(1.4f, 0.3f), flame, new Vector3(25.2f, y, 0f), new Vector3(0f, 0f, 90f));
        stages[2] = ContentKit.Group(body, "Repaired Avionics").gameObject;
        ContentKit.Part(stages[2].transform, "Mast", ContentKit.Cylinder(0.3f, 6f, 10), ContentKit.HullDark, new Vector3(-12f, 6.5f, 0f));
        ContentKit.Part(stages[2].transform, "Dish", ContentKit.Sphere, ContentKit.Hull, new Vector3(-12f, 12.5f, 0f), default, new Vector3(4f, 1f, 4f));
        stages[3] = ContentKit.Group(body, "Fuel Tanks").gameObject;
        foreach (float z in new[] { -7.5f, 7.5f })
            ContentKit.Part(stages[3].transform, "Tank", ContentKit.Cylinder(2f, 14f), ContentKit.Mat("FuelYellow", new Color(0.95f, 0.78f, 0.2f), 0.2f, 0.5f),
                            new Vector3(5f, -1f, z), new Vector3(0f, 0f, -90f));

        // A trigger over the whole ship so F works from anywhere around it
        var trigger = root.AddComponent<BoxCollider>();
        trigger.isTrigger = true;
        trigger.center = new Vector3(0f, 8f, 0f);
        trigger.size = new Vector3(66f, 20f, 34f);

        var wreck = root.AddComponent<ShipWreck>();
        wreck.repairedVisuals = stages;
        wreck.stages = new List<ShipWreck.Stage>
        {
            new ShipWreck.Stage { name = "Seal the hull", parts = new List<ItemAmount> { Amount("hull_plate", 4) } },
            new ShipWreck.Stage { name = "Rebuild the engines", parts = new List<ItemAmount> { Amount("thruster", 2) } },
            new ShipWreck.Stage { name = "Restore navigation", parts = new List<ItemAmount> { Amount("nav_computer", 1) } },
            new ShipWreck.Stage { name = "Fuel up", parts = new List<ItemAmount> { Amount("fuel_tank", 2) } },
        };
        return root;
    }

    static ItemAmount Amount(string id, int n) => new ItemAmount { item = I(id), quantity = n };

    // --------------------------------------------------------------- Resource field prefabs

    static GameObject NodePrefab(string id, string label, string itemId, Vector2Int yield, int gathers, string tool,
                                 float respawn, System.Action<Transform> visual)
    {
        var root = new GameObject(id);
        Transform v = ContentKit.Group(root.transform, "Visual");
        visual(v);
        var node = root.AddComponent<ResourceNode>();
        node.displayName = label;
        node.item = I(itemId);
        node.yieldPerGather = yield;
        node.gathers = gathers;
        node.requiredTool = tool != null ? I(tool) : null;
        node.respawnSeconds = respawn;
        node.visual = v.gameObject;
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{ContentPrefabFolder}/{id}.prefab");
        Object.DestroyImmediate(root);
        return prefab;
    }

    static void RockPile(Transform t, Material m, int seed, float size, int count)
    {
        var rng = new System.Random(seed);
        for (int i = 0; i < count; i++)
        {
            float s = size * (0.5f + (float)rng.NextDouble() * 0.6f);
            Vector3 pos = new Vector3(((float)rng.NextDouble() - 0.5f) * size * 2f, s * 0.35f, ((float)rng.NextDouble() - 0.5f) * size * 2f);
            if (i == 0) { pos = new Vector3(0f, size * 0.45f, 0f); s = size; }
            ContentKit.Part(t, "Rock", ContentKit.Rock(seed * 10 + i, 0.25f), m, pos, new Vector3(0f, (float)rng.NextDouble() * 360f, 0f),
                            new Vector3(s, s * 0.7f, s), true);
        }
    }

    static Prefabs BuildNodes(Prefabs p)
    {
        Material ice = ContentKit.Mat("Ice", new Color(0.72f, 0.88f, 1f), 0.1f, 0.92f, new Color(0.05f, 0.12f, 0.18f));
        Material ilm = ContentKit.Mat("Ilmenite", new Color(0.16f, 0.16f, 0.19f), 0.75f, 0.55f);
        Material he = ContentKit.Mat("HeliumSoil", new Color(0.45f, 0.5f, 0.52f), 0f, 0.2f, new Color(0.05f, 0.35f, 0.32f));

        p.nodes["moon_rock"] = NodePrefab("Node_Regolith", "Regolith rocks", "moon_rock", new Vector2Int(2, 4), 4, null, 180f,
            v => RockPile(v, ContentKit.Regolith, 11, 3.2f, 4));
        p.nodes["ilmenite"] = NodePrefab("Node_Ilmenite", "Ilmenite outcrop", "ilmenite", new Vector2Int(2, 3), 5, "drill", 300f,
            v => RockPile(v, ilm, 23, 3.6f, 5));
        p.nodes["ice"] = NodePrefab("Node_Ice", "Ice deposit", "ice", new Vector2Int(2, 3), 5, "drill", 300f, v =>
        {
            RockPile(v, ContentKit.Regolith, 41, 3f, 2);
            for (int i = 0; i < 5; i++)
                ContentKit.Part(v, "Crystal", ContentKit.Box(1.6f, 4f + i % 3, 1.6f, 0.3f), ice,
                                Quaternion.Euler(0f, i * 72f, 0f) * new Vector3(1.8f, 1.6f, 0f), new Vector3(i * 9f - 15f, i * 72f, 14f - i * 6f), null, true);
        });
        p.nodes["helium3"] = NodePrefab("Node_Helium3", "Helium-3 rich soil", "helium3", new Vector2Int(1, 2), 4, null, 360f, v =>
        {
            ContentKit.Part(v, "Patch", ContentKit.Sphere, he, new Vector3(0f, 0.2f, 0f), default, new Vector3(9f, 0.8f, 7f), true);
            RockPile(v, ContentKit.Regolith, 57, 1.6f, 3);
        });
        p.nodes["scrap"] = NodePrefab("Node_Scrap", "Crash debris", "scrap", new Vector2Int(2, 3), 3, null, 240f, v =>
        {
            for (int i = 0; i < 4; i++)
                ContentKit.Part(v, "Panel", ContentKit.Box(4f - i * 0.5f, 0.4f, 2.6f, 0.1f), i % 2 == 0 ? ContentKit.Hull : ContentKit.HullDark,
                                new Vector3(i * 0.8f - 1f, 0.4f + i * 0.4f, i * 0.5f - 0.8f), new Vector3(i * 12f - 10f, i * 40f, i * 18f - 20f), null, true);
            ContentKit.Part(v, "Strip", ContentKit.Box(3f, 0.3f, 0.6f, 0.05f), ContentKit.Accent, new Vector3(0.5f, 1.8f, 0f), new Vector3(0f, 30f, 25f));
        });
        p.nodes["circuit"] = NodePrefab("Node_Electronics", "Electronics debris", "circuit", new Vector2Int(1, 2), 2, null, 420f, v =>
        {
            ContentKit.Part(v, "Rack", ContentKit.Box(3.4f, 2.4f, 2.2f, 0.2f), ContentKit.Gunmetal, new Vector3(0f, 1.1f, 0f), new Vector3(0f, 20f, 14f), null, true);
            ContentKit.Part(v, "Board", ContentKit.Box(2f, 0.15f, 1.4f, 0.04f), ContentKit.Mat("PCB", new Color(0.12f, 0.45f, 0.22f), 0.2f, 0.6f),
                            new Vector3(1.6f, 0.3f, 0.8f), new Vector3(0f, -25f, 8f));
        });
        return p;
    }

    // =============================================================== Scene

    static void BuildWorld(Prefabs p)
    {
        BuildNodes(p);
        GameObject shipModel = ShipModel();
        p.ship = PrefabUtility.SaveAsPrefabAsset(shipModel, $"{ContentPrefabFolder}/CrashedShip.prefab");
        Object.DestroyImmediate(shipModel);

        BaseArea area = Object.FindFirstObjectByType<BaseArea>();
        CrewManager crew = Object.FindFirstObjectByType<CrewManager>();
        Vector3 center = area.transform.position;
        Transform door = crew != null ? crew.HubDoor : area.transform;
        Vector3 outward = Vector3.ProjectOnPlane(door.forward, Vector3.up).normalized;
        Vector3 side = Vector3.Cross(Vector3.up, outward);

        GameObject old = GameObject.Find(WorldRootName);
        if (old != null) Object.DestroyImmediate(old);
        var world = new GameObject(WorldRootName).transform;

        // Workbench and supply locker on either side of the hub door, facing out
        Place(p.workbench, world, door.position + side * 34f + outward * 6f, Quaternion.LookRotation(outward));
        // The build terminal stands at the hub too, on the other side of the door
        Transform terminal = Object.FindFirstObjectByType<BuildTerminal>()?.transform;
        if (terminal != null) terminal.SetPositionAndRotation(Ground(door.position - side * 32f + outward * 6f), Quaternion.LookRotation(outward));

        // The old loose sample items: the ground isn't covered in free materials any more
        GameObject sample = GameObject.Find("Sample Items");
        if (sample != null) Object.DestroyImmediate(sample);
        // Leftovers of older tools: the row of astronauts and the tilted platform with a ladder
        foreach (GameObject go in EditorSceneManagerRoots())
            if (go.name == "NPCShowcase" || go.name == "Platform" || (go.name.StartsWith("Astronaut_") && go.GetComponent<PlayerMovement>() == null))
                Object.DestroyImmediate(go);

        // Starter crate next to the workbench: food, water, air, and exactly what the first greenhouse needs
        // Left of the workbench (seen from the front), with a gap so both are easy to reach
        GameObject crate = Place(CrateModel("Supply Crate", true), world, door.position + side * 60f + outward * 6f, Quaternion.LookRotation(outward));
        Fill(crate, false, ("glass", 6), ("metal_plate", 4), ("circuit", 1), ("water_pack", 3), ("ration", 2), ("o2_canister", 1));

        var rng = new System.Random(1969);
        Transform fields = new GameObject("Resource Fields").transform;
        fields.SetParent(world, false);

        // Wreck sites from the crash. The cargo pod is a short walk out and carries the rover blueprint; the ship
        // itself is deep out, past the walkable range (SurvivalStats.deepZone) - you need the rover for it.
        Vector3 podSpot = FindSpot(center, 430f, 480f, rng, false);
        GameObject pod = Place(CrateModel("Cargo Pod", false), world, podSpot, Quaternion.Euler(0f, 40f, 0f));
        Fill(pod, true, ("battery", 2), ("circuit", 2), ("fuel_cell", 2), ("scrap", 6));
        Debris(p, fields, podSpot, 3, 2, 0);

        Vector3 landerSpot = FindSpot(center, 700f, 780f, rng, false);
        GameObject lander = Place(CrateModel("Lander Wreck", false), world, landerSpot, Quaternion.Euler(0f, 160f, 0f));
        Fill(lander, false, ("o2_canister", 2), ("battery", 2), ("circuit", 2), ("metal_plate", 3));
        Debris(p, fields, landerSpot, 3, 1, 1);

        Vector3 relaySpot = FindSpot(center, 820f, 900f, rng, false);
        GameObject relay = Place(CrateModel("Relay Wreck", false), world, relaySpot, Quaternion.Euler(0f, 250f, 0f));
        Fill(relay, false, ("circuit", 3), ("glass", 2), ("fuel_cell", 1), ("medkit", 1));
        Debris(p, fields, relaySpot, 2, 2, 2);

        // Resource fields: farther from the base = rarer and more valuable
        Scatter(p.nodes["moon_rock"], fields, center, 150f, 450f, 6, rng, false);
        Scatter(p.nodes["scrap"], fields, center, 320f, 620f, 6, rng, false);
        Scatter(p.nodes["circuit"], fields, center, 420f, 680f, 2, rng, false);
        Scatter(p.nodes["ilmenite"], fields, center, 500f, 850f, 6, rng, false);
        Scatter(p.nodes["ice"], fields, center, 750f, 1050f, 5, rng, true);
        Scatter(p.nodes["helium3"], fields, center, 900f, 1250f, 5, rng, false);

        // The crashed ship far out, with salvage around it
        Vector3 shipSpot = FindSpot(center, 1150f, 1280f, rng, false);
        GameObject ship = Place(p.ship, world, shipSpot, Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f));
        Debris(p, fields, shipSpot, 2, 2, 0, 60f);

        // The rover waits until a garage is built
        foreach (MoonRover rover in Object.FindObjectsByType<MoonRover>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            rover.fuelItem = I("fuel_cell");
            EditorUtility.SetDirty(rover);
            rover.gameObject.SetActive(false);
        }

        // Presentation build: every buildable takes 5 seconds
        foreach (string guid in AssetDatabase.FindAssets("t:BuildableDefinition", new[] { BuildableFolder }))
        {
            var b = AssetDatabase.LoadAssetAtPath<BuildableDefinition>(AssetDatabase.GUIDToAssetPath(guid));
            if (b == null) continue;
            b.buildTime = 5f;
            EditorUtility.SetDirty(b);
        }

        // The captain (red suit) is the player's character
        var captain = AssetDatabase.LoadAssetAtPath<CharacterDefinition>("Assets/Characters/Captain.asset");
        if (captain != null)
        {
            foreach (PlayerCharacter pc in Object.FindObjectsByType<PlayerCharacter>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var so = new SerializedObject(pc);
                so.FindProperty("testCharacter").objectReferenceValue = captain;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            var db = AssetDatabase.LoadAssetAtPath<CharacterDatabase>("Assets/Resources/CharacterDatabase.asset");
            if (db != null)
            {
                var so = new SerializedObject(db);
                SerializedProperty def = so.FindProperty("defaultCharacter");
                if (def != null) { def.objectReferenceValue = captain; so.ApplyModifiedPropertiesWithoutUndo(); }
            }
        }

        // Walking speeds on the player in the scene (the scene value wins over the script default)
        foreach (PlayerMovement pm in Object.FindObjectsByType<PlayerMovement>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            var so = new SerializedObject(pm);
            so.FindProperty("speed").floatValue = 8f;
            so.FindProperty("sprintSpeed").floatValue = 40f;
            so.FindProperty("acceleration").floatValue = 12f;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        Debug.Log($"World: ship at {Vector3.Distance(center, shipSpot):0} units from the base center.");
    }

    // A crate / wreck with a SupplyCrate on it (a scene object, not a prefab: each one has its own contents)
    static GameObject CrateModel(string name, bool starter)
    {
        var root = new GameObject(name);
        Transform t = root.transform;
        GameObject lid;
        if (starter)
        {
            ContentKit.Part(t, "Body", ContentKit.Box(9f, 5f, 6f, 0.4f), ContentKit.Hull, new Vector3(0f, 2.5f, 0f), default, null, true);
            ContentKit.Part(t, "Band", ContentKit.Box(9.05f, 0.6f, 6.05f, 0.1f), ContentKit.Accent, new Vector3(0f, 3.4f, 0f));
            Transform hinge = ContentKit.Group(t, "Lid", new Vector3(0f, 5f, -3f));
            ContentKit.Part(hinge, "Top", ContentKit.Box(9.2f, 0.6f, 6.2f, 0.25f), ContentKit.HullDark, new Vector3(0f, 0.3f, 3f));
            lid = hinge.gameObject;
        }
        else
        {
            // Half-buried capsule with a torn panel and an open hatch
            Transform body = ContentKit.Group(t, "Body", new Vector3(0f, 3.5f, 0f), new Vector3(0f, 0f, 18f));
            ContentKit.Part(body, "Hull", ContentKit.Cylinder(4.5f, 14f, 24), ContentKit.Hull, new Vector3(-7f, 0f, 0f), new Vector3(0f, 0f, -90f), null, true);
            ContentKit.Part(body, "Cap", ContentKit.Sphere, ContentKit.HullDark, new Vector3(7f, 0f, 0f), default, new Vector3(5f, 9f, 9f), true);
            ContentKit.Part(body, "Stripe", ContentKit.Cylinder(4.6f, 1.2f, 24), ContentKit.Accent, new Vector3(-2f, 0f, 0f), new Vector3(0f, 0f, -90f));
            ContentKit.Part(t, "Panel", ContentKit.Box(5f, 0.4f, 3f, 0.1f), ContentKit.HullDark, new Vector3(9f, 0.4f, 5f), new Vector3(8f, 30f, 14f), null, true);
            Transform hinge = ContentKit.Group(t, "Hatch", new Vector3(-2f, 7.6f, 0f));
            ContentKit.Part(hinge, "Door", ContentKit.Box(4f, 0.4f, 3.5f, 0.15f), ContentKit.Gunmetal, new Vector3(0f, 0.2f, 1.6f));
            lid = hinge.gameObject;
        }
        var crate = root.AddComponent<SupplyCrate>();
        crate.displayName = name.ToLowerInvariant();
        crate.isStarterCrate = starter;
        crate.lid = lid;
        var trigger = root.AddComponent<BoxCollider>();
        trigger.isTrigger = true;
        trigger.center = new Vector3(0f, 4f, 0f);
        trigger.size = starter ? new Vector3(12f, 8f, 9f) : new Vector3(24f, 12f, 14f);
        return root;
    }

    static void Fill(GameObject crateObject, bool unlocksRover, params (string id, int n)[] items)
    {
        var crate = crateObject.GetComponent<SupplyCrate>();
        crate.unlocksRover = unlocksRover;
        crate.contents = items.Select(i => new ItemAmount { item = I(i.id), quantity = i.n }).ToList();
    }

    // Scrap / electronics / ilmenite piles around a wreck
    static void Debris(Prefabs p, Transform parent, Vector3 around, int scrap, int electronics, int ilmenite, float radius = 34f)
    {
        int i = 0;
        foreach (var (key, n) in new[] { ("scrap", scrap), ("circuit", electronics), ("ilmenite", ilmenite) })
            for (int k = 0; k < n; k++, i++)
            {
                Vector3 spot = around + Quaternion.Euler(0f, i * 67f + 20f, 0f) * Vector3.forward * (radius + (i % 3) * 9f);
                Place(p.nodes[key], parent, spot, Quaternion.Euler(0f, i * 51f, 0f));
            }
    }

    static GameObject[] EditorSceneManagerRoots() =>
        UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();

    static GameObject Place(GameObject prefab, Transform parent, Vector3 position, Quaternion rotation)
    {
        if (!PrefabUtility.IsPartOfPrefabAsset(prefab))
        {
            // A scene object made here (crates, wrecks): just park it
            prefab.transform.SetParent(parent, false);
            prefab.transform.SetPositionAndRotation(Ground(position), rotation);
            return prefab;
        }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        go.transform.SetPositionAndRotation(Ground(position), rotation);
        return go;
    }

    static void Scatter(GameObject prefab, Transform parent, Vector3 center, float minR, float maxR, int count, System.Random rng, bool lowGround)
    {
        for (int i = 0; i < count; i++)
        {
            Vector3 spot = FindSpot(center, minR, maxR, rng, lowGround);
            Place(prefab, parent, spot, Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f));
        }
    }

    // A fairly flat spot in the ring around the center; lowGround picks the lowest of several tries (crater floors)
    static Vector3 FindSpot(Vector3 center, float minR, float maxR, System.Random rng, bool lowGround)
    {
        Terrain terrain = Terrain.activeTerrain;
        Vector3 best = center;
        float bestScore = float.MaxValue;
        int tries = lowGround ? 30 : 12;
        for (int i = 0; i < tries; i++)
        {
            float a = (float)rng.NextDouble() * Mathf.PI * 2f;
            float r = Mathf.Lerp(minR, maxR, (float)rng.NextDouble());
            Vector3 p = Ground(center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r);
            float slope = 0f;
            if (terrain != null)
            {
                Vector3 local = p - terrain.transform.position;
                Vector3 n = terrain.terrainData.GetInterpolatedNormal(local.x / terrain.terrainData.size.x, local.z / terrain.terrainData.size.z);
                slope = 1f - n.y;
            }
            float score = slope * 400f + (lowGround ? p.y : 0f);
            if (score < bestScore) { bestScore = score; best = p; }
        }
        return best;
    }

    static Vector3 Ground(Vector3 p)
    {
        Terrain terrain = Terrain.activeTerrain;
        if (terrain != null) return new Vector3(p.x, terrain.SampleHeight(p) + terrain.transform.position.y, p.z);
        return BaseArea.GroundPoint(p);
    }
}
