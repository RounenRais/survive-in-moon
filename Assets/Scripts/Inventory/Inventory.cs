using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class InventorySlot
{
    public ItemDefinition item;
    public int quantity;

    public bool IsEmpty => item == null || quantity <= 0;

    public void Clear()
    {
        item = null;
        quantity = 0;
    }
}

// What gets saved: only item ids and amounts, one entry per slot (empty slots have an empty id)
[Serializable]
public struct InventorySaveData
{
    public SavedSlot[] slots;
}

[Serializable]
public struct SavedSlot
{
    public string itemId;
    public int quantity;
}

// Fixed 24 slots with stacking. Knows nothing about UI: the UI listens to OnInventoryChanged and calls
// HandleSlotDrop; all the rules live here.
public class Inventory : MonoBehaviour
{
    public const int SlotCount = 24;

    [SerializeField] private InventorySlot[] slots = new InventorySlot[SlotCount];

    public event Action<int> OnInventoryChanged; // Index of the slot that changed (fires once per changed slot)

    public IReadOnlyList<InventorySlot> Slots => slots;

    void Awake()
    {
        if (slots == null || slots.Length != SlotCount) Array.Resize(ref slots, SlotCount);
        for (int i = 0; i < slots.Length; i++)
            if (slots[i] == null) slots[i] = new InventorySlot();
    }

    // Fills existing stacks of the same item first, then empty slots.
    // Returns how many did NOT fit (0 = everything added).
    public int TryAddItem(ItemDefinition item, int quantity)
    {
        if (item == null || quantity <= 0) return Mathf.Max(0, quantity);
        int maxStack = item.MaxStack;
        int remaining = quantity;

        for (int i = 0; i < slots.Length && remaining > 0; i++)
        {
            InventorySlot slot = slots[i];
            if (slot.IsEmpty || slot.item != item || slot.quantity >= maxStack) continue;
            int added = Mathf.Min(maxStack - slot.quantity, remaining);
            slot.quantity += added;
            remaining -= added;
            OnInventoryChanged?.Invoke(i);
        }
        for (int i = 0; i < slots.Length && remaining > 0; i++)
        {
            InventorySlot slot = slots[i];
            if (!slot.IsEmpty) continue;
            int added = Mathf.Min(maxStack, remaining);
            slot.item = item;
            slot.quantity = added;
            remaining -= added;
            OnInventoryChanged?.Invoke(i);
        }
        return remaining;
    }

    // Removes only if there is enough; returns false and changes nothing otherwise.
    // Takes from the last slots first, so the first stacks stay full.
    public bool RemoveItem(ItemDefinition item, int quantity)
    {
        if (item == null || quantity <= 0) return quantity <= 0;
        if (!HasItem(item, quantity)) return false;

        int remaining = quantity;
        for (int i = slots.Length - 1; i >= 0 && remaining > 0; i--)
        {
            InventorySlot slot = slots[i];
            if (slot.IsEmpty || slot.item != item) continue;
            int taken = Mathf.Min(slot.quantity, remaining);
            slot.quantity -= taken;
            remaining -= taken;
            if (slot.quantity == 0) slot.Clear();
            OnInventoryChanged?.Invoke(i);
        }
        return true;
    }

    // A slot was dragged onto another one in the UI:
    //  - same stackable item: merge as much as fits, the rest stays in the source slot
    //  - otherwise (empty target, different item, same non-stackable item): swap the two slots
    public void HandleSlotDrop(int fromIndex, int toIndex)
    {
        if (fromIndex == toIndex || !IsValidIndex(fromIndex) || !IsValidIndex(toIndex)) return;
        InventorySlot from = slots[fromIndex], to = slots[toIndex];
        if (from.IsEmpty) return;

        if (!to.IsEmpty && to.item == from.item && from.item.MaxStack > 1)
        {
            int moved = Mathf.Min(from.item.MaxStack - to.quantity, from.quantity);
            if (moved <= 0) return; // Target stack is already full
            to.quantity += moved;
            from.quantity -= moved;
            if (from.quantity == 0) from.Clear();
        }
        else
        {
            (from.item, to.item) = (to.item, from.item);
            (from.quantity, to.quantity) = (to.quantity, from.quantity);
        }
        OnInventoryChanged?.Invoke(fromIndex);
        OnInventoryChanged?.Invoke(toIndex);
    }

    public bool IsValidIndex(int index) => index >= 0 && index < slots.Length;

    public bool HasItem(ItemDefinition item, int quantity) => CountOf(item) >= quantity;

    public int CountOf(ItemDefinition item)
    {
        int count = 0;
        foreach (InventorySlot slot in slots)
            if (!slot.IsEmpty && slot.item == item) count += slot.quantity;
        return count;
    }

    // How many more of this item would fit
    public int SpaceFor(ItemDefinition item)
    {
        if (item == null) return 0;
        int space = 0;
        foreach (InventorySlot slot in slots)
        {
            if (slot.IsEmpty) space += item.MaxStack;
            else if (slot.item == item) space += Mathf.Max(0, item.MaxStack - slot.quantity);
        }
        return space;
    }

    // --------------------------------------------------------------- Save / load

    public InventorySaveData GetSaveData()
    {
        var data = new InventorySaveData { slots = new SavedSlot[slots.Length] };
        for (int i = 0; i < slots.Length; i++)
            data.slots[i] = slots[i].IsEmpty
                ? new SavedSlot { itemId = "", quantity = 0 }
                : new SavedSlot { itemId = slots[i].item.id, quantity = slots[i].quantity };
        return data;
    }

    // findItem turns a saved id back into its ItemDefinition (e.g. a lookup in an item database)
    public void LoadSaveData(InventorySaveData data, Func<string, ItemDefinition> findItem)
    {
        for (int i = 0; i < slots.Length; i++)
        {
            slots[i].Clear();
            if (data.slots == null || i >= data.slots.Length || string.IsNullOrEmpty(data.slots[i].itemId)) continue;
            ItemDefinition item = findItem(data.slots[i].itemId);
            if (item == null) continue;
            slots[i].item = item;
            slots[i].quantity = Mathf.Min(data.slots[i].quantity, item.MaxStack);
        }
        for (int i = 0; i < slots.Length; i++) OnInventoryChanged?.Invoke(i);
    }
}
