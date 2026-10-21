using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// DV133 hero squads stronger again (Sim/ZHeroSquad.K442.cs: ALAN &amp; BRAD +15 % hit points, +15 % strong shot, +15 % thrown grenades on
    /// top of DV112), counted headless (ZWorld.Create + Tick, no scene, nothing saved). "Before" = the game's numbers today (DV112 on, the
    /// DV133 switch off), "after" = DV112 on and DV133 on. Every wanted number is worked out here on its own (decimal maths, rounded half up),
    /// not by the rule's code.
    ///  * Check: the numbers at the start (hit points x 1.15; range, shot pause and self-repair share unchanged; DV133 does nothing while DV112
    ///    is off); one forced hit on a light tank, a grunt and a Tough (x 1.15); Alan's real thrown grenades (x 1.15 on a direct and an area
    ///    hit, with the game's grenade rule DV104 on and off; the radius and the pause between two throws unchanged; a grunt's grenade
    ///    unchanged) and the blast on a hull; the self-repair (still 20 % of full health a minute: more hit points a minute, half to full in
    ///    the same 150 s); the numbers out of an own troop carrier; the same duel twice; and the duels below at <see cref="CheckRuns"/> seeds.
    ///  * Duels (open ground 11 tiles apart, the computer's attack order, the game's grenade rule DV104 on): the pair against one grunt squad
    ///    and one Tough squad: "kill" (only the pair fires: its time to kill them), "survive" (only they fire: their time to kill the pair,
    ///    self-repair on as in the game), "fight" (both fire). Then the fight table of DV112's check (two and three grunt squads, a gun, a
    ///    light tank) before / after.
    /// Menus OpenZ/V1.1/Hero Squad: K442 Check (+15 % health and damage) and OpenZ/V1.1/Hero Squad: K442 Duels (before / after, 100 a row);
    /// the Play watch View/OzHeroPlusWatch.cs (menu OpenZ/V1.1/Hero Squad: K442 Watch (Level 16, Play)).
    /// </summary>
    public static class OzV11HeroK442Count
    {
        public static int CheckRuns = 10;
        public static double MaxSeconds = 240;

        /// <summary>v x f rounded half up, in decimal maths (1.15 is exact there).</summary>
        static int Times(int v, decimal f) => (int)Math.Round(v * f, MidpointRounding.AwayFromZero);

        static ZWorld Ground(string settings, int seed)
        {
            var w = OzV11Kit.World(settings, OzV11Kit.Plain(48, 40), seed);
            OzV11Kit.Dummy(w, 46 * 16 + 8, 38 * 16 + 8);
            return w;
        }

        static void Ticks(ZWorld w, double secs, Func<bool> until = null, Action each = null)
        {
            int n = (int)Math.Round(secs / ZConst.TICK);
            for (int i = 0; i < n; i++) { w.Tick(ZConst.TICK); w.Events.Clear(); each?.Invoke(); if (until != null && until()) break; }
        }

        static ZObject Pair(ZWorld w, int tx, int ty) => w.CreateHeroSquad(ZHeroSquads.AlanBrad, Team.Red, new[] { tx, ty, tx, ty + 2 });

        /// <summary>Runs f with DV112 on and DV133 as asked (both put back after).</summary>
        static T With<T>(bool plus, Func<T> f)
        {
            using (ZWorld.HeroBoostOn())
            using (plus ? ZWorld.HeroPlusOn() : ZWorld.HeroPlusOff())
                return f();
        }

        /// <summary>Runs f with the game's grenade rule DV104 on or off, then puts it back.</summary>
        static T WithGrenadeRule<T>(bool on, Func<T> f)
        {
            bool was = ZWorld.GrenadeBoostDeviation;
            try { ZWorld.GrenadeBoostDeviation = on; return f(); }
            finally { ZWorld.GrenadeBoostDeviation = was; }
        }

        /// <summary>One forced hit (hit chance 1, no snipe) of a red <paramref name="hero"/> pair's leader (else a lone grunt) on a blue
        /// crewed light tank, a grunt or a Tough that cannot die nor shoot: the HP it removed.</summary>
        static int HitOn(string settings, bool hero, string what)
        {
            var w = OzV11Kit.World(settings, OzV11Kit.Plain(32, 32), 5);
            OzV11Kit.Dummy(w, 30 * 16 + 8, 30 * 16 + 8);
            var s = hero ? w.CreateHeroSquad(ZHeroSquads.AlanBrad, Team.Red, new[] { 8, 8, 8, 6 }) : w.CreateRobotGroup("grunt", 8 * 16, 8 * 16, Team.Red, 1);
            ZObject t = what == "light" ? w.CreateObject("vehicle", "light", 13 * 16, 8 * 16, Team.Blue) : w.CreateRobotGroup(what, 13 * 16, 8 * 16, Team.Blue, 1);
            t.Damage = 0; t.MoveSpeed = 0; t.MaxHealth = t.Health = 1000000;
            foreach (var m in OzV11Kit.Squad(s)) { m.DamageChance = 1.0; m.SnipeChance = 0; }
            foreach (var m in s.Minions.ToList()) { m.MoveSpeed = 0; m.Damage = 0; }
            OzV11Kit.CpuAttack(w, s, t);
            int before = t.Health;
            for (int i = 0; i < 600 && t.Health == before; i++) { w.Tick(ZConst.TICK); w.Events.Clear(); }
            return before - t.Health;
        }

        /// <summary>Alan's first two grenades (else a grunt squad's, given five) at a blue crewed light tank that cannot die nor shoot: the
        /// first one's HP on a direct hit and its own area hit (0 = the game's), its radius, the game time between the two throws, the world's
        /// grenade numbers beside it and the count of boosted hero grenades.</summary>
        struct ThrowRow { public int damage, area, radius, worldDamage, worldArea, boosted; public double gap; }

        static ThrowRow Throw(string settings, bool hero)
        {
            var w = OzV11Kit.World(settings, OzV11Kit.Plain(32, 32), 6);
            OzV11Kit.Dummy(w, 30 * 16 + 8, 30 * 16 + 8);
            var s = hero ? w.CreateHeroSquad(ZHeroSquads.AlanBrad, Team.Red, new[] { 8, 8, 8, 6 }) : w.CreateRobotGroup("grunt", 8 * 16, 8 * 16, Team.Red, 1);
            if (!hero) s.Grenades = 5;
            var thrower = hero ? s.Minions.FirstOrDefault(m => m.HeroMemberName == "alan") : s;
            var t = w.CreateObject("vehicle", "light", 13 * 16, 8 * 16, Team.Blue);
            t.Damage = 0; t.MaxHealth = t.Health = 1000000;
            OzV11Kit.CpuAttack(w, s, t);
            ZMissile g1 = null, g2 = null; double t1 = -1, t2 = -1;
            for (int i = 0; i < 6000 && g2 == null && thrower != null; i++)
            {
                w.Tick(ZConst.TICK); w.Events.Clear();
                foreach (var m in w.QueuedMissiles.Concat(w.PendingMissiles))
                {
                    if (!m.grenade || m.attackerId != thrower.Id) continue;
                    if (g1 == null) { g1 = m; t1 = w.Time; }
                    else if (m != g1 && g2 == null) { g2 = m; t2 = w.Time; }
                }
            }
            var r = new ThrowRow { damage = -1, area = -1, radius = -1, gap = -1, worldDamage = w.ThrownGrenadeDamage, worldArea = w.ThrownGrenadeAreaDamage, boosted = w.HeroGrenadesThrown };
            if (g1 != null) { r.damage = g1.damage; r.area = g1.areaDamage; r.radius = g1.radius; }
            if (g2 != null) r.gap = t2 - t1;
            return r;
        }

        /// <summary>The HP one red grenade blast (damage, own area hit, radius) at <paramref name="dx"/> px right of a blue light tank's
        /// centre takes from it (0 px: on its body, a direct hit; 20 px: beside its 32 px hull, an area hit).</summary>
        static int BlastOn(string settings, int damage, int area, int radius, int dx)
        {
            var w = OzV11Kit.World(settings, OzV11Kit.Plain(32, 32), 7);
            OzV11Kit.Dummy(w, 30 * 16 + 8, 30 * 16 + 8);
            var t = w.CreateObject("vehicle", "light", 13 * 16, 8 * 16, Team.Blue);
            t.Damage = 0; t.MaxHealth = t.Health = 1000000;
            w.AddMissileForTest(new ZMissile { team = Team.Red, attackerId = -1, targetId = t.Id, x = t.CenterX + dx, y = t.CenterY, damage = damage, areaDamage = area, radius = radius, grenade = true, explodeTime = w.Time + 0.05 });
            int before = t.Health;
            for (int i = 0; i < 30; i++) { w.Tick(ZConst.TICK); w.Events.Clear(); }
            return before - t.Health;
        }

        // ------------------------------------------------------------------ duels
        public struct DuelRow { public int won, lost, draw, n; public double secs, pairHpLeft, oppTaken; }

        public static readonly string[] DuelOpponents = { "grunt", "tough" };
        public static readonly string[] DuelModes = { "kill", "survive", "fight" };

        static string Opp(string type) => type == "tough" ? "a Tough squad" : "a grunt squad";

        /// <summary>One duel: the red pair against one blue squad of <paramref name="opp"/>. mode "kill": only the pair fires (the squad
        /// stands, unarmed); "survive": only the squad fires (the pair stands, unarmed); "fight": both fire. Returns 1 the pair won (the squad is
        /// gone), 2 the pair is gone, 0 neither within <see cref="MaxSeconds"/>; secs = the game time until then.</summary>
        public static int Duel(string settings, string opp, string mode, int seed, out double secs, out double pairHpLeft, out double oppTaken)
        {
            var w = OzV11Kit.World(settings, OzV11Kit.Plain(64, 48), seed);
            OzV11Kit.Dummy(w, 60 * 16 + 8, 44 * 16 + 8);
            var jit = new Random(seed * 31 + 7);
            int J() => jit.Next(-1, 2);
            var pair = w.CreateHeroSquad(ZHeroSquads.AlanBrad, Team.Red, new[] { 18 + J(), 24 + J(), 18 + J(), 26 + J() });
            var squad = w.CreateRobotGroup(opp, (29 + J()) * 16, (24 + J()) * 16, Team.Blue);
            var mine = OzV11Kit.Squad(pair).ToList(); var theirs = OzV11Kit.Squad(squad).ToList();
            bool pairFires = mode != "survive", theyFire = mode != "kill";
            if (!theyFire) foreach (var t in theirs) { t.Damage = 0; t.MoveSpeed = 0; t.Grenades = 0; }
            if (!pairFires) foreach (var m in mine) { m.Damage = 0; m.MoveSpeed = 0; }
            double hp0 = mine.Sum(o => (double)o.MaxHealth), ohp0 = theirs.Sum(o => (double)o.MaxHealth);
            for (int i = 0; i < 10; i++) { w.Tick(ZConst.TICK); w.Events.Clear(); }
            if (pairFires) OzV11Kit.CpuAttack(w, pair, squad);
            if (theyFire) OzV11Kit.CpuAttack(w, squad, pair);
            double t0 = w.Time; int result = 0;
            while (w.Time - t0 < MaxSeconds)
            {
                w.Tick(ZConst.TICK); w.Events.Clear();
                bool pairGone = !mine.Any(OzV11Kit.Alive), oppGone = !theirs.Any(OzV11Kit.Alive);
                if (pairGone || oppGone) { result = pairGone && oppGone ? 0 : pairGone ? 2 : 1; break; }
                if (((int)Math.Round((w.Time - t0) * 100)) % 200 == 0)
                {   // every 2 s: a side that fires and stands idle with enemies left is sent at the nearest one again (the next click)
                    foreach (var (us, them, fires) in new[] { (mine, theirs, pairFires), (theirs, mine, theyFire) })
                    {
                        if (!fires) continue;
                        var u = us.FirstOrDefault(o => OzV11Kit.Alive(o) && !o.IsMinion);
                        if (u == null || !u.CanMove || u.Waypoints.Count > 0 || u.AttackObject != null) continue;
                        var t = them.Where(OzV11Kit.Alive).OrderBy(x => OzV11Kit.Dist(u, x)).FirstOrDefault();
                        if (t != null) OzV11Kit.CpuAttack(w, u, t.Leader ?? t);
                    }
                }
            }
            secs = w.Time - t0;
            pairHpLeft = mine.Where(OzV11Kit.Alive).Sum(o => (double)o.Health) / Math.Max(1, hp0);
            oppTaken = 1.0 - theirs.Where(OzV11Kit.Alive).Sum(o => (double)Math.Max(0, o.Health)) / Math.Max(1, ohp0);
            return result;
        }

        public static DuelRow Duels(string settings, string opp, string mode, int runs, bool plus)
        {
            var r = new DuelRow();
            int seedBase = 4420001 + (opp == "tough" ? 100003 : 0) + Array.IndexOf(DuelModes, mode) * 10007;
            for (int i = 0; i < runs; i++)
            {
                int res = With(plus, () => WithGrenadeRule(true, () => { int x = Duel(settings, opp, mode, seedBase + i * 7919, out double s, out double hp, out double ot); r.secs += s; r.pairHpLeft += hp; r.oppTaken += ot; return x; }));
                if (res == 1) r.won++; else if (res == 2) r.lost++; else r.draw++;
                r.n++;
            }
            if (r.n > 0) { r.secs /= r.n; r.pairHpLeft /= r.n; r.oppTaken /= r.n; }
            return r;
        }

        static string DuelCell(DuelRow r) => $"{r.won}/{r.lost}/{r.draw}, {r.secs:0.0} s, pair health left {r.pairHpLeft * 100:0} %, taken from them {r.oppTaken * 100:0} %";

        // ------------------------------------------------------------------ the check
        public static string Check(string settings)
        {
            var sb = new StringBuilder();
            sb.AppendLine("ALAN & BRAD stronger again (DV133, item 444): hit points x 1.15, strong shot x 1.15, thrown grenades x 1.15 on top of DV112; range, shot pause, self-repair share unchanged");
            var g = OzV11Kit.World(settings, OzV11Kit.Plain(8, 8), 1).S.Unit("grunt");
            var k = ZHeroSquads.Get(ZHeroSquads.AlanBrad);
            int hp108 = g.MaxHealth * k.HealthMul, hp112 = Times(hp108, 1.20m), hpNew = Times(hp112, 1.15m);
            int brad108 = (int)Math.Round(g.attack_radius * ZHeroRange.MulOf(k, 0)), alan108 = (int)Math.Round(g.attack_radius * ZHeroRange.MulOf(k, 1));
            int brad112 = Times(brad108, 1.15m), alan112 = Times(alan108, 1.15m);
            double pause = g.attack_speed * k.CycleMul;
            // 1 the numbers at the start: after, before, and DV112 off (DV133 then does nothing)
            {
                ZObject brad, alan, grunt, b0, a0, b8, a8;
                int regenOn, regenOff;
                using (ZWorld.HeroBoostOn()) using (ZWorld.HeroPlusOn())
                { var w = Ground(settings, 81); brad = Pair(w, 10, 10); alan = brad.Minions.FirstOrDefault(); grunt = w.CreateRobotGroup("grunt", 10 * 16, 20 * 16, Team.Red); regenOn = ZHeroBoost.RegenPct(k); }
                using (ZWorld.HeroBoostOn()) using (ZWorld.HeroPlusOff())
                { var w = Ground(settings, 81); b0 = Pair(w, 10, 10); a0 = b0.Minions.FirstOrDefault(); regenOff = ZHeroBoost.RegenPct(k); }
                using (ZWorld.HeroBoostOff()) using (ZWorld.HeroPlusOn())
                { var w = Ground(settings, 81); b8 = Pair(w, 10, 10); a8 = b8.Minions.FirstOrDefault(); }
                bool after = alan != null && brad.MaxHealth == hpNew && alan.MaxHealth == hpNew && brad.Health == hpNew && alan.Health == hpNew
                             && brad.AttackRadius == brad112 && alan.AttackRadius == alan112 && Math.Abs(brad.AttackSpeed - pause) < 1e-9 && Math.Abs(alan.AttackSpeed - pause) < 1e-9
                             && grunt.MaxHealth == g.MaxHealth && grunt.AttackRadius == g.attack_radius && regenOn == 20;
                bool before = a0 != null && b0.MaxHealth == hp112 && a0.MaxHealth == hp112 && b0.AttackRadius == brad112 && a0.AttackRadius == alan112 && regenOff == 20;
                bool off = a8 != null && b8.MaxHealth == hp108 && a8.MaxHealth == hp108 && b8.AttackRadius == brad108 && a8.AttackRadius == alan108;
                sb.AppendLine(OzV11Kit.Verdict(after && before && off,
                    $"at the start: hit points Brad {brad.MaxHealth}, Alan {alan?.MaxHealth} (wanted {hpNew} = 1.15 x {hp112}; before {b0.MaxHealth} / {a0?.MaxHealth}; a grunt {grunt.MaxHealth}); " +
                    $"range Brad {brad.AttackRadius} px, Alan {alan?.AttackRadius} px (unchanged: {brad112} / {alan112}); shot pause {brad.AttackSpeed:0.000} s (unchanged {pause:0.000}); self-repair {regenOn} % of full health a minute (before {regenOff} %); " +
                    $"DV112 off: {b8.MaxHealth} / {a8?.MaxHealth} HP, {b8.AttackRadius} / {a8?.AttackRadius} px (DV108's numbers: DV133 acts only on top of DV112)"));
            }
            // 2 the strong shot, one forced hit on a light tank, a grunt and a Tough
            {
                var rows = new List<string>(); bool ok = true;
                foreach (var what in new[] { "light", "grunt", "tough" })
                {
                    int grunt = HitOn(settings, false, what);
                    int on = With(true, () => HitOn(settings, true, what)), before = With(false, () => HitOn(settings, true, what));
                    int dv108; using (ZWorld.HeroBoostOff()) using (ZWorld.HeroPlusOn()) dv108 = HitOn(settings, true, what);
                    bool good = grunt > 0 && dv108 == grunt * k.DamageMul && before == Times(dv108, 1.20m) && on == Times(before, 1.15m);
                    ok &= good;
                    rows.Add($"on {(what == "light" ? "a light tank" : what == "grunt" ? "a grunt" : "a Tough")} {on} HP (before {before}, wanted {Times(before, 1.15m)}; DV108 {dv108}; a grunt's own {grunt})");
                }
                sb.AppendLine(OzV11Kit.Verdict(ok, "strong shot, one forced hit: " + string.Join("; ", rows)));
            }
            // 3 their thrown grenades x 1.15 (Alan's real throws), the radius and the pause between throws unchanged, a grunt's unchanged
            {
                var on = WithGrenadeRule(true, () => With(true, () => Throw(settings, true)));
                var before = WithGrenadeRule(true, () => With(false, () => Throw(settings, true)));
                var grunt = WithGrenadeRule(true, () => With(true, () => Throw(settings, false)));
                var on0 = WithGrenadeRule(false, () => With(true, () => Throw(settings, true)));
                var before0 = WithGrenadeRule(false, () => With(false, () => Throw(settings, true)));
                bool thrown = on.damage > 0 && before.damage > 0 && grunt.damage > 0 && on0.damage > 0 && before0.damage > 0 && on.gap > 0 && before.gap > 0;
                bool ok = thrown && before.damage == Times(on.worldDamage, 1.20m) && before.area == Times(on.worldArea, 1.20m)
                          && on.damage == Times(before.damage, 1.15m) && on.area == Times(before.area, 1.15m)
                          && on0.damage == Times(before0.damage, 1.15m) && before0.damage == Times(on0.worldDamage, 1.20m)
                          && grunt.damage == grunt.worldDamage && grunt.area == 0 && grunt.boosted == 0 && on.boosted >= 1
                          && on.radius == before.radius && on.radius == grunt.radius && on0.radius == before0.radius && Math.Abs(on.gap - before.gap) < 1e-6;
                sb.AppendLine(OzV11Kit.Verdict(ok, $"thrown grenades, DV104 on (the game's rule): Alan's carries {on.damage} HP on a direct hit and {on.area} on an area hit (before {before.damage} / {before.area}, wanted {Times(before.damage, 1.15m)} / {Times(before.area, 1.15m)}; the game's grenade {on.worldDamage} / {on.worldArea}); " +
                                                   $"DV104 off: {on0.damage} (before {before0.damage}, wanted {Times(before0.damage, 1.15m)}); radius {on.radius} px (before {before.radius}); {on.gap:0.00} s between his first two throws (before {before.gap:0.00} s); a grunt's grenade {grunt.damage} (the game's {grunt.worldDamage}){(thrown ? "" : "; A THROW WAS MISSING")}"));
                int rd = on.radius > 0 ? on.radius : 36;
                float ratio = 20f / rd;
                var hits = WithGrenadeRule(true, () => (BlastOn(settings, on.damage, on.area, rd, 0), BlastOn(settings, on.damage, on.area, rd, 20), BlastOn(settings, before.damage, before.area, rd, 0), BlastOn(settings, before.damage, before.area, rd, 20)));
                int wantArea = (int)(on.area * (1.0 - ratio)), beforeArea = (int)(before.area * (1.0 - ratio));
                bool blast = hits.Item1 == on.damage && hits.Item2 == wantArea && hits.Item3 == before.damage && hits.Item4 == beforeArea;
                sb.AppendLine(OzV11Kit.Verdict(blast, $"its blast on a light tank's hull (DV104 on): on the hull {hits.Item1} HP (wanted {on.damage}; before {hits.Item3}), 20 px beside its centre {hits.Item2} HP (wanted {wantArea}; before {hits.Item4})"));
            }
            // 4 the self-repair: still 20 % of full health a minute, of the bigger full health
            {
                int Minute(bool plus, out int full, out double toFull, out int bigStep, out int above, out int gruntGain)
                {
                    using (ZWorld.HeroBoostOn()) using (plus ? ZWorld.HeroPlusOn() : ZWorld.HeroPlusOff())
                    {
                        var w = Ground(settings, 82);
                        var brad = Pair(w, 10, 10); var alan = brad.Minions.First();
                        var gr = w.CreateRobotGroup("grunt", 10 * 16, 24 * 16, Team.Red);
                        foreach (var o in new[] { brad, alan, gr }) o.SetHealth(o.MaxHealth / 2);
                        int b0 = brad.Health, g0 = gr.Health, last = brad.Health, big = 0, ab = 0; double t0 = w.Time, f = -1;
                        void Each()
                        {
                            int d = brad.Health - last; last = brad.Health;
                            if (d > big) big = d;
                            if (brad.Health > brad.MaxHealth || alan.Health > alan.MaxHealth) ab++;
                            if (f < 0 && brad.Health >= brad.MaxHealth) f = w.Time - t0;
                        }
                        Ticks(w, 60, null, Each);
                        int gain = brad.Health - b0;
                        Ticks(w, 100, null, Each);
                        full = brad.MaxHealth; toFull = f; bigStep = big; above = ab; gruntGain = gr.Health - g0;
                        return gain;
                    }
                }
                int gOn = Minute(true, out int fOn, out double tOn, out int sOn, out int aOn, out int grOn);
                int gOff = Minute(false, out int fOff, out double tOff, out int sOff, out int aOff, out int grOff);
                int wantOn = (int)((long)hpNew * 20 / 100), wantOff = (int)((long)hp112 * 20 / 100);
                double halfToFull = (hpNew - hpNew / 2) / (hpNew * 0.20 / 60.0);
                bool ok = fOn == hpNew && gOn == wantOn && gOff == wantOff && sOn == 1 && aOn == 0 && grOn == 0 && Math.Abs(tOn - halfToFull) < 0.15 && Math.Abs(tOn - tOff) < 0.15;
                sb.AppendLine(OzV11Kit.Verdict(ok, $"self-repair from half health: +{gOn} HP in 60 s (wanted {wantOn} = 20 % of {hpNew}; before +{gOff} of {fOff}); full after {tOn:0.00} s (before {tOff:0.00} s, wanted {halfToFull:0.00}); at most {sOn} HP a tick, above full {aOn} times; a grunt squad beside them +{grOn}"));
            }
            // 5 an own troop carrier: inside the crew list keeps their hit points; out again, the bigger numbers
            {
                using (ZWorld.HeroBoostOn()) using (ZWorld.HeroPlusOn())
                {
                    var w = Ground(settings, 84);
                    var brad = Pair(w, 10, 10); var alan = brad.Minions.First();
                    foreach (var o in new[] { brad, alan }) o.SetHealth(o.MaxHealth / 2);
                    var v = w.CreateObject("vehicle", "apc", 13 * 16, 10 * 16, Team.Red);
                    OzV11Kit.PlayerEnter(w, brad, v);
                    Ticks(w, 30, () => v.Passengers.Count >= 2);
                    string stored = string.Join(" / ", v.Passengers);
                    var keep = v.Passengers.ToList();
                    Ticks(w, 30);
                    bool kept = keep.Count == 2 && v.Passengers.SequenceEqual(keep) && v.HeroesAboard == 2;
                    bool ej = w.Eject(Team.Red, v); Ticks(w, 0.1);
                    var b2 = w.Objects.FirstOrDefault(o => o.IsHero && o.HeroMemberName == "brad" && OzV11Kit.Alive(o));
                    var a2 = w.Objects.FirstOrDefault(o => o.IsHero && o.HeroMemberName == "alan" && OzV11Kit.Alive(o));
                    bool outOk = ej && b2 != null && a2 != null && b2.MaxHealth == hpNew && a2.MaxHealth == hpNew && b2.AttackRadius == brad112 && a2.AttackRadius == alan112
                                 && b2.Health >= keep[0] && b2.Health <= keep[0] + 2 && a2.Health >= keep[1] && a2.Health <= keep[1] + 2;
                    sb.AppendLine(OzV11Kit.Verdict(kept && outOk, $"own troop carrier: stored {stored} HP (kept 30 s, no repair inside); out: " +
                                                                 (b2 != null && a2 != null ? $"Brad {b2.Health}/{b2.MaxHealth} {b2.AttackRadius} px, Alan {a2.Health}/{a2.MaxHealth} {a2.AttackRadius} px 0.1 s later (wanted full {hpNew}, the stored hit points + at most 2 of self-repair, range unchanged)" : "NOT both out")));
                }
            }
            // 6 the same duel twice: the same numbers (no random draw of its own, the game clock only)
            {
                double s1 = 0, h1 = 0, o1 = 0, s2 = 0, h2 = 0, o2 = 0;
                int r1 = With(true, () => WithGrenadeRule(true, () => Duel(settings, "grunt", "fight", 4429999, out s1, out h1, out o1)));
                int r2 = With(true, () => WithGrenadeRule(true, () => Duel(settings, "grunt", "fight", 4429999, out s2, out h2, out o2)));
                sb.AppendLine(OzV11Kit.Verdict(r1 == r2 && s1 == s2 && h1 == h2 && o1 == o2, $"the same duel twice (against a grunt squad, one seed): {Res(r1)} after {s1:0.00} s with {h1 * 100:0.0} % health left, again {Res(r2)} after {s2:0.00} s with {h2 * 100:0.0} %"));
            }
            // 7 the duels, before / after
            sb.Append(DuelTable(settings, CheckRuns, true));
            return sb.ToString();
        }

        static string Res(int r) => r == 1 ? "won" : r == 2 ? "lost" : "a draw";

        /// <summary>The duel rows before / after (<paramref name="runs"/> a row) and, with <paramref name="verdicts"/>, the verdicts on them.</summary>
        public static string DuelTable(string settings, int runs, bool verdicts)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"duels, {runs} a row (the game's grenade rule DV104 on; cells: the pair won / lost / neither in {MaxSeconds:0} s, mean length, the pair's health left, health taken from the squad) | before | after (DV133)");
            var rows = new Dictionary<string, (DuelRow a, DuelRow b)>();
            foreach (var opp in DuelOpponents)
                foreach (var mode in DuelModes)
                {
                    var a = Duels(settings, opp, mode, runs, false); var b = Duels(settings, opp, mode, runs, true);
                    rows[opp + "/" + mode] = (a, b);
                    string what = mode == "kill" ? "the pair kills " + Opp(opp) + " (only the pair fires)" : mode == "survive" ? Opp(opp) + " kills the pair (only they fire)" : "the pair fights " + Opp(opp);
                    sb.AppendLine($"{what} | {DuelCell(a)} | {DuelCell(b)}");
                }
            if (!verdicts) return sb.ToString();
            var kg = rows["grunt/kill"]; var kt = rows["tough/kill"]; var sg = rows["grunt/survive"]; var st = rows["tough/survive"]; var fg = rows["grunt/fight"]; var ft = rows["tough/fight"];
            int tol = Math.Max(1, (int)Math.Ceiling(runs * 0.1));
            sb.AppendLine(OzV11Kit.Verdict(kt.a.won == runs && kt.b.won == runs && kt.b.secs < kt.a.secs * 0.95,
                $"time to kill a Tough squad (only the pair fires): {kt.a.secs:0.0} s -> {kt.b.secs:0.0} s (x {kt.b.secs / Math.Max(0.01, kt.a.secs):0.00}; 46 instead of 40 HP a hit: 74 hits a Tough instead of 85)"));
            sb.AppendLine(OzV11Kit.Verdict(kg.a.won == runs && kg.b.won == runs && kg.b.secs <= kg.a.secs * 1.05,
                $"time to kill a grunt squad (only the pair fires): {kg.a.secs:0.0} s -> {kg.b.secs:0.0} s (a fresh grunt still takes 3 hits: 2 x 518 = 1036 of his 1081 HP)"));
            sb.AppendLine(OzV11Kit.Verdict(sg.a.lost == runs && sg.b.lost == runs && sg.b.secs > sg.a.secs * 1.05,
                $"time a grunt squad needs to kill the pair (only they fire): {sg.a.secs:0.0} s -> {sg.b.secs:0.0} s (x {sg.b.secs / Math.Max(0.01, sg.a.secs):0.00}; 36 rifle hits a hero instead of 32)"));
            sb.AppendLine(OzV11Kit.Verdict(st.b.secs >= st.a.secs * 0.95,
                $"time a Tough squad needs to kill the pair (only they fire): {st.a.secs:0.0} s -> {st.b.secs:0.0} s (a hero still takes 3 rockets: 2 x 1667 = 3334 HP)"));
            sb.AppendLine(OzV11Kit.Verdict(fg.b.won >= fg.a.won - tol && ft.b.won >= ft.a.won - tol,
                $"the real duels are never weaker: against a grunt squad {fg.a.won} -> {fg.b.won} of {runs} won, against a Tough squad {ft.a.won} -> {ft.b.won} (within {tol} of before or better)"));
            return sb.ToString();
        }

        public static readonly string[] Opponents = { "2 grunt squads", "3 grunt squads", "gun", "light tank" };

        /// <summary>The duel rows and DV112's fight table before (DV133 off) and after (on), <paramref name="runs"/> a row.</summary>
        public static string Fights(string settings, int runs)
        {
            var sb = new StringBuilder();
            sb.Append(DuelTable(settings, runs, false));
            sb.AppendLine($"ALAN & BRAD (red) against | before: won / lost / draws, mean s, red health left / blue health taken | after (DV133) | {runs} fights a row (DV112's table, its seeds, DV104 off as in every counted check)");
            foreach (var opp in Opponents)
            {
                var a = With(false, () => OzV11HeroSquadCount.Count(settings, "ALAN & BRAD", opp, runs, 1100003));
                var b = With(true, () => OzV11HeroSquadCount.Count(settings, "ALAN & BRAD", opp, runs, 1100003));
                sb.AppendLine($"{opp} | {a.red}/{a.blue}/{a.draw}, {a.secs:0.0} s, {a.hpLeft * 100:0}% / {a.blueTaken * 100:0}% | {b.red}/{b.blue}/{b.draw}, {b.secs:0.0} s, {b.hpLeft * 100:0}% / {b.blueTaken * 100:0}%");
            }
            foreach (var opp in new[] { "gun", "light tank" })
            {
                var a = WithGrenadeRule(true, () => With(false, () => OzV11HeroSquadCount.Count(settings, "ALAN & BRAD", opp, runs, 1100003)));
                var b = WithGrenadeRule(true, () => With(true, () => OzV11HeroSquadCount.Count(settings, "ALAN & BRAD", opp, runs, 1100003)));
                sb.AppendLine($"{opp}, DV104 on (as the game) | {a.red}/{a.blue}/{a.draw}, {a.secs:0.0} s, {a.hpLeft * 100:0}% / {a.blueTaken * 100:0}% | {b.red}/{b.blue}/{b.draw}, {b.secs:0.0} s, {b.hpLeft * 100:0}% / {b.blueTaken * 100:0}%");
            }
            return sb.ToString();
        }

#if UNITY_EDITOR
        [UnityEditor.MenuItem("OpenZ/V1.1/Hero Squad: K442 Check (+15 % health and damage)")]
        static void Menu() => OzV11Kit.RunMenu("Hero squad stronger again: +15 % health and damage (DV133, K442)", () => Check(OzV11Kit.SettingsText));

        [UnityEditor.MenuItem("OpenZ/V1.1/Hero Squad: K442 Duels (before / after, 100 a row)")]
        static void MenuFights() => OzV11Kit.RunMenu("Hero squad stronger again: duels and fights before / after (DV133, K442, 100 a row)", () => Fights(OzV11Kit.SettingsText, 100));

        [UnityEditor.MenuItem("OpenZ/V1.1/Hero Squad: K442 Watch (Level 16, Play)")]
        static void MenuWatch()
        {
            if (!UnityEngine.Application.isPlaying) { UnityEngine.Debug.LogWarning("[OpenZ] K442 hero watch: start Level 16 in Play first"); return; }
            OzHeroPlusWatch.Run();
            UnityEngine.Debug.Log("[OpenZ] K442 hero watch started; its report: OzHeroPlusWatch.Report, the log in " + OzHeroPlusWatch.Folder());
        }
#endif
    }
}
