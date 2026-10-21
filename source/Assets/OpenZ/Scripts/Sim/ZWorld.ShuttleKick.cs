using System;

namespace OpenZ.Sim
{
    /// <summary>
    /// DV129, the shuttle's kickflip becomes a rule (on top of DV118; the view's flip: View/OzShuttleKickflip.Core.cs and
    /// View/OzPlayableShuttle.Kickflip.cs). Deterministic: the rules' own clock, the ship's state and hashed rolls only (no random draw is
    /// added to the world's generator, no frame time).
    ///  * THE FLIP WINDOW: <see cref="ZWorld.ShuttleKickSec"/> s (the view's curve, 2.5 s). It is opened by the player's double click
    ///    (the view asks <see cref="ZWorld.ShuttleKickOrder"/> on the frame its flip pops) or by the ship itself (the evasive self-flip,
    ///    below). Never two at once: while a window (or its speed's way out) runs, every other start is refused.
    ///  * THE DASH (the spiral): the ship speeds up along its facing through the window, smoothly, and slows back to its cruise:
    ///    extra speed = (<see cref="ZWorld.ShuttleKickPeak"/> - 1) x its speed at the start x B(t / T), B = 64 u^3 (1 - u)^3 (0 at both
    ///    ends with its rate and its acceleration: no step; its peak, the top speed x ShuttleKickPeak, at the middle of the roll's fast
    ///    part). It arrives sooner by that extra way (about 1.6 s of cruise). The extra speed is added to the flight model's own speed
    ///    (the S-curve, the turns and the planned stop stay as they were); the start test asks for the whole flip, its extra way, the
    ///    planned stop and a margin before the goal or the next middle point of a route.
    ///  * THE WAY OUT: a new order (a goal more than <see cref="ZWorld.ShuttleKickSameGoalPx"/> px from the flip's): the extra speed eases
    ///    to 0 over <see cref="ZWorld.ShuttleKickEaseSec"/> s (a quintic from its value, rate and acceleration), and the flight model turns
    ///    onto the new course as always; a new order in the pop (before <see cref="ZWorld.ShuttleKickPopSec"/>, well before the roll) also closes the window (the
    ///    view eases its pop back). A new order to a near point ahead can start the planned stop while the extra speed still runs: the
    ///    extra speed then eases out over a short quintic (at most <see cref="ZWorld.ShuttleKickTieMaxSec"/> s, at least
    ///    <see cref="ZWorld.ShuttleKickTieMinSec"/> s) along the stop's line and the stop is planned that much shorter, so the two together
    ///    end exactly on the goal (no step in the speed); a goal too near for the flight model's stop gets its settle hop instead (the
    ///    same eased way onto the goal, the extra speed's way out over the shortest length). Only when the stop's line is not the facing
    ///    (the ship already past the goal) or the cruise ends any other way (a destroyed ship, a hand placement, the descent) is the extra
    ///    speed dropped at once (counted).
    ///  * EVASION: while the window is open the ship is a harder target: a gun's hit chance at it x <see cref="ZWorld.ShuttleKickHitMul"/>
    ///    (the same single roll of the world's generator, so the draws stay the same); a rocket or shell fired at it then reaches it with
    ///    that share (a hashed roll per shot: the others are aimed past its hull and burst behind it), and a blast already in flight that
    ///    would reach it does so with that share (a hashed roll). Hits sound and count as before.
    ///  * THE EVASIVE SELF-FLIP (instead of the view's 5 % flip by itself): on a long flight (at least
    ///    <see cref="ZWorld.ShuttleKickSelfMinRemainM"/> m left to the goal or the next middle point, at least
    ///    <see cref="ZWorld.ShuttleKickSelfFlownM"/> m flown since the take-off: never near a take-off or a landing), in the steady cruise
    ///    at cruise height, when enemy fire is aimed at it (a rocket or shell in flight at it, a gun's roll at it or a hit within
    ///    <see cref="ZWorld.ShuttleKickShotHoldSec"/> s) without a pause for its reaction time (0.5 .. 1.0 s, hashed per flip), and at
    ///    least <see cref="ZWorld.ShuttleKickCooldownSec"/> s after the end of its last flip: it flips by itself.
    /// Switch: <see cref="ZWorld.ShuttleKickDeviation"/> (on; off = DV118 as it was: the flip is a picture only). Matters only while DV118
    /// makes shuttles.
    /// </summary>
    public partial class ZObject
    {
        /// <summary>DV129: seconds into the flip's window (-1 = none), whether that flip is the ship's own (evasive), flips started.</summary>
        public double ShuttleKickT { get; private set; } = -1;
        public bool ShuttleKickSelf { get; private set; }
        public int ShuttleKickSeq { get; private set; }
        /// <summary>DV129: the extra speed now (px/s), the extra way of this flip so far (px), the speed at its start (px/s).</summary>
        public double ShuttleKickBoostPx { get; private set; }
        public double ShuttleKickExtraPx { get; private set; }
        public double ShuttleKickV0Px { get; private set; }
        /// <summary>DV129 counts: flips started (all, the ship's own), starts refused, ways out after a new order, pops closed by a new order,
        /// extra speed dropped at once.</summary>
        public int ShuttleKickStarts, ShuttleKickSelfStarts, ShuttleKickRefused, ShuttleKickEaseOuts, ShuttleKickPopCloses, ShuttleKickCuts;
        /// <summary>DV129 counts: planned stops that took over the extra speed's way out (the extra speed eased out along the stop's line), of
        /// them settle hops (the goal too near for the stop).</summary>
        public int ShuttleKickTies, ShuttleKickTieHops;
        /// <summary>DV129: why the last start test said no ("" = it could start).</summary>
        public string ShuttleKickWhy { get; private set; } = "";
        /// <summary>DV129: the world time of the last gun roll at this ship (hit or miss).</summary>
        public double ShuttleShotAt = -1e9;

        int kickMode;                                             // 0 = no extra speed, 1 = on the dash's curve, 2 = easing out
        bool kickTied;                                            // the way out runs under a planned stop (along its line, the stop planned that much shorter)
        double kickA, kickGoalX, kickGoalY, kickEaseT, kickEaseDur, kickE0, kickE1, kickE2, kickCoolUntil = -1e9, kickFireSince = -1, kickFlownPx;

        /// <summary>DV129: the window is open now (a harder target; the view's flip is on).</summary>
        public bool ShuttleKickEvading => ZWorld.ShuttleKickDeviation && ShuttleKickT >= 0 && ShuttleKickT < ZWorld.ShuttleKickSec && !Destroyed;
        /// <summary>DV129: a flip or its speed's way out runs: no other flip may start.</summary>
        public bool ShuttleKickBusy => ShuttleKickT >= 0 || kickMode != 0;
        /// <summary>DV129: under enemy fire now, and since when (world time; -1 = not under fire).</summary>
        public bool ShuttleKickUnderFire => kickFireSince >= 0;
        public double ShuttleKickFireSince => kickFireSince;
        /// <summary>DV129: metres flown in this flight, the earliest time of the next self-flip.</summary>
        public double ShuttleKickFlownM => kickFlownPx / 8.0;
        public double ShuttleKickCoolUntil => kickCoolUntil;

        // the dash's curve: B(u) = 64 u^3 (1 - u)^3, its rates per u, its integral from 0
        internal static double KickB(double u) { if (u <= 0 || u >= 1) return 0; double v = 1 - u; return 64.0 * u * u * u * v * v * v; }
        internal static double KickB1(double u) { if (u <= 0 || u >= 1) return 0; double v = 1 - u; return 192.0 * u * u * v * v * (1 - 2 * u); }
        internal static double KickB2(double u) { if (u <= 0 || u >= 1) return 0; return 384.0 * u * (1 - u) * (1 - 5 * u + 5 * u * u); }
        internal static double KickBInt(double u)
        {
            u = u < 0 ? 0 : u > 1 ? 1 : u;
            double u4 = u * u * u * u;
            return 64.0 * u4 * (0.25 + u * (-0.6 + u * (0.5 - u / 7.0)));
        }

        /// <summary>DV129: the extra way of a whole flip from speed v (px).</summary>
        public static double ShuttleKickExtraFor(double v) => (ZWorld.ShuttleKickPeak - 1.0) * v * ZWorld.ShuttleKickSec * KickBInt(1.0);

        /// <summary>DV129: the way left (px) the start test reads: to the next middle point of a route, else to the goal.</summary>
        public double ShuttleKickWayLeftPx
        {
            get
            {
                double sx = ShuttleSteerX - shuttleFx, sy = ShuttleSteerY - shuttleFy;
                return Math.Sqrt(sx * sx + sy * sy);
            }
        }

        /// <summary>DV129: the way a flip needs from here (px): its cruise way and margin, its extra way, the planned stop, a margin, all
        /// counted at the top speed (a ship still speeding up gets there during the flip).</summary>
        public double ShuttleKickNeedPx
        {
            get
            {
                double v = Math.Max(ShuttleSpeedPx, RealMoveSpeed);
                return v * (ZWorld.ShuttleKickSec + ZWorld.ShuttleKickMarginSec) + ShuttleKickExtraFor(v)
                       + ZWorld.ShuttleBrakeDistance(v, 0.0, RealMoveSpeed) + ZWorld.ShuttleKickSafePx;
            }
        }

        /// <summary>DV129: may a flip start now (the double click's and the ship's own)? why = the first test that fails.</summary>
        public bool ShuttleKickCan(out string why)
        {
            why = "";
            if (!ZWorld.ShuttleKickDeviation) why = "the rule is off";
            else if (!IsShuttle || Destroyed || KillAt >= 0 || World == null) why = "gone";
            else if (ShuttleKickBusy) why = "a flip is on";
            else if (ShuttleState != ZShuttleState.Cruise || ShuttleLift < 1.0 || ShuttleDeckTurning || ShuttleLandTurning) why = "not in the cruise";
            else if (shuttlePlan != 0 || ShuttleBraking) why = "braking";
            else if (ShuttleSpeedPx < ZWorld.ShuttleKickMinSpeed * Math.Max(1.0, RealMoveSpeed)) why = "slow";
            else if (ShuttleKickWayLeftPx < ShuttleKickNeedPx) why = "too little way left";
            ShuttleKickWhy = why;
            return why.Length == 0;
        }

        void ShuttleKickBegin(bool self)
        {
            ShuttleKickV0Px = ShuttleSpeedPx; kickA = (ZWorld.ShuttleKickPeak - 1.0) * ShuttleKickV0Px; kickMode = 1;
            ShuttleKickT = 0; ShuttleKickSelf = self; ShuttleKickSeq++; ShuttleKickStarts++; if (self) ShuttleKickSelfStarts++;
            kickGoalX = ShuttleGoalX; kickGoalY = ShuttleGoalY; ShuttleKickExtraPx = 0; ShuttleKickBoostPx = 0;
        }

        /// <summary>The flip's window closed (its end, a pop closed by a new order, a reset): the cooldown of the self-flip starts.</summary>
        void ShuttleKickClose() { ShuttleKickT = -1; kickCoolUntil = World != null ? World.Time + ZWorld.ShuttleKickCooldownSec : -1e9; }

        void ShuttleKickEase(double u)
        {
            double T = ZWorld.ShuttleKickSec;
            if (kickMode == 1) { kickE0 = kickA * KickB(u); kickE1 = kickA * KickB1(u) / T; kickE2 = kickA * KickB2(u) / (T * T); }
            else { kickE0 = ShuttleKickBoostPx; kickE1 = 0; kickE2 = 0; }
            kickEaseT = 0; kickEaseDur = ZWorld.ShuttleKickEaseSec; kickMode = 2; ShuttleKickEaseOuts++;
        }

        /// <summary>The reaction time of the self-flip number n (s): hashed from the ship and n.</summary>
        double ShuttleKickReactSec(int n)
        {
            unchecked
            {
                ulong z = ((ulong)(uint)Id << 32) ^ (uint)n ^ 0x6B1D_4E57ul;
                z += 0x9E3779B97F4A7C15ul; z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9ul; z = (z ^ (z >> 27)) * 0x94D049BB133111EBul; z ^= z >> 31;
                double r = (z >> 11) * (1.0 / 9007199254740992.0);
                return ZWorld.ShuttleKickReactMinSec + (ZWorld.ShuttleKickReactMaxSec - ZWorld.ShuttleKickReactMinSec) * r;
            }
        }

        /// <summary>ZObject.ShuttleStep, after the flight and the threat watch, before the strafe and the place: the fire watch, the
        /// flight's way so far, the running flip's extra speed (its way added along the facing), the self-flip.</summary>
        void ShuttleKickStep(double dt)
        {
            var w = World;
            if (w == null || dt <= 0) return;
            if (!ZWorld.ShuttleKickDeviation) { if (ShuttleKickBusy) { kickMode = 0; kickTied = false; ShuttleKickBoostPx = 0; ShuttleKickT = -1; } return; }
            double now = w.Time;
            bool fire = ShuttleAirborne && (ShuttleUnderHeavyFire || now - ShuttleShotAt < ZWorld.ShuttleKickShotHoldSec || now - LastEnemyHit < ZWorld.ShuttleKickShotHoldSec);
            if (fire) { if (kickFireSince < 0) kickFireSince = now; } else kickFireSince = -1;
            if (ShuttleState == ZShuttleState.Landed || ShuttleState == ZShuttleState.Climb) kickFlownPx = 0;
            else kickFlownPx += (ShuttleSpeedPx + ShuttleKickBoostPx) * dt;
            if (ShuttleState == ZShuttleState.Landed && ShuttleKickBusy) { kickMode = 0; kickTied = false; ShuttleKickBoostPx = 0; ShuttleKickClose(); return; }
            if (ShuttleKickBusy) { ShuttleKickAdvance(dt); return; }
            if (!ShuttleKickSelfDue(now)) return;
            ShuttleKickBegin(true);
        }

        /// <summary>The self-flip's tests (see the class summary), the start test first.</summary>
        bool ShuttleKickSelfDue(double now)
        {
            if (kickFireSince < 0 || now < kickCoolUntil) return false;
            double vmax = Math.Max(1.0, RealMoveSpeed);
            if (ShuttleSpeedPx < ZWorld.ShuttleKickSelfSpeed * vmax || Math.Abs(ShuttleAccelPx) > ZWorld.ShuttleKickSelfAccel * ZWorld.ShuttleAccelMax(vmax)
                || Math.Abs(ShuttleTurnRateDeg) > ZWorld.ShuttleKickSelfTurnDeg) return false;
            if (kickFlownPx < ZWorld.ShuttleKickSelfFlownM * 8.0 || ShuttleKickWayLeftPx < ZWorld.ShuttleKickSelfMinRemainM * 8.0) return false;
            if (now - kickFireSince < ShuttleKickReactSec(ShuttleKickSelfStarts + 1) - 1e-9) return false;
            return ShuttleKickCan(out _);
        }

        void ShuttleKickAdvance(double dt)
        {
            double T = ZWorld.ShuttleKickSec;
            bool cruise = ShuttleState == ZShuttleState.Cruise && ShuttleLift >= 1.0 && !ShuttleDeckTurning && !ShuttleLandTurning && shuttlePlan == 0;
            if (ShuttleKickT >= 0) ShuttleKickT += dt;
            double b = 0;
            bool flying = ShuttleState == ZShuttleState.Cruise && ShuttleLift >= 1.0 && !ShuttleDeckTurning && !ShuttleLandTurning;
            if (kickTied && (shuttlePlan == 0 || !flying)) kickTied = false;                 // the stop was called off (a new order: the free cruise again) or ended
            if (kickMode != 0 && !cruise && !kickTied && !(flying && shuttlePlan != 0 && ShuttleKickTie()))
            {   // the cruise ended under it another way (the ship past the goal, a hand placement, the descent): the extra speed goes at once
                kickMode = 0; kickTied = false; ShuttleKickCuts++;
            }
            if (kickMode == 1)
            {
                double u = Math.Min(1.0, Math.Max(0.0, ShuttleKickT) / T);
                double gx = ShuttleGoalX - kickGoalX, gy = ShuttleGoalY - kickGoalY;
                bool newGoal = gx * gx + gy * gy > ZWorld.ShuttleKickSameGoalPx * ZWorld.ShuttleKickSameGoalPx;
                double v = ShuttleSpeedPx, left = ShuttleKickWayLeftPx - kickA * T * (KickBInt(1.0) - KickBInt(u)) - v * T * (1.0 - u)
                                                  - ZWorld.ShuttleBrakeDistance(v, shuttleAcc, RealMoveSpeed);
                if (newGoal || left < ZWorld.ShuttleKickSafePx)
                {
                    if (newGoal && ShuttleKickT >= 0 && ShuttleKickT < ZWorld.ShuttleKickPopSec) { ShuttleKickPopCloses++; ShuttleKickClose(); }   // a new order in the pop (well before the roll, so the view still eases back): no roll, no harder target
                    kickGoalX = ShuttleGoalX; kickGoalY = ShuttleGoalY;
                    ShuttleKickEase(u);
                }
                else if (u >= 1.0) kickMode = 0;
                else b = kickA * KickB(u);
            }
            if (kickMode == 2)
            {
                kickEaseT += dt;
                double s = Math.Min(1.0, kickEaseT / kickEaseDur);
                b = ShuttleQuinP(s, kickE0, 0.0, kickE1, kickE2, kickEaseDur);
                if (b < 0 && !kickTied) b = 0;
                if (s >= 1.0) { kickMode = 0; b = 0; kickTied = false; }
            }
            if (b != 0 && kickTied)
            {   // under the planned stop: along its line, the stop's start point moved with it (the stop was planned this much shorter)
                double mx = shuttlePlanUx * b * dt, my = shuttlePlanUy * b * dt;
                shuttleFx += mx; shuttleFy += my; shuttlePlanX0 += mx; shuttlePlanY0 += my;
                ShuttleKickExtraPx += b * dt;
                ShuttleClampToMap();
            }
            else if (b > 0 && cruise)
            {
                double h = ShuttleHeadingDeg * Math.PI / 180.0;
                shuttleFx += Math.Cos(h) * b * dt; shuttleFy += Math.Sin(h) * b * dt;
                ShuttleKickExtraPx += b * dt;
                ShuttleClampToMap();
            }
            ShuttleKickBoostPx = b;
            if (ShuttleKickT >= T) ShuttleKickClose();
        }

        /// <summary>The planned stop began while the extra speed runs (a new order to a near point ahead): the extra speed eases out from
        /// its value, rate and acceleration over tau s along the stop's line, and the stop is planned again from here that much shorter
        /// (its way left minus the way-out's extra way), so both end on the goal together. tau: the longest from ShuttleKickTieMaxSec down
        /// to ShuttleKickTieMinSec whose shorter stop the flight model accepts (its own limits) and that ends well before that stop does;
        /// none: the flight model's settle hop onto the goal (no limit of its own) with the shortest tau. false = the stop's line is not
        /// the facing (the extra speed runs along the facing: the ship is already past the goal) or nothing fits (it is dropped at once).</summary>
        bool ShuttleKickTie()
        {
            double e0, e1, e2, T = ZWorld.ShuttleKickSec;
            if (kickMode == 1) { double u = Math.Min(1.0, Math.Max(0.0, ShuttleKickT) / T); e0 = kickA * KickB(u); e1 = kickA * KickB1(u) / T; e2 = kickA * KickB2(u) / (T * T); }
            else
            {
                double s = Math.Min(1.0, kickEaseT / kickEaseDur), d = kickEaseDur;
                e0 = ShuttleQuinP(s, kickE0, 0.0, kickE1, kickE2, d); e1 = ShuttleHopV(s, -kickE0, kickE1, kickE2, d); e2 = ShuttleHopA(s, -kickE0, kickE1, kickE2, d);
            }
            double h = ShuttleHeadingDeg * Math.PI / 180.0;
            if (Math.Cos(h) * shuttlePlanUx + Math.Sin(h) * shuttlePlanUy < Math.Cos(ZWorld.ShuttleKickTieDeg * Math.PI / 180.0)) return false;   // the stop's line is not the facing
            double age = Math.Min(shuttlePlanAge, shuttlePlanT), uu = age / shuttlePlanT;
            double left = shuttlePlanD0 - (shuttlePlan == 1 ? ShuttleQuarticP(uu, shuttlePlanV0, shuttlePlanA0, shuttlePlanT) : ShuttleHopP(uu, shuttlePlanD0, shuttlePlanV0, shuttlePlanA0, shuttlePlanT));
            double v = ShuttleSpeedPx, a = shuttleAcc;
            for (int k = 0; ; k++)
            {
                double tau = ZWorld.ShuttleKickTieMaxSec - 0.05 * k;
                if (tau < ZWorld.ShuttleKickTieMinSec - 1e-9) break;
                double extra = e0 * tau * 0.5 + e1 * tau * tau * 0.1 + e2 * tau * tau * tau / 120.0, d0 = left - extra;
                if (d0 < 1.0) continue;
                double tp = Math.Abs(a) < 1e-9 ? 2.0 * d0 / Math.Max(1e-6, v) : (-v * 0.5 + Math.Sqrt(Math.Max(0.0, v * v * 0.25 + a * d0 / 3.0))) / (a / 6.0);
                if (!(tp > 0) || tau > 0.85 * tp) continue;                                    // the way out ends well before the stop does
                if (!ShuttleStartBrake(d0, v, a, shuttlePlanUx, shuttlePlanUy)) continue;
                kickE0 = e0; kickE1 = e1; kickE2 = e2; kickEaseT = 0; kickEaseDur = tau; kickMode = 2; kickTied = true; ShuttleKickTies++;
                return true;
            }
            {   // too near for the stop: the settle hop onto the goal (an eased quintic, never past it), the shortest way out
                double tau = ZWorld.ShuttleKickTieMinSec, extra = e0 * tau * 0.5 + e1 * tau * tau * 0.1 + e2 * tau * tau * tau / 120.0, d0 = left - extra;
                if (d0 < 1.0) return false;
                double th = Math.Max(1.0, 1.875 * d0 / 8.0); if (v > 1e-6) th = Math.Min(th, 2.0 * d0 / v); th = Math.Max(0.3, th);
                if (tau > 0.85 * th) return false;
                ShuttleStartHop(d0, Math.Max(0.0, v), a, shuttlePlanUx, shuttlePlanUy);
                kickE0 = e0; kickE1 = e1; kickE2 = e2; kickEaseT = 0; kickEaseDur = tau; kickMode = 2; kickTied = true; ShuttleKickTies++; ShuttleKickTieHops++;
                return true;
            }
        }

        /// <summary>CoverMiss hook (every gun's roll at t, the world's one draw `roll`): the chance against a flipping shuttle. Notes the
        /// shot for the fire watch. Any other target: the chance unchanged (exactly).</summary>
        internal double ShuttleKickHitChance(ZObject t, double roll, double chance)
        {
            if (t == null || !t.IsShuttle || World == null) return chance;
            t.ShuttleShotAt = World.Time;
            if (!t.ShuttleKickEvading || !t.ShuttleAirborne) return chance;
            double c = chance * ZWorld.ShuttleKickHitMul;
            ZWorld.ShuttleKickShots++;
            if (roll <= c) ZWorld.ShuttleKickShotHits++; else if (roll <= chance) ZWorld.ShuttleKickShotDodged++;
            return c;
        }

        /// <summary>ProcessAttackDamage hook (a rocket or shell fired at t, after the game's lead and scatter): at a flipping shuttle the shot
        /// reaches it with ShuttleKickHitMul (a hashed roll); the others are aimed past the hull along the line of fire (the blast's radius
        /// + 4 m beyond its aim point) and burst behind it, harmless to it.</summary>
        internal void ShuttleKickAimPast(ZObject t, ZMissile m, ref int tx, ref int ty)
        {
            if (t == null || m == null || !t.IsShuttle || !t.ShuttleAirborne || !t.ShuttleKickEvading || World == null) return;
            ZWorld.ShuttleKickRockets++;
            if (World.ShuttleKickRoll(Id, t.Id, 1) < ZWorld.ShuttleKickHitMul) { m.kick = 1; return; }
            m.kick = -1; ZWorld.ShuttleKickRocketsPast++;
            double dx = t.CenterX - CenterX, dy = t.CenterY - CenterY, d = Math.Sqrt(dx * dx + dy * dy);
            if (d < 1) { double h = t.ShuttleHeadingDeg * Math.PI / 180.0; dx = Math.Cos(h); dy = Math.Sin(h); d = 1; }
            double push = Math.Max(0, DamageRadius) + ZWorld.ShuttleKickPastPx;
            tx += (int)Math.Round(dx / d * push); ty += (int)Math.Round(dy / d * push);
            var map = World.Map;
            if (map != null) { tx = Math.Max(0, Math.Min(map.WidthPix - 1, tx)); ty = Math.Max(0, Math.Min(map.HeightPix - 1, ty)); }
        }
    }

    public sealed partial class ZWorld
    {
        /// <summary>DV129 switch (on; off = DV118 as it was: the flip is a picture only). Matters only while DV118 makes shuttles.</summary>
        public static bool ShuttleKickDeviation = true;
        /// <summary>DV129: the flip's window (s, the view's curve), the roll's start in it (s), the dash's peak (x the speed at the start),
        /// a gun's hit chance and a rocket's share at a flipping ship, the extra speed's way out (s).</summary>
        public const double ShuttleKickSec = 2.5, ShuttleKickRollSec = 0.32, ShuttleKickPopSec = 0.25;
        public static double ShuttleKickPeak = 2.4, ShuttleKickHitMul = 0.4, ShuttleKickEaseSec = 1.0;
        /// <summary>DV129: the extra speed's way out under a planned stop that began while it ran (a new order to a near point ahead): its
        /// longest and shortest length (s); the most the stop's line may differ from the facing (deg).</summary>
        public static double ShuttleKickTieMaxSec = 0.8, ShuttleKickTieMinSec = 0.25, ShuttleKickTieDeg = 20.0;
        /// <summary>DV129 start test: the least speed (x the top speed), the cruise margin after the flip (s), the spare way (px), the
        /// distance within which a new goal is the same order (px), how far past its aim a dodged rocket bursts (px, + its radius).</summary>
        public static double ShuttleKickMinSpeed = 0.6, ShuttleKickMarginSec = 0.35, ShuttleKickSafePx = 16.0, ShuttleKickSameGoalPx = 48.0, ShuttleKickPastPx = 32.0;
        /// <summary>DV129 the evasive self-flip: the least way left (m: never near a landing) and flown since the take-off (m), the steady
        /// cruise it needs (speed share, acceleration share, turn rate deg/s), the fire watch's hold after a gun's roll or a hit (s), the
        /// reaction time (s, hashed per flip), the pause after any flip (s).</summary>
        public static double ShuttleKickSelfMinRemainM = 45.0, ShuttleKickSelfFlownM = 12.0, ShuttleKickSelfSpeed = 0.8, ShuttleKickSelfAccel = 0.15, ShuttleKickSelfTurnDeg = 8.0;
        public static double ShuttleKickShotHoldSec = 1.0, ShuttleKickReactMinSec = 0.5, ShuttleKickReactMaxSec = 1.0, ShuttleKickCooldownSec = 10.0;
        /// <summary>DV129 counts (all shuttles): gun rolls at a flipping ship, of them hits, misses the flip made (they would have hit);
        /// rockets / shells fired at a flipping ship, of them aimed past it; blasts in flight that reached a flipping ship's radius, of them
        /// dodged; flips the player asked for, refused.</summary>
        public static int ShuttleKickShots, ShuttleKickShotHits, ShuttleKickShotDodged, ShuttleKickRockets, ShuttleKickRocketsPast, ShuttleKickBlastRolls, ShuttleKickBlastDodged, ShuttleKickOrders, ShuttleKickOrdersRefused;

        /// <summary>DV129: the player's double click flip pops now (View/OzPlayableShuttle.KickEvade.cs, the frame its flip starts): the
        /// window opens when the start test allows it (a human team's own ship, no flip on, the steady cruise, the way). true = opened.</summary>
        public bool ShuttleKickOrder(Team team, ZObject s)
        {
            if (!ShuttleKickDeviation || s == null || !s.IsShuttle) return false;
            ShuttleKickOrders++;
            if (!IsHumanTeam(team) || s.Owner != team || !s.ShuttleKickCan(out _)) { ShuttleKickOrdersRefused++; s.ShuttleKickRefused++; return false; }
            s.ShuttleKickBeginOrder();
            return true;
        }

        /// <summary>DV129: a hashed roll 0 .. 1 for a shot of attacker a at target b in this tick (never the world's generator).</summary>
        internal double ShuttleKickRoll(int a, int b, int salt)
        {
            unchecked
            {
                ulong z = ((ulong)(uint)a << 32) ^ (uint)b ^ ((ulong)TickIndex * 0xD1B54A32D192ED03ul) ^ ((ulong)(uint)salt << 48) ^ 0x27D4EB2Ful;
                z += 0x9E3779B97F4A7C15ul; z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9ul; z = (z ^ (z >> 27)) * 0x94D049BB133111EBul; z ^= z >> 31;
                return (z >> 11) * (1.0 / 9007199254740992.0);
            }
        }

        /// <summary>ShuttleAirBlast hook (a blast aimed at the flying shuttle o): true = it leaves o alone. A shot aimed past it (decided when
        /// it was fired) never hurts it; a blast fired before the flip that would reach it while it flips does so with ShuttleKickHitMul.</summary>
        bool ShuttleKickBlastLeaves(ZMissile m, ZObject o)
        {
            if (m == null || o == null) return false;
            if (m.kick < 0) return true;
            if (m.kick > 0 || !ShuttleKickDeviation || !o.ShuttleKickEvading) return false;
            int dx = m.x - o.CenterX, dy = m.y - o.CenterY;
            if (Math.Abs(dx) > m.radius || Math.Abs(dy) > m.radius || Math.Sqrt((double)dx * dx + (double)dy * dy) >= m.radius) return false;   // out of its reach: no roll
            ShuttleKickBlastRolls++;
            bool hit = ShuttleKickRoll(m.attackerId, o.Id, 2) < ShuttleKickHitMul;
            m.kick = (sbyte)(hit ? 1 : -1);
            if (!hit) ShuttleKickBlastDodged++;
            return !hit;
        }
    }

    public partial class ZObject
    {
        internal void ShuttleKickBeginOrder() => ShuttleKickBegin(false);
    }
}
