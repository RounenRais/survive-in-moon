using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public struct ItemAmount
{
    public ItemDefinition item;
    [Min(1)] public int quantity;
}

// Inputs -> output. Create with: Assets > Create > Survive In Moon > Recipe
[CreateAssetMenu(menuName = "Survive In Moon/Recipe", fileName = "NewRecipe")]
public class RecipeDefinition : ScriptableObject
{
    public List<ItemAmount> inputItems = new List<ItemAmount>();
    public ItemAmount outputItem;

    public bool HasInputs(Inventory inventory)
    {
        foreach (ItemAmount input in inputItems)
            if (!inventory.HasItem(input.item, input.quantity)) return false;
        return true;
    }
}
