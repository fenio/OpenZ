namespace OpenZ.Sim
{
    /// <summary>
    /// OpenZ deviation DV79 "unmanned stop": a vehicle with nobody inside never moves, turns or fires.
    /// <para>Zod: the hit that kills the driver clears the drivers and sets the null team; "disengage, clear waypoints, and stop
    /// movement" are "done at the server level" (zobject.cpp:334-359), in the pass after every object has run
    /// (ZServer::UpdateObjectDriverHealth, zserver.cpp:1808-1874), and only while the shooter still has an attack object then
    /// (zserver.cpp:1212). An ejected vehicle is cleared in the eject handler (zserver_events.cpp:631-731).</para>
    /// <para>OpenZ has layers that hand out moves by themselves: the patrol record (DV68, ZObject.Patrol.cs) issues the next leg to a
    /// unit that has no waypoint, the bridge queue (DV8, ZBridges.cs) drives a unit that stands idle on a deck off it. Neither asked
    /// whether anybody sits in the vehicle, and ProcessServer runs any waypoint a unit holds, whoever owns it.</para>
    /// <para>DV79: (1) the step that removes the last driver stops the vehicle itself (UnmannedStop: speed zero, waypoints, path,
    /// patrol / hold record, queued pathing actions and a loaded second barrel dropped; its target goes in the same tick, in Zod's
    /// server pass, which first books what the vehicle itself hit earlier in that tick, or in its own hold); (2) while nobody is inside, the
    /// vehicle's own tick ends before the patrol, waypoint and fire steps (UnmannedHold), and whatever was handed to it meanwhile is
    /// dropped there; (3) the bridge queue lets go of it (ZBridges.ServeBridge), so it stands on a deck like any parked hull.
    /// Entering it is unchanged (CanBeEntered); from the tick a robot is inside it is a normal vehicle again.</para>
    /// Deterministic: sim state only, no random draw of its own (the lid's own close roll of Disengage is drawn in the server
    /// pass as before, or in the vehicle's own hold when it runs after its shooter in that tick). Switch: ZWorld.UnmannedStopDeviation (the game turns it on at every start; headless default
    /// off = Zod's server pass alone). Listed as DV79 in the parity report's Deviations.
    /// </summary>
    public partial class ZObject
    {
        /// <summary>DV79: a vehicle with nobody inside (nobody's), held still by the rule. The view reads it too.</summary>
        public bool UnmannedHeld => ZWorld.UnmannedStopDeviation && Kind == ZKind.Vehicle && Owner == Team.Neutral && !Destroyed;

        /// <summary>DV79: stop dead where it stands and forget everything it was doing. The position and its sub-pixel carry are
        /// kept. caught = a leftover found by the hold (statistics only). keepTarget = the call from the hit's own step: the target
        /// stays until the server pass of this tick (ZWorld.Tick, side effects). That pass books what THIS vehicle hit earlier in
        /// the same tick only while its target is still set (UpdateObjectHealth: the death of a unit it destroyed;
        /// UpdateObjectDriverHealth: the vehicle it sniped), and drops the target itself (v.Disengage, as in Zod); when no pass
        /// comes for it the hold drops it. It cannot fire meanwhile: the hold ends its tick before the fire step.</summary>
        internal void UnmannedStop(bool caught, bool keepTarget = false)
        {
            if (!UnmannedHeld) return;
            Waypoints.Clear(); lastWp = ZWaypoint.Cleared; cur = default;                 // no order, no path, no pending path answer
            StopMove(); IsRunning = false;                                                // speed zero
            if (!keepTarget) Disengage();                                                 // no target (an open lid closes on its own timer)
            ClearOrderModes();                                                            // DV68: a patrol / hold ends with the driver
            PS.ClearPending();                                                            // pathing layer: no queued give-up, back-off or re-path
            hpSecondAt = -1;                                                              // DV30: the loaded second barrel is not fired by the next crew
            cgTarget = null; cgUpSince = -1;                                              // DV31: the crane driver's rifle ends with him (CraneGunStep cleared it each tick; the hold ends the tick before that step)
            if (caught) ZWorld.UnmannedCaught++; else ZWorld.UnmannedStops++;
        }

        /// <summary>DV79, ProcessServer after the stun, lid and pathing steps and before the patrol, waypoint and fire steps:
        /// true = nobody inside, the tick ends here.</summary>
        bool UnmannedHold(double time)
        {
            if (!UnmannedHeld) return false;
            if (Waypoints.Count > 0 || IsMoving || patrolOn || HoldPosition || hpSecondAt >= 0 || cgTarget != null) UnmannedStop(true);
            else if (AttackObject != null) Disengage();                                   // the target the hit's step left for the server pass: this vehicle runs after its shooter in that tick, or no pass came for it
            if (time >= nextPassiveCheck) nextPassiveCheck = time + 0.5;                  // the passive check keeps its 0.5 s beat (it ends at once for a neutral unit)
            return true;
        }
    }

    public sealed partial class ZWorld
    {
        /// <summary>DV79 unmanned stop (see ZObject.Unmanned.cs). The game turns it on at every start; headless default off.</summary>
        public static bool UnmannedStopDeviation = false;
        /// <summary>DV79 statistics (checks / proofs): vehicles stopped in the step that removed their last driver, and leftovers
        /// (a waypoint, a speed, a target, a patrol / hold record) dropped from a vehicle with nobody inside afterwards.</summary>
        public static int UnmannedStops, UnmannedCaught;
    }
}
