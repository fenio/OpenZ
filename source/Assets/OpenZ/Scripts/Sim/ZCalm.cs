using System;
using System.Collections.Generic;

namespace OpenZ.Sim
{
    /// <summary>
    /// Version 1.1: calm movement (the switch: ZWorld.CalmMoveDeviation; on in the game, off in 'Zod exact' and wherever the switch is not
    /// set: the rules exactly as before, tick for tick).
    /// <para>Why. The original engine has no unit collision: units drive through each other, and its paths run from tile to tile in eight
    /// directions. OpenZ keeps units apart (ZSeparation.cs and the files round it). Both show as swerving: a unit alone turns one way
    /// and at once the other way where its path leaves or meets the tile grid, units weave round each other in tight places, a route is
    /// re-planned while nothing has changed, and in a fight a unit gets one small move after the other from different rules.</para>
    /// <para>1. STRAIGHT WAYS (CalmStraight, ZDetour.cs CheckPath). A planned path loses the jogs of the tile grid: the small step onto
    /// the grid at its start, the small hook from the last grid point to the clicked point, and the staircase over open ground, wherever
    /// the straight leg is clear. Bridges, gates and lanes keep their points, except for a vehicle with nobody near it.</para>
    /// <para>2. A ROUTE IS KEPT. First, ZStuckGuard.cs Monitor: the stuck guard measures progress as "the rest of the path got shorter". When
    /// a move that was put in front of an order ended (a step aside from a shell, a rock shot away, a flag taken on the way, a helper
    /// move of the spacing layer), the guard took its first measure of the order that went on in the same tick, before that order's path
    /// existed: the measure was the few pixels left of the move that had just ended. Against that the order never "made progress", and
    /// one stuck window later (0.8 s) the guard replaced the route with a search of its own, another line over the same ground. Now the
    /// first measure is taken one tick later, from the order's own path (CalmFresh). Second, ZDetour.cs Detour: a unit with no other unit
    /// within AloneRadius px gets no way round from the stuck guard unless it has made no way for AloneBlocked s; with company a new way
    /// round is taken at most every RouteKeep s (CalmKeepsRoute). Third, farther than KeepGoalFar px from the point the PLAYER ordered his
    /// move is never ended early: a way round that would stop short of that point is not taken (CalmKeepsGoal), and where the stuck
    /// guard gave such a move up (three steps without a way through, or no movement for 12 s) it plans the way again (CalmKeepsMove).</para>
    /// <para>3. THE SAME TEAM MAY COME CLOSER. A unit on an order that units of its own team have slowed, turned aside or held for
    /// PressAfter s (at once inside a zone: a bridge with its ends, a fort gate, a narrow passage, a water crossing), keeps less room from
    /// units of its own team that are on an order too (robots, vehicles, or one of each): the spacing shrinks by CloseStep every CloseStepTime s down to CloseMin of the normal
    /// one, and grows back the same way once it has been free for RelaxAfter s outside a zone with nobody of its team inside the normal
    /// spacing. While it is that close it queues behind a unit of its team instead of weaving round it (unless it has not moved for
    /// WeaveAfter s); a unit of its team that stands in the way (idle, or waiting for its turn at a bridge or lane) is gone round first
    /// and asked to step aside or make way only after MakeWayAfter s; a robot on an order is sent out of the way of a vehicle of its team at most every YieldRest s (the next
    /// vehicles drive through it, as the rule "vehicles first" already lets them). Nothing changes towards enemies, towards units that
    /// stand without an order (they are gone round, and a unit never stops on top of another: within NearGoal px of the end of its way,
    /// and the moment its order ends, its spacing is the normal one again), for crushing, or for what can be hit.</para>
    /// <para>4. CALM IN A FIGHT, THE PLAYER'S ORDER FIRST, WHO STEPS ASIDE: ZObject.Calm.cs.</para>
    /// Sim only, fixed tick, object order, no clock. This file draws no random number.
    /// </summary>
    public sealed partial class ZPathing
    {
        // ---- numbers (px, s). Chosen with the counts of the kit (SPEC.md part 3).
        public static double AloneRadius = 96;       // 6 tiles: three vehicle spacings; nobody nearer = alone
        public static double AloneBlocked = 2.0;     // a lone unit that made no progress this long may look for another way
        public static double RouteKeep = 6.0;        // with company: a new way round standing units at most this often (before: 2 s)
        public static double PressAfter = 1.0;       // slowed / turned / held by its own team this long in the open: the spacing starts to shrink
        public static double PressGap = 0.5;         // a free moment shorter than this does not end the pressing
        public static double CloseMin = 0.6;         // the smallest share of the normal spacing (two tanks: 36 px -> 22 px between centres)
        public static double CloseStep = 0.1, CloseStepTime = 0.25;   // step by step: 1.0 -> 0.6 in one second
        public static double RelaxAfter = 1.0;       // free this long, outside a zone: the spacing grows back
        public static double NearGoal = 64;          // within this much of the end of its way a unit is back at the normal spacing (4 tiles: the crowd arrival radius)
        public static double WeaveAfter = 3.0;       // not moved this long while close: it may side-step again (truly blocked)
        /// <summary>A/B of the parts (the headless runner only; the game never sets it): 1 resumed order, 2 alone / route keep, 4 closer,
        /// 8 queue instead of weaving, 16 round a waiting unit, 32 a robot's rest between two steps out of a vehicle's way, 64 the jogs of a planned path,
        /// 128 a lone vehicle's jogs inside a bridge or lane keep-clear area.</summary>
        internal static int CalmParts = 255;

        internal static bool CalmOn => ZWorld.CalmMoveDeviation;
        public int CalmFreshSkips, CalmRouteKeeps, CalmCloseUnits, CalmQueued;

        /// <summary>ZStuckGuard.cs Monitor, the moment the watched waypoint changes: its first progress measure waits one tick (part 1).</summary>
        internal bool CalmFresh(ZObject o)
        {
            if (!CalmOn || (CalmParts & 1) == 0) return false;
            CalmFreshSkips++;
            return true;
        }

        /// <summary>ZDetour.cs Detour (the stuck guard's way round standing units): not now (part 2).</summary>
        internal bool CalmKeepsRoute(ZObject o)
        {
            if (!CalmOn || (CalmParts & 2) == 0) return false;
            var c = o.Calm; double now = W.Time;
            if (CalmAlone(o)) { if (now - o.PS.lastProgress < AloneBlocked) { CalmRouteKeeps++; return true; } }
            else if (now < c.routeKeepUntil) { CalmRouteKeeps++; return true; }
            c.routeKeepUntil = now + RouteKeep;
            return false;
        }

        /// <summary>ZDetour.cs CheckPath, after the wall check and the bridge rule and BEFORE the rule that plans a way round parked units (a
        /// way round a parked unit therefore keeps every point it was given). The original path search gives points on the tile grid, eight directions, and adds the clicked point last. So a path begins with a small
        /// jog onto the grid, ends with a small hook from the last grid point to the clicked point, and crosses open ground as a
        /// staircase (three tiles west, one south-west, three west, two south-west ...): turns one way and at once the other way with
        /// nothing in the way. Here a path point is left out when one of its two legs is at most JogMax px long and the straight leg
        /// past it is clear: the unit's own box on free ground all the way (a rock counts as in the way, also for a unit that could
        /// shoot it), never a point in the keep-clear area of a bridge or lane and never a leg across one (there the path stays as it
        /// is: the bridge and lane queues line units up on its points; the same test the layer's own rule for a back step uses).
        /// A VEHICLE WITH NOBODY NEAR (no other unit within AloneRadius px when the path is planned) has no queue to line up with:
        /// its jogs go inside a keep-clear area too, so the small step onto the grid at a bridge end and the hook back to the clicked
        /// point there are gone (a robot squad keeps them: its members walk the deck in single file on those points). Long
        /// legs and the corners between them stay the original path. A unit that stands in a wall or on the shore keeps its whole path
        /// (its first step is its way out).</summary>
        internal List<(int x, int y)> CalmStraight(ZObject o, List<(int x, int y)> pts)
        {
            if (!CalmOn || (CalmParts & 64) == 0 || pts == null || pts.Count < 3) return pts;
            bool robot = o.IsRobot; int half = robot ? 8 : 16, ax = o.CenterX, ay = o.CenterY;
            if (!Reach.BoxFree(ax - half, ay - half, half * 2, half * 2, robot, false)) return pts;
            double j2 = JogMax * JogMax;
            bool alone = (CalmParts & 128) != 0 && !robot && CalmAlone(o, calmNear);          // nobody near: no queue to line up with, so the points in a keep-clear area may go too (vehicles only)
            for (int i = 0; i < pts.Count - 1 && pts.Count >= 3;)
            {
                var b = pts[i]; var c = pts[i + 1];
                double abx = b.x - ax, aby = b.y - ay, bcx = c.x - b.x, bcy = c.y - b.y;
                if ((abx * abx + aby * aby <= j2 || bcx * bcx + bcy * bcy <= j2) && (alone || KeepClearAt(b.x, b.y) == null) && (!JogNotInZone || !ZoneAt(b.x, b.y)) && CalmLegClear(ax, ay, c.x, c.y, robot, half, alone))
                { pts.RemoveAt(i); CalmPointsCut++; continue; }
                ax = b.x; ay = b.y; i++;
            }
            return pts;
        }

        /// <summary>The unit's box fits on free ground along the whole straight leg, and the leg crosses no keep-clear area of a bridge or lane
        /// (a vehicle with nobody near: only the free ground counts).</summary>
        bool CalmLegClear(int x0, int y0, int x1, int y1, bool robot, int half, bool alone)
        {
            double dx = x1 - x0, dy = y1 - y0, len = Math.Sqrt(dx * dx + dy * dy);
            int steps = (int)(len / 4) + 1;
            var la = KeepClearAt(x0, y0); var lc = KeepClearAt(x1, y1);
            for (int i = 1; i <= steps; i++)
            {
                double f = Math.Min(1.0, i * 4 / Math.Max(len, 1e-9));
                int cx = (int)Math.Round(x0 + dx * f), cy = (int)Math.Round(y0 + dy * f);
                if (!Reach.BoxFree(cx - half, cy - half, half * 2, half * 2, robot, false)) return false;
                var l = KeepClearAt(cx, cy);
                if (!alone && l != null && l != la && l != lc) return false;                // (never across a keep-clear area the leg neither starts nor ends in)
            }
            return true;
        }
        public static double JogMax = 48;            // a leg this short (3 tiles) is a jog of the grid, not a way round something
        public int CalmPointsCut;
        readonly List<ZObject> calmNear = new List<ZObject>();                              // (a list of its own: a path can be checked while the layer's shared lists are in use)
        public static bool JogNotInZone = false;     // (A/B of the runner: true = also never a point inside a pathing zone)

        /// <summary>ZDetour.cs Detour: the way round standing units that was found does not reach the goal (it is "walled in": something
        /// stands in the only gap) and ends more than KeepGoalFar px short of where the PLAYER sent this unit: not taken. The unit keeps
        /// its route and waits; before, it walked to that nearer point and its order ended there (ZObject.Calm.cs).</summary>
        internal bool CalmKeepsGoal(ZObject o, List<(int x, int y)> pts, bool found)
        {
            if (!CalmOn || found || pts == null || pts.Count == 0 || o.Waypoints.Count == 0 || W == null || !W.IsHumanTeam(o.Owner)) return false;
            var wp = o.Waypoints[0];
            if (!wp.playerGiven || wp.mode != WpMode.Move) return false;
            var (ex, ey) = pts[pts.Count - 1];
            double dx = ex - wp.x, dy = ey - wp.y;
            if (dx * dx + dy * dy <= KeepGoalFar * KeepGoalFar) return false;
            CalmGoalsKept++;
            Log(o, $"b49: a way round that ends {Math.Sqrt(dx * dx + dy * dy):0} px short of the ordered point is not taken");
            return true;
        }
        /// <summary>ZStuckGuard.cs Escalate and Watchdog, where the guard would give the order up: the first waypoint is a move the PLAYER
        /// ordered and the unit is still more than KeepGoalFar px from the ordered point: the way is planned again instead (as the guard
        /// already does for his enter, attack, repair and fort orders). Nearer than that the guard's give-up stays: there it is what
        /// lets a unit of a crowd stop beside its team.</summary>
        internal bool CalmKeepsMove(ZObject o)
        {
            if (!CalmOn || o.Waypoints.Count == 0 || W == null || !W.IsHumanTeam(o.Owner)) return false;
            var wp = o.Waypoints[0];
            if (!wp.playerGiven || wp.mode != WpMode.Move || wp.refId <= RefStepAside) return false;
            double dx = CX(o) - wp.x, dy = CY(o) - wp.y;
            if (dx * dx + dy * dy <= KeepGoalFar * KeepGoalFar) return false;
            CalmMovesKept++;
            return true;
        }
        public int CalmMovesKept;

        /// <summary>ZStuckGuard.cs Escalate, in front of the step out of a wedge against a parked hull: a squad member that walks back to
        /// its leader (a helper move of the layer) and is held by a parked unit for the (WedgeMax + 1)th time, each within WedgeForget s
        /// of the last, does not step out and try again: its walk back ends there, as the guard ends every other helper move that does
        /// not get on, and the layer's own rule for a new try applies (not while nothing has changed). Before, the step out and the new
        /// try went on for as long as the hull stood there: a robot shuffling beside a parked tank without end.</summary>
        internal bool CalmWedgeEnds(ZObject o, ZWaypoint wp)
        {
            if (!CalmOn || wp.refId != RefRegroup || !o.IsRobot || !ParkedHolder(o)) return false;
            var c = o.Calm; double now = W.Time;
            if (now - c.wedgeLast > WedgeForget) c.wedges = 0;
            c.wedgeLast = now;
            if (++c.wedges <= WedgeMax) return false;
            c.wedges = 0; CalmWedgesEnded++;
            var s = o.PS; s.level = 3; s.giveUp = true; s.nextShove = now + 3;
            Log(o, "b49: its walk back to its leader is wedged against a parked unit again: ends here");
            return true;
        }
        public static int WedgeMax = 3; public static double WedgeForget = 30;
        public int CalmWedgesEnded;
        public static double KeepGoalFar = 96;       // 6 tiles: nearer than this a way round to the nearest free point is the crowd's normal arrival
        public int CalmGoalsKept;

        /// <summary>No other unit of the spacing layer within AloneRadius of the unit (its own squad does not count).</summary>
        internal bool CalmAlone(ZObject o) => CalmAlone(o, near2);
        bool CalmAlone(ZObject o, List<ZObject> scratch)
        {
            Near(o.CenterX, o.CenterY, (int)AloneRadius, scratch);
            foreach (var u in scratch)
            {
                if (u == o || !Separable(u) || SameSquad(o, u)) continue;
                double dx = CX(u) - CX(o), dy = CY(u) - CY(o);
                if (dx * dx + dy * dy <= AloneRadius * AloneRadius) return false;
            }
            return true;
        }

        /// <summary>The share of the normal spacing this pair keeps (1 = normal). Below 1 only for two units of one team that are both on an
        /// order (part 3): the smaller of the two units' own shares.</summary>
        internal double CloseShare(ZObject a, ZObject b)
        {
            if (!CalmOn || (CalmParts & 4) == 0) return 1.0;
            if (a.Owner != b.Owner || a.Owner == Team.Neutral || a.Waypoints.Count == 0 || b.Waypoints.Count == 0 || (a.Kind != b.Kind && !CloseMixed)) return 1.0;
            double qa = a.Calm.q, qb = b.Calm.q;
            return qa < qb ? qa : qb;
        }

        /// <summary>ZSeparation.cs GateCore: this unit queues behind b (shortened step, or it waits) instead of side-stepping round it (part 3).</summary>
        internal bool CalmQueues(ZObject a, ZObject b)
        {
            if (!CalmOn || (CalmParts & 8) == 0 || b == null) return false;
            if (a.Calm.q >= 1.0 || a.Owner != b.Owner || b.Waypoints.Count == 0 || Static(b) || !Walking(b)) return false;   // (a unit that stands or waits is gone round, as before)
            if (a.Calm.stillSince >= 0 && W.Time - a.Calm.stillSince >= WeaveAfter) return false;      // truly blocked: the side-step is back
            CalmQueued++;
            return true;
        }

        /// <summary>ZSeparation.cs TryShove: a unit of its own team that stands in the way (idle, or waiting for its turn at a bridge or a
        /// lane) is not sent aside (and back) at once: the unit that wants past goes round it; only when that has not worked for
        /// MakeWayAfter s is the standing unit asked to step aside or make way, as before (part 3).</summary>
        internal bool CalmGoesRound(ZObject a, ZObject b)
        {
            if (!CalmOn || (CalmParts & 16) == 0 || b == null || a.Owner != b.Owner) return false;
            var c = a.Calm; double now = W.Time;
            if (c.roundId != b.Id || now - c.roundLast > 1.0) { c.roundId = b.Id; c.roundSince = now; }
            c.roundLast = now;
            if (now - c.roundSince >= MakeWayAfter) return false;
            CalmRounds++;
            return true;
        }
        public static double MakeWayAfter = 1.5;
        public static bool CloseInZoneAlways = true;
        public static bool CloseMixed = true;        // (A/B of the runner: false = a robot and a vehicle of one team never come closer)
        public int CalmRounds;

        /// <summary>ZYield.cs YieldAhead (vehicles first): a robot ON AN ORDER that has just been sent out of the way of a vehicle of its team is
        /// not sent again for YieldRest s: the next vehicles drive through it, as that rule already lets them when a robot is not out of
        /// the way in time (nobody is hurt). A robot without an order steps aside as before (part 3).</summary>
        internal bool CalmYieldRest(ZObject v, ZObject r)
        {
            if (ZWorld.FriendsOverlap) return true;                                                // the option FRIENDLY UNITS OVERLAP: the vehicle drives through the robots of its team, nobody is sent aside (ZPathing.Overlap.cs)
            if (!CalmOn || (CalmParts & 32) == 0 || r.Waypoints.Count == 0) return false;
            if (v.PathRemaining() < NearGoal) return false;                                         // the vehicle is about to stop here: the robot makes room, as before
            var c = r.Calm; double now = W.Time;
            if (now < c.yieldRestUntil) { CalmYieldRests++; return true; }
            c.yieldRestUntil = now + YieldRest;
            return false;
        }
        public static double YieldRest = 20.0;
        public int CalmYieldRests;

        /// <summary>EndTick: the per-unit spacing share (part 3).</summary>
        void CalmTick()
        {
            if (!CalmOn || (CalmParts & 4) == 0) return;
            double now = W.Time;
            foreach (var u in units)
            {
                var c = u.Calm; var s = u.PS;
                if (u.Waypoints.Count == 0 || Static(u) || CalmPassing(u)) { c.q = 1.0; c.pressSince = -1; c.lastPress = -100; c.stillSince = -1; continue; }
                var by = s.heldBy;
                bool pressed = (s.wait == ZPathWait.Queue || s.wait == ZPathWait.Sidestep || s.wait == ZPathWait.Held) && by != null && by != u && by.Owner == u.Owner && by.Waypoints.Count > 0;
                if (pressed) { if (c.pressSince < 0) c.pressSince = now; c.lastPress = now; }
                else if (now - c.lastPress > PressGap) c.pressSince = -1;
                // not moved (a standing unit of the layer keeps its heading: the position tells)
                if (double.IsNaN(c.lastX) || Math.Abs(u.PosX - c.lastX) + Math.Abs(u.PosY - c.lastY) > 0.01) { c.lastX = u.PosX; c.lastY = u.PosY; c.stillSince = -1; }   // (the first tick takes the position: a comparison with the empty start value is never true)
                else if (c.stillSince < 0) c.stillSince = now;
                bool zone = ZoneAt(u.CenterX, u.CenterY);
                bool close = (zone && CloseInZoneAlways) || (c.pressSince >= 0 && (zone || now - c.pressSince >= PressAfter));
                if (!u.PathPending && u.PathRemaining() < NearGoal)
                {                                                                             // about to stop: back to the normal spacing, whatever the place
                    if (c.q < 1.0 && now >= c.nextStep) { c.q = Math.Min(1.0, c.q + CloseStep); c.nextStep = now + CloseStepTime; }
                }
                else if (close)
                {
                    if (c.q > CloseMin && now >= c.nextStep) { if (c.q >= 1.0) CalmCloseUnits++; c.q = Math.Max(CloseMin, c.q - CloseStep); c.nextStep = now + CloseStepTime; }
                }
                else if (c.q < 1.0 && !zone && now - c.lastPress >= RelaxAfter && now >= c.nextStep && !CalmCrowded(u))
                { c.q = Math.Min(1.0, c.q + CloseStep); c.nextStep = now + CloseStepTime; }
            }
        }

        /// <summary>A unit that goes round a slower one, or lets faster ones out, under the overtake rule keeps the normal spacing (that
        /// rule never lets two units touch). This line is written by the kit's apply script: false in a project without that rule.</summary>
        static bool CalmPassing(ZObject u) => u.Overtaking || u.FallingIn;   // DV105: a unit busy with the passing rule keeps the normal spacing (ZOvertake.cs; both false while the switch is off)

        /// <summary>A unit of its team that is on an order stands or walks inside the NEXT bigger spacing of this unit: growing back now
        /// would only make the layer push the two apart.</summary>
        bool CalmCrowded(ZObject u)
        {
            double q = Math.Min(1.0, u.Calm.q + CloseStep);
            Near(u.CenterX, u.CenterY, MaxQuery, near2);
            foreach (var o in near2)
            {
                if (o == u || !Separable(o) || o.Owner != u.Owner || o.Waypoints.Count == 0 || PairExempt(u, o)) continue;
                double min = GateSpacing(u, o) * q, dx = CX(o) - CX(u), dy = CY(o) - CY(u);
                if (dx * dx + dy * dy < min * min) return true;
            }
            return false;
        }
    }

    public sealed partial class ZWorld
    {
        /// <summary>Calm movement (Sim/ZCalm.cs, Sim/ZObject.Calm.cs). Static like every rule switch: the game sets it at every match start
        /// (View/ZGame.cs: on unless 'Zod exact'); headless default OFF, so every counted check and the parity report run without it unless
        /// they set it. A future online match must send it with the match settings.</summary>
        public static bool CalmMoveDeviation = false;
    }

    /// <summary>Per-unit state of the calm movement rules. Nothing here is read or written while the switch is off.</summary>
    public sealed class ZCalmState
    {
        // ---- the spacing share towards its own team (ZCalm.cs part 3)
        internal double q = 1.0, nextStep = -1, pressSince = -1, lastPress = -100, stillSince = -1, lastX = double.NaN, lastY;
        // ---- the route (part 2)
        internal double routeKeepUntil = -1;
        // ---- going round a unit that waits (part 3)
        internal int roundId = -1; internal double roundSince = -1, roundLast = -100;
        internal int wedges; internal double wedgeLast = -100;      // a squad member's walk back to its leader, wedged against a parked hull
        // ---- the fight rules (ZObject.Calm.cs)
        internal double dodgeStart = -100, dodgeNext = -100, lastShot = -100, dodgeOver = 1e18, leftAt = -100, leftX, leftY, fightSeen = -100, fightX, fightY, noFightUntil = -100; internal int fightRef = -1;   // (a squad's are kept by its leader)
        internal double yieldRestUntil = -1;                // a robot on an order: not sent out of a vehicle's way again before this
    }

    public partial class ZObject
    {
        internal readonly ZCalmState Calm = new ZCalmState();
        /// <summary>The share of the normal spacing this unit keeps from its own team right now (1 = normal). For checks and the view.</summary>
        public double CalmSpacingShare => Calm.q;
        /// <summary>The length of the rest of this unit's planned path in px (-1 while the path is still being searched). For checks.</summary>
        public double CalmPathRest => PathPending ? -1 : PathRemaining();
    }
}
