using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// crane_front_reach: the REAL surfaces of one building, for the crane's work spots, the weld contact test and RulesView's
    /// fallback weld sparks. Source: the building's own colliders (a MeshCollider with a readable mesh, a BoxCollider), else the
    /// readable meshes of its enabled MeshRenderers (glTFast keeps them readable). Never the renderer AABB, so a tower, a recessed
    /// <para>World-space triangles, built once per building and rebuilt after 30 s or when the building is destroyed / revived
    /// (ruin vs standing). Rays are Moller-Trumbore with a per-triangle box reject; a crane's spot search first gathers the triangles
    /// inside its reach (<see cref="Gather"/>). Built per event (job start / spot pick), never per frame. View only.</para>
    /// </summary>
    public sealed class BuildingSurface
    {
        public static int Built, FacadeHits, FacadeMiss;
        public static float LastBuildMs;
        /// <summary>The last built surface: building, triangles and source (Play check: "mesh(es)" or "collider(s)", never "none").</summary>
        public static string LastSource = "-";
        static readonly Dictionary<int, BuildingSurface> cache = new Dictionary<int, BuildingSurface>();
        static readonly List<Vector3> vbuf = new List<Vector3>(4096), tbuf = new List<Vector3>(16384);
        static readonly List<int> ibuf = new List<int>(8192);
        static ZGame owner;

        public readonly int Id;
        public float BuiltAt;
        public bool Ruin;
        public string Source = "none";
        Vector3[] v0, e1, e2, lo, hi;
        int n;
        Bounds box;
        readonly List<int> near = new List<int>(1024);
        bool useNear;

        public int Triangles => n;
        public Bounds Box => box;

        BuildingSurface(int id) { Id = id; }

        /// <summary>The cached surface of building `b` (null = no game / building).</summary>
        public static BuildingSurface Of(ZGame game, ZObject b)
        {
            if (game == null || b == null) return null;
            if (owner != game) { cache.Clear(); owner = game; }                                     // a new level: ids repeat
            if (cache.TryGetValue(b.Id, out var s) && s != null && s.Ruin == b.Destroyed && Time.time >= s.BuiltAt && Time.time - s.BuiltAt < 30f) return s;
            s = Build(game, b); cache[b.Id] = s;
            return s;
        }

        static BuildingSurface Build(ZGame game, ZObject b)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var s = new BuildingSurface(b.Id) { BuiltAt = Time.time, Ruin = b.Destroyed };
            Built++;
            tbuf.Clear();
            int parts = 0;
            if (game.StaticViews.TryGetValue(b.Id, out var g) && g)
            {
                foreach (var c in g.GetComponentsInChildren<Collider>())
                {
                    if (!c.enabled || c.isTrigger || c.attachedRigidbody) continue;                    // debris chunks fly on rigidbodies
                    if (c is MeshCollider mc && mc.sharedMesh && mc.sharedMesh.isReadable) { AddMesh(mc.sharedMesh, mc.transform.localToWorldMatrix); parts++; }
                    else if (c is BoxCollider bc) { AddBox(bc); parts++; }
                }
                if (parts > 0) s.Source = parts + " collider(s)";
                else
                {
                    foreach (var r in g.GetComponentsInChildren<MeshRenderer>())
                    {
                        if (!r.enabled || r.GetComponentInParent<Rigidbody>()) continue;
                        var mf = r.GetComponent<MeshFilter>(); var m = mf ? mf.sharedMesh : null;
                        if (!m || !m.isReadable) continue;
                        AddMesh(m, r.transform.localToWorldMatrix); parts++;
                    }
                    s.Source = parts > 0 ? parts + " mesh(es)" : "none (no collider, no readable mesh)";
                }
            }
            s.Set(tbuf);
            tbuf.Clear();
            LastBuildMs = (float)sw.Elapsed.TotalMilliseconds;
            LastSource = $"{b.Type}#{b.Id} {s.n} tris from {s.Source}";
            return s;
        }

        static void AddMesh(Mesh m, Matrix4x4 w)
        {
            vbuf.Clear(); m.GetVertices(vbuf);
            for (int sm = 0; sm < m.subMeshCount; sm++)
            {
                if (m.GetTopology(sm) != MeshTopology.Triangles) continue;
                ibuf.Clear(); m.GetTriangles(ibuf, sm);
                for (int i = 0; i + 2 < ibuf.Count; i += 3)
                {
                    tbuf.Add(w.MultiplyPoint3x4(vbuf[ibuf[i]]));
                    tbuf.Add(w.MultiplyPoint3x4(vbuf[ibuf[i + 1]]));
                    tbuf.Add(w.MultiplyPoint3x4(vbuf[ibuf[i + 2]]));
                }
            }
        }

        static readonly int[] boxTris = { 0, 1, 3, 0, 3, 2, 4, 6, 7, 4, 7, 5, 0, 4, 5, 0, 5, 1, 2, 3, 7, 2, 7, 6, 0, 2, 6, 0, 6, 4, 1, 5, 7, 1, 7, 3 };
        static void AddBox(BoxCollider bc)
        {
            var t = bc.transform; var h = bc.size * 0.5f; var c = bc.center;
            for (int i = 0; i < boxTris.Length; i++)
            {
                int k = boxTris[i];
                tbuf.Add(t.TransformPoint(c + new Vector3((k & 4) != 0 ? h.x : -h.x, (k & 2) != 0 ? h.y : -h.y, (k & 1) != 0 ? h.z : -h.z)));
            }
        }

        void Set(List<Vector3> t)
        {
            n = t.Count / 3;
            v0 = new Vector3[n]; e1 = new Vector3[n]; e2 = new Vector3[n]; lo = new Vector3[n]; hi = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                var a = t[3 * i]; var b = t[3 * i + 1]; var c = t[3 * i + 2];
                v0[i] = a; e1[i] = b - a; e2[i] = c - a;
                lo[i] = Vector3.Min(a, Vector3.Min(b, c)); hi[i] = Vector3.Max(a, Vector3.Max(b, c));
                if (i == 0) box = new Bounds(a, Vector3.zero);
                box.Encapsulate(lo[i]); box.Encapsulate(hi[i]);
            }
        }

        /// <summary>Limits the next rays to the triangles inside a vertical cylinder (a crane's reach); <see cref="Release"/> ends it.</summary>
        public void Gather(Vector3 c, float r, float y0, float y1)
        {
            near.Clear();
            for (int i = 0; i < n; i++)
            {
                if (hi[i].y < y0 || lo[i].y > y1) continue;
                float dx = Mathf.Max(lo[i].x - c.x, 0f, c.x - hi[i].x), dz = Mathf.Max(lo[i].z - c.z, 0f, c.z - hi[i].z);
                if (dx * dx + dz * dz > r * r) continue;
                near.Add(i);
            }
            useNear = true;
        }

        public void Release() => useNear = false;

        /// <summary>The nearest surface along o + d t (d normalised), 0 &lt; t &lt;= maxT. `nrm` faces back toward o.</summary>
        public bool Ray(Vector3 o, Vector3 d, float maxT, out float t, out Vector3 nrm)
        {
            t = maxT; nrm = Vector3.zero; int best = -1;
            if (n == 0) return false;
            var e = o + d * maxT;
            var sl = Vector3.Min(o, e) - new Vector3(1e-3f, 1e-3f, 1e-3f); var sh = Vector3.Max(o, e) + new Vector3(1e-3f, 1e-3f, 1e-3f);
            var bmin = box.min; var bmax = box.max;
            if (sh.x < bmin.x || sl.x > bmax.x || sh.y < bmin.y || sl.y > bmax.y || sh.z < bmin.z || sl.z > bmax.z) return false;
            int cnt = useNear ? near.Count : n;
            for (int k = 0; k < cnt; k++)
            {
                int i = useNear ? near[k] : k;
                var l = lo[i]; var h = hi[i];
                if (h.x < sl.x || l.x > sh.x || h.y < sl.y || l.y > sh.y || h.z < sl.z || l.z > sh.z) continue;
                var a = e1[i]; var c = e2[i];
                var p = Vector3.Cross(d, c); float det = Vector3.Dot(a, p);
                if (det > -1e-10f && det < 1e-10f) continue;
                float inv = 1f / det; var s = o - v0[i];
                float u = Vector3.Dot(s, p) * inv; if (u < 0f || u > 1f) continue;
                var q = Vector3.Cross(s, a); float v = Vector3.Dot(d, q) * inv; if (v < 0f || u + v > 1f) continue;
                float tt = Vector3.Dot(c, q) * inv; if (tt <= 1e-4f || tt >= t) continue;
                t = tt; best = i;
            }
            if (best < 0) { t = maxT; return false; }
            nrm = Vector3.Cross(e1[best], e2[best]).normalized;
            if (Vector3.Dot(nrm, d) > 0f) nrm = -nrm;
            return true;
        }

        /// <summary>true = the segment a-b passes through the building.</summary>
        public bool Blocks(Vector3 a, Vector3 b)
        {
            var d = b - a; float len = d.magnitude;
            return len > 1e-4f && Ray(a, d / len, len, out _, out _);
        }

        /// <summary>RulesView's building-side weld sparks (the arm cannot reach the building, the crane is driving, or there is no
        /// arm kit): `p` moves onto the building's real face seen from `side` (horizontal ray from outside; lower if it passes over
        /// the roof line; else down onto the roof / deck / rubble) and `side` becomes that face's normal. false = no surface found
        /// (p unchanged).</summary>
        public static bool OnFacade(ZGame game, ZObject b, ref Vector3 p, ref Vector3 side)
        {
            var s = Of(game, b);
            var dir = side; dir.y = 0f;
            if (s == null || s.n == 0 || dir.sqrMagnitude < 1e-4f) { FacadeMiss++; return false; }
            s.useNear = false;
            dir.Normalize();
            float gy = game.GroundY(p.x, p.z);
            for (int k = 0; k < 2; k++)
            {
                var o = p + dir * 14f; o.y = k == 0 ? p.y : gy + 1.2f;
                if (s.Ray(o, -dir, 28f, out float t, out var nr)) { p = o - dir * t + nr * 0.03f; side = nr; FacadeHits++; return true; }
            }
            var top = new Vector3(p.x, s.box.max.y + 1f, p.z);
            if (s.Ray(top, Vector3.down, s.box.size.y + 2f, out float td, out var nd)) { p = top + Vector3.down * td + nd * 0.03f; side = nd; FacadeHits++; return true; }
            FacadeMiss++;
            return false;
        }

        public static string Report() =>
            $"BuildingSurface: built {Built} (last {LastSource}, {LastBuildMs:F1} ms), fallback sparks on a real face {FacadeHits}, no face found {FacadeMiss}";
    }
}
