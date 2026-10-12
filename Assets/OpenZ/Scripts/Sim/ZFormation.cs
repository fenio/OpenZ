using System;

namespace OpenZ.Sim
{
    /// <summary>
    ///
    /// Zod has no follow offsets: CloneMinionWayPoints (zobject.cpp) hands every minion the leader's identical waypoints, each
    /// minion path-finds to the same point and the squad walks one behind the other (stacked in Zod, single file with the
    /// OpenZ spacing); only ScuffleUnits spreads it when idle. So the V is an OpenZ change, done in the sim, deterministically:
    ///  - while the squad leader walks a MOVE (player, bot, attack-move or rally order; the minion holds the same cloned
    ///    waypoint), each minion steers straight at its slot behind the leader instead of walking its own copy of the path:
    ///    member i (1..) sits in row r = (i + 1) / 2 on side (-1)^i: RowBack x r px behind the leader, SideOut x r px to the
    ///    side (grunt 3 = a V, tough 2 = one behind, pyro / laser 4 = a V of two rows; members 15.6+ px apart, above the 14 px
    ///    squad spacing of ZSeparation, so the gate never has to hold them);
    ///  - the V turns with the leader's heading, smoothed over ~0.1 s (HeadingBlend per tick, no trigonometry), so corners
    ///    swing it instead of snapping it;
    ///  - a slot must be free ground the robot can stand on in its own A* component, reachable in a straight line (the robot's
    ///    box swept along it, ZReach), outside bridge / lane keep-clear areas and clear of units outside the squad (the V never
    ///    steps into a vehicle's or another squad's way): else the V squeezes (half width, then single
    ///    file straight behind the leader), and where even that fails (a bridge deck and its approach, a 1-tile gap, a wall
    ///    between) the minion walks its own Zod path again (single file over the bridge) and re-opens the V after it;
    ///  - a minion in the V walks at its leader's pace (a leader on a road, a minion on the verge beside it) and, behind its slot,
    ///    hurries at up to CatchUp (1.5, below Zod's running 1.8) x that pace until it is back in the V (a turn, the V re-forming
    ///    after a bridge); the leader's speed is never changed;
    ///    the movement gate, pushes and spacing of the pathing layer apply unchanged; the stuck guard, crowd arrival and the
    ///    watchdog see the order's own goal as the rest of the minion's path (progress = the squad advancing), and a minion
    ///    more than MaxLag px behind its slot leaves the formation and walks its own path.
    /// The leader is never touched: its path, speed and arrival are Zod's; the minions stop with it (Zod clears their orders
    /// when the leader's MOVE ends) and stand in the V. Toggle ZWorld.FormationDeviation (static; PlayerPrefs oz_vformation_sim,
    /// off in 'Zod exact').
    /// </summary>
    public static class ZFormation
    {
        /// <summary>Row distance behind the leader and side offset per row (px; 16 px = 2 m).</summary>
        /// 12 / 10 px -> 16 / 13 px (a 2.6 m diagonal), x BodyScale of the squad's type.
        public const int RowBack = 16, SideOut = 13;
        /// <summary>Squad spacing / V offset scale per robot type: the v3 bodies are 15-20 % wider than the BS-01 grunt.</summary>
        public static double BodyScale(string type) => type == "tough" ? 1.2 : type == "psycho" || type == "pyro" || type == "laser" || type == ZElectro.Type ? 1.15 : 1.0;   // electrobot_v1 (a squad of 1: completeness)
        /// <summary>Heading smoothing per 10 ms tick (0..1).</summary>
        public const double HeadingBlend = 0.1;
        /// <summary>A minion further than this (px) from its slot walks its own path (lagging after a jam or a bridge).</summary>
        public const int MaxLag = 48;
        /// <summary>A minion in the V walks at its leader's pace (the leader may be on a road while the minion walks the verge beside
        /// it); more than CatchUpFrom px behind its slot it hurries at up to CatchUp x that pace (1 + (lag - 4) / CatchUpRamp, capped;
        /// Zod's own running is 1.8x, run_unit_speed).</summary>
        public const double CatchUp = 1.5, CatchUpFrom = 4, CatchUpRamp = 32;
        /// <summary>After leaving the formation a minion walks its own path for at least this long (s): no on / off flicker.</summary>
        public const double ReenterDelay = 1.0;
        /// <summary>Squeeze steps of the side offset tried in order: full V, half width, single file.</summary>
        public static readonly double[] Squeeze = { 1.0, 0.5, 0.0 };

        /// <summary>Slot offset of squad member i (1..) in leader space: (back, side) px.</summary>
        public static (int back, int side) Offset(int i, string type = null)
        {
            int r = (i + 1) / 2; double k = BodyScale(type);
            return ((int)Math.Round(RowBack * k) * r, ((i & 1) == 1 ? -1 : 1) * (int)Math.Round(SideOut * k) * r);
        }
    }

    public partial class ZObject
    {
        // DV24 state (minions: in formation / re-enter time / counters; leaders: smoothed heading)
        bool formOn; double formRetry = -1, formHx, formHy; bool formHeadingSet; System.Collections.Generic.List<(int x, int y)> formPts;
        /// <summary>DV24: this minion walks in its squad's V right now (view / checks).</summary>
        public bool InFormation => formOn;
        /// <summary>DV24 counters (checks): ticks in formation, squeezed ticks (half / single file), exits to the own path.</summary>
        public int FormTicks, FormSqueezedHalf, FormSqueezedFile, FormExits;
        /// <summary>DV24: the squeeze of the last formation tick (1 = full V, 0.5, 0 = single file).</summary>
        public double FormSqueeze { get; private set; } = 1;

        /// <summary>DV24 hook in ProcessMoveWP (stoppable MOVE, after attack-move): a minion whose leader walks the same MOVE steers
        /// at its V slot. Returns true when it handled this tick (moved or held in the V).</summary>
        bool FormationStep(double dt)
        {
            if (World.Pathing != null && World.Pathing.CommitOn && World.Pathing.CommitFollow)
            {                                                                                // pathing v3 DV44: V / single file on the leader's trail (ZCommit.Follow.cs)
                if (World.Pathing.FollowOn(this)) return FollowStep(dt);                     // (outside the bridge / bottleneck zones, ZPathV3.cs)
                if (FollowHandover()) return true;                                           // in a zone: its own Zod path, then the live DV24 step below
                if (PC.softPass) { formOn = false; return false; }                           // DV47: on its way over soft debris: its own (soft) path, not the V slot
            }
            if (!IsMinion) return false;
            bool want = ZWorld.FormationDeviation && World.Pathing != null && World.Pathing.Enabled && Kind == ZKind.Robot
                        && Leader != null && !Leader.Destroyed && Leader.KillAt < 0 && Leader.Waypoints.Count > 0 && Waypoints.Count > 0
                        && Waypoints[0].mode == WpMode.Move && Leader.Waypoints[0].Equals(Waypoints[0]) && World.Time >= formRetry;
            int sx = 0, sy = 0; double sq = 1;
            if (want && World.Pathing.CommitOn && World.Pathing.HandoverAhead(this)) want = false;   // pathing v3: never walked back past its own leader (ZPathV3.cs)
            if (want && !World.Pathing.FormationSlot(this, out sx, out sy, out sq)) want = false;
            if (want) FormSqueeze = sq;
            if (!want)
            {
                if (!formOn) return false;
                formOn = false; FormExits++; formRetry = World.Time + ZFormation.ReenterDelay;
                StopMove(); PathReissue();                                                   // its own Zod path from here, next tick
                return true;
            }
            formOn = true; FormTicks++;
            if (FormSqueeze < 0.99) { if (FormSqueeze > 0.01) FormSqueezedHalf++; else FormSqueezedFile++; }
            PS.stillX = double.NaN;                                                             // standing in the V while the leader waits is not frozen
            formPts ??= new System.Collections.Generic.List<(int x, int y)>(1);                // the rest of the "path" is the order's own goal: the stuck
            formPts.Clear(); formPts.Add((Waypoints[0].x, Waypoints[0].y));                     // guard measures progress to it and crowd arrival only
            cur.pfPoints = formPts; cur.pathFindingId = 0; cur.gotPfResponse = true;           // happens there (never at the slot); re-planned on exit
            double dx = sx - (PosX + (WidthPix >> 1)), dy = sy - (PosY + (HeightPix >> 1));
            if (dx * dx + dy * dy <= 2.25) { StopMove(); return true; }                          // on its slot: stand (the slot moves on with the leader)
            SetTarget(sx, sy); SetVelocity();
            double lag = Math.Sqrt(dx * dx + dy * dy), own = RealMoveSpeed;                     // keep the leader's pace (it may walk a road while this
            double pace = Math.Max(own, Leader.RealMoveSpeed);                                  // minion walks the verge beside it), plus a catch-up while
            double k = 1 + (lag > ZFormation.CatchUpFrom ? Math.Min(ZFormation.CatchUp - 1, (lag - ZFormation.CatchUpFrom) / ZFormation.CatchUpRamp) : 0);   // behind its slot
            if (World.Pathing != null && World.Pathing.CommitOn && World.Pathing.PaceCapped(this)) pace = Math.Min(pace * k, World.Pathing.FollowTopSpeed(this)) / k;   // pathing v3: never past its own Zod speed x catch-up (x 1 in water / on a slow tile, ZCommit.Follow.cs)
            if (own > 1e-6 && pace * k > own) { double f = pace * k / own; Dx = (float)(Dx * f); Dy = (float)(Dy * f); }
            if (!ProcessMove(dt, true, out _, out _))
            {                                                                                // a wall the sweep missed: own path
                formOn = false; FormExits++; formRetry = World.Time + ZFormation.ReenterDelay;
                StopMove(); PathReissue();
            }
            return true;
        }

        /// <summary>DV24: the leader's smoothed heading (unit vector); false before the squad has moved at all.</summary>
        internal bool FormationHeading(out double hx, out double hy)
        {
            double l = Math.Sqrt((double)Dx * Dx + (double)Dy * Dy);
            if (IsMoving && !PathHeld && l > 1e-6)
            {
                double vx = Dx / l, vy = Dy / l;
                if (!formHeadingSet) { formHx = vx; formHy = vy; formHeadingSet = true; }
                else if (World.Time != formHeadingTime)
                {
                    double bx = formHx + (vx - formHx) * ZFormation.HeadingBlend, by = formHy + (vy - formHy) * ZFormation.HeadingBlend, bl = Math.Sqrt(bx * bx + by * by);
                    if (bl > 1e-6) { formHx = bx / bl; formHy = by / bl; } else { formHx = vx; formHy = vy; }
                }
                formHeadingTime = World.Time;
            }
            hx = formHx; hy = formHy;
            return formHeadingSet;
        }
        double formHeadingTime = double.NaN;
    }

    public sealed partial class ZPathing
    {
        /// <summary>DV24: the V slot of minion m (px, centre) and the squeeze used; false = no valid slot (walk the own path).</summary>
        internal bool FormationSlot(ZObject m, out int sx, out int sy, out double squeeze)
        {
            sx = sy = 0; squeeze = 1;
            var l = m.Leader;
            if (!l.FormationHeading(out double hx, out double hy)) return false;
            if (KeepClearAt(m.CenterX, m.CenterY) != null || KeepClearAt(l.CenterX, l.CenterY) != null) return false;   // bridge / lane areas: single file on Zod's path
            int idx = l.Minions.IndexOf(m) + 1;
            if (idx <= 0) return false;
            bool nr = NoRocks(m); int c = Reach.UnitComp(m, nr);
            if (c < 0) return false;
            double lx = CX(l), ly = CY(l), mx = CX(m), my = CY(m);
            var (back, side) = ZFormation.Offset(idx, l.Type);
            foreach (double k in ZFormation.Squeeze)
            {
                double px = lx - hx * back + (-hy) * side * k, py = ly - hy * back + hx * side * k;
                int ix = (int)Math.Round(px), iy = (int)Math.Round(py);
                double ddx = ix - mx, ddy = iy - my;
                if (ddx * ddx + ddy * ddy > (double)ZFormation.MaxLag * ZFormation.MaxLag) return false;   // lagging: own path
                if (KeepClearAt(ix, iy) != null || !Reach.PointOk(ix, iy, true, nr, c) || FormationSlotTaken(m, ix, iy) || !FormationLineFree(m, ix, iy, nr)) continue;
                sx = ix; sy = iy; squeeze = k;
                return true;
            }
            return false;
        }

        /// <summary>A unit outside the squad (a vehicle, another squad) stands or walks within its spacing + 4 px of the slot: the V
        /// never steps into other traffic (it squeezes, or the minion keeps the leader's path).</summary>
        bool FormationSlotTaken(ZObject m, int x, int y)
        {
            Near(x, y, MaxQuery, near2);
            foreach (var o in near2)
            {
                if (o == m || o == m.Leader || SameSquad(m, o) || !Separable(o)) continue;
                double min = MinSpacing(m, o) + 4, dx = CX(o) - x, dy = CY(o) - y;
                if (dx * dx + dy * dy < min * min) return true;
            }
            return false;
        }

        /// <summary>The robot's box swept (4 px steps) from where it stands to (x, y) stays on free tiles of its grid.</summary>
        bool FormationLineFree(ZObject m, int x, int y, bool nr)
        {
            double cx = CX(m), cy = CY(m), dx = x - cx, dy = y - cy, len = Math.Sqrt(dx * dx + dy * dy);
            int steps = (int)(len / 4) + 1, half = m.WidthPix >> 1;
            for (int i = 1; i <= steps; i++)
            {
                double f = Math.Min(1.0, i * 4 / Math.Max(len, 1e-9));
                if (!Reach.BoxFree((int)Math.Floor(cx + dx * f) - half, (int)Math.Floor(cy + dy * f) - half, m.WidthPix, m.HeightPix, true, nr)) return false;
            }
            return true;
        }
    }

    public sealed partial class ZWorld
    {
        /// <summary>OpenZ deviation DV24 (squad V formation, ZFormation). OFF = Zod (minions walk the leader's path, single file).
        /// Static like DuelDeviation; ZGame sets it at every start (PlayerPrefs oz_vformation_sim, off in 'Zod exact').</summary>
        public static bool FormationDeviation = true;
    }
}
