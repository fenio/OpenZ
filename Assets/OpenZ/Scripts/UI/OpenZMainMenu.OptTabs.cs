using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    // ============================================================================================================================
    // OPTIONS tabs (main menu). The modal had every setting in one tall column. Now a tab strip sits under the title
    // (OzSettingTips.TabNames: MAIN / GAME / GRAPHICS / ADVANCED) and every row carries the tab of its label (OzSettingTips.TabOf,
    // one table for this menu and the pause menu). Only the open tab's rows are active and they are placed from the top. MAIN
    // also holds the difficulty row and the four volume sliders (the TabMain page). The modal is OptW wide (was OzLayout.PauseW)
    // and as tall as its tallest tab.
    // Hooks in the other parts: BuildOptions -> OptMainPage, ExeOptions -> BuildOptTabs, Keys -> StepOptTab, DragSliders (MAIN
    // only), AddToggle / AddCycle (Toggle.tab).
    // ============================================================================================================================
    public partial class OpenZMainMenu
    {
        const float OptW = 600f;                                                                              // panel units; x optionsScale on the canvas
        int optTab; RectTransform optMainPage;
        readonly List<HudButton> optTabBtns = new List<HudButton>();

        /// <summary>BuildOptions: the page for MAIN's fixed controls (difficulty, volume sliders), moved down to clear the tab strip.</summary>
        RectTransform OptMainPage(RectTransform p)
        {
            optMainPage = Hud.Node("TabMain", p); optMainPage.Fill(0f, -OzLayout.OptTabShift, 0f, OzLayout.OptTabShift);
            return optMainPage;
        }

        /// <summary>ExeOptions, after the last row: the tab strip, then every tab's rows from the top in the table's order.
        /// Returns the plate height: the tallest tab.</summary>
        float BuildOptTabs(RectTransform p)
        {
            float x = OzLayout.OptX, w = OptW - 2 * OzLayout.OptX, tallest = 0f; int n = OzSettingTips.TabNames.Length;
            float bw = (w - OzLayout.OptTabGap * (n - 1)) / n;
            optTabBtns.Clear();
            for (int i = 0; i < n; i++)
            {
                int id = i;
                var b = HudButton.Make(p, "Tab" + i, OzSettingTips.TabNames[i], new Vector2(bw, OzLayout.OptTabH), HudButtonKind.Small, 11, 0.14f);
                b.Rt.Place(new OzRect(x + i * (bw + OzLayout.OptTabGap), OzLayout.OptTabY, bw, OzLayout.OptTabH));
                AddHot(b.Rt, View.Main, () => ShowOptTab(id), null, b, "ui_tab_switch", Side.Options);
                optTabBtns.Add(b);
            }
            Hud.Img(p, "TabRule", OzUi.A(OzUi.Amber, 0.35f)).rectTransform.Place(new OzRect(x, OzLayout.OptTabY + OzLayout.OptTabH + 3f, w, 1f));
            for (int tab = 0; tab < n; tab++)
            {
                var rows = toggles.FindAll(t => t.tab == tab);
                rows.Sort((r1, r2) => { int o = OzSettingTips.TabOrder(r1.label).CompareTo(OzSettingTips.TabOrder(r2.label)); return o != 0 ? o : toggles.IndexOf(r1).CompareTo(toggles.IndexOf(r2)); });
                float top = tab == OzSettingTips.TabMain ? OzLayout.OptToggleY0 + OzLayout.OptTabShift : OzLayout.OptTabRowY0;
                for (int k = 0; k < rows.Count; k++) rows[k].row.Rt.Place(new OzRect(x, top + OzLayout.OptToggleStep * k, w, OzLayout.OptToggleH));
                tallest = Mathf.Max(tallest, top + OzLayout.OptToggleStep * rows.Count + 22f);
            }
            ShowOptTab(Mathf.Clamp(optTab, 0, n - 1));                                                       // a rebuild (resolution change) keeps the open tab
            return tallest;
        }

        /// <summary>Open one tab: its rows and (MAIN) the difficulty + volume page on, the rest off; the tab's button lights.</summary>
        void ShowOptTab(int tab)
        {
            optTab = tab; dragging = null;
            foreach (var t in toggles) t.row.Rt.gameObject.SetActiveFast(t.tab == tab);
            if (optMainPage) optMainPage.gameObject.SetActiveFast(tab == OzSettingTips.TabMain);
            for (int i = 0; i < optTabBtns.Count; i++) SetSelected(optTabBtns[i], i == tab);
        }

        /// <summary>Keys, OPTIONS open: left / right step through the tabs; the ends answer.</summary>
        void StepOptTab(int dir)
        {
            int to = Mathf.Clamp(optTab + dir, 0, optTabBtns.Count - 1);
            if (to == optTab) { Sfx("ui_disabled"); return; }
            Sfx("ui_tab_switch"); ShowOptTab(to);
        }

        // ---------------------------------------------------------------- proof hooks (Play only)
        public void ProofOptTab(int tab) { if (optTabBtns.Count > 0) ShowOptTab(Mathf.Clamp(tab, 0, optTabBtns.Count - 1)); }
        public int ProofOptTabOpen => optTab;
        /// <summary>The labels of the rows shown now, in creation order, " | " between them.</summary>
        public string ProofOptTabRows
        {
            get
            {
                var sb = new System.Text.StringBuilder();
                foreach (var t in toggles) if (t.row.Rt.gameObject.activeSelf) { if (sb.Length > 0) sb.Append(" | "); sb.Append(t.label); }
                return sb.ToString();
            }
        }
        /// <summary>The screen point in the middle of a tab button (for ProofClickAt).</summary>
        public Vector2 ProofOptTabPoint(int tab)
        {
            if (optTabBtns.Count == 0) return default;
            var rt = optTabBtns[Mathf.Clamp(tab, 0, optTabBtns.Count - 1)].Rt;
            return RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint(rt.rect.center));
        }
    }
}
