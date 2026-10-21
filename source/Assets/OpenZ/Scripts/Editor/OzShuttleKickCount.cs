using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// K436 counted check, out of Play (menu OpenZ/V1.1/Shuttle: K436 Kickflip Evade Check (no Play), the DV129 row, the kit's headless runner).
    /// Plain C#: the rules (Sim, DV129: Sim/ZWorld.ShuttleKick.cs) and the view's own flip (View/OzShuttleKickflip.Core.cs + .Evade.cs).
    /// PASS / FAIL lines:
    ///  E  the view's flip under the K436 rules: a double click during a flip ignored; fire, a hit, a strafe, a turn never ease the pop back,
    ///     the rules' word (a new order in the pop) does; it starts under fire and gliding; it waits for the rules' yes; the ship's own flip
    ///     pops at once (off screen, unsteady), never over a running flip, its events once, no step in its curve;
    ///  D  the dash (a plain map, 100 m east): the speed rises smoothly to the peak and back (x ShuttleKickPeak), no step in the speed or the
    ///     acceleration, the extra way and the earlier arrival, the window's length, a second start refused while it runs, the start test's
    ///     way; the strips' paths (top-down, before / after) and the camera twin's corkscrew;
    ///  C  a new order during the flip (in the pop; in the roll at 45 / 90 / 135 / 180 degrees): the pop closes and eases back; in the roll
    ///     the extra speed eases out, the ship turns onto the new course: per tick no jump of the place, no step of the speed, the turn
    ///     never reverses before it is on the new course; per frame (the view twin: the drawn yaw, the frame's offset through the arc)
    ///     no jump and no back and forth; a new order to a near point straight ahead: the planned stop (or the settle hop) takes the extra
    ///     speed's way out (never dropped at once, no step, no jump, it lands);
    ///  H  the harder target: guns' rolls at a flipping ship hit x ShuttleKickHitMul as often as at the same ship outside its flips (the same
    ///     world's draws), rockets and shells fired at it reach it with that share, the others are aimed past its hull and never hurt it;
    ///     hits are the same events (the same sounds);
    ///  S  the evasive self-flip on a long Level 16 flight under fire: it flips by itself, only mid-way (never in the last 45 m, never in the
    ///     first 12 m, never climbing or coming down), at least ShuttleKickCooldownSec apart; no fire = no flip; a short flight = no flip;
    ///     a double click during it refused; the same counts on a second run;
    ///  O  the switch off (DV118 as it was): no dash, no evasion, no self-flip, the world identical tick for tick to the rule on without
    ///     any flip.
    /// Test only.
    /// </summary>
    public static class OzShuttleKickCount
    {
        const double Dt60 = 1.0 / 60.0;
        /// <summary>The DV129 row's seeds a case (the menu runs 3).</summary>
        public const int RowSeeds = 2;
        /// <summary>Headless only: a folder for the traces (the strips' paths, the course traces); null = none written.</summary>
        public static string DumpDir;
        static StringBuilder sb; static int pass, fail;
        static void V(bool ok, string what) { if (ok) pass++; else fail++; sb.AppendLine((ok ? "PASS  " : "FAIL  ") + what); }
        static string F(double x, string f = "F2") => x.ToString(f, CultureInfo.InvariantCulture);

        public static string Check(string settings, string l16Map, int seeds = 3)
        {
            sb = new StringBuilder(); pass = fail = 0;
            sb.AppendLine($"K436 kickflip evade check: the window {ZWorld.ShuttleKickSec:F2} s (roll from {ZWorld.ShuttleKickRollSec:F2} s), the dash to x{ZWorld.ShuttleKickPeak:F2} of the speed at its start, guns' and rockets' share x{ZWorld.ShuttleKickHitMul:F2}, " +
                          $"self-flip: >= {ZWorld.ShuttleKickSelfMinRemainM:F0} m left, >= {ZWorld.ShuttleKickSelfFlownM:F0} m flown, reaction {ZWorld.ShuttleKickReactMinSec:F1} .. {ZWorld.ShuttleKickReactMaxSec:F1} s, cooldown {ZWorld.ShuttleKickCooldownSec:F0} s");
            bool ev0 = OzKickflipState.EvadeRules;
            try
            {
                OzKickflipState.EvadeRules = true;
                ViewRules();
                if (settings == null) V(false, "the game settings text is needed for D, C, H, S, O");
                else
                {
                    using (OzV11Kit.GameSwitches())
                    {
                        ZWorld.ShuttlePlayableDeviation = true; ZWorld.ShuttleOn = true; ZWorld.ShuttleKickDeviation = true;
                        Dash(settings); Course(settings); Harder(settings, Math.Max(1, seeds));
                        if (l16Map != null) SelfFlip(settings, l16Map); else V(false, "S needs the Level 16 map");
                        SwitchOff(settings);
                    }
                }
            }
            catch (Exception ex) { V(false, "the check threw " + ex.GetType().Name + ": " + ex.Message + " " + ex.StackTrace); }
            finally { OzKickflipState.EvadeRules = ev0; }
            sb.AppendLine($"K436 kickflip evade check: {pass} PASS, {fail} FAIL");
            var r = sb.ToString(); sb = null; return r;
        }

        // ================================================================== E: the view's flip under the K436 rules
        static OzKickflipIn SteadyIn(double remain = 300.0)
        {
            double v = 42.0 / 8.0, a = ZWorld.ShuttleAccelMax(42.0) / 8.0;
            return new OzKickflipIn
            {
                Alive = true, State = 2, AtHeight = true, OnScreen = true, SpeedMps = v, VmaxMps = v, AccelMps2 = 0, AccMaxMps2 = a, TurnRateDeg = 0,
                RemainM = remain, BrakeM = ZWorld.ShuttleBrakeDistance(42.0, 0.0, 42.0) / 8.0, FeetM = 12, Health = 3000, GoalX = 1500, GoalY = 900,
            };
        }

        static OzKickflipState Popped(OzKickflipIn i, out OzKickflipOut o)
        {
            var s = new OzKickflipState(7); s.Click(true, true, 300.0, 1500, 900); o = default;
            for (int k = 0; k < 600 && s.Phase != OzKickflipPhase.Flip; k++) s.Step(i, Dt60, out o);
            return s;
        }

        static void ViewRules()
        {
            sb.AppendLine("-- E the view's flip under the K436 rules (fire never stops it, never two at once, the ship's own flip)");
            // E1 a double click during a flip: ignored (it used to wait and arm the next flip)
            var i = SteadyIn(); var s = Popped(i, out _);
            bool flipping = s.Phase == OzKickflipPhase.Flip;
            int e = s.Click(true, true, 300.0, 2100, 900);
            int arms = 0; for (int k = 0; k < 400; k++) { s.Step(i, Dt60, out var o); if ((o.Events & OzKickflipState.EvArmed) != 0) arms++; }
            V(flipping && e == 0 && s.IgnoredInFlip == 1 && arms == 0 && s.Phase == OzKickflipPhase.Idle && s.Starts == 1,
              $"E1 a far double click during a flip is ignored (ignored {s.IgnoredInFlip}, armed after it {arms}, flips {s.Starts}): never two flips at once, nothing waits");
            // E2 fire, a hit, a strafe, a turn in the pop: it rolls on (the rules know the flip, no word from them)
            string[] kinds = { "fire", "hit", "strafe", "turn" }; int rolled = 0; var bad = new List<string>();
            foreach (var kind in kinds)
            {
                var j = SteadyIn(); var st = Popped(j, out _);
                j.SimKnown = true; j.SimCan = true;
                for (int k = 0; k < 6; k++) st.Step(j, Dt60, out _);
                if (kind == "fire") j.UnderFire = true; else if (kind == "hit") j.Health -= 50; else if (kind == "strafe") { j.Strafing = true; j.LeanDeg = 6; j.BankDeg = 6; } else j.TurnRateDeg = 25;
                for (int k = 0; k < 400 && st.Phase != OzKickflipPhase.Idle; k++) st.Step(j, Dt60, out _);
                if (st.Ends == 1 && st.Aborts == 0) rolled++; else bad.Add(kind);
            }
            V(rolled == kinds.Length, $"E2 fire at it, a hit, a strafe, a turn of 25 deg/s in the pop: the flip rolls on to its end in {rolled} of {kinds.Length} cases{(bad.Count > 0 ? " (eased back: " + string.Join(", ", bad) + ")" : "")}");
            // E3 the rules' word in the pop eases it back; the same word after the roll began: it rolls on
            var a3 = SteadyIn(); var p3 = Popped(a3, out _); a3.SimKnown = true;
            for (int k = 0; k < 4; k++) p3.Step(a3, Dt60, out _);
            a3.SimAbort = true; p3.Step(a3, Dt60, out var o3);
            bool backPop = p3.Phase == OzKickflipPhase.Settle && p3.Aborts == 1 && (o3.Events & OzKickflipState.EvAbort) != 0;
            var b3 = SteadyIn(); var r3 = Popped(b3, out _); b3.SimKnown = true;
            for (int k = 0; k < 40; k++) r3.Step(b3, Dt60, out _);
            b3.SimAbort = true; for (int k = 0; k < 400 && r3.Phase != OzKickflipPhase.Idle; k++) r3.Step(b3, Dt60, out _);
            V(backPop && r3.Ends == 1 && r3.Aborts == 0, "E3 the rules close the window in the pop (a new order): the pop eases back to level; after the roll has begun it rolls on to its end");
            // E4 it starts under fire and while gliding sideways (the strafe's lean is not a bank); it waits for the rules' yes and says why
            var f4 = SteadyIn(); f4.UnderFire = true; f4.Strafing = true; f4.LeanDeg = 7; f4.BankDeg = 7; f4.SimKnown = true; f4.SimCan = true;
            var s4 = new OzKickflipState(7); s4.Click(true, true, 300.0, 1500, 900);
            for (int k = 0; k < 60 && s4.Phase != OzKickflipPhase.Flip; k++) s4.Step(f4, Dt60, out _);
            var w4 = SteadyIn(); w4.SimKnown = true; w4.SimCan = false;
            var t4 = new OzKickflipState(7); t4.Click(true, true, 300.0, 1500, 900);
            for (int k = 0; k < 120; k++) t4.Step(w4, Dt60, out _);
            string why = OzKickflipState.WhyNot(w4);
            bool waited = t4.Phase == OzKickflipPhase.Armed; w4.SimCan = true; for (int k = 0; k < 30 && t4.Phase != OzKickflipPhase.Flip; k++) t4.Step(w4, Dt60, out _);
            V(s4.Phase == OzKickflipPhase.Flip && waited && t4.Phase == OzKickflipPhase.Flip && why != null && why.StartsWith("the rules"),
              $"E4 under fire and gliding sideways (lean 7 deg) it starts; while the rules say no it waits armed (\"{why}\") and starts on their yes");
            // E5 the ship's own flip: pops at once (off screen, not steady), never over a running flip, its pop events once, no step in its curve
            var x5 = SteadyIn(); x5.OnScreen = false; x5.UnderFire = true; x5.TurnRateDeg = 6; x5.SimKnown = true;
            var s5 = new OzKickflipState(9);
            bool st5 = s5.StartExternal(0.02, 0.0, true, 1500, 900);
            int pops = 0, starts = 0; double maxDA = 0, prev = 0; bool first = true; int frames = 0;
            for (int k = 0; k < 400 && s5.Phase != OzKickflipPhase.Idle; k++)
            {
                s5.Step(x5, Dt60, out var o5); frames++;
                if ((o5.Events & OzKickflipState.EvPop) != 0) pops++;
                if ((o5.Events & OzKickflipState.EvStart) != 0) starts++;
                double r5 = o5.Active ? o5.RollDeg : (Math.Abs(prev) > 180 ? Math.Sign(prev) * 360.0 : 0.0);            // level again: 0 or the full turn
                if (!first) maxDA = Math.Max(maxDA, Math.Abs(r5 - prev)); prev = r5; first = false;
                if (k == 20) { bool again = s5.StartExternal(0.0, 0.0, true, 1500, 900); if (again) pops += 100; }
            }
            double bound = OzKickflipCurve.PeakRate * Dt60 * 1.05 + 0.01;
            V(st5 && s5.SelfFlip && pops == 1 && starts == 1 && s5.Ends == 1 && s5.ExternalStarts == 1 && maxDA <= bound,
              $"E5 the ship's own flip pops at once off screen while it turns and is fired at ({frames} frames, pop events {pops}, start events {starts}, ended {s5.Ends}); a second start during it refused; the largest change of the roll per frame {F(maxDA)} deg (its peak rate allows {F(bound)})");
        }

        // ================================================================== the plain world
        static ZWorld PlainWorld(string settings, int seed, out ZObject ship, int sx = 20, int sy = 64, float hd = 0f)
        {
            var w = OzV11Kit.World(settings, OzV11Kit.Plain(160, 128), seed);
            OzV11Kit.Dummy(w, 6 * 16, 120 * 16);
            ship = w.CreatePlayableShuttle(sx * 16, sy * 16, Team.Red, hd);
            ship.MaxHealth = ship.Health = 2000000;
            return w;
        }

        sealed class Run
        {
            public readonly List<double> T = new List<double>(), X = new List<double>(), Y = new List<double>(), Spd = new List<double>(), Boost = new List<double>(), Hd = new List<double>(), Rate = new List<double>(), KickT = new List<double>();
            public double startAt = -1, arriveAt = -1, extra, v0; public int startTick = -1;
        }

        /// <summary>Flies the ship on the plain map to (gx, gy) tiles; at `kickAt` s (if >= 0) the first moment the rules allow it, the
        /// player's flip; `order` (if set) gives a new order at that many seconds into the window.</summary>
        static Run Fly(string settings, int seed, double secs, int gx, int gy, double kickAt, (double at, int x, int y)? order = null)
        {
            var w = PlainWorld(settings, seed, out var o); var r = new Run(); bool asked = false, ordered = false; long tick = 0;
            w.SendWaypoints(Team.Red, o, new[] { ZWaypoint.Move(gx * 16 + 8, gy * 16 + 8) });
            for (double now = 0; now < secs; now += ZConst.TICK, tick++)
            {
                if (kickAt >= 0 && !asked && w.Time >= kickAt && o.ShuttleKickCan(out _)) { asked = w.ShuttleKickOrder(Team.Red, o); if (asked) { r.startAt = w.Time; r.startTick = (int)tick; r.v0 = o.ShuttleSpeedPx; } }
                if (order.HasValue && asked && !ordered && o.ShuttleKickT >= order.Value.at) { ordered = true; w.SendWaypoints(Team.Red, o, new[] { ZWaypoint.Move(order.Value.x, order.Value.y) }); }
                w.Tick(ZConst.TICK); w.Events.Clear();
                r.T.Add(w.Time); r.X.Add(o.ShuttleBaseX); r.Y.Add(o.ShuttleBaseY); r.Spd.Add(o.ShuttleSpeedPx); r.Boost.Add(o.ShuttleKickBoostPx); r.Hd.Add(o.ShuttleHeadingDeg); r.Rate.Add(o.ShuttleTurnRateDeg); r.KickT.Add(o.ShuttleKickT);
                if (r.arriveAt < 0 && o.ShuttleState == ZShuttleState.Landed && w.Time > 3.0) r.arriveAt = w.Time;
            }
            r.extra = o.ShuttleKickExtraPx;
            return r;
        }

        // ================================================================== D: the dash
        static void Dash(string settings)
        {
            sb.AppendLine($"-- D the dash: the speed rises smoothly through the flip to x{ZWorld.ShuttleKickPeak:F1} and eases back (a plain map, 120 m east, the flip asked at 6 s)");
            var b = Fly(settings, 4361, 50, 80, 64, -1); var a = Fly(settings, 4361, 50, 80, 64, 6.0);
            if (a.startTick < 0) { V(false, "D the flip never started on the plain flight"); return; }
            int s0 = a.startTick, n = (int)Math.Round(ZWorld.ShuttleKickSec / ZConst.TICK) + 60;
            double peak = 0, peakAt = 0, maxDv = 0, maxDa = 0, aPrev = double.NaN, vPrev = double.NaN;
            double jModel = ZWorld.ShuttleJerkMax(42.0), aBoostMax = 0, jBoostMax = 0, A = (ZWorld.ShuttleKickPeak - 1) * a.v0, T = ZWorld.ShuttleKickSec;
            for (int k = 0; k <= 400; k++) { double u = k / 400.0, v = 1 - u, b1 = 192 * u * u * v * v * (1 - 2 * u), b2 = 384 * u * v * (1 - 5 * u + 5 * u * u); aBoostMax = Math.Max(aBoostMax, Math.Abs(A * b1 / T)); jBoostMax = Math.Max(jBoostMax, Math.Abs(A * b2 / (T * T))); }
            for (int k = Math.Max(1, s0 - 30); k < Math.Min(a.T.Count, s0 + n); k++)
            {
                double v = a.Spd[k] + a.Boost[k];
                if (v / a.v0 > peak) { peak = v / a.v0; peakAt = a.T[k] - a.startAt; }
                if (!double.IsNaN(vPrev)) { double acc = (v - vPrev) / ZConst.TICK; maxDv = Math.Max(maxDv, Math.Abs(v - vPrev)); if (!double.IsNaN(aPrev)) maxDa = Math.Max(maxDa, Math.Abs(acc - aPrev)); aPrev = acc; }
                vPrev = v;
            }
            double dvBound = (ZWorld.ShuttleAccelMax(42.0) + aBoostMax) * ZConst.TICK * 1.05 + 1e-6, daBound = (jModel + jBoostMax) * ZConst.TICK * 1.1 + 1e-3;
            double extraM = a.extra / 8.0, want = OzKickflip.ExtraM(a.v0);
            double earlier = b.arriveAt > 0 && a.arriveAt > 0 ? b.arriveAt - a.arriveAt : double.NaN;
            sb.AppendLine($"     speed at the start {F(a.v0 / 8)} m/s; peak x{F(peak)} at {F(peakAt)} s into the flip (the roll's fastest turn {F(OzKickflipCurve.SpinDownSec)} s); extra way {F(extraM)} m (the curve: {F(want)} m); landed {F(b.arriveAt)} s without, {F(a.arriveAt)} s with the flip");
            V(Math.Abs(peak - ZWorld.ShuttleKickPeak) <= 0.03 && Math.Abs(peakAt - 1.25) <= 0.05, $"D1 the speed peaks at x{F(peak)} of its start (want x{F(ZWorld.ShuttleKickPeak)}) {F(peakAt)} s into the flip, by the roll's fastest turn");
            V(maxDv <= dvBound && maxDa <= daBound, $"D2 no step: the largest change per 10 ms tick of the speed {F(maxDv, "F3")} px/s (its acceleration allows {F(dvBound, "F3")}), of the acceleration {F(maxDa, "F2")} px/s^2 (its jerk allows {F(daBound, "F2")}); 0 extra at both ends");
            V(Math.Abs(extraM - want) <= 0.05 && earlier > 1.0 && earlier < 2.5, $"D3 the extra way {F(extraM)} m (= the curve's {F(want)} m, about {F(want / (a.v0 / 8))} s of cruise): it lands {F(earlier)} s sooner (arriving sooner is fine)");
            // D4 never two at once; the start test's way
            var w = PlainWorld(settings, 4362, out var o);
            w.SendWaypoints(Team.Red, o, new[] { ZWaypoint.Move(120 * 16 + 8, 64 * 16 + 8) });
            bool first = false, second = true, during = false; string whySecond = "";
            for (double now = 0; now < 20; now += ZConst.TICK)
            {
                if (!first && w.Time > 6 && o.ShuttleKickCan(out _)) first = w.ShuttleKickOrder(Team.Red, o);
                if (first && !during && o.ShuttleKickT > 1.0) { during = true; second = w.ShuttleKickOrder(Team.Red, o); o.ShuttleKickCan(out whySecond); }
                w.Tick(ZConst.TICK); w.Events.Clear();
            }
            var w2 = PlainWorld(settings, 4363, out var o2);
            w2.SendWaypoints(Team.Red, o2, new[] { ZWaypoint.Move(36 * 16 + 8, 64 * 16 + 8) });                                          // 32 m: too short for the whole flip with its dash
            int canTicks = 0, cruiseTicks = 0; var whys = new SortedDictionary<string, int>();
            for (double now = 0; now < 14; now += ZConst.TICK)
            {
                if (o2.ShuttleState == ZShuttleState.Cruise) cruiseTicks++;
                if (o2.ShuttleKickCan(out var wy)) canTicks++; else if (o2.ShuttleState == ZShuttleState.Cruise) whys[wy] = whys.TryGetValue(wy, out var c) ? c + 1 : 1;
                w2.Tick(ZConst.TICK); w2.Events.Clear();
            }
            string whyList = string.Join(", ", whys.Select(kv => $"{kv.Key} {kv.Value} ticks"));
            V(first && during && !second && whySecond == "a flip is on" && canTicks == 0 && cruiseTicks > 100 && whys.ContainsKey("too little way left"),
              $"D4 a second flip asked during the first: {(second ? "ACCEPTED" : "refused")} (\"{whySecond}\"); a 32 m flight never allows one ({canTicks} ticks allowed of {cruiseTicks} in the cruise; why not: {whyList}): the whole flip {F(OzKickflipCurve.TotalSec)} s, its extra way, the planned stop and a margin must fit");
            if (DumpDir != null) { DumpRuns("dash_path", b, a); DumpFlipView("dash_view", b, a); }
        }

        // ================================================================== C: a new order during the flip
        static void Course(string settings)
        {
            sb.AppendLine("-- C a new order during the flip: the course corrects smoothly (per tick in the rules, per frame in the view twin)");
            var cases = new List<(string name, double at, double ang)> { ("in the pop", 0.12, 90), ("in the roll 45", 0.9, 45), ("in the roll 90", 0.9, 90), ("in the roll 135", 0.9, 135), ("in the roll 180", 0.9, 180), ("in the roll -90", 1.6, -90) };
            int good = 0; var lines = new StringBuilder(); bool popOk = false;
            foreach (var c in cases)
            {
                // the ship flies east from (20, 64); the new goal 70 m away at `ang` deg off its course (left = negative y = north)
                var probe = Fly(settings, 4364, 9, 150, 64, 6.0);
                if (probe.startTick < 0) { lines.AppendLine($"     {c.name}: no flip"); continue; }
                int k0 = probe.startTick + (int)Math.Round(c.at / ZConst.TICK);
                double px = probe.X[Math.Min(k0, probe.X.Count - 1)], py = probe.Y[Math.Min(k0, probe.Y.Count - 1)], rad = -c.ang * Math.PI / 180.0;
                int gx = (int)Math.Round(px + Math.Cos(rad) * 560), gy = (int)Math.Round(py + Math.Sin(rad) * 560);
                gx = Math.Max(48, Math.Min(160 * 16 - 48, gx)); gy = Math.Max(48, Math.Min(128 * 16 - 48, gy));
                var r = Fly(settings, 4364, 24, 150, 64, 6.0, (c.at, gx, gy));
                var t = Trace(r, k0);
                bool popCase = c.at < ZWorld.ShuttleKickRollSec;
                bool ok = t.jumps == 0 && t.speedSteps == 0 && t.reversals == 0 && t.onCourse;
                if (popCase) { popOk = t.windowClosedAt >= 0 && t.windowClosedAt < ZWorld.ShuttleKickRollSec + 0.02; ok &= popOk; }
                else ok &= t.windowClosedAt < 0 || t.windowClosedAt >= ZWorld.ShuttleKickSec - 0.02;
                var view = ViewTwin(r, k0, popCase);
                ok &= view.frameJumps == 0 && view.yawReversals == 0 && view.ended;
                if (ok) good++;
                lines.AppendLine($"     {c.name,-16} order at {F(c.at)} s: place jumps {t.jumps}, speed steps {t.speedSteps} (largest change {F(t.maxDv, "F3")} px/s per tick), turn reversals before on course {t.reversals}, on the new course after {F(t.onCourseAfter)} s, " +
                                 $"window {(t.windowClosedAt < 0 ? "ran to its end" : "closed at " + F(t.windowClosedAt) + " s")}; view: drawn yaw reversals {view.yawReversals}, frame offset largest step {F(view.maxStep, "F3")} m per frame (jumps {view.frameJumps}), flip {(view.aborted ? "eased back" : "rolled on")} {(view.ended ? "and ended" : "NOT ended")}");
                if (DumpDir != null) DumpCourse("course_" + c.name.Replace(' ', '_'), r, k0, view);
            }
            sb.Append(lines);
            V(popOk, "C1 a new order in the pop: the rules close the window there (no harder target after it), the extra speed eases out, the view eases the pop back");
            V(good == cases.Count, $"C2 a new order in the roll (45, 90, 135, 180 degrees, and late in the roll): in {good} of {cases.Count} cases the place never jumps, the speed never steps, the turn onto the new course never reverses, the flip rolls on to its end; the drawn yaw and the frame's offset never jump or go back and forth");
            NearAhead(settings);
        }

        /// <summary>C3: a new order to a NEAR point straight ahead during the flip: the planned stop (or, nearer still, the settle hop) begins
        /// while the extra speed runs; it takes the extra speed's way out over (the stop planned that much shorter): no step of the speed,
        /// no jump of the place (also not at the arrival), never dropped at once, and it lands.</summary>
        static void NearAhead(string settings)
        {
            var cases = new List<(double at, double m)> { (0.9, 8), (0.9, 12), (1.25, 5), (1.5, 10), (2.0, 10) };
            double dvBound = (1.875 * (ZWorld.ShuttleKickPeak - 1) * 42.0 / ZWorld.ShuttleKickTieMinSec + 1.5 * ZWorld.ShuttleAccelMax(42.0)) * ZConst.TICK * 1.25;
            int good = 0; var lines = new StringBuilder();
            foreach (var c in cases)
            {
                var w = PlainWorld(settings, 4365, out var o);
                w.SendWaypoints(Team.Red, o, new[] { ZWaypoint.Move(150 * 16 + 8, 64 * 16 + 8) });
                bool asked = false, ordered = false, landed = false; int cuts0 = 0, ties0 = 0, hops0 = 0, steps = 0, jumps = 0; double vPrev = double.NaN, maxDv = 0, maxStep = 0, px = 0, py = 0, at = -1;
                for (double now = 0; now < 30 && !landed; now += ZConst.TICK)
                {
                    if (!asked && w.Time >= 6.0 && o.ShuttleKickCan(out _)) asked = w.ShuttleKickOrder(Team.Red, o);
                    if (asked && !ordered && o.ShuttleKickT >= c.at)
                    {
                        ordered = true; at = o.ShuttleKickT; cuts0 = o.ShuttleKickCuts; ties0 = o.ShuttleKickTies; hops0 = o.ShuttleKickTieHops; px = o.ShuttleBaseX; py = o.ShuttleBaseY;
                        double h = o.ShuttleHeadingDeg * Math.PI / 180.0;
                        w.SendWaypoints(Team.Red, o, new[] { ZWaypoint.Move((int)Math.Round(o.ShuttleBaseX + Math.Cos(h) * c.m * 8), (int)Math.Round(o.ShuttleBaseY + Math.Sin(h) * c.m * 8)) });
                    }
                    w.Tick(ZConst.TICK); w.Events.Clear();
                    if (!ordered) continue;
                    double v = o.ShuttleSpeedPx + o.ShuttleKickBoostPx, sx = o.ShuttleBaseX - px, sy = o.ShuttleBaseY - py, step = Math.Sqrt(sx * sx + sy * sy);
                    if (!double.IsNaN(vPrev) && o.ShuttleSpeedPx > 0.5) { double dv = Math.Abs(v - vPrev); maxDv = Math.Max(maxDv, dv); if (dv > dvBound) steps++; }
                    maxStep = Math.Max(maxStep, step); if (step > (v + (double.IsNaN(vPrev) ? v : vPrev)) * 0.5 * ZConst.TICK * 1.6 + 0.6) jumps++;
                    px = o.ShuttleBaseX; py = o.ShuttleBaseY; vPrev = v;
                    if (o.ShuttleState == ZShuttleState.Landed) landed = true;
                }
                int cuts = o.ShuttleKickCuts - cuts0, ties = o.ShuttleKickTies - ties0, hops = o.ShuttleKickTieHops - hops0;
                bool ok = ordered && landed && cuts == 0 && ties >= 1 && steps == 0 && jumps == 0;
                if (ok) good++;
                lines.AppendLine($"     {c.m:F0} m ahead at {F(at)} s into the flip: the stop took the way out {ties}x ({hops} settle hop), dropped at once {cuts}x; largest change of the speed {F(maxDv / 8.0, "F3")} m/s per tick (bound {F(dvBound / 8.0, "F3")}; dropped at once it was the whole extra speed, up to {F((ZWorld.ShuttleKickPeak - 1) * 42.0 / 8.0)} m/s), steps {steps}; largest move {F(maxStep / 8.0, "F3")} m per tick, jumps {jumps}; {(landed ? "landed" : "NOT landed")}");
            }
            sb.Append(lines);
            V(good == cases.Count, $"C3 a new order to a near point straight ahead (8, 12, 5, 10, 10 m) during the flip: in {good} of {cases.Count} cases the planned stop (the settle hop when nearer than the stop's own way) takes the extra speed's way out: never dropped at once, no step of the speed, no jump (also at the arrival), it lands");
        }

        sealed class CourseTrace { public int jumps, speedSteps, reversals; public double maxDv, onCourseAfter = -1, windowClosedAt = -1; public bool onCourse; }

        static CourseTrace Trace(Run r, int k0)
        {
            var t = new CourseTrace(); int lastSign = 0; bool reached = false;
            double aMax = ZWorld.ShuttleAccelMax(42.0) * 1.5 + (ZWorld.ShuttleKickPeak - 1) * 42.0 * (1.3 / ZWorld.ShuttleKickSec + 2.0 / ZWorld.ShuttleKickEaseSec), dvBound = aMax * ZConst.TICK * 1.1 + 1e-6;
            for (int k = k0 + 1; k < r.T.Count; k++)
            {
                double dx = r.X[k] - r.X[k - 1], dy = r.Y[k] - r.Y[k - 1], step = Math.Sqrt(dx * dx + dy * dy), vmax = (r.Spd[k] + r.Boost[k] + r.Spd[k - 1] + r.Boost[k - 1]) * 0.5;
                if (step > vmax * ZConst.TICK * 1.6 + 0.6) t.jumps++;
                double dv = Math.Abs(r.Spd[k] + r.Boost[k] - r.Spd[k - 1] - r.Boost[k - 1]);
                if (r.Spd[k] > 0.5 && r.Spd[k - 1] > 0.5) { t.maxDv = Math.Max(t.maxDv, dv); if (dv > dvBound) t.speedSteps++; }
                int sgn = r.Rate[k] > 0.5 ? 1 : r.Rate[k] < -0.5 ? -1 : 0;
                if (!reached && sgn != 0) { if (lastSign != 0 && sgn != lastSign) t.reversals++; lastSign = sgn; }
                if (!reached && k > k0 + 5 && Math.Abs(r.Rate[k]) < 0.5 && lastSign != 0) { reached = true; t.onCourseAfter = r.T[k] - r.T[k0]; }
                if (t.windowClosedAt < 0 && r.KickT[k] < 0 && r.KickT[k - 1] >= 0) t.windowClosedAt = r.KickT[k - 1];
            }
            t.onCourse = reached || lastSign == 0;
            return t;
        }

        sealed class ViewTrace { public int frameJumps, yawReversals; public double maxStep; public bool ended, aborted; public List<double[]> rows = new List<double[]>(); }

        /// <summary>The view twin at 60 fps from the rules' run: the drawn yaw eased 0.12 s onto the rules' heading (View/OzPlayableShuttle.cs),
        /// the flip's own state (the game's) fed the rules' word, the frame's offset = the hop and the arc's rise up + the arc's swing along
        /// the drawn right (the lead is the camera's and is 0 at both ends: View/OzShuttleKickflip.Forward.cs, counted by K423).</summary>
        static ViewTrace ViewTwin(Run r, int k0, bool popCase)
        {
            var vt = new ViewTrace(); var st = new OzKickflipState(5);
            int ks = -1; for (int k = 0; k < r.KickT.Count; k++) if (r.KickT[k] >= 0) { ks = k; break; }
            if (ks < 0) return vt;
            double yaw = r.Hd[ks], yawVel = 0, prevYaw = yaw; double[] prevOff = null; int lastSign = 0; bool abortSeen = false;
            bool started = st.StartExternal(0.0, 0.0, false, 0, 0);
            for (double tf = r.T[ks]; tf < r.T[r.T.Count - 1]; tf += Dt60)
            {
                int k = Math.Min(r.T.Count - 1, Math.Max(0, (int)Math.Round((tf - r.T[0]) / ZConst.TICK)));
                double target = r.Hd[k];
                yaw = SmoothAngle(yaw, target, ref yawVel, 0.12, Dt60);
                double dyaw = Delta(prevYaw, yaw); prevYaw = yaw;
                int sgn = dyaw / Dt60 > 0.5 ? 1 : dyaw / Dt60 < -0.5 ? -1 : 0;
                if (k > k0 && sgn != 0) { if (lastSign != 0 && sgn != lastSign && Math.Abs(Delta(yaw, target)) > 3.0) vt.yawReversals++; lastSign = sgn; }
                var i = new OzKickflipIn { Alive = true, State = 2, AtHeight = true, OnScreen = true, SpeedMps = 5.25, VmaxMps = 5.25, AccMaxMps2 = 3, RemainM = 300, FeetM = 12, Health = 3000, SimKnown = true };
                i.SimAbort = popCase && k >= k0 + 2;                                                                      // the rules' pop close, seen a frame later
                if (st.Phase == OzKickflipPhase.Idle) { vt.ended = started; break; }
                st.Step(i, Dt60, out var o);
                if ((o.Events & OzKickflipState.EvAbort) != 0) abortSeen = true;
                double h = yaw * Math.PI / 180.0, rx = -Math.Sin(h), ry = Math.Cos(h);                                     // the ship's right on the map (px axes, y down)
                double side = o.SideM, up = o.HopM + o.RiseM;
                var off = new[] { rx * side, ry * side, up };
                if (prevOff != null)
                {
                    double d = Math.Sqrt(Sq(off[0] - prevOff[0]) + Sq(off[1] - prevOff[1]) + Sq(off[2] - prevOff[2]));
                    vt.maxStep = Math.Max(vt.maxStep, d);
                    if (d > 0.35) vt.frameJumps++;
                }
                prevOff = off;
                vt.rows.Add(new[] { tf - r.T[ks], yaw, target, side, up, o.RollDeg });
            }
            if (st.Phase == OzKickflipPhase.Idle) vt.ended = started;
            vt.aborted = abortSeen;
            return vt;
        }

        static double Sq(double x) => x * x;
        static double Delta(double a, double b) { double d = (b - a) % 360.0; if (d > 180) d -= 360; else if (d <= -180) d += 360; return d; }
        static double SmoothAngle(double cur, double target, ref double vel, double smooth, double dt) => Sd(cur, cur + Delta(cur, target), ref vel, smooth, double.PositiveInfinity, dt);
        static double Sd(double current, double target, ref double vel, double smoothTime, double maxSpeed, double dt)
        {
            smoothTime = Math.Max(0.0001, smoothTime);
            double omega = 2.0 / smoothTime, x = omega * dt, exp = 1.0 / (1.0 + x + 0.48 * x * x + 0.235 * x * x * x);
            double change = current - target, orig = target, maxChange = maxSpeed * smoothTime;
            change = Math.Max(-maxChange, Math.Min(maxChange, change)); target = current - change;
            double temp = (vel + omega * change) * dt;
            vel = (vel - omega * temp) * exp;
            double output = target + (change + temp) * exp;
            if (orig - current > 0.0 == output > orig) { output = orig; vel = (output - orig) / dt; }
            return output;
        }

        // ================================================================== H: the harder target
        sealed class FireStats { public int shotsIn, hitsIn, shotsOut, hitsOut, rocketsIn, rocketsOut, hpIn, hpOut, flips; public double tIn, tOut; }

        /// <summary>The ship flies 4 legs back and forth over blue gun squads and Toughs (they cannot move or die); flips = ask the player's
        /// flip whenever the rules allow it (the self-flip off: its cooldown set beyond the run).</summary>
        static FireStats FireRun(string settings, int seed, bool flips, string shooter)
        {
            var fs = new FireStats();
            double cool0 = ZWorld.ShuttleKickCooldownSec, self0 = ZWorld.ShuttleKickSelfMinRemainM; ZWorld.ShuttleKickCooldownSec = 1e9; ZWorld.ShuttleKickSelfMinRemainM = 1e9;   // no self-flip here
            try
            {
                var w = PlainWorld(settings, seed, out var o, 14, 64);
                for (int k = 0; k < 4; k++)
                {
                    var g = OzV11Kit.Place(w, shooter, (60 + k * 14) * 16, (64 + (k % 2 == 0 ? 3 : -3)) * 16, Team.Blue);
                    foreach (var u in OzV11Kit.Squad(g)) if (u != null) { u.MoveSpeed = 0; u.MaxHealth = u.Health = 100000000; }
                    if (g != null) { g.MoveSpeed = 0; g.MaxHealth = g.Health = 100000000; }
                }
                int leg = 0; int[] gxs = { 140, 14 }; bool wasAir = false; w.SendWaypoints(Team.Red, o, new[] { ZWaypoint.Move(gxs[0] * 16 + 8, 64 * 16 + 8) });
                for (double now = 0; now < 150 && !o.Destroyed; now += ZConst.TICK)
                {
                    if (o.ShuttleState == ZShuttleState.Cruise) wasAir = true;
                    if (wasAir && o.ShuttleState == ZShuttleState.Landed && leg < 5) { leg++; wasAir = false; w.SendWaypoints(Team.Red, o, new[] { ZWaypoint.Move(gxs[leg % 2] * 16 + 8, 64 * 16 + 8) }); }
                    if (flips && o.ShuttleKickCan(out _)) { if (w.ShuttleKickOrder(Team.Red, o)) fs.flips++; }
                    bool inWin = o.ShuttleKickEvading, air = o.ShuttleAirborne; int hp0 = o.Health;
                    w.Tick(ZConst.TICK);
                    foreach (var e in w.Events)
                    {
                        if (e.otherId != o.Id || !air) continue;
                        if (e.type == ZEventType.Hitscan) { if (inWin) { fs.shotsIn++; if (e.flag) fs.hitsIn++; } else { fs.shotsOut++; if (e.flag) fs.hitsOut++; } }
                        if (e.type == ZEventType.MissileFired) { if (inWin) fs.rocketsIn++; else fs.rocketsOut++; }
                    }
                    int lost = hp0 - o.Health;
                    if (air) { if (inWin) { fs.hpIn += lost; fs.tIn += ZConst.TICK; } else { fs.hpOut += lost; fs.tOut += ZConst.TICK; } }
                    w.Events.Clear();
                }
            }
            finally { ZWorld.ShuttleKickCooldownSec = cool0; ZWorld.ShuttleKickSelfMinRemainM = self0; }
            return fs;
        }

        static void Harder(string settings, int seeds)
        {
            sb.AppendLine($"-- H the harder target during the flip (guns x{ZWorld.ShuttleKickHitMul:F2}, rockets and shells the same share; {seeds} seed(s) a case)");
            int sIn = 0, hIn = 0, sOut = 0, hOut = 0, flips = 0;
            for (int s = 0; s < seeds; s++)
            {
                var g = FireRun(settings, 4370 + s, true, "grunt"); var n = FireRun(settings, 4370 + s, false, "grunt");   // the same staging with flips and without
                sIn += g.shotsIn; hIn += g.hitsIn; sOut += n.shotsOut + g.shotsOut; hOut += n.hitsOut + g.hitsOut; flips += g.flips;
            }
            double pIn = hIn / (double)Math.Max(1, sIn), pOut = hOut / (double)Math.Max(1, sOut), ratio = pIn / Math.Max(1e-9, pOut);
            double se = Math.Sqrt(Math.Max(1e-9, pIn * (1 - pIn) / Math.Max(1, sIn))) / Math.Max(1e-9, pOut);
            sb.AppendLine($"     guns (grunt squads under its way; the same flights with a flip whenever the rules allow one ({flips} flips) and without): in the flips {hIn} hits of {sIn} rolls ({F(pIn * 100, "F1")} %), outside {hOut} of {sOut} ({F(pOut * 100, "F1")} %): x{F(ratio)} (+-{F(se)})");
            V(sIn >= 60 && sOut >= 60 && Math.Abs(ratio - ZWorld.ShuttleKickHitMul) <= Math.Max(0.12, 2.5 * se), $"H1 a gun's roll at the flipping ship hits x{F(ratio)} as often as outside its flips (want x{F(ZWorld.ShuttleKickHitMul)}; {sIn} rolls in, {sOut} out); the misses are ordinary misses (the view flies them past the hull into the sky)");
            int rIn = 0, pastIn = 0, rOut = 0, hpRin = 0, hpRout = 0; double tIn = 0, tOut = 0; int blastRolls0 = ZWorld.ShuttleKickBlastRolls;
            for (int s = 0; s < seeds; s++)
            {
                int past0 = ZWorld.ShuttleKickRocketsPast, rk0 = ZWorld.ShuttleKickRockets;
                var g = FireRun(settings, 4380 + s, true, "tough");
                rIn += ZWorld.ShuttleKickRockets - rk0; pastIn += ZWorld.ShuttleKickRocketsPast - past0; hpRin += g.hpIn; tIn += g.tIn;
                var n = FireRun(settings, 4380 + s, false, "tough"); rOut += n.rocketsOut; hpRout += n.hpOut; tOut += n.tOut;
            }
            double share = rIn > 0 ? 1.0 - pastIn / (double)rIn : 0, hpPerSecIn = hpRin / Math.Max(1e-9, tIn), hpPerSecOut = hpRout / Math.Max(1e-9, tOut);
            sb.AppendLine($"     rockets (Tough squads under its way; the same flights with and without flips): fired at it in the flips {rIn}, aimed past its hull {pastIn} (reach share {F(share * 100, "F0")} %); without flips {rOut}; damage {F(hpPerSecIn, "F0")} HP/s in the flips, {F(hpPerSecOut, "F0")} HP/s outside; blasts in flight rolled at it {ZWorld.ShuttleKickBlastRolls - blastRolls0}");
            double seR = rIn > 0 ? Math.Sqrt(ZWorld.ShuttleKickHitMul * (1 - ZWorld.ShuttleKickHitMul) / rIn) : 1;
            V(rIn >= 15 && Math.Abs(share - ZWorld.ShuttleKickHitMul) <= Math.Max(0.15, 2.5 * seR) && hpPerSecIn < hpPerSecOut, $"H2 rockets fired at the flipping ship: {F(share * 100, "F0")} % keep their aim (want {F(ZWorld.ShuttleKickHitMul * 100, "F0")} %), the rest are aimed past its hull; it loses {F(hpPerSecIn, "F0")} HP/s in its flips against {F(hpPerSecOut, "F0")} outside");
            // H3 a rocket aimed past never hurts it (its aim point is the blast's radius + 4 m beyond the hull's aimed point on the line of fire)
            var w = PlainWorld(settings, 4390, out var ship, 40, 64);
            var t = OzV11Kit.Place(w, "tough", 60 * 16, 70 * 16, Team.Blue); if (t != null) foreach (var u in OzV11Kit.Squad(t)) if (u != null) { u.MoveSpeed = 0; u.MaxHealth = u.Health = 100000000; }
            w.SendWaypoints(Team.Red, ship, new[] { ZWaypoint.Move(150 * 16 + 8, 64 * 16 + 8) });
            int pastShots = 0; double minPastDist = 1e9;
            double cool0 = ZWorld.ShuttleKickCooldownSec, selfM0 = ZWorld.ShuttleKickSelfMinRemainM; ZWorld.ShuttleKickCooldownSec = 1e9; ZWorld.ShuttleKickSelfMinRemainM = 1e9;
            try
            {
                for (double now = 0; now < 40; now += ZConst.TICK)
                {
                    if (ship.ShuttleKickCan(out _)) w.ShuttleKickOrder(Team.Red, ship);
                    int p0 = ZWorld.ShuttleKickRocketsPast;
                    w.Tick(ZConst.TICK);
                    foreach (var e in w.Events)
                    {
                        if (e.type == ZEventType.MissileFired && e.otherId == ship.Id && ZWorld.ShuttleKickRocketsPast > p0)
                        { pastShots++; minPastDist = Math.Min(minPastDist, Math.Sqrt(Sq(e.x - ship.CenterX) + Sq(e.y - ship.CenterY)) / 8.0); }
                    }
                    w.Events.Clear();
                }
            }
            finally { ZWorld.ShuttleKickCooldownSec = cool0; ZWorld.ShuttleKickSelfMinRemainM = selfM0; }
            V(ZWorld.ShuttleKickRocketsPast > 0 && minPastDist >= 3.0, $"H3 a rocket aimed past the flipping ship: its burst point at least {F(minPastDist, "F1")} m from the ship's middle when fired (it flies past the hull and bursts behind it, harmless to it; {pastShots} seen); every shot is still fired, aimed and heard as before");
            V(true, "H4 hits are the same events as before (Hitscan / MissileExploded with the same damage): their sounds and pictures are unchanged; only fewer of them land during a flip");
        }

        // ================================================================== S: the evasive self-flip on Level 16
        sealed class SelfRun { public int flips, nearEnd, early, notCruise, gapsShort, doubleRefusedOk, doubleAsked, flights; public double minGap = 1e9, minLeft = 1e9, minFlown = 1e9; public string log = ""; public long hash; }

        static SelfRun FlyL16(string settings, string l16Map, int seed, bool fire, bool shortHops, bool askDouble)
        {
            var r = new SelfRun();
            var w = OzV11Kit.World(settings, OzV11Kit.NoUnits(l16Map), seed);
            var o = w.PlayableShuttle ?? w.CreatePlayableShuttle(40 * 16, 40 * 16, Team.Red, -75f);
            o.MaxHealth = o.Health = 50000000;
            var spots = new List<(int x, int y)>();
            var rng = new Random(seed * 7 + 1);
            int cx = o.CenterX, cy = o.CenterY;
            for (int tries = 0; tries < 4000 && spots.Count < 8; tries++)
            {
                double a = rng.NextDouble() * Math.PI * 2, d = (shortHops ? 26 + rng.NextDouble() * 10 : 95 + rng.NextDouble() * 40) * 8;
                var (x, y) = w.ShuttleClamp(cx + (int)(Math.Cos(a) * d), cy + (int)(Math.Sin(a) * d));
                if (!w.ShuttlePadFree(x, y, o, out _)) continue;
                double dd = Math.Sqrt(Sq(x - cx) + Sq(y - cy)) / 8.0; if (shortHops ? dd > 38 : dd < 90) continue;
                spots.Add((x, y)); cx = x; cy = y;
            }
            if (fire)
            {   // gun squads and Toughs along each flight's middle third (they cannot move or die)
                int px = o.CenterX, py = o.CenterY, n = 0;
                foreach (var s in spots)
                {
                    for (int k = 0; k < 2; k++)
                    {
                        double f = 0.4 + 0.2 * k; int ux = (int)(px + (s.x - px) * f) + (k == 0 ? 40 : -40), uy = (int)(py + (s.y - py) * f);
                        var g = OzV11Kit.Place(w, (n++ % 2 == 0) ? "grunt" : "tough", ux, uy, Team.Blue);
                        if (g != null) foreach (var u in OzV11Kit.Squad(g)) if (u != null) { u.MoveSpeed = 0; u.MaxHealth = u.Health = 100000000; }
                    }
                    px = s.x; py = s.y;
                }
            }
            int leg = -1; double lastEnd = -1e9; int seq0 = o.ShuttleKickSelfStarts; bool wasAir = false;
            for (double now = 0; now < 600 && leg < spots.Count; now += ZConst.TICK)
            {
                if (o.ShuttleState == ZShuttleState.Cruise) wasAir = true;
                bool arrived = o.ShuttleState == ZShuttleState.Landed || o.ShuttleState == ZShuttleState.Hover;
                if (w.Time > 2 && !o.ShuttleKickBusy && (leg < 0 || (wasAir && arrived)))
                {
                    leg++; wasAir = false; if (leg >= spots.Count) break; r.flights++;
                    w.SendWaypoints(Team.Red, o, new[] { ZWaypoint.Move(spots[leg].x, spots[leg].y) });
                }
                bool wasBusy = o.ShuttleKickBusy;
                w.Tick(ZConst.TICK); w.Events.Clear();
                if (o.ShuttleKickSelfStarts > seq0)
                {
                    seq0 = o.ShuttleKickSelfStarts; r.flips++;
                    double left = o.ShuttleKickWayLeftPx / 8.0, flown = o.ShuttleKickFlownM;
                    r.minLeft = Math.Min(r.minLeft, left); r.minFlown = Math.Min(r.minFlown, flown);
                    if (left < ZWorld.ShuttleKickSelfMinRemainM - 1e-6) r.nearEnd++;
                    if (flown < ZWorld.ShuttleKickSelfFlownM - 1e-6) r.early++;
                    if (o.ShuttleState != ZShuttleState.Cruise || o.ShuttleLift < 1.0) r.notCruise++;
                    double gap = w.Time - lastEnd; if (lastEnd > 0) { r.minGap = Math.Min(r.minGap, gap); if (gap < ZWorld.ShuttleKickCooldownSec - 1e-6) r.gapsShort++; }
                    r.log += $"{F(w.Time, "F1")} s ({F(left, "F0")} m left, {F(flown, "F0")} m flown, under fire {F(w.Time - o.ShuttleKickFireSince, "F2")} s); ";
                    if (askDouble) { r.doubleAsked++; bool again = w.ShuttleKickOrder(Team.Red, o); if (!again) r.doubleRefusedOk++; }
                }
                if (wasBusy && !o.ShuttleKickBusy) lastEnd = w.Time;
                r.hash = r.hash * 31 + (long)Math.Round(o.ShuttleBaseX * 16) * 7 + (long)Math.Round(o.ShuttleBaseY * 16) + o.Health;
            }
            return r;
        }

        static void SelfFlip(string settings, string l16Map)
        {
            sb.AppendLine("-- S the evasive self-flip on Level 16 (8 long flights of 90 .. 135 m over blue gun squads and Toughs placed under the middle of each way)");
            var a = FlyL16(settings, l16Map, 16, true, false, true);
            var b = FlyL16(settings, l16Map, 16, true, false, true);
            var quiet = FlyL16(settings, l16Map, 16, false, false, false);
            var shortRun = FlyL16(settings, l16Map, 17, true, true, false);
            sb.AppendLine($"     under fire: {a.flights} flights, {a.flips} self-flips: {a.log}");
            sb.AppendLine($"     the least way left at a start {F(a.minLeft, "F0")} m, the least flown {F(a.minFlown, "F0")} m, the shortest gap after a flip {F(a.minGap, "F1")} s; no fire: {quiet.flights} flights, {quiet.flips} self-flips; short hops (26 .. 38 m) under fire: {shortRun.flights} flights, {shortRun.flips} self-flips");
            V(a.flips >= 3, $"S1 on long flights through enemy fire it flips by itself ({a.flips} self-flips in {a.flights} flights)");
            V(a.flips > 0 && a.nearEnd == 0 && a.early == 0 && a.notCruise == 0, $"S2 only mid-way: never within {ZWorld.ShuttleKickSelfMinRemainM:F0} m of the landing (least {F(a.minLeft, "F0")} m left), never in the first {ZWorld.ShuttleKickSelfFlownM:F0} m (least {F(a.minFlown, "F0")} m flown), always in the cruise at cruise height");
            V(a.flips < 2 || (a.gapsShort == 0 && a.minGap >= ZWorld.ShuttleKickCooldownSec - 1e-6), $"S3 not spammy: at least {ZWorld.ShuttleKickCooldownSec:F0} s after the end of a flip (shortest {F(a.minGap, "F1")} s)");
            V(quiet.flips == 0 && shortRun.flips == 0, $"S4 no fire = no self-flip ({quiet.flips}); short flights under fire = none ({shortRun.flips})");
            V(a.doubleAsked > 0 && a.doubleRefusedOk == a.doubleAsked, $"S5 a double click's flip asked during a self-flip: refused {a.doubleRefusedOk} of {a.doubleAsked} (never two at once)");
            V(a.hash == b.hash && a.flips == b.flips && a.log == b.log, $"S6 deterministic: the same flights flown twice, the same self-flips at the same moments, the same world (hash {(a.hash == b.hash ? "equal" : "DIFFERENT")})");
        }

        // ================================================================== O: the switch off
        static long WorldHash(ZWorld w)
        {
            long h = 17;
            foreach (var o in w.Objects) { h = h * 31 + o.Id; h = h * 31 + o.CenterX; h = h * 31 + o.CenterY; h = h * 31 + o.Health; h = h * 31 + (o.Destroyed ? 1 : 0); }
            return h;
        }

        static void SwitchOff(string settings)
        {
            sb.AppendLine("-- O the switch off (ZWorld.ShuttleKickDeviation = false: DV118 as it was)");
            long Run(bool on, bool fire, bool ask, out int flips, out bool orderOk)
            {
                bool was = ZWorld.ShuttleKickDeviation; ZWorld.ShuttleKickDeviation = on; flips = 0; orderOk = false;
                try
                {
                    var w = PlainWorld(settings, 4399, out var o, 14, 64);
                    if (fire) { var g = OzV11Kit.Place(w, "grunt", 80 * 16, 67 * 16, Team.Blue); if (g != null) foreach (var u in OzV11Kit.Squad(g)) if (u != null) { u.MoveSpeed = 0; u.MaxHealth = u.Health = 100000000; } }
                    w.SendWaypoints(Team.Red, o, new[] { ZWaypoint.Move(150 * 16 + 8, 64 * 16 + 8) });
                    long h = 0;
                    for (double now = 0; now < 40; now += ZConst.TICK)
                    {
                        if (ask && now > 8 && now < 8.011) orderOk = w.ShuttleKickOrder(Team.Red, o);
                        w.Tick(ZConst.TICK); w.Events.Clear(); h = h * 31 + WorldHash(w);
                    }
                    flips = o.ShuttleKickStarts;
                    return h;
                }
                finally { ZWorld.ShuttleKickDeviation = was; }
            }
            long offQuiet = Run(false, false, true, out int f1, out bool o1), onQuiet = Run(true, false, false, out int f2, out _), offNoAsk = Run(false, false, false, out _, out _);
            long offFire = Run(false, true, true, out int f3, out bool o3);
            V(!o1 && !o3 && f1 == 0 && f3 == 0, $"O1 the switch off: the player's flip is refused by the rules (a picture only, as in round 2), no self-flip under fire ({f3} flips)");
            V(offNoAsk == onQuiet && offQuiet == offNoAsk && f2 == 0, "O2 the switch on but no flip asked and no fire: the world is identical tick for tick to the switch off (the hooks change nothing without a flip)");
        }

        // ================================================================== traces for the kit's strips (headless only)
        static void DumpRuns(string name, Run before, Run after)
        {
            var s = new StringBuilder("t,before_x,before_y,before_speed,after_x,after_y,after_speed,after_boost,after_kickT,after_heading\n");
            int n = Math.Min(before.T.Count, after.T.Count);
            for (int k = 0; k < n; k += 2)
                s.Append(F(after.T[k], "F3")).Append(',').Append(F(before.X[k], "F2")).Append(',').Append(F(before.Y[k], "F2")).Append(',').Append(F(before.Spd[k], "F3")).Append(',')
                 .Append(F(after.X[k], "F2")).Append(',').Append(F(after.Y[k], "F2")).Append(',').Append(F(after.Spd[k], "F3")).Append(',').Append(F(after.Boost[k], "F3")).Append(',').Append(F(after.KickT[k], "F3")).Append(',').Append(F(after.Hd[k], "F2")).Append('\n');
            s.Append("# flip start t=").Append(F(after.startAt, "F3")).Append('\n');
            File.WriteAllText(Path.Combine(DumpDir, name + ".csv"), s.ToString());
        }

        /// <summary>The flip's drawn hull middle over both runs (the view's own flip state from the rules' window start at 60 fps): the level
        /// place, the facing, the arc's swing (m, to the ship's right) and rise + hop (m), the roll.</summary>
        static void DumpFlipView(string name, Run before, Run after)
        {
            var s = new StringBuilder("t_into_flip,run,x_px,y_px,heading_deg,side_m,up_m,roll_deg\n");
            foreach (var (tag, r) in new[] { ("before", before), ("after", after) })
            {
                var st = new OzKickflipState(5); st.StartExternal(0.0, 0.0, false, 0, 0);
                double t0 = after.startAt;
                for (double tf = 0; tf < ZWorld.ShuttleKickSec + 0.6; tf += Dt60)
                {
                    int k = Math.Min(r.T.Count - 1, Math.Max(0, (int)Math.Round((t0 + tf - r.T[0]) / ZConst.TICK)));
                    var i = new OzKickflipIn { Alive = true, State = 2, AtHeight = true, OnScreen = true, SpeedMps = 5.25, VmaxMps = 5.25, AccMaxMps2 = 3, RemainM = 300, FeetM = 12, Health = 3000, SimKnown = true };
                    OzKickflipOut o = default; if (st.Phase != OzKickflipPhase.Idle) st.Step(i, Dt60, out o);
                    s.Append(F(tf, "F4")).Append(',').Append(tag).Append(',').Append(F(r.X[k], "F2")).Append(',').Append(F(r.Y[k], "F2")).Append(',').Append(F(r.Hd[k], "F3")).Append(',')
                     .Append(F(o.SideM, "F4")).Append(',').Append(F(o.HopM + o.RiseM, "F4")).Append(',').Append(F(o.RollDeg, "F3")).Append('\n');
                }
            }
            File.WriteAllText(Path.Combine(DumpDir, name + ".csv"), s.ToString());
        }

        static void DumpCourse(string name, Run r, int k0, ViewTrace v)
        {
            var s = new StringBuilder("t,x,y,speed,boost,heading,turn_rate,kickT\n");
            for (int k = Math.Max(0, k0 - 200); k < r.T.Count; k += 2)
                s.Append(F(r.T[k], "F3")).Append(',').Append(F(r.X[k], "F2")).Append(',').Append(F(r.Y[k], "F2")).Append(',').Append(F(r.Spd[k], "F3")).Append(',').Append(F(r.Boost[k], "F3")).Append(',').Append(F(r.Hd[k], "F3")).Append(',').Append(F(r.Rate[k], "F3")).Append(',').Append(F(r.KickT[k], "F3")).Append('\n');
            s.Append("# order at t=").Append(F(r.T[k0], "F3")).Append('\n');
            File.WriteAllText(Path.Combine(DumpDir, name + ".csv"), s.ToString());
            var q = new StringBuilder("t_into_flip,drawn_yaw,rules_heading,side_m,up_m,roll_deg\n");
            foreach (var row in v.rows) q.Append(string.Join(",", row.Select(x => F(x, "F4")))).Append('\n');
            File.WriteAllText(Path.Combine(DumpDir, name + "_view.csv"), q.ToString());
        }
    }

    /// <summary>The dash's numbers for the checks (Sim/ZWorld.ShuttleKick.cs's public curve).</summary>
    static class OzKickflip
    {
        /// <summary>The extra way of a whole flip from v0 px/s, in m.</summary>
        public static double ExtraM(double v0px) => ZObject.ShuttleKickExtraFor(v0px) / 8.0;
    }
}
