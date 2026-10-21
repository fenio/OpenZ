using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// K410, the shuttle flies the whole Shift queue (rule DV125 on top of DV118, Sim/ZWorld.ShuttleQueue.cs), counted headless (ZWorld.Create +
    /// Tick, no scene, nothing saved; public members only, like every check of the editor assembly). Menu OpenZ/V1.1/Shuttle: K410 Route Check.
    /// On an open plain (no CPU: both teams take orders, as two players would): routes of 2, 3 and 5 points, flown by one ship alone (OWN) and
    /// by three ships whose routes cross in the middle (TRAFFIC: all red, and red against blue), every case with <see cref="Seeds"/> seeds
    /// (the start, the turns at the points and the leg lengths from the seed). Counted every tick: each middle point's closest approach of the
    /// ship's point on its line and its lift and state there; any landing, descent, hover or landing turn before the last point; the
    /// landing at the last point (OWN: on the clicked point; TRAFFIC: on the spot the traffic gave it); the slowest speed between the first and
    /// the last middle point (OWN: never a stop); the closest two ships in the air and the landing spots (TRAFFIC: K338 kept).
    /// Then: a Shift point added while it flies (the view sends the route still to fly + the new point), a new plain order that replaces a route,
    /// EXIT given in the air during a route, a last point where it cannot land (it hovers there), the switch off (the first point only), a CPU
    /// team's route (refused), the same case twice and replayed (identical), and level 16's own ship on a route over water (the landing rule
    /// only for the last point, the order filter of K256g only for the last point).
    /// </summary>
    public static class OzShuttleQueueCount
    {
        const double Tick = ZConst.TICK;
        /// <summary>Seeds per case; seeds when the Deviations row DV125 runs the check; the most seconds a case runs.</summary>
        public const int Seeds = 6, RowSeeds = 2;
        public const double MaxSec = 240.0;
        /// <summary>Limits: a middle point counts as flown through within this (m); never a stop: the slowest speed between the first and the
        /// last middle point at least this share of the top speed (one ship alone).</summary>
        public const double PassNearM = 3.5, NoStopK = 0.15;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static string F(double v, string f = "0.00") => v.ToString(f, Inv);

        public sealed class Run
        {
            public ZWorld W;
            public string Kind; public int N, Seed; public bool Enemy;
            public readonly List<ZObject> Ships = new List<ZObject>();
            public readonly List<List<(int x, int y)>> Routes = new List<List<(int, int)>>();
            public readonly List<double> OrderAt = new List<double>();
            public readonly List<bool> Ordered = new List<bool>();
        }

        static ZWorld NewWorld(string settings, string map, int seed)
        {
            var w = ZWorld.Create(settings, map, seed);
            w.Events.Clear();
            OzV11Kit.Dummy(w, 12 * 16, 12 * 16);
            return w;
        }

        static int Hash(string s) { int h = 7; foreach (char c in s) h = h * 31 + c; return h & 0x7fff; }

        /// <summary>A route of n points from (sx, sy) px: the first leg toward (fx, fy) when given (else the start heading), then a turn of
        /// 20..150 degrees left or right at each point and legs of 22..40 m, kept inside the square of half side `half` px round (cx, cy).</summary>
        static List<(int x, int y)> MakeRoute(ZWorld w, Random rng, double sx, double sy, double hdDeg, int n, int cx, int cy, double half, (double x, double y)? first = null)
        {
            var pts = new List<(int x, int y)>();
            double x = sx, y = sy, h = hdDeg * Math.PI / 180.0;
            for (int i = 0; i < n; i++)
            {
                double nx = x, ny = y;
                for (int tries = 0; tries < 24; tries++)
                {
                    double hh = h;
                    if (i == 0 && first.HasValue) { nx = first.Value.x + (rng.NextDouble() * 2 - 1) * 8 * 8; ny = first.Value.y + (rng.NextDouble() * 2 - 1) * 8 * 8; break; }
                    if (i > 0 || tries > 0) hh = h + (rng.Next(2) == 0 ? -1 : 1) * (20 + rng.NextDouble() * 130) * Math.PI / 180.0;
                    double len = (22 + rng.NextDouble() * 18) * 8;
                    nx = x + Math.Cos(hh) * len; ny = y + Math.Sin(hh) * len;
                    if (Math.Abs(nx - cx) <= half && Math.Abs(ny - cy) <= half) { h = hh; break; }
                }
                var c = w.ShuttleClamp((int)Math.Round(nx), (int)Math.Round(ny));
                pts.Add(c); h = Math.Atan2(c.y - y, c.x - x); x = c.x; y = c.y;
            }
            return pts;
        }

        /// <summary>Case `kind` (own | traffic) with an n-point route each, from the seed.</summary>
        public static Run Build(string settings, string map, string kind, int n, bool enemy, int seed)
        {
            var r = new Run { Kind = kind, N = n, Seed = seed, Enemy = enemy };
            r.W = NewWorld(settings, map, seed);
            var rng = new Random(seed * 7919 + n * 131 + Hash(kind) * 17 + (enemy ? 5003 : 0));
            int C = 64 * 16;
            int ships = kind == "traffic" ? 3 : 1;
            double a0 = rng.NextDouble() * 2 * Math.PI;
            for (int i = 0; i < ships; i++)
            {
                double a = a0 + 2 * Math.PI * i / ships, rad = (ships == 1 ? 30 : 45) * 8;
                var (sx, sy) = r.W.ShuttleClamp((int)Math.Round(C + Math.Cos(a) * rad), (int)Math.Round(C + Math.Sin(a) * rad));
                double hd = (a + Math.PI) * 180.0 / Math.PI;
                var team = enemy && i % 2 == 1 ? Team.Blue : Team.Red;
                var s = r.W.CreatePlayableShuttle(sx, sy, team, (float)Norm(hd));
                var route = MakeRoute(r.W, rng, sx, sy, hd, n, C, C, 55 * 8, ships > 1 ? (C, C) : ((double, double)?)null);
                r.Ships.Add(s); r.Routes.Add(route); r.OrderAt.Add(0.5 + rng.NextDouble() * (ships > 1 ? 1.5 : 0.2)); r.Ordered.Add(false);
            }
            return r;
        }

        static double Norm(double a) { a %= 360.0; if (a > 180) a -= 360; else if (a <= -180) a += 360; return a; }

        public static List<ZWaypoint> Wps(IEnumerable<(int x, int y)> pts) => pts.Select(p => new ZWaypoint { mode = WpMode.Move, x = p.x, y = p.y, playerGiven = true }).ToList();

        /// <summary>The orders due by now (each ship's route, at its moment).</summary>
        public static void Orders(Run r)
        {
            for (int i = 0; i < r.Ships.Count; i++)
                if (!r.Ordered[i] && r.W.Time >= r.OrderAt[i]) { r.Ordered[i] = true; r.W.SendWaypoints(r.Ships[i].Owner, r.Ships[i], Wps(r.Routes[i])); }
        }

        static double Mx(ZObject s) => s.LocX + s.XOver + s.WidthPix * 0.5;
        static double My(ZObject s) => s.LocY + s.YOver + s.HeightPix * 0.5;

        /// <summary>One tick's state of every ship as a number (positions, headings, states, lifts, the middle points left).</summary>
        public static long StateHash(long h, IEnumerable<ZObject> ships)
        {
            unchecked
            {
                foreach (var s in ships)
                {
                    h = h * 31 + s.LocX; h = h * 31 + s.LocY; h = h * 31 + (long)Math.Round(s.XOver * 1e6); h = h * 31 + (long)Math.Round(s.YOver * 1e6);
                    h = h * 31 + (long)Math.Round(s.ShuttleHeadingDeg * 1e4); h = h * 31 + (int)s.ShuttleState; h = h * 31 + (long)Math.Round(s.ShuttleLift * 1e6);
                    h = h * 31 + s.ShuttleViaCount; h = h * 31 + s.ShuttleGoalX; h = h * 31 + s.ShuttleGoalY; h = h * 31 + s.Health;
                }
            }
            return h;
        }

        /// <summary>What one flown case gave.</summary>
        public sealed class Tally
        {
            public int Routes, Middles, MiddlesNear, MiddlesLowLift, MiddlesNotCruise, EarlyLandings, Landed, LandedOnPoint, LandedOff, Unfinished;
            public double PassWorstM, PassMeanM, SlowestK = 9, SlowestPassK = 9, Closest = 1e9, SpotClosest = 1e9, MaxTurnRate;
            public int Ticks; public long Hash = 17;
            public readonly List<string> Bad = new List<string>();
        }

        /// <summary>Flies one case to its end and counts it.</summary>
        public static Tally Fly(Run r, double maxSec = MaxSec)
        {
            var t = new Tally();
            int n = r.Ships.Count;
            var near = new double[n][]; var liftAt = new double[n][]; var stateAt = new ZShuttleState[n][];
            var tookOff = new bool[n]; var landings0 = new int[n]; var minSpeedK = new double[n];
            for (int i = 0; i < n; i++)
            {
                int m = r.Routes[i].Count - 1; near[i] = Enumerable.Repeat(1e9, m).ToArray(); liftAt[i] = new double[m]; stateAt[i] = new ZShuttleState[m];
                landings0[i] = r.Ships[i].ShuttleLandings; minSpeedK[i] = 9;
            }
            double sum = 0; int cnt = 0;
            var prevHd = r.Ships.Select(s => (double)s.ShuttleHeadingDeg).ToArray();
            while (r.W.Time < maxSec)
            {
                Orders(r); r.W.Tick(Tick); r.W.Events.Clear(); t.Ticks++;
                t.Hash = StateHash(t.Hash, r.Ships);
                bool allDone = true;
                for (int i = 0; i < n; i++)
                {
                    var s = r.Ships[i];
                    if (s.Destroyed) continue;
                    if (s.ShuttleAirborne) tookOff[i] = true;
                    double rate = Math.Abs(ShDelta(prevHd[i], s.ShuttleHeadingDeg)) / Tick; prevHd[i] = s.ShuttleHeadingDeg;
                    if (s.ShuttleAirborne && s.ShuttleState == ZShuttleState.Cruise) t.MaxTurnRate = Math.Max(t.MaxTurnRate, rate);
                    var route = r.Routes[i]; int m = route.Count - 1;
                    int passed = m - s.ShuttleViaCount;                                                       // middle points behind it (once ordered)
                    if (r.Ordered[i] && s.ShuttleRoutes > 0)
                        for (int k = 0; k < m; k++)
                        {
                            double dx = s.ShuttleBaseX - route[k].x, dy = s.ShuttleBaseY - route[k].y, d = Math.Sqrt(dx * dx + dy * dy) / 8.0;
                            if (d < near[i][k]) { near[i][k] = d; liftAt[i][k] = s.ShuttleLift; stateAt[i][k] = s.ShuttleState; }
                        }
                    if (r.Ordered[i] && tookOff[i] && s.ShuttleViaCount > 0 && (s.ShuttleState == ZShuttleState.Descend || s.ShuttleState == ZShuttleState.Hover || s.ShuttleLandTurning
                        || (s.ShuttleState == ZShuttleState.Landed && s.ShuttleLift <= 0) || s.ShuttleLandings > landings0[i]))
                    {
                        t.EarlyLandings++; t.Bad.Add($"{r.Kind} {r.N} seed {r.Seed} #{s.Id}: {s.ShuttleState} with {s.ShuttleViaCount} middle point(s) left at {F(r.W.Time)} s");
                        landings0[i] = s.ShuttleLandings;
                    }
                    if (s.ShuttleViasPassed >= 1 && s.ShuttleViaCount > 0 && s.ShuttleState == ZShuttleState.Cruise) minSpeedK[i] = Math.Min(minSpeedK[i], s.ShuttleSpeedPx / Math.Max(1.0, s.RealMoveSpeed));
                    bool done = r.Ordered[i] && tookOff[i] && s.ShuttleViaCount == 0 && ((s.ShuttleState == ZShuttleState.Landed && s.ShuttleLift <= 0) || s.ShuttleState == ZShuttleState.Hover);
                    if (!done) allDone = false;
                }
                for (int i = 0; i < n; i++)
                    for (int j = i + 1; j < n; j++)
                    {
                        var p = r.Ships[i]; var q = r.Ships[j];
                        if (!p.ShuttleAirborne || !q.ShuttleAirborne) continue;
                        double dx = Mx(p) - Mx(q), dy = My(p) - My(q);
                        t.Closest = Math.Min(t.Closest, Math.Sqrt(dx * dx + dy * dy) / 8.0);
                    }
                if (allDone) break;
            }
            for (int i = 0; i < n; i++)
            {
                var s = r.Ships[i]; var route = r.Routes[i]; int m = route.Count - 1;
                t.Routes++;
                for (int k = 0; k < m; k++)
                {
                    t.Middles++; double d = near[i][k];
                    if (d <= PassNearM) t.MiddlesNear++; else t.Bad.Add($"{r.Kind} {r.N} seed {r.Seed} #{s.Id}: middle point {k + 1} passed {F(d)} m away");
                    if (liftAt[i][k] < 0.999) { t.MiddlesLowLift++; t.Bad.Add($"{r.Kind} {r.N} seed {r.Seed} #{s.Id}: lift {F(liftAt[i][k])} at middle point {k + 1}"); }
                    if (stateAt[i][k] != ZShuttleState.Cruise) t.MiddlesNotCruise++;
                    t.PassWorstM = Math.Max(t.PassWorstM, d); sum += d; cnt++;
                }
                foreach (var pz in r.W.ShuttlePasses) if (pz.id == s.Id) t.SlowestPassK = Math.Min(t.SlowestPassK, pz.speedPx / Math.Max(1.0, s.RealMoveSpeed));
                if (m > 1 || r.Ships.Count == 1) t.SlowestK = Math.Min(t.SlowestK, minSpeedK[i]);
                bool landed = s.ShuttleState == ZShuttleState.Landed && s.ShuttleLift <= 0 && s.ShuttleLandings == landings0[i] + 1 && s.ShuttleViaCount == 0;
                if (!landed) { t.Unfinished++; t.Bad.Add($"{r.Kind} {r.N} seed {r.Seed} #{s.Id}: not landed at its last point ({s.ShuttleState}, landings {s.ShuttleLandings}, {s.ShuttleViaCount} left, {F(r.W.Time)} s)"); continue; }
                t.Landed++;
                var last = route[m];
                bool onGoal = Math.Abs(s.ShuttleBaseX - s.ShuttleGoalX) <= 2 && Math.Abs(s.ShuttleBaseY - s.ShuttleGoalY) <= 2;
                bool onPoint = onGoal && s.ShuttleGoalX == last.x && s.ShuttleGoalY == last.y;
                double off = Math.Sqrt(Math.Pow(s.ShuttleGoalX - last.x, 2) + Math.Pow(s.ShuttleGoalY - last.y, 2)) / 8.0;
                if (onPoint) t.LandedOnPoint++;
                else if (onGoal && r.Ships.Count > 1 && off <= 45.0) t.LandedOff++;                            // the traffic gave it a free spot round its point (K338)
                else { t.Unfinished++; t.Landed--; t.Bad.Add($"{r.Kind} {r.N} seed {r.Seed} #{s.Id}: landed {F(off)} m from its last point"); }
                for (int j = i + 1; j < n; j++)
                {
                    var q = r.Ships[j];
                    if (q.ShuttleState != ZShuttleState.Landed) continue;
                    t.SpotClosest = Math.Min(t.SpotClosest, Math.Sqrt(Math.Pow(s.ShuttleBaseX - q.ShuttleBaseX, 2) + Math.Pow(s.ShuttleBaseY - q.ShuttleBaseY, 2)) / 8.0);
                }
            }
            t.PassMeanM = cnt > 0 ? sum / cnt : 0;
            return t;
        }

        static double ShDelta(double a, double b) { double d = (b - a) % 360.0; if (d > 180) d -= 360; else if (d <= -180) d += 360; return d; }

        /// <summary>The whole check: the PASS / FAIL lines (and the numbers behind them).</summary>
        public static string Check(string settings, string l16Map, int seeds = Seeds)
        {
            var sb = new StringBuilder();
            bool was = ZWorld.ShuttlePlayableDeviation, wasOn = ZWorld.ShuttleOn, wasQ = ZWorld.ShuttleQueueDeviation;
            try
            {
                ZWorld.ShuttlePlayableDeviation = true; ZWorld.ShuttleOn = true; ZWorld.ShuttleQueueDeviation = true;
                string plain = OzV11Kit.Plain(128, 128);
                sb.AppendLine($"K410 route check (DV125): routes of 2, 3, 5 points, one ship alone and three ships crossing (own, own + enemy), {seeds} seed(s) a case");
                // ---------------------------------------------------------------- 1-5 the routes
                Tally own = new Tally(), traf = new Tally(); int ownN = 0, trafN = 0;
                var perCase = new List<string>();
                foreach (var kind in new[] { "own", "traffic" })
                    foreach (int n in new[] { 2, 3, 5 })
                        foreach (bool en in kind == "own" ? new[] { false } : new[] { false, true })
                            for (int seed = 1; seed <= seeds; seed++)
                            {
                                var r = Build(settings, plain, kind, n, en, seed);
                                var t = Fly(r);
                                Add(kind == "own" ? own : traf, t); if (kind == "own") ownN++; else trafN++;
                                if (seed == 1) perCase.Add($"  {kind} {n} {(en ? "own+enemy" : "own")} seed 1: {t.Middles} middle points, worst pass {F(t.PassWorstM)} m, landed {t.Landed} of {t.Routes} ({t.LandedOnPoint} on the point, {t.LandedOff} on a free spot), {F(r.W.Time, "0.0")} s");
                            }
                foreach (var l in perCase) sb.AppendLine(l);
                var all = new Tally(); Add(all, own); Add(all, traf);
                sb.AppendLine(OzV11Kit.Verdict(all.Middles > 0 && all.MiddlesNear == all.Middles && all.MiddlesLowLift == 0 && all.MiddlesNotCruise == 0,
                    $"1. every middle point flown through in the air: {all.MiddlesNear} of {all.Middles} within {F(PassNearM, "0.0")} m (worst {F(all.PassWorstM)} m, mean {F(all.PassMeanM)} m), at the cruise height at each ({all.MiddlesLowLift} lower), cruising at each ({all.MiddlesNotCruise} not)"));
                sb.AppendLine(OzV11Kit.Verdict(all.EarlyLandings == 0, $"2. no landing, descent, landing turn or hover before the last point: {all.EarlyLandings}"));
                sb.AppendLine(OzV11Kit.Verdict(all.Unfinished == 0 && own.LandedOnPoint == own.Routes,
                    $"3. every route ends with one landing at its last point: {all.Landed} of {all.Routes} (alone: {own.LandedOnPoint} of {own.Routes} on the clicked point; with traffic: {traf.LandedOnPoint} on the point, {traf.LandedOff} on the free spot the traffic gave it)"));
                sb.AppendLine(OzV11Kit.Verdict(own.SlowestK >= NoStopK && own.SlowestPassK >= 0.8 * ZWorld.ShuttlePassFloorK && own.MaxTurnRate <= ZWorld.ShuttleTurnMaxDeg + 0.5,
                    $"4. never a stop on the way (one ship alone): the slowest between its first and last middle point {F(own.SlowestK * 100, "0")} % of its top speed (>= {F(NoStopK * 100, "0")} %), the slowest pass {F(own.SlowestPassK * 100, "0")} %, its turns at most {F(own.MaxTurnRate, "0.0")} deg/s (<= {F(ZWorld.ShuttleTurnMaxDeg, "0")}); with traffic the slowest {F(traf.SlowestK * 100, "0")} % (giving way in the air may hold it)"));
                sb.AppendLine(OzV11Kit.Verdict(traf.Closest >= ZWorld.ShuttleAirSepM - 1e-6 && traf.SpotClosest >= ZWorld.ShuttleClaimM - 1e-6,
                    $"5. the air traffic kept on crossing routes: the closest two ships in the air {F(traf.Closest)} m (>= {F(ZWorld.ShuttleAirSepM, "0.0")}), the closest two landed {F(traf.SpotClosest)} m (>= {F(ZWorld.ShuttleClaimM, "0.0")})"));
                foreach (var b in all.Bad.Take(12)) sb.AppendLine("     " + b);
                // ---------------------------------------------------------------- 6 a Shift point added while it flies
                sb.AppendLine(Append(settings, plain));
                // ---------------------------------------------------------------- 7 a plain order replaces the route
                sb.AppendLine(Replace(settings, plain));
                // ---------------------------------------------------------------- 8 EXIT in the air during a route
                sb.AppendLine(AirExit(settings, plain));
                // ---------------------------------------------------------------- 9 the last point taken: it hovers there
                sb.AppendLine(NoLand(settings, plain));
                // ---------------------------------------------------------------- 10 the switch off
                sb.AppendLine(SwitchOff(settings, plain));
                // ---------------------------------------------------------------- 11 a CPU team's route
                sb.AppendLine(Cpu(settings, plain));
                // ---------------------------------------------------------------- 12 the same case twice, and replayed
                sb.AppendLine(Same(settings, plain));
                // ---------------------------------------------------------------- 13 level 16's own ship over water
                sb.AppendLine(L16(settings, l16Map));
            }
            finally { ZWorld.ShuttlePlayableDeviation = was; ZWorld.ShuttleOn = wasOn; ZWorld.ShuttleQueueDeviation = wasQ; }
            return sb.ToString();
        }

        static void Add(Tally a, Tally b)
        {
            a.Routes += b.Routes; a.Middles += b.Middles; a.MiddlesNear += b.MiddlesNear; a.MiddlesLowLift += b.MiddlesLowLift; a.MiddlesNotCruise += b.MiddlesNotCruise;
            a.EarlyLandings += b.EarlyLandings; a.Landed += b.Landed; a.LandedOnPoint += b.LandedOnPoint; a.LandedOff += b.LandedOff; a.Unfinished += b.Unfinished;
            if (b.Middles > 0) { a.PassMeanM = (a.PassMeanM * (a.Middles - b.Middles) + b.PassMeanM * b.Middles) / Math.Max(1, a.Middles); }
            a.PassWorstM = Math.Max(a.PassWorstM, b.PassWorstM); a.SlowestK = Math.Min(a.SlowestK, b.SlowestK); a.SlowestPassK = Math.Min(a.SlowestPassK, b.SlowestPassK);
            a.Closest = Math.Min(a.Closest, b.Closest); a.SpotClosest = Math.Min(a.SpotClosest, b.SpotClosest); a.MaxTurnRate = Math.Max(a.MaxTurnRate, b.MaxTurnRate);
            a.Bad.AddRange(b.Bad);
        }

        static Run One(string settings, string plain, int seed, out ZObject s)
        {
            var r = new Run { Kind = "one", N = 1, Seed = seed };
            r.W = NewWorld(settings, plain, seed);
            int C = 64 * 16;
            var (sx, sy) = r.W.ShuttleClamp(C - 40 * 8, C);
            s = r.W.CreatePlayableShuttle(sx, sy, Team.Red, 0f);
            r.Ships.Add(s);
            return r;
        }

        static (int x, int y) P(ZWorld w, double mx, double my) => w.ShuttleClamp(64 * 16 + (int)Math.Round(mx * 8), 64 * 16 + (int)Math.Round(my * 8));

        static void Until(ZWorld w, Func<bool> done, double maxSec) { double end = w.Time + maxSec; while (w.Time < end && !done()) { w.Tick(Tick); w.Events.Clear(); } }

        static bool LandedAt(ZObject s, (int x, int y) p) => s.ShuttleState == ZShuttleState.Landed && s.ShuttleLift <= 0 && s.ShuttleGoalX == p.x && s.ShuttleGoalY == p.y
                                                          && Math.Abs(s.ShuttleBaseX - p.x) <= 2 && Math.Abs(s.ShuttleBaseY - p.y) <= 2;

        static string Append(string settings, string plain)
        {
            var r = One(settings, plain, 3, out var s); var w = r.W;
            var a = P(w, 0, 0); var b = P(w, 10, 30);
            w.SendWaypoints(Team.Red, s, Wps(new[] { a }));
            Until(w, () => s.ShuttleState == ZShuttleState.Cruise && s.ShuttleBaseX > a.x - 20 * 8, 60);
            bool flying = s.ShuttleState == ZShuttleState.Cruise; int l0 = s.ShuttleLandings;
            // what the view's Shift click sends (View/OzShuttleQueueInput.cs): the route still to fly (here its goal A) + the new point B
            w.SendWaypoints(Team.Red, s, Wps(new[] { (s.ShuttleGoalX, s.ShuttleGoalY), b }));
            bool took = s.ShuttleViaCount == 1 && s.ShuttleGoalX == b.x && s.ShuttleGoalY == b.y;
            double nearA = 1e9; int early = 0;
            Until(w, () =>
            {
                nearA = Math.Min(nearA, Math.Sqrt(Math.Pow(s.ShuttleBaseX - a.x, 2) + Math.Pow(s.ShuttleBaseY - a.y, 2)) / 8.0);
                if (s.ShuttleViaCount > 0 && (s.ShuttleState == ZShuttleState.Descend || s.ShuttleLandings > l0)) early++;
                return s.ShuttleState == ZShuttleState.Landed && s.ShuttleLift <= 0;
            }, 90);
            bool ok = flying && took && nearA <= PassNearM && early == 0 && s.ShuttleLandings == l0 + 1 && LandedAt(s, b);
            return OzV11Kit.Verdict(ok, $"6. a Shift point added while it flies to A (the route still to fly + B): taken ({(took ? "A a middle point, B the goal" : "NOT")}), A flown through {F(nearA)} m away in the air, early landings {early}, landed once at B ({(LandedAt(s, b) ? "yes" : "NO")})");
        }

        static string Replace(string settings, string plain)
        {
            var r = One(settings, plain, 4, out var s); var w = r.W;
            var route = new[] { P(w, 0, 0), P(w, 20, 25), P(w, 40, 0) }; var d = P(w, -10, -30);
            w.SendWaypoints(Team.Red, s, Wps(route));
            Until(w, () => s.ShuttleState == ZShuttleState.Cruise && s.ShuttleSpeedPx > 0.5 * s.RealMoveSpeed, 60);
            int l0 = s.ShuttleLandings, p0 = s.ShuttleViasPassed;
            w.SendWaypoints(Team.Red, s, Wps(new[] { d }));
            bool cleared = s.ShuttleViaCount == 0 && s.ShuttleGoalX == d.x && s.ShuttleGoalY == d.y;
            Until(w, () => s.ShuttleState == ZShuttleState.Landed && s.ShuttleLift <= 0, 90);
            bool ok = cleared && s.ShuttleViasPassed == p0 && s.ShuttleLandings == l0 + 1 && LandedAt(s, d);
            return OzV11Kit.Verdict(ok, $"7. a new plain order replaces the route at once: middle points left {(cleared ? 0 : s.ShuttleViaCount)}, passed after it {s.ShuttleViasPassed - p0}, landed once at the new point ({(LandedAt(s, d) ? "yes" : "NO")})");
        }

        static string AirExit(string settings, string plain)
        {
            var r = One(settings, plain, 5, out var s); var w = r.W;
            for (int i = 0; i < 4; i++) s.Passengers.Add(100);
            s.PassengerType = "grunt";
            var route = new[] { P(w, -10, -20), P(w, 15, -5), P(w, 30, 20) };
            w.SendWaypoints(Team.Red, s, Wps(route));
            Until(w, () => s.ShuttleState == ZShuttleState.Cruise && s.ShuttleSpeedPx > 0.5 * s.RealMoveSpeed && s.ShuttleViaCount == 2, 60);
            int left = s.ShuttleViaCount; var goal = (s.ShuttleGoalX, s.ShuttleGoalY);
            bool given = w.Eject(Team.Red, s);
            bool kept = s.ShuttleViaCount == left && (s.ShuttleGoalX, s.ShuttleGoalY) == goal && s.ShuttleExitOnLanding;
            int outEarly = 0, l0 = s.ShuttleLandings;
            Until(w, () => { if (s.ShuttleAirborne && s.Passengers.Count < 4) outEarly++; return s.ShuttleState == ZShuttleState.Landed && s.ShuttleLift <= 0; }, 90);
            bool atLast = LandedAt(s, route[2]);
            Until(w, () => s.Passengers.Count == 0, 20);
            bool ok = given && kept && outEarly == 0 && atLast && s.ShuttleLandings == l0 + 1 && s.Passengers.Count == 0;
            return OzV11Kit.Verdict(ok, $"8. EXIT given in the air during a route: taken ({given}), the route kept ({(kept ? "yes" : "NO")}: {left} middle point(s), the goal unchanged), nobody out in the air ({outEarly} frames), landed at the LAST point ({(atLast ? "yes" : "NO")}), the squad out there (aboard {s.Passengers.Count})");
        }

        static string NoLand(string settings, string plain)
        {
            var r = One(settings, plain, 6, out var s); var w = r.W;
            var route = new[] { P(w, 0, -15), P(w, 25, 5) };
            var block = OzV11Kit.Place(w, "grunt", route[1].x, route[1].y, Team.Blue, 1); block.MoveSpeed = 0; block.Damage = 0; block.MaxHealth = block.Health = 1000000;
            w.SendWaypoints(Team.Red, s, Wps(route));
            int early = 0, l0 = s.ShuttleLandings;
            Until(w, () => { if (s.ShuttleViaCount > 0 && s.ShuttleState == ZShuttleState.Hover) early++; return s.ShuttleState == ZShuttleState.Hover && s.ShuttleViaCount == 0; }, 90);
            bool hover = s.ShuttleState == ZShuttleState.Hover && Math.Abs(s.ShuttleBaseX - route[1].x) <= 2 && Math.Abs(s.ShuttleBaseY - route[1].y) <= 2;
            bool ok = hover && early == 0 && s.ShuttleViasPassed == 1 && s.ShuttleLandings == l0 && s.ShuttleHovers == 1;
            return OzV11Kit.Verdict(ok, $"9. a unit on the last point: it flies through the middle point ({s.ShuttleViasPassed}) and hovers over the LAST point ({(hover ? "yes" : "NO")}, why \"{s.ShuttleNoLand}\"), never over the middle one ({early}), no landing ({s.ShuttleLandings - l0})");
        }

        static string SwitchOff(string settings, string plain)
        {
            ZWorld.ShuttleQueueDeviation = false;
            try
            {
                var r = One(settings, plain, 7, out var s); var w = r.W;
                var route = new[] { P(w, 0, 0), P(w, 20, 25), P(w, 40, 0) };
                w.SendWaypoints(Team.Red, s, Wps(route));
                bool first = s.ShuttleViaCount == 0 && s.ShuttleGoalX == route[0].x && s.ShuttleGoalY == route[0].y;
                Until(w, () => s.ShuttleState == ZShuttleState.Landed && s.ShuttleLift <= 0 && s.ShuttleLandings > 0, 90);
                bool ok = first && LandedAt(s, route[0]) && s.ShuttleViasPassed == 0;
                return OzV11Kit.Verdict(ok, $"10. the switch off (ZWorld.ShuttleQueueDeviation = false): a 3-point list gives the ship its FIRST point only, as before (goal = the first point {(first ? "yes" : "NO")}, landed there {(LandedAt(s, route[0]) ? "yes" : "NO")})");
            }
            finally { ZWorld.ShuttleQueueDeviation = true; }
        }

        static string Cpu(string settings, string plain)
        {
            var r = One(settings, plain, 8, out var s); var w = r.W;
            var b = w.CreatePlayableShuttle(P(w, 30, 30).x, P(w, 30, 30).y, Team.Blue, 0f);
            w.Bots.Add(new ZBot(w, Team.Blue, 99) { Ignored = true });
            int ref0 = w.ShuttleCpuOrdersRefused;
            bool sent = w.SendWaypoints(Team.Blue, b, Wps(new[] { P(w, 0, 0), P(w, -20, 10) }));
            Until(w, () => false, 3);
            bool ok = !sent && w.ShuttleCpuOrdersRefused == ref0 + 1 && b.ShuttleViaCount == 0 && b.ShuttleGoalX < 0 && !b.ShuttleAirborne;
            return OzV11Kit.Verdict(ok, $"11. a CPU team's route is refused as every CPU order to a shuttle (refused {w.ShuttleCpuOrdersRefused - ref0}, middle points {b.ShuttleViaCount}, still on the ground {!b.ShuttleAirborne})");
        }

        static string Same(string settings, string plain)
        {
            long h1 = Fly(Build(settings, plain, "traffic", 3, true, 1)).Hash, h2 = Fly(Build(settings, plain, "traffic", 3, true, 1)).Hash;
            long h3 = Fly(Build(settings, plain, "own", 5, false, 2)).Hash, h4 = Fly(Build(settings, plain, "own", 5, false, 2)).Hash;
            bool ok = h1 == h2 && h3 == h4;
            return OzV11Kit.Verdict(ok, $"12. deterministic: the same case flown twice from scratch gives the same ships tick for tick (3 ships, own + enemy, 3 points: {(h1 == h2 ? "identical" : "DIFFERENT")}; one ship, 5 points: {(h3 == h4 ? "identical" : "DIFFERENT")}; state hashes {h1}, {h3})");
        }

        /// <summary>Level 16's own ship: a 3-point route whose middle point lies over water (a place it can never land) and whose last point is a
        /// landing spot: the order filter (K256g) lets it through, the ship flies over the water and lands at the last point; the same route
        /// ending over the water is refused by the filter.</summary>
        static string L16(string settings, string map)
        {
            var w = ZWorld.Create(settings, map, 3); w.Events.Clear();
            var s = w.PlayableShuttle;
            if (s == null) return OzV11Kit.Verdict(false, "13. level 16: no playable shuttle made");
            int sx = s.CenterX, sy = s.CenterY;
            // a water tile 25..60 m from the ship, and a landing spot 20..50 m beyond it
            (int x, int y) water = (-1, -1), land = (-1, -1);
            for (int r = 13; r <= 30 && water.x < 0; r++)
                for (int k = 0; k < 48 && water.x < 0; k++)
                {
                    double a = k * Math.PI / 24; int tx = (sx >> 4) + (int)Math.Round(Math.Cos(a) * r), ty = (sy >> 4) + (int)Math.Round(Math.Sin(a) * r);
                    if (tx < 3 || ty < 3 || tx >= w.Map.width - 3 || ty >= w.Map.height - 3) continue;
                    var c = w.Map.ClassAt(tx, ty);
                    if ((c == ZClass.Water || c == ZClass.DeepWater) && w.ShuttleNeverLands(tx * 16 + 8, ty * 16 + 8, s, out _)) water = (tx * 16 + 8, ty * 16 + 8);
                }
            if (water.x < 0) return OzV11Kit.Verdict(false, "13. level 16: no water tile found near the ship");
            double ux = water.x - sx, uy = water.y - sy, ul = Math.Sqrt(ux * ux + uy * uy); ux /= ul; uy /= ul;
            for (int r = 10; r <= 40 && land.x < 0; r++)
                for (int k = -6; k <= 6 && land.x < 0; k++)
                {
                    double a = Math.Atan2(uy, ux) + k * 0.2;
                    var c = w.ShuttleClamp(water.x + (int)Math.Round(Math.Cos(a) * r * 16), water.y + (int)Math.Round(Math.Sin(a) * r * 16));
                    if (w.ShuttleLandSpot(c.x, c.y, s, s.ShuttleHeadingDeg, out _, out _)) land = c;
                }
            if (land.x < 0) return OzV11Kit.Verdict(false, "13. level 16: no landing spot found beyond the water");
            var mid0 = w.ShuttleClamp((sx + water.x) / 2 + 12 * 8, (sy + water.y) / 2);
            var route = new List<(int x, int y)> { mid0, water, land };
            // the K256g filter (LANDABLE SPOTS ONLY): the route ending on land passes, the same route ending over the water is refused
            var pass = new Dictionary<ZObject, List<ZWaypoint>> { [s] = Wps(route) };
            int kept = w.ShuttleOrdersFilter(s.Owner, pass, true);
            var refuse = new Dictionary<ZObject, List<ZWaypoint>> { [s] = Wps(new[] { mid0, land, water }) };
            int dropped = w.ShuttleOrdersFilter(s.Owner, refuse, true);
            w.SendWaypoints(s.Owner, s, Wps(route));
            int early = 0, l0 = s.ShuttleLandings; double nearW = 1e9; bool overWaterCruise = false;
            Until(w, () =>
            {
                double d = Math.Sqrt(Math.Pow(s.ShuttleBaseX - water.x, 2) + Math.Pow(s.ShuttleBaseY - water.y, 2)) / 8.0;
                if (d < nearW) { nearW = d; overWaterCruise = s.ShuttleState == ZShuttleState.Cruise && s.ShuttleLift >= 0.999; }
                if (s.ShuttleViaCount > 0 && (s.ShuttleState == ZShuttleState.Descend || s.ShuttleState == ZShuttleState.Hover || s.ShuttleLandings > l0)) early++;
                return s.ShuttleViaCount == 0 && ((s.ShuttleState == ZShuttleState.Landed && s.ShuttleLift <= 0 && s.ShuttleLandings > l0) || s.ShuttleState == ZShuttleState.Hover);
            }, 150);
            bool landed = LandedAt(s, land) || (s.ShuttleState == ZShuttleState.Landed && s.ShuttleLandings == l0 + 1 && Math.Abs(s.ShuttleBaseX - s.ShuttleGoalX) <= 2 && Math.Abs(s.ShuttleBaseY - s.ShuttleGoalY) <= 2);
            bool ok = kept == 0 && pass.ContainsKey(s) && dropped == 1 && !refuse.ContainsKey(s) && s.ShuttleViasPassed == 2 && nearW <= PassNearM && overWaterCruise && early == 0 && landed;
            return OzV11Kit.Verdict(ok, $"13. level 16, its own ship: a route over water (middle point on water tile {water.x >> 4},{water.y >> 4}) to a landing spot: the order filter keeps it ({(kept == 0 ? "yes" : "NO")}) and refuses the same route ending on the water ({(dropped == 1 ? "yes" : "NO")}); " +
                                         $"flown through both middle points ({s.ShuttleViasPassed}), over the water {F(nearW)} m from its point at the cruise height ({(overWaterCruise ? "yes" : "NO")}), early landings {early}, landed at the last point ({(landed ? "yes" : "NO")}, {F(w.Time, "0.0")} s)");
        }
    }
}
