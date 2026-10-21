using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Counted check of the crocodile rule of level 16 (Sim/ZWorld.Crocodiles.cs, rows DV91 to DV94). Headless simulation
    /// (ZWorld.Create + Tick): no scene, no Play mode, nothing saved; only public members of the simulation.
    ///   1 the table: every crocodile stands on water that is its own
    ///   2 SquadCrossings crossings of the west centre pool (its short way, bank to bank, through its attack water) by a 4-robot squad
    ///     on an order: CrossMin to CrossMax lose one robot, never two. They are counted in sets of CrossSet (the plan's "20 squad
    ///     crossings ...: 8 to 14 lose one robot"): every set walks the same five lines, both ways; the start moment and the seed run
    ///     on from crossing to crossing (0.5 s to 37 s into the level: the pool's animals are met at every point of their round of
    ///     resting and swimming). One set alone swings by several crossings whenever the objects of the level get other numbers
    ///     (the rule's chance is a hash of tick, pool and robot number: a kit that adds map objects moves every robot's number),
    ///     so the verdict is taken on all the sets together, in the plan's own proportion; each set is printed
    ///   3 IdleRobots idle robots two tiles from a bank, a hunt started on each: IdleMin or more reach the bank
    ///   4 a robot on hold position stays; a robot on an order keeps it and wades 1.3 times as fast near the hunt, stamina untouched
    ///   5 a whole match of the two computer players, LongSeconds of game time, sampled every 0.5 s: no part of an animal nearer
    ///     than BankM to a bank or on land, never two robots in one hunt, never more than two hunts at once
    ///   6 levels 2 and 17, computer against computer, with the four switches off and on: the same game, tick for tick
    ///   7 on level 16 a swamp tile weighs double in the robots' path search (five fixed routes) and an idle robot does not wade for a flag
    ///   8 with the switch off level 16 has no crocodile
    /// Menu: OpenZ/L16 Jungle/Checks/Count Crocodile Rule (J8). The last line reads "PASS n/n" when every line passed.
    /// </summary>
    public static class OzL16CrocCount
    {
        public const int SquadCrossings = 100, CrossSet = 20, CrossMin = 40, CrossMax = 70, IdleRobots = 20, IdleMin = 15;
        public const double BankM = 0.6, LongSeconds = 1800, SameSeconds = 600;
        const double Px = ZWorld.CrocPx;

        // the space each take needs round the animal's middle (m: ahead, behind, half width), measured on the 3.3 m model; with the
        // robot in the jaws for the bite, the roll and the drag under
        static (double ahead, double behind, double half) Space(ZCroc c)
        {
            switch (c.State)
            {
                case ZCrocState.Lunge: return c.Took ? (3.57, 1.80, 1.65) : (2.21, 1.80, 1.45);
                case ZCrocState.Snap: return (2.21, 1.80, 1.45);
                case ZCrocState.Thrash: return (3.24, 1.34, 1.82);
                case ZCrocState.Drag: return (3.22, 1.36, 1.45);
                case ZCrocState.Rise: return (1.58, 1.66, 1.34);
                case ZCrocState.Dive: return (1.76, 1.66, 1.33);
                case ZCrocState.Swim: return (1.72, 1.53, 1.36);
                default: return (1.65, 1.66, 1.35);
            }
        }

        /// <summary>The least free water (m) round the outline of one animal right now (8 points of its outline; the rule's own
        /// worst-case shore, see ZWorld.CrocShoreMetres).</summary>
        public static double OutlineFree(ZWorld w, ZCroc c)
        {
            var (ahead, behind, half) = Space(c);
            double hx = Math.Cos(c.Heading), hy = Math.Sin(c.Heading), least = 99;
            void P(double a, double s) { double f = w.CrocShoreMetres(c.X + (hx * a - hy * s) * Px, c.Y + (hy * a + hx * s) * Px); if (f < least) least = f; }
            P(ahead, 0); P(-behind, 0); P(0, half); P(0, -half);
            P(ahead * 0.7, half * 0.7); P(ahead * 0.7, -half * 0.7); P(-behind * 0.7, half * 0.7); P(-behind * 0.7, -half * 0.7);
            return least;
        }

        static bool Dry(ZWorld w, int tx, int ty) { var c = w.Map.ClassAt(tx, ty); return (c == ZClass.Ground || c == ZClass.Road) && w.Path.TilePassable(tx, ty, true); }
        static bool Wet(ZWorld w, int tx, int ty) => w.Map.ClassAt(tx, ty) == ZClass.Water;
        static void Run(ZWorld w, double seconds) { double end = w.Time + seconds; while (w.Time < end) { w.Tick(ZConst.TICK); w.Events.Clear(); } }
        static int Losses(IEnumerable<ZObject> squad) => squad.Count(o => o.Eaten);

        static ZWorld Bare(string settings, string bare, int seed, out ZObject dummy)
        {
            var w = OzV11Kit.World(settings, bare, seed);
            dummy = null;
            for (int ty = 2; ty < w.Map.height - 2 && dummy == null; ty++)
                for (int tx = 2; tx < w.Map.width - 2 && dummy == null; tx++)
                    if (Dry(w, tx, ty) && Dry(w, tx + 1, ty) && Dry(w, tx, ty + 1)) dummy = OzV11Kit.Dummy(w, tx * 16 + 8, ty * 16 + 8);
            return w;
        }

        /// <summary>The pool with the given tile in it: its attack water as a box and a centre (map px).</summary>
        static bool AttackBox(ZWorld w, int pool, out int x0, out int y0, out int x1, out int y1, out int cx, out int cy)
        {
            x0 = y0 = int.MaxValue; x1 = y1 = int.MinValue; long sx = 0, sy = 0, n = 0;
            for (int y = 2; y < w.Map.HeightPix; y += 4)
                for (int x = 2; x < w.Map.WidthPix; x += 4)
                    if (w.CrocAttackWater(x, y) && w.CrocPoolAt(x, y) == pool) { x0 = Math.Min(x0, x); x1 = Math.Max(x1, x); y0 = Math.Min(y0, y); y1 = Math.Max(y1, y); sx += x; sy += y; n++; }
            cx = n > 0 ? (int)(sx / n) : 0; cy = n > 0 ? (int)(sy / n) : 0;
            return n > 0;
        }

        public static string Run(string settings, Func<string, string> maps, int runs = SquadCrossings, bool longRun = true)
        {
            bool b0 = ZWorld.CrocodileDeviation, b1 = ZWorld.CrocHurryDeviation, b2 = ZWorld.CrocFleeDeviation, b3 = ZWorld.CrocWaterCostDeviation;
            var sb = new StringBuilder();
            int pass = 0, lines = 0;
            void V(bool ok, string what) { lines++; if (ok) pass++; sb.AppendLine(OzV11Kit.Verdict(ok, what)); }
            try
            {
                ZWorld.SetCrocodileRules(true, true, true, true, true);
                string map; try { map = maps("L16"); } catch (Exception) { map = null; }
                if (string.IsNullOrEmpty(map)) { V(false, "no grid found for level L16"); return sb.Append($"FAIL {pass}/{lines}").ToString(); }
                string bare = OzV11Kit.NoUnits(map);

                // ---- 1 the table
                var w0 = Bare(settings, bare, 1, out _);
                int ok = w0.Crocs.Count(c => w0.CrocFreeAt(c.X, c.Y) >= ZWorld.CrocTurnM - 1e-6);
                var poolsWith = w0.Crocs.Select(c => c.Pool).Distinct().OrderBy(p => p).ToList();
                sb.AppendLine("pool | crocodiles | attack water m2 | their places (tile) and kinds");
                foreach (int p in poolsWith)
                    sb.AppendLine($"{p} | {w0.Crocs.Count(c => c.Pool == p)} | {w0.CrocPoolAttackM2(p):0.#} | {string.Join(", ", w0.Crocs.Where(c => c.Pool == p).Select(c => $"{(int)c.X >> 4},{(int)c.Y >> 4} {(c.Kind == 0 ? "lurks" : c.Kind == 1 ? "swims" : "swims nose-only")}"))}");
                V(w0.Crocs.Count == ZCrocData.Places.Length && ok == w0.Crocs.Count && w0.CrocPlacesDropped == 0,
                  $"{ok} of {ZCrocData.Places.Length} crocodiles of the table stand where {ZWorld.CrocTurnM} m of water are free all round ({w0.CrocPlacesMoved} moved to the nearest such place, {w0.CrocPlacesDropped} left out); attack water in {poolsWith.Count(p => w0.CrocPoolAttackM2(p) > 0)} of their {poolsWith.Count} pools");

                // ---- 2 squad crossings of the west centre pool (the pool of place 3), across the short side of its attack water
                int crossPool = w0.Crocs.Count > 2 ? w0.Crocs[2].Pool : -1;
                int one = 0, two = 0, none = 0, hunts = 0, missB = 0, missA = 0, gLeft = 0, gTime = 0, gWay = 0; var perRun = new List<string>(); var perSet = new List<int>();
                if (crossPool >= 0 && AttackBox(w0, crossPool, out int ax0, out int ay0, out int ax1, out int ay1, out int acx, out int acy))
                {
                    // the way a squad crosses this pool: the pool's own short way, bank to bank, through the middle of its attack water
                    int bx0 = int.MaxValue, bx1 = int.MinValue, by0 = int.MaxValue, by1 = int.MinValue;
                    for (int ty = 0; ty < w0.Map.height; ty++) for (int tx = 0; tx < w0.Map.width; tx++)
                        if (w0.CrocPoolAt(tx * 16 + 8, ty * 16 + 8) == crossPool) { bx0 = Math.Min(bx0, tx); bx1 = Math.Max(bx1, tx); by0 = Math.Min(by0, ty); by1 = Math.Max(by1, ty); }
                    bool eastWest = bx1 - bx0 < by1 - by0;
                    for (int k = 0; k < runs; k++)
                    {
                        int j = k % CrossSet;                                                            // its place in its set: the line and the direction
                        if (j == 0) perSet.Add(0);
                        var w = Bare(settings, bare, 1 + k, out _);                                      // the seed runs on
                        int off = ((j % 5) - 2) * 8;                                                     // five lines 1 m apart
                        int lx = acx + (eastWest ? 0 : off), ly = acy + (eastWest ? off : 0);
                        // the two dry ends of the line through the pool
                        int sx = lx >> 4, sy = ly >> 4, ex = sx, ey = sy;
                        int dx = eastWest ? 1 : 0, dy = eastWest ? 0 : 1;
                        while (sx > 1 && sy > 1 && !(Dry(w, sx, sy) && Dry(w, sx - dx, sy - dy))) { sx -= dx; sy -= dy; }
                        while (ex < w.Map.width - 2 && ey < w.Map.height - 2 && !(Dry(w, ex, ey) && Dry(w, ex + dx, ey + dy))) { ex += dx; ey += dy; }
                        // from the bank on one side to a place 5 tiles inland on the other (a squad that is sent across, not one parked at the water)
                        int ix = sx - dx * 5, iy = sy - dy * 5, ox = ex + dx * 5, oy = ey + dy * 5;
                        Nearest(w, ref ix, ref iy); Nearest(w, ref ox, ref oy);
                        bool back = (j & 1) == 1;
                        int fx = (back ? ex : sx) * 16 + 8, fy = (back ? ey : sy) * 16 + 8, tx = (back ? ix : ox) * 16 + 8, ty = (back ? iy : oy) * 16 + 8;
                        var squad = OzV11Kit.Place(w, "grunt", fx, fy, Team.Red, 4);
                        var all = OzV11Kit.Squad(squad).ToList();
                        Run(w, 0.5 + 0.37 * k);                                                         // every crossing sets off at its own moment, later from set to set
                        // two orders in a row, as a player gives them to send a squad THROUGH the swamp: into the middle of the pool, then
                        // on to the far side (one order to the far side walks round the pool: with DV94 the swamp is no short cut)
                        w.SendWaypoints(Team.Red, squad, new[] { ZWaypoint.Move(lx, ly), ZWaypoint.Move(tx, ty) });
                        double end = w.Time + 150;
                        while (w.Time < end && all.Any(o => OzV11Kit.Alive(o) && (o.Waypoints.Count > 0 || o.IsMoving || Wet(w, o.CenterX >> 4, o.CenterY >> 4)))) { w.Tick(ZConst.TICK); w.Events.Clear(); }
                        Run(w, 6);
                        int lost = Losses(all);
                        if (lost == 1) { one++; perSet[perSet.Count - 1]++; } else if (lost >= 2) two++; else none++;
                        hunts += w.CrocHunts; missB += w.CrocMissBefore; missA += w.CrocMissAfter; gLeft += w.CrocGaveUpLeft; gTime += w.CrocGaveUpTime; gWay += w.CrocGaveUpNoWay;
                        perRun.Add(lost + (w.CrocHunts > 0 ? "" : "-") + (lost > 1 ? "[" + string.Join(" ", w.CrocNotes.Where(n => n.what == ZCrocNote.Bite || (n.what == ZCrocNote.State && n.state == ZCrocState.Warn)).Select(n => (n.what == ZCrocNote.Bite ? "taken " : "hunt ") + n.time.ToString("0.0") + "s@" + ((int)n.x >> 4) + "," + ((int)n.y >> 4))) + "]" : ""));
                    }
                    sb.AppendLine($"crossings of pool {crossPool} ({(eastWest ? "east-west" : "north-south")} through tile {acx >> 4},{acy >> 4}; attack water {w0.CrocPoolAttackM2(crossPool):0.#} m2), robots lost per crossing (- = no hunt began), in sets of {CrossSet} (the same five lines, both ways, in every set; the start moment and the seed run on):");
                    for (int s0 = 0; s0 < perRun.Count; s0 += CrossSet)
                    {
                        int n0 = Math.Min(CrossSet, perRun.Count - s0);
                        sb.AppendLine($"  set {s0 / CrossSet + 1} | {string.Join(" ", perRun.Skip(s0).Take(n0))} | {perSet[s0 / CrossSet]} of {n0} lose one robot");
                    }
                    sb.AppendLine($"  hunts {hunts}, gave up before the rise {missB} (the robot was out of the attack water {gLeft}, the {ZWorld.CrocStalkLimit:0} s were up {gTime}, no way on {gWay}), missed after it {missA}");
                    int lo = CrossMin * runs / SquadCrossings, hi = (CrossMax * runs + SquadCrossings - 1) / SquadCrossings;
                    V(one >= lo && one <= hi, $"{one} of {runs} squad crossings lose one robot (wanted {lo} to {hi} of {runs}: the plan's 8 to 14 of 20; the sets of {CrossSet}: {string.Join(", ", perSet)}); {none} lose none");
                    V(two == 0, $"{two} crossings lose two robots or more (wanted 0: one robot per attack, then {ZWorld.CrocCoolKillMin:0} to {ZWorld.CrocCoolKillMax:0} s of peace in that pool)");
                }
                else { V(false, "the west centre pool has no attack water: the crossings cannot be run (did the level data change?)"); V(false, "(no crossings)"); }

                // ---- 3 idle robots two tiles from a bank
                var spots = new List<(int x, int y)>();
                for (int ty = 2; ty < w0.Map.height - 2; ty++)
                    for (int tx = 2; tx < w0.Map.width - 2; tx++)
                    {
                        int px = tx * 16 + 8, py = ty * 16 + 8;
                        if (!Wet(w0, tx, ty) || !w0.CrocAttackWater(px, py) || !w0.Crocs.Any(c => c.Pool == w0.CrocPoolAt(px, py))) continue;
                        bool ring1 = false, ring2 = false;
                        for (int j = -2; j <= 2; j++) for (int i = -2; i <= 2; i++) if (Dry(w0, tx + i, ty + j)) { if (Math.Max(Math.Abs(i), Math.Abs(j)) == 1) ring1 = true; else ring2 = true; }
                        if (ring2 && !ring1) spots.Add((px, py));
                    }
                int reached = 0, eaten = 0, stayed = 0, fled = 0, started = 0; var idleLog = new List<string>();
                for (int k = 0; k < IdleRobots && spots.Count > 0; k++)
                {
                    var (px, py) = spots[(int)((long)k * spots.Count / IdleRobots)];
                    var w = Bare(settings, bare, 31 + k, out _);
                    var r = OzV11Kit.Place(w, "grunt", px, py, Team.Red, 1);
                    Run(w, ZWorld.CrocWetSeconds + 0.3 + 0.07 * k);
                    if (w.CrocStartHuntForTest(r) || w.CrocHunts > 0) started++;
                    double end = w.Time + 40;
                    while (w.Time < end && OzV11Kit.Alive(r) && (w.CrocHuntsActive > 0 || r.Waypoints.Count > 0)) { w.Tick(ZConst.TICK); w.Events.Clear(); }
                    Run(w, 1);
                    bool dry = OzV11Kit.Alive(r) && !Wet(w, r.CenterX >> 4, r.CenterY >> 4);
                    if (dry) reached++; else if (r.Eaten) eaten++; else stayed++;
                    fled += w.CrocFlees;
                    idleLog.Add((dry ? "bank" : r.Eaten ? "taken" : "in the water") + $"@{px >> 4},{py >> 4}");
                }
                sb.AppendLine($"idle robots, two tiles from a bank ({spots.Count} such tiles in attack water): {string.Join(" ", idleLog)}");
                V(spots.Count > 0 && started == IdleRobots && reached >= IdleMin, $"{reached} of {IdleRobots} idle robots two tiles from a bank reach it (wanted {IdleMin} or more); {eaten} taken, {stayed} still in the water; hunts started {started}, walks to the bank begun {fled}");

                // ---- 4 hold position stays; an order is kept and the robot hurries
                {
                    var deep = spots.Count > 0 ? spots[spots.Count / 2] : (0, 0);
                    var w = Bare(settings, bare, 77, out _);
                    var r = OzV11Kit.Place(w, "grunt", deep.Item1, deep.Item2, Team.Red, 1);
                    w.SendHold(Team.Red, r);
                    int hx = r.CenterX, hy = r.CenterY;
                    Run(w, ZWorld.CrocWetSeconds + 0.3);
                    bool st = w.CrocHuntsActive > 0 || w.CrocStartHuntForTest(r);                     // a hunt on it: the rule's own (it may have begun while the robot stood wet) or started here
                    double moved = 0; double end = w.Time + 20;
                    while (w.Time < end && OzV11Kit.Alive(r) && w.CrocHuntsActive > 0) { w.Tick(ZConst.TICK); w.Events.Clear(); moved = Math.Max(moved, Math.Sqrt((double)(r.CenterX - hx) * (r.CenterX - hx) + (double)(r.CenterY - hy) * (r.CenterY - hy))); }
                    V(st && moved < 1.0 && w.CrocFlees == 0, $"a robot on hold position in attack water stays where it is while it is hunted (moved {moved / Px:0.00} m, walks to the bank begun {w.CrocFlees}; it was {(r.Eaten ? "taken" : "missed")})");
                }
                {
                    var w = Bare(settings, bare, 78, out _);
                    bool had = AttackBox(w, crossPool, out int bx0, out int by0, out int bx1, out int by1, out int bcx, out int bcy);
                    int sx = bcx >> 4, sy = bcy >> 4, ex = sx; while (sx > 1 && !(Dry(w, sx, sy) && Dry(w, sx - 1, sy))) sx--; while (ex < w.Map.width - 2 && !(Dry(w, ex, sy) && Dry(w, ex + 1, sy))) ex++;
                    var r = OzV11Kit.Place(w, "grunt", sx * 16 + 8, sy * 16 + 8, Team.Red, 1);
                    OzV11Kit.PlayerMove(w, r, ex * 16 + 8, sy * 16 + 8, false);
                    double plain = 0, hurry = 0, stam0 = -1, stamMin = 99; bool kept = true; int waters = 0;
                    double end = w.Time + 150;
                    while (w.Time < end && OzV11Kit.Alive(r) && r.Waypoints.Count > 0)
                    {
                        w.Tick(ZConst.TICK); w.Events.Clear();
                        if (!Wet(w, r.CenterX >> 4, r.CenterY >> 4) || !r.IsMoving) continue;
                        waters++;
                        if (waters == 300 && w.CrocHuntsActive == 0) w.CrocStartHuntForTest(r);       // 3 s in: a hunt on it, wherever it wades
                        if (r.CrocHurries) { hurry = Math.Max(hurry, r.RealMoveSpeed); if (stam0 < 0) stam0 = r.Stamina; stamMin = Math.Min(stamMin, r.Stamina); }
                        else if (hurry <= 0) plain = Math.Max(plain, r.RealMoveSpeed);
                        if (r.Waypoints.Count > 0 && !r.Waypoints[0].playerGiven) kept = false;
                    }
                    double ratio = plain > 0 ? hurry / plain : 0;
                    V(had && kept && w.CrocFlees == 0 && Math.Abs(ratio - ZWorld.CrocHurryFactor) < 0.02 && (stam0 < 0 || stamMin >= stam0 - 1e-9),
                      $"a robot on an order keeps its order (walks to the bank begun {w.CrocFlees}) and wades {ratio:0.00} times as fast within {ZWorld.CrocHurryM:0} m of the hunt (wanted {ZWorld.CrocHurryFactor}; {plain / Px:0.00} -> {hurry / Px:0.00} m/s), stamina untouched");
                }

                // ---- 5 a whole match
                if (longRun)
                {
                    var w = ZWorld.Create(settings, map, 1);
                    w.Bots.Add(new ZBot(w, Team.Blue, 102)); w.Bots.Add(new ZBot(w, Team.Red, 1));
                    int samples = 0, near = 0, maxHunts = 0, twoInOne = 0; double least = 99; string where = "";
                    var taken = new Dictionary<int, int>(); double nextSample = 0.5; int notes = 0;
                    while (w.Time < LongSeconds)
                    {
                        w.Tick(ZConst.TICK); w.Events.Clear();
                        maxHunts = Math.Max(maxHunts, w.CrocHuntsActive);
                        for (; notes < w.CrocNoteCount; notes++)
                        {
                            int back = w.CrocNoteCount - notes; if (back > w.CrocNotes.Count) continue;
                            var n = w.CrocNotes[w.CrocNotes.Count - back];
                            if (n.what == ZCrocNote.State && n.state == ZCrocState.Warn) taken[n.croc] = 0;
                            if (n.what == ZCrocNote.Bite) { taken.TryGetValue(n.croc, out int t); taken[n.croc] = t + 1; if (t + 1 > 1) twoInOne++; }
                        }
                        if (w.Time < nextSample) continue;
                        nextSample += 0.5;
                        foreach (var c in w.Crocs)
                        {
                            if (c.Hidden) continue;
                            samples++;
                            double f = OutlineFree(w, c);
                            if (f < least) { least = f; where = $"{c.State} at tile {(int)c.X >> 4},{(int)c.Y >> 4} at {w.Time:0} s"; }
                            if (f < BankM - 1e-6) near++;
                        }
                    }
                    sb.AppendLine($"whole match, {LongSeconds / 60:0} minutes of game time, both computer players: hunts {w.CrocHunts}, robots taken {w.CrocKills}, gave up before the rise {w.CrocMissBefore}, missed after it {w.CrocMissAfter}, scared off by a blast {w.CrocScared}, came up at another place {w.CrocMoved}; robots that left the water by themselves {w.CrocFlees}; game {(w.GameOver ? "won by " + w.Winner : "not over")}");
                    V(near == 0 && samples > 0, $"{near} of {samples} samples (every 0.5 s) with a part of an animal nearer than {BankM} m to a bank or on land (wanted 0); the least free water round an outline was {least:0.00} m ({where})");
                    V(twoInOne == 0 && maxHunts <= ZWorld.CrocMaxHunts, $"{twoInOne} hunts took a second robot (wanted 0); most hunts at once {maxHunts} (at most {ZWorld.CrocMaxHunts})");
                }

                // ---- 6 levels 2 and 17: the same game with the switches off and on
                foreach (var lv in new[] { "L02", "L17" })
                {
                    string m2; try { m2 = maps(lv); } catch (Exception) { m2 = null; }
                    if (string.IsNullOrEmpty(m2)) { V(false, $"no grid found for level {lv}"); continue; }
                    double secs = longRun ? SameSeconds : 120;
                    ZWorld.SetCrocodileRules(true, false, false, false, false);
                    ulong a = Checksum(settings, m2, lv == "L17", secs, out string endA);
                    ZWorld.SetCrocodileRules(true, true, true, true, true);
                    ulong b = Checksum(settings, m2, lv == "L17", secs, out string endB);
                    V(a == b, $"{lv}, computer against computer, {secs:0} s: {(a == b ? "the same game" : "DIFFERENT games")} with the four switches off and on (state sum every tick {a:X16} / {b:X16}; {endA} / {endB})");
                }

                // ---- 7 the swamp is no short cut
                {
                    ZWorld.SetCrocodileRules(true, false, false, false, false);                      // the routes alone: no animal in the way
                    var off = Routes(settings, bare, out string offText);
                    ZWorld.SetCrocodileRules(true, false, false, false, true);
                    var on = Routes(settings, bare, out string onText);
                    ZWorld.SetCrocodileRules(true, true, true, true, true);
                    int fewer = 0, more = 0; for (int i = 0; i < off.Count && i < on.Count; i++) { if (on[i] < off[i]) fewer++; else if (on[i] > off[i]) more++; }
                    sb.AppendLine("five robot routes, swamp tiles on the way / length in tiles: switch off " + offText + "; switch on " + onText);
                    V(off.Count == 5 && on.Count == 5 && more == 0 && fewer >= 1, $"with the switch on {fewer} of 5 fixed routes cross fewer swamp tiles and {more} cross more (wanted: at least 1 fewer, 0 more)");
                    V(IdleFlag(settings, bare, out string flagText), flagText);
                }

                // ---- 8 the switch off
                {
                    ZWorld.SetCrocodileRules(true, false, false, false, false);
                    var w = Bare(settings, bare, 5, out _);
                    var spot = spots.Count > 0 ? spots[0] : (0, 0);
                    var r = OzV11Kit.Place(w, "grunt", spot.Item1, spot.Item2, Team.Red, 1);
                    Run(w, 60);
                    V(w.Crocs.Count == 0 && OzV11Kit.Alive(r) && w.CrocHunts == 0, $"with the switch off level 16 has {w.Crocs.Count} crocodiles and a robot standing 60 s in the swamp is {(OzV11Kit.Alive(r) ? "alive" : "gone")}");
                }
            }
            catch (Exception e) { V(false, "the check threw " + e.GetType().Name + ": " + e.Message + " " + (e.StackTrace ?? "").Split('\n').FirstOrDefault()); }
            finally { ZWorld.CrocodileDeviation = b0; ZWorld.CrocHurryDeviation = b1; ZWorld.CrocFleeDeviation = b2; ZWorld.CrocWaterCostDeviation = b3; }
            sb.Append(pass == lines ? $"PASS {pass}/{lines}" : $"FAIL {pass}/{lines}");
            return sb.ToString();
        }

        /// <summary>A sum over every object's place, health and owner at every tick of a computer-against-computer game.</summary>
        static ulong Checksum(string settings, string map, bool parked, double seconds, out string end)
        {
            var w = ZWorld.Create(settings, map, 1, parked);
            w.Bots.Add(new ZBot(w, Team.Blue, 102)); w.Bots.Add(new ZBot(w, Team.Red, 1));
            ulong h = 1469598103934665603UL;
            while (w.Time < seconds && !w.GameOver)
            {
                w.Tick(ZConst.TICK); w.Events.Clear();
                foreach (var o in w.Objects)
                {
                    h = (h ^ (ulong)(uint)o.Id) * 1099511628211UL; h = (h ^ (ulong)(uint)o.LocX) * 1099511628211UL; h = (h ^ (ulong)(uint)o.LocY) * 1099511628211UL;
                    h = (h ^ (ulong)(uint)o.Health) * 1099511628211UL; h = (h ^ (ulong)(uint)(int)o.Owner) * 1099511628211UL;
                }
            }
            end = $"{w.Objects.Count} objects at {w.Time:0} s";
            return h;
        }

        /// <summary>Five routes of one grunt across level 16's waters: swamp tiles on the path the game's own search gives.</summary>
        static List<int> Routes(string settings, string bare, out string text)
        {
            var res = new List<int>(); var parts = new List<string>();
            // (from tile, to tile): bank to bank round the north-west lake, the north-east swamp, the west centre pool, the east pool, across the bridge basin
            var pairs = new[] { (12, 18, 35, 18), (68, 14, 103, 14), (12, 66, 41, 66), (87, 57, 103, 57), (60, 70, 90, 73) };
            foreach (var (fx0, fy0, tx0, ty0) in pairs)
            {
                var w = Bare(settings, bare, 3, out _);
                int fx = fx0, fy = fy0, tx = tx0, ty = ty0;
                Nearest(w, ref fx, ref fy); Nearest(w, ref tx, ref ty);
                var r = OzV11Kit.Place(w, "grunt", fx * 16 + 8, fy * 16 + 8, Team.Red, 1);
                OzV11Kit.PlayerMove(w, r, tx * 16 + 8, ty * 16 + 8, false);
                int water = 0, steps = 0, lastT = -1; double end = w.Time + 400;
                while (w.Time < end && OzV11Kit.Alive(r) && r.Waypoints.Count > 0)
                {
                    w.Tick(ZConst.TICK); w.Events.Clear();
                    int t = (r.CenterY >> 4) * 1000 + (r.CenterX >> 4);
                    if (t != lastT) { lastT = t; steps++; if (Wet(w, r.CenterX >> 4, r.CenterY >> 4)) water++; }
                }
                res.Add(water); parts.Add($"{water} / {steps}{(OzV11Kit.Alive(r) ? "" : " (lost)")}");
            }
            text = string.Join(", ", parts);
            return res;
        }

        static void Nearest(ZWorld w, ref int tx, ref int ty)
        {
            for (int r = 0; r < 12; r++)
                for (int j = -r; j <= r; j++)
                    for (int i = -r; i <= r; i++)
                        if (Math.Max(Math.Abs(i), Math.Abs(j)) == r && Dry(w, tx + i, ty + j)) { tx += i; ty += j; return; }
        }

        /// <summary>An idle robot within the flag distance of a flag that is not its own, the straight line to it across more than two
        /// swamp tiles: with the switch off it walks off to the flag by itself, with it on it stays.</summary>
        static bool IdleFlag(string settings, string bare, out string text)
        {
            ZWorld.SetCrocodileRules(true, true, true, true, false);
            var probe = Bare(settings, bare, 9, out _);
            int reach = probe.S.auto_grab_flag_distance;
            foreach (var f in probe.Flags)
            {
                int ftx = f.CenterX >> 4, fty = f.CenterY >> 4, rt = reach / 16;
                for (int j = -rt; j <= rt; j++)
                    for (int i = -rt; i <= rt; i++)
                    {
                        int tx = ftx + i, ty = fty + j;
                        if (!Dry(probe, tx, ty) || Math.Sqrt((double)(i * i + j * j)) * 16 > reach - 24 || Math.Max(Math.Abs(i), Math.Abs(j)) < 5) continue;
                        int water = ZPathFinder.Bresenham(tx, ty, ftx, fty, probe.Map.width, probe.Map.height).Count(t => Wet(probe, t.x, t.y));
                        if (water <= ZWorld.CrocSwampTiles) continue;
                        bool Walks(bool on)
                        {
                            ZWorld.SetCrocodileRules(true, false, false, false, on);
                            var w = Bare(settings, bare, 9, out _);
                            var r = OzV11Kit.Place(w, "grunt", tx * 16 + 8, ty * 16 + 8, Team.Red, 1);
                            double end = w.Time + 25;                                                     // past the calm start
                            while (w.Time < end && r.Waypoints.Count == 0) { w.Tick(ZConst.TICK); w.Events.Clear(); }
                            return r.Waypoints.Count > 0;
                        }
                        if (!Walks(false)) continue;                                                     // the game itself would not send it (sight, reach): another spot
                        bool on2 = Walks(true);
                        ZWorld.SetCrocodileRules(true, true, true, true, true);
                        text = $"an idle robot at tile {tx},{ty}, {water} swamp tiles on the straight line to the flag at {ftx},{fty}: with the switch off it sets off for the flag by itself, with it on it {(on2 ? "STILL SETS OFF" : "stays")}";
                        return !on2;
                    }
            }
            ZWorld.SetCrocodileRules(true, true, true, true, true);
            text = "no spot found on level 16 where an idle robot would wade more than two swamp tiles to a flag by itself (nothing to measure)";
            return false;
        }

#if UNITY_EDITOR
        [UnityEditor.MenuItem("OpenZ/L16 Jungle/Checks/Count Crocodile Rule (J8)")]
        static void Menu() => OzV11Kit.RunMenu("J8 crocodile rule", () => Run(OzV11Kit.SettingsText, OzV11Kit.MapText));
        [UnityEditor.MenuItem("OpenZ/L16 Jungle/Checks/Count Crocodile Rule, short (J8: no whole match)")]
        static void MenuShort() => OzV11Kit.RunMenu("J8 crocodile rule, short", () => Run(OzV11Kit.SettingsText, OzV11Kit.MapText, SquadCrossings, false));
#endif
    }
}
