using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// DV112 hero squads stronger (Sim/ZHeroSquad.Kh4.cs), counted headless (ZWorld.Create + Tick, no scene, nothing saved):
    ///  * Check: the numbers at the start with the switch on and off (hit points x 1.20, Brad's and Alan's range x 1.15, a grunt
    ///    unchanged), one forced hit on a crewed light tank and on a grunt (x 1.20), the self-repair (from half health: one minute of the
    ///    game clock gives back 20 % of full health, never more than one hit point a tick, full after 150 s, never above full; a grunt squad
    ///    beside them repairs nothing; switch off: nothing), a destroyed hero stays destroyed, the hit points kept inside an own troop
    ///    carrier and the repair going on when they are out, two runs of the same fight giving the same numbers, and their thrown
    ///    grenades x 1.20 (item 257: the numbers Alan's real throw carries with the game's grenade rule DV104 on and off, a grunt's
    ///    grenade unchanged, and the HP its blast takes on a direct hit and on an area hit).
    ///  * Fights: the pair against two and three grunt squads, a gun and a light tank, before (switch off: the numbers of DV108 + KH2)
    ///    and after (switch on), the fights of Editor/OzV11HeroSquadCount.cs with their seeds (DV104 off, as in every counted check);
    ///    then the gun and the light tank once more with DV104 on, as the game plays them (the rows where grenades are thrown).
    /// Menus OpenZ/V1.1/Hero Squad: KH4 Check (stronger, self-repair) and OpenZ/V1.1/Hero Squad: KH4 Fights (before / after).
    /// </summary>
    public static class OzV11HeroKh4Count
    {
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

        /// <summary>The DV112 factors, worked out here on their own (not by the rule's code), rounded half up.</summary>
        static int Times(int v, double f) => (int)Math.Round(v * f, MidpointRounding.AwayFromZero);

        /// <summary>One forced hit (hit chance 1, no snipe) of a red <paramref name="hero"/> pair's leader (else a lone grunt) on a blue
        /// crewed light tank or a grunt that cannot die: the HP it removed.</summary>
        static int HitOn(string settings, bool hero, string what)
        {
            var w = OzV11Kit.World(settings, OzV11Kit.Plain(32, 32), 5);
            OzV11Kit.Dummy(w, 30 * 16 + 8, 30 * 16 + 8);
            var s = hero ? w.CreateHeroSquad(ZHeroSquads.AlanBrad, Team.Red, new[] { 8, 8, 8, 6 }) : w.CreateRobotGroup("grunt", 8 * 16, 8 * 16, Team.Red, 1);
            ZObject t = what == "light" ? w.CreateObject("vehicle", "light", 13 * 16, 8 * 16, Team.Blue) : w.CreateRobotGroup("grunt", 13 * 16, 8 * 16, Team.Blue, 1);
            t.Damage = 0; t.MaxHealth = t.Health = 1000000;
            foreach (var m in OzV11Kit.Squad(s)) { m.DamageChance = 1.0; m.SnipeChance = 0; }
            foreach (var m in s.Minions.ToList()) { m.MoveSpeed = 0; m.Damage = 0; }
            OzV11Kit.CpuAttack(w, s, t);
            int before = t.Health;
            for (int i = 0; i < 600 && t.Health == before; i++) { w.Tick(ZConst.TICK); w.Events.Clear(); }
            return before - t.Health;
        }

        public static string Check(string settings)
        {
            var sb = new StringBuilder();
            sb.AppendLine("ALAN & BRAD stronger (DV112): hit points x 1.20, range x 1.15, strong shot x 1.20, thrown grenades x 1.20, self-repair 20 % of full health a minute");
            var g = OzV11Kit.World(settings, OzV11Kit.Plain(8, 8), 1).S.Unit("grunt");
            var k = ZHeroSquads.Get(ZHeroSquads.AlanBrad);
            int hpOld = g.MaxHealth * k.HealthMul, hpNew = Times(hpOld, 1.20);
            int bradOld = (int)Math.Round(g.attack_radius * ZHeroRange.MulOf(k, 0)), alanOld = (int)Math.Round(g.attack_radius * ZHeroRange.MulOf(k, 1));
            int bradNew = Times(bradOld, 1.15), alanNew = Times(alanOld, 1.15);
            // 1 the numbers at the start, on and off
            {
                ZObject brad, alan, grunt, brad2, alan2;
                using (ZWorld.HeroBoostOn())
                {
                    var w = Ground(settings, 81);
                    brad = Pair(w, 10, 10); alan = brad.Minions.FirstOrDefault(); grunt = w.CreateRobotGroup("grunt", 10 * 16, 20 * 16, Team.Red);
                }
                using (ZWorld.HeroBoostOff())
                {
                    var w2 = Ground(settings, 81);
                    brad2 = Pair(w2, 10, 10); alan2 = brad2.Minions.FirstOrDefault();
                }
                bool on = alan != null && brad.MaxHealth == hpNew && alan.MaxHealth == hpNew && brad.Health == hpNew && alan.Health == hpNew
                          && brad.AttackRadius == bradNew && alan.AttackRadius == alanNew && grunt.MaxHealth == g.MaxHealth && grunt.AttackRadius == g.attack_radius;
                bool off = alan2 != null && brad2.MaxHealth == hpOld && alan2.MaxHealth == hpOld && brad2.AttackRadius == bradOld && alan2.AttackRadius == alanOld;
                sb.AppendLine(OzV11Kit.Verdict(on && off,
                    $"at the start: hit points Brad {brad.MaxHealth}, Alan {alan?.MaxHealth} (wanted {hpNew} = 1.20 x {hpOld}; a grunt {grunt.MaxHealth}); " +
                    $"range Brad {brad.AttackRadius} px = {brad.AttackRadius / 8.0:0.0} m (wanted {bradNew} = 1.15 x {bradOld}), Alan {alan?.AttackRadius} px = {(alan?.AttackRadius ?? 0) / 8.0:0.0} m (wanted {alanNew} = 1.15 x {alanOld}: his KH2 1.10 x a grunt's, then the 1.15 on top), a grunt {grunt.AttackRadius}; " +
                    $"switch off: hit points {brad2.MaxHealth} / {alan2?.MaxHealth}, range {brad2.AttackRadius} / {alan2?.AttackRadius} (the numbers before)"));
            }
            // 2 the strong shot, one forced hit, on and off (a grunt's own hit as the reference)
            {
                int gl = HitOn(settings, false, "light"), gg = HitOn(settings, false, "grunt");
                int hl, hg, hl0, hg0;
                using (ZWorld.HeroBoostOn()) { hl = HitOn(settings, true, "light"); hg = HitOn(settings, true, "grunt"); }
                using (ZWorld.HeroBoostOff()) { hl0 = HitOn(settings, true, "light"); hg0 = HitOn(settings, true, "grunt"); }
                bool ok = gl > 0 && gg > 0 && hl0 == gl * k.DamageMul && hg0 == gg * k.DamageMul && hl == Times(hl0, 1.20) && hg == Times(hg0, 1.20);
                sb.AppendLine(OzV11Kit.Verdict(ok, $"strong shot: one hit on a light tank {hl} HP (before {hl0}, wanted {Times(hl0, 1.20)}; a grunt's {gl}), on a grunt {hg} HP (before {hg0}, wanted {Times(hg0, 1.20)}; a grunt's {gg}, the rifle pair)"));
            }
            // 3 the self-repair: both heroes and a grunt squad at half health, nobody near, one minute; then on to full
            {
                using (ZWorld.HeroBoostOn())
                {
                    var w = Ground(settings, 82);
                    var brad = Pair(w, 10, 10); var alan = brad.Minions.First();
                    var grunt = w.CreateRobotGroup("grunt", 10 * 16, 24 * 16, Team.Red);
                    foreach (var o in new[] { brad, alan, grunt }) o.SetHealth(o.MaxHealth / 2);
                    int b0 = brad.Health, a0 = alan.Health, g0 = grunt.Health, last = brad.Health, bigStep = 0, steps = 0, above = 0;
                    double t0 = w.Time, full = -1;
                    void Each()
                    {
                        int d = brad.Health - last; last = brad.Health;
                        if (d > 0) steps++;
                        if (d > bigStep) bigStep = d;
                        if (brad.Health > brad.MaxHealth || alan.Health > alan.MaxHealth) above++;
                        if (full < 0 && brad.Health >= brad.MaxHealth) full = w.Time - t0;
                    }
                    Ticks(w, 60, null, Each);
                    int b60 = brad.Health, a60 = alan.Health, g60 = grunt.Health;
                    int want = (int)((long)hpNew * 20 / 100);                                                    // 20 % of full health in one minute (the rest below 1 HP carried on)
                    Ticks(w, 100, null, Each);
                    double halfToFull = (hpNew - b0) / (hpNew * 0.20 / 60.0);
                    bool ok = b60 - b0 == want && a60 - a0 == want && g60 == g0 && bigStep == 1 && above == 0
                              && Math.Abs(full - halfToFull) < 0.15 && brad.Health == brad.MaxHealth && alan.Health == alan.MaxHealth && grunt.Health == g0 && w.HeroRegenHp == (hpNew - b0) + (hpNew - a0);
                    sb.AppendLine(OzV11Kit.Verdict(ok, $"self-repair from half health: after 60 s Brad +{b60 - b0} HP, Alan +{a60 - a0} HP (wanted {want} = 20 % of {hpNew}); a grunt squad beside them +{g60 - g0}; " +
                                                       $"smooth: {steps} steps up to full, at most {bigStep} HP a tick; full after {full:0.00} s (wanted {halfToFull:0.00} s), 100 s later {brad.Health}/{brad.MaxHealth} and {alan.Health}/{alan.MaxHealth}, above full {above} times; repaired in all {w.HeroRegenHp} HP"));
                }
                using (ZWorld.HeroBoostOff())
                {
                    var w = Ground(settings, 82);
                    var brad = Pair(w, 10, 10); var alan = brad.Minions.First();
                    foreach (var o in new[] { brad, alan }) o.SetHealth(o.MaxHealth / 2);
                    int b0 = brad.Health, a0 = alan.Health;
                    Ticks(w, 60);
                    sb.AppendLine(OzV11Kit.Verdict(brad.Health == b0 && alan.Health == a0 && w.HeroRegenHp == 0, $"switch off: after 60 s at half health Brad {brad.Health - b0:+0;-0;0} HP, Alan {alan.Health - a0:+0;-0;0} HP (no self-repair)"));
                }
            }
            // 4 a destroyed hero stays destroyed; the other repairs on
            {
                using (ZWorld.HeroBoostOn())
                {
                    var w = Ground(settings, 83);
                    var brad = Pair(w, 10, 10); var alan = brad.Minions.First();
                    brad.SetHealth(brad.MaxHealth / 2); int b0 = brad.Health;
                    alan.SetHealth(0); w.UpdateObjectHealth(alan);
                    Ticks(w, 10);
                    bool gone = alan.Destroyed && alan.Health == 0 && !w.Objects.Any(o => o == alan && OzV11Kit.Alive(o));
                    sb.AppendLine(OzV11Kit.Verdict(gone && brad.Health > b0, $"Alan destroyed: 10 s later {(gone ? "still destroyed (0 HP)" : "ALIVE AGAIN")}; Brad beside him +{brad.Health - b0} HP"));
                }
            }
            // 5 an own troop carrier: inside, the crew list keeps their hit points; out again, the numbers and the repair go on
            {
                using (ZWorld.HeroBoostOn())
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
                    string later = string.Join(" / ", v.Passengers);
                    bool ej = w.Eject(Team.Red, v); Ticks(w, 0.1);
                    var b2 = w.Objects.FirstOrDefault(o => o.IsHero && o.HeroMemberName == "brad" && OzV11Kit.Alive(o));
                    var a2 = w.Objects.FirstOrDefault(o => o.IsHero && o.HeroMemberName == "alan" && OzV11Kit.Alive(o));
                    bool outOk = ej && b2 != null && a2 != null && b2.MaxHealth == hpNew && a2.MaxHealth == hpNew && b2.AttackRadius == bradNew && a2.AttackRadius == alanNew;
                    int bOut = b2?.Health ?? 0, aOut = a2?.Health ?? 0;
                    Ticks(w, 10);
                    bool on = outOk && b2.Health > bOut && a2.Health > aOut;
                    sb.AppendLine(OzV11Kit.Verdict(kept && on, $"own troop carrier: stored {stored} HP, 30 s later {later} (kept, no repair inside); " +
                                                               $"out: {(b2 != null && a2 != null ? $"Brad {bOut}/{b2.MaxHealth} {b2.AttackRadius} px, Alan {aOut}/{a2.MaxHealth} {a2.AttackRadius} px; 10 s later +{b2.Health - bOut} / +{a2.Health - aOut} HP" : "NOT both out")}"));
                }
            }
            // 6 the same fight twice: the same numbers (the rule draws nothing, it runs on the game clock)
            {
                using (ZWorld.HeroBoostOn())
                {
                    int r1 = OzV11HeroSquadCount.Fight(settings, "ALAN & BRAD", "2 grunt squads", 1100003, out double s1, out double h1, out double t1);
                    int r2 = OzV11HeroSquadCount.Fight(settings, "ALAN & BRAD", "2 grunt squads", 1100003, out double s2, out double h2, out double t2);
                    sb.AppendLine(OzV11Kit.Verdict(r1 == r2 && s1 == s2 && h1 == h2 && t1 == t2, $"the same fight twice (against two grunt squads, one seed): {Res(r1)} after {s1:0.00} s with {h1 * 100:0.0} % health left, again {Res(r2)} after {s2:0.00} s with {h2 * 100:0.0} %"));
                }
            }
            // 7 item 257: their thrown grenades x 1.20
            Grenades(settings, sb);
            return sb.ToString();
        }

        static string Res(int r) => r == 1 ? "won" : r == 2 ? "lost" : "a draw";

        /// <summary>Runs <paramref name="f"/> with the game's grenade rule DV104 (ZWorld.GrenadeBoostDeviation) on or off, then puts it back.</summary>
        static T WithGrenadeRule<T>(bool on, Func<T> f)
        {
            bool was = ZWorld.GrenadeBoostDeviation;
            try { ZWorld.GrenadeBoostDeviation = on; return f(); }
            finally { ZWorld.GrenadeBoostDeviation = was; }
        }

        /// <summary>The first grenade a red thrower (<paramref name="hero"/>: Alan of the pair with his own five; else a grunt squad given
        /// five) throws at a blue crewed light tank that cannot die nor shoot: the HP it carries on a direct hit and on an area hit
        /// (its own area hit, 0 = the game's) and its radius; the world's grenade numbers beside it.</summary>
        static (int damage, int area, int radius, int worldDamage, int worldArea, int boosted) Throw(string settings, bool hero)
        {
            var w = OzV11Kit.World(settings, OzV11Kit.Plain(32, 32), 6);
            OzV11Kit.Dummy(w, 30 * 16 + 8, 30 * 16 + 8);
            var s = hero ? w.CreateHeroSquad(ZHeroSquads.AlanBrad, Team.Red, new[] { 8, 8, 8, 6 }) : w.CreateRobotGroup("grunt", 8 * 16, 8 * 16, Team.Red, 1);
            if (!hero) s.Grenades = 5;
            var thrower = hero ? s.Minions.FirstOrDefault(m => m.HeroMemberName == "alan") : s;
            var t = w.CreateObject("vehicle", "light", 13 * 16, 8 * 16, Team.Blue);
            t.Damage = 0; t.MaxHealth = t.Health = 1000000;
            OzV11Kit.CpuAttack(w, s, t);
            ZMissile g = null;
            for (int i = 0; i < 3000 && g == null && thrower != null; i++)
            {
                w.Tick(ZConst.TICK); w.Events.Clear();
                g = w.QueuedMissiles.Concat(w.PendingMissiles).FirstOrDefault(m => m.grenade && m.attackerId == thrower.Id);
            }
            return g == null ? (-1, -1, -1, w.ThrownGrenadeDamage, w.ThrownGrenadeAreaDamage, w.HeroGrenadesThrown)
                             : (g.damage, g.areaDamage, g.radius, w.ThrownGrenadeDamage, w.ThrownGrenadeAreaDamage, w.HeroGrenadesThrown);
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

        /// <summary>Item 257: the heroes' thrown grenades x 1.20 (with DV104 on, the game's rule, and off), a grunt's unchanged, and the blast.</summary>
        static void Grenades(string settings, StringBuilder sb)
        {
            var on = WithGrenadeRule(true, () => { using (ZWorld.HeroBoostOn()) return Throw(settings, true); });
            var onGrunt = WithGrenadeRule(true, () => { using (ZWorld.HeroBoostOn()) return Throw(settings, false); });
            var off = WithGrenadeRule(true, () => { using (ZWorld.HeroBoostOff()) return Throw(settings, true); });
            var on0 = WithGrenadeRule(false, () => { using (ZWorld.HeroBoostOn()) return Throw(settings, true); });
            var off0 = WithGrenadeRule(false, () => { using (ZWorld.HeroBoostOff()) return Throw(settings, true); });
            int wantD = Times(on.worldDamage, 1.20), wantA = Times(on.worldArea, 1.20), wantD0 = Times(on0.worldDamage, 1.20);
            bool thrown = on.damage > 0 && onGrunt.damage > 0 && off.damage > 0 && on0.damage > 0 && off0.damage > 0;
            bool ok = thrown && on.damage == wantD && on.area == wantA && on.boosted == 1 && onGrunt.damage == onGrunt.worldDamage && onGrunt.area == 0 && onGrunt.boosted == 0
                      && off.damage == off.worldDamage && off.area == 0 && off.boosted == 0 && on0.damage == wantD0 && off0.damage == off0.worldDamage
                      && on.radius == onGrunt.radius && on.radius == off.radius && on0.radius == off0.radius;
            sb.AppendLine(OzV11Kit.Verdict(ok, $"thrown grenades (item 257): Alan's carries {on.damage} HP on a direct hit and {on.area} on an area hit (wanted {wantD} = 1.20 x {on.worldDamage}, {wantA} = 1.20 x {on.worldArea}, the game's rule DV104 on), radius {on.radius} px; " +
                                               $"a grunt's {onGrunt.damage} / {(onGrunt.area == 0 ? "the game's " + onGrunt.worldArea : onGrunt.area.ToString())} (unchanged); switch off: Alan's {off.damage} / {(off.area == 0 ? "the game's " + off.worldArea : off.area.ToString())}; " +
                                               $"DV104 off: Alan's {on0.damage} (wanted {wantD0} = 1.20 x {on0.worldDamage}), switch off {off0.damage}{(thrown ? "" : "; A THROW WAS MISSING")}"));
            // the blast on a light tank's hull: on its body (direct hit, the centre: the grenade's whole damage) and 20 px beside its centre
            // (outside the 32 px hull: an area hit, the straight-line fall-off of the game's rule), the hero's grenade and the game's
            int rd = on.radius > 0 ? on.radius : 36;
            float ratio = 20f / rd;
            var hits = WithGrenadeRule(true, () => (BlastOn(settings, on.damage, on.area, rd, 0), BlastOn(settings, on.damage, on.area, rd, 20),
                                                    BlastOn(settings, on.worldDamage, 0, rd, 0), BlastOn(settings, on.worldDamage, 0, rd, 20)));
            int wantArea = (int)(wantA * (1.0 - ratio)), gameArea = (int)(on.worldArea * (1.0 - ratio));
            bool blast = hits.Item1 == wantD && hits.Item2 == wantArea && hits.Item3 == on.worldDamage && hits.Item4 == gameArea;
            sb.AppendLine(OzV11Kit.Verdict(blast, $"its blast on a light tank's hull (DV104 on): on the hull {hits.Item1} HP (wanted {wantD}), 20 px beside its centre {hits.Item2} HP (wanted {wantArea}); " +
                                                  $"the game's grenade there {hits.Item3} / {hits.Item4} HP (wanted {on.worldDamage} / {gameArea})"));
        }

        public static readonly string[] Opponents = { "2 grunt squads", "3 grunt squads", "gun", "light tank" };

        /// <summary>The fight table before (switch off) and after (switch on), <paramref name="runs"/> fights a row, the seeds of KH2's table.</summary>
        public static string Fights(string settings, int runs)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"ALAN & BRAD (red) against | before: red wins / blue wins / draws, mean s, red health left / blue health taken | after (DV112) | {runs} fights a row");
            foreach (var opp in Opponents)
            {
                OzV11HeroSquadCount.Row a, b;
                using (ZWorld.HeroBoostOff()) a = OzV11HeroSquadCount.Count(settings, "ALAN & BRAD", opp, runs, 1100003);
                using (ZWorld.HeroBoostOn()) b = OzV11HeroSquadCount.Count(settings, "ALAN & BRAD", opp, runs, 1100003);
                sb.AppendLine($"{opp} | {a.red}/{a.blue}/{a.draw}, {a.secs:0.0} s, {a.hpLeft * 100:0}% / {a.blueTaken * 100:0}% | {b.red}/{b.blue}/{b.draw}, {b.secs:0.0} s, {b.hpLeft * 100:0}% / {b.blueTaken * 100:0}%");
            }
            sb.AppendLine("with the game's grenade rule DV104 on (as the game plays; the rows above run with it off, as every counted check): the rows where Alan throws his grenades");
            foreach (var opp in new[] { "gun", "light tank" })
            {
                var a = WithGrenadeRule(true, () => { using (ZWorld.HeroBoostOff()) return OzV11HeroSquadCount.Count(settings, "ALAN & BRAD", opp, runs, 1100003); });
                var b = WithGrenadeRule(true, () => { using (ZWorld.HeroBoostOn()) return OzV11HeroSquadCount.Count(settings, "ALAN & BRAD", opp, runs, 1100003); });
                sb.AppendLine($"{opp}, DV104 on | {a.red}/{a.blue}/{a.draw}, {a.secs:0.0} s, {a.hpLeft * 100:0}% / {a.blueTaken * 100:0}% | {b.red}/{b.blue}/{b.draw}, {b.secs:0.0} s, {b.hpLeft * 100:0}% / {b.blueTaken * 100:0}%");
            }
            return sb.ToString();
        }

#if UNITY_EDITOR
        [UnityEditor.MenuItem("OpenZ/V1.1/Hero Squad: KH4 Check (stronger, self-repair)")]
        static void Menu() => OzV11Kit.RunMenu("Hero squad stronger + self-repair (DV112, KH4)", () => Check(OzV11Kit.SettingsText));

        [UnityEditor.MenuItem("OpenZ/V1.1/Hero Squad: KH4 Fights (before / after)")]
        static void MenuFights() => OzV11Kit.RunMenu("Hero squad stronger: fights before / after (DV112, KH4, 100 a row)", () => Fights(OzV11Kit.SettingsText, 100));
#endif
    }
}
