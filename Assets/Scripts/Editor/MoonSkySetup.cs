using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

// Builds the realistic-ish moon sky in the game scene, from inside Unity (so an open scene's in-memory copy
// is the one changed, like MoonEnvironmentSetup):
//   - a black, starless sky (there is no air on the moon to scatter light into stars/blue)
//   - the Sun: a plain bright disk that sits exactly where the one Directional Light shines from, so the
//     shadows in the world clearly come from the visible sun
//   - the Earth: a stylised blue marble (a procedurally painted sphere) hanging in the sky, lit by the same
//     Sun so it shows a phase, the way we really see Earth from the moon
//   - moon-like brightness: one fairly strong Sun plus a little cool fill light, nothing washed out
// Run again any time with: Survive In Moon > Apply Moon Sky (Sun + Earth)
public static class MoonSkySetup
{
    const string SkyPath = "Assets/Materials/SpaceSky.mat";
    const string SunMatPath = "Assets/Materials/Sun.mat";
    const string EarthMatPath = "Assets/Materials/Earth.mat";
    const string EarthTexPath = "Assets/Materials/EarthTexture.png";

    [MenuItem("Survive In Moon/Apply Moon Sky (Sun + Earth)")]
    static void Apply()
    {
        var scene = EditorSceneManager.GetActiveScene();

        // 1) Stars on: the starfield skybox at normal exposure (space is black but full of stars)
        Material sky = AssetDatabase.LoadAssetAtPath<Material>(SkyPath);
        if (sky != null)
        {
            sky.SetFloat("_Exposure", 1f);
            EditorUtility.SetDirty(sky);
        }

        // 2) The one Directional Light becomes the Sun
        Light sun = FindSun();
        if (sun == null)
        {
            var go = new GameObject("Sun");
            sun = go.AddComponent<Light>();
            sun.type = LightType.Directional;
        }
        sun.gameObject.name = "Sun";
        sun.color = new Color(1f, 0.96f, 0.9f);          // Slightly warm white sunlight
        sun.intensity = 1.1f;                             // Moon-bright, not blown out
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = 1f;                          // Deep shadows (thin moon atmosphere)
        sun.transform.rotation = Quaternion.Euler(42f, 325f, 0f); // A fairly high sun with long, readable shadows

        // 3) Lighting: a low, cool ambient fill so shadows are dark but not pure black (still playable)
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.10f, 0.11f, 0.14f);
        RenderSettings.reflectionIntensity = 0.2f;
        RenderSettings.sun = sun;

        // 4) Rebuild the sky bodies under a fresh "Sky" root
        GameObject oldRoot = GameObject.Find("Sky");
        if (oldRoot != null) Object.DestroyImmediate(oldRoot);
        var root = new GameObject("Sky");

        // The Sun disk: an unlit, gently glowing ball aligned with the light. Kept small, so that - like in
        // reality seen from the moon - the Earth looks far bigger than the Sun (~3.7x its width).
        Material sunMat = MakeUnlit(SunMatPath, new Color(1f, 0.95f, 0.82f));
        GameObject sunBall = MakeSkySphere("Sun Disk", root.transform, sunMat, 70f);
        var sunBody = sunBall.AddComponent<SkyBody>();
        sunBody.alignToLight = sun;
        sunBody.distance = 650f;

        // The Earth: a big blue marble that glows its own colours (emission), so it is clearly visible in the
        // sky even on the side the Sun doesn't light. Hung high and to one side.
        Texture2D earthTex = BuildEarthTexture();
        Material earthMat = MakeEarth(EarthMatPath, earthTex);
        GameObject earth = MakeSkySphere("Earth", root.transform, earthMat, 260f);
        var earthBody = earth.AddComponent<SkyBody>();
        // ~18 degrees above the horizon: low enough to sit inside the normal third-person view
        earthBody.skyDirection = new Vector3(0.55f, 0.32f, -0.75f);
        earthBody.distance = 650f;

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("Moon sky applied: starfield sky + a small visible Sun (aligned with the shadows) + a big glowing Earth. Scene saved.");
    }

    static Light FindSun()
    {
        foreach (Light l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            if (l.type == LightType.Directional) return l;
        return null;
    }

    // A sphere that only paints the sky: no collider (so nothing bumps it) and it casts no shadow
    static GameObject MakeSkySphere(string name, Transform parent, Material material, float diameter)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localScale = Vector3.one * diameter;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        var renderer = go.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return go;
    }

    static Material MakeUnlit(string path, Color color)
    {
        Material m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        m.SetColor("_BaseColor", color);
        SaveAsset(m, path);
        return m;
    }

    static Material MakeEarth(string path, Texture2D tex)
    {
        Material m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        m.SetTexture("_BaseMap", tex);
        m.SetColor("_BaseColor", Color.white);
        m.SetFloat("_Smoothness", 0.1f); // Mostly matte, so it doesn't look like a shiny marble
        // Self-glow with its own colours so the Earth reads clearly even where the Sun doesn't hit it
        m.EnableKeyword("_EMISSION");
        m.SetTexture("_EmissionMap", tex);
        m.SetColor("_EmissionColor", new Color(0.7f, 0.75f, 0.85f));
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        SaveAsset(m, path);
        return m;
    }

    static void SaveAsset(Material m, string path)
    {
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null)
        {
            existing.shader = m.shader;
            existing.CopyPropertiesFromMaterial(m);
            existing.shaderKeywords = m.shaderKeywords;      // Keep emission (and other) keywords
            existing.globalIlluminationFlags = m.globalIlluminationFlags;
            EditorUtility.SetDirty(existing);
        }
        else
        {
            AssetDatabase.CreateAsset(m, path);
        }
    }

    // --------------------------------------------------------------- Earth texture (painted in code)

    static Texture2D BuildEarthTexture()
    {
        const int W = 512, H = 256;
        var tex = new Texture2D(W, H, TextureFormat.RGB24, true);
        float landSeed = Random.value * 97f;
        float cloudSeed = Random.value * 53f;

        Color ocean = new Color(0.10f, 0.32f, 0.70f);
        Color deep = new Color(0.06f, 0.20f, 0.50f);
        Color land = new Color(0.26f, 0.54f, 0.26f);
        Color sand = new Color(0.58f, 0.50f, 0.30f);

        var pixels = new Color[W * H];
        for (int y = 0; y < H; y++)
        {
            float v = y / (float)H;
            float pole = Mathf.Abs(v - 0.5f) * 2f; // 0 at equator, 1 at the poles
            for (int x = 0; x < W; x++)
            {
                float u = x / (float)W;
                float continents = Fbm(u * 3.5f + landSeed, v * 3.5f, 4);
                Color c;
                if (continents < 0.5f)
                    c = Color.Lerp(deep, ocean, Mathf.InverseLerp(0.3f, 0.5f, continents));
                else
                {
                    float t = Mathf.InverseLerp(0.5f, 0.75f, continents);
                    c = Color.Lerp(sand, land, t);
                }
                // Ice caps at the poles
                if (pole > 0.8f) c = Color.Lerp(c, Color.white, Mathf.InverseLerp(0.8f, 1f, pole));
                // A thin layer of swirling white clouds
                float clouds = Fbm(u * 5f + cloudSeed, v * 5f + cloudSeed, 3);
                if (clouds > 0.58f) c = Color.Lerp(c, Color.white, Mathf.InverseLerp(0.58f, 0.78f, clouds) * 0.9f);
                pixels[y * W + x] = c;
            }
        }
        tex.SetPixels(pixels);
        tex.Apply();

        // Save it as a real asset so the material keeps it between sessions
        File.WriteAllBytes(Path.GetFullPath(EarthTexPath), tex.EncodeToPNG());
        AssetDatabase.ImportAsset(EarthTexPath);
        var importer = (TextureImporter)AssetImporter.GetAtPath(EarthTexPath);
        if (importer != null)
        {
            importer.wrapModeU = TextureWrapMode.Repeat;
            importer.wrapModeV = TextureWrapMode.Clamp;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(EarthTexPath);
    }

    // Fractal Perlin noise (several octaves added up), returned roughly in 0..1
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
}
