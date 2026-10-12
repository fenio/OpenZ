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
    /// Z parity suite (plan/04 section 5b), movement part: runs the Zod simulation headless on the real maps and measures
    /// </summary>
    public static partial class OpenZParity
    {
        static string Report => System.IO.Path.Combine(OpenZPaths.Records, "parity_report.md");
        static string SettingsText => Resources.Load<TextAsset>("OpenZ/default_settings").text;
        static string MapText(string lv) => Resources.Load<TextAsset>($"OpenZ/Maps/{lv}_grid").text;

        class Row { public string test, expected, measured, note; public bool pass; }

        [MenuItem("OpenZ/Run Parity Report")]
        public static string Run()
        {
            var rows = new List<Row>();
            bool snipeDeviation = ZWorld.SnipeDeviation; ZWorld.SnipeDeviation = false;   // DC2: the parity report measures Zod exact
            bool fortGun = ZWorld.FortGunDeviation; ZWorld.FortGunDeviation = false;     // DV7: forts have no weapon in Zod
            bool duelDev = ZWorld.DuelDeviation; ZWorld.DuelDeviation = false;           // DV9: Zod's attacker stops to fire
            bool vetDev = ZWorld.VeteranDeviation; ZWorld.VeteranDeviation = false;       // veterancy_v1 DV12: Zod has no ranks
            bool grabDev = ZWorld.GrabPolicyDeviation; ZWorld.GrabPolicyDeviation = false; // DV38: Zod's idle auto-grab
            bool nadeDev = ZWorld.GrenadeRuleDeviation; ZWorld.GrenadeRuleDeviation = false; // DV40: Zod throws grenades at anything
            bool toughPrep = ZWorld.ToughPrepareDeviation; ZWorld.ToughPrepareDeviation = false;   // weapons_v2 DV11: Zod fires on the engage tick
            bool nbMortar = ZWorld.FortMortarDeviation, nbReach = ZWorld.BuildingReachDeviation, nbExposed = ZWorld.ExposedDriverDeviation,
                 nbApproach = ZWorld.ApproachDeviation, nbChip = ZWorld.ArmourChipDeviation;   // combat_nb_v1 DV25-DV29: the Zod rows measure Zod
            ZWorld.FortMortarDeviation = ZWorld.BuildingReachDeviation = ZWorld.ExposedDriverDeviation = ZWorld.ApproachDeviation = ZWorld.ArmourChipDeviation = false;
            bool heavyPair = ZWorld.HeavyPairDeviation, craneGun = ZWorld.CraneGunDeviation; ZWorld.HeavyPairDeviation = false; ZWorld.CraneGunDeviation = false;   // vehicles_nb_v1 DV30 / DV31: Zod exact
            bool bnRush = ZWorld.FortRushDeviation, bnBoard = ZWorld.BoardNearestDeviation, bnParked = ZWorld.ParkedPathDeviation, bnCarPark = ZWorld.CarParkBotDeviation;   // boarding_nb_v1 DV34-DV37: Zod exact
            ZWorld.FortRushDeviation = ZWorld.BoardNearestDeviation = ZWorld.ParkedPathDeviation = ZWorld.CarParkBotDeviation = false;
            bool fdDefender = ZWorld.FortDefenderDeviation; ZWorld.FortDefenderDeviation = false;   // fort_defender_v1 DV41: forts have no crew in Zod
            bool pcCommit = ZWorld.PathV3Deviation; ZWorld.PathV3Deviation = false;       // pathing_v3 DV44 + DV47: the Zod rows measure Zod movement
            bool cfKnock = ZWorld.KnockHoldDeviation, cfRocket = ZWorld.RocketSpeedDeviation; ZWorld.KnockHoldDeviation = ZWorld.RocketSpeedDeviation = false;
            bool acHelp = ZWorld.HelpDeviation, acRun = ZWorld.CaptureRunDeviation, acGrab = ZWorld.CloseGrabDeviation, acBot = ZWorld.BotCommitDeviation;
            ZWorld.HelpDeviation = ZWorld.CaptureRunDeviation = ZWorld.CloseGrabDeviation = ZWorld.BotCommitDeviation = false;
            bool fomDev = ZWorld.FireOnMoveDeviation; ZWorld.FireOnMoveDeviation = false;
            bool brLock = ZWorld.EjectLockDeviation, brFall = ZWorld.EnterFallbackDeviation; ZWorld.EjectLockDeviation = ZWorld.EnterFallbackDeviation = false;
            bool caeQueue = ZWorld.CtrlAttackEmptyDeviation; ZWorld.CtrlAttackEmptyDeviation = false;   // DV81: the Zod rows keep Zod's clean-up after a shot-out crew (the first waypoint only)
            bool rpPad = ZWorld.RepairInPlaceDeviation; ZWorld.RepairInPlaceDeviation = false;
            bool runOver = ZWorld.RunOverDeviation; ZWorld.RunOverDeviation = false;
            bool unmannedStop = ZWorld.UnmannedStopDeviation; ZWorld.UnmannedStopDeviation = false;   // DV79: the Zod rows measure Zod's server-level stop alone (a Play session leaves the switch on)
            bool riflePair = ZWorld.RiflePairDeviation; ZWorld.RiflePairDeviation = false;       // DV78: Zod's rifle damage in the Zod rows
            // so Zod's end check (at most one team with units) ends them on the first tick and DV10 then halted every mover: the movement,
            // grenade and REAL-clock rows measured 0.000. DV10 is measured on its own in the Deviations block (DevEndFreeze).
            bool elbDev = ZWorld.ElectrobotDeviation, elbBot = ZWorld.ElectrobotBotDeviation; ZWorld.ElectrobotDeviation = ZWorld.ElectrobotBotDeviation = false;   // electrobot_v1 DV45 / DV46: Zod's six robots
            bool endFreeze = ZWorld.EndFreezeDeviation; ZWorld.EndFreezeDeviation = false;
            try
            {
                SettingsTests(rows);
                MapTests(rows, "L02", 244, 9, 7);
                MapTests(rows, "L17", 421, 16, 14);
                MovementTests(rows);
                ZWorld.PathV3Deviation = pcCommit;                                        // pathing_v3: the OpenZ squad rows measure the squads as shipped
                try { SquadTests(rows); if (pcCommit) PathCommitSquadRows(rows); } finally { ZWorld.PathV3Deviation = false; }   // (+ squad-minion pace with v3 on: OpenZParity.PathV3Rows.cs; v3 off = the live rows only)
                VehicleWaterTest(rows);
                RunTest(rows);
                CombatTests(rows);
                RulesTests(rows);
                ZodReferenceTests(rows);                                            // parity P8: Zod-source constants + real-clock measurements (OpenZParity.Zod.cs)
            }
            catch (Exception ex) { rows.Add(new Row { test = "parity suite ran to the end", expected = "no exception", measured = ex.GetType().Name + ": " + ex.Message, pass = false }); }
            finally { ZWorld.EndFreezeDeviation = endFreeze; }
            ZWorld.SnipeDeviation = snipeDeviation; ZWorld.FortGunDeviation = fortGun; ZWorld.DuelDeviation = duelDev; ZWorld.GrabPolicyDeviation = grabDev; ZWorld.GrenadeRuleDeviation = nadeDev;
            ZWorld.ToughPrepareDeviation = toughPrep;                                        // weapons_v2 DV11
            ZWorld.VeteranDeviation = vetDev;                                                // veterancy_v1 DV12
            ZWorld.ElectrobotDeviation = elbDev; ZWorld.ElectrobotBotDeviation = elbBot;     // electrobot_v1 DV45 / DV46
            ZWorld.FortMortarDeviation = nbMortar; ZWorld.BuildingReachDeviation = nbReach; ZWorld.ExposedDriverDeviation = nbExposed;
            ZWorld.ApproachDeviation = nbApproach; ZWorld.ArmourChipDeviation = nbChip;       // combat_nb_v1
            ZWorld.HeavyPairDeviation = heavyPair; ZWorld.CraneGunDeviation = craneGun;      // vehicles_nb_v1 DV30 / DV31
            ZWorld.FortRushDeviation = bnRush; ZWorld.BoardNearestDeviation = bnBoard; ZWorld.ParkedPathDeviation = bnParked; ZWorld.CarParkBotDeviation = bnCarPark;   // boarding_nb_v1
            ZWorld.FortDefenderDeviation = fdDefender;                                                 // fort_defender_v1 DV41
            ZWorld.PathV3Deviation = pcCommit;                                                     // pathing_v3 DV44 + DV47
            ZWorld.KnockHoldDeviation = cfKnock; ZWorld.RocketSpeedDeviation = cfRocket;
            ZWorld.HelpDeviation = acHelp; ZWorld.CaptureRunDeviation = acRun; ZWorld.CloseGrabDeviation = acGrab; ZWorld.BotCommitDeviation = acBot;   // DV53-DV56
            ZWorld.FireOnMoveDeviation = fomDev;                                                        // DV57
            ZWorld.EjectLockDeviation = brLock; ZWorld.EnterFallbackDeviation = brFall;                 // DV58 / DV59
            ZWorld.CtrlAttackEmptyDeviation = caeQueue;                                                 // DV81
            ZWorld.RepairInPlaceDeviation = rpPad;                                                      // DV62
            ZWorld.RunOverDeviation = runOver;                                                          // DV65
            ZWorld.UnmannedStopDeviation = unmannedStop;                                                // DV79
            ZWorld.RiflePairDeviation = riflePair;                                                      // DV78
            var sb = new StringBuilder();
            int pass = rows.Count(r => r.pass), fail = rows.Count - pass;
            sb.AppendLine("# OpenZ Z-parity report");
            sb.AppendLine();
            sb.AppendLine($"Generated {DateTime.Now:yyyy-MM-dd HH:mm} by `OpenZ/Run Parity Report` (headless Zod sim, 100 Hz ticks). " +
                          $"**{pass}/{rows.Count} PASS, {fail} FAIL**. Speeds in Zod px/s (16 px = 1 tile = 2 m); tolerance 2 %.");
            sb.AppendLine();
            // "| FAIL |" while the cells said **FAIL**), and the failing rows listed up front so nobody has to count them.
            sb.AppendLine(fail == 0 ? "Failing rows: none." : $"Failing rows ({fail}): " + string.Join("; ", rows.Where(r => !r.pass).Select(r => r.test)) + ".");
            sb.AppendLine();
            sb.AppendLine("| Test | Expected (Zod) | Measured (OpenZ) | Result | Note |");
            sb.AppendLine("|---|---|---|---|---|");
            foreach (var r in rows) sb.AppendLine($"| {r.test} | {r.expected} | {r.measured} | {(r.pass ? "PASS" : "FAIL")} | {r.note} |");
            var devs = CollectDeviations(PlayerPrefs.GetInt);                        // deliberate OpenZ changes, read + measured live (OpenZParity.Deviations.cs)
            sb.AppendLine();
            AppendDeviations(sb, devs);
            File.WriteAllText(Report, sb.ToString());
            string summary = $"parity: {pass}/{rows.Count} PASS, {fail} FAIL; {DeviationSummary(devs)}";
            Debug.Log(summary + " -> " + Report);
            return summary + "\n" + string.Join("\n", rows.Where(r => !r.pass).Select(r => $"FAIL {r.test}: {r.expected} vs {r.measured} {r.note}"));
        }

        // ------------------------------------------------------------------ settings + maps
        static void SettingsTests(List<Row> rows)
        {
            var s = ZSettings.Parse(SettingsText);
            // research table 1.1 (move_speed, group_amount, attack_radius) + truncated ints (0. conventions)
            var expect = new Dictionary<string, (int speed, int group, int radius)>
            {
                ["grunt"] = (14, 3, 120), ["psycho"] = (12, 3, 120), ["sniper"] = (14, 3, 144), ["tough"] = (12, 2, 120), ["pyro"] = (12, 4, 120),
                ["laser"] = (14, 4, 136), ["jeep"] = (17, 0, 120), ["light"] = (14, 0, 120), ["medium"] = (12, 0, 128), ["heavy"] = (9, 0, 144),
                ["apc"] = (14, 0, 0), ["missile_launcher"] = (6, 0, 160), ["crane"] = (14, 0, 0), ["gatling"] = (0, 0, 120), ["gun"] = (0, 0, 128),
                ["howitzer"] = (0, 0, 200), ["missile_cannon"] = (0, 0, 144)
            };
            foreach (var kv in expect)
            {
                var u = s.Unit(kv.Key);
                bool ok = u.move_speed == kv.Value.speed && u.group_amount == kv.Value.group && u.attack_radius == kv.Value.radius;
                rows.Add(new Row { test = $"settings {kv.Key}: move_speed / group / attack_radius", expected = $"{kv.Value.speed} / {kv.Value.group} / {kv.Value.radius}",
                                   measured = $"{u.move_speed} / {u.group_amount} / {u.attack_radius}", pass = ok });
            }
            var g = s.Unit("grunt");
            rows.Add(new Row { test = "grunt damage per hit (int)(0.001105 x 10000)", expected = "11", measured = g.Damage.ToString(), pass = g.Damage == 11 });
            rows.Add(new Row { test = "grunt max health (int)(0.108108 x 10000)", expected = "1081", measured = g.MaxHealth.ToString(), pass = g.MaxHealth == 1081 });
            rows.Add(new Row { test = "CensorSettings: grunt missile speed / tough hit chance", expected = "0 / 0", measured = $"{g.attack_missile_speed} / {s.Unit("tough").attack_damage_chance}",
                               pass = g.attack_missile_speed == 0 && s.Unit("tough").attack_damage_chance == 0 });
            rows.Add(new Row { test = "globals: run speed / recharge / agro", expected = "1.8 / 0.3 / 40", measured = $"{s.run_unit_speed} / {s.run_recharge_rate} / {s.agro_distance}",
                               pass = Math.Abs(s.run_unit_speed - 1.8) < 1e-9 && Math.Abs(s.run_recharge_rate - 0.3) < 1e-9 && s.agro_distance == 40 });
        }

        static void MapTests(List<Row> rows, string lv, int objs, int zones, int flags)
        {
            var w = ZWorld.Create(SettingsText, MapText(lv));
            int f = w.Flags.Count;
            rows.Add(new Row { test = $"{lv} map load: objects / zones / flags", expected = $"{objs} / {zones} / {flags}",
                               measured = $"{w.Map.objects.Count} / {w.Map.zones.Count} / {f}", pass = w.Map.objects.Count == objs && w.Map.zones.Count == zones && f == flags });
            var leaders = w.Objects.Where(o => o.Kind == ZKind.Robot && !o.IsMinion).ToList();
            bool squads = leaders.All(l => l.Minions.Count + 1 == w.S.Unit(l.Type).group_amount);
            rows.Add(new Row { test = $"{lv} map robots = full squads (one map robot = one squad)", expected = "all leaders have group_amount - 1 minions",
                               measured = $"{leaders.Count} squads, {w.Objects.Count(o => o.Kind == ZKind.Robot)} robots", pass = squads });
        }

        // ------------------------------------------------------------------ movement
        static readonly string[] Movers = { "grunt", "psycho", "sniper", "tough", "pyro", "laser", "jeep", "light", "medium", "heavy", "apc", "missile_launcher", "crane" };

        static void MovementTests(List<Row> rows)
        {
            var probe = ZWorld.Create(SettingsText, MapText("L02"));
            foreach (var type in Movers)
            {
                bool robot = ZSettings.Robots.Contains(type);
                foreach (var cls in robot ? new[] { ZClass.Ground, ZClass.Road, ZClass.Water } : new[] { ZClass.Ground, ZClass.Road })
                {
                    var w = Legacy(1);                                               // no enemies: nobody shoots, dodges or runs
                    if (!FindRun(w, cls, robot ? 1 : 2, 10, out int tx0, out int tx1, out int ty))
                    { rows.Add(new Row { test = $"{type} speed on {cls}", expected = "-", measured = "no test stretch found", pass = false }); continue; }
                    var u = w.CreateObject(robot ? "robot" : "vehicle", type, tx0 * 16, ty * 16, Team.Red);
                    w.SendWaypoints(Team.Red, u, new[] { ZWaypoint.Move(tx1 * 16 + (robot ? 8 : 16), u.CenterY) });
                    double factor = cls == ZClass.Road ? ZConst.ROAD_SPEED : cls == ZClass.Water ? ZConst.WATER_SPEED : 1.0;
                    double expected = u.MoveSpeed * factor;
                    double measured = MedianSpeed(w, u, cls, 40);
                    bool ok = Math.Abs(measured - expected) <= expected * 0.02;
                    rows.Add(new Row { test = $"{type} speed on {cls.ToString().ToLower()}", expected = $"{expected:F3} px/s ({u.MoveSpeed} x {factor})",
                                       measured = $"{measured:F3} px/s", pass = ok,
                                       note = $"{measured / 8:F2} m/s, stretch tiles ({tx0}..{tx1}, {ty})" });
                }
            }
        }

        /// <summary>Median per-tick centre speed while the centre tile (before and after the tick) is of class cls.</summary>
        static double MedianSpeed(ZWorld w, ZObject u, ZClass cls, double maxTime)
        {
            var speeds = new List<double>();
            double px = u.PosX, py = u.PosY;
            for (double t = 0; t < maxTime; t += ZConst.TICK)
            {
                var before = w.Map.ClassAt(u.CenterX >> 4, u.CenterY >> 4);
                w.Tick(ZConst.TICK);
                var after = w.Map.ClassAt(u.CenterX >> 4, u.CenterY >> 4);
                double d = Math.Sqrt((u.PosX - px) * (u.PosX - px) + (u.PosY - py) * (u.PosY - py));
                px = u.PosX; py = u.PosY;
                if (before == cls && after == cls && u.IsMoving && d > 0) speeds.Add(d / ZConst.TICK);
                if (u.Waypoints.Count == 0 && t > 1) break;
            }
            if (speeds.Count == 0) return 0;
            speeds.Sort();
            return speeds[speeds.Count / 2];
        }

        /// <summary>A horizontal run of `len`+ tiles of one class, `hgt` rows tall, free of dynamic barriers, with room around it.</summary>
        static bool FindRun(ZWorld w, ZClass cls, int hgt, int len, out int x0, out int x1, out int y)
        {
            var m = w.Map;
            for (int yy = 2; yy < m.height - hgt - 2; yy++)
                for (int xx = 2; xx < m.width - len - 2; xx++)
                {
                    int x = xx;
                    while (x < m.width - 2 && Enumerable.Range(0, hgt).All(k => m.ClassAt(x, yy + k) == cls && w.Path.TilePassable(x, yy + k, true))) x++;
                    if (x - xx >= len) { x0 = xx; x1 = x - 1; y = yy; return true; }
                }
            x0 = x1 = y = 0; return false;
        }

        // ------------------------------------------------------------------ squads, vehicles vs water, running
        static void SquadTests(List<Row> rows)
        {
            var w = ZWorld.Create(SettingsText, MapText("L02"));
            var lead = w.Objects.First(o => o.Type == "grunt" && o.Source != null && o.Source.x == 53 && o.Source.y == 67 && !o.IsMinion);
            w.SendWaypoints(Team.Red, lead, new[] { ZWaypoint.Move(53 * 16 + 8, 40 * 16 + 8, true) });
            bool cloned = lead.Minions.All(m => m.Waypoints.Count == 1 && m.Waypoints[0].Equals(lead.Waypoints[0]));
            bool rejected = !w.SendWaypoints(Team.Red, lead.Minions[0], new[] { ZWaypoint.Move(0, 0) });
            double maxSpread = 0, minGap = double.MaxValue;
            var squad = new[] { lead }.Concat(lead.Minions).ToList();
            for (int i = 0; i < 4000 && lead.Waypoints.Count > 0; i++)
            {
                w.Tick(ZConst.TICK);
                foreach (var mm in lead.Minions) maxSpread = Math.Max(maxSpread, Math.Abs(mm.CenterY - lead.CenterY));
                for (int a = 0; a < squad.Count; a++)
                    for (int b = a + 1; b < squad.Count; b++)
                        minGap = Math.Min(minGap, Math.Sqrt(Math.Pow(squad[a].CenterX - squad[b].CenterX, 2) + Math.Pow(squad[a].CenterY - squad[b].CenterY, 2)));
            }
            w.Tick(ZConst.TICK);
            bool stopped = lead.Minions.All(mm => mm.Waypoints.Count == 0 && !mm.IsMoving);
            bool spaced = w.Pathing != null && w.Pathing.Enabled;                   // OpenZ pathing layer: squads keep ZPathing.SquadSpacing instead of stacking
            // the squad walks single file (DV24 keep-clear), so the spread is (members - 1) x the squad gap; the old 32 px was written for the
            int squadGap = (int)Math.Round(ZPathing.SquadSpacing * ZFormation.BodyScale(lead.Type)), spreadMax = (squad.Count - 1) * squadGap + 8;
            rows.Add(new Row { test = "squad: order cloned to minions, orders to a minion rejected", expected = "cloned, rejected", measured = $"{(cloned ? "cloned" : "NOT cloned")}, {(rejected ? "rejected" : "accepted")}", pass = cloned && rejected });
            rows.Add(new Row { test = spaced ? "squad: minions walk behind the leader and stop with it (OpenZ pathing spacing replaces Zod's stacking)" : "squad: minions stop when the leader finishes (no formation offsets)",
                               expected = spaced ? $"stopped, spread <= {spreadMax} px ({squad.Count - 1} x {squadGap} px squad gap + 8), members >= 11 px apart" : "stopped, spread < 1 tile",
                               measured = $"{(stopped ? "stopped" : "still moving")}, max spread {maxSpread} px{(spaced ? $", closest members {minGap:F1} px" : "")}",
                               pass = stopped && (spaced ? maxSpread <= spreadMax && minGap >= 11 : maxSpread < 16),
                               note = spaced ? "research 6.3: Zod stacks a squad on one pixel; OpenZ keeps them apart (Sim/ZSeparation.cs, ZPathing.SquadSpacing); single file over the (51,58) bridge" : null });
        }

        static void VehicleWaterTest(List<Row> rows)
        {
            var w = ZWorld.Create(SettingsText, MapText("L02"));
            // jeep north of the wide river (rows 60-61), ordered straight south across it
            var j = w.CreateObject("vehicle", "jeep", 40 * 16, 56 * 16, Team.Red);
            w.SendWaypoints(Team.Red, j, new[] { ZWaypoint.Move(40 * 16 + 16, 65 * 16 + 16) });
            bool entered = false;
            for (int i = 0; i < 3000; i++)
            {
                w.Tick(ZConst.TICK);
                for (int dx = 0; dx < 2; dx++) for (int dy = 0; dy < 2; dy++)
                    if (w.Map.ClassAt((j.LocX + 1 + dx * 29) >> 4, (j.LocY + 1 + dy * 29) >> 4) == ZClass.Water) entered = true;
            }
            var g = w.CreateObject("robot", "grunt", 40 * 16, 56 * 16, Team.Red);
            w.SendWaypoints(Team.Red, g, new[] { ZWaypoint.Move(40 * 16 + 8, 65 * 16 + 8) });
            bool gruntWet = false;
            for (int i = 0; i < 4000 && g.Waypoints.Count > 0; i++) { w.Tick(ZConst.TICK); if (w.Map.ClassAt(g.CenterX >> 4, g.CenterY >> 4) == ZClass.Water) gruntWet = true; }
            rows.Add(new Row { test = "vehicles never enter water, robots wade through (jeep and grunt ordered across the L02 river)",
                               expected = "jeep dry, grunt crosses", measured = $"jeep {(entered ? "ENTERED water" : "dry")} at ({j.CenterX >> 4},{j.CenterY >> 4}), grunt {(gruntWet ? "crossed" : "did not wade")} to ({g.CenterX >> 4},{g.CenterY >> 4})",
                               pass = !entered && gruntWet && (g.CenterY >> 4) >= 64 });
        }

        // ------------------------------------------------------------------ combat (research 2.x-4.x, 8.7, 9, 12.10)
        /// <summary>An empty copy of L02 (terrain + buildings kept, other map objects removed) for clean duels.</summary>
        static ZWorld Legacy(int seed, bool keepRocks = false)
        {
            var w = ZWorld.Create(SettingsText, MapText("L02"), seed);
            w.Objects.RemoveAll(o => !(keepRocks && o.IsDestroyableImpass) && o.Kind != ZKind.Building);
            w.Flags.Clear();                                                         // no zones to grab, no production, no elimination
            foreach (var b in w.Objects) { b.StopBuildingProduction(); b.Owner = Team.Neutral; }
            return w;
        }

        static ZObject Dummy(ZWorld w, string cat, string type, int tx, int ty, Team team)
        {
            var o = cat == "robot" ? w.CreateRobotGroup(type, tx * 16, ty * 16, team, 1) : w.CreateObject(cat, type, tx * 16, ty * 16, team);
            o.MaxHealth = o.Health = 1000000000;
            o.Damage = 0;                                                            // a target that never shoots back or agros
            return o;
        }

        static void CombatTests(List<Row> rows)
        {
            // hitscan: hit rate, damage per hit, cooldown (attacker vs a stationary unkillable enemy on open ground)
            foreach (var (cat, type) in new[] { ("robot", "grunt"), ("robot", "psycho"), ("robot", "sniper"), ("robot", "pyro"), ("robot", "laser"), ("vehicle", "jeep"), ("cannon", "gatling") })
            {
                var w = Legacy(7);
                var a = cat == "robot" ? w.CreateRobotGroup(type, 56 * 16, 14 * 16, Team.Red, 1) : w.CreateObject(cat, type, 56 * 16, 14 * 16, Team.Red);
                var t = Dummy(w, "robot", "grunt", 61, 14, Team.Blue);
                int shots = 0, hits = 0, badDmg = 0, last = t.Health; double firstShot = -1, lastShot = -1;
                for (int i = 0; i < 400000 && shots < 3000; i++)
                {
                    w.Tick(ZConst.TICK);
                    foreach (var e in w.Events)
                        if (e.type == ZEventType.Hitscan && e.objId == a.Id)
                        {
                            shots++; if (firstShot < 0) firstShot = w.Time; lastShot = w.Time;
                            if (e.flag) { hits++; if (last - t.Health != a.Damage) badDmg++; }
                            last = t.Health;
                        }
                    w.Events.Clear();
                }
                double rate = (double)hits / shots, cd = (lastShot - firstShot) / (shots - 1);
                rows.Add(new Row { test = $"{type}: hit rate over {shots} shots", expected = $"{a.DamageChance:P0}", measured = $"{rate:P1}", pass = Math.Abs(rate - a.DamageChance) <= 0.02 });
                rows.Add(new Row { test = $"{type}: damage per hit / cooldown", expected = $"{a.Damage} HP / {a.AttackSpeed:F3} s", measured = $"{(badDmg == 0 ? a.Damage.ToString() : badDmg + " wrong")} HP / {cd:F3} s",
                                   pass = badDmg == 0 && Math.Abs(cd - a.AttackSpeed) <= 0.011, note = "cooldown rounds up to the next 10 ms tick like Zod" });
            }
            // sniping share: sniper vs a crewed blue jeep, driver refilled after each driver hit
            {
                var w = Legacy(8);
                var a = w.CreateRobotGroup("sniper", 56 * 16, 14 * 16, Team.Red, 1);
                var j = Dummy(w, "vehicle", "jeep", 61, 14, Team.Blue);
                int hits = 0, snipes = 0;
                for (int i = 0; i < 400000 && hits < 3000; i++)
                {
                    w.Tick(ZConst.TICK);
                    foreach (var e in w.Events) if (e.type == ZEventType.Hitscan && e.objId == a.Id && e.flag) { hits++; if (e.flag2) snipes++; }
                    w.Events.Clear(); j.Drivers[0] = 1000000;
                }
                double share = (double)snipes / hits;
                rows.Add(new Row { test = $"sniper vs crewed jeep: driver share of {hits} hits", expected = "80.0 %", measured = $"{share:P1}", pass = Math.Abs(share - 0.8) <= 0.02 });
            }
            // splash: linear falloff, no friendly fire, neutral debris hurts everyone
            {
                var w = Legacy(9);
                var red = Dummy(w, "robot", "grunt", 56, 14, Team.Red); var blue = Dummy(w, "robot", "grunt", 64, 14, Team.Blue);
                int h0 = blue.Health, r0 = red.Health;
                w.AddMissileForTest(new ZMissile { x = blue.CenterX, y = blue.CenterY, damage = 1666, radius = 40, team = Team.Red, explodeTime = 0 });
                w.AddMissileForTest(new ZMissile { x = red.CenterX, y = red.CenterY, damage = 1666, radius = 40, team = Team.Red, explodeTime = 0 });
                w.Tick(ZConst.TICK); w.Tick(ZConst.TICK);
                int full = h0 - blue.Health, own = r0 - red.Health; h0 = blue.Health;
                w.AddMissileForTest(new ZMissile { x = blue.CenterX + 20, y = blue.CenterY, damage = 1666, radius = 40, team = Team.Red, explodeTime = 0 });
                w.Tick(ZConst.TICK); w.Tick(ZConst.TICK);
                int half = h0 - blue.Health; r0 = red.Health;
                w.AddMissileForTest(new ZMissile { x = red.CenterX, y = red.CenterY, damage = 40, radius = 40, team = Team.Neutral, debris = true, explodeTime = 0 });
                w.Tick(ZConst.TICK); w.Tick(ZConst.TICK);
                int debris = r0 - red.Health;
                rows.Add(new Row { test = "splash: 1666 HP r40 at centre / at R/2 / own team / neutral debris 40 HP", expected = "1666 / 833 / 0 / 40",
                                   measured = $"{full} / {half} / {own} / {debris}", pass = full == 1666 && half == 833 && own == 0 && debris == 40 });
            }
            // H0 round 2: splash rounding at awkward distances = Zod's float maths (zserver.cpp:1091-1098: float mag, float / int ratio,
            // then double). Fixed literals from IEEE single precision (x86-64 Zod); a double-precision ratio gives 400 / 679 / 99 / 800 / 400.
            {
                var w = Legacy(9);
                var blue = Dummy(w, "robot", "grunt", 64, 14, Team.Blue);
                var got = new List<int>();
                foreach (var (dx, dy) in new[] { (30, 0), (16, 0), (45, 0), (10, 0), (18, 24) })
                {
                    int h0 = blue.Health;
                    w.AddMissileForTest(new ZMissile { x = blue.CenterX + dx, y = blue.CenterY + dy, damage = 1000, radius = 50, team = Team.Red, explodeTime = 0 });
                    w.Tick(ZConst.TICK); w.Tick(ZConst.TICK);
                    got.Add(h0 - blue.Health);
                }
                var zod = new[] { 399, 680, 100, 799, 399 };
                rows.Add(new Row { test = "splash rounding like Zod's float maths: 1000 HP r50 at 30 / 16 / 45 / 10 px / (18,24)", expected = string.Join(" / ", zod),
                                   measured = string.Join(" / ", got), pass = got.SequenceEqual(zod),
                                   note = "Zod: float mag / int radius = float, then 1.0 - ratio in double (zserver.cpp:1098); 18 of 50 distances differ by 1 HP if the ratio stays double" });
            }
            // tank lid opens ~80 % of engagements against snipe-capable units
            {
                var w = Legacy(10);
                var tank = Dummy(w, "vehicle", "light", 56, 14, Team.Red); var g = Dummy(w, "robot", "grunt", 64, 15, Team.Blue);
                int open = 0, n = 4000;
                for (int i = 0; i < n; i++) { tank.Disengage(); w.Tick(2.0); tank.Engage(g); if (tank.LidOpen) open++; }
                double p = (double)open / n;
                rows.Add(new Row { test = "light tank lid open when engaging a sniper-capable unit", expected = "80 % (code, not the 1-in-3 comment)", measured = $"{p:P1}", pass = Math.Abs(p - 0.8) <= 0.02 });
            }
            // a sniped driver empties the jeep; any robot captures it, only the leader drives
            {
                var w = Legacy(11);
                var jeep = w.CreateObject("vehicle", "jeep", 60 * 16, 14 * 16, Team.Blue); jeep.Drivers[0] = 60; jeep.Damage = 0;
                w.CreateRobotGroup("sniper", 56 * 16, 14 * 16, Team.Red, 1);
                bool sniped = false;
                for (int i = 0; i < 6000 && !sniped; i++) { w.Tick(ZConst.TICK); foreach (var e in w.Events) if (e.type == ZEventType.Sniped && e.objId == jeep.Id) sniped = true; w.Events.Clear(); }
                var owner1 = jeep.Owner;
                var gr = w.CreateRobotGroup("grunt", 64 * 16, 15 * 16, Team.Red);
                var minions = gr.Minions.ToList();
                w.SendWaypoints(Team.Red, gr, new[] { new ZWaypoint { mode = WpMode.Enter, refId = jeep.Id, x = jeep.CenterX, y = jeep.CenterY } });
                for (int i = 0; i < 3000 && jeep.Owner != Team.Red; i++) w.Tick(ZConst.TICK);
                for (int i = 0; i < 3; i++) w.Tick(ZConst.TICK);
                bool leaderGone = w.Get(gr.Id) == null, minionsStay = minions.All(m => w.Get(m.Id) != null);
                rows.Add(new Row { test = "sniped jeep turns neutral, any robot captures it (leader drives, squad stays out)", expected = "neutral -> Red, grunt driver, 2 minions outside",
                                   measured = $"{(sniped ? "sniped" : "not sniped")} -> {owner1} -> {jeep.Owner}, driver {jeep.DriverType} x{jeep.Drivers.Count}, leader {(leaderGone ? "inside" : "outside")}, minions {(minionsStay ? "outside" : "gone")}",
                                   pass = sniped && owner1 == Team.Neutral && jeep.Owner == Team.Red && jeep.Drivers.Count == 1 && leaderGone && minionsStay });
            }
            // grenades: grunts cannot target a rock; a crate gives 20; the squad throws from the leader's pool; rock destroyed
            {
                var w = Legacy(12, keepRocks: true);
                var rock = w.Objects.First(o => o.Type == "rock" && o.LocX / 16 == 56 && o.LocY / 16 + 2 == 18);    // the rock pile east of the red road
                var crate = w.CreateObject("map_item", "grenades", 54 * 16, 17 * 16, Team.Neutral);
                var g = w.CreateRobotGroup("grunt", 50 * 16, 16 * 16, Team.Red);
                w.SendWaypoints(Team.Red, g, new[] { new ZWaypoint { mode = WpMode.Attack, refId = rock.Id } });
                bool rejected = g.Waypoints.Count == 0;
                w.SendWaypoints(Team.Red, g, new[] { new ZWaypoint { mode = WpMode.PickupGrenades, refId = crate.Id, x = crate.CenterX, y = crate.CenterY } });
                for (int i = 0; i < 3000 && g.Grenades == 0; i++) w.Tick(ZConst.TICK);
                w.Tick(ZConst.TICK); w.Tick(ZConst.TICK);
                int got = g.Grenades; bool crateGone = w.Get(crate.Id) == null;
                w.SendWaypoints(Team.Red, g, new[] { new ZWaypoint { mode = WpMode.Attack, refId = rock.Id, x = rock.CenterX, y = rock.CenterY } });
                int throws = 0, byMinion = 0, nadeDmg = 0, nadeR = 0; bool dead = false;
                for (int i = 0; i < 6000 && !dead; i++)
                {
                    w.Tick(ZConst.TICK);
                    foreach (var e in w.Events)
                    {
                        if (e.type == ZEventType.MissileFired && e.flag) { throws++; if (e.objId != g.Id) byMinion++; }
                        if (e.type == ZEventType.MissileExploded && e.flag) { nadeDmg = (int)e.value; nadeR = e.radius; }
                        if (e.type == ZEventType.Death && e.objId == rock.Id) dead = true;
                    }
                    w.Events.Clear();
                }
                rows.Add(new Row { test = "grenades: grunt cannot target a rock; crate gives 20; squad throws from the leader's pool; rock destroyed",
                                   expected = "rejected, 20, crate removed, minions throw, 1666 HP r30, destroyed",
                                   measured = $"{(rejected ? "rejected" : "ACCEPTED")}, {got}, crate {(crateGone ? "removed" : "left")}, {throws} throws ({byMinion} by minions), {nadeDmg} HP r{nadeR}, rock {(dead ? "destroyed" : "standing")}",
                                   pass = rejected && got == 20 && crateGone && byMinion > 0 && nadeDmg == 1666 && nadeR == 30 && dead });
                int tx = rock.LocX / 16, ty = rock.LocY / 16 + 2;
                for (int i = 0; i < 3; i++) w.Tick(ZConst.TICK);
                rows.Add(new Row { test = "destroyed rock frees its tile", expected = "passable", measured = w.Path.TilePassable(tx, ty, true) ? "passable" : "blocked", pass = w.Path.TilePassable(tx, ty, true) });
            }
            // dodge: the missile's target gets a DODGE waypoint of 1.0-1.5 x (time to impact x speed)
            {
                var w = Legacy(13);
                var ml = w.CreateObject("vehicle", "missile_launcher", 56 * 16, 14 * 16, Team.Red);
                var t = Dummy(w, "vehicle", "jeep", 64, 14, Team.Blue);
                bool dodged = false; double ratio = 0;
                for (int i = 0; i < 3000 && !dodged; i++)
                {
                    w.Tick(ZConst.TICK);
                    foreach (var e in w.Events)
                        if (!dodged && e.type == ZEventType.MissileFired && e.objId == ml.Id && t.Waypoints.Count > 0 && t.Waypoints[0].mode == WpMode.Dodge)
                        {
                            dodged = true;
                            double d = Math.Sqrt(Math.Pow(t.Waypoints[0].x - t.CenterX, 2) + Math.Pow(t.Waypoints[0].y - t.CenterY, 2));
                            double baseD = e.value * t.MoveSpeed * (e.value > t.MaxStamina ? 1 : w.S.run_unit_speed);   // speed before the dodge run started (open ground)
                            ratio = d / baseD;
                        }
                    w.Events.Clear();
                }
                rows.Add(new Row { test = "missile target dodges (1.0-1.5 x flight time x speed)", expected = "dodge, ratio 1.0-1.5", measured = $"{(dodged ? "dodge" : "none")}, ratio {ratio:F2}", pass = dodged && ratio >= 0.98 && ratio <= 1.52 });
            }
            // eject: an ejectable cannon empties into a grunt squad and turns neutral; a fort turret cannon cannot eject
            {
                var w = Legacy(14);
                var c = w.CreateObject("cannon", "gatling", 58 * 16, 14 * 16, Team.Red);
                bool ok = w.Eject(Team.Red, c);
                int robots = w.Objects.Count(o => o.Kind == ZKind.Robot && o.Owner == Team.Red && o.JustLeftCannon);
                var full = ZWorld.Create(SettingsText, MapText("L02"));
                var turret = full.Objects.FirstOrDefault(o => o.Kind == ZKind.Cannon && !o.Ejectable);
                bool noEject = turret != null && !full.Eject(turret.Owner, turret);
                rows.Add(new Row { test = "eject: gatling crew pops out, cannon neutral; fort turret cannons cannot eject", expected = "1 grunt out, neutral, fort turret refused",
                                   measured = $"{(ok ? robots + " grunt out" : "failed")}, {c.Owner}, fort turret {(turret == null ? "none on map" : noEject ? "refused" : "EJECTED")}",
                                   pass = ok && robots == 1 && c.Owner == Team.Neutral && noEject });
            }
            // tank death: turret debris missile 40 HP r40, 3-4 s, team neutral
            {
                var w = Legacy(15);
                var tank = w.CreateObject("vehicle", "light", 60 * 16, 14 * 16, Team.Blue);
                double t0 = w.Time;
                tank.SetHealth(0); w.UpdateObjectHealth(tank);
                w.Tick(ZConst.TICK);
                var d = w.PendingMissiles.FirstOrDefault(m => m.debris);
                double delay = d == null ? 0 : d.explodeTime - t0;
                rows.Add(new Row { test = "light tank death: turret debris missile", expected = "40 HP r40, neutral, 3.00-3.99 s",
                                   measured = d == null ? "none" : $"{d.damage} HP r{d.radius}, {d.team}, {delay:F2} s",
                                   pass = d != null && d.damage == 40 && d.radius == 40 && d.team == Team.Neutral && delay >= 2.99 && delay <= 4.0 });
            }
        }

        static void RunTest(List<Row> rows)
        {
            // a grunt ordered onto a flag runs (x1.8) unless the 1-in-5 roll says no; try seeds until it runs
            for (int seed = 1; seed <= 10; seed++)
            {
                var w = ZWorld.Create(SettingsText, MapText("L02"), seed);
                var flag = w.Flags.OrderBy(f => f.Id).FirstOrDefault(f => w.Map.ClassAt((f.CenterX >> 4) - 6, f.CenterY >> 4) == ZClass.Ground && w.Map.ClassAt((f.CenterX >> 4) - 3, f.CenterY >> 4) == ZClass.Ground);
                if (flag == null) break;
                var g = w.CreateObject("robot", "grunt", ((flag.CenterX >> 4) - 7) * 16, (flag.CenterY >> 4) * 16, Team.Red);
                w.SendWaypoints(Team.Red, g, new[] { ZWaypoint.Move(flag.CenterX, flag.CenterY) });
                w.Tick(ZConst.TICK);
                if (!g.IsRunning) continue;
                double stamina0 = g.Stamina;
                double v = MedianSpeed(w, g, ZClass.Ground, 10);
                double expected = 14 * 1.8;
                rows.Add(new Row { test = "running to a flag: speed x run_unit_speed, stamina drains", expected = $"{expected:F2} px/s, stamina < {stamina0:F2}",
                                   measured = $"{v:F2} px/s, stamina {g.Stamina:F2}", pass = Math.Abs(v - expected) <= expected * 0.02 && g.Stamina < stamina0,
                                   note = $"seed {seed}; 1 in 5 attempts do not run (zobject.cpp:2095)" });
                return;
            }
            rows.Add(new Row { test = "running to a flag", expected = "runs", measured = "never ran in 10 seeds", pass = false });
        }
    }
}
