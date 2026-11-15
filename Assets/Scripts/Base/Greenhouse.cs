using UnityEngine;

// Grows food under glass. Every growing cycle it uses a little water from the base stock and, if the base has
// power, adds produce to its tray (up to a limit); F harvests the tray into the inventory.
// The crops follow NASA's plant growth experiments (Veggie / APH on the ISS, NASA OSDR): leafy greens grow fast,
// potatoes are slower but much more filling.
public class Greenhouse : MonoBehaviour, IInteractable
{
    public ItemDefinition fastCrop;   // Lettuce
    public ItemDefinition slowCrop;   // Potato
    public float cycleSeconds = 90f;
    public float waterPerCycle = 1f;
    public int trayLimit = 12;
    public Transform plants;          // Grows taller as the tray fills (optional)

    private int fast, slow, cycles;
    private float timer;

    public string Status { get; private set; } = "Growing";

    void Update()
    {
        BaseSupplies supplies = BaseSupplies.Instance;
        if (fast + slow >= trayLimit) { Status = "Tray full - harvest"; return; }
        if (supplies != null && !supplies.HasPower) { Status = "No power"; return; }
        Status = "Growing";
        timer += Time.deltaTime;
        if (timer < cycleSeconds) return;
        timer = 0f;
        if (supplies != null && !supplies.TakeWater(waterPerCycle)) { Status = "Needs water in the base stock"; return; }
        cycles++;
        if (fastCrop != null) fast += 2;
        if (slowCrop != null && cycles % 2 == 0) slow += 1; // Potatoes take twice as long
        if (plants != null) plants.localScale = new Vector3(1f, Mathf.Lerp(0.3f, 1f, (fast + slow) / (float)trayLimit), 1f);
    }

    public string GetPrompt()
    {
        int ready = fast + slow;
        return ready > 0 ? $"Harvest greenhouse ({ready})" : $"Greenhouse · {Status} {Mathf.CeilToInt(cycleSeconds - timer)} s";
    }

    public InteractionAnimation GetAnimationType() => InteractionAnimation.PickUp;
    public bool CanInteract(InteractionSystem actor) => actor.Inventory != null;

    public void OnInteractionResolved(InteractionSystem actor)
    {
        if (fast + slow == 0) { actor.ShowMessage($"Greenhouse: {Status}"); return; }
        int leftFast = fastCrop != null ? actor.Inventory.TryAddItem(fastCrop, fast) : 0;
        int leftSlow = slowCrop != null ? actor.Inventory.TryAddItem(slowCrop, slow) : 0;
        int taken = fast - leftFast + slow - leftSlow;
        MissionLog.CropsHarvested += taken;
        if (taken > 0)
            ScienceCards.Show("harvest");
        fast = leftFast;
        slow = leftSlow;
        actor.ShowMessage(taken > 0 ? $"Harvested {taken} crops" : "Inventory full");
        if (plants != null) plants.localScale = new Vector3(1f, Mathf.Lerp(0.3f, 1f, (fast + slow) / (float)trayLimit), 1f);
    }
}
