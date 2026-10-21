using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// The playable shuttle's landing legs FOLD in flight flat under the belly, toward its centre, like an insect's legs, and come down for
    /// the landing (rule DV118, view only: the rules' flight, landing and timing are unchanged; the kit's twin tools/legfold.py holds the
    /// same numbers and rules). It works on the visible suspension's rig (OzPlayableShuttle.Suspension.cs: each leg cut into its foot, its
    /// strut and the chrome piston); without that rig nothing changes.
    /// <list type="bullet">
    /// <item>THE RIG (cut once per ship from the suspension's strut mesh): the strut is cut by the level plane <see cref="LegsCutY"/>, the red
    /// pod's underside (where its red cuff ends and the dark knuckle begins). Everything under it, the WHOLE visible leg (the knuckle and the
    /// dark rod), becomes a new part (SH_Leg_xx_Fold); the short stub over it stays in its socket inside the pod. The stub's cut is closed by
    /// the POD CAP, a flat cap over the cut's own outline in the pod's own team-painted panel texel, so the pod reads whole once the leg has
    /// swung out; the leg's cut is closed by a flat cap in the dark gunmetal texel. The HINGE lies at the pod's underside on its side toward
    /// the belly's centre (just beyond the leg's furthest point that way); its axis is level, square to the fold's direction.</item>
    /// <item>THE MOTION (the fold's progress 0 = the suspension's own pose exactly, 1 = folded): the chrome piston closes (the foot's hang
    /// eases to the parked one) over the first <see cref="LegsPistonPart"/>; then the whole leg (knuckle, rod, piston, foot with its pad
    /// lamps and neon) swings about the hinge, in and up under the belly, <see cref="LegsHingeDeg"/> at the end, until it lies close under
    /// the belly pointing at its centre; the foot tucks at the ankle <see cref="LegsAnkleDeg"/> so the pad lies flat under the folded leg.
    /// Nothing ever touches the hull, a pod, a lamp, a lift jet, the ramp or another leg in any frame (the kit's per-frame test against the
    /// model). The belly searchlights and the nose lamp are on the hull and do not move (no leg part enters their beams).</item>
    /// <item>WHEN: once the pads are <see cref="LegsFoldFeetM"/> over the ground after a lift-off, over <see cref="LegsFoldSec"/>; folded in
    /// cruise, while hovering (also low over a spot it cannot land) and through a kickflip. A normal descent lowers them
    /// <see cref="LegsUnfoldAtSec"/> before the touchdown (<see cref="LegsUnfoldSec"/>): down and locked <see cref="LegsLockBeforeSec"/>
    /// before the pads touch (a guard holds that whatever happened before), so the suspension's touchdown starts from its exact landing pose.
    /// A crash descent drops them at once and fast (<see cref="LegsUnfoldRoughSec"/>). Standing, they are always down. Shot down: the leg
    /// pieces fly from the pose the leg is in (<see cref="LegsBreakFrom"/>).</item>
    /// <item>SOUND: the hydraulic whine (shuttle_leg_retract) as a fold or an unfold starts, the clunk (shuttle_leg_thud) as it ends (folded;
    /// down and locked).</item>
    /// </list>
    /// </summary>
    public sealed partial class OzPlayableShuttle
    {
        // ------------------------------------------------------------------ the numbers (the twin's: tools/legfold.py)
        /// <summary>Off = the legs as the suspension draws them (a test switch; the cut parts then sit exactly as the whole strut did).</summary>
        public static bool LegsFoldOn = true;
        /// <summary>The cut: the root-space height of the red pod's underside (m; FL FR BL BR).</summary>
        public static readonly float[] LegsCutY = { 0.94f, 1.05f, 1.00f, 0.98f };
        /// <summary>The belly's centre the legs fold toward (root space; only x and z are used).</summary>
        public static Vector3 LegsFoldCentre = new Vector3(0f, 0f, -0.869f);
        /// <summary>Per leg (FL FR BL BR): the fold's direction off the line to the centre (deg about +y); the whole leg's swing about the hinge
        /// and the foot's tuck at the ankle at the end of the fold (deg); the hinge beyond the leg's furthest point toward the fold's direction,
        /// and under the cut (m).</summary>
        public static readonly float[] LegsFoldYaw = { 4.6f, -3.6f, 4.8f, -8.4f }, LegsHingeDeg = { 139.3f, 127.8f, 142.0f, 143.6f }, LegsAnkleDeg = { -137.6f, -128.1f, -141.9f, -143.5f },
                                       LegsHingeIn = { 0.115f, 0.056f, 0.000f, 0.032f }, LegsHingeDn = { 0.001f, 0.001f, 0.031f, 0.019f };
        /// <summary>The ankle: its cut this far up the rod from the collar's top (the rod's first closed section, m); the rod's short tip under
        /// the cut rides on the rod up to this share of the fold and on the foot after it (both poses are the same there: the piston closed,
        /// the ankle not yet turning); per leg, its hinge this far beyond the rod's edge on the side that faces down when folded (m: the
        /// joint opens like a hinge and the tucked pad lies under the rod, not through it).</summary>
        public static float LegsAnkleT = 0.07f, LegsTipSwitch = 0.375f;
        public static readonly float[] LegsAnkleIn = { 0.138f, 0.114f, 0.109f, 0.056f };
        /// <summary>The share of the fold over which the piston closes; the shares at which the hinge and the ankle start.</summary>
        public static float LegsPistonPart = 0.30f, LegsHingeFrom = 0.20f, LegsAnkleFrom = 0.45f;
        /// <summary>The fold, a normal unfold, a crash descent's unfold, an unfold standing (s, game time).</summary>
        public static float LegsFoldSec = 0.9f, LegsUnfoldSec = 0.9f, LegsUnfoldRoughSec = 0.5f, LegsUnfoldLandedSec = 0.3f;
        /// <summary>The pads this far over the ground after a lift-off: the legs fold (m); a normal descent: the unfold starts this many
        /// seconds before the touchdown; down and locked at the latest this many seconds before it.</summary>
        public static float LegsFoldFeetM = 1.5f, LegsUnfoldAtSec = 1.5f, LegsLockBeforeSec = 0.6f;
        /// <summary>The sounds: the whine's volume and pitch (fold, unfold), the clunk's (folded, locked).</summary>
        public static float LegsWhineVol = 0.85f, LegsWhinePitchFold = 1.12f, LegsWhinePitchUnfold = 1.02f, LegsClunkStowVol = 0.5f, LegsClunkStowPitch = 1.15f, LegsClunkLockVol = 0.7f, LegsClunkLockPitch = 0.95f;
        /// <summary>The pod's inner sleeve: from the cut this far up the leg's axis, this far inside the cut's outline (m). It closes the stub's
        /// own open seams over the cut (the model leaves them where the pod covered them), so the pod reads whole from every side.</summary>
        public static float LegsSleeveH = 0.34f, LegsSleeveIn = 0.015f;
        /// <summary>The caps' texels of the shuttle atlas (L16_Shuttle_Albedo): the leg's cut a uniform dark gunmetal block (no team paint, no
        /// glow); the pod cap a uniform team-painted panel block (the pod's own red, as its painted panels).</summary>
        static readonly Vector2 LegsCapUv = new Vector2(1668.5f / 2048f, 1f - 1272.5f / 2048f), LegsPodUv = new Vector2(1638.5f / 2048f, 1f - 1691.5f / 2048f);

        // ------------------------------------------------------------------ read by the Play checks
        /// <summary>Folds and unfolds started, folded and locked ends, whines and clunks fired, guard catches (an unfold forced by the
        /// touchdown's guard), ships fitted / not fitted.</summary>
        public static int LegsFolds, LegsUnfolds, LegsStowedN, LegsLockedN, LegsWhines, LegsClunks, LegsGuardN, LegsFits, LegsFitFails;
        /// <summary>The last fold: folded this many s after the lift-off; the last unfold: locked this many s before the touchdown (the rules'
        /// remaining descent; -1 = not on a descent).</summary>
        public static float LegsStowedAfterLiftSec = -1f, LegsLockedBeforeTouchSec = -1f;
        /// <summary>This ship: the fold's progress (0 down .. 1 folded), its direction (+1 folding, -1 unfolding), the rig is ready.</summary>
        public float LegsFoldNow => legsPh;
        public int LegsFoldDir => legsDir;
        public bool LegsFoldReady => legsOk && legsRig != null;
        /// <summary>This ship, leg i: the hinge's and the ankle's turn now (deg); how far its pad's sole middle has come toward the belly's
        /// centre over the parked pad (m, on the drawn foot); the pad's tilt from lying flat (deg: 0 sole down, 180 sole up); its sole middle's
        /// height in the hull's space (m).</summary>
        public float LegsHingeNow(int i) => i >= 0 && i < 4 ? legsHinge[i] : 0f;
        public float LegsAnkleNow(int i) => i >= 0 && i < 4 ? legsAnkle[i] : 0f;
        public float LegsPadIn(int i) => i >= 0 && i < 4 ? legsPadIn[i] : 0f;
        public float LegsPadTilt(int i) => i >= 0 && i < 4 ? legsPadTilt[i] : 0f;
        public float LegsPadY(int i) => i >= 0 && i < 4 ? legsPadY[i] : 0f;
        /// <summary>The rig's cut of leg i (F0's numbers): cut loops, the pod cap's and the leg cap's triangles.</summary>
        public int LegsCutLoopsOf(int i) => legsRig != null && i >= 0 && i < 4 ? legsRig[i].loops : 0;
        public int LegsPodCapTris(int i) => legsRig != null && i >= 0 && i < 4 ? legsRig[i].podTris : 0;
        public int LegsLegCapTris(int i) => legsRig != null && i >= 0 && i < 4 ? legsRig[i].legTris : 0;
        public int LegsSleeveTris(int i) => legsRig != null && i >= 0 && i < 4 ? legsRig[i].sleeveTris : 0;
        public int LegsAnkleCapTris(int i) => legsRig != null && i >= 0 && i < 4 ? legsRig[i].ankleTris : 0;

        float legsPh, legsLiftT = -1f; int legsDir; bool legsInit, legsOk = true;
        readonly float[] legsHinge = new float[4], legsAnkle = new float[4], legsPadIn = new float[4], legsPadTilt = new float[4], legsPadY = new float[4];
        LegsJoint[] legsRig;
        MaterialPropertyBlock legsMpb;

        /// <summary>One leg's fold rig: the folding part (its object and renderer), the hinge (pivot, axis) and the ankle's hinge, in the
        /// root's space of the parked model.</summary>
        sealed class LegsJoint { public Transform fold, tip; public MeshRenderer foldR, tipR; public Vector3 pivot, axis, ankleHinge; public int loops, podTris, legTris, sleeveTris, ankleTris; }

        static float LegsEase(float u) { u = Mathf.Clamp01(u); return u * u * u * (u * (u * 6f - 15f) + 10f); }   // smootherstep

        /// <summary>The suspension's foot hang under the hull (x + d), with the piston closing (OzPlayableShuttle.Suspension.cs asks here).</summary>
        float LegsFoldHang(float hang) => LegsFoldOn && legsOk && susRig != null && legsPh > 0f ? hang * (1f - LegsEase(legsPh / LegsPistonPart)) : hang;

        // ------------------------------------------------------------------ the rig, cut once from the suspension's strut meshes
        /// <summary>The root-space matrix of a leg part (the foot or a strut piece) in its parked pose under its parent.</summary>
        Matrix4x4 LegsRest(SusLeg L, Transform part) => transform.worldToLocalMatrix * part.parent.localToWorldMatrix * Matrix4x4.TRS(L.restPos, L.restRot, part.localScale);

        /// <summary>The fold's level direction from the root-space point c: toward the belly's centre, turned the leg's yaw about +y.</summary>
        static Vector3 LegsFoldHeading(Vector3 c, int i)
        {
            var h = LegsFoldCentre - c; h.y = 0f; h.Normalize();
            return Quaternion.AngleAxis(LegsFoldYaw[i], Vector3.up) * h;
        }

        void LegsFit()
        {
            var made = new OzMeshSlice.Part[4, 3]; var joints = new LegsJoint[4];
            for (int i = 0; i < 4; i++)
            {
                var L = susRig[i]; var mf = L != null && L.strut ? L.strut.GetComponent<MeshFilter>() : null;
                if (!mf || !mf.sharedMesh || !L.strut.parent) { LegsFitFail(i, "no strut mesh"); return; }
                var src = OzMeshSlice.FromMesh(mf.sharedMesh); if (src == null) { LegsFitFail(i, "the strut mesh is not readable"); return; }
                var meshToRoot = LegsRest(L, L.strut); var rootToMesh = meshToRoot.inverse;
                var J = new Vector3(L.ankle.x, LegsCutY[i], L.ankle.z);
                var n = rootToMesh.MultiplyVector(Vector3.up).normalized; float w = Vector3.Dot(n, rootToMesh.MultiplyPoint3x4(J));
                var loops = LegsCutLoops(src, n, w);
                float area = 0f; foreach (var lp in loops) area += LegsLoopArea(lp, meshToRoot);
                if (loops.Count == 0 || area < 0.15f) { LegsFitFail(i, "the pod's cut is not closed (" + loops.Count + " loop(s), " + area.ToString("F3") + " m2; measured 0.24 .. 0.31)"); return; }
                if (!OzMeshSlice.Split(src, n, w, out var stub, out var leg, false, false)) { LegsFitFail(i, "the pod's cut misses the strut"); return; }
                int podTris = LegsCap(stub, loops, -n, LegsPodUv), legTris = LegsCap(leg, loops, n, LegsCapUv);
                if (podTris == 0 || legTris == 0) { LegsFitFail(i, "the pod's cut has no outline to cap"); return; }
                var c = Vector3.zero; int nc = 0;
                foreach (var lp in loops) foreach (var v in lp) { c += meshToRoot.MultiplyPoint3x4(v.p); nc++; }
                c /= nc; c.y = LegsCutY[i];
                int sleeveTris = LegsSleeve(stub, loops, meshToRoot, c, L.axis, LegsSleeveH, LegsSleeveIn, LegsPodUv);
                // the hinge: at the pod's underside, just beyond the leg's furthest point toward the fold's direction; its axis level, across
                var f = LegsFoldHeading(c, i);
                float e = float.MinValue;
                foreach (var v in leg.v) e = Mathf.Max(e, Vector3.Dot(meshToRoot.MultiplyPoint3x4(v.p) - c, f));
                if (!(e > 0.1f && e < 0.7f)) { LegsFitFail(i, "the leg reaches " + e.ToString("F3") + " m toward the belly's centre (measured 0.34 .. 0.37)"); return; }
                // the ankle: the rod cut LegsAnkleT over the collar's top; its tip rides on the rod, then on the foot; the hinge at the rod's edge
                var Aa = L.ankle + L.axis * LegsAnkleT;
                var na = rootToMesh.MultiplyVector(L.axis).normalized; float wa = Vector3.Dot(na, rootToMesh.MultiplyPoint3x4(Aa));
                var aloops = LegsCutLoops(src, na, wa);
                if (aloops.Count == 0) { LegsFitFail(i, "the ankle's cut is not closed"); return; }
                if (!OzMeshSlice.Split(leg, na, wa, out var rod, out var tip, false, false)) { LegsFitFail(i, "the ankle's cut misses the rod"); return; }
                int ankleTris = LegsCap(rod, aloops, -na, LegsCapUv); LegsCap(tip, aloops, na, LegsCapUv);
                var md = -f + Vector3.Dot(f, L.axis) * L.axis; md.Normalize();
                float ea = float.MinValue;
                foreach (var lp in aloops) foreach (var v in lp) ea = Mathf.Max(ea, Vector3.Dot(meshToRoot.MultiplyPoint3x4(v.p) - Aa, md));
                if (!(ea > 0.03f && ea < 0.3f)) { LegsFitFail(i, "the rod's edge is " + ea.ToString("F3") + " m off the axis (measured 0.10 .. 0.15)"); return; }
                joints[i] = new LegsJoint { pivot = c + f * (e + LegsHingeIn[i]) - Vector3.up * LegsHingeDn[i], axis = Vector3.Cross(-Vector3.up, f).normalized, ankleHinge = Aa + md * (ea + LegsAnkleIn[i]), loops = loops.Count, podTris = podTris, legTris = legTris, sleeveTris = sleeveTris, ankleTris = ankleTris };
                made[i, 0] = stub; made[i, 1] = rod; made[i, 2] = tip;
            }
            // every leg measured: now the parts (the strut keeps the stub, a new part beside it takes the whole visible leg)
            var keeper = GetComponent<OzShuttleSuspensionMeshes>(); if (!keeper) keeper = gameObject.AddComponent<OzShuttleSuspensionMeshes>();
            for (int i = 0; i < 4; i++)
            {
                var L = susRig[i]; var J = joints[i];
                var sm = OzMeshSlice.ToMesh(made[i, 0], LegNames[i] + "_Stub"); var lm = OzMeshSlice.ToMesh(made[i, 1], LegNames[i] + "_Fold"); var tm = OzMeshSlice.ToMesh(made[i, 2], LegNames[i] + "_Tip");
                keeper.Own.Add(sm); keeper.Own.Add(lm); keeper.Own.Add(tm);
                L.strut.GetComponent<MeshFilter>().sharedMesh = sm;
                J.fold = LegsPart(L, LegNames[i] + "_Fold", lm, out J.foldR);
                J.tip = LegsPart(L, LegNames[i] + "_Tip", tm, out J.tipR);
            }
            legsRig = joints;
            LegsFits++;
        }

        /// <summary>A new part beside the strut (the same parent, frame, paint and settings) drawing mesh m.</summary>
        Transform LegsPart(SusLeg L, string name, Mesh m, out MeshRenderer r)
        {
            var g = new GameObject(name) { layer = L.strut.gameObject.layer };
            g.transform.SetParent(L.strut.parent, false); g.transform.localPosition = L.strut.localPosition; g.transform.localRotation = L.strut.localRotation; g.transform.localScale = L.strut.localScale;
            g.AddComponent<MeshFilter>().sharedMesh = m;
            r = g.AddComponent<MeshRenderer>(); SusCopy(L.strutR, r);
            return g.transform;
        }

        /// <summary>The closed loops where the plane dot(n, p) = w cuts the part (mesh space): one segment per crossing triangle, the segments
        /// joined end to end through their welded corners; every vertex channel interpolated as OzMeshSlice.Split does.</summary>
        static List<List<OzMeshSlice.V>> LegsCutLoops(OzMeshSlice.Part src, Vector3 n, float w)
        {
            var b = src.Box(); float q = Mathf.Max(1e-7f, Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z)) * 2e-6f);
            var pts = new List<OzMeshSlice.V>(); var key = new Dictionary<Vector3Int, int>(); var adj = new List<List<int>>();
            int Weld(in OzMeshSlice.V v)
            {
                var k = new Vector3Int(Mathf.RoundToInt(v.p.x / q), Mathf.RoundToInt(v.p.y / q), Mathf.RoundToInt(v.p.z / q));
                if (!key.TryGetValue(k, out int id)) { id = pts.Count; key[k] = id; pts.Add(v); adj.Add(new List<int>(2)); }
                return id;
            }
            var hit = new OzMeshSlice.V[3];
            for (int si = 0; si < src.sub.Length - 1; si++)
            {
                var s = src.sub[si];
                for (int k = 0; k + 2 < s.Count; k += 3)
                {
                    int m = 0;
                    for (int e = 0; e < 3; e++)
                    {
                        var va = src.v[s[k + e]]; var vb = src.v[s[k + (e + 1) % 3]];
                        if (LegsBefore(vb.p, va.p)) { var t = va; va = vb; vb = t; }   // each edge in one order: both its triangles get the very same point
                        float da = Vector3.Dot(n, va.p) - w, db = Vector3.Dot(n, vb.p) - w;
                        if ((da > 0f && db < 0f) || (da < 0f && db > 0f)) { if (m < 3) hit[m] = LegsLerp(va, vb, da / (da - db)); m++; }
                    }
                    if (m != 2) continue;
                    int a = Weld(hit[0]), c = Weld(hit[1]); if (a == c) continue;
                    adj[a].Add(c); adj[c].Add(a);
                }
            }
            var loops = new List<List<OzMeshSlice.V>>(); var used = new bool[pts.Count];
            for (int v0 = 0; v0 < pts.Count; v0++)
            {
                if (used[v0] || adj[v0].Count != 2) continue;
                var loop = new List<OzMeshSlice.V> { pts[v0] }; used[v0] = true; int prev = -1, cur = v0; bool closed = false;
                for (int guard = 0; guard < pts.Count; guard++)
                {
                    int nx = -1;
                    foreach (int wv in adj[cur]) if (wv != prev && (!used[wv] || (wv == v0 && loop.Count > 2))) { nx = wv; break; }
                    if (nx < 0) break;
                    if (nx == v0) { closed = true; break; }
                    if (adj[nx].Count != 2) break;
                    loop.Add(pts[nx]); used[nx] = true; prev = cur; cur = nx;
                }
                if (closed && loop.Count >= 3) loops.Add(loop);
            }
            return loops;
        }

        static bool LegsBefore(Vector3 a, Vector3 b) => a.x != b.x ? a.x < b.x : a.y != b.y ? a.y < b.y : a.z < b.z;

        static OzMeshSlice.V LegsLerp(in OzMeshSlice.V a, in OzMeshSlice.V b, float k) => new OzMeshSlice.V
        {
            p = Vector3.LerpUnclamped(a.p, b.p, k), n = Vector3.LerpUnclamped(a.n, b.n, k).normalized,
            u0 = Vector2.LerpUnclamped(a.u0, b.u0, k), u1 = Vector2.LerpUnclamped(a.u1, b.u1, k), c = Color.LerpUnclamped(a.c, b.c, k),
            t = new Vector4(Mathf.LerpUnclamped(a.t.x, b.t.x, k), Mathf.LerpUnclamped(a.t.y, b.t.y, k), Mathf.LerpUnclamped(a.t.z, b.t.z, k), a.t.w)
        };

        /// <summary>Closes a piece's cut with a flat cap over each loop's own outline (ear clipping; never sticking out of the surface), facing
        /// `outward` (OzMeshSlice's front faces: counter-clockwise in (e1, e2), e1 x e2 = outward), on the texel uv, in the piece's first
        /// sub-mesh (its own material). Returns the cap's triangles.</summary>
        static int LegsCap(OzMeshSlice.Part o, List<List<OzMeshSlice.V>> loops, Vector3 outward, Vector2 uv)
        {
            var e1 = Vector3.Cross(outward, Mathf.Abs(outward.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            var e2 = Vector3.Cross(outward, e1);
            var tan = new Vector4(e1.x, e1.y, e1.z, 1f); int tris = 0;
            var q = new List<Vector2>(); var idx = new List<int>();
            foreach (var loop in loops)
            {
                int b0 = o.v.Count; q.Clear(); idx.Clear();
                foreach (var v in loop)
                {
                    q.Add(new Vector2(Vector3.Dot(v.p, e1), Vector3.Dot(v.p, e2)));
                    var c = v; c.n = outward; c.u0 = uv; c.t = tan; o.v.Add(c);
                }
                float area = 0f; for (int k = 0; k < q.Count; k++) { var a = q[k]; var bq = q[(k + 1) % q.Count]; area += a.x * bq.y - bq.x * a.y; }
                for (int k = 0; k < q.Count; k++) idx.Add(area >= 0f ? k : q.Count - 1 - k);
                for (int guard = 0; idx.Count > 3 && guard < 4096; guard++)
                {
                    bool found = false;
                    for (int k = 0; k < idx.Count; k++)
                    {
                        int i0 = idx[(k + idx.Count - 1) % idx.Count], i1 = idx[k], i2 = idx[(k + 1) % idx.Count];
                        Vector2 a = q[i0], bq = q[i1], c = q[i2];
                        if (Cross2(a, bq, c) <= 1e-12f) continue;
                        bool inside = false;
                        foreach (int j in idx) { if (j == i0 || j == i1 || j == i2) continue; var pj = q[j]; if (Cross2(a, bq, pj) >= 0f && Cross2(bq, c, pj) >= 0f && Cross2(c, a, pj) >= 0f) { inside = true; break; } }
                        if (inside) continue;
                        o.sub[0].Add(b0 + i0); o.sub[0].Add(b0 + i1); o.sub[0].Add(b0 + i2); tris++; idx.RemoveAt(k); found = true; break;
                    }
                    if (!found) break;
                }
                for (int k = 1; k + 1 < idx.Count; k++) { o.sub[0].Add(b0 + idx[0]); o.sub[0].Add(b0 + idx[k]); o.sub[0].Add(b0 + idx[k + 1]); tris++; }
            }
            return tris;
        }

        /// <summary>A loop's area over the ground plane (root space, m2).</summary>
        static float LegsLoopArea(List<OzMeshSlice.V> loop, Matrix4x4 meshToRoot)
        {
            float a = 0f; int n = loop.Count;
            for (int k = 0; k < n; k++) { var p = meshToRoot.MultiplyPoint3x4(loop[k].p); var q = meshToRoot.MultiplyPoint3x4(loop[(k + 1) % n].p); a += p.x * q.z - q.x * p.z; }
            return Mathf.Abs(a) * 0.5f;
        }

        /// <summary>The pod's inner sleeve on the stub: a wall over each cut loop, from the cut up the leg's axis (root space) by h, every corner
        /// inset toward the cut's middle c (level) by inset, facing out (Unity's front faces: cross(b - a, c - a) points out, in the mesh's
        /// own space), on the texel uv, in the stub's first sub-mesh. Returns its triangles.</summary>
        static int LegsSleeve(OzMeshSlice.Part o, List<List<OzMeshSlice.V>> loops, Matrix4x4 meshToRoot, Vector3 c, Vector3 axis, float h, float inset, Vector2 uv)
        {
            var rootToMesh = meshToRoot.inverse; var cm = rootToMesh.MultiplyPoint3x4(c); var um = rootToMesh.MultiplyVector(axis).normalized; int tris = 0;
            foreach (var loop in loops)
            {
                int n = loop.Count; var B = new Vector3[n];
                for (int k = 0; k < n; k++)
                {
                    var p = meshToRoot.MultiplyPoint3x4(loop[k].p); var d = p - c; d.y = 0f;
                    B[k] = rootToMesh.MultiplyPoint3x4(p - d.normalized * inset);
                }
                var up = rootToMesh.MultiplyVector(axis * h);
                for (int k = 0; k < n; k++)
                {
                    Vector3 b0 = B[k], b1 = B[(k + 1) % n], t0 = b0 + up, t1 = b1 + up;
                    var nrm = Vector3.Cross(b1 - b0, t1 - b0); if (nrm.sqrMagnitude < 1e-16f) continue;
                    nrm.Normalize(); var m = 0.5f * (b0 + b1) - cm; var outw = m - um * Vector3.Dot(m, um);
                    bool flip = Vector3.Dot(nrm, outw) < 0f; if (flip) nrm = -nrm;
                    var tan = (b1 - b0).normalized; var t4 = new Vector4(tan.x, tan.y, tan.z, 1f);
                    int v0 = o.v.Count;
                    foreach (var q in new[] { b0, b1, t1, t0 }) { var v = loop[k]; v.p = q; v.n = nrm; v.u0 = uv; v.t = t4; o.v.Add(v); }
                    if (!flip) { o.sub[0].Add(v0); o.sub[0].Add(v0 + 1); o.sub[0].Add(v0 + 2); o.sub[0].Add(v0); o.sub[0].Add(v0 + 2); o.sub[0].Add(v0 + 3); }
                    else { o.sub[0].Add(v0); o.sub[0].Add(v0 + 2); o.sub[0].Add(v0 + 1); o.sub[0].Add(v0); o.sub[0].Add(v0 + 3); o.sub[0].Add(v0 + 2); }
                    tris += 2;
                }
            }
            return tris;
        }

        static float Cross2(Vector2 o, Vector2 a, Vector2 b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);

        void LegsFitFail(int i, string why)
        {
            legsOk = false; legsRig = null; legsPh = 0f; LegsFitFails++;
            Debug.LogWarning("[OpenZ] shuttle #" + (Obj != null ? Obj.Id : -1) + ": the legs cannot fold (" + LegNames[i] + ": " + why + "): they stay as the suspension draws them");
        }

        // ------------------------------------------------------------------ Pose(), after the hull's suspension, before the legs
        /// <summary>Once a frame before the legs are placed: the fold's progress from the rules' state. feet = the pads' height over the
        /// ground (or the deck) as the suspension has them.</summary>
        void LegsFoldStep(float dt, ZShuttleState st, float feet)
        {
            if (!LegsFoldOn || !legsOk || susRig == null) { legsPh = 0f; legsDir = 0; return; }
            if (legsRig == null) { LegsFit(); if (!legsOk) return; }
            if (!legsInit)
            {   // a ship drawn in the air (the testing panel): folded, without a sound
                legsInit = true; legsPh = Obj != null && Obj.ShuttleAirborne && st != ZShuttleState.Descend && feet >= LegsFoldFeetM ? 1f : 0f;
            }
            if (st == ZShuttleState.Climb && lastState == ZShuttleState.Landed) legsLiftT = 0f; else if (legsLiftT >= 0f) legsLiftT += dt;
            float tau = st == ZShuttleState.Descend ? SusLift() * (float)ZWorld.ShuttleDescendSec : -1f;
            bool rough = st == ZShuttleState.Descend && roughArmed, want; float rate;
            if ((st == ZShuttleState.Climb || st == ZShuttleState.Cruise || st == ZShuttleState.Hover) && !crashOn) { want = feet >= LegsFoldFeetM || legsPh > 0f; rate = 1f / LegsFoldSec; }
            else if (st == ZShuttleState.Descend)
            {
                if (rough) { want = false; rate = 1f / LegsUnfoldRoughSec; }
                else if (tau <= LegsUnfoldAtSec) { want = false; rate = 1f / LegsUnfoldSec; }
                else { want = legsPh > 0f && legsDir >= 0; rate = 1f / (want ? LegsFoldSec : LegsUnfoldSec); }
            }
            else { want = false; rate = 1f / LegsUnfoldLandedSec; }
            float target = want ? 1f : 0f;
            if (target != legsPh)
            {
                int d = target > legsPh ? 1 : -1;
                if (d != legsDir) LegsBegin(d);
                legsPh = d > 0 ? Mathf.Min(1f, legsPh + rate * dt) : Mathf.Max(0f, legsPh - rate * dt);
            }
            if (st == ZShuttleState.Descend && !rough)
            {   // the guard: down and locked before the touchdown, whatever happened before
                float cap = Mathf.Max(0f, (tau - LegsLockBeforeSec) / LegsUnfoldSec);
                if (legsPh > cap + 1e-4f) { if (legsDir != -1) { LegsBegin(-1); LegsGuardN++; } legsPh = cap; }
            }
            if (legsDir != 0 && (legsPh >= 1f || legsPh <= 0f))
            {
                bool stowed = legsPh >= 1f;
                LegsSound("shuttle_leg_thud", stowed ? LegsClunkStowVol : LegsClunkLockVol, stowed ? LegsClunkStowPitch : LegsClunkLockPitch); LegsClunks++;
                if (stowed) { LegsStowedN++; LegsStowedAfterLiftSec = legsLiftT; }
                else { LegsLockedN++; LegsLockedBeforeTouchSec = tau; }
                legsDir = 0;
            }
        }

        void LegsBegin(int d)
        {
            legsDir = d;
            if (d > 0) LegsFolds++; else LegsUnfolds++;
            LegsSound("shuttle_leg_retract", LegsWhineVol, d > 0 ? LegsWhinePitchFold : LegsWhinePitchUnfold); LegsWhines++;
        }

        void LegsSound(string id, float vol, float pitch)
        {
            if (!AudioDirector.Has(id)) return;
            AudioDirector.Fire(id, transform.position + Vector3.up * 0.6f, Obj != null ? Obj.Id : -1, vol, pitch);
            LastSound = id;
        }

        // ------------------------------------------------------------------ the legs (after the suspension placed leg i)
        /// <summary>Leg i, after SuspLeg: the folding part follows the strut, then the whole leg (folding part, piston, foot) swings about the
        /// hinge and the foot tucks at the ankle, both about the level axis across the fold as the strut carries it now.</summary>
        void LegsFoldLeg(int i)
        {
            legsHinge[i] = 0f; legsAnkle[i] = 0f; legsPadIn[i] = 0f; legsPadTilt[i] = 0f; legsPadY[i] = 0f;
            if (!legsOk || susRig == null || legsRig == null) return;
            var L = susRig[i]; var F = legsRig[i]; if (L == null || !L.leg || !L.strut || F == null || !F.fold || !F.tip) return;
            F.fold.localPosition = L.strut.localPosition; F.fold.localRotation = L.strut.localRotation;
            if (F.foldR.enabled != L.strutR.enabled) F.foldR.enabled = L.strutR.enabled;
            if (F.tipR.enabled != L.strutR.enabled) F.tipR.enabled = L.strutR.enabled;
            legsMpb ??= new MaterialPropertyBlock();
            for (int s = 0; s < L.subs; s++) { L.strutR.GetPropertyBlock(legsMpb, s); F.foldR.SetPropertyBlock(legsMpb, s); F.tipR.SetPropertyBlock(legsMpb, s); }   // the leg's paint and accent band
            float kh = LegsFoldOn ? LegsEase((legsPh - LegsHingeFrom) / (1f - LegsHingeFrom)) : 0f, ka = LegsFoldOn ? LegsEase((legsPh - LegsAnkleFrom) / (1f - LegsAnkleFrom)) : 0f;
            float th = LegsHingeDeg[i] * kh, fa = LegsAnkleDeg[i] * ka;
            if (th != 0f || fa != 0f)
            {
                var sm = transform.worldToLocalMatrix * L.strut.localToWorldMatrix * LegsRest(L, L.strut).inverse;          // the strut's motion now (root space)
                var pn = sm.MultiplyPoint3x4(F.pivot); var kw = sm.MultiplyVector(F.axis).normalized;
                var rk = Quaternion.AngleAxis(th, kw);
                LegsTurn(F.fold, pn, rk); LegsTurn(L.leg, pn, rk); if (L.piston) LegsTurn(L.piston, pn, rk);
                if (fa != 0f)
                {
                    var fm = transform.worldToLocalMatrix * L.leg.localToWorldMatrix * LegsRest(L, L.leg).inverse;          // the foot's motion after the hinge
                    LegsTurn(L.leg, fm.MultiplyPoint3x4(F.ankleHinge), Quaternion.AngleAxis(fa, kw));
                }
            }
            legsHinge[i] = th; legsAnkle[i] = fa;
            var footM = transform.worldToLocalMatrix * L.leg.localToWorldMatrix * LegsRest(L, L.leg).inverse;              // the foot's motion now (root space)
            if (!LegsFoldOn || legsPh <= LegsTipSwitch) { F.tip.localPosition = F.fold.localPosition; F.tip.localRotation = F.fold.localRotation; }   // the rod's tip on the rod
            else SusPlace(F.tip, L.restPos, L.restRot, footM.rotation, footM.GetColumn(3));                                   // on the foot (the same pose at the switch)
            var pad = footM.MultiplyPoint3x4(new Vector3(PadSoles[i].x, 0f, PadSoles[i].z));
            var c = new Vector2(LegsFoldCentre.x, LegsFoldCentre.z);
            legsPadIn[i] = (new Vector2(PadSoles[i].x, PadSoles[i].z) - c).magnitude - (new Vector2(pad.x, pad.z) - c).magnitude;
            legsPadY[i] = pad.y;
            legsPadTilt[i] = Vector3.Angle(footM.MultiplyVector(Vector3.down), Vector3.down);
        }

        /// <summary>Turns a part rigidly by the root-space rotation r about the root-space point p.</summary>
        void LegsTurn(Transform part, Vector3 p, Quaternion r)
        {
            var pw = transform.TransformPoint(p); var rot = transform.rotation; var rw = rot * r * Quaternion.Inverse(rot);
            part.SetPositionAndRotation(pw + rw * (part.position - pw), rw * part.rotation);
        }

        /// <summary>OzShuttleBreakup: a wreck piece WR_Leg_xx starts from the pose its leg is in (the root-space motion of the folding part
        /// from its parked pose: the strut's motion and the hinge's swing); identity for every other piece, or without the rig.</summary>
        public Matrix4x4 LegsBreakFrom(string piece)
        {
            if (susRig == null || string.IsNullOrEmpty(piece) || !piece.StartsWith("WR_Leg_")) return Matrix4x4.identity;
            int i = System.Array.IndexOf(LegNames, "SH_Leg_" + piece.Substring(7)); if (i < 0) return Matrix4x4.identity;
            var L = susRig[i]; if (L == null || !L.strut || !L.strut.parent) return Matrix4x4.identity;
            var part = legsRig != null && legsRig[i] != null && legsRig[i].fold ? legsRig[i].fold : L.strut;
            return transform.worldToLocalMatrix * part.localToWorldMatrix * LegsRest(L, L.strut).inverse;
        }
    }
}
