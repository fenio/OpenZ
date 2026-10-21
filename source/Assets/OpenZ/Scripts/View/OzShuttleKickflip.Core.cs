using System;

namespace OpenZ
{
    /// <summary>
    /// K344, the shuttle's kickflip (an easter egg, view only): the plain part, shared by the game (View/OzPlayableShuttle.Kickflip.cs),
    /// the counted check (Editor/OzShuttleKickflipCount.cs) and the kit's headless runner. No engine type, no random draw, no clock of its
    /// own (the caller hands in the seconds), nothing allocated per frame. The rules never know about it.
    /// <list type="bullet">
    /// <item>THE GESTURE (<see cref="OzKickflipGesture"/>): two plain order clicks (the right button that gives a move) within
    /// <see cref="OzKickflipGesture.DoubleSec"/> s of real time, within <see cref="OzKickflipGesture.DoublePx"/> px (x the screen's UI
    /// scale) of each other on screen and <see cref="OzKickflipGesture.DoubleM"/> m on the ground; neither a Shift queue click nor a
    /// nearest-only (Z) order. Each shuttle that was in BOTH clicks' selections is told "double"; every other one "single". A third
    /// click starts over (never a second double).</item>
    /// <item>FAR: the ship's own line point to the goal the order gave it at least <see cref="OzKickflipState.FarM"/> m (about 8 s of
    /// cruise at its top speed); nearer = nothing at all.</item>
    /// <item>THE CURVE (<see cref="OzKickflipCurve"/>, 2.5 s; round 2: longer and bigger than the first round's 1.45 s in place): a pop
    /// (the nose up 12 degrees, peak at 0.45 s, gone by 1.2 s; a hop of 0.8 m up and down over the whole flip), then ONE full roll round
    /// the long axis from 0.32 s to 2.5 s whose rate rises quickly and eases out slowly (shape 20u^3 - 45u^4 + 36u^5 - 10u^6: the fastest
    /// turn at 40 %, rate AND acceleration 0 at both ends, so the angle, its rate and its acceleration never step). With the roll the
    /// hull's middle swings round its line like a barrel roll (<see cref="OzKickflipCurve.Arc"/>: out 3.2 m to the side of the rising
    /// pod, 4.8 m up as it turns over the top upside down, across 3.2 m to the other side, home as it levels), all while the ship flies on
    /// along its way at its cruise speed. The main jets flare at the pop and at the catch.</item>
    /// <item>THE STATE (<see cref="OzKickflipState"/>): armed by a far double click; it starts only in the steady cruise (at cruise
    /// height, at least 60 % of its top speed with almost no acceleration, no turn, no strafe, no fire at it, not braking, the lean, bank
    /// and nose settled for 0.3 s, enough way left to finish before the planned stop, the ship on screen); ONE flip per order. A
    /// strafe, a hit, fire at it, a turn or a new order (a goal more than 24 m away in the pop) before the roll has begun: the pop eases back to level (0.6 s); once the roll
    /// has begun it rolls on to the end. The cruise ending under it (the descent, a hover turn, the height lost): it eases to the nearer
    /// level (back to 0 before a quarter turn, on to 360 after) in 0.7 s. Every way out is a quintic from the angle, rate and
    /// acceleration it has: nothing ever snaps.</item>
    /// </list>
    /// </summary>
    public static class OzKickflipCurve
    {
        /// <summary>The flip's length (s), the roll's start (s), the pop's length (s), its nose-up peak (deg), the hop's peak (m).</summary>
        public const double TotalSec = 2.5, RollStartSec = 0.32, PitchSec = 1.2, PitchPeakDeg = 12.0, HopPeakM = 0.8;
        /// <summary>Round 2, the barrel roll's arc round the line (m): out to the side (at a quarter turn) and up (upside down), see <see cref="Arc"/>.</summary>
        public const double ArcSideM = 3.2, ArcRiseM = 4.8;
        /// <summary>The main jets' flare: at the pop (0 .. PopFlareSec) and at the catch (from TotalSec - CatchLeadSec for CatchFlareSec,
        /// peak CatchFlarePeak); the effects run on CatchTailSec after the pose is level again.</summary>
        public const double PopFlareSec = 0.5, CatchLeadSec = 0.5, CatchFlareSec = 0.8, CatchFlarePeak = 0.85, CatchTailSec = 0.35;   // round 2: the longer flip's flares (was 0.38 / 0.32 / 0.55 / 0.85 / 0.23)
        /// <summary>The sound moments (s into the flip): the pop (0), the roll's whoosh and the spin-up puff (RollStartSec), the spin-down
        /// puff (where the roll's acceleration turns over: 40 % of the roll), the soft catch (TotalSec - CatchSoundLeadSec).</summary>
        public const double CatchSoundLeadSec = 0.35;                                                         // round 2 (was 0.25)
        public static double RollSec => TotalSec - RollStartSec;
        public static double SpinDownSec => RollStartSec + 0.4 * RollSec;
        public static double EffectsEndSec => TotalSec + CatchTailSec;

        /// <summary>Round 2: the barrel roll's way round its line at t s into the flip, in the level ship's frame, for a roll whose rising pod
        /// is on the +x side (the caller signs it by the roll's direction and scales it by the room's share): the hull's middle swings out to
        /// that side as the roll spins up (side up to ArcSideM at about a quarter turn), turns over the top while upside down (rise up to
        /// ArcRiseM, the side crossing 0), swings across to the other side (-ArcSideM at about three quarters) and comes home as the ship
        /// levels. Its clock s runs from ArcStartSec to the flip's end, a share (ArcRollShare) of it carried by the roll's own progress, so
        /// the swing keeps step with the turn: s = (1 - share) (t - ArcStartSec) / (TotalSec - ArcStartSec) + share S(roll); rise =
        /// ArcRiseM Bump(s, 4, 4), side = ArcSideM W(s) / max|W|, W(s) = s^3 (1 - s)^3 (1 - 2s) (the bump's own slope): value, rate AND
        /// acceleration 0 at both ends and smooth between (no step anywhere). Rates per s.</summary>
        public static void Arc(double t, out double side, out double sideV, out double sideA, out double rise, out double riseV, out double riseA)
        {
            side = sideV = sideA = rise = riseV = riseA = 0;
            double span = TotalSec - ArcStartSec, lin = (t - ArcStartSec) / span;
            if (lin <= 0 || lin >= 1) return;
            double rs = RollSec, ur = (t - RollStartSec) / rs, a = ArcRollShare;
            double s = (1 - a) * lin + a * S(ur), s1 = (1 - a) / span + a * S1(ur) / rs, s2 = a * S2(ur) / (rs * rs);
            double b = Bump(s, 4, 4, out double b1, out double b2);
            rise = ArcRiseM * b; riseV = ArcRiseM * b1 * s1; riseA = ArcRiseM * (b2 * s1 * s1 + b1 * s2);
            double w = s * s * s * (1 + s * (-5 + s * (9 + s * (-7 + 2 * s))));                       // s^3 (1 - s)^3 (1 - 2s)
            double w1 = s * s * (3 + s * (-20 + s * (45 + s * (-42 + 14 * s))));
            double w2 = s * (6 + s * (-60 + s * (180 + s * (-210 + 84 * s))));
            double k = ArcSideM / ArcWMax;
            side = k * w; sideV = k * w1 * s1; sideA = k * (w2 * s1 * s1 + w1 * s2);
        }
        /// <summary>The arc's clock: when it starts (s, just after the pop begins) and the share of it the roll's own progress carries.</summary>
        public const double ArcStartSec = 0.16, ArcRollShare = 0.25;
        public static readonly double ArcWMax = MaxAbs(u => u * u * u * (1 + u * (-5 + u * (9 + u * (-7 + 2 * u)))));

        static double C01(double u) => u < 0 ? 0 : u > 1 ? 1 : u;

        /// <summary>The roll's shape 0..1 over u = 0..1 and its first and second derivatives (per u).</summary>
        public static double S(double u) { u = C01(u); return u * u * u * (20.0 + u * (-45.0 + u * (36.0 - 10.0 * u))); }
        public static double S1(double u) { if (u <= 0 || u >= 1) return 0; double v = 1 - u; return 60.0 * u * u * v * v * v; }
        public static double S2(double u) { if (u <= 0 || u >= 1) return 0; double v = 1 - u; return 60.0 * u * v * v * (2.0 - 5.0 * u); }
        /// <summary>The largest |S2| (at u = (4 - sqrt 6) / 10 on the spin-up) and the largest |S'''| (120, at u = 0).</summary>
        public static readonly double S2Max = MaxAbs(S2), S3Max = 120.0;

        /// <summary>A C2 bump c u^m (1 - u)^n, its peak 1 (at u = m / (m + n)), and its derivatives per u.</summary>
        public static double Bump(double u, int m, int n, out double d1, out double d2)
        {
            d1 = d2 = 0;
            if (u <= 0 || u >= 1) return 0;
            double pk = (double)m / (m + n), c = 1.0 / (Math.Pow(pk, m) * Math.Pow(1 - pk, n)), v = 1 - u;
            double um = Math.Pow(u, m), vn = Math.Pow(v, n);
            d1 = c * Math.Pow(u, m - 1) * Math.Pow(v, n - 1) * (m * v - n * u);
            d2 = c * (m * (m - 1) * Math.Pow(u, m - 2) * vn - 2.0 * m * n * Math.Pow(u, m - 1) * Math.Pow(v, n - 1) + n * (n - 1) * um * Math.Pow(v, n - 2));
            return c * um * vn;
        }

        /// <summary>The flip at t s (0 .. EffectsEndSec): the roll (deg, + = round +z: the root's +x side goes up first; x dir by the
        /// caller), its rate (deg/s) and acceleration (deg/s^2), the nose-up pitch (deg) and its rates, the hop (m) and its rates, the main
        /// jets' flare (0..1).</summary>
        public static void At(double t, out double roll, out double rollV, out double rollA, out double pitch, out double pitchV, out double pitchA,
                              out double hop, out double hopV, out double hopA, out double flare)
        {
            double rs = RollSec, u = (t - RollStartSec) / rs;
            roll = 360.0 * S(u); rollV = 360.0 * S1(u) / rs; rollA = 360.0 * S2(u) / (rs * rs);
            pitch = PitchPeakDeg * Bump(t / PitchSec, 3, 5, out double p1, out double p2); pitchV = PitchPeakDeg * p1 / PitchSec; pitchA = PitchPeakDeg * p2 / (PitchSec * PitchSec);
            hop = HopPeakM * Bump(t / TotalSec, 3, 3, out double h1, out double h2); hopV = HopPeakM * h1 / TotalSec; hopA = HopPeakM * h2 / (TotalSec * TotalSec);
            double f1 = Bump(t / PopFlareSec, 2, 2, out _, out _), f2 = CatchFlarePeak * Bump((t - (TotalSec - CatchLeadSec)) / CatchFlareSec, 2, 2, out _, out _);
            flare = Math.Max(f1, f2);
        }

        /// <summary>The roll's peak rate (deg/s), its peak acceleration (deg/s^2) and its peak jerk (deg/s^3).</summary>
        public static double PeakRate => 360.0 * S1(0.4) / RollSec;
        public static double PeakAcc => 360.0 * S2Max / (RollSec * RollSec);
        public static double PeakJerk => 360.0 * S3Max / (RollSec * RollSec * RollSec);

        static double MaxAbs(Func<double, double> f) { double m = 0; for (int i = 0; i <= 2000; i++) m = Math.Max(m, Math.Abs(f(i / 2000.0))); return m; }
    }

    /// <summary>A quintic from (p0, v0, a0) to (p1, 0, 0) over T s: the way back to level (or on to the full turn) from any moment.</summary>
    public struct OzKickflipQuintic
    {
        public double P0, V0, A0, C3, C4, C5, T, P1;
        public void Set(double p0, double v0, double a0, double p1, double t)
        {
            T = Math.Max(1e-3, t); P0 = p0; V0 = v0; A0 = a0; P1 = p1;
            double h = p1 - p0, T2 = T * T, T3 = T2 * T;
            C3 = (20.0 * h - 12.0 * v0 * T - 3.0 * a0 * T2) / (2.0 * T3);
            C4 = (-30.0 * h + 16.0 * v0 * T + 3.0 * a0 * T2) / (2.0 * T3 * T);
            C5 = (12.0 * h - 6.0 * v0 * T - a0 * T2) / (2.0 * T3 * T2);
        }
        public void At(double t, out double p, out double v, out double a)
        {
            if (t >= T) { p = P1; v = 0; a = 0; return; }
            if (t < 0) t = 0;
            p = P0 + t * (V0 + t * (0.5 * A0 + t * (C3 + t * (C4 + t * C5))));
            v = V0 + t * (A0 + t * (3.0 * C3 + t * (4.0 * C4 + t * 5.0 * C5)));
            a = A0 + t * (6.0 * C3 + t * (12.0 * C4 + t * 20.0 * C5));
        }
        /// <summary>The largest |jerk| on the way (per s^3; the check's bound for the change of the acceleration per frame).</summary>
        public double MaxJerk()
        {
            double m = 0;
            for (int k = 0; k <= 400; k++) { double t = T * k / 400.0; m = Math.Max(m, Math.Abs(6.0 * C3 + t * (24.0 * C4 + t * 60.0 * C5))); }
            return m;
        }
    }

    /// <summary>What the flip reads each frame: the rules' flight values (read only) and the view's eased lean, bank and nose.</summary>
    public struct OzKickflipIn
    {
        /// <summary>Alive (not destroyed, not dying); the rules' flight state (0 landed, 1 climb, 2 cruise, 3 descend, 4 hover); at cruise
        /// height; the planned stop on; a hover turn (over a repair deck or a landing spot); a strafe on; fire at it now; on screen.</summary>
        public bool Alive, AtHeight, Braking, HoverTurn, Strafing, UnderFire, OnScreen;
        public int State;
        /// <summary>Speed, top speed (m/s), acceleration and its most (m/s^2), the turn rate (deg/s), the way to the goal and the planned
        /// stop's length (m), the pads over the ground (m), the drawn bank, strafe lean and nose (deg), the hit points.</summary>
        public double SpeedMps, VmaxMps, AccelMps2, AccMaxMps2, TurnRateDeg, RemainM, BrakeM, FeetM, BankDeg, LeanDeg, PitchDeg, Health;
        /// <summary>The rules' goal (px) now.</summary>
        public int GoalX, GoalY;
        /// <summary>Round 2: the share of the arc that the room round the ship does NOT allow (0 = the whole arc, 1 = none: the flip in
        /// place); read when the flip starts and kept for the whole flip (the view reads the room: OzPlayableShuttle.KickTrails.cs).</summary>
        public double ArcBlocked;
        /// <summary>K436 (View/OzShuttleKickflip.Evade.cs): the rules know this flip (DV129 on: armed, or a window they opened); they allow
        /// a start now (the way for the dash, no flip on); they closed its window in the pop (a new order).</summary>
        public bool SimKnown, SimCan, SimAbort;
    }

    /// <summary>The flip's pose this frame and what happened in it.</summary>
    public struct OzKickflipOut
    {
        /// <summary>The roll (deg, signed: + = the root's +x side up first), its rate and acceleration; the nose-up pitch (deg); the hop (m);
        /// the main jets' flare (0..1); the attitude jets (-1..1: + = the +x side's bottom port and the -x side's top port fire, - = the
        /// other pair; its size = the roll's acceleration over its peak).</summary>
        public double RollDeg, RollRate, RollAcc, PitchUpDeg, HopM, Flare, Puff;
        /// <summary>Round 2: the arc this frame (m): the hull's middle out to the level ship's +x side, and up (<see cref="OzKickflipCurve.Arc"/>).</summary>
        public double SideM, RiseM;
        /// <summary>The pose or its effects are on this frame.</summary>
        public bool Active;
        /// <summary>OzKickflipState.Ev* bits of this frame.</summary>
        public int Events;
    }

    public enum OzKickflipPhase { Idle = 0, Armed = 1, Flip = 2, Settle = 3 }

    /// <summary>One ship's flip (see <see cref="OzKickflipCurve"/> for the whole story).</summary>
    public sealed partial class OzKickflipState                                                               // K411: + View/OzShuttleKickflip.Luck.cs (the flip by itself)
    {
        /// <summary>The nearest goal that is "far" (m), the steady hold before it starts (s), the way it keeps in hand past the flip (s).</summary>
        public static double FarM = 30.0, SteadyHoldSec = 0.3, MarginSec = 0.35;
        /// <summary>The steady cruise: share of the top speed, acceleration share, turn rate (deg/s), bank, strafe lean, nose (deg), pads
        /// over the ground (m). A turn faster than SoftTurnDeg during the pop is a soft event.</summary>
        public static double SteadySpeed = 0.8, SteadyAcc = 0.06, SteadyTurnDeg = 3.0, SteadyBankDeg = 4.0, SteadyLeanDeg = 1.0, SteadyPitchDeg = 2.5, SteadyFeetM = 8.0, SoftTurnDeg = 8.0;
        /// <summary>The way back to level before the roll (s), the way to the nearer level once the cruise ends under it (s), the share of
        /// the turn from which it rolls on to 360 then, the pads' height under which the cruise counts as over (m), the longest it stays
        /// armed while the ship has not taken off (s).</summary>
        public static double AbortSec = 0.75, WrapSec = 0.9, WrapOnShare = 0.25, HardFeetM = 6.0, ArmedGroundMaxSec = 60.0;
        /// <summary>A goal that moves no farther than this (m) is the same order; farther = a new order (a new click on the map always
        /// counts as one). RetargetM in the pop (a new order there eases it back); ArmedRetargetM while it waits for the steady cruise: the
        /// rules may move a new goal to the nearest free landing spot round the ordered point when another ship holds that one (K338: rings
        /// out to ZObject.ShuttleClaimReachM = 45 m), which is still the order that was double-clicked (review K344: 24 m lost those flips).</summary>
        public static double RetargetM = 24.0, ArmedRetargetM = 48.0;
        public const int EvPop = 1, EvRoll = 2, EvPuffUp = 4, EvPuffDown = 8, EvCatch = 16, EvStart = 32, EvEnd = 64, EvAbort = 128, EvArmed = 256, EvDisarmed = 512, EvWrap = 1024;

        public OzKickflipPhase Phase { get; private set; }
        public int Id;
        public int Dir { get; private set; } = 1;
        /// <summary>Counts: armed, started, finished on the curve, eased back before the roll, eased to a level once the cruise ended,
        /// disarmed, near double clicks, doubles whose order the ship did not take, killed (destroyed during it).</summary>
        public int Arms, Starts, Ends, Aborts, Wraps, Disarms, NearDoubles, NotTaken, Kills;
        public int ArmX { get; private set; } = int.MinValue;
        public int ArmY { get; private set; } = int.MinValue;
        /// <summary>Seconds into the flip (or into the way back), the steady cruise seen so far (s).</summary>
        public double T { get; private set; }
        public double SteadyFor { get; private set; }
        /// <summary>The largest |jerk| of the roll's way out of the curve (deg/s^3), set when it begins.</summary>
        public double SettleJerk { get; private set; }
        /// <summary>Round 2: the share of the arc this flip draws (1 - the room's ArcBlocked when it started).</summary>
        public double ArcK { get; private set; } = 1.0;
        public OzKickflipOut Last;
        bool pending, flew, rollBegan, catchSaid, clickNew; int pendX, pendY; double lastHealth = double.NaN, armedFor;
        OzKickflipQuintic qr, qp, qh, qs, qz; double sT, sFrom;                                              // round 2: qs / qz the arc's way home

        public OzKickflipState(int id = 0) { Id = id; }

        /// <summary>A plain order click this ship took part in. dbl = the second click of a double click; took = the rules' goal is now this
        /// click's (clamped) point; distM = the ship's line point to that goal; (gx, gy) = the goal. A single click whose goal is the armed
        /// (or waiting) one again, within <see cref="OzKickflipGesture.DoubleM"/> m (the third click of a triple click, the destination
        /// clicked once more), is the same order: it stays armed (review K344: a triple click lost the flip).</summary>
        public int Click(bool dbl, bool took, double distM, int gx, int gy)
        {
            if (!dbl)
            {
                if (pending && !(took && Near(gx, gy, pendX, pendY))) pending = false;
                if (Phase == OzKickflipPhase.Armed && !(took && Near(gx, gy, ArmX, ArmY))) { Disarm(); return EvDisarmed; }
                if (Phase == OzKickflipPhase.Flip && took && !Near(gx, gy, ArmX, ArmY)) clickNew = true;           // review K344: a new click on the map in the pop is a new order however near its goal (it eases back; the roll rolls on)
                return 0;
            }
            if (!took) { NotTaken++; return 0; }
            if (distM < FarM) { NearDoubles++; pending = false; if (Phase == OzKickflipPhase.Armed) { Disarm(); return EvDisarmed; } return 0; }
            if (Phase == OzKickflipPhase.Flip || Phase == OzKickflipPhase.Settle) { if (EvadeRules) { IgnoredInFlip++; pending = false; return 0; } pending = true; pendX = gx; pendY = gy; return 0; }   // K436: never two at once
            Arm(gx, gy); return EvArmed;
        }

        double Moved(in OzKickflipIn i) { double dx = i.GoalX - ArmX, dy = i.GoalY - ArmY; return Math.Sqrt(dx * dx + dy * dy) / 8.0; }
        static bool Near(int ax, int ay, int bx, int by) { double dx = ax - bx, dy = ay - by; return Math.Sqrt(dx * dx + dy * dy) / 8.0 <= OzKickflipGesture.DoubleM; }
        void Follow(in OzKickflipIn i) { ArmX = i.GoalX; ArmY = i.GoalY; }

        void Arm(int gx, int gy) { Phase = OzKickflipPhase.Armed; ArmX = gx; ArmY = gy; SteadyFor = 0; flew = false; armedFor = 0; clickNew = false; Arms++; }
        void Disarm() { Phase = OzKickflipPhase.Idle; SteadyFor = 0; Disarms++; }

        /// <summary>Destroyed (or gone) while it flips: the pose stays as it was drawn last (the caller keeps it), the state ends.</summary>
        public void Kill() { if (Phase == OzKickflipPhase.Flip || Phase == OzKickflipPhase.Settle) Kills++; Phase = OzKickflipPhase.Idle; pending = false; clickNew = false; Last = default; }

        /// <summary>The steady cruise the flip may start in (see the class summary), without the on-screen test.</summary>
        public static bool Steady(in OzKickflipIn i) => EvadeRules ? SteadyEvade(i) :                      // K436: fire never stops it (View/OzShuttleKickflip.Evade.cs)
            i.Alive && i.State == 2 && i.AtHeight && !i.Braking && !i.HoverTurn && !i.Strafing && !i.UnderFire
            && i.SpeedMps >= SteadySpeed * i.VmaxMps && Math.Abs(i.AccelMps2) <= SteadyAcc * Math.Max(1e-6, i.AccMaxMps2) && Math.Abs(i.TurnRateDeg) <= SteadyTurnDeg
            && Math.Abs(i.BankDeg) <= SteadyBankDeg && Math.Abs(i.LeanDeg) <= SteadyLeanDeg && Math.Abs(i.PitchDeg) <= SteadyPitchDeg && i.FeetM >= SteadyFeetM
            && i.RemainM - i.BrakeM >= i.SpeedMps * (OzKickflipCurve.TotalSec + MarginSec);

        /// <summary>The first test of <see cref="Steady"/> that fails (null = steady): for the checks' logs.</summary>
        public static string WhyNot(in OzKickflipIn i)
        {
            if (EvadeRules) return WhyNotEvade(i);                                                   // K436 (View/OzShuttleKickflip.Evade.cs)
            if (!i.Alive) return "not alive";
            if (i.State != 2) return i.State == 1 ? "climbing" : i.State == 3 ? "coming down" : i.State == 4 ? "hovering" : "on the ground";
            if (!i.AtHeight) return "under cruise height";
            if (i.Braking) return "braking";
            if (i.HoverTurn) return "hover turn";
            if (i.Strafing) return "strafing";
            if (i.UnderFire) return "under fire";
            if (i.SpeedMps < SteadySpeed * i.VmaxMps) return "slow";
            if (Math.Abs(i.AccelMps2) > SteadyAcc * Math.Max(1e-6, i.AccMaxMps2)) return "speeding up or slowing";
            if (Math.Abs(i.TurnRateDeg) > SteadyTurnDeg) return "turning";
            if (Math.Abs(i.BankDeg) > SteadyBankDeg) return "banked";
            if (Math.Abs(i.LeanDeg) > SteadyLeanDeg) return "leaning (strafe or giving way)";
            if (Math.Abs(i.PitchDeg) > SteadyPitchDeg) return "nose not level";
            if (i.FeetM < SteadyFeetM) return "low";
            if (i.RemainM - i.BrakeM < i.SpeedMps * (OzKickflipCurve.TotalSec + MarginSec)) return "too little way left";
            return null;
        }

        /// <summary>One frame of dt s (the game's clock: 0 while paused). o = the pose and the events of this frame.</summary>
        public void Step(in OzKickflipIn i, double dt, out OzKickflipOut o)
        {
            o = default;
            if (dt < 0 || double.IsNaN(dt)) dt = 0;
            int ev = ExternalEvents();                                                               // K436: a flip the rules started pops now (View/OzShuttleKickflip.Evade.cs)
            if (Phase == OzKickflipPhase.Idle && pending) { pending = false; Arm(pendX, pendY); ev |= EvArmed; }
            switch (Phase)
            {
                case OzKickflipPhase.Armed:
                {
                    armedFor += dt;
                    if (i.State == 1 || i.State == 2) flew = true;
                    bool over = !i.Alive || Moved(i) > ArmedRetargetM || (flew && i.State != 1 && i.State != 2) || (!flew && armedFor > ArmedGroundMaxSec);
                    if (!over) Follow(i);
                    if (over) { Disarm(); ev |= EvDisarmed; break; }
                    SteadyFor = Steady(i) ? SteadyFor + dt : 0;
                    if (SteadyFor >= SteadyHoldSec && i.OnScreen)
                    {
                        Phase = OzKickflipPhase.Flip; T = 0; Starts++; rollBegan = false; catchSaid = false; lastHealth = i.Health; SelfFlip = false;
                        ArcK = double.IsNaN(i.ArcBlocked) ? 1.0 : 1.0 - Clamp(i.ArcBlocked, 0, 1);                       // round 2: the arc the room allows, kept for this flip
                        Dir = ((Id + Starts) & 1) == 0 ? 1 : -1;
                        ev |= EvStart | EvPop;
                        Pose(0, ref o);
                    }
                    break;
                }
                case OzKickflipPhase.Flip:
                {
                    double t0 = T; T += dt;
                    bool hit = !double.IsNaN(lastHealth) && i.Health < lastHealth - 1e-9; lastHealth = i.Health;
                    bool newOrder = clickNew || Moved(i) > RetargetM; clickNew = false; if (!newOrder) Follow(i);
                    bool soft = EvadeRules ? SoftEvade(i, newOrder) : (hit || newOrder || i.Strafing || i.UnderFire || Math.Abs(i.TurnRateDeg) > SoftTurnDeg);   // K436: only the rules' word (or a new order) eases the pop back
                    bool level = T >= OzKickflipCurve.TotalSec;                                                // the pose is level again; only the effects' tail is left
                    bool hard = !i.Alive || i.State != 2 || !i.AtHeight || i.HoverTurn || i.FeetM < HardFeetM;
                    if (!i.Alive) { Kill(); break; }
                    if (!level && (hard || (soft && T < OzKickflipCurve.RollStartSec)))
                    {   // out of the curve, from where it is now (this frame's curve point: no step)
                        OzKickflipCurve.At(T, out double r, out double rv, out double ra, out double p, out double pv, out double pa, out double h, out double hv, out double ha, out _);
                        double share = r / 360.0, target = hard && share >= WrapOnShare ? 360.0 : 0.0, dur = hard ? WrapSec : AbortSec;
                        qr.Set(r * Dir, rv * Dir, ra * Dir, target * Dir, dur); qp.Set(p, pv, pa, 0, dur); qh.Set(h, hv, ha, 0, dur);
                        OzKickflipCurve.Arc(T, out double sd, out double sv, out double sa, out double rz, out double zv, out double za);   // round 2: the arc from where it is, home
                        double ks = Dir * ArcK; qs.Set(sd * ks, sv * ks, sa * ks, 0, dur); qz.Set(rz * ArcK, zv * ArcK, za * ArcK, 0, dur);
                        Phase = OzKickflipPhase.Settle; sT = 0; sFrom = T; ev |= hard ? EvWrap : EvAbort; SettleJerk = qr.MaxJerk();
                        if (hard) Wraps++; else Aborts++;
                        SettlePose(ref o);
                        break;
                    }
                    // review K344: the pose is level from TotalSec; the main jets' catch flare runs out to EffectsEndSec even if the cruise
                    // ends in that tail (ending it there cut a flare of up to 0.8 to 0 in one frame)
                    if (T >= OzKickflipCurve.EffectsEndSec) { End(ref ev); break; }
                    if (t0 < OzKickflipCurve.RollStartSec && T >= OzKickflipCurve.RollStartSec) { ev |= EvRoll | EvPuffUp; rollBegan = true; }
                    if (t0 < OzKickflipCurve.SpinDownSec && T >= OzKickflipCurve.SpinDownSec) ev |= EvPuffDown;
                    double cAt = OzKickflipCurve.TotalSec - OzKickflipCurve.CatchSoundLeadSec;
                    if (t0 < cAt && T >= cAt && !catchSaid) { ev |= EvCatch; catchSaid = true; }
                    Pose(T, ref o);
                    break;
                }
                case OzKickflipPhase.Settle:
                {
                    double s0 = sT; sT += dt;
                    if (!i.Alive) { Kill(); break; }
                    double cAt = qr.T - OzKickflipCurve.CatchSoundLeadSec;
                    if (rollBegan && !catchSaid && s0 < cAt && sT >= cAt) { ev |= EvCatch; catchSaid = true; }
                    if (sT >= qr.T + (rollBegan ? OzKickflipCurve.CatchTailSec : 0.0)) { End(ref ev); break; }   // a caught roll: its catch flare's tail (the pose is level by qr.T)
                    SettlePose(ref o);
                    break;
                }
            }
            o.Events = ev;
            Last = o;
        }

        void End(ref int ev)
        {
            if (Phase == OzKickflipPhase.Flip) Ends++;
            Phase = OzKickflipPhase.Idle; T = 0; ev |= EvEnd;
            if (pending) { pending = false; Arm(pendX, pendY); ev |= EvArmed; }
        }

        void Pose(double t, ref OzKickflipOut o)
        {
            OzKickflipCurve.At(Math.Min(t, OzKickflipCurve.TotalSec), out double r, out double rv, out double ra, out double p, out _, out _, out double h, out _, out _, out double f);
            if (t >= OzKickflipCurve.TotalSec) { r = 360.0; rv = ra = 0; p = h = 0; f = OzKickflipCurve.CatchFlarePeak * OzKickflipCurve.Bump((t - (OzKickflipCurve.TotalSec - OzKickflipCurve.CatchLeadSec)) / OzKickflipCurve.CatchFlareSec, 2, 2, out _, out _); }
            o.RollDeg = r * Dir; o.RollRate = rv * Dir; o.RollAcc = ra * Dir; o.PitchUpDeg = p; o.HopM = h; o.Flare = f;
            OzKickflipCurve.Arc(Math.Min(t, OzKickflipCurve.TotalSec), out double sd, out _, out _, out double rz, out _, out _);   // round 2: the barrel roll's arc
            o.SideM = sd * Dir * ArcK; o.RiseM = rz * ArcK;
            o.Puff = Clamp(o.RollAcc / OzKickflipCurve.PeakAcc, -1, 1); o.Active = true;
        }

        void SettlePose(ref OzKickflipOut o)
        {
            qr.At(sT, out double r, out double rv, out double ra); qp.At(sT, out double p, out _, out _); qh.At(sT, out double h, out _, out _);
            o.RollDeg = r; o.RollRate = rv; o.RollAcc = ra; o.PitchUpDeg = p; o.HopM = h; o.Flare = SettleFlare();
            qs.At(sT, out double sd, out _, out _); qz.At(sT, out double rz, out _, out _); o.SideM = sd; o.RiseM = rz;   // round 2: the arc home on its own quintic
            o.Puff = Clamp(ra / OzKickflipCurve.PeakAcc, -1, 1); o.Active = true;
        }

        /// <summary>The main jets' flare on the way out (review K344: it was 0 from the first frame of the way out, so a pop flare of up to
        /// 1.0, or a catch flare of up to 0.85, went out in one frame: the second flame, the halo and the lights' gain snapped off). The flare
        /// the flip had at the switch runs on along its own curve (the pop's dies out by PopFlareSec, a catch flare that had begun runs out by
        /// EffectsEndSec), and a roll that is caught on the way out gets its own catch flare, timed on the way out's end as on the curve.</summary>
        double SettleFlare()
        {
            double u = sFrom + sT, f = OzKickflipCurve.Bump(u / OzKickflipCurve.PopFlareSec, 2, 2, out _, out _);
            double catch0 = OzKickflipCurve.TotalSec - OzKickflipCurve.CatchLeadSec;
            if (sFrom > catch0) f = Math.Max(f, OzKickflipCurve.CatchFlarePeak * OzKickflipCurve.Bump((u - catch0) / OzKickflipCurve.CatchFlareSec, 2, 2, out _, out _));
            if (rollBegan) f = Math.Max(f, OzKickflipCurve.CatchFlarePeak * OzKickflipCurve.Bump((sT - (qr.T - OzKickflipCurve.CatchLeadSec)) / OzKickflipCurve.CatchFlareSec, 2, 2, out _, out _));
            return f;
        }

        static double Clamp(double x, double a, double b) => x < a ? a : x > b ? b : x;
    }

    /// <summary>The double click (see <see cref="OzKickflipCurve"/>): one per game (PlayerInput's order clicks), one per check.</summary>
    public sealed class OzKickflipGesture
    {
        /// <summary>The longest gap between the two clicks (s, real time), their farthest distance on screen (px at 1080 lines, x the UI
        /// scale) and on the ground (m).</summary>
        public static double DoubleSec = 0.35, DoublePx = 36.0, DoubleM = 6.0;
        public const int MaxShips = 64;
        double lastAt = -1e9, lastSx, lastSy, lastGx, lastGz; bool lastPlain; int lastN;
        readonly int[] lastIds = new int[MaxShips];
        /// <summary>Counts: double clicks, single clicks.</summary>
        public int Doubles, Singles;

        /// <summary>One order click at `now` (s): screen point (px), ground point (m, x / z), the UI scale (screen height / 1080, at least
        /// 0.75), plain = neither a queue click nor a nearest-only order; ids[0..n) = the shuttles in this click's selection. Returns true
        /// for the second click of a double click; inBoth[k] = ship ids[k] was in the first click's selection too.</summary>
        public bool Click(double now, double sx, double sy, double gx, double gz, double uiScale, bool plain, int[] ids, int n, bool[] inBoth)
        {
            n = Math.Min(n, Math.Min(MaxShips, Math.Min(ids.Length, inBoth.Length)));
            double ds = Math.Sqrt((sx - lastSx) * (sx - lastSx) + (sy - lastSy) * (sy - lastSy)), dg = Math.Sqrt((gx - lastGx) * (gx - lastGx) + (gz - lastGz) * (gz - lastGz));
            bool dbl = plain && lastPlain && now >= lastAt && now - lastAt < DoubleSec && ds <= DoublePx * Math.Max(0.75, uiScale) && dg <= DoubleM;
            for (int k = 0; k < n; k++)
            {
                bool b = false;
                if (dbl) for (int j = 0; j < lastN; j++) if (lastIds[j] == ids[k]) { b = true; break; }
                inBoth[k] = b;
            }
            if (dbl) { Doubles++; lastAt = -1e9; lastPlain = false; lastN = 0; return true; }
            Singles++; lastAt = now; lastSx = sx; lastSy = sy; lastGx = gx; lastGz = gz; lastPlain = plain; lastN = n;
            for (int k = 0; k < n; k++) lastIds[k] = ids[k];
            return false;
        }

        public void Reset() { lastAt = -1e9; lastPlain = false; lastN = 0; }
    }
}
