using System.Collections;
using System.Text;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// K402 Play proof: the shuttle's crash landing shakes the camera when it is on screen (View/OzCrashLandingShake.cs), on level 16 in
    /// Play (menu OpenZ/V1.1/Shuttle: K402 Crash Landing Shake Proof, Editor/OzCrashShakeMenu.cs). Counted PASS / FAIL lines, the shake's
    /// own log lines with their strength and the measured camera shake (OpenZFxShake.AmountPx, screen px at the 1080 px reference) every
    /// frame from 0.3 s before the touch to 1.5 s after it (a CSV per stage). Set-up: the CPU teams removed, morning (day clock 0.10,
    /// held), 60 fps, the normal RTS camera at 70 m. Orders through the rules (SendWaypoints; the rules' proof switch for a crash landing),
    /// no desktop input. Stages:
    ///  C1 a crash landing ON SCREEN (the camera on the ship): the slam kicks (strength logged), the bounce kicks smaller, the camera
    ///  C2 a crash landing OFF SCREEN (the camera 150 m away before the touch): no kick, both requests counted off-screen, no shake;
    ///  C3 a crash landing on screen with CAMERA SHAKE ON EXPLOSIONS off: no kick (refused "shake off"), no shake;
    ///  C4 a crash landing on screen with CALM MODE on: no kick (refused "calm mode"), no shake;
    ///  C5 a NORMAL landing on screen: no request at all.
    /// The two switches are put back as they were. Output: <see cref="Folder"/> (the work tree's records, milestones/crash_landing_shake_k402,
    /// found upward at run time). Test only.
    /// </summary>
    public sealed class OzCrashShakeProof : MonoBehaviour
    {
        public static string Report = "idle";
        public static string Folder()
        {
            var d = new System.IO.DirectoryInfo(Application.dataPath);
            for (int i = 0; i < 8 && d != null; i++, d = d.Parent)
                if (System.IO.Directory.Exists(System.IO.Path.Combine(d.FullName, "work", "records")))
                    return System.IO.Path.Combine(d.FullName, "work", "records", "milestones", "crash_landing_shake_k402");
            return System.IO.Path.Combine(Application.persistentDataPath, "crash_landing_shake_k402");
        }
        public static void Run()
        {
            Report = "running";
#if UNITY_EDITOR
            var gv = System.Type.GetType("UnityEditor.GameView,UnityEditor"); if (gv != null) UnityEditor.EditorWindow.FocusWindowIfItsOpen(gv);
#endif
            new GameObject("OzCrashShakeProof").AddComponent<OzCrashShakeProof>();
        }

        ZGame g; ZWorld W; PlayerInput input; RtsCamera rts; Team me = Team.Red;
        ZObject s; OzPlayableShuttle ship;
        readonly StringBuilder sb = new StringBuilder(); int fails, passes; string dir;
        int shakePref0; bool calm0, gateLog0;

        void Start()
        {
            g = ZGame.I; W = g ? g.World : null; input = FindFirstObjectByType<PlayerInput>(); rts = FindFirstObjectByType<RtsCamera>();
            dir = Folder(); System.IO.Directory.CreateDirectory(dir);
            shakePref0 = PlayerPrefs.GetInt("oz_shake", 1); calm0 = OzMotion.Reduced; gateLog0 = OzShakeGate.LogToConsole;
            StartCoroutine(All());
        }

        void Log(string t) { sb.AppendLine(t); Debug.Log("[OzCrashShakeProof] " + t); }
        void Check(string what, bool ok) { if (ok) passes++; else fails++; Log((ok ? "PASS " : "FAIL ") + what); }
        IEnumerator WaitFor(System.Func<bool> c, float max) { float t = 0f; while (t < max && !c()) { t += Time.deltaTime; yield return null; } }
        IEnumerator Wait(float s0) { float t = 0f; while (t < s0) { t += Time.deltaTime; yield return null; } }
        void Cam(Vector3 focus, float h) { if (!rts) return; rts.FreeLookOff(); rts.focus = new Vector3(focus.x, 0f, focus.z); rts.SetHeightImmediate(h); }
        void Select(params ZObject[] os) { input.Selection.Clear(); foreach (var o in os) if (o != null) input.Selection.Add(o); }
        bool Move(int x, int y) => W.SendWaypoints(me, s, new[] { new ZWaypoint { mode = WpMode.Move, x = x, y = y, playerGiven = true } });
        static float AmountNow() { var sh = OpenZFxShake.I; return sh != null ? sh.AmountPx(Camera.main) : 0f; }

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
            Log($"K402 crash landing shake proof, {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}, {Screen.width}x{Screen.height}, world time {W.Time:F1} s; slam {OzCrashLandingShake.SlamPx:F1} px, bounce {OzCrashLandingShake.BouncePx:F1} px, edge x{OzCrashLandingShake.EdgeK:F2}; the effect shake {(OpenZFxShake.I != null ? "on (scale " + OpenZFxShake.I.scale.ToString("F2") + ")" : "MISSING")}");
            int bots = W.Bots.Count; W.Bots.Clear();
            var dn = FindFirstObjectByType<DayNight>(); if (dn) { dn.timeOfDay = 0.10f; dn.Apply(0.10f); dn.running = false; }
            PlayerPrefs.SetInt("oz_shake", 1); OzMotion.Reduced = false; OzShakeGate.LogToConsole = true; OzCrashLandingShake.LogToConsole = true;
            Log($"set-up: CPU removed {bots}, day 0.10 held; CAMERA SHAKE ON EXPLOSIONS on, CALM MODE off (were {(shakePref0 == 1 ? "on" : "off")} / {(calm0 ? "on" : "off")}, put back at the end)");
            Check("C0 the game's explosion shake is there (OpenZFxShake) and not scaled to 0", OpenZFxShake.I != null && OpenZFxShake.I.scale > 0f);
            yield return WaitFor(() => s.ShuttleState == ZShuttleState.Landed && !s.ShuttleAirborne && s.ShuttleLevelled, 30f); yield return Wait(0.8f);

            yield return Crash("C1", "c1_on_screen", false);
            yield return Crash("C2", "c2_off_screen", true);
            PlayerPrefs.SetInt("oz_shake", 0);
            yield return Crash("C3", "c3_shake_option_off", false);
            PlayerPrefs.SetInt("oz_shake", 1); OzMotion.Reduced = true;
            yield return Crash("C4", "c4_calm_mode", false);
            OzMotion.Reduced = false;
            yield return Normal();
            Finish();
        }

        /// <summary>One crash landing (the rules' proof switch), the camera on the ship at 70 m, or 150 m away just before the touch.</summary>
        IEnumerator Crash(string S, string tag, bool away)
        {
            Log($"{S} a crash landing, {(away ? "OFF screen (the camera 150 m away before the touch)" : "ON screen (the camera on the ship, 70 m)")}, shake option {(PlayerPrefs.GetInt("oz_shake", 1) == 1 ? "on" : "OFF")}, calm mode {(OzMotion.Reduced ? "ON" : "off")} ({tag})");
            yield return WaitFor(() => s.ShuttleState == ZShuttleState.Landed && s.ShuttleLevelled && !ship.CrashOn, 30f); yield return Wait(1.0f);
            var q = Free(s.CenterX, s.CenterY, 320, 700); if (q == null) { Check($"{S} a landing place", false); yield break; }
            OzPlayableShuttle.ProofRoughNext = true;
            Select(s); Move(q[0], q[1]);
            yield return WaitFor(() => s.ShuttleState == ZShuttleState.Descend, 60f);
            Cam(ship.transform.position, 70f);
            yield return WaitFor(() => (float)s.ShuttleLift * (float)ZWorld.ShuttleDescendSec <= 0.8f, 20f);
            Cam(ship.transform.position, 70f);
            if (away) yield return LookAway();
            else Log($"   >>> OBS: a 60 fps take of this crash landing now ({tag})");
            OzCrashLandingShake.ResetCounts(); OzShakeGate.ResetCounts();                            // just before the touch (the counts of this crash only)
            var csv = new StringBuilder("t_s (after the touch; before it: minus the time waited),shake_px\n");
            float t = 0f, waited = 0f, peak = 0f, peakAt = -1f, settled = -1f, at03 = 0f; bool began = false, onScr = false;
            while (began ? t < 1.5f : waited < 6f)
            {
                yield return null;
                if (!began && ship.CrashOn) { began = true; t = 0f; onScr = OzShakeGate.OnScreen(ship.transform.position); }
                else if (began) t += Time.deltaTime;
                else { waited += Time.deltaTime; t = -waited; }
                float a = AmountNow(); csv.Append(t.ToString("F3")).Append(',').Append(a.ToString("F3")).Append('\n');
                if (a > peak) { peak = a; peakAt = t; }
                if (began && t >= 0.3f && at03 <= 0f) at03 = a;
                if (began && peak > 0f && t > 0.4f && settled < 0f && a < 0.1f * peak) settled = t;
            }
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, tag + "_shake.csv"), csv.ToString());
            yield return Wait(0.2f);
            foreach (var l in OzCrashLandingShake.Log) Log("   " + l);
            foreach (var l in OzShakeGate.Log) Log("   " + l);
            int K = OzCrashLandingShake.Kicks, A = OzCrashLandingShake.Asked, R = OzCrashLandingShake.Refused, O = OzCrashLandingShake.OffScreen;
            Log($"   {S}: crash drawn {began}, the hull on screen {onScr}; slams {OzCrashLandingShake.Slams}, bounces {OzCrashLandingShake.Bounces}; asked {A}, kicks {K}, refused {R}, off-screen {O}; camera shake peak {peak:F2} px at {peakAt:F2} s, at +0.3 s {at03:F2} px, under a tenth of the peak from {settled:F2} s; the other side down at {ship.CrashHighAtSeen:F2} s");
            if (!began) { Check($"{S} the crash landing was drawn", false); OzPlayableShuttle.ProofRoughNext = false; yield break; }
            if (S == "C1")
                Check("C1 on screen: the slam and the bounce both kick (2 kicks of 2 asked), the camera shakes (peak 1.5 px or more within 0.45 s of the touch) and settles under a tenth of its peak by 1.0 s",
                      onScr && A == 2 && K == 2 && peak >= 1.5f && peakAt <= 0.45f && settled > 0f && settled <= 1.0f);
            else if (S == "C2")
                Check("C2 off screen: no kick, both requests counted off-screen, no camera shake (peak under 0.05 px)", !onScr && K == 0 && O == 2 && peak < 0.05f);
            else
                Check($"{S} {(S == "C3" ? "the shake option off" : "calm mode")}: no kick (both refused, verdict {(S == "C3" ? "shake off" : "calm mode")}), no camera shake (peak under 0.05 px)",
                      onScr && K == 0 && R == 2 && OzCrashLandingShake.LastVerdict == (S == "C3" ? "shake off" : "calm mode") && peak < 0.05f);
            OzPlayableShuttle.ProofRoughNext = false;
        }

        /// <summary>The camera 150 m from the ship, in the first direction that keeps the map under it and the ship out of the view (and its margin).</summary>
        IEnumerator LookAway()
        {
            var p = ship.transform.position; var dirs = new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back };
            foreach (var d in dirs)
            {
                Cam(p + d * 150f, 70f); yield return null; yield return null;
                var c = Camera.main; if (!c) break;
                if (OzCrashLandingShake.ScreenK(c.WorldToViewportPoint(p), c.nearClipPlane, c.farClipPlane) <= 0f) { Log($"   the camera looks {d} 150 m away: the ship is out of the view"); yield break; }
            }
            Log("   no direction put the ship out of the view (the map's edge?)");
        }

        /// <summary>C5: a normal landing on screen asks for no shake at all.</summary>
        IEnumerator Normal()
        {
            Log("C5 a NORMAL landing on screen (70 m): no crash shake request");
            for (int tryN = 0; tryN < 4; tryN++)
            {
                yield return WaitFor(() => s.ShuttleState == ZShuttleState.Landed && s.ShuttleLevelled && !ship.CrashOn, 30f); yield return Wait(1.0f);
                var q = Free(s.CenterX, s.CenterY, 320, 700); if (q == null) { Check("C5 a landing place", false); yield break; }
                OzPlayableShuttle.ProofRoughNext = false;
                Select(s); Move(q[0], q[1]);
                yield return WaitFor(() => s.ShuttleState == ZShuttleState.Descend, 60f);
                if (s.ShuttleRoughLanding) { Log("   the rules made this one a crash landing: another flight"); yield return WaitFor(() => s.ShuttleState == ZShuttleState.Landed && s.ShuttleLevelled && !ship.CrashOn, 30f); continue; }
                Cam(ship.transform.position, 70f);
                OzCrashLandingShake.ResetCounts();
                int l0 = ship.Landings;
                yield return WaitFor(() => ship.Landings > l0, 20f); yield return Wait(1.5f);
                Log($"   C5: landed {ship.Landings > l0}, crash drawn {ship.CrashOn}, asked {OzCrashLandingShake.Asked}, slams {OzCrashLandingShake.Slams}");
                Check("C5 a normal landing: no crash shake request (0 asked, 0 slams)", ship.Landings > l0 && OzCrashLandingShake.Asked == 0 && OzCrashLandingShake.Slams == 0);
                yield break;
            }
            Check("C5 a normal landing came (4 tries)", false);
        }

        void Finish()
        {
            OzPlayableShuttle.ProofRoughNext = false;
            PlayerPrefs.SetInt("oz_shake", shakePref0); OzMotion.Reduced = calm0; OzShakeGate.LogToConsole = gateLog0; OzCrashLandingShake.LogToConsole = false;
            Log($"K402 crash landing shake proof: {passes} PASS, {fails} FAIL (the two switches put back: shake {(shakePref0 == 1 ? "on" : "off")}, calm {(calm0 ? "on" : "off")})");
            Report = fails == 0 ? $"PASS {passes}" : $"FAIL {fails} of {passes + fails}";
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "crash_shake_proof_log.txt"), sb.ToString());
            Destroy(gameObject);
        }
    }
}
