using UnityEngine;
using UnityEngine.UI;

// A crew mate's own needs: food (feed them: walk up and press F with food in your bag) and oxygen (the base's life
// support refills it while the base has power). Above their head: a face (happy / neutral / sad) with two small
// bars for food and oxygen. A hungry or breathless crew mate builds slowly.
public class CrewNeeds : MonoBehaviour, IInteractable
{
    public float food = 80f;
    public float oxygen = 100f;
    public float foodDrain = 100f / 600f;   // Empty in 10 minutes
    public float oxygenDrain = 100f / 240f; // Without power: empty in 4 minutes
    public float oxygenRefill = 10f;

    public float WorkRate => food > 25f && oxygen > 25f ? 1f : 0.3f;
    public float Mood => Mathf.Min(food, oxygen);

    private CrewMember member;
    private Transform badge;
    private Image face, foodFill, airFill;
    private Sprite happy, neutral, sad;

    void Start()
    {
        member = GetComponent<CrewMember>();
        happy = UITheme.Icon("face_happy");
        neutral = UITheme.Icon("face_neutral");
        sad = UITheme.Icon("face_sad");
        BuildBadge();
    }

    void Update()
    {
        float dt = Time.deltaTime;
        food = Mathf.Max(0f, food - foodDrain * dt);
        if (food < 25f)
        {
            int crew = Mathf.Max(1, CrewManager.Instance != null ? CrewManager.Instance.Members.Count - 1 : 1);
            MissionLog.CrewHungrySeconds += dt / crew;
            ScienceCards.Show("crew");
        }
        bool power = BaseSupplies.Instance == null || BaseSupplies.Instance.HasPower;
        oxygen = power ? Mathf.Min(100f, oxygen + oxygenRefill * dt) : Mathf.Max(0f, oxygen - oxygenDrain * dt);
    }

    void LateUpdate()
    {
        if (badge == null) return;
        Camera cam = Camera.main;
        if (cam != null) badge.rotation = Quaternion.LookRotation(badge.position - cam.transform.position, Vector3.up);
        face.sprite = Mood > 60f ? happy : Mood > 25f ? neutral : sad;
        face.color = Mood > 60f ? UITheme.Good : Mood > 25f ? UITheme.Accent : UITheme.Bad;
        SetFill(foodFill, food);
        SetFill(airFill, oxygen);
    }

    static void SetFill(Image fill, float value)
    {
        fill.fillAmount = value / 100f;
        fill.color = value < 25f ? UITheme.Bad : Color.white;
    }

    // A small world-space canvas above the head: face, then food and oxygen icons with fill bars under them
    void BuildBadge()
    {
        Bounds b = new Bounds(transform.position, Vector3.zero);
        foreach (Renderer r in GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
        var go = new GameObject("Needs Badge", typeof(RectTransform));
        badge = go.transform;
        badge.SetParent(transform, false);
        badge.position = new Vector3(b.center.x, b.max.y + 3f, b.center.z);
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        var rt = (RectTransform)badge;
        rt.sizeDelta = new Vector2(120f, 44f);
        badge.localScale = Vector3.one * 0.07f / Mathf.Max(0.01f, transform.lossyScale.x);

        face = Icon(rt, new Vector2(-40f, 0f), 40f);
        foodFill = Need(rt, "food", new Vector2(8f, 0f), new Color(0.96f, 0.7f, 0.36f));
        airFill = Need(rt, "oxygen", new Vector2(46f, 0f), new Color(0.45f, 0.78f, 1f));
    }

    Image Icon(RectTransform parent, Vector2 pos, float size)
    {
        var go = new GameObject("Icon", typeof(RectTransform));
        var t = (RectTransform)go.transform;
        t.SetParent(parent, false);
        t.anchoredPosition = pos;
        t.sizeDelta = new Vector2(size, size);
        var img = go.AddComponent<Image>();
        img.preserveAspect = true;
        img.raycastTarget = false;
        return img;
    }

    Image Need(RectTransform parent, string icon, Vector2 pos, Color color)
    {
        Image i = Icon(parent, pos + new Vector2(0f, 6f), 24f);
        i.sprite = UITheme.Icon(icon);
        i.color = color;
        Image track = Icon(parent, pos + new Vector2(0f, -14f), 30f);
        track.rectTransform.sizeDelta = new Vector2(30f, 5f);
        track.color = new Color(0f, 0f, 0f, 0.6f);
        Image fill = Icon(parent, pos + new Vector2(0f, -14f), 30f);
        fill.rectTransform.sizeDelta = new Vector2(30f, 5f);
        fill.sprite = UITheme.SkinSprite(UITheme.Skin.Well);
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Horizontal;
        return fill;
    }

    // --------------------------------------------------------------- F: feed them

    public string GetPrompt()
    {
        string name = member != null ? member.displayName : "crew mate";
        return food < 90f ? $"Feed {name}" : $"{name} is fed";
    }

    public InteractionAnimation GetAnimationType() => InteractionAnimation.None;
    public bool CanInteract(InteractionSystem actor) => actor.Inventory != null && gameObject.activeInHierarchy;

    public void OnInteractionResolved(InteractionSystem actor)
    {
        if (food >= 90f) { actor.ShowMessage("Not hungry"); return; }
        // The most filling food in the bag
        Inventory inv = actor.Inventory;
        int best = -1;
        float bestFood = 0f;
        for (int i = 0; i < inv.Slots.Count; i++)
        {
            InventorySlot s = inv.Slots[i];
            if (s.IsEmpty || !s.item.IsConsumable || s.item.restoreFood <= bestFood) continue;
            best = i;
            bestFood = s.item.restoreFood;
        }
        if (best < 0) { actor.ShowMessage("You have no food to give"); return; }
        string what = inv.Slots[best].item.displayName;
        inv.RemoveFromSlot(best, 1);
        food = Mathf.Min(100f, food + bestFood);
        MissionLog.MealsGivenToCrew++;
        actor.ShowMessage($"Gave {what}");
    }
}
