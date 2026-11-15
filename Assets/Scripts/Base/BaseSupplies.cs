using UnityEngine;

// What keeps the base and the crew alive:
//  Power  - every building with a PowerNode adds (solar panel, helium-3 generator, the hub's own cells) or uses
//           (habitat, greenhouse, garage...) kilowatts. Short on power: the greenhouse stops and life support
//           refills oxygen slower.
//  Food / Water / Oxygen for the crew - stored in the hub. The crew (everyone except the player) eats and drinks
//           from these over time; fill them at the Supply Locker in the hub (F) with food, water packs and O2.
//           A hungry or thirsty crew builds much slower.
public class BaseSupplies : MonoBehaviour
{
    public static BaseSupplies Instance { get; private set; }

    [Header("Crew use per crew member, units per minute")]
    public float foodPerMinute = 0.35f;
    public float waterPerMinute = 0.5f;
    public float oxygenPerMinute = 0.4f;

    [Header("Starting stock (units)")]
    public float food = 12f;
    public float water = 14f;
    public float oxygen = 16f;
    public float capacity = 60f;

    public float Food => food;
    public float Water => water;
    public float Oxygen => oxygen;
    public float Capacity => capacity;
    public float PowerProduced { get; private set; }
    public float PowerUsed { get; private set; }
    public float PowerBalance => PowerProduced - PowerUsed;
    public bool HasPower => PowerBalance >= 0f;
    public int CrewCount { get; private set; }

    // 1 = normal; a crew without food, water or air works at 30%
    public float CrewWorkRate => (food > 0f && water > 0f && oxygen > 0f) ? 1f : 0.3f;

    // Minutes the stock lasts for the current crew (infinity without crew)
    public float MinutesLeft(float stock, float perMinute) => CrewCount > 0 ? stock / (perMinute * CrewCount) : float.PositiveInfinity;

    private float nextPowerCheck;

    void Awake() => Instance = this;
    void OnDestroy() { if (Instance == this) Instance = null; }

    void Update()
    {
        float minutes = Time.deltaTime / 60f;
        CrewCount = CrewManager.Instance != null ? Mathf.Max(0, CrewManager.Instance.Members.Count - 1) : 0;
        // The crew's own food and air live on each crew mate now (CrewNeeds); the base stock only waters plants

        if (Time.time >= nextPowerCheck)
        {
            nextPowerCheck = Time.time + 1f;
            float produced = 0f, used = 0f;
            foreach (PowerNode node in PowerNode.All)
            {
                if (node.kilowatts >= 0f) produced += node.kilowatts;
                else used -= node.kilowatts;
            }
            PowerProduced = produced;
            PowerUsed = used;
        }
    }

    // Moves every food/water/oxygen consumable from the inventory into the crew stock. Returns a short summary.
    public string DepositFrom(Inventory inventory)
    {
        if (inventory == null) return "Nothing to store";
        int stored = 0;
        for (int i = 0; i < inventory.Slots.Count; i++)
        {
            InventorySlot slot = inventory.Slots[i];
            if (slot.IsEmpty || !slot.item.IsConsumable) continue;
            ItemDefinition item = slot.item;
            if (item.restoreFood <= 0f && item.restoreWater <= 0f && item.restoreOxygen <= 0f) continue;
            while (!slot.IsEmpty && slot.item == item && HasRoomFor(item))
            {
                food = Mathf.Min(capacity, food + item.restoreFood / 20f);
                water = Mathf.Min(capacity, water + item.restoreWater / 20f);
                oxygen = Mathf.Min(capacity, oxygen + item.restoreOxygen / 20f);
                inventory.RemoveFromSlot(i, 1);
                stored++;
            }
        }
        return stored > 0 ? $"Stored {stored} supplies for the crew" : "No food, water or oxygen to store";
    }

    bool HasRoomFor(ItemDefinition item) =>
        item.restoreFood > 0f && food < capacity - 0.5f || item.restoreWater > 0f && water < capacity - 0.5f ||
        item.restoreOxygen > 0f && oxygen < capacity - 0.5f;

    // The greenhouse waters its plants from the stock
    public bool TakeWater(float units)
    {
        if (water < units) return false;
        water -= units;
        return true;
    }
}
