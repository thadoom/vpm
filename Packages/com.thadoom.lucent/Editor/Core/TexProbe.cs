using System;
using UnityEngine;

namespace Lucent
{
    /// <summary>
    /// Facts read straight off a texture's pixels. This is what lets Lucent make decisions that
    /// filename heuristics cannot: is the alpha channel actually doing anything, is this really
    /// one channel of data wearing an RGBA costume, and does the 4K version carry any detail that
    /// the 2K version does not.
    /// </summary>
    public struct PixelFacts
    {
        public bool valid;
        public bool alphaIsOpaque;      // every alpha sample is 1
        public bool alphaIsBinary;      // alpha only ever 0 or 1 -> cutout, DXT1 is enough
        public bool isGrayscale;        // R == G == B everywhere
        public bool isSingleChannel;    // grayscale AND opaque -> BC4 candidate
        public bool isFlatColor;        // no meaningful variance at all -> the texture is a swatch
        public bool looksLikeNormalMap; // blue-dominant, roughly unit length
        public float luminanceVariance;
    }

    public static class TexProbe
    {
        /// <summary>
        /// Blit any texture (compressed, non-readable, whatever) into a readable RGBA32 copy.
        /// Caller owns the result and must DestroyImmediate it.
        /// </summary>
        public static Texture2D Readback(Texture src, int width, int height, bool linear = true)
        {
            if (src == null || width <= 0 || height <= 0) return null;

            var fmt = RenderTextureFormat.ARGB32;
            var rw = linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB;
            var rt = RenderTexture.GetTemporary(width, height, 0, fmt, rw);
            rt.filterMode = FilterMode.Bilinear;

            var prevActive = RenderTexture.active;
            var prevFilter = src.filterMode;
            src.filterMode = FilterMode.Bilinear;

            Texture2D result = null;
            try
            {
                Graphics.Blit(src, rt);
                RenderTexture.active = rt;
                result = new Texture2D(width, height, TextureFormat.RGBA32, false, linear);
                result.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                result.Apply(false, false);
            }
            catch (Exception)
            {
                if (result != null) UnityEngine.Object.DestroyImmediate(result);
                result = null;
            }
            finally
            {
                RenderTexture.active = prevActive;
                RenderTexture.ReleaseTemporary(rt);
                src.filterMode = prevFilter;
            }
            return result;
        }

        /// <summary>Cheap pass: 256x256 sample is plenty to answer the yes/no questions.</summary>
        public static PixelFacts Analyze(Texture src, int sampleSide = 256)
        {
            var facts = new PixelFacts();
            if (src == null) return facts;

            int side = Mathf.Min(sampleSide, Mathf.Max(1, Mathf.Max(src.width, src.height)));
            side = Mathf.Max(8, Mathf.NextPowerOfTwo(side) / 2 * 2);

            int w = Mathf.Max(1, Mathf.RoundToInt(side * Mathf.Min(1f, (float)src.width / Mathf.Max(src.width, src.height))));
            int h = Mathf.Max(1, Mathf.RoundToInt(side * Mathf.Min(1f, (float)src.height / Mathf.Max(src.width, src.height))));

            var tex = Readback(src, w, h);
            if (tex == null) return facts;

            try
            {
                var px = tex.GetPixels32();
                if (px == null || px.Length == 0) return facts;

                bool opaque = true, binaryAlpha = true, gray = true;
                double sum = 0.0, sumSq = 0.0;
                double nx = 0, ny = 0, nz = 0;

                for (int i = 0; i < px.Length; i++)
                {
                    var c = px[i];
                    if (c.a < 250) opaque = false;
                    if (c.a > 8 && c.a < 247) binaryAlpha = false;
                    if (Mathf.Abs(c.r - c.g) > 2 || Mathf.Abs(c.g - c.b) > 2) gray = false;

                    double lum = (0.2126 * c.r + 0.7152 * c.g + 0.0722 * c.b) / 255.0;
                    sum += lum;
                    sumSq += lum * lum;

                    nx += c.r / 255.0;
                    ny += c.g / 255.0;
                    nz += c.b / 255.0;
                }

                int n = px.Length;
                double mean = sum / n;
                double variance = Math.Max(0.0, sumSq / n - mean * mean);

                facts.valid = true;
                facts.alphaIsOpaque = opaque;
                facts.alphaIsBinary = binaryAlpha && !opaque;
                facts.isGrayscale = gray;
                facts.isSingleChannel = gray && opaque;
                facts.luminanceVariance = (float)variance;
                facts.isFlatColor = variance < 0.00002;

                double bAvg = nz / n, rAvg = nx / n, gAvg = ny / n;
                facts.looksLikeNormalMap = bAvg > 0.70 && bAvg > rAvg + 0.12 && bAvg > gAvg + 0.12
                                           && Math.Abs(rAvg - 0.5) < 0.14 && Math.Abs(gAvg - 0.5) < 0.14;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(tex);
            }

            return facts;
        }

        /// <summary>
        /// The expensive, honest test: does halving this texture lose anything a human could see?
        /// Renders the texture at full res, then renders a half-res version back up to full res,
        /// and measures PSNR between the two. Returns dB; higher means "the half-res one is identical".
        /// </summary>
        public static float HalfResPsnr(Texture src, int maxSide)
        {
            if (src == null) return 0f;
            int w = src.width, h = src.height;
            if (w < 64 || h < 64) return 0f;
            if (Mathf.Max(w, h) > maxSide) return -1f; // caller decided this is too big to test

            Texture2D reference = null, half = null, upscaled = null;
            RenderTexture rtHalf = null, rtUp = null;

            try
            {
                reference = Readback(src, w, h);
                if (reference == null) return 0f;

                int hw = Mathf.Max(1, w / 2), hh = Mathf.Max(1, h / 2);

                rtHalf = RenderTexture.GetTemporary(hw, hh, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                rtHalf.filterMode = FilterMode.Bilinear;
                Graphics.Blit(src, rtHalf);

                rtUp = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                rtUp.filterMode = FilterMode.Bilinear;
                rtHalf.filterMode = FilterMode.Bilinear;
                Graphics.Blit(rtHalf, rtUp);

                var prev = RenderTexture.active;
                RenderTexture.active = rtUp;
                upscaled = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
                upscaled.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
                upscaled.Apply(false, false);
                RenderTexture.active = prev;

                var a = reference.GetPixels32();
                var b = upscaled.GetPixels32();
                if (a.Length != b.Length || a.Length == 0) return 0f;

                double mse = 0.0;
                for (int i = 0; i < a.Length; i++)
                {
                    double dr = a[i].r - b[i].r;
                    double dg = a[i].g - b[i].g;
                    double db = a[i].b - b[i].b;
                    double da = a[i].a - b[i].a;
                    mse += dr * dr + dg * dg + db * db + da * da;
                }
                mse /= (a.Length * 4.0);

                if (mse <= 1e-9) return 99f;
                return (float)(10.0 * Math.Log10((255.0 * 255.0) / mse));
            }
            catch (Exception)
            {
                return 0f;
            }
            finally
            {
                if (rtHalf != null) RenderTexture.ReleaseTemporary(rtHalf);
                if (rtUp != null) RenderTexture.ReleaseTemporary(rtUp);
                if (reference != null) UnityEngine.Object.DestroyImmediate(reference);
                if (half != null) UnityEngine.Object.DestroyImmediate(half);
                if (upscaled != null) UnityEngine.Object.DestroyImmediate(upscaled);
            }
        }
    }
}
