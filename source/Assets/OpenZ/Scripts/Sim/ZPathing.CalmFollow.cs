using System;
using System.Collections.Generic;

namespace OpenZ.Sim
{
    /// <summary>
    /// Version 1.1, DV109: calm follow behind a slower unit (the switch: ZWorld.CalmFollowDeviation; on in the game, off in 'Zod exact' and
    /// wherever the switch is not set: the rules exactly as before, tick for tick).
    /// <para>Why. The original engine has no unit collision: a fast unit drives through a slow one. OpenZ keeps units apart
    /// (ZSeparation.cs): a unit that catches up with a slower one going its way drove at its full speed until the spacing, then had its
    /// step cut to the free part at once (a hard brake), and every time the gap opened a little (the slower unit's speed changing on
    /// another ground, a corner, the calm movement rule letting the same team come closer step by step, the spacing growing back near
    /// the end of the way) it took full steps again and was cut again: catch up, brake, wait, start again, pressed against the slower
    /// unit's back. On the screen a mech behind a slow tank stamped, stopped and started.</para>
    /// <para>The rule. Before the gate looks at the step (GateCore, first), a unit on its way looks for a unit right ahead of it in its
    /// own lane (its centre at most the two radii + 2 px to the side of the unit's heading, at most FollowLook px past the gap it keeps)
    /// that goes its way (at least half of its motion along the unit's heading; once followed, any forward motion: round a corner) and
    /// is slower BY ITS OWN SPEED on its ground (at least FollowSlower px/s; a unit of the same kind that only waits in a crowd is not
    /// "slower"), and that does not itself wait or queue (a jam, a lane, a bridge: the gate's own rules there, so no easing chain forms
    /// through a crowd; a unit that eases behind a slower one is followed, so a column of three kinds settles as one). It then eases its
    /// speed to that unit's pace at a steady gap: the gate's spacing + FollowMargin px + FollowBody of its OWN body length of clear road
    /// (2 x its radius). Over the last FollowLeadRamp body lengths of the slower unit's way that clear road closes to the spacing, so both stop
    /// together. Where the calm movement rule has already let the pair come closer (a crowd or a tight place pressed it: ZCalm.cs), the gap
    /// is that closer spacing without the body length (closeness only where there are too many). Its wanted speed is the slower unit's speed + FollowKp per second
    /// of the gap error (a gap larger than wanted closes, a smaller one opens; while it is still too small but outside the spacing it
    /// keeps at least FollowFloor of the slower unit's pace: it never stands while the slower unit drives on), never above the speed it
    /// could still brake from at FollowBrake within the gap left, never above its own speed; its speed then moves towards that by at
    /// most FollowAccel px/s per second up and FollowBrake down (no jumps). When the way is free again it takes up its own speed at
    /// FollowAccel. Only the length of its step changes: never its heading, its path or its order (a human order's destination is
    /// never lost); everything else in the gate (spacing, queue, side-step, lanes, bridges) works on the shorter step as before, so a
    /// sudden stop ahead is still caught by the gate. The calm follow does NOT count as being pressed (the calm movement rule's closer
    /// spacing is for crowds): the unit is marked as queued behind the slower unit only when that unit stands or the gap is below the
    /// spacing, as the gate's own queue would mark it. A unit that the slower one stops before (it stands for FollowStill s) is let go:
    /// the gate's rules for a standing unit act as before. A unit that has not been measured for FollowStale s (its order ended, it
    /// stood) starts its next walk at its own speed.</para>
    /// <para>Not for: its own squad (DV44 follows), pairs that may overlap (boarding, the fort door, the option FRIENDLY UNITS OVERLAP,
    /// the run into the enemy fort DV102, vehicles first), a unit passing through (ghost), a unit inside a pathing zone (a bridge with
    /// its ends, a fort gate, a narrow passage, a water crossing: the lanes and the crowd rules there act as before), a unit in a crowd
    /// (the calm movement rule found it pressed by its own team, or FollowCrowdCount or more units of its team on an order, its own squad
    /// not counted, stand or walk within AloneRadius px: too many in one place, where that rule's closer spacing and queue act as
    /// before), a robot behind a robot (robot squads mix and pass each other under the crowd rules, as before), and, while the
    /// option FAST UNITS PASS SLOW ONES is on, a unit of the same team that the passing rule may take (a plain move, not in a tight
    /// place): there it goes round as that rule says.</para>
    /// Sim only, fixed tick, object order, no clock. This file draws no random number.
    /// </summary>
    public sealed partial class ZPathing
    {
        // ---- numbers (px, px/s, s). Chosen with the counts of the kit (SPEC.md part 3).
        public static double FollowKp = 2.5;          // per second: a gap 4 px too large closes at 10 px/s over the slower unit's speed
        public static double FollowMargin = 1;        // px kept beyond the gate's spacing: the gate never has to cut the step
        public static double FollowBody = 1.0;        // its own body lengths (2 x its radius) of clear road kept behind a slower unit on open ground
        public static double FollowLook = 48;         // px past the wanted gap that the unit looks ahead (3 tiles)
        public static double FollowAccel = 12;        // px/s per second up (a tank from 15 to 20 px/s in 0.4 s)
        public static double FollowBrake = 18;        // px/s per second down
        public static double FollowFloor = 0.5;       // share of the slower unit's pace kept while the gap is still too small but outside the spacing
        public static double FollowSlower = 0.5;      // px/s: the unit ahead is slower when its own speed on its ground is at least this much below the unit's
        public static double FollowKeep = 1.0;        // s: the unit followed stays followed round a corner this long after it was last seen ahead
        public static double FollowStill = 1.0;       // s: a unit followed that has not moved this long is let go (the gate's rules for a standing unit)
        public static double FollowSmooth = 0.1;      // s: the measured speed of a unit is smoothed over this long
        public static double FollowStale = 0.1;       // s: a unit not measured this long stands (for the units behind it) and its own easing is over
        public static double FollowLeadRamp = 4;      // body lengths of the slower unit's way before its end over which the clear road closes to the spacing (they stop together; it walks at most a quarter faster meanwhile)
        public static double FollowOwnRamp = 40;      // px of its own way over which a crowd's closer spacing goes back to the whole spacing before the point where the calm movement rule gives it back
        public static bool FollowNotInZones = true;   // inside a pathing zone (bridge and its ends, fort gate, narrow passage, water crossing) the rule stays out
        public static bool FollowNotInCrowd = true;   // a unit the calm movement rule finds pressed by its own team (a crowd, a tight place: its share below 1) is left to that rule
        public static int FollowCrowdCount = 4;       // this many units of its team on an order (its own squad not counted) within AloneRadius px: too many in one place, the crowd rules act (0 = never)
        public static bool FollowNotBehindEasing = false;   // (A/B only) never follow a unit that itself eases behind a slower one
        public static bool FollowRobotBehindRobot = false;  // a robot behind a slower robot of another squad: off (small bodies; robot squads mix and pass each other as the crowd rules let them)

        /// <summary>Counters (checks, the Play report): ticks a unit's step was eased, times a unit began to ease behind a slower one, the
        /// pairs (unit, the unit it followed) that eased at least once, and the least clear margin over the gate's spacing an easing unit
        /// had to the unit it followed (px; at least 0 = never inside the spacing).</summary>
        public int FollowTicks, FollowStarts;
        public double FollowGapMin = double.PositiveInfinity;
        public int FollowPairs => followPairs.Count;
        /// <summary>Clears the counters (the Play menu, before a counted check in a running level).</summary>
        public void ResetFollowCounters() { FollowTicks = FollowStarts = 0; FollowGapMin = double.PositiveInfinity; followPairs.Clear(); }
        readonly HashSet<long> followPairs = new HashSet<long>();
        readonly List<ZObject> followNear = new List<ZObject>();

        /// <summary>The share of the gate's spacing the unit keeps behind b: the calm movement rule's share (ZCalm.cs; 1 on open ground, below 1
        /// only where that rule found the unit pressed by its own team), going back to the whole spacing over the last FollowOwnRamp px before
        /// the point where that rule gives the whole spacing back (NearGoal px before the end of its way): the gap opens a little at a time,
        /// so the spacing growing back never pushes the unit back.</summary>
        double FollowShare(ZObject a, ZObject b)
        {
            double q = CloseShare(a, b);
            if (q >= 1.0 || a.PathPending) return q;
            double rest = a.PathRemaining() - NearGoal;
            if (rest >= FollowOwnRamp) return q;
            return q + (1.0 - q) * Math.Min(1.0, Math.Max(0.0, (FollowOwnRamp - rest) / FollowOwnRamp));
        }

        /// <summary>The share of the clear road (body px) kept behind b: 1, going to 0 over the last FollowLeadRamp x body px of b's way (both stop together).</summary>
        static double FollowLeadShare(ZObject b, double body)
        {
            double ramp = FollowLeadRamp * body;
            if (b.PathPending || ramp < 1e-9) return 1.0;
            double r = b.PathRemaining();
            return r >= ramp ? 1.0 : Math.Max(0.0, r / ramp);
        }

        /// <summary>ZSeparation.cs GateCore, before the gate's own tests: the step of a unit behind a slower one going its way is shortened
        /// to a calm pace (above). Switch off: nothing read or written.</summary>
        internal void CalmFollow(ZObject a, double ox, double oy, ref double nx, ref double ny, ref double sx, ref double sy, ref double len)
        {
            if (!ZWorld.CalmFollowDeviation || len < 1e-9) return;
            var f = a.FS; double now = W.Time, cx = CX(a), cy = CY(a);
            // the unit's own measured speed (every unit that walks passes here every tick it walks): what a unit behind it sees
            double gap = now - f.t;
            if (double.IsNaN(f.x) || gap > FollowStale) { f.vx = f.vy = 0; f.active = false; f.leadId = -1; f.leadSeen = -100; }   // it walks again (a new order, after a stand): measured from here; an easing of before is over, it starts at its own speed
            else if (gap > 1e-9)
            {
                double k = Math.Min(1.0, gap / FollowSmooth);
                f.vx += ((cx - f.x) / gap - f.vx) * k; f.vy += ((cy - f.y) / gap - f.vy) * k;
                if (f.vx * f.vx + f.vy * f.vy > 0.25) f.movedAt = now;
            }
            f.x = cx; f.y = cy; f.t = now;

            if (a.FortRunOn || now < a.PS.ghostUntil || Static(a) || (ZWorld.OvertakeDeviation && (a.Overtaking || a.FallingIn))) { f.active = false; return; }
            if (FollowNotInZones && ZoneAt(a.CenterX, a.CenterY)) { f.active = false; return; }   // a bridge with its ends, a fort gate, a narrow passage, a water crossing: the crowd rules there act as before
            if (FollowNotInCrowd && a.Calm.q < 1.0) { f.active = false; return; }                // pressed by its own team in a crowd or a tight place: the calm movement rule's closer spacing and queue act there (ZCalm.cs)
            double dt = lastDt > 1e-9 ? lastDt : ZConst.TICK, va = len / dt, body = FollowBody * 2 * Radius(a);
            double hx = sx / len, hy = sy / len;
            ZObject lead = null; double best = double.MaxValue, leadD = 0, leadGs = 0, leadVb = 0;
            Near(a.CenterX, a.CenterY, Math.Max((int)AloneRadius, MaxQuery + (int)Math.Ceiling(FollowLook + body)), followNear);
            if (FollowCrowdCount > 0)
            {                                                                                   // too many of its team in one place: the calm movement rule's crowd rules (closer spacing, queue) act, as before
                int n = 0;
                foreach (var u in followNear)
                {
                    if (u == a || u.Owner != a.Owner || !Separable(u) || u.Waypoints.Count == 0 || SameSquad(a, u)) continue;
                    double ux = CX(u) - cx, uy = CY(u) - cy;
                    if (ux * ux + uy * uy <= AloneRadius * AloneRadius && ++n >= FollowCrowdCount) { f.active = false; return; }
                }
            }
            foreach (var b in followNear)
            {
                if (b == a || !Separable(b) || Static(b) || b.Waypoints.Count == 0 || SameSquad(a, b) || PairExempt(a, b) || PassesOwn(a, b)) continue;
                if (ZWorld.OvertakeDeviation && b.Owner == a.Owner && PassUnit(a) && !PassTight(a) && !PassTight(b)) continue;   // the option FAST UNITS PASS SLOW ONES: where that rule may pass, it takes it (ZOvertake.cs)
                if (b.RealMoveSpeed > a.RealMoveSpeed - FollowSlower) continue;                    // not slower by its own speed on its ground
                if (FollowNotBehindEasing && b.FS.active) continue;
                if (!FollowRobotBehindRobot && a.IsRobot && b.IsRobot) continue;                 // robots among robots: the crowd rules (ZCalm.cs), as before
                var bw = b.PS.wait;
                if (bw == ZPathWait.Queue || bw == ZPathWait.Held || bw == ZPathWait.Lane || bw == ZPathWait.Bridge || bw == ZPathWait.Arrived) continue;   // it waits or queues itself: the gate's own rules (no easing chain through a crowd)
                double rx = CX(b) - cx, ry = CY(b) - cy, along = rx * hx + ry * hy;
                if (along <= 0) continue;
                if (Math.Abs(rx * hy - ry * hx) >= MinSpacing(a, b) + 2) continue;               // not in its lane
                double gs = GateSpacing(a, b), q = FollowShare(a, b), d = Math.Sqrt(rx * rx + ry * ry);
                double open = Math.Max(0.0, Math.Min(1.0, (q - CloseMin) / Math.Max(1e-9, 1.0 - CloseMin)));   // 1 on open ground; 0 where the calm movement rule let the pair come closest (a crowd)
                double g = gs * q + FollowMargin + body * open * FollowLeadShare(b, body);
                if (d > g + FollowLook) continue;
                var bf = b.FS; bool fresh = now - bf.t <= FollowStale;
                double bvx = fresh ? bf.vx : 0, bvy = fresh ? bf.vy : 0, bl = Math.Sqrt(bvx * bvx + bvy * bvy), vAlong = bvx * hx + bvy * hy, vb;
                bool kept = f.leadId == b.Id && now - f.leadSeen <= FollowKeep;
                if (kept) { if (now - bf.movedAt > FollowStill) continue; vb = vAlong > 0 ? bl : 0; }   // followed already: round a corner its whole pace counts
                else { if (!Moving(b) || b.PathHeld || bl < 0.5 || vAlong < 0.5 * bl) continue; vb = vAlong; }
                double e = d - g;
                if (vb >= va && e >= 0) continue;                                                 // not slower now: nothing to ease
                double v = e >= 0 ? vb + Math.Min(FollowKp * e, Math.Sqrt(2 * FollowBrake * 0.8 * e))
                                  : Math.Max(d > gs * q + FollowMargin + 2 ? FollowFloor * vb : 0.0, vb + FollowKp * e);   // too close: it lets the gap open, never standing while the slower unit drives on outside the spacing
                if (v < best) { best = v; lead = b; leadD = d; leadGs = gs * q; leadVb = vb; }
            }
            double vt = lead != null ? Math.Max(0, Math.Min(va, best)) : va;
            if (!f.active)
            {
                if (lead == null || vt >= va) return;                                            // free: the unit's own step, untouched
                f.active = true; FollowStarts++; followPairs.Add(((long)a.Id << 32) | (uint)lead.Id);
                double vm = Math.Sqrt(f.vx * f.vx + f.vy * f.vy);
                f.vg = Math.Min(va, Math.Max(vm, vt));                                         // from where it is: already at speed, or (from a stand) at once at the pace wanted
            }
            if (vt < f.vg) f.vg = Math.Max(vt, f.vg - FollowBrake * dt); else f.vg = Math.Min(vt, f.vg + FollowAccel * dt);
            if (f.vg > va) f.vg = va;
            if (lead != null) { f.leadId = lead.Id; f.leadSeen = now; if (leadD - leadGs < FollowGapMin) FollowGapMin = leadD - leadGs; }
            if (lead == null && f.vg >= va - 1e-9) { f.active = false; return; }              // back at its own speed
            double share = f.vg / va;
            if (share < 1.0) { sx *= share; sy *= share; len *= share; nx = ox + sx; ny = oy + sy; FollowTicks++; }
            if (lead != null && (leadVb < 0.5 || leadD < leadGs)) { a.PS.wait = ZPathWait.Queue; a.PS.heldBy = lead; }   // queued only behind a unit that stands, or inside the spacing, as the gate's own queue would mark it (the stuck guard's traffic patience); a calm follow is not "pressed"
        }
    }

    public sealed partial class ZWorld
    {
        /// <summary>DV109 calm follow behind a slower unit (Sim/ZPathing.CalmFollow.cs). Static like every rule switch: the game sets it at
        /// every match start (View/ZGame.cs: on unless 'Zod exact'); headless default OFF, so every counted check and the parity report
        /// run without it unless they set it. A future online match must send it with the match settings.</summary>
        public static bool CalmFollowDeviation = false;
    }

    /// <summary>Per-unit state of the calm follow rule. Nothing here is read or written while the switch is off.</summary>
    public sealed class ZFollowState
    {
        internal double x = double.NaN, y, t = -1, vx, vy, movedAt = -100;   // measured speed (px/s), smoothed
        internal bool active; internal double vg;                            // easing behind a slower unit: the speed it is held to (px/s)
        internal int leadId = -1; internal double leadSeen = -100;           // the unit followed, when last seen ahead
    }

    public partial class ZObject
    {
        internal readonly ZFollowState FS = new ZFollowState();
        /// <summary>True while the unit eases its pace behind a slower one (DV109). For checks and the view.</summary>
        public bool CalmFollowing => FS.active;
    }
}
