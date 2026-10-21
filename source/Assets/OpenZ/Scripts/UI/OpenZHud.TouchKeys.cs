using OpenZ.Sim;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OpenZ
{
    /// <summary>
    /// The on-screen keys for touch and gamepad play (OzTouch.cs): nine keys for what otherwise needs the keyboard or the wheel.
    ///
    ///   BACK      UI        HELP
    ///   EXIT      REPAIR    ZOOM +
    ///   SPEED -   SPEED +   ZOOM -
    ///
    /// BACK is ESC (it closes the help card first). UI is the MINIMAL UI key. HELP shows the gestures and the gamepad buttons.
    /// EXIT is EXIT CREW, REPAIR sends the damaged vehicles of the selection to repair and back, ZOOM is one wheel notch, SPEED one
    /// step of the game speed. Each calls what its key calls, with that key's own rules and sounds.
    ///
    /// The block only exists while a finger or a gamepad drives the pointer (OzTouch.Driving) and the HUD is in. It is built from
    /// the HUD's own buttons (tap = click on release, hold = tooltip) and is a blocker like every panel, so a tap between two keys
    /// never reaches the map. MINIMAL UI keeps it (it carries the key that brings the panels back).
    ///
    /// Place: the block looks for room among the eight HUD groups where they stand NOW (OzHudLayout.BoxNow, so a layout the
    /// player arranged is respected): down the right edge from the top, then down the left edge, TouchClear units clear of every
    /// group. When nothing is free that way (very narrow canvases at a large interface scale) it only keeps clear of the panels
    /// that are always on screen and may stand on the places of the plates that come and go (messages, CANNON READY, the
    /// transmission window). It looks again when the canvas or the layout changes. At the standard layout that is the right
    /// edge, right under the MENU button.
    ///
    /// While the pointer is driven by a finger or a gamepad a right click on a group key stores the selection (two-finger tap, the
    /// gamepad's order button): the mouse has CTRL + click for that and keeps exactly its own behaviour.
    /// </summary>
    public partial class OpenZHud
    {
        public const int TouchCols = 3, TouchRows = 3;
        /// <summary>One key, the gaps between keys, the room kept to every HUD group (canvas units).</summary>
        public const float TouchKeyW = 88f, TouchKeyH = 54f, TouchGapX = 8f, TouchGapY = 6f, TouchClear = 8f;
        public const float TouchBlockW = TouchCols * TouchKeyW + (TouchCols - 1) * TouchGapX, TouchBlockH = TouchRows * TouchKeyH + (TouchRows - 1) * TouchGapY;

        RectTransform touchKeys, touchHelp;
        readonly HudButton[] touchBtn = new HudButton[TouchCols * TouchRows];
        float touchCw = -1f, touchCh = -1f; int touchLayout = -1; bool touchClear, touchHelpOn, touchStore;

        /// <summary>The block (null before the HUD is built); it is active only while it shows.</summary>
        public RectTransform TouchKeysRect => touchKeys;
        public bool TouchKeysShown => touchKeys && touchKeys.gameObject.activeSelf;
        /// <summary>The block found a place clear of all eight groups (false: only clear of the always-on panels).</summary>
        public bool TouchKeysClear => touchClear;
        public bool TouchHelpShown => touchHelpOn;
        /// <summary>Checks: the key in a cell of the block (0 .. 8, row by row).</summary>
        public HudButton TouchKey(int i) => i >= 0 && i < touchBtn.Length ? touchBtn[i] : null;

        // ================================================================== build
        void BuildTouchKeys()
        {
            touchKeys = Hud.Node("TouchKeys", Root).Place(new OzRect(OzAnchor.TL, 0, 0, TouchBlockW, TouchBlockH));
            AddBlocker(touchKeys);
            MakeTouchKey(0, "BACK", "BACK|Closes what is open and cancels a placement or the rally tool. With nothing open: the pause menu.|ESC", c => { if (!TouchHelpClose()) OzTouch.TapKey(Key.Escape); });
            MakeTouchKey(1, "UI", "MINIMAL UI|Hides or shows the permanent panels.|" + OzBindings.Cap(OzAction.MinimalUi), c => { if (!MinimalUiKey()) OzSfx.Ui("ui_disabled"); });
            MakeTouchKey(2, "HELP", "HELP|The touch gestures and the gamepad buttons.|", c => ShowTouchHelp(!touchHelpOn));
            MakeTouchKey(3, "EXIT", "EXIT CREW|The crew of the selected gun or carrier gets out.|" + OzBindings.Cap(OzAction.ExitCrew), c => ExitSelected());
            MakeTouchKey(4, "REPAIR", "REPAIR|The damaged vehicles of the selection drive to a repair station and come back.|" + OzBindings.Cap(OzAction.RepairVehicles), c => { if (!input.SendToRepair()) AudioDirector.UI("ui_order_invalid"); });
            MakeTouchKey(5, "ZOOM +", "ZOOM IN|One step closer. Two fingers: pinch. Gamepad: the right trigger.|", c => { if (rts) rts.ZoomBy(1f - Mathf.Clamp(rts.wheelStep, 0.01f, 0.9f)); });
            MakeTouchKey(6, "SPEED -", "GAME SPEED|One step slower.|-", c => { if (!OzSpeedKeys.ProofStep(-1)) OzSfx.Ui("ui_disabled"); });
            MakeTouchKey(7, "SPEED +", "GAME SPEED|One step faster.|+", c => { if (!OzSpeedKeys.ProofStep(1)) OzSfx.Ui("ui_disabled"); });
            MakeTouchKey(8, "ZOOM -", "ZOOM OUT|One step back. Two fingers: pinch. Gamepad: the left trigger.|", c => { if (rts) rts.ZoomBy(1f / (1f - Mathf.Clamp(rts.wheelStep, 0.01f, 0.9f))); });
            touchKeys.gameObject.SetActive(false);
            BuildTouchHelp();
        }

        void MakeTouchKey(int i, string label, string tip, System.Action<HudClick> click)
        {
            var b = HudButton.Make(touchKeys, "Touch" + i, label, new Vector2(TouchKeyW, TouchKeyH), HudButtonKind.Large, 13, 0.1f);
            b.Rt.Place(new OzRect((i % TouchCols) * (TouchKeyW + TouchGapX), (i / TouchCols) * (TouchKeyH + TouchGapY), TouchKeyW, TouchKeyH));
            b.Tooltip = tip; b.OnClick = click;
            touchBtn[i] = b;
        }

        // ================================================================== the help card
        static readonly (string key, string text)[] TouchRowsText =
        {
            ("TAP", "Select · press · with units: the order"),
            ("DOUBLE TAP", "All units of that type on screen"),
            ("DRAG", "Selection box"),
            ("HOLD", "Information, no click"),
            ("2 FINGERS · DRAG", "Scroll the map"),
            ("2 FINGERS · PINCH", "Zoom"),
            ("2-FINGER TAP", "Right click: cancel, rally point"),
            ("MINIMAP", "Tap or drag: move the view"),
            ("GROUP KEY", "Tap: recall · 2-finger tap: store"),
        };
        static readonly (string key, string text)[] PadRowsText =
        {
            ("LEFT STICK", "Pointer"),
            ("A · CROSS", "Select, press, hold for a box"),
            ("X · SQUARE", "Order · right click"),
            ("B · CIRCLE", "Back · clear the selection"),
            ("Y · TRIANGLE", "Repair · next robot squad"),
            ("RIGHT STICK", "Scroll the map"),
            ("LT · RT", "Zoom out · zoom in"),
            ("LB · RB", "Hold: CTRL · SHIFT"),
            ("D-PAD", "Groups 1 to 4 · with LB: store"),
            ("START · SELECT", "Pause menu · minimal UI"),
            ("L3 · R3", "Exit crew · last message"),
        };

        void BuildTouchHelp()
        {
            bool pad = OzTouch.PadCompiled;
            const float colW = 410f, keyW = 148f, rowH = 26f, top = 86f, padX = 28f;
            int rows = pad ? Mathf.Max(TouchRowsText.Length, PadRowsText.Length) : TouchRowsText.Length;
            float w = padX * 2f + (pad ? colW * 2f + 24f : colW), h = top + rows * rowH + 74f;
            touchHelp = Hud.Node("TouchHelp", Root).Place(new OzRect(OzAnchor.C, (OzLayout.RefW - w) * 0.5f, (OzLayout.RefH - h) * 0.5f, w, h));
            AddBlocker(touchHelp);
            Hud.Plate(touchHelp, "lg", 0f, OzUi.Amber);
            Hud.Label(touchHelp, "Title", pad ? "TOUCH AND GAMEPAD" : "TOUCH CONTROLS", 16, OzUi.Ink, 0.16f).At(padX, 20);
            Hud.Label(touchHelp, "Sub", "OPTIONS · ADVANCED · " + OzTouch.Label + " SWITCHES THEM", 10, OzUi.Muted, 0.2f, OzStyle.Plain).At(padX, 44, OzH.L, false, w - 2f * padX);   // under the title: the card is narrow without the gamepad column
            HelpColumn("Touch", "TOUCH SCREEN", TouchRowsText, padX, colW, keyW, rowH, top);
            if (pad) HelpColumn("Pad", "GAMEPAD", PadRowsText, padX + colW + 24f, colW, keyW, rowH, top);
            var close = HudButton.Make(touchHelp, "TouchHelpClose", "CLOSE", new Vector2(180f, 44f), HudButtonKind.Small, 13, 0.14f, HudButtonStyle.Primary);
            close.Rt.Place(new OzRect(w - padX - 180f, h - 22f - 44f, 180f, 44f));
            close.OnClick = c => ShowTouchHelp(false); close.Priority = 20;                                 // also over an open build menu
            touchHelp.gameObject.SetActive(false);
        }

        void HelpColumn(string id, string head, (string key, string text)[] rows, float x, float colW, float keyW, float rowH, float top)
        {
            Hud.Label(touchHelp, id + "Head", head, 11, OzUi.Amber, 0.22f, OzStyle.Plain).At(x, top - 28f);
            Hud.Img(touchHelp, id + "Rule", OzUi.A(OzUi.Khaki, 0.18f)).rectTransform.Place(new OzRect(x, top - 10f, colW, 1f));
            for (int i = 0; i < rows.Length; i++)
            {
                float y = top + i * rowH;
                Hud.Label(touchHelp, id + "Key" + i, rows[i].key, 11, OzUi.Ink, 0.12f, OzStyle.Plain).At(x, y + rowH * 0.5f, OzH.L, true, keyW);
                Hud.Label(touchHelp, id + "Text" + i, rows[i].text, 12, OzUi.Ink2, 0.02f, OzStyle.Plain).At(x + keyW + 10f, y + rowH * 0.5f, OzH.L, true, colW - keyW - 10f);
            }
        }

        public void ShowTouchHelp(bool on)
        {
            if (!touchHelp || on == touchHelpOn) return;
            touchHelpOn = on;
            touchHelp.gameObject.SetActive(on);
            if (on) { touchHelp.SetAsLastSibling(); if (PlayerPrefs.GetInt(OzTouch.HelpPref, 0) == 0) { PlayerPrefs.SetInt(OzTouch.HelpPref, 1); PlayerPrefs.Save(); } }
        }

        /// <summary>BACK: the help card closes first. True = it was open.</summary>
        public bool TouchHelpClose() { if (!touchHelpOn) return false; ShowTouchHelp(false); OzSfx.Ui("ui_cancel"); return true; }

        // ================================================================== frame
        /// <summary>Every frame from OpenZHud.Update, after the HUD's own button pass.</summary>
        void TickTouchKeys()
        {
            if (!touchKeys) return;
            bool driving = OzTouch.Driving;
            if (driving != touchStore)
            {   // a right click on a group key stores the selection only while a finger or a gamepad drives the pointer
                touchStore = driving;
                for (int k = 0; k < groupBtn.Length; k++)
                {
                    int d = k; var b = groupBtn[d]; if (b == null) continue;
                    if (driving) b.OnRightClick = c => GroupClick(d, true); else b.OnRightClick = null;
                }
            }
            bool show = driving && introDone && !exited;
            if (touchKeys.gameObject.activeSelf != show) { touchKeys.gameObject.SetActive(show); touchCw = -1f; }
            if (!show) { if (touchHelpOn) ShowTouchHelp(false); return; }

            OzHudLayout.CanvasSize(out float cw, out float ch);
            if (cw != touchCw || ch != touchCh || touchLayout != OzHudLayout.Version)
            {
                touchCw = cw; touchCh = ch; touchLayout = OzHudLayout.Version;
                PlaceTouchKeys(cw, ch);
            }

            bool canExit = false, canRepair = false;
            foreach (var o in input.Selection)
            {
                if (o == null || o.Destroyed || o.Owner != Player) continue;
                if (!canExit && CanExit(o)) canExit = true;
                if (!canRepair && o.Kind == ZKind.Vehicle && o.CanBeRepaired()) canRepair = true;
            }
            SetTouchKey(3, canExit); SetTouchKey(4, canRepair);

            if (!touchHelpOn && !Modal && PlayerPrefs.GetInt(OzTouch.HelpPref, 0) == 0) ShowTouchHelp(true);   // the first time ever: the gestures, once
            if (touchHelpOn && Modal) ShowTouchHelp(false);
        }

        void SetTouchKey(int i, bool on) { var b = touchBtn[i]; if (b != null && b.Interactable != on) { b.Interactable = on; b.Refresh(); } }

        /// <summary>Find the block's place on a canvas cw x ch (see the class text) and put it there.</summary>
        void PlaceTouchKeys(float cw, float ch)
        {
            touchClear = FindTouchPlace(cw, ch, out var at);
            touchKeys.Place(new OzRect(OzAnchor.TL, at.x, at.y, TouchBlockW, TouchBlockH));
            Debug.Log($"[OpenZ HUD] touch keys at {at.x:0},{at.y:0} of the {cw:0} x {ch:0} u canvas ({(touchClear ? "clear of all 8 groups" : "clear of the always-on panels only")}; layout: {OzHudLayout.Describe()})");
        }

        static readonly Rect[] touchBoxes = new Rect[OzHudLayout.Count];

        /// <summary>The block's top-left corner (from the canvas's top-left corner, y down). True = clear of all eight groups.</summary>
        public static bool FindTouchPlace(float cw, float ch, out Vector2 at)
        {
            float m = OzLayout.M;
            for (int i = 0; i < touchBoxes.Length; i++) touchBoxes[i] = OzHudLayout.BoxNow((OzPanel)i);
            for (int pass = 0; pass < 2; pass++)
                for (int side = 0; side < 2; side++)
                {
                    float x = side == 0 ? cw - m - TouchBlockW : m;
                    for (float y = m; y + TouchBlockH <= ch - m + 0.01f; y += 2f)
                        if (TouchPlaceFree(new Rect(x, y, TouchBlockW, TouchBlockH), pass == 0)) { at = new Vector2(x, y); return pass == 0; }
                }
            at = new Vector2(Mathf.Max(m, cw - m - TouchBlockW), Mathf.Max(m, (ch - TouchBlockH) * 0.5f));
            return false;
        }

        static bool TouchPlaceFree(Rect r, bool all)
        {
            var grown = new Rect(r.x - TouchClear, r.y - TouchClear, r.width + 2f * TouchClear, r.height + 2f * TouchClear);
            for (int i = 0; i < touchBoxes.Length; i++)
            {
                var p = (OzPanel)i;
                if (!all && (p == OzPanel.Messages || p == OzPanel.Cannons || p == OzPanel.Comms)) continue;
                if (touchBoxes[i].Overlaps(grown)) return false;
            }
            return true;
        }
    }
}
