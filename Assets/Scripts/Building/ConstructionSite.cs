using UnityEngine;

// A building that is being built. The work only goes on while its builder is at the site working:
//  - an NPC builder walks here by itself (CrewMember)
//  - if the player is the builder: walk here and press F (crouch-and-work animation); moving stops the work
// The building slowly rises out of the ground; when the time is up the finished prefab replaces the site
// and the builder is free again. A progress bar floats above it.
public class ConstructionSite : MonoBehaviour, IInteractable
{
    public BuildableDefinition Buildable { get; private set; }
    public CrewMember Builder { get; private set; }
    public float Progress { get; private set; } // 0 - 1
    public bool IsComplete => Progress >= 1f;

    private Transform visual;
    private float height;
    private Vector2 footprint;
    private Vector3 localCenter;
    private PlayerMovement playerMovement;
    private GUIStyle titleStyle, timeStyle, infoStyle;

    public static ConstructionSite Create(BuildableDefinition buildable, CrewMember builder, Vector3 position, Quaternion rotation)
    {
        var go = new GameObject("Construction: " + buildable.displayName);
        go.transform.SetPositionAndRotation(position, rotation);
        ConstructionSite site = go.AddComponent<ConstructionSite>();
        site.Setup(buildable, builder);
        return site;
    }

    void Setup(BuildableDefinition buildable, CrewMember builder)
    {
        Buildable = buildable;
        Builder = builder;

        visual = BuildGhost.VisualCopy(buildable.prefab, transform).transform;
        Bounds bounds = BuildGhost.LocalBounds(visual.gameObject);
        height = Mathf.Max(0.1f, bounds.size.y);
        footprint = new Vector2(bounds.size.x, bounds.size.z);
        localCenter = new Vector3(bounds.center.x, 0f, bounds.center.z);
        UpdateVisual();

        // Found by the player's InteractionSystem (F) and by placement checks; a trigger, so nobody bumps into it
        var box = gameObject.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.center = bounds.center;
        box.size = bounds.size + new Vector3(2f, 0f, 2f);
        gameObject.AddComponent<PlacedBuilding>(); // Its ground is taken from now on

        if (builder != null)
        {
            builder.AssignJob(this);
            if (builder.isPlayer) playerMovement = builder.GetComponent<PlayerMovement>();
        }
    }

    void Update()
    {
        if (Builder == null || IsComplete) return;

        bool working = Builder.isPlayer ? PlayerIsWorking() : Builder.IsWorking;
        if (Builder.isPlayer) Builder.SetWorking(working);
        if (!working) return;
        if (Builder.isPlayer) FacePlayerToSite();

        Progress = Mathf.Min(1f, Progress + Time.deltaTime / Buildable.buildTime);
        UpdateVisual();
        if (IsComplete) Finish();
    }

    bool PlayerIsWorking()
    {
        if (playerMovement == null || !playerMovement.IsCrafting) return false;
        Vector3 closest = WorkPoint(playerMovement.transform.position);
        return Vector3.ProjectOnPlane(playerMovement.transform.position - closest, Vector3.up).magnitude < 12f;
    }

    // While crafting the player doesn't move, so we can turn them toward the work
    void FacePlayerToSite()
    {
        Transform player = playerMovement.transform;
        Vector3 toSite = Vector3.ProjectOnPlane(transform.TransformPoint(localCenter) - player.position, Vector3.up);
        if (toSite.sqrMagnitude < 0.01f) return;
        player.rotation = Quaternion.Slerp(player.rotation, Quaternion.LookRotation(toSite), 6f * Time.deltaTime);
    }

    // The building comes up out of the ground as the work goes on
    void UpdateVisual()
    {
        float shown = Mathf.Lerp(0.08f, 1f, Progress);
        visual.localPosition = Vector3.down * height * (1f - shown);
    }

    void Finish()
    {
        GameObject building = Instantiate(Buildable.prefab, transform.position, transform.rotation);
        building.name = Buildable.displayName;
        if (building.GetComponent<PlacedBuilding>() == null) building.AddComponent<PlacedBuilding>();
        if (Builder != null) Builder.ReleaseJob();
        var system = FindFirstObjectByType<InteractionSystem>();
        if (system != null) system.ShowMessage($"{Buildable.displayName} built!");
        Destroy(gameObject);
    }

    // Where the builder stands: just outside the footprint, on the side they come from
    public Vector3 WorkPoint(Vector3 from)
    {
        Vector3 local = transform.InverseTransformPoint(from) - localCenter;
        float hx = footprint.x * 0.5f + 3f, hz = footprint.y * 0.5f + 3f;
        // Closest point on the rectangle's edge
        Vector3 p = new Vector3(Mathf.Clamp(local.x, -hx, hx), 0f, Mathf.Clamp(local.z, -hz, hz));
        if (Mathf.Abs(p.x) < hx && Mathf.Abs(p.z) < hz)
        {
            if (hx - Mathf.Abs(p.x) < hz - Mathf.Abs(p.z)) p.x = Mathf.Sign(p.x == 0f ? 1f : p.x) * hx;
            else p.z = Mathf.Sign(p.z == 0f ? 1f : p.z) * hz;
        }
        return BaseArea.GroundPoint(transform.TransformPoint(p + localCenter));
    }

    // --------------------------------------------------------------- F for the player builder

    public string GetPrompt() => $"Build {Buildable.displayName} ({Mathf.FloorToInt(Progress * 100f)}%)";
    public InteractionAnimation GetAnimationType() => InteractionAnimation.Craft;
    public bool CanInteract(InteractionSystem actor) =>
        !IsComplete && Builder != null && Builder.isPlayer && actor.gameObject == Builder.gameObject;
    public void OnInteractionResolved(InteractionSystem actor) { } // The work itself happens in Update

    // --------------------------------------------------------------- Progress panel

    void OnGUI()
    {
        Camera cam = Camera.main;
        if (cam == null || Buildable == null) return;
        Vector3 top = transform.position + Vector3.up * (height + 4f);
        Vector3 screen = cam.WorldToScreenPoint(top);
        if (screen.z <= 0f || screen.z > 400f) return;
        if (titleStyle == null) MakeStyles();

        const float width = 240f, panelHeight = 74f, pad = 12f;
        float x = screen.x - width / 2f, y = Screen.height - screen.y - panelHeight;
        string who = Builder == null ? "No builder"
                   : Builder.isPlayer && !Builder.IsWorking ? "Press F here to build"
                   : Builder.IsWorking ? $"{Builder.displayName} is building" : $"{Builder.displayName} is on the way";
        int secondsLeft = Mathf.CeilToInt((1f - Progress) * Buildable.buildTime);

        GUI.DrawTexture(new Rect(x, y, width, panelHeight), UITheme.Pixel(UITheme.Strip));
        GUI.DrawTexture(new Rect(x, y, 3f, panelHeight), UITheme.Pixel(UITheme.Accent));
        GUI.Label(new Rect(x + pad, y + 6f, width - pad * 2f, 24f), Buildable.displayName, titleStyle);
        GUI.Label(new Rect(x + pad, y + 6f, width - pad * 2f, 24f), $"{secondsLeft / 60}:{secondsLeft % 60:00}", timeStyle);
        GUI.Label(new Rect(x + pad, y + 28f, width - pad * 2f, 20f), who, infoStyle);
        GUI.DrawTexture(new Rect(x + pad, y + 54f, width - pad * 2f, 6f), UITheme.Pixel(new Color(1f, 1f, 1f, 0.1f)));
        GUI.DrawTexture(new Rect(x + pad, y + 54f, (width - pad * 2f) * Progress, 6f), UITheme.Pixel(UITheme.Accent));
    }

    void MakeStyles()
    {
        titleStyle = new GUIStyle(GUI.skin.label)
        {
            font = UITheme.GuiFont, fontSize = 16, alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip,
            normal = { textColor = UITheme.Text }
        };
        timeStyle = new GUIStyle(titleStyle) { alignment = TextAnchor.MiddleRight, normal = { textColor = UITheme.Accent } };
        infoStyle = new GUIStyle(titleStyle) { fontSize = 13, normal = { textColor = UITheme.TextDim } };
    }

    void OnDestroy()
    {
        if (Builder != null && Builder.Job == this) Builder.ReleaseJob();
    }
}
