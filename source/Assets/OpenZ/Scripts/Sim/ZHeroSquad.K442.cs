using System;
using System.Collections.Generic;

namespace OpenZ.Sim
{
    /// <summary>
    /// OpenZ deviation DV133 "hero squads stronger again" (on top of DV112, Sim/ZHeroSquad.Kh4.cs, which is on top of DV108 and KH2).
    /// ALAN &amp; BRAD, both members, from the numbers DV112 gives them, each rounded half up:
    /// <list type="bullet">
    /// <item><b>Hit points x 1.15</b>: 3892 -> 4476 (a grunt's 1081 x 3, x 1.20, x 1.15).</item>
    /// <item><b>The strong shot x 1.15</b>: a bullet of theirs that hits removes DV112's hit x <see cref="ZHeroPlusRow.DamagePct"/> / 100:
    /// on a grunt 450 -> 518, on a hull or a Tough 40 -> 46.</item>
    /// <item><b>Their thrown grenades x 1.15</b> (Alan's own five and any a hero throws from the squad's pool): with the game's grenade rule
    /// DV104 a direct hit 2299 -> 2644 and an area hit 1799 -> 2069 at the centre of the blast; with DV104 off 1999 -> 2299.</item>
    /// </list>
    /// Unchanged: range (Brad 138 px, Alan 152 px), the pause between shots and throws, the hit chance, the blast radius, speed, the
    /// self-repair as a share of full health (20 % a minute: now 895 HP a minute, half to full still 150 s), the driver hit (a grunt's,
    /// as DV108 and DV112 keep it), every other robot.
    /// Applied inside DV112's own three numbers (ZHeroBoost.Health / Damage / Grenade, Sim/ZHeroSquad.Kh4.cs), so it reaches every place
    /// DV112 reaches (ZWorld.MakeHero at the start and every time a member gets out of a crew list, ZObject.HeroHit, ZObject.HeroGrenade)
    /// and nothing else; it acts only while <see cref="ZWorld.HeroBoostDeviation"/> is on (the game sets that at every start, off in
    /// 'Zod exact', where no hero squad exists). Switch <see cref="ZWorld.HeroPlusDeviation"/>: on (a code switch); the counted checks
    /// turn it off (Editor/OzV11Kit.cs GameSwitches) unless they set it themselves. No random draw, no clock: integer maths only.
    /// </summary>
    public sealed class ZHeroPlusRow
    {
        /// <summary>Per cent on top of DV112's hit points, strong shot and thrown grenades (100 = unchanged).</summary>
        public int HealthPct = 100, DamagePct = 100, GrenadePct = 100;
    }

    public static class ZHeroPlus
    {
        /// <summary>The extra step of each hero kind (kind id -> row). Both members of a kind get the same row.</summary>
        public static readonly Dictionary<string, ZHeroPlusRow> Kinds = new Dictionary<string, ZHeroPlusRow>
        {
            [ZHeroSquads.AlanBrad] = new ZHeroPlusRow { HealthPct = 115, DamagePct = 115, GrenadePct = 115 },
        };

        /// <summary>The kind's row while the switch is on (null = nothing on top). Only called from DV112's boosted branch, so DV112 is on.</summary>
        public static ZHeroPlusRow Of(ZHeroKind k) => ZWorld.HeroPlusDeviation && k != null && Kinds.TryGetValue(k.Id, out var r) ? r : null;

        static int Pct(int v, int pct) => v <= 0 || pct == 100 ? v : (int)(((long)v * Math.Max(0, pct) + 50) / 100);   // rounded half up

        /// <summary>Full health from DV112's.</summary>
        public static int Health(int hp, ZHeroKind k) { var r = Of(k); return r == null ? hp : Pct(hp, r.HealthPct); }
        /// <summary>The HP a hit removes, from DV112's strong shot.</summary>
        public static int Damage(int hit, ZHeroKind k) { var r = Of(k); return r == null ? hit : Pct(hit, r.DamagePct); }
        /// <summary>The HP a thrown grenade of theirs takes, from DV112's (a direct hit or an area hit at the centre of the blast).</summary>
        public static int Grenade(int hp, ZHeroKind k) { var r = Of(k); return r == null ? hp : Pct(hp, r.GrenadePct); }
    }

    public sealed partial class ZWorld
    {
        /// <summary>DV133 switch: ALAN &amp; BRAD +15 % hit points and +15 % damage on top of DV112. On by default (a code switch): it acts only
        /// while <see cref="HeroBoostDeviation"/> is on, which the game sets at every start and every headless world leaves off. The counted
        /// checks turn it off unless they set it themselves.</summary>
        public static bool HeroPlusDeviation = true;

        /// <summary>A hold of the switch for checks and reports (put back on Dispose).</summary>
        public sealed class HeroPlusHold : IDisposable
        {
            readonly bool was;
            internal HeroPlusHold(bool on) { was = HeroPlusDeviation; HeroPlusDeviation = on; }
            public void Dispose() { HeroPlusDeviation = was; }
        }
        public static HeroPlusHold HeroPlusOff() => new HeroPlusHold(false);
        public static HeroPlusHold HeroPlusOn() => new HeroPlusHold(true);
    }
}
