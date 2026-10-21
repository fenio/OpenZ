using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Version 1.1, item 375 (cost): the fracture look's cuts take 10-20 ms each in the editor, too long for one frame of the cannon's
    /// <see cref="PrepareSteps"/>
    /// flies, and the main thread only does what must be done there, in small steps:
    /// <list type="bullet">
    /// <item>first frame: the numbers the cuts need from the gun's transforms (axes, the bore, the side that lands facing up, the scales,
    /// frame;</item>
    /// <item><see cref="PrepareSteps"/>
    /// numbers in the same order (its own copy of the steps below, <see cref="CutMath"/>), so every piece is the same to the last bit
    /// (the Play check OzK331eCheck compares both ways piece by piece); also each piece's points for its landing (the same list
    /// <see cref="Points"/> makes from its mesh);</item>
    /// <item>then each piece's mesh built on the main thread, as many light steps a frame as fit in <see cref="CutFrameMs"/> (a further
    /// step of the same kind only while the heaviest of its kind so far would still fit); the convex colliders are then cooked on the
    /// <see cref="BakeOnWorker"/></item>
    /// <item>the fracture textures start loading in the background on the first frame, so the landing frame finds them loaded.</item>
    /// </list>
    /// <see cref="CutOnWorker"/>
    /// on the main thread exactly as before.
    /// </summary>
    public partial class OzFortGunBreakup
    {
        public static bool CutOnWorker = true;
        /// <summary>The main thread's share of the cutting in one frame (ms): a further step is started only while the last one would fit.</summary>
        public static float CutFrameMs = 4f;
        /// <summary>The Play check's limit for the heaviest frame of cutting work on the main thread (ms).</summary>
        public static float CutFrameLimitMs = 6f;
        /// <summary>The fracture textures are loaded in the background while the cannon flies.</summary>
        public static bool WarmTextures = true;
        /// <summary></summary>
        public static bool BakeOnWorker = true;

        // ------------------------------------------------------------------ statistics of the latest break (Play check)
        public static bool LastCutOnWorker, LastCutReady, LastCutBakeOnWorker;
        public static float LastCutBakeWorkerMs, LastCutWorkerMs, LastCutFrameMaxMs, LastCutReadMs, LastCutMeshMs, LastCutBakeMs, LastCutWaitMs, LastCutDoneBeforeS, LastCutAirS;
        public static int LastCutFrames, LastCutPolls, WorkerCuts;
        public static string LastCutError = "-";

        enum Phase { Read, Wait, Mesh, Bake }

        /// <summary></summary>
        sealed class Cutter
        {
            public Plan plan; public int seed; public CultureInfo culture;
            public Transform yaw, head, pitch, recoil;
            public Mesh headMesh, pitchMesh, barrelMesh, ringMesh; public string headName, pitchName, barrelName, ringName;
            public bool hasHead, hasPitch, hasBarrel, hasRing, fracture, splitHousing, splitBarrel;
            public OzMeshSlice.Part headPart, pitchPart, barrelPart, ringPart;
            public float ysc, scHead, scPitch, scRecoil, upSign, fwdSign; public int up, fwd, side;
            public Vector3 sideW, pbl, rbl;
            public readonly List<(Transform t, Vector3 lp)> rams = new List<(Transform, Vector3)>();
            public readonly List<string> ramOn = new List<string>();
            public readonly List<(Def d, OzMeshSlice.Part p, string mesh, Vector3[] pts)> cuts = new List<(Def, OzMeshSlice.Part, string, Vector3[])>();
            public readonly List<Def> wholes = new List<Def>();
            public readonly ManualResetEventSlim done = new ManualResetEventSlim(false), baked = new ManualResetEventSlim(false);
            public System.Exception error;
            public bool bakeOnWorker; public float bakeWorkerMs; public long bakedTick;
            public Phase cur = Phase.Read, next = Phase.Read;                                          // the step just run, the step after the yield
            public float workerMs, frameMax, readMax, meshMax, bakeMax, waitMs; public int frames, polls;
            public long startTick, doneTick;
        }

        static readonly Dictionary<Job, Cutter> cutters = new Dictionary<Job, Cutter>();
        static readonly Dictionary<Plan, Cutter> cutOf = new Dictionary<Plan, Cutter>();
        static readonly object CutLock = new object();
        static readonly List<ResourceRequest> warmLoads = new List<ResourceRequest>();
        /// <summary><see cref="Ready"/></summary>
        static bool Landing;

        static float TickMs(long from, long to) => (float)((to - from) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);

        /// <summary>
        /// <see cref="PrepareSteps"/>.</summary>
        static IEnumerator<bool> PrepareOnWorker(Transform yaw, int seed, Quaternion landRot, Job job)
        {
            var plan = job.plan;
            var c = new Cutter { plan = plan, seed = seed, culture = CultureInfo.CurrentCulture, startTick = System.Diagnostics.Stopwatch.GetTimestamp() };
            Prune();
            cutters[job] = c; cutOf[plan] = c;
            c.cur = Phase.Read;
            Gather(c, yaw, landRot);
            if (WarmTextures && c.fracture) WarmFractureTextures();
            // the model's meshes, one per frame
            if (c.hasHead) { c.next = Phase.Read; yield return true; c.cur = Phase.Read; c.headPart = OzMeshSlice.FromMesh(c.headMesh); }
            if (c.hasPitch) { c.next = Phase.Read; yield return true; c.cur = Phase.Read; c.pitchPart = OzMeshSlice.FromMesh(c.pitchMesh); }
            if (c.hasBarrel && c.fracture && c.splitBarrel) { c.next = Phase.Read; yield return true; c.cur = Phase.Read; c.barrelPart = OzMeshSlice.FromMesh(c.barrelMesh); }
            if (c.hasRing) { c.next = Phase.Read; yield return true; c.cur = Phase.Read; c.ringPart = OzMeshSlice.FromMesh(c.ringMesh); }
            c.next = Phase.Wait; yield return true;
            c.cur = Phase.Wait;
            ThreadPool.QueueUserWorkItem(_ => Work(c));
            while (!c.done.IsSet)
            {
                if (Landing) { var w = System.Diagnostics.Stopwatch.StartNew(); c.done.Wait(); c.waitMs += (float)w.Elapsed.TotalMilliseconds; break; }
                c.polls++;
                c.next = Phase.Wait; yield return true;
            }
            if (c.error != null) throw c.error;                                                          // Step, the cannon lands whole
            // the pieces' meshes on the main thread
            for (int i = 0; i < c.cuts.Count; i++)
            {
                c.next = Phase.Mesh; yield return true;
                c.cur = Phase.Mesh;
                var k = c.cuts[i];
                k.d.mesh = OzMeshSlice.ToMesh(k.p, k.mesh);
                if (k.pts != null) pointCache[k.d.mesh] = k.pts;                                      // the same list Points() makes from this mesh
            }
            foreach (var d in c.wholes)
            {   // as Whole(): the source mesh's triangles
                int tris = 0; if (d.mesh && d.mesh.isReadable) for (int s = 0; s < d.mesh.subMeshCount; s++) tris += (int)d.mesh.GetIndexCount(s) / 3;
                d.tris = tris;
            }
            for (int i = 0; i < c.rams.Count && i < c.ramOn.Count; i++) plan.extras[c.rams[i].t] = c.ramOn[i];
            c.headPart = c.pitchPart = c.barrelPart = c.ringPart = null; c.cuts.Clear();                // the cut parts are not needed any more
            // (Physics.BakeMesh may run on any thread; nothing changes these meshes meanwhile), else one per step on the main thread
            if (BakeOnWorker)
            {
                var ids = new List<EntityId>();
                foreach (var d in plan.defs) if (d.mesh && d.mesh.isReadable) ids.Add(d.mesh.GetEntityId());
                if (ids.Count > 0)
                {
                    c.bakeOnWorker = true;
                    c.next = Phase.Wait; yield return true;
                    c.cur = Phase.Wait;
                    ThreadPool.QueueUserWorkItem(_ => BakeWork(c, ids));
                    while (!c.baked.IsSet)
                    {
                        if (Landing) { var w = System.Diagnostics.Stopwatch.StartNew(); c.baked.Wait(); c.waitMs += (float)w.Elapsed.TotalMilliseconds; break; }
                        c.polls++;
                        c.next = Phase.Wait; yield return true;
                    }
                }
                yield break;
            }
            foreach (var d in plan.defs)
            {
                if (!d.mesh || !d.mesh.isReadable) continue;
                c.next = Phase.Bake; yield return true;
                c.cur = Phase.Bake;
                try { Physics.BakeMesh(d.mesh.GetEntityId(), true); Baked++; } catch (System.Exception) { }
            }
        }

        /// <summary></summary>
        static void BakeWork(Cutter c, List<EntityId> ids)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                foreach (var id in ids)
                {
                    try { Physics.BakeMesh(id, true); Interlocked.Increment(ref Baked); } catch (System.Exception) { }
                }
            }
            finally
            {
                c.bakeWorkerMs = (float)sw.Elapsed.TotalMilliseconds;
                c.bakedTick = System.Diagnostics.Stopwatch.GetTimestamp();
                c.baked.Set();
            }
        }

        /// <summary>The numbers of PrepareSteps' first lines and of its later transform reads, read on the main thread now.</summary>
        static void Gather(Cutter c, Transform yaw, Quaternion landRot)
        {
            c.yaw = yaw;
            c.fracture = FractureLook; c.splitHousing = SplitHousing; c.splitBarrel = SplitBarrel;
            c.ysc = MeshScale(yaw);
            Transform head = Find(yaw, "FortGunHead"), pitch = Find(yaw, "FortGunPitch"), recoil = Find(yaw, "FortGunRecoil"), muzzle = Find(yaw, "FortGunMuzzle");
            var ringMf = yaw.GetComponent<MeshFilter>(); var headMf = head ? head.GetComponent<MeshFilter>() : null;
            int up = 1, fwd = 2; float upSign = 1f, fwdSign = 1f;
            if (ringMf && ringMf.sharedMesh)
            {
                var rb = ringMf.sharedMesh.bounds; up = MinAxis(rb.size);
                if (headMf && headMf.sharedMesh) upSign = headMf.sharedMesh.bounds.center[up] >= rb.center[up] ? 1f : -1f;
            }
            Vector3 boreW = recoil && muzzle ? muzzle.position - recoil.position : recoil && pitch ? recoil.position - pitch.position : yaw.forward;
            var bl = yaw.InverseTransformDirection(boreW);
            float bestF = -1f;
            for (int i = 0; i < 3; i++) if (i != up && Mathf.Abs(bl[i]) > bestF) { bestF = Mathf.Abs(bl[i]); fwd = i; }
            fwdSign = bl[fwd] >= 0f ? 1f : -1f;
            int side = 3 - up - fwd;
            c.up = up; c.fwd = fwd; c.side = side; c.upSign = upSign; c.fwdSign = fwdSign;
            c.hasHead = head && headMf && headMf.sharedMesh;
            if (c.hasHead)
            {
                c.head = head; c.headMesh = headMf.sharedMesh; c.headName = c.headMesh.name; c.scHead = MeshScale(head);
                c.sideW = landRot * yaw.InverseTransformDirection(head.TransformDirection(Axis(side, 1f)));
            }
            MeshFilter pmf = null, bmf = null;
            c.hasPitch = pitch && pitch.TryGetComponent(out pmf) && pmf.sharedMesh;
            if (c.hasPitch) { c.pitch = pitch; c.pitchMesh = pmf.sharedMesh; c.pitchName = c.pitchMesh.name; c.pbl = pitch.InverseTransformDirection(boreW); c.scPitch = MeshScale(pitch); }
            c.hasBarrel = recoil && recoil.TryGetComponent(out bmf) && bmf.sharedMesh;
            if (c.hasBarrel) { c.recoil = recoil; c.barrelMesh = bmf.sharedMesh; c.barrelName = c.barrelMesh.name; c.rbl = recoil.InverseTransformDirection(boreW); c.scRecoil = MeshScale(recoil); }
            c.hasRing = ringMf && ringMf.sharedMesh;
            if (c.hasRing)
            {
                c.ringMesh = ringMf.sharedMesh; c.ringName = c.ringMesh.name;
                foreach (Transform ch in yaw) if (ch.name.StartsWith("FortGunRam_")) c.rams.Add((ch, ch.localPosition));
            }
        }

        static void Work(Cutter c)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var th = Thread.CurrentThread; var was = th.CurrentCulture;
            try
            {
                th.CurrentCulture = c.culture;                                                           // the plan's text as the main thread writes numbers
                lock (CutLock) { CutMath(c); WorkerCuts++; }
            }
            catch (System.Exception e) { c.error = e; }
            finally
            {
                try { th.CurrentCulture = was; } catch (System.Exception) { }
                c.workerMs = (float)sw.Elapsed.TotalMilliseconds;
                c.doneTick = System.Diagnostics.Stopwatch.GetTimestamp();
                c.done.Set();
            }
        }

        /// <summary></summary>
        static void CutW(Cutter c, string name, string kind, Transform src, string srcName, OzMeshSlice.Part part, Vector3 outward, bool shard = false)
        {
            if (part == null || part.Tris == 0) return;
            var d = new Def { name = name, kind = kind, src = src, mesh = null, capped = part.CapTris > 0, shard = shard, outward = outward, tris = part.Tris, fracture = FractureLook };
            c.plan.defs.Add(d);
            c.cuts.Add((d, part, srcName + "_" + name, PointsOf(part)));
        }

        /// <summary>A part kept whole (its source mesh); its triangles are counted on the main thread.</summary>
        static void WholeW(Cutter c, string name, string kind, Transform src, Mesh mesh, Vector3 outward)
        {
            var d = new Def { name = name, kind = kind, src = src, mesh = mesh, capped = false, outward = outward, tris = 0, fracture = FractureLook };
            c.plan.defs.Add(d); c.wholes.Add(d);
        }

        /// <summary>The list <see cref="Points"/> makes from the piece's mesh (OzMeshSlice.ToMesh keeps every vertex and index as they are).</summary>
        static Vector3[] PointsOf(OzMeshSlice.Part p)
        {
            int subs = p.CapTris > 0 ? p.sub.Length : p.sub.Length - 1, nv = p.v.Count;
            var seen = new HashSet<Vector3>(); var list = new List<Vector3>();
            for (int s = 0; s < subs; s++) foreach (int i in p.sub[s]) if (i >= 0 && i < nv && seen.Add(p.v[i].p)) list.Add(p.v[i].p);
            return list.ToArray();
        }

        /// <summary>The barrel in two sections, as BarrelHalves (the transform numbers read on the main thread).</summary>
        static bool BarrelHalvesW(Cutter c, System.Random fr)
        {
            var plan = c.plan;
            float at = R3(fr, BarrelAtMin, BarrelAtMax);
            var bp = c.barrelPart; if (bp == null) return false;
            var bl = c.rbl; int ax = 0;
            for (int i = 1; i < 3; i++) if (Mathf.Abs(bl[i]) > Mathf.Abs(bl[ax])) ax = i;
            float s = bl[ax] >= 0f ? 1f : -1f, sc = c.scRecoil;
            var bx = bp.Box(); float lo = s > 0f ? bx.min[ax] : -bx.max[ax], hi = s > 0f ? bx.max[ax] : -bx.min[ax];
            if (!OzFractureCut.Split(bp, Axis(ax, s), lo + at * (hi - lo), sc, fr, true, out var muzzleSide, out var breechSide) || muzzleSide == null || breechSide == null) return false;
            float whole = RuleMass(bx, sc), sum = RuleMass(muzzleSide.Box(), sc) + RuleMass(breechSide.Box(), sc);
            int before = plan.defs.Count;
            CutW(c, "barrel", "barrel", c.recoil, c.barrelName, breechSide, Vector3.zero);
            CutW(c, "barrel_b", "barrel", c.recoil, c.barrelName, muzzleSide, Axis(ax, s));
            if (plan.defs.Count != before + 2) return plan.defs.Count > before;
            HalfMass(plan, sum > 1e-3f ? whole / sum : 1f);
            plan.defs[before].bore = Axis(ax, 1f); plan.defs[before + 1].bore = Axis(ax, 1f);
            return true;
        }

        /// <summary></summary>
        static void CutMath(Cutter c)
        {
            var plan = c.plan; int seed = c.seed;
            var rnd = new System.Random(seed * 7919 + 205);
            float R(float a, float b) => a + (float)rnd.NextDouble() * (b - a);
            var rnd2 = new System.Random(seed * 6271 + 2051);                                             // the small shards: their own numbers, the big cuts keep theirs
            float R2(float a, float b) => a + (float)rnd2.NextDouble() * (b - a);
            var rnd3 = c.fracture ? new System.Random(seed * 4211 + 375) : null;                      // the fracture look's own numbers
            float ysc = c.ysc;
            int up = c.up, fwd = c.fwd, side = c.side; float upSign = c.upSign, fwdSign = c.fwdSign;
            var sb = new StringBuilder();
            var toRing = new List<OzMeshSlice.Part>();                                                   // the head mesh's front lip is a patch of the ring's top
            float pAu = rnd2.Next(2) == 0 ? -1f : 1f, pAa = R2(0.48f, 0.65f), pAb = R2(0.48f, 0.65f);
            float pBu = rnd2.Next(2) == 0 ? -1f : 1f, pBa = R2(0.48f, 0.65f), pBb = R2(0.48f, 0.65f);
            float rAa = R2(0.48f, 0.65f), rAb = R2(0.48f, 0.65f), rBa = R2(0.48f, 0.65f), rBb = R2(0.48f, 0.65f);

            // --- the head: breech block, two side plates, roof plate, the main housing, four shards; its separate lip
            if (c.hasHead)
            {
                var hp = c.headPart;
                if (hp == null) WholeW(c, "head", "head", c.head, c.headMesh, Vector3.zero);
                else
                {
                    var isl = OzMeshSlice.Islands(hp); var main = isl[0];
                    for (int i = 1; i < isl.Count; i++) { if (isl[i].Tris >= 8) toRing.Add(isl[i]); else OzMeshSlice.Merge(main, isl[i]); }
                    var hb = main.Box(); var size = hb.size;
                    float bottom = upSign > 0f ? hb.min[up] : hb.max[up];
                    main = FClose(main, Axis(up, upSign), upSign * (bottom + upSign * 0.03f * size[up]), ysc, rnd3);
                    float rearEnd = fwdSign > 0f ? hb.min[fwd] : hb.max[fwd];
                    float c1 = rearEnd + fwdSign * R(0.18f, 0.22f) * size[fwd];
                    var rest = main; OzMeshSlice.Part rear = null, rearB = null;
                    if (FSplit(main, Axis(fwd, fwdSign), fwdSign * c1, out var f1, out var b1, ysc, rnd3))
                    {
                        rest = f1; rear = b1;
                        var rbx = rear.Box(); float c1b = rbx.min[side] + R(0.35f, 0.65f) * rbx.size[side];
                        if (FSplit(rear, Axis(side, 1f), c1b, out var ra, out var rb2, ysc, rnd3)) { rear = ra; rearB = rb2; }
                    }
                    float sSign = rnd.Next(2) == 0 ? -1f : 1f;
                    var sideW = c.sideW;
                    bool faceUp = Mathf.Abs(sideW.y) > 0.3f;
                    if (faceUp) sSign = sideW.y > 0f ? 1f : -1f;
                    float sideEnd = sSign > 0f ? hb.max[side] : hb.min[side];
                    float c2 = sideEnd - sSign * R(0.08f, 0.095f) * size[side];
                    OzMeshSlice.Part plateA = null, plateB = null;
                    if (FSplit(rest, Axis(side, sSign), sSign * c2, out var slab, out var r2, ysc, rnd3))
                    {
                        rest = r2;
                        var sbx = slab.Box(); float c3 = sbx.min[fwd] + R(0.42f, 0.58f) * sbx.size[fwd];
                        if (FSplit(slab, Axis(fwd, 1f), c3, out var pa, out var pb, ysc, rnd3)) { plateA = pa; plateB = pb; } else plateA = slab;
                    }
                    float top = upSign > 0f ? hb.max[up] : hb.min[up];
                    float c4 = top - upSign * R(0.09f, 0.11f) * size[up];
                    OzMeshSlice.Part roof = null;
                    if (FSplit(rest, Axis(up, upSign), upSign * c4, out var rf, out var r3, ysc, rnd3)) { roof = rf; rest = r3; }
                    float sc = c.scHead; fracRnd = rnd3;
                    OzMeshSlice.Part shPA = null, shPB = null, shRA = null, shRB = null;
                    if (plateA != null && Corner(plateA, up, pAu, fwd, 1f, pAa / sc, pAb / sc, sc, out var s1, out var k1)) { shPA = s1; plateA = k1; }
                    if (rnd3 != null) fracRnd = rnd3;
                    if (plateB != null && Corner(plateB, up, pBu, fwd, -1f, pBa / sc, pBb / sc, sc, out var s2, out var k2)) { shPB = s2; plateB = k2; }
                    fracRnd = rnd3;
                    if (roof != null && Corner(roof, side, -1f, fwd, fwdSign, rAa / sc, rAb / sc, sc, out var s3, out var k3)) { shRA = s3; roof = k3; }
                    if (rnd3 != null) fracRnd = rnd3;
                    if (roof != null && Corner(roof, side, 1f, fwd, fwdSign, rBa / sc, rBb / sc, sc, out var s4, out var k4)) { shRB = s4; roof = k4; }
                    float hk = 1f; OzMeshSlice.Part restB = null;
                    if (rnd3 != null && c.splitHousing) restB = HousingHalf(ref rest, up, side, ysc, rnd3, out hk);   // the turret shell in two halves
                    CutW(c, "head", "head", c.head, c.headName, rest, Vector3.zero);
                    if (restB != null) { CutW(c, "head_b", "head", c.head, c.headName, restB, Vector3.zero); HalfMass(plan, hk); }
                    if (rear != null) CutW(c, rearB != null ? "breech_a" : "breech", "part", c.head, c.headName, rear, Axis(fwd, -fwdSign));
                    if (rearB != null) CutW(c, "breech_b", "part", c.head, c.headName, rearB, Axis(fwd, -fwdSign));
                    if (plateA != null) CutW(c, "plate_a", "plate", c.head, c.headName, plateA, Axis(side, sSign));
                    if (plateB != null) CutW(c, "plate_b", "plate", c.head, c.headName, plateB, Axis(side, sSign));
                    if (roof != null) CutW(c, "roof", "plate", c.head, c.headName, roof, Axis(up, upSign));
                    int nShards = 0;
                    if (shPA != null) { CutW(c, "shard_plate_a", "chunk", c.head, c.headName, shPA, (Axis(up, pAu) + Axis(fwd, 1f) + Axis(side, sSign)).normalized, true); nShards++; }
                    if (shPB != null) { CutW(c, "shard_plate_b", "chunk", c.head, c.headName, shPB, (Axis(up, pBu) + Axis(fwd, -1f) + Axis(side, sSign)).normalized, true); nShards++; }
                    if (shRA != null) { CutW(c, "shard_roof_a", "chunk", c.head, c.headName, shRA, (Axis(side, -1f) + Axis(fwd, fwdSign) + Axis(up, upSign)).normalized, true); nShards++; }
                    if (shRB != null) { CutW(c, "shard_roof_b", "chunk", c.head, c.headName, shRB, (Axis(side, 1f) + Axis(fwd, fwdSign) + Axis(up, upSign)).normalized, true); nShards++; }
                    sb.Append($"head: {isl.Count} part(s) in its mesh, underside closed, breech {(rearB != null ? 2 : rear != null ? 1 : 0)}, side plates {(plateB != null ? 2 : plateA != null ? 1 : 0)} ({(sSign > 0 ? "+" : "-")}{"xyz"[side]}, {(faceUp ? "the side landing up" : "by the fort")}), roof {(roof != null ? "yes" : "no")}, shards {nShards}; ");
                }
            }
            // --- the mantlet with its sleeve (the pitch mesh's two solid parts, kept together)
            if (c.hasPitch)
            {
                var pp = c.pitchPart;
                if (pp == null) WholeW(c, "mantlet", "part", c.pitch, c.pitchMesh, Vector3.zero);
                else
                {
                    var isl = OzMeshSlice.Islands(pp); var mant = isl[0];
                    for (int i = 1; i < isl.Count; i++) OzMeshSlice.Merge(mant, isl[i]);                       // its sleeve (collar) stays on it
                    var pbl = c.pbl; int pf = 0;
                    for (int i = 1; i < 3; i++) if (Mathf.Abs(pbl[i]) > Mathf.Abs(pbl[pf])) pf = i;
                    float ps = pbl[pf] >= 0f ? 1f : -1f; var mb = mant.Box();
                    float back = ps > 0f ? mb.min[pf] : mb.max[pf];
                    mant = FClose(mant, Axis(pf, ps), ps * (back + ps * 0.02f * mb.size[pf]), c.scPitch, rnd3);
                    CutW(c, "mantlet", "part", c.pitch, c.pitchName, mant, Vector3.zero);
                    sb.Append($"mantlet: {isl.Count} part(s); ");
                }
            }
            // --- the barrel: two sections at a fractured ring break, else whole
            if (c.hasBarrel)
            {
                if (!(rnd3 != null && c.splitBarrel && BarrelHalvesW(c, rnd3))) WholeW(c, "barrel", "barrel", c.recoil, c.barrelMesh, Vector3.zero);
            }
            // --- the ring: its main piece and a broken edge in two chunks; the rams stay on the piece they stand on
            if (c.hasRing)
            {
                var rp = c.ringPart;
                if (rp == null) WholeW(c, "ring", "ring", c.yaw, c.ringMesh, Vector3.zero);
                else
                {
                    var isl = OzMeshSlice.Islands(rp); var ring = isl[0];
                    for (int i = 1; i < isl.Count; i++) OzMeshSlice.Merge(ring, isl[i]);
                    var rbx = ring.Box(); float th = R(0f, Mathf.PI * 2f);
                    float rtop = upSign > 0f ? rbx.max[up] : rbx.min[up];
                    ring = FClose(ring, Axis(up, -upSign), -upSign * (rtop - upSign * 0.15f * rbx.size[up]), ysc, rnd3);
                    foreach (var lip in toRing) OzMeshSlice.Merge(ring, lip);
                    var n = (Mathf.Cos(th) * Axis(side, 1f) + Mathf.Sin(th) * Axis(fwd, 1f)).normalized;
                    float rad = 0.5f * Mathf.Max(rbx.size[side], rbx.size[fwd]);
                    float w = Vector3.Dot(n, rbx.center) + R(0.38f, 0.48f) * rad;
                    OzMeshSlice.Part ca = null, cb = null; Vector3 n2 = Vector3.zero; float w2 = 0f;
                    if (FSplit(ring, n, w, out var seg, out var ringMain, ysc, rnd3, false))
                    {
                        ring = ringMain;
                        n2 = Vector3.Cross(n, Axis(up, 1f)).normalized; var sbx = seg.Box();
                        w2 = Vector3.Dot(n2, sbx.center) + R(-0.12f, 0.12f) * Mathf.Max(sbx.size[side], sbx.size[fwd]);
                        if (FSplit(seg, n2, w2, out var s1, out var s2, ysc, rnd3, false)) { ca = s1; cb = s2; } else ca = seg;
                    }
                    CutW(c, "ring", "ring", c.yaw, c.ringName, ring, Vector3.zero);
                    if (ca != null) CutW(c, "ring_chunk_a", "chunk", c.yaw, c.ringName, ca, n);
                    if (cb != null) CutW(c, "ring_chunk_b", "chunk", c.yaw, c.ringName, cb, n);
                    foreach (var (_, lp) in c.rams)
                    {
                        string on = "ring";
                        if (ca != null && Vector3.Dot(n, lp) - w >= 0f) on = cb == null || Vector3.Dot(n2, lp) - w2 >= 0f ? "ring_chunk_a" : "ring_chunk_b";
                        c.ramOn.Add(on);
                    }
                    sb.Append($"ring: broken edge {(cb != null ? "2 chunks" : ca != null ? "1 chunk" : "none")}; ");
                }
            }
            plan.text = sb.ToString() + (rnd3 != null ? "fracture look: " + OzFractureCut.State + "; " : "");
        }

        // ------------------------------------------------------------------ the frame's share (OzFortGunBreakup.WatchUpdate)
        /// <summary>
        /// <see cref="CutFrameMs"/></summary>
        static void StepFrame(Transform y, Job job)
        {
            if (!cutters.TryGetValue(job, out var c)) { Step(y, job); return; }
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (true)
            {
                double t0 = sw.Elapsed.TotalMilliseconds;
                bool done = Step(y, job);
                float ms = (float)(sw.Elapsed.TotalMilliseconds - t0);
                switch (c.cur)
                {
                    case Phase.Read: if (ms > c.readMax) c.readMax = ms; break;
                    case Phase.Mesh: if (ms > c.meshMax) c.meshMax = ms; break;
                    case Phase.Bake: if (ms > c.bakeMax) c.bakeMax = ms; break;
                }
                if (done) { cutters.Remove(job); break; }
                if (c.cur == Phase.Wait || c.next != c.cur) break;
                float est = Mathf.Max(ms, c.next == Phase.Read ? c.readMax : c.next == Phase.Mesh ? c.meshMax : c.bakeMax);   // the heaviest of its kind so far
                if (sw.Elapsed.TotalMilliseconds + est > CutFrameMs) break;
            }
            float f = (float)sw.Elapsed.TotalMilliseconds; c.frames++; if (f > c.frameMax) c.frameMax = f;
        }

        static void Prune()
        {
            if (cutters.Count > 0)
            {
                var dead = new List<Job>(); foreach (var kv in cutters) if (!jobs.ContainsValue(kv.Key)) dead.Add(kv.Key);
                foreach (var j in dead) cutters.Remove(j);
            }
            if (cutOf.Count > 8)
            {
                var dead = new List<Plan>(); foreach (var kv in cutOf) if (!plans.ContainsValue(kv.Key) && !cutters.ContainsValue(kv.Value)) dead.Add(kv.Key);
                foreach (var p in dead) cutOf.Remove(p);
            }
        }

        /// <summary>The fracture textures start loading in the background (the landing frame's load then finds them in memory).</summary>
        static void WarmFractureTextures()
        {
            if (fLoaded || warmLoads.Count > 0) return;
            foreach (var n in new[] { "FortGun_Fracture_Albedo", "FortGun_Fracture_Normal", "FortGun_Fracture_Mask", "FortV2_Gun_Albedo_Pieces_Wreck", "FortV2_Gun_Albedo_B_Pieces_Wreck" })
            {
                try { var r = Resources.LoadAsync<Texture2D>(FractureDir + n); if (r != null) warmLoads.Add(r); } catch (System.Exception) { }
            }
        }

        /// <summary>Break: the latest plan's cutting numbers for the Play check.</summary>
        static void NoteCut(Plan plan)
        {
            LastCutOnWorker = cutOf.TryGetValue(plan, out var c);
            if (!LastCutOnWorker) { LastCutReady = plan.landingMs <= 1f; return; }
            cutOf.Remove(plan);
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            LastCutWorkerMs = c.workerMs; LastCutFrameMaxMs = c.frameMax; LastCutReadMs = c.readMax; LastCutMeshMs = c.meshMax; LastCutBakeMs = c.bakeMax;
            LastCutWaitMs = c.waitMs; LastCutFrames = c.frames; LastCutPolls = c.polls;
            LastCutAirS = TickMs(c.startTick, now) / 1000f;
            LastCutBakeOnWorker = c.bakeOnWorker; LastCutBakeWorkerMs = c.bakeWorkerMs;
            long last = c.bakeOnWorker ? System.Math.Max(c.doneTick, c.bakedTick) : c.doneTick;
            LastCutDoneBeforeS = last > 0 ? TickMs(last, now) / 1000f : -1f;
            LastCutReady = plan.landingMs <= 1f && c.waitMs <= 0f;
            LastCutError = c.error != null ? c.error.Message : "-";
        }

        /// <summary>Play check: the latest break's cutting passes (the heaviest frame of cutting work within the limit, nothing left for the landing frame).</summary>
        public static bool LastCutOk => LastCutOnWorker ? LastCutFrameMaxMs <= CutFrameLimitMs && LastCutReady : LastPrepareMaxMs <= 10f && LastLandingPrepareMs <= 1f;

        /// <summary>Play check: the latest break's cutting in one line.</summary>
        public static string LastCutText => LastCutOnWorker
            ? $"cutting on a background thread: the cuts took {LastCutWorkerMs:0} ms there, all done {LastCutDoneBeforeS:0.00} s before the landing ({LastCutAirS:0.00} s after the toss); " +
              $"main thread: {LastCutFrames} frames with cutting work, the heaviest {LastCutFrameMaxMs:0.0} ms (wanted <= {CutFrameLimitMs:0}; one step at most: reading a mesh {LastCutReadMs:0.0} ms, " +
              $"building a piece's mesh {LastCutMeshMs:0.0} ms, " + (LastCutBakeOnWorker ? $"the colliders cooked on the background thread ({LastCutBakeWorkerMs:0} ms there)" : $"cooking a collider {LastCutBakeMs:0.0} ms") + $"; {LastCutPolls} frames waited for the background thread at no cost), " +
              $"left for the landing frame {LastLandingPrepareMs:0.00} ms (wanted <= 1; waited for the background thread there {LastCutWaitMs:0.0} ms), the landing frame {LastBreakMs:0.0} ms (info)"
            : $"cutting on the main thread, one step per frame (CutOnWorker off): {LastPrepareSteps} steps, the heaviest {LastPrepareMaxMs:0.0} ms, left for the landing frame {LastLandingPrepareMs:0.00} ms";

        // ------------------------------------------------------------------ Play check: both ways give the same pieces
        static readonly System.Text.RegularExpressions.Regex slowest = new System.Text.RegularExpressions.Regex(@", slowest cut [^;]*? ms");

        /// <summary>Play check (menu OpenZ/V1.1/K331e ...): cuts the cannon under `yaw` both ways, one step per frame on the main thread
        /// <see cref="PrepareSteps"/>
        /// every piece: name, kind, outward, mass scale, bore, triangles and its mesh (every vertex, normal, uv, colour, tangent and index,
        /// hashed). Nothing stays: the meshes made are destroyed, the counters put back. True = all the same.</summary>
        public static bool SamePieces(Transform yaw, int[] seeds, Quaternion[] lands, List<string> lines, out int compared, out int pieces)
        {
            compared = 0; pieces = 0;
            if (!yaw) { lines.Add("no cannon"); return false; }
            bool keepFrac = FractureLook, all = true;
            var keep = Counters();
            var srcMeshes = new HashSet<Mesh>();
            foreach (var mf in yaw.GetComponentsInChildren<MeshFilter>(true)) if (mf.sharedMesh) srcMeshes.Add(mf.sharedMesh);
            try
            {
                foreach (bool frac in new[] { true, false })
                {
                    FractureLook = frac;
                    foreach (int seed in seeds)
                        foreach (var land in lands)
                        {
                            PutBack(keep); var pa = new Plan(); var ia = PrepareSteps(yaw, seed, land, pa);
                            while (ia.MoveNext()) { }
                            string ta = slowest.Replace(pa.text, "");
                            PutBack(keep); var pb = new Plan(); var jb = new Job { plan = pb }; var ib = PrepareOnWorker(yaw, seed, land, jb);
                            Landing = true;
                            try { while (ib.MoveNext()) { } } finally { Landing = false; cutters.Remove(jb); cutOf.Remove(pb); }
                            string tb = slowest.Replace(pb.text, "");
                            var why = new List<string>();
                            if (ta != tb) why.Add("plan text differs");
                            if (pa.defs.Count != pb.defs.Count) why.Add($"pieces {pa.defs.Count} vs {pb.defs.Count}");
                            for (int i = 0; i < pa.defs.Count && i < pb.defs.Count; i++)
                            {
                                var a = pa.defs[i]; var b = pb.defs[i];
                                if (a.name != b.name || a.kind != b.kind || a.src != b.src || a.capped != b.capped || a.shard != b.shard || a.outward != b.outward || a.tris != b.tris
                                    || a.fracture != b.fracture || a.massScale != b.massScale || a.bore != b.bore) why.Add(a.name + ": its numbers differ");
                                bool wa = srcMeshes.Contains(a.mesh), wb = srcMeshes.Contains(b.mesh);
                                if (wa || wb) { if (a.mesh != b.mesh) why.Add(a.name + ": the whole part differs"); }
                                else if (Hash(a.mesh) != Hash(b.mesh)) why.Add(a.name + ": its mesh differs");
                            }
                            if (pa.extras.Count != pb.extras.Count) why.Add("rams differ");
                            else foreach (var kv in pa.extras) if (!pb.extras.TryGetValue(kv.Key, out var on) || on != kv.Value) why.Add("ram " + kv.Key.name + " differs");
                            compared++; pieces += pa.defs.Count;
                            ulong all64 = 14695981039346656037UL; foreach (var d in pb.defs) all64 = (all64 ^ (srcMeshes.Contains(d.mesh) ? 1UL : Hash(d.mesh))) * 1099511628211UL;
                            lines.Add($"{(why.Count == 0 ? "same" : "DIFFERENT")}  fracture look {(frac ? "on " : "off")}, seed {seed,6}, landing turn {land.eulerAngles.z,5:0}: {pb.defs.Count} pieces, hash {all64:x16}{(why.Count > 0 ? " - " + string.Join("; ", why) : "")}");
                            if (why.Count > 0) all = false;
                            foreach (var d in pa.defs) if (d.mesh && !srcMeshes.Contains(d.mesh)) Object.Destroy(d.mesh);
                            foreach (var d in pb.defs) if (d.mesh && !srcMeshes.Contains(d.mesh)) Object.Destroy(d.mesh);
                        }
                }
            }
            finally { FractureLook = keepFrac; PutBack(keep); }
            return all;
        }

        static ulong Hash(Mesh m)
        {
            if (!m) return 0UL;
            ulong h = 14695981039346656037UL;
            void I(int x) { unchecked { for (int k = 0; k < 4; k++) { h ^= (byte)(x >> (8 * k)); h *= 1099511628211UL; } } }
            void F(float x) => I(System.BitConverter.SingleToInt32Bits(x));
            foreach (var v in m.vertices) { F(v.x); F(v.y); F(v.z); }
            foreach (var v in m.normals) { F(v.x); F(v.y); F(v.z); }
            foreach (var v in m.uv) { F(v.x); F(v.y); }
            foreach (var v in m.uv2) { F(v.x); F(v.y); }
            foreach (var v in m.colors) { F(v.r); F(v.g); F(v.b); F(v.a); }
            foreach (var v in m.tangents) { F(v.x); F(v.y); F(v.z); F(v.w); }
            I(m.subMeshCount);
            for (int s = 0; s < m.subMeshCount; s++) { var t = m.GetIndices(s); I(t.Length); foreach (int i in t) I(i); }
            foreach (char ch in m.name) I(ch);
            return h;
        }

        sealed class Saved { public int cuts, fallbacks, caps, capTris, closures, baked, prepared; public float maxMs, lastMs, deepest, refine, clip, lip, cap; public string error; }
        static Saved Counters() => new Saved
        {
            cuts = OzFractureCut.Cuts, fallbacks = OzFractureCut.Fallbacks, caps = OzFractureCut.Caps, capTris = OzFractureCut.CapTrisMade, closures = OzFractureCut.Closures,
            maxMs = OzFractureCut.MaxMs, lastMs = OzFractureCut.LastMs, deepest = OzFractureCut.Deepest, refine = OzFractureCut.MsRefine, clip = OzFractureCut.MsClip,
            lip = OzFractureCut.MsLip, cap = OzFractureCut.MsCap, error = OzFractureCut.LastError, baked = Baked, prepared = Prepared
        };
        static void PutBack(Saved s)
        {
            OzFractureCut.Cuts = s.cuts; OzFractureCut.Fallbacks = s.fallbacks; OzFractureCut.Caps = s.caps; OzFractureCut.CapTrisMade = s.capTris; OzFractureCut.Closures = s.closures;
            OzFractureCut.MaxMs = s.maxMs; OzFractureCut.LastMs = s.lastMs; OzFractureCut.Deepest = s.deepest; OzFractureCut.MsRefine = s.refine; OzFractureCut.MsClip = s.clip;
            OzFractureCut.MsLip = s.lip; OzFractureCut.MsCap = s.cap; OzFractureCut.LastError = s.error; Baked = s.baked; Prepared = s.prepared;
        }
    }
}
