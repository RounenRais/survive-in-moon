using UnityEngine;

// Step/landing dust on the moon surface. Without air, dust doesn't spread like a cloud: it draws
// clean arcs under moon gravity and falls back down. The particle system is built at runtime.
[RequireComponent(typeof(PlayerMovement))]
public class MoonDust : MonoBehaviour
{
    public Color dustColor = new Color(0.62f, 0.6f, 0.57f, 1f);
    public float landingDust = 1f; // Multiplier for landing/hop dust amount
    public float shuffleDust = 1f; // Multiplier for the fine dust while shuffling on the ground

    private PlayerMovement movement;
    private CharacterController controller;
    private ParticleSystem ps;
    private GameObject psObject;
    private Material dustMaterial;
    private Texture2D dustTexture;
    private float characterHeight; // Character height in world scale; all values scale with it
    private float shuffleCounter;
    private bool wasInAir;

    void Start()
    {
        movement = GetComponent<PlayerMovement>();
        controller = GetComponent<CharacterController>();
        characterHeight = controller != null ? controller.height * transform.lossyScale.y : 2f;

        Build();
        movement.Landed += landingSpeed =>
        {
            if (landingSpeed > 0.5f) Burst(Mathf.Clamp01(landingSpeed / 4f), 1f);
        };
    }

    void Update()
    {
        // Taking off for a hop, the foot pushes the ground and kicks some dust backwards
        if (movement.IsInAir && !wasInAir && movement.VerticalSpeed > 0f)
            Burst(0.35f * Mathf.Max(movement.SpeedRatio, 0.4f), -1f);
        wasInAir = movement.IsInAir;

        // Fine dust from the feet dragging while walking slowly (no hops)
        if (!movement.IsInAir && movement.SpeedRatio > 0.15f)
        {
            shuffleCounter += Time.deltaTime * 12f * movement.SpeedRatio * shuffleDust;
            while (shuffleCounter >= 1f)
            {
                shuffleCounter -= 1f;
                EmitParticle(0.15f, -0.6f);
            }
        }
    }

    // strength: 0-1 dust intensity. direction: +1 carried along the movement (landing), -1 thrown backwards (takeoff)
    void Burst(float strength, float direction)
    {
        int count = Mathf.RoundToInt(Mathf.Lerp(6f, 36f, strength) * landingDust);
        for (int i = 0; i < count; i++) EmitParticle(Mathf.Lerp(0.4f, 1f, strength), direction);
    }

    void EmitParticle(float strength, float direction)
    {
        Bounds b = controller.bounds;
        float radius = controller.radius * transform.lossyScale.x;

        Vector2 circle = Random.insideUnitCircle;
        Vector3 outward = new Vector3(circle.x, 0f, circle.y);
        Vector3 position = new Vector3(b.center.x, b.min.y + characterHeight * 0.01f, b.center.z) + outward * radius * 0.7f;

        // Thrown outward and up at a low angle (like regolith grains)
        Vector3 velocity = outward.normalized * Random.Range(0.08f, 0.3f) * characterHeight * strength
                         + Vector3.up * Random.Range(0.05f, 0.22f) * characterHeight * strength
                         + movement.HorizontalVelocity * 0.35f * direction;

        var ep = new ParticleSystem.EmitParams
        {
            position = position,
            velocity = velocity,
            startSize = Random.Range(0.03f, 0.08f) * characterHeight * Mathf.Lerp(0.6f, 1f, strength),
            startLifetime = Random.Range(1.2f, 2.4f),
            startColor = dustColor * Random.Range(0.8f, 1.1f),
            rotation = Random.Range(0f, 360f)
        };
        ps.Emit(ep, 1);
    }

    void Build()
    {
        psObject = new GameObject("MoonDust");
        ps = psObject.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.playOnAwake = false;
        main.loop = true;
        main.maxParticles = 600;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        // Dust falls with the same moon gravity as the character
        main.gravityModifier = Mathf.Abs(movement.gravity) / Mathf.Max(0.01f, -Physics.gravity.y);

        var emission = ps.emission;
        emission.rateOverTime = 0f;

        // Appears bright, then fades as it settles on the ground
        var colorOverLifetime = ps.colorOverLifetime;
        colorOverLifetime.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.8f, 0.06f),
                    new GradientAlphaKey(0.5f, 0.6f), new GradientAlphaKey(0f, 1f) });
        colorOverLifetime.color = gradient;

        var sizeOverLifetime = ps.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.7f, 1f, 1.3f));

        // Stops on hitting the ground without bouncing (no floating in a vacuum)
        var collision = ps.collision;
        collision.enabled = true;
        collision.type = ParticleSystemCollisionType.World;
        collision.mode = ParticleSystemCollisionMode.Collision3D;
        collision.dampen = 0.85f;
        collision.bounce = 0.05f;
        collision.quality = ParticleSystemCollisionQuality.Medium;
        collision.enableDynamicColliders = false;

        var dustRenderer = psObject.GetComponent<ParticleSystemRenderer>();
        dustRenderer.renderMode = ParticleSystemRenderMode.Billboard;
        dustRenderer.material = dustMaterial = CreateMaterial();

        ps.Play();
    }

    Material CreateMaterial()
    {
        // Soft-edged circle texture
        const int n = 32;
        dustTexture = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f)) / (n / 2f);
                float a = Mathf.SmoothStep(1f, 0f, d) * Mathf.Lerp(0.8f, 1f, Random.value);
                dustTexture.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
            }
        dustTexture.Apply();
        return CreateParticleMaterial(dustTexture);
    }

    // A see-through particle material for URP: soft round puffs instead of opaque squares.
    // (URP's particle shader is opaque until it is switched to "Transparent" like below.)
    public static Material CreateParticleMaterial(Texture2D texture)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        var mat = new Material(shader);
        mat.SetTexture("_BaseMap", texture);
        mat.mainTexture = texture;
        mat.SetFloat("_Surface", 1f); // Transparent
        mat.SetFloat("_Blend", 0f);   // Alpha blending
        mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_ZWrite", 0f);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        return mat;
    }

    void OnDestroy()
    {
        if (psObject != null) Destroy(psObject);
        if (dustMaterial != null) Destroy(dustMaterial);
        if (dustTexture != null) Destroy(dustTexture);
    }
}
