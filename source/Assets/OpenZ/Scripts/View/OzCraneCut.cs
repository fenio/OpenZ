using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// K342b: the clean split of the repair station's body mesh into the crane gantry that moves for the shuttle and the station that
    /// stays (View/OzStationCrane.cs calls it when a shuttle calls the crane away). The model is one welded shell, so the gantry is cut off
    /// along a few flat cuts in the station frame (metres from the model's middle, x east, z north, y up from its base) placed on the
    /// model's own joints:
    ///  * the beam's west end block off the west tower's cream top (y 5.05, 1 cm over the top's highest point), and where the beam's
    ///    underside runs 10 cm lower over the tower's east edge, a short upright cut (x -3.015) that meets it along a fold;
    ///  * the dark strut under the beam just above its collar on the tower (y 4.70);
    ///  * the east post's foot with the control box and the cabinets on it just above the deck (y 0.94).
    /// Every triangle a cut's plane crosses (inside the cut's region) is split exactly along it, the new points shared by the triangles
    /// on both sides (no cracks). The gantry = the connected pieces holding the beam (the beam's top is the seed): nothing of the gantry
    /// is left floating and nothing of the station rides along. Every cut between the two is closed on each side by a flat cap (two cuts
    /// meeting at a fold are closed along the fold line), painted with one texel of the station's own atlas: the tower's cream, the dark
    /// steel, the beam's paint (team colour), the deck's own cream: the tower's new top is cream like the tower, the deck where the post
    /// stood is in the deck's own colour (whole, not a dark patch that reads as a hole), the strut's stub dark like the strut, the beam's
    /// and the post's own new ends in their paint. The cuts sit 1 .. 8 cm above the joints (the deck and the tower's top are not flat),
    /// so the station keeps that thin strip of the gantry's walls (the boxes' dark and red, the beam's red) around each cap: those
    /// strips (the rims) are drawn in their cap's paint, so the seat and the socket read as one closed piece of the tower and the deck
    /// (no red, white or dark outline). Plain arithmetic on arrays, no engine calls: the kit runs this same file off the engine
    /// for its checks.
    /// </summary>
    public static class OzCraneCut
    {
        public const int PaintTeam = 0, PaintCream = 1, PaintDark = 2, PaintDeck = 3;
        /// <summary>The rims: the station's triangles at most RimBand under a cut, inside its window, steeper than RimSteep against its
        /// plane (walls, not the tower's top or the deck), joined to the cut.</summary>
        public const float RimBand = 0.10f, RimSteep = 0.7f;

        public struct Cut
        {
            public string name; public int axis; public float val;
            /// <summary>Window (the two other axes, in the order x, y, z without the cut's axis): where the cut separates the two parts.</summary>
            public Vector2 winLo, winHi;
            /// <summary>Region (same axes): the triangles crossing the plane there are split.</summary>
            public Vector2 regLo, regHi;
            /// <summary>The station's side of the plane (+1: above val, -1: below) and the two caps' paints.</summary>
            public int restSide, restPaint, partPaint;
        }

        public static readonly Cut[] Cuts =
        {
            new Cut { name = "the tower's top", axis = 1, val = 5.05f, winLo = new Vector2(-4.75f, 0.60f), winHi = new Vector2(-3.015f, 3.00f),
                      regLo = new Vector2(-4.90f, 0.30f), regHi = new Vector2(4.90f, 3.10f), restSide = -1, restPaint = PaintCream, partPaint = PaintTeam },
            new Cut { name = "the beam's underside at the tower's east edge", axis = 0, val = -3.015f, winLo = new Vector2(4.90f, 0.60f), winHi = new Vector2(5.05f, 3.00f),
                      regLo = new Vector2(4.80f, 0.80f), regHi = new Vector2(6.30f, 2.60f), restSide = -1, restPaint = PaintCream, partPaint = PaintTeam },
            new Cut { name = "the strut over its collar", axis = 1, val = 4.70f, winLo = new Vector2(-2.90f, 1.90f), winHi = new Vector2(-2.40f, 2.40f),
                      regLo = new Vector2(-2.95f, 1.85f), regHi = new Vector2(-2.35f, 2.45f), restSide = -1, restPaint = PaintDark, partPaint = PaintDark },
            new Cut { name = "the post's foot", axis = 1, val = 0.94f, winLo = new Vector2(2.40f, 0.50f), winHi = new Vector2(4.90f, 2.95f),
                      regLo = new Vector2(2.45f, 0.60f), regHi = new Vector2(4.90f, 2.90f), restSide = -1, restPaint = PaintDeck, partPaint = PaintDark },
        };

        /// <summary>The gantry's seed: triangles of the beam's top (their middle).</summary>
        public static bool Seed(Vector3 c) => c.y > 5.6f && c.z > 1.0f && c.z < 2.6f && c.x > -2.9f && c.x < 2.8f;

        public sealed class Result
        {
            public bool ok; public string log = "", timing = "";
            /// <summary>Frame points: the mesh's own (n0), then the cuts' new points, then the caps' points.</summary>
            public List<Vector3> P; public int n0;
            /// <summary>For each point from n0 on: made from points srcA and srcB at srcT (a cut's point); a cap's point copies srcA's
            /// place and gets the cap's own normal (capAxis, capSign) and paint (capPaint); capAxis -1 = a cut's point; capAxis -2 = a
            /// rim's point: srcA's place and normal, the paint capPaint.</summary>
            public List<int> srcA = new List<int>(), srcB = new List<int>(), capAxis = new List<int>(), capSign = new List<int>(), capPaint = new List<int>();
            public List<float> srcT = new List<float>();
            /// <summary>The station's triangles (rest) and the gantry's (part) per submesh, caps included (in submesh 0).</summary>
            public List<int>[] rest, part;
            public int trisIn, trisOut, splitTris, borderTris, gantryTris, capTris, capPolys, chainFails, clipFails, openBefore, openAfter, rimTris;
        }

        /// <summary>+1 when the mesh's triangles face Cross(b - a, c - a) in the frame (their own normals, in the frame, agree), -1 if not.</summary>
        public static float Winding(Vector3[] F, List<int>[] tris, Vector3[] frameNormals)
        {
            double sum = 0;
            if (frameNormals == null || frameNormals.Length != F.Length) return 1f;
            foreach (var l in tris)
                for (int i = 0; i + 2 < l.Count; i += 3)
                {
                    var n = Vector3.Cross(F[l[i + 1]] - F[l[i]], F[l[i + 2]] - F[l[i]]);
                    sum += Vector3.Dot(n, frameNormals[l[i]] + frameNormals[l[i + 1]] + frameNormals[l[i + 2]]);
                }
            return sum >= 0 ? 1f : -1f;
        }

        const float Eps = 1e-6f;
        /// <summary>A hash that spreads packed keys (the default one folds the two halves of a long together: many collisions here).</summary>
        sealed class LongHash : IEqualityComparer<long>
        {
            public bool Equals(long a, long b) => a == b;
            public int GetHashCode(long x) => (int)(((ulong)x * 0x9E3779B97F4A7C15UL) >> 32);
        }
        static readonly LongHash Hash = new LongHash();
        static float Ax(Vector3 p, int a) => a == 0 ? p.x : a == 1 ? p.y : p.z;
        static Vector3 SetAx(Vector3 p, int a, float v) { if (a == 0) p.x = v; else if (a == 1) p.y = v; else p.z = v; return p; }
        static void Other(int a, out int o1, out int o2) { o1 = a == 0 ? 1 : 0; o2 = a == 2 ? 1 : 2; }
        static bool Less(Vector3 a, Vector3 b) => a.x != b.x ? a.x < b.x : a.y != b.y ? a.y < b.y : a.z < b.z;

        /// <summary>Cuts the mesh (frame points F, triangles per submesh), sorts its pieces into the station and the gantry and caps the
        /// cuts between them. sigma: +1 when a triangle's front side is Cross(b - a, c - a) in the frame (Unity's rule), -1 if mirrored.</summary>
        public static Result Run(Vector3[] F, List<int>[] tris, float sigma)
        {
            var r = new Result { P = new List<Vector3>(F), n0 = F.Length };
            var P = r.P; int subs = tris.Length;
            var T = new List<int>[subs];
            for (int k = 0; k < subs; k++) { T[k] = new List<int>(tris[k]); r.trisIn += tris[k].Count / 3; }
            var clock = System.Diagnostics.Stopwatch.StartNew(); var ms = new double[5];
            r.openBefore = OpenEdges(P, T, null, null, false);
            ms[0] = clock.Elapsed.TotalMilliseconds;

            // ---- 1. split along every cut's plane (inside its region)
            var s = new int[3]; var poly = new List<int>(4); var above = new List<int>(4); var below = new List<int>(4);
            for (int ci = 0; ci < Cuts.Length; ci++)
            {
                var c = Cuts[ci]; Other(c.axis, out int o1, out int o2);
                var cache = new Dictionary<long, int>(Hash);
                int Cross(int a, int b)
                {
                    if (Less(P[b], P[a])) { int x = a; a = b; b = x; }                                  // by place: twins on a UV seam get the same point
                    long key = ((long)a << 32) | (uint)b;
                    if (cache.TryGetValue(key, out int v)) return v;
                    Vector3 pa = P[a], pb = P[b]; float t = (c.val - Ax(pa, c.axis)) / (Ax(pb, c.axis) - Ax(pa, c.axis));
                    var p = SetAx(pa + (pb - pa) * t, c.axis, c.val);
                    P.Add(p); r.srcA.Add(a); r.srcB.Add(b); r.srcT.Add(t); r.capAxis.Add(-1); r.capSign.Add(0); r.capPaint.Add(0);
                    v = P.Count - 1; cache[key] = v; return v;
                }
                for (int k = 0; k < subs; k++)
                {
                    var src = T[k]; var dst = new List<int>(src.Count + 96);
                    for (int i = 0; i + 2 < src.Count; i += 3)
                    {
                        int a = src[i], b = src[i + 1], cc = src[i + 2];
                        int pos = 0, neg = 0;
                        for (int q = 0; q < 3; q++)
                        {
                            float d = Ax(P[src[i + q]], c.axis) - c.val;
                            s[q] = Mathf.Abs(d) <= Eps ? 0 : d > 0f ? 1 : -1; if (s[q] > 0) pos++; else if (s[q] < 0) neg++;
                        }
                        if (pos == 0 || neg == 0) { dst.Add(a); dst.Add(b); dst.Add(cc); continue; }
                        // the crossing segment's ends inside the region?
                        int inR = 0, ends = 0;
                        for (int q = 0; q < 3; q++)
                        {
                            int va = src[i + q], vb = src[i + (q + 1) % 3]; Vector3 e;
                            if (s[q] == 0) e = P[va];
                            else if (s[q] * s[(q + 1) % 3] < 0) { var pa = P[va]; var pb = P[vb]; float t = (c.val - Ax(pa, c.axis)) / (Ax(pb, c.axis) - Ax(pa, c.axis)); e = pa + (pb - pa) * t; }
                            else continue;
                            ends++;
                            float u = Ax(e, o1), w = Ax(e, o2);
                            if (u >= c.regLo.x && u <= c.regHi.x && w >= c.regLo.y && w <= c.regHi.y) inR++;
                        }
                        if (inR == 0) { dst.Add(a); dst.Add(b); dst.Add(cc); continue; }
                        if (inR < ends) r.borderTris++;
                        r.splitTris++;
                        above.Clear(); below.Clear();
                        for (int q = 0; q < 3; q++)
                        {
                            int va = src[i + q], vb = src[i + (q + 1) % 3];
                            if (s[q] >= 0) above.Add(va);
                            if (s[q] <= 0) below.Add(va);
                            if (s[q] * s[(q + 1) % 3] < 0) { int m = Cross(va, vb); above.Add(m); below.Add(m); }
                        }
                        Emit(P, above, dst); Emit(P, below, dst);
                    }
                    T[k] = dst;
                }
            }

            ms[1] = clock.Elapsed.TotalMilliseconds;
            // ---- 2. the pieces: connected across every edge that is not on a cut (welded by place), the gantry = the beam's pieces
            int nT = 0; for (int k = 0; k < subs; k++) nT += T[k].Count / 3;
            var W = Weld(P);
            var parent = new int[nT]; for (int i = 0; i < nT; i++) parent[i] = i;
            int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
            var first = new Dictionary<long, int>(nT * 2, Hash);
            int gi = 0;
            for (int k = 0; k < subs; k++)
                for (int i = 0; i + 2 < T[k].Count; i += 3, gi++)
                    for (int q = 0; q < 3; q++)
                    {
                        int a = T[k][i + q], b = T[k][i + (q + 1) % 3];
                        if (CutOf(P, a, b) >= 0) continue;
                        long key = EdgeKey(W[a], W[b]); if (key < 0) continue;
                        if (first.TryGetValue(key, out int o)) { int ra = Find(gi), rb = Find(o); if (ra != rb) parent[ra] = rb; }
                        else first[key] = gi;
                    }
            var seedRoots = new HashSet<int>();
            gi = 0;
            for (int k = 0; k < subs; k++)
                for (int i = 0; i + 2 < T[k].Count; i += 3, gi++)
                    if (Seed((P[T[k][i]] + P[T[k][i + 1]] + P[T[k][i + 2]]) / 3f)) seedRoots.Add(Find(gi));
            r.rest = new List<int>[subs]; r.part = new List<int>[subs];
            var isG = new bool[nT];
            gi = 0;
            for (int k = 0; k < subs; k++)
            {
                r.rest[k] = new List<int>(); r.part[k] = new List<int>();
                for (int i = 0; i + 2 < T[k].Count; i += 3, gi++)
                {
                    bool g = seedRoots.Count > 0 && seedRoots.Contains(Find(gi)); isG[gi] = g;
                    var l = g ? r.part[k] : r.rest[k]; l.Add(T[k][i]); l.Add(T[k][i + 1]); l.Add(T[k][i + 2]);
                    if (g) r.gantryTris++;
                }
            }
            r.trisOut = nT;

            ms[2] = clock.Elapsed.TotalMilliseconds;
            int capP0 = P.Count;                                                                          // the caps' (and the rims') points from here on
            // ---- 3. the caps: each part's open edges on the cuts chained into loops, a loop over a fold split into one polygon per cut
            for (int side = 0; side < 2; side++)
            {
                var mine = side == 0 ? r.rest : r.part;
                var open = new List<int>(); OpenEdges(P, mine, W, open, true);
                // cap edges run the open edges backwards (the cap closes the part's surface)
                var byStart = new Dictionary<int, List<int>>();                                          // welded start -> edge slots
                for (int e = 0; e < open.Count; e += 2)
                {
                    int a = open[e], b = open[e + 1];
                    if (CutOf(P, a, b) < 0) continue;
                    if (!byStart.TryGetValue(W[b], out var lst)) byStart[W[b]] = lst = new List<int>();
                    lst.Add(e);
                }
                var used = new HashSet<int>();
                foreach (var kv in byStart)
                    foreach (int e0 in kv.Value)
                    {
                        if (used.Contains(e0)) continue;
                        var loop = new List<int>(); int e = e0; int start = W[open[e0 + 1]]; bool closed = false;
                        for (int guard = 0; guard < 100000; guard++)
                        {
                            used.Add(e); int from = open[e + 1], to = open[e]; loop.Add(from);
                            if (W[to] == start) { closed = true; break; }
                            if (!byStart.TryGetValue(W[to], out var nx)) break;
                            int pick = -1; foreach (int x in nx) if (!used.Contains(x)) { pick = x; break; }
                            if (pick < 0) break;
                            e = pick;
                        }
                        if (!closed) { r.chainFails++; continue; }
                        int n = loop.Count; var ec = new int[n];
                        for (int i = 0; i < n; i++) ec[i] = CutOf(P, loop[i], loop[(i + 1) % n]);
                        int i0 = -1; for (int i = 0; i < n; i++) if (ec[i] != ec[(i + n - 1) % n]) { i0 = i; break; }
                        if (i0 < 0) { Cap(r, loop, ec[0], side, sigma); continue; }
                        // runs of one cut, each closed along the fold (from its last point straight back to its first)
                        int runs = 0; var run = new List<int> { loop[i0] }; int cur = ec[i0];
                        for (int q = 1; q <= n; q++)
                        {
                            int i = (i0 + q) % n; run.Add(loop[i]);
                            if (q == n || ec[i] != cur) { Cap(r, run, cur, side, sigma); runs++; cur = ec[i]; run = new List<int> { loop[i] }; }
                        }
                        if (runs != 2) r.chainFails++;                                                // only one fold crossing in and out is built for
                    }
            }

            // ---- 3b. the rims (the thin strips of the gantry's walls the station keeps under a cut) in their cap's paint
            r.rimTris = Rims(r, capP0);
            ms[3] = clock.Elapsed.TotalMilliseconds;
            // ---- 4. what is left open (the model's own few open edges; a cut left open would be a failure)
            var W2 = Weld(P);                                                                             // the caps' points too
            r.openAfter = OpenEdges(P, r.rest, W2, null, false) + OpenEdges(P, r.part, W2, null, false);
            ms[4] = clock.Elapsed.TotalMilliseconds;
            r.timing = $"open edges {ms[0]:F0} ms, cuts {ms[1] - ms[0]:F0}, pieces {ms[2] - ms[1]:F0}, caps {ms[3] - ms[2]:F0}, check {ms[4] - ms[3]:F0}";
            r.ok = r.chainFails == 0 && r.clipFails == 0 && r.borderTris == 0 && r.gantryTris > 0 && r.openAfter <= r.openBefore;
            r.log = $"{r.splitTris} triangles cut along {Cuts.Length} planes, {r.trisIn} -> {r.trisOut}; gantry {r.gantryTris}; caps {r.capPolys} ({r.capTris} triangles), rims {r.rimTris} triangles; " +
                    $"open edges {r.openBefore} in the model, {r.openAfter} after the cut; failed chains {r.chainFails}, caps {r.clipFails}, region borders {r.borderTris}";
            return r;
        }

        /// <summary>The rims: for each cut, the station's triangles under it (at most RimBand, inside its window, walls against its
        /// plane) that touch the cut or join one that does, get new points (the same places and normals) in the cut's station paint.
        /// Returns how many triangles were repainted.</summary>
        static int Rims(Result r, int capP0)
        {
            var P = r.P; var W = Weld(P); int n = 0;
            var copies = new Dictionary<long, int>(Hash);
            for (int ci = 0; ci < Cuts.Length; ci++)
            {
                var c = Cuts[ci]; Other(c.axis, out int o1, out int o2);
                float Depth(Vector3 p) => (Ax(p, c.axis) - c.val) * c.restSide;                             // > 0 on the station's side
                var cand = new List<int>(); var isSeed = new List<bool>();                                // packed (submesh << 24 | triangle start)
                for (int k = 0; k < r.rest.Length; k++)
                {
                    var l = r.rest[k];
                    for (int i = 0; i + 2 < l.Count; i += 3)
                    {
                        int a = l[i], b = l[i + 1], cc = l[i + 2];
                        if (a >= capP0 || b >= capP0 || cc >= capP0) continue;                            // a cap, or a rim already
                        Vector3 pa = P[a], pb = P[b], pc = P[cc];
                        float da = Depth(pa), db = Depth(pb), dc = Depth(pc);
                        if (da < -Eps || db < -Eps || dc < -Eps || da > RimBand || db > RimBand || dc > RimBand) continue;
                        var m = (pa + pb + pc) / 3f; float u = Ax(m, o1), w = Ax(m, o2);
                        if (u < c.winLo.x - 0.02f || u > c.winHi.x + 0.02f || w < c.winLo.y - 0.02f || w > c.winHi.y + 0.02f) continue;
                        var nn = Vector3.Cross(pb - pa, pc - pa); float len = nn.magnitude;
                        if (len < 1e-12f || Mathf.Abs(Ax(nn, c.axis)) > RimSteep * len) continue;           // the tower's top, the deck: not a rim
                        cand.Add((k << 24) | i);
                        isSeed.Add(Mathf.Abs(da) <= Eps || Mathf.Abs(db) <= Eps || Mathf.Abs(dc) <= Eps);
                    }
                }
                if (cand.Count == 0) continue;
                // joined through welded edges, from the triangles that touch the cut
                var byEdge = new Dictionary<long, List<int>>(Hash);
                for (int q = 0; q < cand.Count; q++)
                {
                    int k = cand[q] >> 24, i = cand[q] & 0xffffff; var l = r.rest[k];
                    for (int e = 0; e < 3; e++)
                    {
                        long key = EdgeKey(W[l[i + e]], W[l[i + (e + 1) % 3]]); if (key < 0) continue;
                        if (!byEdge.TryGetValue(key, out var lst)) byEdge[key] = lst = new List<int>();
                        lst.Add(q);
                    }
                }
                var take = new bool[cand.Count]; var stack = new Stack<int>();
                for (int q = 0; q < cand.Count; q++) if (isSeed[q]) { take[q] = true; stack.Push(q); }
                while (stack.Count > 0)
                {
                    int q = stack.Pop(); int k = cand[q] >> 24, i = cand[q] & 0xffffff; var l = r.rest[k];
                    for (int e = 0; e < 3; e++)
                    {
                        long key = EdgeKey(W[l[i + e]], W[l[i + (e + 1) % 3]]); if (key < 0 || !byEdge.TryGetValue(key, out var lst)) continue;
                        foreach (int x in lst) if (!take[x]) { take[x] = true; stack.Push(x); }
                    }
                }
                int Copy(int v)
                {
                    long key = ((long)v << 3) | (uint)c.restPaint;
                    if (copies.TryGetValue(key, out int x)) return x;
                    P.Add(P[v]); r.srcA.Add(v); r.srcB.Add(-1); r.srcT.Add(0f); r.capAxis.Add(-2); r.capSign.Add(0); r.capPaint.Add(c.restPaint);
                    x = P.Count - 1; copies[key] = x; return x;
                }
                for (int q = 0; q < cand.Count; q++)
                {
                    if (!take[q]) continue;
                    int k = cand[q] >> 24, i = cand[q] & 0xffffff; var l = r.rest[k];
                    l[i] = Copy(l[i]); l[i + 1] = Copy(l[i + 1]); l[i + 2] = Copy(l[i + 2]); n++;
                }
            }
            return n;
        }

        /// <summary>One cap: the loop's places copied as new points with the cap's own normal and paint, ear-clipped flat.</summary>
        static void Cap(Result r, List<int> loop, int ci, int side, float sigma)
        {
            if (ci < 0 || loop.Count < 3) { r.clipFails++; return; }
            var c = Cuts[ci]; int sign = side == 0 ? -c.restSide : c.restSide;                         // the cap faces away from its part
            int paint = side == 0 ? c.restPaint : c.partPaint;
            var pts = new List<Vector3>(loop.Count); foreach (int v in loop) pts.Add(r.P[v]);
            var tri = EarClip(pts, c.axis, sign * sigma);
            if (tri == null) { r.clipFails++; return; }
            int b0 = r.P.Count;
            for (int i = 0; i < loop.Count; i++)
            {
                r.P.Add(r.P[loop[i]]); r.srcA.Add(loop[i]); r.srcB.Add(-1); r.srcT.Add(0f); r.capAxis.Add(c.axis); r.capSign.Add(sign); r.capPaint.Add(paint);
            }
            var dst = side == 0 ? r.rest[0] : r.part[0];
            for (int i = 0; i < tri.Count; i++) dst.Add(b0 + tri[i]);
            r.capTris += tri.Count / 3; r.capPolys++;
        }

        /// <summary>A flat polygon (points in order, in the plane of the given axis) cut into triangles whose Cross(b - a, c - a) points
        /// along +axis * dir. Points on a straight run stay corners of the triangles around them (no T-junctions); null if it fails.</summary>
        public static List<int> EarClip(List<Vector3> pts, int axis, float dir)
        {
            Other(axis, out int o1, out int o2);
            float cyc = axis == 1 ? -1f : 1f, sg = cyc * (dir > 0f ? 1f : -1f);                       // (u, w, axis) cyclic: x->(y,z), z->(x,y)
            int n = pts.Count; var U = new float[n]; var V = new float[n];
            for (int i = 0; i < n; i++) { U[i] = Ax(pts[i], o1); V[i] = Ax(pts[i], o2); }
            double Ar(int i, int j, int k) => ((double)(U[j] - U[i]) * (V[k] - V[i]) - (double)(V[j] - V[i]) * (U[k] - U[i])) * sg;
            var idx = new List<int>(n); for (int i = 0; i < n; i++) idx.Add(i);
            double tot = 0; for (int i = 1; i + 1 < n; i++) tot += Ar(0, i, i + 1);
            if (tot < 0) idx.Reverse();
            var outT = new List<int>((n - 2) * 3);
            while (idx.Count > 3)
            {
                bool done = false;
                for (int k = 0; k < idx.Count; k++)
                {
                    int i = idx[(k + idx.Count - 1) % idx.Count], j = idx[k], l = idx[(k + 1) % idx.Count];
                    if (Ar(i, j, l) <= 1e-14) continue;
                    bool bad = false;
                    foreach (int m in idx)
                    {
                        if (m == i || m == j || m == l) continue;
                        if (Ar(i, j, m) >= -1e-14 && Ar(j, l, m) >= -1e-14 && Ar(l, i, m) >= -1e-14) { bad = true; break; }
                    }
                    if (bad) continue;
                    outT.Add(i); outT.Add(j); outT.Add(l); idx.RemoveAt(k); done = true; break;
                }
                if (!done) return null;
            }
            if (Ar(idx[0], idx[1], idx[2]) > 1e-14) { outT.Add(idx[0]); outT.Add(idx[1]); outT.Add(idx[2]); }
            return outT;
        }

        /// <summary>A cut piece (3 or 4 points, the triangle's own turn) as triangles; no sliver of zero size.</summary>
        static void Emit(List<Vector3> P, List<int> p, List<int> dst)
        {
            if (p.Count == 3) { if (Area(P, p[0], p[1], p[2]) > 1e-14f) { dst.Add(p[0]); dst.Add(p[1]); dst.Add(p[2]); } return; }
            if (p.Count != 4) return;
            bool d02 = Area(P, p[0], p[1], p[2]) > 1e-14f && Area(P, p[0], p[2], p[3]) > 1e-14f;
            if (d02) { dst.Add(p[0]); dst.Add(p[1]); dst.Add(p[2]); dst.Add(p[0]); dst.Add(p[2]); dst.Add(p[3]); }
            else { dst.Add(p[1]); dst.Add(p[2]); dst.Add(p[3]); dst.Add(p[1]); dst.Add(p[3]); dst.Add(p[0]); }
        }

        static float Area(List<Vector3> P, int a, int b, int c) => Vector3.Cross(P[b] - P[a], P[c] - P[a]).sqrMagnitude;

        /// <summary>The cut whose plane holds edge a-b inside its window, else -1.</summary>
        static int CutOf(List<Vector3> P, int a, int b)
        {
            for (int ci = 0; ci < Cuts.Length; ci++)
            {
                var c = Cuts[ci];
                if (Mathf.Abs(Ax(P[a], c.axis) - c.val) > Eps || Mathf.Abs(Ax(P[b], c.axis) - c.val) > Eps) continue;
                Other(c.axis, out int o1, out int o2); var m = (P[a] + P[b]) * 0.5f; float u = Ax(m, o1), w = Ax(m, o2);
                if (u >= c.winLo.x - Eps && u <= c.winHi.x + Eps && w >= c.winLo.y - Eps && w <= c.winHi.y + Eps) return ci;
            }
            return -1;
        }

        /// <summary>Places welded at 0.1 mm: the same id for points at the same place.</summary>
        static int[] Weld(List<Vector3> P)
        {
            var map = new Dictionary<long, int>(P.Count, Hash); var w = new int[P.Count];
            for (int i = 0; i < P.Count; i++)
            {
                var p = P[i];
                long key = ((long)(Mathf.RoundToInt(p.x * 1e4f) + 1048576) << 42) | ((long)(Mathf.RoundToInt(p.y * 1e4f) + 1048576) << 21) | (long)(Mathf.RoundToInt(p.z * 1e4f) + 1048576);
                if (!map.TryGetValue(key, out int id)) { id = map.Count; map[key] = id; }
                w[i] = id;
            }
            return w;
        }

        static long EdgeKey(int a, int b) => a == b ? -1 : a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;

        /// <summary>Edges used by one triangle only (welded). With collect: their directed (a, b) pairs (as the triangle runs them) into
        /// `outEdges`; only the edges NOT on a cut are counted when collect is false.</summary>
        static int OpenEdges(List<Vector3> P, List<int>[] T, int[] W, List<int> outEdges, bool collect)
        {
            if (W == null) W = Weld(P);
            var cnt = new Dictionary<long, int>(Hash); var dir = new Dictionary<long, long>(Hash);
            foreach (var l in T)
                for (int i = 0; i + 2 < l.Count; i += 3)
                    for (int q = 0; q < 3; q++)
                    {
                        int a = l[i + q], b = l[i + (q + 1) % 3]; long key = EdgeKey(W[a], W[b]); if (key < 0) continue;
                        cnt.TryGetValue(key, out int n); cnt[key] = n + 1;
                        if (n == 0) dir[key] = ((long)a << 32) | (uint)b;
                    }
            int open = 0;
            foreach (var kv in cnt)
            {
                if (kv.Value != 1) continue;
                long d = dir[kv.Key]; int a = (int)(d >> 32), b = (int)(d & 0xffffffffL);
                if (collect) { outEdges.Add(a); outEdges.Add(b); open++; }
                else if (CutOf(P, a, b) < 0) open++;
            }
            return open;
        }
    }
}
