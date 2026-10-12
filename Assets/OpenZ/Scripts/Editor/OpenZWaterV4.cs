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
    /// a Check and a rollback to v3.
    /// Only M_Water (L02 river + bridge spans) changes; M_Water_Harbour (L17, approved muddy olive) is never touched.
    /// The choice is stored in the project (Materials/Env/WaterV4/water_v4_choice.txt) so OpenZSetup / the Water v3 menus keep
    /// it (OpenZWaterV3.ApplyPreset asks <see cref="Installed"/> first, see related_fixes).
    /// </summary>
    public static class OpenZWaterV4
    {
        const string MatEnv = "Assets/OpenZ/Materials/Env";
        const string VarDir = MatEnv + "/WaterV4";
        const string TexDir = "Assets/OpenZ/Textures/WaterV4";
        const string FlowTex = "Assets/OpenZ/Textures/Water/T_WaterFlow_L02.png";         // water v3 flow map, same rect
        const string BedMapTex = TexDir + "/T_WaterBedMap_L02.png";
        const string ShaderName = "OpenZ/WaterV4";
        const string V3ShaderName = "OpenZ/WaterStylized";
        const string ChoiceFile = VarDir + "/water_v4_choice.txt";
        const string BackupMat = VarDir + "/M_Water_v3_backup.mat";
        const string WetShaderName = "OpenZ/WaterV4Wet";
        const string WetMatPath = VarDir + "/M_Water_L02_v4_Wet.mat";
        const string WetMeshPath = "Assets/OpenZ/Models/Generated/L02_ShoreWetBand.asset";
        const string WetObjName = "L02_ShoreWetBand";   // never "WAT_": ZoneMarkers, OpenZWading, FX, scorch decals and AmbientLife take the water level as the max bounds.min.y of every WAT_* renderer
        const string L02Scene = "L02_Desert";
        const float WetCell = 0.5f;          // proxy mesh cell (m)
        const float WetLift = 0.62f;         // proxy plane above the water (the bank top is +0.35): a coverage proxy only
        static readonly Vector4 BedRect = new Vector4(-24f, -204f, 192f, 228f);

        // ------------------------------------------------------------------ menu
        [MenuItem("OpenZ/Water v4/1 Apply (URP settings + textures + variants, L02 river = A)")]
        public static void ApplyAll()
        {
            var log = new StringBuilder("[OpenZ] Water v4 apply\n");
            ApplyUrpSettings(log);
            ApplyTextureImport(log);
            BuildVariantMaterials(log);
            UseVariant("A", log);
            BuildWetMaterial(log);
            if (IsL02Open()) EnsureWetBand(log, true, true); else log.Append("  wet sand band: open L02_Desert and run 'OpenZ > Water v4 > 3 Wet sand band'\n");
            AssetDatabase.SaveAssets();
            log.Append(CheckText());
            Debug.Log(log.ToString());
        }

        [MenuItem("OpenZ/Water v4/Use variant A: oasis (default)")]
        public static void UseA() { var log = new StringBuilder(); UseVariant("A", log); AssetDatabase.SaveAssets(); Debug.Log(log.ToString()); }

        [MenuItem("OpenZ/Water v4/Use variant B: oasis deeper")]
        public static void UseB() { var log = new StringBuilder(); UseVariant("B", log); AssetDatabase.SaveAssets(); Debug.Log(log.ToString()); }

        [MenuItem("OpenZ/Water v4/2 Check")]
        public static void Check() => Debug.Log(CheckText());

        [MenuItem("OpenZ/Water v4/3 Wet sand band (build in the open L02 scene)")]
        public static void WetBand()
        {
            var log = new StringBuilder("[OpenZ] Water v4 wet sand band\n");
            BuildWetMaterial(log);
            if (IsL02Open()) EnsureWetBand(log, true, true); else log.Append("  open L02_Desert first\n");
            AssetDatabase.SaveAssets();
            Debug.Log(log.ToString());
        }

        [MenuItem("OpenZ/Water v4/Rollback: L02 river back to water v3")]
        public static void Rollback()
        {
            WriteChoice("v3");
            var m = AssetDatabase.LoadAssetAtPath<Material>($"{MatEnv}/M_Water.mat");
            var bak = AssetDatabase.LoadAssetAtPath<Material>(BackupMat);
            if (m == null) { Debug.LogWarning("[OpenZ] Water v4 rollback: M_Water.mat missing"); return; }
            Undo.RecordObject(m, "Water v4 rollback");
            if (bak != null) { m.shader = bak.shader; m.CopyPropertiesFromMaterial(bak); m.shaderKeywords = bak.shaderKeywords; }
            else OpenZWaterV3.ApplyPreset(m, false);
            m.renderQueue = -1;
            if (IsL02Open()) { var wo = GameObject.Find(WetObjName); if (wo) { var sc = wo.scene; Undo.DestroyObjectImmediate(wo); EditorSceneManager.MarkSceneDirty(sc); if (!string.IsNullOrEmpty(sc.path)) EditorSceneManager.SaveScene(sc); } }
            EditorUtility.SetDirty(m); AssetDatabase.SaveAssets();
            Debug.Log($"[OpenZ] Water v4: M_Water is back on {(m.shader ? m.shader.name : "?")} ({(bak ? "from the backup" : "v3 preset")})");
        }

        // ------------------------------------------------------------------ public API (OpenZWaterV3.ApplyPreset calls this)
        /// <summary>True once v4 was applied and not rolled back (the choice file names a variant) and the shader exists.</summary>
        public static bool Installed => Shader.Find(ShaderName) != null && "AB".Contains(Choice()) && Choice().Length == 1;

        /// <summary>The chosen L02 variant ("A" default or "B" deeper), "v3" after a rollback, "" before the first Apply.</summary>
        public static string Choice() => File.Exists(ChoiceFile) ? File.ReadAllText(ChoiceFile).Trim() : "";

        /// <summary>Writes the chosen v4 variant into an L02 water material (shader, textures, keywords, numbers).</summary>
        public static void ApplyL02(Material m)
        {
            ApplyVariant(m, Installed ? Choice() : "A");
            // OpenZSetup's environment rebuild comes through here: keep the wet sand band in a (re)built L02 scene
            if (Installed && IsL02Open() && GameObject.Find(WetObjName) == null)
            {
                var log = new StringBuilder();
                BuildWetMaterial(log); EnsureWetBand(log, false, false);        // the setup saves its own scene
            }
        }

        public static void ApplyVariant(Material m, string v)
        {
            if (m == null) return;
            var sh = Shader.Find(ShaderName);
            if (sh == null) { Debug.LogWarning($"[OpenZ] Water v4: {ShaderName} not found (copy Shaders/OpenZWaterV4.shader first)"); return; }
            if (m.shader != sh) m.shader = sh;
            Common(m);
            switch (v)
            {
                case "A": A(m); break;
                case "B": B(m); break;
                default: A(m); break;
            }
            Textures(m);
            m.renderQueue = -1;                                                // shader queue: Transparent-10
        }

        // ------------------------------------------------------------------ steps
        static void UseVariant(string v, StringBuilder log)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>($"{MatEnv}/M_Water.mat");
            if (m == null) { log.Append($"  MISSING {MatEnv}/M_Water.mat\n"); return; }
            Directory.CreateDirectory(VarDir);
            if (AssetDatabase.LoadAssetAtPath<Material>(BackupMat) == null && m.shader != null && m.shader.name == V3ShaderName)
            {
                AssetDatabase.CopyAsset($"{MatEnv}/M_Water.mat", BackupMat);
                log.Append($"  backup of the live v3 M_Water: {BackupMat}\n");
            }
            WriteChoice(v);
            Undo.RecordObject(m, "Water v4 variant");
            ApplyVariant(m, v);
            EditorUtility.SetDirty(m);
            log.Append($"  M_Water (L02 river + bridge spans) = variant {v}: {Label(v)}\n");
        }

        static string Label(string v)
        {
            switch (v)
            {
                case "A": return "A oasis (default): clear sand shallows, bright turquoise, a deep blue channel with darker patches, white wave lines";
                case "B": return "B oasis deeper: the same colours, a wider and darker blue channel, a narrower turquoise shelf";
                default: return v;
            }
        }

        static void WriteChoice(string v)
        {
            Directory.CreateDirectory(VarDir);
            File.WriteAllText(ChoiceFile, v + "\n");
            AssetDatabase.ImportAsset(ChoiceFile);
        }

        static void BuildVariantMaterials(StringBuilder log)
        {
            var sh = Shader.Find(ShaderName);
            if (sh == null) { log.Append($"  {ShaderName} not found: copy Shaders/OpenZWaterV4.shader first\n"); return; }
            Directory.CreateDirectory(VarDir);
            foreach (var v in new[] { "A", "B" })
            {
                string path = $"{VarDir}/M_Water_L02_v4_{v}.mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null) { mat = new Material(sh); AssetDatabase.CreateAsset(mat, path); }
                ApplyVariant(mat, v);
                EditorUtility.SetDirty(mat);
                log.Append($"  {path}: {Label(v)}\n");
            }
        }

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
                Undo.RecordObject(a, "Water v4 URP settings");
                a.supportsCameraDepthTexture = true;
                a.supportsCameraOpaqueTexture = true;
                var so = new SerializedObject(a);
                var ds = so.FindProperty("m_OpaqueDownsampling");
                if (ds != null) { ds.intValue = 0; so.ApplyModifiedPropertiesWithoutUndo(); }
                EditorUtility.SetDirty(a);
                log.Append($"  URP {a.name}: depth texture ON, opaque texture ON, opaque downsampling NONE\n");
            }
        }

        // ------------------------------------------------------------------ wet sand band (OpenZ/WaterV4Wet on a proxy mesh)
        static bool IsL02Open() => EditorSceneManager.GetActiveScene().name == L02Scene;

        static void BuildWetMaterial(StringBuilder log)
        {
            var sh = Shader.Find(WetShaderName);
            if (sh == null) { log.Append($"  {WetShaderName} not found: copy Shaders/OpenZWaterV4Wet.shader first\n"); return; }
            Directory.CreateDirectory(VarDir);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(WetMatPath);
            if (mat == null) { mat = new Material(sh); AssetDatabase.CreateAsset(mat, WetMatPath); }
            if (mat.shader != sh) mat.shader = sh;
            WetCommon(mat);
            mat.SetTexture("_BedMap", AssetDatabase.LoadAssetAtPath<Texture2D>(BedMapTex));
            mat.SetFloat("_WaterLevel", WaterLevel());
            mat.renderQueue = -1;
            EditorUtility.SetDirty(mat);
            log.Append($"  {WetMatPath}: wet sand band material (water level {WaterLevel():0.00} m)\n");
        }

        static float WaterLevel()
        {
            var env = GameObject.Find("Environment");
            var w = env ? env.transform.Find("WAT_Water") : null;
            return w && w.GetComponent<Renderer>() ? w.GetComponent<Renderer>().bounds.min.y : -0.35f;
        }

        /// <summary>The proxy mesh: 0.5 m cells of the bed map within the wet band (+ a margin for the 55 deg parallax), a
        /// flat plane WetLift above the water (or 0.25 m above higher ground). It only tells the GPU which pixels to test.</summary>
        static Mesh BuildWetMesh(out int cells)
        {
            cells = 0;
            if (!File.Exists(BedMapTex)) return null;
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            tex.LoadImage(File.ReadAllBytes(BedMapTex));
            var m = AssetDatabase.LoadAssetAtPath<Material>(WetMatPath);
            Vector4 enc = m && m.HasProperty("_BedEncode") ? m.GetVector("_BedEncode") : new Vector4(1.2f, -2f, 6f, -1.6f);
            float encY = m && m.HasProperty("_BedEncodeY") ? m.GetVector("_BedEncodeY").x : 2.2f;
            Vector4 rect = m && m.HasProperty("_BedRect") ? m.GetVector("_BedRect") : BedRect;
            float width = m && m.HasProperty("_WetWidth") ? m.GetFloat("_WetWidth") : 0.9f;
            float waterY = WaterLevel();
            int nx = Mathf.RoundToInt(rect.z / WetCell), nz = Mathf.RoundToInt(rect.w / WetCell);
            Color S(float x, float z) => tex.GetPixelBilinear((x - rect.x) / rect.z, (z - rect.y) / rect.w);
            var vIndex = new Dictionary<int, int>();
            var verts = new List<Vector3>(); var tris = new List<int>();
            int V(int i, int j)
            {
                int key = j * (nx + 1) + i;
                if (vIndex.TryGetValue(key, out int id)) return id;
                float x = rect.x + i * WetCell, z = rect.y + j * WetCell;
                float ground = enc.w + S(x, z).b * encY;
                verts.Add(new Vector3(x, Mathf.Max(waterY + WetLift, ground + 0.25f), z));
                vIndex[key] = verts.Count - 1;
                return verts.Count - 1;
            }
            for (int j = 0; j < nz; j++)
                for (int i = 0; i < nx; i++)
                {
                    float x = rect.x + (i + 0.5f) * WetCell, z = rect.y + (j + 0.5f) * WetCell;
                    float sd = enc.y + S(x, z).g * enc.z;                    // + in the water
                    if (sd < -(width + 0.9f) || sd > 0.9f) continue;
                    int a = V(i, j), b = V(i + 1, j), c = V(i + 1, j + 1), d = V(i, j + 1);
                    tris.Add(a); tris.Add(c); tris.Add(b); tris.Add(a); tris.Add(d); tris.Add(c);      // facing up
                    cells++;
                }
            Object.DestroyImmediate(tex);
            var mesh = new Mesh { name = "L02_ShoreWetBand", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(verts); mesh.SetTriangles(tris, 0);
            mesh.SetNormals(Enumerable.Repeat(Vector3.up, verts.Count).ToList());
            mesh.RecalculateBounds();
            return mesh;
        }

        static void EnsureWetBand(StringBuilder log, bool rebuildMesh, bool save)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(WetMatPath);
            if (mat == null) { log.Append("  wet band: material missing\n"); return; }
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(WetMeshPath);
            if (mesh == null || rebuildMesh)
            {
                var built = BuildWetMesh(out int cells);
                if (built == null) { log.Append("  wet band: bed map missing\n"); return; }
                Directory.CreateDirectory(Path.GetDirectoryName(WetMeshPath));
                if (mesh == null) { AssetDatabase.CreateAsset(built, WetMeshPath); mesh = built; }
                else { EditorUtility.CopySerialized(built, mesh); mesh.name = "L02_ShoreWetBand"; EditorUtility.SetDirty(mesh); Object.DestroyImmediate(built); }
                log.Append($"  {WetMeshPath}: {cells} cells of {WetCell} m, {mesh.vertexCount} vertices\n");
            }
            var go = GameObject.Find(WetObjName);
            if (go == null)
            {
                go = new GameObject(WetObjName);
                Undo.RegisterCreatedObjectUndo(go, "Water v4 wet band");
            }
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); go.transform.localScale = Vector3.one;
            go.isStatic = false;                                               // never baked or batched into the terrain
            var mf = go.GetComponent<MeshFilter>(); if (!mf) mf = go.AddComponent<MeshFilter>();          // no ?? on Unity objects
            var mr = go.GetComponent<MeshRenderer>(); if (!mr) mr = go.AddComponent<MeshRenderer>();
            mf.sharedMesh = mesh; mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off; mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            mr.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion; mr.allowOcclusionWhenDynamic = false;
            EditorSceneManager.MarkSceneDirty(go.scene);
            if (save && !string.IsNullOrEmpty(go.scene.path)) EditorSceneManager.SaveScene(go.scene);     // only the L02 scene, never a dialog
            log.Append($"  {WetObjName} in {go.scene.name}: the wet sand band{(save ? " (scene saved)" : "")}\n");
        }

        // name, normal map, sRGB, mips, filter, aniso, wrap, compression, max size
        static readonly (string file, bool normal, bool srgb, bool mips, FilterMode filter, int aniso, TextureWrapMode wrap, TextureImporterCompression comp, int max)[] Tex =
        {
            ("T_WaterV4_Normal.png", true, false, true, FilterMode.Trilinear, 8, TextureWrapMode.Repeat, TextureImporterCompression.CompressedHQ, 512),
            ("T_WaterV4_Noise.png", false, false, true, FilterMode.Bilinear, 0, TextureWrapMode.Repeat, TextureImporterCompression.Uncompressed, 256),
            ("T_RiverBed_Albedo.png", false, true, true, FilterMode.Trilinear, 8, TextureWrapMode.Repeat, TextureImporterCompression.CompressedHQ, 1024),
            ("T_RiverBed_Normal.png", false, false, true, FilterMode.Trilinear, 8, TextureWrapMode.Repeat, TextureImporterCompression.CompressedHQ, 1024),
            ("T_RiverBed_Decals.png", false, true, true, FilterMode.Trilinear, 8, TextureWrapMode.Clamp, TextureImporterCompression.CompressedHQ, 1024),
            ("T_RiverBed_DecalsN.png", false, false, true, FilterMode.Trilinear, 8, TextureWrapMode.Clamp, TextureImporterCompression.CompressedHQ, 1024),
            ("T_WaterBedMap_L02.png", false, false, false, FilterMode.Bilinear, 0, TextureWrapMode.Clamp, TextureImporterCompression.Uncompressed, 1024),
        };

        static void ApplyTextureImport(StringBuilder log)
        {
            foreach (var t in Tex)
            {
                string p = $"{TexDir}/{t.file}";
                if (!(AssetImporter.GetAtPath(p) is TextureImporter ti)) { log.Append($"  MISSING {p}\n"); continue; }
                ti.textureType = t.normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                ti.sRGBTexture = t.srgb; ti.mipmapEnabled = t.mips; ti.filterMode = t.filter; ti.anisoLevel = t.aniso;
                ti.wrapMode = t.wrap; ti.textureCompression = t.comp; ti.maxTextureSize = t.max;
                ti.alphaSource = t.normal ? TextureImporterAlphaSource.None : TextureImporterAlphaSource.FromInput;
                ti.alphaIsTransparency = false;                                  // straight alpha, colours already bled
                ti.npotScale = t.file.StartsWith("T_WaterBedMap") ? TextureImporterNPOTScale.None : TextureImporterNPOTScale.ToNearest;
                ti.SaveAndReimport();
                log.Append($"  {p}: {(t.normal ? "normal map" : t.srgb ? "sRGB" : "linear")}, {t.wrap}, {t.filter}, {(t.mips ? "mips" : "no mips")}, {t.comp}\n");
            }
        }

        static void Textures(Material m)
        {
            Texture2D T(string f) => AssetDatabase.LoadAssetAtPath<Texture2D>(f);
            m.SetTexture("_RippleNormal", T($"{TexDir}/T_WaterV4_Normal.png"));
            m.SetTexture("_FlowMap", T(FlowTex));
            m.SetTexture("_BedMap", T(BedMapTex));
            m.SetTexture("_BedAlbedo", T($"{TexDir}/T_RiverBed_Albedo.png"));
            m.SetTexture("_BedNormal", T($"{TexDir}/T_RiverBed_Normal.png"));
            m.SetTexture("_Decals", T($"{TexDir}/T_RiverBed_Decals.png"));
            m.SetTexture("_DecalsN", T($"{TexDir}/T_RiverBed_DecalsN.png"));
            m.SetTexture("_NoiseTex", T($"{TexDir}/T_WaterV4_Noise.png"));
            if (m.GetTexture("_FlowMap") == null) { F(m, "_UseFlowMap", 0f); K(m, "_WATER_FLOWMAP", false); }         // no v3 flow map: uniform drift
            if (m.GetTexture("_BedMap") == null) { F(m, "_UseBedMap", 0f); K(m, "_WATER_BEDMAP", false); }
        }

        static void C(Material m, string p, float r, float g, float b, float a = 1f) { if (m.HasProperty(p)) m.SetColor(p, new Color(r, g, b, a)); }
        static void V(Material m, string p, float x, float y, float z, float w) { if (m.HasProperty(p)) m.SetVector(p, new Vector4(x, y, z, w)); }
        static void F(Material m, string p, float v) { if (m.HasProperty(p)) m.SetFloat(p, v); }
        static void K(Material m, string kw, bool on) { if (on) m.EnableKeyword(kw); else m.DisableKeyword(kw); }

        // ------------------------------------------------------------------ presets (presets/water_v4_presets.json)
        static void Common(Material m)
        {
            F(m, "_AddLightStrength", 1f);
            F(m, "_AdditionalLights", 1f); K(m, "_WATER_NO_ADDITIONAL_LIGHTS", false);
            F(m, "_AmbientStrength", 0.6f);
            F(m, "_BandBreakup", 0.5f);
            F(m, "_BandOpacity", 0.95f);
            F(m, "_BandReach", 1.1f);
            F(m, "_BandSpacing", 0.45f);
            F(m, "_BandSpeed", 0.22f);
            F(m, "_BandTailM", 0.08f);
            F(m, "_BandWidthM", 0.11f);
            F(m, "_BandWobble", 0.35f);
            F(m, "_BedBlendDepth", 0.3f);            // shore_band_fix 04: the real (fixed) bank shows through the first 0.3 m of depth
            F(m, "_BedDetail", 1f);
            F(m, "_BedDetailOn", 1f); K(m, "_WATER_BED_DETAIL", true);
            V(m, "_BedEncode", 1.2f, -2f, 6f, -1.6f);
            V(m, "_BedEncodeY", 2.2f, 0f, 0f, 0f);
            V(m, "_BedRect", -24f, -204f, 192f, 228f);
            F(m, "_BedRelief", 1f);
            C(m, "_BedSandColor", 1f, 1f, 1f, 1f);
            C(m, "_BedSiltColor", 0.62f, 0.72f, 0.72f, 1f);
            F(m, "_BedTile", 6f);
            F(m, "_BedTintDepth", 0.5f);
            F(m, "_BedTintGain", 1f);
            F(m, "_BedTintMaxLum", 1f);
            F(m, "_CausticsCurl", 0.9f);
            F(m, "_CausticsFadeIn", 0.06f);
            F(m, "_CausticsFadeOut", 1f);
            F(m, "_CausticsGlow", 0.25f);
            F(m, "_CausticsMorph", 0.6f);
            F(m, "_CausticsPatch", 8f);
            F(m, "_CausticsScale", 1.7f);
            F(m, "_CausticsSpeed", 0.12f);
            F(m, "_CausticsStrength", 0.3f);
            F(m, "_CausticsSurface", 0f);
            F(m, "_CausticsWarp", 0.12f);
            F(m, "_CausticsWidth", 0.04f);
            F(m, "_ChannelDepth", 1f);
            F(m, "_DecalCell", 2.4f);
            F(m, "_DecalShadow", 0.5f);
            F(m, "_DecalSize", 2.2f);
            F(m, "_DeepDepth", 1.2f);
            F(m, "_DeepPatch", 0.5f);
            F(m, "_DeepPatchScale", 2.2f);
            F(m, "_DuskDarken", 0.75f);
            F(m, "_EdgeFade", 0.012f);
            F(m, "_FallbackDepth", 0.7f);
            F(m, "_FlowCycle", 6f);
            F(m, "_FlowDirection", 90f);
            V(m, "_FlowRect", -24f, -204f, 192f, 228f);
            F(m, "_FoamBright", 1f);
            C(m, "_FoamColor", 0.84f, 0.87f, 0.84f, 0.92f);
            F(m, "_FoamGapScale", 8f);
            F(m, "_FoamLace", 0.55f);
            F(m, "_FoamLineGaps", 0.4f);
            F(m, "_FoamLineM", 0.13f);
            F(m, "_FoamScale", 1.6f);
            F(m, "_FoamScallop", 0.5f);
            F(m, "_FresnelPower", 4f);
            F(m, "_LapSpeed", 1.3f);
            F(m, "_LightNeutral", 0.85f);
            F(m, "_MidDepth", 0.45f);
            F(m, "_NightDarken", 0.5f);
            F(m, "_NightDesaturate", 0.7f);
            F(m, "_NoDepth", 0f); K(m, "_WATER_NO_DEPTH", false);
            F(m, "_NoOpaque", 0f); K(m, "_WATER_NO_OPAQUE", false);
            F(m, "_ObjectFoam", 0.16f);
            F(m, "_ObjectRing", 0.22f);
            F(m, "_PebbleDensity", 0.04f);
            F(m, "_ReflectionF0", 0.05f);
            F(m, "_ReflectionStrength", 0.45f);
            F(m, "_Refraction", 0.12f);
            F(m, "_RippleScaleA", 4.5f);
            F(m, "_RippleScaleB", 1.9f);
            F(m, "_RippleSpeed", 0.22f);
            F(m, "_RippleStrengthA", 0.55f);
            F(m, "_RippleStrengthB", 0.5f);
            F(m, "_RockDensity", 0.16f);
            F(m, "_ShadowStrength", 0.7f);
            F(m, "_SheenBlur", 0.8f);
            F(m, "_SheenFadeHeight", 160f);
            F(m, "_SheenIntensity", 0.012f);
            F(m, "_SheenPatchCover", 0.55f);
            F(m, "_SheenPatchScale", 10f);
            F(m, "_SheenScale", 5f);
            F(m, "_SheenSlope", 1f);
            F(m, "_SheenSoftness", 0.06f);
            F(m, "_SheenThreshold", 0.075f);
            F(m, "_SheenWindAngle", 35f);
            F(m, "_ShelfScale", 0.25f);
            F(m, "_ShelfWidth", 1.8f);
            C(m, "_SkyColor", 0.88f, 0.95f, 1f, 1f);
            F(m, "_SurfaceNoise", 0.06f);
            F(m, "_SurfaceNoiseScale", 7f);
            F(m, "_TerraceLip", 0.06f);
            F(m, "_TerraceReach", 2.4f);
            F(m, "_TerraceRelief", 0.6f);
            F(m, "_TerraceSpacing", 0.55f);
            F(m, "_TerraceWobble", 0.6f);
            F(m, "_TerraceWobbleScale", 3.5f);
            F(m, "_UseBedMap", 1f); K(m, "_WATER_BEDMAP", true);
            F(m, "_UseFlowMap", 1f); K(m, "_WATER_FLOWMAP", true);
            F(m, "_WeedDensity", 0.08f);
            F(m, "_WeedSway", 0.5f);
        }

        static void WetCommon(Material m)
        {
            V(m, "_BedEncode", 1.2f, -2f, 6f, -1.6f);
            V(m, "_BedEncodeY", 2.2f, 0f, 0f, 0f);
            V(m, "_BedRect", -24f, -204f, 192f, 228f);
            // (this method): the numbers live here and in presets/water_v4_presets.json, never only on the material.
            C(m, "_WetColor", 0.99f, 0.985f, 0.975f, 1f);
            F(m, "_WetLap", 0.35f);
            F(m, "_WetMaxHeight", 0.6f);
            F(m, "_WetWidth", 0.5f);
        }

        /// <summary>A oasis (default): clear sand shallows, bright turquoise, a deep blue channel with darker patches, white wave lines</summary>
        static void A(Material m)
        {
            C(m, "_ShallowColor", 0.1876f, 0.7056f, 0.7388f, 1f);
            C(m, "_MidColor", 0.3476f, 0.5579f, 0.6092f, 1f);
            C(m, "_DeepColor", 0.2556f, 0.434f, 0.5405f, 1f);
            C(m, "_DeepPatchColor", 0.184f, 0.3125f, 0.3892f, 1f);
            F(m, "_OpacityMax", 0.97f);
            F(m, "_Clarity", 0.343f);
            C(m, "_BedTintShallow", 0.7139f, 0.9732f, 0.9321f, 1f);
            C(m, "_BedTint", 0.485f, 0.948f, 0.7516f, 1f);
            V(m, "_Absorption", 2.276f, 0.517f, 0.362f, 0f);
            F(m, "_MidDepth", 0.45f);
            F(m, "_DeepDepth", 1.2f);
            F(m, "_ShelfWidth", 1.2f);
            F(m, "_ShelfScale", 0.3f);
            F(m, "_ChannelDepth", 0.85f);
        }

        /// <summary>B oasis deeper: the same colours, a wider and darker blue channel, a narrower turquoise shelf</summary>
        static void B(Material m)
        {
            C(m, "_ShallowColor", 0.1651f, 0.7135f, 0.7554f, 1f);
            C(m, "_MidColor", 0.3365f, 0.5248f, 0.5993f, 1f);
            C(m, "_DeepColor", 0.239f, 0.4055f, 0.5025f, 1f);
            C(m, "_DeepPatchColor", 0.1721f, 0.292f, 0.3618f, 1f);
            F(m, "_OpacityMax", 0.97f);
            F(m, "_Clarity", 0.328f);
            C(m, "_BedTintShallow", 0.7107f, 0.9707f, 0.9286f, 1f);
            C(m, "_BedTint", 0.5085f, 1f, 0.7501f, 1f);
            V(m, "_Absorption", 2.393f, 0.558f, 0.399f, 0f);
            F(m, "_MidDepth", 0.4f);
            F(m, "_DeepDepth", 1.05f);
            F(m, "_ShelfWidth", 1.15f);
            F(m, "_ShelfScale", 0.3f);
            F(m, "_ChannelDepth", 1.15f);
        }

        // ------------------------------------------------------------------ check
        public static string CheckText()
        {
            var sb = new StringBuilder("[OpenZ] Water v4 check\n");
            int bad = 0;
            void Line(bool ok, string s) { if (!ok) bad++; sb.Append(ok ? "  OK   " : "  FAIL ").Append(s).Append('\n'); }
            void Info(string s) => sb.Append("  --   ").Append(s).Append('\n');

            foreach (var a in PipelineAssets())
            {
                var so = new SerializedObject(a); var ds = so.FindProperty("m_OpaqueDownsampling");
                Line(a.supportsCameraDepthTexture, $"URP {a.name}: Depth Texture");
                Line(a.supportsCameraOpaqueTexture, $"URP {a.name}: Opaque Texture");
                Line(ds == null || ds.intValue == 0, $"URP {a.name}: Opaque Downsampling None");
            }
            var sh = Shader.Find(ShaderName);
            Line(sh != null && sh.isSupported, $"{ShaderName} found and supported");
            if (sh != null) Line(sh.passCount == 1, $"{ShaderName} has {sh.passCount} pass (WaterForward)");
            var wsh = Shader.Find(WetShaderName);
            Line(wsh != null && wsh.isSupported, $"{WetShaderName} found and supported");
            var wmat = AssetDatabase.LoadAssetAtPath<Material>(WetMatPath);
            Line(wmat != null && wmat.shader == wsh && wmat.GetTexture("_BedMap") != null, $"{WetMatPath}: OpenZ/WaterV4Wet + bed map");
            if (IsL02Open())
            {
                var wo = GameObject.Find(WetObjName);
                var wmf = wo ? wo.GetComponent<MeshFilter>() : null;
                if (Installed) Line(wo != null && wmf && wmf.sharedMesh && wmf.sharedMesh.vertexCount > 1000 && wo.GetComponent<MeshRenderer>().sharedMaterial == wmat,
                                    $"L02: {WetObjName} (wet sand band proxy) present: {(wmf && wmf.sharedMesh ? wmf.sharedMesh.vertexCount : 0)} vertices");
                else Line(wo == null, $"L02: no {WetObjName} while v4 is not active");
                if (wmat) Info($"wet band water level {wmat.GetFloat("_WaterLevel"):0.00} m (WAT_Water bounds.min.y {WaterLevel():0.00})");
            }
            else Info("wet sand band: open L02_Desert to check it");
            foreach (var t in Tex)
            {
                string p = $"{TexDir}/{t.file}";
                if (AssetImporter.GetAtPath(p) is TextureImporter ti)
                    Line(ti.sRGBTexture == t.srgb && (ti.textureType == TextureImporterType.NormalMap) == t.normal && ti.mipmapEnabled == t.mips && ti.wrapMode == t.wrap,
                         $"{t.file}: {(t.normal ? "normal map" : t.srgb ? "sRGB" : "linear")}, {t.wrap}, {(t.mips ? "mips" : "no mips")}");
                else Line(false, $"{p} exists");
            }
            Line(File.Exists(FlowTex), "v3 flow map present (the ripples follow the river)");

            var m = AssetDatabase.LoadAssetAtPath<Material>($"{MatEnv}/M_Water.mat");
            string choice = Choice();
            Info($"choice file: '{choice}' ({(Installed ? "v4 active, variant " + choice + ": " + Label(choice) : "v4 not active")})");
            if (m == null) Line(false, "M_Water.mat exists");
            else
            {
                bool v4 = m.shader == sh;
                Line(v4 == Installed, $"M_Water shader = {(m.shader ? m.shader.name : "none")}");
                if (v4)
                {
                    foreach (var p in new[] { "_RippleNormal", "_BedMap", "_BedAlbedo", "_BedNormal", "_Decals", "_DecalsN", "_NoiseTex", "_FlowMap" })
                        Line(m.GetTexture(p) != null, $"M_Water {p} assigned");
                    foreach (var kw in new[] { "_WATER_BEDMAP", "_WATER_BED_DETAIL", "_WATER_FLOWMAP" })
                        Line(m.IsKeywordEnabled(kw), $"M_Water {kw} on");
                    foreach (var kw in new[] { "_WATER_NO_OPAQUE", "_WATER_NO_DEPTH", "_WATER_NO_ADDITIONAL_LIGHTS" })
                        Line(!m.IsKeywordEnabled(kw), $"M_Water {kw} off");
                }
            }
            var h = AssetDatabase.LoadAssetAtPath<Material>($"{MatEnv}/M_Water_Harbour.mat");
            Line(h != null && h.shader != null && h.shader != sh, $"L17 M_Water_Harbour untouched (shader {(h && h.shader ? h.shader.name : "?")}, approved olive)");

            // bed map vs the open L02 scene: the map was baked from the carved TER meshes (make_bed_map.py); the rendered terrain
            // heights under the water and the river meshes must line up with it, else re-run make_bed_map.py after a re-carve
            var ters = Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None).Where(f => (f.name == "TER_RiverBed" || f.name == "TER_Ground") && f.sharedMesh && f.sharedMesh.isReadable).ToArray();
            var wats = Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None).Where(f => f.name.StartsWith("WAT_") && f.sharedMesh && f.GetComponent<Renderer>() && f.GetComponent<Renderer>().sharedMaterial == m).ToArray();
            if (ters.Length > 0 && File.Exists(BedMapTex) && m != null)
            {
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
                tex.LoadImage(File.ReadAllBytes(BedMapTex));
                Vector4 enc = m.HasProperty("_BedEncode") ? m.GetVector("_BedEncode") : new Vector4(1.2f, -2f, 6f, -1.6f);
                float encY = m.HasProperty("_BedEncodeY") ? m.GetVector("_BedEncodeY").x : 2.2f;
                Vector4 rect = m.HasProperty("_BedRect") ? m.GetVector("_BedRect") : BedRect;
                Color S(Vector3 w) => tex.GetPixelBilinear((w.x - rect.x) / rect.z, (w.z - rect.y) / rect.w);
                float waterY = wats.Length > 0 ? wats[0].GetComponent<Renderer>().bounds.min.y : -0.35f;
                var errs = new List<float>();
                foreach (var mf in ters)
                {
                    var vs = mf.sharedMesh.vertices; var tr = mf.transform;
                    foreach (var v in vs)
                    {
                        var w = tr.TransformPoint(v);
                        if (w.y > waterY + 0.3f) continue;                                   // under the water and the banks only
                        float u = (w.x - rect.x) / rect.z, vv = (w.z - rect.y) / rect.w;
                        if (u < 0 || u > 1 || vv < 0 || vv > 1) continue;
                        errs.Add(Mathf.Abs(enc.w + S(w).b * encY - w.y));
                    }
                }
                errs.Sort();
                float med = errs.Count > 0 ? errs[errs.Count / 2] : 99f, p90 = errs.Count > 0 ? errs[(int)(errs.Count * 0.9f)] : 99f;
                Line(errs.Count > 100 && med < 0.05f && p90 < 0.15f,
                     $"bed map heights match the rendered terrain under the water: median {med * 100:0.0} cm, p90 {p90 * 100:0.0} cm over {errs.Count} vertices (offline: ~1-2 cm)");
                if (wats.Length > 0)
                {
                    int n = 0, near = 0;
                    foreach (var mf in wats)
                    {
                        var vs = mf.sharedMesh.vertices; var tr = mf.transform;
                        for (int i = 0; i < vs.Length; i += Mathf.Max(1, vs.Length / 800))
                        {
                            float sd = enc.y + S(tr.TransformPoint(vs[i])).g * enc.z; n++;
                            if (sd > -2.5f) near++;
                        }
                    }
                    Line(n > 0 && near >= 0.95f * n, $"river meshes on the bed map's water: {100f * near / Mathf.Max(1, n):0}% of {n} WAT_ vertices within 2.5 m of the waterline (the river mesh is dilated by one 1 m cell)");
                }
                Object.DestroyImmediate(tex);
            }
            else Info("bed map vs terrain: open the L02 scene to test it");
            foreach (var cd in Object.FindObjectsByType<UniversalAdditionalCameraData>(FindObjectsSortMode.None))
            {
                if (cd.requiresColorOption == CameraOverrideOption.Off) Line(false, $"camera {cd.name}: Opaque Texture forced OFF (the water shows no bed there)");
                if (cd.requiresDepthOption == CameraOverrideOption.Off) Line(false, $"camera {cd.name}: Depth Texture forced OFF");
            }
            sb.Append(bad == 0 ? "  ALL OK" : $"  {bad} problem(s)");
            return sb.ToString();
        }
    }
}
