using UnityEngine;

// Third-person camera that orbits around the player.
//  - Hold the right mouse button and move the mouse to look around (the cursor is free otherwise, for UI/crafting)
//  - Arrow keys turn the camera, mouse wheel zooms, R smoothly puts the camera back behind the player
//  - If a wall or the ground is between the player and the camera, the camera moves closer so it never goes inside things
public class CameraOrbit : MonoBehaviour
{
    public Transform target; // The player; set automatically by PlayerMovement
    [Tooltip("Height above the player's feet that the camera looks at and orbits around (0 = automatic)")]
    public float pivotHeight = 0f;

    [Header("Mouse")]
    public bool holdRightMouseToLook = true;
    public float mouseSensitivity = 3f; // Degrees per mouse unit
    public bool invertY =    false;

    [Header("Keyboard")]
    public float arrowTurnSpeed = 100f; // Degrees per second
    public KeyCode resetKey = KeyCode.R;

    [Header("Angles")]
    public float minPitch = -5f; // Looking slightly up from below
    public float maxPitch = 75f; // Looking almost straight down

    [Header("Zoom")]
    public float zoomSpeed = 0.15f; // Fraction of the distance per wheel step
    public float minZoom = 0.4f; // Multipliers of the starting distance
    public float maxZoom = 1.8f;

    [Header("Feel")]
    public float smoothing = 12f; // Higher = snappier
    public float collisionRadius = 0.6f;
    public LayerMask collisionLayers = ~0;
    public float hintSeconds = 8f; // How long the controls hint stays on screen (0 = never)

    private float yaw, pitch, distance; // Wanted values (input changes these)
    private float currentYaw, currentPitch, currentDistance; // Smoothed values the camera actually uses
    private float defaultPitch, defaultDistance;
    private float playerPivotHeight, baseDistance; // baseDistance = starting distance for the current target
    private float lastLookInput = -999f;

    [HideInInspector] public bool followBehind; // Set by a vehicle while driving: the view swings behind it
    private bool resetting;
    private float hintTimer;

    public void Init(Transform player)
    {
        target = player;
        if (pivotHeight <= 0f) pivotHeight = AutoPivotHeight();

        // Start from where the camera was placed in the scene, so nothing jumps
        Vector3 offset = transform.position - Pivot();
        distance = defaultDistance = baseDistance = Mathf.Max(offset.magnitude, 1f);
        playerPivotHeight = pivotHeight;
        pitch = defaultPitch = Mathf.Clamp(Mathf.Asin(offset.y / distance) * Mathf.Rad2Deg, minPitch, maxPitch);
        yaw = Mathf.Atan2(-offset.x, -offset.z) * Mathf.Rad2Deg;
        currentYaw = yaw; currentPitch = pitch; currentDistance = distance;
        hintTimer = hintSeconds;
    }

    void Start()
    {
        if (target != null && defaultDistance <= 0f) Init(target);
    }

    // Switches what the camera orbits (e.g. the rover while driving). The view angle is kept, so nothing jumps.
    // newPivotHeight 0 = the player's own pivot height; distanceScale multiplies the starting distance.
    public void SetTarget(Transform newTarget, float newPivotHeight, float distanceScale)
    {
        target = newTarget;
        pivotHeight = newPivotHeight > 0f ? newPivotHeight : playerPivotHeight;
        baseDistance = defaultDistance * distanceScale;
        distance = baseDistance;
        followBehind = false;
    }

    // Turns the view by the given angles (degrees). Public so other scripts (e.g. a cutscene) can use it too.
    public void Rotate(float deltaYaw, float deltaPitch)
    {
        lastLookInput = Time.time;
        yaw += deltaYaw;
        pitch = Mathf.Clamp(pitch + deltaPitch, minPitch, maxPitch);
        resetting = false;
    }

    // Smoothly moves the camera back behind the player with the starting angle and distance
    public void ResetView()
    {
        yaw = target != null ? target.eulerAngles.y : yaw;
        // Take the short way around (e.g. from 350 to 10 degrees, not the long way back through 180)
        yaw = currentYaw + Mathf.DeltaAngle(currentYaw, yaw);
        pitch = defaultPitch;
        distance = baseDistance;
        resetting = true;
    }

    void Update()
    {
        if (target == null) return;
        ReadInput();
        if (hintTimer > 0f) hintTimer -= Time.unscaledDeltaTime;
    }

    void ReadInput()
    {
        // Mouse look
        bool looking = !holdRightMouseToLook || Input.GetMouseButton(1);
        if (looking)
        {
            float mx = Input.GetAxis("Mouse X") * mouseSensitivity;
            float my = Input.GetAxis("Mouse Y") * mouseSensitivity * (invertY ? 1f : -1f);
            if (Mathf.Abs(mx) > 0.001f || Mathf.Abs(my) > 0.001f) Rotate(mx, my);
        }
        // Hide and lock the cursor only while looking with the right mouse button
        if (holdRightMouseToLook)
        {
            if (Input.GetMouseButtonDown(1)) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
            if (Input.GetMouseButtonUp(1)) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
        }

        // Arrow keys
        float turn = 0f, tilt = 0f;
        if (Input.GetKey(KeyCode.LeftArrow)) turn -= 1f;
        if (Input.GetKey(KeyCode.RightArrow)) turn += 1f;
        if (Input.GetKey(KeyCode.UpArrow)) tilt -= 1f; // Up arrow raises the view (camera goes lower)
        if (Input.GetKey(KeyCode.DownArrow)) tilt += 1f;
        if (turn != 0f || tilt != 0f) Rotate(turn * arrowTurnSpeed * Time.deltaTime, tilt * arrowTurnSpeed * 0.6f * Time.deltaTime);

        // Zoom
        float scroll = Input.mouseScrollDelta.y;
        if (Mathf.Abs(scroll) > 0.01f)
            distance = Mathf.Clamp(distance * (1f - scroll * zoomSpeed), baseDistance * minZoom, baseDistance * maxZoom);

        if (Input.GetKeyDown(resetKey)) ResetView();
    }

    // LateUpdate runs after the player has moved this frame, so the camera never lags one frame behind
    void LateUpdate()
    {
        if (target == null) return;

        // Driving: if the player hasn't looked around for a moment, slowly swing behind the vehicle
        if (followBehind && Time.time - lastLookInput > 1.5f && !resetting)
            yaw += Mathf.DeltaAngle(yaw, target.eulerAngles.y) * (1f - Mathf.Exp(-1.2f * Time.deltaTime));

        float t = 1f - Mathf.Exp(-(resetting ? smoothing * 0.5f : smoothing) * Time.deltaTime); // Frame-rate independent smoothing
        currentYaw = Mathf.Lerp(currentYaw, yaw, t);
        currentPitch = Mathf.Lerp(currentPitch, pitch, t);
        currentDistance = Mathf.Lerp(currentDistance, distance, t);
        if (resetting && Mathf.Abs(currentYaw - yaw) < 0.1f && Mathf.Abs(currentPitch - pitch) < 0.1f) resetting = false;

        Quaternion rotation = Quaternion.Euler(currentPitch, currentYaw, 0f);
        Vector3 pivot = Pivot();
        Vector3 back = rotation * Vector3.back;
        float wanted = currentDistance;

        // Something in the way (wall, ladder, ground)? Stop the camera just in front of it
        float allowed = wanted;
        foreach (RaycastHit hit in Physics.SphereCastAll(pivot, collisionRadius, back, wanted, collisionLayers, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.transform.IsChildOf(target) || hit.distance <= 0f) continue;
            allowed = Mathf.Min(allowed, hit.distance);
        }

        transform.SetPositionAndRotation(pivot + back * allowed, rotation);
    }

    Vector3 Pivot() => target.position + Vector3.up * pivotHeight;

    // About 60% of the character's height (chest level) makes a comfortable orbit center
    float AutoPivotHeight()
    {
        var controller = target.GetComponent<CharacterController>();
        if (controller != null) return (controller.center.y + controller.height * 0.1f) * target.lossyScale.y;
        return 1.5f;
    }

    void OnGUI()
    {
        if (hintTimer <= 0f) return;
        var style = new GUIStyle(GUI.skin.label)
        {
            font = UITheme.GuiFont, fontSize = 15, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(16, 16, 10, 10),
            normal = { background = UITheme.Pixel(UITheme.Strip), textColor = UITheme.Text }
        };
        GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01(hintTimer));
        GUI.Label(new Rect(16, 16, 430, 100),
            "WASD  walk      Space  jump      F  use\n" +
            "Tab  inventory      C  crew\n" +
            "Right mouse  look around      Wheel  zoom\n" +
            $"Arrow keys  turn camera      {resetKey}  reset view", style);
    }
}
