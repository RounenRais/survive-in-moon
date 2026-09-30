using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

// Editor-only tool. Menu: Survive In Moon > Apply Game Font
// Makes Titillium Web (Assets/Resources/Fonts, free OFL license) the font of the whole game:
//  1. Creates TextMeshPro font assets from the .ttf files (Assets/Fonts), with Bold linked to the regular one,
//     so <b> and FontStyles.Bold use the real bold letters
//  2. Makes it TextMeshPro's default font (new texts, and everything built from code, use it)
//  3. Swaps the font in every existing text: the UI prefabs and all scenes
// The OnGUI texts (prompts, hints) load the .ttf themselves through UITheme.GuiFont.
public static class GameFontSetup
{
    const string SourceFolder = "Assets/Resources/Fonts";
    const string AssetFolder = "Assets/Fonts";
    const string SettingsPath = "Assets/TextMesh Pro/Resources/TMP Settings.asset";
    // Put into every font asset up front. The ellipsis matters: without it in the bold font,
    // TextMeshPro draws nothing at all for a bold text that is cut with "..."
    const string Characters = " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~·×…";

    [MenuItem("Survive In Moon/Apply Game Font")]
    public static void Apply()
    {
        TMP_FontAsset regular = FontAsset("TitilliumWeb-Regular");
        TMP_FontAsset semiBold = FontAsset("TitilliumWeb-SemiBold");
        TMP_FontAsset bold = FontAsset("TitilliumWeb-Bold");
        if (regular == null || bold == null) return;

        // Weight table: index 6 = 600 (semi bold), 7 = 700 (bold)
        var so = new SerializedObject(regular);
        SerializedProperty table = so.FindProperty("m_FontWeightTable");
        if (table != null && table.arraySize > 7)
        {
            table.GetArrayElementAtIndex(6).FindPropertyRelative("regularTypeface").objectReferenceValue = semiBold;
            table.GetArrayElementAtIndex(7).FindPropertyRelative("regularTypeface").objectReferenceValue = bold;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        var settings = AssetDatabase.LoadAssetAtPath<TMP_Settings>(SettingsPath);
        if (settings != null)
        {
            var settingsSo = new SerializedObject(settings);
            settingsSo.FindProperty("m_defaultFontAsset").objectReferenceValue = regular;
            settingsSo.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(settings);
        }
        AssetDatabase.SaveAssets();

        int changed = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.StartsWith("Assets/TextMesh Pro")) continue;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null || prefab.GetComponentInChildren<TMP_Text>(true) == null) continue;
            GameObject contents = PrefabUtility.LoadPrefabContents(path);
            changed += SwapFonts(contents, regular);
            PrefabUtility.SaveAsPrefabAsset(contents, path);
            PrefabUtility.UnloadPrefabContents(contents);
        }

        // Scenes: the open ones directly, the others opened for a moment
        string activePath = EditorSceneManager.GetActiveScene().path;
        foreach (string guid in AssetDatabase.FindAssets("t:Scene", new[] { "Assets/Scenes" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var scene = EditorSceneManager.GetSceneByPath(path);
            bool wasOpen = scene.isLoaded;
            if (!wasOpen) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            int count = 0;
            foreach (GameObject root in scene.GetRootGameObjects()) count += SwapFonts(root, regular);
            changed += count;
            if (count > 0) EditorSceneManager.MarkSceneDirty(scene);
            if (!wasOpen)
            {
                if (count > 0) EditorSceneManager.SaveScene(scene);
                EditorSceneManager.CloseScene(scene, true);
            }
            else if (count > 0 && path == activePath) EditorSceneManager.SaveScene(scene);
        }
        Debug.Log($"Game font: Titillium Web is the default TextMeshPro font now; {changed} texts switched.");
    }

    static int SwapFonts(GameObject root, TMP_FontAsset font)
    {
        int count = 0;
        foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
        {
            if (text.font == font) continue;
            Undo.RecordObject(text, "Apply Game Font");
            text.font = font;
            text.fontSharedMaterial = font.material;
            EditorUtility.SetDirty(text);
            count++;
        }
        return count;
    }

    // A dynamic font asset: letters are added to its texture when they are first used
    static TMP_FontAsset FontAsset(string name)
    {
        string path = $"{AssetFolder}/{name} SDF.asset";
        var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        if (existing != null)
        {
            existing.TryAddCharacters(Characters);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        var font = AssetDatabase.LoadAssetAtPath<Font>($"{SourceFolder}/{name}.ttf");
        if (font == null)
        {
            Debug.LogError($"Game font: {SourceFolder}/{name}.ttf not found");
            return null;
        }
        if (!AssetDatabase.IsValidFolder(AssetFolder)) AssetDatabase.CreateFolder("Assets", "Fonts");

        TMP_FontAsset asset = TMP_FontAsset.CreateFontAsset(font, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024,
                                                            AtlasPopulationMode.Dynamic, true);
        asset.name = name + " SDF";
        AssetDatabase.CreateAsset(asset, path);
        // The texture and material live inside the font asset file
        asset.atlasTextures[0].name = name + " Atlas";
        AssetDatabase.AddObjectToAsset(asset.atlasTextures[0], asset);
        asset.material.name = name + " Material";
        AssetDatabase.AddObjectToAsset(asset.material, asset);
        asset.TryAddCharacters(Characters);
        EditorUtility.SetDirty(asset);
        AssetDatabase.SaveAssets();
        return asset;
    }
}
