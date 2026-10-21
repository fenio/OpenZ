using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// DV108 hero squads, KH2 (item 199): Alan's own range (Sim/ZHeroSquad.Kh2.cs), counted headless:
    ///    the radii at the start (Alan 1.10 x a grunt's, Brad and a grunt as the table), the rule's own reach test at 126 px (inside Alan's
    ///    132, outside a grunt's 120), the range kept when he gets out of an own troop carrier, and two staged attacks with the switch on
    ///    and off (an unarmed blue grunt squad: Alan's shots; an unarmed crewed light tank: Alan's grenades), the same seeds.
    /// Menu OpenZ/V1.1/Hero Squad: KH2 Count (Alan's range).
    /// </summary>
    public static class OzV11HeroKh2Count
    {
        static ZWorld Ground(string settings, int seed)
        {
            var w = OzV11Kit.World(settings, OzV11Kit.Plain(48, 40), seed);
            OzV11Kit.Dummy(w, 46 * 16 + 8, 38 * 16 + 8);
            return w;
        }

        static void Ticks(ZWorld w, double secs, Func<bool> until = null)
        {
            int n = (int)Math.Round(secs / ZConst.TICK);
            for (int i = 0; i < n; i++) { w.Tick(ZConst.TICK); w.Events.Clear(); if (until != null && until()) break; }
        }

        static ZObject Pair(ZWorld w, int tx, int ty) => w.CreateHeroSquad(ZHeroSquads.AlanBrad, Team.Red, new[] { tx, ty, tx, ty + 2 });

        public static string Check(string settings)
        {
            var sb = new StringBuilder();
            sb.AppendLine("ALAN & BRAD: Alan's range + 10 % (item 199), Brad unchanged");
            bool keep = ZWorld.HeroRangeDeviation;
            try
            {
                int gr = OzV11Kit.World(settings, OzV11Kit.Plain(8, 8), 1).S.Unit("grunt").attack_radius, want = (int)Math.Round(gr * 1.10);
                {
                    var w = Ground(settings, 71);
                    var brad = Pair(w, 10, 10); var alan = brad.Minions.FirstOrDefault(); var grunt = w.CreateRobotGroup("grunt", 10 * 16, 20 * 16, Team.Red);
                    ZWorld.HeroRangeDeviation = false;
                    var w2 = Ground(settings, 71); var brad2 = Pair(w2, 10, 10); var alan2 = brad2.Minions.FirstOrDefault();
                    ZWorld.HeroRangeDeviation = true;
                    bool ok = alan != null && alan.HeroMemberName == "alan" && alan.AttackRadius == want && brad.AttackRadius == gr && grunt.AttackRadius == gr && alan2 != null && alan2.AttackRadius == gr && brad2.AttackRadius == gr;
                    sb.AppendLine(OzV11Kit.Verdict(ok, $"at the start: Alan {alan?.AttackRadius} px = {(alan?.AttackRadius ?? 0) / 8.0:0.0} m (wanted {want} = 1.10 x a grunt's {gr}), Brad {brad.AttackRadius} (wanted {gr}), a grunt {grunt.AttackRadius}; " +
                                                       $"switch off: Alan {alan2?.AttackRadius}, Brad {brad2.AttackRadius} (both a grunt's)"));
                }
                {   // the rule's own reach test (WithinAttackRadius: the radius, a clear line) at 126 px from the shooter's centre
                    var w = Ground(settings, 72);
                    var brad = Pair(w, 10, 10); var alan = brad.Minions.First(); var grunt = w.CreateRobotGroup("grunt", 10 * 16, 20 * 16, Team.Red);
                    var tA = w.CreateRobotGroup("grunt", alan.CenterX + 126 - 8, alan.CenterY - 8, Team.Blue);
                    var tG = w.CreateRobotGroup("grunt", grunt.CenterX + 126 - 8, grunt.CenterY - 8, Team.Blue);
                    double dA = Dist(alan, tA), dG = Dist(grunt, tG);
                    bool a = alan.WithinAttackRadius(tA), g = grunt.WithinAttackRadius(tG);
                    sb.AppendLine(OzV11Kit.Verdict(a && !g, $"reach test at {dA:0} px (Alan) / {dG:0} px (a grunt): Alan {(a ? "in range" : "OUT of range")}, a grunt {(g ? "IN range" : "out of range")}"));
                }
                {   // out of an own troop carrier: he comes out as Alan with his own range again
                    var w = Ground(settings, 73);
                    var brad = Pair(w, 10, 10);
                    var apc = w.CreateObject("vehicle", "apc", 13 * 16, 10 * 16, Team.Red);
                    Ticks(w, 21);
                    OzV11Kit.PlayerEnter(w, brad, apc);
                    Ticks(w, 30, () => apc.Passengers.Count > 0);
                    int aboard = apc.Passengers.Count; bool ej = w.Eject(Team.Red, apc);
                    Ticks(w, 1);
                    var a2 = w.Objects.FirstOrDefault(o => o.IsHero && o.HeroMemberName == "alan" && OzV11Kit.Alive(o));
                    var b2 = w.Objects.FirstOrDefault(o => o.IsHero && o.HeroMemberName == "brad" && OzV11Kit.Alive(o));
                    sb.AppendLine(OzV11Kit.Verdict(aboard >= 2 && ej && a2 != null && a2.AttackRadius == want && b2 != null && b2.AttackRadius == gr,
                        $"through an own troop carrier: {aboard} rode, EXIT {(ej ? "taken" : "refused")}; out again Alan {a2?.AttackRadius} px (wanted {want}), Brad {b2?.AttackRadius} (wanted {gr})"));
                }
                var on = Stage(settings, false, true); var off = Stage(settings, false, false);
                sb.AppendLine(OzV11Kit.Verdict(on.alanShots >= off.alanShots && on.alanShots > 0,
                    $"staged 30 s, the pair ordered at an unarmed blue grunt squad 14 tiles away: Alan fired {on.alanShots} shots, first {Fmt(on.alanFirst)} after the order (Brad {on.bradShots}, first {Fmt(on.bradFirst)}); " +
                    $"with a grunt's range: Alan {off.alanShots}, first {Fmt(off.alanFirst)} (Brad {off.bradShots}, first {Fmt(off.bradFirst)})"));
                var tOn = Stage(settings, true, true); var tOff = Stage(settings, true, false);
                sb.AppendLine(OzV11Kit.Verdict(tOn.alanNades > 0 && tOn.alanNades >= tOff.alanNades,
                    $"staged 30 s at an unarmed crewed light tank 14 tiles away: Alan threw {tOn.alanNades} grenades, first {Fmt(tOn.nadeFirst)} after the order; with a grunt's range {tOff.alanNades}, first {Fmt(tOff.nadeFirst)}"));
                foreach (var (tank, path) in new[] { (false, false), (false, true), (true, true) })
                {
                    var bOn = Behind(settings, tank, path, true); var bOff = Behind(settings, tank, path, false);
                    int on1 = tank ? bOn.nades : bOn.shots, off1 = tank ? bOff.nades : bOff.shots;
                    sb.AppendLine(OzV11Kit.Verdict(on1 > 0 && on1 >= off1,
                        $"behind Brad, {(path ? "on a one-tile path between rocks" : "on open ground")}, at {(tank ? "an unarmed crewed light tank" : "an unarmed blue grunt squad")} {bOn.bradD0:0} px from Brad " +
                        $"(Alan behind him on the line, {bOn.alanD0:0} px from it), 20 s, {(tank ? "grenades" : "shots")} counted: Alan {bOn.shots} shots + {bOn.nades} grenades, first {Fmt(bOn.first)}, " +
                        $"he stood {bOn.alanD0:0} -> {bOn.alanD1:0} px (Brad {bOn.bradShots} shots); with a grunt's range: Alan {bOff.shots} shots + {bOff.nades} grenades, first {Fmt(bOff.first)}, " +
                        $"{bOff.alanD0:0} -> {bOff.alanD1:0} px (Brad {bOff.bradShots})"));
                }
            }
            catch (Exception ex) { sb.AppendLine(OzV11Kit.Verdict(false, "the check threw " + ex.GetType().Name + ": " + ex.Message)); }
            finally { ZWorld.HeroRangeDeviation = keep; }
            return sb.ToString();
        }

        static double Dist(ZObject a, ZObject b) { double dx = a.CenterX - b.CenterX, dy = a.CenterY - b.CenterY; return Math.Sqrt(dx * dx + dy * dy); }
        static string Fmt(double s) => s < 0 ? "never" : s.ToString("0.0") + " s";

        /// <summary>The pair attacks an unarmed, unkillable, still blue target (a grunt squad or a crewed light tank) from 14 tiles for 30 s.</summary>
        static (int alanShots, int bradShots, int alanNades, double alanFirst, double bradFirst, double nadeFirst) Stage(string settings, bool tank, bool range)
        {
            ZWorld.HeroRangeDeviation = range;
            var w = Ground(settings, tank ? 74 : 75);
            var brad = Pair(w, 8, 12); var alan = brad.Minions.First();
            ZObject target = tank ? w.CreateObject("vehicle", "light", 22 * 16, 12 * 16, Team.Blue) : w.CreateRobotGroup("grunt", 22 * 16, 12 * 16, Team.Blue);
            var blues = new List<ZObject> { target }; blues.AddRange(target.Minions);
            foreach (var b in blues) { b.Damage = 0; b.MaxHealth = 1000000; b.SetHealth(b.MaxHealth); b.MoveSpeed = 0; }
            var ids = new HashSet<int>(blues.Select(b => b.Id));
            Ticks(w, 21);
            OzV11Kit.PlayerAttack(w, brad, target);
            int ash = 0, bsh = 0, an = 0; double t0 = w.Time, af = -1, bf = -1, nf = -1, end = w.Time + 30;
            while (w.Time < end)
            {
                w.Tick(ZConst.TICK);
                foreach (var e in w.Events)
                {
                    if (!ids.Contains(e.otherId)) continue;
                    bool nade = e.type == ZEventType.MissileFired && e.flag, shot = !nade && (e.type == ZEventType.MissileFired || e.type == ZEventType.Hitscan);
                    if (e.objId == alan.Id) { if (nade) { an++; if (nf < 0) nf = w.Time - t0; } else if (shot) { ash++; if (af < 0) af = w.Time - t0; } }
                    else if (e.objId == brad.Id && shot) { bsh++; if (bf < 0) bf = w.Time - t0; }
                }
                w.Events.Clear();
            }
            return (ash, bsh, an, af, bf, nf);
        }

        /// <summary>Item 199 as the owner named it
        /// after the start's settling the target (an unarmed, unkillable, still blue grunt squad or crewed light tank) is put
        /// BradEdge px beyond Brad on the line from Alan through Brad, so Brad fires from where he stands and Alan stands behind him,
        /// out of a grunt's range; the pair is ordered at it and Alan is counted for 20 s (his shots, his grenades, his first one, his
        /// distance to the target at the order and at the end). path: the pair stands on a one-tile path (rock on both sides, tiles 2-17
        /// of rows 11 and 13), the target past its end: nobody can step up beside Brad.</summary>
        static (int shots, int nades, int bradShots, double first, double alanD0, double alanD1, double bradD0) Behind(string settings, bool tank, bool path, bool range)
        {
            ZWorld.HeroRangeDeviation = range;
            ZWorld w;
            if (path)
            {
                const int W = 48, H = 40; var cls = new string[W * H];
                for (int i = 0; i < cls.Length; i++) cls[i] = "0";
                for (int x = 2; x <= 17; x++) { cls[11 * W + x] = "3"; cls[13 * W + x] = "3"; }                 // ZClass.Impassable
                string plain = OzV11Kit.Plain(W, H), zeros = "\"classes\":[" + string.Join(",", Enumerable.Repeat("0", W * H)) + "]";
                w = OzV11Kit.World(settings, plain.Replace(zeros, "\"classes\":[" + string.Join(",", cls) + "]"), tank ? 78 : 79);
                OzV11Kit.Dummy(w, 46 * 16 + 8, 38 * 16 + 8);
            }
            else w = Ground(settings, tank ? 76 : 77);
            var brad = w.CreateHeroSquad(ZHeroSquads.AlanBrad, Team.Red, new[] { 12, 12, 11, 12 }); var alan = brad.Minions.First();
            Ticks(w, 21);
            double ux = brad.CenterX - alan.CenterX, uy = brad.CenterY - alan.CenterY, ul = Math.Sqrt(ux * ux + uy * uy);
            if (ul < 1) { ux = 1; uy = 0; ul = 1; }
            double cx = brad.CenterX + ux / ul * BradEdge, cy = brad.CenterY + uy / ul * BradEdge;   // the target's centre
            ZObject target;
            if (tank)
            {
                var size = Ground(settings, 1).CreateObject("vehicle", "light", 20 * 16, 20 * 16, Team.Blue); int hw = size.CenterX - size.LocX, hh = size.CenterY - size.LocY;   // its centre's offset, in a world of its own
                target = w.CreateObject("vehicle", "light", (int)Math.Round(cx) - hw, (int)Math.Round(cy) - hh, Team.Blue);
            }
            else target = w.CreateRobotGroup("grunt", (int)Math.Round(cx) - 8, (int)Math.Round(cy) - 8, Team.Blue);
            var blues = new List<ZObject> { target }; blues.AddRange(target.Minions);
            foreach (var b in blues) { b.Damage = 0; b.MaxHealth = 1000000; b.SetHealth(b.MaxHealth); b.MoveSpeed = 0; }
            var ids = new HashSet<int>(blues.Select(b => b.Id));
            double a0 = Dist(alan, target), b0 = Dist(brad, target);
            OzV11Kit.PlayerAttack(w, brad, target);
            int sh = 0, nd = 0, bsh = 0; double t0 = w.Time, first = -1, end = w.Time + 20;
            while (w.Time < end)
            {
                w.Tick(ZConst.TICK);
                foreach (var e in w.Events)
                {
                    if (!ids.Contains(e.otherId)) continue;
                    bool nade = e.type == ZEventType.MissileFired && e.flag, shot = !nade && (e.type == ZEventType.MissileFired || e.type == ZEventType.Hitscan);
                    if (e.objId == alan.Id && (nade || shot)) { if (nade) nd++; else sh++; if (first < 0) first = w.Time - t0; }
                    else if (e.objId == brad.Id && shot) bsh++;
                }
                w.Events.Clear();
            }
            return (sh, nd, bsh, first, a0, Dist(alan, target), b0);
        }

        /// <summary>Behind: the target's distance beyond Brad (px): inside a grunt's 120 px, so Brad fires from where he stands.</summary>
        public static int BradEdge = 116;

#if UNITY_EDITOR
        [UnityEditor.MenuItem("OpenZ/V1.1/Hero Squad: KH2 Count (Alan's range)")]
        static void Menu() => OzV11Kit.RunMenu("Hero squad: Alan's range + 10 % (DV108, KH2)", () => Check(OzV11Kit.SettingsText));
#endif
    }
}
