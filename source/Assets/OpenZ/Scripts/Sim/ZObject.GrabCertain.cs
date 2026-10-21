using System;

namespace OpenZ.Sim
{
    /// <summary>
    /// OpenZ deviation DV84 "one unit takes it, for certain" (on top of DV38 one unit per target and DV55 the closest unit goes;
    /// human teams only, the CPU keeps Zod's idle grabs).
    /// <para>Zod: every idle robot within 220 px of an empty vehicle or of a flag that is not its team's walks to it by itself,
    /// each for itself (zobject.cpp:1603-1853; auto_grab_vehicle_distance / auto_grab_flag_distance, zsettings.cpp:277). OpenZ
    /// sends one unit per target (DV38) and only the closest idle one (DV55). Three holes made that "nobody":</para>
    /// <para>(1) The closest unit that sees the target but cannot get to it (shut in by parked hulls or guns, held in a crowd)
    /// tried, ended its try without a word (a crowd arrival or a give-up of a MOVE leaves no memory, ZStuckGuard.cs) and stood
    /// idle again; being the closest it kept every other unit waiting for it, for ever. Now a unit whose own auto-capture ended
    /// without the target taken, or whose way to it has not become <see cref="ZWorld.GrabProgressPx"/> px shorter for
    /// <see cref="ZWorld.GrabStallSeconds"/> s, gives the try up and leaves the target alone for
    /// <see cref="ZWorld.GrabFailMemory"/> s: it no longer counts as the closest and the next closest unit goes on its next
    /// half-second check. Progress is measured ALONG THE UNIT'S PATH, the way the stuck guard measures it (ZStuckGuard.cs
    /// Monitor): a unit on its way round a river or over a bridge gets nearer with every step although the straight line to the
    /// target grows; a new plan (a way round, a re-path) is measured from where it starts; the time a unit fights, waits for
    /// its path or waits its turn in traffic (a bridge turn, a lane, a queue that moves: the stuck guard's own tests) does not
    /// count.</para>
    /// <para>(2) A unit the player has ordered once counts as held (DV38) for the rest of its life, and a held unit took an empty
    /// vehicle only within 80 px (DV55), a flag within 220 px. A group that stopped beside an empty vehicle stood 80 to 200 px
    /// from it: nobody went. Held units now take an empty vehicle or gun within Zod's own 220 px, like a flag
    /// (<see cref="ZWorld.HeldVehicleReach"/>).</para>
    /// <para>(3) Only units without any order looked at all, so a flag or an empty vehicle beside the way of a moving group was
    /// passed. A squad leader or vehicle on the player's plain move (the attack-move, not a retreat ordered next to enemies) now
    /// looks every half second: a flag that is not ours, or (robots) an empty vehicle, within
    /// <see cref="ZWorld.CloseGrabPx"/> px (5 tiles) that it sees, can reach and nobody has claimed, gets Zod's own grab waypoint
    /// in FRONT of the order (as Zod puts an attack in front of an attack-move, zobject.cpp:3285-3339); the unit takes it and
    /// goes on with its order. At most ONE robot of a squad leaves for a vehicle per player order: the order itself carries the
    /// mark (<see cref="ZWaypoint.grabDone"/>, every leg of it), so a new leader does not start again and a squad that passes a
    /// row of empty vehicles arrives with everybody else. Guns are never taken out of a march (a robot in a gun stays there);
    /// idle and held units take them as before. An idle unit that is closer still goes instead. Direct orders (attack, enter,
    /// repair, fort) and retreats never turn off, and a hull that stands on a bridge or in its approach is never taken out of a
    /// march (the squad would stand behind the vehicle it has just manned); idle and held units take it as anywhere else.</para>
    /// Deterministic: sim time, object order, no random draw of its own. Switch: ZWorld.GrabCertainDeviation (the game turns
    /// it on at every start; headless default off).
    /// </summary>
    public partial class ZObject
    {
        int grabId = -1; double grabBest, grabIdle, grabSeen, grabWayNext;

        /// <summary>DV84: the target of this unit's own running auto-capture (-1 = none). Checks.</summary>
        public int GrabTargetId => grabId;

        static bool GrabMode(WpMode m) => m == WpMode.Move || m == WpMode.Enter || m == WpMode.PickupGrenades;

        /// <summary>DV84: this unit has just given itself the auto-capture waypoint on t.</summary>
        void GrabNote(ZObject t)
        {
            if (!ZWorld.GrabCertainDeviation || t == null) return;
            grabId = t.Id; grabBest = double.MaxValue; grabIdle = 0; grabSeen = World.Time;
        }

        /// <summary>DV84, CheckPassiveEngage on its half-second beat, before anything else: is the own auto-capture still running
        /// and getting nearer? Taken or gone = done. Its waypoint gone while the target is still free = failed. The rest of its
        /// path (ZObject.PathRemaining, the stuck guard's measure) not <see cref="ZWorld.GrabProgressPx"/> px shorter for
        /// <see cref="ZWorld.GrabStallSeconds"/> s = stalled: the waypoint is dropped. Not counted: the time the unit fights, is
        /// stunned, waits for its path or waits its turn in traffic (ZPathing.GrabWaitOk). A plan that is more than
        /// <see cref="ZWorld.GrabReplanPx"/> px longer than the best so far is a new plan (a way round): measured from there, the
        /// clock runs on. Failed or stalled: the target is left alone for <see cref="ZWorld.GrabFailMemory"/> s (the pathing
        /// layer's own unreachable memory, which the idle grab and the closest-unit test both ask).</summary>
        void GrabWatch(double time)
        {
            if (grabId < 0) return;
            if (!ZWorld.GrabCertainDeviation) { grabId = -1; return; }
            double step = time - grabSeen; grabSeen = time;
            var t = World.Get(grabId);
            bool open = t != null && !t.Destroyed && t.KillAt < 0 &&
                        (t.Type == "flag" ? t.Owner != Owner : t.Type == "grenades" ? t.Grenades > 0 && CanPickupGrenades() : t.CanBeEntered());
            if (!open) { grabId = -1; return; }
            int at = -1;
            for (int i = 0; i < Waypoints.Count; i++) if (Waypoints[i].refId == grabId && !Waypoints[i].playerGiven && GrabMode(Waypoints[i].mode)) { at = i; break; }
            if (at < 0) { GrabGiveUp(time); ZWorld.GrabFails++; return; }
            var front = Waypoints[0].mode;
            if (AttackObject != null || Stunned || PathPending || (at > 0 && (front == WpMode.Attack || front == WpMode.Agro || front == WpMode.Dodge)) ||
                (World.Pathing != null && World.Pathing.GrabWaitOk(this))) return;               // fighting, waiting for its path or for its turn: the clock waits (a step aside or back-off in front does not stop it)
            if (at == 0 && cur.gotPfResponse && lastWp.Equals(Waypoints[0]))                    // walking the grab waypoint itself on a known path (cur belongs to it)
            {
                double rem = PathRemaining();
                if (rem < grabBest - ZWorld.GrabProgressPx) { grabBest = rem; grabIdle = 0; return; }   // nearer along its way
                if (rem > grabBest + ZWorld.GrabReplanPx) grabBest = rem;                       // a new, longer plan (a way round): measured from here, no progress counted
            }
            grabIdle += step;
            if (grabIdle < Math.Max(ZWorld.GrabStallSeconds, 2.0 * ZWorld.GrabProgressPx / Math.Max(1.0, RealMoveSpeed))) return;   // (a very slow unit gets the time for twice the distance)
            if (at == 0) KillWP(); else { Waypoints.RemoveAt(at); UpdatedWaypoints = true; }      // (the squad copies the list in this tick's side-effect pass)
            GrabGiveUp(time); ZWorld.GrabStalls++;
        }

        void GrabGiveUp(double time)
        {
            PS.Remember(grabId, time + ZWorld.GrabFailMemory);
            grabId = -1;
        }

        /// <summary>DV84, ProcessMoveWP of a stoppable move, after the attack-move check: on the player's plain move a squad leader
        /// or a vehicle takes a flag, or (robots, once per order) an empty vehicle, right beside its way (true = the grab waypoint
        /// is now in front).</summary>
        bool GrabOnTheWay(ZWaypoint wp)
        {
            if (!ZWorld.GrabCertainDeviation || !ZWorld.GrabPolicyDeviation || IsMinion || !wp.playerGiven || !wp.attackTo || wp.mode != WpMode.Move) return false;
            double time = World.Time;
            if (time < grabWayNext) return false;
            grabWayNext = time + 0.5;
            if (!World.IsHumanTeam(Owner) || World.CalmStart(Owner) || (World.GameOver && ZWorld.EndFreezeDeviation)) return false;
            if (!CanAttack() || !CanMove || Stunned || HoldPosition || AttackObject != null) return false;
            ZObject best = null; double bd = double.MaxValue; bool bestEnter = false;
            foreach (var f in World.Flags)
            {
                if (f.Owner == Owner) continue;
                double d = Dist(CenterX, CenterY, f.CenterX, f.CenterY);
                if (d > ZWorld.CloseGrabPx || d >= bd || GrabClaimed(f) || PathUnreachable(f) || !CanSee(f) || CloserGrabber(f, false) || AutoFlagWayHeld()) continue;   // DV130: not while the squad is under fire
                best = f; bd = d; bestEnter = false;
            }
            if (IsRobot && !NoAutoEnter && !wp.grabDone)                                         // one robot per squad and order; vehicles only, never a gun
                foreach (var o in World.Objects)
                {
                    if (o.Kind != ZKind.Vehicle || o.Destroyed || o.KillAt >= 0 || !o.CanBeEntered() || o.AutoEnterLocked || o.Type == "apc") continue;
                    double d = Dist(CenterX, CenterY, o.CenterX, o.CenterY);
                    if (d <= ZWorld.CloseGrabPx && OnBridgeApproach(o)) continue;                // never out of a march over a bridge: the squad would stand behind the hull it has just manned
                    if (d > ZWorld.CloseGrabPx || d >= bd || GrabClaimed(o) || PathUnreachable(o) || TeammateEntering(o) || !CanSee(o) || CloserGrabber(o, true)) continue;
                    best = o; bd = d; bestEnter = true;
                }
            if (best == null) return false;
            if (bestEnter)
                for (int i = 0; i < Waypoints.Count; i++)                                        // the mark goes with the order (every leg of it): a new leader of this squad does not start again
                    if (Waypoints[i].playerGiven) { var q = Waypoints[i]; q.grabDone = true; Waypoints[i] = q; }
            Waypoints.Insert(0, new ZWaypoint { mode = bestEnter ? WpMode.Enter : WpMode.Move, refId = best.Id, x = best.CenterX, y = best.CenterY, attackTo = true });
            GrabNote(best);
            CloneMinionWayPoints();
            ZWorld.GrabsOnTheWay++;
            return true;
        }

        /// <summary>DV84: v stands on a bridge or in its approach (the keep-clear area of the deck's vehicle lane, ZLanes.cs).</summary>
        bool OnBridgeApproach(ZObject v)
        {
            var p = World.Pathing;
            if (p == null || !p.Enabled) return false;
            foreach (var L in p.Lanes)
                if (L.Bridge && L.VehicleLane && v.CenterX >= L.KX0 && v.CenterX < L.KX1 && v.CenterY >= L.KY0 && v.CenterY < L.KY1) return true;
            return false;
        }
    }

    public sealed partial class ZPathing
    {
        /// <summary>DV84: o waits its turn at this moment: its bridge turn, a lane, or a queue behind a unit that itself moves or
        /// waits in order. The stuck guard's own tests of an orderly wait (ZStuckGuard.cs Monitor, TrafficWaitOk), with their
        /// own patience. Standing behind a unit that does not move is NOT such a wait (the guard's 2 s for a shove are left
        /// out), nor is a unit nobody holds; a robot held by a gun or an empty hull never counts (ParkedHolder): those are the
        /// shut-in cases the rule is for.</summary>
        internal bool GrabWaitOk(ZObject o)
        {
            if (!Enabled) return false;
            var s = o.PS;
            if (BridgeParked(o) || s.wait == ZPathWait.Bridge) return true;                     // its bridge turn comes (ZBridges.cs)
            if (s.heldBy == null || !TrafficWaitOk(o)) return false;                             // behind nobody, or the guard's own patience is over
            return s.wait == ZPathWait.Lane || ChainHasRoot(o) || CrowdFlowing(o);               // a lane turn, a queue that moves
        }
    }

    public sealed partial class ZWorld
    {
        /// <summary>DV84 switch (see ZObject.GrabCertain.cs). The game turns it on at every start; headless default off.</summary>
        public static bool GrabCertainDeviation = false;
        /// <summary>DV84: the way of an auto-capture to its target (the rest of the unit's path) must get this many px shorter
        /// every <see cref="GrabStallSeconds"/> s; a plan more than <see cref="GrabReplanPx"/> px (a tile) longer than the best
        /// so far is a new plan.</summary>
        public const int GrabProgressPx = 8, GrabReplanPx = 16;
        public const double GrabStallSeconds = 4.0;
        /// <summary>DV84: seconds a unit leaves a target alone after its own auto-capture of it failed or stalled.</summary>
        public const double GrabFailMemory = 20.0;
        /// <summary>DV84: how far a held unit (DV38: one the player has ordered) takes an empty vehicle or gun: Zod's own
        /// auto_grab_vehicle_distance (220 px) with the rule on, DV55's 80 px without it.</summary>
        public int HeldVehicleReach => GrabCertainDeviation ? S.auto_grab_vehicle_distance : CloseGrabPx;
        /// <summary>DV84 statistics (checks): auto-captures that ended without the target taken, that stalled and were dropped,
        /// and grabs started beside the way of a move order.</summary>
        public static int GrabFails, GrabStalls, GrabsOnTheWay;
    }
}
