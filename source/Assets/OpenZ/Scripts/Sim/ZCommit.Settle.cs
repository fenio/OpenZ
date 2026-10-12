using System;
using System.Collections.Generic;

namespace OpenZ.Sim
{
    /// <summary>
    ///  - Group spots (ZSlots) are spaced by each unit's real size: a vehicle's chassis radius (DV6 ChassisRadii, 16-22 px) + 2, a lone
    ///    robot 8, a squad its whole V (the last row's offset + a robot) - Zod's 17 px for every vehicle put a heavy tank's spot
    ///    inside a medium's spacing, so the second one could never reach it and inched / side-stepped round it.
    ///  - Arrival tolerance: on the last leg of a MOVE within ArriveTol px (robots 24, vehicles 32) of its spot, a unit blocked by a
    ///    team-mate that stands arrives there (the order ends); blocked by one still walking in, it waits for it - it never pushes,
    ///    asks to step aside or side-steps round units at its goal.
    ///  - Separation dead-band + hysteresis: a pair of standing units is only pushed apart when it is DeadBand px inside the spacing
    ///    (walking: 1 px); a push, once started, runs until the pair is PushClear px beyond the spacing (a real move of a pixel or
    ///    more, never a sub-0.1 m correction); settled vehicles are never nudged by robots (the robot steps away), standing units
    ///    get no hand-on "carry" pressure, and a walker that must displace a standing team-mate moves it at least 1 px.
    ///  - DV22's idle ease out of a building margin starts only EaseDeadBand px deep and then runs until the unit is out.
    /// </summary>
    public sealed partial class ZPathing
    {
        public const double DeadBand = 1.0, WalkDeadBand = 0.5, PushClear = 0.75, EaseDeadBand = 1.0;
        public const double ArriveTolRobot = 24, ArriveTolVehicle = 32;
        /// <summary>A unit of a group order blocked by a member of the same order that has arrived stops within its own tolerance + its
        /// group spot's radius of its spot (at most GroupArriveMax px): one unit's width short of it, never several tiles.</summary>
        public const double GroupArriveMax = 64, RegroupArriveTol = 32;
        internal static double GroupArriveTol(ZObject o) => Math.Min(GroupArriveMax, (o.IsRobot ? ArriveTolRobot : ArriveTolVehicle) + CommitSlotRadius(o));
        internal bool SettleOn => CommitOn && CommitSettle;
        /// <summary>ZObject.ProcessServer (DV44): a new waypoint keeps the unit's sub-pixel carry. Zod clears it (clear_and_kill), which moves
        /// a unit up to 1 px (0.12 m) back or sideways the moment it gets a new order, an attack on a rock or a helper move - a visible snap.
        /// Positions stay within 1 px of Zod's; speeds, paths and ranges are unchanged.</summary>

        /// <summary>EndTick: the pairs pushed this tick keep their push going next tick (hysteresis per PAIR: a unit pushed clear of one
        /// neighbour does not start pushing every other neighbour it is near - no chain of pushes through a packed group).</summary>
        void SettleTick()
        {
            foreach (var o in units) { o.PC.pushPartner = SettleOn && o.PS.lastPushed >= W.Time - 1e-9 ? o.PC.pushNext : -1; o.PC.pushNext = -1; }   // (only a push that really moved it goes on)
        }

        /// <summary>PushApart: a pair closer than this is pushed (Zod layer: min - 0.25).</summary>
        double PushStart(ZObject a, ZObject b, double min)
        {
            if (!SettleFor(a, b)) return min - 0.25;
            if (a.PC.pushPartner == b.Id || b.PC.pushPartner == a.Id) return min + PushClear;   // this pair's push runs until it is clear
            return Walking(a) || Walking(b) ? min - WalkDeadBand : min - DeadBand;
        }

        /// <summary>PushApart: the distance a push corrects to (Zod layer: the spacing itself); the pair is remembered for the hysteresis.</summary>
        double PushTarget(ZObject a, ZObject b, double min)
        {
            if (!SettleFor(a, b)) return min;
            a.PC.pushNext = b.Id; b.PC.pushNext = a.Id;
            return min + PushClear;
        }

        /// <summary>PushShare (first): a unit that stands after its order ended takes no share of a push against a unit that still has an
        /// order (walking past, or held / pressing in for a tick - that unit is pushed instead): arrived units are never nudged by
        /// traffic, only a deep overlap moves them (not in lanes).</summary>
        bool ArrivedHolds(ZObject a, ZObject b) => SettleFor(a, b) && a.Waypoints.Count == 0 && !Static(a) && (Walking(b) || b.Waypoints.Count > 0) && !InNarrowArea(a) && !DeepOverlap(a, b);
        static bool DeepOverlap(ZObject a, ZObject b) { double m = MinSpacing(a, b) - 3, dx = CX(a) - CX(b), dy = CY(a) - CY(b); return dx * dx + dy * dy < m * m; }

        /// <summary>PushShare: a vehicle takes no share of a push against a robot that can move (the robot steps away).</summary>
        bool VehicleHolds(ZObject a, ZObject b) => SettleFor(a, b) && a.Kind == ZKind.Vehicle && b.Kind == ZKind.Robot && a.Waypoints.Count == 0;   // (an arrived vehicle)

        /// <summary>TryShove: a vehicle that arrived on the same group order is not sent aside for a mate (the mate goes round it or
        /// arrives next to it): each step-aside was a few px and repeated while the mate drove past - an arrived tank crept. Not in
        /// or at a lane / bridge (the queue's own rules).</summary>
        bool ArrivedVehicle(ZObject a, ZObject b) => SettleFor(a, b) && a.Kind == ZKind.Vehicle && b.Kind == ZKind.Vehicle && GroupSettled(a, b) && !InLaneZone(a) && !InLaneZone(b)
                                                     && a.PS.wait != ZPathWait.Bridge && a.PS.wait != ZPathWait.Lane;   // (bridges and lanes: the queue's own rules)

        /// <summary>TryNudge's hand-on pressure (carry) is not given to a standing unit: its own dead-band push handles a real overlap.</summary>
        bool NoCarry(ZObject c) => SettleFor(c) && c.Waypoints.Count == 0;

        /// <summary>WayBlocked (helper moves): the spacing a planned way keeps to standing units - the gate's own (MinSpacing + 1, the
        /// squad spacing between mates), so a unit is never sent where the gate then holds it (Zod layer: MinSpacing).</summary>
        int WaySpacing(ZObject u, ZObject o) => SettleFor(u, o) ? GateSpacing(u, o) : MinSpacing(u, o);

        /// <summary>DV22 idle ease: start only this deep into the margin, then run until out.</summary>
        bool EaseStarts(ZObject o, double a)
        {
            if (!SettleFor(o)) return a > 0;
            if (a <= 0) { o.PC.easing = false; return false; }
            if (!o.PC.easing && a < EaseDeadBand) return false;
            o.PC.easing = true; return true;
        }

        /// <summary>PlanDisplace: a standing team-mate moved out of a walker's way moves at most its own Zod speed this tick (x DV24's
        /// catch-up on dry ground, never faster in water); one that arrived on the walker's own group order is not moved at all, nor one
        /// in the way of a unit leaving a building (it steps aside to a free spot instead: TryShove - no nudge every second).</summary>
        double DisplaceAmount(ZObject root, ZObject b, double m)
        {
            if (!SettleFor(root, b)) return m;
            if (b.Waypoints.Count > 0) return m > PushRoom(b, lastDt) ? double.MaxValue : m;   // a squad minion on its MOVE: only within its pace (ZCommit.Follow.cs)
            if (root.Waypoints.Count > 0 && root.Waypoints[0].mode == WpMode.ForceMove && !InLaneZone(b))
                return double.MaxValue;                                                     // a unit leaving a building: the idle unit at the door steps aside (TryShove), no nudges
            if (GroupSettled(root, b) && !InLaneZone(b) && !(SameSquad(root, b) && root.Waypoints.Count > 0 && root.Waypoints[0].refId == RefRegroup))
                return double.MaxValue;                                                     // an arrived unit of the walker's own group order: gone round, never nudged (its own squad's regroup may)
            double cap = Math.Max(b.RealMoveSpeed, 1e-6) * lastDt * (SlowGround(b) ? 1.0 : ZFormation.CatchUp);
            return m > cap ? double.MaxValue : m;                                           // it steps aside at its own Zod pace at most (water: never faster), else the walker waits / goes round
        }

        /// <summary>Gate side-step hysteresis: once a unit side-stepped to one side it does not side-step to the other for SideCommit s
        /// (Zod layer: the side is only preferred for 1 s, the other side is still tried and units weave left-right).</summary>
        internal bool SideCommitted(ZObject a, ZPathState s, double deg) => SettleFor(a) && W.Time < s.sideUntil && Math.Sign(deg) != s.sideSign;

        /// <summary>Gate: a unit of the same group order (same clicked point) standing or held ahead of this one on its way: queue
        /// behind it (a shortened step or a wait), no side-step weave round it; the stuck guard's detour goes round it if it stays.</summary>
        internal bool GroupQueue(ZObject a, ZObject b, double hx, double hy)
        {
            if (b == null || !SettleFor(a, b) || b.Owner != a.Owner || a.IsMinion) return false;
            var sa = a.PS; var sb = b.PS;
            if (sa.groupX == int.MinValue || sa.groupX != sb.groupX || sa.groupY != sb.groupY || b.Waypoints.Count == 0) return false;
            double rx = CX(b) - CX(a), ry = CY(b) - CY(a);
            if (rx * hx + ry * hy <= 0 || sb.heldBy == a) return false;                       // ahead of it on its way, not waiting for it
            double bl = Math.Sqrt((double)b.Dx * b.Dx + (double)b.Dy * b.Dy);
            return bl < 1e-6 || b.Dx * hx + b.Dy * hy >= 0;                                   // and not coming the other way (head-on: the gate's own rules)
        }

        /// <summary>A unit of a group order blocked short of its spot by members of its group that already stand round it: its spot
        /// moves to the nearest free place on its own side (within ReslotTiles of it, nearest the clicked point first, reachable in a
        /// straight line past nobody, outside lane areas) - the group fills in from the side it arrives on instead of pushing through.
        /// Never a spot behind it (on the way it came: that was a reversal). At most every 2 s per unit; false = none free (it
        /// arrives where it is).</summary>
        bool ReSlot(ZObject a)
        {
            a.PC.nextReslot = W.Time + 2;
            var s = a.PS; if (s.groupX == int.MinValue || a.Waypoints.Count == 0) return false;
            bool nr = NoRocks(a); int c = Reach.UnitComp(a, nr); if (c < 0) return false;
            var (tx0, ty0) = ZReach.FootTile(a.LocX, a.LocY);
            double hx = a.Waypoints[0].x - a.CenterX, hy = a.Waypoints[0].y - a.CenterY;   // the way to its spot: a free spot behind it is no good (no reversal)
            int bx = int.MinValue, by = 0; long best = long.MaxValue;
            for (int ty = ty0 - ReslotTiles; ty <= ty0 + ReslotTiles; ty++)
                for (int tx = tx0 - ReslotTiles; tx <= tx0 + ReslotTiles; tx++)
                {
                    var (x, y) = ZReach.FootCentre(tx, ty, a.IsRobot);
                    long dg = (long)(x - s.groupX) * (x - s.groupX) + (long)(y - s.groupY) * (y - s.groupY);
                    long da = (long)(x - a.CenterX) * (x - a.CenterX) + (long)(y - a.CenterY) * (y - a.CenterY);
                    long key = dg + da / 4;
                    if (key >= best) continue;
                    if (!Reach.PointOk(x, y, a.IsRobot, nr, c) || Occupied(x, y, a, null) || KeepClearAt(x, y) != null || InBuildingDoor(x, y) || WayBlocked(a, x, y, null) || !HelperLegOk(a, x, y)) continue;
                    double mx = x - a.CenterX, my = y - a.CenterY;
                    if (mx * mx + my * my < 16 * 16 || mx * hx + my * hy < 0) continue;          // (not where it already is, not behind it)
                    best = key; bx = x; by = y;
                }
            if (bx == int.MinValue) return false;
            var w = a.Waypoints[0]; w.x = bx; w.y = by; a.Waypoints[0] = w;
            s.groupSlotX = bx; s.groupSlotY = by;
            if (!a.IsMinion) a.CloneMinionWayPoints();
            Log(a, $"DV44: its spot lies behind its arrived group: takes a free one at ({bx},{by})");
            return true;
        }
        public const int ReslotTiles = 4;

        /// <summary>Within the squad's regroup reach of its leader.</summary>
        /// <summary>Review round 3 (island E->W: a minion on its own path stopped in the group's footprint 12 tiles short of its leader, who
        /// walked on; its regroups then failed against the standing group): a minion whose leader still walks his MOVE does not stop with
        /// the group - it stops with its squad (Zod: minions stop when their leader stops).</summary>
        /// <summary>A minion on its way back to its leader (a regroup, or its own path while he walks on) that an arrived unit of the group has
        /// held for FootprintHoldTicks may nudge it like any unit (the live rules): the arrived-group protection must not wall a squad member
        /// off from its squad (it gave up there and its regroups were dropped for ever).</summary>
        internal bool MinionMustPass(ZObject a, ZObject b) => a.IsMinion && a.PS.heldBy == b && a.PS.heldTicks >= FootprintHoldTicks
                                                              && (a.Waypoints[0].refId == RefRegroup || LeaderWalksOn(a));
        bool LeaderWalksOn(ZObject a) => a.IsMinion && a.Leader != null && !a.Leader.Destroyed && a.Leader.Waypoints.Count > 0 && Moving(a.Leader);
        bool NearLeader(ZObject m) { double dx = CX(m) - CX(m.Leader), dy = CY(m) - CY(m.Leader), r = RegroupLimit(m.Leader); return dx * dx + dy * dy <= r * r; }

        /// <summary>The unit's box overlaps a lane's zone (corridor + approach).</summary>
        bool InLaneZone(ZObject o)
        {
            double lx = o.LocX + o.XOver, ly = o.LocY + o.YOver;
            foreach (var L in Lanes) if (BoxInZone(L, lx, ly, o.WidthPix, o.HeightPix)) return true;
            return false;
        }

        /// <summary>Detour (DV44): a "detour" of one point is no way round anything - taking it pressed the unit into the same units again
        /// (at an unreachable goal an overlap that never ended). It is taken only as a short hop (at most OneHop px, e.g. the last tile of a
        /// waypoint list round a fort) whose straight leg does not pass the unit it is held by; it is then walked as a two-point path.</summary>
        internal bool NoWayRound(ZObject o, ref List<(int x, int y)> pts)
        {
            if (!SettleFor(o) || pts == null || pts.Count >= 2) return false;
            if (pts.Count == 0) return true;
            double ax = CX(o), ay = CY(o), bx = pts[0].x - ax, by = pts[0].y - ay, l2 = bx * bx + by * by;
            if (l2 > OneHop * OneHop) return true;
            var hb = o.PS.heldBy;
            if (hb != null && !hb.Destroyed)
            {
                double hx = CX(hb) - ax, hy = CY(hb) - ay, t = l2 < 1e-9 ? 0 : Math.Max(0, Math.Min(1, (hx * bx + hy * by) / l2));
                double ex = hx - t * bx, ey = hy - t * by, min = MinSpacing(o, hb);
                if (ex * ex + ey * ey < min * min) return true;
            }
            pts = new List<(int x, int y)> { (o.CenterX, o.CenterY), pts[0] };           // the hop as a path of two points (the path code reads two)
            return false;
        }
        public const double OneHop = 24;

        /// <summary>Review round 3 (the DV22 fort walk, the stress rows): the live layer resolves an unreachable goal whose nearest reachable
        /// point lies in a lane / gap / bridge area to a point outside it (up to 3 tiles farther) so that a unit never PARKS there. A waypoint
        /// that is not the order's last one is only passed through: it resolves to the plain nearest reachable point (Zod's own end of the
        /// path), else a chain of waypoints along a lane (a perimeter walk, a queued route) sends the unit out of the lane and back for every
        /// one of them. The last waypoint (where the unit parks) keeps the live rule.</summary>
        internal bool NearestReachableFor(ZObject o, int x, int y, bool robot, bool nr, int c, out int rx, out int ry)
        {
            if (SettleFor(o) && o.Waypoints.Count > 1 && o.Waypoints[0].mode == WpMode.Move && o.Waypoints[1].mode == WpMode.Move)
                return Reach.Nearest(x, y, robot, nr, c, out rx, out ry);
            return NearestReachable(x, y, robot, nr, c, out rx, out ry);
        }

        /// <summary>Detour hysteresis and budget (round its own group that already stands there): a way round those units longer than
        /// DetourFactor x the rest of the current path (and DetourExtra px more) is not taken, and after DetourBudget detours on one
        /// order a unit within GroupNear of the clicked point arrives where it is (it circled its arrived group: close enough) instead
        /// of driving round the block again. Never in or at a lane / gap / bridge (it must not park there).</summary>
        internal bool DetourTooLong(ZObject o, List<(int x, int y)> pts)
        {
            if (!SettleFor(o) || pts == null || pts.Count == 0 || o.Waypoints.Count == 0 || o.Waypoints[0].mode != WpMode.Move || o.Waypoints[0].refId < -1) return false;   // (a helper move - regroup, step aside, back off, bay - must get there)
            var hb = o.PS.heldBy;
            if (hb == null || !(GroupSettled(o, hb) || GroupMateStands(o, hb))) return false;   // only round its own group that stands there
            var c = o.PC; var wp = o.Waypoints[0];
            if (!wp.Equals(c.detourWp)) { c.detourWp = wp; c.detours = 0; }
            c.detours++;
            double rem = o.PathRemaining();
            double len = 0, px = o.CenterX, py = o.CenterY;
            foreach (var p in pts) { len += Math.Sqrt((p.x - px) * (p.x - px) + (p.y - py) * (p.y - py)); px = p.x; py = p.y; }
            double sx = o.Waypoints[0].x - CX(o), sy = o.Waypoints[0].y - CY(o), straight = Math.Sqrt(sx * sx + sy * sy);
            bool longWay = (rem <= 2 * GroupArriveTol(o) + DetourNear || InGroupFootprint(o, GroupEdge)) && len > Math.Max(Math.Max(rem, straight) * DetourFactor, Math.Max(rem, straight) + DetourExtra);   // (at its group: never round the block)
            double gx = o.PS.groupX - CX(o), gy = o.PS.groupY - CY(o);
            bool circled = c.detours > DetourBudget && gx * gx + gy * gy <= GroupNear * GroupNear;
            if (!longWay && !circled) return false;
            if (InNarrowArea(o)) return false;
            o.PS.arrive = true;                                                             // its group stands round its spot: this is close enough
            Log(o, longWay ? $"DV44: detour of {len:0} px for {rem:0} px left round its own arrived group refused: arrived"
                           : $"DV44: detour {c.detours} round its own arrived group, {Math.Sqrt(gx * gx + gy * gy) / 16:0.0} tiles from the clicked point: arrived");
            return true;
        }
        public const double DetourFactor = 2.5, DetourExtra = 192, DetourNear = 64, GroupNear = 12 * 16;

        /// <summary>Review round 3 (L02 east bridge N->S, 3 of 4 order timings: a medium tank 90 px from its spot, walled in by its standing
        /// group, took an 18-point "detour" back over the bridge and round the island, then shuttled between the two bridges until the 360 s
        /// cap): a detour round standing units to an end point that is near (straight line within DetourLocal px) is refused when it is longer
        /// than DetourFactor x the straight line (and DetourExtra px more), for any move - a way round the map is never the way to a spot a few
        /// tiles off. On its group order (outside lanes / gaps / bridges) the unit arrives where it stands; any other move goes on as without
        /// a detour (the stuck guard: re-path, nearest reachable, give up / drop the helper move).</summary>
        internal bool LongWayToNearGoal(ZObject o, List<(int x, int y)> pts)
        {
            if (!SettleFor(o) || pts == null || pts.Count == 0) return false;
            if (o.Waypoints.Count > 0 && o.Waypoints[0].refId == RefRegroup) return false;   // pathing v3: a minion rejoining its squad takes the way round (refused, it gave up 9 tiles off: the canal)
            var (ex, ey) = pts[pts.Count - 1];
            double ox = CX(o), oy = CY(o), straight = Math.Sqrt((ex - ox) * (ex - ox) + (ey - oy) * (ey - oy));
            if (straight > DetourLocal) return false;
            double len = 0, px = ox, py = oy;
            foreach (var p in pts) { len += Math.Sqrt((p.x - px) * (p.x - px) + (p.y - py) * (p.y - py)); px = p.x; py = p.y; }
            if (len <= Math.Max(straight * DetourFactor, straight + DetourExtra)) return false;
            bool group = OnGroupOrder(o) && !InNarrowArea(o);
            if (group) o.PS.arrive = true;                                                   // (its group order: it stops here, next to the units round its spot)
            Log(o, $"DV44: a {len:0} px detour round standing units to a point {straight:0} px away refused{(group ? ": arrived" : "")}");
            return true;
        }
        public const double DetourLocal = 160;

        /// <summary>ZStuckGuard.ParkingInLane (review round 3): the live rule lets a unit within 40 px of its goal stop even with its box in a
        /// lane / gap / bridge zone the goal lies outside ("the goal itself is here"); a group spot just outside a bridge's approach then
        /// parks its unit in the approach (L02 east bridge N->S: a squad leader walled his minion and a tank in on the deck). With DV44 such a
        /// unit walks the last pixels out of a BRIDGE zone (deck, approach, mouth); gaps and corridors keep the live 40 px rule (the strict
        /// rule there cost the head-on runs reversals and settle time).</summary>
        /// <summary>How far outside its group's footprint a unit still counts as at its group (a long way round the group is refused).</summary>
        public const double GroupEdge = 128;
        public const int DetourBudget = 2;
        /// <summary>A squad's V trails its leader: its group spot's radius covers this share of the V's reach (neighbouring spots may
        /// take the room beside the V's open end).</summary>
        public const double SquadSlotShare = 0.5;
        /// <summary>Review round 3 (stress rows ended farther from the goal than live): a unit's spot is the free one nearest its ideal place
        /// AMONG the spots of the innermost free ring + RingSlack px (the live ZSlots rule), so the group's shape never pushes a unit out
        /// past the ring live would give it (an ideal place in water / a wall no longer sends a vehicle 16 tiles down the shore).</summary>
        public const double RingSlack = 40;

        /// <summary>ZSlots (DV44): a group order keeps the group's own shape. Every unit's ideal spot is the clicked point + its offset from
        /// the group's centre, the offsets shrunk until the group fits a compact disc (its members' room: their spot radii, packed); each
        /// unit then takes the free spot nearest its ideal one (the clicked point's candidates: reachable for it, outside lane keep-clear
        /// areas, room for its size), inner ones first. Units keep their places relative to each other on the way and at the goal - no
        /// crossing through the group, no unit sent round it to a spot on the far side - and the whole group stays within its footprint
        /// of the point (GroupFootprint: a unit held by standing mates inside it has arrived). Deterministic (id order ties).</summary>
        void SpreadGroupCommit(List<ZObject> group, int px, int py)
        {
            double cx = 0, cy = 0; foreach (var u in group) { cx += u.CenterX; cy += u.CenterY; }
            cx /= group.Count; cy /= group.Count;
            double area = 0, spread = 0;
            foreach (var u in group)
            {
                double r = SlotRadius(u); area += Math.PI * r * r;
                spread = Math.Max(spread, Math.Sqrt((u.CenterX - cx) * (u.CenterX - cx) + (u.CenterY - cy) * (u.CenterY - cy)) + r);
            }
            double disc = Math.Sqrt(area / Math.PI / PackDensity);                            // the compact disc the group fits in
            double k = spread > disc && spread > 1e-6 ? disc / spread : 1;
            var order = new List<(double d, int id, ZObject u, double ix, double iy)>();
            foreach (var u in group)
            {
                double ix = px + (u.CenterX - cx) * k, iy = py + (u.CenterY - cy) * k;
                order.Add(((ix - px) * (ix - px) + (iy - py) * (iy - py), u.Id, u, ix, iy));
            }
            order.Sort((x, y) => x.d != y.d ? x.d.CompareTo(y.d) : x.id.CompareTo(y.id));   // inner units first
            var taken = new List<(int x, int y, int r)>();
            var cands = new Dictionary<(bool robot, bool nr, int comp), List<(int x, int y, long d)>>();
            var doors = new Dictionary<(bool robot, bool nr, int comp), bool[]>();              // (spots in a building's door: only when nothing else)
            foreach (var (_, _, u, ix0, iy0) in order)
            {
                bool robot = u.IsRobot, nr = NoRocks(u); double ix = ix0, iy = iy0;
                int comp = Reach.UnitComp(u, nr);
                if (comp < 0) continue;
                if (!cands.TryGetValue((robot, nr, comp), out var list))
                {
                    cands[(robot, nr, comp)] = list = Candidates(px, py, robot, nr, comp);
                    GroupWalkPenalty(list, px, py, robot, nr, comp);                              // A/B fix 1: spots far by foot last (ZPathV3.Owner.cs)
                    var dl = new bool[list.Count]; for (int i = 0; i < list.Count; i++) dl[i] = InBuildingDoor(list[i].x, list[i].y);
                    doors[(robot, nr, comp)] = dl;
                }
                var door = doors[(robot, nr, comp)];
                int r = SlotRadius(u); int best = -1; double bestC = double.MaxValue; long ringLimit = -1;
                for (int i = 0; i < list.Count; i++)
                {
                    var (sx, sy, d) = list[i];
                    if (d >= (1L << 40) || door[i]) continue;                                    // (a building's margin or door: only when nothing else)
                    if (ringLimit >= 0 && d > ringLimit) break;                                  // review round 3: the live ring rule (innermost free ring + 2.5 tiles)
                    bool free = true;
                    foreach (var t in taken) if ((long)(t.x - sx) * (t.x - sx) + (long)(t.y - sy) * (t.y - sy) < (long)(t.r + r) * (t.r + r)) { free = false; break; }
                    if (!free) continue;
                    if (ringLimit < 0) { double rd = Math.Sqrt(d) + RingSlack; ringLimit = (long)(rd * rd); }
                    double c = (sx - ix) * (sx - ix) + (sy - iy) * (sy - iy);
                    if (c >= bestC) continue;
                    bestC = c; best = i;
                }
                if (best < 0)
                    for (int i = 0; i < list.Count; i++)
                    {                                                                            // (then the margin spots too)
                        var (sx, sy, d) = list[i];
                        double c = (sx - ix) * (sx - ix) + (sy - iy) * (sy - iy);
                        if (c >= bestC) continue;
                        bool free = true;
                        foreach (var t in taken) if ((long)(t.x - sx) * (t.x - sx) + (long)(t.y - sy) * (t.y - sy) < (long)(t.r + r) * (t.r + r)) { free = false; break; }
                        if (!free) continue;
                        bestC = c; best = i;
                    }
                if (best < 0) continue;
                var (bx, by, _) = list[best];
                taken.Add((bx, by, r));
                var w = u.Waypoints[0]; w.x = bx; w.y = by; u.Waypoints[0] = w;
                u.PS.groupX = px; u.PS.groupY = py; u.PS.groupSlotX = bx; u.PS.groupSlotY = by;
                foreach (var m in u.Minions) { m.PS.groupX = px; m.PS.groupY = py; m.PS.groupSlotX = bx; m.PS.groupSlotY = by; }
                if (!u.IsMinion) u.CloneMinionWayPoints();
                SlotsAssigned++;
                Log(u, $"group goal ({px},{py}): own spot ({bx},{by}) (DV44: its place in the group, ideal ({ix:0},{iy:0}))");
            }
            GroupFootprint(group, px, py);
        }
        /// <summary>A spot in a building's door: closer than DoorClear px to the way a new unit leaves it (its creation point -> the
        /// exit point it force-moves to, Zod's GetBuildingCreationPoint / GetBuildingCreationMovePoint). A group parked there blocked
        /// the building's output (the new unit pressed into it every second); its spots go elsewhere, the door only as a last resort.</summary>
        bool InBuildingDoor(int x, int y)
        {
            foreach (var b in W.Objects)
            {
                if (b.Kind != ZKind.Building || b.Destroyed || !b.GetCreationPoint(out int cx, out int cy) || !b.GetCreationMovePoint(out int ex, out int ey)) continue;
                double dx = ex - cx, dy = ey - cy, l2 = dx * dx + dy * dy, t = l2 < 1e-9 ? 0 : Math.Max(0, Math.Min(1, ((x - cx) * dx + (y - cy) * dy) / l2));
                double qx = cx + t * dx - x, qy = cy + t * dy - y;
                if (qx * qx + qy * qy < DoorClear * DoorClear) return true;
            }
            return false;
        }
        public const double DoorClear = 40;

        /// <summary>How tightly a group's spot discs are packed in its compact disc (share of the area).</summary>
        public const double PackDensity = 0.55;
        /// <summary>A unit of a group held this long by standing group mates inside the group's footprint has arrived (no way through).</summary>
        public const int FootprintHoldTicks = 100;

        /// <summary>ZSlots (DV44): the footprint of a group order - how far its spots reach from the clicked point (spot + unit room) - kept
        /// with every member: a unit of the group held by a standing mate inside it has arrived (it stops with its group).</summary>
        void GroupFootprint(List<ZObject> group, int px, int py)
        {
            if (!SettleOn) return;
            double r = 0;
            foreach (var u in group)
            {
                if (u.PS.groupX != px || u.PS.groupY != py) continue;
                double dx = u.PS.groupSlotX - px, dy = u.PS.groupSlotY - py;
                r = Math.Max(r, Math.Sqrt(dx * dx + dy * dy) + SlotRadius(u));
            }
            foreach (var u in group) { u.PC.groupR = r; foreach (var m in u.Minions) if (m != null) m.PC.groupR = r; }
        }

        /// <summary>The unit stands inside its group order's footprint (+ its own arrival tolerance).</summary>
        bool InGroupFootprint(ZObject a, double extra = 0)
        {
            var s = a.PS; if (!OnGroupOrder(a) || a.PC.groupR <= 0) return false;
            double dx = CX(a) - s.groupX, dy = CY(a) - s.groupY, r = a.PC.groupR + (a.IsRobot ? ArriveTolRobot : ArriveTolVehicle) + extra;
            return dx * dx + dy * dy <= r * r;
        }

        /// <summary>b is a unit of the same group order as a (same clicked point) that stands: arrived, or held (waiting) with its order.</summary>
        bool GroupMateStands(ZObject a, ZObject b) => OnGroupOrder(a) && a.PS.groupX == b.PS.groupX && a.PS.groupY == b.PS.groupY && (b.Waypoints.Count == 0 || !Moving(b) || b.PathHeld);
        /// <summary>The unit walks the MOVE of its group order (its spot; ReSlot keeps it current), not a later order of its own.</summary>
        internal static bool OnGroupOrder(ZObject a) => a.PS.groupX != int.MinValue && a.Waypoints.Count > 0 && a.Waypoints[0].mode == WpMode.Move && a.Waypoints[0].refId == -1;

        /// <summary>ZSlots.OnOrder (DV44): any new order ends the unit's (and its squad's) membership of an earlier group order; the
        /// group rules then only see units of the same, current order (a retargeted spot stays the same order).</summary>
        void GroupOrderReset(ZObject o)
        {
            LiveOrderReset(o);                                                               // pathing v3: the last group order's live arrival rules end (ZPathV3.cs)
            if (!CommitOn || !CommitSettle || o.PS.groupX == int.MinValue) return;
            o.PS.groupX = int.MinValue; o.PC.groupR = 0;
            foreach (var m in o.Minions) if (m != null) { m.PS.groupX = int.MinValue; m.PC.groupR = 0; }
        }

        /// <summary>Regroup (DV44): the timed retry of a minion's regroup to the same leader spot runs RegroupTries times at most (no endless
        /// 20 s loop of regroup -> crowd arrival short of the leader -> regroup); a leader that moves, or the minion pushed away, starts over.</summary>
        bool RegroupSettled(ZObject m)
        {
            if (!SettleFor(m)) return false;
            var l = m.Leader; if (l == null) return false;
            if (m.PC.regroupSpotX != l.CenterX || m.PC.regroupSpotY != l.CenterY) { m.PC.regroupSpotX = l.CenterX; m.PC.regroupSpotY = l.CenterY; m.PC.regroupTries = 0; }
            return ++m.PC.regroupTries > RegroupTries;                                       // (a few retries at one leader spot, then it stays)
        }
        public const int RegroupTries = 3;

        /// <summary>ZSlots: room per unit in a group order.</summary>
        internal static int CommitSlotRadius(ZObject o) => o.Kind == ZKind.Vehicle || o.Minions.Count == 0 ? SquadReach(o) : Math.Max(7 + 6 * o.Minions.Count, (int)Math.Ceiling((SquadReach(o) - Radius(o) - 2) * SquadSlotShare) + Radius(o) + 2);

        /// <summary>How far the unit reaches round its centre: a vehicle's chassis, a robot's body, a squad's whole V (its last row's
        /// outer slot + a body) - the regroup reach of a squad.</summary>
        internal static int SquadReach(ZObject o)
        {
            if (o.Kind == ZKind.Vehicle) return VehicleRadiusOf(o.Type) + 2;
            if (o.Minions.Count == 0) return Radius(o) + 2;
            var (back, side) = ZFormation.Offset(o.Minions.Count, o.Type);
            return (int)Math.Ceiling(Math.Sqrt((double)back * back + (double)side * side)) + Radius(o) + 2;
        }

        /// <summary>GateCore, after crowd arrival: near its goal a unit blocked by a team-mate arrives (the mate stands) or waits
        /// (it walks in too); never pushes, shoves or side-steps round units at its goal.</summary>
        bool NearGoalBlocked(ZObject a, ZObject b, out bool arrive)
        {
            arrive = false;
            if (!SettleFor(a) || b == null || b.Owner != a.Owner || a.PathMode != WpMode.Move) return false;
            var wp = a.Waypoints[0];
            bool regroup = wp.refId == RefRegroup && SameSquad(a, b);                      // a minion walking to its V slot next to its mates
            if (!regroup && (wp.refId == RefStepAside || wp.refId == RefBackOff || wp.refId == RefBay || wp.refId == RefRegroup)) return false;
            if (wp.refId >= 0) return false;                                            // onto a flag / object: must really get there (Zod touch)
            if (a.InFormation) return false;                                            // walking with its squad: its goal is the leader's
            double rem = a.PathRemaining(), tol = a.IsRobot ? ArriveTolRobot : ArriveTolVehicle;
            if (!regroup && wp.refId == -1 && GroupMateStands(a, b) && InGroupFootprint(a) && !InNarrowArea(a) && !LeaderWalksOn(a)
                && (a.PathRemaining() <= GroupArriveTol(a) + SlotRadius(a) || (a.PS.heldTicks >= FootprintHoldTicks && a.PS.heldBy != null && GroupMateStands(a, a.PS.heldBy))))
            {
                if (FootprintReslot && W.Time >= a.PC.nextReslot && ReSlot(a)) return true;       // pathing v3: a free spot nearer the clicked point on its own side first
                arrive = true; Log(a, $"DV44: in its group's footprint, {b.Type}#{b.Id} of the group stands there: arrived"); return true;   // the group stops where it stands, close enough
            }
            bool group = !regroup && GroupSettled(a, b);
            if (group) tol = Math.Max(tol, GroupArriveTol(a));                              // blocked by its own group, already there: close enough
            if (regroup) tol = RegroupArriveTol;                                         // rejoining its leader: it gets close
            if (regroup && (!Moving(b) || b.PathHeld) && a.Leader != null && NearLeader(a) && !InNarrowArea(a) && (b == a.Leader || NearLeader(b)))
            { arrive = true; Log(a, $"DV44: rejoined its squad next to {b.Type}#{b.Id}"); return true; }   // (never in a gap / lane / on a deck, never next to a mate itself out of reach)
            if (rem > tol) return false;
            var (gx, gy) = a.PathFinalPoint;
            if (group ? InLaneZone(a) : ParkingInLane(a, gx, gy, rem)) return false;      // (group: only the corridor and its approach stay clear)
            bool standing = !Moving(b) || b.PathHeld;
            if (!standing)
            {                                                                            // it walks in too (its goal is next to ours): wait for it;
                if (b.Waypoints.Count == 0) return false;                                // one only passing through (another way) is the gate's business
                var (bgx, bgy) = b.PathFinalPoint;
                return (bgx - gx) * (bgx - gx) + (bgy - gy) * (bgy - gy) <= 4 * tol * tol;
            }
            double own = a.IsRobot ? ArriveTolRobot : ArriveTolVehicle;
            if (group && rem > own && W.Time >= a.PC.nextReslot && ReSlot(a)) return true;   // its spot lies behind its arrived group: a free one on this side
            arrive = true; Log(a, $"DV44: {rem:0} px from its spot, {b.Type}#{b.Id} stands there: arrived");
            return true;
        }
    }
}
