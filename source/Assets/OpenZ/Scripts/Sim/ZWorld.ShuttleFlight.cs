using System;

namespace OpenZ.Sim
{
    /// <summary>
    /// DV118, the shuttle's flight model (called from ZObject.ShuttleStep; deterministic, sim dt, no random draw):
    ///  * Facing: ShuttleHeadingDeg is the ship's facing at all times. It turns only here, at most ZWorld.ShuttleTurnMaxDeg deg/s, the
    ///    rate itself changing at most ZWorld.ShuttleTurnAccelDeg deg/s^2, eased over the last 35 degrees (and never faster than it
    ///    can still stop on the bearing), never past the bearing.
    ///  * Speed along the facing, eased at both ends (an S-curve, never a step in the acceleration): the speed follows its target
    ///    with the acceleration at most a = vmax / ZWorld.ShuttleAccelSec either way, the acceleration itself building up and fading
    ///    out over ZWorld.ShuttleJerkSec (the jerk vmax / (ShuttleAccelSec x ShuttleJerkSec)). After the take-off it speeds up
    ///    gently (0 to full speed in ShuttleAccelSec + ShuttleJerkSec s), and the target is held to vmax x cos(error)^2 (it slows to
    ///    swing round instead of sliding) and to the speed from which the planned stop still fits in the distance left.
    ///  * The planned stop: lined up with the goal (within 12 degrees) and as near as its stop needs, it brakes on a planned curve in
    ///    time (a quartic: from its speed and acceleration now to speed 0 AND acceleration 0 exactly on the goal; from full speed it
    ///    takes ZWorld.ShuttleBrakeFullSec s with the same peak deceleration a). The goal is the landing point or the hover spot.
    ///    A straight flight from rest to rest therefore takes ZWorld.ShuttleStraightSec(d, vmax) = d / vmax + 2.3 s.
    ///  * Settle: within ZWorld.ShuttleSettlePx of the goal with the goal well off the nose (more than 60 degrees) or nearly stopped,
    ///    it slides straight onto it on a planned hop (eased at both ends, at most about 8 px/s) without turning.
    ///  * Climb: it does not move but turns toward the goal. Over its repair station (the deck point, ZWorld.ShuttleRepair.cs) it hovers
    ///    in place and turns onto ZWorld.ShuttleDeckHeadingDeg at most ZWorld.ShuttleDeckTurnMaxDeg deg/s (the rate eased in and out at
    ///    most ZWorld.ShuttleDeckTurnAccelDeg deg/s^2) until ZWorld.ShuttleDeckSeatLeftDeg are left; Descend: it does not move; on the deck
    ///    it turns those last degrees, eased in time (its turn rate carried in), so it faces the deck heading exactly at touchdown.
    ///  * The middle never comes nearer than one tile (plus half its box) to the map edge.
    ///  * Outputs: ShuttleTurnRateDeg, ShuttleSpeedPx, ShuttleAccelPx (the signed acceleration: the view pitches the nose by it),
    ///    ShuttleBraking; Dx / Dy = the real velocity (the game's lead for rockets and shells; ZObject.ShuttleStep sets it, the strafe
    ///    of ZWorld.ShuttleEvade.cs included).
    /// </summary>
    public partial class ZObject
    {
        double shuttleRate;                                       // the turn rate carried from step to step (deg/s)
        bool shuttleSeatTurn; double shuttleSeatFrom, shuttleSeatErr, shuttleSeatRate0, shuttleSeatT0, shuttleSeatDur;   // the timed turn onto the deck heading in the descent
        double shuttleAcc;                                        // the acceleration carried from step to step (px/s^2): the S-curve's state
        int shuttlePlan;                                          // 0 = none, 1 = the planned stop, 2 = the settle hop
        double shuttlePlanT, shuttlePlanAge, shuttlePlanV0, shuttlePlanA0, shuttlePlanD0, shuttlePlanX0, shuttlePlanY0, shuttlePlanUx, shuttlePlanUy;

        static double ShuttleDelta(double from, double to)
        {
            double d = (to - from) % 360.0;
            if (d > 180.0) d -= 360.0; else if (d <= -180.0) d += 360.0;
            return d;
        }

        static double ShuttleNorm(double a)
        {
            a %= 360.0;
            if (a > 180.0) a -= 360.0; else if (a <= -180.0) a += 360.0;
            return a;
        }

        static double ShuttleSmooth01(double x) { x = x < 0 ? 0 : x > 1 ? 1 : x; return x * x * (3 - 2 * x); }

        double ShuttleAccelRate => Math.Max(1e-6, ZWorld.ShuttleAccelMax(RealMoveSpeed));
        double ShuttleJerkRate => Math.Max(1e-6, ZWorld.ShuttleJerkMax(RealMoveSpeed));

        /// <summary>One step of the eased turn toward `target` (degrees); returns the heading error left.</summary>
        double ShuttleTurnToward(double target, double dt)
        {
            double hd = ShuttleHeadingDeg, err = ShuttleDelta(hd, target), ae = Math.Abs(err);
            double want = Math.Sign(err) * Math.Min(ZWorld.ShuttleTurnMaxDeg * ShuttleSmooth01(Math.Min(ae, 35.0) / 35.0), Math.Sqrt(2.0 * ZWorld.ShuttleTurnAccelDeg * ae));
            double step = ZWorld.ShuttleTurnAccelDeg * dt;
            double r = shuttleRate + Math.Max(-step, Math.Min(step, want - shuttleRate));
            r = Math.Max(-ZWorld.ShuttleTurnMaxDeg, Math.Min(ZWorld.ShuttleTurnMaxDeg, r));
            double turn = r * dt;
            if (turn * err > 0 && Math.Abs(turn) >= ae) { turn = err; r = err / dt; }    // never past the bearing
            ShuttleHeadingDeg = (float)ShuttleNorm(hd + turn);
            shuttleRate = r;
            return ShuttleDelta(ShuttleHeadingDeg, target);
        }

        /// <summary>The rate fades out (no turn wanted: landed, hovering, settling).</summary>
        void ShuttleNoTurn(double dt)
        {
            double step = ZWorld.ShuttleTurnAccelDeg * dt;
            shuttleRate = Math.Abs(shuttleRate) <= step ? 0 : shuttleRate - Math.Sign(shuttleRate) * step;
            if (shuttleRate != 0)
            {   // the rate left over keeps turning a little until it is gone (no snap of the facing)
                ShuttleHeadingDeg = (float)ShuttleNorm(ShuttleHeadingDeg + shuttleRate * dt);
            }
        }

        /// <summary>Climb: on the spot, turning toward the goal.</summary>
        void ShuttleClimbTurn(double dt)
        {
            double dx = ShuttleSteerX - shuttleFx, dy = ShuttleSteerY - shuttleFy;                       // DV125: toward the next middle point (the goal when there is none)
            if (ShuttleGoalX >= 0 && dx * dx + dy * dy > ZWorld.ShuttleSettlePx * ZWorld.ShuttleSettlePx) ShuttleTurnToward(Math.Atan2(dy, dx) * 180.0 / Math.PI, dt);
            else ShuttleNoTurn(dt);
        }

        /// <summary>The hover turn over the repair station (no movement): one step toward `target` (degrees) at most
        /// ZWorld.ShuttleDeckTurnMaxDeg deg/s, the rate changing at most ZWorld.ShuttleDeckTurnAccelDeg deg/s^2 (eased in) and eased out over
        /// the last ZWorld.ShuttleDeckTurnEaseDeg degrees (never faster than it can still stop on the target), never past it. Returns the
        /// heading error left.</summary>
        internal double ShuttleHoverTurn(double target, double dt)
        {
            double hd = ShuttleHeadingDeg, err = ShuttleDelta(hd, target), ae = Math.Abs(err), max = ZWorld.ShuttleDeckTurnMaxDeg, acc = ZWorld.ShuttleDeckTurnAccelDeg;
            double want = Math.Sign(err) * Math.Min(max * ShuttleSmooth01(Math.Min(ae, ZWorld.ShuttleDeckTurnEaseDeg) / ZWorld.ShuttleDeckTurnEaseDeg), Math.Sqrt(2.0 * acc * ae));
            double step = acc * dt;
            double r = shuttleRate + Math.Max(-step, Math.Min(step, want - shuttleRate));
            r = Math.Max(-max, Math.Min(max, r));
            double turn = r * dt;
            if (turn * err > 0 && Math.Abs(turn) >= ae) { turn = err; r = err / dt; }    // never past the target
            ShuttleHeadingDeg = (float)ShuttleNorm(hd + turn);
            shuttleRate = r;
            return ShuttleDelta(ShuttleHeadingDeg, target);
        }

        /// <summary>The turn rate now (deg/s): the descent onto the deck may begin only when it is slow (the seat's quintic takes it in).</summary>
        internal double ShuttleTurnRateNow => shuttleRate;

        /// <summary>Descend: onto the deck the facing turns the degrees left onto ZWorld.ShuttleDeckHeadingDeg over the descent that is left,
        /// eased in time (a quintic from the turn rate it carries in to rest), so it faces the deck heading exactly at touchdown; on a plain
        /// landing it keeps its facing.</summary>
        void ShuttleDescendTurn(double dt, bool deck)
        {
            var seatTo = ShuttleSeatHeading(deck);                                                        // K256e: the deck's heading, or the landing heading (ZWorld.ShuttleGround.cs)
            if (seatTo == null) { shuttleSeatTurn = false; ShuttleNoTurn(dt); return; }
            double now = World.Time;
            if (!shuttleSeatTurn)
            {
                shuttleSeatTurn = true; shuttleSeatFrom = ShuttleHeadingDeg; shuttleSeatErr = ShuttleDelta(ShuttleHeadingDeg, seatTo.Value);
                shuttleSeatRate0 = shuttleRate; shuttleSeatT0 = now - dt; shuttleSeatDur = Math.Max(dt, ShuttleLift * ZWorld.ShuttleDescendSec + dt);
            }
            double u = Math.Min(1.0, (now - shuttleSeatT0) / shuttleSeatDur);
            double hd0 = ShuttleHeadingDeg;
            ShuttleHeadingDeg = (float)ShuttleNorm(shuttleSeatFrom + ShuttleQuinP(u, 0.0, shuttleSeatErr, shuttleSeatRate0, 0.0, shuttleSeatDur));
            shuttleRate = ShuttleDelta(hd0, ShuttleHeadingDeg) / dt;
        }

        /// <summary>Touchdown on the deck: the timed turn is over (its end is the deck heading).</summary>
        void ShuttleSeatDone(bool deck)
        {
            if (deck) ShuttleHeadingDeg = ZWorld.ShuttleDeckHeadingDeg;
            else if (ShuttleLandHeadingSet) ShuttleHeadingDeg = (float)ShuttleLandHeadingDeg;            // K256e: exactly the landing heading at touchdown
            ShuttleLandHeadingSet = false;
            shuttleSeatTurn = false; shuttleRate = 0;
        }

        /// <summary>Cruise: one step of the eased flight toward the goal. true = arrived this step (on the goal, stopped).</summary>
        bool ShuttleFlyStep(double dt)
        {
            if (ShuttleViaCount > 0 || shuttlePassing) return ShuttleViaStep(dt);                      // DV125: through the middle points in the air, no stop (Sim/ZWorld.ShuttleQueue.cs)
            if (shuttlePlan != 0) return ShuttlePlanStep(dt);
            double gx = ShuttleGoalX, gy = ShuttleGoalY, dx = gx - shuttleFx, dy = gy - shuttleFy, d = Math.Sqrt(dx * dx + dy * dy);
            double v0 = ShuttleSpeedPx, a = ShuttleAccelRate, vmax = RealMoveSpeed;
            if (d <= 0.05 && v0 <= a * dt) { ShuttleArrive(dt, v0); return true; }        // already there, at rest
            double bearing = Math.Atan2(dy, dx) * 180.0 / Math.PI;
            double err0 = ShuttleDelta(ShuttleHeadingDeg, bearing);
            if (d <= ZWorld.ShuttleSettlePx && (Math.Abs(err0) > 60.0 || v0 < 6.0))
            {   // settle: a planned hop straight onto the goal, slowly, without turning
                double ce = Math.Cos(err0 * Math.PI / 180.0);
                double hr = ShuttleHeadingDeg * Math.PI / 180.0, ux = d > 1e-9 ? dx / d : Math.Cos(hr), uy = d > 1e-9 ? dy / d : Math.Sin(hr);   // review S6: no NaN when it sits exactly on the goal
                ShuttleStartHop(d, Math.Max(0.0, v0 * ce), shuttleAcc * Math.Max(0.0, ce), ux, uy);
                return ShuttlePlanStep(dt);
            }
            if (Math.Abs(err0) <= 12.0 && v0 > 0 && d <= ZWorld.ShuttleBrakeDistance(v0, shuttleAcc, vmax) + v0 * dt
                && ShuttleStartBrake(d, v0, shuttleAcc, dx / d, dy / d))
                return ShuttlePlanStep(dt);                                                  // the planned stop onto the goal
            double ox = shuttleFx, oy = shuttleFy;
            double err = ShuttleTurnToward(bearing, dt);
            double c = Math.Max(0.0, Math.Cos(err * Math.PI / 180.0));
            double vt = Math.Min(Math.Min(vmax * c * c, ShuttleCurveSpeed(d, vmax)), ShuttleTrafficVmax(vmax));   // K338: held slower while it gives way in time (ZWorld.ShuttleTraffic.cs; vmax otherwise)
            double acc = ShuttleTrack(v0, shuttleAcc, vt, dt);
            double v = v0 + acc * dt;
            if (Math.Abs(v - vt) < 1e-9) v = vt;                                          // on the target speed (the last step of the S-curve)
            if (v > vmax) { v = vmax; acc = (v - v0) / dt; }
            if (v < 0) { v = 0; acc = -v0 / dt; }
            double h = ShuttleHeadingDeg * Math.PI / 180.0;
            shuttleFx += Math.Cos(h) * v * dt; shuttleFy += Math.Sin(h) * v * dt;
            double ex = gx - shuttleFx, ey = gy - shuttleFy, d1 = Math.Sqrt(ex * ex + ey * ey);
            if (d1 <= ZWorld.ShuttleArrivePx && d <= v * dt + 0.002)
            {   // the goal lies within this step's travel: on it, stopped
                shuttleFx = ox; shuttleFy = oy;
                ShuttleArrive(dt, v0); return true;
            }
            ShuttleSpeedPx = v; ShuttleAccelPx = (v - v0) / dt; shuttleAcc = acc; ShuttleBraking = false;
            ShuttleClampToMap();
            return false;
        }

        /// <summary>The S-curve: the next acceleration toward the target speed vt: at most a either way, changing at most j x dt per
        /// step, and already fading out so that it reaches vt with no acceleration left. Exact for the step: the acceleration that,
        /// falling by j x dt each step from here, covers |vt - v| (the largest m with j dt^2 m (m + 1) / 2 <= |vt - v|, the rest spread
        /// evenly over those m + 1 steps), so there is no overshoot and no last-step jolt.</summary>
        double ShuttleTrack(double v, double acc, double vt, double dt)
        {
            double a = ShuttleAccelRate, j = ShuttleJerkRate, dv = vt - v, ad = Math.Abs(dv), q = j * dt * dt;
            double m = Math.Floor((-1.0 + Math.Sqrt(1.0 + 8.0 * ad / q)) * 0.5);
            double rest = Math.Max(0.0, ad - q * m * (m + 1) * 0.5);
            double want = Math.Sign(dv) * Math.Min(a, m * j * dt + rest / ((m + 1) * dt));
            double step = j * dt;
            return acc + Math.Max(-step, Math.Min(step, want - acc));
        }

        /// <summary>The speed from which the planned stop from a steady speed needs exactly d px (at most vmax).</summary>
        static double ShuttleCurveSpeed(double d, double vmax)
        {
            if (d <= 0) return 0;
            double v = Math.Pow(2.0 * d * Math.Sqrt(Math.Max(1e-9, vmax)) / ZWorld.ShuttleBrakeFullSec, 2.0 / 3.0);
            return Math.Min(vmax, v);
        }

        /// <summary>The planned stop: from speed v0 and acceleration a0 to rest with no acceleration exactly d0 px further on, along (ux, uy).
        /// Its length T solves T x (v0 / 2 + a0 x T / 12) = d0. Refused (false) when it would reverse or brake harder than 1.5 a.</summary>
        bool ShuttleStartBrake(double d0, double v0, double a0, double ux, double uy)
        {
            if (v0 <= 1e-6) return false;
            double t;
            if (Math.Abs(a0) < 1e-9) t = 2.0 * d0 / v0;
            else
            {
                double disc = v0 * v0 * 0.25 + a0 * d0 / 3.0;
                if (disc < 0) return false;
                t = (-v0 * 0.5 + Math.Sqrt(disc)) / (a0 / 6.0);
            }
            if (!(t > 1e-3) || double.IsNaN(t) || double.IsInfinity(t)) return false;
            double lim = 1.5 * ShuttleAccelRate;
            for (int i = 0; i <= 40; i++)
            {
                double u = i / 40.0;
                if (ShuttleQuarticV(u, v0, a0, t) < -1e-6 || Math.Abs(ShuttleQuarticA(u, v0, a0, t)) > lim) return false;
            }
            if (ZWorld.ShuttleQuarticJerk(v0, a0, t) > 2.0 * ShuttleJerkRate) return false;
            shuttlePlan = 1; shuttlePlanT = t; shuttlePlanAge = 0; shuttlePlanV0 = v0; shuttlePlanA0 = a0; shuttlePlanD0 = d0;
            shuttlePlanX0 = shuttleFx; shuttlePlanY0 = shuttleFy; shuttlePlanUx = ux; shuttlePlanUy = uy;
            return true;
        }

        /// <summary>The settle hop: from speed v0 and acceleration a0 toward the goal to rest d0 px further on, along (ux, uy), eased
        /// at both ends (a quintic); at most about 8 px/s, never past the goal.</summary>
        void ShuttleStartHop(double d0, double v0, double a0, double ux, double uy)
        {
            double t = Math.Max(1.0, 1.875 * d0 / 8.0);
            if (v0 > 1e-6) t = Math.Min(t, 2.0 * d0 / v0);
            shuttlePlan = 2; shuttlePlanT = Math.Max(0.3, t); shuttlePlanAge = 0; shuttlePlanV0 = v0; shuttlePlanA0 = a0; shuttlePlanD0 = d0;
            shuttlePlanX0 = shuttleFx; shuttlePlanY0 = shuttleFy; shuttlePlanUx = ux; shuttlePlanUy = uy;
        }

        // the planned stop: speed v(u) = v0 h00(u) + a0 T h10(u), its distance, its acceleration (u = time / T)
        static double ShuttleQuarticV(double u, double v0, double a0, double t) => v0 * (1 - 3 * u * u + 2 * u * u * u) + a0 * t * (u * u * u - 2 * u * u + u);
        static double ShuttleQuarticP(double u, double v0, double a0, double t) => t * (v0 * (u - u * u * u + u * u * u * u * 0.5) + a0 * t * (u * u * u * u * 0.25 - 2.0 * u * u * u / 3.0 + u * u * 0.5));
        static double ShuttleQuarticA(double u, double v0, double a0, double t) => (v0 * (-6 * u + 6 * u * u) + a0 * t * (3 * u * u - 4 * u + 1)) / t;
        // the settle hop: p(u) = d0 S5(u) + v0 T B1(u) + a0 T^2 C1(u), C1 = u^2 (1 - u)^3 / 2
        static double ShuttleHopP(double u, double d0, double v0, double a0, double t) => d0 * (10 * u * u * u - 15 * u * u * u * u + 6 * u * u * u * u * u) + v0 * t * (u - 6 * u * u * u + 8 * u * u * u * u - 3 * u * u * u * u * u) + a0 * t * t * 0.5 * u * u * (1 - u) * (1 - u) * (1 - u);
        static double ShuttleHopV(double u, double d0, double v0, double a0, double t) => (d0 * 30 * u * u * (1 - u) * (1 - u) + v0 * t * (1 - 18 * u * u + 32 * u * u * u - 15 * u * u * u * u) + a0 * t * t * 0.5 * (2 * u * (1 - u) * (1 - u) * (1 - u) - 3 * u * u * (1 - u) * (1 - u))) / t;
        static double ShuttleHopA(double u, double d0, double v0, double a0, double t) => (d0 * (60 * u - 180 * u * u + 120 * u * u * u) + v0 * t * (-36 * u + 96 * u * u - 60 * u * u * u) + a0 * t * t * 0.5 * (2 * (1 - u) * (1 - u) * (1 - u) - 12 * u * (1 - u) * (1 - u) + 6 * u * u * (1 - u))) / (t * t);

        /// <summary>One step along the plan; true = on the goal, stopped (the plan is over).</summary>
        bool ShuttlePlanStep(double dt)
        {
            double v0 = ShuttleSpeedPx;
            shuttlePlanAge += dt;
            double u = Math.Min(1.0, shuttlePlanAge / shuttlePlanT), p, v, acc;
            if (shuttlePlan == 1) { p = ShuttleQuarticP(u, shuttlePlanV0, shuttlePlanA0, shuttlePlanT); v = ShuttleQuarticV(u, shuttlePlanV0, shuttlePlanA0, shuttlePlanT); acc = ShuttleQuarticA(u, shuttlePlanV0, shuttlePlanA0, shuttlePlanT); }
            else { p = ShuttleHopP(u, shuttlePlanD0, shuttlePlanV0, shuttlePlanA0, shuttlePlanT); v = ShuttleHopV(u, shuttlePlanD0, shuttlePlanV0, shuttlePlanA0, shuttlePlanT); acc = ShuttleHopA(u, shuttlePlanD0, shuttlePlanV0, shuttlePlanA0, shuttlePlanT); }
            if (shuttlePlan == 1)
            {   // still lined up: the facing keeps turning onto the bearing (a few degrees at most)
                double bx = ShuttleGoalX - shuttleFx, by = ShuttleGoalY - shuttleFy;
                if (bx * bx + by * by > 0.25) ShuttleTurnToward(Math.Atan2(by, bx) * 180.0 / Math.PI, dt); else ShuttleNoTurn(dt);
            }
            else ShuttleNoTurn(dt);
            if (u >= 1.0)
            {
                shuttlePlan = 0; ShuttleBraking = false;
                ShuttleArrive(dt, v0); return true;
            }
            shuttleFx = shuttlePlanX0 + shuttlePlanUx * p; shuttleFy = shuttlePlanY0 + shuttlePlanUy * p;
            ShuttleSpeedPx = Math.Max(0.0, v); ShuttleAccelPx = (ShuttleSpeedPx - v0) / dt; shuttleAcc = acc; ShuttleBraking = shuttlePlan == 1;
            ShuttleClampToMap();
            return false;
        }

        /// <summary>A new goal (or a new state): any plan is called off; the speed and the acceleration go on from where they are.</summary>
        void ShuttlePlanOff() { shuttlePlan = 0; ShuttleBraking = false; }

        void ShuttleArrive(double dt, double v0)
        {
            shuttleFx = ShuttleGoalX; shuttleFy = ShuttleGoalY;
            ShuttleSpeedPx = 0; ShuttleAccelPx = -v0 / dt; shuttleRate = 0; shuttleAcc = 0; shuttlePlan = 0; ShuttleBraking = false;
        }

        /// <summary>Not flying this step (landed, climbing, hovering, coming down): no speed.</summary>
        void ShuttleHold(double dt)
        {
            double v0 = ShuttleSpeedPx;
            ShuttleSpeedPx = 0; ShuttleAccelPx = v0 > 0 ? -v0 / dt : 0; shuttleAcc = 0; shuttlePlan = 0; ShuttleBraking = false;
        }

        /// <summary>The middle stays one tile (plus half its box) in from every map edge.</summary>
        void ShuttleClampToMap()
        {
            var m = World?.Map;
            if (m == null) return;
            double lo = 16 + WidthPix * 0.5, hx = m.WidthPix - 16 - WidthPix * 0.5, hy = m.HeightPix - 16 - HeightPix * 0.5;
            shuttleFx = Math.Max(lo, Math.Min(hx, shuttleFx)); shuttleFy = Math.Max(lo, Math.Min(hy, shuttleFy));
        }

        /// <summary>STOP while flying: where the planned stop from here would end, along the facing (px).</summary>
        internal (int x, int y) ShuttleBrakeEnd()
        {
            double s = ZWorld.ShuttleBrakeDistance(ShuttleSpeedPx, shuttleAcc, RealMoveSpeed), h = ShuttleHeadingDeg * Math.PI / 180.0;
            return ((int)Math.Round(shuttleFx + Math.Cos(h) * s), (int)Math.Round(shuttleFy + Math.Sin(h) * s));
        }
    }
}
