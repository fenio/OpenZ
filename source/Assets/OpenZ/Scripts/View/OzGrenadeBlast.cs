using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// How big the blast of a THROWN grenade is drawn (view only; the rules never read this). The blast table's Grenade row
    /// (LegacyMatch.Grenade: strength 0.75, glow radius 3.0 m) drew the fire to 1.9 m of a blast that really reaches 3.75 m.
    /// Here the drawn strength follows the blast's real radius, which the rules send with every blast (ZEvent.radius, px):
    ///   strength     = DrawScaleAt30 x radius / 30      30 px (Zod's grenade) -> 0.95,  36 px (rule DV104 on) -> 1.14
    ///   glow radius  = 4 x strength, at most 3.7 m      (the table's own 4 : 1; 3.8 m starts the outer ring of rockets and shells)
    /// Everything of the blast grows with the strength, as for every other blast: fireballs, hot core, smoke, sparks, the thin
    /// shock ring on the ground, the ground light, the burn mark. The fire reaches 2.17 m of 3.75 m (58 %; it was 1.90 m,
    /// 51 %) and 2.43 m of 4.5 m with DV104 (54 %); the shock ring passes the real radius at 19 % of its brightness (it was
    /// 4 %); the burn mark has a radius of 1.2 m on average (1.5 m with DV104; it was 1.0 m).
    /// 0.95 = the light tank shell's strength, just under the Tough rocket's 1.0: with Zod's radius a grenade is never drawn
    /// larger than either. With DV104 on its fire is drawn about a tenth larger than theirs; it stays under 1.2, where the
    /// game starts to throw big chunks high, and it never gets the outer ring, the hero blast or the second explosion look.
    /// The sound, the hot colour and the kind are the Grenade row's. A burst grenade crate keeps the Grenade row as it is,
    /// and so does a grenade blast smaller than Zod's 30 px (only a test tool makes one: Editor/OpenZSandbagsV2.cs, 8 px).
    /// </summary>
    public static class OzGrenadeBlast
    {
        /// <summary>The drawn strength of a grenade whose blast reaches Zod's 30 px. The one number to turn if the blast reads too
        /// big or too small beside a Tough rocket and a light tank shell. At 0.86 the fire of a 36 px grenade is drawn to the
        /// same share of its real radius as before this file (51 %), under it to a SMALLER share (0.83, where it is drawn exactly
        /// as a Tough rocket: 2.24 m of 4.5 m = 50 %): bigger than it was, but no nearer its real size. So with DV104 on there is
        /// no value that is both nearer and no larger than a Tough rocket; going under 0.87 is a choice to make with the
        /// pictures.</summary>
        public const float DrawScaleAt30 = 0.95f;
        public const int ZodRadiusPx = 30;
        /// <summary>The glow layers' radius never reaches OpenZFxCatalog.LegLargeRadius (3.8 m): a grenade stays a small blast.</summary>
        public const float GlowRadiusCap = 3.7f;

        /// <summary>For the checks: grenade blasts drawn by their radius since the start, and the numbers of the last one.</summary>
        public static int Drawn, LastRadiusPx;
        public static float LastStrength, LastGlowRadius;

        public static float StrengthOf(int radiusPx) => DrawScaleAt30 * (radiusPx > 0 ? radiusPx : ZodRadiusPx) / ZodRadiusPx;
        public static float GlowRadiusOf(float strength) => Mathf.Min(4f * strength, GlowRadiusCap);

        /// <summary>The blast table row of a thrown grenade whose blast reaches radiusPx.</summary>
        public static LegacyMatch.BlastSpec For(int radiusPx)
        {
            if (radiusPx < ZodRadiusPx) return LegacyMatch.Grenade;                     // smaller than Zod's grenade (a test tool's 8 px blast): the table row, as before
            var d = LegacyMatch.Grenade;                                                // label, kind, sound bank, gain and pitch stay the grenade's
            d.s = StrengthOf(radiusPx); d.legRadius = GlowRadiusOf(d.s);
            Drawn++; LastRadiusPx = radiusPx; LastStrength = d.s; LastGlowRadius = d.legRadius;
            return d;
        }
    }
}
