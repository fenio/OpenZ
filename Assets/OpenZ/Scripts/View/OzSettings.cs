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
                   $"fullscreen {(Fullscreen ? "ON" : "OFF")}, keys changed {OzBindings.ChangedCount}, deformable ground {On(CraterField.Pref, 1)}, mech foot IK {On(MechWalk.Pref, 1)}, {NightShadows.Describe()}, {OzQuality.Describe()}";
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
        /// own place): below 1440 px 100 %; from 1440 px the largest scale up to 115 % at which the unit panel still fits between
        /// the command bar and the minimap (OzLayout.NarrowCanvas): 109 % at 2560x1600, 115 % at 2560x1440 / 3840x2160.</summary>
        public static float DefaultHudScale(int w, int h)
        {
            if (h < 1440 || w <= 0) return 1f;
            float fit = w * OzLayout.RefH / (h * OzLayout.NarrowCanvas);
            return Mathf.Clamp(Mathf.Floor(fit * 100f + 0.0001f) / 100f, 1f, 1.15f);
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

        public void Apply(string why)
        {
            if (OzSettings.CommandLineOwnsWindow)
            {
                Debug.Log($"[OpenZ Display] {why}: the command line sets the window (-screen-* / -window-mode): left at {Screen.width}x{Screen.height} {Screen.fullScreenMode}");
                return;
            }
            var n = OzSettings.NativeResolution();
            if (OzSettings.Fullscreen)
            {
                if (Screen.fullScreenMode != FullScreenMode.FullScreenWindow || Screen.width != n.x || Screen.height != n.y)
                    Screen.SetResolution(n.x, n.y, FullScreenMode.FullScreenWindow);
            }
            else
            {
                int w = PlayerPrefs.GetInt(OzSettings.WindowWPref, Mathf.RoundToInt(n.x * 0.75f)), h = PlayerPrefs.GetInt(OzSettings.WindowHPref, Mathf.RoundToInt(n.y * 0.75f));
                w = Mathf.Clamp(w, 960, n.x); h = Mathf.Clamp(h, 540, n.y);
                if (Screen.fullScreenMode != FullScreenMode.Windowed) Screen.SetResolution(w, h, FullScreenMode.Windowed);
            }
            ignoreUntil = Time.unscaledTime + 1.5f;
            StartCoroutine(Report(why, n));
        }

        IEnumerator Report(string why, Vector2Int n)
        {
            yield return null; yield return null;
            lastFull = Screen.fullScreen;
            Debug.Log($"[OpenZ Display] {why}: {(Screen.fullScreen ? "FULLSCREEN" : "windowed")} {Screen.width}x{Screen.height} {Screen.fullScreenMode} (native {n.x}x{n.y}, oz_fullscreen {PlayerPrefs.GetInt(OzSettings.FullscreenPref, 1)}, saved)");
        }

        void Update()
        {
            if (Time.unscaledTime < nextCheck) return;
            nextCheck = Time.unscaledTime + 0.5f;
            bool full = Screen.fullScreen;
            if (!full && Time.unscaledTime > ignoreUntil) { PlayerPrefs.SetInt(OzSettings.WindowWPref, Screen.width); PlayerPrefs.SetInt(OzSettings.WindowHPref, Screen.height); }
            if (full == lastFull || Time.unscaledTime < ignoreUntil) { lastFull = full; return; }
            lastFull = full;
            PlayerPrefs.SetInt(OzSettings.FullscreenPref, full ? 1 : 0); PlayerPrefs.Save();
            Debug.Log($"[OpenZ Display] Alt+Enter: {(full ? "FULLSCREEN" : "windowed")} {Screen.width}x{Screen.height}, saved (oz_fullscreen {(full ? 1 : 0)})");
        }
    }
}
