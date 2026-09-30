using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Mesh shapes for the base buildings, made in code and saved as assets (Assets/Prefabs/Buildings/Meshes):
//  - Lathe: a profile line turned around the Y axis, like on a lathe (domes, tanks, poles, rings)
//  - RoundedBox: a box whose edges and corners are rounded, so it catches the light like a real object
public static class BuildingMeshes
{
    const string Folder = "Assets/Prefabs/Buildings/Meshes";
    static readonly Dictionary<string, Mesh> cache = new Dictionary<string, Mesh>();

    public static void ClearCache() => cache.Clear();

    // profile: (radius, height) points from bottom to top. Repeat a point to get a sharp edge there.
    // Start/end with radius 0 to close the shape.
    public static Mesh Lathe(string name, Vector2[] profile, int segments = 48)
    {
        if (cache.TryGetValue(name, out Mesh cached)) return cached;
        int rings = profile.Length, columns = segments + 1;
        var vertices = new Vector3[rings * columns];
        var normals = new Vector3[rings * columns];
        var uvs = new Vector2[rings * columns];

        float total = 0f, run = 0f;
        for (int i = 1; i < rings; i++) total += Vector2.Distance(profile[i], profile[i - 1]);

        for (int i = 0; i < rings; i++)
        {
            if (i > 0) run += Vector2.Distance(profile[i], profile[i - 1]);
            Vector2 n2 = ProfileNormal(profile, i);
            for (int j = 0; j < columns; j++)
            {
                float angle = j * Mathf.PI * 2f / segments;
                float c = Mathf.Cos(angle), s = Mathf.Sin(angle);
                int k = i * columns + j;
                vertices[k] = new Vector3(profile[i].x * c, profile[i].y, profile[i].x * s);
                normals[k] = new Vector3(n2.x * c, n2.y, n2.x * s).normalized;
                uvs[k] = new Vector2(j / (float)segments, total > 0f ? run / total : 0f);
            }
        }

        var triangles = new List<int>();
        for (int i = 0; i < rings - 1; i++)
        {
            if (profile[i] == profile[i + 1]) continue; // The two copies of a sharp edge
            for (int j = 0; j < segments; j++)
            {
                int a = i * columns + j, b = a + 1, up = a + columns, upB = up + 1;
                triangles.AddRange(new[] { a, up, b, up, upB, b }); // Faces point outward
            }
        }

        var mesh = new Mesh { name = name, vertices = vertices, normals = normals, uv = uvs };
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return Save(name, mesh);
    }

    // Outward normal of the profile line at point i (a sharp edge uses only its own side)
    static Vector2 ProfileNormal(Vector2[] p, int i)
    {
        bool dupNext = i < p.Length - 1 && p[i + 1] == p[i];
        bool dupPrev = i > 0 && p[i - 1] == p[i];
        Vector2 tangent = Vector2.zero;
        if (i > 0 && !dupPrev) tangent += (p[i] - p[i - 1]).normalized;
        if (i < p.Length - 1 && !dupNext) tangent += (p[i + 1] - p[i]).normalized;
        if (dupNext && i > 0) tangent = (p[i] - p[i - 1]).normalized;
        if (dupPrev && i < p.Length - 1) tangent = (p[i + 1] - p[i]).normalized;
        if (tangent.sqrMagnitude < 1e-6f) tangent = Vector2.up;
        return new Vector2(tangent.y, -tangent.x).normalized;
    }

    // A box of this size, centered on its pivot, with rounded edges (bevel = radius of the rounding)
    public static Mesh RoundedBox(string name, Vector3 size, float bevel, int steps = 3)
    {
        if (cache.TryGetValue(name, out Mesh cached)) return cached;
        Vector3 half = size * 0.5f;
        bevel = Mathf.Min(bevel, half.x, half.y, half.z) * 0.999f;
        Vector3 inner = half - Vector3.one * bevel;

        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var triangles = new List<int>();

        for (int axis = 0; axis < 3; axis++)
        for (int sign = -1; sign <= 1; sign += 2)
        {
            int u = (axis + 1) % 3, v = (axis + 2) % 3;
            float[] us = Coordinates(half[u], bevel, steps), vs = Coordinates(half[v], bevel, steps);
            int start = vertices.Count;
            Vector3 faceNormal = Vector3.zero;
            faceNormal[axis] = sign;

            foreach (float cv in vs)
            foreach (float cu in us)
            {
                Vector3 p = Vector3.zero;
                p[axis] = sign * half[axis];
                p[u] = cu;
                p[v] = cv;
                // Push the point onto the rounded surface: around the nearest point of the inner box
                Vector3 core = new Vector3(Mathf.Clamp(p.x, -inner.x, inner.x), Mathf.Clamp(p.y, -inner.y, inner.y), Mathf.Clamp(p.z, -inner.z, inner.z));
                Vector3 dir = (p - core).normalized;
                vertices.Add(core + dir * bevel);
                normals.Add(dir);
                uvs.Add(new Vector2((cu / half[u] + 1f) * 0.5f, (cv / half[v] + 1f) * 0.5f));
            }

            int w = us.Length;
            for (int y = 0; y < vs.Length - 1; y++)
            for (int x = 0; x < w - 1; x++)
            {
                int a = start + y * w + x, b = a + 1, c = a + w, d = c + 1;
                AddTriangle(triangles, vertices, a, b, c, faceNormal);
                AddTriangle(triangles, vertices, b, d, c, faceNormal);
            }
        }

        var mesh = new Mesh { name = name };
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return Save(name, mesh);
    }

    // Grid lines along one side: a few inside each rounded edge, none in the flat middle
    static float[] Coordinates(float half, float bevel, int steps)
    {
        var list = new List<float>();
        for (int k = 0; k <= steps; k++) list.Add(-half + bevel * k / steps);
        for (int k = 0; k <= steps; k++) list.Add(half - bevel * (steps - k) / steps);
        return list.ToArray();
    }

    // Keeps the triangle facing outward (Unity draws the side the triangle is wound clockwise on)
    static void AddTriangle(List<int> triangles, List<Vector3> v, int a, int b, int c, Vector3 outward)
    {
        Vector3 n = Vector3.Cross(v[b] - v[a], v[c] - v[a]);
        if (n.sqrMagnitude < 1e-10f) return; // Squashed triangle at a rounded corner
        if (Vector3.Dot(n, outward) >= 0f) triangles.AddRange(new[] { a, b, c });
        else triangles.AddRange(new[] { a, c, b });
    }

    // Quarter/part circle points for profiles: from angle a0 to a1 (degrees, 0 = outward, 90 = up)
    public static IEnumerable<Vector2> Arc(Vector2 center, float radius, float a0, float a1, int steps)
    {
        for (int i = 0; i <= steps; i++)
        {
            float a = Mathf.Lerp(a0, a1, i / (float)steps) * Mathf.Deg2Rad;
            yield return center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
        }
    }

    static Mesh Save(string name, Mesh mesh)
    {
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Prefabs/Buildings", "Meshes");
        string path = $"{Folder}/{name}.asset";
        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing != null)
        {
            EditorUtility.CopySerialized(mesh, existing); // Keep the asset (and everything pointing at it)
            Object.DestroyImmediate(mesh);
            mesh = existing;
        }
        else AssetDatabase.CreateAsset(mesh, path);
        cache[name] = mesh;
        return mesh;
    }
}
