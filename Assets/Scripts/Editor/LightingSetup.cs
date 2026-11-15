using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Makes the game's lighting clean and readable, in neutral colours (no blue cast), so the astronauts' suit parts
// keep their own colours and separate by light and shade:
//  - removes the old "Character Outline" / "Character Rim" renderer features (their outline broke apart on the
//    low-poly models' hard edges and left black lines), and the "Character" layer they used
//  - the open game scene: neutral three-colour ambient (shadows dark but never pure black), a white Sun and a
//    weak neutral fill light from the opposite side
// Running it again is safe. Menu: Survive In Moon > Apply Lighting
public static class LightingSetup
{
    static readonly string[] OldFeatures = { "Character Outline", "Character Rim" };

    [MenuItem("Survive In Moon/Apply Lighting")]
    public static void Apply()
    {
        foreach (string guid in AssetDatabase.FindAssets("t:UniversalRendererData", new[] { "Assets" })) // Not packages
        {
            var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(AssetDatabase.GUIDToAssetPath(guid));
            if (data != null) RemoveOldFeatures(data);
        }
        RemoveLayer("Character");
        LightGameScene();
        AssetDatabase.SaveAssets();
        Debug.Log("Lighting applied: neutral ambient, white sun, soft fill; outline features removed.");
    }

    static void RemoveOldFeatures(UniversalRendererData data)
    {
        var so = new SerializedObject(data);
        SerializedProperty list = so.FindProperty("m_RendererFeatures");
        SerializedProperty map = so.FindProperty("m_RendererFeatureMap");
        for (int i = list.arraySize - 1; i >= 0; i--)
        {
            Object feature = list.GetArrayElementAtIndex(i).objectReferenceValue;
            if (feature == null || System.Array.IndexOf(OldFeatures, feature.name) < 0) continue;
            list.GetArrayElementAtIndex(i).objectReferenceValue = null;
            list.DeleteArrayElementAtIndex(i);
            if (i < map.arraySize) map.DeleteArrayElementAtIndex(i);
            AssetDatabase.RemoveObjectFromAsset(feature);
            Object.DestroyImmediate(feature, true);
        }
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(data);
    }

    static void RemoveLayer(string name)
    {
        var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        SerializedProperty layers = tagManager.FindProperty("layers");
        for (int i = 0; i < layers.arraySize; i++)
            if (layers.GetArrayElementAtIndex(i).stringValue == name) layers.GetArrayElementAtIndex(i).stringValue = "";
        tagManager.ApplyModifiedPropertiesWithoutUndo();
    }

    static void LightGameScene()
    {
        var scene = EditorSceneManager.GetActiveScene();
        Light sun = RenderSettings.sun;
        if (sun == null)
            sun = Object.FindObjectsByType<Light>(FindObjectsSortMode.None).FirstOrDefault(l => l.type == LightType.Directional);
        if (sun == null) { Debug.LogWarning("Lighting: no Sun (directional light) in " + scene.name); return; }

        // Neutral three-colour ambient: a little from above, grey bounce off the moon dust from below
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.2f, 0.2f, 0.21f);
        RenderSettings.ambientEquatorColor = new Color(0.16f, 0.16f, 0.165f);
        RenderSettings.ambientGroundColor = new Color(0.13f, 0.125f, 0.12f);
        RenderSettings.ambientIntensity = 1f;

        sun.color = new Color(1f, 0.98f, 0.95f);
        sun.intensity = 1.45f;
        sun.shadowStrength = 0.88f; // Moon shadows stay deep, but the shape inside them is still visible

        // Weak neutral light from the side opposite the Sun: fills the shadow sides of the astronauts and buildings
        Light fill = Object.FindObjectsByType<Light>(FindObjectsSortMode.None).FirstOrDefault(l => l.name == "Fill Light");
        if (fill == null) fill = new GameObject("Fill Light").AddComponent<Light>();
        fill.type = LightType.Directional;
        fill.color = new Color(0.9f, 0.91f, 0.93f);
        fill.intensity = 0.22f;
        fill.shadows = LightShadows.None;
        Vector3 e = sun.transform.eulerAngles;
        fill.transform.rotation = Quaternion.Euler(25f, e.y + 180f, 0f);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }
}
