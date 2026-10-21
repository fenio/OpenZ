using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace OpenZ
{
    /// <summary>
    /// ARRANGE INTERFACE: the player drags the HUD groups to where he wants them; the places are remembered (OzHudLayout).
    ///
    /// Open: the ARRANGE key (OzAction.ArrangeHud, default F2) or OPTIONS, GAME tab, ARRANGE INTERFACE in the pause menu. While it is
    /// open the game is paused (the same pause the menu uses: the sim clock stops, Time.timeScale 0, the clock shows PAUSED) and the
    /// mode is modal (OpenZHud.Modal): PlayerInput is off, no HUD button answers, the minimap takes no click, so no click ever
    /// reaches the game. Opened from OPTIONS the menu's pause is HELD across the hand-over (RequestArrange): the game does not run
    /// for the 0.15 s between the menu and the mode, the audio plays no resume / pause pair, and the HUD is modal for that moment
    /// too (ArrangeHeld). Every movable group shows an amber outline with a grip tab that carries its name; a press inside an outline
    /// picks the group up, the drag moves it (clamped fully inside the screen), the release saves it. RESET LAYOUT puts every group
    /// back; DONE, ESC or the key again close the mode and the game goes on.
    ///
    /// Nothing about a panel's own code changes: each panel is still placed from its OzLayout rectangle, moved by its group's
    /// offset (OzHudLayout.At). Panels that hide and show together (the lower panels and the production window, MINIMAL UI, the
    /// entrance and exit slides, the pause menu hiding the whole HUD) keep doing so. A group that was never moved is placed
    /// exactly as before.
    ///
    /// The eighth group is the commander's transmission window (HQ TRANSMISSION): it is on a canvas of its own and only built while
    /// he speaks, so its frame is a placeholder of the window's size on screen; ZodLayout.InGame reads the saved place when the
    /// next transmission opens (OzHudLayout.CommsPlace). A transmission that is on screen while the mode is open stays where it is.
    ///
    /// Not movable: the production window (it opens beside its building, by its own placement rule), tooltips (they follow the
    /// pointer), the mission banner (2.9 s at the start), the mission briefing with its letterbox, the F1 controls help, the pause
    /// menu, the end screen with the commander's window on it, the testing overlay, and everything attached to the world (health
    /// bars, brackets, production badges, rally marks, cursors).
    /// Sounds: ui_pause / ui_resume and the announcer's line come from the pause itself (AudioDirector follows ZGame.paused);
    /// ui_drag_pick on a pick-up, ui_confirm on a drop (saved) and on RESET LAYOUT, ui_disabled when the mode cannot open.
    /// </summary>
    public partial class OpenZHud
    {
        /// <summary>The arrange mode is open (modal; the game is paused).</summary>
        public bool Arranging { get; private set; }
        public static bool ArrangingNow => I && I.Arranging;
        /// <summary>The pause is held for the mode that opens in a moment (asked from the pause menu's OPTIONS row): the HUD is modal
        /// for these 0.15 s too (OpenZHud.Modal), so nothing can be clicked and the pause menu cannot be opened in between.</summary>
        public bool ArrangeHeld => arrHeld;
        /// <summary>The pause menu asks this before it answers ESC: the mode took the key (also on the frame that closed it), or the
        /// mode is about to open.</summary>
        public bool ArrangeOwnsEsc => Arranging || arrHeld || Time.frameCount == arrEscFrame;
        /// <summary>Checks: groups dropped at a new place this session.</summary>
        public int ArrangeDrops { get; private set; }

        RectTransform arrRoot;
        readonly RectTransform[] arrFrame = new RectTransform[OzHudLayout.Count];
        readonly Image[] arrFill = new Image[OzHudLayout.Count];
        readonly Image[][] arrEdge = new Image[OzHudLayout.Count][];
        HudButton arrDone, arrReset;
        int arrDrag = -1, arrHover = -1, arrEscFrame = -1, arrFactoryFit = -1;
        Vector2 arrGrab, arrPress;
        bool arrDragMoved, arrWasPaused, arrHeld;
        float arrPrevScale = 1f, arrCw = -1f, arrCh = -1f, arrPendingAt = -1f;

        // ================================================================== build
        void BuildArrange()
        {
            arrRoot = Hud.Node("Arrange", Root); arrRoot.Fill();
            Hud.Img(arrRoot, "Dim", OzUi.A(OzUi.GM950, 0.2f)).rectTransform.Fill();
            for (int i = 0; i < OzHudLayout.Count; i++)
            {
                var box = OzHudLayout.Box((OzPanel)i, OzLayout.RefW);
                var f = Hud.Node("Frame_" + (OzPanel)i, arrRoot);
                f.anchorMin = f.anchorMax = new Vector2(0, 1); f.pivot = new Vector2(0, 1); f.sizeDelta = new Vector2(box.w, box.h);
                arrFill[i] = Hud.Img(f, "Fill", OzUi.A(OzUi.Amber, 0.07f)); arrFill[i].rectTransform.Fill();
                arrEdge[i] = Hud.Border(f, OzUi.Amber, 2f);
                var chip = Hud.Chip(f, "Grip", new OzRect(0, 0, 120, 18), "DRAG · " + OzHudLayout.Names[i], OzUi.Amber, OzUi.GM950, 9, 0.16f);
                chip.bg.transform.parent.GetComponent<RectTransform>().Place(new OzRect(0, 0, chip.text.preferredWidth + 18f, 18));
                arrFrame[i] = f;
            }
            // the three groups whose plates only show now and then say what will stand there
            ArrangeHint(OzPanel.Messages, "MESSAGES APPEAR HERE");
            ArrangeHint(OzPanel.Cannons, "CANNON READY\nPLATES APPEAR HERE");
            ArrangeHint(OzPanel.Comms, "TRANSMISSION WINDOW\nAPPEARS HERE");

            const float bw = 640f, bh = 112f, btn = 200f;
            var bar = Hud.Node("Bar", arrRoot).Place(new OzRect(OzAnchor.C, (OzLayout.RefW - bw) * 0.5f, (OzLayout.RefH - bh) * 0.5f, bw, bh));
            Hud.Plate(bar, "md", 0f, OzUi.Amber);
            Hud.Label(bar, "Title", OzHudLayout.Label, 16, OzUi.Ink, 0.16f).At(24, 18);
            Hud.Label(bar, "Sub", "DRAG A PANEL · IT STAYS INSIDE THE SCREEN · SAVED WHEN YOU DROP IT · GAME PAUSED", 11, OzUi.Muted, 0.1f, OzStyle.Plain).At(24, 46, OzH.L, false, bw - 48);
            arrReset = HudButton.Make(bar, "ArrangeReset", "RESET LAYOUT", new Vector2(btn, 34), HudButtonKind.Small, 12, 0.14f);
            arrReset.Rt.Place(new OzRect(24, 66, btn, 34)); arrReset.Priority = 100;
            arrReset.Tooltip = "RESET LAYOUT|Every panel goes back to its standard place.|";
            arrReset.OnClick = c => ResetLayout();
            arrDone = HudButton.Make(bar, "ArrangeDone", "DONE", new Vector2(btn, 34), HudButtonKind.Small, 12, 0.14f, HudButtonStyle.Primary);
            arrDone.Rt.Place(new OzRect(bw - 24 - btn, 66, btn, 34)); arrDone.Priority = 100;
            Hud.Keycap(arrDone.Content, "Esc", "ESC", btn - 10, (34 - 14) * 0.5f);
            arrDone.Tooltip = "DONE|Keep this layout and go on with the game.|ESC";
            arrDone.OnClick = c => SetArranging(false);
            arrRoot.gameObject.SetActive(false);
        }

        /// <summary>The label of a placeholder frame: right under its grip tab, as wide as the frame (the transmission frame changes
        /// its size with the UI scale). Under the tab, not in the middle: the two placeholder frames that overlap at their standard
        /// places (CANNON READY and HQ TRANSMISSION) then never write over each other's grip or label.</summary>
        void ArrangeHint(OzPanel p, string text)
        {
            var f = arrFrame[(int)p];
            var t = Hud.Label(f, "Hint", text, 11, OzUi.Ink2, 0.14f, OzStyle.Plain);
            t.Body(8, 26, 100);
            var r = t.rectTransform; r.anchorMin = new Vector2(0, 1); r.anchorMax = new Vector2(1, 1); r.pivot = new Vector2(0.5f, 1);
            r.offsetMin = new Vector2(8, -66); r.offsetMax = new Vector2(-8, -26);                          // 8 u inside the frame, 26 .. 66 u under its top edge
        }

        // ================================================================== open / close
        /// <summary>The pause menu's ARRANGE INTERFACE row calls this right after its own Close(): the menu closes first (its fade
        /// hands the HUD back), then the mode opens. Close() has just put the pause and the time scale back to what they were before
        /// the menu; they are taken again HERE, in the same frame, and held until the mode opens. So the game never runs in between
        /// and the audio, which follows ZGame.paused once a frame, sees no change: no ui_resume, no second ui_pause, no announcer
        /// pair. arrWasPaused / arrPrevScale keep the state from before the menu; the mode gives it back when it closes.</summary>
        public void RequestArrange()
        {
            if (arrRoot == null || Arranging) return;
            if (!arrHeld) { arrWasPaused = game.paused; arrPrevScale = Time.timeScale; arrHeld = true; }
            game.paused = true; Time.timeScale = 0f;
            arrPendingAt = OzMotion.Now + 0.15f;
        }

        /// <summary>The mode cannot open after all: the held pause ends, as the menu's Close() meant it to (ui_resume: AudioDirector).</summary>
        void ReleaseHold()
        {
            if (!arrHeld) return;
            arrHeld = false; arrPendingAt = -1f;
            game.paused = arrWasPaused; Time.timeScale = arrPrevScale > 0f ? arrPrevScale : 1f;
        }

        public void SetArranging(bool on)
        {
            if (on == Arranging) return;
            if (arrRoot == null || !Canvas) { ReleaseHold(); return; }
            if (on)
            {
                bool busy = (Pause && Pause.IsOpen) || (EndScreen && EndScreen.IsShown) || OzConfirm.Blocks || exited || !introDone || W == null || W.GameOver
                            || (Rally && Rally.Armed) || input.Placing != null;
                if (busy) { ReleaseHold(); OzSfx.Ui("ui_disabled"); return; }
                if (!hudVisible) SetVisible(true);
                if (Production) Production.Close();
                if (!arrHeld) { arrWasPaused = game.paused; arrPrevScale = Time.timeScale; }                 // opened by the key: the pause starts here (ui_pause + the announcer: AudioDirector follows it)
                arrHeld = false; arrPendingAt = -1f;                                                         // opened from OPTIONS: the pause was held since the menu closed (RequestArrange)
                game.paused = true; Time.timeScale = 0f;
                Arranging = true; arrDrag = -1; arrHover = -1;
                arrRoot.gameObject.SetActive(true); arrRoot.SetAsLastSibling();
                RefreshArrange();
            }
            else
            {
                if (arrDrag >= 0 && arrDragMoved) OzHudLayout.Save((OzPanel)arrDrag);
                arrDrag = -1; Arranging = false;
                arrRoot.gameObject.SetActive(false);
                game.paused = arrWasPaused; Time.timeScale = arrPrevScale > 0f ? arrPrevScale : 1f;      // ui_resume: AudioDirector
            }
            Debug.Log($"[OpenZ HUD] arrange interface {(on ? "OPEN (game paused)" : "closed")}: {OzHudLayout.Describe()}");
        }

        public void ResetLayout()
        {
            OzHudLayout.ResetAll();
            Relayout();
            OzSfx.Ui("ui_confirm");
            Debug.Log("[OpenZ HUD] RESET LAYOUT: every panel at its standard place, saved");
        }

        // ================================================================== placing
        /// <summary>Every movable panel to its place again: after a drag step, RESET LAYOUT, and when the canvas changed (resolution,
        /// UI scale) while a group stands at a place of the player's. The under-attack tab and the messages are placed every frame
        /// they show, from the same rectangles.</summary>
        public void Relayout()
        {
            if (!Canvas || !top) return;
            RePlace(top, OzHudLayout.At(OzPanel.Status, OzLayout.TopStrip));
            RePlace(terr, OzHudLayout.At(OzPanel.Status, OzLayout.TerrTab));
            if (menuBtn != null) RePlace(menuBtn.Rt, OzHudLayout.At(OzPanel.Menu, OzLayout.MenuButton));
            if (Minimap) RePlace(Minimap.Panel, OzHudLayout.At(OzPanel.Minimap, OzLayout.Minimap));
            RePlace(unitPanel, PanelRect());
            RePlace(cmdBar, OzHudLayout.At(OzPanel.Command, OzLayout.CommandBar));
            var cr = OzHudLayout.At(OzPanel.Cannons, OzLayout.StoredCannon);
            for (int j = 0; j < cannonPlates.Count; j++) cannonPlates[j].btn.Rt.Place(cr.Move(0, OzLayout.StoredCannonStep * j));
            if (factoryPanel)
            {
                int fit = FactoryMaxRows();
                PlaceFactoryPanel(Mathf.Clamp(factoryRows.Count, 1, fit)); FactoryPivot();
                if (fit != arrFactoryFit) { arrFactoryFit = fit; factoryBuilt = false; nextFactory = 0f; }   // other room: the rows are made again at the next refresh
            }
        }

        /// <summary>Place, and tell the entrance / exit slide where home is now (they slide from and to the new place).</summary>
        void RePlace(RectTransform rt, OzRect r)
        {
            if (!rt) return;
            rt.Place(r);
            foreach (var s in slides) if (s.rt == rt) s.home = rt.anchoredPosition;
        }

        // ---- the factory list rides with the command bar: 12 u above the bar's top-left corner, growing up; when the bar was moved
        // ---- and there is more room under it than above it, 12 u under it, growing down. Bar never moved = the numbers of OzLayout.
        bool BarMoved(out float left, out float topY, out float ch)
        {
            left = topY = ch = 0f;
            if (!OzHudLayout.Moved(OzPanel.Command)) return false;
            var r = OzHudLayout.BoxNow(OzPanel.Command); left = r.x; topY = r.y;
            OzHudLayout.CanvasSize(out _, out ch);
            return true;
        }

        static bool FactoryBelow(float topY, float ch) => ch - (topY + OzLayout.CommandBar.h) > topY;

        /// <summary>Canvas units the list may be tall.</summary>
        float FactoryRoom()
        {
            if (!BarMoved(out _, out float topY, out float ch)) return CanvasSize.y - (OzLayout.RefH - OzLayout.FactoryBottom) - OzLayout.FactoryMinY;
            return FactoryBelow(topY, ch) ? ch - (topY + OzLayout.CommandBar.h) - 12f - OzLayout.M : topY - 12f - Mathf.Min(OzLayout.FactoryMinY, topY * 0.25f);
        }

        /// <summary>Rows that fit (at least one).</summary>
        int FactoryMaxRows() => Mathf.Max(1, Mathf.FloorToInt((FactoryRoom() - 40f) / OzLayout.FactoryRowStep));

        /// <summary>The list's rectangle for a height h.</summary>
        OzRect FactoryRect(float h)
        {
            if (!BarMoved(out float left, out float topY, out float ch)) return new OzRect(OzAnchor.BL, OzLayout.FactoryX, OzLayout.FactoryBottom - h, OzLayout.FactoryW, h);
            float y = FactoryBelow(topY, ch) ? topY + OzLayout.CommandBar.h + 12f : topY - 12f - h;       // its top edge, from the canvas's top
            return new OzRect(OzAnchor.BL, left, OzLayout.RefH - (ch - y), OzLayout.FactoryW, h);
        }

        /// <summary>The list opens from the side it hangs on: above the bar it grows from its bottom-left corner (as always), under
        /// the bar from its top-left corner (the pivot Place gave it).</summary>
        void FactoryPivot()
        {
            if (!BarMoved(out _, out float topY, out float ch))
            {
                factoryPanel.pivot = new Vector2(0, 0);
                factoryPanel.anchoredPosition = new Vector2(OzLayout.FactoryX, OzLayout.RefH - OzLayout.FactoryBottom);
                return;
            }
            if (FactoryBelow(topY, ch)) return;
            var p = factoryPanel.anchoredPosition;
            factoryPanel.pivot = new Vector2(0, 0);
            factoryPanel.anchoredPosition = new Vector2(p.x, p.y - factoryPanel.sizeDelta.y);
        }

        /// <summary>Top edge of a list h tall, in canvas units above the canvas's bottom edge (the production window keeps clear of it).</summary>
        float FactoryTop(float h)
        {
            if (!BarMoved(out _, out float topY, out float ch)) return OzLayout.RefH - OzLayout.FactoryBottom + h;
            return FactoryBelow(topY, ch) ? ch - (topY + OzLayout.CommandBar.h + 12f) : ch - (topY - 12f - h);
        }

        // ================================================================== frame
        /// <summary>Every frame, after the HUD's own button pass (so DONE / RESET LAYOUT are known) and before the panels refresh.</summary>
        void TickArrange(Mouse m, Keyboard kb)
        {
            // the canvas changed (resolution, UI scale): a moved group keeps its fraction of the free travel
            OzHudLayout.CanvasSize(out float cw, out float ch);
            if (cw != arrCw || ch != arrCh)
            {
                bool first = arrCw < 0f; arrCw = cw; arrCh = ch;
                if (!first && OzHudLayout.AnyMoved) Relayout();
            }
            if (arrPendingAt >= 0f && OzMotion.Now >= arrPendingAt && !(Pause && Pause.IsOpen)) { arrPendingAt = -1f; SetArranging(true); }
            if (kb != null && OzBindings.Pressed(OzAction.ArrangeHud) && !kb.ctrlKey.isPressed && !kb.altKey.isPressed) SetArranging(!Arranging);
            if (!Arranging) return;
            if (!hudVisible || (kb != null && kb.escapeKey.wasPressedThisFrame)) { arrEscFrame = Time.frameCount; SetArranging(false); return; }

            var c = ScreenToCanvas(Pointer);                                                                // origin: the canvas's bottom-left corner
            var p = new Vector2(c.x, CanvasSize.y - c.y);                                                   // from its top-left corner, y down
            int hover = -1; float best = float.MaxValue;
            if (arrDrag < 0 && HotButton == null)
                for (int i = 0; i < OzHudLayout.Count; i++)
                {   // where groups overlap the smallest one wins, so a small group lying on a large one can always be picked up
                    var b = OzHudLayout.BoxNow((OzPanel)i);
                    if (b.Contains(p) && b.width * b.height < best) { best = b.width * b.height; hover = i; }
                }
            if (m.leftButton.wasPressedThisFrame && hover >= 0)
            {
                var b = OzHudLayout.BoxNow((OzPanel)hover);
                arrDrag = hover; arrDragMoved = false; arrPress = p; arrGrab = p - new Vector2(b.x, b.y);
                OzSfx.Ui("ui_drag_pick");
            }
            if (arrDrag >= 0)
            {
                if (m.leftButton.isPressed)
                {
                    if (!arrDragMoved && (p - arrPress).sqrMagnitude > 9f) arrDragMoved = true;           // a click without a drag changes nothing
                    if (arrDragMoved) { OzHudLayout.SetTopLeft((OzPanel)arrDrag, p - arrGrab); Relayout(); }
                }
                else
                {
                    if (arrDragMoved)
                    {
                        OzHudLayout.Save((OzPanel)arrDrag); ArrangeDrops++;
                        OzSfx.Ui("ui_confirm");
                        var f = OzHudLayout.Fraction((OzPanel)arrDrag);
                        Debug.Log($"[OpenZ HUD] arrange: {(OzPanel)arrDrag} dropped at {f.x:0.000};{f.y:0.000} of its free travel, saved ({OzHudLayout.PrefPrefix}{(OzPanel)arrDrag})");
                    }
                    arrDrag = -1;
                }
            }
            arrHover = arrDrag >= 0 ? arrDrag : hover;
            RefreshArrange();
        }

        void RefreshArrange()
        {
            for (int i = 0; i < OzHudLayout.Count; i++)
            {
                var b = OzHudLayout.BoxNow((OzPanel)i);
                var pos = new Vector2(b.x, -b.y); var size = new Vector2(b.width, b.height);
                if (arrFrame[i].anchoredPosition != pos) arrFrame[i].anchoredPosition = pos;
                if (arrFrame[i].sizeDelta != size) arrFrame[i].sizeDelta = size;                              // the transmission frame follows the UI scale
                bool drag = i == arrDrag, hot = i == arrHover;
                Color fill = OzUi.A(OzUi.Amber, drag ? 0.24f : hot ? 0.15f : 0.07f);
                if (arrFill[i].color != fill) arrFill[i].color = fill;
                Color edge = drag || hot ? (Color)OzUi.AmberHi : (Color)OzUi.Amber;
                foreach (var e in arrEdge[i]) if (e.color != edge) e.color = edge;
            }
        }

        // ================================================================== counted check on the live HUD
        /// <summary>Puts every group at 9 places (the 4 corners, the 4 edge middles, the centre), places the real panels and
        /// measures their rectangles on the screen: every one must lie inside it. 19 panel rectangles + the transmission window
        /// (only built while the commander speaks: the rectangle ZodLayout.InGame gives it on its own canvas) = 20 per place.
        /// The saved layout is put back afterwards. Returns one line: "... 180/180 rectangles inside ... PASS".</summary>
        public string ArrangeProof()
        {
            if (!Canvas || !top) return "[OpenZ HUD layout live check] no HUD";
            var keepF = new Vector2[OzHudLayout.Count]; var keepH = new bool[OzHudLayout.Count];
            for (int i = 0; i < OzHudLayout.Count; i++) { keepF[i] = OzHudLayout.Fraction((OzPanel)i); keepH[i] = OzHudLayout.Moved((OzPanel)i); }
            int n = 0, ok = 0; var bad = new System.Text.StringBuilder(); var corners = new Vector3[4];
            for (int ix = 0; ix <= 2; ix++)
                for (int iy = 0; iy <= 2; iy++)
                {
                    for (int i = 0; i < OzHudLayout.Count; i++) OzHudLayout.SetFraction((OzPanel)i, new Vector2(ix * 0.5f, iy * 0.5f));
                    Relayout();
                    // the plates that are only placed while they show: their two extreme places
                    var pr = PanelRect(); var ar = OzLayout.AlertTab;
                    alertTab.Place(new OzRect(pr.a, pr.x + ar.x, pr.y + ar.y, ar.w, ar.h));
                    fortToast.rt.Place(OzHudLayout.At(OzPanel.Messages, OzLayout.FortAlert.Move(0, -OzHudLayout.ToastRise)));
                    toasts[0].rt.Place(OzHudLayout.At(OzPanel.Messages, new OzRect(OzAnchor.TC, OzLayout.Toast.x,
                        OzLayout.Toast.y + OzLayout.FortAlert.h + 8f + (OzLayout.ToastMax - 1f) * OzLayout.ToastStep, OzLayout.Toast.w, OzLayout.Toast.h)));
                    var rects = new System.Collections.Generic.List<RectTransform> { top, terr, Minimap.Panel, unitPanel, exitBtn.Rt, alertTab, cmdBar, factoryPanel, fortToast.rt, toasts[0].rt, menuBtn.Rt };
                    foreach (var cp in cannonPlates) rects.Add(cp.btn.Rt);
                    foreach (var rt in rects)
                    {
                        rt.GetWorldCorners(corners); n++;
                        bool inside = corners[0].x >= -1f && corners[0].y >= -1f && corners[2].x <= Screen.width + 1f && corners[2].y <= Screen.height + 1f;
                        if (inside) ok++;
                        else if (bad.Length < 600) bad.Append($" | {rt.name} at {ix * 0.5f:0.0};{iy * 0.5f:0.0}: {corners[0].x:0},{corners[0].y:0} .. {corners[2].x:0},{corners[2].y:0}");
                    }
                    // the transmission window: where ZodLayout.InGame opens it now, in screen pixels (its canvas is RefH units tall)
                    var zl = ZodLayout.InGame(); float kz = Screen.height / OzLayout.RefH;
                    float zx0 = zl.x * kz, zx1 = (zl.x + zl.W * zl.scale) * kz, zy1 = Screen.height - zl.y * kz, zy0 = Screen.height - (zl.y + zl.H * zl.scale) * kz;
                    n++;
                    if (zx0 >= -1f && zy0 >= -1f && zx1 <= Screen.width + 1f && zy1 <= Screen.height + 1f) ok++;
                    else if (bad.Length < 600) bad.Append($" | transmission window at {ix * 0.5f:0.0};{iy * 0.5f:0.0}: {zx0:0},{zy0:0} .. {zx1:0},{zy1:0}");
                }
            for (int i = 0; i < OzHudLayout.Count; i++) { if (keepH[i]) OzHudLayout.SetFraction((OzPanel)i, keepF[i]); else OzHudLayout.Clear((OzPanel)i); }
            Relayout();
            return $"[OpenZ HUD layout live check] screen {Screen.width}x{Screen.height}, UI scale {OzText.UiScale:0.00}: {ok}/{n} panel rectangles inside the screen "
                   + $"({OzHudLayout.Count} groups x 9 places); layout now: {OzHudLayout.Describe()}. {(ok == n ? "PASS" : "FAIL" + bad)}";
        }
    }
}
