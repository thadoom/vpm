using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Lucent
{
    /// <summary>
    /// How aggressive Lucent is allowed to be. "Maximum" never trades visible quality for VRAM.
    /// </summary>
    public enum QualityMode
    {
        /// <summary>BC7 everywhere it helps, no crunch, no resolution cuts unless provably invisible.</summary>
        Maximum = 0,
        /// <summary>Same rules, but opaque low-detail colour maps drop to DXT1 (4bpp) and the
        /// "provably invisible" threshold is slightly looser.</summary>
        Balanced = 1
    }

    [FilePath("ProjectSettings/LucentSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    public class LucentSettings : ScriptableSingleton<LucentSettings>
    {
        // ---------- Import automation ----------

        [Tooltip("When ON, any NEWLY imported texture gets Lucent's defaults instead of Unity's 2048 / DXT ones. Textures that already have a .meta file are never touched.")]
        public bool autoApplyOnImport = true;

        [Tooltip("Max Size written on newly imported textures. This is the setting that stops Unity from silently resampling your 4K maps down to 2K.")]
        public int defaultMaxSize = 4096;

        [Tooltip("Never raise Max Size above the texture's real source resolution. Recommended: leave ON.")]
        public bool clampToSourceResolution = true;

        [Tooltip("Folders (relative to Assets/) that the import automation must never touch. One per line.")]
        public List<string> excludedFolders = new List<string>
        {
            "Assets/Plugins",
            "Assets/VRChat Examples"
        };

        // ---------- Quality policy ----------

        public QualityMode quality = QualityMode.Maximum;

        [Tooltip("Use BC7 (8bpp) for colour maps. Same VRAM as DXT5 but dramatically fewer gradient/hue artefacts. This is the single biggest free quality win in Unity.")]
        public bool preferBC7 = true;

        [Tooltip("Use BC5 (8bpp, 2-channel) for normal maps instead of Unity's DXT5nm. Sharper, no swimming.")]
        public bool bc5ForNormals = true;

        [Tooltip("Use BC4 (4bpp, 1-channel) for textures that only carry one channel of data. Halves VRAM with zero visible loss.")]
        public bool bc4ForSingleChannel = true;

        [Tooltip("Strip the alpha channel when it is fully opaque. Lets an 8bpp texture become a 4bpp one for free.")]
        public bool stripUselessAlpha = true;

        [Tooltip("Crunch compression shrinks the DOWNLOAD, not the VRAM, and it is lossy on top of BC. It also blocks d4rkAvatarOptimizer's texture merging. Keep OFF unless upload size is your bottleneck.")]
        public bool crunchCompression = false;

        [Range(0, 100)]
        public int crunchQuality = 100;

        [Tooltip("Anisotropic level written on colour/normal maps. 4 is the sweet spot for avatars; 8 for floors and long surfaces in worlds.")]
        [Range(1, 16)]
        public int anisoLevel = 4;

        [Tooltip("Mip maps must stay ON. Turning them off looks worse AND runs slower (texture cache thrash). Lucent will flag any texture that has them disabled.")]
        public bool enforceMipmaps = true;

        // ---------- Deep scan ----------

        [Tooltip("PSNR (dB) above which halving a texture's resolution is considered visually lossless. Higher = more conservative. 48 dB is 'nobody will ever see it'.")]
        [Range(34f, 60f)]
        public float downscalePsnrThreshold = 48f;

        [Tooltip("Deep scan reads every texture back from the GPU at full resolution. Textures wider than this are skipped to keep the scan responsive.")]
        public int deepScanMaxSide = 4096;

        // ---------- Mesh policy ----------

        [Tooltip("Read/Write Enabled doubles a mesh's memory cost (one copy on GPU, one on CPU). VRChat does not need it at runtime.")]
        public bool disableMeshReadWrite = true;

        [Tooltip("Unity's Mesh Compression is lossy vertex quantization that only shrinks the file on disk, not runtime memory. Off = correct for quality-first.")]
        public bool disableMeshCompression = true;

        [Tooltip("Blend shape normals cost 3x the memory of positions alone. Set to None unless your shapes genuinely reshade (most avatar visemes do not).")]
        public bool stripBlendShapeNormals = false;

        [Tooltip("Turn off Skinned Motion Vectors on every SkinnedMeshRenderer. Free frame time, no visual difference in VRChat.")]
        public bool disableSkinnedMotionVectors = true;

        [Tooltip("Turn off 'Update When Offscreen'. It forces a full skinning pass every frame even when nobody can see the mesh.")]
        public bool disableUpdateWhenOffscreen = true;

        // ---------- Project settings ----------

        [Tooltip("Force Global Mipmap Limit = Full Resolution on every quality level. THIS is what makes 4K textures render as 2K in the editor and in play mode.")]
        public bool forceFullMipmapLimit = true;

        [Tooltip("Turn off Mipmap Streaming. VRChat avatars are not streamed; leaving it on just makes textures pop in blurry.")]
        public bool disableMipmapStreaming = true;

        public void Save() => Save(true);

        public bool IsExcluded(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return false;
            foreach (var f in excludedFolders)
            {
                if (string.IsNullOrWhiteSpace(f)) continue;
                var norm = f.Replace('\\', '/').TrimEnd('/');
                if (assetPath.StartsWith(norm + "/", StringComparison.OrdinalIgnoreCase) ||
                    assetPath.Equals(norm, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}
