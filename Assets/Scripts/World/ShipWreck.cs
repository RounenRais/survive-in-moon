using System.Collections.Generic;
using UnityEngine;

// The crashed ship - the goal of the game. Repairing it takes several stages of ship parts made at the workbench
// (hull plating, thrusters, a navigation computer, a fuel tank). F at the ship installs whatever parts of the
// current stage you carry. When every stage is done, F launches and the game is won.
public class ShipWreck : MonoBehaviour, IInteractable
{
    [System.Serializable]
    public class Stage
    {
        public string name;
        public List<ItemAmount> parts = new List<ItemAmount>();
        [HideInInspector] public List<int> installed = new List<int>();
    }

    public List<Stage> stages = new List<Stage>();
    public GameObject[] repairedVisuals; // Shown one by one as stages finish (optional)

    public static ShipWreck Instance { get; private set; }
    public int StageIndex { get; private set; }
    public bool Repaired => StageIndex >= stages.Count;
    public Stage Current => Repaired ? null : stages[StageIndex];

    void Awake()
    {
        Instance = this;
        foreach (Stage s in stages)
            while (s.installed.Count < s.parts.Count) s.installed.Add(0);
        UpdateVisuals();
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    public float Progress
    {
        get
        {
            int total = 0, done = 0;
            foreach (Stage s in stages)
                for (int i = 0; i < s.parts.Count; i++) { total += s.parts[i].quantity; done += s.installed[i]; }
            return total > 0 ? done / (float)total : 1f;
        }
    }

    // "Hull plating 2/4 · Thruster 0/2" for the current stage
    public string StageNeeds()
    {
        if (Repaired) return "Ready for launch";
        var bits = new List<string>();
        for (int i = 0; i < Current.parts.Count; i++)
            if (Current.parts[i].item != null)
                bits.Add($"{Current.parts[i].item.displayName} {Current.installed[i]}/{Current.parts[i].quantity}");
        return string.Join(" · ", bits);
    }

    public string GetPrompt() => Repaired ? "Launch the ship" : $"Repair ship: {Current.name}  ({StageNeeds()})";
    public InteractionAnimation GetAnimationType() => Repaired ? InteractionAnimation.None : InteractionAnimation.PickUp;
    public bool CanInteract(InteractionSystem actor) => actor.Inventory != null;

    public void OnInteractionResolved(InteractionSystem actor)
    {
        if (Repaired) { GameOverUI.Show(true); return; }

        int moved = 0;
        Stage stage = Current;
        for (int i = 0; i < stage.parts.Count; i++)
        {
            ItemAmount need = stage.parts[i];
            if (need.item == null) continue;
            int missing = need.quantity - stage.installed[i];
            int give = Mathf.Min(missing, actor.Inventory.CountOf(need.item));
            if (give <= 0) continue;
            actor.Inventory.RemoveItem(need.item, give);
            stage.installed[i] += give;
            moved += give;
        }

        if (moved == 0) { actor.ShowMessage($"Needed: {StageNeeds()}"); return; }

        bool stageDone = true;
        for (int i = 0; i < stage.parts.Count; i++)
            if (stage.installed[i] < stage.parts[i].quantity) stageDone = false;
        if (stageDone)
        {
            StageIndex++;
            UpdateVisuals();
            actor.ShowMessage(Repaired ? "The ship is repaired! Press F to launch" : $"{stage.name} done. Next: {Current.name}");
        }
        else actor.ShowMessage($"Installed {moved} parts · {StageNeeds()}");
    }

    void UpdateVisuals()
    {
        if (repairedVisuals == null) return;
        for (int i = 0; i < repairedVisuals.Length; i++)
            if (repairedVisuals[i] != null) repairedVisuals[i].SetActive(i < StageIndex);
    }
}
