using System;
using System.Collections.Generic;
using System.Linq;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>los_clearance_v1: the parity report's DV24 row (squad V formation), measured headless on the live sim.</summary>
    public static partial class OpenZParity
    {
        /// <summary>L02 open ground, a grunt squad walks an L (tile 34,11 -> 52,11, a 90 deg turn, -> 52,21; the harness's
        /// "open ground L"): share of walking samples in a V (members 1 and 2 on opposite sides of the leader's line, both behind), closest squad
        /// members, leader arrival with DV24 on and off (the leader must be unchanged).</summary>
        static Deviation DevFormation(Func<string, int, int> getInt)
        {
            bool saved = ZWorld.FormationDeviation, freeze = ZWorld.EndFreezeDeviation;
            ZWorld.EndFreezeDeviation = false;
            try
            {
                bool pc44 = ZWorld.PathV3Deviation; (double vee, double gap, double arrive, double stopped) off;   // pathing_v3: the Zod column without DV44 / DV47
                try { ZWorld.PathV3Deviation = false; ZWorld.FormationDeviation = false; off = FormationWalk(); } finally { ZWorld.PathV3Deviation = pc44; }
                ZWorld.FormationDeviation = true; var on = FormationWalk();
                bool pref = getInt("oz_vformation_sim", 1) == 1 && getInt("oz_zod_exact", 0) == 0;
                var lead = off;                                                             // pathing_v3: "the leader is untouched" is checked against the
                if (pc44) { ZWorld.FormationDeviation = false; lead = FormationWalk(); ZWorld.FormationDeviation = true; }   // same world without DV24 (DV44 as shipped: its idle pushes place the squad before the order)
                bool ok = on.vee >= 60 && off.vee == 0 && on.gap >= 11 && Math.Abs(on.arrive - lead.arrive) <= 0.05 * lead.arrive;
                return new Deviation
                {
                    id = "DV24", change = "Squad V formation (leader in front, minions fanned out behind on both sides)",
                    zod = "no follow offsets: CloneMinionWayPoints gives every minion the leader's identical waypoints, so a squad walks one path (stacked in Zod, single file with the OpenZ spacing)",
                    openz = $"while the leader walks a MOVE each minion steers at its slot: row r = (i + 1) / 2 on alternating sides, {ZFormation.RowBack} px back and {ZFormation.SideOut} px out per row, turned with the leader's smoothed heading; squeezed to half width, then single file, where a slot is not free ground in a straight line; bridge / lane keep-clear areas: single file on Zod's path, the V re-opens after; a minion behind its slot hurries at up to {ZFormation.CatchUp}x its speed (Zod runs at 1.8x), in the V it keeps the leader's pace; spacing rules unchanged, the leader untouched",
                    measured = $"L02 grunt squad, open-ground L (34,11) -> (52,11) -> (52,21): V {on.vee:0} % of walking samples (Zod {off.vee:0} %), closest members {on.gap:0.0} px (Zod {off.gap:0.0}), leader arrives {on.arrive:0.0} s (Zod {off.arrive:0.0} s; DV24 off with DV44 {lead.arrive:0.0} s), minions stopped {on.stopped:0.0} s",
                    toggle = $"ZWorld.FormationDeviation (static); PlayerPrefs oz_vformation_sim = {(getInt("oz_vformation_sim", 1) == 1 ? "1 (on, default)" : "0 (off)")}; off in 'Zod exact' (oz_zod_exact = 1)",
                    status = !pref ? DevActive + " (off)" : ok ? DevActive : DevCheck,
                };
            }
            finally { ZWorld.FormationDeviation = saved; ZWorld.EndFreezeDeviation = freeze; }
        }

        static (double vee, double gap, double arrive, double stopped) FormationWalk()
        {
            var w = ZWorld.Create(SettingsText, MapText("L02"), 1);
            w.Objects.RemoveAll(o => o.IsUnit);
            var lead = w.CreateRobotGroup("grunt", 34 * 16, 11 * 16, Team.Red);
            var squad = new List<ZObject> { lead }; squad.AddRange(lead.Minions);
            for (int i = 0; i < 300; i++) { w.Tick(ZConst.TICK); w.Events.Clear(); }
            w.SendWaypoints(Team.Red, lead, new[] { ZWaypoint.Move(52 * 16 + 8, 11 * 16 + 8), ZWaypoint.Move(52 * 16 + 8, 21 * 16 + 8) });
            int samples = 0, vee = 0; double gap = double.MaxValue, arrive = -1, stopped = -1, t0 = w.Time;
            for (int k = 0; k < 12000; k++)
            {
                w.Tick(ZConst.TICK); w.Events.Clear();
                if (arrive < 0 && lead.Waypoints.Count == 0) arrive = w.Time - t0;
                if (arrive >= 0 && squad.All(u => u.Waypoints.Count == 0 && !u.IsMoving)) { stopped = w.Time - t0; break; }
                for (int a = 0; a < squad.Count; a++)
                    for (int b = a + 1; b < squad.Count; b++)
                        gap = Math.Min(gap, Math.Sqrt(Math.Pow(squad[a].PosX - squad[b].PosX, 2) + Math.Pow(squad[a].PosY - squad[b].PosY, 2)));
                double vl = Math.Sqrt(lead.Dx * lead.Dx + lead.Dy * lead.Dy);
                if (k % 5 != 0 || !lead.IsMoving || lead.PathHeld || vl < 1e-6 || lead.Minions.Count < 2) continue;
                double hx = lead.Dx / vl, hy = lead.Dy / vl;
                samples++;
                var lat = lead.Minions.Select(m => -(m.PosX - lead.PosX) * hy + (m.PosY - lead.PosY) * hx).ToList();
                bool behind = lead.Minions.All(m => -((m.PosX - lead.PosX) * hx + (m.PosY - lead.PosY) * hy) >= 4);
                if (behind && Math.Abs(lat[0]) >= 4 && Math.Abs(lat[1]) >= 4 && Math.Sign(lat[0]) != Math.Sign(lat[1])) vee++;
            }
            return (samples > 0 ? 100.0 * vee / samples : 0, gap, arrive, stopped);
        }
    }
}
