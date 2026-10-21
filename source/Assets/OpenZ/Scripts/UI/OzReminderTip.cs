using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Factory reminder (k247b): where the mouseover tip goes. It hangs under the badge's amber "!" chip: the tooltip panel's amber
    /// edge bar stands straight under the "!" (the panel's left edge BarMid u left of the chip's centre), and the panel's top edge
    /// keeps Gap u clear of the badge every way (below the plate under the "!"; with a cue flag or in the compact form below the
    /// medallion with its reminder ring and the notch), so the time and the TECH row stay readable. The overlay calls it every frame
    /// with the badge's drawn place, so the tip follows the badge as the camera moves. It is kept `margin` u inside the screen; where it
    /// would leave the screen at the bottom or lie on a HUD panel it goes above the "!" instead (Above); where a HUD panel takes both
    /// it slides left along the badge, the "!" still over it, or drops a little lower; where nothing is free it stays at the "!", on
    /// screen and clear of the badge (Clamped).
    /// Canvas units: origin bottom-left, y up, the panel's pivot top-left. Badge-local u: origin the medallion centre, y down (as in
    /// OzProductionBadge). Only Vector2 / Rect / Mathf: the math is checked off-engine.
    /// </summary>
    public static class OzReminderTip
    {
        /// <summary>Gap = u between the badge and the tip (the badges' own spacing); BarMid = the middle of the tooltip panel's amber
        /// edge bar from its left edge (Panels/tooltip: px 4..9 of 96, 2 px per u); Lift = the chip's largest bounce scale; RingR =
        /// the reminder ring's outer edge at its own largest scale (k312: on the medallion's edge TagR at rest, RemRingGrow).</summary>
        public const float Gap = OzLayout.TagGap, BarMid = 3.5f, Lift = 1f + OzProductionBadge.RemGrow;
        public const float RingR = (OzProductionBadge.RemRingR + OzProductionBadge.RemRingW * 0.5f) * (1f + OzProductionBadge.RemRingGrow);

        /// <summary>Place's answer: under the badge, above the "!", or nothing free from the HUD panels (Clamped: at the "!", on screen,
        /// clear of the badge).</summary>
        public const int Under = 0, Above = 1, Clamped = 2;

        /// <summary>The "!" chip's centre at rest (badge-local u, y down): at the plate's start, on the bezel in the compact form,
        /// mirrored to the medallion's upper left while a cue flag (PAUSED / PLACE) sits on the plate. OzProductionBadge.Reminder
        /// draws the chip here.</summary>
        public static Vector2 ChipLocal(float compact, bool flag) =>
            Vector2.Lerp(flag ? OzProductionBadge.RemChipAside : OzProductionBadge.RemChipAt,
                flag ? new Vector2(-OzProductionBadge.RemChipCompact.x, OzProductionBadge.RemChipCompact.y) : OzProductionBadge.RemChipCompact, Mathf.Clamp01(compact));

        /// <summary>The lowest drawn point of the badge over the columns x0..x1 (badge-local u, y down): the plate with its shadow
        /// (while it shows), the medallion with the reminder ring at its largest, the notch with its shadow, the chip at its largest;
        /// a part whose edge column the panel only touches counts too (the safe side).</summary>
        public static float Bottom(float x0, float x1, float compact, bool flag, float plateW)
        {
            float y = 0f;
            if (compact < 0.996f && x1 >= 0f && x0 <= plateW + OzLayout.TagShadowX) y = OzLayout.TagPlateH * 0.5f + OzLayout.TagShadowY;
            float m = x0 > 0f ? x0 : x1 < 0f ? -x1 : 0f;                                                   // the column nearest the centre
            if (m <= RingR) y = Mathf.Max(y, Mathf.Sqrt(RingR * RingR - m * m));
            float n = OzLayout.TagNotchW * 0.5f;
            if (x1 >= -n && x0 <= n) y = Mathf.Max(y, OzLayout.TagNotchTip + OzLayout.TagShadowY);
            var c = ChipLocal(compact, flag); float r = OzProductionBadge.RemChipR * Lift;
            if (x1 >= c.x - r && x0 <= c.x + r) y = Mathf.Max(y, c.y + r);
            return y;
        }

        /// <summary>The highest drawn point of the badge over the columns x0..x1, as u above the centre: the plate's top edge (while it
        /// shows), the medallion with the reminder ring at its largest, the chip at its largest, a cue flag on the plate's top edge.</summary>
        public static float Top(float x0, float x1, float compact, bool flag, float plateW)
        {
            float y = 0f;
            bool plate = compact < 0.996f;
            if (plate && x1 >= 0f && x0 <= plateW + OzLayout.TagShadowX) y = OzLayout.TagPlateH * 0.5f;
            float m = x0 > 0f ? x0 : x1 < 0f ? -x1 : 0f;
            if (m <= RingR) y = Mathf.Max(y, Mathf.Sqrt(RingR * RingR - m * m));
            var c = ChipLocal(compact, flag); float r = OzProductionBadge.RemChipR * Lift;
            if (x1 >= c.x - r && x0 <= c.x + r) y = Mathf.Max(y, -c.y + r);
            if (flag && plate && x1 >= OzLayout.TagFlagX - 1f && x0 <= plateW + OzLayout.TagShadowX) y = Mathf.Max(y, 1f - OzLayout.TagFlagY);
            return y;
        }

        /// <summary>The tip panel's top-left corner (canvas u) for a badge drawn with its medallion centre at `at` (canvas u) and its
        /// resting scale k; compact / flag / plateW as the badge shows them; size = the panel, canvas = the canvas size, margin = the
        /// screen margin; blocked(rect in canvas u) = it lies on a HUD panel (null: no HUD). First at the "!": under the badge, else
        /// above the "!"; where a HUD panel takes both, the panel is nudged right (its bar still under the chip's rim), else slides
        /// left along the badge in Slide steps (the "!" stays over it), else drops lower under the "!" (up to Drops steps); where none
        /// is free (the badge squeezed between panels), it stays at the "!", under or above the badge on screen. side = Under / Above /
        /// Clamped; edge = the badge's edge the gap is kept to (canvas y: its bottom over the panel's columns, or its top when Above).</summary>
        public static Vector2 Place(Vector2 at, float k, float compact, bool flag, float plateW, Vector2 size, Vector2 canvas, float margin,
                                    System.Func<Rect, bool> blocked, out int side, out float edge)
        {
            k = Mathf.Max(0.01f, k);
            var c = ChipLocal(compact, flag);
            float chipX = at.x + c.x * k, lo = margin, hi = Mathf.Max(margin, canvas.x - margin - size.x);
            float x = Mathf.Clamp(chipX - BarMid, lo, hi);                                                 // the edge bar under the "!"
            if (Try(at, k, compact, flag, plateW, size, canvas, margin, blocked, x, out var p, out side, out edge)) return p;
            if (blocked != null)
            {   // a HUD panel takes both: a nudge right (the bar still under the chip's rim), then left in steps (the "!" still over the panel)
                float maxX = Mathf.Clamp(chipX + OzProductionBadge.RemChipR * k - BarMid, x, hi), minX = Mathf.Clamp(chipX + BarMid - size.x, lo, x);
                if (maxX > x + 0.01f && Try(at, k, compact, flag, plateW, size, canvas, margin, blocked, maxX, out p, out side, out edge)) return p;
                for (float xs = x - Slide; xs > minX - Slide; xs -= Slide)
                    if (Try(at, k, compact, flag, plateW, size, canvas, margin, blocked, Mathf.Max(xs, minX), out p, out side, out edge)) return p;
            }
            edge = at.y - Bottom((x - Gap - at.x) / k, (x + size.x + Gap - at.x) / k, compact, flag, plateW) * k;
            for (int i = 1; i <= Drops && blocked != null; i++)                                            // still under the "!", a little lower (off a panel beside the badge)
            {
                float y = edge - Gap - i * Slide;
                if (y - size.y < margin) break;
                if (!Hit(blocked, x, y, size)) { side = Under; return new Vector2(x, y); }
            }
            // nothing free (the badge squeezed between HUD panels): at the "!", under or above it on screen, clear of the badge
            if (Try(at, k, compact, flag, plateW, size, canvas, margin, null, x, out p, out _, out edge)) { side = Clamped; return p; }
            side = Clamped;
            return new Vector2(x, Mathf.Clamp(edge - Gap, margin + size.y, Mathf.Max(margin + size.y, canvas.y - margin)));
        }

        /// <summary>The step (u) the panel slides left, or drops lower, when a HUD panel takes both places at the "!"; Drops = how many
        /// steps lower it may go.</summary>
        public const float Slide = 12f; public const int Drops = 8;

        /// <summary>The panel with its left edge at x: under the badge if that is on screen and free, else above it; false = neither.</summary>
        static bool Try(Vector2 at, float k, float compact, bool flag, float plateW, Vector2 size, Vector2 canvas, float margin,
                        System.Func<Rect, bool> blocked, float x, out Vector2 p, out int side, out float edge)
        {
            float x0 = (x - Gap - at.x) / k, x1 = (x + size.x + Gap - at.x) / k;                           // the badge columns the panel spans, Gap wider each side:
            float low = at.y - Bottom(x0, x1, compact, flag, plateW) * k, high = at.y + Top(x0, x1, compact, flag, plateW) * k;   // Gap clear every way, also at its corners
            float under = low - Gap, above = high + Gap + size.y;
            if (under - size.y >= margin && !Hit(blocked, x, under, size)) { side = Under; edge = low; p = new Vector2(x, under); return true; }
            if (above <= canvas.y - margin && !Hit(blocked, x, above, size)) { side = Above; edge = high; p = new Vector2(x, above); return true; }
            side = Clamped; edge = low; p = new Vector2(x, under);
            return false;
        }

        static bool Hit(System.Func<Rect, bool> blocked, float x, float top, Vector2 size) => blocked != null && blocked(new Rect(x, top - size.y, size.x, size.y));
    }
}
