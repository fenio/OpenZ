using System;
using System.Collections.Generic;

namespace OpenZ.Sim
{
    /// <summary>
    /// OpenZ deviation DV107 "enemy strike groups". The engine's computer never gathers: every 4 to 12 s it orders a random 15 to 35 %
    /// (+1) of its idle units, one unit per nearest target (zbot.cpp:127-201), so its units reach a defended place one by one.
    /// With DV107 the computer now and then collects a group in front of its fort and sends it as one body at the place where most
    /// of the other side's army stands together. Everything is behind ONE switch, <see cref="ZWorld.StrikeGroupDeviation"/>
    /// (Options row ENEMY STRIKE GROUPS, saved in oz_strike_groups, ON by default, set by the game at every start; off in the
    /// engine-exact mode; the static default is off, and the counted checks, the parity report, its Deviations block and the
    /// computer's own checks turn it off for their run (a Play session leaves it on), so they run today's computer unless one opts in).
    /// With the switch off not one line below changes a state: the computer is tick for tick today's.
    /// <para>THE RULE (every number is a constant of ZWorld below).</para>
    /// <list type="bullet">
    /// <item>WHEN. Never before <see cref="ZWorld.StrikeStartSec"/> (8:00) on the game clock; the next window opens no sooner than
    /// <see cref="ZWorld.StrikeGapSec"/> (7:00) after the last group formed and left, and no sooner than
    /// <see cref="ZWorld.StrikeRetrySec"/> after a window that was given up. Every difficulty.</item>
    /// <item>WHO FEEDS IT. The computer's fort and its factories nearest to that fort (centre to centre, the lower id on a tie):
    /// <see cref="ZWorld.StrikeFactories"/> = 1 on a map under <see cref="ZWorld.StrikeSmallMapTiles"/> tiles, 2 under
    /// <see cref="ZWorld.StrikeLargeMapTiles"/>, 3 from there on (the map's size is the only measure; no level is named anywhere). When it owns fewer, all it owns;
    /// with no factory there is no window.</item>
    /// <item>THE WINDOW = one production cycle. It opens the moment one of those buildings turns out a unit and lasts as long as the
    /// slowest of them needs for the unit it is building then (the game's own build-time formula), never longer than
    /// <see cref="ZWorld.StrikeCycleLimitSec"/>. Every armed unit those buildings turn out in the window joins (at most
    /// <see cref="ZWorld.StrikeMaxMembers"/>): it gets a place of its own in front of the fort's doors and walks there (the same
    /// command the computer's own orders use, ZWorld.SendWaypoints); standing on it, it holds position (ZWorld.SendHold). A member
    /// that stands beside its place (the game's own spacing rule ends a walk beside a team-mate that already stands) is sent to
    /// it again, <see cref="ZWorld.StrikeResendTries"/> times at most without coming nearer; then the spot it stands on is its
    /// place when that spot is on the forming ground (out of the gate's corridor, of every exit lane and keep-clear area, not
    /// nearer the gate wall than the places); else it gets the next free place, once; else it is released alone. A member off its
    /// place that has not moved 8 px for <see cref="ZWorld.StrikeGateStandSec"/> on a spot in the gate's way (with an order or
    /// without) gets the next free place, once; still there 2 s later it is released alone.</item>
    /// <item>THE PLACES (<see cref="ZWorld.StrikePlacesFor"/>): open ground in front of the gate wall, from
    /// <see cref="ZWorld.StrikeFrontMinPx"/> to <see cref="ZWorld.StrikeFrontMaxPx"/> px out, up to
    /// <see cref="ZWorld.StrikeSideMaxPx"/> px to each side, a corridor of <see cref="ZWorld.StrikeLaneHalfPx"/> px each side of the
    /// gate line left free all the way out; every place has room for a vehicle with one free tile all round, lies in no building's
    /// exit lane, on no bridge approach, on no flag, on nothing that stands there, is reached by a vehicle (so by a robot too) from
    /// the gate over the forming ground itself, without a bridge and by no long way round, and is at least
    /// <see cref="ZWorld.StrikePlacePitchPx"/> px from every other place. Nearest rows first.</item>
    /// <item>FORMED: every feeding building has delivered once, at least <see cref="ZWorld.StrikeMinMembers"/> members, all of them
    /// standing: the group leaves at once. At the end of the window: with at least that many members it leaves as it is, else the
    /// window is given up.</item>
    /// <item>GIVEN UP while forming (the members are released as singles, each with an order of the computer's own kind at once): a
    /// territory that holds one of the feeding buildings is lost (any other lost territory does not end the window); a robot or
    /// vehicle of the group is destroyed by fire (when it was a squad's leader the window is given up one tick later, when the
    /// squad's next robot is known, so the squad gets its order with the others; a robot taken by a crocodile does NOT end the
    /// window: the group goes on without it); the window ends with too few members; the fort is lost; nobody of the other side is
    /// left to march at.</item>
    /// <item>THE TARGET: the unit (a squad or a vehicle) of the other side with the most units of its side within
    /// <see cref="ZWorld.StrikeGatherPx"/> px of it (itself counted; on a tie the nearest to the group); the target point is the
    /// middle of those units. Every <see cref="ZWorld.StrikeRetargetSec"/> s the middle of the same units is taken again and the
    /// group follows it once it has moved more than <see cref="ZWorld.StrikeRetargetPx"/> px; when none of them is left the
    /// biggest gathering is looked up afresh; when the other side has no robot or vehicle left the group is released.</item>
    /// <item>THE MARCH: one body on ONE common route. When the group leaves, one route is laid from the middle of the formation to
    /// the target point (<see cref="ZWorld.StrikeRouteTiles"/>: the shortest way on the vehicles' graph when the group has a vehicle
    /// and such a way exists, where a vehicle drives a robot walks too; else on the robots' graph), with a point every
    /// <see cref="ZWorld.StrikeRouteStepPx"/> px along it. Every member walks or drives from point to point, then to its own spot at
    /// the target (its place in the formation), with the computer's attack-move order; so a river with two bridges is crossed by
    /// everybody on the same one. Its place on the route's line decides the point it heads for: within
    /// <see cref="ZWorld.StrikeRouteJoinPx"/> px of the line, the end of the stretch it is on (the next point once it is within
    /// <see cref="ZWorld.StrikeRouteAheadPx"/> px of that one); farther off (still on its way from its building when the cycle
    /// ended, or a place to the side of the route) it joins the route at or behind the group's front: where its own shortest way
    /// meets the route when that is not ahead of the front, else at the route point nearest to it; never a point before the one it
    /// has. When the target moves the route is laid again from the LEADING member on its way, the stretch of the old
    /// route between the rear and the leader kept in front of it (nobody is sent back or across). Everybody leaves in the same tick.
    /// Once a second the px of way each member has left to the target point is read along that route (to its next point, then
    /// along the route): the member with
    /// the most left, the rear, moves at its own speed; a member that is more than <see cref="ZWorld.StrikeAheadFreePx"/> px ahead
    /// of the rear slows down, the more the farther ahead, to <see cref="ZWorld.StrikeMinPace"/> of its own speed at
    /// <see cref="ZWorld.StrikeAheadMaxPx"/> px (<see cref="ZObject.StrikePaceNow"/>); nobody stands waiting on the way. So the
    /// group closes up, moves at the pace of its slowest member and arrives together. A member in a fight or in the target area
    /// moves freely (and is not the rear); one that comes out of a fight behind the others is the rear again. While forming and
    /// marching a member is kept out of the computer's order rounds and of the OpenZ rules that order idle units about (the idle
    /// groups DV56, the fort finish DV60, the idle sweep DV70) and its crew does not rest in the hatch (DV75):
    /// <see cref="ZObject.StrikeHeld"/>. The Easy limits are not asked.</item>
    /// <item>RELEASE (all at once, every running order is kept, whoever has none gets one of the computer's own kind): when every
    /// living member has reached the target area (<see cref="ZWorld.StrikeArrivePx"/> px round the target point, or it fights a
    /// unit that stands there, or it fights within <see cref="ZWorld.StrikeBesidePx"/> px of a member that has arrived or of the
    /// target area), or
    /// <see cref="ZWorld.StrikeArriveGraceSec"/> s after half of them have; when fewer than two members are left; when the march
    /// has lasted <see cref="ZWorld.StrikeMarchLimitSec"/> s; when the switch is turned off. A member with no way to the target for
    /// its kind does not march (it is released alone when the group leaves). A single member that stands without
    /// an order outside the area gets its order again, twice; the third time it is released alone. A member on its way (not in
    /// a fight, not stunned) that has not moved <see cref="ZWorld.StrikeStallPx"/> px (times its pace when the rule slows it,
    /// at least <see cref="ZWorld.StrikeMinPace"/>) from where it was in <see cref="ZWorld.StrikeStallSec"/> s (it cannot move)
    /// is released alone with the order it has: nobody slows down for it.</item>
    /// </list>
    /// Deterministic: game time, the object list's order, whole-number positions; no random draw, no frame time, no view state.
    /// </summary>
    public sealed partial class ZWorld
    {
        /// <summary>DV107: the one switch. PlayerPrefs oz_strike_groups (Options, ADVANCED tab), off in the engine-exact mode.</summary>
        public static bool StrikeGroupDeviation = false;

        /// <summary>No window opens before this game time.</summary>
        public const double StrikeStartSec = 480.0;
        /// <summary>The next window opens no sooner than this after a group formed and left.</summary>
        public const double StrikeGapSec = 420.0;
        /// <summary>After a window that was given up: single units only for this long, then it may try again.</summary>
        public const double StrikeRetrySec = 120.0;
        /// <summary>Hard upper limit of the window (one production cycle is the game's own build time of the slowest feeding building).</summary>
        public const double StrikeCycleLimitSec = 180.0;
        /// <summary>The march as one body: a member that has more than StrikeAheadFreePx less way left than the group's rear slows
        /// down, linearly, to StrikeMinPace of its own speed at StrikeAheadMaxPx (it never stands waiting on the way).</summary>
        public const double StrikeMinPace = 0.25;
        public const int StrikeAheadFreePx = 32, StrikeAheadMaxPx = 160;
        /// <summary>The least size that marches; the most members of one group.</summary>
        public const int StrikeMinMembers = 3, StrikeMaxMembers = 12;
        /// <summary>Map sizes (tiles) for 1 / 2 / 3 feeding factories.</summary>
        public const int StrikeSmallMapTiles = 5000, StrikeLargeMapTiles = 10000;
        /// <summary>The forming ground in front of the gate wall (px; 16 px = one tile = 2 m).</summary>
        public const int StrikePlacePitchPx = 48, StrikeFrontMinPx = 96, StrikeFrontMaxPx = 288, StrikeSideMaxPx = 256, StrikeLaneHalfPx = 44;
        /// <summary>A member stands on its place when it has stopped within this many px of it; a member that stands farther off is
        /// sent back to it, once a second at most and StrikeResendTries times without coming nearer (then see ZBot.StrikeFormingStep).</summary>
        public const int StrikeSettlePx = 8, StrikeResendTries = 2;
        /// <summary>The march: a member on its way that has not moved StrikeStallPx (times its pace when the rule slows it) from where
        /// it was in StrikeStallSec is released alone (it cannot move; the group does not creep along for it).</summary>
        public const double StrikeStallSec = 20.0;
        public const int StrikeStallPx = 16;
        /// <summary>"Gathered in one place": units of one side within this many px of a unit. The target area round the target point.</summary>
        public const int StrikeGatherPx = 160, StrikeArrivePx = 352;
        /// <summary>The target is looked at again this often; the group follows a target that moved more than this.</summary>
        public const double StrikeRetargetSec = 5.0;
        public const int StrikeRetargetPx = 64;
        /// <summary>After half of the group has arrived the rest gets this long, then everybody is released.</summary>
        public const double StrikeArriveGraceSec = 15.0;
        /// <summary>Hard limit of a march.</summary>
        public const double StrikeMarchLimitSec = 420.0;
        /// <summary>The common route of a march: a point every StrikeRouteStepPx px along it; a member is sent on to the next point once
        /// it is within StrikeRouteAheadPx of the point it heads for, or past it along the line (it never has to stand on a point).</summary>
        public const int StrikeRouteStepPx = 128, StrikeRouteAheadPx = 64;
        /// <summary>A member within this many px of the route's line is on it (it heads for the end of the stretch it is on); one farther
        /// off joins it where its own shortest way meets it (ZBot.SgPlace).</summary>
        public const int StrikeRouteJoinPx = 96;
        /// <summary>The arrival: a member that fights within this many px of a member that has arrived, or this far outside the target
        /// area, has arrived too.</summary>
        public const int StrikeBesidePx = 64;
        /// <summary>Forming: a member off its place that has not moved 8 px for this long on a spot in the gate's way (the gate's
        /// corridor, an exit lane, a keep-clear area, nearer the gate wall than the places), with an order or without, gets the next
        /// free place, once; still there 2 s later (or with no free place) it is released alone. So nobody stays there 6 s.</summary>
        public const double StrikeGateStandSec = 3.0;

        /// <summary>How many factories feed a group on a map of this many tiles.</summary>
        public static int StrikeFactories(int mapTiles) => StrikeFactoriesForCheck > 0 ? StrikeFactoriesForCheck : mapTiles < StrikeSmallMapTiles ? 1 : mapTiles < StrikeLargeMapTiles ? 2 : 3;
        /// <summary>Counted checks only (what-if rows): this many feeding factories on every map; 0 = by the map's size (the game).</summary>
        public static int StrikeFactoriesForCheck = 0;
        /// <summary>Counted checks only (the before rows of the second round, never the game): true = the first round's rules: any
        /// territory the computer held at the start of the window ends it, a robot taken by a crocodile ends it, and the march has no
        /// common route (each member takes its own way). false = the game.</summary>
        public static bool StrikeRoundOneForCheck = false;

        /// <summary>Counted checks only: what a crocodile's bite does to a robot it takes (ZWorld.Crocodiles.cs), at once.</summary>
        public void StrikeCrocTakeForCheck(ZObject robot)
        {
            if (robot == null || robot.Destroyed) return;
            robot.Eaten = true; robot.EatenBy = 0;
            robot.SetHealth(0);
            UpdateObjectHealth(robot, -1);
        }

        /// <summary>Checks and reports only (never the game): the switch off until the result is disposed, then as it was. A Play
        /// session leaves the switch on in the editor; the parity report, the Deviations block and the computer's own checks
        /// take this hold, so they measure today's computer.</summary>
        public static IDisposable StrikeGroupsOff() => new StrikeOffHold();
        sealed class StrikeOffHold : IDisposable
        {
            readonly bool was = StrikeGroupDeviation;
            public StrikeOffHold() { StrikeGroupDeviation = false; }
            public void Dispose() { StrikeGroupDeviation = was; }
        }

        /// <summary>BuildingCreateUnit hook: building b has just turned out unit u (a squad leader or a vehicle).</summary>
        internal void StrikeNoteBuilt(ZObject b, ZObject u)
        {
            if (!StrikeGroupDeviation || b == null || u == null) return;
            foreach (var bot in Bots) if (bot.Team == b.Owner) bot.StrikeBuilt(b.Id, u.Id);
        }

        /// <summary>The gate of a fort: the exit point, the unit direction out of the gate, the wall the gate is in (px along that direction).</summary>
        public static bool StrikeGate(ZObject fort, out int ex, out int ey, out int dx, out int dy, out int wall)
        {
            ex = ey = dx = dy = wall = 0;
            if (fort == null || !fort.IsFort || !fort.GetCreationPoint(out int cx, out int cy) || !fort.GetCreationMovePoint(out ex, out ey)) return false;
            if (Math.Abs(ey - cy) >= Math.Abs(ex - cx)) { dy = ey > cy ? 1 : -1; wall = dy > 0 ? fort.LocY + fort.HeightPix : fort.LocY; }
            else { dx = ex > cx ? 1 : -1; wall = dx > 0 ? fort.LocX + fort.WidthPix : fort.LocX; }
            return true;
        }

        /// <summary>
        /// The forming places of a fort, best first, at most <paramref name="max"/>. A place is the middle of a 2 x 2 tile box (a
        /// vehicle's own footprint) with one more free tile all round. See the class notes for every condition. Deterministic.
        /// </summary>
        public List<(int x, int y)> StrikePlacesFor(ZObject fort, int max)
        {
            var res = new List<(int x, int y)>();
            if (Pathing == null || !StrikeGate(fort, out int ex, out int ey, out int dx, out int dy, out int wall)) return res;
            // the ground in front of the gate as a vehicle reaches it from the gate without leaving the forming ground and without a
            // bridge: a flood over the path search's own vehicle footprints (2 x 2 tiles; where a vehicle goes a robot goes too)
            int reach = Math.Max(StrikeFrontMaxPx, StrikeSideMaxPx) / 16 + 4, etx = ex >> 4, ety = ey >> 4;
            int wx0 = Math.Max(0, etx - reach), wy0 = Math.Max(0, ety - reach), wx1 = Math.Min(Map.width - 1, etx + reach), wy1 = Math.Min(Map.height - 1, ety + reach);
            int ww = wx1 - wx0 + 1, wh = wy1 - wy0 + 1;
            var steps = new int[ww * wh];
            for (int i = 0; i < steps.Length; i++) steps[i] = -1;
            bool OnBridge(int tx, int ty)
            {
                foreach (var o in Objects)
                    if (o.Kind == ZKind.Building && o.IsBridge && o.LocX < tx * 16 + 32 && tx * 16 < o.LocX + o.WidthPix && o.LocY < ty * 16 + 32 && ty * 16 < o.LocY + o.HeightPix) return true;
                return false;
            }
            var queue = new List<int>();
            for (int s = 0; s <= 8 && queue.Count == 0; s++)
            {
                var (tx, ty) = ZReach.FootTileAt(ex + dx * s * 16, ey + dy * s * 16, false);
                if (tx < wx0 || ty < wy0 || tx > wx1 || ty > wy1 || !Path.StepOk(tx, ty, tx, ty, false, false) || OnBridge(tx, ty)) continue;
                steps[(ty - wy0) * ww + (tx - wx0)] = 0; queue.Add((ty - wy0) * ww + (tx - wx0));
            }
            for (int qi = 0; qi < queue.Count; qi++)
            {
                int cur = queue[qi], cx = cur % ww + wx0, cy = cur / ww + wy0;
                for (int oy = -1; oy <= 1; oy++)
                    for (int ox = -1; ox <= 1; ox++)
                    {
                        int nx = cx + ox, ny = cy + oy;
                        if ((ox == 0 && oy == 0) || nx < wx0 || ny < wy0 || nx > wx1 || ny > wy1) continue;
                        int ni = (ny - wy0) * ww + (nx - wx0);
                        if (steps[ni] >= 0 || !Path.StepOk(cx, cy, nx, ny, false, false) || OnBridge(nx, ny)) continue;
                        steps[ni] = steps[cur] + 1; queue.Add(ni);
                    }
            }
            if (queue.Count == 0) return res;
            var cand = new List<(int score, int x, int y)>();
            for (int ty = wy0; ty <= wy1; ty++)
                for (int tx = wx0; tx <= wx1; tx++)
                {
                    if (tx < 2 || ty < 2 || tx + 3 >= Map.width || ty + 3 >= Map.height) continue;
                    int x = tx * 16 + 16, y = ty * 16 + 16;
                    int front = dy != 0 ? (y - wall) * dy : (x - wall) * dx, side = dy != 0 ? Math.Abs(x - ex) : Math.Abs(y - ey);
                    if (front < StrikeFrontMinPx || front > StrikeFrontMaxPx || side < StrikeLaneHalfPx || side > StrikeSideMaxPx) continue;
                    int st = steps[(ty - wy0) * ww + (tx - wx0)];
                    if (st < 0 || st * 16 > (front + side) * 3 / 2 + 96) continue;        // reached from the gate, and by no long way round
                    bool ok = true, road = false;
                    for (int j = ty - 1; j <= ty + 2 && ok; j++)
                        for (int i = tx - 1; i <= tx + 2; i++)
                        {
                            var c = Map.ClassAt(i, j);
                            if (!Path.TilePassable(i, j, false) || c == ZClass.Water || c == ZClass.DeepWater || c == ZClass.Impassable) { ok = false; break; }
                            if (c == ZClass.Road && i >= tx && i <= tx + 1 && j >= ty && j <= ty + 1) road = true;
                        }
                    if (!ok) continue;
                    if (InExitLane(x, y, 26) || InBuilding(x, y, 34) || Pathing.StrikeKeepClear(x, y)) continue;
                    foreach (var f in Flags) if (Math.Abs(f.CenterX - x) < 48 && Math.Abs(f.CenterY - y) < 48) { ok = false; break; }   // nobody parks on a flag
                    if (!ok) continue;
                    foreach (var o in Objects)
                    {   // nothing that stands there for good: rocks, huts, crates, wrecks, props, guns, empty vehicles
                        if (o.Destroyed || o.KillAt >= 0 || o.Kind == ZKind.Building || o.Type == "flag") continue;
                        if (!(o.Kind == ZKind.MapItem || o.Kind == ZKind.Cannon || (o.Kind == ZKind.Vehicle && o.Owner == Team.Neutral))) continue;
                        if (o.LocX < x + 28 && x - 28 < o.LocX + o.WidthPix && o.LocY < y + 28 && y - 28 < o.LocY + o.HeightPix) { ok = false; break; }
                    }
                    if (!ok) continue;
                    cand.Add((front * 2 + side + (road ? 96 : 0), x, y));               // nearest rows first, then nearest the gate line; a road only when nothing else is left near
                }
            cand.Sort((a, b) => a.score != b.score ? a.score.CompareTo(b.score) : a.y != b.y ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x));
            long pitch2 = (long)StrikePlacePitchPx * StrikePlacePitchPx;
            foreach (var c in cand)
            {
                if (res.Count >= max) break;
                bool far = true;
                foreach (var p in res) if ((long)(p.x - c.x) * (p.x - c.x) + (long)(p.y - c.y) * (p.y - c.y) < pitch2) { far = false; break; }
                if (far) res.Add((c.x, c.y));
            }
            return res;
        }

        /// <summary>
        /// The ways to a point, for the march: for every footprint tile of the path search's own graph (robots: one tile; vehicles:
        /// the top-left tile of their 2 x 2) the px of the shortest way from there to (x, y) over that graph (a straight step 16 px,
        /// a diagonal one 22.6). double.MaxValue = no way. Deterministic (a fixed order on equal lengths).
        /// </summary>
        public double[] StrikeWayField(int x, int y, bool robot)
        {
            int w = Path.W, h = Path.H, n = w * h;
            var d = new double[n];
            for (int i = 0; i < n; i++) d[i] = double.MaxValue;
            var heap = new StrikeHeap(n);
            var (tx0, ty0) = ZReach.FootTileAt(x, y, robot);
            for (int r = 0; r <= 8 && heap.Count == 0; r++)                              // the valid footprints nearest the point
                for (int ty = ty0 - r; ty <= ty0 + r; ty++)
                    for (int tx = tx0 - r; tx <= tx0 + r; tx++)
                    {
                        if (Math.Max(Math.Abs(tx - tx0), Math.Abs(ty - ty0)) != r || tx < 0 || ty < 0 || tx >= w || ty >= h) continue;
                        if (!Path.StepOk(tx, ty, tx, ty, robot, false)) continue;
                        d[ty * w + tx] = 0; heap.Push(ty * w + tx, 0);
                    }
            while (heap.Count > 0)
            {
                heap.Pop(out int cur, out double cd);
                if (cd > d[cur]) continue;
                int cx = cur % w, cy = cur / w;
                for (int oy = -1; oy <= 1; oy++)
                    for (int ox = -1; ox <= 1; ox++)
                    {
                        if (ox == 0 && oy == 0) continue;
                        int nx = cx + ox, ny = cy + oy;
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h || !Path.StepOk(nx, ny, cx, cy, robot, false)) continue;   // walked the other way round: from (nx, ny) to here
                        double nd = cd + (ox != 0 && oy != 0 ? 22.627416997969522 : 16.0);
                        int ni = ny * w + nx;
                        if (nd < d[ni]) { d[ni] = nd; heap.Push(ni, nd); }
                    }
            }
            return d;
        }

        /// <summary>
        /// The common route of a march over a way field (StrikeWayField of the target point): from the valid footprint nearest
        /// (fromX, fromY) downhill to the target, always to the neighbour with the least way left that can be stepped onto; a point
        /// (its px and its way left) every StrikeRouteStepPx px, none inside the target area (StrikeArrivePx). false = no way from
        /// there. Deterministic (a fixed order on equal values).
        /// </summary>
        public bool StrikeRouteTiles(double[] field, int fromX, int fromY, bool robot, List<(int x, int y, double way)> pts)
        {
            pts.Clear();
            int w = Path.W, h = Path.H;
            var (tx0, ty0) = ZReach.FootTileAt(fromX, fromY, robot);
            int cur = -1; double cd = double.MaxValue;
            for (int r = 0; r <= 4 && cur < 0; r++)
                for (int ty = ty0 - r; ty <= ty0 + r; ty++)
                    for (int tx = tx0 - r; tx <= tx0 + r; tx++)
                    {
                        if (Math.Max(Math.Abs(tx - tx0), Math.Abs(ty - ty0)) != r || tx < 0 || ty < 0 || tx >= w || ty >= h) continue;
                        double v = field[ty * w + tx];
                        if (v < cd) { cd = v; cur = ty * w + tx; }
                    }
            if (cur < 0 || cd == double.MaxValue) return false;
            int half = robot ? 8 : 16;
            double run = 0;
            for (int guard = 0; guard < w * h && field[cur] > ZWorld.StrikeArrivePx; guard++)
            {
                int cx = cur % w, cy = cur / w, next = -1; double nd = field[cur];
                for (int oy = -1; oy <= 1; oy++)
                    for (int ox = -1; ox <= 1; ox++)
                    {
                        if (ox == 0 && oy == 0) continue;
                        int nx = cx + ox, ny = cy + oy;
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                        int ni = ny * w + nx;
                        if (field[ni] < nd && Path.StepOk(cx, cy, nx, ny, robot, false)) { nd = field[ni]; next = ni; }
                    }
                if (next < 0) break;
                run += field[cur] - field[next];
                cur = next;
                if (run >= ZWorld.StrikeRouteStepPx && field[cur] > ZWorld.StrikeArrivePx) { pts.Add((cur % w * 16 + half, cur / w * 16 + half, field[cur])); run = 0; }
            }
            return true;
        }

        /// <summary>A unit's value in a way field (its own footprint, else the best valid footprint within two tiles).</summary>
        public double StrikeWayOf(double[] field, ZObject u)
        {
            int w = Path.W, h = Path.H;
            var (tx, ty) = ZReach.FootTile(u.LocX, u.LocY);
            double best = double.MaxValue;
            for (int r = 0; r <= 2 && best == double.MaxValue; r++)
                for (int y = ty - r; y <= ty + r; y++)
                    for (int x = tx - r; x <= tx + r; x++)
                    {
                        if (Math.Max(Math.Abs(x - tx), Math.Abs(y - ty)) != r || x < 0 || y < 0 || x >= w || y >= h) continue;
                        if (field[y * w + x] < best) best = field[y * w + x];
                    }
            return best;
        }

        /// <summary>A small binary heap (index, key), the lower key first; on equal keys the lower index, so the order is fixed.</summary>
        sealed class StrikeHeap
        {
            int[] id; double[] key; public int Count;
            public StrikeHeap(int cap) { id = new int[Math.Max(16, cap)]; key = new double[id.Length]; }
            static bool Less(double ka, int ia, double kb, int ib) => ka < kb || (ka == kb && ia < ib);
            public void Push(int i, double k)
            {
                if (Count == id.Length) { Array.Resize(ref id, Count * 2); Array.Resize(ref key, Count * 2); }
                int c = Count++;
                while (c > 0) { int p = (c - 1) >> 1; if (!Less(k, i, key[p], id[p])) break; id[c] = id[p]; key[c] = key[p]; c = p; }
                id[c] = i; key[c] = k;
            }
            public void Pop(out int i, out double k)
            {
                i = id[0]; k = key[0];
                int li = id[--Count]; double lk = key[Count]; int c = 0;
                while (true)
                {
                    int l = c * 2 + 1; if (l >= Count) break;
                    int r = l + 1, m = r < Count && Less(key[r], id[r], key[l], id[l]) ? r : l;
                    if (!Less(key[m], id[m], lk, li)) break;
                    id[c] = id[m]; key[c] = key[m]; c = m;
                }
                if (Count > 0) { id[c] = li; key[c] = lk; }
            }
        }
    }

    public sealed partial class ZPathing
    {
        /// <summary>DV107: (x, y) lies in a bridge's or a narrow passage's keep-clear area (nobody parks there).</summary>
        internal bool StrikeKeepClear(int x, int y) => KeepClearAt(x, y) != null;
    }

    public partial class ZObject
    {
        /// <summary>DV107: 0 = not in a strike group, 1 = forming, 2 = marching. Written by the computer's strike group code only.</summary>
        internal byte strikeHold;
        /// <summary>DV107: this squad leader or vehicle is a member of a strike group that is forming or marching: the computer's
        /// order rounds and the rules for idle units leave it alone. Always false with the switch off and after the game is over.</summary>
        public bool StrikeHeld => strikeHold != 0 && ZWorld.StrikeGroupDeviation && World != null && !World.GameOver;
        /// <summary>DV107: the share of its own speed a body of a marching strike group moves at (under 1 while it is ahead of the
        /// group's rear; 1 = its own speed). Written by the strike group code only.</summary>
        internal double strikePace = 1.0;
        /// <summary>DV107: this robot's loss to a crocodile was counted by a strike group (once).</summary>
        internal bool strikeEatenSeen;
        /// <summary>DV107 (ProcessMove): the pace of this body now: under 1 only while its squad leader (or itself) marches in a strike
        /// group; 1 with the switch off, after a release, after the game is over.</summary>
        public double StrikePaceNow => strikePace < 1.0 && (Leader ?? this).strikeHold == 2 && ZWorld.StrikeGroupDeviation && World != null && !World.GameOver ? strikePace : 1.0;
    }

    public sealed partial class ZBot
    {
        public enum StrikePhase { Waiting = 0, Forming = 1, Marching = 2 }
        public enum StrikeEventKind
        {
            WindowOpened,   // n = feeding buildings, x / y = the gate's exit point, note = the cycle in s and the buildings
            Joined,         // unitId, x / y = its place, n = the building that made it
            Settled,        // unitId: stands on its place for the first time
            Formed,         // n = members, x / y = the target point, note = how it came about
            GivenUp,        // n = members released, note = why
            Departed,       // unitId, x / y = its spot at the target
            Retargeted,     // x / y = the new target point, n = units of the gathering
            Arrived,        // unitId: inside the target area for the first time
            Released,       // n = members released, note = why
            MemberLost,     // unitId: destroyed (or its vehicle lost its driver)
            MemberLeft,     // unitId: released alone, note = why
            SingleOrder,    // unitId, n = the target it was sent to: the order a released member with nothing to do gets
            NoOrder,        // unitId: released with nothing to do and the computer has no target for it
            Reissued,       // unitId: stood without an order, got it again
            PlaceChanged,   // unitId, x / y = its place from now on, n = 1: the spot it stands on, 2: the next free place
            Eaten,          // unitId: a robot of the group (a squad's robot or its leader) taken by a crocodile; the group goes on
            Route,          // n = points of the common route, x / y = the first point, note = the graph it was laid on
        }
        public struct StrikeEvent { public double time; public StrikeEventKind kind; public int group, unitId, x, y, n; public string note; }

        /// <summary>A/B checks only: this computer player forms no strike groups although the switch is on.</summary>
        public bool StrikeGroupsOn = true;
        /// <summary>DV107: what happened, in order (checks, proofs, a view that wants to announce a group). At most 4000 entries.</summary>
        public readonly List<StrikeEvent> StrikeEvents = new List<StrikeEvent>();
        public StrikePhase StrikePhaseNow => sgPhase;
        /// <summary>Windows opened / groups that formed and left / windows given up.</summary>
        public int StrikeWindows { get; private set; }
        public int StrikeGroupsFormed { get; private set; }
        public int StrikeGivenUp { get; private set; }
        /// <summary>Robots of a group taken by crocodiles (they never end a window).</summary>
        public int StrikeEatenCount { get; private set; }
        /// <summary>Orders this rule gave (walk to the place, hold, march, march again); the engine's own counter Orders is not touched by them.</summary>
        public int StrikeOrders { get; private set; }
        /// <summary>The living members' ids; the places of the open window; the feeding buildings; the target point; the game time the next window may open.</summary>
        public List<int> StrikeMemberIds() { var l = new List<int>(); foreach (var m in sgMembers) l.Add(m.o.Id); return l; }
        public IReadOnlyList<(int x, int y)> StrikePlaces => sgPlaces;
        public IReadOnlyList<int> StrikeFeeders => sgFeeders;
        public (int x, int y) StrikeTarget => (sgTx, sgTy);
        public double StrikeAllowedAt => Math.Max(sgAllowedAt, ZWorld.StrikeStartSec * EasySlowScale);   // K434: the Easy computer's first window 1.3 x later (Sim/ZBot.EasySlow.cs)
        public double StrikeWindowEnd => sgDeadline;
        /// <summary>The common route of the march now: the group's start, the points, the target point (checks, proofs, a view).
        /// Empty while no group marches or when no route could be laid.</summary>
        public List<(int x, int y)> StrikeRouteLine()
        {
            var l = new List<(int x, int y)>();
            if (sgPhase != StrikePhase.Marching || sgRouteField == null) return l;
            l.Add((sgRouteFromX, sgRouteFromY)); foreach (var p in sgRoute) l.Add((p.x, p.y)); l.Add((sgTx, sgTy));
            return l;
        }
        /// <summary>The territories (flag ids) whose loss ends the open window: those that hold a feeding building.</summary>
        public IReadOnlyList<int> StrikeWatchedFlags => sgFlags;
        /// <summary>One line per member of the open window or the marching group (checks, proofs): type, id, where it is, the path
        /// it has left, its pace, what it does.</summary>
        public string StrikeMemberLines()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var m in sgMembers)
                sb.Append($"{m.o.Type}#{m.o.Id} at ({m.o.CenterX},{m.o.CenterY}) way {m.way:F0} px pace {m.o.StrikePaceNow:F2} speed {m.o.RealMoveSpeed:F1}" +
                          $"{(sgPhase == StrikePhase.Marching ? $" leg {m.leg}/{sgRoute.Count}{(m.leg >= 0 && m.leg < sgRoute.Count ? $" to ({sgRoute[m.leg].x},{sgRoute[m.leg].y})" : "")}" : "")}" +
                          $"{(m.arrivedAt >= 0 ? " ARRIVED" : "")}{(SgFighting(m.o) ? " FIGHTS" : "")}{(SgIdle(m.o) ? " STANDS" : "")}; ");
            return sb.ToString();
        }

        sealed class SgMember
        {
            public ZObject o; public int place, bodies, reissues, px, py, sx, sy, tries, bx, by, leg = -1, gx, gy;
            public readonly List<ZObject> crew = new List<ZObject>();                         // its bodies (the leader or vehicle and its squad's robots) at the last look
            public double joined, way = -1, arrivedAt = -1, off = -1, best = -1, bestAt, gateAt = -1; public bool settled, departed, moved, fell, onRoute;
        }

        StrikePhase sgPhase;
        bool sgLeaderFell;                                                                      // a squad leader of the forming group fell in this tick: the window is given up in the next one
        int sgGroup, sgTx, sgTy, sgStarted;
        double sgNextStep, sgAllowedAt = ZWorld.StrikeStartSec, sgOpened, sgDeadline, sgGo, sgNextRetarget, sgHalfAt = -1;
        ZObject sgFort;
        readonly List<int> sgFeeders = new List<int>(), sgFlags = new List<int>(), sgTargetIds = new List<int>();
        readonly Dictionary<int, int> sgDelivered = new Dictionary<int, int>();
        readonly List<(int x, int y)> sgPlaces = new List<(int x, int y)>();
        readonly List<SgMember> sgMembers = new List<SgMember>();
        readonly List<(int b, int u)> sgBuilt = new List<(int b, int u)>();

        void SgLog(StrikeEventKind kind, int unitId = -1, int x = 0, int y = 0, int n = 0, string note = null)
        {
            if (StrikeEvents.Count < 4000) StrikeEvents.Add(new StrikeEvent { time = W.Time, kind = kind, group = sgGroup, unitId = unitId, x = x, y = y, n = n, note = note });
            if (kind != StrikeEventKind.Settled && kind != StrikeEventKind.Arrived && kind != StrikeEventKind.Departed && kind != StrikeEventKind.Joined && kind != StrikeEventKind.SingleOrder && kind != StrikeEventKind.Reissued)
                Log.Add($"{W.Time:F0}s {Team} DV107 strike group {sgGroup}: {kind}{(unitId >= 0 ? " #" + unitId : "")}{(n != 0 ? " n " + n : "")}{(note != null ? " (" + note + ")" : "")}");
        }

        /// <summary>ZWorld.StrikeNoteBuilt: one of our buildings turned out a unit (looked at in this tick's StrikeGroupTick).</summary>
        internal void StrikeBuilt(int buildingId, int unitId) => sgBuilt.Add((buildingId, unitId));

        // ------------------------------------------------------------------ the tick (ZBot.Tick, every server tick, before the 1 s planner)
        void StrikeGroupTick()
        {
            if (!ZWorld.StrikeGroupDeviation || !StrikeGroupsOn || Ignored)
            {
                if (sgBuilt.Count > 0) sgBuilt.Clear();
                sgLeaderFell = false;
                if (sgPhase != StrikePhase.Waiting) StrikeEnd(StrikeEventKind.Released, "switched off", false);
                return;
            }
            if (sgPhase != StrikePhase.Waiting) StrikeUpkeep();
            if (sgLeaderFell) { sgLeaderFell = false; return; }                           // the squad's next leader is known in the next tick: nothing else is decided in this one
            if (sgBuilt.Count > 0)
            {
                for (int i = 0; i < sgBuilt.Count; i++) StrikeRecruit(sgBuilt[i].b, sgBuilt[i].u);
                sgBuilt.Clear();
            }
            if (W.Time + TIME_EPS < sgNextStep) return;
            sgNextStep = W.Time + 1.0;
            if (sgPhase == StrikePhase.Forming) StrikeFormingStep();
            else if (sgPhase == StrikePhase.Marching) StrikeMarchStep();
        }

        static bool SgAlive(ZObject o) => o != null && !o.Destroyed && o.KillAt < 0;
        /// <summary>The pace of a member's bodies (the leader and its squad): 1 = their own speed.</summary>
        static void SgPace(ZObject o, double pace)
        {
            if (pace > 1.0) pace = 1.0;
            o.strikePace = pace; foreach (var mn in o.Minions) if (mn != null) mn.strikePace = pace;
        }
        /// <summary>A body leaves the group: no hold, its own speed.</summary>
        static void SgFree(ZObject o) { o.strikeHold = 0; SgPace(o, 1.0); }

        /// <summary>Every tick: who was destroyed, whose squad has a new leader, who is no longer ours.</summary>
        void StrikeUpkeep()
        {
            bool hit = false;
            for (int i = 0; i < sgMembers.Count;)
            {
                var m = sgMembers[i]; var o = m.o;
                bool here = SgAlive(o) && W.Get(o.Id) == o && o.Owner == Team;
                if (here && SgCrewLost(m)) hit = true;                                                             // a robot of the squad fell to fire (one a crocodile took does not count)
                if (here) { i++; continue; }
                if (W.Get(o.Id) == o && o.Destroyed && o.Owner == Team && o.Minions.Count > 0)
                {   // a squad leader fell in this tick: its squad's next leader is known in the next one. While forming the window is
                    // given up THEN, so that the squad's next robot is the member and gets its order with the others (an order also
                    // ends its hold); giving up now would leave the squad standing at the fort with no order.
                    // (One tick only: should the fallen leader still be there in the next tick, the window is given up as it is.)
                    if (sgPhase == StrikePhase.Forming && !m.fell) { m.fell = true; sgLeaderFell = true; } else if (!SgByCroc(o)) hit = true;
                    i++; continue;
                }
                double had = o.strikePace;
                SgFree(o);
                var next = o.Successor;                                                                            // the squad goes on under its next robot
                if (o.Destroyed && next != null && next.Owner == Team && !next.IsMinion)
                {
                    if (!SgByCroc(o)) hit = true;                                                                  // a leader a crocodile took: the squad goes on, the window too
                    m.o = next; m.bodies = 1 + next.Minions.Count; SgCrew(m); next.strikeHold = (byte)sgPhase; SgPace(next, had);
                    continue;                                                                                      // (looked at again: it may have fallen too)
                }
                if (o.Destroyed && SgByCroc(o)) { }                                                                // taken by a crocodile: no loss to fire, the group goes on
                else if (o.Destroyed || o.Owner != Team) { hit = true; SgLog(StrikeEventKind.MemberLost, o.Id); }
                else SgLog(StrikeEventKind.MemberLeft, o.Id, 0, 0, 0, "no longer on the ground");                 // drove into a repair bay, boarded a vehicle
                sgMembers.RemoveAt(i);
            }
            if (sgPhase == StrikePhase.Forming && hit && !sgLeaderFell) StrikeEnd(StrikeEventKind.GivenUp, "a robot or vehicle of the group was destroyed", true);
            else if (sgPhase == StrikePhase.Marching && sgMembers.Count < 2) StrikeEnd(StrikeEventKind.Released, "fewer than two members left", true);
        }

        /// <summary>The bodies of a member as they are now (the leader or vehicle and its squad's robots).</summary>
        static void SgCrew(SgMember m)
        {
            m.crew.Clear(); m.crew.Add(m.o);
            foreach (var mn in m.o.Minions) if (mn != null && !mn.Destroyed) m.crew.Add(mn);
        }

        /// <summary>This destroyed body was taken by a crocodile (no loss to fire: it never ends a window). Counted once. In the
        /// first round's before row (checks only) it counts as a loss.</summary>
        bool SgByCroc(ZObject b)
        {
            if (b == null || !b.Eaten || ZWorld.StrikeRoundOneForCheck) return false;
            if (!b.strikeEatenSeen) { b.strikeEatenSeen = true; StrikeEatenCount++; SgLog(StrikeEventKind.Eaten, b.Id); }
            return true;
        }

        /// <summary>A robot of the member's squad is gone since the last look: true when one fell to fire. A robot a crocodile took is
        /// counted (StrikeEatenCount) and does not end the window; one that left the squad alive is no loss.</summary>
        bool SgCrewLost(SgMember m)
        {
            bool fire = false, changed = m.crew.Count != 1 + m.o.Minions.Count;
            foreach (var b in m.crew)
            {
                if (b == m.o || (!b.Destroyed && b.Leader == m.o)) continue;
                changed = true;
                if (b.Destroyed && !SgByCroc(b)) fire = true;
            }
            if (changed) { SgCrew(m); m.bodies = m.crew.Count; }
            return fire;
        }

        // ------------------------------------------------------------------ forming
        ZObject StrikeFort()
        {
            foreach (var o in W.Objects) if (o.IsFort && o.Owner == Team && !o.Destroyed) return o;
            return null;
        }

        /// <summary>The buildings that feed a group now: the fort, then its nearest factories (public for the checks).</summary>
        public List<ZObject> StrikeFeedersNow()
        {
            var res = new List<ZObject>(); var fort = StrikeFort();
            if (fort == null) return res;
            res.Add(fort);
            var fac = new List<ZObject>();
            foreach (var b in W.Objects) if (b.Kind == ZKind.Building && b.Owner == Team && !b.Destroyed && b.IsProducer && !b.IsFort) fac.Add(b);
            fac.Sort((a, b) => { long da = SgD2(a, fort), db = SgD2(b, fort); return da != db ? da.CompareTo(db) : a.Id.CompareTo(b.Id); });
            int n = ZWorld.StrikeFactories(W.Map.width * W.Map.height);
            for (int i = 0; i < fac.Count && i < n; i++) res.Add(fac[i]);
            return res;
        }
        static long SgD2(ZObject a, ZObject b) { long dx = a.CenterX - b.CenterX, dy = a.CenterY - b.CenterY; return dx * dx + dy * dy; }

        /// <summary>One production cycle of the feeding buildings now, in game seconds: the longest build time among the units
        /// they are building (the game's own formula: flags held, damage), capped (public for the checks).</summary>
        public double StrikeCycleNow(List<ZObject> feeders)
        {
            double c = 0;
            foreach (var b in feeders)
            {
                if (b.CurUnit == null) continue;
                var u = W.S.Unit(b.CurUnit); if (u == null) continue;
                c = Math.Max(c, b.BuildTimeModified(u.build_time));
            }
            return Math.Min(ZWorld.StrikeCycleLimitSec, c);
        }

        /// <summary>Building b turned out unit u in this tick: it opens a window when one may open, and joins an open one.</summary>
        void StrikeRecruit(int bId, int uId)
        {
            var b = W.Get(bId); var u = W.Get(uId);
            if (b == null || u == null || u.Owner != Team || !u.IsMobile || u.IsMinion || !SgAlive(u) || u.IsFortDefender || !u.CanMove || !u.CanAttack()) return;
            if (sgPhase == StrikePhase.Waiting)
            {
                if (W.Time + TIME_EPS < Math.Max(sgAllowedAt, ZWorld.StrikeStartSec * EasySlowScale) || W.GameOver) return;
                if (!StrikeOpen(b)) return;
            }
            if (sgPhase != StrikePhase.Forming || !sgFeeders.Contains(bId) || SgEasyHeld) return;   // K434: nobody joins an Easy group while it stands its extra time
            sgDelivered[bId] = sgDelivered.TryGetValue(bId, out int had) ? had + 1 : 1;
            if (sgMembers.Count >= ZWorld.StrikeMaxMembers) return;
            int p = StrikeFreePlace(u);
            if (p < 0) return;
            var m = new SgMember { o = u, place = p, bodies = 1 + u.Minions.Count, joined = W.Time, px = sgPlaces[p].x, py = sgPlaces[p].y };
            sgMembers.Add(m); u.strikeHold = 1; SgCrew(m);
            StrikeMove(u, m.px, m.py);
            SgLog(StrikeEventKind.Joined, u.Id, m.px, m.py, bId);
        }

        bool StrikeOpen(ZObject b)
        {
            var feeders = StrikeFeedersNow();
            if (feeders.Count < 2 || !feeders.Contains(b)) return false;                   // the fort and at least one factory; the window opens with a unit of one of them
            var places = W.StrikePlacesFor(feeders[0], ZWorld.StrikeMaxMembers);
            if (places.Count < ZWorld.StrikeMinMembers) return false;
            sgGroup++; StrikeWindows++;
            sgFort = feeders[0];
            sgFeeders.Clear(); sgDelivered.Clear(); sgPlaces.Clear(); sgFlags.Clear(); sgMembers.Clear(); sgTargetIds.Clear();
            string names = "";
            foreach (var f in feeders) { sgFeeders.Add(f.Id); sgDelivered[f.Id] = 0; names += (names.Length > 0 ? ", " : "") + f.Type + "(" + f.LocX / 16 + "," + f.LocY / 16 + ") " + (f.CurUnit ?? "-"); }
            sgPlaces.AddRange(places);
            foreach (var f in W.Flags)
            {   // the territories whose loss ends the window: those that hold one of the feeding buildings (the fort's own ground has no
                // flag: a lost fort ends the window by itself). The first round's before row (checks only) watched every one it held.
                if (f.Owner != Team) continue;
                bool feeds = ZWorld.StrikeRoundOneForCheck;
                foreach (var fb in feeders) if (W.ZoneOf(fb)?.Flag == f) { feeds = true; break; }
                if (feeds) sgFlags.Add(f.Id);
            }
            double cycle = StrikeCycleNow(feeders);
            if (cycle <= 0) cycle = ZWorld.StrikeCycleLimitSec;
            sgOpened = W.Time; sgDeadline = W.Time + cycle; sgHalfAt = -1;
            sgPhase = StrikePhase.Forming;
            ZWorld.StrikeGate(sgFort, out int ex, out int ey, out _, out _, out _);
            SgLog(StrikeEventKind.WindowOpened, -1, ex, ey, feeders.Count, $"cycle {cycle:F0} s; {names}; {places.Count} places");
            return true;
        }

        /// <summary>The first place nobody of the group has and nobody else stands on.</summary>
        int StrikeFreePlace(ZObject u)
        {
            for (int p = 0; p < sgPlaces.Count; p++)
            {
                bool free = true;
                foreach (var m in sgMembers) if (m.place == p) { free = false; break; }
                if (!free) continue;
                int x = sgPlaces[p].x, y = sgPlaces[p].y;
                foreach (var o in W.Objects)
                {
                    if (!o.IsUnit || o == u || !SgAlive(o) || o.IsFortDefender || o.Leader == u || o.strikeHold != 0) continue;
                    if (o.Waypoints.Count > 0 && o.IsMoving) continue;                      // passing through
                    long lim = 28 + ZPathing.Radius(o), ddx = o.CenterX - x, ddy = o.CenterY - y;
                    if (ddx * ddx + ddy * ddy < lim * lim) { free = false; break; }
                }
                if (free) return p;
            }
            return -1;
        }

        /// <summary>The computer's move order (attack-move, like every order of zbot.cpp) through the command layer.</summary>
        bool StrikeMove(ZObject u, int x, int y)
        {
            var wp = new ZWaypoint { mode = WpMode.Move, refId = -1, x = x, y = y, attackTo = true, playerGiven = true };
            if (!W.SendWaypoints(Team, u, new[] { wp })) return false;
            if (ClientView) Relay(u);
            StrikeOrders++;
            return true;
        }

        static bool SgIdle(ZObject o) => o.Waypoints.Count == 0 && !o.IsMoving;

        /// <summary>A member may take the spot it stands on as its place: on the forming ground (no nearer the gate wall and no
        /// farther out or to the side than the places, one place more allowed), outside the corridor that is kept free along the
        /// gate line, in no building's exit lane and in no keep-clear area of a bridge or a narrow passage.</summary>
        bool StrikeSpotOk(ZObject o)
        {
            if (!ZWorld.StrikeGate(sgFort, out int ex, out int ey, out int gdx, out int gdy, out int wall)) return false;
            int x = o.CenterX, y = o.CenterY;
            int front = gdy != 0 ? (y - wall) * gdy : (x - wall) * gdx, side = gdy != 0 ? Math.Abs(x - ex) : Math.Abs(y - ey);
            if (front < ZWorld.StrikeFrontMinPx || front > ZWorld.StrikeFrontMaxPx + ZWorld.StrikePlacePitchPx) return false;
            if (side < ZWorld.StrikeLaneHalfPx || side > ZWorld.StrikeSideMaxPx + ZWorld.StrikePlacePitchPx) return false;
            return !W.InExitLane(x, y, 26) && !(W.Pathing != null && W.Pathing.StrikeKeepClear(x, y));
        }

        /// <summary>A spot in the gate's way (nobody of a forming group stays there): the corridor kept free along the gate line, an exit
        /// lane, a keep-clear area of a bridge or a narrow passage, or nearer the gate wall than the places (in front of the fort).</summary>
        bool StrikeInGateWay(ZObject o)
        {
            if (!ZWorld.StrikeGate(sgFort, out int ex, out int ey, out int gdx, out int gdy, out int wall)) return false;
            int x = o.CenterX, y = o.CenterY;
            int front = gdy != 0 ? (y - wall) * gdy : (x - wall) * gdx, side = gdy != 0 ? Math.Abs(x - ex) : Math.Abs(y - ey);
            if (front > 0 && side < ZWorld.StrikeLaneHalfPx) return true;
            if (front < ZWorld.StrikeFrontMinPx) return true;
            return W.InExitLane(x, y, 26) || (W.Pathing != null && W.Pathing.StrikeKeepClear(x, y));
        }

        void StrikeFormingStep()
        {
            if (sgFort == null || sgFort.Destroyed || sgFort.Owner != Team) { StrikeEnd(StrikeEventKind.GivenUp, "the fort is lost", true); return; }
            foreach (int id in sgFlags)
            {
                var f = W.Get(id);
                if (f == null || f.Owner != Team) { StrikeEnd(StrikeEventKind.GivenUp, "a territory of a feeding building was lost", true); return; }
            }
            bool allStand = true;
            for (int i = 0; i < sgMembers.Count;)
            {
                var m = sgMembers[i]; var o = m.o;
                // nobody stays in the gate's way: a member off its place that has not moved 8 px for StrikeGateStandSec on such a spot
                // (with an order or without: blocked on its way counts too) gets the next free place, once (with none left, or when it
                // already had one, it is released alone); still there StrikeGateStandSec + 2 s after it last moved: released alone
                long gx = o.CenterX - m.gx, gy = o.CenterY - m.gy, ox = o.CenterX - m.px, oy = o.CenterY - m.py;
                if (m.gateAt < 0 || gx * gx + gy * gy > 64) { m.gateAt = W.Time; m.gx = o.CenterX; m.gy = o.CenterY; }   // where and when it last moved 8 px
                else if (W.Time - m.gateAt >= ZWorld.StrikeGateStandSec - TIME_EPS && ox * ox + oy * oy > (long)ZWorld.StrikeSettlePx * ZWorld.StrikeSettlePx && StrikeInGateWay(o)
                         && (!m.moved || W.Time - m.gateAt >= ZWorld.StrikeGateStandSec + 2.0 - TIME_EPS))
                {
                    int p = m.moved ? -1 : StrikeFreePlace(o);
                    if (p < 0)
                    {
                        SgFree(o); sgMembers.RemoveAt(i);
                        SgLog(StrikeEventKind.MemberLeft, o.Id, 0, 0, 0, "stood in the gate's way");
                        StrikeSingles(new List<ZObject> { o }, true);
                        continue;
                    }
                    m.moved = true; m.place = p; m.px = sgPlaces[p].x; m.py = sgPlaces[p].y; m.off = -1; m.tries = 0; allStand = false;
                    StrikeMove(o, m.px, m.py); SgLog(StrikeEventKind.PlaceChanged, o.Id, m.px, m.py, 2, "stood in the gate's way");
                    i++; continue;
                }
                if (!SgIdle(o)) { allStand = false; i++; continue; }                        // walking to its place, or stepping aside for somebody
                long dx = o.CenterX - m.px, dy = o.CenterY - m.py, d2 = dx * dx + dy * dy;
                if (d2 > (long)ZWorld.StrikeSettlePx * ZWorld.StrikeSettlePx)
                {   // it stands, but not on its place (it stopped short, or a passing unit made it step aside): back to its place
                    double off = Math.Sqrt(d2);
                    if (m.off < 0 || off < m.off - ZWorld.StrikeSettlePx) { m.off = off; m.tries = 0; }   // the first time, or nearer than it ever stood: counted afresh
                    if (m.tries < ZWorld.StrikeResendTries)
                    {
                        m.tries++; m.reissues++; allStand = false;
                        StrikeMove(o, m.px, m.py); SgLog(StrikeEventKind.Reissued, o.Id, m.px, m.py);
                        i++; continue;
                    }
                    // sent back and no nearer (the game's own spacing rule ends a walk beside a team-mate that already stands):
                    // nobody is sent again and again. The spot it stands on is its place when it is a spot of the forming ground;
                    // else it gets the next free place, once; with none left it is released alone (nobody stands in the gate's way).
                    if (!StrikeSpotOk(o))
                    {
                        int p = m.moved ? -1 : StrikeFreePlace(o);
                        if (p < 0)
                        {
                            SgFree(o); sgMembers.RemoveAt(i);
                            SgLog(StrikeEventKind.MemberLeft, o.Id, 0, 0, 0, "no place to stand on");
                            StrikeSingles(new List<ZObject> { o }, true);
                            continue;
                        }
                        m.moved = true; m.place = p; m.px = sgPlaces[p].x; m.py = sgPlaces[p].y; m.off = -1; m.tries = 0; allStand = false;
                        StrikeMove(o, m.px, m.py); SgLog(StrikeEventKind.PlaceChanged, o.Id, m.px, m.py, 2, "the next free place");
                        i++; continue;
                    }
                    m.px = o.CenterX; m.py = o.CenterY;
                    SgLog(StrikeEventKind.PlaceChanged, o.Id, m.px, m.py, 1, "stands " + off.ToString("0") + " px beside its place");
                }
                m.off = -1; m.tries = 0;
                if (!o.HoldPosition && W.SendHold(Team, o)) { StrikeOrders++; if (ClientView) Relay(o); }
                if (!m.settled) { m.settled = true; SgLog(StrikeEventKind.Settled, o.Id, o.CenterX, o.CenterY); }
                i++;
            }
            bool delivered = true;
            foreach (int id in sgFeeders)
            {
                var b = W.Get(id);
                if (b == null || b.Destroyed || b.Owner != Team) continue;                    // a ruin delivers nothing: nobody waits for it
                if (sgDelivered[id] == 0) { delivered = false; break; }
            }
            bool enough = sgMembers.Count >= ZWorld.StrikeMinMembers;
            if (SgEasyHolding(enough, delivered, allStand)) return;                         // K434: an Easy group stands 30 % longer (Sim/ZBot.EasySlow.cs)
            if (enough && delivered && allStand) { StrikeGoEasy("every building delivered, all standing"); return; }
            if (W.Time + TIME_EPS < sgDeadline) return;
            if (enough || EasySlowActive) StrikeGoEasy("the cycle is over");                // K434: the Easy computer's cycle ends on the slower clock (too few members: given up then)
            else StrikeEnd(StrikeEventKind.GivenUp, "the cycle is over with fewer than " + ZWorld.StrikeMinMembers + " members", true);
        }

        // ------------------------------------------------------------------ the target
        /// <summary>
        /// The place where the most of the other side's army stands together: every squad (its leader) and every vehicle of a
        /// side that is neither ours nor neutral counts one; the unit with the most of them within StrikeGatherPx of it wins (itself
        /// counted; on a tie the one nearest (fromX, fromY), then the lower id). Returns the middle of those units and their ids.
        /// </summary>
        public bool StrikeGathering(int fromX, int fromY, out int tx, out int ty, List<int> ids)
        {
            tx = ty = 0; ids?.Clear();
            var army = new List<ZObject>();
            foreach (var o in W.Objects)
                if (o.IsMobile && !o.IsMinion && o.Owner != Team && o.Owner != Team.Neutral && SgAlive(o) && !o.IsFortDefender) army.Add(o);
            if (army.Count == 0) return false;
            long g2 = (long)ZWorld.StrikeGatherPx * ZWorld.StrikeGatherPx;
            ZObject best = null; int bestN = 0; long bestD = 0;
            foreach (var u in army)
            {
                int n = 0;
                foreach (var v in army) { if (v.Owner != u.Owner) continue; long dx = v.CenterX - u.CenterX, dy = v.CenterY - u.CenterY; if (dx * dx + dy * dy <= g2) n++; }
                long fx = u.CenterX - fromX, fy = u.CenterY - fromY, d = fx * fx + fy * fy;
                if (best == null || n > bestN || (n == bestN && (d < bestD || (d == bestD && u.Id < best.Id)))) { best = u; bestN = n; bestD = d; }
            }
            long sx = 0, sy = 0; int k = 0;
            foreach (var v in army)
            {
                if (v.Owner != best.Owner) continue;
                long dx = v.CenterX - best.CenterX, dy = v.CenterY - best.CenterY;
                if (dx * dx + dy * dy > g2) continue;
                sx += v.CenterX; sy += v.CenterY; k++; ids?.Add(v.Id);
            }
            tx = (int)(sx / k); ty = (int)(sy / k);
            return true;
        }

        void SgCentre(out int cx, out int cy)
        {
            long sx = 0, sy = 0; int k = 0;
            foreach (var m in sgMembers) { sx += m.o.CenterX; sy += m.o.CenterY; k++; }
            cx = k > 0 ? (int)(sx / k) : sgFort != null ? sgFort.CenterX : 0; cy = k > 0 ? (int)(sy / k) : sgFort != null ? sgFort.CenterY : 0;
        }

        // ------------------------------------------------------------------ the march
        double[] sgRobotField, sgVehicleField;                                                  // the ways to the target point (ZWorld.StrikeWayField), made when first asked, dropped when the target moves
        double[] sgRouteField;                                                                  // the field the common route was laid on (one of the two); null = no route
        readonly List<(int x, int y, double way)> sgRoute = new List<(int x, int y, double way)>();
        int sgRouteFromX, sgRouteFromY;

        /// <summary>The route as a line: -1 = its start, 0 .. Count - 1 its points, Count = the target point. Segment j runs from
        /// point j - 1 to point j; a member on segment j heads for point j.</summary>
        (int x, int y) SgRoutePt(int i) => i < 0 ? (sgRouteFromX, sgRouteFromY) : i < sgRoute.Count ? (sgRoute[i].x, sgRoute[i].y) : (sgTx, sgTy);
        /// <summary>The px of way left along the route from point i to the target point (0 at the target point): the length of the
        /// route's line from there (its points lie 8 tiles apart on the shortest way, so the line is that way), the same measure for
        /// every member and every stretch, also the last one to the target point (SgRouteGeoLay).</summary>
        double SgRoutePtWay(int i) => i >= sgRoute.Count ? 0 : i >= 0 && i < sgRouteGeo.Count ? sgRouteGeo[i] : SgDist(sgRouteFromX, sgRouteFromY, SgRoutePt(0).x, SgRoutePt(0).y) + SgRoutePtWay(0);
        readonly List<double> sgRouteGeo = new List<double>();
        /// <summary>The line's length from every point to the target point (after every lay of the route).</summary>
        void SgRouteGeoLay()
        {
            sgRouteGeo.Clear();
            for (int i = 0; i < sgRoute.Count; i++) sgRouteGeo.Add(0);
            double run = 0;
            for (int i = sgRoute.Count - 1; i >= 0; i--) { var a = SgRoutePt(i); var b = SgRoutePt(i + 1); run += SgDist(a.x, a.y, b.x, b.y); sgRouteGeo[i] = run; }
        }
        static double SgDist(int ax, int ay, int bx, int by) => Math.Sqrt((double)(ax - bx) * (ax - bx) + (double)(ay - by) * (ay - by));
        static double SgSegDist(int px, int py, (int x, int y) a, (int x, int y) b)
        {
            double vx = b.x - a.x, vy = b.y - a.y, l2 = vx * vx + vy * vy;
            double u = l2 <= 0 ? 0 : Math.Max(0, Math.Min(1, ((px - a.x) * vx + (py - a.y) * vy) / l2));
            double qx = a.x + u * vx - px, qy = a.y + u * vy - py;
            return Math.Sqrt(qx * qx + qy * qy);
        }
        /// <summary>The segment of the route nearest (px, py), from segment <paramref name="from"/> on; on equal distances the later one.</summary>
        int SgNearestSeg(int px, int py, int from, out double d)
        {
            int best = from; d = double.MaxValue;
            for (int j = from; j <= sgRoute.Count; j++)
            {
                double dj = SgSegDist(px, py, SgRoutePt(j - 1), SgRoutePt(j));
                if (dj <= d) { d = dj; best = j; }
            }
            return best;
        }

        /// <summary>
        /// Where a member is on the common route: the point it heads for now (sgRoute.Count = its own spot at the target) and the px of
        /// way it has left along the route. ON the route (within StrikeRouteJoinPx of it): the end of the segment it is nearest to (the
        /// next point once it is within StrikeRouteAheadPx of that one); its way = straight to that point, then along the route. OFF the
        /// route (still on its way from its building when the group left, or its place lay to the side of the route): it joins the
        /// route AT OR BEHIND the group's front (the farthest point a member on the route heads for): where its own shortest way to the
        /// target point meets the route (SgJoin) when that is not ahead of the front; else the route point nearest to it, at or behind
        /// the front (nobody takes a road of its own, over another bridge, to a point ahead of the group). Its place on the line
        /// decides, not the way field of another kind of unit. Never a point before the one it already has. Without a route (and in
        /// the first round's before row): its own spot, its own way.
        /// </summary>
        int SgPlace(SgMember m, out double way)
        {
            if (sgRouteField == null || ZWorld.StrikeRoundOneForCheck) { way = SgWay(m.o); m.onRoute = false; return sgRoute.Count; }
            int px = m.o.CenterX, py = m.o.CenterY, cur = Math.Max(0, m.leg);
            int leg = SgNearestSeg(px, py, cur, out double d);
            m.onRoute = d <= ZWorld.StrikeRouteJoinPx;
            if (!m.onRoute)
            {
                int front = -1;
                foreach (var o in sgMembers) if (o != m && o.onRoute && o.arrivedAt < 0 && o.leg > front) front = o.leg;
                int cap = Math.Min(sgRoute.Count, Math.Max(cur, front));
                if (SgJoin(px, py, cur, out int jl, out double jw) && jl <= cap) { way = jw; return jl; }
                int best = cur; double bd = double.MaxValue;
                for (int i = cur; i <= cap; i++) { var q = SgRoutePt(i); double dq = SgDist(px, py, q.x, q.y); if (dq < bd) { bd = dq; best = i; } }
                way = bd + SgRoutePtWay(best);
                return best;
            }
            if (leg < sgRoute.Count && SgDist(px, py, sgRoute[leg].x, sgRoute[leg].y) <= ZWorld.StrikeRouteAheadPx) leg++;
            var p = SgRoutePt(leg);
            way = SgDist(px, py, p.x, p.y) + SgRoutePtWay(leg);
            return leg;
        }

        /// <summary>A member off the route: walks down the route's way field from (px, py) (its own shortest way to the target point on
        /// the graph the route was laid on) to the first footprint within StrikeRouteJoinPx of the route (segment <paramref name="from"/>
        /// on). The point it heads for = the end of that segment; its way = its way to there plus the way along the route from there.
        /// false = no footprint of that graph near it, or its way meets the route nowhere before the target area.</summary>
        bool SgJoin(int px, int py, int from, out int leg, out double way)
        {
            leg = sgRoute.Count; way = -1;
            var f = sgRouteField; int w = W.Path.W, h = W.Path.H; bool robot = f == sgRobotField;
            int half = robot ? 8 : 16;
            var (tx0, ty0) = ZReach.FootTileAt(px, py, robot);
            int cur = -1; double cd = double.MaxValue;
            for (int r = 0; r <= 2 && cur < 0; r++)
                for (int ty = ty0 - r; ty <= ty0 + r; ty++)
                    for (int tx = tx0 - r; tx <= tx0 + r; tx++)
                    {
                        if (Math.Max(Math.Abs(tx - tx0), Math.Abs(ty - ty0)) != r || tx < 0 || ty < 0 || tx >= w || ty >= h) continue;
                        if (f[ty * w + tx] < cd) { cd = f[ty * w + tx]; cur = ty * w + tx; }
                    }
            if (cur < 0 || cd == double.MaxValue) return false;
            double start = f[cur];
            for (int guard = 0; guard < 600 && f[cur] > ZWorld.StrikeArrivePx; guard++)
            {
                int cx = cur % w, cy = cur / w, x = cx * 16 + half, y = cy * 16 + half;
                int j = SgNearestSeg(x, y, from, out double d);
                if (d <= ZWorld.StrikeRouteJoinPx)
                {
                    if (j < sgRoute.Count && SgDist(x, y, sgRoute[j].x, sgRoute[j].y) <= ZWorld.StrikeRouteAheadPx) j++;
                    var p = SgRoutePt(j);
                    leg = j; way = (start - f[cur]) + SgDist(x, y, p.x, p.y) + SgRoutePtWay(j);
                    return true;
                }
                int next = -1; double nd = f[cur];                                          // downhill, as ZWorld.StrikeRouteTiles
                for (int oy = -1; oy <= 1; oy++)
                    for (int ox = -1; ox <= 1; ox++)
                    {
                        if (ox == 0 && oy == 0) continue;
                        int nx = cx + ox, ny = cy + oy;
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                        int ni = ny * w + nx;
                        if (f[ni] < nd && W.Path.StepOk(cx, cy, nx, ny, robot, false)) { nd = f[ni]; next = ni; }
                    }
                if (next < 0) return false;
                cur = next;
            }
            return false;
        }

        /// <summary>Lays the common route from (fx, fy) to the target point: on the vehicles' graph when a member is a vehicle and that
        /// way exists, else on the robots' graph. In the first round's before row (checks only) it is laid for the check's measure
        /// but nobody follows it.</summary>
        void StrikeRouteLay(int fx, int fy, bool log = true)
        {
            sgRoute.Clear(); sgRouteField = null; sgRouteFromX = fx; sgRouteFromY = fy;
            bool vehicle = false;
            foreach (var m in sgMembers) if (!m.o.IsRobot) { vehicle = true; break; }
            for (int pass = vehicle ? 0 : 1; pass < 2 && sgRouteField == null; pass++)
            {
                bool robot = pass == 1;
                var f = robot ? (sgRobotField ??= W.StrikeWayField(sgTx, sgTy, true)) : (sgVehicleField ??= W.StrikeWayField(sgTx, sgTy, false));
                if (W.StrikeRouteTiles(f, fx, fy, robot, sgRoute)) sgRouteField = f;
            }
            foreach (var m in sgMembers) { m.leg = -1; m.onRoute = false; }
            SgRouteGeoLay();
            if (log) SgLogRoute();
        }

        void SgLogRoute() => SgLog(StrikeEventKind.Route, -1, sgRoute.Count > 0 ? sgRoute[0].x : sgTx, sgRoute.Count > 0 ? sgRoute[0].y : sgTy, sgRoute.Count,
                                   sgRouteField == null ? "no common route" : sgRouteField == sgVehicleField ? "the vehicles' way" : "the robots' way");

        /// <summary>
        /// The target moved to (nx, ny): the route again, from the LEADING member on its way (the least way left), with the points of the
        /// old route between the rear and the leader kept in front of it (the others are on that stretch: they go on along it to the
        /// leader's spot, then along the new route). Nobody is sent back or across. Without a member on its way (or without a route):
        /// from the middle of the group, as at the departure.
        /// </summary>
        void StrikeRouteRelay(int nx, int ny)
        {
            SgMember lead = null; int rear = int.MaxValue;
            if (sgRouteField != null && !ZWorld.StrikeRoundOneForCheck)
                foreach (var m in sgMembers)
                {
                    if (m.arrivedAt >= 0 || SgFighting(m.o) || m.way < 0) continue;
                    if (lead == null || m.way < lead.way) lead = m;
                    rear = Math.Min(rear, Math.Max(0, m.leg));
                }
            var kept = new List<(int x, int y)>(); var from = SgRoutePt(-1);
            if (lead != null)
            {
                int ll = Math.Min(Math.Max(0, lead.leg), sgRoute.Count);
                rear = Math.Min(rear, ll);
                from = SgRoutePt(rear - 1);
                for (int i = rear; i < ll; i++) kept.Add((sgRoute[i].x, sgRoute[i].y));
            }
            sgTx = nx; sgTy = ny; sgRobotField = sgVehicleField = null;
            if (lead == null) { SgCentre(out int cx, out int cy); StrikeRouteLay(cx, cy); return; }
            int lx = lead.o.CenterX, ly = lead.o.CenterY;
            StrikeRouteLay(lx, ly, false);
            if (sgRouteField == null) { SgCentre(out int cx, out int cy); StrikeRouteLay(cx, cy); return; }
            // in front of the new points: the old points the others still pass, then the leader's spot (their ways counted back from it)
            var pre = new List<(int x, int y, double way)>();
            double way = SgRoutePtWay(-1);                                                      // from the leader's spot along the new route
            pre.Add((lx, ly, way));
            int px = lx, py = ly;
            for (int i = kept.Count - 1; i >= 0; i--) { way += SgDist(kept[i].x, kept[i].y, px, py); pre.Insert(0, (kept[i].x, kept[i].y, way)); px = kept[i].x; py = kept[i].y; }
            sgRoute.InsertRange(0, pre);
            sgRouteFromX = from.x; sgRouteFromY = from.y;
            SgRouteGeoLay();
            SgLogRoute();
        }

        /// <summary>The px of way a member has to the target point from where it is (the path search's own graph for its kind,
        /// shortest way), -1 = no way.</summary>
        double SgWay(ZObject o)
        {
            var f = o.IsRobot ? (sgRobotField ??= W.StrikeWayField(sgTx, sgTy, true)) : (sgVehicleField ??= W.StrikeWayField(sgTx, sgTy, false));
            double px = W.StrikeWayOf(f, o);
            return px == double.MaxValue ? -1 : px;
        }

        void StrikeGo(string how)
        {
            SgCentre(out int cx, out int cy);
            if (!StrikeGathering(cx, cy, out sgTx, out sgTy, sgTargetIds)) { StrikeEnd(StrikeEventKind.GivenUp, "nobody of the other side is left to march at", true); return; }
            sgRobotField = sgVehicleField = null;
            double longest = 0; int can = 0;
            foreach (var m in sgMembers) { m.way = SgWay(m.o); if (m.way >= 0) can++; if (m.way > longest) longest = m.way; }
            if (can < 2) { StrikeEnd(StrikeEventKind.GivenUp, "no way to the target for the group", true); return; }
            var cannot = new List<ZObject>();
            for (int i = sgMembers.Count - 1; i >= 0; i--)
                if (sgMembers[i].way < 0)
                {   // no way for this kind of unit: it does not march, it is released alone
                    var o = sgMembers[i].o; SgFree(o); sgMembers.RemoveAt(i); cannot.Add(o);
                    SgLog(StrikeEventKind.MemberLeft, o.Id, 0, 0, 0, "no way to the target");
                }
            int fcx = 0, fcy = 0;
            foreach (var m in sgMembers) { fcx += m.px; fcy += m.py; }
            fcx /= sgMembers.Count; fcy /= sgMembers.Count;
            StrikeRouteLay(fcx, fcy);                                                         // one common route for everybody, from the middle of the formation (a member still on its way from its building joins it)
            sgPhase = StrikePhase.Marching; sgGo = W.Time; sgStarted = sgMembers.Count; sgHalfAt = -1;
            sgAllowedAt = W.Time + ZWorld.StrikeGapSec * EasySlowScale; sgNextRetarget = W.Time + ZWorld.StrikeRetargetSec * EasySlowScale;   // K434: x 1.3 on Easy
            StrikeGroupsFormed++;
            SgLog(StrikeEventKind.Formed, -1, sgTx, sgTy, sgMembers.Count, $"{how}; window {W.Time - sgOpened:F0} s; {sgTargetIds.Count} units at the target; the longest way {longest:F0} px");
            foreach (var m in sgMembers)
            {   // everybody leaves in this tick, each for its own spot at the target (its place in the formation)
                m.sx = m.px - fcx; m.sy = m.py - fcy; m.reissues = 0; m.best = -1; m.o.strikeHold = 2;
                StrikeDepart(m, true);
            }
            StrikeSingles(cannot, true);
            StrikePaces();
        }

        /// <summary>Sends a member on: to the point of the common route it heads for now, or to its own spot at the target. With
        /// <paramref name="force"/> always (departure, its order again, a new target); else only when it is due for the next point.</summary>
        void StrikeDepart(SgMember m, bool force)
        {
            int leg = SgPlace(m, out _);
            if (!force && leg == m.leg) return;
            m.leg = leg;
            int x, y;
            if (leg < sgRoute.Count) { x = sgRoute[leg].x; y = sgRoute[leg].y; }
            else { x = Math.Max(16, Math.Min(W.Map.WidthPix - 16, sgTx + m.sx)); y = Math.Max(16, Math.Min(W.Map.HeightPix - 16, sgTy + m.sy)); }
            StrikeMove(m.o, x, y);
            if (!m.departed) { m.departed = true; SgLog(StrikeEventKind.Departed, m.o.Id, x, y); }
        }

        static bool SgFighting(ZObject o) => o.AttackObject != null || (o.Waypoints.Count > 0 && (o.Waypoints[0].mode == WpMode.Attack || o.Waypoints[0].mode == WpMode.Agro));

        /// <summary>
        /// One body. Of the members that are on their way (not in a fight, not yet in the target area) the one with the most way
        /// left to the target point, the rear, moves at its own speed. A member that has more than StrikeAheadFreePx less way left
        /// than the rear slows down, the more the farther ahead it is, to StrikeMinPace of its own speed at StrikeAheadMaxPx; nobody
        /// stands waiting. So the group closes up and then moves at the pace of its slowest member. A member in a fight or in the
        /// target area moves freely; one that comes out of a fight behind the others is the rear, and they slow down for it.
        /// </summary>
        void StrikePaces()
        {
            double rear = 0;
            for (int i = 0; i < sgMembers.Count;)
            {
                var m = sgMembers[i];
                bool onWay = m.arrivedAt < 0 && !SgFighting(m.o);
                double rw = -1;
                if (onWay) SgPlace(m, out rw);
                m.way = onWay ? rw : -1;
                // on its way (not in a fight, not in the target area), not stunned: does it move at all? Its place on the map is
                // looked at, not the way it has left (a unit that drives round something gets farther from the target for a while
                // and is no stuck unit). A member the rule slows down is timed too, over a distance shortened by its pace.
                bool judged = onWay && !m.o.Stunned;
                double stall = ZWorld.StrikeStallPx * Math.Max(m.o.strikePace, ZWorld.StrikeMinPace);
                long mvx = m.o.CenterX - m.bx, mvy = m.o.CenterY - m.by;
                if (!judged || m.best < 0 || mvx * mvx + mvy * mvy >= stall * stall) { m.best = judged ? 1 : -1; m.bx = m.o.CenterX; m.by = m.o.CenterY; m.bestAt = W.Time; }
                else if (W.Time - m.bestAt >= ZWorld.StrikeStallSec - TIME_EPS)
                {   // it keeps its order but does not get on (it cannot move): released alone, so the others do not creep along for it
                    var o = m.o; SgFree(o); sgMembers.RemoveAt(i);
                    SgLog(StrikeEventKind.MemberLeft, o.Id, 0, 0, 0, "did not move in " + ZWorld.StrikeStallSec.ToString("0") + " s");
                    StrikeSingles(new List<ZObject> { o }, false);
                    continue;
                }
                if (m.way > rear) rear = m.way;
                i++;
            }
            foreach (var m in sgMembers)
            {
                double pace = 1.0, ahead = rear - m.way;
                if (m.way >= 0 && ahead > ZWorld.StrikeAheadFreePx)
                    pace = Math.Max(ZWorld.StrikeMinPace, 1.0 - (1.0 - ZWorld.StrikeMinPace) * (ahead - ZWorld.StrikeAheadFreePx) / (ZWorld.StrikeAheadMaxPx - ZWorld.StrikeAheadFreePx));
                SgPace(m.o, pace);
                if (m.way >= 0 && m.arrivedAt < 0) StrikeDepart(m, false);                                // on to the next point of the route when it is due
            }
        }

        void StrikeMarchStep()
        {
            if (W.Time - sgGo >= ZWorld.StrikeMarchLimitSec - TIME_EPS) { StrikeEnd(StrikeEventKind.Released, "the march limit", true); return; }
            if (W.Time + TIME_EPS >= sgNextRetarget)
            {
                sgNextRetarget = W.Time + ZWorld.StrikeRetargetSec * EasySlowScale;                // K434: x 1.3 on Easy
                long sx = 0, sy = 0; int k = 0;
                foreach (int id in sgTargetIds)
                {
                    var t = W.Get(id);
                    if (t == null || !SgAlive(t) || t.Owner == Team || t.Owner == Team.Neutral) continue;
                    sx += t.CenterX; sy += t.CenterY; k++;
                }
                int nx, ny;
                if (k > 0) { nx = (int)(sx / k); ny = (int)(sy / k); }
                else
                {   // the gathering is gone: the biggest one now, seen from where the group is
                    SgCentre(out int cx, out int cy);
                    if (!StrikeGathering(cx, cy, out nx, out ny, sgTargetIds)) { StrikeEnd(StrikeEventKind.Released, "nobody of the other side is left", true); return; }
                    k = sgTargetIds.Count;
                }
                long mx = nx - sgTx, my = ny - sgTy;
                if (mx * mx + my * my > (long)ZWorld.StrikeRetargetPx * ZWorld.StrikeRetargetPx)
                {
                    foreach (var m in sgMembers) m.best = -1;                                          // a new order: who moves is looked at afresh
                    SgLog(StrikeEventKind.Retargeted, -1, nx, ny, k);
                    StrikeRouteRelay(nx, ny);                                                          // the route again, from the leader on its way; the stretch behind it kept
                    foreach (var m in sgMembers) if (m.arrivedAt < 0 && !SgFighting(m.o)) StrikeDepart(m, true);
                }
            }
            long a2 = (long)ZWorld.StrikeArrivePx * ZWorld.StrikeArrivePx; int arrived = 0;
            for (int i = 0; i < sgMembers.Count;)
            {
                var m = sgMembers[i]; var o = m.o;
                long dx = o.CenterX - sgTx, dy = o.CenterY - sgTy; bool inArea = dx * dx + dy * dy <= a2;
                if (!inArea && o.AttackObject != null)
                {   // it has arrived too when it fights somebody who stands in the target area
                    long ex = o.AttackObject.CenterX - sgTx, ey = o.AttackObject.CenterY - sgTy;
                    inArea = o.AttackObject.IsUnit && ex * ex + ey * ey <= a2;
                }
                if (!inArea && SgFighting(o))
                {   // ... or when it fights just outside the target area, or beside a member that has arrived (the group is together at the fight)
                    long r2 = (long)(ZWorld.StrikeArrivePx + ZWorld.StrikeBesidePx) * (ZWorld.StrikeArrivePx + ZWorld.StrikeBesidePx);
                    inArea = dx * dx + dy * dy <= r2;
                    for (int j = 0; j < sgMembers.Count && !inArea; j++)
                    {
                        var a = sgMembers[j]; if (a == m || a.arrivedAt < 0) continue;
                        long bx = a.o.CenterX - o.CenterX, by = a.o.CenterY - o.CenterY;
                        inArea = bx * bx + by * by <= (long)ZWorld.StrikeBesidePx * ZWorld.StrikeBesidePx;
                    }
                }
                if (inArea && m.arrivedAt < 0) { m.arrivedAt = W.Time; SgLog(StrikeEventKind.Arrived, o.Id, o.CenterX, o.CenterY); }
                if (m.arrivedAt >= 0) { arrived++; i++; continue; }
                if (SgIdle(o) && o.AttackObject == null && !o.Stunned)
                {   // stands without an order outside the area: its order again, twice; then it is released alone
                    if (m.reissues < 2) { m.reissues++; StrikeDepart(m, true); SgLog(StrikeEventKind.Reissued, o.Id, sgTx, sgTy); }
                    else
                    {
                        SgFree(o); sgMembers.RemoveAt(i);
                        SgLog(StrikeEventKind.MemberLeft, o.Id, 0, 0, 0, "stood without an order three times");
                        StrikeSingles(new List<ZObject> { o }, false);
                        continue;
                    }
                }
                i++;
            }
            if (sgMembers.Count < 2) { StrikeEnd(StrikeEventKind.Released, "fewer than two members left", true); return; }
            if (arrived >= sgMembers.Count) { StrikeEnd(StrikeEventKind.Released, "arrived", true); return; }
            if (arrived * 2 >= sgMembers.Count)
            {
                if (sgHalfAt < 0) sgHalfAt = W.Time;
                else if (W.Time - sgHalfAt >= ZWorld.StrikeArriveGraceSec - TIME_EPS) { StrikeEnd(StrikeEventKind.Released, "arrived (the rest got " + ZWorld.StrikeArriveGraceSec.ToString("0") + " s)", true); return; }
            }
            StrikePaces();
        }

        // ------------------------------------------------------------------ the end of a group: everybody is free at once
        /// <summary>Ends the group: every member is released in this tick. Running orders are kept. With <paramref name="orders"/>,
        /// whoever has nothing to do gets an order of the computer's own kind at once (never stands waiting at the fort).</summary>
        void StrikeEnd(StrikeEventKind kind, string why, bool orders)
        {
            bool forming = sgPhase == StrikePhase.Forming;
            var free = new List<ZObject>();
            foreach (var m in sgMembers)
            {
                SgFree(m.o); if (SgAlive(m.o) && m.o.Owner == Team) free.Add(m.o);
                if (!orders) { m.o.HoldPosition = false; foreach (var mn in m.o.Minions) if (mn != null) mn.HoldPosition = false; }   // (also the robots of a squad whose leader fell in this tick)
            }
            sgMembers.Clear(); sgTargetIds.Clear(); sgRoute.Clear(); sgRouteField = null;
            sgPhase = StrikePhase.Waiting;
            if (kind == StrikeEventKind.GivenUp) { StrikeGivenUp++; if (forming) sgAllowedAt = Math.Max(sgAllowedAt, W.Time + ZWorld.StrikeRetrySec * EasySlowScale); }   // K434: x 1.3 on Easy
            SgLog(kind, -1, 0, 0, free.Count, why);
            if (orders) StrikeSingles(free, forming);
        }

        /// <summary>
        /// Released members act alone again. Whoever is walking or fighting keeps at it. Whoever stands with nothing to do gets
        /// the computer's own order now: the engine's own matching for these units (the same targets, the same regions, each unit
        /// its nearest target that no other of them has taken; when every target is taken, its nearest), through the same Command
        /// as every order of the planner. The Easy limit on attackers does not hold these orders back (they still count toward it).
        /// <paramref name="walking"/>: also the ones still walking to their place at the fort (a window given up: nobody goes on
        /// to a place to stand there).
        /// </summary>
        void StrikeSingles(List<ZObject> free, bool walking)
        {
            var idle = new List<ZObject>();
            foreach (var o in free) if (SgAlive(o) && o.Owner == Team && !o.IsMinion && o.AttackObject == null && (walking || SgIdle(o))) idle.Add(o);
            if (idle.Count == 0) return;
            bool allOut = GoAllOut_3(out _, out _);
            var targets = new List<ZObject>();
            CollectOurTargets_3(targets, allOut);
            MatchTargets_3(new List<ZObject>(idle), targets);
            var taken = new HashSet<int>();
            foreach (var u in idle)
            {
                var cands = AI(u);
                ZObject best = null; double bd = double.MaxValue;
                for (int pass = 0; pass < 2 && best == null; pass++)
                    foreach (var t in cands)
                    {
                        if (pass == 0 && taken.Contains(t.Id)) continue;
                        double d = Dist(u, t);
                        if (d < bd) { bd = d; best = t; }
                    }
                int before = Orders;
                if (best != null) Command(u, best, true);
                if (Orders > before) { taken.Add(best.Id); SgLog(StrikeEventKind.SingleOrder, u.Id, 0, 0, best.Id); continue; }
                // nothing the computer could send it to: at least it does not keep holding position (the planner finds it idle as today)
                if (u.HoldPosition) { u.HoldPosition = false; foreach (var mn in u.Minions) if (mn != null) mn.HoldPosition = false; }
                SgLog(StrikeEventKind.NoOrder, u.Id);
            }
        }
    }
}
