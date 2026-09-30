using System.Collections.Generic;
using UnityEngine;

// One thing that can be built at the base (solar panel, storage crate, ...): what it costs, how long it takes
// and which prefab stands there when it is finished. Create with: Assets > Create > Survive In Moon > Buildable
// An empty cost list = free to build.
[CreateAssetMenu(menuName = "Survive In Moon/Buildable", fileName = "NewBuildable")]
public class BuildableDefinition : ScriptableObject
{
    public string id;
    public string displayName;
    [TextArea] public string description;
    public Sprite icon;

    [Tooltip("Materials taken from the inventory when it is placed. Empty = free.")]
    public List<ItemAmount> cost = new List<ItemAmount>();
    [Tooltip("Seconds of work the builder needs")]
    [Min(1f)] public float buildTime = 60f;
    [Tooltip("The finished building (its pivot must be at the bottom)")]
    public GameObject prefab;

    public bool IsFree => cost.Count == 0;

    public bool CanAfford(Inventory inventory)
    {
        if (IsFree) return true;
        if (inventory == null) return false;
        foreach (ItemAmount part in cost)
            if (part.item != null && !inventory.HasItem(part.item, part.quantity)) return false;
        return true;
    }

    // Takes the materials; changes nothing if something is missing
    public bool Pay(Inventory inventory)
    {
        if (!CanAfford(inventory)) return false;
        foreach (ItemAmount part in cost)
            if (part.item != null) inventory.RemoveItem(part.item, part.quantity);
        return true;
    }

    void OnValidate()
    {
        if (string.IsNullOrEmpty(id)) id = name;
        if (string.IsNullOrEmpty(displayName)) displayName = name;
    }
}
