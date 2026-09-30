using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

// Creates the sample items: ItemDefinition assets, WorldItem prefabs with their 3D models (ItemModels) and
// inventory icons rendered from those models (ItemIconRenderer), then scatters some in front of the player.
// Running it again rebuilds models and icons in place and replaces the scattered items.
// Menu: Survive In Moon > Create Sample Items
public static class SampleItemsBuilder
{
    const string ItemFolder = "Assets/Items";
    const string IconFolder = "Assets/Items/Icons";
    const string PrefabFolder = "Assets/Prefabs/Items";
    const string SceneRootName = "Sample Items";

    [MenuItem("Survive In Moon/Create Sample Items")]
    public static void Create()
    {
        EnsureFolder("Assets", "Items");
        EnsureFolder(ItemFolder, "Icons");
        EnsureFolder("Assets/Prefabs", "Items");

        try
        {
            ItemDefinition rock = Item("moon_rock", "Moon Rock", "Common regolith rock. Basic building material.",
                ItemCategory.BasicResource, 50, ItemModels.MoonRock, 0);
            ItemDefinition helium = Item("helium3", "Helium-3", "Rare isotope trapped in lunar soil. Valuable fuel.",
                ItemCategory.RareResource, 20, ItemModels.Helium3, 1);
            ItemDefinition chip = Item("circuit", "Circuit Board", "Salvaged electronics used to build tools and machines.",
                ItemCategory.TechComponent, 10, ItemModels.CircuitBoard, 2);
            ItemDefinition drill = Item("drill", "Drill", "Hand drill for breaking hard rock.",
                ItemCategory.Tool, 1, ItemModels.Drill, 3);

            string recipePath = $"{ItemFolder}/Recipe_Drill.asset";
            if (AssetDatabase.LoadAssetAtPath<RecipeDefinition>(recipePath) == null)
            {
                var recipe = ScriptableObject.CreateInstance<RecipeDefinition>();
                recipe.inputItems.Add(new ItemAmount { item = rock, quantity = 3 });
                recipe.inputItems.Add(new ItemAmount { item = chip, quantity = 1 });
                recipe.outputItem = new ItemAmount { item = drill, quantity = 1 };
                AssetDatabase.CreateAsset(recipe, recipePath);
            }
            AssetDatabase.SaveAssets();

            PlaceInScene(new[] { rock, rock, helium, chip, drill, drill }, new[] { 5, 3, 2, 1, 1, 1 });
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    static ItemDefinition Item(string id, string displayName, string description, ItemCategory category, int maxStack,
                               Func<GameObject> buildModel, int step)
    {
        EditorUtility.DisplayProgressBar("Sample Items", displayName, step / 4f);

        string path = $"{ItemFolder}/{id}.asset";
        ItemDefinition item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
        if (item == null)
        {
            item = ScriptableObject.CreateInstance<ItemDefinition>();
            AssetDatabase.CreateAsset(item, path);
        }
        item.id = id;
        item.displayName = displayName;
        item.description = description;
        item.category = category;
        item.maxStackSize = category == ItemCategory.Tool ? 1 : maxStack;

        // Prefab: WorldItem + one box collider around the model (saved over the old one, so references stay)
        var root = new GameObject(id);
        GameObject model = buildModel();
        model.transform.SetParent(root.transform, false);
        WorldItem worldItem = root.AddComponent<WorldItem>();
        worldItem.item = item;
        FitBoxCollider(root);
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabFolder}/{id}.prefab");
        Object.DestroyImmediate(root);
        item.worldPrefab = prefab.GetComponent<WorldItem>();

        AssetDatabase.DeleteAsset($"{PrefabFolder}/{id}.mat"); // Material of the old one-shape model
        item.icon = ItemIconRenderer.Render(prefab, $"{IconFolder}/{id}.png");

        EditorUtility.SetDirty(item);
        return item;
    }

    static void FitBoxCollider(GameObject root)
    {
        Bounds bounds = new Bounds(root.transform.position, Vector3.zero);
        bool any = false;
        foreach (Renderer r in root.GetComponentsInChildren<Renderer>())
        {
            if (!any) { bounds = r.bounds; any = true; }
            else bounds.Encapsulate(r.bounds);
        }
        BoxCollider box = root.AddComponent<BoxCollider>();
        box.isTrigger = true; // See WorldItem: items don't block the camera or the player
        box.center = root.transform.InverseTransformPoint(bounds.center);
        box.size = bounds.size;
    }

    static void PlaceInScene(ItemDefinition[] items, int[] amounts)
    {
        GameObject old = GameObject.Find(SceneRootName);
        if (old != null) Undo.DestroyObjectImmediate(old);

        PlayerMovement player = null;
        foreach (PlayerMovement p in Object.FindObjectsByType<PlayerMovement>(FindObjectsSortMode.None))
            if (p.gameObject.activeInHierarchy) { player = p; break; }
        Vector3 origin = player != null ? player.transform.position : Vector3.zero;
        Vector3 forward = player != null ? Vector3.ProjectOnPlane(player.transform.forward, Vector3.up).normalized : Vector3.forward;
        Vector3 right = Vector3.Cross(Vector3.up, forward);

        var parent = new GameObject(SceneRootName).transform;
        Undo.RegisterCreatedObjectUndo(parent.gameObject, "Create Sample Items");
        for (int i = 0; i < items.Length; i++)
        {
            Vector3 spot = origin + forward * (12f + 4f * (i / 3)) + right * ((i % 3) - 1) * 7f;
            // Drop onto the ground under the spot (the models have their pivot at the bottom)
            if (Physics.Raycast(spot + Vector3.up * 50f, Vector3.down, out RaycastHit hit, 200f, ~0, QueryTriggerInteraction.Ignore))
                spot = hit.point;

            var go = (GameObject)PrefabUtility.InstantiatePrefab(items[i].worldPrefab.gameObject, parent);
            go.transform.SetPositionAndRotation(spot, Quaternion.Euler(0f, i * 67f, 0f));
            go.GetComponent<WorldItem>().quantity = amounts[i];
        }
        EditorSceneManager.MarkSceneDirty(parent.gameObject.scene);
        Selection.activeGameObject = parent.gameObject;
    }

    static void EnsureFolder(string parent, string name)
    {
        if (!AssetDatabase.IsValidFolder($"{parent}/{name}")) AssetDatabase.CreateFolder(parent, name);
    }
}
