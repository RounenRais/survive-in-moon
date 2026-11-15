using System.Collections.Generic;
using UnityEngine;

// A crate that holds items (starting supplies at the base, salvage in the wreck sites). F opens its window
// (CrateUI), where you take items one by one or all at once. The cargo pod also carries the rover blueprint:
// opening it unlocks the Rover Garage on the build terminal.
public class SupplyCrate : MonoBehaviour, IInteractable
{
    public string displayName = "Supply crate";
    public List<ItemAmount> contents = new List<ItemAmount>();
    public bool unlocksRover;
    public bool isStarterCrate;
    public GameObject lid; // Kept for older scenes; not animated

    public bool IsEmpty
    {
        get
        {
            foreach (ItemAmount a in contents) if (a.item != null && a.quantity > 0) return false;
            return true;
        }
    }

    public string GetPrompt() => IsEmpty ? $"{Title} (empty)" : $"Open {Title}";
    public string Title => string.IsNullOrEmpty(displayName) ? "crate" : char.ToUpperInvariant(displayName[0]) + displayName.Substring(1);
    public InteractionAnimation GetAnimationType() => InteractionAnimation.None;
    public bool CanInteract(InteractionSystem actor) => actor.Inventory != null && !CrateUI.IsOpen;

    public void OnInteractionResolved(InteractionSystem actor)
    {
        if (isStarterCrate) Progress.CrateOpened = true;
        if (unlocksRover && !Progress.RoverUnlocked)
        {
            Progress.RoverUnlocked = true;
            actor.ShowMessage("Rover blueprint found - the Rover Garage is on the build terminal now");
        }
        CrateUI.Open(this, actor.Inventory);
    }

    // Moves one stack (or what fits) into the inventory. Returns how many were taken.
    public int Take(int index, Inventory inventory)
    {
        if (index < 0 || index >= contents.Count || inventory == null) return 0;
        ItemAmount a = contents[index];
        if (a.item == null || a.quantity <= 0) return 0;
        int left = inventory.TryAddItem(a.item, a.quantity);
        int taken = a.quantity - left;
        a.quantity = left;
        contents[index] = a;
        if (taken > 0 && IsEmpty && !isStarterCrate) Progress.WrecksSalvaged++;
        return taken;
    }
}
