using UnityEngine;

// Sits on the same object as the Animator (added by InteractionSystem), because Animation Events can only call
// methods there. In a clip, add Animation Events with these function names:
//   InteractionContact - the frame where the hands touch the object (the effect happens here)
//   InteractionEnd     - the end of a one-shot clip (pick up)
// Give the Animator a Trigger parameter named like the animation type ("PickUp", "Craft") to play the clip;
// without one, the procedural poses in AstronautAnimation are used instead.
public class InteractionAnimationRelay : MonoBehaviour
{
    public InteractionSystem system;

    public void InteractionContact()
    {
        if (system != null) system.ResolveActive();
    }

    public void InteractionEnd()
    {
        if (system != null) system.FinishActive();
    }
}
