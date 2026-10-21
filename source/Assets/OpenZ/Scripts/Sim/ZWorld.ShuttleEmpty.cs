using System;
using System.Collections.Generic;

namespace OpenZ.Sim
{
    /// <summary>
    /// DV118, the EMPTY shuttle and EXIT in the air (deterministic: positions, sim time, fixed lists; no random draw, no frame time). Every
    /// rule here is per ship; a match without an empty shuttle and without an EXIT given in the air plays exactly as before.
    ///  * An empty shuttle: no pilot, nobody aboard, the null team, like the game's other empty vehicles (the testing panel's EMPTY row,
    ///    <see cref="ZWorld.SpawnShuttle"/> with the neutral team; a map's entry "vehicle:shuttle" with the owner "null",
    ///    <see cref="ZWorld.ShuttleFromMap"/>). It never flies and takes no order (SendWaypoints refuses the null team), its ramp stays shut.
    ///  * Nobody walks into it by himself: it is never "an empty vehicle anyone can enter" (<see cref="ZObject.CanBeEntered"/> is false for
    ///    every shuttle), so the idle auto-grab, the closest-grabber pick (DV55), the certain grab, the CPU's target lists and its car park
    ///    leave it alone; an ENTER waypoint on it that was not ordered is dropped at once (<see cref="ZObject.ShuttleEmptyAutoRefused"/>).
    ///  * Only an order puts robots in: a human team's squad ordered into it (the same ENTER order, ZObject.CanTakePassengers) walks to its
    ///    back slots as into a crewed shuttle; the ramp comes down when one of them is near (<see cref="ZObject.ShuttleEmptyRampCalled"/>);
    ///    at the ramp foot the squad boards (<see cref="ZWorld.ShuttleTakeEmpty"/>): the LEADER becomes its pilot (Drivers[0], never gets
    ///    out, cannot be ordered out, lost with the ship), his living minions its passengers (the APC's passenger squad: they get out on
    ///    EXIT), and the ship turns to the squad's team. A lone robot is only its pilot (no passengers: EXIT is refused). A CPU team's order
    ///    on it is refused (the CPU never uses a shuttle). A second squad ordered in meanwhile walks on to the spot next to it (DV59) unless
    ///    the first was a lone pilot: then it boards as the passengers.
    ///  * EXIT in the air: EXIT (the button, the right-click on the ship, the cursor) is offered only where the ship may land and let them
    ///    out: flying, the spot where braking would end (the STOP rule), hovering or coming down, the spot under it, must pass the landing
    ///    spot rule (ZWorld.ShuttlePadFree, the cursor's "can I land here"); never while it climbs out of a take-off, never on a repair trip.
    ///    Given: it stops and lands there (<see cref="ZObject.ShuttleExitOnLanding"/>), and the moment it stands on the ground the EXIT is
    ///    the ground's (the ramp down once it stands level, the squad out on its back slots). Called off by any new order to the ship and
    ///    when it cannot land after all (it hovers there; nobody gets out by himself). On the ground EXIT is unchanged.
    /// </summary>
    public partial class ZObject
    {
        /// <summary>DV118: a shuttle with nobody aboard and no pilot (the null team).</summary>
        public bool ShuttleIsEmpty => IsShuttle && Owner == Team.Neutral && Drivers.Count == 0 && Passengers.Count == 0;

        /// <summary>DV118 (ZObject.CanTakePassengers): this empty shuttle takes a squad of `team` now: a human team, the ship standing level
        /// on the ground. The squad's leader will be its pilot.</summary>
        internal bool ShuttleEmptyTakes(Team team) =>
            ShuttleIsEmpty && !Destroyed && KillAt < 0 && World != null && World.IsHumanTeam(team) && ShuttleState == ZShuttleState.Landed && ShuttleLift <= 0
            && World.Time >= ShuttleLevelledAt;

        /// <summary>ZObject.ShuttleRampStep: an empty ship's ramp is called by a human team's robot ordered into it within
        /// ZWorld.ShuttleRampCallM of the ramp foot (the crewed ship's ShuttleRampCalled needs the robot to be of the ship's own team).</summary>
        internal bool ShuttleEmptyRampCalled()
        {
            if (!ShuttleIsEmpty || World == null) return false;
            bool foot = false; int fx = 0, fy = 0;
            double lim = ZWorld.ShuttleRampCallM * 8.0;
            foreach (var o in World.Objects)
            {
                if (!o.IsRobot || o.Destroyed || o.KillAt >= 0 || o.Waypoints.Count == 0) continue;
                var wp = o.Waypoints[0];
                if (wp.mode != WpMode.Enter || wp.refId != Id || !wp.playerGiven || !ShuttleEmptyTakes(o.Owner)) continue;
                if (!foot) { (fx, fy) = ShuttleBackPoint(0); foot = true; }
                double dx = o.CenterX - fx, dy = o.CenterY - fy;
                if (dx * dx + dy * dy <= lim * lim) return true;
            }
            return false;
        }

        /// <summary>ZObject.ShuttleEnterStep, first: an ENTER waypoint on an empty shuttle that nobody ordered is dropped (true = dropped).</summary>
        internal bool ShuttleEmptyAutoDrop(ZObject t)
        {
            if (t == null || !t.ShuttleIsEmpty || Waypoints.Count == 0 || Waypoints[0].playerGiven) return false;
            KillWP(); ShuttleEmptyAutoRefused++;
            return true;
        }

        /// <summary>Check counters (all shuttles): ENTER waypoints on an empty shuttle that nobody ordered, dropped; empty shuttles taken
        /// by an ordered squad; EXITs given in the air, those that ended on the ground with the squad out, those called off (a new order,
        /// it could not land).</summary>
        public static int ShuttleEmptyAutoRefused, ShuttleEmptyTaken, ShuttleAirExits, ShuttleAirExitsLanded, ShuttleAirExitsDropped;

        // ------------------------------------------------------------------ EXIT in the air
        /// <summary>EXIT was given in the air: the ship lands where it stops and the squad gets out once it stands on the ground.</summary>
        public bool ShuttleExitOnLanding { get; internal set; }

        /// <summary>Where an EXIT (or a STOP) given now puts the ship down (px, the middle of a tile): flying, where its braking ends; else
        /// the point on its line (ZWorld.ShuttleOrder's STOP rule), clamped to the map.</summary>
        public (int x, int y) ShuttleExitSpot()
        {
            var (bx, by) = ShuttleState == ZShuttleState.Cruise ? ShuttleBrakeEnd() : (ShuttleLineX, ShuttleLineY);
            return World != null ? World.ShuttleClamp(bx, by) : (bx, by);
        }

        /// <summary>EXIT may be given now in the air (ZObject.CanEjectCrew, besides passengers aboard): a human team's ship that flies,
        /// hovers or comes down, not climbing out of a take-off, not on a repair trip, and the spot it would come down on is a landing spot
        /// (ZWorld.ShuttlePadFree: the cursor's "can I land here"; coming down or turning onto its landing heading: that spot was found free).</summary>
        public bool ShuttleAirExitOk()
        {
            if (!IsShuttle || !ShuttleAirborne || Destroyed || KillAt >= 0 || World == null || !World.IsHumanTeam(Owner) || Drivers.Count == 0) return false;
            if (ShuttleState == ZShuttleState.Climb) return false;                                           // the take-off climbs out first (2 s)
            if (ShuttleRepairTarget >= 0 || ShuttleDeckTurning || ShuttleOnDeckApproach()) return false;     // a repair trip lands on the station's deck
            if (ShuttleState == ZShuttleState.Descend || ShuttleLandTurning) return true;                    // coming down onto a spot found free
            if (ShuttleViaCount > 0) return World.ShuttlePadFree(ShuttleGoalX, ShuttleGoalY, this, out _);   // DV125: on a route they get out at its last point (Sim/ZWorld.ShuttleQueue.cs)
            var (x, y) = ShuttleExitSpot();
            return World.ShuttlePadFree(x, y, this, out _);
        }

        /// <summary>ZObject.ShuttleRampStep, first: an EXIT given in the air becomes the ground's EXIT the moment the ship stands on the
        /// ground (the ramp then comes down once it stands level); it ends with the passengers or the ship.</summary>
        internal void ShuttleAirExitTick()
        {
            if (!ShuttleExitOnLanding) return;
            if (Destroyed || KillAt >= 0 || !ShuttleHasLeavers) { ShuttleExitOnLanding = false; return; }   // K256f2: the passengers, or a hero pilot (Sim/ZWorld.ShuttleHeroes.cs)
            if (ShuttleState == ZShuttleState.Landed && ShuttleLift <= 0)
            {
                ShuttleExitOnLanding = false; ShuttleExitPending = true; ShuttleAirExitsLanded++;
            }
        }

        /// <summary>A new order to the ship, or it could not land after all: the EXIT given in the air is called off.</summary>
        internal void ShuttleAirExitOff()
        {
            if (!ShuttleExitOnLanding) return;
            ShuttleExitOnLanding = false; ShuttleAirExitsDropped++;
        }
    }

    public sealed partial class ZWorld
    {
        /// <summary>ZWorld.RobotEnterObject, first (before DV15's passenger boarding): squad leader r boards the EMPTY shuttle v at its ramp
        /// foot. r becomes its pilot (Drivers[0], DriverType, his rank), his living minions its passengers (squad order, PassengerType, the
        /// squad's grenade pool when there are passengers), the ramp's foot schedule and hold for the whole squad (the view walks every
        /// member up the ramp), the hero seats beside both lists, then Entered (a capture: flag false, value = robots aboard) and the
        /// ship's new team. False = not this case (every other boarding goes on as before).</summary>
        internal bool ShuttleTakeEmpty(ZObject r, ZObject v, HeroBoard heroIn)
        {
            if (r == null || v == null || !v.IsShuttle || !v.ShuttleIsEmpty || v.Destroyed || r.KillAt >= 0 || r.IsMinion || !r.IsRobot || !v.ShuttleEmptyTakes(r.Owner)) return false;
            var team = r.Owner;
            ShuttleBoardSchedule(r, v);                                                                  // the foot times of the leader and his living minions, the ramp's hold
            v.DriverType = r.Type; v.Drivers.Clear(); v.Drivers.Add(r.Health);
            v.TakeCrewFrom(r);                                                                           // veterancy DV12: the pilot's rank travels with him
            r.KillAt = 0;
            v.PassengerType = r.Type; v.Passengers.Clear(); v.PassengerGrenades = 0;
            foreach (var m in r.Minions) { if (m == null || m.KillAt >= 0) continue; v.Passengers.Add(m.Health); m.KillAt = 0; }
            if (v.Passengers.Count > 0) v.PassengerGrenades = r.Grenades;                                // the squad's pool goes with the ones who can get out
            ShuttleEmptySeats(v, heroIn);
            ShuttleHeroPilotIn(v, r);                                                                    // K256f2: a lone hero pilot keeps the squad's pool for when he gets out (Sim/ZWorld.ShuttleHeroes.cs)
            v.ResetDamageInfo();
            ZObject.ShuttleEmptyTaken++;
            Emit(ZEventType.Entered, r, v, 0, 0, v.Drivers.Count + v.Passengers.Count, 0, false);          // a capture (the view holds the empty look until the squad sits)
            ResetObjectTeam(v, team);
            return true;
        }

        /// <summary>DV108 seats of a squad that took an empty shuttle: the leader's beside Drivers, his living minions' beside Passengers.</summary>
        void ShuttleEmptySeats(ZObject v, HeroBoard b)
        {
            if (b == null || v == null) return;
            var ds = new List<ZHeroSeat> { ZHeroSeat.Of(b.all[0]) };
            var ps = new List<ZHeroSeat>();
            for (int i = 1; i < b.all.Length; i++) if (b.alive[i]) ps.Add(ZHeroSeat.Of(b.all[i]));
            v.HeroDriverSeats = ZHeroSeat.Fit(ds, v.Drivers.Count);
            v.HeroPassengerSeats = ZHeroSeat.Fit(ps, v.Passengers.Count);
            CountIn(ds); CountIn(ps);
        }

        /// <summary>ZWorld.ShuttleExitOrder hook: EXIT given while the ship is up. Taken only where it may land and let them out
        /// (ZObject.ShuttleAirExitOk): flying or hovering, it is sent down where it stops (the STOP rule's spot); coming down (or turning
        /// onto its landing heading), it goes on. The squad gets out when it stands on the ground (ZObject.ShuttleAirExitTick).</summary>
        internal bool ShuttleAirExit(ZObject v)
        {
            if (v == null || !v.ShuttleAirExitOk()) return false;
            if (v.ShuttleExitOnLanding) return true;                                                     // given already: it is on its way down there
            if (v.ShuttleViaCount == 0 && ((v.ShuttleState == ZShuttleState.Cruise && !v.ShuttleLandTurning) || v.ShuttleState == ZShuttleState.Hover))   // DV125: on a route it flies on and lands at the last point
            {
                var (x, y) = v.ShuttleExitSpot();
                v.ShuttleSetGoal(x, y);
            }
            v.ShuttleExitOnLanding = true; ZObject.ShuttleAirExits++;
            return true;
        }

        // ------------------------------------------------------------------ a shuttle in a map's data
        /// <summary>Check counters: shuttles made from a map's entries, entries skipped (the shuttle rule off or the option SHUTTLE off).</summary>
        public int ShuttleMapMade { get; private set; }
        public int ShuttleMapSkipped { get; private set; }

        /// <summary>ZWorld.CreateFromMap, first: a map's entry {"kind": "vehicle:shuttle", "owner": "null" | "red" | "blue", "x", "y"} (x, y
        /// the top-left tile of its 2 x 2 tiles, as every vehicle's) is a playable shuttle with its middle there: "null" = an EMPTY one, a team
        /// = crewed by its pilot (the CPU never flies one). It faces north (ZWorld.ShuttleDropHeadingDeg), turned to a free side by the
        /// landing spot rule when north does not fit. It never becomes level 16's own ship (ZWorld.PlayableShuttle). The shuttle rule off
        /// ('Zod exact') or the option SHUTTLE off: the entry makes nothing. True = the entry was a shuttle's (made or skipped).</summary>
        internal bool ShuttleFromMap(ZMapData.Obj o, Team owner)
        {
            if (o == null || string.IsNullOrEmpty(o.kind) || o.Category != "vehicle" || o.kind.IndexOf(':') < 0 || o.Type != ShuttleType) return false;
            if (!ShuttlePlayableDeviation || !ShuttleOn) { ShuttleMapSkipped++; return true; }
            int cx = o.x * 16 + 16, cy = o.y * 16 + 16;
            double hd = ShuttleDropHeadingDeg;
            if (ShuttleLandSpot(cx, cy, null, hd, out double free, out _)) hd = free;
            var keep = PlayableShuttle;
            var s = CreatePlayableShuttle(cx, cy, owner, (float)ZObject.ShuttleNormDeg(hd));
            if (keep == null && PlayableShuttle == s) PlayableShuttle = null;                            // only level 16's own ship (or the first one the testing panel drops) is that
            ShuttleMapMade++;
            return true;
        }
    }
}
