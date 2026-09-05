using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Lucent
{
    public enum TexRole
    {
        Color,          // albedo / emission / matcap — everything the eye reads as a picture
        NormalMap,
        PackedMask,     // metallic/smoothness/AO/detail packed across RGB(A)
        SingleChannel,  // one channel of data, wearing an RGBA costume
        HDR,            // .hdr / .exr / float source
        Unknown
    }

    /// <summary>The import settings Lucent wants a given texture to have, and why.</summary>
    public struct TexTarget
    {
        public TextureImporterType type;
        public TextureImporterFormat format;
        public int maxSize;
        public bool sRGB;
        public bool mipmaps;
        public int aniso;
        public TextureImporterAlphaSource alphaSource;
        public bool alphaIsTransparency;
        public bool crunched;
        public int crunchQuality;
        public int compressionQuality;   // 100 = "Best" for BC7/BC6H
        public bool streamingMipmaps;
        public TextureImporterNPOTScale npotScale;
    }

    public static class TextureRules
    {
        static readonly string[] NormalHints = { "normal", "_norm", "_nrm", "_n_", "-normal", "nrmmap", "_bump", "normalmap" };
        static readonly string[] MaskHints = { "mask", "metal", "rough", "smooth", "gloss", "specular", "_ao", "occlusion", "_orm", "_rma", "_mras", "detail", "height", "_disp", "curvature", "thickness" };
        static readonly string[] LinearDataHints = { "noise", "_lut", "gradient_data", "flowmap", "_flow", "_id", "_idmap" };

        public static bool NameHas(string lower, string[] hints)
        {
            for (int i = 0; i < hints.Length; i++)
                if (lower.Contains(hints[i])) return true;
            return false;
        }

        public static bool IsHdrSource(string assetPath)
        {
            var ext = Path.GetExtension(assetPath).ToLowerInvariant();
            return ext == ".hdr" || ext == ".exr";
        }

        /// <summary>
        /// Classify using, in order of trust: the importer's declared type, the pixels, the filename.
        /// Pixels beat filenames — plenty of packs ship "Body_Normal.png" that is actually an albedo.
        /// </summary>
        public static TexRole Classify(string assetPath, TextureImporter importer, PixelFacts facts)
        {
            if (IsHdrSource(assetPath)) return TexRole.HDR;

            if (importer != null && importer.textureType == TextureImporterType.NormalMap)
                return TexRole.NormalMap;

            string lower = Path.GetFileNameWithoutExtension(assetPath).ToLowerInvariant();

            if (facts.valid && facts.looksLikeNormalMap) return TexRole.NormalMap;
            if (!facts.valid && NameHas(lower, NormalHints)) return TexRole.NormalMap;

            if (facts.valid && facts.isSingleChannel) return TexRole.SingleChannel;

            if (NameHas(lower, MaskHints)) return TexRole.PackedMask;
            if (NameHas(lower, LinearDataHints)) return TexRole.PackedMask;

            if (importer != null && !importer.sRGBTexture && facts.valid && !facts.isGrayscale)
                return TexRole.PackedMask;

            return TexRole.Color;
        }

        /// <summary>
        /// The heart of the tool. Given what the texture actually is, pick the best-looking format
        /// that fits its data — then, and only then, look for free VRAM.
        /// </summary>
        public static TexTarget Decide(string assetPath, TextureImporter importer, Texture tex,
                                       PixelFacts facts, TexRole role, LucentSettings s,
                                       int suggestedMaxSize)
        {
            var t = new TexTarget
            {
                type = TextureImporterType.Default,
                mipmaps = true,
                aniso = s.anisoLevel,
                alphaSource = TextureImporterAlphaSource.FromInput,
                alphaIsTransparency = false,
                crunched = s.crunchCompression,
                crunchQuality = s.crunchQuality,
                compressionQuality = 100,
                streamingMipmaps = false,
                npotScale = TextureImporterNPOTScale.ToNearest,
                sRGB = true,
                maxSize = suggestedMaxSize > 0 ? suggestedMaxSize : s.defaultMaxSize
            };

            bool hasUsefulAlpha = facts.valid ? !facts.alphaIsOpaque : true;
            bool binaryAlpha = facts.valid && facts.alphaIsBinary;

            switch (role)
            {
                case TexRole.NormalMap:
                    t.type = TextureImporterType.NormalMap;
                    t.sRGB = false;
                    t.alphaSource = TextureImporterAlphaSource.None;
                    t.format = s.bc5ForNormals ? TextureImporterFormat.BC5 : TextureImporterFormat.DXT5;
                    t.crunched = false; // BC5 has no crunched variant
                    break;

                case TexRole.HDR:
                    t.sRGB = false;
                    t.alphaSource = TextureImporterAlphaSource.None;
                    t.format = TextureImporterFormat.BC6H;
                    t.crunched = false;
                    t.aniso = Mathf.Min(t.aniso, 2);
                    break;

                case TexRole.SingleChannel:
                    t.sRGB = false;
                    t.alphaSource = TextureImporterAlphaSource.None;
                    t.format = s.bc4ForSingleChannel ? TextureImporterFormat.BC4 : TextureImporterFormat.DXT1;
                    t.crunched = false; // BC4 has no crunched variant
                    t.aniso = Mathf.Min(t.aniso, 2);
                    break;

                case TexRole.PackedMask:
                    // Channels carry independent data. DXT1 correlates R/G/B and destroys packed maps,
                    // so BC7 is the only compressed format that is honest here.
                    t.sRGB = false;
                    t.aniso = Mathf.Min(t.aniso, 2);
                    if (s.stripUselessAlpha && !hasUsefulAlpha)
                        t.alphaSource = TextureImporterAlphaSource.None;
                    t.format = s.preferBC7 ? TextureImporterFormat.BC7 : TextureImporterFormat.DXT5;
                    if (t.format == TextureImporterFormat.BC7) t.crunched = false;
                    break;

                default: // Color
                    t.sRGB = true;
                    if (s.stripUselessAlpha && !hasUsefulAlpha)
                    {
                        t.alphaSource = TextureImporterAlphaSource.None;
                        t.alphaIsTransparency = false;
                    }
                    else
                    {
                        t.alphaSource = TextureImporterAlphaSource.FromInput;
                        t.alphaIsTransparency = true;
                    }

                    if (!hasUsefulAlpha)
                    {
                        // Opaque colour: BC7 for max quality, DXT1 (4bpp) when Balanced and the
                        // texture is low-frequency enough that block artefacts cannot show.
                        bool lowDetail = facts.valid && facts.luminanceVariance < 0.012f;
                        if (s.quality == QualityMode.Balanced && lowDetail)
                            t.format = TextureImporterFormat.DXT1;
                        else
                            t.format = s.preferBC7 ? TextureImporterFormat.BC7 : TextureImporterFormat.DXT1;
                    }
                    else if (binaryAlpha && s.quality == QualityMode.Balanced)
                    {
                        // Cutout foliage / hair cards: 1-bit alpha is all the shader reads.
                        t.format = TextureImporterFormat.DXT5;
                    }
                    else
                    {
                        t.format = s.preferBC7 ? TextureImporterFormat.BC7 : TextureImporterFormat.DXT5;
                    }

                    if (t.format == TextureImporterFormat.BC7 || t.format == TextureImporterFormat.BC5 ||
                        t.format == TextureImporterFormat.BC4 || t.format == TextureImporterFormat.BC6H)
                        t.crunched = false;
                    break;
            }

            if (!s.enforceMipmaps && importer != null) t.mipmaps = importer.mipmapEnabled;
            if (s.disableMipmapStreaming) t.streamingMipmaps = false;

            // Never invent resolution the source file does not have.
            if (s.clampToSourceResolution && tex != null)
            {
                int srcLongest = Mathf.Max(tex.width, tex.height);
                if (srcLongest > 0)
                {
                    int pot = Mathf.NextPowerOfTwo(srcLongest);
                    t.maxSize = Mathf.Min(t.maxSize, Mathf.Max(32, pot));
                }
            }

            t.maxSize = ClampToUnityMaxSize(t.maxSize);
            return t;
        }

        public static int ClampToUnityMaxSize(int v)
        {
            int[] steps = { 32, 64, 128, 256, 512, 1024, 2048, 4096, 8192, 16384 };
            int best = steps[0];
            foreach (var s in steps) if (v >= s) best = s;
            return best;
        }

        /// <summary>Human-readable reason, shown in the window so you can disagree with the tool.</summary>
        public static string Explain(TexRole role, TexTarget t, PixelFacts facts)
        {
            switch (role)
            {
                case TexRole.NormalMap:
                    return t.format == TextureImporterFormat.BC5
                        ? "Normal map -> BC5 (2-channel, 8bpp). Sharper than DXT5nm at identical VRAM."
                        : "Normal map.";
                case TexRole.HDR:
                    return "HDR source -> BC6H, the only format that keeps values above 1.0.";
                case TexRole.SingleChannel:
                    return "One channel of data -> BC4 (4bpp). Half the VRAM of DXT5, zero visible loss.";
                case TexRole.PackedMask:
                    return facts.valid && facts.alphaIsOpaque
                        ? "Packed data map, alpha unused -> BC7, linear, alpha stripped."
                        : "Packed data map -> BC7, linear. DXT1 would correlate the channels and corrupt it.";
                default:
                    if (t.format == TextureImporterFormat.DXT1)
                        return "Opaque, low-frequency colour -> DXT1 (4bpp). Half the VRAM, no visible blocking.";
                    if (t.alphaSource == TextureImporterAlphaSource.None)
                        return "Colour map with a fully opaque alpha channel -> alpha stripped, BC7.";
                    return "Colour map -> BC7 at Best quality. Same 8bpp as DXT5, far fewer gradient artefacts.";
            }
        }
    }
}
