using System.Collections.Generic;
using UnityEngine;
using OpenZ.Sim;
using UnityEngine.Rendering;

namespace OpenZ
{
    /// <summary>
    /// With <see cref="ChunkLibrary"/> each big fort /
    /// building part breaks into SEVERAL modelled library chunks (ConcreteChunkLib, README_IMPORT 5) that share the building's
    /// chunk cap by part volume (fort 90, others 40; 4-24 per part, L first, then M / S / XS), placed on the part's own wall surface,
    /// randomly rotated / mirrored / scaled, M / L heavy on layer 28 (they stack), S / XS on 31; every fracture cell becomes an S / XS
    /// library chunk (3 heaps in the fort's rubble), band chunks carry the owner's colour. MakeConcretePart / ChunkLook stay as the
    /// fallback (ChunkLibrary = false or the library missing). Everything is built inactive at level load (pooled), the death only
    /// activates and launches.
    /// </summary>
    public partial class BuildingBreaker
    {
        public static bool ChunkLibrary = true;
        public static int ChunkCapFort = 90, ChunkCapOther = 40;
        public const int HeavyLayer = 28;                                                            // TagManager "DebrisHeavy"
        public static readonly int[] LibCrashes = new int[3];                                        // L large crashes, M / L thuds, S / XS knocks
        public static string LastChunkStats = "";
        static bool libWarm;
        readonly List<string> libLog = new List<string>();
        int libParts, libCells; readonly int[] libCls = new int[5]; readonly int[] libHist = new int[8]; int libBand, libHeaps, libMirror;
        float heavyWin; int heavyL, heavyM;

        static bool LibReady()
        {
            if (!ChunkLibrary || !ConcreteChunkLib.Load()) return false;
            if (!libWarm)
            {
                libWarm = true;
                foreach (var m in ConcreteChunkLib.Materials) chunkMats[m] = m;                     // OpenZEnding's warm render draws them once
                int heavy = LayerMask.NameToLayer("DebrisHeavy"); if (heavy < 0) heavy = HeavyLayer;
                Physics.IgnoreLayerCollision(heavy, GetDebrisLayer(), true);                          // M / L stack on each other + the world; S / XS hit the world only
            }
            return true;
        }

        static int HeavyLayerIndex { get { int l = LayerMask.NameToLayer("DebrisHeavy"); return l >= 0 ? l : HeavyLayer; } }

        internal bool HeavyBudget(bool large)
        {
            if (Time.time - heavyWin >= 1f) { heavyWin = Time.time; heavyL = 0; heavyM = 0; }
            if (large) { if (heavyL >= 4) return false; heavyL++; return true; }
            if (heavyM >= 8) return false; heavyM++; return true;
        }

        static int ClsIndex(string c) => c == "L" ? 0 : c == "M" ? 1 : c == "S" ? 2 : c == "XS" ? 3 : 4;

        void Count(ConcreteChunkLib.Entry e, Vector3 sc, bool mirror, bool cell)
        {
            libCls[ClsIndex(e.cls)]++; if (e.band) libBand++; if (e.cls == "HEAP") libHeaps++; if (mirror) libMirror++;
            if (cell) libCells++; else libParts++;
            float L = e.Longest * Mathf.Max(sc.x, Mathf.Max(sc.y, sc.z));
            float[] edges = { 0.4f, 0.8f, 1.2f, 1.6f, 2.0f, 2.5f, 3.0f };
            int b = 0; while (b < edges.Length && L >= edges[b]) b++;
            libHist[b]++;
        }

        void ResetLibStats() { libParts = libCells = libBand = libHeaps = libMirror = 0; System.Array.Clear(libCls, 0, libCls.Length); System.Array.Clear(libHist, 0, libHist.Length); }

        void LogChunkStats()
        {
            LastChunkStats = $"{name}: {libParts} part chunks + {libCells} cell chunks = {libParts + libCells} (L {libCls[0]} M {libCls[1]} S {libCls[2]} XS {libCls[3]} heaps {libHeaps}, band {libBand}, mirrored {libMirror}); longest side (m, scaled) <0.4 {libHist[0]} | 0.4-0.8 {libHist[1]} | 0.8-1.2 {libHist[2]} | 1.2-1.6 {libHist[3]} | 1.6-2.0 {libHist[4]} | 2.0-2.5 {libHist[5]} | 2.5-3.0 {libHist[6]} | >3.0 {libHist[7]}";
            ChunkStats = LastChunkStats;
            Debug.Log("[OpenZ] chunks " + LastChunkStats);
            if (obj != null && obj.IsFort) FortChunkStats = LastChunkStats;
        }
        public static string FortChunkStats = "";
        public string ChunkStats { get; private set; } = "";

        /// <summary>README 5.9: a fracture cell becomes a library chunk (rubble: the fort's first 3 = heaps, then S / XS; the old accent
        /// cell = a band chunk; the rest S / XS), its own look, random scale (building scale divided out) and rotation.</summary>
        ConcreteChunkLib.Entry LibCell(GameObject go, int i, bool fort, ref int heapsUsed)
        {
            var rnd = new System.Random(obj.Id * 977 + i * 31 + 7);
            bool isRubble = i % (fort ? 4 : 6) == 0;
            ConcreteChunkLib.Entry e;
            if (isRubble && fort && heapsUsed < 3 && ConcreteChunkLib.Heaps.Count > 0) e = ConcreteChunkLib.Heap(heapsUsed++);
            else if (!isRubble && i % 8 == 3) e = ConcreteChunkLib.PickBand(rnd, rnd.NextDouble() < 0.5 ? "S" : "XS");
            else e = ConcreteChunkLib.Pick(rnd, isRubble ? (rnd.NextDouble() < 0.6 ? "S" : "XS") : (rnd.NextDouble() < 0.45 ? "S" : "XS"), true);
            if (e == null || !e.mesh) return null;
            var mf = go.GetComponent<MeshFilter>(); var mr = go.GetComponent<MeshRenderer>(); if (!mf || !mr) return null;
            mf.sharedMesh = e.mesh; mr.sharedMaterials = e.mats;
            mr.SetPropertyBlock(null);
            ConcreteChunkLib.ApplyLook(mr, e, rnd);
            var fs = fracture.transform.lossyScale;
            var s = ConcreteChunkLib.RandomScale(rnd, e.max_scale);
            go.transform.localScale = new Vector3(s.x / Mathf.Max(1e-3f, Mathf.Abs(fs.x)), s.y / Mathf.Max(1e-3f, Mathf.Abs(fs.y)), s.z / Mathf.Max(1e-3f, Mathf.Abs(fs.z)));
            go.transform.localRotation = isRubble ? Quaternion.identity : ConcreteChunkLib.RandomRotation(rnd);
            Count(e, s, false, true);
            return e;
        }

        sealed class PartTriSet { public Vector3[] v, n; public int[] t; public float[] cdf; }
        static readonly Dictionary<Mesh, PartTriSet> partTris = new Dictionary<Mesh, PartTriSet>();

        /// <summary>The part mesh's FRONT triangles (BuildingRuins.Make appends the flipped back faces after the front ones of every
        /// submesh) with cumulative areas; null when the mesh is not readable.</summary>
        static PartTriSet PartTris(Mesh pm)
        {
            if (partTris.TryGetValue(pm, out var ps)) return ps;
            ps = null;
            if (pm.isReadable)
            {
                var v = pm.vertices; var n = pm.normals; var tl = new List<int>();
                for (int sm = 0; sm < pm.subMeshCount; sm++) { var t = pm.GetTriangles(sm); int half = (t.Length / 3 / 2) * 3; for (int k = 0; k < half; k++) tl.Add(t[k]); }
                if (tl.Count >= 3 && n != null && n.Length == v.Length)
                {
                    var cdf = new float[tl.Count / 3]; float acc = 0f;
                    for (int k = 0; k < cdf.Length; k++) { acc += 0.5f * Vector3.Cross(v[tl[3 * k + 1]] - v[tl[3 * k]], v[tl[3 * k + 2]] - v[tl[3 * k]]).magnitude; cdf[k] = acc; }
                    if (acc > 1e-6f) ps = new PartTriSet { v = v, n = n, t = tl.ToArray(), cdf = cdf };
                }
            }
            partTris[pm] = ps;
            return ps;
        }

        /// <summary>README 5.2-5.4: the quota per part (cap by part volume, fitted to the cap exactly), the L / M / S / XS mix, band
        /// chunks on the top parts, each chunk its own inactive root under the holder with its convex collider and heavy body.</summary>
        void BuildLibraryChunks(BuildingRuins.Set set, Transform mt)
        {
            var s = mt.lossyScale; var sa = new Vector3(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
            var srcMf = mainR.GetComponent<MeshFilter>(); var srcB = srcMf && srcMf.sharedMesh ? srcMf.sharedMesh.bounds : new Bounds(Vector3.zero, Vector3.one);
            int np = set.parts.Count; var V = new float[np]; var size = new Vector3[np]; float sumV = 0f;
            for (int i = 0; i < np; i++) { var pm = set.parts[i]; if (!pm) continue; size[i] = Vector3.Scale(pm.bounds.size, sa); V[i] = size[i].x * size[i].y * size[i].z * 0.5f; sumV += V[i]; }
            if (sumV <= 0f) return;
            int cap = obj.IsFort ? ChunkCapFort : ChunkCapOther;
            var q = new int[np]; int sum = 0;
            for (int i = 0; i < np; i++) { q[i] = set.parts[i] ? Mathf.Clamp(Mathf.RoundToInt(cap * V[i] / sumV), 4, 24) : 0; sum += q[i]; }
            for (int guard = 0; guard < 400 && sum != cap; guard++)
            {
                int best = -1;
                if (sum > cap) { for (int i = 0; i < np; i++) if (q[i] > 4 && (best < 0 || q[i] > q[best])) best = i; if (best < 0) break; q[best]--; sum--; }
                else { for (int i = 0; i < np; i++) if (set.parts[i] && q[i] < 24 && (best < 0 || V[i] / q[i] > V[best] / q[best])) best = i; if (best < 0) break; q[best]++; sum++; }
            }
            var lUse = new Dictionary<ConcreteChunkLib.Entry, int>(); var lMirror = new Dictionary<ConcreteChunkLib.Entry, bool>();
            var lPool = ConcreteChunkLib.Pool("L");
            int layerLight = GetDebrisLayer(), layerHeavy = HeavyLayerIndex;
            var game = ZGame.I;
            for (int pi = 0; pi < np; pi++)
            {
                var pm = set.parts[pi]; if (!pm || q[pi] <= 0) continue;
                var prnd = new System.Random(obj.Id * 131 + pi * 17 + 5);
                float maxD = Mathf.Max(size[pi].x, Mathf.Max(size[pi].y, size[pi].z));
                int quota = q[pi];
                int nL = maxD >= 3.2f ? 1 + prnd.Next(2) : maxD >= 2.2f ? prnd.Next(2) : 0; nL = Mathf.Min(nL, quota / 3);
                if (lPool == null || lPool.Count == 0) nL = 0;
                int rest = quota - nL, nM = Mathf.RoundToInt(0.35f * rest), nS = Mathf.RoundToInt(0.35f * rest), nX = rest - nM - nS;
                if (nX < 1) { if (nS >= nM && nS > 0) nS--; else if (nM > 0) nM--; nX = rest - nM - nS; }
                bool top = pm.bounds.center.y > srcB.min.y + 0.74f * srcB.size.y;
                Debug.Log($"[OpenZ] chunks {name} part {pi}: quota {quota} L{nL} M{nM} S{nS} XS{nX}{(top ? " (top: band M / S / XS)" : "")}");
                var plan = new List<KeyValuePair<string, bool>>();
                for (int k = 0; k < nL; k++) plan.Add(new KeyValuePair<string, bool>("L", false));
                for (int k = 0; k < nM; k++) plan.Add(new KeyValuePair<string, bool>("M", top && k == 0));
                for (int k = 0; k < nS; k++) plan.Add(new KeyValuePair<string, bool>("S", top && k == 0));
                for (int k = 0; k < nX; k++) plan.Add(new KeyValuePair<string, bool>("XS", top && k == 0));
                var tri = PartTris(pm);
                var partCenterW = mt.TransformPoint(pm.bounds.center);
                for (int ci = 0; ci < plan.Count; ci++)
                {
                    var rnd = new System.Random(obj.Id * 131 + pi * 17 + ci * 7919 + 1);
                    string cls = plan[ci].Key; bool band = plan[ci].Value;
                    ConcreteChunkLib.Entry e; bool mirror = rnd.NextDouble() < 0.5;
                    if (cls == "L")
                    {   // each L once per building while unused ones are left, then the least used, mirrored the other way
                        var unused = new List<ConcreteChunkLib.Entry>(); foreach (var c in lPool) if (!lUse.ContainsKey(c)) unused.Add(c);
                        if (unused.Count > 0) e = unused[rnd.Next(unused.Count)];
                        else { e = lPool[0]; foreach (var c in lPool) if (lUse[c] < lUse[e]) e = c; mirror = !lMirror[e]; }
                        lUse.TryGetValue(e, out int u); lUse[e] = u + 1; lMirror[e] = mirror;
                    }
                    else e = band ? ConcreteChunkLib.PickBand(rnd, cls) : ConcreteChunkLib.Pick(rnd, cls, cls == "XS");
                    if (e == null || !e.mesh) continue;
                    var sc = ConcreteChunkLib.RandomScale(rnd, e.max_scale);
                    float sMin = Mathf.Min(sc.x, Mathf.Min(sc.y, sc.z)), inset = 0.5f * e.Thick * sMin;
                    Vector3 pos;
                    if (tri != null)
                    {
                        int k = System.Array.BinarySearch(tri.cdf, (float)rnd.NextDouble() * tri.cdf[tri.cdf.Length - 1]); if (k < 0) k = ~k; k = Mathf.Min(k, tri.cdf.Length - 1);
                        Vector3 a = tri.v[tri.t[3 * k]], b = tri.v[tri.t[3 * k + 1]], c = tri.v[tri.t[3 * k + 2]];
                        float u = (float)rnd.NextDouble(), w = (float)rnd.NextDouble(); if (u + w > 1f) { u = 1f - u; w = 1f - w; }
                        var nW = mt.TransformDirection((tri.n[tri.t[3 * k]] + tri.n[tri.t[3 * k + 1]] + tri.n[tri.t[3 * k + 2]]).normalized);
                        pos = mt.TransformPoint(a + (b - a) * u + (c - a) * w) - nW * inset;
                    }
                    else
                    {
                        var bb = pm.bounds;
                        pos = mt.TransformPoint(bb.center + Vector3.Scale(bb.extents, new Vector3((float)rnd.NextDouble() * 2f - 1f, (float)rnd.NextDouble() * 2f - 1f, (float)rnd.NextDouble() * 2f - 1f) * 0.8f));
                    }
                    float gy = game ? game.GroundY(pos.x, pos.z) : mt.position.y;
                    pos.y = Mathf.Max(pos.y, gy + inset);
                    bool heavy = e.Heavy;
                    var root = new GameObject(e.name) { layer = heavy ? layerHeavy : layerLight };
                    root.transform.SetParent(holder.transform, false);
                    root.transform.SetPositionAndRotation(pos, ConcreteChunkLib.RandomRotation(rnd));
                    root.transform.localScale = sc;
                    var vis = new GameObject("mesh") { layer = root.layer };
                    vis.transform.SetParent(root.transform, false);
                    if (mirror) vis.transform.localScale = new Vector3(-1f, 1f, 1f);
                    vis.AddComponent<MeshFilter>().sharedMesh = e.mesh;
                    var r = vis.AddComponent<MeshRenderer>(); r.sharedMaterials = e.mats;
                    r.shadowCastingMode = ShadowCastingMode.On; r.motionVectorGenerationMode = MotionVectorGenerationMode.Object;
                    ConcreteChunkLib.ApplyLook(r, e, rnd);
                    var colMesh = mirror ? e.colM : e.col;
                    if (heavy && colMesh)
                    {
                        var mc = root.AddComponent<MeshCollider>(); mc.convex = true; mc.sharedMesh = colMesh; mc.sharedMaterial = Concrete();
                    }
                    else
                    {
                        var mb = e.mesh.bounds; var bc = root.AddComponent<BoxCollider>();
                        bc.center = mirror ? new Vector3(-mb.center.x, mb.center.y, mb.center.z) : mb.center; bc.size = mb.size * 0.85f; bc.sharedMaterial = Concrete();
                    }
                    var rb = root.AddComponent<Rigidbody>();
                    rb.mass = Mathf.Clamp(e.suggested_mass * sc.x * sc.y * sc.z, 1f, 3000f);
                    rb.linearDamping = 0.05f;
                    rb.angularDamping = heavy ? 2.2f : 0.9f; rb.maxAngularVelocity = heavy ? 3f : 8f;
                    rb.maxDepenetrationVelocity = 2f;                                                   // chunks start packed in the wall: ease them apart, never pop
                    rb.interpolation = RigidbodyInterpolation.Interpolate;
                    rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                    var bp = root.AddComponent<BigPart>(); bp.owner = this; bp.rb = rb; bp.concrete = true; bp.lib = e; bp.partCenter = partCenterW;
                    root.SetActive(false);
                    bigParts.Add(rb);
                    Count(e, sc, mirror, false);
                }
            }
        }

        /// <summary>README 5.6: heavier classes fly lower and spin slower; pushed away from the blast and out of their own part.</summary>
        void LaunchLibChunk(Rigidbody rb, BigPart bp, Vector3 center, float k)
        {
            var e = bp.lib; var tr = rb.transform;
            if (e.band) ConcreteChunkLib.SetTrim(tr.GetComponentInChildren<MeshRenderer>(), e, obj.Owner != Team.Neutral ? TeamColors.Of(obj.Owner) : TeamColors.Neutral, new System.Random(rb.GetInstanceID()));
            float cs = e.cls == "L" ? 1.0f : e.cls == "M" ? 1.15f : e.cls == "S" ? 1.25f : 1.4f;
            if (obj != null && obj.IsFort) k = Mathf.Min(k, 1.1f);
            else if (e.cls == "S" || e.cls == "XS") k = Mathf.Min(k, 1.3f);
            var away = tr.position - center; away.y = 0f;
            if (away.sqrMagnitude < 0.01f) { away = Random.insideUnitSphere; away.y = 0f; }
            away.Normalize();
            var rad = tr.position - bp.partCenter; rad = rad.sqrMagnitude > 1e-4f ? rad.normalized : Vector3.up;
            float up01 = Mathf.Clamp01((tr.position.y - transform.position.y) / 8f);
            bool small = e.cls == "S" || e.cls == "XS" || (obj != null && obj.IsFort);
            rb.linearVelocity = away * Random.Range(1.4f, 3.6f) * k * cs + Vector3.up * Random.Range(3.5f, 6.5f) * k * (0.6f + 0.6f * up01) * (small ? 0.85f : 0.5f + 0.5f * cs) + rad * (small ? Random.Range(0.5f, 2f) : Random.Range(1f, 4f));
            Vector2 spin = e.cls == "L" ? new Vector2(0.3f, 1.1f) : e.cls == "M" ? new Vector2(0.8f, 2f) : e.cls == "S" ? new Vector2(2f, 5f) : new Vector2(3f, 8f);
            rb.angularVelocity = Random.onUnitSphere * Random.Range(spin.x, spin.y);
        }
    }
}
