using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Editor-only tool. Menu: Survive In Moon > Setup Character Switching
//  1. Makes the astronaut prefabs that are still missing (AstronautPrefabBuilder)
//  2. Creates one CharacterDefinition per astronaut prefab in Assets/Characters
//  3. Creates (or updates) the list of characters: Assets/Resources/CharacterDatabase.asset
//  4. Splits the player in the open scene into "logic" and "look": a new player object gets the movement scripts,
//     the camera and PlayerCharacter; the astronaut model becomes its child "Model" and can then be swapped.
// Safe to run again: existing characters are kept, and a player that is already split is left alone.
// Everything in the scene can be undone with Ctrl+Z.
public static class CharacterSetupTool
{
    const string PrefabFolder = "Assets/Prefabs/Astronauts";
    const string CharacterFolder = "Assets/Characters";
    const string DatabasePath = "Assets/Resources/" + CharacterDatabase.ResourcePath + ".asset";
    const string ModelPrefix = "Astronaut_";
    const string DefaultCharacterId = "Classic";

    [MenuItem("Survive In Moon/Setup Character Switching")]
    public static void Setup()
    {
        AstronautPrefabBuilder.EnsurePrefabs();
        CharacterDatabase database = BuildDatabase();
        if (database == null) return;

        string sceneResult = SetupPlayer(database);
        Debug.Log($"Characters: {database.characters.Count} in {DatabasePath}. {sceneResult}");
    }

    // ------------------------------------------------------------------ Assets

    static CharacterDatabase BuildDatabase()
    {
        if (!AssetDatabase.IsValidFolder(CharacterFolder)) AssetDatabase.CreateFolder("Assets", "Characters");
        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");

        var database = AssetDatabase.LoadAssetAtPath<CharacterDatabase>(DatabasePath);
        if (database == null)
        {
            database = ScriptableObject.CreateInstance<CharacterDatabase>();
            AssetDatabase.CreateAsset(database, DatabasePath);
        }

        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { PrefabFolder }))
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            if (prefab == null || prefab.GetComponentInChildren<Animator>() == null) continue;
            string id = prefab.name.StartsWith(ModelPrefix) ? prefab.name.Substring(ModelPrefix.Length) : prefab.name;

            string path = $"{CharacterFolder}/{id}.asset";
            var character = AssetDatabase.LoadAssetAtPath<CharacterDefinition>(path);
            if (character == null)
            {
                character = ScriptableObject.CreateInstance<CharacterDefinition>();
                character.id = id;
                character.displayName = ObjectNames.NicifyVariableName(id) + " Astronaut";
                character.prefab = prefab;
                AssetDatabase.CreateAsset(character, path);
            }
            else if (character.prefab == null)
            {
                character.prefab = prefab;
                EditorUtility.SetDirty(character);
            }
            if (!database.characters.Contains(character)) database.characters.Add(character);
        }

        if (database.characters.Count == 0)
        {
            EditorUtility.DisplayDialog("Characters", "No astronaut prefabs found in " + PrefabFolder, "OK");
            return null;
        }
        if (database.defaultCharacter == null)
            database.defaultCharacter = database.Get(DefaultCharacterId) ?? database.characters[0];
        EditorUtility.SetDirty(database);
        AssetDatabase.SaveAssets();
        return database;
    }

    // ------------------------------------------------------------------ Scene

    static string SetupPlayer(CharacterDatabase database)
    {
        var movement = Object.FindFirstObjectByType<PlayerMovement>();
        if (movement == null) return "No player (PlayerMovement) in the open scene, so the scene was not changed.";

        GameObject player = movement.gameObject;
        if (player.GetComponent<Animator>() != null) player = SplitPlayer(player); // Old layout: model = player

        var character = player.GetComponent<PlayerCharacter>();
        if (character == null) character = Undo.AddComponent<PlayerCharacter>(player);
        if (character.model == null) PlacePreviewModel(character, database.Default);

        Selection.activeGameObject = player;
        EditorSceneManager.MarkSceneDirty(player.scene);
        return $"Player \"{player.name}\" is ready; save the scene (Ctrl+S).";
    }

    // Moves everything that isn't the model (scripts, the camera...) to a new, unscaled player object
    static GameObject SplitPlayer(GameObject old)
    {
        Transform oldTransform = old.transform;
        float oldScale = Mathf.Abs(oldTransform.lossyScale.y);

        var player = new GameObject(old.name + " Player");
        Undo.RegisterCreatedObjectUndo(player, "Split Player");
        player.tag = old.tag;
        player.layer = old.layer;
        player.transform.SetParent(oldTransform.parent, false);
        player.transform.SetPositionAndRotation(oldTransform.position, oldTransform.rotation);
        player.transform.SetSiblingIndex(oldTransform.GetSiblingIndex());

        // Children added in the scene (the camera) - not the bones and meshes of the model
        for (int i = oldTransform.childCount - 1; i >= 0; i--)
        {
            Transform child = oldTransform.GetChild(i);
            if (child.name.StartsWith(AstronautSkin.AccessoryPrefix)) continue;
            if (PrefabUtility.IsAddedGameObjectOverride(child.gameObject) || !PrefabUtility.IsPartOfPrefabInstance(child))
                Undo.SetTransformParent(child, player.transform, "Split Player");
        }

        foreach (Component c in old.GetComponents<Component>())
        {
            if (!IsLogic(c)) continue;
            UnityEditorInternal.ComponentUtility.CopyComponent(c);
            // A [RequireComponent] of an earlier script may already have added this one: fill that in instead
            Component existing = player.GetComponent(c.GetType());
            if (existing != null) UnityEditorInternal.ComponentUtility.PasteComponentValues(existing);
            else UnityEditorInternal.ComponentUtility.PasteComponentAsNew(player);
        }

        // The CharacterController's size is in the object's own (local) units: the old object was scaled
        var controller = player.GetComponent<CharacterController>();
        if (controller != null)
        {
            controller.height *= oldScale;
            controller.radius *= oldScale;
            controller.center *= oldScale;
        }
        ClearReferencesInto(player, oldTransform);

        Undo.DestroyObjectImmediate(old);
        return player;
    }

    // Scripts and the CharacterController stay on the player; the model's own parts (and the old skin painter,
    // which only works on the original Astronaut.fbx) don't
    static bool IsLogic(Component c) =>
        !(c is Transform) && !(c is Animator) && !(c is Renderer) && !(c is MeshFilter) && !(c is Rigidbody)
        && !(c is AstronautSkin) && (!(c is Collider) || c is CharacterController);

    // A copied field that pointed at the old object (or its bones) would break when it is deleted: empty it,
    // the scripts find their things by themselves again
    static void ClearReferencesInto(GameObject player, Transform old)
    {
        foreach (MonoBehaviour script in player.GetComponents<MonoBehaviour>())
        {
            var so = new SerializedObject(script);
            SerializedProperty p = so.GetIterator();
            while (p.Next(true))
            {
                if (p.propertyType != SerializedPropertyType.ObjectReference || p.objectReferenceValue == null) continue;
                Transform target = p.objectReferenceValue is Component comp ? comp.transform
                                 : p.objectReferenceValue is GameObject go ? go.transform : null;
                if (target != null && target.IsChildOf(old)) p.objectReferenceValue = null;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    // The default character stands in the scene while editing, so the player is visible;
    // when the game starts PlayerCharacter replaces it with the chosen one
    static void PlacePreviewModel(PlayerCharacter character, CharacterDefinition definition)
    {
        if (definition == null || definition.prefab == null) return;
        var model = (GameObject)PrefabUtility.InstantiatePrefab(definition.prefab, character.transform);
        Undo.RegisterCreatedObjectUndo(model, "Place Player Model");
        model.name = "Model";
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.Euler(0f, definition.yawOffset, 0f);
        Vector3 size = definition.prefab.transform.localScale, parent = character.transform.lossyScale;
        model.transform.localScale = new Vector3(size.x / parent.x, size.y / parent.y, size.z / parent.z);

        // The prefab's NPC parts would fight with the player's own
        foreach (MonoBehaviour script in model.GetComponentsInChildren<MonoBehaviour>(true)) Undo.DestroyObjectImmediate(script);
        foreach (Collider c in model.GetComponentsInChildren<Collider>(true)) Undo.DestroyObjectImmediate(c);

        Undo.RecordObject(character, "Place Player Model");
        character.model = model.transform;
    }
}
