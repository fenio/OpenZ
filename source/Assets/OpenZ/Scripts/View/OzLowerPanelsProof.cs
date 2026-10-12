using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace OpenZ
{
    /// <summary>
    /// Play check of the lower panels and the production window (OpenZHud.LowerPanels.cs). One level session in test mode:
    /// the production window is opened by every route and closed by every route, each as its own counted case, with a real
    /// (proof device) mouse and real key events. After each open: every lower panel and the minimap are not drawn and take
    /// no pointer, the top group is drawn. After each close: every panel is back with the rectangle and the visibility it
    /// had before the open. A watcher compares the switch with the window at the end of every frame of the whole run.
    /// Then the pictures: the window on a factory and on the fort at interface scales 90, 100 and 160 percent, and a fort
    /// alarm arriving while the window is open. The last case ends the match (the sim's own end check), so the level is
    /// used up afterwards. <c>OpenZ.OzLowerPanelsProof.Run()</c> in Play mode on a level; the Game view size is the
    /// </summary>
    [DefaultExecutionOrder(1000)]
    public class OzLowerPanelsProof : MonoBehaviour
    {
        public static string Report = "idle";
        public static void Run()
        {
            Report = "running";
#if UNITY_EDITOR
            var gv = System.Type.GetType("UnityEditor.GameView,UnityEditor"); if (gv != null) UnityEditor.EditorWindow.FocusWindowIfItsOpen(gv);
#endif
            var go = new GameObject("OzLowerPanelsProof"); DontDestroyOnLoad(go); go.AddComponent<OzLowerPanelsProof>();
        }

        // ------------------------------------------------------------------ proof devices
        static Mouse proofMouse, realMouse;
        void ProofMouseOn()
        {
            realMouse = Mouse.current;
            proofMouse = InputSystem.AddDevice<Mouse>("OzProofMouse");
            if (realMouse != null) InputSystem.DisableDevice(realMouse);
            proofMouse.MakeCurrent();
        }
        static void ProofMouseOff()
        {
            if (realMouse != null) { InputSystem.EnableDevice(realMouse); realMouse.MakeCurrent(); realMouse = null; }
            if (proofMouse != null) InputSystem.RemoveDevice(proofMouse);
            proofMouse = null;
        }
        static void MouseAt(Vector2 p, bool down = false)
        {
            var st = new MouseState { position = p }; if (down) st = st.WithButton(MouseButton.Left, true);
            InputSystem.QueueStateEvent(proofMouse != null ? proofMouse : Mouse.current, st);
        }
        IEnumerator Click(Vector2 p)
        {
            MouseAt(p); yield return null; yield return null;
            MouseAt(p, true); yield return null; yield return null;
            MouseAt(p, false); yield return null; yield return null;
        }
        IEnumerator Tap(Key k)
        {
            var kb = Keyboard.current; if (kb == null) { Check("a keyboard device for the key cases", false); yield break; }
            InputSystem.QueueStateEvent(kb, new KeyboardState(k)); yield return null; yield return null;
            InputSystem.QueueStateEvent(kb, new KeyboardState()); yield return null; yield return null;
        }
        static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }
        static IEnumerator Real(float s) { yield return new WaitForSecondsRealtime(s); }

        // ------------------------------------------------------------------ log
        readonly StringBuilder sb = new StringBuilder(); int fails, frames, errors, lateErrors, cases, casesOk; string dir; bool ended;
        void Log(string s) { sb.AppendLine(s); Debug.Log("[OzLowerPanelsProof] " + s); }
        void Check(string what, bool ok) { if (!ok) fails++; Log((ok ? "PASS " : "FAIL ") + what); }
        IEnumerator Shot(string name)
        {
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, name + ".png")); frames++; Log($"frame {name}.png");
            yield return null; yield return new WaitForSecondsRealtime(0.25f);
        }
        void OnLog(string msg, string st, LogType type)
        {
            if (type != LogType.Exception && type != LogType.Error) return;
            if (ended) { lateErrors++; if (lateErrors < 6) sb.AppendLine("CONSOLE after the match end " + type + ": " + msg); }
            else { errors++; if (errors < 6) sb.AppendLine("CONSOLE " + type + ": " + msg); }
        }
        void OnDestroy() { ProofMouseOff(); Application.logMessageReceived -= OnLog; }

        static object F(object o, string n)
        {
            var t = o.GetType();
            while (t != null) { var f = t.GetField(n, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance); if (f != null) return f.GetValue(o); t = t.BaseType; }
            return null;
        }
        static bool Active(RectTransform r) => r && r.gameObject.activeInHierarchy;
        static Vector2 Center(RectTransform rt) { var c = new Vector3[4]; rt.GetWorldCorners(c); return (c[0] + c[2]) * 0.5f; }

        // ------------------------------------------------------------------ state
        OpenZHud hud; ZGame g; ZWorld W; ZObject fort, fac;
        RectTransform top, terr, cmdBar, unitPanel, factoryPanel; HudButton menuBtn;
        bool watching; int watched, violations, flips; bool lastHidden;

        void Start()
        {
            dir = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../../../../work/records/milestones/h13_hide_panels_build_menu"));
            System.IO.Directory.CreateDirectory(dir);
            Application.logMessageReceived += OnLog;
            StartCoroutine(All());
        }

        /// <summary>After the HUD's own LateUpdate: the switch equals the window on every frame, every canvas follows it.</summary>
        void LateUpdate()
        {
            if (!watching || !hud || !hud.Production) return;
            watched++;
            bool hidden = hud.LowerPanelsHidden;
            if (hidden != lastHidden) { flips++; lastHidden = hidden; }
            bool want = hud.Production.IsOpen || (hidden && W != null && W.GameOver);
            bool bad = hidden != want;
            var roots = hud.LowerPanelRoots;
            for (int i = 0; i < roots.Count && !bad; i++) { var c = roots[i] ? roots[i].GetComponent<Canvas>() : null; if (!c || c.enabled == hidden) bad = true; }
            if (bad) { violations++; if (violations < 6) Log($"WATCH frame {Time.frameCount}: hidden {hidden}, window open {hud.Production.IsOpen}"); }
        }

        sealed class Snap { public bool[] active, drawn; public Vector3[][] corners; public float[] alpha; public bool alert; }
        Snap Take()
        {
            var roots = hud.LowerPanelRoots; int n = roots.Count;
            var s = new Snap { active = new bool[n], drawn = new bool[n], corners = new Vector3[n][], alpha = new float[n], alert = (bool)F(hud, "alertActive") };
            for (int i = 0; i < n; i++)
            {
                var rt = roots[i]; var c = rt.GetComponent<Canvas>(); var cg = rt.GetComponent<CanvasGroup>();
                s.active[i] = rt.gameObject.activeInHierarchy; s.drawn[i] = c && c.enabled; s.alpha[i] = cg ? cg.alpha : 1f;
                s.corners[i] = new Vector3[4]; rt.GetWorldCorners(s.corners[i]);
            }
            return s;
        }
        string Diff(Snap a, Snap b)
        {
            var roots = hud.LowerPanelRoots; var d = new StringBuilder();
            for (int i = 0; i < roots.Count; i++)
            {
                string nm = roots[i].name;
                if (!b.drawn[i]) d.Append($" [{nm}: canvas still off]");
                if (a.active[i] != b.active[i] && !(nm == "AlertTab" && a.alert != b.alert)) d.Append($" [{nm}: active {a.active[i]} -> {b.active[i]}]");
                if (a.active[i] && b.active[i])
                {
                    float m = 0f; for (int k = 0; k < 4; k++) m = Mathf.Max(m, (a.corners[i][k] - b.corners[i][k]).magnitude);
                    if (m > 0.05f && nm != "AlertTab") d.Append($" [{nm}: rectangle moved {m:0.00} px]");
                    if (Mathf.Abs(a.alpha[i] - b.alpha[i]) > 0.01f && nm != "FactoryList") d.Append($" [{nm}: alpha {a.alpha[i]:0.00} -> {b.alpha[i]:0.00}]");
                }
            }
            return d.ToString();
        }

        bool AllDrawn() { foreach (var rt in hud.LowerPanelRoots) { var c = rt.GetComponent<Canvas>(); if (!c || !c.enabled) return false; } return true; }
        bool UnderLower(RectTransform rt) { if (!rt) return false; foreach (var r in hud.LowerPanelRoots) if (rt.IsChildOf(r)) return true; return false; }

        /// <summary>The window is open: nothing lower is drawn or reachable, the top group is drawn.</summary>
        IEnumerator HiddenChecks(string tag)
        {
            var roots = hud.LowerPanelRoots; var bad = new StringBuilder();
            foreach (var rt in roots)
            {
                var c = rt.GetComponent<Canvas>();
                if (!c || c.enabled) bad.Append($" [{rt.name}: drawn]");
                if (OpenZHud.Contains(rt, Center(rt))) bad.Append($" [{rt.name}: takes the pointer]");
            }
            Check($"{tag}: open: no lower panel and no minimap drawn or reachable{bad}", hud.LowerPanelsHidden && bad.Length == 0);
            Check($"{tag}: open: the top group is drawn (HUD canvas {hud.Canvas.enabled}, strip {Active(top)}, territory {Active(terr)}, menu button {Active(menuBtn.Rt)})",
                  hud.Canvas.enabled && Active(top) && Active(terr) && Active(menuBtn.Rt) && OpenZHud.Contains(menuBtn.Rt, Center(menuBtn.Rt)));
            // the pointer over the places of the command bar, the unit panel and the minimap: no hover, no minimap pointer
            var win = hud.Production.WindowRect; bool hover = false, overUi = false;
            foreach (var rt in new[] { cmdBar, unitPanel, hud.Minimap.Panel })
            {
                var p = Center(rt);
                MouseAt(p); yield return null; yield return null; yield return null;
                if (hud.HotButton != null && UnderLower(hud.HotButton.Rt)) hover = true;
                if (hud.Minimap.PointerOverMap) hover = true;
                if (hud.PointerOverUi && !OpenZHud.Contains(win, p)) overUi = true;
            }
            Check($"{tag}: open: the pointer over the panels' places finds no button and no minimap", !hover);
            Check($"{tag}: open: those places are map, not interface (unless the window itself lies there)", !overUi);
        }

        Vector2 Park => new Vector2(Screen.width * 0.5f, Screen.height * 0.42f);

        IEnumerator CloseQuiet()
        {
            if (hud.Pause && hud.Pause.IsOpen) { hud.Pause.Close(); yield return Real(0.5f); }
            if (hud.input.Placing != null) hud.input.CancelPlace();
            if (hud.Rally && hud.Rally.Armed) hud.Rally.Disarm();
            if (hud.Production.IsOpen) hud.Production.Close();
            MouseAt(Park); yield return Real(0.4f);
        }

        /// <summary>One counted case: open by a route, check, close by a route, check that everything is back.</summary>
        IEnumerator RunCase(string name, IEnumerator open, IEnumerator close, int wantFlips = 2, bool wantBack = true)
        {
            cases++; int f0 = fails;
            yield return CloseQuiet();
            Check($"{name}: before: window closed, every lower panel drawn", !hud.Production.IsOpen && !hud.LowerPanelsHidden && AllDrawn());
            var before = Take(); int flips0 = flips;
            yield return open;
            yield return Frames(3);
            Check($"{name}: the window is open", hud.Production.IsOpen);
            if (hud.Production.IsOpen) yield return HiddenChecks(name);
            yield return close;
            yield return Frames(3);
            Check($"{name}: the window is closed", !hud.Production.IsOpen);
            if (hud.Production.IsOpen) hud.Production.Close();
            yield return Real(0.45f);
            if (wantBack)
            {
                var after = Take(); string d = Diff(before, after);
                Check($"{name}: every lower panel is back as before{d}", !hud.LowerPanelsHidden && d.Length == 0);
            }
            else Check($"{name}: the lower panels stay away while the HUD leaves", hud.LowerPanelsHidden);
            Check($"{name}: the panels went and came once ({flips - flips0} switches, {wantFlips} expected)", flips - flips0 == wantFlips);
            if (fails == f0) casesOk++;
            Log($"case {cases} '{name}': {(fails == f0 ? "PASS" : "FAIL")}");
        }

        // ------------------------------------------------------------------ routes: open
        IEnumerator Look(ZObject b) { if (hud.rts) hud.rts.FlyTo(g.CenterWorld(b)); yield return Real(1.6f); }

        /// <summary>A free point of the building's screen rectangle: on screen, not on the interface.</summary>
        bool BuildingPoint(ZObject b, out Vector2 p)
        {
            var r = hud.input.ScreenRect(b);
            foreach (var o in new[] { new Vector2(0.5f, 0.5f), new Vector2(0.35f, 0.5f), new Vector2(0.65f, 0.5f), new Vector2(0.5f, 0.35f), new Vector2(0.5f, 0.65f) })
            {
                p = new Vector2(r.xMin + r.width * o.x, r.yMin + r.height * o.y);
                if (p.x > 8 && p.y > 8 && p.x < Screen.width - 8 && p.y < Screen.height - 8 && !hud.IsOverUi(p)) return true;
            }
            p = r.center; return false;
        }
        IEnumerator OpenByClick(ZObject b)
        {
            yield return Look(b);
            if (!BuildingPoint(b, out var p)) Log($"no free point on {b.Type}: the centre is used");
            yield return Click(p);
            if (hud.Production.Building != b)
            {   // an own unit stood on that point (it wins the click): the other points of the rectangle
                var r = hud.input.ScreenRect(b);
                foreach (var o in new[] { new Vector2(0.25f, 0.3f), new Vector2(0.75f, 0.3f), new Vector2(0.25f, 0.7f), new Vector2(0.75f, 0.7f) })
                {
                    if (hud.Production.Building == b) break;
                    var q = new Vector2(r.xMin + r.width * o.x, r.yMin + r.height * o.y);
                    if (!hud.IsOverUi(q) || hud.LowerPanelsHidden) yield return Click(q);
                }
            }
            Check($"a left click on the own {b.Type} opens its window", hud.Production.Building == b);
        }
        IEnumerator OpenByListRow()
        {
            hud.factoryListOpen = true; yield return Real(0.6f);
            var rows = F(hud, "factoryRows") as IList; RectTransform row = null;
            if (rows != null) foreach (var r in rows) { var btn = F(r, "btn") as HudButton; if (btn != null && Active(btn.Rt)) { row = btn.Rt; break; } }
            Check("the factory list shows a row to click", row != null);
            if (row != null) yield return Click(Center(row));
        }
        string cannonType;
        string CannonOf(ZObject b)
        {
            for (int lv = b.BuildLevel; lv <= 5; lv++) { var c = ZBuildLists.For(b.Type, lv).FirstOrDefault(ZBuildLists.IsCannon); if (c != null) return c; }
            return null;
        }
        IEnumerator StoreCannon(ZObject b)
        {
            if (cannonType == null) cannonType = CannonOf(b);
            Check("the test fort has a cannon type to store", cannonType != null);
            if (cannonType != null && b.StoredCannons.Count == 0) b.StoredCannons.Add(cannonType);
            yield return Real(0.5f);
        }
        IEnumerator OpenByCannonPlate()
        {
            yield return StoreCannon(fort);
            var plates = F(hud, "cannonPlates") as IList; RectTransform plate = null;
            if (plates != null) foreach (var pl in plates) { var btn = F(pl, "btn") as HudButton; if (btn != null && Active(btn.Rt)) { plate = btn.Rt; break; } }
            Check("a cannon plate shows for the stored cannon", plate != null);
            if (plate != null) yield return Click(Center(plate));
        }
        IEnumerator OpenByMessage(bool key)
        {
            var b = fort;
            hud.Toast("GUN MANUFACTURED", "cannon", false, () => { if (hud.rts) hud.rts.FlyTo(g.CenterWorld(b)); hud.Production.Open(b); });
            yield return Real(0.9f);
            if (key) { yield return Tap(OzBindings.KeyOf(OzAction.LastMessage)); yield break; }
            var toasts = F(hud, "toasts") as IList; RectTransform plate = null;
            if (toasts != null) foreach (var t in toasts) { var rt = F(t, "rt") as RectTransform; if ((bool)F(t, "used") && Active(rt)) { plate = rt; break; } }
            Check("the message plate shows", plate != null);
            if (plate != null) yield return Click(Center(plate));
        }

        // ------------------------------------------------------------------ routes: close
        RectTransform WinButton(string field) { var b = F(hud.Production, field) as HudButton; return b != null ? b.Rt : null; }
        IEnumerator CloseByButton(string field) { var rt = WinButton(field); Check($"the window's {field} control is there", Active(rt)); if (rt) yield return Click(Center(rt)); }

        /// <summary>A point inside a hidden panel's place that is plain map: not the window, not an own producer.</summary>
        Vector2 MapPointIn(RectTransform rt)
        {
            var c = new Vector3[4]; rt.GetWorldCorners(c);
            for (int iy = 1; iy <= 3; iy++)
                for (int ix = 1; ix <= 5; ix++)
                {
                    var p = new Vector2(Mathf.Lerp(c[0].x, c[2].x, ix / 6f), Mathf.Lerp(c[0].y, c[2].y, iy / 4f));
                    if (!hud.IsOverUi(p) && hud.Production.ProducerUnder(p) == null) return p;
                }
            return Center(rt);
        }
        IEnumerator CloseByClickOnBarPlace()
        {
            bool list0 = hud.factoryListOpen; var bB = F(hud, "bB") as HudButton;
            var p = bB != null ? Center(bB.Rt) : Center(cmdBar);
            if (hud.IsOverUi(p) || hud.Production.ProducerUnder(p) != null) p = MapPointIn(cmdBar);
            yield return Click(p);
            Check("a click where the factory list key of the command bar was: the list did not toggle (the click went to the map)", hud.factoryListOpen == list0);
        }
        IEnumerator CloseByClickOnMinimapPlace()
        {
            yield return Real(1.2f);                                                                     // the fly-to of the opening click has come to rest
            var cam0 = hud.rts ? hud.rts.transform.position : Vector3.zero;
            var p = MapPointIn(hud.Minimap.Panel);
            yield return Click(p); yield return Real(0.5f);
            var cam1 = hud.rts ? hud.rts.transform.position : Vector3.zero;
            Check($"a click where the minimap was: the camera did not fly ({(cam1 - cam0).magnitude:0.000} m), no minimap drag", (cam1 - cam0).magnitude < 0.05f && !hud.Minimap.Dragging);
        }
        IEnumerator CloseBySelectingUnit()
        {
            ZObject pick = null; float best = -1f;
            foreach (var o in W.Objects)
            {
                if (o.Owner != g.player || o.Destroyed || o.IsFortDefender || (o.Kind != ZKind.Robot && o.Kind != ZKind.Vehicle)) continue;
                float dd = (g.CenterWorld(o) - g.CenterWorld(fort)).sqrMagnitude; if (dd > best) { best = dd; pick = o; }
            }
            Check("an own unit to select", pick != null);
            if (pick == null) yield break;
            yield return Look(pick);
            var p = hud.input.ScreenRect(pick).center;
            Log($"unit {pick.Type} at {p:0}, under the window {OpenZHud.Contains(hud.Production.WindowRect, p)}");
            yield return Click(p);
            Check("the click selected the unit", hud.input.Selection.Contains(pick.Leader ?? pick) || hud.input.Selection.Contains(pick));
        }
        IEnumerator CloseByCapture()
        {
            var b = hud.Production.Building; if (b == null) yield break;
            var owner = b.Owner; b.Owner = g.player == Team.Red ? Team.Blue : Team.Red;                    // test only: the building changes hands
            yield return Frames(3);
            Check("a captured building closes its window", !hud.Production.IsOpen);
            b.Owner = owner;
        }
        IEnumerator CloseByPlace()
        {
            yield return CloseByButton("bPlace");
            Check("PLACE: placement is on, the window is closed", hud.input.Placing != null && !hud.Production.IsOpen);
            Check("PLACE: the lower panels are back during placement", !hud.LowerPanelsHidden && AllDrawn());
            yield return Shot($"{tagNow}_placement_panels_back");
            hud.input.CancelPlace(); fort.StoredCannons.Clear();
            yield return Real(0.4f);
        }
        IEnumerator CloseByRally()
        {
            var b = hud.Production.Building; int n0 = b != null ? b.RallyPoints.Count : 0;
            yield return CloseByButton("bRally");
            Check("SET RALLY: the tool is armed, the window is closed", hud.Rally.Armed && !hud.Production.IsOpen);
            Check("SET RALLY: the lower panels are back while the tool is armed", !hud.LowerPanelsHidden && AllDrawn());
            yield return Shot($"{tagNow}_rally_panels_back");
            yield return Click(Center(hud.Minimap.Panel)); yield return Real(0.3f);                       // the minimap takes the rally click again
            Check("SET RALLY: a click on the minimap sets the rally point", b != null && b.RallyPoints.Count > 0 && !hud.Rally.Armed);
            if (b != null && n0 == 0) hud.Rally.Clear(b);
            yield return Real(0.3f);
        }
        IEnumerator CloseByPause()
        {
            yield return Click(Center(menuBtn.Rt));
            yield return Real(0.5f);
            Check("MENU button: the pause menu is up, the window is closed, the HUD is hidden", hud.Pause.IsOpen && !hud.Production.IsOpen && !hud.hudVisible);
            Check("pause: the lower switch is released under the hidden HUD", !hud.LowerPanelsHidden && AllDrawn());
            yield return Shot($"{tagNow}_pause_over_the_window");
            yield return Tap(Key.Escape);
            yield return Real(0.6f);
            Check("resume: the HUD is back, the pause menu and the window are closed", !hud.Pause.IsOpen && hud.hudVisible && hud.Canvas.enabled && !hud.Production.IsOpen);
        }
        IEnumerator CloseByHideKey()
        {
            var k = OzBindings.KeyOf(OzAction.HideHud);
            yield return Tap(k);
            Check("hide-HUD key: the HUD is hidden, the window is closed", !hud.hudVisible && !hud.Production.IsOpen);
            yield return Tap(k);
            Check("hide-HUD key again: the HUD is back", hud.hudVisible && hud.Canvas.enabled);
        }
        IEnumerator CloseByCinematic()
        {
            hud.SetVisible(false); hud.enabled = false;                                                  // what a briefing or an ending does to the HUD
            Check("HUD switched off as a whole: the window is closed and the lower switch is released at once", !hud.Production.IsOpen && !hud.LowerPanelsHidden && AllDrawn());
            yield return Frames(5);
            hud.enabled = true; hud.SetVisible(true);
            Check("HUD switched on again: every lower panel is drawn on that same frame", hud.Canvas.enabled && !hud.LowerPanelsHidden && AllDrawn());
            yield return Frames(2);
        }
        IEnumerator CloseByListKey()
        {
            bool list0 = hud.factoryListOpen;
            yield return Tap(OzBindings.KeyOf(OzAction.FactoryList));
            yield return Frames(2);
            Check("factory list key: the window is closed and the list is open and drawn", !hud.Production.IsOpen && hud.factoryListOpen && Active(factoryPanel) && AllDrawn());
            yield return Shot($"{tagNow}_list_key_list_shown");
            hud.factoryListOpen = list0; yield return Real(0.4f);
        }
        IEnumerator CloseAfterSwap()
        {
            int fl = flips;
            yield return Look(fac);
            BuildingPoint(fac, out var p);
            if (OpenZHud.Contains(hud.Production.WindowRect, p)) Log("the factory lies under the pinned window: the swap click may miss");
            yield return Click(p); yield return Frames(3);
            Check("a click on another own producer swaps the window to it", hud.Production.Building == fac);
            Check("the swap: the lower panels stayed hidden all the time (no switch)", flips == fl && hud.LowerPanelsHidden);
            yield return CloseByButton("bClose");
        }
        IEnumerator CloseByMatchEnd()
        {
            foreach (var o in W.Objects.ToList()) if (o.IsUnit && o.Owner != g.player && o.Owner != Team.Neutral) o.Owner = Team.Neutral;   // test only: the sim's own end check ends the match
            float t0 = Time.realtimeSinceStartup;
            while (!W.GameOver && Time.realtimeSinceStartup - t0 < 6f) yield return null;
            ended = true;
            yield return Frames(3);
            Check("match end: the sim ended the match, the window is closed", W.GameOver && !hud.Production.IsOpen);
            Check("match end: the lower panels do not come back for the HUD's exit", hud.LowerPanelsHidden);
            yield return Real(0.12f);
            yield return Shot($"{tagNow}_match_end_no_panels_pop_back");
        }

        // ------------------------------------------------------------------ the run
        string tagNow = "run";

        IEnumerator All()
        {
            yield return new WaitForSecondsRealtime(1f);
            float w0 = Time.realtimeSinceStartup;
            while ((ZGame.I == null || ZGame.I.World == null || OpenZHud.I == null) && Time.realtimeSinceStartup - w0 < 60f) yield return null;
            while (ZodBriefing.BriefingActive && Time.realtimeSinceStartup - w0 < 60f) yield return null;
            yield return new WaitForSecondsRealtime(4f);                                                   // the HUD entrance and the mission banner end
            hud = OpenZHud.I; g = ZGame.I; W = g != null ? g.World : null;
            if (!hud || W == null || !hud.Production || !hud.Minimap) { Report = "no level with a HUD"; Destroy(gameObject); yield break; }
            float scale0 = OzText.UiScale; bool minimal0 = OzSettingTips.MinimalUi;
            if (minimal0) { OzSettingTips.MinimalUi = false; yield return Real(0.5f); }
            top = F(hud, "top") as RectTransform; terr = F(hud, "terr") as RectTransform; cmdBar = F(hud, "cmdBar") as RectTransform;
            unitPanel = F(hud, "unitPanel") as RectTransform; factoryPanel = F(hud, "factoryPanel") as RectTransform; menuBtn = F(hud, "menuBtn") as HudButton;
            tagNow = $"{g.level}_{Screen.width}x{Screen.height}";
            Log($"level {g.level}, screen {Screen.width}x{Screen.height}, interface scale {scale0:0.00}, test mode {GameBoot.TestMode}, lower panels: {string.Join(", ", hud.LowerPanelRoots.Select(r => r.name))}");
            Check("five lower panels are registered (alert tab, minimap, unit panel, command bar, factory list)", hud.LowerPanelRoots.Count == 5 && top && terr && cmdBar && unitPanel && factoryPanel && menuBtn != null);
            fort = W.Objects.FirstOrDefault(o => o.IsFort && o.Owner == g.player && !o.Destroyed);
            fac = W.Objects.FirstOrDefault(o => o.IsProducer && !o.IsFort && o.Owner == g.player && !o.Destroyed);
            if (fac == null)
            {   // test only: the nearest factory is handed to the player so the factory cases have a building
                float best = float.MaxValue;
                foreach (var o in W.Objects)
                {
                    if (!o.IsProducer || o.IsFort || o.Destroyed || fort == null) continue;
                    float dd = (g.CenterWorld(o) - g.CenterWorld(fort)).sqrMagnitude; if (dd < best) { best = dd; fac = o; }
                }
                if (fac != null) { fac.Owner = g.player; Log($"test: {fac.Type} #{fac.Id} handed to the player"); }
            }
            Check("an own fort and an own factory for the cases", fort != null && fac != null);
            if (fort == null || fac == null) { Finish(scale0, minimal0); yield break; }

            ProofMouseOn(); MouseAt(Park);
            lastHidden = hud.LowerPanelsHidden; watching = true;
            yield return Real(0.5f);

            // every way in (closed with the X each time)
            yield return RunCase("open: click on the factory", OpenByClick(fac), CloseByButton("bClose"));
            yield return RunCase("open: click on the fort", OpenByClick(fort), CloseByButton("bClose"));
            hud.factoryListOpen = true; yield return Real(0.6f);
            yield return RunCase("open: a row of the factory list", OpenByListRow(), CloseByButton("bClose"));
            hud.factoryListOpen = false; yield return Real(0.4f);
            yield return StoreCannon(fort);
            yield return RunCase("open: a cannon plate", OpenByCannonPlate(), CloseByButton("bClose"));
            yield return RunCase("open: a click on the gun message", OpenByMessage(false), CloseByButton("bClose"));
            yield return RunCase("open: the last-message key on the gun message", OpenByMessage(true), CloseByButton("bClose"));
            fort.StoredCannons.Clear(); yield return Real(0.4f);

            // every way out (opened with a click on the fort each time)
            yield return RunCase("close: Esc", OpenByClick(fort), Tap(Key.Escape));
            yield return RunCase("close: a click on the map where the command bar was", OpenByClick(fort), CloseByClickOnBarPlace());
            yield return RunCase("close: a click on the map where the minimap was", OpenByClick(fort), CloseByClickOnMinimapPlace());
            yield return RunCase("close: selecting a unit with a click", OpenByClick(fort), CloseBySelectingUnit());
            yield return RunCase("close: the building is captured", OpenByClick(fac), CloseByCapture());
            yield return StoreCannon(fort);
            yield return RunCase("close: the PLACE button (gun placement)", OpenByClick(fort), CloseByPlace());
            yield return RunCase("close: the SET RALLY button (rally placement)", OpenByClick(fort), CloseByRally());
            yield return RunCase("close: the pause menu opened on top, then resumed", OpenByClick(fort), CloseByPause());
            yield return RunCase("close: the hide-HUD key, twice", OpenByClick(fort), CloseByHideKey());
            yield return RunCase("close: the HUD switched off and on as a whole (briefing / ending)", OpenByClick(fort), CloseByCinematic());
            yield return RunCase("close: the factory list key", OpenByClick(fort), CloseByListKey());
            yield return RunCase("swap to another building, then the X", OpenByClick(fort), CloseAfterSwap());
            yield return CloseQuiet();

            // the pictures: factory and fort at 90 / 100 / 160 percent, and a fort alarm arriving with the window open
            foreach (float s in new[] { 0.9f, 1f, 1.6f })
            {
                OzText.UiScale = s; hud.ApplyUiScale(); yield return Real(0.6f);
                foreach (var b in new[] { fac, fort })
                {
                    yield return Look(b); MouseAt(Park);
                    hud.Production.Open(b); yield return Real(0.7f);
                    Check($"picture {Mathf.RoundToInt(s * 100)} % {b.Type}: window open, lower panels hidden", hud.Production.IsOpen && hud.LowerPanelsHidden);
                    yield return Shot($"{tagNow}_scale{Mathf.RoundToInt(s * 100)}_{(b.IsFort ? "fort" : "factory")}_window_open");
                    hud.Production.Close(); yield return Real(0.5f);
                    yield return Shot($"{tagNow}_scale{Mathf.RoundToInt(s * 100)}_{(b.IsFort ? "fort" : "factory")}_window_closed");
                }
            }
            OzText.UiScale = scale0; hud.ApplyUiScale(); yield return Real(0.6f);
            yield return Look(fort); MouseAt(Park);
            hud.Production.Open(fort); yield return Real(0.6f);
            float t1 = Time.unscaledTime; int hp = fort.Health;
            float tw = Time.realtimeSinceStartup; var alarm = F(hud, "fortToast"); RectTransform alarmRt = alarm != null ? F(alarm, "rt") as RectTransform : null;
            while (alarm != null && !(bool)F(alarm, "used") && Time.realtimeSinceStartup - tw < 10f)
            {   // test only: the sim's own record of enemy hits on the fort, kept fresh until the alarm has armed and fired
                fort.LastEnemyHit = W.Time; yield return null;
            }
            yield return Real(0.5f);
            bool shown = alarm != null && (bool)F(alarm, "used") && Active(alarmRt);
            Check("a fort alarm arrives with the window open: its plate shows in the top group, in front of the window", shown && hud.Production.IsOpen && hud.LowerPanelsHidden
                  && alarmRt.GetSiblingIndex() > hud.Production.WindowRect.GetSiblingIndex());
            Log($"fort alarm sounds since the hit: {string.Join(", ", OzSoundLog.Since(t1).Select(e => e.key + (e.clip != null ? "=" + e.clip : "")))}; fort health {hp} -> {fort.Health}");
            yield return Shot($"{tagNow}_fort_alarm_with_window_open");
            if (shown)
            {   // the plate is the one drawn in front, so it is the one the pointer finds, also where it lies over the window
                var alarmHot = F(alarm, "hot") as HudButton; var ap = Center(alarmRt);
                MouseAt(ap); yield return Frames(3);
                Check($"the pointer on the alarm plate finds the plate (the window lies under that point: {OpenZHud.Contains(hud.Production.WindowRect, ap)})", alarmHot != null && hud.HotButton == alarmHot);
                MouseAt(Park); yield return Frames(2);
            }
            // the other order: the plate is up first, then a window opens (it jumps to the front when it opens)
            hud.Production.Close(); yield return Frames(3);
            hud.Production.Open(fort); yield return Frames(3);
            bool still = alarm != null && (bool)F(alarm, "used") && Active(alarmRt);
            Check("a window opened while the alarm plate is up: the plate is still in front of it", still && hud.Production.IsOpen
                  && alarmRt.GetSiblingIndex() > hud.Production.WindowRect.GetSiblingIndex());
            yield return Real(0.4f);
            yield return Shot($"{tagNow}_fort_alarm_then_window_opened");
            yield return CloseQuiet();

            // the last case uses the level up
            yield return RunCase("close: the match ends", OpenByClick(fort), CloseByMatchEnd(), 1, false);
            Finish(scale0, minimal0);
        }

        void Finish(float scale0, bool minimal0)
        {
            watching = false;
            if (hud) { OzText.UiScale = scale0; hud.ApplyUiScale(); }
            if (OzSettingTips.MinimalUi != minimal0) OzSettingTips.MinimalUi = minimal0;
            Check($"watcher: the switch followed the window on every frame ({watched} frames, {violations} off)", violations == 0 && watched > 0);
            Log($"done: {casesOk} of {cases} cases PASS, {fails} FAIL lines, {frames} frames, {errors} console errors ({lateErrors} more after the match end, listed, not counted)");
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, $"proof_log_{tagNow}.txt"), sb.ToString());
            Report = fails == 0 && errors == 0 ? $"PASS {casesOk}/{cases}" : $"{fails} FAIL / {errors} errors, {casesOk}/{cases} cases";
            ProofMouseOff();
            Destroy(gameObject);
        }
    }
}
