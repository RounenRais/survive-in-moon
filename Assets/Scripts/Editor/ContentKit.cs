using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Small modelling kit for GameContentBuilder: rounded boxes, cylinders, spheres and rocks made of saved meshes,
// plus materials, all in the same low-poly style as the buildings and items. Pivot of every model: on the ground.
public static class ContentKit
{
    public const string MaterialFolder = "Assets/Materials/Content";
    const string MeshFolder = "Assets/Prefabs/Content/Meshes";

    static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();

    public static void ClearCache() => materials.Clear();

    // --------------------------------------------------------------- Meshes

    public static Mesh Box(float x, float y, float z, float bevel = 0.1f) =>
        BuildingMeshes.RoundedBox($"K_Box_{x:0.##}_{y:0.##}_{z:0.##}_{bevel:0.##}", new Vector3(x, y, z), Mathf.Min(bevel, Mathf.Min(x, Mathf.Min(y, z)) * 0.45f));

    public static Mesh Cylinder(float radius, float height, int segments = 24) =>
        BuildingMeshes.Lathe($"K_Cyl_{radius:0.##}_{height:0.##}_{segments}", new[]
        {
            new Vector2(0f, 0f), new Vector2(radius, 0f), new Vector2(radius, 0f),
            new Vector2(radius, height), new Vector2(radius, height), new Vector2(0f, height)
        }, segments);

    // A cone-ish nozzle (bell) for thrusters
    public static Mesh Bell(float throat, float mouth, float height) =>
        BuildingMeshes.Lathe($"K_Bell_{throat:0.##}_{mouth:0.##}_{height:0.##}", new[]
        {
            new Vector2(0f, height), new Vector2(throat, height), new Vector2(throat * 1.1f, height * 0.7f),
            new Vector2(mouth * 0.7f, height * 0.3f), new Vector2(mouth, 0f), new Vector2(mouth * 0.92f, 0f), new Vector2(0f, height * 0.2f)
        }, 28);

    public static Mesh Sphere => Resources.GetBuiltinResource<Mesh>("Sphere.fbx");

    // A lumpy, flat-shaded rock (different for every seed)
    public static Mesh Rock(int seed, float roughness = 0.25f)
    {
        string path = $"{MeshFolder}/K_Rock_{seed}_{roughness:0.##}.asset";
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing != null) return existing;

        // Icosphere-ish start: a subdivided octahedron pushed out to a sphere
        var points = new List<Vector3> { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back };
        var tris = new List<int> { 0, 4, 3, 0, 3, 5, 0, 5, 2, 0, 2, 4, 1, 3, 4, 1, 5, 3, 1, 2, 5, 1, 4, 2 };
        for (int s = 0; s < 2; s++)
        {
            var next = new List<int>();
            var mid = new Dictionary<long, int>();
            int Mid(int a, int b)
            {
                long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                if (mid.TryGetValue(key, out int i)) return i;
                points.Add(((points[a] + points[b]) * 0.5f).normalized);
                mid[key] = points.Count - 1;
                return points.Count - 1;
            }
            for (int t = 0; t < tris.Count; t += 3)
            {
                int a = tris[t], b = tris[t + 1], c = tris[t + 2];
                int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                next.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
            }
            tris = next;
        }
        var rng = new System.Random(seed);
        Vector3 o = new Vector3((float)rng.NextDouble() * 50f, (float)rng.NextDouble() * 50f, (float)rng.NextDouble() * 50f);
        for (int i = 0; i < points.Count; i++)
        {
            Vector3 p = points[i];
            float n = Mathf.PerlinNoise(p.x * 1.7f + o.x, p.y * 1.7f + o.y) + Mathf.PerlinNoise(p.z * 1.7f + o.z, p.x * 1.7f + o.x) - 1f;
            points[i] = p * (1f + n * roughness * 2f);
        }
        // Flat shading: every triangle gets its own vertices
        var v = new Vector3[tris.Count];
        var idx = new int[tris.Count];
        for (int i = 0; i < tris.Count; i++) { v[i] = points[tris[i]]; idx[i] = i; }
        var mesh = new Mesh { name = $"K_Rock_{seed}", vertices = v, triangles = idx };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        EnsureFolder(MeshFolder);
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    // --------------------------------------------------------------- Parts

    public static Transform Group(Transform parent, string name, Vector3 position = default, Vector3 euler = default)
    {
        var g = new GameObject(name).transform;
        g.SetParent(parent, false);
        g.localPosition = position;
        g.localRotation = Quaternion.Euler(euler);
        return g;
    }

    public static GameObject Part(Transform parent, string name, Mesh mesh, Material material, Vector3 position,
                                  Vector3 euler = default, Vector3? scale = null, bool collider = false)
    {
        var part = new GameObject(name);
        part.transform.SetParent(parent, false);
        part.transform.localPosition = position;
        part.transform.localRotation = Quaternion.Euler(euler);
        part.transform.localScale = scale ?? Vector3.one;
        part.AddComponent<MeshFilter>().sharedMesh = mesh;
        part.AddComponent<MeshRenderer>().sharedMaterial = material;
        if (collider)
        {
            var mc = part.AddComponent<MeshCollider>();
            mc.sharedMesh = mesh;
            mc.convex = false; // Static scenery: exact shape, no 256-polygon hull limit
        }
        return part;
    }

    // --------------------------------------------------------------- Materials

    public static Material Mat(string name, Color color, float metallic = 0.1f, float smoothness = 0.4f, Color emission = default)
    {
        if (materials.TryGetValue(name, out Material cached) && cached != null) return cached;
        EnsureFolder(MaterialFolder);
        string path = $"{MaterialFolder}/{name}.mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(m, path);
        }
        m.color = color;
        m.SetColor("_BaseColor", color);
        m.SetFloat("_Metallic", metallic);
        m.SetFloat("_Smoothness", smoothness);
        if (emission.maxColorComponent > 0f)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", emission);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        else
        {
            m.DisableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", Color.black);
        }
        EditorUtility.SetDirty(m);
        materials[name] = m;
        return m;
    }

    // Shared palette (matches the buildings: white hull, gunmetal, orange accent)
    public static Material Hull => Mat("Hull", new Color(0.82f, 0.83f, 0.84f), 0.1f, 0.42f);
    public static Material HullDark => Mat("HullDark", new Color(0.5f, 0.52f, 0.55f), 0.35f, 0.45f);
    public static Material Gunmetal => Mat("Gunmetal", new Color(0.19f, 0.2f, 0.22f), 0.6f, 0.45f);
    public static Material Accent => Mat("Accent", new Color(0.86f, 0.47f, 0.2f), 0.1f, 0.35f);
    public static Material Regolith => Mat("Regolith", new Color(0.55f, 0.54f, 0.52f), 0f, 0.08f);
    public static Material Screen => Mat("Screen", new Color(0.05f, 0.1f, 0.15f), 0f, 0.9f, new Color(0.12f, 0.4f, 0.55f));
    public static Material Glass => Mat("GlassPane", new Color(0.55f, 0.75f, 0.85f), 0.2f, 0.95f);
    public static Material Foil => Mat("Foil", new Color(0.78f, 0.6f, 0.28f), 0.9f, 0.6f);

    public static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
    }
}
