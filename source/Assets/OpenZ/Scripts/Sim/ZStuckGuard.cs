using System;
using System.Collections.Generic;

namespace OpenZ.Sim
{
    /// <summary>
    /// Stuck guard of the pathing layer. A unit with a movement order is stuck when its path stops getting shorter
    /// (ProgressEps = 2 px) for StuckWindow = 0.8 s while it is not waiting in an orderly way (fighting, repair queue,
    /// lane, queue behind a unit that moves or waits in order, Zod's scripted building moves). Each stuck step escalates
    /// once per waypoint and never goes back without real progress, so nothing oscillates:
    ///   1. re-path from here exactly like a new Zod waypoint (SetTarget + Find_Path, zobject.cpp:2709-2733);
    ///   2. a MOVE resolves its goal to the nearest point reachable from where the unit stands (ZReach); other orders skip 2;
    ///   3. clean give-up: KillWP (order cleared, the squad gets the rest of the list, the unit idles). ATTACK / AGRO / ENTER /
    ///      PICKUP / building targets are remembered as unreachable for 6 s so agro, attack-move and idle auto-grab do not
    ///      loop on them (zobject.cpp:1603-1853, :3285-3339).
    /// A FORCE_MOVE (units leaving their factory, zserver.cpp:1611-1621) is never cancelled: it passes through the units in
    /// its way for 1 s instead. Head-on deadlocks (a cycle of units each held by the next) back off the lowest-priority unit
    /// (lane travellers, then FORCE_MOVE, then vehicles, then leaders, then lower id keep going). Idle minions left more than
    /// 3 tiles from their idle leader walk back to it (Zod stops minions wherever they are when the leader finishes).
    /// </summary>
    public sealed partial class ZPathing
    {
        void Monitor(ZObject o)
        {
            var s = o.PS; double now = W.Time; var wp0 = ZWaypoint.Cleared;
            bool held = s.wait == ZPathWait.Held || s.wait == ZPathWait.Lane || s.wait == ZPathWait.Arrived || s.wait == ZPathWait.Bridge;
            if (held) { if (s.heldTicks == 0) s.heldSince = now; s.heldTicks++; } else { s.heldTicks = 0; if (s.wait != ZPathWait.Queue) s.heldBy = null; }
            bool gated = s.wait == ZPathWait.Held || s.wait == ZPathWait.Lane || s.wait == ZPathWait.Queue || s.wait == ZPathWait.Sidestep || now - s.lastPushed < 0.05;
            if (gated) { if (s.trafficSince < 0) s.trafficSince = now; s.lastTraffic = now; }
            else if (now - s.lastTraffic > 1.0) s.trafficSince = -1;                           // free for 1 s: the traffic episode is over

            if (o.Waypoints.Count == 0)
            {
                if (s.tracked.mode == WpMode.Move)
                {                                                                             // a MOVE just ended: remember how
                    s.moveDoneAt = now;
                    s.moveDoneArrived = Math.Abs(o.CenterX - s.tracked.x) + Math.Abs(o.CenterY - s.tracked.y) <= 2 * ArriveRadius;
                    if (o.IsMinion) FollowThrough(o);
                }
                if (o.Waypoints.Count == 0) { s.tracked = ZWaypoint.Cleared; s.level = 0; s.laneWaitSince = -1; s.goalX = int.MinValue; s.stillX = double.NaN; return; }
                wp0 = o.Waypoints[0];
            }
            var wp = o.Waypoints[0];
            if (wp0.mode != WpMode.None) wp = wp0;
            if (!wp.Equals(s.tracked))
            {
                s.tracked = wp; s.level = s.carryLevel; s.carryLevel = 0; s.best = double.MaxValue; s.lastProgress = now;
                s.blockRepaths = 0; s.recentered = false; s.nextDetour = -1; s.laneWaitSince = -1; s.stillX = double.NaN;
                if (CalmFresh(o)) return;                                                     // b49: the resumed order's path does not exist yet in this tick: measure it from the next one (ZCalm.cs)
            }
            if (s.giveUp || s.arrive || s.reissue || s.hasRetarget) return;                  // an action is already queued
            if (BridgeParked(o)) { s.lastProgress = now; s.stillX = double.NaN; s.rebase = true; return; }   // waiting for its bridge turn: it comes (ZBridges.cs)
            if (Watchdog(o)) return;

            // orderly waits never count as stuck
            if (o.AttackObject != null || o.PathPending || o.PathInBuildingStage || o.PathWaitingForRepair) { s.lastProgress = now; return; }
            if (FollowsSquad(o)) { s.lastProgress = now; s.best = double.MaxValue; return; }   // DV44: walks with its leader (ZCommit.Follow.cs): no detour / re-path of its own
            if (SoftGoalHolding(o)) { s.lastProgress = now; return; }                       // pathing v3 DV47: waits at debris short of its goal (ZPathV3.cs)
            if (s.wait == ZPathWait.Lane && s.laneWaitSince < 0) s.laneWaitSince = now;         // reset only by progress (below) or a new order
            if (s.wait == ZPathWait.Lane && s.heldTicks >= 50 && now >= s.nextKeepBackOut && ClearLaneMouth(o)) return;

            // progress = the rest of the path got shorter; a new plan (goal moved > 1 tile, re-path, detour) restarts the
            // measure without counting as progress
            double rem = o.PathRemaining();
            if (s.rebase) { s.rebase = false; s.best = rem; s.lastProgress = now; return; }
            var (gx, gy) = o.PathFinalPoint;
            if (s.goalX == int.MinValue || Math.Abs((long)gx - s.goalX) + Math.Abs((long)gy - s.goalY) > 16) { s.goalX = gx; s.goalY = gy; s.best = rem; s.lastProgress = now; return; }
            if (rem < s.best - ProgressEps)
            {
                if (s.wait != ZPathWait.Lane) s.laneWaitSince = -1;
                if (s.level > 0 && s.best != double.MaxValue && s.best - rem > 32) s.level = 0;   // real progress again
                s.best = rem; s.lastProgress = now; return;
            }
            double window = Math.Max(StuckWindow, 16.0 / Math.Max(1.0, o.RealMoveSpeed));    // slow units (Mobile Missile 6 px/s) get 16 px worth of time
            if (now - s.lastProgress < window) return;
            // waiting for other units (queue behind them, a lane, jostling in a crowd) is not stuck: next to the team at the
            // goal it means arrived, elsewhere it waits its turn (TrafficPatience); only then, or without traffic, escalate
            bool helper = wp.refId == RefStepAside || wp.refId == RefBackOff, regroup = wp.refId == RefRegroup;
            if (!helper && TrafficWaitOk(o))
            {
                if (wp.mode == WpMode.Move && ArrivedByStall(o)) { s.arrive = true; Log(o, "waiting next to its team at the goal: arrived"); }
                return;
            }
            // held behind units that do not move (or no progress at all): near the goal that is arrival, else go round them once
            if (!helper && wp.mode == WpMode.Move && ArrivedByStall(o)) { s.arrive = true; Log(o, "blocked next to its team at the goal: arrived"); return; }
            if (!helper && now >= s.nextDetour && SearchBudgetLeftFor(o) && o.PathMode != WpMode.Agro && o.PathMode != WpMode.Dodge && o.PathMode != WpMode.ForceMove && !o.PathInBuildingStage)   // DV44: within the tick's search budget
            {
                s.nextDetour = now + 2.0;                                                     // standing units change: a new way round every 2 s
                if (Detour(o)) { s.rebase = true; return; }
            }
            // blocked by units but no way round them right now: wait for them (no give-up for traffic) up to TrafficPatience
            var hbp = s.heldBy;                                                               // parked in the way and nothing moved it, no way round:
            bool parkedBlock = hbp != null && Separable(hbp) && !hbp.Stunned && (Static(hbp) || hbp.Waypoints.Count == 0 || BridgeParked(hbp));   // no point waiting; electrobot_v1 DV45: a stunned unit moves again
            if (!helper && !regroup && !parkedBlock && InTraffic(o) && now - s.trafficSince < DeadJamPatience) return;   // a dead jam: detours meanwhile, then escalate
                                                                                              // (a regroup that finds no way round is dropped)
            Escalate(o, wp);
        }

        /// <summary>A unit of a group order that is gridlocked by its own team (held without a live queue ahead) within 12 tiles of
        /// the clicked point stops there: the group has arrived around the point; fighting for the exact own spot would
        /// only jam it further.</summary>
        bool GroupArrival(ZObject o)
        {
            var s = o.PS;
            if (s.groupX == int.MinValue || o.Waypoints.Count == 0) return false;
            var wp = o.Waypoints[0];
            if (wp.mode != WpMode.Move || wp.x != s.groupSlotX || wp.y != s.groupSlotY) return false;   // (a minion rejoining its leader gets close instead)
            double dg = Math.Sqrt((CX(o) - s.groupX) * (CX(o) - s.groupX) + (CY(o) - s.groupY) * (CY(o) - s.groupY));
            if (dg > 12 * 16 || !InTraffic(o) || W.Time - s.trafficSince < 2) return false;
            var hb = s.heldBy;
            if (hb == null || hb.Owner != o.Owner) return false;
            var (gx, gy) = o.PathFinalPoint;
            double rem = o.PathRemaining();
            if (rem > 12 * 16) return false;                                                 // near in a straight line only (the far bank of a river)
            return !ParkingInLane(o, gx, gy, rem);
        }

        /// <summary>Stopping here would park the unit in a lane zone (bridge deck, gap, or their approach) short of a goal beyond it.</summary>
        bool ParkingInLane(ZObject o, double gx, double gy, double da)
        {
            if (da <= 40 && !StrictLaneParkingFor(o)) return false;                                 // the goal itself is here (DV44: not from inside a lane zone the goal is not in, ZCommit.Settle.cs)
            double lx = o.LocX + o.XOver, ly = o.LocY + o.YOver;
            foreach (var L in LanesAt(o))
                if (lx + 1 < L.KX1 && L.KX0 < lx + o.WidthPix - 1 && ly + 1 < L.KY1 && L.KY0 < ly + o.HeightPix - 1 &&
                    !(gx >= L.KX0 && gx < L.KX1 && gy >= L.KY0 && gy < L.KY1))
                    if (da > 40 || L.Bridge) return true;                                     // deck, approach or mouth: the way in / out stays clear (DV44 strict: bridge decks / approaches)
            return false;
        }

        /// <summary>Hard safety net under all the rules above: a unit with an order that has not moved 2 px for WatchdogTime (not
        /// fighting, not at the repair station, not in Zod's scripted building moves, not waiting in an orderly queue or lane)
        /// drops the layer's own helper moves and its current order. Nothing stays frozen with an order.</summary>
        bool Watchdog(ZObject o)
        {
            var s = o.PS; double now = W.Time;
            if (double.IsNaN(s.stillX) || Math.Abs(CX(o) - s.stillX) + Math.Abs(CY(o) - s.stillY) >= 2) { s.stillX = CX(o); s.stillY = CY(o); s.stillSince = now; return false; }
            if (o.AttackObject != null || o.PathWaitingForRepair || o.PathInBuildingStage) { s.stillSince = now; return false; }
            if (TrafficWaitOk(o)) { s.stillSince = now; return false; }                     // orderly queue / lane turn: not frozen
            if (now - s.stillSince < WatchdogTime) return false;
            s.stillSince = now; s.Watchdogs++;
            while (o.Waypoints.Count > 1 && o.Waypoints[0].refId <= RefStepAside && o.Waypoints[0].refId >= RefRegroup) o.Waypoints.RemoveAt(0);
            if (o.HumanDirectOrder || CalmKeepsMove(o)) { s.reissue = true; s.nextShove = now + 3; s.ghostUntil = now + 1.0; Log(o, "watchdog on a direct player order: re-path + pass through 1 s (DV16)"); return true; }   // b49: his move too while it is far from its goal (ZCalm.cs)
            s.giveUp = true; s.nextShove = now + 3;
            if (o.Waypoints.Count > 0) s.Remember(o.Waypoints[0].refId, now + UnreachableMemory);
            s.lastReason = $"watchdog: no movement for {WatchdogTime} s";
            Log(o, s.lastReason);
            return true;
        }

        /// <summary>
        /// that never makes way (a cannon, an empty vehicle; not a stunned one): waiting for it is pointless, so the stuck guard looks for
        /// the way round at once (detour, then its escalation) instead of the 2 s queue patience.</summary>
        bool ParkedHolder(ZObject o)
        {
            var hb = o.PS.heldBy;
            return o.IsRobot && o.PS.wait == ZPathWait.Held && hb != null && Separable(hb) && !hb.Stunned && hb.Kind != ZKind.Robot && (Static(hb) || IdleHull(hb));   // robots only: vehicles keep their tuned traffic patience
        }

        /// <summary>DV66: a crewed vehicle standing without an order (a captured start vehicle, a hull left on a gate): it does not make way for
        /// robots either, so robots plan round it and do not queue behind it.</summary>
        internal static bool IdleHull(ZObject u) => u.Kind == ZKind.Vehicle && u.Waypoints.Count == 0 && !u.Destroyed && u.KillAt < 0;

        /// <summary>Gated by other units right now (or within the last second): queueing, lane, jostling, being pushed.</summary>
        bool InTraffic(ZObject o) => o.PS.trafficSince >= 0 && W.Time - o.PS.lastTraffic < 1.0;

        /// <summary>Waiting for traffic that will clear: behind a queue that moves (or a lane turn) for up to TrafficPatience,
        /// behind a unit that does not move for DeadQueuePatience (the step-aside or the deadlock back-off act meanwhile).</summary>
        bool TrafficWaitOk(ZObject o)
        {
            var s = o.PS;
            if (!InTraffic(o)) return false;
            if (ZWorld.ExitLanesDeviation && ParkedHolder(o)) return false;                // DV66 review round 1: a parked hull never clears: go round it now
            double waited = W.Time - s.trafficSince;
            if (s.wait == ZPathWait.Lane || s.laneWaitSince >= 0) return waited < LaneWaitMax;
            if (ChainHasRoot(o) || CrowdFlowing(o)) return waited < TrafficPatience;
            return waited < DeadQueuePatience;
        }

        /// <summary>A unit of the same team within 10 tiles is walking and got nearer its goal in the last second: the jam around this unit
        /// is moving (a funnel into a bridge drains one by one), so waiting is orderly.</summary>
        bool CrowdFlowing(ZObject o)
        {
            var hb = o.PS.heldBy;
            if (hb != null && Separable(hb) && (Static(hb) || hb.Waypoints.Count == 0 || BridgeParked(hb))) return false;   // parked in the way: not a queue
            double tx = o.PathTargetX - CX(o), ty = o.PathTargetY - CY(o);
            foreach (var b in units)                                                          // (a blob draining through a bridge is ~10 tiles deep)
            {
                if (b == o || !Separable(b) || !Moving(b) || b.PathHeld || b.Owner != o.Owner) continue;
                double dx = CX(b) - CX(o), dy = CY(b) - CY(o);
                if (dx * tx + dy * ty <= 0) continue;                                         // only the part of the crowd ahead of us counts
                if (dx * dx + dy * dy <= 160 * 160 && W.Time - b.PS.lastProgress < 1.0 && b.PS.wait != ZPathWait.Held) return true;
            }
            return false;
        }

        void Escalate(ZObject o, ZWaypoint wp)
        {
            var s = o.PS; double now = W.Time;
            s.lastProgress = now; s.rebase = true;
            if (wp.mode == WpMode.ForceMove && ZWorld.ExitLanesDeviation && !o.PathInBuildingStage && W.ExitBlocked(o, wp.x, wp.y) && W.FreeExitCell(o, o.CenterX, o.CenterY, wp.x, wp.y, out int efx, out int efy, true) &&
                Math.Abs(efx - wp.x) + Math.Abs(efy - wp.y) > 4)
            { s.hasRetarget = true; s.retargetX = efx; s.retargetY = efy; W.ExitStuckRedirects++; Log(o, $"DV66: exit move blocked by a parked unit: to the free cell ({efx},{efy})"); return; }
            if (CalmWedgeEnds(o, wp)) return;                                                // b49: not a fourth time for the same walk back to its leader (ZCalm.cs)
            if (ZWorld.ExitLanesDeviation && wp.mode != WpMode.ForceMove && wp.refId != RefBackOff && wp.refId != RefStepAside && o.IsRobot && ParkedHolder(o) && !o.PathInBuildingStage &&
                W.NearestFreeCell(o, 3, out int wfx, out int wfy))
            {   // DV66 review round 1: wedged against a parked hull with no way round (a door, a wall): step out to the nearest free cell
                // (squeezing past the hull's corner for that step), then the order resumes; a minion's regroup the same
                double d = Math.Sqrt((wfx - o.CenterX) * (double)(wfx - o.CenterX) + (wfy - o.CenterY) * (double)(wfy - o.CenterY));
                s.hasBackOff = true; s.backOffX = wfx; s.backOffY = wfy; s.squeezeUntil = now + 1.5 * d / Math.Max(1.0, o.RealMoveSpeed) + 0.5; W.WedgeEscapes++;
                Log(o, $"DV66: wedged against parked {s.heldBy.Type}#{s.heldBy.Id}: steps out to the free cell ({wfx},{wfy})"); return;
            }
            if (wp.mode == WpMode.ForceMove) { s.ghostUntil = now + 1.0; Log(o, "stuck on FORCE_MOVE: pass through units 1 s"); return; }   // leaving a building: never cancelled
            bool straight = wp.mode == WpMode.Agro || wp.mode == WpMode.Dodge;                 // Zod moves these in a straight line
            bool helper = wp.refId == RefStepAside || wp.refId == RefBackOff || wp.refId == RefRegroup;
            if (s.laneWaitSince >= 0 && now - s.laneWaitSince < LaneWaitMax && !helper) return;   // lane turn still coming
            if (wp.mode == WpMode.Move && !helper && ArrivedByStall(o))
            { s.arrive = true; Log(o, "no progress next to its team at the goal: arrived"); return; }
            if (helper && ReplanHelper(o, wp)) return;                                      // A/B round 2 fix 32 (v3 only): re-planned once first (ZPathV3.Owner.cs)
            if (helper) { s.level = 3; s.giveUp = true; s.nextShove = now + 3; Log(o, "helper move stuck: dropped"); return; }   // step-aside / back-off: never escalate; no new request for 3 s
            s.level++;
            s.lastReason = $"stuck: wait {s.wait}, held by {(s.heldBy != null ? s.heldBy.Type + "#" + s.heldBy.Id + " " + s.heldBy.PathWait + (s.heldBy.Waypoints.Count == 0 ? " idle" : "") : "-")}, level {s.level}";
            if (Trace) Log(o, $"escalate: traffic {InTraffic(o)} waited {(s.trafficSince >= 0 ? now - s.trafficSince : -1):F1} root {ChainHasRoot(o)} flowing {CrowdFlowing(o)} lane {s.laneWaitSince:F1}");
            Log(o, s.lastReason);
            if (s.level == 1 && !straight) { s.reissue = true; s.Repaths++; return; }
            if (s.level <= 2 && wp.mode == WpMode.Move && !straight)
            {
                s.level = 2;
                bool nr = NoRocks(o); int c = Reach.UnitComp(o, nr);
                if (c >= 0 && NearestReachableFor(o, wp.x, wp.y, o.IsRobot, nr, c, out int rx, out int ry) &&   // DV44: an intermediate waypoint is passed through (ZCommit.Settle.cs)
                    Math.Abs(rx - o.PathTargetX) + Math.Abs(ry - o.PathTargetY) > 8 &&
                    Math.Abs(rx - o.CenterX) + Math.Abs(ry - o.CenterY) > 8)
                {
                    s.hasRetarget = true; s.retargetX = rx; s.retargetY = ry; s.Resolves++;
                    return;
                }
            }
            var lane = LaneAt(o.CenterX, o.CenterY);
            var (fx, fy) = o.PathFinalPoint;
            if (lane != null && LaneAt(fx, fy) != lane && !o.IsMinion && BackOutOfLane(o, lane))
            { s.level = 2; return; }                                                          // never park on a bridge / in a gap: back out, then retry
            if (o.HumanDirectOrder || CalmKeepsMove(o)) { s.level = 1; s.reissue = true; s.Repaths++; Log(o, "stuck on a direct player order: re-path, never drop (DV16)"); return; }   // b49: his move too while it is far from its goal (ZCalm.cs)
            s.level = 3;
            s.giveUp = true;
            if (wp.mode != WpMode.Move && wp.mode != WpMode.Dodge) s.Remember(wp.refId, now + UnreachableMemory);
        }

        /// <summary>A MOVE that made no progress for StuckWindow counts as arrived when the unit is within ArriveRadius of its
        /// goal and blocked by / next to a unit of its team that stands or has the same order and is nearer the goal (the
        /// space around the goal is taken). Never inside a lane zone (bridge deck, gap) unless the goal lies in that zone.</summary>
        bool ArrivedByStall(ZObject o)
        {
            var (gx, gy) = o.PathFinalPoint;
            double da = o.PathRemaining();                                                    // along the path: no arriving through a wall
            if (GroupArrival(o)) return true;
            double radius = o.Waypoints.Count > 0 && o.Waypoints[0].refId == RefRegroup ? 32 : ArriveRadius;
            if (da > radius || ParkingInLane(o, gx, gy, da)) return false;
            var hb = o.PS.heldBy;
            if (hb != null && Separable(hb) && hb.Owner == o.Owner && (!Moving(hb) || hb.PathHeld)) return true;
            var wp = o.Waypoints[0];
            Near(o.CenterX, o.CenterY, MaxQuery, near2);
            foreach (var b in near2)
            {
                if (b == o || !Separable(b) || b.Owner != o.Owner) continue;
                double dx = CX(b) - CX(o), dy = CY(b) - CY(o);
                double lim = MinSpacing(o, b) + 10;
                if (dx * dx + dy * dy > lim * lim) continue;
                bool standing = !Moving(b) || b.PathHeld;
                bool sameOrder = b.Waypoints.Count > 0 && Math.Abs(b.Waypoints[0].x - wp.x) + Math.Abs(b.Waypoints[0].y - wp.y) <= 16;
                if (!standing && !sameOrder) continue;
                double db = Math.Sqrt((CX(b) - gx) * (CX(b) - gx) + (CY(b) - gy) * (CY(b) - gy));
                if (db <= da + 2) return true;
            }
            return false;
        }

        /// <summary>Following "held by" links reaches a unit that moves or waits in order (the queue will move).</summary>
        bool ChainHasRoot(ZObject o)
        {
            if (o.PS.wait == ZPathWait.Lane) return !LaneLocked(o);
            var x = o.PS.heldBy;
            for (int hop = 0; hop < 12; hop++)
            {
                if (x == null || !Separable(x)) return true;                                 // blocker gone: free next tick
                if (x == o) return false;                                                    // cycle
                var s = x.PS;
                if (x.Stunned) return true;                                                  // electrobot_v1 DV45: it moves / makes way again within 15 s
                if (Static(x)) return false;
                if (x.AttackObject != null || x.PathWaitingForRepair || x.PathInBuildingStage) return true;
                if (s.wait == ZPathWait.Bridge) return !BridgeParked(x);                    // waiting for a bridge turn: parked (go round it)
                if (s.wait == ZPathWait.Lane) return (s.laneWaitSince < 0 || W.Time - s.laneWaitSince < LaneWaitMax) && !LaneLocked(x);
                if (s.wait == ZPathWait.Held) { x = s.heldBy; continue; }
                if (Moving(x)) return W.Time - s.lastProgress < 2 * StuckWindow;                              // moving and getting somewhere
                return x.Waypoints.Count > 0 && !x.PS.giveUp;                                  // about to move (stepping aside, new order); idle = dead end
            }
            return false;
        }

        /// <summary>A unit waiting for its lane turn whose lane blocker is itself held by it (the unit coming out of the lane
        /// behind such a pair for 27 s, all counted as orderly).</summary>
        static bool LaneLocked(ZObject o)
        {
            var b = o.PS.heldBy;
            return b != null && b.PS.heldBy == o && (b.PS.wait == ZPathWait.Held || b.PS.wait == ZPathWait.Lane);
        }

        /// <summary>Debug: the "held by" chain of a unit, with each link's state.</summary>
        public string DescribeChain(ZObject o)
        {
            var sb = new System.Text.StringBuilder();
            var x = o;
            for (int hop = 0; hop < 8 && x != null; hop++)
            {
                var s = x.PS;
                sb.Append($"{x.Type}#{x.Id}[{s.wait} mv={x.IsMoving} wps={x.Waypoints.Count}{(x.Waypoints.Count > 0 ? " " + x.Waypoints[0].mode + " ref" + x.Waypoints[0].refId : "")} atk={(x.AttackObject != null)} pend={x.PathPending} held={s.heldTicks}] -> ");
                if (s.wait != ZPathWait.Held && s.wait != ZPathWait.Lane) break;
                x = s.heldBy; if (x == o) { sb.Append("LOOP"); break; }
            }
            return sb.ToString();
        }

        /// <summary>Units waiting (held, queued, sidestepping, lane) without progress for DeadlockDelay whose "held by" chain
        /// loops back: the lowest-priority unit of the loop yields for 2 s (it blocks nobody and takes the whole separation
        /// push), so the others walk on and shove it aside at push speed; nobody drives through anybody.</summary>
        void ResolveDeadlocks()
        {
            double now = W.Time;
            foreach (var o in units)
            {
                var s = o.PS;
                if (!Gated(o) || now - s.lastProgress < DeadlockDelay || now < s.yieldUntil) continue;
                var loop = Cycle(o);
                if (loop == null) continue;
                ZObject lowest = loop[0];
                foreach (var u in loop) if (Beats(lowest, u)) lowest = u;
                if (now < lowest.PS.yieldUntil) continue;
                // the lowest-priority unit yields; when it has no room to step aside or back up, the next one up tries, and so on
                // member, walled in, was ever asked)
                var order = new List<ZObject>(loop);
                order.Sort((x, y) => x == y ? 0 : Beats(x, y) ? 1 : -1);                      // lowest priority first (a total order)
                bool done = false;
                foreach (var loser in order)
                {
                    if (loser != lowest && now < loser.PS.yieldUntil) continue;
                    var winner = loop[(loop.IndexOf(loser) + loop.Count - 1) % loop.Count];   // the one held by the loser
                    double hl = Math.Sqrt(loser.Dx * loser.Dx + loser.Dy * loser.Dy) * Math.Sqrt(winner.Dx * winner.Dx + winner.Dy * winner.Dy);
                    bool headOn = hl > 1e-9 && loser.Dx * winner.Dx + loser.Dy * winner.Dy < -0.5 * hl;
                    loser.PS.yieldUntil = now + 2.0; loser.PS.Yields++;
                    Log(loser, $"waiting loop of {loop.Count} ({string.Join(" > ", loop.ConvertAll(u => u.Type + "#" + u.Id))}): {(headOn ? "steps aside" : "backs up")}");
                    done = headOn ? StepAsideFor(winner, loser) || BackUp(loser, winner) || StepAsideFor(loser, winner)
                                  : BackUp(loser, winner) || StepAsideFor(winner, loser);        // merging at an entrance: the loser reverses
                    if (done) break;
                    Log(loser, "no room to step aside or back up: the next unit of the loop tries");
                }
                if (!done && !GhostThroughLoop(loop)) Log(lowest, "nobody in the loop has room to step aside or back up: waits (the guard takes over)");
            }
        }

        /// <summary>Zod clears a squad's orders the moment its leader finishes a MOVE, so minions queued behind (at a bridge, in a
        /// crowd) stop wherever they are, far from their leader (zobject.cpp:4344-4356, research 6.3). When the leader's MOVE
        /// ended near its goal (not a Stop order), a minion more than 2 tiles away walks on to its leader and stops next to it.</summary>
        void FollowThrough(ZObject m)
        {
            var l = m.Leader;
            if (l == null || !Separable(l) || l.Waypoints.Count > 0 || !l.PS.moveDoneArrived || W.Time - l.PS.moveDoneAt > 0.05) return;
            if (FollowThroughCommit(m, l)) return;                                           // DV44: stays with its squad, or to a free spot outside lanes (ZCommit.Follow.cs)
            double dx = CX(m) - CX(l), dy = CY(m) - CY(l), d = Math.Sqrt(dx * dx + dy * dy);
            if (d <= 32) return;
            bool nr = NoRocks(l); int c = Reach.UnitComp(l, nr);
            if (c < 0 || Reach.UnitComp(m, nr) != c) return;
            if (!FreeSpotNear(m, l, nr, c, out int tx, out int ty)) return;
            m.Waypoints.Add(ZWaypoint.Move(tx, ty, false, RefRegroup));
            m.PS.regroupLeaderX = l.CenterX; m.PS.regroupLeaderY = l.CenterY; m.PS.regroupDist = d; m.PS.regroupRetry = W.Time + 20;
            m.PS.FollowThroughs++;
            Log(m, $"leader #{l.Id} arrived: follows through to ({tx},{ty})");
        }

        /// <summary>Nearest free spot next to a squad leader for one of its minions: rings 14..63 px round the leader, the side
        /// the minion comes from first, reachable, clear of every other unit and of lane zones.</summary>
        bool FreeSpotNear(ZObject m, ZObject l, bool nr, int c, out int x, out int y)
        {
            x = y = 0;
            double a0 = Math.Atan2(CY(m) - CY(l), CX(m) - CX(l));
            for (int r = SquadSpacing; r <= 63; r += 7)
                for (int k = 0; k < 12; k++)
                {
                    double a = a0 + ((k + 1) / 2) * (Math.PI / 6) * ((k & 1) == 0 ? 1 : -1);
                    int px = (int)Math.Round(CX(l) + Math.Cos(a) * r), py = (int)Math.Round(CY(l) + Math.Sin(a) * r);
                    if (!Reach.PointOk(px, py, true, nr, c) || Occupied(px, py, m, null) || KeepClearAt(px, py) != null) continue;
                    x = px; y = py; return true;
                }
            return false;
        }

        /// <summary>Last resort in a waiting loop: the loser reverses one spacing straight away from the unit it holds up.</summary>
        bool BackUp(ZObject o, ZObject from)
        {
            if (!o.CanOverwriteWP() || o.Waypoints.Count == 0) return false;
            double hx = CX(o) - CX(from), hy = CY(o) - CY(from), l = Math.Sqrt(hx * hx + hy * hy);
            if (l < 1e-6) return false;
            bool nr = NoRocks(o); int c = Reach.UnitComp(o, nr); if (c < 0) return false;
            foreach (double k in new[] { 1.0, 1.5, 2.0 })
            {
                double d = (GateSpacing(o, from) + 4) * k;
                int x = (int)Math.Round(CX(o) + hx / l * d), y = (int)Math.Round(CY(o) + hy / l * d);
                if (!Reach.PointOk(x, y, o.IsRobot, nr, c) || Occupied(x, y, o, from) || WayBlocked(o, x, y, from) || !HelperLegOk(o, x, y)) continue;
                o.PS.hasBackOff = true; o.PS.backOffX = x; o.PS.backOffY = y;
                Log(o, $"backs up for {from.Type}#{from.Id} to ({x},{y})");
                return true;
            }
            return false;
        }

        static bool Gated(ZObject o) { var w = o.PS.wait; return w == ZPathWait.Held || w == ZPathWait.Lane || w == ZPathWait.Queue || w == ZPathWait.Sidestep; }

        List<ZObject> Cycle(ZObject o)
        {
            var path = new List<ZObject> { o };
            var x = o.PS.heldBy;
            for (int hop = 0; hop < 12 && x != null && Separable(x); hop++)
            {
                if (x == o) return path;
                if (path.Contains(x)) return null;                                           // loop not through o: its members handle it
                if (!Gated(x)) { var fl = FollowHold(x); if (fl == null) return null; path.Add(x); x = fl; continue; }   // DV44 (ZCommit.Follow.cs)
                path.Add(x); x = x.PS.heldBy;
            }
            return null;
        }

        int Priority(ZObject o)
        {
            int p = 0;
            if (Static(o)) p += 64;
            if (OnLaneMoving(o) || BridgeHolderNear(o)) p += 16;                             // (a unit with its bridge turn keeps right of way)
            if (o.PathMode == WpMode.ForceMove) p += 8;
            if (o.Kind == ZKind.Vehicle) p += 4;
            if (!o.IsMinion) p += 2;
            return p;
        }
        /// <summary>a keeps right of way over b.</summary>
        bool Beats(ZObject a, ZObject b) { int pa = Priority(a), pb = Priority(b); return pa != pb ? pa > pb : a.Id < b.Id; }

        /// <summary>A unit that cannot get on inside a lane zone reverses to the lane end it came from (a MOVE in front of its
        /// order, like a back-off), so the lane clears and the unit retries when it is its turn.</summary>
        bool BackOutOfLane(ZObject o, ZLane lane)
        {
            if (!o.CanOverwriteWP()) return false;
            bool nr = NoRocks(o); int c = Reach.UnitComp(o, nr);
            if (c < 0) return false;
            double vx = o.Dx, vy = o.Dy;
            double along = lane.Vertical ? vy : vx;
            bool backPlus = along < 0;                                                        // reverse: the end behind the unit
            double mid = lane.Vertical ? (lane.X0 + lane.X1) * 0.5 : (lane.Y0 + lane.Y1) * 0.5;
            for (int pass = 0; pass < 2; pass++, backPlus = !backPlus)
            {
                double edge = lane.Vertical ? (backPlus ? lane.ZY1 : lane.ZY0) : (backPlus ? lane.ZX1 : lane.ZX0);
                for (int step = 0; step < 4; step++)
                    foreach (int off in new[] { 0, 16, -16, 32, -32 })
                    {
                        double d = edge + (backPlus ? 1 : -1) * (Radius(o) + 8 + step * 16);
                        int x = (int)Math.Round(lane.Vertical ? mid + off : d), y = (int)Math.Round(lane.Vertical ? d : mid + off);
                        if (!Reach.PointOk(x, y, o.IsRobot, nr, c) || Occupied(x, y, o, null) || WayBlocked(o, x, y, null) || !HelperLegOk(o, x, y)) continue;
                        o.PS.hasBackOff = true; o.PS.backOffX = x; o.PS.backOffY = y;
                        Log(o, $"cannot get on inside {lane}: backs out to ({x},{y})");
                        return true;
                    }
            }
            return false;
        }

        /// <summary>A unit waiting for its lane turn inside the lane's keep-clear area (it got there while the lane was free, then
        /// traffic came the other way) stands in the mouth that traffic has to leave by: after 0.5 s it backs out past the
        /// keep-clear area (a MOVE in front of its order, like a back-off) and comes back on its turn. At most every 3 s.</summary>
        bool ClearLaneMouth(ZObject o)
        {
            if (!o.CanOverwriteWP() || o.Waypoints.Count == 0 || o.Waypoints[0].refId <= RefStepAside) return false;
            double cx = CX(o), cy = CY(o), lx = o.LocX + o.XOver, ly = o.LocY + o.YOver;
            foreach (var L in Lanes)
            {
                if (!InKeep(L, cx, cy) || BoxInCorridor(L, lx, ly, o.WidthPix, o.HeightPix)) continue;
                int d = EntryDir(L, o), opp = 1 - d;
                if (L.veh[opp] + L.rob[opp] == 0) continue;                                   // nobody coming out: just wait
                o.PS.nextKeepBackOut = W.Time + 3;
                bool nr = NoRocks(o); int c = Reach.UnitComp(o, nr);
                if (c < 0) return false;
                bool low = d == 0;                                                            // at the low end: back off towards lower coordinates
                double mid = L.Vertical ? (L.X0 + L.X1) * 0.5 : (L.Y0 + L.Y1) * 0.5;
                double edge = L.Vertical ? (low ? L.KY0 : L.KY1) : (low ? L.KX0 : L.KX1);
                for (int step = 0; step < 3; step++)
                    foreach (int off in new[] { 0, 16, -16, 32, -32, 48, -48 })
                    {
                        double dd = edge + (low ? -1 : 1) * (Radius(o) + 4 + step * 16);
                        int x = (int)Math.Round(L.Vertical ? mid + off : dd), y = (int)Math.Round(L.Vertical ? dd : mid + off);
                        if (!Reach.PointOk(x, y, o.IsRobot, nr, c) || Occupied(x, y, o, null) || KeepClearAt(x, y) != null) continue;
                        o.PS.hasBackOff = true; o.PS.backOffX = x; o.PS.backOffY = y;
                        Log(o, $"waits in the mouth of {L} against oncoming traffic: backs out to ({x},{y})");
                        return true;
                    }
                return false;
            }
            return false;
        }

        /// <summary>Every 0.5 s per squad: idle minions more than RegroupDistance from their idle leader walk back to it.</summary>
        void Regroup()
        {
            double now = W.Time;
            foreach (var l in units)
            {
                if (l.Kind != ZKind.Robot || l.IsMinion || l.Minions.Count == 0 || now < l.PS.nextRegroup) continue;
                l.PS.nextRegroup = now + 0.5;
                if (l.Waypoints.Count > 0) continue;
                bool nr = NoRocks(l); int c = Reach.UnitComp(l, nr);
                if (c < 0) continue;
                foreach (var m in l.Minions)
                {
                    if (m == null || !Separable(m) || m.Waypoints.Count > 0 || m.AttackObject != null) continue;
                    double dx = CX(m) - CX(l), dy = CY(m) - CY(l), d = Math.Sqrt(dx * dx + dy * dy);
                    if (d <= RegroupLimit(l) || NoTurnBack(m, l, d)) continue;                 // DV44: a minion in its V is regrouped, one just past it is not turned round (ZCommit.Follow.cs)
                    if (Reach.UnitComp(m, nr) != c) continue;                                 // not reachable from here: stays
                    bool sameSpot = m.PS.regroupLeaderX != int.MinValue && Math.Abs(m.PS.regroupLeaderX - l.CenterX) + Math.Abs(m.PS.regroupLeaderY - l.CenterY) <= 32;
                    if (sameSpot && d >= m.PS.regroupDist - 16 && d <= m.PS.regroupDist + 16 && (now < m.PS.regroupRetry || RegroupSettled(m))) continue;   // DV44: a few retries only while nothing changed (ZCommit.Settle.cs)   // retry after it got closer, was pushed
                                                                                      // further away (step-asides), or 20 s later (no loops)
                    m.PS.regroupLeaderX = l.CenterX; m.PS.regroupLeaderY = l.CenterY; m.PS.regroupDist = d; m.PS.regroupRetry = now + 20;
                    if (!FreeSpotNear(m, l, nr, c, out int tx, out int ty)) { Log(m, $"regroup: no free spot next to leader #{l.Id}"); continue; }
                    m.Waypoints.Add(ZWaypoint.Move(tx, ty, false, RefRegroup));
                    m.PS.Regroups++;
                    Log(m, $"regroup to leader #{l.Id} at ({tx},{ty})");
                }
            }
        }
    }
}
