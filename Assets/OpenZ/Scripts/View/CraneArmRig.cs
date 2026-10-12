using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// The crane's REAL arm works while it repairs. The articulated parts are the modelled apc_crane_rebuild_v1 nodes that
    /// ApcCraneKit.ApplyLive already puts under the crane's Model (Crane_Turntable > Crane_Boom > Crane_Jib > Crane_Hook, the ram
    /// pair Crane_RamCylinder (on the turntable) / Crane_RamPiston (on the boom), pins as static children). Nothing is cut or made in
    /// code except the hoist cable (a thin dark line from the jib's hook pin to the lowered hook).
    /// <para>RulesView.Handle(CraneAnim) calls <see cref="Job"/>: the arm swings the turntable round to the building (smoothstep,
    /// the work spot (reach solved on the real boom geometry, luff -6..+40 deg = the ram's travel), lowers the hook on its cable to
    /// the spot (facade of a damaged building at 0.7-2 m, the rubble of a ruin, the deck of a bridge), welds at the hook tip (sparks,
    /// blue-white light, the weld crackle; tip-to-spark distance logged), makes small working nudges (slew / luff / hook bob), raises
    /// the hook and moves to the next spot; when the sim ends the job it raises the hook, slews and luffs home and seats with a
    /// clank. The ram cylinder and piston aim at each other every frame (the rod slides in the gland); the hook hangs plumb and sways
    /// a little after each slew. Every motion has its sound: slew = servo whine (mech-servo), luff = sd_crane_arm_hydraulic, hook =
    /// sd_hoist_chain_a/b, tool contact + boom seat = a clank (sd_tank_breech_clank), nudges = a servo tick (sd_robot_servo), weld =
    /// bld_repair_weld (RulesView's own weld event); all through CombatAudio / AudioDirector, so OzSoundLog lists them.</para>
    /// <para>While the arm owns a job, RulesView no longer draws its random building-side weld sparks (<see cref="Covers"/>): the
    /// weld is at the tool tip only. No kit on the crane (ApcCraneKit off / parts missing) = the old RulesView sparks (fallback).
    /// View only: the sim's repair timing (DV61 4 s at the entrance, Zod's drive-in) is untouched.</para>
    /// </summary>
    public static class CraneArm
    {
        /// <summary>Options switch (Batch C menu): off = RulesView's building-side sparks as before, the arm stays at rest.</summary>
        public static bool Enabled = true;
        public static int Jobs, Ends, Cycles, Welds, Arcs, Sounds, OutOfReach, NoKit, NoContact;
        /// <summary>crane_front_reach: work spots picked on a wall / on a floor (deck, rubble, ruin floor) of the building's real surface.</summary>
        public static int WallSpots, FloorSpots;
        /// <summary>Largest tool-tip-to-spark distance seen (m); the row's acceptance is <= 0.2 m.</summary>
        public static float TipSparkMax;
        /// <summary>
        /// within CraneArmRig.ContactMax of its spot (else NoContact++ and the arm moves on): no sparks in the air above the cab.</summary>
        public static float TipWorkMax;
        public static string Last = "";
        static readonly Dictionary<int, CraneArmRig> byBuilding = new Dictionary<int, CraneArmRig>();

        /// <summary>RulesView.Handle(ZEventType.CraneAnim): on = the crane starts mending `building`, off = the job ended.</summary>
        public static void Job(ZGame game, ZObject crane, ZObject building, bool on)
        {
            if (game == null || building == null) return;
            if (!on)
            {
                if (byBuilding.TryGetValue(building.Id, out var r0) && r0) r0.End(building.Id);
                byBuilding.Remove(building.Id); Ends++;
                return;
            }
            if (!Enabled || crane == null) return;
            if (!game.Views.TryGetValue(crane.Id, out var v) || !v) return;
            var rig = v.GetComponent<CraneArmRig>(); if (!rig) rig = v.gameObject.AddComponent<CraneArmRig>();
            if (!rig.Bind(game, v)) { NoKit++; Last = $"crane#{crane.Id}: no modelled arm (ApcCraneKit off?): RulesView sparks"; return; }
            if (rig.BuildingId >= 0 && rig.BuildingId != building.Id) byBuilding.Remove(rig.BuildingId);
            rig.Begin(building);
            byBuilding[building.Id] = rig; Jobs++;
        }

        /// <summary>RulesView.Update: true = this building's crane welds at its own tool tip, so RulesView skips its building-side sparks.</summary>
        public static bool Covers(int buildingId) =>
            Enabled && byBuilding.TryGetValue(buildingId, out var r) && r && r.isActiveAndEnabled && r.BuildingId == buildingId && r.Welding;   // crane_front_reach

        internal static void Forget(int buildingId, CraneArmRig rig)
        {
            if (byBuilding.TryGetValue(buildingId, out var r) && r == rig) byBuilding.Remove(buildingId);
        }

        public static string Report()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append($"CraneArm: enabled {Enabled}, jobs {Jobs} (ended {Ends}), cycles {Cycles}, welds {Welds} (+{Arcs} arcs), sounds {Sounds}, ");
            sb.Append($"out of reach {OutOfReach}, no kit {NoKit}, tip-to-spark max {TipSparkMax:F3} m (<= 0.2), ");
            sb.Append($"tip-to-work max {TipWorkMax:F3} m (<= {CraneArmRig.ContactMax:F2}), no contact {NoContact} (welds skipped: tip off its spot)\n");
            sb.Append($"  reach (crane_front_reach): spots on a wall {WallSpots}, on a floor {FloorSpots}, out of reach {OutOfReach} (boom points, RulesView sparks on the real face); {BuildingSurface.Report()}\n");
            foreach (var r in Object.FindObjectsByType<CraneArmRig>(FindObjectsSortMode.None)) sb.Append("  ").Append(r.Status).Append('\n');
            if (Last.Length > 0) sb.Append("  last: ").Append(Last).Append('\n');
            return sb.ToString();
        }
    }

    /// <summary>The arm of one crane (added by <see cref="CraneArm.Job"/>). See <see cref="CraneArm"/>.</summary>
    [DefaultExecutionOrder(62)]   // after UnitView (60): the hull has its pose for this frame
    public class CraneArmRig : MonoBehaviour
    {
        // ---- tuning (game metres / degrees / seconds)
        public static float LuffMin = -6f, LuffMax = 40f, MaxDrop = 2.6f, Standoff = 0.22f;
        public static float SwingTime = 1.05f, LowerTime = 0.55f, RaiseTime = 0.45f, ReturnTime = 1.3f;
        public static float WeldGapMin = 0.28f, WeldGapMax = 0.5f, NudgeGapMin = 1.0f, NudgeGapMax = 1.5f, NudgeTime = 0.35f;
        public static float ArcChance = 0.12f;
        /// <summary>
        /// (evidence\crane_report.txt) has front spots where the cab cap / luff floor leave the tip 0.54 m and 1.31 m off the spot.</summary>
        public static float ContactMax = 0.2f;                                                     // crane_front_reach: tip to the SURFACE (row: tip-to-spark <= 0.2 m)
        /// <summary>crane_front_reach: the boom pin stands this far off a wall so the jib head (0.29 m past the pin, 0.27 m half width)
        /// clears it; the hook then leans on its cable onto the wall until its lowest part is 1.5 cm off it (tip ~0.15 m, LeanAt).</summary>
        public static float JibClear = 0.4f, JibNose = 0.32f, JibHalf = 0.3f;
        bool noContact;

        enum St { Rest, Swing, Lower, Work, Raise, Return }

        ZGame game; UnitView view; Transform model, tt, boom, jib, hook, cyl, pis;
        Quaternion ttRest, boomRest, hookRest, cylRest, pisRest, hookRelTT;
        Vector3 hookRestPos, boomPivotTT, pinTT, luffAxisTT, zeroDirM, cylAxisRest, pisAxisRest, tipLocal;
        float cabX0, cabX1;                                                                         // the chassis' length in the Model frame (GLB units)
        /// <summary>apc_crane_rebuild_v1 Crane_Chassis (GLB units): x -0.746..0.947, top y 0.074 (cab roof), z +-0.488. The hook never
        /// drops into it, and the boom never luffs below rest while it points over the cab (+-40 deg of the rest slew).</summary>
        public static float CabTop = 0.074f, CabHalfWidth = 0.488f, OverCabSlew = 40f, HookR = 0.06f;
        bool bound, failed;
        LineRenderer cable; static Material cableMat;

        ZObject job; public int BuildingId { get; private set; } = -1;
        St st = St.Rest; float stT;
        float slew, luff, drop;                      // current pose (deg, deg, m)
        float slew0, luff0, drop0;                   // pose at the phase start
        Vector3 target; int spot; Bounds bb; bool bbOk;
        float nextWeld, nextNudge, nudgeT = 99f; float nSlew, nLuff, nDrop, pSlew, pLuff, pDrop;
        float sway, swayVel, lastSlew, lastSlewVel;
        float jobUntil;
        // crane_front_reach: the spot is ON the building's real surface (BuildingSurface: colliders, else meshes; never the AABB)
        Vector3 surf, surfN = Vector3.up, lean, hookLong = Vector3.right;
        bool reachable = true, wallSpot;
        float hookYaw, hookHalf = -1f, nextRepick, nextRetry, nLat, pLat, nearestWall;
        struct Cand { public Vector3 pin, surf, n; public float score; }
        static readonly List<Cand> cands = new List<Cand>(96);
        static readonly System.Comparison<Cand> byScore = (x, y) => y.score.CompareTo(x.score);
        /// <summary>The arm welds this job itself: a reachable spot and the crane parked (RulesView skips its sparks then).</summary>
        public bool Welding => reachable && view && view.Obj != null && !view.Obj.IsMoving;

        public string Status => !bound ? $"{name}: not bound" : $"{name}: {st} job {BuildingId} slew {slew:F1} luff {luff:F1} drop {drop:F2} m, " +
                                (reachable ? (wallSpot ? "wall spot" : "floor spot") : nearestWall < 0f ? "out of reach (no wall in reach)" : $"out of reach (nearest wall {nearestWall:F1} m from the turntable)");

        /// <summary>Finds the modelled arm under Model (ApcCraneKit's OZ_KitLive) and stores its rest pose. false = no kit.</summary>
        public bool Bind(ZGame g, UnitView v)
        {
            game = g; view = v;
            if (bound) return true;
            if (failed) return false;
            model = v.transform.Find("Model");
            if (!model || !model.Find("OZ_KitLive")) { failed = true; return false; }                   // ApcCraneKit builds the live kit at UnitView.Bind: missing now = missing for good
            tt = Deep(model, "Crane_Turntable"); boom = Deep(model, "Crane_Boom"); jib = Deep(model, "Crane_Jib"); hook = Deep(model, "Crane_Hook");
            cyl = Deep(model, "Crane_RamCylinder"); pis = Deep(model, "Crane_RamPiston");
            if (!tt || !boom || !jib || !hook || boom.parent != tt || hook.parent != jib) { failed = true; return false; }
            ttRest = tt.localRotation; boomRest = boom.localRotation; hookRest = hook.localRotation; hookRestPos = hook.localPosition;
            boomPivotTT = boom.localPosition;
            pinTT = tt.InverseTransformPoint(hook.position);
            var arm = pinTT - boomPivotTT;
            luffAxisTT = Vector3.Cross(arm, Vector3.up); luffAxisTT = luffAxisTT.sqrMagnitude > 1e-8f ? luffAxisTT.normalized : Vector3.forward;
            zeroDirM = model.InverseTransformVector(hook.position - tt.position); zeroDirM.y = 0f;
            zeroDirM = zeroDirM.sqrMagnitude > 1e-8f ? zeroDirM.normalized : Vector3.right;
            hookRelTT = Quaternion.Inverse(tt.rotation) * hook.rotation;
            if (cyl && pis && cyl.parent == tt && pis.parent == boom)
            {
                cylRest = cyl.localRotation; pisRest = pis.localRotation;
                cylAxisRest = tt.InverseTransformPoint(pis.position) - cyl.localPosition;
                pisAxisRest = boom.InverseTransformPoint(cyl.position) - pis.localPosition;
            }
            else { cyl = null; pis = null; }
            tipLocal = HookTip(hook);
            float sx = tt.localPosition.x < 0f ? -1f : 1f;                                            // glTFast negates x: GLB turntable x is +0.49
            cabX0 = Mathf.Min(-0.746f * sx, 0.947f * sx); cabX1 = Mathf.Max(-0.746f * sx, 0.947f * sx);
            bound = true;
            return true;
        }

        static Transform Deep(Transform t, string n)
        {
            if (t.name == n) return t;
            foreach (Transform c in t) { var f = Deep(c, n); if (f) return f; }
            return null;
        }

        /// <summary>The hook's lowest point in its own frame (the welder tip), from its vertices (fallback: the measured GLB value).</summary>
        static Vector3 HookTip(Transform h)
        {
            var mf = h.GetComponent<MeshFilter>(); var m = mf ? mf.sharedMesh : null;
            if (m && m.isReadable)
            {
                var v = m.vertices; if (v.Length > 0)
                {
                    float lo = float.MaxValue; foreach (var p in v) lo = Mathf.Min(lo, p.y);
                    Vector3 s = Vector3.zero; int n = 0;
                    foreach (var p in v) if (p.y < lo + 0.012f) { s += p; n++; }
                    if (n > 0) { s /= n; return new Vector3(s.x, lo, s.z); }
                }
            }
            return new Vector3(0f, -0.243f, 0f);                                                       // apc_crane_rebuild_v1 Crane_Hook: lowest y -0.243 (GLB units)
        }

        // ------------------------------------------------------------------ job
        public void Begin(ZObject b)
        {
            job = b; BuildingId = b.Id; jobUntil = Time.time + 40f; spot = 0;
            bbOk = BuildingBounds(b, out bb);
            reachable = true; nextRepick = 0f; HookFit();                                            // crane_front_reach
            if (st == St.Rest || st == St.Return || st == St.Raise) Go(drop > 0.03f ? St.Raise : St.Swing);
            else if (st == St.Work || st == St.Lower) Go(St.Raise);
            enabled = true;
            CraneArm.Last = $"crane#{view.Obj?.Id} -> {b.Type}#{b.Id}{(b.Destroyed ? " (ruin)" : "")}";
        }

        public void End(int buildingId)
        {
            if (buildingId != BuildingId) return;
            job = null; BuildingId = -1;
            Go(drop > 0.03f ? St.Raise : St.Return);
        }

        void Go(St s)
        {
            st = s; stT = 0f; slew0 = slew; luff0 = luff; drop0 = drop;
            var at = tt ? tt.position : transform.position;
            switch (s)
            {
                case St.Swing:
                    target = PickTarget(spot++); CraneArm.Cycles++;
                    Sound("crane_slew", at, 0.42f, 0.85f, "mech-servo-1", "mech-servo-2");
                    break;
                case St.Lower: if (reachable) Sound("crane_hoist", HookPin(), 0.5f, 1f, "sd_hoist_chain_a", "sd_hoist_chain_b"); break;   // crane_front_reach: out of reach = the hook stays up
                case St.Work: noContact = false; nextWeld = 0.05f; nextNudge = Random.Range(NudgeGapMin, NudgeGapMax); nudgeT = 99f; nSlew = nLuff = nDrop = pSlew = pLuff = pDrop = 0f; nLat = pLat = 0f; nextRetry = Time.time + 1.5f; break;
                case St.Raise: if (drop > 0.03f) Sound("crane_hoist", HookPin(), 0.45f, 1.08f, "sd_hoist_chain_a", "sd_hoist_chain_b"); break;
                case St.Return:
                    Sound("crane_slew", at, 0.4f, 0.8f, "mech-servo-1", "mech-servo-2");
                    Sound("crane_luff", at, 0.5f, 0.95f, "sd_crane_arm_hydraulic");
                    break;
            }
        }

        void Sound(string key, Vector3 at, float vol, float pitch, params string[] clips)
        {
            var au = CombatAudio.I; if (!au) return;
            au.PlayPitched(key, at, vol, 0f, view && view.Obj != null ? view.Obj.Id : -1, pitch, clips);
            CraneArm.Sounds++;
        }

        // ------------------------------------------------------------------ frame
        void LateUpdate()
        {
            if (!bound || !view || view.Obj == null) return;
            if (view.Dying || view.Obj.Destroyed)
            {   // the crane keeps the boom pose it died in (ApcCraneKit.Wreck); the hook goes back on its pin under the blast
                drop = 0f; hookYaw = 0f; lean = Vector3.zero; reachable = false; Pose(); if (cable) cable.enabled = false;   // crane_front_reach: yaw / lean reset
                if (job != null) CraneArm.Forget(job.Id, this);
                job = null; BuildingId = -1; enabled = false; return;
            }
            float dt = Time.deltaTime;
            if (job != null && (Time.time > jobUntil || game == null || game.World == null || game.World.GetAny(job.Id) == null)) End(job.Id);
            if (st == St.Rest && job == null) return;                                                // at rest: the modelled rest pose, untouched
            stT += dt;
            bool moving = view.Obj.IsMoving;
            if (job != null && st != St.Raise && st != St.Return) target = Retarget(target);
            switch (st)
            {
                case St.Swing:
                {
                    Solve(target, out float sT, out float lT, out _);
                    float k = S(stT / SwingTime), kl = S((stT - 0.15f) / (SwingTime - 0.15f));
                    if (stT >= 0.15f && stT - dt < 0.15f && Mathf.Abs(lT - luff0) > 2f) Sound("crane_luff", tt.position, 0.5f, 1f, "sd_crane_arm_hydraulic");
                    slew = slew0 + Mathf.DeltaAngle(slew0, sT) * k; luff = Mathf.Lerp(luff0, lT, kl); drop = Mathf.Lerp(drop0, 0f, S(stT / 0.3f));
                    if (stT >= SwingTime) Go(St.Lower);
                    break;
                }
                case St.Lower:
                {
                    Solve(target, out float sT, out float lT, out float dT);
                    if (moving) dT = Mathf.Min(dT, 0.5f);                                            // driving (Zod drive-in): hook held short
                    slew = sT; luff = lT; drop = Mathf.Lerp(0f, dT, S(stT / LowerTime));
                    if (stT >= LowerTime) { if (reachable && !moving) Sound("crane_clank", Tip(), 0.32f, 1.3f, "sd_tank_breech_clank_a", "sd_tank_breech_clank_b"); Go(St.Work); }   // crane_front_reach
                    break;
                }
                case St.Work:
                {
                    if (!reachable)
                    {   // crane_front_reach: no surface of the building is within the boom's reach (DV61 parks 4 m past the footprint): the
                        // boom points at the building with the hook up, no tool weld (RulesView's sparks show the mend, on the real face);
                        // a silent re-pick every 1.5 s in case the crane moved
                        Solve(target, out float uS, out float uL, out _);
                        float fu = 1f - Mathf.Exp(-6f * dt);
                        slew += Mathf.DeltaAngle(slew, uS) * fu; luff = Mathf.Lerp(luff, Mathf.Clamp(uL, LuffFloor(slew), LuffMax), fu); drop = Mathf.Lerp(drop, 0f, fu);
                        if (job == null) Go(St.Raise);
                        else if (Time.time >= nextRetry) { nextRetry = Time.time + 1.5f; if (TryPick(out _, false)) Go(St.Raise); }
                        break;
                    }
                    if ((nextNudge -= dt) <= 0f && !moving)
                    {   // a small working nudge with a servo tick: at a wall the tool slides along the face (keeps its stand-off), on a
                        // floor spot the slew / luff nudge; the hook bob only lifts (never below the solved / cab-capped drop)
                        nextNudge = Random.Range(NudgeGapMin, NudgeGapMax); nudgeT = 0f;
                        pSlew = nSlew; pLuff = nLuff; pDrop = nDrop; pLat = nLat;
                        if (wallSpot) { nSlew = nLuff = 0f; nLat = Random.Range(-0.18f, 0.18f); }
                        else { nSlew = Random.Range(-3.5f, 3.5f); nLuff = Random.Range(-1.2f, 1.2f); nLat = 0f; }
                        nDrop = Random.Range(-0.08f, 0f);
                        Sound("crane_servo_tick", tt.position, 0.26f, 0.9f, "sd_robot_servo_a", "sd_robot_servo_b", "sd_robot_servo_c");
                    }
                    nudgeT += dt; float kn = S(nudgeT / NudgeTime);
                    float os = Mathf.Lerp(pSlew, nSlew, kn), ol = Mathf.Lerp(pLuff, nLuff, kn), od = Mathf.Lerp(pDrop, nDrop, kn), olat = Mathf.Lerp(pLat, nLat, kn);
                    Solve(wallSpot ? target + Vector3.Cross(Vector3.up, surfN) * olat : target, out float sT, out float lT, out float dT);
                    if (moving) dT = Mathf.Min(dT, 0.5f);                                            // driving (Zod drive-in): hook held short
                    float follow = 1f - Mathf.Exp(-6f * dt);
                    slew += Mathf.DeltaAngle(slew, sT + os) * follow; luff = Mathf.Lerp(luff, Mathf.Clamp(lT + ol, LuffFloor(slew), LuffMax), follow);
                    drop = Mathf.Lerp(drop, Mathf.Clamp(dT + od, 0f, MaxDrop), follow);
                    if ((nextWeld -= dt) <= 0f)
                    {
                        nextWeld = Random.Range(WeldGapMin, WeldGapMax) * (moving ? 1.8f : 1f);
                        Weld(moving);
                    }
                    if (job == null) Go(St.Raise);
                    else if (noContact && !moving && stT > 0.4f) { noContact = false; Go(St.Raise); }
                    else if (!moving && stT > 2.4f && Random.value < dt * 0.35f) Go(St.Raise);          // next spot now and then (a 4 s DV61 job: 1-2 spots)
                    break;
                }
                case St.Raise:
                    drop = Mathf.Lerp(drop0, 0f, S(stT / RaiseTime));
                    if (stT >= RaiseTime) Go(job != null ? St.Swing : St.Return);
                    break;
                case St.Return:
                {
                    float k = S(stT / ReturnTime);
                    slew = slew0 + Mathf.DeltaAngle(slew0, 0f) * k; luff = Mathf.Lerp(luff0, 0f, k); drop = 0f;
                    if (stT >= ReturnTime)
                    {
                        slew = 0f; luff = 0f;
                        Sound("crane_seat", tt.position, 0.3f, 1.1f, "sd_tank_breech_clank_a", "sd_tank_breech_clank_b");
                        st = St.Rest;
                    }
                    break;
                }
            }
            // hook sway: a damped pendulum kicked by the slew's acceleration
            float sv = dt > 1e-5f ? Mathf.DeltaAngle(lastSlew, slew) / dt : 0f, sa = dt > 1e-5f ? (sv - lastSlewVel) / dt : 0f;
            lastSlew = slew; lastSlewVel = sv;
            swayVel += (-38f * sway - 4.5f * swayVel - sa * 0.1f) * dt; sway += swayVel * dt; sway = Mathf.Clamp(sway, -5f, 5f);
            luff = Mathf.Max(luff, LuffFloor(slew));                                                 // never through the cab, in any phase
            Pose();
        }

        static float S(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }

        /// <summary>Lowest luff at slew `deg`: the rest luff (0) while the boom is over the cab (+-25 deg), LuffMin from +-40 deg, smooth
        /// between (so a boom swinging round from a low side spot rises over the cab instead of passing through it).</summary>
        static float LuffFloor(float deg) => Mathf.Lerp(0f, LuffMin, S((Mathf.Abs(Mathf.DeltaAngle(0f, deg)) - 25f) / (OverCabSlew - 25f)));

        // ------------------------------------------------------------------ pose
        void Pose()
        {
            tt.localRotation = Quaternion.AngleAxis(slew, Vector3.up) * ttRest;
            boom.localRotation = Quaternion.AngleAxis(luff, luffAxisTT) * boomRest;
            if (cyl)
            {   // the ram pair aim at each other (README: lower pin on the turntable, upper pin on the boom)
                var a = tt.InverseTransformPoint(pis.position) - cyl.localPosition;
                if (a.sqrMagnitude > 1e-8f) cyl.localRotation = Quaternion.FromToRotation(cylAxisRest, a) * cylRest;
                var b = boom.InverseTransformPoint(cyl.position) - pis.localPosition;
                if (b.sqrMagnitude > 1e-8f) pis.localRotation = Quaternion.FromToRotation(pisAxisRest, b) * pisRest;
            }
            var pin = jib.TransformPoint(hookRestPos);
            var plumb = Quaternion.FromToRotation(tt.up, Vector3.up) * tt.rotation * hookRelTT;      // the hook hangs plumb (counter to the luff and the hull tilt)
            var bdir = boom.position - pin; bdir.y = 0f; bdir = bdir.sqrMagnitude > 1e-6f ? bdir.normalized : transform.forward;
            var sw = Quaternion.AngleAxis(sway, bdir);
            HookAim(plumb);                                                                          // crane_front_reach: long side along the wall, the tool leans onto it
            if (drop < 0.002f && Mathf.Abs(sway) < 0.05f && Mathf.Abs(luff) < 0.01f && Mathf.Abs(slew) < 0.01f && Mathf.Abs(hookYaw) < 0.05f && lean.sqrMagnitude < 1e-6f) { hook.localPosition = hookRestPos; hook.localRotation = hookRest; }
            else
            {
                var hv = sw * Vector3.down * drop + lean;
                var tilt = drop > 0.05f && lean.sqrMagnitude > 1e-6f ? Quaternion.FromToRotation(sw * Vector3.down, hv.normalized) : Quaternion.identity;
                hook.rotation = tilt * sw * Quaternion.AngleAxis(hookYaw, Vector3.up) * plumb; hook.position = pin + hv;
            }
            Cable(pin);
        }

        void Cable(Vector3 pin)
        {
            bool on = drop > 0.02f;
            if (!on) { if (cable) cable.enabled = false; return; }
            if (!cable)
            {
                var go = new GameObject("OZ_CraneCable") { layer = gameObject.layer };
                go.transform.SetParent(transform, false);
                cable = go.AddComponent<LineRenderer>();
                cable.useWorldSpace = true; cable.positionCount = 2; cable.widthMultiplier = 0.045f; cable.numCapVertices = 0;
                cable.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; cable.receiveShadows = false;
                if (!cableMat)
                {
                    var sh = Shader.Find("Universal Render Pipeline/Unlit"); if (!sh) sh = Shader.Find("Sprites/Default");
                    cableMat = new Material(sh) { name = "OZ_CraneCable" };
                    var c = new Color(0.07f, 0.07f, 0.075f, 1f);
                    if (cableMat.HasProperty("_BaseColor")) cableMat.SetColor("_BaseColor", c);
                    if (cableMat.HasProperty("_Color")) cableMat.SetColor("_Color", c);
                }
                cable.sharedMaterial = cableMat; cable.startColor = cable.endColor = new Color(0.07f, 0.07f, 0.075f, 1f);
            }
            cable.enabled = true;
            cable.SetPosition(0, pin); cable.SetPosition(1, hook.position);
        }

        Vector3 HookPin() => jib ? jib.TransformPoint(hookRestPos) : transform.position;
        Vector3 Tip() => hook ? hook.TransformPoint(tipLocal) : transform.position;

        // ------------------------------------------------------------------ targets
        bool BuildingBounds(ZObject b, out Bounds bounds)
        {
            bounds = default; bool any = false;
            if (game != null && game.StaticViews.TryGetValue(b.Id, out var g) && g)
                foreach (var r in g.GetComponentsInChildren<Renderer>())
                {
                    if (!r.enabled || r is ParticleSystemRenderer || r is LineRenderer || r is TrailRenderer) continue;
                    if (!any) { bounds = r.bounds; any = true; } else bounds.Encapsulate(r.bounds);
                }
            return any;
        }

        /// <summary>crane_front_reach: a work spot ON the building's real surface (<see cref="BuildingSurface"/>) that the tool can
        /// touch from where the crane stands, at any slew where the hook clears the cab (the sides and the rear first, as the score
        /// prefers the side that faces the building). Nothing in reach = the boom points at the building with the hook up
        /// (<see cref="reachable"/> false: no tool weld; RulesView's sparks show the mend on the real face).</summary>
        Vector3 PickTarget(int n)
        {
            var axis = tt.position; var b = job;
            if (b == null) return axis + transform.forward * 2.5f;
            if (TryPick(out var t, true)) return t;
            reachable = false; wallSpot = false; CraneArm.OutOfReach++;
            CraneArm.Last = $"crane#{view.Obj?.Id} -> {b.Type}#{b.Id}: out of the boom's reach ({(nearestWall < 0f ? $"no wall within {ReachWorld(LuffMin) + WallOff() + 0.4f:F1} m" : $"nearest wall {nearestWall:F1} m")} of the turntable, reach {ReachWorld(LuffMax):F2}-{ReachWorld(LuffMin):F2} m)";
            return PointAt();
        }

        /// <summary>Out of reach: a target that turns the boom toward the visible building at full reach, above the pin (solved drop 0).</summary>
        Vector3 PointAt()
        {
            var axis = tt.position; var b = job; if (b == null) return target;
            var c = bbOk ? bb.center : game.CenterWorld(b); var dir = c - axis; dir.y = 0f; dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : transform.forward;   // looks at the visible building
            var p = axis + dir * ReachWorld(LuffMin) * 0.97f;
            return new Vector3(p.x, axis.y + 10f, p.z);
        }

        /// <summary>Spot search on the building's real surface. Walls: horizontal rays from the turntable axis every 9 deg at 0.8-1.94 m
        /// over the ground; the first face hit (|n.y| &lt; 0.7) gives the spot, the pin stands <see cref="WallOff"/> off it. Floors (a
        /// bridge deck, a ruin): rays down at three radii; off the deck of a bridge = no spot; a ruin without rubble there = its
        /// terrain floor. Each candidate must <see cref="Fits"/> (reach, luff floor, cab cap, cable / boom / jib clear). commit =
        /// store the spot (surface point + normal); stay = keep the candidate nearest the current spot (a driving crane: no boom jumps).</summary>
        bool TryPick(out Vector3 pick, bool commit, bool stay = false)
        {
            pick = default;
            var b = job; if (b == null || game == null) return false;
            var bs = BuildingSurface.Of(game, b); if (bs == null || bs.Triangles == 0) { nearestWall = -1f; return false; }
            HookFit();
            var axis = tt.position; float gy = game.GroundY(axis.x, axis.z);
            float rmin = ReachWorld(LuffMax) * 1.03f, rmax = ReachWorld(LuffMin) * 0.97f, off = WallOff();
            bool floor = b.IsBridge || b.Destroyed;
            var c = bbOk ? bb.center : game.CenterWorld(b); var toB = c - axis; toB.y = 0f; toB = toB.sqrMagnitude > 1e-4f ? toB.normalized : transform.forward;
            bs.Gather(axis, rmax + off + 1f, gy - 1.5f, gy + 6f);
            cands.Clear(); nearestWall = float.MaxValue;
            for (int k = 0; k < 40; k++)
            {
                var dir = Quaternion.AngleAxis(k * 9f, Vector3.up) * Vector3.forward;
                if (!floor)
                    for (int h = 0; h < 4; h++)
                    {
                        var o = new Vector3(axis.x, gy + 0.8f + h * 0.38f, axis.z);
                        if (!bs.Ray(o, dir, rmax + off + 0.4f, out float t, out var nr)) continue;
                        var nh = new Vector3(nr.x, 0f, nr.z); if (nh.sqrMagnitude < 0.5f) continue;     // a wall, not a roof / ledge
                        nh.Normalize(); nearestWall = Mathf.Min(nearestWall, t);
                        var hit = o + dir * t; var pin = hit + nh * off; pin.y = hit.y;
                        if (!Fits(bs, pin, nh, true, rmin, rmax)) continue;
                        cands.Add(new Cand { pin = pin, surf = hit, n = nh, score = Vector3.Dot(dir, toB) });
                    }
                else
                    for (int r = 0; r < 3; r++)
                    {
                        var q = axis + dir * Mathf.Lerp(rmin + 0.15f, rmax - 0.1f, r * 0.5f);
                        var top = new Vector3(q.x, gy + 5f, q.z); Vector3 hit, nh;
                        if (bs.Ray(top, Vector3.down, 7f, out float t, out var nr) && nr.y > 0.6f) { hit = top + Vector3.down * t; nh = nr; }
                        else if (b.IsBridge) continue;                                                  // off the deck: over the water
                        else { hit = new Vector3(q.x, game.GroundY(q.x, q.z), q.z); nh = Vector3.up; }  // the ruin's floor (terrain)
                        var pin = hit + Vector3.up * 0.03f;
                        if (!Fits(bs, pin, nh, false, rmin, rmax)) continue;
                        cands.Add(new Cand { pin = pin, surf = hit, n = nh, score = Vector3.Dot(dir, toB) });
                    }
            }
            bs.Release();
            if (nearestWall == float.MaxValue) nearestWall = -1f;
            if (cands.Count == 0) return false;
            cands.Sort(byScore);                                                                       // the side that faces the building first
            int best = Mathf.Max(1, Mathf.CeilToInt(cands.Count / 3f)), i0 = Random.Range(0, best), pickI = i0;
            if (stay) { float bd = float.MaxValue; for (int j = 0; j < cands.Count; j++) { float d2 = (cands[j].surf - surf).sqrMagnitude; if (d2 < bd) { bd = d2; pickI = j; } } }
            else for (int k = 0; k < best; k++) { int j = (i0 + k) % best; if ((cands[j].surf - surf).sqrMagnitude > 0.36f) { pickI = j; break; } }   // a new spot each cycle
            var cd = cands[pickI];
            pick = cd.pin;
            if (commit)
            {
                surf = cd.surf; surfN = cd.n; wallSpot = cd.n.y < 0.6f; reachable = true;
                if (!stay) { if (wallSpot) CraneArm.WallSpots++; else CraneArm.FloorSpots++; }
            }
            return true;
        }

        /// <summary>The tool can work at `t` (pin target; y = the tip's height): within reach, the pin really gets there (luff floor
        /// over the cab), the drop is not capped by the cab / bonnet or MaxDrop, the cable can lean the tool onto a wall (<= 20 deg),
        /// and the cable, the leaned hook, the boom and the jib head stay clear of the building.</summary>
        bool Fits(BuildingSurface bs, Vector3 t, Vector3 nh, bool wall, float rmin, float rmax)
        {
            var axis = tt.position; var d = t - axis; d.y = 0f; float r = d.magnitude;
            if (r < rmin || r > rmax) return false;
            Solve(t, out float s, out float l, out float dr);
            var pin = PinWorldAt(s, l);
            var miss = pin - t; miss.y = 0f; if (miss.sqrMagnitude > 0.0064f) return false;
            if (Mathf.Abs(pin.y - TipDepth() - dr - t.y) > 0.06f) return false;
            if (bs.Blocks(pin, new Vector3(pin.x, t.y + 0.05f, pin.z))) return false;
            if (wall)
            {
                float ln = LeanDist(); if (dr * 0.36f < ln - 0.01f) return false;
                var lp = t - nh * ln;
                if (bs.Blocks(new Vector3(lp.x, t.y + Mathf.Min(0.6f, dr * 0.5f), lp.z), new Vector3(lp.x, t.y + 0.05f, lp.z))) return false;
            }
            if (bs.Blocks(tt.TransformPoint(boomPivotTT), pin)) return false;
            var rad = d / Mathf.Max(r, 1e-3f); var tan = Vector3.Cross(Vector3.up, rad);
            return !bs.Blocks(pin, pin + rad * JibNose) && !bs.Blocks(pin, pin + tan * JibHalf) && !bs.Blocks(pin, pin - tan * JibHalf);
        }

        /// <summary>World hook pin at slew / luff (the arm is put back as it was).</summary>
        Vector3 PinWorldAt(float sDeg, float lDeg)
        {
            var tl = tt.localRotation; var bl = boom.localRotation;
            tt.localRotation = Quaternion.AngleAxis(sDeg, Vector3.up) * ttRest;
            boom.localRotation = Quaternion.AngleAxis(lDeg, luffAxisTT) * boomRest;
            var pin = jib.TransformPoint(hookRestPos);
            boom.localRotation = bl; tt.localRotation = tl;
            return pin;
        }

        float TipDepth() => -tipLocal.y * Mathf.Abs(hook.lossyScale.y);
        float WallOff() => Mathf.Max(hookHalf + 0.015f, JibClear);
        float LeanDist() => Mathf.Max(0f, WallOff() - (hookHalf + 0.015f));
        /// <summary>
        /// toward the wall. The hook hangs along its cable, so the tip (TipDepth below the origin, 0.61 m) moves (dr + TipDepth) / dr as
        /// far as the origin; a LeanDist lean at the origin put the tip 0.06 m inside the wall at a 0.7 m drop and the hook's lower body
        /// into the wall at every drop. Now the lowest part of the hook stands 1.5 cm off the wall and the tip ~0.15 m from it.</summary>
        float LeanAt(float dr) => dr <= 0f ? 0f : Mathf.Min(LeanDist(), dr * 0.36f) * dr / (dr + TipDepth());

        /// <summary>The hook's half thickness across its long side (world m) and that long axis, from its mesh bounds.</summary>
        void HookFit()
        {
            if (hookHalf > 0f || !hook) return;
            var mf = hook.GetComponent<MeshFilter>(); var m = mf ? mf.sharedMesh : null; float sc = Mathf.Abs(hook.lossyScale.x);
            if (m)
            {
                var bb0 = m.bounds; bool xLong = bb0.size.x >= bb0.size.z; hookLong = xLong ? Vector3.right : Vector3.forward;
                hookHalf = (xLong ? Mathf.Max(Mathf.Abs(bb0.min.z), Mathf.Abs(bb0.max.z)) : Mathf.Max(Mathf.Abs(bb0.min.x), Mathf.Abs(bb0.max.x))) * sc;
            }
            else hookHalf = 0.053f * sc;                                                                // apc_crane_rebuild_v1 Crane_Hook: z +-0.053 GLB
            hookHalf = Mathf.Clamp(hookHalf, 0.03f, 0.35f);
        }

        /// <summary>At a wall spot the hook turns its long side along the wall and leans on its cable onto it (<see cref="LeanAt"/>,
        /// ramped in with the lowering and tied to the drop, so the tip is on the wall when the hook lands and the first weld touches);
        /// anywhere else both ease back to plumb, and the hook never stands off its pin as it comes up.</summary>
        void HookAim(Quaternion plumb)
        {
            float f = 1f - Mathf.Exp(-6f * Time.deltaTime);
            float wantYaw = 0f; var wantLean = Vector3.zero; bool atWall = false;
            if (reachable && wallSpot && job != null && (st == St.Lower || st == St.Work) && view && !view.Obj.IsMoving)
            {
                var a = plumb * hookLong; a.y = 0f; var tg = Vector3.Cross(Vector3.up, surfN);
                if (a.sqrMagnitude > 1e-6f)
                {
                    wantYaw = Vector3.SignedAngle(a, tg, Vector3.up);
                    float alt = wantYaw - 180f * Mathf.Sign(wantYaw);
                    if (Mathf.Abs(Mathf.DeltaAngle(hookYaw, alt)) < Mathf.Abs(Mathf.DeltaAngle(hookYaw, wantYaw))) wantYaw = alt;
                }
                wantLean = -surfN * LeanAt(drop) * (st == St.Lower ? S(stT / LowerTime) : 1f); atWall = true;
            }
            hookYaw = Mathf.LerpAngle(hookYaw, wantYaw, f);
            lean = atWall ? wantLean : Vector3.Lerp(lean, wantLean, f);
            float lc = LeanAt(drop); if (lean.sqrMagnitude > lc * lc) lean = lc > 0f ? lean.normalized * lc : Vector3.zero;
        }

        /// <summary>The surface under the tool tip: a short ray from the tip toward the work (along the spot's normal); a ruin's
        /// terrain floor counts. false = the tip touches nothing (no weld).</summary>
        bool Touch(Vector3 tip, out Vector3 sp, out Vector3 n)
        {
            sp = tip; n = surfN;
            var bs = job != null ? BuildingSurface.Of(game, job) : null;
            var d = -surfN;
            if (bs != null && bs.Ray(tip - d * 0.05f, d, ContactMax + 0.05f, out float t, out var nr)) { sp = tip + d * (t - 0.05f); n = nr; return true; }
            if (!wallSpot && job != null && !job.IsBridge)
            {
                float gy = game.GroundY(tip.x, tip.z), h = tip.y - gy;
                if (h <= ContactMax && h > -0.1f) { sp = new Vector3(tip.x, gy, tip.z); n = Vector3.up; return true; }
            }
            return false;
        }

        /// <summary>crane_front_reach: a driving crane (Zod drive-in) tracks its spot on the real surface twice a second (the nearest
        /// candidate that still fits, so the boom does not jump; the hook is held short while it drives); out of reach it keeps
        /// pointing at the building and the Work branch's retry re-picks (Raise > Swing > Lower, each move with its sound). A parked
        /// crane keeps its spot.</summary>
        Vector3 Retarget(Vector3 t)
        {
            if (!view.Obj.IsMoving || job == null) return t;
            if (Time.time < nextRepick) return t;
            nextRepick = Time.time + 0.5f;
            if (!reachable) return PointAt();
            return TryPick(out var rp, true, true) ? rp : t;
        }

        /// <summary>Hook pin of the boom at luff `deg`, in the turntable frame.</summary>
        Vector3 PinAt(float deg) => boomPivotTT + Quaternion.AngleAxis(deg, luffAxisTT) * (pinTT - boomPivotTT);

        float ReachWorld(float deg)
        {
            var p = PinAt(deg); p.y = 0f;
            return p.magnitude * Mathf.Abs(model.lossyScale.x);
        }

        /// <summary>Slew (deg, model frame), luff (deg) and hook drop (m) that put the tool tip on `t`.</summary>
        void Solve(Vector3 t, out float sDeg, out float lDeg, out float dropM)
        {
            var dm = model.InverseTransformVector(t - tt.position); dm.y = 0f;
            sDeg = dm.sqrMagnitude > 1e-8f ? Vector3.SignedAngle(zeroDirM, dm.normalized, Vector3.up) : slew;
            var dw = t - tt.position; dw.y = 0f; float want = dw.magnitude;
            float lo = LuffFloor(sDeg), hi = LuffMax;                                                 // over the cab: never below the rest luff
            for (int i = 0; i < 18; i++) { float mid = (lo + hi) * 0.5f; if (ReachWorld(mid) > want) lo = mid; else hi = mid; }   // reach falls as the boom rises
            lDeg = (lo + hi) * 0.5f;
            var tl = tt.localRotation; var bl = boom.localRotation;                                  // the pin at that pose
            tt.localRotation = Quaternion.AngleAxis(sDeg, Vector3.up) * ttRest;
            boom.localRotation = Quaternion.AngleAxis(lDeg, luffAxisTT) * boomRest;
            var pin = jib.TransformPoint(hookRestPos);
            boom.localRotation = bl; tt.localRotation = tl;
            float tipDepth = -tipLocal.y * Mathf.Abs(hook.lossyScale.y);
            float cap = MaxDrop;
            var pm = model.InverseTransformPoint(pin);                                                // GLB units of the Model frame
            if (pm.x + HookR > cabX0 && pm.x - HookR < cabX1 && Mathf.Abs(pm.z) < CabHalfWidth + HookR)
                cap = Mathf.Max(0f, (pm.y + tipLocal.y - (CabTop + 0.04f)) * Mathf.Abs(model.lossyScale.y));   // the tip stays 4 cm (GLB) over the cab / bonnet
            dropM = Mathf.Clamp(pin.y - tipDepth - t.y, 0f, cap);
        }

        // ------------------------------------------------------------------ weld
        void Weld(bool moving)
        {
            var fx = FxManager.I; if (!fx || job == null || !reachable) return;
            var tip = Tip();
            if (!Touch(tip, out var surfAt, out var nOut)) { if (!moving) { CraneArm.NoContact++; noContact = true; } return; }
            float gap = Vector3.Distance(surfAt, tip);
            CraneArm.TipWorkMax = Mathf.Max(CraneArm.TipWorkMax, gap);
            var sp = surfAt + nOut * 0.02f;                                                            // on the surface, under the tool tip; the spray comes off the work
            CraneArm.TipSparkMax = Mathf.Max(CraneArm.TipSparkMax, Vector3.Distance(sp, tip));
            fx.WeldSparks(sp, nOut, Random.Range(0.85f, 1.05f)); CraneArm.Welds++;
            AudioDirector.I?.Play("bld_repair_weld", sp, job.Id);
            if (Random.value < ArcChance && ElectricZaps.Arc(tip, sp, nOut, view.Obj.Id, ElectricZaps.Source.Crane)) CraneArm.Arcs++;   // the arc jumps from the tool to the work
        }
    }
}
