using UnityEngine;

// A big amber arrow on the ground in front of the player that points the way to the current objective
// (ObjectiveUI.Target). It gently pulses, and disappears once you are there.
public class ObjectiveArrow : MonoBehaviour
{
    public float distanceAhead = 14f;
    public float size = 9f;
    public float hideWithin = 25f;

    private Transform arrow;

    void Start()
    {
        Sprite icon = UITheme.Icon("arrow");
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = "Objective Arrow";
        Destroy(go.GetComponent<Collider>());
        arrow = go.transform;
        var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        if (icon != null) m.SetTexture("_BaseMap", icon.texture);
        m.SetColor("_BaseColor", new Color(1f, 0.7f, 0.25f, 0.95f));
        m.SetFloat("_Surface", 1f);
        m.SetOverrideTag("RenderType", "Transparent");
        m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.SetInt("_ZWrite", 0);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        var r = go.GetComponent<MeshRenderer>();
        r.sharedMaterial = m;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    void LateUpdate()
    {
        if (arrow == null) return;
        SurvivalStats player = SurvivalStats.Player;
        Transform target = ObjectiveUI.Target;
        bool show = player != null && target != null && !InteractionSystem.MenuOpen;
        Vector3 to = show ? Vector3.ProjectOnPlane(target.position - player.transform.position, Vector3.up) : Vector3.zero;
        show &= to.magnitude > hideWithin;
        arrow.gameObject.SetActive(show);
        if (!show) return;

        Vector3 dir = to.normalized;
        Vector3 spot = BaseArea.GroundPoint(player.transform.position + dir * distanceAhead) + Vector3.up * 0.6f;
        arrow.position = spot;
        // Lying flat, its tip pointing at the target
        arrow.rotation = Quaternion.LookRotation(Vector3.down, dir);
        float pulse = 1f + Mathf.Sin(Time.time * 4f) * 0.08f;
        bool driving = player.GetComponent<PlayerMovement>()?.InVehicle ?? false;
        arrow.localScale = Vector3.one * size * pulse * (driving ? 2f : 1f);
    }
}
