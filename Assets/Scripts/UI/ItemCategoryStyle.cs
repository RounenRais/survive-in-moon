using UnityEngine;

// How each item category looks in the UI (name and accent color)
public static class ItemCategoryStyle
{
    public static string Name(ItemCategory category)
    {
        switch (category)
        {
            case ItemCategory.BasicResource: return "Basic Resource";
            case ItemCategory.RareResource: return "Rare Resource";
            case ItemCategory.TechComponent: return "Tech Component";
            case ItemCategory.Tool: return "Tool";
            case ItemCategory.Consumable: return "Consumable";
            case ItemCategory.Equipment: return "Equipment";
            case ItemCategory.ShipPart: return "Ship Part";
            default: return "Other";
        }
    }

    public static Color Color(ItemCategory category)
    {
        switch (category)
        {
            case ItemCategory.BasicResource: return new Color(0.72f, 0.72f, 0.7f);
            case ItemCategory.RareResource: return new Color(0.35f, 0.8f, 1f);
            case ItemCategory.TechComponent: return new Color(0.4f, 0.9f, 0.5f);
            case ItemCategory.Tool: return new Color(1f, 0.65f, 0.2f);
            case ItemCategory.Consumable: return new Color(0.95f, 0.45f, 0.5f);
            case ItemCategory.Equipment: return new Color(0.7f, 0.55f, 1f);
            case ItemCategory.ShipPart: return new Color(1f, 0.85f, 0.35f);
            default: return new Color(0.85f, 0.85f, 0.85f);
        }
    }
}
