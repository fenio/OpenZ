using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenZ
{
    /// <summary>
    /// Version 1.1, item 205: the fort's big cannon stayed whole and too perfect when it fell on the ground; now it falls apart into its
    /// parts at that moment, naturally. On the cannon's landing frame, right after the heavy landing (FortGunView.UpdateToss ->
    /// OzHeavyLanding.FortGun: the dust ring, puff, stone chips, concrete thud and camera kick, all kept), the gun breaks apart:
    /// <list type="bullet">
    /// <item>its REAL parts: the barrel (FortGunRecoil; it comes off the mantlet and rolls a little), the mantlet with its sleeve
    /// (FortGunPitch), the turret head (FortGunHead) and the turret ring (the yaw mesh) with its two hydraulic rams; the sides that were
    /// hidden inside the gun and open (the head's underside, the ring's top under it, the sleeve's back end) are closed by a burnt plate
    /// just inside them, so no piece shows a hollow inside;</item>
    /// <item>cut out of the model's own triangles like the vehicles' wreck fragments (OzMeshSlice, every vertex attribute kept; each cut
    /// closed by a burnt cap in the wrecks' dark core material, WreckPieces.CoreMat): the head's breech block cracks off the rear in two,
    /// two side armour plates (off the side that lands facing up, FortGunView.TossEndRot) and a roof plate come away, the ring breaks
    /// into its main piece and two chunks, and four small shards (0.3-0.8 m) break off the corners of the two plates and the roof's
    /// front;</item>
    /// <item>each piece keeps the cannon's material and paint exactly as drawn (team colour, war paint, charred), a convex collider of
    /// its own mesh, mass by size, heavy metal (little bounce, high friction); they all tumble on the impact: the small ones scatter
    /// a few metres, the big ones slump, the plates flip off the top, the barrel rolls;</item>
    /// <item>the gun landed up to 0.2 m into the ground: all pieces are lifted together until the lowest is 2 cm clear (cut faces stay
    /// flush); pieces that start deep inside each other (deeper than <see cref="HoldDepth"/>: the barrel in the sleeve, the sleeve in the
    /// head, the rams in the head) ignore each other until they are apart (checked every 0.2 s); a pair still inside each other when one
    /// of them comes to rest collides again and is pushed apart by the physics, and at <see cref="PushAt"/> s it is moved out, so no
    /// two pieces rest inside each other;</item>
    /// <item>on the frame: a metal break (tear + clang) and the debris scatter on top of the crash and the thud, metal sparks, a scorch
    /// where it hit; each piece crashes / clanks when it hits the ground (a clank the sound table drops is tried once more 0.07 s
    /// later), a rolling or sliding piece knocks; the three heaviest burn and smoke for a while; the counters count only sounds that
    /// really started;</item>
    /// <item>when a piece rests (asleep, or after 7 s) it stops and lies ON the ground by its REAL lowest vertex (3 cm in, the exact
    /// ground probe); a piece lying on another piece or on the ruin stays where it came to rest;</item>
    /// <item>after the cannon's own wreck time (FortGunView.wreckSeconds) they sink away together, as the whole cannon did.</item>
    /// </list>
    /// The pieces are cut while the cannon is still in the air, one step per frame (a watcher sees FortGunView.Tosses; each step is one
    /// cut, closing, mesh or cooked collider, timed), so the landing frame only builds the bodies. Over open water the cannon lands whole
    /// as before. View only. Switch: <see cref="Enabled"/> (off = KC5's whole landing).
    /// </summary>
    public partial class OzFortGunBreakup : MonoBehaviour                                    // item 331: + OzFortGunBreakup.Rocks.cs (the rocks)
    {
        public static bool Enabled = true;
        public const string SoundBreak = "dst_fortgun_break_metal", SoundScatter = "exp_debris_rain", SoundClank = "dst_debris_metal";
        /// <summary>How deep a resting piece's real lowest vertex lies in the ground (m).</summary>
        public const float Sink = 0.03f;
        /// <summary>Two pieces whose colliders start more than this inside each other (m) ignore each other until they are apart;
        /// shallower contacts (the shared cut faces) collide from the first step.</summary>
        public const float HoldDepth = 0.03f;
        /// <summary>A piece whose held pair is still inside each other at this age (s) is moved out of it, then it rests.</summary>
        public const float PushAt = 9f;
        public const int HeavyLayerFallback = 28;                                                          // TagManager "DebrisHeavy": the pieces hit each other and the world

        // ------------------------------------------------------------------ statistics (Play check; sound counters count started sounds only)
        public static int Breaks, Pieces, BreakSounds, ScatterSounds, Clanks, Crashes, Knocks, RetriedClanks, Prepared, Baked, RestedCount, OnGround, OnOther, InWaterCount, Floating, Sunk, Faded;
        public static int HeldPairs, ReleasedPairs, ForcedPairs, PushedOut;
        public static int LastBreakFrame = -1, LastLandingsAtBreak = -1, LastPrepareSteps;
        public static float LastBreakAt = -1f, LastTouchSpeed, LastPrepareMaxMs, LastPrepareTotalMs, LastLandingPrepareMs, LastBreakMs;
        public static Vector3 LastImpact;
        public static string LastPlan = "-", LastHeldText = "-";
        /// <summary>The yaw (the drawn gun) the latest break came from.</summary>
        public static Transform LastSource;
        /// <summary>Raised on the break frame (impact point on the ground).</summary>
        public static event System.Action<Vector3> Broke;
        static readonly List<OzFortGunBreakup> live = new List<OzFortGunBreakup>();
        /// <summary>The pieces of the latest break (Play check).</summary>
        public static readonly List<OzFortGunBreakup> LastPieces = new List<OzFortGunBreakup>();

        // ------------------------------------------------------------------ the plan: pieces cut while the cannon flies
        sealed class Def { public string name, kind; public Transform src; public Mesh mesh; public bool capped, shard; public Vector3 outward; public int tris; public bool fracture; public float massScale = 1f; public Vector3 bore; }   // item 375: + fracture, massScale, bore
        sealed class Plan
        {
            public readonly List<Def> defs = new List<Def>(); public readonly Dictionary<Transform, string> extras = new Dictionary<Transform, string>(); public string text = "";
            public float maxMs, totalMs, landingMs; public int steps;
        }
        sealed class Job { public IEnumerator<bool> it; public Plan plan; }
        static readonly Dictionary<Transform, Plan> plans = new Dictionary<Transform, Plan>();
        static readonly Dictionary<Transform, Job> jobs = new Dictionary<Transform, Job>();
        static readonly List<Transform> stepKeys = new List<Transform>();

        static Transform Find(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t) { var r = Find(c, name); if (r) return r; }
            return null;
        }

        static Vector3 Axis(int i, float sign) { var v = Vector3.zero; v[i] = sign; return v; }
        static int MinAxis(Vector3 s) => s.x <= s.y && s.x <= s.z ? 0 : s.y <= s.z ? 1 : 2;

        /// <summary>Cuts the cannon under `yaw` into its pieces (mesh space; seeded by the fort, the same every time), one step per
        /// MoveNext: a mesh read, a cut, a closing, a mesh built or a collider cooked. `landRot` = the yaw's rotation when it lands.</summary>
        static IEnumerator<bool> PrepareSteps(Transform yaw, int seed, Quaternion landRot, Plan plan)
        {
            var rnd = new System.Random(seed * 7919 + 205);
            float R(float a, float b) => a + (float)rnd.NextDouble() * (b - a);
            var rnd2 = new System.Random(seed * 6271 + 2051);                                             // the small shards: their own numbers, the big cuts keep theirs
            float R2(float a, float b) => a + (float)rnd2.NextDouble() * (b - a);
            var rnd3 = FractureLook ? new System.Random(seed * 4211 + 375) : null;                    // item 375: the fracture look's own numbers (OzFortGunBreakup.Fracture.cs)
            float ysc = MeshScale(yaw);
            Transform head = Find(yaw, "FortGunHead"), pitch = Find(yaw, "FortGunPitch"), recoil = Find(yaw, "FortGunRecoil"), muzzle = Find(yaw, "FortGunMuzzle");
            var ringMf = yaw.GetComponent<MeshFilter>(); var headMf = head ? head.GetComponent<MeshFilter>() : null;
            // the turret's mesh space (ring + head share it): up = the ring's thin axis toward the head, fwd = along the barrel, side = the third
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
            var sb = new StringBuilder();
            var toRing = new List<OzMeshSlice.Part>();                                                   // the head mesh's front lip is a patch of the ring's top
            // the shards' numbers, drawn up front (always the same order): plate a, plate b (up / down corner, two legs), the roof's two front corners
            float pAu = rnd2.Next(2) == 0 ? -1f : 1f, pAa = R2(0.48f, 0.65f), pAb = R2(0.48f, 0.65f);
            float pBu = rnd2.Next(2) == 0 ? -1f : 1f, pBa = R2(0.48f, 0.65f), pBb = R2(0.48f, 0.65f);
            float rAa = R2(0.48f, 0.65f), rAb = R2(0.48f, 0.65f), rBa = R2(0.48f, 0.65f), rBb = R2(0.48f, 0.65f);

            // --- the head: breech block, two side plates, roof plate, the main housing, four shards; its separate lip
            if (head && headMf && headMf.sharedMesh)
            {
                var hp = OzMeshSlice.FromMesh(headMf.sharedMesh);
                yield return true;
                if (hp == null) Whole(plan, "head", "head", head, headMf.sharedMesh, Vector3.zero);
                else
                {
                    var isl = OzMeshSlice.Islands(hp); var main = isl[0];
                    for (int i = 1; i < isl.Count; i++) { if (isl[i].Tris >= 8) toRing.Add(isl[i]); else OzMeshSlice.Merge(main, isl[i]); }   // the front lip lies on the ring: it goes with the ring
                    var hb = main.Box(); var size = hb.size;
                    yield return true;
                    // 0. the head's underside was open (it sat on the ring): closed by a burnt plate just inside it
                    float bottom = upSign > 0f ? hb.min[up] : hb.max[up];
                    main = FClose(main, Axis(up, upSign), upSign * (bottom + upSign * 0.03f * size[up]), ysc, rnd3);   // item 375: dressed as broken steel
                    yield return true;
                    // 1. the breech block cracks off the rear, in two
                    float rearEnd = fwdSign > 0f ? hb.min[fwd] : hb.max[fwd];
                    float c1 = rearEnd + fwdSign * R(0.18f, 0.22f) * size[fwd];
                    var rest = main; OzMeshSlice.Part rear = null, rearB = null;
                    if (FSplit(main, Axis(fwd, fwdSign), fwdSign * c1, out var f1, out var b1, ysc, rnd3))   // item 375: a fractured cut
                    {
                        rest = f1; rear = b1;
                        if (rnd3 != null) yield return true;                                                // item 375: one fractured cut per frame
                        var rbx = rear.Box(); float c1b = rbx.min[side] + R(0.35f, 0.65f) * rbx.size[side];
                        if (FSplit(rear, Axis(side, 1f), c1b, out var ra, out var rb2, ysc, rnd3)) { rear = ra; rearB = rb2; }
                    }
                    yield return true;
                    // 2. a side armour slab, torn in two plates: off the side that lands facing up (the cannon lands rolled onto a side), else
                    //    the side the fort's numbers pick
                    float sSign = rnd.Next(2) == 0 ? -1f : 1f;
                    var sideW = landRot * yaw.InverseTransformDirection(head.TransformDirection(Axis(side, 1f)));
                    bool faceUp = Mathf.Abs(sideW.y) > 0.3f;
                    if (faceUp) sSign = sideW.y > 0f ? 1f : -1f;
                    float sideEnd = sSign > 0f ? hb.max[side] : hb.min[side];
                    float c2 = sideEnd - sSign * R(0.08f, 0.095f) * size[side];
                    OzMeshSlice.Part plateA = null, plateB = null;
                    if (FSplit(rest, Axis(side, sSign), sSign * c2, out var slab, out var r2, ysc, rnd3))
                    {
                        rest = r2;
                        if (rnd3 != null) yield return true;                                                // item 375: one fractured cut per frame
                        var sbx = slab.Box(); float c3 = sbx.min[fwd] + R(0.42f, 0.58f) * sbx.size[fwd];
                        if (FSplit(slab, Axis(fwd, 1f), c3, out var pa, out var pb, ysc, rnd3)) { plateA = pa; plateB = pb; } else plateA = slab;
                    }
                    yield return true;
                    // 3. the roof plate
                    float top = upSign > 0f ? hb.max[up] : hb.min[up];
                    float c4 = top - upSign * R(0.09f, 0.11f) * size[up];
                    OzMeshSlice.Part roof = null;
                    if (FSplit(rest, Axis(up, upSign), upSign * c4, out var rf, out var r3, ysc, rnd3)) { roof = rf; rest = r3; }
                    yield return true;
                    // 4. the small shards: a corner off each plate (its outer end along the barrel, top or bottom) and the roof's two front corners
                    float sc = MeshScale(head); fracRnd = rnd3;                                               // item 375: the corners' fractured cuts
                    OzMeshSlice.Part shPA = null, shPB = null, shRA = null, shRB = null;
                    if (plateA != null && Corner(plateA, up, pAu, fwd, 1f, pAa / sc, pAb / sc, sc, out var s1, out var k1)) { shPA = s1; plateA = k1; }
                    if (rnd3 != null) { yield return true; fracRnd = rnd3; }                                  // item 375: one corner per frame
                    if (plateB != null && Corner(plateB, up, pBu, fwd, -1f, pBa / sc, pBb / sc, sc, out var s2, out var k2)) { shPB = s2; plateB = k2; }
                    yield return true;
                    fracRnd = rnd3;                                                                          // item 375
                    if (roof != null && Corner(roof, side, -1f, fwd, fwdSign, rAa / sc, rAb / sc, sc, out var s3, out var k3)) { shRA = s3; roof = k3; }
                    if (rnd3 != null) { yield return true; fracRnd = rnd3; }                                  // item 375: one corner per frame
                    if (roof != null && Corner(roof, side, 1f, fwd, fwdSign, rBa / sc, rBb / sc, sc, out var s4, out var k4)) { shRB = s4; roof = k4; }
                    yield return true;
                    float hk = 1f; OzMeshSlice.Part restB = null;
                    if (rnd3 != null && SplitHousing) { restB = HousingHalf(ref rest, up, side, ysc, rnd3, out hk); yield return true; }   // item 375: the turret shell in two halves
                    Cut(plan, "head", "head", head, headMf.sharedMesh, rest, Vector3.zero);
                    if (restB != null) { Cut(plan, "head_b", "head", head, headMf.sharedMesh, restB, Vector3.zero); HalfMass(plan, hk); }
                    yield return true;
                    if (rear != null) Cut(plan, rearB != null ? "breech_a" : "breech", "part", head, headMf.sharedMesh, rear, Axis(fwd, -fwdSign));
                    if (rearB != null) Cut(plan, "breech_b", "part", head, headMf.sharedMesh, rearB, Axis(fwd, -fwdSign));
                    yield return true;
                    if (plateA != null) Cut(plan, "plate_a", "plate", head, headMf.sharedMesh, plateA, Axis(side, sSign));
                    if (plateB != null) Cut(plan, "plate_b", "plate", head, headMf.sharedMesh, plateB, Axis(side, sSign));
                    if (roof != null) Cut(plan, "roof", "plate", head, headMf.sharedMesh, roof, Axis(up, upSign));
                    int nShards = 0;
                    if (shPA != null) { Cut(plan, "shard_plate_a", "chunk", head, headMf.sharedMesh, shPA, (Axis(up, pAu) + Axis(fwd, 1f) + Axis(side, sSign)).normalized, true); nShards++; }
                    if (shPB != null) { Cut(plan, "shard_plate_b", "chunk", head, headMf.sharedMesh, shPB, (Axis(up, pBu) + Axis(fwd, -1f) + Axis(side, sSign)).normalized, true); nShards++; }
                    if (shRA != null) { Cut(plan, "shard_roof_a", "chunk", head, headMf.sharedMesh, shRA, (Axis(side, -1f) + Axis(fwd, fwdSign) + Axis(up, upSign)).normalized, true); nShards++; }
                    if (shRB != null) { Cut(plan, "shard_roof_b", "chunk", head, headMf.sharedMesh, shRB, (Axis(side, 1f) + Axis(fwd, fwdSign) + Axis(up, upSign)).normalized, true); nShards++; }
                    yield return true;
                    sb.Append($"head: {isl.Count} part(s) in its mesh, underside closed, breech {(rearB != null ? 2 : rear != null ? 1 : 0)}, side plates {(plateB != null ? 2 : plateA != null ? 1 : 0)} ({(sSign > 0 ? "+" : "-")}{"xyz"[side]}, {(faceUp ? "the side landing up" : "by the fort")}), roof {(roof != null ? "yes" : "no")}, shards {nShards}; ");
                }
            }
            // --- the mantlet with its sleeve (the pitch mesh's two solid parts, kept together)
            if (pitch && pitch.TryGetComponent<MeshFilter>(out var pmf) && pmf.sharedMesh)
            {
                var pp = OzMeshSlice.FromMesh(pmf.sharedMesh);
                if (pp == null) Whole(plan, "mantlet", "part", pitch, pmf.sharedMesh, Vector3.zero);
                else
                {
                    var isl = OzMeshSlice.Islands(pp); var mant = isl[0];
                    for (int i = 1; i < isl.Count; i++) OzMeshSlice.Merge(mant, isl[i]);                       // its sleeve (collar) stays on it
                    yield return true;
                    // the sleeve's back end was open (it slid into the head): closed by a burnt plate just inside it
                    var pbl = pitch.InverseTransformDirection(boreW); int pf = 0;
                    for (int i = 1; i < 3; i++) if (Mathf.Abs(pbl[i]) > Mathf.Abs(pbl[pf])) pf = i;
                    float ps = pbl[pf] >= 0f ? 1f : -1f; var mb = mant.Box();
                    float back = ps > 0f ? mb.min[pf] : mb.max[pf];
                    mant = FClose(mant, Axis(pf, ps), ps * (back + ps * 0.02f * mb.size[pf]), MeshScale(pitch), rnd3);   // item 375
                    Cut(plan, "mantlet", "part", pitch, pmf.sharedMesh, mant, Vector3.zero);
                    sb.Append($"mantlet: {isl.Count} part(s); ");
                }
                yield return true;
            }
            // --- the barrel, whole
            if (recoil && recoil.TryGetComponent<MeshFilter>(out var bmf) && bmf.sharedMesh)
            {   // item 375: two barrel sections at a fractured ring break (OzFortGunBreakup.Fracture.cs); else whole as before
                if (!(rnd3 != null && SplitBarrel && BarrelHalves(plan, recoil, bmf.sharedMesh, boreW, rnd3))) Whole(plan, "barrel", "barrel", recoil, bmf.sharedMesh, Vector3.zero);
                if (rnd3 != null) yield return true;
            }
            // --- the ring: its main piece and a broken edge in two chunks; the rams stay on the piece they stand on
            if (ringMf && ringMf.sharedMesh)
            {
                var rp = OzMeshSlice.FromMesh(ringMf.sharedMesh);
                if (rp == null) Whole(plan, "ring", "ring", yaw, ringMf.sharedMesh, Vector3.zero);
                else
                {
                    var isl = OzMeshSlice.Islands(rp); var ring = isl[0];
                    for (int i = 1; i < isl.Count; i++) OzMeshSlice.Merge(ring, isl[i]);
                    var rbx = ring.Box(); float th = R(0f, Mathf.PI * 2f);
                    yield return true;
                    // its top was open where the head sat on it: closed by a burnt plate just under it
                    float rtop = upSign > 0f ? rbx.max[up] : rbx.min[up];
                    ring = FClose(ring, Axis(up, -upSign), -upSign * (rtop - upSign * 0.15f * rbx.size[up]), ysc, rnd3);   // item 375
                    foreach (var lip in toRing) OzMeshSlice.Merge(ring, lip);
                    yield return true;
                    var n = (Mathf.Cos(th) * Axis(side, 1f) + Mathf.Sin(th) * Axis(fwd, 1f)).normalized;
                    float rad = 0.5f * Mathf.Max(rbx.size[side], rbx.size[fwd]);
                    float w = Vector3.Dot(n, rbx.center) + R(0.38f, 0.48f) * rad;
                    OzMeshSlice.Part ca = null, cb = null; Vector3 n2 = Vector3.zero; float w2 = 0f;
                    if (FSplit(ring, n, w, out var seg, out var ringMain, ysc, rnd3, false))                   // item 375: fractured, its plane kept (the rams sit by it)
                    {
                        ring = ringMain;
                        if (rnd3 != null) yield return true;
                        n2 = Vector3.Cross(n, Axis(up, 1f)).normalized; var sbx = seg.Box();
                        w2 = Vector3.Dot(n2, sbx.center) + R(-0.12f, 0.12f) * Mathf.Max(sbx.size[side], sbx.size[fwd]);
                        if (FSplit(seg, n2, w2, out var s1, out var s2, ysc, rnd3, false)) { ca = s1; cb = s2; } else ca = seg;
                    }
                    yield return true;
                    Cut(plan, "ring", "ring", yaw, ringMf.sharedMesh, ring, Vector3.zero);
                    if (ca != null) Cut(plan, "ring_chunk_a", "chunk", yaw, ringMf.sharedMesh, ca, n);
                    if (cb != null) Cut(plan, "ring_chunk_b", "chunk", yaw, ringMf.sharedMesh, cb, n);
                    foreach (Transform c in yaw)
                    {
                        if (!c.name.StartsWith("FortGunRam_")) continue;
                        var lp = c.localPosition; string on = "ring";
                        if (ca != null && Vector3.Dot(n, lp) - w >= 0f) on = cb == null || Vector3.Dot(n2, lp) - w2 >= 0f ? "ring_chunk_a" : "ring_chunk_b";
                        plan.extras[c] = on;
                    }
                    sb.Append($"ring: broken edge {(cb != null ? "2 chunks" : ca != null ? "1 chunk" : "none")}; ");
                }
            }
            plan.text = sb.ToString() + (rnd3 != null ? "fracture look: " + OzFractureCut.State + "; " : "");   // item 375
            // the convex colliders, cooked one per step (MeshCollider reuses them on the landing frame)
            foreach (var d in plan.defs)
            {
                if (!d.mesh || !d.mesh.isReadable) continue;
                yield return true;
                try { Physics.BakeMesh(d.mesh.GetInstanceID(), true); Baked++; } catch (System.Exception) { }
            }
        }

        static float MeshScale(Transform t) { var s = t.lossyScale; return Mathf.Max(1e-4f, (Mathf.Abs(s.x) + Mathf.Abs(s.y) + Mathf.Abs(s.z)) / 3f); }

        /// <summary>A small shard broken off the corner of `p` in the plane of axes a, b (the corner at the box's sa / sb ends; legs la, lb
        /// in mesh units): the plane through the two leg ends. A shard thinner than 8 cm or outside 0.25-0.9 m (sc = metres per mesh unit) is
        /// tried once more with 1.3 x the legs, else none (the part stays whole).</summary>
        static bool Corner(OzMeshSlice.Part p, int a, float sa, int b, float sb, float la, float lb, float sc, out OzMeshSlice.Part shard, out OzMeshSlice.Part rest)
        {
            shard = null; rest = null;
            if (p == null || la <= 0f || lb <= 0f) return false;
            var bx = p.Box();
            float ca = sa > 0f ? bx.max[a] : bx.min[a], cb = sb > 0f ? bx.max[b] : bx.min[b];
            for (int t = 0; t < 2; t++)
            {
                float k = t == 0 ? 1f : 1.3f, ia = sa / (la * k), ib = sb / (lb * k);
                var n = Axis(a, ia) + Axis(b, ib); float len = n.magnitude;
                if (!FSplit(p, n / len, (ia * ca + ib * cb - 1f) / len, out var f, out var r, sc, fracRnd, false)) continue;   // item 375: fractured (fracRnd null = plain)
                var fs = f.Box().size * sc; float big = Mathf.Max(fs.x, Mathf.Max(fs.y, fs.z)), thin = Mathf.Min(fs.x, Mathf.Min(fs.y, fs.z));
                if (thin < 0.08f || big < 0.25f || big > 0.9f || f.Tris < 6) continue;
                shard = f; rest = r; return true;
            }
            return false;
        }

        static void Whole(Plan plan, string name, string kind, Transform src, Mesh mesh, Vector3 outward)
        {
            int tris = 0; if (mesh.isReadable) for (int s = 0; s < mesh.subMeshCount; s++) tris += (int)mesh.GetIndexCount(s) / 3;
            plan.defs.Add(new Def { name = name, kind = kind, src = src, mesh = mesh, capped = false, outward = outward, tris = tris, fracture = FractureLook });   // item 375
        }

        static void Cut(Plan plan, string name, string kind, Transform src, Mesh srcMesh, OzMeshSlice.Part part, Vector3 outward, bool shard = false)
        {
            if (part == null || part.Tris == 0) return;
            var m = OzMeshSlice.ToMesh(part, srcMesh.name + "_" + name);
            plan.defs.Add(new Def { name = name, kind = kind, src = src, mesh = m, capped = part.CapTris > 0, shard = shard, outward = outward, tris = part.Tris, fracture = FractureLook });   // item 375
        }

        // ------------------------------------------------------------------ the watcher: cut and cook while the cannon is in the air
        static OzFortGunBreakup watch; static int seenTosses = -1;
        bool watcher;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (watch) return;
            var go = new GameObject("OZ_FortGunBreakupWatch"); DontDestroyOnLoad(go);
            watch = go.AddComponent<OzFortGunBreakup>(); watch.watcher = true;
        }

        static void Clean()
        {
            var dead = new List<Transform>();
            foreach (var k in plans.Keys) if (!k) dead.Add(k);
            foreach (var k in jobs.Keys) if (!k) dead.Add(k);
            foreach (var k in dead) { plans.Remove(k); jobs.Remove(k); }
        }

        static Job StartJob(Transform yaw, int seed, Quaternion landRot)
        {
            Clean();
            var job = new Job { plan = new Plan() };
            job.it = CutOnWorker ? PrepareOnWorker(yaw, seed, landRot, job) : PrepareSteps(yaw, seed, landRot, job.plan);
            jobs[yaw] = job;
            return job;
        }

        /// <summary>One step of the cutting, timed. True = the plan is finished (or failed: no plan, the cannon lands whole).</summary>
        static bool Step(Transform yaw, Job job)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew(); bool more;
            try { more = job.it.MoveNext(); }
            catch (System.Exception e)
            {
                Debug.LogWarning("[OpenZ] fort cannon breakup: could not cut the pieces: " + e.Message);
                jobs.Remove(yaw); plans[yaw] = null; return true;
            }
            float ms = (float)sw.Elapsed.TotalMilliseconds;
            job.plan.totalMs += ms; job.plan.steps++; if (ms > job.plan.maxMs) job.plan.maxMs = ms;
            if (more) return false;
            jobs.Remove(yaw); plans[yaw] = job.plan; Prepared++;
            return true;
        }

        /// <summary>The finished plan for `yaw`; the steps not done in the air yet are done now (timed: Plan.landingMs).</summary>
        static Plan Ready(FortGunView gun, Transform yaw, int seed)
        {
            if (plans.TryGetValue(yaw, out var have)) return have;
            if (!jobs.TryGetValue(yaw, out var job)) job = StartJob(yaw, seed, gun.TossEndRot);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Landing = true; try { while (!Step(yaw, job)) { } } finally { Landing = false; }
            job.plan.landingMs = (float)sw.Elapsed.TotalMilliseconds;
            return plans.TryGetValue(yaw, out var p) ? p : null;
        }

        void WatchUpdate()
        {
            if (Enabled && FortGunView.Tosses != seenTosses)
            {
                seenTosses = FortGunView.Tosses;
                foreach (var v in FindObjectsByType<FortGunView>(FindObjectsSortMode.None))
                {
                    var y = v ? v.YawT : null;
                    if (!v || !v.Tossed || !y || !y.gameObject.activeInHierarchy || plans.ContainsKey(y) || jobs.ContainsKey(y)) continue;
                    StartJob(y, v.Fort != null ? v.Fort.Id : 0, v.TossEndRot);
                }
            }
            if (jobs.Count > 0)
            {   // one step of each cannon's cutting per frame (a cut, a closing, a mesh or a cooked collider): no frame of the flight carries it all
                stepKeys.Clear(); stepKeys.AddRange(jobs.Keys);
                foreach (var y in stepKeys)
                {
                    if (!y) { jobs.Remove(y); continue; }
                    if (jobs.TryGetValue(y, out var job)) StepFrame(y, job);
                }
            }
        }

        // ------------------------------------------------------------------ the break (landing frame)
        sealed class Pair { public Collider a, b; public OzFortGunBreakup pa, pb; public float depth0; public bool forced; }
        sealed class Group
        {
            public float wreckAt, sinkDepth = 2f, nextCheck = 0.2f; public Vector3 impact;
            public readonly List<OzFortGunBreakup> pieces = new List<OzFortGunBreakup>();
            /// <summary>Collider pairs that started inside each other and are still held apart (ignoring each other).</summary>
            public readonly List<Pair> held = new List<Pair>();
            public bool Holds(OzFortGunBreakup p) { foreach (var q in held) if (q.pa == p || q.pb == p) return true; return false; }
        }

        static int HeavyLayer { get { int l = LayerMask.NameToLayer("DebrisHeavy"); return l >= 0 ? l : HeavyLayerFallback; } }
        static PhysicsMaterial metal;
        static PhysicsMaterial Metal() => metal ? metal : (metal = new PhysicsMaterial("OZ_FortGunMetal") { bounciness = 0.08f, dynamicFriction = 0.7f, staticFriction = 0.85f, bounceCombine = PhysicsMaterialCombine.Minimum, frictionCombine = PhysicsMaterialCombine.Maximum });

        static float Depth(Collider a, Collider b) =>
            Physics.ComputePenetration(a, a.transform.position, a.transform.rotation, b, b.transform.position, b.transform.rotation, out _, out float d) ? d : 0f;

        /// <summary>A world one-shot of the sound table; true only when it really started (not dropped: gap, voices, off screen ...).</summary>
        static bool PlayAd(string id, Vector3 at, int emitter, float vol = 1f, float pitch = 1f) => AudioDirector.Has(id) && AudioDirector.I.Play(id, at, emitter, vol, pitch);

        /// <summary>The heavy metal crash of the combat bank; true only when it really started.</summary>
        static bool PlayCrash(Vector3 at, float vol, int emitter)
        {
            var ca = CombatAudio.I; if (!ca) return false;
            int p0 = ca.Played;
            ca.Play("turret", at, vol, 0, emitter, OzSoundV3.BankMetalHeavy);
            return ca.Played > p0;
        }

        /// <summary>FortGunView.UpdateToss, the landing frame, right after OzHeavyLanding.FortGun: the cannon under `yaw` falls apart. p = where
        /// it touched down (its pivot); the arc's numbers give the touch-down velocity. False = it stays whole (switched off, open water,
        /// nothing to break).</summary>
        public static bool Break(FortGunView gun, Transform yaw, Vector3 p, Vector3 from, Vector3 to, float apex, float duration, int emitter)
        {
            var g = ZGame.I;
            if (!Enabled || !gun || !yaw || !yaw.gameObject.activeInHierarchy || !g) return false;
            if (OzWaterLanding.OpenWaterAt(p, out _)) return false;                                         // over open water: lands whole, as before
            var clock = System.Diagnostics.Stopwatch.StartNew();
            int id = gun.Fort != null ? gun.Fort.Id : 0;
            Plan plan;
            try { plan = Ready(gun, yaw, id); }
            catch (System.Exception e) { Debug.LogWarning("[OpenZ] fort cannon breakup: " + e.Message); return false; }
            if (plan == null || plan.defs.Count < 2) return false;
            float T = Mathf.Max(0.1f, duration);
            var vin = new Vector3((to.x - from.x) / T, ((to.y - from.y) - 4f * apex) / T, (to.z - from.z) / T);
            var rnd = new System.Random(id * 104729 + Breaks * 31 + 205);
            float R(float a, float b) => a + (float)rnd.NextDouble() * (b - a);
            var grp = new Group();
            var holder = new GameObject("OZ_FortGunBreakup_" + id).transform;
            int layer = HeavyLayer;
            // 1. the bodies, each exactly where its part is drawn now
            foreach (var d in plan.defs)
            {
                var pc = Build(d, layer, holder);
                if (pc) { pc.grp = grp; grp.pieces.Add(pc); }
            }
            if (grp.pieces.Count < 2) { foreach (var pc in grp.pieces) if (pc) Destroy(pc.gameObject); Destroy(holder.gameObject); return false; }
            foreach (var kv in plan.extras)
            {   // the hydraulic rams ride on the ring piece they stand on (with their own small colliders)
                var src = kv.Key; if (!src || !src.gameObject.activeInHierarchy) continue;
                var host = grp.pieces.Find(x => x.PieceName == kv.Value) ?? grp.pieces.Find(x => x.PieceName == "ring");
                if (host) CopyExtra(src, host, layer);
            }
            foreach (var pc in grp.pieces) pc.CollectPoints();
            // 2. the impact point: the lowest real vertex of the whole gun, on the ground under it
            var impact = p; float lowY = float.MaxValue;
            foreach (var pc in grp.pieces) { var q = pc.LowestPoint(); if (q.y < lowY) { lowY = q.y; impact = q; } }
            impact.y = g.GroundY(impact.x, impact.z); grp.impact = impact;
            // 3. nothing starts in the ground (no depenetration pop): the whole gun lifted as one (it landed up to 0.2 m in), so the cut
            //    faces stay flush; pairs that start DEEP inside each other (the barrel in the mantlet's sleeve, the sleeve in the head, the
            //    rams in the head) ignore each other until they are apart; touching cut faces collide from the first step
            float lift = 0f; foreach (var pc in grp.pieces) lift = Mathf.Max(lift, pc.LiftNeed());
            if (lift > 0f) foreach (var pc in grp.pieces) pc.transform.position += Vector3.up * lift;
            Physics.SyncTransforms();
            RocksAtStart(grp, impact);                                                                // item 331: the rocks near the landing made now; a piece in a rock moved out
            foreach (var pc in grp.pieces) pc.StartPos = pc.transform.position;
            var heldText = new Dictionary<string, float>();
            for (int i = 0; i < grp.pieces.Count; i++)
                for (int j = i + 1; j < grp.pieces.Count; j++)
                    foreach (var a in grp.pieces[i].cols)
                        foreach (var b in grp.pieces[j].cols)
                        {
                            if (!a || !b) continue;
                            float depth = Depth(a, b);
                            if (depth <= HoldDepth) continue;
                            Physics.IgnoreCollision(a, b, true);
                            grp.held.Add(new Pair { a = a, b = b, pa = grp.pieces[i], pb = grp.pieces[j], depth0 = depth });
                            string key = grp.pieces[i].PieceName + "/" + grp.pieces[j].PieceName;
                            heldText[key] = Mathf.Max(depth, heldText.TryGetValue(key, out var dd) ? dd : 0f);
                        }
            HeldPairs += grp.held.Count;
            // 4. launch: the impact's momentum (a short skid along the flight, a low rebound) + a scatter out of the impact point
            var h = new Vector3(vin.x, 0f, vin.z); float down = Mathf.Max(0f, -vin.y);
            var headPc = grp.pieces.Find(x => x.Kind == "head"); var hc = headPc ? headPc.Centre() : impact;
            foreach (var pc in grp.pieces)
            {
                var c = pc.Centre(); float L = pc.Size; float s01 = Mathf.Clamp01((L - 0.6f) / 4.4f);
                var dir = c - impact; dir.y = 0f; if (dir.sqrMagnitude < 1e-3f) dir = new Vector3(R(-1f, 1f), 0f, R(-1f, 1f)); dir.Normalize();
                dir = Quaternion.Euler(0f, R(-30f, 30f), 0f) * dir;
                float vOut = Mathf.Lerp(4.2f, 1.0f, s01) * R(0.8f, 1.2f);
                float vUp = Mathf.Clamp(0.16f * down, 0.8f, 2.2f) * Mathf.Lerp(1.5f, 0.7f, s01) + (c.y - impact.y > 1.5f ? 0.4f : 0f);
                var spinAxis = Random.onUnitSphere; float spin = Mathf.Lerp(7f, 1.8f, s01) * R(0.7f, 1.2f);
                switch (pc.Kind)
                {
                    case "head": vOut = R(0.6f, 1.0f); vUp = R(0.4f, 0.8f); spin = R(0.8f, 1.5f); break;
                    case "ring": vOut = R(0.5f, 0.9f); vUp = R(0.3f, 0.6f); spin = R(1.0f, 1.8f); break;
                    case "plate":
                        {
                            var o = pc.Outward; var oh = new Vector3(o.x, 0f, o.z);
                            if (oh.magnitude > 0.35f) dir = Vector3.Slerp(dir, oh.normalized, 0.7f).normalized;      // facing sideways: it flies off sideways
                            else
                            {   // facing up (the cannon lies on its side): it pops up off the top and out past the head's edge
                                var away = c - hc; away.y = 0f;
                                if (away.sqrMagnitude > 0.04f) dir = Vector3.Slerp(dir, away.normalized, 0.6f).normalized;
                                vUp += Mathf.Max(0f, o.y) * 1.2f;
                            }
                            vOut *= 1.15f; var ax = Vector3.Cross(pc.Outward, Vector3.up); if (ax.sqrMagnitude < 1e-4f) ax = Vector3.Cross(pc.Outward, Vector3.right);
                            spinAxis = ax.normalized * (rnd.Next(2) == 0 ? -1f : 1f); spin = R(4f, 6f); break;   // armour plates flip away
                        }
                    case "barrel":
                        {
                            var bore = pc.BoreW; var bh = new Vector3(bore.x, 0f, bore.z);
                            if (bh.sqrMagnitude > 1e-4f) dir = Vector3.Slerp(dir, bh.normalized, 0.5f).normalized;   // it lurches off the mantlet
                            vOut = R(1.6f, 2.4f); vUp = R(0.8f, 1.3f);
                            spinAxis = bore.normalized * (rnd.Next(2) == 0 ? -1f : 1f); spin = R(2.6f, 3.6f); break;   // and rolls a little
                        }
                }
                if (pc.Shard)
                {   // a small shard snaps off its corner: out of the corner, not blown away
                    var o = pc.Outward; var oh = new Vector3(o.x, 0f, o.z);
                    if (oh.sqrMagnitude > 0.04f) dir = Vector3.Slerp(dir, oh.normalized, 0.5f).normalized;
                    vOut = R(2.0f, 3.2f);
                }
                var v = h * Mathf.Lerp(0.55f, 0.3f, s01) + dir * vOut + Vector3.up * vUp;
                pc.Launch(v, spinAxis * spin);
            }
            // the three heaviest burn / smoke for a while (the whole cannon burned 8 s)
            var byMass = new List<OzFortGunBreakup>(grp.pieces); byMass.Sort((a, b) => b.MassKg.CompareTo(a.MassKg));
            for (int i = 0; i < byMass.Count && i < 3; i++) byMass[i].burnUntil = i == 0 ? 8f : R(3.5f, 6f);
            byMass[0].bigFire = true;
            // the wreck time: as long as the whole cannon lay (FortGunView.wreckSeconds), then they sink together
            grp.wreckAt = gun.wreckSeconds + R(0f, 1f);
            float tall = 0f; foreach (var pc in grp.pieces) tall = Mathf.Max(tall, pc.Size);
            grp.sinkDepth = Mathf.Clamp(tall + 0.3f, 1.2f, 6.5f);
            // 5. the gun as drawn goes; its pieces take over
            yaw.gameObject.SetActive(false);
            // 6. the frame: metal break + debris scatter (over the crash and the thud), sparks, a scorch
            if (AudioDirector.Has(SoundBreak)) { if (AudioDirector.I.Play(SoundBreak, impact, emitter)) BreakSounds++; }
            else if (PlayCrash(impact, 1f, emitter)) BreakSounds++;
            if (PlayAd(SoundScatter, impact, emitter + 1)) ScatterSounds++;
            OpenZHitSparks.Burst(impact + Vector3.up * 0.4f, Vector3.down, FxWeapon.MachineGun, FxSurface.Metal, 2.2f, false);
            ScorchDecals.Add(impact, 2.6f, ScorchKind.Wreck);
            Breaks++; Pieces += grp.pieces.Count; LastBreakFrame = Time.frameCount; LastBreakAt = Time.time; LastImpact = impact;
            LastLandingsAtBreak = OzHeavyLanding.Landings; LastTouchSpeed = vin.magnitude; LastSource = yaw;
            LastPieces.Clear(); LastPieces.AddRange(grp.pieces);
            var hl = new List<string>(); foreach (var kv in heldText) hl.Add($"{kv.Key} {kv.Value * 100f:0} cm");
            LastHeldText = hl.Count > 0 ? string.Join(", ", hl) : "none";
            LastPlan = plan.text + $"{grp.pieces.Count} pieces, lifted {lift * 100f:0} cm as one out of the ground, {grp.held.Count} collider pairs held apart at the start (deeper than {HoldDepth * 100f:0} cm: {LastHeldText})";
            LastPrepareMaxMs = plan.maxMs; LastPrepareTotalMs = plan.totalMs; LastPrepareSteps = plan.steps; LastLandingPrepareMs = plan.landingMs;
            NoteCut(plan);
            plans.Remove(yaw);
            LastBreakMs = (float)clock.Elapsed.TotalMilliseconds;
            Debug.Log($"[OpenZ] fort cannon #{id} broke apart on its landing frame {Time.frameCount}: {LastPlan}; touch-down {vin.magnitude:0.0} m/s; this frame {LastBreakMs:0.0} ms");
            Broke?.Invoke(impact);
            return true;
        }

        static OzFortGunBreakup Build(Def d, int layer, Transform holder)
        {
            if (!d.src || !d.mesh || !d.src.TryGetComponent<MeshRenderer>(out var srcR)) return null;
            var vis = new GameObject(d.name) { layer = layer };
            vis.transform.SetPositionAndRotation(d.src.position, d.src.rotation); vis.transform.localScale = d.src.lossyScale;
            vis.AddComponent<MeshFilter>().sharedMesh = d.mesh;
            var r = vis.AddComponent<MeshRenderer>();
            var src = srcR.sharedMaterials; int nSub = d.mesh.subMeshCount;
            var mats = new Material[Mathf.Max(1, nSub)];
            for (int i = 0; i < mats.Length; i++) mats[i] = i < src.Length ? src[i] : (src.Length > 0 && src[0] ? WreckPieces.CoreMat(src[0]) : null);
            if (d.capped && nSub > 0 && src.Length > 0 && src[0]) mats[nSub - 1] = WreckPieces.CoreMat(src[0]);   // the burnt cap over a cut
            bool frac = FractureMats(d, mats, src, nSub);                                              // item 375: the pieces' own paint and broken steel (OzFortGunBreakup.Fracture.cs)
            r.sharedMaterials = mats;
            var mpb = new MaterialPropertyBlock();
            for (int i = 0; i < src.Length && i < mats.Length; i++) { if (d.capped && i == nSub - 1) continue; srcR.GetPropertyBlock(mpb, i); r.SetPropertyBlock(mpb, i); }   // paint as drawn: team colour, band, charred
            r.shadowCastingMode = ShadowCastingMode.On; r.receiveShadows = srcR.receiveShadows;
            r.lightProbeUsage = srcR.lightProbeUsage; r.reflectionProbeUsage = srcR.reflectionProbeUsage;
            r.motionVectorGenerationMode = MotionVectorGenerationMode.Object;
            Collider col;
            if (d.mesh.isReadable) { var mc = vis.AddComponent<MeshCollider>(); mc.sharedMesh = d.mesh; mc.convex = true; col = mc; }
            else { var bc = vis.AddComponent<BoxCollider>(); var mb = d.mesh.bounds; bc.center = mb.center; bc.size = mb.size; col = bc; }
            col.sharedMaterial = Metal(); RockLayerOn(col);                                           // item 331: it meets the rocks
            var root = new GameObject("OZ_FortGunPiece_" + d.name) { layer = layer };
            root.transform.SetParent(holder, false);
            root.transform.position = r.bounds.center;
            vis.transform.SetParent(root.transform, true);
            var rb = root.AddComponent<Rigidbody>();
            var ls = d.mesh.bounds.size; var sc = vis.transform.lossyScale;
            float vol = Mathf.Abs(ls.x * sc.x * ls.y * sc.y * ls.z * sc.z);
            float mass = Mathf.Clamp(vol * 5f, 0.8f, 400f) * 10f * d.massScale;                          // item 375: a split part's halves weigh what the whole did                                          // WreckPieces' rule (volume x 5, x 10), heavier cap
            rb.mass = mass; rb.linearDamping = 0.15f; rb.angularDamping = d.kind == "barrel" ? 0.8f : 0.9f; rb.maxAngularVelocity = 14f;
            rb.maxDepenetrationVelocity = 3f;
            rb.interpolation = RigidbodyInterpolation.Interpolate; rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            var pc = root.AddComponent<OzFortGunBreakup>();
            pc.rb = rb; pc.PieceName = d.name; pc.Kind = d.kind; pc.Shard = d.shard; pc.MassKg = mass; pc.Tris = d.tris; pc.Capped = d.capped;
            pc.BoxSize = Mathf.Max(Mathf.Abs(ls.x * sc.x), Mathf.Max(Mathf.Abs(ls.y * sc.y), Mathf.Abs(ls.z * sc.z)));
            pc.Outward = d.outward.sqrMagnitude > 0f ? d.src.TransformDirection(d.outward).normalized : Vector3.zero;
            pc.StartOutwardY = pc.Outward.y;
            pc.cols.Add(col); pc.vis = vis.transform;
            if (d.kind == "barrel")
            {
                var mb = d.mesh.bounds; int ax = 0; for (int i = 1; i < 3; i++) if (mb.size[i] > mb.size[ax]) ax = i;
                pc.boreLocal = d.bore.sqrMagnitude > 0f ? d.bore : Axis(ax, 1f); pc.boreSpace = vis.transform;   // item 375: a barrel section rolls about the bore
            }
            pc.Fracture = frac;                                                                       // item 375
            live.Add(pc);
            return pc;
        }

        static void CopyExtra(Transform src, OzFortGunBreakup host, int layer)
        {
            if (!src.TryGetComponent<MeshFilter>(out var mf) || !mf.sharedMesh || !src.TryGetComponent<MeshRenderer>(out var mr) || !mr.enabled) return;
            var go = new GameObject(src.name) { layer = layer };
            go.transform.SetPositionAndRotation(src.position, src.rotation); go.transform.localScale = src.lossyScale;
            go.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterials = mr.sharedMaterials;
            var mpb = new MaterialPropertyBlock();
            for (int i = 0; i < mr.sharedMaterials.Length; i++) { mr.GetPropertyBlock(mpb, i); r.SetPropertyBlock(mpb, i); }
            r.shadowCastingMode = ShadowCastingMode.On; r.motionVectorGenerationMode = MotionVectorGenerationMode.Object;
            var cc = go.AddComponent<CapsuleCollider>(); cc.direction = 1; cc.radius = 0.5f; cc.height = 2f; cc.sharedMaterial = Metal();   // the ram's own cylinder
            go.transform.SetParent(host.vis ? host.vis : host.transform, true);
            host.cols.Add(cc); RockLayerOn(cc);
        }

        // ------------------------------------------------------------------ one piece
        public string PieceName { get; private set; } = "-";
        public string Kind { get; private set; } = "-";
        /// <summary>A small shard broken off a plate's or the roof's corner.</summary>
        public bool Shard { get; private set; }
        public float MassKg { get; private set; }
        public int Tris { get; private set; }
        public bool Capped { get; private set; }
        /// <summary>Its largest size along its own axes (m).</summary>
        public float BoxSize { get; private set; }
        public Vector3 Outward { get; private set; }
        /// <summary>How much its outward side faced up when it broke off (plates: + = off the top).</summary>
        public float StartOutwardY { get; private set; }
        public Vector3 LaunchVel { get; private set; }
        public Vector3 LaunchSpin { get; private set; }
        /// <summary>Its largest world size (m) and where it came to rest: "ground", "on &lt;what&gt;", "water", "floating", "sunk".</summary>
        public float Size { get; private set; }
        public string RestOn { get; private set; } = "-";
        /// <summary>After it rested: its real lowest vertex to the exact ground under it (m, - = in); on another piece: to the surface under it.</summary>
        public float RestGap { get; private set; }
        /// <summary>How far it was moved down (or up) onto the ground when it rested (m).</summary>
        public float SnapStep { get; private set; }
        public bool Rested { get; private set; }
        public float RolledDeg { get; private set; }
        /// <summary>Its centre at rest to the impact point, flat (m).</summary>
        public float RestDistance { get; private set; }
        /// <summary>Where it started (its pivot as drawn on the landing frame) and how far it moved over the ground until it rested (flat, m).</summary>
        public Vector3 StartPos { get; private set; }
        public float Moved { get; private set; }
        /// <summary>Moved out of a piece it was still inside at <see cref="PushAt"/> s (m; 0 = never).</summary>
        public float PushedBy { get; private set; }
        public int Hits => hits;
        public int HeardHits => heard;
        public int KnockCount => knocks;
        public Vector3 BoreW => boreSpace ? boreSpace.TransformDirection(boreLocal).normalized : transform.forward;

        Rigidbody rb; Group grp; Transform vis; Transform boreSpace; Vector3 boreLocal;
        internal readonly List<Collider> cols = new List<Collider>();
        readonly List<(Transform space, Vector3[] pts)> pts = new List<(Transform, Vector3[])>();
        static readonly Dictionary<Mesh, Vector3[]> pointCache = new Dictionary<Mesh, Vector3[]>();
        float age, lastHit = -1f, lastKnock = -1f, stayAt = -1f, otherAt = -1f, burnUntil, burnT, snapT = -1f, sinkT = -1f, sinkSpeed;
        float retryAt = -1f, retryVol, retryPitch; Vector3 retryPos;
        int hits, heard, knocks, knockTries; bool frozen, crashed, bigFire, touching, retiring;
        string otherName; Vector3 snapFrom, snapTo, touchAt; Quaternion lastRot;

        void Launch(Vector3 v, Vector3 w)
        {
            LaunchVel = v; LaunchSpin = w;
            rb.linearVelocity = v; rb.angularVelocity = w;
            lastRot = transform.rotation;
        }

        Vector3 Centre() => rb ? rb.worldCenterOfMass : transform.position;

        /// <summary>It is about to rest (asleep, nearly still, or at the 7 s limit).</summary>
        bool WantsRest => !frozen && rb && age - wokeAt > 0.6f && (rb.IsSleeping() || age - wokeAt > 7f || (rb.linearVelocity.sqrMagnitude < 0.04f && rb.angularVelocity.sqrMagnitude < 0.1f));

        void CollectPoints()
        {
            if (!vis) vis = transform.childCount > 0 ? transform.GetChild(0) : transform;
            pts.Clear(); var b = new Bounds(); bool any = false;
            foreach (var r in GetComponentsInChildren<MeshRenderer>())
            {
                if (!r.TryGetComponent<MeshFilter>(out var mf) || !mf.sharedMesh) continue;
                var p = Points(mf.sharedMesh); if (p.Length > 0) pts.Add((r.transform, p));
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            Size = any ? Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z)) : 1f;
        }

        static Vector3[] Points(Mesh m)
        {
            if (pointCache.TryGetValue(m, out var c) && c != null) return c;
            Vector3[] res;
            if (m.isReadable)
            {
                var v = m.vertices; var seen = new HashSet<Vector3>(); var list = new List<Vector3>();
                for (int s = 0; s < m.subMeshCount; s++) foreach (int i in m.GetIndices(s)) if (i >= 0 && i < v.Length && seen.Add(v[i])) list.Add(v[i]);
                res = list.ToArray();
            }
            else
            {
                var b = m.bounds; res = new Vector3[8];
                for (int i = 0; i < 8; i++) res[i] = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
            }
            if (pointCache.Count > 128) { var dead = new List<Mesh>(); foreach (var k in pointCache.Keys) if (!k) dead.Add(k); foreach (var k in dead) pointCache.Remove(k); }
            pointCache[m] = res;
            return res;
        }

        static readonly List<Vector3> wpts = new List<Vector3>(4096);
        void World()
        {
            wpts.Clear();
            foreach (var (space, p) in pts) { if (!space) continue; var m = space.localToWorldMatrix; foreach (var q in p) wpts.Add(m.MultiplyPoint3x4(q)); }
        }

        Vector3 LowestPoint()
        {
            World(); var low = transform.position; float y = float.MaxValue;
            foreach (var q in wpts) if (q.y < y) { y = q.y; low = q; }
            return low;
        }

        /// <summary>Its real lowest vertex against the exact ground under it (m, + = above): the 48 lowest vertices, each with its own
        /// ground probe. `low` = that vertex.</summary>
        float Gap(out Vector3 low)
        {
            var g = ZGame.I; World(); low = transform.position;
            if (!g || wpts.Count == 0) return 0f;
            wpts.Sort((a, b) => a.y.CompareTo(b.y));
            float best = float.MaxValue;
            for (int i = 0; i < wpts.Count && i < 48; i++)
            {
                var q = wpts[i]; float e = q.y - g.GroundY(q.x, q.z);
                if (e < best) { best = e; low = q; }
            }
            return best;
        }

        /// <summary>How far it must go up so its real lowest vertex is 2 cm clear of the ground (m, 0 = clear).</summary>
        float LiftNeed() => Mathf.Max(0f, 0.02f - Gap(out _));

        void Update()
        {
            if (watcher) { WatchUpdate(); return; }
            var g = ZGame.I;
            float dt = g && g.paused ? 0f : Time.deltaTime;
            if (dt <= 0f) return;
            age += dt;
            if (grp != null && grp.pieces.Count > 0 && grp.pieces[0] == this) GroupUpdate();
            if (retiring) { Sinking(dt); return; }
            if (grp != null && age >= grp.wreckAt) { Retire(); return; }
            if (retryAt >= 0f && age >= retryAt)
            {   // a clank the sound table dropped (two pieces landing together): once more, a moment later
                retryAt = -1f;
                if (PlayAd(SoundClank, retryPos, GetInstanceID(), retryVol, retryPitch)) { Clanks++; RetriedClanks++; heard++; }
            }
            if (!frozen && rb)
            {
                RolledDeg += Quaternion.Angle(lastRot, transform.rotation); lastRot = transform.rotation;
                if (touching && age - stayAt < 0.1f && knockTries < 8 && age - lastKnock > 0.3f && age > 0.3f)
                {   // rolling / sliding over the ground: heavy metal knocks
                    var hv = rb.linearVelocity; hv.y = 0f;
                    if (rb.angularVelocity.magnitude > 1.6f || hv.magnitude > 1.3f)
                    {
                        lastKnock = age; knockTries++;
                        if (PlayAd(SoundClank, touchAt, GetInstanceID(), Mathf.Clamp(0.22f + hv.magnitude * 0.05f, 0.22f, 0.45f), MassKg > 1500f ? 0.78f : 0.88f)) { knocks++; Knocks++; }
                    }
                }
                if (age - wokeAt > 0.6f && (rb.IsSleeping() || age - wokeAt > 7f))                      // item 331: counted from a wake too
                {   // still inside another piece: it waits until the physics has pushed them apart; at PushAt it is moved out
                    bool held = grp != null && grp.Holds(this);
                    if (!held) Freeze();
                    else if (age >= PushAt) { PushOut(); Freeze(); }
                }
            }
            touching = false;
            if (snapT >= 0f)
            {   // onto the ground by its real lowest vertex (a few cm, 0.15 s)
                snapT += dt; float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(snapT / 0.15f));
                transform.position = Vector3.Lerp(snapFrom, snapTo, k);
                if (snapT >= 0.15f) { snapT = -1f; RestGap = Gap(out _); Settle(); }
            }
            if (burnUntil > 0f && age < burnUntil + 3f && (burnT -= dt) <= 0f) Burn();
        }

        void GroupUpdate()
        {
            // pairs that started inside each other: every 0.2 s until they are apart (then they collide again); a pair still inside each
            // other while one of them comes to rest collides now, both woken, and the physics pushes them apart (at most 3 m/s)
            if (grp.held.Count == 0 || age < grp.nextCheck) return;
            grp.nextCheck = age + 0.2f;
            for (int i = grp.held.Count - 1; i >= 0; i--)
            {
                var q = grp.held[i];
                if (!q.a || !q.b || !q.a.enabled || !q.b.enabled || !q.pa || !q.pb || q.pa.retiring || q.pb.retiring) { grp.held.RemoveAt(i); continue; }
                if (Depth(q.a, q.b) <= HoldDepth)
                {
                    Physics.IgnoreCollision(q.a, q.b, false); grp.held.RemoveAt(i); ReleasedPairs++;
                    continue;
                }
                if (!q.forced && (q.pa.WantsRest || q.pb.WantsRest))
                {
                    q.forced = true; ForcedPairs++;
                    Physics.IgnoreCollision(q.a, q.b, false);
                    if (q.pa.rb && !q.pa.rb.isKinematic) q.pa.rb.WakeUp();
                    if (q.pb.rb && !q.pb.rb.isKinematic) q.pb.rb.WakeUp();
                }
            }
        }

        /// <summary>At <see cref="PushAt"/> s, still inside another piece: moved straight out of it (the penetration direction and depth).</summary>
        void PushOut()
        {
            if (grp == null) return;
            for (int i = grp.held.Count - 1; i >= 0; i--)
            {
                var q = grp.held[i]; if (q.pa != this && q.pb != this) continue;
                var mine = q.pa == this ? q.a : q.b; var other = q.pa == this ? q.b : q.a;
                if (mine && other && Physics.ComputePenetration(mine, mine.transform.position, mine.transform.rotation, other, other.transform.position, other.transform.rotation, out var dir, out var dist) && dist > 0f)
                {
                    transform.position += dir * (dist + 0.01f); Physics.SyncTransforms();
                    PushedBy += dist + 0.01f; PushedOut++;
                    OutOfRocks();                                                                      // item 331: never out of a piece into a rock
                }
                if (q.a && q.b) Physics.IgnoreCollision(q.a, q.b, false);
                grp.held.RemoveAt(i);
            }
        }

        void Freeze()
        {
            frozen = true; rb.isKinematic = true; rb.interpolation = RigidbodyInterpolation.None;
            RestDistance = grp != null ? Vector3.Distance(new Vector3(transform.position.x, 0f, transform.position.z), new Vector3(grp.impact.x, 0f, grp.impact.z)) : 0f;
            Moved = Vector3.Distance(new Vector3(transform.position.x, 0f, transform.position.z), new Vector3(StartPos.x, 0f, StartPos.z));
            if (OzWaterLanding.InWater(transform)) { RestOn = "water"; InWaterCount++; Settle(); return; }
            float gap = Gap(out var low);
            var on = Under(low, out float support);
            bool ground = on == "ground";
            if (!ground && on != null) { RestOn = "on " + on; RestGap = support; OnOther++; Settle(); return; }                      // on another piece / the ruin, right under its lowest point
            if (!ground && gap > 0.06f && otherName != null && age - otherAt < 0.6f) { RestOn = "on " + otherName; RestGap = 0f; OnOther++; Settle(); return; }   // it lies on something beside its lowest point
            if (ground || (gap < 0.35f && gap > -0.35f))
            {   // on the ground: its real lowest vertex 3 cm in (the convex hull may stand a few cm off the drawn shape); leaning on another
                // piece, only down onto the ground (0.5 cm in), so it does not go into that piece
                float step = -Sink - gap;
                float lim = grp != null ? Mathf.Min(Mathf.Max(DepthIfMoved(Vector3.zero), 0.01f) + 0.015f, HoldDepth - 0.005f) : 0f;   // K271: never past the 3 cm limit (was: now + 1.5 cm, so 2.2 -> 3.7 cm)
                if (step < 0f && grp != null && DepthIfMoved(Vector3.up * step) > lim) step = Mathf.Min(0f, -gap - 0.005f);
                if (step < 0f && grp != null && DepthIfMoved(Vector3.up * step) > lim)
                {   // K271: still too deep in that piece: the deepest step down that stays within the limit (halved 6 times)
                    float s0 = 0f, s1 = step; for (int i = 0; i < 6; i++) { float m = (s0 + s1) * 0.5f; if (DepthIfMoved(Vector3.up * m) > lim) s1 = m; else s0 = m; } step = s0;
                }
                RestOn = "ground"; snapFrom = transform.position; snapTo = transform.position + Vector3.up * step;
                SnapStep = Mathf.Abs(step); snapT = 0f; OnGround++;
                return;
            }
            RestOn = gap > 0f ? "floating" : "sunk"; RestGap = gap; if (gap > 0f) Floating++; else Sunk++;
            Settle();
        }

        void Settle() { Rested = true; RestedCount++; }

        /// <summary>How deep its colliders would lie in another piece of its group if it moved by `delta` (m).</summary>
        float DepthIfMoved(Vector3 delta)
        {
            float deep = 0f;
            foreach (var o in grp.pieces)
            {
                if (!o || o == this) continue;
                foreach (var a in cols)
                {
                    if (!a || !a.enabled) continue;
                    foreach (var b in o.cols)
                        if (b && b.enabled && Physics.ComputePenetration(a, a.transform.position + delta, a.transform.rotation, b, b.transform.position, b.transform.rotation, out _, out float d))
                            deep = Mathf.Max(deep, d);
                }
            }
            return Mathf.Max(deep, RockDepthIfMoved(delta));                                          // item 331: nor into a rock
        }

        /// <summary>What lies right under its lowest vertex (within 8 cm): "ground", another collider's name, or null; `support` = the
        /// distance down to it (m).</summary>
        string Under(Vector3 low, out float support)
        {
            support = 0f;
            var hits = Physics.RaycastAll(low + Vector3.up * 0.4f, Vector3.down, 0.4f + 0.08f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue; Collider c = null;
            foreach (var h in hits) { if (cols.Contains(h.collider) || h.collider.transform.IsChildOf(transform)) continue; if (h.distance < best) { best = h.distance; c = h.collider; } }
            if (!c) return null;
            support = best - 0.4f;
            restCol = c;
            if (IsGround(c)) return "ground";
            if (OzRockSolid.IsRock(c)) return "rock";                                                  // item 331
            var host = c.GetComponentInParent<OzFortGunBreakup>();
            return host ? host.PieceName : c.transform.root.name;
        }

        static bool IsGround(Collider c) => c && (c.gameObject.layer == ZGame.WalkLayer || CraterField.IsGroundCollider(c));

        void Burn()
        {
            bool burning = age < burnUntil; burnT = FireBeatGap(burning);   // item 400: each piece its own beat (OzFortGunBreakup.FireBeat.cs)
            var at = Centre();
            if (bigFire && burning && FxManager.I) { FxManager.I.Burn(at + Vector3.up * 0.5f + Random.insideUnitSphere * 0.8f, Mathf.Lerp(0.9f, 0.3f, age / Mathf.Max(1f, burnUntil))); return; }   // as the whole cannon's wreck burned
            var fxp = OpenZFxPlayer.I;
            if (fxp && fxp.isActiveAndEnabled) fxp.WreckSmoke(at + Vector3.up * 0.3f, burning ? 0.6f : 0.35f, burning);
        }

        void Retire()
        {
            if (retiring) return;
            retiring = true; snapT = -1f; retryAt = -1f;
            if (rb) { rb.isKinematic = true; rb.interpolation = RigidbodyInterpolation.None; }
            foreach (var c in cols) if (c) c.enabled = false;
            sinkSpeed = (grp != null ? grp.sinkDepth : 2f) / 3f; sinkT = 0f;
            if (!OzWaterLanding.InWater(transform) && WreckFx.OnScreen(transform.position)) FxManager.I?.Dust(transform.position, 0.3f);
            RetireOnRock();                                                                            // item 331: on a rock it pops away instead of sinking through it
        }

        void Sinking(float dt)
        {   // they all go down together at one speed, over 3 s (as the whole cannon sank)
            sinkT += dt; transform.position += Vector3.down * sinkSpeed * dt;
            if (sinkT >= 3f) { Faded++; Destroy(gameObject); }
        }

        void OnCollisionEnter(Collision c)
        {
            if (watcher || retiring || frozen) return;
            float sp = c.relativeVelocity.magnitude;
            var at = c.contactCount > 0 ? c.GetContact(0).point : transform.position;
            touchAt = at;
            if (OzRockSolid.IsRock(c.collider)) RockTouches++;                                         // item 331
            if (age < 0.06f || hits >= 6 || age - lastHit < 0.25f || sp < 1.2f) return;
            if (OzWaterLanding.UnderSurface(at)) return;                                                   // b27: its splash is made at the surface
            lastHit = age; hits++;
            if (!crashed && MassKg >= 250f && sp > 2.5f && WreckFx.OnScreen(at))
            {   // a heavy part slams down: metal crash + dust
                crashed = true;
                var fxp = OpenZFxPlayer.I; if (fxp && fxp.isActiveAndEnabled) fxp.Dust(at, Mathf.Min(1.4f, 0.5f + sp * 0.06f)); else FxManager.I?.Dust(at, 0.7f);
                if (PlayCrash(at, Mathf.Clamp01(0.55f + sp * 0.05f), GetInstanceID())) { Crashes++; heard++; return; }
            }
            float vol = Mathf.Clamp01(0.3f + sp * 0.07f), pitch = MassKg > 1500f ? 0.8f : MassKg > 200f ? 0.9f : 1.05f;
            if (PlayAd(SoundClank, at, GetInstanceID(), vol, pitch)) { Clanks++; heard++; }
            else if (retryAt < 0f) { retryAt = age + 0.07f; retryVol = vol; retryPitch = pitch; retryPos = at; }
        }

        void OnCollisionStay(Collision c)
        {
            if (watcher || frozen) return;
            touching = true; stayAt = age;
            if (c.contactCount > 0) touchAt = c.GetContact(0).point;
            if (!IsGround(c.collider)) { var host = c.collider.GetComponentInParent<OzFortGunBreakup>(); bool rock = OzRockSolid.IsRock(c.collider); otherName = rock ? "rock" : host ? host.PieceName : c.collider.transform.root.name; otherAt = age; if (rock) leanCol = c.collider; }   // item 331
        }

        void Start() { if (!watcher && rb) OzWaterLanding.Watch(transform, "fort cannon piece", rb); }

        void OnDestroy()
        {
            live.Remove(this);
            if (grp != null) grp.pieces.Remove(this);
            if (grp != null && grp.pieces.Count == 0 && transform.parent && transform.parent.childCount <= 1) Destroy(transform.parent.gameObject);
        }

        /// <summary>Play check: every piece of the latest break has come to rest.</summary>
        public static bool LastAllRested()
        {
            if (LastPieces.Count == 0) return false;
            foreach (var p in LastPieces) if (p && !p.Rested) return false;
            return true;
        }

        /// <summary>The cannon under `yaw` lies in pieces now: the latest break came from it and at least 8 of its pieces are drawn.</summary>
        public static bool ShownFor(Transform yaw)
        {
            if (!yaw || LastSource != yaw) return false;
            int n = 0;
            foreach (var p in LastPieces)
            {
                if (!p || !p.isActiveAndEnabled) continue;
                var r = p.GetComponentInChildren<MeshRenderer>(); if (r && r.enabled) n++;
            }
            return n >= 8;
        }

        /// <summary>Play check: pairs of pieces of the latest break whose colliders lie more than <see cref="HoldDepth"/> inside each other
        /// now; `worst` = the deepest (m), `which` = that pair.</summary>
        public static int ThroughEachOther(out float worst, out string which)
        {
            worst = 0f; which = "-"; int n = 0;
            for (int i = 0; i < LastPieces.Count; i++)
                for (int j = i + 1; j < LastPieces.Count; j++)
                {
                    var pi = LastPieces[i]; var pj = LastPieces[j]; if (!pi || !pj) continue;
                    float deep = 0f;
                    foreach (var a in pi.cols) foreach (var b in pj.cols) if (a && b && a.enabled && b.enabled) deep = Mathf.Max(deep, Depth(a, b));
                    if (deep > worst) { worst = deep; which = pi.PieceName + " / " + pj.PieceName; }
                    if (deep > HoldDepth) n++;
                }
            return n;
        }
    }
}
