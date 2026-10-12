using System;
using System.Collections.Generic;

namespace OpenZ.Sim
{
    /// <summary>
    ///
    /// Zod blocks a building's own tile pattern (ZWorld.SetMapImpassables: bfort.cpp:550-620 etc.). The 3D models do not match
    /// it everywhere: the imported fort is 20.0 x 19.8 m (10 x 9.9 tiles) centred in Zod's 10 x 12 (fort_front) / 10 x 11
    /// (fort_back) rect, so it covers rows 9-10 of the rect that Zod leaves walkable (the front wall and gatehouse of the red
    /// fort, the back wall of the blue one), plus tower corners Zod keeps open ((0,1) (0,2) (0,7) (0,8) (1,8) and mirrored);
    /// the radar dish base covers Zod's open corner (3,2). The factories and the repair station fit inside Zod's blocked rect.
    /// higher than 0.35 m, 0.125 m raster, prefab fit scale + level yaw), one entry per tile of the Zod rect below.
    ///
    /// What DV22 does (sim, tile grid, deterministic; no Unity, no floats from frame time):
    ///  - pathing: every Zod-walkable tile the mesh covers becomes impassable at map load, except Zod's gate route
    ///    through the fort (creation point -> exit, ENTER_FORT; 'o' below), which stays open (listed in the parity report);
    ///  - separation / idle: a unit's box grown by a margin (robots 2 px = 0.25 m, vehicles 4 px = 0.5 m) should not overlap a
    ///    mesh-covered tile: idle units are eased out of it (ZPathing.KeepOffBuildings) and a unit left standing inside a
    ///    building (a repair / factory exit cut short) walks out. Separation pushes, sidesteps and group spots already respect
    ///    the blocked tiles (ZPathing.BoxAllowed, ZReach); the optional MarginPushes / SlotPreference rules are off (see there).
    ///    Zod's own movement step is never changed by the margin.
    ///  - view: UnitView pushes a drawn unit out of the mesh-covered tiles and live prop tiles (rocks, huts, map objects;
    ///    PushOut) so view-side spacing never slides a model into a wall or a rock (view only, never writes the sim).
    /// The coverage table is also the building part of the line-of-sight grid (DV23, ZSight): coverage >= 40 % blocks sight.
    /// Toggle: ZWorld.ClearanceDeviation (static, read when a world is created; PlayerPrefs oz_clearance_sim, off in 'Zod exact').
    /// </summary>
    public sealed class ZClearance
    {
        /// <summary>Mesh coverage per tile of the Zod rect, row = y from the building's top-left tile: '.' no mesh, '0'..'9' =
        /// floor(10 x coverage), '#' >= 95 %, 'o' = Zod route through the building kept open (fort gate corridor).</summary>
        public static readonly Dictionary<string, string[]> Mesh = new Dictionary<string, string[]>
        {
            ["fort_front"] = new[] { "..........", "5845665485", "##########", "7########7", "09######90", ".9######9.", "0###oo###0", "3###oo###3", "9###oo###9", "8#78oo87#8", "0206oo6020", ".........." },
            ["fort_back"] = new[] { "...2oo2...", "4737oo7374", "####oo####", "7########7", "0########0", ".9######9.", ".9######9.", "3########3", "9########9", "8#88##88#8", "1311111131" },
            ["robot_factory"] = new[] { "....", "7887", "9##8", "5555", "...." },
            ["vehicle_factory"] = new[] { "2552", "8##8", "9##9", "8##8", "1001" },
            ["repair"] = new[] { "7###5", "8###7", "8###5", "68992" },
            ["radar"] = new[] { "4874", "2##2", "4884" },
        };
        /// <summary>Coverage digit from which a building tile blocks line of sight (DV23): 4 = 40 %.</summary>
        public const int SightCover = 4;
        /// <summary>Clearance margin (px) of a unit's box to mesh-covered tiles: robots 2 px (0.25 m), vehicles 4 px (0.5 m).</summary>
        public const int RobotMargin = 2, VehicleMargin = 4;
        /// <summary>DV22 parts (A/B switches). IdleEase (on): idle units ease out of the margin, a unit left inside a building walks
        /// out. MarginPushes (off): pushes / sidesteps may not go deeper into the margin; the headless bridge stress jammed 8-11
        /// vehicles at the L02 island next to the vehicle factory with it. SlotPreference (off): group spots clear of the margin
        /// first; it cost the L17 bridge two-way stress row 36 s of extra jam. The tile grid alone already keeps every box off
        /// the meshes; the two off parts stay as switches for A/B tests.</summary>
        public static bool MarginPushes = false, IdleEase = true, SlotPreference = false;
        /// <summary>Idle ease-out speed (px/s) out of the margin.</summary>
        public const double IdleEaseSpeed = 12;
        const byte None = 255, Keep = 254;

        readonly ZWorld W;
        readonly int w, h;
        readonly int[] bld;              // id of the building whose mesh covers the tile (0 = none)
        readonly byte[] cover;           // coverage digit 0..10 (10 = '#'), Keep, None
        /// <summary>DV22 active for this world (ZWorld.ClearanceDeviation when the world was created).</summary>
        public readonly bool Enabled;
        /// <summary>Tiles DV22 made impassable (x, y, building type) and mesh-covered Zod route tiles it kept open.</summary>
        public readonly List<(int x, int y, string type)> Added = new List<(int, int, string)>(), KeptOpen = new List<(int, int, string)>();

        public ZClearance(ZWorld world)
        {
            W = world; w = world.Map.width; h = world.Map.height;
            bld = new int[w * h]; cover = new byte[w * h];
            for (int i = 0; i < cover.Length; i++) cover[i] = None;
            Enabled = ZWorld.ClearanceDeviation;
        }

        /// <summary>ZWorld.CreateObject, after Zod's SetMapImpassables: record the mesh tiles, block the walkable ones (DV22).</summary>
        internal void OnCreated(ZObject o)
        {
            if (o.Kind != ZKind.Building || !Mesh.TryGetValue(o.Type, out var rows)) return;
            int tx0 = o.LocX >> 4, ty0 = o.LocY >> 4;
            for (int j = 0; j < rows.Length; j++)
                for (int i = 0; i < rows[j].Length; i++)
                {
                    char c = rows[j][i];
                    int tx = tx0 + i, ty = ty0 + j;
                    if (c == '.' || tx < 0 || ty < 0 || tx >= w || ty >= h) continue;
                    int k = ty * w + tx;
                    bld[k] = o.Id;
                    cover[k] = c == 'o' ? Keep : c == '#' ? (byte)10 : (byte)(c - '0');
                    bool zodOpen = W.Path.TilePassable(tx, ty, true);
                    if (!zodOpen) continue;
                    if (c == 'o') { KeptOpen.Add((tx, ty, o.Type)); continue; }
                    if (!Enabled) continue;
                    W.Path.SetImpassable(tx, ty);
                    Added.Add((tx, ty, o.Type));
                }
        }

        public bool OnMap(int tx, int ty) => tx >= 0 && ty >= 0 && tx < w && ty < h;
        /// <summary>Building id whose mesh covers the tile (0 = none; the kept-open gate route counts as none).</summary>
        public int MeshOwner(int tx, int ty) => OnMap(tx, ty) && cover[ty * w + tx] <= 10 ? bld[ty * w + tx] : 0;
        /// <summary>Mesh coverage of the tile in tenths (10 = full), -1 = none / kept open.</summary>
        public int Cover(int tx, int ty) => OnMap(tx, ty) && cover[ty * w + tx] <= 10 ? cover[ty * w + tx] : -1;
        /// <summary>Tile on Zod's gate route through a fort that DV22 keeps open although the mesh covers it.</summary>
        public bool KeptOpenTile(int tx, int ty) => OnMap(tx, ty) && cover[ty * w + tx] == Keep;

        /// <summary>A tile the sim's clearance margin keeps unit boxes away from: covered by a building mesh.</summary>
        public bool MarginTile(int tx, int ty) => MeshOwner(tx, ty) != 0;
        /// <summary>A tile the view keeps drawn units off: mesh-covered, or a live prop barrier (rock, hut, map object: its model
        /// fills its tile). Props are view-only here: in the sim Zod's own tile blocking already keeps boxes off them.</summary>
        public bool ViewTile(int tx, int ty) => MeshOwner(tx, ty) != 0 || W.Path.HasDestroyableBarrier(tx, ty);

        public static int Margin(ZObject o) => o.IsRobot ? RobotMargin : VehicleMargin;

        /// <summary>Overlap (px^2) of the unit's box at (locX, locY), grown by its margin, with mesh-covered tiles; out (ax, ay) = the
        /// overlap-weighted direction from those tiles to the unit (the way out).</summary>
        public double MarginOverlap(ZObject o, int locX, int locY, out double ax, out double ay)
        {
            ax = ay = 0;
            int m = Margin(o);
            int x0 = locX - m, y0 = locY - m, x1 = locX + o.WidthPix + m, y1 = locY + o.HeightPix + m;
            double cx = locX + o.WidthPix * 0.5, cy = locY + o.HeightPix * 0.5, sum = 0;
            for (int ty = y0 >> 4; ty <= (y1 - 1) >> 4; ty++)
                for (int tx = x0 >> 4; tx <= (x1 - 1) >> 4; tx++)
                {
                    if (!MarginTile(tx, ty)) continue;
                    int ox = Math.Min(x1, tx * 16 + 16) - Math.Max(x0, tx * 16), oy = Math.Min(y1, ty * 16 + 16) - Math.Max(y0, ty * 16);
                    if (ox <= 0 || oy <= 0) continue;
                    double a = ox * oy; sum += a;
                    double dx = cx - (tx * 16 + 8), dy = cy - (ty * 16 + 8), l = Math.Sqrt(dx * dx + dy * dy);
                    if (l > 1e-9) { ax += dx / l * a; ay += dy / l * a; }
                }
            return sum;
        }
        public double MarginOverlap(ZObject o, int locX, int locY) => MarginOverlap(o, locX, locY, out _, out _);

        /// <summary>A spot (unit centre) whose box of half size `half` grown by `margin` touches a margin tile (group spots).</summary>
        public bool NearMargin(int cx, int cy, int half, int margin)
        {
            int x0 = cx - half - margin, y0 = cy - half - margin, x1 = cx + half + margin, y1 = cy + half + margin;
            for (int ty = y0 >> 4; ty <= (y1 - 1) >> 4; ty++)
                for (int tx = x0 >> 4; tx <= (x1 - 1) >> 4; tx++)
                    if (MarginTile(tx, ty)) return true;
            return false;
        }

        /// <summary>A circle (px space) overlaps a mesh-covered tile or a live prop tile.</summary>
        public bool Touches(double x, double y, double r)
        {
            for (int ty = (int)Math.Floor((y - r) / 16); ty <= (int)Math.Floor((y + r) / 16); ty++)
                for (int tx = (int)Math.Floor((x - r) / 16); tx <= (int)Math.Floor((x + r) / 16); tx++)
                {
                    if (!ViewTile(tx, ty)) continue;
                    double qx = Math.Max(tx * 16, Math.Min(x, tx * 16 + 16)), qy = Math.Max(ty * 16, Math.Min(y, ty * 16 + 16));
                    if ((x - qx) * (x - qx) + (y - qy) * (y - qy) < r * r) return true;
                }
            return false;
        }

        /// <summary>View helper (pure, px space): the shortest offset (ox, oy) that puts a circle (px, py, r) clear of every
        /// mesh-covered tile and live prop tile (rings of 1 px, 16 directions, up to MaxPushPx); false when it touches none
        /// (or no clear spot is that near).</summary>
        public bool PushOut(double px, double py, double r, out double ox, out double oy)
        {
            ox = oy = 0;
            if (!Touches(px, py, r)) return false;
            for (int d = 1; d <= MaxPushPx; d++)
                for (int k = 0; k < 16; k++)
                {
                    double a = k * Math.PI / 8, x = px + Math.Cos(a) * d, y = py + Math.Sin(a) * d;
                    if (Touches(x, y, r)) continue;
                    ox = x - px; oy = y - py;
                    return true;
                }
            return false;
        }
        /// <summary>Longest view push searched (px): 64 px = 8 m (a view offset is at most 3.2 m, UnitView.maxSep).</summary>
        public const int MaxPushPx = 64;

        /// <summary>Pathing v3 DV47 (UnitView): as Touches, but with skipSoft a soft blocker tile (a wreck the unit has the soft pass for, not
        /// under a building mesh) is not a view tile - the model is drawn over the debris; buildings and other props still count.</summary>
        public bool Touches(double x, double y, double r, bool skipSoft)
        {
            if (!skipSoft || W.Pathing == null) return Touches(x, y, r);
            for (int ty = (int)Math.Floor((y - r) / 16); ty <= (int)Math.Floor((y + r) / 16); ty++)
                for (int tx = (int)Math.Floor((x - r) / 16); tx <= (int)Math.Floor((x + r) / 16); tx++)
                {
                    if (!ViewTile(tx, ty) || (MeshOwner(tx, ty) == 0 && W.Pathing.SoftTileAt(tx, ty))) continue;
                    double qx = Math.Max(tx * 16, Math.Min(x, tx * 16 + 16)), qy = Math.Max(ty * 16, Math.Min(y, ty * 16 + 16));
                    if ((x - qx) * (x - qx) + (y - qy) * (y - qy) < r * r) return true;
                }
            return false;
        }

        /// <summary>Pathing v3 DV47 (UnitView): as PushOut, with skipSoft = Touches(.., skipSoft): only the soft tiles are skipped.</summary>
        public bool PushOut(double px, double py, double r, bool skipSoft, out double ox, out double oy)
        {
            if (!skipSoft) return PushOut(px, py, r, out ox, out oy);
            ox = oy = 0;
            if (!Touches(px, py, r, true)) return false;
            for (int d = 1; d <= MaxPushPx; d++)
                for (int k = 0; k < 16; k++)
                {
                    double a = k * Math.PI / 8, x = px + Math.Cos(a) * d, y = py + Math.Sin(a) * d;
                    if (Touches(x, y, r, true)) continue;
                    ox = x - px; oy = y - py;
                    return true;
                }
            return false;
        }
    }

    public sealed partial class ZWorld
    {
        /// <summary>OpenZ deviation DV22 (building clearance, ZClearance). Read when a world is created. OFF = Zod's own building
        /// tiles only. ZGame sets it at every start (PlayerPrefs oz_clearance_sim, off in 'Zod exact' oz_zod_exact = 1).</summary>
        public static bool ClearanceDeviation = true;
        /// <summary>The building mesh table of this world (always present; its pathing part only when DV22 was on at creation).</summary>
        public ZClearance Clearance { get; private set; }
        internal void InitClearance() => Clearance = new ZClearance(this);
    }

    public sealed partial class ZPathing
    {
        /// <summary>DV22: a push / sidestep may not bring the unit's box deeper into the clearance margin of a building
        /// (Zod's own movement never goes through here).</summary>
        internal bool MarginOk(ZObject o, int oldLocX, int oldLocY, int newLocX, int newLocY)
        {
            var cl = W.Clearance;
            if (cl == null || !cl.Enabled || !ZClearance.MarginPushes || o.PathInBuildingStage) return true;
            double n = cl.MarginOverlap(o, newLocX, newLocY);
            if (n <= 0) return true;
            return n <= cl.MarginOverlap(o, oldLocX, oldLocY) + 1e-9;
        }

        /// <summary>Units the idle ease-out moved this world (stress / checks).</summary>
        public int ClearanceEases { get; private set; }

        /// <summary>DV22 EndTick: an idle unit whose box (+ margin) overlaps a building's mesh tiles is eased straight
        /// out of the margin (IdleEaseSpeed, only where the box is free and it keeps the spacing to other units).</summary>
        void KeepOffBuildings(double dt)
        {
            var cl = W.Clearance;
            if (cl == null || !cl.Enabled || !ZClearance.IdleEase) return;
            foreach (var o in units)
            {
                if (!o.IsMobile || Static(o) || o.Waypoints.Count > 0 || o.PathInBuildingStage || o.IsMoving) continue;
                double a = cl.MarginOverlap(o, o.LocX, o.LocY, out double ax, out double ay);
                if (!EaseStarts(o, a)) continue;                                            // DV44: starts 1 px deep, then runs until out (ZCommit.Settle.cs)
                if (ImpassCount(o, o.LocX, o.LocY) > 0) { WalkOutOfBuilding(o); continue; }       // left idle inside a building (a repair / factory exit cut short)
                double l = Math.Sqrt(ax * ax + ay * ay);
                if (l < 1e-6) continue;                                                     // boxed in on both sides (a 2-tile gap): stay
                double step = Math.Min(IdleEaseStep(dt), ZClearance.Margin(o)), ux = ax / l, uy = ay / l;
                foreach (var (c, sn) in EaseTurns)
                {                                                                           // straight out, else turned (a wall or a unit on that side)
                    double vx = ux * c - uy * sn, vy = ux * sn + uy * c;
                    int plx = (int)Math.Floor(o.LocX + o.XOver + vx), ply = (int)Math.Floor(o.LocY + o.YOver + vy);   // 1 px probe: must really get out
                    if (cl.MarginOverlap(o, plx, ply) >= a - 1e-9 || ImpassCount(o, plx, ply) > 0) continue;
                    if (TryNudge(o, vx * step, vy * step)) { ClearanceEases++; break; }
                }
            }
        }
        /// <summary>(cos, sin) of 0, +-35, +-70 deg as literals (no runtime trig in the sim: identical on every machine).</summary>
        static readonly (double c, double s)[] EaseTurns =
            { (1, 0), (0.8191520442889918, 0.573576436351046), (0.8191520442889918, -0.573576436351046), (0.3420201433256687, 0.9396926207859083), (0.3420201433256687, -0.9396926207859083) };

        /// <summary>The straight line from the unit to (x, y) is one Zod's movement takes from inside a building: the blocked tiles
        /// under the box never increase on the way (ZPathing.EmbeddedEscape) and, once clear, stay clear.</summary>
        bool EscapeLineOk(ZObject o, int x, int y)
        {
            double sx = o.LocX + o.XOver, sy = o.LocY + o.YOver, dx = x - o.CenterX, dy = y - o.CenterY, len = Math.Sqrt(dx * dx + dy * dy);
            int prev = ImpassCount(o, o.LocX, o.LocY), steps = (int)(len / 2) + 1;
            for (int i = 1; i <= steps; i++)
            {
                double f = Math.Min(1.0, i * 2 / Math.Max(len, 1e-9));
                int n = ImpassCount(o, (int)Math.Floor(sx + dx * f), (int)Math.Floor(sy + dy * f));
                if (n > prev || n >= 1000) return false;
                prev = n;
            }
            return prev == 0;
        }

        /// <summary>Units DV22 walked out of a building they were left standing in (checks).</summary>
        public int ClearanceWalkOuts { get; private set; }

        /// <summary>An idle unit standing inside a building's blocked tiles (Zod creates repaired / built units inside and drives them
        /// out with a FORCE_MOVE; if that move was cut short the unit would stand in the model for ever): a Zod-scuffle-style MOVE
        /// to the nearest free spot within 5 tiles it can reach, at most every ShoveInterval.</summary>
        void WalkOutOfBuilding(ZObject b)
        {
            if (W.Time < b.PS.nextShove || !b.CanMove || b.IsMinion) return;
            b.PS.nextShove = W.Time + ShoveInterval;
            bool nr = NoRocks(b); int c = Reach.UnitComp(b, nr);
            if (c < 0) return;
            var (btx, bty) = ZReach.FootTile(b.LocX, b.LocY);
            for (int ring = 1; ring <= 5; ring++)
                for (int ty = bty - ring; ty <= bty + ring; ty++)
                    for (int tx = btx - ring; tx <= btx + ring; tx++)
                    {
                        if (Math.Max(Math.Abs(tx - btx), Math.Abs(ty - bty)) != ring) continue;
                        var (x, y) = ZReach.FootCentre(tx, ty, b.IsRobot);
                        if (!Reach.PointOk(x, y, b.IsRobot, nr, c) || Occupied(x, y, b, null) || KeepClearAt(x, y) != null || !EscapeLineOk(b, x, y)) continue;
                        b.Waypoints.Add(ZWaypoint.Move(x, y, false, RefStepAside));
                        ClearanceWalkOuts++;
                        Log(b, $"DV22: idle inside a building: walks out to ({x},{y})");
                        return;
                    }
        }
        static double IdleEaseStep(double dt) => ZClearance.IdleEaseSpeed * dt;
    }
}
