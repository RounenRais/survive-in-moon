using System.Collections.Generic;
using UnityEngine;

// The round area around the base hub where buildings may be placed. Move the object to move the base,
// change the radius to make it bigger or smaller. A soft glowing ring shows it in the game;
// it gets brighter while placing.
public class BaseArea : MonoBehaviour
{
    public float radius = 300f; // In scene units (the astronaut is ~16 tall)
    public Color borderColor = new Color(1f, 0.68f, 0.32f, 0.3f);
    public Color highlightColor = new Color(1f, 0.68f, 0.32f, 0.85f);
    public float borderWidth = 1f;

    private LineRenderer border;
    private Material borderMaterial;

    void Start()
    {
        var go = new GameObject("Base Border");
        go.transform.SetParent(transform, false);
        border = go.AddComponent<LineRenderer>();
        border.useWorldSpace = true;
        border.loop = true;
        border.widthMultiplier = borderWidth;
        border.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        border.receiveShadows = false;
        borderMaterial = BuildGhost.CreateTransparentMaterial(Color.white);
        border.sharedMaterial = borderMaterial;

        // A point every ~3 units around the circle, each one dropped onto the ground, so the ring follows bumps
        int count = Mathf.Max(24, Mathf.CeilToInt(2f * Mathf.PI * radius / 3f));
        var points = new List<Vector3>(count);
        for (int i = 0; i < count; i++)
        {
            float angle = i * Mathf.PI * 2f / count;
            Vector3 p = transform.position + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
            points.Add(GroundPoint(p) + Vector3.up * 0.3f);
        }
        border.positionCount = points.Count;
        border.SetPositions(points.ToArray());
        SetHighlighted(false);
    }

    void OnDestroy()
    {
        if (borderMaterial != null) Destroy(borderMaterial);
    }

    public void SetHighlighted(bool on)
    {
        if (border == null) return;
        Color c = on ? highlightColor : borderColor;
        border.startColor = border.endColor = c;
        borderMaterial.SetColor("_BaseColor", c);
    }

    // True if the whole footprint (a box on the ground, rotated like the building) is inside the base
    public bool Contains(Vector3 center, Vector2 footprint, Quaternion rotation)
    {
        Vector3 halfX = rotation * Vector3.right * footprint.x * 0.5f;
        Vector3 halfZ = rotation * Vector3.forward * footprint.y * 0.5f;
        foreach (Vector3 corner in new[] { center + halfX + halfZ, center + halfX - halfZ, center - halfX + halfZ, center - halfX - halfZ })
            if (!Contains(corner)) return false;
        return true;
    }

    public bool Contains(Vector3 point) =>
        Vector3.ProjectOnPlane(point - transform.position, Vector3.up).magnitude <= radius;

    // The ground is the lowest thing hit straight down (buildings, rocks and astronauts stand on top of it)
    public static Vector3 GroundPoint(Vector3 point)
    {
        Vector3 best = point;
        float lowest = float.MaxValue;
        foreach (RaycastHit hit in Physics.RaycastAll(point + Vector3.up * 100f, Vector3.down, 300f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.point.y < lowest) { lowest = hit.point.y; best = hit.point; }
        }
        return best;
    }

    void OnDrawGizmos()
    {
        Gizmos.color = highlightColor;
        const int segments = 64;
        for (int i = 0; i < segments; i++)
        {
            float a = i * Mathf.PI * 2f / segments, b = (i + 1) * Mathf.PI * 2f / segments;
            Gizmos.DrawLine(transform.position + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius,
                            transform.position + new Vector3(Mathf.Cos(b), 0f, Mathf.Sin(b)) * radius);
        }
    }
}
