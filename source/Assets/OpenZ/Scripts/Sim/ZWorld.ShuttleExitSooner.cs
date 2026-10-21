using System;

namespace OpenZ.Sim
{
    /// <summary>
    /// K426, inside DV118 (deterministic: sim time only, no random draw, no frame time): the squad may be ordered out sooner after a landing.
    ///  * EXIT is TAKEN from the moment of the touchdown, a crash landing's too: on the ground the EXIT gate (ZObject.ShuttleCrewSettled,
    ///    read by ZObject.CanEjectCrew: the HUD's button, the EXIT cursor on the ship, the right-click) no longer waits until the ship stands
    ///    level; a squad still walking in must sit down first, as before. Given while it crashes, rests tilted or rights itself, the order is
    ///    kept (ShuttleExitPending) and carried out by the rules the moment it stands level: the ramp comes down then
    ///    (Sim/ZWorld.ShuttleRamp.cs, unchanged: never while it is tilted, item 292) and the squad gets out when the ramp is fully down, with
    ///    the usual unloading and sounds. Given while it comes down the last metres it was already taken (K256f: ShuttleExitOnLanding), and a
    ///    normal landing stands level at the touchdown (unchanged), so its ramp starts at once.
    ///  * The crash landing's whole wait is shorter: the crash <see cref="ZWorld.ShuttleSoonerCrashSec"/> s (was 1.4), the tilted rest
    ///    <see cref="ZWorld.ShuttleSoonerRestSec"/> s (was 1.0), the self-righting <see cref="ZWorld.ShuttleSoonerLevelSec"/> s (was 1.2):
    ///    level 1.7 s after the touchdown (was 3.6 s). The view draws its crash on the rules' times by itself (View/OzPlayableShuttle.Crash.cs).
    /// <see cref="ZWorld.ShuttleExitSoonerOn"/> off (checks only) = everything exactly as before.
    /// </summary>
    public partial class ZObject
    {
        /// <summary>K426: standing on the ground after its touchdown, not yet level (a crash landing: it crashes, rests tilted or rights itself):
        /// EXIT is taken now and carried out once it stands level. False with the switch off and for every other unit.</summary>
        public bool ShuttleExitWhileRighting => ZWorld.ShuttleExitSoonerOn && IsShuttle && !Destroyed && World != null
                                                && ShuttleState == ZShuttleState.Landed && ShuttleLift <= 0 && ShuttleTouchdownAt >= 0 && World.Time >= ShuttleTouchdownAt;
    }

    public sealed partial class ZWorld
    {
        /// <summary>K426 switch (the game: on; checks may switch it off to see the old rules: EXIT only once level, the crash 1.4 + 1.0 + 1.2 s).</summary>
        public static bool ShuttleExitSoonerOn = true;
        /// <summary>K426: a crash landing's timing after the touchdown (s): the crash (the slam, the bounce, the other side, the slump and the
        /// slide), the tilted rest, the self-righting.</summary>
        public const double ShuttleSoonerCrashSec = 0.8, ShuttleSoonerRestSec = 0.2, ShuttleSoonerLevelSec = 0.7;
        /// <summary>K426: the same before (the switch off).</summary>
        public const double ShuttleOldCrashSec = 1.4, ShuttleOldRestSec = 1.0, ShuttleOldLevelSec = 1.2;
    }
}
