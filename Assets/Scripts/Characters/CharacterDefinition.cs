using UnityEngine;

// One playable character: which prefab to show and what to write about it on the character select screen.
// Create one with: right click in the Project window > Create > Survive In Moon > Character
// (Survive In Moon > Setup Character Switching makes one for every astronaut prefab automatically.)
[CreateAssetMenu(menuName = "Survive In Moon/Character", fileName = "NewCharacter")]
public class CharacterDefinition : ScriptableObject
{
    [Tooltip("Unique key saved when the player picks this character (don't change it after release)")]
    public string id;
    public string displayName;
    [TextArea] public string description;
    [Tooltip("Picture for the character select screen (optional)")]
    public Sprite icon;

    [Tooltip("The astronaut prefab (model + Animator). Its scripts and colliders are ignored on the player.")]
    public GameObject prefab;
    [Tooltip("Turns the model around its vertical axis, if it doesn't face forward")]
    public float yawOffset = 0f;
}
