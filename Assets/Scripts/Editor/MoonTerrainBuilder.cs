using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

// Replaces the old flat ground (one big cube with one repeating picture) with a real moon surface:
//   - a Unity Terrain: gentle rolling ground, craters of many sizes (bowl + raised rim) and hills on the horizon
//   - the base area stays perfectly flat, so building still works there
//   - 4 surface textures painted in code (light regolith, dark "mare" regolith, rocky ground, bright crater dust),
//     each repeating at a different size and mixed by a splatmap, so the eye can't spot one repeating tile
// Everything is generated from a fixed seed, so running it again gives the same moon. Change Seed for a new one.
// Runs once automatically; can be repeated with the menu: Survive In Moon > Build Moon Terrain
[InitializeOnLoad]
public static class MoonTerrainBuilder
{
    const string ScenePath = "Assets/Scenes/SampleScene.unity";
    const string Folder = "Assets/Terrain";
    const string TextureFolder = Folder + "/Textures";
    const string DataPath = Folder + "/MoonTerrainData.asset";
    const string MaterialPath = Folder + "/MoonTerrain.mat";
    const string TerrainName = "Moon Terrain";

    const int Seed = 1969;
    const float Size = 3000f;          // Width/length in scene units
    const float HeightRange = 320f;    // Lowest to highest point the terrain can have
    const float BaseLevel = 110f;      // How far the flat ground sits above the terrain's lowest possible point
    const int HeightRes = 2049;        // Height samples per side (~1.5 units apart)
    const int SplatRes = 2048;         // Texture-mix pixels per side
    const int TexSize = 1024;          // Pixel size of each painted surface texture
    const float ViewDistance = 2600f;  // Camera draw distance, so the hills at the terrain edge form the horizon

    static string DoneKey => "SurviveInMoon.MoonTerrainBuilt.v2." + Application.dataPath;

    static MoonTerrainBuilder()
    {
        EditorApplication.delayCall += () =>
        {
            if (EditorPrefs.GetBool(DoneKey, false) || EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (EditorSceneManager.GetActiveScene().path != ScenePath) return; // Try again next time the game scene is open
            if (Build()) EditorPrefs.SetBool(DoneKey, true);
        };
    }

    [MenuItem("Survive In Moon/Build Moon Terrain")]
    static void BuildFromMenu()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
        {
            EditorUtility.DisplayDialog("Moon Terrain", "Open " + ScenePath + " first.", "OK");
            return;
        }
        Build();
    }

    // A crater stamped into the ground. Interior/ejecta are remembered so the textures can follow the shapes.
    struct Crater
    {
        public Vector2 center;
        public float radius, depth, rim, floorPower;
        public bool fresh; // Young crater: bright dust thrown out in rays around it
    }

    public static bool Build()
    {
        try
        {
            var scene = EditorSceneManager.GetActiveScene();

            // Where is the flat ground now, and where is the base?
            Transform oldGround = FindOldGround();
            BaseArea baseArea = Object.FindFirstObjectByType<BaseArea>();
            Vector3 baseCenter = baseArea != null ? baseArea.transform.position : (oldGround != null ? oldGround.position : Vector3.zero);
            float baseRadius = baseArea != null ? baseArea.radius : 300f;
            float groundY = oldGround != null ? oldGround.position.y + oldGround.lossyScale.y * 0.5f : baseCenter.y;

            Directory.CreateDirectory(TextureFolder);
            AssetDatabase.Refresh();

            EditorUtility.DisplayProgressBar("Moon Terrain", "Painting surface textures...", 0.05f);
            TerrainLayer[] layers = BuildLayers();

            EditorUtility.DisplayProgressBar("Moon Terrain", "Shaping hills and craters...", 0.45f);
            Vector3 origin = new Vector3(baseCenter.x - Size * 0.5f, groundY - BaseLevel, baseCenter.z - Size * 0.5f);
            var heights = new float[HeightRes, HeightRes];
            var interior = new float[HeightRes, HeightRes];
            var ejecta = new float[HeightRes, HeightRes];
            BuildHeights(origin, new Vector2(baseCenter.x, baseCenter.z), baseRadius, heights, interior, ejecta);

            EditorUtility.DisplayProgressBar("Moon Terrain", "Mixing textures...", 0.75f);
            float[,,] splat = BuildSplat(origin, heights, interior, ejecta);

            EditorUtility.DisplayProgressBar("Moon Terrain", "Creating the terrain...", 0.9f);
            // Save the TerrainData as an asset FIRST: the texture-mix maps are stored inside it and are only
            // kept if the asset already exists when they are made
            if (AssetDatabase.LoadAssetAtPath<TerrainData>(DataPath) != null) AssetDatabase.DeleteAsset(DataPath);
            TerrainData data = new TerrainData();
            AssetDatabase.CreateAsset(data, DataPath);
            data.heightmapResolution = HeightRes;
            data.alphamapResolution = SplatRes;
            data.baseMapResolution = 1024;
            data.SetDetailResolution(32, 16); // No grass on the moon
            data.size = new Vector3(Size, HeightRange, Size);
            data.terrainLayers = layers;
            data.SetHeights(0, 0, heights);
            data.SetAlphamaps(0, 0, splat);
            EditorUtility.SetDirty(data);

            // Replace a previous terrain, hide the old cube ground (kept, so it can be switched back on)
            GameObject previous = GameObject.Find(TerrainName);
            if (previous != null) Object.DestroyImmediate(previous);
            if (oldGround != null) oldGround.gameObject.SetActive(false);

            GameObject go = Terrain.CreateTerrainGameObject(data);
            go.name = TerrainName;
            go.transform.position = origin;
            var terrain = go.GetComponent<Terrain>();
            terrain.materialTemplate = BuildMaterial();
            terrain.drawInstanced = true;
            terrain.heightmapPixelError = 2f;   // Lower = more detailed far away
            terrain.basemapDistance = 1500f;    // Full-quality texture mixing all the way to the horizon
            terrain.shadowCastingMode = ShadowCastingMode.On;

            // The camera used to stop drawing at 1000 units; the horizon hills are further than that
            Camera cam = Camera.main;
            if (cam != null && cam.farClipPlane < ViewDistance)
            {
                Undo.RecordObject(cam, "Camera view distance");
                cam.farClipPlane = ViewDistance;
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("Moon terrain built: craters, rolling ground and horizon hills; base area kept flat. Scene saved.");
            return true;
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    // The old cube ground (Platform/Cube), even if it was already switched off by an earlier run
    static Transform FindOldGround()
    {
        foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (t.name == "Cube" && t.parent != null && t.parent.name == "Platform") return t;
        return null;
    }

    // ------------------------------------------------------------------ Shape: hills + craters

    static void BuildHeights(Vector3 origin, Vector2 baseCenter, float baseRadius, float[,] heights, float[,] interior, float[,] ejecta)
    {
        float step = Size / (HeightRes - 1);
        var rng = new System.Random(Seed);
        float ox = (float)rng.NextDouble() * 1000f, oz = (float)rng.NextDouble() * 1000f;

        // How "free" the ground is to move: 0 inside the base (stays flat), 1 well outside it
        float Freedom(float x, float z) => SmoothStep(baseRadius + 30f, baseRadius + 220f, Vector2.Distance(new Vector2(x, z), baseCenter));

        // 1) Rolling ground + hills that rise towards the horizon
        for (int iz = 0; iz < HeightRes; iz++)
        {
            float z = origin.z + iz * step;
            for (int ix = 0; ix < HeightRes; ix++)
            {
                float x = origin.x + ix * step;
                float roll = (Fbm(x / 420f + ox, z / 420f + oz, 4) - 0.5f) * 28f
                           + (Fbm(x / 110f + oz, z / 110f + ox, 3) - 0.5f) * 7f;
                float h = roll * Freedom(x, z);

                // Far away (beyond where the player usually goes) the land lifts into soft, rounded mountains.
                // They are always tall enough to hide the terrain's edge (and the old mountains painted in the sky).
                float far = SmoothStep(650f, 1350f, Vector2.Distance(new Vector2(x, z), baseCenter));
                if (far > 0f)
                {
                    float hills = SmoothStep(0.35f, 0.75f, Fbm(x / 300f + ox * 0.3f, z / 300f + oz * 0.3f, 4));
                    h += far * (95f + hills * 110f);
                }
                heights[iz, ix] = h; // In world units for now; converted to 0..1 at the end
            }
        }

        // 2) Craters: a few huge, some medium, lots of small ones. Never inside the base.
        void Scatter(int count, float minR, float maxR, float depthRatio, float floorPower, float freshChance)
        {
            for (int i = 0; i < count; i++)
            {
                // Small craters are much more common than big ones (like on the real moon)
                float r = Mathf.Lerp(minR, maxR, Mathf.Pow((float)rng.NextDouble(), 2f));
                Vector2 c = new Vector2(origin.x + (float)rng.NextDouble() * Size, origin.z + (float)rng.NextDouble() * Size);
                if (Vector2.Distance(c, baseCenter) < baseRadius + 40f + r * 0.8f) continue;
                var crater = new Crater
                {
                    center = c,
                    radius = r,
                    depth = r * depthRatio * Mathf.Lerp(0.8f, 1.2f, (float)rng.NextDouble()),
                    rim = r * 0.05f,
                    floorPower = floorPower,
                    fresh = rng.NextDouble() < freshChance,
                };
                Stamp(crater, origin, step, heights, interior, ejecta, Freedom, (float)rng.NextDouble() * 100f);
            }
        }
        Scatter(7, 110f, 230f, 0.12f, 3.5f, 0.15f);
        Scatter(45, 35f, 100f, 0.17f, 2.6f, 0.3f);
        Scatter(420, 7f, 30f, 0.22f, 2f, 0.25f);

        // 3) World units -> 0..1 of the terrain's height range
        for (int iz = 0; iz < HeightRes; iz++)
            for (int ix = 0; ix < HeightRes; ix++)
                heights[iz, ix] = Mathf.Clamp01((heights[iz, ix] + BaseLevel) / HeightRange);
    }

    static void Stamp(Crater c, Vector3 origin, float step, float[,] heights, float[,] interior, float[,] ejecta,
                      System.Func<float, float, float> freedom, float wobbleSeed)
    {
        float reach = c.radius * (c.fresh ? 3.2f : 2.2f);
        int x0 = Mathf.Max(0, Mathf.FloorToInt((c.center.x - reach - origin.x) / step));
        int x1 = Mathf.Min(HeightRes - 1, Mathf.CeilToInt((c.center.x + reach - origin.x) / step));
        int z0 = Mathf.Max(0, Mathf.FloorToInt((c.center.y - reach - origin.z) / step));
        int z1 = Mathf.Min(HeightRes - 1, Mathf.CeilToInt((c.center.y + reach - origin.z) / step));

        for (int iz = z0; iz <= z1; iz++)
        {
            float z = origin.z + iz * step;
            for (int ix = x0; ix <= x1; ix++)
            {
                float x = origin.x + ix * step;
                Vector2 off = new Vector2(x - c.center.x, z - c.center.y);
                float angle = Mathf.Atan2(off.y, off.x);
                float ca = Mathf.Cos(angle), sa = Mathf.Sin(angle);
                // Real craters aren't perfect circles: wobble the edge a little
                float wobble = 1f + (Mathf.PerlinNoise(ca * 1.6f + wobbleSeed, sa * 1.6f + wobbleSeed) - 0.5f) * 0.18f;
                float d = off.magnitude / (c.radius * wobble); // 0 = centre, 1 = rim

                float shape;
                if (d < 1f) shape = -c.depth + (c.depth + c.rim) * Mathf.Pow(d, c.floorPower); // Bowl rising to the rim
                else { float t = d - 1f; shape = c.rim * Mathf.Exp(-t * t * 6f); }           // Rim fading out
                heights[iz, ix] += shape * freedom(x, z);

                if (d < 1f) interior[iz, ix] = Mathf.Max(interior[iz, ix], 1f - d * d);
                float dust = Mathf.Exp(-Mathf.Pow((d - 1f) / 0.35f, 2f)) * 0.6f; // Every rim is a bit brighter
                if (c.fresh && d > 0.7f)
                {
                    // Young craters throw bright dust out in rays
                    float rays = Mathf.Pow(Mathf.PerlinNoise(ca * 5f + wobbleSeed * 2f, sa * 5f + wobbleSeed), 2.5f) * 1.8f;
                    dust = Mathf.Max(dust, Mathf.Exp(-(d - 0.9f) * 1.4f) * Mathf.Clamp01(0.35f + rays));
                }
                ejecta[iz, ix] = Mathf.Max(ejecta[iz, ix], dust);
            }
        }
    }

    // ------------------------------------------------------------------ Which texture goes where

    static float[,,] BuildSplat(Vector3 origin, float[,] heights, float[,] interior, float[,] ejecta)
    {
        var splat = new float[SplatRes, SplatRes, 4];
        float texel = Size / (SplatRes - 1);
        float hStep = Size / (HeightRes - 1);
        float ox = Seed * 0.37f, oz = Seed * 0.71f;

        for (int az = 0; az < SplatRes; az++)
        {
            int hz = Mathf.Clamp(Mathf.RoundToInt(az * (HeightRes - 1f) / (SplatRes - 1f)), 1, HeightRes - 2);
            float z = origin.z + az * texel;
            for (int ax = 0; ax < SplatRes; ax++)
            {
                int hx = Mathf.Clamp(Mathf.RoundToInt(ax * (HeightRes - 1f) / (SplatRes - 1f)), 1, HeightRes - 2);
                float x = origin.x + ax * texel;

                // Steepness from the neighbouring heights (rise per unit)
                float dx = (heights[hz, hx + 1] - heights[hz, hx - 1]) * HeightRange / (2f * hStep);
                float dz = (heights[hz + 1, hx] - heights[hz - 1, hx]) * HeightRange / (2f * hStep);
                float slope = SmoothStep(0.3f, 0.85f, Mathf.Sqrt(dx * dx + dz * dz));

                float jitter = Fbm(x / 60f + oz, z / 60f + ox, 3);
                // Big dark "seas" (maria) plus smaller darker and brighter patches in between
                float mare = SmoothStep(0.44f, 0.58f, Fbm(x / 450f + ox, z / 450f + oz, 4) + (jitter - 0.5f) * 0.1f) * 0.9f;
                mare = Mathf.Max(mare, SmoothStep(0.56f, 0.68f, Fbm(x / 140f + oz, z / 140f + ox, 3)) * 0.55f);
                float dustPatch = SmoothStep(0.57f, 0.7f, Fbm(x / 180f + oz, z / 180f, 3)) * 0.6f;

                float wRegolith = 1f - mare;
                float wDark = mare;
                float wRock = Mathf.Max(slope, interior[hz, hx] * 0.45f) * Mathf.Lerp(0.6f, 1.5f, jitter) * 1.6f;
                float wBright = Mathf.Max(ejecta[hz, hx], dustPatch) * 1.5f;

                float sum = wRegolith + wDark + wRock + wBright;
                splat[az, ax, 0] = wRegolith / sum;
                splat[az, ax, 1] = wDark / sum;
                splat[az, ax, 2] = wRock / sum;
                splat[az, ax, 3] = wBright / sum;
            }
        }
        return splat;
    }

    // ------------------------------------------------------------------ Surface textures (painted in code)

    struct SurfaceStyle
    {
        public string name;
        public Color dark, light;    // Colour range of the ground
        public float mottle;         // Soft light/dark blotches (kept small so the tile doesn't show)
        public float grain;          // Fine sandy noise
        public int pebbles;          // Small stones
        public float pebbleMax;      // Biggest stone radius in pixels
        public int craterlets;       // Tiny dents
        public float bump;           // Normal map strength
        public float tileSize;       // Scene units one texture covers
    }

    static TerrainLayer[] BuildLayers()
    {
        // Tile sizes are deliberately "odd" numbers, so the four patterns never line up with each other
        var styles = new[]
        {
            new SurfaceStyle { name = "Regolith", dark = new Color(0.47f, 0.47f, 0.46f), light = new Color(0.60f, 0.60f, 0.59f),
                               mottle = 0.35f, grain = 0.06f, pebbles = 90, pebbleMax = 14f, craterlets = 2, bump = 2.2f, tileSize = 29f },
            new SurfaceStyle { name = "DarkRegolith", dark = new Color(0.31f, 0.31f, 0.32f), light = new Color(0.40f, 0.40f, 0.41f),
                               mottle = 0.3f, grain = 0.05f, pebbles = 30, pebbleMax = 10f, craterlets = 1, bump = 1.6f, tileSize = 43f },
            new SurfaceStyle { name = "Rocky", dark = new Color(0.38f, 0.38f, 0.37f), light = new Color(0.55f, 0.55f, 0.54f),
                               mottle = 0.45f, grain = 0.08f, pebbles = 260, pebbleMax = 26f, craterlets = 4, bump = 3.2f, tileSize = 19f },
            new SurfaceStyle { name = "BrightDust", dark = new Color(0.64f, 0.64f, 0.63f), light = new Color(0.76f, 0.76f, 0.75f),
                               mottle = 0.3f, grain = 0.05f, pebbles = 25, pebbleMax = 8f, craterlets = 0, bump = 1.2f, tileSize = 37f },
        };

        var layers = new TerrainLayer[styles.Length];
        for (int i = 0; i < styles.Length; i++)
        {
            EditorUtility.DisplayProgressBar("Moon Terrain", "Painting " + styles[i].name + "...", 0.05f + i * 0.1f);
            layers[i] = BuildLayer(styles[i], Seed + i * 101);
        }
        return layers;
    }

    static TerrainLayer BuildLayer(SurfaceStyle s, int seed)
    {
        int n = TexSize;
        var rng = new System.Random(seed);
        var height = new float[n * n];
        var tone = new float[n * n]; // Extra light/dark on top of the mottle (stones, dents)

        // Soft blotches + fine grain. Uses wrapping noise so the texture tiles without seams.
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = x / (float)n, v = y / (float)n;
                float m = TileFbm(u, v, 4, 5, seed);
                float g = TileFbm(u, v, 128, 2, seed + 7);
                height[y * n + x] = m * 0.6f + g * 0.25f;
                tone[y * n + x] = (m - 0.5f) * s.mottle + (g - 0.5f) * s.grain * 4f;
            }

        // Tiny craters: a dent with a soft raised lip
        for (int i = 0; i < s.craterlets; i++)
        {
            float cx = (float)rng.NextDouble() * n, cy = (float)rng.NextDouble() * n;
            float r = Mathf.Lerp(12f, 40f, (float)rng.NextDouble());
            Splash(n, cx, cy, r * 1.6f, (idx, d) =>
            {
                d *= 1.6f; // d is now 0..1 at the rim
                float dent = d < 1f ? -(1f - d * d) * 0.9f : 0f;
                float lip = Mathf.Exp(-Mathf.Pow((d - 1f) / 0.18f, 2f)) * 0.35f;
                height[idx] += dent + lip;
                tone[idx] += lip * 0.12f - (d < 1f ? (1f - d) * 0.05f : 0f);
            });
        }

        // Pebbles: round bumps, a little lighter or darker than the ground
        for (int i = 0; i < s.pebbles; i++)
        {
            float cx = (float)rng.NextDouble() * n, cy = (float)rng.NextDouble() * n;
            float r = Mathf.Lerp(2.5f, s.pebbleMax, Mathf.Pow((float)rng.NextDouble(), 2.5f));
            float shade = ((float)rng.NextDouble() - 0.4f) * 0.18f;
            Splash(n, cx, cy, r, (idx, d) =>
            {
                float dome = Mathf.Sqrt(1f - d * d);
                height[idx] += dome * r * 0.06f;
                tone[idx] += shade * Mathf.Clamp01(dome * 3f);
            });
        }

        // Colours
        var albedo = new Color[n * n];
        for (int i = 0; i < albedo.Length; i++)
        {
            float t = Mathf.Clamp01(0.5f + tone[i]);
            albedo[i] = Color.Lerp(s.dark, s.light, t);
        }

        // Normal map from the height: the slope in x and y tilts the normal (green = up in the texture)
        var normals = new Color[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float hx = height[y * n + (x + 1) % n] - height[y * n + (x - 1 + n) % n];
                float hy = height[((y + 1) % n) * n + x] - height[((y - 1 + n) % n) * n + x];
                Vector3 nrm = new Vector3(-hx * s.bump, -hy * s.bump, 1f).normalized;
                normals[y * n + x] = new Color(nrm.x * 0.5f + 0.5f, nrm.y * 0.5f + 0.5f, nrm.z * 0.5f + 0.5f, 1f);
            }

        Texture2D diffuse = SaveTexture(TextureFolder + "/Moon" + s.name + ".png", albedo, n, false);
        Texture2D normal = SaveTexture(TextureFolder + "/Moon" + s.name + "_Normal.png", normals, n, true);

        string path = Folder + "/Moon" + s.name + ".terrainlayer";
        TerrainLayer layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
        bool isNew = layer == null;
        if (isNew) layer = new TerrainLayer();
        layer.diffuseTexture = diffuse;
        layer.normalMapTexture = normal;
        layer.normalScale = 1f;
        layer.tileSize = new Vector2(s.tileSize, s.tileSize);
        layer.tileOffset = new Vector2((float)rng.NextDouble() * s.tileSize, (float)rng.NextDouble() * s.tileSize);
        layer.metallic = 0f;
        layer.smoothness = 0.05f;
        if (isNew) AssetDatabase.CreateAsset(layer, path);
        else EditorUtility.SetDirty(layer);
        return layer;
    }

    // Calls paint(pixelIndex, distance 0..1) for every pixel inside a circle, wrapping round the texture edges
    static void Splash(int n, float cx, float cy, float r, System.Action<int, float> paint)
    {
        int x0 = Mathf.FloorToInt(cx - r), x1 = Mathf.CeilToInt(cx + r);
        int y0 = Mathf.FloorToInt(cy - r), y1 = Mathf.CeilToInt(cy + r);
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) / r;
                if (d >= 1f) continue;
                int wx = ((x % n) + n) % n, wy = ((y % n) + n) % n;
                paint(wy * n + wx, d);
            }
    }

    static Texture2D SaveTexture(string path, Color[] pixels, int n, bool isNormal)
    {
        var tex = new Texture2D(n, n, TextureFormat.RGB24, false);
        tex.SetPixels(pixels);
        tex.Apply();
        File.WriteAllBytes(Path.GetFullPath(path), tex.EncodeToPNG());
        Object.DestroyImmediate(tex);

        AssetDatabase.ImportAsset(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = isNormal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        importer.wrapMode = TextureWrapMode.Repeat;
        importer.anisoLevel = 4;  // Stays sharp when seen at a low angle (the ground always is)
        importer.mipmapEnabled = true;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static Material BuildMaterial()
    {
        Material m = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (m == null)
        {
            m = new Material(Shader.Find("Universal Render Pipeline/Terrain/Lit"));
            AssetDatabase.CreateAsset(m, MaterialPath);
        }
        m.EnableKeyword("_NORMALMAP");                          // Use the layers' normal maps (bumpy stones)
        m.SetFloat("_EnableInstancedPerPixelNormal", 1f);       // Sharp hill/crater shading even far away
        m.EnableKeyword("_TERRAIN_INSTANCED_PERPIXEL_NORMAL");
        EditorUtility.SetDirty(m);
        return m;
    }

    // ------------------------------------------------------------------ Noise helpers

    static float SmoothStep(float a, float b, float x)
    {
        float t = Mathf.Clamp01((x - a) / (b - a));
        return t * t * (3f - 2f * t);
    }

    // Fractal Perlin noise (several octaves added up), roughly 0..1
    static float Fbm(float x, float y, int octaves)
    {
        float sum = 0f, amp = 0.5f, freq = 1f, total = 0f;
        for (int i = 0; i < octaves; i++)
        {
            sum += Mathf.PerlinNoise(x * freq, y * freq) * amp;
            total += amp;
            amp *= 0.5f;
            freq *= 2f;
        }
        return sum / total;
    }

    // Noise that repeats exactly every `period` cells, so a texture made from it tiles without a seam
    static float TileNoise(float x, float y, int period, int seed)
    {
        int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
        float fx = x - xi, fy = y - yi;
        fx = fx * fx * fx * (fx * (fx * 6f - 15f) + 10f);
        fy = fy * fy * fy * (fy * (fy * 6f - 15f) + 10f);
        int x0 = ((xi % period) + period) % period, x1 = (x0 + 1) % period;
        int y0 = ((yi % period) + period) % period, y1 = (y0 + 1) % period;
        float a = Mathf.Lerp(Hash(x0, y0, seed), Hash(x1, y0, seed), fx);
        float b = Mathf.Lerp(Hash(x0, y1, seed), Hash(x1, y1, seed), fx);
        return Mathf.Lerp(a, b, fy);
    }

    // u,v in 0..1; basePeriod = how many blobs across the texture for the first octave
    static float TileFbm(float u, float v, int basePeriod, int octaves, int seed)
    {
        float sum = 0f, amp = 0.5f, total = 0f;
        int period = basePeriod;
        for (int i = 0; i < octaves; i++)
        {
            sum += TileNoise(u * period, v * period, period, seed + i * 31) * amp;
            total += amp;
            amp *= 0.5f;
            period *= 2;
        }
        return sum / total;
    }

    static float Hash(int x, int y, int seed)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + seed * 982451653);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / 16777215f;
        }
    }
}
