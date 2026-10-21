using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Version 1.1 kit KT, the counted Play check of the option TRACKPAD SCROLL (OzTrackpadScroll.cs).
    /// Menu OpenZ/V1.1/KT Trackpad Scroll: Play Check (this level): in Play on a level, after the level-start briefing, about
    /// 40 s of real time, leave mouse and keyboard alone.
    /// <list type="bullet">
    /// <item>A. The row: ADVANCED tab right under TOUCH · GAMEPAD, its tip, default ZOOM; the pause menu's OPTIONS on ADVANCED with
    /// ZOOM and with MOVE MAP (two pictures).</item>
    /// <item>B. The rules, through OzTrackpadScroll.Frame / KeyFrame as the camera calls them (simulated scroll): MOVE MAP pans
    /// north, west, by the expected metres, smooth (a glide over several frames), scaled with the zoom (x4 at 4x the height);
    /// Ctrl + scroll zooms one wheel step per notch and does not pan; Cmd + scroll zooms on a Mac and pans elsewhere; nothing
    /// while the pause menu is in front; ZOOM hands the wheel through untouched; the zoom keys: one step a press, on while held;
    /// the glide's tail does not end a fly-to.</item>
    /// <item>C. The real input path (counted only when the Game view has focus: the camera reads the mouse only then): a check
    /// mouse device sends scroll events, keyboard states hold Ctrl and + / -: MOVE MAP pans and Ctrl + scroll zooms through
    /// RtsCamera's own frame, Ctrl + = zooms and leaves the speed alone, = alone changes the game speed and not the zoom, ZOOM zooms
    /// one wheel step as before, nothing with the pause menu open.</item>
    /// </list>
    /// Everything is put back (the setting, the camera, the speed, the pause, the devices). Report and pictures:
    /// Menu OpenZ/V1.1/KT Trackpad Scroll: Main Menu Options Pictures: in Play on the main menu, opens OPTIONS on ADVANCED and takes
    /// the row with ZOOM and with MOVE MAP.
    /// </summary>
    public static class OzKtTrackpadCheck
    {
        const string Tag = "[OpenZ KT]";
        public static string Report = "not run";
        static IEnumerator run; static float waitReal; static int waitFrame; static float realLimit;
        static readonly System.Collections.Generic.Stack<IEnumerator> stack = new System.Collections.Generic.Stack<IEnumerator>();
        static readonly StringBuilder text = new StringBuilder();
        static int ok, bad;
        static string dir, level;
        static bool kept, paused0, hadSet0, hadSpeed0; static int set0; static Vector3 focus0; static float h0, speed0, edge0, speedPref0; static Transform follow0;
        static Mouse proofMouse, sysMouse; static bool keysSent;
        static readonly FieldInfo FlyingField = typeof(RtsCamera).GetField("flying", BindingFlags.NonPublic | BindingFlags.Instance);
        static readonly FieldInfo SettingField = typeof(OzTrackpadScroll).GetField("setting", BindingFlags.NonPublic | BindingFlags.Static);

        // ------------------------------------------------------------------ menus
        [MenuItem("OpenZ/V1.1/KT Trackpad Scroll: Play Check (this level)")]
        static void Run()
        {
            var g = ZGame.I; var hud = OpenZHud.I;
            if (!EditorApplication.isPlaying || !g || g.World == null || !hud || !hud.rts) { Debug.LogWarning(Tag + " enter Play on a level first"); return; }
            if (run != null) { Debug.LogWarning(Tag + " a run is still going"); return; }
            if (ZodFlyover.Live != null && ZodFlyover.Live.Active) { Debug.LogWarning(Tag + " the level-start briefing is still flying: run it when the camera is yours"); return; }
            if (RtsCamera.InputLocked) { Debug.LogWarning(Tag + " the ending holds the camera: run it in a running match"); return; }
            Start(Body(), g.level);
        }

        [MenuItem("OpenZ/V1.1/KT Trackpad Scroll: Main Menu Options Pictures")]
        static void RunMenu()
        {
            var mm = UnityEngine.Object.FindFirstObjectByType<OpenZMainMenu>();
            if (!EditorApplication.isPlaying || !mm) { Debug.LogWarning(Tag + " enter Play on the main menu first"); return; }
            if (run != null) { Debug.LogWarning(Tag + " a run is still going"); return; }
            Start(MenuBody(mm), "main_menu");
        }

        static void Start(IEnumerator body, string lvl)
        {
            ok = bad = 0; text.Clear(); Report = "running"; level = lvl; dir = OutDir();
            run = body; stack.Clear(); stack.Push(body); waitReal = 0f; waitFrame = 0; realLimit = Time.realtimeSinceStartup + 300f;
            EditorApplication.update -= Step; EditorApplication.update += Step;
            Debug.Log(Tag + " running on " + level + " (leave mouse and keyboard alone). Report and pictures -> " + dir.Replace('\\', '/'));
        }

        static void Step()
        {
            if (run == null) { EditorApplication.update -= Step; return; }
            if (!EditorApplication.isPlaying || Time.realtimeSinceStartup > realLimit)
            {
                EditorApplication.update -= Step; run = null;
                if (EditorApplication.isPlaying) { Line(false, "the run took longer than 5 minutes of real time"); Finish(); } else { PutBack(); Report = Tag + " stopped: Play was left"; }
                return;
            }
            if (Time.realtimeSinceStartup < waitReal || Time.frameCount < waitFrame) return;
            while (true)
            {
                var top = stack.Peek(); bool more;
                try { more = top.MoveNext(); }
                catch (Exception ex) { Debug.LogException(ex); EditorApplication.update -= Step; run = null; stack.Clear(); Line(false, "exception: " + ex.Message); Finish(); return; }
                if (!more) { stack.Pop(); if (stack.Count == 0) { EditorApplication.update -= Step; run = null; return; } continue; }   // a part ended: its caller goes on at once
                object cur = top.Current;
                if (cur is IEnumerator part) { stack.Push(part); continue; }                                 // a part (Settle) runs inside the step
                waitReal = cur is float s ? Time.realtimeSinceStartup + s : 0f;                               // a float = seconds of real time (the pause menu stops the game clock)
                waitFrame = cur is int n ? Time.frameCount + n : Time.frameCount + 1;                        // an int = player frames; anything else = the next frame
                return;
            }
        }

        static string OutDir()
        {
            string all = Path.Combine(OpenZPaths.RepoRoot, "work", "records", "milestones"), stage = "v1_1";
            if (Directory.Exists(all))
            {
                var ds = Directory.GetDirectories(all, "v1_1*");
                Array.Sort(ds, StringComparer.Ordinal);
                if (ds.Length > 0) stage = Path.GetFileName(ds[ds.Length - 1]);
            }
            string d = Path.Combine(all, stage, "for_owner", "trackpad_scroll");
            Directory.CreateDirectory(d);
            return d;
        }

        static void Line(bool pass, string what) { if (pass) ok++; else bad++; text.Append(pass ? "PASS  " : "FAIL  ").Append(what).Append('\n'); }
        static void Note(string what) { text.Append("      ").Append(what).Append('\n'); }
        static void Shot(string name)
        {
            string p = Path.Combine(dir, "kt_" + level + "_" + name + ".png");
            ScreenCapture.CaptureScreenshot(p);
            Note("picture " + p.Replace('\\', '/') + " (" + Screen.width + "x" + Screen.height + ", setting " + OzTrackpadScroll.Name + ")");
        }
        static bool Near(float got, float want, float rel) => Mathf.Abs(got - want) <= Mathf.Abs(want) * rel + 1e-4f;

        // ------------------------------------------------------------------ helpers
        static RtsCamera Cam => OpenZHud.I ? OpenZHud.I.rts : null;
        /// <summary>The camera reads the wheel itself this frame (and so advances the glide): the window has focus.</summary>
        static bool CamCalls => Mouse.current != null && Application.isFocused;
        static bool Flying(RtsCamera c) => FlyingField != null && c && (bool)FlyingField.GetValue(c);
        static float Sin(RtsCamera c) => Mathf.Max(0.3f, Mathf.Sin(c.pitch * Mathf.Deg2Rad));
        /// <summary>Metres the focus moves for one pixel of drag, sideways (as RtsCamera.DragPixels measures it).</summary>
        static float MPerPx(RtsCamera c) => 2f * c.height * Mathf.Tan(16f * Mathf.Deg2Rad) / Mathf.Max(1, Screen.height) / Sin(c);

        static void Pose(RtsCamera c, Vector3 at, float h) { c.follow = null; c.focus = at; c.SetHeightImmediate(h); }
        static Vector3 Centre(RtsCamera c) => new Vector3(c.mapSizeMeters.x * 0.5f, 0f, -c.mapSizeMeters.y * 0.5f);

        /// <summary>One frame of simulated scroll through the option's frame, as the camera calls it.</summary>
        static void Feed(RtsCamera c, Vector2 s, bool ctrl = false, bool cmd = false) { OzTrackpadScroll.Frame(c, s, ctrl, cmd, Flying(c), Time.unscaledDeltaTime); }

        /// <summary>Waits until the glide is done and the zoom has eased (at most 2.5 s); counts the frames that moved the focus and
        /// the largest step. While the camera does not read the wheel itself (no focus), the frames are given here.</summary>
        static int movedFrames; static float maxStep, totalMove;
        static IEnumerator Settle(RtsCamera c, Vector3 from)
        {
            movedFrames = 0; maxStep = 0f; totalMove = 0f;
            float t0 = Time.realtimeSinceStartup; Vector3 last = from; float lastH = c.height; int still = 0;   // from = the focus before the scroll: its first step counts too
            while (Time.realtimeSinceStartup - t0 < 2.5f)
            {
                if (!CamCalls) Feed(c, Vector2.zero);
                yield return 1;
                float step = (c.focus - last).magnitude; last = c.focus;
                if (step > 1e-4f) { movedFrames++; maxStep = Mathf.Max(maxStep, step); totalMove += step; }
                bool calm = step <= 1e-4f && Mathf.Abs(c.height - lastH) < 1e-3f && OzTrackpadScroll.Pending <= 0f; lastH = c.height;
                still = calm ? still + 1 : 0;
                if (still >= 8 && Time.realtimeSinceStartup - t0 > 0.6f) break;
            }
        }

        static void MouseEv(Vector2 p, Vector2 scroll) { if (proofMouse != null && proofMouse.added) InputSystem.QueueStateEvent(proofMouse, new MouseState { position = p, scroll = scroll }); }
        static void KeyEv(params Key[] keys) { var kb = Keyboard.current; if (kb == null) return; InputSystem.QueueStateEvent(kb, new KeyboardState(keys)); keysSent = true; }

        // ------------------------------------------------------------------ the level run
        static IEnumerator Body()
        {
            var g = ZGame.I; var hud = OpenZHud.I; var c = Cam; var pm = hud.Pause;
            set0 = OzTrackpadScroll.Setting; focus0 = c.focus; h0 = c.height; follow0 = c.follow; speed0 = g.gameSpeed; paused0 = g.paused; edge0 = c.edgePx; kept = true;
            hadSet0 = PlayerPrefs.HasKey(OzTrackpadScroll.Pref); hadSpeed0 = PlayerPrefs.HasKey(OzSpeedKeys.Pref); speedPref0 = PlayerPrefs.GetFloat(OzSpeedKeys.Pref, 1f);   // the player's saved choices come back as they were
            c.edgePx = 0f;                                                                                   // the real pointer resting at a window edge must not scroll the view meanwhile
            if (pm && pm.IsOpen) { pm.Close(); yield return 0.5f; }
            g.paused = false;
            string L = OzTrackpadScroll.Label;
            Note($"{level}: camera height {h0:0.#} m, pitch {c.pitch:0}, wheel step {c.wheelStep:0.##}, screen {Screen.width}x{Screen.height}, the Game view {(Application.isFocused ? "has" : "has NO")} focus; setting was {OzTrackpadScroll.Names[set0]}");

            // ---- A. the row
            Line(OzSettingTips.TabOf(L) == OzSettingTips.TabAdvanced && OzSettingTips.TabOrder(L) == OzSettingTips.TabOrder(OzTouch.Label) + 1,
                $"the row {L} is on {OzSettingTips.TabNames[OzSettingTips.TabOf(L)]} at place {OzSettingTips.TabOrder(L)}, right under {OzTouch.Label} (place {OzSettingTips.TabOrder(OzTouch.Label)}), one table for both menus");
            Line(OzSettingTips.For(L) == OzTrackpadScroll.Tip, "its tip: " + OzTrackpadScroll.Tip.Replace('|', ' ').Trim());
            bool had = PlayerPrefs.HasKey(OzTrackpadScroll.Pref); int saved = PlayerPrefs.GetInt(OzTrackpadScroll.Pref, 0);
            PlayerPrefs.DeleteKey(OzTrackpadScroll.Pref); SettingField?.SetValue(null, -1);
            int fresh = OzTrackpadScroll.Setting;
            if (had) PlayerPrefs.SetInt(OzTrackpadScroll.Pref, saved); SettingField?.SetValue(null, -1);
            Line(fresh == OzTrackpadMath.Zoom && SettingField != null, $"a player who never touched it gets {OzTrackpadScroll.Names[fresh]} (saved key {OzTrackpadScroll.Pref} absent)");
            if (pm)
            {
                OzTrackpadScroll.Setting = OzTrackpadMath.Zoom;
                pm.OpenOptions(OzSettingTips.TabAdvanced); yield return 0.8f;
                string rows = pm.ProofOptTabRows;
                Line(pm.IsOpen && pm.ProofOptTabOpen == OzSettingTips.TabAdvanced && rows.Contains(L), "pause menu OPTIONS, ADVANCED shows: " + rows);
                Shot("pause_advanced_zoom");
                yield return 0.3f;
                OzTrackpadScroll.Step(1); yield return 0.5f;
                Line(OzTrackpadScroll.Setting == OzTrackpadMath.MoveMap, "the row's step turns it to " + OzTrackpadScroll.Name);
                Shot("pause_advanced_move_map");
                yield return 0.3f;
                pm.Close(); yield return 0.6f;
                g.paused = false;
            }
            else Line(false, "no pause menu in this scene");

            // ---- B. the rules, simulated scroll through OzTrackpadScroll.Frame
            OzTrackpadScroll.Setting = OzTrackpadMath.MoveMap;
            Vector3 mid = Centre(c);
            float H = Mathf.Max(1, Screen.height), ppn = OzTrackpadScroll.PixelsPerNotch * H;

            Pose(c, mid, 70f); yield return 4;
            Vector3 f0 = c.focus; float hA = c.height; float want = ppn * MPerPx(c) / Sin(c);
            Feed(c, new Vector2(0f, 1f)); yield return Settle(c, f0);
            Vector3 d = c.focus - f0;
            Line(Near(d.z, want, 0.03f) && Mathf.Abs(d.x) < 0.02f && Mathf.Abs(c.height - hA) < 0.01f,
                $"MOVE MAP, one notch of scroll up: the view goes north {d.z:0.00} m (expected {want:0.00}), sideways {d.x:0.000} m, height {hA:0.0} -> {c.height:0.0} m");
            Line(movedFrames >= 2 && maxStep < 0.75f * Mathf.Max(1e-3f, totalMove), $"smooth: the pan took {movedFrames} frames, the largest step {maxStep:0.00} of {totalMove:0.00} m");

            Pose(c, mid, 70f); yield return 4;
            f0 = c.focus; want = ppn * MPerPx(c);
            Feed(c, new Vector2(1f, 0f)); yield return Settle(c, f0);
            d = c.focus - f0;
            Line(Near(-d.x, want, 0.03f) && Mathf.Abs(d.z) < 0.02f, $"MOVE MAP, one notch of scroll to the right: the ground goes right, the view west {-d.x:0.00} m (expected {want:0.00}), north {d.z:0.000} m");

            float[] hs = { 35f, 140f }; float[] moved = new float[2];
            for (int i = 0; i < 2; i++)
            {
                Pose(c, mid, hs[i]); yield return 4;
                f0 = c.focus; Feed(c, new Vector2(0f, 1f)); yield return Settle(c, f0);
                moved[i] = (c.focus - f0).z;
            }
            Line(moved[0] > 0f && Near(moved[1] / moved[0], hs[1] / hs[0], 0.03f), $"scaled with the zoom: one notch moves {moved[0]:0.00} m at {hs[0]:0} m and {moved[1]:0.00} m at {hs[1]:0} m (x{moved[1] / Mathf.Max(1e-4f, moved[0]):0.00}, the height x{hs[1] / hs[0]:0})");

            Pose(c, mid, 70f); yield return 4;
            f0 = c.focus; hA = c.height;
            Feed(c, new Vector2(0f, 1f), ctrl: true); yield return Settle(c, f0);
            float hWant = hA * (1f - c.wheelStep);
            Line(Near(c.height, hWant, 0.01f) && (c.focus - f0).magnitude < 0.02f, $"MOVE MAP, Ctrl + one notch: zoom {hA:0.0} -> {c.height:0.0} m (expected {hWant:0.0}, one wheel step), the view moved {(c.focus - f0).magnitude:0.000} m");

            Pose(c, mid, 70f); yield return 4;
            f0 = c.focus; hA = c.height;
            Feed(c, new Vector2(0f, 1f), cmd: true); yield return Settle(c, f0);
            if (OzTrackpadScroll.IsMac) Line(Near(c.height, hA * (1f - c.wheelStep), 0.01f), $"Mac: Cmd + one notch zooms {hA:0.0} -> {c.height:0.0} m");
            else Line(c.focus.z - f0.z > 0.5f && Mathf.Abs(c.height - hA) < 0.01f, $"not a Mac: Cmd (the Windows key) + scroll is a plain scroll: it panned {(c.focus - f0).z:0.00} m, no zoom (Ctrl zooms here)");

            if (pm)
            {
                Pose(c, mid, 70f); yield return 4;
                pm.Open(); yield return 0.6f;
                f0 = c.focus; hA = c.height; int m0 = OzTrackpadScroll.MenuFrames;
                Feed(c, new Vector2(0.5f, 1f)); Feed(c, new Vector2(0f, 1f), ctrl: true); yield return Settle(c, f0);
                Line(pm.IsOpen && (c.focus - f0).magnitude < 0.01f && Mathf.Abs(c.height - hA) < 0.01f && OzTrackpadScroll.MenuFrames == m0 + 2,
                    $"the pause menu in front: the camera did nothing (moved {(c.focus - f0).magnitude:0.000} m, height {hA:0.0} -> {c.height:0.0}, frames left to the menu {OzTrackpadScroll.MenuFrames - m0})");
                pm.Close(); yield return 0.6f; g.paused = false;
            }

            OzTrackpadScroll.Setting = OzTrackpadMath.Zoom;
            Pose(c, mid, 70f); yield return 4;
            f0 = c.focus; hA = c.height;
            float back = OzTrackpadScroll.Frame(c, new Vector2(0.37f, 1f), false, false, false, Time.unscaledDeltaTime);
            float back2 = OzTrackpadScroll.Frame(c, new Vector2(0f, -0.004f), true, true, false, Time.unscaledDeltaTime);
            yield return Settle(c, f0);
            Line(back == 1f && back2 == -0.004f && (c.focus - f0).magnitude < 0.01f && Mathf.Abs(c.height - hA) < 0.01f,
                $"ZOOM (default): the wheel goes to the camera's own zoom code untouched (1 -> {back}, -0.004 -> {back2}), nothing panned or zoomed by the option");

            // the zoom keys (every setting): a press = one wheel step, held = on in steps
            Pose(c, mid, 70f); yield return 4;
            hA = c.height; float dt = 1f / 60f;
            OzTrackpadScroll.KeyFrame(c, 1, true, dt); OzTrackpadScroll.KeyFrame(c, 0, false, dt); yield return Settle(c, f0);
            float hIn = c.height;
            OzTrackpadScroll.KeyFrame(c, -1, true, dt); OzTrackpadScroll.KeyFrame(c, 0, false, dt); yield return Settle(c, f0);
            Line(Near(hIn, hA * (1f - c.wheelStep), 0.01f) && Near(c.height, hA, 0.01f), $"the zoom keys: + once {hA:0.0} -> {hIn:0.0} m, - once -> {c.height:0.0} m");
            hA = c.height;
            OzTrackpadScroll.KeyFrame(c, 1, true, dt);
            for (int i = 0; i < 60; i++) OzTrackpadScroll.KeyFrame(c, 1, false, dt);                    // one second held at 60 frames a second
            OzTrackpadScroll.KeyFrame(c, 0, false, dt); yield return Settle(c, f0);
            float notches = Mathf.Log(c.height / hA) / Mathf.Log(1f - c.wheelStep), wantN = 1f + OzTrackpadScroll.KeyNotchesPerSecond * (1f - OzTrackpadScroll.KeyRepeatDelay);
            Line(Near(notches, wantN, 0.06f), $"the zoom key held one second: {notches:0.00} wheel steps (expected {wantN:0.00}: one at the press, {OzTrackpadScroll.KeyNotchesPerSecond:0} a second after {OzTrackpadScroll.KeyRepeatDelay:0.0} s)");

            // the glide's tail does not end a fly-to
            OzTrackpadScroll.Setting = OzTrackpadMath.MoveMap;
            Pose(c, mid, 70f); yield return 4;
            Vector3 target = mid + new Vector3(18f, 0f, 12f);
            Feed(c, new Vector2(0f, 2f)); yield return 1;
            c.FlyTo(target);
            float tf = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - tf < 2f) { if (!CamCalls) Feed(c, Vector2.zero); yield return 1; }
            Line((new Vector3(c.focus.x, 0f, c.focus.z) - target).magnitude < 0.5f, $"a fly-to started during the glide arrives: {(new Vector3(c.focus.x, 0f, c.focus.z) - target).magnitude:0.00} m off the target");

            // ---- C. the real input path (the camera's own frame)
            if (!Application.isFocused) { EditorApplication.ExecuteMenuItem("Window/General/Game"); yield return 0.6f; }
            if (!Application.isFocused || Mouse.current == null)
                Note("C not run: the Game view has no focus (the camera reads the mouse only with focus): click into the Game view and run the check again to count C");
            else
            {
                sysMouse = Mouse.current;
                proofMouse = InputSystem.AddDevice<Mouse>("OzKtProofMouse");
                if (sysMouse != null && sysMouse != proofMouse) InputSystem.DisableDevice(sysMouse);
                proofMouse.MakeCurrent();
                Vector2 at = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
                MouseEv(at, Vector2.zero); yield return 3;

                OzTrackpadScroll.Setting = OzTrackpadMath.MoveMap;
                Pose(c, mid, 70f); yield return 4;
                f0 = c.focus; hA = c.height; want = ppn * MPerPx(c) / Sin(c);
                MouseEv(at, new Vector2(0f, 1f)); yield return Settle(c, f0);
                d = c.focus - f0;
                Line(Near(d.z, want, 0.05f) && Mathf.Abs(c.height - hA) < 0.01f, $"real input, MOVE MAP: a scroll event of one notch up moved the view north {d.z:0.00} m (expected {want:0.00}), height {hA:0.0} -> {c.height:0.0} m");

                Pose(c, mid, 70f); yield return 4;
                f0 = c.focus;
                MouseEv(at, new Vector2(-1f, 0f)); yield return Settle(c, f0);
                d = c.focus - f0;
                Line(d.x > 0.5f && Mathf.Abs(d.z) < 0.05f, $"real input, MOVE MAP: one notch to the left moved the view east {d.x:0.00} m");

                Pose(c, mid, 70f); yield return 4;
                f0 = c.focus; hA = c.height;
                KeyEv(Key.LeftCtrl); yield return 3;
                MouseEv(at, new Vector2(0f, 1f)); yield return 2;
                KeyEv(); yield return Settle(c, f0);
                Line(Near(c.height, hA * (1f - c.wheelStep), 0.015f) && (c.focus - f0).magnitude < 0.05f, $"real input, MOVE MAP: Ctrl + one notch zoomed {hA:0.0} -> {c.height:0.0} m and did not pan ({(c.focus - f0).magnitude:0.000} m)");

                Pose(c, mid, 70f); yield return 4;
                g.gameSpeed = 1f; yield return 2;
                hA = c.height; float sp = g.gameSpeed;
                KeyEv(Key.LeftCtrl); yield return 3; KeyEv(Key.LeftCtrl, Key.Equals); yield return 3; KeyEv(Key.LeftCtrl); yield return 3; KeyEv(); yield return Settle(c, f0);
                Line(Near(c.height, hA * (1f - c.wheelStep), 0.015f) && Mathf.Approximately(g.gameSpeed, sp), $"real input: Ctrl + = zoomed {hA:0.0} -> {c.height:0.0} m, the game speed stayed x{g.gameSpeed:0.##}");
                hA = c.height;
                KeyEv(Key.Equals); yield return 3; KeyEv(); yield return Settle(c, f0);
                Line(g.gameSpeed > sp + 0.01f && Mathf.Abs(c.height - hA) < 0.01f, $"real input: = alone changed the game speed x{sp:0.##} -> x{g.gameSpeed:0.##} and not the zoom ({hA:0.0} -> {c.height:0.0} m)");
                g.gameSpeed = speed0;

                OzTrackpadScroll.Setting = OzTrackpadMath.Zoom;
                Pose(c, mid, 70f); yield return 4;
                f0 = c.focus; hA = c.height;
                MouseEv(at, new Vector2(0f, 1f)); yield return Settle(c, f0);
                Line(Near(c.height, hA * (1f - c.wheelStep), 0.015f) && (c.focus - f0).magnitude < 0.05f, $"real input, ZOOM (default): one notch zoomed {hA:0.0} -> {c.height:0.0} m as before, no pan");

                if (pm)
                {
                    OzTrackpadScroll.Setting = OzTrackpadMath.MoveMap;
                    Pose(c, mid, 70f); yield return 4;
                    pm.Open(); yield return 0.6f;
                    f0 = c.focus;
                    MouseEv(at, new Vector2(0f, 1f)); yield return Settle(c, f0);
                    Line((c.focus - f0).magnitude < 0.01f, $"real input, MOVE MAP, the pause menu open: the view stayed ({(c.focus - f0).magnitude:0.000} m)");
                    pm.Close(); yield return 0.6f; g.paused = false;
                }
                PutBackDevices();
            }
            Note(OzTrackpadScroll.Describe());
            Finish();
        }

        // ------------------------------------------------------------------ the main menu pictures
        static IEnumerator MenuBody(OpenZMainMenu mm)
        {
            set0 = OzTrackpadScroll.Setting; kept = false;
            string L = OzTrackpadScroll.Label;
            var sideT = typeof(OpenZMainMenu).GetNestedType("Side", BindingFlags.NonPublic);
            var open = typeof(OpenZMainMenu).GetMethod("OpenSide", BindingFlags.NonPublic | BindingFlags.Instance);
            if (sideT == null || open == null) { Line(false, "the main menu's OPTIONS could not be opened from here: open OPTIONS, ADVANCED by hand for the pictures"); Finish(); yield break; }
            open.Invoke(mm, new object[] { Enum.Parse(sideT, "Options"), true });
            yield return 0.4f;
            mm.ProofOptTab(OzSettingTips.TabAdvanced);
            OzTrackpadScroll.Setting = OzTrackpadMath.Zoom; yield return 0.8f;
            string rows = mm.ProofOptTabRows;
            Line(mm.ProofOptTabOpen == OzSettingTips.TabAdvanced && rows.Contains(L), "main menu OPTIONS, ADVANCED shows: " + rows);
            Shot("advanced_zoom");
            yield return 0.3f;
            OzTrackpadScroll.Step(1); yield return 0.5f;
            Shot("advanced_move_map");
            yield return 0.3f;
            if (OzTrackpadScroll.Setting != set0) OzTrackpadScroll.Setting = set0;
            Note("put back: " + OzTrackpadScroll.Names[set0] + "; OPTIONS stays open (Esc closes it)");
            Finish();
        }

        // ------------------------------------------------------------------ put back
        static void PutBackDevices()
        {
            if (keysSent && Keyboard.current != null) { InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState()); keysSent = false; }
            if (sysMouse != null) { InputSystem.EnableDevice(sysMouse); sysMouse.MakeCurrent(); }
            if (proofMouse != null && proofMouse.added) InputSystem.RemoveDevice(proofMouse);
            proofMouse = null; sysMouse = null;
        }

        static void PutBack()
        {
            PutBackDevices();
            if (!kept) return; kept = false;
            if (OzTrackpadScroll.Setting != set0) OzTrackpadScroll.Setting = set0;
            if (!hadSet0) { PlayerPrefs.DeleteKey(OzTrackpadScroll.Pref); SettingField?.SetValue(null, -1); }
            if (hadSpeed0) PlayerPrefs.SetFloat(OzSpeedKeys.Pref, speedPref0); else PlayerPrefs.DeleteKey(OzSpeedKeys.Pref);
            PlayerPrefs.Save();
            var c = Cam; var g = ZGame.I;
            if (c) { c.follow = follow0; c.focus = focus0; c.SetHeightImmediate(h0); c.edgePx = edge0; }
            if (g) { g.gameSpeed = speed0; g.paused = paused0; }
        }

        static void Finish()
        {
            PutBack();
            Report = (bad == 0 ? "PASS " : "FAIL ") + ok + "/" + (ok + bad) + " lines: kit KT, TRACKPAD SCROLL option, " + level + ", " + Screen.width + "x" + Screen.height + "\n" + text
                     + "      put back: setting " + OzTrackpadScroll.Names[set0] + "\n";
            string p = Path.Combine(dir, "KT_check_" + level + ".txt");
            File.WriteAllText(p, Report);
            if (bad == 0) Debug.Log(Tag + " " + Report + "-> " + p.Replace('\\', '/')); else Debug.LogError(Tag + " " + Report + "-> " + p.Replace('\\', '/'));
        }
    }
}
