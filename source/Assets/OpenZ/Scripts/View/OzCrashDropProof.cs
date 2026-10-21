using System.Collections;
using System.Text;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// K408 Play proof: the shuttle's crash landing comes down as a real crash (View/OzPlayableShuttle.CrashDrop.cs), on level 16 in Play
    /// (menu OpenZ/V1.1/Shuttle: K408 Faster Crash Landing Proof, Editor/OzCrashDropMenu.cs). Counted PASS / FAIL lines, a CSV per stage of
    /// the drawn height over the ground every frame (the rules' seconds left to the contact, then the crash's own clock) and six pictures
    /// of the crash at the normal RTS camera (70 m). Set-up: the CPU teams removed, morning (day clock 0.10, held), 60 fps. Orders through
    /// the rules (SendWaypoints; the rules' proof switch for a crash landing), no desktop input. Stages:
    ///  D1 a crash landing on screen: the engines cut 0.8 s out (their sputter, two puffs), the last 6 m in 0.75 s or less, the hit at
    ///     11 m/s or more (and the slam uses that speed), the crash sound on the contact frame; then K256e's crash as before (the bounce,
    ///     the other side, the tilted rest, the scrape) and the self-righting on the rules' moment, EXIT / the ramp only once level;
    ///  D2 a NORMAL landing on screen: no cut, no crash, it sets down soft (2 m/s or less at the contact).
    /// Output: <see cref="Folder"/> (the work tree's records, milestones/faster_crash_landing_k408, found upward at run time). Test only.
    /// </summary>
    public sealed class OzCrashDropProof : MonoBehaviour
    {
        public static string Report = "idle";
        public static string Folder()
        {
            var d = new System.IO.DirectoryInfo(Application.dataPath);
            for (int i = 0; i < 8 && d != null; i++, d = d.Parent)
                if (System.IO.Directory.Exists(System.IO.Path.Combine(d.FullName, "work", "records")))
                    return System.IO.Path.Combine(d.FullName, "work", "records", "milestones", "faster_crash_landing_k408");
            return System.IO.Path.Combine(Application.persistentDataPath, "faster_crash_landing_k408");
        }
        public static void Run()
        {
            Report = "running";
#if UNITY_EDITOR
            var gv = System.Type.GetType("UnityEditor.GameView,UnityEditor"); if (gv != null) UnityEditor.EditorWindow.FocusWindowIfItsOpen(gv);
#endif
            new GameObject("OzCrashDropProof").AddComponent<OzCrashDropProof>();
        }

        ZGame g; ZWorld W; PlayerInput input; RtsCamera rts; Team me = Team.Red;
        ZObject s; OzPlayableShuttle ship;
        readonly StringBuilder sb = new StringBuilder(); int fails, passes; string dir;

        void Start()
        {
            g = ZGame.I; W = g ? g.World : null; input = FindFirstObjectByType<PlayerInput>(); rts = FindFirstObjectByType<RtsCamera>();
            dir = Folder(); System.IO.Directory.CreateDirectory(dir);
            StartCoroutine(All());
        }

        void Log(string t) { sb.AppendLine(t); Debug.Log("[OzCrashDropProof] " + t); }
        void Check(string what, bool ok) { if (ok) passes++; else fails++; Log((ok ? "PASS " : "FAIL ") + what); }
        IEnumerator WaitFor(System.Func<bool> c, float max) { float t = 0f; while (t < max && !c()) { t += Time.deltaTime; yield return null; } }
        IEnumerator Wait(float s0) { float t = 0f; while (t < s0) { t += Time.deltaTime; yield return null; } }
        void Cam(Vector3 focus, float h) { if (!rts) return; rts.FreeLookOff(); rts.focus = new Vector3(focus.x, 0f, focus.z); rts.SetHeightImmediate(h); }
        void Select(params ZObject[] os) { input.Selection.Clear(); foreach (var o in os) if (o != null) input.Selection.Add(o); }
        bool Move(int x, int y) => W.SendWaypoints(me, s, new[] { new ZWaypoint { mode = WpMode.Move, x = x, y = y, playerGiven = true } });
        void Shot(string name) { ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, name + ".png")); Log("   picture " + name + ".png"); }
        static int Played(float t0, string key) { int n = 0; foreach (var e in OzSoundLog.Since(t0)) if (e.key == key && e.outcome != null && e.outcome.StartsWith("PLAY")) n++; return n; }
        static float FirstAt(float t0, string key) { foreach (var e in OzSoundLog.Since(t0)) if (e.key == key && e.outcome != null && e.outcome.StartsWith("PLAY")) return e.t; return -1f; }
        float Over() { var p = ship.transform.position; return p.y - g.GroundY(p.x, p.z); }

        /// <summary>A free tile middle (the ship can land there now) between minR and maxR px from (cx, cy), the nearest first.</summary>
        int[] Free(int cx, int cy, int minR, int maxR)
        {
            long best = long.MaxValue; int[] r = null; int e = ZWorld.ShuttleEdgeTiles;
            for (int ty = e; ty < W.Map.height - e; ty++)
                for (int tx = e; tx < W.Map.width - e; tx++)
                {
                    int x = tx * 16 + 8, y = ty * 16 + 8; long d = (long)(x - cx) * (x - cx) + (long)(y - cy) * (y - cy);
                    if (d >= best || d < (long)minR * minR || d > (long)maxR * maxR) continue;
                    if (W.ShuttleNeverLands(x, y, s, out _) || !W.ShuttlePadFree(x, y, s, out _)) continue;
                    best = d; r = new[] { x, y };
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
            Log($"K408 faster crash landing proof, {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}, {Screen.width}x{Screen.height}, world time {W.Time:F1} s; on {OzPlayableShuttle.CrashDropOn}, the drop {OzPlayableShuttle.CrashDropSec:F2} s from {OzPlayableShuttle.CrashDropStartM:F1} m at {OzPlayableShuttle.CrashDropStartMps:F1} m/s, \"Coming in hot!\" {OzPlayableShuttle.CrashHotLeadSec:F2} s out");
            int bots = W.Bots.Count; W.Bots.Clear();
            var dn = FindFirstObjectByType<DayNight>(); if (dn) { dn.timeOfDay = 0.10f; dn.Apply(0.10f); dn.running = false; }
            Log($"set-up: CPU removed {bots}, day 0.10 held");
            Check("D0 the faster crash is on", OzPlayableShuttle.CrashDropOn);
            yield return WaitFor(() => s.ShuttleState == ZShuttleState.Landed && !s.ShuttleAirborne && s.ShuttleLevelled, 30f); yield return Wait(0.8f);
            yield return Crash();
            yield return Normal();
            Finish();
        }

        /// <summary>D1: one crash landing (the rules' proof switch), the camera on the ship at 70 m.</summary>
        IEnumerator Crash()
        {
            Log("D1 a crash landing on screen (70 m)");
            yield return WaitFor(() => s.ShuttleState == ZShuttleState.Landed && s.ShuttleLevelled && !ship.CrashOn, 30f); yield return Wait(1.0f);
            var q = Free(s.CenterX, s.CenterY, 320, 700); if (q == null) { Check("D1 a landing place", false); yield break; }
            OzPlayableShuttle.ProofRoughNext = true;
            Select(s); Move(q[0], q[1]);
            yield return WaitFor(() => s.ShuttleState == ZShuttleState.Descend, 60f);
            Cam(ship.transform.position, 70f);
            Log("   >>> OBS: a 60 fps take of this crash landing now (d1_crash)");
            float t0 = Time.unscaledTime; int cuts0 = OzPlayableShuttle.CrashDropCuts, puffs0 = OzPlayableShuttle.CrashDropPuffs, ramp0 = OzPlayableShuttle.RampWhileRighting, scr0 = OzPlayableShuttle.ScrapesLaid;
            var csv = new StringBuilder("phase,clock_s,drawn_over_ground_m,roll_deg,pitch_deg\n");
            bool sHang = false, sCut = false, sMid = false, sHit = false, sRest = false, sLevel = false, early = false, late = false, began = false;
            float contactAt = -1f, waited = 0f, overTouch = float.NaN; double levelledAt = -1;
            var dTau = new System.Collections.Generic.List<float>(); var dOver = new System.Collections.Generic.List<float>();   // the drawn hull over the ground (what the camera sees)
            while (waited < 14f)
            {
                yield return null; waited += Time.deltaTime;
                if (!began && ship.CrashOn) { began = true; contactAt = Time.unscaledTime; levelledAt = s.ShuttleLevelledAt; overTouch = Over(); }
                var e = ship.transform.eulerAngles; float roll = Mathf.DeltaAngle(0f, e.z), pitch = Mathf.DeltaAngle(0f, e.x);
                if (!began)
                {
                    float tau = (float)(s.ShuttleLift * ZWorld.ShuttleDescendSec);
                    if (s.ShuttleState == ZShuttleState.Descend) { dTau.Add(tau); dOver.Add(Over()); }
                    csv.Append("descent,").Append((-tau).ToString("F3")).Append(',').Append(Over().ToString("F3")).Append(',').Append(roll.ToString("F2")).Append(',').Append(pitch.ToString("F2")).Append('\n');
                    if (!sHang && tau <= 1.2f) { sHang = true; Shot("08a_k408_hang_wobble_h70"); }
                    if (!sCut && tau <= 0.8f) { sCut = true; Shot("08b_k408_engines_cut_h70"); }
                    if (!sMid && tau <= 0.35f) { sMid = true; Shot("08c_k408_dropping_h70"); }
                    if (s.ShuttleState != ZShuttleState.Descend && s.ShuttleState != ZShuttleState.Landed) break;
                }
                else
                {
                    float ct = ship.CrashOn ? ship.CrashT : (float)(W.Time - s.ShuttleTouchdownAt);
                    csv.Append("crash,").Append(ct.ToString("F3")).Append(',').Append(Over().ToString("F3")).Append(',').Append(roll.ToString("F2")).Append(',').Append(pitch.ToString("F2")).Append('\n');
                    if (!sHit && ct >= 0.04f) { sHit = true; Shot("08d_k408_the_hit_h70"); }
                    if (!sRest && ct >= 0.85f) { sRest = true; Shot("08e_k408_tilted_rest_h70"); }   // K426: the tilted rest is 0.75-1.0 s now
                    if (ct >= 0.05f && W.Time < levelledAt - 0.1) early |= s.ShuttleCrewSettled;   // K426: EXIT is taken from the touchdown (the ramp still waits for the level)
                    if (W.Time >= levelledAt + 0.1) { late = s.ShuttleCrewSettled; if (!sLevel) { sLevel = true; Shot("08f_k408_righted_h70"); } }
                    if (W.Time >= levelledAt + 0.6) break;
                }
            }
            OzPlayableShuttle.ProofRoughNext = false;
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "d1_crash_height.csv"), csv.ToString());
            yield return Wait(0.3f);
            if (!began) { Check("D1 the crash landing was drawn", false); yield break; }
            float slamAt = FirstAt(t0, "wreck_crash"), cutAt = FirstAt(t0, "veh_engine_shutdown");
            Log(OzShuttleTouchdown.Log.ToString().TrimEnd());
            Log($"   D1: the engines cut {ship.CrashDropCutTau:F2} s out (sputter {Played(t0, "veh_engine_shutdown")}, whoosh {Played(t0, "shuttle_strafe_whoosh")}, puffs {OzPlayableShuttle.CrashDropPuffs - puffs0}), the hang sank at most {ship.CrashDropHangMaxMps:F2} m/s, the last 6 m in {ship.CrashDropLast6mSec:F2} s, drawn {ship.CrashDropImpactMps:F2} m/s just before the contact (the curve's {ship.CrashDropModelMps:F2}), the slam used {OzShuttleTouchdown.FirstSlam:F2} m/s; wreck_crash {(slamAt > 0f ? (slamAt - contactAt).ToString("+0.000;-0.000") + " s from the contact frame" : "NOT played")}; the crash's numbers: bounce {ship.CrashHopSeen:F2} m, the other side at {ship.CrashHighAtSeen:F3} s (frame before {ship.CrashHighPrevT:F3}), rest {ship.CrashRestTiltSeen:F1} deg held {ship.CrashRestHeldSec:F2} s, level {OzShuttleTouchdown.LevelOff:F3} s off, scrape {ship.CrashScrapeLen:F1} m, worst pad {OzShuttleTouchdown.WorstPadIn * 100f:F1} cm in; EXIT while righting {early}, after {late}; ramp while righting {OzPlayableShuttle.RampWhileRighting - ramp0} frames");
            Check($"D1 the hit is hard: {ship.CrashDropImpactMps:F1} m/s just before the contact (>= 11; K256e's was 5) and the slam uses it ({OzShuttleTouchdown.FirstSlam:F1})",
                  ship.CrashDropImpactMps >= 11f && OzShuttleTouchdown.FirstSlam >= 11f && Mathf.Abs(OzShuttleTouchdown.FirstSlam - ship.CrashDropImpactMps) < 0.6f);
            Check($"D1 the fall accelerates: the last 6 m in {ship.CrashDropLast6mSec:F2} s (<= 0.75; K256e's 1.07), the engines cut {ship.CrashDropCutTau:F2} s out (0.75-0.85), the hang never sank faster than half the hit ({ship.CrashDropHangMaxMps:F1} m/s)",
                  ship.CrashDropLast6mSec > 0f && ship.CrashDropLast6mSec <= 0.75f && ship.CrashDropCutTau >= 0.75f && ship.CrashDropCutTau <= 0.85f && ship.CrashDropHangMaxMps <= 0.5f * ship.CrashDropImpactMps);
            Check($"D1 the cut has its sound and its smoke (sputter {Played(t0, "veh_engine_shutdown")}, puffs {OzPlayableShuttle.CrashDropPuffs - puffs0}, cuts {OzPlayableShuttle.CrashDropCuts - cuts0})",
                  Played(t0, "veh_engine_shutdown") >= 1 && OzPlayableShuttle.CrashDropPuffs - puffs0 >= 2 && OzPlayableShuttle.CrashDropCuts - cuts0 == 1 && cutAt > 0f);
            // the DRAWN hull (bob, let-go height and all): its mean sink over the last 0.15 s before the contact and its last 6 m
            float visV = -1f, vis6 = -1f;
            if (dTau.Count > 2 && !float.IsNaN(overTouch))
            {
                int n = dTau.Count - 1, k = n; while (k > 0 && dTau[k] < dTau[n] + 0.15f) k--;
                if (dTau[k] > dTau[n] + 1e-3f) visV = (dOver[k] - dOver[n]) / (dTau[k] - dTau[n]);
                for (int i = 0; i <= n; i++) if (dOver[i] - overTouch <= 6f) { vis6 = dTau[i]; break; }
            }
            Check($"D1 the drawn hull falls hard too: mean sink {visV:F1} m/s over the last 0.15 s (>= 9; K256e's about 3), its last 6 m in {vis6:F2} s (<= 0.80; K256e's about 1.0)",
                  visV >= 9f && vis6 > 0f && vis6 <= 0.80f);
            Check($"D1 the crash sound on the hit ({(slamAt > 0f ? (slamAt - contactAt).ToString("+0.000;-0.000") : "none")} s from the contact frame, within 0.05)", slamAt > 0f && Mathf.Abs(slamAt - contactAt) <= 0.05f);
            Check($"D1 K256e's crash as before: bounce {ship.CrashHopSeen:F2} m (0.25-0.45), the other side at {ship.CrashHighAtSeen:F3} s (0.30-0.35), rest {ship.CrashRestTiltSeen:F1} deg (13-14) for {ship.CrashRestHeldSec:F2} s (>= 0.8; K426: 1.5 before the shorter rest), scrape {ship.CrashScrapeLen:F1} m (5-7), no pad in the ground",
                  ship.CrashHopSeen >= 0.25f && ship.CrashHopSeen <= 0.45f && ship.CrashHighAtSeen >= 0.30f && (ship.CrashHighAtSeen <= 0.35f || ship.CrashHighPrevT < 0.35f)
                  && ship.CrashRestTiltSeen >= 13f && ship.CrashRestTiltSeen <= 14f && ship.CrashRestHeldSec >= 0.8f && OzPlayableShuttle.ScrapesLaid > scr0 && ship.CrashScrapeLen >= 5f && ship.CrashScrapeLen <= 7f && OzShuttleTouchdown.WorstPadIn <= 0.031f);
            Check($"D1 it rights itself on the rules' moment ({OzShuttleTouchdown.LevelOff:F3} s off, <= 0.05); K426: EXIT is taken while it crashes and rights itself (offered {early}, after {late}) and the ramp waits until it stands level (it moved while righting {OzPlayableShuttle.RampWhileRighting - ramp0} frames)",
                  OzShuttleTouchdown.LevelOff >= 0f && OzShuttleTouchdown.LevelOff <= 0.05f && early && late && OzPlayableShuttle.RampWhileRighting == ramp0);
        }

        /// <summary>D2: a normal landing on screen: no cut, no crash, a soft set-down.</summary>
        IEnumerator Normal()
        {
            Log("D2 a NORMAL landing on screen (70 m): no cut, no crash, it sets down soft");
            for (int tryN = 0; tryN < 4; tryN++)
            {
                yield return WaitFor(() => s.ShuttleState == ZShuttleState.Landed && s.ShuttleLevelled && !ship.CrashOn, 30f); yield return Wait(1.0f);
                var q = Free(s.CenterX, s.CenterY, 320, 700); if (q == null) { Check("D2 a landing place", false); yield break; }
                OzPlayableShuttle.ProofRoughNext = false;
                Select(s); Move(q[0], q[1]);
                yield return WaitFor(() => s.ShuttleState == ZShuttleState.Descend, 60f);
                if (s.ShuttleRoughLanding) { Log("   the rules made this one a crash landing: another flight"); yield return WaitFor(() => s.ShuttleState == ZShuttleState.Landed && s.ShuttleLevelled && !ship.CrashOn, 30f); continue; }
                Cam(ship.transform.position, 70f);
                int cuts0 = OzPlayableShuttle.CrashDropCuts, l0 = ship.Landings; bool crash = false;
                var csv = new StringBuilder("rules_s_left,drawn_over_ground_m\n");
                float prevH = -1f, prevTau = -1f, lastV = 0f;
                while (s.ShuttleState == ZShuttleState.Descend)
                {
                    float tau = (float)(s.ShuttleLift * ZWorld.ShuttleDescendSec), h = Over();
                    csv.Append(tau.ToString("F3")).Append(',').Append(h.ToString("F3")).Append('\n');
                    if (prevTau > tau + 1e-4f && tau < 0.15f) lastV = (prevH - h) / (prevTau - tau);
                    prevH = h; prevTau = tau; crash |= ship.CrashOn;
                    yield return null;
                }
                yield return Wait(1.5f); crash |= ship.CrashOn;
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "d2_normal_height.csv"), csv.ToString());
                Log($"   D2: landed {ship.Landings > l0}, crash drawn {crash}, cuts {OzPlayableShuttle.CrashDropCuts - cuts0}, drawn sink on its last frame before the contact {lastV:F2} m/s");
                Check($"D2 a normal landing is unchanged: no crash, no engine cut, a soft set-down ({lastV:F2} m/s on its last frame, <= 2)", ship.Landings > l0 && !crash && OzPlayableShuttle.CrashDropCuts == cuts0 && lastV <= 2f);
                yield break;
            }
            Check("D2 a normal landing came (4 tries)", false);
        }

        void Finish()
        {
            OzPlayableShuttle.ProofRoughNext = false;
            Log($"K408 faster crash landing proof: {passes} PASS, {fails} FAIL");
            Report = fails == 0 ? $"PASS {passes}" : $"FAIL {fails} of {passes + fails}";
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "crash_drop_proof_log.txt"), sb.ToString());
            Destroy(gameObject);
        }
    }
}
