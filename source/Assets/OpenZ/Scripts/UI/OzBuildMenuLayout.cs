using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// k252: the numbers of the build menu's buttons and of the ready-gun tiles, pure layout (no scene objects), used by
    /// OpenZProductionWindow.Buttons.cs, OzReadyGuns.cs and the layout check.
    /// Build menu (window units, top-left origin, the frame of OzLayout.Prod*):
    ///  - the close X 30 % bigger than OzLayout.ProdClose (26 x 24 -> 34 x 31) around the same centre, 4 u clear of the state chip;
    ///  - the bottom row: STOP (slot 1, unchanged), CLEAR (slot 2), SET RALLY over slots 3 + 4 (green, a map pin left of the words);
    ///  - PLACE leaves the bottom row: a tile as tall as the unit tiles, PlaceW wide, right after the last gun of the CANNONS row (every
    ///    build list has one, the gatling from level 0 on); a list without a cannons row puts it after the last tile of its last row,
    ///    or on a row of its own when that row is full.
    /// Ready guns (badge units: 1 u = the production badge's own unit on screen, so the tiles follow the badge at every HUD scale):
    /// tiles ReadyW x ReadyH, ReadyLead right of the badge, ReadyGap apart, their top level with the medallion's top, in the order the
    /// guns were finished; a row that does not fit on the screen wraps under the first; when not even one tile fits right of the
    /// badge the block goes to its left; a block over a badge, another building's tiles or the screen edge moves down a row at a time.
    /// </summary>
    public static class OzBuildMenuLayout
    {
        // ------------------------------------------------------------------ the build menu (window units)
        public static readonly OzRect Close = new OzRect(404, 9, 34, 31);
        public const float CloseIconX = 8, CloseIconY = 6.5f, CloseIconSize = 18;
        public static OzRect Stop => Slot(0, 1);
        public static OzRect Clear => Slot(1, 1);
        public static OzRect Rally => Slot(2, 2);
        public const float RallyPinSize = 16, RallyPinGap = 7, RallyFont = 13, RallyRim = 1.5f;
        public const float PlaceW = 80, PlaceIconSize = 44, PlaceIconY = 5, PlaceWordMid = 63;
        /// <summary>The unit tiles' right margin (7 tiles end at 431; the hints end at 438).</summary>
        public const float PlaceRight = OzLayout.ProdW - 15f;

        static OzRect Slot(int i, int n) => new OzRect(OzLayout.ProdActionX0 + OzLayout.ProdActionStep * i, 0,
                                                       OzLayout.ProdActionW + OzLayout.ProdActionStep * (n - 1), OzLayout.ProdActionH);

        public static float PlaceX(int col) => OzLayout.ProdTileX0 + OzLayout.ProdTileStep * col;
        public static bool PlaceFits(int col) => PlaceX(col) + PlaceW <= PlaceRight + 0.01f;

        /// <summary>The unit tile rows (one row per class, OzLayout.ProdTileCols a row, as OpenZProductionWindow.RebuildTiles) and the
        /// PLACE tile's column / row. Returns the row count with the PLACE tile (one more than the tiles' rows only when the last row
        /// has no room for it, which no build list of the game needs).</summary>
        public static int PlaceSlot(int robots, int vehicles, int cannons, out int col, out int row)
        {
            int cols = (int)OzLayout.ProdTileCols, rows = 0, lastRow = -1, lastCount = 0;
            int[] n = { robots, vehicles, cannons };
            for (int c = 0; c < 3; c++)
            {
                if (n[c] <= 0) continue;
                rows += (n[c] + cols - 1) / cols;
                lastRow = rows - 1; lastCount = (n[c] - 1) % cols + 1;        // the class's last row (the CANNONS row when there is one)
            }
            if (lastRow < 0) { col = 0; row = 0; return 1; }
            if (PlaceFits(lastCount)) { col = lastCount; row = lastRow; return rows; }
            col = 0; row = rows; return rows + 1;
        }

        // ------------------------------------------------------------------ ready guns (badge units)
        public const float ReadyW = 56, ReadyH = 60, ReadyLead = 10, ReadyGap = 8, ReadyEdge = 8, ReadyIcon = 40, ReadyIconY = 4, ReadyWordMid = 50;

        /// <summary>The badge's own unit in screen pixels from its drawn rect (OpenZUnitOverlay.BadgeRects): the rect's height is the part
        /// above the medallion centre (its radius, or the cue flag's height when a flag shows) plus the notch tip and the shadow.</summary>
        public static float BadgeUnit(Rect badge, bool flag) =>
            badge.height / ((flag ? Mathf.Max(OzLayout.TagR, -OzLayout.TagFlagY) : OzLayout.TagR) + OzLayout.TagNotchTip + OzLayout.TagShadowY);

        static readonly List<Rect> trial = new List<Rect>();

        /// <summary>Screen-pixel rects (origin bottom-left) of `count` ready tiles beside a badge drawn at `badge` with `k` pixels per
        /// badge unit. `blocked` = what no tile may cover (every badge and level tag drawn, the tiles placed for other buildings).
        /// Returns how many tiles still overlap something or leave the screen (0 = clean; then the first clean candidate is used).</summary>
        public static int ReadyTiles(Rect badge, float k, int count, float screenW, float screenH, List<Rect> blocked, List<Rect> output)
        {
            output.Clear();
            if (count <= 0 || k <= 0f) return 0;
            int best = int.MaxValue;
            for (int side = 0; side < 2; side++)
                for (int drop = 0; drop <= 4; drop++)
                {
                    int bad = Candidate(badge, k, count, screenW, screenH, side, drop, blocked, trial);
                    if (bad < best) { best = bad; output.Clear(); output.AddRange(trial); }
                    if (bad == 0) return 0;
                }
            return best;
        }

        static int Candidate(Rect badge, float k, int count, float sw, float sh, int side, int drop, List<Rect> blocked, List<Rect> res)
        {
            res.Clear();
            float w = ReadyW * k, h = ReadyH * k, gap = ReadyGap * k, edge = ReadyEdge * k;
            float x0 = side == 0 ? badge.xMax + ReadyLead * k : badge.xMin - ReadyLead * k - w;
            float room = side == 0 ? sw - edge - x0 : x0 + w - edge;                       // from the first tile's near edge to the screen edge
            int per = Mathf.Max(1, Mathf.Min(count, Mathf.FloorToInt((room + gap) / (w + gap) + 0.0001f)));
            int rows = (count + per - 1) / per;
            float top = badge.yMin + (OzLayout.TagNotchTip + OzLayout.TagShadowY + OzLayout.TagR) * k - drop * (h + gap);   // the medallion's top
            float bottom = top - rows * h - (rows - 1) * gap;
            if (bottom < edge) top += edge - bottom;                                        // never under the bottom edge
            if (top > sh - edge) top = sh - edge;                                           // nor over the top edge
            int bad = 0;
            for (int i = 0; i < count; i++)
            {
                int r = i / per, c = i % per;
                float x = side == 0 ? x0 + c * (w + gap) : x0 - c * (w + gap);
                var t = new Rect(Mathf.Round(x), Mathf.Round(top - r * (h + gap) - h), Mathf.Round(w), Mathf.Round(h));
                res.Add(t);
                bool off = t.xMin < 0f || t.yMin < 0f || t.xMax > sw || t.yMax > sh;
                bool over = Hits(t, badge);
                for (int j = 0; j < blocked.Count && !over; j++) over = Hits(t, blocked[j]);
                if (off || over) bad++;
            }
            return bad;
        }

        /// <summary>Overlap with a 1 px tolerance (touching is not overlapping).</summary>
        public static bool Hits(Rect a, Rect b) => a.xMin < b.xMax - 1f && a.xMax > b.xMin + 1f && a.yMin < b.yMax - 1f && a.yMax > b.yMin + 1f;
    }
}
