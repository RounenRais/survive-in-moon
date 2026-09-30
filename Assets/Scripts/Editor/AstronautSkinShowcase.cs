using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Editor-only tool (scripts in an "Editor" folder are never part of the game build).
// Menu: Survive In Moon > Create NPC Prefabs
// Creates one ready-to-use NPC prefab per skin in Assets/Prefabs/NPCs (drag them into any scene)
// and lines them up next to the player.
public static class AstronautSkinShowcase
{
    const string ModelPath = "Assets/Sprites/Astronaut/cute-astronaut/source/Astronaut.fbx";
    const string ControllerPath = "Assets/Sprites/Astronaut/cute-astronaut/source/Astronaut.controller";
    const string PrefabFolder = "Assets/Prefabs/NPCs";
    const string ShowcaseName = "NPCShowcase";
    const float DefaultScale = 4.281f; // Same size as the player astronaut

    [MenuItem("Survive In Moon/Create NPC Prefabs")]
    public static void CreatePrefabs()
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (model == null)
        {
            EditorUtility.DisplayDialog("NPC Prefabs", "Model not found:\n" + ModelPath, "OK");
            return;
        }
        var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);

        if (!AssetDatabase.IsValidFolder("Assets/Prefabs")) AssetDatabase.CreateFolder("Assets", "Prefabs");
        if (!AssetDatabase.IsValidFolder(PrefabFolder)) AssetDatabase.CreateFolder("Assets/Prefabs", "NPCs");

        // Replace an old showcase if the menu is used again (also the one from the older version of this tool)
        foreach (string oldName in new[] { ShowcaseName, "AstronautSkinShowcase" })
        {
            GameObject old = GameObject.Find(oldName);
            if (old != null) Undo.DestroyObjectImmediate(old);
        }

        var player = Object.FindFirstObjectByType<PlayerMovement>();
        Vector3 scale = Vector3.one * DefaultScale; // The player's own object is no longer the scaled model (see PlayerCharacter)
        Vector3 origin = player != null ? player.transform.position : Vector3.zero;
        Vector3 forward = player != null ? player.transform.forward : Vector3.forward;
        Vector3 side = Vector3.Cross(Vector3.up, forward);

        var showcase = new GameObject(ShowcaseName);
        Undo.RegisterCreatedObjectUndo(showcase, "Create NPC Showcase");

        var types = System.Array.FindAll((AstronautSkin.SkinType[])System.Enum.GetValues(typeof(AstronautSkin.SkinType)),
            t => t != AstronautSkin.SkinType.Custom);
        float spacing = 0f;
        for (int i = 0; i < types.Length; i++)
        {
            AstronautSkin.SkinType type = types[i];
            var npc = (GameObject)PrefabUtility.InstantiatePrefab(model);
            npc.name = "NPC_" + type;
            npc.transform.localScale = scale;

            Animator animator = npc.GetComponent<Animator>();
            if (animator == null) animator = npc.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;

            AstronautSkin skin = npc.AddComponent<AstronautSkin>();
            skin.skin = type;
            npc.AddComponent<AstronautAnimation>(); // Without PlayerMovement it only keeps the boots on the ground
            AddBodyCollider(npc);

            skin.Apply();
            if (spacing <= 0f)
            {
                Bounds b = MeshBounds(npc);
                spacing = Mathf.Max(b.size.x, b.size.z) * 1.4f;
            }

            // Helper objects are rebuilt by the component, so they are kept out of the prefab file
            skin.RemoveAccessories();
            PrefabUtility.SaveAsPrefabAssetAndConnect(npc, $"{PrefabFolder}/NPC_{type}.prefab", InteractionMode.AutomatedAction);

            // In a row in front of the player, facing the player
            Vector3 position = origin + forward * spacing * 2f + side * (i - (types.Length - 1) * 0.5f) * spacing;
            npc.transform.SetPositionAndRotation(GroundPoint(position), Quaternion.LookRotation(-forward, Vector3.up));
            npc.transform.SetParent(showcase.transform, true);
            skin.Apply();
        }

        Selection.activeGameObject = showcase;
        EditorSceneManager.MarkSceneDirty(showcase.scene);
        Debug.Log($"NPCs: {types.Length} prefabs created in {PrefabFolder} and placed in the scene.");
    }

    // A capsule around the body so the player bumps into the NPC instead of walking through it
    static void AddBodyCollider(GameObject npc)
    {
        Bounds b = MeshBounds(npc);
        var capsule = npc.AddComponent<CapsuleCollider>();
        float s = Mathf.Abs(npc.transform.lossyScale.y);
        capsule.direction = 1; // Y axis
        capsule.height = b.size.y / s;
        capsule.radius = Mathf.Min(b.size.x, b.size.z) * 0.5f / s * 0.7f; // Body width without the T-pose arms
        capsule.center = npc.transform.InverseTransformPoint(b.center);
    }

    // Finds the ground below a point (the model's pivot is at its feet)
    static Vector3 GroundPoint(Vector3 position)
    {
        foreach (RaycastHit hit in Physics.RaycastAll(position + Vector3.up * 50f, Vector3.down, 200f))
        {
            if (hit.collider.GetComponentInParent<AstronautSkin>() != null) continue; // Not on top of another NPC
            return hit.point;
        }
        return position;
    }

    // Bounds of the real mesh (Renderer.bounds of an animated model is padded)
    static Bounds MeshBounds(GameObject go)
    {
        bool found = false;
        Bounds bounds = new Bounds(go.transform.position, Vector3.zero);
        foreach (SkinnedMeshRenderer smr in go.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            if (smr.name.StartsWith(AstronautSkin.AccessoryPrefix)) continue;
            var mesh = new Mesh();
            smr.BakeMesh(mesh, false);
            Matrix4x4 toWorld = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
            foreach (Vector3 v in mesh.vertices)
            {
                Vector3 w = toWorld.MultiplyPoint3x4(v);
                if (!found) { bounds = new Bounds(w, Vector3.zero); found = true; }
                else bounds.Encapsulate(w);
            }
            Object.DestroyImmediate(mesh);
        }
        return bounds;
    }
}
