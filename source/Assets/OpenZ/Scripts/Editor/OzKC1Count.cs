using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// The counted check of the movement kit (KC1): the run into the enemy fort (DV102), walking round rocks (DV103) and the option
    /// FRIENDLY UNITS OVERLAP, each before / after on the game clock, outside Play (the rules only). Menu OpenZ/V1.1/Count Movement Kit (KC1).
    /// The same code runs headless (the kit's runner), so every number of the kit's SPEC can be made again in the editor.
    /// Uses public members of the rules only; a switch the project does not have is left alone (the line then says n/a).
    /// </summary>
    public static class OzKC1Count
    {
        public static string Settings, MapsDir;                                          // headless: the settings text and the folder of the *_grid.json maps
#if UNITY_EDITOR
        static string SettingsText => Settings ?? OzV11Kit.SettingsText;
        static string MapText(string level) => MapsDir != null ? System.IO.File.ReadAllText(System.IO.Path.Combine(MapsDir, level + "_grid.json")) : OzV11Kit.MapText(level);
#else
        static string SettingsText => Settings;
        static string MapText(string level) => System.IO.File.ReadAllText(System.IO.Path.Combine(MapsDir, level + "_grid.json"));
#endif
        static string F(double v, string f = "0.0") => double.IsNaN(v) || double.IsInfinity(v) ? "never" : v.ToString(f, CultureInfo.InvariantCulture);
        public static readonly string[] Levels = { "L02", "L16", "L17" };
        static readonly string[] LevelNames = { "Desert", "Jungle", "Car Park" };
        static string Name(string lv) { int i = Array.IndexOf(Levels, lv); return i >= 0 ? LevelNames[i] : lv; }

        // ------------------------------------------------------------------ the heading meter (as the swerve kit's: chords of 3 px,
        // a turn = 15 degrees or more between two chords, a reversal = a turn to one side and within 2 s one to the other side)
        public static Action<ZObject, double, double> OnReversal;                        // headless tracing only (the body, the time, px from the door)
        sealed class Meter
        {
            readonly ZObject u; double ax, ay, hx, hy, lastT = -100; bool hasH; int lastSign;
            public int Reversals, ReversalsNear, Turns; public double Path;
            public readonly int[] Band = new int[4];                                      // reversals by distance from the door: 0-6, 6-12, 12-16, over 16 tiles
            public double D = -1;                                                         // the distance (px) the last Step was told (-1 = none)
            double px, py;
            public Meter(ZObject o) { u = o; px = ax = o.PosX; py = ay = o.PosY; }
            public void Step(double t, bool near, double door = -1)
            {
                D = door;
                if (u.Destroyed) return;
                double dx = u.PosX - px, dy = u.PosY - py; Path += Math.Sqrt(dx * dx + dy * dy); px = u.PosX; py = u.PosY;
                double cx = u.PosX - ax, cy = u.PosY - ay, cl = Math.Sqrt(cx * cx + cy * cy);
                if (cl < 3) return;
                cx /= cl; cy /= cl; ax = u.PosX; ay = u.PosY;
                if (hasH)
                {
                    double d = Math.Atan2(hx * cy - hy * cx, hx * cx + hy * cy) * 180 / Math.PI;
                    if (Math.Abs(d) >= 15)
                    {
                        int sg = d > 0 ? 1 : -1; Turns++;
                        if (lastSign != 0 && sg != lastSign && t - lastT <= 2)
                        {
                            Reversals++; if (near) ReversalsNear++;
                            if (D >= 0) Band[D <= 96 ? 0 : D <= 192 ? 1 : D <= 256 ? 2 : 3]++;
                            OnReversal?.Invoke(u, t, D);
                        }
                        lastSign = sg; lastT = t;
                    }
                }
                hx = cx; hy = cy; hasH = true;
            }
        }

        /// <summary>The level without its flags and grenade crates (movement measures: a unit beside a flag takes it, as the rules allow).</summary>
        static string NoItems(string mapJson)
        {
            var m = ZMapData.FromJson(mapJson);
            m.objects.RemoveAll(o => o.kind == "map_item:flag" || o.kind == "map_item:grenades");
            return UnityEngine.JsonUtility.ToJson(m);
        }

        static ZWorld NewWorld(string level, int seed, bool noItems = false)
        {
            var w = ZWorld.Create(SettingsText, noItems ? NoItems(MapText(level)) : MapText(level), seed, level == "L17");
            w.Bots.Add(new ZBot(w, Team.Blue, seed + 101) { Ignored = true });
            w.Events.Clear();
            return w;
        }

        static ZObject Dummy(ZWorld w, int x, int y, Team t)
        {
            var o = w.CreateRobotGroup("grunt", x, y, t, 1);
            o.MaxHealth = o.Health = 1000000000; o.Damage = 0; o.MoveSpeed = 0; o.RealMoveSpeed = 0;
            return o;
        }

        static bool Spot(ZWorld w, int tx, int ty, out int x, out int y)
        {
            x = y = 0;
            for (int r = 0; r <= 6; r++)
                for (int j = ty - r; j <= ty + r; j++)
                    for (int i = tx - r; i <= tx + r; i++)
                    {
                        if (Math.Max(Math.Abs(i - tx), Math.Abs(j - ty)) != r) continue;
                        if (i < 1 || j < 1 || i >= w.Map.width - 1 || j >= w.Map.height - 1) continue;
                        if (w.Pathing == null || w.Pathing.Reach.Comp(i, j, true, false) < 0) continue;
                        var c = w.Map.ClassAt(i, j); if (c == ZClass.Water || c == ZClass.DeepWater) continue;
                        x = i * 16; y = j * 16; return true;
                    }
            return false;
        }

        // ================================================================== C1: the run into the enemy fort (DV102)
        public struct FortRun { public double Fall, HeldNear, DodgesNear; public int Bodies, AtDoor, Alive, RevNear, Rev; public bool Fell; public int[] Bands; }

        /// <summary>Four red robot squads (two grunt squads, a psycho and a pyro squad: the kinds that step aside) stand FortStart tiles (12) in
        /// front of the BLUE fort's door; the blue fort keeps its own guns and defenders, and a blue Tough squad and a blue light tank stand
        /// beside the door and fire (under fire). One player order: every squad into the fort through its door. Measured until the fort
        /// falls (the first body inside: the fort dies with it) or 120 s: the time, and near the door (within 6 tiles of its exit point)
        /// the heading reversals, the steps aside begun and the time held / queued / side-stepping by the spacing layer, per body.</summary>
        public static string Debug = ""; public static Action<string> Trace;
        public static bool FortTank = false, FortTough = true;
        public static Action<ZWorld, List<ZObject>, double, int, int> OnTick;            // headless tracing only
        public static Action<ZWorld, double, int, int> OnEvents;                        // headless tracing only (the tick's events, before they are cleared)
        public static int FortStart = 12;
        public static string[] FortSquads = { "grunt", "psycho", "grunt", "pyro", "grunt", "psycho", "laser", "pyro" };                                                 // tiles in front of the door
        public static FortRun RunFort(string level, int seed, bool? fortRun)
        {
            var r = new FortRun { Fall = double.PositiveInfinity };
            using (OzV11Kit.GameSwitches())
            {
                OzV11Kit.TrySet(typeof(ZWorld), "CalmMoveDeviation", true);                // the game as it will be: calm movement on
                if (fortRun.HasValue) OzV11Kit.TrySet(typeof(ZWorld), "FortRunDeviation", fortRun.Value);
                var w = NewWorld(level, seed);
                var fort = w.Objects.FirstOrDefault(o => o.IsFort && o.Owner == Team.Blue);
                var home = w.Objects.FirstOrDefault(o => o.IsFort && o.Owner == Team.Red);
                if (fort == null || home == null || !fort.GetCreationMovePoint(out int ex, out int ey)) return r;
                var keep = new List<ZObject> { Dummy(w, 16, 16, Team.Red), Dummy(w, (w.Map.width - 2) * 16, (w.Map.height - 2) * 16, Team.Blue) };   // with them the game is never over
                foreach (var o in w.Objects.Where(o => o.IsUnit && !keep.Contains(o)).ToList())
                {
                    if (o.Kind == ZKind.Cannon) { o.Owner = Team.Neutral; o.Drivers.Clear(); o.Waypoints.Clear(); o.Disengage(); o.Damage = 0; continue; }   // the towers' guns silent: the fire comes from the units below
                    o.SetHealth(0); w.UpdateObjectHealth(o);
                }
                foreach (var b in w.Objects) if (b.IsProducer) b.StopBuildingProduction();
                w.Tick(ZConst.TICK); w.Events.Clear();
                double ox = ex - fort.CenterX, oy = ey - fort.CenterY, ol = Math.Sqrt(ox * ox + oy * oy); if (ol < 1) { ox = 0; oy = 1; ol = 1; }
                ox /= ol; oy /= ol;                                                         // out of the door
                double sx = -oy, sy = ox;                                                   // along the wall
                var squads = new List<ZObject>();
                string[] types = FortSquads;
                for (int i = 0; i < types.Length; i++)
                {
                    double lat = (i - (types.Length - 1) * 0.5) * 2.5;
                    if (!Spot(w, (int)((ex + ox * FortStart * 16 + sx * lat * 16) / 16), (int)((ey + oy * FortStart * 16 + sy * lat * 16) / 16), out int x, out int y)) continue;
                    squads.Add(w.CreateRobotGroup(types[i], x, y, Team.Red));
                }
                if (Spot(w, (int)((ex + ox * 3 * 16 + sx * 4 * 16) / 16), (int)((ey + oy * 3 * 16 + sy * 4 * 16) / 16), out int tx, out int ty) && FortTough) w.CreateRobotGroup("tough", tx, ty, Team.Blue);
                if (Spot(w, (int)((ex + ox * 3 * 16 - sx * 5 * 16) / 16), (int)((ey + oy * 3 * 16 - sy * 5 * 16) / 16), out int vx, out int vy) && FortTank) w.CreateObject("vehicle", "light", vx - 8, vy - 8, Team.Blue);
                for (int k = 0; k < 25; k++) { w.Tick(ZConst.TICK); w.Events.Clear(); }
                var bodies = new List<ZObject>(); foreach (var l in squads) { bodies.Add(l); bodies.AddRange(l.Minions); }
                var meters = bodies.ToDictionary(b => b, b => new Meter(b));
                var lastMode = bodies.ToDictionary(b => b, b => WpMode.None);
                var wp = new ZWaypoint { mode = WpMode.EnterFort, refId = fort.Id, x = fort.CenterX, y = fort.CenterY };
                int sent = 0; foreach (var l in squads) if (w.SendWaypoints(Team.Red, l, new[] { wp })) sent++;
                Debug = "sent " + sent + "/" + squads.Count + ", game over " + w.GameOver + ", door (" + ex / 16 + "," + ey / 16 + "), fort " + fort.Type + " at (" + fort.CenterX / 16 + "," + fort.CenterY / 16 + ")";
                double t0 = w.Time; int held = 0, dodges = 0;
                while (w.Time - t0 < 120)
                {
                    w.Tick(ZConst.TICK);
                    bool fell = false;
                    foreach (var e in w.Events) if (e.type == ZEventType.FortEntered && e.otherId == fort.Id) fell = true;
                    OnEvents?.Invoke(w, w.Time - t0, ex, ey);
                    w.Events.Clear();
                    foreach (var b in bodies)
                    {
                        if (b.Destroyed || b.KillAt >= 0) continue;
                        double d = Math.Sqrt((double)(b.CenterX - ex) * (b.CenterX - ex) + (double)(b.CenterY - ey) * (b.CenterY - ey));
                        bool near = d <= 96;
                        meters[b].Step(w.Time, near, d);
                        var m = b.Waypoints.Count > 0 ? b.Waypoints[0].mode : WpMode.None;
                        if (near && m == WpMode.Dodge && lastMode[b] != WpMode.Dodge) dodges++;
                        lastMode[b] = m;
                        if (near && (b.PathWait == ZPathWait.Held || b.PathWait == ZPathWait.Queue || b.PathWait == ZPathWait.Sidestep || b.PathWait == ZPathWait.Lane)) held++;
                    }
                    OnTick?.Invoke(w, bodies, w.Time - t0, ex, ey);
                    if (Trace != null && ((int)Math.Round((w.Time - t0) / ZConst.TICK)) % 50 == 0)
                    {
                        var live = bodies.Where(b => !b.Destroyed && b.KillAt < 0).ToList();
                        Trace("t " + F(w.Time - t0) + " alive " + live.Count + " nearest " + (live.Count > 0 ? F(live.Min(b => Math.Sqrt((double)(b.CenterX - ex) * (b.CenterX - ex) + (double)(b.CenterY - ey) * (b.CenterY - ey))) / 16) : "-") + " tiles; modes " +
                              string.Join(",", live.Select(b => b.Waypoints.Count > 0 ? b.Waypoints[0].mode.ToString()[0].ToString() + (b.FortRushOn ? "!" : "") : "-")));
                    }
                    if (fell || fort.Destroyed) { r.Fell = true; r.Fall = w.Time - t0; break; }
                }
                r.Bodies = bodies.Count;
                foreach (var b in bodies)
                {
                    if (b.Destroyed || b.KillAt >= 0) continue;
                    r.Alive++;
                    if (Math.Abs(b.CenterX - ex) + Math.Abs(b.CenterY - ey) <= 48) r.AtDoor++;
                }
                r.RevNear = meters.Values.Sum(m => m.ReversalsNear); r.Rev = meters.Values.Sum(m => m.Reversals);
                r.Bands = new int[4]; foreach (var m in meters.Values) for (int i = 0; i < 4; i++) r.Bands[i] += m.Band[i];
                r.HeldNear = held * ZConst.TICK / Math.Max(1, bodies.Count); r.DodgesNear = dodges;
            }
            return r;
        }

        /// <summary>The fort run summed over seeds 1..n: the fall count, the mean time to the fall (a run without a fall within 120 s
        /// counts 120 s), the mean of the runs that fell, the reversals near the door and in the whole run, by distance band.</summary>
        public struct FortSum { public int Fell, Runs, RevNear, Rev, Bodies, Alive; public double Capped, MeanFell, Worst; public int[] Bands; }
        public static FortSum SumFort(string level, int seeds, bool on)
        {
            var r = new FortSum { Bands = new int[4] }; double sum = 0, fellSum = 0;
            for (int s = 1; s <= seeds; s++)
            {
                var f = RunFort(level, s, on);
                r.Runs++; sum += f.Fell ? f.Fall : 120; r.Worst = Math.Max(r.Worst, f.Fell ? f.Fall : double.PositiveInfinity);
                if (f.Fell) { r.Fell++; fellSum += f.Fall; }
                r.RevNear += f.RevNear; r.Rev += f.Rev; r.Bodies += f.Bodies; r.Alive += f.Alive;
                if (f.Bands != null) for (int i = 0; i < 4; i++) r.Bands[i] += f.Bands[i];
            }
            r.Capped = sum / Math.Max(1, r.Runs); r.MeanFell = r.Fell > 0 ? fellSum / r.Fell : double.NaN;
            return r;
        }

        /// <summary>The C1 verdict of one level and fire setting: the time to the fall with the rule no longer than without it, no heading
        /// reversal within 6 tiles of the door, and no more reversals in the whole run than without it.</summary>
        public static bool FortPass(FortSum off, FortSum on) => on.Capped <= off.Capped + 0.05 && on.RevNear == 0 && on.Rev <= off.Rev;

        public static string FortTable(int seeds, bool withRuleOnly = false)
        {
            var sb = new StringBuilder();
            sb.AppendLine("C1 the run into the enemy fort: " + FortSquads.Length + " red squads (" + string.Join(", ", FortSquads) + ") " + FortStart + " tiles before the blue fort's door; one player order: into the fort through its door. " + seeds + " seeds per level.");
            sb.AppendLine("  under fire = the fort gun, its roof defender and a blue Tough squad beside the door" + (FortTank ? " and a blue light tank" : "") + "; quiet = no blue unit by the door (the fort gun and its roof defender fire in both; the tower guns are silent in both). near = within 6 tiles of the door's exit point.");
            sb.AppendLine("  time to the fall: the mean of the runs that fell; capped = the mean over all runs with no fall within 120 s counted as 120 s (the verdict's measure). bands = reversals 0-6 / 6-12 / 12-16 / over 16 tiles from the door (straight distance).");
            sb.AppendLine("level     fire   rule | fort fell  mean s  capped s  worst s | reversals near the door (per body) | reversals in the whole run (per body) | bands | bodies alive at the fall");
            bool tough = FortTough;
            var verdicts = new List<string>();
            try
            {
                foreach (var lv in Levels)
                    foreach (var fire in new[] { false, true })
                    {
                        FortTough = fire;
                        FortSum off = default;
                        foreach (var on in withRuleOnly ? new[] { true } : new[] { false, true })
                        {
                            var r = SumFort(lv, seeds, on);
                            if (!on) off = r;
                            sb.AppendLine(Name(lv).PadRight(9) + (fire ? " fire " : " quiet") + (on ? "  ON " : "  off") + " | " + (r.Fell + "/" + seeds).PadLeft(5) + "  " + F(r.MeanFell).PadLeft(6) + "  " + F(r.Capped).PadLeft(8) + "  " + F(r.Worst).PadLeft(6) +
                                          " | " + r.RevNear + " (" + F(r.Bodies > 0 ? (double)r.RevNear / r.Bodies : 0, "0.00") + ") | " + r.Rev + " (" + F(r.Bodies > 0 ? (double)r.Rev / r.Bodies : 0, "0.00") + ") | " + string.Join("/", r.Bands) + " | " + r.Alive + " of " + r.Bodies);
                            if (on && !withRuleOnly)
                                verdicts.Add(OzV11Kit.Verdict(FortPass(off, r), Name(lv) + (fire ? " under fire" : " quiet") + ": time to the fall " + F(off.Capped) + " -> " + F(r.Capped) + " s (capped), fell " + off.Fell + " -> " + r.Fell + " of " + seeds +
                                                                                   ", reversals near the door " + off.RevNear + " -> " + r.RevNear + ", in the whole run " + off.Rev + " -> " + r.Rev));
                        }
                    }
            }
            finally { FortTough = tough; }
            foreach (var v in verdicts) sb.AppendLine(v);
            return sb.ToString();
        }

        // ================================================================== C9: round the rock, or blast it (DV103)
        /// <summary>Points on the level with a destroyable rock between them for a unit that carries explosives: drawn with a fixed seed
        /// among open tiles 6 to 24 tiles apart; a pair counts when the way through crosses a rock (Zod would blast it).</summary>
        public static List<(int sx, int sy, int ex, int ey, double through, double round)> RockCases(string level, bool robot, int want, int seed)
        {
            var list = new List<(int, int, int, int, double, double)>();
            using (OzV11Kit.GameSwitches())
            {
                var w = NewWorld(level, 1, true);
                var rng = new Random(seed);
                int tries = 0;
                while (list.Count < want && tries++ < want * 4000)
                {
                    int ax = rng.Next(2, w.Map.width - 3), ay = rng.Next(2, w.Map.height - 3);
                    double ang = rng.NextDouble() * Math.PI * 2, d = 6 + rng.NextDouble() * 18;
                    int bx = (int)(ax + Math.Cos(ang) * d), by = (int)(ay + Math.Sin(ang) * d);
                    if (bx < 2 || by < 2 || bx >= w.Map.width - 3 || by >= w.Map.height - 3) continue;
                    if (!w.Path.TilePassable(ax, ay, robot) || !w.Path.TilePassable(bx, by, robot)) continue;
                    if (!robot && (!w.Path.TilePassable(ax + 1, ay + 1, false) || !w.Path.TilePassable(bx + 1, by + 1, false))) continue;
                    var c1 = w.Map.ClassAt(ax, ay); var c2 = w.Map.ClassAt(bx, by);
                    if (c1 == ZClass.Water || c1 == ZClass.DeepWater || c2 == ZClass.Water || c2 == ZClass.DeepWater) continue;
                    int sx = ax * 16 + 8, sy = ay * 16 + 8, ex = bx * 16 + 8, ey = by * 16 + 8;
                    if (!w.Pathing.RockMeasure(robot, sx, sy, ex, ey, out double th, out double rd)) continue;
                    list.Add((sx, sy, ex, ey, th, rd));
                }
            }
            return list;
        }

        public static Action<ZWorld, ZObject, double> RockTrace;                         // headless tracing only
        public struct RockRun { public double Arrive; public int Blasts; public bool Arrived; }

        /// <summary>One unit (a single Tough or a light tank) of the red team alone on the level (no other unit, no flag or crate, the forts'
        /// guns and roof defenders silent: movement only), one player move order from a case's start to its goal: the time until it stands within 1.5
        /// tiles of the goal with its order done (max 90 s), and the rocks destroyed.</summary>
        public static RockRun RunRock(string level, string type, (int sx, int sy, int ex, int ey, double through, double round) c, bool? rule, bool layer = true)
        {
            var r = new RockRun { Arrive = double.PositiveInfinity };
            bool was = ZPathing.DefaultEnabled;
            try
            {
                ZPathing.DefaultEnabled = layer;
                using (OzV11Kit.GameSwitches())
                {
                    if (rule.HasValue) OzV11Kit.TrySet(typeof(ZWorld), "RockRoundDeviation", rule.Value);
                    ZWorld.FortGunDeviation = false; ZWorld.FortDefenderDeviation = false;      // movement only: no fort fires at the unit (GameSwitches puts them back)
                    var w = NewWorld(level, 1, true);                                         // and no flag or crate beside its way
                    var keep = new List<ZObject> { Dummy(w, 16, 16, Team.Red), Dummy(w, (w.Map.width - 2) * 16, (w.Map.height - 2) * 16, Team.Blue) };
                    foreach (var o in w.Objects.Where(o => o.IsUnit && !keep.Contains(o)).ToList()) { o.SetHealth(0); w.UpdateObjectHealth(o); }
                    foreach (var b in w.Objects) if (b.IsProducer) b.StopBuildingProduction();
                    w.Tick(ZConst.TICK); w.Events.Clear();
                    int rocks0 = w.Objects.Count(o => o.Type == "rock" && !o.Destroyed);
                    var u = ZBuildLists.IsRobot(type) ? w.CreateRobotGroup(type, c.sx - 8, c.sy - 8, Team.Red, 1) : w.CreateObject("vehicle", type, c.sx - 8, c.sy - 8, Team.Red);
                    for (int k = 0; k < 5; k++) { w.Tick(ZConst.TICK); w.Events.Clear(); }
                    w.SendWaypoints(Team.Red, u, new[] { ZWaypoint.Move(c.ex, c.ey, false) });
                    double t0 = w.Time;
                    while (w.Time - t0 < 90)
                    {
                        w.Tick(ZConst.TICK); w.Events.Clear();
                        RockTrace?.Invoke(w, u, w.Time - t0);
                        if (u.Destroyed) break;
                        if (Math.Abs(u.CenterX - c.ex) <= 24 && Math.Abs(u.CenterY - c.ey) <= 24 && u.Waypoints.Count == 0) { r.Arrived = true; r.Arrive = w.Time - t0; break; }
                    }
                    r.Blasts = rocks0 - w.Objects.Count(o => o.Type == "rock" && !o.Destroyed);
                }
            }
            finally { ZPathing.DefaultEnabled = was; }
            return r;
        }

        public static string RockTable(int casesPerLevel, string types = "tough,light", bool engineToo = true)
        {
            var sb = new StringBuilder();
            var inv = CultureInfo.InvariantCulture;
            sb.AppendLine("C9 rocks: pairs of open points 6 to 24 tiles apart with a destroyable rock on the way a unit with explosives plans (Zod would blast it); up to " + casesPerLevel + " pairs per level and kind.");
            sb.AppendLine("the rule: the way round is taken when it is at most " + ZPathing.RockRatio.ToString(inv) + " times as long AND at most " + ZPathing.RockExtra.ToString(inv) + " px (" + (ZPathing.RockExtra / 16).ToString("0.#", inv) + " tiles) longer");
            foreach (var type in types.Split(','))
            {
                bool robot = ZBuildLists.IsRobot(type);
                foreach (var lv in Levels)
                {
                    var cases = RockCases(lv, robot, casesPerLevel, 7);
                    if (cases.Count == 0) { sb.AppendLine(Name(lv) + " " + type + ": no rock on any sampled way"); continue; }
                    int none = cases.Count(c => c.round < 0);
                    var wr = cases.Where(c => c.round >= 0).ToList();
                    string P(Func<(int sx, int sy, int ex, int ey, double through, double round), bool> f) => wr.Count(f).ToString();
                    sb.AppendLine(Name(lv) + " " + type + ": " + cases.Count + " pairs; no way round " + none + "; way round / way through: <=1.1 " + P(c => c.round <= c.through * 1.1) + ", <=1.25 " + P(c => c.round <= c.through * 1.25) +
                                  ", <=1.5 " + P(c => c.round <= c.through * 1.5) + ", <=2 " + P(c => c.round <= c.through * 2) + ", more " + P(c => c.round > c.through * 2) +
                                  "; extra: <=4 tiles " + P(c => c.round - c.through <= 64) + ", <=8 " + P(c => c.round - c.through <= 128) + ", <=12 " + P(c => c.round - c.through <= 192) + ", more " + P(c => c.round - c.through > 192));
                    int n = 0, arrOff = 0, arrOn = 0, blOff = 0, blOn = 0, blEng = 0, slower = 0, faster = 0, roundTaken = 0; double tOff = 0, tOn = 0, worstLoss = 0, tTakenOff = 0, tTakenOn = 0; int nTaken = 0;
                    var bands = new double[5, 3];                                            // per band of way round / way through: cases, seconds today, seconds with the rule
                    foreach (var c in cases)
                    {
                        var off = RunRock(lv, type, c, false);
                        var on = RunRock(lv, type, c, true);
                        if (engineToo) blEng += RunRock(lv, type, c, false, false).Blasts;
                        n++; blOff += off.Blasts; blOn += on.Blasts;
                        if (off.Arrived) arrOff++;
                        if (on.Arrived) arrOn++;
                        bool take = c.round >= 0 && c.round <= c.through * ZPathing.RockRatio && c.round - c.through <= ZPathing.RockExtra;
                        if (take) roundTaken++;
                        if (off.Arrived && on.Arrived)
                        {
                            tOff += off.Arrive; tOn += on.Arrive;
                            if (on.Arrive > off.Arrive + 0.5) slower++;
                            if (on.Arrive < off.Arrive - 0.5) faster++;
                            worstLoss = Math.Max(worstLoss, on.Arrive - off.Arrive);
                            if (take) { tTakenOff += off.Arrive; tTakenOn += on.Arrive; nTaken++; }
                            double q = c.round < 0 ? 99 : c.round / c.through; int bi = q <= 1.1 ? 0 : q <= 1.25 ? 1 : q <= 1.5 ? 2 : q <= 2 ? 3 : 4;
                            bands[bi, 0]++; bands[bi, 1] += off.Arrive; bands[bi, 2] += on.Arrive;
                        }
                    }
                    sb.AppendLine("   runs (one " + type + ", one move order): rocks blasted: the engine port (no spacing layer) " + (engineToo ? blEng.ToString() : "-") + ", the game today " + blOff + ", with the rule " + blOn +
                                  " | way round chosen in " + roundTaken + " of " + n + " | arrived " + arrOff + " -> " + arrOn + " | time to the goal, both arrived: " + F(tOff, "0") + " s -> " + F(tOn, "0") + " s (" + faster + " sooner, " + slower + " later by over 0.5 s, worst +" + F(worstLoss) + " s)" +
                                  " | the cases where it walks round: " + F(tTakenOff, "0") + " s -> " + F(tTakenOn, "0") + " s over " + nTaken);
                    string[] bn = { "<=1.1", "1.1-1.25", "1.25-1.5", "1.5-2", ">2 or none" };
                    var bl = new List<string>();
                    for (int b = 0; b < 5; b++) if (bands[b, 0] > 0) bl.Add(bn[b] + ": " + bands[b, 0] + " cases, mean " + F(bands[b, 1] / bands[b, 0]) + " -> " + F(bands[b, 2] / bands[b, 0]) + " s");
                    sb.AppendLine("   by way round / way through (both arrived): " + string.Join("; ", bl));
                }
            }
            return sb.ToString();
        }

        // ================================================================== soak: whole games, computer against computer, every rule of the kit on
        /// <summary>A whole match on the level, both sides played by the computer, every rule of the kit on (the overlap option as given),
        /// up to maxSeconds: exceptions (the pathing layer's fail-safe or a throw), unit samples on a tile closed to them (every 0.5 s).</summary>
        public static string Soak(string level, int seed, bool overlap, double maxSeconds)
        {
            using (OzV11Kit.GameSwitches())
            {
                foreach (var s in new[] { "CalmMoveDeviation", "FortRunDeviation", "RockRoundDeviation" }) OzV11Kit.TrySet(typeof(ZWorld), s, true);
                OzV11Kit.TrySet(typeof(ZWorld), "FriendsOverlap", overlap);
                var w = ZWorld.Create(SettingsText, MapText(level), seed, level == "L17");
                w.Bots.Clear();
                w.Bots.Add(new ZBot(w, Team.Blue, seed + 101)); w.Bots.Add(new ZBot(w, Team.Red, seed));
                int bad = 0, samples = 0, step = 0; string thrown = null; double t = 0;
                try
                {
                    while (t < maxSeconds && !w.GameOver)
                    {
                        w.Tick(ZConst.TICK); w.Events.Clear(); t += ZConst.TICK;
                        if (++step % 50 != 0) continue;
                        foreach (var o in w.Objects)
                        {
                            if (!o.IsUnit || o.Destroyed || o.KillAt >= 0 || o.Kind == ZKind.Cannon || o.IsFortDefender) continue;
                            samples++;
                            int tx = (o.LocX + 8) >> 4, ty = (o.LocY + 8) >> 4;
                            if (!w.Path.TilePassableGrid(tx, ty, o.IsRobot, true)) bad++;
                        }
                    }
                }
                catch (Exception e) { thrown = e.GetType().Name + ": " + e.Message; }
                string fs = w.Pathing != null ? w.Pathing.FailSafeError : null;
                return "SOAK " + Name(level) + " seed " + seed + " overlap " + (overlap ? "YES" : "NO ") + ": " + F(t, "0") + " s" + (w.GameOver ? ", the match ended, winner " + (w.Winner == Team.Neutral ? "none" : w.Winner.ToString()) : ", time limit") +
                       " | exceptions " + ((thrown != null ? 1 : 0) + (fs != null ? 1 : 0)) + (thrown != null ? " (" + thrown + ")" : "") + (fs != null ? " (fail-safe: " + fs.Split('\n')[0] + ")" : "") +
                       " | unit samples on a tile closed to them " + bad + " of " + samples;
            }
        }

        // ================================================================== C5: FRIENDLY UNITS OVERLAP (the option)
        public struct CrowdRun { public int Bodies, Arrived, Reversals, StoppedOnEach, EnemyClose, Samples, Closed; public double Last; }

        /// <summary>A crowd of one team (six robot squads, two light tanks, two jeeps) at one end of the level's first bridge (Desert: the
        /// two-deck bridge's west deck, Jungle: its bridge, Car Park: the long road), one group order to the far side; the calm movement
        /// rules on, the option as given. Until every body has ended its order or 240 s: heading reversals, bodies within 12 tiles of
        /// the goal, the last one's time, and at the end the stopped units of the team standing on each other (centres under 8 px).</summary>
        public static CrowdRun RunCrowd(string level, bool overlap, bool gate = false)
        {
            var r = new CrowdRun { Last = double.PositiveInfinity };
            using (OzV11Kit.GameSwitches())
            {
                OzV11Kit.TrySet(typeof(ZWorld), "CalmMoveDeviation", true);
                OzV11Kit.TrySet(typeof(ZWorld), "FriendsOverlap", overlap);
                var w = NewWorld(level, 1);
                var keep = new List<ZObject> { Dummy(w, 16, 16, Team.Red), Dummy(w, (w.Map.width - 2) * 16, (w.Map.height - 2) * 16, Team.Blue) };
                foreach (var o in w.Objects.Where(o => o.IsUnit && !keep.Contains(o)).ToList()) { o.SetHealth(0); w.UpdateObjectHealth(o); }
                foreach (var b in w.Objects) if (b.IsProducer) b.StopBuildingProduction();
                w.Tick(ZConst.TICK); w.Events.Clear();
                (int fx, int fy, int tx, int ty) = level == "L02" ? (22, 46, 36, 46) : level == "L16" ? (72, 62, 72, 86) : (20, 11, 60, 11);
                if (gate)                                                                   // across the front of the team's own fort gate (its exit lane), 9 tiles to each side
                {
                    var own = w.Objects.FirstOrDefault(o => o.IsFort && o.Owner == Team.Red);
                    if (own == null || !own.GetCreationMovePoint(out int gx, out int gy)) return r;
                    double ox = gx - own.CenterX, oy = gy - own.CenterY, ol = Math.Sqrt(ox * ox + oy * oy); if (ol < 1) { ox = 0; oy = 1; ol = 1; }
                    ox /= ol; oy /= ol;
                    fx = (int)((gx + ox * 32 - oy * 144) / 16); fy = (int)((gy + oy * 32 + ox * 144) / 16); tx = (int)((gx + ox * 32 + oy * 144) / 16); ty = (int)((gy + oy * 32 - ox * 144) / 16);
                    if (Spot(w, tx, ty, out int sx2, out int sy2)) { tx = sx2 / 16; ty = sy2 / 16; }
                }
                var leaders = new List<ZObject>();
                string[] kinds = { "grunt", "psycho", "pyro", "grunt", "tough", "laser", "light", "light", "jeep", "jeep" };
                for (int i = 0; i < kinds.Length; i++)
                {
                    if (!Spot(w, fx - 2 + (i % 4) * 2, fy - 3 + (i / 4) * 2, out int x, out int y)) continue;
                    leaders.Add(ZBuildLists.IsRobot(kinds[i]) ? w.CreateRobotGroup(kinds[i], x, y, Team.Red) : w.CreateObject("vehicle", kinds[i], x, y, Team.Red));
                }
                for (int k = 0; k < 25; k++) { w.Tick(ZConst.TICK); w.Events.Clear(); }
                var bodies = new List<ZObject>(); foreach (var l in leaders) { bodies.Add(l); bodies.AddRange(l.Minions); }
                var meters = bodies.ToDictionary(b => b, b => new Meter(b));
                foreach (var l in leaders) w.SendWaypoints(Team.Red, l, new[] { ZWaypoint.Move(tx * 16 + 8, ty * 16 + 8, false) });
                double t0 = w.Time;
                while (w.Time - t0 < 240)
                {
                    w.Tick(ZConst.TICK); w.Events.Clear();
                    foreach (var b in bodies) meters[b].Step(w.Time, false);
                    if (((int)Math.Round((w.Time - t0) / ZConst.TICK)) % 5 == 0)                // every 0.1 s: a body whose top-left tile is closed to it (a wall, deep water, a building, a rock)
                        foreach (var b in bodies)
                        {
                            if (b.Destroyed || b.KillAt >= 0) continue;
                            r.Samples++;
                            if (!w.Path.TilePassableGrid((b.LocX + 8) >> 4, (b.LocY + 8) >> 4, b.IsRobot, false)) r.Closed++;
                        }
                    if (w.Time - t0 > 2 && bodies.All(b => b.Destroyed || b.Waypoints.Count == 0)) { r.Last = w.Time - t0; break; }
                }
                r.Bodies = bodies.Count;
                r.Reversals = meters.Values.Sum(m => m.Reversals);
                foreach (var b in bodies) if (!b.Destroyed && Math.Abs(b.CenterX - (tx * 16 + 8)) <= 192 && Math.Abs(b.CenterY - (ty * 16 + 8)) <= 192) r.Arrived++;
                for (int i = 0; i < bodies.Count; i++)
                    for (int j = i + 1; j < bodies.Count; j++)
                    {
                        var a = bodies[i]; var b = bodies[j];
                        if (a.Destroyed || b.Destroyed || a.Waypoints.Count > 0 || b.Waypoints.Count > 0) continue;
                        if (Math.Abs(a.CenterX - b.CenterX) < 8 && Math.Abs(a.CenterY - b.CenterY) < 8) r.StoppedOnEach++;
                    }
            }
            return r;
        }

        /// <summary>Two enemy groups meet head-on on a road (Car Park's long road): with the option on, the spacing between ENEMIES stays:
        /// the least distance between a red and a blue body while both move (px), the option off and on.</summary>
        public static double EnemyGap(bool overlap)
        {
            using (OzV11Kit.GameSwitches())
            {
                OzV11Kit.TrySet(typeof(ZWorld), "CalmMoveDeviation", true);
                OzV11Kit.TrySet(typeof(ZWorld), "FriendsOverlap", overlap);
                var w = NewWorld("L17", 1);
                var keep = new List<ZObject> { Dummy(w, 16, 16, Team.Red), Dummy(w, (w.Map.width - 2) * 16, (w.Map.height - 2) * 16, Team.Blue) };
                foreach (var o in w.Objects.Where(o => o.IsUnit && !keep.Contains(o)).ToList()) { o.SetHealth(0); w.UpdateObjectHealth(o); }
                w.Tick(ZConst.TICK); w.Events.Clear();
                var red = new List<ZObject>(); var blue = new List<ZObject>();
                for (int i = 0; i < 3; i++)
                {
                    if (Spot(w, 22, 10 + i, out int x, out int y)) { var v = w.CreateObject("vehicle", "jeep", x, y, Team.Red); v.Damage = 0; red.Add(v); }
                    if (Spot(w, 58, 10 + i, out x, out y)) { var v = w.CreateObject("vehicle", "jeep", x, y, Team.Blue); v.Damage = 0; blue.Add(v); }
                }
                for (int k = 0; k < 25; k++) { w.Tick(ZConst.TICK); w.Events.Clear(); }
                foreach (var v in red) w.SendWaypoints(Team.Red, v, new[] { ZWaypoint.Move(60 * 16, v.CenterY, false) });
                foreach (var v in blue) w.SendWaypoints(Team.Blue, v, new[] { ZWaypoint.Move(20 * 16, v.CenterY, false) });
                double least = double.PositiveInfinity, t0 = w.Time;
                while (w.Time - t0 < 60)
                {
                    w.Tick(ZConst.TICK); w.Events.Clear();
                    foreach (var a in red) foreach (var b in blue)
                        {
                            if (a.Destroyed || b.Destroyed || !a.IsMoving || !b.IsMoving) continue;
                            least = Math.Min(least, Math.Sqrt((double)(a.CenterX - b.CenterX) * (a.CenterX - b.CenterX) + (double)(a.CenterY - b.CenterY) * (a.CenterY - b.CenterY)));
                        }
                }
                return least;
            }
        }

        /// <summary>The option's table and its verdict lines: the three crowd runs and the crowd across the own fort gate (each the same
        /// start with the option NO and YES), the enemy spacing.</summary>
        public static string OverlapTable() => OverlapTable(out _);
        public static string OverlapTable(out bool pass)
        {
            var sb = new StringBuilder(); var v = new List<string>(); pass = true;
            sb.AppendLine("C5 FRIENDLY UNITS OVERLAP: a crowd of one team (6 robot squads, 2 light tanks, 2 jeeps) through the level's bridge or road, and the same crowd across the front of its own fort gate, one group order, the calm movement on; the option NO -> YES, the same start.");
            sb.AppendLine("closed = samples (every 0.1 s, every body) whose top-left tile is closed to it (wall, deep water, building or rock)");
            sb.AppendLine("case | bodies | reversals per body | within 12 tiles of the goal | last order ended s | stopped on each other at the end | closed samples");
            foreach (var gate in new[] { false, true })
                foreach (var lv in Levels)
                {
                    var a = RunCrowd(lv, false, gate); var b = RunCrowd(lv, true, gate);
                    string name = Name(lv) + (gate ? " fort gate" : "");
                    double ra = a.Bodies > 0 ? (double)a.Reversals / a.Bodies : 0, rb = b.Bodies > 0 ? (double)b.Reversals / b.Bodies : 0;
                    sb.AppendLine(name + " | " + a.Bodies + " | " + F(ra, "0.00") + " -> " + F(rb, "0.00") + " | " + a.Arrived + " -> " + b.Arrived + " | " + F(a.Last) + " -> " + F(b.Last) + " | " + a.StoppedOnEach + " -> " + b.StoppedOnEach +
                                  " | " + a.Closed + " of " + a.Samples + " -> " + b.Closed + " of " + b.Samples);
                    bool p = (rb < ra || (ra == 0 && rb == 0)) && b.Arrived >= a.Arrived && b.StoppedOnEach == 0 && b.Closed <= a.Closed;
                    pass &= p;
                    v.Add(OzV11Kit.Verdict(p, "C5 " + name + ": reversals per body YES < NO (" + F(ra, "0.00") + " -> " + F(rb, "0.00") + "), arrivals YES >= NO (" + a.Arrived + " -> " + b.Arrived + "), stopped on each other with YES 0 (" + b.StoppedOnEach + "), closed-tile samples YES <= NO (" + a.Closed + " -> " + b.Closed + ")"));
                }
            double g0 = EnemyGap(false), g1 = EnemyGap(true);
            sb.AppendLine("enemies keep apart: least distance between a red and a blue jeep while both drive head-on on the Car Park road: " + F(g0, "0") + " px NO, " + F(g1, "0") + " px YES");
            bool ge = g1 >= g0 - 1; pass &= ge;
            v.Add(OzV11Kit.Verdict(ge, "C5 the option keeps enemies apart as before (" + F(g0, "0") + " -> " + F(g1, "0") + " px)"));
            foreach (var line in v) sb.AppendLine(line);
            return sb.ToString();
        }

        // ================================================================== the editor's menu and the verdict lines
        /// <summary>The whole check with verdict lines (the editor's menu; also headless: runner kc1 check).</summary>
        public static string Run()
        {
            var sb = new StringBuilder();
            bool all = true;
            void V(bool pass, string what) { all &= pass; sb.AppendLine(OzV11Kit.Verdict(pass, what)); }
            // C1: every map under fire, 2 seeds: the time to the fall no longer, no reversal near the door, no more reversals in the whole run
            var c1 = new List<string>(); bool c1Pass = true;
            foreach (var lv in Levels)
            {
                var a = SumFort(lv, 2, false); var b = SumFort(lv, 2, true);
                bool p = FortPass(a, b); c1Pass &= p;
                c1.Add(Name(lv) + " " + F(a.Capped) + " -> " + F(b.Capped) + " s (fell " + a.Fell + " -> " + b.Fell + "), near the door " + a.RevNear + " -> " + b.RevNear + ", whole run " + a.Rev + " -> " + b.Rev + (p ? "" : " (FAIL)"));
            }
            sb.AppendLine("C1 fort run, 8 squads from 12 tiles under fire, 2 seeds per map, time to the fall (no fall in 120 s = 120 s), heading reversals: " + string.Join("; ", c1));
            V(c1Pass, "C1 the run into the enemy fort: on every map the fort falls no later with the rule, no heading reversal within 6 tiles of the door, and no more reversals in the whole run than without it");
            // C9
            string rocks = RockTable(6, "tough,light", false);
            sb.Append(rocks);
            int blOff = 0, blOn = 0, arrOff = 0, arrOn = 0;
            foreach (var line in rocks.Split('\n'))
            {
                int i = line.IndexOf("the game today ", StringComparison.Ordinal); if (i < 0) continue;
                var parts = line.Substring(i).Split(' '); blOff += int.Parse(parts[3].TrimEnd(',')); blOn += int.Parse(parts[7]);
                int k = line.IndexOf("| arrived ", StringComparison.Ordinal); var ap = line.Substring(k + 10).Split(' '); arrOff += int.Parse(ap[0]); arrOn += int.Parse(ap[2]);
            }
            V(blOn < blOff && arrOn >= arrOff, "C9 rocks: fewer rocks blasted with the rule (" + blOff + " -> " + blOn + "), as many arrivals (" + arrOff + " -> " + arrOn + ")");
            // C5
            string ov = OverlapTable(out bool ovPass); sb.Append(ov);
            V(ovPass, "C5 the option: every crowd case above (fewer reversals, as many arrivals, nobody stopped on another, no more closed-tile samples) and enemies kept apart");
            V(!ZWorld.FriendsOverlap, "C5 the option is off after the check (it is put back)");
            sb.AppendLine(all ? "ALL PASS" : "NOT ALL PASS");
            return sb.ToString();
        }

#if UNITY_EDITOR
        [UnityEditor.MenuItem("OpenZ/V1.1/Count Movement Kit (KC1)")]
        static void Menu() => OzV11Kit.RunMenu("Count Movement Kit (KC1)", Run);

        // In Play, for the before / after pictures and clips: the rule off (or on again) in the running level only; nothing is saved,
        // the next level start sets it from the game's settings again (View/ZGame.cs).
        [UnityEditor.MenuItem("OpenZ/V1.1/Movement Kit (Play)/Fort run OFF until the next level start")]
        static void FortOff() => Say("fort run (DV102)", ZWorld.FortRunDeviation = false);
        [UnityEditor.MenuItem("OpenZ/V1.1/Movement Kit (Play)/Fort run ON again")]
        static void FortOn() => Say("fort run (DV102)", ZWorld.FortRunDeviation = true);
        [UnityEditor.MenuItem("OpenZ/V1.1/Movement Kit (Play)/Round the rocks OFF until the next level start")]
        static void RockOff() => Say("round the rocks (DV103)", ZWorld.RockRoundDeviation = false);
        [UnityEditor.MenuItem("OpenZ/V1.1/Movement Kit (Play)/Round the rocks ON again")]
        static void RockOn() => Say("round the rocks (DV103)", ZWorld.RockRoundDeviation = true);
        [UnityEditor.MenuItem("OpenZ/V1.1/Movement Kit (Play)/Report the counters")]
        static void Counters()
        {
            var g = UnityEngine.Object.FindFirstObjectByType<ZGame>();
            var p = g != null && g.World != null ? g.World.Pathing : null;
            UnityEngine.Debug.Log($"[OpenZ V1.1] movement kit: fort run {(ZWorld.FortRunDeviation ? "ON" : "off")} (ticks run outside the old ring {ZObject.FortRunTicks}, paths cut straight {ZObject.FortRunStraights}); " +
                                  $"round the rocks {(ZWorld.RockRoundDeviation ? "ON" : "off")} (choices {p?.RockChoices ?? 0}, ways round {p?.RockRounds ?? 0}, blasts kept {p?.RockBlastsKept ?? 0}); friendly units overlap {(ZWorld.FriendsOverlap ? "ON" : "off")}");
        }
        static void Say(string what, bool on) => UnityEngine.Debug.Log($"[OpenZ V1.1] {what}: {(on ? "ON" : "OFF until the next level start")} (Play only, nothing saved)");
#endif

        /// <summary>The other modes of the headless runner.</summary>
        public static int Extra(string[] a, Action<string> say)
        {
            switch (a[0])
            {
                case "overlap": say(OverlapTable()); return 0;
                case "check": say(Run()); return 0;
                case "rocks": say(RockTable(int.Parse(a[1]), a.Length > 2 ? a[2] : "tough,light", !(a.Length > 3 && a[3] == "noengine"))); return 0;
                case "soak":
                    foreach (var lv in Levels)
                        for (int s = 1; s <= int.Parse(a[1]); s++)
                            foreach (var ov in new[] { false, true }) say(Soak(lv, s, ov, double.Parse(a[2], CultureInfo.InvariantCulture)));
                    return 0;
            }
            say("unknown mode " + a[0]);
            return 1;
        }
    }
}
