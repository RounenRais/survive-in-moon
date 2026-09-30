using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Applies the moon environment (space skybox + ground that reaches the horizon) to the game scene from inside Unity.
// Changing the .unity file from outside while Unity has the scene open doesn't stick: saving the scene writes
// Unity's in-memory copy over it. Doing it here works on that in-memory copy instead, so nothing is lost.
// Runs once automatically; can be repeated with the menu: Survive In Moon > Apply Moon Environment
[InitializeOnLoad]
public static class MoonEnvironmentSetup
{
    const string ScenePath = "Assets/Scenes/SampleScene.unity";
    const string SkyPath = "Assets/Materials/SpaceSky.mat";
    const float GroundSize = 3000f;
    static string DoneKey => "SurviveInMoon.MoonEnvironmentApplied." + Application.dataPath;

    static MoonEnvironmentSetup()
    {
        // Wait until the editor has finished loading, then apply once
        EditorApplication.delayCall += () =>
        {
            if (EditorPrefs.GetBool(DoneKey, false) || EditorApplication.isPlayingOrWillChangePlaymode) return;
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != ScenePath) return; // Try again next time the game scene is open
            if (Apply()) EditorPrefs.SetBool(DoneKey, true);
        };
    }

    [MenuItem("Survive In Moon/Apply Moon Environment")]
    static void ApplyFromMenu()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
        {
            EditorUtility.DisplayDialog("Moon Environment", "Open " + ScenePath + " first.", "OK");
            return;
        }
        Apply();
    }

    static bool Apply()
    {
        var sky = AssetDatabase.LoadAssetAtPath<Material>(SkyPath);
        if (sky == null)
        {
            Debug.LogWarning("Moon environment: " + SkyPath + " not found.");
            return false;
        }

        var scene = EditorSceneManager.GetActiveScene();
        RenderSettings.skybox = sky;

        // Make the ground big enough to reach the horizon (the camera draws up to 1000 units away)
        GameObject platform = GameObject.Find("Platform");
        Transform ground = platform != null ? platform.transform.Find("Cube") : null;
        if (ground != null)
        {
            Undo.RecordObject(ground, "Moon ground size");
            Vector3 s = ground.localScale;
            ground.localScale = new Vector3(Mathf.Max(s.x, GroundSize), s.y, Mathf.Max(s.z, GroundSize));
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("Moon environment applied: space skybox" + (ground != null ? " + ground reaches the horizon" : "") + ". Scene saved.");
        return true;
    }
}
