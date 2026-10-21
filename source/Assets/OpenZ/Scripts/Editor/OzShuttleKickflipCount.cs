using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// K344 counted check, out of Play (menu OpenZ/V1.1/Shuttle: K344 Kickflip Check (no Play), Editor/OzShuttleKickflipMenu.cs; the
    /// kit's headless runner runs the same code outside the editor). Four parts, PASS / FAIL lines:
    ///  G  the gesture (OzKickflipGesture): double / single for timing, distance on screen and on the ground, the UI scale, queue and
    ///     nearest-only clicks, groups (only the shuttles in both clicks), a third click;
    ///  A  the arming (OzKickflipState.Click): far (30 m) arms, nearer does nothing, a refused order does nothing, a single click
    ///     elsewhere disarms (the same spot again, a triple click, keeps it), a double during a flip waits for its end; at most one flip per
    ///     order; the goal moved by the rules up to 48 m while armed (K338) is the same order; a new map click in the pop eases it back;
    ///  C  the curve at 60, 30 and 144 fps and on a jittery clock: no step in the angle, its rate or its acceleration (each frame's
    ///     change within what the curve's own rate, acceleration and jerk allow), level and still at both ends; every interruption
    ///     (a strafe, a hit, fire, a new order; the descent) at 35 moments: eased back or rolled on, never a step;
    ///  L  the lamps (lamp fix, review K344): the ship's own model files read (View/OzShuttleKickflip.LampCheck.cs); the field of clear
    ///     margins (View/OzShuttleKickflip.LampField.cs) against the model's own cone tests; the flips at 60, 30 and 144 fps with every lamp
    ///     aim the game gives in flight (the belly lamps: their housings' aim, the landing lights after the take-off and on the approach;
    ///     the nose searchlight: its sweep and locks on enemies), both roll directions: no frame with a drawn beam through the ship (the
    ///     builder's gimbal alone has them), the fade 1 at both ends, no step (also at 1000 fps and on paused frames), the interruptions,
    ///     the ramp light;
    ///  R  the rules untouched: the same worlds and the same clicks played twice at 60 fps, once with the flip's twin reading the rules
    ///     every frame (the game's own state machine, View/OzShuttleKickflip.Core.cs) and once without: the whole world hashed every
    ///     tick, identical tick for tick (a plain map with two shuttles and a jeep, a near and a slow double, an eased-back and a
    ///     rolled-on flip, rockets under the way; a Level 16 match against the computer). The flips seen there: each in the steady
    ///     cruise, each finished before the planned stop; none while the ship's own sun shadow is on (K256g: only on the ground).
    /// Test only: nothing here is used by the game.
    /// </summary>
    public static class OzShuttleKickflipCount
    {
        const double Dt60 = 1.0 / 60.0;
        /// <summary>Iteration aid: a line a second for every armed ship in the replays' logs.</summary>
        public static bool Trace;
        static StringBuilder sb; static int pass, fail;

        static void V(bool ok, string what) { if (ok) pass++; else fail++; sb.AppendLine((ok ? "PASS  " : "FAIL  ") + what); }

        /// <summary>modelRoot: the project folder whose ship model files the lamp part (L) reads (null: L fails, the model is needed).</summary>
        public static string Check(string settings, string l16Map, int matchSecs = 240, string modelRoot = null)
        {
            sb = new StringBuilder(); pass = fail = 0;
            sb.AppendLine($"K344 kickflip check: the flip {OzKickflipCurve.TotalSec:F2} s (roll from {OzKickflipCurve.RollStartSec:F2} s; peak rate {OzKickflipCurve.PeakRate:F0} deg/s, acceleration {OzKickflipCurve.PeakAcc:F0} deg/s^2, jerk {OzKickflipCurve.PeakJerk:F0} deg/s^3), far = {OzKickflipState.FarM:F0} m, double = {OzKickflipGesture.DoubleSec:F2} s / {OzKickflipGesture.DoublePx:F0} px / {OzKickflipGesture.DoubleM:F0} m");
            bool k436Ev = OzKickflipState.EvadeRules, k436Dv = ZWorld.ShuttleKickDeviation, k436Luck = OzKickflipLuck.On;   // K436: these parts count round 2's rules (the K436 rules: Editor/OzShuttleKickCount.cs)
            OzKickflipState.EvadeRules = false; ZWorld.ShuttleKickDeviation = false; OzKickflipLuck.On = true;
            try
            {
            Gesture(); Arming(); Curve(); OzShuttleKickflipCountK390.Run(sb, V); OzShuttleKickflipCountK411.Run(sb, V); OzShuttleKickflipCountK423.Run(sb, V, settings); Lamps(modelRoot);           // round 2 (K390): the arc, the room, the trails, the whoosh
            if (settings != null) Replay(settings, l16Map, matchSecs);
            }
            finally { OzKickflipState.EvadeRules = k436Ev; ZWorld.ShuttleKickDeviation = k436Dv; OzKickflipLuck.On = k436Luck; }
            sb.AppendLine($"K344 kickflip check: {pass} PASS, {fail} FAIL");
            var r = sb.ToString(); sb = null; return r;
        }

        // ================================================================== G: the gesture
        static readonly int[] A = { 101 }, AB = { 101, 102 }, B = { 102 };
        static readonly bool[] both = new bool[OzKickflipGesture.MaxShips];

        static bool Two(double gap, double px, double m, double ui = 1.0, bool plain1 = true, bool plain2 = true, int[] s1 = null, int[] s2 = null)
        {
            var g = new OzKickflipGesture(); s1 = s1 ?? A; s2 = s2 ?? A;
            g.Click(100.0, 500, 400, 20, -30, ui, plain1, s1, s1.Length, both);
            return g.Click(100.0 + gap, 500 + px, 400, 20 + m, -30, ui, plain2, s2, s2.Length, both);
        }

        static void Gesture()
        {
            sb.AppendLine("-- G the gesture");
            V(Two(0.20, 4, 0.5) && both[0], "G1 two order clicks 0.20 s, 4 px, 0.5 m apart: a double click, the ship in both");
            V(Two(0.34, 4, 0.5), "G2 0.34 s apart: a double click");
            V(!Two(0.36, 4, 0.5), "G3 0.36 s apart: two single clicks");
            V(Two(0.2, 34, 0.5) && !Two(0.2, 40, 0.5), "G4 34 px apart: double; 40 px: single (UI scale 1, 1080 lines)");
            V(Two(0.2, 50, 0.5, 1.48), "G5 50 px apart at 1600 lines (UI scale 1.48): double");
            V(!Two(0.2, 10, 7.0), "G6 10 px but 7 m apart on the ground (the camera moved between them): single");
            V(!Two(0.2, 4, 0.5, 1, false, true) && !Two(0.2, 4, 0.5, 1, true, false), "G7 a Shift queue click or a nearest-only (Z) order on either side: single");
            bool d1 = Two(0.2, 4, 0.5, 1, true, true, AB, AB); bool a1 = both[0], b1 = both[1];
            bool d2 = Two(0.2, 4, 0.5, 1, true, true, AB, A); bool a2 = both[0];
            bool d3 = Two(0.2, 4, 0.5, 1, true, true, A, AB); bool a3 = both[0], b3 = both[1];
            bool d4 = Two(0.2, 4, 0.5, 1, true, true, B, A); bool a4 = both[0];
            V(d1 && a1 && b1 && d2 && a2 && d3 && a3 && !b3 && d4 && !a4, "G8 groups: both shuttles in both clicks: each told double; a ship only in the second click's selection: single for it; a different ship each click: single");
            var g = new OzKickflipGesture();
            bool c1 = g.Click(10.0, 500, 400, 0, 0, 1, true, A, 1, both), c2 = g.Click(10.2, 500, 400, 0, 0, 1, true, A, 1, both),
                 c3 = g.Click(10.4, 500, 400, 0, 0, 1, true, A, 1, both), c4 = g.Click(10.6, 500, 400, 0, 0, 1, true, A, 1, both);
            V(!c1 && c2 && !c3 && c4, "G9 four quick clicks: double on the 2nd, the 3rd starts over, double again on the 4th (never a double twice in a row)");
            g = new OzKickflipGesture(); g.Click(50.0, 500, 400, 0, 0, 1, true, A, 1, both);
            V(!g.Click(49.9, 500, 400, 0, 0, 1, true, A, 1, both), "G10 a clock that went back (a new match): single");
        }

        // ================================================================== A: the arming
        static void Arming()
        {
            sb.AppendLine("-- A the arming");
            var s = new OzKickflipState(7);
            int e = s.Click(true, true, 30.0, 900, 900);
            V((e & OzKickflipState.EvArmed) != 0 && s.Phase == OzKickflipPhase.Armed && s.ArmX == 900, "A1 a double click whose order the ship took, 30.0 m away: armed for that goal");
            s.Click(false, true, 80.0, 950, 900);
            V(s.Phase == OzKickflipPhase.Idle, "A2 then a single order click: disarmed (a new order)");
            e = s.Click(true, true, 29.9, 900, 900);
            V(e == 0 && s.Phase == OzKickflipPhase.Idle && s.NearDoubles == 1, "A3 a double click 29.9 m away: nothing at all");
            e = s.Click(true, false, 80.0, 900, 900);
            V(e == 0 && s.Phase == OzKickflipPhase.Idle && s.NotTaken == 1, "A4 a double click whose order the ship did not take (refused, or a repair trip): nothing");
            s.Click(true, true, 60.0, 900, 900); s.Click(true, true, 10.0, 910, 900);
            V(s.Phase == OzKickflipPhase.Idle, "A5 armed, then a near double click: disarmed (a new order, and a near one)");
            // a flip under way, then a far double click: it waits for the end, then it is armed for the new goal (one flip per order)
            s = new OzKickflipState(7); s.Click(true, true, 90.0, 1200, 900);
            var i = SteadyIn(1200, 900, 90.0); int starts0 = s.Starts;
            for (int k = 0; k < 120 && s.Phase != OzKickflipPhase.Flip; k++) s.Step(i, Dt60, out _);
            bool flipping = s.Phase == OzKickflipPhase.Flip;
            s.Click(true, true, 90.0, 1300, 900);
            bool stillFlip = s.Phase == OzKickflipPhase.Flip;
            OzKickflipOut o = default; int armedEv = 0;
            for (int k = 0; k < 400 && s.Phase != OzKickflipPhase.Armed; k++) { s.Step(i, Dt60, out o); armedEv |= o.Events; }
            V(flipping && stillFlip && s.Phase == OzKickflipPhase.Armed && s.ArmX == 1300 && (armedEv & OzKickflipState.EvArmed) != 0 && s.Starts == starts0 + 1,
              "A6 a far double click during a flip: the flip goes on to its end, then it is armed for the new order's goal");
            // one flip per order: after the flip, the same order never flips again
            s = new OzKickflipState(7); s.Click(true, true, 300.0, 1500, 900); i = SteadyIn(1500, 900, 300.0);
            for (int k = 0; k < 60 * 20; k++) s.Step(i, Dt60, out _);
            V(s.Starts == 1 && s.Ends == 1 && s.Phase == OzKickflipPhase.Idle, $"A7 one order, 20 s of steady cruise after it: {s.Starts} flip (at most one per order)");
            // armed, never takes off (the ramp held): gone after a minute; armed, the flight ends without a steady moment: gone
            s = new OzKickflipState(7); s.Click(true, true, 90.0, 1500, 900); var g0 = SteadyIn(1500, 900, 90.0); g0.State = 0; g0.SpeedMps = 0;
            for (int k = 0; k < 60 * 61; k++) s.Step(g0, Dt60, out _);
            bool goneGround = s.Phase == OzKickflipPhase.Idle;
            s = new OzKickflipState(7); s.Click(true, true, 90.0, 1500, 900); var c0 = SteadyIn(1500, 900, 90.0); c0.TurnRateDeg = 20;
            for (int k = 0; k < 60; k++) s.Step(c0, Dt60, out _);
            c0.State = 3; s.Step(c0, Dt60, out _);
            V(goneGround && s.Phase == OzKickflipPhase.Idle && s.Starts == 0, "A8 armed but it never took off (60 s), or its cruise ended with no steady moment: disarmed, no flip");
            // not on screen: it waits (armed) and flips once it is
            s = new OzKickflipState(7); s.Click(true, true, 300.0, 1500, 900); var off = SteadyIn(1500, 900, 300.0); off.OnScreen = false;
            for (int k = 0; k < 120; k++) s.Step(off, Dt60, out _);
            bool waited = s.Phase == OzKickflipPhase.Armed && s.Starts == 0; off.OnScreen = true; s.Step(off, Dt60, out _);
            V(waited && s.Phase == OzKickflipPhase.Flip, "A9 off screen it waits (armed); the first steady frame on screen starts it");
            // the rules move its goal 10 m, or 40 m (a free landing spot round the ordered point: the air traffic, K338, looks out to 45 m):
            // the same order; 50 m: a new one
            s = new OzKickflipState(7); s.Click(true, true, 300.0, 1500, 900); var mv = SteadyIn(1500, 900, 300.0); mv.OnScreen = false;
            for (int k = 0; k < 30; k++) s.Step(mv, Dt60, out _);
            mv.GoalX = 1580; for (int k = 0; k < 5; k++) s.Step(mv, Dt60, out _);
            bool kept = s.Phase == OzKickflipPhase.Armed && s.ArmX == 1580;
            mv.GoalX = 1580 + 40 * 8; for (int k = 0; k < 5; k++) s.Step(mv, Dt60, out _);
            bool kept40 = s.Phase == OzKickflipPhase.Armed && s.ArmX == 1580 + 40 * 8;
            mv.GoalX = 1580 + 90 * 8; s.Step(mv, Dt60, out _);
            V(kept && kept40 && s.Phase == OzKickflipPhase.Idle, $"A10 the rules move its goal 10 m, then 40 m (a free spot round the ordered point, as the air traffic does out to 45 m): still armed, for the moved goal; then 50 m: a new order, disarmed (same order up to {OzKickflipState.ArmedRetargetM:F0} m while armed)");
            // a triple click: the third click sends the same order again (the same spot): still armed; a single click 7 m away: disarmed
            s = new OzKickflipState(7); s.Click(false, true, 300.0, 1500, 900); s.Click(true, true, 300.0, 1500, 900);
            bool armed3 = s.Phase == OzKickflipPhase.Armed;
            s.Click(false, true, 300.0, 1500, 900); bool kept3 = s.Phase == OzKickflipPhase.Armed;
            s.Click(false, true, 300.0, 1516, 900); bool keptNear = s.Phase == OzKickflipPhase.Armed;
            s.Click(false, true, 300.0, 1500 + 7 * 8, 900);
            bool gone7 = s.Phase == OzKickflipPhase.Idle;
            // the same during a flip: a far double waits (one flip per order), its third click keeps it waiting
            s = new OzKickflipState(7); s.Click(true, true, 300.0, 1500, 900); var tr3 = SteadyIn(1500, 900, 300.0);
            for (int k = 0; k < 120 && s.Phase != OzKickflipPhase.Flip; k++) s.Step(tr3, Dt60, out _);
            s.Click(false, true, 300.0, 2000, 900); s.Click(true, true, 300.0, 2000, 900); s.Click(false, true, 300.0, 2000, 900);
            tr3.GoalX = 2000; for (int k = 0; k < 400 && s.Phase != OzKickflipPhase.Armed; k++) s.Step(tr3, Dt60, out _);
            V(armed3 && kept3 && keptNear && gone7 && s.Phase == OzKickflipPhase.Armed && s.ArmX == 2000,
              $"A11 a triple click: armed by the double, the third click (the same spot) and one 2 m off keep it armed; a single click 7 m away disarms (a new order; same spot = within {OzKickflipGesture.DoubleM:F0} m); during a flip a far triple click still arms the new order after it");
            // review K344: a new click on the map in the pop is a new order however near its goal (the Play proof's K6 gives one 22 .. 40 m
            // from the ship, which can be under the 24 m the goal test needs); the same spot clicked again is not
            s = new OzKickflipState(7); s.Click(true, true, 300.0, 1500, 900); var p12 = SteadyIn(1500, 900, 300.0);
            for (int k = 0; k < 120 && s.Phase != OzKickflipPhase.Flip; k++) s.Step(p12, Dt60, out _);
            s.Step(p12, Dt60, out _);
            s.Click(false, true, 300.0, 1500, 900); s.Step(p12, Dt60, out _);
            bool sameOn = s.Phase == OzKickflipPhase.Flip && s.Aborts == 0;
            s.Click(false, true, 300.0, 1580, 900); p12.GoalX = 1580; s.Step(p12, Dt60, out _);
            V(sameOn && s.Phase == OzKickflipPhase.Settle && s.Aborts == 1, "A12 in the pop: the same spot clicked again flips on; a new click 10 m from its goal (a new order, under the 24 m the goal test needs) eases it back to level");
        }

        /// <summary>A steady cruise at the shuttle's top speed (the game's numbers: the jeep's 17 x 2.5 = 42 px/s), goal (gx, gy), remain m to go.</summary>
        static OzKickflipIn SteadyIn(int gx, int gy, double remain)
        {
            double v = 42.0 / 8.0, a = ZWorld.ShuttleAccelMax(42.0) / 8.0;
            return new OzKickflipIn
            {
                Alive = true, State = 2, AtHeight = true, OnScreen = true, SpeedMps = v, VmaxMps = v, AccelMps2 = 0, AccMaxMps2 = a, TurnRateDeg = 0,
                RemainM = remain, BrakeM = ZWorld.ShuttleBrakeDistance(42.0, 0.0, 42.0) / 8.0, FeetM = 12, Health = 3000, GoalX = gx, GoalY = gy,
            };
        }

        // ================================================================== C: the curve
        sealed class Track
        {
            public double maxDA, maxDV, maxDAcc, boundA, boundV, boundAcc, maxPitchStep, maxHopStep, endRoll, endRate, endAcc, firstRate, firstAcc, maxNumErr, maxRate, maxFlareStep, maxFlare;
            public int frames; public bool ended, startedLevel = true, pausedMoved;
        }

        /// <summary>Steps a flip on a clock (dts) and measures the frame-to-frame changes of the angle, its rate and acceleration.
        /// interruptAt >= 0: from that moment into the flip the input carries `kind` (soft: strafe / hit / fire / new order / turn; hard:
        /// the descent).</summary>
        static Track Run(Func<int, double> dts, double interruptAt = -1, string kind = "")
        {
            var s = new OzKickflipState(1); var tr = new Track();
            s.Click(true, true, 300.0, 1500, 900);
            var i = SteadyIn(1500, 900, 300.0);
            int guard = 0; OzKickflipOut o = default;
            while (s.Phase != OzKickflipPhase.Flip && guard++ < 1000) s.Step(i, Dt60, out o);
            double pr = o.RollDeg, pv = o.RollRate, pa = o.RollAcc, pp = o.PitchUpDeg, ph = o.HopM, pf = o.Active ? o.Flare : 0, ppr = double.NaN, pppr = double.NaN; double tIn = 0, jerk = OzKickflipCurve.PeakJerk;
            tr.firstRate = o.RollRate; tr.firstAcc = o.RollAcc; tr.startedLevel = Math.Abs(o.RollDeg) < 1e-9 && Math.Abs(o.PitchUpDeg) < 1e-9 && Math.Abs(o.HopM) < 1e-9;
            double dPrev = Dt60;
            for (int k = 0; k < 2000; k++)
            {
                double dt = dts(k);
                if (interruptAt >= 0 && tIn >= interruptAt)
                    switch (kind)
                    {
                        case "strafe": i.Strafing = true; break;
                        case "hit": i.Health -= 10; break;
                        case "fire": i.UnderFire = true; break;
                        case "order": i.GoalX = 1800; break;                                            // 37.5 m away: a new order
                        case "turn": i.TurnRateDeg = 25; break;
                        case "descend": i.State = 3; break;
                        case "hover": i.HoverTurn = true; break;
                    }
                var phase0 = s.Phase;
                s.Step(i, dt, out o); tIn += dt;
                if (s.Phase == OzKickflipPhase.Settle && phase0 == OzKickflipPhase.Flip) jerk = Math.Max(jerk, s.SettleJerk);
                double r = o.Active ? o.RollDeg : (Math.Abs(pr) > 180 ? Math.Sign(pr) * 360.0 : 0.0);   // after the end: the level it ended on (360 = 0)
                double v = o.Active ? o.RollRate : 0, a = o.Active ? o.RollAcc : 0;
                double vmax = Math.Max(Math.Abs(pv), Math.Abs(v)), amax = Math.Max(Math.Abs(pa), Math.Abs(a));
                tr.maxDA = Math.Max(tr.maxDA, Math.Abs(r - pr)); tr.boundA = Math.Max(tr.boundA, vmax * dt + 0.5 * amax * dt * dt + jerk * dt * dt * dt / 6.0 + 1e-9);
                tr.maxDV = Math.Max(tr.maxDV, Math.Abs(v - pv)); tr.boundV = Math.Max(tr.boundV, amax * dt + 0.5 * jerk * dt * dt + 1e-9);
                tr.maxDAcc = Math.Max(tr.maxDAcc, Math.Abs(a - pa)); tr.boundAcc = Math.Max(tr.boundAcc, jerk * dt * 1.0001 + 1e-9);
                tr.maxRate = Math.Max(tr.maxRate, Math.Abs(v));
                double fl = o.Active ? o.Flare : 0;                                                     // review K344: the main jets' flare (drawn only while Active)
                tr.maxFlare = Math.Max(tr.maxFlare, fl);
                if (dt > 0)
                {
                    tr.maxPitchStep = Math.Max(tr.maxPitchStep, Math.Abs((o.Active ? o.PitchUpDeg : 0) - pp) / dt);
                    tr.maxHopStep = Math.Max(tr.maxHopStep, Math.Abs((o.Active ? o.HopM : 0) - ph) / dt);
                    tr.maxFlareStep = Math.Max(tr.maxFlareStep, Math.Abs(fl - pf) / dt);
                }
                else if (Math.Abs(r - pr) > 1e-12 || Math.Abs((o.Active ? o.PitchUpDeg : 0) - pp) > 1e-12 || Math.Abs(fl - pf) > 1e-12) tr.pausedMoved = true;
                pf = fl;
                if (!double.IsNaN(pppr) && Math.Abs(dt - Dt60) < 1e-12 && Math.Abs(dPrev - Dt60) < 1e-12 && o.Active)
                {   // the drawn angles' own second difference against the curve's acceleration (60 fps): the numbers are one curve
                    double num = (r - 2 * pr + ppr) / (dt * dt);
                    tr.maxNumErr = Math.Max(tr.maxNumErr, Math.Abs(num - pa) / Math.Max(1.0, OzKickflipCurve.PeakAcc));
                }
                pppr = ppr; ppr = pr; pr = r; pv = v; pa = a; pp = o.Active ? o.PitchUpDeg : 0; ph = o.Active ? o.HopM : 0; dPrev = dt;
                tr.frames++;
                if (!o.Active && (o.Events & OzKickflipState.EvEnd) != 0) { tr.ended = true; tr.endRoll = r; tr.endRate = v; tr.endAcc = a; break; }
            }
            return tr;
        }

        static void Curve()
        {
            sb.AppendLine("-- C the curve");
            double pitchRateMax = 0, hopRateMax = 0, flareRateMax = 0, fPrev = 0;
            for (int k = 0; k <= 4000; k++)
            {
                double t = OzKickflipCurve.EffectsEndSec * k / 4000.0;
                OzKickflipCurve.At(t, out _, out _, out _, out _, out double pv, out _, out _, out double hv, out _, out double fv);
                pitchRateMax = Math.Max(pitchRateMax, Math.Abs(pv)); hopRateMax = Math.Max(hopRateMax, Math.Abs(hv));
                if (k > 0) flareRateMax = Math.Max(flareRateMax, Math.Abs(fv - fPrev) / (OzKickflipCurve.EffectsEndSec / 4000.0));   // the flare's steepest change on the curve (per s)
                fPrev = fv;
            }
            var rnd = new Random(344);
            var clocks = new (string name, Func<int, double> dt)[]
            {
                ("60 fps", k => Dt60), ("30 fps", k => 1.0 / 30.0), ("144 fps", k => 1.0 / 144.0),
                ("jittery 30-144 fps with 0.05 s hitches", k => k % 37 == 11 ? 0.05 : 1.0 / (30.0 + rnd.NextDouble() * 114.0)),
                ("paused frames (0 s) between 60 fps frames", k => k % 3 == 1 ? 0.0 : Dt60),
            };
            foreach (var c in clocks)
            {
                var tr = Run(c.dt);
                bool ok = tr.ended && tr.startedLevel && !tr.pausedMoved && tr.maxDA <= tr.boundA && tr.maxDV <= tr.boundV && tr.maxDAcc <= tr.boundAcc
                          && Math.Abs(Math.Abs(tr.endRoll) - 360.0) < 1e-6 && Math.Abs(tr.endRate) < 1e-9 && Math.Abs(tr.endAcc) < 1e-9 && Math.Abs(tr.firstRate) < 1e-9 && Math.Abs(tr.firstAcc) < 1e-9
                          && tr.maxPitchStep <= pitchRateMax * 1.001 + 1e-6 && tr.maxHopStep <= hopRateMax * 1.001 + 1e-6 && tr.maxFlareStep <= flareRateMax * 1.001 + 1e-6 && (c.name != "60 fps" || tr.maxNumErr < 0.02);
                V(ok, $"C {c.name}: {tr.frames} frames{(c.name.StartsWith("paused") ? (tr.pausedMoved ? ", it MOVED on a paused frame" : ", nothing moves on a paused frame") : "")}, one full turn ({tr.endRoll:F1} deg), level and still at both ends; largest change per frame: angle {tr.maxDA:F2} deg (allowed by its rate {tr.boundA:F2}), rate {tr.maxDV:F1} deg/s (by its acceleration {tr.boundV:F1}), acceleration {tr.maxDAcc:F0} deg/s^2 (by its jerk {tr.boundAcc:F0}); nose {tr.maxPitchStep:F1} deg/s (curve {pitchRateMax:F1}), hop {tr.maxHopStep:F2} m/s (curve {hopRateMax:F2}), the jets' flare {tr.maxFlareStep:F2}/s (curve {flareRateMax:F2}/s, peak {tr.maxFlare:F2}){(c.name == "60 fps" ? $"; the drawn angles' second difference = the curve's acceleration within {tr.maxNumErr * 100:F2} % of its peak" : "")}");
            }
            // every interruption at 35 moments: never a step, always level at the end
            foreach (var kind in new[] { "strafe", "hit", "fire", "order", "turn", "descend", "hover" })
            {
                int n = 0, bad = 0, eased = 0, rolledOn = 0, wrapped360 = 0, wrapped0 = 0, tail = 0; double worstA = 0, worstV = 0, worstAcc = 0, worstF = 0, peak = 0; string worst = "";
                for (int m = 0; m < 40; m++)                                                                     // review K344: 35 -> 40 moments, into the catch's tail (1.45 .. 1.68 s)
                {
                    double at = 0.02 + m * 0.04;
                    var tr = Run(k => Dt60, at, kind); n++;
                    bool lvl = tr.ended && (Math.Abs(tr.endRoll) < 1e-6 || Math.Abs(Math.Abs(tr.endRoll) - 360.0) < 1e-6) && Math.Abs(tr.endRate) < 1e-9 && Math.Abs(tr.endAcc) < 1e-9;
                    bool smooth = tr.maxDA <= tr.boundA && tr.maxDV <= tr.boundV && tr.maxDAcc <= tr.boundAcc && tr.maxFlareStep <= flareRateMax * 1.001 + 1e-6;   // review K344: the jets' flare never snaps off either
                    if (!(lvl && smooth)) { bad++; worst = $" first bad at {at:F2} s (ended {tr.ended}, end {tr.endRoll:F1} deg, angle {tr.maxDA:F2}/{tr.boundA:F2}, rate {tr.maxDV:F1}/{tr.boundV:F1}, acc {tr.maxDAcc:F0}/{tr.boundAcc:F0}, flare {tr.maxFlareStep:F2}/{flareRateMax:F2} per s)"; }
                    worstA = Math.Max(worstA, tr.maxDA / tr.boundA); worstV = Math.Max(worstV, tr.maxDV / tr.boundV); worstAcc = Math.Max(worstAcc, tr.maxDAcc / tr.boundAcc); worstF = Math.Max(worstF, tr.maxFlareStep / flareRateMax); peak = Math.Max(peak, tr.maxRate);
                    bool hard = kind == "descend" || kind == "hover";
                    if (at < OzKickflipCurve.RollStartSec) eased++;
                    else if (at >= OzKickflipCurve.TotalSec) tail++;
                    else if (!hard) rolledOn++;
                    else if (Math.Abs(tr.endRoll) > 180) wrapped360++; else wrapped0++;
                }
                V(bad == 0, $"C interrupted by {(kind == "descend" ? "the descent beginning (the cruise ends)" : kind == "hover" ? "a hover turn" : kind == "order" ? "a new order" : kind)} at 40 moments (0.02 .. 1.58 s): level and still at the end every time, no step (largest share of the allowed change: angle {worstA * 100:F0} %, rate {worstV * 100:F0} %, acceleration {worstAcc * 100:F0} %, the jets' flare {worstF * 100:F0} %); before the roll eased back {eased}; {(kind == "descend" || kind == "hover" ? $"in the roll eased on to 360 {wrapped360}, back to 0 {wrapped0}" : $"in the roll rolled on to the end {rolledOn}")}; in the catch's tail (level already) the effects ran out {tail}; fastest {peak:F0} deg/s{worst}");
            }
        }

        // ================================================================== L: the lamps (lamp fix, review K344)
        sealed class LampAim { public int K; public string Name; public double X, Y, Z; public bool AtLevelThrough; }

        static readonly string[] LampName = { "belly L", "belly R", "nose" };
        /// <summary>Where each lamp's beam starts in front of its head (m: the belly beam cone's own offset, the nose's 0.36 x NoseScale) and
        /// how far it reaches (m: the belly lamps' Range, the nose searchlight's).</summary>
        static readonly double[] LampReach = { 0.02, 0.02, OzKickflipLamps.FieldNoseReach }, LampLen = { 26.0, 26.0, 42.0 };

        static void Norm(ref double x, ref double y, ref double z) { double n = Math.Sqrt(x * x + y * y + z * z); x /= n; y /= n; z /= n; }

        /// <summary>The lamp aims the game gives in flight, in the level ship's root space.</summary>
        static List<LampAim> LampAims(OzKickflipShipModel m)
        {
            var l = new List<LampAim>();
            const double feet = 12.0;                                                                       // the pads over the ground at cruise height (ZWorld.ShuttleCruiseM)
            for (int k = 0; k < 2; k++)
            {
                string side = k == 0 ? "L" : "R"; var p = m.Markers["MK_Search_" + side]; var q = m.Markers["MK_SearchAim_" + side];
                double hx = q[0] - p[0], hy = q[1] - p[1], hz = q[2] - p[2]; Norm(ref hx, ref hy, ref hz);
                l.Add(new LampAim { K = k, Name = "belly " + side + " housing", X = hx, Y = hy, Z = hz });
                void Landing(string name, double tx, double ty, double tz)
                {   // OzShuttleSearchlights.AimLanding: onto the point, never more than 55 deg off the housing's aim (a slerp)
                    double dx = tx - p[0], dy = ty - p[1], dz = tz - p[2]; Norm(ref dx, ref dy, ref dz);
                    double ang = OzKickflipLamps.Angle(hx, hy, hz, dx, dy, dz) * Math.PI / 180.0, lim = 55.0 * Math.PI / 180.0;
                    if (ang > lim)
                    {
                        double kk = lim / ang, so = Math.Sin(ang), w0 = Math.Sin((1 - kk) * ang) / so, w1 = Math.Sin(kk * ang) / so;
                        dx = w0 * hx + w1 * dx; dy = w0 * hy + w1 * dy; dz = w0 * hz + w1 * dz; Norm(ref dx, ref dy, ref dz);
                    }
                    l.Add(new LampAim { K = k, Name = name, X = dx, Y = dy, Z = dz });
                }
                double sp = k == 0 ? 3.0 : -3.0;                                                            // OzShuttleNightLights.LandingSpreadM: lamp 0 the front, lamp 1 the rear
                Landing($"belly {side} after the take-off", 0, -feet, sp);
                foreach (var D in new[] { 10.0, 16.0, 24.0 }) Landing($"belly {side} approach {D:F0} m", 0, -feet, D + sp);
            }
            void Nose(string name, double yaw, double pitch)
            {
                double y = yaw * Math.PI / 180.0, pt = pitch * Math.PI / 180.0;
                l.Add(new LampAim { K = OzKickflipLamps.Nose, Name = name, X = Math.Sin(y) * Math.Cos(pt), Y = -Math.Sin(pt), Z = Math.Cos(y) * Math.Cos(pt) });
            }
            foreach (var yaw in new[] { -28.0, 0.0, 28.0 }) foreach (var pitch in new[] { 27.0, 33.0 }) Nose($"nose sweep {yaw:F0}/{pitch:F0}", yaw, pitch);   // OzShuttleNightPlan: +-28 deg, 30 +- 3 under the horizon
            foreach (var (yaw, pitch) in new[] { (60.0, 6.0), (90.0, 6.0), (90.0, 30.0), (110.0, 40.0), (125.0, 55.0), (0.0, 80.0), (60.0, 55.0), (-90.0, 20.0), (125.0, 6.0), (125.0, 30.0) })
                Nose($"nose lock {yaw:F0}/{pitch:F0}", yaw, pitch);                                         // on an enemy: up to 110 + 15 deg off the nose, 6 .. 80 under the horizon
            return l;
        }

        /// <summary>The flip's drawn (roll, pitchUp) frame by frame on a clock (dts), from the first frame of the pop to the end; with an
        /// interruption at `at` s (as Run).</summary>
        static List<(double roll, double pitch, double dt)> Frames(int id, Func<int, double> dts, double at = -1, string kind = "")
        {
            var s = new OzKickflipState(id); s.Click(true, true, 300.0, 1500, 900);
            var i = SteadyIn(1500, 900, 300.0); OzKickflipOut o = default; int guard = 0;
            while (s.Phase != OzKickflipPhase.Flip && guard++ < 1000) s.Step(i, Dt60, out o);
            var l = new List<(double, double, double)> { (o.RollDeg, o.PitchUpDeg, 0.0) }; double tIn = 0;
            for (int k = 0; k < 20000; k++)
            {
                double dt = dts(k);
                if (at >= 0 && tIn >= at) { if (kind == "order") i.GoalX = 1800; else if (kind == "descend") i.State = 3; }
                s.Step(i, dt, out o); tIn += dt;
                if (!o.Active) { l.Add((0.0, 0.0, dt)); break; }                                           // level again (360 = 0)
                l.Add((o.RollDeg, o.PitchUpDeg, dt));
            }
            return l;
        }

        sealed class LampRun { public int Frames, Visible, Through, VisibleThrough, PreThrough; public double MaxStep, MinFade = 1, FirstFade = 1, LastFade = 1, Out0 = 999, Out1 = -999; public bool PausedMoved; public string Worst = ""; }

        static LampRun RunLamp(OzKickflipShipModel m, OzKickflipShipModel.Probe[] pr, LampAim a, List<(double roll, double pitch, double dt)> fr, bool cones)
        {
            var r = new LampRun(); double prev = double.NaN; var o = OzKickflipLamps.FieldOrigin[a.K]; double half = OzKickflipLamps.FieldHalfDeg[a.K];
            for (int n = 0; n < fr.Count; n++)
            {
                var (roll, pitch, dt) = fr[n];
                double f = OzKickflipLamps.Fade(a.K, a.X, a.Y, a.Z, roll, pitch);
                if (n == 0) r.FirstFade = f; r.LastFade = f; r.MinFade = Math.Min(r.MinFade, f); r.Frames++;
                if (!double.IsNaN(prev)) { double st = Math.Abs(f - prev); if (st > r.MaxStep) { r.MaxStep = st; r.Worst = $"{roll:F0} deg"; } if (dt <= 0 && st > 1e-12) r.PausedMoved = true; }
                prev = f;
                if (f <= 0.002) { double rr = ((roll % 360) + 360) % 360; r.Out0 = Math.Min(r.Out0, rr); r.Out1 = Math.Max(r.Out1, rr); }
                if (!cones) continue;
                OzKickflipLamps.Unflip(roll, pitch, a.X, a.Y, a.Z, out double dx, out double dy, out double dz);
                double ox = o[0] + LampReach[a.K] * dx, oy = o[1] + LampReach[a.K] * dy, oz = o[2] + LampReach[a.K] * dz;
                bool through = m.Cone(pr[a.K], ox, oy, oz, dx, dy, dz, half, 0.004, LampLen[a.K]);
                if (f > 0.002) r.Visible++;
                if (a.AtLevelThrough) { if (through) r.PreThrough++; continue; }
                if (through) { r.Through++; if (f > 0.002) r.VisibleThrough++; }
            }
            return r;
        }

        static void Lamps(string root)
        {
            sb.AppendLine("-- L the lamps: no beam through the ship while it flips (the ship's own model files)");
            var t0 = DateTime.Now;
            var m = root != null ? OzKickflipShipModel.Load(root) : null;
            if (m == null || m.Error != null || !m.Markers.ContainsKey("MK_Search_L") || !m.Markers.ContainsKey("MK_Search_R"))
            { V(false, "L the ship's model files could not be read: " + (m == null ? "no project folder given" : m.Error ?? "the door kit's lamp markers are missing")); return; }
            // L1 the model; the field's lamps where the game's are
            var mL = m.Markers["MK_Search_L"]; var mR = m.Markers["MK_Search_R"];
            double Off(double[] a, double[] b) => Math.Sqrt((a[0] - b[0]) * (a[0] - b[0]) + (a[1] - b[1]) * (a[1] - b[1]) + (a[2] - b[2]) * (a[2] - b[2]));
            double offL = Off(mL, OzKickflipLamps.FieldOrigin[0]), offR = Off(mR, OzKickflipLamps.FieldOrigin[1]);
            double beamHalf = Math.Atan(Math.Tan(12.5 * Math.PI / 180.0) * 1.2) * 180.0 / Math.PI, noseHalf = Math.Atan(Math.Tan(11.0 * Math.PI / 180.0) * 1.2) * 180.0 / Math.PI;
            bool cones = Math.Abs(OzKickflipLamps.FieldHalfDeg[0] - beamHalf) < 0.01 && Math.Abs(OzKickflipLamps.FieldHalfDeg[1] - beamHalf) < 0.01 && Math.Abs(OzKickflipLamps.FieldHalfDeg[2] - noseHalf) < 0.01;
            V(offL < 0.002 && offR < 0.002 && cones && m.Tris > 15000,
              $"L1 the model: {m.Tris} triangles in the beams' way ({string.Join(", ", m.PartTris.Select(kv => kv.Key + " " + kv.Value))}, and the nose searchlight's head for the belly lamps) from {OzKickflipShipModel.ShipFbx} and {OzKickflipShipModel.KitFbx}; the field's belly lamps at the door kit's markers ({offL * 1000:F1} / {offR * 1000:F1} mm off), its cones the drawn beams' (belly {beamHalf:F2} deg, nose {noseHalf:F2} deg: atan(tan(half the light's angle) x 1.2))");
            var pr = new OzKickflipShipModel.Probe[3];
            for (int k = 0; k < 3; k++) { var o = OzKickflipLamps.FieldOrigin[k]; pr[k] = m.ProbeFor(o[0], o[1], o[2], k == OzKickflipLamps.Nose ? 0.2 : 0.06, k != OzKickflipLamps.Nose); }
            // L2 the field against the model: every direction on a 2 deg grid, per lamp: where the field lets any light out, the cone is clear
            int allowed = 0, wrong = 0, clear = 0, stricter = 0; string firstWrong = "";
            for (int k = 0; k < 3; k++)
            {
                var o = OzKickflipLamps.FieldOrigin[k];
                for (int j = 0; j <= 90; j++)
                    for (int i = 0; i < 180; i++)
                    {
                        double ph = (-180.0 + 2 * i + 1) * Math.PI / 180.0, la = (-90.0 + 2 * j) * Math.PI / 180.0;
                        double dx = Math.Cos(la) * Math.Sin(ph), dy = -Math.Cos(la) * Math.Cos(ph), dz = Math.Sin(la);
                        bool lets = OzKickflipLamps.FadeOfMargin(OzKickflipLamps.Margin(k, dx, dy, dz)) > 0;
                        bool hit = m.Cone(pr[k], o[0] + LampReach[k] * dx, o[1] + LampReach[k] * dy, o[2] + LampReach[k] * dz, dx, dy, dz, OzKickflipLamps.FieldHalfDeg[k], 0.004, LampLen[k]);
                        if (!hit) clear++;
                        if (lets) { allowed++; if (hit) { wrong++; if (firstWrong == "") firstWrong = $" (first: {LampName[k]} phi {ph * 180 / Math.PI:F0} lat {la * 180 / Math.PI:F0})"; } }
                        else if (!hit) stricter++;
                    }
            }
            V(wrong == 0 && allowed > 1000, $"L2 the field against the model: {3 * 91 * 180} directions (2 deg grid, the three lamps): where the field lets any light out ({allowed}) the model's own cone test finds the cone clear every time ({wrong} not){firstWrong}; of the {clear} clear directions the field keeps {stricter} dark (its {OzKickflipLamps.SafetyDeg:F0} deg safety and the grid)");
            // L3 .. L5 the flips at 60 / 30 / 144 fps, every aim, both roll directions
            var aims = LampAims(m);
            foreach (var a in aims)
            {
                var o = OzKickflipLamps.FieldOrigin[a.K];
                a.AtLevelThrough = m.Cone(pr[a.K], o[0] + LampReach[a.K] * a.X, o[1] + LampReach[a.K] * a.Y, o[2] + LampReach[a.K] * a.Z, a.X, a.Y, a.Z, OzKickflipLamps.FieldHalfDeg[a.K], 0.004, LampLen[a.K]);
            }
            var pre = aims.Where(a => a.AtLevelThrough).Select(a => a.Name).ToList();
            var noseSweepMin = 1.0; double bellyOut0 = 999, bellyOutLate = -999; int L = 0;
            foreach (var (name, dtf) in new (string, Func<int, double>)[] { ("60 fps", k => Dt60), ("30 fps", k => 1.0 / 30.0), ("144 fps", k => 1.0 / 144.0) })
            {
                int frames = 0, visible = 0, through = 0, visThrough = 0, preThrough = 0, ends = 0, endsOk = 0; double maxStep = 0; string worst = "", firstBad = "";
                foreach (var a in aims)
                    foreach (int id in new[] { 1, 2 })
                    {
                        var r = RunLamp(m, pr, a, Frames(id, dtf), true);
                        frames += r.Frames; visible += r.Visible; through += r.Through; visThrough += r.VisibleThrough; preThrough += r.PreThrough;
                        ends++; if (Math.Abs(r.FirstFade - 1) < 1e-9 && Math.Abs(r.LastFade - 1) < 1e-6) endsOk++;
                        if (r.MaxStep > maxStep && !a.AtLevelThrough) { maxStep = r.MaxStep; worst = $"{a.Name}, {(id == 1 ? "+" : "-")} roll, at {r.Worst}"; }
                        if (r.VisibleThrough > 0 && firstBad == "") firstBad = $"; FIRST: {a.Name} {(id == 1 ? "+" : "-")} roll, {r.VisibleThrough} frames";
                        if (name == "60 fps" && a.K == OzKickflipLamps.Nose && a.Name.Contains("sweep")) noseSweepMin = Math.Min(noseSweepMin, r.MinFade);
                        if (name == "60 fps" && a.K < 2 && r.Out0 < 999) { double first = Math.Min(r.Out0, 360 - r.Out1); bellyOut0 = Math.Min(bellyOut0, first); bellyOutLate = Math.Max(bellyOutLate, first); }
                    }
                L++;
                V(visThrough == 0 && through > 0 && endsOk == ends,
                  $"L{2 + L} {name}: {aims.Count} lamp aims x both roll directions ({ends} flips, {frames} frames, {visible} with the lamp drawn): frames with a drawn beam through the ship {visThrough} (the builder's gimbal alone: {through} frames through the hull, a pod, a leg or a housing){firstBad}; the fade 1 at both ends of every flip ({endsOk} of {ends}); its largest change in one frame {maxStep:F2} ({worst}; the hull turns up to {OzKickflipCurve.PeakRate * (name == "60 fps" ? Dt60 : name == "30 fps" ? 1.0 / 30.0 : 1.0 / 144.0):F1} deg in one such frame, L6: no step)");
            }
            sb.AppendLine($"   (the belly lamps are out from about {bellyOut0:F0} .. {bellyOutLate:F0} deg of roll to {360 - bellyOutLate:F0} .. {360 - bellyOut0:F0} deg, by their aim (60 fps); the nose searchlight's sweep is never in the ship's way and dims to {noseSweepMin:F2} at the most mid-roll, where the hull comes near its cone)");
            if (pre.Count > 0) sb.AppendLine($"   (not the flip's doing, unchanged: {string.Join(", ", pre)} already meet the ship at level: a lock on an enemy far behind the ship's side at a shallow angle; through those in the flip they ease out with the hull's turn)");
            // L6 no step: at 1000 fps (a step would show its whole size in one 1 ms frame) and on paused frames
            double step1k = 0, stepJit = 0; bool paused = false; var rnd = new Random(345);
            foreach (var a in aims)
                foreach (int id in new[] { 1, 2 })
                {
                    if (a.AtLevelThrough) continue;
                    step1k = Math.Max(step1k, RunLamp(m, pr, a, Frames(id, k => 0.001), false).MaxStep);
                    var rp = RunLamp(m, pr, a, Frames(id, k => k % 3 == 1 ? 0.0 : (k % 37 == 11 ? 0.05 : 1.0 / (30.0 + rnd.NextDouble() * 114.0))), false);
                    paused |= rp.PausedMoved; stepJit = Math.Max(stepJit, rp.MaxStep);
                }
            V(step1k <= 0.06 && !paused, $"L6 no step: at 1000 fps the fade's largest change in one 1 ms frame {step1k:F3} (a step would show its whole size there; the old on / off: 1.0); on a jittery clock with 0.05 s hitches {stepJit:F2}; on paused frames it {(paused ? "MOVED" : "never moves")} (a function of the drawn pose)");
            // L7 the interruptions at 60 fps: a new order in the pop (eased back), the descent beginning in the roll (eased to the nearer level)
            int iFrames = 0, iVis = 0, iEnds = 0, iEndsOk = 0; double iStep = 0;
            foreach (var (kind, at) in new[] { ("order", 0.06), ("descend", 0.45), ("descend", 0.75), ("descend", 1.05), ("descend", 1.35) })
                foreach (var a in aims)
                    foreach (int id in new[] { 1, 2 })
                    {
                        var r = RunLamp(m, pr, a, Frames(id, k => Dt60, at, kind), true);
                        iFrames += r.Frames; iVis += r.VisibleThrough; iEnds++; if (Math.Abs(r.LastFade - 1) < 1e-6) iEndsOk++;
                        if (!a.AtLevelThrough) iStep = Math.Max(iStep, r.MaxStep);
                    }
            V(iVis == 0 && iEndsOk == iEnds, $"L7 interrupted flips at 60 fps (a new order in the pop: eased back; the descent beginning at 0.45 / 0.75 / 1.05 / 1.35 s: eased to the nearer level), every aim, both directions ({iEnds} flips, {iFrames} frames): drawn beams through the ship {iVis}, the fade 1 at the end {iEndsOk} of {iEnds}, its largest change in one frame {iStep:F2}");
            // L8 the ramp light (it burns only on the ground): out by the hull's turn alone
            double r0 = OzKickflipLamps.Fade(OzKickflipLamps.Ramp, 0, -0.8, -0.6, 0, 0), r10 = OzKickflipLamps.Fade(OzKickflipLamps.Ramp, 0, -0.8, -0.6, 10, 0), r30 = OzKickflipLamps.Fade(OzKickflipLamps.Ramp, 0, -0.8, -0.6, 30, 0), r180 = OzKickflipLamps.Fade(OzKickflipLamps.Ramp, 0, -0.8, -0.6, 180, 0);
            V(Math.Abs(r0 - 1) < 1e-12 && r10 < 1 && r10 > 0 && r30 < 1e-9 && r180 < 1e-9, $"L8 the ramp light (it burns only while the ramp is down; kept on its aim as built): faded by the hull's turn alone: 1 at level, {r10:F2} at 10 deg of roll, 0 once the hull has turned {OzKickflipLamps.RampTurnOutDeg:F0} deg against it (30 deg of roll: {r30:F2}) and at 180 ({r180:F2}) ({(DateTime.Now - t0).TotalSeconds:F0} s for L)");
        }

        // ================================================================== R: the rules untouched
        /// <summary>One click of the script: the moment (s), the units (indexes into the scene's list), the ground point (px), plain.</summary>
        struct Click { public double at; public int[] who; public int x, y; public bool plain; public string why; }

        sealed class Scene
        {
            public ZWorld W; public List<ZObject> Units = new List<ZObject>(); public List<Click> Clicks = new List<Click>();
            public readonly List<long> Hashes = new List<long>(); public readonly StringBuilder Log = new StringBuilder();
            public Dictionary<int, OzKickflipState> Flip = new Dictionary<int, OzKickflipState>();
            public Dictionary<int, double[]> ViewTwin = new Dictionary<int, double[]>();
            public int FlipFramesOutside, FlipFrames, Starts, Ends, Aborts, Wraps, Kills, StartedBraking;
            /// <summary>Lamp fix (review K344): flip pose frames while the ship's own sun shadow may show (K256g: on the ground, and for its
            /// 0.5 s cross-fade after a touchdown or a lift-off), the least time aloft at a flip pose frame (s), the time aloft per ship.</summary>
            public int ShadowFrames; public double MinAirAtFlip = 1e9; public Dictionary<int, double> AirT = new Dictionary<int, double>();
            public double MinLeftAtEnd = 1e9; public List<string> Flips = new List<string>();
            public Action<Scene, double> Extra; public bool LogSingles;
            public Dictionary<int, Dictionary<string, int>> Why = new Dictionary<int, Dictionary<string, int>>();
        }

        static void Replay(string settings, string l16Map, int matchSecs)
        {
            sb.AppendLine("-- R the rules untouched (the same world and clicks, the flip's twin on and off)");
            using (OzV11Kit.GameSwitches())
            {
                ZWorld.ShuttlePlayableDeviation = true; ZWorld.ShuttleOn = true;
                // the plain map: a probe run with the twin finds the moments of the two interruptions, then off / on with the same script
                var probe = PlainScene(settings, null); Play(probe, true, PlainSecs);
                double tAbort = Moment(probe, PopClickAt, 0.06), tRoll = Moment(probe, RollClickAt, 0.70);
                var off = PlainScene(settings, (tAbort, tRoll)); Play(off, false, PlainSecs);
                var on = PlainScene(settings, (tAbort, tRoll)); Play(on, true, PlainSecs);
                Compare(off, on, $"plain map, two shuttles and a jeep, {PlainSecs:F0} s (the new orders in the pop at {tAbort:F2} s and in the roll at {tRoll:F2} s)");
                sb.Append(on.Log);
                var a = on.Flip.ContainsKey(on.Units[0].Id) ? on.Flip[on.Units[0].Id] : null; var b = on.Flip.ContainsKey(on.Units[1].Id) ? on.Flip[on.Units[1].Id] : null;
                V(a != null && b != null && !on.Flip.ContainsKey(on.Units[2].Id), "R the group's double click: each shuttle got its own flip state, the jeep none");
                V(a != null && a.NearDoubles == 1, $"R the near double click (12 m): nothing (shuttle A: {a?.NearDoubles} near double, armed {a?.Arms} times in all)");
                double lastArm = 198.23, lastStart = on.Flips.Where(f => f.StartsWith("A ")).Select(f => double.Parse(f.Split(' ')[2], System.Globalization.CultureInfo.InvariantCulture)).DefaultIfEmpty(-1).Last();
                V(on.Units[0].ShuttleStrafes > 0 && lastStart > lastArm + 6.0, $"R the rockets beside its last way: shuttle A strafed {on.Units[0].ShuttleStrafes} time(s), hit for {(on.Units[0].MaxHealth - on.Units[0].Health)} HP; its flip waited for the fire and the strafe to end (started {lastStart - lastArm:F1} s after the double click; a quiet flight starts about 4.4 s after it)");
                V(on.Starts >= 6 && on.Aborts >= 1 && on.FlipFramesOutside == 0 && on.StartedBraking == 0,
                  $"R {on.Starts} flips started, {on.Ends} rolled to the end, {on.Aborts} eased back before the roll (the new order in the pop), {on.Wraps} eased to level, {on.Kills} lost with the ship; every flip frame in the cruise at cruise height ({on.FlipFrames} frames, outside {on.FlipFramesOutside}); none started braking; the least way left at a flip's end before the planned stop: {(on.MinLeftAtEnd < 1e8 ? on.MinLeftAtEnd.ToString("F1") : "-")} m");
                if (l16Map != null)
                {
                    var m1 = MatchScene(settings, l16Map); Play(m1, false, matchSecs);
                    var m2 = MatchScene(settings, l16Map); Play(m2, true, matchSecs);
                    Compare(m1, m2, $"Level 16 against the computer, {matchSecs} s, the shuttle double-clicked to a far landable spot every 20 s");
                    sb.Append(m2.Log);
                    V(m2.FlipFramesOutside == 0, $"R Level 16: {m2.Starts} flips started, {m2.Ends} to the end, {m2.Aborts} eased back, {m2.Wraps} eased to level, {m2.Kills} lost with the ship; frames outside the cruise {m2.FlipFramesOutside}");
                    on.ShadowFrames += m2.ShadowFrames; on.MinAirAtFlip = Math.Min(on.MinAirAtFlip, m2.MinAirAtFlip);
                }
                V(on.ShadowFrames == 0 && on.MinAirAtFlip > 0.55 && on.MinAirAtFlip < 1e8, $"R the ship's own sun shadow (K256g: none in the air; it eases in over 0.5 s at a touchdown, out at a lift-off) never shows during a flip: flip pose frames on the ground or within 0.5 s of leaving it {on.ShadowFrames} (the least time aloft at a flip frame {on.MinAirAtFlip:F1} s); the landing mark is laid from the level pose (as the frame and the health bar)");
            }
        }

        const double PlainSecs = 240, PopClickAt = 110, RollClickAt = 155;
        static double Moment(Scene probe, double after, double into)
        {
            // the probe's first flip start of shuttle A after `after` s, + `into` s (the clicks are on 60 fps frames)
            var starts = probe.Flips.Where(f => f.StartsWith("A ")).Select(f => double.Parse(f.Split(' ')[2], System.Globalization.CultureInfo.InvariantCulture)).Where(s => s > after).ToList();
            return starts.Count > 0 ? starts[0] + into : -1;
        }

        static Scene PlainScene(string settings, (double abort, double roll)? at)
        {
            var sc = new Scene { LogSingles = true };
            var w = sc.W = ZWorld.Create(settings, OzV11Kit.Plain(128, 128), 344); w.Events.Clear();
            OzV11Kit.Dummy(w, 6 * 16, 120 * 16);
            var sA = w.CreatePlayableShuttle(30 * 16, 64 * 16, Team.Red, 0f); var sB = w.CreatePlayableShuttle(30 * 16, 76 * 16, Team.Red, 0f);
            var jeep = OzV11Kit.Place(w, "jeep", 26 * 16, 70 * 16, Team.Red);
            sc.Units.Add(sA); sc.Units.Add(sB); sc.Units.Add(jeep);
            void C(double t, int[] who, int tx, int ty, string why, bool dbl = true, double gap = 0.22)
            {
                sc.Clicks.Add(new Click { at = t, who = who, x = tx * 16 + 8, y = ty * 16 + 8, plain = true, why = why });
                if (dbl) sc.Clicks.Add(new Click { at = t + gap, who = who, x = tx * 16 + 8 + 3, y = ty * 16 + 8, plain = true, why = why });
            }
            // A's flights (5.25 m/s: about 2 s up, d / 5.25 + 2.3 s across, 2 s down): each next click well after it has landed
            C(1.0, new[] { 0, 1, 2 }, 100, 64, "the group (A, B, the jeep): a far double click, 140 m");               // lands about 35 s
            C(40.0, new[] { 0 }, 106, 64, "A: a near double click, 12 m");                                             // lands about 49 s
            C(52.0, new[] { 0 }, 60, 40, "A: a slow double click (0.5 s apart), 104 m", true, 0.5);                    // lands about 79 s
            C(82.0, new[] { 0 }, 20, 20, "A: a far double click, 89 m");                                               // lands about 106 s
            C(PopClickAt, new[] { 0 }, 100, 30, "A: a far double click, 161 m (a new order comes in its pop)");
            if (at.HasValue && at.Value.abort > 0) C(at.Value.abort, new[] { 0 }, 100, 100, "A: the new order in the pop", false);   // lands about 150 s
            C(RollClickAt, new[] { 0 }, 20, 110, "A: a far double click, 161 m (a new order comes in its roll)");
            if (at.HasValue && at.Value.roll > 0) C(at.Value.roll, new[] { 0 }, 110, 110, "A: the new order in the roll", false);    // lands about 195 s
            C(198.0, new[] { 0 }, 110, 20, "A: a far double click, 180 m, rockets under the way");
            sc.Extra = (s, now) =>
            {   // rockets beside the start of A's last way (tough robots, blue, they cannot die), from its take-off
                if (now >= 199.0 && s.W.Objects.All(o => o.Type != "tough" || o.Owner != Team.Blue))
                {
                    var a = s.Units[0]; if (!a.Destroyed) a.MaxHealth = a.Health = 60000;                                   // it must live through them (both runs alike)
                    var t = s.W.CreateRobotGroup("tough", 107 * 16, 100 * 16, Team.Blue, 3);
                    if (t != null) { t.MaxHealth = t.Health = 1000000; foreach (var mm in t.Minions) if (mm != null) mm.MaxHealth = mm.Health = 1000000; }
                }
            };
            return sc;
        }

        static Scene MatchScene(string settings, string l16Map)
        {
            var sc = new Scene();
            var w = sc.W = ZWorld.Create(settings, l16Map, 16); w.Bots.Add(new ZBot(w, Team.Blue, 117)); w.Events.Clear();
            var ship = w.PlayableShuttle ?? w.CreatePlayableShuttle(40 * 16, 40 * 16, Team.Red, -75f);
            sc.Units.Add(ship);
            var rng = new Random(1616); int x0 = ship.CenterX, y0 = ship.CenterY;
            for (double t = 3.0; t < 1e4; t += 20.0)
            {   // a far landable spot (40 .. 110 m from the last one), the same in both runs (the rules' own pad test, read only, at script time)
                int[] spot = null;
                for (int tries = 0; tries < 400 && spot == null; tries++)
                {
                    double a = rng.NextDouble() * Math.PI * 2, r = (40 + rng.NextDouble() * 70) * 8;
                    var (x, y) = w.ShuttleClamp(x0 + (int)(Math.Cos(a) * r), y0 + (int)(Math.Sin(a) * r));
                    if (w.ShuttlePadFree(x, y, ship, out _)) spot = new[] { x, y };
                }
                if (spot == null) continue;
                sc.Clicks.Add(new Click { at = t, who = new[] { 0 }, x = spot[0], y = spot[1], plain = true, why = "far" });
                sc.Clicks.Add(new Click { at = t + 0.2, who = new[] { 0 }, x = spot[0], y = spot[1], plain = true, why = "far" });
                x0 = spot[0]; y0 = spot[1];
                if (t > 2000) break;
            }
            return sc;
        }

        /// <summary>Plays the scene for `secs` at 60 fps the way the game does: the rules' ticks of each frame (ZGame), then the frame's
        /// clicks (PlayerInput: the gesture first, then the orders), then the views (the flip's twin: the game's own state machine).</summary>
        static void Play(Scene sc, bool twin, double secs)
        {
            var w = sc.W; double acc = 0, now = 0; int ci = 0; var gesture = new OzKickflipGesture();
            var ids = new int[OzKickflipGesture.MaxShips]; var inBoth = new bool[OzKickflipGesture.MaxShips];
            var clicked = new Dictionary<int, (bool dbl, int x, int y)>();
            var order = sc.Clicks.OrderBy(c => c.at).ToList();
            while (now < secs && !w.GameOver)
            {
                now += Dt60; acc += Dt60;
                while (acc >= ZConst.TICK - 1e-12) { acc -= ZConst.TICK; w.Tick(ZConst.TICK); sc.Hashes.Add(Hash(w)); }
                w.Events.Clear();
                sc.Extra?.Invoke(sc, now);
                clicked.Clear();
                while (ci < order.Count && order[ci].at <= now + 1e-9)
                {
                    var c = order[ci++];
                    if (twin)
                    {
                        int n = 0; foreach (int u in c.who) if (sc.Units[u].IsShuttle && !sc.Units[u].Destroyed) ids[n++] = sc.Units[u].Id;
                        bool dbl = gesture.Click(now, c.x * 1.25, c.y * 1.25, c.x / 8.0, -c.y / 8.0, 1.0, c.plain, ids, n, inBoth);   // the screen ~10 px per m at 70 m
                        for (int k = 0; k < n; k++) clicked[ids[k]] = (dbl && inBoth[k], c.x, c.y);
                    }
                    foreach (int u in c.who)
                    {
                        var o = sc.Units[u]; if (o.Destroyed) continue;
                        w.SendWaypoints(o.Owner, o, new[] { ZWaypoint.Move(c.x, c.y, true) });
                    }
                    if (twin)
                        foreach (var kv in clicked)
                        {
                            var o = w.Get(kv.Key); if (o == null) continue;
                            if (!sc.Flip.TryGetValue(o.Id, out var st)) sc.Flip[o.Id] = st = new OzKickflipState(o.Id);
                            var (cx, cy) = w.ShuttleClamp(kv.Value.x, kv.Value.y);
                            bool took = o.ShuttleGoalX == cx && o.ShuttleGoalY == cy;
                            double d = Math.Sqrt(Math.Pow(o.ShuttleGoalX - o.ShuttleBaseX, 2) + Math.Pow(o.ShuttleGoalY - o.ShuttleBaseY, 2)) / 8.0;
                            int nd0 = st.NearDoubles, arm0 = st.Arms, dis0 = st.Disarms;
                            st.Click(kv.Value.dbl, took, d, o.ShuttleGoalX, o.ShuttleGoalY);
                            if (!kv.Value.dbl && sc.LogSingles) sc.Log.AppendLine($"     {now,7:F2} s  {Name(sc, o)} single order click{(st.Disarms > dis0 ? ": its armed flip disarmed" : "")}");
                            if (kv.Value.dbl) sc.Log.AppendLine($"     {now,7:F2} s  {Name(sc, o)} double click: {(took ? "taken" : "not taken")}, {d:F1} m{(st.Arms > arm0 ? ": ARMED" : st.NearDoubles > nd0 ? ": near, nothing" : "")}");
                        }
                    clicked.Clear();
                }
                if (!twin) continue;
                foreach (var o in sc.Units) if (o.IsShuttle) sc.AirT[o.Id] = o.ShuttleAirborne ? (sc.AirT.TryGetValue(o.Id, out var a0) ? a0 : 0) + Dt60 : 0;   // the time aloft (lamp fix: K256g)
                foreach (var o in sc.Units)
                {
                    if (!o.IsShuttle || !sc.Flip.TryGetValue(o.Id, out var st)) continue;
                    var i = TwinIn(sc, o, Dt60);
                    int s0 = st.Starts, e0 = st.Ends, a0 = st.Aborts, w0 = st.Wraps, k0 = st.Kills, d0 = st.Disarms; bool armed = st.Phase == OzKickflipPhase.Armed;
                    if (armed)
                    {   // while it waits: why it is not steady yet (the checks' log)
                        var why = OzKickflipState.WhyNot(i) ?? "steady";
                        if (!sc.Why.TryGetValue(o.Id, out var hw)) sc.Why[o.Id] = hw = new Dictionary<string, int>();
                        hw[why] = hw.TryGetValue(why, out var c0) ? c0 + 1 : 1;
                    }
                    st.Step(i, Dt60, out var k);
                    if (Trace && armed && ((int)Math.Round(now * 60)) % 60 == 0) sc.Log.AppendLine($"       trace {now,7:F2} s {Name(sc, o)} {st.Phase} {(OzKickflipState.WhyNot(i) ?? "steady")} state {o.ShuttleState} v {o.ShuttleSpeedPx / 8:F2} lift {o.ShuttleLift:F2} goal {o.ShuttleGoalX},{o.ShuttleGoalY} arm {st.ArmX},{st.ArmY} lean {i.LeanDeg:F1} bank {i.BankDeg:F1}");
                    if (st.Disarms > d0 && st.Starts == s0 && sc.Why.TryGetValue(o.Id, out var hw2))
                    {
                        sc.Log.AppendLine($"     {now,7:F2} s  {Name(sc, o)} disarmed without a flip ({(i.State == 2 ? "a new order" : "its cruise ended")}); while armed: {string.Join(", ", hw2.OrderByDescending(x => x.Value).Select(x => x.Key + " " + (x.Value / 60.0).ToString("F1") + " s"))}");
                    }
                    if (st.Starts > s0 || st.Disarms > d0) sc.Why.Remove(o.Id);
                    if (st.Starts > s0) { sc.Starts++; if (i.Braking) sc.StartedBraking++; sc.Flips.Add($"{Name(sc, o)} start {now:F4} {i.SpeedMps:F2} {i.RemainM:F1}"); sc.Log.AppendLine($"     {now,7:F2} s  {Name(sc, o)} flip starts (dir {st.Dir}): cruise at {i.SpeedMps:F2} m/s ({i.SpeedMps / i.VmaxMps * 100:F0} % of top), {i.RemainM:F1} m to go, the planned stop {i.BrakeM:F1} m"); }
                    if (k.Active && st.Phase == OzKickflipPhase.Flip && st.T <= OzKickflipCurve.TotalSec) { sc.FlipFrames++; if (i.State != 2 || !i.AtHeight) sc.FlipFramesOutside++; }
                    if (k.Active)
                    {   // lamp fix (review K344): K256g's own sun shadow is off in the air (eased over 0.5 s after a lift-off or a touchdown)
                        double air = sc.AirT.TryGetValue(o.Id, out var at0) ? at0 : 0;
                        sc.MinAirAtFlip = Math.Min(sc.MinAirAtFlip, air); if (!o.ShuttleAirborne || air < 0.5 + 0.05) sc.ShadowFrames++;
                    }
                    if (st.Ends > e0) { sc.Ends++; sc.MinLeftAtEnd = Math.Min(sc.MinLeftAtEnd, i.RemainM - i.BrakeM); sc.Log.AppendLine($"     {now,7:F2} s  {Name(sc, o)} flip rolled to the end, {i.RemainM - i.BrakeM:F1} m left before the planned stop"); }
                    if (st.Aborts > a0) { sc.Aborts++; sc.Log.AppendLine($"     {now,7:F2} s  {Name(sc, o)} flip eased back to level before the roll ({(i.Strafing ? "a strafe" : i.UnderFire ? "fire at it" : "a new order or a turn")})"); }
                    if (st.Wraps > w0) { sc.Wraps++; sc.Log.AppendLine($"     {now,7:F2} s  {Name(sc, o)} flip eased to the nearer level (the cruise ended under it)"); }
                    if (st.Kills > k0) { sc.Kills++; sc.Log.AppendLine($"     {now,7:F2} s  {Name(sc, o)} lost during its flip"); }
                }
            }
            sc.Log.Insert(0, $"   {(twin ? "with" : "without")} the twin: {sc.Hashes.Count} ticks, {sc.Clicks.Count} clicks\n");
            foreach (var o in sc.Units)
                if (o.IsShuttle) sc.Log.AppendLine($"   {Name(sc, o)} at the end: {(o.Destroyed || o.KillAt >= 0 ? "DESTROYED" : "alive, " + o.Health + " HP")}, {o.ShuttleFlights} flights, {o.ShuttleLandings} landings ({o.ShuttleRoughLandings} rough), {o.ShuttleHovers} hovers, {o.ShuttleStrafes} strafes");
        }

        static string Name(Scene sc, ZObject o) { int i = sc.Units.IndexOf(o); return i == 0 ? "A" : i == 1 ? "B" : "#" + o.Id; }

        /// <summary>The flip's inputs as the game's view reads them, with a twin of the view's eased bank, strafe lean and nose
        /// (View/OzPlayableShuttle.cs Pose: the same numbers, at 60 fps).</summary>
        static OzKickflipIn TwinIn(Scene sc, ZObject o, double dt)
        {
            if (!sc.ViewTwin.TryGetValue(o.Id, out var v)) sc.ViewTwin[o.Id] = v = new double[6];   // bank, bankVel, lean, pitch, pitchVel, -
            double vmax = Math.Max(1.0, o.RealMoveSpeed), acc = vmax / ZWorld.ShuttleAccelSec, lift = o.ShuttleLift, up2 = Math.Min(1.0, lift * 2);
            double spd = o.ShuttleSpeedPx, flyK = SmoothStep01((spd - 5.0) / (0.45 * vmax - 5.0));
            double turnWant = Clamp(-o.ShuttleTurnRateDeg * 0.28 * up2 * flyK, -20, 20);
            double sv = o.ShuttleStrafeVelPx / 8.0, sa = o.ShuttleStrafeAccelPx / 8.0;
            double leanWant = -Clamp(1.3 * sv + 0.2 * sa, -10, 10) * up2;
            v[2] = MoveTowards(v[2], leanWant, 22.0 * dt);
            v[0] = SmoothDamp(v[0], Clamp(turnWant + v[2], -20, 20), ref v[1], 0.3, 26.0, dt);
            double pitchWant = 7.0 * Clamp(o.ShuttleAccelPx / acc, -1, 1) * up2;
            if (o.ShuttleState == ZShuttleState.Descend) pitchWant = -2.0 * up2;
            v[3] = SmoothDamp(v[3], pitchWant, ref v[4], 0.4, double.PositiveInfinity, dt);
            double dx = o.ShuttleGoalX - o.ShuttleBaseX, dy = o.ShuttleGoalY - o.ShuttleBaseY;
            return new OzKickflipIn
            {
                Alive = !o.Destroyed && o.KillAt < 0, State = (int)o.ShuttleState, AtHeight = lift >= 0.999, Braking = o.ShuttleBraking, HoverTurn = o.ShuttleDeckTurning || o.ShuttleLandTurning,
                Strafing = o.ShuttleStrafeStage != 0, UnderFire = o.ShuttleUnderHeavyFire, OnScreen = true,
                SpeedMps = spd / 8.0, VmaxMps = vmax / 8.0, AccelMps2 = o.ShuttleAccelPx / 8.0, AccMaxMps2 = ZWorld.ShuttleAccelMax(vmax) / 8.0, TurnRateDeg = o.ShuttleTurnRateDeg,
                RemainM = Math.Sqrt(dx * dx + dy * dy) / 8.0, BrakeM = ZWorld.ShuttleBrakeDistance(spd, o.ShuttleAccelPx, vmax) / 8.0,
                FeetM = lift * ZWorld.ShuttleCruiseM, BankDeg = v[0], LeanDeg = v[2], PitchDeg = v[3], Health = o.Health, GoalX = o.ShuttleGoalX, GoalY = o.ShuttleGoalY,
            };
        }

        static long Hash(ZWorld w)
        {
            unchecked
            {
                long h = 17; h = h * 31 + (long)Math.Round(w.Time * 1e6); h = h * 31 + w.Objects.Count;
                foreach (var o in w.Objects)
                {
                    h = h * 31 + o.Id; h = h * 31 + o.LocX; h = h * 31 + o.LocY; h = h * 31 + (long)Math.Round(o.XOver * 1e6); h = h * 31 + (long)Math.Round(o.YOver * 1e6);
                    h = h * 31 + o.Health; h = h * 31 + (o.Destroyed ? 1 : 0); h = h * 31 + (long)o.Owner; h = h * 31 + o.Waypoints.Count;
                    if (o.IsShuttle)
                    {
                        h = h * 31 + (long)Math.Round(o.ShuttleHeadingDeg * 1e4); h = h * 31 + (int)o.ShuttleState; h = h * 31 + (long)Math.Round(o.ShuttleLift * 1e6);
                        h = h * 31 + (long)Math.Round(o.ShuttleSpeedPx * 1e6); h = h * 31 + (long)Math.Round(o.ShuttleStrafePx * 1e6); h = h * 31 + o.ShuttleGoalX; h = h * 31 + o.ShuttleGoalY;
                        h = h * 31 + o.ShuttleLandings; h = h * 31 + o.ShuttleStrafes;
                    }
                }
                return h;
            }
        }

        static void Compare(Scene off, Scene on, string what)
        {
            int n = Math.Min(off.Hashes.Count, on.Hashes.Count), first = -1;
            for (int k = 0; k < n; k++) if (off.Hashes[k] != on.Hashes[k]) { first = k; break; }
            bool same = first < 0 && off.Hashes.Count == on.Hashes.Count && n > 0;
            V(same, $"R {what}: the world hashed every tick, {off.Hashes.Count} ticks without the twin, {on.Hashes.Count} with it: {(same ? "IDENTICAL tick for tick" : "DIFFERENT from tick " + first)} (last hash {(n > 0 ? on.Hashes[n - 1] : 0)})");
        }

        static double Clamp(double x, double a, double b) => x < a ? a : x > b ? b : x;
        static double SmoothStep01(double x) { x = Clamp(x, 0, 1); return x * x * (3 - 2 * x); }
        static double MoveTowards(double c, double t, double d) => Math.Abs(t - c) <= d ? t : c + Math.Sign(t - c) * d;
        /// <summary>Mathf.SmoothDamp (the view's easing), in doubles.</summary>
        static double SmoothDamp(double current, double target, ref double vel, double smoothTime, double maxSpeed, double dt)
        {
            if (dt <= 0) return current;
            smoothTime = Math.Max(0.0001, smoothTime);
            double omega = 2.0 / smoothTime, x = omega * dt, exp = 1.0 / (1.0 + x + 0.48 * x * x + 0.235 * x * x * x);
            double change = current - target, orig = target, maxChange = maxSpeed * smoothTime;
            change = Clamp(change, -maxChange, maxChange); target = current - change;
            double temp = (vel + omega * change) * dt;
            vel = (vel - omega * temp) * exp;
            double output = target + (change + temp) * exp;
            if (orig - current > 0.0 == output > orig) { output = orig; vel = (output - orig) / dt; }
            return output;
        }
    }
}
