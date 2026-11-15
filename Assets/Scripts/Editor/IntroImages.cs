using System.IO;
using UnityEditor;
using UnityEngine;

// Renders the pictures of the intro story (transparent PNGs of the game's own models) into Resources/Intro.
// Menu: Survive In Moon > Render Intro Images
public static class IntroImages
{
    const string Folder = "Assets/Resources/Intro";

    [MenuItem("Survive In Moon/Render Intro Images")]
    public static string Render()
    {
        Directory.CreateDirectory(Folder);
        string result = "";
        foreach (var (prefabPath, file) in new[]
        {
            ("Assets/Models/Astronauts/nasa(moon)/nasa(moon)/rocket.fbx", "ship"), // The NASA moon pack's rocket
            ("Assets/Prefabs/Buildings/BaseHub.prefab", "base"),
            ("Assets/Prefabs/Vehicles/MoonRover.prefab", "rover"),
        })
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null) { result += $"missing {prefabPath}\n"; continue; }
            BridgeRenderer.Render(prefab, $"{Folder}/{file}.png", 1024);
            result += $"{file}.png\n";
        }
        // The Moon: a sphere with the lunar ground material
        var moon = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Object.DestroyImmediate(moon.GetComponent<Collider>());
        var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/MoonGround.mat");
        if (mat != null) moon.GetComponent<MeshRenderer>().sharedMaterial = mat;
        moon.transform.localScale = Vector3.one * 20f;
        GameObject moonPrefab = PrefabUtility.SaveAsPrefabAsset(moon, "Assets/Prefabs/Content/IntroMoon.prefab");
        Object.DestroyImmediate(moon);
        BridgeRenderer.Render(moonPrefab, $"{Folder}/moon.png", 1024);
        result += "moon.png\n";
        AssetDatabase.Refresh();
        return result;
    }
}
