using System.IO;
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Level 16 look, applied to ASSETS only (materials, textures): the built scene is never rebuilt by these menus.
    /// Ground: the layered ground (shader OpenZ/JungleGround): ten layer pictures as one texture array, three weight maps and
    /// a noise picture in Assets/OpenZ/Textures/L16/Ground, on the materials M_L16_Ground (ground, stream beds, apron) and
    /// M_Cliff_JungleRock (rock walls: the same recipe lays the cliff rock on from the side, and this one casts shadows).
    /// </summary>
    public static class OpenZL16Look
    {
        const string Dir = "Assets/OpenZ/Textures/L16/Ground";
        const string MatEnv = "Assets/OpenZ/Materials/Env";
        const string ArrayPath = Dir + "/L16_ground_layers.asset";
        static readonly string[] Layers = { "grass", "verge", "mud", "rock", "wetfloor", "road", "swampbed", "mosstop", "ruinpaving", "cliffside" };

        static void Import(string path, bool srgb, bool compressed, bool mips, bool readable, TextureWrapMode wrap, int aniso)
        {
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            if (ti == null) { Debug.LogWarning("[OpenZ L16] missing texture " + path); return; }
            ti.textureType = TextureImporterType.Default; ti.sRGBTexture = srgb; ti.alphaSource = TextureImporterAlphaSource.FromInput; ti.alphaIsTransparency = false;
            ti.npotScale = TextureImporterNPOTScale.None; ti.maxTextureSize = 4096; ti.mipmapEnabled = mips; ti.isReadable = readable;
            ti.wrapMode = wrap; ti.filterMode = mips ? FilterMode.Trilinear : FilterMode.Bilinear; ti.anisoLevel = aniso;
            ti.textureCompression = compressed ? TextureImporterCompression.CompressedHQ : TextureImporterCompression.Uncompressed;
            ti.SaveAndReimport();
        }

        /// <summary>The ten layer pictures as one array (same format and mip chain as the imported pictures).</summary>
        static Texture2DArray BuildArray()
        {
            Texture2DArray arr = null;
            for (int i = 0; i < Layers.Length; i++)
            {
                string p = $"{Dir}/L16_layer_{Layers[i]}.png";
                Import(p, true, true, true, true, TextureWrapMode.Repeat, 4);
                var t = AssetDatabase.LoadAssetAtPath<Texture2D>(p);
                if (!t) { Debug.LogError("[OpenZ L16] layer picture missing: " + p); return null; }
                if (arr == null) arr = new Texture2DArray(t.width, t.height, Layers.Length, t.format, true, false) { name = "L16_ground_layers" };
                if (t.width != arr.width || t.height != arr.height || t.format != arr.format || t.mipmapCount != arr.mipmapCount)
                { Debug.LogError($"[OpenZ L16] layer {Layers[i]} is {t.width} x {t.height} {t.format} ({t.mipmapCount} mips), the array is {arr.width} x {arr.height} {arr.format} ({arr.mipmapCount})"); return null; }
                for (int m = 0; m < t.mipmapCount; m++) arr.SetPixelData(t.GetPixelData<byte>(m), m, i);
            }
            arr.wrapMode = TextureWrapMode.Repeat; arr.filterMode = FilterMode.Trilinear; arr.anisoLevel = 4;
            arr.Apply(false, true);
            var old = AssetDatabase.LoadAssetAtPath<Texture2DArray>(ArrayPath);
            if (old) { EditorUtility.CopySerialized(arr, old); EditorUtility.SetDirty(old); Object.DestroyImmediate(arr); arr = old; }
            else AssetDatabase.CreateAsset(arr, ArrayPath);
            return arr;
        }

        [MenuItem("OpenZ/L16 Jungle/3 Ground layers (materials only, the scene stays)")]
        public static void GroundLayers()
        {
            if (!Directory.Exists(Dir)) { Debug.LogError("[OpenZ L16] " + Dir + " is missing"); return; }
            var arr = BuildArray();
            if (!arr) return;
            foreach (var n in new[] { "A", "B", "C" }) Import($"{Dir}/L16_splat_{n}.png", false, true, false, false, TextureWrapMode.Clamp, 1);
            Import($"{Dir}/L16_ground_noise.png", false, false, true, false, TextureWrapMode.Repeat, 1);
            Import($"{Dir}/L16_road_studs.png", false, false, false, false, TextureWrapMode.Clamp, 1);   // one point per map tile: read unfiltered
            { var st = (TextureImporter)AssetImporter.GetAtPath($"{Dir}/L16_road_studs.png"); if (st && st.filterMode != FilterMode.Point) { st.filterMode = FilterMode.Point; st.SaveAndReimport(); } }
            int done = 0;
            foreach (var n in new[] { "M_L16_Ground", "M_Cliff_JungleRock" })
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>($"{MatEnv}/{n}.mat");
                if (!m) { Debug.LogWarning("[OpenZ L16] material missing: " + n); continue; }
                ApplyGround(m, n == "M_Cliff_JungleRock"); done++;
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[OpenZ L16] ground layers: array {arr.width} x {arr.height} x {arr.depth} {arr.format}, {arr.mipmapCount} mips; {done} materials on OpenZ/JungleGround");
        }

        // ------------------------------------------------------------------------------------------------ map sides
        const string RimModel = "Assets/OpenZ/Models/Environment/L16_rims.fbx";
        const string RimTex = "Assets/OpenZ/Textures/L16/Rims";

        static Material RimMat(string name, string tex, bool cutout)
        {
            string p = $"{MatEnv}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>($"{RimTex}/{tex}.png");
            if (cutout)
            {   // the earth bank at the wall foot: its outer edge is cut out by the picture's own alpha
                var sh = Shader.Find("Universal Render Pipeline/Lit");
                if (!m) { m = new Material(sh); AssetDatabase.CreateAsset(m, p); }
                m.shader = sh; m.SetTexture("_BaseMap", t); m.SetColor("_BaseColor", Color.white); m.SetFloat("_Smoothness", 0.05f);
                m.SetFloat("_SpecularHighlights", 0f); m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF"); m.SetFloat("_EnvironmentReflections", 0f); m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
                m.SetFloat("_AlphaClip", 1f); m.SetFloat("_Cutoff", 0.5f); m.EnableKeyword("_ALPHATEST_ON"); m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
                m.SetOverrideTag("RenderType", "TransparentCutout");
            }
            else
            {
                var sh = Shader.Find("OpenZ/TeamLit");
                if (!m) { m = new Material(sh); AssetDatabase.CreateAsset(m, p); }
                m.shader = sh; m.SetTexture("_BaseMap", t); m.SetColor("_BaseColor", Color.white);
                m.SetFloat("_UseVertexColor", 0); m.SetFloat("_TeamMode", 0); m.SetFloat("_Smoothness", 0.05f); m.SetFloat("_Triplanar", 0);
            }
            m.enableInstancing = true; EditorUtility.SetDirty(m);
            return m;
        }

        [MenuItem("OpenZ/L16 Jungle/4 Map sides (adds L16_Rims to the open level 16 scene)")]
        public static void MapSidesMenu() => Debug.Log("[OpenZ L16] " + MapSides());

        /// <summary>The rebuilt map sides (rock wall, turf lip, jungle floor, earth bank at the wall foot, logs, the beds of the two rim
        /// falls) as one object under Environment; the old ground under them is not drawn any more (the ground materials cut it).</summary>
        public static string MapSides()
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
            if (!scene.name.StartsWith("L16")) return "open the level 16 scene first";
            var env = GameObject.Find("Environment"); if (!env) return "Environment missing";
            var mi = (ModelImporter)AssetImporter.GetAtPath(RimModel); if (mi == null) return "missing " + RimModel;
            mi.importCameras = false; mi.importLights = false; mi.importAnimation = false; mi.animationType = ModelImporterAnimationType.None;
            mi.bakeAxisConversion = true; mi.isReadable = false; mi.meshCompression = ModelImporterMeshCompression.Off; mi.addCollider = false;
            mi.materialImportMode = ModelImporterMaterialImportMode.None; mi.SaveAndReimport();
            foreach (var f in new[] { "rim_rock_wall_4096x2048", "rim_floor_4096", "rim_foot_bank_4096x512", "rim_log_bark_2048x1024", "rim_log_end_512" })
                Import($"{RimTex}/{f}.png", true, true, true, false, TextureWrapMode.Repeat, 4);
            Import($"{Dir}/L16_rim_cut.png", false, false, false, false, TextureWrapMode.Clamp, 1);
            Material wall = RimMat("M_L16_RimWall", "rim_rock_wall_4096x2048", false), floor = RimMat("M_L16_RimFloor", "rim_floor_4096", false),
                     bank = RimMat("M_L16_RimBank", "rim_foot_bank_4096x512", true), bark = RimMat("M_L16_RimLogBark", "rim_log_bark_2048x1024", false),
                     end = RimMat("M_L16_RimLogEnd", "rim_log_end_512", false);
            string bedPath = $"{MatEnv}/M_L16_RimBed.mat";
            var bed = AssetDatabase.LoadAssetAtPath<Material>(bedPath);
            if (!bed) { bed = new Material(Shader.Find("OpenZ/JungleGround")) { name = "M_L16_RimBed" }; AssetDatabase.CreateAsset(bed, bedPath); }
            ApplyGround(bed, false);
            foreach (var n in new[] { "M_L16_Ground", "M_Cliff_JungleRock" }) { var gm = AssetDatabase.LoadAssetAtPath<Material>($"{MatEnv}/{n}.mat"); if (gm) ApplyGround(gm, n == "M_Cliff_JungleRock"); }

            var old = env.transform.Find("L16_Rims"); if (old) Object.DestroyImmediate(old.gameObject);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(RimModel);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(model, env.transform); go.name = "L16_Rims";
            go.transform.localPosition = Vector3.zero; go.transform.localRotation = Quaternion.identity; go.transform.localScale = Vector3.one;
            var ground = env.transform.Find("TER_Ground"); int layer = ground ? ground.gameObject.layer : 0;
            var sb = new System.Text.StringBuilder(); Bounds all = new Bounds(); bool any = false;
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true))
            {
                r.gameObject.layer = layer;
                switch (r.name)
                {
                    case "RIM2_Wall": r.sharedMaterial = wall; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On; break;
                    case "RIM2_TurfLip": r.sharedMaterial = floor; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On; break;
                    case "RIM2_Floor": r.sharedMaterial = floor; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; break;
                    case "RIM2_FootBank": r.sharedMaterial = bank; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; break;
                    case "RIM2_Logs": { var ms = r.sharedMaterials; for (int i = 0; i < ms.Length; i++) ms[i] = i == 0 ? bark : end; r.sharedMaterials = ms; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On; break; }
                    default: r.sharedMaterial = bed; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; break;
                }
                if (!any) { all = r.bounds; any = true; } else all.Encapsulate(r.bounds);
                var mf = r.GetComponent<MeshFilter>();
                long ic = 0; if (mf && mf.sharedMesh) for (int si = 0; si < mf.sharedMesh.subMeshCount; si++) ic += mf.sharedMesh.GetIndexCount(si);
                sb.Append($" {r.name} {ic / 3}");
            }
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets();
            return $"map sides: L16_Rims under Environment, box x {all.min.x:0.0}..{all.max.x:0.0}, z {all.min.z:0.0}..{all.max.z:0.0}, y {all.min.y:0.0}..{all.max.y:0.0} (wanted x -24..232, z -280..24); triangles:{sb}";
        }

        // ------------------------------------------------------------------------------------------------ road wear
        const string CrackTex = Dir + "/L16_road_cracks.png";

        [MenuItem("OpenZ/L16 Jungle/3b Road wear (materials only, the scene stays)")]
        public static void RoadWearMenu()
        {
            Import(CrackTex, false, true, false, false, TextureWrapMode.Clamp, 1);       // plain numbers (not colour), compressed, no smaller copies, 3328 x 4096 kept
            int done = 0;
            foreach (var n in new[] { "M_L16_Ground", "M_Cliff_JungleRock", "M_L16_RimBed" })
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>($"{MatEnv}/{n}.mat");
                if (m && ApplyRoadWear(m)) done++;
            }
            AssetDatabase.SaveAssets();
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(CrackTex);
            Debug.Log($"[OpenZ L16] road wear: picture {(t ? t.width + " x " + t.height + " " + t.format : "MISSING")}, {done} materials set");
        }

        /// <summary>Cracks, broken patches, moss and broken edges on the level 16 road: one map-wide picture, read by OpenZ/JungleGround on the road layer only.</summary>
        internal static bool ApplyRoadWear(Material m)
        {
            if (!m || !m.shader || m.shader.name != "OpenZ/JungleGround" || !m.HasProperty("_CrackMask")) return false;
            var t = AssetDatabase.LoadAssetAtPath<Texture>(CrackTex);
            m.SetTexture("_CrackMask", t);
            m.SetVector("_CrackRule", new Vector4(t ? 1f : 0f, 0.75f, 0f, 1f));      // amount (0 = none), tone of a break's shaded edge, lighter crumbled rim, moss amount
            m.SetVector("_CrackEdge", new Vector4(0.25f, 0.1f, 2.4f, 0.1f));           // the picture's ramp (m), crumbs (m), road drop at a broken edge, moss edge by its amount (m)
            m.SetVector("_CrackFar", new Vector4(0.075f, 0.1f, 0.2f, 0f));             // full under, none over (ground m per screen point); reach of the rim (m)
            m.SetVector("_CrackMoss", new Vector4(0.25f, 0.27f, 0.19f, 1f));            // stored colour of the moss
            m.SetVector("_CrackEarth", new Vector4(0.26f, 0.22f, 0.14f, 0.5f));       // stored colour of the bare earth in a wide break; shadow inside the edge (m)
            EditorUtility.SetDirty(m);
            return true;
        }

        /// <summary>The layered ground on a material (also called by the environment material step, so both paths give the same).</summary>
        internal static void ApplyGround(Material m, bool castsShadows)
        {
            var sh = Shader.Find("OpenZ/JungleGround");
            var arr = AssetDatabase.LoadAssetAtPath<Texture2DArray>(ArrayPath);
            if (!sh || !arr) { Debug.LogWarning("[OpenZ L16] layered ground not ready (shader or layer array missing): " + m.name + " keeps its look"); return; }
            var paint = AssetDatabase.LoadAssetAtPath<Texture>("Assets/OpenZ/Textures/L16_ground_paint.png");
            m.shader = sh;
            m.SetTexture("_BaseMap", paint); m.SetColor("_BaseColor", Color.white); m.SetFloat("_Smoothness", 0.06f);
            m.SetTexture("_Layers", arr);
            m.SetTexture("_SplatA", AssetDatabase.LoadAssetAtPath<Texture>($"{Dir}/L16_splat_A.png"));
            m.SetTexture("_SplatB", AssetDatabase.LoadAssetAtPath<Texture>($"{Dir}/L16_splat_B.png"));
            m.SetTexture("_SplatC", AssetDatabase.LoadAssetAtPath<Texture>($"{Dir}/L16_splat_C.png"));
            m.SetTexture("_GroundNoise", AssetDatabase.LoadAssetAtPath<Texture>($"{Dir}/L16_ground_noise.png"));
            m.SetVector("_GroundRect", new Vector4(-24f, -280f, 1f / 256f, 1f / 304f));     // the terrain data: 256 x 304 m with the 24 m apron
            m.SetFloat("_LayerTile", 12.8f);
            var studs = AssetDatabase.LoadAssetAtPath<Texture>($"{Dir}/L16_road_studs.png");            // worn white studs on both road edges (made from the level grid)
            m.SetTexture("_RoadStuds", studs); m.SetVector("_StudRule", new Vector4(0.22f, 0.37f, 4f / 3f, studs ? 1f : 0f)); m.SetVector("_MapTiles", new Vector4(104f, 128f, 0f, 0f));
            m.SetColor("_StudColor", new Color32(0xD2, 0xCE, 0xC2, 0xFF));
            var cut = AssetDatabase.LoadAssetAtPath<Texture>($"{Dir}/L16_rim_cut.png");                  // where the rebuilt map sides replace the old ground
            bool sides = cut && AssetDatabase.LoadAssetAtPath<GameObject>(RimModel);
            var cliffCut = m.name == "M_Cliff_JungleRock" ? AssetDatabase.LoadAssetAtPath<Texture>($"{Dir}/L16_rim_cut_cliffs.png") : null;   // the cliff layer's own cut: drawn where the fall beds leave a gap
            m.SetTexture("_RimCut", cliffCut ? cliffCut : cut); m.SetFloat("_RimCutOn", sides && !m.name.StartsWith("M_L16_RimBed") ? 1f : 0f);
            ApplyRoadWear(m);                                                                           // wear on the road layer (the picture may be missing: then amount 0)
            m.SetShaderPassEnabled("ShadowCaster", castsShadows);
            m.enableInstancing = false;
            EditorUtility.SetDirty(m);
        }
    }
}
