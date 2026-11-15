using UnityEngine;

// The crew's pantry in the hub: F stores every food, water and oxygen consumable you carry into the crew stock
// (BaseSupplies). The prompt shows how long the stock lasts.
public class SupplyLocker : MonoBehaviour, IInteractable
{
    public string GetPrompt()
    {
        BaseSupplies s = BaseSupplies.Instance;
        if (s == null) return "Store crew supplies";
        return $"Store crew supplies   Food {s.Food:0} · Water {s.Water:0} · O2 {s.Oxygen:0}";
    }

    public InteractionAnimation GetAnimationType() => InteractionAnimation.None;
    public bool CanInteract(InteractionSystem actor) => BaseSupplies.Instance != null && actor.Inventory != null;
    public void OnInteractionResolved(InteractionSystem actor) => actor.ShowMessage(BaseSupplies.Instance.DepositFrom(actor.Inventory));
}
