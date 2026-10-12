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
    /// Every Zod hut becomes a small, readable military
    /// installation: solar farm / fuel dump / generator post / comms post / supply compound, per level (L02 sandbags, earth
    /// berm, timber kerb; L17 concrete blocks, bund, concrete kerb). Tile, blocking and hut health stay Zod's.
    /// Steps: 1 import settings + materials + set prefabs (Resources/OpenZ/BaseProps2) + layout + Z_hut default model,
    /// </summary>
    public static class OpenZBaseProps
    {
        public const string Dst = "Assets/OpenZ/Models/BaseProps2", MatDir = "Assets/OpenZ/Materials/BaseProps2",
            PrefabDir = "Assets/OpenZ/Resources/OpenZ/BaseProps2", HutPrefab = "Assets/OpenZ/Prefabs/Z_hut.prefab";
        static string Src => Path.Combine(OpenZPaths.RepoRoot, "projects", "blender", "export", "base_props_v2");
        static readonly string[] Keys = { "Solar", "Fuel", "Generator", "Comms", "Supply" };

        [System.Serializable] class ManSet { public string level, key, kind; public string[] chunks; }

        static string Clean(string n) { int k = n.IndexOf("__", System.StringComparison.Ordinal); return k >= 0 ? n.Substring(0, k) : n; }

        // ------------------------------------------------------------------------------------------ 1 import + prefabs
        [MenuItem("OpenZ/Base Props/1 Import kit + build set prefabs")]
        public static string ImportAndBuild()
        {
            Directory.CreateDirectory(Dst); Directory.CreateDirectory(MatDir); Directory.CreateDirectory(PrefabDir);
            int copied = 0;
            if (Directory.Exists(Src))
            {
                foreach (var f in Directory.GetFiles(Src).Where(f => f.EndsWith(".fbx") || Path.GetFileName(f).StartsWith("bp_atlas_") && f.EndsWith(".png") || f.EndsWith("_manifest.json") || f.EndsWith("_sets.json")))
                {
                    var dst = $"{Dst}/{Path.GetFileName(f)}";
                    if (!File.Exists(dst) || !File.ReadAllBytes(dst).SequenceEqual(File.ReadAllBytes(f))) { File.Copy(f, dst, true); copied++; }
                }
                var lay = Path.Combine(Src, "base_props_v2_layout_unity.json");
                var layDst = $"{PrefabDir}/base_props_v2_layout.json";
                if (File.Exists(lay) && (!File.Exists(layDst) || !File.ReadAllBytes(layDst).SequenceEqual(File.ReadAllBytes(lay)))) { File.Copy(lay, layDst, true); copied++; }
            }
            AssetDatabase.Refresh();
            OpenZ.BasePropHuts.ReloadLayout();
            foreach (var path in Directory.GetFiles(Dst, "*.fbx").Select(p => p.Replace('\\', '/')))
            {
                var mi = (ModelImporter)AssetImporter.GetAtPath(path);
                if (!mi) continue;
                mi.globalScale = 1; mi.useFileUnits = true; mi.bakeAxisConversion = false; mi.importCameras = false; mi.importLights = false;
                mi.meshCompression = ModelImporterMeshCompression.Off; mi.importNormals = ModelImporterNormals.Import; mi.addCollider = false;
                mi.isReadable = path.Contains("_Chunks"); mi.importAnimation = false; mi.animationType = ModelImporterAnimationType.None;
                mi.materialImportMode = ModelImporterMaterialImportMode.None; mi.importBlendShapes = false; mi.importVisibility = false;
                mi.SaveAndReimport();
            }
            foreach (var path in Directory.GetFiles(Dst, "bp_atlas_*.png").Select(p => p.Replace('\\', '/')))
            {   // flat palette swatches + decals: full 1024 source, BC7 high quality, mips, trilinear, aniso 8, clamped
                var ti = (TextureImporter)AssetImporter.GetAtPath(path);
                ti.textureType = TextureImporterType.Default; ti.sRGBTexture = true; ti.mipmapEnabled = true; ti.wrapMode = TextureWrapMode.Clamp;
                ti.filterMode = FilterMode.Trilinear; ti.anisoLevel = 8; ti.maxTextureSize = 1024; ti.textureCompression = TextureImporterCompression.CompressedHQ;
                ti.SaveAndReimport();
            }
            var man = ReadManifest();
            var sb = new StringBuilder($"base props v2: {copied} files copied; ");
            foreach (var lv in new[] { "L02", "L17" })
            {
                var m = Mats(lv);
                var chunkMeshes = AssetDatabase.LoadAllAssetsAtPath($"{Dst}/BP2_{lv}_Chunks.fbx").OfType<Mesh>()
                    .GroupBy(x => Clean(x.name).Replace("CH_", "")).ToDictionary(g => g.Key, g => g.First());
                foreach (var key in Keys)
                {
                    man.TryGetValue($"{lv}_{key}", out var ms);
                    var list = new List<Mesh>();
                    foreach (var c in ms?.chunks ?? new string[0])
                        if (chunkMeshes.TryGetValue(c, out var mesh)) list.Add(mesh);
                        else Debug.LogWarning($"[OpenZ] base props v2: chunk {c} missing in BP2_{lv}_Chunks.fbx");
                    var res = BuildSetPrefab(lv, key, ms?.kind ?? key.ToLowerInvariant(), list.ToArray(), m);
                    sb.Append($"{lv}_{key} {res}, ");
                }
            }
            sb.Append(RebuildHutPrefab());
            AssetDatabase.SaveAssets();
            Debug.Log("[OpenZ] " + sb);
            return sb.ToString();
        }

        [System.Serializable] class ManFile { public ManSet[] sets; }

        /// <summary>Per set: kind + the chunk kinds thrown on the break (base_props_v2_sets.json, written by the kit build).</summary>
        static Dictionary<string, ManSet> ReadManifest()
        {
            var d = new Dictionary<string, ManSet>();
            var p = Path.Combine(Src, "base_props_v2_sets.json");
            if (!File.Exists(p)) p = $"{Dst}/base_props_v2_sets.json";
            if (!File.Exists(p)) { Debug.LogWarning("[OpenZ] base props v2: base_props_v2_sets.json missing"); return d; }
            var f = JsonUtility.FromJson<ManFile>(File.ReadAllText(p));
            if (f?.sets != null) foreach (var s in f.sets) if (s != null) d[$"{s.level}_{s.key}"] = s;
            return d;
        }

        struct SetMats { public Material paint, glass, stain, crate; }

        /// <summary>Clean stylized TeamLit (the v1 / rocks v2 values): atlas x baked vertex AO, no team paint, no grime, no rim
        /// outline (dark rim), map-item night class, smooth bevels.</summary>
        static SetMats Mats(string lv)
        {
            var atlas = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Dst}/bp_atlas_{lv}.png");
            Material M(string name, float smooth, float metal)
            {
                string p = $"{MatDir}/{name}_{lv}.mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(p);
                if (!m) { m = new Material(Shader.Find("OpenZ/TeamLit")); AssetDatabase.CreateAsset(m, p); }
                m.SetTexture("_BaseMap", atlas); m.SetTextureScale("_BaseMap", Vector2.one); m.SetTextureOffset("_BaseMap", Vector2.zero);
                m.SetColor("_BaseColor", Color.white);
                m.SetFloat("_UseVertexColor", 1); m.SetFloat("_VertexColorSRGB", 1); m.SetFloat("_TeamMode", 0); m.SetFloat("_Accent", 0);
                m.SetFloat("_Degreen", 0); m.SetFloat("_Triplanar", 0); m.SetFloat("_TopBlend", 0);
                m.SetFloat("_Stylize", 1); m.SetFloat("_Degrime", 0); m.SetFloat("_FlatShade", 0);
                m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", metal);
                m.SetColor("_RimColor", new Color(0.10f, 0.09f, 0.08f)); m.SetFloat("_RimPower", 3.2f);
                if (m.HasProperty("_SoftFill")) m.SetFloat("_SoftFill", 0.62f);
                if (m.HasProperty("_AmbientBoost")) m.SetFloat("_AmbientBoost", 1.25f);
                if (m.HasProperty("_KeyWarm")) m.SetColor("_KeyWarm", new Color(1.06f, 1f, 0.92f));
                m.SetColor("_DarkColor", Color.black); m.SetFloat("_DarkThreshold", 0.12f);
                m.SetFloat("_SatScale", 1f); m.SetFloat("_ValScale", 1f); m.SetFloat("_HueShift", 0f);
                if (m.HasProperty("_NightClass")) m.SetFloat("_NightClass", 3f);
                if (m.HasProperty("_SeeThru")) m.SetFloat("_SeeThru", 0f);
                m.enableInstancing = true;
                EditorUtility.SetDirty(m);
                return m;
            }
            return new SetMats { paint = M("M_BP2_Paint", 0.22f, 0f), glass = M("M_BP2_Glass", 0.35f, 0f), stain = M("M_BP2_Stain", 0.92f, 0f), crate = OpenZGrenadeCrateV2.CrateMaterial() };
        }

        static string BuildSetPrefab(string lv, string key, string kind, Mesh[] chunks, SetMats m)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{Dst}/BP2_{lv}_{key}.fbx");
            if (!model) { Debug.LogError($"[OpenZ] base props v2: {Dst}/BP2_{lv}_{key}.fbx missing"); return "FAILED (fbx missing)"; }
            var root = new GameObject($"BP2_{lv}_{key}");
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            inst.transform.SetParent(root.transform, false);
            PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            inst.name = "Kit";
            // orientation: Blender FBX lands rotated 180 deg about Y (OpenZSetup notes the same for the level); the kit's FRONT_
            // empty sits on the camera side (Blender -Y): turn the kit so it points to Unity -Z (the RTS camera side)
            var front = inst.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => Clean(t.name) == "FRONT");
            float turn = 0f;
            if (front)
            {
                var d = front.position - root.transform.position; d.y = 0f;
                if (d.sqrMagnitude > 1e-6f) turn = Mathf.Round(Vector3.SignedAngle(d.normalized, Vector3.back, Vector3.up) / 90f) * 90f;
                if (Mathf.Abs(turn) > 0.1f) inst.transform.rotation = Quaternion.AngleAxis(turn, Vector3.up) * inst.transform.rotation;
            }
            else Debug.LogWarning($"[OpenZ] base props v2: BP2_{lv}_{key} has no FRONT_ marker (orientation not checked)");
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                string n = Clean(r.name);
                r.sharedMaterial = n.StartsWith("GL_") ? m.glass : n.StartsWith("ST_") ? m.stain : n.StartsWith("CRATE_") ? m.crate : m.paint;
                r.shadowCastingMode = n.StartsWith("ST_") ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On;
                r.receiveShadows = true;
                r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.BlendProbes;
            }
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                string n = Clean(t.name);
                if (n == "RUIN" || n.StartsWith("KEEP_RING_") && n.EndsWith("_GATE")) t.gameObject.SetActive(false);
            }
            var bp = root.AddComponent<OpenZ.BaseProp>();
            bp.setKey = $"{lv}_{key}"; bp.kind = kind; bp.chunks = chunks; bp.chunkMaterial = m.paint; bp.glassMaterial = m.glass; bp.gate = "S";
            PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabDir}/BP2_{lv}_{key}.prefab");
            Object.DestroyImmediate(root);
            return $"ok (turned {turn:0} deg, {chunks.Length} chunks)";
        }

        /// <summary>Z_hut keeps its root (TeamPaint) and gets the L02 fuel dump as its default model (sane bounds for OpenZ
        /// Setup); the level bake / the runtime safety net (BasePropHuts) put the right installation per level and tile.</summary>
        public static string RebuildHutPrefab()
        {
            var pf = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/BP2_L02_Fuel.prefab");
            if (!pf) return "Z_hut: build the set prefabs first";
            var root = PrefabUtility.LoadPrefabContents(HutPrefab);
            for (int i = root.transform.childCount - 1; i >= 0; i--) Object.DestroyImmediate(root.transform.GetChild(i).gameObject);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(pf, root.transform);
            go.transform.localPosition = Vector3.zero; go.transform.localRotation = Quaternion.identity; go.transform.localScale = Vector3.one;
            PrefabUtility.SaveAsPrefabAsset(root, HutPrefab); PrefabUtility.UnloadPrefabContents(root);
            return "Z_hut default model = BP2_L02_Fuel";
        }

        // ------------------------------------------------------------------------------------------ 2 / 3 bake the scenes
        [MenuItem("OpenZ/Base Props/2 Bake huts in open level")]
        public static string BakeOpenLevel()
        {
            var scene = EditorSceneManager.GetActiveScene();
            string lv = scene.name.StartsWith("L17") ? "L17" : "L02";
            OpenZ.BasePropHuts.ReloadLayout();
            var zroot = GameObject.Find("ZodObjects");
            if (!zroot) return "no ZodObjects in the open scene";
            var ground = GameObject.Find("COL_Terrain") ? GameObject.Find("COL_Terrain").GetComponent<Collider>() : null;
            var sb = new StringBuilder();
            int n = 0, sloped = 0, hidden = 0;
            foreach (var hut in zroot.transform.Cast<Transform>().ToList())
            {
                if (!OpenZ.BasePropHuts.TryParseHut(hut.name, out int x, out int y)) continue;
                var (set, gate) = OpenZ.BasePropHuts.SetFor(lv, x, y);
                var pf = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/BP2_{lv}_{set}.prefab");
                if (!pf) return $"missing {PrefabDir}/BP2_{lv}_{set}.prefab: run step 1";
                var outer = PrefabUtility.GetOutermostPrefabInstanceRoot(hut.gameObject);
                if (outer == hut.gameObject) PrefabUtility.UnpackPrefabInstance(outer, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);
                for (int i = hut.childCount - 1; i >= 0; i--) Object.DestroyImmediate(hut.GetChild(i).gameObject);
                // tile centre (the blocked Zod tile); the pad seat: its 4.5 cm top never lower than the highest ground under the
                // 2.8 x 2.4 m pad (no terrain poking through), its 25 cm skirt always reaching the lowest (never floating)
                var c = new Vector3(x * 2 + 1, 0f, -(y * 2 + 1));
                var nudge = OpenZ.BasePropHuts.NudgeFor(lv, x, y);          // REVIEW: installation off the tile centre where level geometry cuts the frame
                var pc = c + nudge;
                float lo = float.MaxValue, hi = float.MinValue;
                for (int i = -2; i <= 2; i++)
                    for (int j = -2; j <= 2; j++)
                    {
                        var p = pc + new Vector3(i * 0.7f, 200f, j * 0.6f);
                        float gy = hut.position.y;
                        if (ground && ground.Raycast(new Ray(p, Vector3.down), out var hit, 400f)) gy = hit.point.y;
                        lo = Mathf.Min(lo, gy); hi = Mathf.Max(hi, gy);
                    }
                float yy = Mathf.Max(lo - 0.02f, hi - 0.035f);
                if (hi - lo > 0.26f) { sloped++; sb.Append($" {hut.name} slope {hi - lo:F2} m (skirt 0.25 m);"); }
                hut.SetPositionAndRotation(new Vector3(c.x, yy, c.z), Quaternion.identity);
                hut.localScale = Vector3.one;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(pf, hut);
                go.transform.localPosition = nudge; go.transform.localRotation = Quaternion.identity; go.transform.localScale = Vector3.one;
                if (nudge.sqrMagnitude > 1e-6f) sb.Append($" {hut.name} installation nudged ({nudge.x:0.00}, {nudge.z:0.00}) m;");
                var bp = go.GetComponent<OpenZ.BaseProp>();
                if (bp) { bp.gate = gate; bp.SetGate(gate); EditorUtility.SetDirty(bp); }
                // the ring pieces (sandbags / berm / blocks / kerb, outside the pad) sit on the lowest ground under each piece:
                // on a slope the high end sinks into the sand, the low end never floats
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                {
                    if (!Clean(r.name).StartsWith("KEEP_RING_")) continue;
                    var b = r.bounds; float gmin = float.MaxValue;
                    for (int k = 0; k <= 4; k++)
                    {
                        var p = Vector3.Lerp(new Vector3(b.min.x, 0, b.min.z), new Vector3(b.max.x, 0, b.max.z), k / 4f) + Vector3.up * 200f;
                        if (ground && ground.Raycast(new Ray(p, Vector3.down), out var hit, 400f)) gmin = Mathf.Min(gmin, hit.point.y);
                    }
                    if (gmin < float.MaxValue && gmin < yy - 0.01f) { r.transform.position += Vector3.down * (yy - gmin - 0.01f); EditorUtility.SetDirty(r.transform); }
                }
                hidden += OpenZ.BasePropHuts.HideDecorOn(scene, new Vector3(pc.x, yy, pc.z));
                n++;
            }
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
            var msg = $"{lv}: {n} huts baked as military installations, {hidden} decor pieces inside frames hidden" + (sloped > 0 ? $", {sloped} on a slope over 0.3 m" : "") + (sb.Length > 0 ? ":" + sb : "");
            Debug.Log("[OpenZ] base props v2 " + msg);
            return msg;
        }

        [MenuItem("OpenZ/Base Props/3 Bake both levels (L02 + L17)")]
        public static string BakeBoth()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return "cancelled";
            var back = EditorSceneManager.GetActiveScene().path;
            var sb = new StringBuilder();
            foreach (var s in new[] { "Assets/OpenZ/Scenes/L02_Desert.unity", "Assets/OpenZ/Scenes/L17_CarPark.unity" })
            {
                EditorSceneManager.OpenScene(s, OpenSceneMode.Single);
                sb.Append(BakeOpenLevel()).Append(" | ");
            }
            if (!string.IsNullOrEmpty(back)) EditorSceneManager.OpenScene(back, OpenSceneMode.Single);
            return sb.ToString();
        }

        [MenuItem("OpenZ/Base Props/4 Remove the v1 kit (after 3)")]
        public static string RemoveV1()
        {
            if (!AssetDatabase.IsValidFolder("Assets/OpenZ/Resources/OpenZ/BaseProps") && !AssetDatabase.IsValidFolder("Assets/OpenZ/Models/BaseProps")) return "v1 kit already removed";
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return "cancelled";
            var back = EditorSceneManager.GetActiveScene().path;
            var still = new List<string>();
            foreach (var s in new[] { "Assets/OpenZ/Scenes/L02_Desert.unity", "Assets/OpenZ/Scenes/L17_CarPark.unity" })
            {
                EditorSceneManager.OpenScene(s, OpenSceneMode.Single);
                foreach (var bp in Object.FindObjectsByType<OpenZ.BaseProp>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    var src = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(bp.gameObject) ?? "";
                    if (src.Contains("/BaseProps/") || !bp.gameObject.name.StartsWith("BP2_")) still.Add($"{s}:{bp.transform.parent?.name}");
                }
            }
            if (!string.IsNullOrEmpty(back)) EditorSceneManager.OpenScene(back, OpenSceneMode.Single);
            if (still.Count > 0) return "NOT removed: v1 sets still in the scenes (run step 3 first): " + string.Join(", ", still.Take(6));
            int n = 0;
            foreach (var f in new[] { "Assets/OpenZ/Resources/OpenZ/BaseProps", "Assets/OpenZ/Models/BaseProps", "Assets/OpenZ/Materials/BaseProps" })
                if (AssetDatabase.IsValidFolder(f) && AssetDatabase.DeleteAsset(f)) n++;
            AssetDatabase.SaveAssets();
            return $"v1 kit removed ({n} folders); the v1 backup is in work/staging/base_props_v1/backup";
        }

        [MenuItem("OpenZ/Base Props/Report")]
        public static string Report()
        {
            var sb = new StringBuilder("base props v2: ");
            foreach (var lv in new[] { "L02", "L17" })
                foreach (var key in Keys)
                {
                    var pf = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/BP2_{lv}_{key}.prefab");
                    if (!pf) { sb.Append($"{lv}_{key} MISSING; "); continue; }
                    int tris = pf.GetComponentsInChildren<MeshFilter>(true).Where(f => f.sharedMesh && !Clean(f.name).StartsWith("RUIN") && !Clean(f.name).StartsWith("KEEP_"))
                        .Sum(f => f.sharedMesh.triangles.Length / 3);
                    var bp = pf.GetComponent<OpenZ.BaseProp>();
                    var names = pf.GetComponentsInChildren<Transform>(true).Select(t => Clean(t.name)).ToList();
                    int Cnt(string pre) => names.Count(nm => nm.StartsWith(pre));
                    var kit = pf.transform.Find("Kit");
                    sb.Append($"{lv}_{key} {tris} tris, kit yaw {(kit ? kit.localEulerAngles.y : 0):0}, {bp?.chunks?.Length ?? 0} chunks, panels {Cnt("SOLY_")}, spin {Cnt("SPIN_")}, " +
                              $"lamps {Cnt("LAMP_")}, blink {Cnt("BLINK_")}, topple {Cnt("TOPPLE_")}, obst {Cnt("OBST_")}, ring {Cnt("KEEP_RING_")}, ruin {Cnt("RUIN")}; ");
                }
            var z = GameObject.Find("ZodObjects");
            if (z)
                foreach (Transform h in z.transform)
                    if (OpenZ.BasePropHuts.TryParseHut(h.name, out _, out _))
                    {
                        var bp = h.GetComponentInChildren<OpenZ.BaseProp>(true);
                        sb.Append($"{h.name}={(bp ? bp.setKey + " gate " + bp.gate + (bp.gameObject.name.StartsWith("BP2_") ? "" : " (v1!)") : "OLD HUT")}" +
                                  (bp && bp.transform.localPosition.sqrMagnitude > 1e-6f ? $" nudge ({bp.transform.localPosition.x:0.00}, {bp.transform.localPosition.z:0.00})" : "") + "; ");
                    }
            Debug.Log("[OpenZ] " + sb);
            return sb.ToString();
        }
    }
}
