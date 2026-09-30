using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

// Gives the astronaut a different look (skin) while keeping the same model, skeleton and animations:
//  1) swaps the suit material (the recolored textures in Resources/AstronautSkins)
//  2) builds small accessories out of simple shapes and attaches them to the bones, so they move with the animation.
// [ExecuteAlways] makes it also run in the editor, so a skin can be previewed without pressing Play.
[ExecuteAlways]
[DisallowMultipleComponent]
public class AstronautSkin : MonoBehaviour
{
    // Custom = this script doesn't touch the materials; use it to pick a material by hand in the Skinned Mesh Renderer
    public enum SkinType { Classic, Pink, Nerd, Cool, Emo, Custom }

    // Everything created by this script starts with this name, so it can be found and removed later
    public const string AccessoryPrefix = "SkinAccessory_";
    const string MaterialFolder = "AstronautSkins/Astronaut_";

    public SkinType skin = SkinType.Classic;
    public bool showAccessories = true;
    [Range(0.5f, 1.5f)] public float accessoryScale = 1f; // Size of decorative parts (bow, pens, propeller...)
    [Tooltip("Tick if accessories that should be in front appear on the back (the model faces -Z)")]
    public bool modelFacesBackward = false;
    public float propellerSpeed = 720f; // Degrees per second (Nerd skin)

    private Transform propeller;
    private readonly List<Material> createdMaterials = new List<Material>();
    private readonly Dictionary<string, Material> materialCache = new Dictionary<string, Material>();

    // Measurements of the model, taken each time the accessories are built
    private Vector3 up, forward, right;
    private Transform headBone, chestBone, hipsBone;
    private Vector3 helmetCenter;
    private float helmetRadius;
    private Vector3 hipsCenter;
    private float hipsHalfWidth, hipsHalfDepth;
    private float far; // A distance safely outside the model, used as the start of the measuring rays
    private readonly List<MeshCollider> probes = new List<MeshCollider>();
    private bool queriesHitBackfacesBefore;

    void OnEnable()
    {
        // In the game every skinned astronaut also gets foot planting, so its boots stay on the ground
        if (Application.isPlaying && GetComponent<AstronautAnimation>() == null) gameObject.AddComponent<AstronautAnimation>();
        Apply();
    }
    void OnDisable() => RemoveAccessories();

#if UNITY_EDITOR
    // Called when a value changes in the Inspector. Objects can't be created in here, so apply right after.
    void OnValidate()
    {
        EditorApplication.delayCall += () =>
        {
            if (this != null && isActiveAndEnabled) Apply();
        };
    }
#endif

    void Update()
    {
        if (propeller != null && Application.isPlaying)
            propeller.Rotate(Vector3.up, propellerSpeed * Time.deltaTime, Space.Self);
    }

    public void Apply()
    {
        RemoveAccessories();
        if (skin == SkinType.Custom) return;
        ApplyMaterial();
        if (showAccessories && skin != SkinType.Classic) BuildAccessories();
    }

    // ---------------------------------------------------------------- Material

    void ApplyMaterial()
    {
        Material material = Resources.Load<Material>(MaterialFolder + skin);
        if (material == null)
        {
            Debug.LogWarning($"AstronautSkin: material 'Resources/{MaterialFolder}{skin}' not found.", this);
            return;
        }

        foreach (SkinnedMeshRenderer r in GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            Material[] materials = r.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] == material) continue;
                materials[i] = material;
                changed = true;
            }
            if (!changed) continue;
            r.sharedMaterials = materials;
#if UNITY_EDITOR
            if (!Application.isPlaying) EditorUtility.SetDirty(r);
#endif
        }
    }

    // ---------------------------------------------------------------- Cleanup

    public void RemoveAccessories()
    {
        propeller = null;
        var toDelete = new List<GameObject>();
        foreach (Transform t in GetComponentsInChildren<Transform>(true))
        {
            // Only the top-level accessory objects; their children are deleted together with them
            if (t != transform && t.name.StartsWith(AccessoryPrefix) &&
                (t.parent == null || !t.parent.name.StartsWith(AccessoryPrefix)))
                toDelete.Add(t.gameObject);
        }
        foreach (GameObject go in toDelete) SafeDestroy(go);

        foreach (Material m in createdMaterials) SafeDestroy(m);
        createdMaterials.Clear();
        materialCache.Clear();
    }

    static void SafeDestroy(Object obj)
    {
        if (obj == null) return;
        if (Application.isPlaying) Destroy(obj);
        else DestroyImmediate(obj);
    }

    // ---------------------------------------------------------------- Building

    void BuildAccessories()
    {
        headBone = FindBone("kopf");
        chestBone = FindBone("brust");
        hipsBone = FindBone("hüfte");

        if (!Measure()) return;
        try
        {
            switch (skin)
            {
                case SkinType.Pink: BuildPink(); break;
                case SkinType.Nerd: BuildNerd(); break;
                case SkinType.Cool: BuildCool(); break;
                case SkinType.Emo: BuildEmo(); break;
            }
        }
        finally
        {
            DestroyProbes();
        }
    }

    // Girl astronaut: a big bow on the helmet and a two-layer tutu around the hips
    void BuildPink()
    {
        float r = helmetRadius * accessoryScale;
        Color bowColor = Hex("#FF4FA3");

        Transform bow = Group("Bow", headBone);
        Vector3 dir = (up + right * 0.55f + forward * 0.15f).normalized;
        Vector3 center = helmetCenter + dir * helmetRadius * 0.98f;
        Quaternion baseRotation = Quaternion.LookRotation(forward, dir);
        Vector3 bowRight = baseRotation * Vector3.right;
        Vector3 bowUp = baseRotation * Vector3.up;
        for (int side = -1; side <= 1; side += 2)
        {
            Part("BowLoop", PrimitiveType.Sphere, bow, center + bowRight * side * 0.32f * r + bowUp * 0.06f * r,
                baseRotation * Quaternion.Euler(0f, 0f, -side * 18f), new Vector3(0.55f, 0.36f, 0.2f) * r, bowColor, 0.6f);
            Part("BowTail", PrimitiveType.Cube, bow, center - bowUp * 0.2f * r + bowRight * side * 0.12f * r,
                baseRotation * Quaternion.Euler(0f, 0f, side * 20f), new Vector3(0.1f, 0.35f, 0.04f) * r, bowColor, 0.6f);
        }
        Part("BowKnot", PrimitiveType.Sphere, bow, center + bowUp * 0.04f * r, baseRotation,
            new Vector3(0.2f, 0.2f, 0.18f) * r, Hex("#E0307F"), 0.6f);

        // Tutu: flat discs slightly wider than the hips (cylinder height = 2 x Y scale)
        Transform tutu = Group("Tutu", hipsBone);
        Quaternion flat = Quaternion.LookRotation(forward, up);
        Part("TutuTop", PrimitiveType.Cylinder, tutu, hipsCenter + up * 0.04f * helmetRadius, flat,
            new Vector3(hipsHalfWidth * 2.7f, 0.05f * helmetRadius, hipsHalfDepth * 2.7f), Hex("#FFB3D1"), 0.3f);
        Part("TutuBottom", PrimitiveType.Cylinder, tutu, hipsCenter - up * 0.07f * helmetRadius, flat,
            new Vector3(hipsHalfWidth * 2.45f, 0.05f * helmetRadius, hipsHalfDepth * 2.45f), Hex("#FF7AB8"), 0.3f);
    }

    // Nerd astronaut: a propeller beanie (spins in Play mode) and a pocket full of pens
    void BuildNerd()
    {
        float r = helmetRadius * accessoryScale;
        Transform hat = Group("PropellerBeanie", headBone);
        Quaternion upright = Quaternion.LookRotation(forward, up);

        // Beanie: an ellipsoid slightly bigger than the helmet, shifted up so only the top pokes out
        Part("Beanie", PrimitiveType.Sphere, hat, helmetCenter + up * helmetRadius * 0.3f, upright,
            new Vector3(2.06f, 1.56f, 2.06f) * helmetRadius, Hex("#3B6FE0"), 0.2f);
        Vector3 top = helmetCenter + up * helmetRadius * 1.07f;
        Part("Stem", PrimitiveType.Cylinder, hat, top + up * 0.1f * r, upright,
            new Vector3(0.07f, 0.1f, 0.07f) * r, Hex("#FFD23F"), 0.5f);

        propeller = Group("Propeller", hat);
        propeller.SetPositionAndRotation(top + up * 0.21f * r, upright);
        Part("Hub", PrimitiveType.Sphere, propeller, propeller.position, upright, Vector3.one * 0.14f * r, Hex("#FFD23F"), 0.5f);
        for (int side = -1; side <= 1; side += 2)
        {
            Part("Blade", PrimitiveType.Cube, propeller, propeller.position + right * side * 0.42f * r,
                upright * Quaternion.Euler(side * 15f, 0f, 0f), new Vector3(0.75f, 0.03f, 0.18f) * r,
                side < 0 ? Hex("#E63946") : Hex("#2A9D8F"), 0.5f);
        }

        // Pocket protector with three pens on the left side of the chest
        if (chestBone != null && FrontSurface(chestBone.position - right * helmetRadius * 0.3f, out RaycastHit hit))
        {
            Transform pocket = Group("PocketPens", chestBone);
            Quaternion facing = Quaternion.LookRotation(hit.normal, up);
            Part("Pocket", PrimitiveType.Cube, pocket, hit.point + hit.normal * 0.02f * r, facing,
                new Vector3(0.3f, 0.34f, 0.05f) * r, Hex("#F4F4F4"), 0.3f);
            string[] penColors = { "#1D4ED8", "#DC2626", "#111111" };
            for (int i = 0; i < 3; i++)
            {
                Vector3 side = (facing * Vector3.right) * (i - 1) * 0.09f * r;
                Part("Pen", PrimitiveType.Cylinder, pocket, hit.point + hit.normal * 0.05f * r + up * 0.2f * r + side,
                    facing, new Vector3(0.05f, 0.13f, 0.05f) * r, Hex(penColors[i]), 0.7f);
            }
        }
    }

    // Cool astronaut: backwards cap and a gold chain with a medallion
    void BuildCool()
    {
        float r = helmetRadius * accessoryScale;
        Transform cap = Group("BackwardsCap", headBone);
        Quaternion upright = Quaternion.LookRotation(forward, up);
        Color capColor = Hex("#D7263D");

        Part("CapDome", PrimitiveType.Sphere, cap, helmetCenter + up * helmetRadius * 0.35f, upright,
            new Vector3(2.06f, 1.4f, 2.06f) * helmetRadius, capColor, 0.3f);
        Part("CapButton", PrimitiveType.Sphere, cap, helmetCenter + up * helmetRadius * 1.04f, upright,
            Vector3.one * 0.12f * helmetRadius, Hex("#9E1B2C"), 0.3f);
        // The brim points backwards and tilts down a little
        Quaternion brimRotation = Quaternion.AngleAxis(12f, right) * Quaternion.LookRotation(-forward, up);
        Part("CapBrim", PrimitiveType.Cube, cap, helmetCenter - forward * helmetRadius * 1.3f + up * helmetRadius * 0.18f,
            brimRotation, new Vector3(1.1f, 0.05f, 0.8f) * helmetRadius, capColor, 0.3f);

        // Gold chain: little beads hanging in a V shape on the chest, found by "touching" the chest surface
        if (chestBone == null) return;
        Transform chain = Group("GoldChain", chestBone);
        Color gold = Hex("#F2C14E");
        Vector3 axis = ProjectOnAxis(chestBone.position, helmetCenter - up * helmetRadius);
        RaycastHit lowest = default;
        bool hasLowest = false;
        const int beads = 13;
        for (int i = 0; i < beads; i++)
        {
            float t = Mathf.Lerp(-1f, 1f, i / (beads - 1f));
            Vector3 point = axis + right * t * helmetRadius * 0.5f - up * ((1f - t * t) * 0.55f + 0.08f) * helmetRadius;
            if (!FrontSurface(point, out RaycastHit hit)) continue;
            Part("Bead", PrimitiveType.Sphere, chain, hit.point + hit.normal * 0.03f * helmetRadius, Quaternion.identity,
                Vector3.one * 0.09f * r, gold, 0.85f, 0.9f);
            if (i == beads / 2) { lowest = hit; hasLowest = true; }
        }
        if (hasLowest)
        {
            // A cylinder's axis is its Y, so turn it 90 degrees to face forward like a coin
            Part("Medallion", PrimitiveType.Cylinder, chain, lowest.point + lowest.normal * 0.06f * helmetRadius - up * 0.12f * r,
                Quaternion.LookRotation(lowest.normal, up) * Quaternion.Euler(90f, 0f, 0f),
                new Vector3(0.28f, 0.025f, 0.28f) * r, gold, 0.85f, 0.9f);
        }
    }

    // Emo astronaut: black hair with side-swept bangs (one purple streak) and a studded belt
    void BuildEmo()
    {
        float r = helmetRadius * accessoryScale;
        Transform hair = Group("EmoHair", headBone);
        Color black = Hex("#141418");

        Part("HairCap", PrimitiveType.Sphere, hair, helmetCenter + up * helmetRadius * 0.25f,
            Quaternion.LookRotation(forward, up), new Vector3(2.08f, 1.64f, 2.08f) * helmetRadius, black, 0.35f);

        // Bangs: strands from the top-right sweeping down over the left side of the visor
        Vector3 start = HelmetDirection(35f, 60f);
        for (int k = 0; k < 4; k++)
        {
            Vector3 end = HelmetDirection(-10f - 12f * k, 12f - 7f * k);
            Vector3 middle = Vector3.Slerp(start, end, 0.55f).normalized;
            Vector3 position = helmetCenter + middle * helmetRadius * 1.05f;
            Vector3 tangent = (end - start).normalized;
            float length = Vector3.Angle(start, end) * Mathf.Deg2Rad * helmetRadius * 1.05f;
            Part("Bang", PrimitiveType.Sphere, hair, position, Quaternion.LookRotation(middle, tangent),
                new Vector3(0.34f * helmetRadius, length, 0.14f * helmetRadius), k == 1 ? Hex("#B45CFF") : black, 0.4f);
        }

        // Studded belt around the hips
        Transform belt = Group("StuddedBelt", hipsBone);
        Part("Belt", PrimitiveType.Cylinder, belt, hipsCenter, Quaternion.LookRotation(forward, up),
            new Vector3(hipsHalfWidth * 2.12f, 0.07f * helmetRadius, hipsHalfDepth * 2.12f), Hex("#1A1A1F"), 0.5f);
        const int studs = 14;
        for (int i = 0; i < studs; i++)
        {
            float a = i * Mathf.PI * 2f / studs;
            Vector3 position = hipsCenter + right * Mathf.Cos(a) * hipsHalfWidth * 1.08f + forward * Mathf.Sin(a) * hipsHalfDepth * 1.08f;
            Part("Stud", PrimitiveType.Sphere, belt, position, Quaternion.identity, Vector3.one * 0.08f * r, Hex("#C9CED6"), 0.9f, 1f);
        }
    }

    // ---------------------------------------------------------------- Measuring the model

    // Temporarily turns the posed mesh into colliders, then shoots rays at it to find the helmet and body surface
    bool Measure()
    {
        up = transform.up;
        forward = modelFacesBackward ? -transform.forward : transform.forward;
        right = Vector3.Cross(up, forward);

        Bounds bounds = new Bounds();
        bool found = false;
        foreach (SkinnedMeshRenderer smr in GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (smr.name.StartsWith(AccessoryPrefix) || smr.sharedMesh == null) continue;
            if (!found) { bounds = smr.bounds; found = true; }
            else bounds.Encapsulate(smr.bounds);

            var mesh = new Mesh { name = AccessoryPrefix + "ProbeMesh" };
            // Without scale, the baked vertices are already world-sized; only position and rotation are needed.
            // (With useScale = true this model's mirrored bone scales produced a mesh ~200x too small.)
            smr.BakeMesh(mesh, false);
            var probe = new GameObject(AccessoryPrefix + "Probe") { hideFlags = HideFlags.HideAndDontSave };
            probe.transform.SetPositionAndRotation(smr.transform.position, smr.transform.rotation);
            var collider = probe.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
            probes.Add(collider);
        }
        if (!found) return false;
        Physics.SyncTransforms();
        // The model's mirrored bone scales make the baked mesh inside-out; let rays hit faces from both sides
        queriesHitBackfacesBefore = Physics.queriesHitBackfaces;
        Physics.queriesHitBackfaces = true;
        far = bounds.size.magnitude * 2f + 1f;

        // Helmet: scan from the top of the head downwards. The width grows until the helmet's widest point
        // (its center), then shrinks toward the neck; a sudden jump means we reached the shoulders.
        Vector3 headAxis = headBone != null ? headBone.position : bounds.center + up * bounds.extents.y * 0.4f;
        Vector3 topPoint = Cast(headAxis + up * far, -up, out RaycastHit topHit) ? topHit.point : headAxis + up * bounds.extents.y * 0.5f;
        float step = bounds.size.y / 200f;
        float best = 0f;
        Vector3 bestLevel = topPoint;
        for (int i = 1; i < 120; i++)
        {
            Vector3 level = topPoint - up * step * i;
            if (!Width(level, right, out Vector3 middle, out float half)) continue;
            float fromTop = step * i;
            // Stop at the shoulders (sudden jump) or once clearly past the widest point.
            // Near the top, small parts (antenna, ear pieces) make the width jumpy, so shrinking only counts further down.
            if (best > 0f && fromTop > best * 0.8f && (half > best * 1.15f || half < best * 0.97f)) break;
            if (half > best) { best = half; bestLevel = middle; }
        }
        helmetRadius = best > 0f ? Mathf.Max(best, Vector3.Dot(topPoint - bestLevel, up)) : bounds.extents.y * 0.25f;
        helmetCenter = bestLevel;
        // Front-to-back: use the front of the helmet (the back may have a backpack attached)
        if (FrontSurface(helmetCenter, out RaycastHit visor)) helmetCenter = visor.point - forward * helmetRadius;

        // Hips: width (side to side) and depth (front to back) of the body at hip height
        Vector3 hipsLevel = hipsBone != null ? hipsBone.position : bounds.center - up * bounds.extents.y * 0.3f;
        hipsCenter = hipsLevel;
        hipsHalfDepth = Width(hipsLevel, forward, out Vector3 hipsMiddle, out float depth) ? depth : helmetRadius * 0.6f;
        hipsHalfWidth = Width(hipsLevel, right, out _, out float width) ? width : hipsHalfDepth;
        hipsHalfWidth = Mathf.Min(hipsHalfWidth, hipsHalfDepth * 1.3f, helmetRadius * 1.1f); // Ignore hands hanging next to the hips
        hipsCenter = hipsMiddle;
        return true;
    }

    // Shoots two rays toward each other through 'point' along 'direction' and returns the middle and half the distance
    bool Width(Vector3 point, Vector3 direction, out Vector3 middle, out float half)
    {
        middle = point;
        half = 0f;
        if (!Cast(point + direction * far, -direction, out RaycastHit a)) return false;
        if (!Cast(point - direction * far, direction, out RaycastHit b)) return false;
        middle = (a.point + b.point) * 0.5f;
        half = Vector3.Distance(a.point, b.point) * 0.5f;
        return true;
    }

    // Surface point in front of 'point', found by shooting a ray from the front toward the body
    bool FrontSurface(Vector3 point, out RaycastHit hit) => Cast(point + forward * far, -forward, out hit);

    bool Cast(Vector3 origin, Vector3 direction, out RaycastHit best)
    {
        best = default;
        bool found = false;
        foreach (MeshCollider probe in probes)
        {
            if (probe.Raycast(new Ray(origin, direction), out RaycastHit hit, far * 2f) && (!found || hit.distance < best.distance))
            {
                best = hit;
                found = true;
            }
        }
        return found;
    }

    void DestroyProbes()
    {
        Physics.queriesHitBackfaces = queriesHitBackfacesBefore;
        foreach (MeshCollider probe in probes)
        {
            if (probe == null) continue;
            DestroyImmediate(probe.sharedMesh);
            DestroyImmediate(probe.gameObject);
        }
        probes.Clear();
    }

    // Direction from the helmet center: yaw = left(-)/right(+) angle, pitch = up angle (0 = straight ahead)
    Vector3 HelmetDirection(float yaw, float pitch)
    {
        return Quaternion.AngleAxis(yaw, up) * Quaternion.AngleAxis(-pitch, right) * forward;
    }

    // A point with the horizontal position of 'horizontal' and the height of 'height'
    Vector3 ProjectOnAxis(Vector3 horizontal, Vector3 height)
    {
        return horizontal + up * Vector3.Dot(height - horizontal, up);
    }

    Transform FindBone(string boneName)
    {
        foreach (Transform t in GetComponentsInChildren<Transform>(true))
            if (t.name == boneName) return t;
        return null;
    }

    // ---------------------------------------------------------------- Creating parts

    // An empty parent object attached to a bone; all of its parts follow that bone's animation
    Transform Group(string groupName, Transform bone)
    {
        var go = new GameObject(AccessoryPrefix + groupName);
        MarkCreated(go);
        go.transform.SetParent(bone != null ? bone : transform, false);
        return go.transform;
    }

    // One simple 3D shape. Position, rotation and scale are given in world space.
    // Unity's built-in shapes: Sphere and Cube are 1 unit wide; Cylinder is 1 wide and 2 tall.
    void Part(string partName, PrimitiveType type, Transform parent, Vector3 position, Quaternion rotation,
              Vector3 scale, Color color, float smoothness, float metallic = 0f)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = AccessoryPrefix + partName;
        MarkCreated(go);
        DestroyImmediate(go.GetComponent<Collider>()); // Decoration only; must not bump into anything
        go.transform.SetPositionAndRotation(position, rotation);
        go.transform.localScale = scale;
        go.transform.SetParent(parent, true); // Keeps the world size even though the bones are scaled
        go.GetComponent<Renderer>().sharedMaterial = GetMaterial(color, smoothness, metallic);
    }

    // Outside Play mode, created objects must not be saved into the scene (they are rebuilt every time)
    static void MarkCreated(GameObject go)
    {
        if (!Application.isPlaying) go.hideFlags = HideFlags.DontSave;
    }

    Material GetMaterial(Color color, float smoothness, float metallic)
    {
        string key = $"{ColorUtility.ToHtmlStringRGB(color)}_{smoothness}_{metallic}";
        if (materialCache.TryGetValue(key, out Material cached)) return cached;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        var material = new Material(shader) { name = AccessoryPrefix + key };
        if (!Application.isPlaying) material.hideFlags = HideFlags.DontSave;
        material.SetColor("_BaseColor", color);
        material.SetColor("_Color", color);
        material.SetFloat("_Smoothness", smoothness);
        material.SetFloat("_Glossiness", smoothness);
        material.SetFloat("_Metallic", metallic);

        materialCache[key] = material;
        createdMaterials.Add(material);
        return material;
    }

    static Color Hex(string html)
    {
        return ColorUtility.TryParseHtmlString(html, out Color color) ? color : Color.magenta;
    }
}
