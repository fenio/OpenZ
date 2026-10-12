using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OpenZ.Sim;
#if !OPENZ_HARNESS
using UnityEditor;
using UnityEngine;
#endif

namespace OpenZ.EditorTools
{
    /// <summary>
    /// electrobot_v1: the Deviations block rows DV45 "Electrobot" (Sim/ZObject.Electro.cs) and DV46 "the CPU builds one Electrobot at a
    /// time" (Sim/ZBot.Electrobot.cs), measured headless in the same sim as the parity rows, with both switches on for the probe and every
    /// other switch as the build has them. DV45: an L17 test field (every map unit removed, cannons neutral, the destroyable map items of
    /// the field blown up, 12 s for the debris): the stats, the tesla cycle against an idle crane (the DV31 crane rifle off: it would roll
    /// rand()), a Tough under a move order (two bolts, then the Electrobot is removed), a light tank, a gun, a grunt, another Electrobot,
    /// the rand() stream compared with the same world without him, and the Tough run twice (event hash). DV46: L17 and L02 bot vs bot,
    /// full checklist (every unit kind, crews, bridges, determinism, balance).
    /// Menus: OpenZ/Deviations/Electrobot (oz_electrobot), OpenZ/Deviations/CPU Electrobot (oz_electrobot_cpu), OpenZ/Deviations/Electrobot
    /// </summary>
    public static partial class OpenZParity
    {
        public struct ElectrobotResult
        {
            public int hp, range, damage, splash, group, build, craneHp, craneBolts, bystanderLoss, toughHp, toughRockets, lightShells, gunBolts, gunStuns, elbStuns, gruntStuns;
            public long firstBolt, cycleMin, cycleMax, toughHold1, toughHold2, toughEndAfter, toughMoveAfter, lightHold, lightMoveAfter;
            public bool listOk, allLevelsOk, gruntKilled, rngEqual, repeatEqual, noBuildingTarget;
            public string bolts;
        }

        /// <summary>electrobot_v1: the DV45 / DV46 rows (CollectDeviations).</summary>
        static void AddElectrobotDeviations(List<Deviation> list, Func<string, int, int> getInt)
        {
            try { list.Add(DevElectrobot(getInt)); }
            catch (Exception ex) { list.Add(new Deviation { id = "DV45", change = "DevElectrobot", zod = "-", openz = "-", measured = "probe threw " + ex.GetType().Name + ": " + ex.Message, toggle = "-", status = DevCheck }); }
            try { list.Add(DevElectrobotBot(getInt)); }
            catch (Exception ex) { list.Add(new Deviation { id = "DV46", change = "DevElectrobotBot", zod = "-", openz = "-", measured = "probe threw " + ex.GetType().Name + ": " + ex.Message, toggle = "-", status = DevCheck }); }
        }

        // ------------------------------------------------------------------ probe helpers (L17 test field)
        static ZWorld EbWorld(int seed)
        {
            var w = ZWorld.Create(SettingsText, MapText("L17"), seed, true);
            var keep = new List<ZObject> { EbDummy(w, "grunt", 2 * 16, 2 * 16, Team.Red), EbDummy(w, "grunt", (w.Map.width - 3) * 16, 2 * 16, Team.Blue) };
            foreach (var o in w.Objects.Where(o => o.IsUnit && !keep.Contains(o)).ToList())
            {
                if (o.Kind == ZKind.Cannon) { o.Owner = Team.Neutral; o.Drivers.Clear(); o.Waypoints.Clear(); o.Disengage(); o.Damage = 0; continue; }
                if (o.Kind == ZKind.Vehicle && o.Owner == Team.Neutral) continue;
                o.SetHealth(0); w.UpdateObjectHealth(o);
            }
            foreach (var b in w.Objects) if (b.IsProducer) b.StopBuildingProduction();
            EbTicks(w, 1200);
            foreach (var b in w.Objects) if (b.IsProducer) b.StopBuildingProduction();
            return w;
        }
        static void EbTicks(ZWorld w, int n) { for (int i = 0; i < n; i++) { w.Tick(ZConst.TICK); w.Events.Clear(); } }
        static ZObject EbDummy(ZWorld w, string type, int x, int y, Team t)
        {
            var o = w.CreateObject("robot", type, x, y, t);
            o.Damage = 0; o.MoveSpeed = 0; o.RealMoveSpeed = 0; o.MaxHealth = o.Health = 1000000000;
            return o;
        }
        static void EbKill(ZWorld w, ZObject o) { if (o != null && !o.Destroyed) { o.SetHealth(0); w.UpdateObjectHealth(o); } }
        static ZObject EbElb(ZWorld w, int cx, int cy, Team t, bool unkillable = true)
        {
            var e = w.CreateRobotGroup(ZElectro.Type, cx - 8, cy - 8, t);
            if (unkillable) e.MaxHealth = e.Health = 1000000000;
            return e;
        }

        /// <summary>A ground / road field of wT x hT tiles, no building, unit or flag within a tile, nearest the map centre; its destroyable
        /// map items blown up (6 s for the debris), then passable for robots and vehicles. Top-left px.</summary>
        static (int x, int y) EbField(ZWorld w, int wT, int hT)
        {
            int W = w.Map.width, H = w.Map.height, cx = W / 2, cy = H / 2;
            var cand = new List<(int d, int x, int y)>();
            for (int y = 3; y + hT < H - 3; y++) for (int x = 3; x + wT < W - 3; x++) cand.Add(((x - cx) * (x - cx) + (y - cy) * (y - cy), x, y));
            foreach (var (_, x, y) in cand.OrderBy(c => c.d).ThenBy(c => c.y).ThenBy(c => c.x))
            {
                bool ok = true;
                for (int j = y; j < y + hT && ok; j++)
                    for (int i = x; i < x + wT && ok; i++) { var c = w.Map.ClassAt(i, j); ok = c == ZClass.Ground || c == ZClass.Road; }
                if (!ok) continue;
                var probe = new ZObject { WidthPix = wT * 16 + 32, HeightPix = hT * 16 + 32 }; probe.SetCords(x * 16 - 16, y * 16 - 16);
                if (w.Objects.Any(o => !o.Destroyed && probe.IntersectsObject(o) && !(o.Kind == ZKind.MapItem && o.Type != "flag"))) continue;
                var items = w.Objects.Where(o => !o.Destroyed && o.Kind == ZKind.MapItem && o.Type != "flag" && o.MaxHealth > 0 && probe.IntersectsObject(o)).ToList();
                foreach (var o in items) { o.SetHealth(0); w.UpdateObjectHealth(o); }
                if (items.Count > 0) EbTicks(w, 600);
                for (int j = y; j < y + hT && ok; j++)
                    for (int i = x; i < x + wT && ok; i++) ok = w.Path.TilePassable(i, j, false) && w.Path.TilePassable(i, j, true);
                if (ok) return (x * 16, y * 16);
            }
            throw new Exception("no free test field on L17");
        }

        /// <summary>One stun run: a blue `type` 110 px east of a red Electrobot, ordered 150 px away; a red unarmed dummy in its range; the
        /// Electrobot is removed after `bolts` bolts. Returns hold ticks per bolt (bolt to StunEnd), shots while stunned, the end tick and
        /// the first moving tick after it, the event hash.</summary>
        static (List<long> holds, int shots, long endAfterLast, long moveAfterEnd, int hp, long hash, int starts) EbStunRun(string type, int bolts)
        {
            var w = EbWorld(2); var (fx, fy) = EbField(w, 24, 12);
            int ux = fx + 170, uy = fy + 96;
            var u = ZBuildLists.IsRobot(type) ? w.CreateRobotGroup(type, ux - 8, uy - 8, Team.Blue) : w.CreateObject("vehicle", type, ux - 16, uy - 16, Team.Blue);
            EbDummy(w, "grunt", ux - 60 - 8, uy + 20 - 8, Team.Red);
            var e = EbElb(w, ux + 110, uy, Team.Red);
            w.SendWaypoints(Team.Blue, u, new[] { ZWaypoint.Move(ux, uy + 150) });
            var boltTicks = new List<long>(); long end = -1, moved = -1; int shots = 0, starts = 0; double x0 = 0, y0 = 0; long hash = 17; double t0 = w.Time;
            while (w.Time < t0 + 45 && !(moved > 0))
            {
                w.Tick(ZConst.TICK);
                foreach (var ev in w.Events.ToArray())
                {
                    hash = unchecked(hash * 31 + (int)ev.type * 7 + ev.objId * 13 + ev.otherId * 17 + ev.x * 3 + ev.y * 5 + (int)ev.value);
                    if (ev.type == ZEventType.ElectroBolt && ev.otherId == u.Id) { boltTicks.Add(w.TickIndex); if (boltTicks.Count == 1) { x0 = u.PosX; y0 = u.PosY; } if (boltTicks.Count >= bolts) EbKill(w, e); }
                    if (ev.type == ZEventType.StunStart && ev.objId == u.Id) starts++;
                    if (ev.type == ZEventType.StunEnd && ev.objId == u.Id && !ev.flag) end = w.TickIndex;
                    if ((ev.type == ZEventType.MissileFired || ev.type == ZEventType.Hitscan) && ev.objId == u.Id && u.Stunned) shots++;
                }
                w.Events.Clear();
                if (end > 0 && moved < 0 && (u.PosX != x0 || u.PosY != y0)) moved = w.TickIndex;
            }
            var holds = boltTicks.Select((b, i) => (i + 1 < boltTicks.Count ? boltTicks[i + 1] : end) - b).ToList();
            return (holds, shots, boltTicks.Count > 0 ? end - boltTicks[boltTicks.Count - 1] : -1, end > 0 && moved > 0 ? moved - end : -1, u.Health, hash, starts);
        }

        /// <summary>DV45 probe (see the class summary). Deterministic (seeded worlds, fixed tick). Both switches on for the probe.</summary>
        public static ElectrobotResult ElectrobotProbe()
        {
            var r = new ElectrobotResult();
            bool d = ZWorld.ElectrobotDeviation, b = ZWorld.ElectrobotBotDeviation, cg = ZWorld.CraneGunDeviation, all = ZWorld.ElectrobotAllLevels;
            ZWorld.ElectrobotDeviation = ZWorld.ElectrobotBotDeviation = true; ZWorld.ElectrobotAllLevels = false;   // the SPEC default; D1's toggle checked below
            bool kh = ZWorld.KnockHoldDeviation; ZWorld.KnockHoldDeviation = false;
            try
            {
                var u = ZSettings.Parse(SettingsText).Unit(ZElectro.Type);
                r.hp = u.MaxHealth; r.range = u.attack_radius; r.damage = u.Damage; r.splash = u.attack_damage_radius; r.group = u.group_amount; r.build = u.build_time;
                r.listOk = true;
                foreach (var t in new[] { "fort_front", "fort_back", "robot_factory", "vehicle_factory" })
                    for (int lv = 0; lv <= 5; lv++)
                    {
                        var l = ZBuildLists.For(t, lv).ToList(); int ie = l.IndexOf(ZElectro.Type), il = l.IndexOf("laser");
                        int lastRobot = l.FindLastIndex(x => x != ZElectro.Type && ZBuildLists.IsRobot(x));
                        bool fort = t == "fort_front" || t == "fort_back";
                        if (fort ? ie != (il >= 0 ? il + 1 : lastRobot + 1) || l[0] != "grunt" : ie >= 0) r.listOk = false;
                    }
                if (ZBuildLists.DisplayTech("fort_front", 0) != 7 || ZBuildLists.DisplayTech("robot_factory", 5) != 6 || ZBuildLists.Roman(7) != "VII") r.listOk = false;
                // the retired D1 toggle must change nothing
                ZWorld.ElectrobotAllLevels = true; r.allLevelsOk = true;
                foreach (var t in new[] { "robot_factory", "vehicle_factory" })
                    for (int lv = 0; lv <= 5; lv++) if (ZBuildLists.For(t, lv).Contains(ZElectro.Type)) r.allLevelsOk = false;
                ZWorld.ElectrobotAllLevels = false;
                ZWorld.ElectrobotDeviation = false;
                foreach (var t in new[] { "fort_front", "robot_factory" }) for (int lv = 0; lv <= 5; lv++) if (ZBuildLists.For(t, lv).Contains(ZElectro.Type)) r.listOk = false;
                if (ZBuildLists.DisplayTech("fort_front", 2) != 3) r.listOk = false;                  // DV45 off: Zod's fort level shown
                ZWorld.ElectrobotDeviation = true;
                // ---- the cycle against an idle crane, a bystander 6 px off its box
                ZWorld.CraneGunDeviation = false;
                long EbCycleRun(bool withElb, out List<long> bolts, out int craneHp, out int dead, out int byLoss, out bool fortRefused)
                {
                    var w = EbWorld(1); var (fx, fy) = EbField(w, 20, 10);
                    var crane = w.CreateObject("vehicle", "crane", fx + 158 - 16, fy + 80 - 16, Team.Blue);
                    var by = EbDummy(w, "grunt", crane.CenterX + 10 - 8, crane.CenterY + 22 - 8, Team.Blue); int by0 = by.Health;
                    craneHp = crane.Health; bolts = new List<long>(); dead = -1;
                    ZObject e = withElb ? EbElb(w, fx + 48, fy + 80, Team.Red) : null;
                    var fort = w.Objects.First(o => o.IsFort && o.Owner == Team.Blue);
                    fortRefused = e == null || (!e.CanAttackObject(fort) && w.SendWaypoints(Team.Red, e, new[] { new ZWaypoint { mode = WpMode.Attack, refId = fort.Id, x = fort.CenterX, y = fort.CenterY } }) && e.Waypoints.Count == 0);
                    long charge = -1; int byAt = -1; double t0 = w.Time;
                    while (w.Time < t0 + 30)
                    {
                        w.Tick(ZConst.TICK);
                        foreach (var ev in w.Events)
                        {
                            if (ev.type == ZEventType.ElectroCharge && charge < 0) charge = w.TickIndex;
                            if (ev.type == ZEventType.ElectroBolt && ev.otherId == crane.Id) { bolts.Add(w.TickIndex - charge); if (ev.flag2) dead = bolts.Count; }
                        }
                        w.Events.Clear();
                        if (dead > 0 && byAt < 0) byAt = by0 - by.Health;                     // while the crane stood (then he zaps the bystander: an enemy in range)
                    }
                    byLoss = byAt < 0 ? by0 - by.Health : byAt;
                    return w.Rng.Next();
                }
                long withNext = EbCycleRun(true, out var bl, out r.craneHp, out r.craneBolts, out r.bystanderLoss, out r.noBuildingTarget);
                long withoutNext = EbCycleRun(false, out _, out _, out _, out _, out _);
                ZWorld.CraneGunDeviation = cg;
                r.rngEqual = withNext == withoutNext;
                r.firstBolt = bl.Count > 0 ? bl[0] : -1;
                var gaps = bl.Zip(bl.Skip(1), (p, q) => q - p).ToList();
                r.cycleMin = gaps.Count > 0 ? gaps.Min() : -1; r.cycleMax = gaps.Count > 0 ? gaps.Max() : -1;
                r.bolts = string.Join(" / ", bl.Select(x => "+" + x));
                // ---- a Tough under a move order (two bolts), twice for the event hash; a light tank (one bolt)
                var t1 = EbStunRun("tough", 2); var t2 = EbStunRun("tough", 2);
                r.toughHold1 = t1.holds.Count > 0 ? t1.holds[0] : -1; r.toughHold2 = t1.holds.Count > 1 ? t1.holds[1] : -1;
                r.toughEndAfter = t1.endAfterLast; r.toughMoveAfter = t1.moveAfterEnd; r.toughRockets = t1.shots; r.toughHp = t1.hp;
                r.repeatEqual = t1.hash == t2.hash && t1.hp == t2.hp;
                var lt = EbStunRun("light", 1);
                r.lightHold = lt.holds.Count > 0 ? lt.holds[0] : -1; r.lightMoveAfter = lt.moveAfterEnd; r.lightShells = lt.shots;
                // ---- a gun, a grunt, another Electrobot
                (int bolts, int stuns, bool killed) Target(Func<ZWorld, int, int, ZObject> make)
                {
                    var w = EbWorld(3); var (fx, fy) = EbField(w, 20, 10);
                    var t = make(w, fx + 160, fy + 80);
                    EbElb(w, fx + 50, fy + 80, Team.Red);
                    int nb = 0, ns = 0; bool k = false; double t0 = w.Time;
                    while (w.Time < t0 + 9) { w.Tick(ZConst.TICK); foreach (var ev in w.Events) { if (ev.type == ZEventType.ElectroBolt && ev.otherId == t.Id) { nb++; k |= ev.flag2; } if (ev.type == ZEventType.StunStart && ev.objId == t.Id) ns++; } w.Events.Clear(); }
                    return (nb, ns, k);
                }
                var gun = Target((w, x, y) => { var c = w.CreateObject("cannon", "gun", x - 16, y - 16, Team.Blue); c.MaxHealth = c.Health = 1000000000; return c; });
                r.gunBolts = gun.bolts; r.gunStuns = gun.stuns;
                var gr = Target((w, x, y) => w.CreateRobotGroup("grunt", x - 8, y - 8, Team.Blue, 1));
                r.gruntKilled = gr.bolts == 1 && gr.killed; r.gruntStuns = gr.stuns;
                var eb = Target((w, x, y) => EbElb(w, x, y, Team.Blue));
                r.elbStuns = eb.bolts >= 2 ? eb.stuns : -1;
            }
            finally { ZWorld.ElectrobotDeviation = d; ZWorld.ElectrobotBotDeviation = b; ZWorld.CraneGunDeviation = cg; ZWorld.ElectrobotAllLevels = all; ZWorld.KnockHoldDeviation = kh; }
            return r;
        }

        static Deviation DevElectrobot(Func<string, int, int> getInt)
        {
            var r = ElectrobotProbe();
            var s = ZSettings.Parse(SettingsText);
            int stun = ZElectro.StunTicks, cyc = ZElectro.CycleTicks, win = ZElectro.WindupTicks;
            bool ok = r.hp == s.Unit("tough").MaxHealth && r.range == s.Unit("sniper").attack_radius && r.damage == s.Unit("tough").Damage && r.splash == 0 && r.group == 1
                      && r.build == s.Unit("laser").build_time && r.listOk && r.allLevelsOk && r.firstBolt == win && r.cycleMin == cyc && r.cycleMax == cyc && r.craneBolts == (int)Math.Ceiling(r.craneHp / (double)r.damage)
                      && r.bystanderLoss == 0 && r.toughHold1 == cyc && r.toughHold2 == stun && r.toughEndAfter == stun && r.toughMoveAfter >= 0 && r.toughMoveAfter <= 1 && r.toughRockets >= 3
                      && r.lightHold == stun && r.lightMoveAfter >= 0 && r.lightMoveAfter <= 1 && r.lightShells >= 1 && r.gunBolts >= 2 && r.gunStuns == 0 && r.gruntKilled && r.gruntStuns == 0
                      && r.elbStuns == 0 && r.rngEqual && r.repeatEqual && r.noBuildingTarget;
            bool pref = getInt("oz_electrobot", 1) == 1 && getInt("oz_zod_exact", 0) == 0;
            string S(long ticks) => (ticks * ZConst.TICK).ToString("0.00", CultureInfo.InvariantCulture) + " s";
            return new Deviation
            {
                id = "DV45", change = "Electrobot: a new robot that Z never had (squad of 1, tesla gatling, 15 s stun)",
                zod = "Z has six robot types: grunt, psycho, sniper, tough, pyro, laser (default_settings.txt, zbuildlist.cpp:22-236). No weapon stuns: every hit only removes health (zobject.cpp:2102-2213).",
                openz = $"unit.electrobot in default_settings.txt: squad {r.group}, {r.hp} HP (= Tough), range {r.range} px (= Sniper), {r.damage} HP per bolt (= the Tough rocket), splash {r.splash}, hit chance 1.0 with a line of sight (DV23), no driver sniping, no explosives (never buildings, rocks or bridges), no grenades, move 12 px/s. " +
                        $"A bolt every {S(cyc)} = {cyc} ticks ({S(win)} charge + {S(ZElectro.RechargeTicks)} recharge). A bolt that does not kill stuns robots and vehicles for {S(stun)} = {stun} ticks, refreshed by a new bolt, never stacked: no movement, the unit still fires at enemies in range, and its orders wait. " +
                        $"Cannons, the fort defender and other Electrobots are never stunned. Build time {r.build} s (= Laser), TECH VII: ONLY forts build him, in the fort list of every level right after the Laser (else after the last robot); robot and vehicle factories keep Zod's lists (the D1 every-level toggle is retired); the UI shows a fort as TECH VII. He never auto-enters a vehicle. Sim ticks only, no rand draw. " +
                        $"Knobs: ZElectro.WindupTicks {win}, CycleTicks {cyc}, StunTicks {stun}, StunChainMaxTicks {ZElectro.StunChainMaxTicks} and StunGraceTicks {ZElectro.StunGraceTicks} (0 = off); ZWorld.ElectrobotImmune {ZWorld.ElectrobotImmune} (D4), ElectrobotNoAutoEnter {ZWorld.ElectrobotNoAutoEnter} (D7); the settings' damage and health. " +
                        "BALANCE WATCH (balance later): the CPU fields one at a time (DV46) but the player is not capped; at 144 px he out-ranges every 120 px robot and each bolt restarts the 15 s, so one Electrobot keeps one target frozen for as long as he lives (three in turn) and several can freeze whole fights. Ready if needed: StunChainMaxTicks (the longest one stun may last) + StunGraceTicks (immunity after it); first player-side numbers with 3 Electrobots: electrobot_v1 sim/reports/stunlock.txt",
                measured = $"probe (L17 test field): HP {r.hp}, range {r.range} px; build lists {(r.listOk ? "OK (forts only, every level, after the Laser / last robot; no factory; fort shown TECH VII; none with DV45 off)" : "WRONG")}, retired D1 toggle {(r.allLevelsOk ? "OK (changes nothing)" : "WRONG")}; bolts at ticks {r.bolts} after the engage ({r.cycleMin}-{r.cycleMax} ticks apart); {r.damage} HP per bolt, the {r.craneHp} HP crane dead on bolt {r.craneBolts}, a bystander 6 px off it {r.bystanderLoss} HP while the crane stood; " +
                           $"Tough under a move order: bolt 2 came {r.toughHold1} ticks after bolt 1, stun ended {r.toughEndAfter} ticks after the last bolt, it moved {r.toughMoveAfter} tick(s) after the stun's end, fired {r.toughRockets} rockets while stunned, {r.toughHp} HP left; light tank: held {r.lightHold} ticks, moved {r.lightMoveAfter} tick(s) after the end, fired {r.lightShells} shells while stunned; " +
                           $"gun: {r.gunBolts} bolts, {r.gunStuns} stuns; grunt: killed by 1 bolt {r.gruntKilled}, {r.gruntStuns} stuns; Electrobot target: {r.elbStuns} stuns; an attack order on the enemy fort dropped {r.noBuildingTarget}; Zod's rand() stream equal with and without him {r.rngEqual}; two runs identical (event hash) {r.repeatEqual}",
                toggle = $"ZWorld.ElectrobotDeviation (PlayerPrefs oz_electrobot = {(getInt("oz_electrobot", 1) == 1 ? "1 (on, default)" : "0 (off)")}; menu OpenZ/Deviations/Electrobot; off in 'Zod exact' oz_zod_exact 1 and in every Zod row: Zod's build lists, no Electrobot); " +
                         "D1 all-levels toggle retired (forts only); full checklist: electrobot_v1 sim harness",
                status = !ok ? DevCheck : pref ? DevActive : DevActive + " (off by pref)",
            };
        }

        /// <summary>DV46 probe: bot vs bot, seed 1, `max` s: Electrobots built per side (first one at), at most one alive per CPU, never an
        /// ENTER order on him, none on L02 (no level-4 producer on Hard / Normal).</summary>
        static (int red, int blue, double firstRed, double firstBlue, int maxAlive, int enterOrders) ElectrobotBotProbe(string lv, double max, bool allLevels = false)
        {
            bool d = ZWorld.ElectrobotDeviation, b = ZWorld.ElectrobotBotDeviation, all = ZWorld.ElectrobotAllLevels;
            ZWorld.ElectrobotDeviation = ZWorld.ElectrobotBotDeviation = true; ZWorld.ElectrobotAllLevels = allLevels;
            try
            {
                var w = ZWorld.Create(SettingsText, MapText(lv), 1, lv == "L17");
                ZBot.Attach(w, Team.Red, true, 1);
                int red = 0, blue = 0, maxAlive = 0, enter = 0; double fr = -1, fb = -1;
                var marked = new HashSet<int>(); var inherited = new HashSet<int>();                // a factory captured mid-build delivers to its new owner (Zod)
                while (w.Time < max && !w.GameOver)
                {
                    w.Tick(ZConst.TICK);
                    foreach (var ev in w.Events)
                    {
                        if (ev.type == ZEventType.OwnerChanged) { var pb = w.GetAny(ev.objId); if (pb != null && pb.IsProducer && pb.Building && pb.CurUnit == ZElectro.Type) marked.Add(pb.Id); }
                        if (ev.type != ZEventType.UnitBuilt) continue;
                        var u = w.GetAny(ev.objId); if (u == null || u.Type != ZElectro.Type) continue;
                        if (u.Owner == Team.Red) { red++; if (fr < 0) fr = w.Time; } else { blue++; if (fb < 0) fb = w.Time; }
                        var bot = w.Bots.FirstOrDefault(x => x.Team == u.Owner);
                        if (marked.Contains(ev.otherId) && !(bot != null && bot.ElectrobotProducerId == ev.otherId)) inherited.Add(u.Id);
                        marked.Remove(ev.otherId);
                    }
                    w.Events.Clear();
                    if (w.TickIndex % 100 != 0) continue;
                    foreach (var team in new[] { Team.Red, Team.Blue })
                        maxAlive = Math.Max(maxAlive, w.Objects.Count(o => o.Owner == team && !o.Destroyed && ((o.IsElectrobot && o.KillAt < 0 && !inherited.Contains(o.Id)) || ((o.Kind == ZKind.Vehicle || o.Kind == ZKind.Cannon) && ((o.DriverType == ZElectro.Type && o.Drivers.Count > 0) || (o.PassengerType == ZElectro.Type && o.Passengers.Count > 0))))));
                    foreach (var o in w.Objects) if (o.IsElectrobot && o.Waypoints.Count > 0 && o.Waypoints[0].mode == WpMode.Enter) enter++;
                }
                return (red, blue, fr, fb, maxAlive, enter);
            }
            finally { ZWorld.ElectrobotDeviation = d; ZWorld.ElectrobotBotDeviation = b; ZWorld.ElectrobotAllLevels = all; }
        }

        static Deviation DevElectrobotBot(Func<string, int, int> getInt)
        {
            var a = ElectrobotBotProbe("L17", 400); var c = ElectrobotBotProbe("L02", 400);
            bool ok = a.red + a.blue >= 1 && a.maxAlive <= 1 && a.enterOrders == 0 && c.maxAlive <= 1 && c.enterOrders == 0;
            bool pref = getInt("oz_electrobot_cpu", 1) == 1 && getInt("oz_electrobot", 1) == 1 && getInt("oz_zod_exact", 0) == 0;
            string T(double t) => t < 0 ? "-" : t.ToString("0", CultureInfo.InvariantCulture) + " s";
            return new Deviation
            {
                id = "DV46", change = "The CPU builds one Electrobot at a time",
                zod = "zbot.cpp:20-27 preferred list (medium, light, jeep, pyro, tough, sniper, tough, psycho), first match wins (zbot.cpp:1372-1432): Zod's bot never builds a Laser, so it would never build an Electrobot",
                openz = "each production round, with the same STOP / START commands: when the CPU has no Electrobot (on foot or aboard) and none in production, its first producer that can make one builds it, then goes back to Zod's pick (or, where Zod's bot has no pick for that building, to its default production, list entry 0); the CPU never orders its Electrobot into a vehicle or cannon; no random draw. A factory captured mid-build still delivers its Electrobot to the new owner (Zod's capture rule, zserver.cpp:2009-2063), so a CPU can briefly own a second one that way",
                measured = $"bot vs bot seed 1, 400 s: L17 Electrobots built red {a.red} (first at {T(a.firstRed)}), blue {a.blue} (first at {T(a.firstBlue)}), at most {a.maxAlive} alive per CPU, {a.enterOrders} enter orders on him; " +
                           $"L02, 400 s (from the forts): red {c.red} (first at {T(c.firstRed)}), blue {c.blue} (first at {T(c.firstBlue)}), at most {c.maxAlive} alive per CPU, {c.enterOrders} enter orders",
                toggle = $"ZWorld.ElectrobotBotDeviation (PlayerPrefs oz_electrobot_cpu = {(getInt("oz_electrobot_cpu", 1) == 1 ? "1 (on, default)" : "0 (off)")}; needs DV45; menu OpenZ/Deviations/CPU Electrobot; off in 'Zod exact'); bot-vs-bot balance: electrobot_v1 sim/reports",
                status = !ok ? DevCheck : pref ? DevActive : DevActive + " (off by pref)",
            };
        }

#if !OPENZ_HARNESS
        const string MenuElectrobot = "OpenZ/Deviations/Electrobot (oz_electrobot)", MenuElectrobotCpu = "OpenZ/Deviations/CPU Electrobot (oz_electrobot_cpu)";
        [MenuItem(MenuElectrobot)] static void TElectrobot() => NbToggle("oz_electrobot", 1, "Electrobot (DV45)");
        [MenuItem(MenuElectrobot, true)] static bool TElectrobotC() { Menu.SetChecked(MenuElectrobot, PlayerPrefs.GetInt("oz_electrobot", 1) == 1); return true; }
        [MenuItem(MenuElectrobotCpu)] static void TElectrobotCpu() => NbToggle("oz_electrobot_cpu", 1, "CPU Electrobot (DV46)");
        [MenuItem(MenuElectrobotCpu, true)] static bool TElectrobotCpuC() { Menu.SetChecked(MenuElectrobotCpu, PlayerPrefs.GetInt("oz_electrobot_cpu", 1) == 1); return true; }

        [MenuItem("OpenZ/Deviations/Probe DV45 DV46 Electrobot")]
        static void ProbeElectrobotMenu()
        {
            Func<string, int, int> get = (k, def) => PlayerPrefs.GetInt(k, def);
            var d45 = DevElectrobot(get); var d46 = DevElectrobotBot(get);
            var path = System.IO.Path.Combine(OpenZPaths.Records, "electrobot_dv45_dv46.md");
            System.IO.File.WriteAllText(path, $"# DV45 / DV46 Electrobot probe {DateTime.Now:yyyy-MM-dd HH:mm}\n\n## DV45 {d45.status}\n\n- {d45.measured.Replace("; ", "\n- ")}\n\n## DV46 {d46.status}\n\n- {d46.measured.Replace("; ", "\n- ")}\n");
            Debug.Log($"[DV45] {d45.status}: {d45.measured}\n[DV46] {d46.status}: {d46.measured}\n-> {path}");
        }
#endif
    }
}
