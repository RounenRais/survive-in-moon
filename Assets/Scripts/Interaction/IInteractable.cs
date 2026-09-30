// Which body animation plays for an interaction. The InteractionSystem starts it; the "contact" moment of the
// animation (hands touch the object) calls OnInteractionResolved.
public enum InteractionAnimation
{
    None,   // No animation: resolved right away
    Craft,  // Crouch and work with the hands (stays until the player moves)
    PickUp  // Short bend down to the ground
}

// Anything the player can use with the interact key (F): items on the ground, crafting spots, ...
// Implement it on a MonoBehaviour that has (or whose children have) a collider, so the InteractionSystem finds it.
public interface IInteractable
{
    string GetPrompt();                               // Text shown on screen, e.g. "Pick up Moon Rock x3"
    InteractionAnimation GetAnimationType();
    bool CanInteract(InteractionSystem actor);        // Can it be used right now?
    void OnInteractionResolved(InteractionSystem actor); // The actual effect (add item, use resources, ...)
}
