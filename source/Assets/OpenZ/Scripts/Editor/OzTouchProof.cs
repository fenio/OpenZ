using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using OpenZ.Sim;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Counted check of touch and gamepad play (OzTouch.cs, OzTouch.Gamepad.cs, OpenZHud.TouchKeys.cs).
    /// Menu: OpenZ/Checks/Touch and gamepad (Play mode), or OpenZ.EditorTools.OzTouchProof.Run(). It runs the part that fits the
    /// scene that is loaded and ends with one log line "[OpenZ touch and gamepad] PASS n/n checks (...)" or "... FAIL ..." (also
    /// subfolder f12_touch_gamepad; the pictures are named by screen size and interface scale).
    ///
    /// Nothing real is touched: the check adds a touch screen, a gamepad and a mouse of its own (OzProofTouch, OzProofPad,
    /// OzProofMouse), queues their events like a device would, and switches the editor's own mouse off for the run, so the
    /// pointer under your hand cannot take part. Keep the Game view in front.
    /// The check runs from the editor's update. There the Input System shows the editor's own copy of every device, and in
    /// Play, with the Game view in front, that copy gets no events: a stick, a button or a pointer's place read from here is
    /// at rest, 0, 0. So the check asks the game (selection, orders, camera) and never a device; for the one check that
    /// needs the pointer's place (G1) the place of the game's mouse is taken down right after each input update of the game.
    ///
    /// In a level (the HUD in, no menu open), 38 checks:
    ///  T (17)  touch: the first touch takes the pointer (virtual mouse current); tap an own squad = selected; the on-screen keys
    ///          show, inside the screen, clear of all eight HUD groups; tap the ground = the order, the selection stays; double tap
    ///          = all of that type; drag = box select; hold on a key = hover, no click; the UI key flips MINIMAL UI and back; a
    ///          SPEED key steps; ZOOM + zooms in; HELP shows the card and BACK closes it without opening the pause menu; BACK then
    ///          opens the pause menu and ESC closes it; two fingers dragged = the view moves against the fingers and nothing is
    ///          ordered; a pinch zooms in; a two-finger tap = the right click's order; a tap on the own fort or factory opens its
    ///          build menu with the selection kept; a tap on a build picture starts or queues that unit; BACK closes the build menu.
    ///  M (3)   the mouse takes over again: the virtual mouse is no longer current and the keys are gone; a left click on the
    ///          ground deselects as always (no order); a right click orders.
    ///  G (9)   gamepad: the left stick takes the pointer and moves it; A on the squad selects; X on the ground orders; the right
    ///          stick scrolls the view; the right trigger zooms in; Start opens the pause menu and B closes it; LB + D-pad up
    ///          stores group 1 and D-pad up recalls it; B with nothing open clears the selection.
    ///  O (5)   TOUCH ON with only a mouse (a screen that sends mouse clicks): the mouse is read as the finger; a left click on
    ///          the ground orders and keeps the selection; a left drag still box-selects; the right button still orders.
    ///  L (3)   the on-screen keys at interface scale 160 %: shown, inside the screen, clear of the always-on panels (the line
    ///          says whether they are clear of all eight groups too).
    ///  X (1)   OFF: once the mouse has moved, a touch does nothing and the keys are gone.
    /// In the main menu, 6 checks: a touch tap opens the ADVANCED tab of OPTIONS; taps on the row's arrows step TOUCH · GAMEPAD and
    /// back; two fingers dragged over CHANGES scroll it; the D-pad moves the focus; A after the D-pad is ENTER (the level
    /// selection opens); B goes back.
    /// The player's settings, interface scale, game speed and devices are put back when it ends, also when a step throws.
    /// </summary>
    public static class OzTouchProof
    {
        const string Tag = "[OpenZ touch and gamepad]";
        public static string Report = "not run";
        /// <summary></summary>
        public static string Folder;

        static readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
        static int waitFrame, ok, bad, touchId = 100;
        static float waitUntil;
        static readonly StringBuilder fails = new StringBuilder();
        static string dir;
        static Touchscreen touch; static Gamepad pad; static Mouse mouse, sysMouse;
        static bool kept, minimal0; static int mode0, help0; static float scale0, speed0; static Vector2 mousePos;
        // the mouse the game calls current and its place, as the GAME reads them: taken down after each input update of the game
        // (the Input System calls this for the game's updates only; the editor's own update is left out in Play)
        static Vector2 gameMouseAt; static string gameMouse = "none";
        static void AfterInput()
        {
            if (InputState.currentUpdateType == InputUpdateType.Editor) return;
            var m = Mouse.current;
            if (m != null) { gameMouseAt = m.position.ReadValue(); gameMouse = m.name; } else gameMouse = "none";
        }

        [MenuItem("OpenZ/Checks/Touch and gamepad (Play mode)")]
        public static void Run()
        {
            if (!EditorApplication.isPlaying) { Debug.LogWarning(Tag + " enter Play mode first (a level, or the main menu)"); return; }
            if (stack.Count > 0) { Debug.LogWarning(Tag + " a run is still going"); return; }
            var gv = System.Type.GetType("UnityEditor.GameView,UnityEditor"); if (gv != null) EditorWindow.FocusWindowIfItsOpen(gv);
            ok = bad = 0; fails.Clear(); Report = "running";
            dir = OutDir(); Directory.CreateDirectory(dir);
            var menu = Object.FindFirstObjectByType<OpenZMainMenu>();
            if (OpenZHud.I && OpenZHud.I.Canvas && OpenZHud.I.W != null) stack.Push(Level(OpenZHud.I));
            else if (menu) stack.Push(Menu(menu));
            else { Report = Tag + " FAIL: no HUD and no main menu in this scene"; Debug.LogError(Report); return; }
            waitFrame = 0; waitUntil = 0f; EditorApplication.update += Step;
        }

        static string OutDir()
        {
            if (!string.IsNullOrEmpty(Folder)) return Path.GetFullPath(Folder);
            string all = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../../work/records/milestones")), stage = "v1_1";
            if (Directory.Exists(all))
            {
                var ds = Directory.GetDirectories(all, "v1_1*");
                System.Array.Sort(ds, System.StringComparer.Ordinal);
                if (ds.Length > 0) stage = Path.GetFileName(ds[ds.Length - 1]);
            }
            return Path.Combine(all, stage, "f12_touch_gamepad");
        }

        // a step yields an int (frames), a float (seconds of real time) or another step (run to its end first)
        static void Step()
        {
            if (stack.Count == 0 || !EditorApplication.isPlaying) { stack.Clear(); PutBack(); EditorApplication.update -= Step; return; }
            if (Time.frameCount < waitFrame || Time.realtimeSinceStartup < waitUntil) return;
            var top = stack.Peek(); bool more;
            try { more = top.MoveNext(); }
            catch (System.Exception ex) { Debug.LogException(ex); stack.Clear(); PutBack(); Report = Tag + " FAIL: exception " + ex.Message; Debug.LogError(Report); EditorApplication.update -= Step; return; }
            if (!more) { stack.Pop(); if (stack.Count == 0) EditorApplication.update -= Step; return; }
            if (top.Current is IEnumerator child) stack.Push(child);
            else if (top.Current is float s) waitUntil = Time.realtimeSinceStartup + s;
            else waitFrame = Time.frameCount + (top.Current is int n ? n : 1);
        }

        static void Check(bool pass, string what) { if (pass) ok++; else { bad++; fails.Append(" | ").Append(what); } Debug.Log($"{Tag} {(pass ? "ok  " : "FAIL")} {what}"); }

        static void Finish(string where, string detail)
        {
            PutBack();
            bool pass = bad == 0;
            Report = $"{Tag} {(pass ? "PASS" : "FAIL")} {ok}/{ok + bad} checks ({where}, {Screen.width}x{Screen.height}): {detail}{(pass ? "" : "; FAILED:" + fails)}";
            if (pass) Debug.Log(Report); else Debug.LogError(Report);
            File.AppendAllText(Path.Combine(dir, "checks.txt"), Report + "\n");
        }

        // ------------------------------------------------------------------ devices of the check
        static void Devices()
        {
            mode0 = PlayerPrefs.GetInt(OzTouch.Pref, 0); help0 = PlayerPrefs.GetInt(OzTouch.HelpPref, 0);
            minimal0 = OzSettingTips.MinimalUi; scale0 = OzText.UiScale; speed0 = ZGame.I ? ZGame.I.gameSpeed : 1f; kept = true;
            OzTouch.Setting = OzTouch.Mode.Auto;
            PlayerPrefs.SetInt(OzTouch.HelpPref, 1);                                                   // the first-time help card would cover the map
            sysMouse = Mouse.current;
            mouse = InputSystem.AddDevice<Mouse>("OzProofMouse");
            if (sysMouse != null && sysMouse != mouse) InputSystem.DisableDevice(sysMouse);
            touch = InputSystem.AddDevice<Touchscreen>("OzProofTouch");
            pad = InputSystem.AddDevice<Gamepad>("OzProofPad");
            InputSystem.onAfterUpdate -= AfterInput; InputSystem.onAfterUpdate += AfterInput;
            mousePos = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            MouseEv(mousePos, 0);
        }

        static void PutBack()
        {
            if (!kept) return;
            kept = false;
            InputSystem.onAfterUpdate -= AfterInput;
            OzTouch.Setting = (OzTouch.Mode)Mathf.Clamp(mode0, 0, 2);
            PlayerPrefs.SetInt(OzTouch.HelpPref, help0);
            if (OzSettingTips.MinimalUi != minimal0) OzSettingTips.MinimalUi = minimal0;
            if (ZGame.I) ZGame.I.gameSpeed = speed0;
            if (!Mathf.Approximately(OzText.UiScale, scale0)) { OzText.UiScale = scale0; if (OpenZHud.I) OpenZHud.I.ApplyUiScale(); }
            if (touch != null && touch.added) InputSystem.RemoveDevice(touch);
            if (pad != null && pad.added) InputSystem.RemoveDevice(pad);
            if (sysMouse != null) { InputSystem.EnableDevice(sysMouse); sysMouse.MakeCurrent(); }
            if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
            touch = null; pad = null; mouse = null; sysMouse = null;
            PlayerPrefs.Save();
        }

        static void TouchEv(int id, TouchPhase ph, Vector2 p) => InputSystem.QueueStateEvent(touch, new TouchState { touchId = id, phase = ph, position = p });
        static void MouseEv(Vector2 p, int buttons)
        {
            var st = new MouseState { position = p };
            if ((buttons & 1) != 0) st = st.WithButton(MouseButton.Left);
            if ((buttons & 2) != 0) st = st.WithButton(MouseButton.Right);
            InputSystem.QueueStateEvent(mouse, st); mousePos = p;
        }
        static void PadEv(Vector2 left, Vector2 right, float lt, float rt, params GamepadButton[] buttons)
        {
            var st = new GamepadState(buttons) { leftStick = left, rightStick = right, leftTrigger = lt, rightTrigger = rt };
            InputSystem.QueueStateEvent(pad, st);
        }

        static IEnumerator Tap(Vector2 p) { int id = ++touchId; TouchEv(id, TouchPhase.Began, p); yield return 3; TouchEv(id, TouchPhase.Ended, p); yield return 8; }
        static IEnumerator Drag(Vector2 a, Vector2 b)
        {
            int id = ++touchId; TouchEv(id, TouchPhase.Began, a); yield return 3;
            for (int i = 1; i <= 4; i++) { TouchEv(id, TouchPhase.Moved, Vector2.Lerp(a, b, i / 4f)); yield return 2; }
            TouchEv(id, TouchPhase.Ended, b); yield return 8;
        }
        static IEnumerator Two(Vector2 a0, Vector2 b0, Vector2 a1, Vector2 b1, int steps)
        {
            int ia = ++touchId, ib = ++touchId;
            TouchEv(ia, TouchPhase.Began, a0); TouchEv(ib, TouchPhase.Began, b0); yield return 3;
            for (int i = 1; i <= steps; i++) { float t = i / (float)steps; TouchEv(ia, TouchPhase.Moved, Vector2.Lerp(a0, a1, t)); TouchEv(ib, TouchPhase.Moved, Vector2.Lerp(b0, b1, t)); yield return 2; }
            TouchEv(ia, TouchPhase.Ended, a1); TouchEv(ib, TouchPhase.Ended, b1); yield return 8;
        }
        static IEnumerator MouseClick(Vector2 p, int button) { MouseEv(p, 0); yield return 3; MouseEv(p, button); yield return 3; MouseEv(p, 0); yield return 8; }
        static IEnumerator PadPress(params GamepadButton[] b) { PadEv(default, default, 0, 0, b); yield return 3; PadEv(default, default, 0, 0); yield return 8; }

        static Vector2 Mid(RectTransform rt) => RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint(rt.rect.center));
        static Vector2 KeyPoint(OpenZHud hud, int i) => Mid(hud.TouchKey(i).Rt);
        static float K => Screen.height / 1080f;

        /// <summary>The block's rectangle on the canvas (from its top-left corner, y down) and the two questions about it.</summary>
        static void KeysBox(OpenZHud hud, out bool inside, out bool clearAll, out bool clearFixed, out string where)
        {
            var c = new Vector3[4]; hud.TouchKeysRect.GetWorldCorners(c);
            inside = c[0].x >= -1f && c[0].y >= -1f && c[2].x <= Screen.width + 1f && c[2].y <= Screen.height + 1f;
            OzHudLayout.CanvasSize(out float cw, out float ch);
            float k = Screen.height / ch;
            var r = new Rect(c[0].x / k, ch - c[2].y / k, (c[2].x - c[0].x) / k, (c[2].y - c[0].y) / k);
            clearAll = clearFixed = true;
            for (int i = 0; i < OzHudLayout.Count; i++)
            {
                if (!OzHudLayout.BoxNow((OzPanel)i).Overlaps(r)) continue;
                clearAll = false;
                var p = (OzPanel)i;
                if (p != OzPanel.Messages && p != OzPanel.Cannons && p != OzPanel.Comms) clearFixed = false;
            }
            where = $"{r.x:0},{r.y:0} {r.width:0}x{r.height:0} u of {cw:0}x{ch:0} u";
        }

        static bool OrderPoint(OpenZHud hud, Vector2 near, out Vector2 p)
        {
            var cam = hud.rts.GetComponent<Camera>();
            var offs = new[] { new Vector2(230, 130), new Vector2(-230, 130), new Vector2(230, -130), new Vector2(-230, -130), new Vector2(330, 0), new Vector2(-330, 0), new Vector2(0, 210), new Vector2(0, -210), new Vector2(430, 200), new Vector2(-430, 200) };
            foreach (var o in offs)
            {
                p = near + o * K;
                if (p.x < 40 || p.y < 40 || p.x > Screen.width - 40 || p.y > Screen.height - 40) continue;
                if (OzTouch.TapIsOrder(p) && hud.game.GroundPoint(cam.ScreenPointToRay(p), out _)) return true;
            }
            p = near; return false;
        }

        static void Shot(string name) => ScreenCapture.CaptureScreenshot(Path.Combine(dir, $"{name}_{Screen.width}x{Screen.height}_s{Mathf.RoundToInt(OzText.UiScale * 100)}.png"));

        // ------------------------------------------------------------------ a level
        static IEnumerator Level(OpenZHud hud)
        {
            var input = hud.input; var W = hud.W; var rts = hud.rts; var game = hud.game;
            if (hud.Modal || W.GameOver || !rts) { Report = Tag + " FAIL: close the menus first (a running match is needed)"; Debug.LogError(Report); yield break; }
            ZObject u = null;
            foreach (var o in W.OwnedSelectable(hud.Player)) { if (o.Kind == ZKind.Robot && !o.IsMinion) { u = o; break; } if (u == null && !o.IsMinion) u = o; }
            if (u == null) { Report = Tag + " FAIL: no own unit in this match"; Debug.LogError(Report); yield break; }
            Devices();
            if (OzSettingTips.MinimalUi) OzSettingTips.MinimalUi = false;
            rts.focus = game.CenterWorld(u); rts.SetHeightImmediate(70f);
            input.Selection.Clear();
            yield return 20;
            System.Func<Vector2> at = () => input.ScreenRect(u).center;

            // ---- T: touch
            yield return Tap(at());
            Check(OzTouch.Now == OzTouch.Driver.Touch && Mouse.current != null && Mouse.current.name == OzTouch.DeviceName, $"T1 the first touch takes the pointer (pointer {OzTouch.Now}, current mouse {(Mouse.current != null ? Mouse.current.name : "none")})");
            Check(input.Selection.Contains(u), $"T2 a tap on an own unit selects it ({input.Selection.Count} selected)");
            KeysBox(hud, out bool inside, out bool clearAll, out _, out string where);
            Check(hud.TouchKeysShown && inside && clearAll && hud.TouchKeysClear, $"T3 the on-screen keys show, inside the screen, clear of all 8 groups (shown {hud.TouchKeysShown}, inside {inside}, clear {clearAll}, at {where})");
            Shot("keys");
            if (!OrderPoint(hud, at(), out var ground)) { Check(false, "T4 no free ground point near the unit for the order checks (move the unit into the open and run again)"); Finish("level " + game.level, OzTouch.Describe()); yield break; }
            float m0 = input.MarkerTime; int orders0 = OzTouch.OrderTaps;
            yield return Tap(ground);
            Check(input.MarkerTime != m0 && input.Selection.Contains(u) && OzTouch.OrderTaps == orders0 + 1 && u.Waypoints.Count > 0, $"T4 a tap on the ground orders and keeps the selection (order {input.MarkerTime != m0}, selected {input.Selection.Count}, waypoints {u.Waypoints.Count})");
            rts.focus = game.CenterWorld(u); yield return 10;
            float all0 = PlayerInput.SelectAllAt;
            { var p = at(); int a = ++touchId, b = ++touchId; TouchEv(a, TouchPhase.Began, p); yield return 3; TouchEv(a, TouchPhase.Ended, p); yield return 4; TouchEv(b, TouchPhase.Began, p); yield return 3; TouchEv(b, TouchPhase.Ended, p); yield return 8; }
            Check(PlayerInput.SelectAllAt > all0, "T5 a double tap selects every unit of that type on screen");
            input.Selection.Clear(); yield return 2;
            { var p = at(); int d0 = OzTouch.Drags; yield return Drag(p + new Vector2(-150, -110) * K, p + new Vector2(150, 110) * K); Check(input.Selection.Contains(u) && OzTouch.Drags == d0 + 1, $"T6 a drag is the selection box ({input.Selection.Count} selected)"); }
            {
                bool min = OzSettingTips.MinimalUi; int h0 = OzTouch.Holds; var kp = KeyPoint(hud, 1); int id = ++touchId;
                TouchEv(id, TouchPhase.Began, kp); yield return 0.8f;
                bool hover = hud.HotButton == hud.TouchKey(1);
                TouchEv(id, TouchPhase.Ended, kp); yield return 8;
                Check(hover && OzSettingTips.MinimalUi == min && OzTouch.Holds == h0 + 1, $"T7 a hold hovers the key and clicks nothing (hover {hover}, holds {OzTouch.Holds - h0})");
                yield return Tap(kp); bool flipped = OzSettingTips.MinimalUi != min;
                yield return 0.4f; bool keysStay = hud.TouchKeysShown;
                yield return Tap(KeyPoint(hud, 1));
                Check(flipped && keysStay && OzSettingTips.MinimalUi == min, $"T8 the UI key flips MINIMAL UI and back, the keys stay (flipped {flipped}, keys stay {keysStay})");
            }
            { int s0 = OzSpeedKeys.Steps + OzSpeedKeys.Stops; yield return Tap(KeyPoint(hud, 7)); bool up = OzSpeedKeys.Steps + OzSpeedKeys.Stops == s0 + 1; yield return Tap(KeyPoint(hud, 6)); Check(up && OzSpeedKeys.Steps + OzSpeedKeys.Stops == s0 + 2, "T9 the SPEED keys step the game speed"); game.gameSpeed = speed0; }
            { rts.SetHeightImmediate(70f); yield return 3; yield return Tap(KeyPoint(hud, 5)); yield return 0.6f; Check(rts.height < 69f, $"T10 ZOOM + zooms in (height {rts.height:0.0} from 70)"); }
            {
                yield return Tap(KeyPoint(hud, 2)); bool shown = hud.TouchHelpShown; if (shown) Shot("help");
                yield return Tap(KeyPoint(hud, 0));
                Check(shown && !hud.TouchHelpShown && !hud.Pause.IsOpen, $"T11 HELP shows the card, BACK closes it and nothing else (shown {shown}, pause menu {hud.Pause.IsOpen})");
                yield return Tap(KeyPoint(hud, 0)); yield return 0.3f; bool open = hud.Pause.IsOpen;
                OzTouch.TapKey(Key.Escape); yield return 0.4f;
                Check(open && !hud.Pause.IsOpen, $"T12 BACK with nothing open is the pause menu, ESC closes it (opened {open}, open now {hud.Pause.IsOpen})");
                yield return 0.4f;
            }
            {
                rts.focus = game.CenterWorld(u); rts.SetHeightImmediate(70f); yield return 10;
                var c = new Vector2(Screen.width * 0.5f, Screen.height * 0.55f); var fx = rts.focus; float mk = input.MarkerTime; int sel = input.Selection.Count;
                yield return Two(c + new Vector2(-70, 0) * K, c + new Vector2(70, 0) * K, c + new Vector2(130, 0) * K, c + new Vector2(270, 0) * K, 5);
                Check(rts.focus.x < fx.x - 0.5f && input.MarkerTime == mk && input.Selection.Count == sel, $"T13 two fingers dragged right: the view goes left, nothing ordered (focus x {fx.x:0.0} -> {rts.focus.x:0.0})");
                rts.SetHeightImmediate(70f); yield return 3;
                yield return Two(c + new Vector2(-60, 0) * K, c + new Vector2(60, 0) * K, c + new Vector2(-220, 0) * K, c + new Vector2(220, 0) * K, 5); yield return 0.6f;
                Check(rts.height < 60f, $"T14 a pinch (fingers apart) zooms in (height {rts.height:0.0} from 70)");
            }
            {
                rts.focus = game.CenterWorld(u); rts.SetHeightImmediate(70f); input.Selection.Clear(); input.Selection.Add(u); yield return 10;
                bool found = OrderPoint(hud, at(), out ground); float mk = input.MarkerTime; int r0 = OzTouch.RightTaps;
                int ia = ++touchId, ib = ++touchId;
                TouchEv(ia, TouchPhase.Began, ground); TouchEv(ib, TouchPhase.Began, ground + new Vector2(90, 0) * K); yield return 4;
                TouchEv(ia, TouchPhase.Ended, ground); TouchEv(ib, TouchPhase.Ended, ground + new Vector2(90, 0) * K); yield return 8;
                Check(found && OzTouch.RightTaps == r0 + 1 && input.MarkerTime != mk, $"T15 a two-finger tap is the right click (order {input.MarkerTime != mk})");
            }
            {
                ZObject fac = null; Vector2 fp = default; bool got = false;
                foreach (var o in W.Objects) if (o.IsProducer && o.Owner == hud.Player && !o.Destroyed) { fac = o; break; }
                if (fac != null)
                {
                    rts.focus = game.CenterWorld(fac); yield return 12;
                    var fr = input.ScreenRect(fac);
                    for (int iy = 1; iy < 6 && !got; iy++) for (int ix = 1; ix < 6 && !got; ix++)
                    {
                        fp = new Vector2(Mathf.Lerp(fr.xMin, fr.xMax, ix / 6f), Mathf.Lerp(fr.yMin, fr.yMax, iy / 6f));
                        got = !hud.IsOverUi(fp) && hud.Production.ProducerUnder(fp) == fac && !OzTouch.TapIsOrder(fp) && FreeOfOwnUnits(hud, fp);
                    }
                }
                int sel = input.Selection.Count;
                if (got) yield return Tap(fp);
                bool open = got && hud.Production.IsOpen && hud.Production.Building == fac;
                Check(open && input.Selection.Count == sel, $"T16 a tap on the own fort or factory opens its build menu, the selection stays (found a point {got}, open {open}, selected {input.Selection.Count} of {sel})");
                bool built = false; string unit = null;
                if (open)
                {
                    yield return 0.4f;
                    foreach (var rt in hud.Production.WindowRect.GetComponentsInChildren<RectTransform>())
                        if (rt.name.StartsWith("Tile_")) { unit = rt.name.Substring(5); yield return Tap(Mid(rt)); break; }
                    built = unit != null && fac.Building && (fac.CurUnit == unit || fac.Queue.Contains(unit));
                }
                Check(built, $"T17 a tap on a build picture starts or queues that unit ({unit ?? "no picture found"}: building {fac != null && fac.Building}, current {(fac != null ? fac.CurUnit : "")}, queue {(fac != null ? fac.Queue.Count : 0)})");
                if (hud.Production.IsOpen) { yield return Tap(KeyPoint(hud, 0)); yield return 0.3f; }
                if (hud.Production.IsOpen) hud.Production.Close();
                if (hud.Pause.IsOpen) hud.Pause.Close();
                yield return 0.3f;
            }

            // ---- M: the mouse takes over again
            yield return 1.0f;
            MouseEv(mousePos + new Vector2(40, 0), 0); yield return 2; MouseEv(mousePos + new Vector2(40, 20), 0); yield return 6;
            Check(OzTouch.Now == OzTouch.Driver.Mouse && Mouse.current == mouse && !hud.TouchKeysShown, $"M1 the mouse takes the pointer back, the keys go (pointer {OzTouch.Now}, current mouse {(Mouse.current != null ? Mouse.current.name : "none")}, keys {hud.TouchKeysShown})");
            rts.focus = game.CenterWorld(u); rts.SetHeightImmediate(70f); input.Selection.Clear(); input.Selection.Add(u); yield return 10;
            OrderPoint(hud, at(), out ground);
            { float mk = input.MarkerTime; yield return MouseClick(ground, 1); Check(input.Selection.Count == 0 && input.MarkerTime == mk, $"M2 a left click on the ground deselects as always, no order ({input.Selection.Count} selected)"); }
            { input.Selection.Clear(); input.Selection.Add(u); yield return 2; float mk = input.MarkerTime; yield return MouseClick(ground, 2); Check(input.MarkerTime != mk && input.Selection.Contains(u), "M3 a right click orders as always"); }

            // ---- G: gamepad
            if (!OzTouch.PadCompiled) Debug.Log(Tag + " the gamepad part is not in the build: its 9 checks are left out");
            else
            {
                // G1. The pointer's place is the one the GAME reads (gameMouseAt). First the stick leaves its rest and the gamepad takes
                // the pointer; then the pointer is put on a known point with room to its right, and the stick is pushed right again
                PadEv(new Vector2(1, 0), default, 0, 0); yield return 6; PadEv(default, default, 0, 0); yield return 4;
                bool took = OzTouch.Now == OzTouch.Driver.Pad && OzTouch.SoftPointer && gameMouse == OzTouch.DeviceName;
                OzTouch.ProofPointer(new Vector2(Mathf.Round(Screen.width * 0.25f), Mathf.Round(Screen.height * 0.5f))); yield return 6;
                float x0 = gameMouseAt.x; bool c0 = OzSoftCursor.Showing; float cx0 = OzSoftCursor.ScreenRect.x;
                PadEv(new Vector2(1, 0), default, 0, 0); yield return 20; PadEv(default, default, 0, 0); yield return 4;
                float x1 = gameMouseAt.x; bool c1 = OzSoftCursor.Showing; float cx1 = OzSoftCursor.ScreenRect.x;
                float editorSide = Mouse.current != null ? Mouse.current.position.ReadValue().x : -1f;                 // what this check read before: the editor's copy of the device
                Check(took && OzTouch.Now == OzTouch.Driver.Pad && gameMouse == OzTouch.DeviceName && x1 > x0 + 20f && (!(c0 && c1) || cx1 > cx0 + 20f),
                      $"G1 the left stick takes the pointer and moves it (pointer {OzTouch.Now}, the game's mouse {gameMouse}; pointer x {x0:0} -> {x1:0}; the cursor drawn on screen, left edge x {(c0 ? cx0.ToString("0") : "not drawn")} -> {(c1 ? cx1.ToString("0") : "not drawn")}; the same pointer read from the editor's side {editorSide:0})");
                rts.focus = game.CenterWorld(u); input.Selection.Clear(); yield return 10;
                OzTouch.ProofPointer(at()); yield return 3; yield return PadPress(GamepadButton.South);
                Check(input.Selection.Contains(u), $"G2 A on the unit selects it ({input.Selection.Count} selected)");
                OrderPoint(hud, at(), out ground);
                { float mk = input.MarkerTime; OzTouch.ProofPointer(ground); yield return 3; yield return PadPress(GamepadButton.West); Check(input.MarkerTime != mk && input.Selection.Contains(u), "G3 X on the ground orders"); }
                { var fx = rts.focus; PadEv(default, new Vector2(1, 0), 0, 0); yield return 20; PadEv(default, default, 0, 0); yield return 4; Check(rts.focus.x > fx.x + 0.5f, $"G4 the right stick scrolls the view (focus x {fx.x:0.0} -> {rts.focus.x:0.0})"); }
                { rts.SetHeightImmediate(70f); yield return 3; PadEv(default, default, 0, 1f); yield return 25; PadEv(default, default, 0, 0); yield return 0.5f; Check(rts.height < 65f, $"G5 the right trigger zooms in (height {rts.height:0.0} from 70)"); }
                { yield return PadPress(GamepadButton.Start); yield return 0.3f; bool open = hud.Pause.IsOpen; yield return PadPress(GamepadButton.East); yield return 0.4f; Check(open && !hud.Pause.IsOpen, $"G6 Start opens the pause menu, B closes it (opened {open}, open now {hud.Pause.IsOpen})"); if (hud.Pause.IsOpen) hud.Pause.Close(); yield return 0.4f; }
                {
                    input.Selection.Clear(); input.Selection.Add(u); yield return 2;
                    PadEv(default, default, 0, 0, GamepadButton.LeftShoulder); yield return 4; PadEv(default, default, 0, 0, GamepadButton.LeftShoulder, GamepadButton.DpadUp); yield return 4; PadEv(default, default, 0, 0, GamepadButton.LeftShoulder); yield return 4; PadEv(default, default, 0, 0); yield return 6;
                    var groups = hud.Bridge != null ? hud.Bridge.Groups : null;
                    bool stored = groups != null && groups.TryGetValue(1, out var g1) && g1.Contains(u);
                    Check(stored, "G7 LB + D-pad up stores the selection as group 1");
                    input.Selection.Clear(); yield return 2; yield return PadPress(GamepadButton.DpadUp);
                    Check(input.Selection.Contains(u), "G8 D-pad up recalls group 1");
                    yield return PadPress(GamepadButton.East);
                    Check(input.Selection.Count == 0 && !hud.Pause.IsOpen, $"G9 B with nothing open clears the selection ({input.Selection.Count} selected, pause menu {hud.Pause.IsOpen})");
                }
            }

            // ---- O: TOUCH ON with only a mouse
            OzTouch.Setting = OzTouch.Mode.On;
            yield return 1.0f;
            MouseEv(mousePos + new Vector2(-40, 0), 0); yield return 2; MouseEv(mousePos + new Vector2(-40, -20), 0); yield return 6;
            Check(OzTouch.Now == OzTouch.Driver.Touch && OzTouch.MouseFinger && hud.TouchKeysShown, $"O1 TOUCH ON: the mouse is read as the finger, the keys show (pointer {OzTouch.Now}, mouse finger {OzTouch.MouseFinger}, keys {hud.TouchKeysShown})");
            rts.focus = game.CenterWorld(u); rts.SetHeightImmediate(70f); input.Selection.Clear(); input.Selection.Add(u); yield return 10;
            OrderPoint(hud, at(), out ground);
            { float mk = input.MarkerTime; yield return MouseClick(ground, 1); Check(input.MarkerTime != mk && input.Selection.Contains(u), $"O2 TOUCH ON: a left click on the ground orders and keeps the selection (order {input.MarkerTime != mk}, selected {input.Selection.Count})"); }
            {
                rts.focus = game.CenterWorld(u); input.Selection.Clear(); yield return 10;
                var p = at(); Vector2 a = p + new Vector2(-150, -110) * K, b = p + new Vector2(150, 110) * K;
                MouseEv(a, 0); yield return 3; MouseEv(a, 1); yield return 3; for (int i = 1; i <= 4; i++) { MouseEv(Vector2.Lerp(a, b, i / 4f), 1); yield return 2; } MouseEv(b, 0); yield return 8;
                Check(input.Selection.Contains(u), $"O3 TOUCH ON: a left drag is still the selection box ({input.Selection.Count} selected)");
            }
            { OrderPoint(hud, at(), out ground); float mk = input.MarkerTime; yield return MouseClick(ground, 2); Check(input.MarkerTime != mk, "O4 TOUCH ON: the right button still orders"); }
            { var p = at(); input.Selection.Clear(); yield return 2; yield return MouseClick(p, 1); Check(input.Selection.Contains(u), "O5 TOUCH ON: a left click on an own unit still selects it"); }

            // ---- L: the keys at interface scale 160 %
            OzText.UiScale = 1.6f; hud.ApplyUiScale(); yield return 12;
            KeysBox(hud, out inside, out clearAll, out bool clearFixed, out where);
            Check(hud.TouchKeysShown, "L1 the keys show at interface scale 160 %");
            Check(inside, $"L2 at 160 % the keys are inside the screen (at {where})");
            Check(clearFixed, $"L3 at 160 % the keys are clear of the always-on panels (clear of all 8 groups: {clearAll}, at {where})");
            Shot("keys160");
            yield return 4;

            // ---- X: OFF
            OzTouch.Setting = OzTouch.Mode.Off;
            yield return 1.0f;
            MouseEv(mousePos + new Vector2(40, 0), 0); yield return 2; MouseEv(mousePos + new Vector2(40, 20), 0); yield return 6;
            rts.focus = game.CenterWorld(u); input.Selection.Clear(); yield return 10;
            yield return Tap(at());
            Check(OzTouch.Now == OzTouch.Driver.Mouse && input.Selection.Count == 0 && !hud.TouchKeysShown && Mouse.current == mouse, $"X1 OFF: the mouse has the pointer, a touch does nothing (pointer {OzTouch.Now}, {input.Selection.Count} selected, keys {hud.TouchKeysShown})");
            string detail = $"{OzTouch.Describe()}; keys at 160 %: {where}, clear of all groups {clearAll}";
            Finish("level " + game.level, detail);
        }

        static bool FreeOfOwnUnits(OpenZHud hud, Vector2 p)
        {
            foreach (var o in hud.W.Objects)
                if (o.Owner == hud.Player && !o.Destroyed && o.KillAt < 0 && (o.Kind == ZKind.Robot || o.Kind == ZKind.Vehicle || o.Kind == ZKind.Cannon) && hud.input.ScreenRect(o).Contains(p)) return false;
            return true;
        }

        // ------------------------------------------------------------------ the main menu
        static string ViewOf(OpenZMainMenu menu)
        {
            var f = typeof(OpenZMainMenu).GetField("view", BindingFlags.NonPublic | BindingFlags.Instance);
            return f != null ? f.GetValue(menu).ToString() : "?";
        }

        static IEnumerator Menu(OpenZMainMenu menu)
        {
            Devices();
            yield return 10;
            menu.ProofOpen("options"); yield return 0.5f;
            yield return Tap(menu.ProofOptTabPoint(OzSettingTips.TabAdvanced));
            Check(menu.ProofOptTabOpen == OzSettingTips.TabAdvanced && OzTouch.Now == OzTouch.Driver.Touch, $"N1 a touch tap opens the ADVANCED tab of OPTIONS (tab {menu.ProofOptTabOpen}, pointer {OzTouch.Now})");
            bool row = menu.ProofArrowPoints(OzTouch.Label, out var left, out var right);
            if (row) { yield return 4; row = menu.ProofArrowPoints(OzTouch.Label, out left, out right); }
            var s0 = OzTouch.Setting;
            if (row) yield return Tap(right);
            bool stepped = OzTouch.Setting != s0;
            if (row) { yield return 1.0f; menu.ProofArrowPoints(OzTouch.Label, out left, out right); yield return Tap(left); }   // the value is wider now: its arrows moved
            Check(row && stepped && OzTouch.Setting == s0, $"N2 taps on the row's arrows step {OzTouch.Label} and back (row {row}, stepped {stepped}, now {OzTouch.ModeName})");
            Shot("menu_options");
            OzTouch.Setting = OzTouch.Mode.Auto;
            menu.ProofOpenChanges(); yield return 0.6f;
            {
                string before = menu.ProofChangesInfo; var c = new Vector2(Screen.width * 0.6f, Screen.height * 0.5f);
                yield return Two(c + new Vector2(-60, 120) * K, c + new Vector2(60, 120) * K, c + new Vector2(-60, -220) * K, c + new Vector2(60, -220) * K, 8); yield return 0.6f;
                string after = menu.ProofChangesInfo;
                Check(menu.ProofChangesMax <= 0f || before != after, $"N3 two fingers dragged over CHANGES scroll it ({(menu.ProofChangesMax <= 0f ? "the list fits, nothing to scroll" : "moved")})");
            }
            if (!OzTouch.PadCompiled) Debug.Log(Tag + " the gamepad part is not in the build: its 3 checks are left out");
            else
            {
                menu.ProofMain(); yield return 0.6f;
                OzTouch.TapKey(Key.Escape); yield return 0.5f;                                             // an open side panel closes
                yield return 1.0f;
                int f0 = menu.ProofFocusItem;
                yield return PadPress(GamepadButton.DpadDown);
                bool moved = menu.ProofFocusItem != f0;
                yield return PadPress(GamepadButton.DpadUp);
                Check(moved && menu.ProofFocusItem == f0 && OzTouch.Now == OzTouch.Driver.Pad, $"N4 the D-pad moves the menu's focus (moved {moved}, pointer {OzTouch.Now})");
                bool play = menu.ProofFocusItem == 0;
                if (!play) { for (int i = 0; i < 12 && menu.ProofFocusItem != 0; i++) yield return PadPress(GamepadButton.DpadUp); play = menu.ProofFocusItem == 0; }
                yield return PadPress(GamepadButton.South); yield return 0.8f;
                string v = ViewOf(menu);
                Check(play && v == "Select", $"N5 A after the D-pad is ENTER: the level selection opens (view {v})");
                yield return PadPress(GamepadButton.East); yield return 0.8f;
                Check(ViewOf(menu) == "Main", $"N6 B goes back to the main menu (view {ViewOf(menu)})");
            }
            Finish("main menu", OzTouch.Describe());
        }
    }
}
