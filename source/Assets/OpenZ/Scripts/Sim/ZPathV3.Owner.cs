using System;
using System.Collections.Generic;

namespace OpenZ.Sim
{
    /// <summary>
    /// Pathing v3 fixes (smoother pathing on by default; the old planner stays on bridges and bottlenecks), found by the headless
    /// OFF / ON A/B:
    ///  1 DV44 group spots by WALKING distance: a spot the group cannot reach within GroupWalkSlack of its straight distance from
    ///    the clicked point (the far side of a wall, a gap or a river) is used only when no other spot is free (the L02 30-unit order
    ///    sent a missile launcher to a spot round a 2-tile gap: it never arrived).
    ///  2 DV44 group spots off the lanes: a spot whose unit box touches a lane's zone (Candidates checks only the centre against the
    ///    keep-clear area) is used only when no other spot is free.
    ///  3 (ZPathV3.cs TestR3: bit 2048 cleared) the hand-back rules - the strict file order of two standing mates and a stalled
    ///    follower's own path after 1 s - hold for EVERY following squad, not only one just out of a zone (two mates level at the
    ///    L17 building corner held each other for 3.5 s while their leader walked on; the L17 30-unit order: 34 give-ups -> 9).
    ///  4 a following minion whose step a wall corner blocks slides along the free axis first (x or y alone, the bigger one first)
    ///    and stays in the follow; only when both axes are blocked does it leave for its own path (L02: the back-and-forth squad's
    ///    stuck step-aside and give-up are gone, 0 reversals on the building route).
    ///  8 the gate lets a following minion step past a mate that stands in the follow WAITING FOR IT (the mate's file predecessor is
    ///    this minion) unless the step comes closer than MateWaitClose x the spacing: at the L17 building corner the wall on one side
    ///    and that mate on the other (MateStepOk's 13 px floor) pinned the minion, the mate waited for it, the file behind both stood 6 s.
    /// Every rule here runs only with ZWorld.PathV3Deviation on (CommitOn): off = the live pathing bit for bit. Integer tiles, fixed
    /// order, no RNG, no clock: deterministic.
    /// </summary>
    public sealed partial class ZPathing
    {
        /// <summary>A/B fixes, one bit each (headless A/B only; the game runs the default): 1 DV44 group spots by walking distance,
        /// 2 DV44 group spots off the lanes, 4 the follow's wall slide, 8 the gate past a mate that waits for it; round 2: 16 the follow's
        /// mutual wait is a waiting loop, 32 a stuck helper move is re-planned once before it is dropped.</summary>
        internal static int TestOwner = 0x3F;
        internal static bool OwnerFix(int bit) => (TestOwner & bit) != 0;
        /// <summary>A group spot whose walk from the clicked point (8-way tile steps, no corner cutting) is longer than its straight
        /// (Chebyshev) tile distance + max(GroupWalkSlack, half of it), or that the walk does not reach within GroupWalkWindow tiles,
        /// is a spot of last resort.</summary>
        public const int GroupWalkSlack = 3, GroupWalkWindow = 14;

        int[] walkDist, walkQueue;

        /// <summary></summary>
        internal void GroupWalkPenalty(List<(int x, int y, long d)> list, int px, int py, bool robot, bool nr, int comp)
        {
            if (!OwnerFix(1 | 2) || list.Count == 0) return;
            const int R = GroupWalkWindow, N = 2 * R + 1;
            if (walkDist == null) { walkDist = new int[N * N]; walkQueue = new int[N * N]; }
            var (ox, oy) = ZReach.FootTileAt(px, py, robot);
            int sx = ox, sy = oy;
            if (Reach.Comp(sx, sy, robot, nr) != comp) (sx, sy) = ZReach.FootTileAt(list[0].x, list[0].y, robot);   // (clicked on a building / water: from the nearest spot)
            bool walkOk = OwnerFix(1) && Math.Abs(sx - ox) <= R && Math.Abs(sy - oy) <= R && Reach.Comp(sx, sy, robot, nr) == comp;
            if (walkOk)
            {                                                                            // breadth-first walk over the unit's own component
                for (int i = 0; i < walkDist.Length; i++) walkDist[i] = -1;
                int head = 0, tail = 0, c0 = (sy - oy + R) * N + (sx - ox + R);
                walkDist[c0] = 0; walkQueue[tail++] = c0;
                while (head < tail)
                {
                    int cell = walkQueue[head++], cx = cell % N, cy = cell / N, cd = walkDist[cell];
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            int nx = cx + dx, ny = cy + dy;
                            if (nx < 0 || ny < 0 || nx >= N || ny >= N || walkDist[ny * N + nx] >= 0) continue;
                            int tx = ox - R + nx, ty = oy - R + ny;
                            if (Reach.Comp(tx, ty, robot, nr) != comp) continue;
                            if (dx != 0 && dy != 0 && (Reach.Comp(tx - dx, ty, robot, nr) != comp || Reach.Comp(tx, ty - dy, robot, nr) != comp)) continue;   // (no corner cutting)
                            walkDist[ny * N + nx] = cd + 1; walkQueue[tail++] = ny * N + nx;
                        }
                }
            }
            bool changed = false;
            for (int i = 0; i < list.Count; i++)
            {
                var (x, y, d) = list[i];
                if (d >= (1L << 40)) continue;
                bool far = false;
                if (walkOk)
                {
                    var (tx, ty) = ZReach.FootTileAt(x, y, robot);
                    int ix = tx - ox + R, iy = ty - oy + R;
                    int walk = ix < 0 || iy < 0 || ix >= N || iy >= N ? -1 : walkDist[iy * N + ix];
                    int cheb = Math.Max(Math.Abs(tx - sx), Math.Abs(ty - sy));
                    far = walk < 0 || walk > cheb + Math.Max(GroupWalkSlack, cheb / 2);
                }
                bool lane = OwnerFix(2) && SpotOnLane(x, y, robot);
                if (!far && !lane) continue;
                list[i] = (x, y, d + (1L << 40)); changed = true;
                if (far) GroupWalkFar++; else GroupLaneSpots++;
            }
            if (changed) list.Sort((a, b) => a.d != b.d ? a.d.CompareTo(b.d) : a.y != b.y ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x));
        }

        /// <summary>
        /// a's step unless the step brings the two closer than MateWaitClose x the spacing (a wall corner on one side and that mate on the
        /// other pinned a minion for 4-5 s: neither could move first).</summary>
        internal bool MateWaitsFor(ZObject a, ZObject b, double ncx, double ncy)
        {
            if (!OwnerFix(8) || !b.IsMinion || !b.PC.standing || b.PC.predId != a.Id) return false;
            double min = MinSpacing(a, b) * MateWaitClose, bx = CX(b), by = CY(b), dn2 = (ncx - bx) * (ncx - bx) + (ncy - by) * (ncy - by);
            return dn2 >= min * min;
        }
        public const double MateWaitClose = 0.66;

        /// <summary></summary>
        bool SpotOnLane(int x, int y, bool robot)
        {
            int h = robot ? 8 : 16;
            foreach (var L in LanesNear(x, y)) if (BoxInZone(L, x - h, y - h, 2 * h, 2 * h)) return true;
            return false;
        }

        /// <summary>
        /// </summary>
        public int GroupWalkFar, GroupLaneSpots, FollowSlides, FollowLoops, HelperReplans;

        /// <summary>Round 2 fix 16: a gate hold this long (DeadlockDelay) on a following minion by the mate that waits for it is a waiting loop.</summary>
        public const int FollowLoopTicks = 60;

        /// <summary>Round 2 fix 16 (v3 follow only; EndTick, right after ResolveDeadlocks): the follow's mutual wait. Minion b is held by the
        /// gate because of mate m (b.PS.heldBy == m), while m's own follow holds m at 0 (MateCap) because b is ahead of it in the squad's
        /// order: m waits for b, b waits for m, and neither is in ResolveDeadlocks' chain (a following minion never counts as stuck, and
        /// a capped follower has no "held by" link). This is that link: the two are a waiting loop, broken like ResolveDeadlocks breaks one -
        /// m, the one standing in b's way, backs up straight away from b (else steps off b's line), with the same 2 s yield; its follow
        /// resumes after the helper move (L17 back-and-forth: #445 capped by #443, #443 gate-held by #445 at the building corner, 6.2 s).</summary>
        void ResolveFollowLoops()
        {
            if (!CommitOn || !OwnerFix(16)) return;
            double now = W.Time;
            foreach (var b in units)                                                          // (fixed order: the world's object order)
            {
                var s = b.PS; var m = s.heldBy;
                if (s.wait != ZPathWait.Held || m == null || s.heldTicks < FollowLoopTicks || !b.IsMinion || !Separable(m) || !m.IsMinion) continue;
                var l = m.Leader;
                if (l == null || l.Destroyed || b.Leader != l || !FollowsSquad(m) || !FollowsSquad(b)) continue;
                var mc = m.PC;
                if (mc.followMode == 4 || mc.standing || mc.vCmd > 0 || Walking(m)) continue;    // m is held at 0 by its own follow (MateCap), not standing in its place
                if (now < m.PS.yieldUntil || now < s.yieldUntil) continue;
                if (!SquadAhead(m, l).Contains(b)) continue;                                  // m does not wait for b: not a loop
                m.PS.yieldUntil = now + 2.0; m.PS.Yields++; FollowLoops++;
                Log(m, $"waiting loop of 2 in the follow ({m.Type}#{m.Id} waits for {b.Type}#{b.Id}, held by it): backs up");
                if (!BackUp(m, b) && !StepAsideFor(b, m)) Log(m, "follow loop: no room to back up or step aside (the guard takes over)");
            }
        }

        /// <summary>Round 2 fix 32 (v3 only; the stuck guard's Escalate): a helper move (step aside, back off) whose stuck window ran out is
        /// re-planned instead of dropped. 1st time: a fresh path from where the unit stands to the same helper point (like a new Zod
        /// waypoint). 2nd time, for a unit that only walks this helper (no order of its own under it) and was sent out of a lane: out of the
        /// lane's zone already - the helper has done its job and ends there (an arrival, not a give-up); still in it - a new "leave the lane"
        /// spot, the other end first. Anything else is dropped as before. One round per unit per HelperReplanWindow s (never loops).
        /// L17 3-vehicle building route: a lane zone appeared on the medium tank's spot after the order, its "leave the lane" move clipped
        /// the gap's wall corner and was dropped (1 GiveUp).</summary>
        readonly Dictionary<int, (int n, double since)> helperReplans = new Dictionary<int, (int, double)>();
        public const double HelperReplanWindow = 10;
        internal bool ReplanHelper(ZObject o, ZWaypoint wp)
        {
            if (!CommitOn || !OwnerFix(32)) return false;
            double now = W.Time; var s = o.PS;
            if (!helperReplans.TryGetValue(o.Id, out var e) || now - e.since > HelperReplanWindow) e = (0, now);
            if (e.n == 0)
            {
                helperReplans[o.Id] = (1, e.since);
                s.reissue = true; s.Repaths++; s.nextShove = now + 3; HelperReplans++;
                Log(o, "helper move stuck: re-planned from here");
                return true;
            }
            if (e.n == 1 && wp.refId == RefStepAside && o.Waypoints.Count == 1)
            {
                helperReplans[o.Id] = (2, e.since);
                var lane = LaneAt(o.CenterX, o.CenterY);
                if (lane == null) { s.arrive = true; s.nextShove = now + 3; HelperReplans++; Log(o, "helper move stuck out of the lane zone: it has left the lane, ends here"); return true; }
                double hx = CX(o) - wp.x, hy = CY(o) - wp.y;                                  // (the other end of the lane first)
                o.Waypoints.RemoveAt(0);
                if (EvacuateLane(o, o, lane, hx, hy)) { s.nextShove = now + 3; HelperReplans++; Log(o, "helper move stuck in the lane: re-planned to a new spot out of it"); return true; }
                o.Waypoints.Insert(0, wp);                                                    // (no spot: dropped as before)
            }
            return false;
        }
    }

    public partial class ZObject
    {
        /// <summary>
        /// first). True = it moved (or the gate held it) on that axis and stays in the follow. Dx / Dy are restored (the follow sets them
        /// anew every tick).</summary>
        bool FollowSlide(double dt)
        {
            var P = World.Pathing;
            if (P == null || !P.CommitOn || !ZPathing.OwnerFix(4)) return false;
            float dx = Dx, dy = Dy; bool ok = false;
            bool xFirst = Math.Abs(dx) >= Math.Abs(dy);
            for (int k = 0; k < 2 && !ok; k++)
            {
                bool useX = (k == 0) == xFirst;
                if (useX ? IsZ(dx) : IsZ(dy)) continue;
                Dx = useX ? dx : 0f; Dy = useX ? 0f : dy;
                ok = ProcessMove(dt, true, out _, out _);
            }
            Dx = dx; Dy = dy;
            if (ok) P.FollowSlides++;
            return ok;
        }
    }
}
