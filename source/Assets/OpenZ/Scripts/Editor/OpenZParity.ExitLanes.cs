using System;
using System.Collections.Generic;
using System.Linq;
using OpenZ.Sim;
#if !OPENZ_HARNESS
using UnityEditor;
using UnityEngine;
#endif

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Measured headless on both maps as in the game (L17 DV32 parked start,
    /// the red head start): start vehicles moved off exit lanes / tight slots, and the longest stand-still of a robot with an order while
    /// each fort builds 5 grunt squads (one every 3 s) that walk to a rally point 6 tiles out and 5 aside, once with DV66 and once without.
    /// </summary>
    public static partial class OpenZParity
    {
        static readonly string[] ExitHeadL02 = { "light", "light", "jeep", "jeep", "medium" }, ExitHeadL17 = { "missile_launcher", "heavy" };

        static (int moved, double still, int over) ExitProbe(string lv, bool on, bool block = false)
        {
            bool keep = ZWorld.ExitLanesDeviation;
            ZWorld.ExitLanesDeviation = on;
            try
            {
                var w = ZWorld.Create(SettingsText, MapText(lv), 1, lv == "L17");
                w.AddHeadStart(Team.Red, lv == "L17", lv == "L02" ? ExitHeadL02 : ExitHeadL17);
                w.Events.Clear();
                var forts = w.Objects.Where(o => o.Kind == ZKind.Building && o.IsFort && o.GetCreationPoint(out _, out _)).ToList();
                foreach (var f in forts) f.StopBuildingProduction();
                if (block)                                                                    // review round 1: an empty hull left right on each gate
                    foreach (var f in forts) { f.GetCreationMovePoint(out int bx, out int by); w.CreateObject("vehicle", "medium", bx - 16, by - 16, Team.Neutral); }
                var made = new HashSet<int>();
                var last = new Dictionary<int, (double x, double y, double t)>();
                double max = 0; var over = new HashSet<int>();
                int built = 0;
                while (w.Time < 45)
                {
                    if (built < 5 && w.Time >= built * 3.0) { foreach (var f in forts) { f.Building = true; f.CurUnit = "grunt"; f.BInit = w.Time; f.BFinal = w.Time; } built++; }
                    else if (built >= 5) foreach (var f in forts) if (f.Building) f.StopBuildingProduction();
                    w.Tick(ZConst.TICK);
                    foreach (var e in w.Events)
                    {
                        if (e.type != ZEventType.Created || !forts.Any(f => f.Id == e.otherId)) continue;
                        var u = w.Get(e.objId); if (u == null || u.Kind != ZKind.Robot) continue;
                        made.Add(u.Id);
                        if (u.IsMinion) continue;
                        var b = w.Get(e.otherId); b.GetCreationPoint(out int cx, out int cy); b.GetCreationMovePoint(out int ex, out int ey);
                        double dx = ex - cx, dy = ey - cy, l = Math.Sqrt(dx * dx + dy * dy);
                        u.Waypoints.Add(ZWaypoint.Move(ex + (int)(dx / l * 96 - dy / l * 80), ey + (int)(dy / l * 96 + dx / l * 80))); u.CloneMinionWayPoints();
                    }
                    w.Events.Clear();
                    foreach (var u in w.Objects)
                    {
                        if (!made.Contains(u.Id) || u.Destroyed || u.Waypoints.Count == 0 || u.Waypoints[0].mode == WpMode.Agro || u.Waypoints[0].mode == WpMode.Attack) { last.Remove(u.Id); continue; }
                        if (!last.TryGetValue(u.Id, out var p) || Math.Abs(u.PosX - p.x) + Math.Abs(u.PosY - p.y) >= 2) { last[u.Id] = (u.PosX, u.PosY, w.Time); continue; }
                        double s = w.Time - p.t; if (s > max) max = s; if (s > 2.05) over.Add(u.Id);
                    }
                }
                return (w.ExitLaneMoves.Count, max, over.Count);
            }
            finally { ZWorld.ExitLanesDeviation = keep; }
        }

        static Deviation DevExitLanes(Func<string, int, int> getInt)
        {
            var on17 = ExitProbe("L17", true); var off17 = ExitProbe("L17", false);
            var on02 = ExitProbe("L02", true); var off02 = ExitProbe("L02", false);
            var bl17 = ExitProbe("L17", true, true); var bl02 = ExitProbe("L02", true, true);
            bool pref = getInt("oz_exit_lanes", 1) == 1 && getInt("oz_zod_exact", 0) == 0;
            bool ok = on17.over == 0 && on02.over == 0 && on17.moved > 0;
            return new Deviation
            {
                id = "DV66", change = "Exit lanes and gate faces kept clear: no start vehicle in front of a fort gate / factory door, free exit cells, robots go round parked hulls, no wedge slots",
                zod = "map vehicles stand where the map file puts them (L17: the red medium tank exactly on the red fort's exit point, four car-park vehicles straight in front of their factory doors); a new unit force-moves in a straight line to the exit point (GetBuildingCreationMovePoint); Zod has no unit collision, so nothing ever blocked it",
                openz = $"at world creation a vehicle in an exit lane (creation point -> exit point + {ZWorld.ExitLaneReach} px, {ZWorld.ExitLaneHalf} px + chassis radius each side), within {ZWorld.ParkRing} tiles of a fort's gate face (the fort's full front width, {ZWorld.GateFaceDepth} tiles deep) or in a 1-tile slot / pocket next to a wall, building, rock or vehicle moves to the nearest spot with {ZWorld.ParkRing} free tiles all round, no water within {ZWorld.ShoreRing} tile (same reachable area, off roads); the head start parks by the same rule beside the gate; " +
                        "a squad whose exit point is taken steps out of the door (or, when a hull covers the door point, at the free edge of the gate opening) and walks to the next free exit cell (not one another squad stands on or walks to); robot paths and a minion's regroup walk go round parked / idle vehicles and cannons with clearance (DV36 for robots), a robot held by one does not queue behind it, and one wedged against it with no way round steps out to the nearest free cell (ZWorld.ExitLanes.cs, ZObject.BoardingNB.cs, ZStuckGuard); " +
                        $"a new vehicle's exit point is moved out along its exit line until its drawn hull (18 to 23 px behind its centre, by type) clears the building by {ZWorld.VehicleExitClearPx} px (only onto free ground), and its hull keeps the door's heading until that exit move is over; the robot factory's exit line runs through the middle of its door (32 px from its left edge; Zod: 43); a unit on its exit move inside the building that made it sees and fires out through that building (DV23's sight line skips it), and can be fired at",
                measured = $"headless, 5 grunt squads per fort (one every 3 s, rally 6 tiles out / 5 aside), 45 s: L17 {on17.moved} start vehicles re-parked, longest stand-still with an order {on17.still:F1} s ({on17.over} robots > 2 s) vs Zod spots {off17.still:F1} s ({off17.over} > 2 s); " +
                           $"L02 {on02.moved} re-parked (+ the head start beside the gate), {on02.still:F1} s ({on02.over} > 2 s) vs {off02.still:F1} s ({off02.over} > 2 s); with an empty medium left on each gate: L17 {bl17.still:F1} s ({bl17.over} > 2 s), L02 {bl02.still:F1} s ({bl02.over} > 2 s)",
                toggle = $"ZWorld.ExitLanesDeviation (PlayerPrefs oz_exit_lanes = {(getInt("oz_exit_lanes", 1) == 1 ? "1 (on, default)" : "0 (off)")}); menu OpenZ/Deviations/Exit Lanes Kept Clear; off in 'Zod exact' and in every Zod row (static default off)",
                status = !ok ? DevCheck : pref ? DevActive : DevActive + " (off by pref)",
            };
        }

#if !OPENZ_HARNESS
        const string ExitLanesMenu = "OpenZ/Deviations/Exit Lanes Kept Clear (oz_exit_lanes)";

        [MenuItem(ExitLanesMenu)]
        static void ToggleExitLanes()
        {
            bool on = PlayerPrefs.GetInt("oz_exit_lanes", 1) != 1;
            PlayerPrefs.SetInt("oz_exit_lanes", on ? 1 : 0); PlayerPrefs.Save();
            Debug.Log($"OpenZ exit lanes kept clear (deviation DV66) from the next Play: {(on ? "ON" : "OFF: Zod's start vehicle spots and straight exits")}");
        }
        [MenuItem(ExitLanesMenu, true)]
        static bool ToggleExitLanesCheck() { Menu.SetChecked(ExitLanesMenu, PlayerPrefs.GetInt("oz_exit_lanes", 1) == 1); return true; }

        [MenuItem("OpenZ/Deviations/Probe DV66 Exit Lanes")]
        static void ProbeExitLanesMenu() { var d = DevExitLanes((k, def) => PlayerPrefs.GetInt(k, def)); Debug.Log($"[DV66] {d.status}: {d.measured}"); }
#endif
    }
}
