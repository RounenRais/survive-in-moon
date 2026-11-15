using System;
using UnityEngine;

// The NASA data the game was built with (fetched in the editor by Survive In Moon > Fetch NASA Data into
// Resources/NasaData). Read-only at runtime, so the game works offline. See NasaDataFetcher for the sources.
public static class NasaData
{
    [Serializable]
    public class Study
    {
        public string accession;
        public string title;
        public string program;
    }

    [Serializable]
    public class Meta
    {
        public string fetchedAt;
        // DONKI
        public int donkiFlares;
        public string donkiFrom, donkiTo;
        // RadLab (absorbed dose rate, microgray per hour)
        public int moonDoseSamples;
        public float moonDoseMedian, moonDoseP99, moonDoseMax, issDoseMedian;
        public string moonDoseFrom, moonDoseTo;
        // OSDR
        public Study[] studies = new Study[0];
        // EPIC
        public string epicImage, epicDate;
        // APOD
        public string apodDate, apodTitle, apodCredit;
    }

    private static Meta meta;
    private static bool loaded;

    public static Meta Info
    {
        get
        {
            if (!loaded)
            {
                loaded = true;
                TextAsset json = Resources.Load<TextAsset>("NasaData/nasa_meta");
                meta = json != null ? JsonUtility.FromJson<Meta>(json.text) : null;
            }
            return meta;
        }
    }

    public static bool HasRadiation => Info != null && Info.moonDoseMedian > 0f;

    // How much a storm multiplies the everyday dose in the game: from how far the 1% worst measured hours on
    // the Moon are above a normal hour (robust against single extreme readings), kept within playable limits
    public static float StormMultiplier => HasRadiation
        ? Mathf.Clamp(Info.moonDoseP99 / Mathf.Max(0.1f, Info.moonDoseMedian) * 2f, 10f, 40f) : 27f;

    public static Texture2D EarthPhoto => Resources.Load<Texture2D>("NasaData/epic_earth");
    public static Texture2D Apod => Resources.Load<Texture2D>("NasaData/apod");

    // Replaces the painted Earth in the sky with NASA's latest real photo of it (EPIC on DSCOVR), as a disc
    // that always faces the camera - from the Moon the Earth looks like exactly that.
    public static void UseEarthPhoto()
    {
        Texture2D photo = EarthPhoto;
        if (photo == null) return;
        foreach (SkyBody body in UnityEngine.Object.FindObjectsByType<SkyBody>(FindObjectsSortMode.None))
        {
            if (body.alignToLight != null || body.GetComponent<EarthPhoto>() != null) continue; // The Sun, or done
            body.gameObject.AddComponent<EarthPhoto>().Init(photo);
        }
    }
}

// A camera-facing quad with the EPIC photo, in place of the Earth sphere's own renderer
public class EarthPhoto : MonoBehaviour
{
    private Transform quad;

    public void Init(Texture2D photo)
    {
        var renderer = GetComponent<MeshRenderer>();
        if (renderer != null) renderer.enabled = false;
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = "EPIC Earth";
        Destroy(go.GetComponent<Collider>());
        quad = go.transform;
        quad.SetParent(transform, false);
        quad.localScale = Vector3.one; // The sphere's scale is the Earth's size
        var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        m.SetTexture("_BaseMap", photo);
        m.SetFloat("_Surface", 1f); // Transparent: the disc edge blends into the sky
        m.SetFloat("_Blend", 0f);
        m.SetOverrideTag("RenderType", "Transparent");
        m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.SetInt("_ZWrite", 0);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        var r = go.GetComponent<MeshRenderer>();
        r.sharedMaterial = m;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    void LateUpdate()
    {
        Camera cam = Camera.main;
        if (quad == null || cam == null) return;
        quad.rotation = Quaternion.LookRotation(quad.position - cam.transform.position, Vector3.up);
    }
}
