using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using OpenZ.Sim;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Zod's L02 grid has 1-tile (2 m) water channels between sim Ground tiles. View only (the sim tiles stay): the rendered
    /// ground (TER_Ground, TER_RiverBed, GND3_FloorPatch) and the walk collider (COL_Terrain) get a SHALLOW trench along those
    /// channels, out to about the normal river width (~6 m of water): bed at water - 0.35 m (robots on the sim Ground tiles
    /// there wade via OpenZWading), smoothstep banks. The trench is min(ground, profile), so running it again changes nothing.
    /// Never touches cliffs (vertices above water + 1.3 m, Impassable tiles), roads, bridges or map objects (the trench rises
    /// out of the ground near them). After it: Water corners 1 Fix (WAT_Water grows into the trench), the bed map bake,
    /// Water v4 3 Wet sand band + 4 Shore band.
    /// </summary>
    public static class OpenZRiverWiden
    {
        const float Flat = 1.0f;          // flat shallow bed out to this distance from the channel tiles (m)
        const float Bank = 2.6f;          // smoothstep bank beyond it
        const float BedDepth = 0.35f;     // bed below the water surface
        const float TopAbove = 1.1f;      // the bank profile ends this high above the water
        const float MaxAbove = 1.3f;      // vertices higher than water + this are never moved (cliffs)
        const float ForbidMargin = 0.6f, ForbidRamp = 2.4f, ForbidRise = 1.6f;
        const float Wobble = 0.9f;        // the bank line meanders by up to about this (m): a natural river, not a canal

        // tile boxes (x0, x1, y0, y1) holding the 1-tile channels (map-edge ones skipped)
        static readonly int[][] Boxes = { new[] { 37, 43, 35, 43 }, new[] { 38, 44, 49, 52 }, new[] { 28, 34, 49, 51 }, new[] { 28, 28, 38, 43 },
                                          new[] { 31, 34, 34, 35 }, new[] { 40, 40, 7, 7 }, new[] { 39, 39, 28, 28 } };
        static readonly string[] Meshes = { "TER_Ground", "TER_RiverBed", "GND3_FloorPatch", "COL_Terrain" };
        static string Backup => Path.Combine(OpenZPaths.RepoRoot, "backups", "river_widen");

        [MenuItem("OpenZ/River widen/1 Widen thin L02 channels (open L02)")]
        public static void Widen() => Debug.Log(Run(true));

        /// <summary>Carve + Water corners 1 Fix + the floor patch again (the Fix resets it from the FBX) + seam / bank normals,
        /// </summary>
        [MenuItem("OpenZ/River widen/2 Widen + rebuild water (then run river_widen_bake.py, then 3)")]
        public static void Pipeline()
        {
            var log = new StringBuilder(Run(true)).Append('\n');
            log.Append(OpenZWaterCorners.FixText()).Append('\n');
            log.Append(Run(true)).Append('\n');                        // idempotent: only the reset floor patch moves again
            var bf = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
            var env = GameObject.Find("Environment");
            var ground = (List<MeshFilter>)typeof(OpenZWaterCorners).GetMethod("GroundFilters", bf).Invoke(null, new object[] { env });
            var writable = ground.Where(f => AssetDatabase.GetAssetPath(f.sharedMesh).EndsWith(".asset") && f.sharedMesh.isReadable).ToList();
            var args = new object[] { writable, 0f };
            int seam = (int)typeof(OpenZWaterCorners).GetMethod("SeamNormals", bf).Invoke(null, args);
            foreach (var f in writable) EditorUtility.SetDirty(f.sharedMesh);
            log.Append($"  seam normals again: {seam} vertices\n").Append(OpenZShoreBand.FlattenBankNormals());
            var fp = writable.FirstOrDefault(f => f.name == "GND3_FloorPatch");
            if (fp)
            {
                var m = fp.sharedMesh; var M = fp.transform.localToWorldMatrix; var inv = System.Globalization.CultureInfo.InvariantCulture;
                var sb = new StringBuilder("{\"v\":[");
                var vv = m.vertices; for (int i = 0; i < vv.Length; i++) { var p = M.MultiplyPoint3x4(vv[i]); if (i > 0) sb.Append(','); sb.Append(p.x.ToString("R", inv)).Append(',').Append(p.y.ToString("R", inv)).Append(',').Append(p.z.ToString("R", inv)); }
                sb.Append("],\"t\":["); var tt = m.triangles; for (int i = 0; i < tt.Length; i++) { if (i > 0) sb.Append(','); sb.Append(tt[i]); } sb.Append("]}");
                string dir = Path.Combine(OpenZPaths.RepoRoot, "work", "tmp", "river_widen"); Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "floorpatch_world.json"), sb.ToString());
                log.Append("  floor patch dumped for the bake: work/tmp/river_widen/floorpatch_world.json\n");
            }
            EditorSceneManager.MarkSceneDirty(env.scene); AssetDatabase.SaveAssets(); EditorSceneManager.SaveOpenScenes();
            Debug.Log("[OpenZ] River widen pipeline\n" + log);
        }

        [MenuItem("OpenZ/River widen/3 After the bake: bed map, wet band, shore band, check")]
        public static void AfterBake()
        {
            AssetDatabase.ImportAsset("Assets/OpenZ/Textures/WaterV4/T_WaterBedMap_L02.png", ImportAssetOptions.ForceUpdate);
            OpenZWaterV4.WetBand();
            var log = new StringBuilder("[OpenZ] River widen after bake\n").Append(OpenZShoreBand.FlattenBankNormals());
            AssetDatabase.SaveAssets(); EditorSceneManager.SaveOpenScenes();
            // the Fix and the wet band write their meshes with EditorUtility.CopySerialized, which does NOT re-upload the GPU
            // copy: until an editor restart the old water outline kept rendering (stepped 1 m water edges over the new banks)
            foreach (var p in new[] { "L02_WAT_Water_corners", "L02_ShoreWetBand", "L02_GND3_FloorPatch_seam", "L02_TER_Ground_carved", "L02_TER_RiverBed_carved", "L02_COL_Terrain_carved" })
            {
                var m = AssetDatabase.LoadAssetAtPath<Mesh>($"Assets/OpenZ/Models/Generated/{p}.asset");
                if (m && m.isReadable) { m.SetVertices(m.vertices); m.UploadMeshData(false); log.Append($"  GPU mesh re-uploaded: {p}\n"); }
            }
            var col = GameObject.Find("Environment")?.transform.Find("COL_Terrain")?.GetComponent<MeshCollider>();
            if (col) { var cm = col.sharedMesh; col.sharedMesh = null; col.sharedMesh = cm; }
            log.Append(OpenZShoreBand.CheckText()).Append(OpenZWaterCorners.CheckText());
            Debug.Log(log.ToString());
        }

        [MenuItem("OpenZ/River widen/Report (dry run)")]
        public static void Report() => Debug.Log(Run(false));

        [MenuItem("OpenZ/River widen/Rollback")]
        public static void Rollback()
        {
            var log = new StringBuilder("[OpenZ] River widen: rollback\n");
            string prj = Path.Combine(OpenZPaths.RepoRoot, "projects", "unity", "OpenZ");
            if (Directory.Exists(Backup))
                foreach (var f in Directory.GetFiles(Backup, "*.asset"))
                {
                    string p = "Assets/OpenZ/Models/Generated/" + Path.GetFileName(f);
                    File.Copy(f, Path.Combine(prj, p), true); AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate); log.Append($"  {p} restored\n");
                }
            var col = GameObject.Find("Environment")?.transform.Find("COL_Terrain")?.GetComponent<MeshCollider>();
            if (col) { var m = col.sharedMesh; col.sharedMesh = null; col.sharedMesh = m; }
            log.Append("  then: Water corners 1 Fix, the bed map bake, Water v4 3 + 4\n");
            Debug.Log(log.ToString());
        }

        struct R { public float x0, x1, z0, z1; public R(float a, float b, float c, float d) { x0 = a; x1 = b; z0 = c; z1 = d; } }
        static R Tile(int x, int y) => new R(x * 2f, x * 2f + 2f, -y * 2f - 2f, -y * 2f);
        static float Dist(List<R> rs, float x, float z)
        {
            float best = 1e9f;
            foreach (var r in rs)
            {
                float dx = Mathf.Max(0f, Mathf.Max(r.x0 - x, x - r.x1)), dz = Mathf.Max(0f, Mathf.Max(r.z0 - z, z - r.z1));
                float d = dx * dx + dz * dz; if (d < best) best = d;
            }
            return Mathf.Sqrt(best);
        }

        public static string Run(bool apply)
        {
            var log = new StringBuilder($"[OpenZ] River widen ({(apply ? "apply" : "dry run")})\n");
            if (EditorApplication.isPlayingOrWillChangePlaymode) return log.Append("  stop Play mode first").ToString();
            var scene = EditorSceneManager.GetActiveScene();
            if (!scene.name.StartsWith("L02")) return log.Append("  open L02_Desert first").ToString();
            var env = GameObject.Find("Environment");
            var wat = env ? env.transform.Find("WAT_Water") : null;
            if (!wat) return log.Append("  Environment/WAT_Water not found").ToString();
            float wy = wat.GetComponent<Renderer>().bounds.min.y, bedY = wy - BedDepth, topY = wy + TopAbove, maxY = wy + MaxAbove;

            // channel tiles + the no-go rects from the Zod grid
            var w = ZWorld.Create(Resources.Load<TextAsset>("OpenZ/default_settings").text, Resources.Load<TextAsset>("OpenZ/Maps/L02_grid").text, 1);
            bool Wet(int x, int y) { var c = w.Map.ClassAt(x, y); return c == ZClass.Water || c == ZClass.DeepWater; }
            var chan = new List<R>(); var chanTiles = new List<string>();
            foreach (var b in Boxes)
                for (int y = b[2]; y <= b[3]; y++) for (int x = b[0]; x <= b[1]; x++)
                    {
                        if (!Wet(x, y)) continue;
                        bool ew = !Wet(x - 1, y) && !Wet(x + 1, y), ns = !Wet(x, y - 1) && !Wet(x, y + 1);
                        if (ew || ns) { chan.Add(Tile(x, y)); chanTiles.Add($"{x},{y}"); }
                    }
            var chanSet = new HashSet<string>(chanTiles);
            foreach (var o in w.Objects.Where(q => q.IsBridge).ToList())
            {
                int bx0 = o.LocX / 16, by0 = o.LocY / 16, bx1 = (o.LocX + o.WidthPix) / 16, by1 = (o.LocY + o.HeightPix) / 16;
                if (o.Type == "bridge_vert")
                {
                    for (int y = by0; y < by1; y++) if (chanSet.Contains($"{bx0 - 1},{y}") || chanSet.Contains($"{bx1},{y}"))
                            for (int x = bx0; x < bx1; x++) { chan.Add(Tile(x, y)); chanTiles.Add($"{x},{y}(deck)"); }
                }
                else
                {
                    for (int x = bx0; x < bx1; x++) if (chanSet.Contains($"{x},{by0 - 1}") || chanSet.Contains($"{x},{by1}"))
                            for (int y = by0; y < by1; y++) { chan.Add(Tile(x, y)); chanTiles.Add($"{x},{y}(deck)"); }
                }
            }
            var forbid = new List<R>(); var soft = new List<R>(); int fTiles = 0, fObjs = 0;
            bool OnBridge(int x, int y) => w.Objects.Any(o => o.IsBridge && x >= o.LocX / 16 && x < (o.LocX + o.WidthPix) / 16 && y >= o.LocY / 16 && y < (o.LocY + o.HeightPix) / 16);
            foreach (var b in Boxes)
                for (int y = b[2] - 3; y <= b[3] + 3; y++) for (int x = b[0] - 3; x <= b[1] + 3; x++)
                    {
                        var c = w.Map.ClassAt(x, y);
                        if (c == ZClass.Road && !OnBridge(x, y)) { forbid.Add(Tile(x, y)); fTiles++; }   // Impassable here = flat rough ground; cliffs are kept by MaxAbove
                    }
            foreach (var o in w.Objects)
            {
                int tx = o.LocX / 16, ty = o.LocY / 16;
                if (o.IsBridge)
                {   // only the end tiles (ramps / abutments) stay no-go; the deck middle lets the river through
                    if (!Boxes.Any(b => tx >= b[0] - 6 && tx <= b[1] + 3 && ty >= b[2] - 6 && ty <= b[3] + 3)) continue;
                    float x0 = o.LocX / 8f, x1 = (o.LocX + o.WidthPix) / 8f, z0 = -(o.LocY + o.HeightPix) / 8f, z1 = -o.LocY / 8f;
                    if (o.Type == "bridge_vert") { soft.Add(new R(x0, x1, z1 - 1f, z1)); soft.Add(new R(x0, x1, z0, z0 + 1f)); }
                    else { soft.Add(new R(x0, x0 + 1f, z0, z1)); soft.Add(new R(x1 - 1f, x1, z0, z1)); }
                    fObjs++; continue;
                }
                if (!Boxes.Any(b => tx >= b[0] - 6 && tx <= b[1] + 3 && ty >= b[2] - 6 && ty <= b[3] + 3)) continue;
                forbid.Add(new R(o.LocX / 8f, (o.LocX + o.WidthPix) / 8f, -(o.LocY + o.HeightPix) / 8f, -o.LocY / 8f)); fObjs++;
            }
            log.Append($"  water y {wy:0.000}, bed {bedY:0.00}; {chan.Count} channel tiles [{string.Join(" ", chanTiles)}]; no-go: {fTiles} road tiles + {fObjs} objects\n");

            float Profile(float x, float z)
            {
                float d = Dist(chan, x, z); if (d > Flat + Bank + Wobble) return float.PositiveInfinity;
                // two octaves of noise move the bank line in and out (never into the channel tiles themselves)
                float n = (Mathf.PerlinNoise(x * 0.13f + 11.3f, z * 0.13f + 4.7f) - 0.5f) * 2f * 0.7f + (Mathf.PerlinNoise(x * 0.37f + 2.1f, z * 0.37f + 8.9f) - 0.5f) * 2f * 0.3f;
                d = Mathf.Max(0f, d - n * Wobble * Mathf.Clamp01(d / Flat));
                float k = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((d - Flat) / Bank));
                float t = bedY + (1f - k) * (topY - bedY);
                float a = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((Dist(forbid, x, z) - ForbidMargin) / ForbidRamp));
                if (soft.Count > 0) a = Mathf.Min(a, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((Dist(soft, x, z) - 0.3f) / 1.0f)));   // bridge ends: tight
                return t + (1f - a) * ForbidRise;
            }

            if (apply)
            {
                Directory.CreateDirectory(Backup);
                string prj = Path.Combine(OpenZPaths.RepoRoot, "projects", "unity", "OpenZ");
                foreach (var n in Meshes)
                {
                    var mesh = MeshOf(env, n); if (!mesh) continue;
                    string p = AssetDatabase.GetAssetPath(mesh);
                    string dst = Path.Combine(Backup, Path.GetFileName(p));
                    if (p.EndsWith(".asset") && !File.Exists(dst)) { File.Copy(Path.Combine(prj, p), dst); log.Append($"  backup {p} -> backups/river_widen\n"); }
                }
            }

            foreach (var n in Meshes)
            {
                var t = FindDeep(env.transform, n); var mesh = MeshOf(env, n);
                if (!t || !mesh) { log.Append($"  {n}: not found\n"); continue; }
                string path = AssetDatabase.GetAssetPath(mesh);
                if (!path.EndsWith(".asset") || !mesh.isReadable) { log.Append($"  {n}: {path} is not a writable mesh asset: skipped\n"); continue; }
                var v = mesh.vertices; var M = t.localToWorldMatrix; var inv = t.worldToLocalMatrix;
                int moved = 0; float maxDrop = 0f; var near = new bool[v.Length];
                for (int i = 0; i < v.Length; i++)
                {
                    var p = M.MultiplyPoint3x4(v[i]);
                    if (Dist(chan, p.x, p.z) <= Flat + Bank + Wobble + 1.0f) near[i] = true;
                    if (p.y > maxY) continue;
                    float prof = Profile(p.x, p.z);
                    if (prof >= p.y - 0.005f) continue;
                    maxDrop = Mathf.Max(maxDrop, p.y - prof); p.y = prof; v[i] = inv.MultiplyPoint3x4(p); moved++;
                }
                log.Append($"  {n} ({path}): {moved} vertices lowered (max {maxDrop:0.00} m)\n");
                if (!apply || moved == 0) continue;
                mesh.vertices = v;
                if (mesh.normals != null && mesh.normals.Length == v.Length && n != "COL_Terrain")
                {   // new normals only around the trench: the seam / bank normals elsewhere stay as the Fix left them
                    var tmp = Object.Instantiate(mesh); tmp.RecalculateNormals();
                    var nn = mesh.normals; var tn = tmp.normals; for (int i = 0; i < nn.Length; i++) if (near[i]) nn[i] = tn[i];
                    mesh.normals = nn; Object.DestroyImmediate(tmp);
                }
                else if (n != "COL_Terrain") mesh.RecalculateNormals();
                mesh.RecalculateBounds(); EditorUtility.SetDirty(mesh);
                var mc = t.GetComponent<MeshCollider>(); if (mc && mc.sharedMesh == mesh) { mc.sharedMesh = null; mc.sharedMesh = mesh; }
            }

            // props standing in the trench (would float over the new water)
            var skip = new HashSet<string>(Meshes) { "WAT_Water" };
            int props = 0;
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (skip.Contains(r.name) || r.name.StartsWith("WAT_") || r.name.StartsWith("CLF3_") || r.name.StartsWith("OUT3_") || r.name.StartsWith("WET_")) continue;
                var c = r.bounds.center; if (r.bounds.size.x > 30f || r.bounds.size.z > 30f) continue;
                float prof = Profile(c.x, c.z);
                if (prof < r.bounds.min.y - 0.1f && prof < wy + 0.2f && Dist(chan, c.x, c.z) < Flat + Bank) { props++; if (props <= 25) log.Append($"    prop in the trench: {PathOf(r.transform)} at ({c.x:0.0}, {c.z:0.0}) y {r.bounds.min.y:0.00} -> ground {prof:0.00}\n"); }
            }
            log.Append($"  props in the trench: {props}\n");

            if (apply)
            {
                EditorSceneManager.MarkSceneDirty(scene); AssetDatabase.SaveAssets(); EditorSceneManager.SaveOpenScenes();
                log.Append("  saved. Next: OpenZ > Water corners > 1 Fix, the bed map bake, Water v4 > 3 Wet sand band + 4 Shore band\n");
            }
            return log.ToString();
        }

        static Transform FindDeep(Transform root, string name)
        {
            var t = root.Find(name); if (t) return t;
            return root.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == name && x.gameObject.activeInHierarchy);
        }

        static Mesh MeshOf(GameObject env, string n)
        {
            var t = FindDeep(env.transform, n); if (!t) return null;
            var mf = t.GetComponent<MeshFilter>(); if (mf && mf.sharedMesh) return mf.sharedMesh;
            var mc = t.GetComponent<MeshCollider>(); return mc ? mc.sharedMesh : null;
        }

        static string PathOf(Transform t) { var s = t.name; while (t.parent) { t = t.parent; s = t.name + "/" + s; } return s; }
    }
}
