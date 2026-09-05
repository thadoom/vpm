using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Lucent
{
    public enum Severity { Ok = 0, Note = 1, Warning = 2, Critical = 3 }

    public class Issue
    {
        public Severity severity;
        public string message;
        public Issue(Severity s, string m) { severity = s; message = m; }
    }

    public class TextureReport
    {
        public Texture texture;
        public string assetPath;
        public TextureImporter importer;

        public int width, height;
        public TextureFormat currentFormat;
        public long currentVram;
        public bool mipmaps;
        public bool sRGB;
        public int aniso;
        public bool crunched;
        public int currentMaxSize;

        public TexRole role = TexRole.Unknown;
        public PixelFacts facts;

        public bool deepScanned;
        public float halfResPsnr;          // >0 means measured
        public int suggestedMaxSize;       // resolution Lucent is willing to accept
        public bool downscaleIsFree;       // measured as visually lossless

        public TexTarget target;
        public bool hasTarget;
        public long targetVram;
        public string reason = "";

        public readonly List<Issue> issues = new List<Issue>();
        public readonly HashSet<string> usedByMaterials = new HashSet<string>();
        public bool selected = true;

        /// <summary>Cached in Evaluate() — recomputing it every repaint would query the importer
        /// once per row per frame and make the window crawl on a heavy avatar.</summary>
        public bool settingsDiffer;

        public long Saving => Math.Max(0L, currentVram - targetVram);
        public bool NeedsChange => hasTarget && (targetVram != currentVram || settingsDiffer);

        public Severity Worst
        {
            get
            {
                var w = Severity.Ok;
                foreach (var i in issues) if (i.severity > w) w = i.severity;
                return w;
            }
        }

        public bool ImportSettingsDiffer()
        {
            if (importer == null || !hasTarget) return false;
            var ps = importer.GetPlatformTextureSettings(LucentPlatforms.Standalone);
            if (!ps.overridden) return true;
            if (ps.format != target.format) return true;
            if (ps.maxTextureSize != target.maxSize) return true;
            if (ps.crunchedCompression != target.crunched) return true;
            if (importer.sRGBTexture != target.sRGB) return true;
            if (importer.mipmapEnabled != target.mipmaps) return true;
            if (importer.anisoLevel != target.aniso) return true;
            if (importer.alphaSource != target.alphaSource) return true;
            if (importer.streamingMipmaps != target.streamingMipmaps) return true;
            return false;
        }
    }

    public class MeshReport
    {
        public Mesh mesh;
        public string assetPath;
        public ModelImporter importer;
        public Renderer renderer;

        public int triangles;
        public int vertices;
        public int subMeshes;
        public int blendShapes;
        public int bones;
        public int uvChannels;
        public bool hasColors;
        public bool isReadable;
        public bool isSkinned;
        public long meshVram;
        public long blendShapeVram;

        public readonly List<Issue> issues = new List<Issue>();
        public bool selected = true;

        public Severity Worst
        {
            get
            {
                var w = Severity.Ok;
                foreach (var i in issues) if (i.severity > w) w = i.severity;
                return w;
            }
        }
    }

    public class MaterialReport
    {
        public Material material;
        public string shaderName;
        public int renderQueue;
        public bool isTransparent;
        public int textureSlotsAssigned;
        public readonly List<Issue> issues = new List<Issue>();
    }

    public class ScanResult
    {
        public GameObject root;
        public readonly List<TextureReport> textures = new List<TextureReport>();
        public readonly List<MeshReport> meshes = new List<MeshReport>();
        public readonly List<MaterialReport> materials = new List<MaterialReport>();

        public long TotalVram;
        public long ProjectedVram;
        public int TotalTriangles;
        public int SkinnedMeshCount;
        public int MeshCount;
        public int MaterialSlots;
        public int BoneCount;
        public int PhysBoneCount;
        public int ParticleSystemCount;

        public string CurrentRank => Vram.RankForVram(TotalVram);
        public string ProjectedRank => Vram.RankForVram(ProjectedVram);
        public long Saving => Math.Max(0L, TotalVram - ProjectedVram);
    }
}
