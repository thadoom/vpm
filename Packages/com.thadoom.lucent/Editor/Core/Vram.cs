using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;

namespace Lucent
{
    /// <summary>
    /// VRAM math. Two sources of truth: the real runtime size Unity reports for a loaded texture,
    /// and a "what would it cost if I changed the format/size" estimator used by the fix preview.
    /// </summary>
    public static class Vram
    {
        public static long RuntimeSize(Texture t)
        {
            if (t == null) return 0;
            return Profiler.GetRuntimeMemorySizeLong(t);
        }

        /// <summary>Bits per pixel for the GPU formats that actually show up in a VRChat project.</summary>
        public static float BitsPerPixel(TextureFormat f)
        {
            switch (f)
            {
                case TextureFormat.DXT1:
                case TextureFormat.DXT1Crunched:
                case TextureFormat.BC4:
                case TextureFormat.ETC_RGB4:
                case TextureFormat.ETC2_RGB:
                    return 4f;

                case TextureFormat.DXT5:
                case TextureFormat.DXT5Crunched:
                case TextureFormat.BC5:
                case TextureFormat.BC6H:
                case TextureFormat.BC7:
                case TextureFormat.ETC2_RGBA8:
                    return 8f;

                case TextureFormat.R8:
                case TextureFormat.Alpha8:
                    return 8f;

                case TextureFormat.R16:
                case TextureFormat.RHalf:
                case TextureFormat.RG16:
                case TextureFormat.RGB565:
                case TextureFormat.RGBA4444:
                case TextureFormat.ARGB4444:
                    return 16f;

                case TextureFormat.RGB24:
                    return 24f;

                case TextureFormat.RGBA32:
                case TextureFormat.ARGB32:
                case TextureFormat.BGRA32:
                case TextureFormat.RGHalf:
                case TextureFormat.RFloat:
                case TextureFormat.RGB9e5Float:
                    return 32f;

                case TextureFormat.RGBAHalf:
                case TextureFormat.RGFloat:
                    return 64f;

                case TextureFormat.RGBAFloat:
                    return 128f;

                default:
                    return 32f;
            }
        }

        public static float BitsPerPixel(TextureImporterFormat f)
        {
            switch (f)
            {
                case TextureImporterFormat.DXT1:
                case TextureImporterFormat.DXT1Crunched:
                case TextureImporterFormat.BC4:
                    return 4f;

                case TextureImporterFormat.DXT5:
                case TextureImporterFormat.DXT5Crunched:
                case TextureImporterFormat.BC5:
                case TextureImporterFormat.BC6H:
                case TextureImporterFormat.BC7:
                    return 8f;

                case TextureImporterFormat.R8:
                case TextureImporterFormat.Alpha8:
                    return 8f;

                case TextureImporterFormat.R16:
                case TextureImporterFormat.RHalf:
                case TextureImporterFormat.RG16:
                case TextureImporterFormat.RGB16:
                case TextureImporterFormat.RGBA16:
                    return 16f;

                case TextureImporterFormat.RGB24:
                    return 24f;

                case TextureImporterFormat.RGBA32:
                case TextureImporterFormat.ARGB32:
                case TextureImporterFormat.BGRA32:
                case TextureImporterFormat.RGHalf:
                case TextureImporterFormat.RFloat:
                    return 32f;

                case TextureImporterFormat.RGBAHalf:
                case TextureImporterFormat.RGFloat:
                    return 64f;

                case TextureImporterFormat.RGBAFloat:
                    return 128f;

                default:
                    return 32f;
            }
        }

        /// <summary>Predicted VRAM for a texture at a given resolution / format / mip setting.</summary>
        public static long Estimate(int width, int height, TextureImporterFormat format, bool mipmaps)
        {
            float bpp = BitsPerPixel(format);
            double bytes = (double)width * height * bpp / 8.0;
            if (mipmaps) bytes *= 4.0 / 3.0;
            return (long)bytes;
        }

        /// <summary>Clamp a texture's real dimensions to a Max Size the way Unity's importer does.</summary>
        public static void ApplyMaxSize(int srcW, int srcH, int maxSize, out int w, out int h)
        {
            w = srcW; h = srcH;
            int longest = Mathf.Max(srcW, srcH);
            if (longest <= maxSize || longest == 0) return;
            float scale = (float)maxSize / longest;
            w = Mathf.Max(1, Mathf.RoundToInt(srcW * scale));
            h = Mathf.Max(1, Mathf.RoundToInt(srcH * scale));
        }

        public static string Format(long bytes)
        {
            if (bytes >= 1024L * 1024L * 1024L) return (bytes / 1073741824.0).ToString("0.00") + " GB";
            if (bytes >= 1024L * 1024L) return (bytes / 1048576.0).ToString("0.0") + " MB";
            if (bytes >= 1024L) return (bytes / 1024.0).ToString("0.0") + " KB";
            return bytes + " B";
        }

        // VRChat PC performance rank thresholds, in bytes.
        public const long RankExcellent = 40L * 1024 * 1024;
        public const long RankGood = 75L * 1024 * 1024;
        public const long RankMedium = 110L * 1024 * 1024;
        public const long RankPoor = 150L * 1024 * 1024;

        public static string RankForVram(long bytes)
        {
            if (bytes < RankExcellent) return "Excellent";
            if (bytes < RankGood) return "Good";
            if (bytes < RankMedium) return "Medium";
            if (bytes < RankPoor) return "Poor";
            return "Very Poor";
        }

        public static Color RankColor(string rank)
        {
            switch (rank)
            {
                case "Excellent": return new Color(0.25f, 0.85f, 0.60f);
                case "Good": return new Color(0.45f, 0.80f, 0.35f);
                case "Medium": return new Color(0.95f, 0.78f, 0.25f);
                case "Poor": return new Color(0.95f, 0.50f, 0.20f);
                default: return new Color(0.90f, 0.30f, 0.30f);
            }
        }
    }
}
