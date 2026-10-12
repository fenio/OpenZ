using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// (Depth Texture ON, Opaque Texture ON, Opaque Downsampling NONE on every pipeline asset in use), the texture import
    /// settings (ripple normal map, L02 flow map), and the two material presets in place (GUIDs kept):
    /// M_Water = L02 warm Maldives turquoise (flow map along the river), M_Water_Harbour = L17 approved muddy olive.
    /// "Check" reports every requirement, the flow map orientation against the WAT_Water mesh, cameras that override
    /// the opaque / depth textures, and the heat-shimmer global in Play mode.
    /// </summary>
    public static class OpenZWaterV3
    {
        const string MatEnv = "Assets/OpenZ/Materials/Env";
        const string TexDir = "Assets/OpenZ/Textures/Water";
        const string RippleTex = TexDir + "/T_WaterRipple_N.png";
        const string FlowTex = TexDir + "/T_WaterFlow_L02.png";
        const string ShaderName = "OpenZ/WaterStylized";
        const string LegacyShaderName = "OpenZ/WaterStylizedV2";          // optional/OpenZWaterStylizedV2.shader
        static readonly Vector4 FlowRectL02 = new Vector4(-24f, -204f, 192f, 228f);        // x0, z0, size x, size z (m)

        // ------------------------------------------------------------------ menu
        [MenuItem("OpenZ/Water v3/1 Apply (URP settings + textures + L02 and L17 presets)")]
        public static void ApplyAll()
        {
            var log = new StringBuilder("[OpenZ] Water v3 apply\n");
            ApplyUrpSettings(log);
            ApplyTextureImport(log);
            ApplyMaterials(log);
            AssetDatabase.SaveAssets();
            log.Append(CheckText());
            Debug.Log(log.ToString());
        }

        [MenuItem("OpenZ/Water v3/2 Check")]
        public static void Check() => Debug.Log(CheckText());

        [MenuItem("OpenZ/Water v3/Materials only (re-apply the presets)")]
        public static void MaterialsOnly()
        {
            var log = new StringBuilder("[OpenZ] Water v3 presets\n");
            ApplyMaterials(log);
            AssetDatabase.SaveAssets();
            Debug.Log(log.ToString());
        }

        [MenuItem("OpenZ/Water v3/Optional: keep L17 on the exact v2 look")]
        public static void KeepL17OnV2()
        {
            var sh = Shader.Find(LegacyShaderName);
            var mat = AssetDatabase.LoadAssetAtPath<Material>($"{MatEnv}/M_Water_Harbour.mat");
            if (sh == null) { Debug.LogWarning($"[OpenZ] Water v3: {LegacyShaderName} is not imported (copy optional/OpenZWaterStylizedV2.shader first)"); return; }
            if (mat == null) { Debug.LogWarning("[OpenZ] Water v3: M_Water_Harbour.mat missing"); return; }
            Undo.RecordObject(mat, "L17 water on v2");
            mat.shader = sh;
            V2Harbour(mat);
            mat.renderQueue = -1;
            EditorUtility.SetDirty(mat); AssetDatabase.SaveAssets();
            Debug.Log("[OpenZ] Water v3: M_Water_Harbour is on OpenZ/WaterStylizedV2 with the approved v2 values");
        }

        // ------------------------------------------------------------------ public API (OpenZSetup calls this)
        /// <summary>Writes the v3 preset into a material on OpenZ/WaterStylized: harbour = L17 olive, else L02 turquoise.</summary>
        public static void ApplyPreset(Material m, bool harbour)
        {
            if (m == null) return;
            if (!harbour && OpenZWaterV4.Installed) { OpenZWaterV4.ApplyL02(m); return; }   // water_v4: the L02 river stays on v4
            var sh = Shader.Find(ShaderName);
            if (sh != null && m.shader != sh) m.shader = sh;
            if (harbour) L17(m); else L02(m);
            m.renderQueue = -1;                                                // shader queue: Transparent-10
        }

        // ------------------------------------------------------------------ URP
        static IEnumerable<UniversalRenderPipelineAsset> PipelineAssets()
        {
            var set = new HashSet<UniversalRenderPipelineAsset>();
            if (GraphicsSettings.defaultRenderPipeline is UniversalRenderPipelineAsset d) set.Add(d);
            for (int i = 0; i < QualitySettings.names.Length; i++)
                if (QualitySettings.GetRenderPipelineAssetAt(i) is UniversalRenderPipelineAsset q) set.Add(q);
            return set;
        }

        static void ApplyUrpSettings(StringBuilder log)
        {
            foreach (var a in PipelineAssets())
            {
                Undo.RecordObject(a, "Water v3 URP settings");
                a.supportsCameraDepthTexture = true;
                a.supportsCameraOpaqueTexture = true;
                var so = new SerializedObject(a);
                var ds = so.FindProperty("m_OpaqueDownsampling");
                if (ds != null) { ds.intValue = 0; so.ApplyModifiedPropertiesWithoutUndo(); }   // 0 = None (full-res bed)
                EditorUtility.SetDirty(a);
                log.Append($"  URP {a.name}: depth texture ON, opaque texture ON, opaque downsampling NONE\n");
            }
        }

        // ------------------------------------------------------------------ textures
        static void ApplyTextureImport(StringBuilder log)
        {
            if (AssetImporter.GetAtPath(RippleTex) is TextureImporter n)
            {
                n.textureType = TextureImporterType.NormalMap; n.sRGBTexture = false; n.mipmapEnabled = true;
                n.wrapMode = TextureWrapMode.Repeat; n.filterMode = FilterMode.Trilinear; n.anisoLevel = 8; n.maxTextureSize = 512;
                n.textureCompression = TextureImporterCompression.CompressedHQ;
                n.SaveAndReimport(); log.Append($"  {RippleTex}: normal map, repeat, trilinear, aniso 8\n");
            }
            else log.Append($"  MISSING {RippleTex}\n");
            if (AssetImporter.GetAtPath(FlowTex) is TextureImporter fl)
            {
                fl.textureType = TextureImporterType.Default; fl.sRGBTexture = false; fl.mipmapEnabled = false;
                fl.wrapMode = TextureWrapMode.Clamp; fl.filterMode = FilterMode.Bilinear; fl.anisoLevel = 0;
                fl.npotScale = TextureImporterNPOTScale.None; fl.alphaSource = TextureImporterAlphaSource.None;
                fl.textureCompression = TextureImporterCompression.Uncompressed; fl.maxTextureSize = 512;
                fl.SaveAndReimport(); log.Append($"  {FlowTex}: linear, clamp, bilinear, uncompressed, no mips\n");
            }
            else log.Append($"  MISSING {FlowTex}\n");
        }

        // ------------------------------------------------------------------ materials
        static void ApplyMaterials(StringBuilder log)
        {
            var sh = Shader.Find(ShaderName);
            if (sh == null || sh.FindPropertyIndex("_MidColor") < 0)
            { log.Append($"  {ShaderName} is not the v3 shader (no _MidColor): copy Shaders/OpenZWaterStylized.shader first\n"); return; }
            foreach (var (name, harbour) in new[] { ("M_Water", false), ("M_Water_Harbour", true) })
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>($"{MatEnv}/{name}.mat");
                if (mat == null) { log.Append($"  MISSING {MatEnv}/{name}.mat\n"); continue; }
                Undo.RecordObject(mat, "Water v3 preset");
                ApplyPreset(mat, harbour);
                EditorUtility.SetDirty(mat);
                log.Append($"  {name}: {(harbour ? "L17 muddy olive (approved look)" : "L02 warm Maldives turquoise + flow map")}\n");
            }
        }

        static void C(Material m, string p, float r, float g, float b, float a = 1f) { if (m.HasProperty(p)) m.SetColor(p, new Color(r, g, b, a)); }
        static void F(Material m, string p, float v) { if (m.HasProperty(p)) m.SetFloat(p, v); }
        static void K(Material m, string kw, bool on) { if (on) m.EnableKeyword(kw); else m.DisableKeyword(kw); }

        static void Common(Material m, bool flow)
        {
            F(m, "_UseFlowMap", flow ? 1f : 0f); K(m, "_WATER_FLOWMAP", flow);
            F(m, "_AdditionalLights", 1f); K(m, "_WATER_NO_ADDITIONAL_LIGHTS", false);
            F(m, "_NoOpaque", 0f); K(m, "_WATER_NO_OPAQUE", false);
            F(m, "_NoDepth", 0f); K(m, "_WATER_NO_DEPTH", false);
            F(m, "_FallbackDepth", 0.6f);
            m.SetTexture("_RippleNormal", AssetDatabase.LoadAssetAtPath<Texture2D>(RippleTex));
            m.SetTexture("_FlowMap", flow ? AssetDatabase.LoadAssetAtPath<Texture2D>(FlowTex) : null);
            m.SetVector("_FlowRect", flow ? FlowRectL02 : new Vector4(0, 0, 1, 1));
        }

        /// <summary>L02: warm Maldives turquoise (clear sandy shallows, turquoise, teal middle), fitted to the game's own warm
        /// light (T19 frames) - see README_IMPORT.md "Colour calibration".</summary>
        static void L02(Material m)
        {
            C(m, "_ShallowColor", 0.377f, 1f, 1f, 1f);
            C(m, "_MidColor", 0.318f, 1f, 0.974f, 1f);
            C(m, "_DeepColor", 0f, 0.717f, 0.7f, 1f);
            F(m, "_MidDepth", 0.3f);
            F(m, "_DeepDepth", 0.72f);
            F(m, "_OpacityShallow", 0.2f);
            F(m, "_OpacityDeep", 0.577f);
            F(m, "_OpacityDepth", 0.1f);
            C(m, "_BedTint", 0.561f, 0.96f, 1f, 1f);
            F(m, "_BedTintDepth", 0.32f);
            F(m, "_SandLift", 0.3f);
            C(m, "_SandColor", 0.9f, 0.8f, 0.6f, 1f);
            F(m, "_SandDepth", 0.35f);
            F(m, "_Refraction", 0.1f);
            F(m, "_RippleScaleA", 6f);
            F(m, "_RippleScaleB", 2.6f);
            F(m, "_RippleStrengthA", 0.32f);
            F(m, "_RippleStrengthB", 0.2f);
            F(m, "_RippleSpeed", 0.12f);
            F(m, "_RippleShade", 0.8f);
            F(m, "_FlowDirection", 90f);
            F(m, "_FlowCycle", 8f);
            F(m, "_AmbientStrength", 0.6f);
            F(m, "_ShadowStrength", 0.75f);
            C(m, "_SkyColor", 0.85f, 0.94f, 1f, 1f);
            F(m, "_ReflectionStrength", 0.6f);
            F(m, "_ReflectionF0", 0.06f);
            F(m, "_FresnelPower", 5f);
            F(m, "_GlintPower", 900f);
            F(m, "_GlintIntensity", 1f);
            F(m, "_GlintThreshold", 0.45f);
            F(m, "_GlintSoftness", 0.08f);
            F(m, "_GlintViewBias", 0.3f);
            F(m, "_GlintSparsity", 0.55f);
            F(m, "_GlintFadeHeight", 150f);
            F(m, "_AddLightStrength", 1f);
            F(m, "_CausticsStrength", 0.8f);
            F(m, "_CausticsScale", 1f);
            F(m, "_CausticsFocus", 0.24f);
            F(m, "_CausticsSpeed", 0.6f);
            F(m, "_CausticsSplit", 0.06f);
            F(m, "_CausticsFadeIn", 0.08f);
            F(m, "_CausticsFadeOut", 1.6f);
            C(m, "_FoamColor", 0.9f, 0.95f, 0.92f, 0.45f);
            F(m, "_FoamWidth", 0.06f);
            F(m, "_FoamScale", 0.9f);
            F(m, "_FoamSpeed", 0.05f);
            F(m, "_FoamBreakup", 0.75f);
            F(m, "_FoamSoftness", 0.08f);
            F(m, "_EdgeFade", 0.02f);
            F(m, "_NightDarken", 0.675f);
            F(m, "_NightDesaturate", 1f);
            F(m, "_RippleContrast", 0f);
            Common(m, true);
        }

        /// <summary>
        /// v2 shader within 6/255 by day and night (tools/fit_l17_colours.py).</summary>
        static void L17(Material m)
        {
            C(m, "_ShallowColor", 0.298f, 0.29f, 0.188f, 1f);
            C(m, "_MidColor", 0.276f, 0.268f, 0.16f, 1f);
            C(m, "_DeepColor", 0.243f, 0.235f, 0.118f, 1f);
            F(m, "_MidDepth", 0.15f);
            F(m, "_DeepDepth", 0.6f);
            F(m, "_OpacityShallow", 0.75f);
            F(m, "_OpacityDeep", 1f);
            F(m, "_OpacityDepth", 0.3f);
            C(m, "_BedTint", 0.69f, 0.771f, 0.422f, 1f);
            F(m, "_BedTintDepth", 0.45f);
            F(m, "_SandLift", 0f);
            C(m, "_SandColor", 0.55f, 0.52f, 0.4f, 1f);
            F(m, "_SandDepth", 0.2f);
            F(m, "_Refraction", 0.04f);
            F(m, "_RippleScaleA", 8f);
            F(m, "_RippleScaleB", 3f);
            F(m, "_RippleStrengthA", 0.5f);
            F(m, "_RippleStrengthB", 0.3f);
            F(m, "_RippleSpeed", 0.05f);
            F(m, "_RippleShade", 0.5f);
            F(m, "_FlowDirection", 0f);
            F(m, "_FlowCycle", 8f);
            F(m, "_AmbientStrength", 1f);
            F(m, "_ShadowStrength", 0.6f);
            C(m, "_SkyColor", 0.55f, 0.58f, 0.55f, 1f);
            F(m, "_ReflectionStrength", 0.5f);
            F(m, "_ReflectionF0", 0.06f);
            F(m, "_FresnelPower", 5f);
            F(m, "_GlintPower", 900f);
            F(m, "_GlintIntensity", 0.7f);
            F(m, "_GlintThreshold", 0.6f);
            F(m, "_GlintSoftness", 0.08f);
            F(m, "_GlintViewBias", 0.3f);
            F(m, "_GlintSparsity", 0.6f);
            F(m, "_GlintFadeHeight", 150f);
            F(m, "_AddLightStrength", 1.2f);
            F(m, "_CausticsStrength", 0f);
            F(m, "_CausticsScale", 1f);
            F(m, "_CausticsFocus", 0.2f);
            F(m, "_CausticsSpeed", 0.4f);
            F(m, "_CausticsSplit", 0f);
            F(m, "_CausticsFadeIn", 0.08f);
            F(m, "_CausticsFadeOut", 0.6f);
            C(m, "_FoamColor", 0.66f, 0.66f, 0.56f, 0.35f);
            F(m, "_FoamWidth", 0.1f);
            F(m, "_FoamScale", 1.2f);
            F(m, "_FoamSpeed", 0.03f);
            F(m, "_FoamBreakup", 0.8f);
            F(m, "_FoamSoftness", 0.1f);
            F(m, "_EdgeFade", 0.03f);
            F(m, "_NightDarken", 0.35f);
            F(m, "_NightDesaturate", 0.45f);
            F(m, "_RippleContrast", 0.35f);
            Common(m, false);
        }

        /// <summary></summary>
        static void V2Harbour(Material m)
        {
            C(m, "_ColorShallow", 0.298f, 0.29f, 0.188f, 0.75f); C(m, "_ColorDeep", 0.243f, 0.235f, 0.118f, 1f);
            F(m, "_DepthDistance", 0.3f); F(m, "_EdgeFade", 0.03f); F(m, "_Refraction", 0.015f);
            C(m, "_AbsorbColor", 0.5f, 0.56f, 0.3f); F(m, "_AbsorbDepth", 0.45f);
            C(m, "_ColorSky", 0.55f, 0.58f, 0.55f); F(m, "_FresnelPower", 3f); F(m, "_FresnelStrength", 0.35f);
            F(m, "_AmbientStrength", 1f); F(m, "_ShadowStrength", 0.6f);
            F(m, "_RippleTiling1", 0.3f); F(m, "_RippleTiling2", 0.8f); F(m, "_RippleSlope", 0.3f); F(m, "_RippleSpeed", 0.08f);
            F(m, "_RippleDirection", 0f); F(m, "_RippleShading", 0.35f); F(m, "_LineStrength", 0.04f); F(m, "_LineWidth", 0.04f);
            C(m, "_ColorFoam", 0.66f, 0.66f, 0.56f, 0.6f); F(m, "_FoamWidth", 0.5f); F(m, "_FoamTiling", 1.4f); F(m, "_FoamSpeed", 0.05f);
            F(m, "_FoamBreakup", 0.85f); F(m, "_FoamSoftness", 0.05f); F(m, "_FoamPulse", 0.1f); F(m, "_SurfaceFoam", 0f);
            F(m, "_SunGlintPower", 900f); F(m, "_SunGlintIntensity", 0.7f); F(m, "_SunGlintThreshold", 0.88f);
            F(m, "_SunGlintSoftness", 0.06f); F(m, "_SunGlintViewBias", 0.45f); F(m, "_AddLightStrength", 1.2f);
            F(m, "_AdditionalLights", 1f); K(m, "_WATER_NO_ADDITIONAL_LIGHTS", false);
            F(m, "_VertexWaves", 0f); K(m, "_WATER_VERTEX_WAVES", false); F(m, "_NoDepth", 0f); K(m, "_WATER_NO_DEPTH", false);
        }

        // ------------------------------------------------------------------ check
        public static string CheckText()
        {
            var sb = new StringBuilder("[OpenZ] Water v3 check\n");
            int bad = 0;
            void Line(bool ok, string s) { if (!ok) bad++; sb.Append(ok ? "  OK   " : "  FAIL ").Append(s).Append('\n'); }

            foreach (var a in PipelineAssets())
            {
                var so = new SerializedObject(a); var ds = so.FindProperty("m_OpaqueDownsampling");
                Line(a.supportsCameraDepthTexture, $"URP {a.name}: Depth Texture");
                Line(a.supportsCameraOpaqueTexture, $"URP {a.name}: Opaque Texture");
                Line(ds == null || ds.intValue == 0, $"URP {a.name}: Opaque Downsampling None (full-res bed, no blur)");
            }
            var sh = Shader.Find(ShaderName);
            Line(sh != null && sh.FindPropertyIndex("_MidColor") >= 0, $"{ShaderName} is the v3 shader");
            foreach (var (name, harbour) in new[] { ("M_Water", false), ("M_Water_Harbour", true) })
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>($"{MatEnv}/{name}.mat");
                if (mat == null) { Line(false, $"{name}.mat exists"); continue; }
                bool v3 = mat.shader == sh, legacy = mat.shader != null && mat.shader.name == LegacyShaderName;
                Line(v3 || (harbour && legacy) || (!harbour && OpenZWaterV4.Installed), $"{name} shader = {(mat.shader ? mat.shader.name : "none")}{(!harbour && OpenZWaterV4.Installed ? " (water v4 active: see OpenZ > Water v4 > 2 Check)" : "")}");
                if (!v3) continue;
                Line(mat.GetTexture("_RippleNormal") != null, $"{name} ripple normal map assigned");
                if (!harbour) Line(mat.IsKeywordEnabled("_WATER_FLOWMAP") && mat.GetTexture("_FlowMap") != null, $"{name} flow map on");
                Line(!mat.IsKeywordEnabled("_WATER_NO_OPAQUE") && !mat.IsKeywordEnabled("_WATER_NO_DEPTH"), $"{name} full path (no fallback keyword)");
            }
            if (AssetImporter.GetAtPath(RippleTex) is TextureImporter n) Line(n.textureType == TextureImporterType.NormalMap, "ripple texture imported as a normal map");
            else Line(false, $"{RippleTex} exists");
            if (AssetImporter.GetAtPath(FlowTex) is TextureImporter fl) Line(!fl.sRGBTexture && fl.wrapMode == TextureWrapMode.Clamp, "flow map linear + clamp");
            else Line(false, $"{FlowTex} exists");

            // flow map orientation: the WAT_Water vertices of the open L02 scene must land on river pixels (B = metres to bank)
            var water = Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None).Where(f => f.name.StartsWith("WAT_") && f.sharedMesh).ToArray();
            var mW = AssetDatabase.LoadAssetAtPath<Material>($"{MatEnv}/M_Water.mat");
            var riverMfs = water.Where(f => f.GetComponent<Renderer>() && f.GetComponent<Renderer>().sharedMaterial == mW).ToArray();
            if (riverMfs.Length > 0 && File.Exists(FlowTex))
            {
                // B = metres to the bank / 3, zero on land (RG is extrapolated onto land, so it cannot tell). The WAT_Water
                // mesh is dilated by one cell, so ~half its vertices are on the bank: compare the real placement with the
                // three mirrored ones; the real one must win clearly (Z maps are nearly mirror-symmetric in x).
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
                tex.LoadImage(File.ReadAllBytes(FlowTex));
                var pts = new List<Vector3>();
                foreach (var mf in riverMfs)
                {
                    var vs = mf.sharedMesh.vertices; var tr = mf.transform;
                    for (int i = 0; i < vs.Length; i += Mathf.Max(1, vs.Length / 600)) pts.Add(tr.TransformPoint(vs[i]));
                }
                // the material's own rect (README step 4 may have mirrored it: a negative size mirrors that axis)
                Vector4 rect = mW.HasProperty("_FlowRect") ? mW.GetVector("_FlowRect") : FlowRectL02;
                float Pct(Vector4 r)
                {
                    int hit = 0;
                    float sx = Mathf.Abs(r.z) < 1e-3f ? 1e-3f : r.z, sz = Mathf.Abs(r.w) < 1e-3f ? 1e-3f : r.w;
                    foreach (var w in pts)
                    {
                        float u = (w.x - r.x) / sx, v = (w.z - r.y) / sz;
                        if (u < 0 || u > 1 || v < 0 || v > 1) continue;
                        if (tex.GetPixelBilinear(u, v).b > 0.01f) hit++;
                    }
                    return pts.Count > 0 ? 100f * hit / pts.Count : 0f;
                }
                string Fmt(Vector4 r) => $"({r.x:0.##}, {r.y:0.##}, {r.z:0.##}, {r.w:0.##})";
                var rx = new Vector4(rect.x + rect.z, rect.y, -rect.z, rect.w);             // mirrored in x
                var rz = new Vector4(rect.x, rect.y + rect.w, rect.z, -rect.w);             // mirrored in z
                var rxz = new Vector4(rx.x, rz.y, rx.z, rz.w);                              // both
                float p0 = Pct(rect), px = Pct(rx), pz = Pct(rz), pxz = Pct(rxz);
                Object.DestroyImmediate(tex);
                float other = Mathf.Max(px, Mathf.Max(pz, pxz));
                Vector4 bestR = other == px ? rx : other == pz ? rz : rxz;
                bool flowOk = p0 >= 40f && p0 >= 1.3f * other;
                Line(flowOk,       // offline on the L02 terrain data: 60 % vs mirrors 41 / 19 / 34 %
                     $"flow map lines up with WAT_Water: {p0:0}% of {pts.Count} river vertices on the map's river with _FlowRect {Fmt(rect)} (mirrored x {px:0}%, z {pz:0}%, both {pxz:0}%)");
                if (!flowOk && other >= 40f && other >= 1.3f * p0)
                    sb.Append($"         -> a mirror fits: set M_Water._FlowRect to {Fmt(bestR)} in the Inspector (the Apply / Materials only menus write the default rect again), then Check again\n");
                else if (!flowOk)
                    sb.Append("         -> no placement fits: untick 'Use the flow map' on M_Water (uniform slow drift) and note it in the build log\n");
            }
            else sb.Append("  --   flow map orientation: open the L02 scene to test it against WAT_Water\n");

            // cameras in the open scenes that switch the opaque / depth texture off
            foreach (var cd in Object.FindObjectsByType<UniversalAdditionalCameraData>(FindObjectsSortMode.None))
            {
                if (cd.requiresColorOption == CameraOverrideOption.Off) Line(false, $"camera {cd.name}: Opaque Texture forced OFF (water shows no bed there)");
                if (cd.requiresDepthOption == CameraOverrideOption.Off) Line(false, $"camera {cd.name}: Depth Texture forced OFF");
            }
            if (Application.isPlaying)
            {
                float shimmer = Shader.GetGlobalFloat("_ShimmerAmount");
                Line(shimmer < 0.001f, $"heat shimmer global _ShimmerAmount = {shimmer:0.00} (the full-screen 'underwater' warp; Atmosphere.cs sets 0 since 27 Sep 13:17)");
            }
            sb.Append(bad == 0 ? "  ALL OK" : $"  {bad} problem(s)");
            return sb.ToString();
        }
    }
}
