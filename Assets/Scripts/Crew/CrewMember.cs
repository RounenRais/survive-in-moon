using UnityEngine;

// What a crew mate is doing right now
public enum CrewOrder
{
    InHub,       // Inside the base hub: not in the world, ready for a job
    Waiting,     // Standing where they are
    MoveTo,      // Walking to a point the player picked
    Follow,      // Walking behind the player
    Job,         // Building something (ConstructionSite)
    ReturnToHub  // Walking back to the hub door, then going inside
}

// One astronaut of the base crew (the player is one too, but takes no orders).
// Crew mates live in the base hub. A job (from the build terminal) or an order (from the crew window, C) brings
// them out of the hub door; when a job is done they walk back in by themselves.
// Walking: they turn toward the target first, then walk to it. On the way they steer around building
// footprints (PlacedBuilding) so they don't walk through bases, instead of a full path finder. The legs are
// animated by AstronautAnimation.walking, the Animator stays on Idle.
public class CrewMember : MonoBehaviour
{
    public string displayName = "Astronaut";
    public bool isPlayer;
    public float walkSpeed = 5f;
    public float turnSpeed = 240f;       // Degrees per second
    private Vector3 steer;               // Smoothed walking direction
    private float bestDistance = float.MaxValue, stuckTimer, escapeUntil;
    private Vector3 lastDestination;
    private int sideBias; // Which way around an obstacle (+1 / -1), kept for the whole trip
    public float followDistance = 26f;   // How far behind the player they keep

    [Header("Avoiding buildings")]
    public float avoidRadius = 6f;       // How wide a gap to keep around a building's footprint
    public float avoidLookAhead = 12f;   // How early to start steering around one

    public CharacterDefinition Character { get; set; }
    public CrewOrder Order { get; private set; } = CrewOrder.Waiting;
    public ConstructionSite Job { get; private set; }
    public bool IsBusy => Job != null;       // Busy = has a building job (can't take another one)
    public bool IsWorking { get; private set; } // At the site, hands on the work
    public bool InHub => Order == CrewOrder.InHub;

    public string Status
    {
        get
        {
            if (isPlayer) return Job == null ? "Free" : IsWorking ? $"Building {Job.Buildable.displayName}" : $"Go build {Job.Buildable.displayName}";
            switch (Order)
            {
                case CrewOrder.InHub: return "In the hub";
                case CrewOrder.MoveTo: return "Walking";
                case CrewOrder.Follow: return "Following you";
                case CrewOrder.ReturnToHub: return "Going back to the hub";
                case CrewOrder.Job: return Job == null ? "Free" : IsWorking ? $"Building {Job.Buildable.displayName}" : $"Going to {Job.Buildable.displayName}";
                default: return "Waiting";
            }
        }
    }

    private AstronautAnimation body;
    private Transform followTarget;
    private Vector3 target;
    private bool followMoving;

    void Awake() => body = GetComponent<AstronautAnimation>();

    // --------------------------------------------------------------- Orders

    public void AssignJob(ConstructionSite site)
    {
        Job = site;
        if (isPlayer) return;
        ComeOut();
        Order = CrewOrder.Job;
    }

    public void ReleaseJob()
    {
        Job = null;
        SetWorking(false);
        if (!isPlayer) ReturnToHub(); // Job done: back inside
    }

    public void MoveTo(Vector3 point)
    {
        bestDistance = float.MaxValue;
        if (isPlayer || IsBusy) return;
        ComeOut();
        target = point;
        Order = CrewOrder.MoveTo;
    }

    public void Follow(Transform leader)
    {
        if (isPlayer || IsBusy) return;
        ComeOut();
        followTarget = leader;
        followMoving = true;
        Order = CrewOrder.Follow;
    }

    public void Wait()
    {
        if (isPlayer || IsBusy) return;
        ComeOut();
        Order = CrewOrder.Waiting;
    }

    public void ReturnToHub()
    {
        if (isPlayer || IsBusy || InHub) return;
        Order = CrewOrder.ReturnToHub;
    }

    // Goes inside the hub: out of the world until the next order
    public void EnterHub()
    {
        if (isPlayer) return;
        Order = CrewOrder.InHub;
        SetWorking(false);
        gameObject.SetActive(false);
    }

    // Steps out of the hub door (does nothing if already outside)
    void ComeOut()
    {
        if (!InHub) return;
        Transform door = CrewManager.Instance != null ? CrewManager.Instance.HubDoor : null;
        if (door != null) transform.SetPositionAndRotation(BaseArea.GroundPoint(door.position), door.rotation);
        Order = CrewOrder.Waiting;
        gameObject.SetActive(true);
    }

    // --------------------------------------------------------------- Walking

    void Update()
    {
        if (isPlayer) return; // The player walks by themselves; ConstructionSite checks them

        bool walking = false;
        switch (Order)
        {
            case CrewOrder.Job:
                if (Job == null) { ReturnToHub(); break; }
                if (Step(Job.WorkPoint(transform.position), 1.5f, ref walking)) SetWorking(false);
                else
                {
                    Turn(Job.transform.position);
                    SetWorking(true);
                }
                break;

            case CrewOrder.MoveTo:
                if (!Step(target, 2f, ref walking)) Order = CrewOrder.Waiting;
                break;

            case CrewOrder.Follow:
                if (followTarget == null) { Order = CrewOrder.Waiting; break; }
                Vector3 slot = FollowSlot();
                float away = Vector3.ProjectOnPlane(slot - transform.position, Vector3.up).magnitude;
                if (away > followDistance * 0.5f) followMoving = true; // Start again only when clearly behind
                if (followMoving && !Step(slot, 3f, ref walking)) followMoving = false;
                if (!followMoving) Turn(followTarget.position);
                break;

            case CrewOrder.ReturnToHub:
                Transform door = CrewManager.Instance != null ? CrewManager.Instance.HubDoor : null;
                if (door == null || !Step(door.position, 3f, ref walking)) EnterHub();
                break;
        }
        if (body != null) body.walking = walking ? 1f : 0f;
    }

    // Turns toward the target, then walks. Returns false once there. walking = legs moving this frame.
    bool Step(Vector3 destination, float stopDistance, ref bool walking)
    {
        Vector3 flat = Vector3.ProjectOnPlane(destination - transform.position, Vector3.up);
        float distance = flat.magnitude;
        if (distance <= stopDistance) return false;

        // A new destination (a follow slot moves all the time) restarts the stuck check
        if ((destination - lastDestination).sqrMagnitude > 25f) { lastDestination = destination; bestDistance = float.MaxValue; stuckTimer = 0f; sideBias = 0; }
        // Stuck check: no real progress for a while -> walk straight for a moment (crew can pass through things)
        if (distance < bestDistance - 1f) { bestDistance = distance; stuckTimer = 0f; }
        else if ((stuckTimer += Time.deltaTime) > 1.5f) { escapeUntil = Time.time + 2f; stuckTimer = 0f; bestDistance = distance; }

        // Bend the straight line toward the target so we go around any building in the way
        Vector3 wish = Time.time < escapeUntil ? flat / distance : AvoidBuildings(flat / distance, destination);
        // Smooth it: the avoidance can flip sides from one frame to the next, which made them shake in place
        steer = steer.sqrMagnitude < 0.01f ? wish : Vector3.Slerp(steer, wish, 1f - Mathf.Exp(-6f * Time.deltaTime));
        Vector3 direction = Vector3.ProjectOnPlane(steer, Vector3.up).normalized;

        // Facing the opposite way: turn on the spot first, instead of walking backwards
        Turn(transform.position + direction);
        if (Vector3.Angle(transform.forward, direction) > 100f) return true;

        Vector3 move = direction * Mathf.Min(walkSpeed * Time.deltaTime, distance - stopDistance * 0.5f);
        transform.position = BaseArea.GroundPoint(transform.position + move);
        walking = true;
        return true;
    }

    // Takes the straight-line direction we want to walk and bends it around building footprints, so the crew
    // walks past bases instead of through them. Local steering: cancel the part of the motion that points into a
    // building and let the rest slide along its edge, which naturally carries us around the corner.
    Vector3 AvoidBuildings(Vector3 wishDirection, Vector3 destination)
    {
        Vector2 pos = new Vector2(transform.position.x, transform.position.z);
        Vector2 dir = new Vector2(wishDirection.x, wishDirection.z);
        Vector2 goal = new Vector2(destination.x, destination.z);
        if (dir.sqrMagnitude < 0.0001f) return wishDirection;

        foreach (PlacedBuilding building in PlacedBuilding.All)
        {
            if (building == null) continue;
            Footprint f = building.Footprint;
            float hx = f.half.x + avoidRadius;
            float hz = f.half.y + avoidRadius;

            // Skip the building that holds our destination (our own site, the hub door...): we must reach it
            Vector2 toGoal = goal - f.center;
            if (Mathf.Abs(Vector2.Dot(toGoal, f.axisX)) < hx && Mathf.Abs(Vector2.Dot(toGoal, f.axisZ)) < hz)
                continue;

            // Where we are in the building's own (x, z) axes
            Vector2 toCrew = pos - f.center;
            float lx = Vector2.Dot(toCrew, f.axisX);
            float lz = Vector2.Dot(toCrew, f.axisZ);

            Vector2 normal; // points from the building out toward us
            if (Mathf.Abs(lx) < hx && Mathf.Abs(lz) < hz)
            {
                // Already inside the padded box: push straight out of the nearest side
                if (hx - Mathf.Abs(lx) < hz - Mathf.Abs(lz)) normal = f.axisX * Mathf.Sign(lx == 0f ? 1f : lx);
                else normal = f.axisZ * Mathf.Sign(lz == 0f ? 1f : lz);
            }
            else
            {
                Vector2 closest = f.center + f.axisX * Mathf.Clamp(lx, -hx, hx) + f.axisZ * Mathf.Clamp(lz, -hz, hz);
                normal = pos - closest;
                float gap = normal.magnitude;
                if (gap > avoidLookAhead) continue; // too far away to matter yet
                normal /= gap;
            }

            float into = -Vector2.Dot(dir, normal); // > 0 means we are heading into the building
            if (into <= 0f) continue;                // walking away already: leave it alone

            dir += normal * into;                    // cancel the part going inward -> slide along the edge
            if (dir.sqrMagnitude < 0.12f)
            {
                // (Nearly) head-on: pick a side once and keep it until the destination changes, otherwise the
                // choice flips back and forth every frame and they shake on the spot
                Vector2 tangent = new Vector2(-normal.y, normal.x);
                if (sideBias == 0) sideBias = Vector2.Dot(tangent, goal - pos) >= 0f ? 1 : -1;
                dir = tangent * sideBias;
            }
            dir.Normalize();
        }
        return new Vector3(dir.x, 0f, dir.y);
    }

    void Turn(Vector3 point)
    {
        Vector3 direction = Vector3.ProjectOnPlane(point - transform.position, Vector3.up);
        if (direction.sqrMagnitude < 0.01f) return;
        transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(direction), turnSpeed * Time.deltaTime);
    }

    // Each follower gets its own place behind the player (left, right, further left...)
    Vector3 FollowSlot()
    {
        int index = CrewManager.Instance != null ? CrewManager.Instance.FollowerIndex(this) : 0;
        float side = (index % 2 == 0 ? -1f : 1f) * (12f + 10f * (index / 2));
        Vector3 back = -Vector3.ProjectOnPlane(followTarget.forward, Vector3.up).normalized;
        Vector3 right = Vector3.Cross(Vector3.up, -back);
        return followTarget.position + back * followDistance + right * side;
    }

    public void SetWorking(bool working)
    {
        IsWorking = working;
        if (body != null) body.working = working;
    }
}
