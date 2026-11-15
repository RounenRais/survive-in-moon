// What the player has done so far in this run (reset when the game scene loads)
public static class Progress
{
    public static bool CrateOpened;
    public static bool RoverUnlocked;  // The rover blueprint from the cargo pod wreck: the garage appears on the terminal
    public static int WrecksSalvaged;

    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void Reset()
    {
        CrateOpened = RoverUnlocked = false;
        WrecksSalvaged = 0;
    }
}
