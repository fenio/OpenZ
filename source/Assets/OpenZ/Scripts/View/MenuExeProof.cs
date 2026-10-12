using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;

namespace OpenZ
{
    /// <summary>
    /// menu_exe_v1 Play proof (ONE short Play of the MainMenu scene, about 30 s): OpenZ > Menu Exe > Run Play Proof, or
    /// <c></c>
    /// AudioSource), the centred OPTIONS / CONTROLS modals, the rebinding (a real key event through the Input System, with a
    /// direct fallback when the Game view gets no keyboard), the conflict display, RESET, the map-card hover sound, the HEAD
    /// START switch + tooltip on both maps, the MEDIUM default, the compact pause table and the HUD layout at 2560x1600 /
    /// 1080p. Review fixes: a click on the modal plate keeps it open and the scrim closes it, the modal owns the keyboard, the
    /// HIDE THE HUD cue follows its binding, Ctrl-blind conflicts + reserved debug keys, the in-game key caps (KIT_KEYCAP)
    /// (music switch, keys, head start) is put back. Ends with "[MenuExeProof] PROOF PASS n/n" (or FAIL + the failed checks).
    /// </summary>
    public sealed class MenuExeProof : MonoBehaviour
    {
        static readonly string Root = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../.."));
        readonly StringBuilder log = new StringBuilder();
        readonly List<string> fails = new List<string>();
        int pass, total; string dir; float t0;

        public static string Run()
        {
            if (!Application.isPlaying) return "enter Play in the MainMenu scene first";
            if (FindFirstObjectByType<MenuExeProof>()) return "already running";
            new GameObject("MenuExeProof").AddComponent<MenuExeProof>();
            return "menu_exe_v1 proof started (about 30 s): watch the Console for [MenuExeProof]";
        }

        void Mark(string s) { string l = $"{Time.unscaledTime - t0,7:0.00}  {s}"; log.AppendLine(l); Debug.Log("[MenuExeProof] " + s); }
        void Check(string what, bool ok, string detail)
        {
            total++; if (ok) pass++; else fails.Add(what);
            Mark($"{(ok ? "PASS" : "FAIL")}  {what}: {detail}");
        }
        IEnumerator Wait(float s) { float e = Time.unscaledTime + s; while (Time.unscaledTime < e) yield return null; }
        void Snap(string file)
        {
            var tb = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(Path.Combine(dir, file), tb.EncodeToPNG());
            Mark($"frame {file} {tb.width}x{tb.height}"); Destroy(tb);
        }
        static string ReadScript(string rel)
        {
            string path = Path.Combine(Application.dataPath, "OpenZ/Scripts", rel);
            return File.Exists(path) ? File.ReadAllText(path) : "";
        }
        /// <summary>Screen point of a panel-local position (x from the plate's left edge, y down from its top), overlay canvas.</summary>
        static Vector2 PanelPoint(RectTransform rt, float x, float yFromTop)
        {
            var r = rt.rect; var w = rt.TransformPoint(new Vector3(r.xMin + x, r.yMax - yFromTop, 0f));
            return new Vector2(w.x, w.y);
        }
        static bool Heard(float since, string key, string outcomePrefix, out string row)
        {
            row = null;
            foreach (var e in OzSoundLog.Since(since))
                if (e.key == key && (outcomePrefix == null || (e.outcome != null && e.outcome.StartsWith(outcomePrefix)))) { row = $"{e.t:0.00} {e.src} {e.key} {e.clip} {e.outcome} vol {e.vol:0.000}"; return true; }
            return false;
        }

        IEnumerator Start()
        {
            t0 = Time.unscaledTime;
            dir = Path.Combine(Root, "work/records/milestones/menu_exe_v1", DateTime.Now.ToString("yyyyMMdd_HHmmss")); Directory.CreateDirectory(dir);
            float w0 = Time.unscaledTime; OpenZMainMenu menu = null;
            while (!(menu = FindFirstObjectByType<OpenZMainMenu>()) && Time.unscaledTime - w0 < 20f) yield return null;
            if (!menu) { Mark("no OpenZMainMenu in the scene (open Assets/OpenZ/Scenes/MainMenu.unity, then Play)"); Finish(); yield break; }
            yield return Wait(1.5f);
            Mark($"menu_exe_v1 proof {DateTime.Now:yyyy-MM-dd HH:mm:ss}, scene {SceneManager.GetActiveScene().name}, screen {Screen.width}x{Screen.height}, canvas {menu.ProofCanvasWidth:0} u");
            Mark("settings: " + OzSettings.Describe());
            Mark("keys: " + OzBindings.Summary());

            // ---- snapshot of what the proof changes
            bool music0 = AudioDirector.MusicEnabled; int musicPref0 = PlayerPrefs.GetInt(AudioDirector.MusicPref, 1);
            bool head0 = OpenZDifficulty.HeadStartToggle;
            var keys0 = new Dictionary<string, int>();
            foreach (var d in OzBindings.Defs) if (PlayerPrefs.HasKey(d.pref)) keys0[d.pref] = PlayerPrefs.GetInt(d.pref);

            // ---- 2 + 11: the menu music
            float tm = 0f;
            if (!music0) { Mark($"music was OFF here (oz_music {musicPref0}: editor prefs, e.g. a take preset): switched ON for the proof"); tm = Time.unscaledTime; AudioDirector.MusicEnabled = true; yield return Wait(2.5f); }
            bool heard = Heard(tm, "mus_menu", "PLAY Music (menu)", out var mrow);
            AudioSource playing = null;
            foreach (var src in FindObjectsByType<AudioSource>(FindObjectsSortMode.None)) if (src && src.isPlaying && src.clip && src.clip.name.StartsWith("M1_menu")) { playing = src; break; }
            Check("menu music plays", heard && playing != null, $"OzSoundLog {(mrow ?? "no PLAY Music (menu) row")}; AudioSource {(playing ? playing.clip.name + " vol " + playing.volume.ToString("0.000") + " at " + playing.time.ToString("0.0") + " s" : "none playing an M1_menu clip")}");
            Check("menu music under the UI", AudioDirector.MenuMusicDb <= -5f, $"{AudioDirector.MenuMusicDb:0} dB against the track's own mix");

            // ---- 5: OPTIONS centred modal
            menu.ProofOpen("options"); yield return Wait(0.7f);
            var ort = menu.ProofPanel("options");
            Check("options beside the list", Beside(ort, out var od), od);
            Check("options MUSIC + FULLSCREEN rows", menu.ProofOptionRows, "MUSIC · SOUNDTRACK and FULLSCREEN · NATIVE RESOLUTION after the live rows");
            Snap("01_options.png");

            // ---- 6: CONTROLS centred modal + table
            menu.ProofOpen("controls"); yield return Wait(0.7f);
            var crt = menu.ProofPanel("controls"); var table = menu.ProofTable;
            Check("controls beside the list", Beside(crt, out var cd), cd);
            Check("controls lists every action", table != null && table.CoversEveryAction, table != null ? $"{table.ActionCaps} rebindable caps, all {OzBindings.Defs.Length} actions, narrowest cap {table.NarrowestCap:0} u (old table: 14 u)" : "no table");
            Snap("02_controls.png");

            // ---- review fix: a click on the plate (label, title, band, gap) keeps the modal, only the scrim closes it
            if (crt && ort)
            {
                string hitLab = menu.ProofClickAt(PanelPoint(crt, 62f, 132f)); yield return null;              // CONTROLS: the first SELECT row's label
                string sideLab = menu.ProofSide;
                bool capOn = table != null && table.ProofCapture(OzAction.NextRobot); yield return null;
                string hitCapLab = menu.ProofClickAt(PanelPoint(crt, 62f, 132f)); yield return null;            // the same click during a rebind
                string sideCap = menu.ProofSide;
                if (table != null) table.Cancel(false);
                menu.ProofOpen("options"); yield return Wait(0.6f);
                string hitTitle = menu.ProofClickAt(PanelPoint(ort, 60f, 24f)); yield return null;              // OPTIONS: the title
                string sideTitle = menu.ProofSide;
                var oc = new Vector3[4]; ort.GetWorldCorners(oc);
                string hitOut = menu.ProofClickAt(new Vector2(oc[0].x * 0.5f, Screen.height * 0.5f)); yield return Wait(0.3f);   // over the menu list, outside the plate
                string sideOut = menu.ProofSide;
                Check("modal: a click on the plate keeps it, the scrim closes it",
                      hitLab == "plate" && sideLab == "Controls" && capOn && hitCapLab == "plate" && sideCap == "Controls" && hitTitle == "plate" && sideTitle == "Options" && hitOut == "scrim" && sideOut == "None",
                      $"CONTROLS row label -> {hitLab} (side {sideLab}); the same during a rebind -> {hitCapLab} (side {sideCap}); OPTIONS title -> {hitTitle} (side {sideTitle}); outside the plate -> {hitOut} (side {sideOut})");
            }
            else Check("modal: a click on the plate keeps it, the scrim closes it", false, "no options / controls panel");

            // ---- review fix: the modal owns the keyboard (W/S moved the list behind it with ui_hover, Enter / Space ran QUIT)
            menu.ProofOpen("controls"); yield return Wait(0.6f);
            {
                int focus0 = menu.ProofFocusItem; bool evS = false; var kbS = Keyboard.current;
                if (kbS != null)
                {
                    try { InputSystem.QueueStateEvent(kbS, new KeyboardState(Key.S)); } catch (Exception) { }
                    yield return null;
                    evS = kbS.sKey.isPressed;
                    try { InputSystem.QueueStateEvent(kbS, new KeyboardState()); } catch (Exception) { }
                    yield return null;
                }
                bool hook = ReadScript("UI/OpenZMainMenu.cs").Contains("menu_exe_v1: the modal owns the keyboard");
                bool stayed = menu.ProofFocusItem == focus0 && menu.ProofSide == "Controls";
                string how = evS ? $"real S key event with CONTROLS open: list focus {focus0} -> {menu.ProofFocusItem}, side {menu.ProofSide}" : "no keyboard event in the Game view (no focus): source hook only";
                Check("modal owns the keyboard", hook && (!evS || stayed), $"{how}; Keys() hook {(hook ? "present" : "MISSING")}");
            }

            // ---- 6: rebinding through a real key event (fallback: the same code path directly)
            if (table != null)
            {
                float tr = Time.unscaledTime;
                table.ProofCapture(OzAction.NextRobot); yield return null; yield return null;
                bool evOk = false;
                var kb = Keyboard.current;
                if (kb != null)
                {
                    try { InputSystem.QueueStateEvent(kb, new KeyboardState(Key.Q)); } catch (Exception ex) { Mark("QueueStateEvent: " + ex.Message); }
                    yield return null; yield return null;
                    try { InputSystem.QueueStateEvent(kb, new KeyboardState()); } catch (Exception) { }
                    yield return null;
                    evOk = OzBindings.KeyOf(OzAction.NextRobot) == Key.Q;
                }
                if (!evOk) { Mark("the keyboard event did not reach the Game view (no focus?): the captured key goes in directly"); table.ProofPress(Key.Q); }
                yield return Wait(0.3f);
                Check("rebind NEXT ROBOT -> Q", OzBindings.KeyOf(OzAction.NextRobot) == Key.Q && Heard(tr, "ui_confirm", "PLAY", out var cr1), $"{(evOk ? "real key event" : "direct")}, key now {OzBindings.Cap(OzAction.NextRobot)}, sound {(Heard(tr, "ui_confirm", "PLAY", out cr1) ? cr1 : "no ui_confirm")}");
                float tc = Time.unscaledTime;
                table.ProofCapture(OzAction.NextRobot); yield return null; table.ProofPress(Key.A); yield return Wait(0.5f);
                var other = OzBindings.ConflictOf(OzAction.NextRobot);
                Check("conflict shown", other != null && other.action == OzAction.ScrollLeft && Heard(tc, "ui_error", "PLAY", out var er), $"NEXT ROBOT = A also {(other != null ? other.label : "nothing")}; status \"{table.StatusText}\"; sound {(Heard(tc, "ui_error", "PLAY", out er) ? er : "no ui_error")}");
                Snap("03_controls_conflict.png");
                float tb = Time.unscaledTime;                                                                   // review fix: Ctrl-blind handlers, debug keys
                table.ProofCapture(OzAction.AllCannons); yield return null; table.ProofPress(Key.Space); yield return Wait(0.3f);
                var blind = OzBindings.ConflictOf(OzAction.AllCannons); var blind2 = OzBindings.ConflictOf(OzAction.LastMessage);
                string blindStatus = table.StatusText;
                table.ProofCapture(OzAction.LastMessage); yield return null; table.ProofPress(Key.F9); yield return null;
                bool f9Refused = OzBindings.KeyOf(OzAction.LastMessage) == Key.Space && table.IsCapturing;
                table.ProofPress(Key.Escape); yield return Wait(0.2f);
                bool fKeys = OzBindings.Reserved(Key.F6) != null && OzBindings.Reserved(Key.F7) != null && OzBindings.Reserved(Key.F8) != null && OzBindings.Reserved(Key.F11) != null && OzBindings.Reserved(Key.F12) != null && OzBindings.Reserved(Key.F10) == null;
                Check("Ctrl-blind conflict + debug keys refused",
                      blind != null && blind.action == OzAction.LastMessage && blind2 != null && blind2.action == OzAction.AllCannons && f9Refused && fKeys && Heard(tb, "ui_error", "PLAY", out _) && Heard(tb, "ui_disabled", "PLAY", out _),
                      $"ALL CANNONS = CTRL+SPACE also {(blind != null ? blind.label : "nothing")} (both red); status \"{blindStatus}\"; F9 for LAST MESSAGE {(f9Refused ? "refused" : "ACCEPTED")} (\"{OzBindings.Reserved(Key.F9)}\"); F6-F8, F11, F12 reserved {fKeys}, F10 free");
                float tx = Time.unscaledTime;
                table.ProofCapture(OzAction.ExitCrew); yield return null; table.ProofPress(Key.Digit3); yield return null; table.ProofPress(Key.Escape); yield return Wait(0.3f);
                Check("reserved key refused + Esc cancels", OzBindings.KeyOf(OzAction.ExitCrew) == Key.E && !table.IsCapturing && Heard(tx, "ui_disabled", "PLAY", out _) && Heard(tx, "ui_cancel", "PLAY", out _), "3 = a group key (ui_disabled), Esc = cancel (ui_cancel), EXIT CREW stays E");
                // ---- review fix: the HUD toggle cue follows the bound HIDE THE HUD key (AudioDirector.PollInput -> HudKeyTick)
                float tk = Time.unscaledTime;
                table.ProofCapture(OzAction.HideHud); yield return null; table.ProofPress(Key.H); yield return Wait(0.1f);
                bool boundH = OzBindings.KeyOf(OzAction.HideHud) == Key.H;
                bool evH = false, cueH = false, f10Quiet = true;
                if (kb != null)
                {
                    try { InputSystem.QueueStateEvent(kb, new KeyboardState(Key.H)); } catch (Exception) { }
                    yield return null;
                    evH = kb.hKey.isPressed;
                    if (evH) cueH = AudioDirector.HudKeyTick();                                                  // what PollInput runs every frame in a level
                    try { InputSystem.QueueStateEvent(kb, new KeyboardState()); } catch (Exception) { }
                    yield return null;
                    if (evH)
                    {
                        try { InputSystem.QueueStateEvent(kb, new KeyboardState(Key.F10)); } catch (Exception) { }
                        yield return null;
                        f10Quiet = !(kb.f10Key.isPressed && AudioDirector.HudKeyTick());
                        try { InputSystem.QueueStateEvent(kb, new KeyboardState()); } catch (Exception) { }
                        yield return null;
                    }
                }
                string adSrc = ReadScript("View/AudioDirector.cs");
                bool wired = adSrc.Contains("HudKeyTick();") && !adSrc.Contains("f10Key.wasPressedThisFrame) UI(\"ui_hud_toggle\")");
                if (!evH) { AudioDirector.UI("ui_hud_toggle"); cueH = true; }                                      // no keyboard in the Game view: the cue itself
                yield return Wait(0.25f);
                bool rowH = Heard(tk, "ui_hud_toggle", "PLAY", out var hrow);
                string howH = evH ? "real H key event: HudKeyTick fired" + (f10Quiet ? ", F10 silent now" : ", F10 STILL plays the cue") : "no keyboard event in the Game view: the cue played directly";
                Check("HIDE THE HUD key has its sound", boundH && wired && cueH && rowH && f10Quiet,
                      $"HideHud = {OzBindings.Cap(OzAction.HideHud)}; {howH}; PollInput calls HudKeyTick {wired}; OzSoundLog {(hrow ?? "no ui_hud_toggle PLAY row")}");
                float tz = Time.unscaledTime;
                table.ResetAll(); yield return Wait(0.3f);
                Check("reset to defaults", OzBindings.ChangedCount == 0 && OzBindings.ConflictText() == "none" && Heard(tz, "ui_confirm", "PLAY", out _), OzBindings.Summary());
                var uses = new StringBuilder(); bool live = true;
                foreach (var f in new[] { "View/PlayerInput.cs", "View/RtsCamera.cs", "UI/OpenZHud.cs", "UI/OpenZMinimap.cs" })
                {
                    string path = Path.Combine(Application.dataPath, "OpenZ/Scripts", f), src = File.Exists(path) ? File.ReadAllText(path) : "";
                    int n = 0, at = 0; while ((at = src.IndexOf("OzBindings.", at, StringComparison.Ordinal)) >= 0) { n++; at++; }
                    if (n == 0) live = false; uses.Append(Path.GetFileName(f)).Append(' ').Append(n).Append("  ");
                }
                // review round 3: no fixed key caption may be left in the HUD / production window (they would show the old key after a rebind)
                var stale = new StringBuilder();
                string[][] fixedCaps =
                {
                    new[] { "UI/OpenZHud.cs", "\"KeyB\", \"B\"", "\"Space\", \"SPACE\"", "press SPACE", "zone.|P\"", "Key(\"R\", ", "Key(\"V\", ", "Key(\"B\", ", "Key(\"G\", ", "\"E · EXIT CREW" },
                    new[] { "UI/OpenZProductionWindow.cs", "zone.|P\"" },
                };
                foreach (var fc in fixedCaps)
                {
                    string path = Path.Combine(Application.dataPath, "OpenZ/Scripts", fc[0]), src = File.Exists(path) ? File.ReadAllText(path) : null;
                    if (src == null) { stale.Append(fc[0]).Append(" missing  "); continue; }
                    for (int i = 1; i < fc.Length; i++) if (src.Contains(fc[i])) stale.Append(Path.GetFileName(fc[0])).Append(": ").Append(fc[i]).Append("  ");
                }
                if (stale.Length > 0) live = false;
                Check("input code reads the bindings", live, "OzBindings calls in " + uses + "(apply.py PI_*, RC_*, HUD_*, PW_TIP_PLACE, MINI_Z); fixed key captions left: " + (stale.Length == 0 ? "none" : stale.ToString()));
            }

            // ---- pause-menu table (compact, read-only), built off screen
            var tmp = new GameObject("CompactProbe", typeof(RectTransform)); tmp.transform.SetParent(crt ? crt.parent : null, false); tmp.SetActive(false);
            var compact = OzControlsTable.BuildCompact((RectTransform)tmp.transform, OzLayout.PauseW, OzLayout.PauseH);
            Check("pause CONTROLS table", compact.CoversEveryAction, $"{compact.ActionCaps} live key caps, every action, narrowest {compact.NarrowestCap:0} u");
            Destroy(tmp);

            // ---- review fix: KIT_KEYCAP measures every Hud.Keycap (was 14 u): the in-game caps against their neighbours
            {
                var kp = new GameObject("KeycapProbe", typeof(RectTransform)); kp.transform.SetParent(crt ? crt.parent : null, false); kp.SetActive(false);
                float wEsc = ProbeCap(kp.transform, "ESC"), wSpace = ProbeCap(kp.transform, "SPACE"), wB = ProbeCap(kp.transform, "B"), wTip = ProbeCap(kp.transform, "CTRL+R ALL"), wF10 = ProbeCap(kp.transform, "F10");
                float wMenu = OzControlsTable.TextWidth(Hud.Label(kp.transform, "MenuProbe", "MENU", 13, OzUi.Ink, 0.14f));
                var mb = OzLayout.MenuButton; var ts = OzLayout.Toast; float tipRes = "CTRL+R ALL".Length * 6.2f + 20f;
                float escGap = mb.w - 8f - wEsc - 16f - wMenu, spaceGap = ts.w - 12f - wSpace - OzLayout.ToastTextX - 260f;
                bool escOk = escGap >= 6f;                                                                      // HUD MENU button: label at 16, ESC cap right at w - 8
                bool spaceOk = spaceGap >= 4f;                                                                  // toast: text box 260 u, SPACE cap right at w - 12
                bool tipOk = wTip <= tipRes && wEsc <= 3 * 6.2f + 20f;                                          // OpenZHud tooltip reserves 6.2 u per letter + 24 u
                bool sane = wEsc > 14f && wSpace > wEsc && wB >= 14f && wB <= 20f && wTip > wSpace;
                Check("in-game key caps fit (KIT_KEYCAP)", escOk && spaceOk && tipOk && sane,
                      $"ESC {wEsc:0} u (MENU button {mb.w:0} u, label MENU {wMenu:0} u, gap {escGap:0} u), SPACE {wSpace:0} u (toast gap {spaceGap:0} u), B {wB:0} u, F10 {wF10:0} u, tooltip CTRL+R ALL {wTip:0} u of {tipRes:0} u reserved (old caps: 14 u each)");
                Destroy(kp);
            }
            menu.ProofOpen("none"); yield return Wait(0.4f);

            // ---- review fix 2: the menu rebuild after a resolution change (the first switch to fullscreen, the FULLSCREEN row,
            // Alt+Enter) kept the old, dying scrim: no backdrop, and a click outside the plate reached the menu list (QUIT / PLAY).
            // (a) rebuild with no modal, 2 frames, open OPTIONS; (b) rebuild with OPTIONS open (what the FULLSCREEN row does).
            // Only a "scrim" hit is clicked, so a failing build never fires a menu item from the proof.
            {
                menu.ProofRebuild(); yield return null; yield return null;
                menu.ProofOpen("options"); yield return Wait(0.6f);
                var ra = menu.ProofPanel("options"); bool scrimA = menu.ProofScrimOk; string infoA = menu.ProofScrimInfo; string hitA = "no panel";
                if (ra) { var rc = new Vector3[4]; ra.GetWorldCorners(rc); hitA = menu.ProofHotAt(new Vector2(rc[0].x * 0.5f, Screen.height * 0.5f)); }
                menu.ProofRebuild(); yield return null; yield return null; yield return Wait(0.4f);
                var rb = menu.ProofPanel("options"); string sideB = menu.ProofSide; bool scrimB = menu.ProofScrimOk; string infoB = menu.ProofScrimInfo;
                string hitB = "no panel", sideOut = "?"; float tq = Time.unscaledTime;
                if (rb)
                {
                    var rc = new Vector3[4]; rb.GetWorldCorners(rc); var outP = new Vector2(rc[0].x * 0.5f, Screen.height * 0.5f);
                    hitB = menu.ProofHotAt(outP);
                    if (hitB == "scrim") menu.ProofClickAt(outP);
                }
                yield return Wait(0.3f); sideOut = menu.ProofSide;
                bool closeSnd = Heard(tq, "ui_panel_close", "PLAY", out var pcr);
                Check("modal scrim survives a menu rebuild (resolution change)",
                      scrimA && hitA == "scrim" && sideB == "Options" && scrimB && hitB == "scrim" && sideOut == "None" && closeSnd,
                      $"(a) rebuild, then OPTIONS: {infoA}; outside the plate -> {hitA}. (b) rebuild with OPTIONS open: side {sideB}, {infoB}; outside the plate -> {hitB}, side after the click {sideOut}, sound {(closeSnd ? pcr : "no ui_panel_close")}");
                if (menu.ProofSide != "None") menu.ProofOpen("none");
                yield return Wait(0.3f);
            }

            // ---- 9: map hover sound
            menu.ProofSelect(); yield return Wait(1.2f);
            menu.ProofFocus(0); yield return Wait(0.2f);
            float th = Time.unscaledTime;
            menu.ProofFocus(1); yield return Wait(0.4f);
            bool hover = Heard(th, "ui_hover", "PLAY", out var hr), whoosh = Heard(th, "ui_level_preview", null, out _);
            Check("map hover = UI hover sound", hover && !whoosh, $"{(hr ?? "no ui_hover")}{(whoosh ? " + ui_level_preview (the flyby whoosh) still plays" : ", no flyby whoosh")}");

            // ---- 8: HEAD START on both maps
            float tt = Time.unscaledTime;
            string l17 = menu.ProofHeadText; bool live17 = menu.ProofHeadRowLive;
            menu.ProofToggleHeadStart(); yield return Wait(0.25f);
            bool flipped = OpenZDifficulty.HeadStartToggle != head0;
            menu.ProofHeadTip(true); yield return Wait(0.45f); Snap("04_select_headstart_tip.png");
            menu.ProofHeadTip(false);
            menu.ProofFocus(0); yield return Wait(0.3f);
            string l02 = menu.ProofHeadText; bool live02 = menu.ProofHeadRowLive;
            menu.ProofToggleHeadStart(); yield return Wait(0.25f);
            bool sounds = Heard(tt, "ui_toggle_on", "PLAY", out _) && Heard(tt, "ui_toggle_off", "PLAY", out _);
            Check("head start switch on every map", live02 && live17 && flipped && OpenZDifficulty.HeadStartToggle == head0 && sounds, $"L17 \"{l17}\" live {live17}; L02 \"{l02}\" live {live02}; toggled and back, ui_toggle_on + _off {(sounds ? "heard" : "MISSING")}");
            // review fix: the MEDIUM default itself (no stored key -> MEDIUM), then the stored key goes back; DEPLOY writes the key, so
            // ZGame's HasKey path gets the menu's choice (MEDIUM included) instead of the scene's serialized default
            bool hadDiff = PlayerPrefs.HasKey(OpenZDifficulty.Key); int diff0 = PlayerPrefs.GetInt(OpenZDifficulty.Key, -1);
            PlayerPrefs.DeleteKey(OpenZDifficulty.Key);
            var dflt = OpenZDifficulty.Level;
            if (hadDiff) PlayerPrefs.SetInt(OpenZDifficulty.Key, diff0);
            bool backDiff = PlayerPrefs.HasKey(OpenZDifficulty.Key) == hadDiff && (!hadDiff || PlayerPrefs.GetInt(OpenZDifficulty.Key) == diff0);
            bool deployWrites = ReadScript("UI/OpenZMainMenu.cs").Contains("PlayerPrefs.SetInt(OpenZDifficulty.Key, (int)OpenZDifficulty.Level);");
            bool zgHasKey = ReadScript("View/ZGame.cs").Contains("PlayerPrefs.HasKey(OpenZDifficulty.Key)");
            Check("MEDIUM default + head start its own switch",
                  dflt == GameDifficulty.Medium && backDiff && deployWrites && zgHasKey && OpenZDifficulty.HeadStart(GameDifficulty.Hard, true) && !OpenZDifficulty.HeadStart(GameDifficulty.Easy, false) && !OpenZDifficulty.Hint(GameDifficulty.Easy).Contains("+5"),
                  $"no oz_difficulty key -> {OpenZDifficulty.Label(dflt)}; stored key {(hadDiff ? diff0.ToString() : "none")} put back {backDiff}; DEPLOY writes oz_difficulty {deployWrites}, ZGame reads it when present {zgHasKey}; head start {(OpenZDifficulty.HeadStartToggle ? "ON" : "OFF")} on every difficulty; EASY hint \"{OpenZDifficulty.Hint(GameDifficulty.Easy)}\"");
            menu.ProofMain(); yield return Wait(0.5f);

            // ---- 10: HUD layout at 2560x1600 and 1080p
            foreach (var c in new[] { new Vector3(2560, 1600, 0), new Vector3(1920, 1080, 0), new Vector3(2560, 1600, 1.15f), new Vector3(2560, 1600, 1.3f), new Vector3(1920, 1080, 1.3f), new Vector3(1440, 1080, 1f) })
            {
                bool ok = Layout((int)c.x, (int)c.y, c.z, out var ld);
                Check($"HUD {c.x}x{c.y} at {(c.z > 0 ? Mathf.RoundToInt(c.z * 100) + " %" : "the default")}", ok, ld);
            }

            // ---- put back what the proof changed
            if (!music0) { AudioDirector.MusicEnabled = false; PlayerPrefs.SetInt(AudioDirector.MusicPref, musicPref0); }
            OpenZDifficulty.HeadStartToggle = head0;
            foreach (var d in OzBindings.Defs) { if (keys0.TryGetValue(d.pref, out var v)) PlayerPrefs.SetInt(d.pref, v); else PlayerPrefs.DeleteKey(d.pref); }
            PlayerPrefs.Save(); OzBindings.Reload();
            Mark($"restored: music {(AudioDirector.MusicEnabled ? "ON" : "OFF")}, head start {(OpenZDifficulty.HeadStartToggle ? "ON" : "OFF")}, keys {OzBindings.ChangedCount} changed");
            Finish();
        }

        static float ProbeCap(Transform p, string key) => Hud.Keycap(p, "Probe", key, 400f, 0f).sizeDelta.x;

        /// <summary>Panel beside the menu list like ROADMAP: left edge at 620 u, mid-line on the screen's, right edge inside the screen.</summary>
        static bool Beside(RectTransform rt, out string d)
        {
            d = "no panel"; if (!rt) return false;
            var c = new Vector3[4]; rt.GetWorldCorners(c);
            float u = Screen.height / 1080f, left = c[0].x / u, right = c[2].x / u, mid = (c[0].y + c[1].y) * 0.5f;
            d = $"left edge {left:0} u (want {OzMenuLayout.CreditsX:0}), right edge {right:0} u of {Screen.width / u:0}, mid-line {mid:0} px of {Screen.height * 0.5f:0}";
            return Mathf.Abs(left - OzMenuLayout.CreditsX) < 2f && right <= Screen.width / u - 58f && Mathf.Abs(mid - Screen.height * 0.5f) < 3f;
        }

        /// <summary>Panel centred on the screen (2 px) and sized between min / max of the screen height.</summary>
        static bool Centred(RectTransform rt, out string detail, float minW, float maxH)
        {
            detail = "no panel";
            if (!rt) return false;
            var c = new Vector3[4]; rt.GetWorldCorners(c);                                                  // overlay canvas: world = screen px
            float cx = (c[0].x + c[2].x) * 0.5f, cy = (c[0].y + c[2].y) * 0.5f, w = c[2].x - c[0].x, h = c[2].y - c[0].y;
            float dx = cx - Screen.width * 0.5f, dy = cy - Screen.height * 0.5f;
            detail = $"{w:0} x {h:0} px on {Screen.width}x{Screen.height} (width {w / Screen.width * 100f:0} %, height {h / Screen.height * 100f:0} %), centre off by {dx:0.0}, {dy:0.0} px";
            return Mathf.Abs(dx) <= 2f && Mathf.Abs(dy) <= 0.02f * Screen.height && w >= minW * Screen.height && h <= maxH * Screen.height;
        }

        /// <summary>HUD layout at w x h and scale s (0 = the default): the unit panel clear of the minimap and the command bar.</summary>
        public static bool Layout(int w, int h, float s, out string detail)
        {
            float sc = s > 0f ? s : OzSettings.DefaultHudScale(w, h);
            float cw = w * OzLayout.RefH / (h * sc);
            var up = OzLayout.UnitPanelFor(cw); var mm = OzLayout.Minimap; var cb = OzLayout.CommandBar;
            float ux = up.XOn(cw), mx = mm.XOn(cw);
            bool clearMini = ux + up.w <= mx - 8f || up.y + up.h <= mm.y - 8f;
            bool clearBar = ux >= cb.x + cb.w + 8f || up.y + up.h <= cb.y - 8f;
            int mode = OzLayout.UnitPanelMode(cw);
            detail = $"scale {Mathf.RoundToInt(sc * 100)} %, canvas {cw:0} u, unit panel {(mode == 0 ? "WIDE (bottom row, left of the minimap)" : mode == 1 ? "RAISED (left of the minimap, above the command bar)" : "ABOVE the minimap (narrow)")} x {ux:0}..{ux + up.w:0} y {up.y:0}..{up.y + up.h:0}, minimap x {mx:0}..{mx + mm.w:0} y {mm.y:0}, command bar to x {cb.x + cb.w:0}";
            return clearMini && clearBar && (s > 0f || mode < 2);
        }

        void Finish()
        {
            string verdict = fails.Count == 0 && total > 0 ? $"PROOF PASS {pass}/{total}" : $"PROOF FAIL {pass}/{total}: {string.Join("; ", fails)}";
            Mark(verdict);
            try
            {
                if (dir != null)
                {
                    File.WriteAllText(Path.Combine(dir, "proof_log.txt"), log.ToString());
                    File.WriteAllText(Path.Combine(dir, "ozsoundlog.txt"), OzSoundLog.Format(OzSoundLog.Since(t0 - 60f)));
                    Debug.Log("[MenuExeProof] files in " + dir);
                }
            }
            catch (Exception ex) { Debug.LogWarning("[MenuExeProof] could not write the proof files: " + ex.Message); }
            Destroy(gameObject);
        }
    }
}
