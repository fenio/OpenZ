using System;
using System.Collections.Generic;

namespace OpenZ.Sim
{
    /// <summary>
    /// DV118, the shuttle's rear ramp as a rule state and the squad's way in and out at the back (called from ZObject.ShuttleStep,
    /// ZObject.ProcessEnterWP and ZApcCrew.cs; deterministic, no random draw, no frame time):
    ///  * The ramp: Shut / Opening / Open / Closing, ShuttleRamp01 linear in sim time (ZWorld.ShuttleRampOpenSec down,
    ///    ZWorld.ShuttleRampCloseSec up). It is wanted down while the ship stands (level) on the ground and an EXIT is pending, or figures are
    ///    still on it (ShuttleRampHoldUntil), or (no take-off waiting) an own robot ordered into it is within ZWorld.ShuttleRampCallM of
    ///    the ramp foot while it can take passengers.
    ///  * Boarding: each member walks to its own back slot (the leader to slot 0, the ramp foot; a minion to slot 1 + its place among
    ///    the leader's living minions), round the hull (the pathing radius ZWorld.ShuttleRadiusPx); squad mates in the file pass each
    ///    other freely (no sidestep, no queue, no push among them). The leader boards within ZWorld.ShuttleBoardPx of the foot (twice
    ///    that when someone standing on it holds him) once the ramp is fully open and nobody is still on it; with the ramp not open yet
    ///    he waits there (at most ShuttleRampOpenSec + 3 s); a minion at its slot stands. The whole squad then boards (the APC's rule) and the foot
    ///    schedule is set: member k's figure reaches the foot at ShuttleFootTimes[k] (walk at ZWorld.ShuttleWalkMps from where it
    ///    stood, at most ZWorld.ShuttleWalkCapM counted, ZWorld.ShuttleFileGapSec apart); the ramp stays down until the last one sat
    ///    (+ ZWorld.ShuttleRampInSec).
    ///  * EXIT: the order makes ShuttleExitPending and the ramp comes down; fully down, the squad is made standing in a line on the back
    ///    slots 1..n (the first out walks to the farthest, so nobody passes anybody and the foot stays clear), no waypoint; member k
    ///    steps off the foot at ShuttleFootTimes[k]; the ramp stays down until the last one is off.
    ///  * A move order waits (ShuttleTakeOffHeld) until the ramp is shut, nothing is pending and nobody is on it; a held take-off no
    ///    longer calls the ramp for a squad walking in, so it closes and the ship goes.
    /// </summary>
    public partial class ZObject
    {
        double shuttleRampT0, shuttleRampFrom;                    // the swing now: its start time and the ramp's value then
        double shuttleWaitSince = -1;                             // a leader waiting at the foot for the ramp (World.Time)
        int shuttleSpotX, shuttleSpotY, shuttleSpotFor = -1;      // this robot's back slot spot for its ENTER order on the shuttle (id)
        int shuttleStandFor = -1;                                 // a minion standing on its back slot of that shuttle (id)
        bool shuttleHeld;                                         // the pathing layer held its last step (traffic in the way)

        /// <summary>This robot walks in file to its back slot of the shuttle, or stands on it: its squad mates pass it freely (the pathing
        /// layer's pair rule), so nobody sidesteps, queues or pushes inside the file.</summary>
        internal bool ShuttleFileBound
        {
            get
            {
                if (Kind != ZKind.Robot) return false;
                if (Waypoints.Count > 0) { var wp = Waypoints[0]; return wp.mode == WpMode.Enter && shuttleSpotFor >= 0 && wp.refId == shuttleSpotFor; }
                if (shuttleStandFor < 0) return false;
                int dx = CenterX - shuttleSpotX, dy = CenterY - shuttleSpotY;
                if (dx * dx + dy * dy > ZWorld.ShuttleBoardPx * ZWorld.ShuttleBoardPx) return false;
                var t = World?.Get(shuttleStandFor);                                         // review S8: only while its leader's ENTER order on that ship still stands
                var lw = Leader != null && Leader.Waypoints.Count > 0 ? Leader.Waypoints[0] : default;
                return t != null && !t.ShuttleAirborne && Leader != null && !Leader.Destroyed && Leader.KillAt < 0 && Leader.Waypoints.Count > 0 && lw.mode == WpMode.Enter && lw.refId == shuttleStandFor;
            }
        }

        /// <summary>Every shuttle step, first: the ramp's swing.</summary>
        internal void ShuttleRampStep()
        {
            ShuttleAirExitTick();                                                          // K256f: an EXIT given in the air, the moment it stands on the ground (Sim/ZWorld.ShuttleEmpty.cs)
            var w = World;
            double now = w.Time;
            bool down = !Destroyed && ShuttleState == ZShuttleState.Landed && ShuttleLift <= 0 && now >= ShuttleLevelledAt;   // after a rough landing: once it stands level
            bool want = down && (ShuttleExitPending || now < ShuttleRampWantUntil || (!ShuttleTakeOffHeld && (ShuttleRampCalled() || ShuttleEmptyRampCalled())));   // K256f: an empty ship's ramp, for a squad ordered into it
            switch (ShuttleRampPhase)
            {
                case ZShuttleRamp.Shut:
                    if (want) ShuttleSwing(ZShuttleRamp.Opening, now);
                    break;
                case ZShuttleRamp.Opening:
                case ZShuttleRamp.Open:
                    if (!want) ShuttleSwing(ZShuttleRamp.Closing, now);
                    break;
                case ZShuttleRamp.Closing:
                    if (want) ShuttleSwing(ZShuttleRamp.Opening, now);
                    break;
            }
            if (ShuttleRampPhase == ZShuttleRamp.Opening)
            {
                ShuttleRamp01 = Math.Min(1.0, shuttleRampFrom + (now - shuttleRampT0) / ZWorld.ShuttleRampOpenSec);
                if (ShuttleRamp01 >= 1.0 - 1e-9) { ShuttleRamp01 = 1.0; ShuttleRampPhase = ZShuttleRamp.Open; }
            }
            else if (ShuttleRampPhase == ZShuttleRamp.Closing)
            {
                ShuttleRamp01 = Math.Max(0.0, shuttleRampFrom - (now - shuttleRampT0) / ZWorld.ShuttleRampCloseSec);
                if (ShuttleRamp01 <= 1e-9) { ShuttleRamp01 = 0.0; ShuttleRampPhase = ZShuttleRamp.Shut; ShuttleRampShuts++; }
            }
            if (ShuttleRampPhase == ZShuttleRamp.Open && ShuttleExitPending) w.ShuttleExitNow(this);
            else if (ShuttleExitPending && ShuttleExitEarly) w.ShuttleExitNow(this);              // K422: while it comes down, ShuttleExitLeadSec before it is down (Sim/ZWorld.ShuttleQuickRamp.cs)
        }

        void ShuttleSwing(ZShuttleRamp to, double now)
        {
            shuttleRampFrom = ShuttleRamp01; shuttleRampT0 = now;
            if (to == ZShuttleRamp.Opening) ShuttleRampOpens++;
            ShuttleRampPhase = to;
        }

        /// <summary>An own robot ordered into the ship is within ZWorld.ShuttleRampCallM of the ramp foot and could board now.</summary>
        bool ShuttleRampCalled()
        {
            if (Passengers.Count > 0 || !CanTakePassengers(Owner)) return false;
            bool foot = false; int fx = 0, fy = 0;
            double lim = ZWorld.ShuttleRampCallM * 8.0;
            foreach (var o in World.Objects)
            {
                if (!o.IsRobot || o.Owner != Owner || o.Destroyed || o.KillAt >= 0 || o.Waypoints.Count == 0) continue;
                var wp = o.Waypoints[0];
                if (wp.mode != WpMode.Enter || wp.refId != Id) continue;
                if (!foot) { (fx, fy) = ShuttleBackPoint(0); foot = true; }                // only once someone is on the way
                double dx = o.CenterX - fx, dy = o.CenterY - fy;
                if (dx * dx + dy * dy <= lim * lim) return true;
            }
            return false;
        }

        /// <summary>The ZObject.ProcessEnterWP isNew hook: where this robot walks for its ENTER order. Anything but the playable shuttle:
        /// the waypoint's own spot. The shuttle: this member's back slot (the order's spot becomes it, so every later re-plan of the
        /// pathing layer aims there too).</summary>
        internal (int x, int y) ShuttleEnterSpot(ZWaypoint wp)
        {
            var t = World?.Get(wp.refId);
            if (t == null || !t.IsShuttle || !IsRobot) return (wp.x, wp.y);
            int slot = t.ShuttleArrivalSlot(this);                                                   // K256e: the leader the foot, the minions in their arrival order (ZWorld.ShuttleGround.cs)
            var (sx, sy) = t.ShuttleBackPoint(slot);
            shuttleSpotX = sx; shuttleSpotY = sy; shuttleSpotFor = t.Id; shuttleWaitSince = -1; shuttleStandFor = -1;
            if (Waypoints.Count > 0 && Waypoints[0].mode == WpMode.Enter && Waypoints[0].refId == t.Id)
            {
                var w = Waypoints[0]; w.x = sx; w.y = sy; Waypoints[0] = w; lastWp = w;   // the same order, aimed at the slot (no new isNew)
            }
            return (sx, sy);
        }

        /// <summary>The ZObject.ProcessEnterWP step when the target is the shuttle (instead of Zod's "under the box" boarding): the walk
        /// to the back slot, the wait at the foot, the boarding there. Always handles the step (true).</summary>
        internal bool ShuttleEnterStep(ZObject t, double dt)
        {
            if (ShuttleEmptyAutoDrop(t)) return true;                                     // K256f: nobody walks into an empty shuttle by himself (Sim/ZWorld.ShuttleEmpty.cs)
            var w = World; var wp = Waypoints[0];
            if (shuttleSpotFor != t.Id) ShuttleEnterSpot(wp);
            double dx = CenterX - shuttleSpotX, dy = CenterY - shuttleSpotY, d2 = dx * dx + dy * dy;
            bool at = d2 <= ZWorld.ShuttleBoardPx * ZWorld.ShuttleBoardPx
                      || (d2 <= 4 * ZWorld.ShuttleBoardPx * ZWorld.ShuttleBoardPx && shuttleHeld);   // held there last tick by someone standing on the spot: as near as it gets
            if (at)
            {
                if (IsMinion) { KillWP(); shuttleStandFor = t.Id; shuttleSpotFor = -1; return true; }   // at its place in the file: it stands there
                StopMove();
                if (t.ShuttleRampPhase == ZShuttleRamp.Open && w.Time >= t.ShuttleRampHoldUntil)
                {   // the ramp is down and clear: the squad boards (BoardAsPassengers, this tick's side effects)
                    KillWP(); EnteredTargetId = t.Id; shuttleSpotFor = -1; shuttleWaitSince = -1; ZWorld.ShuttleBoardsAtFoot++;
                    return true;
                }
                if (shuttleWaitSince < 0) { shuttleWaitSince = w.Time; ZWorld.ShuttleRampWaits++; }
                else if (w.Time - shuttleWaitSince > ZWorld.ShuttleRampOpenSec + 3.0) { KillWP(); shuttleSpotFor = -1; shuttleWaitSince = -1; }   // it never came down: the order ends
                return true;
            }
            shuttleWaitSince = -1;
            if (CheckAttackTo(wp)) return true;
            if (!cur.gotPfResponse) return true;
            bool moved = ProcessMoveOrKillWP(dt);
            shuttleHeld = PS.wait == ZPathWait.Held || PS.wait == ZPathWait.Queue || PS.wait == ZPathWait.Sidestep;
            if (!moved) return true;
            if (!ReachedTarget()) return true;
            if (NextPathPoint()) return true;
            if (HumanDirectOrder && directRetries < 8)
            {   // the path ended short of the slot (a squad mate on the last tile): on to the slot itself
                directRetries++; SetTarget(shuttleSpotX, shuttleSpotY); StartPath();
                return true;
            }
            KillWP(); shuttleSpotFor = -1;
            return true;
        }
    }

    public sealed partial class ZWorld
    {
        /// <summary>ZApcCrew.BoardAsPassengers hook (the squad is boarding the shuttle, nothing changed yet): the foot schedule of the
        /// members that board (the leader and his living minions, in squad order) and the ramp's hold.</summary>
        internal void ShuttleBoardSchedule(ZObject r, ZObject v)
        {
            v.ShuttleFootTimes.Clear(); v.ShuttleFootIds.Clear();
            var (fx, fy) = v.ShuttleBackPoint(0);
            double prev = 0;
            var squad = new List<ZObject> { r };
            foreach (var m in r.Minions) if (m != null && m.KillAt < 0) squad.Add(m);
            for (int k = 0; k < squad.Count; k++)
            {
                var m = squad[k];
                double dx = m.CenterX - fx, dy = m.CenterY - fy, d = Math.Min(ShuttleWalkCapM, Math.Sqrt(dx * dx + dy * dy) / 8.0);
                double f = d / ShuttleWalkMps;
                if (k > 0) f = Math.Max(f, prev + ShuttleFileGapSec);
                prev = f;
                v.ShuttleFootTimes.Add(Time + f); v.ShuttleFootIds.Add(m.Id);
            }
            v.ShuttleRampHoldUntil = Time + prev + ShuttleRampInSec;
            ShuttleQuickUp(v, true, Time + prev);                                                       // K422: the ramp starts up once the last one is through the doorway (Sim/ZWorld.ShuttleQuickRamp.cs)
            v.ShuttleLastRampUse = ZShuttleRampUse.Boarded;
        }

        /// <summary>ZApcCrew.EjectPassengers hook: EXIT on the shuttle = the ramp comes down; the squad gets out when it is fully down.</summary>
        internal bool ShuttleExitOrder(ZObject v, Team team)
        {
            if (v == null || v.Owner != team || !v.ShuttleHasLeavers) return false;          // K256f2: the passengers, or a hero pilot (Sim/ZWorld.ShuttleHeroes.cs)
            if (v.ShuttleAirborne) return ShuttleAirExit(v);                              // K256f: in the air: it lands where it stops and lets them out there (Sim/ZWorld.ShuttleEmpty.cs)
            v.ShuttleExitPending = true;
            return true;
        }

        /// <summary>The ramp is fully down with an EXIT pending: the squad gets out now (PassengersOut: Created per robot, then Ejected).</summary>
        internal void ShuttleExitNow(ZObject v)
        {
            v.ShuttleExitPending = false;
            if (ShuttleHeroesOut(v)) return;                                              // K256f2: a hero pilot: everyone out, the ship empty again (Sim/ZWorld.ShuttleHeroes.cs)
            if (v.Passengers.Count == 0) return;
            PassengersOut(v, v.Owner);
            v.ShuttleLastRampUse = ZShuttleRampUse.Unloaded;
        }

        /// <summary>ZApcCrew.PassengersOut hook (after the hero members are made, before the Created events): every member stands on
        /// its own back slot, centred, no waypoint; the first out (squad order) on the farthest slot n, the last on slot 1, so the line
        /// fills from the back and the foot stays clear. The exit's foot schedule and the ramp's hold.</summary>
        internal void ShuttlePlaceCrew(ZObject v, List<ZObject> squad)
        {
            v.ShuttleFootTimes.Clear(); v.ShuttleFootIds.Clear();
            var unplaced = new HashSet<ZObject>(squad);
            int n = squad.Count;
            for (int k = 0; k < n; k++)
            {
                var m = squad[k];
                if (m == null) continue;
                var (sx, sy) = v.ShuttleBackSpot(n - k, true, unplaced, out bool fb);
                m.SetCords(sx - (m.WidthPix >> 1), sy - (m.HeightPix >> 1)); m.XOver = m.YOver = 0;
                m.Waypoints.Clear(); m.StopMove();
                unplaced.Remove(m);
                ShuttleExitPlaced++; if (fb) ShuttleExitFallbacks++;
                v.ShuttleFootTimes.Add(Time + k * ShuttleFileGapSec + ShuttleRampOutSec); v.ShuttleFootIds.Add(m.Id);
            }
            v.ShuttleRampHoldUntil = Time + Math.Max(0, n - 1) * ShuttleFileGapSec + ShuttleRampOutSec;
            ExitWalkShuttle(v, squad);                                                                       // DV132: each robot's getting-out time (Sim/ZObject.ExitWalk.cs)
            ShuttleQuickUp(v, false, v.ShuttleRampHoldUntil);                                          // K422: the ramp starts up as the last one steps off its tip (Sim/ZWorld.ShuttleQuickRamp.cs)
        }
    }
}
