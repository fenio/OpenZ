#if !OPENZ_HARNESS
using System;
using System.Collections.Generic;
using System.Linq;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>The Deviations row DV113 "Ctrl: Pyros burn buildings and bridges" (the rule: Sim/ZObject.PyroForceBurn.cs), measured
    /// headless on the level 2 map without its start units (forts, buildings, bridges and terrain kept). Every case is its own world:
    /// one squad (four Pyros, three grunts) placed 6 to 10 tiles (one case: 16 to 20) from the target on free ground, one attack order on the target sent the way the player's
    /// input sends it (SEND_WAYPOINTS), with or without the Ctrl mark, then the world runs (90 s at most when the order is taken, 30 s
    /// when it is refused).</summary>
    public static partial class OpenZParity
    {
        static void AddPyroForceBurnDeviation(List<Deviation> list)
        {
            try { list.Add(DevPyroForceBurn()); }
            catch (Exception ex) { list.Add(new Deviation { id = "DV113", change = "DevPyroForceBurn", zod = "-", openz = "-", measured = "probe threw " + ex.GetType().Name + ": " + ex.Message, toggle = "-", status = DevCheck }); }
        }

        /// <summary>One case's numbers: the order taken; the squad's hits on the target, its first hit and its fall (s, -1 = never);
        /// the target's health before and after; squad members lost; hits after a new order (the stop case).</summary>
        internal sealed class PfbResult
        {
            public bool placed, accepted; public int squad, hits, hitsAfterStop = -1, lost, hp0, hp1; public double firstHit = -1, fell = -1;
            public string Text(string target) =>
                !placed ? "no free spot" :
                $"{(accepted ? "taken" : "refused")}, {hits} hits{(firstHit >= 0 ? $" (first at {firstHit:0.0} s)" : "")}, {target} {hp0} -> {hp1} HP" +
                $"{(fell >= 0 ? $", down at {fell:0.0} s" : "")}, {lost} of {squad} lost{(hitsAfterStop >= 0 ? $", {hitsAfterStop} hits after the new order" : "")}";
        }

        /// <summary>The level 2 map without its robots, vehicles and guns.</summary>
        internal static string PfbNoUnits(string mapJson)
        {
            var m = ZMapData.FromJson(mapJson);
            m.objects.RemoveAll(o => { string c = o.kind.Split(':')[0]; return c == "robot" || c == "vehicle" || c == "cannon"; });
            return UnityEngine.JsonUtility.ToJson(m);
        }

        /// <summary>A free spot for a squad: robots can walk the 5 x 5 tiles round it and no building (no bridge deck either) covers them.</summary>
        static bool PfbFree(ZWorld w, int tx, int ty)
        {
            for (int i = -2; i <= 2; i++)
                for (int j = -2; j <= 2; j++)
                {
                    if (!w.Path.TilePassable(tx + i, ty + j, true)) return false;
                    int x = (tx + i) * 16 + 8, y = (ty + j) * 16 + 8;
                    foreach (var o in w.Objects) if (o.Kind == ZKind.Building && o.Contains(x, y)) return false;
                }
            return true;
        }

        static bool PfbSpot(ZWorld w, ZObject t, int ring, out int px, out int py)
        {
            int cx = t.CenterX >> 4, cy = t.CenterY >> 4;
            for (int r = ring; r <= ring + 4; r++)
                for (int k = 0; k < 16; k++)
                {
                    double a = k * Math.PI / 8;
                    int tx = cx + (int)Math.Round(r * Math.Cos(a)), ty = cy + (int)Math.Round(r * Math.Sin(a));
                    if (!PfbFree(w, tx, ty)) continue;
                    px = tx * 16; py = ty * 16;
                    return true;
                }
            px = py = 0;
            return false;
        }

        /// <summary>One case. target: "factory" (the neutral robot factory at tile 61,8, far from both forts), "enemy" (the red team's
        /// robot factory at tile 61,71, beside the red fort: the attacking squad cannot die there, the fort fights back) or "bridge"
        /// (the neutral bridge at tile 41,44); the attackers are a squad of <paramref name="unitType"/> of <paramref name="team"/> (both
        /// teams human unless <paramref name="cpu"/>: then the attacker's team is played by the CPU). force = the Ctrl mark on the
        /// order; rule = the DV113 switch for this case; stopAfter = 3 s after the first hit the squad gets a move order 6 tiles back.</summary>
        internal static PfbResult PfbCase(string target, Team team, string unitType, bool force, bool cpu, bool rule, bool stopAfter, int seed, int ring = 6)
        {
            bool was = ZWorld.PyroForceBurnDeviation;
            ZWorld.PyroForceBurnDeviation = rule;
            var r = new PfbResult();
            try
            {
                var w = ZWorld.Create(SettingsText, PfbNoUnits(MapText("L02")), seed);
                w.Bots.Clear();
                foreach (var (kt, kx) in new[] { (Team.Red, 16), (Team.Blue, (w.Map.width - 2) * 16) })
                {   // one red and one blue grunt in far corners that cannot die and do no harm: neither side ever has no units left
                    var d = w.CreateObject("robot", "grunt", kx, 16, kt);
                    d.MaxHealth = d.Health = 1000000000; d.Damage = 0;
                }
                if (cpu) ZBot.Attach(w, team == Team.Red ? Team.Blue : Team.Red, false, seed);
                int ox = target == "bridge" ? 41 : 61, oy = target == "bridge" ? 44 : target == "enemy" ? 71 : 8;
                var t = w.Objects.First(o => o.Kind == ZKind.Building && o.Source != null && o.Source.x == ox && o.Source.y == oy);
                if (!PfbSpot(w, t, ring, out int sx, out int sy)) return r;
                r.placed = true;
                var lead = w.CreateRobotGroup(unitType, sx, sy, team);
                var squad = new List<ZObject> { lead }; squad.AddRange(lead.Minions);
                if (target == "enemy") foreach (var o in squad) o.MaxHealth = o.Health = 1000000000;
                var ids = new HashSet<int>(squad.Select(o => o.Id));
                r.squad = squad.Count; r.hp0 = t.Health;
                var wp = ZWaypoint.Move(t.CenterX, t.CenterY, force, t.Id);
                wp.mode = WpMode.Attack;
                if (force) { wp.fightOnWay = true; wp.forceFire = true; }                         // as View/PlayerInput.cs BuildWaypoint with Ctrl held
                w.SendWaypoints(team, lead, new[] { wp });
                r.accepted = lead.Waypoints.Count > 0 && lead.Waypoints[0].mode == WpMode.Attack && lead.Waypoints[0].refId == t.Id;
                double end = r.accepted ? 90 : 30, stopAt = -1;
                while (w.Time < end)
                {
                    w.Tick(ZConst.TICK);
                    foreach (var e in w.Events)
                    {
                        if (e.type != ZEventType.Hitscan || !e.flag || e.otherId != t.Id || !ids.Contains(e.objId)) continue;
                        if (stopAt >= 0) r.hitsAfterStop++;
                        else { r.hits++; if (r.firstHit < 0) r.firstHit = w.Time; }
                    }
                    w.Events.Clear();
                    if (t.Destroyed && r.fell < 0) { r.fell = w.Time; if (!stopAfter) break; }
                    if (stopAfter && stopAt < 0 && r.firstHit >= 0 && w.Time >= r.firstHit + 3)
                    {
                        int dx = sx - t.CenterX, dy = sy - t.CenterY; double len = Math.Max(1, Math.Sqrt((double)dx * dx + (double)dy * dy));
                        w.SendWaypoints(team, lead, new[] { ZWaypoint.Move(sx + (int)(dx / len * 96), sy + (int)(dy / len * 96)) });   // 6 tiles further from the target
                        stopAt = w.Time; r.hitsAfterStop = 0; end = Math.Min(end, w.Time + 10);
                    }
                }
                r.hp1 = t.Health;
                r.lost = squad.Count(o => o.Destroyed || o.KillAt >= 0);
                return r;
            }
            finally { ZWorld.PyroForceBurnDeviation = was; }
        }

        static Deviation DevPyroForceBurn()
        {
            string measured; bool pass;
            try
            {
                var set = ZSettings.Parse(SettingsText);
                var py = set.Unit("pyro");
                var a = PfbCase("factory", Team.Red, "pyro", true, false, true, false, 11);          // Ctrl: a neutral factory
                var b = PfbCase("factory", Team.Red, "pyro", false, false, true, false, 11);         // no Ctrl
                var a2 = PfbCase("enemy", Team.Blue, "pyro", true, false, true, false, 17);          // Ctrl: the enemy's factory beside its fort
                var c = PfbCase("bridge", Team.Red, "pyro", true, false, true, false, 12);           // Ctrl: a bridge
                var c2 = PfbCase("bridge", Team.Red, "pyro", true, false, true, false, 18, 16);      // Ctrl: a bridge, the squad 16 tiles away (it walks into range: nobody may stand on the deck when it falls)
                var d = PfbCase("bridge", Team.Red, "pyro", false, false, true, false, 12);          // no Ctrl
                var e = PfbCase("factory", Team.Red, "grunt", true, false, true, false, 13);         // Ctrl, grunts without grenades
                var f = PfbCase("factory", Team.Red, "pyro", true, true, true, false, 14);           // Ctrl mark on a CPU team's order
                var g = PfbCase("factory", Team.Red, "pyro", true, false, false, false, 15);         // Ctrl, the rule off
                var h = PfbCase("factory", Team.Red, "pyro", true, false, true, true, 16);           // Ctrl, then a new order 3 s after the first hit
                bool Burnt(PfbResult x) => x.placed && x.accepted && x.hits > 0 && x.fell >= 0 && x.hp1 == 0 && x.lost == 0;
                bool Untouched(PfbResult x) => x.placed && !x.accepted && x.hits == 0 && x.hp1 == x.hp0;
                pass = Burnt(a) && Untouched(b) && a2.placed && a2.accepted && a2.hits > 0 && a2.hp1 < a2.hp0 && Burnt(c) && Burnt(c2) && Untouched(d) && Untouched(e)
                       && f.placed && !f.accepted && f.hits == 0 && Untouched(g)                     // the CPU may take the factory's zone meanwhile: only its squad's hits count
                       && h.placed && h.accepted && h.hits > 0 && h.hitsAfterStop == 0 && h.hp1 > 0;
                measured = $"pyro {py.Damage} HP a hit at {py.attack_damage_chance:0.00}, every {py.attack_speed:0.0} s, {py.attack_radius} px | " +
                           $"Ctrl on a neutral factory: {a.Text("factory")} | without Ctrl: {b.Text("factory")} | " +
                           $"Ctrl on the enemy's factory beside its fort (the squad cannot die): {a2.Text("factory")} | " +
                           $"Ctrl on a bridge: {c.Text("bridge")} | the same from 16 tiles: {c2.Text("bridge")} | without Ctrl: {d.Text("bridge")} | " +
                           $"Ctrl, grunts without grenades: {e.Text("factory")} | Ctrl mark on a CPU team's order: {f.Text("factory")} | " +
                           $"Ctrl, rule off: {g.Text("factory")} | Ctrl, a new order 3 s after the first hit: {h.Text("factory")}";
            }
            catch (Exception ex) { measured = "the probe threw " + ex.GetType().Name + ": " + ex.Message; pass = false; }
            return new Deviation
            {
                id = "DV113", change = "Ctrl: Pyros burn buildings and bridges (the player's attack order with Ctrl held)",
                zod = "a building or bridge takes explosives only (SetClassFlags, zbuilding.cpp:17-19; CanAttackObject, zobject.cpp:4767-4777): a Pyro without grenades can never attack one, the order is refused",
                openz = "Sim/ZObject.PyroForceBurn.cs: an attack order the player gives with Ctrl held (ZWaypoint.forceFire, View/PlayerInput.cs) on a standing building or bridge not of his team is valid for the Pyros of a human team; " +
                        "they flame it with their own weapon, unchanged, until it falls or another order comes. The Ctrl ATTACK cursor shows it (UI/OpenZCursors.cs). Unchanged: without Ctrl, other units, " +
                        "map items, CPU teams (no mark; the rule asks for a human team); units never pick a building by themselves. View: the Pyro's existing stream, sticky fire, burning ground, smoke, light and sounds",
                measured = measured,
                toggle = "ZWorld.PyroForceBurnDeviation: on in the game, off in 'Zod exact' (View/ZGame.cs); the other rows never give the Ctrl mark",
                status = pass ? DevActive : DevCheck,
            };
        }
    }
}
#endif
