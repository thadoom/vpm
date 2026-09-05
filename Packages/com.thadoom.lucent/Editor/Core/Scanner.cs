using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Lucent
{
    /// <summary>
    /// Walks a GameObject hierarchy (an avatar root, or a world sub-tree) and builds the report
    /// the window renders. Deliberately has no VRChat SDK dependency: PhysBones and the avatar
    /// descriptor are found by type name so the package compiles in any project.
    /// </summary>
    public static class Scanner
    {
        public static ScanResult Scan(GameObject root, bool probePixels, Action<float, string> progress = null)
        {
            var result = new ScanResult { root = root };
            if (root == null) return result;

            var renderers = root.GetComponentsInChildren<Renderer>(true);
            var texByInstance = new Dictionary<int, TextureReport>();
            var meshSeen = new HashSet<int>();
            var matSeen = new Dictionary<int, MaterialReport>();
            var s = LucentSettings.instance;

            for (int r = 0; r < renderers.Length; r++)
            {
                var rend = renderers[r];
                progress?.Invoke((float)r / Mathf.Max(1, renderers.Length), "Scanning " + rend.name);

                var skinned = rend as SkinnedMeshRenderer;
                var filter = rend.GetComponent<MeshFilter>();
                Mesh mesh = skinned != null ? skinned.sharedMesh : (filter != null ? filter.sharedMesh : null);

                if (skinned != null) result.SkinnedMeshCount++;
                else if (filter != null && mesh != null) result.MeshCount++;

                result.MaterialSlots += rend.sharedMaterials.Length;

                if (mesh != null && meshSeen.Add(mesh.GetInstanceID()))
                {
                    var mr = BuildMeshReport(mesh, rend, skinned != null);
                    result.meshes.Add(mr);
                    result.TotalTriangles += mr.triangles;
                }

                AddRendererIssues(rend, skinned, result);

                foreach (var mat in rend.sharedMaterials)
                {
                    if (mat == null) continue;
                    if (!matSeen.ContainsKey(mat.GetInstanceID()))
                    {
                        var matRep = BuildMaterialReport(mat);
                        matSeen[mat.GetInstanceID()] = matRep;
                        result.materials.Add(matRep);
                    }
                    CollectTextures(mat, texByInstance, result);
                }
            }

            if (skinnedRootBones(root, out int boneCount)) result.BoneCount = boneCount;
            result.PhysBoneCount = CountByTypeName(root, "VRCPhysBone");
            result.ParticleSystemCount = root.GetComponentsInChildren<ParticleSystem>(true).Length;

            // Second pass: probe pixels and decide targets.
            int i = 0;
            foreach (var tr in result.textures)
            {
                i++;
                progress?.Invoke((float)i / Mathf.Max(1, result.textures.Count), "Analysing " + tr.texture.name);
                if (probePixels) tr.facts = TexProbe.Analyze(tr.texture);
                Evaluate(tr, s);
            }

            result.TotalVram = result.textures.Sum(t => t.currentVram);
            result.ProjectedVram = result.textures.Sum(t => t.hasTarget ? t.targetVram : t.currentVram);
            return result;
        }

        static bool skinnedRootBones(GameObject root, out int count)
        {
            var set = new HashSet<int>();
            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr.bones == null) continue;
                foreach (var b in smr.bones) if (b != null) set.Add(b.GetInstanceID());
            }
            count = set.Count;
            return true;
        }

        static int CountByTypeName(GameObject root, string typeName)
        {
            int n = 0;
            foreach (var c in root.GetComponentsInChildren<Component>(true))
            {
                if (c == null) continue;
                if (c.GetType().Name == typeName) n++;
            }
            return n;
        }

        static void CollectTextures(Material mat, Dictionary<int, TextureReport> map, ScanResult result)
        {
            var shader = mat.shader;
            if (shader == null) return;

            int count = ShaderUtil.GetPropertyCount(shader);
            for (int p = 0; p < count; p++)
            {
                if (ShaderUtil.GetPropertyType(shader, p) != ShaderUtil.ShaderPropertyType.TexEnv) continue;
                string name = ShaderUtil.GetPropertyName(shader, p);
                var tex = mat.GetTexture(name);
                if (tex == null) continue;
                if (!(tex is Texture2D) && !(tex is Cubemap) && !(tex is Texture2DArray)) continue;

                int id = tex.GetInstanceID();
                if (!map.TryGetValue(id, out var rep))
                {
                    rep = BuildTextureReport(tex);
                    if (rep == null) continue;
                    map[id] = rep;
                    result.textures.Add(rep);
                }
                rep.usedByMaterials.Add(mat.name);
            }
        }

        static TextureReport BuildTextureReport(Texture tex)
        {
            string path = AssetDatabase.GetAssetPath(tex);
            var rep = new TextureReport
            {
                texture = tex,
                assetPath = path,
                importer = string.IsNullOrEmpty(path) ? null : AssetImporter.GetAtPath(path) as TextureImporter,
                width = tex.width,
                height = tex.height,
                currentVram = Vram.RuntimeSize(tex),
                aniso = tex.anisoLevel
            };

            if (tex is Texture2D t2d)
            {
                rep.currentFormat = t2d.format;
                rep.mipmaps = t2d.mipmapCount > 1;
            }
            else
            {
                rep.mipmaps = true;
            }

            if (rep.importer != null)
            {
                rep.sRGB = rep.importer.sRGBTexture;
                rep.crunched = rep.importer.crunchedCompression;
                var ps = rep.importer.GetPlatformTextureSettings(LucentPlatforms.Standalone);
                rep.currentMaxSize = ps.overridden ? ps.maxTextureSize : rep.importer.maxTextureSize;
            }
            else
            {
                rep.currentMaxSize = Mathf.Max(tex.width, tex.height);
            }

            return rep;
        }

        /// <summary>Decide the target settings and raise the issues shown in the list.</summary>
        public static void Evaluate(TextureReport rep, LucentSettings s)
        {
            rep.issues.Clear();

            if (rep.importer == null)
            {
                rep.issues.Add(new Issue(Severity.Note,
                    "Not an imported asset (render texture, generated, or inside a package). Lucent cannot change it."));
                rep.hasTarget = false;
                rep.targetVram = rep.currentVram;
                return;
            }

            rep.role = TextureRules.Classify(rep.assetPath, rep.importer, rep.facts);

            // The headline issue: Unity is downscaling a high-res source.
            int srcW, srcH;
            GetSourceSize(rep.importer, out srcW, out srcH);
            int srcLongest = Mathf.Max(srcW, srcH);
            if (srcLongest > 0 && rep.currentMaxSize > 0 && srcLongest > rep.currentMaxSize)
            {
                rep.issues.Add(new Issue(Severity.Critical,
                    $"Source is {srcW}x{srcH} but Max Size is {rep.currentMaxSize} — Unity is throwing away {(1f - (float)rep.currentMaxSize / srcLongest) * 100f:0}% of the resolution you authored."));
            }

            if (!rep.mipmaps && s.enforceMipmaps)
                rep.issues.Add(new Issue(Severity.Warning,
                    "Mip maps are OFF. This looks worse at distance AND runs slower (texture cache misses). Turn them on."));

            if (rep.crunched)
                rep.issues.Add(new Issue(Severity.Warning,
                    "Crunch compression is ON. It does not save a single byte of VRAM, it is lossy on top of BC, and d4rkAvatarOptimizer refuses to merge crunched textures."));

            if (rep.role == TexRole.Color && !rep.sRGB)
                rep.issues.Add(new Issue(Severity.Warning, "Colour map imported as linear — it will render washed out / too dark."));
            if (rep.role != TexRole.Color && rep.role != TexRole.Unknown && rep.sRGB)
                rep.issues.Add(new Issue(Severity.Warning, "Data map imported as sRGB — the shader is reading gamma-curved numbers as if they were linear."));

            if (rep.facts.valid && rep.facts.isFlatColor && Mathf.Max(rep.width, rep.height) > 64)
                rep.issues.Add(new Issue(Severity.Critical,
                    $"This {rep.width}x{rep.height} texture is a flat colour. 32x32 would be pixel-identical."));

            if (rep.facts.valid && rep.facts.alphaIsOpaque && HasAlphaFormat(rep.currentFormat))
                rep.issues.Add(new Issue(Severity.Warning,
                    "Alpha channel is fully opaque but is still being stored. Stripping it halves the VRAM for free."));

            if (rep.facts.valid && rep.facts.isSingleChannel && Vram.BitsPerPixel(rep.currentFormat) > 4f)
                rep.issues.Add(new Issue(Severity.Warning,
                    "Only one channel carries data. BC4 stores it at 4bpp with no visible loss."));

            if (rep.currentFormat == TextureFormat.RGBA32 || rep.currentFormat == TextureFormat.ARGB32 ||
                rep.currentFormat == TextureFormat.RGB24)
                rep.issues.Add(new Issue(Severity.Critical,
                    "Uncompressed on the GPU — 4x the VRAM of BC7 for no visible gain."));

            if (rep.role == TexRole.NormalMap && rep.importer.textureType != TextureImporterType.NormalMap
                && rep.currentFormat != TextureFormat.BC5)
                rep.issues.Add(new Issue(Severity.Note, "Looks like a normal map but is not imported as one."));

            // Suggested resolution: only ever lowered by a measured deep scan.
            int suggested = rep.currentMaxSize > 0 ? rep.currentMaxSize : s.defaultMaxSize;
            if (srcLongest > 0) suggested = Mathf.Max(suggested, Mathf.Min(s.defaultMaxSize, Mathf.NextPowerOfTwo(srcLongest)));
            if (rep.facts.valid && rep.facts.isFlatColor) suggested = 32;
            else if (rep.deepScanned && rep.downscaleIsFree) suggested = Mathf.Max(32, TextureRules.ClampToUnityMaxSize(Mathf.Max(rep.width, rep.height) / 2));
            rep.suggestedMaxSize = suggested;

            rep.target = TextureRules.Decide(rep.assetPath, rep.importer, rep.texture, rep.facts, rep.role, s, suggested);
            rep.hasTarget = true;
            rep.reason = TextureRules.Explain(rep.role, rep.target, rep.facts);

            int tw, th;
            int baseW = srcW > 0 ? srcW : rep.width;
            int baseH = srcH > 0 ? srcH : rep.height;
            Vram.ApplyMaxSize(baseW, baseH, rep.target.maxSize, out tw, out th);
            rep.targetVram = Vram.Estimate(tw, th, rep.target.format, rep.target.mipmaps);
            rep.settingsDiffer = rep.ImportSettingsDiffer();
        }

        static bool HasAlphaFormat(TextureFormat f)
        {
            return f == TextureFormat.DXT5 || f == TextureFormat.DXT5Crunched || f == TextureFormat.BC7 ||
                   f == TextureFormat.RGBA32 || f == TextureFormat.ARGB32 || f == TextureFormat.RGBAHalf;
        }

        public static void GetSourceSize(TextureImporter importer, out int w, out int h)
        {
            w = 0; h = 0;
            if (importer == null) return;
            try
            {
                var m = typeof(TextureImporter).GetMethod("GetWidthAndHeight",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (m != null)
                {
                    object[] args = { 0, 0 };
                    m.Invoke(importer, args);
                    w = (int)args[0];
                    h = (int)args[1];
                }
            }
            catch { /* private API moved; fall back to the imported size */ }
        }

        static MeshReport BuildMeshReport(Mesh mesh, Renderer rend, bool skinned)
        {
            string path = AssetDatabase.GetAssetPath(mesh);
            var mr = new MeshReport
            {
                mesh = mesh,
                assetPath = path,
                importer = string.IsNullOrEmpty(path) ? null : AssetImporter.GetAtPath(path) as ModelImporter,
                renderer = rend,
                isSkinned = skinned,
                vertices = mesh.vertexCount,
                subMeshes = mesh.subMeshCount,
                blendShapes = mesh.blendShapeCount,
                isReadable = mesh.isReadable,
                meshVram = Vram.RuntimeSize(mesh)
            };

            try { mr.hasColors = mesh.colors32 != null && mesh.colors32.Length > 0; } catch { }

            try
            {
                int tris = 0;
                for (int sm = 0; sm < mesh.subMeshCount; sm++)
                    tris += (int)(mesh.GetIndexCount(sm) / 3);
                mr.triangles = tris;
            }
            catch { mr.triangles = 0; }

            try
            {
                int uv = 0;
                var tmp = new List<Vector4>();
                for (int ch = 0; ch < 8; ch++)
                {
                    mesh.GetUVs(ch, tmp);
                    if (tmp.Count > 0) uv++;
                }
                mr.uvChannels = uv;
            }
            catch { mr.uvChannels = 1; }

            if (skinned && rend is SkinnedMeshRenderer smr && smr.bones != null) mr.bones = smr.bones.Length;

            // Blend shape memory: every frame stores dV, and optionally dN + dT, per vertex.
            long bsBytes = 0;
            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                int frames = mesh.GetBlendShapeFrameCount(i);
                bsBytes += (long)frames * mesh.vertexCount * 12; // deltaVertices always present
            }
            bool normalsInShapes = mr.importer != null && mr.importer.importBlendShapeNormals != ModelImporterNormals.None;
            if (normalsInShapes && mesh.blendShapeCount > 0)
            {
                long extra = 0;
                for (int i = 0; i < mesh.blendShapeCount; i++)
                    extra += (long)mesh.GetBlendShapeFrameCount(i) * mesh.vertexCount * 24;
                bsBytes += extra;
            }
            mr.blendShapeVram = bsBytes;

            EvaluateMesh(mr);
            return mr;
        }

        public static void EvaluateMesh(MeshReport mr)
        {
            mr.issues.Clear();

            if (mr.isReadable)
                mr.issues.Add(new Issue(Severity.Warning,
                    "Read/Write Enabled is ON — Unity keeps a second full copy of this mesh in system RAM. VRChat does not need it."));

            if (mr.importer != null && mr.importer.meshCompression != ModelImporterMeshCompression.Off)
                mr.issues.Add(new Issue(Severity.Warning,
                    "Mesh Compression quantizes your vertex data. It shrinks the file on disk, not runtime memory, and it visibly warps UVs and normals. Turn it off."));

            if (mr.importer != null && !mr.importer.optimizeMeshVertices)
                mr.issues.Add(new Issue(Severity.Note,
                    "Optimize Mesh is off. Reordering vertices for the GPU cache is free performance with zero visual change."));

            if (mr.blendShapes > 0 && mr.importer != null && mr.importer.importBlendShapeNormals != ModelImporterNormals.None)
                mr.issues.Add(new Issue(Severity.Note,
                    $"Blend shape normals are imported: {Vram.Format(mr.blendShapeVram)} of shape data instead of {Vram.Format(mr.blendShapeVram / 3)}. Only needed if your shapes actually reshade."));

            if (mr.uvChannels > 2)
                mr.issues.Add(new Issue(Severity.Note,
                    $"{mr.uvChannels} UV channels. Each unused one is 8 bytes per vertex of dead weight."));

            if (mr.isSkinned && mr.renderer is SkinnedMeshRenderer smr)
            {
                if (smr.updateWhenOffscreen)
                    mr.issues.Add(new Issue(Severity.Warning,
                        "Update When Offscreen forces a full skinning pass every frame even when nobody can see it."));
                if (smr.skinnedMotionVectors)
                    mr.issues.Add(new Issue(Severity.Note,
                        "Skinned Motion Vectors doubles the skinning cost. VRChat does not use them — free frame time."));
                if (smr.quality == SkinQuality.Bone4 || smr.quality == SkinQuality.Auto)
                    mr.issues.Add(new Issue(Severity.Note,
                        "4-bone skinning. Most avatars are indistinguishable at 3 bones (d4rkAvatarOptimizer can do this at build time)."));
            }

            if (mr.vertices > 0 && mr.triangles > 0)
            {
                float ratio = (float)mr.vertices / mr.triangles;
                if (ratio > 1.2f)
                    mr.issues.Add(new Issue(Severity.Note,
                        $"{mr.vertices:N0} vertices for {mr.triangles:N0} triangles — heavy UV/normal splitting. The GPU cost tracks vertices, not the triangle count VRChat shows you."));
            }
        }

        static MaterialReport BuildMaterialReport(Material mat)
        {
            var rep = new MaterialReport
            {
                material = mat,
                shaderName = mat.shader != null ? mat.shader.name : "<missing>",
                renderQueue = mat.renderQueue
            };
            rep.isTransparent = mat.renderQueue >= 2450;

            if (mat.shader == null)
            {
                rep.issues.Add(new Issue(Severity.Critical, "Missing shader."));
                return rep;
            }

            int count = ShaderUtil.GetPropertyCount(mat.shader);
            for (int p = 0; p < count; p++)
            {
                if (ShaderUtil.GetPropertyType(mat.shader, p) != ShaderUtil.ShaderPropertyType.TexEnv) continue;
                if (mat.GetTexture(ShaderUtil.GetPropertyName(mat.shader, p)) != null) rep.textureSlotsAssigned++;
            }

            if (rep.isTransparent)
                rep.issues.Add(new Issue(Severity.Note,
                    "Transparent queue. Overdraw is the single biggest avatar frame cost in a full instance — use cutout where you can."));

            if (rep.shaderName.IndexOf("Standard", StringComparison.OrdinalIgnoreCase) >= 0)
                rep.issues.Add(new Issue(Severity.Note,
                    "Unity Standard shader. Poiyomi or lilToon give better VRChat results and play nicely with d4rkAvatarOptimizer."));

            return rep;
        }

        static void AddRendererIssues(Renderer rend, SkinnedMeshRenderer skinned, ScanResult result)
        {
            // Handled per-mesh; kept as a hook so renderer-level checks have a home.
        }
    }
}
