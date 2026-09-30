using UnityEngine;

// Puts the chosen character's model on the player. The player object only has the "logic" (movement, inventory,
// interaction); the look is a child object called "Model" that can be swapped at any time:
//   - at the start of the scene it shows CharacterSelection.Current (what was picked on the select screen)
//   - CharacterSelection.Select(...) or SetCharacter(...) switches it while playing
// Every astronaut uses the same skeleton, so movement, animations and pick up work the same on all of them.
public class PlayerCharacter : MonoBehaviour
{
    [Tooltip("For testing: always use this character instead of the saved choice (leave empty in the real game)")]
    public CharacterDefinition testCharacter;

    [Tooltip("The current model object (a child of the player). Filled automatically.")]
    public Transform model;

    public CharacterDefinition Current { get; private set; }
    public event System.Action<CharacterDefinition> CharacterChanged;

    private bool started;

    void Awake()
    {
        // Before any Start(), so PlayerMovement and AstronautAnimation set themselves up on the right model
        CharacterDefinition character = testCharacter != null ? testCharacter : CharacterSelection.Current;
        if (character != null) SetCharacter(character);
    }

    void Start() => started = true;

    void OnEnable() => CharacterSelection.Changed += SetCharacter;
    void OnDisable() => CharacterSelection.Changed -= SetCharacter;

    public void SetCharacter(CharacterDefinition character)
    {
        if (character == null || character.prefab == null)
        {
            Debug.LogWarning("PlayerCharacter: the character has no prefab.", this);
            return;
        }

        if (model != null)
        {
            // Detach first: Destroy only happens at the end of the frame, and the new model's bones
            // must be the only ones found in the children from now on
            model.gameObject.SetActive(false);
            model.SetParent(null);
            Destroy(model.gameObject);
        }
        model = SpawnModel(character);
        Current = character;

        if (!started) return; // First model: the other scripts haven't started yet and will find it themselves
        PlayerMovement movement = GetComponent<PlayerMovement>();
        if (movement != null) movement.RefreshModel();
        AstronautAnimation body = GetComponent<AstronautAnimation>();
        if (body != null) body.RefreshModel();
        InteractionSystem interaction = GetComponent<InteractionSystem>();
        if (interaction != null) interaction.RefreshAnimator();
        CharacterChanged?.Invoke(character);
    }

    // A copy of the prefab with only its looks and Animator: the prefab's own scripts and colliders are made for
    // standing NPCs and would fight with the player's. It is built inside an inactive parent, so none of them wake up.
    Transform SpawnModel(CharacterDefinition character)
    {
        var holder = new GameObject("Model Holder");
        holder.SetActive(false);
        GameObject copy = Instantiate(character.prefab, holder.transform);
        copy.name = "Model";

        foreach (MonoBehaviour script in copy.GetComponentsInChildren<MonoBehaviour>(true)) DestroyImmediate(script);
        foreach (Collider c in copy.GetComponentsInChildren<Collider>(true)) DestroyImmediate(c);
        foreach (Rigidbody rb in copy.GetComponentsInChildren<Rigidbody>(true)) DestroyImmediate(rb);

        Transform t = copy.transform;
        t.SetParent(transform, false);
        t.localPosition = Vector3.zero;
        t.localRotation = Quaternion.Euler(0f, character.yawOffset, 0f);
        // Same size as the prefab, whatever the scale of the player object is
        Vector3 size = character.prefab.transform.localScale, parent = transform.lossyScale;
        t.localScale = new Vector3(size.x / parent.x, size.y / parent.y, size.z / parent.z);

        Animator animator = copy.GetComponent<Animator>();
        if (animator != null) animator.applyRootMotion = false; // PlayerMovement moves the character

        Destroy(holder);
        return t;
    }
}
