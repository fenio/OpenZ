using System;
using System.Collections.Generic;

namespace OpenZ.Sim
{
    /// <summary>
    /// DV44 part 1 (PT1 / PT2): squads follow their leader's walked trail. Replaces the DV24 formation step while
    /// ZWorld.PathCommitDeviation is on (ZFormation.cs keeps the V offsets, the catch-up factor and the switch; off = DV24 as before).
    ///  - The leader records a breadcrumb trail of its current MOVE (a point every 2 px walked, the last TrailKeep px) and its
    ///    smoothed walking speed.
    ///  - V: minion i keeps its DV24 slot (RowBack x row behind, SideOut x row to its side) measured ALONG that trail: on a straight
    ///    walk this is DV24's V; at a corner the V bends round it like people walking round a corner instead of swinging. A slot
    ///    that is not free ground (a unit outside the squad in it, a wall) narrows that minion's side offset to half (at once; wider
    ///    again after WideTicks of room). The whole squad goes single file only when the leader's way ahead has no room for a
    ///    half-width V (a gap, a bridge, a lane) or a minion has had no slot at all for VBadTicks: each minion then walks the trail
    ///    FileGap behind the one before it (ranks by who is nearest the leader along the trail, then kept). The V re-opens after
    ///    FileMin s once every slot has fitted for VReturnTicks. Hysteresis and minimum commit times on every switch: no flicker.
    ///  - Pace (never the leader's speed, never a Zod rule changed): the minion's own Zod speed (tile, run, damage: RealMoveSpeed)
    ///    is the top; it keeps its place by matching its smoothed leader: DV24's catch-up (up to CatchUp x, on dry ground only;
    ///    in water or on any slow tile never faster than its own Zod speed) when behind its place, slower down to a stop when ahead
    ///    of it, a car-following limit behind the unit ahead in the file, and a stop / go hysteresis (it stops under StopSpeed and
    ///    walks again over GoSpeed): no bang-bang stop-go, and a unit never walks back to a place behind it.
    ///  - Steering: a point AimAhead px ahead of the minion's own projection on the trail (never behind it), moved into its V lane
    ///    near its place; round a corner the look-ahead shortens instead of cutting it.
    ///  - Followers make no A* of their own (PT2); their "path" is the trail ahead of them plus the leader's own path, so the lane,
    ///    bridge and arrival rules see where they really go.
    ///  - Bridge keep-clear areas keep DV24 / DV8's rule (each minion its own Zod path over the deck, the bridge queue serves it); a
    ///    minion that cannot see its steering point (a wall between), is more than JoinLimit px off the trail or MaxLagFollow px
    ///    behind its place leaves the follow for its own path (the DV44 path budget serves it) and re-joins after ReenterDelay.
    ///  - Arrival: Zod clears the minions' orders when the leader's MOVE ends. A minion in reach of its squad and outside any lane,
    ///    gap or bridge area stays where it stopped (no walk-in, no correction); one out of reach or standing in such an area walks
    ///    to a free spot next to its leader outside them (the layer's follow-through, ZStuckGuard.cs).
    /// </summary>
    public partial class ZObject
    {
        /// <summary>DV44 hook at the top of FormationStep (ProcessMoveWP, stoppable MOVE after attack-move).</summary>
        bool FollowStep(double dt)
        {
            var P = World.Pathing; var c = PC;
            if (!IsMinion)
            {                                                                            // the squad lost its leader and this unit leads now:
                if (!c.noPath) return false;                                             // it has no Zod path yet (it followed): plan one now
                c.noPath = false; c.followMode = 0; formOn = false; StopMove(); PathReissue();
                return true;
            }
            if (c.softPass && Waypoints.Count > 0)
            {                                                                            // pathing v3 DV47: on its way over soft debris - that path, not the trail
                formOn = false; c.noPath = false; c.ownPath = true; c.ownWp = Waypoints[0]; c.followMode = 3; c.standing = false;
                return false;
            }
            if (Waypoints.Count > 0 && Waypoints[0].refId == ZPathing.RefRegroup)
            {                                                                            // a regroup: its own path
                if (c.noPath) { c.noPath = false; formOn = false; StopMove(); PathReissue(); c.followMode = 6; return true; }
                formOn = false; c.followMode = 6; return false;
            }
            bool want = ZWorld.FormationDeviation && Kind == ZKind.Robot && Leader != null && !Leader.Destroyed && Leader.KillAt < 0 && Leader.Waypoints.Count > 0
                        && Waypoints.Count > 0 && Waypoints[0].mode == WpMode.Move && Leader.Waypoints[0].Equals(Waypoints[0]) && World.Time >= formRetry;
            if (want && c.ownPath && c.ownWp.Equals(Waypoints[0]) && !cur.gotPfResponse) want = false;     // its own path request is still on its way
            if (want && !formOn && !c.noPath && !P.RejoinOk(this)) want = false;                // pathing v3: after a zone, back on the trail only where it runs straight its way (ZPathV3.cs)
            int sx = 0, sy = 0, mode = 0; double v = 0;
            if (want && !P.FollowTarget(this, out sx, out sy, out mode, out v)) want = false;
            if (!want && Leader != null && Leader.Waypoints.Count == 0 && (formOn || c.noPath))
            { formOn = false; c.followMode = 0; StopMove(); return true; }                  // the leader's MOVE just ended: Zod clears this order next (side effects)
            if (!want)
            {
                if (!formOn && !c.noPath) { c.followMode = c.ownPath ? 3 : 0; return false; }
                formOn = false; FormExits++; formRetry = World.Time + ZFormation.ReenterDelay; c.followMode = 3; c.standing = false;
                c.noPath = false;
                if (Waypoints.Count > 0) { c.ownPath = true; c.ownWp = Waypoints[0]; }
                StopMove(); PathReissue();                                               // its own Zod path from here, next tick (budgeted)
                P.LogPublic(this, $"DV44: leaves the squad's follow ({c.why ?? "not on the leader's order"}): own path");
                c.why = null;
                return true;
            }
            if (!formOn) c.followSince = World.Time;
            else if (c.wallBlocks > 0 && World.Time - c.followSince >= 3) c.wallBlocks = 0;       // 3 s of clean follow: the wall is behind it
            formOn = true; FormTicks++; c.ownPath = false; c.noPath = true; c.followMode = mode;
            FormSqueeze = mode == 1 ? c.vk : 0;                                           // (checks: 1 = walking in the open V)
            if (FormSqueeze < 0.99) { if (FormSqueeze > 0.01) FormSqueezedHalf++; else FormSqueezedFile++; }
            PS.stillX = double.NaN;                                                      // standing in the squad while the leader waits is not frozen
            formPts ??= new List<(int x, int y)>(16);
            P.FollowPath(this, formPts);                                                 // the rest of its way: the trail ahead, then the leader's path
            cur.pfPoints = formPts; cur.pathFindingId = 0; cur.gotPfResponse = true;
            if (mode == 4) { StopMove(); return true; }                                  // standing in the squad: its place is not ahead yet
            SetTarget(sx, sy); SetVelocity();
            double rs = RealMoveSpeed;
            if (rs > 1e-6) { double f = v / rs; Dx = (float)(Dx * f); Dy = (float)(Dy * f); }
            double cap = P.MateCap(this, PosX + (WidthPix >> 1) + Dx * dt, PosY + (HeightPix >> 1) + Dy * dt);
            if (P.FollowStalled(this, cap < 0.02)) { c.vCmd = 0; return LeaveFollowStalled(); }   // review round 2 (v3): held > 1 s by its own squad's spacing while its leader walks: own path
            if (cap < 0.02) { c.vCmd = 0; StopMove(); return true; }                     // (safety: the pace keeps it clear of the one ahead)
            if (cap < 1) { Dx = (float)(Dx * cap); Dy = (float)(Dy * cap); c.vCmd *= cap; }   // (the one behind sets off with what it really walks)
            if (!ProcessMove(dt, true, out _, out _) && !FollowSlide(dt))
            {                                                                            // a wall the sweep missed: own path
                c.wallBlocks = Math.Min(c.wallBlocks + 1, 5);                              // again and again at the same wall: back off longer
                P.LogPublic(this, $"DV44: blocked by a wall on its follow step: own path{(c.wallBlocks > 1 ? $" for {ZFormation.ReenterDelay * (1 << (c.wallBlocks - 1)):0} s" : "")}");
                formOn = false; FormExits++; formRetry = World.Time + ZFormation.ReenterDelay * (1 << (c.wallBlocks - 1)); c.followMode = 3; c.noPath = false; c.standing = false;
                if (Waypoints.Count > 0) { c.ownPath = true; c.ownWp = Waypoints[0]; }
                StopMove(); PathReissue();
            }
            return true;
        }
    }

    public partial class ZObject
    {
        /// <summary>Review round 2 (v3): the follow is left for the minion's own Zod path (from where it stands, budgeted) after its squad's spacing
        /// held it StallLimit s while its leader walked on; it re-joins after ZFormation.ReenterDelay like after any other exit.</summary>
        bool LeaveFollowStalled()
        {
            var c = PC;
            formOn = false; FormExits++; formRetry = World.Time + ZFormation.ReenterDelay; c.followMode = 3; c.standing = false; c.noPath = false;
            if (Waypoints.Count > 0) { c.ownPath = true; c.ownWp = Waypoints[0]; }
            StopMove(); PathReissue();
            World.Pathing.LogPublic(this, $"DV44: leaves the squad's follow ({c.why}): own path");
            c.why = null;
            return true;
        }
    }

    public sealed partial class ZPathing
    {
        /// <summary>Trail kept behind a squad leader (px of arc).</summary>
        public const double TrailKeep = 200, TrailStep = 2;
        /// <summary>File -> V: every V slot valid for this many ticks and the file at least FileMin s old; a minion's half -> full width
        /// after WideTicks; V -> file after VBadTicks without any slot for one minion.</summary>
        public const int VReturnTicks = 50, WideTicks = 30, VBadTicks = 25;
        public const double FileMin = 3.0, AimAhead = 8;
        /// <summary>A minion farther than this from the trail (and no free line to it) walks its own path.</summary>
        public const double JoinLimit = 128;
        /// <summary>A minion this far behind its place along the trail (a jam held it) walks its own path.</summary>
        public const double MaxLagFollow = 150;
        /// <summary>A minion held by a unit outside its squad this many ticks walks its own path (the stuck guard's detours serve it).</summary>
        public const int FollowHeldTicks = 150;
        /// <summary>Trail look-ahead steps (px) tried in order.</summary>
        static readonly double[] Lookahead = { AimAhead, 4, 2 };
        /// <summary>File ranks swap when the one behind is this far (px, along the trail) ahead of the one before it.</summary>
        public const double RankHysteresis = 8;
        /// <summary>Pace: ahead of its place a minion slows linearly to a stop AheadRamp px ahead; behind it, with the leader standing,
        /// it walks CloseGain px/s per px behind (so it eases into its place); in the file never faster than the one ahead plus GapGain
        /// px/s per px of room over FileGap. Stop under StopSpeed, walk again over GoSpeed (px/s). Smoothing of the measured speeds.</summary>
        public const double AheadRamp = 12, CloseGain = 1.0, GapGain = 2.0, StopSpeed = 0.8, GoSpeed = 2.5, SpeedSmooth = 0.15, SideStepPace = 0.7;

        /// <summary>EndTick: record the trail of every walking squad leader and the smoothed speed of every squad member.</summary>
        void RecordTrails()
        {
            if (!CommitOn) return;                                                        // (bookkeeping only; v3 records trails in zones too)
            foreach (var u in units)
            {
                if (u.Kind != ZKind.Robot || (!u.IsMinion && u.Minions.Count == 0)) continue;
                var uc = u.PC;
                double ux = CX(u), uy = CY(u);
                if (!double.IsNaN(uc.lastX))
                {
                    double dd = Math.Sqrt((ux - uc.lastX) * (ux - uc.lastX) + (uy - uc.lastY) * (uy - uc.lastY)), d = dd / Math.Max(lastDt, 1e-6);
                    uc.vF += (d - uc.vF) * SpeedSmooth;
                    if (dd > 0.05) { uc.lastHx = (ux - uc.lastX) / dd; uc.lastHy = (uy - uc.lastY) / dd; uc.lastMoveAt = W.Time; }   // (its last walking direction)
                }
                uc.lastX = ux; uc.lastY = uy;
            }
            foreach (var l in units)
            {
                if (l.Kind != ZKind.Robot || l.IsMinion || l.Minions.Count == 0) continue;
                var c = l.PC;
                if (l.Waypoints.Count == 0 || l.Waypoints[0].mode != WpMode.Move) continue;
                var wp = l.Waypoints[0];
                double x = CX(l), y = CY(l);
                if (!wp.Equals(c.trailWp) && c.trail.Count > 0 && wp.Equals(c.trailNext) && c.trailNextOf.Equals(c.trailWp))
                    c.trailWp = wp;                                                      // the next waypoint of the same order list: the squad walks on (same trail)
                c.trailNext = l.Waypoints.Count > 1 ? l.Waypoints[1] : ZWaypoint.Cleared; c.trailNextOf = wp;
                if (!wp.Equals(c.trailWp) || c.trail.Count == 0)
                {                                                                        // a new order: a new trail, the squad sets off anew
                    c.trail.Clear(); c.trailLen = 0; c.trailWp = wp; c.trail.Add((x, y)); c.startDir = false; c.orderAt = W.Time;
                    c.squadMode = 0; c.modeSince = W.Time; c.vOkTicks = 0; c.fileOrder.Clear(); c.vOrder.Clear(); c.vAssigned = false;
                    foreach (var m in l.Minions) if (m != null) { m.PC.vk = 1; m.PC.vWide = 0; m.PC.vBad = 0; }
                    continue;
                }
                var (lx, ly) = c.trail[c.trail.Count - 1];
                double dd = Math.Sqrt((x - lx) * (x - lx) + (y - ly) * (y - ly));
                if (dd < TrailStep) continue;
                if (!c.startDir) { c.startHx = (x - c.trail[0].x) / Math.Max(1e-9, Dist(c.trail[0], (x, y))); c.startHy = (y - c.trail[0].y) / Math.Max(1e-9, Dist(c.trail[0], (x, y))); c.startDir = true; }
                c.trail.Add((x, y)); c.trailLen += dd;
                while (c.trail.Count > 2)
                {
                    double s0 = Dist(c.trail[0], c.trail[1]);
                    if (c.trailLen - s0 < TrailKeep) break;
                    c.trailLen -= s0; c.trail.RemoveAt(0);
                }
            }
        }

        static double Dist((double x, double y) a, (double x, double y) b) => Math.Sqrt((a.x - b.x) * (a.x - b.x) + (a.y - b.y) * (a.y - b.y));

        bool TrailValid(ZObject l) => l.PC.trail.Count > 0 && l.Waypoints.Count > 0 && l.Waypoints[0].Equals(l.PC.trailWp);

        /// <summary>Arc length the leader has really walked on this order (head to the oldest crumb); 0 without a trail.</summary>
        double TrailLength(ZObject l)
        {
            if (!TrailValid(l)) return 0;
            var tr = l.PC.trail; var (lx, ly) = tr[tr.Count - 1];
            return l.PC.trailLen + Math.Sqrt((CX(l) - lx) * (CX(l) - lx) + (CY(l) - ly) * (CY(l) - ly));
        }

        /// <summary>Direction the squad walks at the trail's old end (and beyond it): the order's first walking direction, else the
        /// leader's velocity once it walks. False = the leader has not set off yet (its path answer comes next tick).</summary>
        bool BackDir(ZObject l, out double hx, out double hy)
        {
            var c = l.PC;
            if (TrailValid(l) && c.startDir) { hx = c.startHx; hy = c.startHy; return true; }
            double vl = Math.Sqrt((double)l.Dx * l.Dx + (double)l.Dy * l.Dy);
            if (l.Waypoints.Count > 0 && vl > 1e-6) { hx = l.Dx / vl; hy = l.Dy / vl; return true; }
            hx = 0; hy = 1; return false;
        }

        /// <summary>The point `arc` px behind the leader along its trail (beyond the recorded start: straight on backwards), and the
        /// trail's forward direction there.</summary>
        void TrailAt(ZObject l, double arc, out double px, out double py, out double tx, out double ty)
        {
            double ax = CX(l), ay = CY(l), rem = Math.Max(0, arc);
            bool have = false; double ltx = 0, lty = 0;
            if (TrailValid(l))
            {
                var tr = l.PC.trail;
                for (int i = tr.Count - 1; i >= 0; i--)
                {
                    var (bx, by) = tr[i];
                    double sx = ax - bx, sy = ay - by, sl = Math.Sqrt(sx * sx + sy * sy);
                    if (sl < 1e-9) continue;
                    ltx = sx / sl; lty = sy / sl; have = true;
                    if (rem <= sl) { double f = rem / sl; px = ax - sx * f; py = ay - sy * f; tx = ltx; ty = lty; return; }
                    rem -= sl; ax = bx; ay = by;
                }
            }
            if (!have || l.PC.startDir) BackDir(l, out ltx, out lty);
            px = ax - ltx * rem; py = ay - lty * rem; tx = ltx; ty = lty;
        }

        /// <summary>Smoothed forward direction of the trail round `arc` (three samples 8 px apart).</summary>
        void TrailDir(ZObject l, double arc, out double tx, out double ty)
        {
            TrailAt(l, arc - 8, out _, out _, out double ax, out double ay);
            TrailAt(l, arc, out _, out _, out double bx, out double by);
            TrailAt(l, arc + 8, out _, out _, out double cx, out double cy);
            tx = ax + bx + cx; ty = ay + by + cy;
            double tl = Math.Sqrt(tx * tx + ty * ty);
            if (tl < 1e-6) { tx = bx; ty = by; } else { tx /= tl; ty /= tl; }
        }

        /// <summary>Arc position of point (x, y) along the leader's trail (nearest point) and its distance from the trail. Only the part of
        /// the trail within 1.5 x the straight distance to the leader (+ 2 tiles) is searched: where the leader's way turned back on
        /// itself (a U-turn round an obstacle), a unit next to the old arm is not far behind him on it.</summary>
        double TrailProject(ZObject l, double x, double y, out double off)
        {
            double ax = CX(l), ay = CY(l), acc = 0, best = double.MaxValue, bestArc = 0;
            double arcMax = 1.5 * Math.Sqrt((x - ax) * (x - ax) + (y - ay) * (y - ay)) + 32; bool cut = false;
            if (TrailValid(l))
            {
                var tr = l.PC.trail;
                for (int i = tr.Count - 1; i >= 0; i--)
                {
                    if (acc > arcMax) { cut = true; break; }                                     // (the older trail and its extension are not searched)
                    var (bx, by) = tr[i];
                    double sx = bx - ax, sy = by - ay, sl2 = sx * sx + sy * sy;
                    if (sl2 < 1e-12) continue;
                    double t = Math.Max(0, Math.Min(1, ((x - ax) * sx + (y - ay) * sy) / sl2));
                    double qx = ax + sx * t - x, qy = ay + sy * t - y, d2 = qx * qx + qy * qy, sl = Math.Sqrt(sl2);
                    if (d2 < best) { best = d2; bestArc = acc + t * sl; }
                    acc += sl; ax = bx; ay = by;
                }
            }
            if (cut) { off = Math.Sqrt(best); return bestArc; }
            BackDir(l, out double hx, out double hy);                                    // the straight extension beyond the old end
            double along = -((x - ax) * hx + (y - ay) * hy);
            if (along > 0)
            {
                double qx = ax - hx * along - x, qy = ay - hy * along - y, d2 = qx * qx + qy * qy;
                if (d2 < best) { best = d2; bestArc = acc + along; }
            }
            else { double qx = ax - x, qy = ay - y, d2 = qx * qx + qy * qy; if (d2 < best) { best = d2; bestArc = acc; } }
            off = Math.Sqrt(best);
            return bestArc;
        }

        /// <summary>A follower's "path" (cur.pfPoints, the same list every tick): trail points every 16 px from its own place on the
        /// trail up to its leader, then the leader's current target and the rest of the leader's path. The lane / bridge rules,
        /// crowd arrival and the checks see where it really goes; nothing is searched.</summary>
        internal void FollowPath(ZObject m, List<(int x, int y)> pts)
        {
            pts.Clear();
            var l = m.Leader;
            double s = TrailProject(l, CX(m), CY(m), out _);
            for (double a = s - 16; a > 0; a -= 16) { TrailAt(l, a, out double px, out double py, out _, out _); pts.Add(((int)Math.Round(px), (int)Math.Round(py))); }
            pts.Add((l.CenterX, l.CenterY));
            if (l.Waypoints.Count > 0) pts.Add((l.PathTargetX, l.PathTargetY));
            var lp = l.PathPoints;
            if (lp != null) for (int i = 0; i < lp.Count && pts.Count < 64; i++) pts.Add(lp[i]);
            if (l.Waypoints.Count == 0) return;
            var (gx, gy) = l.PathFinalPoint;
            if (pts[pts.Count - 1].x != gx || pts[pts.Count - 1].y != gy) pts.Add((gx, gy));
        }

        /// <summary>ResolveDeadlocks: a minion standing in its squad's follow waits for its leader - the "held by" chain goes on
        /// through him, so a loop of two squads blocking each other (leader A held by a minion of B that waits for leader B, held by
        /// a minion of A) is found and broken like any waiting loop.</summary>
        internal ZObject FollowHold(ZObject x) => FollowsSquad(x) && !Walking(x) && x.Leader != null && !x.Leader.Destroyed ? x.Leader : null;

        internal bool FollowsSquad(ZObject o) => FollowOn(o) && o.IsMinion && o.InFormation && o.PC.followMode != 3 && Following(o);

        /// <summary>Stress / checks (IsQueueWaiting): a follower standing in its squad waits in order only while its leader walks or
        /// itself waits in order; a follower that stands while its leader stands is not excused.</summary>
        bool FollowerWaitOk(ZObject o) => FollowsSquad(o) && o.Leader != null && (Walking(o.Leader) || TrafficWaitOk(o.Leader) || BridgeWaitOk(o.Leader));

        /// <summary>Gate: a minion (walking with its squad) blocked by a squad mate queues behind it (a shortened step or a wait), never
        /// side-steps round it: the V / file places keep them apart, a block is a moment.</summary>
        internal bool MateQueue(ZObject a, ZObject b)
        {
            if (ZonedMateQueue(a, b)) return true;                                        // pathing v3 (review round 2): zoned mates queue too (ZPathV3.cs)
            if (!FollowOn(a, b) || !a.IsMinion || !SameSquad(a, b)) return false;
            if (!a.InFormation) return false;                                             // (a regroup on its own path: the ordinary gate)
            if (InNarrowArea(a) || InNarrowArea(b)) return false;                         // lanes, gaps, bridges: the queue / evacuation rules
            var c = a.PC;
            if (W.Time < c.mateFreeUntil) return false;                                    // held by a standing mate too long: may go round it
            if (a.PS.heldTicks >= MateHoldTicks && a.PS.heldBy == b && (!Moving(b) || b.PathHeld)) { c.mateFreeUntil = W.Time + 1.0; Log(a, $"DV44: {b.Type}#{b.Id} stands in its way: goes round it"); return false; }
            return true;
        }
        /// <summary>A minion held this long by a standing squad mate goes round it (1 s, the gate's own sidestep keeps its side).</summary>
        public const int MateHoldTicks = 30;

        /// <summary>Gate: a minion walking with its squad is not held by its leader or a mate that walks with the squad too: the follow
        /// pace spaces them (MateCap), so nobody side-steps or queues round their own squad.</summary>
        internal bool MatesPass(ZObject a, ZObject b) => FollowOn(a, b) && SameSquad(a, b)
                                                         && (a.IsMinion ? WithSquad(a) && (b == a.Leader || WithSquad(b)) : b.Leader == a && WithSquad(b));   // (the leader too: past his own following squad)
        /// <summary>A mate's step is let through the gate unless it would bring the two closer than the hard minimum spacing (12 px for grunts).</summary>
        static bool MateStepOk(ZObject a, ZObject b, double ncx, double ncy)
        {
            double min = MinSpacing(a, b) + 1, bx = CX(b), by = CY(b), dn2 = (ncx - bx) * (ncx - bx) + (ncy - by) * (ncy - by);   // (+1: 13 px = 1.6 m for grunts, the open V's floor)
            return dn2 >= min * min || dn2 >= (CX(a) - bx) * (CX(a) - bx) + (CY(a) - by) * (CY(a) - by) - 1e-9;
        }
        /// <summary>Two mates closer than the hard spacing minus the dead-band (a real overlap).</summary>
        static bool MatesOverlap(ZObject a, ZObject b) { double m = MinSpacing(a, b) - DeadBand, dx = CX(a) - CX(b), dy = CY(a) - CY(b); return dx * dx + dy * dy < m * m; }
        static bool WithSquad(ZObject m) => m.IsMinion && m.InFormation && m.PC.followMode != 3 && m.PC.followMode != 6;

        /// <summary>Follow step cap (safety): a minion's step may not bring it closer than the hard minimum spacing + 1 px to the leader,
        /// a mate ahead of it in the squad's order (V: slot order; file: earlier in the file) or any mate that stands. Returns the share
        /// of the step it may take (the pace itself keeps the file gap and the V offsets).</summary>
        internal double MateCap(ZObject m, double nx, double ny)
        {
            var l = m.Leader; if (l == null) return 1;
            double ox = CX(m), oy = CY(m), lo = 0, hi = 1;
            bool Blocked(double f)
            {
                double px = ox + (nx - ox) * f, py = oy + (ny - oy) * f;
                foreach (var b in SquadAhead(m, l))
                {
                    double min = MinSpacing(m, b) + 1, bx = CX(b), by = CY(b);                // (the hard minimum: the pace keeps the file gap)
                    double dn = (px - bx) * (px - bx) + (py - by) * (py - by);
                    if (dn >= min * min) continue;
                    if (dn >= (ox - bx) * (ox - bx) + (oy - by) * (oy - by) - 1e-9) continue;   // not getting closer
                    return true;
                }
                return false;
            }
            if (!Blocked(1)) return 1;
            for (int i = 0; i < 6; i++) { double f = (lo + hi) * 0.5; if (Blocked(f)) hi = f; else lo = f; }
            return lo;
        }

        readonly List<ZObject> ahead = new List<ZObject>(6);
        /// <summary>The squad members minion m must not walk into: its leader, the mates before it in the squad's order (V: slot order,
        /// file: the file), and any mate that stands.</summary>
        List<ZObject> SquadAhead(ZObject m, ZObject l)
        {
            ahead.Clear(); ahead.Add(l);
            var c = l.PC;
            if (c.squadMode == 1)
            {                                                                            // file: whoever is ahead of it along the trail, and any mate that stands
                bool tie = ZonesOn && R2(8) && HandbackSquad(l);                         // review round 2 (v3): two standing mates level on the trail (re-joined from their
                foreach (var x in l.Minions)                                             // own paths side by side) held each other for 9 s: of two standing mates neither of
                    if (x != null && x != m && Separable(x) && (x.PC.arc < m.PC.arc || (Stands(x) && x.PC.predId != m.Id && (!tie || FileRankBefore(l, x, m)))))   // them ahead
                        ahead.Add(x);                                                    // along the trail, only the one first in the file holds the other (not one that waits for m)
                return ahead;
            }
            bool before = true;
            foreach (var x in VOrder(l)) { if (x == m) { before = false; continue; } if (x != null && Separable(x) && (before || Stands(x))) ahead.Add(x); }
            return ahead;
        }
        /// <summary>A mate that really stands (its follow stands it, or it is not with the squad): one only held by this very cap wants to
        /// walk on and does not count - two mates held by each other never wait for each other for ever (the squad's order decides).</summary>
        static bool Stands(ZObject x) => !Walking(x) && (!WithSquad(x) || x.PC.standing || x.PC.vCmd <= 0);

        /// <summary>PushApart: two minions of one squad that both walk with it are spaced by the follow (MateCap), never pushed.</summary>
        internal bool MinionsTogether(ZObject a, ZObject b) => FollowOn(a, b) && a.IsMinion && b.IsMinion && a.Leader == b.Leader && WithSquad(a) && WithSquad(b);

        /// <summary>Separation: squad mates walking with their squad (or standing with it in the open) are not shoved or sent aside by
        /// their own squad; in or at a lane, gap or bridge the evacuation and queue rules apply to them like to anybody (a mate idle
        /// on a deck must leave it), and so does any helper move (step aside, back off, bay, regroup).</summary>
        internal bool MatesKeepPlace(ZObject a, ZObject b)
        {
            if (!FollowOn(a, b) || !SameSquad(a, b)) return false;
            if (a.Waypoints.Count > 0) { int r = a.Waypoints[0].refId; if (r == RefStepAside || r == RefBackOff || r == RefBay || r == RefRegroup) return false; }
            if (InNarrowArea(a) || InNarrowArea(b)) return false;
            return true;
        }

        /// <summary>The unit stands in a lane's keep-clear area (a bridge deck, a 1-tile gap, a 2-3 tile vehicle corridor, their
        /// approaches and mouths): nobody parks there.</summary>
        internal bool InNarrowArea(ZObject o) => KeepClearAt(o.CenterX, o.CenterY) != null || InLaneZone(o);

        /// <summary>Regroup: minions within the squad's V (its slot radius) are regrouped already (Zod layer: 40 px).</summary>
        double RegroupLimit(ZObject l) => FollowOn(l) && !LiveOrder(l) && !(R3(524288) && SquadZonedRecently(l)) ? (CompactSquads ? CompactReach : Math.Max(RegroupDistance, SquadReach(l) + 8)) : RegroupDistance;   // (Zod squads of up to 4: <= 63 px; v3: CompactSquads; a live group order: the live reach)
        internal bool FarFromLeader(ZObject m) { double dx = CX(m) - CX(m.Leader), dy = CY(m) - CY(m.Leader); return dx * dx + dy * dy > RegroupLimit(m.Leader) * RegroupLimit(m.Leader); }

        /// <summary>FollowThrough (the leader's MOVE ended at its goal, this minion's with it; Zod stops a minion wherever it is): a
        /// minion in reach of its squad and outside any lane / gap / bridge area stays where it stopped (true, handled). One out of reach
        /// or in such an area walks to the nearest free spot next to its leader outside them (a regroup MOVE on its own path; true).
        /// False = not a DV44 follower of this order (the layer's own follow-through runs).</summary>
        bool FollowThroughCommit(ZObject m, ZObject l)
        {
            if (!FollowOn(m) || m.Kind != ZKind.Robot || LiveOrder(m)) return false;      // (pathing v3: a group order with the live arrivals: the live follow-through too)
            if (R3(524288) && SquadZonedRecently(l)) return false;                       // (pathing v3, review round 2: a squad that crossed a zone on this order: the live follow-through)
            var c = l.PC;
            if (c.trail.Count == 0 || !m.PS.tracked.Equals(c.trailWp) || m.PS.tracked.mode != WpMode.Move) return false;
            bool narrow = InNarrowArea(m);
            if (!narrow && NearLeader(m)) { Log(m, $"DV44: leader #{l.Id} arrived: stops with its squad"); return true; }
            bool nr = NoRocks(l); int comp = Reach.UnitComp(l, nr);
            if (comp < 0 || Reach.UnitComp(m, nr) != comp) return true;
            int tx, ty;
            if (narrow && ExitSpot(m, l, nr, comp, out tx, out ty))
            {                                                                            // in a lane / gap / bridge area: walk on out of it the way it
                m.Waypoints.Add(ZWaypoint.Move(tx, ty, false, RefRegroup));              // was going (its mates behind it do the same: nobody crosses)
                m.PS.regroupLeaderX = l.CenterX; m.PS.regroupLeaderY = l.CenterY; m.PS.regroupDist = Math.Sqrt((CX(m) - CX(l)) * (CX(m) - CX(l)) + (CY(m) - CY(l)) * (CY(m) - CY(l))); m.PS.regroupRetry = W.Time;
                m.PS.FollowThroughs++;                                                   // (out of it and still out of reach: the regroup check sends it on to him at once)
                Log(m, $"DV44: leader #{l.Id} arrived, it stands in a lane / gap / bridge area: walks on out of it to ({tx},{ty})");
                return true;
            }
            if (!FreeSpotNear(m, l, nr, comp, out tx, out ty)) { Log(m, $"DV44: leader #{l.Id} arrived: no free spot next to him"); return true; }
            double hx = m.PC.wantOn ? m.PC.wantHx : m.Dx, hy = m.PC.wantOn ? m.PC.wantHy : m.Dy;   // where it was walking (its follow, else its own path)
            if (!narrow && Math.Abs(hx) + Math.Abs(hy) > 1e-6)
            {                                                                            // it was walking on past him: a spot behind it would be a turn round
                double dl = Math.Sqrt((CX(m) - CX(l)) * (CX(m) - CX(l)) + (CY(m) - CY(l)) * (CY(m) - CY(l)));
                if (dl <= LooseReachV3 && (tx - CX(m)) * hx + (ty - CY(m)) * hy < 0)
                { Log(m, $"DV44: leader #{l.Id} arrived, its spot next to him lies behind it: stops {dl / 16:0.0} tiles from him"); return true; }
            }
            m.Waypoints.Add(ZWaypoint.Move(tx, ty, false, RefRegroup));
            m.PS.regroupLeaderX = l.CenterX; m.PS.regroupLeaderY = l.CenterY; m.PS.regroupDist = Math.Sqrt((CX(m) - CX(l)) * (CX(m) - CX(l)) + (CY(m) - CY(l)) * (CY(m) - CY(l))); m.PS.regroupRetry = W.Time + 20;
            m.PS.FollowThroughs++;
            Log(m, $"DV44: leader #{l.Id} arrived, {(narrow ? "it stands in a lane / gap / bridge area" : "out of the squad's reach")}: to a free spot ({tx},{ty}) next to him");
            return true;
        }

        /// <summary>Regroup (DV44): a minion that stopped just past its leader's reach (within LooseReach, outside any lane / gap / bridge
        /// area) while walking away from him (its last walking direction, the last 2 s) is not turned round to walk back to him.</summary>
        internal bool NoTurnBack(ZObject m, ZObject l, double d)
        {
            if (!FollowOn(m) || LiveOrder(m) || d > LooseReachV3 || InNarrowArea(m) || W.Time - m.PC.lastMoveAt > 2 || (R3(524288) && SquadZonedRecently(l))) return false;
            return (CX(l) - CX(m)) * m.PC.lastHx + (CY(l) - CY(m)) * m.PC.lastHy < 0;
        }

        /// <summary>FollowThrough (DV44): the first free spot out of the lane / gap / bridge area the minion stands in, straight on the way it
        /// was walking (its follow's heading, else towards its leader), within ExitReach px: reachable, clear of units and of every lane
        /// area, a free line to it.</summary>
        bool ExitSpot(ZObject m, ZObject l, bool nr, int comp, out int x, out int y)
        {
            x = y = 0;
            double hx = m.PC.wantOn ? m.PC.wantHx : CX(l) - CX(m), hy = m.PC.wantOn ? m.PC.wantHy : CY(l) - CY(m), hl = Math.Sqrt(hx * hx + hy * hy);
            if (hl < 1e-6) return false;
            hx /= hl; hy /= hl;
            for (double d = 8; d <= ExitReach; d += 8)
            {
                int px = (int)Math.Round(CX(m) + hx * d), py = (int)Math.Round(CY(m) + hy * d);
                if (KeepClearAt(px, py) != null || LaneAtPoint(px, py)) continue;
                if (!Reach.PointOk(px, py, m.IsRobot, nr, comp) || Occupied(px, py, m, null) || !LineFreeBetween(m, CX(m), CY(m), px, py, nr)) continue;
                x = px; y = py; return true;
            }
            return false;
        }
        bool LaneAtPoint(int px, int py) { foreach (var L in Lanes) if (px >= L.ZX0 && px < L.ZX1 && py >= L.ZY0 && py < L.ZY1) return true; return false; }
        public const double ExitReach = 160;

        /// <summary>FollowThrough: a minion walking on (not in a lane / gap / bridge area) this close to its arrived leader stops where it is
        /// rather than turn round to a spot next to him behind it (px; the stress suite's squad bar is 4 tiles).</summary>
        public const double LooseReach = 56;
        /// <summary>Pathing v3: a squad arrives as compact as live (a minion farther than the live RegroupDistance from its arrived leader walks in;
        /// the DV44 V reach let whole squads end 3.5-4 tiles apart: the stress suite's squad-spread measure).</summary>
        internal bool CompactSquads => TestZoneRules[4];
        /// <summary>Pathing v3, compact squads: the reach (px) within which a minion stays where it stopped when its leader arrived (live: 40).</summary>
        internal static double CompactReach = 48;
        double LooseReachV3 => CompactSquads ? CompactReach : LooseReach;

        /// <summary>V slot number (1..) of minion m: its place in the squad's V order (assigned by position), else its member index.</summary>
        static int VIndex(ZObject m, ZObject l)
        {
            var c = l.PC;
            if (c.vAssigned) { int i = c.vOrder.IndexOf(m); if (i >= 0) return i + 1; c.vOrder.Add(m); return c.vOrder.Count; }
            return l.Minions.IndexOf(m) + 1;
        }
        List<ZObject> VOrder(ZObject l) => l.PC.vAssigned ? l.PC.vOrder : l.Minions;

        /// <summary>A new order: ranks and V slots by where the minions stand (nobody crosses anybody's way); the squad sets off in its
        /// V when the way ahead has room for it and every minion has a slot, else in single file behind its leader.</summary>
        void StartSquad(ZObject l)
        {
            var c = l.PC;
            c.squadMode = 1; c.modeSince = W.Time; c.vOkTicks = 0;
            RankFile(l);
            AssignV(l, null);                                                             // the V by where they stand
            if (StartInV)
            {                                                                            // (a narrow part ahead turns the V into a file later, from its rows)
                bool all = true;
                foreach (var m in l.Minions) if (m != null && Following(m) && !TrailVSlot(m, 0.5, out _, out _)) { all = false; break; }
                if (all) { c.squadMode = 0; Log(l, "DV44: squad sets off in its V"); return; }
            }
            AssignV(l, c.fileOrder);                                                      // (the V order it will open into)
            Log(l, $"DV44: squad sets off in single file ({c.fileOrder.Count} behind)");
        }

        void RankFile(ZObject l)
        {
            var c = l.PC; c.fileOrder.Clear();
            var ranked = new List<(double arc, int idx, ZObject m)>();
            for (int i = 0; i < l.Minions.Count; i++) { var m = l.Minions[i]; if (m != null) ranked.Add((FileKey(l, m), i, m)); }
            ranked.Sort((a, b) => a.arc != b.arc ? a.arc.CompareTo(b.arc) : a.idx.CompareTo(b.idx));
            foreach (var r in ranked) c.fileOrder.Add(r.m);
        }

        /// <summary>Assign the V's slots by where the minions are (or in `order`): the nearest to the leader along the trail take the
        /// first row, the one further left of the two its left slot; so falling into the V nobody crosses anybody's way.</summary>
        void AssignV(ZObject l, List<ZObject> order)
        {
            var c = l.PC;
            var ms = new List<(double arc, double lat, int k, ZObject m)>();
            int k = 0;
            foreach (var m in order ?? l.Minions)
            {
                if (m == null || m.Leader != l) continue;
                double arc = TrailProject(l, CX(m), CY(m), out _);
                TrailAt(l, arc, out double px, out double py, out _, out _); TrailDir(l, arc, out double tx, out double ty);
                double lat = (CX(m) - px) * -ty + (CY(m) - py) * tx;
                ms.Add((order != null ? k : arc, lat, k, m)); k++;
            }
            ms.Sort((a, b) => a.arc != b.arc ? a.arc.CompareTo(b.arc) : a.k.CompareTo(b.k));
            c.vOrder.Clear();
            for (int i = 0; i < ms.Count; i += 2)
            {
                if (i + 1 >= ms.Count) { c.vOrder.Add(ms[i].m); break; }
                var (a, b) = (ms[i], ms[i + 1]);
                if (b.lat < a.lat || (b.lat == a.lat && b.k < a.k)) (a, b) = (b, a);    // odd slots are on the left (-), even on the right (+)
                c.vOrder.Add(a.m); c.vOrder.Add(b.m);
            }
            foreach (var m in l.Minions) if (m != null && !c.vOrder.Contains(m)) c.vOrder.Add(m);
            c.vAssigned = true;
        }

        /// <summary>File rank key: how far behind the leader along his trail, plus 1.5 x how far off it (px); a minion level with or
        /// ahead of the leader ranks last (it lets the leader and the file pass, then falls in at the back: nobody waits for it).</summary>
        double FileKey(ZObject l, ZObject m)
        {
            double a = TrailProject(l, CX(m), CY(m), out double off);
            if (a < 1 && BackDir(l, out double hx, out double hy) && (CX(m) - CX(l)) * hx + (CY(m) - CY(l)) * hy > -2) return 1000 + off;
            return a + 1.5 * off;
        }

        static int FileGap(ZObject l) => (int)Math.Round(SquadSpacing * ZFormation.BodyScale(l.Type)) + 2;

        /// <summary>V slot of minion m at side width k in the trail frame (centre px, rounded): the trail point RowBack x row behind
        /// the leader, moved k x SideOut x row along the trail's normal. Valid = free ground in the minion's component, not in a 1-tile
        /// gap / bridge zone, no unit outside the squad in it, and a free line from the trail to it.</summary>
        bool TrailVSlot(ZObject m, double k, out int sx, out int sy) => TrailVSlot(m, k, out sx, out sy, out _);
        bool TrailVSlot(ZObject m, double k, out int sx, out int sy, out string why)
        {
            var l = m.Leader; sx = sy = 0; why = null;
            int idx = VIndex(m, l);
            if (idx <= 0) { why = "not in the squad"; return false; }
            var (back, side) = ZFormation.Offset(idx, l.Type);
            TrailAt(l, back, out double bx, out double by, out _, out _);
            TrailDir(l, back, out double tx, out double ty);
            double px = bx + -ty * side * k, py = by + tx * side * k;
            sx = (int)Math.Round(px); sy = (int)Math.Round(py);
            bool nr = NoRocks(m); int c = Reach.UnitComp(m, nr);
            if (c < 0) { why = "off the graph"; return false; }
            if (SlotInNarrowZone(sx, sy)) { why = "slot in a 1-tile gap / bridge zone"; return false; }
            if (!Reach.PointOk(sx, sy, true, nr, c)) { why = "slot not free ground"; return false; }
            if (FormationSlotTaken(m, sx, sy)) { why = "slot taken by another unit"; return false; }
            if (!LineFreeBetween(m, bx, by, sx, sy, nr)) { why = "wall between the trail and the slot"; return false; }
            return true;
        }

        /// <summary>The robot's box swept (4 px steps) from (x0, y0) to (x1, y1) stays on free tiles of its grid.</summary>
        bool LineFreeBetween(ZObject m, double x0, double y0, double x1, double y1, bool nr)
        {
            double dx = x1 - x0, dy = y1 - y0, len = Math.Sqrt(dx * dx + dy * dy);
            int steps = (int)(len / 4) + 1, half = m.WidthPix >> 1;
            for (int i = 0; i <= steps; i++)
            {
                double f = Math.Min(1.0, i * 4 / Math.Max(len, 1e-9));
                if (!Reach.BoxFree((int)Math.Floor(x0 + dx * f) - half, (int)Math.Floor(y0 + dy * f) - half, m.WidthPix, m.HeightPix, true, nr)) return false;
            }
            return true;
        }

        /// <summary>A V slot may lie in a lane's keep-clear area (walking is not parking) and in a 2-3 tile vehicle corridor where the
        /// ground is free, but not in the zone of a 1-tile robot gap or a bridge deck: there the squad walks single file.</summary>
        bool SlotInNarrowZone(int x, int y)
        {
            foreach (var L in Lanes) if ((L.Bridge || !L.VehicleLane) && BoxInZone(L, x - 8, y - 8, 16, 16)) return true;
            return false;
        }

        bool BridgeKeep(double x, double y, double margin = 0)
        {
            foreach (var L in Lanes) if (L.Bridge && x >= L.KX0 - margin && x < L.KX1 + margin && y >= L.KY0 - margin && y < L.KY1 + margin) return true;
            return false;
        }
        /// <summary>A minion that left its squad's follow for a bridge re-joins it only this far (px) clear of every bridge keep-clear area
        /// (it and its leader): at the edge of the area it went follow -> own path -> follow every second, each time turning round.</summary>
        public const double BridgeRejoinMargin = 48;

        static readonly double[] SqueezeSteps = { 1.0, 0.5 };

        /// <summary>The squad's mode for this tick (evaluated once per tick, by its first minion): each minion's V width (full / half:
        /// narrower at once, wider after WideTicks), and V or single file for the squad.</summary>
        void EvalSquad(ZObject l)
        {
            var c = l.PC;
            if (c.evalTime == W.Time) return;
            c.evalTime = W.Time;
            double bhx, bhy, lvl = Math.Sqrt((double)l.Dx * l.Dx + (double)l.Dy * l.Dy);   // "ahead of the leader": along his heading now
            if (Moving(l) && lvl > 1e-6) { bhx = l.Dx / lvl; bhy = l.Dy / lvl; } else BackDir(l, out bhx, out bhy);
            foreach (var m in l.Minions)
                if (m != null)
                {                                                                        // this tick's places along the trail
                    var mc = m.PC; mc.arc = TrailProject(l, CX(m), CY(m), out double o); mc.off = o;
                    double ahead = (CX(m) - CX(l)) * bhx + (CY(m) - CY(l)) * bhy;       // in front of him along his heading now: the leader passes it first
                    mc.aheadOfLeader = (mc.arc < 1 && ahead > -2) || ahead > 2;
                    if (mc.aheadOfLeader) mc.arc = 0;
                }
            string bad = null, whyAhead = null; bool anyBad = false, allHalf = true;
            foreach (var m in l.Minions)
            {
                if (m == null || !Following(m)) continue;
                var mc = m.PC; int best = -1;
                for (int i = 0; i < SqueezeSteps.Length; i++) { if (TrailVSlot(m, SqueezeSteps[i], out _, out _, out string why)) { best = i; break; } bad ??= $"{m.Type}#{m.Id}: {why}"; }
                if (best < 0) { if (++mc.vBad >= VBadTicks) anyBad = true; allHalf = false; mc.vWide = 0; continue; }
                mc.vBad = 0;
                double kv = SqueezeSteps[best];
                if (kv < mc.vk - 1e-9) { mc.vk = kv; mc.vWide = 0; }                      // narrower at once
                else if (kv > mc.vk + 1e-9) { if (++mc.vWide >= WideTicks) { mc.vk = kv; mc.vWide = 0; } }   // wider after a while
                else mc.vWide = 0;
            }
            if (c.squadMode == 0)
            {
                bool fits = VFitsAhead(l, out whyAhead, W.Time - c.modeSince < VMinTime ? VNearAhead : VLookAhead);   // (a fresh V first forms - its rows make the file - unless the narrow part is right ahead)
                if (fits && !anyBad) return;
                c.squadMode = 1; c.modeSince = W.Time; c.vOkTicks = 0;                    // the V does not fit: single file on the trail
                RankFile(l);
                Log(l, $"DV44: squad single file on the trail ({c.fileOrder.Count} behind; {(anyBad ? bad : whyAhead)})");
                return;
            }
            RankFile(l);                                                                 // (the file's order is where they are along the trail: FilePlace)
            if (W.Time - c.modeSince < FileMin || !allHalf || !VFitsAhead(l, out _)) { c.vOkTicks = 0; return; }
            if (++c.vOkTicks < VReturnTicks) return;
            c.squadMode = 0; c.modeSince = W.Time; c.vOkTicks = 0;
            AssignV(l, c.fileOrder);
            foreach (var m in l.Minions) if (m != null) { m.PC.vk = 0.5; m.PC.vWide = 0; }  // (the V opens half width, then widens where it can)
            Log(l, "DV44: squad re-opens its V");
        }

        /// <summary>The leader's way ahead (the next VLookAhead px of his path) has room for the squad's V at half width: at every 8 px
        /// the V's lateral places (half SideOut x row either side) are free ground outside 1-tile gaps / bridge zones. A squad keeps its
        /// V only where it fits, and goes single file BEFORE a gap, not in its mouth.</summary>
        bool VFitsAhead(ZObject l, out string why, double look = VLookAhead)
        {
            why = null;
            if (l.Waypoints.Count == 0 || !Moving(l)) return true;                          // standing: its slots decide
            int rows = (l.Minions.Count + 1) / 2;
            if (rows <= 0) return true;
            bool nr = NoRocks(l); int comp = Reach.UnitComp(l, nr);
            if (comp < 0) return true;
            double px = CX(l), py = CY(l), left = look, side = Math.Round(ZFormation.SideOut * ZFormation.BodyScale(l.Type)) * 0.5;
            var pts = l.PathPoints;
            for (int i = -1; left > 0 && (pts == null ? i < 0 : i < pts.Count); i++)
            {
                double qx = i < 0 ? l.PathTargetX : pts[i].x, qy = i < 0 ? l.PathTargetY : pts[i].y;
                double dx = qx - px, dy = qy - py, len = Math.Sqrt(dx * dx + dy * dy);
                if (len < 1e-6) continue;
                double ux = dx / len, uy = dy / len;
                for (double t = Math.Min(8, len); t <= len && left > 0; t += 8, left -= 8)
                {
                    double cx = px + ux * t, cy = py + uy * t;
                    for (int r = 1; r <= rows; r++)
                        for (int sg = -1; sg <= 1; sg += 2)
                        {
                            if (r == rows && (l.Minions.Count & 1) == 1 && sg > 0) continue;   // an odd last member stands on the left
                            int x = (int)Math.Round(cx + -uy * side * r * sg), y = (int)Math.Round(cy + ux * side * r * sg);
                            if (SlotInNarrowZone(x, y) || !Reach.PointOk(x, y, true, nr, comp)) { why = $"no room for the V {t:0} px ahead ({x},{y})"; return false; }
                        }
                }
                px = qx; py = qy;
            }
            return true;
        }
        /// <summary>How far along the leader's path the V must fit (px).</summary>
        public const double VLookAhead = 64;

        /// <summary>The minion walks with its squad this tick (not on its own path, same order as its leader).</summary>
        static bool Following(ZObject m) =>
            m.Leader != null && m.Waypoints.Count > 0 && m.Leader.Waypoints.Count > 0 && m.Waypoints[0].Equals(m.Leader.Waypoints[0]) && !(m.PC.ownPath && m.PC.ownWp.Equals(m.Waypoints[0]));

        /// <summary>ProcessMoveWP (DV44): a minion walking a regroup MOVE back to its squad (its own path) more than RegroupHurry px from its
        /// spot hurries at DV24's catch-up on dry ground (the same pace rule as a following minion behind its place: its own Zod speed x
        /// CatchUp, x 1 in water or on a slow tile); every other unit, and the last stretch, at its own Zod speed.</summary>
        internal double RegroupPace(ZObject o)
        {
            if (!FollowOn(o) || !o.IsMinion || o.Kind != ZKind.Robot || o.Waypoints.Count == 0 || o.Waypoints[0].refId != RefRegroup || SlowGround(o)) return 1;
            var (gx, gy) = o.PathFinalPoint; double dx = gx - CX(o), dy = gy - CY(o);
            return dx * dx + dy * dy > RegroupHurry * RegroupHurry ? ZFormation.CatchUp : 1;
        }
        public const double RegroupHurry = 24;

        /// <summary>PlanDisplace (DV44): how far a walking squad minion (following or regrouping) may still be displaced by a walker this
        /// tick - what is left of its pace (FollowTopSpeed x dt) after its own step: walk + displacement never beat its own Zod speed x
        /// DV24's catch-up (x 1 in water). Anybody else: no limit here (the layer's own rules).</summary>
        internal double PushRoom(ZObject o, double dt)
        {
            if (!FollowOn(o) || !o.IsMinion || o.Kind != ZKind.Robot || !Walking(o)) return double.MaxValue;   // (a held or standing one: overlaps are pushed apart as before)
            var c = o.PC; if (double.IsNaN(c.lastX)) return double.MaxValue;
            double top = FollowTopSpeed(o) * dt, wx = CX(o) - c.lastX, wy = CY(o) - c.lastY;
            return Math.Sqrt(Math.Max(0, top * top - (wx * wx + wy * wy)));
        }

        /// <summary>Walking there costs at most its own Zod speed (water, slow tiles): no catch-up.</summary>
        bool SlowGround(ZObject m) => W.Map.TileWalkSpeed(m.CenterX, m.CenterY) < 1.0;

        /// <summary>The minion's top speed on its follow: its own Zod speed (RealMoveSpeed: tile, run, damage) x DV24's CatchUp on
        /// normal / road ground, x 1 in water or on any slow tile.</summary>
        internal double FollowTopSpeed(ZObject m) => m.RealMoveSpeed * (SlowGround(m) ? 1.0 : ZFormation.CatchUp);

        /// <summary>Where minion m steers this tick (centre px), its mode (1 V, 2 file, 4 stands, 5 side-step) and its speed (px/s, at
        /// most FollowTopSpeed). False = no usable place (the minion walks its own path).</summary>
        internal bool FollowTarget(ZObject m, out int sx, out int sy, out int mode, out double v)
        {
            sx = sy = 0; mode = 0; v = 0;
            var l = m.Leader; var mc = m.PC;
            double mx = CX(m), my = CY(m);
            mc.wantOn = false; mc.vCmd = 0; mc.followAlong = double.NaN;
            double bm = mc.bridgeLeft ? BridgeRejoinMargin : 0;                          // (hysteresis: once left for a bridge, back only well clear of it)
            if (BridgeKeep(mx, my, bm) || BridgeKeep(CX(l), CY(l), bm)) { mc.why = "bridge"; mc.bridgeLeft = true; return false; }   // bridges: each minion its own Zod path (DV8 / DV24)
            mc.bridgeLeft = false;
            if (m.PS.wait == ZPathWait.Held && m.PS.heldTicks >= FollowHeldTicks && m.PS.heldBy != null && !SameSquad(m, m.PS.heldBy) && Walking(l))   // (its leader waits too: it waits with him)
            { mc.why = $"held by {m.PS.heldBy.Type}#{m.PS.heldBy.Id} for {m.PS.heldTicks * 0.01:0.0} s"; return false; }   // blocked by traffic outside the squad: its own path (the stuck guard's detours)
            if (!BackDir(l, out double hx, out double hy)) { mode = 4; mc.standing = true; sx = (int)Math.Round(mx); sy = (int)Math.Round(my); return true; }   // the leader has not set off yet
            if (!l.PC.vAssigned) StartSquad(l);
            EvalSquad(l);
            var c = l.PC;
            double top = FollowTopSpeed(m), own = m.RealMoveSpeed;
            // out of the way of the walking leader / a mate with right of way (committed for SideCommitTime: no flicker)
            if (W.Time < mc.sideUntil && (mc.sideX - mx) * (mc.sideX - mx) + (mc.sideY - my) * (mc.sideY - my) >= 1)
            { sx = mc.sideX; sy = mc.sideY; mode = 5; v = Math.Min(top, Math.Max(own * SideStepPace, StartShare * l.PC.vF)); mc.standing = false; Want(mc, mx, my, sx, sy); return true; }
            if (ClearLeaderLine(m, l, out sx, out sy, true))                              // (the lanes / the order along the trail keep mates apart: only the leader's line)
            { mc.sideX = sx; mc.sideY = sy; mc.sideUntil = W.Time + SideCommitTime; mode = 5; v = Math.Min(top, Math.Max(own * SideStepPace, StartShare * l.PC.vF)); mc.standing = false; Want(mc, mx, my, sx, sy); return true; }
            bool vee = c.squadMode == 0;
            double s = TrailProject(l, mx, my, out double off);
            if (mc.aheadOfLeader) s = 0;                                                  // (in front of the leader: where the trail's old arm lies does not count)
            if (off > JoinLimit && !mc.aheadOfLeader) { mc.why = $"{off:0} px off the trail"; return false; }
            double place, side = 0; ZObject pred = null;
            if (vee)
            {
                int idx = VIndex(m, l);
                if (idx <= 0) return false;
                var (back, sd) = ZFormation.Offset(idx, l.Type);
                place = back; side = sd * mc.vk;
            }
            else place = FilePlace(m, l, out pred);
            double along = s - place;                                                    // > 0: behind its place along the trail
            mc.followAlong = along;
            if (along > MaxLagFollow) { mc.why = $"{along:0} px behind its place"; return false; }
            double vl0 = c.vF;
            if (s < 1 && along < 0)
            {                                                                            // level with / ahead of the leader: walk on beside his line at
                double lvx = l.Dx, lvy = l.Dy, lvl = Math.Sqrt(lvx * lvx + lvy * lvy);  // StartShare of his pace, he passes it and it falls in behind
                if (Walking(l) && lvl > 1e-6 && vl0 > GoSpeed)                           // (no standing start, never a step back)
                {
                    double ux = lvx / lvl, uy = lvy / lvl, rx = mx - CX(l), ry = my - CY(l);
                    double lat = rx * -uy + ry * ux, tx = mx + ux * AimAhead, ty = my + uy * AimAhead;
                    if (Math.Abs(lat) >= MinSpacing(l, m) + 1 && LineFreeBetween(m, mx, my, tx, ty, NoRocks(m)))   // (clear of his line: MateStepOk's 13 px)
                    {
                        v = Math.Min(top, StartShare * vl0); mode = vee ? 1 : 2; mc.standing = false; mc.vCmd = v;
                        sx = (int)Math.Round(tx); sy = (int)Math.Round(ty); Want(mc, mx, my, sx, sy); return true;
                    }
                }
                mode = 4; mc.standing = true; mc.vCmd = 0; sx = (int)Math.Round(mx); sy = (int)Math.Round(my); return true;
            }
            // pace: the smoothed leader's speed, DV24's catch-up behind the place, slower ahead of it (walking on slowly while the
            // leader walks, so the place comes to it); with the leader standing it eases into its place; in the file never faster
            // than the one ahead allows
            double vl = c.vF;
            double vdes = along >= 0
                ? Math.Max(vl * (1 + Math.Min(ZFormation.CatchUp - 1, Math.Max(0, along - ZFormation.CatchUpFrom) / ZFormation.CatchUpRamp)), CloseGain * along)
                : vl * Math.Max(s >= 1 && vl > GoSpeed && (vee || off >= FileAside) ? (W.Time - c.orderAt < StartTime ? StartShare : AheadFloor) : 0, 1 + along / AheadRamp);   // (V: walks on in its lane; file: beside the trail it walks on beside it
                                                                                 //  until its turn comes, on the trail or level with the leader it waits)
            if (!vee) vdes = FileCornerPace(m, l, pred, s, along, vl, vdes);     // last3 fix 1 (v3 only): clear of its mate on the ground, it walks on (ZPathV3.Last3.cs)
            if (!vee)
            {
                var ahead = pred ?? l;
                double ex = CX(ahead) - mx, ey = CY(ahead) - my, de = Math.Sqrt(ex * ex + ey * ey);
                double va = ahead == l ? vl : Math.Max(ahead.PC.vF, ahead.PC.vCmd);
                if ((ex * hx + ey * hy) > 0 || ahead == l)
                {                                                                        // (only a unit really ahead of it)
                    double gapPace = va + GapGain * (de - FileGap(l));                   // car-following: FileGap behind the one ahead
                    double block = Math.Min(va + GapGain * (de - StartGap(m, ahead)), StartShare * vl);   // the file sets off as one: at StartShare of
                    vdes = Math.Max(Math.Min(vdes, gapPace), block);                                     // the pace while there is room (the gap opens as it goes)
                }
            }
            v = Math.Max(0, Math.Min(top, vdes));
            mc.vCmd = 0;
            if (mc.standing ? v < GoSpeed : v < StopSpeed)
            { mc.standing = true; mode = 4; v = 0; sx = (int)Math.Round(mx); sy = (int)Math.Round(my); return true; }   // hysteresis: no stop-go flicker
            mc.vCmd = v;
            mc.standing = false;
            mode = vee ? 1 : 2;
            bool nr = NoRocks(m);
            double px = 0, py = 0; bool found = false;
            double blend = vee ? Math.Max(0, Math.Min(1, 1 - (along - AimAhead) / 24)) : 0;   // within a row of its place: into the V lane
            if (!vee && along < -BehindTol && off >= FileAside)
            {                                                                            // file, its turn not yet come: on beside the trail (keeps its side)
                TrailAt(l, s, out double qx, out double qy, out _, out _); TrailDir(l, s, out double qtx, out double qty);
                side = (mx - qx) * -qty + (my - qy) * qtx; blend = 1;
            }
            else if (W.Time < mc.sideUntil + SideReturnHold)
            {                                                                            // just stepped out of his line: on parallel to the trail for a
                TrailAt(l, s, out double qx, out double qy, out _, out _); TrailDir(l, s, out double qtx, out double qty);   // moment (never straight
                double cur = (mx - qx) * -qty + (my - qy) * qtx;                           // back across it, a turn round)
                if (!vee || Math.Abs(cur) > Math.Abs(side * blend)) { side = cur; blend = 1; }
            }
            TrailDir(l, s, out double ftx, out double fty);
            if (!found)
                foreach (double la in Lookahead)
                {                                                                        // pursue the trail ahead of its own projection (never
                    double aim = s - la;                                                 // behind it); round a corner the look-ahead shortens
                    TrailAt(l, aim, out px, out py, out _, out _);
                    TrailDir(l, aim, out double tx, out double ty);
                    px += -ty * side * blend; py += tx * side * blend;
                    if ((px - mx) * ftx + (py - my) * fty < -0.5) continue;               // (never a step back along the squad's way)
                    if (LineFreeBetween(m, mx, my, px, py, nr)) { found = true; break; }
                }
            if (!found)
            {
                TrailAt(l, s, out px, out py, out _, out _);                             // off the line with a corner between: onto the trail first
                if (!LineFreeBetween(m, mx, my, px, py, nr)) { mc.why = "no free line to the trail"; return false; }
            }
            sx = (int)Math.Round(px); sy = (int)Math.Round(py);
            if ((sx - mx) * (sx - mx) + (sy - my) * (sy - my) < 1) { mode = 4; mc.standing = true; v = 0; mc.vCmd = 0; sx = (int)Math.Round(mx); sy = (int)Math.Round(my); return true; }
            Want(mc, mx, my, sx, sy);
            return true;
        }

        /// <summary>Review round 2 (v3 only): a following minion whose step its squad's spacing (MateCap) has blocked for more than StallLimit s
        /// while its leader walks and it is more than StallAlong px behind its place leaves the follow for its own Zod path at once (it stood
        /// v = 0 for 9 s until MaxLagFollow's 150 px). blocked: its step was blocked this tick (false resets the stall clock).</summary>
        internal bool FollowStalled(ZObject m, bool blocked)
        {
            var c = m.PC;
            if (!ZonesOn || !R2(16)) return false;
            var l = m.Leader;
            if (l != null && !HandbackSquad(l)) { c.stallSince = -1; return false; }
            if (!blocked || l == null || !Walking(l) || !(c.followAlong > StallAlong)) { c.stallSince = -1; return false; }
            if (c.stallSince < 0) { c.stallSince = W.Time; return false; }
            if (W.Time - c.stallSince <= StallLimit) return false;
            c.stallSince = -1;
            c.why = $"held {StallLimit:0.0} s by its own squad's spacing {c.followAlong:0} px behind its place while its leader walks";
            return true;
        }
        public const double StallLimit = 1.0, StallAlong = 8;

        /// <summary>Remember where a following minion is heading this tick (its mates step out of that line: ClearLeaderLine).</summary>
        static void Want(ZCommitState mc, double mx, double my, int sx, int sy)
        {
            double dx = sx - mx, dy = sy - my, d = Math.Sqrt(dx * dx + dy * dy);
            if (d < 1e-6) { mc.wantOn = false; return; }
            mc.wantOn = true; mc.wantHx = dx / d; mc.wantHy = dy / d;
        }

        /// <summary>A squad sets off in its V when the V fits there (slots by where the minions stand, the way ahead checked for
        /// StartLookAhead px), else in single file.</summary>
        public const bool StartInV = true;
        public const double AheadFloor = 0.5, SideCommitTime = 0.3, VMinTime = 1.5, VNearAhead = 24, FileAside = 14, BehindTol = 2;
        /// <summary>Setting off: a minion level with / ahead of its leader walks on beside his line at StartShare x his pace (he passes
        /// it); in the file a minion closer than FileGap behind the one ahead still walks at up to StartShare x the pace while it is
        /// StartGap clear of it (so the whole file sets off at once and the gaps open as it goes).</summary>
        public const double StartShare = 0.7, StartMargin = 4, StartTime = 2.0;
        /// <summary>After a side-step out of the leader's / a mate's line a minion walks on parallel to the trail this long (s) before it
        /// eases back onto it: stepping straight back across was a turn round in place.</summary>
        public const double SideReturnHold = 1.0;
        double StartGap(ZObject m, ZObject ahead) => MinSpacing(m, ahead) + StartMargin;

        /// <summary>A minion on the line of a squad member walking (or about to walk) towards it that has right of way (the leader up to
        /// 4 tiles ahead, a mate before it in the squad's order up to 2 tiles), closer than the spacing + 2 px to that line, steps
        /// straight sideways out of it (to its V side when it stands right on the line): nobody walks into his own squad, nobody
        /// pushes. A mate's heading is where its follow steers (it may be held by this very minion).</summary>
        bool ClearLeaderLine(ZObject m, ZObject l, out int sx, out int sy, bool leaderOnly = false)
        {
            sx = sy = 0;
            foreach (var b in RightOfWay(m, l))
            {
                if (leaderOnly && b != l) break;
                double hx, hy;
                if (b == l)
                {
                    if (!Moving(b) || b.PathHeld) continue;
                    double vl = Math.Sqrt((double)b.Dx * b.Dx + (double)b.Dy * b.Dy);
                    if (vl < 1e-6) continue;
                    hx = b.Dx / vl; hy = b.Dy / vl;
                }
                else { if (!b.PC.wantOn) continue; hx = b.PC.wantHx; hy = b.PC.wantHy; }
                double rx = CX(m) - CX(b), ry = CY(m) - CY(b);
                double ahead = rx * hx + ry * hy, lat = rx * -hy + ry * hx, need = MinSpacing(b, m) + 2, range = b == l ? 64 : 32;   // (squad members pass at the hard minimum: MatesPass)
                if (ahead < 4 || ahead > range || Math.Abs(lat) >= need - 1) continue;             // really in front of him, where he would walk into it (MateStepOk: 13 px)
                int idx = VIndex(m, l);
                double sg = Math.Abs(lat) > 0.5 ? Math.Sign(lat) : ((idx & 1) == 1 ? -1 : 1);     // its own V side when it stands right on the line
                var mc = m.PC;
                if (mc.clearSide != 0 && mc.clearAt >= l.PC.orderAt) sg = mc.clearSide;          // (this order it already stepped to that side: never the other way back)
                double fw = b == l ? Math.Max(AimAhead, need - Math.Abs(lat) + 1) : 0;   // (out of the leader's way: aside AND on, a diagonal step - no stop)
                double d = need - Math.Abs(lat) + 1, tx = CX(m) + -hy * sg * d + hx * fw, ty = CY(m) + hx * sg * d + hy * fw;
                if (!LineFreeBetween(m, CX(m), CY(m), tx, ty, NoRocks(m)) || !SquadRoom(m, l, tx, ty))
                {                                                                        // (never into a wall, nor into a mate: two mates held by each other)
                    sg = -sg; d = need + Math.Abs(lat) + 1; tx = CX(m) + -hy * sg * d + hx * fw; ty = CY(m) + hx * sg * d + hy * fw;
                    if (!LineFreeBetween(m, CX(m), CY(m), tx, ty, NoRocks(m)) || !SquadRoom(m, l, tx, ty)) continue;
                }
                sx = (int)Math.Round(tx); sy = (int)Math.Round(ty);
                mc.clearSide = (int)sg; mc.clearAt = W.Time;
                return true;
            }
            return false;
        }

        /// <summary>A side-step target with room: no other member of m's squad closer to it than the hard minimum spacing + 1 px.</summary>
        static bool SquadRoom(ZObject m, ZObject l, double tx, double ty)
        {
            bool Clear(ZObject x) { if (x == null || x == m || x.Destroyed) return true; double min = MinSpacing(m, x) + 1, dx = CX(x) - tx, dy = CY(x) - ty; return dx * dx + dy * dy >= min * min; }
            if (!Clear(l)) return false;
            foreach (var x in l.Minions) if (!Clear(x)) return false;
            return true;
        }

        readonly List<ZObject> rightOfWay = new List<ZObject>(6);
        /// <summary>The leader and the mates before m in the squad's order (V: slot order, file: the file).</summary>
        List<ZObject> RightOfWay(ZObject m, ZObject l)
        {
            rightOfWay.Clear(); rightOfWay.Add(l);
            var c = l.PC;
            var order = c.squadMode == 1 && c.fileOrder.Contains(m) ? c.fileOrder : VOrder(l);
            foreach (var x in order) { if (x == m) break; if (x != null && Separable(x) && WithSquad(x)) rightOfWay.Add(x); }
            return rightOfWay;
        }

        /// <summary>File place, from where everybody is this tick: FileGap behind the squad member nearest ahead of it along the leader's
        /// trail (the leader for the first; a mate still level with / ahead of the leader does not count until he has passed it). No
        /// fixed ranks: whoever is ahead along the trail leads, so nobody ever waits for a unit behind it and there is no loop of waits.</summary>
        double FilePlace(ZObject m, ZObject l, out ZObject pred)
        {
            var mc = m.PC; double s = mc.arc; pred = null; double best = 0;
            foreach (var x in l.Minions)
            {
                if (x == null || x == m || x.Destroyed || x.KillAt >= 0 || !Following(x)) continue;
                var xc = x.PC;
                if (xc.aheadOfLeader || xc.arc >= s - 0.5 || (xc.arc == s && x.Id > m.Id)) continue;
                if (xc.arc > best || pred == null) { best = xc.arc; pred = x; }
            }
            mc.predId = pred != null ? pred.Id : l.Id;
            return best + FileGap(l);
        }

        /// <summary>Review round 2 (v3): x ranks before m in the squad's file order (RankFile, rebuilt every tick from where each member stands
        /// along the trail; the member index breaks a tie).</summary>
        static bool FileRankBefore(ZObject l, ZObject x, ZObject m)
        {
            var fo = l.PC.fileOrder; int ix = fo.IndexOf(x), im = fo.IndexOf(m);
            if (ix >= 0 && im >= 0) return ix < im;
            return l.Minions.IndexOf(x) < l.Minions.IndexOf(m);
        }
    }
}
