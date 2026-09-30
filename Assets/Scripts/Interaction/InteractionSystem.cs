using System;
using UnityEngine;

// The interact key (F) for the player:
//  - Every scanInterval it looks for IInteractables around the player (OverlapSphereNonAlloc) and picks the one
//    closest to where the player is looking as the target. TargetChanged fires only when the target changes.
//  - F on a target plays its animation; the animation's "contact" moment (hands touch the object) calls
//    OnInteractionResolved. The contact comes from the procedural poses in AstronautAnimation, or from an
//    Animation Event in a real clip through InteractionAnimationRelay.
//  - F near a free rover gets in (the rover itself handles getting out), and F with nothing else in reach
//    crafts by hand, like before. F again (or moving/jumping) stops crafting.
// Added automatically by PlayerMovement.
[RequireComponent(typeof(Inventory))]
public class InteractionSystem : MonoBehaviour
{
    public KeyCode interactKey = KeyCode.F;

    [Header("Detection")]
    [Tooltip("0 = automatic: half of the character's height")]
    public float radius = 0f;
    public float scanInterval = 0.1f;
    public LayerMask layers = ~0;
    [Tooltip("Things further to the side of the view direction than this angle are ignored")]
    [Range(0f, 180f)] public float maxViewAngle = 110f;
    [Tooltip("How much distance counts next to the view angle when choosing the target (0 = only the angle)")]
    [Range(0f, 1f)] public float distanceWeight = 0.35f;

    [Header("Animation")]
    [Tooltip("Resolves anyway if the animation never sends its contact event")]
    public float contactTimeout = 3f;

    [Header("UI")]
    public bool createPlaceholderUI = true; // Adds the simple on-screen prompt

    public Inventory Inventory { get; private set; }
    public IInteractable CurrentTarget { get; private set; }
    public bool IsBusy => active != null;

    // Top of the target, for a floating prompt
    public Vector3 TargetPoint => targetCollider != null
        ? targetCollider.bounds.center + Vector3.up * targetCollider.bounds.extents.y
        : transform.position;

    public event Action<IInteractable> TargetChanged; // null = nothing to interact with
    public event Action<string> MessageShown;         // Short notices like "Inventory Full"

    private PlayerMovement movement;
    private AstronautAnimation bodyAnimation;
    private Animator animator;
    private CraftInteractable handCrafting; // Fallback when nothing is targeted
    private readonly Collider[] overlaps = new Collider[32];
    private Collider targetCollider;
    private float nextScanTime;
    private int lastVehicleFrame = -10;

    // The interaction that is playing right now
    private IInteractable active;
    private bool activeResolved;
    private float activeStartTime;

    void Awake()
    {
        Inventory = GetComponent<Inventory>();
        movement = GetComponent<PlayerMovement>();
        handCrafting = GetComponent<CraftInteractable>();
        if (handCrafting == null) handCrafting = gameObject.AddComponent<CraftInteractable>();

        if (createPlaceholderUI && GetComponent<InteractionPromptUI>() == null) gameObject.AddComponent<InteractionPromptUI>();
    }

    void Start()
    {
        if (FindFirstObjectByType<InventoryUI>(FindObjectsInactive.Include) == null)
            Debug.LogWarning("No Inventory UI in the scene, so Tab does nothing. Create it with: Survive In Moon > Create Inventory UI");
    }

    // Called by PlayerCharacter after the model was swapped: the old Animator (and its relay) are gone
    public void RefreshAnimator()
    {
        animator = null;
        HookAnimation();
    }

    // AstronautAnimation and the Animator may be added after us (PlayerMovement.Start), so look them up lazily
    void HookAnimation()
    {
        if (bodyAnimation == null)
        {
            bodyAnimation = GetComponent<AstronautAnimation>();
            if (bodyAnimation != null)
            {
                bodyAnimation.InteractionContact += ResolveActive;
                bodyAnimation.InteractionFinished += FinishActive;
            }
        }
        if (animator == null)
        {
            animator = movement != null && movement.animator != null ? movement.animator : GetComponentInChildren<Animator>();
            if (animator != null)
            {
                var relay = animator.GetComponent<InteractionAnimationRelay>();
                if (relay == null) relay = animator.gameObject.AddComponent<InteractionAnimationRelay>();
                relay.system = this;
            }
        }
    }

    void OnDestroy()
    {
        if (bodyAnimation != null)
        {
            bodyAnimation.InteractionContact -= ResolveActive;
            bodyAnimation.InteractionFinished -= FinishActive;
        }
    }

    void Update()
    {
        HookAnimation();

        // The rover reads F while we drive; skip the frame we get out so the same press doesn't get us back in
        if (movement != null && movement.InVehicle)
        {
            lastVehicleFrame = Time.frameCount;
            CancelActive();
            SetTarget(null, null);
            return;
        }

        UpdateActive();

        if (active != null) SetTarget(null, null); // No prompt while busy
        else if (Time.time >= nextScanTime)
        {
            nextScanTime = Time.time + scanInterval;
            Scan();
        }
        else if (CurrentTarget != null && targetCollider == null) SetTarget(null, null); // Target was destroyed

        if (Input.GetKeyDown(interactKey) && Time.frameCount - lastVehicleFrame > 1) Interact();
    }

    void Interact()
    {
        if (movement != null && movement.IsInAir) return;

        // F again stops crafting (the old toggle); a pick up can't be interrupted
        if (active != null)
        {
            if (active.GetAnimationType() == InteractionAnimation.Craft) StopCrafting();
            return;
        }

        // What we look at comes first, then a rover in reach, then crafting by hand
        IInteractable target = CurrentTarget;
        if (target == null && movement != null)
        {
            MoonRover rover = MoonRover.FindEnterable(transform.position);
            if (rover != null) { rover.Enter(movement); return; }
        }
        if (target == null) target = handCrafting;
        if (target.CanInteract(this)) Begin(target);
    }

    void Begin(IInteractable target)
    {
        active = target;
        activeResolved = false;
        activeStartTime = Time.time;
        SetTarget(null, null);

        InteractionAnimation type = target.GetAnimationType();
        bool hasClip = SetAnimatorTrigger(type.ToString()); // A real clip (with Animation Events) if the Animator has one
        bool hasPose = bodyAnimation != null && bodyAnimation.isActiveAndEnabled;

        switch (type)
        {
            case InteractionAnimation.Craft:
                if (movement != null) movement.SetCrafting(true);
                break;
            case InteractionAnimation.PickUp:
                if (!hasClip && hasPose) bodyAnimation.PlayPickUp();
                break;
        }

        // Nothing can send the contact event: resolve right away
        if (type == InteractionAnimation.None || (!hasClip && !hasPose) || (type == InteractionAnimation.Craft && movement == null))
        {
            ResolveActive();
            if (type != InteractionAnimation.Craft || movement == null) active = null;
        }
    }

    void UpdateActive()
    {
        if (active == null) return;
        InteractionAnimation type = active.GetAnimationType();

        // PlayerMovement stops crafting when the player moves or jumps
        if (type == InteractionAnimation.Craft && movement != null && !movement.IsCrafting)
        {
            active = null;
            return;
        }
        if (!activeResolved && Time.time - activeStartTime > contactTimeout) ResolveActive();
        if (type == InteractionAnimation.PickUp && activeResolved && Time.time - activeStartTime > contactTimeout * 2f) FinishActive();
    }

    // Contact frame of the animation: the actual effect happens here (once per interaction)
    public void ResolveActive()
    {
        if (active == null || activeResolved) return;
        activeResolved = true;
        if (active is UnityEngine.Object obj && obj == null) return; // Destroyed meanwhile
        if (active.CanInteract(this)) active.OnInteractionResolved(this);
        nextScanTime = 0f; // Look again right away (the item may be gone or smaller now)
    }

    // End of a one-shot animation (pick up). Crafting ends when the player moves or presses F again.
    public void FinishActive()
    {
        if (active == null || active.GetAnimationType() == InteractionAnimation.Craft) return;
        ResolveActive();
        active = null;
    }

    void StopCrafting()
    {
        if (movement != null) movement.SetCrafting(false);
        active = null;
    }

    void CancelActive()
    {
        if (active == null) return;
        if (active.GetAnimationType() == InteractionAnimation.Craft && movement != null) movement.SetCrafting(false);
        if (bodyAnimation != null) bodyAnimation.StopPickUp();
        active = null;
    }

    public void ShowMessage(string message) => MessageShown?.Invoke(message);

    // --------------------------------------------------------------- Detection

    void Scan()
    {
        float reach = radius > 0f ? radius : AutomaticRadius();
        Vector3 origin = transform.position;
        Vector3 view = ViewDirection();

        IInteractable best = null;
        Collider bestCollider = null;
        float bestScore = float.MaxValue;

        int count = Physics.OverlapSphereNonAlloc(origin, reach, overlaps, layers, QueryTriggerInteraction.Collide);
        for (int i = 0; i < count; i++)
        {
            Collider c = overlaps[i];
            if (c.transform.IsChildOf(transform)) continue; // Our own colliders (and the hand crafting fallback)
            IInteractable interactable = c.GetComponentInParent<IInteractable>();
            if (interactable == null || !interactable.CanInteract(this)) continue;

            Vector3 toTarget = Vector3.ProjectOnPlane(c.bounds.center - origin, Vector3.up);
            float distance = toTarget.magnitude;
            float angle = distance > 0.001f ? Vector3.Angle(view, toTarget) : 0f;
            if (angle > maxViewAngle) continue;

            float score = angle / 180f * (1f - distanceWeight) + distance / reach * distanceWeight;
            if (score < bestScore) { bestScore = score; best = interactable; bestCollider = c; }
        }
        SetTarget(best, bestCollider);
    }

    void SetTarget(IInteractable target, Collider targetCol)
    {
        targetCollider = targetCol;
        if (ReferenceEquals(target, CurrentTarget)) return;
        CurrentTarget = target;
        TargetChanged?.Invoke(target);
    }

    // Third person: where the camera looks (flattened), otherwise where the character faces
    Vector3 ViewDirection()
    {
        Transform cam = movement != null && movement.cameraTransform != null ? movement.cameraTransform
                      : Camera.main != null ? Camera.main.transform : null;
        Vector3 forward = cam != null ? cam.forward : transform.forward;
        forward = Vector3.ProjectOnPlane(forward, Vector3.up);
        return forward.sqrMagnitude > 0.0001f ? forward.normalized : transform.forward;
    }

    float AutomaticRadius()
    {
        CharacterController cc = movement != null ? movement.controller : GetComponent<CharacterController>();
        if (cc == null) return 2f;
        return cc.height * Mathf.Abs(transform.lossyScale.y) * 0.5f;
    }

    bool SetAnimatorTrigger(string parameter)
    {
        if (animator == null) return false;
        foreach (AnimatorControllerParameter p in animator.parameters)
        {
            if (p.type != AnimatorControllerParameterType.Trigger || p.name != parameter) continue;
            animator.SetTrigger(parameter);
            return true;
        }
        return false;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, radius > 0f ? radius : AutomaticRadius());
    }
}
