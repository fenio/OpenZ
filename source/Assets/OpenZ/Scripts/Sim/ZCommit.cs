using System;
using System.Collections.Generic;

namespace OpenZ.Sim
{
    /// <summary>
    /// Sim-side, deterministic (fixed
    /// tick, object / id order, no RNG, no clock). Zod's speeds, ranges, walkable tiles and water / bridge rules are untouched:
    /// every unit's step is at most its own Zod speed x the DV24 catch-up (minions on dry ground only, never in water); the
    /// changes are about where the pathing layer (DV6 / DV24, OpenZ's own) steers, waits and pushes, and when path searches run.
    /// Pathing v3 (ZPathV3.cs): every rule below applies only to units outside the bridge / bottleneck ZONES (in a zone the live layer
    /// runs); ZWorld.PathV3Deviation = false restores the pathing layer exactly as before (bit for bit).
    ///  1. Squads follow their leader (ZCommit.Follow.cs): a minion on its leader's MOVE makes no path search of its own; it
    ///     keeps its DV24 V slot measured ALONG the leader's walked trail, or walks single file on the trail where the V does not
    ///     fit, at a pace from its own Zod speed (DV24 catch-up on dry ground) with hysteresis on every switch.
    ///  2. Path requests are budgeted per tick by their measured work (ZPathFinder.Work: Zod A* open-list scans and closed-list
    ///     walks; the layer's own searches: heap pops), at most SearchCap requests start per 10 ms tick; the rest wait first come
    ///     first served (orders arrive leader first, id order), standing like a unit waiting for its path answer. A request that
    ///     starts runs whole (with its clean-up searches: wall clip, bridge and parked-unit re-plans), so one tick does at most
    ///     WorkBudget + one request's work. (Zod itself starts one path thread per request, zpath_finding.cpp:885, and the threads
    ///     run concurrently; OpenZ runs them in the sim tick, so a group order is spread over ticks instead.) The budget is in
    ///     ticks and work units, never frame time: deterministic.
    ///  3. Arrival (ZCommit.Settle.cs): a unit whose MOVE ended settles; separation pushes have a dead-band (small overlaps are
    ///     left alone, a push that starts carries on until the pair is clear, never a sub-0.1 m correction of a standing unit),
    ///     and a unit near its goal that is blocked by its own team arrives instead of inching.
    /// </summary>
    public sealed class ZCommitState
    {
        // ---- squad leader: the breadcrumb trail of the current MOVE (oldest first) and the squad's follow mode
        internal readonly List<(double x, double y)> trail = new List<(double x, double y)>(96);
        internal ZWaypoint trailWp = ZWaypoint.Cleared;
        internal ZWaypoint trailNext = ZWaypoint.Cleared, trailNextOf = ZWaypoint.Cleared;   // the waypoint queued after trailWp (a waypoint list walks on one trail)
        internal double trailLen;                          // arc length oldest crumb -> newest crumb
        internal double startHx, startHy; internal bool startDir;   // first walking direction of the order (the trail's backward extension)
        internal double orderAt = -100;                    // when this order's trail started (the squad's set-off window: StartTime)
        internal int squadMode;                            // 0 = V, 1 = single file on the trail
        internal double modeSince = -100;
        internal int vOkTicks;
        internal readonly List<ZObject> fileOrder = new List<ZObject>(6);
        internal readonly List<ZObject> vOrder = new List<ZObject>(6); internal bool vAssigned;   // V slots by position (1..)
        internal double evalTime = double.NaN;
        // ---- every squad member: its smoothed walking speed (px/s, measured each tick)
        internal double lastX = double.NaN, lastY, vF;
        internal double lastHx, lastHy, lastMoveAt = -100;   // its last walking direction and when (regroup: no turning round)
        // ---- minion
        internal bool noPath;                              // follows its leader with no Zod path of its own (no search was made)
        internal bool ownPath; internal ZWaypoint ownWp = ZWaypoint.Cleared;   // left the follow for this order: its own (budgeted) path
        public int followMode;                             // traces: 0 -, 1 V, 2 file, 3 own path, 4 stands in the follow, 5 side-steps, 6 regroup
        internal bool standing;                            // stands in its squad's follow (walks again over GoSpeed)
        internal double vk = 1; internal int vWide, vBad;   // V side width (1 / 0.5), ticks with room for a wider one / with no slot
        internal double arc, off; internal bool aheadOfLeader;   // this tick (EvalSquad): its place along its leader's trail (px behind him), off it, level with / ahead of him
        internal bool wantOn; internal double wantHx, wantHy;   // where its follow steers this tick (mates step out of that line)
        internal bool bridgeLeft;                          // left the follow for a bridge (re-joins only well clear of it)
        internal double vCmd;                              // the pace its follow set this tick (px/s, after the mate cap; 0 standing): the one behind sets off with it
        internal int predId = -1;                          // file: the unit it follows this tick (a mate waiting for it never blocks it)
        internal int sideX, sideY; internal double sideUntil = -1;   // a committed side-step out of a mate's / the leader's line
        internal int clearSide; internal double clearAt = -100;   // the side it stepped out of the leader's line to on this order (kept)
        internal string why;                               // debug: why the follow was left
        internal double mateFreeUntil = -1;                // may go round a standing mate until then
        internal int wallBlocks; internal double followSince;   // follow steps blocked by a wall in a row (own path for 1, 2, 4.. s), follow start
        // ---- path request queue
        internal bool queued, serving; internal ZWaypoint queuedWp; internal double queuedAt;
        internal bool orderWaits;                          // the first path request after a large group order with the live rules waits its turn in the queue too (ZPathV3.cs SpreadGroupV3)
        // ---- settle
        internal int pushPartner = -1, pushNext = -1;      // separation push in progress with this unit (hysteresis per pair)
        internal bool easing;                              // DV22 idle ease in progress (dead-band)
        internal double nextReslot = -1;                   // group order: last free-spot search (rate limit)
        internal int detours;                              // group order: detours round its own arrived group on this order
        internal double groupR;                            // group order: how far its spots reach from the clicked point (ZSlots, DV44)
        internal int regroupSpotX = int.MinValue, regroupSpotY, regroupTries;   // minion: timed regroup retries at this leader spot
        internal ZWaypoint detourWp = ZWaypoint.Cleared;
        // ---- pathing v3 (ZPathV3.cs): zone state (own: tile / way ahead + hysteresis; encounter; effective = the squad's), a group
        // order's live arrival rules, and the DV47 soft unstick (progress clock, permission, goal behind debris)
        internal bool zoneOwn, zoned, zonedNext, goalZoned, sticky; internal double zoneLastIn = -100, zoneOffAt = -100; internal ZWaypoint goalWp = ZWaypoint.Cleared, stickyWp = ZWaypoint.Cleared;
        internal bool encOn, encWas, encHit; internal double encSeen = -100;
        internal bool passOwn, zonedDyn; internal double passLastIn = -100;              // zoned by an active bottleneck passage (opposing traffic)
        internal bool liveOrder; internal double liveLastIn = -100;                       // review round 2: the live-order term's exit hold
        internal ZWaypoint zoneWp = ZWaypoint.Cleared;                                    // review round 2: the order the zone state was decided for (a new one re-decides)
        internal double stallSince = -1, followAlong = double.NaN; internal double stallX = double.NaN, stallY;   // review round 2: a follower that stands while its leader walks
        internal double mStandX = double.NaN, mStandY, mStandSince = -1;                 // review round 2: DV47 clock for a minion left standing behind its leader
        internal double holdD = -1; internal int holdBy = -1; internal double holdSince = -1;   // review round 2: a zoned minion's resume hysteresis behind its own squad mate
        internal bool tightNow, liveZOff, passActive; internal int zwhy, zCls, zSwitchCls = -1, zSwitchX, zSwitchY; internal double zSwitchAt = -100, zSwitchPrevAt = -100; internal ZWaypoint zSwitchWp = ZWaypoint.Cleared; internal double tightLastIn = -100, zWantLast = -100;                              // review round 2: in a tight place this tick; the live order's zone term ended; why zoned (checks)
        internal bool passLive, oppNow, oppSquad, stuckLive, liveOrderPass; internal double passLiveIn = -100, stuckLiveAt = -100, headLast = -100; internal int stuckLiveX, stuckLiveY; internal ZWaypoint stuckLiveWp = ZWaypoint.Cleared, passLiveWp = ZWaypoint.Cleared, dvSpotWp = ZWaypoint.Cleared, passLiveAtOrder = ZWaypoint.Cleared;   // review round 3: a passage live one way; opposing traffic; DV47's live hand-off
        internal double stOdo, stRem = double.NaN, stActive, stNextSample; internal readonly List<(double t, double odo)> stHist = new List<(double t, double odo)>(24); internal bool shortTodo;
        internal double softUntil, softOffSince = -1, softNextTry = -1;
        internal ZWaypoint stWp = ZWaypoint.Cleared, softWp = ZWaypoint.Cleared;
        internal bool softPass, softWasOn;
        internal ZWaypoint softGoalWp = ZWaypoint.Cleared; internal int softGoalX, softGoalY; internal double softHoldSince = -1;   // DV47: the real goal behind debris
    }

    public partial class ZObject
    {
        internal readonly ZCommitState PC = new ZCommitState();
        /// <summary>DV44 (traces / checks): 0 -, 1 V, 2 single file on the leader's trail, 3 own path, 4 stands in the follow, 5 side-steps, 6 regroup.</summary>
        public int PathFollowMode => PC.followMode;
        /// <summary>Checks (parity pace rows): the pathing layer pushed this unit off an overlap this tick.</summary>
        public bool PathPushedThisTick => PS.pushX * PS.pushX + PS.pushY * PS.pushY > 1e-8;
        internal const int QueuedPathId = -7;               // cur.pathFindingId while the request waits in the DV44 queue (Find_Path ids are > 0)

        /// <summary>DV44 queue: park the unit until its request is served (the waypoint handlers wait on !gotPfResponse /
        /// pathFindingId != 0, exactly like for Zod's path thread).</summary>
        internal void PathQueueHold() { StopMove(); cur.gotPfResponse = false; cur.pathFindingId = QueuedPathId; }
        internal bool PathQueuedStill() => World.Get(Id) == this && !Destroyed && KillAt < 0 && Waypoints.Count > 0 && Waypoints[0].Equals(PC.queuedWp) && cur.pathFindingId == QueuedPathId;
        /// <summary>Run the queued Find_Path now (StartPath from where the unit stands, to the target it had).</summary>
        internal void PathServeQueued() { cur.pathFindingId = 0; StartPath(); }

        /// <summary>DV44 (StartPath): a minion on its leader's MOVE walks with its leader (V / single file on the trail) and makes no
        /// search of its own. False = it needs its own path (not a minion, left the follow, another order).</summary>
        internal bool FollowsInsteadOfPath()
        {
            if (!IsMinion || Kind != ZKind.Robot || !ZWorld.FormationDeviation || Waypoints.Count == 0) return false;
            var wp = Waypoints[0];
            if (wp.mode != WpMode.Move || wp.refId == ZPathing.RefRegroup || Leader == null || Leader.Destroyed || Leader.KillAt >= 0 || Leader.Waypoints.Count == 0 || !Leader.Waypoints[0].Equals(wp)) return false;
            if (PC.ownPath && PC.ownWp.Equals(wp)) return false;
            if (World.Time < formRetry) return false;
            cur.gotPfResponse = true; cur.pathFindingId = 0; cur.pfPoints = null;
            StopMove(); PC.noPath = true;
            return true;
        }
    }

    public sealed partial class ZPathing
    {
        /// <summary>DV44 parts, per world (A/B switches for the checks; all on in the game, set once when the world is made).</summary>
        public bool CommitFollow = TestPartDefaults[0], CommitBudget = TestPartDefaults[1], CommitSettle = TestPartDefaults[2];
        /// <summary>Headless A/B harnesses only (never set by the game): the parts a new world starts with.</summary>
        internal static readonly bool[] TestPartDefaults = { true, true, true };
        /// <summary>Path search work allowed to START per 10 ms sim tick (ZPathFinder.Work units + the layer's heap pops x PopWork),
        /// and requests started per tick at most. A request that would go over waits in the queue for the next tick; a started
        /// request is never split (its clean-up searches included), so a tick does at most WorkBudget + one request.</summary>
        public const long WorkBudget = 1000000;
        public const int SearchCap = 4, PopWork = 12;
        internal bool CommitOn => Enabled && ZWorld.PathV3Deviation;

        // ---- counters (checks / debug overlay)
        internal long layerWork;                                           // Search / ParkedSearch / SearchW heap pops x PopWork
        internal int layerSearches;                                        // Search / ParkedSearch / SearchW runs (the layer's own A*)
        /// <summary>Every path search so far: Zod's Find_Path A* runs plus the layer's own searches.</summary>
        public int SearchesTotal => W.Path.AStarRuns + layerSearches;
        /// <summary>Path search work so far (Zod A* + the layer's searches), the unit of the DV44 budget.</summary>
        public long NodesTotal => W.Path.Work + layerWork;
        public int MaxSearchesPerTick { get; private set; }
        /// <summary>Most path requests started (served / let through by the budget) in one tick: at most SearchCap.</summary>
        public int MaxStartsPerTick { get; private set; }
        public long MaxWorkPerTick { get; private set; }
        public int QueuedRequests { get; private set; }
        public int MaxQueueLength { get; private set; }
        public double MaxQueueWait { get; private set; }
        public int FollowNoSearch { get; private set; }
        int tickSearchBase, startedThisTick; long tickWorkBase;
        readonly List<ZObject> pathQueue = new List<ZObject>();
        /// <summary>Checks: restart the per-tick maxima (a Play check measures one order).</summary>
        public void ResetSearchStats() { MaxSearchesPerTick = 0; MaxStartsPerTick = 0; MaxWorkPerTick = 0; MaxQueueLength = 0; MaxQueueWait = 0; }

        bool BudgetLeft => startedThisTick < SearchCap && NodesTotal - tickWorkBase < WorkBudget;

        /// <summary>ZObject.StartPath hook (first line). True = no Find_Path now: the minion follows its leader, or the request waits
        /// in the queue (budget used up this tick, or older requests waiting).</summary>
        public bool DeferPath(ZObject o)
        {
            if (!CommitOn) return false;
            try
            {
                var c = o.PC;
                if (c.serving) return false;                                              // served from the queue right now
                if (FollowOn(o) && o.FollowsInsteadOfPath()) { FollowNoSearch++; return true; }   // (v3: not in a zone)
                c.noPath = false;                                                         // it plans its own path from here on
                if (!BudgetFor(o) && !c.orderWaits) return false;                         // (v3: a unit in a zone plans at once, like live; but the first request of a large group order waits its turn)
                if (pathQueue.Count == 0 && BudgetLeft) { startedThisTick++; c.orderWaits = false; return false; }
                for (int i = pathQueue.Count - 1; i >= 0; i--) if (pathQueue[i] == o) pathQueue.RemoveAt(i);
                pathQueue.Add(o);
                c.queued = true; c.queuedWp = o.Waypoints.Count > 0 ? o.Waypoints[0] : ZWaypoint.Cleared; c.queuedAt = W.Time;
                o.PathQueueHold();
                QueuedRequests++;
                if (pathQueue.Count > MaxQueueLength) MaxQueueLength = pathQueue.Count;
                Log(o, $"DV44: path request queued ({pathQueue.Count} waiting)");
                return true;
            }
            catch (Exception e) { Fail(e); return false; }
        }

        /// <summary>BeginTick: the searches of the last tick are counted; the oldest waiting requests are served while the budget lasts.</summary>
        void ServePathQueue()
        {
            int used = SearchesTotal - tickSearchBase; long work = NodesTotal - tickWorkBase;
            if (used > MaxSearchesPerTick) MaxSearchesPerTick = used;
            if (work > MaxWorkPerTick) MaxWorkPerTick = work;
            if (startedThisTick > MaxStartsPerTick) MaxStartsPerTick = startedThisTick;
            tickSearchBase = SearchesTotal; tickWorkBase = NodesTotal; startedThisTick = 0;
            if (!CommitOn) { if (pathQueue.Count > 0) FlushQueue(); return; }
            while (pathQueue.Count > 0 && BudgetLeft)
            {
                var o = pathQueue[0]; pathQueue.RemoveAt(0);
                var c = o.PC;
                if (!c.queued) continue;
                c.queued = false;
                if (!o.PathQueuedStill()) continue;                                        // a new order, dead, or the waypoint ended meanwhile
                double waited = W.Time - c.queuedAt; if (waited > MaxQueueWait) MaxQueueWait = waited;
                c.serving = true; startedThisTick++; c.orderWaits = false;
                try { o.PathServeQueued(); } finally { c.serving = false; }
            }
        }

        /// <summary>BeginTick with the layer off (switched off, or its fail-safe tripped): requests still waiting in the DV44 queue are
        /// served at once (pure pre-DV44 behaviour from now on), so no unit stays parked with pathFindingId = QueuedPathId.</summary>
        void FlushQueue()
        {
            var q = new List<ZObject>(pathQueue); pathQueue.Clear();
            foreach (var o in q) { var c = o.PC; if (!c.queued) continue; c.queued = false; if (!o.PathQueuedStill()) continue; c.serving = true; try { o.PathServeQueued(); } finally { c.serving = false; } }
        }

        /// <summary>BeginTick hook that runs even with the layer disabled: a queue left behind by the fail-safe is flushed.</summary>
        void FlushQueueIfDisabled() { if (pathQueue.Count > 0) { try { FlushQueue(); } catch (Exception) { pathQueue.Clear(); } } }

        internal void LogPublic(ZObject o, string msg) => Log(o, msg);

        /// <summary>DV44 (CheckPath, Detour): Zod's A* points are tile centres (vehicles: 2x2 corners) and the exact goal is added last,
        /// so a path can start with a point behind the unit (it stands off its tile's centre) or end with a point beyond the goal and a
        /// step back to it. Such a first / last-but-one point is dropped when the straight leg past it is clear (the unit's own box
        /// swept on its grid) and the point does not lie in a lane / bridge keep-clear area (a lane mouth or a deck approach keeps
        /// Zod's point: the bridge and lane queues line units up on it). Everything else of Zod's path stays.</summary>
        public List<(int x, int y)> NoBackSteps(ZObject o, List<(int x, int y)> pts) => FollowOn(o) ? NoBackStepsCore(o, pts) : pts;
        /// <summary>NoBackSteps without the DV44 check (pathing v3: also a zoned minion's hand-over path, ZPathV3.cs).</summary>
        internal List<(int x, int y)> NoBackStepsCore(ZObject o, List<(int x, int y)> pts)
        {
            if (pts == null || pts.Count < 2) return pts;
            bool robot = o.IsRobot, nr = NoRocks(o);
            bool Back(double ax, double ay, double bx, double by, double cx, double cy)
            {                                                                            // the turn at b is sharper than 90 deg
                double ux = bx - ax, uy = by - ay, vx = cx - bx, vy = cy - by;
                return ux * vx + uy * vy < 0 && ux * ux + uy * uy > 0.25 && vx * vx + vy * vy > 0.25;
            }
            int guard = 0;
            while (pts.Count >= 3 && guard++ < 3 && KeepClearAt(pts[0].x, pts[0].y) == null && Back(o.CenterX, o.CenterY, pts[0].x, pts[0].y, pts[1].x, pts[1].y) &&
                   LegClear(o.CenterX, o.CenterY, pts[1].x, pts[1].y, robot, nr, o, true))
            { Log(o, $"DV44: path point ({pts[0].x},{pts[0].y}) behind it dropped"); pts.RemoveAt(0); BackStepsDropped++; }
            if (pts.Count >= 3)
            {                                                                            // (a path keeps at least two points: callers read its last one)
                int n = pts.Count;
                double ax = pts[n - 3].x, ay = pts[n - 3].y;
                if (KeepClearAt(pts[n - 2].x, pts[n - 2].y) == null && Back(ax, ay, pts[n - 2].x, pts[n - 2].y, pts[n - 1].x, pts[n - 1].y) &&
                    LegClear((int)ax, (int)ay, pts[n - 1].x, pts[n - 1].y, robot, nr, o, false))
                { Log(o, $"DV44: path point ({pts[n - 2].x},{pts[n - 2].y}) beyond the goal dropped"); pts.RemoveAt(n - 2); BackStepsDropped++; }
            }
            return pts;
        }
        /// <summary>Checks: path points NoBackSteps dropped.</summary>
        public int BackStepsDropped { get; private set; }

        /// <summary>b arrived on the same group order as a (same clicked point) and stands.</summary>
        internal bool GroupSettled(ZObject a, ZObject b) => SettleFor(a, b) && b.Waypoints.Count == 0 && a.PS.groupX != int.MinValue && a.PS.groupX == b.PS.groupX && a.PS.groupY == b.PS.groupY;   // (group keys end with the next order: GroupOrderReset)
        /// <summary>TryShove / CanDisplace: an arrived unit of the same group order is gone round (or arrived next to), never nudged or
        /// sent walking by its own group: neither by a unit still walking in, nor by a regroup of another squad (only in or at a lane,
        /// gap or bridge the evacuation rules still move it).</summary>
        internal bool GroupSettledNear(ZObject a, ZObject b) => GroupSettled(a, b) && a.Waypoints.Count > 0 && a.Waypoints[0].mode == WpMode.Move && !InNarrowArea(b) && !InNarrowArea(a)
                                                                && !(SameSquad(a, b) && a.Waypoints[0].refId == RefRegroup)
                                                                && !MinionMustPass(a, b);   // review round 3: a minion rejoining its leader held 1 s by it (ZCommit.Settle.cs)

    }

    public sealed partial class ZWorld
    {
        /// <summary>Pathing v3 (ZPathV3.cs): OpenZ deviations DV44 (path commit outside bridge / bottleneck zones: squads follow their
        /// leader's trail, path requests budgeted per tick, arrivals settle; ZCommit*.cs) and DV47 (soft unstick). OFF = the live pathing
        /// layer exactly as before (the one quick rollback). Static like the other DV switches; ZGame sets it at every match start
        /// settings, like every other DV switch.</summary>
        public static bool PathV3Deviation = true;
    }
}
