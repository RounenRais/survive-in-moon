using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// One inventory slot on screen: icon + amount. Only redraws when its own slot changes.
// Dragging, dropping and hovering are passed on to InventoryUI / Inventory; the rules live in Inventory.
public class SlotUI : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
                      IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler
{
    public Image background;
    public Image icon;
    public TMP_Text quantityText;
    public Image categoryBar; // Thin line in the item's category color (optional)
    public Color emptyColor = new Color(1f, 1f, 1f, 0.05f);
    public Color filledColor = new Color(1f, 1f, 1f, 0.11f);
    public Color hoverColor = new Color(1f, 1f, 1f, 0.24f);
    [Range(0f, 1f)] public float draggedIconAlpha = 0.35f; // The source icon while its copy is being dragged

    public int Index { get; private set; }

    private InventoryUI owner;
    private Inventory inventory;
    private bool hovered;

    public void Init(InventoryUI owner, Inventory inventory, int index)
    {
        this.owner = owner;
        this.inventory = inventory;
        Index = index;
        name = "Slot " + index;
        inventory.OnInventoryChanged += OnInventoryChanged;
        Refresh();
    }

    // The window may have closed in the middle of a drag
    void OnEnable()
    {
        if (icon != null) SetIconAlpha(1f);
        hovered = false;
        if (inventory != null) UpdateBackground();
    }

    void OnDestroy()
    {
        if (inventory != null) inventory.OnInventoryChanged -= OnInventoryChanged;
    }

    InventorySlot Slot => inventory.Slots[Index];

    void OnInventoryChanged(int changedIndex)
    {
        if (changedIndex != Index) return;
        Refresh();
        if (hovered) ShowTooltip(); // e.g. something was just dropped here while the pointer is on it
    }

    void Refresh()
    {
        InventorySlot slot = Slot;
        bool empty = slot.IsEmpty;
        icon.sprite = empty ? null : slot.item.icon;
        icon.enabled = icon.sprite != null;
        SetIconAlpha(1f);
        quantityText.text = !empty && slot.quantity > 1 ? slot.quantity.ToString() : "";
        if (categoryBar != null)
        {
            categoryBar.enabled = !empty;
            if (!empty) categoryBar.color = ItemCategoryStyle.Color(slot.item.category);
        }
        UpdateBackground();
    }

    void UpdateBackground()
    {
        if (background != null) background.color = hovered ? hoverColor : Slot.IsEmpty ? emptyColor : filledColor;
    }

    void SetIconAlpha(float alpha)
    {
        Color c = icon.color;
        c.a = alpha;
        icon.color = c;
    }

    void ShowTooltip()
    {
        InventorySlot slot = Slot;
        if (slot.IsEmpty || owner.IsDragging) owner.Tooltip.Hide();
        else owner.Tooltip.Show(slot.item);
    }

    // --------------------------------------------------------------- Hover

    public void OnPointerEnter(PointerEventData eventData)
    {
        hovered = true;
        UpdateBackground();
        ShowTooltip();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        hovered = false;
        UpdateBackground();
        owner.Tooltip.Hide();
    }

    // --------------------------------------------------------------- Drag & drop

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (Slot.IsEmpty || eventData.button != PointerEventData.InputButton.Left)
        {
            eventData.pointerDrag = null; // Nothing to drag: cancel, so no other slot gets a drop from us
            return;
        }
        owner.BeginDrag(this, Slot.item.icon, eventData.position);
        SetIconAlpha(draggedIconAlpha);
    }

    public void OnDrag(PointerEventData eventData) => owner.MoveDrag(eventData.position);

    // Called after OnDrop of the target (if any). Dropping anywhere else does nothing: the item stays.
    public void OnEndDrag(PointerEventData eventData)
    {
        owner.EndDrag();
        SetIconAlpha(1f);
    }

    public void OnDrop(PointerEventData eventData)
    {
        SlotUI from = eventData.pointerDrag != null ? eventData.pointerDrag.GetComponent<SlotUI>() : null;
        if (from == null || from == this || from.inventory != inventory) return;
        inventory.HandleSlotDrop(from.Index, Index);
    }
}
