using UnityEditor;
using UnityEngine;

/// <summary>
/// Applies import settings to the baked sprite set.
///
/// The sprites are produced by a script outside Unity, so they arrive without .meta
/// files. Setting the importer here rather than hand-authoring YAML means the settings
/// are correct by construction and survive a re-bake, a reimport, or someone deleting the
/// Library folder -- all of which would otherwise silently drop them back to default
/// textures and break every sprite reference in the scene.
/// </summary>
public class GeneratedArtPostprocessor : AssetPostprocessor
{
    private const string GeneratedRoot = "Assets/Art/Generated/";

    /// <summary>Matches the 128px blocks so one board cell is exactly one world unit.</summary>
    private const float PixelsPerUnit = 128f;

    /// <summary>
    /// The app icon lives in the same folder but must stay a plain texture: Unity's icon
    /// pipeline takes a Texture2D, and importing it as a sprite makes it unusable there.
    /// </summary>
    private static bool IsIcon(string path) => path.EndsWith("AppIcon.png");

    private void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith(GeneratedRoot) || IsIcon(assetPath)) return;

        var importer = (TextureImporter)assetImporter;

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = PixelsPerUnit;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;

        // Bilinear with clamping: these are drawn at roughly their authored size, and
        // point filtering would show hard stair-stepping on the rounded corners.
        importer.filterMode = FilterMode.Bilinear;
        importer.wrapMode = TextureWrapMode.Clamp;

        // Full Rect rather than Tight. Sliced and tiled draw modes silently misrender on a
        // tight mesh -- Unity warns about it at runtime, not at import -- and these are
        // plain quads, so a tight mesh saves nothing anyway.
        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteMeshType = SpriteMeshType.FullRect;
        settings.spriteGenerateFallbackPhysicsShape = false;
        importer.SetTextureSettings(settings);

        // The board backing stretches to whatever the grid size is, so it has to be
        // 9-sliced or its rounded corners smear. The border matches the 8px inset the
        // generator draws at 96px.
        if (assetPath.EndsWith("BoardFrame.png"))
        {
            importer.spriteBorder = new Vector4(24f, 24f, 24f, 24f);
        }

        importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings
        {
            maxTextureSize = 256,
            // Crunch would introduce colour banding across the block gradients, which is
            // exactly where it would be most visible. These textures are tiny anyway.
            textureCompression = TextureImporterCompression.Uncompressed,
            overridden = false
        });
    }
}
