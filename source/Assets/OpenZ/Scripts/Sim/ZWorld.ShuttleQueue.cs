using System;
using System.Collections.Generic;

namespace OpenZ.Sim
{
    /// <summary>One middle point of a shuttle's route passed in the air (DV125): the checks and the view read it.</summary>
    public struct ZShuttlePass
    {
        public int seq, id, index, x, y;
        /// <summary>Where the ship's point on its line was when it took the next point (px), how far that was from the middle point (px).</summary>
        public double atX, atY, distPx;
        /// <summary>Its lift (1 = at the cruise height), state, speed (px/s), the turn at this point (degrees) and the moment.</summary>
        public double lift, speedPx, turnDeg, time;
        public ZShuttleState state;
    }

    /// <summary>
    /// waypoint list. Switch <see cref="ZWorld.ShuttleQueueDeviation"/> (on; off = DV118 as it was: a list gives the ship only its first
    /// move). Deterministic: the order's points, the ship's own sim state and sim dt only; no random draw, no frame time, nothing from the view.
    ///  * The order (ZWorld.ShuttleOrder, SEND_WAYPOINTS as for every unit: the list replaces the old one): a list with two or more MOVE
    ///    points (each clamped to the map, a point within <see cref="ZWorld.ShuttleSameSpotPx"/> of the one before it dropped) makes the
    ///    LAST point the ship's goal (ZObject.ShuttleGoalX / Y: the landing, its landing-spot rule, the traffic's spot, the hover when it
    ///    cannot land, an EXIT given in the air, a repair station: all exactly as for a one-point order) and the points before it its middle
    ///    points (<see cref="ZObject.ShuttleViaCount"/>, at most <see cref="ZWorld.ShuttleViaMax"/>). One point, STOP, a repair order, a CPU
    ///    team's order: unchanged. Any new order replaces the route (ShuttleSetGoal clears the middle points); the view's Shift click while it
    ///    flies sends the route still to fly plus the new points (View/OzShuttleQueueInput.cs), so a Shift point is added at the end.
    ///  * The flight: it takes off, climbs and turns toward the first middle point (not the goal); at the cruise height it flies through
    ///    every middle point without stopping, coming down or landing: within its pass radius of the point (the turn's arc at the pass
    ///    speed: <see cref="ZWorld.ShuttlePassRadiusPx"/>, between <see cref="ZWorld.ShuttlePassMinM"/> and <see cref="ZWorld.ShuttlePassMaxM"/>)
    ///    the next point becomes its heading's target, so it eases its turn through the point. Before a turn it slows to the pass speed
    ///    (<see cref="ZWorld.ShuttlePassSpeedK"/>: full speed straight on, half at a right angle, at least <see cref="ZWorld.ShuttlePassFloorK"/>)
    ///    on the flight model's own braking curve, and through the turn it keeps at least that speed (never the stop a one-point order
    ///    makes); the speed always leaves room for the planned stop at the goal over the route still to fly. After the last middle point the
    ///    flight model of ZWorld.ShuttleFlight.cs takes the ship to its goal as before (the planned stop, then down or hovering).
    ///  * The air traffic (K338) plans each ship along its whole route (ZWorld.ShuttleTraffic.cs, the route's points instead of the straight
    ///    line) and a ship flying through a middle point is "on its way" (never "settling" over a point it does not land on).
    /// </summary>
    public partial class ZObject
    {
        /// <summary>The middle points still to fly through (x, y pairs, px); null until a route is given.</summary>
        List<int> shuttleVias;
        /// <summary>Through a turn at a middle point: the speed kept at least this (px/s) until it is lined up with its next point.</summary>
        bool shuttlePassing; double shuttlePassVc;

        /// <summary>DV125: middle points still to fly through before the goal (0 = a plain one-point flight).</summary>
        public int ShuttleViaCount => shuttleVias == null ? 0 : shuttleVias.Count >> 1;
        /// <summary>DV125: the middle point i still to fly through (centre px; 0 = the next one).</summary>
        public int ShuttleViaX(int i) => shuttleVias[2 * i];
        public int ShuttleViaY(int i) => shuttleVias[2 * i + 1];
        /// <summary>DV125: where the ship heads now: its next middle point, else its goal.</summary>
        public int ShuttleSteerX => shuttleVias != null && shuttleVias.Count > 0 ? shuttleVias[0] : ShuttleGoalX;
        public int ShuttleSteerY => shuttleVias != null && shuttleVias.Count > 0 ? shuttleVias[1] : ShuttleGoalY;
        /// <summary>DV125 counts: middle points flown through, routes given (two points or more).</summary>
        public int ShuttleViasPassed, ShuttleRoutes;
        /// <summary>DV125: turning through a middle point (its pass speed held until it is lined up with the next point).</summary>
        public bool ShuttlePassing => shuttlePassing;

        /// <summary>A new goal (ShuttleSetGoal) or the route's end: no middle points left.</summary>
        internal void ShuttleViasClear() { if (shuttleVias != null) shuttleVias.Clear(); shuttlePassing = false; }

        internal void ShuttleViasSet(List<(int x, int y)> pts, int count)
        {
            if (shuttleVias == null) shuttleVias = new List<int>(2 * Math.Max(4, count)); else shuttleVias.Clear();
            for (int i = 0; i < count; i++) { shuttleVias.Add(pts[i].x); shuttleVias.Add(pts[i].y); }
            shuttlePassing = false; ShuttleRoutes++;
        }

        /// <summary>The route still to fly from (bx, by) px: through every middle point to the goal (px).</summary>
        public double ShuttleRouteRestPx(double bx, double by)
        {
            double d = 0, x = bx, y = by;
            int n = ShuttleViaCount;
            for (int i = 0; i <= n; i++)
            {
                double px = i < n ? shuttleVias[2 * i] : ShuttleGoalX, py = i < n ? shuttleVias[2 * i + 1] : ShuttleGoalY;
                d += Math.Sqrt((px - x) * (px - x) + (py - y) * (py - y)); x = px; y = py;
            }
            return d;
        }

        /// <summary>The point s px along the route from (bx, by) (through the middle points; at most the goal).</summary>
        public void ShuttleRouteAt(double bx, double by, double s, out double x, out double y)
        {
            x = bx; y = by;
            int n = ShuttleViaCount;
            for (int i = 0; i <= n; i++)
            {
                double px = i < n ? shuttleVias[2 * i] : ShuttleGoalX, py = i < n ? shuttleVias[2 * i + 1] : ShuttleGoalY;
                double l = Math.Sqrt((px - x) * (px - x) + (py - y) * (py - y));
                if (s <= l && l > 1e-9) { x += (px - x) * s / l; y += (py - y) * s / l; return; }
                s -= l; x = px; y = py;
            }
        }

        /// <summary>The turn at middle point i (degrees, 0 = straight on .. 180 = back): between the way into it (from the point on its
        /// line for the next one, else from the point before) and the way out of it (to the next point or the goal).</summary>
        double ShuttleViaTurnDeg(int i)
        {
            double vx = shuttleVias[2 * i], vy = shuttleVias[2 * i + 1];
            double ax = i == 0 ? shuttleFx : shuttleVias[2 * i - 2], ay = i == 0 ? shuttleFy : shuttleVias[2 * i - 1];
            int n = ShuttleViaCount;
            double bx = i + 1 < n ? shuttleVias[2 * i + 2] : ShuttleGoalX, by = i + 1 < n ? shuttleVias[2 * i + 3] : ShuttleGoalY;
            double ix = vx - ax, iy = vy - ay, ox = bx - vx, oy = by - vy;
            double li = Math.Sqrt(ix * ix + iy * iy), lo = Math.Sqrt(ox * ox + oy * oy);
            if (lo < 1.0) return 0.0;
            if (li < 1.0) { double h = ShuttleHeadingDeg * Math.PI / 180.0; ix = Math.Cos(h); iy = Math.Sin(h); li = 1.0; }
            double c = (ix * ox + iy * oy) / (li * lo);
            return Math.Acos(Math.Max(-1.0, Math.Min(1.0, c))) * 180.0 / Math.PI;
        }

        /// <summary>Cruise with middle points ahead (or still turning through the last one): one step of the eased flight toward the next
        /// point, never a stop. False always (the goal itself is reached by ShuttleFlyStep's own planned stop).</summary>
        bool ShuttleViaStep(double dt)
        {
            if (shuttlePlan != 0) ShuttlePlanOff();
            double vmax = RealMoveSpeed, v0 = ShuttleSpeedPx;
            // the passes: within the pass radius of the next middle point, the point after it becomes the target (in the air, no stop)
            for (int guard = 0; ShuttleViaCount > 0 && guard <= ZWorld.ShuttleViaMax; guard++)
            {
                double vx = shuttleVias[0], vy = shuttleVias[1], dx = vx - shuttleFx, dy = vy - shuttleFy, d = Math.Sqrt(dx * dx + dy * dy);
                double th = ShuttleViaTurnDeg(0), vc = ZWorld.ShuttlePassSpeedPx(th, vmax);
                if (d > ZWorld.ShuttlePassRadiusPx(th, vc)) break;
                World?.ShuttlePassNote(this, (int)vx, (int)vy, d, th);
                ShuttleViasPassed++;
                shuttleVias.RemoveRange(0, 2);
                shuttlePassing = true; shuttlePassVc = vc;
            }
            double tx = ShuttleSteerX, ty = ShuttleSteerY, ex = tx - shuttleFx, ey = ty - shuttleFy, dt0 = Math.Sqrt(ex * ex + ey * ey);
            double bearing = Math.Atan2(ey, ex) * 180.0 / Math.PI;
            if (ShuttleViaCount == 0)
            {   // after the last middle point: once lined up with the goal (or near it) the flight model's own approach and planned stop take over
                double e0 = Math.Abs(ShuttleDelta(ShuttleHeadingDeg, bearing));
                if (!shuttlePassing || e0 <= 12.0 || dt0 <= 2.0 * ZWorld.ShuttleSettlePx) { shuttlePassing = false; return ShuttleFlyStep(dt); }
            }
            double err = ShuttleTurnToward(bearing, dt);
            double c = Math.Max(0.0, Math.Cos(err * Math.PI / 180.0));
            double vt = vmax * c * c;
            if (shuttlePassing)
            {
                if (Math.Abs(err) <= 12.0) shuttlePassing = false;
                else vt = Math.Max(vt, shuttlePassVc);                                                     // through the turn: never slower than the pass speed
            }
            if (ShuttleViaCount > 0)
            {   // the next turn: slow to its pass speed on the braking curve before its pass radius
                double th = ShuttleViaTurnDeg(0), vc = ZWorld.ShuttlePassSpeedPx(th, vmax), r = ZWorld.ShuttlePassRadiusPx(th, vc);
                vt = Math.Min(vt, Math.Max(vc, ShuttleCurveSpeed(Math.Max(0.0, dt0 - r), vmax)));
            }
            vt = Math.Min(vt, ShuttleCurveSpeed(ShuttleRouteRestPx(shuttleFx, shuttleFy), vmax));         // the planned stop at the goal still fits in the route left
            vt = Math.Min(vt, ShuttleTrafficVmax(vmax));                                                    // K338: held slower while it gives way in time
            double acc = ShuttleTrack(v0, shuttleAcc, vt, dt);
            double v = v0 + acc * dt;
            if (Math.Abs(v - vt) < 1e-9) v = vt;
            if (v > vmax) { v = vmax; acc = (v - v0) / dt; }
            if (v < 0) { v = 0; acc = -v0 / dt; }
            double h = ShuttleHeadingDeg * Math.PI / 180.0;
            shuttleFx += Math.Cos(h) * v * dt; shuttleFy += Math.Sin(h) * v * dt;
            ShuttleSpeedPx = v; ShuttleAccelPx = (v - v0) / dt; shuttleAcc = acc; ShuttleBraking = false;
            ShuttleClampToMap();
            return false;
        }
    }

    public sealed partial class ZWorld
    {
        /// <summary>DV125 switch: on = a shuttle flies its whole move list (the middle points in the air, the landing at the last); off =
        /// DV118 as it was (only the first move of a list). Only matters while DV118 makes shuttles.</summary>
        public static bool ShuttleQueueDeviation = true;
        /// <summary>The most middle points one route keeps (the rest of a longer list: dropped, the goal is still the last point).</summary>
        public const int ShuttleViaMax = 32;
        /// <summary>The pass: its radius (m) at least / at most; the pass speed's floor (share of the top speed); the lead (s) the turn needs
        /// to build up its rate; the turns sharper than this (degrees) take the arc of this one (a U-turn passes close and slow).</summary>
        public static double ShuttlePassMinM = 1.5, ShuttlePassMaxM = 7.0, ShuttlePassFloorK = 0.2, ShuttlePassLeadSec = 0.3, ShuttlePassArcCapDeg = 120.0;
        /// <summary>DV125: the speed share through a turn of `turnDeg`: cos^2 of half the turn (1 straight on, 0.5 at a right angle), at least the floor.</summary>
        public static double ShuttlePassSpeedK(double turnDeg) { double c = Math.Cos(Math.Min(180.0, Math.Max(0.0, turnDeg)) * Math.PI / 360.0); return Math.Max(ShuttlePassFloorK, c * c); }
        public static double ShuttlePassSpeedPx(double turnDeg, double vmax) => vmax * ShuttlePassSpeedK(turnDeg);
        /// <summary>DV125: how near the middle point the next point becomes the target (px): the arc of the turn at the pass speed and the
        /// full turn rate (radius v / omega, cut back by tan(half the turn)) plus the lead, within the min / max.</summary>
        public static double ShuttlePassRadiusPx(double turnDeg, double vPass)
        {
            double w = ShuttleTurnMaxDeg * Math.PI / 180.0, half = Math.Min(ShuttlePassArcCapDeg, Math.Max(0.0, turnDeg)) * Math.PI / 360.0;
            double r = vPass / w * Math.Tan(half) + vPass * ShuttlePassLeadSec;
            return Math.Max(ShuttlePassMinM * 8.0, Math.Min(ShuttlePassMaxM * 8.0, r));
        }

        /// <summary>DV125: the last middle points passed (at most 128; seq counts up).</summary>
        public readonly List<ZShuttlePass> ShuttlePasses = new List<ZShuttlePass>();
        public int ShuttlePassSeq { get; private set; }
        /// <summary>DV125 counts: routes given (two points or more), middle points dropped because they lay on the point before.</summary>
        public int ShuttleRouteOrders { get; private set; }
        public int ShuttleRoutePointsMerged { get; private set; }

        internal void ShuttlePassNote(ZObject s, int x, int y, double distPx, double turnDeg)
        {
            ShuttlePasses.Add(new ZShuttlePass
            {
                seq = ++ShuttlePassSeq, id = s.Id, index = s.ShuttleViasPassed, x = x, y = y, atX = s.ShuttleBaseX, atY = s.ShuttleBaseY, distPx = distPx,
                lift = s.ShuttleLift, speedPx = s.ShuttleSpeedPx, turnDeg = turnDeg, time = Time, state = s.ShuttleState,
            });
            if (ShuttlePasses.Count > 128) ShuttlePasses.RemoveAt(0);
        }

        /// <summary>DV125: the move points of an order list as the shuttle reads them: null = not a route (the switch off, a repair order
        /// first, fewer than two move points after the merge): ShuttleOrder's one-point rules then, unchanged. Else every MOVE / FORCE MOVE
        /// point in order, clamped to the map, a point on the one before it (within ShuttleSameSpotPx) left out; raw = the last point as
        /// given (the repair station test reads it, as for one point).</summary>
        List<(int x, int y)> ShuttleRoutePoints(IList<ZWaypoint> list, out (int x, int y) raw, out int merged)
        {
            raw = (-1, -1); merged = 0;
            if (!ShuttleQueueDeviation || list == null || list.Count < 2) return null;
            List<(int x, int y)> pts = null;
            foreach (var wp in list)
            {
                if (wp.mode == WpMode.UnitRepair) { if (pts == null) return null; continue; }                 // a repair order first: its own rules
                if (wp.mode != WpMode.Move && wp.mode != WpMode.ForceMove) continue;                          // attack, enter, ...: nothing to it
                var c = ShuttleClamp(wp.x, wp.y);
                pts ??= new List<(int x, int y)>();
                if (pts.Count > 0) { var p = pts[pts.Count - 1]; if ((c.x - p.x) * (c.x - p.x) + (c.y - p.y) * (c.y - p.y) <= ShuttleSameSpotPx * ShuttleSameSpotPx) { merged++; raw = (wp.x, wp.y); continue; } }
                pts.Add(c); raw = (wp.x, wp.y);
            }
            return pts != null && pts.Count >= 2 ? pts : null;
        }

        /// <summary>ShuttleOrder hook (a human team's list that is not STOP): true = taken as a route (DV125); false = the one-point rules.</summary>
        bool ShuttleQueueOrder(ZObject o, IList<ZWaypoint> list)
        {
            var pts = ShuttleRoutePoints(list, out var raw, out int merged);
            if (pts == null) return false;
            o.ShuttleRepairLeave();                                                                         // item 271: any new order calls it off the station
            var last = pts[pts.Count - 1];
            var st = ShuttleStationAt(raw.x, raw.y, o);
            bool repair = st != null && ShuttleRepairOrder(o, st.Id, false);                               // item 271: the route ends on an own station: repaired there, it stays
            if (!repair) o.ShuttleSetGoal(last.x, last.y);
            if (!(o.ShuttleOnRepairPad && repair)) o.ShuttleViasSet(pts, Math.Min(ShuttleViaMax, pts.Count - 1));
            ShuttleRouteOrders++; ShuttleRoutePointsMerged += merged;
            return true;
        }

        /// <summary>K256g's order filter (ShuttleOrdersFilter) and the checks: the move whose landing counts in an order list: the LAST move
        /// of a route (DV125), else the first move as before; null = none (a repair order first, no move).</summary>
        public ZWaypoint? ShuttleLandingMove(IList<ZWaypoint> list)
        {
            if (list == null) return null;
            bool route = ShuttleRoutePoints(list, out _, out _) != null;
            ZWaypoint? found = null;
            foreach (var wp in list)
            {
                if (wp.mode == WpMode.UnitRepair) { if (found == null) return null; continue; }
                if (wp.mode != WpMode.Move && wp.mode != WpMode.ForceMove) continue;
                found = wp;
                if (!route) break;                                                                          // one point (or the switch off): the first move, as before
            }
            return found;
        }
    }
}
