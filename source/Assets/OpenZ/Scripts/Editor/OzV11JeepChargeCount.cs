using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// DV121 the jeep runs robots over (Sim/ZObject.JeepCharge.cs), counted headless (ZWorld.Create + Tick, no scene, nothing saved),
    /// with the switches of a Play session (every rule a start turns on, the Options at their defaults) and the rule off ("before") and
    /// on ("after"). One crewed jeep and the robots on a plain map, both teams computer teams that give no orders of their own (red the
    /// player's team in the "player" rows: its bot is left out), the robots 100 px (6 tiles, inside the jeep's 120 px range) or close
    /// in front of the jeep, each fight its own world, up to 60 s, then 10 s more:
    ///  A. strong robots (1 Tough, 1 Laser, 1 Electrobot, the hero pair, a squad of 3 Toughs, 1 Sniper): wins, time to kill, jeep losses,
    ///     distance driven, the speed while it charges, how far it is from the spot it charged from 10 s after the kill;
    ///  B. Grunts / Psychos / a Pyro far (100 px): exactly the same fights before and after; C. close (40 px): run over; D. the speed;
    ///  E. one Grunt at 24 to 96 px: where the run-over starts; F. one Tough at 40 to 100 px; G. the cases where it must NOT charge (water in the way, an enemy gun by
    ///     the robot, its own squad in the way, the player's attack-move past a Tough) and the orders (a move order in the middle of a
    ///     charge; the computer's attack-move past a Tough: it runs him over and goes on with its order; the player's attack order on a
    ///     Tough from far: it drives into range, runs him over, drives back to where the charge began); H. a robot it runs over goes
    ///     first over the one it shoots (a Grunt walking past close while it shoots a squad farther off; a Tough beside a Grunt it saw
    ///     first) and a squad's call for help against a Tough out of its own chase's reach; I. fair chances, 20 seeds a row whatever the
    ///     others take: a Laser and the hero pair snipe the driver of a jeep that drives at them (the jeep wins 35-55 %), a Tough stands his
    ///     ground (he lets it come within about 2 to 3 tiles, then two true rockets 1 s apart: the jeep wins 25-65 % at every start distance
    ///     from 40 to 100 px, 40-50 % in all, beside a Grunt 30-55 %), the levers only in a charge, a veteran crew still buttoned up (DV12),
    ///     the light tank (no charge, and why).
    /// Menu OpenZ/V1.1/Jeep: DV121 Charge Check.
    /// </summary>
    public static class OzV11JeepChargeCount
    {
        const double Tick = ZConst.TICK;
        public const int W = 64, H = 48, Cx = 24 * 16, Cy = 24 * 16;

        /// <summary>The rules a Play session turns on besides OzV11StrikeCount.Game's (each set only when its kit is installed).</summary>
        public static readonly string[] PlayExtra = { "SniperHeadshotDeviation", "GroundFireDeviation", "HeroBoostDeviation", "VeteranHealDeviation", "FortSniperDeviation",
                                                      "SquadSplitDeviation", "PyroForceBurnDeviation", "SniperSteadyDeviation", "ShuttlePlayableDeviation" };

        /// <summary>The switches of a Play session, the charge as asked. Dispose the result to put everything back.</summary>
        public static OzV11Kit.Switches Play(bool charge)
        {
            var s = OzV11StrikeCount.Game(false);
            foreach (var n in PlayExtra) OzV11Kit.TrySet(typeof(ZWorld), n, true);
            OzV11Kit.TrySet(typeof(ZWorld), "ShuttleOn", true);
            OzV11Kit.TrySet(typeof(ZWorld), "JeepChargeDeviation", charge);
            return s;
        }

        /// <summary>The vehicle that fights in Run ("jeep"; "light" for the light tank's rows).</summary>
        public static string Vehicle = "jeep";

        /// <summary>One fight's numbers.</summary>
        public sealed class Fight
        {
            public bool won, lost, charged, sniped; public int targets, dead, squashed, charges, chains, returns, home, orders, refusedWay, refusedDanger, wetTicks, driverHits, aimedRockets, toughStands, toughHeld, quickReloads;
            public double ttk = -1, path, chargePath, chargeTime, chargeBase, home10 = -1, endX, endY, rocketMiss, rocketHp; public int rockets; public string key = "";
            public double ChargeSpeed => chargeTime > 0 ? chargePath / chargeTime : 0;
            /// <summary>Distance driven while charging / the distance its own speed (ground and damage factors) would have driven in that time.</summary>
            public double ChargeRatio => chargeBase > 0 ? chargePath / chargeBase : 0;
            /// <summary>Everything a fight leaves in the world, for "the same fight before and after".</summary>
            public string Print => $"{(won ? "W" : lost ? "L" : "-")} ttk {ttk:0.00} path {path:0.0} dead {dead}/{targets} sq {squashed} end ({endX:0.0},{endY:0.0}) {key}";
        }

        /// <summary>A plain map; with <paramref name="waterCol"/> >= 0 a strip of water tiles (class 2) 2 tiles wide at that tile column.</summary>
        public static string Map(int waterCol = -1)
        {
            string m = OzV11Kit.Plain(W, H);
            if (waterCol < 0) return m;
            var cls = new string[W * H];
            for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) cls[y * W + x] = x == waterCol || x == waterCol + 1 ? "2" : "0";
            int a = m.IndexOf("\"classes\":[", StringComparison.Ordinal) + 11, b = m.IndexOf(']', a);
            return m.Substring(0, a) + string.Join(",", cls) + m.Substring(b);
        }

        /// <summary>Part I (the Tough on a road): the plain map with a road 3 tiles wide along the line jeep -> robot, from tile column
        /// fromTile on (-1 = the whole width); a jeep drives 1.689 x as fast on it (ZConst.ROAD_SPEED).</summary>
        public static string RoadMap(int fromTile = -1)
        {
            string m = OzV11Kit.Plain(W, H);
            var cls = new string[W * H];
            int cy = Cy / 16;
            for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) cls[y * W + x] = Math.Abs(y - cy) <= 1 && x >= fromTile ? "1" : "0";
            int a = m.IndexOf("\"classes\":[", StringComparison.Ordinal) + 11, b = m.IndexOf(']', a);
            return m.Substring(0, a) + string.Join(",", cls) + m.Substring(b);
        }

        static ZObject Keeper(ZWorld w, Team t, int x, int y)
        {
            var d = w.CreateRobotGroup("grunt", x - 8, y - 8, t, 1);
            d.MoveSpeed = 0; d.Damage = 0; d.MaxHealth = d.Health = 1000000000;
            return d;
        }

        /// <summary>A world: two computer teams that give no orders (red's bot only for a computer jeep), a keeper of each team far
        /// away (no game over), run past the calm start of a human team (DV69: nothing self-started in the first 20 s).</summary>
        public static ZWorld World(string settings, string map, int seed, bool cpuJeep)
        {
            var w = ZWorld.Create(settings, map, seed);
            w.Bots.Add(new ZBot(w, Team.Blue, seed + 101) { Ignored = true });
            if (cpuJeep) w.Bots.Add(new ZBot(w, Team.Red, seed) { Ignored = true });
            Keeper(w, Team.Red, 3 * 16, 3 * 16); Keeper(w, Team.Blue, (W - 3) * 16, (H - 3) * 16);
            w.Events.Clear();
            while (w.Time < ZWorld.CalmStartSec + 1.0) { w.Tick(Tick); w.Events.Clear(); }
            return w;
        }

        /// <summary>The robots: "hero" = the hero pair; else <paramref name="count"/> of the type, in a row across the line to the jeep.</summary>
        public static List<ZObject> Robots(ZWorld w, string type, int count, int x, int y)
        {
            var list = new List<ZObject>();
            if (type == "hero")
            {
                var l = w.CreateHeroSquad(ZHeroSquads.AlanBrad, Team.Blue, new[] { x / 16, y / 16, x / 16 + 1, y / 16 + 1 });
                if (l != null) { list.Add(l); list.AddRange(l.Minions); }
                return list;
            }
            var lead = w.CreateRobotGroup(type, x - 8, y - 8, Team.Blue, count);
            list.Add(lead); list.AddRange(lead.Minions);
            for (int i = 1; i < list.Count; i++) list[i].SetCords(x - 8 + 6 * i, y - 8 + (i % 2 == 1 ? 1 : -1) * 16 * ((i + 1) / 2));
            return list;
        }

        /// <summary>One fight. <paramref name="setup"/> (optional) adds things after the jeep and the robots; <paramref name="during"/>
        /// (optional) runs after every tick (orders in the middle of a fight).</summary>
        public static Fight Run(string settings, string type, int count, int dist, bool cpuJeep, int seed, bool charge, string map = null,
                                Action<ZWorld, ZObject, List<ZObject>> setup = null, Action<ZWorld, ZObject, List<ZObject>> during = null, double secs = 60)
        {
            using (Play(charge))
            {
                var w = World(settings, map ?? Map(), seed, cpuJeep);
                var jeep = w.CreateObject("vehicle", Vehicle, Cx - 16, Cy - 16, Team.Red);
                var rob = Robots(w, type, count, Cx + dist, Cy);
                setup?.Invoke(w, jeep, rob);
                var f = new Fight { targets = rob.Count };
                var meter = new OzV11Kit.Motion(jeep);
                double t0 = w.Time, killAt = -1;
                int sx = jeep.CenterX, sy = jeep.CenterY;
                bool startSet = false;
                double px = jeep.PosX, py = jeep.PosY;
                while (w.Time < t0 + secs)
                {
                    int hp0 = jeep.Health;
                    w.Tick(Tick);
                    foreach (var e in w.Events)
                        if (e.type == ZEventType.MissileExploded && e.otherId == jeep.Id && !e.flag && !e.flag2)
                        {   // a rocket at the jeep bursts: how far from its centre, the HP it took
                            var a = w.GetAny(e.objId);
                            if (a == null || a.Type != "tough") continue;
                            f.rockets++; f.rocketMiss += Math.Sqrt((double)(e.x - jeep.CenterX) * (e.x - jeep.CenterX) + (double)(e.y - jeep.CenterY) * (e.y - jeep.CenterY)); f.rocketHp += Math.Max(0, hp0 - jeep.Health);
                        }
                    w.Events.Clear();
                    double step = Math.Sqrt((jeep.PosX - px) * (jeep.PosX - px) + (jeep.PosY - py) * (jeep.PosY - py)); px = jeep.PosX; py = jeep.PosY;
                    if (killAt < 0) meter.Step(jeep, w.Time);
                    if (jeep.ChargeDriving)
                    {
                        if (!startSet) { sx = jeep.ChargeStartX; sy = jeep.ChargeStartY; startSet = true; }
                        f.charged = true;
                        var cls = w.Map.ClassAt(jeep.CenterX >> 4, jeep.CenterY >> 4);
                        if (cls == ZClass.Water || cls == ZClass.DeepWater) f.wetTicks++;
                        if (step > 1e-9) { f.chargePath += step; f.chargeTime += Tick; f.chargeBase += Tick * jeep.MoveSpeed * w.Map.TileWalkSpeed(jeep.CenterX, jeep.CenterY) * (jeep.ShowDamaged ? w.S.damaged_unit_speed : jeep.ShowPartiallyDamaged ? w.S.partially_damaged_unit_speed : 1.0); }
                    }
                    during?.Invoke(w, jeep, rob);
                    bool jeepGone = !OzV11Kit.Alive(jeep) || jeep.Owner != Team.Red;
                    if (killAt < 0 && rob.All(r => !OzV11Kit.Alive(r))) killAt = w.Time;
                    if (jeepGone && killAt < 0) break;
                    if (killAt >= 0 && w.Time >= killAt + 10.0 - 1e-9) break;
                }
                f.lost = !OzV11Kit.Alive(jeep) || jeep.Owner != Team.Red;
                f.sniped = f.lost && !jeep.Destroyed && jeep.KillAt < 0 && jeep.Owner == Team.Neutral;
                f.driverHits = w.JeepChargeDriverHits; f.aimedRockets = w.JeepChargeAimedRockets;
                f.toughStands = w.JeepChargeToughStands; f.toughHeld = w.JeepChargeToughHeldTicks; f.quickReloads = w.JeepChargeQuickReloads;
                f.dead = rob.Count(r => !OzV11Kit.Alive(r));
                f.squashed = rob.Count(r => r.Squashed);
                f.won = !f.lost && f.dead == rob.Count;
                f.ttk = killAt >= 0 ? killAt - t0 : -1;
                f.path = meter.Path;
                f.endX = jeep.PosX; f.endY = jeep.PosY;
                if (killAt >= 0 && !f.lost) f.home10 = Math.Sqrt((double)(jeep.CenterX - sx) * (jeep.CenterX - sx) + (double)(jeep.CenterY - sy) * (jeep.CenterY - sy));
                f.charges = w.JeepCharges; f.chains = w.JeepChargeChains; f.returns = w.JeepChargeReturns; f.home = w.JeepChargeHome; f.orders = w.JeepChargeOrdersTaken;
                f.refusedWay = w.JeepChargeRefusedWay; f.refusedDanger = w.JeepChargeRefusedDanger;
                f.key = $"hp {jeep.Health} rob {string.Join(",", rob.Select(r => Math.Max(0, r.Health)))}";
                return f;
            }
        }

        /// <summary>A row of seeds: wins, mean time to kill (wins), losses, mean distance driven, charge speed, the worst distance from
        /// the charge spot 10 s after the kill.</summary>
        public sealed class Row
        {
            public string name; public List<Fight> f = new List<Fight>();
            public int Wins => f.Count(x => x.won);
            public int Losses => f.Count(x => x.lost);
            public int Charged => f.Count(x => x.charged);
            public int Squashed => f.Sum(x => x.squashed);
            public double Ttk => f.Where(x => x.won).Select(x => x.ttk).DefaultIfEmpty(-1).Average();
            public double Path => f.Select(x => x.path).DefaultIfEmpty(0).Average();
            public double Speed { get { var c = f.Where(x => x.chargeTime > 0.2).ToList(); return c.Count == 0 ? 0 : c.Sum(x => x.chargePath) / c.Sum(x => x.chargeTime); } }
            public double Ratio { get { var c = f.Where(x => x.chargeTime > 0.2).ToList(); return c.Count == 0 ? 0 : c.Sum(x => x.chargePath) / c.Sum(x => x.chargeBase); } }
            public double Home => f.Where(x => x.home10 >= 0).Select(x => x.home10).DefaultIfEmpty(-1).Max();
            public string Text => $"wins {Wins}/{f.Count}, time to kill {(Ttk < 0 ? "-" : Ttk.ToString("0.0") + " s")}, jeep lost {Losses}, driven {Path:0} px, " +
                                  $"charged in {Charged}, robots squashed {Squashed}, speed while charging {(Speed > 0 ? Speed.ToString("0.0") + " px/s (x" + Ratio.ToString("0.000") + " of its own)" : "-")}, " +
                                  $"10 s after the kill at most {(Home < 0 ? "-" : Home.ToString("0.0") + " px")} from its charge spot";
        }

        public static Row Seeds(string name, int seeds, Func<int, Fight> one)
        {
            var r = new Row { name = name };
            for (int s = 1; s <= seeds; s++) r.f.Add(one(s));
            return r;
        }

        public static readonly (string label, string type, int count)[] Strong =
            { ("1 Tough", "tough", 1), ("1 Laser", "laser", 1), ("1 Electrobot", "electrobot", 1), ("the hero pair", "hero", 2), ("a squad of 3 Toughs", "tough", 3), ("1 Sniper", "sniper", 1) };
        public static readonly (string label, string type, int count)[] Weak = { ("3 Grunts", "grunt", 3), ("3 Psychos", "psycho", 3), ("1 Pyro", "pyro", 1) };
        public const int FarPx = 100, ClosePx = 40;

        /// <summary>The whole report. seeds = fights per row (10 for the report, 3 in the parity row).</summary>
        public static string Check(string settings, int seeds = 10)
        {
            var sb = new StringBuilder();
            double baseSpeed = ZSettings.Parse(settings).Unit("jeep").move_speed;
            sb.AppendLine($"DV121 the jeep runs robots over: {seeds} seeds a row, a plain map, the jeep {FarPx} px (far) or {ClosePx} px (close) from the robots; " +
                          $"strong = Tough, Laser, Electrobot, Sniper, hero squads; a weak robot (Grunt, Psycho, Pyro) is run over within {ZWorld.ChargeClosePx} px; speed x{ZWorld.ChargeSpeedMul:0.00}");
            bool strongBetter = true, homeOk = true, speedOk = true, farSame = true, closeOk = true, offQuiet = true;
            int winsBefore = 0, winsAfter = 0;
            var speeds = new List<double>(); var ratios = new List<double>();
            foreach (bool cpu in new[] { false, true })
            {
                string who = cpu ? "CPU jeep" : "player jeep";
                sb.AppendLine($"A. {who} vs strong robots at {FarPx} px");
                foreach (var (label, type, count) in Strong)
                {
                    var b = Seeds(label, seeds, s => Run(settings, type, count, FarPx, cpu, s, false));
                    var a = Seeds(label, seeds, s => Run(settings, type, count, FarPx, cpu, s, true));
                    sb.AppendLine($"   {label,-20} before: {b.Text}");
                    sb.AppendLine($"   {"",-20} after:  {a.Text}");
                    strongBetter &= a.Wins >= b.Wins && a.Charged == seeds;
                    homeOk &= a.f.Where(x => x.won && x.charged).All(x => x.home10 >= 0 && x.home10 < 32);
                    offQuiet &= b.f.All(x => x.charges == 0 && x.returns == 0);
                    winsBefore += b.Wins; winsAfter += a.Wins;
                    if (a.Speed > 0) { speeds.Add(a.Speed); ratios.Add(a.Ratio); }
                }
                sb.AppendLine($"B. {who} vs Grunts / Psychos / a Pyro far ({FarPx} px): before and after");
                foreach (var (label, type, count) in Weak)
                {
                    var b = Seeds(label, seeds, s => Run(settings, type, count, FarPx, cpu, s, false));
                    var a = Seeds(label, seeds, s => Run(settings, type, count, FarPx, cpu, s, true));
                    int same = Enumerable.Range(0, seeds).Count(i => b.f[i].Print == a.f[i].Print);
                    sb.AppendLine($"   {label,-20} before: {b.Text}");
                    sb.AppendLine($"   {"",-20} after:  {a.Text}; the same fight as before in {same} of {seeds}");
                    farSame &= same == seeds && a.Charged == 0;
                }
                sb.AppendLine($"C. {who} vs Grunts / Psychos / a Pyro close ({ClosePx} px)");
                foreach (var (label, type, count) in Weak)
                {
                    var b = Seeds(label, seeds, s => Run(settings, type, count, ClosePx, cpu, s, false));
                    var a = Seeds(label, seeds, s => Run(settings, type, count, ClosePx, cpu, s, true));
                    sb.AppendLine($"   {label,-20} before: {b.Text}");
                    sb.AppendLine($"   {"",-20} after:  {a.Text}");
                    closeOk &= a.Charged == seeds && a.Squashed > 0 && a.Wins >= b.Wins;
                    homeOk &= a.f.Where(x => x.won && x.charged).All(x => x.home10 >= 0 && x.home10 < 32);
                    if (a.Speed > 0) { speeds.Add(a.Speed); ratios.Add(a.Ratio); }
                }
            }
            double sp = speeds.Count > 0 ? speeds.Average() : 0, ra = ratios.Count > 0 ? ratios.Average() : 0;
            speedOk = ratios.Count > 0 && ratios.All(v => Math.Abs(v - ZWorld.ChargeSpeedMul) < 0.03);
            sb.AppendLine($"D. speed: the jeep's own speed on plain ground {baseSpeed:0.0} px/s undamaged; while charging {sp:0.0} px/s on average (a hit jeep drives slower: x0.9 / x0.8), " +
                          $"x{ra:0.000} of its own speed at that moment (rows {string.Join(", ", ratios.Select(v => v.ToString("0.000")))})");
            sb.AppendLine("E. one Grunt at a distance (player jeep, after; before: the jeep stands and shoots in every one)");
            bool sweepOk = true;
            foreach (int d in new[] { 24, 32, 40, 48, 56, 64, 80, 96 })
            {
                var a = Seeds($"{d} px", seeds, s => Run(settings, "grunt", 1, d, false, s, true));
                var b = Seeds($"{d} px", seeds, s => Run(settings, "grunt", 1, d, false, s, false));
                bool want = d <= ZWorld.ChargeClosePx;
                sweepOk &= want ? a.Charged == seeds : a.Charged == 0 && Enumerable.Range(0, seeds).All(i => a.f[i].Print == b.f[i].Print);
                sb.AppendLine($"   {d,3} px ({d / 16.0:0.0} tiles): charged in {a.Charged}/{seeds}, squashed {a.Squashed}, time to kill {a.Ttk:0.0} s (before {b.Ttk:0.0} s), driven {a.Path:0} px");
            }
            sb.AppendLine("F. one Tough at a distance (player jeep): he stands his ground (lets it come within about 2 to 3 tiles, then fires; his next rocket 1 s later): his second rocket or the jeep's hull, about half and half from every distance");
            bool toughOk = true; int toughBefore = 0, toughAfter = 0;
            foreach (int d in new[] { 40, 60, 80, 100 })
            {
                var b = Seeds($"{d} px", seeds, s => Run(settings, "tough", 1, d, false, s, false));
                var a = Seeds($"{d} px", seeds, s => Run(settings, "tough", 1, d, false, s, true));
                toughOk &= a.Wins >= b.Wins; toughBefore += b.Wins; toughAfter += a.Wins;
                sb.AppendLine($"   {d,3} px ({d / 16.0:0.0} tiles): before wins {b.Wins}/{seeds}, jeep lost {b.Losses}; after wins {a.Wins}/{seeds}, jeep lost {a.Losses}, squashed {a.Squashed}, time to kill {(a.Ttk < 0 ? "-" : a.Ttk.ToString("0.0") + " s")}");
            }
            sb.AppendLine("G. where it must not charge, and orders");
            bool safeOk = true;
            {   // water: a strip of water 2 tiles wide between the jeep and a Tough
                int col = (Cx + 48) / 16;
                var a = Seeds("water", seeds, s => Run(settings, "tough", 1, FarPx, false, s, true, Map(col)));
                var b = Seeds("water", seeds, s => Run(settings, "tough", 1, FarPx, false, s, false, Map(col)));
                bool ok = a.Charged == 0 && a.f.All(x => x.refusedWay > 0 && x.wetTicks == 0) && Enumerable.Range(0, seeds).All(i => a.f[i].Print == b.f[i].Print);
                safeOk &= ok;
                sb.AppendLine($"   water between it and a Tough: charged in {a.Charged}, refused (the way) in {a.f.Count(x => x.refusedWay > 0)}, ticks of a drive on water {a.f.Sum(x => x.wetTicks)}, " +
                              $"the same fight as before in {Enumerable.Range(0, seeds).Count(i => a.f[i].Print == b.f[i].Print)} of {seeds}");
            }
            {   // an enemy crewed gatling 100 px behind the Tough (its reach 120 px: the jeep, 200 px from it, is outside)
                Action<ZWorld, ZObject, List<ZObject>> gun = (w, j, r) => w.CreateObject("cannon", "gatling", Cx + FarPx + 100 - 16, Cy - 16, Team.Blue);
                var a = Seeds("gun", seeds, s => Run(settings, "tough", 1, FarPx, false, s, true, null, gun));
                bool ok = a.Charged == 0 && a.f.All(x => x.refusedDanger > 0);
                safeOk &= ok;
                sb.AppendLine($"   an enemy gun 100 px behind the Tough: charged in {a.Charged}, refused (enemy gun) in {a.f.Count(x => x.refusedDanger > 0)}");
            }
            {   // a row of its own grunts across the lane, 50 px in front of it (they stand, do no harm and cannot die)
                Action<ZWorld, ZObject, List<ZObject>> own = (w, j, r) =>
                {
                    for (int k = -2; k <= 2; k++) { var g = w.CreateRobotGroup("grunt", Cx + 50 - 8, Cy + 16 * k - 8, Team.Red, 1); g.Damage = 0; g.MoveSpeed = 0; g.MaxHealth = g.Health = 1000000000; }
                };
                var a = Seeds("own", seeds, s => Run(settings, "tough", 1, FarPx, false, s, true, null, own));
                bool ok = a.Charged == 0 && a.f.All(x => x.refusedWay > 0);
                safeOk &= ok;
                sb.AppendLine($"   a row of its own grunts across the lane: charged in {a.Charged}, refused (the way) in {a.f.Count(x => x.refusedWay > 0)}");
            }
            {   // the player's attack-move that passes 80 px from a Tough: his order first (no charge, the fight as before)
                int gx = Cx + 260, gy = Cy;
                Action<ZWorld, ZObject, List<ZObject>> mv = (w, j, r) => OzV11Kit.PlayerMove(w, j, gx, gy, true);
                Action<ZWorld, ZObject, List<ZObject>> put = (w, j, r) => { r[0].SetCords(Cx + 130 - 8, Cy + 80 - 8); r[0].Damage = 0; mv(w, j, r); };
                var a = Seeds("pmove", seeds, s => Run(settings, "tough", 1, 0, false, s, true, null, put, null, 40));
                var b = Seeds("pmove", seeds, s => Run(settings, "tough", 1, 0, false, s, false, null, put, null, 40));
                bool ok = a.Charged == 0 && Enumerable.Range(0, seeds).All(i => a.f[i].Print == b.f[i].Print);
                safeOk &= ok;
                sb.AppendLine($"   the player's attack-move past a Tough (80 px from its way): charged in {a.Charged}, the same as before in {Enumerable.Range(0, seeds).Count(i => a.f[i].Print == b.f[i].Print)} of {seeds}");
            }
            {   // a move order in the middle of a charge: the order wins at once, no drive back
                int mx = Cx - 120, my = Cy + 40;
                bool sent = false;
                Action<ZWorld, ZObject, List<ZObject>> during = (w, j, r) => { if (!sent && j.ChargeDriving && w.JeepCharges > 0) { sent = true; OzV11Kit.PlayerMove(w, j, mx, my, false); } };
                var a = Seeds("order", seeds, s => { sent = false; return Run(settings, "tough", 1, FarPx, false, s, true, null, (w, j, r) => r[0].Damage = 0, during, 20); });
                bool ok = a.f.All(x => x.orders == 1 && x.returns == 0 && Math.Abs(x.endX + 16 - mx) <= 12 && Math.Abs(x.endY + 16 - my) <= 12);
                safeOk &= ok;
                sb.AppendLine($"   a move order in the middle of the charge: taken at once in {a.f.Count(x => x.orders == 1)}, drove back after it in {a.f.Count(x => x.returns > 0)}, at the ordered spot in {a.f.Count(x => Math.Abs(x.endX + 16 - mx) <= 12 && Math.Abs(x.endY + 16 - my) <= 12)} of {seeds}");
            }
            {   // the computer's attack-move past a Tough: it runs him over and goes on with its order
                int gx = Cx + 300, gy = Cy;
                var a = Seeds("cmove", seeds, s => Run(settings, "tough", 1, 0, true, s, true, null, (w, j, r) => { r[0].SetCords(Cx + 130 - 8, Cy + 60 - 8); r[0].Damage = 0; w.SendWaypoints(Team.Red, j, new[] { ZWaypoint.Move(gx, gy, true) }); }, null, 40));
                bool ok = a.Charged == seeds && a.f.All(x => x.returns == 0) && a.f.Count(x => x.dead == 1 && Math.Abs(x.endX + 16 - gx) <= 24 && Math.Abs(x.endY + 16 - gy) <= 24) >= seeds - 1;
                safeOk &= ok;
                sb.AppendLine($"   the computer's attack-move past a Tough: charged in {a.Charged}, the Tough dead in {a.f.Count(x => x.dead == 1)}, drove back in {a.f.Count(x => x.returns > 0)}, " +
                              $"at the order's goal at the end in {a.f.Count(x => Math.Abs(x.endX + 16 - gx) <= 24 && Math.Abs(x.endY + 16 - gy) <= 24)} of {seeds}");
            }
            {   // the player's attack order on a Laser 240 px away: into range, the charge, the drive back to the charge spot
                var a = Seeds("pattack", seeds, s => Run(settings, "laser", 1, 240, false, s, true, null, (w, j, r) => OzV11Kit.PlayerAttack(w, j, r[0])));
                bool ok = a.Charged == seeds && a.Wins > 0 && a.f.Where(x => x.won).All(x => x.home10 >= 0 && x.home10 < 32);
                safeOk &= ok;
                sb.AppendLine($"   the player's attack order on a Laser 240 px away: {a.Text}");
            }
            sb.AppendLine("H. a robot it runs over goes first over the one it shoots; a call for help (player jeep)");
            bool firstOk = true;
            {   // a Grunt walking past 28 px from the jeep while it shoots a squad of 3 Grunts 100 px away: the one that is close is run over
                ZObject walker = null;
                Action<ZWorld, ZObject, List<ZObject>> walk = (w, j, r) => { walker = w.CreateRobotGroup("grunt", Cx + 28 - 8, Cy + 150 - 8, Team.Blue, 1); w.SendWaypoints(Team.Blue, walker, new[] { ZWaypoint.Move(Cx + 28, Cy - 200, false) }); };
                int sqB = 0, sqA = 0, homeA = 0;
                var b = Seeds("walk", seeds, s => { var f = Run(settings, "grunt", 3, FarPx, false, s, false, null, walk, null, 30); if (walker.Squashed) sqB++; return f; });
                var a = Seeds("walk", seeds, s => { var f = Run(settings, "grunt", 3, FarPx, false, s, true, null, walk, null, 30); if (walker.Squashed) sqA++; if (f.home > 0) homeA++; return f; });
                firstOk &= sqB == 0 && sqA == seeds && homeA == seeds;
                sb.AppendLine($"   a Grunt walking past 28 px while it shoots a squad 100 px away: the passer-by run over before {sqB}/{seeds}, after {sqA}/{seeds} (back on its spot after it {homeA}/{seeds}); " +
                              $"jeep lost before {b.Losses}, after {a.Losses} (it stops shooting the squad while it runs him over)");
            }
            {   // a Grunt (the first one it sees) 60 px away and a Tough 60 px away beside him: the Tough is run over first
                ZObject tough = null;
                Action<ZWorld, ZObject, List<ZObject>> two = (w, j, r) => tough = w.CreateObject("robot", "tough", Cx + 50 - 8, Cy + 32 - 8, Team.Blue);
                int tB = 0, tA = 0, chA = 0;
                var b = Seeds("mixed", seeds, s => { var f = Run(settings, "grunt", 1, 60, false, s, false, null, two); if (!OzV11Kit.Alive(tough)) tB++; return f; });
                var a = Seeds("mixed", seeds, s => { var f = Run(settings, "grunt", 1, 60, false, s, true, null, two); if (!OzV11Kit.Alive(tough)) tA++; if (f.charged) chA++; return f; });
                firstOk &= chA == seeds && tA >= tB && a.Losses <= b.Losses && tA > 0;
                sb.AppendLine($"   a Grunt and a Tough together 60 px away (it saw the Grunt first): charged in {chA}/{seeds}, the Tough killed before {tB}/{seeds}, after {tA}/{seeds}; jeep lost before {b.Losses}, after {a.Losses}");
            }
            {   // an unkillable red Grunt 120 px away is shot by a Tough 210 px from the jeep (out of its own chase's reach): the call for help
                Action<ZWorld, ZObject, List<ZObject>> help = (w, j, r) => { var g = w.CreateRobotGroup("grunt", Cx + 110 - 8, Cy + 50 - 8, Team.Red, 1); g.Damage = 0; g.MoveSpeed = 0; g.MaxHealth = g.Health = 1000000000; };
                var b = Seeds("help", seeds, s => Run(settings, "tough", 1, 210, false, s, false, null, help));
                var a = Seeds("help", seeds, s => Run(settings, "tough", 1, 210, false, s, true, null, help));
                firstOk &= a.Charged == seeds && a.Squashed == seeds && b.Charged == 0;
                sb.AppendLine($"   a call for help against a Tough 210 px away: before: {b.Text}");
                sb.AppendLine($"   {"",-46} after:  {a.Text}");
            }
            var fair = Fair(settings, sb);
            sb.AppendLine(OzV11Kit.Verdict(strongBetter && winsAfter > winsBefore, $"strong robots: the jeep charges in every fight and wins at least as often in every row ({winsBefore} -> {winsAfter} wins in all)"));
            sb.AppendLine(OzV11Kit.Verdict(toughOk && toughAfter > toughBefore, $"one Tough from 40 to 100 px: won at least as often at every distance ({toughBefore} -> {toughAfter} wins)"));
            sb.AppendLine(OzV11Kit.Verdict(farSame, "Grunts / Psychos / a Pyro far: not a single charge, every fight exactly as before"));
            sb.AppendLine(OzV11Kit.Verdict(closeOk, $"Grunts / Psychos / a Pyro close: run over in every fight (squashes), won at least as often"));
            sb.AppendLine(OzV11Kit.Verdict(sweepOk, $"one Grunt: run over from {ZWorld.ChargeClosePx} px in, shot as before from farther"));
            sb.AppendLine(OzV11Kit.Verdict(speedOk, $"speed while charging x{ra:0.000} of its own (rule x{ZWorld.ChargeSpeedMul:0.00}, every row within 0.03)"));
            sb.AppendLine(OzV11Kit.Verdict(homeOk, "10 s after the kill every jeep that charged is back within 2 tiles of its charge spot"));
            sb.AppendLine(OzV11Kit.Verdict(safeOk, "no charge through water, at a robot under an enemy gun, through its own unit or on the way of the player's order; a new order wins; the computer goes on with its order"));
            sb.AppendLine(OzV11Kit.Verdict(firstOk, "a close Grunt walking past and a Tough beside a Grunt are run over first; a call for help against a Tough ends in a run-over"));
            sb.AppendLine(OzV11Kit.Verdict(offQuiet, "rule off: no charge, no drive back"));
            foreach (var v in fair) sb.AppendLine(v);
            return sb.ToString();
        }

        /// <summary>Fights a row in part I (fair chances), whatever the seeds of the rest (the 35-55 % rows need them).</summary>
        public const int FairSeeds = 20;

        static int HpLeft(Fight f) { var m = System.Text.RegularExpressions.Regex.Match(f.key, @"^hp (-?\d+)"); return m.Success ? int.Parse(m.Groups[1].Value) : -1; }

        /// <summary>Part I, fair chances (the robot it drives at fights back): the rows of the re-tune, the levers acting
        /// only in a charge, the light tank. Returns its verdict lines (printed with the others at the end).</summary>
        static List<string> Fair(string settings, StringBuilder sb)
        {
            int n = FairSeeds, lo = (int)Math.Ceiling(n * 0.35 - 1e-9), hi = (int)Math.Floor(n * 0.55 + 1e-9);
            var st = ZSettings.Parse(settings);
            int rocketHp = st.Unit("tough").Damage, jeepHp = st.Unit("jeep").MaxHealth;
            Row R(string type, int count, int dist, bool charge, Action<ZWorld, ZObject, List<ZObject>> setup = null, double secs = 60) =>
                Seeds(type, n, s => Run(settings, type, count, dist, false, s, charge, null, setup, null, secs));
            string HpRange(Row a) => a.Wins > 0 ? a.f.Where(x => x.won).Min(HpLeft) + "-" + a.f.Where(x => x.won).Max(HpLeft) : "-";
            Action<ZWorld, ZObject, List<ZObject>> mixed = (w, j, r) => r.Add(w.CreateObject("robot", "tough", Cx + 50 - 8, Cy + 32 - 8, Team.Blue));
            sb.AppendLine($"I. fair chances (the robot it drives at fights back), {n} seeds a row, player jeep: a Laser / a hero snipes the driver of a jeep that drives at him from {ZWorld.ChargeSnipePx} px " +
                          $"with {ZWorld.ChargeSnipeLaser:0.00} / {ZWorld.ChargeSnipeHero:0.00} a hit; a Tough stands his ground: he lets it come within {ZWorld.ChargeToughHoldPx}-{ZWorld.ChargeToughHoldPx + ZWorld.ChargeToughHoldSpreadPx} px (drawn once), " +
                          $"fires at once, aimed from where it will be ({ZWorld.ChargeRocketScatter:0.00} of Zod's scatter kept), his next rocket {ZWorld.ChargeToughReload:0.00} s later");
            var verdicts = new List<string>();
            bool fairOk = true;
            foreach (var (label, type, count) in new[] { ("1 Laser", "laser", 1), ("the hero pair", "hero", 2) })
            {
                var b = R(type, count, FarPx, false); var a = R(type, count, FarPx, true);
                bool ok = a.Wins >= lo && a.Wins <= hi; fairOk &= ok;
                sb.AppendLine($"   {label,-20} at {FarPx} px: project wins {b.Wins}/{n}; after: {a.Text}; the driver sniped in {a.f.Count(x => x.sniped)} ({(ok ? "inside" : "OUTSIDE")} {lo}-{hi} of {n})");
            }
            var sn = R("sniper", 1, FarPx, true);
            bool sniperOk = sn.Wins >= (int)Math.Ceiling(n * 0.30 - 1e-9) && sn.Wins <= (int)Math.Floor(n * 0.50 + 1e-9);
            sb.AppendLine($"   1 Sniper             at {FarPx} px: after wins {sn.Wins}/{n}, the driver sniped in {sn.f.Count(x => x.sniped)} (no lever for the Sniper; 30-50 %: {(sniperOk ? "yes" : "NO")})");
            // DV12: a veteran jeep crew stays buttoned up in a charge too (the driver shot x VeteranSnipeFactor, as every other snipe at it)
            Action<ZWorld, ZObject, List<ZObject>> vet = (w, j, r) => { j.Veteran = true; j.Kills = ZWorld.VeteranKills; };
            var vFresh = R("laser", 1, FarPx, true); var vVet = R("laser", 1, FarPx, true, vet);
            int snFresh = vFresh.f.Count(x => x.sniped), snVet = vVet.f.Count(x => x.sniped);
            bool vetOk = snVet * 2 <= snFresh && vVet.Wins > vFresh.Wins;
            sb.AppendLine($"   a VETERAN jeep crew (DV12, buttoned up x{ZWorld.VeteranSnipeFactor:0.00}) at 1 Laser, {FarPx} px: wins {vVet.Wins}/{n}, the driver sniped in {snVet} (a fresh crew: wins {vFresh.Wins}/{n}, sniped in {snFresh})");
            // the Tough: the miss of Zod's aim and of the rule's at a jeep driving at him, how many rockets he gets off, what a rocket takes
            bool lead0 = ZWorld.ChargeRocketLead; double sc0 = ZWorld.ChargeRocketScatter;
            Row zod;
            try { ZWorld.ChargeRocketLead = false; ZWorld.ChargeRocketScatter = 1.0; zod = R("tough", 1, FarPx, true); }
            finally { ZWorld.ChargeRocketLead = lead0; ZWorld.ChargeRocketScatter = sc0; }
            var rule = R("tough", 1, FarPx, true);
            int zodRockets = zod.f.Sum(x => x.rockets), ruleRockets = rule.f.Sum(x => x.rockets);
            double zodMiss = zodRockets > 0 ? zod.f.Sum(x => x.rocketMiss) / zodRockets : -1, ruleMiss = ruleRockets > 0 ? rule.f.Sum(x => x.rocketMiss) / ruleRockets : -1;
            sb.AppendLine($"   a Tough's rocket at a jeep driving at him ({FarPx} px): Zod's aim bursts {zodMiss:0.0} px from its centre on average ({zodRockets} rockets), the rule's {ruleMiss:0.0} px ({ruleRockets} rockets)");
            bool rocketOk = ruleMiss >= 0 && zodMiss > 0 && ruleMiss <= zodMiss * 0.6;
            // he stands his ground (lets it come within the drawn distance, fires at once, the next rocket after ChargeToughReload s): the
            // jeep's chances at every start distance (one rocket cannot destroy the jeep, his second one does)
            int lo25 = (int)Math.Ceiling(n * 0.25 - 1e-9), hi65 = (int)Math.Floor(n * 0.65 + 1e-9), lo30 = (int)Math.Ceiling(n * 0.30 - 1e-9), hi55 = hi;
            int toughWins = 0, toughRows = 0, stoodAll = 0; bool bandOk = true;
            foreach (int d in new[] { 40, 50, 60, 64, 80, 100 })
            {
                var b = R("tough", 1, d, false); var a = R("tough", 1, d, true);
                bool ok = a.Wins >= lo25 && a.Wins <= hi65; bandOk &= ok; toughWins += a.Wins; toughRows++; stoodAll += a.f.Count(x => x.toughStands > 0);
                int rk = a.f.Sum(x => x.rockets), two = a.f.Count(x => x.rockets >= 2);
                sb.AppendLine($"   1 Tough at {d,3} px: project wins {b.Wins}/{n}; after wins {a.Wins}/{n} ({(ok ? "inside" : "OUTSIDE")} {lo25}-{hi65}), jeep lost {a.Losses}; his rockets at it {(double)rk / n:0.0} a fight " +
                              $"(two or more in {two}), {(rk > 0 ? (a.f.Sum(x => x.rocketHp) / rk).ToString("0") : "-")} HP a rocket; the jeep keeps {HpRange(a)} of {jeepHp} HP when it wins");
            }
            double toughAvg = 100.0 * toughWins / Math.Max(1, toughRows * n);
            bandOk &= toughAvg >= 40 - 1e-9 && toughAvg <= 50 + 1e-9 && stoodAll == toughRows * n;
            {
                var b = R("grunt", 1, 60, false, mixed); var a = R("grunt", 1, 60, true, mixed);
                bool ok = a.Wins >= lo30 && a.Wins <= hi55; bandOk &= ok;
                sb.AppendLine($"   a Grunt with a Tough beside it (60 px): project wins {b.Wins}/{n}; after wins {a.Wins}/{n} ({(ok ? "inside" : "OUTSIDE")} {lo30}-{hi55}), jeep lost {a.Losses}; the jeep keeps {HpRange(a)} HP when it wins");
            }
            foreach (var (label, count, dist) in new[] { ("2 Toughs (a squad)", 2, 40), ("3 Toughs", 3, FarPx) })
            {
                var a = R("tough", count, dist, true);
                sb.AppendLine($"   {label} at {dist} px: after wins {a.Wins}/{n}, jeep lost {a.Losses} (only the one it drives at stands his ground, the others fire as always: a squad beats a jeep)");
            }
            {
                var many = Seeds("tough", 200, s => Run(settings, "tough", 1, FarPx, false, s, true));
                sb.AppendLine($"   1 Tough at {FarPx} px, 200 fights: the jeep wins {many.Wins} of 200 ({many.Wins / 2.0:0.0} %); every 20-seed row above draws the same 20 distances, so it shows one count at every start distance");
            }
            // on a road the jeep drives 1.689 x as fast: he counts its way to him as open ground (ZObject.ChargeToughFar), so he fires farther
            // out and the race stays the same; by the plain distance alone the jeep would win every such fight
            bool roadOk = true;
            foreach (var (label, dist, fromTile) in new[] { ("a road all the way", 64, -1), ("a road all the way", FarPx, -1), ("a road from 3 tiles before him", FarPx, (Cx + FarPx) / 16 - 3) })
            {
                string roadMap = RoadMap(fromTile);
                Row OnRoad(bool ground)
                {
                    bool g0 = ZWorld.ChargeToughGround;
                    try { ZWorld.ChargeToughGround = ground; return Seeds("tough", n, s => Run(settings, "tough", 1, dist, false, s, true, roadMap)); }
                    finally { ZWorld.ChargeToughGround = g0; }
                }
                var ra = OnRoad(true); var rp = OnRoad(false);
                bool ok = ra.Wins >= lo25 && ra.Wins <= hi65 && ra.f.All(x => x.toughStands > 0); roadOk &= ok;
                sb.AppendLine($"   1 Tough at {dist,3} px, {label}: after wins {ra.Wins}/{n} ({(ok ? "inside" : "OUTSIDE")} {lo25}-{hi65}), jeep lost {ra.Losses}; by the plain distance (the road not counted) the jeep would win {rp.Wins}/{n}");
            }
            {
                string roadMap = RoadMap(-1);
                var many = Seeds("tough", 200, s => Run(settings, "tough", 1, FarPx, false, s, true, roadMap));
                roadOk &= many.Wins >= 80 && many.Wins <= 120;
                sb.AppendLine($"   1 Tough at {FarPx} px, a road all the way, 200 fights: the jeep wins {many.Wins} of 200 ({many.Wins / 2.0:0.0} %; band 80-120)");
            }
            sb.AppendLine($"   the lone Tough, {toughRows} distances: the jeep wins {toughWins} of {toughRows * n} ({toughAvg:0.0} %; band 40-50 %); one rocket ({rocketHp} HP) cannot destroy the jeep ({jeepHp} HP), his second one does; he stood his ground in {stoodAll} of {toughRows * n} " +
                          $"(he lets it come within {ZWorld.ChargeToughHoldPx}-{ZWorld.ChargeToughHoldPx + ZWorld.ChargeToughHoldSpreadPx} px, no aim-up, his next rocket {ZWorld.ChargeToughReload:0.00} s later)");
            // the levers act only while a vehicle drives at that robot: the player's attack-move past a Laser / a Tough (no charge), the light tank
            bool onlyOk = true;
            foreach (var (label, type) in new[] { ("a Laser", "laser"), ("a Tough", "tough") })
            {
                int gx = Cx + 260, gy = Cy;
                Action<ZWorld, ZObject, List<ZObject>> put = (w, j, r) => { r[0].SetCords(Cx + 130 - 8, Cy + 80 - 8); OzV11Kit.PlayerMove(w, j, gx, gy, true); };
                var b = R(type, 1, 0, false, put, 40); var a = R(type, 1, 0, true, put, 40);
                int same = Enumerable.Range(0, n).Count(i => a.f[i].Print == b.f[i].Print);
                bool ok = a.Charged == 0 && same == n && a.f.All(x => x.driverHits == 0 && x.aimedRockets == 0 && x.toughStands == 0 && x.toughHeld == 0 && x.quickReloads == 0); onlyOk &= ok;
                sb.AppendLine($"   the player's attack-move past {label} (it fires from its way, no charge): the same fight as with the rule off in {same} of {n}; driver shots of the rule {a.f.Sum(x => x.driverHits)}, rockets aimed by the rule {a.f.Sum(x => x.aimedRockets)}, " +
                              $"Toughs standing their ground {a.f.Sum(x => x.toughStands)}, quick second rockets {a.f.Sum(x => x.quickReloads)}");
            }
            string veh0 = Vehicle;
            bool lightOk = true;
            try
            {
                Vehicle = "light";
                foreach (var (label, type, count, dist) in new[] { ("1 Laser", "laser", 1, FarPx), ("the hero pair", "hero", 2, FarPx), ("1 Tough", "tough", 1, 60), ("1 Tough", "tough", 1, FarPx),
                                                                   ("1 Sniper", "sniper", 1, FarPx), ("1 Electrobot", "electrobot", 1, FarPx), ("3 Toughs", "tough", 3, FarPx) })
                {
                    var b = R(type, count, dist, false); var a = R(type, count, dist, true);
                    int same = Enumerable.Range(0, n).Count(i => a.f[i].Print == b.f[i].Print);
                    Row c; bool lt0 = ZWorld.ChargeLightTank;
                    try { ZWorld.ChargeLightTank = true; c = R(type, count, dist, true); } finally { ZWorld.ChargeLightTank = lt0; }
                    lightOk &= a.Charged == 0 && same == n && a.f.All(x => x.driverHits == 0 && x.aimedRockets == 0 && x.toughStands == 0 && x.quickReloads == 0) && (count != 1 || a.Wins >= n - 1);
                    sb.AppendLine($"   the LIGHT TANK vs {label} at {dist} px: in the game (no charge) wins {a.Wins}/{n}, lost {a.Losses}, the same fight as with the rule off in {same} of {n}; " +
                                  $"with a charge (switched on for this count only) wins {c.Wins}/{n}, lost {c.Losses}, squashed {c.Squashed}");
                }
            }
            finally { Vehicle = veh0; }
            verdicts.Add(OzV11Kit.Verdict(fairOk, $"fair chances: against a Laser and against the hero pair at {FarPx} px the charging jeep wins about half the time ({lo}-{hi} of {n} each)"));
            verdicts.Add(OzV11Kit.Verdict(sniperOk, "the Sniper row stays at 30-50 % jeep wins (no lever)"));
            verdicts.Add(OzV11Kit.Verdict(vetOk, "a veteran jeep crew stays buttoned up in a charge (DV12): its driver is sniped at most half as often as a fresh crew's"));
            verdicts.Add(OzV11Kit.Verdict(rocketOk, $"a Tough's rocket at a jeep driving at him is aimed true (it bursts {zodMiss:0.0} -> {ruleMiss:0.0} px from the jeep's centre)"));
            verdicts.Add(OzV11Kit.Verdict(bandOk, $"a lone Tough a jeep drives at stands his ground: the jeep wins {lo25}-{hi65} of {n} at every start distance from 40 to 100 px ({toughAvg:0.0} % in all, band 40-50 %), beside a Grunt {lo30}-{hi55} of {n}"));
            verdicts.Add(OzV11Kit.Verdict(roadOk, $"on a road (the jeep 1.689 x as fast) he counts its way as open ground and fires farther out: the jeep wins {lo25}-{hi65} of {n} there too (by the plain distance it would win every fight)"));
            verdicts.Add(OzV11Kit.Verdict(onlyOk, "the levers act only on a jeep that drives at that robot: an attack-move past a Laser / a Tough is the same fight as with the rule off"));
            verdicts.Add(OzV11Kit.Verdict(lightOk, "the light tank does not charge and its fights are as before; standing it already wins against every lone strong robot (19 of 20 or more)"));
            return verdicts;
        }

#if UNITY_EDITOR
        [UnityEditor.MenuItem("OpenZ/V1.1/Jeep: DV121 Charge Check")]
        static void Menu() => OzV11Kit.RunMenu("DV121 the jeep runs robots over", () => Check(OzV11Kit.SettingsText, 10));
#endif
    }
}
