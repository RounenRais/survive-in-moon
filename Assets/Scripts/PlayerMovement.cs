using UnityEngine;

public class PlayerMovement : MonoBehaviour
{
    public CharacterController controller;
    public Animator animator; // Found automatically in children if left empty
    public Transform cameraTransform; // Uses the Camera in children if left empty
    public bool autoFitCollider = true; // Fits the CharacterController to the model's size at start

    [Header("Walking")]
    public float speed = 8f;        // Calm moon walk (the astronaut is ~16 units tall: about half a body per second)
    public float sprintSpeed = 40f; // Very fast on purpose (presentation build) // Left Shift: long bounding steps, uses stamina (SurvivalStats)
    public KeyCode sprintKey = KeyCode.LeftShift;
    public float acceleration = 12f; // Little grip on the moon: speeds up gently
    public float stopTime = 0.15f; // After releasing the keys, smoothly stops in about this time
    public float turnSpeed = 8f; // How fast the character turns toward the movement direction

    [Header("Moon Physics")]
    public float gravity = -4f; // Moon gravity (~1/6 of Earth's)
    public float jumpHeight = 1.6f;
    [Range(0f, 1f)] public float airControl = 0.2f; // How much you can steer while in the air
    public float walkHopHeight = 0.3f; // Automatic kangaroo hop while walking at full speed (0 = off)

    // Coyote time: lets you still jump for a short moment after leaving the ground
    private float groundedTimer;
    public float groundedGraceTime = 0.15f;

    // States read by the animation script
    public bool IsInAir { get; private set; }
    public bool IsCrafting { get; private set; }
    public bool IsSprinting { get; private set; }
    // How fast the legs should move compared to a normal walk (the animation follows the real speed)
    public float AnimationTempo => Mathf.Clamp(horizontalVelocity.magnitude / Mathf.Max(0.01f, speed), 0f, 1.8f);
    public SurvivalStats Stats { get; private set; }
    public bool InVehicle => Vehicle != null;
    public MoonRover Vehicle { get; private set; }
    public float VerticalSpeed => verticalVelocity.y;
    public float SpeedRatio => horizontalVelocity.magnitude / speed; // 0 = standing, 1 = full speed
    public Vector3 HorizontalVelocity => horizontalVelocity;
    public float JumpStrength { get; private set; } // 1 = full jump, small value = walking hop
    public event System.Action<float> Landed; // Parameter: landing speed

    private Vector3 verticalVelocity;
    private Vector3 horizontalVelocity;
    private bool wasGroundedLastFrame = true;
    private Vector3 stopVelocity; // Internal velocity used by SmoothDamp
    private float airTime;        // Seconds since we last touched the ground

    void Start()
    {
        controller = GetComponent<CharacterController>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (GetComponent<AstronautAnimation>() == null) gameObject.AddComponent<AstronautAnimation>();
        if (GetComponent<MoonDust>() == null) gameObject.AddComponent<MoonDust>();
        if (GetComponent<InteractionSystem>() == null) gameObject.AddComponent<InteractionSystem>();
        Stats = GetComponent<SurvivalStats>();
        if (Stats == null) Stats = gameObject.AddComponent<SurvivalStats>();
        GameSystems.Ensure(); // Storms, HUD, map, base supplies (one each per scene)

        // The CharacterController does the movement. A physics body and extra colliders on the same object
        // would fight with it and push/shake the character, so they are turned off.
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.useGravity = false;
            rb.isKinematic = true;
        }
        foreach (Collider c in GetComponents<Collider>())
            if (c != controller) c.enabled = false;

        if (autoFitCollider) FitControllerToModel();

        if (cameraTransform == null)
        {
            Camera cam = GetComponentInChildren<Camera>();
            if (cam != null) cameraTransform = cam.transform;
        }

        // Detach the camera so it doesn't rotate with the character; CameraOrbit follows and orbits around us
        if (cameraTransform != null)
        {
            if (cameraTransform.IsChildOf(transform)) cameraTransform.SetParent(null, true);
            CameraOrbit orbit = cameraTransform.GetComponent<CameraOrbit>();
            if (orbit == null) orbit = cameraTransform.gameObject.AddComponent<CameraOrbit>();
            orbit.Init(transform);
        }
    }

    // Called by PlayerCharacter after the model (the look) was swapped for another character
    public void RefreshModel()
    {
        animator = GetComponentInChildren<Animator>();
        if (autoFitCollider && controller != null) FitControllerToModel();
    }

    // Fits the CharacterController capsule to the model's visible bounds. If the center stays at (0,0,0),
    // half of the capsule sticks out below a model whose pivot is at its feet, and the character floats.
    void FitControllerToModel()
    {
        // Measure the real posed mesh: Renderer.bounds of an animated model is padded and made the character float
        bool found = false;
        Bounds bounds = new Bounds();
        foreach (SkinnedMeshRenderer smr in GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            if (smr.name.StartsWith(AstronautSkin.AccessoryPrefix) || smr.sharedMesh == null) continue;
            var mesh = new Mesh();
            smr.BakeMesh(mesh, false); // World-sized vertices; only position and rotation are needed
            Matrix4x4 toWorld = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
            foreach (Vector3 v in mesh.vertices)
            {
                Vector3 w = toWorld.MultiplyPoint3x4(v);
                if (!found) { bounds = new Bounds(w, Vector3.zero); found = true; }
                else bounds.Encapsulate(w);
            }
            Destroy(mesh);
        }
        // Models without bones (simple meshes) fall back to the renderer bounds
        if (!found)
        {
            foreach (MeshRenderer r in GetComponentsInChildren<MeshRenderer>())
            {
                MeshFilter filter = r.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null || r.name.StartsWith(AstronautSkin.AccessoryPrefix)) continue;
                if (!found) { bounds = r.bounds; found = true; }
                else bounds.Encapsulate(r.bounds);
            }
        }
        if (!found) return;

        Vector3 scale = transform.lossyScale;
        float scaleY = Mathf.Abs(scale.y);
        float scaleXZ = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
        if (scaleY < 0.0001f || scaleXZ < 0.0001f) return;

        // Sizes in local scale (CharacterController values are multiplied by the transform's scale)
        float height = bounds.size.y / scaleY;
        float radius = Mathf.Min(bounds.size.x, bounds.size.z) * 0.5f / scaleXZ * 0.8f;
        radius = Mathf.Min(radius, height * 0.5f);

        Vector3 localCenter = transform.InverseTransformPoint(bounds.center);
        float localBottom = localCenter.y - height * 0.5f;

        // The controller hovers skinWidth above the ground; shift the capsule down by that much so the feet touch
        controller.height = height;
        controller.radius = radius;
        controller.center = new Vector3(localCenter.x, localBottom + height * 0.5f - controller.skinWidth / scaleY, localCenter.z);
        controller.stepOffset = height * 0.3f; // Climbs small rocks and lips instead of stopping dead at speed
    }

    // Called by MoonRover: sit in the seat. Our own movement and collider switch off; the rover carries us.
    public void EnterVehicle(MoonRover rover, Transform seat)
    {
        Vehicle = rover;
        IsCrafting = false;
        horizontalVelocity = Vector3.zero;
        verticalVelocity = Vector3.zero;
        controller.enabled = false;
        transform.SetParent(seat, false);
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;
        if (animator != null) animator.SetFloat("Speed", 0f);
    }

    // Called by InteractionSystem: start/stop the crafting pose (input is ignored while crafting)
    public void SetCrafting(bool crafting) => IsCrafting = crafting && !InVehicle;

    // Called by MoonRover: stand next to the rover again
    public void ExitVehicle(Vector3 feetPosition, Quaternion rotation)
    {
        Vehicle = null;
        transform.SetParent(null, true);
        transform.SetPositionAndRotation(feetPosition, rotation);
        controller.enabled = true;
        wasGroundedLastFrame = true;
    }

    void Update()
    {
        if (InVehicle) return; // The rover handles input while we drive

        // STEP 1: Ground check and timer
        bool grounded = controller.isGrounded;
        if (grounded)
        {
            groundedTimer = groundedGraceTime; // Keeps refilling the timer while on the ground

            if (!wasGroundedLastFrame) Landed?.Invoke(-verticalVelocity.y);

            if (verticalVelocity.y < 0)
            {
                // Pressed onto the ground harder the faster we go, so running down a slope doesn't launch us
                verticalVelocity.y = -Mathf.Max(2f, horizontalVelocity.magnitude * 1.5f);
            }
        }
        else
        {
            // In the air the timer counts down
            groundedTimer -= Time.deltaTime;
        }
        wasGroundedLastFrame = grounded;
        airTime = grounded ? 0f : airTime + Time.deltaTime;
        IsInAir = airTime > 0.15f || verticalVelocity.y > 1f; // A real jump/hop counts at once, a bump doesn't

        // STEP 2: Input (WASD only; the arrow keys turn the camera). A full-screen menu (inventory, crew, build)
        // freezes all input, so WASD/Space can't move or jump the character behind the open panel.
        float x = 0f, z = 0f;
        bool jumpPressed = false;
        if (!InteractionSystem.MenuOpen)
        {
            x = (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f);
            z = (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f);
            jumpPressed = Input.GetButtonDown("Jump");
        }
        bool hasMoveInput = new Vector2(x, z).sqrMagnitude > 0.01f;

        // F (rover, crafting, picking up) is read by InteractionSystem. Moving or jumping ends crafting
        if (hasMoveInput || jumpPressed) IsCrafting = false;
        if (IsCrafting) { x = 0f; z = 0f; }


        // STEP 3: Horizontal movement (relative to the camera, with momentum)
        Vector3 forward = Vector3.forward;
        Vector3 right = Vector3.right;
        if (cameraTransform != null)
        {
            forward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized;
            right = Vector3.ProjectOnPlane(cameraTransform.right, Vector3.up).normalized;
        }

        Vector3 moveDirection = Vector3.ClampMagnitude(right * x + forward * z, 1f);
        bool wantsSprint = hasMoveInput && !IsCrafting && !InteractionSystem.MenuOpen && Input.GetKey(sprintKey);
        IsSprinting = wantsSprint && (Stats == null || Stats.CanSprint || (IsSprinting && Stats.Stamina > 0f));
        float bonus = Stats != null ? Stats.SpeedMultiplier : 1f;
        float targetSpeed = (IsSprinting ? sprintSpeed : speed) * bonus;
        if (hasMoveInput)
        {
            // Sprinting gets to speed right away, so it never feels like a slow ramp
            float currentAcceleration = IsSprinting ? acceleration * 8f : grounded ? acceleration : acceleration * airControl;
            if (IsSprinting) horizontalVelocity = moveDirection * targetSpeed; // Presentation sprint: turns on a dime, no sliding
            else horizontalVelocity = Vector3.MoveTowards(horizontalVelocity, moveDirection * targetSpeed, currentAcceleration * Time.deltaTime);
            stopVelocity = Vector3.zero;
        }
        else
        {
            // Keys released: a quick brake that starts and ends smoothly (a bit longer in the air)
            float time = grounded ? stopTime : stopTime * 1.5f;
            horizontalVelocity = Vector3.SmoothDamp(horizontalVelocity, Vector3.zero, ref stopVelocity, time);
            if (horizontalVelocity.sqrMagnitude < 0.0001f) horizontalVelocity = Vector3.zero;

            // Released during a walking hop: shorten it so the character doesn't float
            if (!grounded && JumpStrength < 1f && verticalVelocity.y > 0f)
                verticalVelocity.y = Mathf.MoveTowards(verticalVelocity.y, 0f, -gravity * 2f * Time.deltaTime);
        }

        // Turn the character toward the movement direction (slower in the air)
        if (moveDirection.sqrMagnitude > 0.001f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(moveDirection, Vector3.up);
            float turn = IsSprinting ? turnSpeed * 3f : grounded ? turnSpeed : turnSpeed * 0.4f;
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, turn * Time.deltaTime);
        }

        // Animation: Speed blends between Idle and Run; as speed drops the run smoothly turns into idle
        if (animator != null)
        {
            animator.SetFloat("Speed", Mathf.Clamp01(SpeedRatio), 0.08f, Time.deltaTime); // Idle -> Walk blend
        }

        // STEP 4: Jump (the grace timer is checked too)
        if (jumpPressed && groundedTimer > 0)
        {
            verticalVelocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
            JumpStrength = 1f;
            groundedTimer = 0; // Reset right away so you can't jump again in the air
        }
        // Walk on the moon with small hops like real astronauts.
        // Hop height grows with speed: low steps when slow, long glides when running
        else if (grounded && hasMoveInput && !IsSprinting && walkHopHeight > 0f && SpeedRatio > 0.5f) // No hops while sprinting: smooth and fast
        {
            float hop = walkHopHeight * Mathf.InverseLerp(0.5f, 1f, SpeedRatio);
            verticalVelocity.y = Mathf.Sqrt(Mathf.Max(hop, walkHopHeight * 0.3f) * -2f * gravity);
            JumpStrength = Mathf.Clamp01(hop / jumpHeight);
        }

        // STEP 5: Apply gravity and move
        verticalVelocity.y += gravity * Time.deltaTime;
        CollisionFlags collision = controller.Move((horizontalVelocity + verticalVelocity) * Time.deltaTime);

        // Hitting the ceiling stops the upward motion
        if ((collision & CollisionFlags.Above) != 0 && verticalVelocity.y > 0) verticalVelocity.y = 0;
    }
}
