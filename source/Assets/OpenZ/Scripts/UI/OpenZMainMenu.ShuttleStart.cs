using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace OpenZ
{
    // ============================================================================================================================
    // K347: the SHUTTLE box of the battlefield screen (rule DV118; its state, words and rule: UI/OzShuttleStart.cs). The OPTIONS row
    // SHUTTLE is gone: the level's own parked shuttle is an extra chosen here, like the extra vehicles of HEAD START.
    //  * The third block of the deploy bar's row (UI/OpenZMainMenu.DeployBar.cs), a peer of DIFFICULTY and HEAD START: the same width,
    //    its label with the key cap U, HEAD START's switch row (+1 SHUTTLE, ON / OFF, the switch) and its own short hint, on the bar's
    //    shared label / row / hint lines, a hairline in the gap before it.
    //  * Level 16 only: on EASY and MEDIUM it is live; on HARD the slot stays, locked (the disabled row "NOT ON HARD", the hint
    //    "EASY · MEDIUM ONLY", label and key cap dimmed), so a click on HARD never moves a button under the mouse; between the two the
    //    row's content cross-fades (0.16 s, ease out; CALM MODE: 0.12 s). Levels 2 and 17 have no slot: the block fades out (0.08 s),
    //    then DIFFICULTY and HEAD START slide one slot towards DEPLOY (0.16 s); back on level 16 they slide first and the block fades in
    //    once HEAD START has left its slot, so no text ever lies over another.
    //  * ON by default, saved at once (oz_start_shuttle) and remembered like HEAD START. A click on the row or the key U toggles it
    //    (ui_toggle_on / ui_toggle_off); the key where the box is not offered (HARD, other levels) answers ui_disabled. Its tooltip
    //    opens centred over the block, 8 u over the bar.
    // Hooks in OpenZMainMenu.cs: BuildDeployBar -> BuildDeployConsole -> BuildShuttleStart / BuildShuttleTip, RefreshDeployBar ->
    // RefreshDeployConsole -> RefreshShuttleStart, Keys -> the key, BuildSelect's key hint row names the key; the OPTIONS row SHUTTLE
    // is not built (OzShuttleStart.InOptions, both menus).
    // ============================================================================================================================
    public partial class OpenZMainMenu
    {
        /// <summary>The SHUTTLE box's key on the battlefield screen (by its place on the keyboard, as H and T).</summary>
        public const Key ShuttleKey = Key.U;

        RectTransform shuttleRt, shuttleTip, shuttleKeyRt; CanvasGroup shuttleCg, shuttleTipCg, shuttleKeyCg, shuttleLiveCg, shuttleLockCg;
        HudButton shuttleRow; Image shuttleKnob, shuttleTrack; OzText shuttleLabel, shuttleState, shuttleHint, shuttleLockHint;
        bool shuttleShown, shuttleTipOn; float shuttleTipK;
        /// <summary>The block's alpha and its live / locked cross-fade (0 live, 1 locked), each eased out towards its target from where it
        /// was when the target changed (OutCubic, OzMotion.Short; CALM MODE: 0.12 s), so a fade never starts slowly in either direction.</summary>
        float shuttleK, shuttleKFrom, shuttleKTo, shuttleKAt = -10f, shuttleLockK, shuttleLockFrom, shuttleLockTo, shuttleLockAt = -10f;
        /// <summary>Checks: toggles by click or key, key presses where the box was not offered.</summary>
        public static int ShuttleToggles { get; private set; }
        public static int ShuttleKeyRefused { get; private set; }

        /// <summary>BuildDeployConsole, before DIFFICULTY and HEAD START (they draw and take clicks over it while it fades out).</summary>
        void BuildShuttleStart(RectTransform p, OzDeployBar.Layout L)
        {
            float x = L.X(2, 3), bw = L.blockW, top = OzDeployBar.LineTop, rh = OzDeployBar.RowH, mid = rh * 0.5f;
            shuttleRt = Hud.Node("ShuttleBlock", p).Place(new OzRect(x, top, bw, OzDeployBar.LineBottom - top));
            shuttleCg = shuttleRt.gameObject.AddComponent<CanvasGroup>(); shuttleCg.blocksRaycasts = false;
            Hairline(shuttleRt, "Line1", -L.gap * 0.5f, 0f);                                                // the gap before it: comes and goes with the block
            shuttleLabel = Hud.Label(shuttleRt, "Label", OzShuttleStart.Label, 10, OzUi.Ink2, 0.24f, OzStyle.Plain).At(0, OzDeployBar.LabelY - top, OzH.L, false, bw - 22f);
            shuttleKeyRt = Hud.Keycap(shuttleRt, "Key", OzKeyNames.Name(ShuttleKey), bw, OzDeployBar.LabelY - 2f - top);
            shuttleKeyCg = shuttleKeyRt.gameObject.AddComponent<CanvasGroup>(); shuttleKeyCg.blocksRaycasts = false;
            shuttleRow = HudButton.Make(shuttleRt, "ShuttleRow", null, new Vector2(bw, rh), HudButtonKind.Small);
            shuttleRow.Rt.Place(new OzRect(0, OzDeployBar.RowY - top, bw, rh));
            shuttleTrack = Hud.Kit(shuttleRow.Content, "Track", "Buttons/switch_track", new OzRect(bw - OzDeployBar.TrackRX, (rh - OzDeployBar.TrackH) * 0.5f, OzDeployBar.TrackW, OzDeployBar.TrackH), OzKit.Available ? (Color?)null : OzUi.GM950);
            // live: the words, ON / OFF, the knob; locked (HARD): its own words and an OFF knob; the two cross-fade
            var live = Hud.Node("Live", shuttleRow.Content); live.Fill();
            shuttleLiveCg = live.gameObject.AddComponent<CanvasGroup>(); shuttleLiveCg.blocksRaycasts = false;
            Hud.Label(live, "Text", OzShuttleStart.RowText, 11, OzUi.Ink, 0.1f, OzStyle.Plain).At(OzDeployBar.WordsX, mid, OzH.L, true, WordsBox(bw, true));
            shuttleKnob = Hud.Kit(live, "Knob", "Buttons/switch_knob", new OzRect(0, (rh - OzDeployBar.KnobH) * 0.5f, OzDeployBar.KnobW, OzDeployBar.KnobH), OzUi.Amber, Image.Type.Simple);
            shuttleState = Hud.Label(live, "State", "", 9, OzUi.Amber, 0.2f, OzStyle.Plain).At(bw - OzDeployBar.StateRX, mid, OzH.R, true, 40);
            var locked = Hud.Node("Locked", shuttleRow.Content); locked.Fill();
            shuttleLockCg = locked.gameObject.AddComponent<CanvasGroup>(); shuttleLockCg.blocksRaycasts = false;
            Hud.Label(locked, "Text", OzShuttleStart.LockedRow, 11, OzUi.Dim, 0.1f, OzStyle.Plain).At(OzDeployBar.WordsX, mid, OzH.L, true, WordsBox(bw, false));
            Hud.Kit(locked, "Knob", "Buttons/switch_knob", new OzRect(bw - OzDeployBar.TrackRX + 2f, (rh - OzDeployBar.KnobH) * 0.5f, OzDeployBar.KnobW, OzDeployBar.KnobH), OzUi.A(OzUi.GM500, OzDeployBar.LockKnobA), Image.Type.Simple);
            AddHot(shuttleRow.Rt, View.Select, () => ToggleShuttleStart(false), hv => shuttleTipOn = hv, shuttleRow, null);
            shuttleHint = Hud.Label(shuttleRt, "Hint", "", 9, OzUi.Muted, 0.16f, OzStyle.Plain).At(0, OzDeployBar.HintY - top, OzH.L, false, bw);
            shuttleLockHint = Hud.Label(shuttleRt, "HintLocked", OzShuttleStart.LockedHint, 9, OzUi.Dim, 0.16f, OzStyle.Plain).At(0, OzDeployBar.HintY - top, OzH.L, false, bw);

            // a rebuild (a new screen size) shows the state at once
            string id = levels[focusCard].Id; var d = OpenZDifficulty.Level;
            bool slot = OzShuttleStart.HasSlot(id);
            shuttleShown = slot && OzShuttleStart.Offered(id, d);
            shuttleK = shuttleKFrom = shuttleKTo = slot ? 1f : 0f; shuttleKAt = -10f;
            shuttleLockK = shuttleLockFrom = shuttleLockTo = slot && !shuttleShown ? 1f : 0f; shuttleLockAt = -10f;
            shuttleTipK = 0f; shuttleTipOn = false;
            ShowShuttle();
        }

        /// <summary>BuildDeployConsole, after HEAD START's tooltip: the SHUTTLE tooltip (HEAD START's plate, words and size).</summary>
        void BuildShuttleTip(RectTransform p)
        {
            shuttleTip = Hud.Node("ShuttleStartTip", p).Place(new OzRect(0, OzDeployBar.TipBottom - OzDeployBar.TipH, OzDeployBar.TipW, OzDeployBar.TipH));
            Hud.Plate(shuttleTip, "md", 0f, OzUi.Amber);
            shuttleTipCg = shuttleTip.gameObject.AddComponent<CanvasGroup>(); shuttleTipCg.alpha = 0f; shuttleTipCg.blocksRaycasts = false;
            Hud.Label(shuttleTip, "Title", OzShuttleStart.TipTitle + OzKeyNames.Name(ShuttleKey), 12, OzUi.Amber, 0.2f).At(16, 14, OzH.L, false, 300);
            Hud.Label(shuttleTip, "Body", OzShuttleStart.TipBody, 11, OzUi.Ink2, 0f, OzStyle.Plain).Body(16, 36, 304);
            shuttleTip.gameObject.SetActive(false);
        }

        /// <summary>RefreshDeployConsole, every frame on the battlefield screen: the slot (level 16), live or locked (HARD), the fades, the row, the tooltip.</summary>
        void RefreshShuttleStart(GameDifficulty d, bool slot)
        {
            if (!shuttleRt) return;
            bool want = slot && OzShuttleStart.Offered(levels[focusCard].Id, d);
            if (want != shuttleShown)
            {
                shuttleShown = want; if (!want) shuttleTipOn = false;
                Debug.Log($"[OpenZ Menu] shuttle box: {(want ? "offered" : slot ? "locked (HARD)" : "no slot")} ({levels[focusCard].Id}, {OpenZDifficulty.Label(d)}; {(OzShuttleStart.On ? "ON" : "OFF")})");
            }
            float dt = Time.unscaledDeltaTime, dur = Mathf.Max(0.01f, OzMotion.Dur(OzMotion.Short, true)), now = OzMotion.Now;
            bool hidden = shuttleK <= 0.001f, locked = slot && !want;
            float kTo = slot ? 1f : 0f, lTo = locked ? 1f : 0f;
            if (kTo != shuttleKTo) { shuttleKFrom = shuttleK; shuttleKTo = kTo; shuttleKAt = now; }
            if (hidden) { shuttleLockK = shuttleLockFrom = shuttleLockTo = lTo; shuttleLockAt = -10f; }                       // it comes back in its own state, no cross-fade
            else if (lTo != shuttleLockTo) { shuttleLockFrom = shuttleLockK; shuttleLockTo = lTo; shuttleLockAt = now; }
            // the slot's fade: out at once and quickly (DIFFICULTY and HEAD START wait for it), in only when HEAD START has left the slot
            float fade = Mathf.Max(0.01f, OzMotion.Dur(OzDeployBar.SlotFade, true)), after = shuttleKTo > shuttleKFrom ? OzMotion.Dur(OzMotion.Short) * OzDeployBar.SlotInAfter : 0f;
            shuttleK = Mathf.Lerp(shuttleKFrom, shuttleKTo, OzEase.OutCubic(OzMotion.T(shuttleKAt, fade, after)));
            shuttleLockK = Mathf.Lerp(shuttleLockFrom, shuttleLockTo, OzEase.OutCubic(OzMotion.T(shuttleLockAt, dur)));
            ShowShuttle();
            // the tooltip (as TickHeadTip)
            shuttleTipK = Mathf.MoveTowards(shuttleTipK, shuttleTipOn && shuttleShown ? 1f : 0f, dt / dur);
            if (shuttleTipK > 0.001f && !shuttleTip.gameObject.activeSelf) shuttleTip.gameObject.SetActive(true);
            float ta = OzEase.OutCubic(shuttleTipK); if (!Mathf.Approximately(shuttleTipCg.alpha, ta)) shuttleTipCg.alpha = ta;
            if (shuttleTipK <= 0.001f && shuttleTip.gameObject.activeSelf) shuttleTip.gameObject.SetActive(false);
        }

        /// <summary>The block's fade, the live / locked cross-fade and the row as RefreshHeadStart draws HEAD START's.</summary>
        void ShowShuttle()
        {
            bool on = shuttleK > 0.001f;
            if (shuttleRt.gameObject.activeSelf != on) shuttleRt.gameObject.SetActive(on);
            float a = shuttleK, e = shuttleLockK;                                                             // both eased already
            if (!Mathf.Approximately(shuttleCg.alpha, a)) shuttleCg.alpha = a;
            if (!Mathf.Approximately(shuttleLiveCg.alpha, 1f - e)) shuttleLiveCg.alpha = 1f - e;
            if (!Mathf.Approximately(shuttleLockCg.alpha, e)) shuttleLockCg.alpha = e;
            float ka = Mathf.Lerp(1f, OzDeployBar.LockLabelA, e); if (!Mathf.Approximately(shuttleKeyCg.alpha, ka)) shuttleKeyCg.alpha = ka;
            shuttleLabel.color = Color.Lerp(OzUi.Ink2, OzUi.Dim, e);
            var tc = OzKit.Available ? Color.white : (Color)OzUi.GM950; tc.a = Mathf.Lerp(1f, OzDeployBar.LockTrackA, e);
            if (shuttleTrack.color != tc) shuttleTrack.color = tc;
            bool live = shuttleLockK < 0.5f;                                                                  // the row takes clicks while it is live
            if (shuttleRow.Interactable != live) { shuttleRow.Interactable = live; shuttleRow.Refresh(); }
            bool box = OzShuttleStart.On; float w = shuttleRow.Rt.sizeDelta.x;
            shuttleState.Set(box ? "ON" : "OFF"); shuttleState.color = box ? (Color)OzUi.Amber : (Color)OzUi.Muted;
            shuttleKnob.color = box ? (Color)OzUi.Amber : (Color)OzUi.GM500;
            var kp = new Vector2(w - OzDeployBar.TrackRX + (box ? 24f : 2f), shuttleKnob.rectTransform.anchoredPosition.y);
            if (shuttleKnob.rectTransform.anchoredPosition != kp) shuttleKnob.rectTransform.anchoredPosition = kp;
            shuttleHint.Set(box ? OzShuttleStart.HintOn : OzShuttleStart.HintOffFor(levels[focusCard].Id));   // K444: level 17's words with the box off (its car-park shuttle stays)
            shuttleHint.color = OzUi.A(box ? OzUi.Ink2 : OzUi.Muted, 1f - e);
            shuttleLockHint.color = OzUi.A(OzUi.Dim, e);
        }

        /// <summary>A click on the row (only while the box is offered) or the key (where it is not offered: ui_disabled).</summary>
        void ToggleShuttleStart(bool byKey)
        {
            if (!shuttleShown) { if (byKey) { ShuttleKeyRefused++; Sfx("ui_disabled"); } return; }
            bool on = !OzShuttleStart.On;
            OzShuttleStart.On = on;                                                                              // saved at once
            ShuttleToggles++;
            Sfx(on ? "ui_toggle_on" : "ui_toggle_off");
            ShowShuttle();
            Debug.Log($"[OpenZ Menu] shuttle box: {(on ? "ON" : "OFF")} ({levels[focusCard].Id}, {OpenZDifficulty.Label(OpenZDifficulty.Level)}: {(on ? "the shuttle stands by your fort" : "no shuttle")}; {OzShuttleStart.Pref} {(on ? 1 : 0)}, saved)");
        }

        // ---------------------------------------------------------------- proof hooks (OzShuttleStartProof, Play only)
        public RectTransform ProofShuttleRt => shuttleRt;
        public RectTransform ProofShuttleRow => shuttleRow != null ? shuttleRow.Rt : null;
        public bool ProofShuttleRowLive => shuttleRow != null && shuttleRow.Interactable;
        public RectTransform ProofShuttleTip => shuttleTip;
        public RectTransform ProofHeadTipRt => headTip;
        public bool ProofShuttleShown => shuttleShown;
        public float ProofShuttleK => shuttleK;
        public float ProofShuttleLockK => shuttleLockK;
        public void ProofShuttleTipHover(bool on) => shuttleTipOn = on;
        public void ProofToggleShuttle(bool byKey) => ToggleShuttleStart(byKey);
        /// <summary>What the block says: "ON / PARKED BY YOUR FORT", "OFF / NO SHUTTLE", or locked "NOT ON HARD / EASY · MEDIUM ONLY".</summary>
        public string ProofShuttleText => shuttleState == null ? "" : shuttleLockK > 0.5f ? OzShuttleStart.LockedRow + " / " + shuttleLockHint.text : shuttleState.text + " / " + shuttleHint.text;
    }
}
