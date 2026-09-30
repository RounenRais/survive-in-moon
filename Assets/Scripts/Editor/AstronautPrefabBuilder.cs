using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// Editor-only tool: turns every astronaut model in Assets/Models/Astronauts into a ready-to-use prefab
// in Assets/Prefabs/Astronauts, together with its own Animator Controller:
//  - Speed (float) blends Idle -> Walk, the same parameter PlayerMovement sets
//  - PickUp (trigger) plays the Grab clip once (InteractionSystem sets it; the clip's Animation Events
//    InteractionContact / InteractionEnd come from AstronautModelPostprocessor)
// Each prefab also gets AstronautAnimation (keeps the boots on the ground) and a body collider, and has the
// player's scale: all of them share Astronaut.fbx's body, so the same scale gives the same body size
// (measuring the height instead would shrink the ones with tall hats). The scene is not changed.
// Runs once by itself for models that have no prefab yet; the menu rebuilds all of them:
// Survive In Moon > Create Astronaut Prefabs
[InitializeOnLoad]
public static class AstronautPrefabBuilder
{
    const string ModelFolder = "Assets/Models/Astronauts";
    const string PrefabFolder = "Assets/Prefabs/Astronauts";
    const string ControllerFolder = "Assets/Prefabs/Astronauts/Animators";
    const float ReferenceScale = 4.281f; // Scale of the player / NPC astronauts made from Astronaut.fbx

    static AstronautPrefabBuilder()
    {
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (ModelPaths().Any(path => AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(path)) == null))
                Build(false);
        };
    }

    // Makes the prefabs that are still missing (used by CharacterSetupTool)
    public static void EnsurePrefabs() => Build(false);

    [MenuItem("Survive In Moon/Create Astronaut Prefabs")]
    public static void CreateFromMenu() => Build(true);

    static string[] ModelPaths() =>
        AssetDatabase.FindAssets("t:Model", new[] { ModelFolder })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(path => path.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase))
            .ToArray();

    static string PrefabPath(string modelPath) =>
        $"{PrefabFolder}/{System.IO.Path.GetFileNameWithoutExtension(modelPath)}.prefab";

    static void Build(bool rebuildExisting)
    {
        if (!AssetDatabase.IsValidFolder("Assets/Prefabs")) AssetDatabase.CreateFolder("Assets", "Prefabs");
        if (!AssetDatabase.IsValidFolder(PrefabFolder)) AssetDatabase.CreateFolder("Assets/Prefabs", "Astronauts");
        if (!AssetDatabase.IsValidFolder(ControllerFolder)) AssetDatabase.CreateFolder(PrefabFolder, "Animators");

        int created = 0;
        foreach (string modelPath in ModelPaths())
        {
            if (!rebuildExisting && AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(modelPath)) != null) continue;
            if (BuildPrefab(modelPath)) created++;
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"Astronaut prefabs: {created} created in {PrefabFolder}.");
    }

    static bool BuildPrefab(string modelPath)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        if (model == null) return false;
        string name = model.name;

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
        try
        {
            instance.name = name;
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            instance.transform.localScale = Vector3.one * ReferenceScale;

            Animator animator = instance.GetComponent<Animator>();
            if (animator == null) animator = instance.AddComponent<Animator>();
            animator.runtimeAnimatorController = BuildController(modelPath, name);
            animator.applyRootMotion = false;

            if (instance.GetComponent<AstronautAnimation>() == null)
                instance.AddComponent<AstronautAnimation>(); // Without PlayerMovement it only plants the feet
            if (instance.GetComponent<CapsuleCollider>() == null) AddBodyCollider(instance);

            PrefabUtility.SaveAsPrefabAsset(instance, PrefabPath(modelPath));
            return true;
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    // One controller per model, using the clips inside that model's FBX
    static AnimatorController BuildController(string modelPath, string name)
    {
        AnimationClip[] clips = AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<AnimationClip>()
            .Where(c => !c.name.StartsWith("__preview__")).ToArray();
        AnimationClip idle = clips.FirstOrDefault(c => c.name == "Idle");
        AnimationClip walk = clips.FirstOrDefault(c => c.name == "Walk");
        AnimationClip grab = clips.FirstOrDefault(c => c.name == "Grab");

        string path = $"{ControllerFolder}/{name}.controller";
        AssetDatabase.DeleteAsset(path); // Start clean when rebuilding
        var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        AnimatorStateMachine machine = controller.layers[0].stateMachine;

        AnimatorState locomotion = controller.CreateBlendTreeInController("Locomotion", out BlendTree tree, 0);
        tree.blendParameter = "Speed";
        tree.useAutomaticThresholds = false;
        if (idle != null) tree.AddChild(idle, 0f);
        if (walk != null) tree.AddChild(walk, 1f);
        machine.defaultState = locomotion;

        if (grab != null)
        {
            controller.AddParameter("PickUp", AnimatorControllerParameterType.Trigger);
            AnimatorState grabState = machine.AddState("Grab");
            grabState.motion = grab;

            AnimatorStateTransition toGrab = locomotion.AddTransition(grabState);
            toGrab.AddCondition(AnimatorConditionMode.If, 0f, "PickUp");
            toGrab.hasExitTime = false;
            toGrab.duration = 0.1f;

            AnimatorStateTransition back = grabState.AddTransition(locomotion);
            back.hasExitTime = true;
            back.exitTime = 0.95f;
            back.duration = 0.15f;
        }

        if (idle == null || walk == null)
            Debug.LogWarning($"Astronaut prefabs: {name} is missing an Idle or Walk clip.", controller);
        return controller;
    }

    // A capsule around the body so the player bumps into the astronaut instead of walking through it
    static void AddBodyCollider(GameObject go)
    {
        Bounds b = MeshBounds(go);
        var capsule = go.AddComponent<CapsuleCollider>();
        float s = Mathf.Abs(go.transform.lossyScale.y);
        capsule.direction = 1; // Y axis
        capsule.height = b.size.y / s;
        capsule.radius = Mathf.Min(b.size.x, b.size.z) * 0.5f / s * 0.7f; // Body width without the T-pose arms
        capsule.center = go.transform.InverseTransformPoint(b.center);
    }

    // Bounds of the real mesh (Renderer.bounds of an animated model is padded)
    static Bounds MeshBounds(GameObject go)
    {
        bool found = false;
        Bounds bounds = new Bounds(go.transform.position, Vector3.zero);
        foreach (SkinnedMeshRenderer smr in go.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            var mesh = new Mesh();
            smr.BakeMesh(mesh, false); // World-sized vertices; only position and rotation are needed (as in PlayerMovement)
            Matrix4x4 toWorld = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
            foreach (Vector3 v in mesh.vertices)
            {
                Vector3 w = toWorld.MultiplyPoint3x4(v);
                if (!found) { bounds = new Bounds(w, Vector3.zero); found = true; }
                else bounds.Encapsulate(w);
            }
            Object.DestroyImmediate(mesh);
        }
        foreach (MeshFilter mf in go.GetComponentsInChildren<MeshFilter>())
        {
            if (mf.sharedMesh == null) continue;
            foreach (Vector3 v in mf.sharedMesh.vertices)
            {
                Vector3 w = mf.transform.TransformPoint(v);
                if (!found) { bounds = new Bounds(w, Vector3.zero); found = true; }
                else bounds.Encapsulate(w);
            }
        }
        return bounds;
    }
}
