using UnityEngine;
using UnityEngine.UI;

// The inventory window: builds the 24 slots once into the grid (GridLayoutGroup) and owns the drag copy
// and the tooltip, which live on the separate DragLayer canvas above everything.
// Tab opens and closes it. Built by: Survive In Moon > Create Inventory UI
public class InventoryUI : MonoBehaviour
{
    public Inventory inventory; // The player's inventory is found automatically if left empty
    public SlotUI slotPrefab;
    public RectTransform grid;
    public GameObject window;   // Shown/hidden by the toggle key
    public ItemTooltip tooltip;
    public Image dragIcon;      // Semi-transparent copy that follows the pointer while dragging
    public KeyCode toggleKey = KeyCode.Tab;
    public bool startOpen;

    public ItemTooltip Tooltip => tooltip;
    public bool IsOpen => window.activeSelf;
    public bool IsDragging => dragSource != null;

    private SlotUI dragSource;

    void Start()
    {
        dragIcon.raycastTarget = false; // Must not hide the slot under the pointer from the drop
        dragIcon.gameObject.SetActive(false);
        window.SetActive(startOpen);
    }

    void Update()
    {
        // The player's Inventory is added at runtime (PlayerMovement.Start), so it may appear a frame later
        if (inventory == null)
        {
            inventory = FindFirstObjectByType<Inventory>();
            if (inventory != null) BuildSlots();
        }

        if (Input.GetKeyDown(toggleKey)) SetOpen(!IsOpen);
        else if (IsOpen && Input.GetKeyDown(KeyCode.Escape)) SetOpen(false);
    }

    void BuildSlots()
    {
        for (int i = grid.childCount - 1; i >= 0; i--) Destroy(grid.GetChild(i).gameObject); // Editor preview slots
        for (int i = 0; i < inventory.Slots.Count; i++)
            Instantiate(slotPrefab, grid).Init(this, inventory, i);
    }

    public void SetOpen(bool open)
    {
        window.SetActive(open);
        if (!open)
        {
            tooltip.Hide();
            EndDrag();
        }
    }

    // --------------------------------------------------------------- Drag copy

    public void BeginDrag(SlotUI source, Sprite icon, Vector2 pointer)
    {
        dragSource = source;
        tooltip.Hide();
        dragIcon.sprite = icon;
        dragIcon.enabled = icon != null;
        dragIcon.gameObject.SetActive(true);
        MoveDrag(pointer);
    }

    public void MoveDrag(Vector2 pointer)
    {
        if (dragSource != null) dragIcon.rectTransform.position = pointer; // Screen Space Overlay: screen pixels
    }

    public void EndDrag()
    {
        dragSource = null;
        dragIcon.gameObject.SetActive(false);
    }
}
