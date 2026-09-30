using UnityEngine;

// Helpers shared by placement and construction: a copy of a building that only has its looks,
// its size on the ground, and a see-through material.
public static class BuildGhost
{
    // A copy without colliders and scripts (so it can't block anything or run game code)
    public static GameObject VisualCopy(GameObject prefab, Transform parent)
    {
        var holder = new GameObject("Holder");
        holder.SetActive(false); // Nothing on the copy wakes up while it is being cleaned
        GameObject copy = Object.Instantiate(prefab, holder.transform);
        foreach (MonoBehaviour script in copy.GetComponentsInChildren<MonoBehaviour>(true)) Object.DestroyImmediate(script);
        foreach (Collider c in copy.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
        copy.transform.SetParent(parent, false);
        copy.transform.localPosition = Vector3.zero;
        copy.transform.localRotation = Quaternion.identity;
        Object.Destroy(holder);
        return copy;
    }

    // Size of the building in its own space: x/z = footprint on the ground, y = height
    public static Bounds LocalBounds(GameObject building)
    {
        Transform root = building.transform;
        Bounds bounds = new Bounds(Vector3.zero, Vector3.zero);
        bool any = false;
        foreach (MeshFilter mf in building.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;
            Bounds mb = mf.sharedMesh.bounds;
            Matrix4x4 toRoot = root.worldToLocalMatrix * mf.transform.localToWorldMatrix;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 p = toRoot.MultiplyPoint3x4(corner);
                if (!any) { bounds = new Bounds(p, Vector3.zero); any = true; }
                else bounds.Encapsulate(p);
            }
        }
        return bounds;
    }

    // Unlit and see-through (URP): for the placement preview and the base border
    public static Material CreateTransparentMaterial(Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        var mat = new Material(shader);
        mat.SetColor("_BaseColor", color);
        mat.color = color;
        mat.SetFloat("_Surface", 1f); // Transparent
        mat.SetFloat("_Blend", 0f);   // Alpha blending
        mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_ZWrite", 0f);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        return mat;
    }

    public static void SetMaterial(GameObject go, Material material)
    {
        foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
        {
            var mats = new Material[r.sharedMaterials.Length];
            for (int i = 0; i < mats.Length; i++) mats[i] = material;
            r.sharedMaterials = mats;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }
}
