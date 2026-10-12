using UnityEngine;
using UnityEngine.UI;

namespace OpenZ
{
    /// <summary>
    /// CONTROLS page (the kit's options plate, measured key caps, amber sections), REBUILT from OzBindings every time it opens, so a key
    /// changed in the main menu shows at once (never a hard-coded key text). The game keeps running under it (it only reads); F1 or
    /// ESC closes it. Sounds: ui_panel_open / ui_panel_close (OzSoundLog AD rows). Sits on the right side, clear of the minimap and
    /// the unit panel: it slides up when they reach higher, and when the scaled-up HUD leaves no room there it sits on the left (Home).
    /// </summary>
    public class OzHelpOverlay : MonoBehaviour
    {
        OpenZHud hud; Canvas canvas; RectTransform root, panel, table; CanvasGroup cg;
        bool open; float t; int builtVersion = -1;
        public bool IsOpen => open;
        /// <summary>Checks: opens / closes / rebuilds.</summary>
        public static int Opens, Closes, Rebuilds;

        public void Build(OpenZHud h)
        {
            hud = h;
            var go = new GameObject("OpenZ_Help", typeof(RectTransform)) { layer = 5 };
            go.transform.SetParent(h.transform, false);
            canvas = go.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = h.sortingOrder + 3;
            var cs = go.AddComponent<CanvasScaler>(); cs.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            cs.referenceResolution = new Vector2(1920, 1080); cs.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight; cs.matchWidthOrHeight = 1f;
            root = Hud.Node("Root", go.transform); root.Fill();
            cg = root.gameObject.AddComponent<CanvasGroup>(); cg.blocksRaycasts = false; cg.interactable = false; cg.alpha = 0f;
            panel = Hud.Node("HelpPanel", root).Place(new OzRect(OzAnchor.TR, OzLayout.RefW - OzLayout.PauseW - HomeX, HomeY, OzLayout.PauseW, OzLayout.PauseH));
            if (OzKit.Available) Hud.Shadow(panel, "Panels/panel_lg_mask");
            var plate = Hud.Kit(panel, "Plate", "Panels/options_panel", new OzRect(0, 0, OzLayout.PauseW, OzLayout.PauseH), null);
            if (!OzKit.Available) plate.color = HudStyle.Slate;
            Hud.Label(panel, "Close", "F1 · ESC CLOSE", 9, OzUi.Muted, 0.2f, OzStyle.Plain).At(OzLayout.PauseW - OzLayout.OptX, OzLayout.PauseH - 30, OzH.R, false, 200);
            go.SetActive(false);
        }

        void Rebuild()
        {
            if (table) Destroy(table.gameObject);
            table = Hud.Node("Table", panel); table.Fill();
            OzControlsTable.BuildCompact(table, OzLayout.PauseW, OzLayout.PauseH, FootShort);              // the short footer: the close hint stands right of it on the same line
            builtVersion = OzBindings.Version; Rebuilds++;
        }
        const string FootShort = "CHANGE KEYS: MAIN MENU · CONTROLS";
        const float HomeX = 36f, HomeY = 140f, Gap = 12f;

        /// <summary>The top edge (this canvas's units, above the bottom edge) of an HUD rect when it reaches under the panel's place on
        /// the right, else 0. The HUD's own canvas is scaled by the HUD SCALE s; this one is not.</summary>
        static float Reach(OzRect r, float s)
        {
            float near = (OzLayout.RefW - r.x - r.w) * s, far = (OzLayout.RefW - r.x) * s;                   // its span, from the right edge
            return far > HomeX - Gap && near < HomeX + OzLayout.PauseW + Gap ? (OzLayout.RefH - r.y) * s : 0f;
        }

        /// <summary>Where the panel sits: x of its left edge from the canvas's right edge (negative), y of its top edge from the top, and
        /// the side it slides in from. On the right at its usual height when the minimap and the unit panel stay under it; higher (never
        /// over the MENU button) when they reach up; on the left, under the territory bar, when a scaled-up HUD leaves no room.</summary>
        Vector2 Home(out float side)
        {
            float s = Mathf.Clamp(OzText.UiScale, OzText.ScaleMin, OzText.ScaleMax);
            float cw = root.rect.width > 1f ? root.rect.width : OzLayout.RefW, ch = root.rect.height > 1f ? root.rect.height : OzLayout.RefH;
            float column = Mathf.Max(Reach(OzLayout.Minimap, s), Reach(OzLayout.UnitPanelFor(cw / s), s));
            float lowest = ch - column - Gap - OzLayout.PauseH;                                              // the lowest top edge that clears them
            float highest = (OzLayout.MenuButton.y + OzLayout.MenuButton.h) * s + Gap;
            if (OzLayout.TopStrip.w * 0.5f * s + Gap > cw * 0.5f - HomeX - OzLayout.PauseW)                  // the scaled top strip reaches over the panel's left edge
                highest = Mathf.Max(highest, (OzLayout.TopStrip.y + OzLayout.TopStrip.h) * s + Gap);
            side = 1f;
            if (lowest >= HomeY) return new Vector2(-OzLayout.PauseW - HomeX, HomeY);
            if (lowest >= highest) return new Vector2(-OzLayout.PauseW - HomeX, lowest);
            side = -1f;
            return new Vector2(-cw + HomeX, Mathf.Max(HomeY, (OzLayout.TerrTab.y + OzLayout.TerrTab.h) * s + Gap));
        }

        void Update()
        {
            if (hud == null || !canvas) return;
            var kb = UnityEngine.InputSystem.Keyboard.current;
            bool blocked = hud.Modal;
            if (kb != null && !blocked && OzBindings.Pressed(OzAction.HelpOverlay)) Toggle(!open);
            else if (open && (blocked || (kb != null && kb.escapeKey.wasPressedThisFrame))) Toggle(false);
            if (open && builtVersion != OzBindings.Version) Rebuild();                                      // a rebind while it is open
            t = Mathf.MoveTowards(t, open ? 1f : 0f, Time.unscaledDeltaTime / 0.14f);
            cg.alpha = t * t * (3f - 2f * t);
            var home = Home(out float side);
            panel.anchoredPosition = new Vector2(home.x + side * (1f - cg.alpha) * 24f, -home.y);
            if (!open && t <= 0f && canvas.gameObject.activeSelf) canvas.gameObject.SetActive(false);
        }

        public void Toggle(bool on)
        {
            if (on == open) return;
            open = on;
            if (on) { canvas.gameObject.SetActive(true); Rebuild(); Opens++; OzSfx.Ui("ui_panel_open"); }
            else { Closes++; OzSfx.Ui("ui_panel_close"); }
            Debug.Log($"[OpenZ Help] F1 overlay {(on ? "open" : "closed")} (bindings v{OzBindings.Version}, patrol {OzBindings.Cap(OzAction.Patrol)}, stop {OzBindings.Cap(OzAction.StopUnits)}, hold {OzBindings.Cap(OzAction.HoldPosition)})");
        }
    }
}
