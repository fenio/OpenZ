using System;
using System.Collections.Generic;

namespace OpenZ.Sim
{
    /// <summary>
    /// Cause: the L17 map parks the red medium tank (85,34) with its
    /// centre exactly on the red fort's exit point (Zod GetBuildingCreationMovePoint, fort_back LocY - 16); Zod has no unit collision, so
    /// there it never mattered, but with the OpenZ spacing (ZPathing) the new robots force-move into a parked vehicle they may not touch
    /// (pass-through 1 s, pushed back out, 20+ s). The L02 head start parked its mech 1 tile from the red gate, and four L17 car-park
    /// vehicles stand straight in front of their factory doors. Three parts, all deterministic (object order, no random draw):
    ///  1. parking: at world creation every vehicle that stands in a building's exit lane (the line from its creation point through its
    ///     exit point and <see cref="ExitLaneReach"/> px on, <see cref="ExitLaneHalf"/> px each side + the chassis radius) or in a tight
    ///     slot (a 1-tile corridor or a pocket between it and a wall, a building, a rock or another vehicle, where a squad gets wedged)
    ///     moves to the nearest parking spot outside every lane with <see cref="ParkRing"/> tiles of free room all round, same
    ///     robot-reachable area, off roads where possible; the OpenZ head start parks by the same rule. Vehicles stay empty / crewed exactly as before (DV32 / DV3).
    ///  2. spawns: a robot squad whose exit point is taken by a parked vehicle, a cannon or an idle vehicle steps out of the door (the exit
    ///     line, just outside the building) and walks (path finding) to the nearest free exit cell instead; a unit already outside that is
    ///     stuck on its exit move against such a blocker is sent to the nearest free cell (ZStuckGuard) instead of passing through it.
    ///  3. empty vehicles stay hard obstacles of the pathing layer (ZPathing.Static: gate spacing, detours, DV36) and every side of a
    ///     parked vehicle is reachable (part 1), so a robot ordered to board walks to the side it comes from and gets in (Zod's Enter).
    /// Switch: <see cref="ExitLanesDeviation"/> (ZGame: PlayerPrefs oz_exit_lanes, default on, off in 'Zod exact'); static default off, so
    /// the parity rows and headless tools keep Zod's map positions unless they opt in.
    /// </summary>
    public sealed partial class ZWorld
    {
        public static bool ExitLanesDeviation = false;
        /// <summary>The exit lane runs from the creation point through the exit point and this far on (px, 3 tiles).</summary>
        public const int ExitLaneReach = 48;
        /// <summary>Half width of the kept-clear lane (px, 1.5 tiles: a squad walks out three abreast), plus the parked chassis radius.</summary>
        public const int ExitLaneHalf = 24;
        /// <summary>Free tiles all round a parked start vehicle.</summary>
        public const int ParkRing = 2;
        /// <summary>A fort's gate face kept clear (review round 1): the fort's whole front width, this many tiles deep in front of its gate
        /// side; no parked vehicle within <see cref="ParkRing"/> tiles of it (not only the 3-tile exit lane).</summary>
        public const int GateFaceDepth = 5;
        /// <summary>No parked vehicle box within this many tiles of a water tile (shoreline keep-out: a hull never hangs over the bank).</summary>
        public const int ShoreRing = 1;

        /// <summary>DV66: decor the sim does not know (street lamps, signs, hydrants, bins: the view's Environment/DEC_* props), as discs
        /// (x, y, r) in Zod px, set by ZGame before ZWorld.Create for the map file <see cref="ParkingKeepOutMap"/>: a vehicle is never
        /// parked on one (a lamp post through a tank). Empty = none (headless tests unless they load a list).</summary>
        public static List<(int x, int y, int r)> ParkingKeepOut = new List<(int, int, int)>();
        public static string ParkingKeepOutMap;

        /// <summary>DV66 statistics / proof: start vehicles moved off exit lanes or out of tight slots at creation (id, from tile, to tile).</summary>
        public readonly List<(int id, string type, int fx, int fy, int tx, int ty)> ExitLaneMoves = new List<(int, string, int, int, int, int)>();
        /// <summary>DV66 statistics: new squads sent to another exit cell; stuck exit moves redirected to a free cell.</summary>
        public int ExitCellRedirects, ExitStuckRedirects, DoorSqueezes, WedgeEscapes;
        /// <summary>A taken door (review round 1): how far across the door (px from the exit line) the squad may step out instead: a fort
        /// gate opening is ~3.2 tiles wide (half ~26 px), a factory door ~1.5.</summary>
        public const int FortDoorHalf = 28, FactoryDoorHalf = 12;

        static bool ExitLine(ZObject b, out int cx, out int cy, out int ex, out int ey)
        {
            cx = cy = ex = ey = 0;
            return b.Kind == ZKind.Building && b.GetCreationPoint(out cx, out cy) && b.GetCreationMovePoint(out ex, out ey);
        }

        /// <summary>A fort's gate face (px, half-open): the fort's full front width, <see cref="GateFaceDepth"/> tiles deep on the side its
        /// exit point lies (Zod GetBuildingCreationMovePoint).</summary>
        public static bool GateFace(ZObject b, out int x0, out int y0, out int x1, out int y1)
        {
            x0 = y0 = x1 = y1 = 0;
            if (!b.IsFort || !ExitLine(b, out int cx, out int cy, out int ex, out int ey)) return false;
            int d = GateFaceDepth * 16, dx = ex - cx, dy = ey - cy;
            if (Math.Abs(dy) >= Math.Abs(dx))
            { x0 = b.LocX; x1 = b.LocX + b.WidthPix; if (dy < 0) { y1 = b.LocY; y0 = y1 - d; } else { y0 = b.LocY + b.HeightPix; y1 = y0 + d; } }
            else
            { y0 = b.LocY; y1 = b.LocY + b.HeightPix; if (dx < 0) { x1 = b.LocX; x0 = x1 - d; } else { x0 = b.LocX + b.WidthPix; x1 = x0 + d; } }
            return true;
        }

        /// <summary>A box (px, half-open) comes closer than <paramref name="margin"/> px to some fort's gate face.</summary>
        public bool NearGateFace(int bx0, int by0, int bx1, int by1, int margin)
        {
            foreach (var b in Objects)
            {
                if (b.Kind != ZKind.Building || !GateFace(b, out int x0, out int y0, out int x1, out int y1)) continue;
                if (bx0 < x1 + margin && x0 - margin < bx1 && by0 < y1 + margin && y0 - margin < by1) return true;
            }
            return false;
        }

        /// <summary>A body of radius r centred on (x, y) reaches into some building's exit lane.</summary>
        public bool InExitLane(double x, double y, double r)
        {
            foreach (var b in Objects)
            {
                if (!ExitLine(b, out int cx, out int cy, out int ex, out int ey)) continue;
                double dx = ex - cx, dy = ey - cy, l = Math.Sqrt(dx * dx + dy * dy); if (l < 1e-6) continue;
                double ux = dx / l, uy = dy / l, len = l + ExitLaneReach;
                double t = Math.Max(0, Math.Min(len, (x - cx) * ux + (y - cy) * uy));
                double qx = cx + ux * t - x, qy = cy + uy * t - y, lim = ExitLaneHalf + r;
                if (qx * qx + qy * qy < lim * lim) return true;
            }
            return false;
        }

        /// <summary>A 2x2 tile parking box at (tx, ty) (start parking only: world creation and the head start): vehicle-passable dry tiles,
        /// no water within <see cref="ShoreRing"/> tile (off the shoreline), <see cref="ParkRing"/> tiles of robot-passable room all round
        /// with no building, unit (the map's start robots included) or map item in it, outside
        /// every exit lane and at least <see cref="ParkRing"/> tiles from every fort's gate face.</summary>
        public bool ParkingFree(int tx, int ty, ZObject self, string type)
        {
            int r = ParkRing;
            if (tx - r < 1 || ty - r < 1 || tx + 2 + r >= Map.width || ty + 2 + r >= Map.height) return false;
            for (int j = ty - r; j < ty + 2 + r; j++)
                for (int i = tx - r; i < tx + 2 + r; i++)
                {
                    bool box = i >= tx && i < tx + 2 && j >= ty && j < ty + 2;
                    bool shore = i >= tx - ShoreRing && i < tx + 2 + ShoreRing && j >= ty - ShoreRing && j < ty + 2 + ShoreRing;
                    var c = Map.ClassAt(i, j);
                    if (box ? (!Path.TilePassable(i, j, false) || c == ZClass.Water || c == ZClass.DeepWater) : (!Path.TilePassable(i, j, true) || c == ZClass.DeepWater || (shore && c == ZClass.Water))) return false;
                }
            if (NearGateFace(tx * 16, ty * 16, tx * 16 + 32, ty * 16 + 32, ParkRing * 16)) return false;
            int x0 = (tx - r) * 16, y0 = (ty - r) * 16, x1 = (tx + 2 + r) * 16, y1 = (ty + 2 + r) * 16;
            foreach (var o in Objects)
            {
                if (o == self || (o.Destroyed && o.Kind != ZKind.Building)) continue;   // vehicles, cannons, buildings, items; review round 1: also the map's start robots (they stand there at 0:00, never a squad pressed against a parked hull)
                if (o.LocX < x1 && x0 < o.LocX + o.WidthPix && o.LocY < y1 && y0 < o.LocY + o.HeightPix) return false;
            }
            if (ParkingKeepOut.Count > 0 && ParkingKeepOutMap == Map.file)
            {
                int bx0 = tx * 16 - 8, by0 = ty * 16 - 8, bx1 = tx * 16 + 40, by1 = ty * 16 + 40;     // the box + half a tile
                foreach (var k in ParkingKeepOut)
                {
                    int qx = Math.Max(bx0, Math.Min(bx1, k.x)), qy = Math.Max(by0, Math.Min(by1, k.y));
                    if ((qx - k.x) * (qx - k.x) + (qy - k.y) * (qy - k.y) <= k.r * k.r) return false;
                }
            }
            return !InExitLane(tx * 16 + 16, ty * 16 + 16, ZPathing.VehicleRadiusOf(type));
        }

        /// <summary>ZWorld.Create hook (after the map objects and the harbour wrecks): park every vehicle that blocks an exit lane or
        /// stands in a tight slot at the nearest free spot (same robot-reachable area, off roads where possible).</summary>
        void ClearExitLanes()
        {
            ExitLaneMoves.Clear();
            if (!ExitLanesDeviation) return;
            foreach (var v in Objects)
            {
                if (v.Kind != ZKind.Vehicle || v.Destroyed) continue;
                int ox = v.LocX >> 4, oy = v.LocY >> 4;
                if (!InExitLane(v.CenterX, v.CenterY, ZPathing.VehicleRadiusOf(v.Type)) && !TightSlot(v) &&
                    !NearGateFace(v.LocX, v.LocY, v.LocX + v.WidthPix, v.LocY + v.HeightPix, ParkRing * 16)) continue;
                if (!NearestParking(ox, oy, v, v.Type, 12, out int nx, out int ny)) continue;   // nothing free nearby: stays (logged by the probe)
                v.SetCords(nx * 16, ny * 16);
                v.ZoneIndex = GetZoneIndex(v.LocX, v.LocY);
                v.InitRealMoveSpeed(Map);
                ExitLaneMoves.Add((v.Id, v.Type, ox, oy, nx, ny));
            }
        }

        /// <summary>A parked vehicle leaves a slot where robots get wedged: on some side the first tile row next to it is partly blocked
        /// (a pocket), or free while the second row is blocked (a 1-tile corridor along a wall, a rock, a building or another vehicle).
        /// Flush against a wall (the whole first row blocked) or 2 free rows is fine.</summary>
        bool TightSlot(ZObject v)
        {
            int tx = v.LocX >> 4, ty = v.LocY >> 4;
            bool Blocked(int i, int j)
            {
                if (!Path.TilePassable(i, j, true) || Map.ClassAt(i, j) == ZClass.DeepWater) return true;
                int x = i * 16, y = j * 16;
                foreach (var o in Objects)
                    if (o != v && !o.Destroyed && o.Type != "flag" && (o.Kind == ZKind.Building ? !o.IsBridge : true) && o.KillAt < 0 &&
                        x < o.LocX + o.WidthPix && o.LocX < x + 16 && y < o.LocY + o.HeightPix && o.LocY < y + 16 && o.Kind != ZKind.Robot) return true;
                return false;
            }
            for (int side = 0; side < 4; side++)
            {
                int b1 = 0, b2 = 0;
                for (int k = 0; k < 2; k++)
                {
                    int i1, j1, i2, j2;
                    switch (side)
                    {
                        case 0: i1 = tx + k; j1 = ty - 1; i2 = tx + k; j2 = ty - 2; break;          // north
                        case 1: i1 = tx + 2; j1 = ty + k; i2 = tx + 3; j2 = ty + k; break;          // east
                        case 2: i1 = tx + k; j1 = ty + 2; i2 = tx + k; j2 = ty + 3; break;          // south
                        default: i1 = tx - 1; j1 = ty + k; i2 = tx - 2; j2 = ty + k; break;         // west
                    }
                    if (Blocked(i1, j1)) b1++;
                    if (Blocked(i2, j2)) b2++;
                }
                if (b1 == 1 || (b1 == 0 && b2 > 0)) return true;
            }
            return false;
        }

        /// <summary>Nearest free parking box to (ox, oy) within maxR tiles: cost = |dx| + 1.25 |dy| (+3 on a road tile, keeping roads
        /// open), same robot-reachable area as the original spot; deterministic order.</summary>
        public bool NearestParking(int ox, int oy, ZObject self, string type, int maxR, out int nx, out int ny)
        {
            nx = ny = 0;
            int c0 = Pathing != null ? Pathing.Reach.Comp(ox, oy, true, false) : -1;
            double best = double.MaxValue;
            for (int dy = -maxR; dy <= maxR; dy++)
                for (int dx = -maxR; dx <= maxR; dx++)
                {
                    int tx = ox + dx, ty = oy + dy;
                    double cost = Math.Abs(dx) + 1.25 * Math.Abs(dy) + (ty * Map.width + tx) * 1e-7;
                    for (int j = 0; j < 2; j++) for (int i = 0; i < 2; i++) if (Map.ClassAt(tx + i, ty + j) == ZClass.Road) { cost += 3; j = 2; break; }
                    if (cost >= best || !ParkingFree(tx, ty, self, type)) continue;
                    if (c0 >= 0 && Pathing.Reach.Comp(tx, ty, true, false) != c0) continue;
                    best = cost; nx = tx; ny = ty;
                }
            return best < double.MaxValue;
        }

        /// <summary>Something that does not make way stands on (x, y) for unit u: a parked (neutral) vehicle, a cannon, an idle vehicle,
        /// or a stunned unit, closer than the pathing spacing (+4 px).</summary>
        public bool ExitBlocked(ZObject u, int x, int y)
        {
            int ru = ZPathing.Radius(u);
            foreach (var o in Objects)
            {
                if (o == u || o.Destroyed || o.KillAt >= 0 || !o.IsUnit || o.Leader == u || (u.Leader != null && (o == u.Leader || o.Leader == u.Leader))) continue;
                if (!(ZPathing.Static(o) || (o.Kind == ZKind.Vehicle && o.Waypoints.Count == 0))) continue;
                double lim = ru + ZPathing.Radius(o) + 4, dx = o.CenterX - x, dy = o.CenterY - y;
                if (dx * dx + dy * dy < lim * lim) return true;
            }
            return false;
        }

        /// <summary>A unit standing at (x, y) would be inside the gate spacing (the separation's own limit) of a unit that does not make
        /// way (<see cref="ExitBlocked"/>'s set).</summary>
        bool DoorTaken(ZObject u, int x, int y)
        {
            foreach (var o in Objects)
            {
                if (!NoWayMaker(u, o)) continue;
                double lim = ZPathing.MinSpacing(u, o) + 1, dx = o.CenterX - x, dy = o.CenterY - y;
                if (dx * dx + dy * dy < lim * lim) return true;
            }
            return false;
        }

        /// <summary>The straight walk (x0, y0) -> (x1, y1) comes inside the gate spacing of a unit that does not make way.</summary>
        bool LineTaken(ZObject u, int x0, int y0, int x1, int y1)
        {
            double dx = x1 - x0, dy = y1 - y0, len = Math.Sqrt(dx * dx + dy * dy); int steps = (int)(len / 3) + 1;
            for (int i = 1; i <= steps; i++) { double f = (double)i / steps; if (DoorTaken(u, (int)Math.Round(x0 + dx * f), (int)Math.Round(y0 + dy * f))) return true; }
            return false;
        }

        bool NoWayMaker(ZObject u, ZObject o) =>
            o != u && !o.Destroyed && o.KillAt < 0 && o.IsUnit && o.Leader != u && !(u.Leader != null && (o == u.Leader || o.Leader == u.Leader)) &&
            (ZPathing.Static(o) || (o.Kind == ZKind.Vehicle && o.Waypoints.Count == 0));

        /// <summary>Nearest free cell around unit u (half-tile steps, up to maxTiles): passable, not in a building, outside the spacing of
        /// every unit that does not make way, not taken by another squad (review round 1: the way out of a wedge).</summary>
        public bool NearestFreeCell(ZObject u, int maxTiles, out int fx, out int fy)
        {
            fx = u.CenterX; fy = u.CenterY; double best = double.MaxValue;
            bool robot = u.IsRobot; int n = maxTiles * 2;
            int comp = Pathing != null ? Pathing.Reach.Comp(u.CenterX >> 4, u.CenterY >> 4, robot, false) : -1;
            for (int dy = -n; dy <= n; dy++)
                for (int dx = -n; dx <= n; dx++)
                {
                    double cost = dx * dx + dy * dy + (dx < 0 ? 0.01 : 0) + (dy < 0 ? 0.001 : 0);
                    if (cost < 1.5 || cost >= best) continue;
                    int px = u.CenterX + dx * 8, py = u.CenterY + dy * 8, tx = px >> 4, ty = py >> 4;
                    if (!Path.TilePassable(tx, ty, robot) || Map.ClassAt(tx, ty) == ZClass.DeepWater || (!robot && Map.ClassAt(tx, ty) == ZClass.Water)) continue;
                    if (ExitBlocked(u, px, py) || InBuilding(px, py, ZPathing.Radius(u) + 2) || CellTaken(u, px, py)) continue;
                    if (comp >= 0 && Pathing.Reach.Comp(tx, ty, robot, false) != comp) continue;
                    best = cost; fx = px; fy = py;
                }
            return best < double.MaxValue;
        }

        /// <summary>Another robot (not u's squad) stands within 14 px of (x, y), or is walking to a point within 14 px of it.</summary>
        bool CellTaken(ZObject u, int x, int y)
        {
            foreach (var o in Objects)
            {
                if (o.Kind != ZKind.Robot || o == u || o.Destroyed || o.KillAt >= 0 || o.Leader == u || (u.Leader != null && (o == u.Leader || o.Leader == u.Leader))) continue;
                double dx = o.CenterX - x, dy = o.CenterY - y;
                if (dx * dx + dy * dy < 14 * 14) return true;
                foreach (var w in o.Waypoints)
                    if (w.mode == WpMode.Move && (w.x - x) * (w.x - x) + (w.y - y) * (w.y - y) < 14 * 14) return true;
            }
            return false;
        }

        /// <summary>(x, y) lies inside a building's box (bridges excluded) or within r px of it.</summary>
        bool InBuilding(int x, int y, int r)
        {
            foreach (var b in Objects)
                if (b.Kind == ZKind.Building && !b.IsBridge && !b.Destroyed && x > b.LocX - r && x < b.LocX + b.WidthPix + r && y > b.LocY - r && y < b.LocY + b.HeightPix + r) return true;
            return false;
        }

        /// <summary>Nearest free cell to the exit point (ex, ey) of the line (cx, cy) -> (ex, ey): outward and sideways in 1-tile steps
        /// up to 4 tiles, a passable tile for u, not blocked (<see cref="ExitBlocked"/>), in u's reachable area.</summary>
        public bool FreeExitCell(ZObject u, int cx, int cy, int ex, int ey, out int fx, out int fy, bool straight = false)
        {
            fx = ex; fy = ey;
            double dx = ex - cx, dy = ey - cy, l = Math.Sqrt(dx * dx + dy * dy);
            double ux = l < 1e-6 ? 0 : dx / l, uy = l < 1e-6 ? 1 : dy / l, nxv = -uy, nyv = ux;
            bool robot = u.IsRobot;
            int comp = Pathing != null ? Pathing.Reach.Comp(ex >> 4, ey >> 4, robot, false) : -1;
            double best = double.MaxValue;
            for (int s = 0; s <= 4; s++)
                for (int k = -4; k <= 4; k++)
                {
                    int px = (int)Math.Round(ex + ux * s * 16 + nxv * k * 16), py = (int)Math.Round(ey + uy * s * 16 + nyv * k * 16);
                    double cost = s * s + k * k + (k < 0 ? 0.01 : 0);
                    if (cost >= best) continue;
                    if (CellTaken(u, px, py)) cost += 100;                                   // review round 1: the NEXT free cell, not the one the last squad stands on / walks to
                    if (cost >= best) continue;
                    int tx = px >> 4, ty = py >> 4;
                    if (!Path.TilePassable(tx, ty, robot) || Map.ClassAt(tx, ty) == ZClass.DeepWater || (!robot && Map.ClassAt(tx, ty) == ZClass.Water)) continue;
                    if (ExitBlocked(u, px, py) || InBuilding(px, py, ZPathing.Radius(u) + 2)) continue;   // review round 1: never a cell inside the fort / factory box
                    if (straight && LineTaken(u, u.CenterX, u.CenterY, px, py)) continue;          // a FORCE_MOVE goes there in a straight line: not past a hull
                    if (comp >= 0 && Pathing.Reach.Comp(tx, ty, robot, false) != comp) continue;
                    best = cost; fx = px; fy = py;
                }
            return best < double.MaxValue;
        }

        /// <summary>DV66: a new vehicle's exit move ends with its whole box this far (px) clear of the building's box. Zod: the vehicle
        /// factory is 4 x 5 tiles (bvehicle.cpp:17-18) and its exit point lies 96 px down, 16 px below the box, so a 32 px vehicle ends
        /// the FORCE_MOVE (zserver.cpp:1614-1621) with its tail flush in the door and turns there.</summary>
        public const int VehicleExitClearPx = 12;
        /// <summary>DV66 statistics: vehicle exit points moved out along the exit line.</summary>
        public int VehicleExitExtends;

        /// <summary>How far (px) the drawn hull of a vehicle reaches behind its centre (the sim box is 32 px for every vehicle; the
        /// models are 3.9 to 5.2 m long).</summary>
        static int HullRearPx(ZObject u)
        {
            switch (u.Type)
            {
                case "jeep": return 23;
                case "heavy": return 21;
                case "apc": case "missile_launcher": case "crane": return 20;
                case "medium": return 19;
                case "light": return 18;
            }
            return u.HeightPix >> 1;
        }

        /// <summary>Moves a new vehicle's exit point (ex, ey) out along the exit line until its box clears b's box by
        /// <see cref="VehicleExitClearPx"/> (at most 3 tiles): only onto tiles a vehicle may stand on, clear of other buildings and of
        /// units that do not make way; otherwise Zod's point stays.</summary>
        void VehicleExitPoint(ZObject b, ZObject u, int cx, int cy, ref int ex, ref int ey)
        {
            double dx = ex - cx, dy = ey - cy, l = Math.Sqrt(dx * dx + dy * dy); if (l < 1e-6) return;
            double ux = dx / l, uy = dy / l;
            int hw = (u.WidthPix >> 1) + VehicleExitClearPx, hh = Math.Max(u.HeightPix >> 1, HullRearPx(u)) + VehicleExitClearPx;
            for (int s = 0; s <= 48; s += 2)
            {
                int px = (int)Math.Round(ex + ux * s), py = (int)Math.Round(ey + uy * s);
                if (px - hw < b.LocX + b.WidthPix && b.LocX < px + hw && py - hh < b.LocY + b.HeightPix && b.LocY < py + hh) continue;   // not clear yet
                if (s == 0) return;                                                           // Zod's point is clear already
                int bw = (u.WidthPix >> 1) - 1, bh = (u.HeightPix >> 1) - 1;
                for (int j = -1; j <= 1; j += 2)
                    for (int i = -1; i <= 1; i += 2)
                    {
                        int tx = (px + i * bw) >> 4, ty = (py + j * bh) >> 4; var c = Map.ClassAt(tx, ty);
                        if (!Path.TilePassable(tx, ty, false) || c == ZClass.Water || c == ZClass.DeepWater) return;
                    }
                if (ExitBlocked(u, px, py) || InBuilding(px, py, u.HeightPix >> 1)) return;
                ex = px; ey = py; VehicleExitExtends++;
                return;
            }
        }

        /// <summary>BuildingCreateUnit hook: the new robot squad's exit. Zod: one FORCE_MOVE (a straight line) to the exit point. DV66:
        /// when a parked vehicle / cannon / idle vehicle takes the exit point, the squad steps out of the door (on the exit line, just
        /// outside the building) and walks to the nearest free exit cell.</summary>
        void AddExitWaypoints(ZObject b, ZObject u, int cx, int cy, int ex, int ey)
        {
            if (ExitLanesDeviation && u.Kind == ZKind.Vehicle) VehicleExitPoint(b, u, cx, cy, ref ex, ref ey);   // the whole hull out of the door before any turn
            if (!ExitLanesDeviation || !u.IsRobot || !ExitBlocked(u, ex, ey))
            { u.Waypoints.Add(new ZWaypoint { mode = WpMode.ForceMove, refId = -1, x = ex, y = ey }); return; }
            // the door threshold: the exit line 6 px outside the building box
            double dx = ex - cx, dy = ey - cy, l = Math.Sqrt(dx * dx + dy * dy); double ux = l < 1e-6 ? 0 : dx / l, uy = l < 1e-6 ? 1 : dy / l;
            int thx = ex, thy = ey;
            for (int step = 0; step <= 64; step++)
            {
                int px = (int)Math.Round(cx + ux * step * 2), py = (int)Math.Round(cy + uy * step * 2);
                bool inside = px >= b.LocX && px < b.LocX + b.WidthPix && py >= b.LocY && py < b.LocY + b.HeightPix;
                if (!inside) { thx = (int)Math.Round(px + ux * 6); thy = (int)Math.Round(py + uy * 6); break; }
            }
            if (!FreeExitCell(u, cx, cy, ex, ey, out int fx, out int fy))
            { u.Waypoints.Add(new ZWaypoint { mode = WpMode.ForceMove, refId = -1, x = ex, y = ey }); return; }   // no free cell at all: Zod's exit (the guard's fallback)
            if (DoorTaken(u, thx, thy))
            {   // review round 1: the door point itself is taken (a hull left on the gate, flush with the wall): the next free spot across
                // the door's width (a fort gate is ~3 tiles wide, a hull 2), on the free cell's side first; the squad walks out there in
                // a straight line through the opening (Zod's FORCE_MOVE), then to the free exit cell (path finding)
                double nxv = -uy, nyv = ux; int sgn = (fx - thx) * nxv + (fy - thy) * nyv >= 0 ? 1 : -1;
                int half = b.IsFort ? FortDoorHalf : FactoryDoorHalf; bool ok = false; int dxo = thx, dyo = thy;
                for (int k = 4; k <= half && !ok; k += 2)
                    foreach (int sd in new[] { sgn, -sgn })
                    {
                        dxo = (int)Math.Round(thx + nxv * sd * k); dyo = (int)Math.Round(thy + nyv * sd * k);
                        if (Path.TilePassable(dxo >> 4, dyo >> 4, true) && !DoorTaken(u, dxo, dyo) && !LineTaken(u, cx, cy, dxo, dyo)) { ok = true; break; }
                    }
                if (!ok) { u.Waypoints.Add(new ZWaypoint { mode = WpMode.ForceMove, refId = -1, x = ex, y = ey }); return; }   // the whole door is taken: Zod's exit (the guard's fallback)
                FreeExitCell(u, cx + (dxo - thx), cy + (dyo - thy), ex + (dxo - thx), ey + (dyo - thy), out fx, out fy);   // the free cell on that side
                u.Waypoints.Add(new ZWaypoint { mode = WpMode.ForceMove, refId = -1, x = dxo, y = dyo });
                u.Waypoints.Add(ZWaypoint.Move(fx, fy));
                ExitCellRedirects++; DoorSqueezes++;
                return;
            }
            u.Waypoints.Add(new ZWaypoint { mode = WpMode.ForceMove, refId = -1, x = thx, y = thy });
            u.Waypoints.Add(ZWaypoint.Move(fx, fy));
            ExitCellRedirects++;
        }
    }
}
