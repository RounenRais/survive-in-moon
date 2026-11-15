using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Builds the title screen scene (Assets/Scenes/MainMenu.unity) from scratch and makes it the first scene of the
// build, followed by the game scene. The scene holds only the 3D backdrop; MainMenuUI makes the astronaut and the UI.
//   - a big curved piece of moon ground with a few rocks (the horizon bends away like on a small world)
//   - the starfield sky, the Earth hanging above the horizon
//   - a warm Sun from the front-left and a cool rim light from behind, so the astronaut stands out of the dark
// Menu: Survive In Moon > Build Main Menu
public static class MainMenuBuilder
{
    const string ScenePath = "Assets/Scenes/MainMenu.unity";
    const string GameScenePath = "Assets/Scenes/SampleScene.unity";

    [MenuItem("Survive In Moon/Build Main Menu")]
    public static void Build()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var ground = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/MoonGround.mat");
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

        // Ground: a wide, flat sphere - its top is at y = 0 and it curves down toward the horizon
        GameObject moon = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        moon.name = "Moon Ground";
        moon.transform.localScale = new Vector3(900f, 24f, 900f);
        moon.transform.position = new Vector3(0f, -12f, 0f);
        Object.DestroyImmediate(moon.GetComponent<Collider>());
        if (ground != null) moon.GetComponent<MeshRenderer>().sharedMaterial = ground;

        // A few half-buried rocks for depth (fixed seed: the same layout every time)
        var rocks = new GameObject("Rocks").transform;
        Random.State old = Random.state;
        Random.InitState(1969);
        var spots = new List<Vector3>
        {
            new Vector3(14f, 0f, 10f), new Vector3(-22f, 0f, 30f), new Vector3(30f, 0f, 46f), new Vector3(-8f, 0f, 60f),
            new Vector3(52f, 0f, 90f), new Vector3(-48f, 0f, 84f), new Vector3(9f, 0f, -4f), new Vector3(-16f, 0f, 6f),
            new Vector3(70f, 0f, 30f), new Vector3(-70f, 0f, 50f), new Vector3(24f, 0f, 120f), new Vector3(-30f, 0f, 140f)
        };
        foreach (Vector3 spot in spots)
        {
            GameObject rock = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            rock.name = "Rock";
            rock.transform.SetParent(rocks, false);
            float size = Random.Range(1.2f, 4.5f) * (1f + spot.z / 120f);
            rock.transform.localScale = new Vector3(size * Random.Range(1f, 1.6f), size * Random.Range(0.45f, 0.8f), size * Random.Range(1f, 1.5f));
            rock.transform.position = spot - Vector3.up * size * 0.15f;
            rock.transform.rotation = Quaternion.Euler(Random.Range(-12f, 12f), Random.Range(0f, 360f), Random.Range(-12f, 12f));
            Object.DestroyImmediate(rock.GetComponent<Collider>());
            if (ground != null) rock.GetComponent<MeshRenderer>().sharedMaterial = ground;
        }
        Random.state = old;

        // The Earth, hanging above the horizon to the upper right of the view
        GameObject planet = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        planet.name = "Earth";
        planet.transform.localScale = Vector3.one * 170f;
        planet.transform.rotation = Quaternion.Euler(10f, 140f, 20f);
        Object.DestroyImmediate(planet.GetComponent<Collider>());
        var planetRenderer = planet.GetComponent<MeshRenderer>();
        planetRenderer.shadowCastingMode = ShadowCastingMode.Off;
        if (earth != null) planetRenderer.sharedMaterial = earth;
        var body = planet.AddComponent<SkyBody>();
        body.skyDirection = new Vector3(0.36f, 0.24f, 1f);
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
        camGo.transform.position = new Vector3(-2.5f, 3f, -13f);

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
        spotGo.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, 200f, 0f)); // Visor toward the camera, turned a bit toward the title
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
