using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// K443 counted check (rule DV131, Sim/ZWorld.ShuttleHp.cs: every shuttle starts with the light tank's hit points x 1.2). Headless
    /// (ZWorld.Create + Tick, no scene, nothing saved); menu OpenZ/V1.1/Shuttle: K443 More HP Check (out of Play), Editor/OzShuttleHpMenu.cs.
    /// <list type="bullet">
    /// <item>A level 16's own ship: 4054 of 4054 hit points with the switch on, the light tank's 3378 with it off; a light tank keeps 3378.</item>
    /// <item>B every shuttle the rules make (the testing panel's red, blue and empty drops, a map's "vehicle:shuttle" entry for nobody, red
    /// and blue) starts with the same 4054: an enemy ship matches the player's.</item>
    /// <item>C shots to shoot it down, switch off against on, several seeds: the ship hovering at 12 m, one blue attacker of each heavy
    /// weapon at 0.7 of its range on a plain map (the set-up of DV118's check I), and the blue fort's big gun on level 16 (the other blue
    /// guns and units hold their fire). Every hit is the same until the old 3378 would have run out (the rules read no health before 0),
    /// and the ship then needs as many hits or more.</item>
    /// <item>D the repair station on level 16: from half health the pad heals it to its full 4054 in the same 5 s as before (its pad heals
    /// what is missing, whatever the maximum), 1.2 x the hit points a pad tick.</item>
    /// <item>E the damaged look reads a share of the new maximum (under 40 %, 40-70 %).</item>
    /// <item>F deterministic: the same seed twice gives the same fight; the switch is back on afterwards.</item>
    /// </list>
    /// </summary>
    public static class OzShuttleHpCount
    {
        const double Tick = ZConst.TICK;
        /// <summary>Seeds a weapon (the menu's check); the parity row uses fewer.</summary>
        public const int Seeds = 4;
        /// <summary>The weapons of C (a Tough's rockets, the missile launcher, the three tanks, the three guns); the fort's big gun after them.</summary>
        public static readonly string[] Weapons = { "tough", "missile_launcher", "light", "medium", "heavy", "gun", "howitzer", "missile_cannon" };
        public const double WeaponLimitSec = 120, FortGunLimitSec = 300;

        static string F(double v, string f = "0.00") => v.ToString(f, System.Globalization.CultureInfo.InvariantCulture);
        static string P(int x, int y) => x + "," + y;

        /// <summary>One fight against the ship: the shots fired at it, every health drop (game time, hit points), when it went down.</summary>
        sealed class Fight
        {
            public int maxHp, shots, hpLeft = -1; public double downAt = -1, endAt; public bool down;
            public readonly List<(double t, int hp)> drops = new List<(double, int)>();
            public int Hits => drops.Count;
            public string Drops => string.Join(" ", drops.Select(d => d.hp.ToString()));
            public ulong Digest()
            {
                ulong h = 1469598103934665603UL;
                void Fold(long v) { h ^= (ulong)v; h *= 1099511628211UL; }
                Fold(maxHp); Fold(shots); Fold(hpLeft); Fold((long)Math.Round(downAt * 100));
                foreach (var d in drops) { Fold((long)Math.Round(d.t * 100)); Fold(d.hp); }
                return h;
            }
        }

        static void Run(ZWorld w, double secs, Func<bool> until = null)
        {
            double end = w.Time + secs;
            while (w.Time < end) { w.Tick(Tick); w.Events.Clear(); if (until != null && until()) return; }
        }

        /// <summary>Ticks the world until the ship is down or the time is up, counting the shots fired at it (by `from` only, when given);
        /// `each` runs before every tick.</summary>
        static Fight Watch(ZWorld w, ZObject s, double limit, int from = -1, Action each = null)
        {
            var f = new Fight { maxHp = s.MaxHealth };
            int last = s.Health; double t0 = w.Time, end = w.Time + limit;
            w.Events.Clear();
            while (w.Time < end)
            {
                each?.Invoke();
                w.Tick(Tick);
                foreach (var e in w.Events)
                    if (e.type == ZEventType.MissileFired && e.otherId == s.Id && (from < 0 || e.objId == from)) f.shots++;
                w.Events.Clear();
                if (s.Health < last) { f.drops.Add((w.Time - t0, last - Math.Max(0, s.Health))); last = Math.Max(0, s.Health); }
                if (s.Destroyed) { f.down = true; f.downAt = w.Time - t0; break; }
            }
            f.endAt = w.Time - t0; f.hpLeft = Math.Max(0, s.Health);
            return f;
        }

        /// <summary>C: one blue attacker of `type` at 0.7 of its range against the red ship hovering at 12 m on a plain map (DV118's check I).</summary>
        static Fight WeaponFight(string settings, string type, int seed)
        {
            var w = OzV11Kit.World(settings, OzV11Kit.Plain(80, 80), seed);
            OzV11Kit.Dummy(w, 70 * 16, 70 * 16);
            var s = w.CreatePlayableShuttle(400, 640, Team.Red);
            s.ShuttleState = ZShuttleState.Hover; s.ShuttleLift = 1;
            var under = OzV11Kit.Place(w, "tough", 400, 640, Team.Red, 1); under.MoveSpeed = 0; under.Damage = 0; under.MaxHealth = under.Health = 1000000;   // it cannot land on it
            var u = w.S.Unit(type);
            int range = Math.Max(40, u?.attack_radius ?? 100);
            var a = OzV11Kit.Place(w, type, 400 + (int)(range * 0.7), 640, Team.Blue);
            if (a.IsRobot) { a.Grenades = 0; foreach (var m in a.Minions) m.Grenades = 0; }
            OzV11Kit.CpuAttack(w, a, s);
            return Watch(w, s, WeaponLimitSec);
        }

        /// <summary>C: the blue fort's big gun (DV7 / DV25) on level 16 against the red ship hovering `d` px off the fort's front wall (the
        /// gun's rear arc is behind it; its reach is 232 px from the fort's middle, nothing within 64 px of its walls); every other blue unit
        /// and gun holds its fire (attack radius 0, units built during the fight too; no roof defender).</summary>
        static Fight FortGunFight(string settings, string m16, int seed, int d, out ZObject fortOut)
        {
            ZWorld.FortDefenderDeviation = false;                                                      // no roof defender (put back by Check)
            var w = OzV11Kit.World(settings, m16, seed);
            var fort = w.Objects.First(o => o.IsFort && o.Owner == Team.Blue);
            void Hold() { foreach (var o in w.Objects) if (o.Owner == Team.Blue && o != fort && o.AttackRadius > 0) o.AttackRadius = 0; }
            Hold();
            var s = w.PlayableShuttle;
            int side = fort.Type == "fort_back" ? -1 : 1;                                             // fort_front: its rear arc north, so the front is south
            s.ShuttlePlace(fort.CenterX, fort.CenterY + side * (fort.HeightPix / 2 + d)); s.ShuttleState = ZShuttleState.Hover; s.ShuttleLift = 1;
            fortOut = fort;
            return Watch(w, s, FortGunLimitSec, fort.Id, Hold);
        }

        static T With<T>(bool on, Func<T> f)
        {
            bool was = ZWorld.ShuttleMoreHpOn;
            ZWorld.ShuttleMoreHpOn = on;
            try { return f(); } finally { ZWorld.ShuttleMoreHpOn = was; }
        }

        public static string Check(string settings, string m16, string m02, int seeds = Seeds, Action<string> progress = null)
        {
            var sb = new StringBuilder();
            bool was = ZWorld.ShuttlePlayableDeviation, wasOn = ZWorld.ShuttleOn, sw = ZWorld.ShuttleMoreHpOn, fd0 = ZWorld.FortDefenderDeviation; var team0 = ZWorld.ShuttleTeam;
            try
            {
                ZWorld.ShuttlePlayableDeviation = true; ZWorld.ShuttleOn = true; ZWorld.ShuttleTeam = Team.Red;
                var probe = ZWorld.Create(settings, m02, 1);
                int light = probe.S.Unit("light")?.MaxHealth ?? ZWorld.ShuttleBaseHp, want = (int)Math.Round(light * ZWorld.ShuttleHpFactor, MidpointRounding.AwayFromZero);
                sb.AppendLine($"K443 shuttle more hit points (DV131): the light tank {light} HP x {F(ZWorld.ShuttleHpFactor, "0.0#")} = {F(light * ZWorld.ShuttleHpFactor, "0.0")} -> {want} HP; {seeds} seed(s) a weapon");

                // ------------------------------------------------------------ A. level 16's own ship
                var a1 = With(true, () => OzV11Kit.World(settings, m16, 1)); var s1 = a1.PlayableShuttle;
                var a0 = With(false, () => OzV11Kit.World(settings, m16, 1)); var s0 = a0.PlayableShuttle;
                var lt1 = With(true, () => OzV11Kit.Place(a1, "light", 40 * 16, 40 * 16, Team.Red)); var lt0 = With(false, () => OzV11Kit.Place(a0, "light", 40 * 16, 40 * 16, Team.Red));
                int n1 = With(true, () => a1.ShuttleMaxHp), n0 = With(false, () => a0.ShuttleMaxHp);
                sb.AppendLine($"A. level 16 at map load: the ship #{s1?.Id} {s1?.Health}/{s1?.MaxHealth} HP with the switch on, #{s0?.Id} {s0?.Health}/{s0?.MaxHealth} HP off; a light tank {lt1.MaxHealth} / {lt0.MaxHealth} HP; the world's own number {n1} / {n0}");
                sb.AppendLine(OzV11Kit.Verdict(s1 != null && s0 != null && s1.MaxHealth == want && s1.Health == want && s0.MaxHealth == light && s0.Health == light && lt1.MaxHealth == light && lt0.MaxHealth == light
                                               && n1 == want && n0 == light && want == 4054 && light == 3378,
                    $"A level 16's ship starts with {want} of {want} HP (the light tank's {light} x 1.2), {light} with the switch off; a light tank keeps {light} either way"));
                progress?.Invoke("A done");

                // ------------------------------------------------------------ B. every shuttle the rules make
                var made = new List<(string how, ZObject s)>();
                {
                    var w = OzV11Kit.World(settings, m02, 1);
                    var fort = w.Objects.First(o => o.IsFort && o.Owner == Team.Red);
                    int k = 0;
                    foreach (var t in new[] { Team.Red, Team.Blue, Team.Neutral })
                        for (int r = 260; r <= 1400; r += 64)
                        {
                            double g = (k++ % 16) * Math.PI / 8;
                            var o = w.SpawnShuttle(fort.CenterX + (int)(Math.Cos(g) * r), fort.CenterY + (int)(Math.Sin(g) * r), t, -90f, out _);
                            if (o != null) { made.Add(("testing panel " + (t == Team.Neutral ? "empty" : t.ToString().ToLowerInvariant()), o)); break; }
                        }
                    var spots = new List<(int x, int y)>();
                    for (int r = 200; r <= 1400 && spots.Count < 3; r += 32)
                        for (int q = 0; q < 16 && spots.Count < 3; q++)
                        {
                            double g = q * Math.PI / 8 + 0.2;
                            var (x, y) = probe.ShuttleClamp(fort.CenterX + (int)(Math.Cos(g) * r), fort.CenterY + (int)(Math.Sin(g) * r));
                            x -= 8; y -= 8;
                            if (spots.Any(p => Math.Abs(p.x - x) < 160 && Math.Abs(p.y - y) < 160)) continue;
                            if (probe.ShuttleLandSpot(x, y, null, ZWorld.ShuttleDropHeadingDeg, out _, out _)) spots.Add((x, y));
                        }
                    if (spots.Count == 3)
                    {
                        var m = ZMapData.FromJson(m02);
                        string[] owners = { "null", "red", "blue" };
                        for (int i = 0; i < 3; i++) m.objects.Add(new ZMapData.Obj { x = spots[i].x / 16 - 1, y = spots[i].y / 16 - 1, owner = owners[i], kind = "vehicle:" + ZWorld.ShuttleType, health_percent = 100 });
                        var wm = ZWorld.Create(settings, UnityEngine.JsonUtility.ToJson(m), 1);
                        foreach (var o in wm.Objects.Where(o => o.IsShuttle)) made.Add(("map entry " + (o.Owner == Team.Neutral ? "null" : o.Owner.ToString().ToLowerInvariant()), o));
                    }
                }
                sb.AppendLine("B. every way a shuttle is made: " + string.Join("; ", made.Select(x => $"{x.how} #{x.s.Id} ({x.s.Owner}, pilots {x.s.Drivers.Count}) {x.s.Health}/{x.s.MaxHealth} HP")));
                bool sides = made.Select(x => x.s.Owner).Distinct().Count() == 3;
                sb.AppendLine(OzV11Kit.Verdict(made.Count == 6 && sides && made.All(x => x.s.MaxHealth == want && x.s.Health == want),
                    $"B the testing panel's red, blue and empty drops and the map entries for nobody, red and blue: all {want} of {want} HP ({made.Count} of 6 made): an enemy ship matches the player's"));
                progress?.Invoke("B done");

                // ------------------------------------------------------------ C. shots to shoot it down
                sb.AppendLine($"C. shots to shoot it down, switch off (the old {light} HP) -> on ({want} HP); the ship hovering at 12 m; per seed: shots fired at it / hits, the time it went down (or the HP left after the limit); the hits' hit points:");
                int cases = 0, same = 0, moreOrEqual = 0, more = 0, downOff = 0, downOn = 0; double hitsOff = 0, hitsOn = 0, shotsOff = 0, shotsOn = 0;
                var lines = new List<string>(); var digest = new List<ulong>();
                var table = new List<string>();
                void Compare(string name, Fight off, Fight on, string where)
                {
                    cases++;
                    bool sameStart;
                    if (off.down)
                    {
                        int n = off.Hits;
                        sameStart = on.Hits >= n;
                        for (int i = 0; i < n - 1 && sameStart; i++) sameStart = on.drops[i].hp == off.drops[i].hp && Math.Abs(on.drops[i].t - off.drops[i].t) < 1e-6;
                        if (sameStart) sameStart = Math.Abs(on.drops[n - 1].t - off.drops[n - 1].t) < 1e-6 && on.drops[n - 1].hp >= off.drops[n - 1].hp;
                    }
                    else
                    {
                        sameStart = on.Hits == off.Hits;
                        for (int i = 0; i < off.Hits && sameStart; i++) sameStart = on.drops[i].hp == off.drops[i].hp && Math.Abs(on.drops[i].t - off.drops[i].t) < 1e-6;
                    }
                    if (sameStart) same++;
                    if (off.down ? !on.down || on.Hits >= off.Hits : !on.down) moreOrEqual++;          // never fewer hits; never down where the old ship lived
                    if (off.down && (!on.down || on.Hits > off.Hits)) more++;
                    if (off.down) downOff++;
                    if (on.down) downOn++;
                    hitsOff += off.Hits; hitsOn += on.Hits; shotsOff += off.shots; shotsOn += on.shots;
                    digest.Add(off.Digest()); digest.Add(on.Digest());
                    string Cell(Fight x) => $"{x.shots}/{x.Hits} " + (x.down ? $"down {F(x.downAt, "0.0")} s" : $"up after {F(x.endAt, "0")} s, {x.hpLeft} HP left");
                    lines.Add($"   {name,-16} {where}: off {Cell(off)} [{off.Drops}] -> on {Cell(on)} [{on.Drops}]{(sameStart ? "" : "  (NOT the same fight until the old HP ran out)")}");
                }
                foreach (var type in Weapons)
                {
                    var u = probe.S.Unit(type);
                    int dmg = u?.Damage ?? 0;
                    var offs = new List<Fight>(); var ons = new List<Fight>();
                    for (int seed = 1; seed <= seeds; seed++)
                    {
                        var off = With(false, () => WeaponFight(settings, type, seed));
                        var on = With(true, () => WeaponFight(settings, type, seed));
                        Compare(type, off, on, "seed " + seed);
                        offs.Add(off); ons.Add(on);
                    }
                    table.Add(Row(type, dmg, u?.attack_damage_radius ?? 0, light, want, offs, ons));
                    progress?.Invoke("C " + type + " done");
                }
                {
                    var hw = probe.S.Unit("howitzer");
                    int dist = -1; ZObject fort = null;
                    foreach (int d in new[] { 100, 80, 120 })
                    {
                        var t = With(false, () => FortGunFight(settings, m16, 1, d, out fort));
                        if (t.shots > 0) { dist = d; break; }
                    }
                    var offs = new List<Fight>(); var ons = new List<Fight>();
                    if (dist > 0)
                        for (int seed = 1; seed <= seeds; seed++)
                        {
                            var off = With(false, () => FortGunFight(settings, m16, seed, dist, out fort));
                            var on = With(true, () => FortGunFight(settings, m16, seed, dist, out fort));
                            Compare("fort gun", off, on, $"seed {seed}, {dist} px off the blue fort's front wall");
                            offs.Add(off); ons.Add(on);
                        }
                    else lines.Add("   fort gun: NO SHOT at 100 / 80 / 120 px off the blue fort's front wall");
                    table.Add(Row("fort gun", hw?.Damage ?? 0, (hw?.attack_damage_radius ?? 0) + ZObject.FortGunSplashBonusPx, light, want, offs, ons));
                    progress?.Invoke("C fort gun done");
                }
                foreach (var l in lines) sb.AppendLine(l);
                sb.AppendLine("   per weapon (damage of one hit at the blast's centre, splash radius; direct hits at full damage needed off -> on; over the seeds: mean hits to shoot it down off -> on, shot down, mean time):");
                foreach (var l in table) sb.AppendLine(l);
                sb.AppendLine($"   all: {cases} fights a side; shot down {downOff} (off) / {downOn} (on); hits {F(hitsOff / Math.Max(1, cases))} -> {F(hitsOn / Math.Max(1, cases))} a fight, shots {F(shotsOff / Math.Max(1, cases))} -> {F(shotsOn / Math.Max(1, cases))}; " +
                              $"the same fight until the old HP ran out in {same} of {cases}; one hit or more needed (or still up) in {more} of {downOff} fights the old ship lost");
                sb.AppendLine(OzV11Kit.Verdict(cases > 0 && same == cases, $"C1 every fight is the same, hit for hit, until the old {light} HP would have run out (the rules read no health before 0): {same} of {cases}"));
                sb.AppendLine(OzV11Kit.Verdict(cases > 0 && moreOrEqual == cases && downOn <= downOff, $"C2 with {want} HP it never takes fewer hits to shoot down, and it is never shot down where the old ship lived ({moreOrEqual} of {cases}; down {downOff} -> {downOn})"));
                sb.AppendLine(OzV11Kit.Verdict(more > 0, $"C3 the extra hit points cost the attacker: {more} of the {downOff} fights the old ship lost need one hit or more (or it is still up at the limit)"));

                // ------------------------------------------------------------ D. the repair station
                var rOff = With(false, () => Repair(settings, m16)); var rOn = With(true, () => Repair(settings, m16));
                sb.AppendLine($"D. level 16, repair station #{rOn.station}: R from half health: off {rOff.text}; on {rOn.text}");
                bool padSame = rOff.ok && rOn.ok && Math.Abs(rOn.padSec - rOff.padSec) <= ZWorld.RepairPadTick + 1e-6;
                bool perTick = rOff.ok && rOn.ok && Math.Abs(rOn.step - Math.Ceiling((rOn.max - rOn.from) * ZWorld.RepairPadTick / ZWorld.RepairPadSeconds)) < 0.5 && rOn.step > rOff.step;
                sb.AppendLine(OzV11Kit.Verdict(padSame && perTick && rOn.endHp == want && rOff.endHp == light && rOn.back && rOff.back,
                    $"D the repair station heals it to its full {want} HP in the same pad time as before ({F(rOff.padSec)} -> {F(rOn.padSec)} s, pad {F(ZWorld.RepairPadSeconds, "0.0")} s), {rOff.step} -> {rOn.step} HP a pad tick, then it flies back"));

                // ------------------------------------------------------------ E. the damaged look reads a share
                var looks = new List<string>(); bool shares = true;
                foreach (bool on in new[] { false, true })
                {
                    var w = With(on, () => OzV11Kit.World(settings, OzV11Kit.Plain(40, 40), 1));
                    var s = With(on, () => w.CreatePlayableShuttle(320, 320, Team.Red));
                    int mx = s.MaxHealth;
                    s.SetHealth(1500); bool d1500 = s.ShowDamaged, p1500 = s.ShowPartiallyDamaged;
                    s.SetHealth(mx * 4 / 10 - 1); bool dUnder = s.ShowDamaged;
                    s.SetHealth(mx * 4 / 10 + 1); bool pOver = s.ShowPartiallyDamaged && !s.ShowDamaged;
                    s.SetHealth(mx * 7 / 10 - 1); bool pUnder70 = s.ShowPartiallyDamaged;
                    s.SetHealth(mx * 7 / 10 + 1); bool none70 = !s.ShowPartiallyDamaged && !s.ShowDamaged;
                    looks.Add($"{(on ? "on" : "off")} (max {mx}): at 1500 HP ({F(1500.0 / mx * 100, "0")} %) damaged {d1500}, partly {p1500}; 40 % = {mx * 4 / 10} HP, 70 % = {mx * 7 / 10} HP");
                    shares &= dUnder && pOver && pUnder70 && none70 && d1500 == (1500.0 / mx < 0.4);
                    if (on) shares &= d1500; else shares &= !d1500 && p1500;
                }
                sb.AppendLine("E. the damaged look (ZObject.ShowDamaged / ShowPartiallyDamaged): " + string.Join("; ", looks));
                sb.AppendLine(OzV11Kit.Verdict(shares, "E the damaged look reads a share of the new maximum (under 40 %, 40-70 %): 1500 HP is 'damaged' on the 4054 ship, 'partly' on the old 3378 one"));

                // ------------------------------------------------------------ F. determinism, the switch
                var again = With(true, () => WeaponFight(settings, "tough", 1)); var again2 = With(true, () => WeaponFight(settings, "tough", 1));
                var once = digest.Count > 1 ? digest[1] : 0UL;
                sb.AppendLine($"F. the Tough's fight, seed 1, switch on, three times: {once:x16} / {again.Digest():x16} / {again2.Digest():x16}; the switch after the check: {(ZWorld.ShuttleMoreHpOn ? "on" : "OFF")}");
                sb.AppendLine(OzV11Kit.Verdict(again.Digest() == again2.Digest() && again.Digest() == once && ZWorld.ShuttleMoreHpOn == sw, "F deterministic (the same seed, the same fight) and the switch back as it was"));
            }
            catch (Exception ex) { sb.AppendLine("FAIL  the check threw " + ex.GetType().Name + ": " + ex.Message + "\n" + ex.StackTrace); }
            finally { ZWorld.ShuttlePlayableDeviation = was; ZWorld.ShuttleOn = wasOn; ZWorld.ShuttleTeam = team0; ZWorld.ShuttleMoreHpOn = sw; ZWorld.FortDefenderDeviation = fd0; }
            return sb.ToString();
        }

        static string Row(string name, int dmg, int radius, int light, int want, List<Fight> offs, List<Fight> ons)
        {
            string Need(int hp) => dmg > 0 ? ((hp + dmg - 1) / dmg).ToString() : "-";
            string Mean(List<Fight> l, bool hits) { var d = l.Where(x => x.down).ToList(); return d.Count == 0 ? "-" : F(d.Average(x => hits ? x.Hits : x.downAt), hits ? "0.00" : "0.0"); }
            return $"   {name,-16} {dmg} HP, {radius} px; direct hits {Need(light)} -> {Need(want)}; mean hits {Mean(offs, true)} -> {Mean(ons, true)}; down {offs.Count(x => x.down)} -> {ons.Count(x => x.down)} of {offs.Count}; mean time {Mean(offs, false)} -> {Mean(ons, false)} s";
        }

        sealed class RepairRun { public bool ok, back; public int station = -1, from, max, step, endHp; public double padSec; public string text = ""; }

        /// <summary>D: level 16, the ship at half health, R to a red repair station (its zone given to red when needed): the pad's time and
        /// its heal a tick, the health at the end, the flight back.</summary>
        static RepairRun Repair(string settings, string m16)
        {
            var r = new RepairRun();
            var w = OzV11Kit.World(settings, m16, 1);
            var s = w.PlayableShuttle;
            ZObject st = null;
            foreach (var f in w.Flags) foreach (var b in f.LinkedObjects) if (b.Type == "repair" && st == null) { st = b; if (b.Owner != Team.Red) w.AwardZone(f, Team.Red); }
            if (st == null || s == null) { r.text = "no repair station / no ship"; return r; }
            r.station = st.Id; r.max = s.MaxHealth;
            s.SetHealth(s.MaxHealth / 2); r.from = s.Health;
            bool sent = w.SendWaypoints(Team.Red, s, new[] { new ZWaypoint { mode = WpMode.UnitRepair, refId = st.Id, x = st.CenterX, y = st.CenterY, playerGiven = true } });
            int x0 = s.CenterX, y0 = s.CenterY;
            Run(w, 120, () => s.ShuttleOnRepairPad);
            if (!s.ShuttleOnRepairPad) { r.text = $"order {(sent ? "taken" : "REFUSED")}, never on the pad"; return r; }
            double tIn = w.Time; int hpIn = s.Health, last = s.Health;
            double tFull = -1;
            while (w.Time < tIn + 30)
            {
                w.Tick(Tick); w.Events.Clear();
                if (s.Health > last) { if (r.step == 0) r.step = s.Health - last; last = s.Health; }
                if (s.Health >= s.MaxHealth && tFull < 0) tFull = w.Time;
                if (!s.ShuttleOnRepairPad) break;
            }
            r.padSec = tFull < 0 ? -1 : tFull - tIn;
            r.endHp = s.Health;
            bool left = !s.ShuttleOnRepairPad;
            Run(w, 90, () => s.ShuttleState == ZShuttleState.Landed && !s.ShuttleOnRepairPad && Math.Abs(s.CenterX - x0) + Math.Abs(s.CenterY - y0) < 64);
            r.back = left && s.ShuttleState == ZShuttleState.Landed && Math.Abs(s.CenterX - x0) + Math.Abs(s.CenterY - y0) < 64;
            r.ok = sent && tFull > 0;
            r.text = $"order {(sent ? "taken" : "REFUSED")}, on the pad at {hpIn}/{r.max} HP, full {r.endHp}/{r.max} after {F(r.padSec)} s, first pad tick +{r.step} HP; back where it stood {(r.back ? "yes" : "NO")} (at {P(s.CenterX, s.CenterY)}, was {P(x0, y0)})";
            return r;
        }
    }
}
