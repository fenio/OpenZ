using System;
using UnityEngine;
using UnityEngine.UI;

namespace OpenZ
{
    // ============================================================================================================================
    //  (5) OPTIONS is a centred modal now (was pinned next to the list at x 620 and 460 u wide): the same rows, 1.2x
    //      (552 u wide on the 1080 u canvas: 552 px at 1080p, 818 px at 2560x1600; at most 80 % of the height), over a dark
    //      scrim that blocks the list; ESC or a click on the scrim closes it (a click on the plate itself does not: a silent
    //      blocker hot covers it, review fix), and while a modal is open the keyboard only answers Esc (Keys() hook, review fix:
    //      W/S + Space moved and ran the list behind it). Two more rows at the end: MUSIC (the oz_music
    //      switch that was hidden) and FULLSCREEN (native resolution, OzSettings).
    //  (6) CONTROLS is a centred 1120 x 720 u modal with the two-column OzControlsTable and key rebinding (OzBindings).
    //  (8) HEAD START is its own switch on every difficulty and both maps (was: greyed out unless MEDIUM + L02), readable
    //      label, a tooltip plate on hover; MEDIUM is the default difficulty (OpenZDifficulty).
    //  (9) a map card hover plays ui_hover (the FocusCard hook in OpenZMainMenu.cs; was ui_level_preview = demo-flyby-whoosh).
    // Hooks in OpenZMainMenu.cs (apply.py): partial class, BuildOptions -> ExeOptions, BuildControls -> BuildControlsExe,
    // TickSide scale x SideScale, Update -> TickExe / TickHeadTip, Keys() waits while a key is captured, the scrim's silent hover.
    // ============================================================================================================================
    public partial class OpenZMainMenu
    {
        const float OptionsMaxScale = 1.2f, ModalMaxH = 864f;                                                 // 80 % of the 1080 u canvas
        float optionsScale = OptionsMaxScale, controlsScale = 1f, changesScale = 1f; RectTransform exeMusicRow, exeFullRow;
        Image modalScrim; Side scrimFor = Side.None;
        OzControlsTable controlsTable;
        bool headTipOn; RectTransform headTip; CanvasGroup headTipCg; float headTipK;

        float SideScale(Side s) => s == Side.Options ? optionsScale : s == Side.Controls ? controlsScale : s == Side.Changes ? changesScale : 1f;

        /// <summary>End of BuildOptions: the two new rows, the tab strip, then the modal beside the menu list.</summary>
        void ExeOptions(RectTransform p)
        {
            int j = toggles.Count;                                                                             // after every row the live BuildOptions made
            AddToggle(p, j, "MUSIC · SOUNDTRACK", () => AudioDirector.MusicEnabled, v =>
            {
                AudioDirector.MusicEnabled = v;                                                               // saves oz_music, fades the menu music in / out
                Debug.Log($"[OpenZ Menu] music: {(v ? "ON" : "OFF")} (oz_music {(v ? 1 : 0)}, saved)");
            });
            AddToggle(p, j + 1, OzSettings.FullLabel, () => OzSettings.Fullscreen, v => OzSettings.Fullscreen = v);
            if (OpenZ.Intro.OzIntro.Offered) AddToggle(p, j + 2, "PLAY INTRO · ON GAME START", () => OpenZ.Intro.OzIntro.Enabled, v => OpenZ.Intro.OzIntro.Enabled = v);
            exeMusicRow = toggles[j].row.Rt; exeFullRow = toggles[j + 1].row.Rt;
            AddCycle(p, toggles.Count, OzSettings.ResLabel, () => OzSettings.ResolutionName, OzSettings.StepResolution);   // RESOLUTION, under FULLSCREEN (OzSettingTips.TabRows): NATIVE (default) and the lower steps of this display (oz_res_h), saved and applied at once
            toggles[toggles.Count - 1].value.At(OptW - 2 * OzLayout.OptX - 16, OzLayout.OptToggleH * 0.5f, OzH.R, true, OzSettings.ResValueW);   // its longest value is wider than the 170 u of a cycle row
            AddToggle(p, toggles.Count, OzOvertake.Label, () => OzOvertake.On, v => OzOvertake.On = v);         // DV105 FAST UNITS PASS SLOW ONES: oz_overtake, default OFF, from the next match (OzOvertake.cs); its tab and place come from OzSettingTips
            AddToggle(p, toggles.Count, OzFreeCam.Label, () => OzFreeCam.On, v => OzFreeCam.On = v);         // FREE CAMERA: oz_free_camera, default OFF, live (OzFreeCam.cs); its tab and place come from OzSettingTips
            AddCycle(p, toggles.Count, OzOverlapOption.Label, () => OzOverlapOption.Name, OzOverlapOption.Step);   // FRIENDLY UNITS OVERLAP: NO (default) / YES, oz_overlap, live (OzOverlapOption.cs); its tab and place (under HIT FLASH, GAME tab) come from OzSettingTips
            AddCycle(p, toggles.Count, OzTrackpadScroll.Label, () => OzTrackpadScroll.Name, OzTrackpadScroll.Step);   // TRACKPAD SCROLL: ZOOM (default) / MOVE MAP, oz_trackpad_scroll, live (OzTrackpadScroll.cs); its tab and place (ADVANCED, under TOUCH · GAMEPAD) come from OzSettingTips
            float h = BuildOptTabs(p);                                                                        // options tabs: the strip, every tab's rows from the top; the plate is as tall as the tallest tab
            optionsScale = Mathf.Min(OptionsMaxScale, ModalMaxH / h, (CW - OzMenuLayout.CreditsX - 60f) / OptW);   // beside the list: 60 u stay free on the right
            Modal(optionsRt, OptW, h, Side.Options, true);
        }

        /// <summary>CONTROLS: the centred rebinding table (replaces the old keycap list).</summary>
        void BuildControlsExe()
        {
            controlsRt = SidePanel("Controls", "Panels/options_panel", out controlsCg);
            Modal(controlsRt, OzControlsTable.FullW, OzControlsTable.FullH, Side.Controls, true);
            var p = controlsRt;
            controlsTable = OzControlsTable.BuildFull(p, (rt, click, hover) => AddHot(rt, View.Main, click, hover, null, "ui_click", Side.Controls), Sfx);
            SmallButton(p, "ResetKeys", "RESET TO DEFAULTS", new OzRect(32, OzControlsTable.FullH - 60, 220, 36), View.Main, Side.Controls, () => controlsTable.ResetAll());
        }

        /// <summary>Centre a side panel on the canvas at w x h, or (beside) put it next to the menu list like ROADMAP (its plate follows), and put the scrim's hot UNDER the panel's hots
        /// (Poll walks the list from the end, so the panel's rows win and the menu list behind is blocked). Review fix: a blocker hot
        /// for the panel rect itself (no click, no hover tick) sits between the two, so a click on a label, title, band or gap
        /// inside the plate does nothing; only a click outside the plate reaches the scrim and closes the modal. The blocker's
        /// rect is the panel's, so RectangleContainsScreenPoint follows the 1.2x localScale.</summary>
        /// <summary>A panel that would have to shrink below this to fit beside the list is centred at full size instead.</summary>
        public const float BesideMinFit = 0.8f;
        void Modal(RectTransform rt, float w, float h, Side s, bool beside = false)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(w, h); rt.anchoredPosition = new Vector2(0f, -6f);
            // beside the menu list like ROADMAP / CREDITS: left edge at CreditsX, the same mid-line, scaled down to keep 60 u free on the right
            // and to end 12 u above the flyby caption plate in the bottom right corner (a taller panel would cover its top edge)
            float fit = beside ? Mathf.Min(1f, (CW - OzMenuLayout.CreditsX - 60f) / w, 2f * (OzMenuLayout.Caption.y - 12f - OzLayout.RefH * 0.5f) / h) : 1f;
            if (beside && fit < BesideMinFit) { beside = false; fit = 1f; }                    // a small or narrow window: the centred plate at full size stays readable
            if (s == Side.Controls) controlsScale = fit; else if (s == Side.Changes) changesScale = fit;
            if (beside)
            {
                rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f); rt.pivot = new Vector2(0f, 0.5f);
                rt.anchoredPosition = new Vector2(OzMenuLayout.CreditsX, 0f);
            }
            var plate = rt.Find("Plate") as RectTransform; if (plate) plate.sizeDelta = new Vector2(w, h);
            // review fix 2: Build() (a resolution change: the first switch to fullscreen, the FULLSCREEN row, Alt+Enter) destroys the
            // old root, but Destroy lands at the end of the frame, so the old scrim still looks alive here. Reusing it left the
            // modal with no backdrop and let a click outside the plate reach the menu list (QUIT / PLAY). A scrim that is not under
            // the current mainRoot is replaced; it keeps the old alpha (no blink when the modal is open) and scrimFor = None makes
            // TickExe put it under the open panel again.
            if (!modalScrim || modalScrim.transform.parent != mainRoot)
            {
                float keepA = modalScrim ? modalScrim.color.a : 0f;
                modalScrim = Hud.Img(mainRoot, "ModalScrim", OzUi.A(OzUi.GM950, keepA)); modalScrim.rectTransform.Fill();
                modalScrim.gameObject.SetActive(keepA > 0.001f); scrimFor = Side.None;
            }
            var hot = new Hot { rt = modalScrim.rectTransform, view = View.Main, side = s, click = CloseSide, sfx = "" };   // "": no hover tick (Poll hook)
            int first = hots.FindIndex(x => x.side == s);
            if (first < 0) hots.Add(hot); else hots.Insert(first, hot);
            var block = new Hot { rt = rt, view = View.Main, side = s, click = null, sfx = "" };            // review fix: the plate swallows its own clicks
            hots.Insert(hots.IndexOf(hot) + 1, block);
            Debug.Log($"[OpenZ Menu] {s} panel: {(beside ? "beside the list, left edge " + OzMenuLayout.CreditsX.ToString("0") + " u," : "centred modal")} {w * SideScale(s):0} x {h * SideScale(s):0} u on the {CW:0} x 1080 u canvas ({Screen.width}x{Screen.height})");
        }

        /// <summary>Update, main view: the scrim, the key capture.</summary>
        void TickExe(float dt)
        {
            bool modal = side == Side.Options || side == Side.Controls || side == Side.Changes;
            if (modalScrim)
            {
                float target = modal ? 0.62f : 0f, dur = Mathf.Max(0.01f, OzMotion.Dur(OzMotion.Short, true));
                float a = Mathf.MoveTowards(modalScrim.color.a, target, dt / dur * 0.62f);
                if (a > 0.001f && !modalScrim.gameObject.activeSelf) modalScrim.gameObject.SetActive(true);
                if (Mathf.Abs(modalScrim.color.a - a) > 0.001f) modalScrim.color = OzUi.A(OzUi.GM950, a);
                if (a <= 0.001f && !modal && modalScrim.gameObject.activeSelf) modalScrim.gameObject.SetActive(false);
                if (modal && scrimFor != side)
                {
                    scrimFor = side;
                    modalScrim.transform.SetAsLastSibling();
                    var rt = SideRt(side); if (rt) rt.SetAsLastSibling();
                }
                if (!modal) scrimFor = Side.None;
            }
            if (controlsTable != null) { if (side == Side.Controls) controlsTable.Tick(); else controlsTable.Cancel(false); }
        }

        // ---------------------------------------------------------------- HEAD START (level select)
        /// <summary>BuildDeployBar, after the head start block: the "L02 ONLY" chip goes (both maps have one), the label reads
        /// brighter, and a tooltip plate explains the switch.</summary>
        void ExeHeadStart(RectTransform p, (Image bg, Image edge, OzText text) chip)
        {
            if (chip.bg) chip.bg.transform.parent.gameObject.SetActive(false);
            var lbl = p.Find("HeadLabel"); if (lbl && lbl.GetComponent<Graphic>() is Graphic g) g.color = OzUi.Ink2;
            var hr = OzMenuLayout.HeadStart; float headX = XR(hr.x);
            headTip = Hud.Node("HeadStartTip", p).Place(new OzRect(headX - 70, hr.y - 156, 336, 136));
            Hud.Plate(headTip, "md", 0f, OzUi.Amber);
            headTipCg = headTip.gameObject.AddComponent<CanvasGroup>(); headTipCg.alpha = 0f; headTipCg.blocksRaycasts = false;
            Hud.Label(headTip, "Title", "HEAD START · KEY H", 12, OzUi.Amber, 0.2f).At(16, 14, OzH.L, false, 300);
            Hud.Label(headTip, "Body", "Extra vehicles wait at your fort when the match starts.\nL02 DESERT: 2 light tanks, 2 jeeps, 1 mech, crewed.\nL16 JUNGLE: 2 jeeps, 1 light tank, crewed.\nL17 CAR PARK: 1 MLRS, 1 heavy tank, parked empty.\nOFF: the standard map start.",
                      11, OzUi.Ink2, 0f, OzStyle.Plain).Body(16, 36, 304);
            headTip.gameObject.SetActive(false);
        }

        /// <summary>RefreshDeployBar: the switch on every difficulty and both maps (menu_exe_v1).</summary>
        void RefreshHeadStart(GameDifficulty d)
        {
            string id = levels[focusCard].Id; bool on = OpenZDifficulty.HeadStartToggle; int n = OpenZDifficulty.HeadStartCount(id);
            if (!headRow.Interactable) { headRow.Interactable = true; headRow.Refresh(); }
            headText.Set(n > 0 ? $"+{n} VEHICLES" : "NOT ON THIS MAP"); headText.color = n > 0 ? (Color)OzUi.Ink : (Color)OzUi.Dim;
            headState.Set(on ? "ON" : "OFF"); headState.color = on ? (Color)OzUi.Amber : (Color)OzUi.Muted;
            headKnob.color = on ? (Color)OzUi.Amber : (Color)OzUi.GM500;
            headKnob.rectTransform.anchoredPosition = new Vector2(DeployBlockW - 12 - 48 + (on ? 24 : 2), headKnob.rectTransform.anchoredPosition.y);   // K347: the deploy bar's block width (UI/OpenZMainMenu.DeployBar.cs)
            headHint.Set(on ? OzDeployBar.HeadHint(id) : OzDeployBar.HeadOff);                                  // K347: the block's own short hint (the tooltip keeps the full list)
            headHint.color = on ? (Color)OzUi.Ink2 : (Color)OzUi.Muted;
        }

        // ---------------------------------------------------------------- proof hooks (MenuExeProof, Play only)
        public void ProofOpen(string which) { var s = which == "options" ? Side.Options : which == "controls" ? Side.Controls : which == "changes" ? Side.Changes : which == "roadmap" ? Side.Roadmap : Side.None; if (s == Side.None) CloseSide(); else OpenSide(s); }
        public RectTransform ProofPanel(string which) => which == "options" ? optionsRt : which == "controls" ? controlsRt : which == "changes" ? changesRt : which == "roadmap" ? roadmapRt : null;
        public OzControlsTable ProofTable => controlsTable;
        public void ProofSelect() { if (side != Side.None) CloseSide(); GoSelect(); }
        public void ProofMain() { GoMain(); }
        public void ProofFocus(int card) => FocusCard(card, false);
        public void ProofToggleHeadStart() => ToggleHeadStart();
        public void ProofHeadTip(bool on) => headTipOn = on;
        public bool ProofHeadRowLive => headRow != null && headRow.Interactable;
        public string ProofHeadText => headText != null ? headText.text + " / " + (headHint != null ? headHint.text : "") : "";
        public float ProofCanvasWidth => CW;
        public bool ProofOptionRows => exeMusicRow && exeFullRow;
        public string ProofSide => side.ToString();
        /// <summary>Play check: screen points on the left and the right arrow of the value row `label` (for ProofClickAt).</summary>
        public bool ProofArrowPoints(string label, out Vector2 left, out Vector2 right)
        {
            left = right = default;
            foreach (var t in toggles)
                if (t.label == label && t.value != null)
                {
                    ShowOptTab(t.tab); var rt = t.value.rectTransform; var r = rt.rect; float tw = Mathf.Min(t.value.preferredWidth, r.width);
                    left = RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint(new Vector3(r.xMax - tw + 5f, r.center.y, 0f)));
                    right = RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint(new Vector3(r.xMax - 5f, r.center.y, 0f)));
                    return true;
                }
            return false;
        }
        public int ProofFocusItem => focusItem;
        /// <summary>Review fix 2: the rebuild a resolution change runs (Update -> Build when Screen.width / height change).</summary>
        public void ProofRebuild() => Build();
        /// <summary>The scrim is live for the open modal: under the current mainRoot, shown, dark, right under the open panel.</summary>
        public bool ProofScrimOk
        {
            get
            {
                var rt = SideRt(side);
                return modalScrim && modalScrim.transform.parent == mainRoot && modalScrim.gameObject.activeInHierarchy && modalScrim.color.a > 0.5f
                       && rt && rt.parent == mainRoot && modalScrim.transform.GetSiblingIndex() == rt.GetSiblingIndex() - 1;
            }
        }
        public string ProofScrimInfo
        {
            get
            {
                if (!modalScrim) return "no scrim";
                var rt = SideRt(side);
                return $"scrim under {(modalScrim.transform.parent == mainRoot ? "the current Main" : "an OLD root")}, {(modalScrim.gameObject.activeInHierarchy ? "shown" : "hidden")}, alpha {modalScrim.color.a:0.00}, sibling {modalScrim.transform.GetSiblingIndex()} / panel {(rt ? rt.GetSiblingIndex().ToString() : "none")}";
            }
        }

        /// <summary>The hot a click at screen point mp lands on (the same walk as Poll): "scrim", "plate" (the blocker), "control"
        /// or "none".</summary>
        public string ProofHotAt(Vector2 mp) { var h = HotAt(mp); return h == null ? "none" : h.rt == (modalScrim ? modalScrim.rectTransform : null) ? "scrim" : h.click == null && (h.rt == optionsRt || h.rt == controlsRt) ? "plate" : "control"; }

        /// <summary>A left click at screen point mp as Poll resolves it (press + release on the same hot): its sound, then its click.</summary>
        public string ProofClickAt(Vector2 mp)
        {
            string what = ProofHotAt(mp); var h = HotAt(mp);
            if (h != null && h.click != null) { if (!string.IsNullOrEmpty(h.sfx)) Sfx(h.sfx); lastMouse = mp; h.click(); }
            return what;
        }

        Hot HotAt(Vector2 mp)
        {
            for (int i = hots.Count - 1; i >= 0; i--)
            {
                var h = hots[i];
                if (h.view != view || (h.side != Side.Any && h.side != side) || !h.rt || !h.rt.gameObject.activeInHierarchy) continue;
                if (h.btn != null && !h.btn.Interactable) continue;
                if (RectTransformUtility.RectangleContainsScreenPoint(h.rt, mp, null)) return h;
            }
            return null;
        }

        /// <summary>Hot rect -> "TITLE|body|" (OzSettingTips) for every OPTIONS row; the plate shows beside the modal while one is hovered.</summary>
        readonly System.Collections.Generic.Dictionary<RectTransform, string> optTips = new System.Collections.Generic.Dictionary<RectTransform, string>();
        RectTransform optTip; CanvasGroup optTipCg; OzText optTipTitle, optTipBody; string optTipShown; RectTransform optTipFor, proofTipRow; float optTipK, optTipSince;
        public static int OptTipShows { get; private set; }
        const float OptTipW = 320f, OptTipPad = 14f;

        void BuildOptTip()
        {
            optTip = Hud.Node("OptionsTip", mainRoot); optTip.anchorMin = optTip.anchorMax = new Vector2(0.5f, 0.5f); optTip.pivot = new Vector2(0f, 0.5f);
            optTip.sizeDelta = new Vector2(OptTipW, 60f);
            var bg = Hud.Kit(optTip, "Bg", "Panels/tooltip", new OzRect(0, 0, 48, 48), null); bg.rectTransform.Fill();
            if (!OzKit.Available) bg.color = OzUi.A(OzUi.GM900, 0.97f);
            optTipTitle = Hud.Label(optTip, "Title", "", OzLayout.TipTitleSize, OzUi.Amber, 0.12f).At(OptTipPad, OptTipPad, OzH.L, false, OptTipW - 2 * OptTipPad);
            optTipBody = Hud.Label(optTip, "Body", "", OzLayout.TipBodySize + 1, OzUi.Ink2, 0f, OzStyle.Plain).Body(OptTipPad, OptTipPad + 20, OptTipW - 2 * OptTipPad);
            optTipCg = optTip.gameObject.AddComponent<CanvasGroup>(); optTipCg.blocksRaycasts = false; optTipCg.alpha = 0f;
            optTip.gameObject.SetActive(false);
            optTipShown = null; optTipFor = null; optTipK = 0f;
        }

        /// <summary>Update: the hovered OPTIONS row's tip fades in after 0.3 s beside the modal (right of it, else left), level with the row,
        /// always the last sibling of the menu root (over the modal and its scrim).</summary>
        void TickOptTip()
        {
            if (!optTip) return;
            string tip = null; RectTransform rowRt = null;
            var hr = proofTipRow ? proofTipRow : hotOver != null ? hotOver.rt : null;
            if (view == View.Main && side == Side.Options && hr && optTips.TryGetValue(hr, out var t)) { tip = t; rowRt = hr; }
            if (rowRt != optTipFor) { optTipFor = rowRt; optTipSince = OzMotion.Now; }
            bool want = tip != null && OzMotion.Now - optTipSince > 0.3f;
            float dur = Mathf.Max(0.01f, OzMotion.Dur(0.12f, true));
            optTipK = Mathf.MoveTowards(optTipK, want ? 1f : 0f, Time.unscaledDeltaTime / dur);
            if (optTipK <= 0.001f) { if (optTip.gameObject.activeSelf) optTip.gameObject.SetActive(false); return; }
            if (!optTip.gameObject.activeSelf) { optTip.gameObject.SetActive(true); OptTipShows++; }
            optTip.SetAsLastSibling();
            optTipCg.alpha = OzEase.OutCubic(optTipK);
            if (tip == null || rowRt == null) return;                                                      // fading out: keep the last text / place
            if (tip != optTipShown)
            {
                optTipShown = tip;
                var parts = tip.Split('|');
                optTipTitle.Set(parts[0].ToUpperInvariant());
                optTipBody.Set(parts.Length > 1 ? parts[1] : "");
                float bh = parts.Length > 1 && parts[1].Length > 0 ? optTipBody.preferredHeight + 6f : 0f;
                optTip.sizeDelta = new Vector2(OptTipW, 2 * OptTipPad + 16f + bh);
            }
            // beside the modal plate (screen space: overlay canvas, world = pixels), level with the hovered row
            var pc = new Vector3[4]; optionsRt.GetWorldCorners(pc);
            var rc = new Vector3[4]; rowRt.GetWorldCorners(rc);
            float sf = canvas && canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f, wPx = OptTipW * sf, gap = 14f * sf;
            float x = pc[2].x + gap; if (x + wPx > Screen.width - 8f) x = pc[0].x - gap - wPx;
            float hPx = optTip.sizeDelta.y * sf, y = Mathf.Clamp((rc[0].y + rc[1].y) * 0.5f, hPx * 0.5f + 8f, Screen.height - hPx * 0.5f - 8f);
            optTip.position = new Vector3(Mathf.Round(x), Mathf.Round(y), 0f);
        }

        /// <summary></summary>
        public string ProofOptTip(string label)
        {
            proofTipRow = null;
            foreach (var t in toggles)
                if (t.label == label && optTips.TryGetValue(t.row.Rt, out var tip)) { ShowOptTab(t.tab); proofTipRow = t.row.Rt; optTipFor = t.row.Rt; optTipSince = OzMotion.Now - 1f; return tip; }
            return null;
        }
        public RectTransform ProofOptTipRt => optTip;
        public int ProofOptTipCount => optTips.Count;

        /// <summary>Update, select view: the tooltip fades in while the switch is hovered.</summary>
        void TickHeadTip()
        {
            if (!headTip) return;
            float dur = Mathf.Max(0.01f, OzMotion.Dur(OzMotion.Short, true));
            headTipK = Mathf.MoveTowards(headTipK, headTipOn ? 1f : 0f, Time.unscaledDeltaTime / dur);
            if (headTipK > 0.001f && !headTip.gameObject.activeSelf) headTip.gameObject.SetActive(true);
            headTipCg.alpha = OzEase.OutCubic(headTipK);
            if (headTipK <= 0.001f && headTip.gameObject.activeSelf) headTip.gameObject.SetActive(false);
        }
    }
}
