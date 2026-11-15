using System;
using System.Collections.Generic;
using UnityEngine;

// Radiation storms driven by real solar flares from NASA's DONKI database (Space Weather Database Of
// Notifications, Knowledge, Information - https://ccmc.gsfc.nasa.gov/tools/DONKI/). The flares are fetched in the
// editor (Survive In Moon > Fetch NASA Data) into Resources/NasaData/donki_flares.json and replayed in order:
// every few minutes the next real flare "arrives" - first a warning, then a storm whose strength follows the flare
// class (C = weak, M = strong, X = extreme). Outside the base the radiation dose rises fast, so get under cover.
// Without fetched data a few historic flares are used instead.
public class SolarStorms : MonoBehaviour
{
    public float firstStormAfter = 240f;            // Seconds of calm at the start
    public Vector2 timeBetweenStorms = new Vector2(300f, 480f);
    public float warningSeconds = 30f;

    public static float Strength { get; private set; } // 0 = calm, ~1 = X-class storm right now
    public static Flare Current { get; private set; }  // The flare being warned about or happening (null = calm)
    public static bool Warning { get; private set; }    // True during the warning before the storm
    public static float SecondsToStorm { get; private set; }
    public static IReadOnlyList<Flare> Flares => flares;
    public static event Action<string> Announce;

    [Serializable]
    public class Flare
    {
        public string flrID;
        public string beginTime;
        public string peakTime;
        public string endTime;
        public string classType;      // e.g. "M2.1", "X1.0"
        public string sourceLocation; // Position on the Sun, e.g. "S14W27"
        public int activeRegionNum;

        // C1 -> 0.15 ... M1 -> 0.45 ... X1 -> 0.9 ... X5+ -> 1.3
        public float Intensity
        {
            get
            {
                if (string.IsNullOrEmpty(classType)) return 0.2f;
                float.TryParse(classType.Substring(1), System.Globalization.NumberStyles.Float,
                               System.Globalization.CultureInfo.InvariantCulture, out float number);
                number = Mathf.Max(1f, number);
                switch (char.ToUpperInvariant(classType[0]))
                {
                    case 'X': return Mathf.Min(1.3f, 0.9f + 0.08f * (number - 1f));
                    case 'M': return 0.45f + 0.045f * (number - 1f);
                    case 'C': return 0.15f + 0.03f * (number - 1f);
                    default: return 0.1f;
                }
            }
        }

        // Real flares last minutes to hours; in the game that becomes 45..120 seconds
        public float GameDuration => Mathf.Lerp(45f, 120f, Mathf.InverseLerp(0.15f, 1.3f, Intensity));

        public string Date => string.IsNullOrEmpty(peakTime) ? beginTime : peakTime.Replace("T", " ").Replace("Z", " UTC");
    }

    [Serializable] class FlareList { public Flare[] items; }

    private static readonly List<Flare> flares = new List<Flare>();
    private int next;
    private float timer, stormLeft, stormLength;

    void Awake()
    {
        LoadFlares();
        Strength = 0f;
        Current = null;
        Warning = false;
        timer = firstStormAfter;
        // Don't always start with the same flare
        next = flares.Count > 0 ? UnityEngine.Random.Range(0, flares.Count) : 0;
    }

    void OnDestroy()
    {
        Strength = 0f;
        Current = null;
        Warning = false;
    }

    public static void LoadFlares()
    {
        flares.Clear();
        TextAsset json = Resources.Load<TextAsset>("NasaData/donki_flares");
        if (json != null)
        {
            // DONKI returns a bare JSON array; JsonUtility needs an object around it
            FlareList list = JsonUtility.FromJson<FlareList>("{\"items\":" + json.text + "}");
            if (list?.items != null)
                foreach (Flare f in list.items)
                    if (!string.IsNullOrEmpty(f.classType) && "CMX".IndexOf(char.ToUpperInvariant(f.classType[0])) >= 0) flares.Add(f);
        }
        if (flares.Count == 0)
        {
            // A few well-known historic flares as a fallback (DONKI archive)
            flares.Add(new Flare { flrID = "2017-09-06T11:53:00-FLR-001", peakTime = "2017-09-06T12:02Z", classType = "X9.3", sourceLocation = "S09W34", activeRegionNum = 12673 });
            flares.Add(new Flare { flrID = "2024-05-14T16:46:00-FLR-001", peakTime = "2024-05-14T16:51Z", classType = "X8.7", sourceLocation = "S18W90", activeRegionNum = 13664 });
            flares.Add(new Flare { flrID = "2024-05-10T06:27:00-FLR-001", peakTime = "2024-05-10T06:54Z", classType = "X3.9", sourceLocation = "S17W29", activeRegionNum = 13664 });
            flares.Add(new Flare { flrID = "2023-12-31T21:36:00-FLR-001", peakTime = "2023-12-31T21:55Z", classType = "X5.0", sourceLocation = "N04E73", activeRegionNum = 13536 });
        }
        // Weak C flares would barely matter: keep mostly M and X, so every storm is worth reacting to
        flares.RemoveAll(f => char.ToUpperInvariant(f.classType[0]) == 'C' && flares.Count > 6);
    }

    void Update()
    {
        if (flares.Count == 0) return;
        float dt = Time.deltaTime;

        if (stormLeft > 0f)
        {
            stormLeft -= dt;
            // Rises quickly, fades out over the last third
            float t = 1f - stormLeft / stormLength;
            float shape = t < 0.15f ? t / 0.15f : t > 0.66f ? Mathf.InverseLerp(1f, 0.66f, t) : 1f;
            Strength = Current.Intensity * shape;
            if (stormLeft <= 0f)
            {
                Strength = 0f;
                Current = null;
                Announce?.Invoke("The radiation storm is over");
                timer = UnityEngine.Random.Range(timeBetweenStorms.x, timeBetweenStorms.y);
            }
            return;
        }

        timer -= dt;
        if (!Warning && timer <= warningSeconds)
        {
            Warning = true;
            Current = flares[next % flares.Count];
            next++;
            Announce?.Invoke($"Solar flare {Current.classType} detected - radiation storm in {Mathf.CeilToInt(warningSeconds)} s. Get to the base!");
        }
        SecondsToStorm = Mathf.Max(0f, timer);
        if (Warning && timer <= 0f)
        {
            Warning = false;
            stormLength = stormLeft = Current.GameDuration;
        }
    }
}
