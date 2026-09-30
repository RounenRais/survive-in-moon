using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

// The building system of the base:
//  - catalog: everything that can be built (shown on the build terminal's screen)
//  - placement: after choosing a building and a builder on the terminal, a see-through copy follows the mouse.
//    Green = can be placed (inside the base area, nothing in the way), red = can't.
//    Left click: place (the materials are taken now)   Q / E: rotate   Esc: cancel
//  - the placed building becomes a ConstructionSite that the chosen builder builds
public class BuildManager : MonoBehaviour
{
    public static BuildManager Instance { get; private set; }

    public List<BuildableDefinition> catalog = new List<BuildableDefinition>();
    public BaseArea baseArea;
    [Tooltip("Placement snaps to this grid (0 = free)")]
    public float gridSize = 2f;
    public float rotationStep = 45f;
    public Color validColor = new Color(0.3f, 1f, 0.5f, 0.45f);
    public Color invalidColor = new Color(1f, 0.12f, 0.1f, 0.6f);
    [Tooltip("Free space kept between two buildings")]
    public float buildingGap = 1f;

    public bool IsPlacing => ghost != null;

    private BuildableDefinition placing;
    private CrewMember placingBuilder;
    private GameObject ghost;
    private Material ghostMaterial;
    private Bounds ghostBounds;
    private LineRenderer ghostOutline; // The footprint drawn on the ground under the preview
    private float yaw;
    private bool valid;
    private readonly Collider[] overlaps = new Collider[16];

    void Awake()
    {
        Instance = this;
        if (baseArea == null) baseArea = GetComponent<BaseArea>();
    }

    public Inventory PlayerInventory
    {
        get
        {
            var movement = FindFirstObjectByType<PlayerMovement>();
            return movement != null ? movement.GetComponent<Inventory>() : null;
        }
    }

    // --------------------------------------------------------------- Placement

    public void StartPlacement(BuildableDefinition buildable, CrewMember builder)
    {
        CancelPlacement();
        if (buildable == null || buildable.prefab == null) return;
        placing = buildable;
        placingBuilder = builder;

        ghost = BuildGhost.VisualCopy(buildable.prefab, null);
        ghost.name = "Placement Preview";
        ghostBounds = BuildGhost.LocalBounds(ghost);
        ghostMaterial = BuildGhost.CreateTransparentMaterial(validColor);
        BuildGhost.SetMaterial(ghost, ghostMaterial);
        ghostOutline = FootprintOutline(ghost.transform, ghostBounds, ghostMaterial);
        if (baseArea != null) baseArea.SetHighlighted(true);
    }

    public void CancelPlacement()
    {
        if (ghost != null) Destroy(ghost);
        if (ghostMaterial != null) Destroy(ghostMaterial);
        ghost = null;
        placing = null;
        placingBuilder = null;
        if (baseArea != null) baseArea.SetHighlighted(false);
    }

    void Update()
    {
        if (!IsPlacing) return;

        if (Input.GetKeyDown(KeyCode.Escape)) { CancelPlacement(); return; }
        // Q / E (R and the mouse wheel already belong to the camera)
        if (Input.GetKeyDown(KeyCode.E)) yaw += rotationStep;
        if (Input.GetKeyDown(KeyCode.Q)) yaw -= rotationStep;

        if (!MouseOnGround(out Vector3 point)) { ghost.SetActive(false); return; }
        ghost.SetActive(true);
        if (gridSize > 0f)
        {
            point.x = Mathf.Round(point.x / gridSize) * gridSize;
            point.z = Mathf.Round(point.z / gridSize) * gridSize;
            point = BaseArea.GroundPoint(point);
        }
        Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);
        ghost.transform.SetPositionAndRotation(point, rotation);

        valid = CanPlace(point, rotation);
        Color color = valid ? validColor : invalidColor;
        ghostMaterial.SetColor("_BaseColor", color);
        ghostOutline.startColor = ghostOutline.endColor = new Color(color.r, color.g, color.b, 1f);

        bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        if (Input.GetMouseButtonDown(0) && !overUI && valid) Place(point, rotation);
    }

    // Where the mouse points on the ground (also used to send crew mates somewhere)
    public static bool MouseOnGround(out Vector3 point)
    {
        point = Vector3.zero;
        Camera cam = Camera.main;
        if (cam == null) return false;
        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        float best = float.MaxValue;
        bool found = false;
        foreach (RaycastHit hit in Physics.RaycastAll(ray, 2000f, ~0, QueryTriggerInteraction.Ignore))
        {
            // Only the ground: skip astronauts, buildings and items (anything with a script above it)
            if (hit.collider.GetComponentInParent<MonoBehaviour>() != null) continue;
            if (hit.distance < best) { best = hit.distance; point = hit.point; found = true; }
        }
        return found;
    }

    bool CanPlace(Vector3 point, Quaternion rotation)
    {
        Vector2 footprint = new Vector2(ghostBounds.size.x, ghostBounds.size.z);
        Vector3 footprintCenter = point + rotation * new Vector3(ghostBounds.center.x, 0f, ghostBounds.center.z);
        if (baseArea != null && !baseArea.Contains(footprintCenter, footprint, rotation)) return false;

        // Other buildings, sites, the hub and the terminal: their footprints may not overlap ours
        if (PlacedBuilding.AnyOverlaps(new Footprint(footprintCenter, footprint, rotation), buildingGap)) return false;

        // Anything else in the way (astronauts, the rover, rocks)? The box starts a little above the ground
        // so the ground itself doesn't count
        Vector3 half = new Vector3(footprint.x * 0.5f, ghostBounds.size.y * 0.5f, footprint.y * 0.5f);
        Vector3 center = footprintCenter + Vector3.up * (half.y + 0.5f);
        int count = Physics.OverlapBoxNonAlloc(center, half, overlaps, rotation, ~0, QueryTriggerInteraction.Collide);
        for (int i = 0; i < count; i++)
        {
            Collider c = overlaps[i];
            if (c.transform.IsChildOf(ghost.transform)) continue;
            if (c.GetComponentInParent<PlacedBuilding>() != null) continue; // Already checked by footprint
            return false;
        }
        return true;
    }

    static LineRenderer FootprintOutline(Transform parent, Bounds bounds, Material material)
    {
        var go = new GameObject("Footprint");
        go.transform.SetParent(parent, false);
        LineRenderer line = go.AddComponent<LineRenderer>();
        line.useWorldSpace = false;
        line.loop = true;
        line.widthMultiplier = 0.5f;
        line.sharedMaterial = material;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        float x0 = bounds.min.x, x1 = bounds.max.x, z0 = bounds.min.z, z1 = bounds.max.z, y = 0.3f;
        line.positionCount = 4;
        line.SetPositions(new[] { new Vector3(x0, y, z0), new Vector3(x1, y, z0), new Vector3(x1, y, z1), new Vector3(x0, y, z1) });
        return line;
    }

    void Place(Vector3 point, Quaternion rotation)
    {
        Inventory inventory = PlayerInventory;
        InteractionSystem messages = inventory != null ? inventory.GetComponent<InteractionSystem>() : null;
        if (!placing.Pay(inventory))
        {
            if (messages != null) messages.ShowMessage("Not Enough Materials");
            CancelPlacement();
            return;
        }
        if (placingBuilder != null && placingBuilder.IsBusy) placingBuilder = null; // Got another job meanwhile
        ConstructionSite.Create(placing, placingBuilder, point, rotation);
        if (messages != null && placingBuilder != null && placingBuilder.isPlayer)
            messages.ShowMessage("Walk to the site and press F to build");
        CancelPlacement();
    }

    void OnGUI()
    {
        if (!IsPlacing) return;
        UITheme.DrawHint($"Placing {placing.displayName}",
            valid ? "Left click to place    Q / E to rotate    Esc to cancel" : "Needs free ground inside the base",
            valid ? UITheme.TextDim : UITheme.Bad);
    }

    void OnDestroy() => CancelPlacement();
}
