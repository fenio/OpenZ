using System.Collections.Generic;
using System.Linq;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace OpenZ
{
    /// <summary>
    /// OpenZ HUD bootstrap and main panels, built entirely from code (uGUI, Screen Space Overlay, 1920x1080 reference, match
    /// height) and dressed with the OpenZ UI kit (Materials/ui/kit -> Resources/OpenZ/Kit; UI_STYLE_GUIDE.md: Z modernised
    /// as a military command console). Add this ONE component to the ZGame object (or let the auto-install do it): it builds
    /// the top command strip (team emblems, strength gauges x/70, the amber LED mission clock with scanlines), the territory
    /// tab, the unit panel (portrait bay with scan-in, name, weapon, grenades, Zod's absolute health bar, the A alert tab),
    /// the console command bar (R V B G keys + control groups 1..0), Zod's factory list (B), the stored-cannon plates,
    /// computer-message toasts and the fort alarm, tooltips, the menu button and the level-start mission banner, and adds the
    /// minimap, production window, rally points, unit overlay, cursors, pause menu and end screen components.
    ///
    /// Input: the HUD polls its own buttons (no EventSystem needed). A left press that starts on a HUD panel (or on the
    /// map while the rally tool is armed) and a right release over a HUD panel are kept away from PlayerInput by pausing
    /// that component for exactly those frames (it runs later in the frame), so clicking the HUD never deselects units or
    /// fires an order through the panel into the 3D map; while the pause menu or the end screen is up PlayerInput is paused
    /// entirely. PlayerInput itself is not modified. Only the look changed in the restyle: every Zod rule, hook, key and
    /// public member of the staged HUD is kept.
    /// </summary>
    [DefaultExecutionOrder(-45)]
    public partial class OpenZHud : MonoBehaviour
    {
        public static OpenZHud I { get; private set; }

        [Header("Wiring (found automatically when empty)")]
        public ZGame game; public PlayerInput input; public RtsCamera rts;

        [Header("Behaviour")]
        [Tooltip("Pause PlayerInput on frames whose mouse click belongs to the HUD (no PlayerInput edit needed)")] public bool gatePlayerInput = true;
        [Tooltip("Zod's factory list (B key / B button); Zod starts with it closed")] public bool factoryListOpen;
        [Tooltip("F10 toggles the HUD (recordings)")] public bool hudVisible = true;
        public int sortingOrder = 100;
        [Tooltip("Mission banner, then the staggered HUD entrance (guide 7.2 / 9.17). PlayerPrefs oz_hud_intro = 0 skips it")] public bool missionIntro = true;
        [Tooltip("Unit panel health bar on Zod's absolute scale (zhud.cpp:948-1016: bar length = toughness, a Grunt fills ~11 %); off = health / max health (relative)")] public bool absoluteHealthBar = false;

        public Canvas Canvas { get; private set; }
        public RectTransform Root { get; private set; }
        public PlayerInputBridge Bridge { get; private set; }
        public OpenZMinimap Minimap { get; private set; }
        public OpenZProductionWindow Production { get; private set; }
        public OpenZRallyPoints Rally { get; private set; }
        public OpenZUnitOverlay Overlay { get; private set; }
        public OpenZEndScreen EndScreen { get; private set; }
        public OpenZPauseMenu Pause { get; private set; }
        public OzHelpOverlay Help { get; private set; }
        public OpenZCursors Cursors { get; private set; }
        /// <summary>Match statistics for the end screen (units built, kills, territory peaks, forces over time).</summary>
        public readonly OpenZMatchStats Stats = new OpenZMatchStats();
        public ZWorld W => game ? game.World : null;
        public Team Player => game ? game.player : Team.Red;
        /// <summary>The pause menu or the end screen owns the mouse and keyboard.</summary>
        public bool Modal => (Pause && Pause.IsOpen) || (EndScreen && EndScreen.IsShown);

        /// <summary>Who owns the current left press: nobody (PlayerInput), a HUD panel, the armed rally tool, or an own
        /// fort / factory (Zod opens its production window on the press and KEEPS the selection, zplayer.cpp:3327-3331).</summary>
        public enum Capture { None, Ui, Rally, Building }
        public Capture PressCapture { get; private set; }
        /// <summary>The own producer the current press landed on (Capture.Building).</summary>
        public ZObject PressBuilding { get; private set; }
        /// <summary>Alive own units selected when this frame started, before PlayerInput ran (Zod tests the selection before
        /// it sends: a Z-order that drops the last selected unit must not also set a rally point).</summary>
        public int SelectionCountBeforeInput { get; private set; }
        public Vector2 PressPos { get; private set; }
        public Vector2 Pointer { get; private set; }
        public bool PointerOverUi { get; private set; }
        public HudButton HotButton { get; private set; }

        static readonly List<HudButton> buttons = new List<HudButton>();
        readonly List<RectTransform> blockers = new List<RectTransform>();
        readonly List<RectTransform> modalBlockers = new List<RectTransform>();
        HudButton leftDown, rightDown;
        bool gatedByUs;

        // ---- auto-install: any scene with a ZGame gets the HUD (PlayerPrefs "oz_hud" = 0 turns it off, e.g. for captures)
        // AfterSceneLoad fires for the first scene only; the sceneLoaded hook covers levels loaded later (main menu -> level).
        static bool hooked;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoInstall()
        {
            if (!hooked) { hooked = true; UnityEngine.SceneManagement.SceneManager.sceneLoaded += (s, m) => TryInstall(); }
            TryInstall();
        }

        static void TryInstall()
        {
            if (PlayerPrefs.GetInt("oz_hud", 1) == 0) return;
            var g = FindFirstObjectByType<ZGame>();
            if (g && !FindFirstObjectByType<OpenZHud>()) g.gameObject.AddComponent<OpenZHud>();
        }

        public static void RegisterButton(HudButton b) { if (b != null && !buttons.Contains(b)) buttons.Add(b); }
        public void AddBlocker(RectTransform rt) { if (rt && !blockers.Contains(rt)) blockers.Add(rt); }
        /// <summary>Full-screen roots of the pause menu / end screen: over them only their own buttons (Priority 100) react.</summary>
        public void AddModalBlocker(RectTransform rt) { if (rt && !modalBlockers.Contains(rt)) modalBlockers.Add(rt); }

        public static bool Contains(RectTransform rt, Vector2 screen) => rt && rt.gameObject.activeInHierarchy && !LowerHides(rt) && RectTransformUtility.RectangleContainsScreenPoint(rt, screen, null);   // a lower panel that is switched off takes no pointer (OpenZHud.LowerPanels.cs)

        public bool IsOverUi(Vector2 screen)
        {
            if (Modal) return true;
            if (!hudVisible || !Canvas) return false;
            foreach (var b in blockers) if (Contains(b, screen)) return true;
            return false;
        }

        /// <summary>Screen pixels to HUD canvas units (origin bottom-left).</summary>
        public Vector2 ScreenToCanvas(Vector2 screen)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(Root, screen, null, out var lp);
            return lp;
        }
        public Vector2 CanvasSize => Root && Root.rect.width > 1f ? Root.rect.size : new Vector2(CanvasWidth, OzLayout.RefH / Mathf.Max(0.5f, OzText.UiScale));
        /// <summary>Canvas width in units from the screen (valid before the canvas is laid out): W / k, k = H / 1080 x UI scale.</summary>
        public static float CanvasWidth => Screen.height > 0 ? Screen.width * OzLayout.RefH / (Screen.height * Mathf.Max(0.5f, OzText.UiScale)) : OzLayout.RefW;

        void Awake()
        {
            if (I && I != this) { Destroy(this); return; }
            I = this;
            buttons.Clear();
        }

        void OnDestroy() { if (I == this) I = null; if (input && gatedByUs) input.enabled = true; }

        bool prebuilt;
        Behaviour[] SubParts => new Behaviour[] { Rally, Minimap, Production, Overlay, Cursors, Pause, EndScreen, Help };

        /// <summary>
        /// briefing hand-over frame): ZodBriefing builds the HUD on the level's first frames (the load tail, under the letterbox),
        /// hidden and with its parts paused; Start (at the hand-over, when the briefing enables the HUD) only shows it and runs the
        /// entrance, as before. No change when there is no briefing (Start builds it as it always did).</summary>
        public void Prebuild()
        {
            if (I != this || Canvas != null || prebuilt) return;
            if (!game) game = GetComponent<ZGame>(); if (!game) game = ZGame.I; if (!game) game = FindFirstObjectByType<ZGame>();
            if (!input) input = FindFirstObjectByType<PlayerInput>();
            if (!game || game.World == null || !input) return;                                        // not ready: Start builds it later, as before
            var sw = System.Diagnostics.Stopwatch.StartNew();
            if (!BuildAll()) return;
            prebuilt = true;
            SetVisible(false);
            foreach (var b in SubParts) if (b) b.enabled = false;
            Debug.Log($"[OpenZ HUD] prebuilt under the briefing in {sw.Elapsed.TotalMilliseconds:F0} ms (hidden until the hand-over)");
        }

        void Start()
        {
            if (I != this) return;
            if (prebuilt)
            {
                foreach (var b in SubParts) if (b) b.enabled = true;
                SetVisible(true);
                FinishStart();
                return;
            }
            if (!BuildAll()) return;
            FinishStart();
        }

        bool BuildAll()
        {
            if (!game) game = GetComponent<ZGame>(); if (!game) game = ZGame.I; if (!game) game = FindFirstObjectByType<ZGame>();
            if (!input) input = FindFirstObjectByType<PlayerInput>();
            if (!rts) rts = FindFirstObjectByType<RtsCamera>();
            if (!game || game.World == null || !input) { Debug.LogWarning("[OpenZ HUD] no ZGame / PlayerInput in the scene: HUD off"); enabled = false; return false; }
            Bridge = new PlayerInputBridge(input);
            OzText.UiScale = OzText.SavedScale;

            var cgo = new GameObject("OpenZ_HUD", typeof(RectTransform)) { layer = 5 };
            cgo.transform.SetParent(transform, false);
            Canvas = cgo.AddComponent<Canvas>(); Canvas.renderMode = RenderMode.ScreenSpaceOverlay; Canvas.sortingOrder = sortingOrder; Canvas.pixelPerfect = true;
            scaler = cgo.AddComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight; scaler.matchWidthOrHeight = 1f; scaler.referencePixelsPerUnit = 100f;
            Root = Hud.Node("Root", cgo.transform); Root.Fill(); Root.pivot = Vector2.zero;

            Rally = gameObject.AddComponent<OpenZRallyPoints>();
            Minimap = gameObject.AddComponent<OpenZMinimap>();
            Production = gameObject.AddComponent<OpenZProductionWindow>();
            Overlay = gameObject.AddComponent<OpenZUnitOverlay>();
            Cursors = gameObject.AddComponent<OpenZCursors>();
            Pause = gameObject.AddComponent<OpenZPauseMenu>();
            EndScreen = gameObject.AddComponent<OpenZEndScreen>();
            Help = gameObject.AddComponent<OzHelpOverlay>();

            Overlay.Build(this);            // own canvas under the HUD
            BuildAlertTab();                // behind the unit panel (it slides out from under it)
            BuildTopBar();
            Minimap.Build(this);            // bottom-right; its width places the unit panel
            BuildUnitPanel();
            BuildBottomBar();
            BuildFactoryList();
            BuildStoredCannons();
            BuildMenuButton();
            BuildToasts();
            Production.Build(this);
            Rally.Build(this);
            BuildBanner();
            BuildTooltip();
            Cursors.Build(this);
            Pause.Build(this);
            EndScreen.Build(this);
            Help.Build(this);
            BuildLowerPanels();                                                                        // the lower panels and the minimap step aside for the production window (OpenZHud.LowerPanels.cs)
            ApplyUiScale();
            return true;
        }

        void FinishStart()
        {
            Stats.Begin(W, game);
            StartIntro();
            Debug.Log($"[OpenZ HUD] built: {buttons.Count} buttons, kit {(OzKit.Available ? "OK" : "MISSING (flat fallback)")}, text {(OzText.UsesTmp ? "TextMeshPro" : "legacy")}, "
                      + $"{UnitArt.Count} portrait/icon textures, bridge {(PlayerInputBridge.Missing.Count == 0 ? "OK" : "partial")}{(prebuilt ? " (prebuilt under the briefing)" : "")}");
        }

        CanvasScaler scaler;

        /// <summary>UI SCALE option (guide 8.1: 90 / 100 / 110 / 125 %): the canvas shrinks, so everything grows.</summary>
        public void ApplyUiScale()
        {
            var s = Mathf.Clamp(OzText.UiScale, OzText.ScaleMin, OzText.ScaleMax);
            if (scaler) scaler.referenceResolution = new Vector2(1920f, 1080f) / s;
            if (Overlay) Overlay.SetScale(s);
        }

        // ================================================================== input + gating
        void Update()
        {
            if (!Canvas || W == null) return;
            var m = Mouse.current; var kb = Keyboard.current;
            if (m == null) return;
            Pointer = m.position.ReadValue();
            bool modal = Modal;
            if (kb != null && OzBindings.Pressed(OzAction.HideHud) && !(Pause && Pause.IsOpen)) SetVisible(!hudVisible);   // menu_exe_v1: bound key (F10); not under the pause menu (it hides the HUD itself)
            if (!modal && kb != null && OzBindings.Pressed(OzAction.FactoryList) && !kb.ctrlKey.isPressed && !kb.altKey.isPressed) FactoryListKey();   // the list is a lower panel: with the production window up the key closes the window and shows the list
            if (!modal && kb != null && OzBindings.Pressed(OzAction.ExitCrew) && !kb.ctrlKey.isPressed && !kb.altKey.isPressed && input.Selection.Count > 0) ExitSelected();   // EXIT CREW
            if (!modal && kb != null && OzBindings.Pressed(OzAction.LastMessage)) SpaceEvent();                 // Zod: space = the last computer message's action

            ScanEvents();

            int alive = 0; foreach (var o in input.Selection) if (!o.Destroyed && o.Owner == Player) alive++;
            SelectionCountBeforeInput = alive;

            buttons.RemoveAll(b => b == null || b.Dead || !b.Rt);
            PointerOverUi = IsOverUi(Pointer);
            HudButton over = null;
            // modal screens take everything; the production window is drawn on top: over it, only its own buttons (Priority 10)
            // through the whole ending while the cursor rested on the HUD): once the HUD has slid out at GAME OVER (TickEntrance `exited`)
            // only the modal screens' buttons (pause menu, end screen: Priority 100) can be hovered, so no HUD tooltip outlives the HUD
            int minPriority = OzConfirm.Blocks ? int.MaxValue : (modal || exited) ? 100 : Production && Production.IsOpen && Contains(Production.WindowRect, Pointer) ? 10 : int.MinValue;
            if (PointerOverUi)
                for (int i = buttons.Count - 1; i >= 0; i--)
                    if (buttons[i].Priority >= minPriority && (over == null || buttons[i].Priority > over.Priority) && buttons[i].Alive && Contains(buttons[i].Rt, Pointer)) over = buttons[i];
            if (over != HotButton) { if (HotButton != null) { HotButton.Hovered = false; HotButton.Refresh(); } HotButton = over; if (over != null) { over.Hovered = true; over.Refresh(); } }

            bool lp = m.leftButton.wasPressedThisFrame, lr = m.leftButton.wasReleasedThisFrame;
            bool rp = m.rightButton.wasPressedThisFrame, rr = m.rightButton.wasReleasedThisFrame;
            if (lp)
            {
                PressPos = Pointer; PressBuilding = null;
                if (PointerOverUi) PressCapture = Capture.Ui;
                else if (input.Placing != null) PressCapture = Capture.None;                             // placement owns the mouse
                else if (Rally && Rally.Armed) PressCapture = Capture.Rally;
                else if (hudVisible && Production && (PressBuilding = Production.ProducerUnder(Pointer)) != null && !OwnUnitUnder(Pointer)) PressCapture = Capture.Building;
                else { PressCapture = Capture.None; PressBuilding = null; }
                leftDown = PointerOverUi ? over : null;
                if (leftDown != null) { leftDown.Pressed = true; leftDown.Refresh(); }
            }
            if (rp) rightDown = PointerOverUi ? over : null;
            if (lr && leftDown != null)
            {
                var b = leftDown; leftDown = null; b.Pressed = false; b.Refresh();
                if (b == over && b.Interactable && b.OnClick != null) { b.OnClick(Click(kb, false)); UiClickSound(); }
                else if (b == over && !b.Interactable && b.OnClick != null) OzSfx.Ui("ui_disabled");        // sound_v3
            }
            if (rr && rightDown != null)
            {
                var b = rightDown; rightDown = null;
                if (b == over && b.Interactable && b.OnRightClick != null) { b.OnRightClick(Click(kb, true)); UiClickSound(); }
            }

            bool captured = PressCapture != Capture.None && (m.leftButton.isPressed || lr);
            bool gate = modal || captured || (PointerOverUi && (rr || rp));
            if (gatePlayerInput && gate && input.enabled) { input.enabled = false; gatedByUs = true; }

            TickMinimal();
            RefreshTopBar();
            RefreshUnitPanel();
            RefreshAlertTab();
            RefreshBottomBar();
            RefreshFactoryList();
            RefreshStoredCannons();
            RefreshToasts();
            RefreshBanner();
            RefreshTooltip();
            TickEntrance();
        }

        void LateUpdate()
        {
            var m = Mouse.current;
            bool held = m != null && m.leftButton.isPressed;
            if (gatedByUs && input && !Modal && !(PressCapture != Capture.None && held)) { input.enabled = true; gatedByUs = false; }
            if (!held) { PressCapture = Capture.None; PressBuilding = null; }
            SyncLowerPanels();                                                                         // after every Update that can open or close the production window
        }

        /// <summary>Zod's CouldCollectSelectables (zplayer.cpp:3326): an own robot / vehicle / cannon under the pointer wins the
        /// click over the building behind it (same hit test as PlayerInput.CollectClick).</summary>
        bool OwnUnitUnder(Vector2 mp)
        {
            foreach (var o in W.Objects)
                if (o.Owner == Player && !o.Destroyed && o.KillAt < 0 && (o.Kind == ZKind.Robot || o.Kind == ZKind.Vehicle || o.Kind == ZKind.Cannon)
                    && !o.IsFortDefender && input.ScreenRect(o).Contains(mp)) return true;   // fort_defender_v1 DV41: a click on him opens the fort
            return false;
        }

        static HudClick Click(Keyboard kb, bool right) => new HudClick
        {
            shift = kb != null && kb.shiftKey.isPressed, ctrl = kb != null && kb.ctrlKey.isPressed, alt = kb != null && kb.altKey.isPressed, right = right
        };

        /// <summary>UI click (sound coverage list: "UI click"). The audio track's AudioDirector re-points this key at its
        /// ui_click event; set ClickSound to a clip name (or null for silence).</summary>
        public static string ClickSound = "ui_click";
        public static void UiClickSound() { var au = CombatAudio.I; if (au && !string.IsNullOrEmpty(ClickSound)) au.Play2D(ClickSound, 0.22f); }

        public void SetVisible(bool v)
        {
            hudVisible = v;
            if (Canvas) Canvas.enabled = v;
            if (Overlay) Overlay.SetVisible(v);
            if (!v && Production) Production.Close();                                                   // no invisible window taking rally clicks
            // tooltip has its own override-sorting canvas, so hiding the HUD canvas did not hide it, and the briefing disables this
            // component (no RefreshTooltip) right after hiding the HUD: the tooltip goes with the HUD, the next hover starts fresh
            if (!v && tipPanel) { tipPanel.gameObject.SetActive(false); tipFor = null; tipShown = null; }
            SyncLowerPanels();                                                                         // the production window closed with the HUD (pause, briefing, ending, hide key): no lower panel stays switched off
        }

        /// <summary>Invisible HudButton over a non-clickable element so it can carry a tooltip (territory tab, clock ...).</summary>
        HudButton Hotspot(Transform parent, string name, OzRect r, string tooltip)
        {
            var b = HudButton.Make(parent, name, null, r.Size, HudButtonKind.Small, 12);
            b.Rt.Place(r); b.Bg.enabled = false; b.Tooltip = tooltip;
            if (b.Hatch) { Destroy(b.Hatch.gameObject); b.Hatch = null; }
            return b;
        }

        // ================================================================== entrance (guide 7.2) and exit
        sealed class Slide { public RectTransform rt; public Vector2 home, from; public float delay; public CanvasGroup cg; }
        readonly List<Slide> slides = new List<Slide>();
        float introAt = -100f, exitAt = -100f; bool introDone = true, exited;
        /// <summary></summary>
        public static float ControlAt = -1f;

        void Enter(RectTransform rt, Vector2 from, float delay, bool fade = true)
        {
            if (!rt) return;
            var cg = rt.GetComponent<CanvasGroup>(); if (!cg && fade) cg = rt.gameObject.AddComponent<CanvasGroup>();
            if (cg) { cg.blocksRaycasts = false; cg.interactable = false; }
            slides.Add(new Slide { rt = rt, home = rt.anchoredPosition, from = from, delay = delay, cg = cg });
        }

        void StartIntro()
        {
            Enter(top, new Vector2(0, 90), 0f); Enter(terr, new Vector2(0, 90), 0.06f); Enter(menuBtn.Rt, new Vector2(0, 60), 0.06f);
            Enter(cmdBar, new Vector2(0, -80), 0.12f); Enter(Minimap.Panel, new Vector2(360, 0), 0.18f); Enter(unitPanel, new Vector2(40, 0), 0.24f);
            bool intro = missionIntro && PlayerPrefs.GetInt("oz_hud_intro", 1) == 1 && !OzMotion.Reduced;
            introAt = OzMotion.Now + (intro ? 2.9f : 0f); introDone = false;
            ControlAt = introAt;
            bannerAt = intro ? OzMotion.Now : -100f;
            TickEntrance();
        }

        void TickEntrance()
        {
            float now = OzMotion.Now;
            if (!exited && W.GameOver) { exited = true; exitAt = now; if (Production) Production.Close(); factoryListOpen = false; }
            if (introDone && !exited) return;
            bool allHome = true;
            foreach (var s in slides)
            {
                if (!s.rt) continue;
                float k;
                if (exited) k = 1f - OzEase.InCubic(OzMotion.T(exitAt, OzMotion.Dur(0.2f)));
                else { k = OzEase.OutCubic(OzMotion.T(introAt, OzMotion.Dur(OzMotion.Medium), s.delay)); if (k < 1f) allHome = false; }
                s.rt.anchoredPosition = s.home + s.from * (1f - k);
                if (s.cg) s.cg.alpha = k * MinMul(s.rt);
            }
            if (!exited && allHome) introDone = true;
        }

        // MINIMAL UI (Options, default OFF, oz_minimal_ui): the always-on panels (top score / clock strip, territory tab, unit + comms
        // panel, command bar R V B G + groups) and the message plates (toasts, the fort alarm, the under-attack tab, the mission banner)
        // fade out in 0.18 s and leave the hit tests; the MENU button, the minimap and everything opened on demand (production / factory
        // windows, factory list, cannon-ready plates, tooltips, pause menu, end screen) stay. Sounds, Rachel and every shortcut still work.
        float minK = 1f; int minLogged = -1; RectTransform[] minPanels;
        public static bool MinimalHidden => I && I.minK <= 0.001f;
        bool IsMinPanel(RectTransform rt) => rt && (rt == top || rt == terr || rt == unitPanel || rt == cmdBar);
        float MinMul(RectTransform rt) => IsMinPanel(rt) ? minK : 1f;

        void TickMinimal()
        {
            bool want = OzSettingTips.MinimalUi;
            float target = want ? 0f : 1f, dur = OzMotion.Dur(0.18f, true);
            minK = dur <= 0f ? target : Mathf.MoveTowards(minK, target, Time.unscaledDeltaTime / dur);
            bool on = minK > 0.001f;
            if (minPanels == null) minPanels = new[] { top, terr, unitPanel, cmdBar };
            foreach (var rt in minPanels)
            {
                if (!rt) continue;
                rt.gameObject.SetActiveFast(on);
                if (on && introDone && !exited) { var cg = rt.GetComponent<CanvasGroup>(); if (cg) cg.alpha = minK; }
            }
            int state = want ? 1 : 0;
            if (state != minLogged && (minLogged < 0 || minK == target))
            {
                minLogged = state;
                Debug.Log($"[OpenZ HUD] minimal UI {(want ? "ON" : "OFF")}: top {(top && top.gameObject.activeSelf)}, territory {(terr && terr.gameObject.activeSelf)}, unit panel {(unitPanel && unitPanel.gameObject.activeSelf)}, command bar {(cmdBar && cmdBar.gameObject.activeSelf)}, menu button {(menuBtn != null && menuBtn.Rt.gameObject.activeInHierarchy)}, minimap {(Minimap && Minimap.Panel && Minimap.Panel.gameObject.activeInHierarchy)}");
            }
        }

        // ================================================================== sim events: toasts, alerts, stats
        CombatView combatView; bool lookedCv; int evSeen;

        void ScanEvents()
        {
            var ev = W.Events;
            if (!lookedCv) { lookedCv = true; combatView = FindFirstObjectByType<CombatView>(); }
            int start = combatView && combatView.enabled ? 0 : (ev.Count < evSeen ? 0 : evSeen);        // CombatView clears the list every frame
            for (int i = start; i < ev.Count; i++)
            {
                var e = ev[i];
                Stats.OnEvent(W, e);
                switch (e.type)
                {
                    case ZEventType.UnitBuilt:
                    {
                        var u = W.GetAny(e.objId);
                        if (u == null || u.Owner != Player) break;
                        bool robot = u.Kind == ZKind.Robot;
                        var target = u;
                        Toast(robot ? "ROBOT MANUFACTURED" : "VEHICLE MANUFACTURED", UnitInfo.ClassGlyph(u.Type), false, () => SelectAndFly(target));
                        Minimap?.PingNew(u.CenterX, u.CenterY);
                        break;
                    }
                    case ZEventType.CannonStored:
                    {
                        var b = W.GetAny(e.objId);
                        if (b == null || b.Owner != Player) break;
                        var target = b;
                        Toast("GUN MANUFACTURED", "cannon", false, () => { if (rts) rts.FlyTo(game.CenterWorld(target)); Production.Open(target); });
                        break;
                    }
                    case ZEventType.FlagCaptured:
                    {
                        var team = (Team)(int)e.value; var old = (Team)e.radius;
                        int fx = e.x, fy = e.y;
                        System.Action fly = () => { if (rts) rts.FlyTo(ZGame.PxToWorld(fx, fy)); };
                        if (team == Player && old != Player) { Toast("TERRITORY CAPTURED", "flag", false, fly); if (e.flag) Toast("RADAR ACTIVATED", "radar", false, fly); }
                        else if (old == Player && team != Player) { Toast("TERRITORY LOST", "flag", true, fly); Minimap?.PingAlert(fx, fy); }
                        Minimap?.FlashZones();
                        break;
                    }
                    case ZEventType.Hitscan:
                    case ZEventType.MissileFired:
                    {
                        var a = W.GetAny(e.objId); var t = W.GetAny(e.otherId);
                        if (t != null && a != null && t.Owner == Player && a.Owner != Player && t.IsUnit) UnderAttack(t.Leader ?? t);
                        break;
                    }
                }
            }
            evSeen = ev.Count;
            Stats.Tick(W);
            CheckFortDanger();
        }

        void SelectAndFly(ZObject u)
        {
            if (u == null || u.Destroyed || u.Owner != Player) return;
            var s = u.Leader ?? u;
            input.Selection.Clear(); input.Selection.Add(s); Bridge?.MarkSelected();
            if (rts) rts.FlyTo(game.CenterWorld(s));
        }

        // ================================================================== top command strip v2 (guide 9.1 + 15)
        RectTransform top, terr; OzText redCount, redOf, blueCount, blueOf, clock, clockSub, terrRed, terrBlue;
        HudBar redBar, blueBar; RawImage redLimit, blueLimit;
        readonly List<Image> segs = new List<Image>(); readonly List<Image> segFlash = new List<Image>(); readonly List<float> segFlashAt = new List<float>();
        readonly List<Team> segTeam = new List<Team>();
        Image split; float splitX = -1f, splitFrom, splitAt = -10f;
        readonly Ticker redTick = new Ticker(), blueTick = new Ticker(), terrRedTick = new Ticker(), terrBlueTick = new Ticker();
        HudButton terrHot, clockHot;
        float nextTop; int lastClockSec = -1;

        /// <summary>Counts roll to the new value over 250 ms (integers step) and flash on change (guide 7.3).</summary>
        sealed class Ticker
        {
            int from, target = int.MinValue; float t0 = -10f; public float flashAt = -10f; public bool up;
            public int Value
            {
                get
                {
                    if (target == int.MinValue) return 0;
                    float k = OzMotion.T(t0, OzMotion.Dur(0.25f));
                    return k >= 1f ? target : Mathf.RoundToInt(Mathf.Lerp(from, target, OzEase.OutCubic(k)));
                }
            }
            public void Set(int v)
            {
                if (target == int.MinValue) { from = target = v; return; }
                if (v == target) return;
                from = Value; up = v > target; target = v; t0 = OzMotion.Now; flashAt = OzMotion.Now;
            }
            /// <summary>Colour: INK normally; increase flashes INK -> team text, decrease ALERT -> INK over 300 ms.</summary>
            public Color Colour(Color normal, Color upFlash)
            {
                float k = OzMotion.T(flashAt, OzMotion.Dur(0.3f, true));
                if (k >= 1f) return normal;
                return up ? Color.Lerp(upFlash, normal, k) : Color.Lerp(OzUi.Alert, normal, k);
            }
        }

        /// <summary>
        /// (x 460-586, y 40-52, top-left px) and "BLUE FORCE" left-aligned on the right (x 976-1116). While the colours are swapped the
        /// two word blocks trade places with their own colours and backing metal; the rest of the right block is refilled from the plain
        /// streaked metal 160 px to its right.</summary>
        static void SwapTopbarWords(Color32[] orig, Color32[] px, int w, int h)
        {
            if (w != 1560 || h != 128) { Debug.LogWarning($"[OpenZHud] team colour swap: topbar is {w}x{h}, words left as baked"); return; }
            const int y0 = 36, y1 = 57;
            void Copy(int sx0, int sx1, int dx0, Color32[] from)
            {
                for (int y = y0; y <= y1; y++) { int row = (h - 1 - y) * w; for (int x = sx0; x <= sx1; x++) px[row + dx0 + (x - sx0)] = from[row + x]; }
            }
            // the side plates carry a faint team tint too (left warm, right cool): trade R and B on both plates (the clock well stays)
            for (int i = 0; i < px.Length; i++) { int x = i % w; if (x < 604 || x > 956) { var c = orig[i]; px[i] = new Color32(c.b, c.g, c.r, c.a); } }
            Copy(1130, 1282, 970, orig);          // clear the right block (plain metal)
            Copy(970, 1122, 440, orig);           // left: BLUE FORCE, right-aligned where RED FORCE ended
            Copy(456, 592, 972, orig);            // right: RED FORCE, left-aligned where BLUE FORCE began
        }

        void BuildTopBar()
        {
            top = Hud.Node("TopStrip", Root).Place(OzLayout.TopStrip);
            AddBlocker(top);
            if (OzKit.GetSprite("Panels/topbar") != null)
            {
                Hud.Shadow(top, "Panels/topbar_mask");
                var face = Hud.Kit(top, "Face", "Panels/topbar", new OzRect(0, 0, OzLayout.TopStrip.w, OzLayout.TopStrip.h), null, Image.Type.Simple);
                if (TeamColors.Swap && face) face.sprite = TeamColors.SwapHues(face.sprite, SwapTopbarWords);
            }
            else Hud.Plate(top, "md");
            // strength gauges (Zod unit count bar x/70, zhud.cpp:891-946), inset in the baked gauge housings
            redBar = new HudBar(top, "RedStrength", TeamColors.Key(Team.Red), false, false, "Bars/str_ticks_7"); redBar.Root.Place(OzLayout.TopBarL);
            blueBar = new HudBar(top, "BlueStrength", TeamColors.Key(Team.Blue), false, true, "Bars/str_ticks_7"); blueBar.Root.Place(OzLayout.TopBarR);
            float seg = OzLayout.TopBarL.w / 7f;                                                         // last segment = unit limit hazard
            redLimit = Hud.Tiled(top, "RedLimit", "Textures/tex_hazard_signal_fine", new OzRect(OzLayout.TopBarL.x + OzLayout.TopBarL.w - seg, OzLayout.TopBarL.y + 1, seg - 1, OzLayout.TopBarL.h - 2), Color.white);
            blueLimit = Hud.Tiled(top, "BlueLimit", "Textures/tex_hazard_signal_fine", new OzRect(OzLayout.TopBarR.x + 1, OzLayout.TopBarR.y + 1, seg - 1, OzLayout.TopBarR.h - 2), Color.white);
            redLimit.gameObject.SetActive(false); blueLimit.gameObject.SetActive(false);
            // counts: 22 u tabular INK, "/ 70" 11 u MUTED on the same baseline 4 u after (blue mirrored, ending at x 700)
            redCount = Hud.Label(top, "RedCount", "0", OzLayout.TopCountSize, OzUi.Ink).At(OzLayout.TopCountLX, OzLayout.TopCountY, OzH.L, false, 80);
            redOf = Hud.Label(top, "RedOf70", "/ 70", OzLayout.TopOf70Size, OzUi.Muted, 0.06f, OzStyle.Plain).At(OzLayout.TopCountLX + 30, OzLayout.TopOf70Y, OzH.L, false, 60);
            blueOf = Hud.Label(top, "BlueOf70", "/ 70", OzLayout.TopOf70Size, OzUi.Muted, 0.06f, OzStyle.Plain).At(OzLayout.TopCountRX, OzLayout.TopOf70Y, OzH.R, false, 60);
            blueCount = Hud.Label(top, "BlueCount", "0", OzLayout.TopCountSize, OzUi.Ink).At(OzLayout.TopCountRX - 34, OzLayout.TopCountY, OzH.R, false, 80);
            var cr = OzLayout.TopClock;
            var cw = Hud.Node("Clock", top).Place(cr);
            var well = Hud.Kit(cw, "Well", "Panels/well_clock", new OzRect(0, 0, cr.w, cr.h), null, Image.Type.Simple);
            if (!OzKit.Available) well.color = OzUi.GM950;
            clock = Hud.Label(cw, "Digits", "0:00:00", OzLayout.ClockDigitsSize, OzUi.Led, 0f, OzStyle.Glow).At(cr.w * 0.5f, OzLayout.ClockDigitsMid, OzH.C, true, cr.w);
            clockSub = Hud.Label(cw, "Sub", "", OzLayout.ClockSubSize, OzUi.AmberLo, 0.24f, OzStyle.Plain).At(cr.w * 0.5f, OzLayout.ClockSubMid, OzH.C, true, cr.w);
            clockW = cr.w; clockH = cr.h;
            Hud.Kit(cw, "Glass", "Panels/well_clock_glass", new OzRect(0, 0, cr.w, cr.h), null, Image.Type.Simple).enabled = OzKit.Available;
            clockHot = Hotspot(top, "ClockHot", cr, "MISSION TIME|Game time since the start (Zod clock). Stops while paused.|");
            Hotspot(top, "RedHot", new OzRect(20, 4, 280, 56), TeamColors.Name(Team.Red) + " FORCE|Units on the field (robots count one by one). At 70 the factories pause.|");
            Hotspot(top, "BlueHot", new OzRect(480, 4, 280, 56), TeamColors.Name(Team.Blue) + " FORCE|Units on the field (robots count one by one). At 70 the factories pause.|");

            // territory tab: one segment per zone, red packed from the left, blue from the right, neutral between
            terr = Hud.Node("Territory", Root).Place(OzLayout.TerrTab);
            AddBlocker(terr);
            if (OzKit.GetSprite("Panels/territory_tab") != null)
            {
                Hud.Shadow(terr, "Panels/territory_tab_mask");
                Hud.Kit(terr, "Face", "Panels/territory_tab", new OzRect(0, 0, OzLayout.TerrTab.w, OzLayout.TerrTab.h), null, Image.Type.Simple);
            }
            else Hud.Plate(terr, "sm", 0, null, null, false, false);
            terr.SetSiblingIndex(0);                                                                    // hangs under the strip (overlaps it by 2 u)
            int zones = Mathf.Max(1, W.Zones.Count);
            var sr = OzLayout.TerrSegs; float segw = sr.w / zones;
            for (int i = 0; i < zones; i++)
            {
                var s = Hud.Kit(terr, "Seg" + i, "Bars/seg_neutral", new OzRect(sr.x + i * segw + OzLayout.TerrSegGap * 0.5f, sr.y, segw - OzLayout.TerrSegGap, sr.h), null);
                segs.Add(s); segTeam.Add(Team.Neutral);
                var f = Hud.Img(terr, "Flash" + i, OzUi.A(OzUi.Ink, 0f)); f.rectTransform.Place(new OzRect(sr.x + i * segw + OzLayout.TerrSegGap * 0.5f, sr.y, segw - OzLayout.TerrSegGap, sr.h));
                segFlash.Add(f); segFlashAt.Add(-10f);
            }
            split = Hud.KitNative(terr, "Split", "Bars/split_marker", sr.x, OzLayout.TerrSplitY);
            Hud.Icon(terr, "FlagL", "flag", OzLayout.TerrFlagLX, OzLayout.TerrFlagY, OzLayout.TerrFlagSize, OzUi.Muted);
            Hud.Icon(terr, "FlagR", "flag", OzLayout.TerrFlagRX, OzLayout.TerrFlagY, OzLayout.TerrFlagSize, OzUi.Muted);
            terrRed = Hud.Label(terr, "Red", "0", OzLayout.TerrCountSize, OzUi.TeamText(Team.Red)).At(OzLayout.TerrCountLX, OzLayout.TerrCountMid, OzH.R, true, 40);
            terrBlue = Hud.Label(terr, "Blue", "0", OzLayout.TerrCountSize, OzUi.TeamText(Team.Blue)).At(OzLayout.TerrCountRX, OzLayout.TerrCountMid, OzH.L, true, 40);
            terrHot = Hotspot(terr, "Hot", new OzRect(0, 0, OzLayout.TerrTab.w, OzLayout.TerrTab.h), "TERRITORY|");
        }

        void RefreshTopBar()
        {
            float now = OzMotion.Now;
            if (now >= nextTop)
            {
                nextTop = now + 0.1f;
                int r = W.UnitCount(Team.Red), b = W.UnitCount(Team.Blue);
                redTick.Set(r); blueTick.Set(b);
                redBar.Set(r / (float)ZWorld.MAX_UNITS_PER_TEAM); blueBar.Set(b / (float)ZWorld.MAX_UNITS_PER_TEAM);
                bool rl = W.UnitLimitReached(Team.Red), bl = W.UnitLimitReached(Team.Blue);
                redLimit.gameObject.SetActiveFast(rl && redLimit.texture); blueLimit.gameObject.SetActiveFast(bl && blueLimit.texture);

                // territory (zones: owned per team; red from the left, blue from the right)
                int zr = 0, zb = 0, total = W.Zones.Count;
                foreach (var z in W.Zones) { if (z.Owner == Team.Red) zr++; else if (z.Owner == Team.Blue) zb++; }
                terrRedTick.Set(zr); terrBlueTick.Set(zb);
                for (int i = 0; i < segs.Count; i++)
                {
                    var t = i < zr ? Team.Red : i >= segs.Count - zb ? Team.Blue : Team.Neutral;
                    if (segTeam[i] == t) continue;
                    segTeam[i] = t; segFlashAt[i] = now;
                    Hud.Dress(segs[i], "Bars/seg_" + TeamColors.Key(t), OzKit.Available ? (Color?)null : OzUi.Mark(t));
                }
                float want = OzLayout.TerrSegs.x + zr * (OzLayout.TerrSegs.w / Mathf.Max(1, segs.Count)) - 1.5f;
                if (splitX < 0) splitX = splitFrom = want;
                if (!Mathf.Approximately(want, splitX)) { splitFrom = CurrentSplit(); splitX = want; splitAt = now; }
                int flags = W.Flags.Count, fr = 0; foreach (var f in W.Flags) if (f.Owner == Player) fr++;
                int bonus = Mathf.RoundToInt(50f * (float)W.TeamFlagFraction(Player));
                if (zr != tipZr || zb != tipZb || total != tipTotal || fr != tipFr || flags != tipFlags || bonus != tipBonus)
                {
                    tipZr = zr; tipZb = zb; tipTotal = total; tipFr = fr; tipFlags = flags; tipBonus = bonus;
                    terrHot.Tooltip = $"TERRITORY|Red {zr} · Blue {zb} · Neutral {total - zr - zb} zones. You hold {fr} of {flags} flags.\nBuild speed −{bonus} % from your flags.|";
                }
            }
            // tickers + flashes every frame (strings are only built when the shown number changes: no per-frame garbage)
            SetNum(redCount, ref shownRed, redTick.Value); SetNum(blueCount, ref shownBlue, blueTick.Value);
            bool rl2 = W.UnitLimitReached(Team.Red), bl2 = W.UnitLimitReached(Team.Blue);
            redCount.color = rl2 ? (Color)OzUi.Signal : redTick.Colour(OzUi.Ink, OzUi.TeamText(Team.Red));
            blueCount.color = bl2 ? (Color)OzUi.Signal : blueTick.Colour(OzUi.Ink, OzUi.TeamText(Team.Blue));
            PlaceOf70();
            SetNum(terrRed, ref shownTerrRed, terrRedTick.Value); SetNum(terrBlue, ref shownTerrBlue, terrBlueTick.Value);
            terrRed.color = terrRedTick.Colour(OzUi.TeamText(Team.Red), OzUi.Ink); terrBlue.color = terrBlueTick.Colour(OzUi.TeamText(Team.Blue), OzUi.Ink);
            for (int i = 0; i < segs.Count; i++)
            {
                float k = OzMotion.T(segFlashAt[i], OzMotion.Dur(0.3f, true));
                var c = segFlash[i].color; float a = k < 1f ? 1f - k : 0f;
                if (Mathf.Abs(c.a - a) > 0.004f) { c.a = a; segFlash[i].color = c; }
            }
            if (split) { var p = split.rectTransform.anchoredPosition; float x = CurrentSplit(); if (Mathf.Abs(p.x - x) > 0.01f) split.rectTransform.anchoredPosition = new Vector2(x, p.y); }

            // clock (Zod: game time h:mm:ss, "!" past 9 h); PAUSED blinks 0.5 s in AMBER, SPEED xN in AMBER
            int sec = (int)W.Time;
            if (sec != lastClockSec) { lastClockSec = sec; clock.Set(OzText.Tab(UnitInfo.Clock(W.Time))); }
            bool paused = game.paused || (Pause && Pause.IsOpen);
            string sub = W.GameOver ? "GAME OVER" : paused ? "PAUSED" : Mathf.Abs(game.gameSpeed - 1f) > 0.01f ? SpeedLabel(game.gameSpeed) : "";
            sub = OzSpeedKeys.ClockLine(sub, game, W.GameOver || paused);                               // speed keys: the new speed for 1.5 s, also at x1
            clockSub.Set(sub);
            bool hasSub = sub.Length > 0;
            if (hasSub != clockHasSub)
            {   // the digits sit centred in the well when there is no state line under them
                clockHasSub = hasSub;
                clock.At(clockW * 0.5f, hasSub ? OzLayout.ClockDigitsMid : clockH * 0.5f, OzH.C, true, clockW);
            }
            clockSub.color = paused || sub.StartsWith("SPEED") ? (Color)OzUi.Amber : (Color)OzUi.AmberLo;
            if (!paused && !W.GameOver && OzSpeedKeys.NoticeFresh) clockSub.color = OzSpeedKeys.NoticeColor;   // speed keys: bright after a step, the limit colour at an end stop
            clock.Alpha = paused && !OzMotion.Blink(0.5f) ? 0.25f : 1f;
        }

        float clockW, clockH; bool clockHasSub = true;

        float CurrentSplit() => Mathf.Lerp(splitFrom, splitX, OzEase.OutCubic(OzMotion.T(splitAt, OzMotion.Dur(OzMotion.Medium))));

        int shownRed = int.MinValue, shownBlue = int.MinValue, shownTerrRed = int.MinValue, shownTerrBlue = int.MinValue;
        static void SetNum(OzText t, ref int shown, int v) { if (v == shown) return; shown = v; t.Set(OzText.Tab(v.ToString())); }

        float speedShown = -1f; string speedLabel = "";
        string SpeedLabel(float s) { if (!Mathf.Approximately(s, speedShown)) { speedShown = s; speedLabel = $"SPEED ×{s:0.##}"; } return speedLabel; }

        int tipZr = -1, tipZb = -1, tipTotal = -1, tipFr = -1, tipFlags = -1, tipBonus = int.MinValue;

        float lastRedW = -1, lastBlueW = -1;
        void PlaceOf70()
        {
            float rw = redCount.preferredWidth, bw = blueOf.preferredWidth;
            if (!Mathf.Approximately(rw, lastRedW)) { lastRedW = rw; redOf.At(OzLayout.TopCountLX + rw + OzLayout.TopOf70Gap, OzLayout.TopOf70Y, OzH.L, false, 60); }
            if (!Mathf.Approximately(bw, lastBlueW)) { lastBlueW = bw; blueCount.At(OzLayout.TopCountRX - bw - OzLayout.TopOf70Gap, OzLayout.TopCountY, OzH.R, false, 80); }
        }

        // ================================================================== menu button (guide 9.12)
        HudButton menuBtn;
        void BuildMenuButton()
        {
            var r = OzLayout.MenuButton;
            menuBtn = HudButton.Make(Root, "MenuButton", "MENU", r.Size, HudButtonKind.Small, 13, 0.14f, HudButtonStyle.Normal, true, OzH.L);
            menuBtn.Rt.Place(r);
            menuBtn.Label.FillParent(OzH.L, 16, 0, 0, 0);
            Hud.Keycap(menuBtn.Content, "Esc", "ESC", r.w - 8, (r.h - 14) * 0.5f);
            menuBtn.Tooltip = "MENU|Pause the game: options, controls, restart, quit.|ESC";
            menuBtn.OnClick = c => { if (Pause) Pause.Open(); };
            AddBlocker(menuBtn.Rt);
        }

        // ================================================================== unit panel (guide 9.3 + kit 4)
        RectTransform unitPanel; PortraitView portrait; Image groupBadge, divider, weaponIcon, nadeIcon;
        OzText uName, uSub, uCount, uWeaponLabel, uWeapon, uNadeLabel, uNades, uHpLabel, uHp;
        UnitIcon typeIcon; Image typeWell; HudBar uHealth; RectTransform uStats;
        ZObject hudUnit; int selHash = -1; ZObject shownUnit; float unitAt = -10f;
        float nextUnit;

        void BuildUnitPanel()
        {
            unitPanel = Hud.Node("UnitPanel", Root).Place(PanelRect());
            AddBlocker(unitPanel);
            Hud.Plate(unitPanel, "lg", 0f, null, OzUi.TeamPaint(Player), true);
            string lvl = game.level == "L17" ? "city" : "desert";
            portrait = new PortraitView(unitPanel, OzLayout.UnitPortrait, lvl);
            groupBadge = Hud.Kit(unitPanel, "Group", "Badges/badge_1", new OzRect(OzLayout.UnitBadgeX, OzLayout.UnitBadgeY, 24, 24), null, Image.Type.Simple);
            groupBadge.gameObject.SetActive(false);
            uName = Hud.Label(unitPanel, "Name", "", OzLayout.UnitNameSize, OzUi.Ink, -0.01f).At(OzLayout.UnitNameX, OzLayout.UnitNameY, OzH.L, false, OzLayout.UnitNameMaxW);   // ends before the type well (was 260: the well covered "NO SELECTION")
            uSub = Hud.Label(unitPanel, "Sub", "", OzLayout.UnitSubSize, OzUi.Muted, 0.16f, OzStyle.Plain).At(OzLayout.UnitNameX, OzLayout.UnitSubY, OzH.L, false, 280);
            var tw = OzLayout.UnitTypeWell;
            typeWell = Hud.Kit(unitPanel, "TypeWell", "Panels/well_4", tw, null); if (!OzKit.Available) typeWell.color = OzUi.GM950;
            typeIcon = new UnitIcon(unitPanel, "TypeIcon", new Vector2(OzLayout.UnitTypeIconSize, OzLayout.UnitTypeIconSize), false, 0f);
            typeIcon.Rt.Place(new OzRect(tw.x + 8, tw.y + 4, OzLayout.UnitTypeIconSize, OzLayout.UnitTypeIconSize));
            uCount = Hud.Label(unitPanel, "Count", "", 16, OzUi.Ink).At(OzLayout.UnitTypeCountRX, OzLayout.UnitTypeCountMid, OzH.R, true, 60);
            divider = Hud.Img(unitPanel, "Divider", OzUi.A(OzUi.Khaki, 0.17f));
            divider.rectTransform.Place(new OzRect(OzLayout.UnitDividerX0, OzLayout.UnitDividerY, OzLayout.UnitDividerX1 - OzLayout.UnitDividerX0, 1));
            uStats = Hud.Node("Stats", unitPanel); uStats.Place(new OzRect(0, 0, OzLayout.UnitPanel.w, OzLayout.UnitPanel.h));
            var sg = uStats.gameObject.AddComponent<CanvasGroup>(); sg.blocksRaycasts = false;
            uWeaponLabel = Hud.Label(uStats, "WeaponLabel", "WEAPON", 9, OzUi.Muted, 0.22f, OzStyle.Plain).At(OzLayout.UnitNameX, OzLayout.UnitLabelY);
            weaponIcon = Hud.Icon(uStats, "WeaponIcon", "rifle", OzLayout.UnitWeaponIconX, OzLayout.UnitWeaponIconY, OzLayout.UnitWeaponIconSize, OzUi.Ink);
            uWeapon = Hud.Label(uStats, "Weapon", "", 16, OzUi.Ink, 0.04f).At(OzLayout.UnitWeaponX, OzLayout.UnitValueY, OzH.L, false, 118);
            uNadeLabel = Hud.Label(uStats, "NadeLabel", "GRENADES", 9, OzUi.Muted, 0.22f, OzStyle.Plain).At(OzLayout.UnitNadeLabelX, OzLayout.UnitLabelY);
            nadeIcon = Hud.Icon(uStats, "NadeIcon", "grenade", OzLayout.UnitNadeIconX, OzLayout.UnitWeaponIconY, OzLayout.UnitNadeIconSize, OzUi.Amber);
            uNades = Hud.Label(uStats, "Nades", "", 16, OzUi.Ink).At(OzLayout.UnitNadeX, OzLayout.UnitValueY, OzH.L, false, 90);
            uHpLabel = Hud.Label(uStats, "HpLabel", "HEALTH", 9, OzUi.Muted, 0.22f, OzStyle.Plain).At(OzLayout.UnitNameX, OzLayout.UnitHpLabelY);
            uHp = Hud.Label(uStats, "Hp", "", 13, OzUi.Ink).At(OzLayout.UnitHpValueRX, OzLayout.UnitHpValueY, OzH.R, false, 80);
            uHealth = new HudBar(uStats, "Health", "health", true, false, "Bars/hp_ticks_4"); uHealth.Root.Place(OzLayout.UnitHpBar);
            // on the single selected ejectable unit (zplayer_events.cpp:390-425), which misses easily in 3D. A real EXIT CREW control
            // (tab above the panel + E key) ejects every selected own gun / APC with a crew, through the same PlayerInput.EjectUnit.
            exitBtn = HudButton.Make(unitPanel, "ExitCrew", "EXIT CREW", new Vector2(132, 32), HudButtonKind.Small, 12, 0.16f);
            exitBtn.Rt.Place(new OzRect(330, -38, 132, 32));
            exitBtn.Tooltip = OzBindings.Cap(OzAction.ExitCrew) + " · EXIT CREW|The crew of the selected gun or APC gets out (in Z: right-click the unit itself).|" + OzBindings.Cap(OzAction.ExitCrew);   // menu_exe_v1
            exitBtn.OnClick = c => ExitSelected();
            AddBlocker(exitBtn.Rt);
            exitBtn.Rt.gameObject.SetActive(false);
            portraitHot = Hotspot(unitPanel, "PortraitHot", OzLayout.UnitPortrait, PortraitTip);
            portraitHot.OnClick = c => ClickCall();
        }

        /// <summary>Guide 8.1: 12 u left of the minimap; above it on canvases narrower than 1620 u (4:3).</summary>
        OzRect PanelRect() => OzLayout.UnitPanelFor(CanvasWidth);                                         // menu_exe_v1: wide / raised (left of the minimap) / above it
        int panelMode = -1;

        /// <summary>The HUD shows a random member of the selection (zplayer.cpp:3018-3027), re-picked when the selection changes.</summary>
        public ZObject HudUnit => hudUnit;

        int GroupOf(ZObject u)
        {
            var groups = Groups;
            for (int d = 1; d <= 10; d++) { int k = d % 10; if (groups.TryGetValue(k, out var g) && g.Contains(u)) return k; }
            return -1;
        }

        HudButton exitBtn; float panelHide;
        bool CanExit(ZObject o) => o != null && !o.Destroyed && o.Owner == Player && o.CanEjectCrew() && !W.GameOver;   // apc_v1 (DV15): an APC exits its passengers only
        public void ExitSelected()
        {
            bool any = false;
            foreach (var o in new List<ZObject>(input.Selection)) if (CanExit(o)) any |= input.EjectUnit(o);
            if (!any) AudioDirector.UI("ui_order_invalid");
        }

        // stays as before, always shown with the NO SELECTION state)
        public float UnitPanelHide => panelHide;
        public RectTransform ExitButtonRect => exitBtn != null ? exitBtn.Rt : null;
        public RectTransform UnitPanelRect => unitPanel;

        void RefreshUnitPanel()
        {
            var sel = input.Selection;
            bool canExit = false; foreach (var o in sel) if (CanExit(o)) { canExit = true; break; }
            exitBtn.Rt.gameObject.SetActiveFast(canExit);
            int h = sel.Count; foreach (var o in sel) h = h * 31 + o.Id;
            bool changed = false;
            if (h != selHash || hudUnit == null || !sel.Contains(hudUnit) || hudUnit.Destroyed)
            {
                selHash = h;
                var was = hudUnit;
                hudUnit = sel.Count > 0 ? sel[Random.Range(0, sel.Count)] : null;
                changed = true;
                if (hudUnit != null) { unitAt = OzMotion.Now; portrait.Reselect(hudUnit == was); }
            }
            TrackPortraitHits();                                                                            // ui_combat_v1
            portrait.Tick();
            int narrow = OzLayout.UnitPanelMode(CanvasWidth);                                              // menu_exe_v1: 0 wide, 1 raised, 2 above the minimap
            if (narrow != panelMode && introDone)
            {
                panelMode = narrow; unitPanel.Place(PanelRect());
                foreach (var s in slides) if (s.rt == unitPanel) s.home = unitPanel.anchoredPosition;                // the exit slides from the new place
            }
            var u = hudUnit;
            VeteranMarks.PortraitStar(unitPanel, u);                                                        // veterancy_v1 DV12: veteran star
            if (u != null) EndCall();
            if (u == null)
            {
                var call = LiveCall();
                if (call != null) { ShowCall(call); return; }                                                  // NB35: nothing selected: the live transmission
                if (changed || shownUnit != null || callShown != null)
                {
                    EndCall();
                    shownUnit = null;
                    portrait.Idle();
                    groupBadge.gameObject.SetActiveFast(false);
                    uName.Set("NO SELECTION"); uName.FontSize = OzLayout.UnitNameSize; uName.color = OzUi.Dim;
                    uSub.Set("SELECT UNITS · CLICK FORT OR FACTORY TO BUILD");
                    uCount.Set(""); typeIcon.Set(null, Player); typeIcon.Rt.gameObject.SetActiveFast(false); typeWell.gameObject.SetActiveFast(false);
                    uWeapon.Set("—"); uNades.Set(""); uNadeLabel.Set(""); nadeIcon.enabled = false; weaponIcon.enabled = false;
                    uHp.Set(""); uHealth.Set(0f, 0f);
                    SetStatsAlpha(0f);
                }
                return;
            }
            float now = OzMotion.Now;
            if (now < nextUnit && !changed && u == shownUnit) { AnimateUnit(); return; }
            nextUnit = now + 0.1f; shownUnit = u;
            bool robot = u.Kind == ZKind.Robot;
            // face only if that art is missing; an empty vehicle keeps its icon
            Texture crewArt = !robot && u.Drivers.Count > 0 ? UnitArt.VehiclePortrait(u.Type, u.Owner) : null;
            string face = robot ? u.Type : u.Drivers.Count > 0 && crewArt == null ? u.DriverType : null;
            portrait.Show(face, u.Type, u.Owner, robot ? null : "cockpit", crewArt);
            int g = GroupOf(u);
            groupBadge.gameObject.SetActiveFast(g >= 0);
            if (g >= 0) Hud.Dress(groupBadge, "Badges/badge_" + g, OzKit.Available ? (Color?)null : OzUi.GM950, Image.Type.Simple);

            string name = UnitInfo.Name(u.Type);
            uName.Set(name); uName.color = OzUi.Ink;
            uName.FontSize = OzLayout.UnitNameSize;
            if (uName.preferredWidth > OzLayout.UnitNameMaxW) uName.FontSize = OzLayout.UnitNameSmall;
            int squad = 1; if (robot) foreach (var mn in u.Minions) if (!mn.Destroyed) squad++;
            int sameType = 0; foreach (var o in sel) if (o.Type == u.Type) sameType++;
            string more = sel.Count > 1 ? $" · +{sel.Count - 1} MORE SELECTED" : "";
            var hu = PanelHealthOf(u);                                                                     // the squad member under the pointer, else the unit itself
            string sub = robot ? (squad > 1 ? $"{(hu != u ? "SQUAD MEMBER" : "SQUAD LEADER")} · {squad} ROBOTS" : "1 ROBOT")
                       : u.Drivers.Count > 0 ? $"{(u.Kind == ZKind.Cannon ? "GUNNER" : "DRIVER")} · {UnitInfo.Name(u.DriverType)}{(u.Drivers.Count > 1 ? " ×" + u.Drivers.Count : "")}"
                         + (u.IsApc && ZWorld.ApcCrewDeviation ? (u.Passengers.Count > 0 ? $" · PASSENGERS {UnitInfo.Name(u.PassengerType)} ×{u.Passengers.Count}" : " · ROOM FOR ONE SQUAD") : "")   // apc_v1 DV15
                       : "EMPTY · ANY ROBOT CAN CAPTURE";
            uSub.Set(sub + more);
            typeIcon.Rt.gameObject.SetActiveFast(true); typeWell.gameObject.SetActiveFast(true);
            if (robot) typeIcon.SetGlyph("robot"); else typeIcon.Set(u.Type, u.Owner);
            uCount.Set(OzText.Tab("×" + (robot ? squad : sameType)));
            uWeapon.Set(UnitInfo.Weapon(u));
            weaponIcon.enabled = true; Hud.SetIcon(weaponIcon, UnitInfo.WeaponGlyph(u), OzLayout.UnitWeaponIconSize);
            bool nades = robot && u.Type != "tough";                                                       // Zod: robots except Tough (zhud.cpp:794-819)
            if (nades) { uNadeLabel.Set("GRENADES"); nadeIcon.enabled = true; Hud.SetIcon(nadeIcon, "grenade", 24); nadeIcon.color = OzUi.Amber; uNades.Set(OzText.Tab($"{u.Grenades:00}")); }
            else if (!robot) { uNadeLabel.Set("CREW"); nadeIcon.enabled = true; Hud.SetIcon(nadeIcon, "robot", 24); nadeIcon.color = OzUi.Ink2; uNades.Set(OzText.Tab($"{u.CrewAboard:00}"));   /* apc_v1 DV15: driver + passengers */ }
            else { uNadeLabel.Set(""); nadeIcon.enabled = false; uNades.Set(""); }
            // Zod's absolute scale (zhud.cpp:948-1016): green = health / 10000, lost colour up to max health / 10000
            float hp = hu.MaxHealth > 0 ? hu.Health / (float)hu.MaxHealth : 0;
            uHp.Set(OzText.Tab($"{Mathf.CeilToInt(hp * 100)}%"));
            uHp.color = hp < 0.3f ? (Color)OzUi.Alert : (Color)OzUi.Ink;
            uHealth.SetKind(hp < 0.3f ? "health_low" : "health");
            AnimateUnit();
        }

        /// <summary>Scan-in on a new unit (guide 7.3): name slides 8 u from the left and fades (140 ms), health fills
        /// 0 -> value (200 ms), weapon / grenade values fade in after 60 ms (120 ms).</summary>
        void AnimateUnit()
        {
            var u = shownUnit; if (u == null) return;
            float kName = OzEase.OutCubic(OzMotion.T(unitAt, OzMotion.Dur(0.14f)));
            var p = uName.rectTransform.anchoredPosition; float x = OzLayout.UnitNameX - 8f * (1f - kName);
            if (Mathf.Abs(p.x - x) > 0.01f) uName.rectTransform.anchoredPosition = new Vector2(x, p.y);
            uName.Alpha = kName;
            SetStatsAlpha(OzMotion.T(unitAt, OzMotion.Dur(0.12f, true), 0.06f));
            float kHp = OzEase.OutCubic(OzMotion.T(unitAt, OzMotion.Dur(0.2f)));
            var hu = PanelHealthOf(u);
            float hpAbs = hu.Health / (float)ZConst.MAX_UNIT_HEALTH, maxAbs = hu.MaxHealth / (float)ZConst.MAX_UNIT_HEALTH;
            if (!absoluteHealthBar) { hpAbs = hu.MaxHealth > 0 ? hu.Health / (float)hu.MaxHealth : 0f; maxAbs = 1f; }
            uHealth.Set(hpAbs * kHp, maxAbs * kHp);
        }

        /// <summary>The robot whose health the unit panel shows for the selected unit u: while the pointer is on a member of u's squad,
        /// that member (each squad member has its own health; the panel then reads SQUAD MEMBER); else u itself.</summary>
        ZObject PanelHealthOf(ZObject u)
        {
            var m = Overlay ? Overlay.HoverMember : null;
            return u != null && m != null && m != u && !m.Destroyed && m.KillAt < 0 && m.Leader == u ? m : u;
        }

        CanvasGroup statsCg;

        // ui_combat_v1: portrait hit static. Zod flashes a hit unit's sprite (ZObject::DoHitEffect, zobject.cpp:4608-4611; a hit
        // driver too, DoDriverHitEffect :4718-4721) and the A portrait plays "we're under attack" (zplayer_events.cpp:799-812).
        // OpenZ's comms screen answers hits on the SHOWN unit, its squad or its driver with a short burst of static (sim health
        // deltas, so bullets, shells, splash, fire and snipes all count; repairs, boarding and deselection do not).
        ZObject hitUnit; readonly List<ZObject> hitWatch = new List<ZObject>(); readonly List<int> hitHp = new List<int>(); int hitDriverHp = -1;

        void TrackPortraitHits()
        {
            var u = hudUnit;
            if (u != hitUnit) { hitUnit = u; hitWatch.Clear(); hitHp.Clear(); hitDriverHp = -1; }
            if (u == null) return;
            float lost = 0f;
            for (int i = 0; i < hitWatch.Count; i++) { int hp = Mathf.Max(0, hitWatch[i].Health); if (hp < hitHp[i]) lost += hitHp[i] - hp; }
            int driver = u.Kind != ZKind.Robot && u.Drivers.Count > 0 ? u.Drivers[0] : -1;
            bool driverHit = driver >= 0 && hitDriverHp >= 0 && driver < hitDriverHp;
            hitWatch.Clear(); hitHp.Clear();
            int max = Mathf.Max(1, u.MaxHealth);
            hitWatch.Add(u); hitHp.Add(Mathf.Max(0, u.Health));
            if (u.Kind == ZKind.Robot) foreach (var m in u.Minions) if (m != null && !m.Destroyed) { hitWatch.Add(m); hitHp.Add(m.Health); max += Mathf.Max(1, m.MaxHealth); }
            hitDriverHp = driver;
            if (lost <= 0f && !driverHit) return;
            float amp = 0.4f + 3f * lost / max;                                                                // a scratch flickers, a heavy hit or a lost squad member is full static
            if (driverHit) amp = Mathf.Max(amp, 0.85f);                                                         // the crew itself was hit
            portrait.Hit(amp);
        }
        void SetStatsAlpha(float a) { if (!statsCg) statsCg = uStats.GetComponent<CanvasGroup>(); if (statsCg && Mathf.Abs(statsCg.alpha - a) > 0.004f) statsCg.alpha = a; }

        // AudioDirector's distress episode (an off-screen player group under fire; radio discipline) notes each call
        // it puts on the radio (HelpCallId / HelpCallAt); with NOTHING selected the comms screen then shows that unit live for
        // CallShowSec after its last call: its portrait with a hand-held shake, heavy rolling static, signal tears and sparks (each
        // spark burst crackles: ui_transmission), "LIVE" blinking over its name. A click on the screen selects it and flies the camera
        // there (SelectAndFly: RtsCamera.FlyTo, ui_camera_jump); a selection, the unit's death or the end of the calls ends it.
        const string PortraitTip = "COMMS|The selected unit (a random member of the selection, like Zod). Vehicles and guns show their crew at the controls.|";
        const string CallTip = "INCOMING CALL|A unit is calling for help. Click to select it and fly the camera there.|";
        public const float CallShowSec = 8f;
        HudButton portraitHot; ZObject callShown;
        /// <summary>Proof counters: transmissions shown, clicks that flew there.</summary>
        public int CallShows { get; private set; }
        public int CallClicks { get; private set; }
        public ZObject CallShown => callShown;

        ZObject LiveCall()
        {
            if (W == null || W.GameOver || !hudVisible) return null;
            int id = AudioDirector.HelpCallId;
            if (id < 0 || Time.unscaledTime - AudioDirector.HelpCallAt > CallShowSec) return null;
            var o = W.GetAny(id);
            if (o == null || o.Destroyed || o.KillAt >= 0 || o.Owner != Player || !o.IsUnit) return null;
            return o;
        }

        void ShowCall(ZObject o)
        {
            if (callShown != o)
            {
                callShown = o; shownUnit = null;
                portrait.Reselect(false); portrait.SetTransmit(true);
                AudioDirector.UI("ui_transmission");                                                          // the signal cuts in: a crackle
                groupBadge.gameObject.SetActiveFast(false);
                typeIcon.Rt.gameObject.SetActiveFast(true); typeWell.gameObject.SetActiveFast(true);
                if (o.Kind == ZKind.Robot) typeIcon.SetGlyph("robot"); else typeIcon.Set(o.Type, o.Owner);
                uCount.Set(""); uWeapon.Set(""); uNades.Set(""); uHp.Set("");
                SetStatsAlpha(0f);
                uSub.Set("CALLING FOR HELP · CLICK THE SCREEN TO FLY THERE");
                if (portraitHot != null) portraitHot.Tooltip = CallTip;
                CallShows++;
            }
            bool robot = o.Kind == ZKind.Robot;
            Texture crewArt = !robot && o.Drivers.Count > 0 ? UnitArt.VehiclePortrait(o.Type, o.Owner) : null;
            string face = robot ? o.Type : o.Drivers.Count > 0 && crewArt == null ? o.DriverType : null;
            portrait.Show(face, o.Type, o.Owner, robot ? null : "cockpit", crewArt);
            uName.Set("LIVE · " + UnitInfo.Name(o.Type));
            uName.FontSize = OzLayout.UnitNameSize;
            if (uName.preferredWidth > OzLayout.UnitNameMaxW) uName.FontSize = OzLayout.UnitNameSmall;
            uName.Alpha = 1f;
            uName.color = OzMotion.Reduced || OzMotion.Blink(OzMotion.BlinkSlow) ? (Color)OzUi.Alert : (Color)OzUi.Ink;
            if (portrait.Sparked) AudioDirector.UI("ui_transmission");                                        // every spark burst crackles
        }

        void EndCall()
        {
            if (callShown == null) return;
            callShown = null; portrait.SetTransmit(false);
            if (portraitHot != null) portraitHot.Tooltip = PortraitTip;
        }

        void ClickCall()
        {
            var o = callShown;
            if (o == null || o.Destroyed || hudUnit != null) return;
            CallClicks++;
            SelectAndFly(o);                                                                                   // like the A tab: select + smooth flight (ui_camera_jump)
        }

        /// <summary>Portrait bay (guide 9.3 + kit Portrait/*): Mask(portrait_mask) > level backdrop (desert dusk / city night /
        /// vehicle cockpit) > the robot face (BS-01 set first) > static noise > scan band > portrait_frame. Nothing selected =
        /// the idle comms screen (NO SIGNAL) with a scan band every 4 s.</summary>
        sealed class PortraitView
        {
            readonly RectTransform reveal; readonly RectMask2D revealMask; readonly Image backdrop, nosignal, scan; readonly RawImage art, noise;
            readonly float size; readonly string levelBg;
            string shownKey, shownBg; float t0 = -10f; bool sweepOnly, idle = true; float idleAt, flickerAt = -10f, nextFlicker; int noiseFrame = -1;
            static Texture2D[] noiseTex;
            // ui_combat_v1 hit static: a burst of static / tearing when the portrait's unit (or its squad / its driver) is hit
            RectTransform bay; Vector2 bayHome; float glitchAt = -10f, glitchAmp, glitchDur, nextTear; Vector2 tear; Color tint = Color.white; bool glitching;
            public const float HitGap = 0.45f;                                                             // quiet time between two bursts

            public PortraitView(Transform parent, OzRect r, string levelBg)
            {
                size = r.w; this.levelBg = levelBg;
                var root = Hud.Node("Portrait", parent); root.Place(r);
                bay = root; bayHome = root.anchoredPosition;                                                    // ui_combat_v1: the bay shakes a hair on a hit
                var mask = Hud.Kit(root, "Mask", "Portrait/portrait_mask", new OzRect(0, 0, r.w, r.h), null, Image.Type.Simple);
                if (OzKit.GetSprite("Portrait/portrait_mask") != null) mask.gameObject.AddComponent<Mask>().showMaskGraphic = false;
                else mask.color = OzUi.GM950;
                backdrop = Hud.Kit(mask.transform, "Backdrop", "Portrait/portrait_bg_" + levelBg, new OzRect(0, 0, r.w, r.h), null, Image.Type.Simple);
                shownBg = levelBg;
                reveal = Hud.Node("Reveal", mask.transform); reveal.Fill(); revealMask = reveal.gameObject.AddComponent<RectMask2D>();
                var ar = Hud.Node("Art", reveal); ar.Fill(); art = ar.gameObject.AddComponent<RawImage>(); art.raycastTarget = false; art.enabled = false;
                nosignal = Hud.Kit(mask.transform, "NoSignal", "Portrait/portrait_nosignal", new OzRect(0, 0, r.w, r.h), null, Image.Type.Simple);
                noiseTex ??= new[] { OzKit.GetTexture("Textures/tex_noise_0"), OzKit.GetTexture("Textures/tex_noise_1"), OzKit.GetTexture("Textures/tex_noise_2"), OzKit.GetTexture("Textures/tex_noise_3") };
                var nr = Hud.Node("Noise", mask.transform); nr.Fill(); noise = nr.gameObject.AddComponent<RawImage>(); noise.raycastTarget = false;
                noise.texture = noiseTex[0]; noise.enabled = noiseTex[0] != null; noise.color = new Color(1, 1, 1, 0.12f);
                if (noiseTex[0]) noise.uvRect = new Rect(0, 0, r.w * 2f / noiseTex[0].width, r.h * 2f / noiseTex[0].height);
                scan = Hud.Kit(mask.transform, "Scan", "Bits/scan_band", new OzRect(0, -24, r.w, 24), OzUi.A(OzUi.AmberHi, 0.55f), Image.Type.Simple);
                Hud.Kit(root, "Frame", "Portrait/portrait_frame", new OzRect(0, 0, r.w, r.h), null, Image.Type.Simple).enabled = OzKit.Available;
                idleAt = OzMotion.Now;
            }

            public void Idle() { if (idle) return; idle = true; idleAt = OzMotion.Now; shownKey = null; }

            // signal tears (the hit glitch, every 0.5-1.4 s) and spark bursts from the screen's edge (every 1.2-2.6 s, Sparked = the HUD
            // plays their crackle). Reduced motion: static and sparks only, no shake.
            bool transmit; float txAt, nextTxGlitch, nextSpark, sparkAt = -10f;
            Image[] sparks; Vector2[] sparkPos, sparkVel;
            public bool Sparked { get; private set; }
            public bool Transmitting => transmit;
            public void SetTransmit(bool on)
            {
                if (on == transmit) return;
                transmit = on; float now = OzMotion.Now; txAt = now; nextTxGlitch = now + 0.25f; nextSpark = now + 0.12f; Sparked = false;
                if (!on) { if (bay) bay.anchoredPosition = bayHome; if (sparks != null) foreach (var s in sparks) if (s) s.enabled = false; }
            }

            void TransmitTick(float now)
            {
                bool calm = OzMotion.Reduced;
                Vector2 shake = calm ? Vector2.zero : new Vector2((Mathf.PerlinNoise(now * 9f, 0.3f) - 0.5f) * 7f, (Mathf.PerlinNoise(0.7f, now * 8f) - 0.5f) * 5f);
                bay.anchoredPosition = bayHome + shake + (glitching && !calm ? tear * 0.08f : Vector2.zero);
                SetNoise(Mathf.Max(noise.color.a, 0.24f + 0.16f * Mathf.PerlinNoise(now * 13f, 1.1f)));
                if (!glitching) Scan(Mathf.Repeat((now - txAt) / 0.9f, 1f), 0.7f);
                if (now >= nextTxGlitch) { nextTxGlitch = now + Random.Range(0.5f, 1.4f); Hit(Random.Range(0.45f, 0.9f)); }
                if (now >= nextSpark) { nextSpark = now + Random.Range(1.2f, 2.6f); Burst(now); Sparked = true; }
                AnimateSparks(now);
            }

            void Burst(float now)
            {
                if (sparks == null)
                {
                    sparks = new Image[7]; sparkPos = new Vector2[7]; sparkVel = new Vector2[7];
                    for (int i = 0; i < sparks.Length; i++)
                    {
                        sparks[i] = Hud.Kit(bay, "Spark" + i, "Bits/dot", new OzRect(0, 0, 4, 4), new Color(1f, 0.88f, 0.55f, 1f), Image.Type.Simple);
                        sparks[i].raycastTarget = false; sparks[i].enabled = false;
                    }
                }
                sparkAt = now;
                int side = Random.Range(0, 3);                                                                 // left edge, right edge, top edge
                var o = side == 0 ? new Vector2(2f, Random.Range(0.15f, 0.7f) * size) : side == 1 ? new Vector2(size - 4f, Random.Range(0.15f, 0.7f) * size) : new Vector2(Random.Range(0.2f, 0.8f) * size, 2f);
                var inward = side == 0 ? Vector2.right : side == 1 ? Vector2.left : Vector2.up;                 // screen coords: +y = down
                for (int i = 0; i < sparks.Length; i++)
                {
                    float s = Random.Range(2f, 5f);
                    sparks[i].rectTransform.sizeDelta = new Vector2(s, s);
                    sparkPos[i] = o + Random.insideUnitCircle * 3f;
                    var dir = (inward + new Vector2(Random.Range(-0.9f, 0.9f), Random.Range(-1.1f, 0.3f))).normalized;
                    sparkVel[i] = dir * Random.Range(70f, 190f);
                }
            }

            void AnimateSparks(float now)
            {
                if (sparks == null) return;
                float t = now - sparkAt, k = t / 0.34f;
                bool on = k >= 0f && k < 1f;
                for (int i = 0; i < sparks.Length; i++)
                {
                    var im = sparks[i]; if (!im) continue;
                    if (im.enabled != on) im.enabled = on;
                    if (!on) continue;
                    var p = sparkPos[i] + sparkVel[i] * t + new Vector2(0f, 260f * t * t);                        // they fall as they fly
                    im.rectTransform.anchoredPosition = new Vector2(p.x, -p.y);
                    var c = im.color; c.a = (1f - k) * (Random.value < 0.15f ? 0.4f : 1f); im.color = c;
                }
            }
            public void Reselect(bool same) { t0 = OzMotion.Now; sweepOnly = same; }

            /// <summary>ui_combat_v1: a short burst of static on the comms screen (0.22-0.38 s by the hit's weight). While one runs a
            /// stronger hit only raises it; the next burst waits HitGap after the last one ended, so steady fire flickers in bursts,
            /// never a constant noise. amp 0..1.</summary>
            public void Hit(float amp)
            {
                if (idle) return;
                float now = OzMotion.Now;
                if (now - glitchAt < glitchDur) { glitchAmp = Mathf.Max(glitchAmp, Mathf.Clamp01(amp)); return; }
                if (now - (glitchAt + glitchDur) < HitGap) return;
                glitchAt = now; glitchAmp = Mathf.Clamp01(amp); glitchDur = 0.22f + 0.16f * glitchAmp; nextTear = 0f;
            }

            /// <summary>face = robot type whose portrait shows (the driver for vehicles, null = none); unitType = fallback art
            /// (the vehicle's icon when it has no crew); bg = null (level backdrop) or "cockpit".</summary>
            public void Show(string face, string unitType, Team team, string bg, Texture crewArt = null)
            {
                idle = false;
                string b = bg ?? levelBg;
                if (b != shownBg) { shownBg = b; Hud.Dress(backdrop, "Portrait/portrait_bg_" + b, null, Image.Type.Simple); }
                string key = (face ?? "-") + "|" + unitType + "|" + team + (crewArt != null ? "|crew" : "");
                if (key == shownKey) return;
                shownKey = key;
                Texture tex = crewArt != null ? crewArt : face != null ? UnitArt.Portrait(face, team) : UnitArt.Icon(unitType, team);
                art.texture = tex; art.enabled = tex != null;
                var ar = art.rectTransform;
                if (face == null && crewArt == null) ar.Fill(size * 0.12f, size * 0.1f, size * 0.12f, size * 0.14f); else ar.Fill();   // empty vehicle: its icon, smaller
            }

            public void Tick()
            {
                float now = OzMotion.Now;
                if (noise.texture != null && noiseTex[0] != null) { int f = (int)(now / 0.06f) & 3; if (f != noiseFrame) { noiseFrame = f; noise.texture = noiseTex[f]; } }
                if (idle)
                {
                    if (glitching) EndGlitch();                                                                  // ui_combat_v1
                    nosignal.enabled = true; art.enabled = false; SetNoise(0.12f);
                    revealMask.padding = Vector4.zero;
                    float k = Mathf.Repeat(now - idleAt, 4f) / 1.4f;                                        // idle scanline every 4 s
                    Scan(k < 1f ? k : -1f, 0.35f);
                    return;
                }
                nosignal.enabled = false;
                if (art.texture) art.enabled = true;
                float stat = sweepOnly ? 0f : OzMotion.Dur(0.06f), sweep = OzMotion.Dur(sweepOnly ? 0.16f : 0.22f), e = now - t0;
                if (e < stat) { SetNoise(0.2f); revealMask.padding = new Vector4(0, size, 0, 0); Scan(-1f, 0f); }
                else
                {
                    float k = sweep <= 0f ? 1f : Mathf.Clamp01((e - stat) / sweep);
                    revealMask.padding = sweepOnly ? Vector4.zero : new Vector4(0, size * (1f - k), 0, 0);
                    SetNoise(sweepOnly ? 0f : 0.2f * (1f - k));
                    Scan(k < 1f ? k : -1f, 1f);
                }
                // idle life (Zod's blinks): a 2 % brightness flicker every 3-6 s
                if (now > nextFlicker) { nextFlicker = now + Random.Range(3f, 6f); flickerAt = now; }
                float c = now - flickerAt < 0.06f ? 0.98f : 1f;
                var want = new Color(c, c, c, 1f);
                float ge = now - glitchAt;
                if (ge >= 0f && ge < glitchDur)
                {   // ui_combat_v1 hit static: noise burst, the picture tears sideways and drops out, a colour-split tint, the scan band rolls
                    glitching = true;
                    float env = 1f - ge / glitchDur, a = glitchAmp * env * env;
                    bool calm = OzMotion.Reduced;                                                              // reduced motion: static only, no shake
                    SetNoise(Mathf.Max(noise.color.a, 0.18f + 0.6f * a));
                    if (now >= nextTear)
                    {
                        nextTear = now + Random.Range(0.028f, 0.055f);
                        tear = calm ? Vector2.zero : new Vector2(Random.Range(-1f, 1f) * size * 0.075f * a, Random.Range(-1f, 1f) * size * 0.012f * a);
                        float pick = Random.value;
                        tint = pick < 0.2f * glitchAmp ? new Color(1f, 1f, 1f, 0.3f)                             // signal drop-out
                             : pick < 0.5f ? new Color(1f, 0.66f, 0.58f, 1f) : pick < 0.72f ? new Color(0.64f, 0.93f, 1f, 1f) : Color.white;
                    }
                    reveal.anchoredPosition = tear;
                    bay.anchoredPosition = bayHome + (calm ? Vector2.zero : tear * 0.08f);
                    if (!calm) Scan(Mathf.Repeat(ge / 0.11f, 1f), 0.35f + 0.65f * a);
                    want = new Color(tint.r * c, tint.g * c, tint.b * c, Mathf.Lerp(1f, tint.a, env));
                }
                else if (glitching) EndGlitch();
                if (art.color != want) art.color = want;
                Sparked = false;
                if (transmit) TransmitTick(now);
            }

            void EndGlitch()
            {
                glitching = false; tear = Vector2.zero; tint = Color.white;
                if (reveal) reveal.anchoredPosition = Vector2.zero;
                if (bay) bay.anchoredPosition = bayHome;
            }

            void SetNoise(float a) { if (!noise.enabled) return; var c = noise.color; if (Mathf.Abs(c.a - a) > 0.003f) { c.a = a; noise.color = c; } }

            void Scan(float k, float alpha)
            {
                bool on = k >= 0f && scan.sprite != null;
                if (scan.enabled != on) scan.enabled = on;
                if (!on) return;
                scan.rectTransform.anchoredPosition = new Vector2(0, -(k * size - 12f));
                var c = scan.color; c.a = 0.55f * alpha; scan.color = c;
            }
        }

        // ================================================================== alert tab (Zod A button, zhud.cpp:563-572)
        RectTransform alertTab; OzText alertA, alertText; RawImage alertHazard; HudButton alertHot; Image alertChip;
        ZObject alertUnit; float alertLast = -100f, alertStart = -100f, alertPing = -100f; bool alertActive;

        void BuildAlertTab()
        {
            var pr = PanelRect(); var r = OzLayout.AlertTab;
            alertTab = Hud.Node("AlertTab", Root).Place(new OzRect(pr.a, pr.x + r.x, pr.y + r.y, r.w, r.h));
            if (OzKit.Available) Hud.Shadow(alertTab, "Panels/panel_sm_mask");
            Hud.Kit(alertTab, "Tab", "Panels/alert_tab", new OzRect(0, 0, r.w, r.h), null);
            var hz = OzLayout.AlertHazard;
            alertHazard = Hud.Tiled(alertTab, "Hazard", "Textures/tex_hazard_alert_fine", hz, Color.white);
            var ch = OzLayout.AlertChip;
            alertChip = Hud.Kit(alertTab, "Chip", "Panels/chip_4", ch, OzUi.GM950);
            alertA = Hud.Label(alertTab, "A", "A", 12, OzUi.Alert, 0f, OzStyle.Plain).At(ch.x + ch.w * 0.5f, ch.y + ch.h * 0.5f, OzH.C, true, ch.w);
            alertText = Hud.Label(alertTab, "Text", "UNDER ATTACK", OzLayout.AlertTextSize, OzUi.AlertText, 0.16f, OzStyle.Plain).At(OzLayout.AlertTextX, OzLayout.AlertTextMid, OzH.L, true, 180);
            alertHot = Hotspot(alertTab, "Hot", new OzRect(0, 0, r.w, r.h), "UNDER ATTACK|Click to select the attacked unit and fly there.|A");
            alertHot.OnClick = c => SelectAndFly(alertUnit);
            alertTab.gameObject.SetActive(false);
        }

        void UnderAttack(ZObject u)
        {
            float now = OzMotion.Now;
            if (!alertActive || now - alertLast > 2.5f) alertStart = now;                                  // the audio track plays ui_alert + Alan's line itself
            alertActive = true; alertLast = now; alertUnit = u;
            if (now - alertPing > 2f) { alertPing = now; Minimap?.PingAlert(u.CenterX, u.CenterY); if (Minimap && hudVisible) OzSfx.Ui("ui_minimap_ping"); }
        }

        void RefreshAlertTab()
        {
            float now = OzMotion.Now;
            bool live = alertActive && alertUnit != null && now - alertLast < 2.5f;
            if (!live && alertActive && now - alertLast > 2.5f + 0.4f) alertActive = false;             // slides down 400 ms after the alert ends
            float show = alertActive ? OzEase.OutCubic(OzMotion.T(alertStart, OzMotion.Dur(OzMotion.Short))) : 0f;
            if (alertActive && !live) show = 1f - OzEase.InCubic(OzMotion.T(alertLast + 2.5f, OzMotion.Dur(0.4f)));
            bool on = show > 0.001f && hudVisible && !exited && minK > 0.001f;
            alertTab.gameObject.SetActiveFast(on);
            if (!on) return;
            var pr = PanelRect(); var r = OzLayout.AlertTab;
            var home = new OzRect(pr.a, pr.x + r.x, pr.y + r.y + (1f - show) * r.h, r.w, r.h);
            alertTab.Place(home);
            float since = now - alertStart;
            alertA.color = since < 1.2f && !OzMotion.Reduced ? (OzMotion.Blink(OzMotion.BlinkFast) ? (Color)OzUi.Ink : (Color)OzUi.Alert) : (Color)OzUi.Alert;
            if (alertHazard.texture) { var uv = alertHazard.uvRect; uv.x = -(now * 12f / 32f) % 1f; alertHazard.uvRect = uv; }   // 12 u/s along the stripe
            alertText.Alpha = 0.78f + 0.22f * Mathf.Sin(now * Mathf.PI * 2f);                              // tab pulse 1 Hz while live
            if (alertUnit != null)
            {
                var z = W.ZoneOf(alertUnit);
                int zk = z == null ? -1 : z.Flag == null ? -2 : alertUnit.ZoneIndex;                        // the string is built only when the zone changes
                if (zk != alertZoneShown)
                {
                    alertZoneShown = zk;
                    alertText.Set(zk == -1 ? "UNDER ATTACK" : zk == -2 ? "UNDER ATTACK · FORT" : $"UNDER ATTACK · ZONE {zk + 1}");
                }
            }
        }
        int alertZoneShown = int.MinValue;

        // ================================================================== command bar: R / V / B / G + control groups (guide 9.4 + kit)
        RectTransform cmdBar;
        HudButton bR, bV, bB, bG; OzText cR, cV, cB, cG;
        readonly HudButton[] groupBtn = new HudButton[10]; readonly OzText[] groupCount = new OzText[10], groupDigit = new OzText[10];
        readonly Image[] groupKeyBadge = new Image[10], groupGlyph = new Image[10];
        readonly Dictionary<int, List<ZObject>> localGroups = new Dictionary<int, List<ZObject>>();
        int lastRecalled = -1;
        float nextBottom;

        void BuildBottomBar()
        {
            cmdBar = Hud.Node("CommandBar", Root).Place(OzLayout.CommandBar);
            AddBlocker(cmdBar);
            if (OzKit.GetSprite("Panels/cmdbar") != null)
            {
                Hud.Shadow(cmdBar, "Panels/panel_md_mask");
                Hud.Kit(cmdBar, "Face", "Panels/cmdbar", new OzRect(0, 0, OzLayout.CommandBar.w, OzLayout.CommandBar.h), null, Image.Type.Simple);
            }
            else Hud.Plate(cmdBar, "md");
            int i = 0;
            (HudButton, OzText) Key(string key, string tip, System.Action<HudClick> click)
            {
                var b = HudButton.Make(cmdBar, "Key" + key, key, new Vector2(OzLayout.KeyRW, OzLayout.KeyH), HudButtonKind.Key, OzLayout.KeyLetterSize, 0f);
                b.Rt.Place(new OzRect(OzLayout.KeyRX0 + OzLayout.KeyRStep * i++, OzLayout.KeyY, OzLayout.KeyRW, OzLayout.KeyH));
                var n = Hud.Label(b.Content, "Count", "", OzLayout.KeyCountSize, OzUi.Muted, 0f, OzStyle.Plain).At(OzLayout.KeyCountRX, OzLayout.KeyCountY, OzH.R, false, 30);
                b.Tooltip = tip; b.OnClick = click; return (b, n);
            }
            (bR, cR) = Key(OzBindings.Cap(OzAction.NextRobot), OzBindings.Cap(OzAction.NextRobot) + " · NEXT ROBOT|Selects your robot squads one after the other (nearest first).|CTRL+" + OzBindings.Cap(OzAction.AllRobots) + " ALL", c => CycleOrAll('r', ZKind.Robot, c.ctrl));   // menu_exe_v1: bound keys
            (bV, cV) = Key(OzBindings.Cap(OzAction.NextVehicle), OzBindings.Cap(OzAction.NextVehicle) + " · NEXT VEHICLE|Selects your vehicles one after the other.|CTRL+" + OzBindings.Cap(OzAction.AllVehicles) + " ALL", c => CycleOrAll('v', ZKind.Vehicle, c.ctrl));
            (bB, cB) = Key(OzBindings.Cap(OzAction.FactoryList), OzBindings.Cap(OzAction.FactoryList) + " · FACTORY LIST|Your fort and factories: what they build and when it is ready.|" + OzBindings.Cap(OzAction.FactoryList), c => factoryListOpen = !factoryListOpen);
            (bG, cG) = Key(OzBindings.Cap(OzAction.NextGun), OzBindings.Cap(OzAction.NextGun) + " · NEXT GUN|Selects your cannons one after the other.|CTRL+" + OzBindings.Cap(OzAction.AllCannons) + " ALL", c => CycleOrAll('g', ZKind.Cannon, c.ctrl));
            for (int k = 0; k < 10; k++)
            {
                int d = (k + 1) % 10;                                                                    // 1..9 then 0 like the keyboard row
                var b = HudButton.Make(cmdBar, "Group" + d, null, new Vector2(OzLayout.KeyGW, OzLayout.KeyH), HudButtonKind.Key, 12);
                b.Rt.Place(new OzRect(OzLayout.KeyGX0 + OzLayout.KeyGStep * k, OzLayout.KeyY, OzLayout.KeyGW, OzLayout.KeyH));
                groupKeyBadge[d] = Hud.Kit(b.Content, "Badge", "Badges/badge_" + d + "_sm", new OzRect(OzLayout.GroupBadgeX, OzLayout.GroupBadgeY, 14, 14), null, Image.Type.Simple);
                groupGlyph[d] = Hud.Icon(b.Content, "Glyph", "robot", OzLayout.GroupGlyphX, OzLayout.GroupGlyphY, OzLayout.GroupGlyphSize, OzUi.Ink2);
                groupCount[d] = Hud.Label(b.Content, "Count", "", OzLayout.GroupCountSize, OzUi.Ink).At(OzLayout.GroupCountRX, OzLayout.GroupCountY, OzH.R, false, 30);
                groupDigit[d] = Hud.Label(b.Content, "Digit", d.ToString(), OzLayout.GroupEmptySize, OzUi.Dim, 0f, OzStyle.Plain).At(OzLayout.GroupEmptyX, OzLayout.GroupEmptyY);
                b.Tooltip = $"GROUP {d}|Click: select the group (again: fly there). Ctrl+click: store the selection.|{d} · CTRL+{d}";
                b.OnClick = c => GroupClick(d, c.ctrl);
                groupBtn[d] = b;
            }
        }

        Dictionary<int, List<ZObject>> Groups => Bridge?.Groups ?? localGroups;

        void GroupClick(int d, bool store)
        {
            var groups = Groups; var sel = input.Selection;
            if (store) { OzSfx.Ui(sel.Count > 0 ? "ui_group_store" : "ui_deselect"); groups[d] = new List<ZObject>(sel); Toast(sel.Count > 0 ? $"GROUP {d} STORED · {sel.Count}" : $"GROUP {d} CLEARED", "chevron", false, null, true); return; }
            if (!groups.TryGetValue(d, out var g)) { OzSfx.Ui("ui_select_empty"); return; }                    // sound_v3
            g.RemoveAll(o => o.Destroyed || o.Owner != Player);                                         // PlayerInput.cs:97-101 (zplayer.cpp:2705-2724)
            lastRecalled = d; AudioDirector.MouseRecallAt = Time.unscaledTime; if (g.Count == 0) OzSfx.Ui("ui_select_empty");   // sound_v3
            if (g.Count > 0 && g.SequenceEqual(sel)) { if (rts) { var s = Vector3.zero; foreach (var o in g) s += game.CenterWorld(o); rts.FlyTo(s / g.Count); } }
            else { sel.Clear(); sel.AddRange(g); Bridge?.MarkSelected(); }
        }

        void CycleOrAll(char kind, ZKind k, bool all)
        {
            var mid = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            if (all)
            {
                if (Bridge == null || !Bridge.SelectAll(o => o.Kind == k)) { input.Selection.Clear(); input.Selection.AddRange(W.OwnedSelectable(Player).Where(o => o.Kind == k)); Bridge?.MarkSelected(); }
                return;
            }
            if (Bridge != null && Bridge.Cycle(kind, k, mid)) return;
            var list = W.OwnedSelectable(Player).Where(o => o.Kind == k).OrderBy(o => o.Id).ToList();       // fallback: next by id
            if (list.Count == 0) return;
            var cur = input.Selection.Count == 1 ? input.Selection[0] : null;
            var pick = cur != null && cur.Kind == k ? list.FirstOrDefault(o => o.Id > cur.Id) ?? list[0] : list[0];
            input.Selection.Clear(); input.Selection.Add(pick);
            if (rts) rts.FlyTo(game.CenterWorld(pick));
        }

        void RefreshBottomBar()
        {
            if (OzMotion.Now < nextBottom) return; nextBottom = OzMotion.Now + 0.2f;
            int nR = 0, nV = 0, nG = 0, nB = 0;
            foreach (var o in W.Objects)
            {
                if (o.Owner != Player || o.Destroyed || o.IsFortDefender) continue;                   // fort_defender_v1 DV41
                if (o.Kind == ZKind.Robot) nR++; else if (o.Kind == ZKind.Vehicle) nV++; else if (o.Kind == ZKind.Cannon) nG++;
                else if (o.IsProducer) nB++;
            }
            SetInteract(bR, nR > 0); SetInteract(bV, nV > 0); SetInteract(bG, nG > 0); SetInteract(bB, nB > 0);   // greyed like Zod (zplayer.cpp:1457-1568)
            if (bB.Selected != factoryListOpen) { bB.Selected = factoryListOpen; bB.Refresh(); }
            KeyCount(cR, bR, nR, 0); KeyCount(cV, bV, nV, 1); KeyCount(cB, bB, nB, 2); KeyCount(cG, bG, nG, 3);
            var groups = Groups; var sel = input.Selection;
            selAlive.Clear(); foreach (var o in sel) if (o != null && !o.Destroyed && o.Owner == Player) selAlive.Add(o);
            for (int d = 0; d < 10; d++)
            {
                // alive members only: PlayerInput prunes the selection every frame but a group only when it is recalled, so a
                // count / order comparison of the raw lists put the amber lamp out as soon as one member died
                int n = 0, best = 0; bool all = true; string topType = null;
                if (groups.TryGetValue(d, out var g) && g != null)
                {
                    typeCount.Clear();
                    foreach (var o in g)
                    {
                        if (o == null || o.Destroyed || o.Owner != Player) continue;
                        n++; if (!selAlive.Contains(o)) all = false;
                        int c = typeCount.TryGetValue(o.Type, out var k) ? k + 1 : 1; typeCount[o.Type] = c; if (c > best) best = c;
                    }
                    foreach (var o in g) if (o != null && !o.Destroyed && o.Owner == Player && typeCount[o.Type] == best) { topType = o.Type; break; }   // most common type, first seen wins
                }
                var b = groupBtn[d];
                bool empty = n == 0;
                bool selected = !empty && all && n == selAlive.Count;                                    // the recalled group = its selection: amber lamp on
                if (b.Empty != empty || b.Selected != selected) { b.Empty = empty; b.Selected = selected; b.Refresh(); }
                groupDigit[d].SetActive(empty); groupKeyBadge[d].enabled = !empty; groupGlyph[d].enabled = !empty;
                if (groupShown[d] != n) { groupShown[d] = n; groupCount[d].Set(empty ? "" : OzText.Tab(n.ToString())); }
                if (!empty)
                {
                    Hud.SetIcon(groupGlyph[d], UnitInfo.ClassGlyph(topType), OzLayout.GroupGlyphSize);
                    var tint = selected ? (Color)OzUi.AmberHi : (Color)OzUi.Ink2; if (groupGlyph[d].color != tint) groupGlyph[d].color = tint;
                    groupCount[d].color = selected ? (Color)OzUi.AmberHi : (Color)OzUi.Ink;
                }
            }
        }
        readonly HashSet<ZObject> selAlive = new HashSet<ZObject>();
        readonly Dictionary<string, int> typeCount = new Dictionary<string, int>();
        readonly int[] groupShown = { -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 }, keyShown = { -1, -1, -1, -1 };

        void KeyCount(OzText t, HudButton b, int n, int slot)
        {
            if (keyShown[slot] != n) { keyShown[slot] = n; t.Set(n > 0 ? OzText.Tab(n.ToString()) : ""); }
            t.color = b.Selected ? (Color)OzUi.Amber : (Color)OzUi.Muted;
        }

        static void SetInteract(HudButton b, bool on) { if (b.Interactable != on) { b.Interactable = on; b.Refresh(); } }

        // ================================================================== factory list (Zod B list, gwfactory_list.cpp; guide 9.7)
        RectTransform factoryPanel, factoryContent;
        sealed class FactoryRow { public ZObject b; public HudButton btn; public OzText name, unit, time; public HudBar prog, hp; public UnitIcon icon; public Image[] pips; public RawImage paused; }
        readonly List<FactoryRow> factoryRows = new List<FactoryRow>();
        readonly List<ZObject> factoryList = new List<ZObject>(); readonly List<int> factoryIds = new List<int>(); bool factoryBuilt;
        static readonly System.Comparison<ZObject> FactoryOrder = (a, b) =>
        {
            int ka = a.IsFort ? 0 : a.Type == "robot_factory" ? 1 : 2, kb = b.IsFort ? 0 : b.Type == "robot_factory" ? 1 : 2;
            return ka != kb ? ka.CompareTo(kb) : a.Id.CompareTo(b.Id);
        };
        float nextFactory, factoryOpenAt = -10f; bool factoryShown; CanvasGroup factoryCg;

        void BuildFactoryList()
        {
            factoryPanel = Hud.Node("FactoryList", Root);
            PlaceFactoryPanel(1);
            AddBlocker(factoryPanel);
            Hud.Plate(factoryPanel, "md", OzLayout.FactoryHead, OzUi.Amber);
            Hud.Label(factoryPanel, "Title", "FACTORIES", 12, OzUi.Ink, 0.16f).At(16, 10);
            Hud.Keycap(factoryPanel, "KeyB", OzBindings.Cap(OzAction.FactoryList), OzLayout.FactoryW - 14, 9);   // menu_exe_v1: bound key
            factoryContent = Hud.Node("Rows", factoryPanel); factoryContent.Fill();
            factoryPanel.gameObject.AddComponent<CanvasGroup>();
            factoryPanel.gameObject.SetActive(false);
        }

        /// <summary>Top edge of the open factory list in canvas units from the bottom (0 = closed); the production window keeps clear of it.</summary>
        public float FactoryListTop => factoryShown && factoryPanel ? OzLayout.RefH - OzLayout.FactoryBottom + factoryPanel.sizeDelta.y : 0f;

        void PlaceFactoryPanel(int rows)
        {
            float h = 40 + OzLayout.FactoryRowStep * Mathf.Max(1, rows);
            factoryPanel.Place(new OzRect(OzAnchor.BL, OzLayout.FactoryX, OzLayout.FactoryBottom - h, OzLayout.FactoryW, h));
        }

        void RefreshFactoryList()
        {
            bool want = factoryListOpen && hudVisible && !exited;
            if (want && !factoryShown) { factoryOpenAt = OzMotion.Now; OzSfx.Ui("ui_panel_open", 0.7f); }
            else if (!want && factoryShown && !factoryListOpen && hudVisible) OzSfx.Ui("ui_panel_close", 0.7f);   // audio_v2: the list closes with a cue
            factoryShown = want;
            factoryPanel.gameObject.SetActiveFast(want);
            if (!want) return;
            // window open (guide 7.3): scale 0.96 -> 1 from the anchor side + alpha, 160 ms
            float k = OzEase.OutCubic(OzMotion.T(factoryOpenAt, OzMotion.Dur(OzMotion.Short)));
            factoryPanel.localScale = Vector3.one * Mathf.Lerp(0.96f, 1f, k);
            if (!factoryCg) factoryCg = factoryPanel.GetComponent<CanvasGroup>();
            factoryCg.alpha = k;
            if (OzMotion.Now < nextFactory) return; nextFactory = OzMotion.Now + 0.2f;
            // Zod order: forts, then robot factories, then vehicle factories (gwfactory_list.cpp:333-367); reused lists, no LINQ garbage
            var list = factoryList; list.Clear();
            foreach (var o in W.Objects) if (o.IsProducer && o.Owner == Player) list.Add(o);
            list.Sort(FactoryOrder);
            bool same = factoryBuilt && factoryIds.Count == list.Count;
            for (int i = 0; same && i < list.Count; i++) same = factoryIds[i] == list[i].Id;
            if (!same)
            {
                factoryBuilt = true; factoryIds.Clear(); foreach (var o in list) factoryIds.Add(o.Id);
                foreach (var r in factoryRows) r.btn.Kill();
                factoryRows.Clear();
                // rows that fit under the top limit of the CURRENT canvas (UI scale 125 % makes it 864 u tall)
                int maxRows = Mathf.Max(1, Mathf.FloorToInt((CanvasSize.y - (OzLayout.RefH - OzLayout.FactoryBottom) - OzLayout.FactoryMinY - 40) / OzLayout.FactoryRowStep));
                for (int i = 0; i < list.Count && i < maxRows; i++) factoryRows.Add(MakeFactoryRow(list[i], i));
                PlaceFactoryPanel(factoryRows.Count);
                factoryPanel.pivot = new Vector2(0, 0);                                                 // grows from its bottom-left anchor side
                factoryPanel.anchoredPosition = new Vector2(OzLayout.FactoryX, OzLayout.RefH - OzLayout.FactoryBottom);
                if (list.Count == 0) factoryRows.Add(MakeFactoryRow(null, 0));
            }
            foreach (var r in factoryRows) RefreshFactoryRow(r);
        }

        FactoryRow MakeFactoryRow(ZObject b, int i)
        {
            var rr = OzLayout.FactoryRow;
            var row = new FactoryRow { b = b };
            row.btn = HudButton.Make(factoryContent, "Row" + (b != null ? b.Id : -1), null, rr.Size, HudButtonKind.Small);
            row.btn.Rt.Place(rr.Move(0, OzLayout.FactoryRowStep * i));
            var c = row.btn.Content;
            row.icon = new UnitIcon(c, "Icon", OzLayout.FactoryRowWell.Size, true, 2f); row.icon.Rt.Place(OzLayout.FactoryRowWell);
            row.name = Hud.Label(c, "Name", "", 11, OzUi.Ink, 0.1f).At(OzLayout.FactoryRowTextX, OzLayout.FactoryRowNameY, OzH.L, false, 180);
            row.unit = Hud.Label(c, "Unit", "", 10, OzUi.Ink2, 0.1f, OzStyle.Plain).At(OzLayout.FactoryRowTextX, OzLayout.FactoryRowUnitY, OzH.L, false, 190);
            row.time = Hud.Label(c, "Time", "", 12, OzUi.Amber).At(OzLayout.FactoryRowTimeRX, OzLayout.FactoryRowTimeY, OzH.R, false, 60);
            row.pips = new Image[6];
            for (int p = 0; p < 6; p++) row.pips[p] = Hud.KitNative(c, "Pip" + p, "Bits/pip", OzLayout.FactoryPipsX + p * 9, OzLayout.FactoryPipsY, OzUi.GM500);
            row.prog = new HudBar(c, "Prog", "amber", false, false, null, false); row.prog.Root.Place(new OzRect(OzLayout.FactoryRowTextX, OzLayout.FactoryProgY, OzLayout.FactoryBarsW, 5));
            row.paused = Hud.Tiled(c, "Paused", "Textures/tex_hazard_signal_fine", new OzRect(OzLayout.FactoryRowTextX + 1, OzLayout.FactoryProgY + 1, OzLayout.FactoryBarsW - 2, 3), Color.white);
            row.paused.gameObject.SetActive(false);
            row.hp = new HudBar(c, "Hp", "health", false, false, null, false); row.hp.Root.Place(new OzRect(OzLayout.FactoryRowTextX, OzLayout.FactoryHpY, OzLayout.FactoryBarsW, 3));
            if (b != null)
            {
                var target = b;
                row.btn.OnClick = cl => { if (rts) rts.FlyTo(game.CenterWorld(target)); Production.Open(target); };   // gwfactory list click: fly + open
                row.btn.Tooltip = $"{UnitInfo.Name(b.Type)}|Fly there and open its production window.|";
            }
            else { row.btn.Interactable = false; row.btn.Refresh(); row.name.Set("NO FACTORIES"); row.unit.Set("CAPTURE A FLAG TO WIN ONE"); }
            return row;
        }

        void RefreshFactoryRow(FactoryRow r)
        {
            var b = r.b; if (b == null) return;
            r.name.Set(UnitInfo.Name(b.Type) + (b.StoredCannons.Count > 0 ? $"  <color={OzUi.Hex(OzUi.Amber)}>×{b.StoredCannons.Count} GUN</color>" : ""));
            int tl = ZBuildLists.DisplayTech(b.Type, b.BuildLevel);
            for (int p = 0; p < 6; p++) { var c = p < tl ? (Color)OzUi.Amber : (Color)OzUi.GM500; if (r.pips[p].color != c) r.pips[p].color = c; }
            bool paused = W.UnitLimitReached(b.Owner) && b.Building;
            string state = b.Destroyed ? "DESTROYED" : b.StoredCannons.Count > 0 ? "PLACING CANNON" : !b.Building || b.CurUnit == null ? "NO PRODUCTION"
                         : paused ? "PAUSED · UNIT LIMIT" : UnitInfo.Name(b.CurUnit);
            r.unit.Set(state + (b.RallyPoints.Count > 0 && !paused ? " · RALLY" : ""));
            r.unit.color = b.Destroyed || paused ? (Color)OzUi.AlertText : b.StoredCannons.Count > 0 ? (Color)OzUi.Amber : (Color)OzUi.Ink2;
            r.time.Set(b.Building && b.CurUnit != null && !paused && !b.Destroyed ? OzText.Tab(UnitInfo.Mmss(b.ProductionTimeLeft)) : "");
            r.prog.Set((float)b.ProductionProgress);
            r.paused.gameObject.SetActiveFast(paused && r.paused.texture);
            r.hp.Set(b.MaxHealth > 0 ? b.Health / (float)b.MaxHealth : 0);
            r.icon.Set(b.CurUnit, b.Owner);
            bool open = Production && Production.Building == b;
            if (r.btn.Selected != open) { r.btn.Selected = open; r.btn.Refresh(); }
        }

        // ================================================================== stored cannons (Zod gun icon top-left, guide 9.8)
        sealed class CannonPlate { public HudButton btn; public UnitIcon icon; public OzText count, sub; public ZObject b; }
        readonly List<CannonPlate> cannonPlates = new List<CannonPlate>();
        float nextCannons;

        void BuildStoredCannons()
        {
            var r = OzLayout.StoredCannon;
            for (int j = 0; j < (int)OzLayout.StoredCannonMax; j++)
            {
                var p = new CannonPlate();
                p.btn = HudButton.Make(Root, "StoredCannon" + j, null, r.Size, HudButtonKind.Small, 11, 0.16f, HudButtonStyle.Normal, true);
                p.btn.Rt.Place(r.Move(0, OzLayout.StoredCannonStep * j));
                AddBlocker(p.btn.Rt);
                var c = p.btn.Content;
                p.icon = new UnitIcon(c, "Icon", new Vector2(52, 32), true, 1f); p.icon.Rt.Place(new OzRect(6, 6, 52, 32));
                p.count = Hud.Label(c, "Count", "", 11, OzUi.Amber).At(55, 25, OzH.R, false, 30);
                Hud.Label(c, "Title", "CANNON READY", 11, OzUi.Amber, 0.16f).At(68, 10, OzH.L, false, 160);
                p.sub = Hud.Label(c, "Sub", "", 9, OzUi.Ink2, 0.12f, OzStyle.Plain).At(68, 26, OzH.L, false, 160);
                var plate = p;
                p.btn.OnClick = cl => { if (plate.b != null) { if (rts) rts.FlyTo(game.CenterWorld(plate.b)); Production.Open(plate.b); } };
                p.btn.Tooltip = "CANNON READY|Open the building's window and press PLACE, then click a free spot in its zone.|" + OzBindings.Cap(OzAction.PlaceCannon);   // menu_exe_v1: bound key
                p.btn.Rt.gameObject.SetActive(false);
                cannonPlates.Add(p);
            }
        }

        void RefreshStoredCannons()
        {
            if (OzMotion.Now < nextCannons) return; nextCannons = OzMotion.Now + 0.25f;
            int j = 0;
            if (hudVisible && !exited)
                foreach (var o in W.Objects)
                {
                    if (j >= cannonPlates.Count) break;
                    if (o.Kind != ZKind.Building || o.Owner != Player || o.StoredCannons.Count == 0 || o.Destroyed) continue;
                    var p = cannonPlates[j++];
                    p.b = o; p.btn.Rt.gameObject.SetActiveFast(true);
                    p.icon.Set(o.StoredCannons[0], o.Owner);
                    p.count.Set(o.StoredCannons.Count > 1 ? OzText.Tab("×" + o.StoredCannons.Count) : "");
                    p.sub.Set($"{UnitInfo.Name(o.Type)} · CLICK TO PLACE");
                }
            for (; j < cannonPlates.Count; j++) { cannonPlates[j].b = null; cannonPlates[j].btn.Rt.gameObject.SetActiveFast(false); }
        }

        // ================================================================== toasts = Zod computer messages (guide 9.8, 7.3)
        sealed class ToastView
        {
            public RectTransform rt; public CanvasGroup cg; public Image bg, icon, bar; public OzText text; public HudButton hot; public RectTransform key;
            public float t0 = -100f, slot, slotFrom, slotAt; public bool alert, used; public System.Action action; public float hold;
            public string baseText; public int count;
        }
        readonly List<ToastView> toasts = new List<ToastView>();
        ToastView fortToast; float fortLast = -100f, fortCheck, fortDangerSince = -100f; bool fortDanger; ZObject dangerFort; int fortSeen;
        System.Action lastAction;

        void BuildToasts()
        {
            for (int i = 0; i < (int)OzLayout.ToastMax + 1; i++) toasts.Add(MakeToast("Toast" + i, OzLayout.Toast, false));
            fortToast = MakeToast("FortAlert", OzLayout.FortAlert, true);
        }

        ToastView MakeToast(string name, OzRect r, bool fort)
        {
            var t = new ToastView { alert = fort };
            t.rt = Hud.Node(name, Root).Place(r);
            t.cg = t.rt.gameObject.AddComponent<CanvasGroup>(); t.cg.blocksRaycasts = false;
            if (OzKit.Available) Hud.Shadow(t.rt, "Panels/panel_sm_mask");
            t.bg = Hud.Kit(t.rt, "Bg", fort ? "Panels/toast_fort_alert" : "Panels/toast", new OzRect(0, 0, r.w, r.h), null);
            if (!fort)
            {
                t.bar = Hud.Img(t.rt, "Bar", OzUi.AmberHi); t.bar.rectTransform.Place(new OzRect(8, 7, 3, r.h - 14)); t.bar.enabled = false;
                t.icon = Hud.Icon(t.rt, "Icon", "robot", OzLayout.ToastIconX, OzLayout.ToastIconY, OzLayout.ToastIconSize, OzUi.Amber);
                t.text = Hud.Label(t.rt, "Text", "", OzLayout.ToastTextSize, OzUi.Ink, 0.1f).At(OzLayout.ToastTextX, r.h * 0.5f, OzH.L, true, 260);
                t.key = Hud.Keycap(t.rt, "Space", OzBindings.Cap(OzAction.LastMessage), r.w - 12, 12);   // menu_exe_v1: bound key
            }
            else t.text = Hud.Label(t.rt, "Text", "YOUR FORT IS UNDER ATTACK", 16, OzUi.Ink, 0.14f).At(r.w * 0.5f, r.h * 0.5f, OzH.C, true, r.w);
            t.hot = Hotspot(t.rt, "Hot", new OzRect(0, 0, r.w, r.h), null);
            var tv = t; t.hot.OnClick = c => tv.action?.Invoke();
            AddBlocker(t.rt);
            t.rt.gameObject.SetActive(false);
            return t;
        }

        /// <summary>Staged-HUD API: a short feedback message (production, rally, groups). The colour picks the variant:
        /// Danger = alert toast, anything else amber.</summary>
        public void Toast(string text, Color c, float seconds = 2.2f) => Toast(text, c == HudStyle.Danger ? "alert" : c == HudStyle.Muted ? "chevron" : "star", c == HudStyle.Danger, null, true, seconds);

        /// <summary>Computer message (Zod: blinks 0.3 s, then holds 5 s). action = click / SPACE (select + fly, open window).</summary>
        // (x2, x3), a non-alert message shows at most once per ToastCalm seconds, and new toasts come at most one per ToastGap,
        // the rest wait in a short queue without duplicates.
        const float ToastCalm = 6f, ToastGap = 0.7f;
        readonly Dictionary<string, float> toastLast = new Dictionary<string, float>();
        readonly List<(string text, string glyph, bool alert, System.Action action, bool feedback, float hold)> toastQueue = new List<(string, string, bool, System.Action, bool, float)>();
        float toastNextAt;

        public void Toast(string text, string glyph, bool alert, System.Action action, bool feedback = false, float hold = 5f)
        {
            if (!hudVisible || exited) return;
            float now = OzMotion.Now;
            foreach (var x in toasts)
                if (x.used && x.baseText == text && now - x.t0 < x.hold)
                {   // merge into the one on screen
                    x.count++; x.text.Set(text + "  ×" + x.count); OzSoundLog.Add("hud", "toast", text, "merged x" + x.count); if (action != null) { x.action = action; lastAction = action; NoteEvent(action, text); } if (alert && feedback) OzSfx.Ui("ui_error"); return;   // sound_v3
                }
            if (!alert && toastLast.TryGetValue(text, out var last) && now - last < ToastCalm) return;
            if (now < toastNextAt)
            {
                if (toastQueue.Count < 4 && !toastQueue.Exists(q => q.text == text)) toastQueue.Add((text, glyph, alert, action, feedback, hold));
                return;
            }
            ShowToast(text, glyph, alert, action, feedback, hold);
        }

        void ShowToast(string text, string glyph, bool alert, System.Action action, bool feedback, float hold)
        {
            float now0 = OzMotion.Now; toastLast[text] = now0; toastNextAt = now0 + ToastGap;
            OzSoundLog.Add("hud", "toast", text, alert ? "shown (alert)" : "shown");
            if (!feedback) OzSfx.Ui("ui_toast"); else if (alert) OzSfx.Ui("ui_error");
            ToastView t = null;
            foreach (var x in toasts) if (!x.used) { t = x; break; }
            if (t == null) { t = toasts.OrderBy(x => x.t0).First(); }                                    // max 3 (+1 leaving): recycle the oldest
            foreach (var x in toasts) if (x.used && x != t) { x.slotFrom = CurrentSlot(x); x.slot += 1; x.slotAt = OzMotion.Now; }   // older ones move down
            t.used = true; t.t0 = OzMotion.Now; t.slot = 0; t.slotFrom = 0; t.slotAt = -10f; t.alert = alert; t.action = action; t.hold = feedback ? Mathf.Min(hold, 2.6f) : hold;
            t.baseText = text; t.count = 1;
            if (action != null) { lastAction = action; NoteEvent(action, text); }
            Hud.Dress(t.bg, alert ? "Panels/toast_alert" : "Panels/toast", null);
            t.text.Set(text);
            Hud.SetIcon(t.icon, glyph, OzLayout.ToastIconSize); t.icon.color = alert ? OzUi.Alert : OzUi.Amber;
            if (t.bar) t.bar.color = alert ? (Color)OzUi.Alert : (Color)OzUi.AmberHi;
            if (t.key) t.key.gameObject.SetActive(action != null);
            t.hot.Interactable = action != null; t.hot.Tooltip = action != null ? text + "|Click or press " + OzBindings.Cap(OzAction.LastMessage) + " to go there.|" + OzBindings.Cap(OzAction.LastMessage) : null;
            t.cg.alpha = 0f; t.rt.gameObject.SetActive(minK > 0.001f); t.rt.SetAsLastSibling();
        }

        float CurrentSlot(ToastView t) => Mathf.Lerp(t.slotFrom, t.slot, OzEase.OutCubic(OzMotion.T(t.slotAt, OzMotion.Dur(OzMotion.Short))));

        // the fort alarm ...), newest first: each press within SpaceChain s flies to the next older one, a later press starts again at
        // the newest. The same message again (merged x2 / repeats) moves to the front instead of filling the list.
        public const int SpaceEvents = 5; public const float SpaceChain = 6f;
        readonly List<(System.Action act, string text, float at)> recentEvents = new List<(System.Action, string, float)>();
        int spaceIdx = -1; float spaceAt = -100f;
        public int RecentEventCount => recentEvents.Count;
        void NoteEvent(System.Action act, string text)
        {
            if (act == null) return;
            recentEvents.RemoveAll(x => x.text == text);
            recentEvents.Insert(0, (act, text, OzMotion.Now));
            if (recentEvents.Count > SpaceEvents) recentEvents.RemoveRange(SpaceEvents, recentEvents.Count - SpaceEvents);
            spaceIdx = -1;
        }

        void SpaceEvent()
        {
            if (recentEvents.Count == 0) { lastAction?.Invoke(); return; }
            float now = OzMotion.Now;
            spaceIdx = spaceIdx >= 0 && now - spaceAt < SpaceChain ? (spaceIdx + 1) % recentEvents.Count : 0;
            spaceAt = now;
            var e = recentEvents[spaceIdx];
            e.act();                                                                                     // the fly-to plays ui_camera_jump (AudioDirector.CameraJump)
            if (recentEvents.Count > 1) Toast($"EVENT {spaceIdx + 1} / {recentEvents.Count} · {e.text}", "chevron", false, null, true, 1.6f);
            OzSoundLog.Add("hud", "space", e.text, $"event {spaceIdx + 1} of {recentEvents.Count}");
            Debug.Log($"[OpenZ HUD] SPACE: event {spaceIdx + 1} / {recentEvents.Count}: {e.text}");
        }

        void RefreshToasts()
        {
            float now = OzMotion.Now;
            if (toastQueue.Count > 0 && now >= toastNextAt && hudVisible && !exited)
            {
                var q = toastQueue[0]; toastQueue.RemoveAt(0);
                if (q.alert || !toastLast.TryGetValue(q.text, out var lq) || now - lq >= ToastCalm) ShowToast(q.text, q.glyph, q.alert, q.action, q.feedback, q.hold);
            }
            float offset = fortToast.used ? (OzLayout.FortAlert.h + 8f) : 0f;
            foreach (var t in toasts)
            {
                if (!t.used) continue;
                float age = now - t.t0, life = t.hold + 0.4f;
                if (age > life || t.slot >= OzLayout.ToastMax || !hudVisible) { t.used = false; t.rt.gameObject.SetActive(false); continue; }
                float kIn = OzEase.OutCubic(OzMotion.T(t.t0, OzMotion.Dur(OzMotion.Short)));
                float kOut = age > t.hold ? OzMotion.T(t.t0 + t.hold, OzMotion.Dur(0.4f, true)) : 0f;
                t.cg.alpha = kIn * (1f - kOut) * minK;
                t.rt.gameObject.SetActiveFast(minK > 0.001f);
                float y = OzLayout.Toast.y + offset + CurrentSlot(t) * OzLayout.ToastStep - 12f * (1f - kIn);
                t.rt.Place(new OzRect(OzAnchor.TC, OzLayout.Toast.x, y, OzLayout.Toast.w, OzLayout.Toast.h));
                bool blink = age < 1.8f && !OzMotion.Reduced;                                             // bar + icon blink 300 ms x 6
                bool on = !blink || OzMotion.Blink(OzMotion.BlinkSlow);
                if (t.icon.enabled != on) t.icon.enabled = on;
                if (t.bar && t.bar.enabled != (blink && on)) t.bar.enabled = blink && on;
            }
            // fort alarm (guide 9.8): hazard-ended plate at the toast slot, blinking, repeats every 10 s while the danger lasts
            if (fortToast.used)
            {
                float age = now - fortToast.t0;
                if (age > 5.4f || !hudVisible || exited) { fortToast.used = false; fortToast.rt.gameObject.SetActive(false); }
                else
                {
                    float kIn = OzEase.OutCubic(OzMotion.T(fortToast.t0, OzMotion.Dur(OzMotion.Short)));
                    float kOut = age > 5f ? OzMotion.T(fortToast.t0 + 5f, OzMotion.Dur(0.4f, true)) : 0f;
                    fortToast.cg.alpha = kIn * (1f - kOut) * minK;
                    fortToast.rt.gameObject.SetActiveFast(minK > 0.001f);
                    fortToast.rt.Place(OzLayout.FortAlert.Move(0, -12f * (1f - kIn)));
                    bool blink = age < 1.8f && !OzMotion.Reduced;
                    fortToast.text.color = blink && !OzMotion.Blink(OzMotion.BlinkSlow) ? (Color)OzUi.AlertText : (Color)OzUi.Ink;
                }
            }
        }

        /// <summary>Zod music danger level FORT (zplayer.cpp:1086-1096): an enemy robot / vehicle / cannon within 250 px of one of
        /// our forts; the alarm repeats every 10 s while it lasts.</summary>
        void CheckFortDanger()
        {
            float now = OzMotion.Now;
            if (now < fortCheck) return; fortCheck = now + 0.5f;
            bool danger = false;
            foreach (var f in W.Objects)
            {
                if (!f.IsFort || f.Owner != Player || f.Destroyed) continue;
                FortAlert.NoteFortHealth(f.Id, f.Health);                                                // NB31: warnings only after real hits
                FortAlert.NoteFortHit(f.Id, f.LastEnemyHit, W.Time);                                     // combat_nb_v1 NB31: the sim's own record of enemy hits
                if (W.Time - f.LastEnemyHit < FortAlert.HitWindow) { danger = true; dangerFort = f; }      // shelled from beyond 250 px counts too
                foreach (var o in W.Objects)
                {
                    if (!o.IsUnit || o.Owner == Player || o.Owner == Team.Neutral || o.Destroyed) continue;
                    long dx = o.CenterX - f.CenterX, dy = o.CenterY - f.CenterY;
                    if (dx * dx + dy * dy <= 250L * 250L) { danger = true; dangerFort = f; break; }
                }
                if (danger) break;
            }
            int fortFired = FortAlert.Report(danger);
            if (danger && fortFired != fortSeen && hudVisible && !exited)
            {
                fortSeen = fortFired;
                fortLast = now; fortToast.used = true; fortToast.t0 = now; fortToast.cg.alpha = 0f; fortToast.rt.gameObject.SetActive(minK > 0.001f); fortToast.rt.SetAsLastSibling();
                var f = dangerFort; fortToast.action = () => { if (rts && f != null) rts.FlyTo(game.CenterWorld(f)); };
                fortToast.hot.Tooltip = "YOUR FORT IS UNDER ATTACK|Click or press " + OzBindings.Cap(OzAction.LastMessage) + " to fly to the fort.|" + OzBindings.Cap(OzAction.LastMessage);
                lastAction = fortToast.action; NoteEvent(fortToast.action, "YOUR FORT IS UNDER ATTACK");
                if (!fortDanger) fortDangerSince = now;
            }
            fortDanger = danger;
        }

        // ================================================================== mission banner (guide 9.17)
        RectTransform banner; CanvasGroup bannerCg; float bannerAt = -100f;

        void BuildBanner()
        {
            var r = OzLayout.Banner;
            banner = Hud.Node("MissionBanner", Root).Place(r);
            bannerCg = banner.gameObject.AddComponent<CanvasGroup>(); bannerCg.blocksRaycasts = false;
            if (OzKit.Available) Hud.Shadow(banner, "Panels/panel_lg_mask");
            Hud.Kit(banner, "Plate", "Panels/mission_banner", new OzRect(0, 0, r.w, r.h), null);
            string lvl = game.level == "L17" ? "L17 · CAR PARK" : game.level == "L02" ? "L02 · DESERT" : game.level;
            string enemy = TeamColors.Name(Player == Team.Red ? Team.Blue : Team.Red);
            Hud.Label(banner, "Eyebrow", "OPERATION " + lvl, 11, OzUi.Amber, 0.3f).At(OzLayout.BannerTextX, OzLayout.BannerEyebrowY);
            Hud.Label(banner, "Line", $"CAPTURE THE FLAGS · DESTROY THE {enemy} FORT", 20, OzUi.Ink, 0.08f).At(OzLayout.BannerTextX, OzLayout.BannerLineY, OzH.L, false, 600);
            Hud.Label(banner, "Foot", $"YOU COMMAND {TeamColors.Name(Player)} · CPU COMMANDS {enemy} · ZOD ENGINE RULES", 9, OzUi.Muted, 0.2f, OzStyle.Plain).At(OzLayout.BannerTextX, OzLayout.BannerFootY, OzH.L, false, 600);
            banner.gameObject.SetActive(false);
        }

        void RefreshBanner()
        {
            float e = OzMotion.Now - bannerAt;
            bool on = e >= 0f && e < 0.28f + 2.4f + 0.2f && hudVisible && minK > 0.001f;
            banner.gameObject.SetActiveFast(on);
            if (!on) return;
            float kIn = OzEase.OutCubic(Mathf.Clamp01(e / 0.28f)), kOut = OzEase.InCubic(Mathf.Clamp01((e - 0.28f - 2.4f) / 0.2f));
            banner.Place(OzLayout.Banner.Move(0, -24f * (1f - kIn) - 16f * kOut));
            bannerCg.alpha = kIn * (1f - kOut) * minK;
        }

        // ================================================================== tooltip (guide 9.11)
        RectTransform tipPanel; OzText tipTitle, tipBody; RectTransform tipKey; CanvasGroup tipCg; float hoverSince, tipShownAt = -10f; HudButton tipFor; string tipShown;

        void BuildTooltip()
        {
            tipPanel = Hud.Node("Tooltip", Root); tipPanel.anchorMin = tipPanel.anchorMax = Vector2.zero; tipPanel.pivot = new Vector2(0, 1);
            var bg = Hud.Kit(tipPanel, "Bg", "Panels/tooltip", new OzRect(0, 0, 48, 48), null); bg.rectTransform.Fill();
            if (!OzKit.Available) bg.color = OzUi.A(OzUi.GM900, 0.97f);
            tipTitle = Hud.Label(tipPanel, "Title", "", OzLayout.TipTitleSize, OzUi.Ink, 0.1f).At(OzLayout.TipPadX, OzLayout.TipPadY + 1, OzH.L, false, OzLayout.TipMaxW);
            tipBody = Hud.Label(tipPanel, "Body", "", OzLayout.TipBodySize, OzUi.Ink2, 0f, OzStyle.Plain).Body(OzLayout.TipPadX, 30, OzLayout.TipMaxW - 2 * OzLayout.TipPadX);
            tipCg = tipPanel.gameObject.AddComponent<CanvasGroup>(); tipCg.blocksRaycasts = false;
            // (+5) and the end screen (+10) are their own overlay canvases above the HUD; the tooltip sorts on top of every one of them
            var tipCanvas = tipPanel.gameObject.AddComponent<Canvas>(); tipCanvas.overrideSorting = true; tipCanvas.sortingOrder = sortingOrder + 40;
            tipPanel.gameObject.SetActive(false);
        }

        void RefreshTooltip()
        {
            var b = HotButton; float now = OzMotion.Now;
            if (OzUnitPopup.Owns(b)) b = null;                                                    // a build-menu portrait: the unit card speaks for it (OzUnitPopup)
            if (b != tipFor) { bool chain = tipFor != null && tipPanel.gameObject.activeSelf; tipFor = b; hoverSince = chain ? now - 1f : now; }
            bool dragMinimap = Minimap && Minimap.Dragging;
            bool show = b != null && !string.IsNullOrEmpty(b.Tooltip) && now - hoverSince > 0.35f && !Mouse.current.leftButton.isPressed && !dragMinimap
                        && (hudVisible || b.Priority >= 100);                                          // hidden HUD: only the modal screens' own tips (pause menu, end screen)
            if (show && !tipPanel.gameObject.activeSelf) tipShownAt = now;
            tipPanel.gameObject.SetActiveFast(show);
            if (!show) return;
            tipPanel.SetAsLastSibling();
            tipCg.alpha = OzMotion.T(tipShownAt, OzMotion.Dur(0.1f, true));
            if (b.Tooltip != tipShown)
            {
                tipShown = b.Tooltip;
                var parts = b.Tooltip.Split('|');
                tipTitle.Set(parts[0].ToUpperInvariant());
                string body = parts.Length > 1 ? parts[1] : "", key = parts.Length > 2 ? parts[2] : "";
                tipBody.Set(body);
                if (tipKey) Destroy(tipKey.gameObject);
                tipKey = null;
                float keyW = key.Length > 0 ? key.Length * 6.2f + 24f : 0f;                                   // keycap + gap after the title
                float bodyW = string.IsNullOrEmpty(body) ? 0f : tipBody.preferredWidth;                      // widest unwrapped line
                float w = Mathf.Clamp(Mathf.Max(tipTitle.preferredWidth + keyW, bodyW) + 2 * OzLayout.TipPadX, 160f, OzLayout.TipMaxW);
                if (key.Length > 0) tipKey = Hud.Keycap(tipPanel, "Key", key, w - OzLayout.TipPadX + 4, OzLayout.TipPadY - 1);
                tipBody.rectTransform.sizeDelta = new Vector2(w - 2 * OzLayout.TipPadX, 200);
                float bodyH = string.IsNullOrEmpty(body) ? 0 : tipBody.preferredHeight + 6;
                tipPanel.sizeDelta = new Vector2(w, 2 * OzLayout.TipPadY + 14 + bodyH);
            }
            var size = tipPanel.sizeDelta; var cs = CanvasSize;
            var p = ScreenToCanvas(Pointer) + new Vector2(OzLayout.TipDX, -OzLayout.TipDY);                 // canvas origin bottom-left; panel pivot top-left
            if (p.x + size.x > cs.x - OzLayout.M) p.x = ScreenToCanvas(Pointer).x - OzLayout.TipDX - size.x;
            if (p.y - size.y < OzLayout.M) p.y = ScreenToCanvas(Pointer).y + OzLayout.TipDY + size.y;
            p.x = Mathf.Clamp(p.x, OzLayout.M, Mathf.Max(OzLayout.M, cs.x - OzLayout.M - size.x));
            // tooltip sits BESIDE the production window (right, else left), level with the card, never over the card grid
            if (Production && Production.IsOpen && Contains(Production.WindowRect, Pointer))
            {
                var wc = new Vector3[4]; Production.WindowRect.GetWorldCorners(wc);
                var lo = ScreenToCanvas(RectTransformUtility.WorldToScreenPoint(null, wc[0])); var hi = ScreenToCanvas(RectTransformUtility.WorldToScreenPoint(null, wc[2]));
                float x = hi.x + 10f; if (x + size.x > cs.x - OzLayout.M) x = lo.x - 10f - size.x;
                float y = Mathf.Clamp(ScreenToCanvas(Pointer).y + size.y * 0.5f, OzLayout.M + size.y, cs.y - OzLayout.M);
                p = new Vector2(Mathf.Clamp(x, OzLayout.M, Mathf.Max(OzLayout.M, cs.x - OzLayout.M - size.x)), y);
            }
            tipPanel.anchoredPosition = p;
        }
    }
}
