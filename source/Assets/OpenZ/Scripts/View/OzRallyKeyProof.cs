#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using OpenZ.Sim;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace OpenZ
{
    /// <summary>
    /// K327 Play checks (editor only, never in a build). Menus under OpenZ/V1.1/Controls (Editor/OzRallyKeyMenu.cs).
    ///  - Proof (a running match on Level 16 or 17, the levels with a repair station): the conflict matrix of item 328 with the real
    ///    game, through the real input path (a keyboard and a mouse of the check, their events queued like a device's): the build
    ///    menu's SET RALLY by mouse click and by the key give the same state and the same sounds; the rally point set with a real
    ///    click; a damaged vehicle selected while the menu is open gets no repair order; the key again cancels the armed tool; with
    ///    no menu the key sends the damaged vehicle (and the shuttle, on Level 16) to repair and cycles robot squads as before; Esc /
    ///    a left press / the factory list key in the same frame: one action only; held 40 frames: one action; the pause menu: nothing;
    ///    the gamepad's Y fallback and the touch REPAIR key: unchanged. It stages two damaged jeeps by the fort and gives the player
    ///    the repair station's (and, if needed, a factory's) zone, as the testing overlay would. One line at the end:
    ///    "[K327 R key] PLAY PROOF ...: N checks, M FAIL", the report and pictures in Folder.
    ///  - Layout: the controls tables on screen now (or every one: the main menu's CONTROLS in the menu scene; the pause menu's
    ///    CONTROLS and the F1 list in a match, at UI 100 % and 160 %): "[K327 controls] ... overlaps 0, overflow 0, cut 0, shrunk 0".
    /// </summary>
    public class OzRallyKeyProof : MonoBehaviour
    {
        public const string Tag = "[K327 R key] ", LayoutTag = "[K327 controls] ";
        public static string Report = "not run", LayoutReport = "not run";
        /// <summary></summary>
        public static string Folder = "";
        public const string FullRow = "Repair damaged vehicles · build menu open: set rally", CompactRow = "Repair vehicles · build menu: set rally";

        static string Dir()
        {
            string d = string.IsNullOrEmpty(Folder) ? Path.GetFullPath(Path.Combine(Application.dataPath, "../../../../work/records/milestones/k327_r_key_set_rally")) : Folder;
            Directory.CreateDirectory(d); return d;
        }

        public static void RunProof() { Report = "running"; Focus(); new GameObject("OzRallyKeyProof").AddComponent<OzRallyKeyProof>().StartCoroutine("Proof"); }
        public static void RunLayoutAll() { LayoutReport = "running"; Focus(); new GameObject("OzRallyKeyLayout").AddComponent<OzRallyKeyProof>().StartCoroutine("LayoutAll"); }
        static void Focus() { var gv = System.Type.GetType("UnityEditor.GameView,UnityEditor"); if (gv != null) UnityEditor.EditorWindow.FocusWindowIfItsOpen(gv); }

        // ================================================================== the proof
        ZGame g; ZWorld W; OpenZHud hud; PlayerInput input; OpenZProductionWindow pw; OpenZRallyPoints rally; RtsCamera rts;
        ZObject fort, factory, station, A, B, shuttle;
        Keyboard proofKb, realKb; Mouse proofMouse, realMouse;
        readonly StringBuilder sb = new StringBuilder(); int checks, fails;

        void Log(string s) { sb.AppendLine(s); Debug.Log(Tag + s); }
        void Check(string what, bool ok) { checks++; if (!ok) fails++; Log((ok ? "PASS " : "FAIL ") + what); }
        void Shot(string name) { ScreenCapture.CaptureScreenshot(Path.Combine(Dir(), name + ".png")); Log("picture " + name + ".png"); }
        static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

        void DevicesOn()
        {
            realKb = Keyboard.current; proofKb = InputSystem.AddDevice<Keyboard>("OzK327Keyboard"); proofKb.MakeCurrent();
            realMouse = Mouse.current; proofMouse = InputSystem.AddDevice<Mouse>("OzK327Mouse");
            if (realMouse != null && realMouse != proofMouse) InputSystem.DisableDevice(realMouse);
            proofMouse.MakeCurrent();
            MouseAt(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f), false);
        }

        void DevicesOff()
        {
            if (proofKb != null && proofKb.added) InputSystem.RemoveDevice(proofKb);
            if (realKb != null) realKb.MakeCurrent();
            if (proofMouse != null && proofMouse.added) InputSystem.RemoveDevice(proofMouse);
            if (realMouse != null) { InputSystem.EnableDevice(realMouse); realMouse.MakeCurrent(); }
            proofKb = null; realKb = null; proofMouse = null; realMouse = null;
        }

        void OnDestroy() { DevicesOff(); }

        Vector2 mousePos;
        void MouseAt(Vector2 p, bool left) { var st = new MouseState { position = p }; if (left) st = st.WithButton(MouseButton.Left); InputSystem.QueueStateEvent(proofMouse, st); mousePos = p; }
        void KeysDown(params Key[] keys) => InputSystem.QueueStateEvent(proofKb, new KeyboardState(keys));
        void KeysUp() => InputSystem.QueueStateEvent(proofKb, new KeyboardState());
        /// <summary>One press of these keys in one frame (the press reaches the scripts in the frame after the event is queued).</summary>
        IEnumerator Press(params Key[] keys) { KeysUp(); yield return null; KeysDown(keys); yield return Frames(2); KeysUp(); yield return Frames(3); }
        static Key RKey => OzBindings.KeyOf(OzAction.RepairVehicles);

        bool OnRepairTrip(ZObject v) => v != null && ((v.Waypoints.Count > 0 && v.Waypoints[0].mode == WpMode.UnitRepair) || (v.IsShuttle && v.ShuttleRepairTarget >= 0));
        void Select(params ZObject[] us) { input.Selection.Clear(); foreach (var u in us) if (u != null) input.Selection.Add(u); }
        string Sel() => input.Selection.Count == 0 ? "nothing" : string.Join(", ", input.Selection.Select(o => o.Type + "#" + o.Id));
        IEnumerator Calm()
        {   // no menu, no tool, no queued keys: the state before every case
            KeysUp(); MouseAt(mousePos, false);
            if (rally.Armed) rally.Disarm();
            if (pw.IsOpen) pw.Close();
            yield return Frames(4);
        }
        IEnumerator OpenMenu(ZObject b) { pw.Open(b); yield return Frames(3); }     // LateUpdate has seen it open

        struct Snap { public bool armed, menu; public int armedFor, rallies, cancels, swallowed, todays, clicks; public string sounds; }
        Snap Take(float t0)
        {
            var s = new Snap
            {
                armed = rally.Armed, menu = pw.IsOpen, armedFor = rally.Armed && rally.Building != null ? rally.Building.Id : -1,
                rallies = OzRallyKeyCore.SetRallies, cancels = OzRallyKeyCore.Cancels, swallowed = OzRallyKeyCore.Swallowed, todays = OzRallyKeyCore.Todays,
                clicks = OpenZProductionWindow.RallyKeyClicks,
            };
            var keys = OzSoundLog.Since(t0).Where(e => e.outcome != null && e.outcome.StartsWith("PLAY") && e.key != null && e.key.StartsWith("ui_")).Select(e => e.key).OrderBy(k => k);
            s.sounds = string.Join(" ", keys);
            return s;
        }

        ZObject Spawn(string type, int tx, int ty, int pct)
        {
            OzCheatSpawner.FreeTile(W, tx, ty, 2, false, out tx, out ty);
            var o = W.CreateObject("vehicle", type, tx * 16, ty * 16, g.player);
            if (o == null) { Log($"spawn {type} failed"); return null; }
            g.SpawnUnitView(o); o.SetHealth(o.MaxHealth * pct / 100);
            return o;
        }

        void Own(ZObject b)
        {
            if (b == null || b.Owner == g.player) return;
            var f = W.Flags.FirstOrDefault(x => x.ZoneIndex == b.ZoneIndex);
            if (f != null) W.AwardZone(f, g.player);
        }

        /// <summary>A screen point on open ground: not over the HUD or the build menu, not on a building or a unit of the player.</summary>
        bool FreePoint(Vector3 around, out Vector2 p)
        {
            var cam = rts ? rts.GetComponent<Camera>() : Camera.main;
            float[] rings = { 14f, 20f, 26f };
            foreach (float r in rings)
                for (int i = 0; i < 16; i++)
                {
                    float a = i * Mathf.PI / 8f;
                    var w = around + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                    var s = cam.WorldToScreenPoint(w);
                    if (s.z <= 0f) continue;
                    p = s;
                    if (p.x < 80f || p.y < 80f || p.x > Screen.width - 80f || p.y > Screen.height - 80f) continue;
                    if (hud.IsOverUi(p) || pw.ProducerUnder(p) != null) continue;
                    if (!g.GroundPoint(cam.ScreenPointToRay(p), out _)) continue;
                    bool onUnit = false;
                    foreach (var o in W.Objects) if (!o.Destroyed && (o.Kind == ZKind.Building || o.IsUnit) && input.ScreenRect(o).Contains(p)) { onUnit = true; break; }
                    if (!onUnit) return true;
                }
            p = default; return false;
        }

        IEnumerator Proof()
        {
            yield return new WaitForSecondsRealtime(1f);
            g = ZGame.I; W = g ? g.World : null; hud = OpenZHud.I;
            input = hud ? hud.input : null; pw = hud ? hud.Production : null; rally = hud ? hud.Rally : null; rts = hud ? hud.rts : null;
            if (W == null || !hud || !input || !pw || !rally) { Report = Tag + "PLAY PROOF: start a level first (no match, HUD, build menu or rally tool found)"; Debug.LogWarning(Report); Destroy(gameObject); yield break; }
            Log($"{g.level}, {Screen.width}x{Screen.height}, interface scale {OzText.UiScale * 100f:0} %, repair key {OzBindings.Cap(RKey)} (NEXT ROBOT {OzBindings.Cap(OzAction.NextRobot)}, FACTORY LIST {OzBindings.Cap(OzAction.FactoryList)}), conflicts: {OzBindings.ConflictText()}");
            fort = W.Objects.FirstOrDefault(o => o.IsProducer && o.IsFort && o.Owner == g.player && !o.Destroyed);
            factory = W.Objects.FirstOrDefault(o => o.IsProducer && !o.IsFort && o.Owner == g.player && !o.Destroyed);
            if (factory == null)
            {
                var far = W.Objects.Where(o => o.IsProducer && !o.IsFort && !o.Destroyed && o.ZoneIndex >= 0).OrderBy(o => fort == null ? 0 : (o.CenterX - fort.CenterX) * (double)(o.CenterX - fort.CenterX) + (o.CenterY - fort.CenterY) * (double)(o.CenterY - fort.CenterY)).FirstOrDefault();
                Own(far); if (far != null && far.Owner == g.player) { factory = far; Log($"the zone of {far.Type}#{far.Id} given to the player (a factory for the factory case)"); }
            }
            station = W.Objects.FirstOrDefault(o => o.Kind == ZKind.Building && o.Type == "repair" && !o.Destroyed && o.Owner == g.player)
                      ?? W.Objects.FirstOrDefault(o => o.Kind == ZKind.Building && o.Type == "repair" && !o.Destroyed);
            Own(station);
            shuttle = W.Objects.FirstOrDefault(o => o.IsShuttle && o.Owner == g.player && !o.Destroyed);
            Check($"an own fort ({(fort != null ? "#" + fort.Id : "none")}), an own factory ({(factory != null ? factory.Type + "#" + factory.Id : "none")}), an own repair station ({(station != null ? "#" + station.Id + " " + station.Owner : "none")})",
                  fort != null && station != null && station.CanRepairUnit(g.player));
            if (fort == null || station == null) { Finish(); yield break; }
            int fx = fort.CenterX >> 4, fy = fort.CenterY >> 4;
            A = Spawn("jeep", fx + 6, fy + 9, 40); B = Spawn("jeep", fx - 6, fy + 9, 50);
            Check($"two damaged jeeps by the fort (A #{(A != null ? A.Id : -1)} {(A != null ? A.Health + "/" + A.MaxHealth : "-")}, B #{(B != null ? B.Id : -1)} {(B != null ? B.Health + "/" + B.MaxHealth : "-")}); shuttle {(shuttle != null ? "#" + shuttle.Id : "none on this level (its cases are skipped: run on Level 16 for them)")}",
                  A != null && B != null && A.CanBeRepaired() && B.CanBeRepaired());
            if (A == null || B == null) { Finish(); yield break; }
            if (rts) { rts.FlyTo(g.CenterWorld(fort)); rts.ZoomTo(38f); }
            DevicesOn();
            yield return new WaitForSecondsRealtime(1.5f);
            OzRallyKeyCore.ResetCounts();

            // ---- 0: the key on the SET RALLY button (its tooltip's key cap, as PLACE shows its key)
            var bRally = typeof(OpenZProductionWindow).GetField("bRally", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(pw) as HudButton;
            yield return Calm(); Select(); yield return OpenMenu(fort);
            if (bRally != null && bRally.Rt)
            {
                var corners = new Vector3[4]; bRally.Rt.GetWorldCorners(corners);
                MouseAt((corners[0] + corners[2]) * 0.5f, false); yield return new WaitForSecondsRealtime(0.9f);
                Shot("k327_00_set_rally_tooltip_key");
                MouseAt(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f), false); yield return Frames(2);
            }
            Check($"0 the SET RALLY tooltip ends with the key's cap ({(bRally != null ? bRally.Tooltip : "no button")})", bRally != null && bRally.Tooltip != null && bRally.Tooltip.EndsWith("|" + OzBindings.Cap(RKey)));

            // ---- 1: SET RALLY by mouse click (the HUD's click: the button's delegate, then the click sound) and by the key: the same
            yield return Calm(); Select(); yield return OpenMenu(fort);
            float t0 = Time.unscaledTime;
            if (bRally != null && bRally.OnClick != null) { bRally.OnClick(new HudClick()); OpenZHud.UiClickSound(); }
            yield return Frames(4);
            var byClick = Take(t0);
            Log($"1 by click: tool {(byClick.armed ? "armed for #" + byClick.armedFor : "not armed")}, menu {(byClick.menu ? "open" : "closed")}, sounds [{byClick.sounds}]");
            yield return Calm(); Select(); yield return OpenMenu(fort);
            var k0 = Take(Time.unscaledTime); t0 = Time.unscaledTime;
            yield return Press(RKey);
            var byKey = Take(t0);
            Log($"1 by the key: tool {(byKey.armed ? "armed for #" + byKey.armedFor : "not armed")}, menu {(byKey.menu ? "open" : "closed")}, sounds [{byKey.sounds}], route {OzRallyKey.Last}");
            Check("1 a build menu open, nothing selected: the key arms the rally tool for this building and closes the menu (SET RALLY once, by the button's own click)",
                  byKey.armed && byKey.armedFor == fort.Id && !byKey.menu && byKey.rallies == k0.rallies + 1 && byKey.clicks == k0.clicks + 1 && byKey.todays == k0.todays);
            Check("1 the same state and the same sounds as a mouse click on SET RALLY", byClick.armed == byKey.armed && byClick.armedFor == byKey.armedFor && byClick.menu == byKey.menu && byClick.sounds == byKey.sounds && byKey.sounds.Length > 0);
            Shot("k327_01_key_armed_rally_tool");
            // the rally point itself, with a real left click on the map (the armed tool takes it)
            bool set = false;
            if (fort.RallyPoints.Count > 0) { W.SetRallyPoints(g.player, fort, new ZWaypoint[0]); Log($"the fort's old rally points removed for the check"); }
            if (FreePoint(g.CenterWorld(fort), out var rp))
            {
                MouseAt(rp, false); yield return Frames(3); MouseAt(rp, true); yield return Frames(3); MouseAt(rp, false); yield return Frames(6);
                set = fort.RallyPoints.Count > 0 && !rally.Armed;
            }
            Check($"1 the rally point set with a click on the map after the key ({fort.RallyPoints.Count} point(s), tool {(rally.Armed ? "still armed" : "done")})", set);
            if (set) Shot("k327_02_rally_point_set");
            // a factory's menu too
            if (factory != null)
            {
                yield return Calm(); yield return OpenMenu(factory); var f0 = Take(Time.unscaledTime);
                yield return Press(RKey);
                Check($"1 the same for a factory's menu ({factory.Type}#{factory.Id})", rally.Armed && rally.Building == factory && !pw.IsOpen && OzRallyKeyCore.SetRallies == f0.rallies + 1);
            }

            // ---- 2 + 5: a damaged vehicle selected while the menu is open; the key again cancels
            yield return Calm(); Select(B); yield return OpenMenu(fort);
            Check("2 a build menu open with a damaged jeep selected (the menu keeps the selection)", pw.IsOpen && input.Selection.Contains(B));
            var s2 = Take(Time.unscaledTime);
            yield return Press(RKey);
            Check($"2 the open menu owns the key: SET RALLY, no repair order (B on a repair trip: {OnRepairTrip(B)}), the selection kept ({Sel()})",
                  rally.Armed && !pw.IsOpen && !OnRepairTrip(B) && input.Selection.Count == 1 && input.Selection[0] == B && OzRallyKeyCore.SetRallies == s2.rallies + 1 && OzRallyKeyCore.Todays == s2.todays);
            t0 = Time.unscaledTime; var s5 = Take(t0);
            yield return Press(RKey);
            var a5 = Take(t0);
            Check($"5 the tool armed, the key again: cancelled as with Esc (sounds [{a5.sounds}]), still no repair order, the selection kept",
                  !rally.Armed && !pw.IsOpen && !OnRepairTrip(B) && input.Selection.Contains(B) && a5.cancels == s5.cancels + 1);

            // ---- 6: one action in one frame
            yield return Calm(); Select(B); yield return OpenMenu(fort); var s6 = Take(Time.unscaledTime);
            yield return Press(Key.Escape, RKey);
            Check($"6a Esc and the key in one frame: the menu closed by Esc, no tool, no pause menu, no repair order, no robot squad cycled ({Sel()})",
                  !pw.IsOpen && !rally.Armed && !(hud.Pause && hud.Pause.IsOpen) && !OnRepairTrip(B) && input.Selection.Contains(B) && OzRallyKeyCore.Swallowed == s6.swallowed + 1 && OzRallyKeyCore.SetRallies == s6.rallies);
            if (hud.Pause && hud.Pause.IsOpen) { hud.Pause.Close(); yield return new WaitForSecondsRealtime(0.5f); }
            yield return Calm(); Select(B); yield return OpenMenu(fort); s6 = Take(Time.unscaledTime);
            if (FreePoint(g.CenterWorld(fort), out var lp))
            {
                MouseAt(lp, false); yield return Frames(3);
                MouseAt(lp, true); KeysDown(RKey); yield return Frames(3);
                MouseAt(lp, false); KeysUp(); yield return Frames(5);
                Check("6b a left click on the map and the key in one frame: the click closes the menu, no tool, no repair order",
                      !pw.IsOpen && !rally.Armed && !OnRepairTrip(B) && OzRallyKeyCore.Swallowed == s6.swallowed + 1 && OzRallyKeyCore.SetRallies == s6.rallies);
            }
            else Check("6b a free point on the map for the click (move the view and run again)", false);
            if (OzBindings.KeyOf(OzAction.FactoryList) != RKey)
            {
                yield return Calm(); Select(B); yield return OpenMenu(fort); s6 = Take(Time.unscaledTime);
                yield return Press(OzBindings.KeyOf(OzAction.FactoryList), RKey);
                Check("6c the factory list key and the key in one frame: the list key closes the menu first, the key adds nothing",
                      !pw.IsOpen && !rally.Armed && !OnRepairTrip(B) && OzRallyKeyCore.Swallowed == s6.swallowed + 1 && OzRallyKeyCore.SetRallies == s6.rallies);
                yield return Press(OzBindings.KeyOf(OzAction.FactoryList));                                        // the list closes again
            }

            // ---- 7: held down
            yield return Calm(); Select(B); yield return OpenMenu(fort); var s7 = Take(Time.unscaledTime);
            KeysUp(); yield return null; KeysDown(RKey); yield return Frames(40); KeysUp(); yield return Frames(4);
            Check($"7 the key held 40 frames with the menu open: one action (SET RALLY {OzRallyKeyCore.SetRallies - s7.rallies}, clicks {OpenZProductionWindow.RallyKeyClicks - s7.clicks}, cancels {OzRallyKeyCore.Cancels - s7.cancels}), the menu stays closed, no repair order",
                  OzRallyKeyCore.SetRallies == s7.rallies + 1 && OpenZProductionWindow.RallyKeyClicks == s7.clicks + 1 && OzRallyKeyCore.Cancels == s7.cancels && rally.Armed && !pw.IsOpen && !OnRepairTrip(B));

            // ---- 8: the pause menu
            yield return Calm(); Select(B);
            if (hud.Pause)
            {
                hud.Pause.Open(); yield return Frames(4); int last = OzRallyKey.LastFrame; var s8 = Take(Time.unscaledTime);
                yield return Press(RKey);
                Check("8 the pause menu open: the key does nothing (no repair order, no tool, no robot squad cycled)",
                      !OnRepairTrip(B) && !rally.Armed && input.Selection.Contains(B) && OzRallyKey.LastFrame == last && OzRallyKeyCore.Todays == s8.todays);
                hud.Pause.Close(); yield return new WaitForSecondsRealtime(0.6f);
            }

            // ---- 3 + 4: no menu: as before
            yield return Calm(); Select(A); var s3 = Take(Time.unscaledTime);
            yield return Press(RKey);
            Check($"3 no menu, a damaged jeep selected: the key sends it to the repair station as before (route {OzRallyKey.Last}, waypoint {(A.Waypoints.Count > 0 ? A.Waypoints[0].mode.ToString() : "none")})",
                  OnRepairTrip(A) && !rally.Armed && OzRallyKeyCore.Todays == s3.todays + 1);
            W.SendStop(g.player, A);
            var robot = W.OwnedSelectable(g.player).FirstOrDefault(o => o.Kind == ZKind.Robot);
            yield return Calm(); Select(); var s4 = Take(Time.unscaledTime);
            yield return Press(RKey);
            bool cycled = input.Selection.Count == 1 && input.Selection[0].Kind == ZKind.Robot;
            Check($"4 no menu, nothing repairable selected: {(OzBindings.KeyOf(OzAction.NextRobot) == RKey ? "the next robot squad as before" : "nothing new (NEXT ROBOT is on another key)")} ({Sel()})",
                  !rally.Armed && OzRallyKeyCore.Todays == s4.todays + 1 && (OzBindings.KeyOf(OzAction.NextRobot) != RKey || robot == null || cycled));

            // ---- 9: the gamepad's Y fallback (a key tapped by the touch / gamepad layer), the touch REPAIR key
            if (OzTouch.Running && OzBindings.KeyOf(OzAction.NextRobot) == RKey)
            {
                yield return Calm(); Select(); yield return OpenMenu(fort); var s9 = Take(Time.unscaledTime);
                OzTouch.TapKey(OzBindings.KeyOf(OzAction.NextRobot)); yield return Frames(5);
                Check($"9 the gamepad's Y fallback (it taps NEXT ROBOT) with the menu open: as before (route {OzRallyKey.Last}: {Sel()}), no tool",
                      !rally.Armed && OzRallyKeyCore.Todays == s9.todays + 1 && OzRallyKeyCore.SetRallies == s9.rallies && (robot == null || (input.Selection.Count == 1 && input.Selection[0].Kind == ZKind.Robot)));
            }
            else Log("INFO 9 the touch / gamepad layer is not running here or NEXT ROBOT has its own key: the Y fallback case is left out");
            var touchRepair = hud.TouchKey(4);
            if (touchRepair != null && touchRepair.OnClick != null)
            {
                yield return Calm(); Select(B); yield return OpenMenu(fort);
                touchRepair.OnClick(new HudClick()); yield return Frames(3);
                Check($"9b the touch REPAIR key with a menu open: the repair order as before (B on a repair trip: {OnRepairTrip(B)}), no tool", OnRepairTrip(B) && !rally.Armed);
                W.SendStop(g.player, B);
            }

            // ---- 3b / 2b: the shuttle (Level 16)
            if (shuttle != null)
            {
                shuttle.SetHealth(shuttle.MaxHealth * 60 / 100);
                yield return Calm(); Select(shuttle); yield return OpenMenu(fort);
                yield return Press(RKey);
                Check($"2b the damaged shuttle selected with the menu open: SET RALLY, no repair trip (target {shuttle.ShuttleRepairTarget})", rally.Armed && !OnRepairTrip(shuttle));
                yield return Calm(); Select(shuttle); var s3b = Take(Time.unscaledTime);
                yield return Press(RKey);
                Check($"3b the damaged shuttle, no menu: its repair-station trip as before (target {shuttle.ShuttleRepairTarget}, station #{station.Id})", OnRepairTrip(shuttle) && OzRallyKeyCore.Todays == s3b.todays + 1);
            }
            else Log("INFO 2b / 3b no shuttle of the player on this level: run the proof once on Level 16 for the shuttle's two cases");

            yield return Calm();
            Finish();
        }

        void Finish()
        {
            DevicesOff();
            string line = $"{Tag}PLAY PROOF {(g != null ? g.level : "-")} {Screen.width}x{Screen.height}: {checks} checks, {fails} FAIL; routes since the start: SET RALLY {OzRallyKeyCore.SetRallies}, cancelled {OzRallyKeyCore.Cancels}, nothing {OzRallyKeyCore.Swallowed}, as before {OzRallyKeyCore.Todays}";
            sb.AppendLine(line); Report = line;
            try { File.WriteAllText(Path.Combine(Dir(), $"k327_play_proof_{(g != null ? g.level : "none")}_{Screen.width}x{Screen.height}.txt"), sb.ToString()); } catch (IOException e) { Debug.LogWarning(Tag + "report not written: " + e.Message); }
            Debug.Log(line);
            Destroy(gameObject);
        }

        // ================================================================== the controls tables
        struct Box { public string name; public Rect r; }
        static Rect World(RectTransform rt) { var c = new Vector3[4]; rt.GetWorldCorners(c); return Rect.MinMaxRect(c[0].x, c[0].y, c[2].x, c[2].y); }
        static bool Ink(TMP_Text t, out Rect r)
        {
            r = default;
            if (!t || string.IsNullOrEmpty(t.text)) return false;
            t.ForceMeshUpdate();
            var b = t.textBounds; Vector3 a = t.transform.TransformPoint(b.min), z = t.transform.TransformPoint(b.max);
            r = Rect.MinMaxRect(Mathf.Min(a.x, z.x), Mathf.Min(a.y, z.y), Mathf.Max(a.x, z.x), Mathf.Max(a.y, z.y));
            return r.width > 0.01f && r.height > 0.01f;
        }
        static bool Meet(Rect a, Rect b, float s) => a.xMin < b.xMax - s && b.xMin < a.xMax - s && a.yMin < b.yMax - s && b.yMin < a.yMax - s;
        static bool Inside(Rect a, Rect b, float s) => a.xMin >= b.xMin - s && a.xMax <= b.xMax + s && a.yMin >= b.yMin - s && a.yMax <= b.yMax + s;

        /// <summary>Every controls table on screen now: its key caps, labels, separators, heads, rules and the RESET button, each against
        /// every other one (overlaps), each text inside its cap (overflow), inside the panel and the screen (cut), every row label at its
        /// full size (shrunk). The R row's new label must be there. Returns the violations (-1: no table on screen).</summary>
        public static int Layout(string screen, StringBuilder log)
        {
            Canvas.ForceUpdateCanvases();
            int tables = 0, rows = 0, rects = 0, overlaps = 0, overflow = 0, cut = 0, shrunk = 0; bool rRow = false; var seen = new HashSet<Transform>();
            var detail = new StringBuilder();
            foreach (var mark in FindObjectsByType<RectTransform>(FindObjectsSortMode.None))
            {
                bool full = mark.name == "FootRule", compact = mark.name == "Band0";
                if (!(full || compact) || !mark.gameObject.activeInHierarchy || !mark.parent || !seen.Add(mark.parent)) continue;
                tables++;
                var p = (RectTransform)mark.parent; var boxes = new List<Box>();
                Rect panel = World(p), scr = new Rect(0, 0, Screen.width, Screen.height);
                float want = full ? 14f : 11f;
                if (!Inside(panel, scr, 0.5f)) { cut++; detail.AppendLine($"  CUT the panel {panel} leaves the screen"); }
                foreach (Transform ch in p)
                {
                    if (!ch.gameObject.activeInHierarchy || !(ch is RectTransform rt)) continue;
                    string n = ch.name;
                    if (n == "Key" || n.StartsWith("Key_"))
                    {
                        Rect cap = World(rt); boxes.Add(new Box { name = n, r = cap });
                        var kt = ch.Find("Key") ? ch.Find("Key").GetComponent<TMP_Text>() : null;
                        if (Ink(kt, out var ki) && !Inside(ki, cap, 0.5f)) { overflow++; detail.AppendLine($"  OVERFLOW '{kt.text}' leaves its key cap"); }
                    }
                    else if (n.StartsWith("What") || n.StartsWith("Sec_") || n == "Sep" || n == "Title" || n == "Sub" || n == "Foot" || n == "Status")
                    {
                        var t = ch.GetComponent<TMP_Text>();
                        if (!Ink(t, out var r)) continue;
                        boxes.Add(new Box { name = n + " '" + t.text + "'", r = r });
                        if (n.StartsWith("What"))
                        {
                            rows++;
                            if (t.text == (full ? FullRow : CompactRow)) rRow = true;
                            if (t.fontSize < want - 0.01f || t.overflowMode == TextOverflowModes.Ellipsis) { shrunk++; detail.AppendLine($"  SHRUNK '{t.text}' at {t.fontSize:0.0} u, not {want:0} u"); }
                        }
                    }
                    else if (n == "HeadRule" || n == "FootRule" || n.Contains("ResetKeys")) boxes.Add(new Box { name = n, r = World(rt) });
                }
                rects += boxes.Count;
                for (int i = 0; i < boxes.Count; i++)
                {
                    if (!Inside(boxes[i].r, panel, 0.5f)) { cut++; detail.AppendLine($"  CUT {boxes[i].name} leaves the panel"); }
                    for (int j = i + 1; j < boxes.Count; j++)
                        if (Meet(boxes[i].r, boxes[j].r, 0.5f)) { overlaps++; detail.AppendLine($"  OVERLAP {boxes[i].name} and {boxes[j].name}"); }
                }
            }
            int bad = overlaps + overflow + cut + shrunk + (tables > 0 && !rRow ? 1 : 0);
            string line = $"{LayoutTag}{screen} {Screen.width}x{Screen.height}, UI {OzText.UiScale * 100f:0} %: {tables} table(s), rows {rows}, rectangles {rects}, overlaps {overlaps}, overflow {overflow}, cut {cut}, shrunk {shrunk}, R row {(rRow ? "found" : "MISSING")}";
            log.AppendLine(line); if (detail.Length > 0) log.Append(detail);
            Debug.Log(line + (detail.Length > 0 ? "\n" + detail : ""));
            return tables == 0 ? -1 : bad;
        }

        IEnumerator LayoutAll()
        {
            yield return null;
            var log = new StringBuilder(); int bad = 0, screens = 0;
            void Add(int v) { if (v >= 0) { bad += v; screens++; } else bad++; }
            var menu = FindFirstObjectByType<OpenZMainMenu>();
            var h = OpenZHud.I;
            string res = $"{Screen.width}x{Screen.height}";
            if (menu)
            {
                menu.ProofOpen("controls"); yield return new WaitForSecondsRealtime(0.8f);
                Add(Layout("main menu CONTROLS", log)); ScreenCapture.CaptureScreenshot(Path.Combine(Dir(), $"k327_controls_main_{res}.png"));
                yield return new WaitForSecondsRealtime(0.3f);
                menu.ProofOpen("none");
            }
            else if (h && h.Pause)
            {
                float keep = OzText.UiScale;
                foreach (float s in new[] { 1f, 1.6f })
                {
                    OzText.UiScale = s; h.ApplyUiScale(); yield return new WaitForSecondsRealtime(0.2f);
                    h.Pause.Open();
                    typeof(OpenZPauseMenu).GetField("showControls", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(h.Pause, true);
                    yield return new WaitForSecondsRealtime(0.8f);
                    Add(Layout("pause menu CONTROLS", log)); ScreenCapture.CaptureScreenshot(Path.Combine(Dir(), $"k327_controls_pause_{res}_ui{s * 100f:0}.png"));
                    yield return new WaitForSecondsRealtime(0.3f);
                    typeof(OpenZPauseMenu).GetField("showControls", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(h.Pause, false);
                    h.Pause.Close(); yield return new WaitForSecondsRealtime(0.8f);
                    if (h.Help)
                    {
                        h.Help.Toggle(true); yield return new WaitForSecondsRealtime(0.8f);
                        Add(Layout("F1 controls help", log)); ScreenCapture.CaptureScreenshot(Path.Combine(Dir(), $"k327_controls_f1_{res}_ui{s * 100f:0}.png"));
                        yield return new WaitForSecondsRealtime(0.3f);
                        h.Help.Toggle(false); yield return new WaitForSecondsRealtime(0.5f);
                    }
                }
                OzText.UiScale = keep; h.ApplyUiScale();
            }
            else log.AppendLine(LayoutTag + "no main menu and no match: open the main menu scene or start a level");
            LayoutReport = $"{LayoutTag}LAYOUT {res}: {screens} screen(s), {bad} violations (pass = 0)";
            log.AppendLine(LayoutReport);
            try { File.WriteAllText(Path.Combine(Dir(), $"k327_controls_layout_{(menu ? "main" : "match")}_{res}.txt"), log.ToString()); } catch (IOException e) { Debug.LogWarning(LayoutTag + "report not written: " + e.Message); }
            Debug.Log(LayoutReport);
            Destroy(gameObject);
        }
    }
}
#endif
