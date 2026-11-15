using TMPro;
using UnityEngine;

// Name + description panel next to the pointer. Shows and hides instantly.
// Keep it on a layer that doesn't catch the pointer (no raycast targets), or hovering would flicker.
// Starts hidden (saved inactive by the builder).
public class ItemTooltip : MonoBehaviour
{
    public TMP_Text nameText;
    public TMP_Text categoryText;
    public TMP_Text descriptionText;
    public Vector2 offset = new Vector2(18f, -18f); // From the pointer, in screen pixels

    private RectTransform rect;
    private Canvas canvas;

    void Awake()
    {
        rect = (RectTransform)transform;
        canvas = GetComponentInParent<Canvas>();
    }

    public void Show(ItemDefinition item)
    {
        nameText.text = item.displayName;
        if (categoryText != null)
        {
            categoryText.text = ItemCategoryStyle.Name(item.category);
            categoryText.color = ItemCategoryStyle.Color(item.category);
        }
        descriptionText.text = item.description + Effects(item);
        descriptionText.gameObject.SetActive(!string.IsNullOrEmpty(item.description));
        gameObject.SetActive(true);
        Follow(Input.mousePosition);
    }

    public void Hide() => gameObject.SetActive(false);

    // What the item does, in one short line under the description
    static string Effects(ItemDefinition item)
    {
        var parts = new System.Collections.Generic.List<string>();
        if (item.restoreFood > 0f) parts.Add($"+{item.restoreFood:0} food");
        if (item.restoreWater > 0f) parts.Add($"+{item.restoreWater:0} water");
        if (item.restoreOxygen > 0f) parts.Add($"+{item.restoreOxygen:0} oxygen");
        if (item.restoreHealth > 0f) parts.Add($"+{item.restoreHealth:0} health");
        if (item.restoreStamina > 0f) parts.Add($"+{item.restoreStamina:0} stamina");
        if (item.reduceRadiation > 0f) parts.Add($"-{item.reduceRadiation:0}% radiation");
        if (item.boostSeconds > 0f && item.boost != BoostType.None) parts.Add($"{item.boost} boost {item.boostSeconds / 60f:0.#} min");
        if (item.oxygenCapacity > 0f) parts.Add($"+{item.oxygenCapacity * 100f:0}% oxygen capacity");
        if (item.coldResistance > 0f) parts.Add($"{item.coldResistance * 100f:0}% less cold");
        if (item.radiationResistance > 0f) parts.Add($"{item.radiationResistance * 100f:0}% less radiation");
        if (item.moveSpeed > 0f) parts.Add($"+{item.moveSpeed * 100f:0}% speed");
        if (item.efficiency > 0f) parts.Add($"food & water last {item.efficiency * 100f:0}% longer");
        if (parts.Count == 0) return "";
        string line = string.Join(" · ", parts);
        if (item.IsConsumable) line += "\n<color=#9a9ca3>Right click to use</color>";
        else if (item.category == ItemCategory.Equipment) line += "\n<color=#9a9ca3>Works while carried</color>";
        return "\n\n" + line;
    }

    void Update() => Follow(Input.mousePosition);

    // Below-right of the pointer; flips to the other side near the screen edges (the pivot is the top-left corner)
    void Follow(Vector2 pointer)
    {
        if (rect == null) Awake();
        float scale = canvas != null ? canvas.rootCanvas.scaleFactor : 1f;
        Vector2 size = rect.rect.size * scale;
        Vector2 position = pointer + offset;
        if (position.x + size.x > Screen.width) position.x = pointer.x - offset.x - size.x;
        if (position.y - size.y < 0f) position.y = pointer.y - offset.y + size.y;
        rect.position = position;
    }
}
