using System;
using System.Collections.Generic;

namespace OpenZ.Sim
{
    /// <summary>
    /// switch on ZWorld (ZGame sets them at every start from PlayerPrefs; all off in 'Zod exact' oz_zod_exact = 1 and in the parity
    /// report's Zod rows). Pure sim: integer / double maths on the pixel grid, sim time, fixed object order; none of them draws from
    /// Zod's rand() stream when it is off, so with all four off the sim is the previous build bit for bit.
    /// <list type="bullet">
    /// <item><b>DV34 fort rush</b> (NB15): Zod walks an ENTER_FORT robot to the gate exit point (GetBuildingCreationMovePoint) and then
    /// through the gate to the spawn point inside at walking speed (zobject.cpp:2774-2913); it never runs (running is only tried on
    /// flag, enter and pickup orders, zobject.cpp:2729-2747 / 2584-2667), a bot's order still dodges every shell aimed at it
    /// (DodgeMissile zobject.cpp:3657-3714) and still diverts into attack-move fights (attack_to, zobject.cpp:3285-3339), and with the
    /// OpenZ spacing layer a squad converging on the one exit pixel queues and sidesteps in the doorway under the tower guns. With
    /// DV34 a robot on an ENTER_FORT order within FortRushRadius of the gate exit point (and inside, in the EnterBuilding stage)
    /// sprints at Zod's running speed (run_unit_speed, stamina untouched), passes through other units like Zod's own units do
    /// (no spacing, no gate, no pushes, research 7.4), never dodges and never turns off to fight.</item>
    /// <item><b>DV35 nearest robot boards</b> (NB16): Zod lets only the squad LEADER enter (zobject.cpp:2584-2667: `if(!IsMinion())
    /// sflags.entered_target_ref_id`); a minion that reaches the vehicle first just stops on it while the leader walks round the
    /// others. With DV35 the first member of a human player's squad whose centre is on the empty vehicle / cannon enters it (the same
    /// RobotEnterObject, zserver.cpp:1375-1421); the rest of the squad stays a squad. APCs keep Zod's rule (the whole squad boards);
    /// CPU squads keep Zod's leader-only rule (bot-vs-bot balance, see MinionBoards).</item>
    /// <item><b>DV36 parked-aware paths</b> (NB18): Zod's A* sees terrain only (zpath_finding_astar.cpp), so paths run straight
    /// through parked empty vehicles and cannons; the spacing layer then stops the vehicle there, sidesteps and detours round them
    /// every few seconds (L17 car park, L02's gatlings in a pass: one probe gave up behind one). With DV36 a vehicle whose Zod path
    /// (or straight line) passes within the spacing of a unit that never moves (empty vehicle, cannon, unit without speed) gets a
    /// time-weighted A* on its own graph with those footprints closed; the stuck guard's detour falls back to the same search over
    /// the whole map before any give-up; a bridge that comes back (crane / auto repair) makes every vehicle on the way somewhere
    /// re-plan (the bridge may be the faster way now), a destroyed one re-plans the vehicles whose path crosses it at once; and the
    /// bridge queue's box-junction rule (DV8 ExitClear) no longer waits for parked units to clear the far approach (they never do:
    /// the lone vehicle was held 6 s "reserved for waiters").</item>
    /// <item><b>DV37 car park drive</b> (NB20): the Zod bot sends a random 15-35 % (+1) of its idle units per order round to their
    /// mutually nearest targets (zbot.cpp:176-414), so on L17 the empty vehicles of the car park wait minutes. With DV37, on L17 on
    /// the standard (Normal) CPU only, every order round first pairs up to CarParkPerRound idle robot squads with empty vehicles
    /// (Zod's own region check and mutual-nearest pairing, ZBot.MatchTargets_3 / GiveOutOrders_3) while fewer than CarParkMaxEnRoute
    /// squads are already on their way to one; then Zod's round runs on the other idle units. The bot's knowledge is unchanged (its
    /// client object list and regions, no fog in Zod).</item>
    /// </list>
    /// </summary>
    public partial class ZObject
    {
        // ================================================================== DV34 fort rush
        /// <summary>DV34: px from the fort's gate exit point (GetBuildingCreationMovePoint) inside which an ENTER_FORT robot rushes.</summary>
        public const int FortRushRadius = 112;
        bool fortRush;
        /// <summary>DV34: this robot sprints through the enemy fort's door this tick (no spacing, no dodging, running speed).</summary>
        public bool FortRushOn => fortRush;
        /// <summary>DV34 statistics (checks): ticks spent rushing.</summary>
        public int FortRushTicks { get; private set; }

        /// <summary>DV34, ProcessEnterFortWP after the stage checks: rushing near the gate exit or already inside.</summary>
        void FortRushStep(ZObject t)
        {
            fortRush = ZWorld.FortRushDeviation && IsRobot && t != null && t.CanEnterFort(Owner) &&
                       (cur.stage == Stage.EnterBuilding || Within(CenterX, CenterY, cur.exitX, cur.exitY, FortRushRadius));
            if (fortRush) FortRushTicks++;
            FortRunStep(t);                                                               // DV102: the wide ring of a human team's robot: run, own team passes through, no dodge, a clear straight line (ZObject.FortRun.cs)
        }

        // ================================================================== DV35 nearest robot boards
        /// <summary>DV35: a minion standing on this empty vehicle / cannon may enter it itself (not an APC: its whole squad boards). Human
        /// teams only (like DV16's order priority): with it the Zod bot got its boarding squads back idle at once (the leader stays and
        /// relays its KillWP; in Zod the successor keeps a stale ENTER in the bot's client list and looks busy), which moved L02 bot vs bot
        /// from 54 % to 61 % red over 360 seeds; CPU squads keep Zod's leader-only rule.</summary>
        bool MinionBoards(ZObject t) => ZWorld.BoardNearestDeviation && IsMinion && t != null && t.CanBeEntered() && !t.IsApc && World.IsHumanTeam(Owner);
    }

    public sealed partial class ZWorld
    {
        /// <summary>DV34 fort rush (NB15). PlayerPrefs oz_fort_rush.</summary>
        public static bool FortRushDeviation = true;
        /// <summary>DV35 nearest robot boards (NB16). PlayerPrefs oz_board_nearest.</summary>
        public static bool BoardNearestDeviation = true;
        /// <summary>DV36 parked-aware vehicle paths, bridge re-plans, DV8 exit rule (NB18). PlayerPrefs oz_parked_paths.</summary>
        public static bool ParkedPathDeviation = true;
        /// <summary>DV37 car park drive of the standard CPU on L17 (NB20). PlayerPrefs oz_carpark_bot.</summary>
        public static bool CarParkBotDeviation = true;

        /// <summary>DV37: this is L17 "Car Park" (the decoded Zod map p02_bb_orig17.map).</summary>
        public bool CarParkMap => Map != null && Map.file != null && Map.file.EndsWith("orig17.map", StringComparison.OrdinalIgnoreCase);
    }

    public sealed partial class ZPathing
    {
        // ================================================================== DV36 parked-aware paths
        /// <summary>DV36 statistics (checks): paths planned round parked units, detours that used the whole-map search, re-plans
        /// after a bridge came back / went down.</summary>
        public int ParkedPlans { get; private set; }
        public int ParkedDetours { get; private set; }
        public int BridgeReplans { get; private set; }

        readonly List<ZObject> parked = new List<ZObject>();

        /// <summary>Units that never move out of the way (cannons, empty vehicles, units without speed), plus with standingToo the
        /// units standing without an order (the stuck guard's own "standing" set), minus the order's own target.</summary>
        void CollectParked(ZObject o, int skipId, bool standingToo)
        {
            parked.Clear();
            foreach (var u in units)
            {
                if (u == o || !Separable(u) || (u.Id == skipId && !u.IsShuttle)) continue;   // DV118: a squad ordered into the shuttle plans round its hull to the back
                if ((Static(u) && !u.Stunned) || (standingToo && (u.Waypoints.Count == 0 || BridgeParked(u))) ||
                    (RobotParkedPaths(o) && !u.Stunned && IdleHull(u))) parked.Add(u);   // electrobot_v1 DV45: a stunned unit moves on; DV66: robots also go round idle vehicles
            }
        }

        /// <summary>The line (x0, y0) -> (x1, y1) brings unit o within the spacing of a collected parked unit (4 px steps; a unit it
        /// already stands too close to only counts when the line gets closer to it).</summary>
        bool LineNearParked(ZObject o, double x0, double y0, double x1, double y1)
        {
            double dx = x1 - x0, dy = y1 - y0, len = Math.Sqrt(dx * dx + dy * dy);
            int steps = (int)(len / 4) + 1;
            foreach (var u in parked)
            {
                double min = MinSpacing(o, u) + (RobotParkedPaths(o) ? 1 + RobotParkedClearance : 0), ux = CX(u), uy = CY(u);   // DV66: a robot path keeps the gate's spacing + clearance
                double d0 = (x0 - ux) * (x0 - ux) + (y0 - uy) * (y0 - uy);
                // quick reject: the segment's bounding box grown by the spacing
                if (Math.Max(x0, x1) + min < ux || Math.Min(x0, x1) - min > ux || Math.Max(y0, y1) + min < uy || Math.Min(y0, y1) - min > uy) continue;
                for (int i = 1; i <= steps; i++)
                {
                    double f = Math.Min(1.0, i * 4 / Math.Max(len, 1e-9)), px = x0 + dx * f, py = y0 + dy * f;
                    double d2 = (px - ux) * (px - ux) + (py - uy) * (py - uy);
                    if (d2 < min * min && d2 < d0 - 1e-6) return true;
                }
            }
            return false;
        }

        /// <summary>The unit's path (from where it stands) runs within the spacing of a parked unit.</summary>
        bool PathNearParked(ZObject o, List<(int x, int y)> pts)
        {
            double px = CX(o), py = CY(o);
            foreach (var p in pts) { if (LineNearParked(o, px, py, p.x, p.y)) return true; px = p.x; py = p.y; }
            return false;
        }

        static bool ParkedOrder(ZObject o) => ZWorld.ParkedPathDeviation && o.Waypoints.Count > 0 &&
            (o.Kind == ZKind.Vehicle ? !BridgeHelperRef(o.Waypoints[0].refId) : RobotParkedPaths(o) && (o.Waypoints[0].refId == RefRegroup || !BridgeHelperRef(o.Waypoints[0].refId)));

        /// <summary>
        /// Robot paths (orders and a minion's regroup walk) are planned
        /// round parked units too (cannons, empty vehicles: <see cref="CollectParked"/>), keeping the gate's spacing plus this many px,
        /// so a robot never walks into a parked hull's spacing and stands there held (Zod's A* knows only terrain).</summary>
        public const int RobotParkedClearance = 4;
        static bool RobotParkedPaths(ZObject o) => ZWorld.ExitLanesDeviation && o.Kind == ZKind.Robot;

        /// <summary>DV36, PrepareStartPath: Zod's straight-line shortcut (DirectPathPossible) would run through a parked unit: run the
        /// A* instead, so CheckPath sees the path.</summary>
        void ParkedForceSearch(ZObject o)
        {
            if (!ParkedOrder(o)) return;
            CollectParked(o, o.Waypoints[0].refId, false);
            if (parked.Count > 0 && LineNearParked(o, CX(o), CY(o), o.PathTargetX, o.PathTargetY)) W.Path.ForceSearch = true;
        }

        /// <summary>DV36, CheckPath: a vehicle path that runs through a parked unit is re-planned round the parked units (time-weighted
        /// A* on the unit's own graph); no such way = Zod's path as before (the stuck guard handles it).</summary>
        List<(int x, int y)> ParkedPath(ZObject o, List<(int x, int y)> pts)
        {
            if (pts == null || pts.Count == 0 || !ParkedOrder(o)) return pts;
            CollectParked(o, o.Waypoints[0].refId, false);
            if (parked.Count == 0 || !PathNearParked(o, pts)) return pts;
            var (gx, gy) = pts[pts.Count - 1];
            var alt = ParkedSearch(o, gx, gy, out bool found);
            if (alt == null || !found || BridgeRejectsPath(o, alt)) return pts;
            ParkedPlans++;
            Log(o, $"DV36: path through a parked unit: planned round it, {alt.Count} points");
            return alt;
        }

        /// <summary>DV36, Detour: the local search found no way (or only a partial one): the whole map, parked and standing units closed.</summary>
        List<(int x, int y)> ParkedDetour(ZObject o, int gx, int gy, List<(int x, int y)> pts, ref bool found)
        {
            if (!ZWorld.ParkedPathDeviation || (pts != null && found) || o.Waypoints.Count == 0) return pts;
            CollectParked(o, o.Waypoints[0].refId, true);
            var alt = ParkedSearch(o, gx, gy, out bool f2);
            if (alt == null || !f2 || BridgeRejectsPath(o, alt)) return pts;
            found = true; ParkedDetours++;
            Log(o, $"DV36: no local detour: the way round the parked units over the map, {alt.Count} points");
            return alt;
        }

        /// <summary>DV36: a bridge came back: every vehicle on an order (approach stages) plans again from where it is; Zod's A* now
        /// sees the deck and takes it when it is the faster way.</summary>
        internal void BridgeBack(ZObject bridge)
        {
            if (!Enabled || !ZWorld.ParkedPathDeviation) return;
            foreach (var o in W.Objects)
            {
                if (!ReplanCandidate(o)) continue;
                o.PS.reissue = true; BridgeReplans++;
                Log(o, $"DV36: bridge ({bridge.LocX >> 4},{bridge.LocY >> 4}) is back: re-plan");
            }
        }

        /// <summary>DV36: a bridge went down: the vehicles whose path crosses it plan again at once (not at the broken deck).</summary>
        internal void BridgeGone(ZObject bridge)
        {
            if (!Enabled || !ZWorld.ParkedPathDeviation) return;
            foreach (var o in W.Objects)
            {
                if (!ReplanCandidate(o) || !PathCrossesRect(o, bridge.LocX, bridge.LocY, bridge.WidthPix, bridge.HeightPix)) continue;
                o.PS.reissue = true; BridgeReplans++;
                Log(o, $"DV36: bridge ({bridge.LocX >> 4},{bridge.LocY >> 4}) is down: re-plan");
            }
        }

        static bool ReplanCandidate(ZObject o)
        {
            if (o.Kind != ZKind.Vehicle || o.Owner == Team.Neutral || o.Destroyed || o.KillAt >= 0 || o.Waypoints.Count == 0) return false;
            if (o.PathInBuildingStage || o.PathPending || !o.CanOverwriteWP() || BridgeHelperRef(o.Waypoints[0].refId)) return false;
            var m = o.Waypoints[0].mode;
            return m == WpMode.Move || m == WpMode.Attack || m == WpMode.UnitRepair || m == WpMode.CraneRepair || m == WpMode.EnterFort;
        }

        static bool PathCrossesRect(ZObject o, int rx, int ry, int rw, int rh)
        {
            var L = new ZLane { X0 = rx, Y0 = ry, X1 = rx + rw, Y1 = ry + rh };
            double px = CX(o), py = CY(o);
            if (SegmentHits(L, o, px, py, o.PathTargetX, o.PathTargetY)) return true;
            px = o.PathTargetX; py = o.PathTargetY;
            if (o.PathPoints != null) foreach (var p in o.PathPoints) { if (SegmentHits(L, o, px, py, p.x, p.y)) return true; px = p.x; py = p.y; }
            return false;
        }

        // time-weighted A* with the collected parked units closed (arrays of its own: never clobbers SearchW / Search)
        int[] pg, pCame, pHeapNode, pHeapKey; bool[] pDone, pClosed; int pHeapN; readonly List<int> pTouched = new List<int>(), pClosedList = new List<int>();

        /// <summary>DV36: A* over the unit's own graph (footprint tiles, ZPathFinder.StepOk: Zod's move and corner rules, the unit's
        /// grid with / without destroyable rocks) weighted by travel time (100 per ground tile, road / 1.689, water / 0.7, like
        /// SearchW), with every footprint closed whose centre lies within the spacing (+1 px) of a collected parked unit (the start
        /// and the goal tile stay open), never onto a deck of a crossing this order avoids. Deterministic (heap with node tie-break).
        /// Returns Zod-style points (footprint centres, straight runs merged, the exact goal last) or null.</summary>
        List<(int x, int y)> ParkedSearch(ZObject o, int gxp, int gyp, out bool found)
        {
            found = false; layerSearches++;                                                // DV44 path budget (ZCommit.cs)
            var pf = W.Path; int w = pf.W, h = pf.H, n = w * h;
            bool robot = o.IsRobot, nr = NoRocks(o);
            if (pg == null || pg.Length != n)
            {
                pg = new int[n]; pCame = new int[n]; pDone = new bool[n]; pClosed = new bool[n];
                pHeapNode = new int[n * 8 + 16]; pHeapKey = new int[pHeapNode.Length];
                for (int i = 0; i < n; i++) pg[i] = int.MaxValue;
            }
            var (sx, sy) = ZReach.FootTile(o.LocX, o.LocY);
            if (!pf.StepOk(sx, sy, sx, sy, robot, nr)) return null;
            var (gx, gy) = ZReach.FootTileAt(gxp, gyp, robot);
            if (gx < 0 || gy < 0 || gx >= w || gy >= h) return null;
            int start = sy * w + sx, goal = gy * w + gx;
            pClosedList.Clear();
            double ocx = CX(o), ocy = CY(o);
            foreach (var u in parked)
            {                                                                                     // footprints whose centre comes too close to u
                double min = MinSpacing(o, u) + 1 + (RobotParkedPaths(o) ? RobotParkedClearance : 0), ux = CX(u), uy = CY(u);   // DV66: robots keep a clearance
                double lim2 = Math.Min(min * min, (ocx - ux) * (ocx - ux) + (ocy - uy) * (ocy - uy));   // already that close: only closer is closed (the gate's rule)
                int r = (int)Math.Ceiling(min / 16.0) + 1;
                var (utx, uty) = ZReach.FootTileAt((int)ux, (int)uy, robot);
                for (int ty = uty - r; ty <= uty + r; ty++)
                    for (int tx = utx - r; tx <= utx + r; tx++)
                    {
                        if (tx < 0 || ty < 0 || tx >= w || ty >= h) continue;
                        var (cx, cy) = ZReach.FootCentre(tx, ty, robot);
                        if ((cx - ux) * (cx - ux) + (cy - uy) * (cy - uy) >= lim2) continue;
                        int i = ty * w + tx;
                        if (i == start || i == goal || pClosed[i]) continue;
                        pClosed[i] = true; pClosedList.Add(i);
                    }
            }
            pHeapN = 0; pTouched.Clear();
            pg[start] = 0; pCame[start] = -1; pTouched.Add(start); PPush(start, WHeur(sx, sy, gx, gy));
            int expansions = 0, limit = n * 2;
            while (pHeapN > 0 && expansions < limit)
            {
                int cur = PPop();
                if (pDone[cur]) continue;
                pDone[cur] = true; expansions++;
                if (cur == goal) { found = true; break; }
                int cx = cur % w, cy = cur / w, cg = pg[cur];
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int nx = cx + dx, ny = cy + dy;
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                        int ni = ny * w + nx;
                        if (pClosed[ni] || !pf.StepOk(cx, cy, nx, ny, robot, nr) || BridgeAvoidsTile(o, nx, ny)) continue;
                        int ng = cg + StepCost(nx, ny, robot, dx != 0 && dy != 0);
                        if (ng >= pg[ni]) continue;
                        if (pg[ni] == int.MaxValue) pTouched.Add(ni);
                        pg[ni] = ng; pCame[ni] = cur;
                        PPush(ni, ng + WHeur(nx, ny, gx, gy));
                    }
            }
            List<(int x, int y)> pts = null;
            if (found)
            {
                var tiles = new List<int>();
                for (int c = goal; c != -1 && c != start; c = pCame[c]) tiles.Add(c);
                tiles.Reverse();
                pts = new List<(int x, int y)>();
                for (int k = 0; k < tiles.Count; k++)
                {
                    int t = tiles[k];
                    if (k > 0 && k + 1 < tiles.Count)
                    {
                        int a = tiles[k - 1], b = tiles[k + 1];
                        if (t % w - a % w == b % w - t % w && t / w - a / w == b / w - t / w) continue;   // middle of a straight run
                    }
                    pts.Add(ZReach.FootCentre(t % w, t / w, robot));
                }
                if (pts.Count > 0) pts.RemoveAt(pts.Count - 1);                                 // the goal tile's centre -> the exact goal
                pts.Add((gxp, gyp));
            }
            foreach (var i in pTouched) { pg[i] = int.MaxValue; pDone[i] = false; }
            foreach (var i in pClosedList) pClosed[i] = false;
            return pts;
        }

        void PPush(int node, int key)
        {
            if (pHeapN >= pHeapNode.Length) return;
            int i = pHeapN++;
            pHeapNode[i] = node; pHeapKey[i] = key;
            while (i > 0)
            {
                int p = (i - 1) >> 1;
                if (pHeapKey[p] < pHeapKey[i] || (pHeapKey[p] == pHeapKey[i] && pHeapNode[p] <= pHeapNode[i])) break;
                (pHeapNode[p], pHeapNode[i]) = (pHeapNode[i], pHeapNode[p]); (pHeapKey[p], pHeapKey[i]) = (pHeapKey[i], pHeapKey[p]);
                i = p;
            }
        }

        int PPop()
        {
            int top = pHeapNode[0]; layerWork += PopWork;                                 // DV44 path budget (ZCommit.cs): counter only
            pHeapN--;
            pHeapNode[0] = pHeapNode[pHeapN]; pHeapKey[0] = pHeapKey[pHeapN];
            int i = 0;
            while (true)
            {
                int l = i * 2 + 1, r = l + 1, m = i;
                if (l < pHeapN && (pHeapKey[l] < pHeapKey[m] || (pHeapKey[l] == pHeapKey[m] && pHeapNode[l] < pHeapNode[m]))) m = l;
                if (r < pHeapN && (pHeapKey[r] < pHeapKey[m] || (pHeapKey[r] == pHeapKey[m] && pHeapNode[r] < pHeapNode[m]))) m = r;
                if (m == i) break;
                (pHeapNode[m], pHeapNode[i]) = (pHeapNode[i], pHeapNode[m]); (pHeapKey[m], pHeapKey[i]) = (pHeapKey[i], pHeapKey[m]);
                i = m;
            }
            return top;
        }
    }

    public sealed partial class ZBot
    {
        // ================================================================== DV37 car park drive
        /// <summary>DV37: idle robot squads paired with empty vehicles per order round, and the most squads on their way to one.</summary>
        public const int CarParkPerRound = 3, CarParkMaxEnRoute = 6;
        /// <summary>DV37 statistics (checks): ENTER orders this bot gave in its car-park pass.</summary>
        public int CarParkOrders { get; private set; }

        /// <summary>DV37 applies: the switch, L17, the standard CPU (Normal = the Zod bot; Easy keeps its own handicap).</summary>
        public bool CarParkDrive => ZWorld.CarParkBotDeviation && Difficulty == ZBotDifficulty.Normal && W.CarParkMap;

        /// <summary>DV37, Stage1AI_3 after the units and targets are collected (before Zod's random wave): up to CarParkPerRound idle
        /// robot squads (nearest to an empty vehicle first; ties by id) get Zod's pairing against the empty vehicles no unit of ours
        /// is already after; the ordered squads leave the round and their vehicles count as targeted.</summary>
        void CarParkRound(List<ZObject> units, List<ZObject> targeted, List<ZObject> targets)
        {
            if (!CarParkDrive || units.Count == 0) return;
            int enRoute = 0;
            foreach (var o in W.Objects)
            {
                if (o.Kind != ZKind.Robot || o.Owner != Team || o.IsMinion || Gone(o)) continue;
                var wps = KnownWaypoints(o);
                if (wps.Count == 0 || wps[0].mode != WpMode.Enter) continue;
                var t = BotObject(wps[0].refId);
                if (t != null && t.Kind == ZKind.Vehicle && t.Owner == Team.Neutral) enRoute++;
            }
            int budget = Math.Min(CarParkPerRound, CarParkMaxEnRoute - enRoute);
            if (budget <= 0) return;
            var cars = new List<ZObject>();
            foreach (var t in targets) if (t.Kind == ZKind.Vehicle && t.CanBeEntered() && !targeted.Contains(t)) cars.Add(t);
            if (cars.Count == 0) return;
            var cand = new List<(ZObject u, double d)>();
            foreach (var u in units)
            {
                if (u.Kind != ZKind.Robot || u.NoAutoEnter) continue;                          // electrobot_v1 D7
                double best = double.MaxValue;
                foreach (var t in cars)
                {
                    TargetPoint(t, out int tx, out int ty);
                    if (!InSameRegion(u.LocX + 8, u.LocY + 8, tx, ty, true)) continue;
                    best = Math.Min(best, Dist(u, t));
                }
                if (best < double.MaxValue) cand.Add((u, best));
            }
            if (cand.Count == 0) return;
            cand.Sort((a, b) => a.d != b.d ? a.d.CompareTo(b.d) : a.u.Id.CompareTo(b.u.Id));
            var pick = new List<ZObject>();
            for (int i = 0; i < cand.Count && i < budget; i++) pick.Add(cand[i].u);
            int before = LastRoundOrders.Count;
            MatchTargets_3(pick, cars);
            GiveOutOrders_3(pick, cars);
            for (int i = before; i < LastRoundOrders.Count; i++)
            {
                var ord = LastRoundOrders[i];
                units.RemoveAll(x => x.Id == ord.unitId);
                var t = W.Get(ord.targetId); if (t != null && !targeted.Contains(t)) targeted.Add(t);
                CarParkOrders++;
            }
        }
    }
}
