using System;
using System.Collections.Generic;

namespace OpenZ.Sim
{
    /// <summary>
    /// Group move goals. A Zod group order gives every selected unit the same MOVE point (zplayer.cpp:2942-3016; the bot
    /// sends one point per unit, zbot.cpp:222-414), so 25 units all drive for one pixel and pile up. Units that receive a
    /// MOVE to the same point in the same tick (player selection, bot wave) get their own spot around it instead: reachable
    /// for that unit (ZReach), room for the unit (vehicle 17 px, lone robot 7 px, squad 7 + 6 px per minion, so its minions
    /// can stand around their leader), nearest the clicked point first, each unit taking the free spot closest to where it
    /// comes from among the innermost free ring. The unit nearest the point keeps the point itself (a flag is still touched).
    /// Minions keep following their leader (Zod: they stop when the leader stops).
    /// </summary>
    public sealed partial class ZPathing
    {
        readonly List<(ZObject o, int x, int y)> pendingOrders = new List<(ZObject, int, int)>();
        public int SlotsAssigned { get; private set; }

        /// <summary>ZWorld.SendWaypoints hook (player and bot orders).</summary>
        public void OnOrder(ZObject o)
        {
            GroupOrderReset(o);                                                            // DV44: a new order: the old group's rules no longer apply (ZCommit.Settle.cs)
            if (!Enabled || o.Waypoints.Count != 1 || !o.CanMove) return;
            var wp = o.Waypoints[0];
            if (wp.mode != WpMode.Move || !o.CanMove) return;
            for (int i = 0; i < pendingOrders.Count; i++) if (pendingOrders[i].o == o) { pendingOrders.RemoveAt(i); break; }
            pendingOrders.Add((o, wp.x, wp.y));
        }

        int SlotRadius(ZObject o) => SettleOn && !spreadLive ? CommitSlotRadius(o) : o.Kind == ZKind.Vehicle ? 17 : 7 + 6 * o.Minions.Count;   // DV44: chassis / V sizes (ZCommit.Settle.cs)

        /// <summary>BeginTick: spread the MOVE goals of every group ordered to one point last tick.</summary>
        void AssignSlots()
        {
            if (pendingOrders.Count == 0) return;
            var done = new bool[pendingOrders.Count];
            for (int i = 0; i < pendingOrders.Count; i++)
            {
                if (done[i]) continue;
                var (o0, px, py) = pendingOrders[i];
                var group = new List<ZObject>();
                for (int j = i; j < pendingOrders.Count; j++)
                {
                    var (oj, xj, yj) = pendingOrders[j];
                    if (done[j] || xj != px || yj != py) continue;
                    done[j] = true;
                    if (Separable(oj) && oj.Waypoints.Count == 1 && oj.Waypoints[0].mode == WpMode.Move && oj.Waypoints[0].x == px && oj.Waypoints[0].y == py)
                        group.Add(oj);
                }
                if (group.Count >= 2) SpreadGroup(group, px, py);
            }
            pendingOrders.Clear();
        }

        void SpreadGroup(List<ZObject> group, int px, int py)
        {
            if (SettleOn && !spreadLive && SpreadGroupV3(group, px, py)) return;              // DV44: spots keep the group's own shape (ZCommit.Settle.cs); v3: a clicked point in a zone keeps the live spots (ZPathV3.cs)
            // nearest to the point first (deterministic: distance, then id)
            group.Sort((a, b) =>
            {
                long da = (long)(a.CenterX - px) * (a.CenterX - px) + (long)(a.CenterY - py) * (a.CenterY - py);
                long db = (long)(b.CenterX - px) * (b.CenterX - px) + (long)(b.CenterY - py) * (b.CenterY - py);
                return da != db ? da.CompareTo(db) : a.Id.CompareTo(b.Id);
            });
            var taken = new List<(int x, int y, int r)>();
            var cands = new Dictionary<(bool robot, bool nr, int comp), List<(int x, int y, long d)>>();
            foreach (var u in group)
            {
                bool robot = u.IsRobot, nr = NoRocks(u);
                int comp = Reach.UnitComp(u, nr);
                if (comp < 0) continue;
                if (!cands.TryGetValue((robot, nr, comp), out var list)) cands[(robot, nr, comp)] = list = Candidates(px, py, robot, nr, comp);
                int r = SlotRadius(u);
                long ringLimit = -1; int best = -1; long bestD = long.MaxValue;
                for (int k = 0; k < list.Count; k++)
                {
                    var (cx, cy, d) = list[k];
                    if (ringLimit >= 0 && d > ringLimit) break;
                    bool free = true;
                    foreach (var t in taken)
                        if ((long)(t.x - cx) * (t.x - cx) + (long)(t.y - cy) * (t.y - cy) < (long)(t.r + r) * (t.r + r)) { free = false; break; }
                    if (!free) continue;
                    if (ringLimit < 0) { double rd = Math.Sqrt(d) + 40; ringLimit = (long)(rd * rd); }   // innermost free ring + 2.5 tiles
                    long du = (long)(u.CenterX - cx) * (u.CenterX - cx) + (long)(u.CenterY - cy) * (u.CenterY - cy);
                    if (du < bestD) { bestD = du; best = k; }
                }
                if (best < 0) continue;
                var (sx, sy, _) = list[best];
                taken.Add((sx, sy, r));
                var w = u.Waypoints[0]; w.x = sx; w.y = sy; u.Waypoints[0] = w;
                u.PS.groupX = px; u.PS.groupY = py; u.PS.groupSlotX = sx; u.PS.groupSlotY = sy;
                foreach (var m in u.Minions) { m.PS.groupX = px; m.PS.groupY = py; m.PS.groupSlotX = sx; m.PS.groupSlotY = sy; }
                if (!u.IsMinion) u.CloneMinionWayPoints();
                SlotsAssigned++;
                Log(u, $"group goal ({px},{py}): own spot ({sx},{sy})");
            }
        }

        /// <summary>Spots in component comp around (px, py), up to 12 tiles, nearest first (the exact point first if valid).</summary>
        List<(int x, int y, long d)> Candidates(int px, int py, bool robot, bool nr, int comp)
        {
            var list = new List<(int x, int y, long d)>();
            if (Reach.PointOk(px, py, robot, nr, comp)) list.Add((px, py, 0));
            var (cx, cy) = ZReach.FootTileAt(px, py, robot);
            for (int ty = cy - 12; ty <= cy + 12; ty++)
                for (int tx = cx - 12; tx <= cx + 12; tx++)
                {
                    if (Reach.Comp(tx, ty, robot, nr) != comp) continue;
                    var (x, y) = ZReach.FootCentre(tx, ty, robot);
                    if (x == px && y == py) continue;
                    if (KeepClearAt(x, y) is ZLane kc && !(spreadLive && kc.Short)) continue;  // never park the group on a bridge, its approach or its mouth (pathing v3: live spots ignore DV44's short lanes)
                    long d = (long)(x - px) * (x - px) + (long)(y - py) * (y - py);
                    var cl = W.Clearance;                                                       // DV22: spots clear of a building's margin first
                    if (cl != null && cl.Enabled && ZClearance.SlotPreference && cl.NearMargin(x, y, robot ? 8 : 16, robot ? ZClearance.RobotMargin : ZClearance.VehicleMargin)) d += 1L << 40;
                    list.Add((x, y, d));
                }
            list.Sort((a, b) => a.d != b.d ? a.d.CompareTo(b.d) : a.y != b.y ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x));
            return list;
        }
    }
}
