using System.Collections;
using System.Linq;
using System.Text;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace OpenZ
{
    /// <summary>
    /// K256f Play proof: the EMPTY shuttle and EXIT in the air (rule DV118, Sim/ZWorld.ShuttleEmpty.cs), on level 16 in Play (menu
    /// OpenZ/V1.1/Shuttle: K256f Empty Shuttle Proof, Editor/OzShuttleEmptyMenu.cs). Counted PASS / FAIL lines and full-screen pictures from
    /// the normal RTS camera at 70 m (uncropped). Set-up: the CPU teams removed, morning (day clock 0.10, held). The game's pointer is moved
    /// by queued input events inside the game only (as the testing panel's own self-test does): no desktop input. Stages:
    ///  P1 the testing panel (P): its EMPTY row offers the shuttle (a grey tile);
    ///  P2 the panel's own drop of an empty one (OzCheatShuttle.Spawn, the null team) beside the camp: dark (lamps, neon, exhaust off, by day
    ///     and at night), grey, the unit panel's line "EMPTY ...";
    ///  P3 a red grunt squad ordered in: the ramp comes down, they walk up it, the leader is the pilot, the 2 others the passengers, the ship
    ///     red and lit once they sit; the unit panel's line;
    ///  P4 landed, selected: the EXIT button and the EXIT cursor over the ship;
    ///  P5 a flight onto water: every frame the rules' EXIT (K256f) follows the landing test at the spot it would stop
    ///     (OzPlayableShuttle.LandableAt), and the EXIT button and the EXIT cursor are never offered up in the air (K430b, UI/OzShuttleAirOffers.cs);
    ///     over the water while it hovers: no button, no EXIT cursor;
    ///  P6 onto a spot a robot stands on: it hovers, no EXIT; the robot goes: the rules would take EXIT again, but no button and no cursor up
    ///     in the air, the button's own call refused (K430b); the rules' own EXIT (K256f's path, kept for the checks): it comes down there,
    ///     the 2 passengers walk down the ramp, the pilot stays;
    ///  P7 a lone robot ordered into a second empty one: its pilot only, no EXIT button, no EXIT cursor.
    /// Output: <see cref="Folder"/> (the work tree's records, milestones/shuttle_empty_proof, found upward at run time). Test only.
    /// </summary>
    public sealed class OzShuttleEmptyProof : MonoBehaviour
    {
        public static string Report = "idle";
        public static string Folder()
        {
            var d = new System.IO.DirectoryInfo(Application.dataPath);
            for (int i = 0; i < 8 && d != null; i++, d = d.Parent)
                if (System.IO.Directory.Exists(System.IO.Path.Combine(d.FullName, "work", "records")))
                    return System.IO.Path.Combine(d.FullName, "work", "records", "milestones", "shuttle_empty_proof");
            return System.IO.Path.Combine(Application.persistentDataPath, "shuttle_empty_proof");
        }
        public static void Run()
        {
            Report = "running";
#if UNITY_EDITOR
            var gv = System.Type.GetType("UnityEditor.GameView,UnityEditor"); if (gv != null) UnityEditor.EditorWindow.FocusWindowIfItsOpen(gv);
#endif
            new GameObject("OzShuttleEmptyProof").AddComponent<OzShuttleEmptyProof>();
        }

        ZGame g; ZWorld W; PlayerInput input; RtsCamera rts; DayNight dn; OpenZHud hud; OpenZCursors cursors; Team me = Team.Red;
        ZObject s; OzPlayableShuttle ship;
        readonly StringBuilder sb = new StringBuilder(); int fails, passes, frames; string dir;
        // per frame, while a flight is watched
        bool watch, lastOff; int samples, offered, buttonMismatch, landMismatch;

        void Start()
        {
            g = ZGame.I; W = g ? g.World : null; input = FindFirstObjectByType<PlayerInput>(); rts = FindFirstObjectByType<RtsCamera>();
            hud = OpenZHud.I; cursors = FindFirstObjectByType<OpenZCursors>();
            dir = Folder(); System.IO.Directory.CreateDirectory(dir);
            StartCoroutine(All());
        }

        void Log(string t) { sb.AppendLine(t); Debug.Log("[OzShuttleEmptyProof] " + t); }
        void Check(string what, bool ok) { if (ok) passes++; else fails++; Log((ok ? "PASS " : "FAIL ") + what); }
        void Shot(string name) { ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, name + ".png")); frames++; Log($"frame {name}.png"); }
        IEnumerator WaitFor(System.Func<bool> c, float max) { float t = 0f; while (t < max && !c()) { t += Time.deltaTime; yield return null; } }
        IEnumerator Wait(float s0) { float t = 0f; while (t < s0) { t += Time.deltaTime; yield return null; } }
        void Cam(Vector3 focus, float h) { if (!rts) return; rts.FreeLookOff(); rts.focus = new Vector3(focus.x, 0f, focus.z); rts.SetHeightImmediate(h); }
        Vector3 Wp(int x, int y) => ZGame.PxToWorld(x, y);
        ZObject Squad(string type, int tx, int ty, int amount = -1)
        {
            OzCheatSpawner.FreeTile(W, tx, ty, 1, true, out tx, out ty);
            var o = W.CreateRobotGroup(type, tx * 16, ty * 16, me, amount); if (o == null) return null;
            g.SpawnUnitView(o); foreach (var m in o.Minions) g.SpawnUnitView(m);
            return o;
        }
        bool Move(ZObject o, int x, int y) => W.SendWaypoints(me, o, new[] { new ZWaypoint { mode = WpMode.Move, x = x, y = y, playerGiven = true } });
        bool Enter(ZObject sq, ZObject ship0) => sq != null && W.SendWaypoints(me, sq, new[] { new ZWaypoint { mode = WpMode.Enter, refId = ship0.Id, x = ship0.CenterX, y = ship0.CenterY, playerGiven = true } });
        void Select(ZObject o) { input.Selection.Clear(); if (o != null) input.Selection.Add(o); }
        bool ButtonShown => hud && hud.ExitButtonRect && hud.ExitButtonRect.gameObject.activeSelf;

        /// <summary>The game's own pointer onto the selected ship (a queued input event inside the game: the panel self-test's way).</summary>
        void PointAt(ZObject o)
        {
            if (Mouse.current == null || input == null || o == null) return;
            var r = input.ScreenRect(o);
            InputSystem.QueueStateEvent(Mouse.current, new MouseState { position = r.center });
        }
        /// <summary>The cursor the game shows with its pointer on the ship (two frames after the pointer moved there).</summary>
        IEnumerator CursorOn(ZObject o, System.Action<string> got) { PointAt(o); yield return null; yield return null; PointAt(o); yield return null; got(cursors ? cursors.Current : "?"); }

        bool LandableNow(out string why)
        {
            var (x, y) = s.ShuttleExitSpot(); var wpt = Wp(x, y);
            return OzPlayableShuttle.LandableAt(W, s, wpt, out why);
        }

        void Update()
        {
            if (!watch || W == null || s == null || s.Destroyed) return;
            if (!s.ShuttleAirborne || s.ShuttleState == ZShuttleState.Climb || s.ShuttleState == ZShuttleState.Descend || s.ShuttleLandTurning) return;
            samples++;
            bool can = s.CanEjectCrew(), land = LandableNow(out _);
            if (can) offered++;
            if (can != land) landMismatch++;
            bool off = input && input.Selection.Count == 1 && input.Selection[0] == s && hud && ButtonShown != OzShuttleAirOffers.Exit(s);   // K430b: the button follows the player's offer (never up in the air)
            if (off && lastOff) buttonMismatch++;                                                   // the HUD redraws its button in its own Update: one frame behind at a change is allowed
            lastOff = off;
        }

        /// <summary>A free landing spot (ring by ring from (cx, cy), between minR and maxR px; robots may stand round it).</summary>
        int[] Spot(int cx, int cy, int minR, int maxR, ZObject self)
        {
            for (int r = minR; r <= maxR; r += 16)
                for (int a = 0; a < 32; a++)
                {
                    double t = a * System.Math.PI / 16;
                    var q = W.ShuttleClamp(cx + (int)(System.Math.Cos(t) * r), cy + (int)(System.Math.Sin(t) * r));
                    if (W.ShuttlePadFree(q.x, q.y, self, out _)) return new[] { q.x, q.y };
                }
            return null;
        }

        int[] Water(int cx, int cy)
        {
            long best = long.MaxValue; int[] r = null;
            for (int ty = 3; ty < W.Map.height - 3; ty++)
                for (int tx = 3; tx < W.Map.width - 3; tx++)
                {
                    var c = W.Map.ClassAt(tx, ty); if (c != ZClass.Water && c != ZClass.DeepWater) continue;
                    int x = tx * 16 + 8, y = ty * 16 + 8; long d = (long)(x - cx) * (x - cx) + (long)(y - cy) * (y - cy);
                    if (d >= best) continue;
                    if (!W.ShuttlePadFree(x, y, s, out string why) && why == "water") { best = d; r = new[] { x, y }; }
                }
            return r;
        }

        IEnumerator All()
        {
            Application.targetFrameRate = 60;
            if (W == null || hud == null || input == null) { Log("no game (level 16 in Play)"); fails++; Finish(); yield break; }
            if (!ZWorld.ShuttlePlayableDeviation) { Log("the shuttle rule is off ('Zod exact')"); fails++; Finish(); yield break; }
            me = g.player;
            Log($"K256f empty shuttle proof, {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}, {Screen.width}x{Screen.height}, world time {W.Time:F1} s");
            int bots = W.Bots.Count; W.Bots.Clear();
            dn = FindFirstObjectByType<DayNight>(); if (dn) { dn.timeOfDay = 0.10f; dn.Apply(0.10f); dn.running = false; }
            Log($"set-up: CPU removed {bots}, day 0.10 held");
            yield return Wait(0.5f);
            yield return P1(); yield return P2(); yield return P3(); yield return P4(); yield return P5(); yield return P6(); yield return P7();
            Finish();
        }

        // ------------------------------------------------------------------ P1: the testing panel's empty row
        IEnumerator P1()
        {
            var cs = OzCheatSpawner.I;
            if (!cs) { Check("P1 the testing panel is installed (the dev flag on)", false); yield break; }
            cs.Show(true); yield return Wait(0.4f);
            var row = GameObject.Find("CheatNeutral"); var tile = row ? row.transform.Find("Cell_" + ZWorld.ShuttleType) : null;
            var art = tile ? tile.GetComponentInChildren<UnityEngine.UI.RawImage>() : null;
            Shot("k256f_p1_panel_empty_row");
            Check("P1 the testing panel's EMPTY row offers the shuttle (a tile with its picture)", tile != null && art != null && art.texture != null);
            cs.Show(false); yield return Wait(0.3f);
        }

        // ------------------------------------------------------------------ P2: the panel's own empty drop
        IEnumerator P2()
        {
            var own = W.PlayableShuttle;
            int cx = own != null ? own.CenterX : W.Map.WidthPix / 2, cy = own != null ? own.CenterY : W.Map.HeightPix / 2;
            var q = Spot(cx, cy, 300, 700, null);
            if (q == null) { Check("P2 a free place for the drop", false); yield break; }
            s = OzCheatShuttle.Spawn(g, hud, Team.Neutral, new Vector2Int(q[0], q[1]));
            ship = s != null ? OzPlayableShuttle.Of(s) : null;
            if (s == null || !ship) { Check("P2 the panel drops an empty shuttle with its view", false); yield break; }
            Cam(ship.transform.position, 70f); yield return Wait(1.2f);
            var look = ship.GetComponent<OzShuttleEmptyLook>();
            string line = OzShuttleHud.Line(s);
            Select(null);
            Shot("k256f_p2_empty_day_h70");
            Log($"dropped #{s.Id} at {s.CenterX},{s.CenterY}: {s.Owner}, pilots {s.Drivers.Count}, lights mode {(look && look.Lights ? look.Lights.Mode : -1)}, exhaust mode {(look && look.Exhaust ? look.Exhaust.Mode : -1)}; the unit panel's line '{line}'");
            Check("P2 the panel's drop is an EMPTY shuttle (the null team, no pilot), drawn", s.ShuttleIsEmpty && s.Owner == Team.Neutral && ship);
            Check("P2 it is dark: lamps, neon and exhaust off (as an empty vehicle)", look && look.IsDark && look.Lights && look.Lights.Mode == 2 && (!look.Exhaust || look.Exhaust.Mode == 2));
            Check("P2 the unit panel's line says EMPTY", line.StartsWith("EMPTY"));
            if (dn) { dn.timeOfDay = 0.62f; dn.Apply(0.62f); }
            yield return Wait(1.5f); Shot("k256f_p2_empty_night_h70");
            Check("P2 at night it stays dark (no pilot to light it)", look && look.IsDark && OzShuttleEmptyLook.Dark(s));
            if (dn) { dn.timeOfDay = 0.10f; dn.Apply(0.10f); }
            yield return Wait(0.8f);
        }

        // ------------------------------------------------------------------ P3: a squad ordered in
        IEnumerator P3()
        {
            if (s == null) yield break;
            double h = s.ShuttleHeadingDeg * System.Math.PI / 180.0;
            var sq = Squad("grunt", (int)(s.CenterX - System.Math.Cos(h) * 96) >> 4, (int)(s.CenterY - System.Math.Sin(h) * 96) >> 4); yield return Wait(0.6f);
            if (sq == null) { Check("P3 a squad", false); yield break; }
            int n = 1 + sq.Minions.Count;
            Check("P3 the ENTER order on the empty ship is taken", Enter(sq, s));
            yield return WaitFor(() => s.ShuttleRampPhase != ZShuttleRamp.Shut, 20f); yield return Wait(0.7f);
            Shot("k256f_p3_ramp_down_squad_walks_h70");
            yield return WaitFor(() => s.Drivers.Count > 0, 20f); yield return Wait(0.6f);
            Shot("k256f_p3_squad_up_the_ramp_h70");
            yield return WaitFor(() => s.ShuttleRampPhase == ZShuttleRamp.Shut && W.Time >= s.ShuttleRampHoldUntil, 20f); yield return Wait(0.8f);
            var look = ship.GetComponent<OzShuttleEmptyLook>();
            Select(s); yield return Wait(0.3f);
            string line = OzShuttleHud.Line(s);
            Shot("k256f_p3_taken_lit_red_h70");
            Log($"after the boarding: pilot {s.Drivers.Count} ({s.DriverType}), passengers {s.Passengers.Count} of {n}, the ship {s.Owner}, lights mode {(look && look.Lights ? look.Lights.Mode : -1)}, neon team {(look && look.Lights ? look.Lights.NeonTeam.ToString() : "-")}; line '{line}'");
            Check("P3 the squad of 3 boards up the ramp: the leader the pilot, 2 passengers, the ship the squad's team", s.Drivers.Count == 1 && s.Passengers.Count == n - 1 && s.Owner == me);
            Check("P3 the ship is lit in the team's colours once they sit", look && !look.IsDark && look.Lights && look.Lights.Mode == 0 && look.Lights.NeonTeam == TeamColors.Shown(me));
            Check("P3 the unit panel's line: the pilot and the passengers", line.StartsWith("PILOT") && line.Contains("PASSENGERS"));
        }

        // ------------------------------------------------------------------ P4: landed: the EXIT button and cursor
        IEnumerator P4()
        {
            if (s == null || s.Passengers.Count == 0) yield break;
            Select(s); yield return WaitFor(() => s.CanEjectCrew(), 6f); yield return Wait(0.2f);
            string cur = "?"; yield return CursorOn(s, c => cur = c);
            Shot("k256f_p4_landed_exit_button_cursor_h70");
            Log($"landed, selected: CanEjectCrew {s.CanEjectCrew()}, EXIT button shown {ButtonShown}, the cursor on the ship '{cur}'");
            Check("P4 landed with passengers: the EXIT button shown and the EXIT cursor on the ship", s.CanEjectCrew() && ButtonShown && cur == "exit");
        }

        // ------------------------------------------------------------------ P5: a flight onto water
        IEnumerator P5()
        {
            if (s == null || s.Passengers.Count == 0) yield break;
            var wq = Water(s.CenterX, s.CenterY);
            if (wq == null) { Check("P5 water on the level", false); yield break; }
            Select(s);
            samples = offered = buttonMismatch = landMismatch = 0; lastOff = false; watch = true;
            Check("P5 the move order onto the water is taken", Move(s, wq[0], wq[1]));
            bool shotGround = false;
            float t = 0f;
            while (t < 60f && !(s.ShuttleState == ZShuttleState.Hover && s.ShuttleNoLand == "water"))
            {
                Cam(ship.transform.position, 70f);
                if (!shotGround && s.ShuttleState == ZShuttleState.Cruise && s.CanEjectCrew())
                {   // K430b: where the rules would take EXIT (K256f), the player is offered none up in the air
                    string c0 = "?"; yield return CursorOn(s, c => c0 = c);
                    Shot("k256f_p5_air_over_ground_no_exit_offered_k430b_h70"); shotGround = true;
                    Log($"in the air over free ground (the rules would take EXIT {s.CanEjectCrew()}): EXIT button {ButtonShown}, the cursor '{c0}'");
                    Check("P5 in the air where it may land: no EXIT button, no EXIT cursor (K430b: no EXIT in the sky)", !ButtonShown && c0 != "exit");
                }
                t += Time.deltaTime; yield return null;
            }
            yield return Wait(1.0f);
            string cur = "?"; yield return CursorOn(s, c => cur = c);
            Shot("k256f_p5_hover_over_water_no_exit_h70");
            watch = false;
            Log($"the flight: {samples} frames in the air: EXIT offered in {offered}; the button against the rules {buttonMismatch}, the rules against the landing test (LandableAt at the stop spot) {landMismatch}; hovering over water: button {ButtonShown}, cursor '{cur}'");
            Check("P5 every frame of the flight the rules' EXIT follows the landing test at the spot it would stop, and the EXIT button is never shown up in the air (K430b) (0 frames off, one frame's redraw allowed)", samples > 30 && buttonMismatch == 0 && landMismatch == 0 && offered > 0 && offered < samples);
            Check("P5 hovering over the water: no EXIT button, no EXIT cursor", s.ShuttleState == ZShuttleState.Hover && !ButtonShown && cur != "exit" && !s.CanEjectCrew());
        }

        // ------------------------------------------------------------------ P6: a robot on the spot, then EXIT in the air
        IEnumerator P6()
        {
            if (s == null || s.Passengers.Count == 0) yield break;
            var q = Spot(s.CenterX, s.CenterY, 300, 700, s);
            if (q == null) { Check("P6 a free place", false); yield break; }
            var blocker = W.CreateRobotGroup("sniper", q[0] - 8, q[1] - 8, me, 1); g.SpawnUnitView(blocker); blocker.MoveSpeed = 0; blocker.Damage = 0;
            Select(s);
            Move(s, q[0], q[1]);
            yield return WaitFor(() => { Cam(ship.transform.position, 70f); return s.ShuttleState == ZShuttleState.Hover; }, 40f); yield return Wait(1.0f);
            string cur = "?"; yield return CursorOn(s, c => cur = c);
            Shot("k256f_p6_hover_over_a_robot_no_exit_h70");
            Log($"P6 over the robot: why it cannot land '{s.ShuttleNoLand}', the EXIT button {ButtonShown}, the cursor '{cur}'");   // run 27D
            Check("P6 hovering over a robot: it cannot land, no EXIT button, no EXIT cursor", !string.IsNullOrEmpty(s.ShuttleNoLand) && !ButtonShown && cur != "exit");
            blocker.KillAt = W.Time; yield return Wait(0.8f);
            string cur2 = "?"; yield return CursorOn(s, c => cur2 = c);
            Shot("k256f_p6_robot_gone_no_exit_in_the_air_k430b_h70");
            Check("P6 the robot gone: the rules would take EXIT again, but no EXIT button and no EXIT cursor up in the air (K430b)", s.CanEjectCrew() && !ButtonShown && cur2 != "exit");
            int pax = s.Passengers.Count; var (hx, hy) = s.ShuttleExitSpot();
            hud.ExitSelected();                                                                       // the EXIT button's own call: refused up in the air (K430b)
            Check("P6 the EXIT button's call up in the air is refused (K430b): nothing given", !s.ShuttleExitOnLanding && !s.ShuttleExitPending && s.Passengers.Count == pax);
            W.Eject(me, s);                                                                           // K430b: the rules' own EXIT (K256f's path, kept; no player control gives it up in the air)
            Check("P6 EXIT in the air is taken by the rules: it comes down there", s.ShuttleExitOnLanding);
            yield return WaitFor(() => s.ShuttleState == ZShuttleState.Landed, 20f); yield return Wait(0.3f);
            Shot("k256f_p6_down_ramp_opening_h70");
            yield return WaitFor(() => s.Passengers.Count == 0, 12f); yield return Wait(1.6f);
            Shot("k256f_p6_passengers_out_pilot_stays_h70");
            Select(s); yield return Wait(0.3f);
            Log($"EXIT in the air: landed at {s.CenterX},{s.CenterY} (the spot {hx},{hy}), out {s.ShuttleFootIds.Count} of {pax}, pilot {s.Drivers.Count}, the ship {s.Owner}; EXIT button now {ButtonShown}");
            Check("P6 it landed at that spot, the passengers got out, the pilot stays (no EXIT left)", s.CenterX == hx && s.CenterY == hy && s.Passengers.Count == 0 && s.ShuttleFootIds.Count == pax && s.Drivers.Count == 1 && s.Owner == me && !ButtonShown);
        }

        // ------------------------------------------------------------------ P7: a lone robot: its pilot only
        IEnumerator P7()
        {
            var q = Spot(s != null ? s.CenterX : W.Map.WidthPix / 2, s != null ? s.CenterY : W.Map.HeightPix / 2, 320, 800, null);
            if (q == null) { Check("P7 a free place", false); yield break; }
            var e2 = OzCheatShuttle.Spawn(g, hud, Team.Neutral, new Vector2Int(q[0], q[1])); var v2 = e2 != null ? OzPlayableShuttle.Of(e2) : null;
            if (e2 == null || !v2) { Check("P7 a second empty drop", false); yield break; }
            double h = e2.ShuttleHeadingDeg * System.Math.PI / 180.0;
            var lone = Squad("grunt", (int)(e2.CenterX - System.Math.Cos(h) * 96) >> 4, (int)(e2.CenterY - System.Math.Sin(h) * 96) >> 4, 1); yield return Wait(0.5f);
            Cam(v2.transform.position, 70f);
            Check("P7 the lone robot's ENTER order is taken", Enter(lone, e2));
            yield return WaitFor(() => e2.Drivers.Count > 0, 25f);
            yield return WaitFor(() => e2.ShuttleRampPhase == ZShuttleRamp.Shut && W.Time >= e2.ShuttleRampHoldUntil, 20f); yield return Wait(0.8f);
            Select(e2); yield return Wait(0.3f);
            string cur = "?"; yield return CursorOn(e2, c => cur = c);
            Shot("k256f_p7_lone_pilot_no_exit_h70");
            Log($"a lone robot into #{e2.Id}: pilot {e2.Drivers.Count}, passengers {e2.Passengers.Count}, the ship {e2.Owner}; EXIT button {ButtonShown}, cursor '{cur}'; line '{OzShuttleHud.Line(e2)}'");
            Check("P7 a lone robot becomes the pilot only: no passengers, no EXIT button, no EXIT cursor", e2.Drivers.Count == 1 && e2.Passengers.Count == 0 && e2.Owner == me && !ButtonShown && cur != "exit");
        }

        void Finish()
        {
            watch = false;
            Report = $"{(fails == 0 ? "PASS" : "FAIL")} {passes} passed, {fails} failed, {frames} frames";
            sb.Insert(0, Report + "\n");
            try { System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "empty_proof_log.txt"), sb.ToString()); }
            catch (System.Exception ex) { Debug.LogWarning("[OzShuttleEmptyProof] " + ex.Message); }
            Debug.Log("[OzShuttleEmptyProof] " + Report + " -> " + dir);
            Destroy(gameObject);
        }
    }
}
