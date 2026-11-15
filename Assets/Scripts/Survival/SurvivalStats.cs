using System;
using System.Collections.Generic;
using UnityEngine;

// The player's body and suit. Added to the player by PlayerMovement.
//  Oxygen    - refilled inside the base's life support; outside it drains, faster the farther you go
//  Food      - drains slowly (faster when sprinting); eat food from the greenhouse / rations
//  Water     - drains a bit faster than food; drink water packs (made from ice)
//  Warmth    - drops in shadow outside the base (the lunar shade is around -170 °C), recovers in sunlight
//  Stamina   - sprinting uses it, it comes back when walking or standing
//  Radiation - absorbed dose: grows outside the base, much faster during a solar storm (NASA DONKI data)
//  Health    - hurt when oxygen/food/water/warmth run out or the dose gets too high; heals when you're fine
// Carried Equipment items raise the limits (bigger O2 tank, suit liner, shielding...); consumables restore stats
// and can give timed boosts. At 0 health you black out and wake up in the base.
public class SurvivalStats : MonoBehaviour
{
    public enum Stat { Oxygen, Food, Water, Warmth, Stamina, Radiation, Health }

    [Header("Oxygen")]
    public float oxygenMax = 100f;
    public float oxygenRefill = 25f;       // Per second inside life support
    public float oxygenBaseDrain = 0.35f;  // Per second just outside the base
    public float oxygenDistanceDrain = 0.9f; // Extra per second for every 1000 units away from the base edge
    public float deepZone = 650f;          // Beyond this (from the base edge) walking burns oxygen 3x: take the rover
    public float deepZoneFactor = 3f;

    [Header("Food & water (per second)")]
    public float foodDrain = 100f / 900f;  // Full -> empty in 15 minutes
    public float waterDrain = 100f / 600f; // Full -> empty in 10 minutes

    [Header("Warmth (per second)")]
    public float coldInShadow = 1.2f;
    public float warmInSun = 0.6f;
    public float warmInBase = 4f;

    [Header("Stamina (per second)")]
    public float sprintCost = 2f; // Barely tiring (presentation build)
    public float staminaRegen = 9f;

    [Header("Radiation (dose per second)")]
    public float cosmicDose = 0.02f;   // Galactic cosmic rays, always there outside
    public float stormDose = 0.6f;     // Extra at storm strength 1
    public float baseDoseRecovery = 0.25f; // The habitat's medical bay slowly brings it down

    [Header("Health (per second)")]
    public float suffocation = 6f;
    public float starvation = 0.4f;
    public float dehydration = 0.6f;
    public float freezing = 1.2f;
    public float radiationSickness = 0.6f;
    public float healing = 0.8f;

    public float Oxygen { get; private set; }
    public float Food { get; private set; } = 100f;
    public float Water { get; private set; } = 100f;
    public float Warmth { get; private set; } = 100f;
    public float Stamina { get; private set; } = 100f;
    public float Radiation { get; private set; }
    public float Health { get; private set; } = 100f;

    public float OxygenCapacity => oxygenMax * (1f + bonusOxygen);
    public bool InLifeSupport { get; private set; } = true;
    public bool InShadow { get; private set; }
    public float DistanceFromBase { get; private set; }
    public float OxygenSecondsLeft { get; private set; } = float.PositiveInfinity;
    public float SpeedMultiplier => 1f + bonusSpeed;
    public bool CanSprint => Stamina > 5f;
    public bool IsDead { get; private set; }
    public bool InDeepZone => DistanceFromBase > deepZone;

    public static SurvivalStats Player { get; private set; }
    public event Action<string> Notice;    // Short warnings ("Oxygen low", "You blacked out")
    public event Action BlackedOut;

    private PlayerMovement movement;
    private Inventory inventory;
    private float bonusOxygen, bonusCold, bonusRadiation, bonusSpeed, bonusEfficiency;
    private readonly Dictionary<BoostType, float> boosts = new Dictionary<BoostType, float>();
    private float nextShadowCheck;
    private readonly HashSet<Stat> warned = new HashSet<Stat>();

    void Awake()
    {
        Player = this;
        Oxygen = oxygenMax;
    }

    void OnDestroy() { if (Player == this) Player = null; }

    void Start()
    {
        // Real lunar radiation (NASA RadLab, LRO/CRaTER): the everyday dose follows the measured median, and the
        // worst measured hours set how much stronger a storm gets
        if (NasaData.HasRadiation)
        {
            cosmicDose = 0.02f * Mathf.Clamp(NasaData.Info.moonDoseMedian / 11.4f, 0.5f, 2f);
            stormDose = cosmicDose * NasaData.StormMultiplier;
        }
        movement = GetComponent<PlayerMovement>();
        inventory = GetComponent<Inventory>();
        if (inventory != null) inventory.OnInventoryChanged += _ => RefreshEquipment();
        RefreshEquipment();
    }

    public float Get(Stat stat) => stat switch
    {
        Stat.Oxygen => Oxygen,
        Stat.Food => Food,
        Stat.Water => Water,
        Stat.Warmth => Warmth,
        Stat.Stamina => Stamina,
        Stat.Radiation => Radiation,
        _ => Health
    };

    public float Max(Stat stat) => stat == Stat.Oxygen ? OxygenCapacity : 100f;

    public bool HasBoost(BoostType type) => boosts.TryGetValue(type, out float until) && until > Time.time;
    public float BoostSecondsLeft(BoostType type) => boosts.TryGetValue(type, out float until) ? Mathf.Max(0f, until - Time.time) : 0f;

    // --------------------------------------------------------------- Every frame

    void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f || IsDead) return;
        UpdateEnvironment();
        bool driving = movement != null && movement.InVehicle;

        float efficiency = 1f - bonusEfficiency;
        if (HasBoost(BoostType.Metabolism)) efficiency *= 0.5f;
        bool sprinting = movement != null && movement.IsSprinting;

        // Oxygen
        // Short on power: life support refills slower
        float lifeSupport = BaseSupplies.Instance != null && !BaseSupplies.Instance.HasPower ? 0.3f : 1f;
        if (InLifeSupport) Oxygen = Mathf.Min(OxygenCapacity, Oxygen + oxygenRefill * lifeSupport * dt);
        else
        {
            float drain = oxygenBaseDrain + oxygenDistanceDrain * DistanceFromBase / 1000f;
            if (sprinting) drain *= 1.4f; // Heavy breathing
            if (InDeepZone) drain *= deepZoneFactor;
            if (driving) drain = 0.05f;   // The rover's cabin supply keeps you breathing
            Oxygen = Mathf.Max(0f, Oxygen - drain * dt);
            OxygenSecondsLeft = Oxygen / drain;
        }
        if (InLifeSupport) OxygenSecondsLeft = float.PositiveInfinity;

        // Food and water
        float work = sprinting ? 1.6f : 1f;
        Food = Mathf.Max(0f, Food - foodDrain * work * efficiency * dt);
        Water = Mathf.Max(0f, Water - waterDrain * work * efficiency * dt);

        // Warmth
        if (InLifeSupport) Warmth = Mathf.Min(100f, Warmth + warmInBase * dt);
        else if (InShadow && !driving)
        {
            float cold = coldInShadow * (1f - bonusCold);
            if (HasBoost(BoostType.Warmth)) cold *= 0.3f;
            Warmth = Mathf.Max(0f, Warmth - cold * dt);
        }
        else Warmth = Mathf.Min(100f, Warmth + warmInSun * dt);

        // Stamina
        if (sprinting)
        {
            float cost = sprintCost * (HasBoost(BoostType.Stamina) ? 0.35f : 1f);
            Stamina = Mathf.Max(0f, Stamina - cost * dt);
        }
        else Stamina = Mathf.Min(100f, Stamina + staminaRegen * dt);

        // Radiation dose
        if (InLifeSupport) Radiation = Mathf.Max(0f, Radiation - baseDoseRecovery * dt);
        else
        {
            float dose = cosmicDose + stormDose * SolarStorms.Strength;
            Radiation = Mathf.Min(100f, Radiation + dose * (1f - bonusRadiation) * dt);
        }

        // Health
        float damage = 0f;
        if (Oxygen <= 0f) damage += suffocation;
        if (Food <= 0f) damage += starvation;
        if (Water <= 0f) damage += dehydration;
        if (Warmth <= 0f) damage += freezing;
        if (Radiation >= 80f) damage += radiationSickness * Mathf.InverseLerp(80f, 100f, Radiation) + radiationSickness * 0.5f;
        if (damage > 0f) Health = Mathf.Max(0f, Health - damage * dt);
        if (Health <= 0f) { Die(); return; }
        if (damage > 0f) { }
        else if (Oxygen > 20f && Food > 25f && Water > 25f && Warmth > 25f && Radiation < 60f)
            Health = Mathf.Min(100f, Health + healing * dt);

        Warnings();
    }

    void UpdateEnvironment()
    {
        BaseArea area = BaseArea.Main;
        if (area != null)
        {
            float fromCenter = Vector3.ProjectOnPlane(transform.position - area.transform.position, Vector3.up).magnitude;
            DistanceFromBase = Mathf.Max(0f, fromCenter - area.radius);
            InLifeSupport = DistanceFromBase <= 0f;
        }
        else { DistanceFromBase = 0f; InLifeSupport = true; }

        // In shadow = something blocks the way to the Sun (checked a few times a second)
        if (Time.time >= nextShadowCheck)
        {
            nextShadowCheck = Time.time + 0.4f;
            Light sun = RenderSettings.sun;
            InShadow = false;
            if (sun != null)
            {
                Vector3 from = transform.position + Vector3.up * 8f;
                foreach (RaycastHit hit in Physics.RaycastAll(from, -sun.transform.forward, 800f, ~0, QueryTriggerInteraction.Ignore))
                    if (!hit.collider.transform.IsChildOf(transform)) { InShadow = true; break; }
            }
        }
    }

    void Warnings()
    {
        Warn(Stat.Oxygen, Oxygen / OxygenCapacity < 0.25f, "Oxygen low - head back to the base");
        Warn(Stat.Food, Food < 20f, "You are hungry");
        Warn(Stat.Water, Water < 20f, "You are thirsty");
        Warn(Stat.Warmth, Warmth < 25f, "Freezing - get into the sunlight");
        Warn(Stat.Radiation, Radiation > 60f, "Radiation dose high");
        Warn(Stat.Health, Health < 30f, "Health critical");
    }

    void Warn(Stat stat, bool low, string text)
    {
        if (low && warned.Add(stat)) Notice?.Invoke(text);
        else if (!low) warned.Remove(stat);
    }

    // Health ran out: everything stops and the death panel asks to respawn (the inventory is kept)
    void Die()
    {
        IsDead = true;
        if (movement != null && movement.InVehicle && movement.Vehicle != null) movement.Vehicle.Exit();
        BlackedOut?.Invoke();
        DeathUI.Show();
    }

    // Direct damage (falls, hazards, tests)
    public void Hurt(float amount) => Health = Mathf.Max(0f, Health - amount);

    // Wake up in the base with full air, the bag untouched
    public void Respawn()
    {
        IsDead = false;
        Health = 70f;
        Stamina = 100f;
        Oxygen = OxygenCapacity;
        Food = Mathf.Max(Food, 40f);
        Water = Mathf.Max(Water, 40f);
        Warmth = 100f;
        Radiation = Mathf.Min(Radiation, 30f);

        BaseArea area = BaseArea.Main;
        if (area != null)
        {
            Vector3 spot = BaseArea.GroundPoint(area.transform.position + new Vector3(30f, 0f, 30f)) + Vector3.up * 2f;
            var cc = GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            transform.position = spot;
            if (cc != null) cc.enabled = true;
        }
    }

    // --------------------------------------------------------------- Items

    // Uses one consumable. Returns false (and says why) if it would do nothing.
    public bool Consume(ItemDefinition item)
    {
        if (item == null || !item.IsConsumable) return false;
        bool useful = item.restoreFood > 0f && Food < 99f || item.restoreWater > 0f && Water < 99f ||
                      item.restoreOxygen > 0f && Oxygen < OxygenCapacity - 1f || item.restoreHealth > 0f && Health < 99f ||
                      item.restoreStamina > 0f && Stamina < 99f || item.reduceRadiation > 0f && Radiation > 1f ||
                      item.boost != BoostType.None;
        if (!useful) { Notice?.Invoke("You don't need that now"); return false; }

        Food = Mathf.Min(100f, Food + item.restoreFood);
        Water = Mathf.Min(100f, Water + item.restoreWater);
        Oxygen = Mathf.Min(OxygenCapacity, Oxygen + item.restoreOxygen);
        Health = Mathf.Min(100f, Health + item.restoreHealth);
        Stamina = Mathf.Min(100f, Stamina + item.restoreStamina);
        Radiation = Mathf.Max(0f, Radiation - item.reduceRadiation);
        if (item.boost != BoostType.None && item.boostSeconds > 0f)
            boosts[item.boost] = Mathf.Max(Time.time, boosts.TryGetValue(item.boost, out float until) ? until : 0f) + item.boostSeconds;
        return true;
    }

    // The best bonus of each kind among the carried equipment (two tanks don't stack)
    void RefreshEquipment()
    {
        bonusOxygen = bonusCold = bonusRadiation = bonusSpeed = bonusEfficiency = 0f;
        if (inventory == null) return;
        foreach (InventorySlot slot in inventory.Slots)
        {
            ItemDefinition item = slot.item;
            if (item == null || slot.IsEmpty || item.category != ItemCategory.Equipment) continue;
            bonusOxygen = Mathf.Max(bonusOxygen, item.oxygenCapacity);
            bonusCold = Mathf.Max(bonusCold, item.coldResistance);
            bonusRadiation = Mathf.Max(bonusRadiation, item.radiationResistance);
            bonusSpeed = Mathf.Max(bonusSpeed, item.moveSpeed);
            bonusEfficiency = Mathf.Max(bonusEfficiency, item.efficiency);
        }
        bonusCold = Mathf.Clamp01(bonusCold);
        bonusRadiation = Mathf.Clamp01(bonusRadiation);
        bonusEfficiency = Mathf.Clamp(bonusEfficiency, 0f, 0.8f);
        Oxygen = Mathf.Min(Oxygen, OxygenCapacity);
    }
}
