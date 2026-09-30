using System.Collections.Generic;
using UnityEngine;

// Lunar Roving Vehicle (LRV): a drivable moon car.
//  - "Raycast vehicle": each wheel shoots a ray down and pushes the body up like a spring (suspension),
//    then grips the ground sideways and drives it forward. More stable and controllable than WheelColliders.
//  - Four-wheel drive and four-wheel steering like the real LRV (rear wheels turn the other way = tight turns)
//  - F near the rover gets in, F while driving gets out. A key prompt appears above the rover when you are close.
// The model and all references are made by Survive In Moon > Create Moon Rover (MoonRoverBuilder).
[RequireComponent(typeof(Rigidbody))]
public class MoonRover : MonoBehaviour
{
    [Header("Parts (filled in by the builder)")]
    public Transform[] wheelPivots = new Transform[4];   // FL, FR, RL, RR: turn for steering
    public Transform[] wheelSpinners = new Transform[4]; // Children of the pivots: roll and move with the suspension
    public Transform driverSeat;   // The astronaut is attached here while driving
    public Transform hipsAnchor;   // Where the astronaut's hips sit
    public Transform promptPoint;  // Where the "F" prompt floats
    public float wheelRadius = 3.6f;

    [Header("Driving")]
    public float maxSpeed = 32f;
    public float reverseSpeed = 12f;
    public float acceleration = 14f;
    public float brakeDeceleration = 30f;
    public float maxSteerAngle = 28f;
    public bool rearSteering = true;
    [Tooltip("How strongly the tires stop sideways sliding (higher = less drifting)")]
    public float grip = 6f;
    public float tireFriction = 1.6f;

    [Header("Suspension")]
    public float suspensionTravel = 2.4f;
    [Range(0.1f, 1f)] public float dampingRatio = 0.45f;

    [Header("Moon")]
    [Tooltip("Heavier than the astronaut's floaty jumps so the rover stays controllable")]
    public float gravity = 9f;

    [Header("Interaction")]
    public float enterDistance = 14f;
    public KeyCode interactKey = KeyCode.F;

    public PlayerMovement Driver { get; private set; }
    public float ForwardSpeed { get; private set; }

    static readonly List<MoonRover> all = new List<MoonRover>();

    private Rigidbody rb;
    private Collider[] ownColliders;
    private Vector3[] mounts;            // Local top point of each wheel's suspension
    private float[] wheelOffset;         // How far each wheel hangs below its mount (for the visuals)
    private float[] spin;
    private readonly RaycastHit[] hits = new RaycastHit[8];
    private float throttle, steer;
    private bool handbrake;
    private int enteredFrame = -1;
    private float upsideDownTime;
    private ParticleSystem dust;
    private float dustCounter;
    private PlayerMovement player; // For the prompt
    private GUIStyle keyStyle, labelStyle, hintStyle;

    void OnEnable() => all.Add(this);
    void OnDisable() => all.Remove(this);

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.useGravity = false; // Moon gravity is applied by hand
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.angularDamping = 1.5f;
        ownColliders = GetComponentsInChildren<Collider>();

        mounts = new Vector3[4];
        wheelOffset = new float[4];
        spin = new float[4];
        for (int i = 0; i < 4; i++)
        {
            // The mount is half the suspension travel above the modeled wheel center, so at rest (springs half
            // pressed in) the wheels sit exactly where the model has them
            mounts[i] = transform.InverseTransformPoint(wheelPivots[i].position) + Vector3.up * suspensionTravel * 0.5f;
            wheelOffset[i] = suspensionTravel;
        }
        // A low center of mass makes it hard to roll over
        Vector3 com = rb.centerOfMass;
        rb.centerOfMass = new Vector3(com.x, transform.InverseTransformPoint(wheelPivots[0].position).y, com.z);

        BuildDust();
    }

    // --------------------------------------------------------------- Getting in and out

    // The closest free rover within reach of a position (used by PlayerMovement when F is pressed)
    public static MoonRover FindEnterable(Vector3 position)
    {
        MoonRover best = null;
        float bestDistance = float.MaxValue;
        foreach (MoonRover rover in all)
        {
            if (rover.Driver != null) continue;
            float d = rover.DistanceTo(position);
            if (d < rover.enterDistance && d < bestDistance) { best = rover; bestDistance = d; }
        }
        return best;
    }

    float DistanceTo(Vector3 position)
    {
        float best = float.MaxValue;
        foreach (Collider c in ownColliders)
            if (c.enabled && !c.isTrigger) best = Mathf.Min(best, Vector3.Distance(c.ClosestPoint(position), position));
        return best == float.MaxValue ? Vector3.Distance(transform.position, position) : best;
    }

    public void Enter(PlayerMovement astronaut)
    {
        Driver = astronaut;
        enteredFrame = Time.frameCount;
        astronaut.EnterVehicle(this, driverSeat);
        CameraOrbit orbit = Camera.main != null ? Camera.main.GetComponent<CameraOrbit>() : null;
        if (orbit != null) orbit.SetTarget(transform, hipsAnchor.localPosition.y + 3f, 1.5f);
    }

    public void Exit()
    {
        if (Driver == null) return;
        PlayerMovement astronaut = Driver;
        Driver = null;
        throttle = steer = 0f;
        Vector3 spot = FindExitSpot(astronaut);
        astronaut.ExitVehicle(spot, Quaternion.Euler(0f, transform.eulerAngles.y, 0f));
        CameraOrbit orbit = Camera.main != null ? Camera.main.GetComponent<CameraOrbit>() : null;
        if (orbit != null) orbit.SetTarget(astronaut.transform, 0f, 1f);
    }

    // Looks for flat, free ground next to the rover: driver side first, then the other side, back and front
    Vector3 FindExitSpot(PlayerMovement astronaut)
    {
        CharacterController cc = astronaut.controller;
        float radius = cc.radius * astronaut.transform.lossyScale.x;
        float height = cc.height * astronaut.transform.lossyScale.y;
        Bounds b = new Bounds(transform.position, Vector3.zero);
        foreach (Collider c in ownColliders) b.Encapsulate(c.bounds);
        float sideGap = b.extents.magnitude * 0.55f + radius;

        Vector3 flatRight = Vector3.ProjectOnPlane(transform.right, Vector3.up).normalized;
        Vector3 flatForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        Vector3[] directions = { -flatRight, flatRight, -flatForward, flatForward };
        foreach (Vector3 dir in directions)
        {
            Vector3 probe = b.center + dir * sideGap + Vector3.up * height;
            if (!Physics.Raycast(probe, Vector3.down, out RaycastHit hit, height * 4f, ~0, QueryTriggerInteraction.Ignore)) continue;
            if (IsOwn(hit.collider)) continue;
            Vector3 feet = hit.point;
            Vector3 bottom = feet + Vector3.up * (radius + 0.2f);
            Vector3 top = feet + Vector3.up * (height - radius);
            bool blocked = false;
            foreach (Collider c in Physics.OverlapCapsule(bottom, top, radius, ~0, QueryTriggerInteraction.Ignore))
                if (!c.transform.IsChildOf(astronaut.transform)) { blocked = true; break; }
            if (!blocked) return feet;
        }
        return b.center + Vector3.up * (b.extents.y + height); // Nowhere free: on top, gravity does the rest
    }

    bool IsOwn(Collider c)
    {
        foreach (Collider own in ownColliders) if (own == c) return true;
        return false;
    }

    // --------------------------------------------------------------- Input

    void Update()
    {
        if (player == null) player = FindFirstObjectByType<PlayerMovement>();

        if (Driver != null)
        {
            throttle = (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f);
            float steerInput = (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f);
            steer = Mathf.MoveTowards(steer, steerInput, Time.deltaTime * 3f); // Steering wheel turns smoothly
            handbrake = Input.GetKey(KeyCode.Space);
            if (Input.GetKeyDown(interactKey) && Time.frameCount != enteredFrame) Exit();

            // While driving, the camera slowly swings behind the rover
            CameraOrbit orbit = Camera.main != null ? Camera.main.GetComponent<CameraOrbit>() : null;
            if (orbit != null) orbit.followBehind = Mathf.Abs(ForwardSpeed) > 4f;
        }
        else
        {
            throttle = 0f;
            steer = Mathf.MoveTowards(steer, 0f, Time.deltaTime * 2f);
            handbrake = false;
        }
    }

    // --------------------------------------------------------------- Physics

    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        rb.AddForce(Vector3.down * gravity, ForceMode.Acceleration);

        Vector3 up = transform.up;
        ForwardSpeed = Vector3.Dot(rb.linearVelocity, transform.forward);
        float speedFactor = Mathf.Clamp01(Mathf.Abs(ForwardSpeed) / maxSpeed);
        float steerAngle = steer * maxSteerAngle * Mathf.Lerp(1f, 0.4f, speedFactor); // Less steering at high speed

        // Spring strength so that the rover rests with the springs about half compressed
        float wheelMass = rb.mass / 4f;
        float spring = wheelMass * gravity / (suspensionTravel * 0.5f);
        float damper = 2f * dampingRatio * Mathf.Sqrt(spring * wheelMass);

        int groundedWheels = 0;
        for (int i = 0; i < 4; i++)
        {
            Vector3 mount = transform.TransformPoint(mounts[i]);
            float rayLength = suspensionTravel + wheelRadius;
            if (!CastWheel(mount, -up, rayLength, out RaycastHit hit))
            {
                wheelOffset[i] = Mathf.MoveTowards(wheelOffset[i], suspensionTravel, dt * 10f);
                continue;
            }
            groundedWheels++;

            // Suspension: a spring pushing up, a damper stopping it from bouncing forever
            float length = hit.distance - wheelRadius;
            float compression = suspensionTravel - length;
            float springVelocity = Vector3.Dot(rb.GetPointVelocity(mount), up);
            float load = Mathf.Max(0f, spring * compression - damper * springVelocity);
            rb.AddForceAtPosition(up * load, mount);
            wheelOffset[i] = Mathf.Clamp(length, 0f, suspensionTravel);

            // Tire: which way this wheel points (front wheels steer, rear wheels steer the other way)
            bool front = i < 2;
            float angle = front ? steerAngle : (rearSteering ? -steerAngle * 0.6f : 0f);
            Vector3 wheelForward = Vector3.ProjectOnPlane(Quaternion.AngleAxis(angle, up) * transform.forward, hit.normal).normalized;
            Vector3 wheelRight = Vector3.Cross(hit.normal, wheelForward);
            Vector3 velocity = rb.GetPointVelocity(hit.point);
            float sideSpeed = Vector3.Dot(velocity, wheelRight);
            float rollSpeed = Vector3.Dot(velocity, wheelForward);

            // Sideways: the tire resists sliding
            Vector3 force = -wheelRight * sideSpeed * grip * wheelMass;

            // Forward: motor, brakes and rolling resistance (every wheel has a motor, like the real LRV)
            float accel = 0f;
            if (throttle > 0f)
                accel = rollSpeed < maxSpeed ? acceleration * throttle : 0f;
            else if (throttle < 0f)
                accel = rollSpeed > 0.5f ? -brakeDeceleration : (rollSpeed > -reverseSpeed ? acceleration * 0.6f * throttle : 0f);
            float resistance = (Driver == null || handbrake) ? 4f : (throttle == 0f ? 0.5f : 0.05f);
            accel -= rollSpeed * resistance;
            force += wheelForward * accel * wheelMass;

            // A tire can only push as hard as it is pressed onto the ground
            float maxForce = tireFriction * load;
            if (force.magnitude > maxForce) force = force.normalized * maxForce;

            // Pushing at the height of the center of mass instead of the ground keeps the rover from tipping over
            Vector3 point = hit.point + up * Vector3.Dot(rb.worldCenterOfMass - hit.point, up) * 0.8f;
            rb.AddForceAtPosition(force, point);

            if (!front && Mathf.Abs(rollSpeed) > 3f) EmitDust(hit.point, velocity, dt);
        }

        // In the air: gently turn level again (like the astronauts leaning to balance)
        if (groundedWheels == 0)
        {
            Vector3 torque = Vector3.Cross(up, Vector3.up) * 2f;
            rb.AddTorque(torque, ForceMode.Acceleration);
        }

        // Upside down and stuck for a moment: flip back onto the wheels
        if (Vector3.Dot(up, Vector3.up) < 0.3f && rb.linearVelocity.magnitude < 3f) upsideDownTime += dt;
        else upsideDownTime = 0f;
        if (upsideDownTime > 1.5f)
        {
            upsideDownTime = 0f;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.MovePosition(rb.position + Vector3.up * wheelRadius * 2f);
            rb.MoveRotation(Quaternion.Euler(0f, transform.eulerAngles.y, 0f));
        }
    }

    bool CastWheel(Vector3 origin, Vector3 direction, float length, out RaycastHit best)
    {
        best = default;
        int count = Physics.RaycastNonAlloc(origin, direction, hits, length, ~0, QueryTriggerInteraction.Ignore);
        float bestDistance = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            if (IsOwn(hits[i].collider) || (Driver != null && hits[i].collider.transform.IsChildOf(Driver.transform))) continue;
            if (hits[i].distance < bestDistance) { bestDistance = hits[i].distance; best = hits[i]; }
        }
        return bestDistance < float.MaxValue;
    }

    // --------------------------------------------------------------- Visuals

    void LateUpdate()
    {
        float steerAngle = steer * maxSteerAngle * Mathf.Lerp(1f, 0.4f, Mathf.Clamp01(Mathf.Abs(ForwardSpeed) / maxSpeed));
        for (int i = 0; i < 4; i++)
        {
            bool front = i < 2;
            float angle = front ? steerAngle : (rearSteering ? -steerAngle * 0.6f : 0f);
            wheelPivots[i].localRotation = Quaternion.Euler(0f, angle, 0f);

            // Roll: distance traveled / radius = turned angle
            spin[i] += ForwardSpeed / wheelRadius * Mathf.Rad2Deg * Time.deltaTime;
            Vector3 local = wheelSpinners[i].localPosition;
            float target = suspensionTravel * 0.5f - wheelOffset[i]; // Up when pressed in, down when hanging
            local.y = Mathf.Lerp(local.y, target, 1f - Mathf.Exp(-20f * Time.deltaTime));
            wheelSpinners[i].localPosition = local;
            wheelSpinners[i].localRotation = Quaternion.Euler(spin[i], 0f, 0f);
        }
    }

    // --------------------------------------------------------------- Moon dust behind the wheels

    void BuildDust()
    {
        var go = new GameObject("RoverDust");
        dust = go.AddComponent<ParticleSystem>();
        dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = dust.main;
        main.playOnAwake = false;
        main.maxParticles = 800;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = gravity / Mathf.Max(0.01f, -Physics.gravity.y);
        var emission = dust.emission;
        emission.rateOverTime = 0f;
        var colorOverLifetime = dust.colorOverLifetime;
        colorOverLifetime.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.7f, 0.08f), new GradientAlphaKey(0f, 1f) });
        colorOverLifetime.color = gradient;
        var collision = dust.collision;
        collision.enabled = true;
        collision.type = ParticleSystemCollisionType.World;
        collision.dampen = 0.85f;
        collision.bounce = 0.05f;
        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.material = MoonDust.CreateParticleMaterial(SoftDot());
        dust.Play();
    }

    void EmitDust(Vector3 point, Vector3 velocity, float dt)
    {
        dustCounter += dt * Mathf.Abs(ForwardSpeed) * 2.5f;
        while (dustCounter >= 1f)
        {
            dustCounter -= 1f;
            var ep = new ParticleSystem.EmitParams
            {
                position = point + Random.insideUnitSphere * wheelRadius * 0.3f,
                velocity = -velocity * 0.15f + Vector3.up * Random.Range(1f, 4f) + Random.insideUnitSphere * 2f,
                startSize = Random.Range(1.5f, 3.5f),
                startLifetime = Random.Range(0.8f, 1.6f),
                startColor = new Color(0.62f, 0.6f, 0.57f, 1f) * Random.Range(0.85f, 1.05f)
            };
            dust.Emit(ep, 1);
        }
    }

    static Texture2D SoftDot()
    {
        const int n = 32;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f)) / (n / 2f);
                float a = Mathf.SmoothStep(1f, 0f, d);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
            }
        tex.Apply();
        return tex;
    }

    void OnDestroy()
    {
        if (dust != null) Destroy(dust.gameObject);
    }

    // --------------------------------------------------------------- On-screen prompt

    void OnGUI()
    {
        if (keyStyle == null) MakeStyles();
        Camera cam = Camera.main;
        if (cam == null) return;

        if (Driver != null)
        {
            // Driving controls at the bottom of the screen
            const float w = 520f, h = 34f;
            GUI.Label(new Rect((Screen.width - w) / 2f, Screen.height - h - 20f, w, h),
                $"W/S: drive    A/D: steer    Space: brake    {interactKey}: get out", hintStyle);
            return;
        }

        // "F" key prompt floating above the rover when the player is close enough
        if (player == null || player.InVehicle || FindEnterable(player.transform.position) != this) return;
        Vector3 screen = cam.WorldToScreenPoint(promptPoint.position);
        if (screen.z <= 0f) return;
        float bob = Mathf.Sin(Time.time * 4f) * 3f;
        float x = screen.x, y = Screen.height - screen.y + bob;
        GUI.Box(new Rect(x - 58f, y - 18f, 36f, 36f), interactKey.ToString(), keyStyle);
        GUI.Label(new Rect(x - 16f, y - 16f, 90f, 32f), "Drive", labelStyle);
    }

    void MakeStyles()
    {
        keyStyle = new GUIStyle(GUI.skin.box)
        {
            fontSize = 20, font = UITheme.GuiFont, alignment = TextAnchor.MiddleCenter,
            normal = { background = RoundedTexture(new Color(1f, 1f, 1f, 0.95f)), textColor = new Color(0.12f, 0.12f, 0.15f) },
            border = new RectOffset(9, 9, 9, 9)
        };
        labelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 18, font = UITheme.GuiFont, alignment = TextAnchor.MiddleLeft,
            padding = new RectOffset(10, 10, 0, 0),
            normal = { background = RoundedTexture(UITheme.Strip), textColor = UITheme.Text },
            border = new RectOffset(9, 9, 9, 9)
        };
        hintStyle = new GUIStyle(labelStyle) { fontSize = 15, alignment = TextAnchor.MiddleCenter };
    }

    // A small rounded-corner square used as a background (stretched by the GUI with fixed corners)
    static Texture2D RoundedTexture(Color color)
    {
        const int n = 32, r = 8;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = Mathf.Max(0, Mathf.Max(r - x, x - (n - 1 - r)));
                float dy = Mathf.Max(0, Mathf.Max(r - y, y - (n - 1 - r)));
                float a = Mathf.Clamp01(r + 0.5f - Mathf.Sqrt(dx * dx + dy * dy));
                tex.SetPixel(x, y, new Color(color.r, color.g, color.b, color.a * a));
            }
        tex.Apply();
        return tex;
    }
}
