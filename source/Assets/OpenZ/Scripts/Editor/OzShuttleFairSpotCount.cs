using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// K437, the shuttle lands where it really fits (rule DV128 on top of DV118, Sim/ZWorld.ShuttleFairSpot.cs), counted headless (ZWorld.Create +
    /// Tick, no scene, nothing saved; public members only, like every check of the editor assembly). Menu OpenZ/V1.1/Shuttle: K437 Fair Landing
    /// Spot Check. Out of Play the level's plants are not known (the view hands them in during Play): the headless proof of the kit also runs it
    /// with level 16's plant list.
    ///  A. LEVEL 16's GATLING ISLAND (the gun on tiles 29-30 x 63-64, the zone flag on tile 28,66): the click takes the island's tile 29,68
    ///     (before: no island tile); the heading the landing finds from 8 arrival headings fits every time (the finer lattice), and there the
    ///     gear + 0.1 m lies on dry tiles, the whole outline + 0.5 m clear of the gun's ring (2 m), the flag pole 0.5 m clear of the outline, the ramp
    ///     on dry ground (measured anew here, every 0.125 m). The gun's tiles, the flag's tile and the water round the island stay refused.
    ///  B. A FLIGHT TO THE ISLAND: level 16 without its walking units (the guns kept), the own ship with a squad of 4 aboard, ordered through
    ///     the click's filter (LANDABLE SPOTS ONLY) to tile 29,68: the order is kept, it lands there (no hover), clear of the gun; EXIT: the
    ///     squad stands on ground a robot can stand on, nobody inside the ship; one robot then takes the island's flag beside the ship.
    ///  C. THE PROMISES ON THREE LEVELS (every third tile each way of level 16, 2 and 17): wherever the click takes the order, the heading the
    ///     landing finds there keeps the rule's promises (measured anew): the gear dry, the outline + 0.5 m off rocks, closed tiles, the map's
    ///     edge, buildings, items, guns and flag poles, the ramp dry, the squad's line on ground a robot can stand on. 0 broken.
    ///  D. BEFORE AND AFTER on the same tiles (the switch off = the rule as it was): more places taken; a place taken before and refused now
    ///     only where the old rule could not land either (it hovered: the click counts guns now) or landed overlapping something (measured).
    ///  E. THE CLICK AGREES WITH THE FLIGHT: up to 6 places that only the new rule takes (level 16 without walking units): each order kept,
    ///     each flight lands (no hover).
    ///  F. DETERMINISM: C's answers twice, identical; the switch off leaves the new rule's counters untouched.
    /// </summary>
    public static class OzShuttleFairSpotCount
    {
        const double Tick = ZConst.TICK;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static string F(double v, string f = "0.00") => v.ToString(f, Inv);
        static double Norm(double a) { a %= 360.0; if (a > 180.0) a -= 360.0; if (a <= -180.0) a += 360.0; return a; }
        static int H(string s) { int h = 7; foreach (char c in s ?? "") h = h * 31 + c; return h & 0xffff; }
        /// <summary>Level 16's gatling island: the gun's tile, its flag's tile, the tile the ship can land on.</summary>
        public const int GunTx = 29, GunTy = 63, FlagTx = 28, FlagTy = 66, IslandTx = 29, IslandTy = 68;
        /// <summary>C and D test every Step-th tile each way (the Deviations row: every RowStep-th).</summary>
        public const int Step = 3, RowStep = 6;

        /// <summary>A level map without its walking units (robots, vehicles) and with nobody owning the blue objects, its guns kept (made
        /// neutral: nobody mans them): with DV128 on the click's test counts a gun (it never walks away). The switch off: OzV11Kit.NoUnits.</summary>
        public static string NoWalkers(string mapJson)
        {
            if (!ZWorld.ShuttleFairSpotOn) return OzV11Kit.NoUnits(mapJson);
            var m = ZMapData.FromJson(mapJson);
            m.objects.RemoveAll(o => { string c = o.kind.Split(':')[0]; return c == "robot" || c == "vehicle"; });
            foreach (var o in m.objects) if (o.owner == "blue" || (o.kind.StartsWith("cannon") && o.owner != "null")) o.owner = "null";
            return UnityEngine.JsonUtility.ToJson(m);
        }

        static void Run(ZWorld w, double sec, Func<bool> until = null)
        {
            double end = w.Time + sec;
            while (w.Time < end && !(until != null && until())) { w.Tick(Tick); w.Events.Clear(); }
        }

        static ZWorld World(string settings, string map, int seed, IList<double> plants, out ZObject s)
        {
            var w = OzV11Kit.World(settings, map, seed);
            if (plants != null) w.ShuttleSetKeepOut(plants, 1);
            s = w.PlayableShuttle ?? w.CreatePlayableShuttle(3 * 16 + 8, 3 * 16 + 8, Team.Red, -90f);
            return w;
        }

        // ------------------------------------------------------------------ the promises, measured anew (not the rule's own code)
        static double PolyGap(double[] p, double rx, double rz)
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

        /// <summary>The ship's outline (hull + pods + legs) and its landing gear (the pad soles' outline), root metres x right, z nose: the
        /// model's numbers, written here again so the check does not read the rule's own tables.</summary>
        static readonly double[] Plan = { -3.194, -0.057, -3.182, -0.499, -3.156, -0.724, -3.138, -0.873, -3.000, -1.228, -2.332, -2.822, -2.328, -2.830, -2.237, -2.999, -2.119, -3.169, -1.998, -3.329, 2.003, -3.333, 2.190, -3.073, 2.267, -2.955, 2.353, -2.817, 3.003, -1.225, 3.143, -0.842, 3.151, -0.822, 3.174, -0.620, 3.194, -0.442, 3.194, -0.041, 3.188, 0.182, 3.066, 0.902, 3.050, 0.930, 2.446, 1.587, 1.024, 2.892, 0.945, 2.963, 0.763, 3.114, 0.517, 3.243, 0.326, 3.305, 0.133, 3.333, -0.190, 3.323, -0.358, 3.294, -0.531, 3.237, -0.771, 3.106, -1.057, 2.856, -2.441, 1.584, -3.054, 0.911, -3.073, 0.839, -3.153, 0.351, -3.184, 0.162, -3.191, 0.078 };
        static readonly double[] Gear = { -2.754, 1.055, -2.332, -2.822, -2.328, -2.830, -2.237, -2.999, -2.119, -3.169, -1.998, -3.329, 2.003, -3.333, 2.190, -3.073, 2.267, -2.955, 2.353, -2.817, 2.752, 1.087, 2.446, 1.587, -2.162, 1.593, -2.441, 1.584, -2.567, 1.386, -2.751, 1.077 };

        static readonly double[] Upper = { -3.194, -0.057, -3.182, -0.499, -3.156, -0.724, -3.138, -0.873, -3.000, -1.228, -1.301, -2.718, -1.194, -2.809, -1.101, -2.880, -0.895, -3.031, -0.412, -3.240, 0.017, -3.261, 0.454, -3.226, 0.906, -3.005, 1.165, -2.839, 1.258, -2.765, 3.003, -1.225, 3.151, -0.822, 3.174, -0.620, 3.194, -0.441, 3.194, -0.041, 3.188, 0.182, 3.066, 0.902, 3.050, 0.930, 1.025, 2.892, 0.945, 2.963, 0.763, 3.114, 0.517, 3.243, 0.326, 3.305, 0.133, 3.333, -0.190, 3.323, -0.358, 3.294, -0.531, 3.237, -0.771, 3.106, -1.057, 2.856, -3.054, 0.911, -3.073, 0.839, -3.184, 0.162, -3.191, 0.078 };

        public sealed class Promise { public bool ok = true; public string broken = ""; public double water = 99, gun = 99, flag = 99, cloth = 99; }

        /// <summary>Ship middle (cx, cy) px on heading hd: measured every 0.125 m in root space. The units are left out (the click's test).</summary>
        static Promise Measure(ZWorld w, ZObject self, int cx, int cy, double hd)
        {
            var p = new Promise();
            double h = hd * Math.PI / 180.0, nx = Math.Cos(h), ny = Math.Sin(h), rxv = -Math.Sin(h), ryv = Math.Cos(h);
            // the rule samples rocks, closed tiles and the squad's line every 4 px (as DV118 did): those are measured on the outline itself and on
            // the line's middle 2 m (its robots stand on the axis); the gear and the ramp the rule tests exactly per tile: measured with their margin
            double grow = 0.5, gearClear = ZWorld.ShuttleGearClearM; const double laneHalf = 1.0, rampHalf = 1.0, hullBack = 3.333, rampEnd = 4.6 + 0.75, laneEnd = 4.6 + 6.0 - 0.36;
            void Fail(string what) { if (p.ok) p.broken = what; p.ok = false; }
            (int tx, int ty, double px, double py) At(double rx, double rz) { double px = cx + (rz * nx + rx * rxv) * 8.0, py = cy + (rz * ny + rx * ryv) * 8.0; return ((int)Math.Floor(px) >> 4, (int)Math.Floor(py) >> 4, px, py); }
            for (double rz = -laneEnd; rz <= 4.0 + 1e-9; rz += 0.125)
                for (double rx = -4.0; rx <= 4.0 + 1e-9; rx += 0.125)
                {
                    double gp = PolyGap(Plan, rx, rz), gg = PolyGap(Gear, rx, rz);
                    bool ship = gp < grow, gear = gg < gearClear, hull = gp < 0;
                    bool lane = !ship && Math.Abs(rx) <= laneHalf && rz <= -hullBack && rz >= -laneEnd, ramp = lane && Math.Abs(rx) <= rampHalf && rz >= -rampEnd;
                    if (!ship && !lane) continue;
                    var (tx, ty, px, py) = At(rx, rz);
                    if (px < 0 || py < 0 || tx >= w.Map.width || ty >= w.Map.height) { if (hull || ramp) Fail("off the map"); continue; }
                    var c = w.Map.ClassAt(tx, ty); bool wet = c == ZClass.Water || c == ZClass.DeepWater;
                    if (wet && ship) p.water = Math.Min(p.water, gg);
                    if (gear && wet) Fail($"water under the gear at {F(px / 8, "0.0")},{F(py / 8, "0.0")}");
                    if (hull && (c == ZClass.Impassable || (!wet && !w.Path.TilePassable(tx, ty, false)))) Fail($"rocks / a closed tile under the ship at {F(px / 8, "0.0")},{F(py / 8, "0.0")}");
                    if (ramp && (wet || c == ZClass.Impassable || !w.Path.TilePassable(tx, ty, true))) Fail($"the ramp not on dry ground at {F(px / 8, "0.0")},{F(py / 8, "0.0")}");
                    if (lane && !ramp && (c == ZClass.DeepWater || c == ZClass.Impassable || !w.Path.TilePassable(tx, ty, true))) Fail($"the squad's line not on ground a robot can stand on at {F(px / 8, "0.0")},{F(py / 8, "0.0")}");
                }
            foreach (var o in w.Objects)
            {
                if (o == self || o.Destroyed || o.KillAt >= 0) continue;
                double dx0 = o.LocX + o.WidthPix * 0.5 - cx, dy0 = o.LocY + o.HeightPix * 0.5 - cy;
                if (dx0 * dx0 + dy0 * dy0 > 15.0 * 15.0 * 64.0) continue;
                (double rx, double rz) Root(double x, double y) { double dx = (x - cx) / 8.0, dy = (y - cy) / 8.0; return (dx * rxv + dy * ryv, dx * nx + dy * ny); }
                if (o.Kind == ZKind.MapItem && o.Type == "flag")
                {
                    var (fx, fz) = Root(o.LocX + o.WidthPix * 0.5, o.LocY + o.HeightPix * 0.5);
                    double g = PolyGap(Plan, fx, fz) - 0.05, gc = PolyGap(Upper, fx, fz); p.flag = Math.Min(p.flag, g); p.cloth = Math.Min(p.cloth, gc);
                    if (g < grow) Fail($"a flag pole {F(g)} m from the ship");
                    if (gc < 1.45) Fail($"a flag's cloth could reach the ship's upper body ({F(gc)} m from the pole)");
                    if (Math.Abs(fx) <= rampHalf + 0.05 && fz <= -hullBack + 0.05 && fz >= -rampEnd - 0.05 && PolyGap(Plan, fx, fz) >= grow) Fail("a flag pole on the ramp");
                    continue;
                }
                if (o.Kind == ZKind.Cannon)
                {   // its ring: the drawn gun turning (2 m, barrels included)
                    var (gx, gz) = Root(o.LocX + o.WidthPix * 0.5, o.LocY + o.HeightPix * 0.5);
                    double gg = PolyGap(Plan, gx, gz) - 2.0; p.gun = Math.Min(p.gun, gg);
                    if (gg < grow) Fail($"a gun's ring {F(gg)} m from the ship");
                    double qx0 = Math.Max(0.0, Math.Abs(gx) - rampHalf), qz0 = Math.Max(0.0, Math.Max(-rampEnd - gz, gz + hullBack));
                    if (Math.Sqrt(qx0 * qx0 + qz0 * qz0) < 2.0) Fail("a gun's ring over the ramp");
                    continue;
                }
                if (o.Kind != ZKind.Building && o.Kind != ZKind.MapItem) continue;
                double best = 99;
                for (double yy = o.LocY; yy <= o.LocY + o.HeightPix + 1e-9; yy += 2)
                    for (double xx = o.LocX; xx <= o.LocX + o.WidthPix + 1e-9; xx += 2)
                    {
                        var (qx, qz) = Root(Math.Min(xx, o.LocX + o.WidthPix - 0.01), Math.Min(yy, o.LocY + o.HeightPix - 0.01));
                        best = Math.Min(best, PolyGap(Plan, qx, qz));
                    }
                if (best < grow - 0.36) Fail($"{(o.Kind == ZKind.Building ? "a building" : "a map item")} ({o.Type}) {F(best)} m from the ship");   // 0.36 m: the rule samples a box every 4 px
            }
            return p;
        }

        /// <summary>The rule's heading for a place the click took: the landing's lattices from an arrival heading of hd0 (the units left out
        /// is not possible from outside: places with a unit near are skipped by the caller).</summary>
        static bool Heading(ZWorld w, ZObject s, int x, int y, double hd0, out double hd) => w.ShuttleLandSpot(x, y, s, hd0, out hd, out _);

        /// <summary>A place 9 to 16 m from the ship where a squad can stand (the 3 x 3 tiles round it open for robots, dry), the nearest first.</summary>
        static (int x, int y) RobotSpot(ZWorld w, ZObject s)
        {
            for (int r = 72; r <= 128; r += 8)
                for (int a = 0; a < 32; a++)
                {
                    double g = a * Math.PI / 16; int x = s.CenterX + (int)(Math.Cos(g) * r), y = s.CenterY + (int)(Math.Sin(g) * r); bool ok = true;
                    for (int dy = -1; dy <= 1 && ok; dy++)
                        for (int dx = -1; dx <= 1 && ok; dx++)
                        {
                            int tx = (x >> 4) + dx, ty = (y >> 4) + dy;
                            if (tx < 0 || ty < 0 || tx >= w.Map.width || ty >= w.Map.height) { ok = false; break; }
                            var c = w.Map.ClassAt(tx, ty);
                            ok = (c == ZClass.Ground || c == ZClass.Road) && w.Path.TilePassable(tx, ty, true);
                        }
                    if (ok) return (x, y);
                }
            return s.ShuttleBackPoint(5);
        }

        /// <summary>A gun whose box comes within 8 m of the point (px): within reach of the ship's outline + 0.5 m and its ramp.</summary>
        static bool GunNear(ZWorld w, int x, int y)
        {
            foreach (var o in w.Objects)
            {
                if (o.Kind != ZKind.Cannon || !OzV11Kit.Alive(o)) continue;
                double dx = Math.Max(0, Math.Max(o.LocX - x, x - (o.LocX + o.WidthPix))), dy = Math.Max(0, Math.Max(o.LocY - y, y - (o.LocY + o.HeightPix)));
                if (dx * dx + dy * dy <= 64.0 * 64.0) return true;
            }
            return false;
        }

        static bool UnitNear(ZWorld w, ZObject s, int x, int y)
        {
            foreach (var o in w.Objects)
                if (o != s && o.IsUnit && o.Kind != ZKind.Cannon && OzV11Kit.Alive(o) && Math.Abs(o.CenterX - x) < 96 && Math.Abs(o.CenterY - y) < 96) return true;
            return false;
        }

        public static string Check(string settings, string m16, string m02, string m17, IList<double> plants16 = null, bool row = false)
        {
            var sb = new StringBuilder();
            bool was = ZWorld.ShuttlePlayableDeviation, wasOn = ZWorld.ShuttleOn, fair0 = ZWorld.ShuttleFairSpotOn; var team0 = ZWorld.ShuttleTeam;
            try
            {
                ZWorld.ShuttlePlayableDeviation = true; ZWorld.ShuttleOn = true; ZWorld.ShuttleTeam = Team.Red; ZWorld.ShuttleFairSpotOn = true;
                sb.AppendLine($"K437 fair landing spot (DV128); level 16 plants: {(plants16 == null ? "none (out of Play the view hands in none)" : plants16.Count / 4 + " crowns handed in")}");
                // ---------------------------------------------------------------- A. the gatling island
                var w = World(settings, m16, 1, plants16, out var s);
                int ix = IslandTx * 16 + 8, iy = IslandTy * 16 + 8;
                var islandTiles = new List<(int tx, int ty)>();
                for (int ty = 61; ty <= 70; ty++) for (int tx = 25; tx <= 32; tx++) { var c = w.Map.ClassAt(tx, ty); if (c == ZClass.Ground || c == ZClass.Road) islandTiles.Add((tx, ty)); }
                var takenNow = islandTiles.Where(t => !w.ShuttleNeverLands(t.tx * 16 + 8, t.ty * 16 + 8, s, out _)).ToList();
                ZWorld.ShuttleFairSpotOn = false;
                var takenOld = islandTiles.Where(t => !w.ShuttleNeverLands(t.tx * 16 + 8, t.ty * 16 + 8, s, out _)).ToList();
                ZWorld.ShuttleFairSpotOn = true;
                sb.AppendLine($"   the island: {islandTiles.Count} dry tiles; the click takes {takenOld.Count} before, {takenNow.Count} now ({string.Join("; ", takenNow.Select(t => t.tx + "," + t.ty))})");
                var hds = new List<string>(); int fits = 0; bool promA = true; string promWhy = ""; double minGun = 99, minFlag = 99, minWater = 99;
                for (int k = 0; k < 8; k++)
                {
                    if (!Heading(w, s, ix, iy, k * 45.0, out double hd)) { hds.Add($"from {k * 45}: none"); continue; }
                    fits++; hds.Add($"from {k * 45}: {F(Norm(hd), "0")}");
                    var p = Measure(w, s, ix, iy, hd);
                    if (!p.ok) { promA = false; if (promWhy == "") promWhy = p.broken; }
                    minGun = Math.Min(minGun, p.gun); minFlag = Math.Min(minFlag, p.flag); minWater = Math.Min(minWater, p.water);
                }
                sb.AppendLine($"   tile {IslandTx},{IslandTy}: the landing's heading {string.Join(", ", hds)}; measured: the gun's ring {F(minGun)} m from the ship, the flag pole {F(minFlag)} m, water under the pods / nose {(minWater < 50 ? F(minWater) + " m outside the gear" : "none")}{(promA ? "" : "; BROKEN: " + promWhy)}");
                string wg, wf, ww;
                bool gunR = w.ShuttleNeverLands(GunTx * 16 + 8, GunTy * 16 + 8, s, out wg) && w.ShuttleNeverLands((GunTx + 1) * 16 + 8, (GunTy + 1) * 16 + 8, s, out _);
                bool flagR = w.ShuttleNeverLands(FlagTx * 16 + 8, FlagTy * 16 + 8, s, out wf);
                bool waterR = w.ShuttleNeverLands(33 * 16 + 8, 66 * 16 + 8, s, out ww);
                sb.AppendLine($"   still refused: the gun's tiles ({wg}), the flag's tile ({wf}), the water east of the island 33,66 ({ww})");
                bool aOk = takenOld.Count == 0 && takenNow.Contains((IslandTx, IslandTy)) && fits == 8 && promA && minGun >= 0.5 && minFlag >= 0.5 && gunR && flagR && wf == "flag" && waterR && ww == "water";
                sb.AppendLine(OzV11Kit.Verdict(aOk, $"A. the gatling island: tile {IslandTx},{IslandTy} taken (before: no island tile), the landing finds a heading from every arrival heading, the gear dry, the gun's ring (2 m) and the flag pole clear of the ship by the margin, the gun, the flag and the water still refused"));

                // ---------------------------------------------------------------- B. a flight to the island with a squad, EXIT, the flag
                {
                    var wb = World(settings, NoWalkers(m16), 2, plants16, out var sh);
                    OzV11Kit.Dummy(wb, 6 * 16, 6 * 16);                                                       // a blue robot that never moves: the game is not won at once (a won game stops every unit)
                    var (bx, by) = RobotSpot(wb, sh);
                    var lead = wb.CreateRobotGroup("grunt", bx - 8, by - 8, Team.Red, 4);
                    OzV11Kit.PlayerEnter(wb, lead, sh);
                    Run(wb, 60, () => sh.Passengers.Count > 0);
                    Run(wb, 20, () => sh.Passengers.Count >= 4);
                    Run(wb, 20, () => sh.ShuttleRampPhase == ZShuttleRamp.Shut && wb.Time >= sh.ShuttleRampHoldUntil);
                    int aboard = sh.Passengers.Count;
                    var pending = new Dictionary<ZObject, List<ZWaypoint>> { [sh] = new List<ZWaypoint> { ZWaypoint.Move(ix, iy, true) } };
                    var refused = new List<ZObject>();
                    wb.ShuttleOrdersFilter(Team.Red, pending, true, refused);
                    bool kept = refused.Count == 0 && pending.Count == 1 && wb.SendWaypoints(Team.Red, sh, pending[sh]);
                    double t0 = wb.Time; bool left = false;
                    Run(wb, 150, () => { if (sh.ShuttleState != ZShuttleState.Landed) left = true; return sh.Destroyed || (left && wb.Time - t0 > 0.5 && (sh.ShuttleState == ZShuttleState.Landed || sh.ShuttleState == ZShuttleState.Hover)); });
                    Run(wb, 6, () => sh.ShuttleLevelled && wb.Time >= sh.ShuttleRampHoldUntil);
                    bool landed = sh.ShuttleState == ZShuttleState.Landed && !sh.Destroyed;
                    bool atSpot = Math.Abs(sh.CenterX - ix) <= 2 && Math.Abs(sh.CenterY - iy) <= 2;
                    var pm = Measure(wb, sh, sh.CenterX, sh.CenterY, sh.ShuttleHeadingDeg);
                    bool ejected = wb.Eject(Team.Red, sh);
                    Run(wb, 15, () => sh.Passengers.Count == 0 && sh.ShuttleRampPhase == ZShuttleRamp.Shut);
                    var squad = wb.Objects.Where(o => o.IsRobot && o.Owner == Team.Red && OzV11Kit.Alive(o) && OzV11Kit.Dist(o, sh) < 160).ToList();
                    int badGround = 0;
                    foreach (var r in squad)
                    {
                        int tx = r.CenterX >> 4, ty = r.CenterY >> 4; var c = wb.Map.ClassAt(tx, ty);
                        if (c == ZClass.DeepWater || c == ZClass.Impassable || !wb.Path.TilePassable(tx, ty, true)) badGround++;
                    }
                    int inside = wb.ShuttleUnitsInside(sh, 0.0);
                    var flag = wb.Flags.FirstOrDefault(f => (f.LocX >> 4) == FlagTx && (f.LocY >> 4) == FlagTy);
                    var taker = squad.FirstOrDefault(r => !r.IsMinion) ?? squad.FirstOrDefault();
                    if (taker != null && flag != null) OzV11Kit.PlayerMove(wb, taker, flag.CenterX, flag.CenterY, false);
                    Run(wb, 40, () => flag != null && flag.Owner == Team.Red);
                    bool took = flag != null && flag.Owner == Team.Red;
                    sb.AppendLine($"   the flight: {aboard} aboard, the order {(kept ? "kept" : "TAKEN OUT")}, it {(landed ? "landed" : sh.ShuttleState.ToString())} at {F(sh.CenterX / 8.0, "0.0")},{F(sh.CenterY / 8.0, "0.0")} heading {F(Norm(sh.ShuttleHeadingDeg), "0")} (why not: '{sh.ShuttleNoLand}'), the gun's ring {F(pm.gun)} m from it{(pm.ok ? "" : ", BROKEN: " + pm.broken)}; EXIT {ejected}: {squad.Count} robots out, {badGround} on ground a robot cannot stand on, {inside} inside the ship; the island's flag taken by them: {took}");
                    bool bOk = aboard == 4 && kept && landed && atSpot && pm.ok && ejected && squad.Count == 4 && badGround == 0 && inside == 0 && took;
                    sb.AppendLine(OzV11Kit.Verdict(bOk, "B. a flight to the island: the order kept, it lands on tile 29,68 clear of the gun, the squad of 4 gets out onto ground a robot can stand on, and takes the island's flag beside the ship"));
                }

                // ---------------------------------------------------------------- C, D, F. three levels: the promises, before and after
                int step = row ? RowStep : Step;
                int cBroken = 0, cTested = 0, dLostBad = 0; var brokenList = new List<string>(); var lostWhy = new Dictionary<string, int>();
                var dLines = new List<string>(); var lostList = new List<string>(); var overlapList = new List<string>(); long hashA = 17, hashB = 17; int dOldHover = 0, dOldOnGun = 0, dOldClean = 0;
                foreach (var (lv, map, plants) in new[] { ("L16", m16, plants16), ("L02", m02, (IList<double>)null), ("L17", m17, (IList<double>)null) })
                {
                    var wl = World(settings, map, 3, plants, out var sl);
                    int e = ZWorld.ShuttleEdgeTiles, nNew = 0, nOld = 0, nLost = 0, nGain = 0, n = 0;
                    for (int ty = e; ty < wl.Map.height - e; ty += step)
                        for (int tx = e; tx < wl.Map.width - e; tx += step)
                        {
                            int x = tx * 16 + 8, y = ty * 16 + 8; n++;
                            bool nowNever = wl.ShuttleNeverLands(x, y, sl, out string why);
                            ZWorld.ShuttleFairSpotOn = false;
                            bool oldNever = wl.ShuttleNeverLands(x, y, sl, out string whyOld);
                            ZWorld.ShuttleFairSpotOn = true;
                            unchecked { hashA = hashA * 31 + (nowNever ? 1 : 0) + H(why); }
                            if (!nowNever) nNew++; if (!oldNever) nOld++;
                            if (!nowNever && oldNever) nGain++;
                            if (nowNever && !oldNever)
                            {
                                nLost++;
                                bool gun = why == "units" || GunNear(wl, x, y);                                    // a gun on the outline or the ramp at some heading (the word is the one at heading 0)
                                string key = gun ? "a gun beside" : why;
                                lostWhy[key] = lostWhy.TryGetValue(key, out int q) ? q + 1 : 1;
                                // what the rule as it was did there when the ship came (no unit near): it hovered, or it landed overlapping something
                                // (measured: a gun's ring under its hull or ramp, a rock or closed tile under the hull itself, ...), or it landed clear
                                ZWorld.ShuttleFairSpotOn = false;
                                double oh = 0; bool oldLands = !UnitNear(wl, sl, x, y) && Heading(wl, sl, x, y, 0, out oh);
                                ZWorld.ShuttleFairSpotOn = true;
                                Promise op = oldLands ? Measure(wl, sl, x, y, oh) : null;
                                if (!oldLands) dOldHover++;
                                else if (!op.ok) { dOldOnGun++; if (overlapList.Count < 4) overlapList.Add($"{lv} tile {tx},{ty}: {op.broken}"); }
                                else { dOldClean++; dLostBad++; if (lostList.Count < 6) lostList.Add($"{lv} tile {tx},{ty}: {why}"); }
                            }
                            if (nowNever || UnitNear(wl, sl, x, y)) continue;
                            if (!Heading(wl, sl, x, y, 0, out double hd)) continue;
                            cTested++;
                            var p = Measure(wl, sl, x, y, hd);
                            if (!p.ok) { cBroken++; if (brokenList.Count < 6) brokenList.Add($"{lv} tile {tx},{ty} heading {F(hd, "0")}: {p.broken}"); }
                        }
                    // F: the same answers again
                    for (int ty = e; ty < wl.Map.height - e; ty += step)
                        for (int tx = e; tx < wl.Map.width - e; tx += step)
                        {
                            bool nv = wl.ShuttleNeverLands(tx * 16 + 8, ty * 16 + 8, sl, out string why2);
                            unchecked { hashB = hashB * 31 + (nv ? 1 : 0) + H(why2); }
                        }
                    dLines.Add($"{lv}: {n} tiles tested, taken before {nOld}, now {nNew} (+{nGain}, refused now {nLost})");
                }
                sb.AppendLine($"   {string.Join("; ", dLines)}; refused now, taken before: {(lostWhy.Count == 0 ? "none" : string.Join(", ", lostWhy.Select(kv => kv.Key + " " + kv.Value)))}{(lostList.Count > 0 ? " (not for a gun: " + string.Join("; ", lostList) + ")" : "")}");
                sb.AppendLine($"   the places refused now, as the rule was when the ship came: it hovered {dOldHover}, it landed overlapping something {dOldOnGun}{(overlapList.Count > 0 ? " (" + string.Join("; ", overlapList) + ")" : "")}, it landed clear {dOldClean}");
                sb.AppendLine($"   the promises measured at {cTested} places the click takes (the landing's own heading): broken {cBroken}{(brokenList.Count > 0 ? " (" + string.Join("; ", brokenList) + ")" : "")}");
                sb.AppendLine(OzV11Kit.Verdict(cTested >= (row ? 150 : 500) && cBroken == 0, "C. wherever the click takes the order the landing keeps the promises: the gear dry, the ship off rocks, closed tiles, buildings, items, guns and flag poles, the ramp dry, the squad's line standable"));
                bool dOk = dLines.Count == 3 && dLostBad == 0 && dOldClean == 0 && dLines.All(l => !l.Contains("(+0,"));
                sb.AppendLine(OzV11Kit.Verdict(dOk, "D. more places taken on every level; a place taken before is refused now only where the ship could not land before either (it hovered) or landed overlapping something (a gun, a rock under its hull)"));

                // ---------------------------------------------------------------- E. the click agrees with the flight on places only the new rule takes
                {
                    string noW = NoWalkers(m16);
                    var wp = World(settings, noW, 4, plants16, out var sp);
                    var cand = new List<(long d, int x, int y)>();
                    int e = ZWorld.ShuttleEdgeTiles;
                    for (int ty = e; ty < wp.Map.height - e; ty += 2)
                        for (int tx = e; tx < wp.Map.width - e; tx += 2)
                        {
                            int x = tx * 16 + 8, y = ty * 16 + 8; long d = (long)(x - sp.CenterX) * (x - sp.CenterX) + (long)(y - sp.CenterY) * (y - sp.CenterY);
                            if (d < 160L * 160L || d > 900L * 900L) continue;
                            cand.Add((d, x, y));
                        }
                    var picked = new List<(int x, int y)>();
                    foreach (var (d, x, y) in cand.OrderBy(c => c.d))
                    {
                        if (picked.Count >= (row ? 3 : 6)) break;
                        if (picked.Any(p => Math.Abs(p.x - x) + Math.Abs(p.y - y) < 320)) continue;
                        if (wp.ShuttleNeverLands(x, y, sp, out _)) continue;
                        ZWorld.ShuttleFairSpotOn = false; bool oldNever = wp.ShuttleNeverLands(x, y, sp, out _); ZWorld.ShuttleFairSpotOn = true;
                        if (oldNever) picked.Add((x, y));
                    }
                    int kept = 0, landed = 0, hovered = 0, seed = 5; var lines = new List<string>();
                    foreach (var (x, y) in picked)
                    {
                        var wi = World(settings, noW, seed++, plants16, out var si);
                        var pending = new Dictionary<ZObject, List<ZWaypoint>> { [si] = new List<ZWaypoint> { ZWaypoint.Move(x, y, true) } };
                        var refused = new List<ZObject>();
                        wi.ShuttleOrdersFilter(Team.Red, pending, true, refused);
                        if (refused.Count == 0 && pending.Count == 1 && wi.SendWaypoints(Team.Red, si, pending[si])) kept++;
                        double t0 = wi.Time; bool left = false;
                        Run(wi, 150, () => { if (si.ShuttleState != ZShuttleState.Landed) left = true; return si.Destroyed || (left && wi.Time - t0 > 0.5 && (si.ShuttleState == ZShuttleState.Landed || si.ShuttleState == ZShuttleState.Hover)); });
                        if (si.ShuttleState == ZShuttleState.Landed && !si.Destroyed) landed++; else if (si.ShuttleState == ZShuttleState.Hover) hovered++;
                        lines.Add($"{F(x / 8.0, "0")},{F(y / 8.0, "0")} {(si.ShuttleState == ZShuttleState.Landed ? "landed" : si.ShuttleState + " (" + si.ShuttleNoLand + ")")}");
                    }
                    sb.AppendLine($"   places only the new rule takes, flown to: {string.Join("; ", lines)}");
                    sb.AppendLine(OzV11Kit.Verdict(picked.Count >= (row ? 3 : 4) && kept == picked.Count && landed == picked.Count, $"E. the click agrees with the flight: {picked.Count} new places, orders kept {kept}, landed {landed}, hovered {hovered}"));
                }

                // ---------------------------------------------------------------- F. determinism, the switch off
                int c0 = ZWorld.ShuttleGearWaterRefusals + ZWorld.ShuttleOverhangWater + ZWorld.ShuttleGunRefusals + ZWorld.ShuttleFineHeadings;
                ZWorld.ShuttleFairSpotOn = false;
                var wo = World(settings, m16, 1, plants16, out var so);
                for (int ty = 40; ty < 90; ty += 2) for (int tx = 10; tx < 60; tx += 2) { wo.ShuttleNeverLands(tx * 16 + 8, ty * 16 + 8, so, out _); wo.ShuttlePadFree(tx * 16 + 8, ty * 16 + 8, so, out _); }
                ZWorld.ShuttleFairSpotOn = true;
                int c1 = ZWorld.ShuttleGearWaterRefusals + ZWorld.ShuttleOverhangWater + ZWorld.ShuttleGunRefusals + ZWorld.ShuttleFineHeadings;
                sb.AppendLine($"   the answers of C again: {(hashA == hashB ? "identical" : "DIFFERENT")}; the switch off: the new rule's counters moved by {c1 - c0}");
                sb.AppendLine(OzV11Kit.Verdict(hashA == hashB && c1 == c0, "F. deterministic, and the switch off is the rule as it was (the new rule never asked)"));
            }
            finally { ZWorld.ShuttlePlayableDeviation = was; ZWorld.ShuttleOn = wasOn; ZWorld.ShuttleFairSpotOn = fair0; ZWorld.ShuttleTeam = team0; }
            return sb.ToString();
        }
    }
}
