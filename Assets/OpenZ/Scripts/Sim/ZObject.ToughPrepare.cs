namespace OpenZ.Sim
{
    /// <summary>
    /// engages from the carry (it had no target for ZWorld.ToughCarrySeconds) fires its first bazooka rocket
    /// ZWorld.ToughPrepareSeconds after the engage instead of on the engage tick.
    /// <para>Zod numbers it is built from: the robot's attack sprite starts 0.1 s after SetAttackObject (zrobot.cpp:350-361) and Zod's
    /// client holds the Tough's aim frame 0.7-1.0 s before every later rocket (rtough.cpp:195-220, `0.7 + rand % 100 * 0.3 * 0.01`);
    /// 0.1 + 0.7 = 0.8 s. The Zod server itself fires the first rocket on the engage tick (zobject.cpp:2102-2213 has no delay), so this
    /// is a deliberate change, listed in the parity report's Deviations block (DV11) with a toggle.</para>
    /// <para>Everything else stays Zod's: damage, splash, missile speed, lead (EstimateMissileTarget), scatter, the target's dodge, the
    /// 1.442 s cycle (a re-engage within the carry window fires on the cycle exactly as Zod), grenade throws (no prepare: no bazooka),
    /// the passive engage rule, the disengage rule (CanAttackObject is checked every tick of the prepare). No random draw (Zod's rand()
    /// stream is untouched), sim time only: deterministic. OFF = Zod exact (the parity report rows, 'Zod exact' mode oz_zod_exact 1,
    /// PlayerPrefs oz_tough_prepare 0).</para>
    /// </summary>
    public partial class ZObject
    {
        double tpLastEngaged = double.NegativeInfinity, tpReadyAt = -1;

        /// <summary>DV11: sim time at which the current bazooka prepare ends (-1 = not preparing). The view (RobotWeaponPose) raises
        /// and settles the tube by then, so the first rocket always leaves a steady tube at Zod's rocket speed.</summary>
        public double ToughReadyAt => tpReadyAt;

        /// <summary>ProcessAttackDamage, right after the no-target check and BEFORE the cooldown check: true = still preparing (no
        /// missile, no cooldown used, nothing drawn from Zod's rand()). Called every tick while engaged, so the carry window is exact.</summary>
        bool ToughPrepareHold(double time)
        {
            if (!ZWorld.ToughPrepareDeviation || Kind != ZKind.Robot || Type != "tough") return false;
            var t = AttackObject;
            if (t == null) return false;
            bool fresh = time - tpLastEngaged > ZWorld.ToughCarrySeconds + 1e-9;                    // the tube went back to the carry
            tpLastEngaged = time;
            if (Grenades > 0 || (Leader != null && Leader.Grenades > 0)) { tpReadyAt = -1; return false; }   // a grenade throw (zobject.cpp:2118)
            if (fresh) tpReadyAt = time + ZWorld.ToughPrepareSeconds;
            if (tpReadyAt < 0) return false;
            if (time + 1e-9 >= tpReadyAt) { tpReadyAt = -1; return false; }                          // prepared: Zod's code fires this tick
            if (!CanAttackObject(t)) { tpReadyAt = -1; return false; }                                // Zod's own disengage runs as usual
            return true;
        }
    }

    public sealed partial class ZWorld
    {
        /// <summary>OpenZ deviation DV11 (weapons_v2): a Tough's first rocket from the carry waits ToughPrepareSeconds. OFF = Zod exact
        /// (fires on the engage tick). Static like SnipeDeviation / DuelDeviation; ZGame sets it at every start (PlayerPrefs
        /// oz_tough_prepare, oz_zod_exact).</summary>
        public static bool ToughPrepareDeviation = true;
        /// <summary>0.1 s attack sprite delay (zrobot.cpp:360) + Zod's minimum 0.7 s Tough aim hold (rtough.cpp:211).</summary>
        public const double ToughPrepareSeconds = 0.8;
        /// <summary>Seconds without a target after which the bazooka is back in the carry (a new engagement prepares again). Same as
        /// the views' lowering: RobotWeaponPose (ToughTimeline.LowerDelay) and robot_models_v2's BS01Stance (toughLinger 1.6 s).</summary>
        public const double ToughCarrySeconds = 1.6;
    }
}
