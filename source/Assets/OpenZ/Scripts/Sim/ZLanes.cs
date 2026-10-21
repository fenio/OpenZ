using System;
using System.Collections.Generic;

namespace OpenZ.Sim
{
    /// <summary>A one-lane stretch: a bridge deck or a chokepoint where two units cannot pass each other.</summary>
    public sealed class ZLane
    {
        public int X0, Y0, X1, Y1;              // corridor rect, px [X0, X1) x [Y0, Y1)
        public int ZX0, ZY0, ZX1, ZY1;          // zone = corridor + approach at both ends along the axis
        public int KX0, KY0, KX1, KY1;          // keep-clear area = zone + 3 tiles more along the axis, + 1 tile each side:
                                                // nobody parks there (the way in and out of the lane stays open)
        public bool Vertical;                   // travel axis y (else x)
        public bool VehicleLane;                // 2-3 tiles wide: one vehicle at a time; else 1 tile: one robot at a time
        public bool Bridge;                     // corridor lies on a bridge deck
        public bool Short;                      // pathing v3 DV44: a one-tile-long chokepoint (ZCommit.Lanes.cs; not a live lane, never a v3 zone)
        // live occupancy this tick, [0] = +axis travel, [1] = -axis travel
        internal readonly int[] veh = new int[2], rob = new int[2];
        internal readonly ZObject[] occ = new ZObject[2];
        // units with an order into the corridor standing / walking in the approach part at each end, [d] = the end they enter
        // from (d = their travel direction once in): a vehicle goes in alone, robots in any number
        internal readonly int[] apprVeh = new int[2], apprRob = new int[2];
        internal readonly ZObject[] apprOcc = new ZObject[2];
        internal readonly double[] waitSince = { -1, -1 };
        internal readonly bool[] waited = new bool[2];
        // vehicles first (ZYield.cs): the robots in rob / apprRob per team ([d * 3 + team]); who waited at each end this tick / last tick
        // (bit team: robots of that team, bit 3: a vehicle)
        internal readonly int[] robOf = new int[6], apprRobOf = new int[6];
        internal readonly int[] waitWho = new int[2], waitWhoWas = new int[2];
        public int Holds;                       // units held at this lane (stress test / debug)
        public override string ToString() => $"{(Bridge ? "bridge" : VehicleLane ? "vehicle lane" : "robot lane")} {(Vertical ? "N-S" : "E-W")} tiles ({X0 / 16},{Y0 / 16})-({X1 / 16 - 1},{Y1 / 16 - 1})";
    }

    /// <summary>
    /// Bridge and chokepoint queueing (the pathing layer's lanes). Zod lets units drive through each other, so a column meeting
    /// another column on a 2-tile bridge deck just overlaps. With spacing in the sim, two vehicles cannot pass on a deck 32 px
    /// wide (bbridge.cpp:168-190: rails impassable, deck 2 tiles) or in any corridor under 4 tiles, and two robots cannot pass
    /// in a 1-tile gap (two files of robots meeting on a deck jam it too). A lane admits traffic in one direction at a time: a
    /// unit about to enter the zone (corridor + 40 px of approach) against oncoming traffic waits at the zone edge; when both
    /// ends wait, the end that has waited longer than 6 s gets the lane next (the other end stops admitting), so columns
    /// alternate instead of jamming. A vehicle enters the approach at an end only when nobody else heading in is there, and
    /// robots do not enter it while a vehicle is there (a crowd funnelling into a bridge otherwise knots at its mouth: a tank
    /// needs the whole deck width, robots beside it block it and it blocks them); the others wait just outside, in turn. A
    /// unit entering the approach from the side counts as travelling into the lane. Against oncoming traffic a unit waits
    /// already where it would enter the lane's keep-clear area (3 tiles before the approach), so the traffic coming out
    /// finds the mouth open instead of a crowd standing in it. Crossing traffic (not going through the corridor) is never held. Lanes are found from the current grids
    /// and rebuilt when passability changes.
    /// </summary>
    public sealed partial class ZPathing
    {
        public readonly List<ZLane> Lanes = new List<ZLane>();
        /// <summary>Unit-ticks held at lane entries since the world started (stress test / debug).</summary>
        public long LaneHolds;
        int lanesVersion = int.MinValue;

        // ---- the lanes near a point: the lane rules below ask about the lanes round one unit, not about every lane of the map
        const int LaneCell = 64, LaneNearMargin = 48;
        List<ZLane>[] laneCells; int laneCellsW, laneCellsH, laneCellsStamp = -1, laneCellsCount = -1;
        /// <summary>Counts every change of the lane list (EnsureLanes; pathing v3: SyncLanes): the cells are rebuilt after it.</summary>
        int lanesStamp;

        /// <summary>The lanes whose areas (corridor, zone, keep-clear area) come within LaneNearMargin px of the 64 px cell that holds
        /// (x, y), in the order of the lane list; for a point off the map the whole list. A test of a point, or of a unit's box, against
        /// a lane's areas can only be true for a lane of this list while the tested place lies within LaneNearMargin px of (x, y).</summary>
        List<ZLane> LanesNear(double x, double y)
        {
            if (laneCellsStamp != lanesStamp || laneCellsCount != Lanes.Count || laneCells == null) BuildLaneCells();
            int cx = (int)Math.Floor(x / LaneCell), cy = (int)Math.Floor(y / LaneCell);
            return cx >= 0 && cy >= 0 && cx < laneCellsW && cy < laneCellsH ? laneCells[cy * laneCellsW + cx] : Lanes;   // (off the map, NaN: the whole list)
        }

        /// <summary>The lanes a unit's box can touch at top-left (ox, oy) and after a step to (nx, ny): the list of its centre's cell
        /// (a box of at most 32 px and a step of at most 14 px stay within 30 px of the centre); anything bigger: the whole list.</summary>
        List<ZLane> LanesAt(ZObject a, double ox, double oy, double nx, double ny) =>
            a.WidthPix <= 32 && a.HeightPix <= 32 && Math.Abs(nx - ox) <= 14 && Math.Abs(ny - oy) <= 14 ? LanesNear(ox + a.WidthPix * 0.5, oy + a.HeightPix * 0.5) : Lanes;
        List<ZLane> LanesAt(ZObject a) => LanesAt(a, a.LocX + a.XOver, a.LocY + a.YOver, a.LocX + a.XOver, a.LocY + a.YOver);

        void BuildLaneCells()
        {
            laneCellsStamp = lanesStamp; laneCellsCount = Lanes.Count;
            int w = Math.Max(1, W.Map.WidthPix) / LaneCell + 1, h = Math.Max(1, W.Map.HeightPix) / LaneCell + 1;
            if (laneCells == null || w != laneCellsW || h != laneCellsH)
            {
                laneCellsW = w; laneCellsH = h; laneCells = new List<ZLane>[w * h];
                for (int i = 0; i < laneCells.Length; i++) laneCells[i] = new List<ZLane>();
            }
            else foreach (var c in laneCells) c.Clear();
            foreach (var L in Lanes)                                                       // in list order: every cell keeps the order of the list
            {
                int x0 = Math.Min(L.X0, Math.Min(L.ZX0, L.KX0)) - LaneNearMargin, x1 = Math.Max(L.X1, Math.Max(L.ZX1, L.KX1)) + LaneNearMargin;
                int y0 = Math.Min(L.Y0, Math.Min(L.ZY0, L.KY0)) - LaneNearMargin, y1 = Math.Max(L.Y1, Math.Max(L.ZY1, L.KY1)) + LaneNearMargin;
                int cx0 = Math.Max(0, (int)Math.Floor((double)x0 / LaneCell)), cx1 = Math.Min(w - 1, (int)Math.Floor((double)x1 / LaneCell));
                int cy0 = Math.Max(0, (int)Math.Floor((double)y0 / LaneCell)), cy1 = Math.Min(h - 1, (int)Math.Floor((double)y1 / LaneCell));
                for (int cy = cy0; cy <= cy1; cy++)
                    for (int cx = cx0; cx <= cx1; cx++) laneCells[cy * w + cx].Add(L);
            }
        }

        /// <summary>Find the lanes on the current grids (again when passability changed). Runs every tick; tests call it before
        /// the first tick.</summary>
        public void EnsureLanes()
        {
            var pf = W.Path;
            if (lanesVersion == pf.Version && lanesShort == ShortLanes) return;            // DV44: short chokepoints are lanes too (ZCommit.Lanes.cs)
            lanesShort = ShortLanes; squadRes.Clear(); lanesBuilds++; lanesStamp++;
            lanesVersion = pf.Version;
            Lanes.Clear();
            FindLanes(false, 2, 3, true);      // vehicle grid: corridors 2-3 tiles wide (one vehicle)
            FindLanes(false, 2, 3, false);
            FindLanes(true, 1, 1, true);       // robot grid: 1-tile gaps (one robot)
            FindLanes(true, 1, 1, false);
            foreach (var o in W.Objects)
                if (o.IsBridge)
                    foreach (var l in Lanes)
                        if (l.X0 < o.LocX + o.WidthPix && o.LocX < l.X1 && l.Y0 < o.LocY + o.HeightPix && o.LocY < l.Y1) l.Bridge = true;
        }

        /// <summary>Scan rows (vertical corridors) or columns (horizontal corridors) for free runs of minW..maxW tiles bounded
        /// by blocked tiles on both sides; stack runs that overlap by at least minW tiles into one corridor of 2+ rows.</summary>
        void FindLanes(bool robot, int minW, int maxW, bool vertical)
        {
            var pf = W.Path; int w = pf.W, h = pf.H;
            int lines = vertical ? h : w, len = vertical ? w : h;
            bool P(int along, int line) => vertical ? pf.TilePassableGrid(along, line, robot, false) : pf.TilePassableGrid(line, along, robot, false);
            var open = new List<(int a0, int a1, int l0, int l1)>();          // running corridors: along-range [a0,a1], lines [l0,l1]
            var next = new List<(int a0, int a1, int l0, int l1)>();
            for (int line = 0; line <= lines; line++)
            {
                next.Clear();
                if (line < lines)
                {
                    int a = 0;
                    while (a < len)
                    {
                        if (!P(a, line)) { a++; continue; }
                        int s = a; while (a < len && P(a, line)) a++;
                        int e = a - 1, run = e - s + 1;                                       // maximal run: walled (or map edge) both sides
                        if (run < minW || run > maxW) continue;
                        int idx = -1;
                        for (int i = 0; i < open.Count; i++)
                            if (Math.Min(open[i].a1, e) - Math.Max(open[i].a0, s) + 1 >= minW &&
                                Math.Max(open[i].a1, e) - Math.Min(open[i].a0, s) + 1 <= maxW + 1) { idx = i; break; }   // a staggered corridor starts a new lane
                        if (idx >= 0) { var o = open[idx]; open.RemoveAt(idx); next.Add((Math.Min(o.a0, s), Math.Max(o.a1, e), o.l0, line)); }
                        else next.Add((s, e, line, line));
                    }
                }
                foreach (var o in open) if (o.l1 - o.l0 + 1 >= (ShortLanes ? 1 : 2)) AddLane(o.a0, o.a1, o.l0, o.l1, vertical, !robot);   // DV44: a one-tile-long gap too
                open.Clear(); open.AddRange(next);
            }
        }

        void AddLane(int a0, int a1, int l0, int l1, bool vertical, bool vehicle)
        {
            var L = new ZLane { Vertical = vertical, VehicleLane = vehicle };
            if (vertical) { L.X0 = a0 * 16; L.X1 = (a1 + 1) * 16; L.Y0 = l0 * 16; L.Y1 = (l1 + 1) * 16; }
            else { L.Y0 = a0 * 16; L.Y1 = (a1 + 1) * 16; L.X0 = l0 * 16; L.X1 = (l1 + 1) * 16; }
            bool shortLane = ShortLanes && l1 == l0; int ap = shortLane ? ShortApproach : (int)LaneApproach;   // DV44: a one-tile gap: a short approach (ZCommit.Lanes.cs)
            L.Short = shortLane;                                                         // (pathing v3: DV44's own chokepoints are not zones, ZPathV3.cs)
            L.ZX0 = L.X0 - (vertical ? 0 : ap); L.ZX1 = L.X1 + (vertical ? 0 : ap);
            L.ZY0 = L.Y0 - (vertical ? ap : 0); L.ZY1 = L.Y1 + (vertical ? ap : 0);
            int ka = shortLane ? ap : ap + KeepClearExtra, ks = shortLane ? 0 : 16;          // DV44: its keep-clear area is its zone (group spots, parking stay close)
            L.KX0 = L.X0 - (vertical ? ks : ka); L.KX1 = L.X1 + (vertical ? ks : ka);
            L.KY0 = L.Y0 - (vertical ? ka : ks); L.KY1 = L.Y1 + (vertical ? ka : ks);
            Lanes.Add(L);
        }

        static bool BoxInZone(ZLane L, double lx, double ly, int w, int h) =>
            lx + 1 < L.ZX1 && L.ZX0 < lx + w - 1 && ly + 1 < L.ZY1 && L.ZY0 < ly + h - 1;
        static bool BoxInCorridor(ZLane L, double lx, double ly, int w, int h) =>
            lx + 1 < L.X1 && L.X0 < lx + w - 1 && ly + 1 < L.Y1 && L.Y0 < ly + h - 1;

        static bool SegmentHits(ZLane L, ZObject a, double x0, double y0, double x1, double y1)
        {
            double hw = a.WidthPix * 0.5 - 1, hh = a.HeightPix * 0.5 - 1;
            // Liang-Barsky against the corridor grown by the unit's half size
            double rx0 = L.X0 - hw, rx1 = L.X1 + hw, ry0 = L.Y0 - hh, ry1 = L.Y1 + hh, t0 = 0, t1 = 1, dx = x1 - x0, dy = y1 - y0;
            double[] p = { -dx, dx, -dy, dy }, q = { x0 - rx0, rx1 - x0, y0 - ry0, ry1 - y0 };
            for (int i = 0; i < 4; i++)
            {
                if (Math.Abs(p[i]) < 1e-12) { if (q[i] < 0) return false; continue; }
                double t = q[i] / p[i];
                if (p[i] < 0) { if (t > t1) return false; if (t > t0) t0 = t; }
                else { if (t < t0) return false; if (t < t1) t1 = t; }
            }
            return true;
        }

        /// <summary>Direction a unit in or near a lane travels along it, read from its path (the first path point beyond the zone
        /// along the axis; else the last one), so a sidestep or a push does not flip it: 0 = +axis, 1 = -axis, -1 = crossing
        /// or no order.</summary>
        static int TravelDir(ZLane L, ZObject o)
        {
            if (o.Waypoints.Count == 0) return -1;
            double c = L.Vertical ? o.LocY + o.YOver + o.HeightPix * 0.5 : o.LocX + o.XOver + o.WidthPix * 0.5;
            double lo = L.Vertical ? L.ZY0 : L.ZX0, hi = L.Vertical ? L.ZY1 : L.ZX1;
            double last = L.Vertical ? o.PathTargetY : o.PathTargetX;
            if (last < lo || last >= hi) return last > c ? 0 : 1;
            if (o.PathPoints != null)
                foreach (var p in o.PathPoints)
                {
                    last = L.Vertical ? p.y : p.x;
                    if (last < lo || last >= hi) return last > c ? 0 : 1;
                }
            if (Math.Abs(last - c) > 8) return last > c ? 0 : 1;                               // the goal lies in the zone
            return LaneDir(L, o.Dx, o.Dy);
        }

        /// <summary>Travel direction of a unit along a lane: 0 = +axis, 1 = -axis, -1 = standing or crossing.</summary>
        static int LaneDir(ZLane L, double vx, double vy)
        {
            double along = L.Vertical ? vy : vx, across = L.Vertical ? vx : vy;
            if (Math.Abs(along) < 1e-6 || Math.Abs(along) < 0.5 * Math.Abs(across)) return -1;
            return along > 0 ? 0 : 1;
        }

        /// <summary>BeginTick: who is travelling in which direction inside each lane zone.</summary>
        void CountLaneOccupancy()
        {
            foreach (var L in Lanes)
            {
                L.veh[0] = L.veh[1] = L.rob[0] = L.rob[1] = 0; L.occ[0] = L.occ[1] = null;
                L.apprVeh[0] = L.apprVeh[1] = L.apprRob[0] = L.apprRob[1] = 0; L.apprOcc[0] = L.apprOcc[1] = null;
                for (int d = 0; d < 2; d++) { if (!L.waited[d]) L.waitSince[d] = -1; L.waited[d] = false; L.waitWhoWas[d] = L.waitWho[d]; L.waitWho[d] = 0; }
                Array.Clear(L.robOf, 0, 6); Array.Clear(L.apprRobOf, 0, 6);
            }
            if (Lanes.Count == 0) return;
            foreach (var o in units)
            {
                if (Static(o)) continue;
                if (!Moving(o))
                {                                                                          // held in the approach with an order into the lane
                    if (o.Waypoints.Count > 0) foreach (var L in LanesAt(o)) CountApproach(L, o);   // (only a lane whose zone its box touches counts it)
                    continue;
                }
                foreach (var L in LanesAt(o))
                {
                    double lx = o.LocX + o.XOver, ly = o.LocY + o.YOver;
                    if (!BoxInZone(L, lx, ly, o.WidthPix, o.HeightPix)) continue;
                    CountApproach(L, o);
                    if (!BoxInCorridor(L, lx, ly, o.WidthPix, o.HeightPix) && !PathUsesCorridor(L, o, NearLook)) continue;   // beside the lane, not in it
                    int d = TravelDir(L, o); if (d < 0) continue;
                    if (o.Kind == ZKind.Vehicle) L.veh[d]++; else { L.rob[d]++; L.robOf[d * 3 + TeamSlot(o)]++; }
                    if (L.occ[d] == null) L.occ[d] = o;
                }
            }
        }

        /// <summary>A unit in the approach part of the zone (not yet in the corridor) whose path goes into the corridor.</summary>
        void CountApproach(ZLane L, ZObject o)
        {
            double lx = o.LocX + o.XOver, ly = o.LocY + o.YOver;
            if (!BoxInZone(L, lx, ly, o.WidthPix, o.HeightPix) || BoxInCorridor(L, lx, ly, o.WidthPix, o.HeightPix) || !PathUsesCorridor(L, o, NearLook)) return;
            int d = EntryDir(L, o);
            if (o.Kind == ZKind.Vehicle) L.apprVeh[d]++; else { L.apprRob[d]++; L.apprRobOf[d * 3 + TeamSlot(o)]++; }
            if (L.apprOcc[d] == null) L.apprOcc[d] = o;
        }

        /// <summary>Index of the unit's team in the per-team lane counts (0-2).</summary>
        static int TeamSlot(ZObject o) => Math.Max(0, Math.Min(2, (int)o.Owner));

        /// <summary>Travel direction a unit outside the corridor will have in it: from the low end = +axis (0), else -axis (1).</summary>
        static int EntryDir(ZLane L, ZObject o)
        {
            double c = L.Vertical ? o.LocY + o.YOver + o.HeightPix * 0.5 : o.LocX + o.XOver + o.WidthPix * 0.5;
            double mid = L.Vertical ? (L.Y0 + L.Y1) * 0.5 : (L.X0 + L.X1) * 0.5;
            return c < mid ? 0 : 1;
        }

        /// <summary>Would this step enter a lane zone against oncoming traffic (or against the fairness switch), or into an
        /// approach that is full?</summary>
        bool LaneConflict(ZObject a, double ox, double oy, double nx, double ny, bool register, out ZObject blocker)
        {
            blocker = null;
            if (Lanes.Count == 0) return false;
            double sx = nx - ox, sy = ny - oy;
            // vehicles first (ZYield.cs): robots of its own team are no lane traffic for a vehicle (it drives through them): they neither
            // close the lane against it, nor fill its approach, nor take the turn from it
            bool vf = VehicleFirst && a.Kind == ZKind.Vehicle && a.Owner != Team.Neutral; int own = TeamSlot(a), who = a.Kind == ZKind.Vehicle ? 8 : 1 << TeamSlot(a);
            foreach (var L in LanesAt(a, ox, oy, nx, ny))                                    // (every other lane ends at "came in sideways / no entry" below)
            {
                if (!L.VehicleLane && !a.IsRobot) continue;                                  // vehicles never fit a 1-tile gap
                if (L.Bridge && BridgeQueueEnabled) continue;                                // bridge decks: the reservation queue (ZBridges.cs)
                if (SoftLaneFree(a, L) || ShortLaneOff(a, L)) continue;                      // pathing v3: DV47 over the debris beside the lane; a zoned unit: no DV44 short lane (ZPathV3.cs)
                if (a.PS.laneWaitSince >= 0 && W.Time - a.PS.laneWaitSince > LaneWaitMax) continue;   // starved: goes anyway (the loop breaker sorts a head-on)
                bool zoneEntry = BoxInZone(L, nx, ny, a.WidthPix, a.HeightPix) && !BoxInZone(L, ox, oy, a.WidthPix, a.HeightPix);
                bool corridorEntry = BoxInCorridor(L, nx, ny, a.WidthPix, a.HeightPix) && !BoxInCorridor(L, ox, oy, a.WidthPix, a.HeightPix);
                double hw = a.WidthPix * 0.5, hh = a.HeightPix * 0.5;
                bool keepEntry = !zoneEntry && !corridorEntry && InKeep(L, nx + hw, ny + hh) && !InKeep(L, ox + hw, oy + hh) && !BoxInZone(L, nx, ny, a.WidthPix, a.HeightPix);
                if (!zoneEntry && !corridorEntry && !keepEntry) continue;                     // (came into the approach sideways: checked at the corridor)
                if (!PathUsesCorridor(L, a, keepEntry ? 160 : NearLook)) continue;             // only passing the approach / mouth
                int d = keepEntry ? EntryDir(L, a) : TravelDir(L, a);                         // where its path goes, not this step (a sidestep)
                if (d < 0 && zoneEntry && !corridorEntry) d = EntryDir(L, a);                 // stepping into the approach from the side
                if (keepEntry)
                {                                                                             // waiting for the turn happens out here: the lane mouth
                    int o2 = 1 - d;                                                           // stays open for the traffic coming out
                    bool busy = L.veh[o2] + L.rob[o2] - (vf ? L.robOf[o2 * 3 + own] : 0) > 0 || SquadLaneHold(a, L, d, true);          // DV44: a squad passes as one (ZCommit.Lanes.cs)
                    bool theirTurn = L.waitSince[o2] >= 0 && W.Time - L.waitSince[o2] > LaneFairness && (L.waitSince[d] < 0 || L.waitSince[o2] < L.waitSince[d]) && !SquadHasTurn(a, L, d)   // DV44: the rest of a squad in the lane goes on
                                     && (!vf || (L.waitWhoWas[o2] & ~(1 << own)) != 0);
                    if (!busy && !theirTurn) continue;
                    blocker = L.occ[o2];
                    if (register) { L.waited[d] = true; L.waitWho[d] |= who; if (L.waitSince[d] < 0) L.waitSince[d] = W.Time; L.Holds++; LaneHolds++; }
                    return true;
                }
                if (d < 0) continue;                                                          // crossing, not travelling along
                int opp = 1 - d;
                bool conflict = L.veh[opp] + L.rob[opp] - (vf ? L.robOf[opp * 3 + own] : 0) > 0 || SquadLaneHold(a, L, d, false);   // one direction at a time for everybody (DV44: a squad as one)
                if (conflict) blocker = L.occ[opp];
                else if (zoneEntry && !corridorEntry && (a.Kind == ZKind.Vehicle ? L.apprVeh[d] + L.apprRob[d] - (vf ? L.apprRobOf[d * 3 + own] : 0) > 0 : L.apprVeh[d] > 0))
                { conflict = true; blocker = L.apprOcc[d]; }                                  // the approach is full: wait outside it
                else if (zoneEntry && L.waitSince[opp] >= 0 && W.Time - L.waitSince[opp] > LaneFairness && (L.waitSince[d] < 0 || L.waitSince[opp] < L.waitSince[d]) && !SquadHasTurn(a, L, d)   // DV44: the rest of a squad goes on
                         && (!vf || (L.waitWhoWas[opp] & ~(1 << own)) != 0))
                    conflict = true;                                                          // the other end waited longer: our end stops admitting
                                                                                              // (units already in the approach go on in)
                if (!conflict) continue;
                if (register)
                {
                    L.waited[d] = true; L.waitWho[d] |= who; if (L.waitSince[d] < 0) L.waitSince[d] = W.Time;
                    L.Holds++; LaneHolds++;
                }
                return true;
            }
            return false;
        }

        /// <summary>Path length looked ahead from inside a lane zone (Zod's A* points near a corridor are one tile apart, so the
        /// current leg alone often ends just short of it).</summary>
        const double NearLook = 96;

        static bool InKeep(ZLane L, double x, double y) => x >= L.KX0 && x < L.KX1 && y >= L.KY0 && y < L.KY1;

        /// <summary>The rest of the unit's path, up to maxLen px of it, goes through the corridor.</summary>
        static bool PathUsesCorridor(ZLane L, ZObject a, double maxLen)
        {
            double x0 = a.LocX + a.XOver + a.WidthPix * 0.5, y0 = a.LocY + a.YOver + a.HeightPix * 0.5;
            double px = a.PathTargetX, py = a.PathTargetY, len = Math.Sqrt((px - x0) * (px - x0) + (py - y0) * (py - y0));
            if (SegmentHits(L, a, x0, y0, px, py)) return true;
            if (a.PathPoints != null)
                foreach (var p in a.PathPoints)
                {
                    if (len > maxLen) break;
                    if (SegmentHits(L, a, px, py, p.x, p.y)) return true;
                    len += Math.Sqrt((p.x - px) * (p.x - px) + (p.y - py) * (p.y - py)); px = p.x; py = p.y;
                }
            return false;
        }

        /// <summary>After an accepted step: count a unit that just entered a lane zone, so a unit arriving at the other end in
        /// the same tick already sees it.</summary>
        void LaneEnter(ZObject a, double ox, double oy, double nx, double ny)
        {
            if (Lanes.Count == 0) return;
            foreach (var L in LanesAt(a, ox, oy, nx, ny))
            {
                bool zoneEntry = BoxInZone(L, nx, ny, a.WidthPix, a.HeightPix) && !BoxInZone(L, ox, oy, a.WidthPix, a.HeightPix);
                bool corridorEntry = BoxInCorridor(L, nx, ny, a.WidthPix, a.HeightPix) && !BoxInCorridor(L, ox, oy, a.WidthPix, a.HeightPix);
                if (!zoneEntry && !corridorEntry) continue;
                if (!PathUsesCorridor(L, a, NearLook)) continue;
                if (zoneEntry && !corridorEntry)
                {                                                                             // into the approach: it is taken now
                    int e = EntryDir(L, a);
                    if (a.Kind == ZKind.Vehicle) L.apprVeh[e]++; else { L.apprRob[e]++; L.apprRobOf[e * 3 + TeamSlot(a)]++; }
                    if (L.apprOcc[e] == null) L.apprOcc[e] = a;
                }
                int d = TravelDir(L, a); if (d < 0) continue;
                SquadLaneEnter(a, L, d);                                                      // DV44 (ZCommit.Lanes.cs)
                if (zoneEntry && corridorEntry) { } else if (corridorEntry && L.veh[d] + L.rob[d] > 0 && L.occ[d] != null) continue;   // already counted in the approach
                if (a.Kind == ZKind.Vehicle) L.veh[d]++; else { L.rob[d]++; L.robOf[d * 3 + TeamSlot(a)]++; }
                if (L.occ[d] == null) L.occ[d] = a;
            }
        }

        /// <summary>The unit is inside a lane zone and travelling along it (it has right of way in a deadlock).</summary>
        bool OnLaneMoving(ZObject o)
        {
            if (!Moving(o)) return false;
            foreach (var L in LanesAt(o))
                if (BoxInCorridor(L, o.LocX + o.XOver, o.LocY + o.YOver, o.WidthPix, o.HeightPix) && TravelDir(L, o) >= 0) return true;
            return false;
        }

        /// <summary>Keep-clear area (lane zone + 3 tiles along the axis + 1 tile each side) containing the point, if any: group
        /// spots, crowd arrivals, step-aside and regroup spots stay out of it, so the first units across a bridge do not park
        /// in the way of the rest.</summary>
        ZLane KeepClearAt(int x, int y)
        {
            foreach (var L in LanesNear(x, y)) if (x >= L.KX0 && x < L.KX1 && y >= L.KY0 && y < L.KY1) return L;
            return null;
        }
        const int KeepClearExtra = 48;

        /// <summary>Lane zone containing the unit's centre, if any (step-aside targets leave the lane).</summary>
        ZLane LaneAt(int cx, int cy)
        {
            foreach (var L in LanesNear(cx, cy)) if (cx >= L.ZX0 && cx < L.ZX1 && cy >= L.ZY0 && cy < L.ZY1) return L;
            return null;
        }
    }
}
