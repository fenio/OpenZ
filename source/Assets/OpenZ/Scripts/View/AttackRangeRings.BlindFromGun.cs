using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// On top of
    /// AttackRangeRings.BlindSectors (same sectors, the rules' own cells, redrawn when a blocker goes):
    /// <list type="bullet">
    /// <item>the side lines start at the gun's base and run straight past the blocker's edge out to the ring (OzBlindSectorsCore
    /// .FromGun.cs: thin pale dashes, one even line from the gun; a wedge too small to matter gets none);</item>
    /// <item>the blind ground gets a pale cool veil instead of a darkening (blindVeilDay / blindVeilNight, its strength blindDarkDay /
    /// blindDarkNight): a faint haze that greys the ground a little and lifts it, lighter than the ground under it (on dark grass and
    /// at night it lifts, on bright sand it mostly greys), so it never reads as a shadow; it fades in just behind the blocker;</item>
    /// <item>the ring's dots over each blind arc show at about half their brightness (blindRingDim, blindRingFade), eased over the arc's
    /// ends: the ring itself shows where the gun cannot reach;</item>
    /// <item>a small x in the team's colour in every big blind wedge (blindMarks), faint.</item>
    /// </list>
    /// Calm: nothing pulses; the ring's dots keep their slow crawl and pass softly in and out of a dim arc. View only.
    /// </summary>
    public partial class AttackRangeRings
    {
        [Header("Blind sectors seen from the gun")]
        [Tooltip("The grey over the blind ground by day / by night (linear colour, before the tone mapping; its strength: blindDarkDay / blindDarkNight; K333c: a slightly darker neutral grey, darker than the ground under it)")]
        public Color blindVeilDay = new Color(0.03f, 0.031f, 0.034f, 1f), blindVeilNight = new Color(0.012f, 0.012f, 0.014f, 1f);
        [Tooltip("The ring's dots over a blind arc: their colour x this (the dots are HDR and bloom: a darker colour is what dims them on screen; 0.35 with blindRingFade 0.65 leaves them about half their glow over the ground), eased at the arc's ends")] [Range(0f, 1f)] public float blindRingDim = 0.35f;
        [Tooltip("The ring's dots over a blind arc: their alpha x this (a little fainter too)")] [Range(0f, 1f)] public float blindRingFade = 0.65f;
        [Tooltip("The ease at each end of a blind arc (m along the ring)")] public float blindRingEase = 0.9f;
        [Tooltip("A small x in each big blind wedge")] public bool blindMarks = true;
        [Tooltip("The marks' strength by day / by night (x the ring's alpha)")] [Range(0f, 1f)] public float blindMarkDay = 0.5f, blindMarkNight = 0.42f;
        [Tooltip("A mark's arm (m at 40 m camera distance, it grows with the zoom like the dots): length, width")] public float blindMarkLength = 0.3f, blindMarkWidth = 0.07f;
        [Tooltip("The marks' colour: a pale tint, this much of the team colour")] [Range(0f, 1f)] public float blindMarkTeam = 0.85f;

        const int ArcBins = 720;                                                                         // the ring's blind arcs, half a degree a bin
        readonly float[] arcRaw = new float[ArcBins];

        sealed partial class Blind
        {
            public float[] arcDim;                                                                       // per bin of the ring's own angle: 0 open .. 1 on a blind arc (eased)
            public bool arcAny;
            public Color32 markTint;
        }

        /// <summary>After a rebuild of the sectors: the ring's blind arcs (where the ring's line passes over blind ground, the cells just
        /// inside it blind for every gun of the ring) and the marks' tint.</summary>
        void BlindArcs(Blind b, Ring r)
        {
            if (b.arcDim == null) b.arcDim = new float[ArcBins];
            var core = b.core;
            double cx = core.RingX, cy = core.RingY, rr = core.RangePx - 2.0;                               // just inside the ring line (px)
            bool any = false;
            for (int i = 0; i < ArcBins; i++)
            {
                double ang = i * (2.0 * System.Math.PI / ArcBins);                                       // the ring's angle (Build: radial = cos, sin in world x, z)
                double x = cx + System.Math.Cos(ang) * rr, y = cy - System.Math.Sin(ang) * rr;
                bool blind = core.BlindPoint(x, y);
                for (int k = 0; k < b.others.Count && blind; k++) if (!b.others[k].BlindPoint(x, y)) blind = false;
                arcRaw[i] = blind ? 1f : 0f; any |= blind;
            }
            b.arcAny = any;
            int w = Mathf.Clamp(Mathf.RoundToInt(blindRingEase / Mathf.Max(0.5f, r.radius) / (2f * Mathf.PI) * ArcBins), 1, ArcBins / 8);
            float sum = 0f;
            for (int k = -w; k <= w; k++) sum += arcRaw[(k + ArcBins) % ArcBins];
            for (int i = 0; i < ArcBins; i++)
            {   // a running box over +-w bins (a ramp over each arc end), then smoothstep: eased ends
                float s = sum / (2 * w + 1);
                b.arcDim[i] = s * s * (3f - 2f * s);
                sum += arcRaw[(i + w + 1) % ArcBins] - arcRaw[(i - w + ArcBins) % ArcBins];
            }
            Team team = r.team;
            Color tc = team == Team.Red ? TeamColors.DiscRed : team == Team.Blue ? TeamColors.DiscBlue : TeamColors.DiscEmpty;
            float mx = Mathf.Max(1f, Mathf.Max(tc.r, Mathf.Max(tc.g, tc.b)));
            var mc = Color.Lerp(new Color(0.93f, 0.95f, 1f), new Color(tc.r / mx, tc.g / mx, tc.b / mx), blindMarkTeam);
            b.markTint = new Color32((byte)(mc.r * 255f), (byte)(mc.g * 255f), (byte)(mc.b * 255f), 0);
        }

        Ring dimRing; Blind dimBlind; int dimFrame = -1, dimCountFrame = -1;

        /// <summary>This frame's ring dots of rings that show blind sectors / of those, the dots dimmed over a blind arc (the proof).</summary>
        public int BlindRingDots { get; private set; }
        public int BlindDimDots { get; private set; }

        /// <summary>Build's hook: a ring dot's colour at the ring angle ang (rad) with alpha a: white (the ring's own colour) or, over a blind arc
        /// of this ring's own sectors (drawn this frame), darker and fainter (blindRingDim, blindRingFade), eased at the arc's ends.</summary>
        Color32 BlindRingDot(Ring r, float ang, byte a)
        {
            var white = new Color32(255, 255, 255, a);
            if (a == 0 || !blindSectors) return white;
            if (dimRing != r || dimFrame != Time.frameCount)
            {
                dimRing = r; dimFrame = Time.frameCount;
                if (!blinds.TryGetValue(r, out dimBlind) || dimBlind.shownFrame != Time.frameCount || dimBlind.t0 != r.t0 || !dimBlind.arcAny || dimBlind.arcDim == null || dimBlind.nTri == 0) dimBlind = null;
            }
            if (dimBlind == null) return white;
            float f = Mathf.Repeat(ang, 2f * Mathf.PI) / (2f * Mathf.PI) * ArcBins;
            int i = Mathf.FloorToInt(f); float t = f - i; i %= ArcBins; int j = (i + 1) % ArcBins;
            float k = Mathf.Lerp(dimBlind.arcDim[i], dimBlind.arcDim[j], t);
            if (dimCountFrame != Time.frameCount) { dimCountFrame = Time.frameCount; BlindRingDots = BlindDimDots = 0; }
            BlindRingDots++;
            if (k <= 0.001f) return white;
            if (k > 0.5f) BlindDimDots++;
            byte c = Byte(Mathf.Lerp(1f, blindRingDim, k));
            return new Color32(c, c, c, (byte)Mathf.RoundToInt(a * Mathf.Lerp(1f, blindRingFade, k)));
        }

        /// <summary>Build's hook: a dot's soft rim in the dot's own colour (alpha 0), so a darker dot keeps no bright edge.</summary>
        static Color32 BlindRim(Color32 core, Color32 white0) => core.r == 255 && core.g == 255 && core.b == 255 ? white0 : new Color32(core.r, core.g, core.b, 0);

        /// <summary>BuildBlind's strength of dot d on a ring that two or more guns of a type share (the cells blind for all of them are
        /// drawn): a side line stays whole (from the gun) when the blind ground along its edge is blind for every gun of the ring, else it
        /// goes whole; a mark stays when its centre is blind for every gun.</summary>
        float SharedDot(Blind b, int d, float a)
        {
            var core = b.core; var q = core.Dots[d];
            if (q.kind == 1 || q.line < 0 || q.line >= core.Lines.Count)
            {
                for (int k = 0; k < b.others.Count && a > 0f; k++) if (!b.others[k].BlindPoint(q.x, q.y)) a = 0f;
                return a;
            }
            var ln = core.Lines[q.line];
            if (d == ln.firstDot)
            {   // once per line: the middle of its blind edge, 4 px to the blind side
                double ux = System.Math.Cos(ln.th), uy = System.Math.Sin(ln.th), r = 0.5 * (ln.lo + ln.hi);
                double x = core.ApexX + ux * r, y = core.ApexY + uy * r, nx = -uy * 4.0, ny = ux * 4.0;
                if (!core.BlindPoint(x + nx, y + ny)) { nx = -nx; ny = -ny; }
                bool keep = true;
                for (int k = 0; k < b.others.Count && keep; k++) if (!b.others[k].BlindPoint(x + nx, y + ny)) keep = false;
                sharedKeep = keep;
            }
            return sharedKeep ? a : 0f;
        }

        bool sharedKeep;

        /// <summary>Show's colour of vertex i (k: its alpha share 0..1 after the burst): the veil (fill), a side line's dash or a mark's arm.</summary>
        Color32 BlindColour(Blind b, int i, float k, float dark, float line, float night, float ringA)
        {
            if (i < b.nFill)
            {
                var c = Color.Lerp(blindVeilDay, blindVeilNight, night);                                  // linear: the vertex colour goes to the shader as it is
                return new Color32(Byte(c.r), Byte(c.g), Byte(c.b), Byte(k * dark));
            }
            int d = (i - b.nFill) / DotV;
            bool mark = d < b.core.Dots.Count && b.core.Dots[d].kind == 1;
            var t = mark ? b.markTint : b.tint;
            t.a = Byte(k * (mark ? (blindMarks ? Mathf.Lerp(blindMarkDay, blindMarkNight, night) * ringA : 0f) : line));
            return t;
        }

        static byte Byte(float v) => (byte)Mathf.RoundToInt(Mathf.Clamp01(v) * 255f);

        /// <summary>PlaceDots' size of dot d: a side line's dash (blindDotLength / blindDotWidth) or a mark's arm.</summary>
        void DotSize(Blind b, int d, float zs, ref float len, ref float wid)
        {
            if (d < b.core.Dots.Count && b.core.Dots[d].kind == 1) { len = blindMarkLength * 0.5f * zs; wid = blindMarkWidth * 0.5f * zs; }
        }
    }
}
