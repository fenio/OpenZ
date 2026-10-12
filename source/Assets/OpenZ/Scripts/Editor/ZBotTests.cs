using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using OpenZ.Sim;
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
#endif

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Zod CPU player checks (zbot.cpp / zbot_events.cpp, research zod_controls_ui_ai.md section 6), headless on the real
    /// maps, in the style of OpenZParity: aggression tiers, first-round timing, wave size, target spread, mutual-nearest
    /// matching, the eligibility table, the bot client's view (regions, invisible map items, relayed waypoints), factory
    /// exits, production choices, grenade collection and vehicle/cannon capture end to end, bot vs bot on L02 and L17,
    /// The core (Run) has no Unity dependency, so the same file also runs in a plain .NET harness.
    /// </summary>
    public static class ZBotTests
    {
        /// <summary>PlayerPrefs key ZGame.Awake reads: 1 = both teams CPU (bot vs bot demo) at Zod speed; a faster demo only
        /// through the explicit pref oz_demo_speed.</summary>
        public const string DemoPref = "oz_botmatch";

#if UNITY_EDITOR
        static string Report => Path.Combine(OpenZPaths.Records, "bot_checks.md");

        [MenuItem("OpenZ/Run Bot Checks")]
        public static string RunMenu()
        {
            var s = Run(Resources.Load<TextAsset>("OpenZ/default_settings").text, lv => Resources.Load<TextAsset>($"OpenZ/Maps/{lv}_grid").text, Report);
            Debug.Log(s + " -> " + Report);
            return s;
        }

        const string DemoMenu = "OpenZ/Bot Demo Mode (both teams CPU)";
        [MenuItem(DemoMenu)]
        public static void ToggleDemo()
        {
            bool on = PlayerPrefs.GetInt(DemoPref, 0) != 1;
            PlayerPrefs.SetInt(DemoPref, on ? 1 : 0); PlayerPrefs.Save();
            Debug.Log($"OpenZ bot demo mode {(on ? "ON: red and blue are CPU players (Zod speed) from the next Play" : "OFF: red = player, blue = CPU")}");
        }
        [MenuItem(DemoMenu, true)]
        static bool ToggleDemoCheck() { Menu.SetChecked(DemoMenu, PlayerPrefs.GetInt(DemoPref, 0) == 1); return true; }
#endif

        class Row { public string test, expected, measured, note; public bool pass; }

        static string S;
        static Func<string, string> M;
        static ZWorld World(string lv, int seed) => ZWorld.Create(S, M(lv), seed);
        /// <summary>The real map with its buildings, flags and items, minus the map's units.</summary>
        static ZWorld Bare(string lv, int seed)
        {
            var w = World(lv, seed);
            w.Objects.RemoveAll(o => o.Kind == ZKind.Robot || o.Kind == ZKind.Vehicle || o.Kind == ZKind.Cannon);
            return w;
        }
        static ZObject B(ZWorld w, string type, int tx, int ty) => w.Objects.First(o => o.Type == type && o.LocX == tx * 16 && o.LocY == ty * 16);
        static ZObject Grunt(ZWorld w, int tx, int ty, Team t, string type = "grunt") => w.CreateRobotGroup(type, tx * 16, ty * 16, t, 1);
        static void Kill(ZWorld w, ZObject o) { o.SetHealth(0); w.UpdateObjectHealth(o); }

        /// <param name="matches">false skips the long bot-vs-bot runs.</param>
        public static string Run(string settingsText, Func<string, string> mapText, string reportPath, bool matches = true, double matchSeconds = 600)
        {
            S = settingsText; M = mapText;
            bool snipeDeviation = ZWorld.SnipeDeviation; ZWorld.SnipeDeviation = false;   // DC2: the Zod CPU checks run Zod exact
            bool vetDev = ZWorld.VeteranDeviation; ZWorld.VeteranDeviation = false;         // veterancy_v1 DV12: Zod has no ranks
            var rows = new List<Row>();
            void Guard(string name, Action<List<Row>> a)
            {
                try { a(rows); }
                catch (Exception ex) { rows.Add(new Row { test = name, expected = "no exception", measured = ex.GetType().Name + ": " + ex.Message, pass = false, note = ex.StackTrace?.Split('\n').FirstOrDefault()?.Trim() }); }
            }
            Guard("tiers", Tiers);
            Guard("first round", FirstRound);
            Guard("wave", Wave);
            Guard("spread", Spread);
            Guard("mutual nearest", MutualNearest);
            Guard("eligibility", Eligibility);
            Guard("client view: agro", ClientViewAgro);
            Guard("client view: rock", ClientViewRock);
            Guard("factory exit", FactoryExit);
            Guard("production", Production);
            Guard("ignored", IgnoredBot);
            Guard("demo switch", DemoSwitch);
            if (matches)
            {
                Guard("grenades + capture", Collectors);
                Guard("bot vs bot L02", r => BotVsBot(r, "L02", 1, matchSeconds));
                Guard("bot vs bot L17", r => BotVsBot(r, "L17", 1, matchSeconds));
                Guard("determinism", Determinism);
            }

            var sb = new StringBuilder();
            int pass = rows.Count(r => r.pass);
            sb.AppendLine("# OpenZ bot checks (Zod CPU player)");
            sb.AppendLine();
            sb.AppendLine($"Generated {DateTime.Now:yyyy-MM-dd HH:mm} by `OpenZ/Run Bot Checks` (headless Zod sim, 100 Hz ticks). **{pass}/{rows.Count} PASS**. " +
                          "Source: zbot.cpp / zbot_events.cpp, research zod_controls_ui_ai.md section 6.");
            sb.AppendLine();
            sb.AppendLine("| Test | Expected (Zod) | Measured (OpenZ) | Result | Note |");
            sb.AppendLine("|---|---|---|---|---|");
            foreach (var r in rows) sb.AppendLine($"| {r.test} | {r.expected} | {r.measured} | {(r.pass ? "PASS" : "**FAIL**")} | {r.note} |");
            ZWorld.SnipeDeviation = snipeDeviation;
            ZWorld.VeteranDeviation = vetDev;                                                // veterancy_v1 DV12
            if (!string.IsNullOrEmpty(reportPath)) File.WriteAllText(reportPath, sb.ToString());
            string summary = $"bot checks: {pass}/{rows.Count} PASS";
            return summary + "\n" + string.Join("\n", rows.Where(r => !r.pass).Select(r => $"FAIL {r.test}: {r.expected} vs {r.measured} {r.note}"));
        }

        // ------------------------------------------------------------------ GoAllOut_3 (zbot.cpp:635-691)
        static void Tiers(List<Row> rows)
        {
            var w = Bare("L02", 1);
            var bot = new ZBot(w, Team.Blue, 1);
            string Tier() { bool a = bot.GoAllOut_3(out double p, out double d); return $"{(a ? "all-out" : "hold")} {p:0.00} {d:0}s"; }
            void Own(int blue, int red) { int i = 0; foreach (var f in w.Flags) { f.Owner = i < blue ? Team.Blue : i < blue + red ? Team.Red : Team.Neutral; i++; } }
            var cases = new List<(int b, int r, string exp, string why)>
            {
                (0, 0, "hold 0.35 4s", "owners {N}: fair 1, share 0"),
                (1, 0, "hold 0.25 5s", "owners {N,B}: 1/7 >= 0.25 x 0.5"),
                (2, 0, "hold 0.15 8s", "2/7 >= 0.5 x 0.5"),
                (4, 0, "all-out 0.15 12s", "4/7 >= 0.5"),
                (2, 2, "hold 0.15 8s", "owners {N,B,R}: 2/7 >= 1/3 x 0.5"),
                (1, 6, "hold 0.25 5s", "owners {B,R}: 1/7 >= 0.5 x 0.25"),
                (0, 7, "hold 0.35 4s", "owners {R}: 0/7"),
                (3, 4, "hold 0.15 8s", "owners {B,R}: 3/7 < 0.5"),
                (7, 0, "all-out 0.15 12s", "owners {B}: 7/7"),
            };
            var got = new List<string>(); bool ok = true;
            foreach (var c in cases) { Own(c.b, c.r); var t = Tier(); got.Add(t); if (t != c.exp) ok = false; }
            rows.Add(new Row { test = "aggression tier (all-out / wave / delay) for 9 flag splits on L02 (7 flags)", expected = string.Join("; ", cases.Select(c => $"B{c.b}R{c.r}: {c.exp}")),
                               measured = string.Join("; ", cases.Select((c, i) => $"B{c.b}R{c.r}: {got[i]}")), pass = ok, note = "neutral counts as an owner (zbot.cpp:649-659)" });
            w.Flags.Clear();
            var none = Tier();
            rows.Add(new Row { test = "aggression tier with no flags", expected = "all-out 0.35 4s", measured = none, pass = none == "all-out 0.35 4s" });
        }

        // ------------------------------------------------------------------ ProcessAI rate + last_order_time = 0
        static void FirstRound(List<Row> rows)
        {
            var w = World("L02", 2);
            var bots = ZBot.Attach(w, Team.Red, false, 2);
            var bot = bots[0];
            double first = -1, second = -1; int buildAt0 = -1;
            while (w.Time < 30 && second < 0)
            {
                w.Tick(ZConst.TICK); w.Events.Clear();
                if (buildAt0 < 0 && w.Time > 0.005) buildAt0 = bot.BuildChanges;
                if (first < 0 && bot.Rounds >= 1) first = w.Time;
                else if (first >= 0 && second < 0 && bot.Rounds >= 2) second = w.Time;
            }
            rows.Add(new Row { test = "CPU tick: first order round", expected = "4.01 s (AI every 1 s from 0.01, last_order_time 0 + 4 s delay)", measured = $"{first:F2} s",
                               pass = Math.Abs(first - 4.01) < 0.005, note = "zbot.cpp:10-11, 100-125, 140" });
            rows.Add(new Row { test = "CPU tick: next round after the tier delay", expected = "exactly the tier delay later (4 / 5 / 8 / 12 s)", measured = $"{second - first:F2} s later ({bot.LastDelay:0} s tier now)",
                               pass = new[] { 4.0, 5.0, 8.0, 12.0 }.Any(d => Math.Abs(second - first - d) < 0.005) });
            rows.Add(new Row { test = "CPU production pick on its first tick", expected = "blue fort switches Grunt -> Jeep at 0.01 s (STOP + START)", measured = $"{buildAt0} STOP+START pairs at 0.01 s",
                               pass = buildAt0 >= 1, note = string.Join("; ", bot.Log.Take(4)) });
        }

        // ------------------------------------------------------------------ ReduceUnitsToPercent (zbot.cpp:176-201)
        static void Wave(List<Row> rows)
        {
            var w = Bare("L02", 3); var bot = new ZBot(w, Team.Blue, 3);
            List<ZObject> N(int n) => Enumerable.Range(0, n).Select(i => new ZObject { Id = 900000 + i }).ToList();
            int K(int n, double p) { var l = N(n); bot.ReduceUnitsToPercent(l, p); return l.Distinct().Count() == l.Count ? l.Count : -1; }
            var got = $"{K(10, 0.35)}, {K(10, 0.25)}, {K(10, 0.15)}, {K(10, 0.95)}, {K(1, 0.15)}, {K(3, 0.15)}";
            rows.Add(new Row { test = "wave size of 10 idle units at 35 / 25 / 15 / 95 %, 1 unit, 3 units at 15 %", expected = "4, 3, 2, 10, 1, 1", measured = got, pass = got == "4, 3, 2, 10, 1, 1",
                               note = "(int)(p x n) + 1, random pick without repeats" });
        }

        // ------------------------------------------------------------------ RemoveTargetedFromTargets (zbot.cpp:203-220)
        static void Spread(List<Row> rows)
        {
            List<ZObject> N(int n) => Enumerable.Range(0, n).Select(i => new ZObject { Id = 910000 + i }).ToList();
            int R(int targeted) { var t = N(8); var tg = t.Take(targeted).ToList(); tg.Add(t[0]); ZBot.RemoveTargetedFromTargets(t, tg); return t.Count; }
            var got = $"{R(2)}, {R(5)}, {R(6)}, {R(8)}";
            rows.Add(new Row { test = "spread: 8 targets with 2 / 5 / 6 / 8 already targeted", expected = "6, 3, 8, 8", measured = got, pass = got == "6, 3, 8, 8",
                               note = "undo when 25 % or less would remain" });
        }

        // ------------------------------------------------------------------ GiveOutOrders_3 mutual-nearest (zbot.cpp:222-414)
        static void MutualNearest(List<Row> rows)
        {
            var w = Bare("L02", 4);
            var u1 = Grunt(w, 10, 10, Team.Blue); var u2 = Grunt(w, 16, 10, Team.Blue);
            var f1 = w.CreateObject("map_item", "flag", 14 * 16, 10 * 16, Team.Neutral);
            var f2 = w.CreateObject("map_item", "flag", 30 * 16, 10 * 16, Team.Neutral);
            var bot = new ZBot(w, Team.Blue, 4);
            var units = new List<ZObject> { u1, u2 }; var targets = new List<ZObject> { f1, f2 };
            bot.MatchTargets_3(units, targets); bot.GiveOutOrders_3(units, targets);
            string To(ZObject u) => u.Waypoints.Count == 0 ? "none" : u.Waypoints[0].refId == f1.Id ? "F1" : u.Waypoints[0].refId == f2.Id ? "F2" : "?";
            var wp = u2.Waypoints.Count > 0 ? u2.Waypoints[0] : ZWaypoint.Cleared;
            bool shape = wp.mode == WpMode.Move && wp.x == f1.CenterX && wp.y == f1.CenterY && wp.attackTo && wp.playerGiven;
            rows.Add(new Row { test = "mutual-nearest: U1(10,10) U2(16,10), flags F1(14,10) F2(30,10)", expected = "U1 -> F2, U2 -> F1 (greedy nearest would send U1 -> F1); MOVE to the flag centre, attack_to, player_given",
                               measured = $"U1 -> {To(u1)}, U2 -> {To(u2)}; {(shape ? "waypoint shape ok" : $"waypoint {wp.mode} ({wp.x},{wp.y}) at {wp.attackTo} pg {wp.playerGiven}")}",
                               pass = To(u1) == "F2" && To(u2) == "F1" && shape, note = "distance between top-left corners (zobject.cpp:918-926)" });
            rows.Add(new Row { test = "orders counted", expected = "2 orders, 0 rejected", measured = $"{bot.Orders} orders, {bot.RejectedOrders} rejected", pass = bot.Orders == 2 && bot.RejectedOrders == 0 });
        }

        // ------------------------------------------------------------------ MatchTargets_3 table (zbot.cpp:416-511)
        static void Eligibility(List<Row> rows)
        {
            var w = Bare("L02", 5);
            var g0 = Grunt(w, 20, 80, Team.Blue); var tough = Grunt(w, 22, 80, Team.Blue, "tough"); var g1 = Grunt(w, 24, 80, Team.Blue); g1.Grenades = 3;
            var lightDmg = w.CreateObject("vehicle", "light", 26 * 16, 80 * 16, Team.Blue); lightDmg.Health = lightDmg.MaxHealth / 2;
            var crane = w.CreateObject("vehicle", "crane", 28 * 16, 80 * 16, Team.Blue);
            var lightFull = w.CreateObject("vehicle", "light", 30 * 16, 80 * 16, Team.Blue);
            var names = new Dictionary<ZObject, string> { [g0] = "grunt", [tough] = "tough", [g1] = "grunt+nades", [lightDmg] = "light50%", [crane] = "crane", [lightFull] = "light" };
            var flag = w.Flags.First(f => f.LocX == 10 * 16 && f.LocY == 81 * 16);
            var crate = w.Objects.First(o => o.Type == "grenades" && o.LocX == 25 * 16 && o.LocY == 77 * 16);
            var redFort = B(w, "fort_back", 48, 72);
            var repair = w.CreateObject("building", "repair", 58 * 16, 80 * 16, Team.Blue);
            var ruin = B(w, "robot_factory", 7, 74); Kill(w, ruin);
            var jeep = w.CreateObject("vehicle", "jeep", 34 * 16, 82 * 16, Team.Neutral);
            var gat = w.CreateObject("cannon", "gatling", 38 * 16, 80 * 16, Team.Neutral);
            var redGrunt = Grunt(w, 44, 82, Team.Red);
            var redLight = w.CreateObject("vehicle", "light", 44 * 16, 78 * 16, Team.Red);
            var bot = new ZBot(w, Team.Blue, 5);
            var units = names.Keys.ToList();
            var targets = new List<ZObject> { flag, crate, redFort, repair, ruin, jeep, gat, redGrunt, redLight };
            var tnames = new[] { "neutral flag", "grenade crate", "red fort_back", "blue repair station", "destroyed neutral factory", "empty jeep", "empty gatling", "red grunt", "red light tank" };
            var expect = new[]
            {
                "grunt, tough, grunt+nades, light50%, crane, light", "grunt", "grunt, tough, grunt+nades, light50%, crane, light", "light50%", "crane",
                "grunt, tough, grunt+nades", "grunt, tough, grunt+nades", "grunt, tough, grunt+nades, light50%, light", "grunt, tough, grunt+nades, light50%, light"
            };
            bot.MatchTargets_3(units, targets);
            for (int i = 0; i < targets.Count; i++)
            {
                var got = string.Join(", ", units.Where(u => bot.Candidates(targets[i]).Contains(u)).Select(u => names[u]));
                rows.Add(new Row { test = $"eligible for {tnames[i]}", expected = expect[i], measured = got == "" ? "nobody" : got, pass = got == expect[i] });
            }
            // the bot client's regions (tile classes only) vs ZWorld.Path (objects included)
            var legacy = new ZBot(w, Team.Blue, 5) { ClientView = false };
            var lu = units.ToList(); legacy.MatchTargets_3(lu, new List<ZObject> { redFort, repair, ruin });
            int legacyPairs = legacy.Candidates(redFort).Count + legacy.Candidates(repair).Count + legacy.Candidates(ruin).Count;
            bool pathSays = w.Path.InSameRegion(g0.LocX + 8, g0.LocY + 8, redFort.CenterX, redFort.CenterY, true);
            rows.Add(new Row { test = "bot regions = tile classes at map read (zmap.cpp:700-705), not ZWorld.Path", expected = "red fort_back, repair station and ruined factory centres reachable",
                               measured = $"client regions: {bot.Candidates(redFort).Count + bot.Candidates(repair).Count + bot.Candidates(ruin).Count} pairs; ZWorld.Path regions (first port): {legacyPairs} pairs, fort_back centre same region = {pathSays}",
                               pass = bot.Candidates(redFort).Count == 6, note = "info: ZWorld.Create rebuilds regions after placing objects, so every building centre tile is region -2 there" });
        }

        // ------------------------------------------------------------------ client view: server-inserted waypoints are not relayed
        static void ClientViewAgro(List<Row> rows)
        {
            var w = Bare("L02", 6);
            if (!FindOpenRow(w, 10, 230, out int ox, out int oy)) { rows.Add(new Row { test = "client view: agro", expected = "-", measured = "no open stretch found", pass = false }); return; }
            var blue = Grunt(w, ox, oy, Team.Blue);
            var red = w.CreateRobotGroup("grunt", ox * 16 + 140, oy * 16, Team.Red, 1);                  // 140 px: agro range, not attack range
            var bot = new ZBot(w, Team.Blue, 6) { Ignored = true }; w.Bots.Add(bot);
            var legacy = new ZBot(w, Team.Blue, 6) { ClientView = false };
            bool agro = false;
            for (int i = 0; i < 200 && !agro; i++) { w.Tick(ZConst.TICK); w.Events.Clear(); agro = blue.Waypoints.Count > 0 && blue.Waypoints[0].mode == WpMode.Agro; }
            var u = new List<ZObject>(); var tg = new List<ZObject>(); bot.CollectOurUnits_3(u, tg);
            var lu = new List<ZObject>(); var ltg = new List<ZObject>(); legacy.CollectOurUnits_3(lu, ltg);
            rows.Add(new Row { test = "client view: a squad fighting on its own (server AGRO waypoint) looks idle", expected = "idle for the bot (never relayed), busy for the first port",
                               measured = !agro ? "no AGRO waypoint appeared" : $"bot: {(u.Contains(blue) ? "idle" : "busy")}, known list {bot.KnownWaypoints(blue).Count}; first port: {(lu.Contains(blue) ? "idle" : "busy")}, targeted {ltg.Count}",
                               pass = agro && u.Contains(blue) && !lu.Contains(blue) && bot.KnownWaypoints(blue).Count == 0, note = "zobject.cpp:1749-1758 push_back without updated_waypoints (only KillWP sets it, :3341-3343); relay only on KillWP (zserver.cpp:1193-1198)" });
        }

        /// <summary>A row of `len` robot-passable ground tiles with no destroyable barrier on it and no flag or crate within
        /// `clear` px (auto-grab reaches 220 px), so the two squads only see each other.</summary>
        static bool FindOpenRow(ZWorld w, int len, int clear, out int x0, out int y0)
        {
            var items = w.Objects.Where(o => o.Type == "flag" || o.Type == "grenades").ToList();
            for (int y = 4; y < w.Map.height - 4; y++)
                for (int x = 4; x < w.Map.width - len - 4; x++)
                {
                    bool ok = true;
                    for (int i = 0; i <= len && ok; i++)
                        ok = w.Path.TilePassable(x + i, y, true) && !w.Path.HasDestroyableBarrier(x + i, y) && w.Map.ClassAt(x + i, y) == ZClass.Ground;
                    if (!ok) continue;
                    int ax = x * 16 + 8, bx = (x + len) * 16 + 8, cy = y * 16 + 8;
                    if (items.Any(o => Math.Sqrt(Sq(o.CenterX - ax) + Sq(o.CenterY - cy)) < clear || Math.Sqrt(Sq(o.CenterX - bx) + Sq(o.CenterY - cy)) < clear)) continue;
                    x0 = x; y0 = y; return true;
                }
            x0 = y0 = 0; return false;
        }

        static double Sq(int d) => (double)d * d;

        static void ClientViewRock(List<Row> rows)
        {
            var w = Bare("L02", 7);
            ZObject rock = null; int sx = 0, sy = 0;
            foreach (var r in w.Objects.Where(o => o.Type == "rock"))
            {
                int tx = r.LocX / 16, ty = r.LocY / 16 + 4;
                if (w.Path.TilePassable(tx, ty, true) && w.Path.TilePassable(tx, ty + 1, true)) { rock = r; sx = tx; sy = ty; break; }
            }
            if (rock == null) { rows.Add(new Row { test = "client view: rock", expected = "-", measured = "no rock with free ground south of it", pass = false }); return; }
            var tough = Grunt(w, sx, sy, Team.Blue, "tough");
            var bot = new ZBot(w, Team.Blue, 7) { Ignored = true }; w.Bots.Add(bot);
            var legacy = new ZBot(w, Team.Blue, 7) { ClientView = false };
            w.SendWaypoints(Team.Blue, tough, new[] { new ZWaypoint { mode = WpMode.Attack, refId = rock.Id, x = rock.CenterX, y = rock.CenterY } });   // the player's order
            w.Tick(ZConst.TICK); w.Events.Clear();
            bool sent = tough.Waypoints.Count > 0 && tough.Waypoints[0].refId == rock.Id;
            var u = new List<ZObject>(); var tg = new List<ZObject>(); bot.CollectOurUnits_3(u, tg);
            var lu = new List<ZObject>(); var ltg = new List<ZObject>(); legacy.CollectOurUnits_3(lu, ltg);
            var k = bot.KnownWaypoints(tough);
            rows.Add(new Row { test = "client view: a Tough blasting a rock (player order) looks idle", expected = "order relayed to the bot; rock unknown to it -> idle; first port: busy",
                               measured = !sent ? "attack order refused" : $"known first {(k.Count > 0 ? k[0].mode.ToString() : "none")}; bot: {(u.Contains(tough) ? "idle" : "busy")}; first port: {(lu.Contains(tough) ? "idle" : "busy")}",
                               pass = sent && k.Count > 0 && k[0].refId == rock.Id && u.Contains(tough) && !lu.Contains(tough), note = "zbot_events.cpp:96-118 drops rocks, huts, map objects" });
        }

        // ------------------------------------------------------------------ factory exit (FORCE_MOVE ref -1 = idle; kept first)
        static void FactoryExit(List<Row> rows)
        {
            var w = Bare("L02", 8);
            var bot = new ZBot(w, Team.Blue, 8) { Ignored = true }; w.Bots.Add(bot);
            var fort = B(w, "fort_front", 14, 7);
            fort.BFinal = w.Time;                                                                           // finish the running Grunt now
            ZObject fresh = null;
            for (int i = 0; i < 5 && fresh == null; i++)
            {
                w.Tick(ZConst.TICK);
                foreach (var e in w.Events) if (e.type == ZEventType.UnitBuilt && e.otherId == fort.Id) fresh = w.Get(e.objId);
                w.Events.Clear();
            }
            if (fresh == null) { rows.Add(new Row { test = "factory exit", expected = "a Grunt squad", measured = "nothing built", pass = false }); return; }
            var u = new List<ZObject>(); var tg = new List<ZObject>(); bot.CollectOurUnits_3(u, tg);
            var k = bot.KnownWaypoints(fresh);
            var flag = w.Flags.First(f => f.Owner != Team.Blue);
            var units = new List<ZObject> { fresh }; var targets = new List<ZObject> { flag };
            bot.MatchTargets_3(units, targets); bot.GiveOutOrders_3(units, targets);
            string list = string.Join(" + ", fresh.Waypoints.Select(x => x.mode));
            rows.Add(new Row { test = "fresh squad leaving the fort (FORCE_MOVE exit) is idle and keeps its exit", expected = "known first ForceMove, idle; order -> ForceMove + Move",
                               measured = $"known first {(k.Count > 0 ? k[0].mode.ToString() : "none")}, {(u.Contains(fresh) ? "idle" : "busy")}; list after the order: {list}",
                               pass = k.Count > 0 && k[0].mode == WpMode.ForceMove && u.Contains(fresh) && fresh.Waypoints.Count == 2 && fresh.Waypoints[0].mode == WpMode.ForceMove && fresh.Waypoints[1].mode == WpMode.Move,
                               note = "zserver.cpp:1621-1634 relays the exit; zcore.cpp:540-551 keeps a non-overwritable first waypoint" });
        }

        // ------------------------------------------------------------------ ChooseBuildOrders (zbot.cpp:20-27, 1372-1432)
        static void Production(List<Row> rows)
        {
            var w = Bare("L02", 9);
            var bot = new ZBot(w, Team.Blue, 9);
            var fort = B(w, "fort_front", 14, 7); var rf = B(w, "robot_factory", 7, 13); var vf = B(w, "vehicle_factory", 46, 22);
            vf.Owner = Team.Blue;
            string Pick(ZObject b, int level)
            {
                b.Level = level; b.StopBuildingProduction(); b.SetBuildingDefaultProduction();
                bot.ChooseBuildOrders();
                return b.CurUnit ?? "none";
            }
            string Seq(ZObject b) => string.Join(" ", Enumerable.Range(0, 6).Select(l => Pick(b, l)));
            string sf = Seq(fort), sr = Seq(rf), sv = Seq(vf);
            const string ef = "jeep light medium medium medium medium", er = "grunt psycho tough pyro pyro pyro", ev = "jeep light medium medium medium medium";
            rows.Add(new Row { test = "CPU production by level 0-5: fort / robot factory / vehicle factory", expected = $"{ef} / {er} / {ev}", measured = $"{sf} / {sr} / {sv}",
                               pass = sf == ef && sr == er && sv == ev, note = "research 6.3; robot factory L0 keeps its default Grunt" });
            int before = bot.BuildChanges; bot.ChooseBuildOrders(); bot.ChooseBuildOrders();
            rows.Add(new Row { test = "no STOP/START while already building the preferred unit", expected = "0 changes", measured = $"{bot.BuildChanges - before} changes", pass = bot.BuildChanges == before });
            var order = string.Join(",", ZBot.Preferred);
            rows.Add(new Row { test = "preferred build list", expected = "medium,light,jeep,pyro,tough,sniper,tough,psycho", measured = order, pass = order == "medium,light,jeep,pyro,tough,sniper,tough,psycho", note = "zbot.cpp:20-27" });
        }

        // ------------------------------------------------------------------ ignored (Manage Bots Off)
        static void IgnoredBot(List<Row> rows)
        {
            var w = World("L02", 10);
            var bot = ZBot.Attach(w, Team.Red, false, 10)[0]; bot.Ignored = true;
            while (w.Time < 30) { w.Tick(ZConst.TICK); w.Events.Clear(); }
            rows.Add(new Row { test = "ignored bot (Manage Bots: Off)", expected = "0 orders, 0 production changes in 30 s", measured = $"{bot.Orders} orders, {bot.BuildChanges} changes", pass = bot.Orders == 0 && bot.BuildChanges == 0, note = "zbot.cpp:111-112" });
        }

        // ------------------------------------------------------------------ demo switch
        static void DemoSwitch(List<Row> rows)
        {
            var w = World("L02", 11);
            ZBot.Attach(w, Team.Red, false, 11);
            string s1 = string.Join(",", w.Bots.Select(b => b.Team));
            ZBot.Attach(w, Team.Red, true, 11);
            string s2 = string.Join(",", w.Bots.Select(b => b.Team));
            ZBot.SetDemo(w, Team.Red, false);
            bool redIgnored = w.Bots.First(b => b.Team == Team.Red).Ignored, blueActive = !w.Bots.First(b => b.Team == Team.Blue).Ignored;
            ZBot.SetDemo(w, Team.Red, true);
            bool redBack = !w.Bots.First(b => b.Team == Team.Red).Ignored;
            rows.Add(new Row { test = "demo switch: Attach(human Red) then Attach(demo) then SetDemo off/on", expected = "Blue; Blue,Red; red CPU paused then back, blue always on",
                               measured = $"{s1}; {s2}; red paused {redIgnored}, back {redBack}, blue on {blueActive}", pass = s1 == "Blue" && s2 == "Blue,Red" && redIgnored && redBack && blueActive,
                               note = "ZGame: blueBot/redBot or PlayerPrefs oz_botmatch (menu OpenZ/Bot Demo Mode)" });
        }

        // ------------------------------------------------------------------ grenade crates + empty vehicles/cannons, end to end
        static void Collectors(List<Row> rows)
        {
            var w = World("L02", 12);
            var bot = ZBot.Attach(w, Team.Red, false, 12)[0];
            int nades = 0, entered = 0, caps = 0;
            while (w.Time < 300)
            {
                w.Tick(ZConst.TICK);
                foreach (var e in w.Events)
                {
                    var o = w.GetAny(e.objId);
                    if (e.type == ZEventType.GrenadesPicked && o != null && o.Owner == Team.Blue) nades++;
                    if (e.type == ZEventType.Entered && o != null && o.Owner == Team.Blue) entered++;
                    if (e.type == ZEventType.FlagCaptured && (Team)(int)e.value == Team.Blue) caps++;
                }
                w.Events.Clear();
            }
            int M(WpMode m) => bot.OrdersByMode.TryGetValue(m, out int n) ? n : 0;
            rows.Add(new Row { test = "CPU (blue) vs idle red, L02, 300 s: grenade crates, empty vehicles/cannons, flags", expected = ">= 1 pickup order and pickup, >= 1 enter order and capture, >= 1 flag, 0 rejected",
                               measured = $"orders move {M(WpMode.Move)}, pickup {M(WpMode.PickupGrenades)}, enter {M(WpMode.Enter)}, fort {M(WpMode.EnterFort)}, attack {M(WpMode.Attack)}; blue picked {nades} crates, entered {entered}, captured {caps} flags; rejected {bot.RejectedOrders}",
                               pass = M(WpMode.PickupGrenades) >= 1 && nades >= 1 && M(WpMode.Enter) >= 1 && entered >= 1 && caps >= 1 && bot.RejectedOrders == 0,
                               note = "the CPU collects grenades (Zod targets every crate for robots without grenades)" });
        }

        // ------------------------------------------------------------------ bot vs bot (demo mode)
        static void BotVsBot(List<Row> rows, string lv, int seed, double maxSeconds)
        {
            var w = World(lv, seed);
            bool crates = w.Objects.Any(o => o.Type == "grenades");
            var bots = ZBot.Attach(w, Team.Red, true, seed);
            var red = bots.First(b => b.Team == Team.Red); var blue = bots.First(b => b.Team == Team.Blue);
            var caps = new Dictionary<Team, int> { [Team.Red] = 0, [Team.Blue] = 0, [Team.Neutral] = 0 };
            var nades = new Dictionary<Team, int> { [Team.Red] = 0, [Team.Blue] = 0, [Team.Neutral] = 0 };
            var entered = new Dictionary<Team, int> { [Team.Red] = 0, [Team.Blue] = 0, [Team.Neutral] = 0 };
            int fortIn = 0; double end = -1; Team winner = Team.Neutral;
            while (w.Time < maxSeconds)
            {
                w.Tick(ZConst.TICK);
                foreach (var e in w.Events)
                {
                    var o = w.GetAny(e.objId);
                    switch (e.type)
                    {
                        case ZEventType.FlagCaptured: caps[(Team)(int)e.value]++; break;
                        case ZEventType.GrenadesPicked: if (o != null) nades[o.Owner]++; break;
                        case ZEventType.Entered: if (o != null) entered[o.Owner]++; break;
                        case ZEventType.FortEntered: fortIn++; break;
                        case ZEventType.GameOver: end = w.Time; winner = e.value < 0 ? Team.Neutral : (Team)(int)e.value; break;
                    }
                }
                w.Events.Clear();
                if (w.GameOver && w.Time > end + 2) break;
            }
            string Modes(ZBot b) => string.Join(" ", b.OrdersByMode.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}:{kv.Value}"));
            rows.Add(new Row { test = $"bot vs bot {lv} seed {seed} ({maxSeconds:0} s max)", expected = $"both CPUs order and capture flags, {(crates ? "crates and " : "")}vehicles taken, 0 rejected orders",
                               measured = $"{(end >= 0 ? $"winner {winner} at {end:F0} s" : "no winner yet")}; flags R{caps[Team.Red]} B{caps[Team.Blue]}; crates R{nades[Team.Red]} B{nades[Team.Blue]}; entered R{entered[Team.Red]} B{entered[Team.Blue]}; fort entries {fortIn}; " +
                                          $"red {red.Orders} orders ({Modes(red)}), blue {blue.Orders} ({Modes(blue)}); rejected {red.RejectedOrders + blue.RejectedOrders}",
                               pass = red.Orders > 0 && blue.Orders > 0 && caps[Team.Red] > 0 && caps[Team.Blue] > 0 && red.RejectedOrders + blue.RejectedOrders == 0 && (!crates || nades[Team.Red] + nades[Team.Blue] > 0) && entered[Team.Red] + entered[Team.Blue] > 0,
                               note = crates ? "" : "no grenade crates on this map" });
        }

        // ------------------------------------------------------------------ same seed, same game
        static void Determinism(List<Row> rows)
        {
            string Game()
            {
                var w = World("L02", 13);
                var bots = ZBot.Attach(w, Team.Red, true, 13);
                foreach (var b in bots) b.Verbose = true;
                while (w.Time < 120) { w.Tick(ZConst.TICK); w.Events.Clear(); }
                return string.Join("\n", bots.SelectMany(b => b.Log));
            }
            string a = Game(), b2 = Game();
            rows.Add(new Row { test = "determinism: two L02 bot-vs-bot runs, seed 13, 120 s", expected = "identical order logs", measured = a == b2 ? $"identical ({a.Split('\n').Length} lines)" : "different", pass = a == b2 && a.Length > 0 });
        }
    }
}
