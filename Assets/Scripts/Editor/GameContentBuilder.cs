using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

// Builds the survival game content in one go (safe to run again - assets are updated in place, scene objects
// replaced):
//  - items (raw materials, processed materials, food/water/oxygen, boosts, suit equipment, ship parts) with
//    3D models and inventory icons
//  - workbench recipes + Resources/RecipeDatabase
//  - buildings: Workbench, Supply Locker, Greenhouse, Rover Garage; power values on every building; new costs
//  - the game scene: workbench + supply locker at the hub, starter supplies, resource fields spread over the
//    map (farther = rarer: regolith and debris near, ilmenite, then ice and helium-3 far out), the crashed ship,
//    the rover hidden until a garage is built, the player's walking speeds
// Menu: Survive In Moon > Build Game Content   (game scene open)
public static partial class GameContentBuilder
{
    const string ItemFolder = "Assets/Items";
    const string IconFolder = "Assets/Items/Icons";
    const string RecipeFolder = "Assets/Items/Recipes";
    const string ItemPrefabFolder = "Assets/Prefabs/Items";
    const string ContentPrefabFolder = "Assets/Prefabs/Content";
    const string BuildingPrefabFolder = "Assets/Prefabs/Buildings";
    const string BuildableFolder = "Assets/Buildables";
    const string BuildableIconFolder = "Assets/Buildables/Icons";
    const string WorldRootName = "World Content";

    static readonly Dictionary<string, ItemDefinition> items = new Dictionary<string, ItemDefinition>();

    [MenuItem("Survive In Moon/Build Game Content")]
    public static void Build()
    {
        if (Object.FindFirstObjectByType<BaseArea>() == null)
        {
            Debug.LogError("Game content: open the game scene (with the Moon Base) first.");
            return;
        }
        ContentKit.ClearCache();
        BuildingMeshes.ClearCache();
        items.Clear();
        foreach (string f in new[] { ItemFolder, IconFolder, RecipeFolder, ItemPrefabFolder, ContentPrefabFolder, BuildableIconFolder })
            ContentKit.EnsureFolder(f);

        try
        {
            EditorUtility.DisplayProgressBar("Game content", "Items", 0.1f);
            BuildItems();
            EditorUtility.DisplayProgressBar("Game content", "Recipes", 0.4f);
            BuildRecipes();
            EditorUtility.DisplayProgressBar("Game content", "Buildings", 0.55f);
            var prefabs = BuildBuildings();
            EditorUtility.DisplayProgressBar("Game content", "World", 0.75f);
            BuildWorld(prefabs);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            Debug.Log($"Game content built: {items.Count} items, recipes, buildings and the world.");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    // =============================================================== Items

    static void BuildItems()
    {
        // Existing items keep their models and icons
        foreach (string id in new[] { "moon_rock", "helium3", "circuit", "drill" })
        {
            var existing = AssetDatabase.LoadAssetAtPath<ItemDefinition>($"{ItemFolder}/{id}.asset");
            if (existing != null) items[id] = existing;
        }
        if (items.TryGetValue("moon_rock", out var rockItem))
        {
            rockItem.displayName = "Regolith Rock";
            rockItem.description = "Loose lunar rock. Melted down it makes glass; packed, it builds walls.";
            EditorUtility.SetDirty(rockItem);
        }

        // Raw materials
        Item("ice", "Water Ice", "Frozen water from the permanently shaded crater floors. Melt it for drinking water, split it for oxygen.",
             ItemCategory.BasicResource, 30, IceModel);
        Item("ilmenite", "Ilmenite Ore", "Dark iron-titanium ore common in the lunar maria. Smelts into metal and releases oxygen.",
             ItemCategory.BasicResource, 30, IlmeniteModel);
        Item("scrap", "Scrap Metal", "Twisted hull pieces from the crash. Can be reworked into plates.",
             ItemCategory.BasicResource, 30, ScrapModel);

        // Processed
        Item("metal_plate", "Metal Plate", "Smelted iron-titanium sheet. The backbone of everything you build.",
             ItemCategory.TechComponent, 30, PlateModel);
        Item("glass", "Regolith Glass", "Lunar soil melted into clear panels. Greenhouses and visors are made of it.",
             ItemCategory.TechComponent, 30, GlassModel);
        Item("battery", "Battery", "Stores power for suits, tools and machines.", ItemCategory.TechComponent, 20, BatteryModel);
        Item("fuel_cell", "Fuel Cell", "Helium-3 fuel cartridge. Getting into the rover with one tops up its tank.",
             ItemCategory.TechComponent, 10, FuelCellModel);

        // Consumables
        Item("water_pack", "Water Pack", "Filtered water from melted ice.", ItemCategory.Consumable, 10, WaterModel, i => i.restoreWater = 40f);
        Item("o2_canister", "Oxygen Canister", "Oxygen split from ice by electrolysis. Refills your suit anywhere.",
             ItemCategory.Consumable, 10, OxygenModel, i => i.restoreOxygen = 50f);
        Item("lettuce", "Lettuce", "Red romaine, the first crop NASA astronauts grew and ate in space (Veggie, 2015).",
             ItemCategory.Consumable, 20, LettuceModel, i => { i.restoreFood = 10f; i.restoreWater = 6f; });
        Item("potato", "Potato", "Slow to grow, but very filling.", ItemCategory.Consumable, 20, PotatoModel, i => i.restoreFood = 22f);
        Item("ration", "Meal Ration", "A proper meal: potatoes and water, sealed for the road.", ItemCategory.Consumable, 10, RationModel,
             i => { i.restoreFood = 55f; i.restoreWater = 15f; i.restoreHealth = 10f; });
        Item("stim_pack", "Stim Pack", "Sprinting costs far less stamina for a few minutes.", ItemCategory.Consumable, 5, StimModel,
             i => { i.restoreStamina = 100f; i.boost = BoostType.Stamina; i.boostSeconds = 180f; });
        Item("nutrient_bar", "Nutrient Bar", "Dense greens and starch. Food and water last twice as long for a while.",
             ItemCategory.Consumable, 10, NutrientModel, i => { i.restoreFood = 15f; i.boost = BoostType.Metabolism; i.boostSeconds = 300f; });
        Item("heat_pack", "Heat Pack", "Iron powder from ilmenite oxidising in a pouch - warm hands in the lunar shade.",
             ItemCategory.Consumable, 10, HeatPackModel, i => { i.boost = BoostType.Warmth; i.boostSeconds = 240f; });
        Item("medkit", "Medkit", "Treats injuries and helps the body clear radiation damage.", ItemCategory.Consumable, 5, MedkitModel,
             i => { i.restoreHealth = 45f; i.reduceRadiation = 25f; });

        // Equipment (works while carried)
        Item("o2_tank", "Extended O2 Tank", "A second oxygen tank on the suit: +60% oxygen capacity.", ItemCategory.Equipment, 1, TankModel,
             i => i.oxygenCapacity = 0.6f);
        Item("thermal_liner", "Thermal Liner", "Insulating suit layer. The lunar shade hurts much less.", ItemCategory.Equipment, 1, LinerModel,
             i => i.coldResistance = 0.6f);
        Item("rad_shield", "Water Shield Vest", "A vest of water bags - water is one of the best radiation shields.",
             ItemCategory.Equipment, 1, ShieldModel, i => i.radiationResistance = 0.6f);
        Item("servo_boots", "Servo Boots", "Powered boots: you walk and run 20% faster.", ItemCategory.Equipment, 1, BootsModel,
             i => i.moveSpeed = 0.2f);
        Item("recycler", "Water Recycler", "Recovers water from the suit. Food and water last 35% longer.", ItemCategory.Equipment, 1, RecyclerModel,
             i => i.efficiency = 0.35f);

        // Ship parts
        Item("hull_plate", "Hull Plating", "Curved armour to seal the ship's hull.", ItemCategory.ShipPart, 10, HullPlateModel);
        Item("thruster", "Thruster Nozzle", "A rebuilt engine bell.", ItemCategory.ShipPart, 4, ThrusterModel);
        Item("nav_computer", "Navigation Computer", "Guidance for the trip home.", ItemCategory.ShipPart, 2, NavModel);
        Item("fuel_tank", "Fuel Tank", "A pressurised tank of helium-3 fuel for the engines.", ItemCategory.ShipPart, 4, FuelTankModel);
    }

    static ItemDefinition Item(string id, string name, string description, ItemCategory category, int stack,
                               Func<GameObject> model, Action<ItemDefinition> setup = null)
    {
        string path = $"{ItemFolder}/{id}.asset";
        var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
        if (item == null)
        {
            item = ScriptableObject.CreateInstance<ItemDefinition>();
            AssetDatabase.CreateAsset(item, path);
        }
        item.id = id;
        item.displayName = name;
        item.description = description;
        item.category = category;
        item.maxStackSize = stack;
        // Reset the effect fields, then let the setup fill in this item's
        item.restoreFood = item.restoreWater = item.restoreOxygen = item.restoreHealth = item.restoreStamina = item.reduceRadiation = 0f;
        item.boost = BoostType.None;
        item.boostSeconds = 0f;
        item.oxygenCapacity = item.coldResistance = item.radiationResistance = item.moveSpeed = item.efficiency = 0f;
        setup?.Invoke(item);

        // World prefab (what lies on the ground) + icon rendered from it
        var root = new GameObject(id);
        GameObject m = model();
        m.transform.SetParent(root.transform, false);
        var world = root.AddComponent<WorldItem>();
        world.item = item;
        var box = root.AddComponent<BoxCollider>();
        Bounds b = RendererBounds(root);
        box.isTrigger = true;
        box.center = root.transform.InverseTransformPoint(b.center);
        box.size = b.size;
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{ItemPrefabFolder}/{id}.prefab");
        Object.DestroyImmediate(root);
        item.worldPrefab = prefab.GetComponent<WorldItem>();
        item.icon = ItemIconRenderer.Render(prefab, $"{IconFolder}/{id}.png");
        EditorUtility.SetDirty(item);
        items[id] = item;
        return item;
    }

    // --------------------------------------------------------------- Item models (about 2-3 units, pivot on the ground)

    static GameObject Model(out Transform t) { var g = new GameObject("Model"); t = g.transform; return g; }

    static GameObject IceModel()
    {
        var g = Model(out var t);
        Material ice = ContentKit.Mat("Ice", new Color(0.72f, 0.88f, 1f), 0.1f, 0.92f, new Color(0.05f, 0.12f, 0.18f));
        ContentKit.Part(t, "A", ContentKit.Box(0.9f, 1.8f, 0.9f, 0.15f), ice, new Vector3(0f, 0.85f, 0f), new Vector3(8f, 20f, 6f));
        ContentKit.Part(t, "B", ContentKit.Box(0.7f, 1.3f, 0.7f, 0.12f), ice, new Vector3(0.65f, 0.6f, 0.2f), new Vector3(-6f, 50f, -18f));
        ContentKit.Part(t, "C", ContentKit.Box(0.6f, 1f, 0.6f, 0.1f), ice, new Vector3(-0.55f, 0.45f, -0.25f), new Vector3(10f, 10f, 22f));
        return g;
    }

    static GameObject IlmeniteModel()
    {
        var g = Model(out var t);
        ContentKit.Part(t, "Ore", ContentKit.Rock(31, 0.22f), ContentKit.Mat("Ilmenite", new Color(0.16f, 0.16f, 0.19f), 0.75f, 0.55f),
                        new Vector3(0f, 0.7f, 0f), new Vector3(0f, 30f, 0f), new Vector3(1.2f, 0.8f, 1f));
        return g;
    }

    static GameObject ScrapModel()
    {
        var g = Model(out var t);
        ContentKit.Part(t, "Plate", ContentKit.Box(2.2f, 0.12f, 1.4f, 0.04f), ContentKit.Hull, new Vector3(0f, 0.3f, 0f), new Vector3(8f, 15f, 12f));
        ContentKit.Part(t, "Bent", ContentKit.Box(1.6f, 0.12f, 1f, 0.04f), ContentKit.HullDark, new Vector3(0.3f, 0.6f, 0.2f), new Vector3(-20f, 60f, 25f));
        ContentKit.Part(t, "Strip", ContentKit.Box(1.2f, 0.1f, 0.3f, 0.03f), ContentKit.Accent, new Vector3(-0.5f, 0.45f, -0.3f), new Vector3(0f, -30f, 15f));
        return g;
    }

    static GameObject PlateModel()
    {
        var g = Model(out var t);
        Material steel = ContentKit.Mat("Steel", new Color(0.62f, 0.64f, 0.67f), 0.85f, 0.55f);
        ContentKit.Part(t, "Plate1", ContentKit.Box(2f, 0.16f, 1.3f, 0.05f), steel, new Vector3(0f, 0.08f, 0f));
        ContentKit.Part(t, "Plate2", ContentKit.Box(2f, 0.16f, 1.3f, 0.05f), steel, new Vector3(0.05f, 0.25f, 0.02f), new Vector3(0f, 8f, 0f));
        return g;
    }

    static GameObject GlassModel()
    {
        var g = Model(out var t);
        ContentKit.Part(t, "Frame", ContentKit.Box(1.9f, 0.14f, 1.4f, 0.05f), ContentKit.Gunmetal, new Vector3(0f, 0.07f, 0f));
        ContentKit.Part(t, "Pane", ContentKit.Box(1.7f, 0.1f, 1.2f, 0.03f), ContentKit.Glass, new Vector3(0f, 0.15f, 0f));
        return g;
    }

    static GameObject BatteryModel()
    {
        var g = Model(out var t);
        ContentKit.Part(t, "Body", ContentKit.Box(1f, 1.6f, 0.7f, 0.12f), ContentKit.Gunmetal, new Vector3(0f, 0.8f, 0f));
        ContentKit.Part(t, "Band", ContentKit.Box(1.04f, 0.3f, 0.74f, 0.08f), ContentKit.Accent, new Vector3(0f, 1.1f, 0f));
        ContentKit.Part(t, "Pin", ContentKit.Cylinder(0.12f, 0.2f, 12), ContentKit.Hull, new Vector3(0.25f, 1.6f, 0f));
        ContentKit.Part(t, "Pin2", ContentKit.Cylinder(0.12f, 0.2f, 12), ContentKit.Hull, new Vector3(-0.25f, 1.6f, 0f));
        return g;
    }

    static GameObject FuelCellModel()
    {
        var g = Model(out var t);
        Material yellow = ContentKit.Mat("FuelYellow", new Color(0.95f, 0.78f, 0.2f), 0.2f, 0.5f);
        ContentKit.Part(t, "Body", ContentKit.Cylinder(0.5f, 2f), yellow, Vector3.zero);
        ContentKit.Part(t, "Band1", ContentKit.Cylinder(0.53f, 0.18f), ContentKit.Gunmetal, new Vector3(0f, 0.5f, 0f));
        ContentKit.Part(t, "Band2", ContentKit.Cylinder(0.53f, 0.18f), ContentKit.Gunmetal, new Vector3(0f, 1.4f, 0f));
        ContentKit.Part(t, "Cap", ContentKit.Cylinder(0.25f, 0.3f), ContentKit.Gunmetal, new Vector3(0f, 2f, 0f));
        return g;
    }

    static GameObject WaterModel()
    {
        var g = Model(out var t);
        Material blue = ContentKit.Mat("WaterBlue", new Color(0.3f, 0.55f, 0.95f), 0.1f, 0.7f);
        ContentKit.Part(t, "Pouch", ContentKit.Box(1.1f, 1.5f, 0.6f, 0.25f), blue, new Vector3(0f, 0.75f, 0f));
        ContentKit.Part(t, "Cap", ContentKit.Cylinder(0.18f, 0.3f, 12), ContentKit.Hull, new Vector3(0f, 1.5f, 0f));
        return g;
    }

    static GameObject OxygenModel()
    {
        var g = Model(out var t);
        Material o2 = ContentKit.Mat("OxygenBlue", new Color(0.45f, 0.78f, 1f), 0.2f, 0.6f, new Color(0.05f, 0.15f, 0.25f));
        ContentKit.Part(t, "Body", ContentKit.Cylinder(0.55f, 2f), ContentKit.Hull, Vector3.zero);
        ContentKit.Part(t, "Band", ContentKit.Cylinder(0.58f, 0.5f), o2, new Vector3(0f, 0.8f, 0f));
        ContentKit.Part(t, "Valve", ContentKit.Cylinder(0.2f, 0.4f, 12), ContentKit.Gunmetal, new Vector3(0f, 2f, 0f));
        return g;
    }

    static GameObject LettuceModel()
    {
        var g = Model(out var t);
        Material leaf = ContentKit.Mat("LettuceRed", new Color(0.55f, 0.2f, 0.25f), 0f, 0.35f);
        Material green = ContentKit.Mat("LettuceGreen", new Color(0.35f, 0.62f, 0.28f), 0f, 0.35f);
        ContentKit.Part(t, "Core", ContentKit.Sphere, green, new Vector3(0f, 0.6f, 0f), default, new Vector3(1.2f, 1f, 1.2f));
        for (int i = 0; i < 5; i++)
            ContentKit.Part(t, "Leaf", ContentKit.Sphere, leaf, Quaternion.Euler(0f, i * 72f, 0f) * new Vector3(0.45f, 0.65f, 0f),
                            new Vector3(0f, i * 72f, 30f), new Vector3(0.9f, 0.9f, 0.5f));
        return g;
    }

    static GameObject PotatoModel()
    {
        var g = Model(out var t);
        Material brown = ContentKit.Mat("Potato", new Color(0.62f, 0.45f, 0.28f), 0f, 0.2f);
        ContentKit.Part(t, "P1", ContentKit.Rock(77, 0.12f), brown, new Vector3(0f, 0.45f, 0f), new Vector3(0f, 20f, 0f), new Vector3(0.75f, 0.5f, 0.55f));
        ContentKit.Part(t, "P2", ContentKit.Rock(78, 0.12f), brown, new Vector3(0.7f, 0.35f, 0.3f), new Vector3(0f, 70f, 0f), new Vector3(0.55f, 0.38f, 0.45f));
        return g;
    }

    static GameObject RationModel()
    {
        var g = Model(out var t);
        Material pack = ContentKit.Mat("RationPack", new Color(0.82f, 0.78f, 0.66f), 0f, 0.3f);
        ContentKit.Part(t, "Box", ContentKit.Box(1.6f, 0.6f, 1.1f, 0.12f), pack, new Vector3(0f, 0.3f, 0f));
        ContentKit.Part(t, "Label", ContentKit.Box(1.62f, 0.2f, 1.12f, 0.05f), ContentKit.Accent, new Vector3(0f, 0.35f, 0f));
        return g;
    }

    static GameObject StimModel()
    {
        var g = Model(out var t);
        Material red = ContentKit.Mat("StimRed", new Color(0.85f, 0.2f, 0.25f), 0.1f, 0.5f);
        ContentKit.Part(t, "Tube", ContentKit.Cylinder(0.28f, 1.6f, 14), red, Vector3.zero, new Vector3(0f, 0f, 0f));
        ContentKit.Part(t, "Cap", ContentKit.Cylinder(0.3f, 0.35f, 14), ContentKit.Hull, new Vector3(0f, 1.6f, 0f));
        ContentKit.Part(t, "Needle", ContentKit.Cylinder(0.05f, 0.5f, 8), ContentKit.HullDark, new Vector3(0f, 1.95f, 0f));
        return g;
    }

    static GameObject NutrientModel()
    {
        var g = Model(out var t);
        Material bar = ContentKit.Mat("NutrientGreen", new Color(0.45f, 0.65f, 0.3f), 0f, 0.3f);
        ContentKit.Part(t, "Bar", ContentKit.Box(1.8f, 0.4f, 0.6f, 0.1f), bar, new Vector3(0f, 0.2f, 0f));
        ContentKit.Part(t, "Wrap", ContentKit.Box(0.5f, 0.44f, 0.64f, 0.06f), ContentKit.Hull, new Vector3(0.6f, 0.2f, 0f));
        return g;
    }

    static GameObject HeatPackModel()
    {
        var g = Model(out var t);
        Material orange = ContentKit.Mat("HeatOrange", new Color(0.95f, 0.5f, 0.15f), 0f, 0.35f, new Color(0.2f, 0.06f, 0f));
        ContentKit.Part(t, "Pouch", ContentKit.Box(1.4f, 0.3f, 1f, 0.15f), orange, new Vector3(0f, 0.15f, 0f));
        return g;
    }

    static GameObject MedkitModel()
    {
        var g = Model(out var t);
        Material red = ContentKit.Mat("StimRed", new Color(0.85f, 0.2f, 0.25f), 0.1f, 0.5f);
        ContentKit.Part(t, "Case", ContentKit.Box(1.6f, 1f, 0.9f, 0.15f), ContentKit.Hull, new Vector3(0f, 0.5f, 0f));
        ContentKit.Part(t, "CrossA", ContentKit.Box(0.7f, 0.2f, 0.05f, 0.02f), red, new Vector3(0f, 0.55f, 0.46f));
        ContentKit.Part(t, "CrossB", ContentKit.Box(0.2f, 0.7f, 0.05f, 0.02f), red, new Vector3(0f, 0.55f, 0.46f));
        ContentKit.Part(t, "Handle", ContentKit.Box(0.6f, 0.15f, 0.2f, 0.05f), ContentKit.Gunmetal, new Vector3(0f, 1.05f, 0f));
        return g;
    }

    static GameObject TankModel()
    {
        var g = Model(out var t);
        foreach (float x in new[] { -0.45f, 0.45f })
        {
            ContentKit.Part(t, "Tank", ContentKit.Cylinder(0.42f, 2.2f), ContentKit.Hull, new Vector3(x, 0f, 0f));
            ContentKit.Part(t, "Top", ContentKit.Cylinder(0.18f, 0.35f, 12), ContentKit.Gunmetal, new Vector3(x, 2.2f, 0f));
        }
        ContentKit.Part(t, "Strap", ContentKit.Box(1.9f, 0.25f, 0.95f, 0.08f), ContentKit.Accent, new Vector3(0f, 1.2f, 0f));
        return g;
    }

    static GameObject LinerModel()
    {
        var g = Model(out var t);
        Material fabric = ContentKit.Mat("LinerFabric", new Color(0.92f, 0.62f, 0.22f), 0f, 0.25f);
        ContentKit.Part(t, "Fold1", ContentKit.Box(1.8f, 0.3f, 1.3f, 0.12f), fabric, new Vector3(0f, 0.15f, 0f));
        ContentKit.Part(t, "Fold2", ContentKit.Box(1.7f, 0.28f, 1.2f, 0.12f), ContentKit.Foil, new Vector3(0f, 0.43f, 0f), new Vector3(0f, 6f, 0f));
        return g;
    }

    static GameObject ShieldModel()
    {
        var g = Model(out var t);
        Material blue = ContentKit.Mat("WaterBlue", new Color(0.3f, 0.55f, 0.95f), 0.1f, 0.7f);
        ContentKit.Part(t, "Vest", ContentKit.Box(1.7f, 1.9f, 0.5f, 0.2f), ContentKit.Gunmetal, new Vector3(0f, 0.95f, 0f));
        for (int i = 0; i < 3; i++)
            ContentKit.Part(t, "Bag", ContentKit.Box(1.4f, 0.4f, 0.3f, 0.14f), blue, new Vector3(0f, 0.45f + i * 0.5f, 0.28f));
        return g;
    }

    static GameObject BootsModel()
    {
        var g = Model(out var t);
        Material glow = ContentKit.Mat("ServoGlow", new Color(0.4f, 0.85f, 1f), 0f, 0.8f, new Color(0.15f, 0.5f, 0.7f));
        foreach (float x in new[] { -0.5f, 0.5f })
        {
            ContentKit.Part(t, "Sole", ContentKit.Box(0.75f, 0.35f, 1.4f, 0.1f), ContentKit.Gunmetal, new Vector3(x, 0.18f, 0.15f));
            ContentKit.Part(t, "Shaft", ContentKit.Box(0.7f, 1.2f, 0.75f, 0.15f), ContentKit.Hull, new Vector3(x, 0.9f, -0.1f));
            ContentKit.Part(t, "Servo", ContentKit.Box(0.72f, 0.2f, 0.77f, 0.05f), glow, new Vector3(x, 0.7f, -0.1f));
        }
        return g;
    }

    static GameObject RecyclerModel()
    {
        var g = Model(out var t);
        Material blue = ContentKit.Mat("WaterBlue", new Color(0.3f, 0.55f, 0.95f), 0.1f, 0.7f);
        ContentKit.Part(t, "Box", ContentKit.Box(1.5f, 1f, 1f, 0.15f), ContentKit.Hull, new Vector3(0f, 0.5f, 0f));
        ContentKit.Part(t, "Window", ContentKit.Box(0.6f, 0.5f, 0.05f, 0.02f), blue, new Vector3(-0.3f, 0.55f, 0.5f));
        ContentKit.Part(t, "Tube", ContentKit.Cylinder(0.1f, 0.8f, 10), ContentKit.Gunmetal, new Vector3(0.5f, 1f, 0f), new Vector3(0f, 0f, 30f));
        return g;
    }

    static GameObject HullPlateModel()
    {
        var g = Model(out var t);
        ContentKit.Part(t, "Plate", ContentKit.Box(2.6f, 0.2f, 1.8f, 0.08f), ContentKit.Hull, new Vector3(0f, 0.25f, 0f), new Vector3(0f, 0f, 6f));
        ContentKit.Part(t, "Edge", ContentKit.Box(2.62f, 0.24f, 0.25f, 0.06f), ContentKit.Accent, new Vector3(0f, 0.27f, 0.8f), new Vector3(0f, 0f, 6f));
        return g;
    }

    static GameObject ThrusterModel()
    {
        var g = Model(out var t);
        ContentKit.Part(t, "Bell", ContentKit.Bell(0.4f, 1.1f, 1.8f), ContentKit.Gunmetal, Vector3.zero);
        ContentKit.Part(t, "Chamber", ContentKit.Cylinder(0.5f, 0.6f), ContentKit.HullDark, new Vector3(0f, 1.8f, 0f));
        return g;
    }

    static GameObject NavModel()
    {
        var g = Model(out var t);
        ContentKit.Part(t, "Case", ContentKit.Box(1.8f, 1.2f, 1.1f, 0.1f), ContentKit.Gunmetal, new Vector3(0f, 0.6f, 0f));
        ContentKit.Part(t, "Screen", ContentKit.Box(1.2f, 0.7f, 0.05f, 0.02f), ContentKit.Screen, new Vector3(0f, 0.65f, 0.56f));
        ContentKit.Part(t, "Antenna", ContentKit.Cylinder(0.05f, 0.7f, 8), ContentKit.Hull, new Vector3(0.7f, 1.2f, -0.3f));
        return g;
    }

    static GameObject FuelTankModel()
    {
        var g = Model(out var t);
        ContentKit.Part(t, "Tank", ContentKit.Cylinder(0.75f, 2.4f), ContentKit.Hull, Vector3.zero);
        ContentKit.Part(t, "Dome", ContentKit.Sphere, ContentKit.Hull, new Vector3(0f, 2.4f, 0f), default, Vector3.one * 1.5f);
        ContentKit.Part(t, "Band", ContentKit.Cylinder(0.78f, 0.3f), ContentKit.Accent, new Vector3(0f, 1.1f, 0f));
        return g;
    }

    // =============================================================== Recipes

    static void BuildRecipes()
    {
        var list = new List<RecipeDefinition>
        {
            // Materials
            Recipe("metal_plate", 2, ("ilmenite", 3)),
            Recipe("metal_plate", 1, ("scrap", 2)),
            Recipe("glass", 1, ("moon_rock", 3)),
            Recipe("battery", 1, ("scrap", 2), ("moon_rock", 1)),
            Recipe("circuit", 1, ("scrap", 3), ("glass", 1)),
            Recipe("fuel_cell", 1, ("helium3", 2), ("battery", 1)),
            // Survival
            Recipe("water_pack", 2, ("ice", 2)),
            Recipe("o2_canister", 1, ("ice", 3), ("battery", 1)),
            Recipe("ration", 1, ("potato", 2), ("water_pack", 1)),
            Recipe("nutrient_bar", 1, ("potato", 1), ("lettuce", 2)),
            Recipe("stim_pack", 1, ("lettuce", 2), ("water_pack", 1)),
            Recipe("heat_pack", 2, ("ilmenite", 1), ("water_pack", 1)),
            Recipe("medkit", 1, ("metal_plate", 1), ("water_pack", 2), ("lettuce", 1)),
            // Equipment
            Recipe("drill", 1, ("moon_rock", 3), ("circuit", 1)),
            Recipe("o2_tank", 1, ("metal_plate", 3), ("battery", 1)),
            Recipe("thermal_liner", 1, ("glass", 2), ("scrap", 3)),
            Recipe("rad_shield", 1, ("metal_plate", 2), ("ice", 4)),
            Recipe("servo_boots", 1, ("battery", 2), ("circuit", 1), ("metal_plate", 2)),
            Recipe("recycler", 1, ("glass", 1), ("circuit", 1), ("metal_plate", 2)),
            // Ship parts
            Recipe("hull_plate", 1, ("metal_plate", 3), ("glass", 1)),
            Recipe("thruster", 1, ("metal_plate", 4), ("helium3", 2)),
            Recipe("nav_computer", 1, ("circuit", 3), ("battery", 2), ("glass", 1)),
            Recipe("fuel_tank", 1, ("metal_plate", 3), ("fuel_cell", 2)),
        };

        string dbPath = "Assets/Resources/RecipeDatabase.asset";
        var db = AssetDatabase.LoadAssetAtPath<RecipeDatabase>(dbPath);
        if (db == null)
        {
            db = ScriptableObject.CreateInstance<RecipeDatabase>();
            AssetDatabase.CreateAsset(db, dbPath);
        }
        db.recipes = list;
        EditorUtility.SetDirty(db);
    }

    static RecipeDefinition Recipe(string output, int amount, params (string id, int n)[] inputs)
    {
        string name = $"Recipe_{output}_{string.Join("_", inputs.Select(i => i.id))}";
        string path = $"{RecipeFolder}/{name}.asset";
        var r = AssetDatabase.LoadAssetAtPath<RecipeDefinition>(path);
        if (r == null)
        {
            r = ScriptableObject.CreateInstance<RecipeDefinition>();
            AssetDatabase.CreateAsset(r, path);
        }
        r.inputItems = inputs.Select(i => new ItemAmount { item = I(i.id), quantity = i.n }).ToList();
        r.outputItem = new ItemAmount { item = I(output), quantity = amount };
        EditorUtility.SetDirty(r);
        return r;
    }

    static ItemDefinition I(string id)
    {
        if (items.TryGetValue(id, out var item)) return item;
        throw new Exception($"Game content: item '{id}' is missing");
    }

    static Bounds RendererBounds(GameObject root)
    {
        Bounds b = new Bounds(root.transform.position, Vector3.zero);
        bool any = false;
        foreach (Renderer r in root.GetComponentsInChildren<Renderer>())
        {
            if (!any) { b = r.bounds; any = true; }
            else b.Encapsulate(r.bounds);
        }
        return b;
    }
}
