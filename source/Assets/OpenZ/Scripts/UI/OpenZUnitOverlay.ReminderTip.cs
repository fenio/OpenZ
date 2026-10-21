using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Factory reminder (k247b), the tip's place (math: OzReminderTip.cs): DrawBadges hands every marked badge's drawn place to
    /// ReminderTipAnchor (after the neighbour nudge and the edge rule, on whole pixels), and the mouseover tip (FactoryReminderTip)
    /// hangs under that badge's "!" chip, aligned to it, following the badge as the camera moves. Only while the factory's badge is
    /// not drawn (its anchor is off screen) does the tip keep its old place beside the pointer. HUD panels: the tip never lies on
    /// one when it can go above the "!" instead.
    /// </summary>
    public partial class OpenZUnitOverlay
    {
        struct RemTipAnchor { public Vector2 at; public float k, compact, plateW; public bool flag; }
        readonly Dictionary<int, RemTipAnchor> remTipAnchors = new Dictionary<int, RemTipAnchor>(); int remTipAnchorFrame = -1;
        System.Func<Rect, bool> remTipHud; float remTipSf = 1f;
        /// <summary>Play check: frames the tip hung under the "!" / went above it / stayed under it kept on screen / sat beside the
        /// pointer (no badge drawn); its last rect (screen px, origin bottom-left); the last gap between the badge and the tip (px).</summary>
        public static int ReminderTipUnder, ReminderTipAbove, ReminderTipClamped, ReminderTipBeside; public static Rect ReminderTipRect; public static float ReminderTipGapPx;

        /// <summary>DrawBadges, for every drawn badge that carries the mark: its medallion centre (canvas u, whole pixels), its resting
        /// scale, the compact amount, whether a cue flag shows, the plate width.</summary>
        void ReminderTipAnchor(int id, Vector2 at, float k, float compact, bool flag, float plateW)
        {
            if (remTipAnchorFrame != Time.frameCount) { remTipAnchorFrame = Time.frameCount; remTipAnchors.Clear(); }
            remTipAnchors[id] = new RemTipAnchor { at = at, k = k, compact = compact, flag = flag, plateW = plateW };
        }

        /// <summary>FactoryReminderTip: the tip's top-left corner (canvas u) under the "!" of this factory's badge; false = its badge is
        /// not drawn this frame (the caller keeps the place beside the pointer).</summary>
        bool ReminderTipUnderBadge(int id, Vector2 size, Vector2 cs, float sf, out Vector2 p)
        {
            p = default;
            if (remTipAnchorFrame != Time.frameCount || !remTipAnchors.TryGetValue(id, out var a)) { ReminderTipBeside++; return false; }
            remTipSf = sf; if (remTipHud == null) remTipHud = ReminderTipOnHud;                                   // one delegate, no per-frame allocation
            p = OzReminderTip.Place(a.at, a.k, a.compact, a.flag, a.plateW, size, cs, OzLayout.M, remTipHud, out int side, out float edge);
            p = a.at + SnapU(p - a.at, sf);                                    // whole pixels from the badge (itself on whole pixels): it moves with it, no 1 px wobble
            if (side == OzReminderTip.Under) ReminderTipUnder++; else if (side == OzReminderTip.Above) ReminderTipAbove++; else ReminderTipClamped++;
            var q = SnapU(p, sf);
            ReminderTipRect = new Rect(q.x * sf, (q.y - size.y) * sf, size.x * sf, size.y * sf);
            ReminderTipGapPx = (side == OzReminderTip.Above ? q.y - size.y - edge : edge - q.y) * sf;
            return true;
        }

        /// <summary>The tip's rect (canvas u) lies on a HUD panel: its corners and edge middles tested against the HUD's pointer blockers.</summary>
        bool ReminderTipOnHud(Rect r)
        {
            if (hud == null) return false;
            float s = remTipSf;
            for (int n = 0; n < 6; n++)
            {
                float x = n % 3 == 0 ? r.xMin + 1f : n % 3 == 1 ? r.center.x : r.xMax - 1f, y = n < 3 ? r.yMin + 1f : r.yMax - 1f;
                if (hud.IsOverUi(new Vector2(x * s, y * s))) return true;
            }
            return false;
        }
    }
}
