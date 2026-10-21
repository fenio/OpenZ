using UnityEngine;
using UnityEngine.UI;

namespace OpenZ
{
    /// <summary>
    /// Factory reminder (k247), the look on the production badge (rules: OzFactoryReminder.cs): an amber ring on the medallion's own
    /// edge (a crisp line on the bezel's lip, k312) with the badge's own outer glow tinted amber behind the badge's body (so the plate
    /// and its time and TECH row lie on top of it), and a small amber "!" chip at the medallion's upper right, where the plate starts.
    /// Ring, glow and chip bounce together: a smooth, subtle scale cycle every 1.6 s (ease in-out; the chip and the glow 1.00 -> 1.08
    /// -> 1.00, the ring 1.00 -> 1.03 -> 1.00 so the TECH row keeps a clear gap at every UI scale), the glow breathing with it;
    /// reduced motion = still, the glow at its middle. Drawn on the overlay canvas with the
    /// badge (unlit: the same by day and at night), it fades and scales with the badge; while a cue flag (PAUSED / PLACE) sits on
    /// the plate's top edge the chip steps to the medallion's upper left, and in the compact form it sits on the bezel.
    /// </summary>
    public sealed partial class OzProductionBadge
    {
        /// <summary>Badge-local u (origin = the medallion centre, y down): the ring line's radius and width, the chip's radius and its
        /// centre (full plate / compact / with a cue flag), the bounce period (s) and how much it grows (the chip and the glow), and
        /// how much the ring grows. k312: the ring line covers the bezel's lip, from the progress channel's outer edge (TagChO) to the
        /// medallion's own edge (TagR), and grows less than the chip, so at its largest it stays clear of the "T" of TECH (and of the
        /// time) at every resolution and UI scale, edge to edge at least 3.5 px from 1080 lines up; it never covers the progress ring.</summary>
        public const float RemRingR = (OzLayout.TagChO + OzLayout.TagR) * 0.5f, RemRingW = OzLayout.TagR - OzLayout.TagChO, RemChipR = 11f, RemPeriod = 1.6f, RemGrow = 0.08f, RemRingGrow = 0.03f;
        public static readonly Vector2 RemChipAt = new Vector2(35f, -23f), RemChipCompact = new Vector2(21f, -21f), RemChipAside = new Vector2(-35f, -23f);

        Image remGlow, remRing, remChip; int remTier = -1;

        /// <summary>Every frame after Apply for a drawn badge: on = its factory carries the reminder. compact = the view's compact
        /// (0 full plate .. 1 medallion only); flag = a cue flag is on the plate's top edge; sf, s as in Apply.</summary>
        public void Reminder(bool on, float compact, bool flag, float now, float sf, float s)
        {
            on &= art;                                                                              // the flat fallback plate has no medallion to ring
            if (!on)
            {
                if (remGlow != null) { RemShow(remGlow, false); RemShow(remRing, false); RemShow(remChip, false); }
                return;
            }
            if (remGlow == null)
            {   // built on first use, after every other layer: on top of the face, the plate and the texts (the glow: behind them all)
                remGlow = Ring("ReminderGlow", OzLayout.TagGlow);
                remGlow.rectTransform.SetAsFirstSibling();   // k312: behind the badge's body, so its haze never lies on the plate's time and TECH row
                remRing = Ring("ReminderRing", OzReminderArt.RingHalf);
                remChip = Ring("ReminderChip", OzReminderArt.ChipHalf);
            }
            int t = OzBadgeArt.Tier(sf * s);
            if (t != remTier)
            {
                remTier = t;
                SetSprite(remGlow, OzBadgeArt.Get(t, "ring_glow")); SetSprite(remRing, OzReminderArt.Ring(t)); SetSprite(remChip, OzReminderArt.Chip(t));
            }
            bool still = OzMotion.Reduced;
            float k = still ? 0.5f : 0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * Mathf.Repeat(now, RemPeriod) / RemPeriod);   // 0 -> 1 -> 0, ease in-out
            var sc = Vector3.one * (1f + (still ? 0f : RemGrow * k));
            Show(remGlow, true); Show(remRing, true); Show(remChip, true);
            if (remGlow.enabled) { Tint(remGlow, OzUi.A(OzUi.Amber, 0.32f + 0.4f * k)); if (remGlow.rectTransform.localScale != sc) remGlow.rectTransform.localScale = sc; }
            if (remRing.enabled)
            {   // k312: the ring's own, smaller growth (RemRingGrow), in time with the chip and the glow
                Tint(remRing, OzUi.A(OzUi.Amber, 0.78f + 0.2f * k));
                var rs = Vector3.one * (1f + (still ? 0f : RemRingGrow * k));
                if (remRing.rectTransform.localScale != rs) remRing.rectTransform.localScale = rs;
            }
            if (remChip.enabled)
            {
                var at = OzReminderTip.ChipLocal(compact, flag);   // k247b: one formula for the chip and the tip under it (UI/OzReminderTip.cs)
                Move(remChip, at.x, at.y);
                if (remChip.rectTransform.localScale != sc) remChip.rectTransform.localScale = sc;
            }
        }

        static void RemShow(Image i, bool on) { if (i != null && i.enabled != on) i.enabled = on; }
    }

    /// <summary>Factory reminder (k247): the ring line and the "!" chip, drawn in code once per badge tier (t px per u, like the
    /// badge art x1..x4): crisp at every UI scale, no import step. The chip: a dark keyline, an amber rim, a deep amber face and a
    /// cream "!"; the ring: a white line (tinted amber by the badge).</summary>
    public static class OzReminderArt
    {
        /// <summary>Half sizes of the two images in u: the drawn shape plus room for the soft edge, whole u so that the images sit on
        /// whole pixels around the medallion centre and the textures are drawn 1:1.</summary>
        public const float RingHalf = 32f, ChipHalf = 13f;
        static readonly Sprite[] rings = new Sprite[OzBadgeArt.MaxTier + 1], chips = new Sprite[OzBadgeArt.MaxTier + 1];
        static readonly Color32 Key = new Color32(11, 13, 12, 150), Rim = OzUi.Amber, Face = new Color32(150, 100, 44, 245), Bang = new Color32(252, 240, 190, 255);

        public static Sprite Ring(int t)
        {
            t = Mathf.Clamp(t, 1, OzBadgeArt.MaxTier);
            if (rings[t] == null) rings[t] = Make("OzReminder_ring_x" + t, RingHalf, t, RingPx);
            return rings[t];
        }

        public static Sprite Chip(int t)
        {
            t = Mathf.Clamp(t, 1, OzBadgeArt.MaxTier);
            if (chips[t] == null) chips[t] = Make("OzReminder_chip_x" + t, ChipHalf, t, ChipPx);
            return chips[t];
        }

        /// <summary>A square texture of half size h u at t px per u; px(x, y, t) gives the colour at (x, y) u from the centre, y up.</summary>
        static Sprite Make(string name, float h, int t, System.Func<float, float, float, Color32> px)
        {
            int n = Mathf.RoundToInt(2f * h * t);
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var c = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                    c[y * n + x] = px((x + 0.5f - n * 0.5f) / t, (y + 0.5f - n * 0.5f) / t, t);
            tex.SetPixels32(c); tex.Apply(false, true);
            var s = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f * t, 0, SpriteMeshType.FullRect);
            s.name = name;
            return s;
        }

        /// <summary>Coverage of a shape whose signed distance (u, negative inside) is d, one pixel of soft edge.</summary>
        static float Cover(float d, float t) => Mathf.Clamp01(0.5f - d * t);

        static Color32 RingPx(float x, float y, float t)
        {
            float d = Mathf.Abs(Mathf.Sqrt(x * x + y * y) - OzProductionBadge.RemRingR) - OzProductionBadge.RemRingW * 0.5f;
            return new Color32(255, 255, 255, (byte)Mathf.RoundToInt(255f * Cover(d, t)));
        }

        static Color32 ChipPx(float x, float y, float t)
        {
            float r = Mathf.Sqrt(x * x + y * y), R = OzProductionBadge.RemChipR;
            float disc = Cover(r - R, t);
            if (disc <= 0f) return new Color32(0, 0, 0, 0);
            // rings from the outside in: keyline 0.7 u, amber rim 1.3 u, deep amber face
            Color c = Color.Lerp(Key, Rim, Cover(r - (R - 0.7f), t));
            c = Color.Lerp(c, Face, Cover(r - (R - 2.0f), t));
            // the "!" (sized to the chip, q = R / 9.5): a bar that narrows downwards (y 0 .. 5 q u, radius 1.25 q -> 0.95 q) and a dot
            // (y -3.4 q u, radius 1.3 q)
            float q = R / 9.5f, yc = Mathf.Clamp(y, 0f, 5f * q), rb = q * Mathf.Lerp(0.95f, 1.25f, yc / (5f * q));
            float bar = Mathf.Sqrt(x * x + (y - yc) * (y - yc)) - rb;
            float dot = Mathf.Sqrt(x * x + (y + 3.4f * q) * (y + 3.4f * q)) - 1.3f * q;
            c = Color.Lerp(c, Bang, Cover(Mathf.Min(bar, dot), t));
            c.a *= disc;
            return c;
        }
    }
}
