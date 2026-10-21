using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace OpenZ
{
    /// <summary>
    /// K256f2 Play proof: heroes never stay stuck in a shuttle, and no vehicle boards one (rule DV118, Sim/ZWorld.ShuttleHeroes.cs), on level
    /// 16 in Play (menu OpenZ/V1.1/Shuttle: K256f2 Heroes Exit Proof, Editor/OzShuttleHeroesMenu.cs). Counted PASS / FAIL lines and
    /// full-screen pictures from the normal RTS camera at 70 m (uncropped). Set-up: the CPU teams removed, morning (day clock 0.10, held).
    /// The game's pointer is moved by queued input events inside the game only (as the testing panel's own self-test does): no desktop input.
    ///  Q1 the testing panel's drop of an empty ship beside ALAN &amp; BRAD; the hero squad ordered in: up the ramp, Brad the pilot, Alan the
    ///     passenger, the ship red and lit; the unit panel's line names the pilot ("PILOT BRAD");
    ///  Q2 flown and landed: the EXIT button and the EXIT cursor; EXIT (the button's own call): both walk down the ramp as the hero squad,
    ///     the ship EMPTY again: dark (by day and at night), grey, the line "EMPTY ...", no EXIT button; its engine dies (the sound log);
    ///  Q3 every vehicle and gun of the testing panel and another shuttle, each selected on its own with the pointer on the empty ship and on
    ///     a crewed one: the cursor never ENTER (and the right-click's order never ENTER); a grunt squad selected over it: ENTER (control);
    ///  Q4 a grunt squad ordered into the emptied ship takes it (its leader the pilot), the ship lit again.
    /// Output: <see cref="Folder"/> (the work tree's records, milestones/shuttle_heroes_proof, found upward at run time). Test only.
    /// </summary>
    public sealed class OzShuttleHeroesProof : MonoBehaviour
    {
        public static string Report = "idle";
        public static string Folder()
        {
            var d = new System.IO.DirectoryInfo(Application.dataPath);
            for (int i = 0; i < 8 && d != null; i++, d = d.Parent)
                if (System.IO.Directory.Exists(System.IO.Path.Combine(d.FullName, "work", "records")))
                    return System.IO.Path.Combine(d.FullName, "work", "records", "milestones", "shuttle_heroes_proof");
            return System.IO.Path.Combine(Application.persistentDataPath, "shuttle_heroes_proof");
        }
        public static void Run()
        {
            Report = "running";
#if UNITY_EDITOR
            var gv = System.Type.GetType("UnityEditor.GameView,UnityEditor"); if (gv != null) UnityEditor.EditorWindow.FocusWindowIfItsOpen(gv);
#endif
            new GameObject("OzShuttleHeroesProof").AddComponent<OzShuttleHeroesProof>();
        }

        static readonly string[] VehicleTypes = { "jeep", "light", "medium", "heavy", "apc", "missile_launcher", "crane", "gatling", "gun", "howitzer", "missile_cannon" };

        ZGame g; ZWorld W; PlayerInput input; RtsCamera rts; DayNight dn; OpenZHud hud; OpenZCursors cursors; Team me = Team.Red;
        ZObject s; OzPlayableShuttle ship;
        readonly StringBuilder sb = new StringBuilder(); int fails, passes, frames; string dir;

        void Start()
        {
            g = ZGame.I; W = g ? g.World : null; input = FindFirstObjectByType<PlayerInput>(); rts = FindFirstObjectByType<RtsCamera>();
            hud = OpenZHud.I; cursors = FindFirstObjectByType<OpenZCursors>();
            dir = Folder(); System.IO.Directory.CreateDirectory(dir);
            StartCoroutine(All());
        }

        void Log(string t) { sb.AppendLine(t); Debug.Log("[OzShuttleHeroesProof] " + t); }
        void Check(string what, bool ok) { if (ok) passes++; else fails++; Log((ok ? "PASS " : "FAIL ") + what); }
        void Shot(string name) { ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, name + ".png")); frames++; Log($"frame {name}.png"); }
        IEnumerator WaitFor(System.Func<bool> c, float max) { float t = 0f; while (t < max && !c()) { t += Time.deltaTime; yield return null; } }
        IEnumerator Wait(float s0) { float t = 0f; while (t < s0) { t += Time.deltaTime; yield return null; } }
        void Cam(Vector3 focus, float h) { if (!rts) return; rts.FreeLookOff(); rts.focus = new Vector3(focus.x, 0f, focus.z); rts.SetHeightImmediate(h); }
        ZObject Squad(string type, int tx, int ty, int amount = -1)
        {
            OzCheatSpawner.FreeTile(W, tx, ty, 1, true, out tx, out ty);
            var o = W.CreateRobotGroup(type, tx * 16, ty * 16, me, amount); if (o == null) return null;
            g.SpawnUnitView(o); foreach (var m in o.Minions) g.SpawnUnitView(m);
            return o;
        }
        ZObject Unit(string type, int tx, int ty)
        {
            OzCheatSpawner.FreeTile(W, tx, ty, 2, false, out tx, out ty);
            var o = W.CreateObject(ZBuildLists.IsCannon(type) ? "cannon" : "vehicle", type, tx * 16, ty * 16, me);
            if (o != null) g.SpawnUnitView(o);
            return o;
        }
        bool Move(ZObject o, int x, int y) => W.SendWaypoints(me, o, new[] { new ZWaypoint { mode = WpMode.Move, x = x, y = y, playerGiven = true } });
        bool Enter(ZObject sq, ZObject ship0) => sq != null && W.SendWaypoints(me, sq, new[] { new ZWaypoint { mode = WpMode.Enter, refId = ship0.Id, x = ship0.CenterX, y = ship0.CenterY, playerGiven = true } });
        void Select(params ZObject[] os) { input.Selection.Clear(); foreach (var o in os) if (o != null) input.Selection.Add(o); }
        bool ButtonShown => hud && hud.ExitButtonRect && hud.ExitButtonRect.gameObject.activeSelf;

        /// <summary>The game's own pointer onto a unit (a queued input event inside the game: the panel self-test's way).</summary>
        void PointAt(ZObject o)
        {
            if (Mouse.current == null || input == null || o == null) return;
            var r = input.ScreenRect(o);
            InputSystem.QueueStateEvent(Mouse.current, new MouseState { position = r.center });
        }
        /// <summary>The cursor the game shows with its pointer on the unit (two frames after the pointer moved there).</summary>
        IEnumerator CursorOn(ZObject o, System.Action<string> got) { PointAt(o); yield return null; yield return null; PointAt(o); yield return null; got(cursors ? cursors.Current : "?"); }

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

        static int Played(float t0, string key) { int n = 0; foreach (var e in OzSoundLog.Since(t0)) if (e.key == key && e.outcome != null && e.outcome.StartsWith("PLAY")) n++; return n; }

        IEnumerator All()
        {
            Application.targetFrameRate = 60;
            if (W == null || hud == null || input == null) { Log("no game (level 16 in Play)"); fails++; Finish(); yield break; }
            if (!ZWorld.ShuttlePlayableDeviation) { Log("the shuttle rule is off ('Zod exact')"); fails++; Finish(); yield break; }
            me = g.player;
            Log($"K256f2 heroes exit proof, {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}, {Screen.width}x{Screen.height}, world time {W.Time:F1} s");
            int bots = W.Bots.Count; W.Bots.Clear();
            dn = FindFirstObjectByType<DayNight>(); if (dn) { dn.timeOfDay = 0.10f; dn.Apply(0.10f); dn.running = false; }
            Log($"set-up: CPU removed {bots}, day 0.10 held");
            yield return Wait(0.5f);
            yield return Q1(); yield return Q2(); yield return Q3(); yield return Q4();
            Finish();
        }

        // ------------------------------------------------------------------ Q1: the heroes into an empty ship
        IEnumerator Q1()
        {
            var brad = W.Objects.FirstOrDefault(o => o.IsHero && o.HeroMemberName == "brad" && !o.Destroyed && o.KillAt < 0 && o.Owner == me && !o.IsMinion);
            if (brad == null) { Check("Q1 level 16's ALAN & BRAD (the hero squads on)", false); yield break; }
            var q = Spot(brad.CenterX, brad.CenterY, 240, 700, null);
            if (q == null) { Check("Q1 a free place for the drop", false); yield break; }
            s = OzCheatShuttle.Spawn(g, hud, Team.Neutral, new Vector2Int(q[0], q[1]));
            ship = s != null ? OzPlayableShuttle.Of(s) : null;
            if (s == null || !ship) { Check("Q1 the panel drops an empty shuttle with its view", false); yield break; }
            Cam(Vector3.Lerp(ship.transform.position, ZGame.PxToWorld(brad.CenterX, brad.CenterY), 0.4f), 70f); yield return Wait(0.8f);
            Check("Q1 the ENTER order of ALAN & BRAD on the empty ship is taken", Enter(brad, s));
            yield return WaitFor(() => s.ShuttleRampPhase != ZShuttleRamp.Shut, 40f); yield return Wait(0.6f);
            Cam(ship.transform.position, 70f);
            Shot("k256f2_q1_heroes_walk_to_ramp_h70");
            yield return WaitFor(() => s.Drivers.Count > 0, 30f); yield return Wait(0.6f);
            Shot("k256f2_q1_heroes_up_the_ramp_h70");
            yield return WaitFor(() => s.ShuttleRampPhase == ZShuttleRamp.Shut && W.Time >= s.ShuttleRampHoldUntil, 20f); yield return Wait(0.8f);
            var look = ship.GetComponent<OzShuttleEmptyLook>();
            Select(s); yield return Wait(0.3f);
            string line = OzShuttleHud.Line(s);
            Shot("k256f2_q1_taken_lit_red_h70");
            Log($"after the boarding: pilot {s.Drivers.Count} (hero {s.ShuttleHeroPilot}), passengers {s.Passengers.Count}, the ship {s.Owner}, lights mode {(look && look.Lights ? look.Lights.Mode : -1)}; line '{line}'");
            Check("Q1 Brad the pilot (a hero), Alan the passenger, the ship red", s.ShuttleHeroPilot && s.Drivers.Count == 1 && s.Passengers.Count == 1 && s.Owner == me);
            Check("Q1 lit once they sit; the unit panel's line names the pilot", look && !look.IsDark && line.StartsWith("PILOT BRAD"));
        }

        // ------------------------------------------------------------------ Q2: flown, landed, EXIT: both out, the ship empty again
        IEnumerator Q2()
        {
            if (s == null || !s.ShuttleHeroPilot) yield break;
            var q = Spot(s.CenterX, s.CenterY, 320, 700, s);
            if (q == null) { Check("Q2 a free place to fly to", false); yield break; }
            Select(s); Move(s, q[0], q[1]);
            yield return WaitFor(() => { Cam(ship.transform.position, 70f); return s.ShuttleState != ZShuttleState.Landed; }, 15f);
            yield return WaitFor(() => { Cam(ship.transform.position, 70f); return s.ShuttleState == ZShuttleState.Landed && s.ShuttleCrewSettled; }, 60f); yield return Wait(0.5f);
            string cur = "?"; yield return CursorOn(s, c => cur = c);
            Shot("k256f2_q2_landed_exit_ready_h70");
            Log($"landed at {s.CenterX},{s.CenterY}: EXIT {s.CanEjectCrew()}, button {ButtonShown}, cursor '{cur}', line '{OzShuttleHud.Line(s)}'");
            Check("Q2 landed with the heroes: the EXIT button and the EXIT cursor", s.CanEjectCrew() && ButtonShown && cur == "exit");
            float t0 = Time.unscaledTime; int pd0 = OzShuttleHeroesView.PowerDowns;
            var look = ship.GetComponent<OzShuttleEmptyLook>();
            hud.ExitSelected();                                                                       // the EXIT button's own call
            yield return WaitFor(() => s.ShuttleRampPhase == ZShuttleRamp.Open || s.Drivers.Count == 0, 10f); yield return Wait(0.9f);
            Shot("k256f2_q2_heroes_walk_down_ramp_h70");
            yield return WaitFor(() => s.Drivers.Count == 0 && s.Passengers.Count == 0, 12f); yield return Wait(1.8f);
            var outs = s.ShuttleFootIds.Select(id => W.Get(id)).Where(o => o != null && !o.Destroyed).ToList();
            var b = outs.FirstOrDefault(o => !o.IsMinion); var a = outs.FirstOrDefault(o => o.IsMinion);
            Select(s); yield return Wait(0.3f);
            string line = OzShuttleHud.Line(s);
            Shot("k256f2_q2_both_out_ship_dark_h70"); yield return null; yield return null;   // run 27D: the capture is written at the frame's end: the clock below must not land in it
            Log($"after EXIT: out {outs.Count} ({string.Join(", ", outs.Select(o => (o.HeroMemberName ?? o.Type) + (o.IsMinion ? "" : " leads")))}), the ship {s.Owner}, pilots {s.Drivers.Count}, aboard {s.Passengers.Count}, dark {(look && look.IsDark)}, lights mode {(look && look.Lights ? look.Lights.Mode : -1)}; " +
                $"line '{line}'; EXIT button {ButtonShown}; power-downs {OzShuttleHeroesView.PowerDowns - pd0}, in the sound log {Played(t0, OzShuttleHeroesView.PowerDownEvent)}");
            Check("Q2 EXIT lets BOTH heroes out as the hero squad (Brad leads, Alan with him)", outs.Count == 2 && b != null && b.HeroMemberName == "brad" && a != null && a.HeroMemberName == "alan" && a.Leader == b);
            Check("Q2 the ship is EMPTY again: the null team, no pilot, nobody aboard, no EXIT", s.ShuttleIsEmpty && s.Owner == Team.Neutral && !s.CanEjectCrew() && !ButtonShown);
            Check("Q2 it is dark again (lamps, neon, exhaust off) and the unit panel says EMPTY", look && look.IsDark && look.Lights && look.Lights.Mode == 2 && line.StartsWith("EMPTY"));
            Check("Q2 its engine dies as it goes dark (the power-down in the sound log)", OzShuttleHeroesView.PowerDowns > pd0 && Played(t0, OzShuttleHeroesView.PowerDownEvent) > 0);
            if (dn) { dn.timeOfDay = 0.62f; dn.Apply(0.62f); }
            yield return Wait(1.5f); Shot("k256f2_q2_empty_again_night_h70"); yield return null; yield return null;   // run 27D: the same
            Check("Q2 at night it stays dark (no pilot to light it)", look && look.IsDark && OzShuttleEmptyLook.Dark(s));
            if (dn) { dn.timeOfDay = 0.10f; dn.Apply(0.10f); }
            yield return Wait(0.8f);
            int fl = s.ShuttleFlights; yield return Wait(3f);
            Check("Q2 it stays put (no flight, the ramp shut again)", s.ShuttleFlights == fl && s.ShuttleState == ZShuttleState.Landed && s.ShuttleRampPhase == ZShuttleRamp.Shut);
        }

        // ------------------------------------------------------------------ Q3: vehicles over a shuttle: never the ENTER cursor
        IEnumerator Q3()
        {
            if (s == null) yield break;
            var crewedQ = Spot(s.CenterX, s.CenterY, 360, 800, null);
            var crewed = crewedQ != null ? OzCheatShuttle.Spawn(g, hud, me, new Vector2Int(crewedQ[0], crewedQ[1])) : null;
            var units = new List<ZObject>();
            double h = s.ShuttleHeadingDeg * System.Math.PI / 180.0;
            int k = 0;
            foreach (var t in VehicleTypes)
            {
                double a = h + System.Math.PI * 0.5 + (k % 2 == 0 ? 0 : System.Math.PI) ; int r = 110 + (k / 2) * 34; k++;   // beside the ship, off its back axis
                var u = Unit(t, (int)(s.CenterX + System.Math.Cos(a) * r) >> 4, (int)(s.CenterY + System.Math.Sin(a) * r) >> 4);
                if (u != null) units.Add(u);
            }
            if (crewed != null) units.Add(crewed);                                                    // another shuttle, selected over the empty one
            yield return Wait(0.8f);
            Cam(ship.transform.position, 70f); yield return Wait(0.4f);
            var kinds = new Dictionary<string, int>(); int enterCur = 0, enterOrder = 0, tested = 0; string worst = "";
            foreach (var target in new[] { s, crewed })
            {
                if (target == null) continue;
                var tv = OzPlayableShuttle.Of(target); if (tv) Cam(tv.transform.position, 70f); yield return Wait(0.3f);
                foreach (var u in units)
                {
                    if (u == target) continue;
                    Select(u); yield return null;
                    string cur = "?"; yield return CursorOn(target, c => cur = c);
                    var wp = input.OrderFor(u, target, target.CenterX, target.CenterY);
                    kinds.TryGetValue(cur, out int n); kinds[cur] = n + 1; tested++;
                    if (cur == "enter") { enterCur++; worst += $" {u.Type}->{(target == s ? "empty" : "crewed")}"; }
                    if (wp.mode == WpMode.Enter) enterOrder++;
                    if (target == s && (u.Type == "heavy" || u.Type == "missile_launcher")) Shot($"k256f2_q3_{u.Type}_over_empty_ship_cursor_h70");
                    if (target == crewed && u.Type == "jeep") Shot("k256f2_q3_jeep_over_crewed_ship_cursor_h70");
                }
            }
            Log($"vehicles, guns and a shuttle selected one by one over the empty and the crewed ship: {tested} cursors ({string.Join(", ", kinds.Select(kv => kv.Key + " " + kv.Value))}), ENTER {enterCur}{worst}; right-click orders ENTER {enterOrder}");
            Check("Q3 with a vehicle, a gun or a shuttle selected, the cursor over a shuttle is never ENTER (and the right-click's order never ENTER)", tested >= 2 * VehicleTypes.Length && enterCur == 0 && enterOrder == 0);
            // the control: a grunt squad selected over the empty ship shows ENTER
            var gs = Squad("grunt", (int)(s.CenterX - System.Math.Cos(h) * 112) >> 4, (int)(s.CenterY - System.Math.Sin(h) * 112) >> 4); yield return Wait(0.5f);
            Select(gs); string c2 = "?"; yield return CursorOn(s, c => c2 = c);
            Shot("k256f2_q3_grunts_over_empty_ship_enter_h70");
            Check("Q3 the control: a grunt squad selected over the empty ship shows ENTER", c2 == "enter");
            foreach (var u in units) if (u != crewed && u != null && !u.Destroyed) u.KillAt = W.Time;  // out of the ship's way for Q4
            grunts = gs;
            yield return Wait(0.5f);
        }
        ZObject grunts;

        // ------------------------------------------------------------------ Q4: a grunt squad takes the emptied ship
        IEnumerator Q4()
        {
            if (s == null || grunts == null) yield break;
            Check("Q4 the grunt squad's ENTER order on the emptied ship is taken", Enter(grunts, s));
            yield return WaitFor(() => s.Drivers.Count > 0, 30f);
            yield return WaitFor(() => s.ShuttleRampPhase == ZShuttleRamp.Shut && W.Time >= s.ShuttleRampHoldUntil, 20f); yield return Wait(0.8f);
            var look = ship.GetComponent<OzShuttleEmptyLook>();
            Select(s); yield return Wait(0.3f);
            Shot("k256f2_q4_grunts_took_it_lit_h70");
            Log($"the grunts into the emptied ship: pilot {s.Drivers.Count} ({s.DriverType}, hero {s.ShuttleHeroPilot}), passengers {s.Passengers.Count}, the ship {s.Owner}, dark {(look && look.IsDark)}; line '{OzShuttleHud.Line(s)}'");
            Check("Q4 a grunt squad takes the emptied ship: its leader the pilot, the rest its passengers, lit again", s.Drivers.Count == 1 && !s.ShuttleHeroPilot && s.Passengers.Count >= 1 && s.Owner == me && look && !look.IsDark);
        }

        void Finish()
        {
            Report = $"{(fails == 0 ? "PASS" : "FAIL")} {passes} passed, {fails} failed, {frames} frames";
            sb.Insert(0, Report + "\n");
            try { System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "heroes_proof_log.txt"), sb.ToString()); }
            catch (System.Exception ex) { Debug.LogWarning("[OzShuttleHeroesProof] " + ex.Message); }
            Debug.Log("[OzShuttleHeroesProof] " + Report + " -> " + dir);
            Destroy(gameObject);
        }
    }
}
