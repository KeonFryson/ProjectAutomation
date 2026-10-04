using UnityEditor;
using UnityEngine;

/// <summary>Keeps ConveyorArrows.png pixel-perfect: point filtering, no compression, no NPOT resize.</summary>
public class ConveyorArrowsImporter : AssetPostprocessor
{
    void OnPreprocessTexture()
    {
        if (!assetPath.EndsWith("Resources/ConveyorArrows.png")) return;

        var importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Default;
        importer.filterMode = FilterMode.Point;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.maxTextureSize = 2048;
    }
}
