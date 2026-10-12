using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace OpenZ
{
    /// <summary>
    /// The OpenZ front end, built entirely from code with the UI kit (UI_STYLE_GUIDE.md 9.14 main menu, 9.15 level select,
    /// 9.16 loading screen; mock-ups kit_main_menu / kit_level_select). Put this ONE component on an object of the MainMenu
    /// scene (menu OpenZ/Menu/Build Main Menu Scene does it).
    ///
    /// Main menu: the level flybys play behind the menu backdrop (muted VideoPlayer to RenderTexture; the clips alternate
    /// L02 / L17 with a 1 s cross-fade at each loop end; a slow Ken Burns pan over the still when a clip is missing), the
    /// OPENZ logo stamps in, and PLAY / OPTIONS / CONTROLS / CREDITS / QUIT. Options mirror the pause menu's panel (same
    /// PlayerPrefs keys: volumes, edge scroll, health bars, shake, reduced motion, HUD scale) with the CPU difficulty in
    /// place of the in-match game speed. Credits show Resources/OpenZ/Menu/credits (a copy of CREDITS.md).
    ///
    /// Level select: two cards (LEVEL 2 DESERT, LEVEL 17 CAR PARK) whose preview video plays while hovered / focused,
    /// facts and a minimap inset read from the level's own Zod map, and the deploy bar: CPU [NORMAL | EASY]
    /// (OpenZDifficulty, PlayerPrefs oz_difficulty), HEAD START (PlayerPrefs oz_headstart, L02 only, read by ZGame) and
    /// DEPLOY, which shows the loading screen and loads the level scene. Keyboard: arrows / WASD, Enter, Esc, C, H.
    ///
    /// Input is polled like the HUD (new Input System, no EventSystem needed); buttons are the kit's HudButtons. Level scenes
    /// never depend on this component: they still start directly in the editor.
    /// </summary>
    [DefaultExecutionOrder(-40)]
    public partial class OpenZMainMenu : MonoBehaviour                                                     // menu_exe_v1: + OpenZMainMenu.MenuExe.cs
    {
        public const string PrefLevel = "oz_menu_level", PrefOpenSelect = "oz_menu_open_select", PrefHeadStart = "oz_headstart";

        [Tooltip("Main menu background: alternate the L02 / L17 flybys at each loop end (else loop the last played level)")]
        public bool alternateFlybys = true;
        // hard cut (loop end and level select). The cut is taken this long before the clip ends, so the next clip's first frame
        // is decoded and the old clip never stops on screen (the 1 s / 0.6 s cross-fade fields are gone).
        const float LoopCutLead = 0.35f, HardCut = 0.01f;
        /// <summary></summary>
        [Tooltip("Soft vignette over the main-menu flyby videos (0 = off)")] public float flybyVignette = 0.34f;
        RawImage flybyVig; public static int HardCuts { get; private set; }
        [Tooltip("How long a still (no video) holds before the main menu moves on to the other level")] public float stillHold = 18f;
        [Tooltip("Minimum time the loading screen stays up")] public float minLoadingTime = 1.2f;
        public int sortingOrder = 100;

        enum View { Main, Select, Loading }
        enum Side { None, Options, Controls, Credits, Changes, Roadmap, Any = 99 }

        // ------------------------------------------------------------------ state
        View view = View.Main; Side side = Side.None, sideClosing = Side.None;
        int focusItem, focusCard, hoverCard = -1; const float HoverCardK = 0.3f; readonly float[] cardPeekK = new float[2];
        float introAt = -10f, viewAt = -10f, sideAt = -10f, selectK, selectFrom, selectTo;
        bool kbMode, openedWithSelect;
        OzLevelDef[] levels; OzLevelInfo[] infos; OzLevelPreview[] previews;
        int bgLevel = -1, bgFrom = -1; float bgFadeAt = -10f, bgFadeDur = 1f, bgShownAt; bool bgWaitFrame;
        readonly float[] liveK = new float[2], cardHoverK = new float[2], cardLiveK = new float[2], lw = new float[2];

        // ------------------------------------------------------------------ UI
        Canvas canvas; CanvasScaler scaler; RectTransform root; float CW = 1920; int builtW, builtH;
        Image blackFade, backdrop, vignette, selectWash; RawImage gradLeft, gradBottom;
        readonly RawImage[] bgPoster = new RawImage[2], bgLive = new RawImage[2], bgBlur = new RawImage[2];
        RectTransform mainRoot, selectRoot, loadingRoot; CanvasGroup mainCg, selectCg, loadingCg;
        Image logo; OzText eyebrow, footer, footer2; Image underline, rail; OzAnniversaryBadge badge30;
        RectTransform caption; CanvasGroup captionCg; OzText captionTitle, captionSub; (Image bg, Image edge, OzText text) captionChip; OzText testChipText;
        OzText versionLabel;
        RectTransform optionsRt, controlsRt, creditsRt; CanvasGroup optionsCg, controlsCg, creditsCg;
        RectTransform creditsContent, creditsView; OzText creditsText; Image creditsThumb; RectTransform creditsTrack; float creditsScroll, creditsScrollTarget;

        sealed class Item { public string label, hint; public Action act; public RectTransform hit; public Image wash; public OzText text, sub; public float k = -1f, wk, width32, shownK = -2f; }
        readonly List<Item> items = new List<Item>();

        sealed class Card
        {
            public RectTransform rt, imgRt; public CanvasGroup cg; public RawImage poster, live, mini; public Image glow, frameN, frameH, desat, progTrack, progFill; public CanvasGroup previewChip;
            public float x, y, w, imgH; public int index;
        }
        readonly Card[] cards = new Card[2];
        HudButton backBtn, deployBtn, cpuNormal, cpuEasy, cpuMedium, optNormal, optEasy, optMedium, headRow; Image deployFocus, headKnob; OzText headState, headText, headHint, cpuHint, optCpuHint, objLine, errorLine, headChipText;
        float errorUntil;

        // options (same keys and look as OpenZPauseMenu)
        sealed class Slider { public RectTransform track; public Image fill, thumb; public OzText value; public string key, bus; public float v, w; }
        readonly List<Slider> sliders = new List<Slider>(); Slider dragging;
        sealed class Toggle { public HudButton row; public Image knob; public OzText state, value; public Func<bool> get; public Action<bool> set; public bool scale; public string[] names; public string label; public Func<string> cycleName; public int tab; }
        readonly List<Toggle> toggles = new List<Toggle>();
        static readonly float[] Scales = OzText.ScalePresets;

        // loading
        AsyncOperation loadOp; float loadStartAt, loadShown; OzText loadStage, loadPct, loadTip; Image loadFill; int loadLevel;

        // ------------------------------------------------------------------ input (polled like OpenZHud)
        sealed class Hot { public RectTransform rt; public View view; public Side side = Side.Any; public HudButton btn; public Action click; public Action<bool> hover; public string sfx = "ui_click"; }
        readonly List<Hot> hots = new List<Hot>(); Hot hotOver, hotDown; Vector2 lastMouse; float lastCardClickAt = -10f; int lastCardClick = -1;

        // ================================================================== lifecycle
        void Awake()
        {
            Time.timeScale = 1f;                                                                            // the pause menu may have left it at 0
            levels = OzLevelDef.All;
            string last = PlayerPrefs.GetString(PrefLevel, "L02");
            focusCard = Mathf.Max(0, Array.FindIndex(levels, l => l.Id == last));
            openedWithSelect = PlayerPrefs.GetInt(PrefOpenSelect, 0) == 1;
            if (openedWithSelect) { PlayerPrefs.DeleteKey(PrefOpenSelect); PlayerPrefs.Save(); }
        }

        void Start()
        {
            Cursor.visible = true; Cursor.lockState = CursorLockMode.None;
            OzKit.SetCursor("arrow");
            float t0 = Time.realtimeSinceStartup;
            infos = new OzLevelInfo[levels.Length]; previews = new OzLevelPreview[levels.Length];
            for (int i = 0; i < levels.Length; i++) { infos[i] = OzLevelInfo.Load(levels[i].Id); previews[i] = OzLevelPreview.Create(transform, levels[i].Id); }
            var cgo = new GameObject("OpenZ_MainMenu_UI", typeof(RectTransform)) { layer = 5 };
            cgo.transform.SetParent(transform, false);
            canvas = cgo.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = sortingOrder; canvas.pixelPerfect = true;
            scaler = cgo.AddComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(OzLayout.RefW, OzLayout.RefH); scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f; scaler.referencePixelsPerUnit = 100f;                             // the front end stays at the reference layout (HUD SCALE is an in-game option)
            if (openedWithSelect) { view = View.Select; selectK = 1f; selectFrom = selectTo = 1f; }             // back from the last level's CONTINUE
            Build();
            introAt = viewAt = OzMotion.Now;
            if (Mouse.current != null) lastMouse = Mouse.current.position.ReadValue();
            StartMusicFallback();
            Debug.Log($"[OpenZ Menu] built in {(Time.realtimeSinceStartup - t0) * 1000f:0} ms: kit {(OzKit.Available ? "OK" : "MISSING (flat fallback)")}, text {(OzText.UsesTmp ? "TextMeshPro" : "legacy")}, "
                      + $"L02 {(infos[0].Ok ? "map OK" : "fallback facts")}, L17 {(infos[1].Ok ? "map OK" : "fallback facts")}, audio {(DirectorLive ? "AudioDirector" : "fallback clips")}");
        }

        void OnDestroy()
        {
            if (previews != null) foreach (var p in previews) p?.Release();
            if (infos != null) foreach (var i in infos) if (i != null && i.Mini) Destroy(i.Mini);
            PlayerPrefs.Save();
        }

        float CanvasWidth => Screen.height > 0 ? Screen.width * OzLayout.RefH / Screen.height : OzLayout.RefW;
        /// <summary>x of a guide number measured from the right edge of the 1920 u canvas, on the current canvas.</summary>
        float XR(float x1920) => x1920 - OzLayout.RefW + CW;

        // ================================================================== build
        void Build()
        {
            CW = CanvasWidth; builtW = Screen.width; builtH = Screen.height;
            hots.Clear(); items.Clear(); sliders.Clear(); toggles.Clear(); optTips.Clear(); hotOver = hotDown = null; dragging = null;
            if (root) Destroy(root.gameObject);
            root = Hud.Node("Root", canvas.transform); root.Fill();
            BuildBackground();
            mainRoot = Hud.Node("Main", root); mainRoot.Fill(); mainCg = mainRoot.gameObject.AddComponent<CanvasGroup>();
            selectRoot = Hud.Node("Select", root); selectRoot.Fill(); selectCg = selectRoot.gameObject.AddComponent<CanvasGroup>();
            BuildMain();
            BuildSelect();
            blackFade = Hud.Img(root, "FadeFromBlack", OzUi.GM950); blackFade.rectTransform.Fill();
            ApplyView();
        }

        // ---------------------------------------------------------------- background: flybys (sharp for the menu, blurred for the select)
        void BuildBackground()
        {
            var bed = Hud.Img(root, "Bed", OzUi.GM950); bed.rectTransform.Fill();
            for (int i = 0; i < 2; i++)
            {
                bgPoster[i] = Raw(root, "BgStill_" + levels[i].Id);
                bgLive[i] = Raw(root, "BgVideo_" + levels[i].Id);
                bgBlur[i] = Raw(root, "BgBlur_" + levels[i].Id);
            }
            gradLeft = Raw(root, "ScrimLeft"); gradLeft.texture = Gradient(true); gradLeft.color = OzUi.A(new Color32(10, 13, 11, 255), 0f);
            gradBottom = Raw(root, "ScrimBottom"); gradBottom.texture = Gradient(false); gradBottom.color = OzUi.A(new Color32(10, 13, 11, 255), 0f);
            flybyVig = Raw(root, "FlybyVignette"); flybyVig.texture = SoftVignette(); flybyVig.color = new Color(0f, 0f, 0f, 0f);
            selectWash = Hud.Img(root, "SelectWash", OzUi.A(OzUi.GM950, 0f)); selectWash.rectTransform.Fill();
            backdrop = Hud.Kit(root, "MenuBackdrop", "Menu/menu_backdrop", new OzRect(0, 0, 10, 10), null, Image.Type.Simple); backdrop.rectTransform.Fill();
            if (OzKit.GetSprite("Menu/menu_backdrop") == null) { backdrop.enabled = false; gradLeft.color = OzUi.A(new Color32(10, 13, 11, 255), 0.92f); }
            vignette = Hud.Kit(root, "Vignette", "Backdrops/bd_vignette", new OzRect(0, 0, 10, 10), null, Image.Type.Simple); vignette.rectTransform.Fill();
            vignette.enabled = OzKit.GetSprite("Backdrops/bd_vignette") != null;
        }

        static RawImage Raw(RectTransform parent, string name)
        {
            var rt = Hud.Node(name, parent); rt.Fill();
            var r = rt.gameObject.AddComponent<RawImage>(); r.raycastTarget = false; r.color = new Color(1, 1, 1, 0); return r;
        }

        static Texture2D gradH, gradV;
        /// <summary>Soft scrims for the loading screen / missing kit: alpha 1 -> 0 along x (left) or y (bottom), smoothstep.</summary>
        /// <summary></summary>
        static Texture2D SoftVignette()
        {
            const int w = 256, h = 160;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "OpenZ_FlybyVignette" };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float dx = (x + 0.5f) / w * 2f - 1f, dy = (y + 0.5f) / h * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx * 0.9f + dy * dy * 1.1f) / 1.4142f;
                    float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 1.0f, r));
                    px[y * w + x] = new Color32(0, 0, 0, (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f));
                }
            tex.SetPixels32(px); tex.Apply(false, true); return tex;
        }

        static Texture2D Gradient(bool horizontal)
        {
            var cached = horizontal ? gradH : gradV;
            if (cached) return cached;
            var t = new Texture2D(horizontal ? 256 : 1, horizontal ? 1 : 256, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "OZ_Scrim" };
            var px = new Color32[256];
            for (int i = 0; i < 256; i++) { float a = 1f - i / 255f; a = a * a * (3f - 2f * a); px[i] = new Color32(255, 255, 255, (byte)(a * 255)); }
            t.SetPixels32(px); t.Apply(false, true);
            if (horizontal) gradH = t; else gradV = t;
            return t;
        }

        // ---------------------------------------------------------------- main menu (guide 9.14)
        void BuildMain()
        {
            var p = mainRoot; float lx = OzMenuLayout.MenuX;
            eyebrow = Hud.Label(p, "Eyebrow", "A 3D REMAKE OF THE 1996 BITMAP BROTHERS CLASSIC", OzMenuLayout.EyebrowSize, OzUi.Amber, 0.26f).At(OzMenuLayout.EyebrowX, OzMenuLayout.EyebrowY, OzH.L, false, 700);
            float lh = OzMenuLayout.LogoH - 2 * OzMenuLayout.LogoPad;
            if (OzKit.GetSprite("Wordmarks/wm_openz") != null)
            {
                logo = Hud.Kit(p, "Logo", "Wordmarks/wm_openz", new OzRect(lx - OzMenuLayout.LogoPad, OzMenuLayout.LogoY - OzMenuLayout.LogoPad, OzMenuLayout.LogoW, OzMenuLayout.LogoH), null, Image.Type.Simple);
                logo.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                logo.rectTransform.anchoredPosition += new Vector2(OzMenuLayout.LogoW * 0.5f, -OzMenuLayout.LogoH * 0.5f);
            }
            else
            {
                logo = Hud.Img(p, "LogoAnchor", Color.clear); logo.enabled = false;                             // transform only: keeps the stamp animation path
                logo.rectTransform.Place(new OzRect(lx, OzMenuLayout.LogoY, OzMenuLayout.LogoW - 2 * OzMenuLayout.LogoPad, lh));
                logo.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                logo.rectTransform.anchoredPosition += new Vector2((OzMenuLayout.LogoW - 2 * OzMenuLayout.LogoPad) * 0.5f, -lh * 0.5f);
                Hud.Label(logo.transform, "LogoText", "OPENZ", 143, OzUi.Amber, -0.02f).At(0, 0, OzH.L, false, 600);
            }
            underline = Hud.Kit(p, "Underline", "Menu/logo_underline", new OzRect(lx, OzMenuLayout.LogoY + lh + OzMenuLayout.UnderlineGap, 80, 3), OzKit.GetSprite("Menu/logo_underline") != null ? (Color?)null : OzUi.Amber, Image.Type.Simple);
            // Z turns 30 (1996-2026): the anniversary badge under the logo
            badge30 = OzAnniversaryBadge.Build(p, lx, OzMenuLayout.LogoY + lh + OzMenuLayout.UnderlineGap + 16, 12f);

            AddItem("PLAY", "SELECT OPERATION", () => GoSelect());
            AddItem("OPTIONS", "AUDIO · SCROLL · HUD", () => ToggleSide(Side.Options));
            AddItem("CONTROLS", "THE ZOD KEY MAP", () => ToggleSide(Side.Controls));
            AddItem("CHANGES & UPDATES", "WHAT'S NEW SINCE 1996", () => ToggleSide(Side.Changes));
            AddItem("ROADMAP", "WHERE OPENZ IS HEADED", () => ToggleSide(Side.Roadmap));
            AddItem("CREDITS", "TRIBUTE · ORIGINAL CONTENT", () => ToggleSide(Side.Credits));
            AddItem(OzFeedback.Button, "IDEAS · BUGS · FEATURES  ↗", () => OzFeedback.Open("main menu"));
            AddItem("QUIT", "BACK TO THE DESKTOP", () => OzConfirm.Ask("QUIT OPENZ?", "BACK TO THE DESKTOP.", Quit, Sfx, canvas.sortingOrder + 50));
            rail = Hud.Img(p, "RailMarker", OzUi.Amber);
            rail.rectTransform.Place(new OzRect(OzMenuLayout.RailX * CW / OzLayout.RefW, OzMenuLayout.ItemY0 + OzMenuLayout.RailDY, OzMenuLayout.RailW, OzMenuLayout.RailH));

            footer2 = Hud.Label(p, "Footer2", "A FAITHFUL FAN TRIBUTE REMAKE OF Z BY THE BITMAP BROTHERS (1996), CELEBRATING ITS 30TH ANNIVERSARY", OzMenuLayout.FooterSize, OzUi.Muted, 0.16f, OzStyle.Plain)
                .At(lx, OzMenuLayout.FooterY - 16, OzH.L, false, 1100);
            footer = Hud.Label(p, "Footer", "OPENZ 1.0 · OPEN SOURCE GPL-3.0 · FREE, NOT FOR SALE · RULES PORTED FROM THE ZOD ENGINE (GPLv3)", OzMenuLayout.FooterSize, OzUi.Muted, 0.16f, OzStyle.Plain)
                .At(lx, OzMenuLayout.FooterY, OzH.L, false, 1100);
            // contrast, under the preview caption plate (which ends at y 1020), never over other UI
            versionLabel = Hud.Label(p, "Version", OzVersion.Label, OzMenuLayout.FooterSize, OzUi.Dim, 0.16f, OzStyle.Plain)
                .At(CW - OzMenuLayout.VersionRX, OzMenuLayout.VersionY, OzH.R, false, 220);

            // preview caption plate (bottom-right)
            var cr = OzMenuLayout.Caption;
            caption = Hud.Node("Caption", p).Place(cr);
            Hud.Plate(caption, "md", 0f, OzUi.Amber);
            captionCg = caption.gameObject.AddComponent<CanvasGroup>();
            captionChip = Hud.Chip(caption, "Chip", OzMenuLayout.CaptionChip, "LIVE", OzUi.Alert, OzUi.Ink, 9, 0.2f);
            captionTitle = Hud.Label(caption, "Title", "", 13, OzUi.Ink, 0.14f).At(OzMenuLayout.CaptionTitleX, OzMenuLayout.CaptionTitleY, OzH.L, false, 290);
            captionSub = Hud.Label(caption, "Sub", "", 9, OzUi.Muted, 0.16f, OzStyle.Plain).At(OzMenuLayout.CaptionSubX, OzMenuLayout.CaptionSubY, OzH.L, false, 330);

            // warnings for the recording session: test mode / HUD off leave the level in a reduced state
            var warn = new List<string>();
            if (GameBoot.TestMode) warn.Add("TEST MODE ON");
            if (PlayerPrefs.GetInt("oz_hud", 1) == 0) warn.Add("HUD OFF");
            if (warn.Count > 0)
            {
                var tc = OzMenuLayout.TestChip;
                var chip = Hud.Chip(p, "TestChip", new OzRect(OzAnchor.TR, tc.x, tc.y, tc.w, tc.h), string.Join(" · ", warn), OzUi.HazardInk, OzUi.Signal, 9, 0.2f, OzUi.Signal);
                testChipText = chip.text;
            }

            BuildOptions();
            BuildControls();
            BuildCredits();
            BuildChanges();
            BuildRoadmap();
        }

        void AddItem(string label, string hint, Action act)
        {
            int i = items.Count; float lx = OzMenuLayout.MenuX, y = OzMenuLayout.ItemY0 + OzMenuLayout.ItemStep * i;
            var it = new Item { label = label, hint = hint, act = act };
            it.hit = Hud.Node("Item_" + label, mainRoot).Place(new OzRect(lx + OzMenuLayout.ItemHitDX, y + OzMenuLayout.ItemHitDY, OzMenuLayout.ItemHitW, OzMenuLayout.ItemHitH));
            it.wash = Hud.Kit(it.hit, "Wash", "Buttons/menu_item_hover", new OzRect(0, 0, OzMenuLayout.ItemHitW, OzMenuLayout.ItemHitH), OzKit.GetSprite("Buttons/menu_item_hover") != null ? (Color?)null : OzUi.A(OzUi.Amber, 0.16f));
            it.text = Hud.Label(mainRoot, "Label_" + label, label, OzMenuLayout.ItemHoverSize, OzUi.Ink2, 0.06f);
            it.width32 = it.text.preferredWidth;
            it.text.FontSize = OzMenuLayout.ItemSize;
            float hw = Mathf.Max(OzMenuLayout.ItemHitW, it.width32 + 52f);
            if (hw > OzMenuLayout.ItemHitW)
            {
                it.hit.Place(new OzRect(lx + OzMenuLayout.ItemHitDX, y + OzMenuLayout.ItemHitDY, hw, OzMenuLayout.ItemHitH));
                it.wash.rectTransform.Place(new OzRect(0, 0, hw, OzMenuLayout.ItemHitH));
            }
            it.sub = Hud.Label(mainRoot, "Hint_" + label, hint, OzMenuLayout.HintSize, OzUi.Amber, 0.24f);
            it.sub.At(lx + 12 + it.width32 + OzMenuLayout.HintGap, y + OzMenuLayout.HintDY, OzH.L, false, 360);
            items.Add(it);
            int idx = i;
            AddHot(it.hit, View.Main, () => { focusItem = idx; it.act(); }, h => { if (h && MouseMoved) { focusItem = idx; kbMode = false; } }, null, label == "PLAY" ? "ui_level_select" : "ui_click");
        }

        // ---------------------------------------------------------------- options (mirrors OpenZPauseMenu.BuildOptions: same plate, rows and PlayerPrefs)
        void BuildOptions()
        {
            optionsRt = SidePanel("Options", "Panels/options_panel", out optionsCg);
            var p = optionsRt; float x = OzLayout.OptX, w = OptW - 2 * OzLayout.OptX;
            Hud.Label(p, "Title", "OPTIONS", 16, OzUi.Ink, 0.16f).At(x, 15);
            Hud.Label(p, "Saved", "SAVED FOR EVERY MATCH", 9, OzUi.Muted, 0.2f, OzStyle.Plain).At(OptW - x, 18, OzH.R, false, 240);
            var mp = OptMainPage(p);                                                                         // options tabs: MAIN's fixed controls sit on their own page under the tab strip
            Hud.Label(mp, "CpuLabel", "DIFFICULTY", 10, OzUi.Muted, 0.22f, OzStyle.Plain).At(x, OzMenuLayout.CpuRowLabelY);
            float bw = (w - 8) / 3f;
            optEasy = SmallButton(mp, "OptEasy", "EASY", new OzRect(x, OzMenuLayout.CpuRowY, bw, OzMenuLayout.CpuRowH), View.Main, Side.Options, () => SetDifficulty(GameDifficulty.Easy), OzLayout.OptRowText);
            optMedium = SmallButton(mp, "OptMedium", "MEDIUM", new OzRect(x + bw + 4, OzMenuLayout.CpuRowY, bw, OzMenuLayout.CpuRowH), View.Main, Side.Options, () => SetDifficulty(GameDifficulty.Medium), OzLayout.OptRowText);
            optNormal = SmallButton(mp, "OptHard", "HARD", new OzRect(x + 2 * (bw + 4), OzMenuLayout.CpuRowY, bw, OzMenuLayout.CpuRowH), View.Main, Side.Options, () => SetDifficulty(GameDifficulty.Hard), OzLayout.OptRowText);
            optCpuHint = Hud.Label(mp, "CpuHint", "", 9, OzUi.Muted, 0.16f, OzStyle.Plain).At(x, OzMenuLayout.CpuRowHintY, OzH.L, false, w);
            AddSlider(mp, 0, 0, "MUSIC", "oz_vol_music", "Music");
            AddSlider(mp, 0, 1, "EFFECTS · BATTLE", "oz_vol_effects", "SFX");
            AddSlider(mp, 1, 0, "ANNOUNCER", "oz_vol_voice", "Announcer");
            AddSlider(mp, 1, 1, "ROBOT VOICES", "oz_vol_robots", "Robots");
            AddToggle(p, 0, "EDGE SCROLL", () => PlayerPrefs.GetInt("oz_edge_scroll", 1) == 1, v => PlayerPrefs.SetInt("oz_edge_scroll", v ? 1 : 0));
            AddToggle(p, 1, "HEALTH BARS ON EVERY DAMAGED UNIT", () => OpenZUnitOverlay.AlwaysBars, v => OpenZUnitOverlay.AlwaysBars = v);
            AddToggle(p, 2, "SCREEN SHAKE", () => PlayerPrefs.GetInt("oz_shake", 1) == 1, v => PlayerPrefs.SetInt("oz_shake", v ? 1 : 0));
            AddToggle(p, 3, OzSettingTips.CalmLabel, () => OzMotion.Reduced, v => OzMotion.Reduced = v);
            AddToggle(p, 4, "HUD SCALE", null, null, true);
            AddToggle(p, 5, "WEATHER · NIGHT RAIN AND THUNDER", () => OzWeather.Enabled, v => OzWeather.Enabled = v);   // weather_l17_v1 (oz_weather, default on)
            AddToggle(p, 6, "YOUR TEAM COLOUR · VISUAL ONLY", () => TeamColors.Swap, v => { TeamColors.Swap = v; if (objLine != null && levels != null) objLine.Set(TeamColors.Words(levels[focusCard].Objective)); Debug.Log($"[OpenZ Menu] team colour: {(v ? "BLUE" : "RED")} (view only, next level)"); });
            toggles[toggles.Count - 1].names = new[] { "RED", "BLUE" };
            AddToggle(p, 7, "HIT FLASH ON DAMAGED UNITS", () => OpenZDamageStates.HitFlash, v => OpenZDamageStates.HitFlash = v);
            AddToggle(p, 8, "MOTION BLUR · FAST DEBRIS AND SHELLS", () => UnitLookMotionGate.MotionBlurOn, v => UnitLookMotionGate.MotionBlurOn = v);
            AddToggle(p, 9, OzSettingTips.PathV3Label, () => OzSettingTips.PathV3, v => OzSettingTips.PathV3 = v);
            AddToggle(p, 10, OzSettingTips.MinimalLabel, () => OzSettingTips.MinimalUi, v => OzSettingTips.MinimalUi = v);
            AddCycle(p, toggles.Count, NightShadows.Label, () => NightShadows.Name, NightShadows.Step);
            AddToggle(p, toggles.Count, CraterField.Label, () => CraterField.Enabled, v => CraterField.Enabled = v);
            AddCycle(p, toggles.Count, OzQuality.Label, () => OzQuality.Name, OzQuality.Step);
            AddToggle(p, toggles.Count, OzQuality.FpsLabel, () => OzQuality.FpsOn, v => OzQuality.FpsOn = v);
            AddToggle(p, toggles.Count, MechWalk.Label, () => MechWalk.On, v => MechWalk.On = v);
            ExeOptions(p);                                                                                   // menu_exe_v1: MUSIC + FULLSCREEN rows, the centred modal
            foreach (var b in new[] { optEasy, optMedium, optNormal }) if (b != null) optTips[b.Rt] = OzSettingTips.Difficulty;
            BuildOptTip();
        }

        RectTransform SidePanel(string name, string sprite, out CanvasGroup cg)
        {
            var rt = Hud.Node(name, mainRoot).Place(new OzRect(OzMenuLayout.SideX, OzMenuLayout.SideY, OzLayout.PauseW, OzLayout.PauseH));
            if (OzKit.Available) { Hud.Shadow(rt, "Panels/panel_lg_mask"); Hud.Kit(rt, "Plate", sprite, new OzRect(0, 0, OzLayout.PauseW, OzLayout.PauseH), null); }
            else Hud.Plate(rt, "lg", 44f, OzUi.Amber);
            rt.pivot = new Vector2(0f, 0.5f); rt.anchoredPosition += new Vector2(0, -OzLayout.PauseH * 0.5f);   // scale-in from the list side
            cg = rt.gameObject.AddComponent<CanvasGroup>();
            rt.gameObject.SetActive(false);
            return rt;
        }

        HudButton SmallButton(RectTransform p, string name, string label, OzRect r, View v, Side s, Action act, float fs = 11, float track = 0.14f)
        {
            var b = HudButton.Make(p, name, label, new Vector2(r.w, r.h), HudButtonKind.Small, fs, track);
            b.Rt.Place(r);
            AddHot(b.Rt, v, act, null, b, "ui_click", s);
            return b;
        }

        void AddSlider(RectTransform p, int row, int col, string label, string key, string bus)
        {
            float full = OptW - 2 * OzLayout.OptX, w = (full - OzLayout.OptSliderGap) * 0.5f;
            float x = OzLayout.OptX + col * (w + OzLayout.OptSliderGap), y = OzLayout.OptSliderY0 + OzLayout.OptSliderStep * row;
            int j = row * 2 + col;
            var s = new Slider { key = key, bus = bus, v = PlayerPrefs.GetFloat(key, 1f), w = w };                // 100 % = the audio track's own mix (EFFECTS: 50 % is the mix, 100 % is 6 dB over it, OzAudio.EffectsLin)
            Hud.Label(p, "Label" + j, label, 10, OzUi.Muted, 0.22f, OzStyle.Plain).At(x, y);
            s.value = Hud.Label(p, "Value" + j, "", 11, OzUi.Ink, 0f, OzStyle.Plain).At(x + w, y, OzH.R, false, 60);
            s.track = Hud.Node("Track" + j, p).Place(new OzRect(x, y + 20, w, 8));
            var tr = Hud.Kit(s.track, "Bg", "Bars/bar_track", new OzRect(0, 0, w, 8), null); tr.rectTransform.Fill();
            if (!OzKit.Available) tr.color = OzUi.GM950;
            s.fill = OzKit.Available ? OzKit.FillBar(s.track, "Fill", "amber") : Hud.Img(s.track, "Fill", OzUi.Amber);
            s.fill.rectTransform.Fill(1, 1, 1, 1); s.fill.type = Image.Type.Filled; s.fill.fillMethod = Image.FillMethod.Horizontal;
            s.thumb = Hud.Kit(s.track, "Thumb", "Buttons/slider_thumb", new OzRect(0, -6, 14, 20), OzKit.Available ? (Color?)null : OzUi.Ink, Image.Type.Simple);
            var hot = Hud.Node("SliderHot" + j, p).Place(new OzRect(x - 4, y + 10, w + 8, 30));
            AddHot(hot, View.Main, null, null, null, null, Side.Options);
            optTips[hot] = bus == "Music" ? OzSettingTips.Music : bus == "SFX" ? OzSettingTips.Effects : bus == "Robots" ? OzSettingTips.Robots : OzSettingTips.Announcer;
            sliders.Add(s);
            ShowSlider(s);
        }

        void ShowSlider(Slider s)
        {
            float w = s.w > 0f ? s.w : OptW - 2 * OzLayout.OptX;
            s.fill.fillAmount = s.v;
            s.thumb.rectTransform.anchoredPosition = new Vector2(w * s.v - 7f, 6f);
            s.value.Set(OzText.Tab(Mathf.RoundToInt(s.v * 100) + "%"));
        }

        void AddToggle(RectTransform p, int j, string label, Func<bool> get, Action<bool> set, bool scale = false)
        {
            float x = OzLayout.OptX, w = OptW - 2 * OzLayout.OptX, y = OzLayout.OptToggleY0 + OzLayout.OptToggleStep * j;
            var t = new Toggle { get = get, set = set, scale = scale, label = label, tab = OzSettingTips.TabOf(label) };
            t.row = HudButton.Make(p, "Toggle" + j, null, new Vector2(w, OzLayout.OptToggleH), HudButtonKind.Small);
            t.row.Rt.Place(new OzRect(x, y, w, OzLayout.OptToggleH));
            Hud.Label(t.row.Content, "Label", label, OzLayout.OptRowText, OzUi.Ink, 0.1f, OzStyle.Plain).At(16, OzLayout.OptToggleH * 0.5f, OzH.L, true, 300);
            var tip = OzSettingTips.For(label); if (tip != null) optTips[t.row.Rt] = tip;
            if (scale)
            {
                t.value = Hud.Label(t.row.Content, "Value", "", OzLayout.OptRowText, OzUi.Ink2, 0.06f, OzStyle.Plain).At(w - 16, OzLayout.OptToggleH * 0.5f, OzH.R, true, 120);
                AddHot(t.row.Rt, View.Main, () => StepScale(Shift ? -1 : OzSettings.ArrowDir(t.value, lastMouse)), null, t.row, "ui_tab_switch", Side.Options);
            }
            else
            {
                var track = Hud.Kit(t.row.Content, "Track", "Buttons/switch_track", new OzRect(w - 16 - 48, (OzLayout.OptToggleH - 18) * 0.5f, 48, 18), null);
                if (!OzKit.Available) track.color = OzUi.GM950;
                t.knob = Hud.Kit(t.row.Content, "Knob", "Buttons/switch_knob", new OzRect(0, (OzLayout.OptToggleH - 14) * 0.5f, 22, 14), OzUi.Amber, Image.Type.Simple);
                t.state = Hud.Label(t.row.Content, "State", "", 9, OzUi.Amber, 0.2f, OzStyle.Plain).At(w - 16 - 48 - 8, OzLayout.OptToggleH * 0.5f, OzH.R, true, 40);
                AddHot(t.row.Rt, View.Main, () => { bool v = !t.get(); t.set(v); PlayerPrefs.Save(); Sfx(v ? "ui_toggle_on" : "ui_toggle_off"); }, null, t.row, null, Side.Options);   // menu_exe_v1: saved at once
            }
            toggles.Add(t);
        }

        /// <summary></summary>
        void AddCycle(RectTransform p, int j, string label, Func<string> name, Action<int> step)
        {
            float x = OzLayout.OptX, w = OptW - 2 * OzLayout.OptX, y = OzLayout.OptToggleY0 + OzLayout.OptToggleStep * j;
            var t = new Toggle { label = label, cycleName = name, tab = OzSettingTips.TabOf(label) };
            t.row = HudButton.Make(p, "Toggle" + j, null, new Vector2(w, OzLayout.OptToggleH), HudButtonKind.Small);
            t.row.Rt.Place(new OzRect(x, y, w, OzLayout.OptToggleH));
            Hud.Label(t.row.Content, "Label", label, OzLayout.OptRowText, OzUi.Ink, 0.1f, OzStyle.Plain).At(16, OzLayout.OptToggleH * 0.5f, OzH.L, true, 300);
            var tip = OzSettingTips.For(label); if (tip != null) optTips[t.row.Rt] = tip;
            t.value = Hud.Label(t.row.Content, "Value", "", OzLayout.OptRowText, OzUi.Ink2, 0.06f, OzStyle.Plain).At(w - 16, OzLayout.OptToggleH * 0.5f, OzH.R, true, 170);
            AddHot(t.row.Rt, View.Main, () => step(Shift ? -1 : OzSettings.ArrowDir(t.value, lastMouse)), null, t.row, "ui_tab_switch", Side.Options);
            toggles.Add(t);
        }

        void StepScale(int dir)
        {
            float cur = OzText.SavedScale;
            int i = OzSettings.ScaleStep(Scales, cur, dir);                                                  // menu_exe_v1: the 109 % default is no preset
            PlayerPrefs.SetFloat("oz_ui_scale", Scales[i]); PlayerPrefs.Save();                                                  // OpenZHud applies it when the level starts
        }

        void RefreshOptions()
        {
            var d = OpenZDifficulty.Level;
            SetSelected(optNormal, d == GameDifficulty.Hard); SetSelected(optEasy, d == GameDifficulty.Easy); SetSelected(optMedium, d == GameDifficulty.Medium);
            optCpuHint.Set(OpenZDifficulty.Hint(d));
            foreach (var t in toggles)
            {
                if (t.cycleName != null) { t.value.Set(OzText.Tab($"◀  {t.cycleName()}  ▶")); continue; }
                if (t.scale) { int pct = Mathf.RoundToInt(OzText.SavedScale * 100); t.value.Set(OzText.Tab($"◀  {pct}%  ▶")); continue; }
                bool on = t.get();
                t.state.Set(t.names != null ? t.names[on ? 1 : 0] : on ? "ON" : "OFF"); t.state.color = t.names != null ? (Color)(on ? OzUi.BlueText : OzUi.RedText) : on ? (Color)OzUi.Amber : (Color)OzUi.Muted;
                t.knob.color = t.names != null ? (Color)(on ? OzUi.BlueHi : OzUi.RedHi) : on ? (Color)OzUi.Amber : (Color)OzUi.GM500;
                t.knob.rectTransform.anchoredPosition = new Vector2(OptW - 2 * OzLayout.OptX - 16 - 48 + (on ? 24 : 2), t.knob.rectTransform.anchoredPosition.y);
            }
        }

        static void SetSelected(HudButton b, bool on) { if (b != null && b.Selected != on) { b.Selected = on; b.Refresh(); } }

        void SetDifficulty(GameDifficulty d)
        {
            if (OpenZDifficulty.Level == d) return;
            OpenZDifficulty.Level = d;                                                                    // saves PlayerPrefs oz_difficulty; ZGame reads it at level start
            Debug.Log($"[OpenZ Menu] difficulty: {OpenZDifficulty.Label(d)}");
        }

        void DragSliders()
        {
            var m = Mouse.current; if (m == null) return;
            Vector2 mp = m.position.ReadValue();
            if (side == Side.Options && optTab == OzSettingTips.TabMain && m.leftButton.wasPressedThisFrame)   // the sliders are on the MAIN tab only
                foreach (var s in sliders)
                {
                    RectTransformUtility.ScreenPointToLocalPointInRectangle(s.track, mp, null, out var lp);
                    var r = s.track.rect;
                    if (lp.x > r.xMin - 10 && lp.x < r.xMax + 10 && lp.y > r.yMin - 12 && lp.y < r.yMax + 12) { dragging = s; break; }
                }
            if (dragging == null) return;
            if (!m.leftButton.isPressed) { PlayerPrefs.SetFloat(dragging.key, dragging.v); PlayerPrefs.Save(); OzSoundV3.SliderPreview(dragging.bus); dragging = null; return; }   // sound_v3
            RectTransformUtility.ScreenPointToLocalPointInRectangle(dragging.track, mp, null, out var lp2);
            var rr = dragging.track.rect;
            float v = Mathf.Clamp01((lp2.x - rr.xMin) / Mathf.Max(1f, rr.width));
            if (!Mathf.Approximately(v, dragging.v))
            {
                if (Mathf.FloorToInt(v * 10) != Mathf.FloorToInt(dragging.v * 10)) Sfx("ui_slider_tick");
                dragging.v = v; OzAudio.Set(dragging.bus, v); ShowSlider(dragging);
                if (musicSrc && dragging.bus == "Music") musicSrc.volume = MusicFallbackVolume;
            }
        }

        // ---------------------------------------------------------------- controls (the pause menu's key table)
        void BuildControls()
        {
            BuildControlsExe();                                                                              // menu_exe_v1: centred two-column table + key rebinding (OpenZMainMenu.MenuExe.cs)
        }

        /// <summary>OpenZPauseMenu's key table (read by reflection so both lists stay one list); a short copy if it moved.</summary>
        static (string key, string what)[] ControlKeys()
        {
            try
            {
                var f = typeof(OpenZPauseMenu).GetField("Keys", BindingFlags.NonPublic | BindingFlags.Static);
                if (f?.GetValue(null) is ValueTuple<string, string>[] k && k.Length > 0) return k;
            }
            catch (Exception) { }
            return new[]
            {
                ("LEFT CLICK", "Select a unit · drag: box select"), ("RIGHT CLICK", "Order: move, attack, capture, enter, repair"),
                ("CTRL+1 .. 0", "Store the selection as a group"), ("1 .. 0", "Recall a group (again: fly there)"),
                ("WASD / ARROWS", "Scroll · edge scroll · middle drag flies"), ("WHEEL", "Zoom in and out"), ("F10 · ESC", "Hide the HUD · pause menu"),
            };
        }

        const string CreditsHead =
            "<align=center><size=66><b><cspace=0.06em><color=#F4F2EA>OPENZ</color></cspace></b></size>\n" +
            "<size=12>\n</size><size=22><color=#F4F2EA>A faithful fan tribute remake of <b>Z</b> by The Bitmap Brothers (1996)</color></size>\n" +
            "<size=8>\n</size><size=13><cspace=0.24em><color=#F2B84B>30TH ANNIVERSARY  ·  1996 – 2026</color></cspace></size></align>";
        const string CreditsOrigIntro =
            "<align=center><size=13><cspace=0.24em><color=#F2B84B>ORIGINAL CONTENT</color></cspace></size>\n<size=8>\n</size>" +
            "<size=17><color=#C8CABF>OpenZ is an independent, non-commercial fan tribute. Alongside a faithful recreation of the classic rules, " +
            "it contains substantial original content created specifically for this project, including:</color></size></align>";
        static readonly string[] CreditsOrigA =
        {
            "<b>The Electrobot</b>, a new robot type with a stunning tesla gun",
            "<b>Tech level VII</b>, built only at forts",
            "<b>The Mechanoid</b>, a walking medium tank",
            "<b>The fort main defence cannon</b>",
            "<b>The fort roof defender</b>",
            "<b>The nuke finale</b>",
            "<b>Sticky fire</b>: burning ground, units and wrecks",
        };
        static readonly string[] CreditsOrigB =
        {
            "<b>Knockdowns</b> from heavy blasts",
            "<b>Dynamic day and night</b> with searchlights and street lamps",
            "<b>Night rain and thunderstorms</b> on the city map",
            "<b>An original soundtrack</b> with dramatic fort-attack music",
            "<b>Personality voice lines</b> for every unit type",
            "<b>Desert wildlife</b> and a sandworm",
        };
        const string CreditsImprovedIntro = "<align=center><size=13><cspace=0.24em><color=#F2B84B>IMPROVED CLASSIC UNITS</color></cspace></size></align>";
        static readonly string[] CreditsImprovedA =
        {
            "<b>The APC</b>: a squad boards, fires from inside and exits",
            "<b>The crane driver</b> defends himself",
            "<b>Tank commanders</b> fire their own weapons from the hatch",
        };
        static readonly string[] CreditsImprovedB =
        {
            "<b>Manned tower guns</b> with visible gunners",
            "<b>Vehicles and cannons</b> break apart into physical parts",
            "<b>Robots</b> fall with physical ragdoll deaths",
        };
        const string CreditsFoot =
            "<align=center><size=13><cspace=0.24em><color=#F2B84B>BUILT ON OPEN SOURCE</color></cspace></size>\n<size=8>\n</size>" +
            "<size=19><color=#F4F2EA><b>Zod Engine</b></color><color=#8D9286>  ·  GPL-3.0  ·  zod.sourceforge.net</color>\n" +
            "<color=#F4F2EA><b>Inter Tight</b></color><color=#8D9286>  ·  SIL Open Font License 1.1  ·  github.com/rsms/inter</color></size>\n" +
            "<size=30>\n</size><size=16><color=#C8CABF>No copyrighted material is used: every asset was made from scratch.\n" +
            "Free and open source (GPL-3.0), for non-commercial purposes only.</color></size>\n" +
            "<size=16>\n</size><size=15><color=#C8CABF><color=#F4F2EA><b>Z © Rebellion.</b></color> Originally created by The Bitmap Brothers (1996).\n" +
            "OpenZ is an unofficial, free, non-commercial fan remake, not affiliated with or endorsed by Rebellion.</color></size></align>";

        static string CreditsBullets(string[] items)
        {
            var sb = new StringBuilder();
            foreach (var it in items)
                sb.Append("<size=16><color=#F2B84B>•</color><indent=1.1em><color=#C8CABF>").Append(it.Replace("<b>", "<color=#F4F2EA>").Replace("</b>", "</color>")).Append("</color></indent></size>\n");
            return sb.ToString().TrimEnd('\n');
        }

        /// <summary>One credits block (TMP rich text wrapped at w), top at y in the scroll content; returns its height.</summary>
        float CreditsBlock(string rich, float y, float x, float w, string name)
        {
            var t = Hud.Label(creditsContent, name, rich, OzMenuLayout.CreditsBody, OzUi.Ink2, 0.01f, OzStyle.Plain).Body(x, y, w);
            float h = Mathf.Max(10f, t.preferredHeight + 4f);
            t.rectTransform.sizeDelta = new Vector2(w, h);
            if (creditsText == null) creditsText = t;
            return h;
        }

        /// <summary>Two balanced bullet columns centred in the content width; returns the taller column's height.</summary>
        float CreditsColumns(string[] a, string[] b, float y, float cw, string name)
        {
            float colW = Mathf.Min(420f, (cw - 60f) * 0.5f), gap = 40f, x0 = (cw - (2f * colW + gap)) * 0.5f;
            float ha = CreditsBlock(CreditsBullets(a), y, x0, colW, name + "A");
            float hb = CreditsBlock(CreditsBullets(b), y, x0 + colW + gap, colW, name + "B");
            return Mathf.Max(ha, hb);
        }

        void CreditsRule(float y, float cw)
        {
            var r = Hud.Img(creditsContent, "Rule", OzUi.A(OzUi.Amber, 0.55f)).rectTransform;
            r.anchorMin = r.anchorMax = new Vector2(0, 1); r.pivot = new Vector2(0.5f, 1);
            r.anchoredPosition = new Vector2(cw * 0.5f, -y); r.sizeDelta = new Vector2(96f, 2f);
        }

        void BuildCredits()
        {
            float W = Mathf.Min(OzMenuLayout.CreditsW, CW - OzMenuLayout.CreditsX - 60f), H = OzMenuLayout.CreditsH, pad = OzMenuLayout.CreditsPad, head = OzMenuLayout.CreditsHeader;
            creditsRt = Hud.Node("Credits", mainRoot).Place(new OzRect(OzMenuLayout.CreditsX, OzMenuLayout.CreditsY, W, H));
            Hud.Plate(creditsRt, "lg", head, OzUi.Amber, null, true);
            Hud.Label(creditsRt, "Title", "CREDITS", 16, OzUi.Ink, 0.16f).At(pad, 15);
            Hud.Label(creditsRt, "Sub", "30TH ANNIVERSARY FAN TRIBUTE", 9, OzUi.Muted, 0.2f, OzStyle.Plain).At(W - pad, 18, OzH.R, false, 420);
            creditsView = Hud.Node("View", creditsRt).Place(new OzRect(pad, head + 16, W - 2 * pad - 14, H - head - 16 - 40));
            creditsView.gameObject.AddComponent<RectMask2D>();
            creditsContent = Hud.Node("Content", creditsView);
            creditsContent.anchorMin = new Vector2(0, 1); creditsContent.anchorMax = new Vector2(1, 1); creditsContent.pivot = new Vector2(0.5f, 1);
            creditsContent.offsetMin = new Vector2(0, -10); creditsContent.offsetMax = Vector2.zero;
            float cw = W - 2 * pad - 14, y = 22f;
            creditsText = null;
            y += CreditsBlock(CreditsHead, y, 0, cw, "Head") + 26f;
            CreditsRule(y, cw); y += 2f + 26f;
            y += CreditsBlock(CreditsOrigIntro, y, 60f, cw - 120f, "OrigIntro") + 16f;
            y += CreditsColumns(CreditsOrigA, CreditsOrigB, y, cw, "Orig") + 30f;
            y += CreditsBlock(CreditsImprovedIntro, y, 0, cw, "ImpIntro") + 12f;
            y += CreditsColumns(CreditsImprovedA, CreditsImprovedB, y, cw, "Imp") + 28f;
            CreditsRule(y, cw); y += 2f + 26f;
            y += CreditsCommunity(y, cw) + 22f;
            CreditsRule(y, cw); y += 2f + 26f;
            y += CreditsBlock(CreditsFoot, y, 0, cw, "FootBlock") + 24f;
            creditsContent.sizeDelta = new Vector2(0, y);
            creditsTrack = Hud.Node("Track", creditsRt).Place(new OzRect(W - pad + 6, head + 16, 4, H - head - 16 - 40));
            Hud.Img(creditsTrack, "Bg", OzUi.A(OzUi.GM950, 0.9f)).rectTransform.Fill();
            creditsThumb = Hud.Img(creditsTrack, "Thumb", OzUi.Amber);
            creditsThumb.rectTransform.anchorMin = new Vector2(0, 1); creditsThumb.rectTransform.anchorMax = new Vector2(1, 1); creditsThumb.rectTransform.pivot = new Vector2(0.5f, 1);
            Hud.Label(creditsRt, "Foot", "WHEEL / ↑ ↓ SCROLL · ESC CLOSE", 9, OzUi.Dim, 0.2f, OzStyle.Plain).At(pad, H - 24);
            creditsRt.pivot = new Vector2(0f, 0.5f); creditsRt.anchoredPosition += new Vector2(0, -H * 0.5f);
            creditsCg = creditsRt.gameObject.AddComponent<CanvasGroup>();
            creditsRt.gameObject.SetActive(false);
            creditsScroll = creditsScrollTarget = 0f;
            Debug.Log($"[OpenZ] Credits built: content {y:0} u, view {H - head - 16 - 40:0} u, {CreditsOrigA.Length + CreditsOrigB.Length} original + {CreditsImprovedA.Length + CreditsImprovedB.Length} improved items, {creditLinks.Count} community links, no licence link");
        }

        /// <summary>CREDITS.md to TMP rich text: ## headings = amber eyebrows, bullets with a hanging indent, **bold** = INK,
        /// `code` = khaki; the # title is the panel header.</summary>
        public static string MarkdownToTmp(string md)
        {
            var sb = new StringBuilder(md.Length * 2);
            var para = new StringBuilder(); bool bullet = false;
            void Flush()
            {
                if (para.Length == 0) return;
                string t = Inline(para.ToString().Trim());
                sb.Append(bullet ? "<color=#F2B84B>•</color><indent=1.1em>" + t + "</indent>\n" : t + "\n");
                para.Clear(); bullet = false;
            }
            foreach (var raw in md.Replace("\r", "").Split('\n'))
            {
                var line = raw.TrimEnd();
                if (line.StartsWith("# ")) { Flush(); continue; }
                if (line.StartsWith("## ")) { Flush(); sb.Append("\n<size=80%><cspace=0.2em><color=#F2B84B>").Append(Esc(line.Substring(3).Trim().ToUpperInvariant())).Append("</color></cspace></size>\n"); continue; }
                if (line.Trim().Length == 0) { Flush(); continue; }
                var tl = line.TrimStart();
                if (tl.StartsWith("- ")) { Flush(); bullet = true; para.Append(tl.Substring(2)); continue; }
                if (para.Length > 0) para.Append(' ');
                para.Append(tl);
            }
            Flush();
            return sb.ToString().Trim('\n');
        }

        static string Esc(string s) => s.Replace("<", "<noparse><</noparse>");
        static string Inline(string s)
        {
            s = Esc(s);
            var sb = new StringBuilder(s.Length + 32); bool bold = false, code = false;
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '*' && i + 1 < s.Length && s[i + 1] == '*') { sb.Append(bold ? "</color>" : "<color=#F4F2EA>"); bold = !bold; i++; continue; }
                if (s[i] == '`') { sb.Append(code ? "</color>" : "<color=#D6CFAE>"); code = !code; continue; }
                sb.Append(s[i]);
            }
            if (bold) sb.Append("</color>"); if (code) sb.Append("</color>");
            return sb.ToString();
        }

        void ScrollCredits(float dy)
        {
            float max = Mathf.Max(0f, creditsContent.sizeDelta.y - creditsView.rect.height);
            creditsScrollTarget = Mathf.Clamp(creditsScrollTarget + dy, 0f, max);
        }

        void TickCredits(float dt)
        {
            if (!creditsRt || !creditsRt.gameObject.activeSelf) return;
            float max = Mathf.Max(0f, creditsContent.sizeDelta.y - creditsView.rect.height);
            creditsScrollTarget = Mathf.Clamp(creditsScrollTarget, 0f, max);
            creditsScroll = OzMotion.Reduced ? creditsScrollTarget : Mathf.Lerp(creditsScroll, creditsScrollTarget, 1f - Mathf.Exp(-dt * 14f));
            creditsContent.anchoredPosition = new Vector2(0, creditsScroll);
            float th = creditsTrack.rect.height, vis = max <= 0 ? 1f : creditsView.rect.height / creditsContent.sizeDelta.y;
            creditsThumb.rectTransform.sizeDelta = new Vector2(0, Mathf.Max(24f, th * vis));
            creditsThumb.rectTransform.anchoredPosition = new Vector2(0, max <= 0 ? 0 : -(th - creditsThumb.rectTransform.sizeDelta.y) * creditsScroll / max);
            creditsTrack.gameObject.SetActiveFast(max > 0);
            TickCreditLinks();
        }

        // ---------------------------------------------------------------- level select (guide 9.15)
        void BuildSelect()
        {
            var p = selectRoot;
            Hud.Label(p, "Eyebrow", "SELECT OPERATION", 12, OzUi.Amber, 0.3f).At(OzMenuLayout.SelEyebrowX, OzMenuLayout.SelEyebrowY);
            Hud.Label(p, "Title", "CHOOSE YOUR BATTLEFIELD", OzMenuLayout.SelTitleSize, OzUi.Ink, -0.005f).At(OzMenuLayout.SelTitleX, OzMenuLayout.SelTitleY, OzH.L, false, 1100);
            var br = OzMenuLayout.Back;
            backBtn = HudButton.Make(p, "Back", "BACK", new Vector2(br.w, br.h), HudButtonKind.Large, 13, 0.16f, HudButtonStyle.Normal, true, OzH.L);
            backBtn.Rt.Place(br);
            Hud.Keycap(backBtn.Content, "Esc", "ESC", br.w - 10, (br.h - 14) * 0.5f);
            AddHot(backBtn.Rt, View.Select, GoMain, null, backBtn, "ui_menu_back");
            Hud.Label(p, "Keys", "← →  OPERATION     ENTER  DEPLOY     C  CPU     H  HEAD START     ESC  BACK", 9, OzUi.Muted, 0.2f, OzStyle.Plain)
                .At(XR(OzMenuLayout.SelHintRX), OzMenuLayout.SelHintY, OzH.R, false, 900);

            float gap = OzMenuLayout.CardGap;
            float w = Mathf.Min((CW - 2 * OzMenuLayout.CardSide - gap) * 0.5f, OzMenuLayout.CardMaxImgH * OzMenuLayout.CardImgW / OzMenuLayout.CardImgH);
            float ih = w * OzMenuLayout.CardImgH / OzMenuLayout.CardImgW, x0 = CW * 0.5f - w - gap * 0.5f;
            for (int i = 0; i < 2; i++) cards[i] = BuildCard(p, i, x0 + i * (w + gap), OzMenuLayout.CardTop, w, ih);
            BuildDeployBar(p);
        }

        Card BuildCard(RectTransform p, int i, float x, float y, float w, float ih)
        {
            var d = levels[i]; var c = new Card { index = i, x = x, y = y, w = w, imgH = ih };
            float h = ih + OzMenuLayout.CardMeta;
            c.rt = Hud.Node("Card_" + d.Id, p).Place(new OzRect(x, y, w, h));
            c.cg = c.rt.gameObject.AddComponent<CanvasGroup>();
            float g = OzMenuLayout.CardGlow;
            c.glow = Hud.Kit(c.rt, "Glow", "Select/card_glow", new OzRect(-g, -g, w + 2 * g, h + 2 * g), OzUi.A(OzUi.Amber, 0f), Image.Type.Sliced, false);
            if (OzKit.GetSprite("Select/card_glow") == null) c.glow.enabled = false;
            var sh = Hud.Kit(c.rt, "Shadow", "Select/card_mask", new OzRect(OzMenuLayout.CardShadowDX, OzMenuLayout.CardShadowDY, w, h), new Color(0, 0, 0, 0.47f));
            if (OzKit.GetSprite("Select/card_mask") == null) sh.color = new Color(0, 0, 0, 0.47f);
            // image: masked by the chamfered window of the frame
            c.imgRt = Hud.Node("Image", c.rt).Place(new OzRect(0, 0, w, ih));
            var mimg = c.imgRt.gameObject.AddComponent<Image>(); mimg.raycastTarget = false;
            if (OzKit.GetSprite("Select/card_image_mask") != null) { OzKit.Apply(mimg, "Select/card_image_mask"); c.imgRt.gameObject.AddComponent<Mask>().showMaskGraphic = false; }
            else { mimg.color = OzUi.GM900; c.imgRt.gameObject.AddComponent<RectMask2D>(); }
            c.poster = Raw(c.imgRt, "Still"); c.poster.color = Color.white;
            c.live = Raw(c.imgRt, "Video");
            c.desat = Hud.Img(c.imgRt, "Desaturate", OzUi.A(new Color32(96, 98, 92, 255), 0.22f)); c.desat.rectTransform.Fill();
            var fade = Hud.Kit(c.imgRt, "Fade", "Select/card_image_fade", new OzRect(0, ih - OzMenuLayout.CardFadeH, w, OzMenuLayout.CardFadeH), null, Image.Type.Simple);
            if (OzKit.GetSprite("Select/card_image_fade") == null) fade.enabled = false;
            if (!previewAny(i)) Hud.Label(c.imgRt, "NoPreview", "NO PREVIEW", 12, OzUi.Dim, 0.3f, OzStyle.Plain).At(w * 0.5f, ih * 0.5f, OzH.C, true, 300);
            float pi = OzMenuLayout.CardProgInset;
            c.progTrack = Hud.Img(c.rt, "ProgTrack", OzUi.A(OzUi.Ink, 40f / 255f)); c.progTrack.rectTransform.Place(new OzRect(pi, ih - 4, w - 2 * pi, OzMenuLayout.CardProgH));
            c.progFill = Hud.Img(c.progTrack.rectTransform, "Fill", OzUi.Amber); c.progFill.type = Image.Type.Filled; c.progFill.fillMethod = Image.FillMethod.Horizontal; c.progFill.rectTransform.Fill();
            c.progFill.sprite = OzKit.GetSprite("Bars/bar_fill_white");
            // frame: normal + hover (amber outline and corner marks) cross-fade
            c.frameN = Hud.Kit(c.rt, "FrameNormal", "Select/card_frame_normal", new OzRect(0, 0, w, h), null);
            c.frameH = Hud.Kit(c.rt, "FrameHover", "Select/card_frame_hover", new OzRect(0, 0, w, h), OzUi.A(Color.white, 0f));
            if (OzKit.GetSprite("Select/card_frame_normal") == null)
            {
                c.frameN.color = Color.clear;
                var meta = Hud.Img(c.rt, "MetaFallback", OzUi.A(OzUi.GM800, 0.94f)); meta.rectTransform.Place(new OzRect(0, ih, w, OzMenuLayout.CardMeta)); meta.transform.SetSiblingIndex(c.frameN.transform.GetSiblingIndex());
                c.frameH.color = Color.clear;
            }
            // overlays
            Hud.Chip(c.rt, "LevelChip", OzMenuLayout.CardChip, "L" + d.Number, OzUi.GM950, OzUi.Ink, 11, 0.2f, OzUi.A(OzUi.Khaki, 90f / 255f));
            var pc = Hud.Chip(c.rt, "PreviewChip", OzMenuLayout.CardPreviewChip, "▶ PREVIEW", OzUi.Amber, OzUi.GM950, 10, 0.2f);
            c.previewChip = pc.bg.transform.parent.gameObject.AddComponent<CanvasGroup>(); c.previewChip.alpha = 0f;
            float mw = OzMenuLayout.CardMiniWell, mm = OzMenuLayout.CardMiniMargin, mi = OzMenuLayout.CardMiniInset;
            if (infos[i].Mini != null)
            {
                Hud.Kit(c.rt, "MiniWell", "Panels/well_6", new OzRect(w - mm - mw, 22, mw, mw), OzKit.Available ? (Color?)null : OzUi.GM950);
                float side = mw - 2 * mi, tw = infos[i].Width, th = infos[i].Height, s = side / Mathf.Max(tw, th);
                var mr = Hud.Node("Mini", c.rt).Place(new OzRect(w - mm - mw + mi + (side - tw * s) * 0.5f, 22 + mi + (side - th * s) * 0.5f, tw * s, th * s));
                c.mini = mr.gameObject.AddComponent<RawImage>(); c.mini.texture = infos[i].Mini; c.mini.raycastTarget = false;
                var ov = Hud.Kit(c.rt, "MiniGlass", "Minimap/mm_screen_overlay", new OzRect(w - mm - mw + mi, 22 + mi, side, side), null, Image.Type.Simple);
                ov.enabled = OzKit.GetSprite("Minimap/mm_screen_overlay") != null;
            }
            // meta bar
            Hud.Label(c.rt, "Level", "LEVEL " + d.Number, 11, OzUi.Amber, 0.3f, OzStyle.Plain).At(OzMenuLayout.CardLevelX, ih + OzMenuLayout.CardLevelY);
            var name = Hud.Label(c.rt, "Name", d.Name, OzMenuLayout.CardNameSize, OzUi.Ink, 0f).At(OzMenuLayout.CardNameX, ih + OzMenuLayout.CardNameY, OzH.L, false, w * 0.6f);
            float nameRight = OzMenuLayout.CardNameX + name.preferredWidth + 24f;
            Hud.Label(c.rt, "Mood", d.Mood, 11, OzUi.Ink2, 0.2f, OzStyle.Plain).At(w - OzMenuLayout.CardMoodRX, ih + OzMenuLayout.CardMoodY, OzH.R, false, 300);
            float cx = w - OzMenuLayout.CardMoodRX; var chips = infos[i].Chips(d);
            var made = new List<(RectTransform rt, float w)>();
            for (int k = chips.Length - 1; k >= 0; k--)
            {
                var ch = Hud.Chip(c.rt, "Fact" + k, new OzRect(0, ih + OzMenuLayout.CardChipsY, 60, OzMenuLayout.CardChipH), chips[k], OzUi.GM950, OzUi.Ink2, 9, 0.18f, OzUi.A(OzUi.Khaki, 60f / 255f));
                made.Insert(0, (ch.bg.transform.parent.GetComponent<RectTransform>(), ch.text.preferredWidth + 18f));
            }
            float total() { float t = 0; foreach (var m in made) t += m.w + OzMenuLayout.CardChipGap; return t - OzMenuLayout.CardChipGap; }
            while (made.Count > 2 && cx - total() < nameRight) { Destroy(made[made.Count - 1].rt.gameObject); made.RemoveAt(made.Count - 1); }   // narrow card: drop the tile count first
            for (int k = made.Count - 1; k >= 0; k--) { made[k].rt.Place(new OzRect(cx - made[k].w, ih + OzMenuLayout.CardChipsY, made[k].w, OzMenuLayout.CardChipH)); cx -= made[k].w + OzMenuLayout.CardChipGap; }
            int idx = i;
            AddHot(c.rt, View.Select, () => ClickCard(idx), hov => { if (hov) hoverCard = idx; else if (hoverCard == idx) hoverCard = -1; }, null, null);
            return c;
        }

        bool previewAny(int i) => previews[i] != null && (previews[i].Poster != null || previews[i].VideoOk);

        void BuildDeployBar(RectTransform p)
        {
            var bar = Hud.Node("DeployBar", p);
            bar.anchorMin = new Vector2(0, 0); bar.anchorMax = new Vector2(1, 0); bar.pivot = new Vector2(0.5f, 0);
            bar.offsetMin = new Vector2(-2, -2); bar.offsetMax = new Vector2(2, OzMenuLayout.DeployBarH);
            var bg = Hud.Kit(bar, "Bg", "Select/deploy_bar", new OzRect(0, 0, 10, 10), null); bg.rectTransform.Fill();
            if (OzKit.GetSprite("Select/deploy_bar") == null) bg.color = OzUi.A(OzUi.GM800, 0.96f);

            var dr = OzMenuLayout.Deploy; var hr = OzMenuLayout.HeadStart; var cr = OzMenuLayout.Cpu;
            float cpuX = XR(cr.x), headX = XR(hr.x);
            Hud.Label(p, "ObjLabel", "OBJECTIVE", 10, OzUi.Muted, 0.24f, OzStyle.Plain).At(OzMenuLayout.ObjX, OzMenuLayout.ObjLabelY);
            float objMax = cpuX - 40f - OzMenuLayout.ObjX;
            objLine = Hud.Label(p, "Objective", TeamColors.Words(levels[focusCard].Objective), OzMenuLayout.ObjSize, OzUi.Ink, 0.06f).At(OzMenuLayout.ObjX, OzMenuLayout.ObjLineY, OzH.L, false, objMax);
            float footY = OzMenuLayout.ObjFootY;
            if (objLine.preferredWidth > objMax) { objLine.FontSize = 12; objLine.Tracking = 0.02f; }                // narrow canvases
            if (objLine.preferredWidth > objMax) { objLine.Body(OzMenuLayout.ObjX, OzMenuLayout.ObjLineY - 4, objMax); footY += 6; }   // 4:3: two lines
            Hud.Label(p, "ObjFoot", "YOU COMMAND RED · CPU COMMANDS BLUE · ZOD ENGINE RULES", 10, OzUi.Muted, 0.2f, OzStyle.Plain).At(OzMenuLayout.ObjX, footY, OzH.L, false, objMax);
            errorLine = Hud.Label(p, "Error", "", 11, OzUi.AlertText, 0.14f, OzStyle.Plain).At(OzMenuLayout.ObjX, OzMenuLayout.ObjLabelY - 26, OzH.L, false, 1200);

            // DIFFICULTY [EASY | MEDIUM | HARD]
            Hud.Label(p, "CpuLabel", "DIFFICULTY", 10, OzUi.Muted, 0.24f, OzStyle.Plain).At(cpuX, cr.y + OzMenuLayout.BlockLabelDY);
            Hud.Keycap(p, "CpuKey", "C", cpuX + cr.w, cr.y - 2);
            float bw = (cr.w - 8) / 3f, ry = cr.y + OzMenuLayout.BlockRowDY;
            cpuEasy = SmallButton(p, "CpuEasy", "EASY", new OzRect(cpuX, ry, bw, OzMenuLayout.BlockRowH), View.Select, Side.Any, () => SetDifficulty(GameDifficulty.Easy), 11);
            cpuMedium = SmallButton(p, "CpuMedium", "MEDIUM", new OzRect(cpuX + bw + 4, ry, bw, OzMenuLayout.BlockRowH), View.Select, Side.Any, () => SetDifficulty(GameDifficulty.Medium), 11);
            cpuNormal = SmallButton(p, "CpuHard", "HARD", new OzRect(cpuX + 2 * (bw + 4), ry, bw, OzMenuLayout.BlockRowH), View.Select, Side.Any, () => SetDifficulty(GameDifficulty.Hard), 11);
            cpuHint = Hud.Label(p, "CpuHint", "", 9, OzUi.Muted, 0.16f, OzStyle.Plain).At(cpuX + cr.w, OzMenuLayout.BlockHintY, OzH.R, false, 420);

            // HEAD START (L02 only)
            Hud.Label(p, "HeadLabel", "HEAD START", 10, OzUi.Muted, 0.24f, OzStyle.Plain).At(headX, hr.y + OzMenuLayout.BlockLabelDY);
            var hc = Hud.Chip(p, "HeadL02", new OzRect(headX + 96, hr.y - 3, 64, 16), "L02 ONLY", OzUi.GM950, OzUi.Amber, 8, 0.16f, OzUi.A(OzUi.Amber, 0.6f), 3);
            headChipText = hc.text;
            Hud.Keycap(p, "HeadKey", "H", headX + hr.w, hr.y - 2);
            headRow = HudButton.Make(p, "HeadRow", null, new Vector2(hr.w, OzMenuLayout.BlockRowH), HudButtonKind.Small);
            headRow.Rt.Place(new OzRect(headX, ry, hr.w, OzMenuLayout.BlockRowH));
            headText = Hud.Label(headRow.Content, "Text", "+5 VEHICLES", 11, OzUi.Ink, 0.1f, OzStyle.Plain).At(12, OzMenuLayout.BlockRowH * 0.5f, OzH.L, true, 120);
            Hud.Kit(headRow.Content, "Track", "Buttons/switch_track", new OzRect(hr.w - 12 - 48, (OzMenuLayout.BlockRowH - 18) * 0.5f, 48, 18), OzKit.Available ? (Color?)null : OzUi.GM950);
            headKnob = Hud.Kit(headRow.Content, "Knob", "Buttons/switch_knob", new OzRect(0, (OzMenuLayout.BlockRowH - 14) * 0.5f, 22, 14), OzUi.Amber, Image.Type.Simple);
            headState = Hud.Label(headRow.Content, "State", "", 9, OzUi.Amber, 0.2f, OzStyle.Plain).At(hr.w - 12 - 48 - 6, OzMenuLayout.BlockRowH * 0.5f, OzH.R, true, 40);
            AddHot(headRow.Rt, View.Select, ToggleHeadStart, h => headTipOn = h, headRow, null);                    // menu_exe_v1: + the tooltip
            headHint = Hud.Label(p, "HeadHint", "", 9, OzUi.Muted, 0.16f, OzStyle.Plain).At(headX, OzMenuLayout.BlockHintY, OzH.L, false, 300);
            ExeHeadStart(p, hc);                                                                             // menu_exe_v1: no "L02 ONLY", readable label, tooltip

            // DEPLOY (primary) + keyboard focus ring
            float dx = XR(dr.x);
            deployFocus = Hud.Kit(p, "DeployFocus", "Buttons/btn_l_focus", new OzRect(dx - 4, dr.y - 4, dr.w + 8, dr.h + 8), OzUi.A(Color.white, 0f));
            if (OzKit.GetSprite("Buttons/btn_l_focus") == null) deployFocus.enabled = false;
            deployBtn = HudButton.Make(p, "Deploy", "DEPLOY", new Vector2(dr.w, dr.h), HudButtonKind.Large, 18, 0.14f, HudButtonStyle.Primary, true);
            deployBtn.Rt.Place(new OzRect(dx, dr.y, dr.w, dr.h));
            AddHot(deployBtn.Rt, View.Select, Deploy, null, deployBtn, null);
        }

        void RefreshDeployBar()
        {
            var d = OpenZDifficulty.Level;
            SetSelected(cpuNormal, d == GameDifficulty.Hard); SetSelected(cpuEasy, d == GameDifficulty.Easy); SetSelected(cpuMedium, d == GameDifficulty.Medium);
            cpuHint.Set(OpenZDifficulty.Hint(d));
            RefreshHeadStart(d);                                                                             // menu_exe_v1: its own switch on every difficulty, both maps
            if (shownDeployCard != focusCard) { shownDeployCard = focusCard; if (deployBtn.Label != null) deployBtn.Label.Set("DEPLOY · " + levels[focusCard].Name); objLine.Set(TeamColors.Words(levels[focusCard].Objective)); }
            if (deployFocus.enabled) deployFocus.color = OzUi.A(Color.white, kbMode && view == View.Select ? 0.55f + 0.45f * Mathf.Sin(OzMotion.Now * 4f) * (OzMotion.Reduced ? 0f : 1f) : 0f);
            if (errorUntil > 0 && OzMotion.Now > errorUntil) { errorLine.Set(""); errorUntil = 0; }
        }

        int shownDeployCard = -1;
        void ToggleHeadStart()
        {
            bool on = !OpenZDifficulty.HeadStartToggle;                                                      // menu_exe_v1: its own switch (every difficulty, both maps)
            OpenZDifficulty.HeadStartToggle = on;                                                            // saved at once
            Sfx(on ? "ui_toggle_on" : "ui_toggle_off");
            Debug.Log($"[OpenZ Menu] head start: {(on ? "ON" : "OFF")} ({levels[focusCard].Id}: {(on ? OpenZDifficulty.HeadStartHint(levels[focusCard].Id) : "the standard map start")})");
        }

        // ================================================================== views
        void GoSelect()
        {
            if (view != View.Main) return;
            CloseSide();
            view = View.Select; viewAt = OzMotion.Now; selectFrom = selectK; selectTo = 1f;
            selectRoot.gameObject.SetActive(true);
            FocusCard(focusCard, true);
        }

        void GoMain()
        {
            if (view != View.Select) return;
            view = View.Main; viewAt = OzMotion.Now; selectFrom = selectK; selectTo = 0f;
            mainRoot.gameObject.SetActive(true);
            Sfx("ui_menu_back");
            if (bgLevel >= 0 && previews[bgLevel].VideoOk) previews[bgLevel].Player.isLooping = !CanAlternate;
            bgShownAt = OzMotion.Now;
        }

        /// <summary>Show the current view at once (first build and rebuilds after a resolution change).</summary>
        void ApplyView()
        {
            mainRoot.gameObject.SetActive(view == View.Main); selectRoot.gameObject.SetActive(view == View.Select);
            mainCg.alpha = view == View.Main ? 1f : 0f; selectCg.alpha = view == View.Select ? 1f : 0f;
            if (view == View.Select) FocusCard(focusCard, true);
            if (side != Side.None) { var s = side; side = Side.None; OpenSide(s, true); }
        }

        void ToggleSide(Side s) { if (side == s) CloseSide(); else OpenSide(s); }

        void OpenSide(Side s, bool instant = false)
        {
            if (side == s) return;
            if (side != Side.None) sideClosing = side;
            side = s; sideAt = instant ? -10f : OzMotion.Now;
            var rt = SideRt(s); if (rt) { rt.gameObject.SetActive(true); rt.SetAsLastSibling(); }
            if (s == Side.Credits) { creditsScroll = creditsScrollTarget = 0f; }
            if (s == Side.Changes) { cuScroll = cuTarget = 0f; cuDrag = 0; }
            if (s == Side.Roadmap) { rmScroll = rmTarget = 0f; }
            if (!instant) Sfx("ui_panel_open");
            PlayerPrefs.Save();
        }

        void CloseSide()
        {
            if (side == Side.None) return;
            sideClosing = side; side = Side.None; sideAt = OzMotion.Now; dragging = null;
            Sfx("ui_panel_close");
            PlayerPrefs.Save();
        }

        RectTransform SideRt(Side s) => s == Side.Options ? optionsRt : s == Side.Controls ? controlsRt : s == Side.Credits ? creditsRt : s == Side.Changes ? changesRt : s == Side.Roadmap ? roadmapRt : null;
        CanvasGroup SideCg(Side s) => s == Side.Options ? optionsCg : s == Side.Controls ? controlsCg : s == Side.Credits ? creditsCg : s == Side.Changes ? changesCg : s == Side.Roadmap ? roadmapCg : null;

        void FocusCard(int i, bool force, bool silent = false)
        {
            i = Mathf.Clamp(i, 0, levels.Length - 1);
            if (i == focusCard && !force) return;
            bool changed = i != focusCard; focusCard = i;
            if (changed && !silent) Sfx("ui_hover");
            if (view == View.Select && (bgLevel != i || force)) SwitchBg(i, OzLevelPreview.PosterTime, true, HardCut, bgLevel == i);
        }

        void ClickCard(int i)
        {
            bool dbl = lastCardClick == i && OzMotion.Now - lastCardClickAt < 0.35f;
            lastCardClick = i; lastCardClickAt = OzMotion.Now;
            FocusCard(i, false, true);
            if (!dbl) Sfx("ui_level_select");
            if (dbl) Deploy();
        }

        // ================================================================== background flybys
        bool CanAlternate => alternateFlybys && previewAny(0) && previewAny(1);
        /// <summary>The level whose flyby opens the main menu: the desert (L02) while both levels alternate, else the last played level.</summary>
        int FirstFlyby { get { int d = Array.FindIndex(levels, l => l.Id == "L02"); return d >= 0 && CanAlternate ? d : focusCard; } }
        bool bgRotateFrom;                                                                                 // the running cross-fade is a loop-end alternation

        /// <summary>Show level `to` in the background: start its clip at `t` and cross-fade from the current one.</summary>
        void SwitchBg(int to, double t, bool loop, float fade, bool keepPlaying = false)
        {
            if (to == bgLevel && keepPlaying) { if (previews[to].VideoOk) previews[to].Player.isLooping = loop; return; }
            if (to != bgLevel) { bgFrom = bgLevel; bgLevel = to; }
            bgRotateFrom = false;
            bgFadeDur = Mathf.Max(0.01f, OzMotion.Dur(fade, true));
            var p = previews[to];
            if (p.VideoOk) { p.PlayFrom(t, loop); bgWaitFrame = true; }
            else bgWaitFrame = false;
            bgFadeAt = OzMotion.Now; bgShownAt = OzMotion.Now;
        }

        void TickBackground(float dt)
        {
            if (bgLevel < 0) { SwitchBg(view == View.Select ? focusCard : FirstFlyby, view == View.Select ? OzLevelPreview.PosterTime : 0, view == View.Select || !CanAlternate, 0.01f); bgFrom = -1; }
            var cur = previews[bgLevel];
            if (bgWaitFrame)                                                                                  // hold the cross-fade until the new clip shows a frame (or 0.8 s)
            {
                if (cur.ShowingVideo || !cur.VideoOk || OzMotion.Now - bgFadeAt > 0.8f) { bgWaitFrame = false; bgFadeAt = OzMotion.Now; bgShownAt = OzMotion.Now; }
            }
            if (cur.VideoOk && cur.Prepared && !cur.Player.isPlaying && view != View.Loading) cur.PlayFrom(-1, view == View.Select || !CanAlternate);
            float fk = bgWaitFrame ? 0f : Mathf.Clamp01((OzMotion.Now - bgFadeAt) / bgFadeDur);
            if (fk >= 1f && bgFrom >= 0)
            {
                HardCuts++; Debug.Log($"[OpenZ Menu] flyby hard cut -> {levels[bgLevel].Id} #{previews[bgLevel].ClipIndex + 1} (fade {bgFadeDur * 1000f:0} ms)");
                if (bgRotateFrom) previews[bgFrom].NextClip(); else previews[bgFrom].Pause();
                bgFrom = -1; bgRotateFrom = false;
            }

            // main menu: at the loop end, cross-fade to the other level (the new clip starts at 0: seamless, no jump)
            if (view == View.Main && CanAlternate && bgFrom < 0 && !bgWaitFrame)
            {
                double left = cur.ShowingVideo ? cur.TimeLeft : stillHold - (OzMotion.Now - bgShownAt);
                if (left <= LoopCutLead) { SwitchBg(1 - bgLevel, 0, false, HardCut); bgRotateFrom = true; }
            }

            // Exact cross-fades with plain alpha blending (no dark dip): each level stacks still > video > blurred copy, level 0
            // under level 1. Wanted coverage per layer c_k (sum = level weight w); drawn bottom-up with alpha a_k, a layer ends
            // up with a_k x prod(1 - a_j above it), so a_k is solved from the top down. The lower level keeps full cover while
            // the upper one fades over it.
            float sk = OzEase.InOutSine(selectK), aspect = (float)Screen.width / Mathf.Max(1, Screen.height);
            float wIn = bgWaitFrame ? 0f : OzEase.InOutSine(fk);
            for (int i = 0; i < 2; i++) lw[i] = i == bgLevel ? (bgFrom >= 0 ? wIn : 1f) : i == bgFrom ? 1f - wIn : 0f;
            if (lw[1] < 0.999f && lw[0] > 0f) lw[0] = Mathf.Clamp01(lw[0] / (1f - lw[1]));
            for (int i = 0; i < 2; i++)
            {
                var p = previews[i];
                liveK[i] = p.ShowingVideo ? 1f : Mathf.MoveTowards(liveK[i], 0f, dt / 0.3f);
                Texture still = p.Poster != null ? p.Poster : (p.VideoOk && p.Player.frame >= 0 ? p.Frame : null), video = p.VideoOk ? p.Frame : null;
                float L = video != null ? liveK[i] : 0f, w = lw[i];
                float c0 = w * (1f - sk) * (1f - L), c1 = w * (1f - sk) * L, c2 = w * sk;
                var blur = c2 > 0.001f ? p.Blurred : null;
                if (blur == null) { c0 += c2; c2 = 0f; }
                float a2 = c2, rem = 1f - a2;
                float a1 = rem > 1e-4f ? c1 / rem : 0f; rem *= 1f - a1;
                float a0 = rem > 1e-4f ? c0 / rem : 0f;
                SetRaw(bgPoster[i], still, Mathf.Clamp01(a0), aspect, true, i, 1f);
                SetRaw(bgLive[i], video, Mathf.Clamp01(a1), aspect, false, i, 1f);
                SetRaw(bgBlur[i], blur, Mathf.Clamp01(a2), aspect, !p.ShowingVideo, i, 0.42f);                  // level select: 40 % brightness
            }
            // scrims: kit backdrop in the menu, vignette + wash on the select
            if (backdrop.enabled) backdrop.color = OzUi.A(Color.white, 1f - sk);
            else { gradLeft.color = OzUi.A(new Color32(10, 13, 11, 255), 0.92f * (1f - sk)); gradLeft.rectTransform.anchorMax = new Vector2(Mathf.Min(1f, 1100f / CW), 1f); }
            if (vignette.enabled) vignette.color = OzUi.A(Color.white, 0.8f * sk);
            if (flybyVig) { var vc = new Color(0f, 0f, 0f, Mathf.Clamp01(flybyVignette) * (1f - sk)); if (flybyVig.color != vc) flybyVig.color = vc; }
            selectWash.color = OzUi.A(OzUi.GM950, 0.18f * sk);
        }

        void SetRaw(RawImage r, Texture t, float a, float aspect, bool kenBurns, int seed, float bright)
        {
            if (t == null || a <= 0.001f) { if (r.enabled) r.enabled = false; return; }
            if (!r.enabled) r.enabled = true;
            if (r.texture != t) r.texture = t;
            var cover = OzLevelPreview.Cover((float)t.width / Mathf.Max(1, t.height), aspect);
            r.uvRect = kenBurns ? OzLevelPreview.KenBurns(cover, OzMotion.Now, seed * 2.3f) : cover;
            var c = new Color(bright, bright, bright * 0.97f, a); if (r.color != c) r.color = c;
        }

        // ================================================================== per frame
        void Update()
        {
            if (!canvas) return;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            if ((Screen.width != builtW || Screen.height != builtH) && view != View.Loading) Build();
            if (view == View.Loading) { TickLoading(dt); TickBackground(dt); return; }
            if (!OzConfirm.Blocks) { Poll(); Keys(); DragSliders(); }
            TickBackground(dt);
            TickIntro();
            TickViews(dt);
            if (view == View.Main) { TickItems(dt); TickCaption(); TickSide(); TickCredits(dt); TickChanges(dt); TickRoadmap(dt); TickFeedback(); if (side == Side.Options) RefreshOptions(); TickExe(dt); }
            else if (view == View.Select) { TickCards(dt); RefreshDeployBar(); TickHeadTip(); }   // menu_exe_v1: head start tooltip
            TickOptTip();
            if (view == View.Select && side != Side.None) side = Side.None;
        }

        bool MouseMoved { get; set; }
        static bool Shift => Keyboard.current != null && Keyboard.current.shiftKey.isPressed;

        void Poll()
        {
            var m = Mouse.current; if (m == null) return;
            Vector2 mp = m.position.ReadValue();
            MouseMoved = (mp - lastMouse).sqrMagnitude > 0.25f; lastMouse = mp;
            if (MouseMoved) kbMode = false;
            Hot over = null;
            for (int i = hots.Count - 1; i >= 0; i--)
            {
                var h = hots[i];
                if (h.view != view || (h.side != Side.Any && h.side != side) || !h.rt || !h.rt.gameObject.activeInHierarchy) continue;
                if (h.btn != null && !h.btn.Interactable) continue;
                if (RectTransformUtility.RectangleContainsScreenPoint(h.rt, mp, null)) { over = h; break; }
            }
            if (over != hotOver)
            {
                if (hotOver != null) { if (hotOver.btn != null) { hotOver.btn.Hovered = false; hotOver.btn.Refresh(); } hotOver.hover?.Invoke(false); }
                hotOver = over;
                if (over != null)
                {
                    if (over.btn != null) { over.btn.Hovered = true; over.btn.Refresh(); }
                    over.hover?.Invoke(true);
                    if (MouseMoved && over.click != null && over.sfx != "") Sfx("ui_hover");   // menu_exe_v1: the modal scrim is silent
                }
            }
            if (m.leftButton.wasPressedThisFrame)
            {
                hotDown = over;
                if (hotDown?.btn != null) { hotDown.btn.Pressed = true; hotDown.btn.Refresh(); }
            }
            if (m.leftButton.wasReleasedThisFrame && hotDown != null)
            {
                var h = hotDown; hotDown = null;
                if (h.btn != null) { h.btn.Pressed = false; h.btn.Refresh(); }
                if (h == over && h.click != null)
                {
                    if (!string.IsNullOrEmpty(h.sfx)) Sfx(h.sfx);
                    h.click();
                }
            }
            float wheel = m.scroll.ReadValue().y;
            if (Mathf.Abs(wheel) > 0.01f && side == Side.Credits && view == View.Main) ScrollCredits(-Mathf.Sign(wheel) * 56f);
            if (Mathf.Abs(wheel) > 0.01f && side == Side.Roadmap && view == View.Main) ScrollRoadmap(-Mathf.Sign(wheel) * 56f);
        }

        void AddHot(RectTransform rt, View v, Action click, Action<bool> hover, HudButton btn, string sfx, Side s = Side.Any)
            => hots.Add(new Hot { rt = rt, view = v, click = click, hover = hover, btn = btn, sfx = sfx, side = s });

        void Keys()
        {
            var kb = Keyboard.current; if (kb == null || OzControlsTable.Capturing) return;   // menu_exe_v1: a key being bound is no menu key
            bool up = kb.upArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame, down = kb.downArrowKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame;
            bool left = kb.leftArrowKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame, right = kb.rightArrowKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame;
            bool enter = kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame;
            bool esc = kb.escapeKey.wasPressedThisFrame || kb.backspaceKey.wasPressedThisFrame;
            if (up || down || left || right || enter) kbMode = true;
            if (OzMotion.Now - introAt < 0.25f) return;
            if (view == View.Main)
            {
                if (side == Side.Options || side == Side.Controls || side == Side.Changes) { if (esc) CloseSide(); else if (side == Side.Options && (left || right)) StepOptTab(right ? 1 : -1); return; }   // menu_exe_v1: the modal owns the keyboard
                if (side == Side.Credits && (up || down)) { ScrollCredits(up ? -56f : 56f); return; }
                if (side == Side.Credits && (kb.pageUpKey.wasPressedThisFrame || kb.pageDownKey.wasPressedThisFrame)) { ScrollCredits(kb.pageUpKey.wasPressedThisFrame ? -400f : 400f); return; }
                if (side == Side.Roadmap && (up || down)) { ScrollRoadmap(up ? -56f : 56f); return; }
                if (side == Side.Roadmap && (kb.pageUpKey.wasPressedThisFrame || kb.pageDownKey.wasPressedThisFrame)) { ScrollRoadmap(kb.pageUpKey.wasPressedThisFrame ? -400f : 400f); return; }
                if (up) { focusItem = (focusItem + items.Count - 1) % items.Count; Sfx("ui_hover"); }
                if (down) { focusItem = (focusItem + 1) % items.Count; Sfx("ui_hover"); }
                if (enter) { Sfx(focusItem == 0 ? "ui_level_select" : "ui_click"); items[focusItem].act(); }
                if (esc && side != Side.None) CloseSide();
            }
            else if (view == View.Select)
            {
                if (left) { if (focusCard <= 0) Sfx("ui_disabled"); FocusCard(focusCard - 1, false); }     // sound_v3: the end of the list answers
                if (right) { if (focusCard >= levels.Length - 1) Sfx("ui_disabled"); FocusCard(focusCard + 1, false); }
                if (enter) Deploy();
                if (esc) GoMain();
                if (kb.cKey.wasPressedThisFrame) { SetDifficulty(OpenZDifficulty.Next(OpenZDifficulty.Level)); Sfx("ui_tab_switch"); }
                if (kb.hKey.wasPressedThisFrame) ToggleHeadStart();
            }
        }

        // ---------------------------------------------------------------- animation
        void TickIntro()
        {
            blackFade.color = OzUi.A(OzUi.GM950, 1f - OzEase.OutQuad(OzMotion.T(introAt, OzMotion.Dur(0.4f, true))));
            if (blackFade.color.a <= 0.001f && blackFade.enabled) blackFade.enabled = false;
            // logo stamp (guide 7.1 stamp: 220 ms easeOutBack 1.4) after the 400 ms fade
            float ks = OzMotion.T(introAt, OzMotion.Dur(OzMotion.Stamp), OzMotion.Reduced ? 0f : 0.4f);
            if (logo) { logo.rectTransform.localScale = Vector3.one * Mathf.LerpUnclamped(1.6f, 1f, OzEase.OutBack(ks)); SetAlpha(logo, ks); }
            if (ks > 0.02f && ks < 0.9f && OzSoundV3.Once("menu_logo", introAt)) Sfx("ui_stamp");   // sound_v3
            float ke = OzMotion.T(introAt, OzMotion.Dur(0.28f, true), OzMotion.Reduced ? 0f : 0.45f);
            eyebrow.Alpha = ke; SetAlpha(underline, ke); if (badge30) badge30.Alpha = ke;
            float kf = OzMotion.T(introAt, OzMotion.Dur(0.4f, true), OzMotion.Reduced ? 0f : 0.9f);
            footer.Alpha = kf; if (footer2 != null) footer2.Alpha = kf;
            captionCg.alpha = kf;
        }

        static void SetAlpha(Graphic g, float a) { if (!g) return; var c = g.color; if (Mathf.Abs(c.a - a) > 0.002f) { c.a = a; g.color = c; } }

        void TickViews(float dt)
        {
            float t = OzMotion.T(viewAt, OzMotion.Dur(selectTo > selectFrom ? OzMotion.Long : 0.4f, true));
            selectK = Mathf.Lerp(selectFrom, selectTo, t);
            float kIn = OzEase.OutCubic(OzMotion.T(viewAt, OzMotion.Dur(OzMotion.Medium, true), OzMotion.Reduced ? 0f : 0.08f));
            float kOut = 1f - OzEase.OutCubic(OzMotion.T(viewAt, OzMotion.Dur(OzMotion.Short, true)));
            if (view == View.Select)
            {
                selectCg.alpha = kIn; mainCg.alpha = kOut;
                if (kOut <= 0f && mainRoot.gameObject.activeSelf) mainRoot.gameObject.SetActive(false);
            }
            else if (view == View.Main)
            {
                mainCg.alpha = kIn; selectCg.alpha = kOut;
                if (kOut <= 0f && selectRoot.gameObject.activeSelf) selectRoot.gameObject.SetActive(false);
            }
        }

        void TickItems(float dt)
        {
            float now = OzMotion.Now, lx = OzMenuLayout.MenuX;
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                bool on = i == focusItem;
                float target = on ? 1f : 0f;
                it.k = it.k < 0 ? target : Mathf.MoveTowards(it.k, target, dt / Mathf.Max(0.001f, OzMotion.Dur(OzMotion.Short)));
                it.wk = Mathf.MoveTowards(it.wk, target, dt / Mathf.Max(0.001f, OzMotion.Dur(OzMotion.Medium)));
                float e = OzEase.OutCubic(it.k);
                // entrance: items slide in from -20 u, 50 ms stagger, after the logo stamp
                float ki = OzEase.OutCubic(OzMotion.T(introAt, OzMotion.Dur(OzMotion.Medium, true), OzMotion.Reduced ? 0f : 0.62f + 0.05f * i));
                float slide = (1f - ki) * -20f;
                // translucent ROADMAP plate as a ghost line, first seen with the long BUG REPORTS & SUGGESTIONS label)
                bool hideHint = side != Side.None && lx + 12 + it.width32 + OzMenuLayout.HintGap + it.sub.preferredWidth > OzMenuLayout.SideX - 12f;
                float key = e + ki * 10f + it.wk * 100f + (hideHint ? 1000f : 0f);
                if (Mathf.Abs(key - it.shownK) > 0.0005f)
                {
                    it.shownK = key;
                    float y = OzMenuLayout.ItemY0 + OzMenuLayout.ItemStep * i;
                    it.text.FontSize = Mathf.Lerp(OzMenuLayout.ItemSize, OzMenuLayout.ItemHoverSize, e);
                    it.text.Tracking = Mathf.Lerp(0.06f, 0.04f, e);
                    it.text.At(lx + OzMenuLayout.ItemShift * e + slide, y + OzMenuLayout.ItemCapDY, OzH.L, false, 460);
                    it.text.color = OzUi.A(Color.Lerp(OzUi.Ink2, OzUi.Ink, e), ki);
                    it.sub.Alpha = hideHint ? 0f : e * ki;
                    it.sub.rectTransform.anchoredPosition = new Vector2(lx + 12 + it.width32 + OzMenuLayout.HintGap - 8f * (1f - e) + slide, it.sub.rectTransform.anchoredPosition.y);
                    float wk = OzEase.OutCubic(it.wk) * ki;
                    SetAlpha(it.wash, wk);
                    it.wash.rectTransform.anchoredPosition = new Vector2(-24f * (1f - wk), 0f);
                }
            }
            // amber rail marker follows the focused item (x 61 on the stretched backdrop rail)
            var rr = rail.rectTransform;
            float ty = -(OzMenuLayout.ItemY0 + OzMenuLayout.ItemStep * focusItem + OzMenuLayout.RailDY);
            float ny = OzMotion.Reduced ? ty : Mathf.Lerp(rr.anchoredPosition.y, ty, 1f - Mathf.Exp(-dt * 18f));
            rr.anchoredPosition = new Vector2(rr.anchoredPosition.x, ny);
            SetAlpha(rail, OzMotion.T(introAt, OzMotion.Dur(OzMotion.Medium, true), OzMotion.Reduced ? 0f : 0.62f));
        }

        static readonly StringBuilder capSb = new StringBuilder(64);
        int capLevel = -1, capSecs = -1; bool capLive; int stillLogged = -1;
        void TickCaption()
        {
            if (bgLevel < 0) return;
            var p = previews[bgLevel]; bool live = p.ShowingVideo;
            int secs = -1;
            if (CanAlternate && bgFrom < 0)
            {
                double left = live ? p.TimeLeft : stillHold - (OzMotion.Now - bgShownAt);
                secs = Mathf.Max(0, Mathf.CeilToInt((float)left - LoopCutLead));
            }
            if (bgLevel == capLevel && secs == capSecs && live == capLive) return;
            capLevel = bgLevel; capSecs = secs; capLive = live;
            var d = levels[bgLevel];
            captionTitle.Set((live ? "FLYBY · L" : "PREVIEW · L") + d.Number + " " + d.Name);
            captionChip.text.Set(live ? "LIVE" : "STILL");
            if (!live && stillLogged != bgLevel && OzMotion.Now - bgShownAt > 1.5f) { stillLogged = bgLevel; Debug.LogWarning($"[OpenZ Menu] STILL shown for {d.Id} #{p.ClipIndex + 1}/{p.ClipCount}: {p.StillReason()}"); }
            if (live) stillLogged = -1;
            captionChip.bg.color = live ? (Color)OzUi.Alert : (Color)OzUi.GM500;
            capSb.Clear();
            if (secs >= 0) { var o = levels[1 - bgLevel]; capSb.Append("NEXT · L").Append(o.Number).Append(' ').Append(o.Name).Append(" IN ").Append(UnitInfo.Mmss(secs)); captionSub.Set(OzText.Tab(capSb.ToString())); }
            else captionSub.Set(live ? "LEVEL PREVIEW · LOOPING" : "LEVEL PREVIEW");
        }

        static readonly Side[] SidePanels = { Side.Options, Side.Controls, Side.Credits, Side.Changes, Side.Roadmap };
        void TickSide()
        {
            float k = OzEase.OutCubic(OzMotion.T(sideAt, OzMotion.Dur(OzMotion.Short, true)));
            foreach (Side s in SidePanels)
            {
                var rt = SideRt(s); var cg = SideCg(s); if (!rt || !cg) continue;
                if (s == side) { cg.alpha = k; rt.localScale = Vector3.one * (Mathf.Lerp(0.96f, 1f, k) * SideScale(s));   /* menu_exe_v1: the options modal at 1.2x */ }
                else if (rt.gameObject.activeSelf)
                {
                    float kc = OzMotion.T(sideAt, OzMotion.Dur(0.1f, true));
                    cg.alpha = Mathf.Min(cg.alpha, 1f - kc);
                    if (kc >= 1f || s != sideClosing) rt.gameObject.SetActive(false);
                }
            }
        }

        void TickCards(float dt)
        {
            float kIn = OzMotion.T(viewAt, OzMotion.Dur(0.26f, true), 0f);
            for (int i = 0; i < 2; i++)
            {
                var c = cards[i]; var p = previews[i];
                bool on = i == focusCard;
                // brightens its picture and draws a faint outline (peek), it never moves the selection
                cardHoverK[i] = Mathf.MoveTowards(cardHoverK[i], on ? 1f : 0f, dt / Mathf.Max(0.001f, OzMotion.Dur(OzMotion.Medium)));
                cardPeekK[i] = Mathf.MoveTowards(cardPeekK[i], !on && i == hoverCard && view == View.Select ? 1f : 0f, dt / Mathf.Max(0.001f, OzMotion.Dur(OzMotion.Short)));
                float e = OzEase.OutCubic(cardHoverK[i]), pk = OzEase.OutCubic(cardPeekK[i]) * (1f - e);
                // entrance: rise 24 u + fade, 80 ms stagger
                float ke = OzEase.OutCubic(OzMotion.T(viewAt, OzMotion.Dur(0.26f, true), OzMotion.Reduced ? 0f : 0.1f + 0.08f * i));
                float rise = view == View.Select ? (1f - ke) * 24f : 0f;
                c.rt.anchoredPosition = new Vector2(c.x, -(c.y - OzMenuLayout.CardLift * e + rise));
                c.cg.alpha = view == View.Select ? ke : 1f;
                SetAlpha(c.glow, 0.8f * e);
                SetAlpha(c.frameH, OzKit.GetSprite("Select/card_frame_hover") != null ? e + HoverCardK * pk : 0f);
                c.previewChip.alpha = e;
                // image: poster at 72 % brightness, 55 % saturation (grey wash) until hovered; the clip fades in over it
                bool showLive = on && p.ShowingVideo && bgLevel == i;
                cardLiveK[i] = Mathf.MoveTowards(cardLiveK[i], showLive ? 1f : 0f, dt / 0.3f);
                var still = p.Poster != null ? p.Poster : (p.VideoOk && p.Player.frame >= 0 ? (Texture)p.Frame : null);
                float ca = (float)c.w / Mathf.Max(1f, c.imgH), br = Mathf.Lerp(0.72f, 1f, Mathf.Max(e, 0.55f * pk));
                if (still != null)
                {
                    c.poster.texture = still;
                    var cov = OzLevelPreview.Cover((float)still.width / Mathf.Max(1, still.height), ca);
                    c.poster.uvRect = on && !p.VideoOk ? OzLevelPreview.KenBurns(cov, OzMotion.Now, i * 2.3f) : cov;   // no clip: the still pans (animated preview fallback)
                    c.poster.color = new Color(br, br, br, 1f);
                    c.poster.enabled = true;
                }
                else c.poster.enabled = false;
                if (p.VideoOk)
                {
                    c.live.texture = p.Frame; c.live.uvRect = OzLevelPreview.Cover((float)p.Frame.width / Mathf.Max(1, p.Frame.height), ca);
                    c.live.color = new Color(1, 1, 1, cardLiveK[i]); c.live.enabled = cardLiveK[i] > 0.001f;
                }
                else c.live.enabled = false;
                SetAlpha(c.desat, 0.22f * (1f - Mathf.Max(e, 0.55f * pk)));
                bool prog = on && p.ShowingVideo && bgLevel == i;
                c.progTrack.enabled = prog; c.progFill.enabled = prog;
                if (prog) c.progFill.fillAmount = p.Progress;
            }
        }

        // ================================================================== deploy + loading (guide 9.16)
        void Deploy()
        {
            if (view != View.Select) return;
            var d = levels[focusCard];
            if (!Application.CanStreamedLevelBeLoaded(d.Scene))
            {
                Sfx("ui_error");
                errorLine.Set($"{d.Scene.ToUpperInvariant()} IS NOT IN THE BUILD SETTINGS · MENU OPENZ / MENU / SET BUILD ORDER");
                errorUntil = OzMotion.Now + 5f;
                Debug.LogError($"[OpenZ Menu] scene {d.Scene} is not in the build settings (menu OpenZ/Menu/Set Build Order)");
                return;
            }
            if (PlayerPrefs.GetInt("oz_botmatch", 0) == 1) { PlayerPrefs.SetInt("oz_botmatch", 0); Debug.Log("[OpenZ Menu] oz_botmatch was left on by a test: cleared, DEPLOY starts a human vs CPU match"); }
            PlayerPrefs.SetString(PrefLevel, d.Id);
            PlayerPrefs.Save();
            Sfx("ui_level_start");
            PlayerPrefs.SetInt(OpenZDifficulty.Key, (int)OpenZDifficulty.Level);                              // menu_exe_v1: ZGame reads the key only when it exists (MEDIUM is a default now)
            Debug.Log($"[OpenZ Menu] DEPLOY {d.Id} ({d.Scene}): difficulty {OpenZDifficulty.Label(OpenZDifficulty.Level)}, head start {(OpenZDifficulty.HeadStartToggle ? "ON (+" + OpenZDifficulty.HeadStartCount(d.Id) + " vehicles)" : "off")}");
            view = View.Loading; loadLevel = focusCard; viewAt = OzMotion.Now;
            if (hotOver?.btn != null) { hotOver.btn.Hovered = false; hotOver.btn.Refresh(); }
            BuildLoading(d);
            for (int i = 0; i < previews.Length; i++) if (i != focusCard) previews[i].Pause();
            loadStartAt = OzMotion.Now; loadShown = 0f;
            loadOp = SceneManager.LoadSceneAsync(d.Scene, LoadSceneMode.Single);
            if (loadOp != null) loadOp.allowSceneActivation = false;
            else { Debug.LogWarning("[OpenZ Menu] async load refused: loading " + d.Scene + " directly"); SceneManager.LoadScene(d.Scene); }
        }

        /// <summary>
        /// The loading screen's own stills, Resources/OpenZ/Loading/&lt;id&gt;_loading_1..9 (L02: 1 sunrise canyon red fort,
        /// 2 blue fort assault river, 3 canyon valley river overview; L17: 1 car park war machines dusk, 2 blue fortress howitzers rain,
        /// 3 night canal lamps rain). One at random each time the level loads, never the one shown last time (PlayerPrefs) when there are
        /// two or more. Only the picked texture is loaded: the slots are tried in a random order (the last one last), the first that
        /// exists wins, so the pick is uniform over the others. The level-select card and the menu background keep &lt;id&gt;_poster.
        /// Null = no loading still: the caller falls back to the poster.</summary>
        static Texture LoadingStill(string id)
        {
            const int Slots = 9;
            string key = "oz_loading_last_" + id;
            int last = PlayerPrefs.GetInt(key, 0);
            var order = new List<int>(Slots);
            for (int k = 1; k <= Slots; k++) if (k != last) order.Add(k);
            for (int i = order.Count - 1; i > 0; i--) { int j = UnityEngine.Random.Range(0, i + 1); int s = order[i]; order[i] = order[j]; order[j] = s; }
            if (last >= 1 && last <= Slots) order.Add(last);                                               // the last one only if nothing else exists
            foreach (int k in order)
            {
                var t = Resources.Load<Texture2D>($"OpenZ/Loading/{id}_loading_{k}");
                if (t == null) continue;
                PlayerPrefs.SetInt(key, k);
                Debug.Log($"[OpenZ Menu] loading still {id}_loading_{k} ({t.width}x{t.height}, last time {(last > 0 ? last.ToString() : "none")})");
                return t;
            }
            return null;
        }

        static readonly string[] Tips = { "WASD / ARROWS SCROLL · WHEEL ZOOMS", "CTRL+1..0 STORES A GROUP · 1..0 RECALLS", "RIGHT-CLICK ORDERS · SHIFT QUEUES" };

        void BuildLoading(OzLevelDef d)
        {
            loadingRoot = Hud.Node("Loading", root); loadingRoot.Fill();
            loadingCg = loadingRoot.gameObject.AddComponent<CanvasGroup>(); loadingCg.alpha = 0f;
            var bed = Hud.Img(loadingRoot, "Bed", OzUi.GM950); bed.rectTransform.Fill();
            var p = previews[focusCard];
            var img = Raw(loadingRoot, "Poster");
            Texture tex = LoadingStill(d.Id);
            if (tex == null) tex = p.Poster != null ? p.Poster : p.VideoOk ? p.Frame : null;
            if (tex != null) { img.texture = tex; img.uvRect = OzLevelPreview.Cover((float)tex.width / Mathf.Max(1, tex.height), (float)Screen.width / Mathf.Max(1, Screen.height)); img.color = Color.white; }
            var gl = Raw(loadingRoot, "ScrimLeft"); gl.texture = Gradient(true); gl.color = OzUi.A(new Color32(10, 13, 11, 255), 0.5f);
            gl.rectTransform.anchorMax = new Vector2(Mathf.Min(1f, 1100f / CW), 1f);
            var gb = Raw(loadingRoot, "ScrimBottom"); gb.texture = Gradient(false); gb.color = OzUi.A(new Color32(10, 13, 11, 255), 0.9f);
            gb.rectTransform.anchorMax = new Vector2(1f, 0.54f);
            var vg = Hud.Kit(loadingRoot, "Vignette", "Backdrops/bd_vignette", new OzRect(0, 0, 10, 10), null, Image.Type.Simple); vg.rectTransform.Fill();
            vg.enabled = OzKit.GetSprite("Backdrops/bd_vignette") != null;
            float x = OzMenuLayout.LoadX, w = OzMenuLayout.LoadW;
            OzAnniversaryBadge.Build(loadingRoot, x, OzMenuLayout.LoadTop - 46, 11f);                         // Z turns 30
            Hud.Label(loadingRoot, "Eyebrow", "PREPARING OPERATION L" + d.Number, 12, OzUi.Amber, 0.24f).At(x, OzMenuLayout.LoadTop);
            var ms = OzKit.GetSprite(d.Wordmark);
            if (ms != null)
            {
                float k = OzMenuLayout.LoadCap / 118f;                                                           // stencil masks are cap 118 u at native size
                Hud.Kit(loadingRoot, "Wordmark", d.Wordmark, new OzRect(x, OzMenuLayout.LoadWordY, ms.rect.width * 0.5f * k, ms.rect.height * 0.5f * k), OzUi.Ink, Image.Type.Simple);
            }
            else Hud.Label(loadingRoot, "Wordmark", d.Name, 132, OzUi.Ink, -0.02f).At(x, OzMenuLayout.LoadWordY);
            Hud.Label(loadingRoot, "Goal", TeamColors.Words(d.Goal), 16, OzUi.Ink2, 0.02f).At(x, OzMenuLayout.LoadGoalY, OzH.L, false, 700);
            loadStage = Hud.Label(loadingRoot, "Stage", "LOADING TERRAIN", 14, OzUi.Ink2, 0.12f).At(x, OzMenuLayout.LoadMeterY, OzH.L, false, w);
            loadPct = Hud.Label(loadingRoot, "Pct", "0%", 14, OzUi.Amber, 0.04f).At(x + w, OzMenuLayout.LoadMeterY, OzH.R, false, 120);
            var track = Hud.Node("Bar", loadingRoot).Place(new OzRect(x, OzMenuLayout.LoadBarY, w, OzMenuLayout.LoadBarH));
            var tb = Hud.Kit(track, "Track", "Bars/bar_track", new OzRect(0, 0, w, OzMenuLayout.LoadBarH), null); tb.rectTransform.Fill();
            if (!OzKit.Available) tb.color = OzUi.GM950;
            loadFill = OzKit.Available ? OzKit.FillBar(track, "Fill", "amber") : Hud.Img(track, "Fill", OzUi.Amber);
            loadFill.rectTransform.Fill(); loadFill.type = Image.Type.Filled; loadFill.fillMethod = Image.FillMethod.Horizontal; loadFill.fillAmount = 0f;
            loadTip = Hud.Label(loadingRoot, "Tip", Tips[0], 14, OzUi.Muted, 0.12f).At(x, OzMenuLayout.LoadTipY, OzH.L, false, 900);
            loadingRoot.SetAsLastSibling();
        }

        void TickLoading(float dt)
        {
            float k = OzEase.OutCubic(OzMotion.T(viewAt, OzMotion.Dur(0.3f, true)));
            loadingCg.alpha = k; if (mainCg) mainCg.alpha = Mathf.Min(mainCg.alpha, 1f - k); if (selectCg) selectCg.alpha = Mathf.Min(selectCg.alpha, 1f - k);
            if (musicSrc) musicSrc.volume = MusicFallbackVolume * (1f - k);
            if (k >= 1f) foreach (var p in previews) p?.Pause();                                              // the loading screen shows the still: stop decoding
            float target = loadOp == null ? 1f : Mathf.Clamp01(loadOp.progress / 0.9f);
            float minT = Mathf.Clamp01((OzMotion.Now - loadStartAt) / Mathf.Max(0.1f, minLoadingTime));
            loadShown = Mathf.MoveTowards(loadShown, Mathf.Min(target, 0.25f + 0.75f * minT), dt * 1.6f);
            loadFill.fillAmount = loadShown;
            int pct = Mathf.RoundToInt(loadShown * 100f);
            loadPct.Set(OzText.Tab(pct + "%"));
            loadStage.Set(loadShown < 0.48f ? "LOADING TERRAIN" : loadShown < 0.85f ? "DEPLOYING FORCES" : "BRIEFING COMMANDERS");
            OzSoundV3.LoadingStage(loadShown < 0.48f ? 0 : loadShown < 0.85f ? 1 : 2, loadShown, Sfx);   // sound_v3
            loadTip.Set(Tips[loadShown < 0.48f ? 0 : loadShown < 0.85f ? 1 : 2]);
            if (loadOp != null && loadShown >= 0.999f && loadOp.progress >= 0.9f && !loadOp.allowSceneActivation)
            {
                foreach (var p in previews) p?.Pause();
                loadOp.allowSceneActivation = true;                                                           // ZGame.Awake reads oz_difficulty and oz_headstart
            }
        }

        // ================================================================== quit
        void Quit()
        {
            PlayerPrefs.Save();
            Debug.Log("[OpenZ Menu] QUIT");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ================================================================== sound (AudioDirector when imported, else plain clips)
        static PropertyInfo directorI; static bool dirLooked;
        static bool DirectorLive
        {
            get
            {
                if (!dirLooked)
                {
                    dirLooked = true;
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        var t = asm.GetType("OpenZ.AudioDirector"); if (t == null) continue;
                        directorI = t.GetProperty("I", BindingFlags.Public | BindingFlags.Static); break;
                    }
                }
                return directorI != null && directorI.GetValue(null) is UnityEngine.Object o && o;
            }
        }

        AudioSource sfxSrc, musicSrc;
        readonly Dictionary<string, AudioClip> clipCache = new Dictionary<string, AudioClip>();
        float lastSfxAt; string lastSfx;

        void Sfx(string id)
        {
            if (id == lastSfx && OzMotion.Now - lastSfxAt < 0.04f) return;                                  // one event per frame burst
            lastSfx = id; lastSfxAt = OzMotion.Now;
            if (DirectorLive) { OzSfx.Ui(id); return; }
            string[] paths; float vol;
            switch (id)
            {
                case "ui_hover": case "ui_slider_tick": paths = new[] { "OpenZ/Audio/Sfx/sd_ui_hover" }; vol = 0.25f; break;
                case "ui_level_start": paths = new[] { "OpenZ/Audio/Sfx/sd_ui_menu_start", "OpenZ/Sfx/enter" }; vol = 0.4f; break;
                case "ui_panel_open": paths = new[] { "OpenZ/Audio/Sfx/sd_ui_panel_open", "OpenZ/Sfx/enter" }; vol = 0.22f; break;
                case "ui_panel_close": case "ui_menu_back": paths = new[] { "OpenZ/Audio/Sfx/sd_ui_panel_close", "OpenZ/Sfx/enter" }; vol = 0.18f; break;
                default: paths = new[] { "OpenZ/Sfx/enter" }; vol = 0.22f; break;
            }
            AudioClip clip = null;
            foreach (var p in paths)
            {
                if (!clipCache.TryGetValue(p, out clip)) { clip = Resources.Load<AudioClip>(p); clipCache[p] = clip; }
                if (clip != null) break;
            }
            if (clip == null) return;
            if (!sfxSrc) { sfxSrc = gameObject.AddComponent<AudioSource>(); sfxSrc.playOnAwake = false; sfxSrc.spatialBlend = 0f; sfxSrc.ignoreListenerPause = true; }
            sfxSrc.PlayOneShot(clip, vol * OzAudio.EffectsLin(PlayerPrefs.GetFloat("oz_vol_effects", 1f)));
        }

        float MusicFallbackVolume => 0.35f * PlayerPrefs.GetFloat("oz_vol_music", 1f);

        /// <summary>Menu music without the audio director: the M1 menu track from Resources when it is there.</summary>
        void StartMusicFallback()
        {
            if (DirectorType) return;
            if (PlayerPrefs.GetInt("oz_music", 1) == 0) return;                                           // music toggle off (-take)
            AudioClip clip = null;
            foreach (var n in new[] { "OpenZ/Audio/Music/M1_menu_Brass_Tactics", "OpenZ/Audio/Music/M1_menu_Servo_Sergeant" }) { clip = Resources.Load<AudioClip>(n); if (clip) break; }
            if (!clip) return;
            musicSrc = gameObject.AddComponent<AudioSource>();
            musicSrc.clip = clip; musicSrc.loop = true; musicSrc.spatialBlend = 0f; musicSrc.volume = MusicFallbackVolume; musicSrc.Play();
        }

        static bool DirectorType { get { var _ = DirectorLive; return directorI != null; } }
    }
}
