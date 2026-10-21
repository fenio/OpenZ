using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Version 1.1, item 375: the fort cannon's pieces break like real broken metal, not like clean boxes. The cuts of
    /// <see cref="OzMeshSlice.Split"/> are straight planes closed by one flat convex cap; a cut made here instead:
    /// <list type="bullet">
    /// <item>follows a seeded fracture surface: the plane (tilted a few degrees, so no piece is a rectangular slab) minus a height field
    /// h(u, v) >= 0 of long bends (1.5-2.5 m), a mid layer (0.4-0.7 m), a fine layer and small terrace steps (layered plate). Each side
    /// keeps only what lies behind the plane minus ITS OWN field, so a piece only ever loses material (its collider stays inside the
    /// plain cut's piece, apart from the small tilt); the depth is at most <see cref="MaxDepth"/> and never more than
    /// <see cref="ThickShare"/> of the piece's own thickness behind the plane (thin plates stay whole);</item>
    /// <item>the painted skin is subdivided near the break (edges up to <see cref="Edge"/>), clipped by that surface (linear on each
    /// edge, every vertex attribute kept) and its painted lip is bent a few centimetres inward (a torn, dented plate edge);</item>
    /// <item>the break is closed by a RELIEF cap stitched to the clipped skin's own outline (its loops; holes such as a bore stay
    /// open), so the outline is as jagged as the break and the face has thickness and depth; no single-sided teeth, nothing leaning
    /// out;</item>
    /// <item>colours for the look (the pieces' materials read them): soot and scorch on the paint near the break, bright bare metal on the
    /// ridges of the broken face, soot in its pits, rust and temper colours along its rim; the face UVs are in metres per repeat
    /// (<see cref="TileMetres"/>) with a random turn and offset per cut, so no two faces line up and none shows a grid.</item>
    /// </list>
    /// Mesh space throughout (the pieces keep their source's object space). View only. If anything goes wrong the plain cut is used.
    /// </summary>
    public static partial class OzFractureCut
    {
        // ------------------------------------------------------------------ tuning (metres, degrees)
        public static float LongWaveMin = 1.2f, LongWaveMax = 2.2f, LongAmpMin = 0.25f, LongAmpMax = 0.45f;
        public static float MidWaveMin = 0.45f, MidWaveMax = 0.8f, MidAmpMin = 0.10f, MidAmpMax = 0.18f;
        public static float FineWaveMin = 0.16f, FineWaveMax = 0.26f, FineAmp = 0.035f;
        public static float StepMin = 0.04f, StepMax = 0.08f, StepMix = 0.6f;
        /// <summary>The deepest a break eats into a piece (m), and its share of the piece's thickness behind the plane.</summary>
        public static float MaxDepth = 0.45f, ThickShare = 0.35f;
        /// <summary>The plane's tilt (degrees) and its limit: the tilt moves the break across the section by at most this share of the
        /// thinner side's thickness.</summary>
        public static float TiltMin = 6f, TiltMax = 14f, TiltThickShare = 0.25f;
        /// <summary>Skin edges near the break, broken-face edges, the reach of the bent lip past the break, the subdivision past the deepest carve (m).</summary>
        public static float Edge = 0.38f, CapEdge = 0.34f, Reach = 0.25f, Margin = 0.06f;
        public static float LipMin = 0.02f, LipMax = 0.06f;
        /// <summary>The paint at the break (x, linear), back to x1 at ScorchReach (m).</summary>
        public static float ScorchMin = 0.5f, ScorchMax = 0.75f, ScorchReach = 0.45f;
        /// <summary>The broken face: ridges (x), pits (x), rust / temper band along its rim (m).</summary>
        public static float RidgeLight = 1.45f, PitDark = 0.6f, RimReach = 0.14f;
        public static readonly Color Rust = new Color(1.1f, 0.85f, 0.7f, 1f), Straw = new Color(1.08f, 0.98f, 0.74f, 1f), Blue = new Color(0.86f, 0.88f, 1.05f, 1f);
        /// <summary>Metres per repeat of the broken-face texture; the closed hidden sides' shade.</summary>
        public static float TileMetres = 3.0f, HiddenShade = 0.6f;

        // ------------------------------------------------------------------ proof
        public static int Cuts, Fallbacks, Caps, CapTrisMade, Closures;
        public static float MaxMs, LastMs, Deepest, MsRefine, MsClip, MsLip, MsCap;
        public static string LastError = "-";
        public static string State => $"fracture cuts {Cuts} (plain fallbacks {Fallbacks}{(Fallbacks > 0 ? ": " + LastError : "")}), closures {Closures}, relief caps {Caps}, relief triangles {CapTrisMade}, deepest {Deepest * 100f:0} cm, slowest cut {MaxMs:0.0} ms";

        static float R(System.Random r, float a, float b) => a + (float)r.NextDouble() * (b - a);

        /// <summary>A seeded fracture height field over a plane (mesh space axes a1, a2): h in metres, 0 .. amp.</summary>
        sealed class Field
        {
            readonly Vector3 a1, a2; readonly float sc, amp, step, lipA, warpK, warpPh, warpA;
            readonly float[] kx = new float[7], ky = new float[7], ph = new float[7], am = new float[7];
            public float Amp => amp;
            public float LipA => lipA;

            /// <summary>Always the same number of draws (7 x 4 + 5), whatever the plane.</summary>
            public Field(System.Random rnd, Vector3 a1, Vector3 a2, float sc, float ampMax)
            {
                this.a1 = a1; this.a2 = a2; this.sc = sc;
                float sum = 0f;
                for (int i = 0; i < 7; i++)
                {
                    float lam = i < 2 ? R(rnd, LongWaveMin, LongWaveMax) : i < 5 ? R(rnd, MidWaveMin, MidWaveMax) : R(rnd, FineWaveMin, FineWaveMax);
                    float dir = R(rnd, 0f, Mathf.PI * 2f), phase = R(rnd, 0f, Mathf.PI * 2f);
                    float a = i < 2 ? R(rnd, LongAmpMin, LongAmpMax) * 0.5f : i < 5 ? R(rnd, MidAmpMin, MidAmpMax) / 3f : FineAmp * 0.5f * R(rnd, 0.7f, 1.3f);
                    float k = Mathf.PI * 2f / lam;
                    kx[i] = Mathf.Cos(dir) * k; ky[i] = Mathf.Sin(dir) * k; ph[i] = phase; am[i] = a; sum += a;
                }
                warpK = Mathf.PI * 2f / R(rnd, 1.2f, 2.2f); warpPh = R(rnd, 0f, Mathf.PI * 2f); warpA = R(rnd, 0.15f, 0.35f);
                step = R(rnd, StepMin, StepMax); lipA = R(rnd, LipMin, LipMax);
                amp = Mathf.Max(0f, ampMax);
                float k2 = sum > 1e-6f ? Mathf.Min(1f, amp / (2f * sum)) : 0f;                         // h = sum a (1 + sin) spans 0 .. 2 sum
                for (int i = 0; i < 7; i++) am[i] *= k2;
            }

            void UV(Vector3 p, out float u, out float v)
            {
                u = Vector3.Dot(p, a1) * sc; v = Vector3.Dot(p, a2) * sc;
                float wu = warpA * Mathf.Sin(v * warpK + warpPh), wv = warpA * Mathf.Sin(u * warpK * 0.8f + warpPh * 1.3f);
                u += wu; v += wv;
            }

            /// <summary>The carve depth (m) at mesh-space point p (its projection on the plane).</summary>
            public float H(Vector3 p)
            {
                if (amp <= 0f) return 0f;
                UV(p, out float u, out float v);
                float h = 0f;
                for (int i = 0; i < 7; i++) h += am[i] * (1f + Mathf.Sin(kx[i] * u + ky[i] * v + ph[i]));
                if (step > 1e-4f) h = Mathf.Lerp(h, Mathf.Floor(h / step) * step, StepMix);           // layered plate: small terrace steps
                return Mathf.Clamp(h, 0f, amp);
            }

            /// <summary>The lip bend (m) at p: the cut's own depth, varied along the edge.</summary>
            public float Lip(Vector3 p)
            {
                UV(p, out float u, out float v);
                return lipA * (0.6f + 0.4f * Mathf.Sin(kx[5] * 0.7f * u + ky[5] * 0.7f * v + ph[6]));
            }

            /// <summary>A smooth 0..1 noise for colour variation (mid waves).</summary>
            public float Noise(Vector3 p)
            {
                UV(p, out float u, out float v);
                return 0.5f + 0.25f * Mathf.Sin(kx[2] * u + ky[2] * v + ph[3]) + 0.25f * Mathf.Sin(kx[3] * 1.7f * u + ky[3] * 1.7f * v + ph[4]);
            }
        }

        /// <summary>A skin boundary edge a -> b of the clipped piece (the kept polygon's own order); ia / ib = their vertices in the piece.</summary>
        struct Seg { public Vector3 a, b; public int ia, ib; }

        static OzMeshSlice.V Lerp(in OzMeshSlice.V a, in OzMeshSlice.V b, float k) => new OzMeshSlice.V
        {
            p = Vector3.LerpUnclamped(a.p, b.p, k), n = Vector3.LerpUnclamped(a.n, b.n, k).normalized,
            u0 = Vector2.LerpUnclamped(a.u0, b.u0, k), u1 = Vector2.LerpUnclamped(a.u1, b.u1, k), c = Color.LerpUnclamped(a.c, b.c, k),
            t = new Vector4(Mathf.LerpUnclamped(a.t.x, b.t.x, k), Mathf.LerpUnclamped(a.t.y, b.t.y, k), Mathf.LerpUnclamped(a.t.z, b.t.z, k), a.t.w)
        };

        /// <summary>The edge midpoint (commutative: both triangles of an edge get the same floats).</summary>
        static OzMeshSlice.V Mid(in OzMeshSlice.V a, in OzMeshSlice.V b) => new OzMeshSlice.V
        {
            p = (a.p + b.p) * 0.5f, n = (a.n + b.n).normalized, u0 = (a.u0 + b.u0) * 0.5f, u1 = (a.u1 + b.u1) * 0.5f, c = (a.c + b.c) * 0.5f,
            t = new Vector4((a.t.x + b.t.x) * 0.5f, (a.t.y + b.t.y) * 0.5f, (a.t.z + b.t.z) * 0.5f, a.t.w)
        };

        static bool Less(Vector3 a, Vector3 b) => a.x != b.x ? a.x < b.x : a.y != b.y ? a.y < b.y : a.z < b.z;

        /// <summary>Where the surface d = 0 crosses edge a-b, computed from the lower position first so both triangles agree.</summary>
        static OzMeshSlice.V Crossing(in OzMeshSlice.V a, in OzMeshSlice.V b, float da, float db)
            => Less(b.p, a.p) ? Lerp(b, a, db / (db - da)) : Lerp(a, b, da / (da - db));

        static Vector3Int Key(Vector3 p, float q) => new Vector3Int(Mathf.RoundToInt(p.x / q), Mathf.RoundToInt(p.y / q), Mathf.RoundToInt(p.z / q));
        static long EdgeKey(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;

        static float Quantum(OzMeshSlice.Part p)
        {
            var b = p.Box(); return Mathf.Max(1e-6f, Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z)) * 2e-5f);
        }

        // ------------------------------------------------------------------ the cut
        /// <summary>Breaks `src` along the plane dot(n, p) = w (mesh space; sc = metres per mesh unit): `front` = the side n points to.
        /// tilt: the plane may turn a few degrees (off for cuts whose plane other code reuses). Draws a fixed count of numbers from
        /// `rnd`. False = the plane misses the piece (nothing changes). Any failure falls back to the plain cut.</summary>
        public static bool Split(OzMeshSlice.Part src, Vector3 n, float w, float sc, System.Random rnd, bool tilt, out OzMeshSlice.Part front, out OzMeshSlice.Part back, bool capFront = true, bool capBack = true)
        {
            front = null; back = null;
            if (src == null) return false;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            // the draws first, always the same count
            float tiltDeg = R(rnd, TiltMin, TiltMax), tiltAng = R(rnd, 0f, Mathf.PI * 2f);
            int seedF = rnd.Next(), seedB = rnd.Next();
            float rotF = R(rnd, 0f, Mathf.PI * 2f), rotB = R(rnd, 0f, Mathf.PI * 2f);
            var offF = new Vector2(R(rnd, 0f, 1f), R(rnd, 0f, 1f)); var offB = new Vector2(R(rnd, 0f, 1f), R(rnd, 0f, 1f));
            try
            {
                bool ok = Carve(src, n, w, Mathf.Max(1e-5f, sc), tilt ? tiltDeg : 0f, tiltAng, seedF, seedB, rotF, rotB, offF, offB, out front, out back, capFront, capBack);
                LastMs = (float)clock.Elapsed.TotalMilliseconds; if (LastMs > MaxMs) MaxMs = LastMs;
                if (ok) { Cuts++; return true; }
                // the plain plane crosses the piece but the break left one side empty (or the tilt missed it): the plain cut, counted,
                // so the piece count stays as before instead of the part silently staying whole
                front = null; back = null;
                if (!OzMeshSlice.Split(src, n, w, out front, out back, capFront, capBack)) return false;
                Fallbacks++; LastError = "the break left one side empty: the plain cut";
                return true;
            }
            catch (System.Exception e)
            {
                Fallbacks++; LastError = e.GetType().Name + ": " + e.Message;
                front = null; back = null;
                return OzMeshSlice.Split(src, n, w, out front, out back, capFront, capBack);
            }
        }

        static bool Carve(OzMeshSlice.Part src, Vector3 n, float w, float sc, float tiltDeg, float tiltAng, int seedF, int seedB, float rotF, float rotB,
                          Vector2 offF, Vector2 offB, out OzMeshSlice.Part front, out OzMeshSlice.Part back, bool capFront, bool capBack)
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
            float thickF = dMax * sc, thickB = -dMin * sc;
            // the tilt: about an in-plane axis through the section's middle, limited so the break stays well inside both sides
            if (tiltDeg > 0.01f)
            {
                float half = 0.5f * Mathf.Max(uMax - uMin, vMax - vMin) * sc;
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
                    thickF = dMax * sc; thickB = -dMin * sc;
                }
            }
            var fF = new Field(new System.Random(seedF), e1, e2, sc, Mathf.Min(MaxDepth, ThickShare * thickF));
            var fB = new Field(new System.Random(seedB), e1, e2, sc, Mathf.Min(MaxDepth, ThickShare * thickB));
            Deepest = Mathf.Max(Deepest, Mathf.Max(fF.Amp, fB.Amp));
            // 1. the skin near the break subdivided (both sides' bands)
            float q = Quantum(src);
            float lo = -(fB.Amp + Margin) / sc, hi = (fF.Amp + Margin) / sc;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var p0 = Refine(src, n, w, lo, hi, Edge / sc, q);
            MsRefine += (float)sw.Elapsed.TotalMilliseconds; sw.Restart();
            // 2. clipped by each side's surface
            int m = p0.v.Count; var dF = new float[m]; var dB = new float[m];
            for (int i = 0; i < m; i++)
            {
                var p = p0.v[i].p; float d = Vector3.Dot(n, p) - w;
                bool near = d > lo - Edge / sc && d < hi + Edge / sc;                                    // the field only matters near the break
                dF[i] = d - (near ? fF.H(p) / sc : 0f); dB[i] = -d - (near ? fB.H(p) / sc : 0f);
            }
            var segF = new List<Seg>(); var segB = new List<Seg>();
            var distF = new List<float>(); var distB = new List<float>();
            var F = Clip(p0, dF, segF, q, distF); var B = Clip(p0, dB, segB, q, distB);
            MsClip += (float)sw.Elapsed.TotalMilliseconds; sw.Restart();
            if (F.Tris == 0 || B.Tris == 0) return false;
            // 3. the painted lip bent in, scorch on the paint near the break
            Lip(F, distF, fF, sc, q); Lip(B, distB, fB, sc, q);
            MsLip += (float)sw.Elapsed.TotalMilliseconds; sw.Restart();
            // 4. the relief caps on the break's own outline
            if (capFront) Cap(F, segF, n, w, +1f, fF, sc, rotF, offF, q);
            if (capBack) Cap(B, segB, n, w, -1f, fB, sc, rotB, offB, q);
            MsCap += (float)sw.Elapsed.TotalMilliseconds;
            front = F; back = B;
            return true;
        }

        /// <summary>v turned by deg degrees about the unit axis k (Rodrigues).</summary>
        static Vector3 Turn(Vector3 v, Vector3 k, float deg)
        {
            float a = deg * Mathf.Deg2Rad, c = Mathf.Cos(a), s = Mathf.Sin(a);
            return v * c + Vector3.Cross(k, v) * s + k * (Vector3.Dot(k, v) * (1f - c));
        }

        /// <summary>e1 x e2 = n (as OzMeshSlice.Cap).</summary>
        static void Basis(Vector3 n, out Vector3 e1, out Vector3 e2)
        {
            e1 = Vector3.Cross(n, Mathf.Abs(n.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            e2 = Vector3.Cross(n, e1);
        }

        // ------------------------------------------------------------------ 1. subdivision near the break (conforming: an edge is split
        // for every triangle that has it, decided by its two positions)
        static OzMeshSlice.Part Refine(OzMeshSlice.Part src, Vector3 n, float w, float lo, float hi, float maxEdge, float q)
        {
            var cur = src.Like(); cur.v.AddRange(src.v);
            float max2 = maxEdge * maxEdge;
            var d = new List<float>(src.v.Count * 2);
            foreach (var v in src.v) d.Add(Vector3.Dot(n, v.p) - w);
            // an edge is split when it is long and near the break: decided by its two positions alone, so every triangle that has the
            // edge (also across a UV seam) splits it the same way; a triangle with no such edge never gets one later
            bool Cut(int a, int b)
            {
                float da = d[a], db = d[b];
                bool near = (da >= lo && da <= hi) || (db >= lo && db <= hi) || (da < lo && db > hi) || (db < lo && da > hi);
                return near && (cur.v[a].p - cur.v[b].p).sqrMagnitude > max2;
            }
            var mid = new Dictionary<long, int>();
            int M(int a, int b)
            {
                if (!Cut(a, b)) return -1;
                long key = EdgeKey(a, b);
                if (!mid.TryGetValue(key, out int r)) { r = cur.v.Count; var m = Mid(cur.v[a], cur.v[b]); cur.v.Add(m); d.Add(Vector3.Dot(n, m.p) - w); mid[key] = r; }
                return r;
            }
            var kids = new List<int>(12);
            for (int si = 0; si < src.sub.Length; si++)
            {
                var s = src.sub[si]; var done = new List<int>(s.Count + 64); var active = new List<int>();
                for (int k = 0; k + 2 < s.Count; k += 3)
                {
                    int a = s[k], b = s[k + 1], c = s[k + 2];
                    var to = Cut(a, b) || Cut(b, c) || Cut(c, a) ? active : done;
                    to.Add(a); to.Add(b); to.Add(c);
                }
                for (int pass = 0; pass < 8 && active.Count > 0; pass++)
                {
                    var next = new List<int>(active.Count * 3);
                    for (int k = 0; k + 2 < active.Count; k += 3)
                    {
                        int a = active[k], b = active[k + 1], c = active[k + 2];
                        kids.Clear(); SplitTri(kids, a, b, c, M(a, b), M(b, c), M(c, a), cur.v);
                        for (int j = 0; j + 2 < kids.Count; j += 3)
                        {
                            int x = kids[j], y = kids[j + 1], z = kids[j + 2];
                            var to = Cut(x, y) || Cut(y, z) || Cut(z, x) ? next : done;
                            to.Add(x); to.Add(y); to.Add(z);
                        }
                    }
                    active = next;
                }
                done.AddRange(active);
                cur.sub[si] = done;
            }
            return cur;
        }

        static float D2(List<OzMeshSlice.V> v, int a, int b) => (v[a].p - v[b].p).sqrMagnitude;

        /// <summary>One triangle with 0-3 of its edges split (m.. = the midpoint or -1), orientation kept.</summary>
        static void SplitTri(List<int> o, int a, int b, int c, int mab, int mbc, int mca, List<OzMeshSlice.V> v)
        {
            int k = (mab >= 0 ? 1 : 0) + (mbc >= 0 ? 1 : 0) + (mca >= 0 ? 1 : 0);
            void T(int x, int y, int z) { o.Add(x); o.Add(y); o.Add(z); }
            if (k == 0) { T(a, b, c); return; }
            if (k == 3) { T(a, mab, mca); T(mab, b, mbc); T(mca, mbc, c); T(mab, mbc, mca); return; }
            if (k == 1)
            {
                if (mab >= 0) { T(a, mab, c); T(mab, b, c); }
                else if (mbc >= 0) { T(b, mbc, a); T(mbc, c, a); }
                else { T(c, mca, b); T(mca, a, b); }
                return;
            }
            // two: relabel so the edge left whole is c-a
            if (mca >= 0 && mab < 0) { int t0 = a; a = b; b = c; c = t0; int m0 = mab; mab = mbc; mbc = mca; mca = m0; }        // whole was a-b
            else if (mab >= 0 && mbc < 0) { int t0 = a; a = c; c = b; b = t0; int m0 = mab; mab = mca; mca = mbc; mbc = m0; }   // whole was b-c
            T(mab, b, mbc);
            if (D2(v, a, mbc) < D2(v, mab, c)) { T(a, mab, mbc); T(a, mbc, c); } else { T(a, mab, c); T(mab, mbc, c); }
        }

        // ------------------------------------------------------------------ 2. clip by a signed field (keep d >= 0)
        static OzMeshSlice.Part Clip(OzMeshSlice.Part src, float[] d, List<Seg> segs, float q, List<float> dist)
        {
            var o = src.Like(); o.hc = true;
            var map = new int[d.Length]; for (int i = 0; i < map.Length; i++) map[i] = -1;
            float eps = q * 0.05f;
            for (int i = 0; i < d.Length; i++) if (Mathf.Abs(d[i]) < eps) d[i] = eps;                    // nothing lies exactly on the surface
            int Keep(int i) { int k = map[i]; if (k < 0) { k = o.v.Count; o.v.Add(src.v[i]); dist.Add(d[i]); map[i] = k; } return k; }
            var poly = new List<OzMeshSlice.V>(4); var polyD = new List<float>(4); var ids = new int[3];
            for (int si = 0; si < src.sub.Length; si++)
            {
                var s = src.sub[si];
                for (int k = 0; k + 2 < s.Count; k += 3)
                {
                    ids[0] = s[k]; ids[1] = s[k + 1]; ids[2] = s[k + 2];
                    float d0 = d[ids[0]], d1 = d[ids[1]], d2 = d[ids[2]];
                    if (d0 > 0f && d1 > 0f && d2 > 0f) { o.sub[si].Add(Keep(ids[0])); o.sub[si].Add(Keep(ids[1])); o.sub[si].Add(Keep(ids[2])); continue; }
                    if (d0 < 0f && d1 < 0f && d2 < 0f) continue;
                    poly.Clear(); polyD.Clear(); int xOut = -1, xIn = -1;
                    for (int e = 0; e < 3; e++)
                    {
                        int ia = ids[e], ib = ids[(e + 1) % 3]; float da = d[ia], db = d[ib];
                        if (da > 0f) { poly.Add(src.v[ia]); polyD.Add(da); }
                        if ((da > 0f) != (db > 0f))
                        {
                            if (da > 0f) xOut = poly.Count; else xIn = poly.Count;
                            poly.Add(Crossing(src.v[ia], src.v[ib], da, db)); polyD.Add(0f);
                        }
                    }
                    if (poly.Count < 3 || xOut < 0 || xIn < 0) continue;
                    int b0 = o.v.Count; o.v.AddRange(poly);
                    dist.AddRange(polyD);
                    for (int i = 1; i + 1 < poly.Count; i++)
                    {
                        if (Vector3.Cross(poly[i].p - poly[0].p, poly[i + 1].p - poly[0].p).sqrMagnitude < 1e-20f) continue;
                        o.sub[si].Add(b0); o.sub[si].Add(b0 + i); o.sub[si].Add(b0 + i + 1);
                    }
                    segs.Add(new Seg { a = poly[xOut].p, b = poly[xIn].p, ia = b0 + xOut, ib = b0 + xIn });
                }
            }
            return o;
        }

        // ------------------------------------------------------------------ 3. the bent lip and the scorch (position-keyed: every copy of a
        // corner moves and darkens the same, so no crack opens at a seam)
        static void Lip(OzMeshSlice.Part p, List<float> dmesh, Field f, float sc, float q)
        {
            int nv = p.v.Count;
            var nrm = new Dictionary<Vector3Int, Vector3>();
            var keys = new Vector3Int[nv]; var dist = new float[nv];
            for (int i = 0; i < nv; i++)
            {
                var v = p.v[i];
                dist[i] = Mathf.Max(0f, dmesh[i] * sc);                                                 // to the break surface (m; the clip's own numbers)
                if (dist[i] >= Mathf.Max(Reach, ScorchReach)) continue;
                keys[i] = Key(v.p, q);
                if (dist[i] < Mathf.Max(Reach, ScorchReach)) { nrm.TryGetValue(keys[i], out var s); nrm[keys[i]] = s + v.n; }
            }
            float sMin = ScorchMin, sMax = ScorchMax;
            for (int i = 0; i < nv; i++)
            {
                float dm = dist[i]; if (dm >= Mathf.Max(Reach, ScorchReach)) continue;
                var v = p.v[i];
                if (dm < Reach && nrm.TryGetValue(keys[i], out var an) && an.sqrMagnitude > 1e-12f)
                {
                    float k = 1f - Mathf.SmoothStep(0f, 1f, dm / Reach);
                    v.p -= an.normalized * (f.Lip(v.p) * k / sc);
                }
                if (dm < ScorchReach)
                {
                    float nz = f.Noise(v.p);
                    float edge = Mathf.Lerp(sMin, sMax, nz);
                    float k = Mathf.Lerp(edge, 1f, Mathf.SmoothStep(0f, 1f, dm / ScorchReach));
                    v.c = new Color(v.c.r * k, v.c.g * k * 0.98f, v.c.b * k * 0.95f, v.c.a);
                }
                p.v[i] = v;
            }
        }

        // ------------------------------------------------------------------ 4. the relief cap
        // The outline is the convex outline of the break's own points (every point of the clipped skin's edge on it is a vertex of the
        // cap, so where the section is convex the face meets the skin exactly; where it is not, the face spans the hollow, as the plain
        // cap did). Inside: rings toward the middle, each a scaled copy of the outline with fewer points (about CapEdge apart), the
        // points carved by the same field as the skin. Robust on a game mesh that is not watertight.
        static void Cap(OzMeshSlice.Part p, List<Seg> segs, Vector3 n, float w, float side, Field f, float sc, float rot, Vector2 off, float q)
        {
            if (segs.Count < 2) return;
            var outward = -side * n;
            Basis(outward, out var e1, out var e2);                                                         // e1 x e2 = outward: CCW in (e1, e2) faces out
            var O = n * w;
            // the break's points (their vertices after the lip bend), one per position
            var seen = new HashSet<Vector3Int>(); var vid = new List<int>(); var flat = new List<Vector2>();
            foreach (var s in segs)
                for (int e = 0; e < 2; e++)
                {
                    int vi = e == 0 ? s.ia : s.ib; var k = Key(e == 0 ? s.a : s.b, q);
                    if (!seen.Add(k)) continue;
                    var r = p.v[vi].p - O; vid.Add(vi); flat.Add(new Vector2(Vector3.Dot(r, e1), Vector3.Dot(r, e2)));
                }
            var hull = HullIdx(flat);
            if (hull.Count < 3) return;
            Caps++;
            float step = CapEdge / sc;
            // the outline: hull points (real skin vertices) + points on long hull edges (carved by the field)
            var B2 = new List<Vector2>(); var B3 = new List<Vector3>();
            for (int i = 0; i < hull.Count; i++)
            {
                int a = hull[i], b = hull[(i + 1) % hull.Count];
                B2.Add(flat[a]); B3.Add(p.v[vid[a]].p);
                float len = (flat[b] - flat[a]).magnitude; int k = Mathf.Min(64, Mathf.FloorToInt(len / step));
                for (int j = 1; j <= k; j++)
                {
                    var q2 = Vector2.Lerp(flat[a], flat[b], j / (float)(k + 1));
                    B2.Add(q2); B3.Add(Carved(O, e1, e2, q2, n, side, f, sc));
                }
            }
            int nb = B2.Count;
            // the middle (area centroid) and the rings
            Vector2 c = Vector2.zero; float A = 0f;
            for (int i = 0; i < nb; i++) { var a = B2[i]; var b = B2[(i + 1) % nb]; float cr = Cross2(a, b); A += cr; c += (a + b) * cr; }
            if (Mathf.Abs(A) < 1e-12f) return;
            c /= 3f * A;
            float rMax = 0f, per = 0f;
            for (int i = 0; i < nb; i++) { rMax = Mathf.Max(rMax, (B2[i] - c).magnitude); per += (B2[(i + 1) % nb] - B2[i]).magnitude; }
            int K = Mathf.Clamp(Mathf.CeilToInt(rMax / step), 1, 60);
            var P = new List<Vector3>(); var H = new List<float>(); var RD = new List<float>();             // the cap's points: position, carve depth (m), rim distance (m)
            var ring = new List<int>[K + 1]; var at = new List<int>[K + 1];                                  // per ring: its points, and the outline index each stands on
            for (int k = 0; k <= K; k++)
            {
                float t = 1f - k / (float)K;
                int m = k == K ? 1 : k == 0 ? nb : Mathf.Clamp(Mathf.CeilToInt(t * per / step), 3, nb);
                var list = new List<int>(m); var idx = new List<int>(m); int last = -1;
                for (int j = 0; j < m; j++)
                {
                    int bi = k == 0 ? j : Mathf.Min(nb - 1, Mathf.RoundToInt(j * nb / (float)m));
                    if (bi == last) continue; last = bi;
                    Vector3 pos;
                    if (k == 0) pos = B3[bi];
                    else pos = Carved(O, e1, e2, c + (B2[bi] - c) * t, n, side, f, sc);
                    list.Add(P.Count); idx.Add(bi); P.Add(pos); H.Add(f.H(pos)); RD.Add((1f - t) * (B2[bi] - c).magnitude * sc);
                }
                ring[k] = list; at[k] = idx;
            }
            // zip each ring to the next (both in outline order, both starting at outline point 0)
            var tris = new List<int>();
            for (int k = 0; k < K; k++) Zip(ring[k], at[k], ring[k + 1], at[k + 1], nb, tris);
            if (tris.Count < 3) return;
            var N = new Vector3[P.Count];
            for (int t = 0; t + 2 < tris.Count; t += 3)
            {
                var g = Vector3.Cross(P[tris[t + 1]] - P[tris[t]], P[tris[t + 2]] - P[tris[t]]);
                N[tris[t]] += g; N[tris[t + 1]] += g; N[tris[t + 2]] += g;
            }
            float amp = Mathf.Max(1e-4f, f.Amp);
            var tdir = Mathf.Cos(rot) * e1 + Mathf.Sin(rot) * e2; var bdir = Vector3.Cross(outward, tdir);
            float inv = sc / Mathf.Max(0.1f, TileMetres);
            int b0 = p.v.Count; var tan = new Vector4(tdir.x, tdir.y, tdir.z, 1f);
            for (int i = 0; i < P.Count; i++)
            {
                float hn = Mathf.Clamp01(H[i] / amp), nz = f.Noise(P[i]);
                float lvl = Mathf.Lerp(RidgeLight, PitDark, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.12f, 0.88f, hn))) * Mathf.Lerp(0.88f, 1.1f, nz);
                var col = new Color(lvl, lvl, lvl, 1f);
                float rd = RD[i];
                if (rd < RimReach)
                {
                    float k = 1f - Mathf.SmoothStep(0f, 1f, rd / RimReach);
                    var tint = rd < RimReach * 0.45f ? Rust : (nz > 0.5f ? Straw : Blue);
                    col = Color.Lerp(col, new Color(col.r * tint.r, col.g * tint.g, col.b * tint.b, 1f), k);
                }
                var nn = N[i].sqrMagnitude > 1e-20f ? N[i].normalized : outward;
                p.v.Add(new OzMeshSlice.V { p = P[i], n = nn, u0 = new Vector2(Vector3.Dot(P[i], tdir) * inv + off.x, Vector3.Dot(P[i], bdir) * inv + off.y), u1 = default, c = col, t = tan });
            }
            int cs = p.CapSub;
            foreach (int i in tris) p.sub[cs].Add(b0 + i);
            CapTrisMade += tris.Count / 3;
        }

        /// <summary>Triangles between an outer ring (points a, on outline indices ia) and the next ring in (points b, ib), CCW.</summary>
        static void Zip(List<int> a, List<int> ia, List<int> b, List<int> ib, int nb, List<int> tris)
        {
            int na = a.Count, nbb = b.Count, i = 0, j = 0;
            if (na == 0 || nbb == 0) return;
            if (nbb == 1)
            {
                for (int k = 0; k < na; k++) { tris.Add(a[k]); tris.Add(a[(k + 1) % na]); tris.Add(b[0]); }
                return;
            }
            while (i < na || j < nbb)
            {
                int nextA = i + 1 < na ? ia[i + 1] : nb + ia[0], nextB = j + 1 < nbb ? ib[j + 1] : nb + ib[0];
                bool advA = j >= nbb || (i < na && nextA <= nextB);
                if (advA) { tris.Add(a[i % na]); tris.Add(a[(i + 1) % na]); tris.Add(b[j % nbb]); i++; }
                else { tris.Add(a[i % na]); tris.Add(b[(j + 1) % nbb]); tris.Add(b[j % nbb]); j++; }
            }
        }

        /// <summary>The cap plane's point q (2D) carved by the field (mesh space).</summary>
        static Vector3 Carved(Vector3 O, Vector3 e1, Vector3 e2, Vector2 q, Vector3 n, float side, Field f, float sc)
        {
            var flat = O + e1 * q.x + e2 * q.y;
            return flat + n * (side * f.H(flat) / sc);
        }

        static float Cross2(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        /// <summary>Andrew's monotone chain over indices: counter-clockwise, no collinear points.</summary>
        static List<int> HullIdx(List<Vector2> pts)
        {
            var order = new List<int>(pts.Count); for (int i = 0; i < pts.Count; i++) order.Add(i);
            order.Sort((x, y) => pts[x].x != pts[y].x ? pts[x].x.CompareTo(pts[y].x) : pts[x].y.CompareTo(pts[y].y));
            var h = new List<int>(pts.Count + 1);
            if (order.Count < 3) return h;
            var span = pts[order[order.Count - 1]] - pts[order[0]]; float tol = Mathf.Max(1e-14f, span.sqrMagnitude * 1e-10f);
            for (int pass = 0; pass < 2; pass++)
            {
                int start = h.Count;
                for (int k = 0; k < order.Count; k++)
                {
                    int id = pass == 0 ? order[k] : order[order.Count - 1 - k];
                    while (h.Count >= start + 2 && Cross2(pts[h[h.Count - 1]] - pts[h[h.Count - 2]], pts[id] - pts[h[h.Count - 2]]) <= tol) h.RemoveAt(h.Count - 1);
                    h.Add(id);
                }
                h.RemoveAt(h.Count - 1);
            }
            return h;
        }

        // ------------------------------------------------------------------ the closed hidden sides
        /// <summary>A side that was hidden inside the gun, closed by <see cref="OzMeshSlice.Close"/> (the head's underside, the sleeve's back, the
        /// ring's top), dressed as the broken faces are, a shade darker: its cap UVs in metres (a random turn), its colour
        /// <see cref="HiddenShade"/>. Call it right after Close on a piece that had no cap before.</summary>
        public static OzMeshSlice.Part Close(OzMeshSlice.Part p, Vector3 n, float w, float sc, System.Random rnd)
        {
            float rot = R(rnd, 0f, Mathf.PI * 2f); var off = new Vector2(R(rnd, 0f, 1f), R(rnd, 0f, 1f));
            var r = OzMeshSlice.Close(p, n, w);
            if (r == null || ReferenceEquals(r, p)) return p;                                              // nothing was closed
            try
            {
                r.hc = true;
                float inv = Mathf.Max(1e-5f, sc) / Mathf.Max(0.1f, TileMetres);
                var seen = new HashSet<int>();
                foreach (int i in r.sub[r.CapSub])
                {
                    if (!seen.Add(i)) continue;
                    var v = r.v[i];
                    Basis(v.n, out var e1, out var e2);
                    var t = Mathf.Cos(rot) * e1 + Mathf.Sin(rot) * e2; var b = Vector3.Cross(v.n, t);
                    v.u0 = new Vector2(Vector3.Dot(v.p, t) * inv + off.x, Vector3.Dot(v.p, b) * inv + off.y);
                    v.t = new Vector4(t.x, t.y, t.z, 1f);
                    v.c = new Color(HiddenShade, HiddenShade * 0.97f, HiddenShade * 0.94f, 1f);
                    r.v[i] = v;
                }
                Closures++;
            }
            catch (System.Exception e) { Fallbacks++; LastError = e.GetType().Name + ": " + e.Message; }
            return r;
        }

        /// <summary>Two halves of a big part along an oblique fractured plane: normal = `across` turned `turnDeg` about `about`, at
        /// `share` of the part's extent along it. Returns false when it does not cut.</summary>
        public static bool Halves(OzMeshSlice.Part p, Vector3 across, Vector3 about, float turnDeg, float share, float sc, System.Random rnd, out OzMeshSlice.Part a, out OzMeshSlice.Part b)
        {
            a = null; b = null;
            if (p == null) return false;
            var n = Turn(across, about.normalized, turnDeg).normalized;
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (var v in p.v) { float d = Vector3.Dot(n, v.p); if (d < lo) lo = d; if (d > hi) hi = d; }
            if (hi - lo < 1e-6f) return false;
            return Split(p, n, lo + share * (hi - lo), sc, rnd, true, out a, out b);
        }
    }
}
