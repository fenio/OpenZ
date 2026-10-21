using System.Collections;
using System.Text;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// K388 Play proof: the shuttle's landing legs fold in under the belly in flight and come down for the landing (rule DV118, view only), on level 16 in Play
    /// (menu OpenZ/V1.1/Shuttle: K388 Legs Fold Proof, Editor/OzShuttleLegsFoldMenu.cs). Counted PASS / FAIL lines and full-screen pictures
    /// from the normal RTS camera, uncropped. Set-up: the CPU teams removed, morning (day clock 0.10, held), 60 fps. Orders through the rules
    /// (SendWaypoints), no desktop input. Stages:
    ///  F0 the rig: the suspension's rig, every strut cut at its red pod's underside on all four legs (the whole visible leg a part of its
    ///     own, the pod closed by its cap and inner sleeve, the leg's cut capped) and at the ankle (the rod's tip, both cuts capped);
    ///  F1 TAKE-OFF at 70 m (a 10-frame strip): the fold starts once the pads are 1.5 m up, folded 1.0 .. 2.3 s after the lift-off, the whine
    ///     and the clunk heard; folded = every hinge at its full swing and every ankle at its full tuck, the pads <see cref="PadInMin"/> ..
    ///     <see cref="PadInMax"/> m nearer the belly's centre than parked, lying flat (tilt at most <see cref="PadTiltMax"/> deg), the
    ///     pistons closed;
    ///  F2 CRUISE: folded all the way (sampled 3 s), a 35 m picture in F3b's flight;
    ///  F3 DESCENT + TOUCHDOWN at 70 m (a 10-frame strip) and F3b at 35 m: the unfold starts in the descent, down and locked 0.3 s or more
    ///     before the touchdown, the fold 0 at the touchdown (the suspension's exact landing pose: its pistons out 0.30 .. 0.48 m), the whine
    ///  F4 HOVER (the orders set to ANYWHERE, over water: it cannot land): folded all the way (sampled 3 s);
    ///  F5 a CRASH landing (the rules' proof switch): the legs come down at once and fast, locked 0.8 s or more before the slam.
    /// Output: <see cref="Folder"/> (the work tree's records, milestones/shuttle_legs_fold_k388, found upward at run time). Test only.
    /// </summary>
    public sealed class OzShuttleLegsFoldProof : MonoBehaviour
    {
        public static string Report = "idle";
        /// <summary>F1's limits for the folded pads (the twin's numbers with a margin: tools/penetration.py, out/penetration.txt).</summary>
        public const float PadInMin = 1.55f, PadInMax = 2.10f, PadTiltMax = 12f;
        public static string Folder()
        {
            var d = new System.IO.DirectoryInfo(Application.dataPath);
            for (int i = 0; i < 8 && d != null; i++, d = d.Parent)
                if (System.IO.Directory.Exists(System.IO.Path.Combine(d.FullName, "work", "records")))
                    return System.IO.Path.Combine(d.FullName, "work", "records", "milestones", "shuttle_legs_fold_k388");
            return System.IO.Path.Combine(Application.persistentDataPath, "shuttle_legs_fold_k388");
        }
        public static void Run()
        {
            Report = "running";
#if UNITY_EDITOR
            var gv = System.Type.GetType("UnityEditor.GameView,UnityEditor"); if (gv != null) UnityEditor.EditorWindow.FocusWindowIfItsOpen(gv);
#endif
            new GameObject("OzShuttleLegsFoldProof").AddComponent<OzShuttleLegsFoldProof>();
        }

        ZGame g; ZWorld W; PlayerInput input; RtsCamera rts; Team me = Team.Red;
        ZObject s; OzPlayableShuttle ship;
        readonly StringBuilder sb = new StringBuilder(); int fails, passes, frames; string dir; float clock;

        void Start()
        {
            g = ZGame.I; W = g ? g.World : null; input = FindFirstObjectByType<PlayerInput>(); rts = FindFirstObjectByType<RtsCamera>();
            dir = Folder(); System.IO.Directory.CreateDirectory(dir);
            StartCoroutine(All());
        }

        void Log(string t) { sb.AppendLine(t); Debug.Log("[OzShuttleLegsFoldProof] " + t); }
        void Check(string what, bool ok) { if (ok) passes++; else fails++; Log((ok ? "PASS " : "FAIL ") + what); }
        void Shot(string name) { ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, name + ".png")); frames++; Log($"frame {name}.png (t {clock:F2} s, {s.ShuttleState}, fold {ship.LegsFoldNow:F2}, hinge {ship.LegsHingeNow(0):F1} deg, ankle {ship.LegsAnkleNow(0):F1} deg, pad in {ship.LegsPadIn(0):F3} m, pad tilt {ship.LegsPadTilt(0):F1} deg, piston {ship.SuspGap(0):F3} m)"); }
        IEnumerator WaitFor(System.Func<bool> c, float max) { float t = 0f; while (t < max && !c()) { t += Time.deltaTime; yield return null; } }
        IEnumerator Wait(float s0) { float t = 0f; while (t < s0) { t += Time.deltaTime; yield return null; } }
        void Cam(Vector3 focus, float h) { if (!rts) return; rts.FreeLookOff(); rts.focus = new Vector3(focus.x, 0f, focus.z); rts.SetHeightImmediate(h); }
        void Select(params ZObject[] os) { input.Selection.Clear(); foreach (var o in os) if (o != null) input.Selection.Add(o); }
        bool Move(int x, int y) => W.SendWaypoints(me, s, new[] { new ZWaypoint { mode = WpMode.Move, x = x, y = y, playerGiven = true } });
        static int Played(float t0, string key) { int n = 0; foreach (var e in OzSoundLog.Since(t0)) if (e.key == key && e.outcome != null && (e.outcome.StartsWith("PLAY") || e.outcome.StartsWith("LOOP"))) n++; return n; }
        float Tau => (float)s.ShuttleLift * (float)ZWorld.ShuttleDescendSec;

        /// <summary>A tile middle between minR and maxR px from (cx, cy), the nearest first: free to land on (why ""), or one it never lands
        /// on for that reason (why "water").</summary>
        int[] Place(string why, int cx, int cy, int minR, int maxR)
        {
            long best = long.MaxValue; int[] r = null; int e = ZWorld.ShuttleEdgeTiles;
            for (int ty = e; ty < W.Map.height - e; ty++)
                for (int tx = e; tx < W.Map.width - e; tx++)
                {
                    int x = tx * 16 + 8, y = ty * 16 + 8; long d = (long)(x - cx) * (x - cx) + (long)(y - cy) * (y - cy);
                    if (d >= best || d < (long)minR * minR || d > (long)maxR * maxR) continue;
                    bool never = W.ShuttleNeverLands(x, y, s, out string wy);
                    if (why == "" ? (!never && W.ShuttlePadFree(x, y, s, out _)) : (never && wy == why)) { best = d; r = new[] { x, y }; }
                }
            return r;
        }

        IEnumerator All()
        {
            Application.targetFrameRate = 60;
            if (W == null || input == null) { Log("no game (level 16 in Play)"); fails++; Finish(); yield break; }
            if (!ZWorld.ShuttlePlayableDeviation || W.PlayableShuttle == null) { Log("no playable shuttle (level 16, the shuttle rule on, the option SHUTTLE on)"); fails++; Finish(); yield break; }
            me = g.player; s = W.PlayableShuttle; ship = OzPlayableShuttle.Of(s);
            if (!ship) { Log("the shuttle has no view"); fails++; Finish(); yield break; }
            Log($"K388 legs fold proof, {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}, {Screen.width}x{Screen.height}, world time {W.Time:F1} s");
            int bots = W.Bots.Count; W.Bots.Clear();
            var dn = FindFirstObjectByType<DayNight>(); if (dn) { dn.timeOfDay = 0.10f; dn.Apply(0.10f); dn.running = false; }
            yield return WaitFor(() => s.ShuttleState == ZShuttleState.Landed && !s.ShuttleAirborne && s.ShuttleLevelled, 30f); yield return Wait(0.8f);
            Log($"set-up: CPU removed {bots}, day 0.10 held; the suspension's rig {ship.SuspRigged}, the legs' fold ready {ship.LegsFoldReady} (fits {OzPlayableShuttle.LegsFitFails} failed); numbers: cut at y {OzPlayableShuttle.LegsCutY[0]:F2} / {OzPlayableShuttle.LegsCutY[1]:F2} / {OzPlayableShuttle.LegsCutY[2]:F2} / {OzPlayableShuttle.LegsCutY[3]:F2} m, hinge {OzPlayableShuttle.LegsHingeDeg[0]:F0} / {OzPlayableShuttle.LegsHingeDeg[1]:F0} / {OzPlayableShuttle.LegsHingeDeg[2]:F0} / {OzPlayableShuttle.LegsHingeDeg[3]:F0} deg, ankle {OzPlayableShuttle.LegsAnkleDeg[0]:F0} / {OzPlayableShuttle.LegsAnkleDeg[1]:F0} / {OzPlayableShuttle.LegsAnkleDeg[2]:F0} / {OzPlayableShuttle.LegsAnkleDeg[3]:F0} deg, fold {OzPlayableShuttle.LegsFoldSec:F2} s, unfold {OzPlayableShuttle.LegsUnfoldSec:F2} s at {OzPlayableShuttle.LegsUnfoldAtSec:F2} s before the touchdown");
            bool caps = true; var capLine = new StringBuilder();
            for (int i = 0; i < 4; i++) { caps &= ship.LegsCutLoopsOf(i) >= 1 && ship.LegsPodCapTris(i) > 0 && ship.LegsLegCapTris(i) > 0 && ship.LegsSleeveTris(i) > 0 && ship.LegsAnkleCapTris(i) > 0; capLine.Append($" leg {i}: {ship.LegsCutLoopsOf(i)} loop(s), pod cap {ship.LegsPodCapTris(i)} / sleeve {ship.LegsSleeveTris(i)} / leg cap {ship.LegsLegCapTris(i)} / ankle cap {ship.LegsAnkleCapTris(i)} tris;"); }
            Log("   F0:" + capLine);
            Check("F0 the rig: every strut cut at its red pod's underside on all four legs (the pod closed by its cap and sleeve, the leg's cut capped; no fallback), standing it is down (fold 0)", ship.SuspRigged && ship.LegsFoldReady && OzPlayableShuttle.LegsFitFails == 0 && caps && ship.LegsFoldNow == 0f);
            yield return Flight(70f, "f1_f3_h70", true);
            yield return Flight(35f, "f3b_h35", false);
            yield return Hover();
            yield return Crash();
            Finish();
        }

        // ------------------------------------------------------------------ F1 + F2 + F3: a take-off, the cruise, the descent and touchdown
        IEnumerator Flight(float camH, string tag, bool full)
        {
            for (int tryN = 0; tryN < 4; tryN++)
            {
                yield return WaitFor(() => s.ShuttleState == ZShuttleState.Landed && s.ShuttleLevelled && !ship.CrashOn, 30f); yield return Wait(1.0f);
                var q = Place("", s.CenterX, s.CenterY, 480, 900); if (q == null) { Check("F1 a landing place 30 .. 56 m away", false); yield break; }
                Cam(ship.transform.position, camH); yield return Wait(0.3f);
                float t0 = Time.unscaledTime; int f0 = OzPlayableShuttle.LegsFolds, st0 = OzPlayableShuttle.LegsStowedN, u0 = OzPlayableShuttle.LegsUnfolds, lk0 = OzPlayableShuttle.LegsLockedN;
                clock = -0.1f; if (full) Shot($"{tag}_00_parked");
                Select(s); Move(q[0], q[1]);
                yield return WaitFor(() => s.ShuttleAirborne, 15f);
                // F1: the take-off strip (lift-off, 0.4 s, the fold's start, its middle, stowed, 0.5 s on)
                clock = 0f; bool shotStart = false, shotMid = false, shotStow = false; float stowAt = -1f, inMax = 0f; int k = 0; var times = new[] { 0.0f, 0.4f };
                while (clock < 4.0f)
                {
                    if (full && k < times.Length && clock >= times[k]) { Cam(ship.transform.position, camH); Shot($"{tag}_{1 + k:00}_takeoff_+{times[k]:F1}s"); k++; }
                    if (full && !shotStart && ship.LegsFoldDir > 0) { shotStart = true; Shot($"{tag}_03_fold_starts"); }
                    if (full && !shotMid && ship.LegsFoldNow >= 0.5f) { shotMid = true; Shot($"{tag}_04_fold_half"); }
                    if (!shotStow && ship.LegsFoldNow >= 1f) { shotStow = true; stowAt = clock; Shot(full ? $"{tag}_05_folded" : $"{tag}_05_cruise_folded"); for (int i = 0; i < 4; i++) inMax = Mathf.Max(inMax, ship.LegsPadIn(i)); }
                    clock += Time.deltaTime; yield return null;
                }
                float inMin = 9f, tiltMax = 0f; bool full4 = true; for (int i = 0; i < 4; i++) { inMin = Mathf.Min(inMin, ship.LegsPadIn(i)); tiltMax = Mathf.Max(tiltMax, ship.LegsPadTilt(i)); full4 &= Mathf.Abs(ship.LegsHingeNow(i) - OzPlayableShuttle.LegsHingeDeg[i]) < 0.01f && Mathf.Abs(ship.LegsAnkleNow(i) - OzPlayableShuttle.LegsAnkleDeg[i]) < 0.01f; }
                if (full)
                {
                    Shot($"{tag}_06_cruise_folded");
                    Log($"   F1: lift-off -> fold started at {OzPlayableShuttle.LegsFolds - f0} fold(s); folded {OzPlayableShuttle.LegsStowedAfterLiftSec:F2} s after the lift-off (proof clock {stowAt:F2} s); hinges / ankles at full turn {full4}; the pads nearer the belly's centre {inMin:F3} .. {inMax:F3} m, tilted at most {tiltMax:F1} deg; whine {Played(t0, "shuttle_leg_retract")}, clunk {Played(t0, "shuttle_leg_thud")}");
                    Check("F1 take-off: the legs fold smoothly once the pads are 1.5 m up: fully folded 1.0 .. 2.3 s after the lift-off, one fold", OzPlayableShuttle.LegsFolds == f0 + 1 && OzPlayableShuttle.LegsStowedN == st0 + 1 && OzPlayableShuttle.LegsStowedAfterLiftSec >= 1.0f && OzPlayableShuttle.LegsStowedAfterLiftSec <= 2.3f);
                    Check($"F1 folded flat under the belly: every hinge and ankle at its full turn, the pads {PadInMin:F2} .. {PadInMax:F2} m nearer the belly's centre than parked and lying flat (tilt <= {PadTiltMax:F0} deg), the pistons closed (the twin's pose, penetration-free in every frame)", full4 && inMin >= PadInMin && inMax <= PadInMax && tiltMax <= PadTiltMax && ship.SuspGap(0) <= 0.002f && ship.SuspGap(3) <= 0.002f);
                    Check("F1 the hydraulic whine and the clunk play with the fold (every visible action has its sound)", Played(t0, "shuttle_leg_retract") >= 1 && Played(t0, "shuttle_leg_thud") >= 1);
                    // F2: the cruise
                    float minPh = 9f, t2 = 0f; while (t2 < 3f && s.ShuttleState != ZShuttleState.Descend) { minPh = Mathf.Min(minPh, ship.LegsFoldNow); t2 += Time.deltaTime; yield return null; }
                    Check($"F2 cruise: the legs stay folded (the least fold over {t2:F1} s: {minPh:F2})", t2 >= 1f && minPh >= 0.999f);
                }
                yield return WaitFor(() => s.ShuttleState == ZShuttleState.Descend, 90f);
                if (s.ShuttleRoughLanding) { Log("   the rules made this one a crash landing: another flight"); yield return WaitFor(() => s.ShuttleState == ZShuttleState.Landed && s.ShuttleLevelled, 30f); continue; }
                Cam(ship.transform.position, camH);
                Log($"   >>> OBS: a 60 fps take of this descent and landing now ({tag})");
                float t3 = Time.unscaledTime; int l0 = OzPlayableShuttle.SuspLandings;
                clock = -Tau; Shot($"{tag}_07_descent_starts_folded");
                bool sU = false, sH = false, sL = false; float phTouch = -1f, waited = 0f;
                while (OzPlayableShuttle.SuspLandings == l0 && waited < 20f)
                {   // the touchdown, or give up after 20 s (no landing drawn on the rig: the checks below fail, the proof goes on)
                    clock = -Tau; waited += Time.deltaTime;
                    if (!sU && ship.LegsFoldDir < 0) { sU = true; Shot($"{tag}_08_unfold_starts"); }
                    if (sU && !sH && ship.LegsFoldNow <= 0.5f) { sH = true; Shot($"{tag}_09_unfold_half"); }
                    if (!sL && OzPlayableShuttle.LegsLockedN > lk0) { sL = true; Shot($"{tag}_10_down_and_locked"); }
                    yield return null;
                }
                phTouch = ship.LegsFoldNow; clock = 0f; Shot($"{tag}_11_touchdown");
                yield return Wait(1.6f); Shot($"{tag}_12_settled");
                string S = full ? "F3" : "F3b";
                Log($"   {S}: unfolds {OzPlayableShuttle.LegsUnfolds - u0}, down and locked {OzPlayableShuttle.LegsLockedBeforeTouchSec:F2} s before the touchdown (guard catches {OzPlayableShuttle.LegsGuardN}); the fold at the touchdown {phTouch:F3}; the pistons out {OzPlayableShuttle.SuspPistonAirSeen:F3} m at the touchdown; whine {Played(t3, "shuttle_leg_retract")}, clunk {Played(t3, "shuttle_leg_thud")}");
                Check($"{S} descent ({camH:F0} m): the legs come down smoothly and are fully down 0.3 s or more before the pads touch (one unfold)", OzPlayableShuttle.LegsLockedN == lk0 + 1 && OzPlayableShuttle.LegsLockedBeforeTouchSec >= 0.3f && OzPlayableShuttle.LegsUnfolds >= u0 + 1);
                Check($"{S} the touchdown starts from the suspension's exact landing pose (the fold 0, its pistons out 0.30 .. 0.48 m)", phTouch == 0f && OzPlayableShuttle.SuspPistonAirSeen >= 0.30f && OzPlayableShuttle.SuspPistonAirSeen <= 0.48f && OzPlayableShuttle.SuspLandings == l0 + 1);
                Check($"{S} the hydraulic whine and the clunk play with the unfold", Played(t3, "shuttle_leg_retract") >= 1 && Played(t3, "shuttle_leg_thud") >= 1);
                yield break;
            }
            Check("F1 / F3 a normal landing in 4 flights", false);
        }

        // ------------------------------------------------------------------ F4: hovering where it cannot land
        IEnumerator Hover()
        {
            Log("F4 hover: the orders set to ANYWHERE, a flight over water (it cannot land: it hovers)");
            yield return WaitFor(() => s.ShuttleState == ZShuttleState.Landed && s.ShuttleLevelled && !ship.CrashOn, 30f); yield return Wait(1.0f);
            var wat = Place("water", s.CenterX, s.CenterY, 160, 1400); if (wat == null) { Check("F4 water within 87 m to hover over", false); yield break; }
            int was = OzShuttleOrdersOption.Setting; OzShuttleOrdersOption.Setting = OzShuttleOrdersOption.Anywhere;
            Select(s); bool took = Move(wat[0], wat[1]);
            yield return WaitFor(() => s.ShuttleState == ZShuttleState.Hover, 60f);
            if (s.ShuttleState == ZShuttleState.Hover)
            {
                Cam(ship.transform.position, 70f); yield return Wait(0.5f); clock = 0f; Shot("f4_h70_hover_folded");
                float minPh = 9f, t = 0f; while (t < 3f && s.ShuttleState == ZShuttleState.Hover) { minPh = Mathf.Min(minPh, ship.LegsFoldNow); t += Time.deltaTime; yield return null; }
                Log($"   F4: order taken {took}, hovering ({s.ShuttleNoLand}); the least fold over {t:F1} s: {minPh:F2}");
                Check("F4 hovering over a spot it cannot land: the legs stay folded", t >= 2.9f && minPh >= 0.999f);
            }
            else Check($"F4 the hover over water was staged (order taken {took}, now {s.ShuttleState})", false);
            OzShuttleOrdersOption.Setting = was;
            var back = Place("", s.CenterX, s.CenterY, 160, 900); if (back != null) { Select(s); Move(back[0], back[1]); yield return WaitFor(() => s.ShuttleState == ZShuttleState.Landed && s.ShuttleLevelled, 60f); }
        }

        // ------------------------------------------------------------------ F5: a crash landing
        IEnumerator Crash()
        {
            Log("F5 a crash landing (the rules' proof switch) at 70 m: the legs drop at once and fast, down before the slam");
            yield return WaitFor(() => s.ShuttleState == ZShuttleState.Landed && s.ShuttleLevelled && !ship.CrashOn, 30f); yield return Wait(1.0f);
            var q = Place("", s.CenterX, s.CenterY, 480, 900); if (q == null) { Check("F5 a landing place", false); yield break; }
            OzPlayableShuttle.ProofRoughNext = true; int lk0 = OzPlayableShuttle.LegsLockedN; float t0 = Time.unscaledTime;
            Select(s); Move(q[0], q[1]);
            yield return WaitFor(() => s.ShuttleState == ZShuttleState.Descend, 60f);
            Cam(ship.transform.position, 70f); clock = -Tau; Shot("f5_h70_00_descent_starts");
            yield return WaitFor(() => OzPlayableShuttle.LegsLockedN > lk0, 3f); clock = -Tau; Shot("f5_h70_01_down_and_locked");
            yield return WaitFor(() => ship.CrashOn, 5f); clock = 0f; Shot("f5_h70_02_the_slam");
            Log($"   F5: rough {s.ShuttleRoughLanding}, locked {OzPlayableShuttle.LegsLockedBeforeTouchSec:F2} s before the slam; whine {Played(t0, "shuttle_leg_retract")}, clunk {Played(t0, "shuttle_leg_thud")}");
            Check("F5 a crash descent: the legs come down at once and fast, locked 0.8 s or more before the slam (the crash pose takes it on its legs), whine and clunk heard", s.ShuttleRoughLanding && OzPlayableShuttle.LegsLockedN == lk0 + 1 && OzPlayableShuttle.LegsLockedBeforeTouchSec >= 0.8f && Played(t0, "shuttle_leg_retract") >= 1 && Played(t0, "shuttle_leg_thud") >= 1);
            OzPlayableShuttle.ProofRoughNext = false;
            yield return WaitFor(() => s.ShuttleState == ZShuttleState.Landed && s.ShuttleLevelled, 30f);
        }

        void Finish()
        {
            OzPlayableShuttle.ProofRoughNext = false;
            Log($"K388 legs fold proof: {passes} PASS, {fails} FAIL, {frames} pictures");
            Report = fails == 0 ? $"PASS {passes}" : $"FAIL {fails} of {passes + fails}";
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "legs_fold_proof_log.txt"), sb.ToString());
            Destroy(gameObject);
        }
    }
}
