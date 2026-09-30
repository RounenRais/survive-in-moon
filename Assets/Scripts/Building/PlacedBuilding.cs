using System.Collections.Generic;
using UnityEngine;

// Marks something that takes up ground at the base (a building, a construction site, the hub, the terminal)
// and remembers its footprint: the rectangle it covers on the ground. Placement compares footprints, so a new
// building can never overlap another one - even a small crate fully inside a big dome, which a physics check
// against the dome's surface would miss.
public class PlacedBuilding : MonoBehaviour
{
    private static readonly List<PlacedBuilding> all = new List<PlacedBuilding>();
    public static IReadOnlyList<PlacedBuilding> All => all;

    private Bounds localBounds;

    void Awake() => localBounds = BuildGhost.LocalBounds(gameObject);
    void OnEnable() => all.Add(this);
    void OnDisable() => all.Remove(this);

    // The footprint in the world: center on the ground, size (x, z) and the object's turn
    public Footprint Footprint
    {
        get
        {
            Vector3 scale = transform.lossyScale;
            Vector3 center = transform.TransformPoint(new Vector3(localBounds.center.x, 0f, localBounds.center.z));
            return new Footprint(center, new Vector2(localBounds.size.x * scale.x, localBounds.size.z * scale.z), transform.rotation);
        }
    }

    public static bool AnyOverlaps(Footprint footprint, float gap)
    {
        foreach (PlacedBuilding building in all)
            if (building != null && building.Footprint.Overlaps(footprint, gap)) return true;
        return false;
    }
}

// A rotated rectangle on the ground (X/Z)
public readonly struct Footprint
{
    public readonly Vector2 center, axisX, axisZ, half;

    public Footprint(Vector3 center, Vector2 size, Quaternion rotation)
    {
        this.center = new Vector2(center.x, center.z);
        Vector3 right = rotation * Vector3.right, forward = rotation * Vector3.forward;
        axisX = new Vector2(right.x, right.z).normalized;
        axisZ = new Vector2(forward.x, forward.z).normalized;
        half = size * 0.5f;
    }

    // Separating axis test: two rectangles overlap unless some side's direction separates them
    public bool Overlaps(Footprint other, float gap)
    {
        foreach (Vector2 axis in new[] { axisX, axisZ, other.axisX, other.axisZ })
        {
            float distance = Mathf.Abs(Vector2.Dot(other.center - center, axis));
            float reachA = Mathf.Abs(Vector2.Dot(axisX, axis)) * half.x + Mathf.Abs(Vector2.Dot(axisZ, axis)) * half.y;
            float reachB = Mathf.Abs(Vector2.Dot(other.axisX, axis)) * other.half.x + Mathf.Abs(Vector2.Dot(other.axisZ, axis)) * other.half.y;
            if (distance > reachA + reachB + gap) return false;
        }
        return true;
    }
}
