using System;
using System.Collections.Generic;

namespace OpenZ.Sim
{
    /// <summary>
    /// Circles on the pixel grid: robot radius 6 px (Tough 7), vehicle and cannon 16 px, so robots keep 12 px (1.5 m),
    /// robot-vehicle 22 px, vehicles 32 px (their 2x2 box) and squad members 14 px between centres.
    ///  1. Gate (called from ZObject.ProcessMove with Zod's own step): a step that brings the unit closer than the spacing to
    ///     another unit is refused. Exception: a leader never waits for its own minions (they are pushed aside). Then, in
    ///     order: queue behind a unit going the same way (shortened step, no overtaking weave), crowd arrival (on the last leg
    ///     of a MOVE, touching a standing unit of its own team that is nearer the goal = arrived), ask an idle blocker to step
    ///     aside (a Zod ScuffleUnits-style MOVE waypoint, zserver.cpp:974-1041), sidestep 45 / 90 deg keep-right, shortened
    ///     step, else hold. The step length never grows, so Zod's speeds stay exact.
    ///  2. PushApart (end of tick): pairs still closer than the spacing (a squad created on one pixel, a spawn, Zod's snap to
    ///     a waypoint) are pushed apart at up to 40 px/s: static units (cannons, empty vehicles) never move, a leader is never
    ///     pushed by its own minions, robots give way to vehicles, standing units to moving ones; a moving unit is never pushed
    ///     forward; pushes never enter blocked tiles (water for vehicles included).
    /// </summary>
    public sealed partial class ZPathing
    {
        static readonly double[] SideAngles = { 45, 90, -45, -90 };                  // +: turn right (pixel y points down)
        static readonly double[] UnstackAngles = { 180, 150, -150, 120, -120, 90, -90, 0 };   // behind the heading first: the squad walks as a file

        /// <summary>ProcessMove hook. (nx, ny) = Zod's next top-left (continuous); may be shortened / turned. False = hold.</summary>
        public bool Gate(ZObject a, ref double nx, ref double ny)
        {
            double sx0 = nx, sy0 = ny;
            try { return GateCore(a, ref nx, ref ny); }
            catch (Exception e) { Fail(e); nx = sx0; ny = sy0; return true; }                  // Zod's own step
        }

        bool GateCore(ZObject a, ref double nx, ref double ny)
        {
            var s = a.PS;
            if (!Separable(a) || Static(a) || a.PathInBuildingStage || a.FortRushOn) return true;   // boarding_nb_v1 DV34: rushing the fort door
            double ox = a.LocX + a.XOver, oy = a.LocY + a.YOver;
            double sx = nx - ox, sy = ny - oy, len = Math.Sqrt(sx * sx + sy * sy);
            if (len < 1e-9) return true;
            double hw = a.WidthPix * 0.5, hh = a.HeightPix * 0.5;

            if (!BridgeGate(a, ox, oy, nx, ny)) return false;                              // bridge decks: one direction at a time (ZBridges.cs)
            if (W.Time < s.ghostUntil) return true;                                        // passing through (FORCE_MOVE out of a factory, a dead jam), never onto a deck out of turn
            if (LaneConflict(a, ox, oy, nx, ny, true, out var laneBy)) { Hold(a, ZPathWait.Lane, laneBy); return false; }
            if (ResumeHold(a)) return false;                                               // pathing v3: a zoned unit held behind a unit walks again once the gap has opened (ZPathV3.cs)

            Near(a.CenterX, a.CenterY, MaxQuery, near);
            double hx = sx / len, hy = sy / len;
            if (VehicleFirst && a.Kind == ZKind.Vehicle && a.Waypoints.Count > 0) YieldAhead(a, hx, hy);   // vehicles first: robots of its own team clear the strip ahead (ZYield.cs)
            ZObject b; bool following;
            for (int k = 0; ; k++)
            {
                b = Blocker(a, ox + hw, oy + hh, nx + hw, ny + hh);
                if (b == null) { s.sideSince = -1; LaneEnter(a, ox, oy, nx, ny); return true; }
                double bl = Math.Sqrt(b.Dx * b.Dx + b.Dy * b.Dy);
                following = Moving(b) && !b.PathHeld && bl > 1e-6 && (b.Dx * hx + b.Dy * hy) > 0.5 * bl;
                if (!following && GroupQueue(a, b, hx, hy)) following = true;                  // DV44: behind a unit of its own group order: queue
                if (!following && MateQueue(a, b)) { if (NearGoalBlocked(a, b, out bool arrMate)) { if (arrMate) s.arrive = true; Hold(a, arrMate ? ZPathWait.Arrived : ZPathWait.Held, b); return false; } following = true; }   // DV44 (ZCommit.Follow.cs)
                if (following) break;
                if (CrowdArrival(a, b)) { s.arrive = true; Hold(a, ZPathWait.Arrived, b); return false; }
                if (NearGoalBlocked(a, b, out bool arrNear)) { if (arrNear) s.arrive = true; Hold(a, arrNear ? ZPathWait.Arrived : ZPathWait.Held, b); return false; }   // DV44 arrival tolerance
                if (k >= 3 || !TryDisplace(a, b, nx + hw, ny + hh, hx, hy)) break;          // a standing team-mate gives way: look again
                Near(a.CenterX, a.CenterY, MaxQuery, near);
            }
            if (!following) TryShove(a, b, hx, hy);
            // sidestep budget: 1.5 s of sidestepping without getting on = stop weaving for 2 s (hold, so a real deadlock shows)
            if (s.sideSince >= 0 && W.Time - s.sideSince > 1.5 && W.Time - s.lastProgress > 1.5) { s.noSideUntil = W.Time + 2; s.sideSince = -1; }
            if (!following && W.Time >= s.noSideUntil)
            {
                int side = W.Time < s.sideUntil ? s.sideSign : 1;                         // keep going round the same side (no left/right jitter)
                foreach (double deg0 in SideAngles)
                {
                    double deg = deg0 * side;
                    if (SideCommitted(a, s, deg)) continue;                                         // DV44: no left-right weave (ZCommit.Settle.cs)
                    double r = deg * Math.PI / 180, c = Math.Cos(r), sn = Math.Sin(r);
                    double tx = sx * c - sy * sn, ty = sx * sn + sy * c, cx = ox + tx, cy = oy + ty;
                    if (!BoxClear(a, cx, cy)) continue;
                    if (LaneConflict(a, ox, oy, cx, cy, false, out _) || BridgeBlocks(a, ox, oy, cx, cy)) continue;
                    if (Blocker(a, ox + hw, oy + hh, cx + hw, cy + hh) != null) continue;
                    nx = cx; ny = cy; s.reaim = true; s.wait = ZPathWait.Sidestep; s.heldBy = b;
                    s.sideSign = deg > 0 ? 1 : -1; s.sideUntil = W.Time + 1.0;
                    if (s.sideSince < 0) s.sideSince = W.Time;
                    LaneEnter(a, ox, oy, nx, ny);
                    return true;
                }
            }
            double lo = 0, hi = 1;                                                        // longest free part of the step
            for (int i = 0; i < 6; i++)
            {
                double f = (lo + hi) * 0.5;
                if (Blocker(a, ox + hw, oy + hh, ox + sx * f + hw, oy + sy * f + hh) == null) lo = f; else hi = f;
            }
            if (lo > 0.05) { nx = ox + sx * lo; ny = oy + sy * lo; s.wait = ZPathWait.Queue; s.heldBy = b; LaneEnter(a, ox, oy, nx, ny); return true; }
            Hold(a, ZPathWait.Held, b); HoldNote(a, b);                                   // pathing v3: the resume hysteresis starts (ZPathV3.cs)
            return false;
        }

        static void Hold(ZObject a, ZPathWait why, ZObject by) { a.PS.wait = why; a.PS.heldBy = by; }

        /// <summary>Nearest unit this step would approach inside the spacing (null = free). Own minions never block a leader.</summary>
        ZObject Blocker(ZObject a, double ocx, double ocy, double ncx, double ncy)
        {
            ZObject best = null; double bestD = double.MaxValue;
            double sx = ncx - ocx, sy = ncy - ocy, sl = Math.Sqrt(sx * sx + sy * sy);
            foreach (var b in near)
            {
                if (b == a || !Separable(b) || PairExempt(a, b)) continue;
                if (PassesOwn(a, b)) continue;                                                // vehicles first: a robot of its own team never blocks a vehicle on an order (ZYield.cs)
                // (a leader's own minions behind it never block it: the same-direction rule below lets it walk on)
                if (W.Time < b.PS.ghostUntil && !Static(b)) continue;
                if (b.Leader == a && b.InFormation) continue;                                 // DV24: the leader walks on, its minions in the V make room
                if (MatesPass(a, b) && (MateStepOk(a, b, ncx, ncy) || MateWaitsFor(a, b, ncx, ncy))) continue;   // DV44: the squad's follow keeps its members apart (ZCommit.Follow.cs); A/B fix 8: not a mate that waits for it (ZPathV3.Owner.cs)
                if (b.Leader == a && a.HumanDirectOrder) continue;                            // DV16: a squad on a direct order: the leader never queues behind its own minion
                double min = GateSpacing(a, b), bx = CX(b), by = CY(b);
                double dnx = ncx - bx, dny = ncy - by, dn2 = dnx * dnx + dny * dny;
                if (dn2 >= min * min) continue;
                double dox = ocx - bx, doy = ocy - by, do2 = dox * dox + doy * doy;
                if (dn2 >= do2 - 1e-9) continue;                                          // not getting closer: allowed
                if (sl > 1e-9 && Moving(b))
                {                                                                         // traffic that is not head-on (b's intended heading): right of way
                    double bl = Math.Sqrt(b.Dx * b.Dx + b.Dy * b.Dy);
                    if ((b.Dx * sx + b.Dy * sy) > -0.5 * bl * sl)
                    {                                                                     // ahead / behind along the pair's mean heading (a merge)
                        double mx = sx / sl + b.Dx / bl, my = sy / sl + b.Dy / bl, ml = Math.Sqrt(mx * mx + my * my);
                        double along = ((bx - ocx) * mx + (by - ocy) * my) / ml;
                        double hard = MinSpacing(a, b);
                        if (dn2 >= hard * hard)
                        {                                                                 // (never below the hard minimum)
                            if (along < -4) continue;                                     // b is behind: a walks on, b queues
                            if (along <= 4 && !Beats(b, a)) continue;                     // abreast: the lower priority one falls in behind
                        }
                    }
                }
                if (dn2 < bestD) { bestD = dn2; best = b; }
            }
            return best;
        }

        /// <summary>On the last leg of a MOVE, bumping into a standing unit of the own team that is at least as close to the
        /// goal = arrived (a group sent to one point fills the space around it instead of pushing for the exact pixel).</summary>
        bool CrowdArrival(ZObject a, ZObject b)
        {
            if (b.Owner != a.Owner) return false;
            if (a.PathMode == WpMode.ForceMove)
            {                                                                              // leaving a factory: any spot just outside the door will do,
                double ex = a.PathTargetX - CX(a), ey = a.PathTargetY - CY(a);             // but not on top of its squad (it came out stacked)
                return ex * ex + ey * ey <= 24 * 24 && ImpassCount(a, a.LocX, a.LocY) == 0 && !Overlapping(a);
            }
            if (a.PathMode != WpMode.Move) return false;
            var aw = a.Waypoints[0];
            if (aw.refId == RefStepAside || aw.refId == RefBackOff || aw.refId == RefBay) return false;   // making way / to a bay: must really get there
            if (b.PS.wait == ZPathWait.Lane || b.PS.wait == ZPathWait.Bridge) return false;
            bool standing = !Moving(b) || b.PathHeld;
            bool sameOrder = b.Waypoints.Count > 0 && Math.Abs(b.Waypoints[0].x - aw.x) + Math.Abs(b.Waypoints[0].y - aw.y) <= 16;
            if (!standing && !sameOrder) return false;
            var (gx, gy) = a.PathFinalPoint;
            double da = a.PathRemaining();                                                 // along the path: no arriving through a wall
            double db = Math.Sqrt((CX(b) - gx) * (CX(b) - gx) + (CY(b) - gy) * (CY(b) - gy));
            double radius = aw.refId == RefRegroup ? 32 : ArriveRadius;                     // a minion rejoining its leader gets close
            return da <= radius && db <= da + 2 && !ParkingInLane(a, gx, gy, da);         // never park on a bridge / in a gap short of the goal
        }

        // ------------------------------------------------------------------ walking units push standing team-mates aside
        readonly List<(ZObject u, double dx, double dy)> plan = new List<(ZObject, double, double)>();

        /// <summary>A walking unit pushes a standing team-mate out of its way (idle, parked, or held in a jam: Zod's own
        /// ScuffleUnits nudges idle units too, zserver.cpp:974-1041). b gives way sideways off a's line (else ahead of it) by
        /// exactly what a's step needs, never more than 3 px a tick; units in b's way give way in turn, three deep. Only a unit
        /// that outranks b pushes (idle units give way to anyone; else vehicle, leader, lower id; robots never push vehicles),
        /// so two units never push each other back and forth; nobody is pushed into a wall, onto water (vehicles), into a lane
        /// corridor or closer than the spacing to anybody. Enemies and fighting units are never pushed.</summary>
        bool TryDisplace(ZObject a, ZObject b, double ncx, double ncy, double hx, double hy)
        {
            if (!CanDisplace(a, b)) return false;
            plan.Clear();
            if (!PlanDisplace(a, a, b, ncx, ncy, hx, hy, GateSpacing(a, b) + 0.05, 0) || plan.Count == 0) { plan.Clear(); return false; }
            bool moved = true;
            for (int i = 0; i < plan.Count; i++)                                               // the far end of a chain moves first (planned first); if a link
            {                                                                                  // cannot move, the ones pushing it stay put
                var (u, dx, dy) = plan[i];
                if (!u.PathNudge(dx, dy)) { moved = false; break; }
                u.PS.lastPushed = W.Time; u.PS.Displaced++;
                if (u.IsMoving) u.PS.reaim = true;
            }
            plan.Clear();
            return moved;
        }

        bool CanDisplace(ZObject a, ZObject b)
        {
            if (b == null || b == a || b.Owner != a.Owner || !Separable(b) || Static(b) || !b.CanMove || b.PathInBuildingStage || PairExempt(a, b)) return false;
            if (MatesKeepPlace(a, b) || GroupSettledNear(a, b) || ArrivedVehicle(a, b)) return false;   // DV44: squad mates / arrived group mates are gone round, never shoved
            if (b.AttackObject != null || Walking(b) || W.Time < b.PS.ghostUntil) return false;
            if (Radius(a) < Radius(b)) return false;                                         // robots never push vehicles
            return b.Waypoints.Count == 0 || Beats(a, b);
        }

        (double x, double y) Planned(ZObject u)
        {
            foreach (var p in plan) if (p.u == u) return (CX(u) + p.dx, CY(u) + p.dy);
            return (CX(u), CY(u));
        }

        /// <summary>Plan b's way out of the pusher's way (pusher centre at (px, py), heading (hx, hy)); root = the walking unit
        /// whose rank counts for the whole chain.</summary>
        bool PlanDisplace(ZObject root, ZObject pusher, ZObject b, double px, double py, double hx, double hy, double spacing, int depth)
        {
            var (bx, by) = Planned(b);
            double rx = bx - px, ry = by - py, r2 = rx * rx + ry * ry;
            if (r2 >= spacing * spacing) return true;                                        // already clear
            double along = rx * hx + ry * hy, lx = rx - along * hx, ly = ry - along * hy, ll = Math.Sqrt(lx * lx + ly * ly);
            int sg = ((b.Id + depth) & 1) == 0 ? 1 : -1;
            for (int t = 0; t < 3; t++)
            {
                double ux, uy;
                if (t == 2) { ux = hx; uy = hy; }                                             // last: straight ahead of the pusher
                else if (ll > 0.5) { if (t == 1) continue; ux = lx / ll + hx * 0.35; uy = ly / ll + hy * 0.35; }   // its own side of the line
                else { int side = t == 0 ? sg : -sg; ux = -hy * side + hx * 0.35; uy = hx * side + hy * 0.35; }
                double ul = Math.Sqrt(ux * ux + uy * uy); ux /= ul; uy /= ul;
                double rf = rx * ux + ry * uy, disc = rf * rf - (r2 - spacing * spacing);
                if (disc < 0) continue;
                double m = DisplaceAmount(root, b, -rf + Math.Sqrt(disc) + 0.01);   // DV44: a standing unit moves at least 1 px
                if (m <= 0 || m > 3) continue;
                double dx = ux * m, dy = uy * m, nbx = bx + dx, nby = by + dy;
                int mark = plan.Count;
                if (!DisplaceSpotOk(root, b, nbx, nby, ux, uy, depth)) { plan.RemoveRange(mark, plan.Count - mark); continue; }
                double tdx = nbx - CX(b), tdy = nby - CY(b);
                int idx = plan.FindIndex(p => p.u == b);
                if (idx >= 0) plan[idx] = (b, tdx, tdy); else plan.Add((b, tdx, tdy));
                return true;
            }
            return false;
        }

        bool DisplaceSpotOk(ZObject root, ZObject b, double nbx, double nby, double ux, double uy, int depth)
        {
            double lx = nbx - b.WidthPix * 0.5, ly = nby - b.HeightPix * 0.5;
            if (!BoxAllowed(b, b.LocX, b.LocY, (int)Math.Floor(lx), (int)Math.Floor(ly))) return false;
            foreach (var L in Lanes)
                if (BoxInCorridor(L, lx, ly, b.WidthPix, b.HeightPix) && !BoxInCorridor(L, b.LocX + b.XOver, b.LocY + b.YOver, b.WidthPix, b.HeightPix)) return false;
            Near((int)nbx, (int)nby, MaxQuery, near3);
            var around = new List<ZObject>(near3);
            foreach (var c in around)
            {
                if (c == b || c == root || !Separable(c) || PairExempt(b, c)) continue;
                var (cx, cy) = Planned(c);
                double min = MinSpacing(b, c), dn2 = (cx - nbx) * (cx - nbx) + (cy - nby) * (cy - nby);
                if (dn2 >= min * min) continue;
                var (bx, by) = Planned(b);
                if (dn2 >= (cx - bx) * (cx - bx) + (cy - by) * (cy - by) - 1e-9) continue;   // not getting closer to it
                if (depth >= MaxPushDepth || !CanDisplace(root, c) || Radius(b) < Radius(c)) return false;
                if (!PlanDisplace(root, b, c, nbx, nby, ux, uy, min + 0.05, depth + 1)) return false;
            }
            return true;
        }
        readonly List<ZObject> near3 = new List<ZObject>();
        const int MaxPushDepth = 3;

        /// <summary>Some other unit is closer than the minimum spacing (exempt pairs included: a squad walking out stacked).</summary>
        bool Overlapping(ZObject a)
        {
            Near(a.CenterX, a.CenterY, MaxQuery, near2);
            foreach (var o in near2)
            {
                if (o == a || !Separable(o)) continue;
                double min = MinSpacing(a, o) - 1, dx = CX(o) - CX(a), dy = CY(o) - CY(a);
                if (dx * dx + dy * dy < min * min) return true;
            }
            return false;
        }

        /// <summary>An idle unit in the way walks aside: a MOVE waypoint like Zod's ScuffleUnits (not player-given, the bot
        /// still sees the unit as idle). Candidates: its own side of our path, the other side, ahead-and-aside, ahead; out of a
        /// lane zone it walks to the lane end. Enemies that are fighting are not moved.</summary>
        void TryShove(ZObject a, ZObject b, double hx, double hy)
        {
            if (b.PS.wait == ZPathWait.Lane && OnLaneMoving(a)) { MakeWay(a, b, hx, hy); return; }   // a lane waiter blocks traffic leaving the lane
            if (b.PS.wait == ZPathWait.Bridge && BridgeParked(b)) { MakeWay(a, b, hx, hy); return; }   // a unit waiting for a bridge turn steps aside
            if (b.Waypoints.Count > 0 || Static(b) || !b.CanMove || W.Time < b.PS.nextShove) return;
            if (OwnHull(b, a) && a.PathMode != WpMode.ForceMove && !InNarrowArea(b)) return;      // vehicles first: a robot goes round a standing vehicle of its team, it never sends it aside (ZYield.cs; a robot leaving a building still may, and a vehicle parked on a bridge / in a gap still clears it)
            if (MatesKeepPlace(a, b)) return;                                                  // DV44: an arrived leader / mate is not sent walking
            if (GroupSettledNear(a, b) || ArrivedVehicle(a, b)) return;                        // DV44: nor a unit of its own group order that has arrived
            if (b.Owner != a.Owner && b.AttackObject != null) return;
            b.PS.nextShove = W.Time + ShoveInterval;
            var lane = LaneAt(b.CenterX, b.CenterY);
            if (lane != null && EvacuateLane(a, b, lane, hx, hy)) return;
            double rx = CX(b) - CX(a), ry = CY(b) - CY(a);
            double cross = hx * ry - hy * rx;                                            // > 0: b is right of our heading
            int side = cross > 0.5 ? 1 : cross < -0.5 ? -1 : ((b.Id & 1) == 0 ? 1 : -1);
            double px = -hy * side, py = hx * side;                                       // unit vector to b's side
            double sp = GateSpacing(a, b) + 8, bx = CX(b), by = CY(b);
            var cand = new List<(double x, double y)>
            {
                (bx + px * sp, by + py * sp), (bx - px * sp, by - py * sp),
                (bx + hx * sp + px * sp, by + hy * sp + py * sp), (bx + hx * sp * 1.8, by + hy * sp * 1.8)
            };
            bool nr = NoRocks(b); int c = Reach.UnitComp(b, nr);
            if (c >= 0)
            {                                                                                  // then the nearest free spot off a's line, up to 5 tiles
                var (btx, bty) = ZReach.FootTile(b.LocX, b.LocY);
                for (int ring = 2; ring <= 5; ring++)
                    for (int ty = bty - ring; ty <= bty + ring; ty++)
                        for (int tx = btx - ring; tx <= btx + ring; tx++)
                        {
                            if (Math.Max(Math.Abs(tx - btx), Math.Abs(ty - bty)) != ring) continue;
                            var (x, y) = ZReach.FootCentre(tx, ty, b.IsRobot);
                            double off = Math.Abs((x - CX(a)) * hy - (y - CY(a)) * hx);      // distance from a's line of travel
                            if (off < GateSpacing(a, b)) continue;
                            cand.Add((x, y));
                        }
            }
            foreach (var (x, y) in cand)
            {
                int ix = (int)Math.Round(x), iy = (int)Math.Round(y);
                if (c >= 0 && !Reach.PointOk(ix, iy, b.IsRobot, nr, c)) continue;
                if (c < 0 && !BoxAllowed(b, b.LocX, b.LocY, ix - (b.WidthPix >> 1), iy - (b.HeightPix >> 1))) continue;
                if (Occupied(ix, iy, b, a) || KeepClearAt(ix, iy) != null || WayBlocked(b, ix, iy, a) || !HelperLegOk(b, ix, iy)) continue;
                b.Waypoints.Add(ZWaypoint.Move(ix, iy, false, RefStepAside));
                b.PS.Shoves++;
                Log(b, $"steps aside for {a.Type}#{a.Id} to ({ix},{iy})");
                return;
            }
        }

        /// <summary>An idle unit parked in a lane zone (bridge deck, gap) that blocks traffic leaves the zone: to the end in
        /// the blocked unit's direction of travel first, else the other end, at the first free reachable spot.</summary>
        bool EvacuateLane(ZObject a, ZObject b, ZLane lane, double hx, double hy)
        {
            bool nr = NoRocks(b); int c = Reach.UnitComp(b, nr);
            if (c < 0) return false;
            double along = lane.Vertical ? hy : hx;
            int half = Radius(b) + 8;
            for (int pass = 0; pass < 2; pass++)
            {
                bool plus = (along >= 0) == (pass == 0);
                double mid = lane.Vertical ? (lane.X0 + lane.X1) * 0.5 : (lane.Y0 + lane.Y1) * 0.5;
                double edge = lane.Vertical ? (plus ? lane.ZY1 : lane.ZY0) : (plus ? lane.ZX1 : lane.ZX0);
                for (int step = 0; step < 4; step++)
                    foreach (int off in new[] { 0, 16, -16, 32, -32 })
                    {
                        double d = edge + (plus ? 1 : -1) * (half + step * 16);
                        int x = (int)Math.Round(lane.Vertical ? mid + off : d), y = (int)Math.Round(lane.Vertical ? d : mid + off);
                        if (!Reach.PointOk(x, y, b.IsRobot, nr, c) || Occupied(x, y, b, a) || WayBlocked(b, x, y, a) || !HelperLegOk(b, x, y)) continue;
                        b.Waypoints.Add(ZWaypoint.Move(x, y, false, RefStepAside));
                        b.PS.Shoves++;
                        Log(b, $"leaves {lane} for {a.Type}#{a.Id}: to ({x},{y})");
                        return true;
                    }
            }
            return false;
        }

        /// <summary>A unit waiting for its turn at a lane stands in the way of traffic coming out of the lane: it steps aside
        /// (a MOVE inserted in front of its order, like Zod's inserted DODGE) and comes back for its turn afterwards.</summary>
        /// <summary>Deadlock loop: the loser steps aside for the unit it holds up (idle: Zod-scuffle MOVE; with an order: a
        /// MOVE in front of it, its order resumes afterwards).</summary>
        bool StepAsideFor(ZObject a, ZObject b)
        {
            double hx = a.Dx, hy = a.Dy, l = Math.Sqrt(hx * hx + hy * hy);
            if (l < 1e-6) { hx = CX(b) - CX(a); hy = CY(b) - CY(a); l = Math.Sqrt(hx * hx + hy * hy); if (l < 1e-6) { hx = 0; hy = -1; l = 1; } }
            hx /= l; hy /= l;
            int before = b.PS.Shoves;
            b.PS.nextShove = -1;
            if (b.Waypoints.Count == 0) TryShove(a, b, hx, hy); else MakeWay(a, b, hx, hy);
            return b.PS.Shoves != before;
        }

        void MakeWay(ZObject a, ZObject b, double hx, double hy)
        {
            if (W.Time < b.PS.nextShove || !b.CanOverwriteWP() || b.Waypoints.Count == 0) return;
            bool redirect = b.Waypoints[0].refId == RefStepAside;                             // already stepping aside: pick a new spot
            b.PS.nextShove = W.Time + ShoveInterval;
            bool nr = NoRocks(b); int c = Reach.UnitComp(b, nr); if (c < 0) return;
            double rx = CX(b) - CX(a), ry = CY(b) - CY(a);
            int side = hx * ry - hy * rx > 0 ? 1 : -1;
            double px = -hy * side, py = hx * side, bx = CX(b), by = CY(b);
            foreach (double k in new[] { 1.0, 1.6, 2.2 })
                foreach (int sgn in new[] { 1, -1, 0 })
                {
                    double d = (GateSpacing(a, b) + 8) * k;
                    int x = (int)Math.Round(bx + (sgn != 0 ? px * sgn * d : hx * d)), y = (int)Math.Round(by + (sgn != 0 ? py * sgn * d : hy * d));   // aside, else ahead of a
                    if (!Reach.PointOk(x, y, b.IsRobot, nr, c) || Occupied(x, y, b, a) || KeepClearAt(x, y) != null || WayBlocked(b, x, y, a) || !HelperLegOk(b, x, y)) continue;
                    if (redirect) b.Waypoints.RemoveAt(0);
                    b.Waypoints.Insert(0, ZWaypoint.Move(x, y, false, RefStepAside));
                    b.PS.Shoves++;
                    Log(b, $"makes way for {a.Type}#{a.Id} leaving the lane: to ({x},{y})");
                    return;
                }
        }

        /// <summary>A helper move of u straight to (x, y) would run into a unit that stands (idle, parked or held; not skip2):
        /// the spot is free but cannot be reached now, so a different one is taken instead of a move that stalls at once.</summary>
        bool WayBlocked(ZObject u, int x, int y, ZObject skip2)
        {
            double x0 = CX(u), y0 = CY(u), dx = x - x0, dy = y - y0, l2 = dx * dx + dy * dy;
            if (l2 < 1e-9) return false;
            Near((int)((x0 + x) * 0.5), (int)((y0 + y) * 0.5), MaxQuery + (int)Math.Sqrt(l2) / 2, near2);
            foreach (var o in near2)
            {
                if (o == u || o == skip2 || !Separable(o) || PairExempt(u, o)) continue;
                if (Moving(o) && !o.PathHeld) continue;                                     // walking on: it will not be there
                double ox = CX(o) - x0, oy = CY(o) - y0;
                double t = Math.Max(0, Math.Min(1, (ox * dx + oy * dy) / l2));
                double ex = ox - t * dx, ey = oy - t * dy, dmin = Math.Sqrt(ex * ex + ey * ey), dnow = Math.Sqrt(ox * ox + oy * oy);
                if (dmin < WaySpacing(u, o) && dmin < dnow - 0.5) return true;             // the way gets closer to it than allowed (DV44: the gate's spacing, ZCommit.Settle.cs)
            }
            return false;
        }

        /// <summary>Another unit (not skip1 / skip2) stands within the spacing of point (x, y) for unit u.</summary>
        bool Occupied(int x, int y, ZObject u, ZObject skip2)
        {
            Near(x, y, MaxQuery, near2);
            foreach (var o in near2)
            {
                if (o == u || o == skip2 || !Separable(o)) continue;
                double min = MinSpacing(u, o), dx = CX(o) - x, dy = CY(o) - y;
                if (dx * dx + dy * dy < min * min) return true;
            }
            foreach (var o in units)
            {                                                                              // a spot another unit is already walking to
                if (o == u || o.Waypoints.Count == 0) continue;                            // (step-aside, back-off, regroup) is taken too
                var wp = o.Waypoints[0];
                if (wp.refId > RefStepAside || wp.refId < RefBay) continue;                  // (a unit's waiting bay is taken too, ZBridges.cs)
                double min = MinSpacing(u, o), dx = wp.x - x, dy = wp.y - y;
                if (dx * dx + dy * dy < min * min) return true;
            }
            return false;
        }

        /// <summary>Units standing on one pixel (Zod creates a squad, a factory output or an ejected crew on the same
        /// coordinates, zserver.cpp:623-657): every unit but the first moves at once to the nearest free spot around the stack
        /// (8 directions from its heading, spacing + 1 px, free of walls and units); the view eases robots there.</summary>
        void Unstack()
        {
            foreach (var a in units)
            {
                if (Static(a) || !Separable(a) || a.PathInBuildingStage || a.FortRushOn) continue;   // boarding_nb_v1 DV34
                Near(a.CenterX, a.CenterY, MaxQuery, near);
                ZObject b = null;
                foreach (var c in near)
                    if (c != a && c.Id < a.Id && Separable(c) && !PairExempt(a, c) && !OwnHull(a, c) && !PassesOwn(c, a) && Math.Abs(CX(c) - CX(a)) < 1.5 && Math.Abs(CY(c) - CY(a)) < 1.5) { b = c; break; }   // (vehicles first: a vehicle on the pixel of a robot of its team is never the one that is moved off it, ZYield.cs)
                if (b == null) continue;
                double vl = Math.Sqrt(a.Dx * a.Dx + a.Dy * a.Dy), hx = vl > 1e-6 ? a.Dx / vl : 0, hy = vl > 1e-6 ? a.Dy / vl : -1;
                double r = GateSpacing(a, b) + 1;
                int flip = ((a.MemberIndex + a.Id) & 1) == 0 ? 1 : -1;
                bool done = false, walking = Walking(a);
                for (int k = 0; k < 64 && !done; k++)
                {                                                                             // 8 directions x 1, then 16 x 1.5 / 2 / 2.5 spacings
                    double deg = k < 8 ? UnstackAngles[k] : (k < 24 ? UnstackAngles2[k - 8] : k < 40 ? UnstackAngles2[k - 24] : UnstackAngles2[(k - 40) % 16]);
                    if (walking && Math.Abs(deg) < 90) continue;
                    double ang = deg * flip * Math.PI / 180;                                  // behind, then beside, never in front first
                    double ux = hx * Math.Cos(ang) - hy * Math.Sin(ang), uy = hx * Math.Sin(ang) + hy * Math.Cos(ang);
                    double rr = r * (k < 8 ? 1 : k < 24 ? 1.5 : k < 40 ? 2 : 2.5);
                    if (k >= 56 || (k >= 8 && !a.IsRobot && k >= 24)) break;                   // vehicles: 1 and 1.5 spacings only
                    double dx = ux * rr, dy = uy * rr;
                    int nlx = (int)Math.Floor(a.LocX + a.XOver + dx), nly = (int)Math.Floor(a.LocY + a.YOver + dy);
                    if (!BoxAllowed(a, a.LocX, a.LocY, nlx, nly)) continue;
                    double nx = CX(a) + dx, ny = CY(a) + dy; bool free = true;
                    Near((int)nx, (int)ny, MaxQuery, near2);                                  // (the candidate may lie beyond a's own neighbourhood)
                    foreach (var c in near2)
                    {
                        if (c == a || !Separable(c) || PairExempt(a, c)) continue;
                        double ddx = CX(c) - nx, ddy = CY(c) - ny, m = MinSpacing(a, c);
                        if (ddx * ddx + ddy * ddy < m * m) { free = false; break; }
                    }
                    if (!free || KeepClearAt((int)nx, (int)ny) != null && KeepClearAt(a.CenterX, a.CenterY) == null) continue;
                    a.PathNudge(dx, dy);
                    if (a.IsMoving) a.PS.reaim = true;
                    done = true;
                }
                if (!done && a.Waypoints.Count == 0 && a.CanMove && W.Time >= a.PS.nextShove) WalkOutOfOverlap(a);   // no room at all: walk out
            }
        }
        static readonly double[] UnstackAngles2 = { 180, 157.5, -157.5, 135, -135, 112.5, -112.5, 90, -90, 67.5, -67.5, 45, -45, 22.5, -22.5, 0 };

        /// <summary>End of tick: separate pairs that are still closer than the spacing.</summary>
        void PushApart(double dt)
        {
            foreach (var o in units) { o.PS.pushX = o.PS.carryX; o.PS.pushY = o.PS.carryY; o.PS.carryX = o.PS.carryY = 0; }   // + pressure handed on last tick
            foreach (var a in units)
            {
                Near(a.CenterX, a.CenterY, MaxQuery, near);
                foreach (var b in near)
                {
                    if (b.Id <= a.Id || !Separable(a) || !Separable(b) || PairExempt(a, b)) continue;
                    if (MinionsTogether(a, b) && !MatesOverlap(a, b)) continue;                // DV44: spaced by the squad's follow (MateCap); a real overlap is still pushed
                    double min = MinSpacing(a, b), dx = CX(a) - CX(b), dy = CY(a) - CY(b), d2 = dx * dx + dy * dy;
                    double lim = PushStart(a, b, min);                                       // DV44: dead-band + hysteresis (ZCommit.Settle.cs); off: min - 0.25
                    if (d2 >= lim * lim) continue;
                    double wa = PushShare(a, b), wb = PushShare(b, a);
                    if (wa + wb <= 0) continue;
                    double d = Math.Sqrt(d2), ux, uy;
                    if (d < 0.01) (ux, uy) = CoincidentDir(a, b); else { ux = dx / d; uy = dy / d; }
                    double corr = PushTarget(a, b, min) - d, fa = wa / (wa + wb), fb = wb / (wa + wb);   // DV44: a push of two settled units runs until the pair is clear
                    a.PS.pushX += ux * corr * fa; a.PS.pushY += uy * corr * fa;
                    b.PS.pushX -= ux * corr * fb; b.PS.pushY -= uy * corr * fb;
                }
            }
            foreach (var o in units)
            {
                var s = o.PS; double px = s.pushX, py = s.pushY;
                s.pinned = false;
                if (px * px + py * py < 1e-10) continue;
                double cap = (PushSpeed + 4 * Math.Sqrt(px * px + py * py) / Math.Max(dt, 1e-6) * 0.02) * dt;   // deep overlaps (units created on one pixel) open faster
                double fhx = 0, fhy = 0;                                                  // heading of a walking unit (0 = standing)
                if (Walking(o))
                {                                                                         // a walking unit is only ever pushed sideways: never faster
                    double vl = Math.Sqrt(o.Dx * o.Dx + o.Dy * o.Dy), hx = o.Dx / vl, hy = o.Dy / vl, f = px * hx + py * hy;   // than Zod, never
                    double full = Math.Sqrt(px * px + py * py);                           // held back (no push-back livelock with the gate)
                    px -= f * hx; py -= f * hy;
                    double lat = Math.Sqrt(px * px + py * py);
                    if (lat < 0.3 * full) { int sg = (o.Id & 1) == 0 ? 1 : -1; px = -hy * full * sg; py = hx * full * sg; }
                    fhx = hx; fhy = hy;
                }
                double l = Math.Sqrt(px * px + py * py);
                if (l < 1e-6) continue;
                if (l > cap) { px *= cap / l; py *= cap / l; }
                // axis-only or turned lateral push has a forward part, which would move the unit faster than its Zod speed)
                if (TryNudgeFwd(o, px, py, fhx, fhy) || TryNudgeFwd(o, px, 0, fhx, fhy) || TryNudgeFwd(o, 0, py, fhx, fhy) || TryNudgeTurned(o, px, py, fhx, fhy)) { s.lastPushed = W.Time; if (o.IsMoving) s.reaim = true; }
                else
                {
                    s.pinned = true; s.pinnedUntil = W.Time + 0.3;
                    if (o.Waypoints.Count == 0 && o.CanMove && W.Time >= s.nextShove) WalkOutOfOverlap(o);
                }
            }
        }

        /// <summary>An idle unit that still overlaps another and cannot be pushed apart where it stands (a squad that left its
        /// factory stacked into a crowd at the door) walks to the nearest free spot within 5 tiles (a Zod-scuffle MOVE).</summary>
        void WalkOutOfOverlap(ZObject b)
        {
            bool deep = false;                                                             // a real overlap (not a push residue of a few tenths)
            Near(b.CenterX, b.CenterY, MaxQuery, near2);
            foreach (var o in near2)
            {
                if (o == b || !Separable(o) || PairExempt(b, o)) continue;
                double min = MinSpacing(b, o) - 1.5, dx = CX(o) - CX(b), dy = CY(o) - CY(b);
                if (dx * dx + dy * dy < min * min) { deep = true; break; }
            }
            if (!deep) return;
            b.PS.nextShove = W.Time + ShoveInterval;
            bool nr = NoRocks(b); int c = Reach.UnitComp(b, nr);
            if (c < 0) return;
            var (btx, bty) = ZReach.FootTile(b.LocX, b.LocY);
            for (int pass = 0; pass < 2; pass++)                                             // a spot it can walk straight to, else any free spot
            for (int ring = 1; ring <= 5; ring++)
                for (int ty = bty - ring; ty <= bty + ring; ty++)
                    for (int tx = btx - ring; tx <= btx + ring; tx++)
                    {
                        if (Math.Max(Math.Abs(tx - btx), Math.Abs(ty - bty)) != ring) continue;
                        var (x, y) = ZReach.FootCentre(tx, ty, b.IsRobot);
                        if (!Reach.PointOk(x, y, b.IsRobot, nr, c) || Occupied(x, y, b, null) || KeepClearAt(x, y) != null || (pass == 0 && WayBlocked(b, x, y, null))) continue;
                        b.Waypoints.Add(ZWaypoint.Move(x, y, false, RefStepAside));
                        b.PS.Shoves++;
                        Log(b, $"overlaps and cannot be pushed apart here: walks to ({x},{y})");
                        return;
                    }
        }

        /// <summary>A push may not bring the unit closer to any third unit that is already inside the spacing (else crowds
        /// compress), nor into blocked tiles.</summary>
        bool TryNudge(ZObject o, double dx, double dy)
        {
            if (Math.Abs(dx) + Math.Abs(dy) < 1e-9) return false;
            double ox = CX(o), oy = CY(o), nx = ox + dx, ny = oy + dy;
            Near(o.CenterX, o.CenterY, MaxQuery, near2);
            foreach (var c in near2)
            {
                if (c == o || !Separable(c) || PairExempt(o, c)) continue;
                double min = MinSpacing(o, c), cx = CX(c), cy = CY(c);
                double dn = (nx - cx) * (nx - cx) + (ny - cy) * (ny - cy);
                if (dn >= min * min) continue;
                double dd = (ox - cx) * (ox - cx) + (oy - cy) * (oy - cy);
                if (dn < dd - 1e-9)
                {                                                                         // hand the pressure on: an idle neighbour makes room next tick
                    if (!Static(c) && !Walking(c) && W.Time >= c.PS.pinnedUntil && !NoCarry(c))   // DV44: standing units: their own dead-band push
                    {
                        double l = Math.Sqrt(dn), need = min - l;
                        if (l > 1e-6 && need > 0) { c.PS.carryX += (cx - nx) / l * Math.Min(need, 0.5); c.PS.carryY += (cy - ny) / l * Math.Min(need, 0.5); }
                    }
                    return false;
                }
            }
            return o.PathNudge(dx, dy);
        }

        /// <summary>A push blocked by a wall or a third unit slides: the same push turned 45 / 90 deg either way (for a walking
        /// unit, heading (hx, hy), only turns without a forward part).</summary>
        bool TryNudgeTurned(ZObject o, double px, double py, double hx, double hy)
        {
            foreach (double deg in new[] { 45.0, -45.0, 90.0, -90.0 })
            {
                double r = deg * Math.PI / 180, c = Math.Cos(r), sn = Math.Sin(r);
                if (TryNudgeFwd(o, px * c - py * sn, px * sn + py * c, hx, hy)) return true;
            }
            return false;
        }

        /// <summary>TryNudge that refuses any push with a forward part along the heading (hx, hy) of a walking unit ((0, 0) =
        /// standing: any direction). A walking unit only ever takes Zod's own step forward.</summary>
        bool TryNudgeFwd(ZObject o, double dx, double dy, double hx, double hy) => dx * hx + dy * hy <= 1e-9 && TryNudge(o, dx, dy);

        /// <summary>How much of a pair's correction unit a takes against b (0 = never moves).</summary>
        double PushShare(ZObject a, ZObject b)
        {
            if (Static(a) || W.Time < a.PS.pinnedUntil) return 0;
            if (PassesOwn(a, b) || (OwnHull(a, b) && !InNarrowArea(a))) return 0;         // vehicles first: a vehicle takes no push from robots of its own team: on an order anywhere, standing outside bridges / gaps (ZYield.cs)
            if (Static(b) || W.Time < b.PS.pinnedUntil) return 1;
            if (ArrivedHolds(a, b)) return 0; if (ArrivedHolds(b, a)) return 1;             // DV44: arrived vehicles are never nudged by traffic (ZCommit.Settle.cs)
            bool wa = Walking(a), wb = Walking(b);
            if (wa != wb) return wa ? 0 : 1;                                              // a standing unit makes room for a walking one
            if (b.Leader == a) return 0;                                                  // a leader is never pushed by its own minions
            if (a.Leader == b) return 1;
            return VehicleHolds(a, b) ? 0 : a.Kind == ZKind.Vehicle ? 0.2 : 1.0;   // DV44: the robot steps away from a vehicle                                   // robots give way to vehicles
        }

        /// <summary>Actually walking this tick (not standing, not held in a queue).</summary>
        static bool Walking(ZObject o) => Moving(o) && !o.PathHeld && o.PS.wait != ZPathWait.Held && o.PS.wait != ZPathWait.Lane && o.PS.wait != ZPathWait.Bridge;

        /// <summary>Two units on the same pixel: moving the same way -> side by side (across the heading), else a fixed
        /// direction from the ids.</summary>
        static (double, double) CoincidentDir(ZObject a, ZObject b)
        {
            double al = Math.Sqrt(a.Dx * a.Dx + a.Dy * a.Dy), bl = Math.Sqrt(b.Dx * b.Dx + b.Dy * b.Dy);
            int sign = ((a.Id + b.Id) & 1) == 0 ? 1 : -1;
            if (al > 1e-6 && bl > 1e-6 && (a.Dx * b.Dx + a.Dy * b.Dy) > 0.5 * al * bl)
                return (-a.Dy / al * sign, a.Dx / al * sign);
            double ang = ((a.Id * 7919 + b.Id * 104729) % 360) * Math.PI / 180;
            return (Math.Cos(ang), Math.Sin(ang));
        }
    }
}
