using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// feature table. Every rule a grunt squad has, run headless (ZWorld.Create + Tick on plain ground, nothing saved) once for a red
    /// grunt squad and once for the ALAN &amp; BRAD squad in the same world, side by side: orders (move, attack-move, attack a unit,
    /// what they may attack, hold position, stop, patrol), every vehicle kind (in and driven), every gun kind (manned, EXIT), the own
    /// troop carrier, a vehicle and a troop carrier destroyed with them inside, a flag, an empty gun taken by themselves, the enemy
    /// fort, a repair of their vehicle, healing, kills credited, death and the hand-over, the grenade crate, the unit count, shallow
    /// and deep water, the squad help call (answering one and calling one). Then the rows that only the running game shows (the
    /// transmission, ragdoll and parts, the antics, crocodiles, the end screen, saving. Menu OpenZ/V1.1/Hero Squad: Feature Table.
    /// </summary>
    public static class OzV11HeroFeatures
    {
        /// <summary>One headless run: a pass flag and what happened (one cell).</summary>
        public struct Cell { public bool ok; public string what; public Cell(bool ok, string what) { this.ok = ok; this.what = what; } }

        static string Settings;
        static ZWorld W(string map, int seed)
        {
            var w = OzV11Kit.World(Settings, map, seed);
            OzV11Kit.Dummy(w, 62 * 16 + 8, 38 * 16 + 8);                                                // blue keeps a unit: no game over, the blue fort stands
            return w;
        }
        static string Ground(params (string kind, int x, int y, string owner)[] objects) => OzV11Kit.Plain(64, 40, objects);

        static ZObject Squad(ZWorld w, bool hero, int tx, int ty, Team team = Team.Red) =>
            hero ? w.CreateHeroSquad(ZHeroSquads.AlanBrad, team, new[] { tx, ty, tx, ty + 2 }) : w.CreateRobotGroup("grunt", tx * 16, ty * 16, team);

        static int Ticks(ZWorld w, double secs, Func<bool> until = null)
        {
            int n = (int)Math.Round(secs / ZConst.TICK), i = 0;
            for (; i < n; i++) { w.Tick(ZConst.TICK); w.Events.Clear(); if (until != null && until()) break; }
            return i;
        }
        static double Secs(int ticks) => ticks * ZConst.TICK;
        static List<ZObject> Body(ZObject l) => OzV11Kit.Squad(l).ToList();
        static int Px(int tile) => tile * 16 + 8;
        static ZObject Enemy(ZWorld w, int tx, int ty, bool harmless = true, bool still = true)
        {
            var e = w.CreateRobotGroup("grunt", tx * 16, ty * 16, Team.Blue, 1);
            if (harmless) e.Damage = 0; if (still) e.MoveSpeed = 0;
            return e;
        }
        static string Crew(ZObject v, bool hero) => hero
            ? $"crew line \"{OzHeroPair.CrewName(v, false) ?? "-"}\"{(v.Passengers.Count > 0 ? $", passengers \"{OzHeroPair.CrewName(v, true) ?? "-"}\"" : "")}"
            : $"{v.Drivers.Count} driver(s){(v.Passengers.Count > 0 ? $", {v.Passengers.Count} passenger(s)" : "")}";
        static ZObject Hero(ZWorld w, string member) => w.Objects.FirstOrDefault(o => o.IsHero && o.HeroMemberName == member && OzV11Kit.Alive(o));

        // ------------------------------------------------------------------ the rows
        static readonly List<(string feature, Func<bool, Cell> run, string note)> Rows = new List<(string, Func<bool, Cell>, string)>
        {
            ("move order (42 tiles)", hero =>
            {
                var w = W(Ground(), 101); var s = Squad(w, hero, 8, 20);
                OzV11Kit.PlayerMove(w, s, Px(50), Px(20), false);
                int n = Ticks(w, 90, () => Body(s).All(m => Math.Abs(m.CenterX - Px(50)) < 48 && Math.Abs(m.CenterY - Px(20)) < 48 && !m.IsMoving));
                bool ok = Body(s).All(m => Math.Abs(m.CenterX - Px(50)) < 48);
                return new Cell(ok, ok ? $"all there in {Secs(n):0.0} s" : "NOT there");
            }, "the same walk speed (a grunt's)"),
            ("attack-move past an enemy", hero =>
            {
                var w = W(Ground(), 102); var s = Squad(w, hero, 8, 20); var e = Enemy(w, 30, 21);
                OzV11Kit.PlayerMove(w, s, Px(52), Px(20), true);
                int n = Ticks(w, 90, () => e.Destroyed);
                return new Cell(e.Destroyed, e.Destroyed ? $"stopped to fight, enemy dead after {Secs(n):0.0} s" : "the enemy lives");
            }, null),
            ("attack a unit (an enemy jeep)", hero =>
            {
                var w = W(Ground(), 103); var s = Squad(w, hero, 10, 20); var v = w.CreateObject("vehicle", "jeep", 22 * 16, 20 * 16, Team.Blue);
                v.Damage = 0; v.MoveSpeed = 0; int hp0 = v.Health;
                OzV11Kit.PlayerAttack(w, s, v);
                int n = Ticks(w, 60, () => v.Health < hp0 || v.Destroyed || v.Owner != Team.Blue);
                bool ok = v.Health < hp0 || v.Destroyed || v.Owner != Team.Blue;
                return new Cell(ok, ok ? $"hit it after {Secs(n):0.0} s ({(v.Destroyed ? "destroyed" : v.Owner != Team.Blue ? "driver sniped" : $"{hp0 - v.Health} HP off")})" : "no hit");
            }, "their strong shot takes more off (DV108)"),
            ("what they may attack", hero =>
            {
                var w = W(Ground(("building:robot_factory", 40, 4, "blue"), ("map_item:hut", 30, 30, "null"), ("map_item:rock", 34, 30, "null"), ("map_item:flag", 40, 30, "null"), ("map_item:grenades", 44, 30, "null")), 104);
                var s = Squad(w, hero, 10, 20);
                var targets = new List<ZObject> { Enemy(w, 20, 10), w.CreateObject("vehicle", "jeep", 24 * 16, 10 * 16, Team.Blue), w.CreateObject("cannon", "gun", 28 * 16, 10 * 16, Team.Blue),
                                                  w.CreateObject("vehicle", "jeep", 24 * 16, 26 * 16, Team.Neutral), w.CreateRobotGroup("grunt", 16 * 16, 30 * 16, Team.Red, 1) };
                targets.AddRange(w.Objects.Where(o => o.Kind == ZKind.Building || o.Kind == ZKind.MapItem));
                var yes = targets.Where(t => s.CanAttackObject(t)).Select(t => $"{t.Owner.ToString().ToLowerInvariant()} {t.Type}").ToList();
                var no = targets.Where(t => !s.CanAttackObject(t)).Select(t => $"{t.Owner.ToString().ToLowerInvariant()} {t.Type}").ToList();
                return new Cell(true, $"may: {string.Join(", ", yes)}; not: {string.Join(", ", no)}");
            }, "PASS when both lists are the same"),
            ("hold position (enemy in range)", hero =>
            {
                var w = W(Ground(), 105); var s = Squad(w, hero, 20, 20); var e = Enemy(w, 26, 20); int x0 = s.CenterX, y0 = s.CenterY, hp0 = e.Health;
                w.SendHold(Team.Red, s);
                Ticks(w, 10);
                double moved = Math.Sqrt(Math.Pow(s.CenterX - x0, 2) + Math.Pow(s.CenterY - y0, 2));
                bool ok = s.HoldPosition && moved <= 16 && (e.Health < hp0 || e.Destroyed);
                return new Cell(ok, $"moved {moved / 16:0.0} tiles, fired from the spot: {(e.Health < hp0 || e.Destroyed ? "yes" : "NO")}");
            }, null),
            ("stop", hero =>
            {
                var w = W(Ground(), 106); var s = Squad(w, hero, 8, 20);
                OzV11Kit.PlayerMove(w, s, Px(50), Px(20), false); Ticks(w, 2);
                w.SendStop(Team.Red, s); Ticks(w, 1.5);
                bool ok = s.Waypoints.Count == 0 && Body(s).All(m => !m.IsMoving);
                return new Cell(ok, ok ? $"stopped at tile {s.CenterX / 16}" : "still going");
            }, null),
            ("patrol", hero =>
            {
                var w = W(Ground(), 107); var s = Squad(w, hero, 10, 20);
                bool sent = w.SendPatrol(Team.Red, s, Px(30), Px(20)); bool atB = false, backA = false;
                Ticks(w, 70, () => { if (Math.Abs(s.CenterX - Px(30)) < 32) atB = true; if (atB && Math.Abs(s.CenterX - Px(10)) < 32) backA = true; return backA; });
                return new Cell(sent && atB && backA && s.Patrolling, $"legs {s.PatrolLegs}, reached the far end {(atB ? "yes" : "NO")}, back {(backA ? "yes" : "NO")}");
            }, null),
            ("own troop carrier: ride and EXIT", hero =>
            {
                var w = W(Ground(), 108); var s = Squad(w, hero, 10, 20); int n0 = Body(s).Count;
                var v = w.CreateObject("vehicle", "apc", 13 * 16, 20 * 16, Team.Red);
                OzV11Kit.PlayerEnter(w, s, v); Ticks(w, 30, () => v.Passengers.Count > 0);
                int rode = v.Passengers.Count; string crew = Crew(v, hero);
                bool ej = w.Eject(Team.Red, v); Ticks(w, 0.2);
                int outN = w.Objects.Count(o => o.IsRobot && o.Owner == Team.Red && OzV11Kit.Alive(o) && o.Type == "grunt" && (!hero || o.IsHero));
                bool ok = rode == n0 && ej && (hero ? Hero(w, "brad") != null && Hero(w, "alan")?.Leader == Hero(w, "brad") : outN >= n0);
                return new Cell(ok, $"{rode} of {n0} rode ({crew}); EXIT: {(hero ? (ok ? "Brad leads, Alan behind" : "NOT as themselves") : $"{outN} out")}");
            }, null),
            ("a vehicle destroyed with them inside", hero =>
            {
                var w = W(Ground(), 109); var s = Squad(w, hero, 10, 20); int n0 = Body(s).Count;
                var v = w.CreateObject("vehicle", "jeep", 13 * 16, 20 * 16, Team.Neutral);
                OzV11Kit.PlayerEnter(w, s, v); Ticks(w, 30, () => v.Owner == Team.Red && v.Drivers.Count > 0);
                int inside = v.Drivers.Count + v.Passengers.Count;
                v.SetHealth(0); w.UpdateObjectHealth(v); Ticks(w, 1);
                int left = w.Objects.Count(o => o.IsRobot && o.Owner == Team.Red && OzV11Kit.Alive(o));
                return new Cell(left == n0 - inside, $"{inside} inside lost with it, {left} outside live");
            }, "Zod: the crew dies with the vehicle"),
            ("a troop carrier destroyed with them aboard", hero =>
            {
                var w = W(Ground(), 110); var s = Squad(w, hero, 10, 20); int n0 = Body(s).Count;
                var v = w.CreateObject("vehicle", "apc", 13 * 16, 20 * 16, Team.Red);
                OzV11Kit.PlayerEnter(w, s, v); Ticks(w, 30, () => v.Passengers.Count > 0);
                int aboard = v.Passengers.Count;
                v.SetHealth(0); w.UpdateObjectHealth(v); Ticks(w, 1);
                int alive = w.Objects.Count(o => o.IsRobot && o.Owner == Team.Red && OzV11Kit.Alive(o) && (!hero || o.IsHero));
                return new Cell(aboard == n0, $"{aboard} aboard; after: {alive} of the squad alive");
            }, "PASS when both lose the same share"),
            ("capture a flag", hero =>
            {
                var w = W(Ground(("map_item:flag", 30, 20, "null")), 111); var s = Squad(w, hero, 10, 20);
                var f = w.Flags.FirstOrDefault();
                if (f == null) return new Cell(false, "no flag on the test map");
                OzV11Kit.PlayerMove(w, s, f.CenterX, f.CenterY, false);
                int n = Ticks(w, 60, () => f.Owner == Team.Red);
                return new Cell(f.Owner == Team.Red, f.Owner == Team.Red ? $"zone taken after {Secs(n):0.0} s" : "NOT taken");
            }, null),
            ("take an empty gun by themselves (idle)", hero =>
            {
                var w = W(Ground(), 112); var s = Squad(w, hero, 10, 20); var v = w.CreateObject("cannon", "gun", 13 * 16, 20 * 16, Team.Neutral);
                Ticks(w, 30, () => v.Owner == Team.Red);
                return new Cell(true, v.Owner == Team.Red ? $"taken ({Crew(v, hero)})" : "not taken");
            }, "PASS when the grunts take it and the heroes do not (KH, item 186: heroes board only on the player's order)"),
            ("collect a grenade crate", hero =>
            {
                var w = W(Ground(("map_item:grenades", 16, 20, "null")), 113); var s = Squad(w, hero, 10, 20);
                var c = w.Objects.FirstOrDefault(o => o.Type == "grenades");
                if (c == null) return new Cell(false, "no crate on the test map");
                w.SendWaypoints(Team.Red, s, new[] { new ZWaypoint { mode = WpMode.PickupGrenades, refId = c.Id, x = c.CenterX, y = c.CenterY } });
                int n = Ticks(w, 40, () => s.Grenades > 0);
                return new Cell(s.Grenades > 0, s.Grenades > 0 ? $"{s.Grenades} grenades after {Secs(n):0.0} s" : "none");
            }, null),
            ("enter the enemy fort", hero =>
            {
                var w = W(Ground(("building:fort_front", 26, 2, "blue")), 114); var s = Squad(w, hero, 31, 26);
                var f = w.Objects.FirstOrDefault(o => o.IsFort);
                if (f == null) return new Cell(false, "no fort on the test map");
                w.SendWaypoints(Team.Red, s, new[] { new ZWaypoint { mode = WpMode.EnterFort, refId = f.Id, x = f.CenterX, y = f.CenterY } });
                int n = Ticks(w, 120, () => f.Destroyed || w.GameOver);
                bool ok = f.Destroyed || (w.GameOver && w.Winner == Team.Red);
                return new Cell(ok, ok ? $"in after {Secs(n):0.0} s: fort {(f.Destroyed ? "destroyed" : "standing")}, {(w.GameOver ? "won by " + w.Winner : "the game runs")}" : "NOT in");
            }, null),
            ("their vehicle repaired at a repair station", hero =>
            {
                var w = W(Ground(("building:repair", 30, 8, "red")), 115); var s = Squad(w, hero, 10, 20);
                var st = w.Objects.FirstOrDefault(o => o.Type == "repair");
                var v = w.CreateObject("vehicle", "jeep", 13 * 16, 20 * 16, Team.Neutral);
                OzV11Kit.PlayerEnter(w, s, v); Ticks(w, 30, () => v.Owner == Team.Red && v.Drivers.Count > 0);
                if (st == null || v.Owner != Team.Red) return new Cell(false, st == null ? "no station on the test map" : "they did not get in");
                string before = Crew(v, hero);
                v.Health = v.MaxHealth / 2;
                w.SendWaypoints(Team.Red, v, new[] { new ZWaypoint { mode = WpMode.UnitRepair, refId = st.Id, x = st.CenterX, y = st.CenterY } });
                ZObject fresh = null;
                int n = Ticks(w, 120, () => (fresh = w.Objects.FirstOrDefault(o => o.Type == "jeep" && o.Owner == Team.Red && OzV11Kit.Alive(o) && o.Health == o.MaxHealth && o.Drivers.Count > 0)) != null);
                bool ok = fresh != null && (!hero || OzHeroPair.CrewName(fresh, false) == "BRAD");
                return new Cell(ok, ok ? $"repaired after {Secs(n):0.0} s; {before} before, {Crew(fresh, hero)} after" : "NOT repaired");
            }, null),
            ("healed / repaired themselves", hero =>
            {
                var w = W(Ground(), 116); var s = Squad(w, hero, 10, 20); s.Health = s.MaxHealth / 2;
                return new Cell(!s.CanBeRepaired(), s.CanBeRepaired() ? "can be repaired" : "no: robots are never healed or repaired in Z (vehicles only)");
            }, "the same rule for both"),
            ("kills credited (rank, high score)", hero =>
            {
                var w = W(Ground(), 117); var s = Squad(w, hero, 10, 20); var e = Enemy(w, 16, 20);
                OzV11Kit.PlayerAttack(w, s, e); Ticks(w, 60, () => e.Destroyed); Ticks(w, 0.2);
                return new Cell(e.Destroyed && s.Kills >= 1, $"enemy {(e.Destroyed ? "dead" : "alive")}, kills on the squad {s.Kills}");
            }, null),
            ("death and the squad's hand-over", hero =>
            {
                var w = W(Ground(), 118); var s = Squad(w, hero, 10, 20); var next = s.Minions[0];
                s.SetHealth(0); w.UpdateObjectHealth(s); Ticks(w, 0.2);
                bool ok = s.Destroyed && s.Successor == next && next.Leader == null && next.Minions.Count == Body(next).Count - 1;
                return new Cell(ok, ok ? $"leader dead, #{next.Id}{(hero ? " (Alan)" : "")} leads {Body(next).Count}" : "NO hand-over");
            }, null),
            ("unit count (the cap, the force counts)", hero =>
            {
                var w = W(Ground(), 119); int n0 = w.UnitCount(Team.Red);
                var s = hero ? Squad(w, true, 10, 20) : w.CreateRobotGroup("grunt", 10 * 16, 20 * 16, Team.Red, 2);
                int n1 = w.UnitCount(Team.Red);
                return new Cell(n1 - n0 == 2, $"+{n1 - n0} for {Body(s).Count} robots");
            }, "one per robot, as any squad"),
            ("shallow water (wade across)", hero =>
            {
                var w = W(Strip(2), 120); var s = Squad(w, hero, 8, 20);
                OzV11Kit.PlayerMove(w, s, Px(50), Px(20), false);
                Ticks(w, 120, () => Math.Abs(s.CenterX - Px(50)) < 32 && !s.IsMoving);
                return new Cell(true, $"leader ends at tile {s.CenterX / 16}");
            }, "PASS when both end at the same tile (+-2)"),
            ("deep water (no way through)", hero =>
            {
                var w = W(Strip(4), 121); var s = Squad(w, hero, 8, 20);
                OzV11Kit.PlayerMove(w, s, Px(50), Px(20), false);
                Ticks(w, 120, () => Math.Abs(s.CenterX - Px(50)) < 32 && !s.IsMoving);
                return new Cell(true, $"leader ends at tile {s.CenterX / 16}");
            }, "PASS when both end at the same tile (+-2)"),
            ("answer a squad's help call (DV53)", hero =>
            {
                var w = W(Ground(), 122); var helper = Squad(w, hero, 16, 20); var victim = w.CreateRobotGroup("grunt", 24 * 16, 20 * 16, Team.Red, 1);
                var a = w.CreateRobotGroup("grunt", 31 * 16, 20 * 16, Team.Blue, 1); a.MoveSpeed = 0; victim.MoveSpeed = 0; victim.Damage = 0; victim.MaxHealth = victim.Health = 1000000;
                Ticks(w, ZWorld.CalmStartSec + 1);                                                       // past the calm start (DV69: no walking off to help in the first 20 s)
                OzV11Kit.CpuAttack(w, a, victim);
                bool came = false;
                Ticks(w, 20, () => came = helper.AttackObject == a || (helper.Waypoints.Count > 0 && helper.Waypoints[0].refId == a.Id) || a.Destroyed);
                return new Cell(came, came ? (a.Destroyed ? "came and killed the attacker" : "came to help") : "did NOT come");
            }, null),
            ("their help call is answered", hero =>
            {
                var w = W(Ground(), 123); var helper = w.CreateRobotGroup("grunt", 16 * 16, 20 * 16, Team.Red); var victim = Squad(w, hero, 24, 20);
                foreach (var m in Body(victim)) { m.MoveSpeed = 0; m.Damage = 0; m.MaxHealth = m.Health = 1000000; }
                var a = w.CreateRobotGroup("grunt", 31 * 16, 20 * 16, Team.Blue, 1); a.MoveSpeed = 0;
                Ticks(w, ZWorld.CalmStartSec + 1);                                                       // past the calm start (DV69: no walking off to help in the first 20 s)
                OzV11Kit.CpuAttack(w, a, victim);
                bool came = false;
                Ticks(w, 20, () => came = helper.AttackObject == a || (helper.Waypoints.Count > 0 && helper.Waypoints[0].refId == a.Id) || a.Destroyed);
                return new Cell(came, came ? "a grunt squad came to help" : "nobody came");
            }, null),
        };

        /// <summary>Plain ground 64 x 40 tiles with a north-south strip of class <paramref name="cls"/> (2 shallow water, 4 deep) at
        /// columns 28-31.</summary>
        static string Strip(int cls)
        {
            var g = OzV11Kit.Plain(64, 40);
            var cl = new StringBuilder();
            for (int y = 0; y < 40; y++) for (int x = 0; x < 64; x++) cl.Append(x + y > 0 ? "," : "").Append(x >= 28 && x <= 31 ? cls : 0);
            int a = g.IndexOf("\"classes\":[", StringComparison.Ordinal) + 11, b = g.IndexOf(']', a);
            return g.Substring(0, a) + cl + g.Substring(b);
        }

        static IEnumerable<(string feature, Func<bool, Cell> run, string note)> VehicleRows()
        {
            foreach (var type in new[] { "jeep", "light", "medium", "heavy", "missile_launcher", "crane", "apc" })
                yield return ($"{type}: in by order and driven", hero =>
                {
                    var w = W(Ground(), 130 + type.Length); var s = Squad(w, hero, 10, 20);
                    var v = w.CreateObject("vehicle", type, 13 * 16, 20 * 16, Team.Neutral);
                    OzV11Kit.PlayerEnter(w, s, v); Ticks(w, 30, () => v.Owner == Team.Red && v.Drivers.Count > 0 && (type != "apc" || v.Passengers.Count > 0 || Body(s).Count == 1));
                    if (v.Owner != Team.Red) return new Cell(false, "did NOT get in");
                    string crew = Crew(v, hero); int x0 = v.CenterX;
                    OzV11Kit.PlayerMove(w, v, Px(44), Px(20), false); Ticks(w, 60, () => Math.Abs(v.CenterX - Px(44)) < 48 && !v.IsMoving);
                    int drove = (v.CenterX - x0) / 16;
                    bool named = !hero || (OzHeroPair.CrewName(v, false) ?? "").StartsWith("BRAD");
                    return new Cell(drove >= 20 && named, $"{v.Drivers.Count + v.Passengers.Count} aboard, {crew}; drove {drove} tiles");
                }, null);
            foreach (var type in ZBuildLists.Cannons)
                yield return ($"{type}: manned by order, EXIT", hero =>
                {
                    var w = W(Ground(), 150 + type.Length); var s = Squad(w, hero, 10, 20);
                    var v = w.CreateObject("cannon", type, 13 * 16, 20 * 16, Team.Neutral);
                    OzV11Kit.PlayerEnter(w, s, v); Ticks(w, 30, () => v.Owner == Team.Red && v.Drivers.Count > 0);
                    if (v.Owner != Team.Red) return new Cell(false, "did NOT get in");
                    string crew = Crew(v, hero); int n = v.Drivers.Count;
                    bool ej = w.Eject(Team.Red, v); Ticks(w, 0.2);
                    var brad = hero ? Hero(w, "brad") : null;
                    bool outOk = ej && v.Drivers.Count == 0 && (!hero || (brad != null && brad.MaxHealth == brad.U.MaxHealth * 3));
                    return new Cell(outOk, $"{n} in, {crew}; EXIT: {(ej ? hero ? brad != null ? "Brad out as himself" : "NOT Brad" : "out" : "refused")}");
                }, null);
        }

        public static readonly (string feature, string grunt, string check)[] PlayRows =
        {
            ("select (click on any of them)", "the squad", "click Brad, click Alan, click his head: the squad selected, both rings, the panel names them"),
            ("box-select", "the squad", "a box over them (and over them with grunts): they are in the selection"),
            ("double-click", "every grunt squad on screen", "FIXED in this kit (PlayerInput): a double-click on a hero selects the hero squad(s) on screen only, a double-click on a grunt no longer picks them up (they are their own kind, as on the end screen)"),
            ("groups (Ctrl + number, number, again = fly there)", "kept and recalled", "Ctrl+1 on them, select others, 1: they come back; 1 again: the camera flies to them; after one boards a gun, 1 recalls the one outside"),
            ("selection after one gets in a vehicle / gun / troop carrier", "the rest of the squad stays selected", "FIXED in this kit (PlayerInput follows the squad's next member, SelectionOverlay / OpenZUnitOverlay draw nothing for a robot that left the map): report line 'selection after boarding ... still selected after removal 0, lost 0'"),
            ("the unit panel (name, picture, health, weapon, kills)", "GRUNT, the grunt picture", "together: ALAN & BRAD + the pair's picture; apart: ALAN / BRAD + his own picture (report line 'pair:'); health 3 x a grunt's, the rifle, kills and the star"),
            ("the minimap", "a red dot per robot", "their two dots, red, move with them"),
            ("the HUD counters and force counts", "+3 robots", "+2 robots for them (the headless 'unit count' row); the counter changes by 2 when they arrive / die"),
            ("the help call on the HUD", "the transmission with the squad's name", "the transmission names ALAN & BRAD (or the one left) and shows his picture"),
            ("die: ragdoll, parts, the dead colour", "the grunt's ragdoll", "kill each one: a ragdoll at their size, parts fly, the dead colour on their model (not a grunt's body)"),
            ("antics", "the antic list (sunbed, phone, fishing, drink ...)", "only the drink, every 30 s of idle with no enemy near, the level-start lounger lie kept (report line 'hero antics: ... other antics on them 0')"),
            ("their voices and shots", "the grunt lines and bolts", "their own select / move / attack / boarding lines, hero_gun_1-3 (P4, P5)"),
            ("crocodiles (Level 16 river)", "a robot in the water can be taken", "by the code the same: a crocodile takes any robot on the water (ZWorld.Crocodiles CrocPreyOk: Kind == Robot); Play: wade them into the croc river once"),
            ("the end screen / high score", "the team's face is its most numerous robot kind", "their kills and losses count as the team's; if they are the most numerous kind the face is ALAN & BRAD"),
            ("saving / loading", "-", "n/a: the game has no save or load"),
            ("the 'Zod exact' setting", "-", "off = no hero squad on the map (rule check row 'level 16 extra squad ... switch off: no hero')"),
        };

        public static string Table(string settings)
        {
            Settings = settings;
            var sb = new StringBuilder();
            sb.AppendLine("ALAN & BRAD against a grunt squad, every standard feature (headless: the same world once with a red grunt squad, once with the hero squad)");
            sb.AppendLine("| feature | grunt squad | ALAN & BRAD | result |");
            sb.AppendLine("|---|---|---|---|");
            int pass = 0, n = 0;
            foreach (var (feature, run, note) in Rows.Concat(VehicleRows()))
            {
                Cell g, h;
                try { g = run(false); } catch (Exception e) { g = new Cell(false, "EXCEPTION " + e.GetType().Name + ": " + e.Message.Split('\n')[0]); }
                try { h = run(true); } catch (Exception e) { h = new Cell(false, "EXCEPTION " + e.GetType().Name + ": " + e.Message.Split('\n')[0]); }
                bool ok = g.ok && h.ok;
                if (note != null && note.StartsWith("PASS when both lists are the same")) ok = g.what == h.what;
                else if (note != null && note.StartsWith("PASS when both end at the same tile")) ok = SameTile(g.what, h.what);
                else if (note != null && note.StartsWith("PASS when both lose the same share")) ok = g.ok && h.ok && Tail(g.what) == "0" == (Tail(h.what) == "0");
                else if (note != null && note.StartsWith("PASS when both do the same")) ok = g.what.StartsWith("taken") == h.what.StartsWith("taken");
                else if (note != null && note.StartsWith("PASS when the grunts take it and the heroes do not")) ok = g.what.StartsWith("taken") && !h.what.StartsWith("taken");   // KH
                n++; if (ok) pass++;
                sb.AppendLine($"| {feature} | {g.what} | {h.what} | {(ok ? "PASS" : "FAIL")}{(note != null && !note.StartsWith("PASS when") ? " (" + note + ")" : "")} |");
            }
            sb.AppendLine();
            sb.AppendLine(OzV11Kit.Verdict(pass == n, $"feature table: {pass} of {n} headless rows the same for ALAN & BRAD as for a grunt squad"));
            sb.AppendLine();
            sb.AppendLine("Play checks (the running game only):");
            sb.AppendLine("| feature | grunt squad | ALAN & BRAD: what to check |");
            sb.AppendLine("|---|---|---|");
            foreach (var (f, gr, c) in PlayRows) sb.AppendLine($"| {f} | {gr} | {c} |");
            return sb.ToString();
        }

        static string Tail(string s) { int i = s.LastIndexOf(':'); var t = i >= 0 ? s.Substring(i + 1).Trim() : s; return t.Split(' ')[0]; }

        static bool SameTile(string a, string b)
        {
            int Num(string s) { var p = s.Split(' '); return int.TryParse(p[p.Length - 1], out int v) ? v : -999; }
            return Math.Abs(Num(a) - Num(b)) <= 2;
        }

#if UNITY_EDITOR
        [UnityEditor.MenuItem("OpenZ/V1.1/Hero Squad: Feature Table")]
        static void MenuTable() => OzV11Kit.RunMenu("Hero squad feature table (DV108, KC4b)", () => Table(OzV11Kit.SettingsText));   // RunMenu sets the game's switches
#endif
    }
}
