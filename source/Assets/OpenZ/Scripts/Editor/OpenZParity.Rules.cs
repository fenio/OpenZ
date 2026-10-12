using System;
using System.Collections.Generic;
using System.Linq;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>Z parity suite, rules part (research zod_buildings_production_territory.md): zones, flags, production,
    /// queue, cannons, repair station, crane, bridges, auto-repair, fort entry, elimination, game end, unit cap.</summary>
    public static partial class OpenZParity
    {
        /// <summary>The real map with its buildings, flags and items, minus the map's units (so nobody wanders off to a flag).</summary>
        static ZWorld RulesWorld(string lv, int seed, bool keepCannons = false)
        {
            var w = ZWorld.Create(SettingsText, MapText(lv), seed);
            w.Objects.RemoveAll(o => o.Kind == ZKind.Robot || o.Kind == ZKind.Vehicle || (!keepCannons && o.Kind == ZKind.Cannon));
            return w;
        }
        static ZObject B(ZWorld w, string type, int tx, int ty) => w.Objects.First(o => o.Type == type && o.LocX == tx * 16 && o.LocY == ty * 16);

        /// <summary>Ticks until pred (checked after every tick, events visible) or maxT seconds; events are cleared each tick.</summary>
        static bool Until(ZWorld w, double maxT, Func<ZEvent, bool> onEvent = null, Func<bool> pred = null)
        {
            double end = w.Time + maxT;
            while (w.Time < end)
            {
                w.Tick(ZConst.TICK);
                bool hit = false;
                if (onEvent != null) foreach (var e in w.Events) if (onEvent(e)) hit = true;
                w.Events.Clear();
                if (hit || (pred != null && pred())) return true;
            }
            return false;
        }

        static void Kill(ZWorld w, ZObject o) { o.SetHealth(0); w.UpdateObjectHealth(o); }

        /// <summary>The rules suite measures Zod's rules: the end freeze (DV10: no production after a win; these worlds have no units, so
        /// Zod's no-units rule ends them at once) and the fort gun (DV7: shot the fort-entry grunt) are off while it runs.</summary>
        static void RulesTests(List<Row> rows)
        {
            bool ef = ZWorld.EndFreezeDeviation, fg = ZWorld.FortGunDeviation;
            bool gp = ZWorld.GrabPolicyDeviation, gr = ZWorld.GrenadeRuleDeviation; ZWorld.GrenadeRuleDeviation = false; ZWorld.EndFreezeDeviation = false; ZWorld.FortGunDeviation = false; ZWorld.GrabPolicyDeviation = false;
            bool fdd = ZWorld.FortDefenderDeviation; ZWorld.FortDefenderDeviation = false;                 // fort_defender_v1 DV41: no roof defender in Zod
            try { RulesTestsZod(rows); }
            finally { ZWorld.EndFreezeDeviation = ef; ZWorld.FortGunDeviation = fg; ZWorld.GrabPolicyDeviation = gp; ZWorld.GrenadeRuleDeviation = gr; ZWorld.FortDefenderDeviation = fdd; }
        }
        static void RulesTestsZod(List<Row> rows)
        {
            // ---- map init: zone owners, building owners, default production (research 8.1 / 8.2)
            {
                var w = ZWorld.Create(SettingsText, MapText("L02"), 1);
                string owners = string.Join(" ", w.Zones.Select(z => z.Owner == Team.Neutral ? "N" : z.Owner.ToString()[0].ToString()));
                var prod = w.Objects.Where(o => o.IsProducer && o.Building).Select(o => $"{o.Type}({o.LocX / 16},{o.LocY / 16}) {o.Owner} {o.CurUnit}").ToList();
                rows.Add(new Row { test = "L02 zone owners at start (Z0..Z8)", expected = "B N N N N N R N N", measured = owners, pass = owners == "B N N N N N R N N" });
                rows.Add(new Row { test = "L02 default productions", expected = "blue fort + robot factory, red fort + robot factory: Grunt 72 s",
                                   measured = string.Join("; ", prod), pass = prod.Count == 4 && w.Objects.Where(o => o.IsProducer && o.Building).All(o => o.CurUnit == "grunt" && Math.Abs(o.BFinal - 72) < 1e-9) });
                var w17 = ZWorld.Create(SettingsText, MapText("L17"), 1);
                var p17 = w17.Objects.Where(o => o.IsProducer && o.Building).Select(o => $"{o.Type} {o.Owner} {o.CurUnit}").OrderBy(x => x).ToList();
                string e17 = "fort_back Red grunt; fort_front Blue grunt; robot_factory Red grunt; vehicle_factory Blue jeep";
                rows.Add(new Row { test = "L17 default productions", expected = e17, measured = string.Join("; ", p17), pass = string.Join("; ", p17) == e17 });
                int nb = w17.Objects.Count(o => o.IsBridge && o.Destroyed);
                rows.Add(new Row { test = "L17 bridges start destroyed, auto-repair scheduled", expected = "2 destroyed, repair at 600-660 s",
                                   measured = $"{nb} destroyed, repair at {string.Join(", ", w17.Objects.Where(o => o.IsBridge).Select(o => o.NextAutoRepairTime.ToString("F0")))} s",
                                   pass = nb == 2 && w17.Objects.Where(o => o.IsBridge).All(o => o.DoAutoRepair && o.NextAutoRepairTime >= 600 && o.NextAutoRepairTime <= 660) });
            }

            // ---- build time formula: base, flag fraction, damage (research 3.2)
            {
                var w = RulesWorld("L02", 3);
                foreach (var b in w.Objects.Where(o => o.IsProducer && o.Owner == Team.Blue)) b.StopBuildingProduction();
                var fort = B(w, "fort_back", 48, 72);
                var times = new List<double>();
                Func<ZEvent, bool> built = e => { if (e.type == ZEventType.UnitBuilt && e.otherId == fort.Id) { times.Add(w.Time); return true; } return false; };
                w.StopBuilding(Team.Red, fort); double t0 = w.Time; w.StartBuilding(Team.Red, fort, "grunt");
                Until(w, 200, built);
                double d1 = times.Count > 0 ? times[0] - t0 : -1;
                rows.Add(new Row { test = "production: Grunt at full health, 0 flags", expected = "72.00 s", measured = $"{d1:F2} s", pass = Math.Abs(d1 - 72) <= 0.011 });
                // give red 3 of 7 flags (far from both forts): the running unit re-scales from its start time
                foreach (var f in w.Flags.Where(f => (f.LocX / 16, f.LocY / 16) == (10, 81) || (f.LocX / 16, f.LocY / 16) == (28, 67) || (f.LocX / 16, f.LocY / 16) == (64, 42))) w.AwardZone(f, Team.Red);
                foreach (var b in w.Objects.Where(o => o.IsProducer && o != fort)) b.StopBuildingProduction();   // no new flag grabbers
                double frac = w.TeamFlagFraction(Team.Red);
                Until(w, 200, built);
                double d2 = times.Count > 1 ? times[1] - times[0] : -1, e2 = 72 * (1 - 0.5 * 3 / 7.0);
                rows.Add(new Row { test = "production: Grunt with 3/7 flags", expected = $"{e2:F2} s (x(1-0.5*3/7))", measured = $"{d2:F2} s (fraction {frac:F3})", pass = Math.Abs(d2 - e2) <= 0.011 });
                // 50 % fort health: x1.625 on top
                fort.Health = fort.MaxHealth / 2; fort.RecalcBuildTime();
                Until(w, 300, built);
                double d3 = times.Count > 2 ? times[2] - times[1] : -1, e3 = e2 * (1 + 1.25 * (1 - (double)fort.Health / fort.MaxHealth));
                rows.Add(new Row { test = "production: Grunt, 3/7 flags, fort at 50 % health", expected = $"{e3:F2} s (x1.625)", measured = $"{d3:F2} s", pass = Math.Abs(d3 - e3) <= 0.011 });
                // every base time in the build tables = default_settings build_time
                var bad = new List<string>();
                var table = new Dictionary<string, int> { { "grunt", 72 }, { "psycho", 98 }, { "sniper", 148 }, { "tough", 116 }, { "pyro", 161 }, { "laser", 179 }, { "jeep", 81 }, { "light", 137 },
                    { "medium", 225 }, { "heavy", 309 }, { "apc", 118 }, { "missile_launcher", 373 }, { "crane", 97 }, { "gatling", 96 }, { "gun", 125 }, { "howitzer", 179 }, { "missile_cannon", 182 } };
                foreach (var kv in table) if (w.S.Unit(kv.Key)?.build_time != kv.Value) bad.Add($"{kv.Key} {w.S.Unit(kv.Key)?.build_time}");
                rows.Add(new Row { test = "build times of all 17 units", expected = "research 3.3 table", measured = bad.Count == 0 ? "all 17 match" : string.Join(", ", bad), pass = bad.Count == 0 });
            }

            // ---- queue: LIFO insert, endless repeat of the last item, max 5 (research 3.4); Zod's rule measured with DV21 off
            bool fifoSaved = ZWorld.FifoQueueDeviation; ZWorld.FifoQueueDeviation = false;
            try {
            {
                var w = RulesWorld("L02", 4);
                foreach (var b in w.Objects.Where(o => o.IsProducer && o.Owner == Team.Blue)) b.StopBuildingProduction();
                var fort = B(w, "fort_back", 48, 72);
                w.StopBuilding(Team.Red, fort); w.StartBuilding(Team.Red, fort, "grunt");
                w.AddBuildingQueue(Team.Red, fort, "jeep"); w.AddBuildingQueue(Team.Red, fort, "crane");
                string q = string.Join(",", fort.Queue);
                int accepted = 0; for (int i = 0; i < 5; i++) if (w.AddBuildingQueue(Team.Red, fort, "gatling")) accepted++;
                for (int i = 0; i < accepted; i++) fort.CancelBuildingQueue(0, "gatling");
                bool refused = !w.AddBuildingQueue(Team.Red, fort, "medium");                              // not in the Tech 1 list
                var order = new List<string>();
                Until(w, 500, e => { if (e.type == ZEventType.UnitBuilt && e.otherId == fort.Id) order.Add(w.Get(e.objId).Type); return order.Count >= 5; });
                string got = string.Join(",", order);
                rows.Add(new Row { test = "queue: add Jeep then Crane while building Grunt", expected = "queue crane,jeep,grunt; output grunt,crane,jeep,grunt,grunt",
                                   measured = $"queue {q}; output {got}", pass = q == "crane,jeep,grunt" && got == "grunt,crane,jeep,grunt,grunt" });
                rows.Add(new Row { test = "queue limit 5, build list by tech level", expected = "2 more accepted (5 max), Medium refused at Tech 1",
                                   measured = $"{accepted} accepted, Medium {(refused ? "refused" : "accepted")}", pass = accepted == 2 && refused });
            }
            } finally { ZWorld.FifoQueueDeviation = fifoSaved; }
            // ---- DV21 (OpenZ): FIFO queue + REPEAT toggle
            {
                bool saved = ZWorld.FifoQueueDeviation; ZWorld.FifoQueueDeviation = true;
                try
                {
                    var w = RulesWorld("L02", 4);
                    foreach (var b in w.Objects.Where(o => o.IsProducer && o.Owner == Team.Blue)) b.StopBuildingProduction();
                    var fort = B(w, "fort_back", 48, 72);
                    w.StopBuilding(Team.Red, fort); w.StartBuilding(Team.Red, fort, "grunt");
                    w.AddBuildingQueue(Team.Red, fort, "jeep"); w.AddBuildingQueue(Team.Red, fort, "crane"); w.AddBuildingQueue(Team.Red, fort, "gatling");
                    string q = string.Join(",", fort.Queue);
                    var order = new List<string>();
                    bool repeatSent = false;                                                                  // the command is sent after the event loop (it emits)
                    Until(w, 900, e => { if (e.type == ZEventType.UnitBuilt && e.otherId == fort.Id) order.Add(w.Get(e.objId).Type);
                                         else if (e.type == ZEventType.CannonStored && e.objId == fort.Id) order.Add(fort.StoredCannons[fort.StoredCannons.Count - 1]);   // cannons are stored
                                         return order.Count >= 6; },
                          () => { if (order.Count >= 5 && !repeatSent) repeatSent = w.SetBuildingRepeat(Team.Red, fort, false); return false; });
                    string got = string.Join(",", order);
                    // REPEAT off after the 5th: the 6th (gatling, already in production) finishes, then the fort goes idle
                    Until(w, 200, e => false);
                    bool idle = !fort.Building && fort.CurUnit == null;
                    rows.Add(new Row { test = "DV21 FIFO: add Jeep, Crane, Gatling while building Grunt; REPEAT off after 5", expected = "queue jeep,crane,gatling; output grunt,jeep,crane,gatling,gatling,gatling; then idle",
                                       measured = $"queue {q}; output {got}; idle {idle}", pass = q == "jeep,crane,gatling" && got == "grunt,jeep,crane,gatling,gatling,gatling" && idle });
                }
                finally { ZWorld.FifoQueueDeviation = saved; }
            }

            // ---- flag capture: touch = instant, checked every 0.2 s; zone + linked buildings flip, bridges stay neutral (5.3)
            {
                var w = RulesWorld("L02", 5);
                var flag = w.Flags.First(f => (f.LocX / 16, f.LocY / 16) == (39, 40));
                var g = w.CreateRobotGroup("grunt", (39 + 4) * 16, 40 * 16, Team.Red, 1);                     // east of the flag (west is the factory)
                w.SendWaypoints(Team.Red, g, new[] { ZWaypoint.Move(flag.CenterX, flag.CenterY) });
                double touch = -1, cap = -1;
                Until(w, 30, e => { if (e.type == ZEventType.FlagCaptured && cap < 0) cap = w.Time; return cap >= 0; }, () => { if (touch < 0 && flag.IntersectsObject(g)) touch = w.Time; return false; });
                var z = w.ZoneOf(flag); var vf = B(w, "vehicle_factory", 33, 38);
                bool bridgesNeutral = w.Objects.Where(o => o.IsBridge && o.ZoneIndex == flag.ZoneIndex).All(o => o.Owner == Team.Neutral);
                rows.Add(new Row { test = "flag capture: touch to capture delay", expected = "<= 0.20 s (0.2 s check)", measured = cap < 0 ? "no capture" : $"{cap - touch:F2} s",
                                   pass = cap >= 0 && touch >= 0 && cap - touch <= 0.211 });
                rows.Add(new Row { test = "flag capture flips zone Z4 and its vehicle factory", expected = "zone Red, factory Red building Jeep 81 s, bridges neutral",
                                   measured = $"zone {z.Owner}, factory {vf.Owner} {vf.CurUnit} {vf.BFinal - vf.BInit:F2} s, bridges {(bridgesNeutral ? "neutral" : "changed")}",
                                   pass = z.Owner == Team.Red && vf.Owner == Team.Red && vf.CurUnit == "jeep" && Math.Abs(vf.BFinal - vf.BInit - 81 * (1 - 0.5 / 7)) < 1e-6 && bridgesNeutral,
                                   note = "Jeep time already x(1-0.5*1/7) for the new flag" });
            }

            // ---- auto-grab: idle armed units walk to a non-owned flag within 220 px (research 5.3)
            // Zod's blind radius check: the 192 px grunt stands west of the flag with the vehicle factory between them, which the
            bool autoSight = ZObject.AutoSight; ZObject.AutoSight = false;
            try
            {
                var w = RulesWorld("L02", 6);
                var flag = w.Flags.First(f => (f.LocX / 16, f.LocY / 16) == (39, 40));
                w.Objects.RemoveAll(o => o.Type == "grenades"); w.Flags.RemoveAll(f => f != flag);           // only this flag to grab
                var near = w.CreateRobotGroup("grunt", flag.LocX - 12 * 16, flag.LocY, Team.Red, 1);                 // 192 px
                Until(w, 25, e => e.type == ZEventType.FlagCaptured);
                var w2 = RulesWorld("L02", 6);
                var flag2 = w2.Flags.First(f => (f.LocX / 16, f.LocY / 16) == (39, 40));
                w2.Objects.RemoveAll(o => o.Type == "grenades"); w2.Flags.RemoveAll(f => f != flag2);
                var far = w2.CreateRobotGroup("grunt", flag2.LocX - 15 * 16, flag2.LocY, Team.Red, 1);             // 240 px
                int x0 = far.CenterX; Until(w2, 10);
                rows.Add(new Row { test = "auto-grab flag distance 220 px", expected = "grunt at 192 px takes it, grunt at 240 px stays",
                                   measured = $"192 px: zone {w.ZoneOf(flag).Owner}; 240 px: moved {Math.Abs(far.CenterX - x0)} px", pass = w.ZoneOf(flag).Owner == Team.Red && far.CenterX == x0 });
            }
            finally { ZObject.AutoSight = autoSight; }

            // ---- cannons: storage limited to 4 per zone (Z6 holds 3), placement rules (research 4, 8.1)
            {
                var w = RulesWorld("L02", 7, keepCannons: true);
                foreach (var b in w.Objects.Where(o => o.IsProducer && o.Owner == Team.Blue)) b.StopBuildingProduction();
                foreach (var b in w.Objects.Where(o => o.Type == "robot_factory" && o.Owner == Team.Red)) b.StopBuildingProduction();
                var fort = B(w, "fort_back", 48, 72);
                int before = w.CannonsInZone(fort);
                w.StopBuilding(Team.Red, fort); w.StartBuilding(Team.Red, fort, "gatling");
                w.Events.Clear();
                int n = 0; Until(w, 300, e => e.type == ZEventType.ProductionChanged && e.objId == fort.Id && ++n >= 2);
                int stored = fort.StoredCannons.Count;
                rows.Add(new Row { test = "cannon storage: 2 Gatlings built in the red fort zone", expected = "zone had 3 -> 1 stored, 2nd discarded",
                                   measured = $"zone had {before} -> {stored} stored", pass = before == 3 && stored == 1 });
                bool edge = w.CannonPlacable(fort, 36, 70), onFort = w.CannonPlacable(fort, 50, 74), inZone = w.CannonPlacable(fort, 40, 66);
                var c = w.PlaceCannon(Team.Red, fort, 55, 72);
                rows.Add(new Row { test = "cannon placement rules", expected = "zone edge no, fort body no, open ground yes, turret spot (55,72) yes + non-ejectable, Grunt driver",
                                   measured = $"edge {edge}, fort {onFort}, ground {inZone}, turret {(c != null ? $"placed, ejectable {c.Ejectable}, drivers {c.Drivers.Count} {c.DriverType}" : "refused")}",
                                   pass = !edge && !onFort && inZone && c != null && !c.Ejectable && c.Drivers.Count == 1 && c.Owner == Team.Red && fort.StoredCannons.Count == 0 });
            }

            // ---- repair station (L17 blue station): drive in, flat 5 s, fresh vehicle with the same crew (research 6.2)
            {
                var w = RulesWorld("L17", 8);
                var st = B(w, "repair", 7, 20);
                st.GetRepairEntrance(out int ex, out int ey);
                var tank = w.CreateObject("vehicle", "light", ex - 16, ey + 3 * 16, Team.Blue);              // created in place (speed from its tile)
                tank.Health = tank.MaxHealth / 2;
                bool ok = w.SendWaypoints(Team.Blue, tank, new[] { new ZWaypoint { mode = WpMode.UnitRepair, refId = st.Id, x = st.CenterX - 8, y = st.CenterY } }) && tank.Waypoints.Count == 1;
                double tin = -1, tout = -1; ZObject fresh = null;
                Until(w, 60, e =>
                {
                    if (e.type == ZEventType.RepairIn) tin = w.Time;
                    if (e.type == ZEventType.RepairOut) { tout = w.Time; fresh = w.Get(e.objId); return true; }
                    return false;
                });
                rows.Add(new Row { test = "repair station: time inside, result", expected = "5.00 s, same type, full health, Blue, 1 driver",
                                   measured = !ok ? "order refused" : tout < 0 ? $"in {tin:F2}, never out" : $"{tout - tin:F2} s, {fresh.Type}, {fresh.Health}/{fresh.MaxHealth}, {fresh.Owner}, {fresh.Drivers.Count} driver",
                                   pass = ok && fresh != null && Math.Abs(tout - tin - 5) <= 0.011 && fresh.Type == "light" && fresh.Health == fresh.MaxHealth && fresh.Owner == Team.Blue && fresh.Drivers.Count == 1 });
            }

            // ---- crane repair + auto-repair window + bridge death (research 1.5, 6.3)
            {
                var w = RulesWorld("L02", 9);
                var vf = B(w, "vehicle_factory", 22, 61);                                                       // Z7, neutral
                Kill(w, vf);
                double due = vf.NextAutoRepairTime - w.Time;
                vf.GetCraneEntrance(out int ex, out int ey, out _, out _);
                var crane = w.CreateObject("vehicle", "crane", ex - 16, ey + 32, Team.Red);
                bool ok = w.SendWaypoints(Team.Red, crane, new[] { new ZWaypoint { mode = WpMode.CraneRepair, refId = vf.Id, x = vf.CenterX, y = vf.CenterY } }) && crane.Waypoints.Count == 1;
                double t0 = w.Time, tr = -1;
                Until(w, 90, e => { if (e.type == ZEventType.Revived && e.objId == vf.Id) { tr = w.Time; return true; } return false; });
                rows.Add(new Row { test = "crane repairs a destroyed factory", expected = "revived long before 600 s, full health, owner kept, idle",
                                   measured = !ok ? "order refused" : tr < 0 ? "not revived in 90 s" : $"revived after {tr - t0:F1} s, {vf.Health}/{vf.MaxHealth}, {vf.Owner}, {(vf.Building ? "producing" : "idle")}",
                                   pass = ok && tr > 0 && vf.Health == vf.MaxHealth && vf.Owner == Team.Neutral && !vf.Building });
                var radar = B(w, "radar", 39, 18); Kill(w, radar);
                double rdue = radar.NextAutoRepairTime - w.Time, t1 = w.Time, rr = -1;
                Until(w, 700, e => { if (e.type == ZEventType.Revived && e.objId == radar.Id) { rr = w.Time; return true; } return false; });
                rows.Add(new Row { test = "auto-repair of a destroyed building", expected = "600-660 s", measured = rr < 0 ? $"not revived (due {rdue:F0} s)" : $"{rr - t1:F2} s",
                                   pass = rr > 0 && rr - t1 >= 600 && rr - t1 <= 660.011 && due >= 600 && due <= 660 });
                // bridge: a robot on the deck dies with it; the deck blocks until repaired
                var br = B(w, "bridge_horz", 41, 44);
                var g = w.CreateRobotGroup("grunt", br.CenterX - 8, br.CenterY - 8, Team.Red, 1);
                int dtx = br.CenterX >> 4, dty = br.CenterY >> 4;
                bool passBefore = w.Path.TilePassable(dtx, dty, true);
                Kill(w, br);
                bool dead = g.Destroyed, passDead = w.Path.TilePassable(dtx, dty, true);
                br.SetHealth(br.MaxHealth);
                bool passAfter = w.Path.TilePassable(dtx, dty, true);
                rows.Add(new Row { test = "bridge destroyed: robot on the deck, deck passability", expected = "robot dies; deck open -> blocked -> open after repair",
                                   measured = $"robot {(dead ? "dead" : "alive")}; deck {passBefore} -> {passDead} -> {passAfter}", pass = dead && passBefore && !passDead && passAfter });
            }

            // ---- win: walk into the enemy fort -> fort dies -> team eliminated -> game over (research 7)
            {
                var w = RulesWorld("L02", 10);
                var fort = B(w, "fort_front", 14, 7);
                fort.GetCreationMovePoint(out int ex, out int ey);
                var g = w.CreateRobotGroup("grunt", ex - 8, ey + 3 * 16, Team.Red, 1);
                var blue = w.CreateRobotGroup("grunt", 8 * 16, 22 * 16, Team.Blue, 3);
                blue.Damage = 0; foreach (var m in blue.Minions) m.Damage = 0;                                // bystanders, not defenders
                var rf = B(w, "robot_factory", 7, 13);
                var flag = w.Flags.First(); w.AwardZone(flag, Team.Blue);
                bool ok = w.SendWaypoints(Team.Red, g, new[] { new ZWaypoint { mode = WpMode.EnterFort, refId = fort.Id, x = fort.CenterX, y = fort.CenterY } }) && g.Waypoints.Count == 1;
                double tIn = -1, tElim = -1, tEnd = -1; Team winner = Team.Neutral;
                Until(w, 60, e =>
                {
                    if (e.type == ZEventType.FortEntered) tIn = w.Time;
                    if (e.type == ZEventType.TeamEliminated) tElim = w.Time;
                    if (e.type == ZEventType.GameOver) { tEnd = w.Time; winner = (Team)(int)e.value; return true; }
                    return false;
                });
                rows.Add(new Row { test = "fort entry: a Grunt walks into the blue fort", expected = "fort destroyed, Blue eliminated, Blue squad dead, factory rubble, Blue flag neutral, Red wins <= 1 s later",
                                   measured = !ok ? "order refused" : $"entered {tIn:F1} s, fort {(fort.Destroyed ? "destroyed" : "alive")}, eliminated {tElim:F1} s, squad {(blue.Destroyed ? "dead" : "alive")}, factory {(rf.Destroyed ? "rubble" : "standing")}, flag {flag.Owner}, winner {winner} at {tEnd:F1} s",
                                   pass = ok && fort.Destroyed && tElim >= 0 && blue.Destroyed && rf.Destroyed && flag.Owner == Team.Neutral && winner == Team.Red && tEnd - tElim <= 1.011 });
                bool attackBuilding = new[] { ("grunt", false), ("tough", true) }.All(t =>
                {
                    var u = w.CreateRobotGroup(t.Item1, 40 * 16, 66 * 16, Team.Blue, 1);
                    return u.CanAttackObject(B(w, "robot_factory", 61, 71)) == t.Item2;
                });
                rows.Add(new Row { test = "buildings take explosive attacks only", expected = "Grunt can't target a factory, Tough can", measured = attackBuilding ? "as expected" : "wrong", pass = attackBuilding });
            }

            // ---- no units left -> the team's fort is destroyed (zserver.cpp:1925-1965)
            {
                var w = RulesWorld("L02", 11);
                var lone = w.CreateRobotGroup("grunt", 20 * 16, 22 * 16, Team.Blue, 1);
                var fort = B(w, "fort_front", 14, 7);
                Kill(w, lone);
                rows.Add(new Row { test = "last blue unit dies", expected = "blue fort destroyed", measured = fort.Destroyed ? "blue fort destroyed" : "fort standing", pass = fort.Destroyed });
            }

            // ---- unit cap 70: the finished unit waits, then spawns on the first tick below the cap (research 3.7)
            {
                var w = RulesWorld("L02", 12);
                foreach (var b in w.Objects.Where(o => o.IsProducer && o.Owner == Team.Blue)) b.StopBuildingProduction();
                foreach (var b in w.Objects.Where(o => o.Type == "robot_factory" && o.Owner == Team.Red)) b.StopBuildingProduction();
                var fort = B(w, "fort_back", 48, 72);
                var filler = new List<ZObject>();
                for (int i = 0; i < 70; i++) filler.Add(w.CreateObject("robot", "grunt", (38 + i % 20) * 16, (80 + i / 20) * 16, Team.Red));
                w.StopBuilding(Team.Red, fort); w.StartBuilding(Team.Red, fort, "grunt");
                bool early = Until(w, 80, e => e.type == ZEventType.UnitBuilt && e.otherId == fort.Id);
                Kill(w, filler[0]); double tk = w.Time, tb = -1;
                Until(w, 5, e => { if (e.type == ZEventType.UnitBuilt && e.otherId == fort.Id) { tb = w.Time; return true; } return false; });
                rows.Add(new Row { test = "unit cap 70", expected = "no spawn at 72 s with 70 units; spawns right after one dies", measured = $"{(early ? "spawned at cap" : "held")}, after the death: {(tb < 0 ? "nothing" : $"{tb - tk:F2} s")}",
                                   pass = !early && tb >= 0 && tb - tk <= 0.021 });
            }
        }
    }
}
