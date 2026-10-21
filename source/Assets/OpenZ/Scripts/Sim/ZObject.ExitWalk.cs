using System;
using System.Collections.Generic;

namespace OpenZ.Sim
{
    /// <summary>
    /// OpenZ deviation DV132 "out of the carrier first" (on top of DV118, the shuttle, and DV15, the APC's passengers). The rules let a squad
    /// out of the shuttle or an APC in one tick and stand each robot on its spot at once (the shuttle: its back slot behind the ramp; the APC:
    /// Zod's eject spot); the view then walks each figure out to its robot: up from the bench, along the aisle, through the doorway, down the
    /// ramp and on to its spot (the shuttle: ZWorld.ShuttleRampOutSec to the ramp foot at the rules' moment, then the walk to the slot; the APC:
    /// the rear ramp drops, the file leaves 0.55 s apart, a 3.2 s walk each). Before this rule each robot fought from its spot at once (shots,
    /// rockets, grenades, walking off, being shot at), and the view squeezed the rest of the walk into 0.2 s the moment its robot fired, was
    /// shot at or moved: under fire the figures jumped from the bay to their spots. Now each robot so let out is GETTING OUT until
    /// <see cref="ExitOutAt"/> (sim seconds), the moment its figure stands on its spot:
    ///  * the shuttle: the moment the rules give it to step off the ramp foot (ShuttleFootTimes) + the walk from the foot (slot 0) to its back
    ///    slot at <see cref="ZWorld.ExitWalkMps"/> (at least <see cref="ZWorld.ExitWalkMinSec"/>) + <see cref="ZWorld.ExitHandSec"/>;
    ///  * the APC: <see cref="ZWorld.ApcExitOpenSec"/> (the ramp drops) + <see cref="ZWorld.ApcExitGapSec"/> per robot before it in the file
    ///    + <see cref="ZWorld.ApcExitWalkSec"/> (its walk) + ExitHandSec.
    /// While it is getting out it takes no step and fires nothing (no shot, rocket, grenade or zap; it engages nobody); its orders, also an
    /// order the player gives meanwhile, wait and start from its spot at that moment (a fresh start of the same waypoint, as after a stun, so
    /// the waiting never counts as stuck); and no enemy can pick it as a target (it is still in the bay or on the ramp). A blast landing next
    /// to it still hurts it (splash goes by area). Zod's own crew exits (EJECT from a tank, a jeep, a gun) are unchanged: those robots act at
    /// once, as in Zod. Deterministic: sim time and the rules' foot schedule only, no rand() draw. Switch: <see cref="ZWorld.ExitWalkHoldOn"/>
    /// (on; off = as before, the robots fight at once). Matters only while DV118 makes shuttles or DV15 gives APCs passengers.
    /// </summary>
    public partial class ZObject
    {
        /// <summary>DV132: the sim time this robot's figure stands on its spot after leaving the shuttle or an APC (-1 = it is not getting out).</summary>
        public double ExitOutAt = -1;
        /// <summary>DV132 statistics (checks): the ticks this robot was held while getting out.</summary>
        public int ExitHeldTicks { get; private set; }

        /// <summary>DV132: still getting out at `time` (the switch on).</summary>
        public bool ExitWalking(double time) => ZWorld.ExitWalkHoldOn && ExitOutAt > time + 1e-9;

        /// <summary>DV132, ProcessServer right after the stun step: true = still getting out (the tick ends here: no step, no shot, no
        /// engage). On the tick it is out it falls through and acts in that very tick.</summary>
        bool ExitWalkHold(double time)
        {
            if (ExitOutAt < 0) return false;
            if (!ZWorld.ExitWalkHoldOn || time >= ExitOutAt - 1e-9 || Kind != ZKind.Robot || Owner == Team.Neutral || KillAt >= 0 || World == null) { ExitWalkEnd(); return false; }
            StopMove(); IsRunning = false; fortRush = false;
            LastAttackPlayerGiven = false;
            var s = PS;
            s.wait = ZPathWait.None; s.heldTicks = 0; s.lastProgress = time; s.stillX = double.NaN;   // standing in the bay is no "stuck" and no frozen watchdog
            if (AttackObject != null) Disengage();                                                    // a target given meanwhile (a squad help call): taken up once it is out
            ExitHeldTicks++; ZWorld.ExitHeldTicksAll++;
            return true;
        }

        /// <summary>Out: its orders start from where it stands (Zod's new-waypoint path, as at the end of a stun), the stuck clocks restart.</summary>
        void ExitWalkEnd()
        {
            ExitOutAt = -1;
            var s = PS; double now = World != null ? World.Time : 0;
            s.lastProgress = now; s.stillX = double.NaN; s.rebase = true; s.trafficSince = -1; s.heldTicks = 0; s.wait = ZPathWait.None;
            s.sideSince = -1; s.bridge.jamSince = now; s.bridge.jamX = CenterX; s.bridge.jamY = CenterY;
            if (Waypoints.Count > 0 && !PathInBuildingStage) PathReissue();
            ZWorld.ExitWalksOut++;
        }
    }

    public sealed partial class ZWorld
    {
        /// <summary>DV132 switch (on; off = the robots let out of the shuttle or an APC fight at once, as before).</summary>
        public static bool ExitWalkHoldOn = true;
        /// <summary>DV132, the shuttle: the walk from the ramp foot to the back slot (the view's walking pace, BoardingView.runSpeed, m/s), its
        /// shortest time (s, BoardingView's last-key minimum) and the hand-over after it (s).</summary>
        public const double ExitWalkMps = 3.2, ExitWalkMinSec = 0.15, ExitHandSec = 0.1;
        /// <summary>DV132, the APC (the view's rear-door exit, View/ApcRearDoor.cs + BoardingView.BuildApcExit): the ramp drops and the first
        /// one stands up (ApcRearDoor.OpenTime 0.55 + 0.1 s), the file 0.55 s apart (BoardingView.ApcGap), each walk 3.2 s (ApcExitBudget).</summary>
        public const double ApcExitOpenSec = 0.65, ApcExitGapSec = 0.55, ApcExitWalkSec = 3.2;
        /// <summary>DV132 statistics (checks, all worlds): robots given a getting-out time, robots that came out, ticks held.</summary>
        public static int ExitWalksSet, ExitWalksOut, ExitHeldTicksAll;

        /// <summary>DV132, ShuttlePlaceCrew (the squad stands on its back slots, the foot schedule set): each robot's getting-out time.</summary>
        internal void ExitWalkShuttle(ZObject v, List<ZObject> squad)
        {
            if (!ExitWalkHoldOn || v == null || squad == null) return;                                      // the switch off: nothing set (as before)
            var (fx, fy) = v.ShuttleBackPoint(0);                                                           // slot 0 = the ramp foot
            int n = Math.Min(v.ShuttleFootIds.Count, v.ShuttleFootTimes.Count);
            for (int k = 0; k < n; k++)
            {
                ZObject m = null;
                foreach (var x in squad) if (x != null && x.Id == v.ShuttleFootIds[k]) { m = x; break; }
                if (m == null || m.Kind != ZKind.Robot) continue;
                double dx = m.CenterX - fx, dy = m.CenterY - fy;
                double walk = Math.Max(ExitWalkMinSec, Math.Sqrt(dx * dx + dy * dy) / 8.0 / ExitWalkMps);   // 8 px a metre
                m.ExitOutAt = v.ShuttleFootTimes[k] + walk + ExitHandSec;
                ExitWalksSet++;
            }
        }

        /// <summary>DV132, PassengersOut on an APC (the squad stands at the eject spot): each robot's getting-out time, in squad order (the view's file).</summary>
        internal void ExitWalkApc(ZObject v, List<ZObject> squad)
        {
            if (!ExitWalkHoldOn || v == null || squad == null || !v.IsApc) return;
            for (int i = 0; i < squad.Count; i++)
            {
                var m = squad[i];
                if (m == null || m.Kind != ZKind.Robot) continue;
                m.ExitOutAt = Time + ApcExitOpenSec + i * ApcExitGapSec + ApcExitWalkSec + ExitHandSec;
                ExitWalksSet++;
            }
        }
    }
}
