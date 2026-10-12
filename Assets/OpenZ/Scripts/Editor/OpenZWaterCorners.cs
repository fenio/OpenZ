using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Three causes, all fixed here (L02 only):
    /// 1. Cliffs v3 replaced the old TER_Cliffs cells (incl. river cells at channel corners that the level build had called
    ///    "steep") by GND3_FloorPatch, but the water v4 bed map was baked from TER_Ground + TER_RiverBed only: there the shader
    ///    saw land at +0.6 m, so it drew the raw floor patch (faceted, its own normals) with an object foam ring = the
    ///    triangular / square patch. Fixed by the new T_WaterBedMap_L02.png (baked from the whole rendered ground, copied by
    ///    apply.py) + seam normals: GND3_FloorPatch, TER_Ground and TER_RiverBed share one smooth normal on every common vertex.
    /// 2. WAT_Water only grew 4-connected by one 1 m cell from the Zod water tiles: diagonal corner cells and one narrow trench
    ///    (tile 43,37-38) lay below the water level with no surface (a dry notch / a broken stream). Rebuilt here: every cell
    ///    with ground below the water level that is 8-connected to the river gets water.
    /// 3. WAT_BridgeSpan_* quads lay on top of WAT_Water beside every bridge (double blended strips): the rebuilt WAT_Water
    ///    covers the carved river under the decks, the span objects are switched off (not deleted).
    /// Idempotent (always rebuilds from the FBX originals). Backups of the carved meshes in &lt;repo&gt;/backups/water_corners_v1.
    /// </summary>
    public static class OpenZWaterCorners
    {
        const string SceneName = "L02_Desert";
        const string Gen = "Assets/OpenZ/Models/Generated";
        const string WaterAsset = Gen + "/L02_WAT_Water_corners.asset";
        const string FloorAsset = Gen + "/L02_GND3_FloorPatch_seam.asset";
        const string EnvFbx = "Assets/OpenZ/Models/Environment/L02_environment.fbx";
        const string CliffFbx = "Assets/OpenZ/Models/Environment/CliffsV3/L02_cliffs_v3.fbx";
        const string BedMapTex = "Assets/OpenZ/Textures/WaterV4/T_WaterBedMap_L02.png";
        const string WaterMeshName = "L02_WAT_Water_corners";
        const float X0 = -24f, Z0 = -204f;            // = the bed map rect (-24, -204, 192, 228)
        const int NX = 192, NZ = 228;                  // 1 m cells
        const int Sub = 4;                             // 0.25 m node grid inside each cell
        const int Grow = 10;                           // growth steps (m) from the old surface through below-water cells
        static readonly Vector4 BedEnc = new Vector4(1.2f, -2f, 6f, -1.6f);   // extra, sd lo, sd range, y lo (bedmap_L02.json)
        const float BedEncY = 2.2f;

        // the spots that showed the patch (world x, z, ground height there): the Check samples the bed map at each
        static readonly (float x, float z, float y, string tile)[] PatchSpots =
        {
            (48.938f, -173.938f, -0.87f, "24,86"), (74.062f, -171.938f, -0.88f, "37,85"), (61.938f, -170.938f, -1.04f, "30,85"),
            (61.062f, -169.938f, -0.71f, "30,84"), (70.062f, -131.062f, -1.09f, "35,65"), (135.938f, -122.062f, -0.88f, "67,61"),
            (137.938f, -120.062f, -1.09f, "68,60"), (137.938f, -117.938f, -1.10f, "68,58"), (69.938f, -102.062f, -1.06f, "34,51"),
            (10.062f, -58.938f, -1.06f, "5,29"), (44.938f, -58.938f, -1.10f, "22,29"), (81.062f, -15.062f, -1.02f, "40,7"),
            (70.062f, -12.938f, -0.74f, "35,6"),
        };

        static string Backup => Path.Combine(OpenZPaths.RepoRoot, "backups", "water_corners_v1");

        // ------------------------------------------------------------------ menu
        [MenuItem("OpenZ/Water corners/1 Fix L02 river corners (open L02)")]
        public static void Fix() => Debug.Log(FixText());

        [MenuItem("OpenZ/Water corners/2 Check")]
        public static void Check() => Debug.Log(CheckText());

        [MenuItem("OpenZ/Water corners/Rollback")]
        public static void Rollback() => Debug.Log(RollbackText());

        public static string FixText()
        {
            var log = new StringBuilder("[OpenZ] Water corners v1: fix\n");
            if (!Ready(log, out var env, out var water)) return log.ToString();
            var bedOk = BedMapInstalled(out string bedMsg);
            log.Append($"  {(bedOk ? "OK  " : "WARN")} {bedMsg}\n");

            // backups of the carved meshes (once) - the seam normals change them in place
            Directory.CreateDirectory(Backup);
            foreach (var n in new[] { "TER_Ground", "TER_RiverBed" })
            {
                var mf = Find(env, n); if (!mf || !mf.sharedMesh) continue;
                string p = AssetDatabase.GetAssetPath(mf.sharedMesh);
                if (p.EndsWith(".asset"))
                {
                    string dst = Path.Combine(Backup, Path.GetFileName(p));
                    if (!File.Exists(dst)) { File.Copy(Path.Combine(OpenZPaths.RepoRoot, "projects", "unity", "OpenZ", p), dst); log.Append($"  backup {p} -> backups/water_corners_v1\n"); }
                }
            }

            // 1. the water surface
            var ground = GroundFilters(env);
            var built = BuildWater(water, ground, out int oldCells, out int newCells, out float wy);
            if (built == null) { log.Append("  FAIL: WAT_Water source mesh not found in L02_environment.fbx (or not readable)\n"); return log.ToString(); }
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(WaterAsset);
            if (existing) { EditorUtility.CopySerialized(built, existing); existing.name = WaterMeshName; Object.DestroyImmediate(built); built = existing; EditorUtility.SetDirty(existing); }
            else { Directory.CreateDirectory(Gen); AssetDatabase.CreateAsset(built, WaterAsset); }
            Undo.RecordObject(water, "Water corners");
            water.sharedMesh = built;
            var wmc = water.GetComponent<MeshCollider>(); if (wmc) wmc.sharedMesh = built;
            log.Append($"  WAT_Water rebuilt: {oldCells} -> {newCells} cells of 1 m at y {wy:0.000} ({built.vertexCount} vertices) -> {WaterAsset}\n");

            // 2. bridge span quads off (WAT_Water covers the carved river under the decks now)
            int off = 0;
            foreach (Transform c in env.transform)
                if (c.name.StartsWith("WAT_BridgeSpan") && c.gameObject.activeSelf) { Undo.RecordObject(c.gameObject, "Water corners"); c.gameObject.SetActive(false); off++; }
            log.Append($"  WAT_BridgeSpan_*: {off} switched off (no double water beside the decks)\n");

            // 3. seam normals: TER_Ground + TER_RiverBed + GND3_FloorPatch
            ground = GroundFilters(env);
            var floor = ground.FirstOrDefault(f => f.name == "GND3_FloorPatch");
            if (floor)
            {
                var src = FbxMesh(CliffFbx, "GND3_FloorPatch") ?? floor.sharedMesh;
                var fm = AssetDatabase.LoadAssetAtPath<Mesh>(FloorAsset);
                var copy = Object.Instantiate(src); copy.name = "L02_GND3_FloorPatch_seam";
                if (fm) { EditorUtility.CopySerialized(copy, fm); fm.name = copy.name; Object.DestroyImmediate(copy); }
                else { AssetDatabase.CreateAsset(copy, FloorAsset); fm = copy; }
                Undo.RecordObject(floor, "Water corners");
                floor.sharedMesh = fm;
            }
            else log.Append("  WARN: GND3_FloorPatch not found under Environment (Cliffs v3 placed?)\n");
            foreach (var f in ground)
            {
                string p = AssetDatabase.GetAssetPath(f.sharedMesh);
                if (!p.EndsWith(".asset") || !f.sharedMesh.isReadable) { log.Append($"  WARN: {f.name} mesh {p} is not a writable mesh asset: skipped for the seam normals\n"); }
            }
            var writable = ground.Where(f => AssetDatabase.GetAssetPath(f.sharedMesh).EndsWith(".asset") && f.sharedMesh.isReadable).ToList();
            int seam = SeamNormals(writable, out float maxBefore);
            foreach (var f in writable) EditorUtility.SetDirty(f.sharedMesh);
            log.Append($"  seam normals: {seam} vertices on {string.Join(" + ", writable.Select(f => f.name))} (max crease across meshes before: {maxBefore:0.0} deg)\n");
            // shore_band_fix: the seam normals re-tilt the river banks toward the water (the dark bank side); turn their shading up again
            log.Append(OpenZShoreBand.FlattenBankNormals());

            EditorSceneManager.MarkSceneDirty(water.gameObject.scene);
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveOpenScenes();

            // 4. the wet sand band proxy follows the (new) bed map
            try { OpenZWaterV4.WetBand(); log.Append("  wet sand band rebuilt from the bed map (Water v4 menu 3)\n"); }
            catch (System.Exception e) { log.Append($"  WARN: wet band rebuild failed ({e.Message}): run OpenZ > Water v4 > 3 Wet sand band\n"); }
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveOpenScenes();
            log.Append(CheckText());
            return log.ToString();
        }

        public static string CheckText()
        {
            var sb = new StringBuilder("[OpenZ] Water corners v1: check\n"); int bad = 0;
            void Line(bool ok, string s) { sb.Append(ok ? "  OK   " : "  FAIL ").Append(s).Append('\n'); if (!ok) bad++; }
            if (!Ready(sb, out var env, out var water)) return sb.ToString();
            Line(water.sharedMesh && water.sharedMesh.name == WaterMeshName, $"WAT_Water mesh = {(water.sharedMesh ? water.sharedMesh.name : "none")} (expect {WaterMeshName})");
            var spans = env.transform.Cast<Transform>().Where(c => c.name.StartsWith("WAT_BridgeSpan")).ToList();
            Line(spans.All(c => !c.gameObject.activeSelf), $"WAT_BridgeSpan_*: {spans.Count(c => c.gameObject.activeSelf)} of {spans.Count} active (expect 0)");
            var ground = GroundFilters(env);
            var floor = ground.FirstOrDefault(f => f.name == "GND3_FloorPatch");
            Line(floor && floor.sharedMesh && floor.sharedMesh.name == "L02_GND3_FloorPatch_seam", $"GND3_FloorPatch mesh = {(floor && floor.sharedMesh ? floor.sharedMesh.name : "missing")}");

            // no ground below the water level without a surface (8-connected to the river)
            float wy = water.GetComponent<Renderer>().bounds.min.y;
            var node = NodeHeights(ground, wy + 1.2f);
            var wet = WetCells(node, wy);
            var cov = Coverage(water.sharedMesh, water.transform);
            int exposed = 0;
            for (int j = 0; j < NZ; j++)
                for (int i = 0; i < NX; i++)
                {
                    if (!wet[j, i] || cov[j, i]) continue;
                    bool nb = false;
                    for (int dj = -1; dj <= 1 && !nb; dj++) for (int di = -1; di <= 1 && !nb; di++) { int a = j + dj, b = i + di; if (a >= 0 && b >= 0 && a < NZ && b < NX && cov[a, b]) nb = true; }
                    if (nb) exposed++;
                }
            Line(exposed == 0, $"ground below the water level next to the river without a water surface: {exposed} cells (before the fix: 20 corner cells + the trench at tile 43,37-38)");

            // the bed map at every spot that showed the patch: water side, height = the rendered ground
            if (!File.Exists(BedMapTex)) Line(false, $"{BedMapTex} missing");
            else
            {
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
                tex.LoadImage(File.ReadAllBytes(BedMapTex));
                int ok = 0; var fails = new List<string>();
                foreach (var s in PatchSpots)
                {
                    var c = tex.GetPixelBilinear((s.x - X0) / NX, (s.z - Z0) / NZ);
                    float sd = BedEnc.y + c.g * BedEnc.z, y = BedEnc.w + c.b * BedEncY;
                    float g = NodeAt(node, s.x, s.z); if (float.IsNegativeInfinity(g)) g = s.y;
                    if (sd > 0f && Mathf.Abs(y - g) < 0.135f) ok++; else fails.Add($"tile {s.tile}: sd {sd:0.00} m, bed y {y:0.00} vs ground {g:0.00}");
                }
                Line(ok == PatchSpots.Length, $"bed map at the {PatchSpots.Length} former patch spots: {ok} read as water with the right ground height" + (fails.Count > 0 ? " | " + string.Join("; ", fails) : ""));
                Object.DestroyImmediate(tex);
            }

            // seam normals
            var readable = ground.Where(f => f.sharedMesh && f.sharedMesh.isReadable).ToList();
            float crease = MaxSeamCrease(readable);
            Line(crease < 2f, $"normals agree across TER_Ground / TER_RiverBed / GND3_FloorPatch: max {crease:0.00} deg on shared vertices (expect < 2)");
            var wb = GameObject.Find("L02_ShoreWetBand");
            Line(wb && wb.activeInHierarchy, "L02_ShoreWetBand present (rebuild: OpenZ > Water v4 > 3 Wet sand band)");
            sb.Append(bad == 0 ? "  ALL OK" : $"  {bad} problem(s)");
            return sb.ToString();
        }

        public static string RollbackText()
        {
            var log = new StringBuilder("[OpenZ] Water corners v1: rollback\n");
            if (!Ready(log, out var env, out var water)) return log.ToString();
            var src = FbxMesh(EnvFbx, "WAT_Water");
            if (src) { Undo.RecordObject(water, "Water corners rollback"); water.sharedMesh = src; log.Append("  WAT_Water back on the FBX mesh\n"); }
            foreach (Transform c in env.transform) if (c.name.StartsWith("WAT_BridgeSpan")) c.gameObject.SetActive(true);
            var floor = GroundFilters(env).FirstOrDefault(f => f.name == "GND3_FloorPatch");
            var fsrc = FbxMesh(CliffFbx, "GND3_FloorPatch");
            if (floor && fsrc) { floor.sharedMesh = fsrc; log.Append("  GND3_FloorPatch back on the FBX mesh\n"); }
            string prj = Path.Combine(OpenZPaths.RepoRoot, "projects", "unity", "OpenZ");
            foreach (var n in new[] { "L02_TER_Ground_carved.asset", "L02_TER_RiverBed_carved.asset" })
            {
                string b = Path.Combine(Backup, n), p = $"{Gen}/{n}";
                if (File.Exists(b)) { File.Copy(b, Path.Combine(prj, p), true); AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate); log.Append($"  {p} restored (normals)\n"); }
            }
            string bb = Path.Combine(Backup, "T_WaterBedMap_L02.png");
            if (File.Exists(bb)) { File.Copy(bb, Path.Combine(prj, BedMapTex), true); AssetDatabase.ImportAsset(BedMapTex, ImportAssetOptions.ForceUpdate); log.Append("  bed map restored from the backup\n"); }
            else log.Append("  no bed map backup (apply.py makes it): the new bed map stays\n");
            log.Append(OpenZShoreBand.FlattenBankNormals());                     // shore_band_fix: keep the flat bank shading
            EditorSceneManager.MarkSceneDirty(water.gameObject.scene);
            AssetDatabase.SaveAssets(); EditorSceneManager.SaveOpenScenes();
            try { OpenZWaterV4.WetBand(); } catch (System.Exception e) { log.Append($"  WARN: wet band: {e.Message}\n"); }
            EditorSceneManager.SaveOpenScenes();
            return log.ToString();
        }

        // ------------------------------------------------------------------ scene access
        static bool Ready(StringBuilder log, out GameObject env, out MeshFilter water)
        {
            env = null; water = null;
            if (EditorApplication.isPlayingOrWillChangePlaymode) { log.Append("  stop Play mode first\n"); return false; }
            if (EditorSceneManager.GetActiveScene().name != SceneName) { log.Append($"  open {SceneName} first\n"); return false; }
            env = GameObject.Find("Environment");
            var w = env ? env.transform.Find("WAT_Water") : null;
            water = w ? w.GetComponent<MeshFilter>() : null;
            if (!water) { log.Append("  Environment/WAT_Water not found\n"); return false; }
            return true;
        }

        static MeshFilter Find(GameObject env, string name)
        {
            var t = env.transform.Find(name); return t ? t.GetComponent<MeshFilter>() : null;
        }

        /// <summary>The rendered ground around the river: TER_Ground, TER_RiverBed (carved) and the Cliffs v3 floor patch.</summary>
        static List<MeshFilter> GroundFilters(GameObject env)
        {
            var l = new List<MeshFilter>();
            foreach (var n in new[] { "TER_Ground", "TER_RiverBed" }) { var f = Find(env, n); if (f && f.sharedMesh && f.gameObject.activeInHierarchy) l.Add(f); }
            var fp = env.GetComponentsInChildren<MeshFilter>(true).FirstOrDefault(f => f.name == "GND3_FloorPatch" && f.gameObject.activeInHierarchy && f.sharedMesh);
            if (fp) l.Add(fp);
            return l;
        }

        static Mesh FbxMesh(string fbx, string name) => AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<Mesh>().FirstOrDefault(m => m.name == name);

        static bool BedMapInstalled(out string msg)
        {
            // the (34,50) channel corner: the old map read land here (sd -0.32 m, bed y +0.60 m); the new one water (sd +0.42, y -0.88)
            if (!File.Exists(BedMapTex)) { msg = $"{BedMapTex} missing"; return false; }
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            tex.LoadImage(File.ReadAllBytes(BedMapTex));
            var c = tex.GetPixelBilinear((69.5f - X0) / NX, (-101.5f - Z0) / NZ);
            Object.DestroyImmediate(tex);
            float sd = BedEnc.y + c.g * BedEnc.z, y = BedEnc.w + c.b * BedEncY;
            bool ok = sd > 0f && y < -0.5f;
            msg = ok ? $"new bed map installed (corner 34,50: sd {sd:0.00} m, bed y {y:0.00} m)"
                     : $"the bed map is still the OLD one (corner 34,50: sd {sd:0.00} m, bed y {y:0.00} m): run work/staging/water_corners_v1/apply.py, then this menu again";
            return ok;
        }

        // ------------------------------------------------------------------ water surface
        struct Tri { public Vector3 a, b, c; }

        static List<Tri> WorldTris(Mesh m, Transform t, float yMax)
        {
            var l = new List<Tri>();
            var v = m.vertices; var ix = m.triangles;
            var w = new Vector3[v.Length];
            for (int i = 0; i < v.Length; i++) w[i] = t.TransformPoint(v[i]);
            for (int k = 0; k < ix.Length; k += 3)
            {
                var a = w[ix[k]]; var b = w[ix[k + 1]]; var c = w[ix[k + 2]];
                if (Mathf.Min(a.y, Mathf.Min(b.y, c.y)) >= yMax) continue;
                l.Add(new Tri { a = a, b = b, c = c });
            }
            return l;
        }

        /// <summary>Topmost ground height at every 0.25 m node (x = X0 + i / 4, z = Z0 + j / 4); -inf where no ground.</summary>
        static float[,] NodeHeights(List<MeshFilter> ground, float yMax)
        {
            int nx = NX * Sub + 1, nz = NZ * Sub + 1; float step = 1f / Sub;
            var h = new float[nz, nx];
            for (int j = 0; j < nz; j++) for (int i = 0; i < nx; i++) h[j, i] = float.NegativeInfinity;
            foreach (var f in ground)
            {
                if (!f.sharedMesh.isReadable) continue;
                foreach (var t in WorldTris(f.sharedMesh, f.transform, yMax))
                {
                    Vector3 a = t.a, b = t.b, c = t.c;
                    float d = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
                    if (Mathf.Abs(d) < 1e-9f) continue;
                    int i0 = Mathf.Max(0, Mathf.CeilToInt((Mathf.Min(a.x, Mathf.Min(b.x, c.x)) - X0) / step - 1e-4f));
                    int i1 = Mathf.Min(nx - 1, Mathf.FloorToInt((Mathf.Max(a.x, Mathf.Max(b.x, c.x)) - X0) / step + 1e-4f));
                    int j0 = Mathf.Max(0, Mathf.CeilToInt((Mathf.Min(a.z, Mathf.Min(b.z, c.z)) - Z0) / step - 1e-4f));
                    int j1 = Mathf.Min(nz - 1, Mathf.FloorToInt((Mathf.Max(a.z, Mathf.Max(b.z, c.z)) - Z0) / step + 1e-4f));
                    for (int j = j0; j <= j1; j++)
                    {
                        float z = Z0 + j * step;
                        for (int i = i0; i <= i1; i++)
                        {
                            float x = X0 + i * step;
                            float w0 = ((b.z - c.z) * (x - c.x) + (c.x - b.x) * (z - c.z)) / d;
                            float w1 = ((c.z - a.z) * (x - c.x) + (a.x - c.x) * (z - c.z)) / d;
                            float w2 = 1f - w0 - w1;
                            if (w0 < -1e-5f || w1 < -1e-5f || w2 < -1e-5f) continue;
                            float y = w0 * a.y + w1 * b.y + w2 * c.y;
                            if (y > h[j, i]) h[j, i] = y;
                        }
                    }
                }
            }
            return h;
        }

        static float NodeAt(float[,] h, float x, float z)
        {
            int i = Mathf.Clamp(Mathf.RoundToInt((x - X0) * Sub), 0, h.GetLength(1) - 1), j = Mathf.Clamp(Mathf.RoundToInt((z - Z0) * Sub), 0, h.GetLength(0) - 1);
            return h[j, i];
        }

        /// <summary>A 1 m cell is wet when any of its 5 x 5 nodes (edges included) has ground below the water level.</summary>
        static bool[,] WetCells(float[,] h, float wy)
        {
            var wet = new bool[NZ, NX];
            for (int cj = 0; cj < NZ; cj++)
                for (int ci = 0; ci < NX; ci++)
                {
                    bool w = false;
                    for (int a = 0; a <= Sub && !w; a++)
                        for (int b = 0; b <= Sub && !w; b++)
                        {
                            float y = h[cj * Sub + a, ci * Sub + b];
                            if (!float.IsNegativeInfinity(y) && y < wy) w = true;
                        }
                    wet[cj, ci] = w;
                }
            return wet;
        }

        /// <summary>Cells whose centre lies on a triangle of the mesh (seen from above).</summary>
        static bool[,] Coverage(Mesh m, Transform t)
        {
            var cov = new bool[NZ, NX];
            if (!m || !m.isReadable) return cov;
            foreach (var tr in WorldTris(m, t, float.PositiveInfinity))
            {
                Vector3 a = tr.a, b = tr.b, c = tr.c;
                float d = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
                if (Mathf.Abs(d) < 1e-9f) continue;
                int i0 = Mathf.Max(0, Mathf.CeilToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x)) - X0 - 0.5f - 1e-4f));
                int i1 = Mathf.Min(NX - 1, Mathf.FloorToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x)) - X0 - 0.5f + 1e-4f));
                int j0 = Mathf.Max(0, Mathf.CeilToInt(Mathf.Min(a.z, Mathf.Min(b.z, c.z)) - Z0 - 0.5f - 1e-4f));
                int j1 = Mathf.Min(NZ - 1, Mathf.FloorToInt(Mathf.Max(a.z, Mathf.Max(b.z, c.z)) - Z0 - 0.5f + 1e-4f));
                for (int j = j0; j <= j1; j++)
                    for (int i = i0; i <= i1; i++)
                    {
                        float x = X0 + i + 0.5f, z = Z0 + j + 0.5f;
                        float w0 = ((b.z - c.z) * (x - c.x) + (c.x - b.x) * (z - c.z)) / d;
                        float w1 = ((c.z - a.z) * (x - c.x) + (a.x - c.x) * (z - c.z)) / d;
                        if (w0 >= -1e-5f && w1 >= -1e-5f && 1f - w0 - w1 >= -1e-5f) cov[j, i] = true;
                    }
            }
            return cov;
        }

        static Mesh BuildWater(MeshFilter water, List<MeshFilter> ground, out int oldCells, out int newCells, out float wy)
        {
            oldCells = newCells = 0; wy = -0.35f;
            var src = FbxMesh(EnvFbx, "WAT_Water");
            if (!src || !src.isReadable) return null;
            var tr = water.transform;
            wy = src.vertices.Select(v => tr.TransformPoint(v).y).Min();
            var cells = Coverage(src, tr);
            for (int j = 0; j < NZ; j++) for (int i = 0; i < NX; i++) if (cells[j, i]) oldCells++;
            var wet = WetCells(NodeHeights(ground, wy + 1.2f), wy);
            for (int step = 0; step < Grow; step++)
            {
                var add = new List<(int, int)>();
                for (int j = 0; j < NZ; j++)
                    for (int i = 0; i < NX; i++)
                    {
                        if (cells[j, i] || !wet[j, i]) continue;
                        bool nb = false;
                        for (int dj = -1; dj <= 1 && !nb; dj++) for (int di = -1; di <= 1 && !nb; di++) { int a = j + dj, b = i + di; if (a >= 0 && b >= 0 && a < NZ && b < NX && cells[a, b]) nb = true; }
                        if (nb) add.Add((j, i));
                    }
                if (add.Count == 0) break;
                foreach (var (j, i) in add) cells[j, i] = true;
            }
            // quads at the water level, shared vertices, planar UV like the Blender build (u = (x + 24) / 192, v = (z + 204) / 228)
            var vIndex = new Dictionary<int, int>(); var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
            float wyl = wy;
            int V(int i, int j)
            {
                int key = j * (NX + 1) + i;
                if (vIndex.TryGetValue(key, out int id)) return id;
                float x = X0 + i, z = Z0 + j;
                verts.Add(tr.InverseTransformPoint(new Vector3(x, wyl, z))); uvs.Add(new Vector2((x - X0) / NX, (z - Z0) / NZ));
                vIndex[key] = verts.Count - 1; return verts.Count - 1;
            }
            for (int j = 0; j < NZ; j++)
                for (int i = 0; i < NX; i++)
                {
                    if (!cells[j, i]) continue;
                    newCells++;
                    int a = V(i, j), b = V(i + 1, j), c = V(i + 1, j + 1), d = V(i, j + 1);
                    tris.Add(a); tris.Add(c); tris.Add(b); tris.Add(a); tris.Add(d); tris.Add(c);      // facing up (as the wet band)
                }
            // a mirrored transform would flip the winding: keep the faces pointing up in world space
            if (tris.Count >= 3)
            {
                Vector3 p0 = tr.TransformPoint(verts[tris[0]]), p1 = tr.TransformPoint(verts[tris[1]]), p2 = tr.TransformPoint(verts[tris[2]]);
                if (Vector3.Cross(p1 - p0, p2 - p0).y < 0) for (int k = 0; k < tris.Count; k += 3) { int s = tris[k + 1]; tris[k + 1] = tris[k + 2]; tris[k + 2] = s; }
            }
            var mesh = new Mesh { name = WaterMeshName, indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(verts); mesh.SetUVs(0, uvs); mesh.SetTriangles(tris, 0);
            var up = tr.InverseTransformDirection(Vector3.up).normalized;
            mesh.SetNormals(Enumerable.Repeat(up, verts.Count).ToList());
            mesh.RecalculateTangents(); mesh.RecalculateBounds();
            return mesh;
        }

        // ------------------------------------------------------------------ seam normals
        static long Key(Vector3 w) => ((long)Mathf.RoundToInt(w.x * 100f) + 1000000L) * 10000000L + (Mathf.RoundToInt(w.z * 100f) + 1000000L);

        /// <summary>One area-weighted normal per ground position over all three meshes; written to every vertex of the floor
        /// patch and to every vertex the meshes share (the rest keeps its normal).</summary>
        static int SeamNormals(List<MeshFilter> mfs, out float maxBefore)
        {
            maxBefore = MaxSeamCrease(mfs);
            var acc = new Dictionary<long, Vector3>(); var owners = new Dictionary<long, int>();
            for (int k = 0; k < mfs.Count; k++)
            {
                var m = mfs[k].sharedMesh; var tr = mfs[k].transform;
                var v = m.vertices; var ix = m.triangles;
                var w = new Vector3[v.Length]; for (int i = 0; i < v.Length; i++) w[i] = tr.TransformPoint(v[i]);
                for (int t = 0; t < ix.Length; t += 3)
                {
                    var n = Vector3.Cross(w[ix[t + 1]] - w[ix[t]], w[ix[t + 2]] - w[ix[t]]);
                    for (int q = 0; q < 3; q++)
                    {
                        long key = Key(w[ix[t + q]]);
                        acc[key] = acc.TryGetValue(key, out var s) ? s + n : n;
                        owners[key] = (owners.TryGetValue(key, out int o) ? o : 0) | (1 << k);
                    }
                }
            }
            int changed = 0;
            for (int k = 0; k < mfs.Count; k++)
            {
                var m = mfs[k].sharedMesh; var tr = mfs[k].transform; bool isFloor = mfs[k].name == "GND3_FloorPatch";
                var v = m.vertices; var nrm = m.normals;
                if (nrm == null || nrm.Length != v.Length) { m.RecalculateNormals(); nrm = m.normals; }
                for (int i = 0; i < v.Length; i++)
                {
                    long key = Key(tr.TransformPoint(v[i]));
                    if (!acc.TryGetValue(key, out var n) || n.sqrMagnitude < 1e-12f) continue;
                    int o = owners[key];
                    bool shared = (o & (o - 1)) != 0;
                    if (!isFloor && !shared) continue;
                    if (n.y < 0) n = -n;
                    nrm[i] = tr.InverseTransformDirection(n.normalized).normalized; changed++;
                }
                m.normals = nrm;
            }
            return changed;
        }

        /// <summary>Largest angle between the normals of vertices at the same ground position in different meshes (deg).</summary>
        static float MaxSeamCrease(List<MeshFilter> mfs)
        {
            var first = new Dictionary<long, (int mesh, Vector3 n)>(); float max = 0f;
            for (int k = 0; k < mfs.Count; k++)
            {
                var m = mfs[k].sharedMesh; if (!m || !m.isReadable) continue;
                var tr = mfs[k].transform; var v = m.vertices; var nrm = m.normals;
                if (nrm == null || nrm.Length != v.Length) continue;
                for (int i = 0; i < v.Length; i++)
                {
                    long key = Key(tr.TransformPoint(v[i])); var n = tr.TransformDirection(nrm[i]).normalized;
                    if (first.TryGetValue(key, out var f)) { if (f.mesh != k) max = Mathf.Max(max, Vector3.Angle(f.n, n)); }
                    else first[key] = (k, n);
                }
            }
            return max;
        }
    }
}
