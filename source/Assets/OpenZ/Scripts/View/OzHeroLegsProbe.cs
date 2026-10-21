using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Hero legs (item 209): the Play numbers, made only by the menu OpenZ/Legs/Hero legs: Play numbers. View only, read only: it looks at
    /// the drawn bones after every pass of the frame (LateUpdate order 2000) for `seconds`, on both heroes and on the grunts near them (the
    /// control rows, printed as REF, not judged). Run it once with the switch OFF (the baseline) and once ON; the header says which.
    /// Per hero it counts (the switch ON is judged):
    /// <list type="number">
    /// <item>thigh roll: the UpLeg's twist about its own long axis (relative to its first frame): range over any 1.6 s window of gait frames
    /// and the largest step per frame (wanted range &lt;= 12 deg, step &lt;= 2);</item>
    /// <item>foot heading: ankle to toe bone on the ground against the travel direction (the front when still), on straight-walk frames:
    /// within 14 +- 6 deg on 95 % or more, beyond 45 deg from 14 on none;</item>
    /// <item>planted slide: the ankle's ground speed while the component reports the foot planted (&lt;= 0.05 m/s on 95 % or more), the ground
    /// path per stance (mean and largest &lt;= 3 cm x the Model's scale), the largest foot step at a stance to gait entry (&lt;= 8 cm);</item>
    /// <item>soles: per stance the lowest toe-bone height over the ground against its rest height (+-2 cm on 95 % or more); the hips' height
    /// over the ground on gait frames against the bake's (+-3 cm);</item>
    /// <item>lying: the largest per-frame change of any written bone's local rotation after the first lying frame (0.00 deg), the toe bone's
    /// height over the ground against the bake's lie (+-2 cm), the hips' height against the bake's (+-3 cm), feet over the middle (0);</item>
    /// <item>crossing: feet over the middle (0) and the inner boot gap (ankle gap minus the boot widths) &gt;= 2 cm (Alan) / 10 cm (Brad) on
    /// walking and standing frames;</item>
    /// <item>drink / fire / grenade: foot and knee steps over 1 cm in the root's frame while those states play (0);</item>
    /// <item>lie-down / get-up: leg joint steps over 5 cm per frame (0); the step at the hand-over into the lie / into the stand (&lt;= 1 cm);</item>
    /// <item>knockdown / animated death (when one happens): the thigh roll range and step against the bake's own numbers for that program;</item>
    /// <item>boarding: frames written on a boarding clone (reported);</item>
    /// <item></item>
    /// <item>size: the drawn standing height of Alan over Brad, 0.90-0.98.</item>
    /// </list>
    /// </summary>
    [DefaultExecutionOrder(2000)]
    public sealed class OzHeroLegsProbe : MonoBehaviour
    {
        public static string Status = "idle", LastResult = "";
        public static float StraightSpeed = 0.3f, TwistWindow = 1.6f;
        float seconds, t0; bool drive, droveHome; readonly StringBuilder csv = new StringBuilder();
        readonly List<(ZObject o, int x, int y)> starts = new List<(ZObject, int, int)>();
        static readonly string[] Gaits = { "walking", "run_forward", "run_backward", "strafe_left", "strafe_right" };
        static readonly string[] Known = { "walking", "run_forward", "run_backward", "strafe_left", "strafe_right", "idle", "shooting", "grenade_throw", "drink", "crush", "toss",
            "grab_stand_L", "grab_stand_R", "grab_scoop_L", "grab_scoop_R", "sunbed_down", "sunbed_lie", "sunbed_up", "blast_airborne", "death_back", "death_side", "landing",
            "jump_takeoff", "jump_air", "climb_ladder", "seated_driver", "seated_gunner", "melee", "weapon_stow", "aim_up", "aim_down", "antic_turn", "antic_scan360", "antic_neck_stretch", "antic_phone",
            "fish_cast", "fish_hold", "fish_bite", "fish_reel" };
        static readonly Dictionary<int, string> hashes = new Dictionary<int, string>();

        sealed class R
        {
            public UnitView v; public Animator a; public OzHeroLegs legs; public RobotLegIK ik; public string name, member; public bool hero; public float sc;
            public Transform hips; public readonly Transform[] up = new Transform[2], knee = new Transform[2], foot = new Transform[2], toe = new Transform[2];
            public readonly Quaternion[] refUp = new Quaternion[2]; public readonly Vector3[] axisUp = new Vector3[2]; public bool refOk;
            public readonly List<(float t, float tw)>[] win = { new List<(float, float)>(), new List<(float, float)>() };
            public readonly float[] twPrev = new float[2]; public bool twPrevOk; public string prevState; public float prevYaw; public bool yawOk;
            public readonly Vector3[] footPrevW = new Vector3[2], prevFoot = new Vector3[2], prevKnee = new Vector3[2]; public Vector3 prevRoot; public bool locOk;
            public readonly bool[] inStance = new bool[2]; public readonly float[] path = new float[2], toeMin = new float[2];
            public readonly Quaternion[] lieQ = new Quaternion[16]; public bool lieQOk;
            // counts
            public int frames, gaitFrames, straightFrames, headIn, headOver, plantedFrames, plantedSlow, stanceN, stanceSoleOk, hipsFrames, hipsOk, lieFrames, lieCross, lieToeOk, lieHipsOk,
                       crossFrames, crossed, gapBad, holdFrames, holdFootOver, holdKneeOver, bedFrames, bedOver5, deathFrames, cloneSeen;
            public float twRangeMax, twStepMax, stanceMean, stanceMax, hipsMean, lieStepMax, lieToeMin = 9f, lieToeMax = -9f, lieHipsMean, gapMin = 9f, holdFootMax, holdKneeMax, bedStepMax, bedHandLie = -1f, bedHandStand = -1f, entryMax = -1f,
                         deathTwMin = 999f, deathTwMax = -999f, deathStepMax; public string deathProg = "-";
            public readonly List<float> heights = new List<float>();
            public int ik0Locked, ik0Guarded, ik0Forced;
        }
        readonly List<R> rs = new List<R>();

        public static OzHeroLegsProbe Begin(float seconds = 60f, bool drive = false)
        {
            var g = ZGame.I;
            if (!g || g.World == null) { Status = "no running game"; Debug.LogWarning("[OpenZ HeroLegs] " + Status); return null; }
            var c = new GameObject("OzHeroLegsProbe").AddComponent<OzHeroLegsProbe>();
            c.seconds = Mathf.Max(5f, seconds); c.drive = drive; Status = "running";
            return c;
        }

        void Start()
        {
            if (hashes.Count == 0) foreach (var s in Known) hashes[Animator.StringToHash(s)] = s;
            t0 = Time.time;
            var g = ZGame.I;
            var heroes = new List<UnitView>();
            foreach (var v in g.Views.Values) if (v && v.Obj != null && v.Obj.IsHero && !v.Dying && v.isActiveAndEnabled) heroes.Add(v);
            foreach (var v in g.Views.Values)
            {
                if (!v || v.Obj == null || v.Obj.Kind != ZKind.Robot || v.Dying || !v.isActiveAndEnabled) continue;
                bool hero = v.Obj.IsHero;
                if (!hero)
                {   // the control: grunts within 30 m of a hero, at most 4
                    if (v.Obj.Type != "grunt" || rs.Count(q => !q.hero) >= 4) continue;
                    bool near = false; foreach (var h in heroes) if ((h.transform.position - v.transform.position).sqrMagnitude < 900f) near = true;
                    if (!near) continue;
                }
                var a = v.GetComponentInChildren<Animator>(); if (!a || !a.isHuman) continue;
                var r = new R { v = v, a = a, hero = hero, member = hero ? v.Obj.HeroMemberName : v.Obj.Type, name = (hero ? v.Obj.HeroMemberName : v.Obj.Type) + " #" + v.Obj.Id, legs = hero ? OzHeroLegs.Of(v) : null, ik = a.GetComponent<RobotLegIK>(), sc = Mathf.Max(0.1f, a.transform.lossyScale.y) };
                r.hips = a.GetBoneTransform(HumanBodyBones.Hips);
                r.up[0] = a.GetBoneTransform(HumanBodyBones.LeftUpperLeg); r.up[1] = a.GetBoneTransform(HumanBodyBones.RightUpperLeg);
                r.knee[0] = a.GetBoneTransform(HumanBodyBones.LeftLowerLeg); r.knee[1] = a.GetBoneTransform(HumanBodyBones.RightLowerLeg);
                r.foot[0] = a.GetBoneTransform(HumanBodyBones.LeftFoot); r.foot[1] = a.GetBoneTransform(HumanBodyBones.RightFoot);
                r.toe[0] = a.GetBoneTransform(HumanBodyBones.LeftToes); r.toe[1] = a.GetBoneTransform(HumanBodyBones.RightToes);
                if (!r.hips || !r.up[0] || !r.up[1] || !r.knee[0] || !r.knee[1] || !r.foot[0] || !r.foot[1]) continue;
                if (r.ik) { r.ik0Locked = r.ik.LockedFrames; r.ik0Guarded = r.ik.Guarded; r.ik0Forced = r.ik.ForcedReleases; }
                rs.Add(r);
            }
            csv.AppendLine("frame,t,who,state,program,phase,speed,plantedL,plantedR,footL_x,footL_y,footL_z,footR_x,footR_y,footR_z,toeL_h,toeR_h,hips_h,twistL,twistR,headL,headR,dt");
            if (drive) Drive(false);
            Debug.Log($"[OpenZ HeroLegs] Play numbers: {seconds:0} s, switch {(OzHeroLegs.On ? "ON" : "OFF")}, {rs.Count(q => q.hero)} heroes, {rs.Count(q => !q.hero)} grunts as the control");
        }

        void Drive(bool home)
        {
            var g = ZGame.I; if (!g) return;
            if (home) { foreach (var (o, x, y) in starts) if (!o.Destroyed) g.World.SendWaypoints(g.player, o, new[] { ZWaypoint.Move(x, y) }); return; }
            foreach (var r in rs)
            {
                var o = r.v.Obj; if (o.IsMinion || o.Owner != g.player) continue;
                if (starts.Any(s => s.o == o)) continue;
                int cx = o.LocX + o.WidthPix / 2, cy = o.LocY + o.HeightPix / 2;
                starts.Add((o, cx, cy));
                g.World.SendWaypoints(g.player, o, new[] { ZWaypoint.Move(cx + 96, cy) });
            }
        }

        static string StateName(AnimatorStateInfo st) => hashes.TryGetValue(st.shortNameHash, out var n) ? n : "other";
        static bool IsGait(string s) { foreach (var g in Gaits) if (g == s) return true; return false; }
        static float Twist(Quaternion dq, Vector3 axis)
        {
            float d = dq.x * axis.x + dq.y * axis.y + dq.z * axis.z;
            float a = 2f * Mathf.Atan2(d, dq.w) * Mathf.Rad2Deg;
            return Mathf.Repeat(a + 180f, 360f) - 180f;
        }

        void LateUpdate()
        {
            var g = ZGame.I; if (!g) { Finish(); return; }
            float dt = Mathf.Max(1e-4f, Time.deltaTime), t = Time.time - t0;
            if (drive && !droveHome && t >= seconds * 0.5f) { droveHome = true; Drive(true); }
            foreach (var r in rs)
            {
                if (!r.v || r.v.Obj == null || r.v.Obj.Destroyed || !r.a || !r.a.isInitialized) continue;
                if (r.hero && r.legs == null) r.legs = OzHeroLegs.Of(r.v);
                var root = r.v.transform; var inv = Quaternion.Inverse(root.rotation);
                var st = r.a.GetCurrentAnimatorStateInfo(0); string sn = StateName(st);
                var front = -root.forward; front.y = 0f; if (front.sqrMagnitude < 1e-6f) front = Vector3.forward; front.Normalize();
                var right = Vector3.Cross(Vector3.up, front);
                float yaw = root.eulerAngles.y; float yawRate = r.yawOk ? Mathf.Abs(Mathf.DeltaAngle(r.prevYaw, yaw)) / dt : 0f; r.prevYaw = yaw; r.yawOk = true;
                float speed = r.legs != null ? r.legs.SpeedAlong : 0f;
                var vel = Vector3.zero;
                if (r.locOk) { vel = (root.position - r.prevRoot) / dt; vel.y = 0f; }
                if (r.legs == null) speed = vel.magnitude;
                r.frames++;
                bool gait = IsGait(sn), dying = r.v.Dying;
                bool straight = (sn == "walking" || sn == "run_forward") && speed > StraightSpeed && yawRate < 30f && !dying;
                var travel = vel.magnitude > StraightSpeed ? vel.normalized : front;
                // 1. the thigh roll (relative to the first frame's local rotation, about the thigh's own axis)
                if (!r.refOk)
                {
                    for (int i = 0; i < 2; i++) { r.refUp[i] = r.up[i].localRotation; r.axisUp[i] = (Quaternion.Inverse(r.up[i].rotation) * (r.knee[i].position - r.up[i].position)).normalized; }
                    r.refOk = true;
                }
                float[] tw = new float[2], head = new float[2]; bool[] pl = new bool[2]; float[] toeH = new float[2];
                for (int i = 0; i < 2; i++)
                {
                    tw[i] = Twist(Quaternion.Inverse(r.refUp[i]) * r.up[i].localRotation, r.axisUp[i]);
                    if (gait && !dying)
                    {
                        r.gaitFrames += i == 0 ? 1 : 0;
                        if (r.twPrevOk && r.prevState == sn) r.twStepMax = Mathf.Max(r.twStepMax, Mathf.Abs(Mathf.DeltaAngle(r.twPrev[i], tw[i])));
                        var w = r.win[i]; w.Add((Time.time, tw[i])); w.RemoveAll(e => Time.time - e.t > TwistWindow);
                        if (w.Count > 3) { float lo = w.Min(e => e.tw), hi = w.Max(e => e.tw); r.twRangeMax = Mathf.Max(r.twRangeMax, hi - lo); }
                    }
                    else r.win[i].Clear();
                    r.twPrev[i] = tw[i];
                    // 2. the foot heading on straight-walk frames
                    var toeDir = (r.toe[i] ? r.toe[i].position : r.foot[i].position + root.forward * -0.1f) - r.foot[i].position; toeDir.y = 0f;
                    float ang = toeDir.sqrMagnitude > 1e-6f ? Vector3.SignedAngle(travel, toeDir, Vector3.up) : 0f;
                    head[i] = i == 1 ? ang : -ang;                                               // + = toes out, toward that foot's own side
                    if (straight) { if (i == 0) r.straightFrames++; if (Mathf.Abs(head[i] - 14f) <= 6f) r.headIn++; if (Mathf.Abs(head[i] - 14f) > 45f) r.headOver++; }
                    // 3. the planted slide (the component's flag; a grunt: its ankle within 2 cm x size of its rest height)
                    var fp = r.foot[i].position; float gy = g.GroundY(fp.x, fp.z);
                    float rest = r.ik ? r.ik.AnkleRest(i) : float.NaN;
                    bool planted = r.legs != null ? r.legs.Planted(i) && gait : (!float.IsNaN(rest) && fp.y - gy <= rest + 0.02f * r.sc && gait);
                    pl[i] = planted;
                    toeH[i] = (r.toe[i] ? r.toe[i].position.y : fp.y) - gy;
                    if (planted && !dying)
                    {
                        r.plantedFrames++;
                        if (r.inStance[i]) { var d2 = fp - r.footPrevW[i]; d2.y = 0f; float sp = d2.magnitude / dt; if (sp <= 0.05f) r.plantedSlow++; r.path[i] += d2.magnitude; r.toeMin[i] = Mathf.Min(r.toeMin[i], toeH[i]); }
                        else { r.inStance[i] = true; r.path[i] = 0f; r.toeMin[i] = toeH[i]; r.plantedSlow++; }
                    }
                    else if (r.inStance[i])
                    {   // a stance ended: its path and its lowest toe height
                        r.inStance[i] = false; r.stanceN++; r.stanceMean += r.path[i]; r.stanceMax = Mathf.Max(r.stanceMax, r.path[i]);
                        float want = (r.legs != null && r.legs.Data != null ? r.legs.Data.J.toeRestY : 0.063f) * r.sc;
                        if (Mathf.Abs(r.toeMin[i] - want) <= 0.02f) r.stanceSoleOk++;
                    }
                    r.footPrevW[i] = fp;
                }
                // 4. the hips' height on gait frames
                float hipsH = r.hips.position.y - g.GroundY(r.hips.position.x, r.hips.position.z);
                if (gait && !dying && r.legs != null && r.legs.Data != null)
                {
                    r.hipsFrames++; r.hipsMean += hipsH;
                    float want = (sn == "walking" ? r.legs.Data.J.walkHipsY : r.legs.Data.J.walkHipsY) * r.sc;
                    if (Mathf.Abs(hipsH - want) <= 0.03f) r.hipsOk++;
                }
                if (r.legs != null && r.legs.EntryStep >= 0f) r.entryMax = Mathf.Max(r.entryMax, r.legs.EntryStep);
                // the feet and knees in the root's frame (steps for 6, 7, 8)
                var fLoc = new Vector3[2]; var kLoc = new Vector3[2];
                for (int i = 0; i < 2; i++) { fLoc[i] = inv * (r.foot[i].position - root.position); kLoc[i] = inv * (r.knee[i].position - root.position); }
                // 5. lying
                var antic = r.v.Antic; bool lying = antic != null && antic.Kind == AnticKind.Sunbed && antic.Step == "sunbed_lie";
                if (lying && r.hero)
                {
                    r.lieFrames++;
                    if (r.legs != null && r.legs.Ready)
                    {
                        float mx = 0f; int nb = r.legs.Data.Bones.Length;
                        for (int b = 0; b < nb; b++) { var tb = r.legs.Bone(b); if (!tb) continue; if (r.lieQOk) mx = Mathf.Max(mx, Quaternion.Angle(r.lieQ[b], tb.localRotation)); r.lieQ[b] = tb.localRotation; }
                        if (r.lieQOk) r.lieStepMax = Mathf.Max(r.lieStepMax, mx);
                        r.lieQOk = true;
                        float toeWant = r.legs.Data.J.lieToeY * r.sc, hipsWant = r.legs.Data.J.lieHipsY * r.sc;
                        float toeLow = Mathf.Min(toeH[0], toeH[1]); r.lieToeMin = Mathf.Min(r.lieToeMin, toeLow); r.lieToeMax = Mathf.Max(r.lieToeMax, toeLow);
                        if (Mathf.Abs(toeLow - toeWant) <= 0.02f) r.lieToeOk++;
                        if (Mathf.Abs(hipsH - hipsWant) <= 0.03f) r.lieHipsOk++;
                        r.lieHipsMean += hipsH;
                    }
                    var mid = 0.5f * (r.up[0].position + r.up[1].position);
                    float xl = -Vector3.Dot(r.foot[0].position - mid, right), xr = Vector3.Dot(r.foot[1].position - mid, right);
                    if (xl <= 0f || xr <= 0f) r.lieCross++;
                }
                else r.lieQOk = false;
                // 6. crossing on walking and standing frames
                if (!dying && (gait || sn == "idle" || sn == "shooting") && !lying)
                {
                    r.crossFrames++;
                    var mid = 0.5f * (r.up[0].position + r.up[1].position);
                    float xl = -Vector3.Dot(r.foot[0].position - mid, right), xr = Vector3.Dot(r.foot[1].position - mid, right);
                    if (xl <= 0f || xr <= 0f) r.crossed++;
                    float bw = r.legs != null && r.legs.Data != null ? 0.5f * (r.legs.Data.J.bootWidthL + r.legs.Data.J.bootWidthR) : 0.2f;
                    float gap = (xl + xr) - bw * r.sc; r.gapMin = Mathf.Min(r.gapMin, gap);
                    float gapWant = r.member == "brad" ? 0.10f : 0.02f;
                    if (r.hero && gap < gapWant) r.gapBad++;
                }
                // 7. drink / fire / grenade: steps over 1 cm in the root's frame
                if (!dying && (sn == "shooting" || sn == "grenade_throw" || sn == "drink" || sn == "crush" || sn == "toss") && r.locOk && r.prevState == sn)
                {
                    r.holdFrames++;
                    for (int i = 0; i < 2; i++)
                    {
                        float fs = (fLoc[i] - r.prevFoot[i]).magnitude * 100f, ks = (kLoc[i] - r.prevKnee[i]).magnitude * 100f;
                        r.holdFootMax = Mathf.Max(r.holdFootMax, fs); r.holdKneeMax = Mathf.Max(r.holdKneeMax, ks);
                        if (fs > 1f) r.holdFootOver++; if (ks > 1f) r.holdKneeOver++;
                    }
                }
                // 8. lie-down / get-up: joint steps over 5 cm; the hand-over steps
                if (!dying && (sn == "sunbed_down" || sn == "sunbed_up") && r.locOk)
                {
                    r.bedFrames++;
                    float mx = 0f; for (int i = 0; i < 2; i++) { mx = Mathf.Max(mx, (fLoc[i] - r.prevFoot[i]).magnitude); mx = Mathf.Max(mx, (kLoc[i] - r.prevKnee[i]).magnitude); }
                    r.bedStepMax = Mathf.Max(r.bedStepMax, mx); if (mx > 0.05f && r.prevState == sn) r.bedOver5++;
                }
                if (r.locOk && r.prevState == "sunbed_down" && sn == "sunbed_lie") r.bedHandLie = Mathf.Max(r.bedHandLie, Mathf.Max((fLoc[0] - r.prevFoot[0]).magnitude, (fLoc[1] - r.prevFoot[1]).magnitude));
                if (r.locOk && r.prevState == "sunbed_up" && sn == "idle") r.bedHandStand = Mathf.Max(r.bedHandStand, Mathf.Max((fLoc[0] - r.prevFoot[0]).magnitude, (fLoc[1] - r.prevFoot[1]).magnitude));
                // 9. knockdown / animated death
                if (sn == "blast_airborne" || sn == "death_back" || sn == "death_side")
                {
                    r.deathFrames++; r.deathProg = sn;
                    for (int i = 0; i < 2; i++) { r.deathTwMin = Mathf.Min(r.deathTwMin, tw[i]); r.deathTwMax = Mathf.Max(r.deathTwMax, tw[i]); if (r.twPrevOk && r.prevState == sn) r.deathStepMax = Mathf.Max(r.deathStepMax, Mathf.Abs(Mathf.DeltaAngle(r.twPrev[i], tw[i]))); }
                }
                // 12. the drawn standing height (idle, no antic, not dying)
                if (r.hero && sn == "idle" && antic == null && !dying && (r.frames % 10) == 0) r.heights.Add(OzHeroSquadView.DrawnHeight(r.v));
                // memory
                for (int i = 0; i < 2; i++) { r.prevFoot[i] = fLoc[i]; r.prevKnee[i] = kLoc[i]; }
                r.prevRoot = root.position;
                r.twPrevOk = true; r.locOk = true; r.prevState = sn;
                csv.Append(Time.frameCount).Append(',').Append(F(t)).Append(',').Append(r.name).Append(',').Append(sn).Append(',').Append(r.legs != null ? r.legs.ProgramName : "-").Append(',')
                   .Append(F(r.legs != null ? r.legs.Phase : 0f)).Append(',').Append(F(speed)).Append(',').Append(pl[0] ? 1 : 0).Append(',').Append(pl[1] ? 1 : 0);
                for (int i = 0; i < 2; i++) { var f = r.foot[i].position; csv.Append(',').Append(F(f.x)).Append(',').Append(F(f.y)).Append(',').Append(F(f.z)); }
                csv.Append(',').Append(F(toeH[0])).Append(',').Append(F(toeH[1])).Append(',').Append(F(hipsH)).Append(',').Append(F(tw[0])).Append(',').Append(F(tw[1])).Append(',').Append(F(head[0])).Append(',').Append(F(head[1])).Append(',').Append(F(dt)).Append('\n');
            }
            if (t >= seconds) Finish();
        }

        static string F(float x) => x.ToString("0.0000", CultureInfo.InvariantCulture);
        static string P(int a, int b) => b > 0 ? (100f * a / b).ToString("0.0", CultureInfo.InvariantCulture) + " %" : "-";

        void Finish()
        {
            var sb = new StringBuilder(); var fails = new List<string>(); int judged = 0;
            sb.AppendLine("| robot | frames (gait / straight) | 1 thigh roll range / step (deg) | 2 heading within 14+-6 / beyond 45 | 3 planted: slow share, slide per stance mean / max (cm), entry step (cm) | 4 soles: stances within +-2 cm, hips within +-3 cm (mean m) | 5 lying: frames, max step (deg), toe height min..max (m), toe / hips within, crossed | 6 crossing: crossed, inner gap min (cm), frames under | 7 hold states: frames, foot / knee steps over 1 cm (largest cm) | 8 lie-down / get-up: frames, steps over 5 cm (largest cm), hand-over lie / stand (cm) | 9 knockdown / death: frames, roll range / step | verdict |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|");
            float hAlan = -1f, hBrad = -1f;
            foreach (var r in rs)
            {
                var why = new List<string>();
                float stMean = r.stanceN > 0 ? r.stanceMean / r.stanceN * 100f : 0f, stMax = r.stanceMax * 100f;
                float gapWant = r.member == "brad" ? 10f : 2f;
                string heights = "";
                if (r.hero && r.heights.Count > 0) { var h = r.heights.OrderBy(x => x).ToList(); float med = h[h.Count / 2]; if (r.member == "alan") hAlan = med; else if (r.member == "brad") hBrad = med; heights = $" standing height {med:0.00} m"; }
                if (r.hero && OzHeroLegs.On)
                {
                    judged++;
                    if (r.legs == null || !r.legs.Ready) why.Add("the component is not driving this hero (" + (r.legs != null ? r.legs.Why : "none") + ")");
                    if (r.gaitFrames >= 30)
                    {
                        if (r.twRangeMax > 12f) why.Add($"thigh roll range {r.twRangeMax:0.0}");
                        if (r.twStepMax > 2f) why.Add($"thigh roll step {r.twStepMax:0.0}");
                    }
                    if (r.straightFrames >= 30 && (r.headIn < 0.95f * r.straightFrames || r.headOver > 0)) why.Add($"heading within 14+-6 on {P(r.headIn, r.straightFrames)}, beyond 45 on {r.headOver}");
                    if (r.plantedFrames >= 30 && r.plantedSlow < 0.95f * r.plantedFrames) why.Add($"planted slow share {P(r.plantedSlow, r.plantedFrames)}");
                    if (r.stanceN >= 3 && (stMax > 3f * r.sc || stMean > 3f * r.sc)) why.Add($"slide per stance {stMean:0.0} / {stMax:0.0} cm");
                    if (r.entryMax > 0.08f) why.Add($"entry step {r.entryMax * 100:0.0} cm");
                    if (r.stanceN >= 3 && r.stanceSoleOk < 0.95f * r.stanceN) why.Add($"soles within +-2 cm on {r.stanceSoleOk} of {r.stanceN} stances");
                    if (r.hipsFrames >= 30 && r.hipsOk < 0.95f * r.hipsFrames) why.Add($"hips height within +-3 cm on {P(r.hipsOk, r.hipsFrames)}");
                    if (r.lieFrames >= 30 && (r.lieStepMax > 0.005f || r.lieCross > 0 || r.lieToeOk < 0.95f * r.lieFrames || r.lieHipsOk < 0.95f * r.lieFrames)) why.Add($"lying: max step {r.lieStepMax:0.000} deg, crossed {r.lieCross}, toe within {P(r.lieToeOk, r.lieFrames)}, hips within {P(r.lieHipsOk, r.lieFrames)}");
                    if (r.crossed > 0 || r.gapBad > 0) why.Add($"crossing {r.crossed}, inner gap under {gapWant:0} cm on {r.gapBad} frames (min {r.gapMin * 100:0.0})");
                    if (r.holdFrames > 0 && (r.holdFootOver > 0 || r.holdKneeOver > 0)) why.Add($"hold states: foot / knee steps over 1 cm {r.holdFootOver} / {r.holdKneeOver}");
                    if (r.bedFrames > 0 && (r.bedOver5 > 0 || r.bedHandLie > 0.01f || r.bedHandStand > 0.01f)) why.Add($"lie-down / get-up: steps over 5 cm {r.bedOver5}, hand-over {r.bedHandLie * 100:0.0} / {r.bedHandStand * 100:0.0} cm");
                    if (r.deathFrames > 0 && r.legs != null && r.legs.Data != null)
                    {
                        var pr = r.legs.Data.Find(r.deathProg); float want = pr != null ? Mathf.Max(pr.RollRangeL, pr.RollRangeR) + 5f : 35f, wantStep = pr != null ? Mathf.Max(pr.RollStepL, pr.RollStepR) + 3f : 33f;
                        if (r.deathTwMax - r.deathTwMin > want || r.deathStepMax > wantStep) why.Add($"{r.deathProg}: roll range {r.deathTwMax - r.deathTwMin:0.0} (bake {want - 5:0.0}), step {r.deathStepMax:0.0} (bake {wantStep - 3:0.0})");
                    }
                }
                string verdict = !r.hero ? "REF (a grunt: the control, not judged)" : !OzHeroLegs.On ? "baseline (switch OFF, not judged)" : why.Count == 0 ? "ok" : "FAIL: " + string.Join("; ", why);
                if (r.hero && OzHeroLegs.On && why.Count > 0) fails.Add(r.name + ": " + string.Join("; ", why));
                sb.AppendLine($"| {r.name}{heights} | {r.frames} ({r.gaitFrames} / {r.straightFrames}) | {r.twRangeMax:0.0} / {r.twStepMax:0.00} | {P(r.headIn, r.straightFrames)} / {r.headOver} | {P(r.plantedSlow, r.plantedFrames)}, {stMean:0.0} / {stMax:0.0}, {(r.entryMax >= 0 ? (r.entryMax * 100).ToString("0.0", CultureInfo.InvariantCulture) : "-")} | {r.stanceSoleOk} of {r.stanceN}, {P(r.hipsOk, r.hipsFrames)} ({(r.hipsFrames > 0 ? r.hipsMean / r.hipsFrames : 0f):0.00}) | {r.lieFrames}, {r.lieStepMax:0.000}, {(r.lieFrames > 0 ? r.lieToeMin.ToString("0.000", CultureInfo.InvariantCulture) + ".." + r.lieToeMax.ToString("0.000", CultureInfo.InvariantCulture) : "-")}, {P(r.lieToeOk, r.lieFrames)} / {P(r.lieHipsOk, r.lieFrames)}, {r.lieCross} | {r.crossed}, {(r.crossFrames > 0 ? (r.gapMin * 100).ToString("0.0", CultureInfo.InvariantCulture) : "-")}, {r.gapBad} | {r.holdFrames}, {r.holdFootOver} / {r.holdKneeOver} ({r.holdFootMax:0.00} / {r.holdKneeMax:0.00}) | {r.bedFrames}, {r.bedOver5} ({r.bedStepMax * 100:0.0}), {(r.bedHandLie >= 0 ? (r.bedHandLie * 100).ToString("0.0", CultureInfo.InvariantCulture) : "-")} / {(r.bedHandStand >= 0 ? (r.bedHandStand * 100).ToString("0.0", CultureInfo.InvariantCulture) : "-")} | {r.deathFrames}{(r.deathFrames > 0 ? " " + r.deathProg + ", " + (r.deathTwMax - r.deathTwMin).ToString("0.0", CultureInfo.InvariantCulture) + " / " + r.deathStepMax.ToString("0.0", CultureInfo.InvariantCulture) : "")} | {verdict} |");
            }
            string size = hAlan > 0f && hBrad > 0f ? $"size: Alan {hAlan:0.00} m standing over Brad {hBrad:0.00} m = {hAlan / hBrad:0.000} (wanted 0.90-0.98){(hAlan / hBrad >= 0.90f && hAlan / hBrad <= 0.98f ? "" : " FAIL")}" : "size: not measured (both heroes must stand idle with no antic during the run)";
            if (OzHeroLegs.On && hAlan > 0f && hBrad > 0f && (hAlan / hBrad < 0.90f || hAlan / hBrad > 0.98f)) fails.Add(size);
            string clone = OzHeroLegs.CloneFrames > 0 ? $"boarding: clone legs written on {OzHeroLegs.CloneFrames} frames" : "boarding: not verified (no hero boarded while the component ran)";
            string result = !OzHeroLegs.On ? "RESULT: BASELINE (switch OFF: today's legs measured, nothing judged)" : judged == 0 ? "RESULT: NO DATA (no hero found)" : fails.Count == 0 ? $"RESULT: PASS ({judged} heroes judged)" : "RESULT: FAIL: " + string.Join(" | ", fails);
            var head = new StringBuilder();
            head.AppendLine(result).AppendLine();
            head.AppendLine($"# Hero legs Play numbers: {seconds:0} s; switch oz_hero_legs {(OzHeroLegs.On ? "ON" : "OFF")}, foot lock v2 {(RobotLegIK.FootLockV2 ? "ON" : "OFF")}, RobotLegIK.On {RobotLegIK.On}; drive {drive}; Time.timeScale {Time.timeScale}; targetFrameRate {Application.targetFrameRate}; level {(ZGame.I ? ZGame.I.level : "-")}");
            head.AppendLine($"Component: views driven {OzHeroLegs.Attached}, frames written {OzHeroLegs.Frames}, Hips writes {OzHeroLegs.HipsWrites}; {clone}; {size}.");
            head.AppendLine("Thigh roll = the UpLeg's twist about its own long axis relative to its first frame, range over any 1.6 s window of gait frames and the largest per-frame step. Heading = ankle to toe bone on the ground against the travel direction, + = toes out. Planted = the component's stance flag (a grunt: its ankle within 2 cm x size of its rest height). Steps = in the root's frame.");
            head.AppendLine("Grunt control rows: REF, their RobotLegIK counters over the run (LockedFrames / Guarded / ForcedReleases) for the regression comparison: " + string.Join("; ", rs.Where(q => !q.hero && q.ik).Select(q => $"{q.name} {q.ik.LockedFrames - q.ik0Locked} / {q.ik.Guarded - q.ik0Guarded} / {q.ik.ForcedReleases - q.ik0Forced}")) + $"; ToeLifts (all robots) {RobotLegIK.ToeLifts}.");
            head.AppendLine();
            string dir = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../../../../work/records"));
            try
            {
                System.IO.Directory.CreateDirectory(dir);
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "hero_legs_check.md"), head.ToString() + sb.ToString() + "\n" + string.Join("\n", OzHeroLegs.Log) + "\n");
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "hero_legs_trace.csv"), csv.ToString());
            }
            catch (System.Exception e) { Debug.LogWarning("[OpenZ HeroLegs] could not write the report: " + e.Message); }
            LastResult = result; Status = "done";
            Debug.Log("[OpenZ HeroLegs] " + result + " (hero_legs_check.md)\n" + sb);
            Destroy(gameObject);
        }
    }
}
