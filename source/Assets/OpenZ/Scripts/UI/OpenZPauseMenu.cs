using System;
using System.Collections.Generic;
using System.Reflection;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace OpenZ
{
    /// <summary>
    /// Pause menu, options and controls (guide 9.12, mock-up mock_pause_options): Esc (or the MENU button) pauses the game
    /// like Zod's pause (ZGame.paused: the sim clock stops; Time.timeScale 0 freezes the animations; UI runs on unscaled
    /// time) and dims the world through a post volume (saturation -55, exposure -1.2, vignette 0.4) plus a GM_950 35 %
    /// overlay. Left: the hazard-lidded pause panel with the PAUSED stencil and RESUME / OPTIONS / CONTROLS / RESTART LEVEL /
    /// QUIT TO MAIN MENU. Right: OPTIONS (game speed 25-400 % like gmm_options.cpp, music / effects / voice volumes, edge
    /// scroll, health bars, screen shake, reduced motion, UI scale) or CONTROLS (the key list). Settings persist in
    /// PlayerPrefs (oz_*). Esc does not open the menu while a production window, the rally tool or cannon placement owns
    /// it (those close first), and never over the end screen. Fixes Zod quirk 1: Esc no longer quits.
    /// </summary>
    [DefaultExecutionOrder(-44)]
    public class OpenZPauseMenu : MonoBehaviour
    {
        public bool IsOpen { get; private set; }
        OpenZHud hud; ZGame game; Canvas canvas; RectTransform root, pausePanel, sidePanel, optionsRoot, controlsRoot, optMainPage;
        CanvasGroup rootCg; Image dim; OzPost post; float openAt = -10f, closeAt = -10f; bool closing, wasPaused; float prevScale = 1f;
        OzText sub; HudButton bResume, bOptions, bControls, bRestart, bQuit, bReport;
        bool showControls;
        readonly List<HudButton> speedBtns = new List<HudButton>();
        public static readonly float[] Speeds = { 0.25f, 0.5f, 0.75f, 1f, 1.5f, 2f, 4f };   // rising; also the steps of the + and - keys (OzSpeedKeys)
        static readonly float[] Scales = OzText.ScalePresets;

        sealed class Slider { public RectTransform track; public Image fill, thumb; public OzText value; public string key, bus; public float v, w; public Action<float> apply; }
        readonly List<Slider> sliders = new List<Slider>(); Slider dragging;
        sealed class Toggle { public HudButton row; public Image knob, track; public OzText state, value; public Func<bool> get; public Action<bool> set; public bool scale, chatter, robots; public string label; public int tab; }
        readonly List<Toggle> toggles = new List<Toggle>();
        int optTab; readonly List<HudButton> optTabBtns = new List<HudButton>();                             // options tabs (OzSettingTips.TabNames)

        /// <summary></summary>
        public HudButton ProofReportButton => bReport;

        public void Build(OpenZHud h)
        {
            hud = h; game = h.game;
            var go = new GameObject("OpenZ_Pause", typeof(RectTransform)) { layer = 5 };
            go.transform.SetParent(h.transform, false);
            canvas = go.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = h.sortingOrder + 5;
            var cs = go.AddComponent<CanvasScaler>(); cs.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            cs.referenceResolution = new Vector2(1920, 1080); cs.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight; cs.matchWidthOrHeight = 1f;
            root = Hud.Node("Root", go.transform); root.Fill();
            rootCg = root.gameObject.AddComponent<CanvasGroup>(); rootCg.blocksRaycasts = false;
            h.AddModalBlocker(root);
            var vig = Hud.Kit(root, "Vignette", "Backdrops/bd_vignette", new OzRect(0, 0, 10, 10), null, Image.Type.Simple); vig.rectTransform.Fill(-100, 0, -100, 0); vig.enabled = OzKit.Available;
            dim = Hud.Img(root, "Dim", OzUi.A(OzUi.GM950, 0.35f)); dim.rectTransform.Fill();
            float cx = OzLayout.RefW * 0.5f;

            // ---- pause panel
            pausePanel = Hud.Node("PausePanel", root).Place(new OzRect(OzAnchor.TC, cx + OzLayout.PauseDX, OzLayout.PauseY, OzLayout.PauseW, OzLayout.PauseH));
            if (OzKit.Available) Hud.Shadow(pausePanel, "Panels/pause_panel_mask");
            var pp = Hud.Kit(pausePanel, "Plate", "Panels/pause_panel", new OzRect(0, 0, OzLayout.PauseW, OzLayout.PauseH), null);
            if (!OzKit.Available) pp.color = HudStyle.Slate;
            var ms = OzKit.GetSprite("Wordmarks/mask_paused");
            if (ms != null)
            {
                float k = OzLayout.PauseWordCap / 118f;                                                      // the stencil mask is cap 118 u at native size
                Hud.Kit(pausePanel, "Paused", "Wordmarks/mask_paused", new OzRect(OzLayout.PauseWordX, OzLayout.PauseWordY, ms.rect.width * 0.5f * k, ms.rect.height * 0.5f * k), OzUi.Ink, Image.Type.Simple);
            }
            else Hud.Label(pausePanel, "Paused", "PAUSED", 77, OzUi.Ink, -0.02f).At(OzLayout.PauseWordX, OzLayout.PauseWordY + 2);
            sub = Hud.Label(pausePanel, "Sub", "", 11, OzUi.Muted, 0.2f, OzStyle.Plain).At(OzLayout.PauseSubX, OzLayout.PauseSubY, OzH.L, false, 400);
            int bi = 0;
            HudButton Big(string label, HudButtonStyle st, Action act)
            {
                var b = HudButton.Make(pausePanel, "Btn" + label, label, new Vector2(OzLayout.PauseBtnW, OzLayout.PauseBtnH), HudButtonKind.Large, 16, 0.14f, st, true, OzH.L);
                b.Rt.Place(new OzRect(OzLayout.PauseBtnX, OzLayout.PauseBtnY0 + OzLayout.PauseBtnStep * bi++, OzLayout.PauseBtnW, OzLayout.PauseBtnH));
                b.Priority = 100; b.OnClick = c => act(); return b;
            }
            bResume = Big("RESUME", HudButtonStyle.Primary, Close);
            Hud.Keycap(bResume.Content, "Esc", "ESC", OzLayout.PauseBtnW - 10, (OzLayout.PauseBtnH - 14) * 0.5f);
            bOptions = Big("OPTIONS", HudButtonStyle.Normal, () => { if (showControls) OzSfx.Ui("ui_tab_switch"); showControls = false; });
            bControls = Big("CONTROLS", HudButtonStyle.Normal, () => { if (!showControls) OzSfx.Ui("ui_tab_switch"); showControls = true; });
            bRestart = Big("RESTART LEVEL", HudButtonStyle.Normal, () => OzConfirm.Ask("RESTART THE LEVEL?", "THE MATCH STARTS OVER FROM THE BEGINNING.", () => OzScenes.Restart(), id => OzSfx.Ui(id), canvas.sortingOrder + 10));
            bQuit = Big("QUIT TO MAIN MENU", HudButtonStyle.Danger, () => OzConfirm.Ask("QUIT TO THE MAIN MENU?", "THIS MATCH ENDS HERE AND ITS PROGRESS IS LOST.", () => { OzSfx.Ui("ui_back"); OzScenes.MainMenu(); }, id => OzSfx.Ui(id), canvas.sortingOrder + 10));
            // system browser (OzFeedback, Application.OpenURL); the game stays paused, nothing else changes. The HUD plays ui_click.
            bReport = HudButton.Make(pausePanel, "BtnReportBug", "REPORT A BUG  ↗", new Vector2(OzLayout.PauseBtnW, 36f), HudButtonKind.Small, 11, 0.16f);
            bReport.Rt.Place(new OzRect(OzLayout.PauseBtnX, OzLayout.PauseBtnY0 + OzLayout.PauseBtnStep * bi + 4f, OzLayout.PauseBtnW, 36f));
            bReport.Priority = 100; bReport.OnClick = c => OzFeedback.Open("pause menu");
            Hud.Label(pausePanel, "Foot", "GAME TIME IS FROZEN WHILE PAUSED", 9, OzUi.Dim, 0.2f, OzStyle.Plain).At(OzLayout.PauseBtnX, OzLayout.PauseFootY);

            // ---- side panel: options / controls
            sidePanel = Hud.Node("SidePanel", root).Place(new OzRect(OzAnchor.TC, cx + OzLayout.OptionsDX, OzLayout.PauseY, OzLayout.PauseW, OzLayout.PauseH));
            if (OzKit.Available) Hud.Shadow(sidePanel, "Panels/panel_lg_mask");
            var op = Hud.Kit(sidePanel, "Plate", "Panels/options_panel", new OzRect(0, 0, OzLayout.PauseW, OzLayout.PauseH), null);
            if (!OzKit.Available) op.color = HudStyle.Slate;
            optionsRoot = Hud.Node("Options", sidePanel); optionsRoot.Fill();
            controlsRoot = Hud.Node("Controls", sidePanel); controlsRoot.Fill();
            BuildOptions();
            BuildControls();
            go.SetActive(false);
            ApplySaved();
        }

        // ------------------------------------------------------------------ options (guide 9.12)
        void BuildOptions()
        {
            var p = optionsRoot; float x = OzLayout.OptX, w = OzLayout.PauseW - 2 * OzLayout.OptX;
            Hud.Label(p, "Title", "OPTIONS", 16, OzUi.Ink, 0.16f).At(x, 15);
            var mp = optMainPage = Hud.Node("TabMain", p); mp.Fill(0f, -OzLayout.OptTabShift, 0f, OzLayout.OptTabShift);   // options tabs: MAIN's fixed controls sit on their own page under the tab strip
            Hud.Label(mp, "SpeedLabel", "GAME SPEED", 10, OzUi.Muted, 0.22f, OzStyle.Plain).At(x, OzLayout.OptSpeedLabelY);
            float sw = w / Speeds.Length;
            for (int i = 0; i < Speeds.Length; i++)
            {
                float sp = Speeds[i];
                var b = HudButton.Make(mp, "Speed" + i, Mathf.RoundToInt(sp * 100) + "%", new Vector2(sw - 4, OzLayout.OptSpeedH), HudButtonKind.Small, OzLayout.OptRowText, 0.04f);
                b.Rt.Place(new OzRect(x + i * sw, OzLayout.OptSpeedY, sw - 4, OzLayout.OptSpeedH));
                b.Priority = 100; b.OnClick = c => { game.gameSpeed = sp; PlayerPrefs.SetFloat("oz_speed", sp); };
                speedBtns.Add(b);
            }
            AddSlider(mp, 0, 0, "MUSIC", "oz_vol_music", "Music", v => OzAudio.Set("Music", v), OzSettingTips.Music);
            AddSlider(mp, 0, 1, "EFFECTS · BATTLE", "oz_vol_effects", "SFX", v => OzAudio.Set("SFX", v), OzSettingTips.Effects);
            AddSlider(mp, 1, 0, "ANNOUNCER", "oz_vol_voice", "Announcer", v => OzAudio.Set("Announcer", v), OzSettingTips.Announcer);
            AddSlider(mp, 1, 1, "ROBOT VOICES", "oz_vol_robots", "Robots", v => OzAudio.Set("Robots", v), OzSettingTips.Robots);
            AddToggle(p, 0, "EDGE SCROLL", () => PlayerPrefs.GetInt("oz_edge_scroll", 1) == 1, v => { PlayerPrefs.SetInt("oz_edge_scroll", v ? 1 : 0); ApplyEdgeScroll(); });
            AddToggle(p, 1, "HEALTH BARS ON EVERY DAMAGED UNIT", () => OpenZUnitOverlay.AlwaysBars, v => OpenZUnitOverlay.AlwaysBars = v);
            AddToggle(p, 2, "CAMERA SHAKE ON EXPLOSIONS", () => PlayerPrefs.GetInt("oz_shake", 1) == 1, v => PlayerPrefs.SetInt("oz_shake", v ? 1 : 0));
            AddToggle(p, 3, OzSettingTips.CalmLabel, () => OzMotion.Reduced, v => OzMotion.Reduced = v);
            AddToggle(p, 4, "UI SCALE", null, null, true);
            AddToggle(p, 5, "ANNOUNCER CHATTER", null, null, true, true);
            AddToggle(p, 6, OzSettingTips.RobotChatterLabel, null, null, true, true, true);
            AddToggle(p, 7, OzNightRain.Label, () => OzWeather.Enabled, v => OzWeather.Enabled = v);   // NIGHT & RAIN (oz_weather, default on; off = always day, no rain: OzNightRain.cs)
            AddToggle(p, 8, "HIT FLASH ON DAMAGED UNITS", () => OpenZDamageStates.HitFlash, v => OpenZDamageStates.HitFlash = v);
            AddToggle(p, 9, "MOTION BLUR · FAST DEBRIS AND SHELLS", () => UnitLookMotionGate.MotionBlurOn, v => UnitLookMotionGate.MotionBlurOn = v);
            AddToggle(p, 10, OzSettingTips.PathV3Label, () => OzSettingTips.PathV3, v => OzSettingTips.PathV3 = v);
            AddToggle(p, toggles.Count, OzOvertake.Label, () => OzOvertake.On, v => OzOvertake.On = v);     // DV105 FAST UNITS PASS SLOW ONES: oz_overtake, default OFF, from the next match (OzOvertake.cs)
            AddToggle(p, 11, OzSettingTips.MinimalLabel, () => OzSettingTips.MinimalUi, v => OzSettingTips.MinimalUi = v);
            AddToggle(p, toggles.Count, OzFreeCam.Label, () => OzFreeCam.On, v => OzFreeCam.On = v);     // FREE CAMERA: oz_free_camera, default OFF, live (OzFreeCam.cs)
            AddToggle(p, toggles.Count, NightShadows.Label, null, null, true);
            { var ns = toggles[toggles.Count - 1]; ns.row.OnClick = c => { NightShadows.Step(c.right || c.shift ? -1 : Arrow(ns)); OzSfx.Ui("ui_tab_switch"); }; ns.row.OnRightClick = c => NightShadows.Step(-1); }
            AddToggle(p, toggles.Count, CraterField.Label, () => CraterField.Enabled, v => CraterField.Enabled = v);
            AddToggle(p, toggles.Count, OzQuality.Label, null, null, true);
            { var gq = toggles[toggles.Count - 1]; gq.row.OnClick = c => { OzQuality.Step(c.right || c.shift ? -1 : Arrow(gq)); OzSfx.Ui("ui_tab_switch"); }; gq.row.OnRightClick = c => OzQuality.Step(-1); }
            AddToggle(p, toggles.Count, OzFpsCap.Label, null, null, true);                                  // FRAME RATE LIMIT: 30 / 60 (oz_fps_cap, default 60, saved and applied at once)
            { var fc = toggles[toggles.Count - 1]; fc.row.OnClick = c => { OzFpsCap.Step(c.right || c.shift ? -1 : Arrow(fc)); OzSfx.Ui("ui_tab_switch"); }; fc.row.OnRightClick = c => OzFpsCap.Step(-1); }
            AddToggle(p, toggles.Count, OzQuality.FpsLabel, () => OzQuality.FpsOn, v => OzQuality.FpsOn = v);
            AddToggle(p, toggles.Count, OzTouch.Label, null, null, true);                                   // touch and gamepad: AUTO / TOUCH ON / OFF (oz_touch, live; OzTouch.cs)
            { var tg = toggles[toggles.Count - 1]; tg.row.OnClick = c => { OzTouch.StepMode(c.right || c.shift ? -1 : Arrow(tg)); OzSfx.Ui("ui_tab_switch"); }; tg.row.OnRightClick = c => OzTouch.StepMode(-1); }
            AddToggle(p, toggles.Count, OzTrackpadScroll.Label, null, null, true);                          // TRACKPAD SCROLL: ZOOM (default) / MOVE MAP, oz_trackpad_scroll, live (OzTrackpadScroll.cs)
            { var tp = toggles[toggles.Count - 1]; tp.row.OnClick = c => { OzTrackpadScroll.Step(c.right || c.shift ? -1 : Arrow(tp)); OzSfx.Ui("ui_tab_switch"); }; tp.row.OnRightClick = c => OzTrackpadScroll.Step(-1); }
            AddToggle(p, toggles.Count, MechWalk.Label, () => MechWalk.On, v => MechWalk.On = v);
            AddToggle(p, toggles.Count, OzOverlapOption.Label, null, null, true);                          // FRIENDLY UNITS OVERLAP: NO (default) / YES, oz_overlap, live (OzOverlapOption.cs); the cycle form of this menu
            { var ov = toggles[toggles.Count - 1]; ov.row.OnClick = c => { OzOverlapOption.Step(c.right || c.shift ? -1 : Arrow(ov)); OzSfx.Ui("ui_tab_switch"); }; ov.row.OnRightClick = c => OzOverlapOption.Step(-1); }
            AddToggle(p, toggles.Count, OzHudLayout.Label, null, null, true);                               // ARRANGE INTERFACE: this menu closes, then the panels can be dragged (OpenZHud.Arrange.cs)
            { var ar = toggles[toggles.Count - 1]; ar.row.Tooltip = OzHudLayout.Tip; ar.row.OnClick = c => { Close(); hud.RequestArrange(); }; ar.row.OnRightClick = null; }
            AddToggle(p, toggles.Count, OzSettings.FullLabel, () => OzSettings.Fullscreen, v => OzSettings.Fullscreen = v);   // FULLSCREEN, directly over RESOLUTION as in the main menu (oz_fullscreen, saved and applied at once)
            AddToggle(p, toggles.Count, OzSettings.ResLabel, null, null, true);                             // RESOLUTION: NATIVE (default) and the lower steps of this display (oz_res_h, OzSettings), saved and applied at once
            { var rs = toggles[toggles.Count - 1]; rs.value.At(OzLayout.PauseW - 2 * OzLayout.OptX - 16, OzLayout.OptToggleH * 0.5f, OzH.R, true, OzSettings.ResValueW); rs.row.OnClick = c => { OzSettings.StepResolution(c.right || c.shift ? -1 : Arrow(rs)); OzSfx.Ui("ui_tab_switch"); }; rs.row.OnRightClick = c => OzSettings.StepResolution(-1); }
            AddToggle(p, toggles.Count, OzStrikeGroups.Label, () => OzStrikeGroups.On, v => OzStrikeGroups.On = v);   // DV107 enemy strike groups: oz_strike_groups, default ON, live (ADVANCED tab, first row; OzStrikeGroups.cs)
            if (OzShuttleStart.InOptions)                                                                      // K347: the SHUTTLE switch is the battlefield screen's box now (UI/OpenZMainMenu.ShuttleStart.cs): the old OPTIONS row below is not built
            AddToggle(p, toggles.Count, OzShuttleOption.Label, () => OzShuttleOption.On, v => OzShuttleOption.On = v);   // DV118 the level 16 shuttle: oz_shuttle, default ON, read at the next level start (OzShuttleOption.cs)
            AddToggle(p, toggles.Count, OzShuttleOrdersOption.Label, null, null, true);                    // K256g SHUTTLE ORDERS: LANDABLE SPOTS ONLY (default) / ANYWHERE, oz_shuttle_orders, live (OzShuttleOrdersOption.cs); ADVANCED, under SHUTTLE
            { var so = toggles[toggles.Count - 1]; so.value.At(OzLayout.PauseW - 2 * OzLayout.OptX - 16, OzLayout.OptToggleH * 0.5f, OzH.R, true, OzSettings.ResValueW); so.row.OnClick = c => { OzShuttleOrdersOption.Step(c.right || c.shift ? -1 : Arrow(so)); OzSfx.Ui("ui_tab_switch"); }; so.row.OnRightClick = c => OzShuttleOrdersOption.Step(-1); }
            foreach (var t in toggles) if (string.IsNullOrEmpty(t.row.Tooltip)) t.row.Tooltip = OzSettingTips.For(t.label);
            BuildOptTabs(p);                                                                                 // options tabs: the strip, every tab's rows from the top (no row squeezing any more)
            foreach (var b in speedBtns) b.Tooltip = OzSettingTips.Speed;
        }

        void AddSlider(RectTransform p, int row, int col, string label, string key, string bus, Action<float> apply, string tip = null)
        {
            float full = OzLayout.PauseW - 2 * OzLayout.OptX, w = (full - OzLayout.OptSliderGap) * 0.5f;
            float x = OzLayout.OptX + col * (w + OzLayout.OptSliderGap), y = OzLayout.OptSliderY0 + OzLayout.OptSliderStep * row;
            int j = row * 2 + col;
            var s = new Slider { key = key, bus = bus, apply = apply, v = PlayerPrefs.GetFloat(key, 1f), w = w };      // 100 % = the audio track's own mix (EFFECTS: 50 % is the mix, 100 % is 6 dB over it, OzAudio.EffectsLin)
            Hud.Label(p, "Label" + j, label, 10, OzUi.Muted, 0.22f, OzStyle.Plain).At(x, y);
            s.value = Hud.Label(p, "Value" + j, "", 11, OzUi.Ink, 0f, OzStyle.Plain).At(x + w, y, OzH.R, false, 60);
            s.track = Hud.Node("Track" + j, p).Place(new OzRect(x, y + 20, w, 8));
            var tr = Hud.Kit(s.track, "Bg", "Bars/bar_track", new OzRect(0, 0, w, 8), null); tr.rectTransform.Fill();
            if (!OzKit.Available) tr.color = OzUi.GM950;
            s.fill = OzKit.Available ? OzKit.FillBar(s.track, "Fill", "amber") : Hud.Img(s.track, "Fill", OzUi.Amber);
            s.fill.rectTransform.Fill(1, 1, 1, 1); s.fill.type = Image.Type.Filled; s.fill.fillMethod = Image.FillMethod.Horizontal;
            s.thumb = Hud.Kit(s.track, "Thumb", "Buttons/slider_thumb", new OzRect(0, -6, 14, 20), OzKit.Available ? (Color?)null : OzUi.Ink, Image.Type.Simple);
            var hot = HudButton.Make(p, "SliderHot" + j, null, new Vector2(w + 8, 30), HudButtonKind.Small);
            hot.Rt.Place(new OzRect(x - 4, y + 10, w + 8, 30)); hot.Bg.enabled = false; hot.Priority = 100; hot.Tooltip = tip;
            if (hot.Hatch) { Destroy(hot.Hatch.gameObject); hot.Hatch = null; }
            sliders.Add(s);
            ShowSlider(s);
        }

        void ShowSlider(Slider s)
        {
            float w = s.w > 0f ? s.w : OzLayout.PauseW - 2 * OzLayout.OptX;
            s.fill.fillAmount = s.v;
            s.thumb.rectTransform.anchoredPosition = new Vector2(w * s.v - 7f, 6f);
            s.value.Set(OzText.Tab(Mathf.RoundToInt(s.v * 100) + "%"));
        }

        void AddToggle(RectTransform p, int j, string label, Func<bool> get, Action<bool> set, bool scale = false, bool chatter = false, bool robots = false)
        {
            float x = OzLayout.OptX, w = OzLayout.PauseW - 2 * OzLayout.OptX, y = OzLayout.OptToggleY0 + OzLayout.OptToggleStep * j;
            var t = new Toggle { get = get, set = set, scale = scale, chatter = chatter, robots = robots, label = label, tab = OzSettingTips.TabOf(label) };
            t.row = HudButton.Make(p, "Toggle" + j, null, new Vector2(w, OzLayout.OptToggleH), HudButtonKind.Small);
            t.row.Rt.Place(new OzRect(x, y, w, OzLayout.OptToggleH)); t.row.Priority = 100;
            Hud.Label(t.row.Content, "Label", label, OzLayout.OptRowText, OzUi.Ink, 0.1f, OzStyle.Plain).At(16, OzLayout.OptToggleH * 0.5f, OzH.L, true, 300);
            if (scale)
            {
                t.value = Hud.Label(t.row.Content, "Value", "", OzLayout.OptRowText, OzUi.Ink2, 0.06f, OzStyle.Plain).At(w - 16, OzLayout.OptToggleH * 0.5f, OzH.R, true, 170);
                if (chatter && robots)
                {
                    t.row.Tooltip = OzSettingTips.RobotChatter;
                    t.row.OnClick = c => { AudioDirector.RobotChatter.Level = (AudioDirector.RobotChatter.Level + (c.right || c.shift || Arrow(t) < 0 ? 2 : 1)) % 3; OzSfx.Ui("ui_tab_switch"); };
                    t.row.OnRightClick = c => AudioDirector.RobotChatter.Level = (AudioDirector.RobotChatter.Level + 2) % 3;
                }
                else if (chatter)
                {
                    t.row.Tooltip = "ANNOUNCER CHATTER|Full: every Rachel line (default). Less: only important lines, longer gaps. Minimal: only critical lines (fort under attack, victory, defeat, territory).|";
                    t.row.OnClick = c => { AudioDirector.Chatter.Level = (AudioDirector.Chatter.Level + (c.right || c.shift || Arrow(t) < 0 ? 2 : 1)) % 3; OzSfx.Ui("ui_tab_switch"); };
                    t.row.OnRightClick = c => AudioDirector.Chatter.Level = (AudioDirector.Chatter.Level + 2) % 3;
                }
                else
                {
                t.row.OnClick = c => { StepScale(c.right || c.shift ? -1 : Arrow(t)); OzSfx.Ui("ui_tab_switch"); };
                t.row.OnRightClick = c => StepScale(-1);
                }
            }
            else
            {
                t.track = Hud.Kit(t.row.Content, "Track", "Buttons/switch_track", new OzRect(w - 16 - 48, (OzLayout.OptToggleH - 18) * 0.5f, 48, 18), null);
                if (!OzKit.Available) t.track.color = OzUi.GM950;
                t.knob = Hud.Kit(t.row.Content, "Knob", "Buttons/switch_knob", new OzRect(0, (OzLayout.OptToggleH - 14) * 0.5f, 22, 14), OzUi.Amber, Image.Type.Simple);
                t.state = Hud.Label(t.row.Content, "State", "", 9, OzUi.Amber, 0.2f, OzStyle.Plain).At(w - 16 - 48 - 8, OzLayout.OptToggleH * 0.5f, OzH.R, true, 40);
                t.row.OnClick = c => { bool v = !t.get(); t.set(v); PlayerPrefs.Save(); OzSfx.Ui(v ? "ui_toggle_on" : "ui_toggle_off"); };
            }
            toggles.Add(t);
        }

        // ------------------------------------------------------------------ options tabs (OzSettingTips.TabNames: MAIN / GAME / GRAPHICS / ADVANCED)
        /// <summary>The tab strip under the title. Every row carries the tab of its label (OzSettingTips.TabOf, the main menu's table);
        /// each tab's rows are placed from the top in the table's order and only the open tab's rows are active. MAIN also holds
        /// the game speed row and the four volume sliders (the TabMain page).</summary>
        void BuildOptTabs(RectTransform p)
        {
            float x = OzLayout.OptX, w = OzLayout.PauseW - 2 * OzLayout.OptX; int n = OzSettingTips.TabNames.Length;
            float bw = (w - OzLayout.OptTabGap * (n - 1)) / n;
            optTabBtns.Clear();
            for (int i = 0; i < n; i++)
            {
                int id = i;
                var b = HudButton.Make(p, "Tab" + i, OzSettingTips.TabNames[i], new Vector2(bw, OzLayout.OptTabH), HudButtonKind.Small, 11, 0.14f);
                b.Rt.Place(new OzRect(x + i * (bw + OzLayout.OptTabGap), OzLayout.OptTabY, bw, OzLayout.OptTabH));
                b.Priority = 100; b.OnClick = c => { if (optTab != id) OzSfx.Ui("ui_tab_switch"); ShowOptTab(id); };   // the HUD adds ui_click
                optTabBtns.Add(b);
            }
            Hud.Img(p, "TabRule", OzUi.A(OzUi.Amber, 0.35f)).rectTransform.Place(new OzRect(x, OzLayout.OptTabY + OzLayout.OptTabH + 3f, w, 1f));
            for (int tab = 0; tab < n; tab++)
            {
                var rows = toggles.FindAll(t => t.tab == tab);
                rows.Sort((r1, r2) => { int o = OzSettingTips.TabOrder(r1.label).CompareTo(OzSettingTips.TabOrder(r2.label)); return o != 0 ? o : toggles.IndexOf(r1).CompareTo(toggles.IndexOf(r2)); });
                float top = tab == OzSettingTips.TabMain ? OzLayout.OptToggleY0 + OzLayout.OptTabShift : OzLayout.OptTabRowY0;
                for (int k = 0; k < rows.Count; k++) rows[k].row.Rt.Place(new OzRect(x, top + OzLayout.OptToggleStep * k, w, OzLayout.OptToggleH));
            }
            ShowOptTab(Mathf.Clamp(optTab, 0, n - 1));
        }

        /// <summary>Open one tab: its rows and (MAIN) the speed + volume page on, the rest off; the tab's button lights.</summary>
        void ShowOptTab(int tab)
        {
            optTab = tab; dragging = null;
            foreach (var t in toggles) t.row.Rt.gameObject.SetActiveFast(t.tab == tab);
            if (optMainPage) optMainPage.gameObject.SetActiveFast(tab == OzSettingTips.TabMain);
            for (int i = 0; i < optTabBtns.Count; i++) if (optTabBtns[i].Selected != (i == tab)) { optTabBtns[i].Selected = i == tab; optTabBtns[i].Refresh(); }
        }

        /// <summary>Proof hooks: open an options tab (with the OPTIONS page shown), the open tab, the labels of the rows shown now.</summary>
        public void ProofOptTab(int tab) { showControls = false; if (optTabBtns.Count > 0) ShowOptTab(Mathf.Clamp(tab, 0, optTabBtns.Count - 1)); }
        /// <summary>Pause the game and show OPTIONS on one tab (the slow-computer plate opens GRAPHICS this way).</summary>
        public void OpenOptions(int tab) { Open(); if (!IsOpen) return; showControls = false; if (optTabBtns.Count > 0) ShowOptTab(Mathf.Clamp(tab, 0, optTabBtns.Count - 1)); }
        public int ProofOptTabOpen => optTab;
        public string ProofOptTabRows
        {
            get
            {
                var sb = new System.Text.StringBuilder();
                foreach (var t in toggles) if (t.row.Rt.gameObject.activeSelf) { if (sb.Length > 0) sb.Append(" | "); sb.Append(t.label); }
                return sb.ToString();
            }
        }

        /// <summary>The step of a click on a value row: -1 on its left arrow, else +1 (OzSettings.ArrowDir).</summary>
        static int Arrow(Toggle t) { var m = Mouse.current; return m == null ? 1 : OzSettings.ArrowDir(t.value, m.position.ReadValue()); }

        void StepScale(int dir)
        {
            int i = OzSettings.ScaleStep(Scales, OzText.UiScale, dir);                                     // menu_exe_v1: the 109 % default is no preset
            OzText.UiScale = Scales[i]; PlayerPrefs.SetFloat("oz_ui_scale", Scales[i]); PlayerPrefs.Save();
            hud.ApplyUiScale();
        }

        // ------------------------------------------------------------------ controls (Zod key table + OpenZ camera keys)
        static readonly (string key, string what)[] Keys =
        {
            ("LEFT CLICK", "Select a unit · drag: box select"), ("SHIFT+CLICK", "Add to / remove from the selection"), ("DOUBLE-CLICK", "All units of that type on screen"),
            ("RIGHT CLICK", "Order: move, attack, capture, enter, repair"), ("SHIFT+RIGHT", "Queue waypoints (release Shift to send)"), ("CTRL / ALT+RIGHT", "Attack-move / plain move"),
            ("Z+RIGHT", "Only the unit nearest to the point"), ("1 .. 0", "Recall a group (again: fly there)"), ("CTRL+1 .. 0", "Store the selection as a group"),
            ("R  V  G", "Next robot / vehicle / gun"), ("CTRL+R V C A", "All robots / vehicles / cannons / units"), ("B", "Factory list · C: place a stored cannon"),
            ("WASD / ARROWS", "Scroll · edge scroll · middle drag flies"), ("WHEEL", "Zoom in and out"), ("SPACE", "Go to the last message"), ("F10 · ESC", "Hide the HUD · this menu"),
        };

        void BuildControls()
        {
            var p = controlsRoot;
            OzControlsTable.BuildCompact(p, OzLayout.PauseW, OzLayout.PauseH);                              // menu_exe_v1: measured key caps, the live bindings (read-only here)
        }

        // ------------------------------------------------------------------ open / close
        public void Open()
        {
            if (IsOpen || hud == null || (hud.EndScreen && hud.EndScreen.IsShown)) return;
            IsOpen = true; closing = false; openAt = OzMotion.Now;
            wasPaused = game.paused; game.paused = true;
            prevScale = Time.timeScale; Time.timeScale = 0f;
            if (hud.Production) hud.Production.Close();
            // The whole in-game HUD (selection panel, minimap,
            // top bar, build bar, toasts, tooltips, unit overlays and labels) goes away while the pause menu is up and comes back once
            // the menu has faded out on resume. A HUD the player hid himself (F10) stays hidden.
            if (!hidHud) { hidHud = hud.hudVisible; if (hidHud) hud.SetVisible(false); }
            post ??= new OzPost("OZ_Pause", -55f, -1.2f, 0.4f, 55);
            canvas.gameObject.SetActive(true);
            optionsRoot.gameObject.SetActiveFast(!showControls); controlsRoot.gameObject.SetActiveFast(showControls);
            string lvl = game.level == "L17" ? "L17 · CAR PARK" : game.level == "L16" ? "L16 · JUNGLE" : game.level == "L02" ? "L02 · DESERT" : game.level;
            sub.Set(OzText.Tab($"OPERATION {lvl} · {UnitInfo.Clock(game.World.Time)}"));
            RefreshValues();                                                                             // ui_pause / ui_resume: the audio track follows game.paused
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false; closing = true; closeAt = OzMotion.Now;
            PlayerPrefs.Save();                                                                              // menu_exe_v1: sliders / speed saved when the menu closes
            game.paused = wasPaused;
            Time.timeScale = prevScale > 0f ? prevScale : 1f;
        }

        void Update()
        {
            if (hud == null || game == null || game.World == null) return;
            var kb = Keyboard.current; var m = Mouse.current;
            bool esc = kb != null && kb.escapeKey.wasPressedThisFrame && !OzConfirm.Blocks;
            if (esc)
            {
                if (IsOpen) Close();
                else if (!(hud.EndScreen && hud.EndScreen.IsShown) && !(hud.Production && hud.Production.IsOpen) && !(hud.Rally && hud.Rally.Armed) && hud.input.Placing == null && !hud.ArrangeOwnsEsc) Open();
            }
            ApplyShake();
            if (Time.unscaledTime < audioRetryUntil && Time.unscaledTime > nextAudioRetry) { nextAudioRetry = Time.unscaledTime + 2f; OzAudio.Apply(); }   // the audio table may load after the HUD
            if (IsOpen)
            {
                float k = OzEase.OutCubic(OzMotion.T(openAt, OzMotion.Dur(OzMotion.Short)));
                rootCg.alpha = k; if (post != null) post.Weight = k;
                pausePanel.localScale = sidePanel.localScale = Vector3.one * Mathf.Lerp(0.96f, 1f, k);
                optionsRoot.gameObject.SetActiveFast(!showControls); controlsRoot.gameObject.SetActiveFast(showControls);
                if (bOptions.Selected != !showControls) { bOptions.Selected = !showControls; bOptions.Refresh(); }
                if (bControls.Selected != showControls) { bControls.Selected = showControls; bControls.Refresh(); }
                if (!OzConfirm.Blocks) DragSliders(m);
                RefreshValues();
            }
            else if (closing)
            {
                float k = OzMotion.T(closeAt, OzMotion.Dur(0.1f, true));
                rootCg.alpha = 1f - k; if (post != null) post.Weight = 1f - k;
                if (k >= 1f) { closing = false; canvas.gameObject.SetActive(false); RestoreHud(); }
            }
        }

        /// <summary>The HUD this menu hid on Open comes back (after the close fade, so it never shows under the fading panel).</summary>
        bool hidHud;
        void RestoreHud() { if (!hidHud) return; hidHud = false; if (hud && !(hud.EndScreen && hud.EndScreen.IsShown)) hud.SetVisible(true); }

        void DragSliders(Mouse m)
        {
            if (m == null) return;
            Vector2 mp = m.position.ReadValue();
            if (m.leftButton.wasPressedThisFrame && optMainPage && optMainPage.gameObject.activeInHierarchy)   // the sliders are on the MAIN tab only, and never under the CONTROLS page
                foreach (var s in sliders)
                {
                    RectTransformUtility.ScreenPointToLocalPointInRectangle(s.track, mp, null, out var lp);
                    var r = s.track.rect;
                    if (lp.x > r.xMin - 10 && lp.x < r.xMax + 10 && lp.y > r.yMin - 12 && lp.y < r.yMax + 12) { dragging = s; break; }
                }
            if (dragging != null)
            {
                if (!m.leftButton.isPressed) { PlayerPrefs.SetFloat(dragging.key, dragging.v); OzSoundV3.SliderPreview(dragging.bus == "Music" ? null : dragging.bus); dragging = null; return; }   // sound_v3
                RectTransformUtility.ScreenPointToLocalPointInRectangle(dragging.track, mp, null, out var lp);
                var r = dragging.track.rect;
                float v = Mathf.Clamp01((lp.x - r.xMin) / Mathf.Max(1f, r.width));
                if (!Mathf.Approximately(v, dragging.v)) { if (Mathf.FloorToInt(v * 10) != Mathf.FloorToInt(dragging.v * 10)) OzSfx.Ui("ui_slider_tick"); dragging.v = v; dragging.apply(v); ShowSlider(dragging); }
            }
        }

        int scaleShown = -1;
        void RefreshValues()
        {
            for (int i = 0; i < speedBtns.Count; i++)
            {
                bool on = Mathf.Abs(game.gameSpeed - Speeds[i]) < 0.01f;
                if (speedBtns[i].Selected != on) { speedBtns[i].Selected = on; speedBtns[i].Refresh(); }
            }
            foreach (var t in toggles)
            {
                if (t.label == OzHudLayout.Label) { t.value.Set("DRAG THE PANELS  ▶"); continue; }
                if (t.label == NightShadows.Label) { t.value.Set(OzText.Tab($"◀  {NightShadows.Name}  ▶")); continue; }
                if (t.label == OzTouch.Label) { t.value.Set(OzText.Tab($"◀  {OzTouch.ModeName}  ▶")); continue; }
                if (t.label == OzTrackpadScroll.Label) { t.value.Set(OzText.Tab($"◀  {OzTrackpadScroll.Name}  ▶")); continue; }   // TRACKPAD SCROLL: ZOOM / MOVE MAP
                if (t.label == OzShuttleOrdersOption.Label) { t.value.Set(OzText.Tab($"◀  {OzShuttleOrdersOption.Name}  ▶")); continue; }   // K256g SHUTTLE ORDERS: LANDABLE SPOTS ONLY / ANYWHERE
                if (t.label == OzQuality.Label) { t.value.Set(OzText.Tab($"◀  {OzQuality.Name}  ▶")); continue; }
                if (t.label == OzFpsCap.Label) { t.value.Set(OzText.Tab($"◀  {OzFpsCap.Name}  ▶")); continue; }
                if (t.label == OzOverlapOption.Label) { t.value.Set(OzText.Tab($"◀  {OzOverlapOption.Name}  ▶")); continue; }   // FRIENDLY UNITS OVERLAP: NO / YES
                if (t.label == OzSettings.ResLabel) { t.value.Set(OzText.Tab($"◀  {OzSettings.ResolutionName}  ▶")); continue; }
                if (t.chatter && t.robots) { t.value.Set(OzText.Tab($"◀  {AudioDirector.RobotChatter.Names[AudioDirector.RobotChatter.Level]}  ▶")); continue; }
                if (t.chatter) { t.value.Set(OzText.Tab($"◀  {AudioDirector.Chatter.Names[AudioDirector.Chatter.Level]}  ▶")); continue; }
                if (t.scale)
                {
                    int pct = Mathf.RoundToInt(OzText.UiScale * 100);                                        // the string only when the scale changes
                    if (pct != scaleShown) { scaleShown = pct; t.value.Set(OzText.Tab($"◀  {pct}%  ▶")); }
                    continue;
                }
                bool on = t.get();
                t.state.Set(on ? "ON" : "OFF"); t.state.color = on ? (Color)OzUi.Amber : (Color)OzUi.Muted;
                t.knob.color = on ? (Color)OzUi.Amber : (Color)OzUi.GM500;
                t.knob.rectTransform.anchoredPosition = new Vector2(OzLayout.PauseW - 2 * OzLayout.OptX - 16 - 48 + (on ? 24 : 2), t.knob.rectTransform.anchoredPosition.y);
            }
        }

        // ------------------------------------------------------------------ settings applied at start and on change
        void ApplySaved()
        {
            foreach (var s in sliders) if (PlayerPrefs.HasKey(s.key)) s.apply(s.v);
            ApplyEdgeScroll();
            audioRetryUntil = Time.unscaledTime + 12f;
        }
        float audioRetryUntil, nextAudioRetry;

        float edgeDefault = -1f;
        void ApplyEdgeScroll()
        {
            if (!hud.rts) return;
            if (edgeDefault < 0f) edgeDefault = hud.rts.edgePx > 0 ? hud.rts.edgePx : 6f;
            hud.rts.edgePx = PlayerPrefs.GetInt("oz_edge_scroll", 1) == 1 ? edgeDefault : 0f;
        }

        static FieldInfo shakeField; static bool shakeLooked; static readonly object ZeroF = 0f;
        /// <summary>SCREEN SHAKE off: RtsCamera has no switch, so its private shake amount is cleared every frame (this
        /// component runs before the camera).</summary>
        void ApplyShake()
        {
            if (PlayerPrefs.GetInt("oz_shake", 1) == 1 && !OzMotion.Reduced || !hud.rts) return;
            if (!shakeLooked) { shakeLooked = true; shakeField = typeof(RtsCamera).GetField("shake", BindingFlags.NonPublic | BindingFlags.Instance); }
            if (shakeField != null && shakeField.FieldType == typeof(float)) shakeField.SetValue(hud.rts, ZeroF);   // cached box: no garbage per frame
        }

        void OnDestroy() { post?.Destroy(); if (IsOpen) Time.timeScale = prevScale > 0f ? prevScale : 1f; }
    }

    /// <summary>Volume options onto the audio track's AudioDirector buses (by reflection: its bus table is private and the
    /// </summary>
    public static class OzAudio
    {
        static FieldInfo busField; static bool looked; static UnityEngine.Object director; static Type dirType;
        static readonly Dictionary<string, float> baseDb = new Dictionary<string, float>();
        static readonly Dictionary<string, float> wanted = new Dictionary<string, float>();

        /// <summary>EFFECTS slider scale: its middle (50 %) plays the designed mix, its top is EffectsTopDb over it.</summary>
        public const float EffectsTopDb = 6.02f;
        /// <summary>Linear gain of an EFFECTS slider position: 0.5 -> 1, 1 -> 2.</summary>
        public static float EffectsLin(float v) => 2f * Mathf.Clamp01(v);

        /// <summary>Once per install: a position saved on the old scale (100 % = the designed mix) is halved, a missing one starts at 50 %.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void EnsureEffectsScale()
        {
            if (PlayerPrefs.GetInt("oz_vol_effects_scale", 1) >= 2) return;
            PlayerPrefs.SetFloat("oz_vol_effects", PlayerPrefs.HasKey("oz_vol_effects") ? 0.5f * PlayerPrefs.GetFloat("oz_vol_effects", 1f) : 0.5f);
            PlayerPrefs.SetInt("oz_vol_effects_scale", 2);
        }

        public static void Set(string bus, float v)
        {
            wanted[bus] = Mathf.Clamp01(v);
            Apply();
        }

        public static void Apply()
        {
            if (!looked)
            {
                looked = true;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    var t = asm.GetType("OpenZ.AudioDirector"); if (t == null) continue;
                    busField = t.GetField("busDb", BindingFlags.NonPublic | BindingFlags.Instance);
                    dirType = t;
                    break;
                }
            }
            if (!director && dirType != null) director = UnityEngine.Object.FindFirstObjectByType(dirType);
            if (busField != null && director && busField.GetValue(director) is Dictionary<string, float> db)
            {
                var loaded = director.GetType().GetProperty("Loaded", BindingFlags.Public | BindingFlags.Instance);
                if (loaded != null && !(bool)loaded.GetValue(director)) return;                              // bus table not read yet: retried later
                foreach (var kv in wanted)
                {
                    foreach (var bus in kv.Key == "SFX" ? new[] { "SFX", "Ambience" } : kv.Key == "Announcer" ? new[] { "Voice/Announcer", "Voice/Commander" } : kv.Key == "Robots" ? new[] { "Voice/Robots" } : new[] { kv.Key })
                    {
                        if (!baseDb.ContainsKey(bus)) baseDb[bus] = db.TryGetValue(bus, out var b0) ? b0 : 0f;
                        db[bus] = baseDb[bus] + (kv.Value <= 0.001f ? -80f : 20f * Mathf.Log10(kv.Value) + (kv.Key == "SFX" ? EffectsTopDb : 0f));
                    }
                }
                return;
            }
            if (CombatAudio.I && wanted.TryGetValue("SFX", out var fx)) CombatAudio.I.volume = 0.8f * EffectsLin(fx) / 0.85f;
        }
    }
}
