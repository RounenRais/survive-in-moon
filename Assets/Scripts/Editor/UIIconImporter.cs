using UnityEditor;

// The status icons in Assets/Resources/UI/Icons (white Material Symbols glyphs, Apache 2.0) come in as UI sprites,
// so the HUD can tint them with each stat's colour.
public class UIIconImporter : AssetPostprocessor
{
    void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith("Assets/Resources/UI/Icons/")) return;
        var importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = true; // Drawn small: mipmaps keep the edges clean
        importer.textureCompression = TextureImporterCompression.Uncompressed;
    }
}
