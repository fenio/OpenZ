using System.Collections.Generic;
using System.Linq;
using OpenZ.Sim;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace OpenZ
{
    /// <summary>
    /// a soft skirt that ends at h = 0) drawn with the level's own ground material (Sand.shader, role 2: ground paint by UV0 + the
    /// crater decal by UV1), the ground under it cut by a clip in every Sand pass (role 1, global _OzCraters[12]).
    /// Heights: the patch is seated per vertex on the RENDERED ground (TrackSurface.RenderY), its collider (COL_CRATER) on the
    /// terrain collider. GroundY / TerrainY / track marks / scorches / FX ground add the crater's polar-grid height (built from the
    /// imported collider mesh, so no axis convention is assumed) while it eases in over 0.25 s; then the terrain collider chunk
    /// under it is swapped for a copy with the bowl cut out (baked in a job, no main-thread cook) and the bowl collider goes live:
    /// ragdolls, debris, wheels and cans rest IN the crater. The sim is untouched (view only: paths and line of sight ignore it).
    /// At most 12; each is removed after ~2 min only when off screen with nothing resting in it.
    /// </summary>
    [DefaultExecutionOrder(104)]
    public sealed class CraterField : MonoBehaviour
    {
        public const string Pref = "oz_deformable_ground";
        public const string Label = "EXPERIMENTAL · DEFORMABLE GROUND";
        public const string Tip = "DEFORMABLE GROUND · EXPERIMENTAL|Big explosions on open sand, dirt and paving can leave a shallow crater that units drive through. At most 12 at once; each fades after about 2 minutes when it is off screen. Visual only: the rules, paths and line of sight never change.|";
        public const int Cap = 12;
        public const float Life = 120f, EaseTime = 0.25f, ChunkSize = 24f, MinStrength = 1.15f;
        const string Res = "OpenZ/Craters/";

        public static CraterField I { get; private set; }

        public static bool Enabled
        {
            get => PlayerPrefs.GetInt(Pref, 1) == 1;
            set
            {
                PlayerPrefs.SetInt(Pref, value ? 1 : 0); PlayerPrefs.Save();
                Debug.Log($"[OpenZ Options] deformable ground (experimental): {(value ? "ON" : "OFF")} ({Pref} {(value ? 1 : 0)}, saved, live)");
                if (value) Ensure(); else if (I) I.ClearAll("option switched off");
            }
        }

        // ------------------------------------------------------------------------------------------------ data
        sealed class Variant
        {
            public string id; public bool paved; public float R;
            public Vector3[] pv, pn; public Vector2[] puv1; public int[] ptri;        // render patch (patch space)
            public Vector3[] kv; public Vector3[] kn; public Vector2[] kuv; public int[] ktri; public Material kitMatSrc;   // seated clods / slabs
            public Vector3[] cv; public int[] ctri;                                       // collider
            public float[] ringR; public float[][] ringH; public float[] ringT0; public int segs;
            public float renderErr;
            public float HLocal(float lx, float lz)
            {
                float r = Mathf.Sqrt(lx * lx + lz * lz);
                int n = ringR.Length;
                if (r >= ringR[n - 1]) return 0f;
                int k = 0; while (k < n - 2 && r >= ringR[k + 1]) k++;
                float t = Mathf.Clamp01((r - ringR[k]) / Mathf.Max(1e-5f, ringR[k + 1] - ringR[k]));
                float th = Mathf.Atan2(lz, lx);
                return Mathf.Lerp(RingAt(k, th), RingAt(k + 1, th), t);
            }
            float RingAt(int k, float th)
            {
                var h = ringH[k]; int m = h.Length; if (m == 1) return h[0];
                float f = Mathf.Repeat(th - ringT0[k], Mathf.PI * 2f) / (Mathf.PI * 2f) * m;
                int i0 = Mathf.FloorToInt(f) % m, i1 = (i0 + 1) % m;
                return Mathf.Lerp(h[i0], h[i1], f - Mathf.Floor(f));
            }
        }

        sealed class Crater
        {
            public int serial; public Variant v; public Vector3 c; public float cs, sn, born, ease, offSince = -1f; public bool live, removing, dead;
            public GameObject root; public Mesh patch, clods; public MeshRenderer patchR, clodR; public Vector3[] pBase, pOut, nG, nOut; public Vector3[] kBase;
            public MeshCollider col; public Mesh colMesh; public readonly List<Chunk> chunks = new List<Chunk>(); public float spawnMs;
            public float H(float x, float z)
            {
                float dx = x - c.x, dz = z - c.z;
                if (dx * dx + dz * dz >= v.R * v.R) return 0f;
                return v.HLocal(cs * dx - sn * dz, sn * dx + cs * dz);
            }
            public Vector3 ToWorld(Vector3 l) => new Vector3(c.x + cs * l.x + sn * l.z, l.y, c.z - sn * l.x + cs * l.z);
            public Vector3 DirToWorld(Vector3 l) => new Vector3(cs * l.x + sn * l.z, l.y, -sn * l.x + cs * l.z);
        }

        sealed class Chunk
        {
            public int ix, iz; public MeshCollider mc; public Mesh orig; public int[] tris; public Bounds b;
            public readonly List<Crater> craters = new List<Crater>();
            public Mesh pending; public JobHandle job; public bool busy; public readonly List<Crater> pendingSet = new List<Crater>();
        }

        /// <summary>Bakes ONE mesh off the main thread. Crash fix (native "pure virtual function" in Physics.BakeMesh): a mesh
        /// must never be baked by two threads at once, nor while a collider uses it. The rules, all kept by ScheduleChunk:
        /// <list type="bullet">
        /// <item>a job gets only the brand-new cut mesh of its chunk, which nothing else knows until the job is completed</item>
        /// <item>a chunk's original mesh (already baked, maybe in use by its collider) is never baked again</item>
        /// <item>a crater's own bowl mesh is baked once, on the main thread, when the crater is made (Spawn), never in a job</item>
        /// <item>the jobs run one after the other (each depends on the one before: <see cref="bakeChain"/>)</item>
        /// <item>the main thread touches a job's mesh (assign, destroy) only after job.Complete()</item>
        /// </list></summary>
        struct BakeJob : IJob
        {
            public int id; public MeshColliderCookingOptions opt;
            public void Execute() { Physics.BakeMesh(id, false, opt); }
        }

        // the static mirror the hot paths read (GroundY runs thousands of times a frame): no allocation, early out when empty
        static readonly Crater[] act = new Crater[Cap];
        static int n;
        static readonly HashSet<Collider> terrainCols = new HashSet<Collider>(), craterCols = new HashSet<Collider>();

        readonly Dictionary<string, Variant> variants = new Dictionary<string, Variant>();
        readonly List<Crater> craters = new List<Crater>();
        readonly Dictionary<string, Material> patchMats = new Dictionary<string, Material>();
        readonly Dictionary<string, int> rejects = new Dictionary<string, int>();
        readonly List<(Renderer r, Bounds b, Vector3 ua, Vector3 va, Material m, int slot)> groundUv = new List<(Renderer, Bounds, Vector3, Vector3, Material, int)>();
        Chunk[] chunks; int cnx, cnz; float cx0, cz0; MeshCollider orig; bool physicsOk, ready, failed; int isoLayer = -1;
        JobHandle bakeChain;                                                              // the last scheduled bake: the next one waits for it (never two at once)
        int bakeJobs; static bool quiet;                                                  // bakes sent to a job so far; quiet = the self test (no log line per crater)
        Material kitMat; Mesh[] clodKit = new Mesh[0], chipKit = new Mesh[0];
        int serial, spawned; float lastReport, updMsSum; int updFrames; float maxSpawnMs;
        readonly Vector4[] glob = new Vector4[Cap];
        static readonly int CratersId = Shader.PropertyToID("_OzCraters"), CountId = Shader.PropertyToID("_OzCraterCount"),
            RoleId = Shader.PropertyToID("_OzCraterRole"), DecalId = Shader.PropertyToID("_OzCraterDecal"), DecalMulId = Shader.PropertyToID("_OzCraterDecalMul");
        readonly List<Thrown> thrown = new List<Thrown>();
        sealed class Thrown { public Mesh m; public Vector3 p, v, spin; public Quaternion q; public float t, rest = -1f, scale; public bool sounded; public Crater owner; }

        // ------------------------------------------------------------------------------------------------ lifetime
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            SceneManager.sceneLoaded -= OnScene; SceneManager.sceneLoaded += OnScene;
        }

        static void OnScene(Scene s, LoadSceneMode m)
        {
            n = 0; terrainCols.Clear(); craterCols.Clear(); quiet = false;
            Shader.SetGlobalFloat(CountId, 0f);
            if (Enabled && (s.name.StartsWith("L02") || s.name.StartsWith("L17"))) Ensure();
        }

        public static CraterField Ensure()
        {
            if (I) return I;
            I = new GameObject("CraterField").AddComponent<CraterField>();
            return I;
        }

        void OnDestroy()
        {
            if (I == this) I = null;
            bakeChain.Complete();                                                            // no bake may outlive this object
            foreach (var ch in chunks ?? new Chunk[0]) if (ch != null && ch.busy) { ch.job.Complete(); ch.busy = false; }
            n = 0; terrainCols.Clear(); craterCols.Clear();
            Shader.SetGlobalFloat(CountId, 0f);
        }

        void Update()
        {
            if (failed) return;
            if (!ready) { TryInit(); return; }
            float t0 = Time.realtimeSinceStartup;
            bool dirty = false;
            foreach (var c in craters)
            {
                if (c.dead) continue;
                if (c.ease < 1f)
                {
                    c.ease = Mathf.Min(1f, c.ease + Time.deltaTime / EaseTime);
                    Seat(c);
                    if (c.ease >= 1f) { c.clodR.enabled = true; OpenZFxPlayer.I?.InvalidateGround(c.c, c.v.R + 0.5f); }
                    dirty = true;
                }
            }
            if (physicsOk) PollChunks();
            UpdateThrown();
            if (Time.time - lastReport > 0.5f) { Housekeep(); lastReport = Time.time; }
            if (dirty) PushGlobals();
            updMsSum += (Time.realtimeSinceStartup - t0) * 1000f; updFrames++;
        }

        // ------------------------------------------------------------------------------------------------ init
        void TryInit()
        {
            var g = ZGame.I;
            if (g == null || g.World == null || g.World.Map == null) return;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                foreach (var id in new[] { "Sand_A", "Sand_B", "Sand_C", "Sand_D", "Paved_A", "Paved_B" }) LoadVariant(id);
                if (variants.Count == 0) { failed = true; Debug.LogWarning("[OpenZ Craters] no crater patch in Resources/OpenZ/Craters (run OpenZ > Craters > 1 Reimport assets): deformable ground stays off this match"); return; }
                LoadKit();
                FitGroundUv();
                var cg = GameObject.Find("COL_Terrain");
                orig = cg ? cg.GetComponent<MeshCollider>() : null;
                BuildChunks();
            }
            catch (System.Exception e) { failed = true; Debug.LogError("[OpenZ Craters] init failed, deformable ground off this match: " + e); return; }
            ready = true;
            foreach (var r in groundUv)
            {   // role 1 on the rendered ground only, on its Sand slot's own block (ShoreBeach keeps its paint override in the same
                // per-slot block and reads it back before writing, so neither loses the other's values); the asset is never touched
                var mpb = new MaterialPropertyBlock(); r.r.GetPropertyBlock(mpb, r.slot); mpb.SetFloat(RoleId, 1f); r.r.SetPropertyBlock(mpb, r.slot);
            }
            PushGlobals();
            var sb = new System.Text.StringBuilder();
            foreach (var v in variants.Values) sb.Append($" {v.id} R {v.R:0.00} render-vs-collider {v.renderErr * 100f:0.0} cm;");
            Debug.Log($"[OpenZ Craters] ready ({g.level}) in {sw.ElapsedMilliseconds} ms: {variants.Count} variants ({sb.ToString().Trim()}), ground UV fits {groundUv.Count}, " +
                      $"physics {(physicsOk ? $"{chunks.Length} terrain chunks of {ChunkSize:0} m (COL_Terrain moved to isolated layer {isoLayer})" : "OFF (COL_Terrain mesh not readable: bodies rest on the flat collider)")}, clods {clodKit.Length}+{chipKit.Length}");
        }

        void LoadVariant(string id)
        {
            var go = Resources.Load<GameObject>(Res + "CraterPatch_" + id);
            if (!go) return;
            Transform P = null, K = null, C = null;
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "CRATER_" + id) P = t; else if (t.name == "CRATER_" + id + "_Clods") K = t; else if (t.name == "COL_CRATER_" + id) C = t;
            }
            var pm = P ? P.GetComponent<MeshFilter>()?.sharedMesh : null; var cm = C ? C.GetComponent<MeshFilter>()?.sharedMesh : null;
            if (!pm || !cm || !pm.isReadable || !cm.isReadable) { Debug.LogWarning($"[OpenZ Craters] {id}: patch / collider mesh missing or not Read/Write (import settings): skipped"); return; }
            var root = go.transform.worldToLocalMatrix;
            var v = new Variant { id = id, paved = id.StartsWith("Paved") };
            Matrix4x4 mp = root * P.localToWorldMatrix, mc = root * C.localToWorldMatrix;
            v.pv = Xf(pm.vertices, mp); v.pn = XfDir(pm.normals, mp); v.ptri = pm.triangles;
            var uv1 = new List<Vector2>(); pm.GetUVs(1, uv1); v.puv1 = uv1.Count == v.pv.Length ? uv1.ToArray() : null;
            if (v.puv1 == null) { v.puv1 = new Vector2[v.pv.Length]; float rr = 0f; foreach (var p in v.pv) rr = Mathf.Max(rr, new Vector2(p.x, p.z).magnitude); for (int i = 0; i < v.pv.Length; i++) v.puv1[i] = new Vector2(0.5f + v.pv[i].x / (2f * rr), 0.5f + v.pv[i].z / (2f * rr)); }
            v.cv = Xf(cm.vertices, mc); v.ctri = cm.triangles;
            if (K && K.GetComponent<MeshFilter>()?.sharedMesh is Mesh km && km.isReadable)
            {
                var mk = root * K.localToWorldMatrix;
                v.kv = Xf(km.vertices, mk); v.kn = XfDir(km.normals, mk); v.kuv = km.uv; v.ktri = km.triangles;
            }
            float R = 0f; foreach (var p in v.pv) R = Mathf.Max(R, new Vector2(p.x, p.z).magnitude); v.R = R;
            BuildPolar(v);
            float err = 0f; foreach (var p in v.pv) err = Mathf.Max(err, Mathf.Abs(p.y - v.HLocal(p.x, p.z)));
            v.renderErr = err;
            variants[id] = v;
        }

        static Vector3[] Xf(Vector3[] a, Matrix4x4 m) { var o = new Vector3[a.Length]; for (int i = 0; i < a.Length; i++) o[i] = m.MultiplyPoint3x4(a[i]); return o; }
        static Vector3[] XfDir(Vector3[] a, Matrix4x4 m) { var o = new Vector3[a.Length]; for (int i = 0; i < a.Length; i++) o[i] = m.MultiplyVector(a[i]).normalized; return o; }

        /// <summary>The collider is an exact polar grid (centre + rings of equal radius): rings by radius, each ring's samples by angle.
        /// Built from the imported vertices, so the FBX axis mapping never matters.</summary>
        static void BuildPolar(Variant v)
        {
            var pts = new List<(float r, float th, float h)>();
            var seen = new HashSet<Vector3Int>();
            foreach (var p in v.cv)
            {
                if (!seen.Add(new Vector3Int(Mathf.RoundToInt(p.x * 1000), Mathf.RoundToInt(p.y * 1000), Mathf.RoundToInt(p.z * 1000)))) continue;
                pts.Add((new Vector2(p.x, p.z).magnitude, Mathf.Atan2(p.z, p.x), p.y));
            }
            pts.Sort((a, b) => a.r.CompareTo(b.r));
            var rings = new List<List<(float r, float th, float h)>>();
            foreach (var q in pts)
            {
                if (rings.Count == 0 || q.r - rings[rings.Count - 1][0].r > 0.02f) rings.Add(new List<(float, float, float)>());
                rings[rings.Count - 1].Add(q);
            }
            int nr = rings.Count;
            v.ringR = new float[nr]; v.ringH = new float[nr][]; v.ringT0 = new float[nr]; v.segs = 0;
            for (int k = 0; k < nr; k++)
            {
                var ring = rings[k]; ring.Sort((a, b) => a.th.CompareTo(b.th));
                float rs = 0f; foreach (var q in ring) rs += q.r; v.ringR[k] = rs / ring.Count;
                v.ringH[k] = new float[ring.Count]; for (int i = 0; i < ring.Count; i++) v.ringH[k][i] = ring[i].h;
                v.ringT0[k] = ring[0].th; v.segs = Mathf.Max(v.segs, ring.Count);
            }
        }

        void LoadKit()
        {
            var atlas = Resources.Load<Texture2D>(Res + "CraterKit_Atlas");
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh) { kitMat = new Material(sh) { name = "M_CraterKit (runtime)" }; if (atlas) kitMat.SetTexture("_BaseMap", atlas); kitMat.SetFloat("_Smoothness", 0.12f); kitMat.SetFloat("_EnvironmentReflections", 0f); kitMat.enableInstancing = true; }
            var kit = Resources.Load<GameObject>(Res + "CraterClodKit");
            if (!kit) return;
            var cl = new List<Mesh>(); var ch = new List<Mesh>();
            foreach (var mf in kit.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!mf.sharedMesh) continue;
                if (mf.name.StartsWith("CLOD_")) cl.Add(mf.sharedMesh); else if (mf.name.StartsWith("CHIP_")) ch.Add(mf.sharedMesh);
            }
            clodKit = cl.ToArray(); chipKit = ch.ToArray();
        }

        /// <summary>Planar UV of each TER_Ground renderer: u, v = a.x * x + a.y * z + a.z, least squares over its vertices.</summary>
        void FitGroundUv()
        {
            foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (!r.name.StartsWith("TER_Ground") || !r.gameObject.activeInHierarchy) continue;
                var mf = r.GetComponent<MeshFilter>(); var m = mf ? mf.sharedMesh : null;
                if (!m || !m.isReadable) continue;
                var vs = m.vertices; var uv = m.uv; if (uv.Length != vs.Length || vs.Length < 3) continue;
                var l2w = r.transform.localToWorldMatrix;
                double sxx = 0, sxz = 0, szz = 0, sx = 0, sz = 0, s1 = 0, sux = 0, suz = 0, su = 0, svx = 0, svz = 0, sv = 0;
                int step = Mathf.Max(1, vs.Length / 6000);
                for (int i = 0; i < vs.Length; i += step)
                {
                    var w = l2w.MultiplyPoint3x4(vs[i]); double x = w.x, z = w.z;
                    sxx += x * x; sxz += x * z; szz += z * z; sx += x; sz += z; s1 += 1; sux += uv[i].x * x; suz += uv[i].x * z; su += uv[i].x; svx += uv[i].y * x; svz += uv[i].y * z; sv += uv[i].y;
                }
                var a = Solve3(sxx, sxz, sx, sxz, szz, sz, sx, sz, s1, sux, suz, su);
                var b = Solve3(sxx, sxz, sx, sxz, szz, sz, sx, sz, s1, svx, svz, sv);
                float res = 0f;
                for (int i = 0; i < vs.Length; i += step)
                {
                    var w = l2w.MultiplyPoint3x4(vs[i]);
                    res = Mathf.Max(res, Mathf.Abs(a.x * w.x + a.y * w.z + a.z - uv[i].x), Mathf.Abs(b.x * w.x + b.y * w.z + b.z - uv[i].y));
                }
                if (res > 2e-3f) { Debug.LogWarning($"[OpenZ Craters] {r.name}: UV0 is not planar (max residual {res:0.0000}); no craters on it"); continue; }
                int slot = 0; var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) if (mats[i] && mats[i].shader && mats[i].shader.name == "OpenZ/Sand") { slot = i; break; }
                groundUv.Add((r, r.bounds, a, b, slot < mats.Length ? mats[slot] : r.sharedMaterial, slot));
            }
        }

        static Vector3 Solve3(double a, double b, double c, double d, double e, double f, double g, double h, double i, double r0, double r1, double r2)
        {
            double det = a * (e * i - f * h) - b * (d * i - f * g) + c * (d * h - e * g);
            if (System.Math.Abs(det) < 1e-12) return Vector3.zero;
            double x = (r0 * (e * i - f * h) - b * (r1 * i - f * r2) + c * (r1 * h - e * r2)) / det;
            double y = (a * (r1 * i - f * r2) - r0 * (d * i - f * g) + c * (d * r2 - r1 * g)) / det;
            double z = (a * (e * r2 - r1 * h) - b * (d * r2 - r1 * g) + r0 * (d * h - e * g)) / det;
            return new Vector3((float)x, (float)y, (float)z);
        }

        // ------------------------------------------------------------------------------------------------ physics chunks
        void BuildChunks()
        {
            physicsOk = false;
            if (!orig || !orig.sharedMesh || !orig.sharedMesh.isReadable) return;
            var m = orig.sharedMesh; var l2w = orig.transform.localToWorldMatrix;
            var wv = Xf(m.vertices, l2w); var tri = m.triangles;
            var b = orig.bounds;
            cx0 = b.min.x; cz0 = b.min.z; cnx = Mathf.Max(1, Mathf.CeilToInt(b.size.x / ChunkSize)); cnz = Mathf.Max(1, Mathf.CeilToInt(b.size.z / ChunkSize));
            var lists = new List<int>[cnx * cnz];
            for (int t = 0; t < tri.Length; t += 3)
            {
                var c = (wv[tri[t]] + wv[tri[t + 1]] + wv[tri[t + 2]]) / 3f;
                int ix = Mathf.Clamp(Mathf.FloorToInt((c.x - cx0) / ChunkSize), 0, cnx - 1), iz = Mathf.Clamp(Mathf.FloorToInt((c.z - cz0) / ChunkSize), 0, cnz - 1);
                (lists[iz * cnx + ix] ??= new List<int>()).AddRange(new[] { tri[t], tri[t + 1], tri[t + 2] });
            }
            chunkVerts = wv;
            isoLayer = -1;
            for (int i = 27; i >= 8; i--) if (i != ZGame.WalkLayer && string.IsNullOrEmpty(LayerMask.LayerToName(i))) { isoLayer = i; break; }
            if (isoLayer < 0) return;
            chunks = new Chunk[cnx * cnz];
            var parent = new GameObject("COL_TerrainChunks").transform;
            for (int k = 0; k < chunks.Length; k++)
            {
                if (lists[k] == null) continue;
                var ch = new Chunk { ix = k % cnx, iz = k / cnx, tris = lists[k].ToArray() };
                ch.orig = BuildMesh(ch.tris, null, $"COL_TerrainChunk_{ch.ix}_{ch.iz}");
                var go = new GameObject(ch.orig.name) { layer = orig.gameObject.layer };
                go.transform.SetParent(parent, false);
                ch.mc = go.AddComponent<MeshCollider>(); ch.mc.sharedMaterial = orig.sharedMaterial; ch.mc.cookingOptions = orig.cookingOptions; ch.mc.sharedMesh = ch.orig;
                ch.b = ch.orig.bounds;
                chunks[k] = ch; terrainCols.Add(ch.mc);
            }
            terrainCols.Add(orig);
            orig.gameObject.layer = isoLayer;                                                 // still answers Collider.Raycast (GroundY fallback, TerrainY), no contacts
            for (int i = 0; i < 32; i++) Physics.IgnoreLayerCollision(isoLayer, i, true);
            physicsOk = true;
        }

        Vector3[] chunkVerts;

        Mesh BuildMesh(int[] tris, List<Vector3> extraV, string name)
        {
            var map = new Dictionary<int, int>(); var v = new List<Vector3>(); var t = new List<int>(tris.Length);
            foreach (var i in tris) { if (!map.TryGetValue(i, out int j)) { j = v.Count; map[i] = j; v.Add(chunkVerts[i]); } t.Add(j); }
            if (extraV != null) { int b0 = v.Count; v.AddRange(extraV); for (int i = 0; i < extraV.Count; i++) t.Add(b0 + i); }
            var m = new Mesh { name = name, indexFormat = v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateBounds();
            return m;
        }

        /// <summary>The chunk with the bowl of every crater in `set` cut out: triangles touching a crater square are replaced by a
        /// 0.5 m grid on the same surface (heights from the removed triangles, else the original collider), minus the cells inside
        /// R - 0.35 m, which the crater's own collider covers. Overlap with kept triangles is harmless; there is never a gap.</summary>
        Mesh CutMesh(Chunk ch, List<Crater> set)
        {
            if (set.Count == 0) return ch.orig;
            var keep = new List<int>(ch.tris.Length); var rem = new List<int>();
            for (int t = 0; t < ch.tris.Length; t += 3)
            {
                Vector3 a = chunkVerts[ch.tris[t]], b = chunkVerts[ch.tris[t + 1]], c = chunkVerts[ch.tris[t + 2]];
                float x0 = Mathf.Min(a.x, Mathf.Min(b.x, c.x)), x1 = Mathf.Max(a.x, Mathf.Max(b.x, c.x)), z0 = Mathf.Min(a.z, Mathf.Min(b.z, c.z)), z1 = Mathf.Max(a.z, Mathf.Max(b.z, c.z));
                bool hit = false;
                foreach (var k in set) if (x1 > k.c.x - k.v.R && x0 < k.c.x + k.v.R && z1 > k.c.z - k.v.R && z0 < k.c.z + k.v.R) { hit = true; break; }
                (hit ? rem : keep).AddRange(new[] { ch.tris[t], ch.tris[t + 1], ch.tris[t + 2] });
            }
            if (rem.Count == 0) return ch.orig;
            float gx0 = float.MaxValue, gx1 = float.MinValue, gz0 = float.MaxValue, gz1 = float.MinValue;
            foreach (var i in rem) { var p = chunkVerts[i]; gx0 = Mathf.Min(gx0, p.x); gx1 = Mathf.Max(gx1, p.x); gz0 = Mathf.Min(gz0, p.z); gz1 = Mathf.Max(gz1, p.z); }
            const float G = 0.5f;
            gx0 = Mathf.Floor(gx0 / G) * G; gz0 = Mathf.Floor(gz0 / G) * G;
            int nx = Mathf.CeilToInt((gx1 - gx0) / G), nz = Mathf.CeilToInt((gz1 - gz0) / G);
            var hy = new float[(nx + 1) * (nz + 1)]; var inside = new bool[hy.Length];
            for (int j = 0; j <= nz; j++)
                for (int i = 0; i <= nx; i++)
                {
                    float x = gx0 + i * G, z = gz0 + j * G; int k = j * (nx + 1) + i;
                    inside[k] = InTris(rem, x, z, out hy[k]);
                    if (!inside[k]) hy[k] = OrigY(x, z, hy[k]);
                }
            var extra = new List<Vector3>();
            for (int j = 0; j < nz; j++)
                for (int i = 0; i < nx; i++)
                {
                    int k00 = j * (nx + 1) + i, k10 = k00 + 1, k01 = k00 + nx + 1, k11 = k01 + 1;
                    if (!(inside[k00] || inside[k10] || inside[k01] || inside[k11])) continue;
                    float cx = gx0 + (i + 0.5f) * G, cz = gz0 + (j + 0.5f) * G; bool hole = false;
                    foreach (var k in set) { float dx = cx - k.c.x, dz = cz - k.c.z; if (dx * dx + dz * dz < Sq(k.v.R - 0.35f)) { hole = true; break; } }
                    if (hole) continue;
                    Vector3 p00 = new Vector3(gx0 + i * G, hy[k00], gz0 + j * G), p10 = new Vector3(p00.x + G, hy[k10], p00.z), p01 = new Vector3(p00.x, hy[k01], p00.z + G), p11 = new Vector3(p00.x + G, hy[k11], p00.z + G);
                    extra.Add(p00); extra.Add(p01); extra.Add(p11); extra.Add(p00); extra.Add(p11); extra.Add(p10);   // clockwise from above (Unity: up-facing)
                }
            return BuildMesh(keep.ToArray(), extra, ch.orig.name + "_cut");
        }

        static float Sq(float v) => v * v;

        bool InTris(List<int> rem, float x, float z, out float y)
        {
            y = 0f;
            for (int t = 0; t < rem.Count; t += 3)
            {
                Vector3 a = chunkVerts[rem[t]], b = chunkVerts[rem[t + 1]], c = chunkVerts[rem[t + 2]];
                float d = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
                if (Mathf.Abs(d) < 1e-9f) continue;
                float u = ((b.z - c.z) * (x - c.x) + (c.x - b.x) * (z - c.z)) / d, w = ((c.z - a.z) * (x - c.x) + (a.x - c.x) * (z - c.z)) / d;
                if (u >= -1e-4f && w >= -1e-4f && u + w <= 1.0001f) { y = u * a.y + w * b.y + (1f - u - w) * c.y; return true; }
            }
            return false;
        }

        float OrigY(float x, float z, float fallback)
        {
            if (orig && orig.Raycast(new Ray(new Vector3(x, 200f, z), Vector3.down), out var h, 400f)) return h.point.y;
            return fallback;
        }

        void ScheduleChunk(Chunk ch)
        {
            if (ch.busy) { ch.job.Complete(); FinishChunk(ch, false); if (ch.pending && ch.pending != ch.orig && ch.pending != ch.mc.sharedMesh) Destroy(ch.pending); }
            ch.pendingSet.Clear(); foreach (var k in ch.craters) if (!k.removing) ch.pendingSet.Add(k);
            ch.pending = CutMesh(ch, ch.pendingSet);
            // only a brand-new cut mesh goes to a job (see BakeJob): never the chunk's original, never the mesh its collider uses
            bool fresh = ch.pending && ch.pending != ch.orig && ch.pending != ch.mc.sharedMesh && ch.pending.vertexCount >= 3;
            if (fresh) { ch.job = new BakeJob { id = ch.pending.GetInstanceID(), opt = ch.mc.cookingOptions }.Schedule(bakeChain); bakeChain = ch.job; bakeJobs++; }
            else ch.job = default;                                                          // nothing to bake: "completed" at once
            ch.busy = true;
        }

        void PollChunks()
        {
            foreach (var ch in chunks)
            {
                if (ch == null || !ch.busy || !ch.job.IsCompleted) continue;
                bool easing = false; foreach (var k in ch.pendingSet) if (k.ease < 1f) easing = true;
                if (easing) continue;                                                        // swap only once every crater in it has eased in
                ch.job.Complete(); FinishChunk(ch, true);
            }
        }

        void FinishChunk(Chunk ch, bool swap)
        {
            ch.job.Complete();                                                              // always: nobody touches a mesh a job still bakes
            ch.busy = false;
            if (!swap) return;
            var old = ch.mc.sharedMesh;
            ch.mc.sharedMesh = ch.pending;
            if (old && old != ch.orig && old != ch.pending) Destroy(old);
            foreach (var k in ch.pendingSet)
            {
                if (k.live || k.dead) continue;
                if (!k.col.enabled) { k.col.sharedMesh = k.colMesh; k.col.enabled = true; }   // with the FIRST chunk that opens its hole (no frame without a floor)
                bool all = true; foreach (var c2 in k.chunks) if (c2.busy || !c2.pendingSet.Contains(k)) all = false;
                if (!all) continue;
                k.live = true;                                                                  // every chunk under it is cut: GroundY reads the bowl collider
                OpenZFxPlayer.I?.InvalidateGround(k.c, k.v.R + 0.5f);
            }
            // removals: a crater that is out of every chunk's set and whose chunks are all done goes now
            foreach (var k in craters.ToArray()) if (k.removing && !k.dead && k.chunks.TrueForAll(c2 => !c2.busy)) Kill(k);
        }

        // ------------------------------------------------------------------------------------------------ self test
        /// <summary>Self test of the collider bake path (the check for the bake crash fix). Each round makes up to <see cref="Cap"/>
        /// craters in ONE frame on neighbouring chunk corners (a crater there lies on 2-4 chunks and shares them with the next
        /// one: the case that crashed), lets 0-3 frames pass while the bake jobs run, removes some craters one by one (which
        /// re-schedules busy chunks) and clears the rest; every 4th round waits until the craters are live first (the swap path).
        /// Start it in a running level: StartCoroutine(CraterField.SelfTest(200, Debug.Log)); or start the game with
        /// "-oz-craterselftest 200" (it loads L17, runs the test, writes the result to the log and quits).</summary>
        public static System.Collections.IEnumerator SelfTest(int rounds, System.Action<string> done)
        {
            float t0 = Time.realtimeSinceStartup;
            while ((ZGame.I == null || ZGame.I.World == null || !I || !I.ready) && Time.realtimeSinceStartup - t0 < 90f) yield return null;
            var f = I;
            if (!f || !f.ready || !f.physicsOk || ZGame.I == null) { done?.Invoke("FAILED: the crater field is not ready (is a level running, is DEFORMABLE GROUND on?)"); yield break; }
            quiet = true;
            try
            {
            var pts = new List<Vector3>();
            for (int iz = 1; iz < f.cnz; iz++)
                for (int ix = 1; ix < f.cnx; ix++)
                {
                    if (!f || ZGame.I == null) { done?.Invoke("FAILED: the level ended during the test"); yield break; }
                    var p = new Vector3(f.cx0 + ix * ChunkSize, 0f, f.cz0 + iz * ChunkSize);
                    if (f.TrySpawn(p, 1.7f, "self test", true) == null) pts.Add(p);
                    int alive = 0; foreach (var k in f.craters) if (!k.dead && !k.removing) alive++;
                    if (alive >= Cap) { f.ClearAll("self test"); yield return null; }
                }
            f.ClearAll("self test"); yield return null;
            if (pts.Count < 4) { done?.Invoke($"FAILED: only {pts.Count} free chunk corners on this map"); yield break; }
            var rng = new System.Random(20261008);
            int made = 0, multi = 0, live = 0, removed = 0, bakes0 = f.bakeJobs;
            for (int r = 0; r < rounds; r++)
            {
                if (!f || ZGame.I == null) { done?.Invoke($"FAILED: the level ended during the test (round {r} of {rounds})"); yield break; }
                int start = rng.Next(pts.Count), got = 0;
                for (int i = 0; i < pts.Count && got < Cap; i++)
                {
                    var p = pts[(start + i) % pts.Count] + new Vector3((float)(rng.NextDouble() - 0.5) * 2.4f, 0f, (float)(rng.NextDouble() - 0.5) * 2.4f);
                    if (f.TrySpawn(p, 1.2f + (float)rng.NextDouble() * 0.8f, "self test", true) != null) continue;
                    got++; made++; if (f.craters[f.craters.Count - 1].chunks.Count > 1) multi++;
                }
                for (int k = rng.Next(0, 4); k > 0; k--) yield return null;
                foreach (var c in f.craters.ToArray()) if (!c.dead && !c.removing && rng.Next(3) == 0) { f.Remove(c, "self test"); removed++; }
                if (rng.Next(2) == 0) yield return null;
                if (r % 4 == 0)
                {
                    float t = Time.realtimeSinceStartup;
                    while (Time.realtimeSinceStartup - t < EaseTime + 0.35f) yield return null;
                    foreach (var c in f.craters) if (c.live && !c.dead) live++;
                }
                f.ClearAll("self test");
                if (r % 3 == 0) yield return null;
            }
            yield return null;
            if (!f) { done?.Invoke("FAILED: the level ended during the test"); yield break; }
            int busy = 0, cut = 0; foreach (var ch in f.chunks) if (ch != null) { if (ch.busy) busy++; if (ch.mc.sharedMesh != ch.orig) cut++; }
            bool clean = busy == 0 && cut == 0 && f.craters.Count == 0;
            done?.Invoke($"{(clean ? "PASSED" : "FAILED (left over: busy chunks " + busy + ", cut chunks " + cut + ", craters " + f.craters.Count + ")")}: {rounds} rounds, {made} craters " +
                         $"({multi} on 2-4 chunks) on {pts.Count} chunk corners, {f.bakeJobs - bakes0} collider bakes in jobs, {removed} removed one by one, {live} went live, " +
                         $"{Time.realtimeSinceStartup - t0:0} s");
            }
            finally { quiet = false; }
        }

        sealed class SelfTestHost : MonoBehaviour { }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void SelfTestArg()
        {
            if (Application.isEditor) return;
            var a = System.Environment.GetCommandLineArgs(); int rounds = 0;
            for (int i = 0; i < a.Length - 1; i++) if (a[i].Equals("-oz-craterselftest", System.StringComparison.OrdinalIgnoreCase)) int.TryParse(a[i + 1], out rounds);
            if (rounds <= 0) return;
            var host = new GameObject("CraterSelfTest"); DontDestroyOnLoad(host);
            host.AddComponent<SelfTestHost>().StartCoroutine(SelfTestRun(Mathf.Min(rounds, 5000)));
        }

        static System.Collections.IEnumerator SelfTestRun(int rounds)
        {
            Debug.Log($"[OpenZ Craters] self test asked on the command line: {rounds} rounds on L17");
            SceneManager.LoadScene("L17_CarPark");
            yield return null; yield return null;
            string result = null;
            yield return SelfTest(rounds, s => result = s);
            Debug.Log("[OpenZ Craters] self test " + (result ?? "FAILED: no result"));
            Application.Quit(result != null && result.StartsWith("PASSED") ? 0 : 3);
        }

        // ------------------------------------------------------------------------------------------------ placement
        public static void OnBlast(Vector3 at, float s, FxBlastKind kind)
        {
            if (!Enabled) return;
            var f = Ensure();
            if (!f.ready) return;
            if (kind == FxBlastKind.Nuke || kind == FxBlastKind.Crate || kind == FxBlastKind.Debris || kind == FxBlastKind.Grenade) return;
            if (s < MinStrength) { f.Reject("small blast"); return; }
            float chance = s >= 1.6f ? 0.7f : 0.45f;
            if (Random.value > chance) { f.Reject("chance"); return; }
            f.TrySpawn(at, s, kind.ToString(), false);
        }

        /// <summary>Proof / test: a crater at the ground point under the screen centre (the filter still applies; chance skipped).</summary>
        public static string TestAtScreenCentre(float s = 1.7f)
        {
            if (!Enabled) return "deformable ground is OFF (Options)";
            var f = Ensure(); var cam = Camera.main;
            if (!f.ready || !cam || ZGame.I == null) return "not ready";
            if (!ZGame.I.GroundPoint(cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f)), out var p)) return "no ground under the screen centre";
            return f.TrySpawn(p, s, "test", true) ?? "spawned";
        }

        void Reject(string why) { rejects[why] = rejects.TryGetValue(why, out int c) ? c + 1 : 1; }

        string TrySpawn(Vector3 at, float s, string src, bool test)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var g = ZGame.I; var map = g.World.Map; bool city = map.planet == "city";
            int alive = 0; foreach (var k in craters) if (!k.dead && !k.removing) alive++;
            if (alive >= Cap) { Reject("cap 12"); return "cap 12 reached"; }
            var surf = TrackMarks.I ? TrackMarks.I.Surface : null;
            float roadC = 0f, waterC = 0f, rockC = 0f;
            surf?.Classes(at.x, at.z, out roadC, out waterC, out rockC);
            bool paved = city && roadC > 0.5f;
            string id = PickVariant(s, paved);
            if (id == null) { Reject("no variant"); return "no variant imported"; }
            var v = variants[id]; float R = v.R, M = R + 1f;
            float W = map.width * 2f, D = map.height * 2f;
            if (at.x < M + 4f || at.x > W - M - 4f || -at.z < M + 4f || -at.z > D - M - 4f) { Reject("map edge"); return "map edge"; }
            foreach (var k in craters) if (!k.dead && (new Vector2(k.c.x - at.x, k.c.z - at.z)).magnitude < k.v.R + R + 0.5f) { Reject("overlaps a crater"); return "overlaps a crater"; }
            float lo = float.MaxValue, hi = float.MinValue;
            for (int ring = 0; ring <= 2; ring++)
            {
                float rr = ring * M * 0.5f; int nseg = ring == 0 ? 1 : 12;
                for (int a = 0; a < nseg; a++)
                {
                    float th = a * Mathf.PI * 2f / nseg, x = at.x + Mathf.Cos(th) * rr, z = at.z + Mathf.Sin(th) * rr;
                    var cl = map.ClassAt(Mathf.FloorToInt(x * 8f) >> 4, Mathf.FloorToInt(-z * 8f) >> 4);
                    if (cl == ZClass.Water || cl == ZClass.DeepWater) { Reject("near water"); return "near water"; }
                    if (cl == ZClass.Impassable) { Reject("rock / impassable"); return "rock / impassable"; }
                    if (!city && cl == ZClass.Road) { Reject("road (L02)"); return "road"; }
                    if (surf != null)
                    {
                        surf.Classes(x, z, out float rw, out float ww, out float kw);
                        if (ww > 0.05f) { Reject("near water"); return "near water (paint)"; }
                        if (kw > 0.15f) { Reject("rock / impassable"); return "rock (paint)"; }
                        if (!city && rw > 0.35f) { Reject("road (L02)"); return "road (paint)"; }
                        if (city && (rw > 0.5f) != paved && rr <= R) { Reject("mixed ground"); return "mixed paving / dirt"; }
                    }
                    if (Physics.Raycast(new Ray(new Vector3(x, 200f, z), Vector3.down), out var hit, 400f, 1 << ZGame.WalkLayer, QueryTriggerInteraction.Ignore) && !IsGroundCollider(hit.collider))
                    { Reject("bridge / deck"); return "bridge / deck"; }
                    float y = RenderY(x, z); lo = Mathf.Min(lo, y); hi = Mathf.Max(hi, y);
                }
            }
            if (hi - lo > 0.6f) { Reject("slope"); return "slope"; }
            var px = ZGame.WorldToPx(at); float mpx = (R + 1.5f) * 8f;
            foreach (var o in g.World.Objects)
            {
                bool solid = o.Kind == ZKind.Building || o.Kind == ZKind.MapItem || o.Kind == ZKind.Cannon || (o.Kind == ZKind.Vehicle && o.Destroyed);
                if (!solid) continue;
                if (px.x > o.LocX - mpx && px.x < o.LocX + o.WidthPix + mpx && px.y > o.LocY - mpx && px.y < o.LocY + o.HeightPix + mpx) { Reject("building / prop / wreck"); return "building / prop / wreck: " + o.Type; }
            }
            int propMask = LayerMask.GetMask("Buildings", "Props");
            foreach (var c in Physics.OverlapSphere(at + Vector3.up * 0.8f, R + 0.5f, propMask, QueryTriggerInteraction.Ignore))
                if (c && !c.attachedRigidbody) { Reject("building / prop / wreck"); return "static prop: " + c.name; }
            if (!groundFor(at, out var gu)) { Reject("no ground UV"); return "no TER_Ground under it"; }
            Spawn(v, at, s, gu, paved, src);
            float ms = (float)sw.Elapsed.TotalMilliseconds; craters[craters.Count - 1].spawnMs = ms; maxSpawnMs = Mathf.Max(maxSpawnMs, ms);
            var cr = craters[craters.Count - 1];
            if (!quiet) Debug.Log($"[OpenZ Craters] +#{cr.serial} {id} at ({at.x:0.0}, {at.z:0.0}) {src} s {s:0.00}, R {R:0.00} m, depth {-v.HLocal(0, 0):0.00} m, spawn {ms:0.00} ms (main thread; chunk collider bakes in jobs), " +
                      $"chunks {cr.chunks.Count}, active {alive + 1}/{Cap}{(test ? " (TEST)" : "")}");
            return null;
        }

        string PickVariant(float s, bool paved)
        {
            string[] pool = paved ? (s >= 1.4f ? new[] { "Paved_B", "Paved_A" } : new[] { "Paved_A", "Paved_B" })
                                  : (s >= 1.4f ? new[] { "Sand_C", "Sand_D" } : new[] { "Sand_A", "Sand_B" });
            int i0 = Random.Range(0, 2);
            for (int i = 0; i < 2; i++) { var id = pool[(i0 + i) % 2]; if (variants.ContainsKey(id)) return id; }
            foreach (var kv in variants) if (kv.Value.paved == paved) return kv.Key;
            return null;
        }

        bool groundFor(Vector3 p, out (Renderer r, Bounds b, Vector3 ua, Vector3 va, Material m, int slot) gu)
        {
            foreach (var e in groundUv) if (p.x >= e.b.min.x && p.x <= e.b.max.x && p.z >= e.b.min.z && p.z <= e.b.max.z) { gu = e; return true; }
            gu = default; return false;
        }

        static float RenderY(float x, float z)
        {
            var s = TrackMarks.I ? TrackMarks.I.Surface : null;
            if (s != null && s.RenderY(x, z, out float y)) return y;
            var f = I;
            if (f && f.orig && f.orig.Raycast(new Ray(new Vector3(x, 200f, z), Vector3.down), out var h, 400f)) return h.point.y;
            return ZGame.I ? ZGame.I.GroundY(x, z) : 0f;
        }

        // ------------------------------------------------------------------------------------------------ spawn / seat
        static readonly string[] SandDecals = { "CraterDecal_Sand_01_Mul", "CraterDecal_Sand_02_Mul", "CraterDecal_Sand_03_Mul" }, PavedDecals = { "CraterDecal_Paved_01", "CraterDecal_Paved_02" };

        void Spawn(Variant v, Vector3 at, float s, (Renderer r, Bounds b, Vector3 ua, Vector3 va, Material m, int slot) gu, bool paved, string src)
        {
            var c = new Crater { serial = ++serial, v = v, c = new Vector3(at.x, RenderY(at.x, at.z), at.z), born = Time.time };
            float yaw = Random.Range(0f, Mathf.PI * 2f); c.cs = Mathf.Cos(yaw); c.sn = Mathf.Sin(yaw);
            c.root = new GameObject($"Crater_{c.serial}_{v.id}");
            // render patch: ground paint by planar UV0 at each vertex's world xz, decal by the patch's own UV1 turned by a random angle
            int nv = v.pv.Length;
            c.pBase = new Vector3[nv]; c.pOut = new Vector3[nv]; c.nG = new Vector3[nv]; c.nOut = new Vector3[nv];
            var uv0 = new Vector2[nv]; var uv1 = new Vector2[nv];
            float dr = Random.Range(0f, Mathf.PI * 2f), dc = Mathf.Cos(dr), ds = Mathf.Sin(dr);
            for (int i = 0; i < nv; i++)
            {
                var w = c.ToWorld(v.pv[i]); float by = RenderY(w.x, w.z);
                c.pBase[i] = new Vector3(w.x, by, w.z);
                c.nG[i] = GroundNormal(w.x, w.z, by);
                uv0[i] = new Vector2(gu.ua.x * w.x + gu.ua.y * w.z + gu.ua.z, gu.va.x * w.x + gu.va.y * w.z + gu.va.z);
                var d = v.puv1[i] - new Vector2(0.5f, 0.5f);
                uv1[i] = new Vector2(0.5f + dc * d.x - ds * d.y, 0.5f + ds * d.x + dc * d.y);
            }
            c.patch = new Mesh { name = c.root.name + "_patch" }; c.patch.MarkDynamic();
            c.patch.vertices = c.pBase; c.patch.uv = uv0; c.patch.uv2 = uv1; c.patch.triangles = v.ptri;
            var pg = new GameObject("Patch"); pg.transform.SetParent(c.root.transform, false); pg.layer = gu.r.gameObject.layer;
            pg.AddComponent<MeshFilter>().sharedMesh = c.patch;
            c.patchR = pg.AddComponent<MeshRenderer>(); c.patchR.sharedMaterial = PatchMaterial(gu.m, paved ? PavedDecals[Random.Range(0, PavedDecals.Length)] : SandDecals[Random.Range(0, SandDecals.Length)], !paved);
            c.patchR.shadowCastingMode = ShadowCastingMode.Off; c.patchR.receiveShadows = true;
            c.patchR.renderingLayerMask = gu.r.renderingLayerMask;
            {   // the ground's per-slot overrides (ShoreBeach paint B on L02) drawn on the patch too, so paint and patch match; role 2 on top
                var mpb = new MaterialPropertyBlock(); gu.r.GetPropertyBlock(mpb, gu.slot);
                if (!mpb.isEmpty) { mpb.SetFloat(RoleId, 2f); c.patchR.SetPropertyBlock(mpb); }
            }
            // seated clods / slabs (shown when the crater has eased in, as the thrown clods land)
            c.clods = new Mesh { name = c.root.name + "_clods" };
            if (v.kv != null)
            {
                c.kBase = new Vector3[v.kv.Length]; var kn = new Vector3[v.kv.Length];
                for (int i = 0; i < v.kv.Length; i++) { var w = c.ToWorld(v.kv[i]); c.kBase[i] = new Vector3(w.x, RenderY(w.x, w.z) + v.kv[i].y, w.z); kn[i] = c.DirToWorld(v.kn[i]); }
                c.clods.vertices = c.kBase; c.clods.normals = kn; c.clods.uv = v.kuv; c.clods.triangles = v.ktri; c.clods.RecalculateBounds();
            }
            var kg = new GameObject("Clods"); kg.transform.SetParent(c.root.transform, false); kg.layer = pg.layer;
            kg.AddComponent<MeshFilter>().sharedMesh = c.clods;
            c.clodR = kg.AddComponent<MeshRenderer>(); c.clodR.sharedMaterial = kitMat; c.clodR.enabled = false; c.clodR.shadowCastingMode = ShadowCastingMode.On;
            // bowl collider on the terrain collider's heights (live once the chunk under it is swapped)
            if (physicsOk)
            {
                var cvw = new Vector3[v.cv.Length];
                for (int i = 0; i < cvw.Length; i++) { var w = c.ToWorld(v.cv[i]); cvw[i] = new Vector3(w.x, OrigY(w.x, w.z, c.c.y) + v.cv[i].y, w.z); }
                c.colMesh = new Mesh { name = c.root.name + "_col" }; c.colMesh.vertices = cvw; c.colMesh.triangles = v.ctri; c.colMesh.RecalculateBounds();
                bakeChain.Complete();                                                            // strict: no bake job runs while the main thread bakes
                Physics.BakeMesh(c.colMesh.GetInstanceID(), false, orig.cookingOptions);        // crash fix: once, here, never in a job (it was baked by every chunk job at once)
                var cgo = new GameObject("COL_Crater") { layer = ZGame.WalkLayer }; cgo.transform.SetParent(c.root.transform, false);
                c.col = cgo.AddComponent<MeshCollider>(); c.col.enabled = false; c.col.cookingOptions = orig.cookingOptions; c.col.sharedMaterial = orig.sharedMaterial;
                craterCols.Add(c.col);
                foreach (var ch in chunks)
                {
                    if (ch == null) continue;
                    if (ch.b.max.x < c.c.x - v.R - 1f || ch.b.min.x > c.c.x + v.R + 1f || ch.b.max.z < c.c.z - v.R - 1f || ch.b.min.z > c.c.z + v.R + 1f) continue;
                    ch.craters.Add(c); c.chunks.Add(ch);
                }
            }
            craters.Add(c); spawned++;
            Seat(c);
            Mirror(); PushGlobals();
            foreach (var ch in c.chunks) ScheduleChunk(ch);
            OpenZFxPlayer.I?.InvalidateGround(c.c, v.R + 0.5f);
            Throw(c, s, paved);
        }

        Material PatchMaterial(Material ground, string decal, bool mul)
        {
            string key = (ground ? ground.GetInstanceID() : 0) + "|" + decal;
            if (patchMats.TryGetValue(key, out var m) && m) return m;
            m = new Material(ground) { name = "CraterPatch_" + decal };
            m.SetFloat(RoleId, 2f);
            var tex = Resources.Load<Texture2D>(Res + "Decals/" + decal);
            if (tex) m.SetTexture(DecalId, tex);
            m.SetFloat(DecalMulId, mul ? 1f : 0f);
            patchMats[key] = m;
            return m;
        }

        static Vector3 GroundNormal(float x, float z, float y)
        {
            const float e = 0.3f;
            float hx = RenderY(x + e, z) - RenderY(x - e, z), hz = RenderY(x, z + e) - RenderY(x, z - e);
            return new Vector3(-hx, 2f * e, -hz).normalized;
        }

        void Seat(Crater c)
        {
            var v = c.v; float e = Mathf.SmoothStep(0f, 1f, c.ease);
            for (int i = 0; i < c.pBase.Length; i++)
            {
                c.pOut[i] = new Vector3(c.pBase[i].x, c.pBase[i].y + e * v.pv[i].y, c.pBase[i].z);
                var ln = c.DirToWorld(v.pn[i]);
                c.nOut[i] = (c.nG[i] + e * (ln - Vector3.up)).normalized;                     // the outer ring keeps exactly the ground normal
            }
            c.patch.vertices = c.pOut; c.patch.normals = c.nOut; c.patch.RecalculateBounds();
        }

        // ------------------------------------------------------------------------------------------------ thrown clods (dirt patter on landing)
        void Throw(Crater c, float s, bool paved)
        {
            var kit = paved && chipKit.Length > 0 ? chipKit : clodKit;
            if (kit.Length == 0 || !kitMat) { AudioDirector.Fire("dst_crater_clods", c.c); return; }
            int count = Mathf.RoundToInt(Mathf.Lerp(6f, 10f, Mathf.InverseLerp(1.15f, 2.2f, s)));
            for (int i = 0; i < count; i++)
            {
                float a = Random.Range(0f, Mathf.PI * 2f), sp = Random.Range(2.5f, 4.2f);
                float tf = Random.Range(0.28f, 0.48f);                                           // lands while the crater eases in (sfx_batch_v1: ~0.25 s)
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var p0 = c.c + dir * Random.Range(0.2f, c.v.R * 0.3f) + Vector3.up * 0.25f;
                float dist = sp * tf; var target = p0 + dir * dist;
                float vy = (FinalY(target.x, target.z) - p0.y + 0.5f * 9.81f * tf * tf) / tf;
                thrown.Add(new Thrown { m = kit[Random.Range(0, kit.Length)], p = p0, v = dir * sp + Vector3.up * vy, q = Random.rotation, spin = Random.insideUnitSphere * 540f, scale = Random.Range(0.8f, 1.25f), owner = c });
            }
        }

        static float FinalY(float x, float z) => (ZGame.I ? ZGame.I.GroundY(x, z) : 0f) + FinalMissing(x, z);

        void UpdateThrown()
        {
            if (thrown.Count == 0) return;
            float dt = Time.deltaTime; bool anySound = false;
            for (int i = thrown.Count - 1; i >= 0; i--)
            {
                var t = thrown[i]; t.t += dt;
                if (t.rest < 0f)
                {
                    t.v.y -= 9.81f * dt; t.p += t.v * dt; t.q = Quaternion.Euler(t.spin * dt) * t.q;
                    float gy = FinalY(t.p.x, t.p.z) + 0.04f * t.scale;
                    if (t.p.y <= gy && t.v.y < 0f)
                    {
                        t.p.y = gy; t.rest = 0f;
                        if (!t.owner.dead && !anySound && !t.sounded)
                        {   // one patter per crater, on the first clod's contact frame
                            bool first = true; foreach (var o in thrown) if (o.owner == t.owner && o.sounded) first = false;
                            if (first) { AudioDirector.Fire("dst_crater_clods", t.p); OzSoundLog.Add("craters", "dst_crater_clods", "sd_crater_clods_*", "clods land", 1f, t.p); anySound = true; }
                            t.sounded = true;
                        }
                    }
                }
                else
                {
                    t.rest += dt;
                    t.p.y = FinalY(t.p.x, t.p.z) + 0.04f * t.scale - Mathf.Max(0f, t.rest - 3.5f) * 0.08f;   // settle, then sink away after 3.5 s
                    if (t.rest > 5f || t.owner.dead) { thrown.RemoveAt(i); continue; }
                }
                Graphics.RenderMesh(new RenderParams(kitMat) { shadowCastingMode = ShadowCastingMode.On, receiveShadows = true }, t.m, 0, Matrix4x4.TRS(t.p, t.q, Vector3.one * t.scale));
            }
        }

        // ------------------------------------------------------------------------------------------------ removal
        void Housekeep()
        {
            var cam = Camera.main;
            var planes = cam ? GeometryUtility.CalculateFrustumPlanes(cam) : null;
            foreach (var c in craters.ToArray())
            {
                if (c.dead || c.removing || Time.time - c.born < Life) continue;
                var b = new Bounds(c.c, new Vector3(c.v.R * 2f + 2f, 3f, c.v.R * 2f + 2f));
                bool seen = planes != null && GeometryUtility.TestPlanesAABB(planes, b);
                if (seen) { c.offSince = -1f; continue; }
                if (c.offSince < 0f) { c.offSince = Time.time; continue; }
                if (Time.time - c.offSince < 1f) continue;
                string busy = Busy(c);
                if (busy != null) { if (Time.frameCount % 600 < 30) Debug.Log($"[OpenZ Craters] #{c.serial} due but kept: {busy}"); continue; }
                Remove(c, $"age {Time.time - c.born:0} s, off screen {Time.time - c.offSince:0.0} s, nothing resting");
            }
            if (Time.time > nextSummary)
            {
                nextSummary = Time.time + 30f;
                int alive = 0; foreach (var k in craters) if (!k.dead) alive++;
                var sb = new System.Text.StringBuilder(); foreach (var kv in rejects) sb.Append($" {kv.Key} {kv.Value},");
                Debug.Log($"[OpenZ Craters] summary: {alive}/{Cap} active, {spawned} made, update {(updFrames > 0 ? updMsSum / updFrames : 0f):0.000} ms/frame avg, worst spawn {maxSpawnMs:0.00} ms, rejected:{sb.ToString().TrimEnd(',')}");
                updMsSum = 0f; updFrames = 0;
            }
        }
        float nextSummary;

        string Busy(Crater c)
        {
            var g = ZGame.I;
            if (g != null && g.World != null)
                foreach (var o in g.World.Objects)
                {
                    if (!(o.IsMobile || (o.Kind == ZKind.Vehicle && o.Destroyed))) continue;
                    var w = g.CenterWorld(o, false);
                    if (Sq(w.x - c.c.x) + Sq(w.z - c.c.z) < Sq(c.v.R + 0.6f)) return (o.Destroyed ? "wreck " : "unit ") + o.Type;
                }
            foreach (var col in Physics.OverlapSphere(c.c, c.v.R * 0.9f, ~0, QueryTriggerInteraction.Ignore))
                if (col && col.attachedRigidbody && !col.attachedRigidbody.isKinematic) return "body " + col.attachedRigidbody.name;
            foreach (var t in thrown) if (t.owner == c) return "clods in flight";
            return null;
        }

        void Remove(Crater c, string why)
        {
            c.removing = true;
            if (!quiet) Debug.Log($"[OpenZ Craters] -#{c.serial} {c.v.id} removed ({why})");
            if (c.chunks.Count == 0) { Kill(c); return; }
            foreach (var ch in c.chunks) { ch.craters.Remove(c); ScheduleChunk(ch); }
        }

        void Kill(Crater c)
        {
            if (c.dead) return;
            c.dead = true;
            foreach (var ch in c.chunks) ch.craters.Remove(c);
            if (c.col) craterCols.Remove(c.col);
            ScorchDecals.ClearIn(c.c, c.v.R);
            if (c.root) Destroy(c.root);
            if (c.patch) Destroy(c.patch); if (c.clods) Destroy(c.clods); if (c.colMesh) Destroy(c.colMesh);
            craters.Remove(c);
            Mirror(); PushGlobals();
            OpenZFxPlayer.I?.InvalidateGround(c.c, c.v.R + 0.5f);
        }

        void ClearAll(string why)
        {
            var list = new List<Crater>(craters);
            if (list.Count > 0 && !quiet) Debug.Log($"[OpenZ Craters] clearing {list.Count} crater(s): {why}");
            if (chunks != null)
                foreach (var ch in chunks)
                {
                    if (ch == null) continue;
                    if (ch.busy) { ch.job.Complete(); FinishChunk(ch, false); }
                    ch.craters.Clear(); ch.pendingSet.Clear();
                    var old = ch.mc.sharedMesh; if (old != ch.orig) { ch.mc.sharedMesh = ch.orig; if (old) Destroy(old); }
                    if (ch.pending && ch.pending != ch.orig && ch.pending != old) Destroy(ch.pending);
                    ch.pending = null;
                }
            foreach (var c in list) Kill(c);
            thrown.Clear();
        }

        void Mirror()
        {
            n = 0;
            foreach (var c in craters) if (!c.dead && n < Cap) act[n++] = c;
        }

        void PushGlobals()
        {
            int k = 0;
            for (int i = 0; i < n; i++)
            {
                var c = act[i];
                glob[k++] = new Vector4(c.c.x, c.c.z, c.v.R * Mathf.Cos(Mathf.PI / 64f) - 0.002f, c.ease);   // the clip circle = the render patch's 64-gon inner circle
            }
            for (; k < Cap; k++) glob[k] = Vector4.zero;
            Shader.SetGlobalVectorArray(CratersId, glob);
            Shader.SetGlobalFloat(CountId, n);
        }

        // ------------------------------------------------------------------------------------------------ the height API (hot paths)
        /// <summary>True for the terrain collider, its chunks and the crater bowls (walk surface = ground, not a bridge deck).</summary>
        public static bool IsGroundCollider(Collider c) => c != null && (terrainCols.Contains(c) || craterCols.Contains(c));

        /// <summary>ZGame.GroundY: what to add to a walk-layer hit. 0 on decks and on a live bowl collider (it has the dent).</summary>
        public static float GroundAdd(Collider hit, float x, float z)
        {
            if (n == 0 || hit == null || craterCols.Contains(hit) || !terrainCols.Contains(hit)) return 0f;
            float s = 0f;
            for (int i = 0; i < n; i++) { var c = act[i]; if (!c.live) s += Mathf.SmoothStep(0f, 1f, c.ease) * c.H(x, z); }
            return s;
        }

        /// <summary>The rendered dent now (eased): add to a flat height (RenderY, the original collider).</summary>
        public static float DentRender(float x, float z)
        {
            if (n == 0) return 0f;
            float s = 0f;
            for (int i = 0; i < n; i++) { var c = act[i]; s += Mathf.SmoothStep(0f, 1f, c.ease) * c.H(x, z); }
            return s;
        }

        /// <summary>The part of the dent still to come (craters easing in): scorch marks and FX ground land on the final bowl.</summary>
        public static float FinalMissing(float x, float z)
        {
            if (n == 0) return 0f;
            float s = 0f;
            for (int i = 0; i < n; i++) { var c = act[i]; if (c.ease < 1f) s += (1f - Mathf.SmoothStep(0f, 1f, c.ease)) * c.H(x, z); }
            return s;
        }

        public static int ActiveCount => n;
    }
}
