using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// DV115 the CPU builds a mix of units (Sim/ZBot.HeavyMix.cs), counted headless (ZWorld.Create + Tick, no scene, nothing saved):
    ///  * Check: the picks of single producers of the level 17 map (their build level set for the check) unit after unit, written out
    ///    here on their own (not by the rule's code): switch off = Zod's picks and the Lasers of DV71; vehicles before 8:00 = Zod's pick;
    ///    from 8:00 the first of every 3 vehicles a heavy tank or a missile launcher in turn (only the one it can make; neither = Zod's
    ///    pick), a vehicle in the making at 8:00 finished first; the fort's Lasers keep their places; robots: every 2nd squad Tough,
    ///    Psycho, Sniper in turn, the Lasers keep their places; and one
    ///    whole computer match on level 16 with the switch on: the buildings' two counters equal the units they turned out, and no heavy
    ///    tank or missile launcher before 8:00.
    ///  * Table: whole computer matches (blue and red both CPU, the game's switches with every rule a Play session turns on) until the
    /// Menus OpenZ/V1.1/CPU Mix: DV115 Check (picks) and OpenZ/V1.1/CPU Mix: DV115 Table (20 min, L16 L02 L17).
    /// </summary>
    public static class OzV11BotHeavyCount
    {
        /// <summary>The levels of the table.</summary>
        public static readonly string[] Levels = { "L16", "L02", "L17" };
        static readonly string[] VehicleOrder = { "jeep", "light", "medium", "heavy", "missile_launcher", "apc", "crane" };
        static readonly string[] RobotOrder = { "grunt", "psycho", "sniper", "tough", "pyro", "laser", ZElectro.Type };

        /// <summary>Zod's own pick (zbot.cpp:20-27), written out here.</summary>
        static string Zod(ZObject b)
        {
            foreach (var p in new[] { "medium", "light", "jeep", "pyro", "tough", "sniper", "tough", "psycho" })
                if (ZBuildLists.InList(b.Type, b.BuildLevel, p)) return p;
            return null;
        }

        /// <summary>The production round's chain of picks (ZBot.ChooseBuildOrders without the Electrobot claim).</summary>
        static string Pick(ZBot bot, ZObject b) => bot.RobotMixPick(b, bot.HeavyPick(b, bot.LaserPick(b, Zod(b))));

        /// <summary>A producer turns out <paramref name="n"/> units at game time <paramref name="time"/>: the pick of each; after each
        /// one its counters move as a built unit moves them (written out here: every unit but the Electrobot for DV71, robot squads
        /// without Lasers and Electrobots, vehicles started from 8:00 on).</summary>
        static string Turn(ZWorld w, ZBot bot, ZObject b, int level, double time, int n)
        {
            b.Level = level; b.BuiltCount = 0; b.BotLateVehicles = 0; b.BotMixRobots = 0; b.Building = false; w.Time = time;
            var l = new List<string>();
            for (int i = 0; i < n; i++)
            {
                string p = Pick(bot, b) ?? "-";
                l.Add(p);
                if (p != ZElectro.Type) b.BuiltCount++;
                if (p == "laser" || p == ZElectro.Type || p == "-") continue;
                if (ZBuildLists.IsRobot(p)) b.BotMixRobots++;
                else if (time >= 480) b.BotLateVehicles++;
            }
            return string.Join(" ", l);
        }

        static string Rep(string t, int n) => string.Join(" ", Enumerable.Repeat(t, n));

        public static string Check(string settings, string l16, string l17)
        {
            var sb = new StringBuilder();
            sb.AppendLine("The CPU builds a mix of units (DV115): heavy tanks and missile launchers from 8:00, Tough / Psycho / Sniper squads in turn");
            bool was = ZWorld.BotHeavyDeviation, laserWas = ZWorld.BotLaserDeviation;
            try
            {
                ZWorld.BotLaserDeviation = true;
                var w = ZWorld.Create(settings, l17, 1);
                var bot = new ZBot(w, Team.Blue, 108);
                var rf = w.Objects.First(o => o.Type == "robot_factory");
                var vf = w.Objects.First(o => o.Type == "vehicle_factory");
                var fort = w.Objects.First(o => o.IsFort && o.Owner == Team.Blue);
                const string M = "medium", H = "heavy", L = "missile_launcher";
                // 1 switch off: Zod's picks and DV71's Lasers
                ZWorld.BotHeavyDeviation = false;
                {
                    string r = Turn(w, bot, rf, 4, 600, 6), v = Turn(w, bot, vf, 5, 600, 9);
                    bool ok = r == "pyro laser pyro pyro laser pyro" && v == Rep(M, 9);
                    sb.AppendLine($"  switch off, from 8:00: level 5 robot factory {r}; level 6 vehicle factory {v}");
                    sb.AppendLine(OzV11Kit.Verdict(ok, "switch off: Zod's picks and the Lasers of DV71, nothing else"));
                }
                ZWorld.BotHeavyDeviation = true;
                // 2 vehicles
                {
                    string early = Turn(w, bot, vf, 5, 479, 9), six = Turn(w, bot, vf, 5, 480, 9), five = Turn(w, bot, vf, 4, 600, 9), two = Turn(w, bot, vf, 1, 600, 6);
                    sb.AppendLine($"  vehicle factory before 8:00 (level 6): {early}");
                    sb.AppendLine($"  vehicle factory from 8:00: level 6 {six}; level 5 {five}; level 2 {two}");
                    sb.AppendLine(OzV11Kit.Verdict(early == Rep(M, 9), "vehicles before 8:00: Zod's pick only"));
                    bool ok = six == $"{H} {M} {M} {L} {M} {M} {H} {M} {M}" && five == $"{H} {M} {M} {H} {M} {M} {H} {M} {M}" && two == Rep("light", 6);
                    sb.AppendLine(OzV11Kit.Verdict(ok, "vehicles from 8:00: the first of every 3 a heavy tank or a missile launcher in turn, heavy first; only heavy tanks where it cannot make the launcher; Zod's pick where it can make neither"));
                    // a Medium tank in the making at 8:00 is finished first (never thrown away), the next one started is the heavy tank
                    vf.Level = 5; vf.BotLateVehicles = 0; w.Time = 480.5;
                    vf.Building = true; vf.CurUnit = M; vf.BInit = 470; string keep = Pick(bot, vf);
                    vf.BInit = 481; string next = Pick(bot, vf);
                    vf.Building = false; vf.CurUnit = null;
                    sb.AppendLine($"  a Medium tank started at 7:50, at 8:00.5: {keep}; restarted at 8:01: {next}");
                    sb.AppendLine(OzV11Kit.Verdict(keep == M && next == H, "the vehicle in the making at 8:00 is finished as it is, the next one started is the heavy tank"));
                }
                // 3 the fort: the Lasers keep their places
                {
                    string f = Turn(w, bot, fort, 5, 600, 18);
                    sb.AppendLine($"  fort (level 6) from 8:00: {f}");
                    bool ok = f == "laser laser heavy laser laser medium laser laser medium laser laser missile_launcher laser laser medium laser laser medium";
                    sb.AppendLine(OzV11Kit.Verdict(ok, "fort: the Lasers of DV71 keep their places, the first of every 3 of its vehicles a heavy tank or a missile launcher"));
                }
                // 4 robots
                {
                    string r5 = Turn(w, bot, rf, 4, 0, 9), r4 = Turn(w, bot, rf, 3, 0, 6), r3 = Turn(w, bot, rf, 2, 0, 6), r2 = Turn(w, bot, rf, 1, 0, 4);
                    sb.AppendLine($"  robot factory from the start: level 5 {r5}; level 4 {r4}; level 3 {r3}; level 2 {r2}");
                    bool ok = r5 == "pyro laser tough pyro laser psycho pyro laser sniper" && r4 == "pyro tough pyro psycho pyro sniper"
                              && r3 == "tough tough tough psycho tough sniper" && r2 == Rep("psycho", 4);
                    sb.AppendLine(OzV11Kit.Verdict(ok, "robots: every 2nd squad Tough, Psycho, Sniper in turn (those it can make), the Lasers keep their places, from the start"));
                }
                // 5 one whole match: the counters equal the units turned out
                {
                    var g = Match(settings, l16, "L16", 1, true, 1200);
                    sb.AppendLine($"  level 16 computer match, switch on: {g.End}; counters checked on {g.Producers} producers");
                    sb.AppendLine(OzV11Kit.Verdict(g.CountersOk && g.Producers > 0, "the buildings' counters equal the units they turned out (" + (g.CountersNote ?? "all equal") + ")"));
                    sb.AppendLine(OzV11Kit.Verdict(g.EarlyHeavy == 0, $"no heavy tank or missile launcher before 8:00 ({g.EarlyHeavy})"));
                }
            }
            finally { ZWorld.BotHeavyDeviation = was; ZWorld.BotLaserDeviation = laserWas; }
            return sb.ToString();
        }

        // ------------------------------------------------------------------ whole matches
        public sealed class Game
        {
            public string Level, End; public int Seed; public bool On; public double Secs;
            /// <summary>Units built [team][before 8:00 = 0 / from 8:00 = 1][type].</summary>
            public readonly Dictionary<Team, Dictionary<string, int>[]> Built = new Dictionary<Team, Dictionary<string, int>[]>();
            public int EarlyHeavy, Producers; public bool CountersOk = true, PyroHalfOk = true; public string CountersNote, PyroNote;
            public int Count(Team t, int part, Func<string, bool> which) => Built.TryGetValue(t, out var a) ? a[part].Where(kv => which(kv.Key)).Sum(kv => kv.Value) : 0;
        }

        static bool IsVehicle(string t) => !ZBuildLists.IsRobot(t) && !ZBuildLists.IsCannon(t);

        /// <summary>One computer match (blue seed + 101, red seed, both Normal; the game's switches with the rules a Play session turns
        /// on, strike groups on) until the game is over or <paramref name="seconds"/>.</summary>
        public static Game Match(string settings, string map, string level, int seed, bool on, double seconds)
        {
            var g = new Game { Level = level, Seed = seed, On = on };
            using (OzV11StrikeCount.Game(true))
            {
                ZWorld.BotHeavyDeviation = on;
                var w = ZWorld.Create(settings, map, seed, level == "L17");
                w.Bots.Add(new ZBot(w, Team.Blue, seed + 101)); w.Bots.Add(new ZBot(w, Team.Red, seed));
                w.Events.Clear();
                var late = new Dictionary<int, int>(); var mix = new Dictionary<int, int>(); var pyro = new Dictionary<int, int>();
                foreach (var t in new[] { Team.Red, Team.Blue }) g.Built[t] = new[] { new Dictionary<string, int>(), new Dictionary<string, int>() };
                while (w.Time < seconds && !w.GameOver)
                {
                    w.Tick(ZConst.TICK);
                    foreach (var e in w.Events)
                    {
                        if (e.type != ZEventType.UnitBuilt) continue;
                        var u = w.GetAny(e.objId); var b = w.GetAny(e.otherId);
                        if (u == null || b == null || !g.Built.ContainsKey(u.Owner)) continue;
                        int part = w.Time + 1e-6 >= ZBot.HEAVY_FROM ? 1 : 0;
                        var d = g.Built[u.Owner][part]; d[u.Type] = d.TryGetValue(u.Type, out int n) ? n + 1 : 1;
                        if (part == 0 && (u.Type == "heavy" || u.Type == "missile_launcher")) g.EarlyHeavy++;
                        if (ZBuildLists.IsRobot(u.Type) && u.Type != "laser" && u.Type != ZElectro.Type) mix[b.Id] = mix.TryGetValue(b.Id, out int m) ? m + 1 : 1;
                        if (u.Type == "pyro") pyro[b.Id] = pyro.TryGetValue(b.Id, out int p) ? p + 1 : 1;
                        if (IsVehicle(u.Type) && w.Time - e.value + 1e-3 >= ZBot.HEAVY_FROM) late[b.Id] = late.TryGetValue(b.Id, out int v) ? v + 1 : 1;   // value = its build time
                    }
                    w.Events.Clear();
                }
                g.Secs = w.Time;
                g.End = (w.GameOver ? "won by " + w.Winner : "time") + $" at {(int)(w.Time / 60)}:{(int)(w.Time % 60):00}";
                foreach (var b in w.Objects.Where(o => o.Kind == ZKind.Building && o.IsProducer))
                {
                    int lv = late.TryGetValue(b.Id, out int a) ? a : 0, mr = mix.TryGetValue(b.Id, out int c) ? c : 0, py = pyro.TryGetValue(b.Id, out int q) ? q : 0;
                    if (lv == 0 && mr == 0 && b.BotLateVehicles == 0 && b.BotMixRobots == 0) continue;
                    g.Producers++;
                    if (b.BotLateVehicles != lv || b.BotMixRobots != mr) { g.CountersOk = false; g.CountersNote = $"{b.Type} #{b.Id}: vehicles started from 8:00 {b.BotLateVehicles} / {lv} built, robot squads {b.BotMixRobots} / {mr} built"; }
                    if (on && py * 2 > mr + 1) { g.PyroHalfOk = false; g.PyroNote = $"{b.Type} #{b.Id}: {py} Pyros of {mr} squads"; }
                }
            }
            return g;
        }

        static string Names(Dictionary<string, int> d, string[] order)
        {
            var l = order.Where(t => d.TryGetValue(t, out int n) && n > 0).Select(t => $"{(t == "missile_launcher" ? "launcher" : t)} {d[t]}").ToList();
            l.AddRange(d.Keys.Where(t => Array.IndexOf(order, t) < 0 && d[t] > 0).OrderBy(t => t).Select(t => $"{t} {d[t]}"));
            return l.Count == 0 ? "-" : string.Join(", ", l);
        }

        static string Cell(Game g, Team t, int part)
        {
            var d = g.Built[t][part];
            return "vehicles: " + Names(d.Where(kv => IsVehicle(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value), VehicleOrder) +
                   "; robots: " + Names(d.Where(kv => ZBuildLists.IsRobot(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value), RobotOrder);
        }

        /// <summary>The table: every level of <see cref="Levels"/> (or the given ones), seeds 1..<paramref name="seeds"/>, the switch off
        /// and on, until the game is over or <paramref name="seconds"/>.</summary>
        public static string Table(string settings, Func<string, string> mapText, int seeds, double seconds, params string[] levels)
        {
            if (levels == null || levels.Length == 0) levels = Levels;
            var sb = new StringBuilder();
            sb.AppendLine($"DV115 computer matches (blue and red both CPU, Normal; the game's switches with the rules a Play session turns on, strike groups on), until the game is over or {seconds / 60:0}:00");
            sb.AppendLine("level | seed | DV115 | end | team | built before 8:00 | built from 8:00");
            bool early = true, counters = true, pyroHalf = true, offClean = true;
            foreach (var lv in levels)
            {
                string map = mapText(lv);
                int heavyOn = 0, vehLateOn = 0, robotsOn = 0, pyroOn = 0, robotsOff = 0, pyroOff = 0, heavyOff = 0;
                var seenOn = new HashSet<string>();
                for (int seed = 1; seed <= seeds; seed++)
                    foreach (bool on in new[] { false, true })
                    {
                        var g = Match(settings, map, lv, seed, on, seconds);
                        foreach (var t in new[] { Team.Blue, Team.Red })
                            sb.AppendLine($"{lv} | {seed} | {(on ? "on " : "off")} | {g.End} | {t} | {Cell(g, t, 0)} | {Cell(g, t, 1)}");
                        early &= g.EarlyHeavy == 0; counters &= g.CountersOk; if (!g.CountersOk) sb.AppendLine("      counters: " + g.CountersNote);
                        pyroHalf &= g.PyroHalfOk; if (!g.PyroHalfOk) sb.AppendLine("      pyros: " + g.PyroNote);
                        foreach (var t in new[] { Team.Blue, Team.Red })
                            for (int part = 0; part < 2; part++)
                            {
                                int hv = g.Count(t, part, x => x == "heavy" || x == "missile_launcher");
                                int rb = g.Count(t, part, x => ZBuildLists.IsRobot(x) && x != ZElectro.Type), py = g.Count(t, part, x => x == "pyro");
                                if (on) { heavyOn += hv; robotsOn += rb; pyroOn += py; if (part == 1) vehLateOn += g.Count(t, 1, IsVehicle); foreach (var kv in g.Built[t][part]) if (kv.Value > 0) seenOn.Add(kv.Key); }
                                else { heavyOff += hv; robotsOff += rb; pyroOff += py; }
                            }
                        if (!on) offClean &= g.Count(Team.Blue, 0, x => x == "heavy" || x == "missile_launcher") + g.Count(Team.Blue, 1, x => x == "heavy" || x == "missile_launcher") +
                                             g.Count(Team.Red, 0, x => x == "heavy" || x == "missile_launcher") + g.Count(Team.Red, 1, x => x == "heavy" || x == "missile_launcher") == 0;
                    }
                string P(int a, int b) => b == 0 ? "-" : $"{a * 100 / b} %";
                sb.AppendLine($"  {lv} in all ({seeds} seed(s), both teams): off: heavy + launchers {heavyOff}, Pyros {pyroOff} of {robotsOff} robot squads ({P(pyroOff, robotsOff)}); " +
                              $"on: heavy + launchers {heavyOn} of {vehLateOn} vehicles from 8:00 ({P(heavyOn, vehLateOn)}), Pyros {pyroOn} of {robotsOn} robot squads ({P(pyroOn, robotsOn)}); " +
                              $"robot types seen with the switch on: {string.Join(", ", RobotOrder.Where(seenOn.Contains))}");
            }
            sb.AppendLine(OzV11Kit.Verdict(early, "no heavy tank or missile launcher before 8:00 in any match"));
            sb.AppendLine(OzV11Kit.Verdict(offClean, "switch off: no heavy tank or missile launcher at all (Zod's picks)"));
            sb.AppendLine(OzV11Kit.Verdict(counters, "the buildings' counters equal the units they turned out, every match"));
            sb.AppendLine(OzV11Kit.Verdict(pyroHalf, "switch on: no producer made more than half of its robot squads (Lasers aside, one squad of room) as Pyros"));
            return sb.ToString();
        }

#if UNITY_EDITOR
        [UnityEditor.MenuItem("OpenZ/V1.1/CPU Mix: DV115 Check (picks)")]
        static void Menu() => OzV11Kit.RunMenu("The CPU builds a mix of units (DV115)", () => Check(OzV11Kit.SettingsText, OzV11Kit.MapText("L16"), OzV11Kit.MapText("L17")));

        [UnityEditor.MenuItem("OpenZ/V1.1/CPU Mix: DV115 Table (20 min, L16 L02 L17)")]
        static void MenuTable() => OzV11Kit.RunMenu("The CPU builds a mix of units: computer matches before / after (DV115)", () => Table(OzV11Kit.SettingsText, OzV11Kit.MapText, 2, 1200));
#endif
    }
}
