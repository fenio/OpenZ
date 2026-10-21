using System;
using System.Collections.Generic;

namespace OpenZ.Sim
{
    /// <summary>Where the playable shuttle is in its flight (DV118, ZWorld.Shuttle.cs).</summary>
    public enum ZShuttleState { Landed = 0, Climb = 1, Cruise = 2, Descend = 3, Hover = 4 }

    /// <summary>What the playable shuttle just did (DV118): read by the view for its sounds, voices and dust, and by the checks.</summary>
    public enum ZShuttleNoteKind { TakeOff, Arrived, Landed, CannotLand, Aborted, Crashed, Wrecked }

    /// <summary>One thing the shuttle did. seq counts up from 1 (the view keeps the last one it played).</summary>
    public struct ZShuttleNote
    {
        public int seq;
        public ZShuttleNoteKind kind;
        public int id, x, y, aboard;
        /// <summary>It was hit by an enemy in the last <see cref="ZWorld.ShuttleUnderFireSec"/> s (a landing "coming in hot").</summary>
        public bool underFire;
        /// <summary>CannotLand / Aborted: why ("water", "rocks", "blocked", "units", "building", "flag", "bridge", "item", "map edge",
        /// "repair bay busy", "new order"); Landed: <see cref="ZWorld.ShuttleWhyRepairPad"/> on a repair landing, else "".</summary>
        public string why;
        public double time;
    }

    /// <summary>
    /// OpenZ deviation DV118 (level 16, a new unit that Zod does not have): the robots' shuttle beside the red fort is the player's
    /// transport. One switch, <see cref="ZWorld.ShuttlePlayableDeviation"/> (the game: on, off in 'Zod exact'; headless default off,
    /// so every older counted check keeps its numbers), and the GAMEPLAY option SHUTTLE (<see cref="ZWorld.ShuttleOn"/>: off = no
    /// shuttle on level 16 at all, read when a level starts).
    ///  * The unit: a vehicle of type <see cref="ZWorld.ShuttleType"/>, created at map load in the place of the decoration of DV95
    ///    (the middle of its square, in the same place of the creation order, so every later object keeps its id), owned by the
    ///    player's team (<see cref="ZWorld.ShuttleTeam"/>), facing <see cref="ZWorld.L16ShuttleHeadingDeg"/> (the scene's ship). Hit
    ///    points of the light tank, no weapon, never sniped (its pilot sits inside the closed cockpit): the pilot is the game's usual
    ///    one crew robot of an owned vehicle; he never comes out and never shoots. It closes no tile (a unit, not a barrier): the
    ///    pathing layer treats it as a parked hull of radius <see cref="ZWorld.ShuttleRadiusPx"/> on the ground (robots plan round
    ///    it, never through it) and leaves it out of the ground traffic while it is up.
    ///  * Boarding and unloading through the rear ramp (ZWorld.ShuttleRamp.cs): one squad (heroes too, DV108), ordered into it as into
    ///    the APC (DV15's passenger rules), only while it stands on the ground. The squad walks round the hull to its slots behind the
    ///    ship; the ramp comes down when the first is near; the leader boards at the ramp foot once it is fully down and the squad
    ///    goes in one by one; the ramp then shuts. EXIT brings the ramp down and the squad stands in a line behind the ship; refused
    ///    while it is up, while hovering and while a squad is still walking in (the HUD greys the button: CanEjectCrew).
    ///  * Flight (ZObject.ShuttleStep, instead of Zod's movement; the eased flight model of ZWorld.ShuttleFlight.cs): a move order =
    ///    the ramp shut first, then climb <see cref="ZWorld.ShuttleClimbSec"/> s straight up to <see cref="ZWorld.ShuttleCruiseM"/> m
    ///    while it turns toward the target, fly there in a smooth curve at up to the jeep's speed x <see cref="ZWorld.ShuttleJeepFactor"/>
    ///    (speeding up gently and braking on a planned stop, the acceleration eased at both ends: an S-curve), then descend
    ///    <see cref="ZWorld.ShuttleDescendSec"/> s and land. Nobody gets out by himself. A target where it cannot land
    ///    (<see cref="ZWorld.ShuttlePadFree"/>: a 6 x 6 m pad of ground or road tiles with no building, rock, hut, tree, detail, water,
    ///    bridge, flag, crate or unit on it): it HOVERS there until the next move order. A new order on the way: it takes the new
    ///    target (climbing back first if it was coming down); STOP: it brakes and lands where the braking ends, or hovers there.
    ///  * Under fire (ZWorld.ShuttleEvade.cs): up in the air, the first projectile aimed at it makes it glide a few metres sideways off
    ///    its line or hover spot, away from the shooters, in ONE eased move; it holds there while the fire goes on and drifts back slowly
    ///    once it stops (never a zig-zag); its flight plan goes on unchanged, it never leaves the map and it does not strafe while it comes
    ///    down to land (the descent waits until it is back).
    ///  * A repair station (ZWorld.ShuttleRepair.cs): it lands ON the station's deck, turned onto <see cref="ZWorld.ShuttleDeckHeadingDeg"/>
    ///    in a slow hover turn before the descent, once the station's crane has moved out of the way (<see cref="ZWorld.ShuttleCraneMoveSec"/>).
    ///  * The landing: about one in <see cref="ZWorld.ShuttleRoughEvery"/> (by the landing's number, never on a repair station) is a rough
    ///    one: the crash at the touchdown (<see cref="ZWorld.ShuttleRoughCrashSec"/>), resting tilted
    ///    (<see cref="ZWorld.ShuttleRoughRestSec"/>), the levelling (<see cref="ZWorld.ShuttleRoughLevelSec"/>); EXIT, the ramp and a
    ///    take-off wait until it stands level (ShuttleLevelledAt). No hit points are lost.
    ///  * The map edge: every target is clamped to the map, <see cref="ZWorld.ShuttleEdgeTiles"/> tiles in from each edge, and the
    ///    flight never takes its middle nearer than one tile (plus half its box) to an edge.
    ///  * Flags: it never captures one (only robots who came out of it do).
    ///  * Fire: every weapon can hit it. Rifles, lasers, flame, the jeep's and the gatling's guns are direct hits on the target (no
    ///    height in the rules), and while it is up no wall, cliff or building on the ground hides it (ZSight.ClearFrom) and no closed
    ///    tile under or before it stops a shot (the engage barrier test is skipped for it: parts/sim_c.py). Rockets,
    ///    shells, grenades, the howitzer, missiles and the fort gun fly at its place with the game's own lead (its flight velocity is
    ///    its Dx / Dy): a blast aimed at it up there hurts only the shuttle; a blast on the ground never reaches it while it is up.
    ///  * Destroyed (in the air or on the ground): everyone aboard is lost (robots still waiting at their slots outside live); one
    ///    row per lost robot goes into ZWorld.ShuttleLostCrew (type, hero, place, the moment, the ship's height and velocity) for the
    ///    view, which throws their bodies out among the pieces.
    ///  * The CPU never uses it: the CPU's order rounds leave it out (ZBot.CollectOurUnits_3), an order from a CPU team is refused,
    ///    and only a human team's squad can board it.
    ///  * More of them: the testing panel drops more shuttles (<see cref="ZWorld.SpawnShuttle"/>), each the same full unit on any level;
    ///    every rule here is per ship (level 16's own ship stays <see cref="ZWorld.PlayableShuttle"/>).
    /// No random draw, no frame time; every change goes through the existing commands (SEND_WAYPOINTS, EJECT_VEHICLE).
    /// </summary>
    public partial class ZObject
    {
        /// <summary>DV118: this is the playable shuttle.</summary>
        public bool IsShuttle => Kind == ZKind.Vehicle && Type == ZWorld.ShuttleType;
        /// <summary>DV118 flight state.</summary>
        public ZShuttleState ShuttleState;
        /// <summary>DV118: 0 = on the ground .. 1 = at the cruise height.</summary>
        public double ShuttleLift;
        /// <summary>DV118: the target (centre px), -1 = none yet.</summary>
        public int ShuttleGoalX = -1, ShuttleGoalY = -1;
        /// <summary>DV118: the ship's facing at all times, landed too (degrees, atan2 of the sim's y over x: 0 = east, 90 = south; the
        /// view's yaw = this + 90). Set at creation, turned only by the flight model (ZWorld.ShuttleFlight.cs).</summary>
        public float ShuttleHeadingDeg;
        /// <summary>DV118 counts: take-offs, landings, arrivals where it could not land.</summary>
        public int ShuttleFlights, ShuttleLandings, ShuttleHovers;
        /// <summary>DV118: why it hovers (empty while it can land or is on the ground).</summary>
        public string ShuttleNoLand = "";
        double shuttleFx, shuttleFy; bool shuttleNewGoal;

        /// <summary>DV118: off the ground (climbing, flying, hovering or coming down).</summary>
        public bool ShuttleAirborne => IsShuttle && (ShuttleState != ZShuttleState.Landed || ShuttleLift > 0);
        /// <summary>DV118: boarding and unloading are possible now (true for every other vehicle): standing on the ground, a human team's.</summary>
        public bool ShuttleDoorOk => !IsShuttle || (ShuttleState == ZShuttleState.Landed && ShuttleLift <= 0 && World != null && World.IsHumanTeam(Owner));
        /// <summary>DV118: its height above the ground in metres (the view, the checks).</summary>
        public double ShuttleHeightM => IsShuttle ? ShuttleLift * ZWorld.ShuttleCruiseM : 0;

        /// <summary>DV118: puts it at a centre (px), sub-pixel kept (no strafe, no plan).</summary>
        public void ShuttlePlace(int cx, int cy) { shuttleFx = cx; shuttleFy = cy; ShuttleStrafeReset(); ShuttlePlanOff(); ShuttleSetPos(); }

        /// <summary>Its place: the point on its line plus the strafe's offset (ZWorld.ShuttleEvade.cs).</summary>
        void ShuttleSetPos()
        {
            double tlx = shuttleFx + shuttleOffX - WidthPix * 0.5, tly = shuttleFy + shuttleOffY - HeightPix * 0.5;
            int lx = (int)Math.Floor(tlx), ly = (int)Math.Floor(tly);
            SetCords(lx, ly); XOver = tlx - lx; YOver = tly - ly;
        }

        /// <summary>The point on its line as whole px (CenterX / CenterY without the strafe): the pad tests use it.</summary>
        internal int ShuttleLineX => (int)Math.Floor(shuttleFx);
        internal int ShuttleLineY => (int)Math.Floor(shuttleFy);

        /// <summary>DV118: a new target (already clamped to the map), taken at its next step (a planned stop is called off).</summary>
        internal void ShuttleSetGoal(int x, int y) { ShuttleGoalX = x; ShuttleGoalY = y; shuttleNewGoal = true; ShuttlePlanOff(); ShuttleViasClear(); }   // DV125: a new goal ends a route (Sim/ZWorld.ShuttleQueue.cs)

        static int ShuttleHash(int a, int b) { unchecked { uint h = (uint)(a * 73856093) ^ (uint)(b * 19349663); h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15; return (int)(h & 0x7fffffff); } }

        /// <summary>The descent begins: is this landing a rough one (by the landing's number; never on a repair station)? Which side first?</summary>
        void ShuttleDescentBegins()
        {
            int n = ShuttleLandings + 1;
            ShuttleRoughLanding = ShuttleRepairTarget < 0 && (ZWorld.ShuttleRoughProofNext || ShuttleHash(Id, n) % ZWorld.ShuttleRoughEvery == 0);
            ShuttleRoughSide = (ShuttleHash(Id + 7, n) & 1) == 0 ? 1 : -1;
        }

        /// <summary>The touchdown (the lift reached 0): the rough landing's timeline, or level at once.</summary>
        void ShuttleTouchdown()
        {
            double now = World.Time;
            ShuttleTouchdownAt = now;
            if (ShuttleRoughLanding && !ShuttleOnRepairPad)
            {
                ShuttleRoughLandings++; ZWorld.ShuttleRoughProofNext = false;
                ShuttleLevelStartAt = now + ZWorld.ShuttleRoughCrashSec + ZWorld.ShuttleRoughRestSec;
                ShuttleLevelledAt = ShuttleLevelStartAt + ZWorld.ShuttleRoughLevelSec;
            }
            else { ShuttleRoughLanding = false; ShuttleLevelStartAt = ShuttleLevelledAt = now; }
        }

        /// <summary>Over its repair station's deck point, the pad free: the hover turn begins (state Cruise, no movement) and the station's
        /// crane is called away (a call already standing for that station is kept: the crane is already moving or away).</summary>
        void ShuttleDeckBegin()
        {
            var st = World.Get(ShuttleRepairTarget);
            ShuttleState = ZShuttleState.Cruise; ShuttleDeckTurning = true; ShuttleNoLand = "";
            if (st != null && !(ShuttleCraneStation == st.Id && ShuttleCraneCallAt >= 0)) { ShuttleCraneStation = st.Id; ShuttleCraneCallAt = World.Time; ShuttleCraneCalls++; }
        }

        /// <summary>The hover turn over the station: onto ZWorld.ShuttleDeckHeadingDeg at most ZWorld.ShuttleDeckTurnMaxDeg deg/s (eased); the
        /// descent begins once only ZWorld.ShuttleDeckSeatLeftDeg are left, the turn is slow and the crane is out of the way. The bay taken
        /// meanwhile (or anything on the pad): it hovers there ("repair bay busy"), as at the arrival.</summary>
        void ShuttleDeckStep(double dt)
        {
            var w = World;
            if (!ShuttleOnDeckApproach()) { ShuttleDeckTurning = false; ShuttleNoTurn(dt); return; }      // the repair called off or the station gone: on with the flight
            double err = ShuttleStrafeBusy ? ShuttleDelta(ShuttleHeadingDeg, ZWorld.ShuttleDeckHeadingDeg) : ShuttleHoverTurn(ZWorld.ShuttleDeckHeadingDeg, dt);
            if (ShuttleStrafeBusy) ShuttleNoTurn(dt);                                                         // a strafe from before still drifting back: the turn waits for it
            if (!w.ShuttlePadFree(ShuttleLineX, ShuttleLineY, this, out string why))
            {
                ShuttleDeckTurning = false; ShuttleState = ZShuttleState.Hover; ShuttleNoLand = why; ShuttleHovers++; w.ShuttleNote(ZShuttleNoteKind.CannotLand, this, why);
                return;
            }
            if (Math.Abs(err) <= ZWorld.ShuttleDeckSeatLeftDeg && Math.Abs(ShuttleTurnRateNow) <= ZWorld.ShuttleDeckSeatRateDeg && !ShuttleStrafeBusy && w.Time >= ShuttleCraneClearAt - 1e-9)
            {
                ShuttleDeckTurning = false; ShuttleState = ZShuttleState.Descend; ShuttleDescentBegins(); w.ShuttleNote(ZShuttleNoteKind.Arrived, this);
            }
        }

        /// <summary>The crane's call ends when it is not needed any more: the ship destroyed or the station gone; waiting over a busy bay (the
        /// crane works on the other vehicle); back at cruise height and off that deck (it flew away, or a new order took it).</summary>
        void ShuttleCraneTick()
        {
            if (ShuttleCraneStation < 0) return;
            var st = World.Get(ShuttleCraneStation);
            bool keep = st != null && !st.Destroyed && !Destroyed && KillAt < 0;
            if (keep && !ShuttleDeckTurning && ShuttleState != ZShuttleState.Descend && ShuttleState != ZShuttleState.Landed)
            {
                if (ShuttleState == ZShuttleState.Hover) keep = false;
                else if (ShuttleLift >= 1.0) keep = ShuttleDeckPoint(st, out int dx, out int dy) && Math.Abs(ShuttleLineX - dx) <= 16 && Math.Abs(ShuttleLineY - dy) <= 16;
            }
            if (!keep) { ShuttleCraneStation = -1; ShuttleCraneCallAt = -1.0; }
        }

        /// <summary>Check counter: crane calls made (all shuttles).</summary>
        public static int ShuttleCraneCalls;

        /// <summary>DV118 (ZObject.ProcessServer hook, before any Zod movement): the shuttle's whole step. Always true (nothing of
        /// Zod's own step runs for it: no path, no weapon, no lid, no dodge).</summary>
        internal bool ShuttleStep(double time, double dt)
        {
            var w = World;
            if (Waypoints.Count > 0) Waypoints.Clear();                                  // nothing but its own flight plan moves it (a scuffle nudge, a dodge step: dropped)
            if (AttackObject != null) Disengage();                                         // it has no weapon
            if (w == null || dt <= 0) return true;
            double hd0 = ShuttleHeadingDeg, px0 = shuttleFx + shuttleOffX, py0 = shuttleFy + shuttleOffY;
            ShuttleRampStep();                                                             // the rear ramp's swing, the exit when it is fully down (ZWorld.ShuttleRamp.cs)
            ShuttleTrafficBegin();                                                         // K338: several ships: the spots of new goals, the flags of the last tick (ZWorld.ShuttleTraffic.cs)
            if (shuttleNewGoal)
            {
                ShuttleDeckTurning = false; ShuttleLandTurning = false; ShuttleLandHeadingSet = false;   // a new order ends a hover turn over a repair station (K256e: and a landing turn)
                double gx = ShuttleGoalX - shuttleFx, gy = ShuttleGoalY - shuttleFy;
                bool far = gx * gx + gy * gy > (double)ZWorld.ShuttleSameSpotPx * ZWorld.ShuttleSameSpotPx || ShuttleViaCount > 0;   // DV125: a route back to where it stands still flies
                switch (ShuttleState)
                {
                    case ZShuttleState.Landed:
                        if (!far) { shuttleNewGoal = false; ShuttleTakeOffHeld = false; break; }
                        if (ShuttleRampPhase != ZShuttleRamp.Shut || ShuttleExitPending || w.Time < ShuttleRampHoldUntil || w.Time < ShuttleLevelledAt) { ShuttleTakeOffHeld = true; break; }   // the ramp first (and level after a rough landing): the order is kept and taken the moment it is shut
                        if (ShuttleLiftWait(dt) || ShuttleAirRoomWait()) { ShuttleTakeOffHeld = true; break; }   // K256e: a unit within 2 m of the footprint: it waits (at most 3 s), robots are blown aside (ZWorld.ShuttleGround.cs); K338: another ship up within 10 m: it waits, that ship gives way
                        shuttleNewGoal = false; ShuttleTakeOffHeld = false; ShuttleRoughLanding = false;
                        ShuttleState = ZShuttleState.Climb; ShuttleFlights++; ShuttleNoLand = ""; w.ShuttleNote(ZShuttleNoteKind.TakeOff, this);
                        break;
                    case ZShuttleState.Descend: shuttleNewGoal = false; ShuttleState = ZShuttleState.Climb; w.ShuttleNote(ZShuttleNoteKind.Aborted, this, "new order"); break;   // up again first, then on
                    case ZShuttleState.Hover: shuttleNewGoal = false; ShuttleState = ZShuttleState.Cruise; ShuttleNoLand = ""; break;
                    default: shuttleNewGoal = false; break;
                }
            }
            if (ShuttleState != ZShuttleState.Descend) shuttleSeatTurn = false;
            switch (ShuttleState)
            {
                case ZShuttleState.Landed:
                    ShuttleHold(dt); ShuttleNoTurn(dt);
                    ShuttleRepairStep();                                                                          // item 271: repaired on a station's deck, then back
                    break;
                case ZShuttleState.Climb:
                    ShuttleHold(dt); ShuttleClimbTurn(dt);                                                         // on the spot, turning toward the goal
                    ShuttleLift = Math.Min(1.0, ShuttleLift + dt / ZWorld.ShuttleClimbSec);
                    if (ShuttleLift >= 1.0) ShuttleState = ZShuttleState.Cruise;
                    break;
                case ZShuttleState.Cruise:
                    if (ShuttleDeckTurning) { ShuttleHold(dt); ShuttleDeckStep(dt); break; }                     // over its repair station: the hover turn, the crane, then down
                    if (ShuttleLandTurning) { ShuttleHold(dt); ShuttleLandTurnStep(dt); break; }                 // K256e: the hover turn so the ramp faces a free side, then down (ZWorld.ShuttleGround.cs)
                    if (ShuttleFlyStep(dt) && !ShuttleHoldForShip())                              // K338: on a spot another ship is leaving it holds until that ship has gone
                    {   // the pad is tested under the point on its line (a strafe eases back before it comes down)
                        if (w.ShuttleLandSpot(ShuttleLineX, ShuttleLineY, this, ShuttleHeadingDeg, out double landHd, out string why))   // K256e: the footprint + 0.5 m and the ramp lane free, on its heading or turned to a free side
                        {
                            if (ShuttleOnDeckApproach()) ShuttleDeckBegin();                                          // onto a repair station's deck: it turns first, the crane moves away
                            else ShuttleLandBegin(landHd);                                                            // K256e: straight down, or a hover turn onto that heading first
                        }
                        else { ShuttleState = ZShuttleState.Hover; ShuttleNoLand = why; ShuttleHovers++; w.ShuttleNote(ZShuttleNoteKind.CannotLand, this, why); }
                    }
                    break;
                case ZShuttleState.Hover:
                    ShuttleHold(dt);
                    if (ShuttleOnDeckApproach() && !ShuttleStrafeBusy) ShuttleHoverTurn(ZWorld.ShuttleDeckHeadingDeg, dt);   // the bay busy: it turns onto the deck heading while it waits
                    else ShuttleNoTurn(dt);
                    if (ShuttleLift < 1.0) ShuttleLift = Math.Min(1.0, ShuttleLift + dt / ZWorld.ShuttleClimbSec);
                    if (ShuttleRepairRetry() && w.ShuttlePadFree(ShuttleLineX, ShuttleLineY, this, out _))
                    {   // item 271: the repair bay is free again: the crane is called and it comes down by itself (a plain move order hovers until the next order)
                        ShuttleNoLand = ""; ShuttleDeckBegin();
                    }
                    break;
                case ZShuttleState.Descend:
                {
                    ShuttleHold(dt);
                    if (!ShuttleDescentPadFree(out string why))                                                    // K256e: on the heading it comes down on (ZWorld.ShuttleGround.cs)
                    {   // a unit walked onto the pad while it came down: up again, then it hovers there
                        ShuttleState = ZShuttleState.Climb; shuttleSeatTurn = false; ShuttleLandHeadingSet = false; w.ShuttleNote(ZShuttleNoteKind.Aborted, this, why);
                        break;
                    }
                    if (ShuttleStrafeBusy) { ShuttleNoTurn(dt); break; }                                          // the strafe eases back onto its line first: the descent waits for it
                    bool deck = ShuttleOnDeckApproach();
                    ShuttleDescendTurn(dt, deck);                                                                  // onto a repair station's deck: the last degrees onto the deck heading by touchdown
                    ShuttleLift = Math.Max(0.0, ShuttleLift - dt / ZWorld.ShuttleDescendSec);
                    if (ShuttleLift <= 0)
                    {
                        ShuttleSeatDone(deck);
                        ShuttleState = ZShuttleState.Landed; ShuttleLandings++;
                        ShuttleRepairArrive();                                                                     // item 271: the station takes it first, so the note knows
                        ShuttleTouchdown();                                                                        // a rough landing: the crash, the tilted rest, the levelling (EXIT waits)
                        w.ShuttleNote(ZShuttleNoteKind.Landed, this, ShuttleOnRepairPad ? ZWorld.ShuttleWhyRepairPad : "");
                    }
                    break;
                }
            }
            ShuttleThreatScan();                                                           // projectiles aimed at it (ZWorld.ShuttleEvade.cs)
            bool strafeOk = ShuttleLift >= ZWorld.ShuttleStrafeMinLift && (ShuttleState == ZShuttleState.Climb || ShuttleState == ZShuttleState.Cruise || ShuttleState == ZShuttleState.Hover)
                            && !ShuttleDeckTurning && !ShuttleLandTurning && !(ShuttleState == ZShuttleState.Hover && ShuttleOnDeckApproach());   // not while it turns or waits over its repair station (it is about to land); K256e: nor over its landing spot
            ShuttleKickStep(dt);                                                           // DV129: the kickflip's window, its dash, the evasive self-flip (Sim/ZWorld.ShuttleKick.cs)
            ShuttleStrafeStep(dt, strafeOk);                                               // under fire: one smooth glide sideways, a hold, a slow drift back
            ShuttleTrafficStep(dt);                                                        // K338: giving way to the other ships in the air (its avoid offset, eased, added to the strafe's)
            ShuttleCraneTick();                                                            // the repair station's crane: away while it lands, stands and lifts off there
            ShuttleSetPos();
            Dx = (float)((shuttleFx + shuttleOffX - px0) / dt); Dy = (float)((shuttleFy + shuttleOffY - py0) / dt);   // the real velocity, the strafe included: the game's lead for rockets and shells
            ShuttleTurnRateDeg = (float)(ShuttleDelta(hd0, ShuttleHeadingDeg) / dt);
            return true;
        }
    }

    public sealed partial class ZWorld
    {
        public const string ShuttleType = "shuttle";
        /// <summary>DV118 switch (off = DV95's decoration as before). The game sets it at every start (off in 'Zod exact'); headless default off.</summary>
        public static bool ShuttlePlayableDeviation;
        /// <summary>The GAMEPLAY option SHUTTLE (oz_shuttle, default on). Off: no shuttle on level 16 at all (neither the playable one nor
        /// the decoration; the huts of DV95 stay out of the rules, nothing is closed in its place). Read when a level starts.</summary>
        public static bool ShuttleOn = true;
        /// <summary>DV118: the team that owns it (the game: the player's team).</summary>
        public static Team ShuttleTeam = Team.Red;
        public const double ShuttleCruiseM = 12.0, ShuttleClimbSec = 2.0, ShuttleDescendSec = 2.0, ShuttleJeepFactor = 2.5, ShuttleUnderFireSec = 5.0;
        /// <summary>The landing pad (tiles a side: 3 = 6 x 6 m), the tiles kept free at each map edge, the distance that is "here" (px).</summary>
        public const int ShuttlePadTiles = 3, ShuttleEdgeTiles = 2, ShuttleSameSpotPx = 12;
        const int ShuttleNotesKept = 64;

        /// <summary>DV118: the playable shuttle created at map load (null: another level, the switch off, or the option off); on another
        /// level the first one the testing panel drops (ZWorld.SpawnShuttle).</summary>
        public ZObject PlayableShuttle { get; private set; }
        /// <summary>Shuttles made in this match (map load and the testing panel): 0 = nothing of the shuttle's rules can matter.</summary>
        int shuttlesMade;
        /// <summary>True when the option SHUTTLE was off at this level's start on level 16 (no shuttle at all).</summary>
        public bool L16ShuttleOptionOff { get; private set; }
        /// <summary>DV118: the last things the shuttle did (at most 64; seq counts up).</summary>
        public readonly List<ZShuttleNote> ShuttleNotes = new List<ZShuttleNote>();
        public int ShuttleNoteSeq { get; private set; }
        /// <summary>DV118 counts for the checks.</summary>
        public int ShuttleCpuOrdersRefused { get; private set; }
        public int ShuttlePassengersLost { get; private set; }

        /// <summary>ZWorld.AddL16Shuttle hook (after the two huts left the rules, before the decoration is made): true = no decoration.</summary>
        bool L16ShuttleReplaced()
        {
            if (!ShuttleOn) { L16ShuttleOptionOff = true; sight = null; return true; }
            if (!ShuttlePlayableDeviation) return false;
            var q = L16ShuttleSquare;
            PlayableShuttle = CreatePlayableShuttle((q[0] * 2 + q[2]) * 8, (q[1] * 2 + q[3]) * 8, ShuttleTeam);   // the middle of the decoration's square
            sight = null;
            return true;
        }

        /// <summary>DV118: a playable shuttle standing with its middle at (cx, cy) px, facing headingDeg (map load on level 16: the
        /// scene's ship; the checks on any map).</summary>
        public ZObject CreatePlayableShuttle(int cx, int cy, Team team, float headingDeg = L16ShuttleHeadingDeg)
        {
            var s = CreateObject("vehicle", ShuttleType, cx - 16, cy - 16, team);          // an owned vehicle: its one crew robot is the pilot
            var light = S.Unit("light"); var jeep = S.Unit("jeep");
            s.MaxHealth = s.Health = ShuttleHpFrom(light != null ? light.MaxHealth : 3378);   // DV131 (K443): the light tank's hit points x 1.2, the same for every team (Sim/ZWorld.ShuttleHp.cs)
            s.MoveSpeed = (int)Math.Round((jeep != null ? jeep.move_speed : 17) * ShuttleJeepFactor);
            s.RealMoveSpeed = s.MoveSpeed;
            s.Damage = 0; s.AttackRadius = 0; s.DamageRadius = 0; s.CanSnipe = false; s.CanBeSnipedFlag = false; s.HasLid = false;
            s.ShuttleState = ZShuttleState.Landed; s.ShuttleLift = 0; s.ShuttleHeadingDeg = headingDeg;
            s.ShuttlePlace(cx, cy);
            if (PlayableShuttle == null) PlayableShuttle = s;
            shuttlesMade++;
            return s;
        }

        /// <summary>The testing panel (P, drag and drop): one more full playable shuttle for `team`, standing on the nearest free 6 x 6 m pad
        /// (ShuttlePadFree) within 6 tiles of the dropped point (px), facing headingDeg. The same unit as level 16's (CreatePlayableShuttle:
        /// its pilot, the light tank's hit points x 1.2 (DV131), the next free id); the CPU never uses it (a CPU team's one just stands there). null + why:
        /// the shuttle rule off ('Zod exact'), no free pad near ("no room"). The neutral team: an EMPTY shuttle, no pilot (K256f: only an
        /// order puts a squad in, its leader becomes the pilot, Sim/ZWorld.ShuttleEmpty.cs).</summary>
        public ZObject SpawnShuttle(int px, int py, Team team, float headingDeg, out string why)
        {
            why = "";
            if (!ShuttlePlayableDeviation) { why = "the shuttle rule is off"; return null; }
            // K256f: the neutral team = an EMPTY shuttle (no pilot; Sim/ZWorld.ShuttleEmpty.cs)
            if (Map == null) { why = "no map"; return null; }
            var (cx0, cy0) = ShuttleClamp(px, py);
            int tx0 = cx0 >> 4, ty0 = cy0 >> 4;
            for (int r = 0; r <= 6; r++)
                for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                        var (cx, cy) = ShuttleClamp((tx0 + dx) * 16 + 8, (ty0 + dy) * 16 + 8);
                        if (cx >> 4 != tx0 + dx || cy >> 4 != ty0 + dy) continue;                 // outside the map's edge rule: that tile is not a place
                        if (!ShuttleLandSpot(cx, cy, null, headingDeg, out double dropHd, out _)) continue;   // K256e: the landing spot rule (footprint + 0.5 m, the ramp lane), turned to a free side
                        return CreatePlayableShuttle(cx, cy, team, (float)ZObject.ShuttleNormDeg(dropHd));
                    }
            why = "no room";
            return null;
        }

        /// <summary>SendWaypoints hook for the shuttle (instead of the waypoint list): a human team's first MOVE is its target (clamped
        /// to the map); an empty list (STOP) lands it where braking from here ends (or where it is); anything else (attack, enter)
        /// means nothing to it; UNIT_REPAIR = the repair trip (item 271). A CPU team's order is refused.</summary>
        bool ShuttleOrder(Team team, ZObject o, IList<ZWaypoint> list)
        {
            if (!IsHumanTeam(team)) { ShuttleCpuOrdersRefused++; return false; }
            o.ShuttleAirExitOff();                                                                                // K256f: any new order calls off an EXIT given in the air (Sim/ZWorld.ShuttleEmpty.cs)
            if (list == null || list.Count == 0)
            {
                o.ShuttleRepairLeave();
                var (bx, by) = o.ShuttleState == ZShuttleState.Cruise ? o.ShuttleBrakeEnd() : (o.ShuttleLineX, o.ShuttleLineY);   // the point on its line (a strafe eases back)
                var (sx, sy) = ShuttleClamp(bx, by); o.ShuttleSetGoal(sx, sy); return true;
            }
            if (ShuttleQueueOrder(o, list)) return true;                                                          // DV125: two or more move points: the last is the goal, the rest middle points flown through in the air (Sim/ZWorld.ShuttleQueue.cs)
            foreach (var wp in list)
            {
                if (wp.mode == WpMode.UnitRepair) return ShuttleRepairOrder(o, wp.refId, true);              // item 271: R / the own station: repaired there, then back
                if (wp.mode == WpMode.Move || wp.mode == WpMode.ForceMove)
                {
                    o.ShuttleRepairLeave();                                                                       // item 271: any new order calls it off the station
                    var st = ShuttleStationAt(wp.x, wp.y, o);
                    if (st != null && ShuttleRepairOrder(o, st.Id, false)) return true;                            // item 271: sent onto an own station by hand: repaired, it stays
                    var (gx, gy) = ShuttleClamp(wp.x, wp.y); o.ShuttleSetGoal(gx, gy); return true;
                }
            }
            return false;
        }

        /// <summary>DV118: a target point (px) moved inside the map, <see cref="ShuttleEdgeTiles"/> tiles in from each edge, onto its tile's middle.</summary>
        public (int x, int y) ShuttleClamp(int x, int y)
        {
            if (Map == null) return (x, y);
            int e = ShuttleEdgeTiles;
            int tx = Math.Max(e, Math.Min(Map.width - 1 - e, x >> 4)), ty = Math.Max(e, Math.Min(Map.height - 1 - e, y >> 4));
            return (tx * 16 + 8, ty * 16 + 8);
        }

        /// <summary>DV118, the repair deck's landing rule (K256e: every other spot is ZWorld.ShuttleLandSpot, Sim/ZWorld.ShuttleGround.cs): every
        /// tile of the 6 x 6 m pad round (cx, cy) is ground or road, open for vehicles, and no unit (but itself), building or map item stands on
        /// it; on its repair station's deck point (item 271 / 317) the station's own tiles and the station itself are a landing place, but its
        /// bay must be free for it ("repair bay busy"). why = the first thing in the way.</summary>
        bool ShuttleDeckPadFree(int cx, int cy, ZObject self, out string why)
        {
            why = "";
            if (Map == null) { why = "no map"; return false; }
            int half = ShuttlePadTiles * 8, x0 = cx - half, y0 = cy - half, x1 = cx + half, y1 = cy + half;
            var rs = ShuttleRepairStation(self);                                                                  // item 271 / 317: landing on the station's deck for a repair
            bool deck = rs != null && self.ShuttleDeckPoint(rs, out int ex, out int ey) && Math.Abs(cx - ex) <= 16 && Math.Abs(cy - ey) <= 16;
            if (deck && ((rs.RepairingUnit && rs.RepairOccupantId != self.Id) || rs.BayTakenFor(self))) { why = "repair bay busy"; return false; }
            bool OnDeck(int tx, int ty) => deck && tx * 16 + 8 >= rs.LocX && tx * 16 + 8 < rs.LocX + rs.WidthPix && ty * 16 + 8 >= rs.LocY && ty * 16 + 8 < rs.LocY + rs.HeightPix;
            for (int ty = y0 >> 4; ty <= (y1 - 1) >> 4; ty++)
                for (int tx = x0 >> 4; tx <= (x1 - 1) >> 4; tx++)
                {
                    if (tx < 0 || ty < 0 || tx >= Map.width || ty >= Map.height) { why = "map edge"; return false; }
                    var c = Map.ClassAt(tx, ty);
                    if (c == ZClass.Water || c == ZClass.DeepWater) { why = "water"; return false; }
                    if (c == ZClass.Impassable && !OnDeck(tx, ty)) { why = "rocks"; return false; }
                    if (!Path.TilePassable(tx, ty, false) && !OnDeck(tx, ty)) { why = "blocked"; return false; }
                }
            foreach (var o in Objects)
            {
                if (o == self || o.Destroyed || o.KillAt >= 0) continue;
                if (!(o.LocX < x1 && x0 < o.LocX + o.WidthPix && o.LocY < y1 && y0 < o.LocY + o.HeightPix)) continue;
                if (o.IsUnit) { if (o.ShuttleAirborne) continue; why = "units"; return false; }
                if (o.Kind == ZKind.Building) { if (deck && o == rs) continue; why = "building"; return false; }
                if (o.Kind == ZKind.MapItem) { why = o.Type == "flag" ? "flag" : o.IsBridge ? "bridge" : "item"; return false; }
            }
            return true;
        }

        /// <summary>UpdateObjectHealth hook (right after the death is relayed): destroyed, in the air (Crashed) or on the ground
        /// (Wrecked), everyone aboard is lost; nobody gets out. One ShuttleLostCrew row per lost robot (for the view's bodies).</summary>
        void ShuttleDown(ZObject o)
        {
            if (o == null || !o.IsShuttle) return;
            ShuttleDoorwayDown(o);                                                                              // DV126: a squad on the ramp: drawn outside = saved, past the doorway = lost (Sim/ZWorld.ShuttleDoorway.cs)
            int aboard = o.Passengers.Count;
            ShuttleNote(o.ShuttleAirborne ? ZShuttleNoteKind.Crashed : ZShuttleNoteKind.Wrecked, o, "", aboard);
            ShuttleLostRows(o, aboard);
            ShuttleHeroPilotLost(o);                                                                            // K256f2: a hero pilot is lost with it too: his row after theirs (Sim/ZWorld.ShuttleHeroes.cs)
            ShuttlePassengersLost += aboard;
            o.Passengers.Clear(); o.PassengerGrenades = 0; o.HeroPassengerSeats = null; o.ShuttleExitPending = false;
            ShuttleDeckWreck(o);                                                                                // K256e: on (or over) a repair deck: the bay closed and the crane away for 4 s (ZWorld.ShuttleGround.cs)
            HeavyFallShuttle(o);                                                                                // DV124: shot down in the air, its hull fells the trees where it comes down (ZWorld.HeavyFall.cs)
            o.ShuttleDeckTurning = false; o.ShuttleLandTurning = false; o.ShuttleCraneStation = -1; o.ShuttleCraneCallAt = -1.0;           // the station's crane comes back
        }

        /// <summary>The rows of the squad lost with the ship, in squad order (the leader first): type, hero member, hit points, the
        /// moment and the ship's height, place, velocity and facing then.</summary>
        void ShuttleLostRows(ZObject o, int aboard)
        {
            var seats = o.HeroPassengerSeats;
            for (int k = 0; k < aboard; k++)
            {
                var seat = seats != null && k < seats.Count ? seats[k] : null;
                var kind = seat?.Kind;
                string member = kind?.Members != null && seat.Member >= 0 && seat.Member < kind.Members.Length ? kind.Members[seat.Member] : "";
                ShuttleLostCrew.Add(new ZShuttleLost
                {
                    seq = ++ShuttleLostSeq, shipId = o.Id, team = o.Owner, type = string.IsNullOrEmpty(o.PassengerType) ? "grunt" : o.PassengerType,
                    hero = kind?.Id ?? "", member = member, index = k, count = aboard, hp = o.Passengers[k], time = Time,
                    airborne = o.ShuttleAirborne, heightM = o.ShuttleHeightM, x = o.CenterX, y = o.CenterY, vx = o.Dx, vy = o.Dy, headingDeg = o.ShuttleHeadingDeg,
                });
                if (ShuttleLostCrew.Count > 32) ShuttleLostCrew.RemoveAt(0);
            }
        }

        /// <summary>ProcessMissileDamage hook (true = this blast leaves object o alone): up in the air only a blast aimed at a shuttle
        /// reaches it, and that blast is up there with it, so nobody on the ground is hurt by it. Every shuttle (level 16's and the testing
        /// panel's); nothing changes in a match without one.</summary>
        bool ShuttleAirBlast(ZMissile m, ZObject o)
        {
            if (shuttlesMade == 0) return false;
            if (o.IsShuttle && o.ShuttleAirborne) return m.targetId != o.Id || ShuttleKickBlastLeaves(m, o);   // DV129: a flipping ship dodges some (Sim/ZWorld.ShuttleKick.cs)
            if (m.targetId < 0 || m.targetId == o.Id) return false;
            var t = GetAny(m.targetId);
            return t != null && t.IsShuttle && t.ShuttleAirborne;                   // (also when this very blast has just shot it down)
        }

        internal void ShuttleNote(ZShuttleNoteKind k, ZObject s, string why = "", int aboard = -1)
        {
            if (k == ZShuttleNoteKind.CannotLand && s != null) s.ShuttleAirExitOff();                 // K256f: it cannot land after all: an EXIT given in the air is called off (nobody gets out by himself)
            ShuttleNotes.Add(new ZShuttleNote
            {
                seq = ++ShuttleNoteSeq, kind = k, id = s.Id, x = s.CenterX, y = s.CenterY, aboard = aboard >= 0 ? aboard : s.Passengers.Count,
                underFire = Time - s.LastEnemyHit < ShuttleUnderFireSec, why = why ?? "", time = Time,
            });
            if (ShuttleNotes.Count > ShuttleNotesKept) ShuttleNotes.RemoveAt(0);
        }
    }
}
