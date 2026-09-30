using System.IO;
using UnityEditor;
using UnityEngine;

// Renders an item prefab into a transparent PNG sprite for the inventory.
// The prefab is shot in an isolated preview scene (own lights, nothing else around) from a 3/4 view.
// It is rendered twice, on black and on white: the difference gives the exact transparency, so it works
// no matter what the render pipeline does with the alpha channel.
public static class ItemIconRenderer
{
    public static Sprite Render(GameObject prefab, string path, int size = 512)
    {
        var preview = new PreviewRenderUtility();
        try
        {
            GameObject instance = preview.InstantiatePrefabInScene(prefab);
            instance.transform.rotation = Quaternion.Euler(0f, -30f, 0f);

            Bounds bounds = new Bounds(instance.transform.position, Vector3.zero);
            bool any = false;
            foreach (Renderer r in instance.GetComponentsInChildren<Renderer>())
            {
                if (!any) { bounds = r.bounds; any = true; }
                else bounds.Encapsulate(r.bounds);
            }

            Camera cam = preview.camera;
            cam.fieldOfView = 25f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            Vector3 viewDirection = new Vector3(0f, 0.55f, -1f).normalized; // Slightly from above
            float radius = bounds.extents.magnitude;
            float distance = radius / Mathf.Sin(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 0.92f;
            cam.transform.position = bounds.center + viewDirection * distance;
            cam.transform.LookAt(bounds.center);
            cam.nearClipPlane = Mathf.Max(0.01f, distance - radius * 2f);
            cam.farClipPlane = distance + radius * 2f;

            preview.lights[0].intensity = 1.3f;
            preview.lights[0].color = new Color(1f, 0.97f, 0.92f);
            preview.lights[0].transform.rotation = Quaternion.Euler(40f, -40f, 0f);
            preview.lights[1].intensity = 0.7f;
            preview.lights[1].color = new Color(0.7f, 0.8f, 1f);
            preview.lights[1].transform.rotation = Quaternion.Euler(20f, 160f, 0f);
            preview.ambientColor = new Color(0.35f, 0.36f, 0.4f);

            Texture2D onBlack = Shoot(preview, size, Color.black);
            Texture2D onWhite = Shoot(preview, size, Color.white);
            Texture2D icon = Combine(onBlack, onWhite);
            Object.DestroyImmediate(onBlack);
            Object.DestroyImmediate(onWhite);

            File.WriteAllBytes(path, icon.EncodeToPNG());
            Object.DestroyImmediate(icon);
        }
        finally
        {
            preview.Cleanup();
        }
        return ImportSprite(path);
    }

    // Turns a PNG in the project into a smooth UI sprite
    internal static Sprite ImportSprite(string path)
    {
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = true; // Drawn much smaller than rendered: mipmaps keep it smooth
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    internal static Texture2D Shoot(PreviewRenderUtility preview, int size, Color background)
    {
        preview.BeginStaticPreview(new Rect(0, 0, size, size));
        preview.camera.backgroundColor = background;
        preview.Render(true, false); // Keep our field of view
        return preview.EndStaticPreview();
    }

    // alpha = 1 - (white - black); the color is the black shot divided by alpha
    internal static Texture2D Combine(Texture2D onBlack, Texture2D onWhite)
    {
        int w = onBlack.width, h = onBlack.height;
        Color[] black = onBlack.GetPixels(), white = onWhite.GetPixels();
        var result = new Color[black.Length];
        for (int i = 0; i < black.Length; i++)
        {
            Color b = black[i], wh = white[i];
            float alpha = Mathf.Clamp01(1f - ((wh.r - b.r) + (wh.g - b.g) + (wh.b - b.b)) / 3f);
            result[i] = alpha > 0.004f
                ? new Color(Mathf.Clamp01(b.r / alpha), Mathf.Clamp01(b.g / alpha), Mathf.Clamp01(b.b / alpha), alpha)
                : Color.clear;
        }
        var texture = new Texture2D(w, h, TextureFormat.RGBA32, false);
        texture.SetPixels(result);
        texture.Apply();
        return texture;
    }
}
