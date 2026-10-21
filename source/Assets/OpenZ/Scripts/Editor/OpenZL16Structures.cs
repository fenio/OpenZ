using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Level 16 structures, placed into the OPEN level 16 scene (the scene is never rebuilt; this menu replaces only its own
    /// objects and can be run again), from Assets/OpenZ/Models/L16/Structures/L16_structures_places.json:
    /// - L16_Structures under Environment: Ruins (69 pieces, each near / middle / far version behind a level-of-detail switch, all
    ///   on ONE picture sheet and one material; the swamp pieces on their deep footing; the plinth and the pad of the pyramids),
    ///   Surround (the 23 pieces outside the map, middle and far versions only), the two pyramids (objects named by their uid with
    ///   the detail component and L16Pyramid: Intact, Damaged, RUIN_Mound) and Fires (20 fires: Out and Lit shapes, L16Fires).
    /// - the 18 huts of the map: each scene object hut_x_y keeps its name and place and gets the thatched hut, its fallen state
    ///   and L16Hut (whatever stood under it is removed).
    /// - the 15 lamp posts as lamp props DEC_Service_lamp_L16_&lt;id&gt; directly under Environment (the service lamp of level 2 from
    ///   its own model, with its materials): the night lights find lamp props by name.
    /// Every piece is seated on the ground the level shows (the list's dz); nothing here closes a tile: the tiles come with the
    /// level data (apply_j7.py of the kit closes them). The list also carries, for the checks, the whole foot of every piece
    /// (foot: every tile it covers) and the tiles under low pieces that do not block direct fire (sight_low).
    /// </summary>
    public static class OpenZL16Structures
    {
        internal const string Dir = "Assets/OpenZ/Models/L16/Structures", Tex = "Assets/OpenZ/Textures/L16/Structures";
        internal const string Kit = Dir + "/L16_structures_kit.fbx", PlacesPath = Dir + "/L16_structures_places.json";
        const string Paving = "Assets/OpenZ/Textures/L16/Ground/L16_layer_ruinpaving.png", LampModel = "Assets/OpenZ/Models/Environment/L02_environment.fbx";
        internal const float BoxTolerance = 0.08f;
        /// <summary>Allowed box difference of one piece: 8 cm, scaled with a piece that stands enlarged; a piece of the surround shows its
        /// middle version first (a thinned mesh, its box a little smaller): 15 cm more.</summary>
        internal static float Tol(Row r) => BoxTolerance * Mathf.Max(1f, r.scale != null && r.scale.Length > 0 ? Mathf.Abs(r.scale[0]) : 1f) + (r.far_only ? 0.15f : 0f);

        [System.Serializable] internal class Row { public string name, kind, shape, uid; public float[] loc, quat_wxyz, scale, bmin, bmax; public bool sunk, far_only; public float dz; public int[] tiles, foot; }
        [System.Serializable] internal class FireRow { public string id, kind, name, host, shape_out, shape_lit; public float[] loc, quat_wxyz, scale; public float dz; public int[] tiles, foot; }
        [System.Serializable] internal class HutRow { public string name; public int tx, ty; }
        [System.Serializable] internal class LampRow { public string id, kind, name, near; public float x, z, yaw, distance_m; }
        [System.Serializable] internal class PyrRow { public string uid, name; public float scale; public string[] fires; }
        [System.Serializable] internal class Counts { public int ruins, surround, footings, pyramids, fires, huts, lamps, tiles, sight_low, foot_tiles; }
        [System.Serializable] internal class Doc { public Row[] ruins; public FireRow[] fires; public HutRow[] huts; public LampRow[] lamps; public PyrRow[] pyramids; public int[] tiles, sight_low; public float[] junctions; public float sunk_depth; public Counts counts; }

        internal static Doc Load()
        {
            if (!File.Exists(PlacesPath)) { Debug.LogError("[OpenZ L16] placement list missing: " + PlacesPath); return null; }
            return JsonUtility.FromJson<Doc>(File.ReadAllText(PlacesPath));
        }

        internal static void Place(float[] loc, float[] quat, float[] scale, float dz, out Vector3 pos, out Quaternion rot, out Vector3 scl)
        {
            OpenZL16Rocks.Place(new OpenZL16Rocks.BObj { loc = loc, quat_wxyz = quat, scale = scale }, out pos, out rot, out scl);
            pos.y += dz;
        }

        /// <summary>Largest difference (m) between the box of a placed shape and the box the blockout list has for it (moved down by dz).
        /// A swamp piece reaches deeper than the list on purpose (its footing): its lower edge is not compared.</summary>
        internal static float BoxDiff(Row r, Transform lod0)
        {
            if (r.bmin == null || r.bmin.Length < 3 || !lod0) return -1f;
            var mf = lod0.GetComponent<MeshFilter>(); if (!mf || !mf.sharedMesh) return -1f;
            var mb = mf.sharedMesh.bounds; var m = lod0.localToWorldMatrix;
            Vector3 lo = Vector3.one * float.MaxValue, hi = Vector3.one * float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                var c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var w = m.MultiplyPoint3x4(c); lo = Vector3.Min(lo, w); hi = Vector3.Max(hi, w);
            }
            var wantLo = new Vector3(r.bmin[0], r.bmin[2] + r.dz, r.bmin[1]); var wantHi = new Vector3(r.bmax[0], r.bmax[2] + r.dz, r.bmax[1]);
            var d1 = lo - wantLo; var d2 = hi - wantHi;
            float e = Mathf.Max(Mathf.Abs(d1.x), Mathf.Abs(d1.z), Mathf.Abs(d2.x), Mathf.Abs(d2.z), Mathf.Abs(d2.y));
            return r.sunk ? e : Mathf.Max(e, Mathf.Abs(d1.y));
        }

        static void ImportKit()
        {
            var mi = (ModelImporter)AssetImporter.GetAtPath(Kit); if (mi == null) return;
            mi.importCameras = false; mi.importLights = false; mi.importAnimation = false; mi.animationType = ModelImporterAnimationType.None;
            mi.bakeAxisConversion = true; mi.isReadable = false; mi.meshCompression = ModelImporterMeshCompression.Off; mi.addCollider = false;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportStandard; mi.materialLocation = ModelImporterMaterialLocation.InPrefab; mi.SaveAndReimport();
        }

        static Material Stone(string name, string tex, Color tint, bool seeThru)
        {
            var m = OpenZL16Rocks.RockMat(name, tex, false, seeThru);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", tint);
            EditorUtility.SetDirty(m); return m;
        }

        static Dictionary<string, Material> Materials(System.Text.StringBuilder sb)
        {
            var mats = new Dictionary<string, Material>();
            string P(string f) { string p = $"{Tex}/{f}.png"; if (!File.Exists(p)) sb.Append($" [picture missing: {f}]"); return p; }
            OpenZL16Water.ImportTex(P("L16_ruins_sheet"), false, 4096);
            mats["sheet"] = Stone("M_L16_St_sheet", P("L16_ruins_sheet"), Color.white, true);
            foreach (var k in new[] { "pyramid_intact", "pyramid_damaged", "pyramid_mound" })
            {
                OpenZL16Water.ImportTex(P("L16_" + k), false, 2048);
                mats[k] = Stone("M_L16_St_" + k, P("L16_" + k), Color.white, true);
            }
            if (!File.Exists(Paving)) sb.Append(" [the old paving picture of the ground is missing: footings stay untextured]");
            mats["footing"] = Stone("M_L16_St_footing_sunk", Paving, new Color(0.5f, 0.52f, 0.42f), false);      // under water: darker and greener than the dry paving
            mats["footing_dry"] = Stone("M_L16_St_footing", Paving, Color.white, false);
            mats["chunk"] = Stone("M_L16_St_chunk", Paving, new Color(0.86f, 0.7f, 0.5f), false);                 // flying pyramid stone: the paving stone, ochre
            foreach (var k in new[] { "fire_stone_moss", "fire_wood_bark", "fire_wood_charred", "fire_wood_ember", "fire_ash_cold", "fire_ash_glowing", "fire_iron_rust" })
            {
                OpenZL16Water.ImportTex(P("L16_" + k), true, 512);
                var m = OpenZL16Water.LitMat("M_L16_St_" + k, P("L16_" + k), 0.05f, Dir);
                if (k == "fire_wood_ember" || k == "fire_ash_glowing")
                {   // the lit state glows by itself (it is shown only while the fire burns)
                    m.EnableKeyword("_EMISSION");
                    if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", new Color(1f, 0.36f, 0.1f) * (k == "fire_wood_ember" ? 1.5f : 0.9f));
                    EditorUtility.SetDirty(m);
                }
                mats[k] = m;
            }
            OpenZL16Water.ImportTex(P("L16_hut"), false, 1024); OpenZL16Water.ImportTex(P("L16_hut_palette"), false, 128);
            mats["hut"] = OpenZL16Water.LitMat("M_L16_St_hut", P("L16_hut"), 0.04f, Dir);
            mats["hut_palette"] = OpenZL16Water.LitMat("M_L16_St_hut_palette", P("L16_hut_palette"), 0.04f, Dir);
            return mats;
        }

        sealed class Ctx
        {
            public Dictionary<string, Transform> shape = new Dictionary<string, Transform>(); public Dictionary<string, Material> mats; public int layer; public long tris, trisFar;
            public Material[] MatsOf(Transform proto, bool dryFooting)
            {
                var ms = proto.GetComponent<MeshRenderer>().sharedMaterials;
                for (int i = 0; i < ms.Length; i++)
                {
                    string k = ms[i] ? ms[i].name.Replace("EXP_", "") : "sheet";
                    if (k == "footing" && dryFooting) k = "footing_dry";
                    ms[i] = mats.TryGetValue(k, out var mm) ? mm : mats["sheet"];
                }
                return ms;
            }
            /// <summary>One copy of a kit shape under parent, in the kit's own local pose.</summary>
            public GameObject One(Transform parent, string shapeName, string name, bool dryFooting, bool shadows)
            {
                if (!shape.TryGetValue(shapeName, out var proto)) return null;
                var go = Object.Instantiate(proto.gameObject, parent, false); go.name = name; go.layer = layer;
                var r = go.GetComponent<MeshRenderer>(); r.sharedMaterials = MatsOf(proto, dryFooting);
                r.shadowCastingMode = shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = true;
                return go;
            }
            /// <summary>The versions of a shape behind one level-of-detail switch on `host`. Returns the nearest version.</summary>
            public Transform Lods(GameObject host, string shapeName, bool farOnly, bool dryFooting, float t0 = 0.12f, float t1 = 0.035f, float t2 = 0.004f)
            {
                var lods = new List<LOD>(); Transform first = null;
                var a = farOnly ? null : One(host.transform, shapeName, "LOD0", dryFooting, true);
                var b = One(host.transform, shapeName + "_LOD1", "LOD1", dryFooting, true);
                var c = One(host.transform, shapeName + "_LOD2", "LOD2", dryFooting, false);
                if (!a && !b && !c) { a = One(host.transform, shapeName, "LOD0", dryFooting, true); if (!a) return null; return a.transform; }   // a shape with one version
                if (a) { lods.Add(new LOD(b || c ? t0 : t2, new Renderer[] { a.GetComponent<Renderer>() })); first = a.transform; tris += Tris(a); }
                if (b) { lods.Add(new LOD(c ? t1 : t2, new Renderer[] { b.GetComponent<Renderer>() })); if (!first) first = b.transform; }
                if (c) { lods.Add(new LOD(t2, new Renderer[] { c.GetComponent<Renderer>() })); if (!first) first = c.transform; trisFar += Tris(c); }
                if (lods.Count > 1) { var lg = host.AddComponent<LODGroup>(); lg.fadeMode = LODFadeMode.None; lg.SetLODs(lods.ToArray()); lg.RecalculateBounds(); }
                return first;
            }
            static long Tris(GameObject g) { var m = g.GetComponent<MeshFilter>().sharedMesh; long n = 0; for (int i = 0; i < m.subMeshCount; i++) n += m.GetIndexCount(i) / 3; return n; }
        }

        [MenuItem("OpenZ/L16 Jungle/9 Structures (pyramids, ruins, fires, huts, lamp posts; open level 16 scene)")]
        public static void StructuresMenu() => Debug.Log("[OpenZ L16] " + Structures());

        public static string Structures()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (!scene.name.StartsWith("L16")) return "open the level 16 scene first";
            if (Application.isPlaying) return "stop Play first";
            var env = GameObject.Find("Environment"); if (!env) return "Environment missing";
            var doc = Load(); if (doc == null || doc.ruins == null) return "placement list missing or unreadable: " + PlacesPath;
            if (!File.Exists(Kit)) return "missing " + Kit;
            var sb = new System.Text.StringBuilder();
            ImportKit();
            var ctx = new Ctx { mats = Materials(sb) };
            var kit = AssetDatabase.LoadAssetAtPath<GameObject>(Kit);
            foreach (Transform t in kit.transform) ctx.shape[t.name] = t;
            var ground = env.transform.Find("TER_Ground"); ctx.layer = ground ? ground.gameObject.layer : 0;
            var col = GameObject.Find("COL_Terrain")?.GetComponent<Collider>();
            Physics.SyncTransforms();
            float Ground(float x, float z, float fallback) => col && col.Raycast(new Ray(new Vector3(x, 300f, z), Vector3.down), out var h, 600f) ? h.point.y : fallback;

            var old = env.transform.Find("L16_Structures"); if (old) Object.DestroyImmediate(old.gameObject);
            var root = new GameObject("L16_Structures").transform; root.SetParent(env.transform, false);
            root.gameObject.AddComponent<L16LampPosts>();
            var ruins = new GameObject("Ruins").transform; ruins.SetParent(root, false);
            var surround = new GameObject("Surround").transform; surround.SetParent(root, false);
            var firesT = new GameObject("L16_Fires").transform; firesT.SetParent(root, false);

            // ---- ruin pieces, footings, the surround
            int placed = 0, far = 0, footings = 0, boxOk = 0, boxAll = 0, noShape = 0; float worst = 0f; string worstAt = "";
            var byName = new Dictionary<string, Transform>();
            foreach (var r in doc.ruins)
            {
                if (r.kind == "pyramid") continue;
                Place(r.loc, r.quat_wxyz, r.scale, r.dz, out var pos, out var rot, out var scl);
                var go = new GameObject(r.name); go.transform.SetParent(r.kind == "surround" ? surround : ruins, false);
                go.transform.SetPositionAndRotation(pos, rot); go.transform.localScale = scl; go.layer = ctx.layer;
                var first = ctx.Lods(go, r.shape, r.far_only, r.shape == "ST_pyramid_pad");
                if (!first) { noShape++; Object.DestroyImmediate(go); continue; }
                byName[r.name] = go.transform;
                if (r.kind == "footing") { footings++; continue; }
                if (r.kind == "surround") far++; else placed++;
                if (r.shape == "ST_causeway_slabs") continue;
                float e = BoxDiff(r, first);
                if (e >= 0f) { boxAll++; if (e <= Tol(r)) boxOk++; if (e > worst) { worst = e; worstAt = r.name; } }
            }

            // ---- pyramids: three stages under one object named by its uid (the rules' map item is bound by that name)
            var chunks = Enumerable.Range(0, 8).Select(i => AssetDatabase.LoadAllAssetsAtPath($"Assets/OpenZ/Models/RocksZ/RockZ_Desert_Chunk_{i}.fbx").OfType<Mesh>().FirstOrDefault()).Where(x => x).ToArray();
            int pyramids = 0; var pyrOf = new Dictionary<string, L16Pyramid>();
            foreach (var p in doc.pyramids ?? new PyrRow[0])
            {
                var r = doc.ruins.FirstOrDefault(x => x.name == p.name); if (r == null) continue;
                Place(r.loc, r.quat_wxyz, new[] { 1f, 1f, 1f }, r.dz, out var pos, out var rot, out _);
                var go = new GameObject(p.uid); go.transform.SetParent(root, false); go.transform.SetPositionAndRotation(pos, rot); go.layer = ctx.layer;
                var stages = new GameObject("Stages").transform; stages.SetParent(go.transform, false); stages.localScale = Vector3.one * p.scale;
                GameObject Stage(string n, string shapeName) { var s = new GameObject(n); s.transform.SetParent(stages, false); s.layer = ctx.layer; ctx.Lods(s, shapeName, false, false, 0.25f, 0.07f, 0.004f); return s; }
                var intact = Stage("Intact", "ST_pyramid_intact"); var damaged = Stage("Damaged", "ST_pyramid_damaged"); var mound = Stage("RUIN_Mound", "ST_pyramid_mound");
                damaged.SetActive(false);
                var dp = go.AddComponent<DetailProp>();
                dp.id = p.uid; dp.kind = "pyramid"; dp.health = 3f; dp.hitRadius = 8f * p.scale; dp.hitHeight = 9.4f * p.scale; dp.blastOnly = true; dp.intact = false;
                dp.mat = "stone"; dp.debris = new[] { new Color(0.52f, 0.37f, 0.2f), new Color(0.4f, 0.29f, 0.16f), new Color(0.55f, 0.56f, 0.18f) }; dp.debrisCount = 42; dp.scorch = 0f;
                dp.breakEvent = "dst_rock_crash_large"; dp.hitEvent = "dst_rock_chip"; dp.knockWobble = 0f; dp.glowAtNight = false; dp.sway = 0f;
                var lp = go.AddComponent<L16Pyramid>(); lp.intact = intact; lp.damaged = damaged; lp.chunks = chunks; lp.chunkMaterial = ctx.mats["chunk"]; lp.size = p.scale; lp.fireIds = p.fires ?? new string[0];
                pyrOf[p.name] = lp; pyramids++;
                var first = intact.transform.Find("LOD0"); float e = BoxDiff(r, first);
                if (e >= 0f) { boxAll++; if (e <= Tol(r)) boxOk++; if (e > worst) { worst = e; worstAt = r.name; } }
            }

            // ---- fires: the out shape and the lit shape of each, under the object the fire component reads them from
            firesT.gameObject.AddComponent<L16Fires>(); int fires = 0; var perPyr = new Dictionary<string, List<GameObject>>();
            foreach (var f in doc.fires ?? new FireRow[0])
            {
                Place(f.loc, f.quat_wxyz, f.scale, f.dz, out var pos, out var rot, out var scl);
                var go = new GameObject("Fire_" + f.id); go.transform.SetParent(firesT, false); go.transform.SetPositionAndRotation(pos, rot); go.transform.localScale = scl; go.layer = ctx.layer;
                GameObject o, l;
                if (f.kind == "bowl")
                {   // one shape (near, middle, far versions); lit = its glowing coals on top
                    o = new GameObject("Out"); o.transform.SetParent(go.transform, false); o.layer = ctx.layer; ctx.Lods(o, f.shape_out, false, false, 0.1f, 0.03f, 0.004f);
                    l = ctx.One(go.transform, f.shape_lit, "Lit", false, false);
                }
                else { o = ctx.One(go.transform, f.shape_out, "Out", false, true); l = ctx.One(go.transform, f.shape_lit, "Lit", false, true); }
                if (!o || !l) { sb.Append($" [fire {f.id}: shape missing]"); Object.DestroyImmediate(go); continue; }
                l.SetActive(false); fires++;
                if (!string.IsNullOrEmpty(f.host) && pyrOf.ContainsKey(f.host)) { if (!perPyr.TryGetValue(f.host, out var lst)) perPyr[f.host] = lst = new List<GameObject>(); lst.Add(go); }
            }
            foreach (var kv in perPyr)
            {   // the bowls on a pyramid's ledges fall with its top: the pyramid knows their models, in the order of its fire ids
                var lp = pyrOf[kv.Key]; lp.fireModels = lp.fireIds.Select(id => kv.Value.FirstOrDefault(g => g.name == "Fire_" + id)).ToArray();
            }

            // ---- the 18 huts of the map, each on its own scene object
            int huts = 0; var hutMissing = new List<string>(); var zod = GameObject.Find("ZodObjects");
            foreach (var h in doc.huts ?? new HutRow[0])
            {
                var t = zod ? zod.transform.Find(h.name) : null;
                if (!t) { hutMissing.Add(h.name); continue; }
                if (PrefabUtility.IsPartOfPrefabInstance(t.gameObject)) PrefabUtility.UnpackPrefabInstance(PrefabUtility.GetOutermostPrefabInstanceRoot(t.gameObject), PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                for (int i = t.childCount - 1; i >= 0; i--) Object.DestroyImmediate(t.GetChild(i).gameObject);
                foreach (var c in t.GetComponents<Component>()) if (!(c is Transform)) Object.DestroyImmediate(c);
                float x = h.tx * 2 + 1, z = -(h.ty * 2 + 1);
                float lowest = Mathf.Min(Mathf.Min(Ground(x - 0.8f, z - 0.8f, t.position.y), Ground(x + 0.8f, z - 0.8f, t.position.y)), Mathf.Min(Ground(x - 0.8f, z + 0.8f, t.position.y), Ground(x + 0.8f, z + 0.8f, t.position.y)));
                t.SetPositionAndRotation(new Vector3(x, lowest - 0.04f, z), Quaternion.identity); t.localScale = Vector3.one;
                var hut = new GameObject("Hut"); hut.transform.SetParent(t, false); hut.layer = ctx.layer; ctx.Lods(hut, "ST_hut", false, false, 0.1f, 0.03f, 0.004f);
                var ruin = ctx.One(t, "ST_hut_ruin", "RUIN", false, true);
                if (ruin) ruin.SetActive(false);
                var lh = t.gameObject.AddComponent<L16Hut>(); lh.hut = hut; lh.ruin = ruin;
                huts++;
            }

            // ---- lamp posts: the service lamp of level 2, from its own model (its materials come with it), as props under Environment
            for (int i = env.transform.childCount - 1; i >= 0; i--) { var c = env.transform.GetChild(i); if (c.name.StartsWith("DEC_Service_lamp_L16_")) Object.DestroyImmediate(c.gameObject); }
            int lamps = 0; string lampNote = "";
            var lampSrc = AssetDatabase.LoadAssetAtPath<GameObject>(LampModel);
            var lampT = lampSrc ? lampSrc.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name.StartsWith("DEC_Service_lamp") && t.childCount > 0 && t.GetComponentsInChildren<Renderer>(true).Length > 0) : null;
            if (!lampT) lampNote = " [the service lamp was not found in " + LampModel + ": NO lamp post placed]";
            else foreach (var p in doc.lamps ?? new LampRow[0])
            {
                var go = Object.Instantiate(lampT.gameObject); go.name = p.name; go.transform.SetParent(env.transform, true);
                foreach (var li in go.GetComponentsInChildren<Light>(true)) Object.DestroyImmediate(li);
                var rs = go.GetComponentsInChildren<Renderer>(true); Bounds all = rs[0].bounds; Renderer tallest = rs[0]; bool gotHead = false; Bounds head = default;
                foreach (var r in rs)
                {
                    all.Encapsulate(r.bounds); if (r.bounds.size.y > tallest.bounds.size.y) tallest = r;
                    if (r.name.ToLowerInvariant().Contains("glow")) { if (!gotHead) { head = r.bounds; gotHead = true; } else head.Encapsulate(r.bounds); }
                }
                var foot = new Vector3(tallest.bounds.center.x, all.min.y, tallest.bounds.center.z);
                var arm = (gotHead ? head.center : foot) - foot; arm.y = 0f;
                float yawT = arm.sqrMagnitude > 0.01f ? Mathf.Atan2(arm.x, arm.z) * Mathf.Rad2Deg : go.transform.eulerAngles.y;
                var dq = Quaternion.Euler(0f, p.yaw - yawT, 0f); var off = dq * (foot - go.transform.position);
                go.transform.rotation = dq * go.transform.rotation;
                float gy = Ground(p.x, p.z, foot.y);
                go.transform.position = new Vector3(p.x - off.x, gy - 0.03f - off.y, p.z - off.z);          // the arm points where the list says, the foot stands on the ground
                go.layer = ctx.layer; lamps++;
            }

            AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(scene);
            var cn = doc.counts ?? new Counts();
            return $"structures: ruin pieces {placed} of {cn.ruins}, surround {far} of {cn.surround}, footings {footings} of {cn.footings}, pyramids {pyramids} of {cn.pyramids} ({chunks.Length} chunk meshes), fires {fires} of {cn.fires}, " +
                   $"huts {huts} of {cn.huts}{(hutMissing.Count > 0 ? " (not found: " + string.Join(", ", hutMissing.Take(6)) + ")" : "")}, lamp posts {lamps} of {cn.lamps}{lampNote}; " +
                   $"boxes against the blockout list: {boxOk} of {boxAll} within {BoxTolerance:0.00} m (largest {worst:0.000} m at {worstAt}); shapes missing for {noShape}; " +
                   $"{ctx.tris} triangles in the near versions placed, {ctx.trisFar} in the far versions{(col ? "" : " [COL_Terrain missing: huts and lamps kept their heights]")}{sb}. SAVE THE SCENE.";
        }
    }
}
