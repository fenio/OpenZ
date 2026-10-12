using System;
using System.Collections.Generic;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// pathing_v3 (from pathing_commit_v1 review 1, DV24 + DV44): the parity report's OpenZ squad-pace rows. (Pathing v3: inside a bridge /
    /// water / passage zone the live layer steers a minion - its DV24 step as is (review round 3: no pace cap in a zone), and the live
    /// pushes, side-steps and displacements, which can beat the Zod speed for a moment exactly as with v3 off; the rows check DV44's pace
    /// where DV44 steers the minion and show the zone windows apart - review round 1.) The only place DV24 / DV44 touch a unit's
    /// speed is a minion following its squad; these rows measure it with the deviations switched as the game ships them (DV24 + DV44
    /// on). A 6-grunt squad walks a water crossing (L02 river 33,23 -> 33,33 and back, L17 canal 47,7 -> 62,7 and back) and the longest
    /// road run of each level; per 0.25 s window every minion's walked distance against its own Zod speed (ZObject.RealMoveSpeed: tile,
    /// run, damage) by the ground it is on, in every window the unit walks a MOVE order. Zod: every unit walks at exactly its own speed. OpenZ: a minion never walks faster than its
    /// own speed in water (or on any slow tile), and at most DV24's catch-up (ZFormation.CatchUp) on normal ground and road; the
    /// leader stays Zod-exact. A step walks at the RealMoveSpeed set before it (ZObject.ProcessMove updates it from the new tile after
    /// the step, Zod), and Zod's own snap onto a path point (ReachedTarget clears the sub-pixel carry, up to 1 px per axis) is not pace;
    /// windows in which the pathing layer pushed the unit off an overlap (its separation, DV6) are counted apart, not as pace.
    /// Headless, deterministic (seeded world, fixed tick). (The package harness runs the same probe: tools/PcGate.cs GSpeed.)
    /// </summary>
    public static partial class OpenZParity
    {
        public struct SquadPace { public int waterWin, waterOver, roadWin, roadOver, groundWin, groundOver, pushedWin, zonedWin, zonedOver; public double waterMax, roadMax, groundMax, leaderMax, zonedMax; }

        public static SquadPace PathCommitSquadPace(string lv, int x0, int y0, int x1, int y1)
        {
            bool ef = ZWorld.EndFreezeDeviation, fg = ZWorld.FortGunDeviation, fd = ZWorld.FortDefenderDeviation;
            ZWorld.EndFreezeDeviation = false; ZWorld.FortGunDeviation = false; ZWorld.FortDefenderDeviation = false;
            try
            {
                var w = ZWorld.Create(SettingsText, MapText(lv), 1, false);
                foreach (var o in w.Objects)
                    if (o.Kind == ZKind.Robot || o.Kind == ZKind.Cannon || (o.Kind == ZKind.Vehicle && o.Owner != Team.Neutral)) o.KillAt = w.Time;
                for (int i = 0; i < 100; i++) { w.Tick(ZConst.TICK); w.Events.Clear(); }
                var l = w.CreateRobotGroup("grunt", x0 * 16, y0 * 16, Team.Red, 6); l.PlayerHold = true;
                var all = new List<ZObject> { l }; all.AddRange(l.Minions);
                for (int i = 0; i < 300; i++) { w.Tick(ZConst.TICK); w.Events.Clear(); }        // the stacked squad unstacks
                w.SendWaypoints(Team.Red, l, new List<ZWaypoint> { ZWaypoint.Move(x1 * 16 + 8, y1 * 16 + 8, true) });
                var pos = new List<(double x, double y, double v, double tile, bool snap, bool order, bool pushed, bool zoned)>[all.Count];
                for (int i = 0; i < all.Count; i++) pos[i] = new List<(double, double, double, double, bool, bool, bool, bool)>();
                for (int k = 0; k < 9000 && (k < 100 || all.Exists(o => o.Waypoints.Count > 0)); k++)
                {
                    w.Tick(ZConst.TICK); w.Events.Clear();
                    for (int i = 0; i < all.Count; i++)
                    {
                        var o = all[i]; var p = pos[i];
                        double nx = o.PosX + o.WidthPix * 0.5, ny = o.PosY + o.HeightPix * 0.5; bool snap = false;
                        if (p.Count > 0)
                        {                                                                        // Zod's snap onto a path point: whole pixels, carry cleared, a jump
                            var q = p[p.Count - 1];
                            snap = o.XOver == 0 && o.YOver == 0 && nx == Math.Floor(nx) && ny == Math.Floor(ny)
                                   && Math.Abs(nx - q.x) + Math.Abs(ny - q.y) > 1.8 * Math.Max(q.v, o.RealMoveSpeed) * ZConst.TICK;
                        }
                        p.Add((nx, ny, o.RealMoveSpeed, w.Map.TileWalkSpeed(o.CenterX, o.CenterY), snap, o.Waypoints.Count > 0 && o.Waypoints[0].mode == WpMode.Move,
                               o.PathPushedThisTick, o.PathZoned));                     // (the layer pushed it off an overlap this tick; pathing v3: in a zone)
                    }
                }
                var r = new SquadPace();
                for (int i = 0; i < all.Count; i++)
                {
                    var p = pos[i];
                    for (int k = 25; k < p.Count; k += 25)
                    {
                        double len = 0, own = 0; bool water = true, road = true, walking = true, pushed = false, zoned = false;
                        for (int j = k - 24; j <= k; j++)
                        {
                            if (!p[j].order) walking = false;
                            if (p[j].pushed) pushed = true;
                            if (p[j].zoned) zoned = true;
                            if (!p[j].snap) len += Math.Sqrt((p[j].x - p[j - 1].x) * (p[j].x - p[j - 1].x) + (p[j].y - p[j - 1].y) * (p[j].y - p[j - 1].y));
                            own += p[j - 1].v; if (p[j].tile >= 1) water = false; if (p[j].tile <= 1) road = false;
                        }
                        own /= 25; double d = len / 0.25;
                        if (!walking || d < 0.5 || own < 1e-6) continue;                      // (walking windows: a MOVE order the whole window)
                        if (pushed) { if (i > 0) r.pushedWin++; continue; }                     // (separated from an overlap by the layer's push: not its pace; counted apart)
                        double ratio0 = d / own;
                        if (zoned && i > 0)
                        {                                                                        // (pathing v3: in a bridge / water / passage zone the live layer steers the
                            r.zonedWin++; r.zonedMax = Math.Max(r.zonedMax, ratio0);           //  minion - its live DV24 step, and its pushes, side-steps
                            if (ratio0 > (water ? 1.02 : ZFormation.CatchUp + 0.02)) r.zonedOver++;   //  and displacements: counted apart, shown; the rows check DV44)
                            continue;
                        }
                        double ratio = d / own;
                        if (i == 0) { r.leaderMax = Math.Max(r.leaderMax, ratio); continue; }
                        if (water) { r.waterWin++; r.waterMax = Math.Max(r.waterMax, ratio); if (ratio > 1.02) r.waterOver++; }
                        else if (road) { r.roadWin++; r.roadMax = Math.Max(r.roadMax, ratio); if (ratio > ZFormation.CatchUp + 0.02) r.roadOver++; }
                        else { r.groundWin++; r.groundMax = Math.Max(r.groundMax, ratio); if (ratio > ZFormation.CatchUp + 0.02) r.groundOver++; }
                    }
                }
                return r;
            }
            finally { ZWorld.EndFreezeDeviation = ef; ZWorld.FortGunDeviation = fg; ZWorld.FortDefenderDeviation = fd; }
        }

#if !OPENZ_HARNESS
        /// <summary>OpenZParity.Run (DV44 as shipped): squad-minion pace rows on water, road and normal ground (both levels). (The
        /// Deviations-block harness, OPENZ_HARNESS, compiles the probes without the parity rows.)</summary>
        static void PathCommitSquadRows(List<Row> rows)
        {
            var runs = new List<(string name, SquadPace p)>
            {
                ("L02 river (33,23) -> (33,33)", PathCommitSquadPace("L02", 33, 23, 33, 33)),
                ("L02 river (33,33) -> (33,23)", PathCommitSquadPace("L02", 33, 33, 33, 23)),
                ("L17 canal (47,7) -> (62,7)", PathCommitSquadPace("L17", 47, 7, 62, 7)),
                ("L17 canal (62,7) -> (47,7)", PathCommitSquadPace("L17", 62, 7, 47, 7)),
                ("L17 harbour basin (24,100) -> (12,98)", PathCommitSquadPace("L17", 24, 100, 12, 98)),   // (open water, no crossing: DV44 steers the squad in water)
                ("L02 road (14,45) -> (58,45)", PathCommitSquadPace("L02", 14, 45, 58, 45)),
                ("L17 road (8,13) -> (101,13)", PathCommitSquadPace("L17", 8, 13, 101, 13)),
            };
            int ww = 0, wo = 0, rw = 0, ro = 0, gw = 0, go = 0, pw = 0, zw = 0, zo = 0; double wm = 0, rm = 0, gm = 0, lm = 0, zm = 0;
            foreach (var (_, p) in runs)
            {
                pw += p.pushedWin; zw += p.zonedWin; zo += p.zonedOver; zm = Math.Max(zm, p.zonedMax);
                ww += p.waterWin; wo += p.waterOver; wm = Math.Max(wm, p.waterMax);
                rw += p.roadWin; ro += p.roadOver; rm = Math.Max(rm, p.roadMax);
                gw += p.groundWin; go += p.groundOver; gm = Math.Max(gm, p.groundMax); lm = Math.Max(lm, p.leaderMax);
            }
            string where = string.Join(", ", runs.ConvertAll(x => x.name));
            string cu = ZFormation.CatchUp.ToString("0.0#", System.Globalization.CultureInfo.InvariantCulture);
            rows.Add(new Row { test = "squad minions in water (DV24 + DV44 on): pace against their own Zod water speed where DV44 steers them", expected = "every 0.25 s window <= 1.00 x own speed (+2 % sampling)",
                               measured = $"{wo} of {ww} windows over, max {wm:0.00} x (apart: {zw} minion windows in a pathing v3 zone - the live layer: {zo} over its bar, max {zm:0.00} x)", pass = ww > 0 && wo == 0, note = "never faster than Zod in water or on a slow tile; " + where });
            rows.Add(new Row { test = "squad minions on road (DV24 + DV44 on): pace against their own Zod road speed", expected = $"every 0.25 s window <= {cu} x own speed (DV24 catch-up)",
                               measured = $"{ro} of {rw} windows over, max {rm:0.00} x", pass = rw > 0 && ro == 0, note = where });
            rows.Add(new Row { test = "squad minions on normal ground (DV24 + DV44 on): pace against their own Zod speed where DV44 steers them", expected = $"every 0.25 s window <= {cu} x own speed (DV24 catch-up)",
                               measured = $"{go} of {gw} windows over, max {gm:0.00} x ({pw} minion windows with an overlap push and {zw} in a pathing v3 zone counted apart)", pass = gw > 0 && go == 0, note = where });
            rows.Add(new Row { test = "squad leader (DV24 + DV44 on): pace against its own Zod speed", expected = "<= 1.00 x (+2 % sampling): the leader is Zod-exact",
                               measured = $"max {lm:0.00} x", pass = lm <= 1.02, note = where });
        }
#endif
    }
}
