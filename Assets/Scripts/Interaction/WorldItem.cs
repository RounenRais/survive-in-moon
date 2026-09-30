using UnityEngine;

// An item lying on the ground. F near it: bend down and put it in the inventory.
// Needs a collider on this object or a child. The colliders are made triggers: the InteractionSystem still finds
// them, but the camera doesn't bump into them (it would jump in front of the item) and the player walks through.
public class WorldItem : MonoBehaviour, IInteractable
{
    public ItemDefinition item;
    [Min(1)] public int quantity = 1;

    void Awake()
    {
        foreach (Collider c in GetComponentsInChildren<Collider>()) c.isTrigger = true;
    }

    public string GetPrompt()
    {
        if (item == null) return "";
        return quantity > 1 ? $"Pick up {item.displayName} x{quantity}" : $"Pick up {item.displayName}";
    }

    public InteractionAnimation GetAnimationType() => InteractionAnimation.PickUp;

    public bool CanInteract(InteractionSystem actor) => item != null && quantity > 0 && actor.Inventory != null;

    public void OnInteractionResolved(InteractionSystem actor)
    {
        int remaining = actor.Inventory.TryAddItem(item, quantity);
        if (remaining == 0)
        {
            Destroy(gameObject);
            return;
        }
        // Didn't fit (or only partly): the rest stays on the ground
        quantity = remaining;
        actor.ShowMessage("Inventory Full");
    }
}
