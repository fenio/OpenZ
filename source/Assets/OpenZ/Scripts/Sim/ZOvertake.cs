using System;
using System.Collections.Generic;

namespace OpenZ.Sim
{
    /// <summary>
    /// Version 1.1: faster units pass slower ones (the switch: ZWorld.OvertakeDeviation; the Options row FAST UNITS PASS SLOW ONES,
    /// OFF by default; off = the rules exactly as before, tick for tick).
    /// <para>Why. In the original engine units have no collision and drive through each other, so every unit of a mixed group
    /// travels at its own speed. OpenZ's spacing layer (ZSeparation.cs) keeps units apart, and its gate made a unit QUEUE behind any
    /// unit going its way: a column moved at the pace of its slowest vehicle in front (counted: jeeps behind a medium tank arrived
    /// 1.45 times later than alone, behind a heavy tank about 2 times, behind a Mobile Missile 2.5 to 2.8 times).</para>
    /// <para>1. PASSING (the gate's side-step, which until now was only for units that stand): a unit on a plain move that the gate
    /// queues behind a unit of its own team going the same way may go round it when
    ///  - its own speed on the ground beside that unit is at least PassRatio times what the unit ahead really does (a queue ahead
    ///    counts with the pace of its slowest), and
    ///  - there is room beside it: the strip it would drive in (its own box, PassMargin px clear of the unit's spacing) holds no
    ///    blocked tile, no water for a vehicle, no bridge or passage with its approach, no building door way or building margin,
    ///    and no unit that stands or comes the other way. Where the strip is slower ground than its own line (the side of a road)
    ///    it must be free for the whole pass; on ground as fast as its own PassLookOpen px ahead are enough.
    /// It goes round on the side it is already on (dead astern: on the right), keeps that side, and never touches the slow unit:
    /// the spacing is kept the whole time, nobody is pushed, nobody's speed is raised. Once it is a spacing ahead it drives back
    /// onto the line it left (a road stays a road). A squad's members go round with their leader, on his side. With no room a
    /// unit queues as before; a pass that gains nothing for PassStall s ends and the unit falls in behind the slow one (it stands
    /// for that, FallInMax s at most, never in the way of a unit of its team, and never taken for a jam by the layer's last
    /// resort).
    /// Never: a unit inside or at a bridge, a water crossing, a fort gate or a narrow passage (the queue rules there are
    /// untouched), a unit within PassMinLeft px of the end of its way (the arrival rules), a unit under an attack order, a unit
    /// marked ZObject.KeepTogether (the mark for units that another rule marches as one body).</para>
    /// <para>2. GROUP ATTACKS STAY TOGETHER. A group order whose clicked point has an enemy unit, gun or building within
    /// ContactRange px runs today's rules for every member until its next order (no passing): the group arrives as one body, as
    /// before. No unit starts a pass while an enemy is within that range of itself. Orders that name a target (attack orders)
    /// are never touched at all.</para>
    /// <para>Not in the rule: a slow front unit that stands at the start of a group order to let the faster ones out first. It was
    /// built and counted: over 55 road columns it gained under 1 % and made 28 of 140 units arrive later than without the rule,
    /// so it was taken out. Nobody is ever made to stand for somebody else.</para>
    /// Sim only, fixed tick, object order, no random draw, no clock.
    /// </summary>
    public sealed partial class ZPathing { }

    public sealed partial class ZWorld
    {
        /// <summary>Faster units pass slower ones (Sim/ZOvertake.cs). Static like every rule switch: the game sets it at every match
        /// start (View/ZGame.cs: on unless 'Zod exact' or the Options row is off). Default OFF; a Play session in the editor leaves
        /// it as the game set it, so the counted checks (Editor/OzV11Kit.cs GameSwitches) and the parity report (Editor/OpenZParity.cs,
        /// Editor/OpenZParity.Deviations.cs) turn it off themselves: they run without it unless a check sets it. A future online
        /// match must send it with the match settings.</summary>
        public static bool OvertakeDeviation = false;

        /// <summary>Turns the passing rule off until it is disposed, then puts back what was there (the parity report's Deviations
        /// block holds one: a Play session may leave the switch on).</summary>
        public sealed class OvertakeHold : IDisposable
        {
            readonly bool was;
            internal OvertakeHold() { was = OvertakeDeviation; OvertakeDeviation = false; }
            public void Dispose() { OvertakeDeviation = was; }
        }
        public static OvertakeHold OvertakeOff() => new OvertakeHold();
    }

    /// <summary>Per-unit state of the passing rule (ZOvertake.cs). Nothing here is read or written while the switch is off.</summary>
    public sealed class ZOvertakeState
    {
        // ---- passing (the faster unit)
        internal int passId = -1;                           // the unit it goes round (-1: none)
        internal int passSide;                              // +1 right of its heading, -1 left
        internal double passCheckAt = -1;                   // when the strip is looked at again
        internal double passNow = -1;                       // the tick the gate last let it side-step for this pass
        internal bool aimLoose;                             // it side-stepped for a pass and was re-aimed from between two pixels: to be aimed exactly once it drives straight again
        internal int deniedId = -1; internal double deniedUntil = -1;   // no room found beside this unit: not asked again until then
        // ---- the line it left (the leg it was on when the pass began) and its way back onto it
        internal bool lineOn; internal double lineX, lineY, lineHx, lineHy; internal int lineTx, lineTy; internal ZWaypoint lineWp = ZWaypoint.Cleared;
        internal double lineTry = -1, retUntil = -1;       // next look for a way back; no look after this time
        internal bool retOn; internal int retX, retY;       // walking to this point on its line
        internal double passBest, passBestAt;               // how far ahead of the passed unit it got, and when it last gained
        internal int passedId = -1, passedSide; internal double passedAt = -100;   // the unit it last got past, and on which side (its squad's members follow it round that unit)
        internal int fallId = -1; internal double fallUntil = -1;   // a pass that failed: it stands until it can fall in behind this unit
        // ---- its last group order
        internal bool together;                             // that order goes into contact: today's rules for it
        internal bool contactNear; internal double contactCheckAt = -1;   // an enemy within ContactRange of it (looked at every ContactRecheck s)
    }

    public partial class ZObject
    {
        internal readonly ZOvertakeState PO = new ZOvertakeState();
        /// <summary>For a rule that marches units as one body (the computer's strike groups): it sets this on each of its units and
        /// clears it when it lets the unit go; a unit with it set never goes round a slower one. Nothing in this file sets it.</summary>
        public bool KeepTogether;
        /// <summary>Checks / the view: the unit is going round a slower one right now.</summary>
        public bool Overtaking => World != null && PO.passId >= 0 && World.Time - PO.passNow < 0.25;
        /// <summary>Checks / the view: after a pass that failed the unit stands right now, until it can fall in behind the slow one.</summary>
        public bool FallingIn => World != null && PO.fallId >= 0 && PO.lineOn && World.Time < PO.fallUntil;
        /// <summary>What Zod's ProcessMove makes of this unit's speed on a tile of the given walk speed (zobject.cpp:3360-3393).</summary>
        internal double SpeedOnTile(double tile) => MoveSpeed * tile * DamagedSpeed() * RunSpeed();
        /// <summary>The unit drops the point it is walking to and heads for the next point of its path (the point a way back onto its
        /// line was, when a new pass begins on that way).</summary>
        internal void PathSkipPoint() { if (cur.pfPoints != null && cur.pfPoints.Count > 0) NextPathPoint(); }
        /// <summary>Heads exactly for the point it is walking to, from its whole pixel. Why: Zod's SetVelocity keeps a part of the old
        /// velocity that changes by less than 0.1 px/s, and SetTarget counts from whole pixels. After the many small turns of a
        /// pass that leaves the unit up to a pixel or two off on a long flat leg, and Zod's arrival test (both axes done) then
        /// lets it run on past its point and sets it back onto it in one tick (counted: up to a tile). At most a pixel's part of
        /// its place is dropped; its speed is untouched.</summary>
        internal void PathAimExact()
        {
            if (Waypoints.Count == 0 || !IsMoving) return;
            XOver = YOver = 0;
            SetTarget(cur.x, cur.y);
            Dx = 0; Dy = 0;                                                                 // (no part of the old velocity is kept)
            SetVelocity();
        }
    }

    public sealed partial class ZPathing
    {
        // ------------------------------------------------------------------ numbers (px, s; 16 px = 1 tile)
        /// <summary>A pass starts when the unit's own speed in the strip beside the slow unit is at least this many times the slow unit's.</summary>
        public const double PassRatio = 1.15;
        /// <summary>The strip keeps this much more than the gate's spacing to the slow unit; it is looked at again every PassRecheck s;
        /// a refusal (no room) stands for PassRetry s.</summary>
        public const double PassMargin = 2, PassRecheck = 0.25, PassRetry = 0.5;
        /// <summary>A unit that side-stepped for a pass within the last PassSideKeep s goes round a second unit only on the same side
        /// (between two slow units it queues, it never weaves from one side to the other).</summary>
        public const double PassSideKeep = 2.0, PassSquadKeep = 20;
        /// <summary>How far ahead the strip must be free. Where the strip is slower ground than the unit's own line (the side of a
        /// road): for the whole pass, as far as the unit drives until it is a spacing ahead of the slow one at the two speeds, +
        /// PassLookMin px, and a pass that would need more than PassLookMax px is not begun (a pass that cannot be finished would
        /// leave the unit on the slow ground). Where the strip is as fast as its line (open ground): PassLookOpen px at most (if the
        /// room ends later the unit simply queues again, nothing is lost).</summary>
        public const double PassLookMin = 32, PassLookMax = 640, PassLookOpen = 160;
        /// <summary>A pass that gains nothing on the slow unit for PassStall s has failed: the unit stands (at most FallInMax s) until the
        /// slow one is far enough ahead and falls in behind it on its line. It never stands in anybody's way: the moment a unit of
        /// its team is held up by it, or an enemy comes within ContactRange, it drives on (and keeps looking for its way back).
        /// The stand is an orderly wait: while it lasts the unit is no dead jam for the layer's last resort (ZBridges.cs
        /// UnstickDeadJams, "no movement for 6 s: passes through"), so nobody drives through it and it drives through nobody.</summary>
        public const double PassStall = 3, FallInMax = 12;
        /// <summary>No pass within this many px of the end of the unit's way: there the arrival rules decide.</summary>
        public const double PassMinLeft = 96;
        /// <summary>The way back onto its line: at least ReturnMin px long along the line and twice as long as the unit is off it; it is
        /// looked for every ReturnRetry s for at most ReturnKeep s after the pass, then the unit keeps the straight way it is on.</summary>
        public const double ReturnMin = 32, ReturnRetry = 0.25, ReturnKeep = 60, ReturnOff = 3;
        /// <summary>A unit that has not side-stepped for a pass for AimAfter s is aimed exactly at its path point again (ZObject.PathAimExact).</summary>
        public const double AimAfter = 0.25;
        /// <summary>A group order whose clicked point has an enemy unit, gun or building within ContactRange px goes into contact: today's
        /// rules for all of it. No unit starts a pass with an enemy within ContactRange px of itself (looked at every ContactRecheck s).</summary>
        public const double ContactRange = 256, ContactRecheck = 0.5;
        /// <summary>Headless A/B only (never set by the game): [0] passing, [1] in contact today's rules (a group order into contact,
        /// a unit with an enemy near), [2] the way back onto the line.</summary>
        internal static readonly bool[] TestOvertake = { true, true, true };   // (the game runs all three)

        /// <summary>Checks: passes started, ways back onto the line taken, passes that failed (the unit stood to fall in), group orders
        /// into contact (today's rules).</summary>
        public int Overtakes { get; private set; }
        public int PassReturns { get; private set; }
        public int FallIns { get; private set; }
        public int ContactOrders { get; private set; }
        readonly List<ZObject> passNear = new List<ZObject>();

        // ------------------------------------------------------------------ 1. passing
        /// <summary>GateCore (the side-step condition): a is queued behind b, which goes its way. True = a may side-step round b on the
        /// side set in a.PS.sideSign. Switch off: false, nothing read or written.</summary>
        internal bool MayPass(ZObject a, ZObject b, double hx, double hy)
        {
            if (!ZWorld.OvertakeDeviation || !TestOvertake[0] || b == null) return false;
            var p = a.PO; double now = W.Time;
            if (!PassEligible(a, b)) { if (p.passId == b.Id) p.passId = -1; return false; }
            if (p.passId == b.Id && now < p.passCheckAt) return PassGo(a, p, now);
            if (p.deniedId == b.Id && now < p.deniedUntil) return false;
            int keep = p.passId >= 0 && p.passId != b.Id && p.passSide != 0 && now - p.passNow < PassSideKeep ? p.passSide : 0;   // going round another unit right now: this one only on the same side
            if (a.IsMinion) { var q = a.Leader.PO; int ls = q.passId == b.Id ? q.passSide : q.passedSide; if (ls != 0) keep = ls; }   // a squad member: its leader's side
            int side = PassSide(a, b, hx, hy, keep);
            if (side == 0) { p.deniedId = b.Id; p.deniedUntil = now + PassRetry; if (p.passId == b.Id) p.passId = -1; return false; }
            if (p.passId != b.Id || now - p.passNow > 1.0) { Overtakes++; Log(a, $"f40: goes round {b.Type}#{b.Id} on the {(side > 0 ? "right" : "left")}"); }
            if (p.passId != b.Id) { p.passBest = double.MinValue; p.passBestAt = now; }
            p.passId = b.Id; p.passSide = side; p.passCheckAt = now + PassRecheck;
            TakeLine(a, hx, hy);
            return PassGo(a, p, now);
        }

        bool PassGo(ZObject a, ZOvertakeState p, double now)
        {
            p.passNow = now; p.aimLoose = true;
            a.PS.sideSign = p.passSide; if (a.PS.sideUntil < now + 0.5) a.PS.sideUntil = now + 0.5;   // the gate's own side memory: this side first
            return true;
        }

        /// <summary>GateCore (the side-step loop): while a goes round a moving unit it only side-steps to the side of its pass.</summary>
        internal bool PassKeepsSide(ZObject a, double deg) => ZWorld.OvertakeDeviation && a.PO.passNow == W.Time && a.PO.passId >= 0 && a.PO.passSide != 0 && Math.Sign(deg) != a.PO.passSide;

        bool PassUnit(ZObject a)
        {
            if (a.KeepTogether || a.PO.together || a.Waypoints.Count == 0) return false;
            var wp = a.Waypoints[0];
            return wp.mode == WpMode.Move && !HelperMove(wp);                               // plain moves only: never an attack order, never the layer's own helper moves
        }

        /// <summary>A squad member goes round a unit only with its leader: while he goes round it, and PassSquadKeep s after he got past.</summary>
        bool PassWithLeader(ZObject a, ZObject b)
        {
            var l = a.Leader; if (l == null || l.Destroyed || l.KillAt >= 0) return false;
            var q = l.PO;
            return q.passId == b.Id || (q.passedId == b.Id && W.Time - q.passedAt < PassSquadKeep);
        }

        bool PassEligible(ZObject a, ZObject b)
        {
            if (!PassUnit(a) || a.Owner != b.Owner) return false;
            if (a.IsMinion && !PassWithLeader(a, b)) return false;
            if (!Moving(b) || b.PathHeld || Static(b)) return false;
            if (PassTight(a) || PassTight(b)) return false;
            if (a.PathRemaining() < PassMinLeft) return false;
            return !TestOvertake[1] || !EnemyNear(a);                                       // in contact: today's rules, the group stays as it is
        }

        /// <summary>An enemy unit, gun or building within ContactRange px of the unit (looked at every ContactRecheck s).</summary>
        bool EnemyNear(ZObject a)
        {
            var p = a.PO; double now = W.Time;
            if (now < p.contactCheckAt) return p.contactNear;
            p.contactCheckAt = now + ContactRecheck;
            return p.contactNear = ContactAt(a.CenterX, a.CenterY, a.Owner);
        }

        /// <summary>In or at a bridge, a water crossing, a fort gate, a passage or a one-lane gap: the queue rules there are untouched.</summary>
        bool PassTight(ZObject o) => InNarrowArea(o) || (CommitOn && (Zoned(o) || o.PC.tightNow));

        /// <summary>What the unit ahead really does (px/s): its own speed, or the pace of the slowest unit of the queue it is in.</summary>
        double PaceOf(ZObject b)
        {
            double v = Math.Sqrt((double)b.Dx * b.Dx + (double)b.Dy * b.Dy);
            var x = b;
            for (int hop = 0; hop < 4; hop++)
            {
                var s = x.PS;
                if (s.wait != ZPathWait.Queue || s.heldBy == null || s.heldBy == b || !Moving(s.heldBy)) break;
                x = s.heldBy;
                double vx = Math.Sqrt((double)x.Dx * x.Dx + (double)x.Dy * x.Dy);
                if (vx < v) v = vx;
            }
            return v;
        }

        /// <summary>The side a may go round b on: the side it is on already, dead astern the right, then the other. 0 = no room.</summary>
        int PassSide(ZObject a, ZObject b, double hx, double hy, int keep)
        {
            double rx = CX(b) - CX(a), ry = CY(b) - CY(a);
            double cross = hx * ry - hy * rx;                                              // > 0: b is right of a's heading (pixel y points down)
            int first = cross > 2 ? -1 : cross < -2 ? 1 : (a.PO.passId == b.Id && a.PO.passSide != 0 ? a.PO.passSide : 1);
            double vb = PaceOf(b);
            if (keep != 0) return (first == keep || Math.Abs(cross) <= 0.5 * GateSpacing(a, b)) && PassStripFree(a, b, hx, hy, keep, vb) ? keep : 0;
            if (PassStripFree(a, b, hx, hy, first, vb)) return first;
            if (a.PO.passId == b.Id && a.PO.passSide == first) return 0;                   // (a pass under way never changes sides)
            if (Math.Abs(cross) <= 0.5 * GateSpacing(a, b) && PassStripFree(a, b, hx, hy, -first, vb)) return -first;   // (well off to one side already: never across the slow unit's line)
            return 0;
        }

        /// <summary>A place a's box may drive over on a pass or on its way back: free tiles on its own grid (rocks are walls), no bridge,
        /// passage or one-lane gap with its approach, no building margin, no building door way (DoorWays() read by the caller).</summary>
        bool PassGround(ZObject a, int cx, int cy)
        {
            bool robot = a.IsRobot;
            if (!Reach.BoxFree(cx - (a.WidthPix >> 1), cy - (a.HeightPix >> 1), a.WidthPix, a.HeightPix, robot, false)) return false;
            if (KeepClearAt(cx, cy) != null || LaneAt(cx, cy) != null) return false;
            if (CommitOn && TightAt(cx, cy)) return false;
            var cl = W.Clearance;
            if (cl != null && cl.Enabled && cl.NearMargin(cx, cy, robot ? 8 : 16, robot ? ZClearance.RobotMargin : ZClearance.VehicleMargin)) return false;
            return !InDoorWay(cx, cy);
        }

        /// <summary>The strip a would drive in round b on that side: a's own box from where it stands, out to the gate's spacing +
        /// PassMargin beside b (at 45 degrees, like the gate's side-step) and on past b's nose.</summary>
        bool PassStripFree(ZObject a, ZObject b, double hx, double hy, int side, double vb)
        {
            double px = -hy * side, py = hx * side;                                        // unit vector to that side
            double ax = CX(a), ay = CY(a), rx = CX(b) - ax, ry = CY(b) - ay;
            double along = rx * hx + ry * hy, lat = rx * px + ry * py;                     // b ahead of a by `along`, to that side by `lat`
            double sp = GateSpacing(a, b) + PassMargin, need = Math.Max(0, lat + sp);     // how far a must move over to be clear of b
            double gain = Math.Max(along, 0) + sp;                                         // what a must gain on b to be a spacing ahead of it
            double tile = W.Map.TileWalkSpeed((int)Math.Round(ax + px * need), (int)Math.Round(ay + py * need)), look = PassLookMax;
            double own = W.Map.TileWalkSpeed(a.CenterX, a.CenterY);                       // the ground a is on now
            DoorWays();                                                                    // (the buildings' door ways, read once for the strip)
            for (double s = 8; ; s += 8)
            {
                if (s > PassLookMax + 8) return false;                                     // (never longer than the longest pass)
                double off = Math.Min(need, s);
                int cx = (int)Math.Round(ax + hx * s + px * off), cy = (int)Math.Round(ay + hy * s + py * off);
                if (!PassGround(a, cx, cy)) return false;
                if (off >= need - 1e-9) { double t = W.Map.TileWalkSpeed(cx, cy); if (t < tile) tile = t; }
                double va = a.SpeedOnTile(tile);
                if (va <= 0 || va < PassRatio * vb) return false;                          // not faster there (off the road it may not be)
                look = gain * va / (va - vb) + PassLookMin;                                // the whole pass at the two speeds
                if (tile >= own) look = Math.Min(look, PassLookOpen);                      // ground as fast as its own: no need to see the whole pass
                else if (look > PassLookMax) return false;                                 // slower ground and too long a pass
                if (s >= look) break;
            }
            // units in the strip: one that stands or comes the other way closes it
            double mx = ax + hx * look * 0.5 + px * need * 0.5, my = ay + hy * look * 0.5 + py * need * 0.5;
            Near((int)mx, (int)my, (int)(look * 0.5 + need) + MaxQuery, passNear);
            foreach (var c in passNear)
            {
                if (c == a || c == b || !Separable(c) || PairExempt(a, c) || PassesOwn(a, c) || c.Leader == a) continue;
                double qx = CX(c) - ax, qy = CY(c) - ay, s = qx * hx + qy * hy, l = qx * px + qy * py;
                if (s < -4 || s > look + 4) continue;
                double off = Math.Min(need, Math.Max(s, 0)), min = MinSpacing(a, c) + 1;
                if (Math.Abs(l - off) >= min) continue;                                    // beside the strip
                if (Moving(c) && !c.PathHeld && c.Dx * hx + c.Dy * hy > 0) continue;       // goes the same way: a queues behind it or passes it in its turn
                return false;
            }
            return true;
        }

        // ------------------------------------------------------------------ the line a unit leaves, and its way back onto it
        /// <summary>The leg a is on when it starts to go round a unit: from where it stands straight to its current path point.</summary>
        void TakeLine(ZObject a, double hx, double hy)
        {
            var p = a.PO;
            if (a.IsMinion) return;                                                        // (a squad member walks with its squad: no line of its own)
            if (p.lineOn && LineHolds(a, p, out _)) return;                                // (a second pass on the same leg keeps the first line)
            p.lineOn = true; p.retOn = false; p.lineX = CX(a); p.lineY = CY(a); p.lineHx = hx; p.lineHy = hy;
            p.lineTx = a.PathTargetX; p.lineTy = a.PathTargetY; p.lineWp = a.Waypoints[0]; p.lineTry = -1; p.retUntil = -1; p.fallId = -1;
        }

        bool LineHolds(ZObject a, ZOvertakeState p, out bool onReturn)
        {
            onReturn = false;
            if (a.Waypoints.Count == 0 || !a.Waypoints[0].Equals(p.lineWp)) return false;
            int tx = a.PathTargetX, ty = a.PathTargetY;
            if (p.retOn && tx == p.retX && ty == p.retY) { onReturn = true; return true; }
            return tx == p.lineTx && ty == p.lineTy;
        }

        /// <summary>EndTick: every unit that left its line for a pass: is the pass over (a spacing ahead of the unit it went round)?
        /// Then it drives back onto the line, as soon as the way there is free.</summary>
        void PassTick()
        {
            if (!ZWorld.OvertakeDeviation) return;
            double now = W.Time;
            foreach (var a in units)
            {
                var p = a.PO;
                if (!p.lineOn) continue;
                if (!LineHolds(a, p, out bool onReturn)) { p.lineOn = p.retOn = false; p.passId = -1; continue; }   // the leg ended, a new order, a new plan
                if (onReturn)
                {
                    if (p.passId < 0 || now - p.passNow > 0.05) continue;                    // on its way back
                    a.PathSkipPoint(); p.retOn = false;                                      // a new pass began on the way back: the old line stays, the way back is planned again
                    if (!LineHolds(a, p, out _)) { p.lineOn = false; p.passId = -1; continue; }
                }
                else if (p.retOn) { p.lineOn = p.retOn = false; p.passId = -1; continue; }   // back on its line
                double off = (CX(a) - p.lineX) * -p.lineHy + (CY(a) - p.lineY) * p.lineHx;
                if (p.passId >= 0)
                {
                    var b = W.Get(p.passId);
                    if (b != null && !b.Destroyed && b.KillAt < 0 && Separable(b))
                    {
                        double ahead = (CX(a) - CX(b)) * p.lineHx + (CY(a) - CY(b)) * p.lineHy;
                        if (ahead < GateSpacing(a, b) + PassMargin)
                        {
                            if (ahead > p.passBest + 1) { p.passBest = ahead; p.passBestAt = now; }
                            if (now - p.passBestAt < PassStall) continue;                    // still going round it
                            if (Math.Abs(off) >= ReturnOff && TestOvertake[2]) { p.fallId = b.Id; p.fallUntil = now + FallInMax; FallIns++; Log(a, $"f40: cannot get past {b.Type}#{b.Id}: falls in behind it"); }
                        }
                        else { p.passedId = b.Id; p.passedAt = now; p.passedSide = p.passSide; }   // past it
                    }
                    p.passId = -1;
                }
                if (Math.Abs(off) < ReturnOff) { p.lineOn = false; p.fallId = -1; continue; }   // on its line anyway
                if (p.fallId >= 0)
                {                                                                            // a failed pass: it stands (FallInHold) until the way back is free behind the slow unit
                    if (now >= p.fallUntil || PassTight(a) || !PassUnit(a)) { p.fallId = -1; p.lineOn = false; continue; }
                    if (TryReturn(a, p, off, ReturnMin)) p.fallId = -1;
                    continue;
                }
                if (p.retUntil < 0) p.retUntil = now + ReturnKeep;
                if (!TestOvertake[2] || now > p.retUntil) { p.lineOn = false; continue; }
                if (now < p.lineTry) continue;
                p.lineTry = now + ReturnRetry;
                if (PassTight(a) || !PassUnit(a)) { p.lineOn = false; continue; }
                TryReturn(a, p, off, Math.Max(ReturnMin, 2 * Math.Abs(off)));
            }
            foreach (var a in units)
            {                                                                                // the side-steps of a pass are over: it heads exactly for its path point again (ZObject.PathAimExact)
                var p = a.PO;
                if (!p.aimLoose || now - p.passNow < AimAfter) continue;
                p.aimLoose = false;
                if (!p.retOn && !a.IsMinion && !a.PathPending && !a.PathInBuildingStage && PassUnit(a)) a.PathAimExact();   // (on its way back it is aimed when it reaches its line; a squad member walks with its squad)
            }
        }

        /// <summary>The way back: a point on the line, `run` px ahead of the unit (after a pass: twice what it is off the line, ReturnMin
        /// at least), still before the end of the leg; the straight way there must be free ground and pass no unit closer than the
        /// gate's spacing + PassMargin. True = it is on its way (or the leg ends first and it needs none).</summary>
        bool TryReturn(ZObject a, ZOvertakeState p, double off, double run)
        {
            double ax = CX(a), ay = CY(a), along = (ax - p.lineX) * p.lineHx + (ay - p.lineY) * p.lineHy;
            double qx = p.lineX + p.lineHx * (along + run), qy = p.lineY + p.lineHy * (along + run);
            double left = (p.lineTx - qx) * p.lineHx + (p.lineTy - qy) * p.lineHy;
            if (left < 8) { p.lineOn = false; return true; }                                 // the leg ends first: it goes straight to its path point
            int ix = (int)Math.Round(qx), iy = (int)Math.Round(qy);
            double dx = ix - ax, dy = iy - ay, len = Math.Sqrt(dx * dx + dy * dy);
            DoorWays();
            for (double s = 8; s < len + 8; s += 8)
            {
                double f = Math.Min(1.0, s / len);
                if (!PassGround(a, (int)Math.Round(ax + dx * f), (int)Math.Round(ay + dy * f))) return false;
            }
            Near((int)(ax + dx * 0.5), (int)(ay + dy * 0.5), (int)(len * 0.5) + MaxQuery, passNear);
            foreach (var c in passNear)
            {
                if (c == a || !Separable(c) || PairExempt(a, c) || PassesOwn(a, c) || c.Leader == a) continue;
                double ox = CX(c) - ax, oy = CY(c) - ay, t = Math.Max(0, Math.Min(1, (ox * dx + oy * dy) / (len * len)));
                double ex = ox - t * dx, ey = oy - t * dy, min = GateSpacing(a, c) + PassMargin;
                if (ex * ex + ey * ey < min * min) return false;                             // a unit on or beside the way back: later
            }
            a.PathRecenter(ix, iy);
            a.PS.rebase = true;                                                              // (the stuck guard: a new plan, its measure starts again)
            p.retOn = true; p.retX = ix; p.retY = iy; PassReturns++;
            Log(a, $"f40: back onto its line at ({ix},{iy})");
            return true;
        }

        // ------------------------------------------------------------------ 2. group attacks stay together
        /// <summary>ZSlots.AssignSlots, after the spots of a group order are set: an order whose clicked point has an enemy unit, gun
        /// or building within ContactRange px goes into contact: today's rules for every member until its next order.</summary>
        internal void ContactOrder(List<ZObject> group, int px, int py)
        {
            if (!ZWorld.OvertakeDeviation || group.Count < 2) return;
            bool contact = TestOvertake[1] && ContactAt(px, py, group[0].Owner);
            if (contact) ContactOrders++;
            foreach (var u in group) { u.PO.together = contact; foreach (var m in u.Minions) if (m != null) m.PO.together = contact; }
        }

        /// <summary>An enemy unit, gun or building within ContactRange px of the point.</summary>
        bool ContactAt(int px, int py, Team team)
        {
            foreach (var o in W.Objects)
            {
                if (o.Owner == team || o.Owner == Team.Neutral || o.Destroyed || o.KillAt >= 0) continue;
                if (!o.IsUnit && o.Kind != ZKind.Building) continue;
                double dx = o.CenterX - px, dy = o.CenterY - py;
                if (dx * dx + dy * dy <= ContactRange * ContactRange) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ a pass that failed
        /// <summary>GateCore (first): after a pass that failed the unit stands this tick: it lets the slow unit go ahead, then falls in
        /// behind it (PassTick). FallInMax s at most, and never in anybody's way: when a unit of its team is held up by it (held,
        /// queued or side-stepping because of it), or an enemy comes within ContactRange, the stand is over and it drives on
        /// (PassTick still looks for its way back onto its line). While it stands the layer's last resort for a dead jam
        /// (ZBridges.cs UnstickDeadJams) does not count it: nobody passes through a unit that stands in order.
        /// Switch off: false, nothing read or written.</summary>
        internal bool FallInHold(ZObject a)
        {
            if (!ZWorld.OvertakeDeviation) return false;
            var p = a.PO; double now = W.Time;
            if (p.fallId < 0 || !p.lineOn || now >= p.fallUntil) return false;
            if (TestOvertake[1] && EnemyNear(a)) { p.fallId = -1; return false; }             // in contact: today's rules, nobody stands
            Near(a.CenterX, a.CenterY, MaxQuery, passNear);
            foreach (var c in passNear)
                if (c != a && c.Owner == a.Owner && c.PS.heldBy == a && c.PS.wait != ZPathWait.None) { p.fallId = -1; return false; }   // in somebody's way: it drives on
            a.PS.bridge.jamSince = now;                                                     // an orderly wait, not a dead jam (ZBridges.cs UnstickDeadJams)
            Hold(a, ZPathWait.Held, null);                                                  // (held by nobody: no deadlock loop)
            return true;
        }

        /// <summary>ZSlots.OnOrder: any new order ends what the last one set.</summary>
        internal void PassReset(ZObject o)
        {
            if (!ZWorld.OvertakeDeviation) return;
            ResetState(o.PO);
            foreach (var m in o.Minions) if (m != null) ResetState(m.PO);
        }
        static void ResetState(ZOvertakeState p) { p.together = false; p.passId = -1; p.deniedId = -1; p.fallId = -1; p.lineOn = p.retOn = false; p.aimLoose = false; }
    }
}
