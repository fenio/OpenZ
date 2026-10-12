using System.Collections.Generic;

namespace OpenZ.Sim
{
    /// <summary>
    /// Rifle pairs, deviation DV78. Zod: a bullet that hits removes the attacker's attack_damage whatever the target is
    /// (zobject.cpp:2187-2199); a grunt's rifle takes 11 HP of a grunt's 1081 per hit, so two grunt squads without grenades
    /// stand and fire at each other for minutes. OpenZ: for the attacker / target pairs of ZWorld.RiflePair a bullet that hits the
    /// robot removes the pair's own value instead. Only the damage of that hit changes: the hit roll, the cooldown, the range, who
    /// is engaged, grenades (thrown as before while the squad has any) and every pair not listed stay Zod's. The roof defender of a
    /// fort (DV41) is left out on both sides. Pure sim, no extra draw from Zod's rand() stream.
    /// </summary>
    public partial class ZObject
    {
        /// <summary>ProcessAttackDamage, hitscan branch, a hit that is not a driver snipe: the HP it removes from t. DV78 for a
        /// listed robot pair, else HullDamage (Zod's Damage, DV29).</summary>
        int DirectHitDamage(ZObject t)
        {
            if (ZWorld.RiflePairDeviation && IsRobot && t.IsRobot && !IsFortDefender && !t.IsFortDefender
                && ZWorld.RiflePair.TryGetValue((Type, t.Type), out int pair) && pair > Damage) return pair;
            return HullDamage(t);
        }
    }

    public sealed partial class ZWorld
    {
        /// <summary>DV78 switch. Static like SnipeDeviation: ZGame sets it at every start (on, off in 'Zod exact' oz_zod_exact = 1);
        /// off by default so every headless world (parity rows, tests) keeps Zod's damage.</summary>
        public static bool RiflePairDeviation = false;

        /// <summary>DV78: HP one rifle hit removes, per attacker robot type and target robot type (Zod: the attacker's damage, grunt 11).
        /// Set on counted mirror fights of two fresh squads (Editor menu OpenZ/Deviations/Count Grunt Mirror Fights).</summary>
        public static readonly Dictionary<(string attacker, string target), int> RiflePair =
            new Dictionary<(string attacker, string target), int>
            {
                [("grunt", "grunt")] = 125,    // 9 hits fell a grunt (Zod: 99): two fresh squads standing in range, one is dead after about 10 s
            };
    }
}
