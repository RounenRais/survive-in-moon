using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Builds the title screen scene (Assets/Scenes/MainMenu.unity) from scratch and makes it the first scene of the
// build, followed by the game scene. The scene holds only the 3D backdrop; MainMenuUI makes the astronaut and the UI.
//   - the game's own lunar terrain (same heightmap and hills), the astronaut on a flat spot by the base area
//   - the starfield sky, the Earth hanging above the horizon
//   - a warm Sun from the front-left and a cool rim light from behind, so the astronaut stands out of the dark
// Menu: Survive In Moon > Build Main Menu
public static class MainMenuBuilder
{
    const string ScenePath = "Assets/Scenes/MainMenu.unity";
    const string GameScenePath = "Assets/Scenes/SampleScene.unity";

    // Where the terrain and the base are in the game scene (opened for a moment, additively)
    static void ReadGameScene(ref Vector3 terrainPos, ref Vector3 baseCenter)
    {
        var scene = EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Additive);
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            var t = root.GetComponentInChildren<Terrain>(true);
            if (t != null) terrainPos = t.transform.position;
            var area = root.GetComponentInChildren<BaseArea>(true);
            if (area != null) baseCenter = area.transform.position;
        }
        EditorSceneManager.CloseScene(scene, true);
    }

    [MenuItem("Survive In Moon/Build Main Menu")]
    public static void Build()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var earth = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Earth.mat");
        var sky = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/SpaceSky.mat");

        // Sky and ambient light: dark but not black, neutral grey (no colour cast on the suits)
        RenderSettings.skybox = sky;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.17f, 0.17f, 0.18f);
        RenderSettings.ambientEquatorColor = new Color(0.13f, 0.13f, 0.135f);
        RenderSettings.ambientGroundColor = new Color(0.11f, 0.105f, 0.1f);
        RenderSettings.reflectionIntensity = 0.3f;

        // Sun: warm key light from the front-left, high enough for long soft shadows
        var sun = new GameObject("Sun").AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.color = new Color(1f, 0.95f, 0.87f);
        sun.intensity = 1.7f;
        sun.shadows = LightShadows.Soft;
        sun.transform.rotation = Quaternion.Euler(32f, 35f, 0f);
        RenderSettings.sun = sun;

        // Back light: soft and neutral, separates the astronaut from the black sky
        var rim = new GameObject("Rim Light").AddComponent<Light>();
        rim.type = LightType.Directional;
        rim.color = new Color(0.92f, 0.93f, 0.95f);
        rim.intensity = 0.35f;
        rim.shadows = LightShadows.None;
        rim.transform.rotation = Quaternion.Euler(18f, 205f, 0f);

        // Ground: the game's own lunar terrain (same heightmap, craters and hills on the horizon), placed where it
        // is in the game. The astronaut stands on a flat spot near the base area.
        Vector3 terrainPos = Vector3.zero, baseCenter = Vector3.zero;
        ReadGameScene(ref terrainPos, ref baseCenter);
        var terrainData = AssetDatabase.LoadAssetAtPath<TerrainData>("Assets/Terrain/MoonTerrainData.asset");
        Vector3 spot = Vector3.zero;
        if (terrainData != null)
        {
            GameObject terrainGo = Terrain.CreateTerrainGameObject(terrainData);
            terrainGo.name = "Moon Terrain";
            terrainGo.transform.position = terrainPos;
            var terrain = terrainGo.GetComponent<Terrain>();
            var terrainMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Terrain/MoonTerrain.mat");
            if (terrainMat != null) terrain.materialTemplate = terrainMat;
            terrain.heightmapPixelError = 3f;
            Vector3 p = baseCenter + new Vector3(-80f, 0f, -150f); // Just outside the hub, looking out over open ground
            spot = new Vector3(p.x, terrain.SampleHeight(p) + terrainPos.y, p.z);
        }

        // The Earth, hanging above the horizon to the upper right of the view
        GameObject planet = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        planet.name = "Earth";
        planet.transform.localScale = Vector3.one * 75f; // ~6.6 degrees: big enough to read, far enough to feel far
        planet.transform.rotation = Quaternion.Euler(10f, 140f, 20f);
        Object.DestroyImmediate(planet.GetComponent<Collider>());
        var planetRenderer = planet.GetComponent<MeshRenderer>();
        planetRenderer.shadowCastingMode = ShadowCastingMode.Off;
        if (earth != null) planetRenderer.sharedMaterial = earth;
        var body = planet.AddComponent<SkyBody>();
        body.skyDirection = new Vector3(0.62f, 0.28f, 1f); // Beside the astronaut, above the hills
        body.distance = 650f;

        // Camera (MainMenuUI moves it), with post-processing
        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.fieldOfView = 38f;
        cam.nearClipPlane = 0.3f;
        cam.farClipPlane = 2600f;
        cam.clearFlags = CameraClearFlags.Skybox;
        camGo.AddComponent<AudioListener>();
        var camData = camGo.AddComponent<UniversalAdditionalCameraData>();
        camData.renderPostProcessing = true;
        camGo.transform.position = spot + new Vector3(-2.5f, 3f, -13f);

        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/Settings/SampleSceneProfile.asset");
        if (profile != null)
        {
            var volumeGo = new GameObject("Global Volume");
            var volume = volumeGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = profile;
        }

        // Where the astronaut stands, turned toward the camera, and the menu itself
        var spotGo = new GameObject("Astronaut Spot");
        spotGo.transform.SetPositionAndRotation(spot, Quaternion.Euler(0f, 200f, 0f)); // Visor toward the camera, turned a bit toward the title
        var menu = new GameObject("Main Menu").AddComponent<MainMenuUI>();
        menu.astronautSpot = spotGo.transform;

        EditorSceneManager.SaveScene(scene, ScenePath);

        // Build order: the title screen first, then the game
        EditorBuildSettings.scenes = new[]
        {
            new EditorBuildSettingsScene(ScenePath, true),
            new EditorBuildSettingsScene(GameScenePath, true)
        };
        Debug.Log("Main menu built: " + ScenePath + " (first scene of the build, then the game).");
    }
}
