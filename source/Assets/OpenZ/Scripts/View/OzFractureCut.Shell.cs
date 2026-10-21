using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Version 1.1, the shuttle's break-up pieces as broken metal (item 375's look, for the ship). The ship's rear body breaks into curved
    /// hull panels: open shells, not solid blocks. So their cuts are torn with the cannon's own fracture field but left open
    /// (<see cref="Tear"/>), and every panel becomes a plate of real thickness (<see cref="Plate"/>):
    /// <list type="bullet">
    /// <item>the tear follows a seeded fracture surface (a tilted plane minus a height field of long bends, a mid layer, fine ripples and
    /// small steps), each side its own field, so the edge is jagged and no two panels match; the painted lip near the tear is bent in a
    /// few centimetres and darkened by soot (vertex colours);</item>
    /// <item>the inside of the plate is <see cref="PlateM"/> under the painted skin: dark scorched broken steel, its texture in metres
    /// with a turn and an offset of its own (no grid, nothing lines up), sooty near the tear, rust, straw and blue temper colours along
    /// the rim;</item>
    /// <item>a wall closes the plate along every torn edge (the plate's thickness seen at the break).</item>
    /// </list>
    /// The domes' small shard cut is the cannon's relief cut (<see cref="ReliefQuiet"/>), with the cannon's counters left as they were.
    /// Mesh space (the ship's root space, metres). View only. Any failure falls back to the plain cut.
    /// </summary>
    public static partial class OzFractureCut
    {
        // ------------------------------------------------------------------ tuning (metres)
        /// <summary>The deepest a tear eats into a panel, and its share of the panel's extent behind the cut.</summary>
        public static float ShellDepth = 0.32f, ShellThickShare = 0.3f;
        /// <summary>The plate's thickness (its inside under the painted skin), the soot's reach from the tear and its darkest level (x).</summary>
        public static float PlateM = 0.05f, SootReach = 0.6f, SootEdge = 0.45f;
        /// <summary>The inside's overall level (x the broken-steel texture and the material's colour) and its wall's level.</summary>
        public static float PlateLevel = 1f, WallLevel = 0.6f;

        // ------------------------------------------------------------------ proof (the shuttle's own counters; the cannon's stay its own)
        public static int ShellTears, ShellPlain, Plates, PlateInnerTris, PlateWallTris, PlateEdges, ReliefCuts, ReliefPlain;
        public static float ShellMaxMs;
        public static string ShellError = "-";
        public static string ShellState => $"torn cuts {ShellTears} (plain {ShellPlain}{(ShellPlain > 0 ? ": " + ShellError : "")}), plates {Plates} (inside triangles {PlateInnerTris}, " +
                                            $"wall triangles {PlateWallTris}, torn edges {PlateEdges}), dome relief cuts {ReliefCuts} (plain {ReliefPlain}), slowest {ShellMaxMs:0.0} ms";

        /// <summary>One torn edge of a plate (for the ember rim): a -> b on the painted skin, the way into the plate, the skin's normal there.</summary>
        public struct TornEdge { public Vector3 a, b, into, n; }

        // ------------------------------------------------------------------ the open tear
        /// <summary>Tears the open shell `src` along the plane dot(n, p) = w (mesh space in metres): `front` = the side n points to. Draws a
        /// fixed count of numbers from `rnd`. False = the plane misses it. A tear that fails is the plain open cut (counted).</summary>
        public static bool Tear(OzMeshSlice.Part src, Vector3 n, float w, System.Random rnd, out OzMeshSlice.Part front, out OzMeshSlice.Part back)
        {
            front = null; back = null;
            if (src == null) return false;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            float tiltDeg = R(rnd, TiltMin, TiltMax), tiltAng = R(rnd, 0f, Mathf.PI * 2f);
            int seedF = rnd.Next(), seedB = rnd.Next();
            bool ok;
            try { ok = TearCarve(src, n, w, tiltDeg, tiltAng, seedF, seedB, out front, out back); }
            catch (System.Exception e) { ok = false; ShellError = e.GetType().Name + ": " + e.Message; }
            float ms = (float)clock.Elapsed.TotalMilliseconds; if (ms > ShellMaxMs) ShellMaxMs = ms;
            if (ok) { ShellTears++; return true; }
            front = null; back = null;
            if (!OzMeshSlice.Split(src, n, w, out front, out back, false, false)) return false;
            ShellPlain++; if (ShellError == "-") ShellError = "the tear left one side empty: the plain cut";
            return true;
        }

        /// <summary>Carve's steps 1-3 (the skin refined near the break, clipped by each side's field, the lip bent in and sooted) with the
        /// shell's own depth and no cap; never touches the cannon's numbers.</summary>
        static bool TearCarve(OzMeshSlice.Part src, Vector3 n, float w, float tiltDeg, float tiltAng, int seedF, int seedB, out OzMeshSlice.Part front, out OzMeshSlice.Part back)
        {
            front = null; back = null;
            n.Normalize();
            int nv = src.v.Count; if (nv < 3) return false;
            Basis(n, out var e1, out var e2);
            float dMin = float.MaxValue, dMax = float.MinValue, uMin = float.MaxValue, uMax = float.MinValue, vMin = float.MaxValue, vMax = float.MinValue;
            var mean = Vector3.zero;
            for (int i = 0; i < nv; i++)
            {
                var p = src.v[i].p; float d = Vector3.Dot(n, p) - w; mean += p;
                if (d < dMin) dMin = d; if (d > dMax) dMax = d;
                float u = Vector3.Dot(p, e1), v = Vector3.Dot(p, e2);
                if (u < uMin) uMin = u; if (u > uMax) uMax = u; if (v < vMin) vMin = v; if (v > vMax) vMax = v;
            }
            if (dMax <= 0f || dMin >= 0f) return false;
            mean /= nv;
            float thickF = dMax, thickB = -dMin;
            if (tiltDeg > 0.01f)
            {
                float half = 0.5f * Mathf.Max(uMax - uMin, vMax - vMin);
                float lim = half > 1e-4f ? Mathf.Atan(TiltThickShare * Mathf.Min(thickF, thickB) / half) * Mathf.Rad2Deg : 0f;
                float deg = Mathf.Min(tiltDeg, lim);
                if (deg > 0.01f)
                {
                    var c = mean - n * (Vector3.Dot(n, mean) - w);
                    var ax = (Mathf.Cos(tiltAng) * e1 + Mathf.Sin(tiltAng) * e2).normalized;
                    n = Turn(n, ax, deg).normalized; w = Vector3.Dot(n, c);
                    Basis(n, out e1, out e2);
                    dMin = float.MaxValue; dMax = float.MinValue;
                    for (int i = 0; i < nv; i++) { float d = Vector3.Dot(n, src.v[i].p) - w; if (d < dMin) dMin = d; if (d > dMax) dMax = d; }
                    if (dMax <= 0f || dMin >= 0f) return false;
                    thickF = dMax; thickB = -dMin;
                }
            }
            var fF = new Field(new System.Random(seedF), e1, e2, 1f, Mathf.Min(ShellDepth, ShellThickShare * thickF));
            var fB = new Field(new System.Random(seedB), e1, e2, 1f, Mathf.Min(ShellDepth, ShellThickShare * thickB));
            float q = Quantum(src);
            float lo = -(fB.Amp + Margin), hi = fF.Amp + Margin;
            var p0 = Refine(src, n, w, lo, hi, Edge, q);
            int m = p0.v.Count; var dF = new float[m]; var dB = new float[m];
            for (int i = 0; i < m; i++)
            {
                var p = p0.v[i].p; float d = Vector3.Dot(n, p) - w;
                bool near = d > lo - Edge && d < hi + Edge;
                dF[i] = d - (near ? fF.H(p) : 0f); dB[i] = -d - (near ? fB.H(p) : 0f);
            }
            var segF = new List<Seg>(); var segB = new List<Seg>(); var distF = new List<float>(); var distB = new List<float>();
            var F = Clip(p0, dF, segF, q, distF); var B = Clip(p0, dB, segB, q, distB);
            if (F.Tris == 0 || B.Tris == 0) return false;
            Lip(F, distF, fF, 1f, q); Lip(B, distB, fB, 1f, q);
            front = F; back = B;
            return true;
        }

        // ------------------------------------------------------------------ the plate (inside, walls) of an open panel
        /// <summary>Makes the open panel `p` a plate: its inside (every skin triangle again, turned round, <see cref="PlateM"/> in along the
        /// skin's averaged normal, so the inside has no cracks) and a wall along every open edge, both in the cap sub-mesh (the broken-steel
        /// material) with their UVs in metres (one turn and offset per panel from `rnd`) and their colours (soot near the tear, rust and
        /// temper at the rim, a slow variation). Returns the torn edges (for the ember rim). The panel's open edges are its tears: the body
        /// it comes from is closed.</summary>
        public static List<TornEdge> Plate(OzMeshSlice.Part p, System.Random rnd)
        {
            var edges = new List<TornEdge>();
            if (p == null) return edges;
            float rot = R(rnd, 0f, Mathf.PI * 2f); var off = new Vector2(R(rnd, 0f, 1f), R(rnd, 0f, 1f)); int seed = rnd.Next();
            int cs = p.CapSub; var cap = p.sub[cs]; cap.Clear();
            p.hc = true; p.ht = true;
            float q = Quantum(p);
            int nv0 = p.v.Count;
            // 1. welded corners, their area-weighted normals and the panel's mean inside normal
            var wid = new int[nv0]; var weld = new Dictionary<Vector3Int, int>(nv0);
            for (int i = 0; i < nv0; i++) { var k = Key(p.v[i].p, q); if (!weld.TryGetValue(k, out int id)) { id = weld.Count; weld[k] = id; } wid[i] = id; }
            var wn = new Vector3[weld.Count]; var sumN = Vector3.zero;
            var edgeUse = new Dictionary<long, int>(); var edgeAt = new Dictionary<long, (int a, int b, int c)>();
            for (int s = 0; s < cs; s++)
            {
                var t = p.sub[s];
                for (int k = 0; k + 2 < t.Count; k += 3)
                {
                    int a = t[k], b = t[k + 1], c = t[k + 2];
                    var fn = Vector3.Cross(p.v[b].p - p.v[a].p, p.v[c].p - p.v[a].p);
                    wn[wid[a]] += fn; wn[wid[b]] += fn; wn[wid[c]] += fn; sumN += fn;
                    Use(a, b, c); Use(b, c, a); Use(c, a, b);
                }
            }
            void Use(int x, int y, int z)
            {
                long ek = EdgeKey(wid[x], wid[y]);
                edgeUse.TryGetValue(ek, out int u); edgeUse[ek] = u + 1;
                if (u == 0) edgeAt[ek] = (x, y, z);
            }
            for (int i = 0; i < wn.Length; i++) wn[i] = wn[i].sqrMagnitude > 1e-20f ? wn[i].normalized : Vector3.up;
            var inN = sumN.sqrMagnitude > 1e-12f ? -sumN.normalized : Vector3.down;
            Basis(inN, out var b1, out var b2);
            var tdir = Mathf.Cos(rot) * b1 + Mathf.Sin(rot) * b2; var bdir = Vector3.Cross(inN, tdir);
            float inv = 1f / Mathf.Max(0.1f, TileMetres);
            var tan = new Vector4(tdir.x, tdir.y, tdir.z, 1f);
            // 2. the torn (open) edges
            var open = new List<(int a, int b, int c)>();
            foreach (var kv in edgeUse) if (kv.Value == 1) open.Add(edgeAt[kv.Key]);
            var segA = new Vector3[open.Count]; var segB = new Vector3[open.Count];
            for (int i = 0; i < open.Count; i++) { segA[i] = p.v[open[i].a].p; segB[i] = p.v[open[i].b].p; }
            var noise = new Field(new System.Random(seed), b1, b2, 1f, 0.1f);
            // the distance of a point to the nearest torn edge (m, up to SootReach), looked up once per welded corner in a grid of the edges
            float cell = Mathf.Max(0.1f, SootReach);
            var grid = new Dictionary<Vector3Int, List<int>>();
            for (int e = 0; e < segA.Length; e++)
            {
                var lo3 = Vector3.Min(segA[e], segB[e]); var hi3 = Vector3.Max(segA[e], segB[e]);
                var c0 = new Vector3Int(Mathf.FloorToInt(lo3.x / cell), Mathf.FloorToInt(lo3.y / cell), Mathf.FloorToInt(lo3.z / cell));
                var c1 = new Vector3Int(Mathf.FloorToInt(hi3.x / cell), Mathf.FloorToInt(hi3.y / cell), Mathf.FloorToInt(hi3.z / cell));
                for (int x = c0.x; x <= c1.x; x++) for (int y = c0.y; y <= c1.y; y++) for (int z = c0.z; z <= c1.z; z++)
                {
                    var k = new Vector3Int(x, y, z);
                    if (!grid.TryGetValue(k, out var l)) grid[k] = l = new List<int>(4);
                    l.Add(e);
                }
            }
            var rimDist = new float[weld.Count]; for (int i = 0; i < rimDist.Length; i++) rimDist[i] = -1f;
            float RimDist(int i)
            {
                int id = wid[i]; if (rimDist[id] >= 0f) return rimDist[id];
                var x = p.v[i].p; float best = SootReach + 1f;
                int cx = Mathf.FloorToInt(x.x / cell), cy = Mathf.FloorToInt(x.y / cell), cz = Mathf.FloorToInt(x.z / cell);
                for (int gx = cx - 1; gx <= cx + 1; gx++) for (int gy = cy - 1; gy <= cy + 1; gy++) for (int gz = cz - 1; gz <= cz + 1; gz++)
                {
                    if (!grid.TryGetValue(new Vector3Int(gx, gy, gz), out var l)) continue;
                    foreach (int e in l)
                    {
                        var ab = segB[e] - segA[e]; float L2 = ab.sqrMagnitude;
                        float t = L2 > 1e-12f ? Mathf.Clamp01(Vector3.Dot(x - segA[e], ab) / L2) : 0f;
                        float d = (x - (segA[e] + ab * t)).sqrMagnitude; if (d < best * best) best = Mathf.Sqrt(d);
                    }
                }
                return rimDist[id] = best;
            }
            Color Steel(Vector3 pos, float rd, float level)
            {
                float nz = noise.Noise(pos);
                float lvl = level * Mathf.Lerp(0.82f, 1.12f, nz);
                float soot = Mathf.Lerp(SootEdge, 1f, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(rd / Mathf.Max(0.05f, SootReach))));
                var col = new Color(lvl * soot, lvl * soot * 0.98f, lvl * soot * 0.95f, 1f);
                if (rd < RimReach)
                {
                    float k = 1f - Mathf.SmoothStep(0f, 1f, rd / RimReach);
                    var tint = rd < RimReach * 0.45f ? Rust : (nz > 0.5f ? Straw : Blue);
                    col = Color.Lerp(col, new Color(col.r * tint.r, col.g * tint.g, col.b * tint.b, 1f), k);
                }
                return col;
            }
            // 3. the inside: one vertex per skin vertex, PlateM in along the welded normal, its own shading normal turned in
            var inner = new int[nv0]; for (int i = 0; i < nv0; i++) inner[i] = -1;
            int Inner(int i)
            {
                if (inner[i] >= 0) return inner[i];
                var v = p.v[i]; var pos = v.p - wn[wid[i]] * PlateM;
                var nn = v.n.sqrMagnitude > 1e-8f ? -v.n.normalized : -wn[wid[i]];
                inner[i] = p.v.Count;
                p.v.Add(new OzMeshSlice.V { p = pos, n = nn, u0 = new Vector2(Vector3.Dot(pos, tdir) * inv + off.x, Vector3.Dot(pos, bdir) * inv + off.y), u1 = v.u1, c = Steel(pos, RimDist(i), PlateLevel), t = tan });
                return inner[i];
            }
            int innerTris = 0;
            for (int s = 0; s < cs; s++)
            {
                var t = p.sub[s];
                for (int k = 0; k + 2 < t.Count; k += 3)
                {
                    int a = t[k], b = t[k + 1], c = t[k + 2];
                    if (Vector3.Cross(p.v[b].p - p.v[a].p, p.v[c].p - p.v[a].p).sqrMagnitude < 1e-14f) continue;
                    int ia = Inner(a), ib = Inner(b), ic = Inner(c);
                    cap.Add(ia); cap.Add(ic); cap.Add(ib); innerTris++;
                }
            }
            // 4. the walls along the torn edges (the plate's thickness), facing out of the plate
            int wallTris = 0; float along = 0f;
            foreach (var (a, b, c) in open)
            {
                var A = p.v[a].p; var B = p.v[b].p; var C = p.v[c].p;
                var e = B - A; float len = e.magnitude; if (len < 1e-5f) continue;
                var ed = e / len; var qv = C - A; var into = qv - ed * Vector3.Dot(qv, ed); into = into.sqrMagnitude > 1e-12f ? into.normalized : Vector3.Cross(wn[wid[a]], ed).normalized;
                var nA = wn[wid[a]]; var nB = wn[wid[b]];
                var A2 = A - nA * PlateM; var B2 = B - nB * PlateM;
                var outw = -into;
                float u0 = along * inv, u1 = (along + len) * inv, vt = PlateM * inv; along += len;
                var wt = new Vector4(ed.x, ed.y, ed.z, 1f);
                Color ca = Steel(A, 0f, WallLevel), cb = Steel(B, 0f, WallLevel);
                int w0 = p.v.Count;
                p.v.Add(new OzMeshSlice.V { p = A, n = outw, u0 = new Vector2(u0 + off.x, off.y), c = ca, t = wt });
                p.v.Add(new OzMeshSlice.V { p = B, n = outw, u0 = new Vector2(u1 + off.x, off.y), c = cb, t = wt });
                p.v.Add(new OzMeshSlice.V { p = B2, n = outw, u0 = new Vector2(u1 + off.x, vt + off.y), c = new Color(cb.r * 0.8f, cb.g * 0.8f, cb.b * 0.8f, 1f), t = wt });
                p.v.Add(new OzMeshSlice.V { p = A2, n = outw, u0 = new Vector2(u0 + off.x, vt + off.y), c = new Color(ca.r * 0.8f, ca.g * 0.8f, ca.b * 0.8f, 1f), t = wt });
                bool faces = Vector3.Dot(Vector3.Cross(B - A, B2 - A), outw) > 0f;               // a front face: cross(b - a, c - a) points out
                if (faces) { cap.Add(w0); cap.Add(w0 + 1); cap.Add(w0 + 2); cap.Add(w0); cap.Add(w0 + 2); cap.Add(w0 + 3); }
                else { cap.Add(w0); cap.Add(w0 + 2); cap.Add(w0 + 1); cap.Add(w0); cap.Add(w0 + 3); cap.Add(w0 + 2); }
                wallTris += 2;
                edges.Add(new TornEdge { a = A, b = B, into = into, n = (nA + nB).sqrMagnitude > 1e-8f ? (nA + nB).normalized : nA });
            }
            Plates++; PlateInnerTris += innerTris; PlateWallTris += wallTris; PlateEdges += edges.Count;
            return edges;
        }

        // ------------------------------------------------------------------ the domes' shard cut
        /// <summary>A relief cut of a closed part (the dome halves' shard): the cannon's own Carve with its relief caps, tilt on; the cannon's
        /// cap counters are put back (its report stays its own). A failed relief cut is the plain capped cut (counted).</summary>
        public static bool ReliefQuiet(OzMeshSlice.Part src, Vector3 n, float w, System.Random rnd, out OzMeshSlice.Part front, out OzMeshSlice.Part back)
        {
            front = null; back = null;
            if (src == null) return false;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            float tiltDeg = R(rnd, TiltMin, TiltMax), tiltAng = R(rnd, 0f, Mathf.PI * 2f);
            int seedF = rnd.Next(), seedB = rnd.Next();
            float rotF = R(rnd, 0f, Mathf.PI * 2f), rotB = R(rnd, 0f, Mathf.PI * 2f);
            var offF = new Vector2(R(rnd, 0f, 1f), R(rnd, 0f, 1f)); var offB = new Vector2(R(rnd, 0f, 1f), R(rnd, 0f, 1f));
            int caps0 = Caps, tris0 = CapTrisMade; float deep0 = Deepest;
            bool ok;
            try { ok = Carve(src, n, w, 1f, tiltDeg, tiltAng, seedF, seedB, rotF, rotB, offF, offB, out front, out back, true, true); }
            catch (System.Exception e) { ok = false; ShellError = e.GetType().Name + ": " + e.Message; }
            finally { int dc = Caps - caps0, dt = CapTrisMade - tris0; if (dc > 0) Caps -= dc; if (dt > 0) CapTrisMade -= dt; if (Deepest > deep0) Deepest = deep0; }
            float ms = (float)clock.Elapsed.TotalMilliseconds; if (ms > ShellMaxMs) ShellMaxMs = ms;
            if (ok && front != null && back != null) { ReliefCuts++; return true; }
            front = null; back = null;
            if (!OzMeshSlice.Split(src, n, w, out front, out back)) return false;
            ReliefPlain++; if (ShellError == "-") ShellError = "a dome's relief cut left one side empty: the plain cut";
            return true;
        }
    }
}
