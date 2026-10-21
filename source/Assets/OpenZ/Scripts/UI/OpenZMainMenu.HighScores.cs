using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace OpenZ
{
    // ============================================================================================================================
    // Battlefield screen (level select): the small HIGH SCORES button and its popup.
    //
    // The button stands in the header, left of BACK (same row, 12 u between them), in the look of the screen's small buttons:
    // a star, the words, the key cap. Mouse: a click. Keyboard: the key on its cap (HsKey, read by its place on the keyboard,
    // named as the active layout prints it: OzKeyNames), also shown in the screen's key hint row.
    //
    // The popup is a centred plate over a dark scrim, drawn above everything of this screen (a canvas of its own sorting, so the
    // commander's small transmission window never lies over it). One block per level in the menu's order (Desert, Jungle,
    // Car Park), for one difficulty at a time: the tabs EASY / MEDIUM / HARD, opening on the difficulty chosen on this screen.
    // A tab only changes what the popup shows, never the chosen difficulty. A block lists up to 10 victories, best first, with
    // exactly what a table entry stores (OzScoreEntry): place, rank (from the score), score, mission time, units killed,
    // units lost, units built, zones held of the map's zones. The best entry of a level is marked (amber line, wash, stripe).
    // A level without a victory on that difficulty says so in one line. When the blocks are taller than the view (three full
    // tables) the view scrolls: wheel, up / down, page up / page down; a thumb shows where.
    //
    // Closing: ESC, the same key, the close button, or a click on the scrim (outside the plate), as on the OPTIONS modal.
    // While it is open the popup owns the keyboard (ENTER does not deploy, the arrows do not move the cards) and every click:
    // its pointer areas are added last, so they lie over the cards, BACK and the deploy bar; a click on the plate itself does
    // nothing. Sounds, the screen's own cues: ui_panel_open, ui_panel_close, ui_tab_switch, ui_disabled at the first / last tab.
    //
    // The tables are read through OzHighScores (one local file, never sent anywhere); nothing is written here.
    // Hooks in OpenZMainMenu.cs: BuildSelect -> BuildHighScores (after the deploy bar) and the key hint row, Update ->
    // TickHighScores, Keys -> HighScoresKeys (asked first on this screen).
    // ============================================================================================================================
    public partial class OpenZMainMenu
    {
        /// <summary>The key of the HIGH SCORES popup (by its place on the keyboard).</summary>
        public const Key HsKey = Key.T;
        // the button (canvas units; anchored like BACK to the right edge)
        public const float HsBtnW = 168f, HsBtnH = 32f, HsBtnGap = 12f;
        // the plate (plate-local units, top-left origin)
        public const float HsW = 940f, HsH = 780f, HsPad = 28f, HsHead = 44f, HsTabY = 56f, HsTabX = 132f, HsTabW = 120f, HsTabH = 26f, HsTabGap = 4f, HsViewY = 96f, HsFoot = 44f;
        // a level's block inside the view: title line, column titles, rows, room to the next block
        public const float HsBlockHead = 34f, HsColHead = 22f, HsRowH = 22f, HsBlockGap = 18f, HsEmptyH = 26f, HsContentPad = 6f;
        const float HsScrimAlpha = 0.62f;                                                                     // the OPTIONS modal's scrim
        const int HsSortAbove = 5;                                                                            // above the menu canvas and the commander's window (+1), under the confirm modal (+50)
        static readonly GameDifficulty[] HsTabs = { GameDifficulty.Easy, GameDifficulty.Medium, GameDifficulty.Hard };
        /// <summary>The table's columns: title, left edge and width in the view, right-aligned or not. Boxes never overlap.
        /// The place ends at x 26; the rank starts at x 44 in a 170 u box (the longest rank name is 139 u wide); the room right of
        /// it, x 216 to 866, holds the six number columns as equal 80 u boxes with 34 u between them, so their right edges stand
        /// 114 u apart: 296, 410, 524, 638, 752, 866. With the widest values and the titles, the free room between two neighbours
        /// is 67 to 79 u from the rank on.</summary>
        static readonly (string title, float x, float w, bool right)[] HsCols =
        {
            ("#", 0f, 26f, true), ("RANK", 44f, 170f, false), ("SCORE", 216f, 80f, true), ("TIME", 330f, 80f, true),
            ("KILLED", 444f, 80f, true), ("LOST", 558f, 80f, true), ("BUILT", 672f, 80f, true), ("ZONES", 786f, 80f, true),
        };
        /// <summary>The table's right edge in the view (the last column's, 4 u inside the 870 u view): the numbers, their titles and
        /// a level's count line all end here.</summary>
        static float HsTableRight => HsCols[HsCols.Length - 1].x + HsCols[HsCols.Length - 1].w;

        sealed class HsRow { public RectTransform rt; public bool best; public OzText[] cells; }
        sealed class HsBlock { public RectTransform rt, cols; public OzText count, empty; public HsRow[] rows; public int shown; }

        HudButton hsBtn; RectTransform hsRoot, hsPanel, hsView, hsContent, hsTrack, hsClose; Canvas hsCanvas; CanvasGroup hsCg; Image hsScrim, hsThumb;
        readonly List<HudButton> hsTabBtns = new List<HudButton>(); readonly List<OzText> hsTexts = new List<OzText>(); HsBlock[] hsBlocks;
        bool hsOpen; int hsTab = -1, hsShownVersion = -1; float hsAt = -10f, hsViewAt, hsScroll, hsTarget;

        // ---------------------------------------------------------------- build
        /// <summary>BuildSelect, last: the button in the header and the popup's (still empty) root. The plate itself is built the
        /// first time the popup opens, so the menu's own build stays as fast as before.</summary>
        void BuildHighScores(RectTransform p)
        {
            var br = OzMenuLayout.Back;
            hsBtn = HudButton.Make(p, "HighScores", "HIGH SCORES", new Vector2(HsBtnW, HsBtnH), HudButtonKind.Small, 11, 0.16f, HudButtonStyle.Normal, true, OzH.L);
            hsBtn.Rt.Place(new OzRect(OzAnchor.TR, br.x - HsBtnGap - HsBtnW, br.y + (br.h - HsBtnH) * 0.5f, HsBtnW, HsBtnH));
            hsBtn.Label.FillParent(OzH.L, 32, 0, 0, 0);
            Hud.Icon(hsBtn.Content, "Glyph", "star", 10, (HsBtnH - 16f) * 0.5f, 16, OzUi.Amber);
            Hud.Keycap(hsBtn.Content, "Key", OzKeyNames.Name(HsKey), HsBtnW - 8, (HsBtnH - 14) * 0.5f);
            AddHot(hsBtn.Rt, View.Select, () => OpenHighScores(), null, hsBtn, null);

            hsRoot = Hud.Node("HighScoresPopup", p); hsRoot.Fill();
            hsCanvas = hsRoot.gameObject.AddComponent<Canvas>(); hsCanvas.overrideSorting = true; hsCanvas.sortingOrder = canvas.sortingOrder + HsSortAbove;
            hsCg = hsRoot.gameObject.AddComponent<CanvasGroup>(); hsCg.alpha = 0f; hsCg.blocksRaycasts = false;
            hsPanel = null; hsBlocks = null; hsTabBtns.Clear(); hsTexts.Clear(); hsShownVersion = -1;
            hsRoot.gameObject.SetActive(false);
            if (hsOpen) { hsOpen = false; OpenHighScores(true); }                                           // a rebuild (the screen size changed) keeps it open, on the same tab
        }

        OzText HsLabel(Transform parent, string name, string text, float size, Color c, float tracking, OzStyle style = OzStyle.Plain)
        {
            var t = Hud.Label(parent, name, text, size, c, tracking, style);
            hsTexts.Add(t);
            return t;
        }

        /// <summary>The plate, the scrim and their pointer areas. Called with the popup's root shown, after every other pointer
        /// area of the screen exists: the list is walked from its end, so these win.</summary>
        void BuildHighScoresPanel()
        {
            hsScrim = Hud.Img(hsRoot, "Scrim", OzUi.A(OzUi.GM950, HsScrimAlpha)); hsScrim.rectTransform.Fill();
            hots.Add(new Hot { rt = hsScrim.rectTransform, view = View.Select, click = () => CloseHighScores(), sfx = "" });   // "": no hover tick
            hsPanel = Hud.Node("Panel", hsRoot);
            hsPanel.anchorMin = hsPanel.anchorMax = hsPanel.pivot = new Vector2(0.5f, 0.5f); hsPanel.sizeDelta = new Vector2(HsW, HsH); hsPanel.anchoredPosition = Vector2.zero;
            var back = Hud.Img(hsPanel, "Back", OzUi.A(OzUi.GM900, 0.97f)); back.rectTransform.Fill(6, 6, 6, 6);   // a solid bed: the table stays readable over the dimmed cards
            Hud.Plate(hsPanel, "lg", HsHead, OzUi.Amber, null, true);
            back.transform.SetSiblingIndex(1);                                                               // over the plate's shadow, under its body
            hots.Add(new Hot { rt = hsPanel, view = View.Select, click = null, sfx = "" });                   // the plate swallows its own clicks

            // header: title, where the tables live, close
            HsLabel(hsPanel, "Title", "HIGH SCORES", 16, OzUi.Ink, 0.16f, OzStyle.Underlay).At(HsPad, 15);
            HsLabel(hsPanel, "Sub", "KEPT ON THIS COMPUTER ONLY · NEVER SENT ANYWHERE", 9, OzUi.Muted, 0.2f).At(HsW - 60f, 18, OzH.R, false, 440);
            var close = HudButton.Make(hsPanel, "Close", null, new Vector2(26, 24), HudButtonKind.Small);
            close.Rt.Place(new OzRect(HsW - 20f - 26f, 10, 26, 24)); Hud.Icon(close.Content, "X", "close", 6, 5, 14, OzUi.Ink2);
            hsClose = close.Rt;
            AddHot(close.Rt, View.Select, () => CloseHighScores(), null, close, null);

            // difficulty tabs
            HsLabel(hsPanel, "TabLabel", "DIFFICULTY", 10, OzUi.Muted, 0.22f).At(HsPad, HsTabY + HsTabH * 0.5f, OzH.L, true, 100);
            hsTabBtns.Clear();
            for (int i = 0; i < HsTabs.Length; i++)
            {
                int id = i;
                var b = HudButton.Make(hsPanel, "Tab" + i, OpenZDifficulty.Label(HsTabs[i]), new Vector2(HsTabW, HsTabH), HudButtonKind.Small, 11, 0.14f);
                b.Rt.Place(new OzRect(HsTabX + i * (HsTabW + HsTabGap), HsTabY, HsTabW, HsTabH));
                AddHot(b.Rt, View.Select, () => ShowHighScoresTab(id), null, b, "ui_tab_switch");
                hsTabBtns.Add(b);
            }
            HsLabel(hsPanel, "TabNote", "THE " + OzHighScores.TableSize + " BEST VICTORIES OF EACH LEVEL", 9, OzUi.Muted, 0.2f).At(HsW - HsPad, HsTabY + HsTabH * 0.5f, OzH.R, true, 380);
            Hud.Img(hsPanel, "TabRule", OzUi.A(OzUi.Amber, 0.35f)).rectTransform.Place(new OzRect(HsPad, HsTabY + HsTabH + 5f, HsW - 2 * HsPad, 1f));

            // the view: one block per level, scrolling when the blocks are taller than it
            float vw = HsW - 2 * HsPad - 14f, vh = HsH - HsViewY - HsFoot;
            hsView = Hud.Node("View", hsPanel).Place(new OzRect(HsPad, HsViewY, vw, vh));
            hsView.gameObject.AddComponent<RectMask2D>();
            hsContent = Hud.Node("Content", hsView);
            hsContent.anchorMin = new Vector2(0, 1); hsContent.anchorMax = new Vector2(1, 1); hsContent.pivot = new Vector2(0.5f, 1);
            hsContent.offsetMin = new Vector2(0, -10); hsContent.offsetMax = Vector2.zero;
            hsBlocks = new HsBlock[levels.Length];
            for (int i = 0; i < levels.Length; i++) hsBlocks[i] = BuildHighScoresBlock(levels[i], vw);
            hsTrack = Hud.Node("Track", hsPanel).Place(new OzRect(HsW - HsPad - 6f, HsViewY, 4, vh));
            Hud.Img(hsTrack, "Bg", OzUi.A(OzUi.GM950, 0.9f)).rectTransform.Fill();
            hsThumb = Hud.Img(hsTrack, "Thumb", OzUi.Amber);
            hsThumb.rectTransform.anchorMin = new Vector2(0, 1); hsThumb.rectTransform.anchorMax = new Vector2(1, 1); hsThumb.rectTransform.pivot = new Vector2(0.5f, 1);

            // foot: the keys, and what a score is made of
            HsLabel(hsPanel, "Keys", "WHEEL / ↑ ↓  SCROLL     ← →  DIFFICULTY     " + OzKeyNames.Name(HsKey) + " / ESC  CLOSE", 9, OzUi.Dim, 0.2f).At(HsPad, HsH - 24, OzH.L, false, 470);
            HsLabel(hsPanel, "Made", "SCORE = VICTORY + SPEED + COMBAT + TERRITORY", 9, OzUi.Dim, 0.2f).At(HsW - HsPad, HsH - 24, OzH.R, false, 400);
            Debug.Log($"[OpenZ Menu] HIGH SCORES popup built: plate {HsW:0} x {HsH:0} u, view {vw:0} x {vh:0} u, {levels.Length} levels x {OzHighScores.TableSize} rows x {HsCols.Length} columns, on the {CW:0} x 1080 u canvas ({Screen.width}x{Screen.height})");
        }

        HsBlock BuildHighScoresBlock(OzLevelDef d, float vw)
        {
            var b = new HsBlock();
            b.rt = Hud.Node("Level_" + d.Id, hsContent).Place(new OzRect(0, 0, vw, HsBlockHead));
            HsLabel(b.rt, "Level", "LEVEL " + d.Number, 11, OzUi.Amber, 0.3f).At(0, 15f, OzH.L, true, 96);
            HsLabel(b.rt, "Name", d.Name, 20, OzUi.Ink, 0.02f, OzStyle.Underlay).At(100, 15f, OzH.L, true, 400);
            b.count = HsLabel(b.rt, "Count", "", 9, OzUi.Muted, 0.2f).At(HsTableRight, 15f, OzH.R, true, 300);   // over the last column's right edge: on the view's own edge the letter spacing after its last digit reached 2 u past the view
            Hud.Img(b.rt, "Rule", OzUi.A(OzUi.Khaki, 0.18f)).rectTransform.Place(new OzRect(0, HsBlockHead - 4f, vw, 1f));
            b.cols = Hud.Node("Columns", b.rt).Place(new OzRect(0, HsBlockHead, vw, HsColHead));
            foreach (var c in HsCols)
                HsLabel(b.cols, "Col_" + c.title, c.title, 9, OzUi.Muted, 0.2f).At(c.right ? c.x + c.w : c.x, HsColHead * 0.5f, c.right ? OzH.R : OzH.L, true, c.w);
            b.rows = new HsRow[OzHighScores.TableSize];
            for (int r = 0; r < b.rows.Length; r++)
            {
                var row = new HsRow { best = r == 0 };
                row.rt = Hud.Node("Row" + (r + 1), b.rt).Place(new OzRect(0, HsBlockHead + HsColHead + r * HsRowH, vw, HsRowH));
                if (row.best)
                {   // the level's best entry: amber wash, stripe and text
                    Hud.Img(row.rt, "Best", OzUi.A(OzUi.Amber, 0.14f)).rectTransform.Fill();
                    Hud.Img(row.rt, "BestStripe", OzUi.Amber).rectTransform.Place(new OzRect(0, 0, 3, HsRowH));
                }
                else if ((r & 1) == 0) Hud.Img(row.rt, "Band", OzUi.A(OzUi.Ink, 0.03f)).rectTransform.Fill();   // every other row a shade lighter: easier to read across
                row.cells = new OzText[HsCols.Length];
                for (int c = 0; c < HsCols.Length; c++)
                {
                    var col = HsCols[c];
                    Color ink = row.best ? (Color)OzUi.AmberHi : c == 0 ? (Color)OzUi.Muted : (Color)OzUi.Ink2;
                    row.cells[c] = HsLabel(row.rt, "Cell_" + col.title, "", 12, ink, c == 1 ? 0.04f : 0.02f)
                        .At(col.right ? col.x + col.w : col.x, HsRowH * 0.5f, col.right ? OzH.R : OzH.L, true, col.w);
                }
                b.rows[r] = row;
            }
            b.empty = HsLabel(b.rt, "Empty", "", 11, OzUi.Muted, 0.14f).At(0, HsBlockHead + HsEmptyH * 0.5f, OzH.L, true, vw);
            return b;
        }

        // ---------------------------------------------------------------- open, close, tabs
        void OpenHighScores(bool instant = false)
        {
            if (hsOpen || view != View.Select || !hsRoot) return;
            hsOpen = true; hsAt = instant ? -10f : OzMotion.Now; hsViewAt = viewAt;
            hsRoot.gameObject.SetActive(true);
            hsCanvas.overrideSorting = true; hsCanvas.sortingOrder = canvas.sortingOrder + HsSortAbove;       // set on the shown popup: its own sorting, above this screen
            if (!hsPanel) BuildHighScoresPanel();
            if (!instant || hsTab < 0) hsTab = System.Array.IndexOf(HsTabs, OpenZDifficulty.Level);            // opens on the difficulty chosen on this screen
            if (hsTab < 0) hsTab = 1;
            if (!instant) hsScroll = hsTarget = 0f;
            FillHighScores();
            SetSelected(hsBtn, true);
            if (!instant) Sfx("ui_panel_open");
        }

        /// <summary>silent: at once and without the cue (the screen was left).</summary>
        void CloseHighScores(bool silent = false)
        {
            if (!hsOpen) return;
            hsOpen = false; hsAt = OzMotion.Now;
            SetSelected(hsBtn, false);
            if (silent) { if (hsRoot) hsRoot.gameObject.SetActive(false); }
            else Sfx("ui_panel_close");
        }

        void ShowHighScoresTab(int tab)
        {
            hsTab = Mathf.Clamp(tab, 0, HsTabs.Length - 1); hsScroll = hsTarget = 0f;
            FillHighScores();
        }

        void StepHighScoresTab(int dir)
        {
            int to = Mathf.Clamp(hsTab + dir, 0, HsTabs.Length - 1);
            if (to == hsTab) { Sfx("ui_disabled"); return; }                                                 // the first / last tab answers
            Sfx("ui_tab_switch"); ShowHighScoresTab(to);
        }

        void ScrollHighScores(float dy)
        {
            if (!hsContent || !hsView) return;
            float max = Mathf.Max(0f, hsContent.sizeDelta.y - hsView.rect.height);
            hsTarget = Mathf.Clamp(hsTarget + dy, 0f, max);
        }

        /// <summary>The shown tab's tables into the rows; the blocks stand under each other, each as tall as what it shows.</summary>
        void FillHighScores()
        {
            if (hsBlocks == null) return;
            hsShownVersion = OzHighScores.Version;
            var d = HsTabs[Mathf.Clamp(hsTab, 0, HsTabs.Length - 1)]; string diff = OpenZDifficulty.Label(d);
            for (int i = 0; i < hsTabBtns.Count; i++) SetSelected(hsTabBtns[i], i == hsTab);
            float y = HsContentPad;
            for (int i = 0; i < hsBlocks.Length; i++)
            {
                var b = hsBlocks[i]; var list = OzHighScores.Top(levels[i].Id, d);
                int n = Mathf.Min(list.Count, b.rows.Length);
                b.shown = n;
                b.count.Set(n > 0 ? OzText.Tab(n + " OF " + OzHighScores.TableSize) : "");
                b.cols.gameObject.SetActiveFast(n > 0);
                b.empty.SetActive(n == 0);
                if (n == 0) b.empty.Set("NO VICTORY ON THIS LEVEL ON " + diff + " YET");
                for (int r = 0; r < b.rows.Length; r++)
                {
                    var row = b.rows[r]; bool on = r < n;
                    row.rt.gameObject.SetActiveFast(on);
                    if (!on) continue;
                    var e = list[r];
                    row.cells[0].Set(OzText.Tab((r + 1) + "."));
                    row.cells[1].Set(OzHighScores.Rank(e.score));
                    row.cells[2].Set(OzText.Tab(OzHighScores.Group(e.score)));
                    row.cells[3].Set(OzText.Tab(UnitInfo.Duration(e.seconds)));
                    row.cells[4].Set(OzText.Tab(e.kills.ToString()));
                    row.cells[5].Set(OzText.Tab(e.losses.ToString()));
                    row.cells[6].Set(OzText.Tab(e.built.ToString()));
                    row.cells[7].Set(e.zonesTotal > 0 ? OzText.Tab(e.zones + "/" + e.zonesTotal) : "-");       // an entry without the zone numbers shows a dash
                }
                float h = HsBlockHead + (n > 0 ? HsColHead + n * HsRowH : HsEmptyH);
                b.rt.anchoredPosition = new Vector2(0, -y); b.rt.sizeDelta = new Vector2(b.rt.sizeDelta.x, h);
                y += h + HsBlockGap;
            }
            hsContent.sizeDelta = new Vector2(0, y - HsBlockGap + HsContentPad);
        }

        // ---------------------------------------------------------------- per frame, keys
        /// <summary>Update, battlefield screen: the fade, the wheel, the scroll, a table that changed.</summary>
        void TickHighScores(float dt)
        {
            if (!hsRoot) return;
            if (hsOpen && hsViewAt != viewAt) CloseHighScores(true);                                         // the screen was left and entered again
            if (!hsRoot.gameObject.activeSelf || !hsPanel) return;
            if (hsOpen)
            {
                float k = OzEase.OutCubic(OzMotion.T(hsAt, OzMotion.Dur(OzMotion.Short, true)));
                hsCg.alpha = k; hsPanel.localScale = Vector3.one * Mathf.Lerp(0.96f, 1f, k);
                if (hsShownVersion != OzHighScores.Version) FillHighScores();
                var m = Mouse.current; float wheel = m != null ? m.scroll.ReadValue().y : 0f;
                if (Mathf.Abs(wheel) > 0.01f) ScrollHighScores(-Mathf.Sign(wheel) * 56f);
            }
            else
            {
                float kc = OzMotion.T(hsAt, OzMotion.Dur(0.1f, true));
                hsCg.alpha = Mathf.Min(hsCg.alpha, 1f - kc);
                if (kc >= 1f) { hsRoot.gameObject.SetActive(false); return; }
            }
            float max = Mathf.Max(0f, hsContent.sizeDelta.y - hsView.rect.height);
            hsTarget = Mathf.Clamp(hsTarget, 0f, max);
            hsScroll = OzMotion.Reduced ? hsTarget : Mathf.Lerp(hsScroll, hsTarget, 1f - Mathf.Exp(-dt * 14f));
            hsContent.anchoredPosition = new Vector2(0, hsScroll);
            float th = hsTrack.rect.height, vis = max <= 0 ? 1f : hsView.rect.height / hsContent.sizeDelta.y;
            hsThumb.rectTransform.sizeDelta = new Vector2(0, Mathf.Max(24f, th * vis));
            hsThumb.rectTransform.anchoredPosition = new Vector2(0, max <= 0 ? 0 : -(th - hsThumb.rectTransform.sizeDelta.y) * hsScroll / max);
            hsTrack.gameObject.SetActiveFast(max > 0);
        }

        /// <summary>Keys(), battlefield screen, asked first. True = the popup took the keyboard this frame.</summary>
        bool HighScoresKeys(Keyboard kb, bool left, bool right, bool up, bool down, bool esc)
        {
            bool key = kb[HsKey].wasPressedThisFrame && !kb.ctrlKey.isPressed && !kb.altKey.isPressed;
            int page = kb.pageUpKey.wasPressedThisFrame ? -1 : kb.pageDownKey.wasPressedThisFrame ? 1 : 0;
            return HighScoresInput(key, esc, left, right, up, down, page);
        }

        /// <summary>Closed: the key opens it, everything else is the screen's. Open: the key or ESC closes it, left / right step
        /// the tabs, up / down and the page keys scroll, and every other key of the screen waits.</summary>
        bool HighScoresInput(bool key, bool esc, bool left, bool right, bool up, bool down, int page)
        {
            if (!hsOpen) { if (key) { OpenHighScores(); return true; } return false; }
            if (esc || key) { CloseHighScores(); return true; }
            if (left || right) StepHighScoresTab(right ? 1 : -1);
            else if (up || down) ScrollHighScores(up ? -56f : 56f);
            else if (page != 0) ScrollHighScores(page * 400f);
            return true;
        }

        // ---------------------------------------------------------------- checks (Play only)
        public bool ProofHighScoresOpen => hsOpen;
        public void ProofHighScores(bool open) { if (open) OpenHighScores(); else CloseHighScores(); }
        /// <summary>The shown tab: 0 EASY, 1 MEDIUM, 2 HARD.</summary>
        public int ProofHighScoresTab => hsTab;
        /// <summary>This frame's keys as Keys() hands them over (key = the popup's own key).</summary>
        public bool ProofHighScoresInput(bool key, bool esc, bool left = false, bool right = false) => HighScoresInput(key, esc, left, right, false, false, 0);
        public RectTransform ProofHighScoresButton => hsBtn != null ? hsBtn.Rt : null;
        public RectTransform ProofHighScoresPanel => hsPanel;
        public RectTransform ProofHighScoresView => hsView;
        public RectTransform ProofHighScoresClose => hsClose;
        public RectTransform ProofHighScoresTabRt(int tab) => tab >= 0 && tab < hsTabBtns.Count ? hsTabBtns[tab].Rt : null;
        public Canvas ProofHighScoresCanvas => hsCanvas;
        public bool ProofHighScoresShown => hsRoot && hsRoot.gameObject.activeInHierarchy;
        /// <summary>How far the blocks are taller than the view (0 = everything fits), and the scroll thumb's state.</summary>
        public float ProofHighScoresOverflow => hsContent && hsView ? Mathf.Max(0f, hsContent.sizeDelta.y - hsView.rect.height) : 0f;
        public bool ProofHighScoresTrackOn => hsTrack && hsTrack.gameObject.activeSelf;
        /// <summary>Scroll at once: 0 = the top, 1 = the end.</summary>
        public void ProofHighScoresScroll(float k01) { hsTarget = hsScroll = ProofHighScoresOverflow * Mathf.Clamp01(k01); if (hsContent) hsContent.anchoredPosition = new Vector2(0, hsScroll); }
        public int ProofHighScoresLevels => hsBlocks != null ? hsBlocks.Length : 0;
        public RectTransform ProofHighScoresBlock(int level) => hsBlocks != null && level >= 0 && level < hsBlocks.Length ? hsBlocks[level].rt : null;
        public int ProofHighScoresRows(int level) => hsBlocks != null && level >= 0 && level < hsBlocks.Length ? hsBlocks[level].shown : -1;
        public RectTransform ProofHighScoresRow(int level, int row) => ProofHighScoresRows(level) > row && row >= 0 ? hsBlocks[level].rows[row].rt : null;
        public bool ProofHighScoresBest(int level, int row) => ProofHighScoresRows(level) > row && row >= 0 && hsBlocks[level].rows[row].best;
        public int ProofHighScoresColumns => HsCols.Length;
        /// <summary>A cell as shown (with the digit tags), null when that row is not shown.</summary>
        public string ProofHighScoresCell(int level, int row, int col) => ProofHighScoresRows(level) > row && row >= 0 && col >= 0 && col < HsCols.Length ? hsBlocks[level].rows[row].cells[col].text : null;
        /// <summary>The "no victory yet" line of a level ("" while the level has rows).</summary>
        public string ProofHighScoresEmpty(int level) => ProofHighScoresRows(level) == 0 ? hsBlocks[level].empty.text : "";
        public void ProofHighScoresTexts(List<OzText> into) { foreach (var t in hsTexts) if (t != null && t.graphic && t.gameObject.activeInHierarchy) into.Add(t); }
        /// <summary>What a pointer at this screen point is on, by the menu's own walk: "scrim", "plate", "close", "tab0" .. "tab2"
        /// (the popup's), "button" (HIGH SCORES), "card0" .., "other:name" (anything else of the screen) or "none".</summary>
        public string ProofHighScoresHot(Vector2 mp)
        {
            var h = HotAt(mp); if (h == null) return "none";
            if (hsScrim && h.rt == hsScrim.rectTransform) return "scrim";
            if (hsPanel && h.rt == hsPanel) return "plate";
            if (hsClose && h.rt == hsClose) return "close";
            for (int i = 0; i < hsTabBtns.Count; i++) if (h.rt == hsTabBtns[i].Rt) return "tab" + i;
            if (hsBtn != null && h.rt == hsBtn.Rt) return "button";
            for (int i = 0; i < cards.Length; i++) if (cards[i] != null && h.rt == cards[i].rt) return "card" + i;
            return "other:" + (h.rt ? h.rt.name : "?");
        }
        public RectTransform ProofSelectRoot => selectRoot;
        public string ProofView => view.ToString();
        public int ProofFocusCard => focusCard;
        public int ProofCardCount => cards.Length;
        public RectTransform ProofCard(int i) => i >= 0 && i < cards.Length && cards[i] != null ? cards[i].rt : null;
    }
}
