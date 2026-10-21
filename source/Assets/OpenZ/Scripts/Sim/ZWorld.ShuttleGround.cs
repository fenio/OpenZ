using System;
using System.Collections.Generic;

namespace OpenZ.Sim
{
    /// <summary>
    /// DV118, the playable shuttle on the ground (the third polish round): where it may land or park, what must stay clear round it, and the
    /// way out of the way at a take-off. Deterministic: positions, sim time and fixed lists only, no random draw, no frame time.
    ///  * The ship's plan outline (<see cref="ShuttlePlan"/>: the hull, both engine pods and the four legs seen from above, root metres, x
    ///    right, z nose; measured from the model) is the footprint. <see cref="ZObject.ShuttlePlanGap"/> = metres from a point to it.
    ///  * The landing / parking spot (<see cref="ShuttleLandSpot"/>, used for every landing, the testing panel's drop and "can I land
    ///    here"): the footprint grown by <see cref="ShuttleSpotClearM"/> and the ramp lane behind it (<see cref="ShuttleLaneHalfW"/> x 2 wide,
    ///    from the hull's back to <see cref="ShuttleLaneBehindM"/> behind the ramp foot) are free: no closed or water tile (the lane: no
    ///    tile a robot cannot stand on), no building, hut, crate, flag, bridge or prop, no tree or plant crown at least
    ///    <see cref="ShuttlePlantMinM"/> tall (the level's drawn plants, handed in once at the level start: <see cref="ShuttleSetKeepOut"/>),
    ///    and no unit on the footprint. If its own heading does not fit, the ship turns up to 90 degrees either way (15 degree steps, the
    ///    smaller turn first, right before left) so that the ramp faces a free side; it turns in a slow hover before it comes down. No
    ///    heading fits = it cannot land there (it hovers, as before). On a repair station's deck the station's own rule holds (unchanged).
    ///  * The landed ship is a hard obstacle for every ground unit: its pathing ring (<see cref="ShuttleRadiusPx"/>) holds the footprint
    ///    + 0.5 m and no pair rule lets a unit through it (ZPathing.PairExempt: only a unit in a building's own door stage).
    ///  * A take-off waits (at most <see cref="ShuttleLiftWaitSec"/>) while a ground unit stands within <see cref="ShuttleLiftClearM"/> of the
    ///    footprint; robots standing there are blown out of the way at <see cref="ShuttleLiftNudgeMps"/>.
    ///  * Shot down on a repair station's deck (or over it, coming down onto it): the station takes nobody else for
    ///    <see cref="ShuttleDeckWreckSec"/> (its bay stays closed, its crane stays away) while the wreck clears off the deck.
    /// </summary>
    public partial class ZObject
    {
        /// <summary>The ship's plan outline: x right, z nose, metres from its middle (convex, counter-clockwise).</summary>
        internal static readonly double[] ShuttlePlan = { -3.194, -0.057, -3.182, -0.499, -3.156, -0.724, -3.138, -0.873, -3.000, -1.228, -2.332, -2.822, -2.328, -2.830, -2.237, -2.999, -2.119, -3.169, -1.998, -3.329, 2.003, -3.333, 2.190, -3.073, 2.267, -2.955, 2.353, -2.817, 3.003, -1.225, 3.143, -0.842, 3.151, -0.822, 3.174, -0.620, 3.194, -0.442, 3.194, -0.041, 3.188, 0.182, 3.066, 0.902, 3.050, 0.930, 2.446, 1.587, 1.024, 2.892, 0.945, 2.963, 0.763, 3.114, 0.517, 3.243, 0.326, 3.305, 0.133, 3.333, -0.190, 3.323, -0.358, 3.294, -0.531, 3.237, -0.771, 3.106, -1.057, 2.856, -2.441, 1.584, -3.054, 0.911, -3.073, 0.839, -3.153, 0.351, -3.184, 0.162, -3.191, 0.078 };
        /// <summary>The farthest point of the outline from the middle (m) and the hull's back edge behind the middle (m).</summary>
        public const double ShuttlePlanRadiusM = 3.889, ShuttleHullBackM = 3.333;

        /// <summary>Metres from the sim point (x, y) px to the outline of a ship standing with its middle at (mx, my) px, facing hdDeg
        /// (negative = inside the outline).</summary>
        public static double ShuttlePlanGap(double mx, double my, double hdDeg, double x, double y)
        {
            double hd = hdDeg * Math.PI / 180.0, nx = Math.Cos(hd), ny = Math.Sin(hd);
            double dx = (x - mx) / 8.0, dy = (y - my) / 8.0;
            double rz = dx * nx + dy * ny, rx = -dx * ny + dy * nx;
            return ShuttlePlanGapRoot(rx, rz);
        }

        /// <summary>The same in the ship's root space (x right, z nose, m).</summary>
        internal static double ShuttlePlanGapRoot(double rx, double rz)
        {
            var p = ShuttlePlan; int n = p.Length / 2; bool inside = true; double best = double.MaxValue;
            for (int k = 0; k < n; k++)
            {
                double ax = p[2 * k], az = p[2 * k + 1], bx = p[(2 * k + 2) % p.Length], bz = p[(2 * k + 3) % p.Length];
                double ex = bx - ax, ez = bz - az, qx = rx - ax, qz = rz - az;
                if (ex * qz - ez * qx < 0) inside = false;
                double u = Math.Max(0.0, Math.Min(1.0, (qx * ex + qz * ez) / Math.Max(1e-12, ex * ex + ez * ez)));
                double cx = ax + ex * u - rx, cz = az + ez * u - rz;
                best = Math.Min(best, Math.Sqrt(cx * cx + cz * cz));
            }
            return inside ? -best : best;
        }

        /// <summary>The ship's exact middle (px, sub-pixel).</summary>
        double ShuttleMidX => LocX + XOver + WidthPix * 0.5;
        double ShuttleMidY => LocY + YOver + HeightPix * 0.5;

        /// <summary>Metres from the sim point (x, y) px to this ship's outline as it stands now (negative = inside).</summary>
        public double ShuttleOutlineGapM(double x, double y) => ShuttlePlanGap(ShuttleMidX, ShuttleMidY, ShuttleHeadingDeg, x, y);

        // ------------------------------------------------------------------ the landing heading (the hover turn before the descent)
        /// <summary>The heading it comes down on (deg; set at the arrival when the spot is free on it) and whether one is set.</summary>
        public double ShuttleLandHeadingDeg { get; internal set; }
        public bool ShuttleLandHeadingSet { get; internal set; }
        /// <summary>Hovering in place over its landing spot, turning onto ShuttleLandHeadingDeg so that the ramp faces a free side.</summary>
        public bool ShuttleLandTurning { get; internal set; }
        /// <summary>The hover turn onto the landing heading of its last landing (s; 0 = it came straight down): a flight's time is the
        /// straight flight's + this.</summary>
        public double ShuttleLastLandTurnSec { get; internal set; }
        double shuttleLandTurnAt;
        /// <summary>Check counters (all shuttles): landings that turned first, take-offs that waited for units, robots blown aside.</summary>
        public static int ShuttleLandTurns, ShuttleLiftWaits, ShuttleLiftNudges;
        /// <summary>The longest take-off wait for units so far (s, all shuttles).</summary>
        public static double ShuttleLiftWaitMax;
        double shuttleLiftWaitSince = -1.0, shuttleLiftLastAt = -1.0;

        /// <summary>Over its landing spot with the spot free on `hd`: straight down (it already faces it) or a hover turn first.</summary>
        internal void ShuttleLandBegin(double hd)
        {
            ShuttleLandHeadingDeg = ShuttleNorm(hd); ShuttleLandHeadingSet = true; ShuttleNoLand = "";
            if (Math.Abs(ShuttleDelta(ShuttleHeadingDeg, ShuttleLandHeadingDeg)) <= ZWorld.ShuttleDeckSeatLeftDeg)
            {
                ShuttleLandTurning = false; ShuttleLastLandTurnSec = 0; ShuttleState = ZShuttleState.Descend; ShuttleDescentBegins(); World.ShuttleNote(ZShuttleNoteKind.Arrived, this);
                return;
            }
            ShuttleLandTurning = true; ShuttleLandTurns++; shuttleLandTurnAt = World.Time;                   // state Cruise, no movement: the hover turn
        }

        /// <summary>The hover turn onto the landing heading (the deck turn's rate and ease); the spot taken meanwhile: another heading that
        /// fits, else it hovers there ("can't land").</summary>
        internal void ShuttleLandTurnStep(double dt)
        {
            var w = World;
            if (!w.ShuttlePadFreeAt(ShuttleLineX, ShuttleLineY, this, ShuttleLandHeadingDeg, out string why))
            {
                if (w.ShuttleLandSpot(ShuttleLineX, ShuttleLineY, this, ShuttleHeadingDeg, out double hd2, out string why2)) { ShuttleLandHeadingDeg = ShuttleNorm(hd2); }
                else
                {
                    ShuttleLandTurning = false; ShuttleLandHeadingSet = false; ShuttleNoTurn(dt);
                    ShuttleState = ZShuttleState.Hover; ShuttleNoLand = why2; ShuttleHovers++; w.ShuttleNote(ZShuttleNoteKind.CannotLand, this, why2);
                    return;
                }
            }
            double err = ShuttleStrafeBusy ? ShuttleDelta(ShuttleHeadingDeg, ShuttleLandHeadingDeg) : ShuttleHoverTurn(ShuttleLandHeadingDeg, dt);
            if (ShuttleStrafeBusy) ShuttleNoTurn(dt);
            if (Math.Abs(err) <= ZWorld.ShuttleDeckSeatLeftDeg && Math.Abs(ShuttleTurnRateNow) <= ZWorld.ShuttleDeckSeatRateDeg && !ShuttleStrafeBusy)
            {
                ShuttleLandTurning = false; ShuttleLastLandTurnSec = w.Time - shuttleLandTurnAt; ShuttleState = ZShuttleState.Descend; ShuttleDescentBegins(); w.ShuttleNote(ZShuttleNoteKind.Arrived, this);
            }
        }

        /// <summary>The descent's pad test: on a repair deck the station's rule, else the spot at the heading it comes down on.</summary>
        internal bool ShuttleDescentPadFree(out string why)
        {
            if (ShuttleOnDeckApproach() || !ShuttleLandHeadingSet) return World.ShuttlePadFree(ShuttleLineX, ShuttleLineY, this, out why);
            return World.ShuttlePadFreeAt(ShuttleLineX, ShuttleLineY, this, ShuttleLandHeadingDeg, out why);
        }

        /// <summary>The heading the descent turns onto: the deck's, or the landing heading (null: it keeps its facing).</summary>
        internal double? ShuttleSeatHeading(bool deck) => deck ? ZWorld.ShuttleDeckHeadingDeg : ShuttleLandHeadingSet ? ShuttleLandHeadingDeg : (double?)null;

        // ------------------------------------------------------------------ the take-off: units out of the way first
        /// <summary>A take-off is wanted (ramp shut, level): true = it waits this tick (a ground unit within ShuttleLiftClearM of the
        /// footprint, waited less than ShuttleLiftWaitSec); standing robots there are moved straight out at ShuttleLiftNudgeMps.</summary>
        internal bool ShuttleLiftWait(double dt)
        {
            var w = World; double now = w.Time;
            if (shuttleLiftLastAt >= 0 && now - shuttleLiftLastAt > 2.5 * dt + 1e-9) shuttleLiftWaitSince = -1.0;   // a new take-off order: a new wait
            shuttleLiftLastAt = now;
            bool near = false;
            double mx = ShuttleMidX, my = ShuttleMidY, reach = (ShuttlePlanRadiusM + ZWorld.ShuttleLiftClearM + 1.0) * 8.0;
            foreach (var o in w.Objects)
            {
                if (o == this || !o.IsUnit || o.Destroyed || o.KillAt >= 0 || o.ShuttleAirborne || o.IsFortDefender) continue;
                double dx = o.CenterX - mx, dy = o.CenterY - my;
                if (Math.Abs(dx) > reach || Math.Abs(dy) > reach) continue;
                double gap = ShuttlePlanGap(mx, my, ShuttleHeadingDeg, o.LocX + o.XOver + o.WidthPix * 0.5, o.LocY + o.YOver + o.HeightPix * 0.5);
                if (gap >= ZWorld.ShuttleLiftClearM) continue;
                near = true;
                if (shuttleLiftWaitSince >= 0 && now - shuttleLiftWaitSince >= ZWorld.ShuttleLiftWaitSec) continue;
                if (!o.IsRobot || (o.Waypoints.Count > 0 && o.IsMoving)) continue;                          // a walking robot walks on; vehicles: the ship waits
                double l = Math.Sqrt(dx * dx + dy * dy), step = ZWorld.ShuttleLiftNudgeMps * 8.0 * dt;
                double ux, uy;
                if (l > 1e-6) { ux = dx / l; uy = dy / l; } else { double h = ShuttleHeadingDeg * Math.PI / 180.0; ux = -Math.Sin(h); uy = Math.Cos(h); }
                if (o.PathNudge(ux * step, uy * step)) ShuttleLiftNudges++;
            }
            if (!near) { EndLiftWait(now); return false; }
            if (shuttleLiftWaitSince < 0) { shuttleLiftWaitSince = now; ShuttleLiftWaits++; }
            if (now - shuttleLiftWaitSince >= ZWorld.ShuttleLiftWaitSec - 1e-9) { EndLiftWait(now); return false; }   // at most this long: it goes
            return true;
        }

        void EndLiftWait(double now)
        {
            if (shuttleLiftWaitSince >= 0) ShuttleLiftWaitMax = Math.Max(ShuttleLiftWaitMax, now - shuttleLiftWaitSince);
            shuttleLiftWaitSince = -1.0;
        }

        /// <summary>World.Time from which it waited for units at its take-off (-1 = not waiting): the view can spool the jets up meanwhile.</summary>
        public double ShuttleLiftWaitSince => shuttleLiftWaitSince;

        // ------------------------------------------------------------------ the squad's wait slots: on the back axis, in arrival order
        int shuttleRankLeader = -1; double shuttleRankAt = -1.0; readonly List<int> shuttleRankIds = new List<int>();

        /// <summary>The back slot a robot ordered into this ship walks to: the leader the ramp foot (0); the minions 1, 2, ... in the order
        /// they will arrive (nearest to the foot first, when the order is given; ties by id; kept while the leader's ENTER order on this
        /// ship stands), so nobody walks past a squad mate.</summary>
        internal int ShuttleArrivalSlot(ZObject r)
        {
            if (r == null || !r.IsMinion || r.Leader == null) return 0;
            var lead = r.Leader; double now = World != null ? World.Time : 0.0;
            bool leaderOrder = lead.Waypoints.Count > 0 && lead.Waypoints[0].mode == WpMode.Enter && lead.Waypoints[0].refId == Id;
            if (shuttleRankLeader != lead.Id || !shuttleRankIds.Contains(r.Id) || (now - shuttleRankAt > 0.5 && !leaderOrder))   // kept for the whole order (no re-ranking on the way)
            {
                shuttleRankLeader = lead.Id; shuttleRankAt = now; shuttleRankIds.Clear();
                var (fx, fy) = ShuttleBackSpot(0, false, null, out _);
                var list = new List<(double d, int id)>();
                foreach (var m in lead.Minions)
                    if (m != null && !m.Destroyed && m.KillAt < 0) { double dx = m.CenterX - fx, dy = m.CenterY - fy; list.Add((dx * dx + dy * dy, m.Id)); }
                list.Sort((a, b) => a.d != b.d ? a.d.CompareTo(b.d) : a.id.CompareTo(b.id));
                foreach (var e in list) shuttleRankIds.Add(e.id);
            }
            int k = shuttleRankIds.IndexOf(r.Id);
            return k < 0 ? 1 : k + 1;
        }

        /// <summary>The ground of a robot's spot (x, y) px: every tile under its body on the map, open for robots, not water (things standing
        /// there not counted).</summary>
        bool ShuttleSpotGround(int x, int y)
        {
            var m = World.Map; int rr = ZPathing.RobotRadius;
            if (x < 16 + rr || y < 16 + rr || x >= m.WidthPix - 16 - rr || y >= m.HeightPix - 16 - rr) return false;
            for (int ty = (y - rr) >> 4; ty <= (y + rr) >> 4; ty++)
                for (int tx = (x - rr) >> 4; tx <= (x + rr) >> 4; tx++)
                {
                    var c = m.ClassAt(tx, ty);
                    if (c == ZClass.DeepWater || c == ZClass.Water || c == ZClass.Impassable || !World.Path.TilePassable(tx, ty, true)) return false;
                }
            return true;
        }

        /// <summary>The k-th free spot on the back axis (k = the slot): the spots ShuttleBackM + j x ShuttleFileM behind the middle, j =
        /// 0, 1, 2 ...; a spot taken by a parked vehicle or gun (or a standing robot, withRobots) is skipped, so the later slots move on
        /// further back on the same axis; the axis ends at the first spot on ground a robot cannot stand on (a closed or water tile: nobody
        /// is ever put beyond a hedge or a ravine) or ShuttleSlotsBeyond spots past the slot. None free: the slot's own spot 25 degrees to
        /// either side; then the plain spot (fallback = not the plain spot).</summary>
        internal (int x, int y) ShuttleAxisSpot(int slot, bool withRobots, ICollection<ZObject> skip, out bool fallback)
        {
            fallback = false;
            slot = Math.Max(0, slot);
            double hd = ShuttleHeadingDeg * Math.PI / 180.0, bx = -Math.Cos(hd), by = -Math.Sin(hd);
            int Px(double r) => (int)Math.Round(CenterX + bx * r * 8.0);
            int Py(double r) => (int)Math.Round(CenterY + by * r * 8.0);
            double r0 = ZWorld.ShuttleBackM + slot * ZWorld.ShuttleFileM;
            if (World == null || World.Map == null) return (Px(r0), Py(r0));
            int free = 0;
            for (int j = 0; j <= slot + ZWorld.ShuttleSlotsBeyond; j++)
            {
                double r = ZWorld.ShuttleBackM + j * ZWorld.ShuttleFileM;
                if (!ShuttleSpotGround(Px(r), Py(r))) break;                                               // the axis ends at a hedge, a ravine, water
                if (!ShuttleSpotFree(Px(r), Py(r), withRobots, skip)) continue;
                if (free == slot) { fallback = j != slot; return (Px(r), Py(r)); }
                free++;
            }
            fallback = true;
            double back = Math.Atan2(by, bx);
            foreach (int off in ShuttleSlotTurns)
            {
                if (off == 0) continue;
                double a = back + off * Math.PI / 180.0;
                int px = (int)Math.Round(CenterX + Math.Cos(a) * r0 * 8.0), py = (int)Math.Round(CenterY + Math.Sin(a) * r0 * 8.0);
                if (ShuttleSpotFree(px, py, withRobots, skip)) return (px, py);
            }
            return (Px(r0), Py(r0));
        }
    }

    public sealed partial class ZWorld
    {
        /// <summary>The landing spot: the footprint grown by this (m) must be free; the ramp lane: this half width (m), up to this far behind
        /// the ramp foot (m); plants this tall (m) or taller count (ferns, grass and flowers do not).</summary>
        public const double ShuttleSpotClearM = 0.5, ShuttleLaneHalfW = 1.5, ShuttleLaneBehindM = 6.0, ShuttlePlantMinM = 0.6;
        /// <summary>A keep-out circle wider than this (m) is a joined mesh of many small things (the level's flower carpet), not one plant:
        /// it is left out.</summary>
        public const double ShuttleKeepOutMaxM = 10.0;
        /// <summary>The turn to a free side: at most this far (deg) either way, in this step (deg). Every heading is tried (the smaller turn
        /// first): the review's +-90 would make a spot landable for a ship arriving one way and not for one arriving another, so the
        /// pointer's "can land here" (ShuttlePadFree) would not be what the ship does when it gets there.</summary>
        public const int ShuttleLandTurnMaxDeg = 180, ShuttleLandTurnStepDeg = 15;
        /// <summary>The heading a ship dropped without one looks for first (deg: nose north, the ramp toward the camera).</summary>
        public const double ShuttleDropHeadingDeg = -90.0;
        /// <summary>The take-off: units within this of the footprint (m) make it wait, at most this long (s); robots there are moved out at
        /// this speed (m/s).</summary>
        public const double ShuttleLiftClearM = 2.0, ShuttleLiftWaitSec = 3.0, ShuttleLiftNudgeMps = 2.0;
        /// <summary>Shot down on a repair station's deck: the station takes nobody for this long (s) while the wreck clears off it.</summary>
        public const double ShuttleDeckWreckSec = 4.0;
        /// <summary>The wait slots: free spots looked for this many slots further back on the axis before the 25 degree side spots.</summary>
        public const int ShuttleSlotsBeyond = 6;

        // ------------------------------------------------------------------ the level's drawn plants (and other drawn things) as circles
        /// <summary>Keep-out circles (px: x, y, radius; m: height), sorted, in 4-tile buckets. Handed in by the view at the level start
        /// (the level's own plants, fixed by the level: the same list every time); empty in a headless match.</summary>
        readonly List<double[]> shuttleKeepOut = new List<double[]>();
        readonly Dictionary<long, List<int>> shuttleKeepOutCells = new Dictionary<long, List<int>>();
        readonly HashSet<int> shuttleKeepOutSeen = new HashSet<int>();                                       // the spot test's circles seen once (reused)
        /// <summary>How many circles are held, and the hand-over's stamp (the view hands them in again only when its list changes).</summary>
        public int ShuttleKeepOutCount => shuttleKeepOut.Count;
        public int ShuttleKeepOutStamp { get; private set; } = -1;
        /// <summary>Check counter: spots refused for a plant crown.</summary>
        public static int ShuttleKeepOutRefusals;
        const int KeepOutCell = 64;

        static long KeepOutKey(int cx, int cy) => ((long)cx << 32) ^ (uint)cy;

        /// <summary>The view (or a check): the keep-out circles, flattened as x, y, r (px) and h (m) per circle; stamp = a number of the
        /// list (any change of it replaces the whole list). Sorted by x, y, r, h, so the order handed in never matters.</summary>
        public void ShuttleSetKeepOut(IList<double> xyrh, int stamp)
        {
            shuttleKeepOut.Clear(); shuttleKeepOutCells.Clear(); ShuttleKeepOutStamp = stamp;
            if (xyrh == null) return;
            for (int i = 0; i + 3 < xyrh.Count; i += 4)
            {
                double x = xyrh[i], y = xyrh[i + 1], r = xyrh[i + 2], h = xyrh[i + 3];
                if (double.IsNaN(x) || double.IsNaN(y) || r <= 0 || r > ShuttleKeepOutMaxM * 8.0 || h < ShuttlePlantMinM) continue;
                shuttleKeepOut.Add(new[] { Math.Round(x, 2), Math.Round(y, 2), Math.Round(r, 2), Math.Round(h, 2) });
            }
            shuttleKeepOut.Sort((a, b) => a[0] != b[0] ? a[0].CompareTo(b[0]) : a[1] != b[1] ? a[1].CompareTo(b[1]) : a[2] != b[2] ? a[2].CompareTo(b[2]) : a[3].CompareTo(b[3]));
            for (int k = 0; k < shuttleKeepOut.Count; k++)
            {
                var c = shuttleKeepOut[k];
                for (int bx = (int)Math.Floor((c[0] - c[2]) / KeepOutCell); bx <= (int)Math.Floor((c[0] + c[2]) / KeepOutCell); bx++)
                    for (int by = (int)Math.Floor((c[1] - c[2]) / KeepOutCell); by <= (int)Math.Floor((c[1] + c[2]) / KeepOutCell); by++)
                    {
                        var key = KeepOutKey(bx, by);
                        if (!shuttleKeepOutCells.TryGetValue(key, out var l)) shuttleKeepOutCells[key] = l = new List<int>();
                        l.Add(k);
                    }
            }
        }

        /// <summary>A plant whose tree (a level 16 tree that closes a tile, ZWorld.TreeTiles.cs) has been felled no longer counts.</summary>
        bool KeepOutFelled(double[] c)
        {
            foreach (var t in TreeTiles)
            {
                if (!t.Destroyed) continue;
                double dx = t.LocX + 8 - c[0], dy = t.LocY + 8 - c[1];
                if (dx * dx + dy * dy <= 16.0 * 16.0) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ the landing / parking spot
        /// <summary>The ship (self; null = one being dropped) can land with its middle at (cx, cy) on some heading within
        /// ShuttleLandTurnMaxDeg of hd0 (hd = that heading; the smaller turn first). On its repair station's deck the station's rule and
        /// the deck heading. why = what is in the way at hd0 (no heading fits).</summary>
        public bool ShuttleLandSpot(int cx, int cy, ZObject self, double hd0, out double hd, out string why)
        {
            hd = hd0; why = "";
            if (Map == null) { why = "no map"; return false; }
            var rs = ShuttleRepairStation(self);
            if (rs != null && self.ShuttleDeckPoint(rs, out int ex, out int ey) && Math.Abs(cx - ex) <= 16 && Math.Abs(cy - ey) <= 16)
            { hd = ShuttleDeckHeadingDeg; return ShuttleDeckPadFree(cx, cy, self, out why); }
            string first = null;
            for (int t = 0; t <= ShuttleLandTurnMaxDeg; t += ShuttleLandTurnStepDeg)
                foreach (int sg in t == 0 || t == 180 ? OneSide : BothSides)
                {
                    double h = hd0 + sg * t;
                    if (ShuttlePadFreeAt(cx, cy, self, h, out string w)) { hd = h; return true; }
                    if (first == null) first = w;
                }
            if (ShuttleFairSpotOn && !shuttleSpotNoUnits && ShuttleFairSpotFine(cx, cy, self, hd0, out hd, ref first)) return true;   // DV128: then every multiple of 5 degrees (the click's lattice; the click itself tries them all), the smaller turn first: where the click took the order it lands; none fits: the place's own word
            why = first ?? "blocked";
            return false;
        }

        static readonly int[] OneSide = { 1 }, BothSides = { 1, -1 };

        /// <summary>DV118: the shuttle (self; null = a dropped one) can land with its middle at (cx, cy) on some heading (its own first, then
        /// the smaller turns; ShuttleLandSpot). why = the first thing in the way.</summary>
        public bool ShuttlePadFree(int cx, int cy, ZObject self, out string why) =>
            ShuttleLandSpot(cx, cy, self, self != null ? self.ShuttleHeadingDeg : ShuttleDropHeadingDeg, out _, out why);

        /// <summary>The landing spot on exactly heading hdDeg (not a repair deck): the footprint + ShuttleSpotClearM on ground or road open
        /// for vehicles with no unit, building, map item or plant crown on it; then the ramp lane on ground or road a robot can stand on with
        /// no building, map item or plant crown on it. why = the first part's worst (the footprint's before the lane's, so the why of a spot
        /// hardly depends on the heading tried; tiles: water, then rocks, then a closed tile, then the map's edge; then units, buildings, map
        /// items, plants): "water", "rocks", "blocked", "map edge", "units", "building", "flag", "bridge", "item", "trees".</summary>
        public bool ShuttlePadFreeAt(int cx, int cy, ZObject self, double hdDeg, out string why)
        {
            why = "";
            if (Map == null) { why = "no map"; return false; }
            return ShuttleSpotPart(cx, cy, self, hdDeg, false, out why) && ShuttleSpotPart(cx, cy, self, hdDeg, true, out why);
        }

        /// <summary>One part of the landing spot: the footprint + ShuttleSpotClearM (lane false) or the ramp lane (lane true).</summary>
        bool ShuttleSpotPart(int cx, int cy, ZObject self, double hdDeg, bool lanePart, out string why)
        {
            why = "";
            if (ShuttleFairSpotOn) return ShuttleFairSpotPart(cx, cy, self, hdDeg, lanePart, out why);   // DV128: the ship as drawn: dry ground under its gear, the pods may reach over water, a flag by its pole, a gun always (Sim/ZWorld.ShuttleFairSpot.cs)
            double hd = hdDeg * Math.PI / 180.0, nx = Math.Cos(hd), ny = Math.Sin(hd), rxv = -Math.Sin(hd), ryv = Math.Cos(hd);
            double laneZ0 = -(ShuttleBackM + ShuttleLaneBehindM), laneZ1 = -ZObject.ShuttleHullBackM, grow = ShuttleSpotClearM;
            void Root(double x, double y, out double rx, out double rz) { double dx = (x - cx) / 8.0, dy = (y - cy) / 8.0; rz = dx * nx + dy * ny; rx = dx * rxv + dy * ryv; }
            bool InLane(double rx, double rz) => Math.Abs(rx) <= ShuttleLaneHalfW && rz >= laneZ0 && rz <= laneZ1;
            bool InPart(double rx, double rz) { bool foot = ZObject.ShuttlePlanGapRoot(rx, rz) < grow; return lanePart ? !foot && InLane(rx, rz) : foot; }
            double LaneDist(double rx, double rz) { double qx = Math.Max(0.0, Math.Abs(rx) - ShuttleLaneHalfW), qz = Math.Max(0.0, Math.Max(laneZ0 - rz, rz - laneZ1)); return Math.Sqrt(qx * qx + qz * qz); }
            double reachM = (lanePart ? Math.Sqrt(ShuttleLaneHalfW * ShuttleLaneHalfW + laneZ0 * laneZ0) : ZObject.ShuttlePlanRadiusM + grow) + 0.5;
            int bx0 = (int)Math.Floor(cx - reachM * 8), by0 = (int)Math.Floor(cy - reachM * 8), bx1 = (int)Math.Ceiling(cx + reachM * 8), by1 = (int)Math.Ceiling(cy + reachM * 8);
            // ---- the tiles, sampled every 4 px over the part's box: the worst one found
            int worst = 0;                                                                                  // 4 water, 3 rocks, 2 blocked, 1 map edge
            for (int py = by0; py <= by1 && worst < 4; py += 4)
                for (int px = bx0; px <= bx1 && worst < 4; px += 4)
                {
                    Root(px, py, out double rx, out double rz);
                    if (!InPart(rx, rz)) continue;
                    int tx = px >> 4, ty = py >> 4;
                    if (px < 0 || py < 0 || tx >= Map.width || ty >= Map.height) { worst = Math.Max(worst, 1); continue; }
                    var c = Map.ClassAt(tx, ty);
                    if (c == ZClass.Water || c == ZClass.DeepWater) worst = 4;
                    else if (c == ZClass.Impassable) worst = Math.Max(worst, 3);
                    else if (!Path.TilePassable(tx, ty, lanePart)) worst = Math.Max(worst, 2);           // the footprint: open for vehicles; the lane: for robots
                }
            if (worst > 0) { why = worst == 4 ? "water" : worst == 3 ? "rocks" : worst == 2 ? "blocked" : "map edge"; return false; }
            // ---- things standing there (units on the footprint only; buildings and map items on either)
            foreach (var o in Objects)
            {
                if (o == self || o.Destroyed || o.KillAt >= 0) continue;
                if (o.LocX > cx + reachM * 8 + 4 || o.LocX + o.WidthPix < cx - reachM * 8 - 4 || o.LocY > cy + reachM * 8 + 4 || o.LocY + o.HeightPix < cy - reachM * 8 - 4) continue;
                if (o.IsUnit)
                {
                    if (lanePart || o.ShuttleAirborne || shuttleSpotNoUnits) continue;                // K256g: ShuttleNeverLands leaves the units out (they walk away; Sim/ZWorld.ShuttleOrders.cs)
                    Root(o.LocX + o.XOver + o.WidthPix * 0.5, o.LocY + o.YOver + o.HeightPix * 0.5, out double ux, out double uz);
                    if (ZObject.ShuttlePlanGapRoot(ux, uz) < grow + ZPathing.Radius(o) / 8.0) { why = "units"; return false; }
                    continue;
                }
                if (o.Kind != ZKind.Building && o.Kind != ZKind.MapItem) continue;
                bool hit = !lanePart && o.Contains(cx, cy);
                for (int yy = o.LocY; yy <= o.LocY + o.HeightPix && !hit; yy += 4)
                    for (int xx = o.LocX; xx <= o.LocX + o.WidthPix && !hit; xx += 4)
                    {
                        Root(Math.Min(xx, o.LocX + o.WidthPix - 0.01), Math.Min(yy, o.LocY + o.HeightPix - 0.01), out double qx, out double qz);
                        hit = InPart(qx, qz);
                    }
                if (!hit) continue;
                why = o.Kind == ZKind.Building ? "building" : o.Type == "flag" ? "flag" : o.IsBridge ? "bridge" : "item";
                return false;
            }
            // ---- the plants' crowns
            if (shuttleKeepOut.Count > 0)
            {
                var seen = shuttleKeepOutSeen; seen.Clear();                                                 // one set for every test (no allocation per tick)
                for (int bx = (int)Math.Floor((cx - reachM * 8) / KeepOutCell); bx <= (int)Math.Floor((cx + reachM * 8) / KeepOutCell); bx++)
                    for (int by = (int)Math.Floor((cy - reachM * 8) / KeepOutCell); by <= (int)Math.Floor((cy + reachM * 8) / KeepOutCell); by++)
                    {
                        if (!shuttleKeepOutCells.TryGetValue(KeepOutKey(bx, by), out var l)) continue;
                        foreach (int k in l)
                        {
                            if (!seen.Add(k)) continue;
                            var c = shuttleKeepOut[k];
                            Root(c[0], c[1], out double qx, out double qz);
                            double r = c[2] / 8.0;
                            if (lanePart ? LaneDist(qx, qz) >= r : ZObject.ShuttlePlanGapRoot(qx, qz) - r >= grow) continue;
                            if (KeepOutFelled(c)) continue;
                            why = "trees"; ShuttleKeepOutRefusals++;
                            return false;
                        }
                    }
            }
            return true;
        }

        // ------------------------------------------------------------------ the footprint as a hard obstacle
        /// <summary>ZPathing.PairExempt for a pair with a shuttle in it: no rule lets a unit through the landed ship's ring, only a unit in a
        /// building's own door stage (its scripted walk out of the door).</summary>
        internal static bool ShuttlePairExempt(ZObject a, ZObject b)
        {
            var u = a.IsShuttle ? b : a;
            return u.PathInBuildingStage || u.FortRushOn;
        }

        /// <summary>Ground units (robots, vehicles, guns; not the ship, nothing in the air) whose middle is inside the ship's outline grown
        /// by marginM (the proofs' count: 0 while it stands or lifts off).</summary>
        public int ShuttleUnitsInside(ZObject s, double marginM)
        {
            if (s == null || !s.IsShuttle) return 0;
            int n = 0;
            foreach (var o in Objects)
            {
                if (o == s || !o.IsUnit || o.Destroyed || o.KillAt >= 0 || o.ShuttleAirborne) continue;
                if (s.ShuttleOutlineGapM(o.LocX + o.XOver + o.WidthPix * 0.5, o.LocY + o.YOver + o.HeightPix * 0.5) < marginM) n++;
            }
            return n;
        }

        // ------------------------------------------------------------------ the wreck on a repair station's deck
        /// <summary>ShuttleDown hook: destroyed on (or coming down onto, or over) its repair station's deck: the station takes nobody else
        /// for ShuttleDeckWreckSec and its crane stays away meanwhile.</summary>
        void ShuttleDeckWreck(ZObject o)
        {
            if (o == null || o.ShuttleRepairTarget < 0) return;
            var st = Get(o.ShuttleRepairTarget);
            if (st == null || st.Destroyed || !(o.ShuttleOnRepairPad || o.ShuttleOnDeckApproach())) return;
            st.ShuttleWreckUntil = Time + ShuttleDeckWreckSec; ShuttleDeckWrecks++;
            if (st.RepairOccupantId < 0 || st.RepairOccupantId == o.Id) st.RepairOccupantId = o.Id;          // the bay holds the wreck
        }

        /// <summary>Check counter: ships destroyed on (or over) a repair deck.</summary>
        public static int ShuttleDeckWrecks;
    }

    public partial class ZObject
    {
        /// <summary>A repair station: World.Time until which a shuttle's wreck lies on its deck (the bay and the crane wait for it).</summary>
        public double ShuttleWreckUntil = -1.0;
        /// <summary>The station's deck holds a shuttle's wreck now.</summary>
        public bool ShuttleWreckOnDeck => World != null && World.Time < ShuttleWreckUntil;
    }

    public partial class ZObject
    {
        /// <summary>A heading in (-180, 180] degrees.</summary>
        internal static double ShuttleNormDeg(double a) => ShuttleNorm(a);
    }

    public sealed partial class ZPathing
    {
        /// <summary>GateCore, a unit passing through traffic (its ghost time): its step still never comes closer than the spacing to a landed
        /// shuttle (the hard obstacle; ZWorld.ShuttlePairExempt decides the few that may). True = the step is refused.</summary>
        bool ShuttleRingBlocks(ZObject a, double ocx, double ocy, double ncx, double ncy)
        {
            if (W.ShuttlesMadeCount == 0 || a.IsShuttle) return false;
            foreach (var b in W.Objects)
            {
                if (!b.IsShuttle || b == a || b.Destroyed || b.KillAt >= 0 || b.ShuttleAirborne || ZWorld.ShuttlePairExempt(a, b)) continue;
                double min = MinSpacing(a, b), bx = CX(b), by = CY(b);
                double dnx = ncx - bx, dny = ncy - by, dn2 = dnx * dnx + dny * dny;
                if (dn2 >= min * min) continue;
                double dox = ocx - bx, doy = ocy - by;
                if (dn2 >= dox * dox + doy * doy - 1e-9) continue;                                       // not getting closer: allowed
                return true;
            }
            return false;
        }
    }

    public sealed partial class ZWorld
    {
        /// <summary>Shuttles made in this match (map load and the testing panel).</summary>
        internal int ShuttlesMadeCount => shuttlesMade;
    }
}
