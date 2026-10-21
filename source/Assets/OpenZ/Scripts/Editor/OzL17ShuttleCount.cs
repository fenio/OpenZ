using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// K444, level 17's own shuttles (rule DV134, Sim/ZWorld.L17Shuttles.cs; the box: UI/OzShuttleStart.cs), counted headless
    /// (ZWorld.Create + Tick, no scene; the box's state is handed in, the player's own box is never read or written). Menu OpenZ/V1.1/Shuttle:
    /// K444 Level 17 Shuttles Check (out of Play). The level starts are made as ZGame makes them (human red against the CPU blue; HARD with
    /// the fort door guns of DV73, EASY with the start crews of DV63, DV32's parked start, DV66's exit lanes, HEAD START on and off).
    ///  A. the box: offered for levels 16 and 17 on EASY / MEDIUM (never HARD, never level 2, never 'Zod exact'); level 17's rule switch
    ///     stays on whatever the box; the fort shuttle's answer (ZWorld.L17StartShuttle) = level 17, EASY / MEDIUM, the box on;
    ///  B. 12 level-17 starts (EASY / MEDIUM / HARD x the box on / off x HEAD START on / off): the empty car-park shuttle in every one (its
    ///     spot, its heading, the null team, no pilot, nobody aboard), the fort shuttle exactly where the box asks (its spot, red, its
    ///     pilot, the level's playable shuttle); no spot searched; every other object (type, team, place, size, hit points), every DV66 move
    ///     and every HEAD START place the same as with the rule off and the same with the box on and off;
    ///  C. room at every start: from each ship's outline, every vehicle and gun >= 4 m, every robot >= 3 m, every building >= 2 m, every
    ///     flag >= 4 m, every hut / item / detail / wreck / rock >= 1.5 m, every drawn prop of the keep-out list (lamps, bollards, boulders)
    ///     >= 1.5 m when the list is given; its ramp lane clear of buildings, items, props and vehicles by 0.5 m; no exit-lane point under
    ///     the outline + 0.5 m, not within 2 tiles of a fort's gate face; the landing spot rule takes it where it stands; no unit inside
    ///     its outline + 2 m;
    ///  D. the player takes the empty one: a red squad of 4 ordered into it boards (the leader its pilot, 3 aboard, the ship red); a red
    ///     robot standing beside it with no order never gets in; a blue (CPU) squad's ENTER on it is refused; it flies (order to a free spot
    ///     south-east of the fort), lands without hovering, EXIT lets the 3 out;
    ///  E. CPU against CPU on level 17 (both Normal, the box on: both ships there) for <see cref="CpuSoakSec"/> s x <see cref="CpuSeeds"/>
    ///     seeds, and the human (red, idle) against the CPU (EASY and MEDIUM) for the same time: the empty shuttle stays empty and neutral,
    ///     no CPU order ever targets it, no robot of a CPU ever has an ENTER on it, the CPU never flies the fort shuttle; the matches go on to
    ///     their end (or the time);
    ///  F. levels 2 and 16: every object the same with the rule on and off, and a CPU match's state hash the same after 120 s;
    ///  G. the rule off: no shuttle on level 17; 'Zod exact' (DV118 off): none;
    ///  H. the same start twice: the same objects; the same 120 s match twice: the same hash.
    /// </summary>
    public static class OzL17ShuttleCount
    {
        public const double CpuSoakSec = 720.0; public const int CpuSeeds = 3;
        /// <summary>The parity row's shorter soak.</summary>
        public const double RowSoakSec = 600.0; public const int RowSeeds = 1;
        /// <summary>Level 17's HEAD START (ZGame.EasyL17Types), parked empty (DV32).</summary>
        public static readonly string[] HeadStartL17 = { "missile_launcher", "heavy" };
        static readonly GameDifficulty[] Diffs = { GameDifficulty.Easy, GameDifficulty.Medium, GameDifficulty.Hard };
        const string L17File = "p02_bb_orig17.map";

        static string D(GameDifficulty d) => d == GameDifficulty.Easy ? "EASY" : d == GameDifficulty.Medium ? "MEDIUM" : "HARD";
        static string Sig(ZObject o) => o.Kind + ":" + o.Type + ":" + o.Owner + "@" + o.LocX + "," + o.LocY + ":" + o.WidthPix + "x" + o.HeightPix + ":" + o.MaxHealth;
        static string SigId(ZObject o) => o.Id + ":" + Sig(o);

        /// <summary>One level start as ZGame makes it (human red against the CPU blue; cpuRed: both CPUs, Normal, as a bot match).</summary>
        public static ZWorld Start(string settings, string map, string level, GameDifficulty d, bool box, bool headStart, bool rule, int seed, bool cpuRed,
                                   IList<(int x, int y, int r)> keepOut, out List<ZObject> extra)
        {
            bool bots = cpuRed;
            ZWorld.ShuttlePlayableDeviation = true; ZWorld.L16ShuttleDeviation = true; ZWorld.ShuttleTeam = Team.Red;
            ZWorld.ShuttleOn = OzShuttleStart.Rule(level, d, box);
            ZWorld.FortDoorGunDeviation = d == GameDifficulty.Hard && !bots;
            ZWorld.L17CrewsDeviation = level == "L17" && d == GameDifficulty.Easy && !bots; ZWorld.L17CrewsTeam = Team.Red;
            ZWorld.ExitLanesDeviation = true;
            ZWorld.L17ShuttlesDeviation = rule; ZWorld.L17StartShuttle = OzShuttleStart.L17Start(level, d, box);
            var m = ZMapData.FromJson(map);
            ZWorld.ParkingKeepOut = keepOut != null && m.file == L17File ? new List<(int, int, int)>(keepOut) : new List<(int, int, int)>();
            ZWorld.ParkingKeepOutMap = m.file;
            var w = ZWorld.Create(settings, map, seed, level == "L17");
            w.Bots.Add(new ZBot(w, Team.Blue, seed + 101) { Difficulty = bots ? ZBotDifficulty.Normal : d == GameDifficulty.Easy ? ZBotDifficulty.Easy : ZBotDifficulty.Normal });
            if (cpuRed) w.Bots.Add(new ZBot(w, Team.Red, seed) { Difficulty = ZBotDifficulty.Normal });
            for (int i = 0; i < w.TechBonus.Length; i++) w.TechBonus[i] = 0;
            if (!bots) w.TechBonus[(int)Team.Red] = d == GameDifficulty.Easy ? 5 : d == GameDifficulty.Medium ? 2 : 0;
            extra = headStart && !bots && level == "L17" ? w.AddHeadStart(Team.Red, true, HeadStartL17) : new List<ZObject>();
            w.Events.Clear();
            return w;
        }

        public static long Hash(ZWorld w)
        {
            long h = 17;
            unchecked { foreach (var o in w.Objects) { h = h * 31 + o.Id; h = h * 31 + o.Health; h = h * 31 + o.LocX; h = h * 31 + o.LocY; h = h * 31 + (o.Destroyed ? 1 : 0); h = h * 31 + (int)o.Owner; } }
            return h;
        }

        /// <summary>Metres from the ship's outline to a box (px, half-open; negative = they overlap), sampled every 2 px.</summary>
        static double BoxGap(ZObject s, double x0, double y0, double x1, double y1)
        {
            double best = double.MaxValue;
            for (double y = y0; y <= y1; y += 2)
                for (double x = x0; x <= x1; x += 2)
                    best = Math.Min(best, s.ShuttleOutlineGapM(Math.Min(x, x1 - 0.01), Math.Min(y, y1 - 0.01)));
            return best;
        }

        /// <summary>Metres from the ship's ramp lane (ZWorld.ShuttleLaneHalfW, from the hull's back to ShuttleBackM + ShuttleLaneBehindM
        /// behind its middle) to the point (px).</summary>
        static double LaneGap(ZObject s, double x, double y)
        {
            double hd = s.ShuttleHeadingDeg * Math.PI / 180.0, nx = Math.Cos(hd), ny = Math.Sin(hd);
            double dx = (x - s.CenterX - s.XOver) / 8.0, dy = (y - s.CenterY - s.YOver) / 8.0;
            double rz = dx * nx + dy * ny, rx = -dx * ny + dy * nx;
            double z0 = -(ZWorld.ShuttleBackM + ZWorld.ShuttleLaneBehindM), z1 = -ZObject.ShuttleHullBackM;
            double qx = Math.Max(0.0, Math.Abs(rx) - ZWorld.ShuttleLaneHalfW), qz = Math.Max(0.0, Math.Max(z0 - rz, rz - z1));
            return Math.Sqrt(qx * qx + qz * qz);
        }

        static double LaneBoxGap(ZObject s, double x0, double y0, double x1, double y1)
        {
            double best = double.MaxValue;
            for (double y = y0; y <= y1; y += 2)
                for (double x = x0; x <= x1; x += 2)
                    best = Math.Min(best, LaneGap(s, Math.Min(x, x1 - 0.01), Math.Min(y, y1 - 0.01)));
            return best;
        }

        /// <summary>C: the room round one ship at a start; "" = every rule kept, else what is too near.</summary>
        static string Room(ZWorld w, ZObject s, IList<(int x, int y, int r)> props, out string near)
        {
            var bad = new StringBuilder(); double nv = 99, nb = 99, nf = 99, ni = 99, np = 99, nl = 99; string wv = "", wb = "", wp = "", wl = "";
            foreach (var o in w.Objects)
            {
                if (o == s || o.Destroyed || o.KillAt >= 0) continue;
                if (Math.Abs(o.CenterX - s.CenterX) > 200 || Math.Abs(o.CenterY - s.CenterY) > 200) continue;
                double g = BoxGap(s, o.LocX, o.LocY, o.LocX + o.WidthPix, o.LocY + o.HeightPix);
                double lim; string what = o.Type + "#" + o.Id + "@" + (o.LocX >> 4) + "," + (o.LocY >> 4);
                if (o.Kind == ZKind.Vehicle || o.Kind == ZKind.Cannon) { lim = 4.0; if (g < nv) { nv = g; wv = what; } }
                else if (o.Kind == ZKind.Robot) lim = 3.0;
                else if (o.Kind == ZKind.Building) { lim = 2.0; if (g < nb) { nb = g; wb = what; } }
                else if (o.Type == "flag") { lim = 4.0; nf = Math.Min(nf, g); }
                else { lim = 1.5; ni = Math.Min(ni, g); }
                if (g < lim) bad.Append($" {what} {g:0.0} m (< {lim:0.0});");
                if (o.Kind != ZKind.Robot)
                {
                    double lg = LaneBoxGap(s, o.LocX, o.LocY, o.LocX + o.WidthPix, o.LocY + o.HeightPix);
                    if (lg < nl) { nl = lg; wl = what; }
                    if (lg < 0.5) bad.Append($" ramp lane: {what} {lg:0.0} m;");
                }
            }
            if (props != null)
                foreach (var p in props)
                {
                    if (Math.Abs(p.x - s.CenterX) > 200 || Math.Abs(p.y - s.CenterY) > 200) continue;
                    double g = s.ShuttleOutlineGapM(p.x, p.y) - p.r / 8.0, lg = LaneGap(s, p.x, p.y) - p.r / 8.0;
                    if (g < np) { np = g; wp = $"prop @{p.x / 16.0:0.0},{p.y / 16.0:0.0}"; }
                    if (g < 1.5) bad.Append($" drawn prop @{p.x / 16.0:0.0},{p.y / 16.0:0.0} {g:0.0} m;");
                    if (lg < nl) { nl = lg; wl = "prop"; }
                    if (lg < 0.5) bad.Append($" ramp lane: drawn prop @{p.x / 16.0:0.0},{p.y / 16.0:0.0} {lg:0.0} m;");
                }
            int lanePts = 0, all = 0;
            for (int y = s.CenterY - 48; y <= s.CenterY + 48; y += 4)
                for (int x = s.CenterX - 48; x <= s.CenterX + 48; x += 4)
                {
                    if (s.ShuttleOutlineGapM(x, y) >= 0.5) continue;
                    all++; if (w.InExitLane(x, y, 0)) lanePts++;
                }
            if (lanePts > 0) bad.Append($" {lanePts} of {all} outline points in an exit lane;");
            if (w.NearGateFace(s.CenterX - 32, s.CenterY - 32, s.CenterX + 32, s.CenterY + 32, 32)) bad.Append(" within 2 tiles of a fort's gate face;");
            if (!w.ShuttlePadFreeAt(s.CenterX, s.CenterY, s, s.ShuttleHeadingDeg, out string why)) bad.Append($" the landing spot rule refuses it where it stands ({why});");
            int inside = w.ShuttleUnitsInside(s, 2.0);
            if (inside > 0) bad.Append($" {inside} unit(s) within 2 m of its outline;");
            near = $"vehicle {nv:0.0} m ({wv}), building {nb:0.0} m ({wb}), flag {(nf < 90 ? nf.ToString("0.0") + " m" : "none within 25 m")}, item {(ni < 90 ? ni.ToString("0.0") + " m" : "none within 25 m")}, prop {(props != null && props.Count > 0 ? np.ToString("0.0") + " m (" + wp + ")" : "n/a")}, lane {nl:0.0} m ({wl})";
            return bad.ToString();
        }

        static bool AtSpot(ZObject s, int[] spot) => s != null && s.CenterX == spot[0] * 16 + 8 && s.CenterY == spot[1] * 16 + 8 && Math.Abs(Norm(s.ShuttleHeadingDeg - spot[2])) < 0.01;
        static double Norm(double a) { a %= 360.0; if (a > 180.0) a -= 360.0; if (a <= -180.0) a += 360.0; return a; }
        static string Where(ZObject s) => s == null ? "none" : $"#{s.Id} {s.Owner} tile {s.CenterX >> 4},{s.CenterY >> 4} hd {s.ShuttleHeadingDeg:0} pilots {s.Drivers.Count} aboard {s.Passengers.Count}";

        /// <summary>The first tile middle ring by ring from (tx, ty) where the ship may land (ZWorld.ShuttlePadFree, units counted).</summary>
        static (int x, int y) FreeSpot(ZWorld w, ZObject s, int tx, int ty)
        {
            for (int r = 0; r <= 12; r++)
                for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                        int x = (tx + dx) * 16 + 8, y = (ty + dy) * 16 + 8;
                        if (w.ShuttlePadFree(x, y, s, out _)) return (x, y);
                    }
            return (-1, -1);
        }

        public static string Check(string settings, string m17, string m16, string m02, IList<(int x, int y, int r)> keepOut, double soakSec, int seeds)
        {
            var sb = new StringBuilder();
            bool was = ZWorld.ShuttlePlayableDeviation, wasOn = ZWorld.ShuttleOn, wasL16 = ZWorld.L16ShuttleDeviation, wasDoor = ZWorld.FortDoorGunDeviation, wasCrews = ZWorld.L17CrewsDeviation;
            bool wasRule = ZWorld.L17ShuttlesDeviation, wasStart = ZWorld.L17StartShuttle, wasLanes = ZWorld.ExitLanesDeviation;
            var team0 = ZWorld.ShuttleTeam; var keep0 = ZWorld.ParkingKeepOut; var keepMap0 = ZWorld.ParkingKeepOutMap;
            bool props = keepOut != null && keepOut.Count > 0;
            try
            {
                sb.AppendLine($"K444 level 17's own shuttles (DV134): the car-park spot tile {ZWorld.L17CarParkShuttleSpot[0]},{ZWorld.L17CarParkShuttleSpot[1]} heading {ZWorld.L17CarParkShuttleSpot[2]}, the fort spot tile {ZWorld.L17FortShuttleSpot[0]},{ZWorld.L17FortShuttleSpot[1]} heading {ZWorld.L17FortShuttleSpot[2]}; drawn props of the keep-out list: {(props ? keepOut.Count + " discs" : "none given (C measures the rules' objects only)")}");
                // ---------------------------------------------------------------- A. the box
                bool aOk = true; var at = new StringBuilder();
                foreach (var lv in new[] { "L16", "L02", "L17" })
                    foreach (var d in Diffs)
                    {
                        bool off = OzShuttleStart.Offered(lv, d, false), wantOff = (lv == "L16" || lv == "L17") && d != GameDifficulty.Hard;
                        bool slot = OzShuttleStart.HasSlot(lv, false), wantSlot = lv == "L16" || lv == "L17";
                        bool st1 = OzShuttleStart.L17Start(lv, d, true), st0 = OzShuttleStart.L17Start(lv, d, false), wantSt = lv == "L17" && d != GameDifficulty.Hard;
                        bool rule = lv != "L17" || (OzShuttleStart.Rule(lv, d, true) && OzShuttleStart.Rule(lv, d, false));
                        aOk &= off == wantOff && slot == wantSlot && st1 == wantSt && !st0 && rule && !OzShuttleStart.Offered(lv, d, true) && !OzShuttleStart.HasSlot(lv, true);
                        at.Append($" {lv} {D(d)}: {(off ? "offered" : slot ? "locked" : "-")}{(lv == "L17" ? ", fort ship " + (st1 ? "on" : "off") + " / off" : "")};");
                    }
                sb.AppendLine("A. the box ->" + at);
                sb.AppendLine(OzV11Kit.Verdict(aOk, "the box: levels 16 and 17 on EASY / MEDIUM (locked on HARD, no slot on level 2, never in 'Zod exact'); level 17's rule switch on whatever the box; the fort ship = level 17, EASY / MEDIUM, the box on"));

                // ---------------------------------------------------------------- B / C. the starts
                int starts = 0; var bBad = new StringBuilder(); var cBad = new StringBuilder(); var nearLines = new List<string>();
                foreach (var d in Diffs)
                    foreach (bool hs in new[] { false, true })
                    {
                        var off = Start(settings, m17, "L17", d, true, hs, false, 1, false, keepOut, out var exOff);
                        var offSig = off.Objects.Select(Sig).ToList(); var offMoves = string.Join(";", off.ExitLaneMoves.Select(mv => mv.type + mv.fx + "," + mv.fy + ">" + mv.tx + "," + mv.ty));
                        var offHs = string.Join(";", exOff.Select(v => v.Type + "@" + v.LocX + "," + v.LocY));
                        List<string> boxOnSig = null;
                        foreach (bool box in new[] { true, false })
                        {
                            starts++;
                            var w = Start(settings, m17, "L17", d, box, hs, true, 1, false, keepOut, out var ex);
                            string tag = $"{D(d)} box {(box ? "on" : "off")} head start {(hs ? "on" : "off")}";
                            var cp = w.L17CarParkShuttle; var fs = w.L17FortShuttle;
                            bool wantFort = box && d != GameDifficulty.Hard;
                            int ships = w.Objects.Count(o => o.IsShuttle && !o.Destroyed);
                            bool ok = cp != null && cp.ShuttleIsEmpty && AtSpot(cp, ZWorld.L17CarParkShuttleSpot) && w.L17ShuttlesMoved == 0 && w.L17ShuttleWhy == ""
                                      && (wantFort ? fs != null && fs.Owner == Team.Red && fs.Drivers.Count == 1 && fs.Passengers.Count == 0 && AtSpot(fs, ZWorld.L17FortShuttleSpot) && w.PlayableShuttle == fs && ships == 2
                                                   : fs == null && w.PlayableShuttle == null && ships == 1);
                            if (!ok) bBad.Append($" {tag}: car park {Where(cp)}, fort {Where(fs)}, playable {(w.PlayableShuttle != null ? "#" + w.PlayableShuttle.Id : "none")}, moved {w.L17ShuttlesMoved} '{w.L17ShuttleWhy}';");
                            var sig = w.Objects.Where(o => !o.IsShuttle).Select(Sig).ToList();
                            if (!sig.SequenceEqual(offSig)) bBad.Append($" {tag}: the other objects differ from the rule off ({sig.Count} / {offSig.Count});");
                            if (box) boxOnSig = sig; else if (boxOnSig != null && !sig.SequenceEqual(boxOnSig)) bBad.Append($" {tag}: the other objects differ with the box on / off;");
                            string moves = string.Join(";", w.ExitLaneMoves.Select(mv => mv.type + mv.fx + "," + mv.fy + ">" + mv.tx + "," + mv.ty));
                            if (moves != offMoves) bBad.Append($" {tag}: the DV66 moves differ;");
                            string hsNow = string.Join(";", ex.Select(v => v.Type + "@" + v.LocX + "," + v.LocY));
                            if (hsNow != offHs || (hs && ex.Count != HeadStartL17.Length)) bBad.Append($" {tag}: HEAD START {hsNow} (rule off: {offHs});");
                            foreach (var s in new[] { cp, fs })
                            {
                                if (s == null) continue;
                                string r = Room(w, s, props ? keepOut : null, out string near);
                                if (r != "") cBad.Append($" {tag} {(s == cp ? "car park" : "fort")}:{r}");
                                if (d == GameDifficulty.Easy && box && hs) nearLines.Add($"   {(s == cp ? "car-park" : "fort")} ship at {Where(s)}: nearest {near}");
                            }
                        }
                    }
                sb.AppendLine($"B. {starts} level-17 starts: {(bBad.Length == 0 ? "the empty car-park ship in every one, the fort ship exactly where the box asks, no spot searched; every other object, DV66 move and HEAD START place as with the rule off" : "WRONG:" + bBad)}");
                sb.AppendLine(OzV11Kit.Verdict(starts == 12 && bBad.Length == 0, "every start: the EMPTY car-park shuttle (its spot, nose south, no pilot), the fort shuttle only with the box on EASY / MEDIUM (red, its pilot, the playable one), the world otherwise as before"));
                foreach (var l in nearLines) sb.AppendLine(l);
                sb.AppendLine(OzV11Kit.Verdict(cBad.Length == 0, "room at every start: vehicles / guns >= 4 m, robots >= 3 m, buildings >= 2 m, flags >= 4 m, items >= 1.5 m" + (props ? ", drawn props >= 1.5 m" : "") + ", the ramp lane clear by 0.5 m, no exit lane, no gate face, the landing rule takes it, nobody within 2 m" + (cBad.Length > 0 ? "; TOO NEAR:" + cBad : "")));

                // ---------------------------------------------------------------- D. the player takes the empty one
                {
                    var dl = new StringBuilder(); bool dOk = true;
                    // the CPU's squad first, in a start of its own (no fight with the player's squad): its ENTER on the empty ship is refused
                    var wb = Start(settings, m17, "L17", GameDifficulty.Medium, true, false, true, 1, false, keepOut, out _);
                    var sb0 = wb.L17CarParkShuttle;
                    if (sb0 != null)
                    {
                        var (qx, qy) = sb0.ShuttleBackPoint(6);
                        var blue = wb.CreateRobotGroup("grunt", qx - 8, qy - 8, Team.Blue, 2);
                        bool blueTaken = wb.SendWaypoints(Team.Blue, blue, new[] { new ZWaypoint { mode = WpMode.Enter, refId = sb0.Id, x = sb0.CenterX, y = sb0.CenterY } });
                        double tb = wb.Time;
                        while (wb.Time - tb < 20) wb.Tick(ZConst.TICK);
                        bool stay = sb0.ShuttleIsEmpty && sb0.Owner == Team.Neutral;
                        dl.Append($" a CPU (blue) squad of 2 at the ramp ordered in ({(blueTaken ? "the order list accepted, the boarding refused" : "the order refused")}): after 20 s the ship {(stay ? "is still empty and neutral" : "WAS TAKEN: " + Where(sb0))};");
                        dOk &= stay;
                    }
                    var w = Start(settings, m17, "L17", GameDifficulty.Medium, true, false, true, 1, false, keepOut, out _);
                    var s = w.L17CarParkShuttle; dOk &= s != null && sb0 != null;
                    if (s != null)
                    {
                        var (bx, by) = s.ShuttleBackPoint(6);
                        var lead = w.CreateRobotGroup("grunt", bx - 8, by - 8, Team.Red, 4);
                        double side = (s.ShuttleHeadingDeg + 90) * Math.PI / 180;
                        var idle = w.CreateRobotGroup("grunt", s.CenterX + (int)(Math.Cos(side) * 44) - 8, s.CenterY + (int)(Math.Sin(side) * 44) - 8, Team.Red, 1);   // beside it, no order
                        int entered = 0;
                        bool sent = w.SendWaypoints(Team.Red, lead, new[] { new ZWaypoint { mode = WpMode.Enter, refId = s.Id, x = s.CenterX, y = s.CenterY, playerGiven = true } });
                        double t0 = w.Time;
                        while (w.Time - t0 < 60 && !(s.Owner == Team.Red && s.Drivers.Count == 1 && s.Passengers.Count == 3 && s.ShuttleRampPhase == ZShuttleRamp.Shut))
                        {
                            w.Tick(ZConst.TICK);
                            foreach (var e in w.Events) if (e.type == ZEventType.Entered && e.otherId == s.Id) entered++;
                            w.Events.Clear();
                        }
                        bool boarded = s.Owner == Team.Red && s.Drivers.Count == 1 && s.Passengers.Count == 3;
                        bool idleNotIn = entered == 1 && s.Drivers.Count + s.Passengers.Count == 4;
                        dl.Append($" a red squad of 4 ordered in: ordered {sent}, boarded {boarded} after {w.Time - t0:0.0} s ({Where(s)}), boardings of the ship {entered}; the red robot beside it with no order {(idleNotIn ? "never got in" : "GOT IN")} ({(idle.KillAt < 0 ? "still outside" : "it entered something else")});");
                        dOk &= sent && boarded && idleNotIn;
                        while (w.Time - t0 < 75 && !(s.ShuttleRampPhase == ZShuttleRamp.Shut && w.Time >= s.ShuttleRampHoldUntil)) w.Tick(ZConst.TICK);
                        var (gx, gy) = FreeSpot(w, s, 95, 55);
                        bool mv = gx > 0 && w.SendWaypoints(Team.Red, s, new[] { new ZWaypoint { mode = WpMode.Move, x = gx, y = gy, playerGiven = true } });
                        double t1 = w.Time; int fl0 = s.ShuttleFlights, hv0 = s.ShuttleHovers;
                        while (w.Time - t1 < 120 && !(s.ShuttleFlights > fl0 && s.ShuttleState == ZShuttleState.Landed && s.ShuttleLift <= 0 && s.ShuttleLevelled)) w.Tick(ZConst.TICK);
                        bool flew = s.ShuttleFlights > fl0 && s.ShuttleState == ZShuttleState.Landed && s.ShuttleHovers == hv0 && Math.Abs(s.CenterX - gx) <= 16 && Math.Abs(s.CenterY - gy) <= 16;
                        dl.Append($" flown to tile {gx >> 4},{gy >> 4} (the first free spot from 95,55, south-east of the fort): {(flew ? "landed there" : "state " + s.ShuttleState + " '" + s.ShuttleNoLand + "'")} after {w.Time - t1:0.0} s;");
                        while (w.Time - t1 < 130 && w.Time < s.ShuttleRampHoldUntil) w.Tick(ZConst.TICK);
                        int before = w.Objects.Count(o => o.IsRobot && o.Owner == Team.Red && !o.Destroyed && o.KillAt < 0);
                        bool ej = w.Eject(Team.Red, s); double t2 = w.Time;
                        while (w.Time - t2 < 30 && s.Passengers.Count > 0) w.Tick(ZConst.TICK);
                        for (int k = 0; k < 200; k++) w.Tick(ZConst.TICK);
                        int after = w.Objects.Count(o => o.IsRobot && o.Owner == Team.Red && !o.Destroyed && o.KillAt < 0);
                        dl.Append($" EXIT {(ej ? "taken" : "REFUSED")}: aboard {s.Passengers.Count}, red robots on the map {before} -> {after};");
                        dOk &= mv && flew && ej && s.Passengers.Count == 0 && after - before == 3 && s.Drivers.Count == 1;
                    }
                    sb.AppendLine("D." + dl);
                    sb.AppendLine(OzV11Kit.Verdict(dOk, "the player takes the empty car-park shuttle by an order (the leader its pilot, 3 aboard, red); nobody gets in by himself; the CPU's ENTER is refused; it flies, lands, EXIT lets the 3 out"));
                }

                // ---------------------------------------------------------------- E. CPU against CPU, and the human against the CPU
                {
                    var el = new StringBuilder(); bool eOk = true; int ends = 0, runs = 0;
                    var cases = new List<(string name, bool cpuRed, GameDifficulty d, int seed)>();
                    for (int seed = 1; seed <= seeds; seed++) cases.Add(("CPU vs CPU", true, GameDifficulty.Medium, seed));
                    cases.Add(("human (idle) vs CPU EASY", false, GameDifficulty.Easy, 1));
                    cases.Add(("human (idle) vs CPU MEDIUM", false, GameDifficulty.Medium, 2));
                    foreach (var c in cases)
                    {
                        runs++;
                        var w = Start(settings, m17, "L17", c.d, true, false, true, c.seed, c.cpuRed, keepOut, out _);
                        var s = w.L17CarParkShuttle; var f = w.L17FortShuttle; int sid = s != null ? s.Id : -1, fid = f != null ? f.Id : -1;
                        int targeted = 0, enterTicks = 0, notEmpty = 0, fortFlights = 0, fortOwn = 0, fortAttacks = 0, auto0 = ZObject.ShuttleEmptyAutoRefused; int hp0 = s != null ? s.Health : 0;
                        var seen = new HashSet<string>();
                        while (w.Time < soakSec && !w.GameOver)
                        {
                            w.Tick(ZConst.TICK);
                            foreach (var b in w.Bots)
                                foreach (var o in b.LastRoundOrders)
                                {
                                    if (o.targetId != sid && o.targetId != fid) continue;
                                    if (!seen.Add(b.Team + ":" + o.unitId + ":" + o.targetId + ":" + o.time.ToString("0.000"))) continue;   // each order once (the list lives until the next round)
                                    if (o.targetId == sid) targeted++;                                                                 // the empty car-park ship: never
                                    else if (f != null && b.Team == f.Owner) fortOwn++;                                                // the fort ship's own team's CPU: never
                                    else fortAttacks++;                                                                                // the other CPU's attack on that ship: as on any enemy unit
                                }
                            foreach (var o in w.Objects)
                            {
                                if (!o.IsRobot || o.Destroyed || o.Waypoints.Count == 0) continue;
                                var wp = o.Waypoints[0];
                                if (wp.mode == WpMode.Enter && (wp.refId == sid || wp.refId == fid) && !w.IsHumanTeam(o.Owner)) enterTicks++;
                            }
                            if (s != null && !s.Destroyed && !(s.Owner == Team.Neutral && s.Drivers.Count == 0 && s.Passengers.Count == 0)) notEmpty++;
                        }
                        if (f != null) fortFlights = f.ShuttleFlights;
                        bool ok = s != null && targeted == 0 && fortOwn == 0 && enterTicks == 0 && notEmpty == 0 && fortFlights == 0;
                        if (w.GameOver) ends++;
                        eOk &= ok;
                        el.Append($"\n   {c.name} seed {c.seed}: {(w.GameOver ? "won by " + w.Winner : "time")} at {w.Time:0} s; CPU orders on the empty car-park ship {targeted}, on the fort ship by its own team's CPU {fortOwn} (by the other CPU, attacks as on any enemy unit: {fortAttacks}), CPU robot ticks with an ENTER on a level-17 shuttle {enterTicks}, ticks the car-park ship was not empty and neutral {notEmpty}, the fort ship's flights {fortFlights}" +
                                  $" (CPU orders on it refused {w.ShuttleCpuOrdersRefused}), unordered ENTERs dropped {ZObject.ShuttleEmptyAutoRefused - auto0}; the car-park ship {(s == null ? "MISSING" : s.Destroyed ? "destroyed by fire" : "hit points " + s.Health + " / " + hp0)}");
                    }
                    sb.AppendLine($"E. {runs} matches on level 17 ({soakSec:0} s each or to the end; {ends} ended):" + el);
                    sb.AppendLine(OzV11Kit.Verdict(eOk, "the CPU never targets, enters or captures the empty car-park shuttle (it stays empty and neutral) and never flies the fort one (CPU vs CPU and human vs CPU)"));
                }

                // ---------------------------------------------------------------- F. levels 2 and 16
                {
                    var fl = new StringBuilder(); bool fOk = true;
                    foreach (var (lv, map) in new[] { ("L02", m02), ("L16", m16) })
                        foreach (var d in Diffs)
                        {
                            var a = Start(settings, map, lv, d, true, false, false, 1, false, null, out _); var b = Start(settings, map, lv, d, true, false, true, 1, false, null, out _);
                            bool same = a.Objects.Select(SigId).SequenceEqual(b.Objects.Select(SigId)) && b.L17ShuttlesMade == 0;
                            fOk &= same; if (!same) fl.Append($" {lv} {D(d)}: the start DIFFERS;");
                        }
                    foreach (var (lv, map) in new[] { ("L02", m02), ("L16", m16) })
                    {
                        long[] h = new long[2];
                        for (int k = 0; k < 2; k++)
                        {
                            var w = Start(settings, map, lv, GameDifficulty.Medium, true, false, k == 1, 1, true, null, out _);
                            while (w.Time < 120 && !w.GameOver) w.Tick(ZConst.TICK);
                            h[k] = Hash(w);
                        }
                        fl.Append($" {lv} CPU match 120 s: hash {h[0]} rule off / {h[1]} on;");
                        fOk &= h[0] == h[1];
                    }
                    sb.AppendLine("F." + fl);
                    sb.AppendLine(OzV11Kit.Verdict(fOk, "levels 2 and 16: every start (3 difficulties) and a 120 s CPU match exactly the same with the rule on"));
                }

                // ---------------------------------------------------------------- G. switches, H. determinism
                {
                    var off = Start(settings, m17, "L17", GameDifficulty.Medium, true, false, false, 1, false, keepOut, out _);
                    bool gOk = off.Objects.Count(o => o.IsShuttle) == 0 && off.L17ShuttlesMade == 0;
                    ZWorld.ShuttlePlayableDeviation = false;
                    ZWorld.L17ShuttlesDeviation = true; ZWorld.L17StartShuttle = true; ZWorld.ShuttleOn = true;
                    var m = ZMapData.FromJson(m17); ZWorld.ParkingKeepOut = new List<(int, int, int)>(); ZWorld.ParkingKeepOutMap = m.file;
                    var zx = ZWorld.Create(settings, m17, 1, true);
                    gOk &= zx.Objects.Count(o => o.IsShuttle) == 0 && zx.L17ShuttlesMade == 0;
                    sb.AppendLine(OzV11Kit.Verdict(gOk, $"the rule off: no shuttle on level 17 ({off.Objects.Count(o => o.IsShuttle)}); 'Zod exact' (DV118 off): none ({zx.Objects.Count(o => o.IsShuttle)})"));
                    var a = Start(settings, m17, "L17", GameDifficulty.Easy, true, true, true, 3, false, keepOut, out _); var b = Start(settings, m17, "L17", GameDifficulty.Easy, true, true, true, 3, false, keepOut, out _);
                    bool hOk = a.Objects.Select(SigId).SequenceEqual(b.Objects.Select(SigId));
                    long[] hh = new long[2];
                    for (int k = 0; k < 2; k++)
                    {
                        var w = Start(settings, m17, "L17", GameDifficulty.Medium, true, false, true, 2, true, keepOut, out _);
                        while (w.Time < 120 && !w.GameOver) w.Tick(ZConst.TICK);
                        hh[k] = Hash(w);
                    }
                    hOk &= hh[0] == hh[1];
                    sb.AppendLine(OzV11Kit.Verdict(hOk, $"deterministic: the same start twice the same objects; the same 120 s CPU match twice the same hash ({hh[0]} / {hh[1]})"));
                }
            }
            finally
            {
                ZWorld.ShuttlePlayableDeviation = was; ZWorld.ShuttleOn = wasOn; ZWorld.L16ShuttleDeviation = wasL16; ZWorld.FortDoorGunDeviation = wasDoor; ZWorld.L17CrewsDeviation = wasCrews;
                ZWorld.L17ShuttlesDeviation = wasRule; ZWorld.L17StartShuttle = wasStart; ZWorld.ExitLanesDeviation = wasLanes; ZWorld.ShuttleTeam = team0;
                ZWorld.ParkingKeepOut = keep0; ZWorld.ParkingKeepOutMap = keepMap0;
            }
            int pass = sb.ToString().Split('\n').Count(l => l.StartsWith("PASS")), fail = sb.ToString().Split('\n').Count(l => l.StartsWith("FAIL"));
            sb.AppendLine($"K444 CHECK: {pass} PASS {fail} FAIL");
            return sb.ToString();
        }

        /// <summary>The editor's keep-out list for level 17 when ZGame set it for level 17 at the last level start (the scene's DEC_ props and
        /// the night lamps), else none.</summary>
        public static IList<(int x, int y, int r)> EditorKeepOut() =>
            ZWorld.ParkingKeepOutMap == L17File && ZWorld.ParkingKeepOut != null && ZWorld.ParkingKeepOut.Count > 0 ? new List<(int, int, int)>(ZWorld.ParkingKeepOut) : null;
    }
}
