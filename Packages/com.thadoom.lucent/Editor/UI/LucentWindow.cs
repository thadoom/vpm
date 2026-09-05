using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Lucent
{
    public class LucentWindow : EditorWindow
    {
        enum Tab { Overview, Textures, Meshes, Materials, Project, Settings }

        [SerializeField] GameObject _root;
        Tab _tab = Tab.Overview;
        ScanResult _result;
        Vector2 _scroll;
        string _search = "";
        bool _onlyProblems = false;
        readonly HashSet<int> _expanded = new HashSet<int>();
        List<ProjectFixer.Check> _projectChecks;

        [MenuItem("Tools/Lucent/Optimizer %#l")]
        public static void Open()
        {
            var w = GetWindow<LucentWindow>("Lucent");
            w.minSize = new Vector2(720, 480);
            w.Show();
        }

        [MenuItem("GameObject/Lucent/Analyse this avatar", false, 20)]
        static void OpenFromHierarchy(MenuCommand cmd)
        {
            var w = GetWindow<LucentWindow>("Lucent");
            w._root = cmd.context as GameObject;
            w.Rescan(false);
            w.Show();
        }

        void OnEnable()
        {
            if (_root == null && Selection.activeGameObject != null) _root = Selection.activeGameObject.transform.root.gameObject;
            _projectChecks = ProjectFixer.Inspect();
        }

        void OnGUI()
        {
            DrawHeader();
            DrawToolbar();

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            switch (_tab)
            {
                case Tab.Overview: DrawOverview(); break;
                case Tab.Textures: DrawTextures(); break;
                case Tab.Meshes: DrawMeshes(); break;
                case Tab.Materials: DrawMaterials(); break;
                case Tab.Project: DrawProject(); break;
                case Tab.Settings: DrawSettings(); break;
            }
            EditorGUILayout.EndScrollView();
        }

        // ------------------------------------------------------------------ header

        void DrawHeader()
        {
            EditorGUILayout.BeginVertical(LucentUI.Card);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Lucent — quality-first optimizer", LucentUI.H1);
            GUILayout.FlexibleSpace();
            if (_result != null)
            {
                LucentUI.Badge(_result.CurrentRank, Vram.RankColor(_result.CurrentRank), 90);
                if (_result.ProjectedVram != _result.TotalVram)
                {
                    GUILayout.Label("->", GUILayout.Width(18));
                    LucentUI.Badge(_result.ProjectedRank, Vram.RankColor(_result.ProjectedRank), 90);
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            _root = (GameObject)EditorGUILayout.ObjectField("Avatar / root", _root, typeof(GameObject), true);
            if (EditorGUI.EndChangeCheck()) _result = null;

            using (new EditorGUI.DisabledScope(_root == null))
            {
                if (GUILayout.Button("Scan", GUILayout.Width(80))) Rescan(false);
                if (GUILayout.Button(new GUIContent("Deep Scan",
                        "Reads every texture back from the GPU at full resolution and measures whether halving it is visually lossless. Slower, but it is the only honest way to know."),
                        GUILayout.Width(90))) Rescan(true);
            }
            EditorGUILayout.EndHorizontal();

            if (_root == null)
                EditorGUILayout.HelpBox("Drop your avatar root here (or right-click it in the Hierarchy -> Lucent -> Analyse this avatar).", MessageType.Info);

            EditorGUILayout.EndVertical();
        }

        void DrawToolbar()
        {
            _tab = (Tab)GUILayout.Toolbar((int)_tab,
                new[] { "Overview", "Textures", "Meshes", "Materials", "Project Settings", "Settings" },
                EditorStyles.toolbarButton);
        }

        // ------------------------------------------------------------------ scanning

        void Rescan(bool deep)
        {
            if (_root == null) return;
            try
            {
                _result = Scanner.Scan(_root, true, (p, msg) =>
                    EditorUtility.DisplayProgressBar("Lucent", msg, p * (deep ? 0.3f : 1f)));

                if (deep) RunDeepScan();
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            _projectChecks = ProjectFixer.Inspect();
            Repaint();
        }

        void RunDeepScan()
        {
            var s = LucentSettings.instance;
            var list = _result.textures;
            for (int i = 0; i < list.Count; i++)
            {
                var tr = list[i];
                if (EditorUtility.DisplayCancelableProgressBar("Lucent — deep scan",
                        $"Measuring detail in {tr.texture.name} ({i + 1}/{list.Count})",
                        0.3f + 0.7f * i / Mathf.Max(1, list.Count)))
                    break;

                if (Mathf.Max(tr.width, tr.height) > s.deepScanMaxSide) continue;

                float psnr = TexProbe.HalfResPsnr(tr.texture, s.deepScanMaxSide);
                tr.halfResPsnr = psnr;
                tr.deepScanned = psnr > 0f;
                tr.downscaleIsFree = psnr >= s.downscalePsnrThreshold;
                Scanner.Evaluate(tr, s);

                if (tr.downscaleIsFree)
                    tr.issues.Add(new Issue(Severity.Warning,
                        $"Measured: halving this to {Mathf.Max(tr.width, tr.height) / 2}px is visually lossless ({psnr:0.0} dB PSNR). The extra pixels carry no detail."));
                else if (tr.deepScanned)
                    tr.issues.Add(new Issue(Severity.Ok,
                        $"Measured: this resolution is earning its VRAM ({psnr:0.0} dB — halving it would be visible). Leave it alone."));
            }

            _result.TotalVram = _result.textures.Sum(t => t.currentVram);
            _result.ProjectedVram = _result.textures.Sum(t => t.hasTarget ? t.targetVram : t.currentVram);
        }

        // ------------------------------------------------------------------ overview

        void DrawOverview()
        {
            if (_result == null) { EditorGUILayout.HelpBox("Nothing scanned yet.", MessageType.None); return; }

            EditorGUILayout.BeginVertical(LucentUI.Card);
            EditorGUILayout.LabelField("Texture memory", LucentUI.H2);

            var bar = GUILayoutUtility.GetRect(0, 22, GUILayout.ExpandWidth(true));
            LucentUI.VramBar(bar, _result.TotalVram, Vram.RankColor(_result.CurrentRank));
            GUI.Label(new Rect(bar.x + 6, bar.y + 2, bar.width, 18),
                $"{Vram.Format(_result.TotalVram)}   ({_result.CurrentRank})", EditorStyles.miniBoldLabel);

            if (_result.ProjectedVram < _result.TotalVram)
            {
                var bar2 = GUILayoutUtility.GetRect(0, 22, GUILayout.ExpandWidth(true));
                LucentUI.VramBar(bar2, _result.ProjectedVram, Vram.RankColor(_result.ProjectedRank));
                GUI.Label(new Rect(bar2.x + 6, bar2.y + 2, bar2.width, 18),
                    $"after Lucent: {Vram.Format(_result.ProjectedVram)}   ({_result.ProjectedRank})   -{Vram.Format(_result.Saving)}",
                    EditorStyles.miniBoldLabel);
            }

            EditorGUILayout.LabelField("Marks are the VRChat PC thresholds: 40 / 75 / 110 / 150 MB.", LucentUI.Subtle);
            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginVertical(LucentUI.Card);
            EditorGUILayout.LabelField("Against the VRChat PC ranking", LucentUI.H2);
            LucentUI.LimitRow("Triangles", _result.TotalTriangles, 32000, 70000, 70000, 70000);
            LucentUI.LimitRow("Skinned meshes", _result.SkinnedMeshCount, 1, 2, 8, 16);
            LucentUI.LimitRow("Meshes", _result.MeshCount, 4, 8, 16, 24);
            LucentUI.LimitRow("Material slots", _result.MaterialSlots, 4, 8, 16, 32);
            LucentUI.LimitRow("Bones", _result.BoneCount, 75, 150, 256, 400);
            LucentUI.LimitRow("PhysBone components", _result.PhysBoneCount, 4, 8, 16, 32);
            LucentUI.LimitRow("Particle systems", _result.ParticleSystemCount, 0, 4, 8, 16);
            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginVertical(LucentUI.Card);
            EditorGUILayout.LabelField("What Lucent found", LucentUI.H2);

            int crit = _result.textures.Count(t => t.Worst == Severity.Critical);
            int warn = _result.textures.Count(t => t.Worst == Severity.Warning);
            int downscalable = _result.textures.Count(t => t.downscaleIsFree);
            int deep = _result.textures.Count(t => t.deepScanned);

            LucentUI.StatRow("Textures", _result.textures.Count.ToString(), true);
            LucentUI.StatRow("Critical issues", crit.ToString(), crit == 0);
            LucentUI.StatRow("Warnings", warn.ToString(), warn == 0);
            LucentUI.StatRow("Deep-scanned", deep == 0 ? "0 — run Deep Scan for resolution advice" : deep.ToString(), deep > 0);
            LucentUI.StatRow("Provably safe to halve", downscalable.ToString(), true);
            LucentUI.StatRow("Meshes", _result.meshes.Count.ToString(), true);
            LucentUI.StatRow("Unique materials", _result.materials.Count.ToString(), true);
            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginVertical(LucentUI.Card);
            EditorGUILayout.LabelField("Lucent stops here on purpose", LucentUI.H2);
            EditorGUILayout.LabelField(
                "Lucent changes import settings and renderer flags — things that affect how your assets are stored, never how they are structured. " +
                "Mesh merging, material merging, blend shape baking and FX layer optimisation are destructive build-time transforms; " +
                "d4rkAvatarOptimizer already does them properly. Run Lucent first so d4rk gets clean, uncrunched, correctly-formatted inputs, " +
                "then let d4rk do the merging at upload time.", LucentUI.Subtle);
            if (GUILayout.Button("Open d4rkAvatarOptimizer on GitHub", GUILayout.Width(260)))
                Application.OpenURL("https://github.com/d4rkc0d3r/d4rkAvatarOptimizer");
            EditorGUILayout.EndVertical();
        }

        // ------------------------------------------------------------------ textures

        void DrawTextures()
        {
            if (_result == null) { EditorGUILayout.HelpBox("Nothing scanned yet.", MessageType.None); return; }

            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            _search = GUILayout.TextField(_search, EditorStyles.toolbarSearchField, GUILayout.Width(200));
            _onlyProblems = GUILayout.Toggle(_onlyProblems, "Only issues", EditorStyles.toolbarButton, GUILayout.Width(90));
            if (GUILayout.Button("Select all", EditorStyles.toolbarButton, GUILayout.Width(70)))
                foreach (var t in _result.textures) t.selected = true;
            if (GUILayout.Button("Select none", EditorStyles.toolbarButton, GUILayout.Width(80)))
                foreach (var t in _result.textures) t.selected = false;
            GUILayout.FlexibleSpace();

            int sel = _result.textures.Count(t => t.selected && t.NeedsChange);
            long saving = _result.textures.Where(t => t.selected && t.hasTarget).Sum(t => t.Saving);
            GUILayout.Label($"{sel} to fix · -{Vram.Format(saving)}", EditorStyles.miniLabel);

            using (new EditorGUI.DisabledScope(sel == 0))
            {
                if (GUILayout.Button("Apply selected", EditorStyles.toolbarButton, GUILayout.Width(110)))
                    ApplyTextureFixes();
            }
            EditorGUILayout.EndHorizontal();

            var list = _result.textures
                .Where(t => string.IsNullOrEmpty(_search) || t.texture.name.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0)
                .Where(t => !_onlyProblems || t.Worst >= Severity.Warning)
                .OrderByDescending(t => t.currentVram)
                .ToList();

            foreach (var t in list) DrawTextureRow(t);
        }

        void DrawTextureRow(TextureReport t)
        {
            int id = t.texture.GetInstanceID();
            bool open = _expanded.Contains(id);

            EditorGUILayout.BeginVertical(LucentUI.Card);

            EditorGUILayout.BeginHorizontal();
            t.selected = EditorGUILayout.Toggle(t.selected, GUILayout.Width(18));

            var thumb = GUILayoutUtility.GetRect(34, 34, GUILayout.Width(34), GUILayout.Height(34));
            var preview = AssetPreview.GetAssetPreview(t.texture) ?? (Texture)t.texture;
            if (preview != null) EditorGUI.DrawPreviewTexture(thumb, preview);

            EditorGUILayout.BeginVertical();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(t.texture.name, EditorStyles.linkLabel, GUILayout.MaxWidth(240)))
                EditorGUIUtility.PingObject(t.texture);
            GUILayout.FlexibleSpace();
            LucentUI.Badge(t.role.ToString(), LucentUI.SeverityColor(Severity.Note) * 0.8f, 100);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label($"{t.width}x{t.height}  {t.currentFormat}", LucentUI.Mono, GUILayout.Width(190));
            if (t.hasTarget && t.NeedsChange)
            {
                GUILayout.Label("->", GUILayout.Width(16));
                var c = GUI.color; GUI.color = new Color(0.5f, 0.9f, 0.7f);
                GUILayout.Label($"max {t.target.maxSize}  {t.target.format}", LucentUI.Mono, GUILayout.Width(190));
                GUI.color = c;
            }
            GUILayout.FlexibleSpace();
            GUILayout.Label(Vram.Format(t.currentVram), EditorStyles.miniBoldLabel, GUILayout.Width(70));
            if (t.hasTarget && t.Saving > 0)
            {
                var c = GUI.color; GUI.color = new Color(0.5f, 0.9f, 0.7f);
                GUILayout.Label("-" + Vram.Format(t.Saving), EditorStyles.miniBoldLabel, GUILayout.Width(70));
                GUI.color = c;
            }
            else GUILayout.Space(74);

            if (GUILayout.Button(open ? "▾" : "▸", EditorStyles.miniButton, GUILayout.Width(22)))
            {
                if (open) _expanded.Remove(id); else _expanded.Add(id);
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();

            if (t.Worst >= Severity.Warning && !open)
            {
                var first = t.issues.OrderByDescending(i => i.severity).First();
                EditorGUILayout.LabelField(first.message, LucentUI.Subtle);
            }

            if (open)
            {
                EditorGUILayout.Space(2);
                if (!string.IsNullOrEmpty(t.reason))
                    EditorGUILayout.LabelField("Plan: " + t.reason, LucentUI.Subtle);

                foreach (var i in t.issues.OrderByDescending(i => i.severity))
                    EditorGUILayout.HelpBox(i.message, LucentUI.ToMessageType(i.severity));

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Path", t.assetPath, LucentUI.Mono);
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.LabelField("Used by: " + string.Join(", ", t.usedByMaterials), LucentUI.Subtle);

                if (t.facts.valid)
                {
                    EditorGUILayout.LabelField(
                        $"Pixels: alpha {(t.facts.alphaIsOpaque ? "fully opaque" : t.facts.alphaIsBinary ? "1-bit cutout" : "graded")}" +
                        $" · {(t.facts.isGrayscale ? "grayscale" : "colour")}" +
                        $" · detail variance {t.facts.luminanceVariance:0.0000}" +
                        (t.deepScanned ? $" · half-res PSNR {t.halfResPsnr:0.0} dB" : ""),
                        LucentUI.Mono);
                }

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Select importer", GUILayout.Width(120)))
                    Selection.activeObject = AssetDatabase.LoadAssetAtPath<Texture>(t.assetPath);
                using (new EditorGUI.DisabledScope(!t.NeedsChange))
                {
                    if (GUILayout.Button("Apply this one", GUILayout.Width(120)))
                    {
                        TextureFixer.ApplyOne(t);
                        Rescan(false);
                    }
                }
                if (GUILayout.Button(new GUIContent("Deep scan this", "Measure whether half resolution is visually lossless."), GUILayout.Width(120)))
                {
                    var s = LucentSettings.instance;
                    float psnr = TexProbe.HalfResPsnr(t.texture, s.deepScanMaxSide);
                    t.halfResPsnr = psnr; t.deepScanned = psnr > 0f;
                    t.downscaleIsFree = psnr >= s.downscalePsnrThreshold;
                    Scanner.Evaluate(t, s);
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndVertical();
        }

        void ApplyTextureFixes()
        {
            var todo = _result.textures.Where(t => t.selected && t.NeedsChange).ToList();
            if (todo.Count == 0) return;

            long before = todo.Sum(t => t.currentVram);
            long after = todo.Sum(t => t.targetVram);

            if (!EditorUtility.DisplayDialog("Lucent",
                    $"Reimport {todo.Count} textures?\n\n{Vram.Format(before)} -> {Vram.Format(after)}\n\n" +
                    "This rewrites the .meta import settings. Undo works, but a reimport of large textures can take a while.",
                    "Apply", "Cancel"))
                return;

            int n = TextureFixer.Apply(todo);
            Debug.Log($"[Lucent] Reimported {n} textures.");
            Rescan(false);
        }

        // ------------------------------------------------------------------ meshes

        void DrawMeshes()
        {
            if (_result == null) { EditorGUILayout.HelpBox("Nothing scanned yet.", MessageType.None); return; }

            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Apply mesh fixes to all", EditorStyles.toolbarButton, GUILayout.Width(160)))
            {
                int n = MeshFixer.Apply(_result.meshes, LucentSettings.instance);
                Debug.Log($"[Lucent] Fixed {n} mesh importers / renderers.");
                Rescan(false);
            }
            EditorGUILayout.EndHorizontal();

            foreach (var m in _result.meshes.OrderByDescending(m => m.triangles))
            {
                EditorGUILayout.BeginVertical(LucentUI.Card);
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button(m.mesh.name, EditorStyles.linkLabel, GUILayout.MaxWidth(260)))
                    EditorGUIUtility.PingObject(m.mesh);
                GUILayout.FlexibleSpace();
                GUILayout.Label($"{m.triangles:N0} tris · {m.vertices:N0} verts · {m.subMeshes} submesh · {m.blendShapes} shapes",
                    LucentUI.Mono);
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.LabelField(
                    $"Mesh {Vram.Format(m.meshVram)}" +
                    (m.blendShapeVram > 0 ? $" + blend shapes {Vram.Format(m.blendShapeVram)}" : "") +
                    $" · {m.uvChannels} UV channels" + (m.isReadable ? " · Read/Write ON" : ""),
                    LucentUI.Subtle);

                foreach (var i in m.issues.OrderByDescending(i => i.severity))
                    EditorGUILayout.HelpBox(i.message, LucentUI.ToMessageType(i.severity));

                EditorGUILayout.EndVertical();
            }
        }

        // ------------------------------------------------------------------ materials

        void DrawMaterials()
        {
            if (_result == null) { EditorGUILayout.HelpBox("Nothing scanned yet.", MessageType.None); return; }

            var byShader = _result.materials.GroupBy(m => m.shaderName).OrderByDescending(g => g.Count());
            EditorGUILayout.HelpBox(
                $"{_result.materials.Count} unique materials across {byShader.Count()} shaders. " +
                "Every distinct shader variant is a separate draw call and a separate shader compile at load. " +
                "Fewer shaders = faster avatar load for everyone in the instance.", MessageType.Info);

            foreach (var g in byShader)
            {
                EditorGUILayout.BeginVertical(LucentUI.Card);
                EditorGUILayout.LabelField(g.Key, LucentUI.H2);
                foreach (var m in g)
                {
                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button(m.material.name, EditorStyles.linkLabel, GUILayout.MaxWidth(240)))
                        EditorGUIUtility.PingObject(m.material);
                    GUILayout.FlexibleSpace();
                    GUILayout.Label($"queue {m.renderQueue} · {m.textureSlotsAssigned} textures", LucentUI.Mono);
                    EditorGUILayout.EndHorizontal();
                    foreach (var i in m.issues)
                        EditorGUILayout.LabelField("· " + i.message, LucentUI.Subtle);
                }
                EditorGUILayout.EndVertical();
            }
        }

        // ------------------------------------------------------------------ project

        void DrawProject()
        {
            EditorGUILayout.HelpBox(
                "These are project-wide. The first one is the answer to \"why does my 4K texture look like 2K even though the importer says 4096\": " +
                "Global Mipmap Limit drops the top mip on every texture in the project, and Unity ships some quality levels with it set to Half Resolution.",
                MessageType.Info);

            if (_projectChecks == null) _projectChecks = ProjectFixer.Inspect();

            EditorGUILayout.BeginVertical(LucentUI.Card);
            foreach (var c in _projectChecks)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(c.label, GUILayout.Width(280));
                var col = GUI.color;
                GUI.color = c.ok ? new Color(0.5f, 0.9f, 0.6f) : new Color(0.95f, 0.6f, 0.35f);
                EditorGUILayout.LabelField(c.current, EditorStyles.boldLabel, GUILayout.Width(240));
                GUI.color = col;
                EditorGUILayout.LabelField(c.ok ? "" : "-> " + c.recommended, LucentUI.Subtle);
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Fix all project settings", GUILayout.Height(28)))
            {
                ProjectFixer.ApplyAll();
                _projectChecks = ProjectFixer.Inspect();
            }
            if (GUILayout.Button("Re-check", GUILayout.Height(28), GUILayout.Width(90)))
                _projectChecks = ProjectFixer.Inspect();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(8);
            EditorGUILayout.BeginVertical(LucentUI.Card);
            EditorGUILayout.LabelField("Bulk operations", LucentUI.H2);
            EditorGUILayout.LabelField(
                "Applies Lucent's import rules to textures that were imported before you installed it. " +
                "This reimports assets and can take several minutes on a large project.", LucentUI.Subtle);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Retarget selected folder in Project window"))
                BulkRetarget(Selection.assetGUIDs);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        static void BulkRetarget(string[] guids)
        {
            if (guids == null || guids.Length == 0)
            {
                EditorUtility.DisplayDialog("Lucent", "Select a folder in the Project window first.", "OK");
                return;
            }

            var folders = guids.Select(AssetDatabase.GUIDToAssetPath).Where(AssetDatabase.IsValidFolder).ToArray();
            if (folders.Length == 0)
            {
                EditorUtility.DisplayDialog("Lucent", "That selection contains no folders.", "OK");
                return;
            }

            var texGuids = AssetDatabase.FindAssets("t:Texture2D", folders);
            if (!EditorUtility.DisplayDialog("Lucent",
                    $"Re-evaluate and reimport {texGuids.Length} textures under:\n\n{string.Join("\n", folders)}\n\nThis can take a while.",
                    "Go", "Cancel"))
                return;

            var s = LucentSettings.instance;
            var reports = new List<TextureReport>();

            for (int i = 0; i < texGuids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(texGuids[i]);
                if (s.IsExcluded(path)) continue;
                if (EditorUtility.DisplayCancelableProgressBar("Lucent", "Analysing " + path, (float)i / texGuids.Length)) break;

                var tex = AssetDatabase.LoadAssetAtPath<Texture>(path);
                if (tex == null) continue;
                var imp = AssetImporter.GetAtPath(path) as TextureImporter;
                if (imp == null) continue;

                var rep = new TextureReport
                {
                    texture = tex,
                    assetPath = path,
                    importer = imp,
                    width = tex.width,
                    height = tex.height,
                    currentVram = Vram.RuntimeSize(tex),
                    currentMaxSize = imp.maxTextureSize,
                    sRGB = imp.sRGBTexture,
                    crunched = imp.crunchedCompression,
                    mipmaps = imp.mipmapEnabled
                };
                if (tex is Texture2D t2) rep.currentFormat = t2.format;
                rep.facts = TexProbe.Analyze(tex);
                Scanner.Evaluate(rep, s);
                if (rep.NeedsChange) reports.Add(rep);
            }
            EditorUtility.ClearProgressBar();

            int n = TextureFixer.Apply(reports);
            Debug.Log($"[Lucent] Bulk retarget: reimported {n} textures.");
        }

        // ------------------------------------------------------------------ settings

        void DrawSettings()
        {
            var s = LucentSettings.instance;
            var so = new SerializedObject(s);
            so.Update();

            EditorGUILayout.BeginVertical(LucentUI.Card);
            EditorGUILayout.LabelField("Import automation", LucentUI.H2);
            EditorGUILayout.PropertyField(so.FindProperty("autoApplyOnImport"), new GUIContent("Apply on new imports"));
            var maxSize = so.FindProperty("defaultMaxSize");
            maxSize.intValue = EditorGUILayout.IntPopup("Default Max Size", maxSize.intValue,
                new[] { "1024", "2048", "4096", "8192" }, new[] { 1024, 2048, 4096, 8192 });
            EditorGUILayout.PropertyField(so.FindProperty("clampToSourceResolution"), new GUIContent("Never upscale past source"));
            EditorGUILayout.PropertyField(so.FindProperty("excludedFolders"), new GUIContent("Excluded folders"), true);
            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginVertical(LucentUI.Card);
            EditorGUILayout.LabelField("Quality policy", LucentUI.H2);
            EditorGUILayout.PropertyField(so.FindProperty("quality"));
            EditorGUILayout.PropertyField(so.FindProperty("preferBC7"), new GUIContent("BC7 for colour maps"));
            EditorGUILayout.PropertyField(so.FindProperty("bc5ForNormals"), new GUIContent("BC5 for normal maps"));
            EditorGUILayout.PropertyField(so.FindProperty("bc4ForSingleChannel"), new GUIContent("BC4 for 1-channel maps"));
            EditorGUILayout.PropertyField(so.FindProperty("stripUselessAlpha"), new GUIContent("Strip opaque alpha"));
            EditorGUILayout.PropertyField(so.FindProperty("crunchCompression"), new GUIContent("Crunch compression"));
            if (s.crunchCompression)
            {
                EditorGUILayout.PropertyField(so.FindProperty("crunchQuality"));
                EditorGUILayout.HelpBox(
                    "Crunch does not reduce VRAM — only the download. It is lossy on top of BC, and d4rkAvatarOptimizer cannot merge crunched textures into arrays.",
                    MessageType.Warning);
            }
            EditorGUILayout.PropertyField(so.FindProperty("anisoLevel"));
            EditorGUILayout.PropertyField(so.FindProperty("enforceMipmaps"), new GUIContent("Force mip maps on"));
            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginVertical(LucentUI.Card);
            EditorGUILayout.LabelField("Deep scan", LucentUI.H2);
            EditorGUILayout.PropertyField(so.FindProperty("downscalePsnrThreshold"), new GUIContent("Lossless threshold (dB)"));
            EditorGUILayout.LabelField("48 dB = invisible even side by side. 42 dB = invisible in motion in VR.", LucentUI.Subtle);
            var deepMax = so.FindProperty("deepScanMaxSide");
            deepMax.intValue = EditorGUILayout.IntPopup("Skip textures above", deepMax.intValue,
                new[] { "2048", "4096", "8192" }, new[] { 2048, 4096, 8192 });
            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginVertical(LucentUI.Card);
            EditorGUILayout.LabelField("Mesh policy", LucentUI.H2);
            EditorGUILayout.PropertyField(so.FindProperty("disableMeshReadWrite"), new GUIContent("Turn off Read/Write"));
            EditorGUILayout.PropertyField(so.FindProperty("disableMeshCompression"), new GUIContent("Turn off Mesh Compression"));
            EditorGUILayout.PropertyField(so.FindProperty("stripBlendShapeNormals"), new GUIContent("Strip blend shape normals"));
            EditorGUILayout.PropertyField(so.FindProperty("disableSkinnedMotionVectors"), new GUIContent("Turn off Skinned Motion Vectors"));
            EditorGUILayout.PropertyField(so.FindProperty("disableUpdateWhenOffscreen"), new GUIContent("Turn off Update When Offscreen"));
            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginVertical(LucentUI.Card);
            EditorGUILayout.LabelField("Project settings enforcement", LucentUI.H2);
            EditorGUILayout.PropertyField(so.FindProperty("forceFullMipmapLimit"), new GUIContent("Force full mipmap limit"));
            EditorGUILayout.PropertyField(so.FindProperty("disableMipmapStreaming"), new GUIContent("Disable mipmap streaming"));
            EditorGUILayout.EndVertical();

            if (so.ApplyModifiedProperties()) s.Save();

            EditorGUILayout.Space(6);
            if (GUILayout.Button("Reset to quality-first defaults"))
            {
                if (EditorUtility.DisplayDialog("Lucent", "Reset all Lucent settings?", "Reset", "Cancel"))
                {
                    var fresh = ScriptableObject.CreateInstance<LucentSettings>();
                    EditorUtility.CopySerialized(fresh, s);
                    DestroyImmediate(fresh);
                    s.Save();
                }
            }
        }
    }
}
