using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenZ
{
    /// <summary>
    /// Version 1.1, item 205: cuts a model's own mesh into real pieces, the way the vehicles' wreck fragments are cut (WreckPieces: the
    /// model's triangles clipped at a wall, every vertex attribute interpolated, nothing invented), for OzFortGunBreakup.
    /// <list type="bullet">
    /// <item><see cref="Islands"/>: the separate solid parts a mesh already holds (triangles joined by shared corners).</item>
    /// <item><see cref="Split"/>: one plane through a piece: both sides keep their surface; the open cut is closed on both sides by a cap
    /// (the convex outline of the cut, in the cap sub-mesh drawn with the wreck's burnt core material), so a piece reads as solid
    /// broken metal, never as a hollow shell.</item>
    /// </list>
    /// Mesh space throughout (the pieces keep their source's object space, so paint bands and decals stay where they were). View only.
    /// </summary>
    public static class OzMeshSlice
    {
        public struct V { public Vector3 p, n; public Vector2 u0, u1; public Color c; public Vector4 t; }

        /// <summary>A piece under construction: vertex lists, one triangle list per sub-mesh; the last sub-mesh holds the caps.</summary>
        public sealed class Part
        {
            public readonly List<V> v = new List<V>();
            public List<int>[] sub;
            public bool hn, h0, h1, hc, ht;
            public int CapSub => sub.Length - 1;
            public Part(int subs) { sub = new List<int>[subs]; for (int i = 0; i < subs; i++) sub[i] = new List<int>(); }
            public Part Like() => new Part(sub.Length) { hn = hn, h0 = h0, h1 = h1, hc = hc, ht = ht };
            public int Tris { get { int k = 0; foreach (var s in sub) k += s.Count / 3; return k; } }
            public int CapTris => sub[CapSub].Count / 3;
            public Bounds Box()
            {
                var b = new Bounds(); bool any = false;
                foreach (var s in sub) foreach (int i in s) { if (!any) { b = new Bounds(v[i].p, Vector3.zero); any = true; } else b.Encapsulate(v[i].p); }
                return b;
            }
        }

        /// <summary>The mesh's triangles (every sub-mesh) as a Part with one extra (empty) cap sub-mesh; only the vertices its index lists use.
        /// Null when the mesh cannot be read.</summary>
        public static Part FromMesh(Mesh m)
        {
            if (!m || !m.isReadable) return null;
            var vs = m.vertices; var ns = m.normals; var a0 = m.uv; var a1 = m.uv2; var cs = m.colors; var ts = m.tangents;
            int nv = vs.Length;
            var p = new Part(m.subMeshCount + 1) { hn = ns.Length == nv, h0 = a0.Length == nv, h1 = a1.Length == nv, hc = cs.Length == nv, ht = ts.Length == nv };
            var map = new Dictionary<int, int>();
            for (int s = 0; s < m.subMeshCount; s++)
            {
                if (m.GetTopology(s) != MeshTopology.Triangles) continue;
                var tri = m.GetTriangles(s);
                for (int k = 0; k + 2 < tri.Length; k += 3)
                    for (int j = 0; j < 3; j++)
                    {
                        int i = tri[k + j];
                        if (!map.TryGetValue(i, out int d))
                        {
                            d = p.v.Count; map[i] = d;
                            p.v.Add(new V { p = vs[i], n = p.hn ? ns[i] : Vector3.up, u0 = p.h0 ? a0[i] : default, u1 = p.h1 ? a1[i] : default, c = p.hc ? cs[i] : Color.white, t = p.ht ? ts[i] : new Vector4(1f, 0f, 0f, 1f) });
                        }
                        p.sub[s].Add(d);
                    }
            }
            return p;
        }

        /// <summary>The separate solid parts of `src` (triangles joined through corners at the same position), largest first.</summary>
        public static List<Part> Islands(Part src)
        {
            int nv = src.v.Count;
            var weld = new Dictionary<Vector3Int, int>(); var root = new int[nv];
            var b = src.Box(); float q = Mathf.Max(1e-6f, Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z)) * 2e-5f);
            for (int i = 0; i < nv; i++)
            {
                var p = src.v[i].p; var key = new Vector3Int(Mathf.RoundToInt(p.x / q), Mathf.RoundToInt(p.y / q), Mathf.RoundToInt(p.z / q));
                if (!weld.TryGetValue(key, out int w)) { w = i; weld[key] = i; }
                root[i] = w;
            }
            var parent = new int[nv]; for (int i = 0; i < nv; i++) parent[i] = i;
            int Find(int a) { while (parent[a] != a) { parent[a] = parent[parent[a]]; a = parent[a]; } return a; }
            foreach (var s in src.sub)
                for (int k = 0; k + 2 < s.Count; k += 3)
                {
                    int a = Find(root[s[k]]), c1 = Find(root[s[k + 1]]), c2 = Find(root[s[k + 2]]);
                    parent[c1] = a; parent[Find(c2)] = a;
                }
            var byRoot = new Dictionary<int, Part>(); var maps = new Dictionary<int, Dictionary<int, int>>();
            for (int si = 0; si < src.sub.Length; si++)
            {
                var s = src.sub[si];
                for (int k = 0; k + 2 < s.Count; k += 3)
                {
                    int r = Find(root[s[k]]);
                    if (!byRoot.TryGetValue(r, out var part)) { part = src.Like(); byRoot[r] = part; maps[r] = new Dictionary<int, int>(); }
                    var map = maps[r];
                    for (int j = 0; j < 3; j++)
                    {
                        int i = s[k + j];
                        if (!map.TryGetValue(i, out int d)) { d = part.v.Count; part.v.Add(src.v[i]); map[i] = d; }
                        part.sub[si].Add(d);
                    }
                }
            }
            var list = new List<Part>(byRoot.Values);
            list.Sort((x, y) => y.Tris.CompareTo(x.Tris));
            return list;
        }

        /// <summary>Closes an open side of a piece (a part that was hidden inside the gun: the head's open underside, the ring's top under
        /// the head, the mantlet sleeve's back end): the piece is cut by the plane dot(n, p) = w just inside that opening and put back
        /// together, with one cap on the plane facing -n (toward the opening). The surface stays where it was.</summary>
        public static Part Close(Part p, Vector3 n, float w)
        {
            if (!Split(p, n, w, out var front, out var back, true, false)) return p;
            Merge(front, back);
            return front;
        }

        /// <summary>Appends `p`'s surface to `into` (a small island that stays with its main piece).</summary>
        public static void Merge(Part into, Part p)
        {
            if (into == null || p == null) return;
            int b0 = into.v.Count; into.v.AddRange(p.v);
            for (int s = 0; s < into.sub.Length && s < p.sub.Length; s++) foreach (int i in p.sub[s]) into.sub[s].Add(b0 + i);
        }

        [System.ThreadStatic] static List<V> tPolyA, tPolyB;

        static V Lerp(in V a, in V b, float k) => new V
        {
            p = Vector3.LerpUnclamped(a.p, b.p, k), n = Vector3.LerpUnclamped(a.n, b.n, k).normalized,
            u0 = Vector2.LerpUnclamped(a.u0, b.u0, k), u1 = Vector2.LerpUnclamped(a.u1, b.u1, k), c = Color.LerpUnclamped(a.c, b.c, k),
            t = new Vector4(Mathf.LerpUnclamped(a.t.x, b.t.x, k), Mathf.LerpUnclamped(a.t.y, b.t.y, k), Mathf.LerpUnclamped(a.t.z, b.t.z, k), a.t.w)
        };

        /// <summary>Splits `src` by the plane dot(n, p) = w (mesh space): `front` = the side n points to, `back` = the other; each closed by
        /// a cap over the cut (its convex outline). A side with no surface is null. Returns false when the plane misses the piece.</summary>
        public static bool Split(Part src, Vector3 n, float w, out Part front, out Part back, bool capFront = true, bool capBack = true)
        {
            front = null; back = null;
            if (src == null) return false;
            var polyA = tPolyA ?? (tPolyA = new List<V>(8)); var polyB = tPolyB ?? (tPolyB = new List<V>(8));
            n.Normalize();
            var b = src.Box(); float eps = Mathf.Max(1e-7f, Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z)) * 1e-5f);
            int nv = src.v.Count; var d = new float[nv];
            for (int i = 0; i < nv; i++) d[i] = Vector3.Dot(n, src.v[i].p) - w;
            Part F = src.Like(), B = src.Like();
            var mapF = new Dictionary<int, int>(); var mapB = new Dictionary<int, int>();
            var cut = new List<Vector3>();
            int Keep(Part o, Dictionary<int, int> map, int i) { if (!map.TryGetValue(i, out int k)) { k = o.v.Count; o.v.Add(src.v[i]); map[i] = k; } return k; }
            for (int si = 0; si < src.sub.Length; si++)
            {
                var s = src.sub[si];
                for (int k = 0; k + 2 < s.Count; k += 3)
                {
                    int i0 = s[k], i1 = s[k + 1], i2 = s[k + 2];
                    float d0 = d[i0], d1 = d[i1], d2 = d[i2];
                    if (Mathf.Abs(d0) <= eps) cut.Add(src.v[i0].p);
                    if (Mathf.Abs(d1) <= eps) cut.Add(src.v[i1].p);
                    if (Mathf.Abs(d2) <= eps) cut.Add(src.v[i2].p);
                    if (d0 >= -eps && d1 >= -eps && d2 >= -eps) { F.sub[si].Add(Keep(F, mapF, i0)); F.sub[si].Add(Keep(F, mapF, i1)); F.sub[si].Add(Keep(F, mapF, i2)); continue; }
                    if (d0 <= eps && d1 <= eps && d2 <= eps) { B.sub[si].Add(Keep(B, mapB, i0)); B.sub[si].Add(Keep(B, mapB, i1)); B.sub[si].Add(Keep(B, mapB, i2)); continue; }
                    // the plane crosses this triangle: clip it into both sides (Sutherland-Hodgman, one plane)
                    polyA.Clear(); polyB.Clear();
                    int[] ids = { i0, i1, i2 };
                    for (int e = 0; e < 3; e++)
                    {
                        int ia = ids[e], ib = ids[(e + 1) % 3];
                        var va = src.v[ia]; var vb = src.v[ib]; float da = d[ia], db = d[ib];
                        if (da >= 0f) polyA.Add(va);
                        if (da <= 0f) polyB.Add(va);
                        if ((da > 0f && db < 0f) || (da < 0f && db > 0f))
                        {
                            var x = Lerp(va, vb, da / (da - db));
                            polyA.Add(x); polyB.Add(x); cut.Add(x.p);
                        }
                    }
                    Fan(F, si, polyA); Fan(B, si, polyB);
                }
            }
            bool hasF = F.Tris > 0, hasB = B.Tris > 0;
            if (!hasF || !hasB) return false;
            if (capFront) Cap(F, cut, -n);
            if (capBack) Cap(B, cut, n);
            front = F; back = B;
            return true;
        }

        static void Fan(Part o, int si, List<V> poly)
        {
            if (poly.Count < 3) return;
            int b0 = o.v.Count; bool any = false;
            for (int i = 1; i + 1 < poly.Count; i++)
            {
                if (Vector3.Cross(poly[i].p - poly[0].p, poly[i + 1].p - poly[0].p).sqrMagnitude < 1e-16f) continue;
                if (!any) { foreach (var q in poly) o.v.Add(q); any = true; }
                o.sub[si].Add(b0); o.sub[si].Add(b0 + i); o.sub[si].Add(b0 + i + 1);
            }
        }

        /// <summary>Closes the cut of `o` with the convex outline of the cut points, facing `outward` (Unity front faces: cross(b - a, c - a)
        /// points out).</summary>
        static void Cap(Part o, List<Vector3> cut, Vector3 outward)
        {
            if (cut.Count < 3) return;
            var e1 = Vector3.Cross(outward, Mathf.Abs(outward.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            var e2 = Vector3.Cross(outward, e1);                                                          // e1 x e2 = outward
            var o0 = cut[0];
            var pts = new List<Vector2>(cut.Count);
            foreach (var c in cut) { var r = c - o0; pts.Add(new Vector2(Vector3.Dot(r, e1), Vector3.Dot(r, e2))); }
            var hull = Hull(pts);
            if (hull.Count < 3) return;
            float area = 0f; for (int i = 1; i + 1 < hull.Count; i++) area += Cross(hull[i] - hull[0], hull[i + 1] - hull[0]);
            if (area * 0.5f < 1e-10f) return;
            float depth = 0f; foreach (var c in cut) depth += Vector3.Dot(c - o0, outward); depth /= cut.Count;   // the plane (all cut points lie on it)
            int b0 = o.v.Count; var tan = new Vector4(e1.x, e1.y, e1.z, 1f);
            foreach (var h in hull)
            {
                var p = o0 + e1 * h.x + e2 * h.y + outward * depth;
                o.v.Add(new V { p = p, n = outward, u0 = h, u1 = default, c = Color.white, t = tan });
            }
            int cs = o.CapSub;
            for (int i = 1; i + 1 < hull.Count; i++) { o.sub[cs].Add(b0); o.sub[cs].Add(b0 + i); o.sub[cs].Add(b0 + i + 1); }   // CCW in (e1, e2) = facing outward
        }

        static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        /// <summary>Andrew's monotone chain, counter-clockwise, no repeated or collinear points.</summary>
        static List<Vector2> Hull(List<Vector2> pts)
        {
            pts.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            var h = new List<Vector2>(pts.Count + 1);
            float tol = 0f; if (pts.Count > 1) { var span = pts[pts.Count - 1] - pts[0]; tol = Mathf.Max(1e-14f, span.sqrMagnitude * 1e-12f); }
            for (int pass = 0; pass < 2; pass++)
            {
                int start = h.Count;
                for (int k = 0; k < pts.Count; k++)
                {
                    var p = pass == 0 ? pts[k] : pts[pts.Count - 1 - k];
                    while (h.Count >= start + 2 && Cross(h[h.Count - 1] - h[h.Count - 2], p - h[h.Count - 2]) <= tol) h.RemoveAt(h.Count - 1);
                    h.Add(p);
                }
                h.RemoveAt(h.Count - 1);
            }
            return h;
        }

        /// <summary>The piece as a mesh (its source's object space). The cap sub-mesh is dropped when the piece has no cap.</summary>
        public static Mesh ToMesh(Part p, string name)
        {
            int subs = p.CapTris > 0 ? p.sub.Length : p.sub.Length - 1;
            var m = new Mesh { name = name, indexFormat = p.v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            var pos = new List<Vector3>(p.v.Count); var nrm = new List<Vector3>(p.v.Count); var u0 = new List<Vector2>(p.v.Count); var u1 = new List<Vector2>(p.v.Count);
            var col = new List<Color>(p.v.Count); var tan = new List<Vector4>(p.v.Count);
            foreach (var q in p.v) { pos.Add(q.p); nrm.Add(q.n); u0.Add(q.u0); u1.Add(q.u1); col.Add(q.c); tan.Add(q.t); }
            m.SetVertices(pos); m.SetNormals(nrm); m.SetUVs(0, u0);
            if (p.h1) m.SetUVs(1, u1);
            if (p.hc) m.SetColors(col);
            if (p.ht) m.SetTangents(tan);
            m.subMeshCount = subs;
            for (int s = 0; s < subs; s++) m.SetTriangles(p.sub[s], s, false);
            if (!p.hn) m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }
    }
}
