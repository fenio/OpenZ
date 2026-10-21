using System;
using System.Text;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// K423, the kickflip keeps travelling forward through the whole roll, counted out of Play (run by Editor/OzShuttleKickflipCount.cs's menu
    /// check and the kit's headless runner, after the K390 and K411 parts). Plain C#: the game's own curve and state (View/OzShuttleKickflip.Core.cs),
    /// the lead (View/OzShuttleKickflip.Forward.cs) and the rules (Sim). The game's camera is rebuilt here as View/RtsCamera.cs places it (pitch 55
    /// below the horizon, yaw 0 = north up, h m over its focus, vertical FOV 32, 1920 x 1080 px). PASS / FAIL lines F1 .. F5:
    ///  F1 the lead from the camera's picture (OzKickflipForward.LeadSolve): no offset = 0; a rise flying toward / away from the camera answered
    ///     by a lead the other way, across the screen about 0; the picture after it level with the level hull's along the flight's screen line
    ///     (within 0.01 px, 24 headings); behind the camera / the switch off = 0;
    ///  F2 the lead through whole flips at 60, 30 and 144 fps and on a jittery clock (a still camera, two headings): 0 at both ends, no step (each
    ///     frame within what its own peak rate allows);
    ///  F3 every way out (eased back in the pop, eased to the nearer level once the cruise ends) takes the lead home with the hop and the arc,
    ///     no step, 0 at the end;
    ///  F4 THE SCREEN TWIN: the flip flown in 8 headings under the 70 m and the 35 m camera (still): the hull's middle's on-screen pace along the
    ///     flight's screen line per frame against the level path's: BEFORE (the round-2 pose as the exe draws it) it stalls and runs backwards
    ///     the spiral's sideways swing on screen is kept; under a camera following the ship the drawn hull stays level with the level ship
    ///     along its screen line in every frame's own picture (within 0.5 px);
    ///  F5 THE WAY LEFT with a route (DV125, the rules as they are): a short first leg, a right-angle turn at its middle point, a long second
    ///     leg: BEFORE (the way left to the route's end) the flip runs into the slow-down for the turn; AFTER (to the next point) every flip
    ///     flies at its cruise speed from start to end, and the long leg still gets its flip.
    /// Test only.
    /// </summary>
    public static class OzShuttleKickflipCountK423
    {
        const double Dt60 = 1.0 / 60.0, V0 = 42.0 / 8.0, CruiseY = 12.0 + 2.8;
        static Action<bool, string> V;
        static StringBuilder Sb;
        static string Settings;

        /// <summary>settings: the game settings text (F5's route run on the rules; null = F5 fails, it needs them).</summary>
        public static void Run(StringBuilder sb, Action<bool, string> v, string settings)
        {
            V = v; Sb = sb; Settings = settings;
            sb.AppendLine($"-- F K423 the flip keeps travelling forward on screen (the lead along the flight, at most {OzKickflipForward.LeadMaxM:F0} m; the way left to a route's next point)");
            bool on0 = OzKickflipForward.LeadOn;
            try { OzKickflipForward.LeadOn = true; Solver(); LeadClocks(); WaysOut(); Screen(); Route(); }
            finally { OzKickflipForward.LeadOn = on0; }
        }

        static OzKickflipIn Steady(double remain = 300.0)
        {
            double a = ZWorld.ShuttleAccelMax(42.0) / 8.0;
            return new OzKickflipIn
            {
                Alive = true, State = 2, AtHeight = true, OnScreen = true, SpeedMps = V0, VmaxMps = V0, AccelMps2 = 0, AccMaxMps2 = a, TurnRateDeg = 0,
                RemainM = remain, BrakeM = ZWorld.ShuttleBrakeDistance(42.0, 0.0, 42.0) / 8.0, FeetM = 12, Health = 3000, GoalX = 1500, GoalY = 900,
            };
        }

        static OzKickflipState Started(OzKickflipIn i, int id, out OzKickflipOut o)
        {
            var s = new OzKickflipState(id); s.Click(true, true, 300.0, 1500, 900); o = default;
            for (int k = 0; k < 1000 && s.Phase != OzKickflipPhase.Flip; k++) s.Step(i, Dt60, out o);
            return s;
        }

        /// <summary>The game's RTS camera (View/RtsCamera.cs Place) over the focus (focusX, focusZ), and its picture (px, y up as Unity's screen points).</summary>
        struct Cam : IOzKickScreen
        {
            const double W = 1920, H = 1080;
            double cx, cy, cz, fy, fz, uy, uz, F;
            public static Cam At(double pitch, double h, double focusX, double focusZ)
            {
                double p = pitch * Math.PI / 180.0;
                return new Cam { cx = focusX, cy = h, cz = focusZ - h / Math.Tan(p), fy = -Math.Sin(p), fz = Math.Cos(p), uy = Math.Cos(p), uz = Math.Sin(p), F = (H / 2) / Math.Tan(16.0 * Math.PI / 180.0) };
            }
            public bool S(double x, double y, double z, out double X, out double Y)
            {
                double ax = x - cx, ay = y - cy, az = z - cz, yc = ay * uy + az * uz, zc = ay * fy + az * fz;
                X = W / 2 + ax / zc * F; Y = H / 2 + yc / zc * F; return zc > 0.1;
            }
        }

        /// <summary>The drawn hull's middle for a level middle b, the flight (dx, dz), its right (nx, nz) and this frame's flip output, with the lead
        /// solved from the camera as the view does (lead = false: the round-2 pose as the exe draws it).</summary>
        static void Drawn(ref Cam cam, bool lead, double bx, double by, double bz, double dx, double dz, double nx, double nz, in OzKickflipOut o, out double x, out double y, out double z, out double L)
        {
            double ox = nx * o.SideM, oy = o.HopM + o.RiseM, oz = nz * o.SideM;
            L = lead ? OzKickflipForward.LeadSolve(ref cam, bx, by, bz, dx, dz, ox, oy, oz) : 0.0;
            x = bx + ox + dx * L; y = by + oy; z = bz + oz + dz * L;
        }

        // ------------------------------------------------------------------ F1
        static void Solver()
        {
            var c = Cam.At(55, 70, 0, 0); double cot = 1.0 / Math.Tan(55 * Math.PI / 180.0), r = 5.6;
            double none = OzKickflipForward.LeadSolve(ref c, 0, CruiseY, 0, 0, 1, 0, 0, 0);
            double away = OzKickflipForward.LeadSolve(ref c, 0, CruiseY, 0, 0, 1, 0, r, 0), toward = OzKickflipForward.LeadSolve(ref c, 0, CruiseY, 0, 0, -1, 0, r, 0);
            double across = OzKickflipForward.LeadSolve(ref c, 0, CruiseY, 0, 1, 0, 0, r, 0);
            double worst = 0;                                                                                 // the picture after the lead: level along the flight's screen line
            for (int hd = 0; hd < 360; hd += 15)
                for (double side = -3.2; side <= 3.21; side += 1.6)
                    for (double rise = 0; rise <= 5.61; rise += 1.4)
                    {
                        double a = hd * Math.PI / 180.0, dx = Math.Sin(a), dz = Math.Cos(a), nx = dz, nz = -dx;
                        double L = OzKickflipForward.LeadSolve(ref c, 3, CruiseY, -4, dx, dz, nx * side, rise, nz * side);
                        c.S(3, CruiseY, -4, out double b0x, out double b0y); c.S(3 + dx, CruiseY, -4 + dz, out double b1x, out double b1y);
                        double ux = b1x - b0x, uy = b1y - b0y, ul = Math.Sqrt(ux * ux + uy * uy); ux /= ul; uy /= ul;
                        c.S(3 + nx * side + dx * L, CruiseY + rise, -4 + nz * side + dz * L, out double sx, out double sy);
                        worst = Math.Max(worst, Math.Abs((sx - b0x) * ux + (sy - b0y) * uy));
                    }
            double back = OzKickflipForward.LeadSolve(ref c, 0, CruiseY, -200, 0, 1, 0, r, 0);
            OzKickflipForward.LeadOn = false; double off = OzKickflipForward.LeadSolve(ref c, 0, CruiseY, 0, 0, 1, 0, r, 0); OzKickflipForward.LeadOn = true;
            bool ok = none == 0.0 && away < -0.5 * cot * r && toward > 0.5 * cot * r && Math.Abs(across) < 0.05 && worst < 0.01 && back == 0.0 && off == 0.0 && Math.Abs(away) <= OzKickflipForward.LeadMaxM;
            V(ok, $"F1 the lead from the camera's picture (70 m, pitch 55, the hull at the screen's middle, a {r:F1} m rise): flying away {away:+0.00;-0.00} m, toward {toward:+0.00;-0.00} m (the slant alone would say {cot * r:F2}), across {across:+0.000;-0.000} m; no offset {none:F1}; after it the picture is level with the level hull along the flight's screen line within {worst:F4} px (24 headings x 5 swings x 5 rises); behind the camera {back:F1}; the switch off {off:F1}");
        }

        // ------------------------------------------------------------------ F2
        /// <summary>One flip under a still 70 m camera, heading hd, on the clock: the lead per frame; its largest change against its own peak rate
        /// (from a 1 ms clock).</summary>
        static void LeadRun(double hd, Func<int, double> clock, out double first, out double last, out double maxAbs, out double worstOver, out double rate)
        {
            double a = hd * Math.PI / 180.0, dx = Math.Sin(a), dz = Math.Cos(a), nx = dz, nz = -dx, T = OzKickflipCurve.TotalSec;
            var cam = Cam.At(55, 70, 0, 0); double sx0 = -dx * V0 * T / 2, sz0 = -dz * V0 * T / 2;
            rate = 0;
            {
                var s1 = Started(Steady(), 7, out var o1); double t = 0, prev = 0; int n = 0;
                while (s1.Phase != OzKickflipPhase.Idle && n++ < 10000)
                {
                    s1.Step(Steady(), 1e-3, out o1); t += 1e-3; if (!o1.Active) break;
                    Drawn(ref cam, true, sx0 + dx * V0 * t, CruiseY, sz0 + dz * V0 * t, dx, dz, nx, nz, o1, out _, out _, out _, out double L1);
                    rate = Math.Max(rate, Math.Abs(L1 - prev) / 1e-3); prev = L1;
                }
            }
            var s = Started(Steady(), 7, out var o); double tt = 0, pv = 0; int k = 0; first = 0; last = 0; maxAbs = 0; worstOver = 0; bool f = true;
            while (s.Phase != OzKickflipPhase.Idle && k < 5000)
            {
                double dt = clock(k++); s.Step(Steady(), dt, out o); tt += dt;
                double L = 0; if (o.Active) Drawn(ref cam, true, sx0 + dx * V0 * tt, CruiseY, sz0 + dz * V0 * tt, dx, dz, nx, nz, o, out _, out _, out _, out L);
                if (f) { first = L; f = false; }
                maxAbs = Math.Max(maxAbs, Math.Abs(L)); worstOver = Math.Max(worstOver, Math.Abs(L - pv) - (rate * dt * 1.05 + 1e-6)); pv = L; last = L;
                if (!o.Active) break;
            }
        }

        static void LeadClocks()
        {
            var clocks = new (string name, Func<int, double> dt)[] { ("60 fps", n => Dt60), ("30 fps", n => 1.0 / 30.0), ("144 fps", n => 1.0 / 144.0), ("jittery", n => (n % 7 == 3 ? 0.045 : n % 5 == 1 ? 0.004 : n % 11 == 0 ? 0.0 : 0.017)) };
            foreach (var hd in new[] { 0.0, 135.0 })
                foreach (var c in clocks)
                {
                    LeadRun(hd, c.dt, out double first, out double last, out double maxAbs, out double over, out double rate);
                    V(Math.Abs(first) < 0.05 && Math.Abs(last) < 1e-9 && over <= 0 && maxAbs > 1.0 && maxAbs <= OzKickflipForward.LeadMaxM,
                      $"F2 the lead at {c.name}, heading {hd:F0} deg (70 m camera): {first:F3} m on the first frame, {last:F4} m once level again, largest {maxAbs:F2} m, no step (each frame within its own peak rate {rate:F1} m/s: worst over {over:+0.0000;-0.0000;0} m)");
                }
        }

        // ------------------------------------------------------------------ F3
        static void WaysOut()
        {
            var cam = Cam.At(55, 70, 0, 0); double T = OzKickflipCurve.TotalSec, dx = 0, dz = -1, nx = -1, nz = 0;      // toward the camera (the biggest lead)
            LeadRun(180.0, n => Dt60, out _, out _, out _, out _, out double rate);
            int runs = 0, bad = 0; double worst = 0, worstEnd = 0;
            for (int m = 1; m <= 35; m++)
                for (int kind = 0; kind < 2; kind++)
                {
                    double at = kind == 0 ? OzKickflipCurve.RollStartSec * m / 36.0 : T * m / 36.0;
                    var i = Steady(); var s = Started(i, 3, out var o); double prev = 0, t = 0; int n = 0;
                    while (s.Phase != OzKickflipPhase.Idle && n++ < 3000)
                    {
                        var j = i; t += Dt60;
                        if (t >= at) { if (kind == 0) j.Strafing = true; else j.State = 3; }
                        s.Step(j, Dt60, out o);
                        double L = 0; if (o.Active) Drawn(ref cam, true, -dx * V0 * T / 2 + dx * V0 * t, CruiseY, -dz * V0 * T / 2 + dz * V0 * t, dx, dz, nx, nz, o, out _, out _, out _, out L);
                        double step = Math.Abs(L - prev); worst = Math.Max(worst, step);
                        if (step > 2.5 * rate * Dt60 + 1e-6) bad++;
                        prev = L;
                    }
                    worstEnd = Math.Max(worstEnd, Math.Abs(prev)); runs++;
                }
            V(bad == 0 && worstEnd < 1e-9, $"F3 every way out ({runs} runs: a strafe in the pop at 35 moments, the cruise ending under it at 35 moments; flying toward the 70 m camera): the lead goes home with the hop and the arc, no step (largest change a frame {worst:F3} m; the full flip's own at 60 fps {rate * Dt60:F3} m), 0 at the end (worst {worstEnd:E1} m)");
        }

        // ------------------------------------------------------------------ F4 the screen twin
        sealed class ScreenRun { public double minShare = 9, maxShare = -9, perp, travel, lead, offset; public int stall, back, frames; }

        /// <summary>The flip flown along heading deg (0 = away from the camera, 90 = to the right across the screen); camH = the camera's height,
        /// follow = the camera's focus moving with the ship (the measure projects last frame's points with this frame's camera, as the view's
        /// proof eye does). The hull's middle's on-screen pace along the flight's screen line per frame against the level path's.</summary>
        static ScreenRun Fly(double headingDeg, bool lead, int id, double camH, bool follow)
        {
            double hd = headingDeg * Math.PI / 180.0, dx = Math.Sin(hd), dz = Math.Cos(hd), nx = dz, nz = -dx, T = OzKickflipCurve.TotalSec;
            double sx0 = -dx * V0 * T / 2, sz0 = -dz * V0 * T / 2;                                                            // the flip's middle under the camera's focus
            var i = Steady(); var s = Started(i, id, out var o);
            var r = new ScreenRun(); double t = 0, pbx = sx0, pbz = sz0, pdx = sx0, pdy = CruiseY, pdz = sz0;
            while (s.Phase != OzKickflipPhase.Idle && t < T + 0.5)
            {
                s.Step(i, Dt60, out o); t += Dt60;
                if (!o.Active) break;
                double bx = sx0 + dx * V0 * t, bz = sz0 + dz * V0 * t;
                var cam = follow ? Cam.At(55, camH, bx, bz) : Cam.At(55, camH, 0, 0);
                Drawn(ref cam, lead, bx, CruiseY, bz, dx, dz, nx, nz, o, out double qx, out double qy, out double qz, out double L);
                r.lead = Math.Max(r.lead, Math.Abs(L));
                {   // this frame's own picture: the drawn hull against the level hull along the flight's screen line (px)
                    cam.S(bx, CruiseY, bz, out double l0x, out double l0y); cam.S(bx + dx, CruiseY, bz + dz, out double l1x, out double l1y); cam.S(qx, qy, qz, out double q0x, out double q0y);
                    double vx = l1x - l0x, vy = l1y - l0y, vl = Math.Sqrt(vx * vx + vy * vy);
                    if (vl > 1e-6) r.offset = Math.Max(r.offset, Math.Abs(((q0x - l0x) * vx + (q0y - l0y) * vy) / vl));
                }
                cam.S(pbx, CruiseY, pbz, out double a0x, out double a0y); cam.S(bx, CruiseY, bz, out double a1x, out double a1y);
                cam.S(pdx, pdy, pdz, out double c0x, out double c0y); cam.S(qx, qy, qz, out double c1x, out double c1y);
                double ux = a1x - a0x, uy = a1y - a0y, ul = Math.Sqrt(ux * ux + uy * uy);
                if (t <= T + 1e-9 && ul > 1e-6)
                {
                    ux /= ul; uy /= ul;
                    double sh = ((c1x - c0x) * ux + (c1y - c0y) * uy) / ul; r.minShare = Math.Min(r.minShare, sh); r.maxShare = Math.Max(r.maxShare, sh); r.frames++;
                    if (sh < 0.5) r.stall++;
                    if (sh < 0) r.back++;
                    r.travel += (c1x - c0x) * ux + (c1y - c0y) * uy;
                    r.perp = Math.Max(r.perp, Math.Abs((c1x - a1x) * -uy + (c1y - a1y) * ux));
                }
                pbx = bx; pbz = bz; pdx = qx; pdy = qy; pdz = qz;
            }
            return r;
        }

        static void Screen()
        {
            var heads = new[] { (0.0, "away (up the screen)"), (45.0, "away, diagonal"), (90.0, "across"), (135.0, "toward, diagonal"), (180.0, "toward (down the screen)"), (225.0, "toward, diagonal left"), (270.0, "across, left"), (315.0, "away, diagonal left") };
            int stalledBefore = 0, backBefore = 0; bool after = true; var lines = new StringBuilder();
            foreach (var (hd, name) in heads)
            {
                var b = Fly(hd, false, 7, 70, false); var a = Fly(hd, true, 7, 70, false);
                var b35 = Fly(hd, false, 7, 35, false); var a35 = Fly(hd, true, 7, 35, false); var af = Fly(hd, true, 7, 70, true); var bf = Fly(hd, false, 7, 70, true);
                if (b.stall > 0) stalledBefore++;
                if (b.back > 0 || b35.back > 0) backBefore++;
                bool ok = true;
                foreach (var x in new[] { a, a35 }) ok &= x.minShare >= 0.95 && x.maxShare <= 1.05 && x.back == 0 && x.stall == 0;
                ok &= af.offset <= 0.5;
                ok &= a.perp >= 0.9 * b.perp - 1.0 && a35.perp >= 0.9 * b35.perp - 1.0;
                after &= ok;
                lines.AppendLine($"     {name,-24} 70 m BEFORE pace {b.minShare * 100,4:F0} .. {b.maxShare * 100,4:F0} % ({b.stall,2} frames under 50 %, {b.back,2} backwards) sideways {b.perp,4:F0} px | AFTER {a.minShare * 100,5:F1} .. {a.maxShare * 100,5:F1} % sideways {a.perp,4:F0} px, lead up to {a.lead:F1} m | 35 m BEFORE {b35.minShare * 100,4:F0} % AFTER {a35.minShare * 100,5:F1} .. {a35.maxShare * 100,5:F1} % sideways {b35.perp,4:F0} > {a35.perp,4:F0} px | following camera: along its line BEFORE up to {bf.offset,4:F0} px off the level ship, AFTER {af.offset:F2} px{(ok ? "" : "  <- FAIL")}");
            }
            Sb.Append(lines);
            V(stalledBefore >= 4 && backBefore >= 4, $"F4 BEFORE (the round-2 pose as in the exe): in {stalledBefore} of 8 headings the hull's on-screen pace along its way drops under half its cruise pace, in {backBefore} it runs backwards for a moment (the flip reads as in place)");
            V(after, "F4 AFTER: in all 8 headings, under the still 70 m and 35 m camera the hull keeps 95 .. 105 % of its cruise pace along its screen line through the whole flip, never backwards, the spiral's sideways swing kept; under a camera following the ship it stays level with the level ship along its line in every frame's picture (within 0.5 px)");
        }

        // ------------------------------------------------------------------ F5 the way left with a route
        sealed class RouteRun { public int starts, ends, slowFlips; public double minShare = 9, startAt = -1; public string log = ""; }

        static RouteRun FlyRoute(string settings, bool newRule)
        {
            var r = new RouteRun();
            using (OzV11Kit.GameSwitches())
            {
                ZWorld.ShuttlePlayableDeviation = true; ZWorld.ShuttleOn = true; ZWorld.ShuttleQueueDeviation = true;
                var w = ZWorld.Create(settings, OzV11Kit.Plain(128, 128), 423); w.Events.Clear();
                OzV11Kit.Dummy(w, 6 * 16, 120 * 16);
                var o = w.CreatePlayableShuttle(20 * 16, 64 * 16, Team.Red, 0f);
                var st = new OzKickflipState(o.Id); double now = 0, acc = 0; bool sent = false; var view = new double[6];
                int viaX = 31 * 16 + 8, viaY = 64 * 16 + 8, gX = 31 * 16 + 8, gY = 112 * 16 + 8;                                   // 22 m east, then a right angle, 96 m south
                while (now < 60.0)
                {
                    now += Dt60; acc += Dt60;
                    while (acc >= ZConst.TICK - 1e-12) { acc -= ZConst.TICK; w.Tick(ZConst.TICK); }
                    w.Events.Clear();
                    if (!sent && now >= 1.0)
                    {
                        sent = true;
                        w.SendWaypoints(o.Owner, o, new[] { ZWaypoint.Move(viaX, viaY, true), ZWaypoint.Move(gX, gY, true) });
                        double d = Math.Sqrt(Math.Pow(o.ShuttleGoalX - o.ShuttleBaseX, 2) + Math.Pow(o.ShuttleGoalY - o.ShuttleBaseY, 2)) / 8.0;
                        st.Click(true, o.ShuttleGoalX == gX && o.ShuttleGoalY == gY || o.ShuttleViaCount > 0, d, o.ShuttleGoalX, o.ShuttleGoalY);
                    }
                    if (!sent) continue;
                    var i = TwinIn(o, Dt60, view, newRule);
                    int s0 = st.Starts, e0 = st.Ends; bool flipping = st.Phase == OzKickflipPhase.Flip && st.T <= OzKickflipCurve.TotalSec;
                    if (flipping) r.minShare = Math.Min(r.minShare, i.SpeedMps / Math.Max(1e-9, i.VmaxMps));
                    st.Step(i, Dt60, out var k);
                    if (st.Starts > s0) { r.starts++; if (r.startAt < 0) r.startAt = now; r.log += $"start {now:F2} s ({i.RemainM:F1} m to {(o.ShuttleViaCount > 0 ? "the middle point" : "the goal")} by the rule, middle points left {o.ShuttleViaCount}); "; }
                    if (st.Ends > e0) r.ends++;
                }
                if (r.minShare < 0.95) r.slowFlips = 1;
            }
            return r;
        }

        /// <summary>The flip's inputs as the view reads them (bank / strafe lean / nose eased as View/OzPlayableShuttle.cs does), with the way
        /// left by the old rule (to the route's end) or the K423 rule (to its next point).</summary>
        static OzKickflipIn TwinIn(ZObject o, double dt, double[] v, bool newRule)
        {
            double vmax = Math.Max(1.0, o.RealMoveSpeed), acc = vmax / ZWorld.ShuttleAccelSec, lift = o.ShuttleLift, up2 = Math.Min(1.0, lift * 2);
            double spd = o.ShuttleSpeedPx, flyK = S01((spd - 5.0) / (0.45 * vmax - 5.0));
            double turnWant = Cl(-o.ShuttleTurnRateDeg * 0.28 * up2 * flyK, -20, 20);
            double sv = o.ShuttleStrafeVelPx / 8.0, sa = o.ShuttleStrafeAccelPx / 8.0;
            double leanWant = -Cl(1.3 * sv + 0.2 * sa, -10, 10) * up2;
            v[2] = Mt(v[2], leanWant, 22.0 * dt);
            v[0] = Sd(v[0], Cl(turnWant + v[2], -20, 20), ref v[1], 0.3, 26.0, dt);
            double pitchWant = 7.0 * Cl(o.ShuttleAccelPx / acc, -1, 1) * up2; if (o.ShuttleState == ZShuttleState.Descend) pitchWant = -2.0 * up2;
            v[3] = Sd(v[3], pitchWant, ref v[4], 0.4, double.PositiveInfinity, dt);
            double gx = o.ShuttleGoalX - o.ShuttleBaseX, gy = o.ShuttleGoalY - o.ShuttleBaseY, toGoal = Math.Sqrt(gx * gx + gy * gy) / 8.0;
            double nx = o.ShuttleSteerX - o.ShuttleBaseX, ny = o.ShuttleSteerY - o.ShuttleBaseY, toNext = Math.Sqrt(nx * nx + ny * ny) / 8.0;
            return new OzKickflipIn
            {
                Alive = !o.Destroyed && o.KillAt < 0, State = (int)o.ShuttleState, AtHeight = lift >= 0.999, Braking = o.ShuttleBraking, HoverTurn = o.ShuttleDeckTurning || o.ShuttleLandTurning,
                Strafing = o.ShuttleStrafeStage != 0, UnderFire = o.ShuttleUnderHeavyFire, OnScreen = true,
                SpeedMps = spd / 8.0, VmaxMps = vmax / 8.0, AccelMps2 = o.ShuttleAccelPx / 8.0, AccMaxMps2 = ZWorld.ShuttleAccelMax(vmax) / 8.0, TurnRateDeg = o.ShuttleTurnRateDeg,
                RemainM = newRule ? OzKickflipForward.WayLeftM(toGoal, o.ShuttleViaCount > 0, toNext) : toGoal, BrakeM = ZWorld.ShuttleBrakeDistance(spd, o.ShuttleAccelPx, vmax) / 8.0,
                FeetM = lift * ZWorld.ShuttleCruiseM, BankDeg = v[0], LeanDeg = v[2], PitchDeg = v[3], Health = o.Health, GoalX = o.ShuttleGoalX, GoalY = o.ShuttleGoalY,
            };
        }

        static void Route()
        {
            string settings = Settings;
            if (settings == null) { V(false, "F5 the route: no game settings text (the check needs default_settings.txt)"); return; }
            var b = FlyRoute(settings, false); var a = FlyRoute(settings, true);
            Sb.AppendLine($"     route 22 m east, a right angle, 96 m south: BEFORE {b.starts} flip(s), the slowest moment of a flip {b.minShare * 100:F0} % of the top speed; {b.log}");
            Sb.AppendLine($"     AFTER {a.starts} flip(s), the slowest moment of a flip {a.minShare * 100:F0} % of the top speed; {a.log}");
            V(b.slowFlips > 0, $"F5 BEFORE (the way left to the route's end): the flip ran into the slow-down for the turn at the middle point (it flew down to {b.minShare * 100:F0} % of its top speed during the flip)");
            V(a.starts >= 1 && a.ends == a.starts && a.slowFlips == 0, $"F5 AFTER (the way left to the next point): {a.starts} flip(s), all rolled to the end at {a.minShare * 100:F0} % of the top speed or more (want >= 95 %), the first {(a.startAt >= 0 ? a.startAt.ToString("F1") + " s" : "-")} into the run");
        }

        static double Cl(double x, double a, double b) => x < a ? a : x > b ? b : x;
        static double S01(double x) { x = Cl(x, 0, 1); return x * x * (3 - 2 * x); }
        static double Mt(double c, double t, double d) => Math.Abs(t - c) <= d ? t : c + Math.Sign(t - c) * d;
        static double Sd(double current, double target, ref double vel, double smoothTime, double maxSpeed, double dt)
        {
            if (dt <= 0) return current;
            smoothTime = Math.Max(0.0001, smoothTime);
            double omega = 2.0 / smoothTime, x = omega * dt, exp = 1.0 / (1.0 + x + 0.48 * x * x + 0.235 * x * x * x);
            double change = current - target, orig = target, maxChange = maxSpeed * smoothTime;
            change = Cl(change, -maxChange, maxChange); target = current - change;
            double temp = (vel + omega * change) * dt;
            vel = (vel - omega * temp) * exp;
            double output = target + (change + temp) * exp;
            if (orig - current > 0.0 == output > orig) { output = orig; vel = (output - orig) / dt; }
            return output;
        }
    }
}
