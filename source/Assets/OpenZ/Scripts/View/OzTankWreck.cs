using System.Collections.Generic;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenZ
{
    /// <summary>
    /// Version 1.1, item 378 (the request: a tank wreck "reads as 4 perfect black boxes attached to each other, improve wreck to be falling apart
    /// not so ideal 4 boxes"). A tank's wreck used to break into its baked bins (x halves x z halves x 3 layers, cut at the bin walls, each
    /// squashed onto a rounded dark core block), and the break's 12-group budget glued some thirty bins and wheels into one rigid clump: a
    /// few black rounded boxes stuck together. Now (switch <see cref="On"/>; off = exactly the old break) a light or heavy tank breaks
    /// into its own model's broken pieces:
    /// <list type="bullet">
    /// <item>the hull: a few seeded fracture cuts that are never on a grid (across, along the bigger half, a wedge off the other half, two
    /// corner shards; OzTankWreck.Cut.cs with the cannon's fracture cut: jagged, stepped, relief faces with thickness, the lip bent in);</item>
    /// <item>both tracks torn where the hull broke across (torn ends, plates with a wall), each length riding with the hull piece over it,
    /// the road wheels and lamps on the piece they sit in, one wheel loose (<see cref="Regroup"/>);</item>
    /// <item>the paint is the unit's own (its own materials and team colour), burnt less than the old near-black wreck
    /// (<see cref="PieceChar"/>) and sooted in patches and along every break by vertex colours; the broken faces are dark torn steel
    /// (the cannon's fracture maps, no grid, nothing white); no core blocks.</item>
    /// </list>
    /// <see cref="Variants"/>
    /// after the level starts and for a new model at its first death); the main thread only reads the baked fragments (one part a frame)
    /// and builds the pieces' meshes (within <see cref="FrameMs"/> a frame). A break that would come before its set is ready waits for it
    /// (at most <see cref="HoldMax"/> s, the wreck burns on meanwhile), else the old break. The pieces are WreckPieces' own bodies: the
    /// rocks and statics rules (items 331 / 332) are unchanged. View only.
    /// </summary>
    public static partial class OzTankWreck
    {
        public static bool On = true;
        /// <summary>The vehicle types that break this way.</summary>
        public static readonly HashSet<string> Types = new HashSet<string> { "light", "heavy" };
        /// <summary>Break-ups prepared per tank model (each tank takes one by its id).</summary>
        public static int Variants = 3;
        /// <summary>The pieces' paint burn (TeamPaint Charred scale: 0.82 = the old near-black wreck, 0 = clean paint).</summary>
        public static float PieceChar = 0.66f;
        /// <summary>The broken steel (sRGB, x its texture and the vertex colours), metallic and smoothness over its mask map.</summary>
        public static Color FractureColor = new Color(0.18f, 0.176f, 0.172f, 1f);
        public static float FractureMetallic = 0.15f, FractureSmoothness = 0.12f;
        /// <summary>The longest a break waits for its set (s); the main thread's share of the work in one frame (ms); loose parts kept.</summary>
        public static float HoldMax = 2f, FrameMs = 3f;
        public static int LooseParts = 1;
        /// <summary>The sets of the tanks on the map are made this long after the level is running (s).</summary>
        public static float WarmDelay = 6f;
        public const string FractureDir = "OpenZ/FortV2/Wreck/";

        // ------------------------------------------------------------------ proof
        public static int SetsQueued, SetsReady, SetsFailed, Breaks, PiecesMade, NotReady, Holds, HoldTimeouts, CapFaces, CoreFallbacks;
        public static float WorkerMsMax, FrameMsMax, HoldWaitMax, LastHoldWait;
        public static string LastPlan = "-", LastError = "-", LastBreak = "-", MatState = "-";
        /// <summary>The pieces of the latest break (their transforms; the bodies are their parents).</summary>
        public static readonly List<Transform> LastPieces = new List<Transform>();
        public static string State => $"tank wreck look {(On ? "on" : "OFF")}: sets ready {SetsReady} of {SetsQueued} (failed {SetsFailed}{(SetsFailed > 0 ? ": " + LastError : "")}), " +
            $"the slowest set on the background thread {WorkerMsMax:0} ms, the main thread's heaviest frame {FrameMsMax:0.0} ms; breaks {Breaks} ({PiecesMade} pieces), breaks before a set was ready {NotReady}, " +
            $"waits {Holds} frames (longest {HoldWaitMax:0.00} s, timed out {HoldTimeouts}); broken faces {CapFaces} ({MatState}; core fallbacks {CoreFallbacks}); last: {LastBreak}";

        // ------------------------------------------------------------------ sets
        enum Phase { Read, Work, Mesh, Ready, Failed }

        sealed class Src
        {
            public MeshRenderer mr; public Mesh mesh; public readonly List<Mesh> frags = new List<Mesh>(); public int subs;
            public OzMeshSlice.Part part; public bool hull; public Matrix4x4 toHull; public string label; public float sc;
        }

        sealed class Built { public Mesh mesh; public string name; public bool capped; }

        sealed class TankSet
        {
            public Mesh key; public int variant, seed, tries; public Phase phase = Phase.Read;
            public readonly List<Src> srcs = new List<Src>(); public int readAt;
            public Vector3 up; public float sc;
            public readonly List<(int src, Piece p)> cut = new List<(int, Piece)>(); public int meshAt;
            public readonly Dictionary<Mesh, List<Built>> bySource = new Dictionary<Mesh, List<Built>>();
            public CutStats stats = new CutStats(); public string plan = "-", error; public float workerMs;
            public readonly ManualResetEventSlim done = new ManualResetEventSlim(false);
        }

        static readonly Dictionary<(Mesh, int), TankSet> sets = new Dictionary<(Mesh, int), TankSet>();
        static readonly List<TankSet> queue = new List<TankSet>();
        static readonly Dictionary<UnitView, TankSet> unitSet = new Dictionary<UnitView, TankSet>();
        static readonly HashSet<UnitView> madeFor = new HashSet<UnitView>(), notReady = new HashSet<UnitView>();

        static bool Applies(UnitView v) => On && v && v.Obj != null && v.Obj.Type != null && Types.Contains(v.Obj.Type);

        static int VariantOf(UnitView v) => Variants <= 1 ? 0 : (int)(RobotDeathRule.Hash(v.Obj.Id, 7, 378u) % (uint)Variants);

        static int Fnv(string s) { unchecked { uint h = 2166136261u; foreach (char c in s) { h ^= c; h *= 16777619u; } return (int)(h & 0x7fffffff); } }

        /// <summary>The renderers of `v` with baked fragments (the hull first: the biggest), their fragments, the hull's axes and scale.</summary>
        static bool Gather(UnitView v, TankSet s)
        {
            var list = new List<Src>();
            foreach (var mr in v.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!mr || !mr.TryGetComponent<MeshFilter>(out var mf) || !mf.sharedMesh) continue;
                var holder = mr.transform.Find("OZ_WreckFragments"); if (!holder) continue;
                var src = new Src { mr = mr, mesh = mf.sharedMesh, subs = Mathf.Max(1, mr.sharedMaterials.Length) };
                foreach (Transform c in holder)
                {
                    if (!c.name.StartsWith("frag_") || !c.TryGetComponent<MeshFilter>(out var cmf) || !cmf.sharedMesh) continue;
                    if (c.localPosition.sqrMagnitude > 1e-10f || Quaternion.Angle(c.localRotation, Quaternion.identity) > 0.01f || (c.localScale - Vector3.one).sqrMagnitude > 1e-8f) return false;
                    if (holder.localPosition.sqrMagnitude > 1e-10f || Quaternion.Angle(holder.localRotation, Quaternion.identity) > 0.01f || (holder.localScale - Vector3.one).sqrMagnitude > 1e-8f) return false;
                    if (!cmf.sharedMesh.isReadable) return false;
                    if (!src.frags.Contains(cmf.sharedMesh)) src.frags.Add(cmf.sharedMesh);
                    src.subs = Mathf.Max(src.subs, cmf.sharedMesh.subMeshCount);
                }
                if (src.frags.Count == 0) continue;
                list.Add(src);
            }
            if (list.Count == 0) return false;
            float Vol(Src x) { var b = x.mesh.bounds.size; var ls = x.mr.transform.lossyScale; return Mathf.Abs(b.x * b.y * b.z * ls.x * ls.y * ls.z); }
            list.Sort((a, b) => Vol(b).CompareTo(Vol(a)));
            var hull = list[0].mr.transform; list[0].hull = true;
            var hw = hull.worldToLocalMatrix;
            int side = 0;
            foreach (var x in list)
            {
                x.toHull = hw * x.mr.transform.localToWorldMatrix;
                var ls = x.mr.transform.lossyScale; x.sc = Mathf.Max(1e-4f, (Mathf.Abs(ls.x) + Mathf.Abs(ls.y) + Mathf.Abs(ls.z)) / 3f);
                if (!x.hull)
                {
                    string n = x.mr.name.ToLowerInvariant();
                    x.label = "belt_" + (n.EndsWith("_l") || n.Contains("left") ? "l" : n.EndsWith("_r") || n.Contains("right") ? "r" : ((char)('a' + side)).ToString()); side++;
                }
            }
            s.srcs.Clear(); s.srcs.AddRange(list);
            s.up = hull.InverseTransformDirection(v.transform.up).normalized; s.sc = list[0].sc;
            return true;
        }

        /// <summary>UnitView.BeginWreck (and the level's warm-up): the break-up of this tank's model and variant, made now if it is not
        /// made yet. Null = this unit does not break this way.</summary>
        public static void Want(UnitView v) { WantSet(v); }

        static TankSet WantSet(UnitView v)
        {
            if (!Applies(v)) return null;
            if (unitSet.TryGetValue(v, out var known) && known != null && (known.phase != Phase.Failed || known.tries >= 3)) return known.phase == Phase.Failed ? null : known;
            var hullMesh = HullMesh(v); if (!hullMesh) return null;
            int variant = VariantOf(v);
            if (!sets.TryGetValue((hullMesh, variant), out var s) || (s.phase == Phase.Failed && s.tries < 3))
            {
                var tries = s != null ? s.tries : 0;
                s = new TankSet { key = hullMesh, variant = variant, tries = tries + 1, seed = Fnv(hullMesh.name) % 100000 + variant * 977 + 378 };
                if (!Gather(v, s)) { s.phase = Phase.Failed; s.error = "no complete baked fragments"; sets[(hullMesh, variant)] = s; unitSet[v] = s; return null; }
                sets[(hullMesh, variant)] = s; queue.Add(s); SetsQueued++;
                OzTankWreckRunner.Ensure(); WarmFracture();
            }
            unitSet[v] = s;
            return s.phase == Phase.Failed ? null : s;
        }

        /// <summary>The level's warm-up: every variant of every tank model on the map (one tank of each model reads it).</summary>
        public static void WarmAll()
        {
            if (!On) return;
            var seen = new HashSet<Mesh>();
            foreach (var v in Object.FindObjectsByType<UnitView>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (!Applies(v) || v.Dying) continue;
                var m = HullMesh(v); if (!m || !seen.Add(m)) continue;
                for (int k = 0; k < Mathf.Max(1, Variants); k++)
                {
                    if (sets.ContainsKey((m, k))) continue;
                    var s = new TankSet { key = m, variant = k, tries = 1, seed = Fnv(m.name) % 100000 + k * 977 + 378 };
                    if (!Gather(v, s)) break;
                    sets[(m, k)] = s; queue.Add(s); SetsQueued++;
                }
            }
            if (queue.Count > 0) OzTankWreckRunner.Ensure();
            if (seen.Count > 0) WarmFracture();
        }

        static Mesh HullMesh(UnitView v)
        {
            Mesh best = null; float bv = -1f;
            foreach (var mr in v.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!mr || !mr.transform.Find("OZ_WreckFragments") || !mr.TryGetComponent<MeshFilter>(out var mf) || !mf.sharedMesh) continue;
                var b = mf.sharedMesh.bounds.size; var ls = mr.transform.lossyScale; float vol = Mathf.Abs(b.x * b.y * b.z * ls.x * ls.y * ls.z);
                if (vol > bv) { bv = vol; best = mf.sharedMesh; }
            }
            return best;
        }

        /// <summary>UnitView.UpdateWreck: true = this tank's break waits a frame (its set is still being made; at most HoldMax s).</summary>
        public static bool Hold(UnitView v, float waited)
        {
            var s = WantSet(v);
            if (s == null || s.phase == Phase.Ready || s.phase == Phase.Failed) return false;
            if (waited >= HoldMax) { HoldTimeouts++; return false; }
            Holds++; LastHoldWait = waited; if (waited > HoldWaitMax) HoldWaitMax = waited;
            return true;
        }

        // ------------------------------------------------------------------ the frame's share (OzTankWreckRunner.Update)
        internal static void Tick()
        {
            if (queue.Count == 0) return;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var s = queue[0];
            try
            {
                switch (s.phase)
                {
                    case Phase.Read:
                        if (s.readAt < s.srcs.Count)
                        {   // one part a frame: the renderer's baked fragments together (its whole mesh, every attribute)
                            var x = s.srcs[s.readAt++];
                            x.part = ReadFrags(x);
                            if (x.part == null) throw new System.Exception("could not read the fragments of " + (x.mr ? x.mr.name : "?"));
                        }
                        if (s.readAt >= s.srcs.Count)
                        {
                            s.phase = Phase.Work;
                            foreach (var x in s.srcs) x.mr = null;                                           // the unit may go: nothing of it is needed now
                            ThreadPool.QueueUserWorkItem(_ => Work(s));
                        }
                        break;
                    case Phase.Work:
                        if (!s.done.IsSet) break;
                        if (s.error != null) throw new System.Exception(s.error);
                        s.phase = Phase.Mesh;
                        break;
                    case Phase.Mesh:
                        while (s.meshAt < s.cut.Count)
                        {
                            var (si, p) = s.cut[s.meshAt++];
                            var src = s.srcs[si];
                            var b = new Built { name = "OZ_TankPiece_" + p.name, capped = p.part.CapTris > 0 };
                            b.mesh = OzMeshSlice.ToMesh(p.part, src.mesh.name + "_" + p.name);
                            if (!s.bySource.TryGetValue(src.mesh, out var l)) s.bySource[src.mesh] = l = new List<Built>();
                            l.Add(b);
                            if (sw.Elapsed.TotalMilliseconds > FrameMs * 0.6f) break;
                        }
                        if (s.meshAt >= s.cut.Count)
                        {
                            s.phase = Phase.Ready; SetsReady++; s.cut.Clear();
                            foreach (var x in s.srcs) x.part = null;
                            queue.RemoveAt(0);
                            LastPlan = $"{s.key.name} variant {s.variant}: {s.plan}";
                        }
                        break;
                    default: queue.RemoveAt(0); break;
                }
            }
            catch (System.Exception e)
            {
                s.phase = Phase.Failed; SetsFailed++; LastError = e.GetType().Name + ": " + e.Message; queue.Remove(s);
                Debug.LogWarning("[OpenZ] tank wreck: a break-up could not be made (" + LastError + "): that tank breaks the old way");
            }
            float ms = (float)sw.Elapsed.TotalMilliseconds; if (ms > FrameMsMax) FrameMsMax = ms;
        }

        /// <summary>A renderer's baked fragments as one part (each fragment's own sub-meshes kept, a last empty sub-mesh for the caps).</summary>
        static OzMeshSlice.Part ReadFrags(Src x)
        {
            var all = new OzMeshSlice.Part(x.subs + 1) { hn = true, h0 = true, h1 = true, hc = true, ht = true };
            bool any = false;
            foreach (var m in x.frags)
            {
                var p = OzMeshSlice.FromMesh(m); if (p == null) return null;
                if (!any) { all.hn = p.hn; all.h0 = p.h0; all.h1 = p.h1; all.hc = p.hc; all.ht = p.ht; any = true; }
                else { all.hn &= p.hn; all.h0 &= p.h0; all.h1 &= p.h1; all.hc &= p.hc; all.ht &= p.ht; }
                int b0 = all.v.Count; all.v.AddRange(p.v);
                for (int s = 0; s < p.sub.Length - 1; s++) { int to = Mathf.Min(s, x.subs - 1); foreach (int i in p.sub[s]) all.sub[to].Add(b0 + i); }
            }
            if (!all.hc) { for (int i = 0; i < all.v.Count; i++) { var v = all.v[i]; v.c = Color.white; all.v[i] = v; } }
            return any && all.Tris > 0 ? all : null;
        }

        static void Work(TankSet s)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                lock (OzFortGunBreakup.FractureLock)                                                      // one fracture job at a time (the cannon's too)
                {
                    var keep = Save();
                    try { Cut(s); }
                    finally { Restore(keep); }                                                          // the cannon's and the shuttle's counters stay their own
                }
            }
            catch (System.Exception e) { s.error = e.GetType().Name + ": " + e.Message; }
            finally
            {
                s.workerMs = (float)clock.Elapsed.TotalMilliseconds;
                if (s.workerMs > WorkerMsMax) WorkerMsMax = s.workerMs;
                s.done.Set();
            }
        }

        static void Cut(TankSet s)
        {
            var log = new StringBuilder();
            var hull = s.srcs[0];
            // the hull's long axis: the larger of its two extents across `up`
            var box = hull.part.Box().size; var up = s.up.sqrMagnitude > 0.5f ? s.up : Vector3.up;
            Vector3 fwd = Vector3.forward; float best = -1f;
            for (int a = 0; a < 3; a++)
            {
                var ax = Vector3.zero; ax[a] = 1f;
                if (Mathf.Abs(Vector3.Dot(ax, up)) > 0.7f) continue;
                if (box[a] > best) { best = box[a]; fwd = ax; }
            }
            var hp = CutHull(hull.part, up, fwd, hull.sc, s.seed, s.stats, log, out var pn, out var pw);
            for (int i = 0; i < hp.Count; i++) { Soot(hp[i].part, hull.sc, s.seed + i * 7, up); s.cut.Add((0, hp[i])); }
            for (int k = 1; k < s.srcs.Count; k++)
            {
                var x = s.srcs[k]; var A = x.toHull;                                                  // this part's mesh space -> the hull's
                var nt = new Vector3(A.m00 * pn.x + A.m10 * pn.y + A.m20 * pn.z, A.m01 * pn.x + A.m11 * pn.y + A.m21 * pn.z, A.m02 * pn.x + A.m12 * pn.y + A.m22 * pn.z);
                float wt = pw - (pn.x * A.m03 + pn.y * A.m13 + pn.z * A.m23);
                var tp = CutTrack(x.part, nt, wt, s.seed + k * 31, x.label, s.stats, log);
                var upT = new Vector3(A.m00 * up.x + A.m10 * up.y + A.m20 * up.z, A.m01 * up.x + A.m11 * up.y + A.m21 * up.z, A.m02 * up.x + A.m12 * up.y + A.m22 * up.z).normalized;
                for (int i = 0; i < tp.Count; i++) { Soot(tp[i].part, x.sc, s.seed + 100 + k * 13 + i * 7, upT); s.cut.Add((k, tp[i])); }
            }
            int tris = 0; foreach (var (_, p) in s.cut) tris += p.part.Tris; s.stats.tris = tris;
            s.plan = $"{s.cut.Count} pieces, {tris} triangles; fracture cuts {s.stats.cuts} (plain {s.stats.plain}{(s.stats.plain > 0 ? ": " + s.stats.error : "")}), torn tracks {s.stats.tears}, plates {s.stats.plates}, shards {s.stats.shards}; {log}";
        }

        sealed class Counters
        {
            public int cuts, fallbacks, caps, capTris, closures, tears, plain, plates, inner, wall, edges, relief, reliefPlain;
            public float maxMs, lastMs, deepest, refine, clip, lip, cap, shellMax; public string error, shellError;
        }
        static Counters Save() => new Counters
        {
            cuts = OzFractureCut.Cuts, fallbacks = OzFractureCut.Fallbacks, caps = OzFractureCut.Caps, capTris = OzFractureCut.CapTrisMade, closures = OzFractureCut.Closures,
            maxMs = OzFractureCut.MaxMs, lastMs = OzFractureCut.LastMs, deepest = OzFractureCut.Deepest, refine = OzFractureCut.MsRefine, clip = OzFractureCut.MsClip,
            lip = OzFractureCut.MsLip, cap = OzFractureCut.MsCap, error = OzFractureCut.LastError,
            tears = OzFractureCut.ShellTears, plain = OzFractureCut.ShellPlain, plates = OzFractureCut.Plates, inner = OzFractureCut.PlateInnerTris, wall = OzFractureCut.PlateWallTris,
            edges = OzFractureCut.PlateEdges, relief = OzFractureCut.ReliefCuts, reliefPlain = OzFractureCut.ReliefPlain, shellMax = OzFractureCut.ShellMaxMs, shellError = OzFractureCut.ShellError
        };
        static void Restore(Counters c)
        {
            OzFractureCut.Cuts = c.cuts; OzFractureCut.Fallbacks = c.fallbacks; OzFractureCut.Caps = c.caps; OzFractureCut.CapTrisMade = c.capTris; OzFractureCut.Closures = c.closures;
            OzFractureCut.MaxMs = c.maxMs; OzFractureCut.LastMs = c.lastMs; OzFractureCut.Deepest = c.deepest; OzFractureCut.MsRefine = c.refine; OzFractureCut.MsClip = c.clip;
            OzFractureCut.MsLip = c.lip; OzFractureCut.MsCap = c.cap; OzFractureCut.LastError = c.error;
            OzFractureCut.ShellTears = c.tears; OzFractureCut.ShellPlain = c.plain; OzFractureCut.Plates = c.plates; OzFractureCut.PlateInnerTris = c.inner; OzFractureCut.PlateWallTris = c.wall;
            OzFractureCut.PlateEdges = c.edges; OzFractureCut.ReliefCuts = c.relief; OzFractureCut.ReliefPlain = c.reliefPlain; OzFractureCut.ShellMaxMs = c.shellMax; OzFractureCut.ShellError = c.shellError;
        }

        // ------------------------------------------------------------------ the break (WreckPieces.Break)
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor"), TeamColorId = Shader.PropertyToID("_TeamColor"), VividId = Shader.PropertyToID("_OZTeamVivid"),
                            GlowId = Shader.PropertyToID("_OZTeamGlow"), UseVcId = Shader.PropertyToID("_UseVertexColor");
        static MaterialPropertyBlock blk;

        /// <summary>WreckPieces.Break, for a renderer with baked fragments: its broken pieces instead of the bins (each its own group, children of
        /// the renderer at the same place, the renderer hidden). False = the old way for this renderer.</summary>
        internal static bool Pieces(UnitView view, MeshRenderer mr, MeshFilter mf, Transform frag, List<List<Transform>> groups, float charred)
        {
            if (!Applies(view) || !mr || !mf || !mf.sharedMesh) return false;
            if (!unitSet.TryGetValue(view, out var s) || s == null) s = WantSet(view);
            if (s == null || s.phase != Phase.Ready) { if (s != null && notReady.Add(view)) NotReady++; return false; }
            if (!s.bySource.TryGetValue(mf.sharedMesh, out var list) || list.Count == 0) return false;
            if (madeFor.Add(view)) { LastPieces.Clear(); Breaks++; LastBreak = $"{view.Obj.Type} #{view.Obj.Id} ({s.key.name} variant {s.variant}): {s.plan}"; }
            var src = mr.sharedMaterials;
            blk ??= new MaterialPropertyBlock();
            float kNow = Mathf.Max(0.05f, 1f - 0.78f * Mathf.Clamp01(charred)), kNew = 1f - 0.78f * Mathf.Clamp01(PieceChar), f = kNew / kNow;
            foreach (var b in list)
            {
                var go = new GameObject(b.name) { layer = mr.gameObject.layer };
                go.transform.SetParent(mr.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = b.mesh;
                var r = go.AddComponent<MeshRenderer>();
                int n = b.mesh.subMeshCount; var mats = new Material[n];
                for (int i = 0; i < n; i++)
                {
                    if (b.capped && i == n - 1) { mats[i] = CapMat(src.Length > 0 ? src[0] : null); CapFaces++; }
                    else mats[i] = src.Length > 0 ? src[Mathf.Min(i, src.Length - 1)] : null;
                }
                r.sharedMaterials = mats;
                r.shadowCastingMode = ShadowCastingMode.On;
                bool vc = b.mesh.HasVertexAttribute(VertexAttribute.Color);
                for (int i = 0; i < n; i++)
                {
                    if (b.capped && i == n - 1) continue;
                    mr.GetPropertyBlock(blk, Mathf.Min(i, Mathf.Max(0, src.Length - 1)));                       // the unit's own paint (its team colour, burn, accents)
                    if (blk.HasColor(BaseColorId)) { var c = blk.GetColor(BaseColorId); blk.SetColor(BaseColorId, new Color(c.r * f, c.g * f, c.b * f, c.a)); }
                    if (blk.HasColor(TeamColorId)) { var c = blk.GetColor(TeamColorId); blk.SetColor(TeamColorId, new Color(c.r * f, c.g * f, c.b * f, c.a)); }
                    if (blk.HasVector(VividId)) { var q = blk.GetVector(VividId); blk.SetVector(VividId, new Vector4(q.x * f, q.y * f, q.z * f, q.w)); }
                    if (blk.HasVector(GlowId)) { var q = blk.GetVector(GlowId); blk.SetVector(GlowId, new Vector4(q.x * f, q.y * f, q.z * f, q.w)); }
                    if (vc) blk.SetFloat(UseVcId, 1f);                                                        // the soot and the scorch are vertex colours
                    r.SetPropertyBlock(blk, i);
                }
                groups.Add(new List<Transform> { go.transform });
                LastPieces.Add(go.transform); PiecesMade++;
            }
            mr.enabled = false;
            return true;
        }

        /// <summary>WreckPieces.Break, before its budget: when this break made tank pieces, every torn track length joins the hull piece it lies
        /// under (most overlap), the road wheels and lamps join the piece they sit in, and <see cref="LooseParts"/> wheel(s) fly on their own.</summary>
        internal static void Regroup(UnitView view, List<List<Transform>> groups)
        {
            if (view == null || !madeFor.Remove(view)) return;
            var hulls = new List<List<Transform>>(); var belts = new List<List<Transform>>(); var others = new List<List<Transform>>();
            foreach (var g in groups)
            {
                string n = g.Count > 0 && g[0] ? g[0].name : "";
                if (n.StartsWith("OZ_TankPiece_hull")) hulls.Add(g); else if (n.StartsWith("OZ_TankPiece_belt")) belts.Add(g); else others.Add(g);
            }
            if (hulls.Count == 0) return;
            void Join(List<Transform> g)
            {
                var b = BoxOf(g); int best = 0; float bi = -1f, bd = float.MaxValue;
                for (int i = 0; i < hulls.Count; i++)
                {
                    var h = BoxOf(hulls[i]);
                    var lo = Vector3.Max(b.min, h.min); var hi = Vector3.Min(b.max, h.max); var d = hi - lo;
                    float inter = d.x > 0f && d.y > 0f && d.z > 0f ? d.x * d.y * d.z : 0f, dist = (b.center - h.center).sqrMagnitude;
                    if (inter > bi + 1e-6f || (Mathf.Abs(inter - bi) <= 1e-6f && dist < bd)) { bi = inter; bd = dist; best = i; }
                }
                hulls[best].AddRange(g);
            }
            foreach (var g in belts) Join(g);
            others.Sort((a, b) => Rank(b).CompareTo(Rank(a)));                                         // the wheels first: the loose part is a wheel
            int loose = Mathf.Clamp(LooseParts, 0, Mathf.Max(0, WreckPieces.MaxGroups - hulls.Count));
            var keep = new List<List<Transform>>();
            for (int i = 0; i < others.Count; i++) { if (i < loose) keep.Add(others[i]); else Join(others[i]); }
            groups.Clear(); groups.AddRange(hulls); groups.AddRange(keep);
        }

        static int Rank(List<Transform> g)
        {
            int n = 0; foreach (var t in g) if (t && t.name.ToLowerInvariant().Contains("wheel")) n += 100000;
            foreach (var t in g) if (t && t.TryGetComponent<MeshFilter>(out var mf) && mf.sharedMesh) n += mf.sharedMesh.vertexCount;
            return n;
        }

        static Bounds BoxOf(List<Transform> g)
        {
            var b = new Bounds(); bool any = false;
            foreach (var t in g) if (t) foreach (var r in t.GetComponents<Renderer>()) { if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
            return b;
        }

        // ------------------------------------------------------------------ the broken steel
        static Texture2D fAlb, fNrm, fMask; static bool fLoaded, fOk;
        static readonly List<ResourceRequest> fWarm = new List<ResourceRequest>();

        /// <summary>The broken-steel maps start loading in the background at the level's warm-up (or a new model's first death), so the
        /// first break's load finds them in memory instead of reading three 2048 maps from disk on that frame.</summary>
        static void WarmFracture()
        {
            if (fLoaded || fWarm.Count > 0) return;
            foreach (var n in new[] { "FortGun_Fracture_Albedo", "FortGun_Fracture_Normal", "FortGun_Fracture_Mask" })
            {
                try { var r = Resources.LoadAsync<Texture2D>(FractureDir + n); if (r != null) fWarm.Add(r); } catch (System.Exception) { }
            }
        }
        static readonly Dictionary<Shader, Material> capMats = new Dictionary<Shader, Material>();
        static readonly string[] Off = { "_TeamMode", "_Accent", "_PaintFromAlpha", "_OwnerPart", "_Degreen", "_UnitLook", "_VertexColorSRGB", "_LegacyHeavy", "_Triplanar", "_TopBlend", "_Rust", "_SeeThru", "_CreamToTeam" };

        static bool FracLoad()
        {
            if (fLoaded) return fOk;
            fLoaded = true; fWarm.Clear();
            fAlb = Resources.Load<Texture2D>(FractureDir + "FortGun_Fracture_Albedo");
            fNrm = Resources.Load<Texture2D>(FractureDir + "FortGun_Fracture_Normal");
            fMask = Resources.Load<Texture2D>(FractureDir + "FortGun_Fracture_Mask");
            fOk = fAlb && fNrm && fMask;
            MatState = fOk ? "fracture maps loaded" : "fracture maps MISSING under Resources/" + FractureDir + ": the broken faces take the dark core colour";
            if (!fOk) Debug.LogWarning("[OpenZ] tank wreck: " + MatState);
            return fOk;
        }

        /// <summary>The broken faces (one material per shader): the cannon's broken-steel maps, its vertex colours (ridges, pits, rust, soot),
        /// no team paint, our darker steel colour.</summary>
        static Material CapMat(Material like)
        {
            if (!like) return null;
            if (!FracLoad()) { CoreFallbacks++; return WreckPieces.CoreMat(like); }
            if (capMats.TryGetValue(like.shader, out var m) && m) return m;
            m = new Material(like) { name = "OZ_TankWreckFracture" };
            if (m.HasProperty("_BaseMap")) { m.SetTexture("_BaseMap", fAlb); m.SetTextureScale("_BaseMap", Vector2.one); m.SetTextureOffset("_BaseMap", Vector2.zero); }
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", fAlb);
            if (m.HasProperty("_BumpMap")) { m.SetTexture("_BumpMap", fNrm); if (m.HasProperty("_UseNormalMap")) m.SetFloat("_UseNormalMap", 1f); }
            if (m.HasProperty("_MaskMap")) { m.SetTexture("_MaskMap", fMask); if (m.HasProperty("_UseMaskMap")) m.SetFloat("_UseMaskMap", 1f); }
            foreach (var p in Off) if (m.HasProperty(p)) m.SetFloat(p, 0f);
            if (m.HasProperty("_UseVertexColor")) m.SetFloat("_UseVertexColor", 1f);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", FractureMetallic);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", FractureSmoothness);
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", Color.black);
            if (m.HasProperty("_EmissionMapColor")) m.SetColor("_EmissionMapColor", Color.black);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", FractureColor);
            if (m.HasProperty("_Color")) m.SetColor("_Color", FractureColor);
            capMats[like.shader] = m;
            return m;
        }

        /// <summary>Drops the made sets and materials (a Play check that changes the tuning; the next deaths make them again).</summary>
        public static void Reset()
        {
            sets.Clear(); queue.Clear(); unitSet.Clear(); madeFor.Clear(); notReady.Clear(); capMats.Clear(); fLoaded = false; fWarm.Clear();
        }

        /// <summary>Play check: the sets made so far (model, variant, phase, plan).</summary>
        public static string SetsText()
        {
            var sb = new StringBuilder();
            foreach (var kv in sets) sb.Append("      ").Append(kv.Key.Item1 ? kv.Key.Item1.name : "?").Append(" variant ").Append(kv.Key.Item2).Append(": ").Append(kv.Value.phase)
                .Append(kv.Value.phase == Phase.Failed ? " (" + kv.Value.error + ")" : "").Append(", on the background thread ").Append(kv.Value.workerMs.ToString("0")).Append(" ms; ").Append(kv.Value.plan).Append('\n');
            return sb.ToString();
        }

        /// <summary>Play check: true when every set of this unit's model is ready.</summary>
        public static bool ReadyFor(UnitView v) { var s = WantSet(v); return s != null && s.phase == Phase.Ready; }
    }
}
