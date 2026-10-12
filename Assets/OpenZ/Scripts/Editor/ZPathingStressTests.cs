using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using OpenZ.Sim;
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// sim at 100 Hz on both real maps (terrain, buildings, bridges and rocks kept; map units, crates and flags removed; no
    /// production). Large mixed groups (6 squads = 19 robots + 6 vehicles per team) are sent, all to the same point like a
    /// Zod group order, across bridges, through the L02 island chokepoint, into rivers and the harbour (robots wade, vehicles
    /// route around or stop at the bank), to unreachable spots (cliff, building interior) and back, plus two-way traffic
    /// (two armies of one team swapping banks over one bridge). Groups start on open ground (not in a rock pocket, not on a
    /// bridge or in its mouth). First, 5 detector self-tests prove every check fires on a case built to trip it (layer off)
    /// and stays quiet on a clean case (layer on). Every tick it checks:
    ///   - stuck: a unit with an order that has not moved 2 px for more than 3 s of game time, unless it waits in an orderly
    ///     way (lane, queue behind moving / waiting units; ZPathing.IsQueueWaiting) while the test itself sees traffic moving
    ///     within 12 tiles; orderly waits must stay under 120 s;
    ///   - overlap: two units closer than ZPathing.MinSpacing - 1.5 px for more than 0.5 s (a robot boarding its vehicle, Zod's
    ///     scripted building moves and a fresh squad walking out of its factory excepted, ZPathing.PairExempt);
    ///   - water: any vehicle footprint on a water or deep-water tile (same probe as the parity suite);
    ///   - walls: any unit box (shrunk by 2 px) on a tile its own grid blocks (buildings, rocks, rails, cliffs; water for
    ///     vehicles);
    ///   - end of each phase: nobody left with an order, every unit within 14 tiles of its goal (16 of the nearest reachable
    ///     point for unreachable goals), every minion within 4 tiles of its leader.
    /// Variant > 0 re-runs the same scenarios from 17 other start layouts (robustness; the harness runs 0..17).
    /// Then it re-runs the parity suite's movement rows (speeds on ground / road / water for all 13 movers, running, squads,
    /// vehicles vs water) with the pathing layer on: they must still PASS unchanged.
    /// </summary>
    public static class ZPathingStressTests
    {
        public const double StuckLimit = 3.0, OverlapLimit = 0.5, OrderlyWaitLimit = 120, OverlapTolerance = 1.5, ArrivalTiles = 14, ArrivalTilesResolved = 16, SquadTiles = 4;

        public sealed class Row { public string test, expected, measured, note; public bool pass; }
        public sealed class Result { public string summary, file; public List<Row> rows = new List<Row>(); public int pass, total; }

        static string SettingsText => Resources.Load<TextAsset>("OpenZ/default_settings").text;
        static string MapText(string lv) => Resources.Load<TextAsset>($"OpenZ/Maps/{lv}_grid").text;

        /// <summary>Robustness runs: variant v > 0 shifts every group's anchor (v 1-8: one tile in 8 directions, v 9-16: two tiles,
        /// v 17: three) and the world seed, so the same scenarios run from a different crowd layout. 0 = the standard test.</summary>
        public static int Variant;
        static readonly (int dx, int dy)[] VariantShift = { (0, 0), (1, 0), (0, 1), (-1, 0), (0, -1), (1, 1), (-1, -1), (1, -1), (-1, 1) };

        /// <summary>Harness / debug hook, called after every tick of a phase.</summary>
        public static Action<string, ZWorld, List<ZObject>> OnTick;
        /// <summary>Harness / debug hook, called after every phase with its name, world and units.</summary>
        public static Action<string, ZWorld, List<ZObject>, Dictionary<ZObject, (int x, int y)>> OnPhaseEnd;

        static readonly string[] Squads = { "grunt", "tough", "pyro", "laser", "sniper", "psycho" };
        static readonly string[] Vehicles = { "jeep", "light", "medium", "heavy", "apc", "missile_launcher" };

        [MenuItem("OpenZ/Run Pathing Stress Test")]
        public static string RunMenu() => Run().summary;

        /// <summary>pathing = false runs the same scenarios on pure Zod movement (A/B); the speed rows are skipped then.</summary>
        public static Result Run(bool pathing = true, string reportFile = null)
        {
            var res = new Result();
            var t0 = DateTime.Now;
            if (Variant == 0) DetectorSelfTests(res.rows);
            L02(res.rows, pathing);
            L17(res.rows, pathing);
            if (pathing && Variant == 0)
                foreach (var r in OpenZParity.MovementParityRows())
                    res.rows.Add(new Row { test = "speed parity (pathing on): " + r.test, expected = r.expected, measured = r.measured, pass = r.pass, note = r.note });
            res.pass = res.rows.Count(r => r.pass); res.total = res.rows.Count;
            var sb = new StringBuilder();
            sb.AppendLine("# OpenZ pathing stress test");
            sb.AppendLine();
            sb.AppendLine($"Generated {DateTime.Now:yyyy-MM-dd HH:mm} by `OpenZ/Run Pathing Stress Test` (headless Zod sim, 100 Hz), pathing layer " +
                          $"**{(pathing ? "ON" : "OFF (pure Zod)")}**, {(DateTime.Now - t0).TotalSeconds:F0} s wall time. **{res.pass}/{res.total} PASS**.");
            sb.AppendLine();
            sb.AppendLine($"Limits: stuck > {StuckLimit} s (orderly lane / queue waits excepted while traffic within 12 tiles moves, < {OrderlyWaitLimit} s), overlap closer than the spacing - {OverlapTolerance} px " +
                          $"for > {OverlapLimit} s (robots 12 px, robot-vehicle 22 px, vehicles 32 px), no vehicle on water, no unit on a wall / building tile, nobody left with an order, arrival within {ArrivalTiles} tiles. " +
                          "The first rows run each detector on a case built to trip it (layer off) and on a clean case (layer on).");
            sb.AppendLine();
            sb.AppendLine("| Test | Expected | Measured | Result | Note |");
            sb.AppendLine("|---|---|---|---|---|");
            foreach (var r in res.rows) sb.AppendLine($"| {r.test} | {r.expected} | {r.measured} | {(r.pass ? "PASS" : "**FAIL**")} | {r.note} |");
            res.file = reportFile ?? Path.Combine(OpenZPaths.Records, pathing ? "pathing_stress_report.md" : "pathing_stress_report_zod_raw.md");
            File.WriteAllText(res.file, sb.ToString());
            res.summary = $"pathing stress ({(pathing ? "on" : "off")}): {res.pass}/{res.total} PASS" +
                          string.Concat(res.rows.Where(r => !r.pass).Select(r => $"\nFAIL {r.test}: {r.measured} {r.note}"));
            Debug.Log(res.summary + " -> " + res.file);
            return res;
        }

        // ------------------------------------------------------------------ scenarios
        static void L02(List<Row> rows, bool pathing)
        {
            // A: east bridge (bridge_vert 51,58 over the river rows 60-61), north -> south and back
            {
                var w = World("L02", 21, pathing);
                var red = Group(w, Team.Red, 46, 53);
                Phase(rows, w, red, "L02 east bridge N->S (6 squads + 6 vehicles)", 52, 69);
                Phase(rows, w, red, "L02 east bridge S->N (back)", 46, 53);
            }
            // B: two-way traffic on the same bridge: two armies of one team swap banks (reinforcements forward, the wounded back)
            {
                var w = World("L02", 22, pathing);
                var north = Group(w, Team.Red, 46, 53); var south = Group(w, Team.Red, 58, 66);
                Phase(rows, w, north.Concat(south).ToList(), "L02 east bridge two-way (two red armies swap banks, 50 units)", 0, 0,
                      north.Select(u => (u, 58, 66)).Concat(south.Select(u => (u, 46, 53))).ToList());
            }
            // C: the island: both horizontal bridges (26,44) and (41,44) west -> east and back
            {
                var w = World("L02", 23, pathing);
                var red = Group(w, Team.Red, 10, 46);
                Phase(rows, w, red, "L02 island chokepoint W->E (bridges 26,44 + 41,44)", 62, 46);
                Phase(rows, w, red, "L02 island chokepoint E->W (back)", 10, 46);
            }
            // D: into the river: robots wade in, vehicles stop at the nearest bank
            {
                var w = World("L02", 24, pathing);
                var red = Group(w, Team.Red, 26, 20);
                Phase(rows, w, red, "L02 into the river (35,20): robots wade, vehicles at the bank", 35, 20);
                Phase(rows, w, red, "L02 out of the river (back)", 26, 20);
            }
            // E: unreachable targets and back
            {
                var w = World("L02", 25, pathing);
                var red = Group(w, Team.Red, 46, 30);
                Phase(rows, w, red, "L02 unreachable: inside the cliff (70,60)", 70, 60);
                Phase(rows, w, red, "L02 unreachable: inside the red fort (52,76)", 52, 76);
                Phase(rows, w, red, "L02 back to the start", 46, 30);
            }
        }

        static void L17(List<Row> rows, bool pathing)
        {
            // A: canal (x 51-58): robots wade across, vehicles take a land crossing
            {
                var w = World("L17", 31, pathing);
                var red = Group(w, Team.Red, 36, 40);
                Phase(rows, w, red, "L17 canal crossing W->E (robots wade, vehicles by land)", 78, 36);
                Phase(rows, w, red, "L17 canal crossing E->W (back)", 36, 40);
            }
            // B: repaired bridge (52,94) two-way: red west -> east, blue east -> west
            {
                var w = World("L17", 32, pathing);
                var br = w.Objects.First(o => o.IsBridge && o.LocX == 52 * 16 && o.LocY == 94 * 16);
                br.SetHealth(br.MaxHealth); br.DoAutoRepair = false;
                var west = Group(w, Team.Red, 38, 96); var east = Group(w, Team.Red, 72, 96);
                Phase(rows, w, west.Concat(east).ToList(), "L17 repaired bridge (52,94) two-way (two red armies swap banks, 50 units)", 0, 0,
                      west.Select(u => (u, 72, 96)).Concat(east.Select(u => (u, 38, 96))).ToList());
            }
            // C: destroyed bridge (52,114): vehicles must go round by the north, robots wade the canal
            {
                var w = World("L17", 33, pathing);
                var red = Group(w, Team.Red, 38, 112);
                Phase(rows, w, red, "L17 across the destroyed bridge (52,114): vehicles route around", 72, 112, maxT: 500);
            }
            // D: harbour water and unreachable cliff / building
            {
                var w = World("L17", 34, pathing);
                var red = Group(w, Team.Red, 32, 100);
                Phase(rows, w, red, "L17 into the harbour (12,98): robots wade, vehicles at the shore", 12, 98);
                Phase(rows, w, red, "L17 unreachable: inside the repair station (9,22)", 9, 22, maxT: 450);
                Phase(rows, w, red, "L17 unreachable: east cliff (107,40)", 107, 40, maxT: 450);
            }
        }

        // ------------------------------------------------------------------ detector self-tests
        /// <summary>The checks must catch what they are meant to catch: each detector runs on a case built to trip it (pure Zod,
        /// layer off) and must fire; a clean case with the layer on must trip none of them. A 48 x 48 test map: open ground, a
        /// water pool (tiles 30-35 x 10-20) and an impassable block (tiles 10-13 x 30-33).</summary>
        internal static void DetectorSelfTests(List<Row> rows)
        {
            // 1. overlap: two empty jeeps parked on one spot, and a Zod squad (3 grunts) walking stacked on one pixel
            {
                var w = TestWorld(false);
                var a = w.CreateObject("vehicle", "jeep", 5 * 16, 5 * 16, Team.Neutral);
                var b = w.CreateObject("vehicle", "jeep", 5 * 16 + 4, 5 * 16 + 4, Team.Neutral);
                var sq = w.CreateRobotGroup("grunt", 5 * 16, 22 * 16, Team.Red, 3);
                var list = new List<ZObject> { a, b, sq }; list.AddRange(sq.Minions);
                w.SendWaypoints(Team.Red, sq, new[] { ZWaypoint.Move(25 * 16 + 8, 22 * 16 + 8) });
                var watch = RunFor(w, list, 2.0, false);
                rows.Add(SelfRow("detector self-test: overlap (2 jeeps parked on one spot + a Zod squad walking stacked, layer off)",
                                 "4 pairs flagged", $"{watch.OverlapPairs} pairs, longest {watch.MaxOverlap:F2} s", watch.OverlapPairs == 4));
            }
            // 2. stuck: a jeep that keeps its order but cannot move (speed 0 after the order was accepted)
            {
                var w = TestWorld(false);
                var j = w.CreateObject("vehicle", "jeep", 5 * 16, 5 * 16, Team.Red);
                w.SendWaypoints(Team.Red, j, new[] { ZWaypoint.Move(20 * 16, 6 * 16) });
                j.MoveSpeed = 0; j.RealMoveSpeed = 0;
                var watch = RunFor(w, new List<ZObject> { j }, 4.0, false);
                rows.Add(SelfRow("detector self-test: stuck (a jeep keeps its order but cannot move for 4 s, layer off)",
                                 "1 unit flagged", $"{watch.StuckUnits} units, stuck max {watch.MaxStuck:F1} s", watch.StuckUnits == 1 && watch.MaxStuck > StuckLimit));
            }
            // 3 + 4. water and walls: a jeep parked in the pool, a light tank and a robot on the impassable block
            {
                var w = TestWorld(false);
                var j = w.CreateObject("vehicle", "jeep", 31 * 16, 12 * 16, Team.Red);
                var t = w.CreateObject("vehicle", "light", 10 * 16, 30 * 16, Team.Red);
                var r = w.CreateRobotGroup("grunt", 13 * 16, 33 * 16, Team.Red, 1);
                var watch = RunFor(w, new List<ZObject> { j, t, r }, 0.5, false);
                rows.Add(SelfRow("detector self-test: vehicle on water (a jeep parked in a pool, layer off)",
                                 "jeep flagged", $"{watch.WetTicks} ticks ({watch.WetWho})", watch.WetTicks > 0 && watch.WetWho.Contains("jeep")));
                rows.Add(SelfRow("detector self-test: unit in a wall (light tank + robot on an impassable block, jeep in water, layer off)",
                                 "3 units flagged", $"{watch.WallUnits} units ({watch.WallWho})", watch.WallUnits == 3));
            }
            // 5. clean case, layer on: a squad created on one pixel and two vehicles cross open ground to one point
            {
                var w = TestWorld(true);
                var sq = w.CreateRobotGroup("pyro", 4 * 16, 40 * 16, Team.Red);
                var j = w.CreateObject("vehicle", "jeep", 4 * 16, 43 * 16, Team.Red);
                var m = w.CreateObject("vehicle", "medium", 8 * 16, 40 * 16, Team.Red);
                var list = new List<ZObject> { sq, j, m }; list.AddRange(sq.Minions);
                foreach (var u in new[] { sq, j, m }) w.SendWaypoints(Team.Red, u, new[] { ZWaypoint.Move(40 * 16 + 8, 40 * 16 + 8) });
                var watch = RunFor(w, list, 60.0, true);
                bool idle = list.All(u => u.Waypoints.Count == 0);
                rows.Add(SelfRow("detector self-test: clean case (layer on: a squad built on one pixel + 2 vehicles cross open ground)",
                                 "no detector fires, all arrive", $"stuck {watch.StuckUnits}, overlap pairs {watch.OverlapPairs} (longest {watch.MaxOverlap:F2} s), water {watch.WetTicks}, walls {watch.WallUnits}, {(idle ? "all idle" : "orders left")}, {w.Time:F0} s",
                                 watch.StuckUnits == 0 && watch.OverlapPairs == 0 && watch.WetTicks == 0 && watch.WallUnits == 0 && idle && w.Pathing.FailSafeError == null));
            }
        }

        static Row SelfRow(string test, string expected, string measured, bool pass) => new Row { test = test, expected = expected, measured = measured, pass = pass };

        static ZWorld TestWorld(bool pathing)
        {
            const int n = 48;
            var cls = new int[n * n];
            for (int y = 10; y <= 20; y++) for (int x = 30; x <= 35; x++) cls[y * n + x] = (int)ZClass.Water;
            for (int y = 30; y <= 33; y++) for (int x = 10; x <= 13; x++) cls[y * n + x] = (int)ZClass.Impassable;
            var sb = new StringBuilder("{\"file\":\"pathing_selftest\",\"planet\":\"desert\",\"width\":" + n + ",\"height\":" + n + ",\"classes\":[");
            sb.Append(string.Join(",", cls)).Append("],\"tiles\":[").Append(string.Join(",", Enumerable.Repeat("0", n * n)));
            sb.Append("],\"zones\":[{\"x\":0,\"y\":0,\"w\":" + n + ",\"h\":" + n + "}],\"objects\":[]}");
            var w = ZWorld.Create(SettingsText, sb.ToString(), 7);
            w.Pathing.Enabled = pathing;
            w.Events.Clear();
            return w;
        }

        static Watch RunFor(ZWorld w, List<ZObject> units, double seconds, bool untilIdle)
        {
            var watch = new Watch(w, units);
            double t0 = w.Time;
            while (w.Time - t0 < seconds)
            {
                w.Tick(ZConst.TICK); w.Events.Clear(); watch.Tick();
                if (untilIdle && w.Time - t0 > 1 && units.All(u => w.Get(u.Id) != u || u.Waypoints.Count == 0)) break;
            }
            return watch;
        }

        // ------------------------------------------------------------------ world + groups
        /// <summary>The real map: terrain, buildings, bridges, rocks, huts and map objects; no units, crates, flags or production.</summary>
        static ZWorld World(string lv, int seed, bool pathing)
        {
            var w = ZWorld.Create(SettingsText, MapText(lv), seed + 100 * Variant);
            w.Pathing.Enabled = pathing;
            w.Objects.RemoveAll(o => o.IsUnit || o.Type == "grenades" || o.Type == "flag");
            w.Flags.Clear();
            foreach (var b in w.Objects) if (b.Kind == ZKind.Building) b.StopBuildingProduction();
            w.Pathing.EnsureLanes();
            return w;
        }

        /// <summary>6 squads (Grunt 3, Tough 2, Pyro 4, Laser 4, Sniper 3, Psycho 3) + 6 vehicles (Jeep, Light, Medium, Heavy,
        /// APC, Mobile Missile) around an anchor tile, each squad on one pixel like Zod (zserver.cpp:623-657).</summary>
        static List<ZObject> Group(ZWorld w, Team team, int ax, int ay)
        {
            var reach = w.Pathing.Reach;
            if (Variant > 0)
            {                                                                              // v1-8: 1 tile in 8 directions, v9-16: 2 tiles, v17+: 3+ tiles
                var sh = VariantShift[1 + (Variant - 1) % 8]; int k = 1 + (Variant - 1) / 8;   // (the seed alone changes nothing here: no fights,
                ax += sh.dx * k; ay += sh.dy * k;                                          // so every variant needs its own layout)
            }
            var placed = new List<(int x, int y, int r)>();
            var all = new List<ZObject>();
            int cr = AnchorComp(w, ax, ay, true), cv = AnchorComp(w, ax, ay, false);
            bool Free(int x, int y, int r) => placed.All(p => (p.x - x) * (p.x - x) + (p.y - y) * (p.y - y) >= (p.r + r) * (p.r + r)) &&
                                              !w.Pathing.Lanes.Any(l => x >= l.KX0 && x < l.KX1 && y >= l.KY0 && y < l.KY1);   // nobody starts on a bridge / in its mouth
            IEnumerable<(int tx, int ty)> Ring()
            {
                for (int r = 0; r <= 24; r++)
                    for (int ty = ay - r; ty <= ay + r; ty++)
                        for (int tx = ax - r; tx <= ax + r; tx++)
                            if (Math.Max(Math.Abs(tx - ax), Math.Abs(ty - ay)) == r) yield return (tx, ty);
            }
            bool Open(int tx0, int ty0, int n, bool robot)
            {                                                                              // an open n x n tile block (no dead-end pocket)
                for (int j = ty0; j < ty0 + n; j++) for (int i = tx0; i < tx0 + n; i++) if (!w.Path.TilePassableGrid(i, j, robot, false)) return false;
                return true;
            }
            foreach (var type in Vehicles)
                foreach (var (tx, ty) in Ring())
                {
                    if (reach.Comp(tx, ty, false, false) != cv || !Open(tx - 1, ty - 1, 4, false)) continue;
                    int cx = tx * 16 + 16, cy = ty * 16 + 16;
                    if (!Free(cx, cy, 26)) continue;
                    var v = w.CreateObject("vehicle", type, tx * 16, ty * 16, team);
                    placed.Add((cx, cy, 26)); all.Add(v);
                    break;
                }
            foreach (var type in Squads)
                foreach (var (tx, ty) in Ring())
                {
                    if (reach.Comp(tx, ty, true, false) != cr || !Open(tx - 1, ty - 1, 3, true)) continue;
                    int cx = tx * 16 + 8, cy = ty * 16 + 8;
                    if (!Free(cx, cy, 14)) continue;
                    var l = w.CreateRobotGroup(type, tx * 16, ty * 16, team);
                    placed.Add((cx, cy, 14)); all.Add(l); all.AddRange(l.Minions);
                    break;
                }
            return all;
        }

        static int AnchorComp(ZWorld w, int ax, int ay, bool robot)
        {
            var reach = w.Pathing.Reach;
            for (int r = 0; r <= 6; r++)
                for (int ty = ay - r; ty <= ay + r; ty++)
                    for (int tx = ax - r; tx <= ax + r; tx++)
                    {
                        int c = reach.Comp(tx, ty, robot, false);
                        if (c >= 0) return c;
                    }
            return -1;
        }

        // ------------------------------------------------------------------ one phase = one group order, run until everyone idles
        static void Phase(List<Row> rows, ZWorld w, List<ZObject> units, string name, int gx, int gy, List<(ZObject u, int tx, int ty)> perUnit = null, double maxT = 360)
        {
            var goals = new Dictionary<ZObject, (int x, int y)>();
            var orders = perUnit ?? units.Select(u => (u, gx, gy)).ToList();
            foreach (var (u, tx, ty) in orders)
            {
                if (w.Get(u.Id) != u) continue;
                u.PathState.LastOrderEnd = null; foreach (var m in u.Minions) m.PathState.LastOrderEnd = null;
                goals[u] = (tx * 16 + 8, ty * 16 + 8);
                foreach (var m in u.Minions) goals[m] = goals[u];
                if (u.IsMinion) continue;
                w.SendWaypoints(u.Owner, u, new[] { ZWaypoint.Move(tx * 16 + 8, ty * 16 + 8) });
            }
            var watch = new Watch(w, units);
            long lanesHeld0 = w.Pathing.LaneHolds;
            double t0 = w.Time, idleSince = -1;
            while (w.Time - t0 < maxT)
            {
                w.Tick(ZConst.TICK);
                w.Events.Clear();
                watch.Tick();
                OnTick?.Invoke(name, w, units);
                bool idle = units.All(u => w.Get(u.Id) != u || u.Waypoints.Count == 0);         // (no waypoint = idle: Zod may leave a < 0.1 px/s velocity, zobject.cpp:3646)
                if (!idle) idleSince = -1;
                else if (idleSince < 0) idleSince = w.Time;
                else if (w.Time - idleSince >= 1.0) break;
            }
            OnPhaseEnd?.Invoke(name, w, units, goals);
            // end state: orders left, distance to the goal (or to the nearest point its unit can reach)
            var alive = units.Where(u => w.Get(u.Id) == u).ToList();
            int left = alive.Count(u => u.Waypoints.Count > 0);
            double worst = 0, worstRatio = 0, worstSquad = 0; string worstWho = "-", squadWho = "-";
            foreach (var u in alive)
            {
                if (u.IsMinion)
                {                                                                              // Zod: minions stop when their leader stops
                    double ds = Math.Sqrt((double)(u.CenterX - u.Leader.CenterX) * (u.CenterX - u.Leader.CenterX) + (double)(u.CenterY - u.Leader.CenterY) * (u.CenterY - u.Leader.CenterY)) / 16;
                    if (ds > worstSquad) { worstSquad = ds; squadWho = $"{u.Type}#{u.Id}(m) at ({u.CenterX >> 4},{u.CenterY >> 4})"; }
                    continue;
                }
                var (x, y) = goals.TryGetValue(u, out var g) ? g : (u.CenterX, u.CenterY);
                bool nr = u.HasExplosives(); int c = w.Pathing.Reach.UnitComp(u, nr);
                double limit = ArrivalTiles;
                if (c >= 0 && !w.Pathing.Reach.PointOk(x, y, u.IsRobot, nr, c) && w.Pathing.Reach.Nearest(x, y, u.IsRobot, nr, c, out int rx, out int ry)) { x = rx; y = ry; limit = ArrivalTilesResolved; }
                double d = Math.Sqrt((double)(u.CenterX - x) * (u.CenterX - x) + (double)(u.CenterY - y) * (u.CenterY - y)) / 16;
                if (d / limit > worstRatio)
                {
                    worstRatio = d / limit; worst = d;
                    worstWho = $"{u.Type}#{u.Id} at ({u.CenterX >> 4},{u.CenterY >> 4}), limit {limit}{(u.PathState.LastOrderEnd != null ? ", " + u.PathState.LastOrderEnd : "")}";
                }
            }
            var st = alive.Select(u => u.PathState).ToList();
            string counters = $"re-paths {st.Sum(s => s.Repaths)}, resolved goals {st.Sum(s => s.Resolves)}, give-ups {st.Sum(s => s.GiveUps)}, crowd arrivals {st.Sum(s => s.Arrivals)}, " +
                              $"step-asides {st.Sum(s => s.Shoves)}, back-offs {st.Sum(s => s.BackOffs)}, yields {st.Sum(s => s.Yields)}, regroups {st.Sum(s => s.Regroups)}, follow-throughs {st.Sum(s => s.FollowThroughs)}, watchdog drops {st.Sum(s => s.Watchdogs)}, pushes {st.Sum(s => s.Displaced)}, lane holds {w.Pathing.LaneHolds - lanesHeld0}";
            if (w.Pathing.FailSafeError != null) counters += " FAIL-SAFE TRIPPED: " + w.Pathing.FailSafeError.Split('\n')[0];
            bool ok = w.Pathing.FailSafeError == null && watch.StuckUnits == 0 && watch.OverlapPairs == 0 && watch.WetTicks == 0 && watch.WallTicks == 0 && left == 0 && worstRatio <= 1.0 && worstSquad <= SquadTiles && watch.MaxWait <= OrderlyWaitLimit;
            rows.Add(new Row
            {
                test = name,
                expected = $"stuck <= {StuckLimit} s, overlap <= {OverlapLimit} s, dry vehicles, nobody in a wall / building, 0 orders left, arrival <= {ArrivalTiles} tiles ({ArrivalTilesResolved} if resolved), minions <= {SquadTiles} tiles from leader",
                measured = $"{w.Time - t0:F0} s; stuck max {watch.MaxStuck:F1} s ({watch.StuckUnits} units > {StuckLimit} s{(watch.StuckUnits > 0 ? ": " + watch.StuckWho : "")}); " +
                           $"overlap max {watch.MaxOverlap:F2} s ({watch.OverlapPairs} pairs > {OverlapLimit} s{(watch.OverlapPairs > 0 ? ": " + watch.OverlapWho : "")}); " +
                           $"vehicle on water {watch.WetTicks} ticks{(watch.WetTicks > 0 ? " (" + watch.WetWho + ")" : "")}; in a wall {watch.WallTicks} unit-ticks{(watch.WallTicks > 0 ? " (" + watch.WallWho + ")" : "")}; " +
                           $"orders left {left}; arrival max {worst:F1} tiles ({worstWho}); squad spread max {worstSquad:F1} tiles ({squadWho}); " +
                           $"orderly wait max {watch.MaxWait:F1} s",
                pass = ok,
                note = counters
            });
        }

        /// <summary>Per-tick checks for one phase. Everything is measured from positions and the map; the only thing taken from
        /// the pathing layer is whether a still unit waits in an orderly way (ZPathing.IsQueueWaiting), and that is accepted
        /// only while the test itself sees traffic flowing next to it (another unit with an order moved 2+ px within 12 tiles
        /// in the last 3 s), so a gridlock the layer calls "orderly" still counts as stuck.</summary>
        sealed class Watch
        {
            public const double FlowRadius = 12 * 16;
            readonly ZWorld w; readonly List<ZObject> units;
            readonly Dictionary<int, (double x, double y, double t)> anchor = new Dictionary<int, (double, double, double)>();
            readonly Dictionary<int, (double x, double y)> flowAnchor = new Dictionary<int, (double, double)>();
            readonly Dictionary<int, double> lastMoved = new Dictionary<int, double>();
            readonly Dictionary<int, double> waitStart = new Dictionary<int, double>();
            readonly Dictionary<long, double> pairStart = new Dictionary<long, double>();
            readonly HashSet<int> stuck = new HashSet<int>(); readonly HashSet<long> overlapping = new HashSet<long>();
            readonly HashSet<int> inWall = new HashSet<int>();
            public double MaxStuck, MaxOverlap, MaxWait; public int StuckUnits, OverlapPairs, WetTicks, WallTicks;
            public int WallUnits => inWall.Count;
            public string StuckWho = "-", OverlapWho = "-", WetWho = "-", WallWho = "-";

            public Watch(ZWorld w, List<ZObject> units) { this.w = w; this.units = units; }

            /// <summary>Another unit with an order within FlowRadius moved 2+ px in the last StuckLimit seconds.</summary>
            bool Flowing(ZObject u, List<ZObject> alive, double now)
            {
                foreach (var v in alive)
                {
                    if (v == u || !lastMoved.TryGetValue(v.Id, out double t) || now - t > StuckLimit) continue;
                    double dx = v.PosX + v.WidthPix * 0.5 - u.PosX - u.WidthPix * 0.5, dy = v.PosY + v.HeightPix * 0.5 - u.PosY - u.HeightPix * 0.5;
                    if (dx * dx + dy * dy <= FlowRadius * FlowRadius) return true;
                }
                return false;
            }

            /// <summary>Zod's scripted building moves (crane / repair / fort entry, factory exit) drive through walls by design.</summary>
            static bool ScriptedMove(ZObject u) => u.Waypoints.Count > 0 && (u.Waypoints[0].mode == WpMode.CraneRepair || u.Waypoints[0].mode == WpMode.UnitRepair ||
                                                                              u.Waypoints[0].mode == WpMode.EnterFort || u.Waypoints[0].mode == WpMode.ForceMove);

            /// <summary>The unit's box, shrunk by 2 px, lies on a tile its own grid blocks (buildings, rock walls, bridge rails, cliffs,
            /// water for vehicles). Zod's own move test shrinks by 1 px (zpath_finding.cpp:629-698), so this allows 1 px more.</summary>
            public static bool OnBlockedTile(ZWorld w, ZObject u)
            {
                int x0 = u.LocX + 2, y0 = u.LocY + 2, x1 = u.LocX + u.WidthPix - 3, y1 = u.LocY + u.HeightPix - 3;
                for (int ty = y0 >> 4; ty <= y1 >> 4; ty++)
                    for (int tx = x0 >> 4; tx <= x1 >> 4; tx++)
                        if (!w.Path.TilePassable(tx, ty, u.IsRobot) && !(w.Pathing != null && w.Pathing.SoftPassTile(u, tx, ty))) return true;   // pathing v3 DV47: a soft pass over debris
                return false;
            }

            public void Tick()
            {
                double now = w.Time;
                var alive = new List<ZObject>();
                foreach (var u in units) if (w.Get(u.Id) == u && !u.Destroyed) alive.Add(u);
                foreach (var u in alive)
                {                                                                          // traffic flow, measured here (not by the layer)
                    double x = u.PosX, y = u.PosY;
                    if (!flowAnchor.TryGetValue(u.Id, out var f) || u.Waypoints.Count == 0) { flowAnchor[u.Id] = (x, y); continue; }   // idle jostling is not flow
                    if (Math.Abs(x - f.x) + Math.Abs(y - f.y) >= 2) { flowAnchor[u.Id] = (x, y); lastMoved[u.Id] = now; }
                }
                foreach (var u in alive)
                {
                    double x = u.PosX, y = u.PosY;
                    if (u.Waypoints.Count == 0 || !anchor.TryGetValue(u.Id, out var a)) { anchor[u.Id] = (x, y, now); waitStart.Remove(u.Id); continue; }
                    if (Math.Abs(x - a.x) + Math.Abs(y - a.y) >= 2) { anchor[u.Id] = (x, y, now); waitStart.Remove(u.Id); continue; }
                    if (w.Pathing.Enabled && w.Pathing.IsQueueWaiting(u) && Flowing(u, alive, now))
                    {
                        anchor[u.Id] = (a.x, a.y, now);
                        if (!waitStart.TryGetValue(u.Id, out double ws)) waitStart[u.Id] = ws = now;
                        if (now - ws > MaxWait) MaxWait = now - ws;
                        continue;
                    }
                    double sd = now - a.t;
                    if (sd > MaxStuck)
                    {
                        MaxStuck = sd;
                        if (sd > StuckLimit)
                        {
                            StuckWho = Who(u) + " chain " + w.Pathing.DescribeChain(u);
                            if (u.PathState.Log != null) StuckWho += " log: " + string.Join(" / ", u.PathState.Log.Skip(Math.Max(0, u.PathState.Log.Count - 4)));
                        }
                    }
                    if (sd > StuckLimit && stuck.Add(u.Id)) StuckUnits++;
                }
                for (int i = 0; i < alive.Count; i++)
                    for (int j = i + 1; j < alive.Count; j++)
                    {
                        var a = alive[i]; var b = alive[j];
                        long key = ((long)Math.Min(a.Id, b.Id) << 32) | (uint)Math.Max(a.Id, b.Id);
                        double min = ZPathing.MinSpacing(a, b) - OverlapTolerance;
                        double dx = a.PosX + a.WidthPix * 0.5 - b.PosX - b.WidthPix * 0.5, dy = a.PosY + a.HeightPix * 0.5 - b.PosY - b.HeightPix * 0.5;
                        if (dx * dx + dy * dy >= min * min || ZPathing.PairExempt(a, b)) { pairStart.Remove(key); continue; }
                        if (!pairStart.TryGetValue(key, out double s0)) { pairStart[key] = now; continue; }
                        double od = now - s0;
                        if (od > MaxOverlap) { MaxOverlap = od; OverlapWho = $"{Who(a)} / {Who(b)} {Math.Sqrt(dx * dx + dy * dy):F1} px"; }
                        if (od > OverlapLimit && overlapping.Add(key)) OverlapPairs++;
                    }
                foreach (var v in alive)
                {
                    if (v.Kind != ZKind.Vehicle) continue;
                    for (int dx = 0; dx < 2; dx++)
                        for (int dy = 0; dy < 2; dy++)
                        {
                            var cls = w.Map.ClassAt((v.LocX + 1 + dx * 29) >> 4, (v.LocY + 1 + dy * 29) >> 4);
                            if (cls == ZClass.Water || cls == ZClass.DeepWater) { WetTicks++; WetWho = Who(v); goto next; }
                        }
                    next:;
                }
                foreach (var u in alive)
                    if (!ScriptedMove(u) && OnBlockedTile(w, u)) { WallTicks++; inWall.Add(u.Id); WallWho = Who(u); }
            }

            static string Who(ZObject u) => $"{u.Owner} {u.Type}#{u.Id}{(u.IsMinion ? "(m)" : "")} ({u.CenterX >> 4},{u.CenterY >> 4}) {u.PathWait}";
        }
    }

    public static partial class OpenZParity
    {
        /// <summary>The parity suite's movement rows (speed on ground / road / water for every mover, running, squads, vehicles vs
        /// water), run again by the pathing stress test: the pathing layer must not change any of them.</summary>
        public static List<(string test, string expected, string measured, bool pass, string note)> MovementParityRows()
        {
            var rows = new List<Row>();
            MovementTests(rows); RunTest(rows); SquadTests(rows); VehicleWaterTest(rows);
            return rows.Select(r => (r.test, r.expected, r.measured, r.pass, r.note)).ToList();
        }
    }

#if UNITY_EDITOR
    /// <summary>Test Runner (EditMode) entry points, like SnipeParityTests: the fast detector self-tests run with "Run All"; the
    /// full stress test (both maps, about 15 s headless, longer in the editor) is Explicit: run it by hand in the Test Runner
    /// </summary>
    [NUnit.Framework.TestFixture]
    public class ZPathingStressTestRunner
    {
        [NUnit.Framework.Test]
        public void DetectorsCatchStuckOverlapWaterAndWalls()
        {
            var rows = new List<ZPathingStressTests.Row>();
            ZPathingStressTests.DetectorSelfTests(rows);
            foreach (var r in rows) NUnit.Framework.Assert.IsTrue(r.pass, $"{r.test}: expected {r.expected}, measured {r.measured}");
        }

        [NUnit.Framework.Test, NUnit.Framework.Explicit("Long: both maps, all scenarios; same as the menu OpenZ/Run Pathing Stress Test")]
        public void StressBothMaps()
        {
            var res = ZPathingStressTests.Run();
            NUnit.Framework.Assert.AreEqual(res.total, res.pass, res.summary);
        }
    }
#endif
}
