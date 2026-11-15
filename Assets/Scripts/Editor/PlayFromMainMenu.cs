using UnityEditor;
using UnityEditor.SceneManagement;

// Pressing Play in the editor always starts at the main menu (like the built game), whatever scene is open.
// Turn it off to test the open scene directly: Survive In Moon > Play From Main Menu
[InitializeOnLoad]
public static class PlayFromMainMenu
{
    const string MenuPath = "Survive In Moon/Play From Main Menu";
    const string ScenePath = "Assets/Scenes/MainMenu.unity";
    const string PrefKey = "SurviveInMoon.PlayFromMainMenu";

    static bool Enabled
    {
        get => EditorPrefs.GetBool(PrefKey, true);
        set => EditorPrefs.SetBool(PrefKey, value);
    }

    static PlayFromMainMenu() => EditorApplication.delayCall += Apply;

    static void Apply()
    {
        EditorSceneManager.playModeStartScene = Enabled ? AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) : null;
        Menu.SetChecked(MenuPath, Enabled);
    }

    [MenuItem(MenuPath)]
    static void Toggle()
    {
        Enabled = !Enabled;
        Apply();
    }
}
