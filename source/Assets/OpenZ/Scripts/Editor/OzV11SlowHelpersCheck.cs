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

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Counted check of the three helpers for slow computers: the one-time plate with its click, the FRAME RATE LIMIT row, the
    /// frame counter's two numbers. Menu: OpenZ/Checks/Slow computer helpers (Play mode), in a level (a running match, started on
    /// the scene) or in the main menu (the row there). One log line at the end: "[OpenZ slow computer helpers] PASS n/n ...".
    /// A slow computer is played by a sleep in every editor tick (the frame limit stays 60): the plate's rule counts real frames.
    /// The waits of the plate's part are real seconds, because its rule is in real seconds. The check uses a mouse of its own
    /// for the hover and the click; the saved marks, the limit, the counter option and the Game view's size are put back.
    /// </summary>
    public static class OzV11SlowHelpersCheck
    {
        const string Tag = "[OpenZ slow computer helpers]";
        public static string Report = "not run";
        static readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
        static int waitFrame, ok, bad, pictures, slowMs;
        static float waitUntil;
        static readonly StringBuilder fails = new StringBuilder();
        static string dir;
        static Mouse mouse, sysMouse;
        static bool kept; static int tip0, cap0, fpsOn0, capFps0, target0;

        [MenuItem("OpenZ/Checks/Slow computer helpers (Play mode)")]
        public static void Run()
        {
            if (!EditorApplication.isPlaying) { Debug.LogWarning(Tag + " enter Play mode first"); return; }
            if (stack.Count > 0) { Debug.LogWarning(Tag + " a run is still going"); return; }
            ok = bad = pictures = 0; fails.Clear(); Report = "running";
            dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../../work/records/milestones/v1_1_20261012/f44_slow_computer_helpers"));
            Directory.CreateDirectory(Path.Combine(dir, "for_owner"));
            var hud = OpenZHud.I;
            if (hud && hud.Canvas && hud.W != null) stack.Push(Level(hud));
            else if (Object.FindFirstObjectByType<OpenZMainMenu>()) stack.Push(Menu(Object.FindFirstObjectByType<OpenZMainMenu>()));
            else { Report = Tag + " FAIL: a level or the main menu is needed"; Debug.LogError(Report); return; }
            waitFrame = 0; waitUntil = 0f; EditorApplication.update += Step;
        }

        static void Step()
        {
            if (stack.Count == 0 || !EditorApplication.isPlaying) { stack.Clear(); PutBack(); EditorApplication.update -= Step; return; }
            if (slowMs > 0) System.Threading.Thread.Sleep(slowMs);                                            // the slow computer
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
            bool pass = bad == 0 && ok > 0;
            Report = $"{Tag} {(pass ? "PASS" : "FAIL")} {ok}/{ok + bad} checks ({where}, {Screen.width}x{Screen.height}, {pictures} pictures): {detail}{(bad == 0 ? "" : "; FAILED:" + fails)}";
            if (pass) Debug.Log(Report); else Debug.LogError(Report);
            File.AppendAllText(Path.Combine(dir, "checks.txt"), Report + "\n");
        }

        static void Keep()
        {
            tip0 = PlayerPrefs.GetInt(OpenZHud.SlowTipPref, 0); cap0 = PlayerPrefs.GetInt(OzFpsCap.Pref, -1); fpsOn0 = PlayerPrefs.GetInt(OzQuality.FpsPref, 0);
            capFps0 = GameBoot.CapFps; target0 = Application.targetFrameRate; kept = true;
        }

        static void PutBack()
        {
            slowMs = 0;
            if (mouse != null) { if (sysMouse != null) { InputSystem.EnableDevice(sysMouse); sysMouse.MakeCurrent(); } if (mouse.added) InputSystem.RemoveDevice(mouse); mouse = null; sysMouse = null; }
            if (!kept) return;
            kept = false;
            PlayerPrefs.SetInt(OpenZHud.SlowTipPref, tip0);
            if (cap0 < 0) PlayerPrefs.DeleteKey(OzFpsCap.Pref); else PlayerPrefs.SetInt(OzFpsCap.Pref, cap0);
            PlayerPrefs.SetInt(OzQuality.FpsPref, fpsOn0); PlayerPrefs.Save();
            GameBoot.CapFps = capFps0; Application.targetFrameRate = target0; HeatGuardRuntime.HeldFps = 0;
        }

        static void MouseEv(Vector2 p, bool down)
        {
            if (mouse == null) { sysMouse = Mouse.current; mouse = InputSystem.AddDevice<Mouse>("OzF44Mouse"); if (sysMouse != null && sysMouse != mouse) InputSystem.DisableDevice(sysMouse); }
            var st = new MouseState { position = p }; if (down) st = st.WithButton(MouseButton.Left);
            InputSystem.QueueStateEvent(mouse, st);
        }

        static IEnumerator Shot(string name)
        {
            string path = Path.Combine(dir, "for_owner", name + "_" + Screen.width + "x" + Screen.height + ".png");
            try { if (File.Exists(path)) File.Delete(path); } catch (System.Exception) { }
            ScreenCapture.CaptureScreenshot(path);
            float t0 = Time.realtimeSinceStartup;
            while (!File.Exists(path) && Time.realtimeSinceStartup - t0 < 4f) yield return 1;
            if (File.Exists(path)) pictures++; else Debug.LogWarning(Tag + " picture not written: " + path);
            yield return 2;
        }

        static float mFps, mClock;
        /// <summary>Frames a second and game seconds per real second (at the game speed) over `seconds` of real time.</summary>
        static IEnumerator Measure(OpenZHud hud, float seconds)
        {
            int f0 = Time.frameCount; float r0 = Time.realtimeSinceStartup; double w0 = hud != null ? hud.W.Time : 0.0;
            yield return seconds;
            float dr = Time.realtimeSinceStartup - r0;
            mFps = (Time.frameCount - f0) / dr;
            mClock = hud != null ? (float)((hud.W.Time - w0) / dr / Mathf.Max(0.01f, hud.game.gameSpeed)) : 0f;
        }

        static IEnumerator WaitPlate(OpenZHud hud, int shown0, float seconds)
        {
            float t0 = Time.realtimeSinceStartup;
            while (OpenZHud.SlowTipShown == shown0 && Time.realtimeSinceStartup - t0 < seconds) yield return 5;
            yield return 0.6f;
        }

        // ------------------------------------------------------------------ a level
        static IEnumerator Level(OpenZHud hud)
        {
            var W = hud.W;
            if (hud.Modal || W.GameOver || !hud.hudVisible) { Report = Tag + " FAIL: a running match with the HUD shown and no menu open is needed"; Debug.LogError(Report); yield break; }
            Keep();
            string level = hud.game.level;

            // ---- the limit row
            PlayerPrefs.DeleteKey(OzFpsCap.Pref);
            Check(OzFpsCap.Saved == 60 && OzFpsCap.Name == "60", $"L1 no saved value: the limit is {OzFpsCap.Saved} (must be 60)");
            OzFpsCap.Set(30); yield return 2f; yield return Measure(hud, 5f);
            Check(mFps > 28.5f && mFps < 31f && mClock > 0.98f && mClock < 1.02f && PlayerPrefs.GetInt(OzFpsCap.Pref, 0) == 30 && GameBoot.CapFps == 30,
                $"L2 the limit set to 30: {mFps:0.0} frames a second after 2 s, the game clock runs {mClock:0.000} game seconds a second, saved value {PlayerPrefs.GetInt(OzFpsCap.Pref, 0)}, the game's cap {GameBoot.CapFps}");
            // the heat guard's cap comes and goes over the player's 30
            var guard = new GameObject("OzF44HeatGuard").AddComponent<HeatGuardRuntime>();
            var temp = typeof(HeatGuardRuntime).GetField("temp", BindingFlags.NonPublic | BindingFlags.Static);
            temp.SetValue(null, 92); yield return 4; int hot30 = Application.targetFrameRate, held30 = HeatGuardRuntime.HeldFps;
            OzFpsCap.Set(60); int hotStep = Application.targetFrameRate;                                    // the player moves the row while the guard holds 30
            temp.SetValue(null, 80); yield return 4; int cool60 = Application.targetFrameRate;
            OzFpsCap.Set(30); temp.SetValue(null, 92); yield return 4; temp.SetValue(null, 80); yield return 4; int cool30 = Application.targetFrameRate;
            temp.SetValue(null, -1); Object.Destroy(guard.gameObject); HeatGuardRuntime.HeldFps = 0;
            Check(hot30 == 30 && held30 == 30 && hotStep == 30 && cool60 == 60 && cool30 == 30,
                $"L3 the heat guard's cap (played: 92 C, then 80 C): hot {hot30} (holds {held30}), the row moved to 60 while hot: still {hotStep}; cool again: {cool60} = the player's 60; with the player's 30: cool again {cool30}");
            OzFpsCap.Set(60); yield return 2f; yield return Measure(hud, 4f);
            Check(Application.targetFrameRate == 60 && GameBoot.CapFps == 60 && mFps > 45f && mFps < 61.5f && mClock > 0.98f && mClock < 1.02f, $"L4 back to 60: the limit is {Application.targetFrameRate} again (the game's cap {GameBoot.CapFps}), {mFps:0.0} frames a second (what this scene gives in the editor under that limit; over the 30 it had), the game clock {mClock:0.000}");

            // ---- the counter
            OzQuality.FpsOn = false; yield return 0.8f;
            var chipCanvas = GameObject.Find("OpenZ_FpsCanvas"); var cv = chipCanvas ? chipCanvas.GetComponent<Canvas>() : null;
            Check((cv == null || !cv.enabled) && OzFpsChip.ShownText == "", $"C1 counter OFF: no chip drawn ({(cv == null ? "no canvas" : "canvas enabled " + cv.enabled)}), nothing measured (text \"{OzFpsChip.ShownText}\")");
            OzQuality.FpsOn = true; yield return 1.4f;
            float fps = OzFpsChip.ShownFps, cpu = OzFpsChip.ShownCpuMs, gpu = OzFpsChip.ShownGpuMs, frame = 1000f / Mathf.Max(1f, fps);
            Check(OzFpsChip.ShownText.Contains(" FPS · CPU ") && cpu > 0f && cpu <= frame * 1.15f && gpu <= frame * 1.15f,
                $"C2 counter ON at the 60 limit: \"{OzFpsChip.ShownText}\" (frame {frame:0.0} ms, processor {cpu:0.0} ms, graphics chip {(gpu > 0f ? gpu.ToString("0.0") + " ms" : "not given by this machine: left out")}); neither part is longer than the frame");
            Application.targetFrameRate = -1; yield return 1.2f; yield return 1.2f;                         // a few seconds without a limit: one side limits
            fps = OzFpsChip.ShownFps; cpu = OzFpsChip.ShownCpuMs; gpu = OzFpsChip.ShownGpuMs; frame = 1000f / Mathf.Max(1f, fps);
            string free = OzFpsChip.ShownText; Application.targetFrameRate = GameBoot.CapFps;
            float big = Mathf.Max(cpu, gpu);
            Check(big > 0f && big <= frame * 1.15f, $"C3 without a limit for 2.4 s: \"{free}\" (frame {frame:0.0} ms, the larger part {big:0.0} ms = {big / frame * 100f:0} % of the frame; in the editor the editor's own work is in the frame too)");
            cv = GameObject.Find("OpenZ_FpsCanvas").GetComponent<Canvas>(); var chip = cv.transform.Find("Root/FpsChip") as RectTransform;
            yield return ChipClear(hud, chip, "C4 the chip lies on no panel");
            yield return Shot("counter_on_" + level);
            float scale0 = OzText.UiScale; OzText.UiScale = 1.6f; hud.ApplyUiScale(); yield return 1.8f;
            yield return ChipClear(hud, chip, "C5 the same at interface scale 160 %");
            yield return Shot("counter_on_scale160_" + level);
            float y160 = OzFpsChip.PlacedY;
            OzText.UiScale = scale0; hud.ApplyUiScale(); yield return 1.4f;
            Check(Mathf.Abs(OzFpsChip.PlacedY - OzFpsChip.Y) < 0.01f, $"C6 back at the standard scale the chip stands at its own place again ({OzFpsChip.PlacedY:0} canvas units from the top; at 160 % it stood at {y160:0})");
            OzQuality.FpsOn = false;

            // ---- the plate
            PlayerPrefs.SetInt(OpenZHud.SlowTipPref, 0); hud.SlowTipProofReset();
            int s0 = OpenZHud.SlowTipShown;
            // (a) never at 60
            hud.SlowTipProofSkipQuietStart(); OpenZHud.SlowTipAverage = 0f;
            yield return 24f;
            Check(OpenZHud.SlowTipShown == s0 && OpenZHud.SlowTipAverage >= 45f, $"T1 at 60 frames a second, past the two minutes: no plate in 24 s (measured {OpenZHud.SlowTipAverage:0.0} over 20 s)");
            // (b) the player's own 30 is not a slow computer
            OzFpsCap.Set(30); hud.SlowTipProofReset(); hud.SlowTipProofSkipQuietStart(); OpenZHud.SlowTipAverage = 0f;
            yield return 24f;
            Check(OpenZHud.SlowTipShown == s0 && OpenZHud.SlowTipAverage > 27f && OpenZHud.SlowTipAverage < 32f, $"T2 the player's own limit of 30: no plate in 24 s (measured {OpenZHud.SlowTipAverage:0.0}; the line is 22.5 there)");
            OzFpsCap.Set(60);
            // (c) the real two minutes: slow from here on, no plate before 2:00, one after it
            hud.SlowTipProofReset(); slowMs = 30;
            float t0 = Time.realtimeSinceStartup;
            while (hud.SlowTipControl < 110f && Time.realtimeSinceStartup - t0 < 200f) yield return 10;
            yield return Measure(hud, 3f);
            Check(OpenZHud.SlowTipShown == s0 && mFps < 45f, $"T3 slow from the first second of control ({mFps:0.0} frames a second now): no plate after {hud.SlowTipControl:0} s of control");
            yield return WaitPlate(hud, s0, 40f);
            Check(OpenZHud.SlowTipShown == s0 + 1 && hud.SlowTipShowing && hud.SlowTipControl >= 120f && hud.SlowTipControl < 128f && PlayerPrefs.GetInt(OpenZHud.SlowTipPref, 0) == 1,
                $"T4 one plate {hud.SlowTipControl:0.0} s after control (never before 120): shown {OpenZHud.SlowTipShown - s0}, on screen {hud.SlowTipShowing}, measured {OpenZHud.SlowTipAverage:0.0} frames a second, the mark saved {PlayerPrefs.GetInt(OpenZHud.SlowTipPref, 0)}");
            yield return Shot("plate_" + level);
            yield return 13.5f;
            Check(!hud.SlowTipShowing && !hud.game.paused && Time.timeScale > 0f && !hud.Pause.IsOpen, $"T5 not clicked: after its 12 s the plate is gone ({!hud.SlowTipShowing}) and nothing paused (paused {hud.game.paused}, pause menu {hud.Pause.IsOpen})");
            // (d) once per install: a new level start with the mark set
            hud.SlowTipProofReset(); hud.SlowTipProofSkipQuietStart();
            yield return 26f;
            Check(OpenZHud.SlowTipShown == s0 + 1 && OpenZHud.SlowTipHeldBy.StartsWith("shown before"), $"T6 the mark is set, a level's state as at a new start, slow for 26 s: no second plate (held back: \"{OpenZHud.SlowTipHeldBy}\")");
            // (e) the pause does not count
            PlayerPrefs.SetInt(OpenZHud.SlowTipPref, 0); hud.SlowTipProofReset(); hud.SlowTipProofSkipQuietStart(); yield return 3f;
            float c0 = hud.SlowTipControl; hud.Pause.Open(); yield return 24f; float c1 = hud.SlowTipControl; bool none = OpenZHud.SlowTipShown == s0 + 1;
            hud.Pause.Close(); yield return 1.2f;
            Check(none && c1 - c0 < 0.3f, $"T7 paused for 24 s while slow: no plate, {c1 - c0:0.00} s of it counted as play");
            // (f) the click
            ZObject unit = null; foreach (var o in W.Objects) if (o.IsUnit && o.Owner == hud.Player && !o.Destroyed && o.Kind == ZKind.Robot && o.Leader == null) { unit = o; break; }
            hud.input.Selection.Clear(); if (unit != null) hud.input.Selection.Add(unit);
            int wp0 = unit != null ? unit.Waypoints.Count : 0;
            yield return WaitPlate(hud, s0 + 1, 40f);
            bool up = OpenZHud.SlowTipShown == s0 + 2 && hud.SlowTipShowing;
            Vector2 pt = up ? hud.SlowTipPoint : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            bool overUi = hud.IsOverUi(pt);
            MouseEv(pt, false); yield return 6; bool hover = hud.SlowTipHovered;
            yield return Shot("plate_hover_" + level);
            MouseEv(pt, true); yield return 3; MouseEv(pt, false); yield return 10;
            bool paused = hud.game.paused && hud.Pause.IsOpen, tab = hud.Pause.ProofOptTabOpen == OzSettingTips.TabGraphics; string rows = hud.Pause.ProofOptTabRows;
            bool same = hud.input.Selection.Count == (unit != null ? 1 : 0) && (unit == null || (hud.input.Selection.Contains(unit) && unit.Waypoints.Count == wp0));
            Check(up && overUi && hover, $"T8 the second install's plate: on screen {up}, a panel for the pointer {overUi}, lit under the pointer {hover}");
            Check(paused && tab && rows.Contains(OzQuality.Label) && rows.Contains(OzFpsCap.Label) && !hud.SlowTipShowing,
                $"T9 a click on the plate: paused {paused}, OPTIONS on the GRAPHICS tab {tab} (rows: {rows}), the plate gone {!hud.SlowTipShowing}");
            Check(same, $"T10 that click gave no order and no selection in the world: {hud.input.Selection.Count} selected (the same unit {(unit != null && hud.input.Selection.Contains(unit))}), its orders {(unit != null ? unit.Waypoints.Count : 0)} (before {wp0})");
            yield return 0.6f; yield return Shot("options_graphics_" + level);
            MouseEv(new Vector2(Screen.width * 0.5f, Screen.height * 0.62f), false); yield return 2;
            hud.Pause.Close(); yield return 1.2f; hud.input.Selection.Clear();
            // (g) the ending does not count (the last case: the match ends here)
            PlayerPrefs.SetInt(OpenZHud.SlowTipPref, 0); hud.SlowTipProofReset(); hud.SlowTipProofSkipQuietStart(); yield return 3f;
            OpenZVictory.DebugFortFalls(true);
            t0 = Time.realtimeSinceStartup; while (!W.GameOver && Time.realtimeSinceStartup - t0 < 30f) yield return 5;
            c0 = hud.SlowTipControl; int s1 = OpenZHud.SlowTipShown; yield return 26f;
            Check(W.GameOver && OpenZHud.SlowTipShown == s1 && hud.SlowTipControl - c0 < 0.3f, $"T11 the ending (the enemy fort staged to fall), slow for 26 s: no plate, {hud.SlowTipControl - c0:0.00} s counted as play (game over {W.GameOver})");
            slowMs = 0;
            Finish("level " + level, "the limit row L1 to L4, the counter C1 to C5, the plate T1 to T11; a briefing is not in this run (the HUD does not run during it and the rule asks ZodBriefing.BriefingActive); the main menu has no plate (it belongs to the level's HUD)");
        }

        static IEnumerator ChipClear(OpenZHud hud, RectTransform chip, string what)
        {
            yield return 2;
            var c = new Vector3[4]; chip.GetWorldCorners(c); int over = 0;
            var pts = new[] { (Vector2)c[0], (Vector2)c[1], (Vector2)c[2], (Vector2)c[3], (Vector2)((c[0] + c[2]) * 0.5f) };
            foreach (var p in pts) if (hud.IsOverUi(p)) over++;
            bool inside = c[0].x >= 0f && c[0].y >= 0f && c[2].x <= Screen.width && c[2].y <= Screen.height;
            Check(over == 0 && inside, $"{what}: {over} of its 5 points on a panel, inside the screen {inside} (chip {c[0].x:0},{c[0].y:0} to {c[2].x:0},{c[2].y:0}, text \"{OzFpsChip.ShownText}\")");
        }

        // ------------------------------------------------------------------ the main menu
        static IEnumerator Menu(OpenZMainMenu menu)
        {
            Keep();
            menu.ProofOpen("options"); yield return 0.8f; menu.ProofOptTab(OzSettingTips.TabGraphics); yield return 0.5f;
            string rows = menu.ProofOptTabRows;
            PlayerPrefs.DeleteKey(OzFpsCap.Pref);
            Check(rows.Contains(OzQuality.Label) && rows.Contains(OzFpsCap.Label) && OzFpsCap.Saved == 60, $"M1 the main menu's GRAPHICS tab holds the row (rows: {rows}); with no saved value it reads {OzFpsCap.Name}");
            yield return Shot("options_graphics_main_menu");
            OzFpsCap.Step(1); yield return 2f; yield return Measure(null, 4f);
            Check(OzFpsCap.Saved == 30 && mFps > 28.5f && mFps < 31f, $"M2 one step: {OzFpsCap.Name}, {mFps:0.0} frames a second in the menu");
            yield return Shot("options_graphics_main_menu_30");
            OzFpsCap.Step(1); yield return 2f; yield return Measure(null, 3f);
            Check(OzFpsCap.Saved == 60 && mFps > 55f && mFps < 61.5f, $"M3 the next step: {OzFpsCap.Name}, {mFps:0.0} frames a second");
            menu.ProofOpen("none");
            Finish("main menu", "the row in the main menu's OPTIONS");
        }
    }
}
