using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Vehicle wreck pieces, on Unity physics like RockPillar / BuildingBreaker:
    ///  * pieces = the wreck's rigid sub-assemblies: renderers grouped by their parent joint (turret, pods, arms, legs, wheels,
    ///    launcher boxes, barrels), the largest group absorbing the smallest until at most 12 remain;
    ///    a single-mesh imported model (APC, crane, cannon bases) uses the fragments OpenZ/Deaths/Bake Wreck Fragments cut once in the
    ///    editor (bins: x halves x z halves x 3 height layers), so the break works in players too;
    ///  * each piece a rigid body (box collider on the debris layer, mass = volume x 5 clamped .8-160 like pieceFrom, x 10),
    ///    launch = destroy():156-164: away from the centre x 5.8 x (.24 + .92 / (1 + d / reach)), up 3.8 + i%3 x .65 + 1.5,
    ///    barrels / turrets / launchers / pods +8 up and +-2.4 sideways, spin (sin a 4.3, 1.4 cos a, -cos a 4.3), friction .9,
    ///    restitution .12, damping .28 / .95; a cook-off throws at 55 %;
    ///  * the three heaviest pieces keep burning for 3.5-6 s (fire, light, then smoke), first contacts clank (dst_debris_metal,
    /// <see cref="MaxLive"/>
    ///    live (the oldest leaves early). Big parts (turret, barrels, launchers, pods) whoosh as they fly (2 per break) and heavy
    ///    parts crash with a metal clang and dust when they land on screen.
    /// </summary>
    public class WreckPieces : MonoBehaviour
    {
        public const int MaxGroups = 12, MaxLive = 72;
        static readonly List<WreckPieces> live = new List<WreckPieces>();
        static readonly Regex Launchers = new Regex("barrel|turret|cannon|launcher|pod|rocket|gun|spin|rack|boom|arm", RegexOptions.IgnoreCase);

        /// <summary>Sinking out at the end of its life (DebrisAudit skips it).</summary>
        public bool Fading => popping;
        Rigidbody rb; float age, life, burnUntil, smokeT, lastContact = -10f, fadeT, sinkDepth, massKg; int contacts, id; bool frozen, popping, landed; Vector3 sinkFrom;

        /// <summary>Breaks `view`'s wreck (everything under it except `skip`) into pieces. Returns the number of pieces.</summary>
        public static int Break(UnitView view, Transform skip, Vector3 centre, float power, int id, float charred)
        {
            var root = view.transform;
            var groups = new List<List<Transform>>();
            var byKey = new Dictionary<Transform, List<Transform>>();
            foreach (var mr in root.GetComponentsInChildren<MeshRenderer>())
            {
                if (!mr.enabled || !mr.TryGetComponent<MeshFilter>(out var mf) || !mf.sharedMesh) continue;
                if (skip && mr.transform.IsChildOf(skip)) continue;
                if (ApcCraneKit.IsKitHull(mr)) continue;
                var n = mr.name.ToLowerInvariant(); if (n.Contains("shadow") || n.Contains("label")) continue;
                string type = view.Obj != null ? view.Obj.Type : null;
                if (mr.name.StartsWith("WheelSpin_OZ") && mr.transform.parent && mr.transform.parent.Find("OZ_WreckFragments") && !WheelSplit.Strips(type)) { mr.enabled = false; continue; }   // vehicles_nb_v1 NB23: the baked fragments already hold the wheels
                if (mr.sharedMaterial && mr.sharedMaterial.renderQueue >= 2450) { mr.enabled = false; continue; }   // light cones, glows, decals: not debris
                if (mr.name == "OZ_HoleCore" || mr.name == "OZ_AxleStub") { mr.enabled = false; continue; }
                if (mr.transform.parent && mr.transform.parent.name == "OZ_WreckFragments")
                {
                    Chunkify(mr.transform); groups.Add(new List<Transform> { mr.transform }); continue;
                }
                bool wheel = WheeledBreakup.IsWheelName(mr.name);
                if (wheel) RollingWheel.BurnTyre(mr.transform);
                var frag = wheel ? null : mr.transform.Find("OZ_WreckFragments");
                if (frag)
                {   // baked fragments of a one-piece model: each its own piece
                    mr.enabled = false; frag.gameObject.SetActive(true);
                    var srcB = mf.sharedMesh.bounds; bool strip = WheelSplit.Strips(type) && mr.transform.Find("WheelSpin_OZ_0");
                    foreach (Transform f in frag)
                    {
                        if (strip && f.TryGetComponent<MeshFilter>(out var fmf) && fmf.sharedMesh) fmf.sharedMesh = WheelSplit.WreckFragment(fmf.sharedMesh, type, srcB);   // the tyres are their own WheelSpin_OZ pieces
                        Chunkify(f); groups.Add(new List<Transform> { f });
                    }
                    continue;
                }
                var key = wheel ? mr.transform : mr.transform.parent && mr.transform.parent != root && mr.transform.parent.parent != root ? mr.transform.parent : mr.transform;
                if (!byKey.TryGetValue(key, out var g)) { g = new List<Transform>(); byKey[key] = g; groups.Add(g); }
                g.Add(mr.transform);
            }
            if (groups.Count == 0) return 0;
            // largest first, the smallest merged into the largest until the budget holds
            groups.Sort((a, b) => Verts(b).CompareTo(Verts(a)));
            while (groups.Count > MaxGroups) { groups[0].AddRange(groups[groups.Count - 1]); groups.RemoveAt(groups.Count - 1); }
            var made = new List<(WreckPieces p, float mass)>(); int whooshes = 0;
            float reach = 2f;
            var all = WorldBounds(root, skip); if (all.size.sqrMagnitude > 0f) reach = Mathf.Max(2f, Mathf.Max(all.size.x, Mathf.Max(all.size.y, all.size.z)) * 0.55f);
            for (int i = 0; i < groups.Count; i++)
            {
                var g = groups[i];
                var b = new Bounds(); bool any = false;
                foreach (var t in g) foreach (var r in t.GetComponents<Renderer>()) { if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
                if (!any || b.size.magnitude < 0.05f) continue;
                var go = new GameObject("OZ_WreckPiece");
                go.transform.position = b.center;
                string names = "";
                foreach (var t in g) { t.SetParent(go.transform, true); names += t.name + " "; }
                var col = new GameObject("Hull") { layer = BuildingBreaker.DebrisLayer };
                col.transform.SetParent(go.transform, false);
                var bc = col.AddComponent<BoxCollider>(); bc.size = Vector3.Max(b.size * 0.92f, Vector3.one * 0.12f); bc.sharedMaterial = WreckFx.Material;
                var body = go.AddComponent<Rigidbody>();
                float mass = Mathf.Clamp(b.size.x * b.size.y * b.size.z * 5f, 0.8f, 160f);
                body.mass = mass * 10f; body.linearDamping = 0.28f; body.angularDamping = 0.95f; body.maxAngularVelocity = 16f;
                body.interpolation = RigidbodyInterpolation.Interpolate; body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                var offset = b.center - centre; float len = Mathf.Max(0.001f, new Vector2(offset.x, offset.z).magnitude); if (len < 0.01f) { offset.x = 1f; len = 1f; }
                float angle = i * 2.399f + centre.x * 0.13f;
                float pw = 5.8f * (0.24f + 0.92f / (1f + offset.magnitude / reach)) * power;
                var v = new Vector3(offset.x / len * pw + Mathf.Cos(angle) * 0.65f,
                                    (3.8f + i % 3 * 0.65f + 1.5f + Mathf.Clamp(offset.y / Mathf.Max(1f, offset.magnitude), -0.3f, 0.8f) * 2f) * Mathf.Lerp(0.7f, 1f, power),
                                    offset.z / len * pw + Mathf.Sin(angle) * 0.65f);
                if (Launchers.IsMatch(names)) { v.y += (8f + i % 3 * 1.6f) * power; v.x += Mathf.Sin(angle) * 2.4f; v.z += Mathf.Cos(angle) * 2.4f; }
                body.linearVelocity = v;
                body.angularVelocity = new Vector3(Mathf.Sin(angle) * 4.3f, 1.4f * Mathf.Cos(angle), -Mathf.Cos(angle) * 4.3f);
                foreach (var r in go.GetComponentsInChildren<Renderer>()) r.motionVectorGenerationMode = MotionVectorGenerationMode.Object;
                var p = go.AddComponent<WreckPieces>();
                p.rb = body; p.id = id; p.life = Lifetime(id, i); p.massKg = mass;
                if (whooshes < 2 && Launchers.IsMatch(names)) { whooshes++; AudioDirector.Fire("dst_turret_toss", b.center, id); }   // the big parts whoosh up
                made.Add((p, mass));
                live.Add(p);
            }
            made.Sort((a, b) => b.mass.CompareTo(a.mass));
            for (int i = 0; i < made.Count && i < 3; i++) made[i].p.burnUntil = 3.5f + ((RobotDeathRule.Hash(id, i, 21u) & 255u) / 255f) * 2.5f;   // burnPieces
            while (live.Count > MaxLive) { var old = live[0]; live.RemoveAt(0); if (old) old.Retire(); }
            AudioDirector.Fire("dst_debris_metal", centre, id);
            return made.Count;
        }

        /// <summary>
        /// (lower than the turret it leaves), same body, clanks, settle and sink as Break's pieces.</summary>
        public static bool Single(Transform part, Vector3 centre, float power, int id)
        {
            if (!part) return false;
            var b = new Bounds(); bool any = false;
            foreach (var r in part.GetComponentsInChildren<Renderer>()) { if (!r.enabled) continue; if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
            if (!any || b.size.magnitude < 0.05f) return false;
            var go = new GameObject("OZ_WreckPiece");
            go.transform.position = b.center;
            part.SetParent(go.transform, true);
            var col = new GameObject("Hull") { layer = BuildingBreaker.DebrisLayer };
            col.transform.SetParent(go.transform, false);
            var bc = col.AddComponent<BoxCollider>(); bc.size = Vector3.Max(b.size * 0.92f, Vector3.one * 0.12f); bc.sharedMaterial = WreckFx.Material;
            var body = go.AddComponent<Rigidbody>();
            float mass = Mathf.Clamp(b.size.x * b.size.y * b.size.z * 5f, 0.8f, 160f);
            body.mass = mass * 10f; body.linearDamping = 0.28f; body.angularDamping = 0.95f; body.maxAngularVelocity = 16f;
            body.interpolation = RigidbodyInterpolation.Interpolate; body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            var off = b.center - centre; off.y = 0f; if (off.sqrMagnitude < 1e-4f) off = Vector3.right; off.Normalize();
            float a = (RobotDeathRule.Hash(id, 7, 23u) & 0xFFFFu) / 65535f * 6.2831853f;
            body.linearVelocity = (off * Random.Range(2.4f, 3.6f) + Vector3.up * Random.Range(4.2f, 5.6f)) * Mathf.Lerp(0.7f, 1f, power);
            body.angularVelocity = new Vector3(Mathf.Sin(a) * 4.3f, 1.4f * Mathf.Cos(a), -Mathf.Cos(a) * 4.3f);
            foreach (var r in go.GetComponentsInChildren<Renderer>()) r.motionVectorGenerationMode = MotionVectorGenerationMode.Object;
            var p = go.AddComponent<WreckPieces>();
            p.rb = body; p.id = id; p.life = Lifetime(id, 20); p.massKg = mass;
            live.Add(p);
            while (live.Count > MaxLive) { var old = live[0]; live.RemoveAt(0); if (old) old.Retire(); }
            return true;
        }

        /// <summary>
        /// The baked fragments (OpenZWreckFragments: triangles binned by their centre) were open shells whose long
        /// triangles reached far out of their bin (spikes). Now each fragment's vertices are clamped into the box of its triangle
        /// centres (+6 %: every triangle ends at the bin wall instead of poking through it), and a rounded dark core block fills the
        /// shell (82 % of that box), so every piece reads as a closed chunky part. The clamped mesh is cached per source mesh.
        /// <para>
        /// the clamp kept the teeth. A baked bin holds every triangle WHOLE by its centre, so a quad strip that crosses a bin wall
        /// gives every other triangle to each bin: a piece showed a row of full-length black teeth (the ground between them) and
        /// its long panel triangles as spikes; clamping only moved the tips onto the box wall (thin wedges), and where the box
        /// spans the strip (a track's top and bottom run) nothing at all. Now the whole baked set is re-cut once at the real bin
        /// walls (<see cref="ClipSet"/>: every triangle of the set clipped by every bin it crosses, normals / UVs / colours /
        /// tangents interpolated), so each piece holds exactly the surface inside its bin: closed rows, no tooth, no spike.
        /// ClipAtBinWalls = false brings the old clamp back (A/B only).</para></summary>
        public static bool ChunkyFragments = true;
        public static bool ClipAtBinWalls = true;
        /// <summary>
        /// so the parts of it outside the dark core block read as thin blades / flaps / combs at the menu-flyby distance. Each cut piece is
        /// squashed toward its core: per axis, everything further out than the core's half size (82 % of the piece) keeps only this share
        /// of its outward reach. A monotonic per-axis map (shapes keep their order, nothing new intersects or becomes coplanar, the paint
        /// stays outside the core); 1 = off (A/B).</summary>
        public static float SquashOuter = 0.35f;
        public static int Squashed;
        public static int Chunked, CoresAdded, ClampedVerts, ClippedSets, ClippedPieces, ClippedTris, OwnClips;
        public static string ClipLast = "";
        static readonly Dictionary<Mesh, (Mesh m, Bounds box)> chunkCache = new Dictionary<Mesh, (Mesh, Bounds)>();
        static readonly Dictionary<Shader, Material> coreMats = new Dictionary<Shader, Material>();
        static Mesh coreMesh;

        internal static void Chunkify(Transform f)
        {
            if (!ChunkyFragments || !f || !f.TryGetComponent<MeshFilter>(out var mf) || !mf.sharedMesh) return;
            var src = mf.sharedMesh;
            if (ClipAtBinWalls && !chunkCache.ContainsKey(src) && BakedEntry(f, src, out var baked)) { chunkCache[src] = baked; BakedHits++; }   // soak_hitch_fix: cut in the editor
            if (ClipAtBinWalls && !chunkCache.ContainsKey(src)) ClipSet(f.parent);                         // wreck_teeth: the whole baked set, once
            if (ClipAtBinWalls && !chunkCache.ContainsKey(src) && src.isReadable)
            {   // a fragment outside a full set (a stripped copy, a lone piece): its own triangles CUT at its own box, never clamped
                var own = ClipOwn(src, out var ob);
                chunkCache[src] = own ? (own, ob) : (null, default);
            }
            if (!chunkCache.TryGetValue(src, out var entry))
            {
                entry = (null, default);
                if (src.isReadable)
                {
                    var v = src.vertices; var box = new Bounds(); bool any = false;
                    for (int sm = 0; sm < src.subMeshCount; sm++)
                    {
                        var tri = src.GetTriangles(sm);
                        for (int i = 0; i + 2 < tri.Length; i += 3)
                        {
                            var c = (v[tri[i]] + v[tri[i + 1]] + v[tri[i + 2]]) / 3f;
                            if (!any) { box = new Bounds(c, Vector3.zero); any = true; } else box.Encapsulate(c);
                        }
                    }
                    if (any)
                    {
                        float big = Mathf.Max(box.size.x, Mathf.Max(box.size.y, box.size.z));
                        box.Expand(box.size * 0.06f + Vector3.one * big * 0.02f);
                        Vector3 lo = box.min, hi = box.max; int moved = 0;
                        for (int i = 0; i < v.Length; i++)
                        {
                            var q = Vector3.Min(hi, Vector3.Max(lo, v[i]));
                            if ((q - v[i]).sqrMagnitude > 1e-10f) { v[i] = q; moved++; }
                        }
                        var m = Instantiate(src); m.name = src.name + "_chunky"; m.vertices = v; m.RecalculateBounds();
                        ClampedVerts += moved;
                        entry = (m, box);
                    }
                }
                chunkCache[src] = entry;
            }
            if (!entry.m) return;
            mf.sharedMesh = entry.m; Chunked++;
            if (!f.TryGetComponent<MeshRenderer>(out var mr) || !mr.sharedMaterial) return;
            var core = new GameObject("OZ_ChunkCore");
            core.transform.SetParent(f, false);
            core.transform.localPosition = entry.box.center;
            core.transform.localScale = Vector3.Max(entry.box.size * 0.82f, Vector3.one * 1e-3f);
            core.AddComponent<MeshFilter>().sharedMesh = CoreMesh();
            var cr = core.AddComponent<MeshRenderer>(); cr.sharedMaterial = CoreMat(mr.sharedMaterial);
            cr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            CoresAdded++;
        }

        // ------------------------------------------------------------------ wreck_teeth: cut at the bin walls
        struct CV { public Vector3 p, n; public Vector2 u0, u1; public Color c; public Vector4 t; }
        sealed class Src { public Vector3[] v, n; public Vector2[] u0, u1; public Color[] c; public Vector4[] t; public int[][] tri; }
        static readonly List<CV> clipA = new List<CV>(16), clipB = new List<CV>(16);

        static Src Read(Mesh m)
        {
            var s = new Src { v = m.vertices, n = m.normals, u0 = m.uv, u1 = m.uv2, c = m.colors, t = m.tangents, tri = new int[m.subMeshCount][] };
            for (int k = 0; k < m.subMeshCount; k++) s.tri[k] = m.GetTriangles(k);
            return s;
        }

        static CV Corner(Src s, int i, bool hn, bool h0, bool h1, bool hc, bool ht) => new CV
        {
            p = s.v[i], n = hn ? s.n[i] : default, u0 = h0 ? s.u0[i] : default, u1 = h1 ? s.u1[i] : default,
            c = hc ? s.c[i] : default, t = ht ? s.t[i] : default
        };

        static CV Lerp(in CV a, in CV b, float k) => new CV
        {
            p = Vector3.LerpUnclamped(a.p, b.p, k), n = Vector3.LerpUnclamped(a.n, b.n, k), u0 = Vector2.LerpUnclamped(a.u0, b.u0, k),
            u1 = Vector2.LerpUnclamped(a.u1, b.u1, k), c = Color.LerpUnclamped(a.c, b.c, k),
            t = new Vector4(Mathf.LerpUnclamped(a.t.x, b.t.x, k), Mathf.LerpUnclamped(a.t.y, b.t.y, k), Mathf.LerpUnclamped(a.t.z, b.t.z, k), a.t.w)
        };

        /// <summary>Sutherland-Hodgman: keeps the part of `inp` with sign x (p[axis] - val) >= 0.</summary>
        static void ClipPlane(List<CV> inp, List<CV> outp, int axis, float val, float sign)
        {
            outp.Clear(); int n = inp.Count;
            for (int i = 0; i < n; i++)
            {
                var a = inp[i]; var b = inp[(i + 1) % n];
                float da = sign * (a.p[axis] - val), db = sign * (b.p[axis] - val);
                if (da >= 0f) outp.Add(a);
                if ((da >= 0f) != (db >= 0f)) outp.Add(Lerp(a, b, da / (da - db)));
            }
        }

        sealed class Build
        {
            public readonly List<Vector3> v = new List<Vector3>(), n = new List<Vector3>(); public readonly List<Vector2> u0 = new List<Vector2>(), u1 = new List<Vector2>();
            public readonly List<Color> c = new List<Color>(); public readonly List<Vector4> t = new List<Vector4>(); public List<int>[] sub; public int tris;
            public Build(int subs) { sub = new List<int>[subs]; for (int k = 0; k < subs; k++) sub[k] = new List<int>(); }
            public void Add(in CV q) { v.Add(q.p); n.Add(q.n); u0.Add(q.u0); u1.Add(q.u1); c.Add(q.c); t.Add(q.t); }
            public Mesh ToMesh(string name, bool hn, bool h0, bool h1, bool hc, bool ht)
            {
                if (tris == 0) return null;
                var m = new Mesh { name = name, indexFormat = v.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
                m.SetVertices(v);
                if (hn) { for (int i = 0; i < n.Count; i++) n[i] = n[i].sqrMagnitude > 1e-12f ? n[i].normalized : Vector3.up; m.SetNormals(n); }
                if (h0) m.SetUVs(0, u0); if (h1) m.SetUVs(1, u1); if (hc) m.SetColors(c); if (ht) m.SetTangents(t);
                m.subMeshCount = sub.Length;
                for (int k = 0; k < sub.Length; k++) m.SetTriangles(sub[k], k, false);
                if (!hn) m.RecalculateNormals();
                m.RecalculateBounds();
                return m;
            }
        }

        /// <summary>Clips triangle (a, b, c) of sub-mesh `sm` to [lo, hi] and fans the rest into `o` (slivers under 1e-10 m2 dropped).</summary>
        static void ClipTri(in CV a, in CV b, in CV c, int sm, Vector3 lo, Vector3 hi, Build o)
        {
            Vector3 mn = Vector3.Min(a.p, Vector3.Min(b.p, c.p)), mx = Vector3.Max(a.p, Vector3.Max(b.p, c.p));
            if (mx.x < lo.x || mx.y < lo.y || mx.z < lo.z || mn.x > hi.x || mn.y > hi.y || mn.z > hi.z) return;
            clipA.Clear(); clipA.Add(a); clipA.Add(b); clipA.Add(c);
            bool inside = mn.x >= lo.x && mn.y >= lo.y && mn.z >= lo.z && mx.x <= hi.x && mx.y <= hi.y && mx.z <= hi.z;
            if (!inside)
            {
                for (int ax = 0; ax < 3 && clipA.Count >= 3; ax++)
                {
                    ClipPlane(clipA, clipB, ax, lo[ax], 1f); ClipPlane(clipB, clipA, ax, hi[ax], -1f);
                }
                if (clipA.Count < 3) return;
            }
            int b0 = o.v.Count;
            for (int i = 0; i < clipA.Count; i++) o.Add(clipA[i]);
            for (int i = 1; i + 1 < clipA.Count; i++)
            {
                if (Vector3.Cross(clipA[i].p - clipA[0].p, clipA[i + 1].p - clipA[0].p).sqrMagnitude < 4e-20f) continue;
                o.sub[sm].Add(b0); o.sub[sm].Add(b0 + i); o.sub[sm].Add(b0 + i + 1); o.tris++;
            }
        }

        /// <summary>OpenZWreckFragments' bin key of a point in the set's bounds (x half + 2 z half + 4 layer).</summary>
        static int BinKey(Vector3 p, Bounds u)
        {
            var s = Vector3.Max(u.size, Vector3.one * 1e-4f); var q = p - u.min;
            return (q.x / s.x > 0.5f ? 1 : 0) + (q.z / s.z > 0.5f ? 2 : 0) + Mathf.Clamp(Mathf.FloorToInt(q.y / s.y * 3f), 0, 2) * 4;
        }

        /// <summary>wreck_teeth: each piece's bake bin in bounds `u`: at least 98 % of its triangle centres in one bin, one piece per bin.</summary>
        static bool BinsOf(Src[] srcs, Bounds u, int[] keys)
        {
            var used = new HashSet<int>(); var votes = new int[12];
            for (int i = 0; i < srcs.Length; i++)
            {
                System.Array.Clear(votes, 0, 12); int n = 0; var s = srcs[i];
                foreach (var tri in s.tri)
                    for (int j = 0; j + 2 < tri.Length; j += 3) { votes[BinKey((s.v[tri[j]] + s.v[tri[j + 1]] + s.v[tri[j + 2]]) / 3f, u)]++; n++; }
                int best = 0; for (int k = 1; k < 12; k++) if (votes[k] > votes[best]) best = k;
                if (n == 0 || votes[best] < n * 0.98f || !used.Add(best)) return false;
                keys[i] = best;
            }
            return true;
        }

        /// <summary>wreck_teeth: area of `s`'s surface inside [lo, hi].</summary>
        static float AreaIn(Src s, Vector3 lo, Vector3 hi)
        {
            float a = 0f;
            foreach (var tri in s.tri)
                for (int q = 0; q + 2 < tri.Length; q += 3)
                {
                    Vector3 p0 = s.v[tri[q]], p1 = s.v[tri[q + 1]], p2 = s.v[tri[q + 2]];
                    Vector3 mn = Vector3.Min(p0, Vector3.Min(p1, p2)), mx = Vector3.Max(p0, Vector3.Max(p1, p2));
                    if (mx.x < lo.x || mx.y < lo.y || mx.z < lo.z || mn.x > hi.x || mn.y > hi.y || mn.z > hi.z) continue;
                    clipA.Clear(); clipA.Add(new CV { p = p0 }); clipA.Add(new CV { p = p1 }); clipA.Add(new CV { p = p2 });
                    for (int ax = 0; ax < 3 && clipA.Count >= 3; ax++) { ClipPlane(clipA, clipB, ax, lo[ax], 1f); ClipPlane(clipB, clipA, ax, hi[ax], -1f); }
                    for (int i = 1; i + 1 < clipA.Count; i++) a += Vector3.Cross(clipA[i].p - clipA[0].p, clipA[i + 1].p - clipA[0].p).magnitude * 0.5f;
                }
            return a;
        }

        static void BinBox(int key, Bounds u, out Vector3 lo, out Vector3 hi)
        {
            Vector3 mn = u.min, s = u.size; int ly = key >> 2;
            lo = new Vector3((key & 1) == 0 ? float.NegativeInfinity : mn.x + s.x * 0.5f, ly == 0 ? float.NegativeInfinity : mn.y + s.y * ly / 3f, (key & 2) == 0 ? float.NegativeInfinity : mn.z + s.z * 0.5f);
            hi = new Vector3((key & 1) == 0 ? mn.x + s.x * 0.5f : float.PositiveInfinity, ly == 2 ? float.PositiveInfinity : mn.y + s.y * (ly + 1) / 3f, (key & 2) == 0 ? mn.z + s.z * 0.5f : float.PositiveInfinity);
        }

        /// <summary>wreck_teeth: re-cuts the whole baked set under `holder` (OZ_WreckFragments, frag_* children on identity) at the
        /// real bin walls and caches each fragment's piece. Leaves the cache alone when the set is not complete / readable.</summary>
        // ------------------------------------------------------------------ soak_hitch_fix: the cut baked in the editor
        /// <summary>soak_hitch_fix: counters for the soak's hitch rows (baked entries used, runtime cuts made and the worst one).</summary>
        public static int BakedHits, RuntimeClipSets; public static float RuntimeClipMsMax;

        /// <summary>soak_hitch_fix: the piece the editor bake stored for `src` (a frag_ under a holder carrying <see cref="WreckClipBaked"/>
        /// made with the current SquashOuter). The entry is exactly what ClipSet would cache: the cut mesh (null = the cut left nothing)
        /// and the core box (the cut piece's bounds before the squash).</summary>
        static bool BakedEntry(Transform f, Mesh src, out (Mesh m, Bounds box) e)
        {
            e = default;
            var holder = f ? f.parent : null;
            if (!holder || !holder.TryGetComponent<WreckClipBaked>(out var bk) || !bk.Matches(SquashOuter) || !bk.SameModel(holder)) return false;
            return bk.TryGet(src, out e);
        }

        /// <summary>soak_hitch_fix: the editor already cut this holder's set for the current SquashOuter and every frag_ still shows the
        /// mesh it was cut from (a stripped / swapped fragment sends the whole set back to the runtime cut, as before).</summary>
        public static bool IsBaked(Transform holder)
        {
            if (!holder || !holder.TryGetComponent<WreckClipBaked>(out var bk) || !bk.Matches(SquashOuter) || !bk.SameModel(holder)) return false;
            foreach (Transform c in holder)
                if (c.name.StartsWith("frag_") && c.TryGetComponent<MeshFilter>(out var cmf) && cmf.sharedMesh && !bk.Has(cmf.sharedMesh)) return false;
            return true;
        }

        /// <summary>soak_hitch_fix: the editor bake (OpenZ/Wreck/Rebake clipped fragments) runs THIS cut (same bins, same clip, same
        /// squash) and saves its result. Fills `src` with the holder's distinct frag_ meshes (child order) and `cut` / `boxes` with the
        /// piece the cache would hold for each. Leaves the static cache as it was. False = not a complete baked set (the runtime leaves
        /// such a set to the per-piece cut too, so nothing is stored for it).</summary>
        public static bool ClipSetForBake(Transform holder, List<Mesh> src, List<Mesh> cut, List<Bounds> boxes)
        {
            src.Clear(); cut.Clear(); boxes.Clear();
            if (!holder) return false;
            foreach (Transform c in holder)
                if (c.name.StartsWith("frag_") && c.TryGetComponent<MeshFilter>(out var cmf) && cmf.sharedMesh && !src.Contains(cmf.sharedMesh)) src.Add(cmf.sharedMesh);
            foreach (var m in src) chunkCache.Remove(m);
            int setsBefore = ClippedSets;
            ClipSetCore(holder);
            bool ok = ClippedSets > setsBefore;
            foreach (var m in src)
            {
                if (chunkCache.TryGetValue(m, out var e)) { cut.Add(e.m); boxes.Add(e.box); chunkCache.Remove(m); }
                else { cut.Add(null); boxes.Add(default); ok = false; }
            }
            return ok;
        }

        /// <summary>soak_hitch_fix: WreckClipWarmup (the level's first frames, under the briefing) cuts a set that is NOT baked, so the
        /// first break of that wreck type never pays for it mid-match. False = baked / already cut / not a set.</summary>
        public static bool Prewarm(Transform holder)
        {
            if (!ChunkyFragments || !ClipAtBinWalls || !holder || holder.name != "OZ_WreckFragments" || IsBaked(holder)) return false;
            foreach (Transform c in holder)
                if (c.name.StartsWith("frag_") && c.TryGetComponent<MeshFilter>(out var cmf) && cmf.sharedMesh && chunkCache.ContainsKey(cmf.sharedMesh)) return false;
            int before = ClippedSets; ClipSetCore(holder);
            return ClippedSets > before;
        }

        static void ClipSet(Transform holder)
        {
            if (!holder || holder.name != "OZ_WreckFragments" || IsBaked(holder)) return;                     // soak_hitch_fix: the editor already cut it
            int before = ClippedSets; var sw = System.Diagnostics.Stopwatch.StartNew();
            ClipSetCore(holder);
            if (ClippedSets == before) return;
            float ms = (float)sw.Elapsed.TotalMilliseconds; RuntimeClipSets++; if (ms > RuntimeClipMsMax) RuntimeClipMsMax = ms;
            Debug.Log($"[OpenZ Wreck] runtime clip (set not baked: run OpenZ/Wreck/Rebake clipped fragments): {ClipLast} in {ms:0.0} ms");
        }

        static void ClipSetCore(Transform holder)
        {
            if (!holder || holder.name != "OZ_WreckFragments") return;
            var meshes = new List<Mesh>();
            foreach (Transform c in holder)
            {
                if (!c.name.StartsWith("frag_") || !c.TryGetComponent<MeshFilter>(out var cmf) || !cmf.sharedMesh) continue;
                if (c.localPosition.sqrMagnitude > 1e-10f || Quaternion.Angle(c.localRotation, Quaternion.identity) > 0.01f || (c.localScale - Vector3.one).sqrMagnitude > 1e-8f) return;
                var m = cmf.sharedMesh;
                if (!m.isReadable || chunkCache.ContainsKey(m)) return;                                         // a partly processed / foreign set: per-piece path
                if (!meshes.Contains(m)) meshes.Add(m);
            }
            if (meshes.Count < 2) return;
            var srcs = new Src[meshes.Count]; var u = new Bounds(); bool any = false; int subs = 1;
            bool hn = true, h0 = true, h1 = true, hc = true, ht = true;
            for (int i = 0; i < meshes.Count; i++)
            {
                var s = srcs[i] = Read(meshes[i]); int nv = s.v.Length; subs = Mathf.Max(subs, s.tri.Length);
                hn &= s.n.Length == nv; h0 &= s.u0.Length == nv; h1 &= s.u1.Length == nv; hc &= s.c.Length == nv; ht &= s.t.Length == nv;
                foreach (var p in s.v) { if (!any) { u = new Bounds(p, Vector3.zero); any = true; } else u.Encapsulate(p); }
            }
            if (!any) return;
            // The bake binned in the SOURCE mesh's bounds (OpenZWreckFragments.Split: src.bounds). Where the bake dropped a thin bin
            // (MLRS hull: its top layer) or the source carries the whole turret's bounds (gatling / howitzer / missile-cannon bases),
            // the fragments' own extent puts the walls elsewhere: cut there, 39-68 % of those wrecks fell into bins no piece owns.
            // So the source's bounds first, the fragments' extent second; a set neither reproduces is left to the per-piece cut.
            var keys = new int[meshes.Count]; Mesh srcMesh = null;
            if (holder.parent) { var pmf = holder.parent.GetComponent<MeshFilter>(); if (pmf) srcMesh = pmf.sharedMesh; }
            if (srcMesh && srcMesh.bounds.size.sqrMagnitude > 1e-8f && BinsOf(srcs, srcMesh.bounds, keys)) u = srcMesh.bounds;
            else if (!BinsOf(srcs, u, keys)) return;                                                           // not a baked set
            // a bin no piece owns (empty, or dropped by the bake under 12 tris): its surface goes WHOLE to the piece owning most of it,
            // so nothing of the wreck is lost and that region is never split between two pieces (no teeth there either)
            var owner = new int[12]; for (int k = 0; k < 12; k++) owner[k] = -1;
            for (int i = 0; i < keys.Length; i++) owner[keys[i]] = i;
            for (int k = 0; k < 12; k++)
            {
                if (System.Array.IndexOf(keys, k) >= 0) continue;
                BinBox(k, u, out var lo, out var hi); float best = 1e-8f;
                for (int i = 0; i < srcs.Length; i++) { float a = AreaIn(srcs[i], lo, hi); if (a > best) { best = a; owner[k] = i; } }
            }
            int tris = 0;
            for (int i = 0; i < meshes.Count; i++)
            {
                var o = new Build(subs);
                for (int k = 0; k < 12; k++)
                {
                    if (owner[k] != i) continue;
                    BinBox(k, u, out var lo, out var hi);
                    for (int j = 0; j < srcs.Length; j++)
                    {
                        var s = srcs[j];
                        for (int sm = 0; sm < s.tri.Length; sm++)
                        {
                            var tri = s.tri[sm];
                            for (int q = 0; q + 2 < tri.Length; q += 3)
                                ClipTri(Corner(s, tri[q], hn, h0, h1, hc, ht), Corner(s, tri[q + 1], hn, h0, h1, hc, ht), Corner(s, tri[q + 2], hn, h0, h1, hc, ht), sm, lo, hi, o);
                        }
                    }
                }
                var m = o.ToMesh(meshes[i].name + "_clip", hn, h0, h1, hc, ht);
                var mb = m ? m.bounds : default; if (m) Squash(m, mb);                                                 // the core keeps the cut piece's own box
                chunkCache[meshes[i]] = m ? (m, mb) : (null, default);
                if (m) { ClippedPieces++; tris += o.tris; }
            }
            ClippedSets++; ClippedTris += tris;
            int heirs = 0; for (int k = 0; k < 12; k++) if (owner[k] >= 0 && System.Array.IndexOf(keys, k) < 0) heirs++;
            ClipLast = $"{(holder.parent ? holder.parent.name : "?")}: {meshes.Count} baked bins re-cut at the bin walls ({(srcMesh && u == srcMesh.bounds ? "source" : "own")} bounds, {heirs} empty bins merged), {tris} tris";
        }

        /// <summary>wreck_teeth: a fragment on its own: its triangles cut at the box of their centres (+6 % / +2 %, the old clamp box).</summary>
        static Mesh ClipOwn(Mesh src, out Bounds box)
        {
            box = default;
            var s = Read(src); int nv = s.v.Length; bool any = false;
            bool hn = s.n.Length == nv, h0 = s.u0.Length == nv, h1 = s.u1.Length == nv, hc = s.c.Length == nv, ht = s.t.Length == nv;
            foreach (var tri in s.tri)
                for (int i = 0; i + 2 < tri.Length; i += 3)
                {
                    var c = (s.v[tri[i]] + s.v[tri[i + 1]] + s.v[tri[i + 2]]) / 3f;
                    if (!any) { box = new Bounds(c, Vector3.zero); any = true; } else box.Encapsulate(c);
                }
            if (!any) return null;
            float big = Mathf.Max(box.size.x, Mathf.Max(box.size.y, box.size.z));
            box.Expand(box.size * 0.06f + Vector3.one * big * 0.02f);
            var o = new Build(Mathf.Max(1, s.tri.Length));
            for (int sm = 0; sm < s.tri.Length; sm++)
            {
                var tri = s.tri[sm];
                for (int q = 0; q + 2 < tri.Length; q += 3)
                    ClipTri(Corner(s, tri[q], hn, h0, h1, hc, ht), Corner(s, tri[q + 1], hn, h0, h1, hc, ht), Corner(s, tri[q + 2], hn, h0, h1, hc, ht), sm, box.min, box.max, o);
            }
            var m = o.ToMesh(src.name + "_clip", hn, h0, h1, hc, ht);
            if (m) { OwnClips++; box = m.bounds; Squash(m, box); }
            return m;
        }

        /// <summary><see cref="SquashOuter"/></summary>
        static void Squash(Mesh m, Bounds b)
        {
            float k = Mathf.Clamp01(SquashOuter); if (k >= 0.999f) return;
            var v = m.vertices; Vector3 c = b.center, h = b.extents * 0.82f;
            for (int i = 0; i < v.Length; i++)
            {
                var d = v[i] - c;
                for (int ax = 0; ax < 3; ax++)
                {
                    float a = Mathf.Abs(d[ax]); if (a <= h[ax]) continue;
                    d[ax] = Mathf.Sign(d[ax]) * (h[ax] + (a - h[ax]) * k);
                }
                v[i] = c + d;
            }
            m.vertices = v; m.RecalculateBounds(); Squashed++;
        }

        /// <summary>Dark burnt metal in the fragment's own shader (lit like the wreck), without its texture.</summary>
        /// <summary></summary>
        public static Color CoreDark = new Color(0.10f, 0.09f, 0.085f, 1f);
        /// <summary>Drops the cached core materials (after a CoreDark change in a Play check).</summary>
        public static void ResetCoreMats() { coreMats.Clear(); holeMats.Clear(); }

        internal static Material CoreMat(Material like)
        {
            if (coreMats.TryGetValue(like.shader, out var m) && m) return m;
            m = new Material(like) { name = "OZ_WreckChunkCore" };
            var dark = CoreDark;
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", null);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", null);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", dark);
            if (m.HasProperty("_Color")) m.SetColor("_Color", dark);
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", Color.black);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.25f);
            foreach (var p in new[] { "_TeamMode", "_Accent", "_PaintFromAlpha", "_OwnerPart" }) if (m.HasProperty(p)) m.SetFloat(p, 0f);
            if (m.HasProperty("_UnitShade")) { var us = m.GetVector("_UnitShade"); us.w = 1f; m.SetVector("_UnitShade", us); }
            coreMats[like.shader] = m;
            return m;
        }

        /// <summary></summary>
        public static Color HoleDark = new Color(0.055f, 0.048f, 0.044f, 1f);
        static readonly Dictionary<Shader, Material> holeMats = new Dictionary<Shader, Material>();
        internal static Material HoleMat(Material like)
        {
            if (holeMats.TryGetValue(like.shader, out var m) && m) return m;
            m = new Material(CoreMat(like)) { name = "OZ_WreckHoleCavity" };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", HoleDark);
            if (m.HasProperty("_Color")) m.SetColor("_Color", HoleDark);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.1f);
            holeMats[like.shader] = m;
            return m;
        }

        /// <summary>A unit rounded box (each face a 4 x 4 grid pushed onto a box with rounded edges, radius 0.3 of the half size).</summary>
        internal static Mesh CoreMesh()
        {
            if (coreMesh) return coreMesh;
            const int N = 4; const float R = 0.3f;
            var v = new List<Vector3>(); var t = new List<int>();
            Vector3[] nrm = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            foreach (var n in nrm)
            {
                var a = n.x != 0f ? Vector3.forward : Vector3.right; var b = Vector3.Cross(n, a);
                int i0 = v.Count;
                for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                    {
                        var q = n + a * (x / (N - 1f) * 2f - 1f) + b * (y / (N - 1f) * 2f - 1f);
                        var inner = Vector3.Max(-Vector3.one * (1f - R), Vector3.Min(Vector3.one * (1f - R), q));
                        var d = q - inner; var p = inner + (d.sqrMagnitude > 1e-8f ? d.normalized * R : n * R);
                        v.Add(p * 0.5f);
                    }
                for (int y = 0; y < N - 1; y++)
                    for (int x = 0; x < N - 1; x++)
                    {
                        int k = i0 + y * N + x;
                        t.Add(k); t.Add(k + N); t.Add(k + 1); t.Add(k + 1); t.Add(k + N); t.Add(k + N + 1);
                    }
            }
            // outward winding for every triangle (the face bases above are not all right-handed)
            for (int i = 0; i < t.Count; i += 3)
            {
                var c = (v[t[i]] + v[t[i + 1]] + v[t[i + 2]]) / 3f;
                if (Vector3.Dot(Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]), c) < 0f) { int sw = t[i + 1]; t[i + 1] = t[i + 2]; t[i + 2] = sw; }
            }
            coreMesh = new Mesh { name = "OZ_ChunkCoreMesh" };
            coreMesh.SetVertices(v); coreMesh.SetTriangles(t, 0);
            coreMesh.RecalculateNormals(); coreMesh.RecalculateBounds();
            return coreMesh;
        }

        /// <summary>debrisLifetime: 9 + seeded 0..8 s per piece.</summary>
        static float Lifetime(int id, int i) => 9f + (RobotDeathRule.Hash(id, i + 1, 17u) & 0xFFFFu) / 65535f * 8f;

        static int Verts(List<Transform> g)
        {
            int n = 0; foreach (var t in g) if (t.TryGetComponent<MeshFilter>(out var mf) && mf.sharedMesh) n += mf.sharedMesh.vertexCount;
            return n;
        }

        static Bounds WorldBounds(Transform root, Transform skip)
        {
            bool any = false; var b = new Bounds(root.position, Vector3.zero);
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>())
            {
                if (!r.enabled || (skip && r.transform.IsChildOf(skip))) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            return b;
        }

        void Update()
        {
            float dt = Time.deltaTime; age += dt;
            if (popping)
            {
                fadeT += dt; float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(fadeT / 2.5f));
                transform.position = sinkFrom + Vector3.down * sinkDepth * k;
                if (fadeT >= 2.5f) Destroy(gameObject);
                return;
            }
            if (burnUntil > 0f && age < burnUntil + 3f && (smokeT -= dt) <= 0f)
            {   // burning debris: fire + light, then smoke that fades
                bool burning = age < burnUntil; smokeT = burning ? 0.3f : 0.6f;
                var fxp = OpenZFxPlayer.I;
                if (fxp && fxp.isActiveAndEnabled) fxp.WreckSmoke(transform.position + Vector3.up * 0.3f, burning ? 0.6f : 0.35f, burning);
            }
            if (!frozen && rb && (rb.IsSleeping() || age > life - 1.5f || age > 8f))
            {   // settled: stop simulating (sleeps), keep the pose
                frozen = true; rb.isKinematic = true; rb.interpolation = RigidbodyInterpolation.None;
                foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
            }
            if (age > life && !popping) Retire();
        }

        /// <summary>
        /// over 2.5 s (a faint dust puff), then is destroyed.</summary>
        void Retire()
        {
            if (popping) return;
            popping = true; live.Remove(this);
            if (rb) { rb.isKinematic = true; rb.interpolation = RigidbodyInterpolation.None; }
            foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
            var b = new Bounds(transform.position, Vector3.zero); bool any = false;
            foreach (var r in GetComponentsInChildren<Renderer>()) { if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
            sinkFrom = transform.position; sinkDepth = Mathf.Max(0.3f, b.size.y * 1.05f); fadeT = 0f;
            if (WreckFx.OnScreen(transform.position)) FxManager.I?.Dust(transform.position, 0.25f);
        }

        void OnCollisionEnter(Collision c)
        {
            if (age < 0.14f || contacts >= 5 || age - lastContact < 0.3f || c.relativeVelocity.magnitude < 1.4f) return;
            lastContact = age; contacts++;
            float sp = c.relativeVelocity.magnitude;
            var at = c.contactCount > 0 ? c.GetContact(0).point : transform.position;
            if (!landed && massKg > 12f && sp > 3f && WreckFx.OnScreen(at))
            {   // a heavy part lands: metal crash + dust (only on screen)
                CombatAudio.I?.Play("turret", at, Mathf.Clamp01(0.5f + sp * 0.05f), 0, GetInstanceID(), OzSoundV3.BankMetalHeavy);   // sound_v3
                var fxp = OpenZFxPlayer.I; if (fxp && fxp.isActiveAndEnabled) fxp.Dust(at, Mathf.Min(1.2f, 0.4f + sp * 0.06f)); else FxManager.I?.Dust(at, 0.6f);
            }
            else AudioDirector.Fire("dst_debris_metal", at, id, Mathf.Clamp01(0.35f + sp * 0.06f));
            landed = true;
        }

        void OnDestroy() { live.Remove(this); }
    }
}
