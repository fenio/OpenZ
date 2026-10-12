using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// after every view script ran (order 1003: after UnitView 60, UnitAim 62, OpenZWading 70, WeaponHold 80, BS01TypeKit 85
    /// and the probes 1000-1002), a per-frame trace of every on-screen BS-01 robot: ankle and toe height above the walkable
    /// ground, both feet in the robot's own frame, the knee's offset in front of the hip-ankle line, the base-layer state,
    /// anim.speed and the RobotLegIK guard count. With drive = true it walks up to four on-screen player squads 5 tiles out and
    /// back (idle -> walk -> turn -> stop). Foot jerk = FootJitterProbe's metric (velocity change per frame, scaled to a 60 fps
    /// frame, cm) divided by max(1, anim.speed)^2 so a fast playback is not a pop; hitch frames are skipped.
    /// PASS (DIAGNOSIS.md section 6): walking <= 4 cm with <= 2 % of its frames over 3 cm; runs / strafes / back-pedal <= 7 cm;
    /// any frame incl. every cross-fade <= 8 cm; the per-frame ankle height trace (world y) jerks <= 2.5 cm while walking and
    /// ground and left / right within 2.5 cm; knees never flip behind the hip-ankle line (> 2 cm); the feet really stride
    /// (>= 20 cm in the body frame); <= 2 base-layer state changes per robot per second; >= 3 robots with >= 90 steady gait
    /// Start: menu OpenZ/Checks/Legs gate: Play, or LegJitterGate.Begin(8f, true). A/B: RobotLegIK.On = false first.
    /// </summary>
    [DefaultExecutionOrder(1003)]
    public class LegJitterGate : MonoBehaviour
    {
        public const float JerkWalkCm = 4f, JerkGaitCm = 7f, JerkAnyCm = 8f, Over3Share = 0.02f, HeightJerkWalkCm = 2.5f, HeightJerkAnyCm = 5f,
                           StanceBandCm = 4f, StanceAsymCm = 2.5f, KneeFlipCm = 2f, MinStrideCm = 20f, MaxStateChangesPerS = 2f;
        public const int MinGaitRobots = 3, MinGaitFrames = 90;
        public static string Status = "idle", Result = "";
        public float duration = 8f; public bool drive = true;

        sealed class R
        {
            public UnitView v; public Animator a; public SkinnedMeshRenderer skin; public RobotLegIK ik;
            public readonly Transform[] foot = new Transform[2], toe = new Transform[2], knee = new Transform[2], hip = new Transform[2];
            public readonly float[] toeRest = new float[2];
            public readonly List<Vector3>[] local = { new List<Vector3>(), new List<Vector3>() }, world = { new List<Vector3>(), new List<Vector3>() };
            public readonly List<float>[] toeH = { new List<float>(), new List<float>() }, kneeF = { new List<float>(), new List<float>() };
            public readonly List<float> dt = new List<float>(), speed = new List<float>(), groundOff = new List<float>();
            public readonly List<bool> trans = new List<bool>(), vis = new List<bool>();
            public readonly List<string> state = new List<string>();
            public int changes; string last;
            public void Changed(string s) { if (last != null && s != last) changes++; last = s; }
        }
        readonly List<R> rs = new List<R>();
        readonly StringBuilder csv = new StringBuilder("frame,t,dt,id,type,state,trans,vis,anim_speed,root_y,ground_y,ankleL_h,ankleR_h,toeL_h,toeR_h,lx,ly,lz,rx,ry,rz,kneeL_fwd,kneeR_fwd,ik_guarded,wlx,wlz,wrx,wrz,lockL,lockR,phase,liftL,liftR\n");
        float t0; bool back; readonly List<(ZObject o, int x, int y)> starts = new List<(ZObject, int, int)>();
        static readonly string[] Names = { "idle", "walking", "run_forward", "run_backward", "strafe_left", "strafe_right", "shooting", "bazooka_idle", "bazooka_fire", "grenade_throw" };

        /// <summary></summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoStart()
        {
            if (!Application.isEditor || PlayerPrefs.GetInt("oz_autogate", 0) != 1) return;
            var go = new GameObject("LegGateAuto"); go.AddComponent<AutoRunner>();
        }
        sealed class AutoRunner : MonoBehaviour
        {
            float t;
            void Update() { if (ZGame.I == null) return; t += Time.deltaTime; if (t > 4f) { Begin(8f, true); Destroy(gameObject); } }
        }

        public static LegJitterGate Begin(float seconds = 8f, bool drive = true)
        {
            var g = ZGame.I;
            if (!g || g.World == null) { Status = "no running ZGame"; Debug.LogWarning("[LegJitterGate] " + Status); return null; }
            var c = new GameObject("LegJitterGate").AddComponent<LegJitterGate>();
            c.duration = seconds; c.drive = drive; Status = "running";
            return c;
        }

        static string StateName(Animator a)
        {
            var i = a.GetCurrentAnimatorStateInfo(0);
            foreach (var s in Names) if (i.IsName(s)) return s;
            return i.shortNameHash.ToString(CultureInfo.InvariantCulture);
        }
        static bool IsGait(string s) => s == "walking" || s == "run_forward" || s == "run_backward" || s == "strafe_left" || s == "strafe_right";

        void Start()
        {
            t0 = Time.time;
            foreach (var v in ZGame.I.Views.Values)
            {
                if (!v || v.Obj == null || v.Obj.Kind != ZKind.Robot || v.Dying || !v.isActiveAndEnabled) continue;
                var a = v.GetComponentInChildren<Animator>(); if (!a || !a.isHuman) continue;          // BS-01 only (the laser is Generic)
                var r = new R { v = v, a = a, skin = v.GetComponentInChildren<SkinnedMeshRenderer>(), ik = a.GetComponent<RobotLegIK>() };
                var hb = new[] { (HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftUpperLeg),
                                 (HumanBodyBones.RightFoot, HumanBodyBones.RightToes, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightUpperLeg) };
                for (int i = 0; i < 2; i++)
                {
                    r.foot[i] = a.GetBoneTransform(hb[i].Item1); r.toe[i] = a.GetBoneTransform(hb[i].Item2);
                    r.knee[i] = a.GetBoneTransform(hb[i].Item3); r.hip[i] = a.GetBoneTransform(hb[i].Item4);
                    r.toeRest[i] = RestHeight(r, r.toe[i] ? r.toe[i] : r.foot[i]);
                }
                if (r.foot[0] && r.foot[1] && r.knee[0] && r.knee[1]) rs.Add(r);
            }
            if (drive) Drive(false);
            Debug.Log($"[LegJitterGate] {rs.Count} BS-01 robots, {duration} s, drive {drive}, RobotLegIK {(RobotLegIK.On ? "on" : "OFF")} / knee hints {(RobotLegIK.KneeHints ? "on" : "OFF")}");
        }

        /// <summary>Bind-pose height of a bone above the robot's root (the soles' plane), from the skin's bind pose (like BS01TypeKit).</summary>
        static float RestHeight(R r, Transform bone)
        {
            var smr = r.skin;
            if (smr && smr.sharedMesh && bone)
            {
                int i = System.Array.IndexOf(smr.bones, bone); var bp = smr.sharedMesh.bindposes;
                if (i >= 0 && i < bp.Length)
                {
                    Vector3 w = (smr.transform.localToWorldMatrix * bp[i].inverse).GetColumn(3);
                    return (Quaternion.Inverse(r.v.transform.rotation) * (w - r.v.transform.position)).y;
                }
            }
            return float.NaN;
        }

        /// <summary>Walks up to four on-screen player squads 5 tiles out, then back to where they stood (start, turn, stop).</summary>
        void Drive(bool home)
        {
            var g = ZGame.I; var cam = Camera.main; int n = 0;
            if (home) { foreach (var (o, x, y) in starts) if (!o.Destroyed) g.World.SendWaypoints(g.player, o, new[] { ZWaypoint.Move(x, y) }); return; }
            foreach (var r in rs.OrderBy(q => cam ? (q.v.transform.position - cam.transform.position).sqrMagnitude : 0f))
            {
                var o = r.v.Obj; if (o.IsMinion || o.Owner != g.player || n >= 4) continue;
                if (cam) { var vp = cam.WorldToViewportPoint(r.v.transform.position); if (vp.z < 0 || vp.x < 0.1f || vp.x > 0.9f || vp.y < 0.1f || vp.y > 0.9f) continue; }
                int dir = o.Id % 2 == 0 ? 1 : -1, cx = o.LocX + o.WidthPix / 2, cy = o.LocY + o.HeightPix / 2;
                starts.Add((o, cx, cy));
                g.World.SendWaypoints(g.player, o, new[] { ZWaypoint.Move(cx + dir * 80, cy + ((o.Id / 2) % 2 == 0 ? 24 : -24)) });
                n++;
            }
        }

        void LateUpdate()
        {
            float now = Time.time - t0;
            if (drive && !back && now > duration * 0.55f) { back = true; Drive(true); }
            var g = ZGame.I; float dt = Time.deltaTime;
            foreach (var r in rs)
            {
                if (!r.v || r.v.Dying || r.v.Obj == null || r.v.Obj.Destroyed || !r.a) continue;
                var root = r.v.transform; var inv = Quaternion.Inverse(root.rotation);
                string st = StateName(r.a); bool tr = r.a.IsInTransition(0); r.Changed(st);
                var fwd = -root.forward; fwd.y = 0f; fwd = fwd.sqrMagnitude > 1e-6f ? fwd.normalized : Vector3.forward;   // robots face their -z
                float gy = g.GroundY(root.position.x, root.position.z);
                bool vis = r.skin && r.skin.isVisible;
                r.dt.Add(Mathf.Max(1e-4f, dt)); r.speed.Add(r.a.speed); r.state.Add(st); r.trans.Add(tr); r.vis.Add(vis); r.groundOff.Add(root.position.y - gy);
                var ank = new float[2]; var toeh = new float[2]; var kf = new float[2]; var lp = new Vector3[2];
                for (int i = 0; i < 2; i++)
                {
                    var fp = r.foot[i].position; var tp = r.toe[i] ? r.toe[i].position : fp;
                    ank[i] = fp.y - g.GroundY(fp.x, fp.z); toeh[i] = tp.y - g.GroundY(tp.x, tp.z);
                    lp[i] = inv * (fp - root.position);
                    var hipP = r.hip[i] ? r.hip[i].position : root.position;
                    kf[i] = Vector3.Dot(r.knee[i].position - Vector3.Lerp(hipP, fp, 0.5f), fwd);
                    r.local[i].Add(lp[i]); r.world[i].Add(fp); r.toeH[i].Add(toeh[i]); r.kneeF[i].Add(kf[i]);
                }
                csv.Append(Time.frameCount).Append(',').Append(F(now)).Append(',').Append(F(dt)).Append(',').Append(r.v.Obj.Id).Append(',').Append(r.v.Obj.Type)
                   .Append(',').Append(st).Append(',').Append(tr ? 1 : 0).Append(',').Append(vis ? 1 : 0).Append(',').Append(F(r.a.speed))
                   .Append(',').Append(F(root.position.y)).Append(',').Append(F(gy))
                   .Append(',').Append(F(ank[0])).Append(',').Append(F(ank[1])).Append(',').Append(F(toeh[0])).Append(',').Append(F(toeh[1]));
                for (int i = 0; i < 2; i++) csv.Append(',').Append(F(lp[i].x)).Append(',').Append(F(lp[i].y)).Append(',').Append(F(lp[i].z));
                csv.Append(',').Append(F(kf[0])).Append(',').Append(F(kf[1])).Append(',').Append(r.ik ? r.ik.Guarded : -1);
                csv.Append(',').Append(F(r.foot[0].position.x)).Append(',').Append(F(r.foot[0].position.z)).Append(',').Append(F(r.foot[1].position.x)).Append(',').Append(F(r.foot[1].position.z));
                csv.Append(',').Append(F(r.ik ? r.ik.LockWeight(0) : -1f)).Append(',').Append(F(r.ik ? r.ik.LockWeight(1) : -1f)).Append(',').Append(F(Mathf.Repeat(r.a.GetCurrentAnimatorStateInfo(0).normalizedTime, 1f)));
                csv.Append(',').Append(r.ik && r.ik.LiftFrame[0] == Time.frameCount ? F(r.ik.LiftedTo[0]) : "").Append(',').Append(r.ik && r.ik.LiftFrame[1] == Time.frameCount ? F(r.ik.LiftedTo[1]) : "").Append('\n');
            }
            if (now >= duration) { Finish(); Destroy(gameObject); }
        }

        static string F(float x) => x.ToString("0.0000", CultureInfo.InvariantCulture);

        /// <summary>FootJitterProbe's jerk (velocity change between consecutive frames scaled to a 60 fps frame, cm), divided by the
        /// playback speed squared (a clip played 1.5x fast has 2.25x the jerk without any pop).</summary>
        static float Jerk(List<Vector3> x, List<float> dt, float speed, int i) =>
            ((x[i] - x[i - 1]) / dt[i] - (x[i - 1] - x[i - 2]) / dt[i - 1]).magnitude / 60f * 100f / Mathf.Max(1f, speed * speed);
        /// <summary>The same metric on the ankle's world height only (the foot-height trace).</summary>
        static float HeightJerk(List<Vector3> x, List<float> dt, float speed, int i) =>
            Mathf.Abs((x[i].y - x[i - 1].y) / dt[i] - (x[i - 1].y - x[i - 2].y) / dt[i - 1]) / 60f * 100f / Mathf.Max(1f, speed * speed);

        void Finish()
        {
            var sb = new StringBuilder(); int gaitRobots = 0; bool pass = true; var fails = new List<string>();
            sb.AppendLine($"# Legs gate (Play) {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}, level {ZGame.I.level}, {duration} s, drive {drive}");
            sb.AppendLine($"RobotLegIK {(RobotLegIK.On ? "on" : "OFF")}, knee hints {(RobotLegIK.KneeHints ? "on" : "OFF")}, RobotLegIK on {rs.Count(q => q.ik)} of {rs.Count} BS-01 robots, Time.timeScale {Time.timeScale}");
            sb.AppendLine($"Thresholds: walking jerk <= {JerkWalkCm} cm (<= {Over3Share:P0} over 3 cm), run / strafe / back-pedal <= {JerkGaitCm} cm, any frame <= {JerkAnyCm} cm, ankle height jerk walking <= {HeightJerkWalkCm} cm / any <= {HeightJerkAnyCm} cm, " +
                          $"stance toes +-{StanceBandCm} cm of bind, L/R <= {StanceAsymCm} cm, knee flips 0 (> {KneeFlipCm} cm), stride >= {MinStrideCm} cm, <= {MaxStateChangesPerS} state changes/s, " +
                          $">= {MinGaitRobots} robots with >= {MinGaitFrames} steady gait frames on screen\n");
            sb.AppendLine("| robot | type | frames | on screen | walk / gait frames | walk jerk max | walk >3 cm | gait jerk max | any jerk max | height jerk walk / any | stance toe L / R cm (bind) | knee flips | stride cm | changes/s | IK guard | verdict |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
            foreach (var r in rs)
            {
                int n = r.dt.Count; if (n < 30) continue;
                int seen = r.vis.Count(b => b); if (seen < 0.8f * n) continue;
                float med = r.dt.OrderBy(d => d).ElementAt(n / 2);
                bool Hitch(int i) => r.dt[i] > Mathf.Max(0.034f * Time.timeScale, 2f * med);
                bool Steady(int i) => !r.trans[i] && !r.trans[i - 1] && !r.trans[i - 2] && r.state[i] == r.state[i - 2] && r.state[i] == r.state[i - 1];
                float walkMax = 0f, gaitMax = 0f, anyMax = 0f, hWalk = 0f, hAny = 0f; int walkN = 0, gaitN = 0, over3 = 0;
                for (int i = 2; i < n; i++)
                {
                    if (Hitch(i) || Hitch(i - 1)) continue;
                    float j = Mathf.Max(Jerk(r.local[0], r.dt, r.speed[i], i), Jerk(r.local[1], r.dt, r.speed[i], i));
                    float h = Mathf.Max(HeightJerk(r.world[0], r.dt, r.speed[i], i), HeightJerk(r.world[1], r.dt, r.speed[i], i));
                    anyMax = Mathf.Max(anyMax, j); hAny = Mathf.Max(hAny, h);
                    if (!Steady(i) || !IsGait(r.state[i])) continue;
                    gaitN++;
                    if (r.state[i] == "walking") { walkN++; walkMax = Mathf.Max(walkMax, j); hWalk = Mathf.Max(hWalk, h); if (j > 3f) over3++; }
                    else gaitMax = Mathf.Max(gaitMax, j);
                }
                // stance (walking / running only: strafes may have an authored uneven stance) = the foot barely moves over the ground
                var stance = new[] { float.PositiveInfinity, float.PositiveInfinity }; var rest = new float[2]; int flips = 0; float stride = 0f;
                for (int f = 0; f < 2; f++)
                {
                    float zmin = float.PositiveInfinity, zmax = float.NegativeInfinity; int sign = 0;
                    for (int i = 1; i < n; i++)
                    {
                        var d = r.world[f][i] - r.world[f][i - 1]; d.y = 0f;
                        bool fwdGait = !r.trans[i] && (r.state[i] == "walking" || r.state[i] == "run_forward");
                        if (fwdGait && d.magnitude / r.dt[i] < 0.25f) stance[f] = Mathf.Min(stance[f], r.toeH[f][i]);
                        if (fwdGait) { zmin = Mathf.Min(zmin, r.local[f][i].z); zmax = Mathf.Max(zmax, r.local[f][i].z); }
                        float k = r.kneeF[f][i];
                        if (Mathf.Abs(k) > KneeFlipCm / 100f) { int s = k > 0 ? 1 : -1; if (sign != 0 && s != sign) flips++; sign = s; }
                    }
                    rest[f] = r.toeRest[f] + r.groundOff.Average();
                    if (zmax > zmin) stride = Mathf.Max(stride, (zmax - zmin) * 100f);
                }
                float changes = r.changes / duration;
                var why = new List<string>();
                if (walkN > 0 && walkMax > JerkWalkCm) why.Add($"walk jerk {walkMax:0.0}");
                if (walkN > 0 && over3 > Over3Share * walkN) why.Add($"{over3} walk frames > 3 cm");
                if (gaitMax > JerkGaitCm) why.Add($"gait jerk {gaitMax:0.0}");
                if (anyMax > JerkAnyCm) why.Add($"jerk {anyMax:0.0}");
                if (hWalk > HeightJerkWalkCm) why.Add($"walk height jerk {hWalk:0.0}");
                if (hAny > HeightJerkAnyCm) why.Add($"height jerk {hAny:0.0}");
                bool hasStance = !float.IsInfinity(stance[0]) && !float.IsInfinity(stance[1]);
                if (hasStance)
                {
                    for (int f = 0; f < 2; f++)
                        if (!float.IsNaN(rest[f]) && Mathf.Abs(stance[f] - rest[f]) * 100f > StanceBandCm) why.Add($"{(f == 0 ? "L" : "R")} stance {(stance[f] - rest[f]) * 100f:+0.0;-0.0} cm");
                    if (Mathf.Abs(stance[0] - stance[1]) * 100f > StanceAsymCm) why.Add($"L/R stance {Mathf.Abs(stance[0] - stance[1]) * 100f:0.0} cm");
                }
                if (flips > 0) why.Add($"{flips} knee flips");
                if (walkN >= MinGaitFrames / 2 && stride < MinStrideCm) why.Add($"feet frozen ({stride:0} cm stride)");
                if (changes > MaxStateChangesPerS) why.Add($"{changes:0.0} state changes/s");
                if (gaitN >= MinGaitFrames) gaitRobots++;
                string verdict = why.Count == 0 ? "ok" : "FAIL: " + string.Join(", ", why);
                if (why.Count > 0) { pass = false; fails.Add($"{r.v.Obj.Id} {string.Join(", ", why)}"); }
                string sl = hasStance ? $"{stance[0] * 100f:0.0} / {stance[1] * 100f:0.0} ({rest[0] * 100f:0.0} / {rest[1] * 100f:0.0})" : "-";
                sb.AppendLine($"| {r.v.Obj.Id} | {r.v.Obj.Type}{(r.v.Obj.IsMinion ? " m" : "")} | {n} | {seen} | {walkN} / {gaitN} | {walkMax:0.0} | {over3} | {gaitMax:0.0} | {anyMax:0.0} | {hWalk:0.0} / {hAny:0.0} | {sl} | {flips} | {stride:0} | {changes:0.00} | {(r.ik ? r.ik.Guarded.ToString() : "no IK")} | {verdict} |");
            }
            sb.AppendLine("\nStance lock per robot (RobotLegIK): " + string.Join(", ", rs.Where(q => q.ik && q.v && q.v.Obj != null).Select(q => $"{q.v.Obj.Id} locked {q.ik.LockedFrames} forced {q.ik.ForcedReleases} (dist L{q.ik.ForcedDist[0]}/R{q.ik.ForcedDist[1]}, reach L{q.ik.ForcedReach[0]}/R{q.ik.ForcedReach[1]})")));
            sb.AppendLine($"Reach ratio (hip-pin / leg length) while locked: L mean {(RobotLegIK.ReachRatioN[0] > 0 ? RobotLegIK.ReachRatioSum[0] / RobotLegIK.ReachRatioN[0] : 0):0.00} max {RobotLegIK.ReachRatioMax[0]:0.00}, R mean {(RobotLegIK.ReachRatioN[1] > 0 ? RobotLegIK.ReachRatioSum[1] / RobotLegIK.ReachRatioN[1] : 0):0.00} max {RobotLegIK.ReachRatioMax[1]:0.00}");
            if (gaitRobots < MinGaitRobots) { pass = false; fails.Add($"only {gaitRobots} robots walked on screen for {MinGaitFrames}+ steady frames (need {MinGaitRobots}): camera on player squads, re-run"); }
            Result = (pass ? "PASS" : "FAIL") + (fails.Count > 0 ? ": " + string.Join("; ", fails.Take(8)) : $" ({gaitRobots} walking robots)");
            sb.Insert(0, $"RESULT: {Result}\n\n");
#if UNITY_EDITOR
            string dir = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../../../../work/records"));
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "leg_gate.md"), sb.ToString());
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "leg_gate_trace.csv"), csv.ToString());
#endif
            Status = "done: " + Result;
            Debug.Log("[LegJitterGate] " + Result);
        }
    }
}
