using System;
using System.Collections.Generic;

namespace OpenZ.Sim
{
    /// <summary>
    /// DV118, several shuttles in the air at once (own and enemy ships alike): they keep apart, give way to each other smoothly and never
    /// share a landing spot. Deterministic: positions, sim time, the fixed object order and the ships' ids; no random draw, no frame time.
    /// With one shuttle in the match (or <see cref="ZWorld.ShuttleTrafficOn"/> off) nothing here runs: every ship flies exactly as before.
    ///  * THE SEPARATION: two ships in the air keep their middles at least <see cref="ZWorld.ShuttleAirSepM"/> apart (the outline's farthest
    ///    point is 3.889 m from the middle: twice that + 1.2 m, so the hulls never touch whatever their headings); the steering aims at
    ///    <see cref="ZWorld.ShuttleAirSoftM"/>.
    ///  * WHO GIVES WAY: a ship standing with its take-off held for room first, then the ships settling (coming down, turning or hovering
    ///    over their spot, on their goal), then the ships on their way (climbing out of a take-off too); among equals the lower id goes
    ///    first; and everyone gives way to a ship low over the ground (climbing out, or low in its descent: it does not move aside there). A
    ///    ship gives way only to the ships before it (never to one that gives way to it: nobody waits for a ship that waits for it). A ship on
    ///    its way that nothing of its own clears asks the one still in the way (if that one is on its way too, never one coming down to land)
    ///    to make room for a second.
    ///  * GIVING WAY: every ship looks <see cref="ZWorld.ShuttleAvoidLookSec"/> ahead along both flight plans (its point on the line going on
    ///    to its goal at its speed, speeding up and braking as the flight model does; a ship climbing, coming down or turning over its spot
    ///    stands; the strafe's glide at its end; each ship's avoid offset getting to where it is going; beyond that the lines on to the
    ///    goals), for the ships within <see cref="ZWorld.ShuttleAvoidRangeM"/>. Where its plan comes closer than ShuttleAirSoftM to the plan of
    ///    a ship before it, it picks the nearest change that clears every one of them: a little aside, a little slower, further aside, aside
    ///    from its line, much slower or waiting in the air, aside and slower (the side that gives room first); within
    ///    <see cref="ZWorld.ShuttleAvoidMinHoldSec"/> of its last turn never back the way it came while that still keeps
    ///    <see cref="ZWorld.ShuttleAvoidKeepM"/> more than the separation. That is its avoid offset (the flight plan goes on unchanged, as with
    ///    the strafe, so it never leaves its order) and its speed share. The offset moves smoothly (what it wants eased over
    ///    <see cref="ZWorld.ShuttleAvoidTauOut"/> at most <see cref="ZWorld.ShuttleAvoidRateOutMps"/>, followed critically damped at
    ///    <see cref="ZWorld.ShuttleAvoidOmegaOut"/>, its acceleration at most <see cref="ZWorld.ShuttleAvoidAccMax"/>; a new choice heads
    ///    straight there from where the offset is). It holds while it is needed and drifts back gently
    ///    (<see cref="ZWorld.ShuttleAvoidRateBackMps"/>) once its whole way back is clear, it has come to rest where it went, the least hold
    ///    has passed since it last chose and turned, and the ships it gives way to have settled their own ways (none low, none that chose
    ///    within <see cref="ZWorld.ShuttleAvoidSettleSec"/>): never a back-and-forth. A ship waiting in the air or crawling looks for a way
    ///    round to go on (<see cref="ZWorld.ShuttleResumeTicks"/>). Back on its line first: the descent, the landing turn and the deck turn
    ///    wait for it (as for the strafe: ZObject.ShuttleStrafeBusy), so it always comes down on the spot that was tested. The strafe's glide
    ///    under fire never goes into another ship beside it.
    ///  * LANDING SPOTS: every ship holds one spot (standing: where it stands; flying, or ordered off: its goal). A new goal within
    ///    <see cref="ZWorld.ShuttleClaimM"/> of another ship's spot moves to the nearest free landing spot round the ordered point (rings out
    ///    to <see cref="ZWorld.ShuttleClaimReachM"/>, the side toward the ship first; of several ships ordered in one tick the one nearest to
    ///    its goal first): a swarm sent to one point lands round it and two ships never share a spot. A spot that a ship is leaving is not
    ///    held against a newcomer; arriving before that ship has gone, the newcomer holds over it (no "can't land") until it has climbed out
    ///    and flown off. No free spot near: the ordered point (it may hover there, as over any blocked pad). A repair trip keeps the
    ///    station's own bay rule.
    ///  * THE TAKE-OFF waits while another ship is in the air within <see cref="ZWorld.ShuttleLiftAirM"/>; that ship gives way to it, so ships
    ///    parked close lift off one after the other.
    /// Outputs for the view: ShuttleAvoidXPx / YPx, ShuttleAvoidLatVelPx / LatAccelPx (the lean), ShuttleHolding; counts for the checks.
    /// </summary>
    public partial class ZObject
    {
        double shuttleAvX, shuttleAvY, shuttleAvVx, shuttleAvVy, shuttleAvAx, shuttleAvAy;   // the avoid offset (px), its rate (px/s), its acceleration (px/s^2)
        double shuttleAvTx, shuttleAvTy, shuttleAvEx, shuttleAvEy;                           // where it is going (px) and that eased
        double shuttleAvChoseAt = -1e9;                                                        // World.Time it last chose where to go
        double shuttleAvSpeedK = 1.0;                                                          // its cruise held to this share of its top speed (giving way in time)
        double shuttleAvDirX, shuttleAvDirY, shuttleAvTurnAt = -1e9;                           // the way it last moved off its line, World.Time that motion began or turned back
        int shuttleAskId = -1, shuttleWorstId = -1; double shuttleAskUntil = -1e9;              // the ship asked to make room too (no offset of its own clears it), until
        int shuttleClaimX = int.MinValue, shuttleClaimY = int.MinValue;                        // the goal its spot was decided for

        /// <summary>DV118 traffic: the avoid offset off its line now (px; world axes, part of CenterX / CenterY like the strafe's).</summary>
        public double ShuttleAvoidXPx => shuttleAvX;
        public double ShuttleAvoidYPx => shuttleAvY;
        /// <summary>Where the avoid offset is going (px).</summary>
        public double ShuttleAvoidToXPx => shuttleAvTx;
        public double ShuttleAvoidToYPx => shuttleAvTy;
        /// <summary>Giving way in time: its cruise speed is held to this share of its top speed (1 = not held; the flight model's eased
        /// speed follows it, ZWorld.ShuttleFlight.cs).</summary>
        public double ShuttleTrafficSpeedK => shuttleAvSpeedK;
        /// <summary>The avoid offset's rate and acceleration to the right of the facing (px/s, px/s^2; + = right): the view leans with it.</summary>
        public double ShuttleAvoidLatVelPx { get; private set; }
        public double ShuttleAvoidLatAccelPx { get; private set; }
        /// <summary>The avoid offset's rate (px/s, world axes: sim x and y).</summary>
        public double ShuttleAvoidVelXPx => shuttleAvVx;
        public double ShuttleAvoidVelYPx => shuttleAvVy;
        /// <summary>The avoid offset's rate and acceleration (px/s, px/s^2, their size).</summary>
        public double ShuttleAvoidSpeedPx => Math.Sqrt(shuttleAvVx * shuttleAvVx + shuttleAvVy * shuttleAvVy);
        public double ShuttleAvoidAccelPx => Math.Sqrt(shuttleAvAx * shuttleAvAx + shuttleAvAy * shuttleAvAy);
        /// <summary>Off its line, or going off it (giving way to another ship).</summary>
        public bool ShuttleAvoiding => ShuttleAvoidBusy;
        /// <summary>Times it chose a new offset (to give way, or back onto its line).</summary>
        public int ShuttleAvoidEncounters { get; private set; }
        /// <summary>The last order's point (px; -1 = none) and whether its goal was moved to a free spot round it (another ship's spot there).</summary>
        public int ShuttleOrderX { get; private set; } = -1;
        public int ShuttleOrderY { get; private set; } = -1;
        public bool ShuttleSpotMoved { get; private set; }
        /// <summary>Over its goal, holding until the ship that is leaving that spot has gone.</summary>
        public bool ShuttleHolding { get; private set; }
        /// <summary>Standing, its take-off held for room in the air (the ships up there give way to it).</summary>
        public bool ShuttleWantsAir { get; private set; }
        bool shuttleAirWaitOn;
        /// <summary>Proof logs: its class in the traffic, where its offset is and goes (m).</summary>
        public string ShuttleTrafficState =>
            "c" + ShuttleTrafficClass + " k" + shuttleAvSpeedK.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " off " + (shuttleAvX / 8).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "," + (shuttleAvY / 8).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)
            + " to " + (shuttleAvTx / 8).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "," + (shuttleAvTy / 8).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>Off its line (the avoid offset, its rate or where it goes not yet back at 0).</summary>
        internal bool ShuttleAvoidBusy => shuttleAvX != 0 || shuttleAvY != 0 || shuttleAvVx != 0 || shuttleAvVy != 0 || shuttleAvTx != 0 || shuttleAvTy != 0;
        /// <summary>The flight model's cruise speed cap from the traffic (px/s; ZWorld.ShuttleFlight.cs): its top speed when it does not give
        /// way in time (and always with one shuttle in the match).</summary>
        internal double ShuttleTrafficVmax(double vmax) => vmax * shuttleAvSpeedK;

        bool ShuttleTrafficPinned => ShuttleState == ZShuttleState.Climb || ShuttleState == ZShuttleState.Descend || ShuttleLandTurning || ShuttleDeckTurning;
        /// <summary>Counts in the air traffic: up, or standing with its take-off held for room.</summary>
        bool ShuttleTrafficPresent => !Destroyed && KillAt < 0 && (ShuttleAirborne || ShuttleWantsAir);
        /// <summary>Who gives way to whom: -1 standing (its take-off held for room), 0 settling (coming down, turning over its spot, hovering,
        /// holding, or on its goal), 1 on its way (climbing out of a take-off too).</summary>
        int ShuttleTrafficClass
        {
            get
            {
                if (!ShuttleAirborne) return -1;
                if (ShuttleState == ZShuttleState.Climb) return ShuttleGoalX >= 0 ? 1 : 0;                   // climbing out: on its way
                if (ShuttleTrafficPinned || ShuttleState == ZShuttleState.Hover || ShuttleHolding || ShuttleGoalX < 0) return 0;
                double dx = ShuttleGoalX - shuttleFx, dy = ShuttleGoalY - shuttleFy;
                return ShuttleViaCount == 0 && dx * dx + dy * dy < 16.0 * 16.0 ? 0 : 1;               // DV125: over a middle point it is on its way
            }
        }
        /// <summary>This ship gives way to ship o: o's class is lower, or the same and o's id lower; and to any ship climbing out of a take-off
        /// or low in its descent (it does not move aside then), unless this one is too.</summary>
        bool ShuttleYieldsTo(ZObject o)
        {
            bool lowA = ShuttleTrafficLow, lowB = o.ShuttleTrafficLow;
            if (lowA != lowB) return lowB;
            int a = ShuttleTrafficClass, b = o.ShuttleTrafficClass;
            return a > b || (a == b && Id > o.Id);
        }

        /// <summary>Climbing out of a take-off, or low in its descent (under ZWorld.ShuttleStrafeMinLift): it does not move aside.</summary>
        bool ShuttleTrafficLow => ShuttleAirborne && (ShuttleState == ZShuttleState.Climb || (ShuttleState == ZShuttleState.Descend && ShuttleLift < ZWorld.ShuttleStrafeMinLift));

        /// <summary>ZObject.ShuttleStrafeRoom: how far (px) a glide the strafe picks now may go to `side` (+1 right, -1 left) before it comes
        /// within ZWorld.ShuttleAirSoftM of another ship in the air beside it (ahead or behind within that and its glide's way); a glide under
        /// way is never cut short; no ship there: no limit (with one shuttle in the match always none).</summary>
        internal double ShuttleTrafficStrafeRoom(int side)
        {
            if (!ShuttleTrafficLive || side == 0 || shuttleStrafeMode != 0) return 1e9;                        // only for a glide it picks now (never cut a glide short)
            var (rx, ry) = ShuttleRight(); double h = ShuttleHeadingDeg * Math.PI / 180.0, fx = Math.Cos(h), fy = Math.Sin(h);
            double soft = ZWorld.ShuttleAirSoftM * 8.0, room = 1e9, reach = soft + ZWorld.ShuttleStrafeM * 8.0;
            foreach (var o in World.TrafficShips)
            {
                if (o == this || o.Destroyed || o.KillAt >= 0 || !o.ShuttleAirborne) continue;
                double dx = o.shuttleFx + o.shuttleOffX - (shuttleFx + shuttleOffX), dy = o.shuttleFy + o.shuttleOffY - (shuttleFy + shuttleOffY);
                double lat = (dx * rx + dy * ry) * side, fwd = dx * fx + dy * fy;
                if (lat <= 0 || lat > reach + soft || Math.Abs(fwd) > reach) continue;
                double need = Math.Sqrt(Math.Max(0.0, soft * soft - fwd * fwd));                                // beside it: up to where it would come within soft
                room = Math.Min(room, Math.Max(0.0, lat - need));
            }
            return room;
        }
        /// <summary>Its flight plan moves it on to its goal (else its point on the line stands).</summary>
        bool ShuttleTrafficCruising => ShuttleState == ZShuttleState.Cruise && !ShuttleLandTurning && !ShuttleDeckTurning && !ShuttleHolding && ShuttleGoalX >= 0;
        /// <summary>Standing with an order to fly somewhere else (its spot is about to be free).</summary>
        bool ShuttleLeaving
        {
            get
            {
                if (!shuttleNewGoal || ShuttleGoalX < 0) return false;
                double dx = ShuttleGoalX - shuttleFx, dy = ShuttleGoalY - shuttleFy;
                return dx * dx + dy * dy > (double)ZWorld.ShuttleSameSpotPx * ZWorld.ShuttleSameSpotPx;
            }
        }
        bool ShuttleTrafficLive => World != null && ZWorld.ShuttleTrafficOn && World.ShuttlesMadeCount >= 2;

        /// <summary>ZObject.ShuttleStep, first (after the ramp): the world's spot round for this tick, the flags of the last tick cleared.</summary>
        internal void ShuttleTrafficBegin()
        {
            if (!ShuttleTrafficLive) return;
            World.ShuttleTrafficTick();
            ShuttleWantsAir = false; ShuttleHolding = false;
        }

        /// <summary>ZObject.ShuttleStep, the take-off (ramp shut, level, no unit in the way): true = it waits this tick, another ship is up
        /// within ZWorld.ShuttleLiftAirM (that ship gives way to it).</summary>
        internal bool ShuttleAirRoomWait()
        {
            ShuttleWantsAir = false;
            if (!ShuttleTrafficLive) return false;
            double lim = ZWorld.ShuttleLiftAirM * 8.0;
            foreach (var o in World.TrafficShips)
            {
                if (o == this || o.Destroyed || o.KillAt >= 0 || !o.ShuttleAirborne) continue;
                double dx = o.shuttleFx + o.shuttleOffX - shuttleFx, dy = o.shuttleFy + o.shuttleOffY - shuttleFy;
                if (dx * dx + dy * dy >= lim * lim) continue;
                ShuttleWantsAir = true;
                if (!shuttleAirWaitOn) { shuttleAirWaitOn = true; ZWorld.ShuttleAirWaits++; }
                return true;
            }
            shuttleAirWaitOn = false;
            return false;
        }

        /// <summary>ZObject.ShuttleStep, on its goal in the cruise: true = it holds there this tick (no landing, no "can't land"), a ship is
        /// leaving that spot (standing with its order to go, climbing out, or coming down or turning there).</summary>
        internal bool ShuttleHoldForShip()
        {
            if (!ShuttleTrafficLive || ShuttleRepairTarget >= 0) return false;
            double lim = ZWorld.ShuttleClaimM * 8.0;
            foreach (var o in World.TrafficShips)
            {
                if (o == this || o.Destroyed || o.KillAt >= 0) continue;
                bool standing = o.ShuttleState == ZShuttleState.Landed && o.ShuttleLift <= 0;
                if (!(standing ? o.ShuttleLeaving : o.ShuttleTrafficPinned)) continue;
                double dx = o.shuttleFx - ShuttleGoalX, dy = o.shuttleFy - ShuttleGoalY;
                if (dx * dx + dy * dy >= lim * lim) continue;
                if (!ShuttleHolding) ZWorld.ShuttleHolds++;
                ShuttleHolding = true;
                return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ the flight plans
        double[] shuttlePlanXY, shuttlePlanAlt; long shuttlePlanTick = -1, shuttlePlanAltTick = -1; double shuttlePlanAltK = -1;

        /// <summary>Its point on the line over the look ahead (px, every ZWorld.ShuttleAvoidStepSec; made once a tick, and once for another
        /// speed share it tries): flying, straight on to its goal at its speed, speeding up (or slowing to `share` of its top speed) as the
        /// flight model does (at most vmax / ShuttleAccelSec) and braking on the flight model's own planned stop (ShuttleCurveSpeed), stopping
        /// on the goal; climbing, the same once the climb is done; else it stands.</summary>
        double[] ShuttleBasePlan(double share)
        {
            bool main = share == shuttleAvSpeedK;
            if (main && shuttlePlanXY != null && shuttlePlanTick == World.TickIndex) return shuttlePlanXY;
            if (!main && shuttlePlanAlt != null && shuttlePlanAltTick == World.TickIndex && shuttlePlanAltK == share) return shuttlePlanAlt;
            int n = (int)Math.Round(ZWorld.ShuttleAvoidLookSec / ZWorld.ShuttleAvoidStepSec);
            var P = main ? shuttlePlanXY : shuttlePlanAlt;
            if (P == null || P.Length != 2 * (n + 1) + 1) P = new double[2 * (n + 1) + 1];
            if (main) { shuttlePlanXY = P; shuttlePlanTick = World.TickIndex; } else { shuttlePlanAlt = P; shuttlePlanAltTick = World.TickIndex; shuttlePlanAltK = share; }
            double bx = shuttleFx, by = shuttleFy, ux = 0, uy = 0, d = 0, v = 0, s = 0, vmax = 1, a = 1, wait = 0;
            bool fly = ShuttleTrafficCruising;
            if (ShuttleState == ZShuttleState.Climb && ShuttleGoalX >= 0) { fly = true; wait = Math.Max(0.0, 1.0 - ShuttleLift) * ZWorld.ShuttleClimbSec; }   // it flies once the climb is done
            if (fly)
            {
                double gx = ShuttleSteerX - bx, gy = ShuttleSteerY - by, d0 = Math.Sqrt(gx * gx + gy * gy); d = ShuttleViaCount > 0 ? ShuttleRouteRestPx(bx, by) : d0;   // DV125: the route still to fly
                if (d > 1e-6) { ux = gx / Math.Max(1e-9, d0); uy = gy / Math.Max(1e-9, d0); v = Math.Max(0.0, ShuttleSpeedPx); vmax = Math.Max(1.0, RealMoveSpeed); a = ZWorld.ShuttleAccelMax(vmax); } else fly = false;
            }
            const int sub = 5; double h = ZWorld.ShuttleAvoidStepSec / sub;
            for (int k = 0; k <= n; k++)
            {
                if (k > 0 && fly)
                    for (int j = 0; j < sub && s < d; j++)
                    {
                        if (wait > 0) { wait -= h; continue; }
                        double vk = vmax * share;
                        v = v > vk ? Math.Max(vk, v - a * h) : Math.Min(vk, v + a * h);
                        v = Math.Min(v, Math.Max(0.5, ShuttleCurveSpeed(d - s, vmax))); s = Math.Min(d, s + v * h);
                    }
                if (fly && ShuttleViaCount > 0) ShuttleRouteAt(bx, by, s, out P[2 * k], out P[2 * k + 1]);    // DV125: along its route's points
                else { P[2 * k] = bx + ux * s; P[2 * k + 1] = by + uy * s; }
            }
            P[2 * (n + 1)] = fly ? Math.Max(0.0, d - s) : 0.0;                                                 // still to go at the end (px)
            return P;
        }

        /// <summary>How long (s) the avoid offset takes from where it is to (x, y) px: its eased speed plus the damped follow.</summary>
        double ShuttleAvoidReach(double x, double y)
        {
            double dx = x - shuttleAvX, dy = y - shuttleAvY, d = Math.Sqrt(dx * dx + dy * dy);
            return ShuttleAvoidGentle(x, y) ? d / (ZWorld.ShuttleAvoidRateBackMps * 8.0) + 2.5 : d / (ZWorld.ShuttleAvoidRateOutMps * 8.0) + 1.2;
        }

        /// <summary>A move to (x, y) px is the gentle drift back: nearer its line than it is now, on its way (a ship over its spot comes back
        /// onto it at the out pace: it flies to its spot).</summary>
        bool ShuttleAvoidGentle(double x, double y) => x * x + y * y < shuttleAvX * shuttleAvX + shuttleAvY * shuttleAvY && ShuttleTrafficClass == 1;

        /// <summary>Where its flight plan puts its middle at step k of the look ahead (px): its point on the line (ShuttleBasePlan), its
        /// strafe's planned offset, and its avoid offset on its way from where it is to (tx, ty) (it gets there in `reach` s).</summary>
        void ShuttlePlanAt(double[] P, int k, double tx, double ty, double reach, out double x, out double y)
        {
            double f = reach > 1e-6 ? Math.Min(1.0, k * ZWorld.ShuttleAvoidStepSec / reach) : 1.0;
            var (rx, ry) = ShuttleRight();
            double st = shuttleStrafeMode == 1 || shuttleStrafeMode == 3 || shuttleStrafeMode == 4 ? shuttleStrafeTo : ShuttleStrafePx;
            x = P[2 * k] + st * rx + shuttleAvX + (tx - shuttleAvX) * f;
            y = P[2 * k + 1] + st * ry + shuttleAvY + (ty - shuttleAvY) * f;
        }

        /// <summary>The closest the two flight plans come (px), this ship's offset going to (tx, ty), the other's to where it goes: within
        /// ZWorld.ShuttleAvoidLookSec, or, still closing at its end, the miss distance of their way on; and the way this ship gets more room
        /// then (off the line of their relative motion, to the side it is on; head on: its own right of that motion; drifting apart: straight
        /// away from the other).</summary>
        double ShuttlePairMin(ZObject o, double tx, double ty, double share, out double nx, out double ny, bool escape = false)
        {
            double step = ZWorld.ShuttleAvoidStepSec, best = double.MaxValue, slow = 0.5 * 8.0 * step;
            int n = (int)Math.Round(ZWorld.ShuttleAvoidLookSec / step);
            double ra = ShuttleAvoidReach(tx, ty), rb = o.ShuttleAvoidReach(o.shuttleAvTx, o.shuttleAvTy);
            double[] A = ShuttleBasePlan(share), B = o.ShuttleBasePlan(o.shuttleAvSpeedK);
            nx = Id < o.Id ? 1 : -1; ny = 0;
            ShuttlePlanAt(A, 0, tx, ty, ra, out double ax, out double ay); o.ShuttlePlanAt(B, 0, o.shuttleAvTx, o.shuttleAvTy, rb, out double bx, out double by);
            double dx0 = ax - bx, dy0 = ay - by;
            // escape: closer than wanted already now: judged from the moment its offset has got where it goes (at most 3 s on)
            double skip = escape && Math.Sqrt(dx0 * dx0 + dy0 * dy0) < ZWorld.ShuttleAirSoftM * 8.0 ? Math.Min(ra, 3.0) : 0.0;
            for (int k = 1; k <= n; k++)
            {
                ShuttlePlanAt(A, k, tx, ty, ra, out ax, out ay); o.ShuttlePlanAt(B, k, o.shuttleAvTx, o.shuttleAvTy, rb, out bx, out by);
                double dx1 = ax - bx, dy1 = ay - by, ex = dx1 - dx0, ey = dy1 - dy0, ee = ex * ex + ey * ey, el = Math.Sqrt(ee);
                double u = ee > 1e-12 ? Math.Max(0.0, Math.Min(1.0, -(dx0 * ex + dy0 * ey) / ee)) : 0.0;
                double cx = dx0 + ex * u, cy = dy0 + ey * u;
                if (k == n)
                {   // beyond the look ahead: both go on as at its end (the offsets have got there), each only as far as its goal
                    double avx = (A[2 * n] - A[2 * n - 2]) / step, avy = (A[2 * n + 1] - A[2 * n - 1]) / step, bvx = (B[2 * n] - B[2 * n - 2]) / step, bvy = (B[2 * n + 1] - B[2 * n - 1]) / step;
                    double av = Math.Sqrt(avx * avx + avy * avy), bv = Math.Sqrt(bvx * bvx + bvy * bvy), ar = A[2 * (n + 1)], br = B[2 * (n + 1)];
                    double ta = av > 1e-6 ? ar / av : 0.0, tb = bv > 1e-6 ? br / bv : 0.0, tm = Math.Min(20.0, Math.Max(ta, tb));
                    double ex0 = dx1, ey0 = dy1;
                    for (double tt = 0.5; tt <= tm + 0.5 - 1e-9; tt += 0.5)
                    {
                        double ua = Math.Min(tt, ta), ub = Math.Min(tt, tb);
                        double ex1 = dx1 + avx * ua - bvx * ub, ey1 = dy1 + avy * ua - bvy * ub, fx = ex1 - ex0, fy = ey1 - ey0, ff = fx * fx + fy * fy;
                        double w = ff > 1e-12 ? Math.Max(0.0, Math.Min(1.0, -(ex0 * fx + ey0 * fy) / ff)) : 0.0;
                        double qx = ex0 + fx * w, qy = ey0 + fy * w;
                        if (qx * qx + qy * qy < cx * cx + cy * cy) { cx = qx; cy = qy; ex = fx; ey = fy; el = Math.Sqrt(ff); u = 0.5; }
                        ex0 = ex1; ey0 = ey1;
                    }
                }
                double dd = Math.Sqrt(cx * cx + cy * cy);
                if (k * step < skip - 1e-9) { dx0 = dx1; dy0 = dy1; continue; }
                if (dd < best - 1e-9)
                {
                    best = dd;
                    if ((k == 1 && u <= 0.0) || el <= slow)
                    {   // drifting apart already, or hardly moving against each other: straight away from it
                        if (dd > 1e-6) { nx = cx / dd; ny = cy / dd; } else { nx = Id < o.Id ? 1 : -1; ny = 0; }
                    }
                    else
                    {   // off the line of their relative motion, to the side it is on (head on: its own right of that motion)
                        double wx = ex / el, wy = ey / el, a2 = cx * wx + cy * wy, px = cx - a2 * wx, py = cy - a2 * wy, pl = Math.Sqrt(px * px + py * py);
                        if (pl > 2.0) { nx = px / pl; ny = py / pl; } else { nx = -wy; ny = wx; }
                    }
                }
                dx0 = dx1; dy0 = dy1;
            }
            return best;
        }

        static readonly List<ZObject> shuttleYieldTo = new List<ZObject>();                             // scratch (the rules run on one thread)

        /// <summary>The closest its plan comes to the plans of the ships it gives way to, with its offset going to (tx, ty) (px); the way
        /// that gives most room from the worst of them.</summary>
        double ShuttleWorst(double tx, double ty, double share, out double nx, out double ny, bool escape = false)
        {
            double best = double.MaxValue; nx = 0; ny = 0; shuttleWorstId = -1;
            foreach (var o in shuttleYieldTo)
            {
                double m = ShuttlePairMin(o, tx, ty, share, out double ux, out double uy, escape);
                if (m < best) { best = m; nx = ux; ny = uy; shuttleWorstId = o.Id; }
            }
            return best;
        }

        static readonly double[] AvoidRingsM = { 1.0, 2.0, 3.5, 5.0, 7.0, 9.5, 12.0, 14.0 };
        static readonly double[] SpeedShares = { 0.7, 0.45, 0.2, 0.0 };
        static double NextShare(double k) { double best = 1.0; foreach (double s in SpeedShares) if (s > k + 1e-9 && s < best) best = s; return best; }

        /// <summary>ZObject.ShuttleStep, after the strafe: where its avoid offset should go (the ships it gives way to), its smooth move there,
        /// the offset added to its place.</summary>
        internal void ShuttleTrafficStep(double dt)
        {
            if (!ShuttleTrafficLive) return;
            var w = World; double now = w.Time;
            if (!ShuttleAirborne || Destroyed) { ShuttleAvoidClear(); return; }
            // the ships it gives way to, near enough to matter
            shuttleYieldTo.Clear();
            double range = ZWorld.ShuttleAvoidRangeM * 8.0, soft = ZWorld.ShuttleAirSoftM * 8.0;
            foreach (var o in w.TrafficShips)
                if (o != this && o.ShuttleTrafficPresent && Math.Abs(o.shuttleFx - shuttleFx) < range && Math.Abs(o.shuttleFy - shuttleFy) < range
                    && (ShuttleYieldsTo(o) || (o.shuttleAskId == Id && now < o.shuttleAskUntil))) shuttleYieldTo.Add(o);   // and a ship that asked it for room
            bool hold = now - shuttleAvChoseAt < ZWorld.ShuttleAvoidMinHoldSec - 1e-9;
            bool held = shuttleAvTx != 0 || shuttleAvTy != 0 || shuttleAvSpeedK != 1.0;
            if (ShuttleTrafficLow) { }                                                                         // climbing out, or low in its descent: it does not move aside (the others go round it)
            else if (shuttleYieldTo.Count == 0)
            {   // nobody to give way to: back onto its line, at its own speed (not before the least hold)
                if (held && !hold) Choose(0, 0, 1.0, now);
            }
            else
            {
                double m = ShuttleWorst(shuttleAvTx, shuttleAvTy, shuttleAvSpeedK, out double nx, out double ny), want = soft + 0.2 * 8.0;
                double m0 = ShuttleWorst(shuttleAvTx, shuttleAvTy, shuttleAvSpeedK, out _, out _, true);           // the same, judged as the candidates are
                if (m < soft - 1e-6)
                {   // its plan does not clear them: the nearest change that does
                    double bestM = m0, bx = shuttleAvTx, by = shuttleAvTy, bk = shuttleAvSpeedK; bool found = false;
                    double px = nx, py = ny, pl = Math.Sqrt(px * px + py * py);
                    if (pl < 1e-6) { px = 1; py = 0; } else { px /= pl; py /= pl; }
                    bool noBack = now - shuttleAvTurnAt < ZWorld.ShuttleAvoidMinHoldSec - 1e-9 && (shuttleAvDirX != 0 || shuttleAvDirY != 0);
                    void Try(double cx, double cy, double k)
                    {
                        if (found) return;
                        double cl = Math.Sqrt(cx * cx + cy * cy), cap = ZWorld.ShuttleAvoidMaxM * 8.0;
                        if (cl > cap) { cx *= cap / cl; cy *= cap / cl; }
                        if (noBack)
                        {   // its motion off its line began or turned less than the least hold ago: not back the way it came (first)
                            double ex = cx - shuttleAvX, ey = cy - shuttleAvY, el = Math.Sqrt(ex * ex + ey * ey), dl = Math.Sqrt(shuttleAvDirX * shuttleAvDirX + shuttleAvDirY * shuttleAvDirY);
                            if (el > 0.8 && ex * shuttleAvDirX + ey * shuttleAvDirY < -0.5 * el * dl) return;
                        }
                        double mc = ShuttleWorst(cx, cy, k, out _, out _, true);
                        if (mc >= want) { bx = cx; by = cy; bk = k; found = true; }
                        else if (mc > bestM + 1e-6) { bestM = mc; bx = cx; by = cy; bk = k; }
                    }
                    void Rings(double ox, double oy, double k, double r0, double r1)
                    {
                        foreach (double rm in AvoidRingsM)
                        {
                            if (rm < r0 || rm > r1 || found) continue;
                            for (int j = 0; j < 16 && !found; j++)
                            {
                                int kk = (j + 1) / 2 * ((j & 1) == 1 ? 1 : -1);                                   // 0, +1, -1, +2 ...: from the way that gives room
                                double a = Math.Atan2(py, px) + kk * Math.PI / 8.0;
                                Try(ox + Math.Cos(a) * rm * 8.0, oy + Math.Sin(a) * rm * 8.0, k);
                            }
                        }
                    }
                    bool flying = ShuttleTrafficCruising;
                    for (int pass = 0; pass < 2 && !found; pass++, noBack = false)
                    {   // first never back the way it just came; then anything (the separation first)
                        Rings(shuttleAvTx, shuttleAvTy, shuttleAvSpeedK, 0, 3.5);                              // a little aside
                        if (flying) foreach (double k in SpeedShares) if (k < shuttleAvSpeedK && k >= 0.4) Try(shuttleAvTx, shuttleAvTy, k);   // a little slower: it lets the other pass
                        Rings(shuttleAvTx, shuttleAvTy, shuttleAvSpeedK, 5.0, 99);                             // further aside
                        if (shuttleAvTx != 0 || shuttleAvTy != 0) Rings(0, 0, shuttleAvSpeedK, 0, 99);        // aside, from its line
                        if (flying) foreach (double k in SpeedShares) if (k < shuttleAvSpeedK && k < 0.4) Try(shuttleAvTx, shuttleAvTy, k);    // much slower, or waiting in the air
                        if (flying) foreach (double k in SpeedShares) if (k < shuttleAvSpeedK) Rings(shuttleAvTx, shuttleAvTy, k, 0, 99);   // aside and slower
                        if (noBack && !found && bestM >= (ZWorld.ShuttleAirSepM + ZWorld.ShuttleAvoidKeepM) * 8.0) { found = true; break; }   // not back the way it came when that still keeps them well apart
                        if (!noBack) break;
                    }
                    if ((found || bestM > m0 + 2.0) && (bx != shuttleAvTx || by != shuttleAvTy || bk != shuttleAvSpeedK)) { Choose(bx, by, bk, now); ZWorld.ShuttleAvoidsBegun++; }   // none clears them: only for a quarter metre more room
                    if (found) shuttleAskId = -1;
                    else
                    {   // nothing of its own clears them: the ship still in the way is asked to make room too (it gives way to this one meanwhile)
                        ShuttleWorst(shuttleAvTx, shuttleAvTy, shuttleAvSpeedK, out _, out _, true);
                        ZObject wo = null; foreach (var o in shuttleYieldTo) if (o.Id == shuttleWorstId) wo = o;
                        if (wo != null && wo.ShuttleTrafficClass == 1 && !wo.ShuttleTrafficLow && !wo.ShuttleTrafficPinned)   // only a ship on its way (never one coming down to land: that waits for its line)
                        { shuttleAskId = shuttleWorstId; shuttleAskUntil = now + 1.0; ZWorld.ShuttleAsks++; }
                        else shuttleAskId = -1;
                    }
                }
                else if (held && !hold && ShuttleAvoidArrived(now) && ShuttleTrafficSettled(now))
                {   // clear: back onto its line at its own speed once that whole way back is clear too, or one of the two (only once its offset
                    // has come to rest where it went, the least hold after its last turn, and when the ships it gives way to have settled their
                    // own ways: none climbing out or low, none that chose anew within ShuttleAvoidSettleSec)
                    double rel = soft + ZWorld.ShuttleAvoidReleaseM * 8.0;
                    if (ShuttleWorst(0, 0, 1.0, out _, out _) >= rel) Choose(0, 0, 1.0, now);
                    else if (shuttleAvSpeedK != 1.0 && ShuttleWorst(shuttleAvTx, shuttleAvTy, 1.0, out _, out _) >= rel) Choose(shuttleAvTx, shuttleAvTy, 1.0, now);
                    else if (shuttleAvSpeedK < 0.7 && ShuttleWorst(shuttleAvTx, shuttleAvTy, NextShare(shuttleAvSpeedK), out _, out _) >= rel) Choose(shuttleAvTx, shuttleAvTy, NextShare(shuttleAvSpeedK), now);   // a step faster
                    else if ((shuttleAvTx != 0 || shuttleAvTy != 0) && ShuttleWorst(0, 0, shuttleAvSpeedK, out _, out _) >= rel) Choose(0, 0, shuttleAvSpeedK, now);
                    else if (shuttleAvSpeedK < 0.45 && ShuttleTrafficCruising && (w.TickIndex + Id) % ZWorld.ShuttleResumeTicks == 0) ShuttleResume(now, rel);
                }
            }
            ShuttleAvoidMove(dt);
            shuttleOffX += shuttleAvX; shuttleOffY += shuttleAvY;                                           // part of its place, like the strafe
        }

        /// <summary>Waiting in the air or crawling (its speed share under 0.45): on its way again round them, the fastest speed share that
        /// clears them with the release room, aside from where it is first, then from its line (looked at every ZWorld.ShuttleResumeTicks
        /// ticks).</summary>
        void ShuttleResume(double now, double rel)
        {
            foreach (double k in ResumeShares)
            {
                if (k <= shuttleAvSpeedK) continue;
                ShuttleWorst(shuttleAvTx, shuttleAvTy, k, out double nx, out double ny);
                double pl = Math.Sqrt(nx * nx + ny * ny), px = pl < 1e-6 ? 1 : nx / pl, py = pl < 1e-6 ? 0 : ny / pl;
                for (int c = 0; c < 2; c++)
                {
                    double ox = c == 0 ? shuttleAvTx : 0, oy = c == 0 ? shuttleAvTy : 0;
                    if (c == 1 && ox == shuttleAvTx && oy == shuttleAvTy) continue;
                    foreach (double rm in ResumeRingsM)
                        for (int j = 0; j < (rm == 0 ? 1 : 8); j++)
                        {
                            int kk = (j + 1) / 2 * ((j & 1) == 1 ? 1 : -1);
                            double a = Math.Atan2(py, px) + kk * Math.PI / 4.0, cx = ox + Math.Cos(a) * rm * 8.0, cy = oy + Math.Sin(a) * rm * 8.0;
                            double cl = Math.Sqrt(cx * cx + cy * cy), cap = ZWorld.ShuttleAvoidMaxM * 8.0;
                            if (cl > cap) { cx *= cap / cl; cy *= cap / cl; }
                            if (ShuttleWorst(cx, cy, k, out _, out _) >= rel) { Choose(cx, cy, k, now); ZWorld.ShuttleResumes++; return; }
                        }
                }
            }
        }
        static readonly double[] ResumeShares = { 1.0, 0.7, 0.45 };
        static readonly double[] ResumeRingsM = { 0.0, 2.0, 4.0, 7.0, 10.0, 14.0 };

        /// <summary>The ships it gives way to keep their ways for now: none low over the ground (climbing out or coming down: its own way
        /// off its line is still to come), none that chose anew within ZWorld.ShuttleAvoidSettleSec.</summary>
        bool ShuttleTrafficSettled(double now)
        {
            foreach (var o in shuttleYieldTo)
                if (o.ShuttleTrafficLow || now - o.shuttleAvChoseAt < ZWorld.ShuttleAvoidSettleSec) return false;
            return true;
        }

        /// <summary>Its offset has come to rest where it went (within a quarter metre, under 0.2 m/s), the least hold after its last turn.</summary>
        bool ShuttleAvoidArrived(double now)
        {
            double ex = shuttleAvTx - shuttleAvX, ey = shuttleAvTy - shuttleAvY, v2 = shuttleAvVx * shuttleAvVx + shuttleAvVy * shuttleAvVy;
            return ex * ex + ey * ey < 2.0 * 2.0 && v2 < 1.6 * 1.6 && now - shuttleAvTurnAt >= ZWorld.ShuttleAvoidMinHoldSec - 1e-9;
        }

        void Choose(double x, double y, double k, double now)
        {
            shuttleAvTx = x; shuttleAvTy = y; shuttleAvSpeedK = k; shuttleAvChoseAt = now; ShuttleAvoidEncounters++;
            // where it goes eased: if that has run past the new place (it would come back toward the offset first), it starts again from the
            // offset itself, so the offset heads straight for the new place (never out and back on the way)
            double fx = shuttleAvEx - shuttleAvX, fy = shuttleAvEy - shuttleAvY;
            if (fx * (x - shuttleAvEx) + fy * (y - shuttleAvEy) < 0) { shuttleAvEx = shuttleAvX; shuttleAvEy = shuttleAvY; }
        }

        void ShuttleAvoidClear()
        {
            shuttleAvX = shuttleAvY = shuttleAvVx = shuttleAvVy = shuttleAvAx = shuttleAvAy = shuttleAvTx = shuttleAvTy = shuttleAvEx = shuttleAvEy = 0;
            shuttleAvSpeedK = 1.0; shuttleAvDirX = shuttleAvDirY = 0; shuttleAvTurnAt = -1e9;
            ShuttleAvoidLatVelPx = ShuttleAvoidLatAccelPx = 0;
        }

        /// <summary>One step of the avoid offset toward where it goes (the map edge rule kept): that eased (over ZWorld.ShuttleAvoidTauOut, at
        /// most ZWorld.ShuttleAvoidRateOutMps; back gentler), followed critically damped (ZWorld.ShuttleAvoidOmegaOut; back
        /// ZWorld.ShuttleAvoidOmegaBack), its acceleration at most ZWorld.ShuttleAvoidAccMax; exactly 0 again once back and at rest.</summary>
        void ShuttleAvoidMove(double dt)
        {
            bool back = ShuttleAvoidGentle(shuttleAvTx, shuttleAvTy);
            double tau = back ? ZWorld.ShuttleAvoidTauBack : ZWorld.ShuttleAvoidTauOut, om = back ? ZWorld.ShuttleAvoidOmegaBack : ZWorld.ShuttleAvoidOmegaOut;
            double lim = (back ? ZWorld.ShuttleAvoidRateBackMps : ZWorld.ShuttleAvoidRateOutMps) * 8.0 * dt, k = 1.0 - Math.Exp(-dt / tau);
            double sx = (shuttleAvTx - shuttleAvEx) * k, sy = (shuttleAvTy - shuttleAvEy) * k, sl = Math.Sqrt(sx * sx + sy * sy);
            if (sl > lim) { sx *= lim / sl; sy *= lim / sl; }
            shuttleAvEx += sx; shuttleAvEy += sy;
            double ax = om * om * (shuttleAvEx - shuttleAvX) - 2.0 * om * shuttleAvVx, ay = om * om * (shuttleAvEy - shuttleAvY) - 2.0 * om * shuttleAvVy, al = Math.Sqrt(ax * ax + ay * ay);
            double amax = ZWorld.ShuttleAvoidAccMax * 8.0 * 0.95;
            if (al > amax) { ax *= amax / al; ay *= amax / al; }
            shuttleAvVx += ax * dt; shuttleAvVy += ay * dt; shuttleAvX += shuttleAvVx * dt; shuttleAvY += shuttleAvVy * dt; shuttleAvAx = ax; shuttleAvAy = ay;
            double vl = Math.Sqrt(shuttleAvVx * shuttleAvVx + shuttleAvVy * shuttleAvVy), dl = Math.Sqrt(shuttleAvDirX * shuttleAvDirX + shuttleAvDirY * shuttleAvDirY);
            if (vl > 0.8)
            {   // the way it moves off its line: when that motion begins or turns back (the least hold counts from then)
                if (dl < 1e-9 || shuttleAvVx * shuttleAvDirX + shuttleAvVy * shuttleAvDirY < -0.5 * vl * dl) shuttleAvTurnAt = World.Time;
                shuttleAvDirX = shuttleAvVx; shuttleAvDirY = shuttleAvVy;
            }
            var m = World.Map;
            if (m != null)
            {   // the middle stays one tile (plus half its box) in from every edge, with the strafe's offset
                double lo = 16 + WidthPix * 0.5, hx = m.WidthPix - 16 - WidthPix * 0.5, hy = m.HeightPix - 16 - HeightPix * 0.5;
                double px = shuttleFx + shuttleOffX, py = shuttleFy + shuttleOffY;
                double cx = Math.Max(lo - px, Math.Min(hx - px, shuttleAvX)), cy = Math.Max(lo - py, Math.Min(hy - py, shuttleAvY));
                if (cx != shuttleAvX) { shuttleAvX = cx; shuttleAvVx = 0; }
                if (cy != shuttleAvY) { shuttleAvY = cy; shuttleAvVy = 0; }
            }
            if (shuttleAvTx == 0 && shuttleAvTy == 0 && Math.Abs(shuttleAvEx) < 0.02 && Math.Abs(shuttleAvEy) < 0.02 && Math.Abs(shuttleAvX) < 0.02 && Math.Abs(shuttleAvY) < 0.02
                && Math.Abs(shuttleAvVx) < 0.05 && Math.Abs(shuttleAvVy) < 0.05)
            {   // back on its line, at rest: exactly 0 (the descent may begin)
                shuttleAvX = shuttleAvY = shuttleAvVx = shuttleAvVy = shuttleAvAx = shuttleAvAy = shuttleAvEx = shuttleAvEy = 0;
            }
            var (rx, ry) = ShuttleRight();
            ShuttleAvoidLatVelPx = shuttleAvVx * rx + shuttleAvVy * ry;
            ShuttleAvoidLatAccelPx = shuttleAvAx * rx + shuttleAvAy * ry;
        }

        // ------------------------------------------------------------------ landing spots
        /// <summary>The spots this ship holds (px) for the others' new goals: standing and staying = where it stands; ordered off, or up in
        /// the air = its goal once that is decided (a goal not decided yet in this tick's round holds nothing yet).</summary>
        internal int ShuttleSpotsHeld(out int x, out int y)
        {
            x = y = 0;
            bool standing = ShuttleState == ZShuttleState.Landed && ShuttleLift <= 0;
            bool decided = ShuttleGoalX >= 0 && ShuttleGoalX == shuttleClaimX && ShuttleGoalY == shuttleClaimY;
            if (standing && !ShuttleLeaving) { x = ShuttleLineX; y = ShuttleLineY; return 1; }
            if (decided) { x = ShuttleGoalX; y = ShuttleGoalY; return 1; }
            if (!standing && ShuttleGoalX < 0) { x = ShuttleLineX; y = ShuttleLineY; return 1; }
            return 0;
        }

        internal bool ShuttleSpotFresh(out bool canMove)
        {
            canMove = false;
            if (ShuttleGoalX < 0 || (ShuttleGoalX == shuttleClaimX && ShuttleGoalY == shuttleClaimY)) return false;
            canMove = ShuttleRepairTarget < 0 && (ShuttleState == ZShuttleState.Landed ? shuttleNewGoal
                      : ShuttleState == ZShuttleState.Climb || ShuttleState == ZShuttleState.Hover || (ShuttleState == ZShuttleState.Cruise && !ShuttleLandTurning && !ShuttleDeckTurning));
            return true;
        }

        internal void ShuttleSpotDecided(int x, int y, bool moved, int orderX, int orderY)
        {
            ShuttleOrderX = orderX; ShuttleOrderY = orderY; ShuttleSpotMoved = moved;
            if (moved) { ShuttleGoalX = x; ShuttleGoalY = y; shuttleNewGoal = true; ShuttlePlanOff(); }      // the order's own flag is kept: it simply flies there
            shuttleClaimX = ShuttleGoalX; shuttleClaimY = ShuttleGoalY;
        }

        internal void ShuttleSpotAdopt() { shuttleClaimX = ShuttleGoalX; shuttleClaimY = ShuttleGoalY; }

        /// <summary>Its point on the line (px, sub-pixel).</summary>
        internal double ShuttleTrafficBaseX => shuttleFx;
        internal double ShuttleTrafficBaseY => shuttleFy;
    }

    public sealed partial class ZWorld
    {
        /// <summary>DV118 traffic switch (the game: on; with one shuttle in the match nothing of it runs). Off = every ship as before.</summary>
        public static bool ShuttleTrafficOn = true;
        /// <summary>The separation in the air: the middles never closer than ShuttleAirSepM (m: the outline's farthest point 3.889 m, twice,
        /// + 1.2 m); the steering aims at ShuttleAirSoftM; a way back counts as clear with ShuttleAvoidReleaseM more.</summary>
        public const double ShuttleAirSepM = 9.0, ShuttleAirSoftM = 10.5, ShuttleAvoidReleaseM = 0.5;
        /// <summary>Within the least hold of its last turn it keeps its way (no turn back) when that still keeps this much more than
        /// ShuttleAirSepM (m).</summary>
        public const double ShuttleAvoidKeepM = 0.35;
        /// <summary>Back onto its line only when the ships it gives way to chose their own ways at least this long ago (s).</summary>
        public const double ShuttleAvoidSettleSec = 1.0;
        /// <summary>The look ahead (s) and its step (s); ships farther apart than this (m) are not looked at; the largest offset (m).</summary>
        public const double ShuttleAvoidLookSec = 6.0, ShuttleAvoidStepSec = 0.25, ShuttleAvoidRangeM = 140.0, ShuttleAvoidMaxM = 14.0;
        /// <summary>The avoid offset's move: where it goes eased over this time (s) and at most this fast (m/s), followed critically damped at
        /// this rate (1/s): out ShuttleAvoidTauOut / RateOutMps / OmegaOut, back gentler; at least ShuttleAvoidMinHoldSec (s) between two
        /// choices of where to go (the strafe's rule for two turns of the sideways motion); its acceleration at most ShuttleAvoidAccMax (m/s^2).</summary>
        public const double ShuttleAvoidTauOut = 0.4, ShuttleAvoidRateOutMps = 2.5, ShuttleAvoidOmegaOut = 1.8, ShuttleAvoidTauBack = 0.7, ShuttleAvoidRateBackMps = 2.2,
                            ShuttleAvoidOmegaBack = 1.1, ShuttleAvoidMinHoldSec = 3.0, ShuttleAvoidAccMax = 2.0;
        /// <summary>Two ships' landing spots at least this far apart (m); free spots looked for on rings round the ordered point out to this
        /// far (m), at most ShuttleClaimTests of them tested as landing spots.</summary>
        public const double ShuttleClaimM = 15.0, ShuttleClaimReachM = 45.0;
        public const int ShuttleClaimTests = 48;
        /// <summary>A take-off waits while another ship is up within this (m).</summary>
        public const double ShuttleLiftAirM = 10.0;

        /// <summary>Check counters (reset by the checks): offsets chosen to give way, goals moved to a free spot, orders with no free spot near,
        /// take-offs that waited for room in the air, holds over a spot being left.</summary>
        public static int ShuttleAvoidsBegun, ShuttleSpotsMoved, ShuttleSpotsNone, ShuttleAirWaits, ShuttleHolds, ShuttleAsks, ShuttleResumes;
        /// <summary>A ship waiting in the air looks for a way round every this many ticks.</summary>
        public const int ShuttleResumeTicks = 25;

        long trafficTick = -1;
        /// <summary>The shuttles of this match standing or flying (made once a tick by the first shuttle's step).</summary>
        internal readonly List<ZObject> TrafficShips = new List<ZObject>();
        readonly List<ZObject> trafficFresh = new List<ZObject>();
        readonly HashSet<int> trafficTiles = new HashSet<int>();

        /// <summary>Once a tick (the first shuttle's step): the ships, then the spots of the goals given since the last tick.</summary>
        internal void ShuttleTrafficTick()
        {
            if (trafficTick == TickIndex) return;
            trafficTick = TickIndex;
            TrafficShips.Clear();
            foreach (var o in Objects) if (o.IsShuttle && !o.Destroyed && o.KillAt < 0) TrafficShips.Add(o);
            trafficFresh.Clear();
            foreach (var s in TrafficShips)
            {
                if (!s.ShuttleSpotFresh(out bool canMove)) continue;
                if (canMove) trafficFresh.Add(s); else s.ShuttleSpotAdopt();                                 // already on its way down (or a repair deck): its own spot
            }
            if (trafficFresh.Count == 0) return;
            trafficFresh.Sort((a, b) =>
            {
                double da = Sq(a.ShuttleGoalX - a.ShuttleTrafficBaseX) + Sq(a.ShuttleGoalY - a.ShuttleTrafficBaseY), db = Sq(b.ShuttleGoalX - b.ShuttleTrafficBaseX) + Sq(b.ShuttleGoalY - b.ShuttleTrafficBaseY);
                return da != db ? da.CompareTo(db) : a.Id.CompareTo(b.Id);
            });
            foreach (var s in trafficFresh) ShuttleClaimSpot(s);
        }

        static double Sq(double v) => v * v;

        /// <summary>No other ship holds a spot within ShuttleClaimM of (x, y).</summary>
        bool ShuttleClaimFree(ZObject s, int x, int y)
        {
            double lim = ShuttleClaimM * 8.0;
            foreach (var o in TrafficShips)
            {
                if (o == s || o.Destroyed || o.KillAt >= 0 || o.ShuttleSpotsHeld(out int hx, out int hy) == 0) continue;
                double dx = hx - x, dy = hy - y;
                if (dx * dx + dy * dy < lim * lim) return false;
            }
            return true;
        }

        readonly List<int> trafficCand = new List<int>();

        static double SegDist(double ax, double ay, double bx, double by, double x, double y)
        {
            double ex = bx - ax, ey = by - ay, ee = ex * ex + ey * ey, u = ee > 1e-9 ? Math.Max(0.0, Math.Min(1.0, ((x - ax) * ex + (y - ay) * ey) / ee)) : 0.0;
            double dx = ax + ex * u - x, dy = ay + ey * u - y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>Ship s flying in to (x, y) px passes no other ship's spot (beyond ShuttleAirSoftM; spots near its start do not count), and
        /// (x, y) lies on no other ship's way in to its spot (the same, near that ship's start not counted).</summary>
        bool ShuttleApproachClear(ZObject s, int x, int y)
        {
            double soft = ShuttleAirSoftM * 8.0, near = ShuttleClaimM * 8.0, sx = s.ShuttleTrafficBaseX, sy = s.ShuttleTrafficBaseY;
            foreach (var o in TrafficShips)
            {
                if (o == s || o.Destroyed || o.KillAt >= 0 || o.ShuttleSpotsHeld(out int hx, out int hy) == 0) continue;
                if (Math.Sqrt(Sq(hx - sx) + Sq(hy - sy)) > near && SegDist(sx, sy, x, y, hx, hy) < soft) return false;   // its way in passes that spot
                double ox = o.ShuttleTrafficBaseX, oy = o.ShuttleTrafficBaseY;
                if (Sq(hx - ox) + Sq(hy - oy) > 16.0 * 16.0 && Math.Sqrt(Sq(x - ox) + Sq(y - oy)) > near && SegDist(ox, oy, hx, hy, x, y) < soft) return false;   // on that ship's way in
            }
            return true;
        }

        /// <summary>A new goal: kept when no other ship holds a spot near it, else the nearest free landing spot round it whose way in is
        /// clear of the other ships' spots and ways in (else the nearest free one; only free of the others' spots when the ordered point
        /// itself is no landing spot: it hovers there as it would at that point).</summary>
        void ShuttleClaimSpot(ZObject s)
        {
            int px = s.ShuttleGoalX, py = s.ShuttleGoalY;
            if (ShuttleClaimFree(s, px, py)) { s.ShuttleSpotDecided(px, py, false, px, py); return; }
            bool landable = ShuttleLandSpot(px, py, s, s.ShuttleHeadingDeg, out _, out _);
            double sx = s.ShuttleTrafficBaseX - px, sy = s.ShuttleTrafficBaseY - py;
            double a0 = sx * sx + sy * sy > 1.0 ? Math.Atan2(sy, sx) : (s.ShuttleHeadingDeg + 180.0) * Math.PI / 180.0;   // the side toward the ship first
            trafficTiles.Clear(); trafficTiles.Add((px >> 4) * 65536 + (py >> 4)); trafficCand.Clear();
            for (double r = 4.0; r <= ShuttleClaimReachM + 1e-9; r += 4.0)
            {
                int n = Math.Max(6, (int)Math.Ceiling(2.0 * Math.PI * r / 4.0));
                for (int j = 0; j < n; j++)
                {
                    int k = (j + 1) / 2 * ((j & 1) == 1 ? 1 : -1);                                               // 0, +1, -1, +2, -2 ...: outward from the ship's side
                    double a = a0 + 2.0 * Math.PI * k / n;
                    var (cx, cy) = ShuttleClamp((int)Math.Round(px + Math.Cos(a) * r * 8.0), (int)Math.Round(py + Math.Sin(a) * r * 8.0));
                    if (!trafficTiles.Add((cx >> 4) * 65536 + (cy >> 4)) || !ShuttleClaimFree(s, cx, cy)) continue;
                    trafficCand.Add(cx); trafficCand.Add(cy);
                }
            }
            int tests = 0, firstX = int.MinValue, firstY = 0;
            for (int i = 0; i + 1 < trafficCand.Count; i += 2)
            {   // the nearest landing spot with a clear way in; the nearest landing spot at all kept as the second choice
                int cx = trafficCand[i], cy = trafficCand[i + 1];
                bool clear = ShuttleApproachClear(s, cx, cy);
                if (!clear && firstX != int.MinValue) continue;
                if (landable)
                {
                    if (tests++ >= ShuttleClaimTests) break;
                    if (!ShuttleLandSpot(cx, cy, s, s.ShuttleHeadingDeg, out _, out _)) continue;
                }
                if (clear) { ShuttleSpotsMoved++; s.ShuttleSpotDecided(cx, cy, true, px, py); return; }
                if (firstX == int.MinValue) { firstX = cx; firstY = cy; }
            }
            if (firstX != int.MinValue) { ShuttleSpotsMoved++; s.ShuttleSpotDecided(firstX, firstY, true, px, py); return; }
            ShuttleSpotsNone++; s.ShuttleSpotDecided(px, py, false, px, py);                                     // nothing free near: the ordered point
        }
    }
}
