using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// DV108 hero squads, items 186 and 187 (Sim/ZHeroSquad.Kh.cs), counted headless (ZWorld.Create + Tick, no scene, nothing saved):
    ///  * no boarding by themselves: idle 60 s after the calm start (DV69, 20 s in which nothing starts by itself) beside an empty jeep
    ///    and an empty gun (and a grunt squad on the same spot for comparison),
    ///    a plain move past an empty jeep (DV84's grab on the way), then the player's enter order (they board);
    ///  * Alan's grenades: 5 at the start (Brad none, the panel's squad count 5); a staged fight (30 s from the order) against an unarmed grunt squad and against
    ///    an unarmed crewed light tank, Alan beside a sniper squad given the same 5 grenades: both throw the same way (never at the
    ///    robots, at the tank) and with the grenade rule off both throw at the robots too; his grenades ride with him through a troop
    ///    carrier and back out; Brad picks up a grenade crate and dies: Alan keeps his own and gets the pool (ZHeroGrenades.Merge).
    /// Menu OpenZ/V1.1/Hero Squad: KH Count (boarding, grenades).
    /// </summary>
    public static class OzV11HeroKhCount
    {
        static ZWorld Ground(string settings, int seed)
        {
            var w = OzV11Kit.World(settings, OzV11Kit.Plain(48, 40), seed);
            OzV11Kit.Dummy(w, 46 * 16 + 8, 38 * 16 + 8);
            return w;
        }

        static int Ticks(ZWorld w, double secs, Func<bool> until = null)
        {
            int n = (int)Math.Round(secs / ZConst.TICK), i = 0;
            for (; i < n; i++) { w.Tick(ZConst.TICK); w.Events.Clear(); if (until != null && until()) break; }
            return i;
        }

        static ZObject Squad(ZWorld w, bool hero, int tx, int ty) =>
            hero ? w.CreateHeroSquad(ZHeroSquads.AlanBrad, Team.Red, new[] { tx, ty, tx, ty + 2 }) : w.CreateRobotGroup("grunt", tx * 16, ty * 16, Team.Red);

        static bool Aboard(ZObject v) => v != null && v.Owner == Team.Red && (v.Drivers.Count > 0 || v.Passengers.Count > 0);

        public static string Check(string settings)
        {
            var sb = new StringBuilder();
            sb.AppendLine("ALAN & BRAD: boarding only on the player's order (item 186), Alan's 5 grenades by the sniper's rule (item 187)");
            // ---------------------------------------------------------------- 186
            {   // idle 60 s after the calm start (nothing starts by itself in its first 20 s), an empty jeep 3 tiles east and an empty gun 3 tiles south-east
                var res = new Dictionary<bool, (bool jeep, bool gun, int boarded, int onMap)>();
                foreach (bool hero in new[] { true, false })
                {
                    var w = Ground(settings, 61);
                    var s = Squad(w, hero, 10, 10);
                    var jeep = w.CreateObject("vehicle", "jeep", 13 * 16, 10 * 16, Team.Neutral);
                    var gun = w.CreateObject("cannon", "gun", 13 * 16, 14 * 16, Team.Neutral);
                    Ticks(w, ZWorld.CalmStartSec + 60);
                    int onMap = w.Objects.Count(o => o.IsRobot && o.Owner == Team.Red && OzV11Kit.Alive(o) && (!hero || o.IsHero));
                    res[hero] = (Aboard(jeep), Aboard(gun), w.HeroesBoarded, onMap);
                }
                var h = res[true]; var g = res[false];
                sb.AppendLine(OzV11Kit.Verdict(!h.jeep && !h.gun && h.boarded == 0 && h.onMap == 2 && (g.jeep || g.gun),
                    $"idle 60 s after the calm start ({ZWorld.CalmStartSec:0} s; {ZWorld.CalmStartSec + 60:0} s in all) beside an empty jeep (3 tiles) and an empty gun: the heroes boarded {h.boarded} times (wanted 0), jeep {(h.jeep ? "TAKEN" : "empty")}, gun {(h.gun ? "TAKEN" : "empty")}, both still on foot {h.onMap}/2; " +
                    $"a grunt squad on the same spot: jeep {(g.jeep ? "taken" : "empty")}, gun {(g.gun ? "taken" : "empty")} (the auto-grab is live)"));
            }
            {   // the player's plain move past an empty jeep 2 tiles beside the way (DV84 grab on the way)
                var res = new Dictionary<bool, (bool jeep, double dist)>();
                foreach (bool hero in new[] { true, false })
                {
                    var w = Ground(settings, 62);
                    var s = Squad(w, hero, 6, 20);
                    var jeep = w.CreateObject("vehicle", "jeep", 20 * 16, 22 * 16, Team.Neutral);
                    Ticks(w, 21);                                                                   // past the calm start (DV69)
                    OzV11Kit.PlayerMove(w, s, 36 * 16 + 8, 20 * 16 + 8, true);
                    Ticks(w, 40);
                    var lead = w.Objects.FirstOrDefault(o => o.IsRobot && o.Owner == Team.Red && !o.IsMinion && OzV11Kit.Alive(o) && o.CenterX > 0);
                    res[hero] = (Aboard(jeep), lead != null ? Math.Abs(lead.CenterX - (36 * 16 + 8)) / 16.0 : -1);
                }
                sb.AppendLine(OzV11Kit.Verdict(!res[true].jeep && res[true].dist >= 0 && res[true].dist < 4 && res[false].jeep,
                    $"a plain move past an empty jeep 2 tiles beside the way: the heroes {(res[true].jeep ? "TOOK it" : "walked on")} (arrived {res[true].dist:0.0} tiles from the goal); a grunt squad {(res[false].jeep ? "took it on the way" : "walked on")}"));
            }
            {   // the player's enter order: they board (jeep: Brad drives; gun: Brad mans it; own troop carrier: both ride)
                string Ordered(string what, Team owner, out bool ok)
                {
                    var w = Ground(settings, 63);
                    var s = Squad(w, true, 10, 10);
                    var v = w.CreateObject(what == "gun" ? "cannon" : "vehicle", what, 13 * 16, 10 * 16, owner);
                    Ticks(w, 21);
                    OzV11Kit.PlayerEnter(w, s, v);
                    Ticks(w, 30, () => what == "apc" ? v.Passengers.Count > 0 : Aboard(v));
                    ok = what == "apc" ? v.Passengers.Count >= 2 : Aboard(v);
                    return $"{what} {(ok ? $"boarded ({v.HeroesAboard} hero{(v.HeroesAboard == 1 ? "" : "es")} aboard)" : "NOT boarded")}";
                }
                string a = Ordered("jeep", Team.Neutral, out bool okA), b = Ordered("gun", Team.Neutral, out bool okB), c = Ordered("apc", Team.Red, out bool okC);
                sb.AppendLine(OzV11Kit.Verdict(okA && okB && okC, $"on the player's enter order: {a}; {b}; own troop carrier {c}"));
            }
            // ---------------------------------------------------------------- 187
            {
                var w = Ground(settings, 64);
                var brad = Squad(w, true, 10, 10); var alan = brad.Minions.FirstOrDefault();
                var grunt = Squad(w, false, 10, 20);
                bool ok = alan != null && alan.HeroMemberName == "alan" && alan.Grenades == 5 && brad.Grenades == 0 && brad.SquadGrenades == 5 && grunt.SquadGrenades == 0;
                sb.AppendLine(OzV11Kit.Verdict(ok, $"at the start: Alan {alan?.Grenades} grenades (wanted 5), Brad {brad.Grenades} (wanted 0), the panel's squad count {brad.SquadGrenades} (wanted 5); a grunt squad {grunt.SquadGrenades}; Alan throws by the {alan?.GrenadeRuleType}'s rule"));
            }
            var vsGrunts = Stage(settings, false, true); var vsTank = Stage(settings, true, true);
            var vsGruntsOff = Stage(settings, false, false);
            sb.AppendLine(OzV11Kit.Verdict(vsGrunts.alanNades == 0 && vsGrunts.alanShots > 0 && vsGrunts.bradNades == 0 && vsGrunts.sniperNades == 0 && vsGrunts.sniperShots > 0,
                $"staged 30 s against an unarmed grunt squad (grenade rule on): Alan threw {vsGrunts.alanNades} grenades and fired {vsGrunts.alanShots} shots, Brad threw {vsGrunts.bradNades}; " +
                $"a sniper squad with the same 5 grenades threw {vsGrunts.sniperNades} and fired {vsGrunts.sniperShots} (the sniper's rule: the gun against robots)"));
            sb.AppendLine(OzV11Kit.Verdict(vsTank.alanNades > 0 && vsTank.alanNades <= 5 && vsTank.bradNades == 0 && vsTank.sniperNades > 0 && vsTank.alanLeft == 5 - vsTank.alanNades && vsTank.panel == vsTank.alanLeft,
                $"staged 30 s against an unarmed crewed light tank: Alan threw {vsTank.alanNades} grenades (left {vsTank.alanLeft}, the panel {vsTank.panel}), Brad {vsTank.bradNades}; the sniper squad threw {vsTank.sniperNades} and fired {vsTank.sniperShots} (the sniper's rule: grenades at vehicles)"));
            sb.AppendLine(OzV11Kit.Verdict(vsGruntsOff.alanNades > 0 && vsGruntsOff.sniperNades > 0,
                $"the grenade rule off ('Zod exact' DV40 off): against the grunt squad Alan threw {vsGruntsOff.alanNades}, the sniper squad {vsGruntsOff.sniperNades} (both at anything, as Zod)"));
            {   // his grenades ride with him through an own troop carrier and come out with him
                var w = Ground(settings, 65);
                var brad = Squad(w, true, 10, 10); var alan = brad.Minions.FirstOrDefault();
                var apc = w.CreateObject("vehicle", "apc", 13 * 16, 10 * 16, Team.Red);
                Ticks(w, 21);
                OzV11Kit.PlayerEnter(w, brad, apc);
                Ticks(w, 30, () => apc.Passengers.Count > 0);
                int aboard = apc.Passengers.Count;
                bool ej = w.Eject(Team.Red, apc);
                Ticks(w, 1);
                var a2 = w.Objects.FirstOrDefault(o => o.IsHero && o.HeroMemberName == "alan" && OzV11Kit.Alive(o));
                var b2 = w.Objects.FirstOrDefault(o => o.IsHero && o.HeroMemberName == "brad" && OzV11Kit.Alive(o));
                bool ok = aboard >= 2 && ej && a2 != null && a2 != alan && a2.Grenades == 5 && b2 != null && b2.Grenades == 0 && b2.SquadGrenades == 5;
                sb.AppendLine(OzV11Kit.Verdict(ok, $"through an own troop carrier: {aboard} rode, EXIT {(ej ? "taken" : "refused")}; out again Alan {(a2 != null ? a2.Grenades.ToString() : "-")} grenades (wanted 5), Brad {(b2 != null ? b2.Grenades.ToString() : "-")}, the panel {(b2 != null ? b2.SquadGrenades.ToString() : "-")}"));
            }
            {   // Brad (the leader, no grenades of his own) picks up a crate, then dies: the pool passes to Alan on top of his own
                var w = Ground(settings, 68);
                var brad = Squad(w, true, 10, 10); var alan = brad.Minions.FirstOrDefault();
                var crate = w.CreateObject("map_item", "grenades", 14 * 16, 10 * 16, Team.Neutral);
                Ticks(w, 21);
                w.SendWaypoints(Team.Red, brad, new[] { new ZWaypoint { mode = WpMode.PickupGrenades, refId = crate.Id, x = crate.CenterX, y = crate.CenterY } });
                Ticks(w, 40, () => brad.Grenades > 0);
                int pool = brad.Grenades, own = alan != null ? alan.Grenades : -1;
                brad.SetHealth(0); w.UpdateObjectHealth(brad);
                Ticks(w, 1);
                var a2 = w.Objects.FirstOrDefault(o => o.IsHero && o.HeroMemberName == "alan" && OzV11Kit.Alive(o));
                bool ok = pool > 0 && own == 5 && a2 != null && !a2.IsMinion && a2.Grenades == own + pool && a2.SquadGrenades == own + pool;
                sb.AppendLine(OzV11Kit.Verdict(ok, $"Brad picks up a grenade crate ({pool} grenades in the squad's pool) and dies: Alan {(a2 == null ? "gone" : a2.IsMinion ? "still a member" : "leads")} with {(a2 != null ? a2.Grenades.ToString() : "-")} grenades (wanted his own {own} + the pool {pool} = {own + pool}), the panel {(a2 != null ? a2.SquadGrenades.ToString() : "-")}"));
            }
            return sb.ToString();
        }

        /// <summary>A staged fight: the hero squad and a sniper squad (5 grenades, as Alan) side by side attack an unarmed, unkillable
        /// blue target (a grunt squad, or a crewed light tank) for 30 s from the order. Counts per thrower: grenades thrown at it, other shots.</summary>
        static (int alanNades, int alanShots, int bradNades, int sniperNades, int sniperShots, int alanLeft, int panel) Stage(string settings, bool tank, bool rule)
        {
            bool keep = ZWorld.GrenadeRuleDeviation; ZWorld.GrenadeRuleDeviation = rule;
            try
            {
                var w = Ground(settings, tank ? 66 : 67);
                var brad = Squad(w, true, 10, 12); var alan = brad.Minions.First();
                var sn = w.CreateRobotGroup("sniper", 10 * 16, 24 * 16, Team.Red);
                ZObject target = tank ? w.CreateObject("vehicle", "light", 24 * 16, 18 * 16, Team.Blue) : w.CreateRobotGroup("grunt", 24 * 16, 18 * 16, Team.Blue);   // out of every range while the calm start runs
                var blues = new List<ZObject> { target }; blues.AddRange(target.Minions);
                foreach (var b in blues) { b.Damage = 0; b.MaxHealth = 1000000; b.SetHealth(b.MaxHealth); b.MoveSpeed = 0; }
                var snIds = new HashSet<int> { sn.Id }; foreach (var m in sn.Minions) snIds.Add(m.Id);
                var blueIds = new HashSet<int>(blues.Select(b => b.Id));
                Ticks(w, 21);
                sn.Grenades = 5;                                                                     // the sniper squad gets Alan's 5 when the stage starts
                OzV11Kit.PlayerAttack(w, brad, target); OzV11Kit.PlayerAttack(w, sn, target);
                int an = 0, ash = 0, bn = 0, snn = 0, sns = 0;
                double end = w.Time + 30;
                while (w.Time < end)
                {
                    w.Tick(ZConst.TICK);
                    foreach (var e in w.Events)
                    {
                        if (!blueIds.Contains(e.otherId)) continue;
                        bool nade = e.type == ZEventType.MissileFired && e.flag, shot = !nade && (e.type == ZEventType.MissileFired || e.type == ZEventType.Hitscan);
                        if (e.objId == alan.Id) { if (nade) an++; else if (shot) ash++; }
                        else if (e.objId == brad.Id) { if (nade) bn++; }
                        else if (snIds.Contains(e.objId)) { if (nade) snn++; else if (shot) sns++; }
                    }
                    w.Events.Clear();
                }
                return (an, ash, bn, snn, sns, alan.Grenades, brad.SquadGrenades);
            }
            finally { ZWorld.GrenadeRuleDeviation = keep; }
        }

#if UNITY_EDITOR
        [UnityEditor.MenuItem("OpenZ/V1.1/Hero Squad: KH Count (boarding, grenades)")]
        static void Menu() => OzV11Kit.RunMenu("Hero squad: boarding on order, Alan's grenades (DV108, KH)", () => Check(OzV11Kit.SettingsText));
#endif
    }
}
