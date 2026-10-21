using System;
using System.Collections.Generic;
using System.Text;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// K390, the kickflip's second round, counted out of Play (run by Editor/OzShuttleKickflipCount.cs's menu check and the kit's headless
    /// runner, with the first round's G / A / C / L / R parts). Plain C#: the game's own curve, state and trail (View/OzShuttleKickflip.Core.cs,
    /// View/OzShuttleKickflip.Trail.cs). PASS / FAIL lines K1 .. K8:
    ///  K1 the round-2 numbers (longer: 1.5 .. 2 x the first round's 1.45 s; the arc several metres; at the cruise speed it flies on well
    ///     over its own length during the flip);
    ///  K2 the arc through whole flips at 60, 30 and 144 fps and on a jittery clock: out to the rising pod's side and up over the top, 0 at
    ///     both ends, no step in its position or its speed (each frame within what the curve's own speed and acceleration allow);
    ///  K3 the room: a part of the arc blocked = that much smaller; all of it = the flip in place; read once at the start;
    ///  K4 every way out (eased back in the pop, eased to the nearer level once the cruise ends) takes the arc home, no step;
    ///  K5 the clearance: the hull's lowest point (the ship's model: the pods' ends, the legs, the hull's top upside down; the pop's nose-up
    ///     tilt) through the flip never under where the first round's flip had it, and never more than 0.8 m under the level pads;
    ///  K6 the trails at 60, 30 and 144 fps and on a jittery clock: one chain from the nozzle, no gap over 0.45 m, a few metres long, the
    ///     same shape at any frame rate, still on a paused frame, gone soon after the flip; the attitude streaks short;
    ///  K7 the long whoosh: the roll's event once per flip (a full flip, a flip caught by the cruise's end), none for a flip eased back
    ///     before its roll;
    ///  K8 the drawn pose is the same function on every clock (the arc's top and side reached at 30 / 60 / 144 fps within 2 %).
    /// Test only.
    /// </summary>
    public static class OzShuttleKickflipCountK390
    {
        const double Dt60 = 1.0 / 60.0;
        /// <summary>The first round's numbers (1.45 s; roll from 0.18 s; nose up 8 deg over 0.9 s; hop 0.8 m), for the comparisons.</summary>
        const double OldTotal = 1.45, OldRollStart = 0.18, OldPitchSec = 0.9, OldPitch = 8.0, OldHop = 0.8;
        /// <summary>The hull's lowest point under the roll's axis by roll angle (m, every 15 deg 0 .. 180; the ship's model files, as the view's table).</summary>
        static readonly double[] Lowest = { 2.80, 3.42, 3.80, 3.92, 3.78, 3.38, 3.19, 3.39, 3.35, 3.12, 2.75, 2.34, 2.34 };
        const double HalfLength = 3.33, PivotY = 2.8;
        static readonly double[] NozzleL = { 2.45, 2.26, -0.2 }, Pivot = { 0, 2.8, 0 };

        static Action<bool, string> V;

        public static void Run(StringBuilder sb, Action<bool, string> v)
        {
            V = v;
            sb.AppendLine($"-- K round 2 (K390): the flip {OzKickflipCurve.TotalSec:F2} s (the first round {OldTotal:F2} s), roll from {OzKickflipCurve.RollStartSec:F2} s, nose up {OzKickflipCurve.PitchPeakDeg:F0} deg, hop {OzKickflipCurve.HopPeakM:F1} m, the arc {OzKickflipCurve.ArcSideM:F1} m to the side / {OzKickflipCurve.ArcRiseM:F1} m up");
            Numbers(); ArcClocks(); Room(); WaysOut(); Clearance(); Trails(); Whoosh();
        }

        static OzKickflipIn Steady(double remain = 300.0)
        {
            double v = 42.0 / 8.0, a = ZWorld.ShuttleAccelMax(42.0) / 8.0;
            return new OzKickflipIn
            {
                Alive = true, State = 2, AtHeight = true, OnScreen = true, SpeedMps = v, VmaxMps = v, AccelMps2 = 0, AccMaxMps2 = a, TurnRateDeg = 0,
                RemainM = remain, BrakeM = ZWorld.ShuttleBrakeDistance(42.0, 0.0, 42.0) / 8.0, FeetM = 12, Health = 3000, GoalX = 1500, GoalY = 900,
            };
        }

        /// <summary>A state armed and stepped to its first flip frame (id sets the roll's direction).</summary>
        static OzKickflipState Started(OzKickflipIn i, int id, out OzKickflipOut o)
        {
            var s = new OzKickflipState(id); s.Click(true, true, 300.0, 1500, 900); o = default;
            for (int k = 0; k < 1000 && s.Phase != OzKickflipPhase.Flip; k++) s.Step(i, Dt60, out o);
            return s;
        }

        // ------------------------------------------------------------------ K1
        static void Numbers()
        {
            double ratio = OzKickflipCurve.TotalSec / OldTotal, vm = 42.0 / 8.0;
            double way = vm * OzKickflipCurve.TotalSec, oldWay = vm * OldTotal;
            V(ratio >= 1.5 && ratio <= 2.0 && OzKickflipCurve.ArcSideM >= 2.5 && OzKickflipCurve.ArcRiseM >= 3.5 && OzKickflipCurve.PitchPeakDeg > OldPitch,
              $"K1 longer and bigger: {OzKickflipCurve.TotalSec:F2} s = {ratio:F2} x the first round (1.5 .. 2); the arc {OzKickflipCurve.ArcSideM:F1} m out to the side and {OzKickflipCurve.ArcRiseM:F1} m up (the first round: none, a 0.8 m hop); nose up {OzKickflipCurve.PitchPeakDeg:F0} deg (8); at the cruise speed ({vm:F2} m/s) it flies on {way:F1} m during the flip (the first round {oldWay:F1} m; the ship is 6.7 m long); peak roll rate {OzKickflipCurve.PeakRate:F0} deg/s; it flips only at {OzKickflipState.SteadySpeed * 100:F0} % of its top speed or more");
        }

        // ------------------------------------------------------------------ K2 / K8: the arc on every clock
        sealed class ArcTrack { public double maxSide, minSide, maxRise, endSide, endRise, startSide, startRise, maxDS, maxDR, boundS, boundR, maxD2S, maxD2R, bound2S, bound2R; public int frames, dir; }

        /// <summary>The arc's largest speed and acceleration along the curve (m/s, m/s^2; 1 ms steps), x the share k.</summary>
        static void ArcPeaks(out double vS, out double vR, out double aS, out double aR)
        {
            vS = vR = aS = aR = 0; double h = 1e-3; double ps = 0, pr = 0, pvs = 0, pvr = 0;
            for (int n = 0; n * h <= OzKickflipCurve.TotalSec + 1e-9; n++)
            {
                OzKickflipCurve.Arc(n * h, out double s, out _, out _, out double z, out _, out _);
                if (n > 0)
                {
                    double vs = (s - ps) / h, vr = (z - pr) / h; vS = Math.Max(vS, Math.Abs(vs)); vR = Math.Max(vR, Math.Abs(vr));
                    if (n > 1) { aS = Math.Max(aS, Math.Abs(vs - pvs) / h); aR = Math.Max(aR, Math.Abs(vr - pvr) / h); }
                    pvs = vs; pvr = vr;
                }
                ps = s; pr = z;
            }
        }

        static ArcTrack ArcRun(Func<int, double> dts, int id, double blocked = 0, double blockLater = double.NaN)
        {
            var i = Steady(); i.ArcBlocked = blocked;
            var s = Started(i, id, out var o); var tr = new ArcTrack { dir = s.Dir, startSide = o.SideM, startRise = o.RiseM };
            ArcPeaks(out double vS, out double vR, out double aS, out double aR);
            double ps = o.SideM, pr = o.RiseM, pvs = 0, pvr = 0, pdt = Dt60;
            for (int k = 0; k < 4000 && s.Phase != OzKickflipPhase.Idle; k++)
            {
                double dt = dts(k);
                if (!double.IsNaN(blockLater) && k == 20) i.ArcBlocked = blockLater;                       // the room changes mid-flip: nothing changes
                s.Step(i, dt, out o);
                double sd = o.Active ? o.SideM : 0, rz = o.Active ? o.RiseM : 0;
                tr.maxSide = Math.Max(tr.maxSide, sd); tr.minSide = Math.Min(tr.minSide, sd); tr.maxRise = Math.Max(tr.maxRise, rz);
                if (dt > 0)
                {
                    double ds = sd - ps, dr = rz - pr;
                    tr.maxDS = Math.Max(tr.maxDS, Math.Abs(ds)); tr.boundS = Math.Max(tr.boundS, vS * dt * 1.02 + 1e-6);
                    tr.maxDR = Math.Max(tr.maxDR, Math.Abs(dr)); tr.boundR = Math.Max(tr.boundR, vR * dt * 1.02 + 1e-6);
                    double vs = ds / dt, vr = dr / dt;
                    if (tr.frames > 0) { double dd = 0.5 * (dt + pdt); tr.maxD2S = Math.Max(tr.maxD2S, Math.Abs(vs - pvs)); tr.bound2S = Math.Max(tr.bound2S, aS * dd * 1.25 + 1e-4); tr.maxD2R = Math.Max(tr.maxD2R, Math.Abs(vr - pvr)); tr.bound2R = Math.Max(tr.bound2R, aR * dd * 1.25 + 1e-4); }
                    pvs = vs; pvr = vr; pdt = dt; tr.frames++;
                }
                ps = sd; pr = rz;
            }
            tr.endSide = ps; tr.endRise = pr;
            return tr;
        }

        static void ArcClocks()
        {
            var rnd = new Random(390);
            var jit = new double[4000]; for (int k = 0; k < jit.Length; k++) jit[k] = (k % 37 == 5) ? 0.0 : Math.Max(0.002, Dt60 * (0.55 + 0.9 * rnd.NextDouble()));
            var clocks = new (string, Func<int, double>)[] { ("60 fps", k => Dt60), ("30 fps", k => 1.0 / 30.0), ("144 fps", k => 1.0 / 144.0), ("a jittery clock with paused frames", k => jit[k]) };
            double s60 = 0, r60 = 0; var tops = new List<string>(); bool topsOk = true;
            foreach (var (name, dts) in clocks)
            {
                for (int id = 0; id < 2; id++)
                {
                    var t = ArcRun(dts, id);
                    double side = t.dir > 0 ? t.maxSide : -t.minSide, other = t.dir > 0 ? -t.minSide : t.maxSide;
                    bool ends = Math.Abs(t.startSide) < 1e-9 && Math.Abs(t.startRise) < 1e-9 && Math.Abs(t.endSide) < 1e-9 && Math.Abs(t.endRise) < 1e-9;
                    bool shape = side >= OzKickflipCurve.ArcSideM * 0.98 && side <= OzKickflipCurve.ArcSideM + 1e-6 && other >= OzKickflipCurve.ArcSideM * 0.98 && t.maxRise >= OzKickflipCurve.ArcRiseM * 0.98 && t.maxRise <= OzKickflipCurve.ArcRiseM + 1e-6;
                    bool smooth = t.maxDS <= t.boundS && t.maxDR <= t.boundR && t.maxD2S <= t.bound2S && t.maxD2R <= t.bound2R;
                    V(ends && shape && smooth, $"K2 the arc, {name}, roll direction {(t.dir > 0 ? "+" : "-")}: out {side:F2} m to the rising pod's side first, {other:F2} m the other way after the top, up {t.maxRise:F2} m (the arc {OzKickflipCurve.ArcSideM:F1} / {OzKickflipCurve.ArcRiseM:F1}); 0 at both ends; per frame the side moved at most {t.maxDS:F3} m (its speed allows {t.boundS:F3}), the rise {t.maxDR:F3} m ({t.boundR:F3}); its speed changed at most {t.maxD2S:F2} / {t.maxD2R:F2} m/s ({t.bound2S:F2} / {t.bound2R:F2}): no step ({t.frames} frames)");
                    if (name == "60 fps" && id == 0) { s60 = side; r60 = t.maxRise; }
                    else if (s60 > 0 && name != "a jittery clock with paused frames") { bool ok = Math.Abs(side - s60) <= 0.02 * s60 && Math.Abs(t.maxRise - r60) <= 0.02 * r60; topsOk &= ok; tops.Add($"{name} {side:F2} / {t.maxRise:F2}"); }
                }
            }
            V(topsOk, $"K8 the same flip on every clock: the arc's side / top at 60 fps {s60:F2} / {r60:F2} m; {string.Join(", ", tops)} (within 2 %)");
        }

        // ------------------------------------------------------------------ K3 the room
        static void Room()
        {
            var half = ArcRun(k => Dt60, 0, 0.5); var none = ArcRun(k => Dt60, 0, 1.0); var nan = ArcRun(k => Dt60, 0, double.NaN); var late = ArcRun(k => Dt60, 0, 0.0, 1.0);
            double hs = Math.Max(half.maxSide, -half.minSide);
            V(Math.Abs(hs - 0.5 * OzKickflipCurve.ArcSideM) < 0.05 && Math.Abs(half.maxRise - 0.5 * OzKickflipCurve.ArcRiseM) < 0.05
              && none.maxSide == 0 && none.minSide == 0 && none.maxRise == 0 && nan.maxRise >= OzKickflipCurve.ArcRiseM * 0.98 && late.maxRise >= OzKickflipCurve.ArcRiseM * 0.98,
              $"K3 the room: half the arc blocked = {hs:F2} m / {half.maxRise:F2} m (half of {OzKickflipCurve.ArcSideM:F1} / {OzKickflipCurve.ArcRiseM:F1}); all of it blocked = the flip in place ({none.maxSide:F2} / {none.maxRise:F2} m: the first round's way round its axis, with the longer timing); an unread room (NaN) = the whole arc ({nan.maxRise:F2} m); the room closing in mid-flip changes nothing (read once at the start: {late.maxRise:F2} m)");
        }

        // ------------------------------------------------------------------ K4 the ways out
        static void WaysOut()
        {
            int n = 0, home = 0; double worst = 0, worstRise = 0, worstShare = 0, worstSpeed = 0; string bad = "";
            ArcPeaks(out double vS, out double vR, out double aS, out double aR);
            foreach (bool hard in new[] { false, true })
                for (int m = 0; m < 30; m++)
                {
                    double at = hard ? 0.05 + m * (OzKickflipCurve.TotalSec - 0.1) / 29.0 : 0.01 + m * (OzKickflipCurve.RollStartSec - 0.02) / 29.0;
                    var i = Steady(); var s = Started(i, m, out var o); double t = 0, ps = o.SideM, pr = o.RiseM, pvs = 0, pvz = 0; bool hit = false;
                    for (int k = 0; k < 2000 && s.Phase != OzKickflipPhase.Idle; k++)
                    {
                        if (!hit && t >= at) { hit = true; if (hard) i.State = 3; else i.Strafing = true; }
                        s.Step(i, Dt60, out o); t += Dt60;
                        double sd = o.Active ? o.SideM : 0, rz = o.Active ? o.RiseM : 0;
                        double vs = (sd - ps) / Dt60, vz = (rz - pr) / Dt60;                                    // the arc's speed this frame
                        if (k > 0) worstShare = Math.Max(worstShare, Math.Max(Math.Abs(vs - pvs) / (aS * Dt60 * 3.0 + 1e-6), Math.Abs(vz - pvz) / (aR * Dt60 * 3.0 + 1e-6)));   // its change: within three times the curve's own acceleration (a quintic home from where it is, in 0.75 / 0.9 s)
                        worstSpeed = Math.Max(worstSpeed, Math.Max(Math.Abs(vs) / vS, Math.Abs(vz) / vR));
                        worst = Math.Max(worst, Math.Abs(sd - ps)); worstRise = Math.Max(worstRise, Math.Abs(rz - pr)); ps = sd; pr = rz; pvs = vs; pvz = vz;
                    }
                    n++;
                    if (s.Phase == OzKickflipPhase.Idle && Math.Abs(ps) < 1e-9 && Math.Abs(pr) < 1e-9) home++; else bad += $" {(hard ? "hard" : "soft")}@{at:F2}";
                }
            V(home == n && worstShare <= 1.0 && worstSpeed <= 1.6, $"K4 every way out takes the arc home: {home} of {n} (a strafe at 30 moments of the pop: eased back over {OzKickflipState.AbortSec:F2} s; the cruise ending at 30 moments of the flip: eased to the nearer level over {OzKickflipState.WrapSec:F2} s){bad}; no step: the largest change in one 60 fps frame {worst:F3} m side, {worstRise:F3} m rise (the curve's own at its fastest {vS * Dt60:F3} / {vR * Dt60:F3}; the way home at most {worstSpeed:F2} x the curve's top speed), its speed's change per frame at most {worstShare * 100:F0} % of three times the curve's own acceleration (the quintic home starts from the arc's place, speed and acceleration: no step)");
        }

        // ------------------------------------------------------------------ K5 the clearance
        static double Low(double rollDeg)
        {
            double a = Math.Abs(rollDeg) % 360.0; if (a > 180) a = 360 - a;
            double f = a / 15.0; int i = Math.Min(11, (int)f);
            return Lowest[i] + (Lowest[i + 1] - Lowest[i]) * (f - i);
        }

        static void Clearance()
        {
            // round 2: the hull's lowest point (over the level pads) frame by frame at 60 fps; the first round's by its own numbers
            var i = Steady(); var s = Started(i, 0, out var o); double minNew = 0, atNew = 0, t = 0;
            for (int k = 0; k < 2000 && s.Phase != OzKickflipPhase.Idle; k++)
            {
                if (o.Active)
                {
                    double low = PivotY - Low(o.RollDeg) + o.HopM + o.RiseM - HalfLength * Math.Sin(o.PitchUpDeg * Math.PI / 180.0);
                    if (low < minNew) { minNew = low; atNew = Math.Abs(o.RollDeg); }
                }
                s.Step(i, Dt60, out o); t += Dt60;
            }
            double minOld = 0, atOld = 0;
            for (double tt = 0; tt <= OldTotal; tt += 1e-3)
            {
                double u = Math.Min(1, Math.Max(0, (tt - OldRollStart) / (OldTotal - OldRollStart))), r = 360.0 * u * u * u * (20.0 + u * (-45.0 + u * (36.0 - 10.0 * u)));
                double p = OldPitch * OzKickflipCurve.Bump(tt / OldPitchSec, 3, 5, out _, out _), h = OldHop * OzKickflipCurve.Bump(tt / OldTotal, 3, 3, out _, out _);
                double low = PivotY - Low(r) + h - HalfLength * Math.Sin(p * Math.PI / 180.0);
                if (low < minOld) { minOld = low; atOld = r; }
            }
            V(minNew >= minOld - 0.05 && minNew >= -0.8,
              $"K5 the clearance: the drawn hull's lowest point through the flip {minNew:F2} m under the level pads at its lowest (at {atNew:F0} deg of roll), the first round's flip in place {minOld:F2} m (at {atOld:F0} deg): the arc only lifts it; at the cruise ({ZWorld.ShuttleCruiseM:F0} m, the pads at least {OzKickflipState.SteadyFeetM:F0} m over the ground to start) the ground, trees and buildings stay well clear; beside its line the view reads the room before it starts (OzPlayableShuttle.KickTrails.cs: 1 m over hard tops, 2 m over crowns)");
        }

        // ------------------------------------------------------------------ K6 the trails
        /// <summary>The drawn pose's point (root space p) and the jet's axis at a frame of the flip: the level ship flying +z at v, its pads at 12 m.</summary>
        static void World(in OzKickflipOut o, double along, double[] p, double[] w, out double ax, out double ay, out double az)
        {
            double r = (o.Active ? o.RollDeg : 0) * Math.PI / 180.0, pt = -(o.Active ? o.PitchUpDeg : 0) * Math.PI / 180.0;
            double x = p[0] - Pivot[0], y = p[1] - Pivot[1], z = p[2] - Pivot[2];
            double x1 = x * Math.Cos(r) - y * Math.Sin(r), y1 = x * Math.Sin(r) + y * Math.Cos(r), z1 = z;                 // Euler(0, 0, roll)
            double y2 = y1 * Math.Cos(pt) - z1 * Math.Sin(pt), z2 = y1 * Math.Sin(pt) + z1 * Math.Cos(pt);                 // then Euler(-pitch, 0, 0)
            double sd = o.Active ? o.SideM : 0, rz = o.Active ? o.RiseM + o.HopM : 0;
            w[0] = x1 + Pivot[0] + sd; w[1] = y2 + Pivot[1] + 12.0 + rz; w[2] = z2 + Pivot[2] + along;
            double dx = 0, dy = -1, dz = 0;
            double ex = dx * Math.Cos(r) - dy * Math.Sin(r), ey = dx * Math.Sin(r) + dy * Math.Cos(r);
            ax = ex; ay = ey * Math.Cos(pt) - dz * Math.Sin(pt); az = ey * Math.Sin(pt) + dz * Math.Cos(pt);
        }

        sealed class TrailTrack { public double maxGap, maxLen, maxReach, reachMid, maxBright, goneAfter = -1; public int overflow, frames, maxPts; public bool pausedMoved; public List<double[]> snap; }

        /// <summary>The view's numbers (OzPlayableShuttle.KickTrails.cs: KickTrailInterval / Life / Drag / Speed / Carry), kept the same here.</summary>
        static TrailTrack TrailRun(Func<int, double> dts, double snapAt, double life = 0.55, double speed = 8, double drag = 3, double carry = 0.5, double interval = 1.0 / 120.0)
        {
            var i = Steady(); var s = Started(i, 0, out var o);
            var tr = new OzKickTrail(interval, life, drag); var tt = new TrailTrack();
            double v = 42.0 / 8.0, t = 0, along = 0, tEnd = -1; var w = new double[3]; bool snapped = false;
            for (int k = 0; k < 4000; k++)
            {
                double dt = dts(k);
                if (k > 0) { s.Step(i, dt, out o); t += dt; along += v * dt; }                          // as the game: the flip's step, then the jets of this pose with the same dt
                else dt = 0;
                bool burning = o.Active && s.Phase == OzKickflipPhase.Flip && s.T < OzKickflipCurve.TotalSec;
                World(o, along, NozzleL, w, out double ax, out double ay, out double az);
                double mx = w[0] + ax * 0.35, my = w[1] + ay * 0.35, mz = w[2] + az * 0.35;
                double x0 = 0, y0 = 0, z0 = 0; int n0 = tr.Count; if (dt == 0 && n0 > 1) tr.Point(n0 - 1, out x0, out y0, out z0, out _, out _);
                tr.Step(dt, mx, my, mz, ax, ay, az, speed, 0, 0, v * carry, burning ? 1.0 : 0.0, burning);
                if (dt == 0 && n0 > 1 && tr.Count == n0) { tr.Point(n0 - 1, out double x1, out double y1, out double z1, out _, out _); if (Math.Abs(x1 - x0) + Math.Abs(y1 - y0) + Math.Abs(z1 - z0) > 1e-12) tt.pausedMoved = true; }
                if (burning)
                {
                    double len = tr.Length(out double gap), reach = tr.Reach(); tt.maxBright = Math.Max(tt.maxBright, tr.Reach(0.4)); tt.maxGap = Math.Max(tt.maxGap, gap); tt.maxLen = Math.Max(tt.maxLen, len); tt.maxReach = Math.Max(tt.maxReach, reach); tt.maxPts = Math.Max(tt.maxPts, tr.Count);
                    if (s.T >= OzKickflipCurve.RollStartSec + 0.4 * OzKickflipCurve.RollSec && tt.reachMid == 0) tt.reachMid = reach;
                    tt.frames++;
                }
                if (!snapped && s.Phase == OzKickflipPhase.Flip && s.T >= snapAt - 1e-9)
                {
                    snapped = true; tt.snap = new List<double[]>();
                    for (int j = 0; j < tr.Count; j++) { tr.Point(j, out double x, out double y, out double z, out _, out _); tt.snap.Add(new[] { x, y, z - along }); }   // in the ship's travelling frame
                }
                if (s.Phase == OzKickflipPhase.Idle && tEnd < 0) tEnd = t;
                if (s.Phase == OzKickflipPhase.Idle && tt.goneAfter < 0 && !tr.Live) tt.goneAfter = t - tEnd;
                if (s.Phase == OzKickflipPhase.Idle && !tr.Live) break;
            }
            tt.overflow = tr.Overflow;
            return tt;
        }

        /// <summary>Two chains compared: each resampled at 24 points by length; the largest distance between them (m).</summary>
        static double Shape(List<double[]> a, List<double[]> b)
        {
            if (a == null || b == null || a.Count < 2 || b.Count < 2) return 99;
            var ra = Resample(a, 24); var rb = Resample(b, 24); double m = 0;
            for (int k = 0; k < 24; k++) m = Math.Max(m, Math.Sqrt(Sq(ra[k][0] - rb[k][0]) + Sq(ra[k][1] - rb[k][1]) + Sq(ra[k][2] - rb[k][2])));
            return m;
        }

        static double Sq(double x) => x * x;

        static List<double[]> Resample(List<double[]> p, int n)
        {
            var cum = new double[p.Count]; for (int k = 1; k < p.Count; k++) cum[k] = cum[k - 1] + Math.Sqrt(Sq(p[k][0] - p[k - 1][0]) + Sq(p[k][1] - p[k - 1][1]) + Sq(p[k][2] - p[k - 1][2]));
            var o = new List<double[]>(); int j = 0;
            for (int k = 0; k < n; k++)
            {
                double d = cum[cum.Length - 1] * k / (n - 1);
                while (j < p.Count - 2 && cum[j + 1] < d) j++;
                double f = cum[j + 1] > cum[j] ? (d - cum[j]) / (cum[j + 1] - cum[j]) : 0;
                o.Add(new[] { p[j][0] + (p[j + 1][0] - p[j][0]) * f, p[j][1] + (p[j + 1][1] - p[j][1]) * f, p[j][2] + (p[j + 1][2] - p[j][2]) * f });
            }
            return o;
        }

        static void Trails()
        {
            var rnd = new Random(3901);
            var jit = new double[4000]; for (int k = 0; k < jit.Length; k++) jit[k] = (k % 29 == 7) ? 0.0 : Math.Max(0.002, Dt60 * (0.55 + 0.9 * rnd.NextDouble()));
            double snapAt = 1.0;                                                                                   // a moment every clock reaches exactly (30, 60 and 144 frames), near the fastest roll
            var t60 = TrailRun(k => Dt60, snapAt); var t30 = TrailRun(k => 1.0 / 30.0, snapAt); var t144 = TrailRun(k => 1.0 / 144.0, snapAt); var tj = TrailRun(k => jit[k], snapAt);
            foreach (var (name, t) in new[] { ("60 fps", t60), ("30 fps", t30), ("144 fps", t144), ("a jittery clock with paused frames", tj) })
                V(t.maxGap <= 0.45 && t.maxBright >= 2.0 && t.maxBright <= 6.0 && t.maxReach <= 12.0 && t.overflow == 0 && !t.pausedMoved && t.goneAfter >= 0 && t.goneAfter <= 0.55 + 0.05,
                  $"K6 a main jet's trail, {name}: one chain from the nozzle through the roll ({t.frames} frames burning, up to {t.maxPts} points): the largest gap between two neighbours {t.maxGap:F2} m (at most 0.45: the ribbon joins them smoothly), its bright part (the youngest 40 %) reaches {t.maxBright:F1} m from the nozzle at most (2 .. 6: a few metres), its faint tail {t.maxReach:F1} m (at most 12), following the nozzle's own way round the roll ({t.reachMid:F1} m at the fastest roll; {t.maxLen:F1} m along its curl), no point lost ({t.overflow}), still on a paused frame ({(t.pausedMoved ? "MOVED" : "yes")}), gone {t.goneAfter:F2} s after the flip's end");
            double d30 = Shape(t60.snap, t30.snap), d144 = Shape(t60.snap, t144.snap);
            V(d30 <= 0.3 && d144 <= 0.3, $"K6 the same trail at any frame rate (points born at a fixed rate, the mouth and the axis interpolated between frames): mid-roll ({snapAt:F2} s) its shape at 30 fps is within {d30:F2} m of the 60 fps one, at 144 fps within {d144:F2} m (at most 0.3)");
            var st = TrailRun(k => Dt60, snapAt, 0.22, 9, 4, 0.5);
            V(st.maxBright >= 0.5 && st.maxBright <= 3.0 && st.maxReach <= 6.0 && st.maxGap <= 0.45, $"K6 an attitude jet's streak (life 0.22 s, 9 m/s; drawn here from a main nozzle's place through the whole roll, the worst sweep: in the game it fires only while its pair drives the roll): a short smooth streak, its bright part reaching {st.maxBright:F1} m (0.5 .. 3), its faint tail {st.maxReach:F1} m (at most 6), the largest gap {st.maxGap:F2} m");
        }

        // ------------------------------------------------------------------ K7 the whoosh
        static void Whoosh()
        {
            int full = 0, wrap = 0, abort = 0;
            { var i = Steady(); var s = Started(i, 0, out var o); for (int k = 0; k < 2000 && s.Phase != OzKickflipPhase.Idle; k++) { s.Step(i, Dt60, out o); if ((o.Events & OzKickflipState.EvRoll) != 0) full++; } }
            { var i = Steady(); var s = Started(i, 0, out var o); double t = 0; for (int k = 0; k < 2000 && s.Phase != OzKickflipPhase.Idle; k++) { if (t >= 1.2) i.State = 3; s.Step(i, Dt60, out o); t += Dt60; if ((o.Events & OzKickflipState.EvRoll) != 0) wrap++; } }
            { var i = Steady(); var s = Started(i, 0, out var o); i.Strafing = true; for (int k = 0; k < 2000 && s.Phase != OzKickflipPhase.Idle; k++) { s.Step(i, Dt60, out o); if ((o.Events & OzKickflipState.EvRoll) != 0) abort++; } }
            V(full == 1 && wrap == 1 && abort == 0, $"K7 the long whoosh (fired on the roll's event, at {OzKickflipCurve.RollStartSec:F2} s, through the {OzKickflipCurve.RollSec:F2} s roll): once in a full flip ({full}), once in a flip the cruise's end catches mid-roll ({wrap}), none in a flip eased back in its pop ({abort}: the pop's own sound only)");
        }
    }
}
