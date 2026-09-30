using UnityEngine;

// Adds procedural poses on top of the animations played by the Animator:
// leaning forward in the air (moon walk), crouching on landing, crafting and picking things up (F).
// Also keeps the soles on the ground (foot planting), so the legs never sink into or float above it.
// Works without PlayerMovement too (e.g. showcase astronauts): then it only does the foot planting.
// Bone names come from the (German) armature in Astronaut.fbx, so those strings can't be renamed.
public class AstronautAnimation : MonoBehaviour
{
    public PlayerMovement movement; // Found on the same object if left empty
    public Animator animator; // Found in children if left empty

    [Tooltip("Without PlayerMovement (NPCs): tick to crouch and work with the hands (e.g. while building)")]
    public bool working = false;

    [Header("NPC Walk (without PlayerMovement)")]
    [Tooltip("0 = standing, 1 = walking. Set by CrewMember. The legs swing with the distance moved, so feet don't slide.")]
    [Range(0f, 1f)] public float walking = 0f;
    public float stepLength = 4.5f;   // Scene units per step
    public float thighSwing = 22f;    // Degrees forward/back
    public float kneeBend = 34f;      // Extra bend while the leg swings forward
    public float armSwing = 14f;

    [Tooltip("Tick if the leaning happens in the wrong direction (the model faces -Z)")]
    public bool modelFacesBackward = false;

    [Header("Moon Feel")]
    public float groundAnimationSpeed = 0.8f; // Animations are a bit slow-motion on the ground
    public float airAnimationSpeed = 0.45f;

    [Header("In The Air")]
    public float springStiffness = 2.5f; // Spring frequency for pose transitions (higher = faster)
    [Range(0.2f, 1f)] public float springDamping = 0.55f; // Below 1 it wobbles back and forth a little
    public float forwardLean = 16f; // Forward lean angle of the body in the air
    public float legSpread = 0.5f; // How much the legs open in the air (0-1)

    [Header("Foot Planting")]
    public bool plantFeet = true; // Keep the lowest sole exactly on the ground
    public LayerMask groundLayers = ~0;
    [Range(0f, 45f)] public float maxFootTilt = 35f; // How much the boots may tilt to lie flat on a slope

    [Header("Crafting")]
    public float craftBlendSpeed = 3f;
    public float handMoveSpeed = 9f;

    [Header("Picking Up")]
    public float pickUpDuration = 1.2f;
    [Tooltip("Part of the animation where the hand reaches the ground (the item is taken here)")]
    [Range(0.1f, 0.8f)] public float pickUpContact = 0.45f;

    // Work like Animation Events of a clip: the InteractionSystem listens to them
    public event System.Action InteractionContact; // Hands touch the object (crafting pose reached, item grabbed)
    public event System.Action InteractionFinished; // Pick up is over

    private Transform hips, chest, head;
    private Transform shoulderL, shoulderR, forearmL, forearmR, handL, handR;
    private Transform thighL, thighR, shinL, shinR, footL, footR;

    private float airWeight, craftWeight, landWeight;
    private float airSpring, airSpringVelocity; // Springy air weight (overshoots slightly)
    private float fallSpring, fallSpringVelocity; // Rising/falling transition
    private float leanSpring, leanSpringVelocity; // Forward lean based on speed
    private float pickUpTime = -1f; // Seconds into the pick up, -1 = not picking up
    private float walkWeight, walkPhase; // NPC walk: how much, and where in the step cycle
    private Vector3 lastPosition;

    // So added rotations don't pile up each frame: the clean pose written by the Animator and the pose we wrote
    private Transform[] bones;
    private Quaternion[] cleanRotations, writtenRotations;
    private Vector3 hipsCleanPosition, hipsWrittenPosition;
    private bool poseSaved;

    private CharacterController controller;
    // Points on the surface of the legs and boots. Each one follows the (up to) two bones that bend it,
    // with the same skin weights Unity uses to bend the mesh, so we know exactly where knees, shins and soles are.
    private Transform[] legBoneA, legBoneB;
    private Vector3[] legLocalA, legLocalB;
    private float[] legWeightA;
    private float bodyHeight; // Height of the whole mesh, used to limit the correction
    private readonly RaycastHit[] groundHits = new RaycastHit[3]; // Ground under the left foot, right foot and hips
    private int groundHitCount;

    void Start()
    {
        if (movement == null) movement = GetComponent<PlayerMovement>();
        if (movement != null)
            movement.Landed += landingSpeed => landWeight = Mathf.Max(landWeight, Mathf.Clamp01(landingSpeed / 6f));
        controller = GetComponent<CharacterController>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        FindBones();
    }

    // An NPC can be hidden and shown again somewhere else (e.g. coming out of the hub): don't count that jump
    // as walking
    void OnEnable() => lastPosition = transform.position;

    // Called by PlayerCharacter after the model was swapped for another character: the old bones are gone
    public void RefreshModel()
    {
        animator = GetComponentInChildren<Animator>();
        poseSaved = false;
        FindBones();
    }

    void FindBones()
    {
        hips = Find("hüfte");
        chest = Find("brust");
        head = Find("kopf");
        shoulderL = Find("schulter.L"); shoulderR = Find("schulter.R");
        forearmL = Find("unterarm.L"); forearmR = Find("unterarm.R");
        handL = Find("handfläche.L"); handR = Find("handfläche.R");
        thighL = Find("oberschenkel.L"); thighR = Find("oberschenkel.R");
        shinL = Find("unterschenkel.L"); shinR = Find("unterschenkel.R");
        footL = Find("fuss.L"); footR = Find("fuss.R");

        // Doesn't run on objects without the astronaut skeleton (e.g. a test capsule)
        if (hips == null || chest == null)
        {
            enabled = false;
            return;
        }
        enabled = true;

        MeasureLegPoints();

        var list = new System.Collections.Generic.List<Transform>();
        foreach (Transform t in new[] { hips, chest, head, shoulderL, shoulderR, forearmL, forearmR, handL, handR,
                                        thighL, thighR, shinL, shinR, footL, footR })
            if (t != null) list.Add(t);
        bones = list.ToArray();
        cleanRotations = new Quaternion[bones.Length];
        writtenRotations = new Quaternion[bones.Length];
    }

    // If the Animator didn't write a bone this frame (not in the clip, stopped, etc.) the bone is still in the
    // pose we wrote last frame. Put it back to the clean pose; otherwise rotations stack up and the
    // character starts spinning.
    void CleanPose()
    {
        for (int i = 0; i < bones.Length; i++)
        {
            if (poseSaved && bones[i].localRotation == writtenRotations[i])
                bones[i].localRotation = cleanRotations[i];
            cleanRotations[i] = bones[i].localRotation;
        }
        if (poseSaved && hips.localPosition == hipsWrittenPosition) hips.localPosition = hipsCleanPosition;
        hipsCleanPosition = hips.localPosition;
    }

    void SavePose()
    {
        for (int i = 0; i < bones.Length; i++) writtenRotations[i] = bones[i].localRotation;
        hipsWrittenPosition = hips.localPosition;
        poseSaved = true;
    }

    // A calm walk: the legs swing forward and back under the body (never out to the side), the knee bends while
    // a leg comes forward, the arms swing opposite to the legs. w = how much (0-1).
    void ApplyWalkPose(float w, Vector3 right)
    {
        float s = Mathf.Sin(walkPhase), c = Mathf.Cos(walkPhase);
        // Positive angle around the right axis = the leg goes back
        Rotate(thighL, right, thighSwing * s * w);
        Rotate(thighR, right, -thighSwing * s * w);
        Rotate(shinL, right, kneeBend * Mathf.Max(0f, -c) * w);
        Rotate(shinR, right, kneeBend * Mathf.Max(0f, c) * w);
        Rotate(shoulderL, right, -armSwing * s * w);
        Rotate(shoulderR, right, armSwing * s * w);
        Rotate(forearmL, right, -12f * w);
        Rotate(forearmR, right, -12f * w);
        Rotate(chest, Vector3.up, 4f * s * w); // The upper body turns a little with the steps
        Rotate(chest, right, 4f * w);          // and leans slightly forward
    }

    // Crouch and move the hands as if working on something in front (c = how much, 0-1)
    void ApplyCraftPose(float c, Vector3 right, Vector3 forward, Vector3 up)
    {
        float time = Time.time * handMoveSpeed;

        // Crouch: hips forward, compensate back so legs stay upright + bend knees
        Rotate(hips, right, 20f * c);
        Rotate(chest, right, 25f * c);
        Rotate(head, right, 15f * c);
        Rotate(thighL, right, -65f * c); Rotate(thighR, right, -65f * c);
        Rotate(shinL, right, 80f * c); Rotate(shinR, right, 80f * c);
        Rotate(footL, right, -35f * c); Rotate(footR, right, -35f * c);

        // Arms reach forward and come together
        Rotate(shoulderL, right, -55f * c); Rotate(shoulderR, right, -55f * c);
        Rotate(shoulderL, forward, 12f * c); Rotate(shoulderR, forward, -12f * c);

        // Hands take turns moving up-down and left-right
        Rotate(forearmL, right, (-35f + Mathf.Sin(time) * 18f) * c);
        Rotate(forearmR, right, (-35f + Mathf.Sin(time + Mathf.PI) * 18f) * c);
        Rotate(forearmL, up, Mathf.Sin(time * 0.5f) * 15f * c);
        Rotate(forearmR, up, Mathf.Sin(time * 0.5f + 1.3f) * 15f * c);
        Rotate(handL, forward, Mathf.Sin(time * 1.3f) * 25f * c);
        Rotate(handR, forward, Mathf.Sin(time * 1.3f + 2f) * 25f * c);
    }

    Transform Find(string boneName)
    {
        foreach (Transform t in GetComponentsInChildren<Transform>(true))
            if (t.name == boneName) return t;
        return null;
    }

    // Rotates the bone around one of the character's world axes at its own pivot.
    // Independent of the bones' local axes, so the Blender/Unity axis difference doesn't matter.
    void Rotate(Transform bone, Vector3 axis, float angle)
    {
        if (bone == null || Mathf.Abs(angle) < 0.01f) return;
        bone.rotation = Quaternion.AngleAxis(angle, axis) * bone.rotation;
    }

    // Runs after the Animator has written this frame's pose
    void LateUpdate()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        CleanPose();

        if (movement == null)
        {
            // NPC astronaut: the Animator plays Idle; walking and working are added here on top of it,
            // then the feet are kept on the ground
            Vector3 npcRight = modelFacesBackward ? -transform.right : transform.right;
            Vector3 npcForward = modelFacesBackward ? -transform.forward : transform.forward;

            float moved = Vector3.ProjectOnPlane(transform.position - lastPosition, Vector3.up).magnitude;
            lastPosition = transform.position;
            walkPhase += moved / Mathf.Max(0.1f, stepLength) * Mathf.PI; // One step = half a cycle
            walkWeight = Mathf.MoveTowards(walkWeight, walking, dt * 4f);
            if (walkWeight > 0f) ApplyWalkPose(Mathf.SmoothStep(0f, 1f, walkWeight), npcRight);

            craftWeight = Mathf.MoveTowards(craftWeight, working ? 1f : 0f, dt * craftBlendSpeed);
            float npcCraft = Mathf.SmoothStep(0f, 1f, craftWeight);
            if (npcCraft > 0f) ApplyCraftPose(npcCraft, npcRight, npcForward, Vector3.up);
            PlantFeet(true, 1f);
            SavePose();
            return;
        }

        if (movement.InVehicle)
        {
            pickUpTime = -1f;
            SitInVehicle();
            SavePose();
            return;
        }

        airWeight = Spring(ref airSpring, ref airSpringVelocity, movement.IsInAir ? 1f : 0f, springStiffness, springDamping, dt);
        float previousCraftWeight = craftWeight;
        craftWeight = Mathf.MoveTowards(craftWeight, movement.IsCrafting ? 1f : 0f, dt * craftBlendSpeed);
        bool contact = craftWeight >= 1f && previousCraftWeight < 1f; // Crouched down, hands on the work
        float p = PickUpWeight(dt, ref contact, out bool pickUpFinished);
        landWeight = Mathf.MoveTowards(landWeight, 0f, dt * 3f);

        float h = Mathf.Clamp(airWeight, 0f, 1.15f); // The spring overshoot exaggerates the pose for a moment, then settles
        float c = Mathf.SmoothStep(0f, 1f, craftWeight);
        float l = Mathf.SmoothStep(0f, 1f, landWeight);

        if (animator != null)
            animator.speed = Mathf.Lerp(groundAnimationSpeed, airAnimationSpeed, h);

        // Positive angle around the right axis: body leans forward, arm/leg goes back
        Vector3 right = modelFacesBackward ? -transform.right : transform.right;
        Vector3 forward = modelFacesBackward ? -transform.forward : transform.forward;
        Vector3 up = Vector3.up;

        // Speed-based forward lean exists slightly on the ground too (body leans forward when running)
        Spring(ref leanSpring, ref leanSpringVelocity, movement.SpeedRatio, springStiffness * 0.6f, springDamping, dt);
        Rotate(chest, right, leanSpring * 6f);

        // --- AIR: leaning forward, arms floating for balance, legs pedaling slowly (moon hop) ---
        if (h > 0.001f)
        {
            float time = Time.time;
            float strength = movement.JumpStrength; // Bigger jump = more visible pose
            float t = h * Mathf.Lerp(0.6f, 1f, strength);
            float falling = Spring(ref fallSpring, ref fallSpringVelocity,
                Mathf.InverseLerp(0.5f, -2f, movement.VerticalSpeed), springStiffness * 0.7f, 0.7f, dt); // 0 = rising, 1 = falling
            float lean = forwardLean * Mathf.Lerp(0.5f, 1f, leanSpring);

            Rotate(hips, right, lean * 0.5f * t);
            Rotate(chest, right, lean * 0.5f * t);
            Rotate(chest, up, Mathf.Sin(time * 1.7f) * 5f * t); // Slight body twist
            Rotate(head, right, -lean * 0.6f * t); // Keep looking ahead

            // Arms: rise with inertia when falling starts, keep floating gently
            float armOpen = Mathf.Lerp(18f, 32f, falling);
            float floatWave = Mathf.Sin(time * 2.3f) * 7f;
            Rotate(shoulderL, forward, (-armOpen - floatWave) * t);
            Rotate(shoulderR, forward, (armOpen - floatWave) * t);
            Rotate(shoulderL, right, (-25f + Mathf.Sin(time * 1.9f) * 10f) * t);
            Rotate(shoulderR, right, (8f + Mathf.Sin(time * 1.9f + 2f) * 10f) * t);
            Rotate(forearmL, right, (-25f + floatWave) * t);
            Rotate(forearmR, right, (-18f - floatWave) * t);

            // Legs: open slightly, a bit back when rising, forward for landing when falling; pedal slowly
            float legStrength = t * strength * legSpread;
            float thighAngle = Mathf.Lerp(8f, -18f, falling);
            float pedal = Mathf.Sin(time * 3f) * 8f;
            Rotate(thighL, right, (thighAngle - 10f + pedal) * legStrength);
            Rotate(thighR, right, (thighAngle + 6f - pedal) * legStrength);
            Rotate(shinL, right, (30f + pedal) * legStrength);
            Rotate(shinR, right, (20f - pedal) * legStrength);
            Rotate(footL, right, -10f * legStrength);
            Rotate(footR, right, -8f * legStrength);
        }
        else
        {
            fallSpring = 0f; fallSpringVelocity = 0f; // Next jump starts from the rising pose
        }

        // --- LANDING: knees bend briefly ---
        if (l > 0f)
        {
            Rotate(chest, right, 10f * l);
            Rotate(thighL, right, -30f * l); Rotate(thighR, right, -30f * l);
            Rotate(shinL, right, 55f * l); Rotate(shinR, right, 55f * l);
            Rotate(footL, right, -25f * l); Rotate(footR, right, -25f * l);
        }

        // --- CRAFTING: crouches and moves the hands as if working on something in front ---
        if (c > 0f) ApplyCraftPose(c, right, forward, up);

        // --- PICKING UP: bend down, the right hand reaches the ground in front, the left rests toward the knee ---
        if (p > 0f)
        {
            Rotate(hips, right, 30f * p);
            Rotate(chest, right, 35f * p);
            Rotate(head, right, -15f * p); // Keeps the visor on the item instead of the feet
            Rotate(thighL, right, -55f * p); Rotate(thighR, right, -45f * p);
            Rotate(shinL, right, 55f * p); Rotate(shinR, right, 45f * p);
            Rotate(footL, right, -30f * p); Rotate(footR, right, -30f * p);
            Rotate(shoulderR, right, -25f * p);
            Rotate(forearmR, right, -15f * p);
            Rotate(shoulderL, right, -10f * p);
            Rotate(forearmL, right, -30f * p);
        }

        // Keep the soles on the ground: never below it, and while standing not above it either
        PlantFeet(!movement.IsInAir, Mathf.Clamp01(1f - h));

        SavePose();

        // After the pose is done, so listeners can safely start a new animation
        if (contact) InteractionContact?.Invoke();
        if (pickUpFinished) InteractionFinished?.Invoke();
    }

    public void PlayPickUp() => pickUpTime = 0f;
    public void StopPickUp() => pickUpTime = -1f;

    // Down until the contact moment, a short hold with the hand on the ground, then back up
    float PickUpWeight(float dt, ref bool contact, out bool finished)
    {
        finished = false;
        if (pickUpTime < 0f) return 0f;
        float before = pickUpTime / pickUpDuration;
        pickUpTime += dt;
        float t = pickUpTime / pickUpDuration;
        if (before < pickUpContact && t >= pickUpContact) contact = true;
        if (t >= 1f) { pickUpTime = -1f; finished = true; return 0f; }

        const float hold = 0.12f;
        if (t < pickUpContact) return Mathf.SmoothStep(0f, 1f, t / pickUpContact);
        if (t < pickUpContact + hold) return 1f;
        return Mathf.SmoothStep(1f, 0f, (t - pickUpContact - hold) / (1f - pickUpContact - hold));
    }

    // Sitting in the rover: knees up, lower legs down, hands forward on the controller, hips on the seat
    void SitInVehicle()
    {
        Vector3 right = modelFacesBackward ? -transform.right : transform.right;
        Rotate(thighL, right, -80f); Rotate(thighR, right, -80f);
        Rotate(shinL, right, 85f); Rotate(shinR, right, 85f);
        Rotate(footL, right, -5f); Rotate(footR, right, -5f);
        Rotate(chest, right, 6f);
        Rotate(shoulderL, right, -45f); Rotate(shoulderR, right, -45f);
        Rotate(forearmL, right, -30f); Rotate(forearmR, right, -30f);
        if (movement.Vehicle != null && movement.Vehicle.hipsAnchor != null)
            hips.position = movement.Vehicle.hipsAnchor.position;
    }

    // Damped spring: moves toward the target; with damping < 1 it overshoots a little and comes back (lively instead of stiff)
    static float Spring(ref float value, ref float velocity, float target, float frequency, float damping, float dt)
    {
        float w = frequency * Mathf.PI * 2f;
        // Compute in small steps so it stays stable even when the frame rate drops
        while (dt > 0f)
        {
            float step = Mathf.Min(dt, 1f / 120f);
            velocity += (-2f * damping * w * velocity - w * w * (value - target)) * step;
            value += velocity * step;
            dt -= step;
        }
        return value;
    }

    // Keeps the legs out of the ground:
    //  - no point of the legs/boots may go below the ground (always, in every state: crouching, landing, slopes)
    //  - while standing, the body is lowered until the lowest point touches the ground (no floating)
    // The ground is measured separately under each foot and the hips, so slopes and steps work too.
    void PlantFeet(bool grounded, float weight)
    {
        if (!plantFeet || legBoneA == null) return;
        if (!MeasureGround()) return;

        if (grounded && weight > 0f) TiltFeetToGround(weight);

        // Deepest point below the ground (positive) or smallest gap above it (negative)
        float deepest = float.MinValue;
        for (int i = 0; i < legBoneA.Length; i++)
        {
            Vector3 p = Vector3.LerpUnclamped(legBoneB[i].TransformPoint(legLocalB[i]), legBoneA[i].TransformPoint(legLocalA[i]), legWeightA[i]);
            deepest = Mathf.Max(deepest, GroundHeightAt(p) - p.y);
        }

        if (deepest > 0f)
            hips.position += Vector3.up * Mathf.Min(deepest, bodyHeight * 0.5f); // Something went into the ground: lift
        else if (grounded && weight > 0f && -deepest < bodyHeight * 0.15f)
            hips.position += Vector3.up * deepest * weight; // Standing but hovering: lower (big gaps mean a ledge)
    }

    // Ray down under both feet and the hips; skips our own colliders
    bool MeasureGround()
    {
        groundHitCount = 0;
        foreach (Transform t in new[] { footL, footR, hips })
        {
            Vector3 start = new Vector3(t.position.x, hips.position.y + bodyHeight * 0.5f, t.position.z);
            float best = float.MaxValue;
            foreach (RaycastHit hit in Physics.RaycastAll(start, Vector3.down, bodyHeight * 3f, groundLayers, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.transform.IsChildOf(transform) || hit.distance >= best) continue;
                best = hit.distance;
                groundHits[groundHitCount] = hit;
            }
            if (best < float.MaxValue) groundHitCount++;
        }
        return groundHitCount > 0;
    }

    // Ground height at any point, using the closest measured spot and its slope (the surface normal)
    float GroundHeightAt(Vector3 p)
    {
        RaycastHit nearest = groundHits[0];
        float bestDistance = float.MaxValue;
        for (int i = 0; i < groundHitCount; i++)
        {
            Vector3 d = groundHits[i].point - p;
            float horizontal = d.x * d.x + d.z * d.z;
            if (horizontal < bestDistance) { bestDistance = horizontal; nearest = groundHits[i]; }
        }
        Vector3 n = nearest.normal;
        if (n.y < 0.2f) return nearest.point.y; // Almost a wall: treat as flat
        // Plane through the hit point: follow the slope to p's horizontal position
        return nearest.point.y - (n.x * (p.x - nearest.point.x) + n.z * (p.z - nearest.point.z)) / n.y;
    }

    // On a slope, turn each boot so its sole lies along the ground instead of poking into it
    void TiltFeetToGround(float weight)
    {
        for (int f = 0; f < 2; f++)
        {
            Transform foot = f == 0 ? footL : footR;
            Vector3 normal = Vector3.up;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < groundHitCount; i++)
            {
                Vector3 d = groundHits[i].point - foot.position;
                float horizontal = d.x * d.x + d.z * d.z;
                if (horizontal < bestDistance) { bestDistance = horizontal; normal = groundHits[i].normal; }
            }
            Quaternion tilt = Quaternion.FromToRotation(Vector3.up, normal);
            tilt.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle < 0.5f || angle > 90f) continue;
            foot.rotation = Quaternion.AngleAxis(Mathf.Min(angle, maxFootTilt) * weight, axis) * foot.rotation;
        }
    }

    // Picks points on the legs and boots once and ties each one to the bones that bend it (its skin weights).
    // This is the same math Unity uses to bend the mesh ("linear blend skinning"), limited to the two strongest bones.
    void MeasureLegPoints()
    {
        legBoneA = null; // No stale points from a previous model if this one has no readable mesh
        var bones = new System.Collections.Generic.List<Transform>();
        var boneB = new System.Collections.Generic.List<Transform>();
        var localA = new System.Collections.Generic.List<Vector3>();
        var localB = new System.Collections.Generic.List<Vector3>();
        var weightA = new System.Collections.Generic.List<float>();
        var world = new System.Collections.Generic.List<Vector3>();
        var candidates = new System.Collections.Generic.List<(Transform, Vector3, Transform, Vector3, float)>();
        float lowest = float.MaxValue, highest = float.MinValue;

        foreach (SkinnedMeshRenderer smr in GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            Mesh mesh = smr.sharedMesh;
            if (smr.name.StartsWith(AstronautSkin.AccessoryPrefix) || mesh == null || !mesh.isReadable) continue;
            Vector3[] vertices = mesh.vertices;
            BoneWeight[] weights = mesh.boneWeights;
            Matrix4x4[] bindposes = mesh.bindposes;
            Transform[] smrBones = smr.bones;
            if (weights.Length != vertices.Length) continue;

            for (int v = 0; v < vertices.Length; v++)
            {
                BoneWeight bw = weights[v];
                // Local position of the vertex in the space of its two strongest bones
                Transform a = smrBones[bw.boneIndex0], b = smrBones[bw.boneIndex1];
                if (a == null) continue;
                if (b == null || bw.weight1 <= 0f) b = a;
                Vector3 la = bindposes[bw.boneIndex0].MultiplyPoint3x4(vertices[v]);
                Vector3 lb = b == a ? la : bindposes[bw.boneIndex1].MultiplyPoint3x4(vertices[v]);
                float total = bw.weight0 + (b == a ? 0f : bw.weight1);
                float wa = total > 0f ? bw.weight0 / total : 1f;
                Vector3 w = Vector3.LerpUnclamped(b.TransformPoint(lb), a.TransformPoint(la), wa);
                lowest = Mathf.Min(lowest, w.y);
                highest = Mathf.Max(highest, w.y);
                world.Add(w);
                candidates.Add((a, la, b, lb, wa));
            }
        }
        if (candidates.Count == 0) return;
        bodyHeight = highest - lowest;

        // The lower 40% of the body = legs and boots. Keep about 800 points so it stays cheap every frame;
        // the very bottom of the boots (the soles) is always kept.
        float limit = lowest + bodyHeight * 0.4f;
        int below = 0;
        foreach (Vector3 w in world) if (w.y <= limit) below++;
        int step = Mathf.Max(1, below / 800);
        int counter = 0;
        for (int i = 0; i < candidates.Count; i++)
        {
            if (world[i].y > limit) continue;
            bool isSole = world[i].y <= lowest + bodyHeight * 0.01f;
            if (!isSole && counter++ % step != 0) continue;
            var c = candidates[i];
            bones.Add(c.Item1); localA.Add(c.Item2); boneB.Add(c.Item3); localB.Add(c.Item4); weightA.Add(c.Item5);
        }
        legBoneA = bones.ToArray();
        legLocalA = localA.ToArray();
        legBoneB = boneB.ToArray();
        legLocalB = localB.ToArray();
        legWeightA = weightA.ToArray();
    }
}
