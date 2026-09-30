using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Builds the 3D models of the sample items out of generated meshes and simple shapes (low-poly style to match
// the cute astronaut). Every model has its pivot on the ground (y = 0). Meshes and materials are saved as assets
// so prefabs can use them; rebuilding updates the same assets instead of making new ones.
// Sizes are in scene units: the astronaut is about 16 units tall.
public static class ItemModels
{
    const string MeshFolder = "Assets/Prefabs/Items/Meshes";
    const string MaterialFolder = "Assets/Prefabs/Items/Materials";

    // --------------------------------------------------------------- Moon Rock

    // A lumpy, flat-shaded rock with a couple of small craters
    public static GameObject MoonRock()
    {
        var root = new GameObject("Model");
        Mesh mesh = SaveMesh(RockMesh(1.25f, 7), "moon_rock");
        Part(root.transform, "Rock", mesh, Mat("Regolith", new Color(0.52f, 0.51f, 0.5f), 0f, 0.08f),
             new Vector3(0f, 0.55f, 0f), Quaternion.Euler(0f, 25f, 0f), new Vector3(1.15f, 0.72f, 1f));
        return root;
    }

    static Mesh RockMesh(float radius, int seed)
    {
        var random = new System.Random(seed);
        float ox = (float)random.NextDouble() * 100f, oy = (float)random.NextDouble() * 100f, oz = (float)random.NextDouble() * 100f;
        Vector3[] craters = { new Vector3(0.6f, 0.7f, 0.3f).normalized, new Vector3(-0.5f, 0.5f, -0.6f).normalized, new Vector3(0.2f, 0.4f, -0.9f).normalized };
        float[] craterSize = { 0.45f, 0.32f, 0.25f };

        Icosphere(2, out List<Vector3> points, out List<int> triangles);
        for (int i = 0; i < points.Count; i++)
        {
            Vector3 p = points[i];
            float r = 1f + 0.22f * Noise(p * 1.4f + new Vector3(ox, oy, oz)) + 0.08f * Noise(p * 3.5f + new Vector3(oz, ox, oy));
            for (int c = 0; c < craters.Length; c++)
            {
                float d = Vector3.Angle(p, craters[c]) * Mathf.Deg2Rad / craterSize[c];
                if (d < 1f) r -= 0.13f * (1f - d * d);          // Bowl
                else if (d < 1.4f) r += 0.04f * (1.4f - d) / 0.4f; // Raised rim
            }
            points[i] = p * r * radius;
        }
        return FlatShaded(points, triangles, "MoonRock");
    }

    // --------------------------------------------------------------- Helium-3

    // A small gas canister: white body, glowing cyan window band, dark valve on top
    public static GameObject Helium3()
    {
        var root = new GameObject("Model");
        Mesh body = SaveMesh(Lathe(new[] {
            V(0f, 0f), V(0.62f, 0f), V(0.72f, 0.1f), V(0.72f, 0.1f), V(0.72f, 1.95f), V(0.72f, 1.95f),
            V(0.6f, 2.2f), V(0.34f, 2.32f), V(0.26f, 2.34f), V(0f, 2.34f) }, 28), "helium3_body");
        Mesh band = SaveMesh(Lathe(new[] { V(0.7f, 0.75f), V(0.76f, 0.78f), V(0.76f, 1.32f), V(0.7f, 1.35f) }, 28), "helium3_band");
        Mesh ring = SaveMesh(Lathe(new[] { V(0.7f, 0.12f), V(0.77f, 0.15f), V(0.77f, 0.3f), V(0.7f, 0.33f) }, 28), "helium3_ring");
        Mesh valve = SaveMesh(Lathe(new[] {
            V(0f, 2.3f), V(0.24f, 2.3f), V(0.24f, 2.55f), V(0.24f, 2.55f), V(0.34f, 2.58f), V(0.34f, 2.7f), V(0f, 2.72f) }, 16), "helium3_valve");

        Material dark = Mat("DarkMetal", new Color(0.18f, 0.19f, 0.22f), 0.7f, 0.5f);
        Part(root.transform, "Body", body, Mat("WhiteMetal", new Color(0.88f, 0.9f, 0.92f), 0.35f, 0.6f));
        Part(root.transform, "Band", band, Mat("HeliumGlow", new Color(0.3f, 0.85f, 1f), 0f, 0.8f, new Color(0.25f, 1.4f, 2.2f)));
        Part(root.transform, "Ring", ring, dark);
        Part(root.transform, "Valve", valve, dark);
        return root;
    }

    // --------------------------------------------------------------- Circuit Board

    // A green board with chips, gold contacts and a small LED
    public static GameObject CircuitBoard()
    {
        var root = new GameObject("Model");
        Mesh cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        Mesh sphere = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
        Material board = Mat("CircuitBoard", new Color(0.12f, 0.45f, 0.24f), 0f, 0.45f);
        Material chip = Mat("Chip", new Color(0.08f, 0.08f, 0.1f), 0.1f, 0.55f);
        Material gold = Mat("Gold", new Color(0.95f, 0.72f, 0.3f), 1f, 0.7f);
        Material led = Mat("LedRed", new Color(1f, 0.3f, 0.2f), 0f, 0.8f, new Color(2.5f, 0.4f, 0.2f));

        const float t = 0.12f; // Board thickness
        Part(root.transform, "Board", cube, board, new Vector3(0f, t / 2f, 0f), Quaternion.identity, new Vector3(2.4f, t, 1.6f));
        Part(root.transform, "Processor", cube, chip, new Vector3(-0.35f, t + 0.06f, 0.1f), Quaternion.identity, new Vector3(0.7f, 0.12f, 0.7f));
        Part(root.transform, "ChipA", cube, chip, new Vector3(0.6f, t + 0.05f, 0.35f), Quaternion.identity, new Vector3(0.45f, 0.1f, 0.3f));
        Part(root.transform, "ChipB", cube, chip, new Vector3(0.6f, t + 0.05f, -0.2f), Quaternion.identity, new Vector3(0.45f, 0.1f, 0.3f));
        Part(root.transform, "Led", sphere, led, new Vector3(0.95f, t + 0.04f, -0.55f), Quaternion.identity, Vector3.one * 0.14f);

        // Gold contacts along the front edge and pins around the processor
        for (int i = 0; i < 9; i++)
            Part(root.transform, "Contact", cube, gold, new Vector3(-1f + i * 0.25f, t + 0.005f, -0.72f), Quaternion.identity, new Vector3(0.14f, 0.01f, 0.16f));
        for (int i = 0; i < 5; i++)
        {
            float o = -0.24f + i * 0.12f;
            Part(root.transform, "Pin", cube, gold, new Vector3(-0.35f + o, t + 0.01f, 0.5f), Quaternion.identity, new Vector3(0.05f, 0.02f, 0.1f));
            Part(root.transform, "Pin", cube, gold, new Vector3(-0.35f + o, t + 0.01f, -0.3f), Quaternion.identity, new Vector3(0.05f, 0.02f, 0.1f));
        }
        // Traces
        Part(root.transform, "Trace", cube, gold, new Vector3(0.2f, t + 0.003f, 0.35f), Quaternion.identity, new Vector3(0.5f, 0.005f, 0.035f));
        Part(root.transform, "Trace", cube, gold, new Vector3(0.2f, t + 0.003f, -0.2f), Quaternion.identity, new Vector3(0.5f, 0.005f, 0.035f));
        Part(root.transform, "Trace", cube, gold, new Vector3(0.95f, t + 0.003f, -0.1f), Quaternion.identity, new Vector3(0.035f, 0.005f, 0.8f));
        return root;
    }

    // --------------------------------------------------------------- Drill

    // A cordless drill lying on its side: orange body, dark grip and battery, threaded steel bit
    public static GameObject Drill()
    {
        var model = new GameObject("Model");
        var drill = new GameObject("Drill").transform; // Built standing like a pistol, then laid down
        drill.SetParent(model.transform, false);

        Mesh cylinder = Resources.GetBuiltinResource<Mesh>("Cylinder.fbx");
        Mesh cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        Material orange = Mat("ToolOrange", new Color(1f, 0.55f, 0.12f), 0.1f, 0.5f);
        Material dark = Mat("DarkMetal", new Color(0.18f, 0.19f, 0.22f), 0.7f, 0.5f);
        Material rubber = Mat("Rubber", new Color(0.12f, 0.12f, 0.13f), 0f, 0.25f);
        Material steel = Mat("Steel", new Color(0.78f, 0.8f, 0.84f), 1f, 0.75f);

        Quaternion alongX = Quaternion.Euler(0f, 0f, 90f); // Cylinders point up by default
        Part(drill, "Motor", cylinder, orange, new Vector3(0f, 1.6f, 0f), alongX, new Vector3(0.62f, 0.8f, 0.62f));
        Part(drill, "BackCap", cylinder, dark, new Vector3(-0.83f, 1.6f, 0f), alongX, new Vector3(0.5f, 0.05f, 0.5f));
        Part(drill, "Chuck", cylinder, dark, new Vector3(1.0f, 1.6f, 0f), alongX, new Vector3(0.36f, 0.22f, 0.36f));
        Mesh bit = SaveMesh(Lathe(BitProfile(), 12), "drill_bit");
        Part(drill, "Bit", bit, steel, new Vector3(1.2f, 1.6f, 0f), Quaternion.Euler(0f, 0f, -90f), Vector3.one);
        Part(drill, "Grip", cube, rubber, new Vector3(-0.2f, 0.85f, 0f), Quaternion.Euler(0f, 0f, -12f), new Vector3(0.42f, 1.2f, 0.4f));
        Part(drill, "Trigger", cube, dark, new Vector3(0.08f, 1.15f, 0f), Quaternion.identity, new Vector3(0.14f, 0.24f, 0.18f));
        Part(drill, "Battery", cube, orange, new Vector3(-0.1f, 0.15f, 0f), Quaternion.identity, new Vector3(0.9f, 0.3f, 0.62f));
        Part(drill, "BatteryBand", cube, dark, new Vector3(-0.1f, 0.33f, 0f), Quaternion.identity, new Vector3(0.92f, 0.06f, 0.64f));

        drill.localRotation = Quaternion.Euler(90f, 0f, 0f); // Lay it on its side
        GroundPivot(model.transform);
        return model;
    }

    // Threaded look: the radius goes in and out along the bit, with a pointed tip
    static Vector2[] BitProfile()
    {
        var profile = new List<Vector2> { V(0f, 0f), V(0.1f, 0f) };
        for (int i = 0; i < 7; i++)
        {
            profile.Add(V(0.1f, 0.12f + i * 0.13f));
            profile.Add(V(0.065f, 0.18f + i * 0.13f));
        }
        profile.Add(V(0.09f, 1.05f));
        profile.Add(V(0f, 1.25f));
        return profile.ToArray();
    }

    // --------------------------------------------------------------- Helpers

    static Vector2 V(float radius, float y) => new Vector2(radius, y);

    // Adds a mesh part (no collider; the item's root gets one box around everything)
    static void Part(Transform parent, string name, Mesh mesh, Material material)
        => Part(parent, name, mesh, material, Vector3.zero, Quaternion.identity, Vector3.one);

    static void Part(Transform parent, string name, Mesh mesh, Material material, Vector3 position, Quaternion rotation, Vector3 scale)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.SetLocalPositionAndRotation(position, rotation);
        go.transform.localScale = scale;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = material;
    }

    // Moves the children so the lowest point sits at y = 0
    static void GroundPivot(Transform model)
    {
        float lowest = float.MaxValue;
        foreach (Renderer r in model.GetComponentsInChildren<Renderer>()) lowest = Mathf.Min(lowest, r.bounds.min.y);
        if (lowest == float.MaxValue) return;
        foreach (Transform child in model) child.position += Vector3.up * (model.position.y - lowest);
    }

    // Surface of revolution around the Y axis. Profile points are (radius, height) from bottom to top;
    // repeat a point to get a sharp edge there.
    static Mesh Lathe(Vector2[] profile, int segments)
    {
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        int ring = segments + 1;
        foreach (Vector2 p in profile)
            for (int s = 0; s <= segments; s++)
            {
                float a = s / (float)segments * Mathf.PI * 2f;
                vertices.Add(new Vector3(Mathf.Cos(a) * p.x, p.y, Mathf.Sin(a) * p.x));
            }
        for (int i = 0; i < profile.Length - 1; i++)
        {
            if (profile[i] == profile[i + 1]) continue; // Hard edge marker: no faces between the copies
            for (int s = 0; s < segments; s++)
            {
                int a = i * ring + s, b = a + 1, c = a + ring, d = c + 1;
                triangles.AddRange(new[] { a, c, b, b, c, d });
            }
        }
        var mesh = new Mesh { name = "Lathe" };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    static void Icosphere(int subdivisions, out List<Vector3> points, out List<int> triangles)
    {
        float t = (1f + Mathf.Sqrt(5f)) / 2f;
        points = new List<Vector3> {
            new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
            new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
            new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1) };
        for (int i = 0; i < points.Count; i++) points[i] = points[i].normalized;
        triangles = new List<int> {
            0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11, 1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
            3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9, 4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1 };

        for (int level = 0; level < subdivisions; level++)
        {
            var middles = new Dictionary<long, int>();
            var next = new List<int>();
            for (int i = 0; i < triangles.Count; i += 3)
            {
                int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                int ab = Middle(a, b, points, middles), bc = Middle(b, c, points, middles), ca = Middle(c, a, points, middles);
                next.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
            }
            triangles = next;
        }
    }

    static int Middle(int a, int b, List<Vector3> points, Dictionary<long, int> cache)
    {
        long key = a < b ? ((long)a << 32) + b : ((long)b << 32) + a;
        if (cache.TryGetValue(key, out int index)) return index;
        points.Add(((points[a] + points[b]) * 0.5f).normalized);
        cache[key] = points.Count - 1;
        return points.Count - 1;
    }

    // Every triangle gets its own vertices, so the faces look faceted (low-poly)
    static Mesh FlatShaded(List<Vector3> points, List<int> triangles, string name)
    {
        var vertices = new Vector3[triangles.Count];
        var indices = new int[triangles.Count];
        for (int i = 0; i < triangles.Count; i++)
        {
            vertices[i] = points[triangles[i]];
            indices[i] = i;
        }
        var mesh = new Mesh { name = name, vertices = vertices, triangles = indices };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    // Smooth 3D noise in about -1..1 from three Perlin planes
    static float Noise(Vector3 p)
    {
        float n = Mathf.PerlinNoise(p.x, p.y) + Mathf.PerlinNoise(p.y + 31.7f, p.z) + Mathf.PerlinNoise(p.z + 57.3f, p.x);
        return n / 1.5f - 1f;
    }

    static Mesh SaveMesh(Mesh mesh, string name)
    {
        EnsureFolder(MeshFolder);
        string path = $"{MeshFolder}/{name}.asset";
        mesh.name = name;
        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing == null)
        {
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }
        EditorUtility.CopySerialized(mesh, existing); // Keep the asset (and its references), replace the data
        Object.DestroyImmediate(mesh);
        return existing;
    }

    static Material Mat(string name, Color color, float metallic, float smoothness, Color emission = default)
    {
        EnsureFolder(MaterialFolder);
        string path = $"{MaterialFolder}/{name}.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        bool isNew = material == null;
        if (isNew) material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));

        material.color = color;
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        material.SetFloat("_Metallic", metallic);
        material.SetFloat("_Smoothness", smoothness);
        material.SetFloat("_Glossiness", smoothness);
        if (emission.maxColorComponent > 0f)
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", emission);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }

        if (isNew) AssetDatabase.CreateAsset(material, path);
        else EditorUtility.SetDirty(material);
        return material;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int slash = path.LastIndexOf('/');
        EnsureFolder(path.Substring(0, slash));
        AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
    }
}
