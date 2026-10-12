using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// five paid imported GLBs) replaces the rejected v1 generated bags at the same VX64 plan positions
    /// (Assets/OpenZ/Models/Sandbags/Lxx_sandbags.json). One menu does it all:
    ///   OpenZ/Sandbags/V2 Build both levels  = import settings + per-level materials, then L02_Desert and L17_CarPark are opened,
    ///   placed and saved in turn (the scene that was open comes back). OpenZ/Sandbags/V2 Place (open level) does the open one.
    /// Per segment DEC_Sandbag_Lxx_NN (root = the plan position and yaw, untouched): "Intact", "Damaged" (inactive), "Rubble"
    /// (inactive), each a LODGroup (LOD0 to 18 % screen height, LOD1 below, culled under 1 %) of the mesh that
    /// Assets/OpenZ/Models/Sandbags/V2/sandbags_v2_levels.json resolved for it (v0 / v1 / v2 round robin = no identical
    /// neighbours; IC / ICM real mirrored corner meshes, no negative scale).
    /// Turns (= the approved Blender previews, preview_sandbags_v2.py segment_matrix) plus the importer's own turn, which is
    /// MEASURED from the corner mesh (Blender FBX lands turned 180 deg about Y, see OpenZBaseProps), never assumed:
    ///   corner pieces: turned when the corner record's end is +1 (the plan has none); every corner arm is checked against its
    ///     check_corners.py rect in the log;
    ///   intact straight walls: every second one in plan order turned 180 deg (the bags are symmetric: 6 looks per length);
    ///   damaged / rubble straight: the spill side (Blender -Y) faces the segment's outward side.
    /// Ground fit: a rigid piece sits on the lowest ground under its footprint (4 corners + centre, sink at most 6 cm); where
    /// the ground spreads more than 6 cm (plan ground_range or measured) every vertex is draped onto the ground (0.25 m raycast
    /// grid, bilinear) and the piece sunk 2 cm; draped copies live in V2/SandbagsV2_Drape_Lxx.asset. Normals stay as modelled.
    /// Material per level: OpenZ/TeamLit, the level atlas x the baked vertex AO, _TeamMode 0 (the L17 canvas hue sits just
    /// under the olive band), smoothness 0.1, night class 0 like the other DEC props, no tint.
    /// SandbagBreakables (on the group) throws SB2_Bag_A/B/C and swaps Damaged / Rubble in (see its summary).
    /// </summary>
    public static class OpenZSandbagsV2
    {
        const string PlanDir = "Assets/OpenZ/Models/Sandbags";
        const string Dir = PlanDir + "/V2";
        const string Kit = Dir + "/SandbagsV2_Kit.fbx";
        const string Lod1Dir = Dir + "/LOD1";
        const string MatDir = "Assets/OpenZ/Materials/Env";
        static readonly string[] Scenes = { "Assets/OpenZ/Scenes/L02_Desert.unity", "Assets/OpenZ/Scenes/L17_CarPark.unity" };
        const float Lod0Height = 0.18f, CullHeight = 0.01f, RigidSpread = 0.06f, MaxSink = 0.06f, DrapeSink = 0.02f, Grid = 0.25f;

        [System.Serializable] class Seg { public string name, anchor, team; public float x, y, z, yaw, length, ground_range; public int bags, rows; public float[] outward; }
        [System.Serializable] class PlanDoc { public string level; public List<Seg> segments; }
        [System.Serializable] class Row { public string segment, intact, damaged, rubble; public bool corner; public int bags, rows; public float length; }
        [System.Serializable] class Lvl { public Row[] plan; }
        [System.Serializable] class LevelsDoc { public Lvl L02, L17; }
        [System.Serializable] class Corner { public string segment, partner; public int end, side; public float gap; public float[] rect; public bool ok; }
        [System.Serializable] class CornerDoc { public string level; public Corner[] corners; }

        // ------------------------------------------------------------------------------------------ the one menu
        [MenuItem("OpenZ/Sandbags/V2 Build both levels (import + place L02 + L17)")]
        public static string BuildBoth()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[OpenZ] Sandbags v2: stop Play first"); return "FAILED (play mode)"; }
            var start = EditorSceneManager.GetActiveScene();
            if (start.isDirty) { Debug.LogError("[OpenZ] Sandbags v2: the open scene has unsaved changes; save it first (nothing done)"); return "FAILED (unsaved scene)"; }
            string back = start.path;
            var sb = new StringBuilder(ImportKit()).Append(" | ");
            foreach (var sc in Scenes)
            {
                if (!File.Exists(sc)) { sb.Append($"{sc} missing | "); continue; }
                EditorSceneManager.OpenScene(sc, OpenSceneMode.Single);
                sb.Append(Place()).Append(" | ");
            }
            if (!string.IsNullOrEmpty(back) && File.Exists(back) && EditorSceneManager.GetActiveScene().path != back) EditorSceneManager.OpenScene(back, OpenSceneMode.Single);
            Debug.Log("[OpenZ] Sandbags v2 build both: " + sb);
            return sb.ToString();
        }

        [MenuItem("OpenZ/Sandbags/V2 Place (open level)")]
        public static string PlaceOpen()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[OpenZ] Sandbags v2: stop Play first"); return "FAILED (play mode)"; }
            return ImportKit() + " | " + Place();
        }

        // ------------------------------------------------------------------------------------------ Play checks (test tools)
        [MenuItem("OpenZ/Sandbags/V2 Play: grenade ON the wall nearest the screen centre (break)")]
        static void PlayBlastOn() => PlayBlast(0f);
        [MenuItem("OpenZ/Sandbags/V2 Play: grenade 3 m out from that wall (partial hit)")]
        static void PlayBlastOut() => PlayBlast(3f);

        /// <summary>Test tool (like GateBattery's sandbag step): a real sim grenade (neutral, 1 damage) at the standing wall nearest
        /// the screen centre, on it or `outM` metres out on its outward side, so the whole chain plays: the sim blast, its FX and
        /// boom, FxManager.Exploded -> SandbagBreakables. Play mode only; nothing is saved.</summary>
        static void PlayBlast(float outM)
        {
            if (!Application.isPlaying) { Debug.LogError("[OpenZ] Sandbags v2: enter Play first"); return; }
            var g = ZGame.I; var cam = Camera.main;
            if (g == null || g.World == null || !cam) { Debug.LogError("[OpenZ] Sandbags v2: no running game / camera"); return; }
            var ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            var c = new Plane(Vector3.up, Vector3.zero).Raycast(ray, out float t) ? ray.GetPoint(t) : cam.transform.position;
            var env = GameObject.Find("Environment"); var grp = env ? env.transform.Find("Sandbags") : null;
            if (!grp) { Debug.LogError("[OpenZ] Sandbags v2: no Environment/Sandbags"); return; }
            Transform best = null; float bd = float.MaxValue;
            foreach (Transform s in grp)
            {
                var rub = s.Find("Rubble"); if (rub && rub.gameObject.activeSelf) continue;          // already broken
                var d = s.position - c; d.y = 0; if (d.sqrMagnitude < bd) { bd = d.sqrMagnitude; best = s; }
            }
            if (!best) { Debug.LogError("[OpenZ] Sandbags v2: no standing wall"); return; }
            var o = Vector3.zero;
            string level = g.level == "L17" ? "L17" : "L02", planPath = $"{PlanDir}/{level}_sandbags.json";
            if (outM > 0f && File.Exists(planPath))
            {
                var s = JsonUtility.FromJson<PlanDoc>(File.ReadAllText(planPath)).segments.FirstOrDefault(q => q.name == best.name);
                if (s?.outward != null && s.outward.Length >= 2) o = new Vector3(s.outward[0], 0, s.outward[1]).normalized;
            }
            var at = best.position + o * outM;
            var px = ZGame.WorldToPx(at);
            g.World.AddMissileForTest(new OpenZ.Sim.ZMissile { x = px.x, y = px.y, damage = 1, radius = 8, team = Team.Neutral, grenade = true, explodeTime = g.World.Time });
            Debug.Log($"[OpenZ] Sandbags v2 test: grenade at {at.ToString("F1")} ({outM:F1} m out from {best.name})");
        }

        // ------------------------------------------------------------------------------------------ import settings + materials
        /// <summary>Import settings as the other Blender kits (OpenZBaseProps / OpenZRocks): scale 1, file units, no axis bake,
        /// imported normals, no materials / colliders / animation; readable (the drape reads the vertices). Atlases sRGB, mips,
        /// aniso 4, 4096, BC7, no crunch. Reimports only what differs.</summary>
        public static string ImportKit()
        {
            AssetDatabase.Refresh();
            int re = 0;
            var models = new List<string> { Kit };
            if (Directory.Exists(Lod1Dir)) models.AddRange(Directory.GetFiles(Lod1Dir, "*.fbx").Select(p => p.Replace('\\', '/')));
            foreach (var path in models)
            {
                var mi = AssetImporter.GetAtPath(path) as ModelImporter;
                if (!mi) { Debug.LogError($"[OpenZ] Sandbags v2: {path} is not imported (run the package's apply.py)"); continue; }
                bool same = mi.globalScale == 1f && mi.useFileUnits && !mi.bakeAxisConversion && !mi.importCameras && !mi.importLights
                    && mi.meshCompression == ModelImporterMeshCompression.Off && mi.importNormals == ModelImporterNormals.Import && !mi.addCollider
                    && mi.isReadable && !mi.importAnimation && mi.animationType == ModelImporterAnimationType.None
                    && mi.materialImportMode == ModelImporterMaterialImportMode.None && !mi.importBlendShapes && !mi.importVisibility;
                if (same) continue;
                mi.globalScale = 1; mi.useFileUnits = true; mi.bakeAxisConversion = false; mi.importCameras = false; mi.importLights = false;
                mi.meshCompression = ModelImporterMeshCompression.Off; mi.importNormals = ModelImporterNormals.Import; mi.addCollider = false;
                mi.isReadable = true; mi.importAnimation = false; mi.animationType = ModelImporterAnimationType.None;
                mi.materialImportMode = ModelImporterMaterialImportMode.None; mi.importBlendShapes = false; mi.importVisibility = false;
                mi.SaveAndReimport(); re++;
            }
            foreach (var lv in new[] { "L02", "L17" })
            {
                string path = $"{Dir}/SandbagsV2_Atlas_{lv}.png";
                var ti = AssetImporter.GetAtPath(path) as TextureImporter;
                if (!ti) { Debug.LogError($"[OpenZ] Sandbags v2: {path} missing (run the package's apply.py)"); continue; }
                bool same = ti.textureType == TextureImporterType.Default && ti.sRGBTexture && ti.mipmapEnabled && ti.anisoLevel == 4 && ti.maxTextureSize == 4096
                    && ti.textureCompression == TextureImporterCompression.CompressedHQ && !ti.crunchedCompression && ti.filterMode == FilterMode.Trilinear && ti.wrapMode == TextureWrapMode.Clamp;
                if (!same)
                {
                    ti.textureType = TextureImporterType.Default; ti.sRGBTexture = true; ti.mipmapEnabled = true; ti.anisoLevel = 4; ti.maxTextureSize = 4096;
                    ti.textureCompression = TextureImporterCompression.CompressedHQ; ti.crunchedCompression = false; ti.filterMode = FilterMode.Trilinear;
                    ti.wrapMode = TextureWrapMode.Clamp;
                    ti.SaveAndReimport(); re++;
                }
                Material(lv);
            }
            AssetDatabase.SaveAssets();
            return $"kit: {models.Count} FBX + 2 atlases checked, {re} reimported, materials M_SandbagsV2_L02 / _L17";
        }

        /// <summary>One TeamLit material per level: the atlas x the baked soft AO (vertex colour Col, sRGB), no team paint, no tint.
        /// Values = the clean DEC / base-prop TeamLit set (OpenZBaseProps.Mats), smoothness 0.1 (burlap), night class 0 (DEC props).</summary>
        static Material Material(string lv)
        {
            Directory.CreateDirectory(MatDir);
            string p = $"{MatDir}/M_SandbagsV2_{lv}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (!m) { m = new Material(Shader.Find("OpenZ/TeamLit")); AssetDatabase.CreateAsset(m, p); }
            m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{Dir}/SandbagsV2_Atlas_{lv}.png"));
            m.SetTextureScale("_BaseMap", Vector2.one); m.SetTextureOffset("_BaseMap", Vector2.zero);
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_UseVertexColor", 1); m.SetFloat("_VertexColorSRGB", 1); m.SetFloat("_TeamMode", 0);
            if (m.HasProperty("_Accent")) m.SetFloat("_Accent", 0);
            m.SetFloat("_Degreen", 0); m.SetFloat("_Triplanar", 0);
            if (m.HasProperty("_TopBlend")) m.SetFloat("_TopBlend", 0);
            m.SetFloat("_Stylize", 1); m.SetFloat("_Degrime", 0); m.SetFloat("_FlatShade", 0);
            m.SetFloat("_Smoothness", 0.1f); m.SetFloat("_Metallic", 0f);
            m.SetColor("_RimColor", new Color(0.10f, 0.09f, 0.08f)); m.SetFloat("_RimPower", 3.2f);
            if (m.HasProperty("_SoftFill")) m.SetFloat("_SoftFill", 0.6f);
            if (m.HasProperty("_AmbientBoost")) m.SetFloat("_AmbientBoost", 1.25f);
            if (m.HasProperty("_KeyWarm")) m.SetColor("_KeyWarm", new Color(1.06f, 1f, 0.92f));
            m.SetColor("_DarkColor", Color.black); m.SetFloat("_DarkThreshold", 0.12f);
            m.SetFloat("_SatScale", 1f); m.SetFloat("_ValScale", 1f); m.SetFloat("_HueShift", 0f);
            if (m.HasProperty("_NightClass")) m.SetFloat("_NightClass", 0f);
            if (m.HasProperty("_SeeThru")) m.SetFloat("_SeeThru", 0f);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        // ------------------------------------------------------------------------------------------ placement
        static Dictionary<string, Mesh> KitMeshes()
        {
            var d = new Dictionary<string, Mesh>();
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(Kit);
            if (root)
                foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                    if (mf.sharedMesh && !d.ContainsKey(Clean(mf.name))) d[Clean(mf.name)] = mf.sharedMesh;
            foreach (var m in AssetDatabase.LoadAllAssetsAtPath(Kit).OfType<Mesh>())
                if (!d.ContainsKey(Clean(m.name))) d[Clean(m.name)] = m;
            return d;
        }

        static Mesh Lod1(string piece)
        {
            string p = $"{Lod1Dir}/{piece}_LOD1.fbx";
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
            var mf = go ? go.GetComponentInChildren<MeshFilter>(true) : null;
            return mf ? mf.sharedMesh : AssetDatabase.LoadAllAssetsAtPath(p).OfType<Mesh>().FirstOrDefault();
        }

        static string Clean(string n)
        {
            int k = n.IndexOf("__", System.StringComparison.Ordinal); if (k >= 0) n = n.Substring(0, k);
            k = n.IndexOf('.'); if (k >= 0) n = n.Substring(0, k);
            return n;
        }

        /// <summary>The importer's turn about Y (0 or 180) measured on SB2_Seg_IC_v0: in Blender its corner arm (|y| > 0.32) is at
        /// the -X end, to +Y; in the preview frame that is local (-x, +z). Anything else but a pure 180 = a mirrored import.</summary>
        static int KitTurn(Mesh ic, out string why)
        {
            why = "";
            var v = ic.vertices; double ax = 0, az = 0; int n = 0;
            foreach (var p in v) if (Mathf.Abs(p.z) > 0.32f) { ax += p.x; az += p.z; n++; }
            if (n < 20) { why = $"SB2_Seg_IC_v0 has no corner arm ({n} arm vertices)"; return -1; }
            ax /= n; az /= n;
            why = $"IC arm at local ({ax:F2}, {az:F2})";
            if (ax < -0.3 && az > 0.2) return 0;
            if (ax > 0.3 && az < -0.2) return 180;
            why += ": a mirrored import (neither the Blender frame nor a 180 deg turn)";
            return -1;
        }

        public static string Place()
        {
            var scene = EditorSceneManager.GetActiveScene();
            string level = scene.name.Contains("L02") ? "L02" : scene.name.Contains("L17") ? "L17" : null;
            if (level == null) { Debug.LogError("[OpenZ] Sandbags v2: open L02_Desert or L17_CarPark"); return "FAILED (not a level)"; }
            string Fail(string why) { Debug.LogError($"[OpenZ] Sandbags v2 {level}: {why} (nothing changed)"); return $"{level} FAILED ({why})"; }
            string planPath = $"{PlanDir}/{level}_sandbags.json", levelsPath = $"{Dir}/sandbags_v2_levels.json", cornersPath = $"{Dir}/{level}_corners.json";
            foreach (var f in new[] { planPath, levelsPath, cornersPath }) if (!File.Exists(f)) return Fail(f + " missing");
            var plan = JsonUtility.FromJson<PlanDoc>(File.ReadAllText(planPath));
            var levels = JsonUtility.FromJson<LevelsDoc>(File.ReadAllText(levelsPath));
            var lvl = level == "L02" ? levels.L02 : levels.L17;
            var corners = (JsonUtility.FromJson<CornerDoc>(File.ReadAllText(cornersPath)).corners ?? new Corner[0]).ToDictionary(c => c.segment);
            if (plan?.segments == null || lvl?.plan == null) return Fail("plan / levels json unreadable");
            var rows = lvl.plan.ToDictionary(r => r.segment);
            var env = GameObject.Find("Environment");
            if (!env) return Fail("no Environment in the scene");
            var kit = KitMeshes();
            if (!kit.TryGetValue("SB2_Seg_IC_v0", out var ic)) return Fail($"{Kit} not imported or has no SB2_Seg_IC_v0");
            if (!ic.isReadable) return Fail("the kit is not readable (ImportKit did not run)");
            int kitTurn = KitTurn(ic, out var turnWhy);
            if (kitTurn < 0) return Fail(turnWhy);
            var mat = Material(level);
            var bags = new[] { "SB2_Bag_A", "SB2_Bag_B", "SB2_Bag_C" }.Select(n => kit.TryGetValue(n, out var m) ? m : null).ToArray();
            if (bags.Any(b => !b)) return Fail("SB2_Bag_A / B / C missing in the kit");

            // idempotent: the old group (v1 or v2) and the old draped meshes go
            var old = env.transform.Find("Sandbags");
            if (old) Object.DestroyImmediate(old.gameObject);
            string drapePath = $"{Dir}/SandbagsV2_Drape_{level}.asset";
            if (AssetDatabase.LoadMainAssetAtPath(drapePath)) AssetDatabase.DeleteAsset(drapePath);
            var group = new GameObject("Sandbags");
            group.transform.SetParent(env.transform, false);
            group.transform.position = Vector3.zero; group.transform.rotation = Quaternion.identity; group.transform.localScale = Vector3.one;
            Physics.SyncTransforms();

            var drapes = new List<Mesh>(); var cache = new Dictionary<long, float>();
            int made = 0, draped = 0, rigid = 0, cornerOk = 0, cornerBad = 0, missingRows = 0, lod1Missing = 0, noGround = 0, worstFloatCm = 0;
            long tris0 = 0, tris1 = 0;
            var sbWarn = new StringBuilder();
            for (int k = 0; k < plan.segments.Count; k++)
            {
                var s = plan.segments[k];
                corners.TryGetValue(s.name, out var cr);
                if (!rows.TryGetValue(s.name, out var row)) { row = Derive(s, cr, k); missingRows++; sbWarn.Append($"{s.name}: not in sandbags_v2_levels.json (rule-derived {row.intact}); "); }
                var go = new GameObject(s.name);
                go.transform.SetParent(group.transform, false);
                go.transform.position = new Vector3(s.x, s.y, s.z);
                go.transform.rotation = Quaternion.Euler(0, s.yaw, 0);
                float yr = s.yaw * Mathf.Deg2Rad;
                int outZ = s.outward != null && s.outward.Length >= 2 && s.outward[0] * Mathf.Sin(yr) + s.outward[1] * Mathf.Cos(yr) > 0 ? 1 : -1;

                var states = new[] { ("Intact", row.intact), ("Damaged", row.damaged), ("Rubble", row.rubble) };
                bool Flip(string stateName) => (cr != null ? cr.end == 1 : stateName == "Intact" ? k % 2 == 1 : outZ == 1) ^ (kitTurn == 180);   // preview rule (spill = Blender -Y to outward) + importer turn
                // one ground decision per segment, from the intact piece's footprint: drape when the plan or the measured ground
                // spreads more than 6 cm, or a rigid piece would float more than 6 cm at an end
                bool drape = s.ground_range > RigidSpread;
                if (!drape && kit.TryGetValue(row.intact ?? "", out var mi0))
                {
                    var g = Probe(go.transform, Quaternion.Euler(0, Flip("Intact") ? 180f : 0f, 0), mi0.bounds, s.y, out bool any);
                    if (!any) noGround++;
                    drape = g.Max() - g.Min() > RigidSpread || g.Min() - s.y < -MaxSink;
                }
                foreach (var (stateName, piece) in states)
                {
                    if (string.IsNullOrEmpty(piece) || !kit.TryGetValue(piece, out var m0)) { sbWarn.Append($"{s.name}: {stateName} mesh '{piece}' missing in the kit; "); continue; }
                    var m1 = Lod1(piece); if (!m1) lod1Missing++;
                    var st = new GameObject(stateName);
                    st.transform.SetParent(go.transform, false);
                    st.transform.localRotation = Quaternion.Euler(0, Flip(stateName) ? 180f : 0f, 0);
                    Mesh use0 = m0, use1 = m1;
                    if (!drape)
                    {   // rigid: on the lowest ground under its own footprint (sink at most 6 cm; lifted when the ground is higher than the plan)
                        var g = Probe(go.transform, st.transform.localRotation, m0.bounds, s.y, out _);
                        float mlo = g.Min() - s.y;
                        float dy = mlo < 0f ? Mathf.Max(mlo, -MaxSink) : mlo > 0.03f ? Mathf.Min(mlo - 0.01f, 0.15f) : 0f;
                        st.transform.localPosition = new Vector3(0, dy, 0);
                    }
                    else
                    {
                        use0 = Drape(m0, st.transform, s.y, cache, $"{s.name}_{stateName}_{piece}", out float lift); drapes.Add(use0);
                        worstFloatCm = Mathf.Max(worstFloatCm, Mathf.RoundToInt(lift * 100f));
                        if (m1) { use1 = Drape(m1, st.transform, s.y, cache, $"{s.name}_{stateName}_{piece}_LOD1", out _); drapes.Add(use1); }
                    }
                    var r0 = Part(st, "LOD0", use0, mat);
                    var lods = new List<LOD> { new LOD(m1 ? Lod0Height : CullHeight, new Renderer[] { r0 }) };
                    if (m1) lods.Add(new LOD(CullHeight, new Renderer[] { Part(st, "LOD1", use1, mat) }));
                    var lg = st.AddComponent<LODGroup>();
                    lg.fadeMode = LODFadeMode.None; lg.SetLODs(lods.ToArray()); lg.RecalculateBounds();
                    if (stateName == "Intact")
                    {
                        tris0 += Tris(use0); tris1 += m1 ? Tris(use1) : Tris(use0);
                        if (cr != null)
                        {   // the corner arm must fill its check_corners.py rect (robot-impassable, allowed tile)
                            if (ArmIn(use0, st.transform, cr.rect, out var armW)) cornerOk++;
                            else { cornerBad++; sbWarn.Append($"{s.name}: corner arm at ({armW.x:F2}, {armW.z:F2}) is outside its rect [{string.Join(", ", cr.rect)}]; "); }
                        }
                    }
                    if (stateName != "Intact") st.SetActive(false);
                }
                if (drape) draped++; else rigid++;
                made++;
            }
            if (drapes.Count > 0)
            {
                AssetDatabase.CreateAsset(drapes[0], drapePath);
                for (int i = 1; i < drapes.Count; i++) AssetDatabase.AddObjectToAsset(drapes[i], drapePath);
            }
            var br = group.AddComponent<SandbagBreakables>();
            br.bagMeshes = bags; br.bagMesh = bags[0]; br.material = mat;
            EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(scene);
            string res = $"{level}: {made} segments ({rigid} rigid, {draped} draped, worst drape lift {worstFloatCm} cm), corners {cornerOk} in their rect / {cornerBad} outside, " +
                         $"intact tris LOD0 {tris0:N0} / LOD1 {tris1:N0}, importer turn {kitTurn} deg ({turnWhy}), LOD1 missing {lod1Missing}, no ground hit {noGround}, plan rows derived {missingRows}. Saved.";
            if (sbWarn.Length > 0) Debug.LogWarning($"[OpenZ] Sandbags v2 {level} warnings: {sbWarn}");
            if (cornerBad > 0 || missingRows > 0) Debug.LogError($"[OpenZ] Sandbags v2 {res}"); else Debug.Log($"[OpenZ] Sandbags v2 {res}");
            return res;
        }

        /// <summary>The preview rule (pick) for a segment missing in sandbags_v2_levels.json.</summary>
        static Row Derive(Seg s, Corner c, int k)
        {
            int n = Mathf.Clamp(s.bags, 2, 4); int v = k % 3;
            var r = new Row { segment = s.name, bags = s.bags, rows = s.rows, length = s.length, corner = c != null };
            if (c != null)
            {
                bool turn = c.end == 1; int sideAfter = turn ? -c.side : c.side; string m = sideAfter == -1 ? "M" : "";
                r.intact = $"SB2_Seg_IC{m}_v{Mathf.Min(v, 1)}"; r.damaged = $"SB2_Seg_DC{m}"; r.rubble = $"SB2_Seg_RC{m}";
            }
            else
            {
                string t = s.rows >= 3 && n >= 3 ? "T" : "";
                r.intact = $"SB2_Seg_I{n}{t}_v{v}"; r.damaged = $"SB2_Seg_D{n}"; r.rubble = $"SB2_Seg_R{n}";
            }
            return r;
        }

        static MeshRenderer Part(GameObject parent, string name, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On; mr.receiveShadows = true;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.BlendProbes;
            GameObjectUtility.SetStaticEditorFlags(go, 0);           // Intact / Damaged / Rubble swap at run time: never static-batched
            return mr;
        }

        static long Tris(Mesh m) { long t = 0; for (int i = 0; i < m.subMeshCount; i++) t += m.GetIndexCount(i) / 3; return t; }

        /// <summary>Ground under the 4 footprint corners (5 cm in) + the centre of a piece (turned by `turn` in the segment frame), world y.</summary>
        static float[] Probe(Transform seg, Quaternion turn, Bounds mb, float planY, out bool any)
        {
            float x0 = mb.min.x + 0.05f, x1 = mb.max.x - 0.05f, z0 = mb.min.z + 0.05f, z1 = mb.max.z - 0.05f;
            var pts = new[] { new Vector3(x0, 0, z0), new Vector3(x0, 0, z1), new Vector3(x1, 0, z0), new Vector3(x1, 0, z1), new Vector3((x0 + x1) * 0.5f, 0, (z0 + z1) * 0.5f) };
            var g = new float[pts.Length]; any = false;
            for (int i = 0; i < pts.Length; i++)
            {
                var w = seg.position + seg.rotation * (turn * pts[i]);
                g[i] = GroundAt(w.x, w.z, planY, out bool hit); any |= hit;
            }
            return g;
        }

        /// <summary>Copy of the mesh with every vertex moved by ground(x, z) - plan y (0.25 m raycast grid, bilinear), sunk 2 cm.
        /// Normals stay as modelled (a 6 deg shear at most). lift = the largest upward move (how much a rigid piece would float).</summary>
        static Mesh Drape(Mesh src, Transform st, float planY, Dictionary<long, float> cache, string name, out float lift)
        {
            var m = Object.Instantiate(src); m.name = name;
            var v = m.vertices; lift = 0f; float minD = float.MaxValue;
            var ltw = Matrix4x4.TRS(st.parent.position, st.parent.rotation * st.localRotation, Vector3.one);   // the state's frame at the plan y
            for (int i = 0; i < v.Length; i++)
            {
                var w = ltw.MultiplyPoint3x4(v[i]);
                float g = Bilinear(w.x, w.z, planY, cache) - planY;
                v[i].y += g - DrapeSink;
                minD = Mathf.Min(minD, g);
            }
            lift = minD == float.MaxValue ? 0f : -Mathf.Min(0f, minD);
            st.localPosition = Vector3.zero;
            m.vertices = v; m.RecalculateBounds();
            return m;
        }

        static float Bilinear(float x, float z, float planY, Dictionary<long, float> cache)
        {
            float gx = x / Grid, gz = z / Grid; int ix = Mathf.FloorToInt(gx), iz = Mathf.FloorToInt(gz); float fx = gx - ix, fz = gz - iz;
            float a = Cached(ix, iz, planY, cache), b = Cached(ix + 1, iz, planY, cache), c = Cached(ix, iz + 1, planY, cache), d = Cached(ix + 1, iz + 1, planY, cache);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fz);
        }

        static float Cached(int ix, int iz, float planY, Dictionary<long, float> cache)
        {
            long key = ((long)ix << 32) ^ (uint)iz;
            if (!cache.TryGetValue(key, out float y)) { y = GroundAt(ix * Grid, iz * Grid, planY, out _); cache[key] = y; }
            return y;
        }

        /// <summary>Highest terrain hit at (x, z) within 0.6 m of the plan y: COL_Terrain first, else any other Environment collider
        /// (never units, buildings, the sandbags themselves); the plan y when nothing is hit.</summary>
        static float GroundAt(float x, float z, float planY, out bool hit)
        {
            hit = false;
            var hits = Physics.RaycastAll(new Vector3(x, planY + 4f, z), Vector3.down, 9f, ~0, QueryTriggerInteraction.Ignore);
            float bestT = float.NegativeInfinity, bestE = float.NegativeInfinity;
            foreach (var h in hits)
            {
                if (Mathf.Abs(h.point.y - planY) >= 0.6f) continue;
                bool terrain = false, env = false;
                for (var q = h.collider.transform; q; q = q.parent)
                {
                    if (q.name == "COL_Terrain") { terrain = true; break; }
                    if (q.name == "ZodObjects" || q.name == "Sandbags") break;
                    if (q.name == "Environment") { env = true; break; }
                }
                if (terrain) bestT = Mathf.Max(bestT, h.point.y); else if (env) bestE = Mathf.Max(bestE, h.point.y);
            }
            if (bestT > float.NegativeInfinity) { hit = true; return bestT; }
            if (bestE > float.NegativeInfinity) { hit = true; return bestE; }
            return planY;
        }

        /// <summary>World centroid of the corner arm (mesh vertices with |z| > 0.32 before the state's turn) inside rect
        /// [x0, x1, z0, z1] grown by 0.12 m.</summary>
        static bool ArmIn(Mesh m, Transform st, float[] rect, out Vector3 armW)
        {
            armW = Vector3.zero;
            var v = m.vertices; Vector3 sum = Vector3.zero; int n = 0;
            foreach (var p in v) if (Mathf.Abs(p.z) > 0.32f) { sum += p; n++; }
            if (n == 0 || rect == null || rect.Length < 4) return false;
            armW = st.TransformPoint(sum / n);
            const float e = 0.12f;
            return armW.x >= rect[0] - e && armW.x <= rect[1] + e && armW.z >= rect[2] - e && armW.z <= rect[3] + e;
        }
    }
}
