using System;
using System.Collections.Generic;

namespace OpenZ.Sim
{
    /// <summary>
    /// DV108 hero squads, KH2: a member's own weapon range (item 199: Alan walks behind Brad, so with a grunt's range he often stood out
    /// of reach, did not shoot and did not throw his grenades). A member's attack radius is his base type's x his own factor
    /// (<see cref="Mul"/>): ALAN &amp; BRAD: Brad x 1.0 (a grunt's 120 px = 15 m), Alan x 1.10 (132 px = 16.5 m; the sniper reaches 144 px).
    /// The attack radius is the one range of a robot's rules: his rifle shots, his grenade throws (thrown by the sniper's grenade rule,
    /// Sim/ZHeroSquad.Kh.cs: never at robots while DV40 is on), the engage and agro tests (attack radius + agro distance) all follow it.
    /// Set where the kind's numbers are set (ZWorld.MakeHero: at the start and every time he gets out of a vehicle, gun or troop carrier).
    /// Switch <see cref="ZWorld.HeroRangeDeviation"/> (off: a grunt's range for both). No random draw, no clock.
    /// </summary>
    public static class ZHeroRange
    {
        /// <summary>Attack radius factor per member (kind id -> per member, leader first).</summary>
        public static readonly Dictionary<string, double[]> Mul = new Dictionary<string, double[]>
        {
            [ZHeroSquads.AlanBrad] = new[] { 1.0, 1.10 },                                              // Brad a grunt's, Alan + 10 %
        };

        public static double MulOf(ZHeroKind k, int member) =>
            ZWorld.HeroRangeDeviation && k != null && Mul.TryGetValue(k.Id, out var a) && member >= 0 && member < a.Length ? Math.Max(0.1, a[member]) : 1.0;

        /// <summary>The member's attack radius (px) from his base type's.</summary>
        public static int Radius(int baseRadius, ZHeroKind k, int member) => (int)Math.Round(baseRadius * MulOf(k, member));
    }

    public partial class ZObject
    {
        /// <summary>KH2, DV28's approach (ZObject.CombatNB.cs ApproachPlan: an attack out of range walks to the nearest spot in range): a hero
        /// member plans that spot with his squad leader's range when it is shorter than his own, so the pair walks up together as before
        /// and he opens fire on the way the moment the target is within his own range (ProcessAttackWP tests the range first, every tick).
        /// Without it Alan, planning with his longer range, was sent onto the tile where Brad then stood firing and waited behind him for
        /// good, out of range, without a shot (headless: 0 shots in 30 s; the KH2 count's staged line). Every other robot: its own range.</summary>
        internal int HeroPlanRadius() =>
            ZWorld.HeroRangeDeviation && IsHero && Leader != null && Leader.IsHero && !Leader.Destroyed && Leader.KillAt < 0 ? Math.Min(AttackRadius, Leader.AttackRadius) : AttackRadius;
    }

    public sealed partial class ZWorld
    {
        /// <summary>KH2 switch: a hero member's own range factor (ZHeroRange), and the approach rule above (ZObject.HeroPlanRadius). On by default (it acts only on
        /// hero squads, which exist only while <see cref="HeroSquadDeviation"/> is on).</summary>
        public static bool HeroRangeDeviation = true;
    }
}
