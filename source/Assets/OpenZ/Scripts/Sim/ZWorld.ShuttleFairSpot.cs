using System;

namespace OpenZ.Sim
{
    /// <summary>
    /// DV128, the shuttle's landing spot measured on the ship as it is drawn (on top of DV118's rule, Sim/ZWorld.ShuttleGround.cs;
    /// deterministic: positions and fixed lists only, no random draw, no frame time). Every landing, the click's "can it ever land here"
    /// (ShuttleNeverLands: the cursor, the order filter), K338's free spots, the testing panel's drop and the empty ship's parking ask the same
    /// rule, so the cursor, the red mark and the flight agree.
    ///  * THE GROUND UNDER THE FEET: only the landing gear stands on the ground. Its plan (<see cref="ZObject.ShuttleGearPlan"/>: the four pads'
    ///    soles and everything between them, measured from the model; every part of the ship lower than <see cref="ZWorld.ShuttleLowM"/>
    ///    over the ground lies inside it) grown by <see cref="ZWorld.ShuttleGearClearM"/> must lie on dry ground: no water tile under it.
    ///    The rest of the ship (the engine pods from 1.08 m up, the nose and the hull's sides from 1.0 m up) may reach out over water or a
    ///    bank, as a lander does. A plant lower than ShuttleLowM counts only under the gear (+ the same margin): it fits under the pods.
    ///  * WHAT IS TALL STAYS OUT OF THE WHOLE SHIP: the whole outline (ZObject.ShuttlePlan, hull + pods + legs) grown by ShuttleSpotClearM
    ///    (0.5 m) keeps off rocks (closed map tiles), closed tiles (trees, props, building meshes), the map's edge, buildings, huts, crates,
    ///    bridges and props (their boxes, as before), plant crowns of ShuttleLowM and taller (as before) and units (as before). A flag counts
    ///    by its pole (<see cref="ZWorld.ShuttleFlagPoleM"/>) and its cloth (<see cref="ZWorld.ShuttleFlagClothM"/> round the pole, clear of
    ///    the ship's parts at its height, <see cref="ZObject.ShuttleUpperPlan"/>), not by its whole tile: the ship may stand beside a flag,
    ///    and robots still reach it from the other side. A gun (a cannon) counts always, also in the click's test (it never walks away), by its ring (2 m: the
    ///    drawn gun turning, barrels included) as the arrival tested it before: the ship never lands on or over a gun, its ramp never on one.
    ///  * THE RAMP AND THE SQUAD'S LINE (the lane behind, as before ShuttleLaneHalfW x 2 wide up to ShuttleLaneBehindM behind the ramp foot):
    ///    the RAMP itself (the leaf, <see cref="ZWorld.ShuttleRampHalfW"/> either side of the axis, from the hull's back to the first robot's
    ///    spot at the ramp foot) comes down on dry ground a robot can stand on, with no flag pole and no gun on it; behind it the squad's line
    ///    needs ground a robot can stand on: shallow water counts (robots wade), deep water, rocks and closed tiles do not; buildings, huts,
    ///    crates, bridges, props and plant crowns stay off the whole lane as before.
    ///  * THE ARRIVAL TRIES THE CLICK'S HEADINGS TOO: the landing tries the 15-degree lattice from the heading it arrives on (as before, the
    ///    smaller turn first); when none fits it also tries every multiple of 5 degrees (the click's lattice, ZWorld.ShuttleNeverLattices),
    ///    the smaller turn first. So a place the click took is a place it lands on (unless a unit stands there when it arrives).
    /// <see cref="ZWorld.ShuttleFairSpotOn"/> off (checks only) = the rule exactly as before (the footprint + 0.5 m dry, the lane dry).
    /// </summary>
    public partial class ZObject
    {
        /// <summary>The landing gear's plan: the convex outline of the four pad soles (x right, z nose, metres from the middle,
        /// counter-clockwise; the model's SH_Leg_* parts, which also hold every part of the hull lower than 1.0 m over the ground).</summary>
        internal static readonly double[] ShuttleGearPlan = { -2.754, 1.055, -2.332, -2.822, -2.328, -2.830, -2.237, -2.999, -2.119, -3.169, -1.998, -3.329, 2.003, -3.333, 2.190, -3.073, 2.267, -2.955, 2.353, -2.817, 2.752, 1.087, 2.446, 1.587, -2.162, 1.593, -2.441, 1.584, -2.567, 1.386, -2.751, 1.077 };

        /// <summary>The ship's plan at a flag's height and up (every part 2.2 m or more over the ground: the hull's upper body and the pods; the
        /// legs' back corners are lower), the same root space; measured from the model.</summary>
        internal static readonly double[] ShuttleUpperPlan = { -3.194, -0.057, -3.182, -0.499, -3.156, -0.724, -3.138, -0.873, -3.000, -1.228, -1.301, -2.718, -1.194, -2.809, -1.101, -2.880, -0.895, -3.031, -0.412, -3.240, 0.017, -3.261, 0.454, -3.226, 0.906, -3.005, 1.165, -2.839, 1.258, -2.765, 3.003, -1.225, 3.151, -0.822, 3.174, -0.620, 3.194, -0.441, 3.194, -0.041, 3.188, 0.182, 3.066, 0.902, 3.050, 0.930, 1.025, 2.892, 0.945, 2.963, 0.763, 3.114, 0.517, 3.243, 0.326, 3.305, 0.133, 3.333, -0.190, 3.323, -0.358, 3.294, -0.531, 3.237, -0.771, 3.106, -1.057, 2.856, -3.054, 0.911, -3.073, 0.839, -3.184, 0.162, -3.191, 0.078 };

        /// <summary>Metres from the root-space point (rx, rz) to the convex outline p (negative = inside).</summary>
        internal static double ShuttlePolyGapRoot(double[] p, double rx, double rz)
        {
            int n = p.Length / 2; bool inside = true; double best = double.MaxValue;
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

        /// <summary>Metres from the root-space point (rx, rz) to the landing gear's plan (negative = inside).</summary>
        internal static double ShuttleGearGapRoot(double rx, double rz) => ShuttlePolyGapRoot(ShuttleGearPlan, rx, rz);
    }

    public sealed partial class ZWorld
    {
        /// <summary>DV128 switch (the game: on; checks may switch it off to see the rule as it was).</summary>
        public static bool ShuttleFairSpotOn = true;
        /// <summary>The gear's margin over dry ground (m: the drawn bank runs on into the water tile); the ramp leaf's half width with its margin
        /// (m: the leaf is 1.5 m wide); a flag's pole (m: its radius; the margin is ShuttleSpotClearM as for every tall thing) and its cloth
        /// (m: 1.45 m long, 2.3 to 3.2 m up, flying downwind; the wind turns, so all round the pole, against the ship's upper plan); the height
        /// under which a part of the ship lies inside the gear's plan (m: the hull's lowest skin outside it is 1.04 m up, the pods' 1.08 m).</summary>
        public const double ShuttleGearClearM = 0.1, ShuttleRampHalfW = 1.0, ShuttleFlagPoleM = 0.05, ShuttleFlagClothM = 1.45, ShuttleLowM = 1.0;
        /// <summary>The finer lattice the arrival tries after its own (deg): every multiple of this, the click's lattice.</summary>
        public const int ShuttleFineStepDeg = 5;
        /// <summary>A circle round the ship's middle that lies inside its outline (m; the outline's own inscribed radius there is 2.818 m): the
        /// sampled test skips the outline's distance for points inside it (and beyond ShuttlePlanRadiusM + the margin), the same answer cheaper.</summary>
        const double ShuttlePlanInM = 2.80;
        /// <summary>Check counters: spots refused for water under the gear, spots taken with water under the pods or the nose (refused before),
        /// spots refused for a gun, landings that found their heading only on the finer lattice.</summary>
        public static int ShuttleGearWaterRefusals, ShuttleOverhangWater, ShuttleGunRefusals, ShuttleFineHeadings;

        /// <summary>DV128, ZWorld.ShuttleLandSpot when its 15-degree lattice from hd0 found nothing: every multiple of ShuttleFineStepDeg (the
        /// click's lattice), the smaller turn from hd0 first (a turn to the right before the same turn to the left); hd = the first that fits.
        /// None fits: why = the place's own word, the same from every arrival heading (the 15-degree lattice's first word changes with the
        /// heading it arrives on, now that only the gear must be dry): "units" where a unit alone is in the way at some heading (it can land
        /// once the unit has gone), else the worst tile word at any heading (water, rocks, blocked, map edge), else the word as before.</summary>
        bool ShuttleFairSpotFine(int cx, int cy, ZObject self, double hd0, out double hd, ref string why)
        {
            hd = hd0;
            int n = 360 / ShuttleFineStepDeg, worst = 0; bool unitsOnly = false;
            var order = new (double turn, double h)[n];
            for (int k = 0; k < n; k++)
            {
                double h = k * ShuttleFineStepDeg, turn = ZObject.ShuttleNormDeg(h - hd0);
                order[k] = (turn, h);
            }
            Array.Sort(order, (a, b) => Math.Abs(a.turn) != Math.Abs(b.turn) ? Math.Abs(a.turn).CompareTo(Math.Abs(b.turn)) : b.turn.CompareTo(a.turn));
            foreach (var (turn, h) in order)
            {
                if (ShuttlePadFreeAt(cx, cy, self, hd0 + turn, out string w))
                {
                    double r = Math.Abs(turn) % ShuttleLandTurnStepDeg;
                    if (Math.Min(r, ShuttleLandTurnStepDeg - r) >= 1e-6) ShuttleFineHeadings++;          // found off the 15-degree lattice
                    hd = hd0 + turn;
                    return true;
                }
                worst = Math.Max(worst, w == "water" ? 4 : w == "rocks" ? 3 : w == "blocked" ? 2 : w == "map edge" ? 1 : 0);
                if (w == "units" && !unitsOnly)
                {
                    bool was = shuttleSpotNoUnits; shuttleSpotNoUnits = true;
                    try { unitsOnly = ShuttlePadFreeAt(cx, cy, self, hd0 + turn, out _); }
                    finally { shuttleSpotNoUnits = was; }
                }
            }
            if (unitsOnly) why = "units";
            else if (worst > 0) why = worst == 4 ? "water" : worst == 3 ? "rocks" : worst == 2 ? "blocked" : "map edge";
            return false;
        }

        /// <summary>DV128: one part of the landing spot (ZWorld.ShuttleSpotPart, the switch on): the ship (lanePart false) or the ramp and the
        /// squad's line behind it (lanePart true) at exactly heading hdDeg. why as before: the tiles' worst (water, rocks, blocked, map edge),
        /// then units (a gun too), buildings, flags, bridges, items, plants.</summary>
        bool ShuttleFairSpotPart(int cx, int cy, ZObject self, double hdDeg, bool lanePart, out string why)
        {
            why = "";
            double hd = hdDeg * Math.PI / 180.0, nx = Math.Cos(hd), ny = Math.Sin(hd), rxv = -Math.Sin(hd), ryv = Math.Cos(hd);
            double laneZ0 = -(ShuttleBackM + ShuttleLaneBehindM), laneZ1 = -ZObject.ShuttleHullBackM, grow = ShuttleSpotClearM;
            double rampZ0 = -(ShuttleBackM + ZPathing.RobotRadius / 8.0);                              // the ramp foot's first robot spot, its body included
            void Root(double x, double y, out double rx, out double rz) { double dx = (x - cx) / 8.0, dy = (y - cy) / 8.0; rz = dx * nx + dy * ny; rx = dx * rxv + dy * ryv; }
            bool InLane(double rx, double rz) => Math.Abs(rx) <= ShuttleLaneHalfW && rz >= laneZ0 && rz <= laneZ1;
            bool Foot(double rx, double rz) => ZObject.ShuttlePlanGapRoot(rx, rz) < grow;
            bool InPart(double rx, double rz) => lanePart ? InLane(rx, rz) && !Foot(rx, rz) : Foot(rx, rz);      // the cheap box test first (the same answer)
            bool InRamp(double rx, double rz) => Math.Abs(rx) <= ShuttleRampHalfW && rz >= rampZ0 && rz <= laneZ1 && !Foot(rx, rz);
            double RampDist(double rx, double rz) { double qx = Math.Max(0.0, Math.Abs(rx) - ShuttleRampHalfW), qz = Math.Max(0.0, Math.Max(rampZ0 - rz, rz - laneZ1)); return Math.Sqrt(qx * qx + qz * qz); }
            double LaneDist(double rx, double rz) { double qx = Math.Max(0.0, Math.Abs(rx) - ShuttleLaneHalfW), qz = Math.Max(0.0, Math.Max(laneZ0 - rz, rz - laneZ1)); return Math.Sqrt(qx * qx + qz * qz); }
            double reachM = (lanePart ? Math.Sqrt(ShuttleLaneHalfW * ShuttleLaneHalfW + laneZ0 * laneZ0) : ZObject.ShuttlePlanRadiusM + grow) + 0.5;
            int bx0 = (int)Math.Floor(cx - reachM * 8), by0 = (int)Math.Floor(cy - reachM * 8), bx1 = (int)Math.Ceiling(cx + reachM * 8), by1 = (int)Math.Ceiling(cy + reachM * 8);
            // ---- the tiles. The gear's dry ground and the ramp's are tested EXACTLY per tile (the convex outline against the tile's square:
            // a pad never stands on a water tile by a sampling gap); the rest is sampled every 4 px over the part's box, as before
            int worst = 0; bool overWater = false;                                                         // 4 water, 3 rocks, 2 blocked, 1 map edge
            if (!lanePart) { if (ShuttleGearOnWater(cx, cy, nx, ny, rxv, ryv)) worst = 4; else worst = ShuttleHullOverClosed(cx, cy, nx, ny, rxv, ryv); }
            else worst = ShuttleRampGround(cx, cy, nx, ny, rxv, ryv, rampZ0, laneZ1);
            double inR2 = ShuttlePlanInM * ShuttlePlanInM, outR = ZObject.ShuttlePlanRadiusM + grow, outR2 = outR * outR;
            for (int py = by0; py <= by1 && worst < 4; py += 4)
                for (int px = bx0; px <= bx1 && worst < 4; px += 4)
                {
                    Root(px, py, out double rx, out double rz);
                    if (!lanePart)
                    {   // the same answer as Foot, cheaper: inside the outline's inscribed circle it is in, beyond its circle + the margin it is out
                        double r2 = rx * rx + rz * rz;
                        if (r2 > outR2 || (r2 >= inR2 && !Foot(rx, rz))) continue;
                    }
                    else if (!InPart(rx, rz)) continue;
                    int tx = px >> 4, ty = py >> 4;
                    if (px < 0 || py < 0 || tx >= Map.width || ty >= Map.height) { worst = Math.Max(worst, 1); continue; }
                    var c = Map.ClassAt(tx, ty);
                    bool wet = c == ZClass.Water || c == ZClass.DeepWater;
                    if (!lanePart)
                    {   // the ship: rocks and closed tiles under all of it; water only under the gear (above)
                        if (wet) overWater = true;
                        else if (c == ZClass.Impassable) worst = Math.Max(worst, 3);
                        else if (!Path.TilePassable(tx, ty, false)) worst = Math.Max(worst, 2);
                    }
                    else if (!InRamp(rx, rz))
                    {   // the squad's line: ground a robot can stand on (shallow water counts); the ramp itself above
                        if (c == ZClass.DeepWater) worst = 4;
                        else if (c == ZClass.Impassable) worst = Math.Max(worst, 3);
                        else if (!Path.TilePassable(tx, ty, true)) worst = Math.Max(worst, 2);
                    }
                }
            if (worst > 0)
            {
                if (worst == 4 && !lanePart) ShuttleGearWaterRefusals++;
                why = worst == 4 ? "water" : worst == 3 ? "rocks" : worst == 2 ? "blocked" : "map edge";
                return false;
            }
            // ---- things standing there (units on the ship's outline; a gun on the outline and on the ramp, also in the click's test; buildings
            // and map items on either part; a flag by its pole, on the outline and on the ramp)
            foreach (var o in Objects)
            {
                if (o == self || o.Destroyed || o.KillAt >= 0) continue;
                if (o.LocX > cx + reachM * 8 + 4 || o.LocX + o.WidthPix < cx - reachM * 8 - 4 || o.LocY > cy + reachM * 8 + 4 || o.LocY + o.HeightPix < cy - reachM * 8 - 4) continue;
                if (o.Kind == ZKind.Cannon && !o.ShuttleAirborne)
                {   // a gun never walks away: its ring (ZPathing.CannonRadius, 2 m: the drawn gun turning, barrels included) off the outline + the
                    // margin, as the arrival tested it before, now also in the click's test; and never under the ramp
                    Root(o.LocX + o.XOver + o.WidthPix * 0.5, o.LocY + o.YOver + o.HeightPix * 0.5, out double gx, out double gz);
                    double r = ZPathing.Radius(o) / 8.0;
                    if (lanePart ? RampDist(gx, gz) >= r : ZObject.ShuttlePlanGapRoot(gx, gz) >= grow + r) continue;
                    why = "units"; ShuttleGunRefusals++;
                    return false;
                }
                if (o.IsUnit)
                {
                    if (lanePart || o.ShuttleAirborne || shuttleSpotNoUnits) continue;                // as before: the units walk away (the click's test leaves them out)
                    Root(o.LocX + o.XOver + o.WidthPix * 0.5, o.LocY + o.YOver + o.HeightPix * 0.5, out double ux, out double uz);
                    if (ZObject.ShuttlePlanGapRoot(ux, uz) < grow + ZPathing.Radius(o) / 8.0) { why = "units"; return false; }
                    continue;
                }
                if (o.Kind != ZKind.Building && o.Kind != ZKind.MapItem) continue;
                if (o.Type == "flag")
                {   // its pole (3.4 m tall) with the margin of every tall thing, its cloth clear of the ship's upper body; robots walk past it, so the
                    // squad's line may hold it, the ramp not
                    Root(o.LocX + o.WidthPix * 0.5, o.LocY + o.HeightPix * 0.5, out double fx, out double fz);
                    if (lanePart ? RampDist(fx, fz) >= ShuttleFlagPoleM
                                 : ZObject.ShuttlePlanGapRoot(fx, fz) - ShuttleFlagPoleM >= grow && ZObject.ShuttlePolyGapRoot(ZObject.ShuttleUpperPlan, fx, fz) >= ShuttleFlagClothM) continue;
                    why = "flag";
                    return false;
                }
                bool hit = !lanePart && o.Contains(cx, cy);
                for (int yy = o.LocY; yy <= o.LocY + o.HeightPix && !hit; yy += 4)
                    for (int xx = o.LocX; xx <= o.LocX + o.WidthPix && !hit; xx += 4)
                    {
                        Root(Math.Min(xx, o.LocX + o.WidthPix - 0.01), Math.Min(yy, o.LocY + o.HeightPix - 0.01), out double qx, out double qz);
                        if (lanePart) hit = InPart(qx, qz);
                        else { double r2 = qx * qx + qz * qz; hit = r2 <= outR2 && (r2 < inR2 || Foot(qx, qz)); }   // Foot's answer, cheaper (as the tiles above)
                    }
                if (!hit) continue;
                why = o.Kind == ZKind.Building ? "building" : o.IsBridge ? "bridge" : "item";
                return false;
            }
            // ---- the plants' crowns (as before; one lower than ShuttleLowM only under the gear: it fits under the pods and the nose)
            if (shuttleKeepOut.Count > 0)
            {
                var seen = shuttleKeepOutSeen; seen.Clear();
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
                            bool clear = lanePart ? LaneDist(qx, qz) >= r
                                       : c[3] < ShuttleLowM ? ZObject.ShuttleGearGapRoot(qx, qz) - r >= ShuttleGearClearM
                                       : ZObject.ShuttlePlanGapRoot(qx, qz) - r >= grow;
                            if (clear) continue;
                            if (KeepOutFelled(c)) continue;
                            why = "trees"; ShuttleKeepOutRefusals++;
                            return false;
                        }
                    }
            }
            if (overWater) ShuttleOverhangWater++;
            return true;
        }
        /// <summary>Metres between the tile (tx, ty)'s square and the convex root-space outline p of a ship at (cx, cy) px (nx, ny: its nose;
        /// rxv, ryv: its right), 0 when they overlap: the closest pair of two separate convex shapes has a corner of one of them.</summary>
        static double ShuttleTileGap(double[] p, int tx, int ty, int cx, int cy, double nx, double ny, double rxv, double ryv)
        {
            double best = double.MaxValue;
            for (int k = 0; k < 4; k++)
            {
                double x = (tx + (k & 1)) * 16.0, y = (ty + (k >> 1)) * 16.0, dx = (x - cx) / 8.0, dy = (y - cy) / 8.0;
                double g = ZObject.ShuttlePolyGapRoot(p, dx * rxv + dy * ryv, dx * nx + dy * ny);
                if (g <= 0) return 0.0;
                best = Math.Min(best, g);
            }
            double sx0 = tx * 16.0, sy0 = ty * 16.0, sx1 = sx0 + 16.0, sy1 = sy0 + 16.0;
            for (int k = 0; k < p.Length / 2; k++)
            {
                double wx = cx + (p[2 * k + 1] * nx + p[2 * k] * rxv) * 8.0, wy = cy + (p[2 * k + 1] * ny + p[2 * k] * ryv) * 8.0;
                double ddx = Math.Max(0.0, Math.Max(sx0 - wx, wx - sx1)), ddy = Math.Max(0.0, Math.Max(sy0 - wy, wy - sy1));
                if (ddx <= 0 && ddy <= 0) return 0.0;
                best = Math.Min(best, Math.Sqrt(ddx * ddx + ddy * ddy) / 8.0);
            }
            return best;
        }

        /// <summary>The worst closed tile reaching under the ship's outline itself (exact per tile; the 0.5 m margin round it is sampled as
        /// before): 3 rocks, 2 a closed tile (water tiles are not closed here), 1 off the map, 0 none.</summary>
        int ShuttleHullOverClosed(int cx, int cy, double nx, double ny, double rxv, double ryv)
        {
            double r = ZObject.ShuttlePlanRadiusM * 8.0;
            int tx0 = (int)Math.Floor((cx - r) / 16.0), ty0 = (int)Math.Floor((cy - r) / 16.0), tx1 = (int)Math.Floor((cx + r) / 16.0), ty1 = (int)Math.Floor((cy + r) / 16.0);
            int worst = 0;
            for (int ty = ty0; ty <= ty1; ty++)
                for (int tx = tx0; tx <= tx1; tx++)
                {
                    bool off = tx < 0 || ty < 0 || tx >= Map.width || ty >= Map.height;
                    int code = 0;
                    if (off) code = 1;
                    else
                    {
                        var c = Map.ClassAt(tx, ty);
                        if (c == ZClass.Impassable) code = 3;
                        else if (c != ZClass.Water && c != ZClass.DeepWater && !Path.TilePassable(tx, ty, false)) code = 2;
                    }
                    if (code <= worst) continue;
                    if (ShuttleTileGap(ZObject.ShuttlePlan, tx, ty, cx, cy, nx, ny, rxv, ryv) > 0) continue;
                    worst = code;
                    if (worst == 3) return 3;
                }
            return worst;
        }

        /// <summary>A water tile within ShuttleGearClearM of the landing gear (exact per tile).</summary>
        bool ShuttleGearOnWater(int cx, int cy, double nx, double ny, double rxv, double ryv)
        {
            double r = (ZObject.ShuttlePlanRadiusM + ShuttleGearClearM) * 8.0;
            int tx0 = (int)Math.Floor((cx - r) / 16.0), ty0 = (int)Math.Floor((cy - r) / 16.0), tx1 = (int)Math.Floor((cx + r) / 16.0), ty1 = (int)Math.Floor((cy + r) / 16.0);
            for (int ty = Math.Max(0, ty0); ty <= Math.Min(Map.height - 1, ty1); ty++)
                for (int tx = Math.Max(0, tx0); tx <= Math.Min(Map.width - 1, tx1); tx++)
                {
                    var c = Map.ClassAt(tx, ty);
                    if (c != ZClass.Water && c != ZClass.DeepWater) continue;
                    if (ShuttleTileGap(ZObject.ShuttleGearPlan, tx, ty, cx, cy, nx, ny, rxv, ryv) < ShuttleGearClearM) return true;
                }
            return false;
        }

        /// <summary>The ramp's rectangle (ShuttleRampHalfW either side, from z0 to z1 behind the middle, root m): the worst tile under it
        /// (exact per tile): 4 water, 3 rocks, 2 closed for robots, 1 off the map, 0 dry ground a robot can stand on.</summary>
        int ShuttleRampGround(int cx, int cy, double nx, double ny, double rxv, double ryv, double z0, double z1)
        {
            var rect = new[] { -ShuttleRampHalfW, z1, -ShuttleRampHalfW, z0, ShuttleRampHalfW, z0, ShuttleRampHalfW, z1 };   // counter-clockwise (x right, z nose)
            double r = Math.Sqrt(ShuttleRampHalfW * ShuttleRampHalfW + z0 * z0) * 8.0;
            int tx0 = (int)Math.Floor((cx - r) / 16.0), ty0 = (int)Math.Floor((cy - r) / 16.0), tx1 = (int)Math.Floor((cx + r) / 16.0), ty1 = (int)Math.Floor((cy + r) / 16.0);
            int worst = 0;
            for (int ty = ty0; ty <= ty1; ty++)
                for (int tx = tx0; tx <= tx1; tx++)
                {
                    if (ShuttleTileGap(rect, tx, ty, cx, cy, nx, ny, rxv, ryv) > 0) continue;
                    if (tx < 0 || ty < 0 || tx >= Map.width || ty >= Map.height) { worst = Math.Max(worst, 1); continue; }
                    var c = Map.ClassAt(tx, ty);
                    if (c == ZClass.Water || c == ZClass.DeepWater) return 4;
                    if (c == ZClass.Impassable) worst = Math.Max(worst, 3);
                    else if (!Path.TilePassable(tx, ty, true)) worst = Math.Max(worst, 2);
                }
            return worst;
        }
    }
}
