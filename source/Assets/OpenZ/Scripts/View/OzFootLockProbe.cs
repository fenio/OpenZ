using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Foot lock v2 Play check (item 203.3; made only by the menu OpenZ/Legs/Foot lock v2: Play check). Two halves of `seconds`
    /// each: RobotLegIK.FootLockV2 OFF (the stance lock as before), then ON; the switch is put back as it was at the end. With
    /// `drive`, up to six on-screen player squads walk 10 m out in the first half and back in the second (as the legs gate does).
    /// Per BS-01 robot and half, from the drawn bones (LateUpdate, after the IK): the planted boot's slide = the foot bone's path
    /// over the ground while its ankle is within 2 cm (x the robot's leg size) of its rest height, per stance (cm, mean / max);
    /// down foot-frames and the share fully locked (lock weight 1); forced releases (reach / clip ran away), pins moved toward the
    /// hip (v2); the pelvis drop (mean, cm); the legs gate's jerk (the ankle in the robot's frame, velocity change per frame scaled to
    /// PASS: every robot (every kind: the ProcSwingSkip kinds tough, sniper and electrobot are phase-locked by v2 too) with at least
    /// 3 stances in the ON half slides at most 12 cm x its leg size per stance with the switch ON, at most 40 % of its OFF slide when
    /// the OFF half has 3 stances too (a boot that never leaves the ground, or never reaches it, has none: then ON alone is judged and
    /// the row says so), and its ON jerk stays within 8 cm. Also per half: the boots' path over the ground while down, cm per second
    /// (counts a boot that glides without ever lifting).
    /// </summary>
    [DefaultExecutionOrder(80)]   // after UnitView (60) and RobotLegIK (70): reads the pose as drawn
    public sealed class OzFootLockProbe : MonoBehaviour
    {
        public static string Status = "idle";
        public static string LastResult = "";
        float seconds; bool drive; float t0; int half = -1; bool wasOn;
        int v2Drags0, v2Torn0; double v2DragCm0;                                                       // RobotLegIK's static totals when the ON half starts
        readonly List<(ZObject o, int x, int y)> starts = new List<(ZObject, int, int)>();
        readonly StringBuilder csv = new StringBuilder();

        sealed class Half
        {
            public readonly List<float> stances = new List<float>(); public int down, lockedDown, frames, forcedReach, forcedDist, drags;
            public double drop, path, secs; public float jerkMax;
        }
        sealed class R
        {
            public UnitView v; public Animator a; public RobotLegIK ik; public string name; public bool skip;
            public readonly Transform[] foot = new Transform[2];
            public readonly float[] run = new float[2]; public readonly bool[] inRun = new bool[2]; public readonly Vector3[] last = new Vector3[2];
            public readonly List<Vector3>[] loc = { new List<Vector3>(), new List<Vector3>() }; public readonly List<float> dts = new List<float>();
            public readonly Half[] h = { new Half(), new Half() };
            public int fr0, fd0, dr0, lk0;
        }
        readonly List<R> rs = new List<R>();

        public static OzFootLockProbe Begin(float seconds = 8f, bool drive = true)
        {
            var g = ZGame.I;
            if (!g || g.World == null) { Status = "no running game"; Debug.LogWarning("[FootLockV2] " + Status); return null; }
            var c = new GameObject("OzFootLockProbe").AddComponent<OzFootLockProbe>();
            c.seconds = Mathf.Max(2f, seconds); c.drive = drive; Status = "running";
            return c;
        }

        void Start()
        {
            wasOn = RobotLegIK.FootLockV2;
            foreach (var v in ZGame.I.Views.Values)
            {
                if (!v || v.Obj == null || v.Obj.Kind != ZKind.Robot || v.Dying || !v.isActiveAndEnabled) continue;
                var a = v.GetComponentInChildren<Animator>(); if (!a || !a.isHuman) continue;
                var ik = a.GetComponent<RobotLegIK>(); if (!ik) continue;
                var r = new R { v = v, a = a, ik = ik, name = v.Obj.Id + " " + v.Obj.Type + (v.Obj.IsHero ? " (hero)" : ""), skip = RobotLegIK.ProcSwingSkip.Contains(v.Obj.Type) };
                r.foot[0] = a.GetBoneTransform(HumanBodyBones.LeftFoot); r.foot[1] = a.GetBoneTransform(HumanBodyBones.RightFoot);
                if (r.foot[0] && r.foot[1]) rs.Add(r);
            }
            csv.AppendLine("frame,t,half,id,type,state,footL_x,footL_y,footL_z,footR_x,footR_y,footR_z,lockL,lockR,drop,dt");
            StartHalf(0);
        }

        void StartHalf(int k)
        {
            half = k; t0 = Time.time;
            RobotLegIK.FootLockV2 = k == 1;
            if (k == 1) { v2Drags0 = RobotLegIK.V2Drags; v2Torn0 = RobotLegIK.V2Torn; v2DragCm0 = RobotLegIK.V2DragCm; }
            foreach (var r in rs)
            {
                r.fr0 = r.ik.ForcedReach[0] + r.ik.ForcedReach[1]; r.fd0 = r.ik.ForcedDist[0] + r.ik.ForcedDist[1]; r.dr0 = r.ik.DragFrames;
                for (int i = 0; i < 2; i++) { r.inRun[i] = false; r.run[i] = 0f; r.loc[i].Clear(); }
                r.dts.Clear();
            }
            if (drive) Drive(k == 1);
            Debug.Log("[FootLockV2] check half " + (k + 1) + ": switch " + (k == 1 ? "ON" : "OFF") + ", " + rs.Count + " BS-01 robots");
        }

        void Drive(bool home)
        {
            var g = ZGame.I; var cam = Camera.main;
            if (home) { foreach (var (o, x, y) in starts) if (!o.Destroyed) g.World.SendWaypoints(g.player, o, new[] { ZWaypoint.Move(x, y) }); return; }
            int n = 0;
            foreach (var r in rs.OrderBy(q => cam ? (q.v.transform.position - cam.transform.position).sqrMagnitude : 0f))
            {
                var o = r.v.Obj; if (o.IsMinion || o.Owner != g.player || n >= 6) continue;
                if (cam) { var vp = cam.WorldToViewportPoint(r.v.transform.position); if (vp.z < 0 || vp.x < 0.05f || vp.x > 0.95f || vp.y < 0.05f || vp.y > 0.95f) continue; }
                int dir = o.Id % 2 == 0 ? 1 : -1, cx = o.LocX + o.WidthPix / 2, cy = o.LocY + o.HeightPix / 2;
                starts.Add((o, cx, cy));
                g.World.SendWaypoints(g.player, o, new[] { ZWaypoint.Move(cx + dir * 80, cy) });
                n++;
            }
        }

        void LateUpdate()
        {
            if (half < 0) return;
            var g = ZGame.I; if (!g) { Finish(); return; }
            float dt = Mathf.Max(1e-4f, Time.deltaTime);
            foreach (var r in rs)
            {
                if (!r.v || r.v.Dying || r.v.Obj == null || r.v.Obj.Destroyed || !r.a) continue;
                var H = r.h[half]; H.frames++; H.drop += r.ik.PelvisDropNow;
                var root = r.v.transform; var inv = Quaternion.Inverse(root.rotation); float sc = r.ik.LegScale;
                var st = r.a.GetCurrentAnimatorStateInfo(0);
                string sn = st.IsName("walking") ? "walking" : st.IsName("run_forward") ? "run_forward" : st.IsName("run_backward") ? "run_backward" : st.IsName("idle") ? "idle" : "other";
                r.dts.Add(dt);
                if (sn != "idle" && sn != "other") H.secs += dt;
                for (int i = 0; i < 2; i++)
                {
                    var f = r.foot[i].position; float rest = r.ik.AnkleRest(i);
                    bool down = !float.IsNaN(rest) && f.y - g.GroundY(f.x, f.z) <= rest + 0.02f * sc && sn != "idle" && sn != "other";
                    if (down)
                    {
                        H.down++; if (r.ik.LockWeight(i) >= 0.999f) H.lockedDown++;
                        if (r.inRun[i]) { float step = new Vector2(f.x - r.last[i].x, f.z - r.last[i].z).magnitude; r.run[i] += step; H.path += step; }
                        r.inRun[i] = true;
                    }
                    else if (r.inRun[i]) { H.stances.Add(r.run[i] * 100f); r.run[i] = 0f; r.inRun[i] = false; }
                    r.last[i] = f;
                    var lp = inv * (f - root.position); var L = r.loc[i]; L.Add(lp);
                    int n = L.Count;
                    if (n >= 3 && sn != "idle")
                    {
                        float j = ((L[n - 1] - L[n - 2]) / r.dts[n - 1] - (L[n - 2] - L[n - 3]) / r.dts[n - 2]).magnitude / 60f * 100f / Mathf.Max(1f, r.a.speed * r.a.speed);
                        if (j > H.jerkMax) H.jerkMax = j;
                    }
                }
                csv.Append(Time.frameCount).Append(',').Append(F(Time.time - t0)).Append(',').Append(half).Append(',').Append(r.v.Obj.Id).Append(',').Append(r.v.Obj.Type).Append(',').Append(sn);
                for (int i = 0; i < 2; i++) { var f = r.foot[i].position; csv.Append(',').Append(F(f.x)).Append(',').Append(F(f.y)).Append(',').Append(F(f.z)); }
                csv.Append(',').Append(F(r.ik.LockWeight(0))).Append(',').Append(F(r.ik.LockWeight(1))).Append(',').Append(F(r.ik.PelvisDropNow)).Append(',').Append(F(dt)).Append('\n');
            }
            if (Time.time - t0 < seconds) return;
            foreach (var r in rs)
            {
                var H = r.h[half];
                H.forcedReach = r.ik.ForcedReach[0] + r.ik.ForcedReach[1] - r.fr0; H.forcedDist = r.ik.ForcedDist[0] + r.ik.ForcedDist[1] - r.fd0; H.drags = r.ik.DragFrames - r.dr0;
            }
            if (half == 0) StartHalf(1); else Finish();
        }

        static string F(float x) => x.ToString("0.0000", CultureInfo.InvariantCulture);

        void Finish()
        {
            RobotLegIK.FootLockV2 = wasOn; half = -1;
            var sb = new StringBuilder(); var fails = new List<string>(); int judged = 0;
            sb.AppendLine("| robot | kind | OFF: stances, slide cm mean / max | OFF: over the ground while down cm/s | OFF: down frames locked | OFF: forced reach / ran away | OFF: drop cm | OFF: jerk max | ON: stances, slide cm mean / max | ON: over the ground while down cm/s | ON: down frames locked | ON: forced reach / ran away | ON: pins moved | ON: drop cm | ON: jerk max | verdict |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
            foreach (var r in rs)
            {
                var a = r.h[0]; var b = r.h[1]; float sc = r.ik ? r.ik.LegScale : 1f;
                float ma = a.stances.Count > 0 ? a.stances.Average() : 0f, mb = b.stances.Count > 0 ? b.stances.Average() : 0f;
                string verdict = "-";
                if (b.stances.Count >= 3)
                {
                    bool offToo = a.stances.Count >= 3;
                    judged++; var why = new List<string>();
                    if (mb > 12f * sc) why.Add($"ON slide {mb:0.0} cm > {12f * sc:0.0}");
                    if (offToo && mb > 0.4f * ma) why.Add($"ON slide {mb:0.0} cm > 40 % of OFF {ma:0.0}");
                    if (b.jerkMax > 8f) why.Add($"ON jerk {b.jerkMax:0.0} cm");
                    verdict = (why.Count == 0 ? "ok" : "FAIL: " + string.Join(", ", why)) + (offToo ? "" : " (OFF: under 3 stances, the boots never left or never reached the ground: ON judged alone)");
                    if (why.Count > 0) fails.Add(r.name + " " + string.Join(", ", why));
                }
                string S(Half h, float m) => h.stances.Count + ", " + m.ToString("0.0", CultureInfo.InvariantCulture) + " / " + (h.stances.Count > 0 ? h.stances.Max() : 0f).ToString("0.0", CultureInfo.InvariantCulture);
                string Lk(Half h) => h.lockedDown + " of " + h.down;
                string D(Half h) => (h.frames > 0 ? h.drop / h.frames * 100.0 : 0.0).ToString("0.0", CultureInfo.InvariantCulture);
                string G(Half h) => (h.secs > 0.0 ? h.path * 100.0 / h.secs : 0.0).ToString("0.0", CultureInfo.InvariantCulture);
                sb.AppendLine($"| {r.name} | {(r.skip ? "own clip (phase lock with v2 only)" : "phase lock")} | {S(a, ma)} | {G(a)} | {Lk(a)} | {a.forcedReach} / {a.forcedDist} | {D(a)} | {a.jerkMax:0.0} | {S(b, mb)} | {G(b)} | {Lk(b)} | {b.forcedReach} / {b.forcedDist} | {b.drags} | {D(b)} | {b.jerkMax:0.0} | {verdict} |");
            }
            string result = judged == 0 ? "RESULT: NO DATA (no robot walked 3 stances in the ON half: walk squads on screen, or use drive)"
                          : fails.Count == 0 ? $"RESULT: PASS ({judged} robots judged)" : "RESULT: FAIL: " + string.Join("; ", fails);
            var head = new StringBuilder();
            head.AppendLine(result).AppendLine();
            head.AppendLine($"# Foot lock v2 Play check: {seconds:0} s switch OFF, then {seconds:0} s ON; drive {drive}; Time.timeScale {Time.timeScale}; Application.targetFrameRate {Application.targetFrameRate}; level {(ZGame.I ? ZGame.I.level : "-")}");
            head.AppendLine($"Slide = the foot bone's path over the ground while its ankle is within 2 cm x leg size of its rest height, per stance; 'over the ground while down' = the same path per second of walking / running (it also counts a boot that never lifts). All robots, ON half only: pins moved toward the hip {RobotLegIK.V2Drags - v2Drags0} frames ({RobotLegIK.V2DragCm - v2DragCm0:0.0} cm in all), torn by a shove {RobotLegIK.V2Torn - v2Torn0}. Switch now: {(RobotLegIK.FootLockV2 ? "ON" : "OFF")} (as before the check).");
            head.AppendLine();
            string dir = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../../../../work/records"));
            try
            {
                System.IO.Directory.CreateDirectory(dir);
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "foot_lock_v2_check.md"), head.ToString() + sb.ToString());
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "foot_lock_v2_trace.csv"), csv.ToString());
            }
            catch (System.Exception e) { Debug.LogWarning("[FootLockV2] could not write the report: " + e.Message); }
            LastResult = result; Status = "done";
            Debug.Log("[FootLockV2] " + result + " (foot_lock_v2_check.md)");
            Destroy(gameObject);
        }
    }
}
