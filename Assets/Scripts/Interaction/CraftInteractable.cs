using UnityEngine;

// Crouch and work with the hands. Can sit on a workbench in the world, and the InteractionSystem also keeps one
// on the player as the fallback: F with nothing else in reach crafts by hand (the old F behavior).
// Without a recipe it only plays the animation. With a recipe it uses the inputs and adds the output
// when the hands reach the work.
public class CraftInteractable : MonoBehaviour, IInteractable
{
    public string prompt = "Craft";
    public RecipeDefinition recipe; // Optional

    public string GetPrompt()
    {
        if (recipe == null || recipe.outputItem.item == null) return prompt;
        return $"{prompt}: {recipe.outputItem.item.displayName}";
    }

    public InteractionAnimation GetAnimationType() => InteractionAnimation.Craft;

    public bool CanInteract(InteractionSystem actor) => true;

    public void OnInteractionResolved(InteractionSystem actor)
    {
        if (recipe == null || recipe.outputItem.item == null || actor.Inventory == null) return;

        Inventory inventory = actor.Inventory;
        if (!recipe.HasInputs(inventory))
        {
            actor.ShowMessage("Not Enough Materials");
            return;
        }
        if (inventory.SpaceFor(recipe.outputItem.item) < recipe.outputItem.quantity)
        {
            actor.ShowMessage("Inventory Full");
            return;
        }
        foreach (ItemAmount input in recipe.inputItems) inventory.RemoveItem(input.item, input.quantity);
        inventory.TryAddItem(recipe.outputItem.item, recipe.outputItem.quantity);
    }
}
