using UnityEngine;

// A faraway body painted in the sky (the Sun, the Earth). It keeps a fixed DIRECTION in the sky and always stays
// the same distance from the camera, so you can never walk up to it - exactly like a real body in space.
//  - alignToLight set: it sits where that light comes FROM, so the Sun disk lines up with the shadows it casts.
//  - otherwise it sits in skyDirection (a fixed world direction, y up = higher in the sky).
[ExecuteAlways]
public class SkyBody : MonoBehaviour
{
    public Light alignToLight;                // The Sun: sits opposite this light's direction (matches the shadows)
    public Vector3 skyDirection = Vector3.up; // The Earth and others: a fixed direction to hang in the sky
    public float distance = 650f;             // Must stay inside the camera's far clip (2600)

    void LateUpdate()
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        Vector3 dir = alignToLight != null ? -alignToLight.transform.forward : skyDirection;
        if (dir.sqrMagnitude < 0.0001f) dir = Vector3.up;
        transform.position = cam.transform.position + dir.normalized * distance;
    }
}
