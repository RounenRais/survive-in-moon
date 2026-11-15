using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

// Downloads the NASA data the game uses and stores it in Assets/Resources/NasaData, so the game works offline:
//  - DONKI (CCMC, Space Weather Database): solar flares of the last year -> the in-game radiation storms
//  - RadLab (NASA OSDR): radiation dose rates measured on the Moon by LRO/CRaTER (and on the ISS for comparison)
//    -> the base cosmic-ray dose and how strong storms get
//  - OSDR (Open Science Data Repository): plant-growth studies (Veggie on the ISS) -> the greenhouse crops
//  - EPIC (api.nasa.gov, DSCOVR): the latest real photo of the whole Earth -> the Earth in the lunar sky
//  - APOD (api.nasa.gov): the latest Astronomy Picture of the Day -> shown in the main menu's Resources panel
// The api.nasa.gov key is read from UserSettings/NasaApiKey.txt (never committed) or the NASA_API_KEY variable.
// Menu: Survive In Moon > Fetch NASA Data
public static class NasaDataFetcher
{
    const string Folder = "Assets/Resources/NasaData";
    const string KeyFile = "UserSettings/NasaApiKey.txt";

    [MenuItem("Survive In Moon/Fetch NASA Data")]
    public static void Fetch()
    {
        ContentKitFolder();
        var meta = new NasaData.Meta { fetchedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm 'UTC'") };
        var log = new StringBuilder();
        try
        {
            Step("DONKI solar flares", 0.1f); FetchDonki(meta, log);
            Step("RadLab dose rates", 0.35f); FetchRadLab(meta, log);
            Step("OSDR plant studies", 0.55f); FetchOsdr(meta, log);
            Step("EPIC Earth photo", 0.7f); FetchEpic(meta, log);
            Step("APOD", 0.85f); FetchApod(meta, log);
        }
        finally { EditorUtility.ClearProgressBar(); }

        File.WriteAllText($"{Folder}/nasa_meta.json", JsonUtility.ToJson(meta, true));
        AssetDatabase.Refresh();
        Debug.Log("NASA data fetched:\n" + log);
    }

    static void Step(string what, float t) => EditorUtility.DisplayProgressBar("NASA data", what, t);

    static void ContentKitFolder()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Resources", "NasaData");
    }

    static string ApiKey()
    {
        string env = Environment.GetEnvironmentVariable("NASA_API_KEY");
        if (!string.IsNullOrEmpty(env)) return env.Trim();
        return File.Exists(KeyFile) ? File.ReadAllText(KeyFile).Trim() : "DEMO_KEY";
    }

    // --------------------------------------------------------------- DONKI

    [Serializable] class FlrArray { public SolarStorms.Flare[] items; }

    static void FetchDonki(NasaData.Meta meta, StringBuilder log)
    {
        // The DONKI API accepts at most 60 days per request: walk back one year in 60-day steps
        var all = new List<string>();
        DateTime end = DateTime.UtcNow.Date;
        int flares = 0;
        for (int i = 0; i < 6; i++)
        {
            DateTime start = end.AddDays(-59);
            string url = $"https://ccmc.gsfc.nasa.gov/DONKI-API/get/FLR?startDate={start:yyyy-MM-dd}&endDate={end:yyyy-MM-dd}";
            string json = Get(url, out string error);
            if (json == null) { log.AppendLine($"DONKI {start:yyyy-MM-dd}: {error}"); end = start.AddDays(-1); continue; }
            json = json.Trim();
            if (json.StartsWith("[") && json.Length > 2)
            {
                all.Add(json.Substring(1, json.Length - 2));
                flares += JsonUtility.FromJson<FlrArray>("{\"items\":" + json + "}")?.items?.Length ?? 0;
            }
            end = start.AddDays(-1);
        }
        if (all.Count == 0) { log.AppendLine("DONKI: nothing downloaded, the game uses historic flares"); return; }
        File.WriteAllText($"{Folder}/donki_flares.json", "[" + string.Join(",", all) + "]");
        meta.donkiFlares = flares;
        meta.donkiFrom = end.AddDays(1).ToString("yyyy-MM-dd");
        meta.donkiTo = DateTime.UtcNow.ToString("yyyy-MM-dd");
        log.AppendLine($"DONKI: {flares} flares {meta.donkiFrom} .. {meta.donkiTo}");
    }

    // --------------------------------------------------------------- RadLab

    static void FetchRadLab(NasaData.Meta meta, StringBuilder log)
    {
        string moon = Get("https://visualization.osdr.nasa.gov/radlab/api/?celestial_body=Moon&absorbed_dose_rate&instrument&spacecraft&format=csv", out string e1, 180);
        if (moon == null) { log.AppendLine("RadLab Moon: " + e1); return; }
        List<(string time, float dose)> rows = ParseDose(moon);
        if (rows.Count == 0) { log.AppendLine("RadLab Moon: no rows"); return; }
        var sorted = rows.Select(r => r.dose).OrderBy(v => v).ToList();
        meta.moonDoseSamples = sorted.Count;
        meta.moonDoseMedian = sorted[sorted.Count / 2];
        meta.moonDoseP99 = sorted[(int)(sorted.Count * 0.99f)];
        meta.moonDoseMax = sorted[sorted.Count - 1];
        meta.moonDoseFrom = rows.Min(r => r.time).Substring(0, 10);
        meta.moonDoseTo = rows.Max(r => r.time).Substring(0, 10);

        string iss = Get("https://visualization.osdr.nasa.gov/radlab/api/?spacecraft=ISS&absorbed_dose_rate&instrument" +
                         "&timestamp%3E=2022-04-01&timestamp%3C2022-04-08&format=csv", out string e2, 180);
        if (iss != null)
        {
            var issRows = ParseDose(iss).Select(r => r.dose).OrderBy(v => v).ToList();
            if (issRows.Count > 0) meta.issDoseMedian = issRows[issRows.Count / 2];
        }
        else log.AppendLine("RadLab ISS: " + e2);
        log.AppendLine($"RadLab: Moon median {meta.moonDoseMedian:0.0} uGy/h (max {meta.moonDoseMax:0}), ISS {meta.issDoseMedian:0.0} uGy/h");
    }

    static List<(string time, float dose)> ParseDose(string csv)
    {
        var result = new List<(string time, float dose)>();
        string[] lines = csv.Split('\n');
        if (lines.Length < 2) return result;
        string[] header = lines[0].Trim().Split(',');
        int t = Array.IndexOf(header, "timestamp"), d = Array.IndexOf(header, "absorbed_dose_rate");
        if (t < 0 || d < 0) return result;
        for (int i = 1; i < lines.Length; i++)
        {
            string[] cells = lines[i].Trim().Split(',');
            if (cells.Length <= Math.Max(t, d)) continue;
            if (float.TryParse(cells[d], NumberStyles.Float, CultureInfo.InvariantCulture, out float dose))
                result.Add((cells[t].Trim('"'), dose));
        }
        return result;
    }

    // --------------------------------------------------------------- OSDR

    [Serializable] class OsdrResult { public OsdrHits hits; }
    [Serializable] class OsdrHits { public OsdrHit[] hits; }
    [Serializable] class OsdrHit { public OsdrSource _source; }

    static void FetchOsdr(NasaData.Meta meta, StringBuilder log)
    {
        var studies = new List<NasaData.Study>();
        foreach (string term in new[] { "Veggie", "lettuce" })
        {
            string json = Get($"https://osdr.nasa.gov/osdr/data/search?term={term}&type=cgene&size=12", out string error, 90);
            if (json == null) { log.AppendLine($"OSDR {term}: {error}"); continue; }
            // Field names with spaces: rename them so JsonUtility can read them
            json = json.Replace("\"Study Title\"", "\"StudyTitle\"").Replace("\"Flight Program\"", "\"FlightProgram\"");
            var result = JsonUtility.FromJson<OsdrResult>(json);
            if (result?.hits?.hits == null) continue;
            foreach (OsdrHit h in result.hits.hits)
            {
                if (h._source == null || string.IsNullOrEmpty(h._source.Accession)) continue;
                if (studies.Any(s => s.accession == h._source.Accession)) continue;
                string title = h._source.StudyTitle ?? "";
                if (!(title.ToLowerInvariant().Contains("lettuce") || title.Contains("Veggie") || title.ToLowerInvariant().Contains("leafy") ||
                      title.ToLowerInvariant().Contains("plant") || title.ToLowerInvariant().Contains("crop"))) continue;
                studies.Add(new NasaData.Study { accession = h._source.Accession, title = title, program = h._source.FlightProgram ?? "" });
            }
        }
        meta.studies = studies.Take(8).ToArray();
        log.AppendLine($"OSDR: {meta.studies.Length} plant studies");
    }

    [Serializable] class OsdrSource { public string Accession; public string StudyTitle; public string FlightProgram; }

    // --------------------------------------------------------------- EPIC

    [Serializable] class EpicList { public EpicImage[] items; }
    [Serializable] class EpicImage { public string image; public string date; public string caption; }

    static void FetchEpic(NasaData.Meta meta, StringBuilder log)
    {
        string key = ApiKey();
        string json = Get($"https://api.nasa.gov/EPIC/api/natural?api_key={key}", out string error);
        if (json == null) json = Get("https://epic.gsfc.nasa.gov/api/natural", out error);
        if (json == null) { log.AppendLine("EPIC: " + error); return; }
        var list = JsonUtility.FromJson<EpicList>("{\"items\":" + json + "}");
        if (list?.items == null || list.items.Length == 0) { log.AppendLine("EPIC: no images"); return; }
        EpicImage img = list.items[list.items.Length / 2]; // A middle one of the day
        DateTime date = DateTime.Parse(img.date, CultureInfo.InvariantCulture);
        string day = date.ToString("yyyy'/'MM'/'dd", CultureInfo.InvariantCulture); // '/' would be '.' on a Turkish PC
        string url = $"https://epic.gsfc.nasa.gov/archive/natural/{day}/jpg/{img.image}.jpg";
        byte[] bytes = GetBytes(url, out error, 120);
        if (bytes == null) { log.AppendLine("EPIC image: " + error); return; }

        // Cut the Earth disc out of the black frame (transparent outside), so it hangs in the sky as a disc
        var tex = new Texture2D(2, 2);
        tex.LoadImage(bytes);
        Texture2D disc = CutDisc(tex, 1024);
        File.WriteAllBytes($"{Folder}/epic_earth.png", disc.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(tex);
        UnityEngine.Object.DestroyImmediate(disc);
        meta.epicImage = img.image;
        meta.epicDate = img.date;
        log.AppendLine($"EPIC: {img.image} ({img.date})");
    }

    static Texture2D CutDisc(Texture2D src, int size)
    {
        // Bounding box of everything brighter than the black of space
        int w = src.width, h = src.height, minX = w, minY = h, maxX = 0, maxY = 0;
        Color32[] px = src.GetPixels32();
        for (int y = 0; y < h; y += 2)
            for (int x = 0; x < w; x += 2)
            {
                Color32 c = px[y * w + x];
                if (c.r + c.g + c.b < 60) continue;
                minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
            }
        float cx = (minX + maxX) * 0.5f, cy = (minY + maxY) * 0.5f, r = Math.Max(maxX - minX, maxY - minY) * 0.5f + 2f;
        var dst = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var outPx = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size * 2f - 1f, v = (y + 0.5f) / size * 2f - 1f;
                float d = Mathf.Sqrt(u * u + v * v);
                Color c = src.GetPixelBilinear((cx + u * r) / w, (cy + v * r) / h);
                c.a = Mathf.Clamp01((1f - d) * size * 0.5f); // Soft 1-2 pixel edge
                outPx[y * size + x] = c;
            }
        dst.SetPixels(outPx);
        dst.Apply();
        return dst;
    }

    // --------------------------------------------------------------- APOD

    [Serializable] class Apod { public string date, title, explanation, url, media_type, copyright; }

    static void FetchApod(NasaData.Meta meta, StringBuilder log)
    {
        string key = ApiKey();
        for (int back = 0; back < 7; back++)
        {
            string date = DateTime.UtcNow.AddDays(-back).ToString("yyyy-MM-dd");
            string json = Get($"https://api.nasa.gov/planetary/apod?api_key={key}&date={date}", out string error);
            if (json == null) continue;
            var apod = JsonUtility.FromJson<Apod>(json);
            if (apod == null || apod.media_type != "image" || string.IsNullOrEmpty(apod.url)) continue;
            if (apod.title == "NASA Science" || apod.url.Contains("nasa-logo")) continue; // A broken day: placeholder logo
            byte[] bytes = GetBytes(apod.url, out error, 120);
            if (bytes == null) continue;
            var tex = new Texture2D(2, 2);
            if (!tex.LoadImage(bytes)) continue;
            // Keep it small: the panel shows it at ~500 px
            Texture2D small = Scale(tex, 768);
            File.WriteAllBytes($"{Folder}/apod.jpg", small.EncodeToJPG(88));
            UnityEngine.Object.DestroyImmediate(tex);
            UnityEngine.Object.DestroyImmediate(small);
            meta.apodDate = apod.date;
            meta.apodTitle = apod.title;
            meta.apodCredit = string.IsNullOrEmpty(apod.copyright) ? "NASA" : apod.copyright.Trim().Replace("\n", " ");
            log.AppendLine($"APOD: {apod.date} {apod.title}");
            return;
        }
        log.AppendLine("APOD: no image in the last week");
    }

    static Texture2D Scale(Texture2D src, int maxSize)
    {
        float k = Mathf.Min(1f, maxSize / (float)Mathf.Max(src.width, src.height));
        int w = Mathf.Max(1, Mathf.RoundToInt(src.width * k)), h = Mathf.Max(1, Mathf.RoundToInt(src.height * k));
        var dst = new Texture2D(w, h, TextureFormat.RGB24, false);
        var px = new Color[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++) px[y * w + x] = src.GetPixelBilinear((x + 0.5f) / w, (y + 0.5f) / h);
        dst.SetPixels(px);
        dst.Apply();
        return dst;
    }

    // --------------------------------------------------------------- HTTP

    static string Get(string url, out string error, int timeout = 60)
    {
        byte[] b = GetBytes(url, out error, timeout);
        return b != null ? Encoding.UTF8.GetString(b) : null;
    }

    static byte[] GetBytes(string url, out string error, int timeout = 60)
    {
        using (var req = UnityWebRequest.Get(url))
        {
            req.timeout = timeout;
            var op = req.SendWebRequest();
            while (!op.isDone) System.Threading.Thread.Sleep(20);
            if (req.result != UnityWebRequest.Result.Success)
            {
                error = $"{req.responseCode} {req.error}";
                return null;
            }
            error = null;
            return req.downloadHandler.data;
        }
    }
}
