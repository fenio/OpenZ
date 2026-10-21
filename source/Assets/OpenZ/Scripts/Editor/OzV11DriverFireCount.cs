using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Counted check of kit b11_driver_fire (DV87: hatch drivers fire their own weapon). Headless simulation on a plain map with
    /// one bridge, one fort, one robot factory, one vehicle factory and one repair station (all nobody's), like the check of kit b1.
    /// Part 1: per tank (light, medium, heavy) and per driver type, staged fights of the tank (it cannot move, be destroyed or lose
    /// its driver; its own shells are made harmless so every lost point is the driver's) against a red grunt squad that cannot die,
    /// 100 px away, with the rule on and off. Part 2: a veteran crew. Part 3: a resting crew. Part 4: on a bridge, in a door lane
    /// and on control spots beside them, standing and driving over the bridge. Part 5: the APC. Part 6: a tank whose driver is
    /// shot out.
    /// </summary>
    public static class OzV11DriverFireCount
    {
        public const int Runs = 20;
        public const double FightSeconds = 20;
        const int Big = 1000000000;
        static readonly string[] Tanks = { "light", "medium", "heavy" };
        static readonly string[] DriverTypes = { "grunt", "psycho", "sniper", "tough", "pyro", "laser", ZElectro.Type };
        const int FX = 1250, FY = 300;                                                       // the open ground the staged fights stand on (px)

        // tiles: bridge_vert 4 x 5 at (20,20); fort_front 10 x 12 at (40,10); robot_factory 4 x 5 at (60,40); vehicle_factory 4 x 5 at (20,60); repair 5 x 4 at (60,70)
        static string Map() => OzV11Kit.Plain(96, 96, ("building:bridge_vert", 20, 20, "null"), ("building:fort_front", 40, 10, "null"), ("building:robot_factory", 60, 40, "null"),
                                                      ("building:vehicle_factory", 20, 60, "null"), ("building:repair", 60, 70, "null"));

        static ZWorld World(string settings, string map, int seed)
        {
            var w = OzV11Kit.World(settings, map, seed);
            var d = w.CreateRobotGroup("grunt", 40, 1480, Team.Red, 1);                      // a red robot far away that cannot move, shoot or die: the game is never over
            d.MoveSpeed = 0; d.Damage = 0; d.MaxHealth = d.Health = Big;
            OzV11Kit.Dummy(w, 80, 1480);                                                     // and a blue one
            return w;
        }

        /// <summary>A crewed blue vehicle that stands still and cannot be destroyed; its driver is of the given robot type.</summary>
        static ZObject Vehicle(ZWorld w, string type, string driver, int cx, int cy, bool driverCannotDie = true)
        {
            var t = OzV11Kit.Place(w, type, cx, cy, Team.Blue);
            t.DriverType = driver; t.Drivers[0] = driverCannotDie ? Big : w.S.Unit(driver).MaxHealth;
            t.MoveSpeed = 0; t.RealMoveSpeed = 0; t.MaxHealth = t.Health = Big;
            t.ResetDamageInfo();
            return t;
        }

        static ZObject Enemies(ZWorld w, string type, int cx, int cy, ZObject target, bool harmless = false)
        {
            var sq = OzV11Kit.Place(w, type, cx, cy, Team.Red);
            foreach (var m in OzV11Kit.Squad(sq)) { m.MaxHealth = m.Health = Big; if (harmless) { m.Damage = 0; m.MoveSpeed = 0; m.RealMoveSpeed = 0; } }   // harmless: they stand (no stepping aside for a shell)
            if (!harmless && target != null) OzV11Kit.PlayerAttack(w, sq, target);
            return sq;
        }

        static long Lost(ZObject sq) { long s = 0; foreach (var m in OzV11Kit.Squad(sq)) s += Big - m.Health; return s; }

        sealed class Tally
        {
            public int runs, lidRuns, lidRunsWithShots, shots, hits, rockets, bolts, shutShots, blasts, badBlasts, badBolts;
            public long damage; public double upTime, gapSum, firstMin = double.MaxValue; public int gaps;
        }

        /// <summary>One staged fight of part 1. The tank's own shells burst without a blast (splash radius 0), so the squad only loses
        /// what the driver takes; realGun = the gun as it is.</summary>
        static void Fight(string settings, string map, int seed, string tank, string driver, double seconds, Tally a, bool veteran = false, bool realGun = false)
        {
            var w = World(settings, map, seed);
            var t = Vehicle(w, tank, driver, FX, FY);
            if (!realGun) t.DamageRadius = 0;                                                // its shells burst without a blast: no point lost to the tank's own gun
            t.Veteran = veteran;
            var sq = Enemies(w, "grunt", FX + 100, FY, t);
            double lidAt = -1, lastShot = -1; int shots = 0; bool lidEver = false;
            while (w.Time < seconds)
            {
                w.Tick(ZConst.TICK);
                if (t.LidOpen && lidAt < 0) lidAt = w.Time;
                if (t.LidOpen) { lidEver = true; a.upTime += ZConst.TICK; }
                foreach (var e in w.Events)
                {
                    if (e.type == ZEventType.MissileExploded && e.driver && e.objId == t.Id)
                    { a.blasts++; if ((int)e.value != t.DriverGunDamage || e.radius != w.S.Unit("tough").attack_damage_radius) a.badBlasts++; }
                    if (e.type != ZEventType.DriverShot || e.objId != t.Id) continue;
                    shots++; a.shots++;
                    if (!t.LidOpen) a.shutShots++;
                    if (e.radius > 0) { a.rockets++; a.hits++; }
                    else if (driver == ZElectro.Type) { a.bolts++; a.hits++; if ((int)e.value != t.DriverGunDamage) a.badBolts++; }
                    else if (e.flag) a.hits++;
                    if (lastShot >= 0) { a.gapSum += w.Time - lastShot; a.gaps++; }
                    else if (lidAt >= 0) a.firstMin = Math.Min(a.firstMin, w.Time - lidAt);
                    lastShot = w.Time;
                }
                w.Events.Clear();
            }
            a.runs++; if (lidEver) { a.lidRuns++; if (shots > 0) a.lidRunsWithShots++; }
            a.damage += Lost(sq);
        }

        /// <summary>(name, tank centre, grunt squad centre, kit b1 protects the spot).</summary>
        static List<(string name, int x, int y, int sx, int sy, bool safe)> Spots(ZWorld w)
        {
            var l = new List<(string, int, int, int, int, bool)>();
            var br = w.Objects.First(o => o.IsBridge);
            int bx = br.LocX + br.WidthPix / 2, top = br.LocY, mid = br.LocY + br.HeightPix / 2;
            l.Add(("bridge: middle of the deck", bx, mid, bx + 92, mid, true));
            l.Add(("before the bridge: hull 32 px off its north end", bx, top - 48, bx + 92, top - 48, true));
            l.Add(("road: hull 160 px off the north end (control)", bx, top - 176, bx + 92, top - 176, false));
            foreach (var b in w.Objects.Where(o => o.Kind == ZKind.Building && !o.IsBridge && o.Type != "repair").OrderBy(o => o.Id))
            {
                int ex, ey;
                if (!b.GetCreationMovePoint(out ex, out ey)) continue;
                string n = b.Type == "fort_front" ? "fort gate" : b.Type == "robot_factory" ? "robot factory door" : "vehicle factory door";
                l.Add(($"{n}: on the exit point", ex, ey, ex + 70, ey + 60, true));
                if (b.Type == "fort_front") l.Add(($"{n}: 100 px out along the exit line (control)", ex, ey + 100, ex + 70, ey + 160, false));
                if (b.Type == "robot_factory") l.Add(($"{n}: 96 px to the side of the exit point (control)", ex + 96, ey + 16, ex + 96, ey + 108, false));
            }
            return l;
        }

        /// <summary>The whole check. settings = default_settings. Returns the report.</summary>
        public static string Run(string settings, int runs = Runs)
        {
            var sb = new StringBuilder();
            var st = ZSettings.Parse(settings);
            string map = Map();
            bool was = ZWorld.DriverFireDeviation;
            int held0 = ZWorld.HatchesHeld;
            double pop = ZWorld.CraneGunPopSeconds;

            // ---------------------------------------------------------------- part 1: every driver type in every tank
            sb.AppendLine($"Part 1, the driver's own weapon: {runs} fights of {FightSeconds:0} s per tank and driver type against a grunt squad 100 px away (the hatch opens 4 times in 5, Zod's roll).");
            sb.AppendLine("tank | driver | rule | fights with the hatch open | of those with driver shots | driver shots | hits | points per hit | seconds between shots | first shot after the hatch opened | points per second while the hatch is open | shots with the hatch shut");
            var perDriver = new Dictionary<string, Tally>(); var offShots = new Dictionary<string, int>();
            var balance = new List<string>(); var gunDps = new Dictionary<string, double>();
            ZWorld.DriverFireDeviation = false;
            foreach (string tank in Tanks)
            {   // the tank's own gun on the same squad, as in release 1.0 (the rule off, the gun as it is)
                var g = new Tally();
                for (int seed = 1; seed <= runs; seed++) Fight(settings, map, seed, tank, "grunt", FightSeconds, g, false, true);
                gunDps[tank] = g.damage / (g.runs * FightSeconds);
            }
            foreach (string drv in DriverTypes)
            {
                var all = new Tally(); perDriver[drv] = all; offShots[drv] = 0;
                foreach (string tank in Tanks)
                {
                    foreach (bool rule in new[] { true, false })
                    {
                        ZWorld.DriverFireDeviation = rule;
                        var a = new Tally();
                        for (int seed = 1; seed <= runs; seed++) Fight(settings, map, seed, tank, drv, rule ? FightSeconds : FightSeconds / 2, a);
                        string perHit = a.hits > 0 ? (a.damage / (double)a.hits).ToString("0.#") : "-";
                        sb.AppendLine($"{tank} | {drv} | {(rule ? "on" : "off")} | {a.lidRuns} of {a.runs} | {a.lidRunsWithShots} | {a.shots} | {a.hits} | {perHit} | {(a.gaps > 0 ? a.gapSum / a.gaps : 0):0.000} | {(a.firstMin < double.MaxValue ? a.firstMin : 0):0.00} | {(a.upTime > 0 ? a.damage / a.upTime : 0):0} | {a.shutShots}");
                        if (!rule) { offShots[drv] += a.shots; continue; }
                        all.runs += a.runs; all.lidRuns += a.lidRuns; all.lidRunsWithShots += a.lidRunsWithShots; all.shots += a.shots; all.hits += a.hits; all.rockets += a.rockets;
                        all.bolts += a.bolts; all.shutShots += a.shutShots; all.blasts += a.blasts; all.badBlasts += a.badBlasts; all.badBolts += a.badBolts; all.damage += a.damage;
                        all.upTime += a.upTime; all.gapSum += a.gapSum; all.gaps += a.gaps; all.firstMin = Math.Min(all.firstMin, a.firstMin);
                        double gun = gunDps[tank], drvDps = a.upTime > 0 ? a.damage / a.upTime : 0;
                        balance.Add($"{tank} | {drv} | {gun:0} | {drvDps:0} | +{(gun > 0 ? 100.0 * drvDps / gun : 0):0} %");
                    }
                }
            }
            sb.AppendLine($"What the driver adds while the hatch is open, on the same grunt squad (the tank's own gun: measured with the rule off and the gun as it is, {runs} fights of {FightSeconds:0} s; the driver: the table above):");
            sb.AppendLine("tank | driver | the tank's gun, points per second | the driver, points per second | the driver adds");
            foreach (var l in balance) sb.AppendLine(l);

            bool okFire = true, okShut = true, okKind = true, okNumbers = true, okOff = true; var bad = new List<string>();
            foreach (string drv in DriverTypes)
            {
                var a = perDriver[drv]; var u = st.Unit(drv);
                int dmg = drv == "grunt" ? u.Damage * ZWorld.CrewRifleMul : u.Damage;
                double cycle = drv == ZElectro.Type ? ZElectro.Seconds(ZElectro.CycleTicks) : u.attack_speed, gap = a.gaps > 0 ? a.gapSum / a.gaps : 0;
                if (a.lidRuns == 0 || a.lidRunsWithShots != a.lidRuns) { okFire = false; bad.Add($"{drv}: shots in {a.lidRunsWithShots} of {a.lidRuns} open-hatch fights"); }
                if (a.shutShots != 0) { okShut = false; bad.Add($"{drv}: {a.shutShots} shots with the hatch shut"); }
                bool kind = drv == "tough" ? a.rockets == a.shots && a.badBlasts == 0 && a.blasts > 0 && a.blasts <= a.rockets
                          : drv == ZElectro.Type ? a.bolts == a.shots && a.badBolts == 0 && a.rockets == 0
                          : a.rockets == 0 && a.bolts == 0 && a.blasts == 0;
                if (!kind) { okKind = false; bad.Add($"{drv}: wrong projectile (rockets {a.rockets}, bolts {a.bolts}, driver blasts {a.blasts}, wrong blasts {a.badBlasts}, wrong bolts {a.badBolts} of {a.shots} shots)"); }
                bool num = gap >= cycle - 0.001 && gap <= cycle + 0.011 && a.firstMin >= pop - 0.005;
                if (drv != "tough") num &= a.damage == (long)a.hits * dmg;                           // every hit takes exactly the table's points (a rocket's blast falls off with the distance)
                else num &= a.damage > 0;
                double p = u.attack_damage_chance, tol = Math.Max(0.04, 4 * Math.Sqrt(p * (1 - p) / Math.Max(1, a.shots)));   // 4 standard deviations of the count
                if (drv != "tough" && drv != ZElectro.Type) num &= Math.Abs(a.hits / (double)Math.Max(1, a.shots) - p) <= tol;
                if (!num) { okNumbers = false; bad.Add($"{drv}: numbers off (points {a.damage} for {a.hits} hits, table {dmg}; cycle {gap:0.000}, table {cycle:0.000}; hits {a.hits} of {a.shots}, table {u.attack_damage_chance:0.00}; first shot after {a.firstMin:0.00} s, rule {pop:0.0})"); }
                if (offShots[drv] != 0) { okOff = false; bad.Add($"{drv}: {offShots[drv]} driver shots with the rule off"); }
            }

            // ---------------------------------------------------------------- part 2: veteran crews never fire (they are never shown, DV12)
            ZWorld.DriverFireDeviation = true;
            var vet = new Tally();
            foreach (string tank in Tanks) for (int seed = 1; seed <= runs; seed++) Fight(settings, map, seed, tank, seed % 2 == 0 ? "laser" : "grunt", FightSeconds / 2, vet, true);
            sb.AppendLine($"Part 2, veteran crews (grunt and laser drivers, {vet.runs} fights): the lid opened in {vet.lidRuns} fights, driver shots {vet.shots}.");

            // ---------------------------------------------------------------- part 3: a resting crew does not fire until it is woken
            int restRuns = 0, restLid = 0, restShots = 0, wokenLid = 0, wokenWithShots = 0;
            foreach (string tank in Tanks)
                for (int seed = 1; seed <= runs; seed++)
                {
                    var w = World(settings, map, seed);
                    var t = Vehicle(w, tank, "grunt", FX, FY); t.DamageRadius = 0;
                    while (w.Time < 40 && !t.Resting) { w.Tick(ZConst.TICK); w.Events.Clear(); }
                    if (!t.Resting) continue;
                    restRuns++;
                    Enemies(w, "grunt", FX + 100, FY, t, true);                               // harmless: the crew is woken by seeing them, 4 s
                    double end = w.Time + 14; bool lidInRest = false, lidAfter = false; int after = 0;
                    while (w.Time < end)
                    {
                        w.Tick(ZConst.TICK);
                        if (t.Resting && t.LidOpen) lidInRest = true;
                        if (!t.Resting && t.LidOpen) lidAfter = true;
                        foreach (var e in w.Events) if (e.type == ZEventType.DriverShot && e.objId == t.Id) { if (t.Resting) restShots++; else after++; }
                        w.Events.Clear();
                    }
                    if (lidInRest) restLid++;
                    if (lidAfter) { wokenLid++; if (after > 0) wokenWithShots++; }
                }
            sb.AppendLine($"Part 3, resting crews ({restRuns} tanks at rest, then a grunt squad appears 100 px away): while the crew still rested the lid was open in {restLid} runs, driver shots in that time {restShots}; " +
                          $"once woken, the hatch was open in {wokenLid} runs and the driver fired in {wokenWithShots} of them.");

            // ---------------------------------------------------------------- part 4: bridge and door lanes
            var spots = Spots(World(settings, map, 1));
            sb.AppendLine($"Part 4, bridges and doors: a light tank with a grunt driver on the spot, a grunt squad 92 px away, {runs} fights of 12 s per spot.");
            sb.AppendLine("spot | rule | fights with the hatch open | driver shots | shots of the tank's own gun");
            int safeLidOn = 0, safeShotsOn = 0, safeGunOn = 0, safeLidOff = 0, ctrlRuns = 0, ctrlLidOn = 0, ctrlFired = 0;
            foreach (var s in spots)
                foreach (bool rule in new[] { false, true })
                {
                    ZWorld.DriverFireDeviation = rule;
                    int lid = 0, shots = 0, gun = 0, fired = 0;
                    for (int seed = 1; seed <= runs; seed++)
                    {
                        var w = World(settings, map, seed);
                        var t = Vehicle(w, "light", "grunt", s.x, s.y); t.DamageRadius = 0;
                        Enemies(w, "grunt", s.sx, s.sy, t);
                        bool open = false; int mine = 0;
                        while (w.Time < 12)
                        {
                            w.Tick(ZConst.TICK);
                            if (t.LidOpen) open = true;
                            foreach (var e in w.Events)
                            {
                                if (e.type == ZEventType.DriverShot && e.objId == t.Id) mine++;
                                if (e.type == ZEventType.MissileFired && e.objId == t.Id) gun++;
                            }
                            w.Events.Clear();
                        }
                        if (open) { lid++; if (mine > 0) fired++; }
                        shots += mine;
                    }
                    sb.AppendLine($"{s.name} | {(rule ? "on" : "off")} | {lid} of {runs} | {shots} | {gun}");
                    if (s.safe) { if (rule) { safeLidOn += lid; safeShotsOn += shots; safeGunOn += gun; } else safeLidOff += lid; }
                    else if (rule) { ctrlRuns += runs; ctrlLidOn += lid; ctrlFired += fired; }
                }
            ZWorld.DriverFireDeviation = true;
            int shutNow = 0, shutOf = 0;
            foreach (var s in spots.Where(x => x.safe))
            {   // a tank that arrives with its lid open: the lid shuts
                var w = World(settings, map, 1);
                var t = Vehicle(w, "light", "grunt", s.x, s.y);
                t.LidOpen = true; shutOf++;
                while (w.Time < 0.5) { w.Tick(ZConst.TICK); w.Events.Clear(); }
                if (!t.LidOpen) shutNow++;
            }
            sb.AppendLine($"A lid that is open when the tank stands on a protected spot: shut after 0.5 s on {shutNow} of {shutOf} spots.");

            // a tank and an APC that DRIVE over the bridge past enemies beside the road: the driver comes up before the bridge and must be down on it
            sb.AppendLine($"Driving over the bridge past seven grunt squads beside the road, {runs} crossings per row; the protected strip is the deck and {ZWorld.SnipeSafeBridgeReach} px before each end.");
            sb.AppendLine("vehicle | driver | crossed | crossings with the driver up at some time | his shots off the strip | his shots on the strip | longest time he was up on the strip");
            int crossRows = 0, crossDone = 0, crossShotsOn = 0, crossUpTicksOn = 0; bool crossLive = true;
            foreach (var c in new[] { (veh: "light", drv: "grunt"), (veh: "light", drv: "psycho"), (veh: "apc", drv: "grunt") })
            {
                int crossed = 0, upRuns = 0, onStrip = 0, offStrip = 0, worst = 0;
                for (int seed = 1; seed <= runs; seed++)
                {
                    var w = World(settings, map, seed);
                    var br = w.Objects.First(o => o.IsBridge);
                    int bx = br.LocX + br.WidthPix / 2, top = br.LocY, bot = br.LocY + br.HeightPix;
                    var t = OzV11Kit.Place(w, c.veh, bx, top - 260, Team.Blue);
                    t.DriverType = c.drv; t.Drivers[0] = Big; t.MaxHealth = t.Health = Big; t.DamageRadius = 0; t.ResetDamageInfo();
                    foreach (int y in new[] { top - 200, top - 120, top - 40, (top + bot) / 2, bot + 40, bot + 120, bot + 200 }) Enemies(w, "grunt", bx + 96, y, t, true);
                    w.SendWaypoints(Team.Blue, t, new[] { new ZWaypoint { mode = WpMode.Move, refId = -1, x = bx, y = bot + 260, attackTo = false, playerGiven = true } });
                    bool everUp = false; int stretch = 0;
                    while (w.Time < 90 && t.CenterY < bot + 250)
                    {
                        w.Tick(ZConst.TICK);
                        bool strip = w.SnipeSafeSpot(t), up = t.IsApc ? t.ApcDriverUp : t.LidOpen;       // where it stands and who is up after this tick
                        if (up) everUp = true;
                        if (strip && up) { stretch++; if (stretch > worst) worst = stretch; } else stretch = 0;
                        foreach (var e in w.Events)
                        {   // his shots: a tank driver's own DriverShot; the APC's shots while its driver is up (down, it fires from the side ports as in 1.0)
                            bool his = e.objId == t.Id && (t.IsApc ? e.type == ZEventType.Hitscan && up : e.type == ZEventType.DriverShot);
                            if (his) { if (strip) onStrip++; else offStrip++; }
                        }
                        w.Events.Clear();
                    }
                    if (t.CenterY >= bot + 250) crossed++;
                    if (everUp) upRuns++;
                }
                sb.AppendLine($"{c.veh} | {c.drv} | {crossed} of {runs} | {upRuns} | {offStrip} | {onStrip} | {worst} ticks = {worst * ZConst.TICK:0.00} s");
                crossRows += runs; crossDone += crossed; crossShotsOn += onStrip; crossUpTicksOn = Math.Max(crossUpTicksOn, worst);
                if (upRuns == 0 || offStrip == 0) crossLive = false;
            }
            sb.AppendLine($"Lids the rule kept shut or shut in this whole check: {ZWorld.HatchesHeld - held0}.");

            // ---------------------------------------------------------------- part 5: the APC
            sb.AppendLine($"Part 5, the APC: a grunt squad 100 px away attacks it, {runs} fights of 16 s per case; at 10 s the squad is removed.");
            sb.AppendLine("case | fights with the driver up | APC shots | hits | points per hit | seconds between shots | first shot after he began to come up | back inside after the squad was gone");
            var apc = new Dictionary<string, (int up, int shots, long dmg, int hits, double first, double downMin, double downMax, double gap)>();
            foreach (string c in new[] { "driver only", "driver only, rule off", "driver and a grunt squad aboard", "driver only, on the bridge", "driver only, veteran" })
            {
                ZWorld.DriverFireDeviation = c != "driver only, rule off";
                int up = 0, shots = 0, hits = 0, gaps = 0; long dmg = 0; double first = double.MaxValue, downMin = double.MaxValue, downMax = 0, gapSum = 0;
                var bridgeSpot = spots[0];
                for (int seed = 1; seed <= runs; seed++)
                {
                    var w = World(settings, map, seed);
                    bool onBridge = c == "driver only, on the bridge";
                    int ax = onBridge ? bridgeSpot.x : FX, ay = onBridge ? bridgeSpot.y : FY;
                    var t = Vehicle(w, "apc", "grunt", ax, ay);
                    if (c == "driver and a grunt squad aboard") { t.PassengerType = "grunt"; for (int i = 0; i < 3; i++) t.Passengers.Add(w.S.Unit("grunt").MaxHealth); t.ResetDamageInfo(); }
                    if (c == "driver only, veteran") t.Veteran = true;
                    var sq = Enemies(w, "grunt", ax + (onBridge ? 92 : 100), ay, t);
                    double upAt = -1, shotAt = -1, goneAt = -1, downAt = -1, last = -1; bool everUp = false; long lost = 0;
                    while (w.Time < 16)
                    {
                        w.Tick(ZConst.TICK);
                        if (t.ApcDriverUp) { everUp = true; if (upAt < 0) upAt = w.Time; }
                        else if (everUp && goneAt >= 0 && downAt < 0) downAt = w.Time;
                        foreach (var e in w.Events)
                        {
                            if (e.type != ZEventType.Hitscan || e.objId != t.Id) continue;
                            shots++; if (e.flag) hits++;
                            if (shotAt < 0) shotAt = w.Time;
                            if (last >= 0) { gapSum += w.Time - last; gaps++; }
                            last = w.Time;
                        }
                        w.Events.Clear();
                        if (goneAt < 0 && w.Time >= 10)
                        {
                            lost = Lost(sq);
                            foreach (var m in OzV11Kit.Squad(sq).ToList()) { m.SetHealth(0); w.UpdateObjectHealth(m); }
                            goneAt = w.Time;
                        }
                    }
                    dmg += lost;
                    if (everUp) { up++; if (shotAt >= 0) first = Math.Min(first, shotAt - upAt); if (downAt >= 0) { downMin = Math.Min(downMin, downAt - goneAt); downMax = Math.Max(downMax, downAt - goneAt); } else downMax = double.MaxValue; }
                }
                apc[c] = (up, shots, dmg, hits, first, downMin, downMax, gaps > 0 ? gapSum / gaps : 0);
                string f = first < double.MaxValue ? first.ToString("0.00") + " s" : "-", dn = up > 0 && downMax < double.MaxValue ? $"{downMin:0.00} to {downMax:0.00} s" : up > 0 ? "not in time" : "-";
                sb.AppendLine($"{c} | {up} of {runs} | {shots} | {hits} | {(hits > 0 ? (dmg / (double)hits).ToString("0.#") : "-")} | {(gaps > 0 ? gapSum / gaps : 0):0.000} | {f} | {dn}");
            }

            // ---------------------------------------------------------------- part 6: a tank whose driver is shot out has nobody to fire
            int outRuns = 0, shotsAfter = 0, stillUp = 0;
            for (int seed = 1; seed <= runs; seed++)
            {
                var w = World(settings, map, seed);
                var t = Vehicle(w, "light", "grunt", FX, FY, false); t.DamageRadius = 0; t.Drivers[0] = 1;
                Enemies(w, "sniper", FX + 100, FY, t);
                bool gone = false;
                while (w.Time < 12)
                {
                    w.Tick(ZConst.TICK);
                    foreach (var e in w.Events)
                    {
                        if (e.type == ZEventType.Sniped && e.objId == t.Id) gone = true;
                        else if (gone && e.type == ZEventType.DriverShot && e.objId == t.Id) shotsAfter++;
                    }
                    w.Events.Clear();
                    if (gone && t.DriverFireUp) stillUp++;
                }
                if (gone) outRuns++;
            }
            sb.AppendLine($"Part 6, an emptied tank (a sniper squad shoots the driver out, {runs} fights): shot out in {outRuns}, driver shots afterwards {shotsAfter}.");

            ZWorld.DriverFireDeviation = was;
            var on = apc["driver only"]; var off = apc["driver only, rule off"]; var full = apc["driver and a grunt squad aboard"]; var brd = apc["driver only, on the bridge"]; var vt = apc["driver only, veteran"];
            int rifle = st.Unit("grunt").Damage;
            sb.AppendLine(OzV11Kit.Verdict(okFire, "rule on: in every fight with the hatch open the driver fired, for all " + DriverTypes.Length + " driver types in all three tanks" + Tail(bad, "open-hatch")));
            sb.AppendLine(OzV11Kit.Verdict(okKind, "every driver fires his own projectile (Tough: rockets with his blast; Electrobot: bolts; the others: direct fire)" + Tail(bad, "projectile")));
            sb.AppendLine(OzV11Kit.Verdict(okNumbers, $"points per hit, cycle and hit share are the rule table's (the grunt: {rifle * ZWorld.CrewRifleMul} points, the crane driver's rifle), first shot {pop:0.0} s after the hatch opened" + Tail(bad, "numbers")));
            sb.AppendLine(OzV11Kit.Verdict(okShut && okOff, "0 driver shots with the hatch shut and 0 with the rule off" + Tail(bad, "hatch shut") + Tail(bad, "rule off")));
            sb.AppendLine(OzV11Kit.Verdict(vet.shots == 0 && vet.lidRuns > 0, $"veteran crews never fire from the hatch (shots {vet.shots}, lid open in {vet.lidRuns} of {vet.runs} fights)"));
            sb.AppendLine(OzV11Kit.Verdict(restShots == 0 && restLid > 0 && wokenLid > 0 && wokenWithShots == wokenLid, $"a resting crew does not fire until it is woken (shots while resting {restShots}, lid open while resting in {restLid} runs; woken: fired in {wokenWithShots} of {wokenLid})"));
            sb.AppendLine(OzV11Kit.Verdict(safeLidOn == 0 && safeShotsOn == 0 && safeGunOn > 0 && safeLidOff > 0 && shutNow == shutOf, $"on a bridge and in a door lane the hatch stays shut and nobody fires from it, the tank's own gun fires on (hatch open {safeLidOn}, driver shots {safeShotsOn}, gun shots {safeGunOn}; with the rule off the hatch opened in {safeLidOff} fights there; an open lid shut on {shutNow} of {shutOf} spots)"));
            sb.AppendLine(OzV11Kit.Verdict(crossDone == crossRows && crossLive && crossShotsOn == 0 && crossUpTicksOn == 0,
                $"driving over a bridge under fire, a tank driver and the APC driver are down for the whole protected strip and fire before and after it (crossed {crossDone} of {crossRows}; his shots on the strip {crossShotsOn}; longest time up on the strip {crossUpTicksOn} ticks)"));
            sb.AppendLine(OzV11Kit.Verdict(ctrlLidOn * 10 >= ctrlRuns * 4 && ctrlFired == ctrlLidOn, $"beside those spots the hatch opens and the driver fires as everywhere (open in {ctrlLidOn} of {ctrlRuns} fights, fired in {ctrlFired})"));
            sb.AppendLine(OzV11Kit.Verdict(on.up == runs && on.shots > 0 && on.dmg == (long)on.hits * rifle * ZWorld.CrewRifleMul && on.first >= pop - 0.005 && on.downMin >= ZWorld.CraneGunLinger - 0.05 && on.downMax <= ZWorld.CraneGunLinger + 0.05,
                $"APC with its driver only: he comes up in every fight ({on.up} of {runs}), fires the crane driver's rifle ({rifle * ZWorld.CrewRifleMul} points per hit), first shot {on.first:0.00} s after he began to come up, back inside {on.downMin:0.00} to {(on.downMax < double.MaxValue ? on.downMax : 99):0.00} s after the last target"));
            sb.AppendLine(OzV11Kit.Verdict(full.up == 0 && full.shots > 0 && brd.up == 0 && brd.shots > 0 && vt.up == 0 && vt.shots > 0 && off.up == 0 && off.shots > 0,
                $"APC with a squad aboard, on a bridge, with a veteran crew, or with the rule off: nobody comes up and it fires from inside as before (up {full.up} / {brd.up} / {vt.up} / {off.up}; shots {full.shots} / {brd.shots} / {vt.shots} / {off.shots})"));
            sb.AppendLine(OzV11Kit.Verdict(outRuns > 0 && shotsAfter == 0 && stillUp == 0, $"an emptied tank has nobody to fire (driver shot out in {outRuns} of {runs} fights, shots afterwards {shotsAfter})"));
            return sb.ToString();
        }

        static string Tail(List<string> bad, string key)
        {
            var mine = bad.Where(b => b.Contains(key)).ToList();
            return mine.Count == 0 ? "" : " [" + string.Join("; ", mine) + "]";
        }

#if UNITY_EDITOR
        [UnityEditor.MenuItem("OpenZ/V1.1/Count Driver Shots From The Hatch (b11)")]
        static void Menu() => OzV11Kit.RunMenu("b11 driver fire", () => Run(OzV11Kit.SettingsText));
#endif
    }
}
