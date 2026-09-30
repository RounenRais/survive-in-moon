using UnityEngine;

public enum ItemCategory
{
    // Saved as numbers in the item assets: add new ones at the end, don't reorder
    BasicResource,
    RareResource,
    TechComponent,
    Tool,
    Other
}

// One kind of item (Moon rock, drill, ...). Create with: Assets > Create > Survive In Moon > Item
// The id is what gets saved, so don't change it after release.
[CreateAssetMenu(menuName = "Survive In Moon/Item", fileName = "NewItem")]
public class ItemDefinition : ScriptableObject
{
    public string id;
    public string displayName;
    public Sprite icon;
    [TextArea] public string description;
    [Min(1)] public int maxStackSize = 50;
    public ItemCategory category = ItemCategory.BasicResource;
    public WorldItem worldPrefab; // What lies on the ground (for dropping later)

    // Tools never stack
    public int MaxStack => category == ItemCategory.Tool ? 1 : Mathf.Max(1, maxStackSize);

    void OnValidate()
    {
        if (string.IsNullOrEmpty(id)) id = name;
        if (string.IsNullOrEmpty(displayName)) displayName = name;
        if (category == ItemCategory.Tool) maxStackSize = 1;
    }
}
