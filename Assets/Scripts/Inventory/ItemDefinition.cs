using UnityEngine;

public enum ItemCategory
{
    // Saved as numbers in the item assets: add new ones at the end, don't reorder
    BasicResource,
    RareResource,
    TechComponent,
    Tool,
    Other,
    Consumable, // Used up from the inventory (right click): food, water, oxygen, boosts
    Equipment,  // Works while carried: bigger oxygen tank, suit liner, shielding...
    ShipPart    // Installed into the wrecked ship to repair it
}

// A timed effect a consumable gives (e.g. a stim pack)
public enum BoostType
{
    None,
    Stamina,    // Sprinting costs much less
    Metabolism, // Food and water last longer
    Warmth      // Cold hurts much less
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

    [Header("Consumable (right click in the inventory)")]
    public float restoreFood;
    public float restoreWater;
    public float restoreOxygen;
    public float restoreHealth;
    public float restoreStamina;
    public float reduceRadiation;
    public BoostType boost;
    public float boostSeconds;

    [Header("Equipment (works while carried, doesn't add up with a second copy)")]
    [Tooltip("Extra oxygen capacity, 0.6 = +60%")] public float oxygenCapacity;
    [Tooltip("Less cold, 0.6 = 60% less")] public float coldResistance;
    [Tooltip("Less radiation, 0.6 = 60% less")] public float radiationResistance;
    [Tooltip("Faster walking and running, 0.2 = +20%")] public float moveSpeed;
    [Tooltip("Food and water last longer, 0.3 = 30% slower drain")] public float efficiency;

    // Tools, equipment and ship parts never stack
    public int MaxStack => category == ItemCategory.Tool || category == ItemCategory.Equipment ? 1 : Mathf.Max(1, maxStackSize);

    public bool IsConsumable => category == ItemCategory.Consumable;

    void OnValidate()
    {
        if (string.IsNullOrEmpty(id)) id = name;
        if (string.IsNullOrEmpty(displayName)) displayName = name;
        if (category == ItemCategory.Tool || category == ItemCategory.Equipment) maxStackSize = 1;
    }
}
