using System;

namespace OpenZ.Sim
{
    /// <summary>
    /// doorway moments are quicker (the constants in ZWorld.ShuttleContract.cs and ZWorld.ShuttleDoorway.cs), and two waits overlap now:
    ///  * Boarding: the ramp starts up as soon as the last robot of the file is through the doorway (+ <see cref="ZWorld.ShuttleBoardUpMarginSec"/>),
    ///    late enough that it shuts at least <see cref="ZWorld.ShuttleSeatShutMarginSec"/> after he sat down (<see cref="ZWorld.ShuttleBoardUpSec"/>
    ///    after he reached the foot). Before, the ramp stood open until he sat (ShuttleRampInSec) and only then began its swing.
    ///    ShuttleRampHoldUntil keeps its meaning (the last one sat): EXIT, a take-off and a second squad still wait for it.
    ///  * Unloading: the squad is let out <see cref="ZWorld.ShuttleExitLeadSec"/> (= ShuttleExitDoorSec) before the ramp is fully down, so the
    ///    first robot reaches the doorway exactly as the ramp lands (before, everybody waited seated until it was down and only then got up);
    ///    the ramp starts up as the last one steps off its tip (<see cref="ZWorld.ShuttleExitTipSec"/> before he is off the foot).
    /// Deterministic: sim time and the rules' foot schedule only. Switch: <see cref="ZWorld.ShuttleQuickRamp"/> (on; off = the old order of the
    /// waits with the new times).
    /// </summary>
    public partial class ZObject
    {
        /// <summary>K422: the moment the ramp starts up after a boarding or an exit (sim s; -1 none). Set with ShuttleRampHoldUntil.</summary>
        public double ShuttleRampUpAt { get; internal set; } = -1.0;

        /// <summary>ShuttleRampStep: the ramp is wanted down until this moment (K422 on: ShuttleRampUpAt, else the hold).</summary>
        internal double ShuttleRampWantUntil => ZWorld.ShuttleQuickRamp && ShuttleRampUpAt >= 0.0 ? ShuttleRampUpAt : ShuttleRampHoldUntil;

        /// <summary>ShuttleRampStep, an EXIT pending and the ramp not down yet: K422 on, the squad gets out now once the opening ramp is at
        /// most ShuttleExitLeadSec from fully down (with the ramp down it gets out as before).</summary>
        internal bool ShuttleExitEarly =>
            ZWorld.ShuttleQuickRamp && ShuttleRampPhase == ZShuttleRamp.Opening && (1.0 - ShuttleRamp01) * ZWorld.ShuttleRampOpenSec <= ZWorld.ShuttleExitLeadSec + 1e-9;
    }

    public sealed partial class ZWorld
    {
        /// <summary>K422 switch (code; on). Off: the ramp stands open until the last boarder sat, the squad gets up only with the ramp fully
        /// down and the ramp starts up when the last one is off the foot (the waits as before K422, with K422's times).</summary>
        public static bool ShuttleQuickRamp = true;
        /// <summary>The boarding ramp starts up this long after the last boarder is through the doorway at the earliest (s).</summary>
        public const double ShuttleBoardUpMarginSec = 0.15;
        /// <summary>The boarding ramp shuts at least this long after the hold, the last boarder sat (s; the view's figure sits within
        /// ShuttleRampInSec - 0.05 of the foot, so the bay shuts at least 0.2 s after the drawn figure sat, never on one still walking).</summary>
        public const double ShuttleSeatShutMarginSec = 0.15;
        /// <summary>Unloading: the squad is let out this long before the ramp is fully down (s): the first one at the doorway as it lands.</summary>
        public const double ShuttleExitLeadSec = ShuttleExitDoorSec;
        /// <summary>Unloading: the ramp starts up this long before the last one is off the foot (s): as he steps off its tip onto the ground
        /// (the view's last key, tip -> foot, is 0.25 s of ShuttleRampOutSec's walk).</summary>
        public const double ShuttleExitTipSec = 0.25;

        /// <summary>Boarding: from the last boarder at the ramp foot to the ramp starting up (s): 1.7 with today's constants.</summary>
        public static double ShuttleBoardUpSec => Math.Max(ShuttleBoardDoorSec + ShuttleBoardUpMarginSec, ShuttleRampInSec - ShuttleRampCloseSec + ShuttleSeatShutMarginSec);
        /// <summary>The ramp's value at the moment an EXIT lets the squad out (1 = fully down; K422 on: 1 - lead / opening).</summary>
        public static double ShuttleExitRamp01 => ShuttleQuickRamp ? Math.Max(0.0, 1.0 - ShuttleExitLeadSec / ShuttleRampOpenSec) : 1.0;
        /// <summary>From an EXIT given with the ramp shut to the squad let out (s): the opening, less the lead.</summary>
        public static double ShuttleExitAfterSec => ShuttleRampOpenSec * ShuttleExitRamp01;

        /// <summary>Check counters (reset by the checks): boardings whose ramp started up before the hold, exits let out while the ramp opened.</summary>
        public static int ShuttleQuickUps, ShuttleQuickExits;

        /// <summary>ShuttleBoardSchedule / ShuttlePlaceCrew hook, right after the hold is set: when the ramp starts up. lastFoot = the last
        /// figure's foot moment (boarding: it reaches the foot; exit: it steps off the foot).</summary>
        internal void ShuttleQuickUp(ZObject v, bool boarding, double lastFoot)
        {
            if (!ShuttleQuickRamp) { v.ShuttleRampUpAt = v.ShuttleRampHoldUntil; return; }
            v.ShuttleRampUpAt = boarding ? lastFoot + ShuttleBoardUpSec : Math.Max(Time, lastFoot - ShuttleExitTipSec);
            if (boarding) ShuttleQuickUps++;
            else if (v.ShuttleRampPhase == ZShuttleRamp.Opening) ShuttleQuickExits++;
        }
    }
}
