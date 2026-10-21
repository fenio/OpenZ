using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Counted check of the MINIMAL UI key (OzAction.MinimalUi, OpenZHud.MinimalKey.cs) and of its rows in the CONTROLS tables.
    /// Menu: OpenZ/Checks/Minimal UI key (Play mode), or OpenZ.EditorTools.OzMinimalKeyProof.Run(). It runs the part that fits the
    /// scene that is loaded; the result is one log line "[OpenZ minimal UI key] PASS n/n checks (...)" or "... FAIL ..." (also in
    /// subfolder f10_minimal_ui_key; the pictures are named by screen size and UI scale). The tables are measured by the project's own rectangle check (OzControlsKeysProof.Rects: every
    /// key cap, text, rule and button of a CONTROLS table that is on screen; 0 violations = nothing overlaps, nothing is cut or
    /// drawn smaller, everything inside its panel and the panel inside the screen).
    ///
    /// In a level (the HUD shown, no menu open, the Game view clicked once so that it has the keyboard), 23 checks:
    ///  K (3)   the action stands in the key table in the list's order with the default U; U is on no other default and is not a
    ///          fixed key; the player's own key map has no pair on it.
    ///  T (10)  ten presses of the real key (queued key events): each flips the setting, the saved value (oz_minimal_ui), the four
    ///          always-on panels (0 or 4 switched on; the MENU button and the minimap stay), plays the switch's sound
    ///          (ui_toggle_on / ui_toggle_off, a PLAY row in the sound log) and shows the note "MINIMAL UI ON / OFF · KEY U".
    ///  S (3)   the OPTIONS switch agrees: key ON = the pause menu's MINIMAL UI row reads ON; under the pause menu the key does
    ///          nothing; the switch's own setter OFF = the row reads OFF.
    ///  P (1)   the pause menu's CONTROLS page lists the key: 0 violations.
    ///  G (2)   the key does nothing while the HUD is hidden as a whole; with CTRL held it is not this key.
    ///  R (2)   rebound to Y: Y flips it, U no longer, the note names Y; a key in use is taken and flagged as a pair and a fixed
    ///          key is refused, exactly as for EXIT CREW (the same answers and the same reason text).
    ///  H (2)   at UI scale 100 % and 160 %: the F1 help lists the key: 0 violations.
    /// When the key events do not reach the game (the Game view has no keyboard) the key's own handler is called instead, the
    /// line says so and the result is FAIL: click into the Game view and run it again.
    /// In the main menu, 6 checks: CONTROLS lists every action; its table has 0 violations; click the cap + press Y = saved
    /// (ui_confirm); press H = taken and flagged as a pair in red (ui_error); F9 = refused (ui_disabled) and ESC cancels
    /// (ui_cancel); the player's key is back afterwards.
    /// The player's setting, key map and UI scale are put back when it ends, also when a step throws.
    /// </summary>
    public static class OzMinimalKeyProof
    {
        const string Tag = "[OpenZ minimal UI key]";
        public static string Report = "not run";
        /// <summary>
        /// is none), subfolder f10_minimal_ui_key.</summary>
        public static string Folder;
        static IEnumerator run;
        static int waitFrame, ok, bad;
        static float waitUntil;
        static readonly StringBuilder fails = new StringBuilder();
        static bool kept, minimal0, delivered;
        static Key key0, exit0;
        static float scale0;
        static string dir;

        [MenuItem("OpenZ/Checks/Minimal UI key (Play mode)")]
        public static void Run()
        {
            if (!EditorApplication.isPlaying) { Debug.LogWarning(Tag + " enter Play mode first (a level, or the main menu)"); return; }
            if (run != null) { Debug.LogWarning(Tag + " a run is still going"); return; }
            var gv = System.Type.GetType("UnityEditor.GameView,UnityEditor"); if (gv != null) EditorWindow.FocusWindowIfItsOpen(gv);
            ok = bad = 0; fails.Clear(); Report = "running"; delivered = true;
            dir = OutDir();
            Directory.CreateDirectory(dir);
            var menu = Object.FindFirstObjectByType<OpenZMainMenu>();
            if (OpenZHud.I && OpenZHud.I.Canvas) run = Match(OpenZHud.I);
            else if (menu) run = Menu(menu);
            else { Stop("no HUD and no main menu in this scene"); return; }
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
            return Path.Combine(all, stage, "f10_minimal_ui_key");
        }

        static void Step()
        {
            if (run == null || !EditorApplication.isPlaying) { run = null; EditorApplication.update -= Step; return; }
            if (Time.frameCount < waitFrame || Time.realtimeSinceStartup < waitUntil) return;
            bool more;
            try { more = run.MoveNext(); }
            catch (System.Exception ex) { Debug.LogException(ex); PutBack(); Report = Tag + " FAIL: exception " + ex.Message; more = false; }
            if (!more) { run = null; EditorApplication.update -= Step; return; }
            if (run.Current is float s) waitUntil = Time.realtimeSinceStartup + s;                          // seconds, real time (the pause menu stops the game clock)
            else waitFrame = Time.frameCount + (run.Current is int n ? n : 1);                              // frames
        }

        static void Check(bool pass, string what) { if (pass) ok++; else { bad++; fails.Append(" | ").Append(what); } }
        static void Stop(string why) { PutBack(); Report = Tag + " FAIL: " + why; Debug.LogError(Report); }

        static void Finish(string where, string detail)
        {
            PutBack();
            bool pass = bad == 0 && delivered;
            Report = $"{Tag} {(pass ? "PASS" : "FAIL")} {ok}/{ok + bad} checks ({where}, {Screen.width}x{Screen.height}): {detail}{(bad == 0 ? "" : "; FAILED:" + fails)}";
            if (pass) Debug.Log(Report); else Debug.LogError(Report);
            File.AppendAllText(Path.Combine(dir, "checks.txt"), Report + "\n");
        }

        static void Keep()
        {
            key0 = OzBindings.KeyOf(OzAction.MinimalUi); exit0 = OzBindings.KeyOf(OzAction.ExitCrew);
            minimal0 = OzSettingTips.MinimalUi; scale0 = OzText.UiScale; kept = true;
        }

        /// <summary>The player's own keys, setting and UI scale back.</summary>
        static void PutBack()
        {
            if (!kept) return;
            kept = false;
            OzBindings.Set(OzAction.MinimalUi, key0, out _);
            OzBindings.Set(OzAction.ExitCrew, exit0, out _);
            var hud = OpenZHud.I;
            if (!hud || !hud.Canvas) return;
            if (hud.Help && hud.Help.IsOpen) hud.Help.Toggle(false);
            if (hud.Pause && hud.Pause.IsOpen) hud.Pause.Close();
            if (!Mathf.Approximately(OzText.UiScale, scale0)) { OzText.UiScale = scale0; hud.ApplyUiScale(); }
            if (OzSettingTips.MinimalUi != minimal0) OzSettingTips.MinimalUi = minimal0;
        }

        /// <summary>Keys down for three frames, then up.</summary>
        static IEnumerator Tap(Keyboard kb, params Key[] keys)
        {
            InputSystem.QueueStateEvent(kb, new KeyboardState(keys)); yield return 3;
            InputSystem.QueueStateEvent(kb, new KeyboardState()); yield return 3;
        }

        /// <summary>The MINIMAL UI key: a real key event, or (when those do not reach the game) the handler the key calls.</summary>
        static IEnumerator Press(OpenZHud hud, Keyboard kb, Key k)
        {
            if (!delivered) { hud.MinimalUiKey(); yield return 2; yield break; }
            for (var e = Tap(kb, k); e.MoveNext();) yield return e.Current;
        }

        static bool Heard(float t0, string key)
        {
            foreach (var e in OzSoundLog.Since(t0)) if (e.key == key && e.outcome != null && e.outcome.StartsWith("PLAY")) return true;
            return false;
        }

        static void Shot(string name)
        {
            int pct = Mathf.RoundToInt(OzText.UiScale * 100f);
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, $"{Screen.width}x{Screen.height}_ui{pct}_{name}.png"));
        }

        /// <summary>The MINIMAL UI key cap is drawn in the table under `table`.</summary>
        static bool CapShown(Transform table)
        {
            var cap = table ? table.Find("Key_" + OzAction.MinimalUi) : null;
            return cap && cap.gameObject.activeInHierarchy;
        }

        /// <summary>The state text of the pause menu's MINIMAL UI row ("ON" / "OFF"), or null when the row is not found.</summary>
        static string SwitchText(OpenZHud hud)
        {
            var opt = hud.transform.Find("OpenZ_Pause/Root/SidePanel/Options");
            if (!opt) return null;
            foreach (var t in opt.GetComponentsInChildren<TMP_Text>(true))
            {
                if (t.name != "Label" || t.text != OzSettingTips.MinimalLabel) continue;
                var st = t.transform.parent.Find("State");
                var tt = st ? st.GetComponent<TMP_Text>() : null;
                return tt ? tt.text : null;
            }
            return null;
        }

        // ------------------------------------------------------------------ in a level
        static IEnumerator Match(OpenZHud hud)
        {
            var kb = Keyboard.current;
            if (kb == null) { Stop("no keyboard device"); yield break; }
            if (hud.Modal || !hud.hudVisible || hud.W == null || hud.W.GameOver) { Stop("a level with the HUD shown and no menu open is needed"); yield break; }
            Keep();
            if (hud.Production && hud.Production.IsOpen) hud.Production.Close();
            if (key0 != Key.U) OzBindings.Set(OzAction.MinimalUi, Key.U, out _);                            // the checks run on the default key
            if (OzSettingTips.MinimalUi) { OzSettingTips.MinimalUi = false; yield return 0.5f; }
            if (!Mathf.Approximately(OzText.UiScale, 1f)) { OzText.UiScale = 1f; hud.ApplyUiScale(); yield return 0.3f; }
            Key k = Key.U;

            // ---- K: the key table
            var def = OzBindings.DefOf(OzAction.MinimalUi);
            bool order = true; int onU = 0;
            for (int i = 0; i < OzBindings.Defs.Length; i++) { if ((int)OzBindings.Defs[i].action != i) order = false; if (OzBindings.Defs[i].key == Key.U) onU++; }
            Check(order && def.action == OzAction.MinimalUi && def.key == Key.U && !def.ctrl && !def.hold && def.section == OzBindings.UiSection,
                  $"K1 the key table is in the action list's order and MINIMAL UI has the default U (order {order}, default {def.key})");
            Check(onU == 1 && OzBindings.Reserved(Key.U) == null, $"K2 U is the default of one action only and is not a fixed key ({onU} on U, fixed: {OzBindings.Reserved(Key.U) ?? "no"})");
            Check(OzBindings.ConflictOf(OzAction.MinimalUi) == null, $"K3 no other action of this key map is on U (pairs: {OzBindings.ConflictText()})");

            // ---- T: ten presses of the real key
            int real = 0;
            for (int i = 1; i <= 10; i++)
            {
                bool want = (i & 1) == 1; float t0 = Time.unscaledTime; int f0 = OpenZHud.MinimalKeyFlips;
                for (var e = Press(hud, kb, k); e.MoveNext();) yield return e.Current;
                if (delivered && OpenZHud.MinimalKeyFlips == f0) { delivered = false; hud.MinimalUiKey(); yield return 2; }   // no key event reached the game: the same handler, called
                else if (delivered) real++;
                yield return 0.45f;
                bool set = OzSettingTips.MinimalUi == want, saved = PlayerPrefs.GetInt(OzSettingTips.PrefMinimal, -1) == (want ? 1 : 0);
                bool panels = hud.MinimalPanelsOn == (want ? 0 : 4) && hud.MinimalKeepsOn;
                bool sound = Heard(t0, want ? "ui_toggle_on" : "ui_toggle_off"), note = hud.MinimalNoteText == OpenZHud.MinimalNote(want);
                Check(set && saved && panels && sound && note && OpenZHud.MinimalKeyFlips == f0 + 1,
                      $"T{i} press {i}: MINIMAL UI {(want ? "ON" : "OFF")} (setting {set}, saved {saved}, panels on {hud.MinimalPanelsOn} with MENU + minimap {hud.MinimalKeepsOn}, sound {sound}, note \"{hud.MinimalNoteText}\")");
                if (i <= 2) { Shot(i == 1 ? "1_key_on_note" : "2_key_off_note"); yield return 2; }
            }

            // ---- S: the OPTIONS switch agrees
            for (var e = Press(hud, kb, k); e.MoveNext();) yield return e.Current;                           // ON
            yield return 0.3f;
            hud.Pause.Open(); hud.Pause.ProofOptTab(OzSettingTips.TabOf(OzSettingTips.MinimalLabel));
            yield return 0.7f;
            Check(OzSettingTips.MinimalUi && SwitchText(hud) == "ON", $"S1 the key switched it ON and the pause menu's MINIMAL UI row reads ON (row: {SwitchText(hud) ?? "not found"})");
            Shot("3_options_switch_on"); yield return 2;
            for (var e = Tap(kb, k); e.MoveNext();) yield return e.Current;
            Check(OzSettingTips.MinimalUi && !hud.MinimalUiKey() && OzSettingTips.MinimalUi, "S2 under the pause menu the key does nothing");
            OzSettingTips.MinimalUi = false;                                                                // what a click on the row does
            yield return 3;
            Check(SwitchText(hud) == "OFF", $"S3 the switch's own setter OFF: the row reads OFF (row: {SwitchText(hud) ?? "not found"})");

            // ---- P: the pause menu's CONTROLS page
            var show = typeof(OpenZPauseMenu).GetField("showControls", BindingFlags.NonPublic | BindingFlags.Instance);
            if (show != null) show.SetValue(hud.Pause, true);
            yield return 0.4f;
            int pv = OzControlsKeysProof.Rects();
            Check(show != null && pv == 0 && CapShown(hud.transform.Find("OpenZ_Pause/Root/SidePanel/Controls")), $"P1 pause menu CONTROLS lists the key, {pv} violations in its table (the [H8H10] RECTS line has the details)");
            Shot("4_pause_controls"); yield return 2;
            if (show != null) show.SetValue(hud.Pause, false);
            hud.Pause.Close();
            yield return 0.6f;

            // ---- G: the hidden HUD, CTRL
            hud.SetVisible(false);
            for (var e = Tap(kb, k); e.MoveNext();) yield return e.Current;
            bool hidden = !OzSettingTips.MinimalUi && !hud.MinimalUiKey() && !OzSettingTips.MinimalUi;
            hud.SetVisible(true);
            yield return 3;
            Check(hidden, "G1 while the HUD is hidden as a whole the key does nothing");
            for (var e = Tap(kb, Key.LeftCtrl, k); e.MoveNext();) yield return e.Current;
            Check(delivered && !OzSettingTips.MinimalUi, "G2 with CTRL held it is not this key" + (delivered ? "" : " (needs the real key events)"));

            // ---- R: another key; a key in use; a fixed key
            bool boundY = OzBindings.Set(OzAction.MinimalUi, Key.Y, out _);
            for (var e = Tap(kb, Key.U); e.MoveNext();) yield return e.Current;
            bool oldDead = !OzSettingTips.MinimalUi;
            for (var e = Tap(kb, Key.Y); e.MoveNext();) yield return e.Current;
            yield return 0.3f;
            bool newOn = OzSettingTips.MinimalUi; string noteY = hud.MinimalNoteText;
            for (var e = Tap(kb, Key.Y); e.MoveNext();) yield return e.Current;
            yield return 0.3f;
            bool newOff = !OzSettingTips.MinimalUi;
            if (!newOff) OzSettingTips.MinimalUi = false;
            Check(delivered && boundY && oldDead && newOn && newOff && noteY != null && noteY == OpenZHud.MinimalNote(true) && noteY.EndsWith(" " + OzBindings.Cap(Key.Y)),
                  $"R1 rebound to Y: U does nothing ({oldDead}), Y switches it on and off ({newOn}, {newOff}), the note names the new key (\"{noteY}\")" + (delivered ? "" : " (needs the real key events)"));
            bool takeM = OzBindings.Set(OzAction.MinimalUi, Key.H, out _);
            var pairM = OzBindings.ConflictOf(OzAction.MinimalUi); string pairs = OzBindings.ConflictText();
            bool refuseM = !OzBindings.Set(OzAction.MinimalUi, Key.F9, out string whyM) && OzBindings.KeyOf(OzAction.MinimalUi) == Key.H;
            bool takeE = OzBindings.Set(OzAction.ExitCrew, Key.Q, out _);
            var pairE = OzBindings.ConflictOf(OzAction.ExitCrew);
            bool refuseE = !OzBindings.Set(OzAction.ExitCrew, Key.F9, out string whyE) && OzBindings.KeyOf(OzAction.ExitCrew) == Key.Q;
            OzBindings.Set(OzAction.ExitCrew, exit0, out _);
            OzBindings.Set(OzAction.MinimalUi, Key.U, out _);
            Check(takeM && takeE && pairM != null && pairM.action == OzAction.HoldPosition && pairE != null && pairE.action == OzAction.Patrol && pairs.Contains("MINIMAL UI")
                  && refuseM && refuseE && whyM == whyE && OzBindings.ConflictOf(OzAction.MinimalUi) == null,
                  $"R2 a key in use is taken and flagged as a pair, a fixed key is refused, as for EXIT CREW (on H: {(pairM != null ? pairM.label : "no pair")}; pairs \"{pairs}\"; F9: \"{whyM}\" / \"{whyE}\")");

            // ---- H: the F1 help at UI scale 100 % and 160 %
            string hv = "";
            foreach (float s in new[] { 1f, 1.6f })
            {
                OzText.UiScale = s; hud.ApplyUiScale();
                yield return 0.4f;
                for (var e = Press(hud, kb, k); e.MoveNext();) yield return e.Current;                       // ON, for the picture
                yield return 0.4f;
                Shot("5_minimal_on"); yield return 2;
                for (var e = Press(hud, kb, k); e.MoveNext();) yield return e.Current;                       // OFF
                yield return 0.4f;
                Shot("6_minimal_off"); yield return 2;
                hud.Help.Toggle(true);
                yield return 0.5f;
                int v = OzControlsKeysProof.Rects(); hv += (hv.Length > 0 ? " / " : "") + v;
                Check(v == 0 && CapShown(hud.transform.Find("OpenZ_Help/Root/HelpPanel/Table")) && !OzSettingTips.MinimalUi,
                      $"H{(s > 1f ? 2 : 1)} F1 help at UI scale {s * 100f:0} % lists the key, {v} violations in its table (the [H8H10] RECTS line has the details)");
                Shot("7_f1_help"); yield return 2;
                hud.Help.Toggle(false);
                yield return 0.3f;
            }
            Finish("level " + (hud.game ? hud.game.level : "?"),
                   $"real key {real}/10 presses{(delivered ? "" : " (the key events did NOT reach the game: click into the Game view and run again; the key's own handler was called instead)")}; "
                   + $"violations: pause CONTROLS {pv}, F1 help at 100 % / 160 % {hv}; pictures in {dir.Replace('\\', '/')}");
        }

        // ------------------------------------------------------------------ in the main menu
        static IEnumerator Menu(OpenZMainMenu menu)
        {
            Keep();
            if (key0 != Key.U) OzBindings.Set(OzAction.MinimalUi, Key.U, out _);
            menu.ProofOpen("controls");
            yield return 0.9f;
            var crt = menu.ProofPanel("controls"); var table = menu.ProofTable;
            if (!crt || table == null) { Stop("the main menu has no CONTROLS table"); yield break; }
            Check(table.CoversEveryAction && CapShown(crt), $"M1 CONTROLS lists every action ({table.ActionCaps} key caps for {OzBindings.Defs.Length} actions) and the MINIMAL UI key");
            int mv = OzControlsKeysProof.Rects();
            Check(mv == 0, $"M2 main menu CONTROLS: {mv} violations in its table (the [H8H10] RECTS line has the details)");
            Shot("8_menu_controls"); yield return 2;

            float t0 = Time.unscaledTime;
            bool cap = table.ProofCapture(OzAction.MinimalUi);
            yield return 3;
            bool waiting = table.IsCapturing;
            Shot("9_menu_controls_press_a_key"); yield return 2;
            table.ProofPress(Key.Y);
            yield return 3;
            Check(cap && waiting && !table.IsCapturing && OzBindings.KeyOf(OzAction.MinimalUi) == Key.Y && table.StatusText.StartsWith("SAVED") && Heard(t0, "ui_confirm"),
                  $"M3 click the cap, press Y: saved (key {OzBindings.Cap(OzAction.MinimalUi)}, status \"{table.StatusText}\", ui_confirm {Heard(t0, "ui_confirm")})");

            t0 = Time.unscaledTime;
            table.ProofCapture(OzAction.MinimalUi);
            yield return 3;
            table.ProofPress(Key.H);
            yield return 3;
            var pair = OzBindings.ConflictOf(OzAction.MinimalUi);
            Check(OzBindings.KeyOf(OzAction.MinimalUi) == Key.H && pair != null && pair.action == OzAction.HoldPosition && table.StatusText.StartsWith("CONFLICT") && Heard(t0, "ui_error"),
                  $"M4 press H (HOLD POSITION): taken and flagged as a pair (status \"{table.StatusText}\", ui_error {Heard(t0, "ui_error")})");
            Shot("10_menu_controls_pair_in_red"); yield return 2;

            t0 = Time.unscaledTime;
            table.ProofCapture(OzAction.MinimalUi);
            yield return 3;
            table.ProofPress(Key.F9);
            yield return 3;
            bool refused = table.IsCapturing && OzBindings.KeyOf(OzAction.MinimalUi) == Key.H;
            string said = table.StatusText;
            table.ProofPress(Key.Escape);
            yield return 3;
            Check(refused && !table.IsCapturing && Heard(t0, "ui_disabled") && Heard(t0, "ui_cancel"),
                  $"M5 press F9: refused, still waiting (status \"{said}\", ui_disabled {Heard(t0, "ui_disabled")}); ESC cancels (ui_cancel {Heard(t0, "ui_cancel")})");

            Key back = key0;
            PutBack();
            yield return 3;
            Check(OzBindings.KeyOf(OzAction.MinimalUi) == back && (back != Key.U || !PlayerPrefs.HasKey(OzBindings.DefOf(OzAction.MinimalUi).pref)),
                  $"M6 the player's key is back ({OzBindings.Cap(OzAction.MinimalUi)})");
            menu.ProofOpen("none");
            Finish("main menu", $"violations in the CONTROLS table: {mv}; pictures in {dir.Replace('\\', '/')}");
        }
    }
}
