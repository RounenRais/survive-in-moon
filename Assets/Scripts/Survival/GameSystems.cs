using UnityEngine;

// Starts the game-wide systems once per game scene (called by PlayerMovement.Start), so the scene file doesn't
// need to hold them: solar storms, base supplies, the status HUD (top right) and the map (M).
// Also forwards the survival/storm notices to the on-screen message line.
public static class GameSystems
{
    public static void Ensure()
    {
        if (Object.FindFirstObjectByType<SolarStorms>() == null) // First call in this game scene
        {
            MissionLog.Reset();
            ScienceCards.ResetForNewGame();
        }
        Make<SolarStorms>("Solar Storms");
        Make<BaseSupplies>("Base Supplies");
        Make<StatusHUD>("Status HUD");
        Make<MapUI>("Map");
        Make<RoverFuelHUD>("Rover HUD");
        Make<ObjectiveUI>("Objectives");
        Make<CompassUI>("Compass");
        Make<ObjectiveArrow>("Objective Arrow");
        Make<PauseUI>("Pause");
        Make<DemoKeys>("Demo Keys"); // F1 science card, F2 mission lost (presentation)
        IntroUI.ShowOnce(); // The story cards before play starts
        NasaData.UseEarthPhoto(); // The real Earth (NASA EPIC) in the sky
    }

    static void Make<T>(string name) where T : Component
    {
        if (Object.FindFirstObjectByType<T>() == null) new GameObject(name).AddComponent<T>();
    }

    // Shows a short notice through the player's interaction prompt (bottom of the screen)
    public static void Notify(string message)
    {
        var system = Object.FindFirstObjectByType<InteractionSystem>();
        if (system != null) system.ShowMessage(message);
    }
}
