using System;
using System.Collections.Generic;

namespace OpenZ.Sim
{
    /// <summary>DV118: where the playable shuttle's rear ramp is in its swing (a rule state: the take-off waits for it).</summary>
    public enum ZShuttleRamp { Shut = 0, Opening = 1, Open = 2, Closing = 3 }

    /// <summary>DV118: why the ramp was last down (the view says "All aboard!" at the close after a boarding).</summary>
    public enum ZShuttleRampUse { None = 0, Boarded = 1, Unloaded = 2 }

    /// <summary>
    /// DV118: one robot lost aboard the shuttle when it was destroyed (everyone aboard dies, in the air or on the ground): what the view
    /// needs to throw its body out among the falling pieces. Written once per lost robot, in squad order, in the tick of the ship's
    /// death (ZWorld.ShuttleLostCrew). Sim units: px, px/s, World.Time s, degrees.
    /// </summary>
    public struct ZShuttleLost
    {
        /// <summary>Counts up from 1 over the match (the view keeps the last one it threw).</summary>
        public int seq;
        /// <summary>The shuttle's id.</summary>
        public int shipId;
        /// <summary>The squad's team.</summary>
        public Team team;
        /// <summary>The robot type ("grunt", "tough" ...; a hero member: its squad's base type).</summary>
        public string type;
        /// <summary>The hero squad's id ("" = a normal robot) and the member's name ("brad", "alan"; "" = a normal robot).</summary>
        public string hero, member;
        /// <summary>Its place in the squad (0 = the leader, then the minions in order) and how many were aboard.</summary>
        public int index, count;
        /// <summary>Its hit points when it was lost.</summary>
        public int hp;
        /// <summary>The moment: World.Time of the ship's death.</summary>
        public double time;
        /// <summary>Shot down in the air (true) or destroyed on the ground (false).</summary>
        public bool airborne;
        /// <summary>The ship's height above the ground then (m: ShuttleHeightM).</summary>
        public double heightM;
        /// <summary>The ship's middle then (px).</summary>
        public int x, y;
        /// <summary>The ship's velocity then (px/s: its Dx / Dy, the strafe included).</summary>
        public float vx, vy;
        /// <summary>The ship's facing then (degrees, ShuttleHeadingDeg).</summary>
        public float headingDeg;
    }

    /// <summary>
    /// DV118, the shuttle's rules as the view reads them: its facing, its turn, speed and acceleration, the rear ramp and the squad's
    /// walk to and from the ramp foot. Sim units throughout: px (1 m = 8 px; world x = px / 8, z = -py / 8), px/s, seconds of
    /// World.Time, degrees (heading = atan2(sim dy, dx): 0 = east, 90 = south, -90 = north; the view's yaw = heading + 90).
    /// The values are written only by the rules (Sim/ZWorld.Shuttle.cs, ZWorld.ShuttleFlight.cs, ZWorld.ShuttleRamp.cs, ZWorld.ShuttleEvade.cs); the view
    /// reads them and acts only through the existing commands (SendWaypoints, Eject).
    /// </summary>
    public partial class ZObject
    {
        // ------------------------------------------------------------------ flight (ZWorld.ShuttleFlight.cs)
        /// <summary>The heading change of the last step / dt (deg/s); + = clockwise seen from above (a right turn).</summary>
        public float ShuttleTurnRateDeg { get; internal set; }
        /// <summary>The flight speed along the heading (px/s, 0 .. RealMoveSpeed; the slow settle onto the goal included).</summary>
        public double ShuttleSpeedPx { get; internal set; }
        /// <summary>The speed change of the last step / dt (px/s^2; + faster, - braking). Eased: it builds up and fades out smoothly
        /// (never a step), so the view can pitch the nose by it: at most vmax / ZWorld.ShuttleAccelSec either way.</summary>
        public double ShuttleAccelPx { get; internal set; }
        /// <summary>The planned stop onto the goal is on (the landing point or the hover spot): speed and acceleration both reach 0 on it.</summary>
        public bool ShuttleBraking { get; internal set; }

        // ------------------------------------------------------------------ the strafe under fire (ZWorld.ShuttleEvade.cs)
        /// <summary>The sideways offset off its line (or its hover spot), px; + = to the right of its facing. 0 when no strafe is on.</summary>
        public double ShuttleStrafePx { get; internal set; }
        /// <summary>The offset's rate (px/s) and its acceleration (px/s^2), + = to the right: the view leans the ship into the strafe.</summary>
        public double ShuttleStrafeVelPx { get; internal set; }
        public double ShuttleStrafeAccelPx { get; internal set; }
        /// <summary>Under fire now: a projectile aimed at it (a rocket, shell, missile or grenade) was seen in the last
        /// ZWorld.ShuttleHeavyFireHoldSec s (one is enough).</summary>
        public bool ShuttleUnderHeavyFire { get; internal set; }
        /// <summary>Where the strafe is: 0 = on its line, 1 = gliding out to its side, 2 = holding (the fire goes on), 3 = drifting back
        /// onto its line, 4 = easing to a stop on the way back (fired at again: it holds there, it never turns back out).</summary>
        public int ShuttleStrafeStage => shuttleStrafeMode;
        /// <summary>The side of this threat's glide: +1 = right of the facing, -1 = left, 0 = on its line (kept until it is back).</summary>
        public int ShuttleStrafeSide => shuttleStrafeMode == 0 ? 0 : shuttleStrafeKeep;
        /// <summary>Counts: projectiles aimed at it seen in flight; glides out begun (the view's whoosh); returns onto the line.</summary>
        public int ShuttleThreatsSeen { get; internal set; }
        public int ShuttleStrafes { get; internal set; }
        public int ShuttleStrafeReturns { get; internal set; }
        /// <summary>The point on its line without the strafe (px, sub-pixel): its flight plan's own position. CenterX / CenterY = this + the
        /// strafe (the place every shot and blast uses).</summary>
        public double ShuttleBaseX => shuttleFx;
        public double ShuttleBaseY => shuttleFy;

        // ------------------------------------------------------------------ the rough landing (ZWorld.Shuttle.cs)
        /// <summary>This landing is a rough one (a crash landing in the rules' timing): from the start of its descent until the next
        /// take-off. About one landing in ZWorld.ShuttleRoughEvery, never on a repair station; no hit points are lost.</summary>
        public bool ShuttleRoughLanding { get; internal set; }
        /// <summary>The side whose two pads hit first: +1 = the ship's +x side (root space), -1 = the -x side.</summary>
        public int ShuttleRoughSide { get; internal set; } = 1;
        /// <summary>World.Time of the last touchdown (the lift reached 0), -1 = none yet.</summary>
        public double ShuttleTouchdownAt { get; internal set; } = -1.0;
        /// <summary>World.Time the levelling begins (a rough landing: touchdown + ZWorld.ShuttleRoughCrashSec + ZWorld.ShuttleRoughRestSec;
        /// a clean one: the touchdown).</summary>
        public double ShuttleLevelStartAt { get; internal set; } = -1.0;
        /// <summary>World.Time it stands level again (a rough landing: ShuttleLevelStartAt + ZWorld.ShuttleRoughLevelSec; a clean one:
        /// the touchdown). EXIT, the ramp and a take-off wait for it.</summary>
        public double ShuttleLevelledAt { get; internal set; } = -1.0;
        /// <summary>It stands level (true for everything but the shuttle; the shuttle in the air or after a clean landing too).</summary>
        public bool ShuttleLevelled => !IsShuttle || World == null || World.Time >= ShuttleLevelledAt;
        /// <summary>Rough landings so far.</summary>
        public int ShuttleRoughLandings { get; internal set; }
        /// <summary>The same as ShuttleLevelledAt: the ramp, EXIT, boarding and a take-off wait until then.</summary>
        public double ShuttleSettleUntil => ShuttleLevelledAt;

        // ------------------------------------------------------------------ the landing ON the repair station (ZWorld.ShuttleRepair.cs)
        /// <summary>Hovering in place over its repair station (state Cruise, speed 0, at cruise height), turning onto
        /// ZWorld.ShuttleDeckHeadingDeg and waiting for the crane; then it comes down onto the deck.</summary>
        public bool ShuttleDeckTurning { get; internal set; }
        /// <summary>The station whose crane it wants out of the way (its id; -1 = none): from the hover over the station until it is back
        /// at cruise height off that deck.</summary>
        public int ShuttleCraneStation { get; internal set; } = -1;
        /// <summary>World.Time it called that crane away (-1 = no call); the descent begins no earlier than this + ZWorld.ShuttleCraneMoveSec.</summary>
        public double ShuttleCraneCallAt { get; internal set; } = -1.0;
        /// <summary>World.Time the crane is out of the way (ShuttleCraneCallAt + ZWorld.ShuttleCraneMoveSec; -1 = no call).</summary>
        public double ShuttleCraneClearAt => ShuttleCraneCallAt >= 0 ? ShuttleCraneCallAt + ZWorld.ShuttleCraneMoveSec : -1.0;
        /// <summary>The deck point of a repair station (px): its repair centre + (ZWorld.ShuttleDeckOffsetXPx, ZWorld.ShuttleDeckOffsetYPx).
        /// false = not a repair station.</summary>
        public bool ShuttleDeckPoint(ZObject station, out int x, out int y)
        {
            x = y = 0;
            if (station == null || !station.GetRepairCenter(out int cx, out int cy)) return false;
            x = cx + ZWorld.ShuttleDeckOffsetXPx; y = cy + ZWorld.ShuttleDeckOffsetYPx;
            return true;
        }

        // ------------------------------------------------------------------ the rear ramp (ZWorld.ShuttleRamp.cs)
        /// <summary>Shut, Opening, Open, Closing.</summary>
        public ZShuttleRamp ShuttleRampPhase { get; internal set; }
        /// <summary>0 shut .. 1 fully down, linear in sim time (1 / ShuttleRampOpenSec up, 1 / ShuttleRampCloseSec down).</summary>
        public double ShuttleRamp01 { get; internal set; }
        /// <summary>Openings begun (a reversal from closing counts too): the view plays the opening sound when it changes.</summary>
        public int ShuttleRampOpens { get; internal set; }
        /// <summary>Completed shuts (the latch): the view plays the latch when it changes; a held take-off goes after it.</summary>
        public int ShuttleRampShuts { get; internal set; }
        /// <summary>World.Time until which the ramp stays down (figures walking on it).</summary>
        public double ShuttleRampHoldUntil { get; internal set; } = -1.0;
        /// <summary>EXIT ordered: the ramp is coming down and the passengers are still aboard.</summary>
        public bool ShuttleExitPending { get; internal set; }
        /// <summary>A move order waits for the ramp to shut (taken the moment the latch closes).</summary>
        public bool ShuttleTakeOffHeld { get; internal set; }
        /// <summary>Why the ramp was last down: None, Boarded, Unloaded.</summary>
        public ZShuttleRampUse ShuttleLastRampUse { get; internal set; }
        /// <summary>Per squad member in squad order (leader first), World.Time s. Boarding: when its figure reaches the ramp foot;
        /// exit: when it steps off the ramp foot.</summary>
        public readonly List<double> ShuttleFootTimes = new List<double>();
        /// <summary>The robot ids in the same order (boarding: the robots that boarded; exit: the robots made at the exit).</summary>
        public readonly List<int> ShuttleFootIds = new List<int>();
        /// <summary>EXIT is allowed (true for everything but the shuttle): the squad walking in has sat down and the ship stands level
        /// (after a rough landing it rights itself first: ShuttleLevelledAt).</summary>
        public bool ShuttleCrewSettled => !IsShuttle || World == null || (World.Time >= ShuttleRampHoldUntil && (World.Time >= ShuttleLevelledAt || ShuttleExitWhileRighting));   // K426: on the ground EXIT is taken while it rights itself, carried out once level (Sim/ZWorld.ShuttleExitSooner.cs)

        /// <summary>The ground spot (px) of back slot `slot`: slot 0 = the ramp foot, ShuttleBackM behind the ship's middle along its
        /// facing; slot k is k x ShuttleFileM further back. The slots stay ON the back axis: a spot a robot cannot stand on (a closed or
        /// water tile, a parked vehicle or gun) is skipped and slot k is the k-th free spot on the axis (the later slots move on further
        /// back); none free within ZWorld.ShuttleSlotsBeyond more: the slot's own distance 25 degrees to either side; then the plain spot
        /// (K256e, Sim/ZWorld.ShuttleGround.cs). Deterministic; robots never block it.</summary>
        public (int x, int y) ShuttleBackPoint(int slot) => ShuttleBackSpot(slot, false, null, out _);

        /// <summary>The back slot with the free-spot rule; withRobots = standing robots block it too (the exit's placing); skip = robots
        /// that do not count; fallback = the plain spot was taken.</summary>
        internal (int x, int y) ShuttleBackSpot(int slot, bool withRobots, ICollection<ZObject> skip, out bool fallback) => ShuttleAxisSpot(slot, withRobots, skip, out fallback);   // K256e: on the back axis (Sim/ZWorld.ShuttleGround.cs)

        static readonly int[] ShuttleSlotTurns = { 0, 25, -25 };                                                  // K256e: 25 degrees to a side at most (review M6b)

        /// <summary>A robot can stand with its middle at (x, y): every tile under its body is on the map, open for robots and not deep
        /// water; no parked vehicle or gun on the ground is within the spacing; withRobots: no other standing robot either.</summary>
        bool ShuttleSpotFree(int x, int y, bool withRobots, ICollection<ZObject> skip)
        {
            var w = World; var m = w.Map; int rr = ZPathing.RobotRadius;
            if (x < 16 + rr || y < 16 + rr || x >= m.WidthPix - 16 - rr || y >= m.HeightPix - 16 - rr) return false;
            for (int ty = (y - rr) >> 4; ty <= (y + rr) >> 4; ty++)
                for (int tx = (x - rr) >> 4; tx <= (x + rr) >> 4; tx++)
                {
                    var c = m.ClassAt(tx, ty);
                    if (c == ZClass.DeepWater || c == ZClass.Impassable || !w.Path.TilePassable(tx, ty, true)) return false;
                }
            foreach (var o in w.Objects)
            {
                if (o == this || !o.IsUnit || o.Destroyed || o.KillAt >= 0 || o.ShuttleAirborne) continue;
                if (o.IsRobot && (!withRobots || (skip != null && skip.Contains(o)))) continue;
                double lim = o.IsRobot ? 2 * rr : ZPathing.Radius(o) + rr + 2, dx = o.CenterX - x, dy = o.CenterY - y;   // robots: their own spacing (the file step is wider)
                if (dx * dx + dy * dy < lim * lim) return false;
            }
            return true;
        }
    }

    public sealed partial class ZWorld
    {
        // ------------------------------------------------------------------ flight
        /// <summary>Turn: at most ShuttleTurnMaxDeg deg/s, the rate changes at most ShuttleTurnAccelDeg deg/s^2. Speed: the acceleration
        /// is at most vmax / ShuttleAccelSec (either way) and builds up or fades out over ShuttleJerkSec (an S-curve: 0 to full speed takes
        /// ShuttleAccelSec + ShuttleJerkSec s); the planned stop from full speed takes ShuttleBrakeFullSec s.</summary>
        public const double ShuttleTurnMaxDeg = 70.0, ShuttleTurnAccelDeg = 140.0, ShuttleAccelSec = 1.6, ShuttleJerkSec = 0.6;
        /// <summary>The planned stop's length from full speed (s): its peak deceleration is then vmax / ShuttleAccelSec and its jerk
        /// vmax / (ShuttleAccelSec x ShuttleJerkSec); from a lower speed v it takes ShuttleBrakeFullSec x sqrt(v / vmax).</summary>
        public static readonly double ShuttleBrakeFullSec = Math.Max(1.5 * ShuttleAccelSec, Math.Sqrt(6.0 * ShuttleAccelSec * ShuttleJerkSec));
        /// <summary>The peak acceleration (px/s^2) and the jerk (px/s^3) for a top speed vmax (px/s).</summary>
        public static double ShuttleAccelMax(double vmax) => vmax / ShuttleAccelSec;
        public static double ShuttleJerkMax(double vmax) => vmax / (ShuttleAccelSec * ShuttleJerkSec);
        /// <summary>The planned stop's length (s) from speed v (px/s), no acceleration, for a top speed vmax.</summary>
        public static double ShuttleBrakeSec(double v, double vmax) => ShuttleBrakeFullSec * Math.Sqrt(Math.Max(0.0, Math.Min(1.0, v / Math.Max(1e-9, vmax))));
        /// <summary>The planned stop's length (s) from speed v and acceleration a (px/s^2): the shortest one, at least ShuttleBrakeSec(v,
        /// vmax), whose deceleration stays within vmax / ShuttleAccelSec and whose jerk stays within ShuttleJerkMax(vmax).</summary>
        public static double ShuttleBrakeSec(double v, double a, double vmax)
        {
            double t0 = Math.Max(1e-3, ShuttleBrakeSec(v, vmax));
            if (v <= 0) return t0;
            double am = ShuttleAccelMax(vmax) * (1 + 1e-9), jm = ShuttleJerkMax(vmax) * (1 + 1e-9);
            bool Fits(double t) => ShuttleQuarticDecel(v, a, t) <= am && ShuttleQuarticJerk(v, a, t) <= jm;
            if (Fits(t0)) return t0;
            double lo = t0, hi = t0 * 2;
            for (int i = 0; i < 8 && !Fits(hi); i++) { lo = hi; hi *= 2; }
            for (int i = 0; i < 40; i++) { double mid = 0.5 * (lo + hi); if (Fits(mid)) hi = mid; else lo = mid; }
            return hi;
        }
        /// <summary>The quartic stop over t s from speed v and acceleration a: its strongest deceleration (px/s^2, >= 0) and its largest
        /// jerk (px/s^3). Acceleration a(u) = (v (6u^2 - 6u) + a t (3u^2 - 4u + 1)) / t, u = time / t.</summary>
        public static double ShuttleQuarticDecel(double v, double a, double t)
        {
            double c2 = (6 * v + 3 * a * t) / t, c1 = (-6 * v - 4 * a * t) / t, c0 = a, lo = Math.Min(c0, c2 + c1 + c0);
            if (c2 > 1e-12) { double u = -c1 / (2 * c2); if (u > 0 && u < 1) lo = Math.Min(lo, c2 * u * u + c1 * u + c0); }
            return Math.Max(0.0, -lo);
        }
        public static double ShuttleQuarticJerk(double v, double a, double t)
        {
            double c2 = (6 * v + 3 * a * t) / t, c1 = (-6 * v - 4 * a * t) / t;
            return Math.Max(Math.Abs(c1), Math.Abs(2 * c2 + c1)) / t;
        }
        /// <summary>The planned stop's distance (px) from speed v (px/s) and acceleration a (px/s^2): T x (v / 2 + a x T / 12), T =
        /// ShuttleBrakeSec(v, a, vmax) (the quartic stop: speed and acceleration both reach 0 at its end).</summary>
        public static double ShuttleBrakeDistance(double v, double a, double vmax)
        {
            if (v <= 0) return 0;
            double t = ShuttleBrakeSec(v, a, vmax);
            return Math.Max(0.0, t * (v * 0.5 + a * t / 12.0));
        }
        /// <summary>A straight flight along its facing from rest to rest, d px at top speed vmax (px/s), long enough to reach it: the
        /// cruise's seconds (the climb and the descent not counted) = d / vmax + (ShuttleAccelSec + ShuttleJerkSec) / 2 + ShuttleBrakeFullSec / 2.</summary>
        public static double ShuttleStraightSec(double d, double vmax) => d / Math.Max(1e-9, vmax) + 0.5 * (ShuttleAccelSec + ShuttleJerkSec) + 0.5 * ShuttleBrakeFullSec;
        /// <summary>Within ShuttleSettlePx of the goal it may slide straight onto it; at ShuttleArrivePx it has arrived.</summary>
        public const double ShuttleSettlePx = 16.0, ShuttleArrivePx = 2.0;
        /// <summary>The parked ship's facing at map load (K256e: -75 = the view's yaw 15, nose north-north-east: the rear ramp, its lane and the
        /// squad's slots face the level's start camera, clear of the fort, the heroes' camp, the hedge and every closed tile, and not under the
        /// unit panel; counted on the level's own data: every heading from -109 to -50 is free).</summary>
        public const float L16ShuttleHeadingDeg = -75f;
        // ------------------------------------------------------------------ the landing ON the repair station (ZWorld.ShuttleRepair.cs)
        /// <summary>The deck point: the station's repair centre (the vehicles' pad point, GetRepairCenter) + this (px; +x east, +y south),
        /// and the heading it lands on (degrees; the view's yaw 318: nose to the north-west). The pose that fits the ship on the deck once
        /// the crane gantry has slid away (measured on the station's mesh and the ship's parts: nothing of the station inside the hull, all
        /// four pads on the deck itself). The free room round it is small, so it lands exactly here, on this heading.</summary>
        public const int ShuttleDeckOffsetXPx = 12, ShuttleDeckOffsetYPx = -3;
        public const float ShuttleDeckHeadingDeg = -132f;
        /// <summary>The hover turn over the station before the descent: at most ShuttleDeckTurnMaxDeg deg/s, the rate changing at most
        /// ShuttleDeckTurnAccelDeg deg/s^2 and easing out over the last ShuttleDeckTurnEaseDeg degrees; the descent begins with at most
        /// ShuttleDeckSeatLeftDeg degrees left and the turn slower than ShuttleDeckSeatRateDeg deg/s (it turns the rest on the way down).</summary>
        public const double ShuttleDeckTurnMaxDeg = 30.0, ShuttleDeckTurnAccelDeg = 30.0, ShuttleDeckTurnEaseDeg = 25.0, ShuttleDeckSeatLeftDeg = 3.0, ShuttleDeckSeatRateDeg = 3.0;   // K256e: 45 -> 30 deg/s (and deg/s^2); also the landing turn to a free side
        /// <summary>The station's crane moves out of the way in this time (s) once a shuttle calls it (the view's crane move takes exactly
        /// this long); the descent waits for it.</summary>
        public const double ShuttleCraneMoveSec = 2.0;

        // ------------------------------------------------------------------ the ramp and the squad
        /// <summary>The ramp foot behind the middle (m), the file step of the slots behind it (m), the distance from the foot at which an
        /// own robot ordered in calls the ramp down (m).</summary>
        public const double ShuttleBackM = 4.6, ShuttleFileM = 1.75, ShuttleRampCallM = 7.0;
        /// <summary></summary>
        public const double ShuttleRampOpenSec = 1.1, ShuttleRampCloseSec = 1.1;
        /// <summary>The figures' walk (m/s), the longest walk counted (m), the gap between two figures on the ramp (s). K422: 3.2 -> 3.5 m/s
        /// (the view's figures run to the foot at this pace), 0.55 -> 0.5 s (1.17 m apart on the ramp at its 2.35 m/s walk).</summary>
        public const double ShuttleWalkMps = 3.5, ShuttleWalkCapM = 12.0, ShuttleFileGapSec = 0.5;
        /// <summary>The ramp foot to the seat / the seat to off the ramp foot (s): the view's figure budgets. K422: 3.2 -> 2.65 / 3.2 -> 2.5 s
        /// (the ramp at 2.35 m/s, the aisle at 2.0 m/s, sitting 0.35 s: BoardingView.ShuttleQuick.cs).</summary>
        public const double ShuttleRampInSec = 2.65, ShuttleRampOutSec = 2.5;
        /// <summary>The leader boards within ShuttleBoardPx of the ramp foot; the pathing radius of the ship (px; K256e 28 -> 35: the landed
        /// ship is a hard obstacle, a robot's middle stays 41 px = 5.1 m off its middle, its body clear of the hull, pods and legs + 0.5 m).</summary>
        public const int ShuttleBoardPx = 8, ShuttleRadiusPx = 35;
        /// <summary>The Landed note's why on a repair landing (the view plays no landing voices then).</summary>
        public const string ShuttleWhyRepairPad = "repair pad";

        /// <summary>Check counters (reset by the checks): leaders that waited at the foot for the ramp, boardings at the foot, robots set
        /// on their back slots at an exit, of them on a free-spot fallback.</summary>
        public static int ShuttleRampWaits, ShuttleBoardsAtFoot, ShuttleExitPlaced, ShuttleExitFallbacks;

        // ------------------------------------------------------------------ the strafe under fire (ZWorld.ShuttleEvade.cs)
        /// <summary>Under fire: from each projectile (rocket, shell, missile, the fort gun's shell, grenade) aimed at it, for
        /// ShuttleHeavyFireHoldSec s.</summary>
        public const double ShuttleHeavyFireHoldSec = 2.5;
        /// <summary>The strafe: ONE glide of ShuttleStrafeM to the side away from the shooters, eased (a quintic) over ShuttleStrafeSec; it
        /// holds there while the fire goes on, then drifts back onto its line over ShuttleStrafeBackSec (eased); fired at again on the way
        /// back it eases to a stop over ShuttleStrafeStopSec and holds. Two changes of the sideways direction are at least
        /// ShuttleStrafeMinFlipSec apart (a sideways speed under ShuttleStrafeStillPx px/s counts as still); a glide is never reversed. Only up
        /// in the air (lift at least ShuttleStrafeMinLift), never while it comes down to land or turns over a repair station.</summary>
        public const double ShuttleStrafeM = 5.0, ShuttleStrafeSec = 1.8, ShuttleStrafeBackSec = 4.0, ShuttleStrafeStopSec = 1.2, ShuttleStrafeMinFlipSec = 3.0, ShuttleStrafeMinLift = 0.5, ShuttleStrafeStillPx = 0.05;
        /// <summary>Checks only: false = no strafe (the hit count without it). The game: always true.</summary>
        public static bool ShuttleStrafeOn = true;

        // ------------------------------------------------------------------ the rough landing (ZWorld.Shuttle.cs)
        /// <summary>About one landing in ShuttleRoughEvery is rough (by the landing's number, no random draw; never on a repair station).</summary>
        public const int ShuttleRoughEvery = 4;
        /// <summary>A rough landing's timing after the touchdown: the crash (the first side slams, it rocks, the second side bangs down,
        /// one hop) ShuttleRoughCrashSec, resting tilted ShuttleRoughRestSec, the hydraulic levelling ShuttleRoughLevelSec (s).</summary>
        public static double ShuttleRoughCrashSec => ShuttleExitSoonerOn ? ShuttleSoonerCrashSec : ShuttleOldCrashSec; public static double ShuttleRoughRestSec => ShuttleExitSoonerOn ? ShuttleSoonerRestSec : ShuttleOldRestSec; public static double ShuttleRoughLevelSec => ShuttleExitSoonerOn ? ShuttleSoonerLevelSec : ShuttleOldLevelSec;   // K426: 0.8 + 0.2 + 0.7 s (was 1.4 + 1.0 + 1.2; Sim/ZWorld.ShuttleExitSooner.cs)
        /// <summary>A rough landing's whole time from the touchdown until it stands level (s).</summary>
        public static double ShuttleRoughSettleSec => ShuttleRoughCrashSec + ShuttleRoughRestSec + ShuttleRoughLevelSec;   // K426: 1.7 s (was 3.6)
        /// <summary>Proof only: the next landing (not on a repair station) is rough; cleared when it is used.</summary>
        public static bool ShuttleRoughProofNext;
        /// <summary>The same switch as ShuttleRoughProofNext.</summary>
        public static bool ShuttleProofRoughNext { get => ShuttleRoughProofNext; set => ShuttleRoughProofNext = value; }

        /// <summary>A shuttle wants the crane of this repair station out of the way now (the station's view moves it away, in
        /// ShuttleCraneMoveSec, and back when this turns false).</summary>
        public bool ShuttleCraneAway(int stationId)
        {
            if (shuttlesMade == 0 || stationId < 0) return false;
            foreach (var o in Objects) if (o.IsShuttle && !o.Destroyed && o.ShuttleCraneStation == stationId) return true;
            var wst = Get(stationId); if (wst != null && wst.ShuttleWreckOnDeck) return true;              // K256e: a shot down ship's wreck on the deck (ZWorld.ShuttleGround.cs)
            return false;
        }

        // ------------------------------------------------------------------ the squad lost with the ship
        /// <summary>One line per robot lost aboard (in squad order, in the tick of the death); at most 32 kept, seq counts up.</summary>
        public readonly List<ZShuttleLost> ShuttleLostCrew = new List<ZShuttleLost>();
        public int ShuttleLostSeq { get; internal set; }
    }
}
