using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Builds the Lunar Roving Vehicle model out of simple shapes, saves it as a prefab and puts one in the game scene.
// Runs once by itself; can be repeated with the menu: Survive In Moon > Create Moon Rover
// Sizes are in scene units: the astronaut is about 16 units tall (the real LRV is ~1.7 astronauts long).
[InitializeOnLoad]
public static class MoonRoverBuilder
{
    const string PrefabPath = "Assets/Prefabs/Vehicles/MoonRover.prefab";
    const string MaterialFolder = "Assets/Materials/Rover";
    const string ScenePath = "Assets/Scenes/SampleScene.unity";
    static string DoneKey => "SurviveInMoon.MoonRoverPlaced." + Application.dataPath;

    const float WheelRadius = 3.6f, WheelWidth = 2.4f, TrackHalf = 9.5f, BaseHalf = 9.2f;

    static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();

    static MoonRoverBuilder()
    {
        EditorApplication.delayCall += () =>
        {
            if (EditorPrefs.GetBool(DoneKey, false) || EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (EditorSceneManager.GetActiveScene().path != ScenePath) return;
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null) BuildPrefab();
            if (Object.FindFirstObjectByType<MoonRover>() == null) PlaceInScene();
            EditorPrefs.SetBool(DoneKey, true);
        };
    }

    [MenuItem("Survive In Moon/Create Moon Rover")]
    public static void CreateFromMenu()
    {
        BuildPrefab();
        if (EditorSceneManager.GetActiveScene().path == ScenePath && Object.FindFirstObjectByType<MoonRover>() == null) PlaceInScene();
    }

    // --------------------------------------------------------------- Scene

    static void PlaceInScene()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        var player = Object.FindFirstObjectByType<PlayerMovement>();
        Vector3 origin = player != null ? player.transform.position : Vector3.zero;
        Vector3 forward = player != null ? Vector3.ProjectOnPlane(player.transform.forward, Vector3.up).normalized : Vector3.forward;
        Vector3 spot = FindFreeSpot(origin, forward);

        var rover = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        rover.transform.SetPositionAndRotation(spot, Quaternion.LookRotation(-forward, Vector3.up)); // Facing the player
        Undo.RegisterCreatedObjectUndo(rover, "Place Moon Rover");
        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("Moon rover placed in the scene. Walk up to it and press F to drive.");
    }

    // Tries spots in growing circles around the player until one has nothing in the way (NPCs, rocks...)
    static Vector3 FindFreeSpot(Vector3 origin, Vector3 forward)
    {
        Physics.SyncTransforms(); // Make sure the physics world knows where everything in the scene is
        Vector3 halfSize = new Vector3(12f, 8f, 16f); // A bit bigger than the rover
        for (float distance = 45f; distance <= 300f; distance += 25f)
        {
            for (int step = 0; step < 12; step++)
            {
                Vector3 dir = Quaternion.AngleAxis(30f + step * 30f, Vector3.up) * forward; // Start to the front-right
                Vector3 candidate = origin + dir * distance;
                if (!Physics.Raycast(candidate + Vector3.up * 100f, Vector3.down, out RaycastHit ground, 300f)) continue;
                Vector3 center = ground.point + Vector3.up * (halfSize.y + 0.5f);
                bool blocked = false;
                foreach (Collider c in Physics.OverlapBox(center, halfSize, Quaternion.identity))
                    if (c != ground.collider) { blocked = true; break; }
                if (!blocked) return ground.point;
            }
        }
        return origin + forward * 45f;
    }

    // --------------------------------------------------------------- Model

    public static void BuildPrefab()
    {
        materials.Clear();
        EnsureFolder("Assets/Prefabs");
        EnsureFolder("Assets/Prefabs/Vehicles");
        EnsureFolder(MaterialFolder);

        var root = new GameObject("MoonRover");
        Transform body = Group("Body", root.transform, Vector3.zero);

        // Chassis: flat aluminium deck with side rails and a dark floor panel under the seats
        Box("Deck", body, new Vector3(0f, 5.2f, 0f), new Vector3(14f, 0.7f, 24f), "Frame");
        Box("RailLeft", body, new Vector3(-7.2f, 5.2f, 0f), new Vector3(0.6f, 0.7f, 26f), "FrameDark");
        Box("RailRight", body, new Vector3(7.2f, 5.2f, 0f), new Vector3(0.6f, 0.7f, 26f), "FrameDark");
        Box("FloorPanel", body, new Vector3(0f, 5.6f, -1f), new Vector3(13f, 0.2f, 9f), "FrameDark");
        Box("FootRest", body, new Vector3(0f, 6.0f, 5.2f), new Vector3(12f, 0.4f, 0.4f), "FrameDark");

        // Seats (blue, like the real LRV's nylon seats)
        foreach (float x in new[] { -3.6f, 3.6f })
        {
            Box("SeatBase", body, new Vector3(x, 6.2f, -1.5f), new Vector3(5.2f, 0.9f, 5f), "Seat");
            Box("SeatBack", body, new Vector3(x, 9.0f, -4.3f), new Vector3(5.2f, 5.5f, 0.7f), "Seat", Quaternion.Euler(-12f, 0f, 0f));
            Box("SeatFrame", body, new Vector3(x, 8.8f, -4.8f), new Vector3(5.6f, 0.35f, 0.35f), "FrameDark");
        }

        // Control console with a display and three lights, and the T-handle hand controller between the seats
        Cylinder("ConsolePost", body, new Vector3(0f, 7.6f, 2.4f), new Vector3(0.6f, 1.4f, 0.6f), "FrameDark");
        Transform console = Box("Console", body, new Vector3(0f, 10.2f, 3f), new Vector3(6f, 3f, 1.2f), "Dark", Quaternion.Euler(-20f, 0f, 0f));
        Box("LightRed", console, new Vector3(-0.3f, 0.15f, -0.55f), new Vector3(0.12f, 0.12f, 0.2f), "LightRed", Quaternion.identity, true);
        Box("LightGreen", console, new Vector3(0f, 0.15f, -0.55f), new Vector3(0.12f, 0.12f, 0.2f), "LightGreen", Quaternion.identity, true);
        Box("LightYellow", console, new Vector3(0.3f, 0.15f, -0.55f), new Vector3(0.12f, 0.12f, 0.2f), "Gold", Quaternion.identity, true);
        Cylinder("HandController", body, new Vector3(0f, 7.6f, 0.6f), new Vector3(0.4f, 1.1f, 0.4f), "Dark");
        Box("HandControllerBar", body, new Vector3(0f, 8.7f, 0.6f), new Vector3(1.8f, 0.35f, 0.35f), "Orange");

        // Front: gold-foil electronics box with a white radiator on top, TV camera and two antennas
        Box("ElectronicsBox", body, new Vector3(0f, 7.2f, 9.6f), new Vector3(11f, 3.2f, 3.8f), "Gold");
        Box("Radiator", body, new Vector3(0f, 8.9f, 9.6f), new Vector3(10f, 0.25f, 3.4f), "White");
        Cylinder("CameraPost", body, new Vector3(3.8f, 10.3f, 10.8f), new Vector3(0.35f, 1.6f, 0.35f), "FrameDark");
        Transform tv = Box("TvCamera", body, new Vector3(3.8f, 12.3f, 10.8f), new Vector3(1.8f, 1.7f, 2.8f), "Dark");
        Cylinder("TvLens", tv, new Vector3(0f, 0f, 0.6f), new Vector3(0.6f, 0.15f, 0.6f), "Lens", Quaternion.Euler(90f, 0f, 0f), true);
        Cylinder("DishPole", body, new Vector3(-4.8f, 13.4f, 10.4f), new Vector3(0.35f, 4.6f, 0.35f), "FrameDark");
        Sphere("Dish", body, new Vector3(-4.8f, 18.2f, 10.8f), new Vector3(8f, 1.5f, 8f), "White", Quaternion.Euler(-35f, 0f, 0f));
        Cylinder("DishFeed", body, new Vector3(-4.8f, 18.9f, 11.5f), new Vector3(0.3f, 1.2f, 0.3f), "FrameDark", Quaternion.Euler(-35f, 0f, 0f));
        Cylinder("LowGainPole", body, new Vector3(4.8f, 11.5f, 8.4f), new Vector3(0.25f, 2.6f, 0.25f), "FrameDark");
        Sphere("LowGainAntenna", body, new Vector3(4.8f, 14.8f, 8.4f), new Vector3(1.2f, 2.4f, 1.2f), "White");

        // Rear: tool pallet with orange straps and a small orange pennant
        Box("ToolPallet", body, new Vector3(0f, 7.1f, -10.9f), new Vector3(12f, 3.6f, 3.2f), "FrameDark");
        foreach (float x in new[] { -3f, 3f })
            Box("Strap", body, new Vector3(x, 7.1f, -10.9f), new Vector3(0.6f, 3.7f, 3.3f), "Orange");
        Cylinder("FlagPole", body, new Vector3(-6.2f, 9.6f, -12.2f), new Vector3(0.2f, 3.8f, 0.2f), "White");
        Box("Pennant", body, new Vector3(-6.2f, 12.3f, -13.5f), new Vector3(0.1f, 1.6f, 2.6f), "Orange");

        // Wheels: steering pivot -> spinner (rolls and moves with the suspension) -> tire, hub, chevron treads
        var pivots = new Transform[4];
        var spinners = new Transform[4];
        string[] names = { "FL", "FR", "RL", "RR" };
        for (int i = 0; i < 4; i++)
        {
            float x = (i % 2 == 0 ? -1f : 1f) * TrackHalf;
            float z = (i < 2 ? 1f : -1f) * BaseHalf;
            float side = Mathf.Sign(x);
            Transform pivot = Group("Wheel_" + names[i], root.transform, new Vector3(x, WheelRadius, z));
            Transform spinner = Group("Spinner", pivot, Vector3.zero);
            Quaternion axle = Quaternion.Euler(0f, 0f, 90f); // Cylinders stand on Y; lay them on their side (axle = X)
            Cylinder("Tire", spinner, Vector3.zero, new Vector3(WheelRadius * 2f, WheelWidth * 0.5f, WheelRadius * 2f), "Tire", axle);
            Cylinder("Hub", spinner, new Vector3(side * 0.1f, 0f, 0f), new Vector3(WheelRadius * 0.9f, WheelWidth * 0.52f, WheelRadius * 0.9f), "Silver", axle);
            Cylinder("HubCap", spinner, new Vector3(side * (WheelWidth * 0.5f + 0.05f), 0f, 0f), new Vector3(1.2f, 0.12f, 1.2f), "Orange", axle);
            const int treads = 14;
            for (int t = 0; t < treads; t++)
            {
                float angle = t * 360f / treads;
                Quaternion around = Quaternion.AngleAxis(angle, Vector3.right);
                Vector3 radial = around * Vector3.up;
                Quaternion zigzag = Quaternion.AngleAxis(t % 2 == 0 ? 18f : -18f, radial); // Chevron pattern
                Box("Tread", spinner, radial * (WheelRadius + 0.05f), new Vector3(WheelWidth * 1.02f, 0.35f, 0.9f), "Tread", zigzag * around);
            }
            // Fender over the top of the wheel (turns with the wheel, stays still while it rolls)
            for (int f = 0; f < 3; f++)
            {
                float angle = 45f + f * 45f; // Front-top, top, back-top
                Quaternion around = Quaternion.AngleAxis(angle - 90f, Vector3.right);
                Vector3 radial = around * Vector3.up;
                Box("Fender", pivot, radial * (WheelRadius + 1.5f), new Vector3(WheelWidth + 0.6f, 0.25f, 3.1f), "Orange", around);
            }
            // Suspension arm from the chassis rail to the wheel
            Box("SuspensionArm", body, new Vector3(side * 8.1f, 4.5f, z), new Vector3(2.2f, 0.45f, 0.45f), "FrameDark");
            pivots[i] = pivot;
            spinners[i] = spinner;
        }

        // Where the astronaut sits (commander seat on the left) and where the "F" prompt floats
        Transform seat = Group("DriverSeat", root.transform, new Vector3(-3.6f, 3.2f, -1.2f));
        Transform hips = Group("HipsAnchor", root.transform, new Vector3(-3.6f, 7.6f, -1.8f));
        Transform prompt = Group("PromptPoint", root.transform, new Vector3(0f, 21f, 0f));

        // Physics: one box for the whole body (the wheels use rays, not colliders)
        var box = root.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, 7.2f, 0f);
        box.size = new Vector3(15.5f, 4.6f, 26f);
        var rb = root.AddComponent<Rigidbody>();
        rb.mass = 600f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        var rover = root.AddComponent<MoonRover>();
        rover.wheelPivots = pivots;
        rover.wheelSpinners = spinners;
        rover.driverSeat = seat;
        rover.hipsAnchor = hips;
        rover.promptPoint = prompt;
        rover.wheelRadius = WheelRadius;

        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        Debug.Log("Moon rover prefab created: " + PrefabPath);
    }

    // --------------------------------------------------------------- Helpers

    static Transform Group(string name, Transform parent, Vector3 localPosition)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        return go.transform;
    }

    static Transform Box(string name, Transform parent, Vector3 pos, Vector3 size, string material,
                         Quaternion? rotation = null, bool localToParentScale = false)
        => Part(PrimitiveType.Cube, name, parent, pos, size, material, rotation, localToParentScale);

    static Transform Cylinder(string name, Transform parent, Vector3 pos, Vector3 size, string material,
                              Quaternion? rotation = null, bool localToParentScale = false)
        => Part(PrimitiveType.Cylinder, name, parent, pos, size, material, rotation, localToParentScale);

    static Transform Sphere(string name, Transform parent, Vector3 pos, Vector3 size, string material, Quaternion? rotation = null)
        => Part(PrimitiveType.Sphere, name, parent, pos, size, material, rotation, false);

    // One simple shape without a collider. localToParentScale: small details placed on a scaled parent (e.g. lights on the console)
    static Transform Part(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 size, string material,
                          Quaternion? rotation, bool localToParentScale)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = rotation ?? Quaternion.identity;
        go.transform.localScale = size;
        if (!localToParentScale && parent.lossyScale != Vector3.one)
        {
            // Keep the given size in world units even under a scaled parent
            Vector3 p = parent.lossyScale;
            go.transform.localScale = new Vector3(size.x / p.x, size.y / p.y, size.z / p.z);
        }
        go.GetComponent<Renderer>().sharedMaterial = GetMaterial(material);
        return go.transform;
    }

    static Material GetMaterial(string name)
    {
        if (materials.TryGetValue(name, out Material cached)) return cached;
        string path = $"{MaterialFolder}/Rover_{name}.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(material, path);
        }
        // name: (color, metallic, smoothness)
        (Color color, float metallic, float smooth) look = name switch
        {
            "Frame" => (new Color(0.80f, 0.80f, 0.83f), 0.4f, 0.35f),
            "FrameDark" => (new Color(0.45f, 0.46f, 0.50f), 0.3f, 0.3f),
            "Seat" => (new Color(0.25f, 0.38f, 0.64f), 0f, 0.2f),
            "Dark" => (new Color(0.16f, 0.17f, 0.2f), 0.2f, 0.4f),
            "Lens" => (new Color(0.05f, 0.05f, 0.08f), 0f, 0.9f),
            "Gold" => (new Color(0.91f, 0.69f, 0.23f), 0.6f, 0.55f),
            "White" => (new Color(0.94f, 0.94f, 0.95f), 0f, 0.3f),
            "Silver" => (new Color(0.78f, 0.79f, 0.82f), 0.7f, 0.5f),
            "Orange" => (new Color(1f, 0.6f, 0.2f), 0f, 0.3f),
            "Tire" => (new Color(0.28f, 0.29f, 0.31f), 0.3f, 0.25f),
            "Tread" => (new Color(0.45f, 0.45f, 0.48f), 0.4f, 0.3f),
            "LightRed" => (new Color(1f, 0.2f, 0.2f), 0f, 0.6f),
            "LightGreen" => (new Color(0.3f, 1f, 0.4f), 0f, 0.6f),
            _ => (Color.magenta, 0f, 0f)
        };
        material.SetColor("_BaseColor", look.color);
        material.SetFloat("_Metallic", look.metallic);
        material.SetFloat("_Smoothness", look.smooth);
        if (name.StartsWith("Light"))
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", look.color * 1.5f);
        }
        EditorUtility.SetDirty(material);
        materials[name] = material;
        return material;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int slash = path.LastIndexOf('/');
        AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
    }
}
