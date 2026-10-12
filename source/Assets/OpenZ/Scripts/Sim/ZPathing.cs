using System;
using System.Collections.Generic;

namespace OpenZ.Sim
{
    /// <summary>
    /// Zod has no unit-unit collision (research 7.4: units overlap freely, squads stack on one pixel),
    /// kills an order at the first wall (zobject.cpp:3470-3484), lets minions clip walls and freeze on impassable tiles with
    /// speed 0 (research 11.9), and sends a unit straight at a target its A* cannot reach (zpath_finding.cpp:810-900,
    /// zpath_finding_astar.cpp). This layer keeps every Zod speed, path and rule and adds, in the sim, on the pixel grid,
    /// deterministic (object order, no RNG):
    ///  - ZSeparation.cs: the movement gate (never step closer than the minimum spacing to another unit; a leader is never
    ///    blocked by its own minions), walking units pushing standing team-mates aside (a chain up to three deep, never
    ///    into walls, water or a lane), sidesteps, queueing behind units going the same way, idle units stepping aside
    ///    (a Zod scuffle-style MOVE), crowd arrival at a shared goal, unstacking and a capped overlap push;
    ///  - ZLanes.cs: bridges and one-lane chokepoints admit one direction at a time, alternating fairly; a vehicle enters
    ///    an approach alone; units wait for their turn outside the lane's keep-clear area, where nobody parks;
    ///  - ZStuckGuard.cs: stuck = no path progress for 0.8 s -> Zod re-path from here -> nearest reachable goal -> clean
    ///    give-up (order cleared, unit idles), each step once per waypoint so nothing oscillates; detours round parked
    ///    units; head-on deadlocks back off the lower-priority unit; split squads regroup; a 12 s watchdog under all of it;
    ///  - ZReach.cs: exact A* reachability, so any order to an unreachable spot (water for vehicles, cliffs, buildings, a
    ///    destroyed bridge's far side) resolves to the nearest reachable point (outside lane mouths when one is as near);
    ///  - ZSlots.cs: a group sent to one point gets one spot per unit around it; ZDetour.cs: clean A* legs and detours.
    /// Speeds are never raised: the gate only shortens or turns Zod's step, pushes are capped and never push a moving unit
    /// forward. Pathing.Enabled = false restores pure Zod movement for A/B checks.
    /// </summary>
    public sealed partial class ZPathing
    {
        /// <summary>Default for new worlds (A/B tests, a debug toggle). Each world can still switch its own layer.</summary>
        public static bool DefaultEnabled = true;
        public bool Enabled = DefaultEnabled;

        // ---- tunables (Zod px / s; 16 px = 1 tile = 2 m)
        public const int RobotRadius = 6, ToughRadius = 7, VehicleRadius = 16, CannonRadius = 16;
        public const int SquadSpacing = 18;
        public const double PushSpeed = 40;             // overlap correction speed
        public const double StuckWindow = 0.8;          // no path progress for this long = one escalation step
        public const double ProgressEps = 2;            // px of path that count as progress
        public const int MaxBlockTries = 4;             // terrain blocks handled (re-centre / re-path) before Zod's KillWP
        public const double ArriveRadius = 64;          // crowd arrival: max path distance from the own goal (group moves have own spots)
        public const double TrafficPatience = 120;      // waiting behind moving traffic (queue, lane, jostle) before the guard escalates (a 25-unit column behind a 6 px/s Mobile Missile takes ~90 s through a 1-lane bridge)
        public const double DeadQueuePatience = 2;      // waiting behind a unit that does not move (shove / deadlock back-off get this long)
        public const double DeadJamPatience = 30;       // a jam with nothing moving near it: detour attempts, then the guard escalates
        public const double WatchdogTime = 12;          // hard net: an order with no movement at all for this long is dropped
        public const double LaneApproach = 40, LaneWaitMax = 45, LaneFairness = 6;
        public const double DeadlockDelay = 0.6, UnreachableMemory = 6, ShoveInterval = 0.6, RegroupDistance = 40;
        const int Cell = 32, MaxQuery = 48;
        /// <summary>refId of the MOVE waypoints this layer adds itself (never player-given; the bot sees the unit as idle).</summary>
        public const int RefStepAside = -2, RefBackOff = -3, RefRegroup = -4;

        readonly ZWorld W;
        public readonly ZReach Reach;
        readonly List<ZObject> units = new List<ZObject>();
        List<ZObject>[] cells; int gw, gh;
        readonly List<ZObject> near = new List<ZObject>(), near2 = new List<ZObject>();
        double lastDt = ZConst.TICK;

        public ZPathing(ZWorld w) { W = w; Reach = new ZReach(w); }

        /// <summary>First exception thrown inside the layer (it then switches itself off: the game goes on with pure Zod
        /// movement instead of a broken tick). Null while healthy; the stress test and the view can show it.</summary>
        public string FailSafeError { get; private set; }
        void Fail(Exception e) { if (FailSafeError == null) FailSafeError = e.ToString(); Enabled = false; }

        /// <summary>Record every pathing decision per unit in ZPathState.Log (last 40 lines). Debug only.</summary>
        public static bool Trace;
        void Log(ZObject o, string msg)
        {
            if (!Trace) return;
            var l = o.PS.Log ??= new List<string>();
            l.Add($"{W.Time:F2}s ({o.CenterX},{o.CenterY}) {(o.Waypoints.Count > 0 ? o.Waypoints[0].mode + $"->({o.Waypoints[0].x},{o.Waypoints[0].y})" : "idle")}: {msg}");
            if (l.Count > 40) l.RemoveAt(0);
        }

        /// <summary>Units the layer handles this tick (robots, vehicles, cannons alive), in object order.</summary>
        public IReadOnlyList<ZObject> Units => units;

        // ------------------------------------------------------------------ classification
        public static bool Separable(ZObject o) => o != null && o.IsUnit && !o.Destroyed && o.KillAt < 0 && !o.IsFortDefender;   // fort_defender_v1 DV41: on the roof, never in the ground traffic
        /// <summary>Never moves: cannons, empty (neutral) vehicles, units without speed.</summary>
        public static bool Static(ZObject o) => o.Kind == ZKind.Cannon || o.MoveSpeed <= 0 || o.Owner == Team.Neutral || o.Stunned;   // electrobot_v1 DV45: never pushed, never "stuck", others go round
        public static int Radius(ZObject o) => o.Kind == ZKind.Robot ? (o.Type == "tough" || o.Type == ZElectro.Type ? ToughRadius : RobotRadius) : o.Kind == ZKind.Vehicle ? VehicleRadiusOf(o.Type) : CannonRadius;   // electrobot_v1: Tough radius

        /// <summary>
        /// (crane and APC trucks ~7 m, heavy tank ~6 m), so bigger chassis keep more room. OpenZ spacing only (Zod has no unit
        /// collision), speeds and paths unchanged.</summary>
        public static bool ChassisRadii = true;   // false = one 16 px radius for every vehicle (A/B for the stress test)
        public static int VehicleRadiusOf(string type) => !ChassisRadii ? VehicleRadius : type switch
        {
            "crane" => 22, "apc" => 21, "heavy" => 20, "missile_launcher" => 19, "medium" => 18, "light" => 17, _ => VehicleRadius,
        };
        /// <summary>Minimum centre distance of two units (robots 12 px = 1.5 m, robot-vehicle 22 px, vehicles 32 px).</summary>
        public static int MinSpacing(ZObject a, ZObject b) => Radius(a) + Radius(b);
        /// <summary>The unit really walks: a velocity AND a waypoint. Zod's SetVelocity keeps a velocity component under 0.1 px/s
        /// when it stops a unit (the `fabs(dx - old_dx) &lt; 0.1` hysteresis, zobject.cpp:3646-3647), so an idle unit can keep
        /// such a "ghost walker" blocked a squad mate's regroup for 120 s and counted as lane traffic.</summary>
        public static bool Moving(ZObject o) => o.IsMoving && o.Waypoints.Count > 0;
        public static bool SameSquad(ZObject a, ZObject b) => a.Leader == b || b.Leader == a || (a.Leader != null && a.Leader == b.Leader);
        /// <summary>Spacing the movement gate keeps: squad members 14 px, everyone else the minimum + 1 px (Zod snaps a unit onto
        /// its waypoint pixel and drops the sub-pixel carry, up to 1 px, zobject.cpp:3395-3420).</summary>
        static int GateSpacing(ZObject a, ZObject b) => SameSquad(a, b) ? Math.Max((int)Math.Round(SquadSpacing * ZFormation.BodyScale(a.Type)), MinSpacing(a, b) + 1) : MinSpacing(a, b) + 1;
        /// <summary>Pairs allowed to overlap: a robot boarding its vehicle / cannon, Zod's scripted building moves, and the members
        /// of a squad just built, while they all still walk out of the factory door (Zod creates them on one pixel).</summary>
        public static bool PairExempt(ZObject a, ZObject b) =>
            a.PathInBuildingStage || b.PathInBuildingStage || a.FortRushOn || b.FortRushOn || Boards(a, b) || Boards(b, a) ||   // boarding_nb_v1 DV34: through the fort door
            (SameSquad(a, b) && a.PathMode == WpMode.ForceMove && b.PathMode == WpMode.ForceMove) ||   // a fresh squad walking out of its factory door
            ZWorld.RunOverPair(a, b) || ZWorld.RunOverPair(b, a) ||                                  // DV65: a moving enemy vehicle drives into the robot (ZWorld.RunOver.cs)
            YieldPair(a, b) || YieldPair(b, a) ||                                                    // vehicles first: a robot stepping aside for a moving vehicle of its team (ZYield.cs)
            Squeeze(a, b) || Squeeze(b, a);                                                          // DV66: a robot stepping out of a wedge past a parked hull (ZStuckGuard)
        static bool Squeeze(ZObject a, ZObject b) => a.World != null && a.World.Time < a.PS.squeezeUntil && Static(b) && !b.Stunned;
        static bool Boards(ZObject a, ZObject b) => a.Waypoints.Count > 0 && a.Waypoints[0].mode == WpMode.Enter && a.Waypoints[0].refId == b.Id;
        static double CX(ZObject o) => o.LocX + o.XOver + o.WidthPix * 0.5;
        static double CY(ZObject o) => o.LocY + o.YOver + o.HeightPix * 0.5;
        /// <summary>The unit plans through destroyable rocks (Zod: it blasts them on the way, zobject.cpp:3716-3747), unless it
        /// recently stood at one it could not attack (unarmed, no driver): then rocks are walls for its paths.</summary>
        static bool NoRocks(ZObject o) => o.HasExplosives() && !(o.World != null && o.World.Time < o.PS.rocksSolidUntil);

        // ------------------------------------------------------------------ tick hooks (ZWorld.Tick)
        /// <summary>After the killed objects are deleted, before ProcessServer: spatial hash, lanes.</summary>
        public void BeginTick()
        {
            if (!Enabled) { FlushQueueIfDisabled(); return; }                             // DV44: requests left in the queue by the fail-safe are served (ZCommit.cs)
            try { AssignSlots(); BuildHash(); EnsureLanes(); UpdateZones(); CountLaneOccupancy(); UpdateBridges(); ServePathQueue(); }   // pathing v3: zones (ZPathV3.cs); DV44: queued path requests (ZCommit.cs)
            catch (Exception e) { Fail(e); }
        }

        /// <summary>After ProcessServer, path results and side effects: overlap push, stuck monitor, deadlocks, regroup.</summary>
        public void EndTick(double dt)
        {
            if (!Enabled) return;
            try
            {
                lastDt = dt;
                Unstack();
                PushApart(dt);
                KeepOffBuildings(dt);                                                     // DV22: idle units eased out of a building's margin (ZClearance.cs)
                foreach (var o in units) if (o.IsMobile && !Static(o) && Separable(o)) Monitor(o);
                ResolveDeadlocks();
                ResolveFollowLoops();                                                     // A/B round 2 fix 16 (v3 follow only): a follower's mutual wait (ZPathV3.Owner.cs)
                Regroup();
                RecordTrails();                                                           // DV44: squad leaders' trails (ZCommit.Follow.cs)
                SettleTick();                                                             // DV44: arrived units settle (ZCommit.Settle.cs)
                UnstickTick();                                                            // pathing v3 DV47: stuck in a tight place: over soft blockers (ZPathV3.cs)
            }
            catch (Exception e) { Fail(e); }
        }

        void BuildHash()
        {
            int wpx = Math.Max(1, W.Map.WidthPix), hpx = Math.Max(1, W.Map.HeightPix);
            int cw = wpx / Cell + 1, ch = hpx / Cell + 1;
            if (cells == null || cw != gw || ch != gh)
            {
                gw = cw; gh = ch; cells = new List<ZObject>[gw * gh];
                for (int i = 0; i < cells.Length; i++) cells[i] = new List<ZObject>(4);
            }
            else foreach (var c in cells) c.Clear();
            units.Clear();
            foreach (var o in W.Objects)
            {
                if (!Separable(o)) continue;
                units.Add(o);
                cells[CellOf(o.CenterX, o.CenterY)].Add(o);
            }
        }

        int CellOf(int x, int y)
        {
            int cx = Math.Max(0, Math.Min(gw - 1, x / Cell)), cy = Math.Max(0, Math.Min(gh - 1, y / Cell));
            return cy * gw + cx;
        }

        /// <summary>Units whose centre was within r (+ one cell) of (x, y) at the start of the tick, in a fixed order.</summary>
        void Near(int x, int y, int r, List<ZObject> into)
        {
            into.Clear();
            if (cells == null) return;
            int x0 = Math.Max(0, (x - r) / Cell), x1 = Math.Min(gw - 1, (x + r) / Cell);
            int y0 = Math.Max(0, (y - r) / Cell), y1 = Math.Min(gh - 1, (y + r) / Cell);
            for (int cy = y0; cy <= y1; cy++)
                for (int cx = x0; cx <= x1; cx++)
                    foreach (var o in cells[cy * gw + cx]) into.Add(o);
        }

        // ------------------------------------------------------------------ ZObject hooks
        /// <summary>Start of ZObject.ProcessServer (before the waypoint switch): run the deferred actions exactly where Zod
        /// itself starts, ends or restarts a waypoint.</summary>
        public void PreProcess(ZObject o)
        {
            if (!o.IsMobile) return;
            if (!Enabled)
            {                                                                                // switched off (A/B toggle or the fail-safe): a unit the layer
                var ps = o.PS;                                                               // had parked for a deferred give-up (PathHoldForGiveUp: no path
                if (ps.giveUp || ps.arrive) { ps.giveUp = ps.arrive = ps.hasRetarget = ps.hasBackOff = ps.reissue = ps.reaim = false; o.PathKillWP(); }   // response) would wait forever
                return;
            }
            try { PreProcessCore(o); } catch (Exception e) { Fail(e); }
        }

        void PreProcessCore(ZObject o)
        {
            var s = o.PS;
            s.wait = ZPathWait.None;
            if (s.giveUp || s.arrive)
            {
                if (s.giveUp) s.GiveUps++; else s.Arrivals++;
                Log(o, s.giveUp ? "give up (KillWP)" : "crowd arrival (KillWP)");
                if (s.giveUp && o.Waypoints.Count > 0 && o.Waypoints[0].playerGiven) s.LastOrderEnd = $"gave up at {W.Time:F1}s ({o.CenterX >> 4},{o.CenterY >> 4}) {s.lastReason}";
                s.giveUp = s.arrive = s.hasRetarget = s.reissue = false;
                bool rejoin = o.IsMinion && o.Leader != null && !o.Leader.Destroyed && o.Leader.Waypoints.Count > 0 && o.Leader.HumanDirectOrder;
                o.PathKillWP();
                if (rejoin) { o.Waypoints.Clear(); o.Waypoints.AddRange(o.Leader.Waypoints); o.SetVelocity(); Log(o, "minion rejoins the squad's direct order (DV16)"); }
            }
            if (s.hasRetarget)
            {
                s.hasRetarget = false;
                if (o.Waypoints.Count > 0)
                {
                    Log(o, $"retarget to nearest reachable ({s.retargetX},{s.retargetY})");
                    var w = o.Waypoints[0]; w.x = s.retargetX; w.y = s.retargetY; o.Waypoints[0] = w;
                    s.carryLevel = s.level;                                                  // the stuck level survives the new waypoint
                    if (!o.IsMinion) o.CloneMinionWayPoints();
                }
            }
            if (s.hasBackOff)
            {
                s.hasBackOff = false;
                if (o.Waypoints.Count > 0 && o.CanOverwriteWP())
                {
                    Log(o, $"back off to ({s.backOffX},{s.backOffY})");
                    o.Waypoints.Insert(0, ZWaypoint.Move(s.backOffX, s.backOffY, false, RefBackOff));   // like Zod's inserted DODGE / ATTACK:
                    s.BackOffs++;                                                            // the original order resumes after it
                }
            }
            if (s.reissue) { s.reissue = false; s.rebase = true; o.PathReissue(); Log(o, "re-path"); }
            if (s.reaim) { s.reaim = false; o.PathReaim(); }
            if (o.RealMoveSpeed <= 0 && o.MoveSpeed > 0) o.RealMoveSpeed = o.MoveSpeed * s.lastTileSpeed;   // research 11.9
            // Zod's SetVelocity hysteresis (zobject.cpp:3646-3647) leaves a stopped unit (typically a minion whose leader just
            // arrived) with a velocity part under 0.1 px/s: IsMoving stays true while it never moves, so the robot never engages
            // passively (ZObject.CheckPassiveEngage returns while IsMoving) and the view turns / walks it in place. The
            // separation pushes make such near-axis headings common, so the layer clears the leftover on idle units.
            if (o.Waypoints.Count == 0 && o.IsMoving) o.StopMove();
        }

        /// <summary>Start of ZObject.StartPath: resolve a path goal the unit cannot reach (or cannot stand on) to the nearest
        /// reachable point. Building entrances that cannot be reached end the order instead (their next stage drives
        /// through walls). Returns false when Find_Path must not run (a clean give-up follows next tick).</summary>
        public bool PrepareStartPath(ZObject o)
        {
            if (!Enabled) return true;
            try { bool go = PrepareStartPathCore(o); if (go) { BridgeForceSearch(o); ParkedForceSearch(o); } return go; } catch (Exception e) { Fail(e); return true; }   // boarding_nb_v1 DV36
        }

        bool PrepareStartPathCore(ZObject o)
        {
            var s = o.PS;
            bool robot = o.IsRobot, nr = NoRocks(o);
            int tx = o.PathTargetX, ty = o.PathTargetY;
            int c = Reach.UnitComp(o, nr);
            if (c < 0) return true;                                                          // off the graph: Zod's own line, the guard rescues
            if (Reach.PointOk(tx, ty, robot, nr, c)) { s.goalX = tx; s.goalY = ty; ForceSearchIfClipping(o, tx, ty, robot, nr); return true; }
            var mode = o.PathMode; int refId = o.Waypoints.Count > 0 ? o.Waypoints[0].refId : -1;
            if (mode == WpMode.EnterFort || mode == WpMode.CraneRepair || mode == WpMode.UnitRepair ||
                !NearestReachableFor(o, tx, ty, robot, nr, c, out int rx, out int ry))          // DV44: an intermediate waypoint is passed through (ZCommit.Settle.cs)
            {
                s.Remember(refId, W.Time + UnreachableMemory);
                s.giveUp = true; o.PathHoldForGiveUp();
                Log(o, $"goal ({tx},{ty}) unreachable ({mode}): give up");
                return false;
            }
            if (mode != WpMode.Attack) s.Remember(refId, W.Time + UnreachableMemory);      // idle auto-grab will not retry it
            o.PathSetCurTarget(rx, ry);
            s.goalX = rx; s.goalY = ry; s.Resolves++;
            SoftGoalNote(o, tx, ty, rx, ry);                                                 // pathing v3 DV47 (ZPathV3.cs)
            ForceSearchIfClipping(o, rx, ry, robot, nr);
            Log(o, $"goal ({tx},{ty}) not reachable: nearest reachable ({rx},{ry})");
            return true;
        }

        /// <summary>Nearest reachable point to an unreachable goal. A point in a lane zone (bridge deck, 2-tile passage and their
        /// approaches) is only taken when nothing outside the lanes lies within 3 more tiles: a unit parked there blocks the
        /// passage for everybody else (robots wading on past the vehicles' last dry tile, a bridge).</summary>
        bool NearestReachable(int x, int y, bool robot, bool nr, int c, out int rx, out int ry)
        {
            if (!Reach.Nearest(x, y, robot, nr, c, out rx, out ry)) return false;
            if (KeepClearAt(rx, ry) == null) return true;
            double d0 = Math.Sqrt((double)(rx - x) * (rx - x) + (double)(ry - y) * (ry - y));
            if (Reach.Nearest(x, y, robot, nr, c, (px, py) => KeepClearAt(px, py) != null, out int ox, out int oy) &&
                Math.Sqrt((double)(ox - x) * (ox - x) + (double)(oy - y) * (oy - y)) <= d0 + 48) { rx = ox; ry = oy; }
            return true;
        }

        /// <summary>Zod's straight-line shortcut (DirectPathPossible) is measured from loc + 8, i.e. a vehicle's top-left tile
        /// centre, so its real box can clip a wall by up to 7 px on the way. Sweep the unit's own box along the line (4 px
        /// steps); if it touches a blocked tile, the next Find_Path runs Zod's A* instead.</summary>
        void ForceSearchIfClipping(ZObject o, int tx, int ty, bool robot, bool nr)
        {
            if (!Reach.BoxFree(o.LocX, o.LocY, o.WidthPix, o.HeightPix, robot, nr)) return;   // embedded: Zod's line, the escape rule handles it
            double x = o.CenterX, y = o.CenterY, dx = tx - x, dy = ty - y, len = Math.Sqrt(dx * dx + dy * dy);
            int half = robot ? 8 : 16, steps = (int)(len / 4) + 1;
            for (int i = 1; i <= steps; i++)
            {
                double f = Math.Min(1.0, i * 4 / Math.Max(len, 1e-9));
                int cx = (int)Math.Round(x + dx * f), cy = (int)Math.Round(y + dy * f);
                if (!Reach.BoxFree(cx - half, cy - half, half * 2, half * 2, robot, nr)) { W.Path.ForceSearch = true; return; }
            }
        }

        /// <summary>ZObject.ProcessMoveOrKillWP, a stoppable move blocked by terrain (no rock to blast): re-path from here up
        /// to twice before Zod's KillWP. Straight-line waypoints (AGRO, DODGE) keep Zod's behaviour.</summary>
        public bool OnBlocked(ZObject o, int stopX, int stopY)
        {
            if (!Enabled) return false;
            try { return OnBlockedCore(o, stopX, stopY); } catch (Exception e) { Fail(e); return false; }
        }

        bool OnBlockedCore(ZObject o, int stopX, int stopY)
        {
            if (BridgeRealign(o, stopX, stopY)) return true;                                 // off the centre line at a deck mouth: line up first (ZBridges.cs)
            var m = o.PathMode;
            if (m == WpMode.None || m == WpMode.Agro || m == WpMode.Dodge) return false;
            var s = o.PS;
            if (s.blockRepaths > 0 && Math.Abs(o.CenterX - s.blockX) + Math.Abs(o.CenterY - s.blockY) > 24) s.blockRepaths = 0;
            if (W.Time - s.lastTraffic < 0.5 && s.blockRepaths >= 2) s.blockRepaths = 1;      // pushed / sidestepped into the wall by traffic: keep trying
            if (o.HasExplosives() && W.Time >= s.rocksSolidUntil && !W.Path.TilePassableGrid(stopX >> 4, stopY >> 4, o.IsRobot, false) &&
                W.Path.TilePassableGrid(stopX >> 4, stopY >> 4, o.IsRobot, true))
            {                                                                                // a destroyable rock it could not blast: Zod's
                s.rocksSolidUntil = W.Time + 60;                                             // path (through rocks) would hit it again
                var (rx, ry) = o.PathFinalPoint;
                var rp = Search(o, rx, ry, false, out bool rf);
                if (rp != null && rf) { o.PathSetPoints(rp); CleanPaths++; s.lastProgress = W.Time; Log(o, $"rock at ({stopX},{stopY}) it cannot blast: route round it, {rp.Count} points"); return true; }
            }
            if (s.blockRepaths == MaxBlockTries - 1)
            {                                                                                // last try: a clean search from where the unit really stands
                var (fx, fy) = o.PathFinalPoint;
                var pts = Search(o, fx, fy, false, out bool found);
                if (pts != null && found) { s.blockRepaths++; o.PathSetPoints(pts); CleanPaths++; Log(o, $"blocked by terrain at ({stopX},{stopY}): clean search, {pts.Count} points"); return true; }
            }
            if (s.blockRepaths >= MaxBlockTries)
            {
                s.blockRepaths = 0; s.GiveUps++; Log(o, $"blocked by terrain at ({stopX},{stopY}) again: Zod KillWP");
                if (o.Waypoints.Count > 0 && o.Waypoints[0].playerGiven) s.LastOrderEnd = $"terrain block at ({stopX >> 4},{stopY >> 4}) {W.Time:F1}s";
                return false;
            }
            if (s.blockRepaths == 0) { s.blockX = o.CenterX; s.blockY = o.CenterY; }
            s.blockRepaths++;
            s.lastProgress = W.Time;                                                         // give the fix time before the stuck guard looks again
            // Zod's A* points are tile centres (vehicles: 2x2 corners) and assume the unit stands on one; a unit pushed or stopped
            // off-centre clips a corner on its first leg: walk to the own tile centre first, then resume the same path
            var (ftx, fty) = ZReach.FootTile(o.LocX, o.LocY);
            var (fcx, fcy) = ZReach.FootCentre(ftx, fty, o.IsRobot);
            if ((Math.Abs(fcx - o.CenterX) > 1 || Math.Abs(fcy - o.CenterY) > 1) &&
                Reach.BoxFree(fcx - (o.WidthPix >> 1), fcy - (o.HeightPix >> 1), o.WidthPix, o.HeightPix, o.IsRobot, false))
            {
                o.PathRecenter(fcx, fcy);
                Log(o, $"blocked by terrain at ({stopX},{stopY}): step to tile centre ({fcx},{fcy}) first");
                return true;
            }
            Log(o, $"blocked by terrain at ({stopX},{stopY}): re-path {s.blockRepaths}");
            o.StopMove();
            s.reissue = true; s.Repaths++;
            return true;
        }

        /// <summary>Research 11.9: a minion never stoppable walks into walls. With the layer on, a minion on a normal
        /// (stoppable) order may not step from a clear spot into a blocked tile; it re-paths like a leader instead.</summary>
        public bool MinionWouldClip(ZObject o, int inx, int iny, out int stopX, out int stopY)
        {
            stopX = stopY = 0;
            var pf = W.Path;
            if (pf.WithinImpassable(o.LocX + 1, o.LocY + 1, o.WidthPix - 2, o.HeightPix - 2, out _, out _, o.IsRobot)) return false;
            return pf.WithinImpassable(inx + 1, iny + 1, o.WidthPix - 2, o.HeightPix - 2, out stopX, out stopY, o.IsRobot);
        }

        /// <summary>A unit already overlapping a blocked tile (spawned in a doorway, pushed by Zod's scuffle) may take any step
        /// that does not overlap more blocked tiles, so it can walk out instead of being frozen by Zod's check forever.</summary>
        public bool EmbeddedEscape(ZObject o, int inx, int iny)
        {
            if (!Enabled) return false;
            int now = ImpassCount(o, o.LocX, o.LocY);
            return now > 0 && ImpassCount(o, inx, iny) <= now;
        }

        /// <summary>Research 11.9: an impassable-class tile has walk speed 0 and would freeze the unit; keep the last speed.</summary>
        public double SafeTileSpeed(ZObject o, double tile)
        {
            if (tile > 0) { o.PS.lastTileSpeed = tile; return tile; }
            return Enabled ? o.PS.lastTileSpeed : tile;
        }

        /// <summary>Blocked tiles (unit's own grid, rocks included) under the box shrunk by 1 px; off the map counts 1000.</summary>
        public int ImpassCount(ZObject o, int locX, int locY)
        {
            var pf = W.Path;
            int x = locX + 1, y = locY + 1, w = o.WidthPix - 2, h = o.HeightPix - 2;
            if (x < 0 || y < 0 || x + w >= pf.WidthPix || y + h >= pf.HeightPix) return 1000;
            int n = 0;
            for (int j = y >> 4; j <= (y + h - 1) >> 4; j++)
                for (int i = x >> 4; i <= (x + w - 1) >> 4; i++)
                    if (!pf.TilePassable(i, j, o.IsRobot) && !SoftFree(o, i, j)) n++;            // pathing v3 DV47: on / over debris only hard tiles count (ZPathV3.cs)
            return n;
        }

        /// <summary>A push / sidestep may put the unit here: clear of blocked tiles, or (for a unit already embedded) no worse.</summary>
        public bool BoxAllowed(ZObject o, int oldLocX, int oldLocY, int newLocX, int newLocY)
        {
            int n = ImpassCount(o, newLocX, newLocY);
            if (n == 0) return MarginOk(o, oldLocX, oldLocY, newLocX, newLocY);          // DV22: never deeper into a building's clearance margin
            if (n >= 1000) return false;
            int was = ImpassCount(o, oldLocX, oldLocY);
            return was > 0 && n <= was;
        }

        bool BoxClear(ZObject o, double lx, double ly) => BoxAllowed(o, o.LocX, o.LocY, (int)Math.Floor(lx), (int)Math.Floor(ly));

        // ------------------------------------------------------------------ queries for tests / overlay
        /// <summary>True while the unit waits in an orderly way: at a lane, behind a unit that is moving or itself waits in
        /// order, at the repair station, or while fighting.</summary>
        public bool IsQueueWaiting(ZObject o)
        {
            var s = o.PS;
            if (o.AttackObject != null || o.PathWaitingForRepair || o.PathInBuildingStage) return true;
            if (BridgeWaitOk(o)) return true;                                             // in a bridge queue (ZBridges.cs)
            if (FollowerWaitOk(o)) return true;                                           // DV44: waits for its place in the squad while its leader walks / waits in order (ZCommit.Follow.cs)
            return TrafficWaitOk(o);
        }
    }
}
