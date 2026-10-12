using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// details_v2: OpenZ/Details v2/Build and Place. For the OPEN level (ZGame.level, else the scene name): import settings of
    /// the kit (Models/DetailsV2/DV2_&lt;L&gt;_Kit.fbx, atlas, water texture), materials (Materials/DetailsV2), one prefab per kit
    /// prop in Resources/OpenZ/DetailsV2 (turned so FRONT_ faces -Z, the base_props_v2 FBX facing fix; components from the kit
    /// manifest dv2_kit_&lt;L&gt;.json), then the placement (Resources/OpenZ/DetailsV2/placement_&lt;L&gt;.json) under one root
    /// "DetailsV2_&lt;L&gt;". Idempotent: its own previous root is removed first; the scene is saved. View only, no sim change.
    /// Also: Remove from open level, Report, and the Play check (shoot one of each kind + the DetailsV2 sound log).
    /// </summary>
    public static class OpenZDetailsV2
    {
        public const string ModelDir = "Assets/OpenZ/Models/DetailsV2", MatDir = "Assets/OpenZ/Materials/DetailsV2",
            PrefabDir = "Assets/OpenZ/Resources/OpenZ/DetailsV2";

        [Serializable] public class KitProp
        {
            public string id, node, kind = "sign", mat = "wood", notes;
            public float health = 3f, hitRadius = 0.6f, hitHeight = 2f, loopMin = 4f, loopMax = 30f, loopVol = 0.5f, scorch = 0.7f;
            public float lightRange = 3f, lightIntensity = 0.8f, oneShotVol = 0.35f, oneShotPitch = 1f, sway;
            public int debrisCount = 16;
            public bool blastOnly, intact;
            public string[] debris, breakClips, hitClips, oneShotClips, breakLayers;
            public string breakEvent, hitEvent, loopEvent, loopRes, oneShotEvent, lightColor, glowColor;
            public float[] oneShotEvery;
            public float phase;
            public string flapEvent;
            public string[] ripClips, flapClips, clangClips, states;
        }
        [Serializable] public class Kit { public string level, fbx, atlas, water; public KitProp[] props; }
        [Serializable] public class PItem { public string uid, id; public float x, y, z, yaw = 180f, scale = 1f, hideDecor; public bool walkable, skip; }
        [Serializable] public class PLane { public string id; public float[] from, to; }
        [Serializable] public class PFile { public string version, level; public PItem[] items; public PLane[] lanes; public float[] wind; }

        static string Clean(string n) { return OpenZ.DetailKit.Base(n); }

        static string OpenLevel()
        {
            var g = UnityEngine.Object.FindFirstObjectByType<OpenZ.ZGame>();
            if (g && !string.IsNullOrEmpty(g.level)) return g.level;
            var n = EditorSceneManager.GetActiveScene().name;
            return n.Contains("L17") ? "L17" : n.Contains("L02") ? "L02" : null;
        }

        [MenuItem("OpenZ/Details v2/Build and Place")]
        public static string BuildAndPlace()
        {
            var L = OpenLevel();
            if (L == null) { Debug.LogError("[OpenZ] details v2: open the L02 or L17 scene first"); return "FAILED (no level open)"; }
            var sb = new StringBuilder("[OpenZ] details v2 (" + L + "): ");
            var kit = ReadKit(L);
            if (kit == null || kit.props == null) { Debug.LogError("[OpenZ] details v2: " + ModelDir + "/dv2_kit_" + L + ".json missing (run apply.py --apply)"); return "FAILED (kit json)"; }
            var built = BuildPrefabs(L, kit, sb);
            foreach (var extra in ExtraKits(L)) built += BuildPrefabs(L, extra, sb);
            if (L == "L02" && OpenZPalmsV3.Ready()) { OpenZPalmsV3.Swap(false); sb.Append("palms_v3 swapped back in; "); }   // palms_v3: the kit rebuild above restored the old palms
            var placed = Place(L, sb);
            Debug.Log(sb.ToString());
            return "built " + built + ", placed " + placed + ". " + sb;
        }

        [MenuItem("OpenZ/Details v2/Remove from open level")]
        public static string Remove()
        {
            var L = OpenLevel(); if (L == null) return "no level";
            int n = RemoveRoot(L);
            var s = EditorSceneManager.GetActiveScene(); EditorSceneManager.MarkSceneDirty(s); EditorSceneManager.SaveScene(s);
            Debug.Log("[OpenZ] details v2: removed " + n + " root(s) from " + L);
            return "removed " + n;
        }

        [MenuItem("OpenZ/Details v2/Report")]
        public static string Report()
        {
            var L = OpenLevel();
            var root = L == null ? null : FindRoot(L);
            var sb = new StringBuilder("[OpenZ] details v2 report (" + (L ?? "?") + "): ");
            if (!root) { sb.Append("not placed"); Debug.Log(sb); return sb.ToString(); }
            foreach (Transform t in root.transform)
            {
                var p = t.GetComponent<OpenZ.DetailProp>();
                sb.Append(t.name).Append(p ? " [" + p.kind + (p.intact ? ", intact" : ", hp " + p.health) + "]" : "").Append(" @ ").Append(t.position.ToString("F1")).Append("; ");
            }
            Debug.Log(sb); return sb.ToString();
        }

        // ------------------------------------------------------------------------------------------ kit
        static Kit ReadKit(string L)
        {
            var p = ModelDir + "/dv2_kit_" + L + ".json";
            if (!File.Exists(p)) return null;
            return JsonUtility.FromJson<Kit>(File.ReadAllText(p));
        }

        /// <summary>
        /// dv2_kit_L17_fuelbanner.json + DV2_L17_FuelBanner_Kit.fbx); same contract, same prefab folder, one placement file.</summary>
        static List<Kit> ExtraKits(string L)
        {
            var list = new List<Kit>();
            if (!Directory.Exists(ModelDir)) return list;
            foreach (var f in Directory.GetFiles(ModelDir, "dv2_kit_" + L + "_*.json").OrderBy(x => x))
            {
                var k = JsonUtility.FromJson<Kit>(File.ReadAllText(f));
                if (k != null && k.props != null && !string.IsNullOrEmpty(k.fbx)) list.Add(k);
            }
            return list;
        }

        static int BuildPrefabs(string L, Kit kit, StringBuilder sb)
        {
            Directory.CreateDirectory(MatDir); Directory.CreateDirectory(PrefabDir);
            AssetDatabase.Refresh();
            string fbx = ModelDir + "/" + (string.IsNullOrEmpty(kit.fbx) ? "DV2_" + L + "_Kit.fbx" : kit.fbx);
            var mi = AssetImporter.GetAtPath(fbx) as ModelImporter;
            if (!mi) { sb.Append("FBX missing: " + fbx); return 0; }
            mi.globalScale = 1; mi.useFileUnits = true; mi.bakeAxisConversion = false; mi.importCameras = false; mi.importLights = false;
            mi.meshCompression = ModelImporterMeshCompression.Off; mi.importNormals = ModelImporterNormals.Import; mi.addCollider = false;
            mi.isReadable = false; mi.importAnimation = false; mi.animationType = ModelImporterAnimationType.None; mi.preserveHierarchy = true;
            bool cloth = kit.props.Any(p => p != null && p.kind == "banner");
            mi.materialImportMode = cloth ? ModelImporterMaterialImportMode.ImportViaMaterialDescription : ModelImporterMaterialImportMode.None;
            if (cloth) mi.materialLocation = ModelImporterMaterialLocation.InPrefab;
            mi.importBlendShapes = false; mi.importVisibility = false;
            mi.SaveAndReimport();
            var atlas = Tex(ModelDir + "/" + (string.IsNullOrEmpty(kit.atlas) ? "dv2_atlas_" + L + ".png" : kit.atlas), TextureWrapMode.Clamp);
            var water = string.IsNullOrEmpty(kit.water) ? null : Tex(ModelDir + "/" + kit.water, TextureWrapMode.Repeat);
            var paint = Mat("M_DV2_Paint_" + L, atlas, 0.22f, 3f, true);
            var wmat = water ? Mat("M_DV2_Water_" + L, water, 0.55f, 0f, true) : paint;

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            inst.transform.position = Vector3.zero;
            int n = 0;
            try
            {
                foreach (var p in kit.props)
                {
                    if (p == null || string.IsNullOrEmpty(p.id)) continue;
                    var node = inst.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => Clean(t.name) == p.node);
                    if (!node) { sb.Append(p.id + ": node " + p.node + " MISSING; "); continue; }
                    var root = new GameObject("DV2_" + p.id);
                    node.SetParent(root.transform, true);
                    // facing fix (base_props_v2 README section 1): turn the kit until FRONT_ points to Unity -Z (the camera side)
                    var front = node.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => Clean(t.name).StartsWith("FRONT_"));
                    float turn = 0f;
                    if (front)
                    {
                        var d = front.position - root.transform.position; d.y = 0f;
                        if (d.sqrMagnitude > 1e-6f) turn = Mathf.Round(Vector3.SignedAngle(d.normalized, Vector3.back, Vector3.up) / 90f) * 90f;
                        if (Mathf.Abs(turn) > 0.1f)
                        {
                            var q = Quaternion.AngleAxis(turn, Vector3.up);
                            node.position = q * node.position; node.rotation = q * node.rotation;
                        }
                    }
                    else Debug.LogWarning("[OpenZ] details v2: " + p.node + " has no FRONT_ marker (orientation not checked)");
                    foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                    {
                        string rn = Clean(r.name);
                        var cm = cloth ? ClothMats(r, paint) : null;
                        if (cm != null) r.sharedMaterials = cm;
                        else r.sharedMaterial = rn.StartsWith("WATER_") ? wmat : paint;
                        bool noShadow = rn.StartsWith("WATER_") || rn.StartsWith("GLOW_") || rn.StartsWith("NOSHADOW_");
                        r.shadowCastingMode = noShadow ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On;
                        r.receiveShadows = true;
                        r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.BlendProbes;
                    }
                    foreach (var c in root.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(c);
                    AddComponents(root, node, p);
                    PrefabUtility.SaveAsPrefabAsset(root, PrefabDir + "/DV2_" + p.id + ".prefab");
                    UnityEngine.Object.DestroyImmediate(root);
                    sb.Append(p.id + " ok (turned " + turn.ToString("0") + " deg); ");
                    n++;
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(inst); }
            AssetDatabase.SaveAssets();
            return n;
        }

        public const string BannerTexDir = ModelDir + "/FuelBanner";

        /// <summary>The banner FBX slots (M_FuelBanner{A,B}_{D0,D1,Flap,Strips}_{Front,Back}) as OpenZ/BannerCloth materials; other slots
        /// (dv2_atlas_L17: frame, lamps, ruin) get the level paint. Null when the renderer has no banner slot.</summary>
        static Material[] ClothMats(Renderer r, Material paint)
        {
            var src = r.sharedMaterials;
            if (src == null || !src.Any(m => m && Clean(m.name).StartsWith("M_FuelBanner"))) return null;
            var o = new Material[src.Length];
            for (int i = 0; i < src.Length; i++)
            {
                string n = src[i] ? Clean(src[i].name) : "";
                o[i] = n.StartsWith("M_FuelBanner") ? (BannerMat(n) ?? paint) : paint;
            }
            return o;
        }

        static Material BannerMat(string slot)
        {
            var parts = slot.Split('_');                                                        // M, FuelBannerA, D0, Front
            if (parts.Length < 4) { Debug.LogError("[OpenZ] fuel banner: unknown slot " + slot); return null; }
            string x = parts[1].Substring(parts[1].Length - 1), st = parts[2]; bool back = parts[3] == "Back";
            string file = st == "D0" ? "d0_weathered" : st == "D1" ? "d1_damaged" : st == "Flap" ? "flap" : "strips";
            string tp = BannerTexDir + "/oz_fuelbanner_" + x + "_" + file + (back ? "_back" : "") + ".png";
            var tex = ClothTex(tp);
            if (!tex) { Debug.LogError("[OpenZ] fuel banner: texture missing " + tp + " (run apply.py --apply)"); return null; }
            string p = MatDir + "/M_DV2_FuelBanner" + x + "_" + st + "_" + (back ? "Back" : "Front") + ".mat";
            var sh = Shader.Find("OpenZ/BannerCloth");
            if (!sh) { Debug.LogError("[OpenZ] fuel banner: shader OpenZ/BannerCloth missing"); return null; }
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (!m) { m = new Material(sh); AssetDatabase.CreateAsset(m, p); }
            m.shader = sh;
            m.SetTexture("_BaseMap", tex); m.SetTextureScale("_BaseMap", Vector2.one); m.SetTextureOffset("_BaseMap", Vector2.zero);
            F(m, "_Cutoff", 0.5f); F(m, "_MirrorU", back ? 1f : 0f); F(m, "_SwayScale", 1f); F(m, "_Cull", 2f);
            F(m, "_SwayAmp", st == "Strips" ? 0.30f : st == "Flap" ? 0.10f : 0.12f);       // strips swing as pendulums, the panel bellies
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Print import (fuel_banner_v1 README_IMPORT.md): sRGB, alpha is transparency, mipmaps that PRESERVE COVERAGE at the 0.5
        /// cut-off (holes neither close up nor blur away at the RTS distance), clamp, 2048 (prints 1:1), BC7.</summary>
        static Texture2D ClothTex(string path)
        {
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti)
            {
                ti.textureType = TextureImporterType.Default; ti.sRGBTexture = true; ti.alphaSource = TextureImporterAlphaSource.FromInput;
                ti.alphaIsTransparency = true; ti.mipmapEnabled = true; ti.mipMapsPreserveCoverage = true; ti.alphaTestReferenceValue = 0.5f;
                ti.wrapMode = TextureWrapMode.Clamp; ti.filterMode = FilterMode.Trilinear; ti.anisoLevel = 4; ti.maxTextureSize = 2048;
                ti.textureCompression = TextureImporterCompression.CompressedHQ; ti.isReadable = false;
                ti.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>The run-time bullet-hole atlas (FuelBanner.Stamp reads it on the CPU): linear, readable, uncompressed, no mips.</summary>
        static Texture2D HolesAtlas()
        {
            string path = BannerTexDir + "/oz_fuelbanner_holes_atlas.png";
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti)
            {
                ti.textureType = TextureImporterType.Default; ti.sRGBTexture = false; ti.alphaSource = TextureImporterAlphaSource.FromInput;
                ti.isReadable = true; ti.mipmapEnabled = false; ti.wrapMode = TextureWrapMode.Clamp; ti.filterMode = FilterMode.Bilinear;
                ti.maxTextureSize = 1024; ti.textureCompression = TextureImporterCompression.Uncompressed;
                ti.SaveAndReimport();
            }
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (!t) Debug.LogError("[OpenZ] fuel banner: " + path + " missing (run apply.py --apply): new shots will not cut holes");
            return t;
        }

        static Texture2D Tex(string path, TextureWrapMode wrap)
        {
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti)
            {
                ti.textureType = TextureImporterType.Default; ti.sRGBTexture = true; ti.mipmapEnabled = true; ti.wrapMode = wrap;
                ti.filterMode = FilterMode.Trilinear; ti.anisoLevel = 8; ti.maxTextureSize = 1024; ti.textureCompression = TextureImporterCompression.CompressedHQ;
                ti.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>Clean stylized TeamLit as base_props_v2 (atlas x baked vertex AO, no team paint, no grime, dark rim).</summary>
        static Material Mat(string name, Texture2D tex, float smooth, float nightClass, bool vcol)
        {
            string p = MatDir + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (!m) { m = new Material(Shader.Find("OpenZ/TeamLit")); AssetDatabase.CreateAsset(m, p); }
            m.SetTexture("_BaseMap", tex); m.SetTextureScale("_BaseMap", Vector2.one); m.SetTextureOffset("_BaseMap", Vector2.zero);
            m.SetColor("_BaseColor", Color.white);
            F(m, "_UseVertexColor", vcol ? 1 : 0); F(m, "_VertexColorSRGB", 1); F(m, "_TeamMode", 0); F(m, "_Accent", 0);
            F(m, "_Degreen", 0); F(m, "_Triplanar", 0); F(m, "_TopBlend", 0); F(m, "_Stylize", 1); F(m, "_Degrime", 0); F(m, "_FlatShade", 0);
            F(m, "_Smoothness", smooth); F(m, "_Metallic", 0);
            if (m.HasProperty("_RimColor")) m.SetColor("_RimColor", new Color(0.10f, 0.09f, 0.08f));
            F(m, "_RimPower", 3.2f); F(m, "_SoftFill", 0.62f); F(m, "_AmbientBoost", 1.25f);
            if (m.HasProperty("_KeyWarm")) m.SetColor("_KeyWarm", new Color(1.06f, 1f, 0.92f));
            if (m.HasProperty("_DarkColor")) m.SetColor("_DarkColor", Color.black);
            F(m, "_DarkThreshold", 0.12f); F(m, "_SatScale", 1f); F(m, "_ValScale", 1f); F(m, "_HueShift", 0f);
            F(m, "_NightClass", nightClass); F(m, "_SeeThru", 0f);
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", Color.black);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }
        static void F(Material m, string p, float v) { if (m.HasProperty(p)) m.SetFloat(p, v); }

        static Color[] Cols(string[] hex, Color fb)
        {
            if (hex == null || hex.Length == 0) return new[] { fb };
            return hex.Select(h => OpenZ.DetailProp.Hex(h, fb)).ToArray();
        }

        static void AddComponents(GameObject root, Transform node, KitProp p)
        {
            string k = p.kind ?? "sign";
            if (k == "tumbleweed")
            {
                node.name = "Template";
                node.gameObject.SetActive(false);                                            // the spawner clones it at run time
                var tw = root.AddComponent<OpenZ.TumbleweedRoll>();
                if (p.debris != null && p.debris.Length > 0) tw.twigs = Cols(p.debris, new Color(0.7f, 0.56f, 0.35f));
                return;
            }
            if (k == "waterfall")
            {
                var wf = root.AddComponent<OpenZ.WaterfallFx>();
                if (!string.IsNullOrEmpty(p.loopEvent)) wf.loopEvent = p.loopEvent;
                if (!string.IsNullOrEmpty(p.loopRes)) wf.loopRes = p.loopRes;
                wf.loopVol = p.loopVol; wf.loopMin = p.loopMin; wf.loopMax = p.loopMax;
                return;
            }
            if (k == "cave") return;
            var d = root.AddComponent<OpenZ.DetailProp>();
            d.id = p.id; d.kind = k; d.health = p.health; d.hitRadius = p.hitRadius; d.hitHeight = p.hitHeight; d.blastOnly = p.blastOnly; d.intact = p.intact;
            d.mat = p.mat ?? "wood"; d.debris = Cols(p.debris, new Color(0.5f, 0.4f, 0.3f)); d.debrisCount = p.debrisCount; d.scorch = p.scorch;
            d.breakEvent = p.breakEvent ?? ""; d.breakClips = p.breakClips ?? new string[0]; d.breakLayers = p.breakLayers ?? new string[0];
            d.hitEvent = p.hitEvent ?? ""; d.hitClips = p.hitClips ?? new string[0];
            d.loopEvent = p.loopEvent ?? ""; d.loopRes = p.loopRes ?? ""; d.loopMin = p.loopMin; d.loopMax = p.loopMax; d.loopVol = p.loopVol;
            d.oneShotEvent = p.oneShotEvent ?? ""; d.oneShotClips = p.oneShotClips ?? new string[0]; d.oneShotVol = p.oneShotVol; d.oneShotPitch = p.oneShotPitch;
            if (p.oneShotEvery != null && p.oneShotEvery.Length == 2) d.oneShotEvery = new Vector2(p.oneShotEvery[0], p.oneShotEvery[1]);
            d.lightColor = OpenZ.DetailProp.Hex(p.lightColor, d.lightColor); d.glowColor = OpenZ.DetailProp.Hex(p.glowColor, d.glowColor);
            d.lightRange = p.lightRange; d.lightIntensity = p.lightIntensity; d.sway = p.sway;
            if (k == "windmill") root.AddComponent<OpenZ.WindmillSpin>();
            else if (k == "traffic_light" || k == "roadworks_signal") { var tl = root.AddComponent<OpenZ.TrafficLightCycle>(); tl.phase = p.phase; d.glowAtNight = false; }
            else if (k == "neon") { root.AddComponent<OpenZ.NeonFlicker>(); d.glowAtNight = false; }
            else if (k == "hydrant") root.AddComponent<OpenZ.HydrantSpray>();
            else if (k == "banner")
            {
                var fb = root.AddComponent<OpenZ.FuelBanner>();
                if (!string.IsNullOrEmpty(p.breakEvent)) fb.ripEvent = p.breakEvent;
                if (p.ripClips != null && p.ripClips.Length > 0) fb.ripClips = p.ripClips;
                if (!string.IsNullOrEmpty(p.flapEvent)) fb.flapEvent = p.flapEvent;
                if (p.flapClips != null && p.flapClips.Length > 0) fb.flapClips = p.flapClips;
                if (p.clangClips != null && p.clangClips.Length > 0) fb.clangClips = p.clangClips;
                fb.holesAtlas = HolesAtlas();
                d.knockWobble = 0.15f;
            }
        }

        // ------------------------------------------------------------------------------------------ placement
        static GameObject FindRoot(string L)
        {
            var s = EditorSceneManager.GetActiveScene();
            return s.GetRootGameObjects().FirstOrDefault(g => g.name == "DetailsV2_" + L);
        }

        static int RemoveRoot(string L)
        {
            int n = 0;
            var s = EditorSceneManager.GetActiveScene();
            foreach (var g in s.GetRootGameObjects().Where(g => g.name == "DetailsV2_" + L).ToList())
            {
                var h = g.GetComponent<OpenZ.DetailsV2Hidden>();
                if (h) foreach (var d in h.hidden) if (d) d.SetActive(true);                   // the decor it hid comes back
                UnityEngine.Object.DestroyImmediate(g); n++;
            }
            return n;
        }

        /// <summary>Level DEC_ decor (under Environment) within r metres of a detail is switched off (e.g. the cactus in front of
        /// the cave mouth) and remembered on the root, so Remove / a rebuild brings it back.</summary>
        static void HideDecor(GameObject root, Vector3 at, float r, StringBuilder sb)
        {
            var env = GameObject.Find("Environment"); if (!env) return;
            var h = root.GetComponent<OpenZ.DetailsV2Hidden>(); if (!h) h = root.AddComponent<OpenZ.DetailsV2Hidden>();   // no ?? on Unity objects
            foreach (Transform t in env.transform)
            {
                if (!t.name.StartsWith("DEC_") || !t.gameObject.activeSelf) continue;
                var rs = t.GetComponentsInChildren<Renderer>(); if (rs.Length == 0) continue;
                var b = rs[0].bounds; foreach (var x in rs) b.Encapsulate(x.bounds);
                var c = b.center; c.y = at.y;
                if ((c - at).magnitude > r || b.size.x > 7f || b.size.z > 7f) continue;
                t.gameObject.SetActive(false); h.hidden.Add(t.gameObject);
                sb.Append("hid " + t.name + "; ");
            }
        }

        /// <summary>
        /// Zod water tiles only, but the drawn river (water_v3 / water_v4 / water_corners_v1: carved terrain under the WAT_ plane) is
        /// up to ~2 m wider than those tiles at the bridge abutments, so props passed on "dry" bank tiles that are under water now.
        /// This probe asks the scene itself: temporary MeshColliders on every active WAT_* mesh; a point is wet when the water
        /// surface above it is higher than the highest other surface (temporary colliders on the drawn TER_* terrain meshes, plus
        /// COL_Terrain, quays, decks) or nothing else is there. Tested:
        /// the prop's own ground footprint (every vertex within 0.3 m of its foot) and a ring of 'margin' metres around each
        /// sample (0.8 m for cacti = the 0.5 m wet sand band + 0.3 m, 0.3 m for everything else). Waterfall and tumbleweed
        /// prefabs are water / rolling by design and skipped. Edit mode only, nothing is kept.</summary>
        sealed class WaterProbe
        {
            public const float CactusMargin = 0.8f, ShoreMargin = 0.3f;
            readonly List<MeshCollider> temp = new List<MeshCollider>(), ground = new List<MeshCollider>();
            public static WaterProbe Begin()
            {
                var w = new WaterProbe();
                foreach (var mf in UnityEngine.Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
                {
                    if (!mf || !mf.sharedMesh || !mf.gameObject.activeInHierarchy) continue;
                    bool wat = mf.name.StartsWith("WAT_"), ter = mf.name.StartsWith("TER_");     // the drawn water / the drawn terrain
                    if (!wat && !ter) continue;
                    var r = mf.GetComponent<Renderer>(); if (!r || !r.enabled) continue;
                    var mc = mf.gameObject.AddComponent<MeshCollider>(); mc.sharedMesh = mf.sharedMesh;
                    (wat ? w.temp : w.ground).Add(mc);
                }
                Physics.SyncTransforms();
                return w;
            }
            public void End()
            {
                foreach (var c in temp) if (c) UnityEngine.Object.DestroyImmediate(c);
                foreach (var c in ground) if (c) UnityEngine.Object.DestroyImmediate(c);
                temp.Clear(); ground.Clear(); Physics.SyncTransforms();
            }
            public bool Applies(GameObject go) => temp.Count > 0 && !go.GetComponent<OpenZ.WaterfallFx>() && !go.GetComponent<OpenZ.TumbleweedRoll>();

            /// <summary>Water depth (m, &gt; 0 = wet) at (x, z), or -1 when dry.</summary>
            float Wet(float x, float z)
            {
                float water = float.NegativeInfinity, ground = float.NegativeInfinity;
                foreach (var h in Physics.RaycastAll(new Vector3(x, 300f, z), Vector3.down, 600f, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (h.collider is MeshCollider mc && temp.Contains(mc)) water = Mathf.Max(water, h.point.y);
                    else ground = Mathf.Max(ground, h.point.y);
                }
                if (float.IsNegativeInfinity(water)) return -1f;
                if (float.IsNegativeInfinity(ground)) return 9f;
                return water > ground + 0.01f ? water - ground : -1f;
            }

            /// <summary>null when the prop's ground footprint stands on dry land 'margin' metres from the water, else the reason.</summary>
            public string Test(GameObject go, float margin)
            {
                float foot = go.transform.position.y;
                var pts = new List<Vector3> { go.transform.position };
                foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (!mf.sharedMesh || mf.name.StartsWith("RUIN_")) continue;
                    Vector3[] vs;
                    try { vs = mf.sharedMesh.vertices; } catch { continue; }
                    var tr = mf.transform;
                    for (int i = 0; i < vs.Length; i += Mathf.Max(1, vs.Length / 400))
                    {
                        var w = tr.TransformPoint(vs[i]);
                        if (w.y < foot + 0.3f) pts.Add(w);
                    }
                }
                float worst = -1f; Vector3 at = go.transform.position;
                foreach (var p in pts)
                    for (int k = 0; k <= 8; k++)
                    {
                        float a = k * Mathf.PI / 4f, r = k == 0 ? 0f : margin;
                        float d = Wet(p.x + r * Mathf.Cos(a), p.z + r * Mathf.Sin(a));
                        if (d > worst) { worst = d; at = p; }
                    }
                return worst > 0f ? "stands in / within " + margin.ToString("0.0") + " m of the water (" + worst.ToString("0.00") + " m deep near " + at.ToString("F1") + ")" : null;
            }
        }

        static int Place(string L, StringBuilder sb)
        {
            var path = PrefabDir + "/placement_" + L + ".json";
            if (!File.Exists(path)) { sb.Append("placement missing: " + path); return 0; }
            var pf = JsonUtility.FromJson<PFile>(File.ReadAllText(path));
            int removed = RemoveRoot(L);
            var root = new GameObject("DetailsV2_" + L);
            int n = 0, refused = 0, wet = 0;
            var water = WaterProbe.Begin();
            foreach (var it in pf.items ?? new PItem[0])
            {
                if (it == null || it.skip) continue;
                if (it.walkable) { refused++; Debug.LogWarning("[OpenZ] details v2: " + it.uid + " sits on a walkable Zod tile: not placed"); continue; }
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "/DV2_" + it.id + ".prefab");
                if (!prefab) { sb.Append(it.uid + ": prefab DV2_" + it.id + " missing; "); continue; }
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
                go.name = string.IsNullOrEmpty(it.uid) ? it.id : it.uid;
                go.transform.position = new Vector3(it.x, it.y, it.z);
                go.transform.rotation = Quaternion.Euler(0f, it.yaw - 180f, 0f);                // FRONT_ faces -Z at 0: yaw 180 = the camera side
                go.transform.localScale = Vector3.one * (it.scale > 0f ? it.scale : 1f);
                var tw = go.GetComponent<OpenZ.TumbleweedRoll>();
                if (tw)
                {
                    var ls = (pf.lanes ?? new PLane[0]).Where(l => l != null && l.from != null && l.to != null && l.from.Length >= 2 && l.to.Length >= 2 && (string.IsNullOrEmpty(l.id) || l.id == it.id)).ToList();
                    tw.lanes = ls.Select(l => new Vector4(l.from[0], l.from[1], l.to[0], l.to[1])).ToArray();
                    if (pf.wind != null && pf.wind.Length >= 2) tw.wind = new Vector2(pf.wind[0], pf.wind[1]);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(tw);
                }
                if (water.Applies(go))
                {
                    float margin = (it.id ?? "").IndexOf("Cactus", StringComparison.OrdinalIgnoreCase) >= 0 ? WaterProbe.CactusMargin : WaterProbe.ShoreMargin;
                    string wet1 = water.Test(go, margin);
                    if (wet1 != null)
                    {
                        wet++; Debug.LogError("[OpenZ] details v2: " + go.name + " " + wet1 + ": not placed");
                        UnityEngine.Object.DestroyImmediate(go); continue;
                    }
                }
                if (it.hideDecor > 0f) HideDecor(root, go.transform.position, it.hideDecor, sb);
                n++;
            }
            water.End();
            if (wet > 0) sb.Append("REFUSED IN THE WATER: " + wet + " (see the errors); ");
            if (L == "L02") sb.Append(OpenZWaterfallV2.Apply(root.transform));
            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            sb.Append("placed " + n + " (previous roots removed: " + removed + ", refused on walkable tiles: " + refused + ", refused in the water: " + wet + "), scene saved.");
            return n;
        }

        // ------------------------------------------------------------------------------------------ play check (builder 2)
        /// <summary>OpenZ/Details v2/Play check (in Play, L02 or L17).
        /// 1. Listener test at the DEFAULT camera (review fix: the one-shot gates measure the listener rig, not the camera): the
        ///    RTS camera flies to the first traffic light (L17: relay clicks) or the wind-pump (L02: creaks) at h 70 and listens
        ///    14 s; the report lists every DetailsV2 sound logged meanwhile, with the camera height and the distance to the listener.
        /// 2. Shooting: the camera flies to the first prop of every destructible kind (30 m), which then takes hitscan shots
        ///    (DetailProp.Damage(1, from, false): the call DetailPropHub makes when a sim Hitscan segment crosses the hit
        ///    cylinder; blast-only stones take one blast) every 0.45 s until it breaks. Review fix: one kind at a time, the next
        ///    kind is framed only 2 s after the current prop broke (or gave up after 12 shots), so every break (the wind-pump's
        ///    6th shot too) happens on screen.
        /// 3. One console line: each prop BROKEN / NOT BROKEN, whether the test segment crosses its cylinder, the listener test,
        ///    and every DetailsV2 entry of the in-Play sound log since Play started, grouped.</summary>
        class ShotJob { public OpenZ.DetailProp p; public int shots; public bool crosses; public double start = -1, next, done = -1; }
        static List<ShotJob> checkJobs; static bool checkRunning; static double checkEnd = -1, listenFrom, listenTo;
        static float listenU0 = -1f, listenU1 = -1f, listenH0, listenH1; static OpenZ.DetailProp listenProp;
        const double Listen = 14.0, Arrive = 2.0, Gap = 0.45, After = 2.0;

        static List<OpenZ.FuelBanner> fbQueue; static double fbNext; static int fbStep; static float fbU0;

        /// <summary>OpenZ/Details v2/Fuel banner check (Play, L17): per banner, the RTS camera at 30 m on it, then 8 rifle-style shots
        /// through the print 0.4 s apart (each crosses the print: a new hole + the canvas knock), a splash in the outer reach (D1: the
        /// flap tears off and falls, the slap on the landing frame), 4 s later a blast in full reach (D2: strips + rip + clang). The break
        /// itself is the sim's: order a tank / Tough attack on the banner afterwards (explosives only, the Zod rule). The report lists the
        /// FuelBanner log and every DetailsV2 sound since the check started.</summary>
        [MenuItem("OpenZ/Details v2/Fuel banner check (Play)")]
        public static string FuelBannerCheck()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[OpenZ] fuel banner check: enter Play first (L17)"); return "enter Play first"; }
            fbQueue = UnityEngine.Object.FindObjectsByType<OpenZ.FuelBanner>(FindObjectsSortMode.None).Where(b => b && b.State == 0).OrderBy(b => b.name).ToList();
            if (fbQueue.Count == 0) { Debug.LogWarning("[OpenZ] fuel banner check: no intact banner in the scene (Build and Place on L17 with the C11 / C12 placement)"); return "no banner"; }
            fbStep = 0; fbNext = EditorApplication.timeSinceStartup; fbU0 = Time.unscaledTime;
            OpenZ.FuelBanner.Log.Clear();
            EditorApplication.update -= FuelBannerTick; EditorApplication.update += FuelBannerTick;
            var s = "[OpenZ] fuel banner check: " + fbQueue.Count + " banner(s): " + string.Join(", ", fbQueue.Select(b => b.name).ToArray()) + "; report in about " + (fbQueue.Count * 18) + " s";
            Debug.Log(s); return s;
        }

        static void FuelBannerTick()
        {
            if (!Application.isPlaying || fbQueue == null) { EditorApplication.update -= FuelBannerTick; return; }
            double now = EditorApplication.timeSinceStartup;
            if (now < fbNext) return;
            if (fbQueue.Count == 0)
            {
                EditorApplication.update -= FuelBannerTick;
                var es = OpenZ.OzSoundLog.Since(fbU0).Where(e => e.src == "DetailsV2").ToList();
                Debug.Log("[OpenZ] fuel banner check done. Log: " + string.Join(" | ", OpenZ.FuelBanner.Log.ToArray()) + ". DetailsV2 sounds since the check: " + es.Count + ": " +
                          string.Join(", ", es.Select(e => e.t.ToString("0.00") + " " + (e.key ?? "") + " | " + (e.clip ?? "-") + " | " + (e.outcome ?? "")).ToArray()));
                fbQueue = null; return;
            }
            var b = fbQueue[0]; var p = b ? b.GetComponent<OpenZ.DetailProp>() : null;
            if (!b || !p || p.Broken) { fbQueue.RemoveAt(0); fbStep = 0; return; }
            var cam = UnityEngine.Object.FindFirstObjectByType<OpenZ.RtsCamera>();
            var foot = p.Foot; var fwd = -b.transform.forward; fwd.y = 0f; fwd.Normalize();                 // the print side
            if (fbStep == 0) { if (cam) { cam.FlyTo(foot); cam.ZoomTo(30f); } fbStep = 1; fbNext = now + 2.0; return; }
            if (fbStep <= 8)
            {   // a shot from 14 m in front, through a random point of the print (the same call DetailPropHub makes for a sim Hitscan)
                var right = Vector3.Cross(Vector3.up, fwd);
                var target = b.overhead ? b.PrintPoint(new Vector2(UnityEngine.Random.Range(0.15f, 0.85f), UnityEngine.Random.Range(0.2f, 0.8f)))   // fuel_banner_v1_street
                                        : foot + right * UnityEngine.Random.Range(-1.0f, 1.0f) * p.transform.lossyScale.x + Vector3.up * UnityEngine.Random.Range(2.0f, 5.5f) * p.transform.lossyScale.y;
                var a = target + fwd * 14f + Vector3.up * 1.0f; var c = target - fwd * 3f - Vector3.up * 0.2f;
                OpenZ.DetailPropHub.TestShot(a, c);
                fbStep++; fbNext = now + 0.4; return;
            }
            if (fbStep == 9) { p.Damage(p.health * 0.5f, foot + fwd * 6f, true); fbStep = 10; fbNext = now + 4.0; return; }   // splash: D1 + the flap
            if (fbStep == 10) { p.Damage(b.overhead ? p.health * 0.5f : p.health + 1f, foot + fwd * 3f, true); fbStep = 11; fbNext = now + 4.0; return; }    // blast: D2 strips (overhead: one ladder step)
            if (fbStep == 11 && b.overhead) { p.Damage(p.health * 0.5f, foot + fwd * 3f, true); fbStep = 12; fbNext = now + 4.0; return; }   // fuel_banner_v1_street: view only, the third blast breaks it
            fbQueue.RemoveAt(0); fbStep = 0; fbNext = now + 0.5;
        }

        [MenuItem("OpenZ/Details v2/Play check (shoot one of each kind)")]
        public static string PlayCheck()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[OpenZ] details v2 play check: enter Play first"); return "enter Play first"; }
            if (checkRunning) return "already running";
            var byKind = new Dictionary<string, OpenZ.DetailProp>();
            foreach (var p in OpenZ.DetailProp.All.Where(x => x).OrderBy(x => x.name))
                if (!p.intact && !p.Broken && !byKind.ContainsKey(p.kind ?? "")) byKind[p.kind ?? ""] = p;
            listenProp = OpenZ.DetailProp.All.Where(x => x && !x.Broken && (x.kind == "traffic_light" || x.kind == "roadworks_signal")).OrderBy(x => x.name).FirstOrDefault();
            if (!listenProp) listenProp = OpenZ.DetailProp.All.Where(x => x && !x.Broken && x.kind == "windmill").FirstOrDefault();
            if (!listenProp) listenProp = OpenZ.DetailProp.All.Where(x => x && !x.Broken && x.oneShotClips != null && x.oneShotClips.Length > 0).OrderBy(x => x.name).FirstOrDefault();
            double now = EditorApplication.timeSinceStartup;
            listenFrom = now + (listenProp ? Arrive : 0.0); listenTo = listenFrom + (listenProp ? Listen : 0.0);
            listenU0 = listenU1 = -1f;
            if (listenProp)
            {
                var cam = UnityEngine.Object.FindFirstObjectByType<OpenZ.RtsCamera>();
                if (cam) { cam.FlyTo(listenProp.Foot); cam.ZoomTo(70f); }
            }
            checkJobs = byKind.Values.Select(p => new ShotJob { p = p }).ToList();
            checkEnd = -1; checkRunning = true;
            double eta = (listenTo - now) + checkJobs.Sum(j => Arrive + Gap * (j.p.blastOnly ? 1 : Mathf.CeilToInt(j.p.health)) + After) + 4.0;
            EditorApplication.update -= PlayCheckTick; EditorApplication.update += PlayCheckTick;
            var s = "[OpenZ] details v2 play check: listener test at h 70 on " + (listenProp ? listenProp.name : "(nothing with a one-shot)") + ", then " +
                    checkJobs.Count + " kinds, shooting " + string.Join(", ", byKind.Select(kv => kv.Key + "=" + kv.Value.name).ToArray()) +
                    "; report in about " + eta.ToString("0") + " s";
            Debug.Log(s);
            return s;
        }

        static void PlayCheckTick()
        {
            if (!Application.isPlaying || checkJobs == null) { EditorApplication.update -= PlayCheckTick; checkRunning = false; return; }
            double now = EditorApplication.timeSinceStartup;
            if (now < listenTo)
            {   // 1. listener test at the default height
                if (now >= listenFrom && listenU0 < 0f) { listenU0 = Time.unscaledTime; listenH0 = OpenZ.DetailProp.CamHeight(); }
                return;
            }
            if (listenU1 < 0f) { listenU1 = Time.unscaledTime; listenH1 = OpenZ.DetailProp.CamHeight(); }
            // 2. one kind at a time: frame (Arrive s), shoot every Gap s until broken, keep the break on screen After s, next kind
            var j = checkJobs.FirstOrDefault(x => x.done < 0);
            if (j != null)
            {
                if (j.start < 0)
                {
                    j.start = now; j.next = now + Arrive;
                    var cam = UnityEngine.Object.FindFirstObjectByType<OpenZ.RtsCamera>();
                    if (cam && j.p) { cam.FlyTo(j.p.Foot); cam.ZoomTo(30f); }   // 30 m: the break on screen, its sounds by the listener
                    return;
                }
                if (now < j.next) return;
                if (!j.p || j.p.Broken || j.shots >= 12) { j.done = now; return; }
                var foot = j.p.Foot; float h = Mathf.Min(1.1f, j.p.hitHeight * 0.5f);
                var a = foot + new Vector3(0.25f, h, -14f); var b = foot + new Vector3(-0.1f, h, 3f);   // from the camera side, through the post
                if (j.shots == 0) j.crosses = j.p.ShotHits(a, b);
                j.p.Damage(j.p.blastOnly ? j.p.health + 1f : 1f, a, j.p.blastOnly);
                j.shots++;
                j.next = now + (j.p.Broken ? After : Gap);                                      // a break stays on screen After s
                return;
            }
            if (checkEnd < 0) { checkEnd = now + 4.0; return; }
            if (now < checkEnd) return;
            EditorApplication.update -= PlayCheckTick; checkRunning = false;
            var sb = new StringBuilder("[OpenZ] details v2 play check: ");
            int broken = 0;
            foreach (var x in checkJobs)
            {
                if (!x.p) { sb.Append("(prop gone); "); continue; }
                if (x.p.Broken) broken++;
                sb.Append(x.p.name + " [" + x.p.kind + "] " + (x.p.Broken ? "BROKEN" : "NOT BROKEN") + " after " + x.shots + (x.p.blastOnly ? " blast" : " shot(s)") +
                          (x.crosses ? "" : " (test segment misses the cylinder!)") + "; ");
            }
            sb.Append("=> " + broken + "/" + checkJobs.Count + " broken. ");
            if (listenProp && listenU0 >= 0f)
            {
                var ls = OpenZ.OzSoundLog.Since(listenU0).Where(e => e.src == "DetailsV2" && e.t <= listenU1).ToList();
                sb.Append("Listener test on " + listenProp.name + " at camera height " + listenH0.ToString("0") + "-" + listenH1.ToString("0") + " m, " +
                          (listenU1 - listenU0).ToString("0") + " s: " + ls.Count + " DetailsV2 sounds: " +
                          string.Join(", ", ls.GroupBy(e => (e.key ?? "") + " | " + (e.clip ?? "-") + " | " + (e.outcome ?? "")).Select(g => g.Key + " x" + g.Count() +
                          " (listener " + g.Min(e => e.dist).ToString("0") + "-" + g.Max(e => e.dist).ToString("0") + " m)").ToArray()) + ". ");
            }
            var es = OpenZ.OzSoundLog.Since(0f).Where(e => e.src == "DetailsV2").ToList();
            sb.Append("Sound log DetailsV2 since Play: " + es.Count + " entries: ");
            sb.Append(string.Join(", ", es.GroupBy(e => (e.key ?? "") + " | " + (e.clip ?? "-") + " | " + (e.outcome ?? "")).Select(g => g.Key + " x" + g.Count()).ToArray()));
            Debug.Log(sb.ToString());
        }
    }
}
