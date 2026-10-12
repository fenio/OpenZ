using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace OpenZ
{
    /// <summary>
    /// The old table put every key into Hud.Keycap, whose width came from TextMeshPro's preferredWidth measured inside a 14 u
    /// box (it wraps there), so every cap stayed 14 u wide and long keys ("LEFT CLICK", "CTRL+R V C A") ran over the text.
    /// Here every key is its own measured cap ([CTRL] + [R]), right-aligned in a key column, the action in its own column,
    /// rows on a subtle band, sections in amber.
    ///  - Full (main menu, 1120 x 720 u, two columns): every action; the caps of OzBindings actions are buttons: click, press
    ///    the new key (Esc cancels, a click elsewhere cancels), saved at once; a key used twice in the same context turns both
    ///    caps red and the footer names the pair; RESET TO DEFAULTS. Sounds: ui_hover on a cap, ui_click to start, ui_confirm
    ///    bound, ui_error bound with a conflict, ui_disabled reserved key, ui_cancel cancelled, ui_confirm reset (OzSoundLog AD rows).
    ///  - Compact (pause menu, 460 x 600 u): the same table read-only, 20 rows, every action, the live bindings, "change keys in the main menu".
    /// </summary>
    public sealed class OzControlsTable
    {
        // ------------------------------------------------------------------ metrics (canvas units)
        /// <summary>FullH: the longer column (17 rows of 32 u + 2 section heads) ends at 696 u; the foot rule sits at FullH - 76 and the
        /// RESET TO DEFAULTS button at FullH - 60, so the panel must be at least 696 + 12 + 76 = 784 u tall (720 u let the last
        /// two rows run over the rule, the button and the status line). BuildFull warns in the log if a column outgrows it.</summary>
        public const float FullW = 1120, FullH = 784;
        const float Pad = 32, ColGap = 40, HeadY = 22, RowsY = 84, RowStep = 32, RowH = 30, SecStep = 32, CapH = 24, CapText = 12, CapPad = 10, CapGap = 5, LabelSize = 14;
        const float CRowsY = 62, CRowStep = 30, CCapH = 18, CCapText = 10, CCapPad = 7, CKeyRight = 222, CLabelX = 232, CLabelSize = 11;

        sealed class Part { public string text; public bool isAction, sep; public OzAction action; public RectTransform rt; public Image edgeImg; public Image[] edgeLines; public OzText label; public float w; public bool hover; }
        sealed class Row { public string label, section; public Part[] parts; }

        static Part K(string t) => new Part { text = t };
        static Part A(OzAction a) => new Part { isAction = true, action = a };
        static Part S(string t) => new Part { text = t, sep = true };
        static Row R(string label, params Part[] parts) => new Row { label = label, parts = parts };
        static Row H(string section) => new Row { section = section };

        static Row[] FullLeft() => new[]
        {
            H("SELECT"),
            R("Select a unit · drag: box select", K("LEFT CLICK")),
            R("Add to / remove from the selection", K("SHIFT"), S("+"), K("CLICK")),
            R("All units of that type on screen", K("DOUBLE-CLICK")),
            R(OzBindings.DefOf(OzAction.NextRobot).label, A(OzAction.NextRobot)),
            R(OzBindings.DefOf(OzAction.NextVehicle).label, A(OzAction.NextVehicle)),
            R(OzBindings.DefOf(OzAction.NextGun).label, A(OzAction.NextGun)),
            R(OzBindings.DefOf(OzAction.AllRobots).label, K("CTRL"), S("+"), A(OzAction.AllRobots)),
            R(OzBindings.DefOf(OzAction.AllVehicles).label, K("CTRL"), S("+"), A(OzAction.AllVehicles)),
            R(OzBindings.DefOf(OzAction.AllCannons).label, K("CTRL"), S("+"), A(OzAction.AllCannons)),
            R(OzBindings.DefOf(OzAction.AllUnits).label, K("CTRL"), S("+"), A(OzAction.AllUnits)),
            H("ORDERS"),
            R("Move, attack, capture, enter, repair", K("RIGHT CLICK")),
            R("Queue waypoints (release Shift)", K("SHIFT"), S("+"), K("RIGHT CLICK")),
            R("Attack-move / plain move", K("CTRL"), S("/"), K("ALT"), S("+"), K("RIGHT CLICK")),
            R("Attack an empty vehicle, gun or building", K("CTRL"), S("+"), K("RIGHT CLICK")),                         // a plain right-click never attacks them
            R("Only the unit nearest to the point", A(OzAction.NearestOnly), S("+"), K("RIGHT CLICK")),
            R("Damaged vehicles: to repair and back", A(OzAction.RepairVehicles)),
            R("Patrol (then right-click) · stop · hold", A(OzAction.Patrol), S("·"), A(OzAction.StopUnits), S("·"), A(OzAction.HoldPosition)),
        };

        static Row[] FullRight() => new[]
        {
            H("GROUPS"),
            R("Recall a group (again: fly there)", K("1 .. 0")),
            R("Store the selection as a group", K("CTRL"), S("+"), K("1 .. 0")),
            H("CAMERA"),
            R(OzBindings.DefOf(OzAction.ScrollUp).label + " · or the arrow keys", A(OzAction.ScrollUp)),
            R(OzBindings.DefOf(OzAction.ScrollDown).label, A(OzAction.ScrollDown)),
            R(OzBindings.DefOf(OzAction.ScrollLeft).label, A(OzAction.ScrollLeft)),
            R(OzBindings.DefOf(OzAction.ScrollRight).label, A(OzAction.ScrollRight)),
            R("Hold SHIFT to scroll faster", K("SHIFT")),
            R("Zoom · middle drag flies the view", K("WHEEL"), S("·"), K("MIDDLE DRAG")),
            H("INTERFACE"),
            R(OzBindings.DefOf(OzAction.FactoryList).label, A(OzAction.FactoryList)),
            R(OzBindings.DefOf(OzAction.PlaceCannon).label, A(OzAction.PlaceCannon)),
            R(OzBindings.DefOf(OzAction.ExitCrew).label, A(OzAction.ExitCrew)),
            R(OzBindings.DefOf(OzAction.LastMessage).label, A(OzAction.LastMessage)),
            R("Game speed: slower / faster", K("-"), S("/"), K("+")),                                                // fixed keys, the numpad pair too (OzSpeedKeys)
            R(OzBindings.DefOf(OzAction.HideHud).label, A(OzAction.HideHud)),
            R("Controls help · pause menu", A(OzAction.HelpOverlay), S("·"), K("ESC")),
        };

        static Row[] Compact() => new[]
        {
            R("Select a unit · drag: box select", K("L-CLICK")),
            R("Add to / remove from the selection", K("SHIFT"), S("+"), K("L-CLICK")),
            R("All units of that type on screen", K("DOUBLE-CLICK")),
            R("Order: move, attack, capture, enter", K("R-CLICK")),
            R("Queue waypoints (release Shift)", K("SHIFT"), S("+"), K("R-CLICK")),
            R("Attack-move / plain move", K("CTRL"), S("/"), K("ALT"), S("+"), K("R-CLICK")),
            R("Attack an empty vehicle, gun, building", K("CTRL"), S("+"), K("R-CLICK")),
            R("Only the unit nearest to the point", A(OzAction.NearestOnly), S("+"), K("R-CLICK")),
            R("Damaged vehicles: to repair and back", A(OzAction.RepairVehicles)),
            R("Patrol (then R-click) · stop · hold", A(OzAction.Patrol), S("·"), A(OzAction.StopUnits), S("·"), A(OzAction.HoldPosition)),   // CTL1
            R("Recall a group (again: fly there)", K("1 .. 0")),
            R("Store the selection as a group", K("CTRL"), S("+"), K("1 .. 0")),
            R("Next robot / vehicle / gun", A(OzAction.NextRobot), A(OzAction.NextVehicle), A(OzAction.NextGun)),
            R("All robots / vehicles / cannons / units", K("CTRL"), S("+"), A(OzAction.AllRobots), A(OzAction.AllVehicles), A(OzAction.AllCannons), A(OzAction.AllUnits)),
            R("Scroll (arrows too) · wheel zooms", A(OzAction.ScrollUp), A(OzAction.ScrollLeft), A(OzAction.ScrollDown), A(OzAction.ScrollRight)),
            R("Hold SHIFT to scroll faster", K("SHIFT")),
            R("Factory list · place a stored cannon", A(OzAction.FactoryList), S("·"), A(OzAction.PlaceCannon)),
            R("Exit crew · cycle the last 5 messages", A(OzAction.ExitCrew), S("·"), A(OzAction.LastMessage)),
            R("Game speed: slower / faster", K("-"), S("/"), K("+")),
            R("Hide the HUD · help · this menu", A(OzAction.HideHud), S("·"), A(OzAction.HelpOverlay), S("·"), K("ESC")),
        };

        // ------------------------------------------------------------------ state
        readonly List<Part> actionParts = new List<Part>();
        readonly bool full;
        readonly Action<string> sfx;
        OzText status; float statusUntil; string statusMsg; Color statusColor;
        Part capture; int shownVersion = -1;
        static OzControlsTable capturingTable; static int lastTickFrame = -10;

        /// <summary>True while a cap waits for its new key (the main menu ignores its own keys then: Esc must not close the panel).</summary>
        public static bool Capturing => capturingTable != null && capturingTable.capture != null && Time.frameCount - lastTickFrame <= 1;

        OzControlsTable(bool full, Action<string> sfx) { this.full = full; this.sfx = sfx; }

        /// <summary>Main-menu table with rebinding. addHot(rt, click, hover) registers a mouse target in the menu's own input
        /// (Side.Controls); reset = the RESET TO DEFAULTS button rect is made by the caller (it owns HudButton hots).</summary>
        public static OzControlsTable BuildFull(RectTransform p, Action<RectTransform, Action, Action<bool>> addHot, Action<string> sfx)
        {
            var t = new OzControlsTable(true, sfx);
            Hud.Label(p, "Title", "CONTROLS", 20, OzUi.Ink, 0.16f).At(Pad, HeadY);
            Hud.Label(p, "Sub", "ZOD KEY MAP + OPENZ CAMERA · CLICK A KEY TO CHANGE IT · SAVED AT ONCE", 10, OzUi.Muted, 0.2f, OzStyle.Plain).At(FullW - Pad, HeadY + 6, OzH.R, false, 640);
            var rule = Hud.Img(p, "HeadRule", OzUi.A(OzUi.Khaki, 0.18f)); rule.rectTransform.TL(Pad, 64, FullW - 2 * Pad, 1);
            float colW = (FullW - 2 * Pad - ColGap) * 0.5f;
            float end = Mathf.Max(t.Column(p, FullLeft(), Pad, colW, addHot), t.Column(p, FullRight(), Pad + colW + ColGap, colW, addHot));
            if (end > FullH - 76 - 8) Debug.LogWarning($"[OpenZ Menu] CONTROLS table: the rows end at {end:0} u, over the foot rule at {FullH - 76:0} u: raise OzControlsTable.FullH");
            var rule2 = Hud.Img(p, "FootRule", OzUi.A(OzUi.Khaki, 0.18f)); rule2.rectTransform.TL(Pad, FullH - 76, FullW - 2 * Pad, 1);
            t.status = Hud.Label(p, "Status", "", 11, OzUi.Muted, 0.14f, OzStyle.Plain).At(Pad + 250, FullH - 42, OzH.L, true, FullW - 2 * Pad - 250);
            t.Refresh(true);
            return t;
        }

        public const string CompactFoot = "CHANGE KEYS IN THE MAIN MENU · CONTROLS";
        /// <summary>Pause-menu table, read-only (the live keys; rebinding lives in the main menu). foot = the footer line (the in-game
        /// help passes a shorter one: its own close hint stands on the same line).</summary>
        public static OzControlsTable BuildCompact(RectTransform p, float w, float h, string foot = CompactFoot)
        {
            var t = new OzControlsTable(false, null);
            Hud.Label(p, "Title", "CONTROLS", 16, OzUi.Ink, 0.16f).At(OzLayout.OptX, 15);
            Hud.Label(p, "Sub", "ZOD KEY MAP + OPENZ CAMERA", 9, OzUi.Muted, 0.2f, OzStyle.Plain).At(w - OzLayout.OptX, 18, OzH.R, false, 240);
            var rows = Compact();
            float step = Mathf.Min(CRowStep, (h - 38f - CRowsY) / Mathf.Max(1, rows.Length));
            for (int i = 0; i < rows.Length; i++)
            {
                float y = CRowsY + i * step;
                if ((i & 1) == 0) { var band = Hud.Img(p, "Band" + i, OzUi.A(OzUi.GM800, 0.42f)); band.rectTransform.TL(OzLayout.OptX - 8, y - 4, w - 2 * OzLayout.OptX + 16, step - 2); }
                t.Caps(p, rows[i].parts, CKeyRight, y + (step - 2 - 8 - CCapH) * 0.5f, CCapH, CCapText, CCapPad, null);
                Hud.Label(p, "What" + i, rows[i].label, CLabelSize, OzUi.Ink2, 0f, OzStyle.Plain).At(CLabelX, y + (step - 10) * 0.5f, OzH.L, true, w - CLabelX - OzLayout.OptX + 4);
            }
            Hud.Label(p, "Foot", foot, 9, OzUi.Muted, 0.2f, OzStyle.Plain).At(OzLayout.OptX, h - 30, OzH.L, false, w - 2 * OzLayout.OptX);
            t.Refresh(true);
            return t;
        }

        /// <summary>Draws one column; returns the bottom edge of its last row (canvas units).</summary>
        float Column(RectTransform p, Row[] rows, float x, float w, Action<RectTransform, Action, Action<bool>> addHot)
        {
            float y = RowsY; int band = 0;
            foreach (var r in rows)
            {
                if (r.section != null)
                {
                    if (y > RowsY) y += 6;
                    Hud.Label(p, "Sec_" + r.section, r.section, 11, OzUi.Amber, 0.26f, OzStyle.Plain).At(x, y + 8);
                    y += SecStep; band = 0;
                    continue;
                }
                if ((band++ & 1) == 0) { var b = Hud.Img(p, "Band", OzUi.A(OzUi.GM800, 0.45f)); b.rectTransform.TL(x - 10, y, w + 20, RowH); }
                float capsW = Caps(p, r.parts, x + w, y + (RowH - CapH) * 0.5f, CapH, CapText, CapPad, addHot);
                Hud.Label(p, "What", r.label, LabelSize, OzUi.Ink2, 0f, OzStyle.Plain).At(x, y + RowH * 0.5f, OzH.L, true, Mathf.Max(120f, w - capsW - 24f));
                y += RowStep;
            }
            return y - RowStep + RowH;
        }

        /// <summary>Draws the caps of one row right-aligned at xRight; returns their total width.</summary>
        float Caps(RectTransform p, Part[] parts, float xRight, float y, float h, float size, float pad, Action<RectTransform, Action, Action<bool>> addHot)
        {
            float x = xRight, total = 0f;
            for (int i = parts.Length - 1; i >= 0; i--)
            {
                var part = parts[i];
                if (part.sep)
                {
                    var s = Hud.Label(p, "Sep", part.text, size + 1, OzUi.Muted, 0f, OzStyle.Plain);
                    float sw = Mathf.Max(8f, TextWidth(s));
                    s.At(x - CapGap - sw, y + h * 0.5f, OzH.L, true, sw + 2);
                    x -= sw + 2 * CapGap; total += sw + 2 * CapGap;
                    continue;
                }
                var root = Hud.Node(part.isAction ? "Key_" + part.action : "Key", p);
                var bg = Hud.Kit(root, "Bg", "Panels/keycap", new OzRect(0, 0, 14, h)); bg.rectTransform.Fill();
                if (OzKit.GetSprite("Panels/keycap") == null) bg.color = OzUi.GM950;
                string text = part.isAction ? OzBindings.Cap(part.action) : part.text;
                var t = Hud.Label(root, "Key", text, size, part.isAction ? OzUi.Ink : OzUi.Ink2, 0.06f, OzStyle.Plain);
                float w = Mathf.Max(h + 6f, TextWidth(t) + 2 * pad);
                if (part.isAction) w = Mathf.Max(w, full ? 44f : 26f);                                             // one-letter keys still look like buttons
                t.FillParent(OzH.C);
                root.TL(x - w, y, w, h);
                part.rt = root; part.label = t; part.w = w;
                if (part.isAction)
                {
                    if (OzKit.GetSprite("Panels/chip_3_outline") != null) { part.edgeImg = Hud.Kit(root, "Edge", "Panels/chip_3_outline", new OzRect(0, 0, w, h), OzUi.A(OzUi.Amber, 0f)); part.edgeImg.rectTransform.Fill(); }
                    else part.edgeLines = Hud.Border(root, OzUi.A(OzUi.Amber, 0f), 1f);
                    actionParts.Add(part);
                    if (full && addHot != null) { var pr = part; addHot(root, () => StartCapture(pr), hv => pr.hover = hv); }
                }
                x -= w + CapGap; total += w + CapGap;
            }
            return total;
        }

        /// <summary>Width of a label's text in canvas units, measured without its box (TextMeshPro GetPreferredValues with an open
        /// width: preferredWidth is cached and wraps inside a small box, which kept every old keycap 14 u wide); an estimate
        /// from the caps font (0.68 em per capital + tracking) when nothing can measure.</summary>
        public static float TextWidth(OzText t)
        {
            if (t == null) return 0f;
            string s = t.text ?? "";
            if (s.Length == 0) return 0f;
            float w = 0f;
            try
            {
                if (t.graphic is TMPro.TextMeshProUGUI tmp) w = tmp.isActiveAndEnabled ? tmp.GetPreferredValues(s, 99999f, 0f).x : MeasureInactive(tmp, s);
                else if (t.graphic is Text leg) { var o = leg.horizontalOverflow; leg.horizontalOverflow = HorizontalWrapMode.Overflow; w = leg.preferredWidth; leg.horizontalOverflow = o; }
            }
            catch (Exception) { w = 0f; }
            float est = s.Length * t.Size * 0.68f;
            return w > t.Size * 0.4f ? w : est;
        }

        /// <summary>
        /// probes) measures multi-word text at about a tenth of its width ("LEFT CLICK" 6.6 u instead of 66 u), so the mouse caps
        /// were drawn too narrow with the text spilling out. An always-active hidden measurer with the same font settings gives
        /// the active value exactly.</summary>
        static TMPro.TextMeshProUGUI measurer;
        static float MeasureInactive(TMPro.TextMeshProUGUI src, string s)
        {
            if (!measurer)
            {
                var go = new GameObject("OzTextMeasure", typeof(RectTransform)) { hideFlags = HideFlags.HideAndDontSave };
                measurer = go.AddComponent<TMPro.TextMeshProUGUI>();
                measurer.textWrappingMode = TMPro.TextWrappingModes.NoWrap; measurer.raycastTarget = false;
            }
            measurer.font = src.font; measurer.fontSize = src.fontSize; measurer.fontStyle = src.fontStyle; measurer.fontWeight = src.fontWeight;
            measurer.characterSpacing = src.characterSpacing; measurer.wordSpacing = src.wordSpacing; measurer.richText = src.richText;
            return measurer.GetPreferredValues(s, 99999f, 0f).x;
        }

        // ------------------------------------------------------------------ rebinding
        void StartCapture(Part p)
        {
            if (!full) return;
            capture = p; capturingTable = this; lastTickFrame = Time.frameCount;
            Say($"PRESS THE NEW KEY FOR {OzBindings.DefOf(p.action).label.ToUpperInvariant()}{(OzBindings.DefOf(p.action).ctrl ? " (CTRL STAYS)" : "")} · ESC CANCELS", OzUi.Amber, 0f);
            Debug.Log($"[OzBindings] rebind {p.action}: waiting for a key (now {OzBindings.Cap(p.action)})");
        }

        public void Cancel(bool sound)
        {
            if (capture == null) return;
            Debug.Log($"[OzBindings] rebind {capture.action}: cancelled, stays {OzBindings.Cap(capture.action)}");
            capture = null; if (capturingTable == this) capturingTable = null;
            if (sound) sfx?.Invoke("ui_cancel");
            Say("CANCELLED · NOTHING CHANGED", OzUi.Muted, 2f);
        }

        public void ResetAll()
        {
            if (capture != null) Cancel(false);
            OzBindings.ResetAll();
            sfx?.Invoke("ui_confirm");
            Say("EVERY KEY BACK TO THE ZOD DEFAULTS · SAVED", OzUi.Go, 3f);
        }

        /// <summary>Main menu, every frame while CONTROLS is open (after the menu's Poll: a click that starts a capture has run).</summary>
        public void Tick()
        {
            if (capture != null)
            {
                lastTickFrame = Time.frameCount;
                var kb = Keyboard.current; var m = Mouse.current;
                if (m != null && (m.leftButton.wasPressedThisFrame || m.rightButton.wasPressedThisFrame))
                {
                    Vector2 mp = m.position.ReadValue(); bool onCap = false;
                    foreach (var ap in actionParts) if (ap.rt && RectTransformUtility.RectangleContainsScreenPoint(ap.rt, mp, null)) { onCap = true; break; }
                    if (!onCap || m.rightButton.wasPressedThisFrame) Cancel(true);
                }
                if (capture != null && kb != null)
                {
                    Key got = Key.None;
                    foreach (var kc in kb.allKeys) if (kc != null && kc.wasPressedThisFrame) { got = kc.keyCode; break; }
                    if (got == Key.Escape) Cancel(true);
                    else if (got != Key.None) Bind(got);
                }
            }
            else if (capturingTable == this) capturingTable = null;
            Refresh(false);
        }

        /// <summary>The captured key: bound (ui_confirm), bound with a conflict (ui_error, both caps red), or refused (ui_disabled).</summary>
        void Bind(Key got)
        {
            if (capture == null) return;
            var a = capture.action;
            if (!OzBindings.Set(a, got, out var why)) { sfx?.Invoke("ui_disabled"); Say($"{OzBindings.Cap(got)}: {why} · PICK ANOTHER KEY · ESC CANCELS", OzUi.AlertText, 0f); Debug.Log($"[OzBindings] rebind {a}: {got} refused ({why})"); return; }
            capture = null; if (capturingTable == this) capturingTable = null;
            var other = OzBindings.ConflictOf(a);
            string k = (OzBindings.DefOf(a).ctrl ? "CTRL+" : "") + OzBindings.Cap(a);
            if (other != null) { sfx?.Invoke("ui_error"); Say($"CONFLICT · {k} ALSO {other.label.ToUpperInvariant()}: BOTH FIRE · CHANGE ONE OF THEM", OzUi.AlertText, 6f); }
            else { sfx?.Invoke("ui_confirm"); Say($"SAVED · {OzBindings.DefOf(a).label.ToUpperInvariant()} = {k}", OzUi.Go, 3f); }
        }

        // ------------------------------------------------------------------ proof hooks (MenuExeProof)
        /// <summary>Start the capture on an action's cap as a click would.</summary>
        public bool ProofCapture(OzAction a) { foreach (var ap in actionParts) if (ap.action == a) { StartCapture(ap); return true; } return false; }
        /// <summary>The key a real key event would give (when the Game view gets no keyboard events).</summary>
        public void ProofPress(Key k) { if (capture == null) return; if (k == Key.Escape) Cancel(true); else Bind(k); }
        public bool IsCapturing => capture != null;
        public int ActionCaps => actionParts.Count;
        /// <summary>Every OzAction has a cap in this table.</summary>
        public bool CoversEveryAction { get { var seen = new HashSet<OzAction>(); foreach (var ap in actionParts) seen.Add(ap.action); return seen.Count == OzBindings.Defs.Length; } }
        /// <summary>Narrowest cap in canvas units (the old table: 14 u).</summary>
        public float NarrowestCap { get { float m = float.MaxValue; foreach (var ap in actionParts) if (ap.rt) m = Mathf.Min(m, ap.rt.sizeDelta.x); return m == float.MaxValue ? 0f : m; } }
        public string StatusText => status != null ? status.text : "";

        void Say(string msg, Color c, float secs) { statusMsg = msg; statusColor = c; statusUntil = secs > 0f ? OzMotion.Now + secs : float.MaxValue; }

        void Refresh(bool force)
        {
            bool keysMoved = OzBindings.Version != shownVersion;
            if (keysMoved || force)
            {
                shownVersion = OzBindings.Version;
                foreach (var ap in actionParts)
                {
                    ap.label.Set(OzBindings.Cap(ap.action));
                    if (full)
                    {   // a longer key name widens the cap to the left (right edge fixed)
                        float w = Mathf.Max(Mathf.Max(CapH + 6f, 44f), TextWidth(ap.label) + 2 * CapPad);
                        var r = ap.rt; float right = r.anchoredPosition.x + r.sizeDelta.x;
                        r.anchoredPosition = new Vector2(right - w, r.anchoredPosition.y); r.sizeDelta = new Vector2(w, r.sizeDelta.y); ap.w = w;
                    }
                }
            }
            float pulse = OzMotion.Reduced ? 1f : 0.55f + 0.45f * Mathf.Sin(OzMotion.Now * 6f);
            foreach (var ap in actionParts)
            {
                bool conflict = OzBindings.ConflictOf(ap.action) != null, cap = ap == capture;
                if (cap) ap.label.Set("PRESS A KEY");
                else if (ap.label.text == "PRESS A KEY") { ap.label.Set(OzBindings.Cap(ap.action)); }
                Color edge = cap ? OzUi.A(OzUi.Amber, pulse) : conflict ? OzUi.A(OzUi.Alert, 0.95f) : ap.hover ? OzUi.A(OzUi.Amber, 0.85f) : OzUi.A(OzUi.Amber, full ? 0.28f : 0f);   // a faint amber keyline = "click to change"
                if (ap.edgeImg) { if (ap.edgeImg.color != edge) ap.edgeImg.color = edge; }
                else if (ap.edgeLines != null) foreach (var e in ap.edgeLines) if (e && e.color != edge) e.color = edge;
                ap.label.color = cap ? (Color)OzUi.Amber : conflict ? (Color)OzUi.AlertText : ap.hover ? (Color)OzUi.AmberHi : (Color)OzUi.Ink;
            }
            FitCaptureCap(capture);
            if (status == null) return;
            string conflicts = OzBindings.ConflictText();
            if (statusMsg != null && OzMotion.Now < statusUntil) { status.Set(statusMsg); status.color = statusColor; }
            else if (conflicts != "none") { status.Set("CONFLICT · " + conflicts + " · CLICK ONE OF THE RED KEYS TO CHANGE IT"); status.color = OzUi.AlertText; }
            else { statusMsg = null; status.Set("ESC OR A CLICK OUTSIDE CLOSES · MOUSE, SHIFT / CTRL / ALT, 1 .. 0, + / -, ARROWS, ESC, F6-F9 / F11 / F12 ARE FIXED"); status.color = OzUi.Muted; }
        }

        /// <summary>The capture prompt is wider than a key: the capturing cap grows to fit it (and back afterwards).</summary>
        void FitCaptureCap(Part c)
        {
            if (!full) return;
            foreach (var ap in actionParts)
            {
                float want = ap == c ? Mathf.Max(ap.w, 110f) : ap.w;
                var r = ap.rt; if (!r || Mathf.Abs(r.sizeDelta.x - want) < 0.5f) continue;
                float right = r.anchoredPosition.x + r.sizeDelta.x;
                r.anchoredPosition = new Vector2(right - want, r.anchoredPosition.y); r.sizeDelta = new Vector2(want, r.sizeDelta.y);
            }
        }
    }
}
