using System.Collections.Generic;
using UnityEngine;

// A deposit in the world (regolith rocks, ilmenite, water ice, helium-3, crash debris). F gathers a few items
// per go; some need a tool in the inventory (e.g. the drill for ilmenite and ice). When it runs out it hides
// and grows back after a while. Placed by Survive In Moon > Build Game Content.
public class ResourceNode : MonoBehaviour, IInteractable
{
    public string displayName = "Deposit";
    public ItemDefinition item;
    public Vector2Int yieldPerGather = new Vector2Int(1, 3);
    public int gathers = 4;               // How many times F works before it is empty
    public ItemDefinition requiredTool;   // Optional
    public float respawnSeconds = 240f;
    public GameObject visual;             // Hidden while empty

    private static readonly List<ResourceNode> all = new List<ResourceNode>();
    public static IReadOnlyList<ResourceNode> All => all;
    public bool IsEmpty => left <= 0;

    private int left;
    private float refillAt;
    private Collider[] colliders;

    void Awake()
    {
        left = gathers;
        colliders = GetComponentsInChildren<Collider>();
    }

    void OnEnable() => all.Add(this);
    void OnDisable() => all.Remove(this);

    void Update()
    {
        if (left > 0 || Time.time < refillAt) return;
        left = gathers;
        Show(true);
    }

    public string GetPrompt()
    {
        string name = item != null ? item.displayName : displayName;
        if (requiredTool != null && !HasTool()) return $"{displayName} · needs {requiredTool.displayName}";
        return $"Gather {name}";
    }

    public InteractionAnimation GetAnimationType() => InteractionAnimation.PickUp;

    public bool CanInteract(InteractionSystem actor) => left > 0 && item != null && actor.Inventory != null;

    public void OnInteractionResolved(InteractionSystem actor)
    {
        if (requiredTool != null && !actor.Inventory.HasItem(requiredTool, 1))
        {
            actor.ShowMessage($"You need a {requiredTool.displayName}");
            return;
        }
        int amount = Random.Range(yieldPerGather.x, yieldPerGather.y + 1);
        int notAdded = actor.Inventory.TryAddItem(item, amount);
        if (notAdded >= amount) { actor.ShowMessage("Inventory full"); return; }
        actor.ShowMessage($"+{amount - notAdded} {item.displayName}");
        left--;
        if (left <= 0)
        {
            refillAt = Time.time + respawnSeconds;
            Show(false);
        }
    }

    bool HasTool()
    {
        SurvivalStats p = SurvivalStats.Player;
        Inventory inv = p != null ? p.GetComponent<Inventory>() : null;
        return inv != null && inv.HasItem(requiredTool, 1);
    }

    void Show(bool on)
    {
        if (visual != null) visual.SetActive(on);
        foreach (Collider c in colliders) if (c != null) c.enabled = on;
    }
}
