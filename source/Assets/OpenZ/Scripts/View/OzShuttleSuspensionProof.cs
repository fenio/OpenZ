using System.Collections;
using System.Collections.Generic;
using System.Text;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// K256g3 Play proof: the shuttle's visible leg suspension (rule DV118, view only; owner items 334 + 367), on level 16 in Play (menu
    /// OpenZ/V1.1/Shuttle: K256g3 Suspension Proof, Editor/OzShuttleSuspensionMenu.cs). Counted PASS / FAIL lines and full-screen pictures
    /// from the normal RTS camera, uncropped: 10-frame strips of each landing (0.3 s before the touchdown, the touchdown, +0.1, +0.2, +0.3 s,
    /// the deepest point, +0.62 s, the rebound's top, +1.1 s, settled). Set-up: the CPU teams removed, morning (day clock 0.10, held), 60 fps.
    /// Orders are given through the rules (SendWaypoints, the R key's repair order), no desktop input. Stages:
    ///  S1 a normal landing at 70 m, S2 one at 35 m: the hull's whole sink, its overshoot and rebound, the settle, the rock, the struts' splay
    ///     and slide, the pistons out in the air, the planted pads (slide and sink measured on the drawn feet), the hiss, the thud, the creak,
    ///  S3 a take-off at 70 m: the pads stay down while the legs unload (s), the hiss and the clunk;
    ///  S4 a crash landing (the rules' proof switch) at 70 m: K256e's crash as before (its own checks), the legs locked as they touched,
    ///     the level settle's dip;
    ///  S5 the repair station's deck at 70 m (half health, R): the same landing on the deck, the pads on the deck (6 cm), no part in it.
    /// Output: <see cref="Folder"/> (the work tree's records, milestones/shuttle_suspension_k256g3, found upward at run time). Test only.
    /// </summary>
    public sealed class OzShuttleSuspensionProof : MonoBehaviour
    {
        public static string Report = "idle";
        public static string Folder()
        {
            var d = new System.IO.DirectoryInfo(Application.dataPath);
            for (int i = 0; i < 8 && d != null; i++, d = d.Parent)
                if (System.IO.Directory.Exists(System.IO.Path.Combine(d.FullName, "work", "records")))
                    return System.IO.Path.Combine(d.FullName, "work", "records", "milestones", "shuttle_suspension_k256g3");
            return System.IO.Path.Combine(Application.persistentDataPath, "shuttle_suspension_k256g3");
        }
        public static void Run()
        {
            Report = "running";
#if UNITY_EDITOR
            var gv = System.Type.GetType("UnityEditor.GameView,UnityEditor"); if (gv != null) UnityEditor.EditorWindow.FocusWindowIfItsOpen(gv);
#endif
            new GameObject("OzShuttleSuspensionProof").AddComponent<OzShuttleSuspensionProof>();
        }

        ZGame g; ZWorld W; PlayerInput input; RtsCamera rts; Team me = Team.Red;
        ZObject s; OzPlayableShuttle ship;
        readonly StringBuilder sb = new StringBuilder(); int fails, passes, frames; string dir;
        static readonly Vector3 HullTop = new Vector3(0f, 5.1f, 0.4f);

        void Start()
        {
            g = ZGame.I; W = g ? g.World : null; input = FindFirstObjectByType<PlayerInput>(); rts = FindFirstObjectByType<RtsCamera>();
            dir = Folder(); System.IO.Directory.CreateDirectory(dir);
            StartCoroutine(All());
        }

        void Log(string t) { sb.AppendLine(t); Debug.Log("[OzShuttleSuspensionProof] " + t); }
        void Check(string what, bool ok) { if (ok) passes++; else fails++; Log((ok ? "PASS " : "FAIL ") + what); }
        void Shot(string name) { ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, name + ".png")); frames++; Log($"frame {name}.png (t {sinceTouch:F2} s, hull over parked {ship.SuspXNow:F3} m, mode {ship.SuspMode})"); }
        IEnumerator WaitFor(System.Func<bool> c, float max) { float t = 0f; while (t < max && !c()) { t += Time.deltaTime; yield return null; } }
        IEnumerator Wait(float s0) { float t = 0f; while (t < s0) { t += Time.deltaTime; yield return null; } }
        void Cam(Vector3 focus, float h) { if (!rts) return; rts.FreeLookOff(); rts.focus = new Vector3(focus.x, 0f, focus.z); rts.SetHeightImmediate(h); }
        Camera CamNow => rts ? rts.GetComponent<Camera>() : Camera.main;
        void Select(params ZObject[] os) { input.Selection.Clear(); foreach (var o in os) if (o != null) input.Selection.Add(o); }
        bool Move(int x, int y) => W.SendWaypoints(me, s, new[] { new ZWaypoint { mode = WpMode.Move, x = x, y = y, playerGiven = true } });
        static int Played(float t0, string key) { int n = 0; foreach (var e in OzSoundLog.Since(t0)) if (e.key == key && e.outcome != null && (e.outcome.StartsWith("PLAY") || e.outcome.StartsWith("LOOP"))) n++; return n; }
        Vector2 Px(Vector3 world) { var c = CamNow; if (!c) return Vector2.zero; var p = c.WorldToScreenPoint(world); return new Vector2(p.x, p.y); }
        float sinceTouch;

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
            Log($"K256g3 suspension proof, {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}, {UnityEngine.Screen.width}x{UnityEngine.Screen.height}, world time {W.Time:F1} s");
            int bots = W.Bots.Count; W.Bots.Clear();
            var dn = FindFirstObjectByType<DayNight>(); if (dn) { dn.timeOfDay = 0.10f; dn.Apply(0.10f); dn.running = false; }
            ZObject station = null; foreach (var f in W.Flags) foreach (var b in f.LinkedObjects) if (b.Type == "repair" && station == null) { station = b; if (b.Owner != me) W.AwardZone(f, me); }
            Log($"set-up: CPU removed {bots}, day 0.10 held, repair station {(station != null ? "#" + station.Id : "none")}; the rig fitted {ship.SuspRigged} (rigs {OzPlayableShuttle.SuspRigs}, fallbacks {OzPlayableShuttle.SuspFallbacks}); travel {OzPlayableShuttle.SuspTravelM:F2} m");
            Check("S0 the visible suspension is fitted on the ship (its legs cut into foot, strut and piston; no fallback)", ship.SuspRigged && OzPlayableShuttle.SuspFallbacks == 0);
            yield return WaitFor(() => s.ShuttleState == ZShuttleState.Landed && !s.ShuttleAirborne && s.ShuttleLevelled, 30f); yield return Wait(0.8f);
            yield return Landing(70f, "s1_h70", null);
            yield return Landing(35f, "s2_h35", null);
            yield return TakeOff();
            yield return Crash();
            if (station != null) yield return Landing(70f, "s5_deck_h70", station); else Check("S5 a repair station on the level", false);
            Finish();
        }

        // ------------------------------------------------------------------ S1 / S2 / S5: a landing, its 10-frame strip and its numbers
        IEnumerator Landing(float camH, string tag, ZObject station)
        {
            string S = tag.Substring(0, 2).ToUpper();
            Log($"{S} a {(station != null ? "repair station deck" : "normal")} landing, the camera at {camH:F0} m ({tag})");
            for (int tryN = 0; tryN < 4; tryN++)
            {
                yield return WaitFor(() => s.ShuttleState == ZShuttleState.Landed && s.ShuttleLevelled && !ship.CrashOn, 30f);
                if (station != null) { s.SetHealth(Mathf.Max(1, s.MaxHealth / 2)); Select(s); if (!input.SendToRepair()) { Check($"{S} the repair order (R) is taken", false); yield break; } }
                else
                {
                    var q = Free(s.CenterX, s.CenterY, 320, 700); if (q == null) { Check($"{S} a landing place", false); yield break; }
                    Select(s); Move(q[0], q[1]);
                }
                yield return WaitFor(() => s.ShuttleAirborne, 15f);
                yield return Wait(1.0f);
                float gapAir = 0f;   /* K388: read at the touchdown below (1 s after a lift-off the legs are folding) */
                yield return WaitFor(() => s.ShuttleState == ZShuttleState.Descend, 90f);
                if (s.ShuttleRoughLanding) { Log("   the rules made this one a crash landing: another flight"); yield return WaitFor(() => s.ShuttleState == ZShuttleState.Landed && s.ShuttleLevelled, 30f); yield return Wait(0.5f); continue; }
                Cam(ship.transform.position, camH);
                Log($"   >>> OBS: a 60 fps take of this landing now ({tag})");
                float t0 = Time.unscaledTime; int l0 = OzPlayableShuttle.SuspLandings, p0 = OzPlayableShuttle.SuspPuffs;
                yield return WaitFor(() => (float)s.ShuttleLift * (float)ZWorld.ShuttleDescendSec <= 0.3f, 20f);
                Cam(ship.transform.position, camH); sinceTouch = -0.3f; Shot($"{tag}_00_0.3s_before");
                yield return WaitFor(() => OzPlayableShuttle.SuspLandings > l0, 5f);
                sinceTouch = 0f; Shot($"{tag}_01_touchdown");
                var c = CamNow; var top0 = Px(ship.transform.TransformPoint(HullTop)); float pxPerM = c ? (Px(ship.transform.position + c.transform.right) - Px(ship.transform.position)).magnitude : 0f;
                float minX = 9f, maxPx = 0f; var times = new[] { 0.10f, 0.20f, 0.30f }; int k = 0; bool deep = false, up = false, f62 = false, f11 = false; float last = ship.SuspXNow;
                while (sinceTouch < 1.85f)
                {
                    sinceTouch += Time.deltaTime; yield return null;
                    float x = ship.SuspXNow; minX = Mathf.Min(minX, x);
                    maxPx = Mathf.Max(maxPx, (Px(ship.transform.TransformPoint(HullTop)) - top0).magnitude);
                    if (k < times.Length && sinceTouch >= times[k]) { Shot($"{tag}_{2 + k:00}_+{times[k]:F2}s"); k++; }
                    else if (!deep && sinceTouch > 0.15f && x > last) { deep = true; Shot($"{tag}_05_deepest"); }
                    else if (deep && !f62 && sinceTouch >= 0.62f) { f62 = true; Shot($"{tag}_06_+0.62s"); }
                    else if (deep && f62 && !up && x < last) { up = true; Shot($"{tag}_07_rebound_top"); }
                    else if (up && !f11 && sinceTouch >= 1.1f) { f11 = true; Shot($"{tag}_08_+1.10s"); }
                    last = x;
                }
                Shot($"{tag}_09_settled");
                yield return Wait(0.3f);
                gapAir = OzPlayableShuttle.SuspPistonAirSeen; /* K388: the pistons in flight, at the touchdown */ bool calm = OzMotion.Reduced; float ck = calm ? 0.5f : 1f;
                Log($"   {S}: the hull sank {OzPlayableShuttle.SuspSinkSeen:F3} m from its touchdown height ({OzPlayableShuttle.SuspOverSeen:F3} m past the parked pose), rebound {OzPlayableShuttle.SuspReboundSeen:F3} m over it, settled {OzPlayableShuttle.SuspSettleSeen:F2} s; rock {OzPlayableShuttle.SuspRockSeen:F2} deg; the struts turned {OzPlayableShuttle.SuspTurnSeen:F1} deg, slid {OzPlayableShuttle.SuspSlideSeen:F3} m into their pods; the pistons out {OzPlayableShuttle.SuspPistonAirSeen:F3} m at the touchdown ({gapAir:F3} m in flight); " +
                    $"the pads: slide {OzPlayableShuttle.SuspPadSlideMax * 100f:F2} cm, sink {OzPlayableShuttle.SuspPadSinkMax * 100f:F2} cm; the hull's top moved {maxPx:F1} px on the screen ({pxPerM:F1} px per m); puffs {OzPlayableShuttle.SuspPuffs - p0}; " +
                    $"sounds: hiss {Played(t0, "shuttle_leg_hiss")}, thud {Played(t0, "shuttle_leg_thud")}, creak {Played(t0, "shuttle_leg_spring")}, the landing {Played(t0, "shuttle_landing")}");
                Check($"{S}: the hull sinks {(calm ? 0.44f : 0.50f):F2} .. {0.62f:F2} m onto its legs (past the parked pose {0.07f * ck:F2} .. {0.18f * ck:F2} m), {(calm ? "no visible rebound (CALM SCREEN)" : "one soft rebound (0.015 m or more over the parked pose)")}, settled in 0.9 .. 1.7 s",
                      OzPlayableShuttle.SuspLandings == l0 + 1 && OzPlayableShuttle.SuspSinkSeen >= (calm ? 0.44f : 0.50f) && OzPlayableShuttle.SuspSinkSeen <= 0.62f && OzPlayableShuttle.SuspOverSeen >= (calm ? 0.0f : 0.07f) && OzPlayableShuttle.SuspOverSeen <= 0.18f * ck + 0.01f
                      && (calm || OzPlayableShuttle.SuspReboundSeen >= 0.015f) && OzPlayableShuttle.SuspSettleSeen >= (calm ? 0.5f : 0.9f) && OzPlayableShuttle.SuspSettleSeen <= 1.7f);
                Check($"{S}: it rocks {1.5f * ck:F1} .. {4.0f * ck:F1} deg and eases out; the struts splay 4 .. 12 deg and slide into their pods 0.04 .. 0.20 m; the pistons were out 0.30 .. 0.48 m in flight",
                      OzPlayableShuttle.SuspRockSeen >= 1.5f * ck && OzPlayableShuttle.SuspRockSeen <= 4.0f * ck && OzPlayableShuttle.SuspTurnSeen >= 4f && OzPlayableShuttle.SuspTurnSeen <= 12f && OzPlayableShuttle.SuspSlideSeen >= (calm ? 0f : 0.04f) && OzPlayableShuttle.SuspSlideSeen <= 0.20f && gapAir >= 0.30f && gapAir <= 0.48f);
                Check($"{S}: the pads stay planted (on the drawn feet: slide <= 0.5 cm, never into the {(station != null ? "deck" : "ground")} by more than 0.5 cm)", OzPlayableShuttle.SuspPadSlideMax <= 0.005f && OzPlayableShuttle.SuspPadSinkMax <= 0.005f);
                Check($"{S}: the hydraulic hiss, the struts' thud and the creak play once each (every visible action has its sound), and a puff at the pads", Played(t0, "shuttle_leg_hiss") >= 1 && Played(t0, "shuttle_leg_thud") >= 1 && Played(t0, "shuttle_leg_spring") >= 1 && OzPlayableShuttle.SuspPuffs > p0);
                if (camH >= 69f) Check($"{S}: at the 70 m camera the hull's top moves 7 px or more (K256g's dip: about 4 px)", maxPx >= 7f);
                if (station != null)
                {
                    float off = 0f; int n = 0;
                    foreach (var pl in new[] { new Vector3(2.228f, 0f, 1.029f), new Vector3(-2.168f, 0f, 1.079f), new Vector3(1.829f, 0f, -2.843f), new Vector3(-1.771f, 0f, -2.838f) })
                    { var pw = ship.transform.TransformPoint(pl); if (OzStationCrane.DeckY(station.Id, pw, out float dy)) { n++; off = Mathf.Max(off, Mathf.Abs(pw.y - dy)); } }
                    int hits = OzStationCrane.ShipHits(station.Id, ship.transform, out float deepM, out string where);
                    Log($"   {S} on the deck: pads {off * 100f:F1} cm off the deck ({n} of 4 over it); ship vertices inside the station {hits} ({where})");
                    Check($"{S}: on the deck, every pad within 6 cm of it, no part of the ship (its struts and pistons too) inside the station", s.ShuttleOnRepairPad && n == 4 && off <= 0.06f && hits == 0);
                    s.SetHealth(s.MaxHealth);
                }
                yield break;
            }
            Check($"{S}: a normal landing in 4 flights", false);
        }

        // ------------------------------------------------------------------ S3: a take-off
        IEnumerator TakeOff()
        {
            Log("S3 a take-off at 70 m: the legs unload while the pads stay down, then the pads leave");
            yield return WaitFor(() => s.ShuttleState == ZShuttleState.Landed && s.ShuttleLevelled && !ship.CrashOn, 30f); yield return Wait(1.5f);
            var q = Free(s.CenterX, s.CenterY, 320, 700); if (q == null) { Check("S3 a place to fly to", false); yield break; }
            Cam(ship.transform.position, 70f); yield return Wait(0.3f);
            int t0n = OzPlayableShuttle.SuspTakeoffs; float t0 = Time.unscaledTime;
            sinceTouch = -0.1f; Shot("s3_h70_00_parked");
            Select(s); Move(q[0], q[1]);
            yield return WaitFor(() => OzPlayableShuttle.SuspTakeoffs > t0n, 10f);
            sinceTouch = 0f; Shot("s3_h70_01_takeoff_note");
            var times = new[] { 0.05f, 0.10f, 0.15f, 0.20f, 0.25f, 0.35f, 0.50f, 0.80f }; int k = 0;
            while (k < times.Length) { sinceTouch += Time.deltaTime; yield return null; if (sinceTouch >= times[k]) { Shot($"s3_h70_{2 + k:00}_+{times[k]:F2}s"); k++; } }
            Log($"   S3: the pads stayed down {OzPlayableShuttle.SuspTakeoffPlantedSec:F2} s after the take-off note; hiss {Played(t0, "shuttle_leg_hiss")}, clunk {Played(t0, "shuttle_leg_thud")}");
            Check("S3: the hull rises off its springs while the pads stay down 0.15 .. 0.35 s, then the pads leave (the hiss and the clunk play)", OzPlayableShuttle.SuspTakeoffPlantedSec >= 0.15f && OzPlayableShuttle.SuspTakeoffPlantedSec <= 0.35f && Played(t0, "shuttle_leg_hiss") >= 1 && Played(t0, "shuttle_leg_thud") >= 1);
            yield return WaitFor(() => s.ShuttleState == ZShuttleState.Landed && s.ShuttleLevelled, 60f);
        }

        // ------------------------------------------------------------------ S4: a crash landing
        IEnumerator Crash()
        {
            Log("S4 a crash landing (the rules' proof switch) at 70 m: K256e's crash, the legs take the slam and lock, the level settle dips");
            yield return WaitFor(() => s.ShuttleState == ZShuttleState.Landed && s.ShuttleLevelled && !ship.CrashOn, 30f); yield return Wait(1.0f);
            var q = Free(s.CenterX, s.CenterY, 320, 700); if (q == null) { Check("S4 a landing place", false); yield break; }
            OzPlayableShuttle.ProofRoughNext = true; int c0 = OzPlayableShuttle.SpringsCrash, lk0 = OzPlayableShuttle.SuspLocks; float t0 = Time.unscaledTime;
            Select(s); Move(q[0], q[1]);
            yield return WaitFor(() => s.ShuttleState == ZShuttleState.Descend, 60f);
            Cam(ship.transform.position, 70f);
            yield return WaitFor(() => (float)s.ShuttleLift * (float)ZWorld.ShuttleDescendSec <= 0.3f, 20f); sinceTouch = -0.3f; Shot("s4_h70_00_0.3s_before");
            yield return WaitFor(() => ship.CrashOn, 5f); sinceTouch = 0f; Shot("s4_h70_01_the_slam");
            var times = new[] { 0.15f, 0.30f, 0.60f, 1.50f, 3.00f }; int k = 0;
            while (k < times.Length && ship.CrashOn) { sinceTouch += Time.deltaTime; yield return null; if (sinceTouch >= times[k]) { Shot($"s4_h70_{2 + k:00}_+{times[k]:F2}s"); k++; } }
            yield return WaitFor(() => OzPlayableShuttle.SpringsCrash > c0, 10f); Shot("s4_h70_07_level");
            yield return Wait(0.15f); Shot("s4_h70_08_level_dip"); yield return Wait(1.2f); Shot("s4_h70_09_settled");
            Log($"   S4: rough {s.ShuttleRoughLanding}, legs locked {OzPlayableShuttle.SuspLocks - lk0}, the level settle {OzPlayableShuttle.SpringsCrash - c0} (dip {OzPlayableShuttle.SpringDipSeen:F3} m); K256e: rest tilt {ship.CrashRestTiltSeen:F1} deg, the low side's hop {ship.CrashHopSeen:F2} m, the other side down at {ship.CrashHighAtSeen:F2} s; hiss {Played(t0, "shuttle_leg_hiss")}, creak {Played(t0, "shuttle_leg_spring")}");
            Check("S4: the crash keeps K256e's pose (rest tilt 12 .. 15 deg, the hop 0.25 .. 0.40 m, the other side down 0.28 .. 0.38 s), its legs lock as they touch (3 or more), one level settle (dip 0.07 .. 0.12 m) with its hiss and creak",
                  ship.CrashRestTiltSeen >= 12f && ship.CrashRestTiltSeen <= 15f && ship.CrashHopSeen >= 0.25f && ship.CrashHopSeen <= 0.40f && ship.CrashHighAtSeen >= 0.28f && ship.CrashHighAtSeen <= 0.38f
                  && OzPlayableShuttle.SuspLocks - lk0 >= 3 && OzPlayableShuttle.SpringsCrash == c0 + 1 && OzPlayableShuttle.SpringDipSeen >= 0.07f * (OzMotion.Reduced ? 0.5f : 1f) && OzPlayableShuttle.SpringDipSeen <= 0.12f && Played(t0, "shuttle_leg_hiss") >= 1 && Played(t0, "shuttle_leg_spring") >= 1);
            OzPlayableShuttle.ProofRoughNext = false;
        }

        void Finish()
        {
            OzPlayableShuttle.ProofRoughNext = false;
            Log($"K256g3 suspension proof: {passes} PASS, {fails} FAIL, {frames} pictures");
            Report = fails == 0 ? $"PASS {passes}" : $"FAIL {fails} of {passes + fails}";
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "suspension_proof_log.txt"), sb.ToString());
            Destroy(gameObject);
        }
    }
}
