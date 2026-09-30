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
// Walking: they turn toward the target first, then walk straight to it (no path finding yet). The legs are
// animated by AstronautAnimation.walking, the Animator stays on Idle.
public class CrewMember : MonoBehaviour
{
    public string displayName = "Astronaut";
    public bool isPlayer;
    public float walkSpeed = 5f;
    public float turnSpeed = 240f;       // Degrees per second
    public float followDistance = 26f;   // How far behind the player they keep

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
        if (flat.magnitude <= stopDistance) return false;

        // Facing far away from the target: turn on the spot first, instead of walking sideways
        if (Vector3.Angle(transform.forward, flat) > 45f)
        {
            Turn(destination);
            return true;
        }
        Turn(destination);
        Vector3 move = flat.normalized * Mathf.Min(walkSpeed * Time.deltaTime, flat.magnitude - stopDistance * 0.5f);
        transform.position = BaseArea.GroundPoint(transform.position + move);
        walking = true;
        return true;
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
