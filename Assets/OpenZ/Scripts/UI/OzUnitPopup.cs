using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace OpenZ
{
    /// <summary>
    /// The unit card of the build menu: rest the mouse on a portrait of the production window and a small plate says what the
    /// unit is, one strength, one weakness, and its HP, damage and range with a bar each (words and numbers: OzUnitFacts).
    /// It takes the place of the plain tooltip on those portraits (OpenZHud.RefreshTooltip asks Owns); every other button keeps
    /// its tooltip.
    ///
    /// Look: the kit's medium plate with the olive header band, the amber tab and the hard shadow, Inter Tight, the HUD's
    /// colours and bars. Drawn at the build menu's own scale (1.1, less when the menu itself had to shrink), so the card and the
    /// menu always read as one piece.
    ///
    /// Place (the method Place below):
    ///   1 right of the build menu, 2 else left of it: top edge in line with the portrait's, slid up or down clear of every
    ///     other HUD panel that is on screen, never off the screen. A panel that is switched off does not count: not active,
    ///     faded out, or its own canvas off (the lower panels and the minimap while the build menu is open, OpenZHud.LowerPanels);
    ///   3 only when neither side has room: over the menu itself, above or below the whole portrait grid (the nearer first),
    ///     so no portrait is ever covered; above a row the card also stays above the row's class word (ROBOTS / VEHICLES /
    ///     CANNONS), below a row it covers the next class word whole: its edge never runs through a word;
    ///   4 above or below the hovered portrait's own rows; then the same again one and two size steps smaller.
    ///     LastResortCount counts the frames nothing fitted (the proof wants 0).
    ///   The in-game Zod transmission window is drawn over the HUD and the menu: while it is up the card keeps clear of all of it
    ///   (only when no place at all is left that way does the card give that up, before it would ever touch a HUD panel).
    /// Behaviour: appears after the mouse has rested Rest seconds on a portrait, changes at once on the next portrait, stays
    /// Linger seconds when the mouse crosses the gap between two portraits (no flicker), then fades; gone at once when the menu
    /// closes, the HUD hides, the pause menu or the end screen opens. It is no button and no blocker and none of its graphics
    /// is a raycast target: it never takes a click. Nothing is allocated per frame (cards are built once per unit).
    /// One soft "ui_hover" tick when a card comes up or changes (the main menu's hover cue).
    /// </summary>
    [DefaultExecutionOrder(20)]                    // after OpenZHud (hover) and OpenZProductionWindow (10: the menu's place this frame)
    public sealed class OzUnitPopup : MonoBehaviour
    {
        // ---- the card, in canvas units
        public const float W = 300f, H = 160f, Header = 28f, Pad = 14f;
        public const float NameSize = 16f, BodySize = 12f, LabelSize = 11f, NameTrack = 0.04f, LabelTrack = 0.16f;
        public const float BasisW = 136f, RoleY = 38f, Rule1Y = 55f, PlusY = 63f, MinusY = 80f, Rule2Y = 97f;
        public const float StatMid0 = 110.5f, StatStep = 17f, Mark = 9f, TextX = Pad + Mark + 7f, ValueX = 84f;
        public const float BarW = 84f, BarH = 8f, BarX = W - Pad - BarW;
        public const float ShadowX = 5f, ShadowY = 6f;
        // ---- placement
        public const float GapWin = 10f, GapObs = 8f, GapTile = 6f, Edge = 8f, Inset = 4f;
        static readonly float[] Steps = { 1f, 0.9f, 0.8f };
        // ---- behaviour (seconds, unscaled)
        public const float Rest = 0.30f, Linger = 0.12f, FadeIn = 0.10f, FadeOut = 0.08f;

        /// <summary>Fixed HUD panels the card never covers: direct children of the HUD root, by name or by the start of the name.</summary>
        static readonly string[] PanelNames = { "TopStrip", "Territory", "MenuButton", "UnitPanel", "AlertTab", "CommandBar", "FactoryList", "Minimap", "MissionBanner", "FortAlert" };
        static readonly string[] PanelPrefixes = { "StoredCannon", "Toast", "Cheat" };
        static readonly string[] StatNames = { "HP", "DAMAGE", "RANGE" };
        static readonly string[] BarKinds = { "health", "amber", "neutral" };

        public static OzUnitPopup I { get; private set; }
        /// <summary>Proof only (editor routine): treat this portrait as the one under the mouse, with no rest time and no sound.</summary>
        public static HudButton ProofHover;
        /// <summary>Frames on which no rule place fitted and the card fell back to the screen clamp. The proof wants 0.</summary>
        public static int LastResortCount { get; private set; }

        sealed class TileRef { public HudButton btn; public string unit; public int col, row; }
        static readonly List<TileRef> tiles = new List<TileRef>();

        // ------------------------------------------------------------------ hooks
        /// <summary>OpenZProductionWindow.MakeTile: a portrait of the build menu, its unit and its place in the grid.</summary>
        public static void Note(HudButton btn, string unit, int col, int row)
        {
            if (btn == null || unit == null) return;
            for (int i = tiles.Count - 1; i >= 0; i--) { var t = tiles[i]; if (t.btn == null || t.btn.Dead || !t.btn.Rt) tiles.RemoveAt(i); }
            tiles.Add(new TileRef { btn = btn, unit = unit, col = col, row = row });
            if (!I)
            {
                var hud = OpenZHud.I;
                if (hud && hud.Root) { I = hud.gameObject.AddComponent<OzUnitPopup>(); I.Build(hud); }
            }
        }

        /// <summary>OpenZHud.RefreshTooltip: this button is a build-menu portrait, the unit card speaks for it (no plain tooltip).</summary>
        public static bool Owns(HudButton b) => I && I.root && Find(b) != null;

        static TileRef Find(HudButton b)
        {
            if (b == null) return null;
            for (int i = 0; i < tiles.Count; i++) { var t = tiles[i]; if (t.btn == b) return t.btn.Alive ? t : null; }
            return null;
        }

        // ------------------------------------------------------------------ proof access (read only)
        public RectTransform CardRect => root;
        public bool Visible => visible;
        public string ShownUnit => shown != null ? shown.unit : null;
        public OzUnitFacts.Card ShownCard => shownCard;
        /// <summary>1 right of the menu, 2 left, 3 above the grid, 4 below the grid, 5 above the rows, 6 below the rows, 7 last resort.</summary>
        public int How { get; private set; }
        public float CardScale { get; private set; }
        public static int TileCount => tiles.Count;
        public static bool Tile(int i, out HudButton btn, out string unit, out int col, out int row)
        {
            btn = null; unit = null; col = row = 0;
            if (i < 0 || i >= tiles.Count) return false;
            var t = tiles[i]; btn = t.btn; unit = t.unit; col = t.col; row = t.row;
            return t.btn != null && t.btn.Alive;
        }
        public void ProofTexts(List<OzText> into)
        {
            into.Add(title); if (basis.text.Length > 0) into.Add(basis);
            into.Add(role); into.Add(plus); into.Add(minus);
            for (int i = 0; i < 3; i++) { into.Add(statLabel[i]); into.Add(statValue[i]); }
        }
        public HudBar ProofBar(int i) => bars[i];
        public OzText ProofValue(int i) => statValue[i];
        /// <summary>Proof only: one more frame of the card's own work (the proof counts the bytes it allocates: 0).</summary>
        public void ProofUpdate() { Update(); }

        // ------------------------------------------------------------------ build
        OpenZHud hud; RectTransform root; CanvasGroup cg;
        OzText title, basis, role, plus, minus;
        readonly OzText[] statLabel = new OzText[3], statValue = new OzText[3];
        readonly HudBar[] bars = new HudBar[3];
        Image rule1, rule2, plusH, plusV, minusH;
        float linesK = -1f;

        void Build(OpenZHud h)
        {
            hud = h;
            root = Hud.Node("UnitPopup", h.Root);
            root.anchorMin = root.anchorMax = Vector2.zero; root.pivot = new Vector2(0, 1); root.sizeDelta = new Vector2(W, H);
            Hud.Plate(root, "md", Header, (Color?)(Color)OzUi.Amber);
            float nameMid = Header * 0.5f + 0.5f;
            title = Hud.Label(root, "Name", "", NameSize, OzUi.Ink, NameTrack).At(Pad, nameMid, OzH.L, true, W - 2f * Pad);
            basis = Hud.Label(root, "Basis", "", LabelSize, OzUi.Muted, LabelTrack, OzStyle.Plain).At(W - Pad, nameMid, OzH.R, true, BasisW);
            role = Hud.Label(root, "Role", "", BodySize, OzUi.Ink2, 0f, OzStyle.Plain).At(Pad, RoleY, OzH.L, false, W - 2f * Pad);
            rule1 = Hud.Img(root, "Rule1", HudStyle.Line); rule2 = Hud.Img(root, "Rule2", HudStyle.Line);
            plusH = Hud.Img(root, "PlusH", OzUi.Go); plusV = Hud.Img(root, "PlusV", OzUi.Go); minusH = Hud.Img(root, "MinusH", OzUi.Alert);
            plus = Hud.Label(root, "Plus", "", BodySize, OzUi.Ink, 0f, OzStyle.Plain).At(TextX, PlusY, OzH.L, false, W - TextX - Pad);
            minus = Hud.Label(root, "Minus", "", BodySize, OzUi.Ink, 0f, OzStyle.Plain).At(TextX, MinusY, OzH.L, false, W - TextX - Pad);
            for (int i = 0; i < 3; i++)
            {
                float mid = StatMid0 + StatStep * i;
                statLabel[i] = Hud.Label(root, "Label" + i, StatNames[i], LabelSize, OzUi.Muted, LabelTrack, OzStyle.Plain).At(Pad, mid, OzH.L, true, ValueX - Pad - 4f);
                statValue[i] = Hud.Label(root, "Value" + i, "", BodySize, OzUi.Ink, 0f, OzStyle.Plain).At(ValueX, mid, OzH.L, true, BarX - ValueX - 8f);
                bars[i] = new HudBar(root, "Bar" + i, BarKinds[i], false, false, null, false);
                bars[i].Root.Place(new OzRect(BarX, mid - BarH * 0.5f, BarW, BarH));
            }
            cg = root.gameObject.AddComponent<CanvasGroup>(); cg.blocksRaycasts = false; cg.interactable = false;
            // its own canvas: it moves without rebuilding the HUD's, and it draws over every HUD layer below the plain tooltip
            var cv = root.gameObject.AddComponent<Canvas>(); cv.overrideSorting = true; cv.sortingOrder = h.sortingOrder + 39;
            if (h.Canvas) cv.additionalShaderChannels |= h.Canvas.additionalShaderChannels;   // the labels were made under the HUD canvas: the card's own canvas sends the same mesh channels (TextMeshPro needs them)
            LayLines(OzPx.K * OpenZProductionWindow.ProdScale);
            root.gameObject.SetActive(false);
        }

        /// <summary>Hairlines and the plus / minus marks, in whole screen pixels at the card's real size (kc = pixels per card unit).
        /// The card's corner sits on a whole pixel (Place), so every edge worked out here in pixels and divided by kc lands on one.
        /// The marks are sized so that (size - stroke) is even: the upright stroke of the plus sits exactly in the middle, both
        /// arms the same length at every interface scale.</summary>
        void LayLines(float kc)
        {
            linesK = kc;
            float t1 = Mathf.Max(1f, Mathf.Round(kc));                                          // hairline, pixels
            float t2 = Mathf.Max(1f, Mathf.Round(2f * kc));                                     // stroke of the marks, pixels
            float m = Mathf.Max(t2 + 2f, Mathf.Round(Mark * kc));                               // size of the marks, pixels
            if (Mathf.RoundToInt(m - t2) % 2 != 0) m -= 1f;                                     // equal arms
            float arm = (m - t2) * 0.5f, x0 = Mathf.Round(Pad * kc), x1 = Mathf.Round((W - Pad) * kc);
            rule1.rectTransform.TL(x0 / kc, Mathf.Round(Rule1Y * kc) / kc, (x1 - x0) / kc, t1 / kc);
            rule2.rectTransform.TL(x0 / kc, Mathf.Round(Rule2Y * kc) / kc, (x1 - x0) / kc, t1 / kc);
            float half = 0.3635f * BodySize;                                                    // the caps' centre line of each row
            float py = Mathf.Round((PlusY + half) * kc - t2 * 0.5f), my = Mathf.Round((MinusY + half) * kc - t2 * 0.5f);
            plusH.rectTransform.TL(x0 / kc, py / kc, m / kc, t2 / kc);
            plusV.rectTransform.TL((x0 + arm) / kc, (py - arm) / kc, t2 / kc, m / kc);
            minusH.rectTransform.TL(x0 / kc, my / kc, m / kc, t2 / kc);
        }

        void Fill(OzUnitFacts.Card c)
        {
            title.At(Pad, Header * 0.5f + 0.5f, OzH.L, true, c.basis.Length > 0 ? W - 2f * Pad - BasisW - 8f : W - 2f * Pad);
            title.Set(c.title); basis.Set(c.basis);
            role.Set(c.role); plus.Set(c.plus); minus.Set(c.minus);
            statValue[0].Set(c.hp); statValue[1].Set(c.damage); statValue[2].Set(c.range);
            bars[0].Set(c.hpBar); bars[1].Set(c.damageBar); bars[2].Set(c.rangeBar);
        }

        // ------------------------------------------------------------------ frame
        TileRef hover, shown; OzUnitFacts.Card shownCard;
        bool visible; float hoverSince, shownAt, lostAt = -1f;

        void Update()
        {
            if (!hud || !root) return;
            float now = OzMotion.Now;
            var prod = hud.Production;
            bool menuUp = hud.hudVisible && hud.Canvas && hud.Canvas.enabled && !hud.Modal && !OzConfirm.Blocks
                          && prod && prod.IsOpen && prod.WindowRect && !(hud.Help && hud.Help.IsOpen) && hud.W != null;
            if (!menuUp) { Hide(); hover = null; return; }                              // menu closed, HUD hidden, paused, ended: gone at once
            var t = Find(ProofHover ?? hud.HotButton);
            if (t != hover)
            {
                hover = t;
                if (t != null) hoverSince = visible ? now - Rest : now;                   // a card is up: the next portrait's card at once
            }
            if (t != null && (ProofHover != null || now - hoverSince >= Rest))
            {
                if (shown != t || shownCard == null)
                {
                    var c = OzUnitFacts.For(hud.W.S, t.unit);
                    if (c == null) { Hide(); return; }
                    Fill(c); shown = t; shownCard = c;
                    if (ProofHover == null) OzSfx.Ui("ui_hover", 0.5f);
                }
                if (!visible) { visible = true; shownAt = now; root.gameObject.SetActive(true); }
                lostAt = -1f;
                Place(t);
                float a = ProofHover != null ? 1f : OzMotion.T(shownAt, OzMotion.Dur(FadeIn, true));
                if (!Mathf.Approximately(cg.alpha, a)) cg.alpha = a;
            }
            else if (visible && t == null)
            {
                if (lostAt < 0f) lostAt = now;                                             // the mouse left the portrait: stay, then fade
                float k = OzMotion.T(lostAt, OzMotion.Dur(FadeOut, true), Linger);
                float a = 1f - k;
                if (!Mathf.Approximately(cg.alpha, a)) cg.alpha = a;
                if (k >= 1f) Hide();
            }
        }

        void OnDisable() { Hide(); hover = null; }

        void Hide()
        {
            if (root && root.gameObject.activeSelf) root.gameObject.SetActive(false);
            visible = false; shown = null; shownCard = null; lostAt = -1f; How = 0;
        }

        // ------------------------------------------------------------------ place (canvas units, origin bottom-left, y up)
        static readonly Vector3[] corners = new Vector3[4];
        readonly List<RectTransform> panelRts = new List<RectTransform>();
        readonly List<CanvasGroup> panelCgs = new List<CanvasGroup>();
        int rootChildren = -1;
        readonly Rect[] obs = new Rect[48]; int obsN;
        Transform zodCanvas; Rect zod; bool zodOn;
        readonly Rect[] tileRect = new Rect[64]; readonly int[] tileRow = new int[64];
        readonly float[] blk0 = new float[48], blk1 = new float[48], cand = new float[100];

        Rect CanvasRect(RectTransform rt)
        {
            rt.GetWorldCorners(corners);
            Vector3 a = hud.Root.InverseTransformPoint(corners[0]), b = hud.Root.InverseTransformPoint(corners[2]);
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }

        void GatherPanels()
        {
            panelRts.Clear(); panelCgs.Clear();
            rootChildren = hud.Root.childCount;
            for (int i = 0; i < rootChildren; i++)
            {
                var ch = hud.Root.GetChild(i) as RectTransform;
                if (!ch || ch == root) continue;
                string n = ch.name; bool panel = false;
                for (int k = 0; k < PanelNames.Length && !panel; k++) panel = n == PanelNames[k];
                for (int k = 0; k < PanelPrefixes.Length && !panel; k++) panel = n.StartsWith(PanelPrefixes[k], StringComparison.Ordinal);
                if (!panel) continue;
                panelRts.Add(ch); panelCgs.Add(ch.GetComponent<CanvasGroup>());
            }
        }

        void Place(TileRef hov)
        {
            if (hud.Root.childCount != rootChildren) GatherPanels();
            var cs = hud.CanvasSize; float cw = cs.x, ch = cs.y;
            var winRt = hud.Production.WindowRect;
            Rect win = CanvasRect(winRt);
            float cur = Mathf.Clamp(winRt.localScale.x, 0.4f, 2f);                       // the menu's own scale: the card is drawn at it

            obsN = 0;
            for (int i = 0; i < panelRts.Count && obsN < obs.Length; i++)
            {
                var rt = panelRts[i]; if (!rt || !rt.gameObject.activeInHierarchy) continue;
                var g = panelCgs[i]; if (g && g.alpha < 0.02f) continue;                   // a faded-out toast or list is not there
                if (rt.TryGetComponent<Canvas>(out var pc) && !pc.enabled) continue;       // nor is a panel whose own canvas is switched off: the lower panels and the minimap while the build menu is open (OpenZHud.LowerPanels)
                var r = CanvasRect(rt); if (r.width < 1f || r.height < 1f) continue;
                obs[obsN++] = r;
            }
            int obsHud = obsN;                                                             // the HUD panels end here
            zodOn = ZodRect(out zod);                                                      // the Zod transmission window, while it is up
            if (zodOn && obsN < obs.Length) obs[obsN++] = zod;

            int n = 0, hi = -1;
            for (int i = 0; i < tiles.Count && n < tileRect.Length; i++)
            {
                var t = tiles[i]; if (t.btn == null || t.btn.Dead || !t.btn.Rt) continue;
                tileRect[n] = CanvasRect(t.btn.Rt); tileRow[n] = t.row; if (t == hov) hi = n; n++;
            }
            if (hi < 0) return;
            Rect tr = tileRect[hi];
            float gridTop = tr.yMax, gridBot = tr.yMin, nbTop = tr.yMax, nbBot = tr.yMin;
            for (int i = 0; i < n; i++)
            {
                gridTop = Mathf.Max(gridTop, tileRect[i].yMax); gridBot = Mathf.Min(gridBot, tileRect[i].yMin);
                if (Mathf.Abs(tileRow[i] - hov.row) <= 1) { nbTop = Mathf.Max(nbTop, tileRect[i].yMax); nbBot = Mathf.Min(nbBot, tileRect[i].yMin); }
            }
            float tcx = tr.center.x, tcy = tr.center.y;

            float x = 0f, top = 0f, sc = cur; int how = 0;
            // first with the Zod window as one more thing to stay clear of; if no place is left then (a narrow screen at a big
            // interface scale), once more without it: the HUD panels and the portraits come first, the card may lie on that window
            for (int pass = 0; pass < 2 && how == 0; pass++)
            {
                if (pass == 1) { if (!zodOn) break; zodOn = false; obsN = obsHud; }
                for (int s = 0; s < Steps.Length && how == 0; s++)
                {
                    sc = cur * Steps[s];
                    float w = W * sc, h = H * sc, sw = ShadowX * sc, sh = ShadowY * sc;
                    // 1 right of the menu, 2 left of it: level with the portrait, slid clear of the HUD panels
                    x = win.xMax + GapWin;
                    if (x + w + sw <= cw - Edge && SlideY(x, w, h, sw, sh, tr.yMax, ch, out top)) { how = 1; break; }
                    x = win.xMin - GapWin - w - sw;
                    if (x >= Edge && SlideY(x, w, h, sw, sh, tr.yMax, ch, out top)) { how = 2; break; }
                    // 3-6 over the menu itself (it is drawn over every HUD panel it overlaps, so inside it the card covers nothing
                    // new): above / below the whole portrait grid, then above / below the hovered portrait's own rows
                    float xlo = win.xMin + Inset, xhi = win.xMax - Inset - w - sw;
                    if (xhi < xlo) continue;
                    x = Mathf.Clamp(tcx - w * 0.5f, xlo, xhi);
                    // a row's class word (ROBOTS / VEHICLES / CANNONS) sits just over the row: a card above a row stays above that word
                    // too, a card below a row starts above the next row's word and covers it whole. Its edge never runs through a word.
                    float gapUp = GapTile + (OzLayout.ProdTileY - OzLayout.ProdClassY) * cur, gapDn = Mathf.Min(GapTile, 4f * cur);
                    float up = gridTop + gapUp + sh + h, dn = gridBot - gapDn;
                    bool upFirst = Mathf.Abs(up - h * 0.5f - tcy) <= Mathf.Abs(dn - h * 0.5f - tcy);
                    if (OverMenu(x, upFirst ? up : dn, w, h, sw, sh, win, ch)) { top = upFirst ? up : dn; how = upFirst ? 3 : 4; break; }
                    if (OverMenu(x, upFirst ? dn : up, w, h, sw, sh, win, ch)) { top = upFirst ? dn : up; how = upFirst ? 4 : 3; break; }
                    up = nbTop + gapUp + sh + h; dn = nbBot - gapDn;
                    upFirst = Mathf.Abs(up - h * 0.5f - tcy) <= Mathf.Abs(dn - h * 0.5f - tcy);
                    if (OverMenu(x, upFirst ? up : dn, w, h, sw, sh, win, ch)) { top = upFirst ? up : dn; how = upFirst ? 5 : 6; break; }
                    if (OverMenu(x, upFirst ? dn : up, w, h, sw, sh, win, ch)) { top = upFirst ? dn : up; how = upFirst ? 6 : 5; break; }
                }
            }
            if (how == 0)
            {   // last resort (the sweep and the proof show it is never reached): right of the menu, held inside the screen
                sc = cur * Steps[Steps.Length - 1];
                float w = W * sc, h = H * sc;
                x = Mathf.Min(Mathf.Max(win.xMax + GapWin, Edge), cw - Edge - w - ShadowX * sc);
                top = Mathf.Min(Mathf.Max(tr.yMax, Edge + ShadowY * sc + h), ch - Edge);
                how = 7; LastResortCount++;
            }
            float k = OzPx.K;                                                             // whole screen pixels: crisp edges and text
            x = Mathf.Round(x * k) / k; top = Mathf.Round(top * k) / k;
            var p = new Vector2(x, top);
            if (root.anchoredPosition != p) root.anchoredPosition = p;
            if (!Mathf.Approximately(root.localScale.x, sc)) root.localScale = new Vector3(sc, sc, 1f);
            if (Mathf.Abs(k * sc - linesK) > 0.0005f) LayLines(k * sc);
            How = how; CardScale = sc;
        }

        /// <summary>The top edge nearest to `desired` at which a card w x h in the column starting at x is on the screen and clear
        /// (GapObs) of every HUD panel; false when the column has no such place.</summary>
        bool SlideY(float x, float w, float h, float sw, float sh, float desired, float ch, out float top)
        {
            float lo = Edge + sh + h, hi = ch - Edge; top = 0f;
            if (hi < lo - 0.0001f) return false;
            int nb = 0;
            for (int i = 0; i < obsN; i++)
            {
                var o = obs[i];
                if (x < o.xMax + GapObs && x + w + sw > o.xMin - GapObs) { blk0[nb] = o.yMin - GapObs; blk1[nb] = o.yMax + GapObs + h + sh; nb++; }
            }
            int nc = 0;
            cand[nc++] = Mathf.Clamp(desired, lo, hi); cand[nc++] = lo; cand[nc++] = hi;
            for (int i = 0; i < nb; i++) { cand[nc++] = blk0[i]; cand[nc++] = blk1[i]; }
            bool ok = false; float best = 0f;
            for (int i = 0; i < nc; i++)
            {
                float c = cand[i];
                if (c < lo - 0.0001f || c > hi + 0.0001f) continue;
                bool blocked = false;
                for (int j = 0; j < nb && !blocked; j++) blocked = c > blk0[j] + 0.0001f && c < blk1[j] - 0.0001f;
                if (blocked) continue;
                if (!ok || Mathf.Abs(c - desired) < Mathf.Abs(best - desired) - 0.0001f) { ok = true; best = c; }
            }
            top = best;
            return ok;
        }

        /// <summary>A card over the menu with its top edge at `top`: on the screen, and whatever sticks out above or below the menu
        /// is clear of every HUD panel.</summary>
        bool OverMenu(float x, float top, float w, float h, float sw, float sh, Rect win, float ch)
        {
            float bot = top - h - sh;
            if (top > ch - Edge + 0.0001f || bot < Edge - 0.0001f) return false;
            // the Zod window is drawn over the menu as well: clear of all of it, not only of what sticks out of the menu
            if (zodOn && x - GapObs < zod.xMax && zod.xMin < x + w + sw + GapObs && bot - GapObs < zod.yMax && zod.yMin < top + GapObs) return false;
            if (top > win.yMax && Hits(x - GapObs, win.yMax, x + w + sw + GapObs, top + GapObs)) return false;
            if (bot < win.yMin && Hits(x - GapObs, bot - GapObs, x + w + sw + GapObs, win.yMin)) return false;
            return true;
        }

        /// <summary>The in-game Zod transmission window (ZodComms "ZodWindow", on a canvas of its own under ZodBriefing that is drawn
        /// over the HUD and over the build menu), in HUD canvas units; false while it is closed. Found by name: ZodBriefing keeps
        /// its ZodComms to itself. Two child look-ups a frame while a card is up, nothing allocated.</summary>
        bool ZodRect(out Rect r)
        {
            r = default;
            if (!zodCanvas)
            {
                var zb = ZodBriefing.I; if (!zb) return false;
                zodCanvas = zb.transform.Find("ZodComms"); if (!zodCanvas) return false;
            }
            var w = zodCanvas.Find("ZodWindow") as RectTransform;
            if (!w || !w.gameObject.activeInHierarchy) return false;
            if (w.TryGetComponent<CanvasGroup>(out var g) && g.alpha < 0.02f) return false;
            r = CanvasRect(w);
            return r.width >= 1f && r.height >= 1f;
        }

        bool Hits(float x0, float y0, float x1, float y1)
        {
            for (int i = 0; i < obsN; i++) { var o = obs[i]; if (x0 < o.xMax && o.xMin < x1 && y0 < o.yMax && o.yMin < y1) return true; }
            return false;
        }
    }
}
