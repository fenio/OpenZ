using System.Collections;
using System.Collections.Generic;
using System.Text;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace OpenZ
{
    /// <summary>
    /// K430b Play proof: no EXIT and no boarding offered while the shuttle is up in the air (UI/OzShuttleAirOffers.cs), on level 16 in Play
    /// (menu OpenZ/V1.1/Shuttle: K430b Air Cursors Proof, Editor/OzShuttleAirCursorsMenu.cs). Counted PASS / FAIL lines and full-screen
    /// pictures from the normal RTS camera at 70 m. Set-up: the CPU teams removed, morning (day clock 0.10, held), a red grunt squad of 4
    /// boards the ship. The game's pointer, its right button and the EXIT key are queued input events inside the game only (no desktop
    /// input); the cursor is the game's own (OpenZCursors.Current), read at the end of each frame with the rules' state of that frame, the
    /// EXIT CREW button is the HUD's own. Stages:
    ///  F a NORMAL landing, the ship selected, the pointer on it every frame: up in the air (the climb, the cruise, the landing turn, the
    ///    descent) never the EXIT cursor, never the EXIT CREW button; the EXIT key pressed in the cruise: nothing given, the refusal blip;
    ///    from the touchdown the EXIT cursor and the button; the right-click on the ship there: EXIT taken, the squad out;
    ///  C a CRASH landing, the same: up in the air never; from the touchdown, while it is still crooked (K426), the EXIT cursor and the
    ///    button; the EXIT key there: taken, the ramp shut while crooked, the squad out once it stands level;
    ///  B the ship with its pilot only, the squad (out on the ground) selected, the pointer on the ship: up in the air never the ENTER
    ///    cursor; the right-click on the ship in the cruise: no ENTER order (a move order for the squad, or nothing), nobody boards; from the
    ///    touchdown the ENTER cursor; the right-click there: the ENTER order, the squad boards;
    ///  G the test switch off for a moment in the cruise (the old offers): the EXIT cursor over the flying ship appears (as in the player's
    ///    picture), back on: gone; R the right-click on the flying ship with the ship selected: no EXIT, a move order to the spot under the
    ///    pointer (or the refusal blip where it can never land).
    /// Output: <see cref="Folder"/> (the work tree's records, milestones/shuttle_air_cursors_k430b, found upward at run time). Test only.
    /// </summary>
    public sealed class OzShuttleAirCursorsProof : MonoBehaviour
    {
        public static string Report = "idle";
        public static string Folder()
        {
            var d = new System.IO.DirectoryInfo(Application.dataPath);
            for (int i = 0; i < 8 && d != null; i++, d = d.Parent)
                if (System.IO.Directory.Exists(System.IO.Path.Combine(d.FullName, "work", "records")))
                    return System.IO.Path.Combine(d.FullName, "work", "records", "milestones", "shuttle_air_cursors_k430b");
            return System.IO.Path.Combine(Application.persistentDataPath, "shuttle_air_cursors_k430b");
        }
        public static void Run()
        {
            Report = "running";
#if UNITY_EDITOR
            var gv = System.Type.GetType("UnityEditor.GameView,UnityEditor"); if (gv != null) UnityEditor.EditorWindow.FocusWindowIfItsOpen(gv);
#endif
            new GameObject("OzShuttleAirCursorsProof").AddComponent<OzShuttleAirCursorsProof>();
        }

        ZGame g; ZWorld W; PlayerInput input; RtsCamera rts; OpenZHud hud; OpenZCursors cursors; Team me = Team.Red;
        ZObject s, lead; OzPlayableShuttle ship;
        readonly StringBuilder sb = new StringBuilder(); int fails, passes; string dir;

        /// <summary>Frames per phase of one flight: how many, with the EXIT cursor, with the EXIT CREW button, with the ENTER cursor.</summary>
        sealed class Tally
        {
            public readonly List<string> order = new List<string>();
            public readonly Dictionary<string, int[]> n = new Dictionary<string, int[]>();
            public int airFrames, airExit, airButton, airEnter, gndFrames, gndExit, gndButton, gndEnter;
            public void Add(string ph, bool air, bool exit, bool button, bool enter)
            {
                if (!n.TryGetValue(ph, out var a)) { a = new int[4]; n[ph] = a; order.Add(ph); }
                a[0]++; if (exit) a[1]++; if (button) a[2]++; if (enter) a[3]++;
                if (air) { airFrames++; if (exit) airExit++; if (button) airButton++; if (enter) airEnter++; }
                else { gndFrames++; if (exit) gndExit++; if (button) gndButton++; if (enter) gndEnter++; }
            }
            public string Text()
            {
                var t = new StringBuilder();
                foreach (var p in order) { var a = n[p]; t.AppendLine($"      {p,-34} {a[0],5} frames | cursor 'exit' {a[1],4} | EXIT CREW button {a[2],4} | cursor 'enter' {a[3],4}"); }
                return t.ToString().TrimEnd();
            }
        }

        void Start()
        {
            g = ZGame.I; W = g ? g.World : null; input = FindFirstObjectByType<PlayerInput>(); rts = FindFirstObjectByType<RtsCamera>();
            hud = OpenZHud.I; cursors = FindFirstObjectByType<OpenZCursors>();
            dir = Folder(); System.IO.Directory.CreateDirectory(dir);
            StartCoroutine(All());
        }

        void OnDestroy() { OzShuttleAirOffers.GroundOnly = true; OzPlayableShuttle.ProofRoughNext = false; }

        void Log(string t) { sb.AppendLine(t); Debug.Log("[OzShuttleAirCursorsProof] " + t); }
        void Check(string what, bool ok) { if (ok) passes++; else fails++; Log((ok ? "PASS " : "FAIL ") + what); }
        IEnumerator WaitFor(System.Func<bool> c, float max) { float t = 0f; while (t < max && !c()) { t += Time.deltaTime; yield return null; } }
        IEnumerator Wait(float s0) { float t = 0f; while (t < s0) { t += Time.deltaTime; yield return null; } }
        void Cam(Vector3 focus, float h) { if (!rts) return; rts.FreeLookOff(); rts.focus = new Vector3(focus.x, 0f, focus.z); rts.SetHeightImmediate(h); }
        void Select(params ZObject[] os) { input.Selection.Clear(); foreach (var o in os) if (o != null) input.Selection.Add(o); }
        bool Move(int x, int y) => W.SendWaypoints(me, s, new[] { new ZWaypoint { mode = WpMode.Move, x = x, y = y, playerGiven = true } });
        void Shot(string name) { ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, name + ".png")); Log("   picture " + name + ".png"); }
        static int Played(float t0, string key) { int n = 0; foreach (var e in OzSoundLog.Since(t0)) if (e.key == key && e.outcome != null && e.outcome.StartsWith("PLAY")) n++; return n; }
        Vector2 At(ZObject o) => input.ScreenRect(o).center;
        void PointAt(ZObject o) { if (Mouse.current == null || input == null || o == null) return; InputSystem.QueueStateEvent(Mouse.current, new MouseState { position = At(o) }); }
        void MouseEv(Vector2 p, bool right) { if (Mouse.current != null) InputSystem.QueueStateEvent(Mouse.current, new MouseState { position = p }.WithButton(MouseButton.Right, right)); }
        IEnumerator RightClick(ZObject o) { var p = At(o); MouseEv(p, false); yield return null; p = At(o); MouseEv(p, true); yield return null; yield return null; p = At(o); MouseEv(p, false); yield return null; yield return null; }
        IEnumerator PressExitKey() { var k = OzBindings.KeyOf(OzAction.ExitCrew); InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(k)); yield return null; yield return null; InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState()); yield return null; yield return null; }
        bool ButtonShown => hud && hud.ExitButtonRect && hud.ExitButtonRect.gameObject.activeSelf;
        string Cur => cursors ? cursors.Current : "?";
        static string F(double v) => v.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        static bool Alive(ZObject o) => o != null && !o.Destroyed && o.KillAt < 0;

        string PhaseNow(bool wasUp)
        {
            if (s.ShuttleAirborne)
            {
                switch (s.ShuttleState)
                {
                    case ZShuttleState.Climb: return "air: take-off climb";
                    case ZShuttleState.Descend: return "air: descent";
                    case ZShuttleState.Hover: return "air: hover";
                    case ZShuttleState.Cruise: return s.ShuttleLandTurning ? "air: landing turn" : "air: cruise";
                }
                return "air: lifting";
            }
            if (!wasUp) return "ground: before the take-off";
            return W.Time < s.ShuttleLevelledAt - 1e-9 ? "ground: crash, not level yet" : "ground: after the touchdown";
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

        /// <summary>The squad (out on the ground, or made new behind the ship) boards the ship and the ramp shuts again.</summary>
        IEnumerator Board()
        {
            if (s.Passengers.Count > 0) yield break;
            if (!Alive(lead)) { var (bx, by) = s.ShuttleBackPoint(6); lead = W.CreateRobotGroup("grunt", bx - 8, by - 8, me, 4); }
            yield return WaitFor(() => s.ShuttleRampPhase == ZShuttleRamp.Shut && W.Time >= s.ShuttleRampHoldUntil, 15f);
            W.SendWaypoints(me, lead, new[] { new ZWaypoint { mode = WpMode.Enter, x = s.CenterX, y = s.CenterY, refId = s.Id, playerGiven = true } });
            yield return WaitFor(() => s.Passengers.Count > 0, 60f);
            yield return WaitFor(() => s.ShuttleRampPhase == ZShuttleRamp.Shut && W.Time >= s.ShuttleRampHoldUntil, 20f);
            yield return Wait(0.5f);
        }

        IEnumerator All()
        {
            Application.targetFrameRate = 60;
            if (W == null || input == null || cursors == null || hud == null) { Log("no game (level 16 in Play)"); fails++; Finish(); yield break; }
            if (!ZWorld.ShuttlePlayableDeviation || W.PlayableShuttle == null) { Log("no playable shuttle (level 16, the shuttle rule on, the option SHUTTLE on)"); fails++; Finish(); yield break; }
            s = W.PlayableShuttle; ship = OzPlayableShuttle.Of(s);
            if (!ship) { Log("the shuttle has no view"); fails++; Finish(); yield break; }
            Log($"K430b air cursors proof, {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}, {Screen.width}x{Screen.height}, world time {F(W.Time)} s; the K426 switch {ZWorld.ShuttleExitSoonerOn}; the gate {OzShuttleAirOffers.GroundOnly}");
            int bots = W.Bots.Count; W.Bots.Clear();
            var dn = FindFirstObjectByType<DayNight>(); if (dn) { dn.timeOfDay = 0.10f; dn.Apply(0.10f); dn.running = false; }
            Log($"set-up: CPU removed {bots}, day 0.10 held");
            Check("E0 the gate is on (EXIT and boarding only on the ground)", OzShuttleAirOffers.GroundOnly);
            yield return WaitFor(() => s.ShuttleState == ZShuttleState.Landed && !s.ShuttleAirborne && s.ShuttleLevelled, 30f);
            yield return Board();
            Check($"E0 a squad of 4 aboard ({s.Passengers.Count})", s.Passengers.Count == 4);
            yield return StageF(false);
            yield return StageF(true);
            yield return StageB();
            yield return StageGR();
            Finish();
        }

        /// <summary>F (normal) / C (crash): the ship selected, a squad aboard, flown to a free spot.</summary>
        IEnumerator StageF(bool rough)
        {
            string tag = rough ? "C" : "F";
            for (int tryN = 0; tryN < 4; tryN++)
            {
                yield return WaitFor(() => s.ShuttleState == ZShuttleState.Landed && s.ShuttleLevelled && !ship.CrashOn && W.Time >= s.ShuttleRampHoldUntil && s.ShuttleRampPhase == ZShuttleRamp.Shut, 30f);
                yield return Board();
                if (s.Passengers.Count == 0) { Check($"{tag} a squad aboard", false); yield break; }
                var q = Free(s.CenterX, s.CenterY, 320, 700); if (q == null) { Check($"{tag} a landing place", false); yield break; }
                Select(s); PointAt(s); yield return null;
                OzPlayableShuttle.ProofRoughNext = rough;
                Move(q[0], q[1]);
                var t = new Tally(); bool up = false, keyDone = false, shotAir = false; float keyAt = -10f; double upAt = -1; int pax0 = s.Passengers.Count; bool keyGiven = false; int keyPax = -1;
                float until = Time.unscaledTime + 70f;
                while (Time.unscaledTime < until && !(up && !s.ShuttleAirborne))
                {
                    Cam(ship.transform.position, 70f); PointAt(s);
                    yield return new WaitForEndOfFrame();
                    if (s.ShuttleAirborne && !up) { up = true; upAt = W.Time; }
                    string ph = PhaseNow(up);
                    if (up && s.ShuttleAirborne) t.Add(ph, true, Cur == "exit", ButtonShown, Cur == "enter");
                    if (!shotAir && ph == "air: cruise" && W.Time - upAt > ZWorld.ShuttleClimbSec + 0.8) { shotAir = true; Shot($"k430b_{tag}_a_flying_ship_selected_cursor_{Cur}_h70"); Log($"   in the cruise: the cursor over the ship '{Cur}', the EXIT CREW button {ButtonShown}, the rules would take EXIT {s.CanEjectCrew()}"); }
                    if (!keyDone && shotAir && ph == "air: cruise")
                    {   // the EXIT key in the cruise
                        keyDone = true; keyAt = Time.unscaledTime;
                        yield return PressExitKey();
                        keyGiven = s.ShuttleExitOnLanding || s.ShuttleExitPending; keyPax = s.Passengers.Count;
                        Log($"   the EXIT key in the cruise: EXIT on landing {s.ShuttleExitOnLanding}, pending {s.ShuttleExitPending}, aboard {keyPax}; the refusal blip {Played(keyAt, "ui_order_invalid")}");
                    }
                }
                OzPlayableShuttle.ProofRoughNext = false;
                if (s.ShuttleState != ZShuttleState.Landed || s.ShuttleAirborne) { Log($"   it did not land (state {s.ShuttleState}, '{s.ShuttleNoLand}'): another flight"); continue; }
                if (s.ShuttleRoughLanding != rough) { Log("   the rules made this landing " + (s.ShuttleRoughLanding ? "a crash" : "a normal one") + ": another flight"); yield return WaitFor(() => s.ShuttleLevelled && s.ShuttleRampPhase == ZShuttleRamp.Shut, 20f); continue; }
                double td = s.ShuttleTouchdownAt;
                // on the ground: the cursor and the button from the touchdown (the first two frames are the change), while crooked too
                int gnd = 0, gndExit = 0, gndButton = 0; float g0 = Time.unscaledTime;
                while (W.Time - td < (rough ? 0.6 : 0.4))
                {
                    PointAt(s); yield return new WaitForEndOfFrame();
                    if (Time.unscaledTime - g0 < 0.05f) continue;
                    gnd++; if (Cur == "exit") gndExit++; if (ButtonShown) gndButton++;
                    t.Add(PhaseNow(true), false, Cur == "exit", ButtonShown, Cur == "enter");
                }
                bool crooked = W.Time < s.ShuttleLevelledAt - 1e-9;
                string curTd = Cur; Shot($"k430b_{tag}_b_touchdown{(rough ? "_crooked" : "")}_cursor_{curTd}_h70");
                Log($"   {tag}: the touchdown at {F(td)} s, level +{F(s.ShuttleLevelledAt - td)} s; frames per phase:"); Log(t.Text());
                Check($"{tag} up in the air ({t.airFrames} frames: climb, cruise, landing turn, descent): the EXIT cursor over the selected ship on {t.airExit} frames, the EXIT CREW button on {t.airButton} (one frame's redraw allowed)",
                      t.airFrames > 60 && t.airExit == 0 && t.airButton <= 1);
                Check($"{tag} the EXIT key in the cruise: nothing given (EXIT on landing / pending {keyGiven}, aboard {keyPax} of {pax0}), the refusal blip played ({Played(keyAt, "ui_order_invalid")})",
                      keyDone && !keyGiven && keyPax == pax0 && Played(keyAt, "ui_order_invalid") > 0);
                // EXIT on the ground: F the right-click on the ship, C the EXIT key while it is still crooked
                int opens0 = s.ShuttleRampOpens; float x0 = Time.unscaledTime; int rampWhileCrooked = 0;
                if (rough) yield return PressExitKey(); else yield return RightClick(s);
                bool taken = s.ShuttleExitPending || s.Passengers.Count < pax0 || s.ShuttleRampOpens != opens0;
                double takenAt = W.Time - td;
                double outAt = -1; float u2 = Time.unscaledTime + 14f;
                while (Time.unscaledTime < u2 && s.Passengers.Count > 0)
                {
                    if (W.Time < s.ShuttleLevelledAt - 1e-6 && s.ShuttleRamp01 > 0) rampWhileCrooked++;
                    yield return null;
                }
                if (s.Passengers.Count == 0) outAt = W.Time - td;
                yield return Wait(1.2f); Shot($"k430b_{tag}_c_squad_out_h70");
                Check($"{tag} from the touchdown{(rough ? $" (still crooked {crooked}: K426)" : "")}: the EXIT cursor on {gndExit} of {gnd} frames, the EXIT CREW button on {gndButton}; the {(rough ? "EXIT key" : "right-click on the ship")} at +{F(takenAt)} s taken {taken}; the ramp moved while crooked on {rampWhileCrooked} frames; the squad out +{F(outAt)} s; the door's sound {Played(x0, "shuttle_door_open")}",
                      gnd > 3 && gndExit == gnd && gndButton >= gnd - 1 && taken && rampWhileCrooked == 0 && outAt >= 0 && Played(x0, "shuttle_door_open") > 0 && (!rough || crooked));
                if (s.ShuttleFootIds.Count > 0) { var nl = W.Get(s.ShuttleFootIds[0]); if (nl != null) lead = nl.Leader ?? nl; }   // the squad out is the one that boards again (and is selected in B)
                yield break;
            }
            Check($"{tag} the wanted landing came (4 tries)", false);
        }

        /// <summary>B: the ship with its pilot only, the squad selected, the pointer on the ship.</summary>
        IEnumerator StageB()
        {
            yield return WaitFor(() => s.ShuttleState == ZShuttleState.Landed && s.ShuttleLevelled && W.Time >= s.ShuttleRampHoldUntil && s.ShuttleRampPhase == ZShuttleRamp.Shut, 30f);
            if (s.Passengers.Count > 0) { Check("B the ship empty of passengers", false); yield break; }
            if (!Alive(lead)) { var (bx, by) = s.ShuttleBackPoint(6); lead = W.CreateRobotGroup("grunt", bx - 8, by - 8, me, 4); yield return Wait(0.5f); }
            for (int tryN = 0; tryN < 4; tryN++)
            {
                var q = Free(s.CenterX, s.CenterY, 360, 700); if (q == null) { Check("B a landing place", false); yield break; }
                Select(lead); PointAt(s); yield return null;
                Move(q[0], q[1]);                                                                      // the ship's own order (the squad stays selected)
                var t = new Tally(); bool up = false, clicked = false; double upAt = -1; bool enterWp = false; string clickWp = "-"; bool shot = false;
                float until = Time.unscaledTime + 70f;
                while (Time.unscaledTime < until && !(up && !s.ShuttleAirborne))
                {
                    Cam(ship.transform.position, 70f); PointAt(s);
                    yield return new WaitForEndOfFrame();
                    if (s.ShuttleAirborne && !up) { up = true; upAt = W.Time; }
                    string ph = PhaseNow(up);
                    if (up && s.ShuttleAirborne) t.Add(ph, true, Cur == "exit", ButtonShown, Cur == "enter");
                    if (!shot && ph == "air: cruise" && W.Time - upAt > ZWorld.ShuttleClimbSec + 0.8) { shot = true; Shot($"k430b_B_a_flying_robots_selected_cursor_{Cur}_h70"); }
                    if (!clicked && shot && ph == "air: cruise")
                    {   // the right-click on the flying ship with the squad selected
                        clicked = true;
                        yield return RightClick(s);
                        enterWp = lead.Waypoints.Exists(x => x.mode == WpMode.Enter && x.refId == s.Id);
                        clickWp = lead.Waypoints.Count > 0 ? lead.Waypoints[0].mode.ToString() : "none";
                        Log($"   the right-click on the flying ship, the squad selected: its order {clickWp}, an ENTER order on the ship {enterWp}");
                    }
                }
                if (s.ShuttleState != ZShuttleState.Landed || s.ShuttleAirborne) { Log($"   it did not land (state {s.ShuttleState}, '{s.ShuttleNoLand}'): another flight"); continue; }
                double td = s.ShuttleTouchdownAt;
                int gnd = 0, gndEnter = 0; float g0 = Time.unscaledTime;
                while (W.Time - td < 0.4)
                {
                    PointAt(s); yield return new WaitForEndOfFrame();
                    if (Time.unscaledTime - g0 < 0.05f) continue;
                    gnd++; if (Cur == "enter") gndEnter++;
                    t.Add(PhaseNow(true), false, Cur == "exit", ButtonShown, Cur == "enter");
                }
                Shot($"k430b_B_b_landed_robots_selected_cursor_{Cur}_h70");
                Log($"   B: the touchdown at {F(td)} s; frames per phase:"); Log(t.Text());
                Check($"B up in the air ({t.airFrames} frames): the ENTER cursor over the ship on {t.airEnter} frames, the squad selected",
                      t.airFrames > 60 && t.airEnter == 0);
                Check($"B the right-click on the flying ship: no ENTER order ({enterWp}; the squad's order {clickWp}), nobody aboard ({s.Passengers.Count})",
                      clicked && !enterWp && s.Passengers.Count == 0);
                yield return RightClick(s);
                bool entered = lead.Waypoints.Exists(x => x.mode == WpMode.Enter && x.refId == s.Id);
                yield return WaitFor(() => s.Passengers.Count > 0, 90f);
                yield return Wait(0.5f); Shot("k430b_B_c_boarded_h70");
                Check($"B from the touchdown: the ENTER cursor on {gndEnter} of {gnd} frames; the right-click on the ship: the ENTER order {entered}, aboard {s.Passengers.Count}",
                      gnd > 3 && gndEnter == gnd && entered && s.Passengers.Count > 0);
                yield break;
            }
            Check("B the wanted landing came (4 tries)", false);
        }

        /// <summary>G: the test switch off for a moment in the cruise (the old offers); R: the right-click on the flying ship, the ship selected.</summary>
        IEnumerator StageGR()
        {
            yield return WaitFor(() => s.ShuttleState == ZShuttleState.Landed && s.ShuttleLevelled && W.Time >= s.ShuttleRampHoldUntil && s.ShuttleRampPhase == ZShuttleRamp.Shut, 30f);
            yield return Board();
            if (s.Passengers.Count == 0) { Check("G a squad aboard", false); yield break; }
            var q = Free(s.CenterX, s.CenterY, 480, 900); if (q == null) { Check("G a landing place", false); yield break; }
            Select(s); PointAt(s); yield return null;
            Move(q[0], q[1]);
            double upAt = -1;
            yield return WaitFor(() => { Cam(ship.transform.position, 70f); PointAt(s); if (s.ShuttleAirborne && upAt < 0) upAt = W.Time; return s.ShuttleState == ZShuttleState.Cruise && upAt >= 0 && W.Time - upAt > ZWorld.ShuttleClimbSec + 0.8; }, 20f);
            yield return WaitFor(() => { Cam(ship.transform.position, 70f); PointAt(s); return s.CanEjectCrew(); }, 3f);
            string on1 = Cur; bool rules = s.CanEjectCrew();
            OzShuttleAirOffers.GroundOnly = false;
            string off = "?"; bool offButton = false;
            for (int i = 0; i < 8; i++) { Cam(ship.transform.position, 70f); PointAt(s); yield return new WaitForEndOfFrame(); off = Cur; offButton = ButtonShown; }
            Shot($"k430b_G_switch_off_old_offer_cursor_{off}_h70");
            OzShuttleAirOffers.GroundOnly = true;
            string on2 = "?"; bool onButton = true;
            for (int i = 0; i < 8; i++) { Cam(ship.transform.position, 70f); PointAt(s); yield return new WaitForEndOfFrame(); on2 = Cur; onButton = ButtonShown; }
            Shot($"k430b_G_switch_on_cursor_{on2}_h70");
            Log($"   G in the cruise (the rules would take EXIT {rules}): the cursor '{on1}' -> switch off '{off}' (button {offButton}) -> on '{on2}' (button {onButton})");
            Check($"G the old offers for a moment: the EXIT cursor over the flying ship '{off}' and the button {offButton} (as in the player's picture); the gate back on: '{on2}', button {onButton}",
                  rules && off == "exit" && offButton && on2 != "exit" && !onButton);
            // R: the right-click on the flying ship, the ship selected
            int gx0 = s.ShuttleGoalX, gy0 = s.ShuttleGoalY, pax = s.Passengers.Count, ref0 = OzShuttleOrderAim.RefusedClicks; float r0 = Time.unscaledTime;
            string before = Cur;
            yield return RightClick(s);
            yield return null;
            bool moved = s.ShuttleGoalX != gx0 || s.ShuttleGoalY != gy0, refused = OzShuttleOrderAim.RefusedClicks > ref0;
            Log($"   R the right-click on the flying ship (the cursor '{before}'): EXIT on landing {s.ShuttleExitOnLanding}, aboard {s.Passengers.Count}; its goal {gx0},{gy0} -> {s.ShuttleGoalX},{s.ShuttleGoalY} (a move order {moved}), refused as a spot it can never land on {refused} (blip {Played(r0, "ui_order_invalid")})");
            Check($"R the right-click on the flying ship, the ship selected: no EXIT (on landing {s.ShuttleExitOnLanding}, aboard {s.Passengers.Count} of {pax}); a move order to the spot under the pointer {moved} or the refusal {refused}",
                  before != "exit" && !s.ShuttleExitOnLanding && !s.ShuttleExitPending && s.Passengers.Count == pax && (moved || (refused && Played(r0, "ui_order_invalid") > 0)));
            yield return WaitFor(() => { Cam(ship.transform.position, 70f); return !s.ShuttleAirborne || s.ShuttleState == ZShuttleState.Hover; }, 30f);
            yield return Wait(0.5f); Shot("k430b_R_after_the_right_click_h70");
            Log($"   R then: state {s.ShuttleState}{(s.ShuttleState == ZShuttleState.Hover ? " ('" + s.ShuttleNoLand + "')" : "")} at {s.CenterX},{s.CenterY}");
        }

        void Finish()
        {
            OzShuttleAirOffers.GroundOnly = true; OzPlayableShuttle.ProofRoughNext = false;
            Log($"K430b result: {passes} PASS, {fails} FAIL");
            Report = fails == 0 ? $"PASS ({passes})" : $"FAIL ({fails} of {passes + fails})";
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "air_cursors_proof_log.txt"), sb.ToString());
            Destroy(gameObject);
        }
    }
}
