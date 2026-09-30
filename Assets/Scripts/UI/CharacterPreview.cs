using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

// Shows the player's astronaut in the inventory window. A copy of the model (only its meshes and Animator,
// so it plays the idle animation with the same skin) stands in a small "studio" far below the world, out of the
// main camera's reach, with its own camera and lights; the picture goes into this RawImage.
// The camera only renders while the window is open. Drag to turn the astronaut.
[RequireComponent(typeof(RawImage))]
public class CharacterPreview : MonoBehaviour, IDragHandler
{
    public Color background = UITheme.Panel;
    [Range(10f, 60f)] public float fieldOfView = 20f;
    [Tooltip("How much empty space around the astronaut (1 = touching the edges)")]
    public float framing = 1.12f;
    public float dragRotateSpeed = 0.5f;
    public Vector3 studioPosition = new Vector3(0f, -5000f, 0f);

    private RawImage image;
    private GameObject studio;
    private Transform model;
    private Camera previewCamera;
    private RenderTexture texture;
    private PlayerCharacter playerCharacter; // Rebuilds the copy when the player switches character

    void Awake()
    {
        image = GetComponent<RawImage>();
        image.color = background; // Plain panel until the first picture arrives
    }

    void OnEnable()
    {
        if (previewCamera != null) previewCamera.enabled = true;
    }

    void OnDisable()
    {
        if (previewCamera != null) previewCamera.enabled = false;
    }

    void Update()
    {
        if (studio == null) TryBuild(); // The player may not exist in the first frames
    }

    void OnDestroy()
    {
        if (playerCharacter != null) playerCharacter.CharacterChanged -= OnCharacterChanged;
        Clear();
    }

    // Throws away the copy; Update builds a new one from the player's current model
    void OnCharacterChanged(CharacterDefinition character) => Clear();

    void Clear()
    {
        if (studio != null) Destroy(studio);
        studio = null;
        model = null;
        previewCamera = null;
        if (image != null)
        {
            image.texture = null;
            image.color = background;
        }
        if (texture != null)
        {
            texture.Release();
            Destroy(texture);
        }
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (model != null) model.Rotate(0f, -eventData.delta.x * dragRotateSpeed, 0f, Space.World);
    }

    void TryBuild()
    {
        PlayerMovement player = FindFirstObjectByType<PlayerMovement>();
        if (player == null) return;
        if (playerCharacter == null && player.TryGetComponent(out playerCharacter))
            playerCharacter.CharacterChanged += OnCharacterChanged;

        studio = new GameObject("Character Preview Studio");
        studio.transform.position = studioPosition;
        model = CopyVisuals(player.gameObject, studio.transform);
        model.localRotation = Quaternion.Euler(0f, 200f, 0f); // Facing the camera, turned a little

        Bounds bounds = VisibleBounds(model);
        Rect rect = ((RectTransform)transform).rect;
        float aspect = rect.height > 0f ? rect.width / rect.height : 0.75f;
        texture = new RenderTexture(Mathf.Max(64, Mathf.RoundToInt(rect.width * 2f)), Mathf.Max(64, Mathf.RoundToInt(rect.height * 2f)), 24)
        {
            antiAliasing = 4,
            name = "Character Preview"
        };

        // Far enough that the whole body (height and width) fits
        float halfFov = fieldOfView * 0.5f * Mathf.Deg2Rad;
        float fitHeight = bounds.extents.y / Mathf.Tan(halfFov);
        float fitWidth = Mathf.Max(bounds.extents.x, bounds.extents.z) / (Mathf.Tan(halfFov) * aspect);
        float distance = Mathf.Max(fitHeight, fitWidth) * framing + bounds.extents.z;

        var camObject = new GameObject("Preview Camera");
        camObject.transform.SetParent(studio.transform, false);
        previewCamera = camObject.AddComponent<Camera>();
        previewCamera.clearFlags = CameraClearFlags.SolidColor;
        previewCamera.backgroundColor = background;
        previewCamera.fieldOfView = fieldOfView;
        previewCamera.nearClipPlane = distance * 0.2f;
        previewCamera.farClipPlane = distance * 3f;
        previewCamera.targetTexture = texture;
        camObject.transform.position = bounds.center + Vector3.back * distance;
        camObject.transform.LookAt(bounds.center);
        UniversalAdditionalCameraData data = previewCamera.GetUniversalAdditionalCameraData();
        data.renderShadows = false;       // The ground far above would shadow the studio
        data.renderPostProcessing = false;

        // Point lights with a short range, so they only light the studio. Brightness falls off with the square
        // of the distance, so the intensity is scaled by it.
        AddLight("Key Light", bounds.center + new Vector3(-0.7f, 0.8f, -1f).normalized * distance, 1.4f, new Color(1f, 0.96f, 0.9f), distance);
        AddLight("Rim Light", bounds.center + new Vector3(0.9f, 0.5f, 1f).normalized * distance, 1.1f, new Color(0.6f, 0.75f, 1f), distance);

        image.texture = texture;
        image.color = Color.white;
        previewCamera.enabled = isActiveAndEnabled;
    }

    void AddLight(string name, Vector3 position, float brightness, Color color, float distance)
    {
        var go = new GameObject(name);
        go.transform.SetParent(studio.transform, false);
        go.transform.position = position;
        Light light = go.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = color;
        light.range = distance * 2f;
        light.intensity = brightness * distance * distance;
        light.shadows = LightShadows.None;
    }

    // Copies the player and keeps only what is needed to draw and animate it. The copy is made inside an inactive
    // parent, so none of the player's scripts ever wake up on it.
    static Transform CopyVisuals(GameObject source, Transform parent)
    {
        var holder = new GameObject("Copy Holder");
        holder.SetActive(false);
        GameObject copy = Instantiate(source, holder.transform);
        copy.name = "Astronaut";

        foreach (ParticleSystem ps in copy.GetComponentsInChildren<ParticleSystem>(true))
            if (ps.gameObject != copy) DestroyImmediate(ps.gameObject);
            else DestroyImmediate(ps);

        // Several passes: a component that another one requires can only go after that one is gone
        for (int pass = 0; pass < 4; pass++)
            foreach (Component c in copy.GetComponentsInChildren<Component>(true))
                if (c != null && !Keep(c) && !RequiredByOthers(c)) DestroyImmediate(c);

        copy.transform.SetParent(parent, false);
        copy.transform.localPosition = Vector3.zero;
        copy.transform.localScale = source.transform.lossyScale;
        Animator animator = copy.GetComponentInChildren<Animator>(); // On the "Model" child of the player
        if (animator != null) animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        Destroy(holder);
        return copy.transform;
    }

    static bool Keep(Component c) =>
        c is Transform || c is Animator || c is SkinnedMeshRenderer || c is MeshRenderer || c is MeshFilter;

    static bool RequiredByOthers(Component component)
    {
        foreach (Component other in component.GetComponents<Component>())
        {
            if (other == null || other == component) continue;
            foreach (RequireComponent require in other.GetType().GetCustomAttributes(typeof(RequireComponent), true))
                if (Requires(require.m_Type0, component) || Requires(require.m_Type1, component) || Requires(require.m_Type2, component))
                    return true;
        }
        return false;
    }

    static bool Requires(System.Type type, Component component) => type != null && type.IsInstanceOfType(component);

    static Bounds VisibleBounds(Transform root)
    {
        Bounds bounds = new Bounds(root.position, Vector3.zero);
        bool any = false;
        foreach (Renderer r in root.GetComponentsInChildren<Renderer>())
        {
            if (!any) { bounds = r.bounds; any = true; }
            else bounds.Encapsulate(r.bounds);
        }
        return bounds;
    }
}
