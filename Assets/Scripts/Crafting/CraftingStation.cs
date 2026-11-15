using UnityEngine;

// The workbench in the base: F opens the crafting window (CraftingUI).
public class CraftingStation : MonoBehaviour, IInteractable
{
    public string prompt = "Use Workbench";

    public string GetPrompt() => prompt;
    public InteractionAnimation GetAnimationType() => InteractionAnimation.None;
    public bool CanInteract(InteractionSystem actor) => !CraftingUI.IsOpen && actor.Inventory != null;
    public void OnInteractionResolved(InteractionSystem actor) => CraftingUI.Open(actor.Inventory);
}
