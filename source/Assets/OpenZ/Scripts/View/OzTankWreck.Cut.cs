using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// kit's harness). The hull is broken by a few seeded fracture cuts that are never on a grid: one across the hull, turned and tilted;
    /// one along the bigger half, turned and tilted; a wedge off the other half's upper side; the biggest piece once more on a slant; then a few corner shards off the biggest
    /// pieces (the cannon's fracture cut, OzFractureCut.Split: jagged, stepped, relief faces with thickness, the painted lip bent in and
    /// sooted). Both tracks are torn where the hull broke across (OzFractureCut.Tear), each length a plate with a torn wall
    /// (OzFractureCut.Plate), so it stays with the hull piece over it. Then soot is laid on the paint as patches (vertex colours), so the pieces read charred with traces of their
    /// own paint, never as one flat black. Mesh space of each part; `sc` = metres per mesh unit.
    /// </summary>
    public static partial class OzTankWreck
    {
        // ------------------------------------------------------------------ tuning (degrees, shares, metres)
        public static float AcrossTurnMin = 12f, AcrossTurnMax = 30f, AcrossTiltMax = 14f, AcrossAtMin = 0.36f, AcrossAtMax = 0.62f;
        public static float AlongTurnMin = 14f, AlongTurnMax = 34f, AlongTiltMax = 18f, AlongAtMin = 0.36f, AlongAtMax = 0.64f;
        public static float WedgeUpMin = 0.55f, WedgeUpMax = 1.1f, WedgeAtMin = 0.56f, WedgeAtMax = 0.72f;
        /// <summary>The biggest piece is broken once more on a slant through all three axes, at this share of it.</summary>
        public static bool Diagonal = true;
        public static float DiagAtMin = 0.38f, DiagAtMax = 0.62f;
        /// <summary>Corner shards: how many, and how deep each corner is cut (m, along the corner's direction).</summary>
        public static int Shards = 3;
        public static float ShardDepthMin = 0.18f, ShardDepthMax = 0.34f, ShardMinM = 0.25f, ShardMaxM = 1.5f;
        /// <summary>The tracks are torn where the hull broke across (each length stays with the hull piece over it).</summary>
        public static bool TearTrack = true;
        /// <summary>Soot patches on the paint (vertex colour x): the darkest patch, the share of the paint under soot, the patch size (m).</summary>
        public static float SootMin = 0.36f, SootCover = 0.55f, SootPatchM = 0.9f, UnderSoot = 0.4f;

        /// <summary>One piece of a part's break-up (mesh space of its part).</summary>
        public sealed class Piece { public string name; public OzMeshSlice.Part part; public bool track; }

        /// <summary>The counts of one part's cuts (made by this code alone; the cannon's counters are kept its own).</summary>
        public sealed class CutStats { public int cuts, plain, tears, plates, shards, tris; public string error = "-"; }

        static float Rf(System.Random r, float a, float b) => a + (float)r.NextDouble() * (b - a);
        static float Sign(System.Random r) => r.Next(2) == 0 ? -1f : 1f;

        /// <summary>v turned by deg degrees about the unit axis k (Rodrigues; no engine call, so it runs on any thread and off-engine).</summary>
        static Vector3 Rot(Vector3 v, Vector3 k, float deg)
        {
            float a = deg * Mathf.Deg2Rad, c = Mathf.Cos(a), s = Mathf.Sin(a);
            return v * c + Vector3.Cross(k, v) * s + k * (Vector3.Dot(k, v) * (1f - c));
        }

        static void Span(OzMeshSlice.Part p, Vector3 n, out float lo, out float hi)
        {
            lo = float.MaxValue; hi = float.MinValue;
            foreach (var s in p.sub) foreach (int i in s) { float d = Vector3.Dot(n, p.v[i].p); if (d < lo) lo = d; if (d > hi) hi = d; }
        }

        static float Vol(OzMeshSlice.Part p) { var s = p.Box().size; return Mathf.Abs(s.x * s.y * s.z); }

        /// <summary>A fracture cut of `p` along n at `share` of its extent along n. False = no cut (both sides must keep surface).</summary>
        static bool At(OzMeshSlice.Part p, Vector3 n, float share, float sc, System.Random r, CutStats st, out OzMeshSlice.Part front, out OzMeshSlice.Part back, out float w)
        {
            front = null; back = null; w = 0f;
            if (p == null || p.Tris < 8) return false;
            n.Normalize(); Span(p, n, out float lo, out float hi);
            if (hi - lo < 1e-5f) return false;
            int fb = OzFractureCut.Fallbacks;
            w = lo + share * (hi - lo);
            bool ok = OzFractureCut.Split(p, n, w, sc, r, true, out front, out back) && front != null && back != null && front.Tris > 0 && back.Tris > 0;
            if (OzFractureCut.Fallbacks != fb) { st.plain++; st.error = OzFractureCut.LastError; }
            if (ok) st.cuts++;
            return ok;
        }

        /// <summary>The hull's break-up: across, along the bigger half, a wedge off the other half, the biggest piece on a slant, then corner shards. `up` / `fwd` = the
        /// hull's up and long axes in mesh space (unit vectors). Draws a fixed order of numbers from the seed. planeN / planeW = the cut across
        /// (hull mesh space; zero = none): the tracks are torn there too, so each torn length stays with the hull piece over it.</summary>
        public static List<Piece> CutHull(OzMeshSlice.Part hull, Vector3 up, Vector3 fwd, float sc, int seed, CutStats st, StringBuilder log, out Vector3 planeN, out float planeW)
        {
            var list = new List<Piece>(); planeN = Vector3.zero; planeW = 0f;
            if (hull == null || hull.Tris == 0) return list;
            up.Normalize(); fwd = (fwd - up * Vector3.Dot(fwd, up)).normalized; var side = Vector3.Cross(up, fwd).normalized;
            var r = new System.Random(seed * 7919 + 378);
            // the draws first, always the same count and order
            float aTurn = Sign(r) * Rf(r, AcrossTurnMin, AcrossTurnMax), aTilt = Rf(r, -AcrossTiltMax, AcrossTiltMax), aAt = Rf(r, AcrossAtMin, AcrossAtMax);
            float bTurn = Sign(r) * Rf(r, AlongTurnMin, AlongTurnMax), bTilt = Rf(r, -AlongTiltMax, AlongTiltMax), bAt = Rf(r, AlongAtMin, AlongAtMax);
            float cSide = Sign(r), cUp = Rf(r, WedgeUpMin, WedgeUpMax), cTurn = Rf(r, -20f, 20f), cAt = Rf(r, WedgeAtMin, WedgeAtMax);
            float dUp = Rf(r, 0.5f, 1.0f) * Sign(r), dFwd = Rf(r, 0.4f, 1.0f) * Sign(r), dSide = Rf(r, 0.3f, 0.9f) * Sign(r), dAt = Rf(r, DiagAtMin, DiagAtMax);
            var fr = new System.Random(seed * 4211 + 3781);                                                  // the fracture surfaces' own numbers
            // 1. across the hull
            var nA = Rot(Rot(fwd, up, aTurn), side, aTilt);
            OzMeshSlice.Part front, back;
            if (!At(hull, nA, aAt, sc, fr, st, out front, out back, out float wA)) { list.Add(new Piece { name = "hull", part = hull }); log?.Append("hull: the cut across failed, whole; "); return list; }
            planeN = nA.normalized; planeW = wA;
            bool frontBig = Vol(front) >= Vol(back);
            var big = frontBig ? front : back; var small = frontBig ? back : front;
            string bigName = frontBig ? "front" : "rear", smallName = frontBig ? "rear" : "front";
            // 2. along the bigger half
            var nB = Rot(Rot(side, up, bTurn), fwd, bTilt);
            var pieces = new List<(string n, OzMeshSlice.Part p)>();
            if (At(big, nB, bAt, sc, fr, st, out var bl, out var br, out _)) { pieces.Add((bigName + "_l", bl)); pieces.Add((bigName + "_r", br)); }
            else pieces.Add((bigName, big));
            // 3. a wedge off the smaller half's upper side
            var nC = Rot((side * cSide + up * cUp).normalized, up, cTurn);
            if (At(small, nC, cAt, sc, fr, st, out var wedge, out var rest, out _)) { pieces.Add((smallName, rest)); pieces.Add((smallName + "_wedge", wedge)); }
            else pieces.Add((smallName, small));
            // 4. the biggest piece once more, on a slant through all three axes (no piece stays a big block)
            if (Diagonal && pieces.Count > 0)
            {
                int bi = 0; for (int i = 1; i < pieces.Count; i++) if (Vol(pieces[i].p) > Vol(pieces[bi].p)) bi = i;
                var nD = (up * dUp + fwd * dFwd + side * dSide).normalized;
                if (At(pieces[bi].p, nD, dAt, sc, fr, st, out var da, out var db, out _)) { var nm = pieces[bi].n; pieces[bi] = (nm + "_a", da); pieces.Add((nm + "_b", db)); }
            }
            // 5. corner shards off the biggest pieces, at their upper outer corners
            var sr = new System.Random(seed * 6271 + 3782);
            var hc = hull.Box().center;
            int made = 0; var why = new StringBuilder();
            for (int k = 0; k < Shards; k++)
            {
                float s1 = Sign(sr), s2 = Sign(sr), dep = Rf(sr, ShardDepthMin, ShardDepthMax), tu = Rf(sr, -12f, 12f);
                if (pieces.Count == 0) break;
                int bi = 0; for (int i = 1; i < pieces.Count; i++) if (Vol(pieces[i].p) > Vol(pieces[bi].p)) bi = i;
                var host = pieces[bi].p;
                // the corner away from the hull's middle on this piece's side
                var off = host.Box().center - hc; float sx = Mathf.Abs(Vector3.Dot(off, side)) > 1e-4f ? Mathf.Sign(Vector3.Dot(off, side)) : s1;
                float sz = Mathf.Abs(Vector3.Dot(off, fwd)) > 1e-4f ? Mathf.Sign(Vector3.Dot(off, fwd)) : s2;
                var dir = Rot((up * 0.8f + side * sx + fwd * sz * 0.9f).normalized, up, tu).normalized;
                float best = float.MinValue; foreach (var s in host.sub) foreach (int i in s) best = Mathf.Max(best, Vector3.Dot(dir, host.v[i].p));
                float w = best - dep / sc;
                if (!OzFractureCut.Split(host, dir, w, sc, fr, false, out var shard, out var keep) || shard == null || keep == null) { why.Append("shard: no cut; "); continue; }
                var ss = shard.Box().size * sc; float bigM = Mathf.Max(ss.x, Mathf.Max(ss.y, ss.z)), thin = Mathf.Min(ss.x, Mathf.Min(ss.y, ss.z));
                if (thin < 0.08f || bigM < ShardMinM || bigM > ShardMaxM || shard.Tris < 6 || keep.Tris < 8) { why.Append($"shard skipped ({bigM:0.00} x {thin:0.00} m, {shard.Tris} tris); "); continue; }
                st.cuts++;
                pieces[bi] = (pieces[bi].n, keep);
                pieces.Add(("shard_" + (char)('a' + made), shard)); made++;
            }
            st.shards += made;
            foreach (var (n, p) in pieces) { list.Add(new Piece { name = "hull_" + n, part = p }); }
            log?.Append($"hull: {list.Count} pieces (across {aTurn:0}/{aTilt:0} deg at {aAt * 100f:0} %, along {bTurn:0}/{bTilt:0} deg at {bAt * 100f:0} %, wedge {(cSide > 0 ? "+" : "-")}side up {cUp:0.00} at {cAt * 100f:0} %, shards {made}); " + why);
            return list;
        }

        /// <summary>A track torn in two where the hull broke across (n, w: that cut in the TRACK's mesh space): open torn ends, each length a
        /// plate with a wall along the tear (it rides with the hull piece over it). Whole when the plane misses it. Draws a fixed order of
        /// numbers from the seed.</summary>
        public static List<Piece> CutTrack(OzMeshSlice.Part track, Vector3 n, float w, int seed, string name, CutStats st, StringBuilder log)
        {
            var list = new List<Piece>();
            if (track == null || track.Tris == 0) return list;
            var r = new System.Random(seed * 5381 + 3783);
            float m = n.magnitude;
            if (!TearTrack || m < 1e-6f) { list.Add(new Piece { name = name, part = track, track = true }); log?.Append(name + ": whole; "); return list; }
            n /= m; w /= m; Span(track, n, out float lo, out float hi);
            float k = hi - lo > 1e-5f ? (w - lo) / (hi - lo) : -1f;
            if (k < 0.12f || k > 0.88f) { list.Add(new Piece { name = name, part = track, track = true }); log?.Append(name + ": the break misses it, whole; "); return list; }
            int plain = OzFractureCut.ShellPlain;
            if (OzFractureCut.Tear(track, n, w, r, out var a, out var b) && a != null && b != null && a.Tris > 0 && b.Tris > 0)
            {
                if (OzFractureCut.ShellPlain != plain) { st.plain++; st.error = OzFractureCut.ShellError; } else st.tears++;
                OzFractureCut.Plate(a, r); OzFractureCut.Plate(b, r); st.plates += 2;
                list.Add(new Piece { name = name + "_a", part = a, track = true }); list.Add(new Piece { name = name + "_b", part = b, track = true });
                log?.Append($"{name}: torn in two at {k * 100f:0} %; ");
            }
            else { list.Add(new Piece { name = name, part = track, track = true }); log?.Append(name + ": the tear failed, whole; "); }
            return list;
        }

        /// <summary>Soot patches on a piece's paint (sub-meshes before the cap; vertex colours x a seeded smooth field, keyed by position so a
        /// seam never shows): about SootCover of the paint goes down to SootMin, the rest keeps its paint (x 0.9-1). The hull's underside (facing
        /// down, `up` = the part's up in mesh space) is sooted to UnderSoot: never a pale bare belly.</summary>
        public static void Soot(OzMeshSlice.Part p, float sc, int seed, Vector3 up)
        {
            if (p == null) return;
            var r = new System.Random(seed * 2903 + 3784);
            float k = Mathf.PI * 2f * Mathf.Max(1e-4f, sc) / Mathf.Max(0.1f, SootPatchM);
            var dir = new Vector3[4]; var ph = new float[4];
            for (int i = 0; i < 4; i++)
            {
                float a = Rf(r, 0f, Mathf.PI * 2f), b = Rf(r, -1f, 1f), m = Mathf.Sqrt(Mathf.Max(0f, 1f - b * b));
                dir[i] = new Vector3(Mathf.Cos(a) * m, b, Mathf.Sin(a) * m) * (k * (i < 2 ? 1f : 2.1f)); ph[i] = Rf(r, 0f, Mathf.PI * 2f);
            }
            float thr = Mathf.Lerp(0.85f, -0.85f, Mathf.Clamp01(SootCover));                                  // the field's level above which soot lies
            p.hc = true;
            var seen = new HashSet<int>();
            for (int s = 0; s < p.CapSub; s++)
                foreach (int i in p.sub[s])
                {
                    if (!seen.Add(i)) continue;
                    var v = p.v[i]; var q = v.p;
                    float f = 0.6f * (Mathf.Sin(Vector3.Dot(dir[0], q) + ph[0]) + Mathf.Sin(Vector3.Dot(dir[1], q) + ph[1])) * 0.5f
                            + 0.4f * (Mathf.Sin(Vector3.Dot(dir[2], q) + ph[2]) + Mathf.Sin(Vector3.Dot(dir[3], q) + ph[3])) * 0.5f;
                    float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((f - thr) / 0.35f));
                    float g = Mathf.Lerp(0.9f + 0.1f * (0.5f + 0.5f * f), SootMin, t);
                    float dn = up.sqrMagnitude > 0.5f ? -Vector3.Dot(v.n, up) : 0f;
                    if (dn > 0.2f) g *= Mathf.Lerp(1f, UnderSoot, Mathf.SmoothStep(0f, 1f, (dn - 0.2f) / 0.5f));
                    v.c = new Color(v.c.r * g, v.c.g * g * 0.98f, v.c.b * g * 0.96f, v.c.a);
                    p.v[i] = v;
                }
        }
    }
}
