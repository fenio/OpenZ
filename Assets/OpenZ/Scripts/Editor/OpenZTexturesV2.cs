using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// 1 Import settings = the texture rules below (idempotent; only changed importers reimport).
    /// 2 Materials = both ground materials on OpenZ/Sand with the crisp-edge SDF + world detail (L17 switches from URP Lit,
    ///   every desert-only feature zeroed), rocks / cliffs v3 without the de-grime blur.
    /// 3 Check = PASS / FAIL per texture and material; in Play mode also the live TER_Ground and the textures' GPU memory.
    /// </summary>
    public static class OpenZTexturesV2
    {
        const string Tex = "Assets/OpenZ/Textures", Env = "Assets/OpenZ/Materials/Env", Rocks = "Assets/OpenZ/Models/RocksZ";
        public const string SdfL02 = Tex + "/Ground/L02_ground_sdf.png", SdfL17 = Tex + "/Ground/L17_ground_sdf.png";
        public const string Detail = Tex + "/Detail/oz_ground_detail.png", DetailN = Tex + "/Detail/oz_ground_detail_n.png";

        enum Kind { Colour, Data, Sdf, Normal }

        readonly struct Rule
        {
            public readonly string path; public readonly Kind kind; public readonly int max, aniso; public readonly TextureWrapMode wrap;
            public readonly bool npotNone; public readonly int w, h;
            public Rule(string path, Kind kind, int max, int aniso, TextureWrapMode wrap, bool npotNone, int w, int h)
            { this.path = path; this.kind = kind; this.max = max; this.aniso = aniso; this.wrap = wrap; this.npotNone = npotNone; this.w = w; this.h = h; }
        }

        const TextureWrapMode Clamp = TextureWrapMode.Clamp, Repeat = TextureWrapMode.Repeat;
        static readonly Rule[] Rules =
        {   // path, kind, max size, aniso, wrap, keep NPOT size, expected imported width / height
            new Rule(Tex + "/L02_ground_paint.png", Kind.Colour, 8192, 16, Clamp, true, 4096, 8192),
            new Rule(Tex + "/L17_ground_paint.png", Kind.Colour, 8192, 16, Clamp, false, 4096, 4096),
            new Rule(SdfL02, Kind.Sdf, 4096, 1, Clamp, true, 2048, 2432),
            new Rule(SdfL17, Kind.Sdf, 4096, 1, Clamp, true, 2048, 2048),
            new Rule(Detail, Kind.Data, 1024, 8, Repeat, false, 1024, 1024),
            new Rule(DetailN, Kind.Data, 1024, 8, Repeat, false, 1024, 1024),
            new Rule(Rocks + "/rockz2_atlas_Desert.png", Kind.Colour, 2048, 8, Clamp, false, 2048, 2048),
            new Rule(Rocks + "/rockz2_atlas_City.png", Kind.Colour, 2048, 8, Clamp, false, 2048, 2048),
            new Rule(Tex + "/CliffsV3/cliffv3_rock_L02_albedo.png", Kind.Colour, 4096, 8, Repeat, false, 2048, 4096),
            new Rule(Tex + "/CliffsV3/cliffv3_rock_L17_albedo.png", Kind.Colour, 4096, 8, Repeat, false, 2048, 4096),
            new Rule(Tex + "/CliffsV3/cliffv3_top_L02_albedo.png", Kind.Colour, 2048, 8, Repeat, false, 1024, 1024),
            new Rule(Tex + "/CliffsV3/cliffv3_top_L17_albedo.png", Kind.Colour, 2048, 8, Repeat, false, 1024, 1024),
            new Rule(Tex + "/CliffsV3/cliffv3_dunes_L02_albedo.png", Kind.Colour, 2048, 8, Repeat, false, 1024, 1024),
            new Rule(Tex + "/CliffsV3/cliffv3_concrete_L17_albedo.png", Kind.Colour, 2048, 8, Repeat, false, 1024, 1024),
            new Rule(Tex + "/CliffsV3/cliffv3_facade_L17_albedo.png", Kind.Colour, 2048, 8, Repeat, false, 1024, 1024),
            new Rule(Tex + "/CliffsV3/cliffv3_rock_L02_normal.png", Kind.Normal, 4096, 8, Repeat, false, 1024, 2048),
            new Rule(Tex + "/CliffsV3/cliffv3_rock_L17_normal.png", Kind.Normal, 4096, 8, Repeat, false, 1024, 2048),
            new Rule(Tex + "/CliffsV3/cliffv3_top_L02_normal.png", Kind.Normal, 2048, 8, Repeat, false, 1024, 1024),
            new Rule(Tex + "/CliffsV3/cliffv3_top_L17_normal.png", Kind.Normal, 2048, 8, Repeat, false, 1024, 1024),
        };

        static readonly string[] NoDegrime =
        {
            Rocks + "/M_RockZ_Desert.mat", Rocks + "/M_RockZ_City.mat",
            Env + "/M_CliffV3_Rock_L02.mat", Env + "/M_CliffV3_Top_L02.mat", Env + "/M_CliffV3_Dunes_L02.mat",
            Env + "/M_CliffV3_Rock_L17.mat", Env + "/M_CliffV3_Top_L17.mat", Env + "/M_CliffV3_Concrete_L17.mat",
            Env + "/M_CliffV3_Facade_L17.mat", Env + "/M_CliffV3_Ground_L17.mat",
        };

        // ------------------------------------------------------------------------------------------------ 1 import settings
        [MenuItem("OpenZ/Textures v2/1 Import settings")]
        public static string ApplyImports()
        {
            var log = new List<string>();
            foreach (var r in Rules)
            {
                var ti = AssetImporter.GetAtPath(r.path) as TextureImporter;
                if (ti == null) { log.Add("MISSING " + r.path); Debug.LogError("[textures_v2] missing texture " + r.path); continue; }
                bool colour = r.kind == Kind.Colour, sdf = r.kind == Kind.Sdf, normal = r.kind == Kind.Normal;
                bool dirty = false;
                void Set<T>(T now, T want, System.Action<T> set) { if (!EqualityComparer<T>.Default.Equals(now, want)) { set(want); dirty = true; } }
                Set(ti.textureType, normal ? TextureImporterType.NormalMap : TextureImporterType.Default, v => ti.textureType = v);
                if (!normal) Set(ti.sRGBTexture, colour, v => ti.sRGBTexture = v);
                Set(ti.mipmapEnabled, !sdf, v => ti.mipmapEnabled = v);
                Set(ti.filterMode, sdf ? FilterMode.Bilinear : FilterMode.Trilinear, v => ti.filterMode = v);
                Set(ti.anisoLevel, r.aniso, v => ti.anisoLevel = v);
                Set(ti.wrapMode, r.wrap, v => ti.wrapMode = v);
                Set(ti.maxTextureSize, r.max, v => ti.maxTextureSize = v);
                Set(ti.npotScale, r.npotNone ? TextureImporterNPOTScale.None : TextureImporterNPOTScale.ToNearest, v => ti.npotScale = v);
                Set(ti.isReadable, false, v => ti.isReadable = v);
                Set(ti.streamingMipmaps, false, v => ti.streamingMipmaps = v);
                Set(ti.textureCompression, sdf ? TextureImporterCompression.Uncompressed : TextureImporterCompression.CompressedHQ, v => ti.textureCompression = v);
                Set(ti.crunchedCompression, false, v => ti.crunchedCompression = v);
                if (!colour && !normal) Set(ti.alphaSource, r.kind == Kind.Data ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None, v => ti.alphaSource = v);
                // the editor and the Windows player read the Standalone entry: SDF = 8-bit RG (exact edges; BC5 would move them
                // by up to ~3 cm), everything else = automatic HQ (BC7; BC5 for the normal maps) at the rule's size
                var ps = ti.GetPlatformTextureSettings("Standalone");
                var want = new TextureImporterPlatformSettings
                {
                    name = "Standalone", overridden = sdf, maxTextureSize = r.max, resizeAlgorithm = ps.resizeAlgorithm,
                    format = sdf ? (r.path == SdfL17 ? TextureImporterFormat.RGB24 : TextureImporterFormat.RG16) : TextureImporterFormat.Automatic,   // L17: B = road markings (lights_roads_v1)
                    textureCompression = sdf ? TextureImporterCompression.Uncompressed : TextureImporterCompression.CompressedHQ,
                    compressionQuality = ps.compressionQuality, crunchedCompression = false, allowsAlphaSplitting = false
                };
                if (ps.overridden != want.overridden || ps.maxTextureSize != want.maxTextureSize || ps.format != want.format ||
                    ps.textureCompression != want.textureCompression || ps.crunchedCompression)
                { ti.SetPlatformTextureSettings(want); dirty = true; }
                if (dirty) { ti.SaveAndReimport(); log.Add(r.path.Substring(r.path.LastIndexOf('/') + 1)); }
            }
            var s = log.Count == 0 ? "import settings already right" : "reimported: " + string.Join(", ", log);
            Debug.Log("[textures_v2] " + s);
            return s;
        }

        // ------------------------------------------------------------------------------------------------ 2 materials
        static Texture Load(string p) => AssetDatabase.LoadAssetAtPath<Texture>(p);

        /// <summary>Ground material of a level ("L02" / "L17") on OpenZ/Sand with the crisp-edge SDF and the detail layer.
        /// Also called by OpenZSetup when it rebuilds the ground materials.</summary>
        public static void ApplyGround(Material m, string level)
        {
            if (!m) return;
            var sand = Shader.Find("OpenZ/Sand");
            if (!sand || sand.FindPropertyIndex("_GroundSdf") < 0)
            { Debug.LogError("[textures_v2] OpenZ/Sand has no _GroundSdf: apply the Sand.shader patch first"); return; }
            bool l02 = level == "L02";
            if (!l02 && m.shader != sand)
            {   // L17 was URP Lit: keep its paint, tint and smoothness, zero every desert-only feature of OpenZ/Sand
                var tex = m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : null;
                var tint = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : new Color(0.86f, 0.87f, 0.9f);
                float sm = m.HasProperty("_Smoothness") ? m.GetFloat("_Smoothness") : 0.05f;
                m.shader = sand;
                m.SetTexture("_BaseMap", tex ? tex : Load(Tex + "/L17_ground_paint.png")); m.SetColor("_BaseColor", tint); m.SetFloat("_Smoothness", sm);
                m.SetFloat("_Grade", 0); m.SetFloat("_Blur", 0); m.SetFloat("_RippleLarge", 0); m.SetFloat("_RippleSmall", 0); m.SetFloat("_ShadowLift", 0);
                m.shaderKeywords = new string[0];
            }
            // L17 as on URP Lit: no environment reflection (the reflection probe / skybox is the static DAY sky, it would light the
            // floor at night) and no specular highlights. OpenZ/Sand's own _OZ_NO_REFL_SPEC defines URP's two switches; L02 never
            // gets it (its stale m_InvalidKeywords _ENVIRONMENTREFLECTIONS_OFF / _SPECULARHIGHLIGHTS_OFF stay inert: L02 unchanged)
            if (l02) m.DisableKeyword("_OZ_NO_REFL_SPEC"); else m.EnableKeyword("_OZ_NO_REFL_SPEC");
            m.SetTexture("_GroundSdf", Load(l02 ? SdfL02 : SdfL17));
            m.SetFloat("_SdfMode", l02 ? 1 : 3);                                              // L17: + road markings (lights_roads_v1)
            if (m.HasProperty("_MarkIO")) m.SetVector("_MarkIO", new Vector4(0.08f, 0.10f, 0, 0));
            m.SetFloat("_SdfRange", 1);
            m.SetVector("_GroundSize", l02 ? new Vector4(192, 228, 0, 0) : new Vector4(268, 304, 0, 0));
            m.SetVector("_EdgeIO", new Vector4(0.16f, 0.12f, 0.07f, 0.10f));
            m.SetVector("_CrispPx", new Vector4(0.05f, 0.10f, 0, 0));
            m.SetVector("_PlateBevel", new Vector4(1.08f, 0.74f, 0.12f, 0.08f));
            m.SetTexture("_DetailTex", Load(Detail));
            m.SetTexture("_DetailNrm", Load(DetailN));
            m.SetFloat("_DetailOn", 1);
            m.SetVector("_DetailTiles", new Vector4(4f, 9.7f, 0, 0));
            m.SetVector("_DetailStr", l02 ? new Vector4(0.55f, 0.5f, 0.4f, 0.4f) : new Vector4(0.45f, 0.5f, 0.4f, 0.3f));
            m.SetFloat("_DetailNormalStr", 1);
            m.SetVector("_DetailFade", new Vector4(110, 260, 0.35f, 0));
            EditorUtility.SetDirty(m);
        }

        [MenuItem("OpenZ/Textures v2/2 Materials")]
        public static string ApplyMaterials()
        {
            var log = new List<string>();
            foreach (var (name, lv) in new[] { ("M_L02_Ground", "L02"), ("M_L17_Ground", "L17") })
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>($"{Env}/{name}.mat");
                if (!m) { log.Add("missing " + name); continue; }
                ApplyGround(m, lv); log.Add(name);
            }
            foreach (var p in NoDegrime)
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(p);
                if (!m || !m.HasProperty("_Degrime")) continue;
                if (m.GetFloat("_Degrime") != 0f) { m.SetFloat("_Degrime", 0f); EditorUtility.SetDirty(m); log.Add(m.name + " de-grime 0"); }
            }
            AssetDatabase.SaveAssets();
            var s = string.Join(", ", log);
            Debug.Log("[textures_v2] materials: " + s);
            return s;
        }

        [MenuItem("OpenZ/Textures v2/Revert L17 ground to URP Lit")]
        public static string RevertL17()
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>($"{Env}/M_L17_Ground.mat");
            if (!m) return "missing M_L17_Ground";
            var tex = m.GetTexture("_BaseMap"); var tint = m.GetColor("_BaseColor"); float sm = m.GetFloat("_Smoothness");
            m.DisableKeyword("_OZ_NO_REFL_SPEC");
            m.shader = Shader.Find("Universal Render Pipeline/Lit");
            m.SetTexture("_BaseMap", tex); m.SetTexture("_MainTex", tex); m.SetColor("_BaseColor", tint); m.SetColor("_Color", tint);
            m.SetFloat("_Smoothness", sm); m.SetFloat("_Metallic", 0); m.SetFloat("_SpecularHighlights", 0); m.SetFloat("_EnvironmentReflections", 0);
            m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF"); m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            EditorUtility.SetDirty(m); AssetDatabase.SaveAssets();
            Debug.Log("[textures_v2] M_L17_Ground back on URP Lit");
            return "M_L17_Ground -> URP Lit";
        }

        // ------------------------------------------------------------------------------------------------ 3 check
        [MenuItem("OpenZ/Textures v2/3 Check (edit or Play mode)")]
        public static string Check()
        {
            var sb = new StringBuilder(); int fails = 0; long bytes = 0;
            void R(bool ok, string what) { sb.AppendLine((ok ? "PASS " : "FAIL ") + what); if (!ok) fails++; }
            foreach (var r in Rules)
            {
                var t = AssetDatabase.LoadAssetAtPath<Texture2D>(r.path);
                var ti = AssetImporter.GetAtPath(r.path) as TextureImporter;
                string n = r.path.Substring(r.path.LastIndexOf('/') + 1);
                if (!t || ti == null) { R(false, n + " missing"); continue; }
                bool sizeOk = t.width == r.w && t.height == r.h;
                bool fmtOk = r.kind == Kind.Sdf ? t.format == (r.path == SdfL17 ? TextureFormat.RGB24 : TextureFormat.RG16) :
                             r.kind == Kind.Normal ? (t.format == TextureFormat.BC5 || t.format == TextureFormat.DXT5 || t.format == TextureFormat.BC7) :
                             (t.format == TextureFormat.BC7 || t.format == TextureFormat.DXT5 || t.format == TextureFormat.DXT1);
                bool bc7 = r.kind == Kind.Sdf || r.kind == Kind.Normal || t.format == TextureFormat.BC7;
                long mem = Profiler.GetRuntimeMemorySizeLong(t); bytes += mem;
                R(sizeOk && fmtOk && bc7 && (t.mipmapCount > 1) == (r.kind != Kind.Sdf) && ti.anisoLevel >= r.aniso,
                  $"{n}: {t.width}x{t.height} {t.format} mips {t.mipmapCount} aniso {ti.anisoLevel} ({mem / 1048576f:F1} MB)" +
                  (sizeOk ? "" : $" [want {r.w}x{r.h}]"));
            }
            var sand = Shader.Find("OpenZ/Sand");
            R(sand && sand.FindPropertyIndex("_GroundSdf") >= 0 && sand.keywordSpace.FindKeyword("_OZ_NO_REFL_SPEC").isValid &&
              !sand.keywordSpace.FindKeyword("_ENVIRONMENTREFLECTIONS_OFF").isValid && !sand.keywordSpace.FindKeyword("_SPECULARHIGHLIGHTS_OFF").isValid,
              "OpenZ/Sand has the textures_v2 properties and its own _OZ_NO_REFL_SPEC switch (URP's two keywords stay undeclared)");
            foreach (var (name, mode, sdf) in new[] { ("M_L02_Ground", 1f, SdfL02), ("M_L17_Ground", 3f, SdfL17) })   // L17 mode 3: lights_roads_v1 road markings
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>($"{Env}/{name}.mat");
                bool ok = m && m.shader == sand && m.GetFloat("_SdfMode") == mode && m.GetTexture("_GroundSdf") == Load(sdf) &&
                          m.GetTexture("_DetailTex") == Load(Detail) && m.GetTexture("_DetailNrm") == Load(DetailN) && m.GetFloat("_DetailOn") > 0;
                R(ok, $"{name}: OpenZ/Sand, _SdfMode {mode}, SDF + detail set");
                if (m && name == "M_L17_Ground" && m.shader == sand)
                    R(m.GetFloat("_Grade") == 0 && m.GetFloat("_RippleLarge") == 0 && m.GetFloat("_RippleSmall") == 0 && m.GetFloat("_ShadowLift") == 0 && m.GetFloat("_Blur") == 0,
                      "M_L17_Ground: desert-only features zeroed (grade, ripples, warm shadow lift, blur)");
                if (m && m.shader == sand)
                    R(m.IsKeywordEnabled("_OZ_NO_REFL_SPEC") == (name == "M_L17_Ground"), name == "M_L17_Ground"
                      ? "M_L17_Ground: environment reflection + specular highlights off (_OZ_NO_REFL_SPEC, as on URP Lit; no day-sky reflection at night)"
                      : "M_L02_Ground: reflection / specular as before (_OZ_NO_REFL_SPEC off)");
            }
            foreach (var p in NoDegrime)
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(p);
                if (m && m.HasProperty("_Degrime")) R(m.GetFloat("_Degrime") == 0f, $"{m.name}: de-grime 0");
            }
            if (Application.isPlaying)
            {   // the live level: the ground renderers carry the v2 material
                var ground = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).Where(r => r.name.StartsWith("TER_Ground")).ToArray();
                R(ground.Length > 0, $"Play: {ground.Length} TER_Ground renderer(s) in the scene");
                foreach (var g in ground)
                {
                    var m = g.sharedMaterial;
                    R(m && m.shader == sand && m.GetFloat("_SdfMode") > 0 && m.GetFloat("_DetailOn") > 0, $"Play: {g.name} uses {(m ? m.name : "none")} with the v2 pass");
                }
                sb.AppendLine($"Play: QualitySettings aniso {QualitySettings.anisotropicFiltering}, mipmap limit {QualitySettings.globalTextureMipmapLimit}");
                R(QualitySettings.globalTextureMipmapLimit == 0, "Play: textures at full resolution (global mipmap limit 0)");
            }
            sb.AppendLine($"GPU memory of the textures_v2 set: {bytes / 1048576f:F1} MB (one level is loaded at a time: see README)");
            string s = (fails == 0 ? "TEXTURES V2 CHECK PASS\n" : $"TEXTURES V2 CHECK: {fails} FAIL\n") + sb;
            if (fails == 0) Debug.Log("[textures_v2] " + s); else Debug.LogError("[textures_v2] " + s);
            return s;
        }
    }
}
