using System.Collections;
using System.Text;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace OpenZ
{
    /// <summary>
    /// K426 Play proof: EXIT sooner after a landing (rule DV118, Sim/ZWorld.ShuttleExitSooner.cs), on level 16 in Play (menu OpenZ/V1.1/Shuttle:
    /// K426 Exit Sooner Proof, Editor/OzShuttleExitSoonerMenu.cs). Counted PASS / FAIL lines, a table of the times and full-screen pictures from
    /// the normal RTS camera at 70 m. Set-up: the CPU teams removed, morning (day clock 0.10, held), a red grunt squad of 4 boards the ship.
    /// The game's pointer is moved by queued input events inside the game only (no desktop input); EXIT is the game's own command
    /// (PlayerInput.EjectUnit, the right-click / button path). Stages (each a flight to a free spot):
    ///  N a NORMAL landing, EXIT clicked on the touchdown frame: the EXIT cursor on the ship, the order taken, the ramp down at once, the squad
    ///    out a ramp's swing later, the door's sound;
    ///  C a CRASH landing, EXIT clicked on the touchdown frame (the ship still tilted): the EXIT cursor, taken, the ramp shut while it crashes,
    ///    rests and rights itself, down the moment it stands level, the squad out a ramp's swing later (less with a kit that lets them out on the way down);
    ///  L a normal landing, EXIT clicked while it comes down the last 3 m: refused (K430b: no EXIT in the sky, UI/OzShuttleAirOffers.cs), clicked
    ///    again on the touchdown frame: taken, the squad out as in N;
    ///  O today's rules for comparison (the switch ZWorld.ShuttleExitSoonerOn off for this one flight): a crash landing, EXIT clicked the
    ///    moment the game offers it (refused until level, 3.6 s); then the switch back on.
    /// Output: <see cref="Folder"/> (the work tree's records, milestones/shuttle_exit_sooner_k426, found upward at run time). Test only.
    /// </summary>
    public sealed class OzShuttleExitSoonerProof : MonoBehaviour
    {
        public static string Report = "idle";
        public static string Folder()
        {
            var d = new System.IO.DirectoryInfo(Application.dataPath);
            for (int i = 0; i < 8 && d != null; i++, d = d.Parent)
                if (System.IO.Directory.Exists(System.IO.Path.Combine(d.FullName, "work", "records")))
                    return System.IO.Path.Combine(d.FullName, "work", "records", "milestones", "shuttle_exit_sooner_k426");
            return System.IO.Path.Combine(Application.persistentDataPath, "shuttle_exit_sooner_k426");
        }
        public static void Run()
        {
            Report = "running";
#if UNITY_EDITOR
            var gv = System.Type.GetType("UnityEditor.GameView,UnityEditor"); if (gv != null) UnityEditor.EditorWindow.FocusWindowIfItsOpen(gv);
#endif
            new GameObject("OzShuttleExitSoonerProof").AddComponent<OzShuttleExitSoonerProof>();
        }

        ZGame g; ZWorld W; PlayerInput input; RtsCamera rts; OpenZCursors cursors; Team me = Team.Red;
        ZObject s, lead; OzPlayableShuttle ship;
        readonly StringBuilder sb = new StringBuilder(), table = new StringBuilder(); int fails, passes; string dir;

        sealed class Stage
        {
            public string tag; public bool ok, rough, offeredAtTd, taken, airTaken, airTried; public string cursorAtTd = "?", cursorTilted = "-", airCursor = "-";   // K430b: L's refused click up in the air
            public double td = -1, level = -1, clickAt = -1, rampStart = -1, outAt = -1, door = -1, foot = -1; public int made, aboard, rampWhileTilted, doorSounds; public float openDeg;
        }

        void Start()
        {
            g = ZGame.I; W = g ? g.World : null; input = FindFirstObjectByType<PlayerInput>(); rts = FindFirstObjectByType<RtsCamera>(); cursors = FindFirstObjectByType<OpenZCursors>();
            dir = Folder(); System.IO.Directory.CreateDirectory(dir);
            StartCoroutine(All());
        }

        void Log(string t) { sb.AppendLine(t); Debug.Log("[OzShuttleExitSoonerProof] " + t); }
        void Check(string what, bool ok) { if (ok) passes++; else fails++; Log((ok ? "PASS " : "FAIL ") + what); }
        IEnumerator WaitFor(System.Func<bool> c, float max) { float t = 0f; while (t < max && !c()) { t += Time.deltaTime; yield return null; } }
        IEnumerator Wait(float s0) { float t = 0f; while (t < s0) { t += Time.deltaTime; yield return null; } }
        void Cam(Vector3 focus, float h) { if (!rts) return; rts.FreeLookOff(); rts.focus = new Vector3(focus.x, 0f, focus.z); rts.SetHeightImmediate(h); }
        void Select(params ZObject[] os) { input.Selection.Clear(); foreach (var o in os) if (o != null) input.Selection.Add(o); }
        bool Move(int x, int y) => W.SendWaypoints(me, s, new[] { new ZWaypoint { mode = WpMode.Move, x = x, y = y, playerGiven = true } });
        void Shot(string name) { ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, name + ".png")); Log("   picture " + name + ".png"); }
        static int Played(float t0, string key) { int n = 0; foreach (var e in OzSoundLog.Since(t0)) if (e.key == key && e.outcome != null && e.outcome.StartsWith("PLAY")) n++; return n; }
        void PointAt(ZObject o) { if (Mouse.current == null || input == null || o == null) return; InputSystem.QueueStateEvent(Mouse.current, new MouseState { position = input.ScreenRect(o).center }); }
        static string F(double v) => v.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        /// <summary>From the ramp starting down to the squad let out (s): the ramp's swing, or a later kit's own value when it lets the squad out
        /// while the ramp still comes down (a public static ZWorld.ShuttleExitAfterSec, read when present).</summary>
        static double ExitAfterSec()
        {
            var p = typeof(ZWorld).GetProperty("ShuttleExitAfterSec", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (p != null && p.PropertyType == typeof(double)) return (double)p.GetValue(null);
            return ZWorld.ShuttleRampOpenSec;
        }

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

        /// <summary>The squad (back on the ground after an EXIT, or made new) boards the ship and the ramp shuts again.</summary>
        IEnumerator Board()
        {
            if (s.Passengers.Count > 0) yield break;
            if (lead == null || !OzV11Alive(lead))
            {
                var (bx, by) = s.ShuttleBackPoint(6);
                lead = W.CreateRobotGroup("grunt", bx - 8, by - 8, me, 4);
            }
            yield return WaitFor(() => s.ShuttleRampPhase == ZShuttleRamp.Shut && W.Time >= s.ShuttleRampHoldUntil, 15f);
            W.SendWaypoints(me, lead, new[] { new ZWaypoint { mode = WpMode.Enter, x = s.CenterX, y = s.CenterY, refId = s.Id, playerGiven = true } });
            yield return WaitFor(() => s.Passengers.Count > 0, 60f);
            yield return WaitFor(() => s.ShuttleRampPhase == ZShuttleRamp.Shut && W.Time >= s.ShuttleRampHoldUntil, 20f);
            yield return Wait(0.5f);
        }
        static bool OzV11Alive(ZObject o) => o != null && !o.Destroyed && o.KillAt < 0;

        IEnumerator All()
        {
            Application.targetFrameRate = 60;
            if (W == null || input == null) { Log("no game (level 16 in Play)"); fails++; Finish(); yield break; }
            if (!ZWorld.ShuttlePlayableDeviation || W.PlayableShuttle == null) { Log("no playable shuttle (level 16, the shuttle rule on, the option SHUTTLE on)"); fails++; Finish(); yield break; }
            me = g.player; s = W.PlayableShuttle; ship = OzPlayableShuttle.Of(s);
            if (!ship) { Log("the shuttle has no view"); fails++; Finish(); yield break; }
            Log($"K426 EXIT sooner proof, {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}, {Screen.width}x{Screen.height}, world time {F(W.Time)} s; switch {ZWorld.ShuttleExitSoonerOn}; a crash: {F(ZWorld.ShuttleRoughCrashSec)} + {F(ZWorld.ShuttleRoughRestSec)} + {F(ZWorld.ShuttleRoughLevelSec)} s; the ramp {F(ZWorld.ShuttleRampOpenSec)} s, the doorway {F(ZWorld.ShuttleExitDoorSec)} s");
            int bots = W.Bots.Count; W.Bots.Clear();
            var dn = FindFirstObjectByType<DayNight>(); if (dn) { dn.timeOfDay = 0.10f; dn.Apply(0.10f); dn.running = false; }
            Log($"set-up: CPU removed {bots}, day 0.10 held");
            Check("E0 the K426 rule is on", ZWorld.ShuttleExitSoonerOn);
            yield return WaitFor(() => s.ShuttleState == ZShuttleState.Landed && !s.ShuttleAirborne && s.ShuttleLevelled, 30f);
            yield return Board();
            Check($"E0 a squad of 4 aboard ({s.Passengers.Count})", s.Passengers.Count == 4);
            Log($"   the parked ramp's open angle at the boarding: {ShuttleRearDoor.LastOpenDeg:F1} deg");
            table.AppendLine("stage | landing | EXIT clicked | cursor at the touchdown | taken | level | ramp down from | squad out | first robot through the doorway | off the ramp foot (s after the touchdown)");
            var n = new Stage { tag = "N" }; yield return Fly(n, false, "touchdown");
            var c = new Stage { tag = "C" }; yield return Fly(c, true, "touchdown");
            var l = new Stage { tag = "L" }; yield return Fly(l, false, "last3m");
            var o = new Stage { tag = "O" };
            ZWorld.ShuttleExitSoonerOn = false;
            yield return Fly(o, true, "asap");
            ZWorld.ShuttleExitSoonerOn = true;
            double ro = ExitAfterSec(), tol = 0.05;   // the ramp starting down -> the squad let out
            Check($"N normal landing, EXIT on the touchdown frame: the EXIT cursor on the ship ('{n.cursorAtTd}'), taken {n.taken}, the ramp down from +{F(n.rampStart)} s (at once), the squad out +{F(n.outAt)} s ({n.made} of {n.aboard}), the door's sound {n.doorSounds}",
                  n.ok && !n.rough && n.cursorAtTd == "exit" && n.taken && n.rampStart >= 0 && n.rampStart <= System.Math.Max(0.0, n.clickAt) + tol && n.outAt >= 0 && System.Math.Abs(n.outAt - n.rampStart - ro) <= tol && n.made == n.aboard && n.aboard > 0 && n.doorSounds > 0);
            Check($"C crash landing, EXIT on the touchdown frame (tilted): the EXIT cursor ('{c.cursorAtTd}', resting tilted '{c.cursorTilted}'), taken {c.taken}; the ramp shut while it crashes and rights itself (moved {c.rampWhileTilted} frames), down from +{F(c.rampStart)} s (level +{F(c.level)}), the squad out +{F(c.outAt)} s ({c.made} of {c.aboard})",
                  c.ok && c.rough && c.cursorAtTd == "exit" && c.cursorTilted == "exit" && c.taken && c.rampWhileTilted == 0 && c.rampStart >= 0 && System.Math.Abs(c.rampStart - c.level) <= tol && System.Math.Abs(c.outAt - c.level - ro) <= tol && c.made == c.aboard && c.aboard > 0);
            Check($"L EXIT clicked in the last 3 m: refused in the air (K430b: taken {l.airTaken}, the cursor '{l.airCursor}'); clicked again on the touchdown frame: taken {l.taken}, the ramp down from +{F(l.rampStart)} s, the squad out +{F(l.outAt)} s after the touchdown (N: +{F(n.outAt)})",
                  l.ok && l.airTried && !l.airTaken && l.airCursor != "exit" && l.taken && l.rampStart >= 0 && l.rampStart <= System.Math.Max(0.0, l.clickAt) + tol && System.Math.Abs(l.outAt - l.rampStart - ro) <= tol && l.made == l.aboard && l.aboard > 0);
            Check($"O today's rules for comparison: a crash's EXIT refused until it stands level (the cursor at the touchdown '{o.cursorAtTd}'), clicked at +{F(o.clickAt)} s, the squad out +{F(o.outAt)} s; K426: +{F(c.outAt)} s ({F(o.outAt - c.outAt)} s sooner, at least 1.8)",
                  o.ok && o.rough && o.cursorAtTd != "exit" && o.clickAt >= 3.5 && c.outAt >= 0 && o.outAt >= 0 && o.outAt - c.outAt >= 1.8);
            Check($"E the ramp's open angle solved on the parked pose ({OzPlayableShuttle.RampParkedSolves} openings, the largest suspension offset taken out {OzPlayableShuttle.RampParkedLiftMax:F2} m): N {n.openDeg:F1} deg, C {c.openDeg:F1}, L {l.openDeg:F1}",
                  OzPlayableShuttle.RampParkedSolves > 0);
            Log("TIMES (the rules' clock):"); Log(table.ToString().TrimEnd());
            Log("   look at: N/L the ramp's tip resting on the ground in the e pictures (never into it, never floating); C the ship still tilted in b with the squad aboard, the ramp coming down only in c (level); the robots walking down in d");
            Finish();
        }

        /// <summary>One flight to a free spot and its landing; mode "touchdown" | "last3m" | "asap".</summary>
        IEnumerator Fly(Stage st, bool rough, string mode)
        {
            Log($"{st.tag} a {(rough ? "CRASH" : "normal")} landing, EXIT {(mode == "touchdown" ? "clicked on the touchdown frame" : mode == "last3m" ? "clicked while it comes down the last 3 m" : "clicked the moment it is offered")} (switch {ZWorld.ShuttleExitSoonerOn})");
            for (int tryN = 0; tryN < 4; tryN++)
            {
                yield return WaitFor(() => s.ShuttleState == ZShuttleState.Landed && s.ShuttleLevelled && !ship.CrashOn && W.Time >= s.ShuttleRampHoldUntil && s.ShuttleRampPhase == ZShuttleRamp.Shut, 30f);
                yield return Board();
                var q = Free(s.CenterX, s.CenterY, 320, 700); if (q == null) { Check($"{st.tag} a landing place", false); yield break; }
                OzPlayableShuttle.ProofRoughNext = rough;
                Select(s); Move(q[0], q[1]);
                yield return WaitFor(() => s.ShuttleState == ZShuttleState.Descend, 60f);
                OzPlayableShuttle.ProofRoughNext = false;
                if (s.ShuttleState != ZShuttleState.Descend) { Log("   it did not come down (state " + s.ShuttleState + "): another flight"); continue; }
                if (s.ShuttleRoughLanding != rough) { Log("   the rules made this landing " + (s.ShuttleRoughLanding ? "a crash" : "a normal one") + ": another flight"); yield return WaitFor(() => s.ShuttleState == ZShuttleState.Landed && s.ShuttleLevelled, 20f); continue; }
                st.rough = rough; st.aboard = s.Passengers.Count;
                Cam(ship.transform.position, 70f);
                float t0 = Time.unscaledTime; int opens0 = s.ShuttleRampOpens; bool clicked = false;
                while (s.ShuttleState == ZShuttleState.Descend)
                {
                    PointAt(s);
                    if (mode == "last3m" && !st.airTried && s.ShuttleHeightM <= 3.0) { st.airTried = true; st.airCursor = cursors ? cursors.Current : "?"; st.airTaken = input.EjectUnit(s) || s.ShuttleExitOnLanding; Shot($"k426_{st.tag}_a_clicked_last_3m_refused_h70"); }   // K430b: no EXIT in the sky: refused; clicked again on the touchdown frame
                    yield return null;
                }
                if (s.ShuttleState != ZShuttleState.Landed) { Log("   the descent was called off (state " + s.ShuttleState + "): another flight"); continue; }
                st.td = s.ShuttleTouchdownAt; st.level = s.ShuttleLevelledAt - st.td;
                st.offeredAtTd = s.CanEjectCrew();
                if ((mode == "touchdown" || mode == "last3m") && !clicked) { clicked = true; st.clickAt = W.Time - st.td; st.taken = input.EjectUnit(s); }   // K430b: L clicks again on the touchdown frame
                bool shotB = false, shotC = false, shotD = false, shotE = false; double outAbs = -1; float until = Time.unscaledTime + 14f;
                while (Time.unscaledTime < until)
                {
                    yield return null;
                    PointAt(s);
                    double now = W.Time, t = now - st.td;
                    if (st.cursorAtTd == "?" && t >= 0.03) { st.cursorAtTd = cursors ? cursors.Current : "none"; Shot($"k426_{st.tag}_a_touchdown_h70"); }   // the cursor over the ship just after the touchdown (a few frames: the ground's cursor, not the flight's), read BEFORE the first picture (its slow frame runs several rule ticks at once)
                    if (mode == "asap" && !clicked && s.CanEjectCrew()) { clicked = true; st.clickAt = t; st.taken = input.EjectUnit(s); }
                    if (now < s.ShuttleLevelledAt - 1e-6 && s.ShuttleRamp01 > 0) st.rampWhileTilted++;
                    if (rough && !shotB && t >= System.Math.Min(0.9, s.ShuttleLevelStartAt - st.td - 0.05)) { shotB = true; st.cursorTilted = cursors ? cursors.Current : "?"; Shot($"k426_{st.tag}_b_tilted_exit_pending_h70"); Log($"   +{F(t)} s tilted {ship.CrashRollNow:F1} deg: EXIT pending {s.ShuttleExitPending}, the ramp {s.ShuttleRampPhase}, aboard {s.Passengers.Count}, the cursor '{st.cursorTilted}'"); }
                    if (st.rampStart < 0 && s.ShuttleRampOpens != opens0)
                    {   // the rules' own start moment (it opened from shut): exact even when a slow frame (a picture being written) ran several rule ticks at once
                        st.rampStart = s.ShuttleRampPhase == ZShuttleRamp.Opening ? now - s.ShuttleRamp01 * ZWorld.ShuttleRampOpenSec - st.td : t;
                        st.openDeg = ShuttleRearDoor.LastOpenDeg;
                    }
                    if (!shotC && st.rampStart >= 0 && t >= st.rampStart + 0.5 * ZWorld.ShuttleRampOpenSec) { shotC = true; Shot($"k426_{st.tag}_c_ramp_coming_down_h70"); }
                    if (outAbs < 0 && clicked && s.Passengers.Count == 0)
                    {
                        outAbs = now; st.outAt = s.ShuttleFootTimes.Count > 0 ? s.ShuttleFootTimes[0] - ZWorld.ShuttleRampOutSec - st.td : t;   // the rules' exit moment (the first one's foot time less his walk), not this frame's
                        if (s.ShuttleFootTimes.Count > 0) { st.foot = s.ShuttleFootTimes[0] - st.td; st.door = st.foot - ZWorld.ShuttleRampOutSec + ZWorld.ShuttleExitDoorSec; st.made = s.ShuttleFootIds.Count; }
                        if (s.ShuttleFootIds.Count > 0) { var nl = W.Get(s.ShuttleFootIds[0]); if (nl != null) lead = nl; }   // the squad out is the one that boards again
                    }
                    if (!shotD && st.door >= 0 && t >= st.door + 0.3) { shotD = true; Shot($"k426_{st.tag}_d_first_robot_out_h70"); }
                    if (!shotE && st.foot >= 0 && t >= st.foot + 0.6) { shotE = true; Shot($"k426_{st.tag}_e_squad_out_h70"); }
                    if (shotE && outAbs >= 0) break;
                }
                st.doorSounds = Played(t0, "shuttle_door_open");
                st.ok = true;
                Log($"   {st.tag}: touchdown at {F(st.td)} s; level +{F(st.level)}; EXIT offered at the touchdown {st.offeredAtTd}, the cursor '{st.cursorAtTd}'; clicked +{F(st.clickAt)} s, taken {st.taken}{(st.airTaken ? " (in the air)" : "")}; the ramp down from +{F(st.rampStart)} s ({st.openDeg:F1} deg); the squad out +{F(st.outAt)} s ({st.made} of {st.aboard}); the first robot through the doorway +{F(st.door)} s, off the ramp foot +{F(st.foot)} s; the door's sound {st.doorSounds}");
                table.AppendLine($"{st.tag} | {(rough ? "crash" : "normal")}{(ZWorld.ShuttleExitSoonerOn ? "" : " (today's rules)")} | {mode} {F(st.clickAt)} | {st.cursorAtTd} | {st.taken} | {F(st.level)} | {F(st.rampStart)} | {F(st.outAt)} | {F(st.door)} | {F(st.foot)}");
                yield return Wait(1.0f);
                yield break;
            }
            Check($"{st.tag} the wanted landing came (4 tries)", false);
        }

        void Finish()
        {
            ZWorld.ShuttleExitSoonerOn = true; OzPlayableShuttle.ProofRoughNext = false;
            Log($"K426 result: {passes} PASS, {fails} FAIL");
            Report = fails == 0 ? $"PASS ({passes})" : $"FAIL ({fails} of {passes + fails})";
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "exit_sooner_proof_log.txt"), sb.ToString());
            Destroy(gameObject);
        }
    }
}
