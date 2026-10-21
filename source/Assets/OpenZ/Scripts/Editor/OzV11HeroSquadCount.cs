using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// DV108 hero squads (Sim/ZHeroSquad.cs): the counted checks, headless (ZWorld.Create + Tick, no scene, nothing saved).
    /// Menu OpenZ/V1.1/Hero Squad: Count Fights (about a minute) and OpenZ/V1.1/Hero Squad: Rule Check (seconds).
    ///  * Fights: on open ground the ALAN &amp; BRAD squad (red) and, for comparison, one, two and three red grunt squads, each
    ///    against one, two and three blue grunt squads, a blue light tank and a blue gun; both sides ordered to attack (the
    ///    computer's attack order), <see cref="Runs"/> fights a row with their own seeds and a small start jitter; a row ends when one
    ///    side has nobody left (or after <see cref="MaxSeconds"/>: a draw). Printed: red wins / blue wins / draws, mean length,
    ///    the hit points red has left. The hero squad's fire rate is chosen from these rows: worth about two grunt squads (its wins
    ///    against each opponent near two squads' wins) and never more than three (it loses to three squads more often than three
    ///    squads lose to three).
    ///  * Rules: health, the strong shot on a hull and on a grunt, the shot pause, Brad leads, vehicles (a jeep, a gun and the own
    ///    troop carrier by order, never by themselves (KH: a grunt squad does take the jeep); out again as Alan and Brad with their own hit points),
    ///    the level data's extra squad with the switch on and off (the map's own units unchanged, the psycho squad there, the start
    ///    and campfire tiles clear of the shuttle, the searchlight and the forts' lanes; KC4b: the start camp as the game lays it out,
    ///    CampCheck), and the match goes on after the squad is lost; KC4b round 4: together and apart (one dies, one in a gun, the
    ///    squad split, both out of one troop carrier: name, picture and who speaks, View/OzHeroPair.cs) and the drink rule's timing
    ///    (View/OzHeroAnticPlan.cs). The feature table against a grunt squad: Editor/OzV11HeroFeatures.cs.
    /// </summary>
    public static class OzV11HeroSquadCount
    {
        public static int Runs = 100;
        public static double MaxSeconds = 150;
        static readonly string[] Opponents = { "1 grunt squad", "2 grunt squads", "3 grunt squads", "light tank", "gun" };
        static readonly string[] Sides = { "ALAN & BRAD", "1 grunt squad", "2 grunt squads", "3 grunt squads" };

        // ------------------------------------------------------------------ fights
        public struct Row { public int red, blue, draw; public double secs, hpLeft, blueTaken; public int n; }

        static List<ZObject> Place(ZWorld w, string what, Team team, int tx, int ty, Random jit)
        {
            var list = new List<ZObject>();
            int Jx() => jit.Next(-1, 2);
            int Jy() => jit.Next(-1, 2);
            switch (what)
            {
                case "ALAN & BRAD":
                    list.Add(w.CreateHeroSquad(ZHeroSquads.AlanBrad, team, new[] { tx + Jx(), ty + Jy(), tx + Jx(), ty + 2 + Jy() }));
                    break;
                case "light tank": list.Add(w.CreateObject("vehicle", "light", (tx + Jx()) * 16, (ty + Jy()) * 16, team)); break;
                case "gun": list.Add(w.CreateObject("cannon", "gun", (tx + Jx()) * 16, (ty + Jy()) * 16, team)); break;
                default:
                    int n = what[0] - '0';
                    for (int i = 0; i < n; i++) list.Add(w.CreateRobotGroup("grunt", (tx + Jx()) * 16, (ty + (int)Math.Round((i - (n - 1) / 2.0) * 4) + Jy()) * 16, team));   // squads 4 tiles apart, side by side
                    break;
            }
            return list;
        }

        static IEnumerable<ZObject> Bodies(List<ZObject> leaders) { foreach (var l in leaders) foreach (var b in OzV11Kit.Squad(l)) yield return b; }

        /// <summary>One fight: red <paramref name="side"/> against blue <paramref name="opp"/>. Returns 1 red won, 2 blue won, 0 draw.</summary>
        public static int Fight(string settings, string side, string opp, int seed, out double secs, out double redHpLeft, out double blueTaken)
        {
            var w = OzV11Kit.World(settings, OzV11Kit.Plain(64, 48), seed);
            OzV11Kit.Dummy(w, 60 * 16 + 8, 44 * 16 + 8);
            var jit = new Random(seed * 31 + 7);
            var red = Place(w, side, Team.Red, 18, 24, jit);
            var blue = Place(w, opp, Team.Blue, 29, 24, jit);
            var redB = Bodies(red).ToList(); var blueB = Bodies(blue).ToList();
            double hp0 = redB.Sum(o => (double)o.MaxHealth), bhp0 = blueB.Sum(o => (double)o.MaxHealth);
            for (int i = 0; i < 10; i++) { w.Tick(ZConst.TICK); w.Events.Clear(); }
            foreach (var r in red) { var t = blue.OrderBy(b => OzV11Kit.Dist(r, b)).First(); OzV11Kit.CpuAttack(w, r, t); }
            foreach (var b in blue) { if (!b.CanMove) continue; var t = red.OrderBy(r => OzV11Kit.Dist(r, b)).First(); OzV11Kit.CpuAttack(w, b, t); }
            double t0 = w.Time; int result = 0;
            while (w.Time - t0 < MaxSeconds)
            {
                w.Tick(ZConst.TICK); w.Events.Clear();
                bool redGone = !redB.Any(OzV11Kit.Alive), blueGone = !blueB.Any(OzV11Kit.Alive);
                if (redGone || blueGone) { result = redGone && blueGone ? 0 : redGone ? 2 : 1; break; }
                if (((int)Math.Round((w.Time - t0) * 100)) % 200 == 0)
                {   // every 2 s: a robot squad that stands idle with enemies left is sent at the nearest one again (the player's / CPU's next click)
                    foreach (var (mine, theirs) in new[] { (red, blueB), (blue, redB) })
                        foreach (var l in mine)
                        {
                            var u = OzV11Kit.Squad(l).FirstOrDefault(OzV11Kit.Alive);
                            if (u == null || !u.CanMove || u.IsMinion || u.Waypoints.Count > 0 || u.AttackObject != null) continue;
                            var t = theirs.Where(OzV11Kit.Alive).OrderBy(x => OzV11Kit.Dist(u, x)).FirstOrDefault();
                            if (t != null) OzV11Kit.CpuAttack(w, u, t.Leader ?? t);
                        }
                }
            }
            secs = w.Time - t0;
            redHpLeft = redB.Where(OzV11Kit.Alive).Sum(o => (double)o.Health) / Math.Max(1, hp0);
            blueTaken = 1.0 - blueB.Where(OzV11Kit.Alive).Sum(o => (double)Math.Max(0, o.Health)) / Math.Max(1, bhp0);
            return result;
        }

        public static Row Count(string settings, string side, string opp, int runs, int seedBase)
        {
            var r = new Row();
            for (int i = 0; i < runs; i++)
            {
                int res = Fight(settings, side, opp, seedBase + i * 7919, out double s, out double hp, out double bt);
                if (res == 1) r.red++; else if (res == 2) r.blue++; else r.draw++;
                r.secs += s; r.hpLeft += hp; r.blueTaken += bt; r.n++;
            }
            if (r.n > 0) { r.secs /= r.n; r.hpLeft /= r.n; r.blueTaken /= r.n; }
            return r;
        }

        static string Cell(Row r) => $"{r.red}/{r.blue}/{r.draw}  {r.secs:0.0} s  {r.hpLeft * 100:0}% / {r.blueTaken * 100:0}%";

        /// <summary>The fight table for the given fire-rate multiples of the hero squad (null = the kind's own), plus the grunt rows.
        /// "worth" = how many grunt squads the hero squad matches: the opponent count where its red wins cross half the fights.</summary>
        public static string Run(string settings, int runs = -1, double[] cycles = null)
        {
            if (runs <= 0) runs = Runs;
            var k = ZHeroSquads.Get(ZHeroSquads.AlanBrad);
            double own = k.CycleMul;
            cycles = cycles ?? new[] { own };
            var sb = new StringBuilder();
            sb.AppendLine($"DV108 hero squad fights: {runs} fights a row, open ground 11 tiles apart, both sides ordered to attack; cells: red wins / blue wins / draws, mean length, red hit points left / blue hit points taken");
            sb.AppendLine("| red side | " + string.Join(" | ", Opponents) + " |");
            sb.AppendLine("|---|" + string.Join("|", Opponents.Select(_ => "---")) + "|");
            var refRows = new Dictionary<string, Row[]>();
            foreach (var side in Sides.Skip(1))
            {
                var rows = Opponents.Select((o, i) => Count(settings, side, o, runs, 1000 + i * 100003)).ToArray();
                refRows[side] = rows;
                sb.AppendLine($"| {side} | " + string.Join(" | ", rows.Select(Cell)) + " |");
            }
            var heroRows = new Dictionary<double, Row[]>();
            try
            {
                foreach (var c in cycles)
                {
                    k.CycleMul = c;
                    var rows = Opponents.Select((o, i) => Count(settings, "ALAN & BRAD", o, runs, 1000 + i * 100003)).ToArray();
                    heroRows[c] = rows;
                    sb.AppendLine($"| ALAN & BRAD, shot pause x{c.ToString("0.00", CultureInfo.InvariantCulture)} ({0.5 * c:0.00} s) | " + string.Join(" | ", rows.Select(Cell)) + " |");
                }
            }
            finally { k.CycleMul = own; }
            sb.AppendLine();
            // the verdict on the kind's own number
            if (heroRows.TryGetValue(own, out var h))
            {
                var two = refRows["2 grunt squads"]; var three = refRows["3 grunt squads"];
                int tol = (int)Math.Ceiling(runs * 0.15);
                bool worthTwo = Enumerable.Range(0, 3).All(i => Math.Abs(h[i].red - two[i].red) <= tol) && h[0].red >= runs * 0.8;
                bool notThree = Enumerable.Range(0, 5).All(i => h[i].red <= three[i].red) && h[2].red < runs * 0.5;
                bool armour = h[3].blueTaken <= three[3].blueTaken + 0.02 && h[4].blueTaken <= three[4].blueTaken + 0.02;
                sb.AppendLine($"own shot pause x{own:0.00} ({0.5 * own:0.00} s, a grunt 0.50 s): wins against 1 / 2 / 3 grunt squads {h[0].red} / {h[1].red} / {h[2].red} of {runs}; two grunt squads: {two[0].red} / {two[1].red} / {two[2].red}; three: {three[0].red} / {three[1].red} / {three[2].red}");
                sb.AppendLine($"armour, hit points taken from it: light tank hero {h[3].blueTaken * 100:0} %, two squads {two[3].blueTaken * 100:0} %, three squads {three[3].blueTaken * 100:0} %; gun hero {h[4].blueTaken * 100:0} %, two squads {two[4].blueTaken * 100:0} %, three squads {three[4].blueTaken * 100:0} %");
                sb.AppendLine(OzV11Kit.Verdict(worthTwo, $"worth about two grunt squads: against one, two and three grunt squads it wins within {tol} fights of what two grunt squads win, and beats one squad in 80 % or more"));
                sb.AppendLine(OzV11Kit.Verdict(notThree, "never more than three: against every opponent it wins no more often than three grunt squads do, and beats three squads in under half the fights"));
                sb.AppendLine(OzV11Kit.Verdict(armour, "against the light tank and the gun it takes no more of their hit points than three grunt squads do"));
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------ the rule check
        public static string Rules(string settings, string l16MapJson = null)
        {
            var sb = new StringBuilder();
            var k = ZHeroSquads.Get(ZHeroSquads.AlanBrad);
            var w = OzV11Kit.World(settings, OzV11Kit.Plain(48, 48), 3);
            OzV11Kit.Dummy(w, 44 * 16 + 8, 44 * 16 + 8);
            var lead = w.CreateHeroSquad(ZHeroSquads.AlanBrad, Team.Red, new[] { 10, 10, 10, 12 });
            var g = w.S.Unit("grunt");
            var all = OzV11Kit.Squad(lead).ToList();
            bool two = all.Count == 2, brad = lead.HeroMemberName == "brad" && all[1].HeroMemberName == "alan" && all[1].Leader == lead;
            bool hp = all.All(o => o.MaxHealth == g.MaxHealth * 3 && o.Health == o.MaxHealth);
            bool cyc = all.All(o => Math.Abs(o.AttackSpeed - g.attack_speed * k.CycleMul) < 1e-9);
            bool typ = all.All(o => o.Type == "grunt" && o.Damage == g.Damage);
            sb.AppendLine(OzV11Kit.Verdict(two && brad, $"exactly two robots, Brad (#{lead.Id}) leads, Alan (#{all[1].Id}) is his squad member (name {k.Name})"));
            sb.AppendLine(OzV11Kit.Verdict(hp, $"hit points {lead.MaxHealth} each = 3 x a grunt's {g.MaxHealth}"));
            sb.AppendLine(OzV11Kit.Verdict(cyc && typ, $"grunt type and weapon row; shot pause {lead.AttackSpeed:0.00} s = {k.CycleMul:0.00} x a grunt's {g.attack_speed:0.00} s"));
            // the strong shot: one forced hit on a crewed light tank and on a grunt, against a grunt's hit
            int HitOn(ZObject shooter, string what)
            {
                var ww = OzV11Kit.World(settings, OzV11Kit.Plain(32, 32), 5);
                OzV11Kit.Dummy(ww, 30 * 16 + 8, 30 * 16 + 8);
                var s = shooter.IsHero ? ww.CreateHeroSquad(ZHeroSquads.AlanBrad, Team.Red, new[] { 8, 8, 8, 6 }) : ww.CreateRobotGroup("grunt", 8 * 16, 8 * 16, Team.Red, 1);
                ZObject t = what == "light" ? ww.CreateObject("vehicle", "light", 13 * 16, 8 * 16, Team.Blue) : ww.CreateRobotGroup("grunt", 13 * 16, 8 * 16, Team.Blue, 1);
                t.Damage = 0; t.MaxHealth = t.Health = 1000000;
                foreach (var m in OzV11Kit.Squad(s)) m.DamageChance = 1.0;
                foreach (var m in OzV11Kit.Squad(s)) m.SnipeChance = 0;
                foreach (var m in s.Minions.ToList()) { m.MoveSpeed = 0; m.Damage = 0; }
                OzV11Kit.CpuAttack(ww, s, t);
                int before = t.Health;
                for (int i = 0; i < 600 && t.Health == before; i++) { ww.Tick(ZConst.TICK); ww.Events.Clear(); }
                return before - t.Health;
            }
            using (OzV11Kit.GameSwitches())
            {
                int hl = HitOn(lead, "light"), gl = HitOn(w.CreateRobotGroup("grunt", 30 * 16, 30 * 16, Team.Red, 1), "light");
                int hg = HitOn(lead, "grunt"), gg = HitOn(w.CreateRobotGroup("grunt", 30 * 16, 32 * 16, Team.Red, 1), "grunt");
                sb.AppendLine(OzV11Kit.Verdict(hl == gl * 3 && gl > 0 && hg == gg * 3 && gg > 0, $"strong shot: one hit on a light tank {hl} HP (a grunt's {gl}), on a grunt {hg} HP (a grunt's {gg}, the rifle pair)"));
            }
            // Brad in front: three moves in three directions; Alan's place along Brad's way, sampled every 0.1 s while Brad walks, and where he stands after
            {
                var ww = OzV11Kit.World(settings, OzV11Kit.Plain(64, 40), 8);
                OzV11Kit.Dummy(ww, 62 * 16 + 8, 38 * 16 + 8);
                var hs = ww.CreateHeroSquad(ZHeroSquads.AlanBrad, Team.Red, new[] { 8, 20, 8, 22 });
                var al = hs.Minions[0];
                int behind = 0, side = 0, ahead = 0, stands = 0, standBehind = 0;
                foreach (var (gx, gy) in new[] { (52, 20), (30, 6), (12, 34) })
                {
                    OzV11Kit.PlayerMove(ww, hs, gx * 16 + 8, gy * 16 + 8, false);
                    double hx = 0, hy = 0;
                    for (int i = 0; i < 6000; i++)
                    {
                        ww.Tick(ZConst.TICK); ww.Events.Clear();
                        if (hs.IsMoving) { double l = Math.Sqrt(hs.Dx * hs.Dx + hs.Dy * hs.Dy); if (l > 1e-6) { hx = hs.Dx / l; hy = hs.Dy / l; } }
                        if (i % 10 == 0 && hs.IsMoving && i > 300)                                  // from 3 s on (the V forms after the start)
                        {
                            double p = (al.PosX - hs.PosX) * hx + (al.PosY - hs.PosY) * hy;
                            if (p < -4) behind++; else if (p > 4) ahead++; else side++;
                        }
                        if (i > 100 && !hs.IsMoving && hs.Waypoints.Count == 0 && !al.IsMoving) break;
                    }
                    stands++;
                    if ((al.PosX - hs.PosX) * hx + (al.PosY - hs.PosY) * hy < -4) standBehind++;
                }
                int n = behind + side + ahead;
                sb.AppendLine(OzV11Kit.Verdict(n > 0 && behind >= n * 0.95 && standBehind == stands, $"Brad in front: in three moves Alan walked behind Brad in {behind} of {n} samples (beside {side}, ahead {ahead}) and stood behind him after {standBehind} of {stands} moves"));
            }
            // vehicles: they board exactly as a grunt squad does on an order (the same rules; KH: never by themselves) and come out as themselves
            sb.Append(Vehicles(settings, k));
            // the loss of the squad: the match goes on while the team has other units
            {
                var ww = OzV11Kit.World(settings, OzV11Kit.Plain(48, 48), 7);
                var hs = ww.CreateHeroSquad(ZHeroSquads.AlanBrad, Team.Red, new[] { 8, 8, 8, 10 });
                ww.CreateRobotGroup("grunt", 40 * 16, 40 * 16, Team.Red);
                ww.CreateRobotGroup("grunt", 40 * 16, 8 * 16, Team.Blue);
                foreach (var m in OzV11Kit.Squad(hs).ToList()) { m.SetHealth(0); ww.UpdateObjectHealth(m); }
                for (int i = 0; i < 500; i++) { ww.Tick(ZConst.TICK); ww.Events.Clear(); }
                sb.AppendLine(OzV11Kit.Verdict(!ww.GameOver && OzV11Kit.Squad(hs).All(o => !OzV11Kit.Alive(o)), $"both lost, 5 s later the match runs on ({(ww.GameOver ? "OVER" : "not over")}): a normal loss"));
            }
            // KC4b round 4: together / apart (item 165) and the drink rule (items 167 / 168)
            sb.Append(PairRows(settings, k));
            sb.Append(AnticRows());
            // the level data: the "heroes" entry is an EXTRA squad with the switch on; off: the map alone
            if (l16MapJson != null) sb.Append(Level16(settings, l16MapJson, k));
            return sb.ToString();
        }

        // ------------------------------------------------------------------ vehicles, guns and troop carriers
        static int Ticks(ZWorld w, double secs, Func<bool> until = null)
        {
            int n = (int)Math.Round(secs / ZConst.TICK), i = 0;
            for (; i < n; i++) { w.Tick(ZConst.TICK); w.Events.Clear(); if (until != null && until()) break; }
            return i;
        }

        static ZObject HeroNamed(ZWorld w, string member) => w.Objects.FirstOrDefault(o => o.IsHero && o.HeroMemberName == member && OzV11Kit.Alive(o));

        /// <summary>One boarding: red <paramref name="hero"/> squad (else a grunt squad) on open ground, the vehicle 3 tiles east;
        /// ordered (<paramref name="order"/>) or idle 30 s. Returns the world, the squad's leader and the vehicle.</summary>
        static (ZWorld w, ZObject lead, ZObject v) Board(string settings, bool hero, string what, Team vOwner, bool order, int seed)
        {
            var w = OzV11Kit.World(settings, OzV11Kit.Plain(40, 40), seed);
            OzV11Kit.Dummy(w, 38 * 16 + 8, 38 * 16 + 8);
            var lead = hero ? w.CreateHeroSquad(ZHeroSquads.AlanBrad, Team.Red, new[] { 10, 10, 10, 12 }) : w.CreateRobotGroup("grunt", 10 * 16, 10 * 16, Team.Red);
            if (hero) { lead.Health = 2000; lead.Minions[0].Health = 1500; }                     // their own hit points, to see them carried through the crew list
            var v = w.CreateObject(what == "gun" ? "cannon" : "vehicle", what, 13 * 16, 10 * 16, vOwner);
            if (order) OzV11Kit.PlayerEnter(w, lead, v);
            Ticks(w, 30, () => order && (what == "apc" ? v.Passengers.Count > 0 : v.Drivers.Count > 0 && v.Owner == Team.Red));
            return (w, lead, v);
        }

        static string Vehicles(string settings, ZHeroKind k)
        {
            var sb = new StringBuilder();
            var g = OzV11Kit.World(settings, OzV11Kit.Plain(8, 8), 1).S.Unit("grunt");
            int maxHp = g.MaxHealth * k.HealthMul; double pause = g.attack_speed * k.CycleMul;
            bool Itself(ZObject o, string member, int hp) => o != null && o.IsHero && o.Hero == k && o.HeroMemberName == member && o.MaxHealth == maxHp && o.Health == hp
                                                             && Math.Abs(o.AttackSpeed - pause) < 1e-9 && o.Type == k.BaseType;
            // 1 a jeep by order: Brad (the leader) drives as a grunt leader would; a jeep has no EXIT (Zod: only a troop carrier and a gun
            //   let their crew out), for a grunt squad the same
            {
                var (w, lead, v) = Board(settings, true, "jeep", Team.Neutral, true, 21);
                var (wg, gl, vg) = Board(settings, false, "jeep", Team.Neutral, true, 21);
                int nIn = v.Drivers.Count, hp0 = nIn > 0 ? v.Drivers[0] : 0;
                string line = OzHeroPair.CrewName(v, false);                                         // KC4b round 4: one of them aboard = his own name
                bool inH = v.Owner == Team.Red && nIn == 1 && v.HeroCrewKind(false) == k && line == "BRAD" && hp0 == 2000 && HeroNamed(w, "brad") == null;
                bool inG = vg.Owner == Team.Red && vg.Drivers.Count == nIn;
                var alan = HeroNamed(w, "alan");
                bool ejH = w.Eject(Team.Red, v), ejG = wg.Eject(Team.Red, vg);
                bool same = ejH == ejG && Itself(alan, "alan", 1500);
                sb.AppendLine(OzV11Kit.Verdict(inH && inG && same, $"jeep by order: {nIn} of the squad got in (a grunt squad's same order: {vg.Drivers.Count}), the crew line names \"{line ?? "a grunt"}\", " +
                                                                    $"stored hit points {hp0}; EXIT {(ejH ? "taken" : "refused")} as for the grunt's jeep ({(ejG ? "taken" : "refused")}); Alan waited outside as himself"));
            }
            // 2 a gun by order: the same; EXIT: Brad again, as a gun's crew does (it never takes the gun back by itself)
            {
                var (w, lead, v) = Board(settings, true, "gun", Team.Neutral, true, 22);
                var (wg, gl, vg) = Board(settings, false, "gun", Team.Neutral, true, 22);
                int nIn = v.Drivers.Count;
                string line = OzHeroPair.CrewName(v, false);
                bool inH = v.Owner == Team.Red && nIn >= 1 && v.HeroCrewKind(false) == k && line == "BRAD";
                bool inG = vg.Owner == Team.Red && vg.Drivers.Count == nIn;
                bool ej = w.Eject(Team.Red, v); Ticks(w, 0.1);
                var brad = HeroNamed(w, "brad");
                bool outOk = ej && Itself(brad, "brad", 2000) && brad.JustLeftCannon && v.Drivers.Count == 0;
                sb.AppendLine(OzV11Kit.Verdict(inH && inG && outOk, $"gun by order: {nIn} got in (a grunt squad: {vg.Drivers.Count}), crew line \"{line ?? "-"}\"; EXIT: {(brad != null ? $"Brad out as himself, {brad.Health}/{brad.MaxHealth} HP, shot pause {brad.AttackSpeed:0.000} s" : "NO Brad")}"));
            }
            // 3 the own troop carrier (DV15): both ride as its passenger squad; EXIT: both out, Brad leads, Alan behind him, each with his own hit points
            {
                var (w, lead, v) = Board(settings, true, "apc", Team.Red, true, 23);
                var (wg, gl, vg) = Board(settings, false, "apc", Team.Red, true, 23);
                string pl = OzHeroPair.CrewName(v, true);                                            // KC4b round 4: both aboard = the pair's name
                bool inH = v.Passengers.Count == 2 && v.HeroCrewKind(true) == k && pl == k.Name && v.Passengers[0] == 2000 && v.Passengers[1] == 1500 && v.HeroesAboard == 2;
                bool inG = vg.Passengers.Count == g.group_amount;
                int nIn = v.Passengers.Count; string stored = string.Join(" / ", v.Passengers);
                bool ej = w.Eject(Team.Red, v); Ticks(w, 0.1);
                var brad = HeroNamed(w, "brad"); var alan = HeroNamed(w, "alan");
                bool outOk = ej && Itself(brad, "brad", 2000) && Itself(alan, "alan", 1500) && !brad.IsMinion && alan.Leader == brad && v.Passengers.Count == 0 && v.Drivers.Count == 1;
                sb.AppendLine(OzV11Kit.Verdict(inH && inG && outOk, $"own troop carrier by order: {nIn} passengers (the whole squad, as a grunt squad's {vg.Passengers.Count}), passengers line \"{pl ?? "-"}\", stored {stored} HP; " +
                                                                    $"EXIT: {(brad != null && alan != null ? $"Brad {brad.Health}/{brad.MaxHealth} leads, Alan {alan.Health}/{alan.MaxHealth} {(alan.Leader == brad ? "behind him" : "NOT his squad")}" : "NOT both out")}; the grunt driver stays"));
            }
            // 4 by themselves: idle beside an empty jeep, no order: KH (item 186, switch ZWorld.HeroOrderedBoardingDeviation on) they never take it
            // while a grunt squad does; with the switch off they take it exactly when a grunt squad does (the old rule)
            {
                var (w, lead, v) = Board(settings, true, "jeep", Team.Neutral, false, 24);
                var (wg, gl, vg) = Board(settings, false, "jeep", Team.Neutral, false, 24);
                bool heroIn = v.Owner == Team.Red && v.Drivers.Count > 0, gruntIn = vg.Owner == Team.Red && vg.Drivers.Count > 0;
                bool same = ZWorld.HeroOrderedBoardingDeviation ? !heroIn                             // KH (item 186): the heroes never take it, the grunts still do
                          : heroIn == gruntIn && v.Drivers.Count == vg.Drivers.Count && (!heroIn || (v.HeroCrewKind(false) == k && OzHeroPair.CrewName(v, false) == "BRAD"));
                sb.AppendLine(OzV11Kit.Verdict(same && gruntIn, $"{(ZWorld.HeroOrderedBoardingDeviation ? "not by themselves (KH; " : "by themselves (")}idle 30 s, a neutral jeep 3 tiles away): {(heroIn ? $"they took it ({v.Drivers.Count} aboard, crew line \"{OzHeroPair.CrewName(v, false) ?? "a grunt"}\")" : "they did NOT take it")}; a grunt squad {(gruntIn ? $"took it ({vg.Drivers.Count} aboard)" : "did not")}"));
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------ KC4b (item 165): together and apart (View/OzHeroPair.cs)
        static string Who(ZObject o) => o == null ? "-" : $"\"{OzHeroPair.NameOf(o)}\" / picture {OzHeroPair.PictureKey(o)} / speakers {OzHeroPair.Mates(o).Count}";

        /// <summary>Each way the two come apart (and together again): the name, the picture and who may speak, as the unit panel and the
        /// voice lines read them (OzHeroPair), and the crew lines of a vehicle they sit in.</summary>
        static string PairRows(string settings, ZHeroKind k)
        {
            var sb = new StringBuilder();
            bool Pair(ZObject o) => o != null && OzHeroPair.NameOf(o) == k.Name && OzHeroPair.PictureKey(o) == "alanbrad" && OzHeroPair.Mates(o).Count == 2;
            bool Own(ZObject o, string name) => o != null && OzHeroPair.NameOf(o) == name.ToUpperInvariant() && OzHeroPair.PictureKey(o) == name && OzHeroPair.Mates(o).Count == 1;
            ZWorld World(int seed, int w = 48) { var ww = OzV11Kit.World(settings, OzV11Kit.Plain(w, 40), seed); OzV11Kit.Dummy(ww, (w - 2) * 16 + 8, 38 * 16 + 8); return ww; }
            // 1 together (the level start)
            {
                var w = World(41); var b = w.CreateHeroSquad(ZHeroSquads.AlanBrad, Team.Red, new[] { 10, 10, 10, 12 }); var a = b.Minions[0];
                sb.AppendLine(OzV11Kit.Verdict(Pair(b) && Pair(a), $"together: Brad {Who(b)}, Alan {Who(a)} (the pair's name and picture, either may answer)"));
            }
            // 2 Alan dies / 3 Brad dies (the hand-over: Alan leads)
            {
                var w = World(42); var b = w.CreateHeroSquad(ZHeroSquads.AlanBrad, Team.Red, new[] { 10, 10, 10, 12 }); var a = b.Minions[0];
                a.SetHealth(0); w.UpdateObjectHealth(a); Ticks(w, 0.2);
                bool ok2 = Own(b, "brad");
                var w3 = World(43); var b3 = w3.CreateHeroSquad(ZHeroSquads.AlanBrad, Team.Red, new[] { 10, 10, 10, 12 }); var a3 = b3.Minions[0];
                b3.SetHealth(0); w3.UpdateObjectHealth(b3); Ticks(w3, 0.2);
                bool ok3 = Own(a3, "alan") && a3.Leader == null && b3.Successor == a3;
                sb.AppendLine(OzV11Kit.Verdict(ok2 && ok3, $"one dies: Alan dead, Brad {Who(b)}; Brad dead, Alan leads (the squad's hand-over) {Who(a3)}"));
            }
            // 4 Brad gets into a gun, Alan waits outside; 7 Brad out again: two squads, each on his own (no rule joins two squads)
            {
                var (w, lead, v) = Board(settings, true, "gun", Team.Neutral, true, 44);
                Ticks(w, 0.2);
                var alan = HeroNamed(w, "alan");
                bool ok4 = Own(alan, "alan") && OzHeroPair.CrewName(v, false) == "BRAD";
                string crew = OzHeroPair.CrewName(v, false) ?? "-";
                bool ej = w.Eject(Team.Red, v); Ticks(w, 0.2);
                var brad = HeroNamed(w, "brad");
                bool ok7 = ej && Own(brad, "brad") && Own(alan, "alan") && OzHeroPair.CrewName(v, false) == null;
                sb.AppendLine(OzV11Kit.Verdict(ok4 && ok7, $"one in a gun: crew line \"{crew}\", Alan outside {Who(alan)}; EXIT: Brad {Who(brad)}, Alan {Who(alan)} (two squads now: apart until they ride out of one troop carrier together)"));
            }
            // 5 the DV98 split: Alan starts 50 tiles (800 px) from Brad: after the 1 s check each is a squad of his own
            {
                var w = World(45, 72); var b = w.CreateHeroSquad(ZHeroSquads.AlanBrad, Team.Red, new[] { 8, 10, 58, 10 }); var a = b.Minions[0];
                Ticks(w, 2.5);
                sb.AppendLine(OzV11Kit.Verdict(w.SquadSplits >= 1 && a.Leader == null && Own(b, "brad") && Own(a, "alan"), $"the squad split (DV98, {ZWorld.SquadSplitPx} px apart): splits {w.SquadSplits}; Brad {Who(b)}, Alan {Who(a)}"));
            }
            // 6 both into the own troop carrier: the passengers' line is the pair; EXIT: one squad again (Brad leads): together again
            {
                var (w, lead, v) = Board(settings, true, "apc", Team.Red, true, 46);
                string pass = OzHeroPair.CrewName(v, true) ?? "-";
                bool ej = w.Eject(Team.Red, v); Ticks(w, 0.2);
                var brad = HeroNamed(w, "brad"); var alan = HeroNamed(w, "alan");
                bool ok = pass == k.Name && ej && alan != null && alan.Leader == brad && Pair(brad) && Pair(alan);
                sb.AppendLine(OzV11Kit.Verdict(ok, $"both in the own troop carrier: passengers \"{pass}\"; EXIT: together again, Brad {Who(brad)}, Alan {Who(alan)}"));
            }
            // 8 a neutral troop carrier: Brad drives, Alan rides (DV15's split): one name on each line
            {
                var (w, lead, v) = Board(settings, true, "apc", Team.Neutral, true, 47);
                string dr = OzHeroPair.CrewName(v, false) ?? "-", ps = OzHeroPair.CrewName(v, true) ?? "-";
                sb.AppendLine(OzV11Kit.Verdict(dr == "BRAD" && ps == "ALAN", $"a neutral troop carrier taken: driver line \"{dr}\", passengers \"{ps}\" (the leader drives, the other rides)"));
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------ KC4b (items 167 / 168): the drink every 30 s (View/OzHeroAnticPlan.cs)
        static string AnticRows()
        {
            var sb = new StringBuilder();
            var rnd = new Random(5);
            string Times(List<double> l) => string.Join(", ", l.Select(x => x.ToString("0.0", CultureInfo.InvariantCulture)));
            // 1 two minutes and ten seconds of idle, a drink lasting 7 s: four each, 30 s start to start, Alan 4 s after Brad
            {
                var s = OzHeroAnticPlan.Simulate(130, 7, 2, 0.5, () => rnd.NextDouble());
                var gaps = new List<double>(); for (int m = 0; m < 2; m++) for (int i = 1; i < s[m].Count; i++) gaps.Add(s[m][i] - s[m][i - 1]);
                double lock_ = double.MaxValue; foreach (var x in s[0]) foreach (var y in s[1]) lock_ = Math.Min(lock_, Math.Abs(x - y));
                bool ok = s[0].Count == 4 && s[1].Count == 4 && gaps.All(g => g >= 28.9 && g <= 31.6) && s[0][0] >= 29.9 && s[0][0] <= 30.6 && s[1][0] >= 33.9 && lock_ >= 2.0;
                sb.AppendLine(OzV11Kit.Verdict(ok, $"drink rule, idle 130 s: Brad drinks at {Times(s[0])} s, Alan at {Times(s[1])} s ({s[0].Count} + {s[1].Count}; start to start {gaps.Min():0.0}-{gaps.Max():0.0} s; never within {lock_:0.0} s of each other)"));
            }
            // 2 an order (or an enemy near) from 45 to 55 s: no drink while busy, the next one 30 s after they stand idle again
            {
                var s = OzHeroAnticPlan.Simulate(130, 7, 2, 0.5, () => rnd.NextDouble(), new List<(double, double)> { (45, 55) });
                bool none = s.All(l => l.All(x => x < 45 || x >= 55));
                double firstAfter = s.Min(l => l.Where(x => x >= 55).DefaultIfEmpty(999).Min());
                bool ok = none && firstAfter >= 55 + OzHeroAnticPlan.Period - 0.5 - 0.01 && s[0].Count >= 3 && s[1].Count >= 3;
                sb.AppendLine(OzV11Kit.Verdict(ok, $"drink rule with an order / an enemy near from 45 to 55 s: Brad {Times(s[0])} s, Alan {Times(s[1])} s; none while busy, the first after it at {firstAfter:0.0} s (30 s of idle again, from the last busy scan)"));
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------ level 16: the extra squad and its campfire
        static string Level16(string settings, string l16MapJson, ZHeroKind k)
        {
            var sb = new StringBuilder();
            bool was = ZWorld.HeroSquadDeviation;
            try
            {
                ZWorld.HeroSquadDeviation = false;
                var off = ZWorld.Create(settings, l16MapJson, 1);
                ZWorld.HeroSquadDeviation = true;
                var on = ZWorld.Create(settings, l16MapJson, 1);
                var entry = on.Map.heroes?.FirstOrDefault(h => h != null && h.squad == ZHeroSquads.AlanBrad);
                var heroes = on.Objects.Where(o => o.IsHero).ToList();
                string Key(ZObject o) => $"{o.Kind}|{o.Type}|{o.Owner}|{o.LocX}|{o.LocY}|{o.Health}";
                var offKeys = off.Objects.Select(Key).OrderBy(x => x, StringComparer.Ordinal).ToList();
                var onKeys = on.Objects.Where(o => !o.IsHero).Select(Key).OrderBy(x => x, StringComparer.Ordinal).ToList();
                bool sameMap = offKeys.SequenceEqual(onKeys) && off.Map.objects.Count == on.Map.objects.Count && !off.Objects.Any(o => o.IsHero) && off.HeroSquadsMade == 0;
                int redOff = off.Objects.Count(o => o.IsRobot && o.Owner == Team.Red), redOn = on.Objects.Count(o => o.IsRobot && o.Owner == Team.Red);
                int blueOff = off.Objects.Count(o => o.IsUnit && o.Owner == Team.Blue), blueOn = on.Objects.Count(o => o.IsUnit && o.Owner == Team.Blue);
                int psyOff = off.Objects.Count(o => o.Type == "psycho" && o.Owner == Team.Red && o.Source != null && o.Source.x == 58 && o.Source.y == 112);
                int psyOn = on.Objects.Count(o => o.Type == "psycho" && o.Owner == Team.Red && o.Source != null && o.Source.x == 58 && o.Source.y == 112);
                var brad = heroes.FirstOrDefault(o => o.HeroMemberName == "brad"); var alan = heroes.FirstOrDefault(o => o.HeroMemberName == "alan");
                bool placed = entry != null && heroes.Count == 2 && on.HeroSquadsMade == 1 && brad != null && alan != null && !brad.IsMinion && alan.Leader == brad && brad.Owner == Team.Red
                              && entry.at != null && entry.at.Length >= 4 && brad.LocX == entry.at[0] * 16 && brad.LocY == entry.at[1] * 16 && alan.LocX == entry.at[2] * 16 && alan.LocY == entry.at[3] * 16;
                bool front = brad != null && alan != null && brad.LocY == alan.LocY && brad.LocX < alan.LocX;   // KC4b: side by side on one row, Brad on the left (the camp plan)
                sb.AppendLine(OzV11Kit.Verdict(placed && front && sameMap && redOn == redOff + 2 && blueOn == blueOff && psyOn == psyOff && psyOn == on.S.Unit("psycho").group_amount,
                    $"level 16 extra squad: switch on: {(brad != null ? $"Brad tile ({brad.LocX / 16},{brad.LocY / 16})" : "NO Brad")}, {(alan != null ? $"Alan tile ({alan.LocX / 16},{alan.LocY / 16})" : "NO Alan")} (side by side, Brad on the left); " +
                    $"red start robots {redOff} -> {redOn} (+2), the psycho squad at tile (58,112) there with both ({psyOff} / {psyOn} robots), blue units {blueOff} -> {blueOn}, " +
                    $"the map's {off.Map.objects.Count} entries and all {off.Objects.Count} objects of its world the same; switch off ('Zod exact'): no hero, the map alone"));
                // the start tiles and the campfire tile: open ground, clear of the shuttle square, its searchlight, buildings, map items and every building's exit lane / fort gate face
                var sq = ZWorld.L16ShuttleSquare; var sl = ZWorld.L16ShuttleSearchlightTile;
                var tiles = new List<(string what, int x, int y, int r)>();
                if (entry?.at != null) for (int i = 0; i + 1 < entry.at.Length; i += 2) tiles.Add((i == 0 ? "Brad" : "Alan", entry.at[i], entry.at[i + 1], 8));
                bool hasCamp = entry?.camp != null && entry.camp.Length >= 2;
                if (hasCamp) tiles.Add(("campfire", entry.camp[0], entry.camp[1], 7));
                var bad = new List<string>();
                foreach (var (what, x, y, r) in tiles)
                {
                    if (x >= sq[0] && x < sq[0] + sq[2] && y >= sq[1] && y < sq[1] + sq[3]) bad.Add(what + " on the shuttle");
                    if (x == sl[0] && y == sl[1]) bad.Add(what + " on the searchlight");
                    if (on.Map.ClassAt(x, y) != ZClass.Ground) bad.Add(what + " not on open ground (" + on.Map.ClassAt(x, y) + ")");
                    foreach (var o in on.Objects)
                        if (!o.IsUnit && !o.Destroyed && x * 16 + 8 >= o.LocX && x * 16 + 8 < o.LocX + Math.Max(16, o.WidthPix) && y * 16 + 8 >= o.LocY && y * 16 + 8 < o.LocY + Math.Max(16, o.HeightPix)) bad.Add(what + " on " + o.Type);
                    if (on.InExitLane(x * 16 + 8, y * 16 + 8, r)) bad.Add(what + " in a building's exit lane");
                    if (on.NearGateFace(x * 16, y * 16, x * 16 + 16, y * 16 + 16, 32)) bad.Add(what + " within 2 tiles of a fort's gate face");
                }
                string near = "";
                if (hasCamp && brad != null && alan != null)
                {
                    double db = Math.Sqrt(Math.Pow(entry.camp[0] - brad.LocX / 16, 2) + Math.Pow(entry.camp[1] - brad.LocY / 16, 2)), da = Math.Sqrt(Math.Pow(entry.camp[0] - alan.LocX / 16, 2) + Math.Pow(entry.camp[1] - alan.LocY / 16, 2));
                    near = $", {db * 2:0.0} m from Brad and {da * 2:0.0} m from Alan";
                    if (db > 3 || da > 3) bad.Add("campfire not beside the loungers");
                    if (entry.camp[1] <= Math.Max(brad.LocY, alan.LocY) / 16) bad.Add("campfire not in front of the loungers (they lie toward the back)");
                }
                sb.AppendLine(OzV11Kit.Verdict(tiles.Count == 3 && hasCamp && bad.Count == 0,
                    $"level 16 start and campfire tiles: {string.Join(", ", tiles.Select(t => $"{t.what} ({t.x},{t.y})"))}{near}: open ground, clear of the shuttle square ({sq[0]}-{sq[0] + sq[2] - 1}, {sq[1]}-{sq[1] + sq[3] - 1}), the searchlight ({sl[0]},{sl[1]}), every building and map item, every exit lane and fort gate face" +
                    (bad.Count > 0 ? "; WRONG: " + string.Join("; ", bad) : "")));
                if (brad != null && alan != null) sb.AppendLine(CampCheck(on, brad, alan, sq, sl));
            }
            finally { ZWorld.HeroSquadDeviation = was; }
            return sb.ToString();
        }

        /// <summary>KC4b: the start camp as the game lays it out (OzHeroCampPlan, here from the start tiles with the heroes' nominal
        /// scale): a shallow V facing the fire, the fire's clear gap to the loungers' foot ends 1.0 to 1.6 m, the loungers and the two
        /// parasol canopies apart, every footprint tile (fire, both loungers, both parasol feet) open ground, no building or map item, no
        /// exit lane, no fort gate face, off the shuttle square and its searchlight.</summary>
        static string CampCheck(ZWorld on, ZObject brad, ZObject alan, int[] sq, int[] sl)
        {
            OzHeroCampPlan.PxToWorld(brad.LocX + 8, brad.LocY + 8, out double bx, out double bz);
            OzHeroCampPlan.PxToWorld(alan.LocX + 8, alan.LocY + 8, out double ax, out double az);
            var plan = OzHeroCampPlan.Plan(new[] { bx, ax }, new[] { bz, az }, new[] { OzHeroCampPlan.NominalK("brad"), OzHeroCampPlan.NominalK("alan") },
                                           new[] { OzHeroCampPlan.BradBedWidth, OzHeroCampPlan.AlanBedWidth }, 0, 1);
            var bad = new List<string>();
            var foot = new Dictionary<(int, int), List<string>>();
            void Mark(string what, double x0, double z0, double x1, double z1)
            {
                OzHeroCampPlan.Tile(Math.Min(x0, x1), Math.Max(z0, z1), out int tx0, out int ty0);
                OzHeroCampPlan.Tile(Math.Max(x0, x1) - 1e-6, Math.Min(z0, z1) + 1e-6, out int tx1, out int ty1);
                for (int y = ty0; y <= ty1; y++) for (int x = tx0; x <= tx1; x++) { if (!foot.TryGetValue((x, y), out var l)) foot[(x, y)] = l = new List<string>(); if (!l.Contains(what)) l.Add(what); }
            }
            Mark("fire", plan.fireX - plan.fireHalfWidth, plan.fireZ - plan.fireHalfDepth, plan.fireX + plan.fireHalfWidth, plan.fireZ + plan.fireHalfDepth);
            string[] names = { "Brad", "Alan" };
            for (int i = 0; i < 2; i++)
            {
                var s = plan.spots[i]; var c = s.Corners();
                double minX = double.MaxValue, maxX = double.MinValue, minZ = double.MaxValue, maxZ = double.MinValue;
                for (int j = 0; j < 8; j += 2) { minX = Math.Min(minX, c[j]); maxX = Math.Max(maxX, c[j]); minZ = Math.Min(minZ, c[j + 1]); maxZ = Math.Max(maxZ, c[j + 1]); }
                Mark(names[i] + "'s lounger", minX, minZ, maxX, maxZ);
                Mark(names[i] + "'s parasol foot", s.poleX - 0.3, s.poleZ - 0.3, s.poleX + 0.3, s.poleZ + 0.3);
            }
            foreach (var kv in foot)
            {
                int x = kv.Key.Item1, y = kv.Key.Item2; string what = string.Join("/", kv.Value);
                if (x >= sq[0] && x < sq[0] + sq[2] && y >= sq[1] && y < sq[1] + sq[3]) bad.Add(what + " on the shuttle");
                if (x == sl[0] && y == sl[1]) bad.Add(what + " on the searchlight");
                if (on.Map.ClassAt(x, y) != ZClass.Ground) bad.Add($"{what} not on open ground at ({x},{y})");
                foreach (var o in on.Objects)
                    if (!o.IsUnit && !o.Destroyed && x * 16 + 8 >= o.LocX && x * 16 + 8 < o.LocX + Math.Max(16, o.WidthPix) && y * 16 + 8 >= o.LocY && y * 16 + 8 < o.LocY + Math.Max(16, o.HeightPix)) bad.Add($"{what} on {o.Type} at ({x},{y})");
                if (on.InExitLane(x * 16 + 8, y * 16 + 8, 7)) bad.Add($"{what} in a building's exit lane at ({x},{y})");
                if (on.NearGateFace(x * 16, y * 16, x * 16 + 16, y * 16 + 16, 32)) bad.Add($"{what} within 2 tiles of a fort's gate face at ({x},{y})");
            }
            // the loungers apart (sampled edges) and the canopies apart (their circles over the ground, leaning away from the camera)
            double apart = double.MaxValue;
            var e0 = Edge(plan.spots[0].Corners()); var e1 = Edge(plan.spots[1].Corners());
            foreach (var p in e0) foreach (var q in e1) apart = Math.Min(apart, Math.Sqrt((p.Item1 - q.Item1) * (p.Item1 - q.Item1) + (p.Item2 - q.Item2) * (p.Item2 - q.Item2)));
            double lean0 = OzHeroCampPlan.ParasolCanopyY * OzHeroCampPlan.NominalK("brad") * Math.Sin(OzHeroCampPlan.ParasolTilt * Math.PI / 180), lean1 = OzHeroCampPlan.ParasolCanopyY * OzHeroCampPlan.NominalK("alan") * Math.Sin(OzHeroCampPlan.ParasolTilt * Math.PI / 180);
            double cdx = plan.spots[0].poleX - plan.spots[1].poleX, cdz = (plan.spots[0].poleZ + lean0) - (plan.spots[1].poleZ + lean1);
            double canopies = Math.Sqrt(cdx * cdx + cdz * cdz) - OzHeroCampPlan.ParasolCanopyR * (OzHeroCampPlan.NominalK("brad") + OzHeroCampPlan.NominalK("alan"));
            double gap = plan.FireGap();
            double vDeg = Math.Abs(((plan.spots[0].yaw - plan.spots[1].yaw) % 360.0 + 540.0) % 360.0 - 180.0);   // the angle between their facings
            bool vOk = plan.spots[0].side == -1 && plan.spots[1].side == 1 && Math.Abs(vDeg - 2 * OzHeroCampPlan.VAngle) < 0.1 && vDeg <= 30.0
                       && (OzHeroCampPlan.VAngle < 0.01 || (plan.spots[0].fx > 0 && plan.spots[1].fx < 0));      // each turned in toward the fire (VAngle 0: parallel, item 175)
            if (gap < 1.0 || gap > 1.6) bad.Add($"fire gap {gap:F2} m (1.0 to 1.6 wanted)");
            if (apart < 0.3) bad.Add($"loungers only {apart:F2} m apart");
            if (canopies <= 0) bad.Add($"the parasol canopies overlap ({canopies:F2} m)");
            if (!vOk) bad.Add(OzHeroCampPlan.VAngle < 0.01 ? "not parallel facing the fire" : "not a shallow V facing the fire");
            var tiles = foot.Keys.OrderBy(k2 => k2.Item2).ThenBy(k2 => k2.Item1).Select(k2 => $"({k2.Item1},{k2.Item2})");
            return OzV11Kit.Verdict(bad.Count == 0,
                $"level 16 start camp (the game's plan from the start tiles): fire at ({plan.fireX:F1}, {plan.fireZ:F1}) {plan.ahead:F2} m in front of the pair, clear gap to the loungers' foot ends {gap:F2} m, " +
                $"loungers turned {OzHeroCampPlan.VAngle:0} deg in each (a {2 * OzHeroCampPlan.VAngle:0} deg V facing the fire), {apart:F2} m apart, parasol canopies {canopies:F2} m apart (Brad's on the left, Alan's on the right); " +
                $"{foot.Count} footprint tiles {string.Join(" ", tiles)}: open ground, no building or map item, no exit lane, no gate face, off the shuttle and the searchlight" +
                (bad.Count > 0 ? "; WRONG: " + string.Join("; ", bad) : ""));
        }

        static List<(double, double)> Edge(double[] c)
        {
            var l = new List<(double, double)>();
            for (int i = 0; i < 4; i++)
            {
                double ax = c[2 * i], az = c[2 * i + 1], bx = c[(2 * i + 2) % 8], bz = c[(2 * i + 3) % 8];
                for (int s = 0; s <= 40; s++) l.Add((ax + (bx - ax) * s / 40.0, az + (bz - az) * s / 40.0));
            }
            return l;
        }

#if UNITY_EDITOR
        [UnityEditor.MenuItem("OpenZ/V1.1/Hero Squad: Count Fights")]
        static void MenuFights() => OzV11Kit.RunMenu("Hero squad fights (DV108)", () => Run(OzV11Kit.SettingsText));
        [UnityEditor.MenuItem("OpenZ/V1.1/Hero Squad: Rule Check")]
        static void MenuRules() => OzV11Kit.RunMenu("Hero squad rules (DV108)", () => Rules(OzV11Kit.SettingsText, OzV11Kit.MapText("L16")));
#endif
    }
}
