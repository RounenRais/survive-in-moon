using System.IO;
using UnityEngine;

// Records smooth cinematic clips of the game for the presentation website: the camera flies a scripted path
// (orbit around the base, flight to the crashed ship, ...) and every frame is rendered off-screen at a fixed
// size and saved as a JPG. Time.captureFramerate locks game time to the frame count, so the clip is smooth
// even if the editor renders slowly. Started from the editor bridge: record <shot> <seconds> <folder>.
public class CinematicRecorder : MonoBehaviour
{
    public string shot = "base";
    public int frames = 240;
    public int fps = 30;
    public int width = 1600, height = 900;
    public string folder;

    public static bool Busy { get; private set; }
    public static int FramesWritten { get; private set; }

    private Camera cam;
    private CameraOrbit orbit;
    private RenderTexture rt;
    private Texture2D readback;
    private int frame;
    private Vector3 center, target;

    public static void Begin(string shot, float seconds, string folder)
    {
        if (Busy) return;
        var go = new GameObject("Cinematic Recorder");
        var r = go.AddComponent<CinematicRecorder>();
        r.shot = shot;
        r.frames = Mathf.RoundToInt(seconds * r.fps);
        r.folder = folder;
    }

    void Start()
    {
        Busy = true;
        FramesWritten = 0;
        Directory.CreateDirectory(folder);
        foreach (string f in Directory.GetFiles(folder, "*.jpg")) File.Delete(f);
        cam = Camera.main;
        orbit = cam != null ? cam.GetComponent<CameraOrbit>() : null;
        if (orbit != null) orbit.enabled = false;
        Time.captureFramerate = fps;
        rt = new RenderTexture(width, height, 24) { antiAliasing = 4 };
        readback = new Texture2D(width, height, TextureFormat.RGB24, false);
        center = BaseArea.Main != null ? BaseArea.Main.transform.position : Vector3.zero;
        target = Target();
    }

    Vector3 Target()
    {
        switch (shot)
        {
            case "ship": return ShipWreck.Instance != null ? ShipWreck.Instance.transform.position : center;
            case "pod":
                foreach (SupplyCrate c in FindObjectsByType<SupplyCrate>(FindObjectsSortMode.None))
                    if (c.unlocksRover) return c.transform.position;
                return center;
            case "journey": return ShipWreck.Instance != null ? ShipWreck.Instance.transform.position : center + Vector3.forward * 1000f;
            case "player": return SurvivalStats.Player != null ? SurvivalStats.Player.transform.position : center;
            default: return center;
        }
    }

    void LateUpdate()
    {
        if (cam == null) { Finish(); return; }
        float t = frames > 1 ? frame / (float)(frames - 1) : 0f;
        float e = t * t * (3f - 2f * t); // Ease in/out
        Place(e, t);

        cam.targetTexture = rt;
        cam.Render();
        cam.targetTexture = null;
        RenderTexture.active = rt;
        readback.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        readback.Apply();
        RenderTexture.active = null;
        File.WriteAllBytes(Path.Combine(folder, $"frame_{frame:0000}.jpg"), readback.EncodeToJPG(92));
        FramesWritten = ++frame;
        if (frame >= frames) Finish();
    }

    // The camera path of each shot (e = eased 0..1, t = linear 0..1)
    void Place(float e, float t)
    {
        Transform c = cam.transform;
        switch (shot)
        {
            case "base": // Slow orbit around the base, the crew at work below
            {
                float a = Mathf.Lerp(200f, 320f, e) * Mathf.Deg2Rad;
                Vector3 p = center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 175f;
                c.position = Ground(p) + Vector3.up * 62f;
                c.LookAt(center + Vector3.up * 8f);
                break;
            }
            case "ship":
            case "pod":
            {
                float radius = shot == "ship" ? 105f : 55f, up = shot == "ship" ? 38f : 22f;
                float a = Mathf.Lerp(-30f, 70f, e) * Mathf.Deg2Rad;
                Vector3 p = target + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius;
                c.position = Ground(p) + Vector3.up * up;
                c.LookAt(target + Vector3.up * (shot == "ship" ? 8f : 4f));
                break;
            }
            case "journey": // Low flight from the base out to the crashed ship
            {
                Vector3 from = center, to = target;
                Vector3 dir = Vector3.ProjectOnPlane(to - from, Vector3.up).normalized;
                Vector3 p = Vector3.Lerp(from - dir * 60f, to - dir * 140f, e);
                c.position = Ground(p) + Vector3.up * Mathf.Lerp(90f, 55f, e);
                c.rotation = Quaternion.Slerp(Quaternion.LookRotation(dir + Vector3.down * 0.35f),
                                              Quaternion.LookRotation((to + Vector3.up * 10f) - c.position), Mathf.SmoothStep(0f, 1f, (t - 0.6f) / 0.4f));
                break;
            }
            case "earth": // From the ground, tilting up from the base to the Earth in the black sky
            {
                Vector3 p = center + new Vector3(-150f, 0f, -120f);
                c.position = Ground(p) + Vector3.up * 14f;
                Vector3 earth = center;
                foreach (SkyBody b in FindObjectsByType<SkyBody>(FindObjectsSortMode.None))
                    if (b.alignToLight == null) earth = b.transform.position;
                Quaternion look1 = Quaternion.LookRotation((center + Vector3.up * 10f) - c.position);
                Quaternion look2 = Quaternion.LookRotation(earth - c.position);
                c.rotation = Quaternion.Slerp(look1, look2, e);
                break;
            }
            default: // "player": behind and above the player
            {
                Transform pl = SurvivalStats.Player != null ? SurvivalStats.Player.transform : null;
                Vector3 at = pl != null ? pl.position : center;
                float a = Mathf.Lerp(250f, 290f, e) * Mathf.Deg2Rad;
                c.position = at + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 40f + Vector3.up * 18f;
                c.LookAt(at + Vector3.up * 8f);
                break;
            }
        }
    }

    static Vector3 Ground(Vector3 p)
    {
        Terrain terrain = Terrain.activeTerrain;
        if (terrain != null) return new Vector3(p.x, terrain.SampleHeight(p) + terrain.transform.position.y, p.z);
        return p;
    }

    void Finish()
    {
        Time.captureFramerate = 0;
        if (orbit != null) orbit.enabled = true;
        if (rt != null) rt.Release();
        Busy = false;
        Destroy(gameObject);
    }
}
