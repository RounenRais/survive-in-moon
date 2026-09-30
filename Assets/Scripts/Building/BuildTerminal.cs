using UnityEngine;

// The screen standing outside at the base. F opens the build menu (BuildMenuUI).
public class BuildTerminal : MonoBehaviour, IInteractable
{
    public string prompt = "Use Build Terminal";

    public string GetPrompt() => prompt;
    public InteractionAnimation GetAnimationType() => InteractionAnimation.None;
    public bool CanInteract(InteractionSystem actor) =>
        BuildManager.Instance != null && !BuildManager.Instance.IsPlacing && !BuildMenuUI.IsOpen;

    public void OnInteractionResolved(InteractionSystem actor) => BuildMenuUI.Open();
}
