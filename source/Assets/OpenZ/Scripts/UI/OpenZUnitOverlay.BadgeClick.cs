using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// K429: a click on a building's production badge is a click on that building. The badge (the medallion with the unit's picture,
    /// the time and TECH plate, the factory reminder's flashing ring and "!" chip; or the small TECH tag of a factory without a badge)
    /// is drawn about 7 m above the building, mostly above the building's own click rectangle (PlayerInput.ScreenRect: the model's box),
    /// so a click on it reached the map: the selection went, and no build menu opened. Players click the flashing badge first, since
    /// its tip says "CLICK TO SET PRODUCTION".
    ///
    /// DrawBadges notes every drawn badge's screen rect here (the rect it already keeps in BadgeRects) and, while the reminder shows,
    /// the boxes of its ring and of its "!" chip, which stand out over the medallion. A badge drawn at less than ClickMinAlpha (faded
    /// under a flying shuttle's hull, fading in or out, at the screen edge) is not noted: what shows through it takes the click.
    /// OpenZProductionWindow.ProducerUnder asks BadgeUnder first, so the HUD's press on a building (OpenZHud.Update), the menu that
    /// opens when it is let go (OpenZProductionWindow.Update), the box that starts there (b30) and a tap through the touch layer
    /// (OzTouch.TapIsOrder) treat the badge exactly as the building: the build menu opens with its usual click sound, the selection is
    /// kept. Only the player's own standing forts and factories answer; an enemy's badge stays a part of the map. The badge is drawn
    /// over the units, so a press on it wins over an own unit hidden behind it (OpenZHud.Update, PressOnBadge).
    /// View only: nothing here reads or writes the rules.
    /// </summary>
    public partial class OpenZUnitOverlay
    {
        /// <summary>Off = a click on a badge goes to the map, as before.</summary>
        public static bool BadgeClicks = true;
        /// <summary>A badge drawn at least this opaque takes the click.</summary>
        public static float ClickMinAlpha = 0.5f;
        /// <summary>Play check: left presses (mouse or tap) that landed on an own building's badge.</summary>
        public static int BadgeClickHits;

        struct ClickBox { public int id; public Rect r; }
        static readonly List<ClickBox> clickBoxes = new List<ClickBox>();                               // in draw order: the last one lies on top
        static readonly Dictionary<int, Rect> clickMain = new Dictionary<int, Rect>(), clickChip = new Dictionary<int, Rect>();
        static int clickFrame = -10;

        /// <summary>DrawBadges, first thing: this frame's boxes start empty.</summary>
        void BadgeClickBegin() { clickBoxes.Clear(); clickMain.Clear(); clickChip.Clear(); clickFrame = Time.frameCount; }

        static void AddBox(int id, Rect r) { if (r.width > 0f && r.height > 0f) clickBoxes.Add(new ClickBox { id = id, r = r }); }

        /// <summary>DrawBadges, for a drawn level tag: its rect (screen px, origin bottom-left) and drawn alpha.</summary>
        void BadgeClickNote(int id, Rect px, float alpha)
        {
            if (alpha < ClickMinAlpha || px.width <= 0f || px.height <= 0f) return;
            AddBox(id, px); clickMain[id] = px;
        }

        /// <summary>DrawBadges, for a drawn badge: its rect and alpha; with the reminder on, its ring and "!" chip too (at = the medallion
        /// centre in canvas u, k = its resting scale, as the reminder tip places itself; sf = the canvas scale).</summary>
        void BadgeClickNote(int id, Rect px, float alpha, bool reminder, Vector2 at, float k, float compact, bool flag, float sf)
        {
            if (alpha < ClickMinAlpha || px.width <= 0f || px.height <= 0f) return;
            AddBox(id, px); clickMain[id] = px;
            if (!reminder) return;
            float u = Mathf.Max(0.01f, k) * sf;
            var c = new Vector2(at.x * sf, at.y * sf);
            float ring = OzReminderTip.RingR * u;
            AddBox(id, new Rect(c.x - ring, c.y - ring, 2f * ring, 2f * ring));                          // the amber ring, at its largest
            var chip = OzReminderTip.ChipLocal(compact, flag); float r = OzProductionBadge.RemChipR * OzReminderTip.Lift * u;
            var cc = new Vector2(c.x + chip.x * u, c.y - chip.y * u);                                    // badge-local y runs down, the screen's up
            var cr = new Rect(cc.x - r, cc.y - r, 2f * r, 2f * r);
            AddBox(id, cr); clickChip[id] = cr;                                                          // the "!" chip, at its largest
        }

        /// <summary>The player's own standing fort / factory whose badge (drawn this frame or the last) holds the screen point, or null.</summary>
        public static ZObject BadgeUnder(ZWorld w, Team player, Vector2 mp)
        {
            if (!BadgeClicks || w == null || player == Team.Neutral || Time.frameCount - clickFrame > 1) return null;
            for (int i = clickBoxes.Count - 1; i >= 0; i--)
            {
                var cb = clickBoxes[i];
                if (!cb.r.Contains(mp)) continue;
                var b = w.Get(cb.id);
                if (b != null && b.IsProducer && b.Owner == player && !b.Destroyed) return b;
            }
            return null;
        }

        /// <summary>OpenZHud.Update, on a left press over an own producer: true = the press is on its badge, which wins over an own unit
        /// behind it (counted in BadgeClickHits).</summary>
        public static bool PressOnBadge(ZWorld w, Team player, Vector2 mp)
        {
            if (BadgeUnder(w, player, mp) == null) return false;
            BadgeClickHits++;
            return true;
        }

        /// <summary>Play check: this frame's clickable rect of a building's badge body and of its "!" chip (screen px); false = none.</summary>
        public static bool BadgeClickRect(int id, out Rect r) { r = default; return Time.frameCount - clickFrame <= 1 && clickMain.TryGetValue(id, out r); }
        public static bool BadgeChipRect(int id, out Rect r) { r = default; return Time.frameCount - clickFrame <= 1 && clickChip.TryGetValue(id, out r); }
    }
}
