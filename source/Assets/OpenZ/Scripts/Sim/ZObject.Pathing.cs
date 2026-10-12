using System;
using System.Collections.Generic;

namespace OpenZ.Sim
{
    /// <summary>Why a unit did not take its full Zod step this tick (set by <see cref="ZPathing.Gate"/>).</summary>
    public enum ZPathWait { None, Queue, Sidestep, Held, Lane, Arrived, Bridge }

    /// <summary>Per-unit state of the OpenZ pathing layer (<see cref="ZPathing"/>): the gate result of this tick, deferred
    /// actions for the start of the unit's next ProcessServer, the stuck monitor and counters for the stress test.</summary>
    public sealed class ZPathState
    {
        // gate result (reset every tick in PreProcess)
        public ZPathWait wait;
        public ZObject heldBy;                     // the unit (or lane occupant) this one is waiting for
        public int heldTicks; public double heldSince;
        // deferred actions (run by ZPathing.PreProcess, before the waypoint switch, where Zod itself starts a waypoint)
        internal bool giveUp, arrive, reissue, reaim, hasRetarget, hasBackOff;
        /// <summary>A new order: drop every action queued for the previous one.</summary>
        internal void ClearPending() { giveUp = arrive = reissue = reaim = hasRetarget = hasBackOff = false; level = carryLevel = 0; blockRepaths = 0; }
        internal int retargetX, retargetY, backOffX, backOffY;
        // stuck monitor
        internal ZWaypoint tracked = ZWaypoint.Cleared;
        internal int level, carryLevel, blockRepaths;
        internal double best = double.MaxValue, lastProgress, laneWaitSince = -1, blockX, blockY, ghostUntil = -1, yieldUntil = -1;
        internal int goalX = int.MinValue, goalY = int.MinValue;
        internal bool recentered; internal int regroupLeaderX = int.MinValue, regroupLeaderY = int.MinValue; internal double regroupDist, regroupRetry = -1;
        internal double moveDoneAt = -1; internal bool moveDoneArrived;                // last MOVE of this unit ended (time, near its goal?)
        internal int groupX = int.MinValue, groupY, groupSlotX, groupSlotY;              // group order: clicked point and this unit's spot
        internal double stillX = double.NaN, stillY, stillSince;                          // watchdog: where / since when the unit has not moved
        internal double trafficSince = -1, lastTraffic = -100, lastPushed = -100;   // waiting for other units (queue, lane, jostle)
        internal int sideSign = 1; internal double sideUntil = -1, sideSince = -1, noSideUntil = -1;
        internal double nextDetour = -1; internal string lastReason; internal bool rebase;
        // separation
        internal double pushX, pushY, pinnedUntil = -1, carryX, carryY; internal bool pinned;
        internal double nextShove = -1, nextRegroup = -1, lastTileSpeed = 1.0;
        internal double rocksSolidUntil = -1;
        internal double vehicleYieldUntil = -1;                                           // vehicles first: a step-aside made for a vehicle of its team counts until then (ZYield.cs)
        internal double squeezeUntil = -1;                                                // DV66 review round 1: stepping out of a wedge past a parked hull (no separation against parked units)
        internal double nextKeepBackOut = -1;                                             // last back-out of a lane mouth (rate limit)                                            // hit a rock it cannot blast: rocks are walls for its paths
        internal readonly List<(int id, double until)> unreachable = new List<(int, double)>();
        internal readonly ZBridgeUnit bridge = new ZBridgeUnit();                              // bridge queue state (ZBridges.cs)
        // counters (stress test / debug overlay)
        public int Repaths, Resolves, GiveUps, Arrivals, Shoves, BackOffs, Yields, Regroups, FollowThroughs, Watchdogs, Displaced;
        /// <summary>Why the last player / bot order of this unit ended early (give-up reason), for the stress report.</summary>
        public string LastOrderEnd;
        /// <summary>Last pathing decisions for this unit (only while ZPathing.Trace is on; debug overlay / stress report).</summary>
        public List<string> Log;

        internal bool IsUnreachable(int id, double now)
        {
            for (int i = unreachable.Count - 1; i >= 0; i--)
            {
                if (unreachable[i].until < now) { unreachable.RemoveAt(i); continue; }
                if (unreachable[i].id == id) return true;
            }
            return false;
        }
        internal void Remember(int id, double until)
        {
            if (id < 0) return;
            for (int i = 0; i < unreachable.Count; i++) if (unreachable[i].id == id) { unreachable[i] = (id, until); return; }
            unreachable.Add((id, until));
        }
    }

    /// <summary>ZObject members used by the OpenZ pathing layer (they need the private waypoint state `cur` / `lastWp`).</summary>
    public partial class ZObject
    {
        internal readonly ZPathState PS = new ZPathState();
        /// <summary>Pathing state for tests, the debug overlay and the view.</summary>
        public ZPathState PathState => PS;
        public ZPathWait PathWait => PS.wait;
        /// <summary>True while the pathing layer holds the unit (queue, lane, crowd) for 5+ ticks: the view shows it standing,
        /// not walking in place (Dx/Dy keep Zod's heading).</summary>
        public bool PathHeld => PS.heldTicks >= 5 && World?.Pathing != null && World.Pathing.Enabled;   // (layer switched off: Zod's own IsMoving)
        /// <summary>Auto-behaviours (agro, attack-move, idle auto-grab) skip a target this unit recently failed to reach.</summary>
        public bool PathUnreachable(ZObject o) => o != null && World?.Pathing != null && World.Pathing.Enabled && PS.IsUnreachable(o.Id, World.Time);

        internal WpMode PathMode => Waypoints.Count > 0 ? Waypoints[0].mode : WpMode.None;
        /// <summary>Zod's scripted, unblockable building moves (crane / repair / fort: drive in, drive out).</summary>
        internal bool PathInBuildingStage => Waypoints.Count > 0 && (cur.stage == Stage.EnterBuilding || cur.stage == Stage.ExitBuilding || cur.stage == Stage.Repairing || cur.stage == Stage.RepairLeave) &&
            (Waypoints[0].mode == WpMode.CraneRepair || Waypoints[0].mode == WpMode.UnitRepair || Waypoints[0].mode == WpMode.EnterFort);
        internal bool PathWaitingForRepair => Waypoints.Count > 0 && Waypoints[0].mode == WpMode.UnitRepair && cur.stage == Stage.Wait;
        internal bool PathPending => cur.pathFindingId != 0;
        internal int PathTargetX => cur.x;
        internal int PathTargetY => cur.y;
        internal bool PathOnLastLeg => cur.pfPoints == null || cur.pfPoints.Count == 0;
        internal (int x, int y)? PathNextPoint => cur.pfPoints != null && cur.pfPoints.Count > 0 ? cur.pfPoints[0] : ((int, int)?)null;
        internal (int x, int y) PathFinalPoint => cur.pfPoints != null && cur.pfPoints.Count > 0 ? cur.pfPoints[cur.pfPoints.Count - 1] : (cur.x, cur.y);

        /// <summary>Length of the rest of the current path: centre -> current point -> remaining A* points.</summary>
        internal double PathRemaining()
        {
            double d = Math.Sqrt((double)(CenterX - cur.x) * (CenterX - cur.x) + (double)(CenterY - cur.y) * (CenterY - cur.y));
            if (cur.pfPoints == null) return d;
            int px = cur.x, py = cur.y;
            foreach (var p in cur.pfPoints) { d += Math.Sqrt((double)(p.x - px) * (p.x - px) + (double)(p.y - py) * (p.y - py)); px = p.x; py = p.y; }
            return d;
        }

        /// <summary>Restart Waypoints[0] next tick exactly like a new Zod waypoint (SetTarget + Find_Path from here).</summary>
        internal void PathReissue() => lastWp = ZWaypoint.Cleared;
        internal void PathKillWP() { if (Waypoints.Count > 0) KillWP(); }
        /// <summary>Re-aim the current straight segment from where the unit stands (after a sidestep or a push), so Zod's
        /// ReachedTarget overshoot test never snaps it back across the offset.</summary>
        internal void PathReaim() { if (Waypoints.Count > 0 && IsMoving) { SetTarget(cur.x, cur.y); SetVelocity(); } }
        internal void PathSetCurTarget(int x, int y) => SetTarget(x, y);
        /// <summary>Walk to (x, y) first (the centre of the unit's own footprint tile), then carry on with the current path:
        /// Zod's A* points are tile centres and assume the unit starts on one; off-centre it can clip a corner.</summary>
        internal void PathRecenter(int x, int y)
        {
            cur.pfPoints ??= new List<(int x, int y)>();
            cur.pfPoints.Insert(0, (cur.x, cur.y));
            SetTarget(x, y); SetVelocity();
        }
        /// <summary>Replace the rest of the current path (detour): head for the first point now, the others after it.</summary>
        internal void PathSetPoints(List<(int x, int y)> pts)
        {
            cur.pathFindingId = 0; cur.gotPfResponse = true;
            cur.pfPoints = pts;
            SetTarget(pts[0].x, pts[0].y); SetVelocity();
            pts.RemoveAt(0);
        }
        /// <summary>Stop and wait for the deferred give-up (the waypoint handlers return on !gotPfResponse).</summary>
        internal void PathHoldForGiveUp() { StopMove(); cur.gotPfResponse = false; cur.pathFindingId = 0; }

        /// <summary>Move the unit by a small offset (separation push / rescue slide), keeping Zod's sub-pixel carry.</summary>
        internal bool PathNudge(double dx, double dy, bool ignoreWalls = false)
        {
            double nx = LocX + XOver + dx, ny = LocY + YOver + dy;
            int inx = (int)Math.Floor(nx), iny = (int)Math.Floor(ny);
            if (!ignoreWalls && !World.Pathing.BoxAllowed(this, LocX, LocY, inx, iny)) return false;
            LocX = inx; LocY = iny; XOver = nx - inx; YOver = ny - iny;
            CenterX = LocX + (WidthPix >> 1); CenterY = LocY + (HeightPix >> 1);
            return true;
        }
    }
}
