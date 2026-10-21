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
    /// Three Level 16 map fixes in the OPEN level 16 scene (the scene is never rebuilt). Run after the kit's data script made the
    /// eighteen tiles west of the gatling island shallow water (Resources/OpenZ/Maps/L16_grid.json) and repainted the ground pictures.
    /// - Bug 3, the gatling island reads as an island: the ground (TER_Ground, TER_RiverBed) and the walk collider (COL_Terrain) of the
    ///   new channel take the shape of the channel north of the island (copied across at the same distance from the water's edge,
    ///   only ever lowered), each as a new mesh asset; the water surface (WAT_Water) gets a flat sheet over the new channel at the
    ///   level's water height, the old sheet cut out exactly under it.
    /// - Bug 5, the dark rock blocks: the rock-tile pillars' materials M_L16_Rock, M_L16_RockMoss and M_L16_RockFresh take the tint of
    ///   every rock beside them (white instead of 0.5, 0.53, 0.5).
    /// - Bug 6, ruins in the ridge west of the middle: RUIN_42_ruin_gateway and RUIN_46_ruin_column_broken are pulled out of the
    ///   ridge's south face onto the flat ledge at its foot (measured with the walk collider) and seated on it; the three other
    ///   ruins of that ridge are measured and reported only.
    /// Menu 2 first writes an undo copy (the saved scene file and the three materials) to backups/l16_map_fixes; menu 3 puts it back.
    /// Menu 1 reports what menu 2 would do and changes nothing. Running menu 2 again changes nothing.
    /// </summary>
    public static class OzL16MapFixes
    {
        const string Menu = "OpenZ/L16 Jungle/Map fixes K356/";
        const string Gen = "Assets/OpenZ/Models/Generated";
        const string RockDir = "Assets/OpenZ/Models/L16/Rocks";
        const string Tag = "_k356";
        static string BackupRoot => Path.Combine(OpenZPaths.RepoRoot, "backups", "l16_map_fixes");
        static readonly string[] GroundMeshes = { "TER_Ground", "TER_RiverBed", "COL_Terrain" };
        static readonly string[] RockMats = { "M_L16_Rock", "M_L16_RockMoss", "M_L16_RockFresh" };
        static readonly string[] RuinsMove = { "RUIN_42_ruin_gateway", "RUIN_46_ruin_column_broken" };
        static readonly string[] RuinsMeasure = { "RUIN_43_ruin_column_standing", "RUIN_44_ruin_column_broken", "RUIN_45_ruin_column_standing" };
        static readonly int[] NewWater = { 23, 60, 24, 60, 25, 60, 23, 61, 24, 61, 25, 61, 23, 62, 24, 62, 25, 62, 23, 63, 24, 63, 23, 64, 24, 64, 23, 65, 24, 65, 23, 66, 24, 66, 23, 67 };
        // the area the channel copy may touch, the copy's source (the north channel west of the pond) and its fade (as the data script)
        const float BX0 = 42f, BX1 = 56f, BZ0 = -140f, BZ1 = -116f, SrcX0 = 56f, SrcLen = 8f, SrcEdgeZ = -120f, Full = 2f, Fade = 3.5f;
        // the water sheet: the old sheet is cut out of this box and the box is covered at the water height (its sides lie on dry
        // banks or across open water, where the two sheets meet at the same height)
        const float WX0 = 44.5f, WX1 = 53f, WZ0 = -137f, WZ1 = -119f, Cell = 0.5f;
        const float RuinClear = 0.35f, RuinMargin = 0.1f, RuinSink = 0.06f, RuinMaxPull = 3.5f;

        [Serializable] sealed class GridDoc { public int width, height; public int[] classes; }

        [MenuItem(Menu + "1 Report (dry run)")]
        public static void Report() => Debug.Log(Run(false));

        [MenuItem(Menu + "2 Apply (undo copy first)")]
        public static void Apply() => Debug.Log(Run(true));

        [MenuItem(Menu + "3 Undo (put the undo copy back)")]
        public static void UndoAll() => Debug.Log(Restore());

        // ------------------------------------------------------------------------------------------------ the run
        public static string Run(bool apply)
        {
            var log = new StringBuilder($"[OpenZ L16 map fixes] {(apply ? "apply" : "report (dry run)")}\n");
            if (EditorApplication.isPlayingOrWillChangePlaymode) return log.Append("  stop Play mode first").ToString();
            var scene = EditorSceneManager.GetActiveScene();
            if (!scene.name.StartsWith("L16")) return log.Append("  open L16_Jungle first").ToString();
            if (apply && scene.isDirty) return log.Append("  save the scene first (the undo copy is the saved scene file)").ToString();
            var env = GameObject.Find("Environment");
            Transform T(string n) => env ? env.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == n) : null;
            var colT = T("COL_Terrain"); var watT = T("WAT_Water");
            var col = colT ? colT.GetComponent<MeshCollider>() : null;
            if (!env || !col || !watT || !T("TER_Ground") || !T("TER_RiverBed")) return log.Append("  Environment with TER_Ground, TER_RiverBed, COL_Terrain (collider) and WAT_Water not found").ToString();
            var ta = Resources.Load<TextAsset>("OpenZ/Maps/L16_grid");
            var g = ta ? JsonUtility.FromJson<GridDoc>(ta.text) : null;
            if (g == null || g.classes == null) return log.Append("  level data L16_grid missing").ToString();
            int notWet = 0; for (int i = 0; i < NewWater.Length; i += 2) if (g.classes[NewWater[i + 1] * g.width + NewWater[i]] != 2) notWet++;
            if (notWet > 0) return log.Append($"  {notWet} of the 18 channel tiles are not shallow water yet: run the kit's data script (apply_k356.py --apply) first, let the editor import, then this menu").ToString();

            string undoDir = null;
            if (apply)
            {
                undoDir = Path.Combine(BackupRoot, DateTime.Now.ToString("yyyyMMdd_HHmmss"));
                Directory.CreateDirectory(undoDir);
                string prj = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                File.Copy(Path.Combine(prj, scene.path), Path.Combine(undoDir, Path.GetFileName(scene.path)), true);
                foreach (var n in RockMats) { string p = $"{RockDir}/{n}.mat"; if (File.Exists(Path.Combine(prj, p))) File.Copy(Path.Combine(prj, p), Path.Combine(undoDir, n + ".mat"), true); }
                File.WriteAllText(Path.Combine(undoDir, "scene_path.txt"), scene.path);
                log.Append($"  undo copy: {undoDir} (scene file + {RockMats.Length} materials)\n");
            }
            log.Append(Channel(apply, env, T, col, watT, g));
            log.Append(Rocks(apply));
            log.Append(RuinsFix(apply, col, g));
            if (apply)
            {
                EditorSceneManager.MarkSceneDirty(scene); AssetDatabase.SaveAssets(); EditorSceneManager.SaveScene(scene);
                log.Append("  saved. Next: the Play checks of the kit's SPEC part 0.\n");
            }
            return log.ToString();
        }

        // ------------------------------------------------------------------------------------------------ bug 3: the channel
        struct Rc { public float x0, x1, z0, z1; }
        static Rc TileRc(int tx, int ty) => new Rc { x0 = tx * 2f, x1 = tx * 2f + 2f, z0 = -ty * 2f - 2f, z1 = -ty * 2f };
        static float RcDist(Rc r, float x, float z) { float dx = Mathf.Max(0f, Mathf.Max(r.x0 - x, x - r.x1)), dz = Mathf.Max(0f, Mathf.Max(r.z0 - z, z - r.z1)); return Mathf.Sqrt(dx * dx + dz * dz); }
        static float PingPong(float t, float L) { float m = t - Mathf.Floor(t / (2f * L)) * 2f * L; return L - Mathf.Abs(m - L); }

        sealed class Field
        {
            public List<Rc> wet = new List<Rc>(), dry = new List<Rc>(), fresh = new List<Rc>();
            public float S(float x, float z)
            {
                float dOut = 1e9f, dIn = 1e9f;
                foreach (var r in wet) dOut = Mathf.Min(dOut, RcDist(r, x, z));
                if (dOut > 0f) return -dOut;
                foreach (var r in dry) dIn = Mathf.Min(dIn, RcDist(r, x, z));
                return dIn;
            }
            public float W(float x, float z)
            {
                float d = 1e9f; foreach (var r in fresh) d = Mathf.Min(d, RcDist(r, x, z));
                float k = Mathf.Clamp01((d - Full) / (Fade - Full)); return 1f - k * k * (3f - 2f * k);
            }
        }

        static Field MakeField(GridDoc g)
        {
            var f = new Field();
            for (int ty = 54; ty < 75; ty++) for (int tx = 16; tx < 33; tx++)
                {
                    int c = g.classes[ty * g.width + tx];
                    (c == 2 || c == 4 ? f.wet : f.dry).Add(TileRc(tx, ty));
                }
            for (int i = 0; i < NewWater.Length; i += 2) f.fresh.Add(TileRc(NewWater[i], NewWater[i + 1]));
            return f;
        }

        /// <summary>Top of the walk collider (the ground and the stream beds) at a point; NaN where there is none.</summary>
        static float Top(MeshCollider col, float x, float z) => col.Raycast(new Ray(new Vector3(x, 200f, z), Vector3.down), out var hit, 400f) ? hit.point.y : float.NaN;

        /// <summary>The height the copy asks for at a point (the channel north of the island at the same distance from its edge),
        /// blended for edges running north-south and east-west as the data script blends the pictures.</summary>
        static float Wanted(Field f, MeshCollider col, float x, float z)
        {
            const float e = 0.25f;
            float s = f.S(x, z), gx = f.S(x + e, z) - f.S(x - e, z), gz = f.S(x, z + e) - f.S(x, z - e);
            float sc = Mathf.Clamp(s, -Fade, 2f);
            float north = SrcEdgeZ - sc, south = SrcEdgeZ - 4f + sc;
            float hv = Top(col, SrcX0 + PingPong(-z - 116f, SrcLen), gx >= 0f ? north : south);
            float hh = Top(col, SrcX0 + PingPong(x - 40f, SrcLen), gz <= 0f ? north : south);
            float ax = Mathf.Abs(gx), az = Mathf.Abs(gz);
            float a = Mathf.Clamp01((ax - az) / Mathf.Max(ax + az, 1e-6f) * 2f + 0.5f); a = a * a * (3f - 2f * a);
            if (float.IsNaN(hv)) return hh; if (float.IsNaN(hh)) return hv;
            return hv * a + hh * (1f - a);
        }

        static string Channel(bool apply, GameObject env, Func<string, Transform> T, MeshCollider col, Transform watT, GridDoc g)
        {
            var log = new StringBuilder("  bug 3, the channel west of the gatling island:\n");
            var tg = T("TER_Ground"); var mfG = tg.GetComponent<MeshFilter>();
            if (mfG && mfG.sharedMesh && mfG.sharedMesh.name.EndsWith(Tag)) return log.Append("    already done (TER_Ground uses its kit mesh)\n").ToString();
            var f = MakeField(g);
            // the new height on a 0.125 m grid over the box, from the walk collider as it is now (nothing is changed before all is measured)
            const float c = 0.125f; int nx = Mathf.RoundToInt((BX1 - BX0) / c) + 1, nz = Mathf.RoundToInt((BZ1 - BZ0) / c) + 1;
            var H = new float[nx, nz]; int lowered = 0; float maxDrop = 0f;
            for (int i = 0; i < nx; i++) for (int j = 0; j < nz; j++)
                {
                    float x = BX0 + i * c, z = BZ0 + j * c, h0 = Top(col, x, z), w = f.W(x, z);
                    H[i, j] = h0;
                    if (float.IsNaN(h0) || w <= 0f) continue;
                    float hs = Wanted(f, col, x, z); if (float.IsNaN(hs) || hs >= h0) continue;
                    H[i, j] = h0 + w * (hs - h0); lowered++; maxDrop = Mathf.Max(maxDrop, h0 - H[i, j]);
                }
            float Hb(float x, float z)
            {
                float u = Mathf.Clamp((x - BX0) / c, 0f, nx - 1.001f), v = Mathf.Clamp((z - BZ0) / c, 0f, nz - 1.001f);
                int i = (int)u, j = (int)v; float a = u - i, b = v - j;
                return Mathf.Lerp(Mathf.Lerp(H[i, j], H[i + 1, j], a), Mathf.Lerp(H[i, j + 1], H[i + 1, j + 1], a), b);
            }
            Vector3 Nb(float x, float z) { const float e = 0.25f; return new Vector3(Hb(x - e, z) - Hb(x + e, z), 2f * e, Hb(x, z - e) - Hb(x, z + e)).normalized; }
            log.Append($"    height grid {nx} x {nz} (0.125 m): {lowered} points lowered, deepest by {maxDrop:0.00} m\n");
            float bedMin = float.MaxValue; for (int i = 0; i < nx; i++) for (int j = 0; j < nz; j++) { float x = BX0 + i * c, z = BZ0 + j * c; if (x > 46.5f && x < 51.5f && z > -134f && z < -122f && !float.IsNaN(H[i, j])) bedMin = Mathf.Min(bedMin, H[i, j]); }
            log.Append($"    new channel bed (x 46.5-51.5, z -134..-122): lowest {bedMin:0.00} m\n");

            // the three ground meshes
            var made = new List<(Component, Mesh)>();
            foreach (var n in GroundMeshes)
            {
                var t = T(n); var mf = t.GetComponent<MeshFilter>(); var mc = t.GetComponent<MeshCollider>();
                var src = mf && mf.sharedMesh ? mf.sharedMesh : mc ? mc.sharedMesh : null;
                if (!src) { log.Append($"    {n}: no mesh\n"); continue; }
                var v = src.vertices; var M = t.localToWorldMatrix; var inv = t.worldToLocalMatrix;
                bool hasN = n != "COL_Terrain" && src.normals != null && src.normals.Length == v.Length;
                var nn = hasN ? src.normals : null; int moved = 0;
                for (int i = 0; i < v.Length; i++)
                {
                    var p = M.MultiplyPoint3x4(v[i]);
                    if (p.x < BX0 || p.x > BX1 || p.z < BZ0 || p.z > BZ1) continue;
                    float w = f.W(p.x, p.z); if (w <= 0f) continue;
                    float hs = Wanted(f, col, p.x, p.z); if (float.IsNaN(hs) || hs >= p.y - 0.002f) continue;
                    float y = p.y + w * (hs - p.y);
                    v[i] = inv.MultiplyPoint3x4(new Vector3(p.x, y, p.z)); moved++;
                    if (hasN) nn[i] = inv.MultiplyVector(Nb(p.x, p.z)).normalized;
                }
                log.Append($"    {n} ({AssetDatabase.GetAssetPath(src)}): {moved} of {v.Length} vertices lowered\n");
                if (!apply || moved == 0) continue;
                var m = UnityEngine.Object.Instantiate(src); m.name = "L16_" + n + Tag;
                m.vertices = v; if (hasN) m.normals = nn; m.RecalculateBounds();
                Directory.CreateDirectory(Gen); string path = $"{Gen}/{m.name}.asset";
                AssetDatabase.DeleteAsset(path); AssetDatabase.CreateAsset(m, path);
                if (mf) { Undo.RecordObject(mf, "L16 map fixes"); mf.sharedMesh = m; EditorUtility.SetDirty(mf); }
                if (mc) { Undo.RecordObject(mc, "L16 map fixes"); mc.sharedMesh = null; mc.sharedMesh = m; EditorUtility.SetDirty(mc); }
                made.Add((mf ? (Component)mf : mc, m));
            }

            // the water sheet
            var wmf = watT.GetComponent<MeshFilter>(); var wm = wmf ? wmf.sharedMesh : null;
            if (!wm) return log.Append("    WAT_Water: no mesh\n").ToString();
            var WM = watT.localToWorldMatrix; var WI = watT.worldToLocalMatrix;
            var wv = wm.vertices; var wn = wm.normals; var wt = wm.tangents; var wu = wm.uv; var tri = wm.triangles;
            bool hasWn = wn != null && wn.Length == wv.Length, hasWt = wt != null && wt.Length == wv.Length, hasWu = wu != null && wu.Length == wv.Length;
            var ww = wv.Select(p => WM.MultiplyPoint3x4(p)).ToArray();
            var near = ww.Where(p => p.x > WX0 - 4f && p.x < WX1 + 4f && p.z > WZ0 - 4f && p.z < WZ1 + 4f).Select(p => p.y).OrderBy(y => y).ToList();
            if (near.Count == 0) return log.Append("    WAT_Water: no water near the channel\n").ToString();
            float wy = near[near.Count / 2];
            if (near.Last() - near.First() > 0.05f) log.Append($"    WAT_Water: heights near the channel vary {near.First():0.000} .. {near.Last():0.000} (the sheet takes the middle one)\n");
            // uv = a x + b z + c (least squares over the vertices near the channel)
            Vector3 ru = Vector3.zero, rv = Vector3.zero; bool uvOk = false;
            if (hasWu)
            {
                double sxx = 0, sxz = 0, szz = 0, sx = 0, sz = 0, n0 = 0, sux = 0, suz = 0, su = 0, svx = 0, svz = 0, sv = 0;
                for (int i = 0; i < ww.Length; i++)
                {
                    var p = ww[i]; if (p.x < WX0 - 8f || p.x > WX1 + 8f || p.z < WZ0 - 8f || p.z > WZ1 + 8f) continue;
                    sxx += p.x * p.x; sxz += p.x * p.z; szz += p.z * p.z; sx += p.x; sz += p.z; n0++;
                    sux += wu[i].x * p.x; suz += wu[i].x * p.z; su += wu[i].x; svx += wu[i].y * p.x; svz += wu[i].y * p.z; sv += wu[i].y;
                }
                var A = new Matrix4x4(new Vector4((float)sxx, (float)sxz, (float)sx, 0), new Vector4((float)sxz, (float)szz, (float)sz, 0), new Vector4((float)sx, (float)sz, (float)n0, 0), new Vector4(0, 0, 0, 1));
                if (n0 >= 3 && Mathf.Abs(A.determinant) > 1e-6f)
                {
                    var Ai = A.inverse;
                    ru = Ai.MultiplyVector(new Vector3((float)sux, (float)suz, (float)su)); rv = Ai.MultiplyVector(new Vector3((float)svx, (float)svz, (float)sv)); uvOk = true;
                    float err = 0f; for (int i = 0; i < ww.Length; i++) { var p = ww[i]; if (p.x < WX0 - 8f || p.x > WX1 + 8f || p.z < WZ0 - 8f || p.z > WZ1 + 8f) continue; err = Mathf.Max(err, Mathf.Abs(ru.x * p.x + ru.y * p.z + ru.z - wu[i].x) + Mathf.Abs(rv.x * p.x + rv.y * p.z + rv.z - wu[i].y)); }
                    log.Append($"    WAT_Water uv: planar fit, largest miss {err:0.0000}\n");
                }
            }
            var V = new List<Vector3>(wv); var N = hasWn ? new List<Vector3>(wn) : null; var Tn = hasWt ? new List<Vector4>(wt) : null; var U = hasWu ? new List<Vector2>(wu) : null;
            var outTri = new List<int>(tri.Length + 4096); int cut = 0, kept = 0, split = 0;
            Vector4 tan0 = hasWt && wt.Length > 0 ? wt[0] : new Vector4(1, 0, 0, -1);
            int AddV(Vector3 world, int a, int b, float t)
            {
                V.Add(WI.MultiplyPoint3x4(world));
                if (N != null) N.Add(Vector3.Lerp(wn[a], wn[b], t).normalized);
                if (Tn != null) Tn.Add(Vector4.Lerp(wt[a], wt[b], t));
                if (U != null) U.Add(uvOk ? new Vector2(ru.x * world.x + ru.y * world.z + ru.z, rv.x * world.x + rv.y * world.z + rv.z) : Vector2.Lerp(wu[a], wu[b], t));
                return V.Count - 1;
            }
            // clip one triangle to a convex region given by half planes (a x + b z <= c); vertices carry (world, the two old indices, t)
            List<(Vector3 p, int a, int b, float t)> Clip(List<(Vector3 p, int a, int b, float t)> poly, float ka, float kb, float kc)
            {
                var res = new List<(Vector3, int, int, float)>();
                for (int i = 0; i < poly.Count; i++)
                {
                    var P = poly[i]; var Q = poly[(i + 1) % poly.Count];
                    float dp = ka * P.p.x + kb * P.p.z - kc, dq = ka * Q.p.x + kb * Q.p.z - kc;
                    if (dp <= 0f) res.Add(P);
                    if ((dp < 0f && dq > 0f) || (dp > 0f && dq < 0f))
                    {
                        float s = dp / (dp - dq); var p = Vector3.Lerp(P.p, Q.p, s);
                        // attributes: interpolate between the two old vertices nearest along the edge (both ends carry their own old pair)
                        res.Add((p, P.a, Q.a, s));
                    }
                }
                return res;
            }
            for (int k = 0; k < tri.Length; k += 3)
            {
                int i0 = tri[k], i1 = tri[k + 1], i2 = tri[k + 2];
                var p0 = ww[i0]; var p1 = ww[i1]; var p2 = ww[i2];
                bool In(Vector3 p) => p.x > WX0 && p.x < WX1 && p.z > WZ0 && p.z < WZ1;
                float mnx = Mathf.Min(p0.x, Mathf.Min(p1.x, p2.x)), mxx = Mathf.Max(p0.x, Mathf.Max(p1.x, p2.x)), mnz = Mathf.Min(p0.z, Mathf.Min(p1.z, p2.z)), mxz = Mathf.Max(p0.z, Mathf.Max(p1.z, p2.z));
                if (mxx <= WX0 || mnx >= WX1 || mxz <= WZ0 || mnz >= WZ1) { outTri.Add(i0); outTri.Add(i1); outTri.Add(i2); kept++; continue; }
                if (In(p0) && In(p1) && In(p2) && mnx >= WX0 && mxx <= WX1 && mnz >= WZ0 && mxz <= WZ1) { cut++; continue; }
                split++;
                var tri0 = new List<(Vector3 p, int a, int b, float t)> { (p0, i0, i0, 0f), (p1, i1, i1, 0f), (p2, i2, i2, 0f) };
                // outside the box = { x <= WX0 } + { x >= WX1 } + { WX0 < x < WX1, z <= WZ0 } + { WX0 < x < WX1, z >= WZ1 }
                var pieces = new[]
                {
                    Clip(tri0, 1f, 0f, WX0),
                    Clip(tri0, -1f, 0f, -WX1),
                    Clip(Clip(Clip(tri0, -1f, 0f, -WX0), 1f, 0f, WX1), 0f, 1f, WZ0),
                    Clip(Clip(Clip(tri0, -1f, 0f, -WX0), 1f, 0f, WX1), 0f, -1f, -WZ1),
                };
                foreach (var poly in pieces)
                {
                    if (poly.Count < 3) continue;
                    var idx = poly.Select(q => q.t == 0f && q.a == q.b ? q.a : AddV(q.p, q.a, q.b, q.t)).ToList();
                    // keep the winding of the old triangle
                    var o = Vector3.Cross(p1 - p0, p2 - p0).y;
                    for (int j = 1; j + 1 < idx.Count; j++)
                    {
                        var a = poly[0].p; var b = poly[j].p; var cc = poly[j + 1].p;
                        if (Mathf.Abs(Vector3.Cross(b - a, cc - a).y) < 1e-7f) continue;
                        if (Vector3.Cross(b - a, cc - a).y * o >= 0f) { outTri.Add(idx[0]); outTri.Add(idx[j]); outTri.Add(idx[j + 1]); }
                        else { outTri.Add(idx[0]); outTri.Add(idx[j + 1]); outTri.Add(idx[j]); }
                    }
                }
            }
            // the sheet over the box (winding as the old water: its first triangle's normal points up in the world)
            bool up = tri.Length >= 3 && Vector3.Cross(ww[tri[1]] - ww[tri[0]], ww[tri[2]] - ww[tri[0]]).y > 0f;
            int cx = Mathf.CeilToInt((WX1 - WX0) / Cell), cz = Mathf.CeilToInt((WZ1 - WZ0) / Cell), first = V.Count;
            var nUp = WI.MultiplyVector(Vector3.up).normalized; var tX = WI.MultiplyVector(Vector3.right).normalized;
            for (int j = 0; j <= cz; j++) for (int i = 0; i <= cx; i++)
                {
                    float x = Mathf.Min(WX0 + i * Cell, WX1), z = Mathf.Min(WZ0 + j * Cell, WZ1);
                    V.Add(WI.MultiplyPoint3x4(new Vector3(x, wy, z)));
                    if (N != null) N.Add(nUp);
                    if (Tn != null) Tn.Add(new Vector4(tX.x, tX.y, tX.z, tan0.w));
                    if (U != null) U.Add(uvOk ? new Vector2(ru.x * x + ru.y * z + ru.z, rv.x * x + rv.y * z + rv.z) : Vector2.zero);
                }
            for (int j = 0; j < cz; j++) for (int i = 0; i < cx; i++)
                {
                    int a = first + j * (cx + 1) + i, b = a + 1, d = a + cx + 1, e2 = d + 1;
                    // a (x, z), b (x + 1, z), d (x, z + 1): (b - a) x (d - a) has y < 0 with x east and z north: down
                    if (up) { outTri.Add(a); outTri.Add(d); outTri.Add(b); outTri.Add(b); outTri.Add(d); outTri.Add(e2); }
                    else { outTri.Add(a); outTri.Add(b); outTri.Add(d); outTri.Add(b); outTri.Add(e2); outTri.Add(d); }
                }
            log.Append($"    WAT_Water ({AssetDatabase.GetAssetPath(wm)}): water height {wy:0.000}; {kept} triangles kept, {cut} under the new sheet removed, {split} cut at its edge; sheet {cx} x {cz} cells of {Cell} m\n");
            int flooded = 0;
            for (int j = 0; j <= cz * 2; j++) for (int i = 0; i <= cx * 2; i++)
                {
                    float x = WX0 + i * Cell * 0.5f, z = WZ0 + j * Cell * 0.5f; float h = Hb(x, z);
                    int tx = Mathf.FloorToInt(x / 2f), ty = Mathf.FloorToInt(-z / 2f); int cls = g.classes[ty * g.width + tx];
                    if (!float.IsNaN(h) && h < wy - 0.02f && cls != 2 && cls != 4 && f.S(x, z) < -1.2f) flooded++;
                }
            log.Append($"    ground below the water more than 1.2 m from any water tile under the sheet: {flooded} points (must be 0)\n");
            if (!apply) return log.ToString();
            if (V.Count > 65000 && wm.indexFormat == UnityEngine.Rendering.IndexFormat.UInt16) return log.Append("    WAT_Water: too many vertices for 16-bit indices: not changed\n").ToString();
            var nm = UnityEngine.Object.Instantiate(wm); nm.name = "L16_WAT_Water" + Tag;
            nm.Clear(); nm.indexFormat = wm.indexFormat;
            nm.SetVertices(V); if (N != null) nm.SetNormals(N); if (Tn != null) nm.SetTangents(Tn); if (U != null) nm.SetUVs(0, U);
            nm.subMeshCount = 1; nm.SetTriangles(outTri, 0); nm.RecalculateBounds();
            string wpath = $"{Gen}/{nm.name}.asset"; AssetDatabase.DeleteAsset(wpath); AssetDatabase.CreateAsset(nm, wpath);
            Undo.RecordObject(wmf, "L16 map fixes"); wmf.sharedMesh = nm; EditorUtility.SetDirty(wmf);
            log.Append($"    written: {made.Count} ground meshes + the water sheet under {Gen} (names end in {Tag})\n");
            // what stands in the new channel (would float over the water or stand in it)
            int props = 0;
            foreach (var r in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (r.transform.IsChildOf(env.transform)) continue;
                var b = r.bounds; if (b.size.x > 12f || b.size.z > 12f) continue;
                float x = b.center.x, z = b.center.z; if (x < 45f || x > 53f || z < -137f || z > -119f) continue;
                float h = Hb(x, z); if (float.IsNaN(h) || b.min.y - h < 0.25f) continue;
                if (++props <= 20) log.Append($"    stands above the new ground: {PathOf(r.transform)} at ({x:0.0}, {z:0.0}) bottom {b.min.y:0.00} -> ground {h:0.00}\n");
            }
            log.Append($"    objects above the new ground in the channel: {props}\n");
            return log.ToString();
        }

        // ------------------------------------------------------------------------------------------------ bug 5: rock tints
        static string Rocks(bool apply)
        {
            var log = new StringBuilder("  bug 5, the rock-tile pillars' tint (the rim rocks, the rock tops and the cliffs beside them are white):\n");
            foreach (var n in RockMats)
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>($"{RockDir}/{n}.mat");
                if (!m || !m.HasProperty("_BaseColor")) { log.Append($"    {n}: not found\n"); continue; }
                var c = m.GetColor("_BaseColor");
                if (c == Color.white) { log.Append($"    {n}: already white\n"); continue; }
                log.Append($"    {n}: ({c.r:0.00}, {c.g:0.00}, {c.b:0.00}) -> (1, 1, 1)\n");
                if (apply) { Undo.RecordObject(m, "L16 map fixes"); m.SetColor("_BaseColor", Color.white); EditorUtility.SetDirty(m); }
            }
            return log.ToString();
        }

        // ------------------------------------------------------------------------------------------------ bug 6: ruins in the ridge
        static string RuinsFix(bool apply, MeshCollider col, GridDoc g)
        {
            var log = new StringBuilder("  bug 6, ruins in the ridge west of the middle (tiles x 14-38, rows 77-79):\n");
            var root = GameObject.Find("L16_Structures"); var ruins = root ? root.transform.Find("Ruins") : null;
            if (!ruins) return log.Append("    L16_Structures/Ruins not found\n").ToString();
            foreach (var n in RuinsMove.Concat(RuinsMeasure))
            {
                var t = ruins.Find(n); if (!t) { log.Append($"    {n}: not found\n"); continue; }
                var rs = t.GetComponentsInChildren<Renderer>(true).Where(r => r.name == "LOD0").ToArray();
                if (rs.Length == 0) rs = t.GetComponentsInChildren<Renderer>(true);
                if (rs.Length == 0) { log.Append($"    {n}: nothing drawn\n"); continue; }
                var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                float baseY = b.min.y;
                float Buried(float dz)
                {
                    int cells = 0, high = 0;
                    for (float x = b.min.x; x <= b.max.x; x += 0.25f) for (float z = b.min.z + dz; z <= b.max.z + dz; z += 0.25f) { float h = Top(col, x, z); cells++; if (!float.IsNaN(h) && h > baseY + RuinClear) high++; }
                    return cells == 0 ? 0f : (float)high / cells;
                }
                float now = Buried(0f);
                bool move = RuinsMove.Contains(n);
                if (!move) { log.Append($"    {n}: at ({t.position.x:0.0}, {t.position.y:0.00}, {t.position.z:0.0}), footprint {b.size.x:0.0} x {b.size.z:0.0} m, {now * 100f:0} % of it under ground higher than its foot + {RuinClear} m (reported only)\n"); continue; }
                if (now <= 0f) { log.Append($"    {n}: clear of the ridge (already done)\n"); continue; }
                float pull = -1f;
                for (float d = 0.1f; d <= RuinMaxPull; d += 0.1f) if (Buried(-d) <= 0f) { pull = d + RuinMargin; break; }
                if (pull < 0f) { log.Append($"    {n}: no clear ground within {RuinMaxPull} m south of it: left as it is (remove it?)\n"); continue; }
                float ground = float.MaxValue; var tiles = new SortedSet<string>();
                for (float x = b.min.x; x <= b.max.x; x += 0.25f) for (float z = b.min.z - pull; z <= b.max.z - pull; z += 0.25f)
                    {
                        float h = Top(col, x, z); if (!float.IsNaN(h)) ground = Mathf.Min(ground, h);
                        int tx = Mathf.FloorToInt(x / 2f), ty = Mathf.FloorToInt(-z / 2f); tiles.Add($"{tx},{ty}:{g.classes[ty * g.width + tx]}");
                    }
                float dy = ground - RuinSink - baseY;
                log.Append($"    {n}: {now * 100f:0} % in the ridge; pulled {pull:0.0} m south, foot {baseY:0.00} -> {baseY + dy:0.00} on the ledge; new tiles (x,y:class) {string.Join(" ", tiles)}\n");
                if (apply) { Undo.RecordObject(t, "L16 map fixes"); t.position += new Vector3(0f, dy, -pull); EditorUtility.SetDirty(t); }
            }
            return log.ToString();
        }

        // ------------------------------------------------------------------------------------------------ undo
        public static string Restore()
        {
            var log = new StringBuilder("[OpenZ L16 map fixes] undo\n");
            if (EditorApplication.isPlayingOrWillChangePlaymode) return log.Append("  stop Play mode first").ToString();
            if (!Directory.Exists(BackupRoot)) return log.Append("  no undo copy in " + BackupRoot).ToString();
            var dir = Directory.GetDirectories(BackupRoot).OrderBy(d => d).LastOrDefault();
            if (dir == null || !File.Exists(Path.Combine(dir, "scene_path.txt"))) return log.Append("  no undo copy in " + BackupRoot).ToString();
            string scenePath = File.ReadAllText(Path.Combine(dir, "scene_path.txt")).Trim();
            string prj = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            File.Copy(Path.Combine(dir, Path.GetFileName(scenePath)), Path.Combine(prj, scenePath), true); log.Append($"  {scenePath} put back from {dir}\n");
            foreach (var n in RockMats)
            {
                string src = Path.Combine(dir, n + ".mat"); if (!File.Exists(src)) continue;
                File.Copy(src, Path.Combine(prj, $"{RockDir}/{n}.mat"), true); log.Append($"  {RockDir}/{n}.mat put back\n");
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            foreach (var n in GroundMeshes.Concat(new[] { "WAT_Water" }))
            {
                string p = $"{Gen}/L16_{n}{Tag}.asset"; if (AssetDatabase.LoadAssetAtPath<Mesh>(p) && AssetDatabase.DeleteAsset(p)) log.Append($"  {p} removed\n");
            }
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            log.Append("  done (the level data and the ground pictures go back with the data script's own copy: the kit's backup_* folder)\n");
            return log.ToString();
        }

        static string PathOf(Transform t) { var s = t.name; while (t.parent) { t = t.parent; s = t.name + "/" + s; } return s; }
    }
}
