using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Lucent
{
    public static class TextureFixer
    {
        /// <summary>Write the decided target onto the importer and reimport. Batched.</summary>
        public static int Apply(IEnumerable<TextureReport> reports)
        {
            var list = new List<TextureReport>();
            foreach (var r in reports)
                if (r != null && r.hasTarget && r.importer != null) list.Add(r);

            if (list.Count == 0) return 0;

            int changed = 0;
            try
            {
                AssetDatabase.StartAssetEditing();
                for (int i = 0; i < list.Count; i++)
                {
                    var r = list[i];
                    if (EditorUtility.DisplayCancelableProgressBar("Lucent",
                            $"Reimporting {System.IO.Path.GetFileName(r.assetPath)}", (float)i / list.Count))
                        break;
                    if (ApplyOne(r)) changed++;
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                EditorUtility.ClearProgressBar();
                AssetDatabase.Refresh();
            }
            return changed;
        }

        public static bool ApplyOne(TextureReport r)
        {
            var imp = r.importer;
            if (imp == null) return false;
            var t = r.target;

            Undo.RegisterImportedObjectUndo(r.texture, "Lucent texture fix");

            imp.textureType = t.type;
            if (t.type != TextureImporterType.NormalMap)
                imp.sRGBTexture = t.sRGB;

            imp.mipmapEnabled = t.mipmaps;
            imp.streamingMipmaps = t.streamingMipmaps;
            imp.anisoLevel = Mathf.Clamp(t.aniso, 1, 16);
            imp.filterMode = FilterMode.Trilinear;
            imp.npotScale = t.npotScale;
            imp.alphaSource = t.alphaSource;
            imp.alphaIsTransparency = t.alphaIsTransparency;
            imp.maxTextureSize = t.maxSize;
            imp.textureCompression = TextureImporterCompression.CompressedHQ;
            imp.compressionQuality = t.compressionQuality;
            imp.crunchedCompression = t.crunched;
            imp.mipmapFilter = TextureImporterMipFilter.KaiserFilter;

            var ps = imp.GetPlatformTextureSettings(LucentPlatforms.Standalone);
            ps.overridden = true;
            ps.name = LucentPlatforms.Standalone;
            ps.maxTextureSize = t.maxSize;
            ps.format = t.format;
            ps.textureCompression = TextureImporterCompression.CompressedHQ;
            ps.compressionQuality = t.compressionQuality;
            ps.crunchedCompression = t.crunched;
            ps.resizeAlgorithm = TextureResizeAlgorithm.Mitchell;
            imp.SetPlatformTextureSettings(ps);

            EditorUtility.SetDirty(imp);
            imp.SaveAndReimport();
            return true;
        }
    }

    public static class MeshFixer
    {
        public static int Apply(IEnumerable<MeshReport> reports, LucentSettings s)
        {
            var models = new Dictionary<string, ModelImporter>();
            var renderers = new List<SkinnedMeshRenderer>();

            foreach (var r in reports)
            {
                if (r == null) continue;
                if (r.importer != null && !models.ContainsKey(r.assetPath)) models[r.assetPath] = r.importer;
                if (r.renderer is SkinnedMeshRenderer smr) renderers.Add(smr);
            }

            int changed = 0;

            foreach (var smr in renderers)
            {
                bool dirty = false;
                if (s.disableSkinnedMotionVectors && smr.skinnedMotionVectors)
                {
                    Undo.RecordObject(smr, "Lucent mesh fix");
                    smr.skinnedMotionVectors = false; dirty = true;
                }
                if (s.disableUpdateWhenOffscreen && smr.updateWhenOffscreen)
                {
                    Undo.RecordObject(smr, "Lucent mesh fix");
                    smr.updateWhenOffscreen = false; dirty = true;
                }
                if (dirty) { EditorUtility.SetDirty(smr); changed++; }
            }

            if (models.Count > 0)
            {
                try
                {
                    AssetDatabase.StartAssetEditing();
                    foreach (var kv in models)
                    {
                        var imp = kv.Value;
                        bool dirty = false;

                        if (s.disableMeshReadWrite && imp.isReadable) { imp.isReadable = false; dirty = true; }
                        if (s.disableMeshCompression && imp.meshCompression != ModelImporterMeshCompression.Off)
                        { imp.meshCompression = ModelImporterMeshCompression.Off; dirty = true; }
                        if (!imp.optimizeMeshVertices) { imp.optimizeMeshVertices = true; dirty = true; }
                        if (!imp.optimizeMeshPolygons) { imp.optimizeMeshPolygons = true; dirty = true; }
                        if (!imp.weldVertices) { imp.weldVertices = true; dirty = true; }
                        if (imp.importCameras) { imp.importCameras = false; dirty = true; }
                        if (imp.importLights) { imp.importLights = false; dirty = true; }
                        if (s.stripBlendShapeNormals && imp.importBlendShapeNormals != ModelImporterNormals.None)
                        { imp.importBlendShapeNormals = ModelImporterNormals.None; dirty = true; }

                        if (dirty) { imp.SaveAndReimport(); changed++; }
                    }
                }
                finally
                {
                    AssetDatabase.StopAssetEditing();
                    AssetDatabase.Refresh();
                }
            }

            return changed;
        }
    }

    public static class ProjectFixer
    {
        public struct Check
        {
            public string label;
            public string current;
            public string recommended;
            public bool ok;
        }

        public static List<Check> Inspect()
        {
            var list = new List<Check>();

            int worstMip = 0;
            bool anyStreaming = false;
            int levels = QualitySettings.names.Length;
            int active = QualitySettings.GetQualityLevel();

            for (int i = 0; i < levels; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
#if UNITY_2022_2_OR_NEWER
                worstMip = Mathf.Max(worstMip, QualitySettings.globalTextureMipmapLimit);
#else
                worstMip = Mathf.Max(worstMip, QualitySettings.masterTextureLimit);
#endif
                if (QualitySettings.streamingMipmapsActive) anyStreaming = true;
            }
            QualitySettings.SetQualityLevel(active, false);

            list.Add(new Check
            {
                label = "Global Mipmap Limit (all quality levels)",
                current = worstMip == 0 ? "Full Resolution" : (worstMip == 1 ? "Half Resolution" : worstMip == 2 ? "Quarter" : "Eighth"),
                recommended = "Full Resolution",
                ok = worstMip == 0
            });

            list.Add(new Check
            {
                label = "Mipmap Streaming",
                current = anyStreaming ? "Enabled" : "Disabled",
                recommended = "Disabled",
                ok = !anyStreaming
            });

            list.Add(new Check
            {
                label = "Anisotropic Filtering",
                current = QualitySettings.anisotropicFiltering.ToString(),
                recommended = "Per Texture",
                ok = QualitySettings.anisotropicFiltering == AnisotropicFiltering.Enable
            });

            list.Add(new Check
            {
                label = "Color Space",
                current = PlayerSettings.colorSpace.ToString(),
                recommended = "Linear (matches how VRChat renders)",
                ok = PlayerSettings.colorSpace == ColorSpace.Linear
            });

            list.Add(new Check
            {
                label = "Lucent import automation",
                current = LucentSettings.instance.autoApplyOnImport
                    ? $"On — new textures import at {LucentSettings.instance.defaultMaxSize}px"
                    : "Off — Unity's 2048 default applies",
                recommended = "On, 4096",
                ok = LucentSettings.instance.autoApplyOnImport && LucentSettings.instance.defaultMaxSize >= 4096
            });

            return list;
        }

        public static void ApplyAll()
        {
            var s = LucentSettings.instance;
            int active = QualitySettings.GetQualityLevel();
            int levels = QualitySettings.names.Length;

            for (int i = 0; i < levels; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                if (s.forceFullMipmapLimit)
                {
#if UNITY_2022_2_OR_NEWER
                    QualitySettings.globalTextureMipmapLimit = 0;
#else
                    QualitySettings.masterTextureLimit = 0;
#endif
                }
                if (s.disableMipmapStreaming) QualitySettings.streamingMipmapsActive = false;
                QualitySettings.anisotropicFiltering = AnisotropicFiltering.Enable; // "Per Texture"
            }
            QualitySettings.SetQualityLevel(active, false);

            s.autoApplyOnImport = true;
            if (s.defaultMaxSize < 4096) s.defaultMaxSize = 4096;
            s.Save();

            AssetDatabase.SaveAssets();
            Debug.Log("[Lucent] Project settings updated: Global Mipmap Limit = Full Resolution on all quality levels, mipmap streaming off, anisotropic filtering per-texture, import automation on.");
        }
    }
}
