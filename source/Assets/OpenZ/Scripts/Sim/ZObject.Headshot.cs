using System;

namespace OpenZ.Sim
{
    /// <summary>
    /// OpenZ deviation DV116 "sniper headshot" (Zod has none: a sniper's bullet that hits a robot removes the sniper's normal damage,
    /// zobject.cpp:2192). A sniper's bullet that hits a robot on foot (every robot type, the hero squads too; never a vehicle, a gun, a
    /// building or a map item) is a headshot with <see cref="ZWorld.HeadshotPct"/> % chance: that hit destroys the robot (its health
    /// to 0), whatever health it had. Only hits roll (a miss is a miss), a driver snipe is not a robot hit.
    /// The roll is its own deterministic stream (<see cref="ZWorld.HeadshotRoll"/>: a hash of the shooter, the target, the game clock
    /// and the world's count of such rolls): the world's random numbers are never drawn for it, so every other roll of a match stays as
    /// it was, and the same match twice gives the same headshots.
    /// The view takes the head off (View/UnitView.Headshot.cs): it reads <see cref="HeadshotBy"/> on the destroyed robot.
    /// Switch <see cref="ZWorld.SniperHeadshotDeviation"/>: the game sets it at every start (off in 'Zod exact'); off in headless worlds
    /// and in the counted checks unless they set it (OzV11Kit.GameSwitches).
    /// </summary>
    public partial class ZObject
    {
        /// <summary>DV116: the id of the sniper whose headshot destroyed this robot (-1 = none).</summary>
        public int HeadshotBy { get; internal set; } = -1;
        /// <summary>DV116: the game time of that headshot (-1 = none).</summary>
        public double HeadshotAt { get; internal set; } = -1;

        /// <summary>DV116, ProcessAttackDamage's direct hit: the HP this hit removes from t. A sniper's headshot on a robot = all the health
        /// t has left (at least the normal hit); every other hit = <paramref name="hit"/> unchanged (no roll, nothing counted).</summary>
        internal int HeadshotHit(ZObject t, int hit)
        {
            if (!ZWorld.SniperHeadshotDeviation || Type != "sniper" || t == null || t.Kind != ZKind.Robot || t.Health <= 0) return hit;
            if (!World.HeadshotRoll(this, t)) return hit;
            t.HeadshotBy = Id; t.HeadshotAt = World.Time;
            return Math.Max(hit, t.Health);
        }
    }

    public sealed partial class ZWorld
    {
        /// <summary>DV116 switch: a sniper's hit on a robot is a headshot (instant kill) with HeadshotPct % chance. Off by default (headless
        /// worlds, the parity report's other rows, the counted checks); View/ZGame.cs sets it at every start, off in 'Zod exact'.</summary>
        public static bool SniperHeadshotDeviation = false;
        /// <summary>DV116: the headshot chance of one sniper hit on a robot (per cent).</summary>
        public const int HeadshotPct = 15;

        /// <summary>DV116 statistics (checks): sniper hits on robots that rolled, and the headshots among them.</summary>
        public int HeadshotRolls { get; private set; }
        public int Headshots { get; private set; }

        /// <summary>DV116: one roll for a sniper's hit on a robot. Its own stream (never the world's Rng): a hash of the shooter's id, the
        /// target's id, this world's roll count and the game clock (ms); true with HeadshotPct % chance.</summary>
        internal bool HeadshotRoll(ZObject shooter, ZObject target)
        {
            uint h = unchecked(HeadshotMix((uint)shooter.Id * 0x9E3779B1u ^ HeadshotMix((uint)target.Id * 0x85EBCA77u + (uint)HeadshotRolls * 0xC2B2AE3Du)
                                           ^ (uint)(long)Math.Round(Time * 1000.0)));
            HeadshotRolls++;
            bool head = h % 100u < (uint)HeadshotPct;
            if (head) Headshots++;
            return head;
        }

        static uint HeadshotMix(uint x) { unchecked { x ^= x >> 16; x *= 0x7feb352du; x ^= x >> 15; x *= 0x846ca68bu; x ^= x >> 16; return x; } }
    }
}
