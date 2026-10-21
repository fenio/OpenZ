using System;

namespace OpenZ.Sim
{
    /// <summary>
    /// Every shuttle the rules make (ZWorld.CreatePlayableShuttle: level 16's own ship, the testing panel's red, blue and empty drops,
    /// a map's "vehicle:shuttle" entry of any owner) starts with the light tank's hit points x <see cref="ShuttleHpFactor"/>, rounded to
    /// the nearest whole point: 3378 x 1.2 = 4053.6, so 4054 (was 3378). One number for every team, so an enemy ship and the player's
    /// ship always match.
    /// <list type="bullet">
    /// <item>Everything that reads the ship's health reads it as a share of its maximum, so it scales with it and is left as it is:
    /// the damage smoke and fire (View/FxV3/OpenZDamageStates: smoke at 50 %, fire at 25 %, the fire out past 40 %), the damaged look
    /// (ZObject.ShowDamaged under 40 %, ShowPartiallyDamaged 40-70 %), the health bars and the unit panel's percent, the repair station
    /// (its pad heals what is missing in its 5 s, whatever the maximum: ZObject.RepairBayTick), the veteran heal (a share). A crash
    /// landing or a break-up comes only at 0 hit points, as before.</item>
    /// <item>Not changed: every weapon, its damage, splash, scatter and hit chance; the light tank itself (still 3378); every other unit;
    /// the level 16 decoration of DV95 (a map item of its own, only when the playable shuttle is off).</item>
    /// </list>
    /// Deterministic (no random draw, no clock). Switch <see cref="ShuttleMoreHpOn"/> (code switch, on): off = the light tank's hit
    /// points, as before.
    /// </summary>
    public sealed partial class ZWorld
    {
        /// <summary>K443 switch (code switch, on). Off = a shuttle starts with the light tank's hit points, as before.</summary>
        public static bool ShuttleMoreHpOn = true;
        /// <summary>K443: the shuttle's hit points as a multiple of the light tank's.</summary>
        public const double ShuttleHpFactor = 1.2;
        /// <summary>The light tank's hit points when the settings have no light tank (unit.light.health 0.337838 x 10000).</summary>
        public const int ShuttleBaseHp = 3378;

        /// <summary>K443: a shuttle's maximum hit points from the light tank's: x <see cref="ShuttleHpFactor"/>, rounded to the nearest
        /// whole point (3378 -> 4054) while the switch is on; the light tank's own with the switch off.</summary>
        public static int ShuttleHpFrom(int lightTankHp) =>
            ShuttleMoreHpOn ? (int)Math.Round(lightTankHp * ShuttleHpFactor, MidpointRounding.AwayFromZero) : lightTankHp;

        /// <summary>K443: the maximum hit points a shuttle made in this world starts with (the checks and the Play proof read it).</summary>
        public int ShuttleMaxHp => ShuttleHpFrom(S?.Unit("light")?.MaxHealth ?? ShuttleBaseHp);
    }
}
