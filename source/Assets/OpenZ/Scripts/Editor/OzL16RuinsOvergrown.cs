using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Level 16: the ridge ruins west of the middle overgrown, so the two standing columns on the ridge top no longer read as a
    /// bare pair of square stubs and the temple gateway at the ridge's foot no longer reads as a bare door in the earth wall.
    /// Places the level's own plants (the plant kit's bushes, ferns, a young palm, a banana plant, reeds and hanging vines, with
    /// the materials the level's plants of the same shape use) into the OPEN level 16 scene, under
    /// Environment/L16_Flora/P_RuinsOvergrownK284 (a group with one level of detail: the level's plant wind moves them as small plants).
    /// Every plant's foot is measured on the scene's own meshes in a probe scene (nothing of the level is used for it):
    /// - ground: the drawn ground, cliff and river bed (TER_Ground, TER_Cliffs, TER_RiverBed), the highest hit from above;
    /// - water: the drawn water surface (WAT_Water), as the level's reeds stand;
    /// - top: the ruin's own top surface (its LOD0 mesh), at a place given in the ruin's frame;
    /// - hang: a vine over the gateway's lintel: the hooks its strands end in at the top lie HookLift over the lintel's top, every
    ///   other point of the vine stands HangClear in front of the stone where it hangs, so the strands' tops sit at the top front edge;
    /// - hangT: a vine from the ridge's top edge, as the level hangs its cliff vines: its strand tops TopDrop under the ridge's
    ///   highest point at its x, their wall side HangClear from the wall.
    /// The plants are scenery only: no collider, nothing under ZodObjects, no rules data is read or written for them.
    /// Menu 1 reports what menu 2 would do and changes nothing. Menu 2 first copies the saved scene file to backups/l16_ruins_overgrown,
    /// places the group (one undo step) and saves the scene; run again it changes nothing. Menu 3 is the counted check of the
    /// placed group. Menu 4 removes the group (backup copy first) and saves.
    /// The group lives inside L16_Flora: the level's menu 8 (trees and plants) rebuilds L16_Flora and drops it; run menu 2 again after it.
    /// </summary>
    public static partial class OzL16RuinsOvergrown
    {
        const string Menu = "OpenZ/L16 Jungle/Ruins overgrown K284/";
        public const string RootName = "P_RuinsOvergrownK284";
        const string Prefix = "K284_";
        const string Kit = "Assets/OpenZ/Models/L16/Flora/L16_flora_kit.fbx";
        const string MatDir = "Assets/OpenZ/Models/L16/Flora";
        const string UndoName = "L16 ruins overgrown";
        static string BackupRoot => Path.Combine(OpenZPaths.RepoRoot, "backups", "l16_ruins_overgrown");

        // ---- the knobs (SPEC part 0 says when to turn which)
        /// <summary>A foot counts as on its surface within this (m); a vine's tops within this of the face they hang from.</summary>
        public const float SeatTol = 0.03f;
        /// <summary>A hanging vine's strands stand this far in front of the face (m).</summary>
        public const float HangClear = 0.02f;
        /// <summary>A lintel vine (kind hang): the kit's vine strands end at the top in a small hook bent back toward the stone. The
        /// vine's top band = its points within HookBand of its highest point; a hook = a top-band point at least HookMin behind the band's
        /// frontmost point (both in the vine's own frame, not turned). The hooks' lowest point lies HookLift over the stone's top, read
        /// HookBehind behind the stone's most forward face point; every strand's top within StrandTopMax of the stone right behind it
        /// (read StrandTopProbe under the top) (m).</summary>
        public const float HookBand = 0.03f, HookMin = 0.12f, HookLift = 0.01f, HookBehind = 0.10f, StrandTopMax = 0.15f;
        static readonly float[] StrandTopProbe = { 0.02f, 0.05f };
        /// <summary>A ridge vine's strand tops hang this far under the ridge's highest point at its x (m).</summary>
        public const float TopDrop = 0.15f;
        /// <summary>The z band the ridge's top edge is looked for in (world, m), and where the wall rays start.</summary>
        public const float EdgeZ0 = -159.4f, EdgeZ1 = -157.4f, WallRayFromZ = -165.4f;
        /// <summary>No plant point deeper than this inside a stone (m); no plant with more than this share of its points under the ground.</summary>
        public const float DeepMax = 0.30f, UnderMax = 0.25f;
        /// <summary>The tallest plant allowed on an open tile of ground or road (m): nothing tall stands in a way units walk.</summary>
        public const float LowOnOpen = 1.0f;
        /// <summary>A placed plant counts as unchanged within this (m, degrees).</summary>
        const float SameEps = 0.005f, SameDeg = 0.5f;
        /// <summary>A ruin farther than this from its place in the rows (m) stops the placing.</summary>
        const float RuinMoved = 0.2f;

        sealed class Row
        {
            public readonly string n, shape, kind, on; public readonly float x, z, yaw, s, sxy, sz, tx, ty, tz;
            public Row(string n, string shape, string kind, string on, float x, float z, float yaw, float s, float sxy, float sz, float tx, float ty, float tz)
            {
                this.n = n; this.shape = shape; this.kind = kind; this.on = on; this.x = x; this.z = z; this.yaw = yaw; this.s = s; this.sxy = sxy; this.sz = sz;
                this.tx = tx; this.ty = ty; this.tz = tz;
            }
            public bool Hangs => kind == "hang" || kind == "hangT";
            public float Size => Hangs ? sxy : s;
        }

        sealed class Plan { public Row r; public Transform proto; public Mesh mesh; public Vector3 pos; public Quaternion rot; public Vector3 scl; public string how = ""; }

        [Serializable] sealed class GridDoc { public int width, height; public int[] classes; }

        [MenuItem(Menu + "1 Report (dry run)")]
        public static void ReportMenu() => Debug.Log(Run(false));

        [MenuItem(Menu + "2 Place the plants (backup copy first, saves the scene)")]
        public static void PlaceMenu() => Debug.Log(Run(true));

        [MenuItem(Menu + "3 Counted check")]
        public static void CheckMenu() => Debug.Log(Check());

        [MenuItem(Menu + "4 Remove the plants (backup copy first, saves the scene)")]
        public static void RemoveMenu() => Debug.Log(Remove());

        // ------------------------------------------------------------------------------------------------ the probe scene
        /// <summary>Copies of the scene's ground, wall, water and ruin meshes as colliders in a preview scene: the level is not changed or dirtied.</summary>
        sealed class Probe : IDisposable
        {
            readonly Scene scene;
            public readonly List<Collider> Ground = new List<Collider>(), Wall = new List<Collider>();
            public Collider Water;
            public readonly Dictionary<string, Collider> Stone = new Dictionary<string, Collider>();
            public readonly Dictionary<string, Transform> Ruin = new Dictionary<string, Transform>();
            public readonly Dictionary<string, Bounds> Box = new Dictionary<string, Bounds>();
            public readonly StringBuilder Notes = new StringBuilder();

            public Probe(Transform env, Transform ruins)
            {
                scene = EditorSceneManager.NewPreviewScene();
                foreach (var n in new[] { "TER_Ground", "TER_Cliffs", "TER_RiverBed" })
                {
                    var c = Clone(Find(env, n));
                    if (!c) { Notes.Append($" [{n} not found]"); continue; }
                    Ground.Add(c); if (n != "TER_RiverBed") Wall.Add(c);
                }
                Water = Clone(Find(env, "WAT_Water")); if (!Water) Notes.Append(" [WAT_Water not found]");
                var lods = new Dictionary<string, Transform>();
                foreach (var name in RuinNames)
                {
                    var t = ruins ? ruins.Find(name) : null; if (!t) { Notes.Append($" [{name} not found]"); continue; }
                    Ruin[name] = t; var lod0 = t.Find("LOD0"); var mr = lod0 ? lod0.GetComponent<Renderer>() : null;
                    if (!mr) { Notes.Append($" [{name}: no LOD0]"); continue; }
                    Box[name] = mr.bounds; lods[name] = lod0;
                    var c = Clone(lod0); if (c) Stone[name] = c;
                }
                Physics.SyncTransforms();
                foreach (var kv in lods)
                {
                    if (!Stone.TryGetValue(kv.Key, out var c)) { Notes.Append($" [{kv.Key}: its stone could not be probed: the kit's table is used]"); continue; }
                    var b = Box[kv.Key]; bool any = false;                                                      // the copy answers rays where the stone is
                    for (int i = 0; i < 5 && !any; i++) for (int j = 0; j < 5 && !any; j++)
                            any = c.Raycast(new Ray(new Vector3(Mathf.Lerp(b.min.x, b.max.x, (i + 0.5f) / 5f), b.max.y + 5f, Mathf.Lerp(b.min.z, b.max.z, (j + 0.5f) / 5f)), Vector3.down), out _, 50f);
                    if (!any) { Stone.Remove(kv.Key); Notes.Append($" [{kv.Key}: its stone could not be probed: the kit's table is used]"); }
                }
            }

            Collider Clone(Transform src)
            {
                if (!src) return null;
                var mf = src.GetComponent<MeshFilter>(); if (!mf || !mf.sharedMesh) return null;
                var go = EditorUtility.CreateGameObjectWithHideFlags("probe_k284_" + src.name, HideFlags.HideAndDontSave);   // hidden from the start (as the editor's own preview scenes do), then into the probe scene
                SceneManager.MoveGameObjectToScene(go, scene);
                go.transform.SetPositionAndRotation(src.position, src.rotation); go.transform.localScale = src.lossyScale;
                var mc = go.AddComponent<MeshCollider>(); mc.sharedMesh = mf.sharedMesh;
                return mc;
            }

            public void Dispose() { EditorSceneManager.ClosePreviewScene(scene); }

            public static bool Down(Collider c, float x, float z, out float y)
            {
                y = 0f; if (!c || !c.Raycast(new Ray(new Vector3(x, 300f, z), Vector3.down), out var h, 600f)) return false;
                y = h.point.y; return true;
            }

            /// <summary>The highest drawn ground at (x, z), or NaN.</summary>
            public float GroundAt(float x, float z)
            {
                float best = float.NaN;
                foreach (var c in Ground) if (Down(c, x, z, out var y) && (float.IsNaN(best) || y > best)) best = y;
                return best;
            }

            public float WaterAt(float x, float z) => Down(Water, x, z, out var y) ? y : float.NaN;
            public float StoneTop(string n, float x, float z) => Stone.TryGetValue(n, out var c) && Down(c, x, z, out var y) ? y : float.NaN;

            /// <summary>The first point north (+z) of p on the colliders, its z, or NaN.</summary>
            public static float North(IEnumerable<Collider> cs, Vector3 p, float reach = 40f)
            {
                float best = float.NaN;
                foreach (var c in cs) if (c && c.Raycast(new Ray(p, Vector3.forward), out var h, reach) && (float.IsNaN(best) || h.point.z < best)) best = h.point.z;
                return best;
            }

            static Transform Find(Transform env, string n) => env ? env.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == n) : null;
        }

        // ------------------------------------------------------------------------------------------------ resolving the rows
        static Vector3[] Local(Plan p)
        {
            var v = p.mesh.vertices; var o = new Vector3[v.Length];
            for (int i = 0; i < v.Length; i++) o[i] = p.rot * Vector3.Scale(p.scl, v[i]);
            return o;
        }

        /// <summary>A shape's points sized and standing (the kit child's own turn) but not turned about up: the vine's own frame.</summary>
        static Vector3[] Own(Transform proto, Mesh mesh, Vector3 scl)
        {
            var v = mesh.vertices; var o = new Vector3[v.Length];
            for (int i = 0; i < v.Length; i++) o[i] = proto.localRotation * Vector3.Scale(scl, v[i]);
            return o;
        }

        /// <summary>A vine's top band and its hooks (top-band points well behind the band's front), from its points in its own frame.</summary>
        static void Hooks(Vector3[] own, out bool[] band, out bool[] hook)
        {
            float ymax = float.NegativeInfinity, zb = float.PositiveInfinity;
            foreach (var v in own) ymax = Mathf.Max(ymax, v.y);
            band = new bool[own.Length]; hook = new bool[own.Length];
            for (int i = 0; i < own.Length; i++) if (own[i].y >= ymax - HookBand) { band[i] = true; zb = Mathf.Min(zb, own[i].z); }
            for (int i = 0; i < own.Length; i++) hook[i] = band[i] && own[i].z >= zb + HookMin;
        }

        /// <summary>The connected parts (strands) of a mesh, its points welded at 1 mm: a label per point.</summary>
        static int[] Strands(Mesh mesh)
        {
            var v = mesh.vertices; var t = mesh.triangles; var key = new Dictionary<Vector3Int, int>(); var weld = new int[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                var k = Vector3Int.RoundToInt(v[i] * 1000f);
                if (!key.TryGetValue(k, out var id)) { id = key.Count; key[k] = id; }
                weld[i] = id;
            }
            var par = new int[key.Count]; for (int i = 0; i < par.Length; i++) par[i] = i;
            int F(int i) { while (par[i] != i) { par[i] = par[par[i]]; i = par[i]; } return i; }
            void U(int a, int b) { int ra = F(a), rb = F(b); if (ra != rb) par[ra] = rb; }
            for (int i = 0; i + 2 < t.Length; i += 3) { U(weld[t[i]], weld[t[i + 1]]); U(weld[t[i + 1]], weld[t[i + 2]]); }
            var lab = new int[v.Length]; for (int i = 0; i < v.Length; i++) lab[i] = F(weld[i]);
            return lab;
        }

        static List<Plan> Resolve(Probe pr, Dictionary<string, Transform> shapes, StringBuilder log, out int problems)
        {
            problems = 0; var plans = new List<Plan>();
            foreach (var r in Rows)
            {
                var p = new Plan { r = r };
                if (!shapes.TryGetValue(r.shape, out p.proto)) { problems++; log.Append($"    {r.n}: shape {r.shape} missing in the plant kit\n"); continue; }
                var mf = p.proto.GetComponent<MeshFilter>(); p.mesh = mf ? mf.sharedMesh : null;
                if (!p.mesh || !p.mesh.isReadable) { problems++; log.Append($"    {r.n}: the kit's mesh {r.shape} is missing or not readable\n"); continue; }
                p.rot = Quaternion.AngleAxis(r.yaw, Vector3.up) * p.proto.localRotation;
                p.scl = Vector3.Scale(r.Hangs ? new Vector3(r.sxy, r.sxy, r.sz) : Vector3.one * r.s, p.proto.localScale);
                Transform ruin = null;
                if (r.on.Length > 0 && !pr.Ruin.TryGetValue(r.on, out ruin)) { problems++; log.Append($"    {r.n}: {r.on} not in L16_Structures/Ruins\n"); continue; }
                switch (r.kind)
                {
                    case "ground":
                    {
                        float y = pr.GroundAt(r.x, r.z);
                        if (float.IsNaN(y)) { problems++; log.Append($"    {r.n}: no ground under ({r.x:0.00}, {r.z:0.00})\n"); continue; }
                        p.pos = new Vector3(r.x, y, r.z); p.how = "on the ground"; break;
                    }
                    case "water":
                    {
                        float y = pr.WaterAt(r.x, r.z);
                        if (float.IsNaN(y)) { problems++; log.Append($"    {r.n}: no water under ({r.x:0.00}, {r.z:0.00})\n"); continue; }
                        p.pos = new Vector3(r.x, y, r.z); p.how = "on the water"; break;
                    }
                    case "top":
                    {
                        var w = ruin.TransformPoint(new Vector3(r.x, 0f, r.z)); float y = pr.StoneTop(r.on, w.x, w.z);
                        if (float.IsNaN(y)) { y = ruin.position.y + r.ty; p.how = "on " + r.on + " (the kit's table)"; } else p.how = "on " + r.on;
                        p.pos = new Vector3(w.x, y, w.z); break;
                    }
                    case "hang":
                    {
                        if (Quaternion.Angle(ruin.rotation, Quaternion.identity) > 2f) { problems++; log.Append($"    {r.n}: {r.on} was turned since the kit was made: the vine rows need new places\n"); continue; }
                        if (!pr.Stone.TryGetValue(r.on, out var c)) { p.pos = ruin.TransformPoint(new Vector3(r.tx, r.ty, r.tz)); p.how = "from " + r.on + "'s lintel (the kit's table)"; break; }
                        var loc = Local(p); var b = pr.Box[r.on];
                        Hooks(Own(p.proto, p.mesh, p.scl), out _, out var hook);
                        float half = (loc.Max(v => v.x) - loc.Min(v => v.x)) * 0.5f, cx = ruin.TransformPoint(new Vector3(r.x, 0f, 0f)).x, z0 = b.min.z - 2f;
                        float zfu = float.PositiveInfinity, band = b.max.y - 2f, hookMin = float.PositiveInfinity;
                        for (int k = 0; k < loc.Length; k++) if (hook[k]) hookMin = Mathf.Min(hookMin, loc[k].y);
                        for (int i = 0; i <= 8; i++)
                            for (int j = 0; j < 32; j++)
                                if (c.Raycast(new Ray(new Vector3(cx - half + 2f * half * i / 8f, band + (2f - 0.02f) * j / 31f, z0), Vector3.forward), out var h, 10f)) zfu = Mathf.Min(zfu, h.point.z);
                        var tops = new List<float>();                                                   // the lintel's top just behind its most forward face point: the median
                        for (int i = 0; i <= 6; i++)
                            if (Probe.Down(c, cx - 0.8f * half + 1.6f * half * i / 6f, zfu + HookBehind, out var ty) && ty >= band) tops.Add(ty);
                        if (float.IsInfinity(zfu) || tops.Count == 0) { problems++; log.Append($"    {r.n}: no lintel face found on {r.on}\n"); continue; }
                        if (float.IsInfinity(hookMin)) { problems++; log.Append($"    {r.n}: the vine shape {r.shape} has no hooks at its top\n"); continue; }
                        tops.Sort();
                        float Y = tops[tops.Count / 2] + HookLift - hookMin, bound = float.PositiveInfinity;   // the hooks on the lintel's top
                        for (int k = 0; k < loc.Length; k++)                                            // every other point HangClear in front of the stone
                        {
                            if (hook[k]) continue; var v = loc[k];
                            if (c.Raycast(new Ray(new Vector3(cx + v.x, Y + v.y, z0), Vector3.forward), out var h, 10f)) bound = Mathf.Min(bound, h.point.z - v.z - HangClear);
                        }
                        if (float.IsInfinity(bound)) { problems++; log.Append($"    {r.n}: no lintel face behind the vine on {r.on}\n"); continue; }
                        p.pos = new Vector3(cx, Y, bound); p.how = "over " + r.on + "'s lintel edge"; break;
                    }
                    case "hangT":
                    {
                        var loc = Local(p); float ymax = loc.Max(v => v.y), yc = float.NegativeInfinity;
                        for (float zz = EdgeZ0; zz < EdgeZ1; zz += 0.05f) { float y = pr.GroundAt(r.x, zz); if (!float.IsNaN(y)) yc = Mathf.Max(yc, y); }
                        if (float.IsInfinity(yc)) { problems++; log.Append($"    {r.n}: no ridge edge found at x {r.x:0.00}\n"); continue; }
                        float Y = yc - TopDrop - ymax, bound = float.PositiveInfinity;
                        foreach (var v in loc)
                        {
                            if (v.y < ymax - 0.15f) continue;
                            float zh = Probe.North(pr.Wall, new Vector3(r.x + v.x, Y + v.y, WallRayFromZ));
                            if (!float.IsNaN(zh)) bound = Mathf.Min(bound, zh - v.z - HangClear);
                        }
                        if (float.IsInfinity(bound)) { problems++; log.Append($"    {r.n}: no wall found north of x {r.x:0.00}\n"); continue; }
                        p.pos = new Vector3(r.x, Y, bound); p.how = "from the ridge's top edge"; break;
                    }
                    default: problems++; log.Append($"    {r.n}: unknown kind {r.kind}\n"); continue;
                }
                plans.Add(p);
            }
            return plans;
        }

        // ------------------------------------------------------------------------------------------------ the run
        static bool Context(StringBuilder log, out Scene scene, out Transform env, out Transform flora, out Transform ruins, out Dictionary<string, Transform> shapes)
        {
            scene = EditorSceneManager.GetActiveScene(); env = flora = ruins = null; shapes = null;
            if (EditorApplication.isPlayingOrWillChangePlaymode) { log.Append("  stop Play mode first"); return false; }
            if (!scene.name.StartsWith("L16")) { log.Append("  open L16_Jungle first"); return false; }
            var e = GameObject.Find("Environment"); env = e ? e.transform : null;
            flora = env ? env.Find("L16_Flora") : null;
            var st = GameObject.Find("L16_Structures"); ruins = st ? st.transform.Find("Ruins") : null;
            if (!env || !flora || !ruins) { log.Append("  Environment, Environment/L16_Flora or L16_Structures/Ruins not found"); return false; }
            if ((flora.position - Vector3.zero).sqrMagnitude > 1e-6f || Quaternion.Angle(flora.rotation, Quaternion.identity) > 0.01f || (flora.lossyScale - Vector3.one).sqrMagnitude > 1e-6f)
            { log.Append("  L16_Flora is not at the origin with size 1 (the kit expects the level's own)"); return false; }
            var kit = AssetDatabase.LoadAssetAtPath<GameObject>(Kit);
            if (!kit) { log.Append("  missing " + Kit); return false; }
            shapes = new Dictionary<string, Transform>(); foreach (Transform t in kit.transform) shapes[t.name] = t;
            return true;
        }

        static bool Same(Transform root, List<Plan> plans)
        {
            if (!root || root.childCount != plans.Count) return false;
            foreach (var p in plans)
            {
                var c = root.Find(Prefix + p.r.n); if (!c) return false;
                var mf = c.GetComponent<MeshFilter>();
                if (!mf || mf.sharedMesh != p.mesh || (c.position - p.pos).magnitude > SameEps || Quaternion.Angle(c.rotation, p.rot) > SameDeg || (c.localScale - p.scl).magnitude > 0.001f) return false;
            }
            return true;
        }

        static string GridHash()
        {
            var ta = Resources.Load<TextAsset>("OpenZ/Maps/L16_grid"); if (!ta) return "missing";
            using (var sha = System.Security.Cryptography.SHA1.Create()) return BitConverter.ToString(sha.ComputeHash(ta.bytes)).Replace("-", "").Substring(0, 12);
        }

        static string Backup(Scene scene, StringBuilder log)
        {
            var dir = Path.Combine(BackupRoot, DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(dir);
            string prj = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            File.Copy(Path.Combine(prj, scene.path), Path.Combine(dir, Path.GetFileName(scene.path)), true);
            File.WriteAllText(Path.Combine(dir, "scene_path.txt"), scene.path);
            log.Append($"  backup copy of the saved scene: {dir}\n");
            return dir;
        }

        public static string Run(bool apply)
        {
            var log = new StringBuilder($"[OpenZ L16 ruins overgrown] {(apply ? "place" : "report (dry run)")}\n");
            if (!Context(log, out var scene, out var env, out var flora, out var ruins, out var shapes)) return log.ToString();
            bool dirtyAtStart = scene.isDirty;                                                               // read BEFORE the probe scene is made: only the user's own unsaved changes count
            var old = flora.Find(RootName);
            List<Plan> plans; int problems;
            using (var pr = new Probe(env, ruins))
            {
                if (pr.Notes.Length > 0) log.Append("  notes:" + pr.Notes + "\n");
                plans = Resolve(pr, shapes, log, out problems);
                foreach (var kv in RuinAt)                                                                  // the rows were made round the ruins where they stood then
                    if (pr.Ruin.TryGetValue(kv.Key, out var rt) && (rt.position - kv.Value).magnitude > RuinMoved)
                    { problems++; log.Append($"    {kv.Key} stands {(rt.position - kv.Value).magnitude:0.00} m from where it stood when the kit was made: the rows need new places\n"); }
            }
            if (problems > 0) return log.Append($"  STOP: {problems} plant(s) could not be placed (above): nothing was changed").ToString();
            foreach (var p in plans)
                log.Append($"    {Prefix + p.r.n,-24} {p.r.shape,-20} {p.how,-36} at ({p.pos.x:0.00}, {p.pos.y:0.00}, {p.pos.z:0.00}) turn {p.r.yaw:0} size {p.r.Size:0.00}{(p.r.Hangs ? $" x {p.r.sz:0.00}" : "")}\n");
            if (Same(old, plans)) return log.Append($"  already in place: the {plans.Count} plants under {RootName} stand as planned; nothing changed").ToString();
            if (!apply) return log.Append($"  would {(old ? "replace the group (it differs from the rows)" : "place")} {plans.Count} plants under Environment/L16_Flora/{RootName}; nothing changed (menu 2 places them)").ToString();
            if (dirtyAtStart) return log.Append("  save the scene first (the backup copy is the saved scene file): nothing changed").ToString();

            // the level's own look per shape: the materials and shadow settings of a level plant with the same mesh
            var refs = new Dictionary<Mesh, MeshRenderer>();
            foreach (var mf in flora.GetComponentsInChildren<MeshFilter>(true))
            {
                if (old && mf.transform.IsChildOf(old)) continue;
                var m = mf.sharedMesh; if (!m || refs.ContainsKey(m)) continue;
                var mr = mf.GetComponent<MeshRenderer>(); if (mr && mr.sharedMaterials.Length > 0 && mr.sharedMaterials.All(x => x)) refs[m] = mr;
            }
            var mats = new Dictionary<Plan, Material[]>(); var lookFrom = new Dictionary<Plan, MeshRenderer>();
            foreach (var p in plans)
            {
                if (refs.TryGetValue(p.mesh, out var rr)) { mats[p] = rr.sharedMaterials; lookFrom[p] = rr; continue; }
                var protoR = p.proto.GetComponent<MeshRenderer>();
                var ms = protoR ? protoR.sharedMaterials.Select(m => m ? AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/M_L16_Flora_{m.name.Replace("EXP_", "")}.mat") : null).ToArray() : new Material[0];
                if (ms.Length == 0 || ms.Any(m => !m)) return log.Append($"  STOP: no level material found for {p.r.shape}: nothing changed").ToString();
                mats[p] = ms;
            }

            string hashBefore = GridHash();
            Backup(scene, log);
            Undo.IncrementCurrentGroup(); Undo.SetCurrentGroupName(UndoName); int group = Undo.GetCurrentGroup();
            if (old) Undo.DestroyObjectImmediate(old.gameObject);
            var root = new GameObject(RootName).transform;
            Undo.RegisterCreatedObjectUndo(root.gameObject, UndoName);
            root.SetParent(flora, false);
            var ground = env.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "TER_Ground"); int layer = ground ? ground.gameObject.layer : 0;
            root.gameObject.layer = layer;
            var rends = new List<Renderer>();
            foreach (var p in plans)
            {
                var go = Object.Instantiate(p.proto.gameObject, root, false); go.name = Prefix + p.r.n; go.layer = layer;
                GameObjectUtility.SetStaticEditorFlags(go, 0);
                go.transform.SetPositionAndRotation(p.pos, p.rot); go.transform.localScale = p.scl;
                foreach (var col in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(col);
                var r = go.GetComponent<MeshRenderer>(); r.sharedMaterials = mats[p];
                if (lookFrom.TryGetValue(p, out var lf)) { r.shadowCastingMode = lf.shadowCastingMode; r.receiveShadows = lf.receiveShadows; r.lightProbeUsage = lf.lightProbeUsage; r.reflectionProbeUsage = lf.reflectionProbeUsage; }
                else r.shadowCastingMode = p.r.shape.Contains("VineCurtain") ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
                rends.Add(r);
            }
            var lg = root.gameObject.AddComponent<LODGroup>();
            lg.fadeMode = LODFadeMode.None; lg.SetLODs(new[] { new LOD(0.01f, rends.ToArray()) }); lg.RecalculateBounds();
            Undo.CollapseUndoOperations(group);
            string check;                                                                                   // the counted check (its own probe scene) BEFORE the save: the scene is left saved and clean
            try { check = Check(); }
            catch (Exception e) { check = $"  CHECK PROBLEMS: the counted check stopped ({e.GetType().Name}: {e.Message}); the plants are placed and saved, run menu 3\n"; }
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            string hashAfter = GridHash();
            log.Append($"  {(old ? "replaced" : "placed")}: {plans.Count} plants under Environment/L16_Flora/{RootName} (one undo step until the next save), scene saved; " +
                       $"the level grid L16_grid {(hashBefore == hashAfter ? "unchanged" : "CHANGED")} ({hashBefore} -> {hashAfter})\n");
            log.Append(check);
            return log.ToString();
        }

        public static string Remove()
        {
            var log = new StringBuilder("[OpenZ L16 ruins overgrown] remove\n");
            if (!Context(log, out var scene, out var env, out var flora, out var ruins, out var shapes)) return log.ToString();
            var old = flora.Find(RootName);
            if (!old) return log.Append($"  no {RootName} in the scene: nothing to remove").ToString();
            if (scene.isDirty) return log.Append("  save the scene first (the backup copy is the saved scene file): nothing changed").ToString();
            Backup(scene, log);
            Undo.DestroyObjectImmediate(old.gameObject);
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            return log.Append($"  removed {RootName}, scene saved").ToString();
        }

        // ------------------------------------------------------------------------------------------------ the counted check
        static readonly Vector3[] Parity = { new Vector3(0f, 1f, 0f), new Vector3(0.577f, -0.577f, 0.577f).normalized, new Vector3(-0.577f, 0.3f, -0.76f).normalized };
        static readonly Vector3[] Axes = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };

        /// <summary>Inside a closed stone: two of three rays cross its surface an odd number of times (back faces counted).</summary>
        static bool Inside(Collider c, Vector3 p)
        {
            if (!c.bounds.Contains(p)) return false;
            int votes = 0;
            foreach (var d in Parity)
            {
                int n = 0; var o = p;
                for (int k = 0; k < 64; k++)
                {
                    if (!c.Raycast(new Ray(o, d), out var h, 60f)) break;
                    n++; o = h.point + d * 0.0005f;
                }
                if ((n & 1) == 1) votes++;
            }
            return votes >= 2;
        }

        static float Depth(Collider c, Vector3 p)
        {
            float best = float.PositiveInfinity;
            foreach (var d in Axes) if (c.Raycast(new Ray(p, d), out var h, 10f)) best = Mathf.Min(best, h.distance);
            return best;
        }

        public static string Check()
        {
            var log = new StringBuilder("[OpenZ L16 ruins overgrown] counted check\n");
            if (!Context(log, out var scene, out var env, out var flora, out var ruins, out var shapes)) return log.ToString();
            var root = flora.Find(RootName);
            if (!root) return log.Append($"  BAD no {RootName} under Environment/L16_Flora (menu 2 places it)").ToString();
            var ta = Resources.Load<TextAsset>("OpenZ/Maps/L16_grid"); var g = ta ? JsonUtility.FromJson<GridDoc>(ta.text) : null;
            int ok = 0, total = 0;
            void Line(bool good, string text) { total++; if (good) ok++; log.Append(good ? "  OK  " : "  BAD ").Append(text).Append('\n'); }

            var names = new HashSet<string>(Rows.Select(r => Prefix + r.n));
            int placed = 0, extra = 0, seated = 0, strandsAll = 0, strandsAir = 0, hookedAll = 0, baseInStone = 0, baseInBox = 0, deepPlants = 0, inPoints = 0, flat = 0, halfIn = 0, sizeOk = 0, tallOpen = 0, colliders = 0, zod = 0, moving = 0, statics = 0, inLod = 0, tableUsed = 0;
            float worstGap = 0f, worstDeep = 0f, worstUnder = 0f, worstStrandTop = 0f; string worstGapAt = "", worstDeepAt = "", worstUnderAt = "", worstStrandTopAt = "";
            var boxes = new List<string>(); var tiles = new StringBuilder(); var yaws = new HashSet<int>();
            var lg = root.GetComponent<LODGroup>(); var lod0 = lg && lg.lodCount > 0 ? new HashSet<Renderer>(lg.GetLODs()[0].renderers.Where(x => x)) : new HashSet<Renderer>();
            foreach (Transform c in root) if (!names.Contains(c.name)) extra++;
            bool hitBack = Physics.queriesHitBackfaces;
            try
            {
                Physics.queriesHitBackfaces = true;                                                         // the inside test counts back faces
                using (var pr = new Probe(env, ruins))
                {
                    if (pr.Notes.Length > 0) log.Append("  notes:" + pr.Notes + "\n");
                    foreach (var r in Rows)
                    {
                        var t = root.Find(Prefix + r.n); if (!t) continue; placed++;
                        var mf = t.GetComponent<MeshFilter>(); var mesh = mf ? mf.sharedMesh : null; var rend = t.GetComponent<Renderer>();
                        if (!mesh || !mesh.isReadable) continue;
                        if (rend && lod0.Contains(rend)) inLod++;
                        var M = t.localToWorldMatrix; var V = mesh.vertices; var W = new Vector3[V.Length];
                        float ymin = float.PositiveInfinity, ymax = float.NegativeInfinity;
                        for (int i = 0; i < V.Length; i++) { W[i] = M.MultiplyPoint3x4(V[i]); ymin = Mathf.Min(ymin, W[i].y); ymax = Mathf.Max(ymax, W[i].y); }
                        var b = t.position; float gap = float.NaN; bool hangOk = true;
                        switch (r.kind)
                        {
                            case "ground": gap = b.y - pr.GroundAt(b.x, b.z); break;
                            case "water": gap = b.y - pr.WaterAt(b.x, b.z); break;
                            case "top":
                                if (pr.Stone.ContainsKey(r.on)) gap = b.y - pr.StoneTop(r.on, b.x, b.z);
                                else if (pr.Ruin.TryGetValue(r.on, out var ru)) { gap = b.y - (ru.position.y + r.ty); tableUsed++; }
                                break;
                            case "hang":
                                if (pr.Stone.TryGetValue(r.on, out var sc) && shapes.TryGetValue(r.shape, out var proto))
                                {
                                    // per strand: its hooks' height over the lintel's top (0 in or under the stone, over air: none under it) and
                                    // its top's distance from the stone right behind it (a probe point inside the stone counts 0)
                                    Hooks(Own(proto, mesh, t.localScale), out var band, out var hook); var lab = Strands(mesh);
                                    float worstHook = 0f, worstTop = 0f;
                                    foreach (int k in lab.Distinct())
                                    {
                                        bool inBand = false, hasHook = false; float best = float.PositiveInfinity, bt = float.PositiveInfinity;
                                        for (int i = 0; i < W.Length; i++)
                                        {
                                            if (lab[i] != k || !band[i]) continue;
                                            inBand = true;
                                            if (hook[i])
                                            {
                                                hasHook = true;
                                                if (sc.Raycast(new Ray(W[i] + Vector3.up, Vector3.down), out var hh, 10f)) best = Mathf.Min(best, Mathf.Max(0f, W[i].y - hh.point.y));
                                                continue;
                                            }
                                            foreach (float dy in StrandTopProbe)
                                            {
                                                var o = W[i] + Vector3.down * dy;
                                                float d = Inside(sc, o) ? 0f : (sc.Raycast(new Ray(o, Vector3.forward), out var hf, 10f) ? hf.distance : float.PositiveInfinity);
                                                bt = Mathf.Min(bt, d);
                                            }
                                        }
                                        if (!inBand) continue;
                                        strandsAll++;
                                        if (hasHook) { hookedAll++; if (float.IsInfinity(best)) { strandsAir++; worstHook = 9f; } else worstHook = Mathf.Max(worstHook, best); }
                                        if (float.IsInfinity(bt)) { strandsAir++; worstTop = 9f; } else worstTop = Mathf.Max(worstTop, bt);
                                    }
                                    gap = worstHook; hangOk = worstTop <= StrandTopMax && worstHook < 9f;
                                    if (worstTop > worstStrandTop) { worstStrandTop = worstTop; worstStrandTopAt = t.name; }
                                }
                                else { gap = 0f; tableUsed++; }
                                break;
                            case "hangT":
                                foreach (var w in W)
                                {
                                    if (w.y < ymax - 0.15f) continue;
                                    float gy = pr.GroundAt(w.x, w.z); float d;
                                    if (!float.IsNaN(gy) && gy > w.y) d = -1f;
                                    else { float zh = Probe.North(pr.Wall, w, 5f); d = float.IsNaN(zh) ? float.PositiveInfinity : zh - w.z; }
                                    gap = float.IsNaN(gap) ? d : Mathf.Min(gap, d);
                                }
                                break;
                        }
                        bool seatOk = !float.IsNaN(gap) && hangOk && (r.Hangs ? gap >= -0.001f && gap <= SeatTol : Mathf.Abs(gap) <= SeatTol);
                        if (seatOk) seated++;
                        float ag = float.IsNaN(gap) ? 99f : Mathf.Abs(gap);
                        if (ag > worstGap) { worstGap = ag; worstGapAt = t.name; }
                        // the foot: never inside a stone; inside a stone's box only on its top or in the gateway's opening
                        if (!r.Hangs)
                        {
                            var foot = b + Vector3.up * 0.04f;
                            foreach (var kv in pr.Box) if (kv.Value.Contains(foot)) { baseInBox++; boxes.Add($"{t.name} in {kv.Key}'s box ({(r.kind == "top" ? "on its top" : "not in the stone")})"); }
                            foreach (var kv in pr.Stone) if (Inside(kv.Value, foot)) baseInStone++;
                        }
                        // the plant's points inside the stones
                        float deep = 0f;
                        foreach (var kv in pr.Stone)
                        {
                            var bb = kv.Value.bounds;
                            foreach (var w in W)
                            {
                                if (!bb.Contains(w) || !Inside(kv.Value, w)) continue;
                                inPoints++; deep = Mathf.Max(deep, Depth(kv.Value, w));
                            }
                        }
                        if (deep > DeepMax) deepPlants++;
                        if (deep > worstDeep) { worstDeep = deep; worstDeepAt = t.name; }
                        // flat faces on a stone's top (would flicker against it)
                        if (r.kind == "top")
                        {
                            var T = mesh.triangles;
                            for (int i = 0; i < T.Length; i += 3)
                            {
                                Vector3 a = W[T[i]], c2 = W[T[i + 1]], d2 = W[T[i + 2]];
                                if (Mathf.Abs(a.y - b.y) < 0.01f && Mathf.Abs(c2.y - b.y) < 0.01f && Mathf.Abs(d2.y - b.y) < 0.01f) flat++;
                            }
                        }
                        // points under the ground (a plant half in a slope)
                        if (!r.Hangs)
                        {
                            int step = Mathf.Max(1, W.Length / 80), n = 0, under = 0;
                            for (int i = 0; i < W.Length; i += step) { n++; float gy = pr.GroundAt(W[i].x, W[i].z); if (!float.IsNaN(gy) && W[i].y < gy - 0.05f) under++; }
                            float share = n > 0 ? (float)under / n : 0f;
                            if (share > UnderMax) halfIn++;
                            if (share > worstUnder) { worstUnder = share; worstUnderAt = t.name; }
                        }
                        // size, turn, tile, rules, wind
                        if (LevelSizes.TryGetValue(r.shape, out var range) && r.Size >= range.x - 0.001f && r.Size <= range.y + 0.001f && (!r.Hangs || (r.sz >= VineLength.x - 0.001f && r.sz <= VineLength.y + 0.001f))) sizeOk++;
                        yaws.Add(Mathf.RoundToInt(r.yaw));
                        int tx = Mathf.FloorToInt(b.x / 2f), ty = Mathf.FloorToInt(-b.z / 2f), cls = g != null && tx >= 0 && ty >= 0 && tx < g.width && ty * g.width + tx < g.classes.Length ? g.classes[ty * g.width + tx] : -1;
                        tiles.Append($" {tx},{ty}:{cls}");
                        if ((cls == 0 || cls == 1) && ymax - ymin > LowOnOpen && !r.Hangs) tallOpen++;
                        colliders += t.GetComponentsInChildren<Collider>(true).Length;
                        if (IsUnder(t, "ZodObjects")) zod++;
                        if (GameObjectUtility.AreStaticEditorFlagsSet(t.gameObject, StaticEditorFlags.BatchingStatic)) statics++;
                        int kind = OzFloraWindMath.KindOf(mesh.name, out _, out bool known);
                        if (known && kind != OzFloraWindMath.Still) moving++;
                    }
                }
            }
            finally { Physics.queriesHitBackfaces = hitBack; }

            int n0 = Rows.Length;
            Line(placed == n0 && extra == 0, $"placed: {placed} of {n0} plants under Environment/L16_Flora/{RootName}, {extra} other objects in it");
            Line(seated == n0 && strandsAir == 0, $"every foot on its surface within {SeatTol * 100f:0} cm (ground, water, stone top; a ridge vine's tops at the wall; a lintel vine's hooks on the lintel's top and every strand's top within {StrandTopMax * 100f:0} cm of the stone behind it): {seated} of {n0}, the worst {worstGap * 100f:0.0} cm{(worstGapAt.Length > 0 ? " (" + worstGapAt + ")" : "")}; lintel vine strands {strandsAll} ({hookedAll} with a hook), over air {strandsAir}, the worst strand top {worstStrandTop * 100f:0.0} cm from the stone{(worstStrandTopAt.Length > 0 ? " (" + worstStrandTopAt + ")" : "")}{(tableUsed > 0 ? $"; {tableUsed} measured by the kit's table only" : "")}");
            Line(baseInStone == 0, $"feet inside a stone: {baseInStone} (inside a stone's box, by design: {baseInBox}{(boxes.Count > 0 ? ": " + string.Join("; ", boxes) : "")})");
            Line(deepPlants == 0, $"plants poking into a stone deeper than {DeepMax * 100f:0} cm: {deepPlants} ({inPoints} leaf points touch a stone in all, the deepest {worstDeep * 100f:0} cm{(worstDeepAt.Length > 0 ? " at " + worstDeepAt : "")})");
            Line(flat == 0, $"flat faces lying on a stone's top (flicker): {flat}");
            Line(halfIn == 0, $"plants half in a slope (over {UnderMax * 100f:0} % of their points under the ground): {halfIn} (the most {worstUnder * 100f:0} %{(worstUnderAt.Length > 0 ? " at " + worstUnderAt : "")})");
            Line(sizeOk == n0 && yaws.Count >= n0 - 2, $"sizes within the level's own range per shape: {sizeOk} of {n0}; turns: {yaws.Count} different");
            Line(colliders == 0 && zod == 0 && tallOpen == 0, $"rules: no new blocked tile: {colliders} colliders on the plants, {zod} under ZodObjects, the level grid not written (hash {GridHash()}), plants taller than {LowOnOpen:0.0} m on an open ground / road tile: {tallOpen}; tiles (x,y:class){tiles}");
            Line(moving == n0 && statics == 0 && inLod == n0 && root.name.StartsWith("P_"), $"the level's wind: {moving} of {n0} plants are wind shapes, {statics} marked static, {inLod} in the group's level of detail, the group is a small-plant group (P_) under L16_Flora");
            return log.Append($"  {(ok == total ? "CHECK OK" : "CHECK PROBLEMS")} {ok}/{total} lines").ToString();
        }

        static bool IsUnder(Transform t, string rootName)
        {
            for (var p = t.parent; p; p = p.parent) if (p.name == rootName && !p.parent) return true;
            return false;
        }
    }
}
