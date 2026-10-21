#if !OPENZ_HARNESS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>The Deviations row DV116 "sniper headshot" (the rule: Sim/ZObject.Headshot.cs) and its counted check (menu OpenZ/V1.1/Sniper:
    /// K248 Headshot Check). Measured headless on the level 2 map without its start units (forts, buildings and terrain kept): one blue
    /// shooter that cannot die fires at one red target 6 tiles away on the free ground nearest the map's middle; a destroyed target is replaced by a fresh one on the
    /// same spot until the shooter has landed the wanted number of hits (no CPU player, two far-away keep-alive grunts so the match never ends).</summary>
    public static partial class OpenZParity
    {
        static void AddSniperHeadshotDeviation(List<Deviation> list)
        {
            try { list.Add(DevSniperHeadshot()); }
            catch (Exception ex) { list.Add(new Deviation { id = "DV116", change = "Sniper headshot", zod = "-", openz = "-", measured = "probe threw " + ex.GetType().Name + ": " + ex.Message, toggle = "-", status = DevCheck }); }
        }

        /// <summary>One case's numbers: shots, hits (driver snipes apart), headshots (each a robot destroyed by that very hit), the kills by
        /// normal hits, the targets used, and a fingerprint of every hit (for the same-match-twice check).</summary>
        internal sealed class SnhResult
        {
            public bool placed; public int shots, hits, snipes, heads, instant, notInstant, normalKills, targets; public double secs; public ulong trace;
            public double Pct => hits > 0 ? 100.0 * heads / hits : 0;
            public string Text() =>
                !placed ? "no free spot" :
                $"{hits} hits in {shots} shots{(snipes > 0 ? $" (+{snipes} driver snipes)" : "")}, {heads} headshots ({Pct:0.0} %), {instant} of them destroyed the target at that hit" +
                $"{(notInstant > 0 ? $", {notInstant} NOT" : "")}, {normalKills} targets destroyed by normal hits, {targets} targets, {secs:0} s";
        }

        /// <summary>The level 2 map without its robots, vehicles and guns (forts, buildings, flags, map items and terrain stay).</summary>
        internal static string SnhNoUnits(string mapJson)
        {
            var m = ZMapData.FromJson(mapJson);
            m.objects.RemoveAll(o => { string c = o.kind.Split(':')[0]; return c == "robot" || c == "vehicle" || c == "cannon"; });
            return UnityEngine.JsonUtility.ToJson(m);
        }

        static ulong SnhFnv(ulong h, long v) { h ^= (ulong)v; h *= 1099511628211UL; h ^= h >> 31; return h; }

        /// <summary>One case: a blue <paramref name="shooter"/> against red <paramref name="cat"/>:<paramref name="type"/> targets until
        /// <paramref name="wantHits"/> hits, driver snipes included (or 6 s of game time per wanted hit), the DV116 switch as given (put back afterwards).</summary>
        internal static SnhResult SnhCase(bool on, string shooter, string cat, string type, int wantHits, int seed)
        {
            bool was = ZWorld.SniperHeadshotDeviation;
            ZWorld.SniperHeadshotDeviation = on;
            var r = new SnhResult();
            try
            {
                var w = ZWorld.Create(SettingsText, SnhNoUnits(MapText("L02")), seed);
                w.Bots.Clear();
                foreach (var (tm, x) in new[] { (Team.Red, 16), (Team.Blue, (w.Map.width - 2) * 16) })
                {   // keep-alive grunts in far corners that cannot die and do no harm (Zod's no-units rule would end the match)
                    var d = w.CreateObject("robot", "grunt", x, 16, tm);
                    d.MaxHealth = d.Health = 1000000000; d.Damage = 0;
                }
                int sx = -1, sy = -1, cx = w.Map.width / 2, cy = w.Map.height / 2;
                for (int ring = 0; ring <= 40 && sx < 0; ring++)                                         // the free ground nearest the map's middle
                    for (int dy = -ring; dy <= ring && sx < 0; dy++)
                        for (int dx = -ring; dx <= ring && sx < 0; dx++)
                        {
                            if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != ring) continue;
                            int tx = cx + dx, ty = cy + dy;
                            bool free = true;
                            for (int k = -1; k <= 8 && free; k++) for (int j = -1; j <= 2 && free; j++) free = w.Path.TilePassable(tx + k, ty + j, true);
                            if (free) { sx = tx * 16; sy = ty * 16; }
                        }
                if (sx < 0) return r;
                r.placed = true;
                var a = w.CreateObject("robot", shooter, sx, sy, Team.Blue);
                a.MaxHealth = a.Health = 1000000000;
                ZObject t = null; var counted = new HashSet<int>();
                ulong h = 1469598103934665603UL;
                double limit = 6.0 * wantHits;
                while (r.hits + r.snipes < wantHits && w.Time < limit)
                {
                    if (t == null || t.Destroyed || t.Owner != Team.Red)                                    // destroyed, or a vehicle whose driver was sniped
                    {
                        t = w.CreateObject(cat, type, sx + 96, sy, Team.Red); r.targets++;
                        w.SendWaypoints(Team.Blue, a, new[] { new ZWaypoint { mode = WpMode.Attack, refId = t.Id, x = t.CenterX, y = t.CenterY } });
                    }
                    w.Tick(ZConst.TICK);
                    foreach (var e in w.Events)
                    {
                        if (e.type == ZEventType.Hitscan && e.objId == a.Id)
                        {
                            r.shots++;
                            if (!e.flag) continue;
                            if (e.flag2) { r.snipes++; continue; }
                            r.hits++;
                            var v = w.GetAny(e.otherId);
                            h = SnhFnv(SnhFnv(h, e.otherId), BitConverter.DoubleToInt64Bits(w.Time));
                            if (v != null && v.HeadshotBy == a.Id && counted.Add(v.Id))
                            {
                                r.heads++; h = SnhFnv(h, -v.Id);
                                bool died = v.Destroyed && w.Events.Any(d => d.type == ZEventType.Death && d.objId == v.Id && d.otherId == a.Id);
                                if (died) r.instant++; else r.notInstant++;
                            }
                        }
                        else if (e.type == ZEventType.Death && e.otherId == a.Id)
                        {
                            var v = w.GetAny(e.objId);
                            if (v != null && v.HeadshotBy < 0) r.normalKills++;
                        }
                    }
                    w.Events.Clear();
                }
                r.secs = w.Time; r.trace = h;
                return r;
            }
            finally { ZWorld.SniperHeadshotDeviation = was; }
        }

        /// <summary>The counted check (headless runner and the menu): 1000 hits of a sniper on grunts with the rule on, the same match
        /// again, the rule off, a sniper on a light tank, a sniper on a laser robot, a grunt on grunts.</summary>
        internal static string SnhReport(int hits, out bool all)
        {
            var sb = new StringBuilder();
            bool ok_ = true, before = ZWorld.SniperHeadshotDeviation;
            void V(bool ok, string what) { sb.AppendLine((ok ? "PASS  " : "FAIL  ") + what); ok_ &= ok; }
            var on = SnhCase(true, "sniper", "robot", "grunt", hits, 7);
            var again = SnhCase(true, "sniper", "robot", "grunt", hits, 7);
            var off = SnhCase(false, "sniper", "robot", "grunt", hits, 7);
            var tank = SnhCase(true, "sniper", "vehicle", "light", Math.Max(50, hits / 5), 7);
            var laser = SnhCase(true, "sniper", "robot", "laser", Math.Max(100, hits / 4), 7);
            var grunt = SnhCase(true, "grunt", "robot", "grunt", Math.Max(100, hits / 4), 7);
            sb.AppendLine($"sniper on grunts, rule on : {on.Text()}");
            sb.AppendLine($"the same match again      : {again.Text()}");
            sb.AppendLine($"sniper on grunts, rule off: {off.Text()}");
            sb.AppendLine($"sniper on a light tank    : {tank.Text()}");
            sb.AppendLine($"sniper on laser robots    : {laser.Text()}");
            sb.AppendLine($"grunt on grunts, rule on  : {grunt.Text()}");
            double lo = hits * 0.15 - 3.0 * Math.Sqrt(hits * 0.15 * 0.85), hi = hits * 0.15 + 3.0 * Math.Sqrt(hits * 0.15 * 0.85);
            V(on.placed && on.hits == hits && on.heads >= lo && on.heads <= hi, $"H1 {hits} sniper hits on robots give about 15 % headshots: {on.heads} ({on.Pct:0.0} %; three standard deviations: {lo:0} .. {hi:0})");
            V(on.heads > 0 && on.instant == on.heads && on.notInstant == 0, $"H2 every headshot destroys its robot at that hit ({on.instant} of {on.heads})");
            V(again.hits == on.hits && again.heads == on.heads && again.trace == on.trace, "H3 the same match twice: the same headshots on the same hits (deterministic)");
            V(off.placed && off.heads == 0 && off.normalKills > 0, $"H4 rule off: no headshot, robots die by normal hits only ({off.normalKills} in {off.hits} hits)");
            V(tank.placed && tank.hits + tank.snipes >= Math.Max(50, hits / 5) && tank.heads == 0, $"H5 a vehicle never takes a headshot ({tank.hits} hull hits, {tank.snipes} driver snipes, 0 headshots)");
            V(laser.placed && laser.heads > 0 && laser.instant == laser.heads, $"H6 another robot type (laser) takes headshots too ({laser.heads} in {laser.hits} hits)");
            V(grunt.placed && grunt.hits > 0 && grunt.heads == 0, $"H7 only snipers: a grunt's hits are never headshots ({grunt.hits} hits)");
            V(ZWorld.SniperHeadshotDeviation == before, $"H8 the switch is put back after the cases ({(before ? "on" : "off")})");
            all = ok_;
            sb.AppendLine(all ? "ALL PASS" : "NOT ALL PASS");
            return sb.ToString();
        }

        static Deviation DevSniperHeadshot()
        {
            string measured; bool pass;
            try
            {
                var on = SnhCase(true, "sniper", "robot", "grunt", 400, 7);
                var off = SnhCase(false, "sniper", "robot", "grunt", 400, 7);
                var tank = SnhCase(true, "sniper", "vehicle", "light", 80, 7);
                pass = on.placed && on.hits == 400 && on.heads >= 40 && on.heads <= 82 && on.instant == on.heads && off.heads == 0 && tank.heads == 0 && tank.hits + tank.snipes > 0;
                measured = $"rule on: {on.Text()} | rule off: {off.Text()} | on a light tank: {tank.Text()}";
            }
            catch (Exception ex) { measured = "the probe threw " + ex.GetType().Name + ": " + ex.Message; pass = false; }
            return new Deviation
            {
                id = "DV116", change = "Sniper headshot: 15 % of a sniper's hits on a robot destroy it at once",
                zod = "a sniper's bullet that hits a robot removes the sniper's normal damage (zobject.cpp:2192); no headshots",
                openz = $"Sim/ZObject.Headshot.cs: a sniper's hit on a robot on foot (every robot type, the hero squads too; never a vehicle, gun, building or map item) is a headshot with {ZWorld.HeadshotPct} % chance: " +
                        "that hit takes all the health the robot has left. Only hits roll; driver snipes are unchanged. The roll is its own stream (a hash of shooter, target, clock and roll count): the world's random numbers are untouched, " +
                        "the same match gives the same headshots. View (UnitView.Headshot.cs): the round ends at the head, the head falls off with physics like the robots' rocket-fuel can, the body collapses as a ragdoll without the death fling, " +
                        "a metal ping; the player's sniper calls it on camera at most once in three headshots and never twice within 20 s (vo_headshot_sniper)",
                measured = measured,
                toggle = "ZWorld.SniperHeadshotDeviation: View/ZGame.cs sets it at every start (off in 'Zod exact'); off in headless worlds, the other rows and the version 1.1 counted checks unless they set it",
                status = pass ? DevActive : DevCheck,
            };
        }
    }

#if UNITY_EDITOR
    /// <summary>K248 menus: the counted check (outside Play) and, in Play, a staged fight for the approval clip.</summary>
    public static class OzV11HeadshotCheck
    {
        [UnityEditor.MenuItem("OpenZ/V1.1/Sniper: K248 Headshot Check (1000 hits)")]
        static void Menu() => OzV11Kit.RunMenu("Sniper: K248 Headshot Check (1000 hits)", () => OpenZParity.SnhReport(1000, out _));

        /// <summary>In Play: a squad of the player's snipers and two enemy grunt squads 9 tiles to their right, under the middle of the
        /// screen (free ground looked for nearby); the snipers are ordered to attack. Frame it at the normal RTS distance and record.</summary>
        [UnityEditor.MenuItem("OpenZ/V1.1/Sniper: K248 Stage headshots (Play)")]
        static void Stage()
        {
            if (OzV11SniperSteadyStage.Run()) return;                                   // K248b: the clean stage: dry open ground, nothing to grab near, never two trios (OpenZParity.V11SniperSteady.cs)
            var g = ZGame.I;
            if (!UnityEngine.Application.isPlaying || g == null || g.World == null) { UnityEngine.Debug.LogWarning("[OpenZ V1.1] K248 stage: start a level in Play first."); return; }
            var w = g.World; var cam = UnityEngine.Camera.main;
            if (!cam) { UnityEngine.Debug.LogWarning("[OpenZ V1.1] K248 stage: no main camera."); return; }
            var ray = cam.ViewportPointToRay(new UnityEngine.Vector3(0.5f, 0.5f, 0f));
            var c = cam.transform.position; var plane = new UnityEngine.Plane(UnityEngine.Vector3.up, new UnityEngine.Vector3(0f, g.GroundY(c.x, c.z), 0f));
            if (!plane.Raycast(ray, out float dist)) { UnityEngine.Debug.LogWarning("[OpenZ V1.1] K248 stage: the camera does not look at the ground."); return; }
            var px = ZGame.WorldToPx(ray.GetPoint(dist));
            int bx = -1, by = -1;
            for (int r = 0; r <= 12 && bx < 0; r++)
                for (int dy = -r; dy <= r && bx < 0; dy++)
                    for (int dx = -r; dx <= r && bx < 0; dx++)
                    {
                        int tx = (px.x >> 4) - 5 + dx, ty = (px.y >> 4) + dy; bool free = true;
                        for (int k = -1; k <= 11 && free; k++) for (int j = -4; j <= 4 && free; j++) free = w.Path.TilePassable(tx + k, ty + j, true);
                        if (free) { bx = tx * 16; by = ty * 16; }
                    }
            if (bx < 0) { UnityEngine.Debug.LogWarning("[OpenZ V1.1] K248 stage: no free ground under the middle of the screen; move the camera to open ground."); return; }
            var enemy = g.player == Team.Red ? Team.Blue : Team.Red;
            ZWorld.SniperHeadshotDeviation = true;
            var snipers = w.CreateRobotGroup("sniper", bx, by, g.player);
            var g1 = w.CreateRobotGroup("grunt", bx + 150, by - 40, enemy);
            var g2 = w.CreateRobotGroup("grunt", bx + 165, by + 40, enemy);
            foreach (var o in new[] { snipers, g1, g2 }) if (o != null) { g.SpawnUnitView(o); foreach (var m in o.Minions) g.SpawnUnitView(m); }   // the staged units get their views (as the cheat spawner does)
            if (snipers != null && g1 != null) w.SendWaypoints(g.player, snipers, new[] { new ZWaypoint { mode = WpMode.Attack, refId = g1.Id, x = g1.CenterX, y = g1.CenterY, playerGiven = true } });
            UnityEngine.Debug.Log($"[OpenZ V1.1] K248 stage: {(snipers != null ? 1 + snipers.Minions.Count : 0)} snipers of {g.player} at tile {bx >> 4},{by >> 4}, two {enemy} grunt squads 9-10 tiles east; headshot rule ON. " +
                                  $"Counters so far: headshot deaths {UnitView.HeadshotDeaths}, heads thrown {UnitView.HeadsThrown}, calls {UnitView.HeadCalls} (skipped {UnitView.HeadCallsSkipped}).");
        }

        [UnityEditor.MenuItem("OpenZ/V1.1/Sniper: K248 Report the counters (Play)")]
        static void Counters()
        {
            var w = ZGame.I ? ZGame.I.World : null;
            UnityEngine.Debug.Log($"[OpenZ V1.1] K248: rule {(ZWorld.SniperHeadshotDeviation ? "ON" : "off")}; sim: sniper hits on robots rolled {(w != null ? w.HeadshotRolls : 0)}, headshots {(w != null ? w.Headshots : 0)}; " +
                                  $"view: headshot deaths {UnitView.HeadshotDeaths}, heads thrown {UnitView.HeadsThrown} (baked part {UnitView.HeadsBaked}, skin copy {UnitView.HeadsSkinned}), calls {UnitView.HeadCalls}, calls skipped {UnitView.HeadCallsSkipped}; " +
                                  $"rocket-fuel cans and heads: {AnticCan.Thrown} thrown, {AnticCan.Clatters} clatters, {AnticCan.Disposed} disposed, {AnticCan.Sunk} sunk");
        }
    }
#endif
}
#endif
