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
        descriptionText.text = item.description;
        descriptionText.gameObject.SetActive(!string.IsNullOrEmpty(item.description));
        gameObject.SetActive(true);
        Follow(Input.mousePosition);
    }

    public void Hide() => gameObject.SetActive(false);

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
