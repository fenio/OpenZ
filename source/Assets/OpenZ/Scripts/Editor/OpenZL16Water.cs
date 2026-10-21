using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Level 16 water, placed into the OPEN level 16 scene (the scene is never rebuilt):
    /// - the swamp look on the material M_Water_Swamp: murky brown-green, see-through in the shallows, gone by about 1 m;
    /// - L16_WaterDressing under Environment: the rock rows of the stream steps, the stream bed stones, the water tongues of the
    ///   steps, the damp rock skin, sheets and feeder water of the four wall falls and the great far fall (one model, joined per
    ///   kind in world space, so it stands at the origin), with the component that moves the sheets and plays foam and sound;
    /// - L16_FallRocks: the rock pieces at the wall falls that stand on closed tiles or outside the map (a rock that does not
    ///   block is not put where units walk);
    /// - L16_BridgeApproach: the two road ends of the plank bridge.
    /// </summary>
    public static class OpenZL16Water
    {
        const string Dir = "Assets/OpenZ/Models/L16/Water";
        const string Tex = "Assets/OpenZ/Textures/L16/Water";
        const string MatEnv = "Assets/OpenZ/Materials/Env";
        const string Model = Dir + "/L16_water.fbx";
        const string Approach = "Assets/OpenZ/Models/Environment/BRGJ_Approach.fbx";
        const string RockDir = "Assets/OpenZ/Models/L16/Rocks";

        [System.Serializable] class GridDoc { public int width, height; public int[] classes; }

        internal static void ImportTex(string path, bool repeat = true, int max = 2048)
        {
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            if (ti == null) { Debug.LogWarning("[OpenZ L16] missing texture " + path); return; }
            ti.textureType = TextureImporterType.Default; ti.sRGBTexture = true; ti.mipmapEnabled = true; ti.wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            ti.filterMode = FilterMode.Trilinear; ti.anisoLevel = 4; ti.maxTextureSize = max; ti.textureCompression = TextureImporterCompression.CompressedHQ;
            ti.alphaSource = TextureImporterAlphaSource.FromInput; ti.alphaIsTransparency = false;
            ti.SaveAndReimport();
        }

        internal static Material LitMat(string name, string texPath, float smooth, string dir = MatEnv)
        {
            string p = $"{dir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            var sh = Shader.Find("OpenZ/TeamLit");
            if (!m) { m = new Material(sh); AssetDatabase.CreateAsset(m, p); }
            m.shader = sh; m.SetTexture("_BaseMap", string.IsNullOrEmpty(texPath) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(texPath)); m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_UseVertexColor", 0); m.SetFloat("_TeamMode", 0); m.SetFloat("_Smoothness", smooth); m.SetFloat("_Triplanar", 0);
            m.enableInstancing = true; EditorUtility.SetDirty(m);
            return m;
        }

        static Material SheetMat(string name, string texPath, float alpha, float foamStreaks)
        {
            string p = $"{MatEnv}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            var sh = Shader.Find("OpenZ/WaterfallSheet");
            if (!m) { m = new Material(sh); AssetDatabase.CreateAsset(m, p); }
            m.shader = sh; m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texPath));
            m.SetColor("_BaseColor", new Color(0.74f, 0.78f, 0.62f, 1f));        // the level's water: olive, not the desert's blue
            m.SetColor("_FoamColor", new Color(0.86f, 0.86f, 0.74f, 1f));
            m.SetFloat("_Alpha", alpha); m.SetFloat("_FoamStreaks", foamStreaks); m.SetFloat("_EdgeBright", 0.3f); m.SetFloat("_EdgeSoft", 0.12f);
            m.SetFloat("_FadeY", -100f); m.SetFloat("_FadeRange", 0.3f);        // falls at many heights in one mesh: no single lake height to fade under
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>The swamp look as the preparation wrote it for this shader (numbers of the water look sheet).</summary>
        public static string SwampLook()
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>($"{MatEnv}/M_Water_Swamp.mat");
            if (!m) return "M_Water_Swamp missing";
            m.SetColor("_ShallowColor", new Color32(112, 104, 60, 255)); m.SetColor("_MidColor", new Color32(96, 92, 52, 255)); m.SetColor("_DeepColor", new Color32(82, 80, 44, 255));
            m.SetFloat("_MidDepth", 0.4f); m.SetFloat("_DeepDepth", 1.0f);
            m.SetFloat("_OpacityShallow", 0.02f); m.SetFloat("_OpacityDeep", 0.985f); m.SetFloat("_OpacityDepth", 0.45f);
            m.SetColor("_BedTint", new Color(0.86f, 0.84f, 0.60f, 1f)); m.SetFloat("_BedTintDepth", 0.25f); m.SetFloat("_SandLift", 0f);
            m.SetFloat("_Refraction", 0.04f); m.SetFloat("_RippleStrengthA", 0.2f); m.SetFloat("_RippleStrengthB", 0.12f); m.SetFloat("_RippleSpeed", 0.05f);
            EditorUtility.SetDirty(m);
            return "swamp look on M_Water_Swamp (cover 2 % at the waterline to 98.5 % deep, 63 % at 0.45 m)";
        }

        [MenuItem("OpenZ/L16 Jungle/7 Water (swamp look, stream steps, falls, bridge road ends; open level 16 scene)")]
        public static void WaterMenu() => Debug.Log("[OpenZ L16] " + Water());

        public static string Water()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (!scene.name.StartsWith("L16")) return "open the level 16 scene first";
            var env = GameObject.Find("Environment"); if (!env) return "Environment missing";
            var sb = new System.Text.StringBuilder(SwampLook());

            // ---- the plank bridge model readable (its deck is moved a little under vehicles)
            var bi = (ModelImporter)AssetImporter.GetAtPath("Assets/OpenZ/Models/Environment/BRG_jungle.fbx");
            if (bi != null && !bi.isReadable) { bi.isReadable = true; bi.SaveAndReimport(); sb.Append("; bridge model readable"); }
            foreach (var a in new[] { "sd_waterfall_jungle_near_loop", "sd_waterfall_jungle_far_loop" })
            {
                var ai = (AudioImporter)AssetImporter.GetAtPath($"Assets/OpenZ/Resources/OpenZ/Audio/Sfx/L16/{a}.wav");
                if (ai == null) { sb.Append($" [sound {a} missing]"); continue; }
                var ss = ai.defaultSampleSettings;
                if (ss.loadType != AudioClipLoadType.CompressedInMemory) { ss.loadType = AudioClipLoadType.CompressedInMemory; ss.compressionFormat = AudioCompressionFormat.Vorbis; ss.quality = 0.7f; ai.defaultSampleSettings = ss; ai.SaveAndReimport(); }
            }

            // ---- the dressing model
            var mi = (ModelImporter)AssetImporter.GetAtPath(Model); if (mi == null) return sb + "; missing " + Model;
            mi.importCameras = false; mi.importLights = false; mi.importAnimation = false; mi.animationType = ModelImporterAnimationType.None;
            mi.bakeAxisConversion = true; mi.isReadable = false; mi.meshCompression = ModelImporterMeshCompression.Off; mi.addCollider = false;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportStandard; mi.materialLocation = ModelImporterMaterialLocation.InPrefab; mi.SaveAndReimport();
            var texOf = new Dictionary<string, (string file, float smooth)>
            {
                { "step_rock_wet", ("kit_rock_wet_1024", 0.3f) }, { "step_rock_wet_mossy", ("kit_rock_wet_mossy_1024", 0.2f) },
                { "stream_stone_tex_dry_light", ("kit_stone_dry_light_512", 0.08f) }, { "stream_stone_tex_dry_warm", ("kit_stone_dry_warm_512", 0.08f) },
                { "stream_stone_tex_mossy", ("kit_stone_mossy_512", 0.12f) }, { "stream_stone_tex_wet_dark", ("kit_stone_wet_dark_512", 0.3f) },
                { "wallfall_rock_damp", ("wallfall_rock_damp_1024", 0.25f) }, { "wallfall_rock_damp_mossy", ("wallfall_rock_damp_mossy_1024", 0.18f) },
            };
            foreach (var kv in texOf) ImportTex($"{Tex}/{kv.Value.file}.png");
            ImportTex($"{Tex}/kit_fall_sheet_streaks_tile_512.png"); ImportTex($"{Tex}/wallfall_sheet_tall_512x1024.png"); ImportTex($"{Tex}/BRG_jungle_v2_palette.png", false);
            var mats = new Dictionary<string, Material>();
            foreach (var kv in texOf) mats[kv.Key] = LitMat("M_L16_" + kv.Key, $"{Tex}/{kv.Value.file}.png", kv.Value.smooth);
            mats["fall_sheet"] = SheetMat("M_L16_FallSheet", $"{Tex}/kit_fall_sheet_streaks_tile_512.png", 0.78f, 0.5f);
            mats["wallfall_sheet"] = SheetMat("M_L16_WallFallSheet", $"{Tex}/wallfall_sheet_tall_512x1024.png", 0.66f, 0.6f);
            mats["wallfall_feeder"] = AssetDatabase.LoadAssetAtPath<Material>($"{MatEnv}/M_Water_Swamp.mat");

            var old = env.transform.Find("L16_WaterDressing"); if (old) Object.DestroyImmediate(old.gameObject);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Model), env.transform); go.name = "L16_WaterDressing";
            go.transform.localPosition = Vector3.zero; go.transform.localRotation = Quaternion.identity; go.transform.localScale = Vector3.one;
            var ground = env.transform.Find("TER_Ground"); int layer = ground ? ground.gameObject.layer : 0;
            long tris = 0; int unmapped = 0; Bounds wallSheets = new Bounds();
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true))
            {
                r.gameObject.layer = layer;
                var ms = r.sharedMaterials;
                for (int i = 0; i < ms.Length; i++)
                {
                    string key = ms[i] ? ms[i].name.Replace("EXP_", "") : "";
                    if (mats.TryGetValue(key, out var m) && m) ms[i] = m; else unmapped++;
                }
                r.sharedMaterials = ms;
                bool water = r.name.Contains("Sheets") || r.name.Contains("Feeders");
                r.shadowCastingMode = water ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On;
                if (r.name == "L16_StreamStones") r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;      // pebbles under water: no shadow pass for them
                var mf = r.GetComponent<MeshFilter>(); long ic = 0; if (mf && mf.sharedMesh) for (int si = 0; si < mf.sharedMesh.subMeshCount; si++) ic += mf.sharedMesh.GetIndexCount(si);
                tris += ic / 3; sb.Append($"; {r.name} {ic / 3}");
                if (r.name == "L16_WallFallSheets") wallSheets = r.bounds;
            }
            if (!go.GetComponent<L16WaterFx>()) go.AddComponent<L16WaterFx>();
            int places = go.GetComponentsInChildren<Transform>(true).Count(t => t.name.StartsWith("FX_") || t.name.StartsWith("SND_"));
            var f1 = go.transform.Find("FX_Foam_wall_f1");
            sb.Append($"; {tris} triangles, {places} foam / mist / sound places, {unmapped} material slots without a look");
            if (f1) sb.Append($"; foot of wall fall 1 at {f1.position.x:0.0}, {f1.position.y:0.0}, {f1.position.z:0.0} (blockout 119.0, -2.6, -63.1), sheets box y {wallSheets.min.y:0.0}..{wallSheets.max.y:0.0}");

            sb.Append("; " + FallRocks(env)); sb.Append("; " + BridgeEnds(env, layer));
            EditorSceneManager.MarkSceneDirty(scene); AssetDatabase.SaveAssets();
            return sb.ToString();
        }

        static string Piece(string data) { int i = data.IndexOf("JGH_"); return i < 0 ? null : data.Substring(i).Split('.')[0]; }

        static string FallRocks(GameObject env)
        {
            var ta = Resources.Load<TextAsset>("OpenZ/Maps/L16_grid"); if (!ta) return "level grid missing: no fall rocks";
            var grid = JsonUtility.FromJson<GridDoc>(ta.text);
            var list = OpenZL16Rocks.LoadList().Where(o => o.type == "MESH" && !o.hidden && o.data != null && o.name.StartsWith("RCK_WallFall_") && Piece(o.data) != null).ToArray();
            var oldp = env.transform.Find("L16_FallRocks"); if (oldp) Object.DestroyImmediate(oldp.gameObject);
            var parent = new GameObject("L16_FallRocks").transform; parent.SetParent(env.transform, false);
            int placed = 0, walk = 0; float worst = 0f; var fixes = new Dictionary<string, Quaternion>(); var missing = new HashSet<string>();
            foreach (var o in list)
            {
                float cx = (o.min[0] + o.max[0]) * 0.5f, cy = (o.min[1] + o.max[1]) * 0.5f; int tx = Mathf.FloorToInt(cx * 0.5f), ty = Mathf.FloorToInt(-cy * 0.5f);
                bool inMap = tx >= 0 && ty >= 0 && tx < grid.width && ty < grid.height;
                if (inMap && grid.classes[ty * grid.width + tx] != 3) { walk++; continue; }          // ground, road or swamp: units walk there
                string piece = Piece(o.data);
                var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{RockDir}/{piece}.fbx");
                var near = model ? model.transform.Find(piece) : null; var far = model ? model.transform.Find(piece + "_far") : null;
                if (!near) { missing.Add(piece); continue; }
                var m = OpenZL16Rocks.RockMat("M_L16_" + piece, $"{RockDir}/{piece}_orm.png", false, false);
                var mf = far ? OpenZL16Rocks.RockMat("M_L16_" + piece + "_far", $"{RockDir}/{piece}_far_basecolor_512.png", false, false) : null;
                if (!fixes.TryGetValue(piece, out var fix))
                {
                    float best = float.MaxValue; fix = Quaternion.identity;
                    foreach (float yaw in new[] { 0f, 180f, 90f, 270f }) { float e = OpenZL16Rocks.BoxError(o, near, Quaternion.Euler(0, yaw, 0)); if (e < best - 0.01f) { best = e; fix = Quaternion.Euler(0, yaw, 0); } }
                    fixes[piece] = fix;
                }
                OpenZL16Rocks.Place(o, out var pos, out var rot, out var scl);
                var g = new GameObject(o.name); g.transform.SetParent(parent, false); g.transform.SetPositionAndRotation(pos, rot); g.transform.localScale = scl;
                var fixT = new GameObject("Model").transform; fixT.SetParent(g.transform, false); fixT.localRotation = fix;
                var a = Object.Instantiate(near.gameObject, fixT, false); a.name = "LOD0"; a.GetComponent<MeshRenderer>().sharedMaterial = m;
                var lods = new List<LOD> { new LOD(far ? 0.3f : 0.01f, a.GetComponentsInChildren<Renderer>()) };
                if (far) { var b = Object.Instantiate(far.gameObject, fixT, false); b.name = "LOD1"; b.GetComponent<MeshRenderer>().sharedMaterial = mf; lods.Add(new LOD(0.01f, b.GetComponentsInChildren<Renderer>())); }
                var lg = g.AddComponent<LODGroup>(); lg.SetLODs(lods.ToArray()); lg.RecalculateBounds();
                worst = Mathf.Max(worst, OpenZL16Rocks.BoxError(o, a.transform, Quaternion.identity, true)); placed++;
            }
            return $"fall rocks: {placed} of {list.Length} placed (largest box difference {worst:0.00} m), {walk} left out because they stand on tiles units walk" + (missing.Count > 0 ? ", models missing: " + string.Join(", ", missing) : "");
        }

        static string BridgeEnds(GameObject env, int layer)
        {
            var mi = (ModelImporter)AssetImporter.GetAtPath(Approach); if (mi == null) return "bridge road ends: model missing";
            mi.importCameras = false; mi.importLights = false; mi.importAnimation = false; mi.animationType = ModelImporterAnimationType.None;
            mi.bakeAxisConversion = true; mi.isReadable = false; mi.addCollider = false; mi.materialImportMode = ModelImporterMaterialImportMode.None; mi.SaveAndReimport();
            var mat = LitMat("M_L16_BridgeApproach", $"{Tex}/BRG_jungle_v2_palette.png", 0.05f);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Approach);
            var oldp = env.transform.Find("L16_BridgeApproach"); if (oldp) Object.DestroyImmediate(oldp.gameObject);
            var parent = new GameObject("L16_BridgeApproach").transform; parent.SetParent(env.transform, false);
            var list = OpenZL16Rocks.LoadList(); var sb = new System.Text.StringBuilder("bridge road ends:");
            foreach (var n in new[] { "A", "B" })
            {
                var o = list.FirstOrDefault(x => x.name == "BLD_BRGJ_Approach_" + n); var src = model.transform.Find("BRGJ_Approach_" + n);
                if (o == null || !src) { sb.Append($" {n} missing ({(o == null ? "list" : "model")})"); continue; }
                float best = float.MaxValue; var fix = Quaternion.identity;
                foreach (float yaw in new[] { 0f, 180f, 90f, 270f }) { float e = OpenZL16Rocks.BoxError(o, src, Quaternion.Euler(0, yaw, 0)); if (e < best - 0.01f) { best = e; fix = Quaternion.Euler(0, yaw, 0); } }
                OpenZL16Rocks.Place(o, out var pos, out var rot, out var scl);
                var g = new GameObject("BRGJ_Approach_" + n); g.transform.SetParent(parent, false); g.transform.SetPositionAndRotation(pos, rot); g.transform.localScale = scl;
                var fixT = new GameObject("Model").transform; fixT.SetParent(g.transform, false); fixT.localRotation = fix;
                var a = Object.Instantiate(src.gameObject, fixT, false); a.name = "Mesh"; a.layer = layer;
                foreach (var r in a.GetComponentsInChildren<MeshRenderer>(true)) { var ms = r.sharedMaterials; for (int i = 0; i < ms.Length; i++) ms[i] = mat; r.sharedMaterials = ms; r.gameObject.layer = layer; }
                sb.Append($" {n} at {pos.x:0.0}, {pos.y:0.00}, {pos.z:0.0} (box difference to the blockout {OpenZL16Rocks.BoxError(o, a.transform, Quaternion.identity, true):0.00} m)");
            }
            return sb.ToString();
        }
    }
}
