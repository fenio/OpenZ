using System.Collections.Generic;

namespace OpenZ.Sim
{
    /// <summary>
    /// <para>
    /// Zod has no blast reaction (a
    /// robot hit by splash fires on); DV19 draws one. With DV51 the SIM owns the knockdown: a robot that survives a heavy blast (rocket,
    /// radius, every hit) is marked down (KnockAt / KnockUntil, KnockBaseSec + up to KnockSpreadSec, hashed from the robot and the blast: no rand()
    /// draw) and ProcessAttackDamage holds its fire until KnockUntil (no cooldown used, no missile, no roll). The view plays the fall,
    /// the lie and the get-up so the robot is on its feet when KnockUntil comes (UnitView.React); a Tough then raises and settles the
    /// bazooka again (DV11 prepare) before its rocket. Already down = no refresh; at most KnockMaxDown robots down at once (the view's
    /// cap); the fort defender (DV41) is never knocked. Deterministic: sim time only, object order. All robot types, the Electrobot too.</para>
    /// <para>
    /// Zod's speeds are already the live numbers (tough 150, missile_cannon
    /// 128, MLRS 70 px/s, default_settings.txt), so the slower rockets are a deviation: RocketSpeedFactor x the Zod speed for
    /// the Tough's bazooka (also an APC's Tough crew) and the missile cannon; MLRS and the mech's rockets keep Zod's. The lead
    /// (EstimateMissileTarget), scatter, damage, splash and the target's dodge use the slower speed like any Zod missile.</para>
    /// OFF = Zod exact (the parity report's Zod rows, 'Zod exact' oz_zod_exact 1, PlayerPrefs oz_knock_hold / oz_rocket_speed 0).
    /// </summary>
    public partial class ZObject
    {
        /// <summary>DV51: sim time of the knockdown's blast and of the get-up (-1 = never knocked).</summary>
        public double KnockAt = -1, KnockUntil = -1;
        /// <summary>DV51 statistics (checks): attack ticks held while down.</summary>
        public int KnockHeldTicks { get; private set; }
        /// <summary></summary>
        public bool KnockSkipNext;

        /// <summary>DV51: down at `time` (the switch on).</summary>
        public bool KnockedDown(double time) => ZWorld.KnockHoldDeviation && KnockUntil > time + 1e-9;

        /// <summary>ProcessAttackDamage, before DV11's prepare and the cooldown: true = still down (no shot, no cooldown used, nothing
        /// drawn from Zod's rand()).</summary>
        bool KnockHold(double time)
        {
            if (!KnockedDown(time) || Kind != ZKind.Robot) return false;
            KnockHeldTicks++;
            return true;
        }
    }

    public sealed partial class ZWorld
    {
        /// <summary>DV51 (see ZObject.CombatFeel.cs). PlayerPrefs oz_knock_hold; ZGame sets it at every start.</summary>
        public static bool KnockHoldDeviation = true;
        /// <summary>DV51: the knockdown ring (fraction of the splash radius), the most robots down at once, the hold length.</summary>
        public const float KnockFrac = 0.75f;
        public const int KnockMaxDown = 24;
        public const double KnockBaseSec = 1.9, KnockSpreadSec = 0.4;
        /// <summary>
        /// A
        /// knock-worthy hit is a heavy blast centred on the robot (inside KnockDirectFrac of the radius, >= 65 % of the full damage) or
        /// the shell aimed at it inside KnockTargetFrac; then a hashed roll (no rand() draw) against KnockChance / KnockChanceHowitzer
        /// (howitzer + the fort's gun); a robot that was knocked down skips the roll on its next knock-worthy hit. Every other hit is a
        /// view-only flinch (UnitView HitReactions: no stop, no attack timer reset).</summary>
        public const float KnockDirectFrac = 0.35f, KnockTargetFrac = 0.5f;
        public const double KnockChance = 0.20, KnockChanceHowitzer = 0.60;
        /// <summary>Probes only: &gt;= 0 replaces both chances (1 = every knock-worthy hit knocks).</summary>
        public static double KnockChanceForTest = -1;
        /// <summary>DV51 statistics (checks): knockdowns marked; knock-worthy hits rolled; hits skipped by the never-twice rule.</summary>
        public int KnockCount { get; private set; }
        public int KnockRolls { get; private set; }
        public int KnockSkips { get; private set; }
        static readonly HashSet<string> KnockHeavy = new HashSet<string> { "tough", "light", "medium", "heavy", "howitzer", "gun", "missile_cannon", "missile_launcher" };

        /// <summary></summary>
        void KnockOnBlast(ZObject o, ZMissile m, float ratio)
        {
            if (!KnockHoldDeviation || o.Kind != ZKind.Robot || o.Destroyed || o.KillAt >= 0 || o.Health <= 0 || o.IsFortDefender) return;
            if (m.debris || m.grenade) return;
            if (!(ratio < KnockDirectFrac || (m.targetId == o.Id && ratio < KnockTargetFrac))) return;
            var a = GetAny(m.attackerId);
            string type = a == null ? null : a.IsFort ? "howitzer" : a.Type;
            if (type == null || !KnockHeavy.Contains(type)) return;
            if (o.KnockUntil > Time + 1e-9) return;                                            // already down: no refresh
            if (o.KnockSkipNext) { o.KnockSkipNext = false; KnockSkips++; return; }              // never twice in a row
            uint h = unchecked((uint)o.Id * 2654435761u ^ (uint)m.x * 40503u ^ (uint)m.y * 2246822519u ^ (uint)(long)System.Math.Round(Time * 1000.0) * 374761393u);
            h ^= h >> 15; h = unchecked(h * 2246822519u); h ^= h >> 13;
            KnockRolls++;
            double chance = KnockChanceForTest >= 0 ? KnockChanceForTest : type == "howitzer" ? KnockChanceHowitzer : KnockChance;
            uint r2 = unchecked(h * 668265263u); r2 ^= r2 >> 16;
            if ((r2 % 10000u) / 10000.0 >= chance) return;                                     // the roll: most direct hits are a flinch only
            int down = 0;
            foreach (var x in Objects) if (x.Kind == ZKind.Robot && !x.Destroyed && x.KnockUntil > Time + 1e-9) down++;
            if (down >= KnockMaxDown) return;
            o.KnockAt = Time; o.KnockUntil = Time + KnockBaseSec + KnockSpreadSec * (h % 1000u) / 1000.0;
            o.KnockSkipNext = true;
            KnockCount++;
        }

        /// <summary>DV52 (see ZObject.CombatFeel.cs). PlayerPrefs oz_rocket_speed; ZGame sets it at every start. Read when a unit's
        /// weapon stats are set (creation, an APC crew change), so a world keeps the speeds it was created with.</summary>
        public static bool RocketSpeedDeviation = true;
        public const double RocketSpeedFactor = 0.88;
        /// <summary>DV52: the missile speed a unit of `type` fires at (Zod's `zod` px/s unless the switch is on for a Tough / missile cannon).</summary>
        public static int RocketSpeed(string type, int zod) =>
            RocketSpeedDeviation && zod > 0 && (type == "tough" || type == "missile_cannon") ? (int)System.Math.Round(zod * RocketSpeedFactor) : zod;
    }
}
