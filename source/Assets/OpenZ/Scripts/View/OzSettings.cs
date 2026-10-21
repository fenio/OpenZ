using System;
using System.Collections;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// <company>\OpenZ) and restored at the next start, plus the display (fullscreen at the native resolution, remembered).
    ///
    /// ROOT CAUSE of the missing music in the exe: the exe's own PlayerPrefs held oz_music = 0. HeatGuardRuntime.
    /// TakeFlag (`-take`, recorded build takes) wrote oz_music 0 into the player's prefs and only put the old value
    /// "[MusicV3] live: ... music OFF (oz_music)" (Player.log, Player-prev.log) and the main menu plays the dusk bed instead of
    /// the menu music (AudioDirector.Start: MusicOn false). The clips were in the build all along (Resources, 45 files, the
    /// MusicV3 line counts 43 loaded tracks), and Options showed only a volume slider (100 %), never the switch that was off.
    /// Fix: -take is a SESSION override now (AudioDirector.SessionMusicOff, never written); the take's difficulty / head start
    /// preselection is snapshotted once (oz_take_saved) and put back at quit OR at the next normal start (a killed take cannot
    /// stick any more); a one-time player repair (oz_settings_v2) turns the stuck music back on and applies the new
    /// defaults (MEDIUM, head start off); Options shows MUSIC ON / OFF.
    /// </summary>
    public static class OzSettings
    {
        public const string FullscreenPref = "oz_fullscreen", RepairPref = "oz_settings_v2", TakeSavedPref = "oz_take_saved";
        public const string WindowWPref = "oz_window_w", WindowHPref = "oz_window_h";
        /// <summary>The RESOLUTION row of OPTIONS (both menus, MAIN tab): its label, its saved value (0 = NATIVE, else the height of
        /// the picture in pixels) and the width of its value text (the longest value, "NATIVE (3840 x 2160)", needs more than the
        /// 170 u of the other value rows).</summary>
        public const string ResLabel = "RESOLUTION", ResPref = "oz_res_h";
        /// <summary>The FULLSCREEN row of OPTIONS (both menus, MAIN tab, directly over RESOLUTION).</summary>
        public const string FullLabel = "FULLSCREEN";
        public const float ResValueW = 240f;

        public static bool TakeArg { get; private set; }
        static bool booted;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            if (booted) return;
            booted = true;
            foreach (var a in Environment.GetCommandLineArgs()) if (!global::OpenZ.OzRelease.IsRelease && a.Equals("-take", StringComparison.OrdinalIgnoreCase)) TakeArg = true;
            if (!TakeArg) TakeRestore("next start");
            if (!Application.isEditor && !TakeArg) RepairOnce();
            CalmDefaultOnce();
            HudScaleDefaultOnce();                                                                       // item 224: the HUD scale default is 100 %; a scale saved by an older version is dropped once
            if (TakeArg) AudioDirector.SessionMusicOff = true;                                             // also when TakeFlag boots after us
            Debug.Log("[OpenZ Settings] restored: " + Describe() + (TakeArg ? " (-take session: music off for this run only, nothing written)" : ""));
            Debug.Log("[OpenZ Settings] keys: " + OzBindings.Summary());
            if (!Application.isEditor)
            {
                var go = new GameObject("OzDisplay") { hideFlags = HideFlags.HideInHierarchy };
                UnityEngine.Object.DontDestroyOnLoad(go);
                go.AddComponent<OzDisplayHost>();
            }
        }

        /// <summary>One line of every saved setting (Player.log proof of item 7).</summary>
        public static string Describe()
        {
            float sc = OzText.SavedScale;
            return $"music {(AudioDirector.MusicEnabled ? "ON" : "OFF")} (oz_music {PlayerPrefs.GetInt(AudioDirector.MusicPref, 1)}), volumes music {Pct("oz_vol_music")} effects {Pct("oz_vol_effects")} voice {Pct("oz_vol_voice")}, " +
                   $"difficulty {OpenZDifficulty.Label(OpenZDifficulty.Level)}, head start {(OpenZDifficulty.HeadStartToggle ? "ON" : "OFF")}, " +
                   $"HUD scale {Mathf.RoundToInt(sc * 100)} %{(PlayerPrefs.HasKey("oz_ui_scale") ? "" : " (default)")}, edge scroll {On("oz_edge_scroll", 1)}, health bars {On("oz_hp_always", 0)}, " +
                   $"shake {On("oz_shake", 1)}, calm screen {On("oz_reduced_motion", 0)}, weather {On("oz_weather", 1)}, team colour {(PlayerPrefs.GetInt("oz_team_blue", 0) == 1 ? "BLUE" : "RED")}, " +
                   $"fullscreen {(Fullscreen ? "ON" : "OFF")}, resolution {ResolutionWord}, keys changed {OzBindings.ChangedCount}, deformable ground {On(CraterField.Pref, 1)}, mech foot IK {On(MechWalk.Pref, 1)}, {NightShadows.Describe()}, {OzQuality.Describe()}";
        }

        static string Pct(string k) => Mathf.RoundToInt(PlayerPrefs.GetFloat(k, 1f) * 100) + " %";
        static string On(string k, int def) => PlayerPrefs.GetInt(k, def) == 1 ? "on" : "off";

        /// <summary>
        /// REDUCED MOTION, oz_reduced_motion) defaults to OFF; once per install a saved ON from before the rename goes back to OFF.</summary>
        static void CalmDefaultOnce()
        {
            if (PlayerPrefs.GetInt("oz_calm_default_v1", 0) >= 1) return;
            bool was = PlayerPrefs.GetInt("oz_reduced_motion", 0) == 1;
            if (was) OzMotion.Reduced = false;
            PlayerPrefs.SetInt("oz_calm_default_v1", 1); PlayerPrefs.Save();
            Debug.Log("[OpenZ Settings] calm screen default OFF (one-time): " + (was ? "was ON -> OFF" : "already OFF"));
        }

        /// <summary>item 224: the HUD scale's default is 100 % from this version on. Once per install a scale saved by an older version is
        /// dropped (the old 109 / 115 % defaults were never saved; the 160 % the owner found is the top preset: the OPTIONS row steps UP
        /// on every click except on its left arrow, so a few clicks ratchet a saved scale 109 -> 115 -> 130 -> 145 -> 160 and each step
        /// is saved), so the game starts at 100 %; a scale set in OPTIONS after that is saved and kept as before.</summary>
        public const string HudScalePref = "oz_hud_scale_default_v3";
        static void HudScaleDefaultOnce()
        {
            if (PlayerPrefs.GetInt(HudScalePref, 0) >= 1) return;
            string was = "none saved";
            if (PlayerPrefs.HasKey("oz_ui_scale"))
            {
                was = Mathf.RoundToInt(PlayerPrefs.GetFloat("oz_ui_scale", 1f) * 100f) + " % saved by an older version -> dropped (set it again in OPTIONS to keep it)";
                PlayerPrefs.DeleteKey("oz_ui_scale");
            }
            PlayerPrefs.SetInt(HudScalePref, 1); PlayerPrefs.Save();
            Debug.Log("[OpenZ Settings] HUD scale default 100 % (one-time): " + was);
        }

        /// <summary></summary>
        static void RepairOnce()
        {
            if (PlayerPrefs.GetInt(RepairPref, 0) >= 1) return;
            var sb = new System.Text.StringBuilder();
            if (PlayerPrefs.GetInt(AudioDirector.MusicPref, 1) == 0) { PlayerPrefs.SetInt(AudioDirector.MusicPref, 1); sb.Append("music back ON (oz_music was left at 0 by a -take run); "); }
            if (OpenZDifficulty.Level != GameDifficulty.Medium) { sb.Append($"difficulty {OpenZDifficulty.Label(OpenZDifficulty.Level)} -> MEDIUM (new default); "); PlayerPrefs.SetInt(OpenZDifficulty.Key, (int)GameDifficulty.Medium); }
            if (PlayerPrefs.GetInt(OpenZDifficulty.HeadStartPref, 0) == 1) { PlayerPrefs.SetInt(OpenZDifficulty.HeadStartPref, 0); sb.Append("head start -> OFF (new default, the toggle adds the vehicles); "); }
            if (PlayerPrefs.HasKey("oz_ui_scale") && Mathf.Abs(PlayerPrefs.GetFloat("oz_ui_scale", 1f) - 1.35f) < 0.001f) { PlayerPrefs.DeleteKey("oz_ui_scale"); sb.Append("HUD scale 135 % (old default) -> the new default; "); }
            PlayerPrefs.SetInt(RepairPref, 1);
            PlayerPrefs.Save();
            Debug.Log("[OpenZ Settings] one-time repair (menu_exe_v1): " + (sb.Length > 0 ? sb.ToString().TrimEnd(' ', ';') : "nothing to change"));
        }

        // ------------------------------------------------------------------ -take (HeatGuardRuntime.TakeFlag)
        /// <summary>Before -take writes its preselection: keep the player's own difficulty / head start ONCE (a second take while
        /// the first one's snapshot is still there keeps the first, so a killed take can never become "the old value").</summary>
        public static void TakeSnapshot()
        {
            if (PlayerPrefs.HasKey(TakeSavedPref)) return;
            PlayerPrefs.SetString(TakeSavedPref, (int)OpenZDifficulty.Level + "," + (OpenZDifficulty.HeadStartToggle ? 1 : 0));
            PlayerPrefs.Save();
        }

        /// <summary>Puts the snapshot back (take quit, or the next normal start after a killed take).</summary>
        public static void TakeRestore(string when)
        {
            if (!PlayerPrefs.HasKey(TakeSavedPref)) return;
            var p = PlayerPrefs.GetString(TakeSavedPref, "").Split(',');
            int d, h;
            if (p.Length == 2 && int.TryParse(p[0], out d) && int.TryParse(p[1], out h))
            {
                PlayerPrefs.SetInt(OpenZDifficulty.Key, d); PlayerPrefs.SetInt(OpenZDifficulty.HeadStartPref, h);
                Debug.Log($"[OpenZ Settings] -take preselection undone ({when}): difficulty {OpenZDifficulty.Label(OpenZDifficulty.Level)}, head start {(h == 1 ? "ON" : "OFF")}");
            }
            PlayerPrefs.DeleteKey(TakeSavedPref);
            PlayerPrefs.Save();
        }

        // ------------------------------------------------------------------ HUD scale steps (both menus)
        /// <summary>Index of the next preset above (dir > 0) or below (dir &lt; 0) `cur`; the saved default (109 % at 2560x1600)
        /// is not a preset, so IndexOf no longer jumps to 90 / 115 %. The steps stop at the ends: the right arrow at the largest preset keeps the
        /// largest one, the left arrow at the smallest preset keeps the smallest (the left arrow steps down: ArrowDir).</summary>
        public static int ScaleStep(float[] presets, float cur, int dir)
        {
            if (presets == null || presets.Length == 0) return 0;
            if (dir > 0) { for (int i = 0; i < presets.Length; i++) if (presets[i] > cur + 0.004f) return i; return presets.Length - 1; }
            if (dir < 0) { for (int i = presets.Length - 1; i >= 0; i--) if (presets[i] < cur - 0.004f) return i; return 0; }
            int best = 0; for (int i = 1; i < presets.Length; i++) if (Mathf.Abs(presets[i] - cur) < Mathf.Abs(presets[best] - cur)) best = i;
            return best;
        }

        /// <summary>Which way a click on a value row steps ("◀  value  ▶", right-aligned text): -1 on the left arrow (from 14 u
        /// left of the text to its middle), else +1 (the right arrow, the value's right half, the rest of the row).</summary>
        public static int ArrowDir(OzText value, Vector2 screenPoint)
        {
            if (value == null || !value.rectTransform) return 1;
            // the text is right-aligned, so its left arrow moves when the width of the value changes: a click on the same row with the
            // pointer not moved repeats the direction of the click before it
            if (value == arrowLastValue && (screenPoint - arrowLastPoint).sqrMagnitude < 4f) return arrowLastDir;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(value.rectTransform, screenPoint, null, out var lp);
            var r = value.rectTransform.rect; float tw = Mathf.Min(value.preferredWidth, r.width);
            int dir = lp.x >= r.xMax - tw - 14f && lp.x < r.xMax - tw * 0.5f ? -1 : 1;
            arrowLastValue = value; arrowLastPoint = screenPoint; arrowLastDir = dir;
            return dir;
        }
        static OzText arrowLastValue; static Vector2 arrowLastPoint; static int arrowLastDir = 1;

        // ------------------------------------------------------------------ HUD scale default (item 10)
        /// <summary>
        /// The earlier rule (below 1440 lines 100 %; from 1440 lines the largest scale up to
        /// 115 % at which the unit panel still fits: 109 % at 2560x1600, 115 % at 2560x1440 / 4K) is gone. A scale the player sets in
        /// OPTIONS is saved (oz_ui_scale) and kept; HudScaleDefaultOnce drops a scale saved by an older version once.</summary>
        public static float DefaultHudScale(int w, int h)
        {
            return 1f;
        }

        // ------------------------------------------------------------------ display (item 1)
        /// <summary>Fullscreen at the display's native resolution (borderless FullScreenWindow, as PlayerSettings), default ON,
        /// remembered. Setting it saves at once and applies in the player (the editor only saves).</summary>
        public static bool Fullscreen
        {
            get => PlayerPrefs.GetInt(FullscreenPref, 1) == 1;
            set
            {
                PlayerPrefs.SetInt(FullscreenPref, value ? 1 : 0); PlayerPrefs.Save();
                if (OzDisplayHost.I) OzDisplayHost.I.Apply(value ? "options: fullscreen ON" : "options: fullscreen OFF");
                else Debug.Log($"[OpenZ Display] fullscreen {(value ? "ON" : "OFF")} saved (applied in the Windows build)");
            }
        }

        // ------------------------------------------------------------------ resolution (the RESOLUTION row of OPTIONS)
        /// <summary>The standard picture heights the row offers below the display's own, tallest first.</summary>
        static readonly int[] ResHeights = { 2160, 1800, 1600, 1440, 1200, 1080, 900, 720 };

        /// <summary>The row's values on a display nativeH pixels tall: 0 (NATIVE) first, then every standard height below the
        /// display's own. Nothing above the display, nothing below 720.</summary>
        public static int[] ResolutionSteps(int nativeH)
        {
            var l = new System.Collections.Generic.List<int> { 0 };
            foreach (int h in ResHeights) if (h < nativeH) l.Add(h);
            return l.ToArray();
        }

        /// <summary>The picture size of a step on a display. NATIVE (0, or a height the display does not offer) is the display's
        /// own size. A lower step has the width of the display's own shape, rounded to an even number, so the picture is never
        /// stretched.</summary>
        public static Vector2Int ResolutionSize(Vector2Int native, int h)
        {
            if (h <= 0 || h >= native.y || Array.IndexOf(ResHeights, h) < 0) return native;
            return new Vector2Int((int)(((long)native.x * h + native.y) / (2L * native.y)) * 2, h);
        }

        /// <summary>The saved step as a height, 0 = NATIVE. No saved value, or one the display does not offer any more (another
        /// monitor), is NATIVE: a player who never touched the row gets the display's own resolution, as before the row.</summary>
        public static int ResolutionHeight(int nativeH)
        {
            int h = PlayerPrefs.GetInt(ResPref, 0);
            return h > 0 && h < nativeH && Array.IndexOf(ResHeights, h) >= 0 ? h : 0;
        }

        /// <summary>The picture size the saved step asks for on a display (NATIVE: the display's own size).</summary>
        public static Vector2Int ResolutionWanted(Vector2Int native) => ResolutionSize(native, ResolutionHeight(native.y));

        /// <summary>For the logs: "NATIVE" or "1920x1080".</summary>
        public static string ResolutionWord
        {
            get { var n = NativeResolution(); var s = ResolutionWanted(n); return s.y >= n.y ? "NATIVE" : s.x + "x" + s.y; }
        }

        static string resName; static int resNameW = -1, resNameH = -1, resNameSaved = -1;
        /// <summary>The row's value: "NATIVE (3840 x 2160)" or "1920 x 1080". While the command line owns the window the row shows
        /// the size the window has.</summary>
        public static string ResolutionName
        {
            get
            {
                int saved = PlayerPrefs.GetInt(ResPref, 0);
                if (resName != null && Screen.width == resNameW && Screen.height == resNameH && saved == resNameSaved) return resName;   // made again only after a step or a change of the screen
                resNameW = Screen.width; resNameH = Screen.height; resNameSaved = saved;
                if (CommandLineOwnsWindow) return resName = Screen.width + " x " + Screen.height;
                var n = NativeResolution(); var s = ResolutionWanted(n);
                return resName = s.y >= n.y ? "NATIVE (" + n.x + " x " + n.y + ")" : s.x + " x " + s.y;
            }
        }

        /// <summary>The row's arrows: dir above 0 = the next lower step, dir under 0 = back towards NATIVE; the steps stop at the
        /// ends. Saved at once and applied in the player (the editor only saves). While the command line owns the window nothing
        /// changes.</summary>
        public static void StepResolution(int dir)
        {
            if (CommandLineOwnsWindow) { Debug.Log($"[OpenZ Display] resolution: the command line sets the window (-screen-* / -window-mode): left at {Screen.width}x{Screen.height}"); return; }
            var n = NativeResolution(); var steps = ResolutionSteps(n.y);
            int cur = Mathf.Max(0, Array.IndexOf(steps, ResolutionHeight(n.y))), to = Mathf.Clamp(cur + (dir < 0 ? -1 : 1), 0, steps.Length - 1);
            if (to == cur) return;
            PlayerPrefs.SetInt(ResPref, steps[to]); PlayerPrefs.Save();
            if (OzDisplayHost.I) OzDisplayHost.I.Apply("options: resolution " + ResolutionWord, true);
            else Debug.Log($"[OpenZ Display] resolution {ResolutionWord} saved (applied in the Windows build)");
        }

        /// <summary>The display's own height while the game fills it with a lower RESOLUTION step, else 0 (OzDisplayHost sets it).
        /// The system draws its pointer in the display's pixels, not in the picture's: with this height the pointer keeps the
        /// size it has at NATIVE (OzKit.HardwareCursorSize).</summary>
        public static int DisplayHeight;

        /// <summary>A -screen-* / -window-mode / -popupwindow argument (capture scripts, build_take.py) owns the window.</summary>
        public static bool CommandLineOwnsWindow
        {
            get
            {
                foreach (var a in Environment.GetCommandLineArgs())
                {
                    var s = a.ToLowerInvariant();
                    if (s.StartsWith("-screen-") || s == "-window-mode" || s == "-popupwindow" || s == "-monitor") return true;
                }
                return false;
            }
        }

        public static Vector2Int NativeResolution()
        {
            try { var di = Screen.mainWindowDisplayInfo; if (di.width > 0 && di.height > 0) return new Vector2Int(di.width, di.height); } catch (Exception) { }
            var d = Display.main;
            if (d != null && d.systemWidth > 0) return new Vector2Int(d.systemWidth, d.systemHeight);
            var r = Screen.currentResolution; return new Vector2Int(Mathf.Max(640, r.width), Mathf.Max(480, r.height));
        }
    }

    /// <summary>menu_exe_v1: applies the saved display mode one frame after the start (and on the Options switch), and keeps
    /// Unity's own Alt+Enter switch remembered (oz_fullscreen follows it).</summary>
    public sealed class OzDisplayHost : MonoBehaviour
    {
        public static OzDisplayHost I { get; private set; }
        bool lastFull; float ignoreUntil, nextCheck;

        void Awake() { I = this; lastFull = Screen.fullScreen; Apply("start"); }                     // before the first scene: no windowed flash

        IEnumerator Start()
        {
            yield return null; yield return null;
            if (!OzSettings.CommandLineOwnsWindow && OzSettings.Fullscreen && !Screen.fullScreen) Apply("start (again: the first switch did not take)");
        }

        /// <summary>resize: the RESOLUTION row was stepped (a window then takes the step's size; fullscreen has it in any case).</summary>
        public void Apply(string why, bool resize = false)
        {
            if (OzSettings.CommandLineOwnsWindow)
            {
                Debug.Log($"[OpenZ Display] {why}: the command line sets the window (-screen-* / -window-mode): left at {Screen.width}x{Screen.height} {Screen.fullScreenMode}");
                return;
            }
            var n = OzSettings.NativeResolution(); var want = OzSettings.ResolutionWanted(n);              // NATIVE (the default): the display's own size, as before the RESOLUTION row
            if (OzSettings.Fullscreen)
            {
                if (Screen.fullScreenMode != FullScreenMode.FullScreenWindow || Screen.width != want.x || Screen.height != want.y)
                    Screen.SetResolution(want.x, want.y, FullScreenMode.FullScreenWindow);                 // borderless as before: a lower step is drawn at its own size and fills the screen
                OzSettings.DisplayHeight = want.y < n.y ? n.y : 0;
            }
            else
            {
                int w = PlayerPrefs.GetInt(OzSettings.WindowWPref, Mathf.RoundToInt(n.x * 0.75f)), h = PlayerPrefs.GetInt(OzSettings.WindowHPref, Mathf.RoundToInt(n.y * 0.75f));
                if (resize) { w = want.x; h = want.y; }                                                     // the row sets the window's size; a window dragged by hand afterwards saves as before (Update)
                w = Mathf.Clamp(w, 960, n.x); h = Mathf.Clamp(h, 540, n.y);
                if (Screen.fullScreenMode != FullScreenMode.Windowed || (resize && (Screen.width != w || Screen.height != h))) Screen.SetResolution(w, h, FullScreenMode.Windowed);
                OzSettings.DisplayHeight = 0;
            }
            ignoreUntil = Time.unscaledTime + 1.5f;
            StartCoroutine(Report(why, n));
        }

        IEnumerator Report(string why, Vector2Int n)
        {
            yield return null; yield return null;
            lastFull = Screen.fullScreen;
            Debug.Log($"[OpenZ Display] {why}: {(Screen.fullScreen ? "FULLSCREEN" : "windowed")} {Screen.width}x{Screen.height} {Screen.fullScreenMode} (native {n.x}x{n.y}, resolution {OzSettings.ResolutionWord}, oz_fullscreen {PlayerPrefs.GetInt(OzSettings.FullscreenPref, 1)}, saved)");
        }

        int seenW, seenH;
        void Update()
        {
            if (Screen.width != seenW || Screen.height != seenH)
            {   // the screen changed size (RESOLUTION, FULLSCREEN, Alt+Enter, a dragged window): the HUD scale's default follows at once
                seenW = Screen.width; seenH = Screen.height;
                if (OpenZHud.I) OpenZHud.I.FollowScreenSize();
            }
            if (Time.unscaledTime < nextCheck) return;
            nextCheck = Time.unscaledTime + 0.5f;
            bool full = Screen.fullScreen;
            if (!full) OzSettings.DisplayHeight = 0;                                                        // a window: one pixel of the picture is one pixel of the display
            if (!full && Time.unscaledTime > ignoreUntil) { PlayerPrefs.SetInt(OzSettings.WindowWPref, Screen.width); PlayerPrefs.SetInt(OzSettings.WindowHPref, Screen.height); }
            if (full == lastFull || Time.unscaledTime < ignoreUntil) { lastFull = full; return; }
            lastFull = full;
            PlayerPrefs.SetInt(OzSettings.FullscreenPref, full ? 1 : 0); PlayerPrefs.Save();
            Debug.Log($"[OpenZ Display] Alt+Enter: {(full ? "FULLSCREEN" : "windowed")} {Screen.width}x{Screen.height}, saved (oz_fullscreen {(full ? 1 : 0)})");
        }
    }
}
