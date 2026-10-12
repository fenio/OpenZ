using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// imported meshes (m06_apc.glb 19164 verts / m07_crane.glb, one primitive, ONE welded vertex island: the tyres are fused into
    /// the body), so VehicleMotion found no WheelSpin_ / Wheel_ node and only rocked the whole model.
    /// <para>Fix (view only, once per source mesh, shared by every APC / crane): the wheels are cut out of the mesh at spawn. Each
    /// fit + wheel_split.py renders, preview/split_*): a triangle belongs to wheel w when its centroid is inside the cylinder
    /// (radius Cut, |z| inside the tyre slab) and every vertex within Cut x VertexSlack of the axis. A cylinder is symmetric
    /// under its own rotation, so the spinning wheel always fills exactly the hole it was cut from (checked offline at 22.5 deg
    /// steps). The body keeps every other triangle and its original bounds (TeamPaint's panel / accent band stay where they
    /// were); each wheel becomes a child "WheelSpin_OZ_n" of the Model node, pivot on the axle, local x = the root's x, so
    /// a clone of the model's material named *_wheels with the accent band off (no team paint on tyres).</para>
    /// <para>GLB -> Unity mesh space: glTFast negates x; both signs are tried and the one that finds the measured wheels is used
    /// (at least 60 % of the offline triangle count), else nothing is cut (logged once).</para>
    /// </summary>
    public static class WheelSplit
    {
        public static bool Enabled = true;
        public const float VertexSlack = 1.12f;

        sealed class Spec { public float zi, zo; public Vector3[] wheels; public int expectTris; }   // wheels: (x, y, cut) in GLB space

        // measured on the GLBs (tools/wheel_specs.json; offline triangle counts: reports/wheel_split_counts.json)
        static readonly Dictionary<string, Spec> Specs = new Dictionary<string, Spec>
        {
            ["apc"] = new Spec { zi = 0.33f, zo = 0.64f, expectTris = 7612,
                                 wheels = new[] { new Vector3(-0.5255f, -0.2345f, 0.262f), new Vector3(0.0227f, -0.2330f, 0.262f), new Vector3(0.5818f, -0.2310f, 0.262f) } },
            ["crane"] = new Spec { zi = 0.20f, zo = 0.53f, expectTris = 3635,
                                   wheels = new[] { new Vector3(-0.3425f, -0.5400f, 0.222f), new Vector3(0.4217f, -0.5417f, 0.222f) } },
        };

        sealed class Cut
        {
            public Mesh body; public readonly List<(Mesh mesh, Vector3 centre)> wheels = new List<(Mesh, Vector3)>();
            public Quaternion rot; public Material[] wheelMats; public int tris; public float sign;
        }
        static readonly Dictionary<Mesh, Cut> cache = new Dictionary<Mesh, Cut>();
        static readonly Dictionary<Material, Material> wheelMat = new Dictionary<Material, Material>();
        static readonly HashSet<string> warned = new HashSet<string>();
        static readonly Dictionary<string, float> signOf = new Dictionary<string, float>();
        static readonly Dictionary<Mesh, Mesh> wreckFrag = new Dictionary<Mesh, Mesh>();
        static readonly Dictionary<Mesh, Bounds> ownBounds = new Dictionary<Mesh, Bounds>();
        static readonly Dictionary<Mesh, float> cutR = new Dictionary<Mesh, float>();
        static readonly Dictionary<Mesh, Mesh> flight = new Dictionary<Mesh, Mesh>();                 // cut wheel -> its closed flight mesh
        static readonly Dictionary<Mesh, float> flightR = new Dictionary<Mesh, float>();
        static readonly Dictionary<Mesh, int> flightOpen = new Dictionary<Mesh, int>();

        /// <summary>
        /// where the tyre was fused to the arch never rotate into view while driving. false = the old open cut with the model texture.</summary>
        public static bool ClosedLiving = true;
        public static string FlightLog = "";

        /// <summary>A wreck fragment's own geometry bounds (WreckFragment copies carry the whole model's bounds).</summary>
        public static Bounds FragmentBounds(Mesh m) => m && ownBounds.TryGetValue(m, out var b) ? b : (m ? m.bounds : default);

        /// <summary></summary>
        public static bool Strips(string type) => type != null && signOf.ContainsKey(type);

        /// <summary>
        /// tyres included) without the tyre triangles (they are their own WheelSpin_OZ nodes since NB23, same Classify rule), with
        /// `bounds` = the whole model's (TeamPaint's accent band / panels are measured on the mesh bounds: a fragment's own bounds
        /// would paint a band on every fragment). Cached per fragment mesh; an unreadable mesh comes back as it is.</summary>
        public static Mesh WreckFragment(Mesh frag, string type, Bounds bounds)
        {
            if (!frag) return frag;
            if (wreckFrag.TryGetValue(frag, out var m) && m) return m;
            if (wreckFrag.ContainsValue(frag) || !frag.isReadable) return frag;
            m = Object.Instantiate(frag); m.name = frag.name + "_wreck";
            if (type != null && Specs.TryGetValue(type, out var spec) && signOf.TryGetValue(type, out var sx))
            {
                var v = frag.vertices;
                for (int s = 0; s < frag.subMeshCount; s++)
                {
                    var tri = frag.GetTriangles(s); var own = Classify(v, tri, spec, sx);
                    var keep = new List<int>(tri.Length);
                    for (int t = 0; t < own.Length; t++) if (own[t] < 0) { keep.Add(tri[3 * t]); keep.Add(tri[3 * t + 1]); keep.Add(tri[3 * t + 2]); }
                    m.SetTriangles(keep, s, false);
                }
            }
            ownBounds[m] = frag.bounds; m.bounds = bounds;
            wreckFrag[frag] = m;
            return m;
        }
        static readonly int AccentFromId = Shader.PropertyToID("_AccentFrom"), AccentToId = Shader.PropertyToID("_AccentTo");

        /// <summary>
        /// APC / crane tyre is an open shell (54-70 open edges along the upper tread where it was fused to the arch, an open inner
        /// hub) and TeamLit culls back faces, so the ground showed through it. The flight mesh (cached per cut wheel, also used by the
        /// living wheels when <see cref="ClosedLiving"/>): axle = the smallest bounds axis through the wheel's own origin (the cut
        /// re-based it on the axle); triangles with a vertex beyond the cut radius x 1.02 (the arch horns) are dropped; every open
        /// boundary loop is fan-filled (tread openings as a tent at the loop's own radius, hub openings flat), so the shell is
        /// closed; 2 submeshes: 0 rubber (outer), 1 hub (centroid inside 0.5 r and the hub caps). `r` = the cut radius.</summary>
        public static Mesh FlightMesh(Mesh w, out float r)
        {
            r = 0f;
            if (!w) return w;
            if (flightR.TryGetValue(w, out r)) return w;                                                   // already a flight mesh
            if (flight.TryGetValue(w, out var done) && done) { r = flightR[done]; return done; }
            var b = w.bounds; var sz = b.size;
            int ax = sz.x <= sz.y && sz.x <= sz.z ? 0 : (sz.y <= sz.z ? 1 : 2), a1 = (ax + 1) % 3, a2 = (ax + 2) % 3;
            if (!cutR.TryGetValue(w, out r)) r = 0.5f * Mathf.Max(sz[a1], sz[a2]);
            if (!w.isReadable) return w;
            var v = w.vertices; var nr = w.normals; var uv = new List<Vector2>(); w.GetUVs(0, uv);
            bool hasN = nr != null && nr.Length == v.Length, hasUv = uv.Count == v.Length;
            float lim2 = r * 1.02f * (r * 1.02f), hub = 0.5f * r;
            System.Func<Vector3, float> rad2 = p => p[a1] * p[a1] + p[a2] * p[a2];
            // weld split vertices (UV / normal seams) so the open boundary is the real one
            var weld = new int[v.Length]; var at = new Dictionary<Vector3Int, int>();
            for (int i = 0; i < v.Length; i++)
            {
                var k = new Vector3Int(Mathf.RoundToInt(v[i].x * 20000f), Mathf.RoundToInt(v[i].y * 20000f), Mathf.RoundToInt(v[i].z * 20000f));
                if (!at.TryGetValue(k, out int j)) { j = i; at[k] = i; }
                weld[i] = j;
            }
            var keep = new List<int>(); int dropped = 0;
            for (int s = 0; s < w.subMeshCount; s++)
            {
                var t = w.GetTriangles(s);
                for (int i = 0; i + 2 < t.Length; i += 3)
                {
                    int p = t[i], q = t[i + 1], o = t[i + 2];
                    if (rad2(v[p]) > lim2 || rad2(v[q]) > lim2 || rad2(v[o]) > lim2) { dropped++; continue; }
                    if (weld[p] == weld[q] || weld[q] == weld[o] || weld[o] == weld[p]) continue;
                    keep.Add(p); keep.Add(q); keep.Add(o);
                }
            }
            // open edges (used by one triangle), directed as in their triangle
            var count = new Dictionary<long, int>();
            System.Func<int, int, long> ek = (p, q) => p < q ? (long)p * 4000000L + q : (long)q * 4000000L + p;
            for (int i = 0; i < keep.Count; i += 3)
                for (int e = 0; e < 3; e++)
                {
                    long k = ek(weld[keep[i + e]], weld[keep[i + (e + 1) % 3]]);
                    count.TryGetValue(k, out int c); count[k] = c + 1;
                }
            var next = new Dictionary<int, List<int>>(); int open = 0;
            var vOf = new Dictionary<int, int>();                                                           // welded id -> one original vertex (uv / normal)
            for (int i = 0; i < keep.Count; i += 3)
                for (int e = 0; e < 3; e++)
                {
                    int p = weld[keep[i + e]], q = weld[keep[i + (e + 1) % 3]];
                    if (count[ek(p, q)] != 1) continue;
                    if (!next.TryGetValue(p, out var l)) next[p] = l = new List<int>();
                    l.Add(q); open++; vOf[p] = keep[i + e]; vOf[q] = keep[i + (e + 1) % 3];
                }
            // chain the open edges into loops
            var loops = new List<List<int>>();
            foreach (var p0 in new List<int>(next.Keys))
                while (next[p0].Count > 0)
                {
                    var loop = new List<int> { p0 }; int cur = next[p0][0]; next[p0].RemoveAt(0); int guard = 0;
                    while (cur != p0 && guard++ < 20000)
                    {
                        loop.Add(cur);
                        if (!next.TryGetValue(cur, out var l) || l.Count == 0) break;
                        int nx = l[0]; l.RemoveAt(0); cur = nx;
                    }
                    if (loop.Count >= 3) loops.Add(loop);
                }
            // new mesh: the kept triangles + one fan per loop
            var pv = new List<Vector3>(); var pn = new List<Vector3>(); var pu = new List<Vector2>(); var map = new Dictionary<int, int>();
            var rub = new List<int>(); var hubT = new List<int>();
            System.Func<int, int> idx = vi =>
            {
                if (map.TryGetValue(vi, out int ni)) return ni;
                ni = pv.Count; map[vi] = ni; pv.Add(v[vi]); pn.Add(hasN ? nr[vi] : Vector3.zero); pu.Add(hasUv ? uv[vi] : Vector2.zero); return ni;
            };
            for (int i = 0; i < keep.Count; i += 3)
            {
                var cen = (v[keep[i]] + v[keep[i + 1]] + v[keep[i + 2]]) / 3f;
                var dst = rad2(cen) < hub * hub ? hubT : rub;
                dst.Add(idx(keep[i])); dst.Add(idx(keep[i + 1])); dst.Add(idx(keep[i + 2]));
            }
            int caps = 0;
            foreach (var loop in loops)
            {
                var c = Vector3.zero; float rr = 0f;
                foreach (int p in loop) { c += v[vOf[p]]; rr += Mathf.Sqrt(rad2(v[vOf[p]])); }
                c /= loop.Count; rr /= loop.Count;
                bool tread = rr > 0.6f * r;
                if (tread)
                {   // a tread opening: the fan centre sits on the tread at the loop's own radius (a tent, not a chord through the tyre)
                    float cr = Mathf.Sqrt(rad2(c));
                    if (cr > 1e-4f) { float k = rr / cr; c[a1] *= k; c[a2] *= k; }
                }
                // the cap's normal (Newell), oriented like its triangles (edge q -> p reversed against the open edge p -> q)
                var nrm = Vector3.zero;
                for (int i = 0; i < loop.Count; i++) { var a = v[vOf[loop[i]]]; var bb = v[vOf[loop[(i + 1) % loop.Count]]]; nrm += Vector3.Cross(bb - c, a - c); }
                nrm = nrm.sqrMagnitude > 1e-12f ? nrm.normalized : Vector3.up;
                var dst = tread ? rub : hubT;
                int ci = pv.Count; pv.Add(c); pn.Add(nrm); pu.Add(hasUv ? uv[vOf[loop[0]]] : Vector2.zero);
                int first = pv.Count;
                foreach (int p in loop) { pv.Add(v[vOf[p]]); pn.Add(nrm); pu.Add(hasUv ? uv[vOf[p]] : Vector2.zero); }
                for (int i = 0; i < loop.Count; i++)
                {
                    int a = first + i, bb = first + (i + 1) % loop.Count;
                    dst.Add(bb); dst.Add(a); dst.Add(ci);
                }
                caps++;
            }
            var m = new Mesh { name = w.name + "_flight" };
            if (pv.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(pv); m.SetNormals(pn); m.SetUVs(0, pu);
            m.subMeshCount = 2; m.SetTriangles(rub, 0, false); m.SetTriangles(hubT, 1, false);
            m.RecalculateBounds(); m.RecalculateTangents();
            // closed? count the open edges of the result (welded)
            int after = OpenEdges(m);
            flight[w] = m; flightR[m] = r; flightOpen[m] = after;
            FlightLog = $"{w.name}: r {r:F3} (bounds r {0.5f * Mathf.Max(sz[a1], sz[a2]):F3} -> {0.5f * Mathf.Max(m.bounds.size[a1], m.bounds.size[a2]):F3}), {dropped} horn tris dropped, {open} open edges in {loops.Count} loops -> {caps} caps, {after} open edges left, rubber {rub.Count / 3} / hub {hubT.Count / 3} tris";
            Debug.Log("[OpenZ] WheelSplit flight mesh " + FlightLog);
            return m;
        }

        /// <summary>Open (single-use) edges of a mesh after welding equal positions: 0 = closed.</summary>
        public static int OpenEdges(Mesh m)
        {
            if (!m || !m.isReadable) return -1;
            var v = m.vertices; var weld = new int[v.Length]; var at = new Dictionary<Vector3Int, int>();
            for (int i = 0; i < v.Length; i++)
            {
                var k = new Vector3Int(Mathf.RoundToInt(v[i].x * 20000f), Mathf.RoundToInt(v[i].y * 20000f), Mathf.RoundToInt(v[i].z * 20000f));
                if (!at.TryGetValue(k, out int j)) { j = i; at[k] = i; }
                weld[i] = j;
            }
            var count = new Dictionary<long, int>();
            for (int s = 0; s < m.subMeshCount; s++)
            {
                var t = m.GetTriangles(s);
                for (int i = 0; i + 2 < t.Length; i += 3)
                    for (int e = 0; e < 3; e++)
                    {
                        int p = weld[t[i + e]], q = weld[t[i + (e + 1) % 3]]; if (p == q) continue;
                        long k = p < q ? (long)p * 4000000L + q : (long)q * 4000000L + p;
                        count.TryGetValue(k, out int c); count[k] = c + 1;
                    }
            }
            int n = 0; foreach (var c in count.Values) if (c == 1) n++;
            return n;
        }

        static readonly Dictionary<Material, Material[]> tyreMats = new Dictionary<Material, Material[]>(), burntMats = new Dictionary<Material, Material[]>();
        static readonly int UnitShadeId = Shader.PropertyToID("_UnitShade");

        /// <summary>
        /// no sheen (_UnitShade.w 1 = TeamLit's matte opt-out: no smoothness add, no raised highlight cap, no env gloss, never
        /// "bare metal"). burnt = the wreck's burnt rubber / scorched hub; else the living tyre.</summary>
        public static Material[] TyreMats(Material like, bool burnt)
        {
            if (!like) return null;
            var cache = burnt ? burntMats : tyreMats;
            if (cache.TryGetValue(like, out var a) && a[0] && a[1]) return a;
            a = new[]
            {
                TyreMat(like, burnt ? "OZ_BurntRubber" : "OZ_TyreRubber", burnt ? new Color(0.18f, 0.17f, 0.16f) : new Color(0.21f, 0.20f, 0.19f), 0.04f, 0.009f),
                TyreMat(like, burnt ? "OZ_ScorchedHub" : "OZ_TyreHub", burnt ? new Color(0.36f, 0.34f, 0.31f) : new Color(0.50f, 0.48f, 0.44f), 0.12f, 0.02f),
            };
            cache[like] = a; return a;
        }

        static Material TyreMat(Material like, string name, Color c, float smooth, float cap)
        {
            var m = new Material(like) { name = name };
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", null);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", null);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);                                   // sRGB in, Unity stores it linear
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            foreach (var p in new[] { "_TeamMode", "_Accent", "_PaintFromAlpha", "_OwnerPart", "_Metallic" }) if (m.HasProperty(p)) m.SetFloat(p, 0f);
            if (m.HasProperty(AccentFromId)) { m.SetFloat(AccentFromId, 2f); if (m.HasProperty(AccentToId)) m.SetFloat(AccentToId, 2f); }
            if (m.HasProperty("_DarkColor")) m.SetVector("_DarkColor", new Vector4(0.045f, 0.041f, 0.037f, 1f));   // warm grey lift (linear)
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", Color.black);
            if (m.HasProperty(UnitShadeId)) { var us = m.GetVector(UnitShadeId); us.z = cap; us.w = 1f; m.SetVector(UnitShadeId, us); }
            return m;
        }

        /// <summary>
        /// width, outside the recessed hub, so the living closed tyre read as a flat black puck: now +-0.28 (inside the hub), and none
        /// at all for a flight mesh with 0 open edges (closed: nothing to back up). TyreCoreSkipped counts the closed ones.</summary>
        public static float TyreCoreHalf = 0.28f;
        public static int TyreCoreSkipped, TyreCoreMade;
        public static void TyreCore(Transform wheel, float r, Material rubber)
        {
            if (!wheel || wheel.Find("OZ_TyreCore") || !wheel.TryGetComponent<MeshFilter>(out var mf) || !mf.sharedMesh) return;
            if (flightOpen.TryGetValue(mf.sharedMesh, out int open) && open == 0) { TyreCoreSkipped++; return; }
            TyreCoreMade++;
            var sz = mf.sharedMesh.bounds.size; int ax = sz.x <= sz.y && sz.x <= sz.z ? 0 : (sz.y <= sz.z ? 1 : 2);
            var core = new GameObject("OZ_TyreCore") { layer = wheel.gameObject.layer };
            core.transform.SetParent(wheel, false);
            core.transform.localPosition = Vector3.zero;
            core.transform.localRotation = Quaternion.FromToRotation(Vector3.right, ax == 0 ? Vector3.right : ax == 1 ? Vector3.up : Vector3.forward);
            core.AddComponent<MeshFilter>().sharedMesh = RollingWheel.Cylinder(r * 0.9f, sz[ax] * TyreCoreHalf, 20);
            var cr = core.AddComponent<MeshRenderer>(); cr.sharedMaterial = rubber;
            cr.motionVectorGenerationMode = MotionVectorGenerationMode.Object;
            if (wheel.TryGetComponent<MeshRenderer>(out var wr)) { cr.shadowCastingMode = wr.shadowCastingMode; cr.receiveShadows = wr.receiveShadows; }
        }

        /// <summary>A WheelSpin_OZ tyre gets the closed flight mesh + the given rubber / hub, with empty property blocks (the TeamPaint
        /// block would override the colours) and the core backstop. Returns false for any other wheel (jeep: closed tyre meshes).</summary>
        public static bool CloseTyre(Transform wheel, bool burnt)
        {
            if (!wheel || !wheel.name.StartsWith("WheelSpin_OZ") || !wheel.TryGetComponent<MeshFilter>(out var mf) || !mf.sharedMesh || !wheel.TryGetComponent<MeshRenderer>(out var mr)) return false;
            var fm = FlightMesh(mf.sharedMesh, out float r);
            if (!fm || !flightR.ContainsKey(fm)) return false;
            mf.sharedMesh = fm;
            var mats = TyreMats(mr.sharedMaterial, burnt);
            if (mats == null) return false;
            mr.sharedMaterials = mats;
            var empty = new MaterialPropertyBlock(); mr.SetPropertyBlock(empty); mr.SetPropertyBlock(empty, 0); mr.SetPropertyBlock(empty, 1);
            var core = wheel.Find("OZ_TyreCore");
            if (core && core.TryGetComponent<MeshRenderer>(out var ccr)) { ccr.sharedMaterial = mats[0]; ccr.SetPropertyBlock(empty); }
            else TyreCore(wheel, r, mats[0]);
            return true;
        }

        /// <summary>Stats for the Play proof: vehicles split, wheels made.</summary>
        public static int Vehicles { get; private set; }
        public static int WheelsMade { get; private set; }

        /// <summary>UnitView.Bind (vehicles), before VehicleMotion scans: cuts the wheels of an APC / crane once per view. Idempotent.</summary>
        public static void Apply(GameObject go, string type)
        {
            if (ApcCraneKit.ApplyLive(go, type))
            {
                Vehicles++;
                var vk = go.GetComponent<VehicleMotion>(); if (vk) vk.Rescan();
                return;
            }
            if (!Enabled || !go || type == null || !Specs.TryGetValue(type, out var spec)) return;
            var root = go.transform;
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name.StartsWith("WheelSpin_OZ")) return;   // already split
            var model = root.Find("Model");
            var mf = model ? model.GetComponent<MeshFilter>() : null; var mr = model ? model.GetComponent<MeshRenderer>() : null;
            if (!mf || !mr || !mf.sharedMesh) { Warn(type, "no Model mesh"); return; }
            var src = mf.sharedMesh;
            if (!cache.TryGetValue(src, out var cut))
            {
                var axis = model.InverseTransformDirection(root.right);                              // the wheels' local x = the root's x
                cut = Build(src, spec, axis, type);
                cache[src] = cut;
            }
            if (cut == null || !cut.body) return;
            mf.sharedMesh = cut.body;
            var mats = mr.sharedMaterials;
            if (cut.wheelMats == null || cut.wheelMats.Length != mats.Length) cut.wheelMats = WheelMats(mats);
            for (int i = 0; i < cut.wheels.Count; i++)
            {
                var w = new GameObject("WheelSpin_OZ_" + i);
                w.layer = model.gameObject.layer;
                w.transform.SetParent(model, false);
                w.transform.localPosition = cut.wheels[i].centre; w.transform.localRotation = cut.rot; w.transform.localScale = Vector3.one;
                w.AddComponent<MeshFilter>().sharedMesh = cut.wheels[i].mesh;
                var r = w.AddComponent<MeshRenderer>();
                r.sharedMaterials = cut.wheelMats;
                r.shadowCastingMode = mr.shadowCastingMode; r.receiveShadows = mr.receiveShadows;
                r.lightProbeUsage = mr.lightProbeUsage; r.reflectionProbeUsage = mr.reflectionProbeUsage;
                r.renderingLayerMask = mr.renderingLayerMask; r.motionVectorGenerationMode = MotionVectorGenerationMode.Object;   // spinning: motion blur
                if (ClosedLiving) CloseTyre(w.transform, false);
                WheelsMade++;
            }
            Vehicles++;
            var vm = go.GetComponent<VehicleMotion>(); if (vm) vm.Rescan();                            // a VehicleMotion already on the prefab rescans
        }

        static void Warn(string type, string why) { if (warned.Add(type + why)) Debug.LogWarning($"[OpenZ] WheelSplit {type}: {why}; its wheels stay fused (no spin)"); }

        /// <summary></summary>
        internal static Material[] WheelMatsFor(Material[] mats) => WheelMats(mats);

        static Material[] WheelMats(Material[] mats)
        {
            var o = new Material[mats.Length];
            for (int i = 0; i < mats.Length; i++)
            {
                var m = mats[i]; if (!m) continue;
                if (!wheelMat.TryGetValue(m, out var w) || !w)
                {
                    w = new Material(m) { name = m.name + "_wheels" };                                  // not a TeamPaint panel rule name
                    if (w.HasProperty(AccentFromId)) { w.SetFloat(AccentFromId, 2f); if (w.HasProperty(AccentToId)) w.SetFloat(AccentToId, 2f); }   // accent band off
                    wheelMat[m] = w;
                }
                o[i] = w;
            }
            return o;
        }

        static Cut Build(Mesh src, Spec spec, Vector3 axisLocal, string type)
        {
            if (!src.isReadable) { Warn(type, "mesh not readable (enable Read/Write on the model import)"); return null; }
            var v = src.vertices;
            int subs = src.subMeshCount;
            var tris = new int[subs][]; for (int s = 0; s < subs; s++) tris[s] = src.GetTriangles(s);
            // which way glTFast mapped the GLB x axis: the sign that finds the measured wheels
            int bestN = -1; float sign = -1f; int[][] bestOwner = null;
            foreach (float sx in new[] { -1f, 1f })
            {
                var owner = new int[subs][]; int n = 0;
                for (int s = 0; s < subs; s++) { owner[s] = Classify(v, tris[s], spec, sx); foreach (var k in owner[s]) if (k >= 0) n++; }
                if (n > bestN) { bestN = n; sign = sx; bestOwner = owner; }
            }
            if (bestN < 0.6f * spec.expectTris) { Warn(type, $"found {bestN} wheel triangles, expected ~{spec.expectTris}"); return null; }
            var cut = new Cut { sign = sign, tris = bestN };
            signOf[type] = sign;
            axisLocal = axisLocal.sqrMagnitude > 1e-6f ? axisLocal.normalized : Vector3.forward;
            cut.rot = Quaternion.FromToRotation(Vector3.right, axisLocal);
            var inv = Quaternion.Inverse(cut.rot);

            // body: the same vertex data, the wheel triangles removed, the original bounds kept
            cut.body = Object.Instantiate(src); cut.body.name = src.name + "_body";
            for (int s = 0; s < subs; s++)
            {
                var keep = new List<int>(tris[s].Length);
                for (int t = 0; t < tris[s].Length / 3; t++) if (bestOwner[s][t] < 0) { keep.Add(tris[s][3 * t]); keep.Add(tris[s][3 * t + 1]); keep.Add(tris[s][3 * t + 2]); }
                cut.body.SetTriangles(keep, s, false);
            }
            cut.body.bounds = src.bounds;

            // wheels: vertices re-based on the axle centre, in the wheel's local frame (x = the axle)
            var nrm = src.normals; var tan = src.tangents; var uv0 = new List<Vector2>(); src.GetUVs(0, uv0);
            var uv1 = new List<Vector2>(); src.GetUVs(1, uv1); var col = src.colors32;
            int nw = spec.wheels.Length * 2;
            for (int w = 0; w < nw; w++)
            {
                var ws = spec.wheels[w / 2]; float zSide = (w % 2 == 0 ? 1f : -1f) * 0.5f * (spec.zi + spec.zo);
                var centre = new Vector3(ws.x * sign, ws.y, zSide);
                var map = new Dictionary<int, int>(); var pv = new List<Vector3>(); var pn = new List<Vector3>(); var pt = new List<Vector4>();
                var pu0 = new List<Vector2>(); var pu1 = new List<Vector2>(); var pc = new List<Color32>();
                var sub = new List<int>[subs]; int count = 0;
                for (int s = 0; s < subs; s++)
                {
                    sub[s] = new List<int>();
                    for (int t = 0; t < tris[s].Length / 3; t++)
                    {
                        if (bestOwner[s][t] != w) continue;
                        for (int k = 0; k < 3; k++)
                        {
                            int vi = tris[s][3 * t + k];
                            if (!map.TryGetValue(vi, out int ni))
                            {
                                ni = pv.Count; map[vi] = ni;
                                pv.Add(inv * (v[vi] - centre));
                                if (nrm != null && nrm.Length == v.Length) pn.Add(inv * nrm[vi]);
                                if (tan != null && tan.Length == v.Length) { var tv = inv * new Vector3(tan[vi].x, tan[vi].y, tan[vi].z); pt.Add(new Vector4(tv.x, tv.y, tv.z, tan[vi].w)); }
                                if (uv0.Count == v.Length) pu0.Add(uv0[vi]);
                                if (uv1.Count == v.Length) pu1.Add(uv1[vi]);
                                if (col != null && col.Length == v.Length) pc.Add(col[vi]);
                            }
                            sub[s].Add(ni);
                        }
                        count++;
                    }
                }
                if (count == 0) continue;
                var m = new Mesh { name = $"{src.name}_wheel{w}" };
                if (pv.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                m.SetVertices(pv); if (pn.Count == pv.Count) m.SetNormals(pn); if (pt.Count == pv.Count) m.SetTangents(pt);
                if (pu0.Count == pv.Count) m.SetUVs(0, pu0); if (pu1.Count == pv.Count) m.SetUVs(1, pu1); if (pc.Count == pv.Count) m.SetColors(pc);
                m.subMeshCount = subs; for (int s = 0; s < subs; s++) m.SetTriangles(sub[s], s, false);
                m.RecalculateBounds();
                cutR[m] = ws.z;
                cut.wheels.Add((m, centre));
            }
            Debug.Log($"[OpenZ] WheelSplit {type}: {cut.wheels.Count} wheels, {bestN} triangles (GLB x sign {sign:+0;-0}) cut from {src.name}");
            return cut;
        }

        /// <summary>Wheel index per triangle (w = 2 x wheel + side, side 0 = +z), -1 = body. Same rule as tools/wheel_split.py.</summary>
        static int[] Classify(Vector3[] v, int[] tri, Spec spec, float sx)
        {
            int n = tri.Length / 3; var owner = new int[n];
            for (int t = 0; t < n; t++)
            {
                owner[t] = -1;
                Vector3 a = v[tri[3 * t]], b = v[tri[3 * t + 1]], c = v[tri[3 * t + 2]];
                var cen = (a + b + c) / 3f;
                for (int w = 0; w < spec.wheels.Length && owner[t] < 0; w++)
                {
                    var ws = spec.wheels[w]; float wx = ws.x * sx, cutR = ws.z, slack = cutR * VertexSlack;
                    float dxc = cen.x - wx, dyc = cen.y - ws.y;
                    if (dxc * dxc + dyc * dyc > cutR * cutR) continue;
                    if (R2(a, wx, ws.y) > slack * slack || R2(b, wx, ws.y) > slack * slack || R2(c, wx, ws.y) > slack * slack) continue;
                    for (int side = 0; side < 2; side++)
                    {
                        float sg = side == 0 ? 1f : -1f, zc = cen.z * sg;
                        if (zc < spec.zi || zc > spec.zo) continue;
                        if (Mathf.Min(a.z * sg, Mathf.Min(b.z * sg, c.z * sg)) < spec.zi - 0.03f) continue;
                        owner[t] = 2 * w + side; break;
                    }
                }
            }
            return owner;
        }

        static float R2(Vector3 p, float x, float y) { float dx = p.x - x, dy = p.y - y; return dx * dx + dy * dy; }
    }
}
