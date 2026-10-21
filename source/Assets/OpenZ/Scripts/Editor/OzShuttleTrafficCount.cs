using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// K338, several shuttles in the air (rule DV118, Sim/ZWorld.ShuttleTraffic.cs), counted headless (ZWorld.Create + Tick, no scene, nothing
    /// saved; public members only, like every check of the editor assembly). Menu OpenZ/V1.1/Shuttle: K338 Air Traffic Check.
    /// On an open plain (no CPU: both teams take orders, as two players would), 2, 3 (not HEAD), 4 and 6 ships, own only and own against enemy (half of
    /// them blue), every case with <see cref="Seeds"/> seeds (the places, headings, order moments and points shifted from the seed):
    ///  CROSS  two groups whose ways cross (west to east, south to north), timed to meet in the middle;
    ///  HEAD   pairs on one line flying at each other (even seeds: they swap places; odd seeds: each flies past the other);
    ///  SAME   ships round a circle all sent to one point;
    ///  SWARM  a parked group (some only 10 m apart) all sent to one far point;
    ///  FIRE   CROSS with 4 ships under rocket fire from the ground (the strafe and the traffic together).
    /// Counted every tick: the closest two ships in the air come (their middles; a hull overlap = under ZWorld.ShuttleAirSepM), sideways
    /// reversals (the sideways motion off the line, the strafe's and the avoid offset's, turning back within 3 s of its last turn), the
    /// avoid offset's hardest acceleration, every landing's distance to the other ships standing (a shared spot = under ZWorld.ShuttleClaimM),
    /// ships that never landed, and each ship's delay: its flight from the order to the touchdown against the same flight alone (its start,
    /// its order moment, the spot it landed on, no other ship in the match).
    /// </summary>
    public static class OzShuttleTrafficCount
    {
        const double Tick = ZConst.TICK;
        /// <summary>Seeds per case, the most seconds a case runs, a delay counted as sane (s).</summary>
        public const int Seeds = 20;
        /// <summary>Seeds per case when the Deviations report's DV118 row runs this check (about 40 s a seed).</summary>
        public const int RowSeeds = 2;
        public const double MaxSec = 150.0, SaneDelaySec = 25.0;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static string F(double v, string f = "0.00") => v.ToString(f, Inv);

        // ---------------------------------------------------------------- the cases (the headless runner's K338Scenes.cs is made from this block)
        /// <summary>One case being run: the world, its ships, their order points and moments.</summary>
        public sealed class Scene
        {
            public ZWorld W;
            public string Kind; public int N, Seed; public bool Enemy;
            public readonly List<ZObject> Ships = new List<ZObject>();
            public readonly List<(int x, int y)> Starts = new List<(int, int)>(), Goals = new List<(int, int)>();
            public readonly List<float> Headings = new List<float>();
            public readonly List<Team> Teams = new List<Team>();
            public readonly List<double> OrderAt = new List<double>();
            public readonly List<bool> Ordered = new List<bool>();
            public int CentreX, CentreY;
        }

        /// <summary>The plain the cases fly over (128 x 128 tiles of ground; a blue dummy far in a corner keeps the match from ending).</summary>
        public static string PlainMap() => OzV11Kit.Plain(128, 128);

        static ZWorld NewWorld(string settings, string map, int seed)
        {
            var w = ZWorld.Create(settings, map, seed);
            w.Events.Clear();
            OzV11Kit.Dummy(w, 12 * 16, 12 * 16);
            return w;
        }

        /// <summary>Case `kind` with n ships (enemy: every second one blue), from the seed. only &gt;= 0: that ship alone, ordered to `goal`.</summary>
        public static Scene Build(string settings, string map, string kind, int n, bool enemy, int seed, int only = -1, (int x, int y)? goal = null, double orderAt = -1)
        {
            var sc = new Scene { Kind = kind, N = n, Seed = seed, Enemy = enemy };
            sc.W = NewWorld(settings, map, seed);
            var rng = new Random(seed * 7919 + n * 131 + Hash(kind) * 17 + (enemy ? 5003 : 0));
            int C = 64 * 16; sc.CentreX = C; sc.CentreY = C;
            double J(double m) => (rng.NextDouble() * 2 - 1) * m * 8;                                     // a jitter of up to m metres (px)
            var starts = new List<(double x, double y, double hd)>(); var goals = new List<(double x, double y)>(); var at = new List<double>(); var teams = new List<Team>();
            switch (kind)
            {
                case "cross": case "fire":
                {
                    int a = (n + 1) / 2, b = n - a; double d = 60 * 8, sp = 14 * 8;
                    for (int i = 0; i < a; i++) { double off = (i - (a - 1) * 0.5) * sp; starts.Add((C - d + J(3), C + off + J(3), 0)); goals.Add((C + d + J(3), C + off + J(3))); at.Add(rng.NextDouble() * 1.5); teams.Add(Team.Red); }
                    for (int i = 0; i < b; i++) { double off = (i - (b - 1) * 0.5) * sp; starts.Add((C + off + J(3), C + d + J(3), -90)); goals.Add((C + off + J(3), C - d + J(3))); at.Add(rng.NextDouble() * 1.5); teams.Add(enemy ? Team.Blue : Team.Red); }
                    break;
                }
                case "head":
                {
                    int pairs = n / 2; double d = 50 * 8, sp = 16 * 8; bool swap = seed % 2 == 0;
                    double rot = rng.NextDouble() * Math.PI, c = Math.Cos(rot), s = Math.Sin(rot);
                    (double, double) R(double x, double y) => (C + x * c - y * s, C + x * s + y * c);
                    for (int i = 0; i < pairs; i++)
                    {
                        double off = (i - (pairs - 1) * 0.5) * sp, j1 = J(1.5), j2 = J(1.5);
                        var (ax, ay) = R(-d, off + j1); var (bx, by) = R(d, off + j2);
                        double hdA = rot * 180 / Math.PI, hdB = hdA + 180;
                        starts.Add((ax, ay, hdA)); starts.Add((bx, by, hdB));
                        if (swap) { goals.Add((bx, by)); goals.Add((ax, ay)); }
                        else { var (gx, gy) = R(d + 20 * 8, off + j1); var (hx, hy) = R(-d - 20 * 8, off + j2); goals.Add((gx, gy)); goals.Add((hx, hy)); }
                        double t0 = rng.NextDouble() * 0.6; at.Add(t0); at.Add(t0 + rng.NextDouble() * 0.6);
                        teams.Add(Team.Red); teams.Add(enemy ? Team.Blue : Team.Red);
                    }
                    break;
                }
                case "same":
                {
                    double r0 = 55 * 8, a0 = rng.NextDouble() * 2 * Math.PI;
                    double gx = C + J(3), gy = C + J(3);
                    for (int i = 0; i < n; i++)
                    {
                        double a = a0 + 2 * Math.PI * i / n + (rng.NextDouble() - 0.5) * 0.5, r = r0 + J(12);
                        starts.Add((C + Math.Cos(a) * r, C + Math.Sin(a) * r, rng.NextDouble() * 360 - 180)); goals.Add((gx, gy));
                        at.Add(rng.NextDouble() * 2.0); teams.Add(enemy && i % 2 == 1 ? Team.Blue : Team.Red);
                    }
                    break;
                }
                case "swarm":
                {
                    double rot = rng.NextDouble() * 2 * Math.PI, c = Math.Cos(rot), s = Math.Sin(rot);
                    double sx = C - c * 70 * 8, sy = C - s * 70 * 8, gx = C + c * 70 * 8 + J(3), gy = C + s * 70 * 8 + J(3);
                    var grid = new[] { (0.0, 0.0), (0.0, 13.0), (0.0, -13.0), (-11.3, 6.5), (-11.3, -6.5), (-11.3, 19.5) };
                    for (int i = 0; i < n; i++)
                    {
                        double lx = grid[i].Item1, ly = grid[i].Item2;
                        if (i == 3 || i == 5) ly += i == 3 ? -3.0 : -3.0;                                          // two of them only ~10 m from a neighbour
                        double x = sx + (lx * c - ly * s) * 8 + J(0.5), y = sy + (lx * s + ly * c) * 8 + J(0.5);
                        starts.Add((x, y, rot * 180 / Math.PI)); goals.Add((gx, gy)); at.Add(0.2 * rng.NextDouble()); teams.Add(enemy && i % 2 == 1 ? Team.Blue : Team.Red);
                    }
                    break;
                }
            }
            for (int i = 0; i < starts.Count; i++)
            {
                if (only >= 0 && i != only) continue;
                var (cx, cy) = sc.W.ShuttleClamp((int)Math.Round(starts[i].x), (int)Math.Round(starts[i].y));
                var ship = sc.W.CreatePlayableShuttle(cx, cy, teams[i], (float)Norm(starts[i].hd));
                var (gx, gy) = goal ?? sc.W.ShuttleClamp((int)Math.Round(goals[i].x), (int)Math.Round(goals[i].y));
                sc.Ships.Add(ship); sc.Starts.Add((cx, cy)); sc.Goals.Add((gx, gy)); sc.Headings.Add(ship.ShuttleHeadingDeg); sc.Teams.Add(teams[i]);
                sc.OrderAt.Add(orderAt >= 0 ? orderAt : 0.5 + at[i]); sc.Ordered.Add(false);
            }
            if (kind == "fire" && only < 0)
            {   // rocket robots under the crossing, a little south of it (they fire at the red ships flying over)
                var lead = sc.W.CreateRobotGroup("tough", C - 8, C + 8 * 8 - 8, Team.Blue, 3);
                lead.MaxHealth = lead.Health = 1000000;
                foreach (var m in lead.Minions) if (m != null) { m.MaxHealth = m.Health = 1000000; }
            }
            return sc;
        }

        /// <summary>The orders whose moment has come (a MOVE, as the player's right click gives it).</summary>
        public static void Orders(Scene sc)
        {
            for (int i = 0; i < sc.Ships.Count; i++)
            {
                if (sc.Ordered[i] || sc.W.Time + 1e-9 < sc.OrderAt[i]) continue;
                sc.Ordered[i] = true;
                var s = sc.Ships[i];
                sc.W.SendWaypoints(s.Owner, s, new[] { new ZWaypoint { mode = WpMode.Move, refId = -1, x = sc.Goals[i].x, y = sc.Goals[i].y, playerGiven = true } });
            }
        }

        static double Norm(double a) { a %= 360.0; if (a > 180.0) a -= 360.0; else if (a <= -180.0) a += 360.0; return a; }
        static int Hash(string s) { unchecked { int h = 23; foreach (char c in s) h = h * 31 + c; return h & 0x7fffffff; } }
        // ---------------------------------------------------------------- the cases end

        static double MX(ZObject o) => o.LocX + o.XOver + o.WidthPix * 0.5;
        static double MY(ZObject o) => o.LocY + o.YOver + o.HeightPix * 0.5;

        /// <summary>What one run counted.</summary>
        public sealed class Result
        {
            public double MinAirM = double.MaxValue, PeakAccMps2, MinLandedM = double.MaxValue, Secs;
            public int Overlaps, OverlapTicks, Reversals, LatFlips, Shared, Stuck, Landed, Destroyed, Encounters, Moved, AirWaits, Holds, Strafes;
            public readonly List<double> FlightSec = new List<double>();
            public readonly List<(int x, int y)> LandedAt = new List<(int, int)>();
            public readonly List<int> LandedIdx = new List<int>();
            public string Worst = "";
        }

        /// <summary>Runs a built case to the end (every ship landed and the orders all given, or MaxSec), counting as it goes.</summary>
        public static Result Run(Scene sc, Action<Scene> each = null)
        {
            var r = new Result(); var w = sc.W; int n = sc.Ships.Count;
            int a0 = ZWorld.ShuttleAvoidsBegun, m0 = ZWorld.ShuttleSpotsMoved, w0 = ZWorld.ShuttleAirWaits, h0 = ZWorld.ShuttleHolds;
            var landings = sc.Ships.Select(s => s.ShuttleLandings).ToArray();
            var flown = new bool[n]; var done = new bool[n];
            var lastDir = new int[n]; var lastTurn = new double[n]; var strafes0 = sc.Ships.Select(s => s.ShuttleStrafes).ToArray();
            var lastVx = new double[n]; var lastVy = new double[n]; var lastSTurn = new double[n]; var lastLat = new int[n]; var lastLatAt = new double[n];
            for (int i = 0; i < n; i++) lastSTurn[i] = lastLatAt[i] = -1e9;
            for (int i = 0; i < n; i++) lastTurn[i] = -1e9;
            double sep = ZWorld.ShuttleAirSepM * 8, still = 0.1 * 8;
            var inside = new bool[n, n];
            while (w.Time < MaxSec)
            {
                Orders(sc);
                w.Tick(Tick);
                each?.Invoke(sc);
                bool all = true;
                for (int i = 0; i < n; i++)
                {
                    var s = sc.Ships[i];
                    if (s.Destroyed || s.KillAt >= 0) { if (!done[i]) { done[i] = true; r.Destroyed++; } continue; }
                    if (s.ShuttleAirborne) flown[i] = true;
                    // the motion off its line: the avoid offset's (its way over the ground turning back: more than 120 degrees) and the
                    // strafe's (its sideways speed changing sign); a turn within 3 s of the last turn (or of the start of that motion) is jitter
                    if (s.ShuttleAirborne)
                    {
                        double vx = s.ShuttleAvoidVelXPx, vy = s.ShuttleAvoidVelYPx, vl = Math.Sqrt(vx * vx + vy * vy);
                        if (vl > still)
                        {
                            double ll = Math.Sqrt(lastVx[i] * lastVx[i] + lastVy[i] * lastVy[i]);
                            if (ll < 1e-9) { lastTurn[i] = w.Time; }                                            // the motion begins
                            else if (vx * lastVx[i] + vy * lastVy[i] < -0.5 * vl * ll)
                            {
                                if (w.Time - lastTurn[i] < 3.0 - 1e-9) { r.Reversals++; if (r.Worst.Length < 160) r.Worst += $"ship {i} turned back {F(w.Time - lastTurn[i])} s after its last turn at {F(w.Time, "0.0")} s; "; }
                                lastTurn[i] = w.Time;
                            }
                            lastVx[i] = vx; lastVy[i] = vy;
                        }
                        double sv = s.ShuttleStrafeVelPx; int dir = sv > still ? 1 : sv < -still ? -1 : 0;
                        if (dir != 0 && dir != lastDir[i])
                        {
                            if (lastDir[i] != 0 && w.Time - lastSTurn[i] < 3.0 - 1e-9) { r.Reversals++; if (r.Worst.Length < 160) r.Worst += $"ship {i} strafe turned {F(w.Time - lastSTurn[i])} s after its last turn; "; }
                            lastDir[i] = dir; lastSTurn[i] = w.Time;
                        }
                        double lat = s.ShuttleStrafeVelPx + s.ShuttleAvoidLatVelPx; int ld = lat > still ? 1 : lat < -still ? -1 : 0;   // for information: across its facing
                        if (ld != 0 && ld != lastLat[i]) { if (lastLat[i] != 0 && w.Time - lastLatAt[i] < 3.0 - 1e-9) r.LatFlips++; lastLat[i] = ld; lastLatAt[i] = w.Time; }
                        r.PeakAccMps2 = Math.Max(r.PeakAccMps2, s.ShuttleAvoidAccelPx / 8.0);
                    }
                    else { lastDir[i] = 0; lastVx[i] = lastVy[i] = 0; lastLat[i] = 0; }
                    if (s.ShuttleLandings > landings[i])
                    {
                        landings[i] = s.ShuttleLandings;
                        if (sc.Ordered[i] && flown[i] && !done[i])
                        {
                            done[i] = true; r.Landed++; r.FlightSec.Add(w.Time - sc.OrderAt[i]); r.LandedAt.Add(((int)Math.Floor(s.ShuttleBaseX), (int)Math.Floor(s.ShuttleBaseY))); r.LandedIdx.Add(i);
                            for (int k = 0; k < n; k++)
                            {
                                var o = sc.Ships[k];
                                if (k == i || o.Destroyed || o.KillAt >= 0 || o.ShuttleAirborne) continue;
                                double d = Math.Sqrt((MX(o) - MX(s)) * (MX(o) - MX(s)) + (MY(o) - MY(s)) * (MY(o) - MY(s))) / 8.0;
                                r.MinLandedM = Math.Min(r.MinLandedM, d);
                                if (d < ZWorld.ShuttleClaimM - 1e-6) { r.Shared++; if (r.Worst.Length < 160) r.Worst += $"ship {i} landed {F(d)} m from ship {k}; "; }
                            }
                        }
                    }
                    if (!done[i]) all = false;
                }
                for (int i = 0; i < n; i++)
                    for (int k = i + 1; k < n; k++)
                    {
                        var a = sc.Ships[i]; var b = sc.Ships[k];
                        bool both = a.ShuttleAirborne && b.ShuttleAirborne && !a.Destroyed && !b.Destroyed && a.KillAt < 0 && b.KillAt < 0;
                        if (!both) { inside[i, k] = false; continue; }
                        double dx = MX(a) - MX(b), dy = MY(a) - MY(b), d = Math.Sqrt(dx * dx + dy * dy);
                        r.MinAirM = Math.Min(r.MinAirM, d / 8.0);
                        if (d < sep - 1e-6) { r.OverlapTicks++; if (!inside[i, k]) { inside[i, k] = true; r.Overlaps++; if (r.Worst.Length < 160) r.Worst += $"ships {i} and {k} {F(d / 8)} m apart at {F(w.Time, "0.0")} s; "; } }
                        else inside[i, k] = false;
                    }
                if (all && sc.Ordered.All(x => x)) break;
            }
            for (int i = 0; i < n; i++) if (!done[i]) r.Stuck++;
            r.Encounters = ZWorld.ShuttleAvoidsBegun - a0; r.Moved = ZWorld.ShuttleSpotsMoved - m0; r.AirWaits = ZWorld.ShuttleAirWaits - w0; r.Holds = ZWorld.ShuttleHolds - h0;
            for (int i = 0; i < n; i++) r.Strafes += sc.Ships[i].ShuttleStrafes - strafes0[i];
            r.Secs = w.Time;
            return r;
        }

        /// <summary>Each landed ship's flight alone: the same start, heading and order moment, to the spot it landed on, no other ship.</summary>
        static List<double> Alone(string settings, string map, Scene sc, Result r)
        {
            var list = new List<double>();
            for (int j = 0; j < r.LandedIdx.Count; j++)
            {
                int i = r.LandedIdx[j];
                var one = Build(settings, map, sc.Kind, sc.N, sc.Enemy, sc.Seed, i, r.LandedAt[j], sc.OrderAt[i]);
                var s = one.Ships[0]; int l0 = s.ShuttleLandings; bool up = false;
                while (one.W.Time < MaxSec)
                {
                    Orders(one); one.W.Tick(Tick);
                    if (s.ShuttleAirborne) up = true;
                    if (up && s.ShuttleLandings > l0) break;
                }
                list.Add(s.ShuttleLandings > l0 ? one.W.Time - one.OrderAt[0] : double.NaN);
            }
            return list;
        }

        public static string Check(string settings, string map16, int seeds = Seeds, string only = "")
        {
            var sb = new StringBuilder(); int pass = 0, fail = 0;
            void V(bool ok, string what) { sb.AppendLine((ok ? "PASS  " : "FAIL  ") + what); if (ok) pass++; else fail++; }
            ZWorld.ShuttlePlayableDeviation = true; ZWorld.ShuttleOn = true;
            string map = PlainMap();
            sb.AppendLine($"K338 the shuttles' air traffic: separation {F(ZWorld.ShuttleAirSepM, "0.0")} m (steering {F(ZWorld.ShuttleAirSoftM, "0.0")} m), spots {F(ZWorld.ShuttleClaimM, "0.0")} m apart, {seeds} seeds a case, on a 128 x 128 tile plain (no CPU)");
            var cases = new List<(string kind, int n, bool enemy)>();
            foreach (var k in new[] { "cross", "head", "same", "swarm" })
                foreach (int n in new[] { 2, 3, 4, 6 })
                    if (n != 3 || k != "head")                                                               // 3 ships: the testing panel's usual few (HEAD flies pairs)
                        foreach (bool e in new[] { false, true }) cases.Add((k, n, e));
            cases.Add(("fire", 4, false));
            double minAir = double.MaxValue, peakAcc = 0, worstDelay = 0, minLanded = double.MaxValue; int overlaps = 0, reversals = 0, latFlips = 0, shared = 0, stuck = 0, runs = 0, landed = 0, enc = 0, moved = 0, waits = 0, holds = 0, strafes = 0, destroyed = 0;
            var delays = new List<double>(); string worst = "";
            foreach (var (kind, n, enemy) in cases)
            {
                string name = $"{kind} {n} {(enemy ? "own+enemy" : "own")}";
                if (only != "" && !only.Split(',').Any(o => name.StartsWith(o))) continue;
                double cMin = double.MaxValue, cAcc = 0, cWorstD = 0, cMinL = double.MaxValue; string cWorstAt = "-"; double cRatio = 0; int cOv = 0, cRev = 0, cLat = 0, cSh = 0, cSt = 0, cEnc = 0, cMov = 0, cWait = 0, cHold = 0, cStr = 0, cLand = 0, cDes = 0; var cDel = new List<double>();
                for (int seed = 1; seed <= seeds; seed++)
                {
                    var sc = Build(settings, map, kind, n, enemy, seed);
                    var r = Run(sc);
                    var alone = Alone(settings, map, sc, r);
                    for (int j = 0; j < alone.Count; j++)
                        if (!double.IsNaN(alone[j]))
                        {
                            double d = r.FlightSec[j] - alone[j]; cDel.Add(d); cRatio = Math.Max(cRatio, r.FlightSec[j] / Math.Max(1.0, alone[j]));
                            if (d > cWorstD) { cWorstD = d; cWorstAt = $"seed {seed} ship {r.LandedIdx[j]}: {F(r.FlightSec[j], "0.0")} s against {F(alone[j], "0.0")} s alone"; }
                        }
                    cMin = Math.Min(cMin, r.MinAirM); cAcc = Math.Max(cAcc, r.PeakAccMps2); cMinL = Math.Min(cMinL, r.MinLandedM);
                    cOv += r.Overlaps; cRev += r.Reversals; cLat += r.LatFlips; cSh += r.Shared; cSt += kind == "fire" ? 0 : r.Stuck; cEnc += r.Encounters; cMov += r.Moved; cWait += r.AirWaits; cHold += r.Holds; cStr += r.Strafes; cLand += r.Landed; cDes += r.Destroyed;
                    if (r.Worst != "" && worst.Length < 600) worst += $"[{name} seed {seed}] {r.Worst}";
                    runs++;
                }
                sb.AppendLine($"      {name,-18} closest in the air {F(cMin)} m, hull overlaps {cOv}, turns back within 3 s {cRev} (across its facing, for information: {cLat}), hardest avoid acceleration {F(cAcc)} m/s^2; landed {cLand}" +
                              $"{(cDes > 0 ? $" (shot down {cDes})" : "")}, closest standing at a landing {(cMinL < 1e9 ? F(cMinL) : "-")} m, shared spots {cSh}, never landed {cSt}; delay against alone: mean {(cDel.Count > 0 ? F(cDel.Average()) : "-")} s, most {F(cWorstD)} s ({cWorstAt}); " +
                              $"encounters {cEnc}, goals moved to a free spot {cMov}, take-offs that waited for room {cWait}, holds over a spot being left {cHold}{(kind == "fire" ? $", strafes {cStr}" : "")}");
                minAir = Math.Min(minAir, cMin); peakAcc = Math.Max(peakAcc, cAcc); worstDelay = Math.Max(worstDelay, cWorstD); minLanded = Math.Min(minLanded, cMinL);
                overlaps += cOv; reversals += cRev; latFlips += cLat; shared += cSh; stuck += cSt; landed += cLand; enc += cEnc; moved += cMov; waits += cWait; holds += cHold; strafes += cStr; destroyed += cDes; delays.AddRange(cDel);
            }
            if (worst != "") sb.AppendLine("      first findings: " + worst);
            V(overlaps == 0 && minAir >= ZWorld.ShuttleAirSepM - 1e-6, $"1. 0 hull overlaps in the air in {runs} runs: the closest two ships came {F(minAir)} m (middles; at least {F(ZWorld.ShuttleAirSepM, "0.0")} m)");
            V(reversals == 0, $"2. 0 jitter: the motion off its line (the avoid offset over the ground, the strafe across its facing) turning back within 3 s of its last turn {reversals} (the avoid and strafe motion across a turning facing changing side within 3 s, for information: {latFlips}); the avoid offset's hardest acceleration {F(peakAcc)} m/s^2 (at most {F(ZWorld.ShuttleAvoidAccMax + 0.05)}: never a hard push)");
            V(peakAcc <= ZWorld.ShuttleAvoidAccMax + 0.05, $"3. never a hard push: the hardest avoid acceleration {F(peakAcc)} m/s^2");
            V(shared == 0 && minLanded >= ZWorld.ShuttleClaimM - 1e-6, $"4. 0 shared landing spots: at {landed} landings the nearest other ship standing was {(minLanded < 1e9 ? F(minLanded) : "-")} m away (at least {F(ZWorld.ShuttleClaimM, "0.0")} m)");
            V(stuck == 0, $"5. every ship landed ({landed} landings, never landed {stuck}; the fire case's ships shot down {destroyed} not counted there)");
            V(delays.Count > 0 && worstDelay <= SaneDelaySec, $"6. sane delays against each ship's flight alone: mean {(delays.Count > 0 ? F(delays.Average()) : "-")} s, 90 % within {(delays.Count > 0 ? F(Pct(delays, 0.9)) : "-")} s, most {F(worstDelay)} s (at most {F(SaneDelaySec, "0")} s)");
            sb.AppendLine($"      counts: encounters {enc}, goals moved to a free spot {moved}, take-offs that waited for room in the air {waits}, holds over a spot being left {holds}, strafes in the fire case {strafes}");
            sb.AppendLine($"{pass} PASS, {fail} FAIL");
            return sb.ToString();
        }

        static double Pct(List<double> v, double q) { var s = v.OrderBy(x => x).ToList(); return s[Math.Min(s.Count - 1, (int)Math.Floor(q * (s.Count - 1) + 0.5))]; }


#if UNITY_EDITOR
        [UnityEditor.MenuItem("OpenZ/V1.1/Shuttle: K338 Air Traffic Check")]
        static void Menu() => OzV11Kit.RunMenu("K338 the shuttles' air traffic", () => Check(OzV11Kit.SettingsText, OzV11Kit.MapText("L16")));
        [UnityEditor.MenuItem("OpenZ/V1.1/Shuttle: K338 Air Traffic Check (quick, 3 seeds)")]
        static void MenuQuick() => OzV11Kit.RunMenu("K338 the shuttles' air traffic, 3 seeds", () => Check(OzV11Kit.SettingsText, OzV11Kit.MapText("L16"), 3));
#endif
    }
}
