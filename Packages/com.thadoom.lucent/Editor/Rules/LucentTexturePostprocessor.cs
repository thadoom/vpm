using System.IO;
using UnityEditor;
using UnityEngine;

namespace Lucent
{
    /// <summary>
    /// This is the piece that answers "why does Unity keep resampling my 4K textures to 2K".
    /// Unity's built-in default Max Size is 2048 and it is written into the .meta the first time
    /// an asset is imported. We intercept that first import and write sane defaults instead.
    ///
    /// It only fires when importSettingsMissing is true, i.e. the asset has no .meta yet.
    /// Re-importing, moving, or hand-editing a texture will never have its settings stomped.
    /// </summary>
    public class LucentTexturePostprocessor : AssetPostprocessor
    {
        // Run late so user postprocessors and SDK importers get first say.
        public override int GetPostprocessOrder() => 1000;

        void OnPreprocessTexture()
        {
            var s = LucentSettings.instance;
            if (!s.autoApplyOnImport) return;

            var importer = assetImporter as TextureImporter;
            if (importer == null) return;
            if (!importer.importSettingsMissing) return;          // never touch an existing .meta
            if (s.IsExcluded(assetPath)) return;
            if (assetPath.Contains("/Editor/") || assetPath.Contains("/Editor Default Resources/")) return;

            string lower = Path.GetFileNameWithoutExtension(assetPath).ToLowerInvariant();
            bool isHdr = TextureRules.IsHdrSource(assetPath);
            bool isNormal = TextureRules.NameHas(lower, new[] { "normal", "_norm", "_nrm", "_n_", "-normal", "_bump", "normalmap" });
            bool isData = TextureRules.NameHas(lower, new[] { "mask", "metal", "rough", "smooth", "gloss", "specular", "_ao", "occlusion", "_orm", "_rma", "detail", "height", "_disp", "noise", "_lut", "_flow" });

            // Pixels are not available at OnPreprocessTexture time, so this pass is deliberately
            // conservative: correct resolution, correct colour space, correct format family.
            // The Lucent window's Deep Scan refines it afterwards with real pixel data.
            importer.mipmapEnabled = true;
            importer.streamingMipmaps = false;
            importer.npotScale = TextureImporterNPOTScale.ToNearest;
            importer.anisoLevel = s.anisoLevel;
            importer.filterMode = FilterMode.Trilinear;
            importer.maxTextureSize = s.defaultMaxSize;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.compressionQuality = 100;
            importer.crunchedCompression = false;

            TextureImporterFormat fmt;
            if (isHdr)
            {
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = false;
                importer.alphaSource = TextureImporterAlphaSource.None;
                fmt = TextureImporterFormat.BC6H;
            }
            else if (isNormal)
            {
                importer.textureType = TextureImporterType.NormalMap;
                importer.sRGBTexture = false;
                importer.alphaSource = TextureImporterAlphaSource.None;
                fmt = s.bc5ForNormals ? TextureImporterFormat.BC5 : TextureImporterFormat.DXT5;
            }
            else if (isData)
            {
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = false;
                importer.anisoLevel = Mathf.Min(s.anisoLevel, 2);
                fmt = s.preferBC7 ? TextureImporterFormat.BC7 : TextureImporterFormat.DXT5;
            }
            else
            {
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = true;
                importer.alphaIsTransparency = true;
                fmt = s.preferBC7 ? TextureImporterFormat.BC7 : TextureImporterFormat.DXT5;
            }

            var ps = importer.GetPlatformTextureSettings(LucentPlatforms.Standalone);
            ps.overridden = true;
            ps.maxTextureSize = s.defaultMaxSize;
            ps.format = fmt;
            ps.textureCompression = TextureImporterCompression.CompressedHQ;
            ps.compressionQuality = 100;
            ps.crunchedCompression = false;
            ps.resizeAlgorithm = TextureResizeAlgorithm.Mitchell;
            importer.SetPlatformTextureSettings(ps);
        }
    }

    public static class LucentPlatforms
    {
        public const string Standalone = "Standalone";
        public const string Android = "Android";
    }
}
