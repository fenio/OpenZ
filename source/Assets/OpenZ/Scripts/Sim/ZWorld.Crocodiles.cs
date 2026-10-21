using System;
using System.Collections.Generic;

namespace OpenZ.Sim
{
    /// <summary>What a crocodile is doing (the view picks its take and its signs on the water from this).</summary>
    public enum ZCrocState
    {
        Lurk,       // waits: eye hoods above the water
        Swim,       // a slow pass along its own stretch of water (decoration, 1.5 m/s)
        Warn,       // the warning: stands, the nose comes up, growl and bubbles
        Stalk,      // nose only, 4.5 m/s, the V wake
        Rise,       // 0.6 s: head and back burst up
        Lunge,      // 1.0 s: the bite falls 0.47 s in
        Thrash,     // 2.4 s: two rolls with the robot
        Drag,       // 1.33 s: under, tail last
        Watch,      // gave up before the rise: stands and watches for 1.0 s
        Snap,       // missed after the rise: snaps at the air for the rest of the lunge
        Dive,       // 1.2 s: sinks out of sight
        Under       // not to be seen; comes up again at its own place
    }

    /// <summary>One crocodile of the level (ZWorld.Crocodiles.cs). Positions are map px (8 px = 1 m) of its middle on the water surface;
    /// the heading is the angle of its nose in the map plane (radians, 0 = east, +y = south).</summary>
    public sealed class ZCroc
    {
        public int Index, Pool, Kind;                 // Kind: 0 lurks at its place, 1 swims its stretch with head and back showing, 2 the same nose-only
        public double X, Y, Heading, Speed;           // Speed px/s along the heading (0 = standing)
        public double HomeX, HomeY, HomeHeading;
        public ZCrocState State;
        public double Since, Until;                   // game time the state began / ends
        public int PreyId = -1;
        public bool Took;                             // this hunt took its robot (the view hangs it in the jaws from the bite on)
        public double BiteAt = -1;
        internal double[] route;                      // x, y pairs of its stretch (px); null = stays at its place
        internal int leg, legDir = 1;
        internal double slid, stalkEnd, aimNext; internal int aimSide = -1;
        /// <summary>Warning, stalk, rise, lunge or roll: what robots near it react to.</summary>
        public bool Hunting => State == ZCrocState.Warn || State == ZCrocState.Stalk || State == ZCrocState.Rise || State == ZCrocState.Lunge || State == ZCrocState.Thrash;
        public bool Busy => State != ZCrocState.Lurk && State != ZCrocState.Swim;
        public bool Hidden => State == ZCrocState.Under;
        public double HomeDist(double x, double y) => Math.Sqrt((x - HomeX) * (x - HomeX) + (y - HomeY) * (y - HomeY));
    }

    /// <summary>A line of the crocodile log the view reads once (ZWorld.CrocNotes): a state change or one of the moments below.</summary>
    public struct ZCrocNote
    {
        public const byte State = 0, Bite = 1, Moved = 2, Scared = 3, Hurry = 4, Flee = 5, Bank = 6;
        public int croc, robot; public ZCrocState state; public double time; public float x, y; public byte what;
    }

    /// <summary>
    /// DV91 to DV94, level 16 "Jungle": crocodiles in the swamp. The original engine has no crocodile (refs/zod_engine/src: the only
    /// hits are the missile cannon's "crockets"; the 1996 game's bite sound ships unused), so everything here is a deliberate OpenZ rule,
    /// built like the run-over rule (ZWorld.RunOver.cs): its own switches, its own clock, counters, no random call.
    /// <para>DV91 THE BITE. Crocodiles live at fixed places of the level table (ZCrocData) and only ever on water that is theirs:
    /// "free water" of a point = its distance to the nearest tile that is not swamp (anything that closes a tile in the water counts as
    /// shore: bridge, ruin, rock, tree) less 1 m for the rounded pool outlines, and never more than the distance to the nearest place
    /// the level draws as dry (ZCrocData's mask). The animal's middle moves only where 2.4 m are free (its turning circle of 1.77 m
    /// plus 0.6 m of water). A robot (never a vehicle, never on a bridge) that has waded for CrocWetSeconds without a break and stands in
    /// ATTACK WATER can be picked: once a second per pool, one chance in four, one hunt per pool and CrocMaxHunts on the map. Attack
    /// water = a place where the whole attack fits: the box 5.05 m ahead, 2.45 m behind and 2.45 m either side of the animal, turned
    /// the way it faces, wholly on water, with the robot 2.08 m ahead of its middle. The hunt: the warning (1 s or more: first sign to
    /// bite is never under 3.75 s), the nose-only stalk at 4.5 m/s (it turns away from banks and gives up after 6 s, or the moment the
    /// robot is out of attack water), the rise (0.6 s) once the robot is within 2.7 m and 25 degrees and the box fits, the lunge:
    /// 0.47 s in, the robot within the jaws' reach is taken: health 0, one robot per attack. Then the roll (2.4 s), the drag under
    /// (1.33 s) and CrocCoolKill seconds of peace in that pool (10 s after a miss, 20 s when a blast within 4 m scared it off).
    /// It cannot be shot. Both teams and the computer player are prey alike.</para>
    /// <para>DV92 ROBOTS HURRY: a robot on a water tile within CrocHurryM of a hunting crocodile wades CrocHurryFactor times as fast (and
    /// 2 s longer), stamina untouched (ZObject.RunSpeed).</para>
    /// <para>DV93 ROBOTS LEAVE: a single robot or squad leader on a water tile with no order, not holding position, walks to the nearest
    /// dry tile when a crocodile hunts within CrocFleeM or has picked its squad, and starts nothing by itself until the hunt is over
    /// (+3 s, 20 s at most); any order ends that (ZObject.CrocFleeStep, CheckPassiveEngage).</para>
    /// <para>DV94 THE SWAMP IS NO SHORT CUT: on this level a swamp tile weighs twice in the robots' path search, robots do not wade
    /// round a bridge while a way over it exists, and an idle robot starts no walk of its own (flag, empty vehicle, crate) whose
    /// straight line crosses more than CrocSwampTiles swamp tiles.</para>
    /// Deterministic: game time and tick count, map px, object order, a hash of ids and ticks where the design says "chance".
    /// OFF (each switch by itself; all off = the level without crocodiles) is the headless default; ZGame turns them on at every start.
    /// Levels without a table (2 and 17) never run a line of this.
    /// </summary>
    public sealed partial class ZWorld
    {
        public static bool CrocodileDeviation, CrocHurryDeviation, CrocFleeDeviation, CrocWaterCostDeviation;
        /// <summary>ZGame, at every start: 'Zod exact' turns all four off; each has its own PlayerPrefs switch.</summary>
        public static void SetCrocodileRules(bool game, bool bite, bool hurry, bool flee, bool waterCost)
        { CrocodileDeviation = game && bite; CrocHurryDeviation = game && bite && hurry; CrocFleeDeviation = game && bite && flee; CrocWaterCostDeviation = game && waterCost; }

        // ------------------------------------------------------------------ numbers (metres, seconds; 8 px = 1 m)
        public const double CrocPx = 8.0;
        public const double CrocWobbleM = 1.0, CrocTurnM = 2.4, CrocTravelM = 1.95, CrocRouteM = 2.76, CrocDrawnSlackM = 0.36;
        public const double CrocBoxAheadM = 5.05, CrocBoxBehindM = 2.45, CrocBoxSideM = 2.45, CrocRobotAheadM = 2.08, CrocSlideM = 0.9;
        public const double CrocWetSeconds = 3.0, CrocCheckEvery = 1.0, CrocStartMinM = 12.0, CrocStartMaxM = 16.0;
        public const int CrocStartChance = 25, CrocMaxHunts = 2;
        public const double CrocWarnSeconds = 1.0, CrocFirstSignSeconds = 3.75, CrocStalkMps = 4.5, CrocSlowMps = 1.5, CrocStalkTurnDeg = 90, CrocTurnDeg = 120, CrocLungeTurnDeg = 60, CrocStalkLimit = 6.0;
        public const double CrocRiseM = 2.7, CrocRiseDeg = 25, CrocRiseSeconds = 0.6, CrocBiteSeconds = 0.47, CrocLungeSeconds = 1.0, CrocThrashSeconds = 2.4, CrocDragSeconds = 1.33, CrocDiveSeconds = 1.2, CrocWatchSeconds = 1.0;
        public const double CrocReachAlongM = 0.7, CrocReachNearM = 1.2, CrocReachSideM = 0.9, CrocSwitchM = 8;
        public const double CrocCoolKillMin = 30, CrocCoolKillMax = 40, CrocCoolMiss = 10, CrocCoolScared = 20, CrocScareM = 4.0, CrocBackAfter = 15, CrocBackAfterMiss = 4;
        public const double CrocHurryM = 14.0, CrocHurryFactor = 1.3, CrocHurryAfter = 2.0;
        public const double CrocFleeM = 16.0, CrocFleeKeepM = 6.0, CrocWaryAfter = 3.0, CrocWaryMax = 20.0; public const int CrocFleeTiles = 24;
        public const float CrocWaterCost = 2f; public const int CrocSwampTiles = 2;
        public const double CrocSwimMps = 1.5, CrocRestMin = 6, CrocRestMax = 14;

        // ------------------------------------------------------------------ state
        readonly List<ZCroc> crocs = new List<ZCroc>();
        public IReadOnlyList<ZCroc> Crocs => crocs;
        /// <summary>The log for the view: the last 256 lines; CrocNoteCount = lines written so far (a reader keeps its own count).</summary>
        public readonly List<ZCrocNote> CrocNotes = new List<ZCrocNote>();
        public int CrocNoteCount { get; private set; }
        public int CrocHunts { get; private set; }
        public int CrocKills { get; private set; }
        public int CrocMissBefore { get; private set; }
        public int CrocMissAfter { get; private set; }
        /// <summary>Why it gave up before the rise: the robot left the attack water (or the water), the 6 s were up, no way on.</summary>
        public int CrocGaveUpLeft { get; private set; }
        public int CrocGaveUpTime { get; private set; }
        public int CrocGaveUpNoWay { get; private set; }
        /// <summary>Stalks that went on with another robot of the attack water.</summary>
        public int CrocSwitches { get; private set; }
        public int CrocScared { get; private set; }
        public int CrocMoved { get; private set; }
        public int CrocFlees { get; private set; }
        public int CrocBanks { get; private set; }
        public int CrocPlacesMoved { get; private set; }
        public int CrocPlacesDropped { get; private set; }
        public int CrocHuntsActive { get; private set; }
        /// <summary>Checks: the least free water (m) under the middle of any animal since the start.</summary>
        public double CrocLeastFree { get; private set; } = 99;
        int cw, ch; float[] crocFree; bool[] crocAttack; int[] crocPoolOfTile; int crocPools;
        double[] crocPoolCool; int[] crocPoolHunt; List<int>[] crocPoolTurnCells; double[] crocPoolAttackM2;
        double nextCrocCheck, nextCrocBeat; bool crocMapKnown, crocMap;

        /// <summary>This map has a crocodile table (level 16), whatever the switches say.</summary>
        public bool CrocMap { get { if (!crocMapKnown) { crocMapKnown = true; crocMap = Map != null && ZCrocData.For(Map.file) != null; } return crocMap; } }
        /// <summary>DV94: the weight of a swamp tile in the robots' path search on this map (ZWorld.InitPathfinding).</summary>
        float CrocPathWaterCost() => CrocWaterCostDeviation && CrocMap ? CrocWaterCost : 1f;
        /// <summary>DV94: robots do not wade round a bridge while a way over it exists (ZPathing.BridgePath).</summary>
        public bool CrocKeepsToBridge => CrocWaterCostDeviation && CrocMap;
        public int CrocPoolCount => crocPools;
        public double CrocPoolAttackM2(int pool) => crocPoolAttackM2 != null && pool >= 0 && pool < crocPools ? crocPoolAttackM2[pool] : 0;
        public double CrocPoolCoolUntil(int pool) => crocPoolCool != null && pool >= 0 && pool < crocPools ? crocPoolCool[pool] : 0;
        public int CrocPoolAt(int px, int py) { int tx = px >> 4, ty = py >> 4; return crocPoolOfTile == null || tx < 0 || ty < 0 || tx >= Map.width || ty >= Map.height ? -1 : crocPoolOfTile[ty * Map.width + tx]; }

        static uint CrocHash(uint a, uint b, uint c)
        {
            uint h = unchecked(a * 2654435761u ^ b * 40503u ^ c * 374761393u);
            h ^= h >> 15; h = unchecked(h * 2246822519u); h ^= h >> 13; h = unchecked(h * 3266489917u); h ^= h >> 16;
            return h;
        }

        void CrocNote(byte what, ZCroc c, int robot = -1, double x = double.NaN, double y = double.NaN)
        {
            if (CrocNotes.Count >= 256) CrocNotes.RemoveAt(0);
            CrocNotes.Add(new ZCrocNote { what = what, croc = c != null ? c.Index : -1, robot = robot, state = c != null ? c.State : ZCrocState.Lurk, time = Time,
                                          x = (float)(double.IsNaN(x) ? (c != null ? c.X : 0) : x), y = (float)(double.IsNaN(y) ? (c != null ? c.Y : 0) : y) });
            CrocNoteCount++;
        }

        void CrocSet(ZCroc c, ZCrocState s, double seconds = 0)
        {
            c.State = s; c.Since = Time; c.Until = Time + seconds;
            if (s != ZCrocState.Stalk && s != ZCrocState.Swim && s != ZCrocState.Rise) c.Speed = 0;
            CrocNote(ZCrocNote.State, c, c.PreyId);
        }

        // ------------------------------------------------------------------ the water that is theirs
        /// <summary>ZWorld.Create, after every map object stands: free water per 0.5 m cell, pools, attack water, the animals.</summary>
        void InitCrocodiles()
        {
            crocs.Clear(); crocFree = null; crocAttack = null; crocPools = 0;
            if (!CrocodileDeviation || !CrocMap) return;
            var places = ZCrocData.For(Map.file);
            int w = Map.width, h = Map.height; cw = w * 4; ch = h * 4;
            var water = new bool[w * h];
            for (int j = 0; j < h; j++) for (int i = 0; i < w; i++) water[j * w + i] = Map.ClassAt(i, j) == ZClass.Water && Path.TilePassable(i, j, true);
            var drawn = ZCrocData.DrawnWater(cw, ch);
            crocFree = new float[cw * ch];
            for (int k = 0; k < crocFree.Length; k++) crocFree[k] = -100f;
            const int reach = 5, dreach = 8;
            var near = new List<int>(128);
            // the ring of cell steps for the drawn bank, nearest first (ties in a fixed order)
            var ring = new List<(int d2, int dj, int di)>();
            for (int dj = -dreach; dj <= dreach; dj++) for (int di = -dreach; di <= dreach; di++) ring.Add((dj * dj + di * di, dj, di));
            ring.Sort((a, b) => a.d2 != b.d2 ? a.d2.CompareTo(b.d2) : a.dj != b.dj ? a.dj.CompareTo(b.dj) : a.di.CompareTo(b.di));
            for (int ty = 0; ty < h; ty++)
                for (int tx = 0; tx < w; tx++)
                {
                    if (!water[ty * w + tx]) continue;
                    near.Clear();
                    for (int dy = -reach; dy <= reach; dy++)
                        for (int dx = -reach; dx <= reach; dx++)
                        {
                            int x = tx + dx, y = ty + dy;
                            if (x < 0 || y < 0 || x >= w || y >= h || !water[y * w + x]) { near.Add(x); near.Add(y); }
                        }
                    for (int b = 0; b < 4; b++)
                        for (int a = 0; a < 4; a++)
                        {
                            double cx = tx * 2.0 + 0.25 + 0.5 * a, cy = ty * 2.0 + 0.25 + 0.5 * b, best = reach * 2.0;
                            for (int k = 0; k < near.Count; k += 2)
                            {
                                double x0 = near[k] * 2.0, y0 = near[k + 1] * 2.0;
                                double ddx = Math.Max(Math.Max(x0 - cx, cx - (x0 + 2.0)), 0.0), ddy = Math.Max(Math.Max(y0 - cy, cy - (y0 + 2.0)), 0.0);
                                double d = Math.Sqrt(ddx * ddx + ddy * ddy);
                                if (d < best) best = d;
                            }
                            double f = best - CrocWobbleM;
                            int ci = tx * 4 + a, cj = ty * 4 + b;
                            if (drawn != null)
                            {
                                double dd = dreach * 0.5;
                                for (int k = 0; k < ring.Count; k++)
                                {
                                    int jj = cj + ring[k].dj, ii = ci + ring[k].di;
                                    if (jj < 0 || ii < 0 || jj >= ch || ii >= cw || !drawn[jj * cw + ii]) { dd = Math.Sqrt(ring[k].d2) * 0.5 - CrocDrawnSlackM; break; }
                                }
                                if (dd < f) f = dd;
                            }
                            crocFree[cj * cw + ci] = (float)f;
                        }
                }
            // pools: swamp tiles that touch (4 neighbours), numbered in reading order
            crocPoolOfTile = new int[w * h];
            for (int k = 0; k < crocPoolOfTile.Length; k++) crocPoolOfTile[k] = -1;
            var q = new List<int>(512);
            for (int k0 = 0; k0 < water.Length; k0++)
            {
                if (!water[k0] || crocPoolOfTile[k0] >= 0) continue;
                q.Clear(); q.Add(k0); crocPoolOfTile[k0] = crocPools;
                for (int qi = 0; qi < q.Count; qi++)
                {
                    int x = q[qi] % w, y = q[qi] / w;
                    for (int n = 0; n < 4; n++)
                    {
                        int nx = x + (n == 0 ? -1 : n == 1 ? 1 : 0), ny = y + (n == 2 ? -1 : n == 3 ? 1 : 0);
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                        int nk = ny * w + nx;
                        if (water[nk] && crocPoolOfTile[nk] < 0) { crocPoolOfTile[nk] = crocPools; q.Add(nk); }
                    }
                }
                crocPools++;
            }
            crocPoolCool = new double[crocPools]; crocPoolHunt = new int[crocPools]; crocPoolTurnCells = new List<int>[crocPools]; crocPoolAttackM2 = new double[crocPools];
            for (int p = 0; p < crocPools; p++) crocPoolHunt[p] = -1;
            // attack water: where a robot stands when the whole attack fits round an animal 2.08 m behind it, in one of 16 directions
            crocAttack = new bool[cw * ch];
            for (int cj = 0; cj < ch; cj++)
                for (int ci = 0; ci < cw; ci++)
                {
                    if (crocFree[cj * cw + ci] < CrocTurnM) continue;
                    int p = crocPoolOfTile[(cj >> 2) * w + (ci >> 2)];
                    (crocPoolTurnCells[p] ?? (crocPoolTurnCells[p] = new List<int>())).Add(cj * cw + ci);
                    double x = (ci + 0.5) * 0.5, y = (cj + 0.5) * 0.5;
                    for (int k = 0; k < 16; k++)
                    {
                        double dx = Math.Cos(k * Math.PI / 8), dy = Math.Sin(k * Math.PI / 8);
                        if (!CrocBoxFitsM(x, y, dx, dy)) continue;
                        int ri = (int)Math.Floor((x + dx * CrocRobotAheadM) / 0.5), rj = (int)Math.Floor((y + dy * CrocRobotAheadM) / 0.5);
                        for (int jj = rj - 1; jj <= rj + 1; jj++)
                            for (int ii = ri - 1; ii <= ri + 1; ii++)
                                if (ii >= 0 && jj >= 0 && ii < cw && jj < ch && crocFree[jj * cw + ii] >= 0) crocAttack[jj * cw + ii] = true;
                    }
                }
            for (int k = 0; k < crocAttack.Length; k++)
                if (crocAttack[k]) crocPoolAttackM2[crocPoolOfTile[((k / cw) >> 2) * w + ((k % cw) >> 2)]] += 0.25;
            // the animals of the table: each at its place when it may turn there, else at the nearest such place of the same pool
            for (int i = 0; i < places.Length; i++)
            {
                double x = places[i].x, y = places[i].y;
                int pool = CrocPoolAt((int)x, (int)y);
                if (CrocFreeAt(x, y) < CrocTurnM)
                {
                    if (pool < 0 || !CrocNearestCell(pool, x, y, CrocTurnM, 12.0, out x, out y)) { CrocPlacesDropped++; continue; }
                    CrocPlacesMoved++;
                }
                var c = new ZCroc { Index = crocs.Count, Pool = pool, Kind = places[i].kind, X = x, Y = y, HomeX = x, HomeY = y, Heading = places[i].heading, HomeHeading = places[i].heading, State = ZCrocState.Lurk };
                if (c.Kind != 0) c.route = CrocRoute(c);
                if (c.route == null) c.Kind = 0;
                c.Until = Time + CrocRest(c, 0) * 0.5;
                crocs.Add(c);
            }
        }

        /// <summary>Free water (m) at a map px point, never more than it really is: the value of its 0.5 m cell less the point's distance
        /// from that cell's middle (the free water of two points differs by no more than their distance). Under 0 = not theirs at all.</summary>
        public double CrocFreeAt(double px, double py) => crocFree == null ? -100 : CrocFreeM(px / CrocPx, py / CrocPx);
        double CrocFreeM(double xm, double ym)
        {
            int ci = (int)Math.Floor(xm / 0.5), cj = (int)Math.Floor(ym / 0.5);
            if (ci < 0 || cj < 0 || ci >= cw || cj >= ch) return -100;
            double ox = xm - (ci + 0.5) * 0.5, oy = ym - (cj + 0.5) * 0.5;
            return crocFree[cj * cw + ci] - Math.Sqrt(ox * ox + oy * oy);
        }
        /// <summary>A robot standing here can be taken (attack water).</summary>
        public bool CrocAttackWater(int px, int py)
        {
            if (crocAttack == null) return false;
            int ci = px >> 2, cj = py >> 2;
            return ci >= 0 && cj >= 0 && ci < cw && cj < ch && crocAttack[cj * cw + ci];
        }

        /// <summary>The whole attack fits: the box round an animal at (x, y) m facing (dx, dy), sampled every 0.5 m, wholly on water.</summary>
        bool CrocBoxFitsM(double x, double y, double dx, double dy)
        {
            double px = -dy, py = dx;
            for (double a = -CrocBoxBehindM; ; a = Math.Min(CrocBoxAheadM, a + 0.5))
            {
                for (double s = -CrocBoxSideM; ; s = Math.Min(CrocBoxSideM, s + 0.5))
                {
                    if (CrocFreeM(x + dx * a + px * s, y + dy * a + py * s) < 0) return false;
                    if (s >= CrocBoxSideM) break;
                }
                if (a >= CrocBoxAheadM) break;
            }
            return true;
        }
        public bool CrocBoxFits(double px, double py, double heading) => crocFree != null && CrocBoxFitsM(px / CrocPx, py / CrocPx, Math.Cos(heading), Math.Sin(heading));

        /// <summary>Nearest cell centre of the pool (px) with at least `need` m free, within `within` m; fixed scan order.</summary>
        bool CrocNearestCell(int pool, double px, double py, double need, double within, out double rx, out double ry)
        {
            rx = px; ry = py; double best = double.MaxValue; int w = Map.width;
            int ci0 = (int)Math.Floor(px / 4.0), cj0 = (int)Math.Floor(py / 4.0), r = (int)Math.Ceiling(within / 0.5);
            for (int cj = cj0 - r; cj <= cj0 + r; cj++)
                for (int ci = ci0 - r; ci <= ci0 + r; ci++)
                {
                    if (ci < 0 || cj < 0 || ci >= cw || cj >= ch || crocFree[cj * cw + ci] < need) continue;
                    if (crocPoolOfTile[(cj >> 2) * w + (ci >> 2)] != pool) continue;
                    double x = ci * 4 + 2, y = cj * 4 + 2, d = (x - px) * (x - px) + (y - py) * (y - py);
                    if (d < best) { best = d; rx = x; ry = y; }
                }
            return best <= within * CrocPx * within * CrocPx;
        }

        /// <summary>Every point of the straight line keeps `need` m free (sampled every 0.25 m).</summary>
        bool CrocLineFree(double ax, double ay, double bx, double by, double need)
        {
            double len = Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay)); int n = Math.Max(1, (int)Math.Ceiling(len / 2.0));
            for (int i = 0; i <= n; i++) if (CrocFreeAt(ax + (bx - ax) * i / n, ay + (by - ay) * i / n) < need) return false;
            return true;
        }

        /// <summary>A swimmer's own stretch: from its place the longest straight run with room to turn everywhere on it (16 directions,
        /// 4 to 30 m), then a second run from its end when the way home from there is free too. Null = no room: it lurks.</summary>
        double[] CrocRoute(ZCroc c)
        {
            double hx = c.HomeX, hy = c.HomeY;
            if (CrocFreeAt(hx, hy) < CrocRouteM)
            {
                if (!CrocNearestCell(c.Pool, hx, hy, CrocRouteM, 6.0, out hx, out hy)) return null;
                c.X = c.HomeX = hx; c.Y = c.HomeY = hy;
            }
            int k1 = CrocBestRun(hx, hy, -1, out double l1);
            if (k1 < 0) return null;
            double x1 = hx + Math.Cos(k1 * Math.PI / 8) * l1, y1 = hy + Math.Sin(k1 * Math.PI / 8) * l1;
            int k2 = CrocBestRun(x1, y1, k1, out double l2);
            if (k2 >= 0)
            {
                double x2 = x1 + Math.Cos(k2 * Math.PI / 8) * l2, y2 = y1 + Math.Sin(k2 * Math.PI / 8) * l2;
                if (CrocLineFree(x2, y2, hx, hy, CrocRouteM)) return new[] { hx, hy, x1, y1, x2, y2 };
            }
            return new[] { hx, hy, x1, y1 };
        }

        int CrocBestRun(double x, double y, int cameAlong, out double length)
        {
            int bestK = -1; length = 0;
            for (int k = 0; k < 16; k++)
            {
                if (cameAlong >= 0) { int back = (cameAlong + 8) & 15, d = Math.Abs(k - back); d = Math.Min(d, 16 - d); if (d <= 2 || k == cameAlong) continue; }   // not straight back, not straight on
                double dx = Math.Cos(k * Math.PI / 8), dy = Math.Sin(k * Math.PI / 8), l = 0;
                while (l + 4.0 <= 30 * CrocPx && CrocFreeAt(x + dx * (l + 4.0), y + dy * (l + 4.0)) >= CrocRouteM) l += 4.0;
                if (l >= 4 * CrocPx && l > length + 1e-9) { length = l; bestK = k; }
            }
            return bestK;
        }

        double CrocRest(ZCroc c, int n) => CrocRestMin + (CrocRestMax - CrocRestMin) * (CrocHash((uint)c.Index + 17u, (uint)n, (uint)(c.leg + 3)) % 1000u) / 1000.0;

        /// <summary>Checks: the exact free water (m) at a map px point, from the tiles and the drawn mask (slow: not used by the rule).</summary>
        public double CrocShoreMetres(double px, double py)
        {
            if (crocFree == null) return -100;
            int w = Map.width, h = Map.height, tx = (int)Math.Floor(px / 16.0), ty = (int)Math.Floor(py / 16.0);
            if (tx < 0 || ty < 0 || tx >= w || ty >= h || !(Map.ClassAt(tx, ty) == ZClass.Water && crocPoolOfTile[ty * w + tx] >= 0)) return -100;
            double xm = px / CrocPx, ym = py / CrocPx, best = 10.0;
            for (int dy = -5; dy <= 5; dy++)
                for (int dx = -5; dx <= 5; dx++)
                {
                    int x = tx + dx, y = ty + dy;
                    if (!(x < 0 || y < 0 || x >= w || y >= h || crocPoolOfTile[y * w + x] < 0)) continue;
                    double x0 = x * 2.0, y0 = y * 2.0, ddx = Math.Max(Math.Max(x0 - xm, xm - (x0 + 2.0)), 0.0), ddy = Math.Max(Math.Max(y0 - ym, ym - (y0 + 2.0)), 0.0);
                    best = Math.Min(best, Math.Sqrt(ddx * ddx + ddy * ddy));
                }
            double f = best - CrocWobbleM;
            var drawn = ZCrocData.DrawnWater(cw, ch);
            if (drawn != null)
            {
                int ci = (int)Math.Floor(xm / 0.5), cj = (int)Math.Floor(ym / 0.5);
                for (int jj = cj - 9; jj <= cj + 9; jj++)
                    for (int ii = ci - 9; ii <= ci + 9; ii++)
                    {
                        if (!(jj < 0 || ii < 0 || jj >= ch || ii >= cw || !drawn[jj * cw + ii])) continue;
                        double x0 = ii * 0.5, y0 = jj * 0.5, ddx = Math.Max(Math.Max(x0 - xm, xm - (x0 + 0.5)), 0.0), ddy = Math.Max(Math.Max(y0 - ym, ym - (y0 + 0.5)), 0.0);
                        f = Math.Min(f, Math.Sqrt(ddx * ddx + ddy * ddy));
                    }
            }
            return f;
        }

        // ------------------------------------------------------------------ the tick
        static double CrocAngle(double a) { while (a > Math.PI) a -= 2 * Math.PI; while (a < -Math.PI) a += 2 * Math.PI; return a; }
        static double CrocTurn(double from, double to, double max) { double d = CrocAngle(to - from); return from + Math.Max(-max, Math.Min(max, d)); }
        static bool CrocOnWater(ZWorld w, ZObject r) => w.Map.ClassAt(r.CenterX >> 4, r.CenterY >> 4) == ZClass.Water;
        bool CrocPreyOk(ZObject r) => r != null && r.Kind == ZKind.Robot && !r.Destroyed && r.Health > 0 && r.KillAt < 0 && CrocOnWater(this, r);

        /// <summary>ZWorld.Tick after the run-over step: the animals' own clock.</summary>
        void CrocodileTick(double dt)
        {
            if (crocs.Count == 0 || !CrocodileDeviation) return;
            if (Time >= nextCrocBeat) { nextCrocBeat = Time + 0.1; CrocBeat(); }
            if (Time >= nextCrocCheck && !(GameOver && EndFreezeDeviation)) { nextCrocCheck = Time + CrocCheckEvery; CrocLookForPrey(); }
            for (int i = 0; i < crocs.Count; i++) CrocStep(crocs[i], dt);
        }

        /// <summary>Every 0.1 s: how long each robot has waded without a break; who hurries (DV92); the least free water (checks).</summary>
        void CrocBeat()
        {
            int n = Objects.Count;
            for (int i = 0; i < n && i < Objects.Count; i++)
            {
                var r = Objects[i];
                if (r.Kind != ZKind.Robot) continue;
                if (r.Destroyed || r.KillAt >= 0 || !CrocOnWater(this, r)) { r.CrocWetSince = -1; continue; }
                if (r.CrocWetSince < 0) r.CrocWetSince = Time;
                if (CrocHuntsActive == 0) continue;
                for (int k = 0; k < crocs.Count; k++)
                {
                    var c = crocs[k];
                    if (!c.Hunting) continue;
                    double dx = r.CenterX - c.X, dy = r.CenterY - c.Y;
                    if (dx * dx + dy * dy > CrocHurryM * CrocPx * CrocHurryM * CrocPx) continue;
                    if (CrocHurryDeviation && r.CrocPanicUntil < Time)
                    {
                        var lead = r.Leader ?? r;
                        if (lead.CrocHurryHunt != CrocHunts * 16 + c.Index) { lead.CrocHurryHunt = CrocHunts * 16 + c.Index; CrocNote(ZCrocNote.Hurry, c, lead.Id, lead.CenterX, lead.CenterY); }   // one line per squad per hunt
                    }
                    r.CrocPanicUntil = Time + CrocHurryAfter;
                    break;
                }
            }
            for (int k = 0; k < crocs.Count; k++) if (!crocs[k].Hidden) { double f = CrocFreeAt(crocs[k].X, crocs[k].Y); if (f < CrocLeastFree) CrocLeastFree = f; }
        }

        /// <summary>Once a second: per pool without a hunt and off its cooldown, the robot that has waded longest in attack water; one
        /// chance in four (a hash of the tick, the pool and the robot: no random call) starts the hunt.</summary>
        void CrocLookForPrey()
        {
            for (int p = 0; p < crocPools; p++)
            {
                if (CrocHuntsActive >= CrocMaxHunts) return;
                if (crocPoolTurnCells[p] == null || crocPoolAttackM2[p] <= 0 || crocPoolHunt[p] >= 0 || Time < crocPoolCool[p]) continue;
                ZObject prey = null;
                int n = Objects.Count;
                for (int i = 0; i < n && i < Objects.Count; i++)
                {
                    var r = Objects[i];
                    if (r.Kind != ZKind.Robot || r.CrocWetSince < 0 || Time - r.CrocWetSince < CrocWetSeconds - 1e-9 || r.IsFortDefender || !CrocPreyOk(r)) continue;
                    if (CrocPoolAt(r.CenterX, r.CenterY) != p || !CrocAttackWater(r.CenterX, r.CenterY)) continue;
                    if (prey == null || r.CrocWetSince < prey.CrocWetSince - 1e-9) prey = r;
                }
                if (prey == null) continue;
                if (CrocHash((uint)TickIndex, (uint)p + 1u, (uint)prey.Id) % 100u >= (uint)CrocStartChance) continue;
                CrocStartHunt(prey, p);
            }
        }

        /// <summary>Checks: start a hunt on this robot now (it must stand in attack water of a pool that has an animal free).</summary>
        public bool CrocStartHuntForTest(ZObject prey)
        {
            if (crocs.Count == 0 || !CrocPreyOk(prey)) return false;
            int p = CrocPoolAt(prey.CenterX, prey.CenterY);
            if (p < 0 || crocPoolHunt[p] >= 0 || !CrocAttackWater(prey.CenterX, prey.CenterY)) return false;
            return CrocStartHunt(prey, p);
        }

        bool CrocStartHunt(ZObject prey, int p)
        {
            // the animal: the nearest one of the pool that is free and 5 to 16 m away; else the nearest free one comes up at the pool's
            // farthest place within 16 m of the robot (the design)
            ZCroc pick = null, any = null; double bd = double.MaxValue, ad = double.MaxValue;
            foreach (var c in crocs)
            {
                if (c.Pool != p || c.Busy) continue;
                double d = Math.Sqrt((c.X - prey.CenterX) * (c.X - prey.CenterX) + (c.Y - prey.CenterY) * (c.Y - prey.CenterY)) / CrocPx;
                if (d < ad) { ad = d; any = c; }
                if (d >= CrocStartMinM && d <= CrocStartMaxM && d < bd) { bd = d; pick = c; }
            }
            if (any == null) return false;
            if (pick == null)
            {
                int bestCell = -1; double bestD = -1; bool bestClear = false;
                foreach (int cell in crocPoolTurnCells[p])
                {
                    double x = (cell % cw) * 4 + 2, y = (cell / cw) * 4 + 2, d = Math.Sqrt((x - prey.CenterX) * (x - prey.CenterX) + (y - prey.CenterY) * (y - prey.CenterY)) / CrocPx;
                    if (d > CrocStartMaxM || d < CrocRiseM) continue;
                    if (bestClear && d <= bestD + 1e-9) continue;
                    // a place with a straight free way to the point where it would rise comes before a farther one without
                    double k = (d - CrocRiseM) / d;
                    bool clear = CrocLineFree(x, y, x + (prey.CenterX - x) * k, y + (prey.CenterY - y) * k, CrocTurnM);
                    if (clear ? (!bestClear || d > bestD + 1e-9) : (!bestClear && d > bestD + 1e-9)) { bestD = d; bestCell = cell; bestClear = clear; }
                }
                if (bestCell < 0) return false;
                pick = any; bd = bestD;
                CrocNote(ZCrocNote.Moved, pick, prey.Id);                                              // where it went under
                pick.X = (bestCell % cw) * 4 + 2; pick.Y = (bestCell / cw) * 4 + 2;
                pick.Heading = Math.Atan2(prey.CenterY - pick.Y, prey.CenterX - pick.X);
                CrocMoved++;
            }
            pick.PreyId = prey.Id; pick.Took = false; pick.BiteAt = -1; pick.slid = 0; pick.aimNext = 0; pick.aimSide = -1;
            double way = Math.Max(0, bd - CrocRobotAheadM);
            CrocSet(pick, ZCrocState.Warn, Math.Max(CrocWarnSeconds, CrocFirstSignSeconds - way / CrocStalkMps - CrocRiseSeconds - CrocBiteSeconds));
            crocPoolHunt[p] = pick.Index; CrocHuntsActive++; CrocHunts++;
            return true;
        }

        /// <summary>Another robot for a stalking animal whose own is out of reach: alive, wading in attack water of its pool, within
        /// CrocSwitchM of it; the nearest (ties: the earlier object).</summary>
        ZObject CrocOtherPrey(ZCroc c)
        {
            ZObject best = null; double bd = CrocSwitchM * CrocPx * CrocSwitchM * CrocPx;
            int n = Objects.Count;
            for (int i = 0; i < n && i < Objects.Count; i++)
            {
                var r = Objects[i];
                if (r.Kind != ZKind.Robot || r.Id == c.PreyId || r.IsFortDefender || !CrocPreyOk(r) || !CrocAttackWater(r.CenterX, r.CenterY) || CrocPoolAt(r.CenterX, r.CenterY) != c.Pool) continue;
                double d = (r.CenterX - c.X) * (r.CenterX - c.X) + (r.CenterY - c.Y) * (r.CenterY - c.Y);
                if (d < bd - 1e-9) { bd = d; best = r; }
            }
            return best;
        }

        void CrocEndHunt(ZCroc c, double cool)
        {
            if (crocPoolHunt[c.Pool] == c.Index)
            {
                crocPoolHunt[c.Pool] = -1; CrocHuntsActive = Math.Max(0, CrocHuntsActive - 1);
                crocPoolCool[c.Pool] = Math.Max(crocPoolCool[c.Pool], Time + cool);
                int n = Objects.Count;                                                                 // DV93: the robots that left this pool may walk again 3 s from now
                for (int i = 0; i < n && i < Objects.Count; i++) { var r = Objects[i]; if (r.CrocWaryPool == c.Pool && r.CrocWaryUntil > Time + CrocWaryAfter) r.CrocWaryUntil = Time + CrocWaryAfter; }
            }
            c.PreyId = -1;
        }

        void CrocGiveUp(ZCroc c, bool afterRise)
        {
            if (afterRise) { CrocMissAfter++; CrocSet(c, ZCrocState.Snap, Math.Max(0.05, c.Since + CrocLungeSeconds - Time)); }
            else { CrocMissBefore++; CrocSet(c, ZCrocState.Watch, CrocWatchSeconds); }
        }

        /// <summary>ProcessMissileDamage: a blast within CrocScareM of an animal that is warning or stalking sends it under.</summary>
        void CrocBlast(int x, int y)
        {
            if (crocs.Count == 0) return;
            foreach (var c in crocs)
            {
                if (c.State != ZCrocState.Warn && c.State != ZCrocState.Stalk) continue;
                if ((c.X - x) * (c.X - x) + (c.Y - y) * (c.Y - y) > CrocScareM * CrocPx * CrocScareM * CrocPx) continue;
                CrocScared++; CrocNote(ZCrocNote.Scared, c, c.PreyId);
                CrocEndHunt(c, CrocCoolScared);
                CrocSet(c, ZCrocState.Dive, CrocDiveSeconds);
            }
        }

        void CrocStep(ZCroc c, double dt)
        {
            switch (c.State)
            {
                case ZCrocState.Lurk:
                    if (c.route != null && Time >= c.Until)
                    {   // off along its stretch
                        int pts = c.route.Length / 2;
                        if (pts == 2) c.leg = c.leg == 0 ? 1 : 0; else c.leg = (c.leg + 1) % pts;
                        CrocSet(c, ZCrocState.Swim);
                    }
                    break;
                case ZCrocState.Swim:
                {
                    double tx = c.route[c.leg * 2], ty = c.route[c.leg * 2 + 1], dx = tx - c.X, dy = ty - c.Y, d = Math.Sqrt(dx * dx + dy * dy);
                    if (d < 0.5) { c.X = tx; c.Y = ty; c.Until = 0; CrocSet(c, ZCrocState.Lurk, CrocRest(c, (int)(Time * 10))); break; }
                    double want = Math.Atan2(dy, dx);
                    c.Heading = CrocTurn(c.Heading, want, CrocTurnDeg * Math.PI / 180 * dt);
                    double off = Math.Abs(CrocAngle(want - c.Heading));
                    c.Speed = off > 0.6 ? 0 : CrocSwimMps * CrocPx * (1 - off / 0.6 * 0.5);                // turns on the spot first (2.4 m free everywhere on its stretch)
                    double step = Math.Min(d, c.Speed * dt);
                    c.X += dx / d * step; c.Y += dy / d * step;                                          // always exactly on the checked line
                    break;
                }
                case ZCrocState.Warn:
                {
                    var prey = Get(c.PreyId);
                    if (!CrocPreyOk(prey)) { CrocEndHunt(c, CrocCoolMiss); CrocGiveUp(c, false); break; }
                    c.Heading = CrocTurn(c.Heading, Math.Atan2(prey.CenterY - c.Y, prey.CenterX - c.X), CrocTurnDeg * Math.PI / 180 * dt);
                    if (Time >= c.Until) { c.stalkEnd = Time + CrocStalkLimit; CrocSet(c, ZCrocState.Stalk); }
                    break;
                }
                case ZCrocState.Stalk:
                {
                    var prey = Get(c.PreyId);
                    bool timeUp = Time >= c.stalkEnd;
                    if (!timeUp && (!CrocPreyOk(prey) || !CrocAttackWater(prey.CenterX, prey.CenterY)))
                    {   // its robot is out of the attack water: another robot that wades in it within CrocSwitchM takes its place (the warning
                        // was given to all of them), else the hunt is over
                        prey = CrocOtherPrey(c);
                        if (prey != null) { c.PreyId = prey.Id; c.aimNext = 0; CrocSwitches++; CrocNote(ZCrocNote.State, c, prey.Id); }
                    }
                    if (timeUp || prey == null)
                    {
                        if (timeUp) CrocGaveUpTime++; else CrocGaveUpLeft++;
                        CrocEndHunt(c, CrocCoolMiss); CrocGiveUp(c, false); break;
                    }
                    double dx = prey.CenterX - c.X, dy = prey.CenterY - c.Y, d = Math.Sqrt(dx * dx + dy * dy), toPrey = Math.Atan2(dy, dx);
                    double offPrey = Math.Abs(CrocAngle(toPrey - c.Heading));
                    if (d <= CrocRiseM * CrocPx && offPrey <= CrocRiseDeg * Math.PI / 180 && CrocBoxFits(c.X, c.Y, toPrey))
                    { c.Heading = CrocTurn(c.Heading, toPrey, 0.2); CrocSet(c, ZCrocState.Rise, CrocRiseSeconds); break; }
                    // where it swims to: the place where the robot will be when it gets there (the step the robot is taking, at most 1.2 s
                    // ahead); straight at it when the attack fits from there, else round to the nearest of 16 sides from which it does
                    double lead = Math.Min(1.2, d / (CrocStalkMps * CrocPx)), ax = prey.CenterX + prey.Dx * lead, ay = prey.CenterY + prey.Dy * lead;
                    double want = Math.Atan2(ay - c.Y, ax - c.X), back = (CrocRobotAheadM + 0.3) * CrocPx;
                    if (Time >= c.aimNext)
                    {
                        c.aimNext = Time + 0.25; c.aimSide = -1;
                        if (!CrocBoxFits(ax - Math.Cos(want) * back, ay - Math.Sin(want) * back, want))
                        {
                            double best = double.MaxValue;
                            for (int k = 0; k < 16; k++)
                            {
                                double a = k * Math.PI / 8, px = ax - Math.Cos(a) * back, py = ay - Math.Sin(a) * back;
                                if (CrocFreeAt(px, py) < CrocTurnM || !CrocBoxFits(px, py, a)) continue;
                                double dd = (px - c.X) * (px - c.X) + (py - c.Y) * (py - c.Y);
                                if (dd < best) { best = dd; c.aimSide = k; }
                            }
                        }
                    }
                    bool atSide = false;
                    if (c.aimSide >= 0)
                    {
                        double a = c.aimSide * Math.PI / 8, px = ax - Math.Cos(a) * back, py = ay - Math.Sin(a) * back;
                        double sd = Math.Sqrt((px - c.X) * (px - c.X) + (py - c.Y) * (py - c.Y));
                        if (sd > 4.0) want = Math.Atan2(py - c.Y, px - c.X); else { atSide = true; want = toPrey; }   // there: it turns to the robot on the spot
                    }
                    else if (d <= (CrocRobotAheadM + 0.1) * CrocPx) want = toPrey;
                    c.Heading = CrocTurn(c.Heading, want, CrocStalkTurnDeg * Math.PI / 180 * dt);
                    double off = Math.Abs(CrocAngle(want - c.Heading));
                    // forward: full speed on open water, slowly in a tight turn and where the next metre is not its water; never onto
                    // water that is not its own: then the side with more room, else it gives up
                    double hx = Math.Cos(c.Heading), hy = Math.Sin(c.Heading);
                    double speed = atSide || (c.aimSide < 0 && d <= (CrocRobotAheadM + 0.1) * CrocPx) ? 0 : CrocStalkMps * CrocPx;
                    if (speed > 0 && (off > 1.2 || CrocFreeAt(c.X + hx * CrocPx, c.Y + hy * CrocPx) < CrocTurnM)) speed = CrocSlowMps * CrocPx;
                    c.Speed = speed;
                    if (speed <= 0) break;
                    double nx = c.X + hx * speed * dt, ny = c.Y + hy * speed * dt;
                    if (CrocFreeAt(nx, ny) >= CrocTurnM) { c.X = nx; c.Y = ny; break; }
                    bool moved = false;
                    for (int k = 1; k <= 4 && !moved; k++)
                    {
                        double a = k * Math.PI / 8, lx = Math.Cos(c.Heading - a), ly = Math.Sin(c.Heading - a), rx = Math.Cos(c.Heading + a), ry = Math.Sin(c.Heading + a);
                        double st = CrocSlowMps * CrocPx * dt, fl = CrocFreeAt(c.X + lx * st, c.Y + ly * st), fr = CrocFreeAt(c.X + rx * st, c.Y + ry * st);
                        double pl = CrocFreeAt(c.X + lx * 16, c.Y + ly * 16), pr = CrocFreeAt(c.X + rx * 16, c.Y + ry * 16);   // the side with more free water 2 m on
                        bool left = fl >= CrocTurnM && (fr < CrocTurnM || pl > pr + 1e-9 || (Math.Abs(pl - pr) <= 1e-9 && CrocAngle(want - c.Heading) < 0));
                        if (left) { c.X += lx * st; c.Y += ly * st; moved = true; }
                        else if (fr >= CrocTurnM) { c.X += rx * st; c.Y += ry * st; moved = true; }
                    }
                    c.Speed = moved ? CrocSlowMps * CrocPx : 0;
                    if (!moved && off < 0.2 && d > CrocRiseM * CrocPx) { CrocGaveUpNoWay++; CrocEndHunt(c, CrocCoolMiss); CrocGiveUp(c, false); }   // no way on: it stops, watches and dives
                    break;
                }
                case ZCrocState.Rise:
                {
                    var prey = Get(c.PreyId);
                    if (!CrocPreyOk(prey)) { CrocEndHunt(c, CrocCoolMiss); CrocMissAfter++; CrocSet(c, ZCrocState.Dive, CrocDiveSeconds); break; }
                    double dx = prey.CenterX - c.X, dy = prey.CenterY - c.Y, d = Math.Sqrt(dx * dx + dy * dy);
                    c.Heading = CrocTurn(c.Heading, Math.Atan2(dy, dx), CrocTurnDeg * Math.PI / 180 * dt);
                    double gap = d - CrocRobotAheadM * CrocPx;                                       // it glides to 2.08 m from the robot: on, or back from one that wades at it
                    c.Speed = 0;
                    if (Math.Abs(gap) > 0.01 && d > 1e-6)
                    {
                        double step = gap > 0 ? Math.Min(gap, CrocStalkMps * CrocPx * dt) : Math.Max(gap, -CrocSlowMps * CrocPx * dt), nx = c.X + dx / d * step, ny = c.Y + dy / d * step;
                        if (CrocFreeAt(nx, ny) >= CrocTurnM) { c.X = nx; c.Y = ny; c.Speed = step / dt; }
                    }
                    if (Time >= c.Until) { c.slid = 0; c.BiteAt = Time + CrocBiteSeconds; CrocSet(c, ZCrocState.Lunge, CrocLungeSeconds); }
                    break;
                }
                case ZCrocState.Lunge:
                {
                    if (!c.Took && c.BiteAt >= 0)
                    {
                        var prey = Get(c.PreyId);
                        double hx = Math.Cos(c.Heading), hy = Math.Sin(c.Heading);
                        if (prey != null)
                        {
                            c.Heading = CrocTurn(c.Heading, Math.Atan2(prey.CenterY - c.Y, prey.CenterX - c.X), CrocLungeTurnDeg * Math.PI / 180 * dt);   // the burst follows it a little
                            hx = Math.Cos(c.Heading); hy = Math.Sin(c.Heading);
                            double along = ((prey.CenterX - c.X) * hx + (prey.CenterY - c.Y) * hy) / CrocPx - CrocRobotAheadM;
                            double room = CrocSlideM - c.slid, step = Math.Min(Math.Min(along, room), CrocSlideM / 0.3 * dt);   // the slide: up to 0.9 m, done in 0.3 s
                            if (step > 0) { c.X += hx * step * CrocPx; c.Y += hy * step * CrocPx; c.slid += step; }
                        }
                        if (Time >= c.BiteAt)
                        {
                            c.BiteAt = -1;
                            bool got = false;
                            if (CrocPreyOk(prey))
                            {
                                double rx = (prey.CenterX - c.X) / CrocPx, ry = (prey.CenterY - c.Y) / CrocPx, along = rx * hx + ry * hy - CrocRobotAheadM, side = -rx * hy + ry * hx;
                                got = along <= CrocReachAlongM && along >= -CrocReachNearM && Math.Abs(side) <= CrocReachSideM;   // from under the snout to the tips of the wide jaws
                            }
                            if (got)
                            {   // taken: one robot per attack
                                c.Took = true; CrocKills++;
                                prey.Eaten = true; prey.EatenBy = c.Index;
                                CrocNote(ZCrocNote.Bite, c, prey.Id, prey.CenterX, prey.CenterY);
                                prey.SetHealth(0);
                                UpdateObjectHealth(prey, -1);                                            // the Death event; nobody gets the kill
                            }
                            else { CrocEndHunt(c, CrocCoolMiss); CrocGiveUp(c, true); break; }
                        }
                    }
                    if (Time >= c.Until) CrocSet(c, ZCrocState.Thrash, CrocThrashSeconds);
                    break;
                }
                case ZCrocState.Thrash:
                    if (Time >= c.Until) CrocSet(c, ZCrocState.Drag, CrocDragSeconds);
                    break;
                case ZCrocState.Drag:
                    if (Time >= c.Until)
                    {
                        double cool = CrocCoolKillMin + (CrocCoolKillMax - CrocCoolKillMin) * (CrocHash((uint)TickIndex, (uint)c.Index + 5u, (uint)CrocKills) % 1000u) / 1000.0;
                        CrocEndHunt(c, cool);
                        CrocSet(c, ZCrocState.Under, CrocBackAfter);
                    }
                    break;
                case ZCrocState.Watch:
                case ZCrocState.Snap:
                    if (Time >= c.Until) CrocSet(c, ZCrocState.Dive, CrocDiveSeconds);
                    break;
                case ZCrocState.Dive:
                    if (Time >= c.Until) CrocSet(c, ZCrocState.Under, CrocBackAfterMiss);
                    break;
                case ZCrocState.Under:
                    if (Time >= c.Until)
                    {   // up again at its own place
                        c.X = c.HomeX; c.Y = c.HomeY; c.Heading = c.HomeHeading; c.leg = 0; c.Took = false;
                        CrocSet(c, ZCrocState.Lurk, CrocRest(c, (int)(Time * 10)));
                    }
                    break;
            }
        }

        /// <summary>DV93: the hunting animal this robot (or its squad) must get away from: one that has picked it or one of its squad,
        /// else the nearest within `within` px. Null = none.</summary>
        internal ZCroc CrocHuntFor(ZObject r, double within)
        {
            if (CrocHuntsActive == 0) return null;
            ZCroc best = null; double bd = within * within;
            foreach (var c in crocs)
            {
                if (!c.Hunting) continue;
                if (c.PreyId == r.Id) return c;
                foreach (var m in r.Minions) if (m != null && m.Id == c.PreyId) return c;
                double d = (c.X - r.CenterX) * (c.X - r.CenterX) + (c.Y - r.CenterY) * (c.Y - r.CenterY);
                if (d <= bd) { bd = d; best = c; }
            }
            return best;
        }

        internal void CrocNoteFlee(ZCroc c, ZObject r) { CrocFlees++; CrocNote(ZCrocNote.Flee, c, r.Id, r.CenterX, r.CenterY); }
        internal void CrocNoteBank(ZObject r) { CrocBanks++; CrocNote(ZCrocNote.Bank, null, r.Id, r.CenterX, r.CenterY); }
    }

    public partial class ZObject
    {
        /// <summary>DV91: taken by a crocodile (the view's "eaten" death, no cry of the usual kind), and by which one.</summary>
        public bool Eaten; public int EatenBy = -1;
        /// <summary>DV91: game time since which this robot has waded without a break (-1 = on dry ground).</summary>
        public double CrocWetSince = -1;
        /// <summary>DV92: hurries until this game time (while it stands on a water tile).</summary>
        public double CrocPanicUntil = -1;
        /// <summary>DV92: the hunt its squad was told about (one line per squad per hunt).</summary>
        public int CrocHurryHunt = -1;
        /// <summary>DV93: starts nothing by itself until this game time; the pool it left.</summary>
        public double CrocWaryUntil = -1; public int CrocWaryPool = -1;
        double nextCrocLook; bool crocFleeing;
        public bool CrocWary => CrocWaryUntil > 0 && World != null && World.Time < CrocWaryUntil;
        public bool CrocHurries => ZWorld.CrocHurryDeviation && World != null && CrocPanicUntil > World.Time && World.Map.ClassAt(CenterX >> 4, CenterY >> 4) == ZClass.Water;

        /// <summary>DV92 (RunSpeed): the hurry of a wading robot near a hunting crocodile: at least 1.3, the running speed when it runs
        /// anyway (the larger of the two, never both), stamina untouched.</summary>
        bool CrocHurry(out double speed)
        {
            speed = 1.0;
            if (!CrocHurries) return false;
            bool runs = (Leader != null ? Leader.IsRunning : IsRunning) && !ShowDamaged;
            speed = Math.Max(ZWorld.CrocHurryFactor, runs ? World.S.run_unit_speed : 1.0);
            return true;
        }

        /// <summary>DV93 (ProcessServer, after the patrol step): no order, in the water, a crocodile hunts near: to the nearest dry tile.</summary>
        void CrocFleeStep(double time)
        {
            if (Kind != ZKind.Robot || CrocWaryUntil < 0 && !ZWorld.CrocFleeDeviation) return;
            if (CrocWaryUntil > 0)
            {
                if (Waypoints.Count > 0 && Waypoints[0].playerGiven) { CrocWaryUntil = -1; crocFleeing = false; }      // any order ends it
                else if (time >= CrocWaryUntil) { CrocWaryUntil = -1; crocFleeing = false; }
                else if (crocFleeing && Waypoints.Count == 0 && !IsMinion)
                {
                    crocFleeing = false;
                    if (World.Map.ClassAt(CenterX >> 4, CenterY >> 4) != ZClass.Water) World.CrocNoteBank(this);
                }
            }
            if (!ZWorld.CrocFleeDeviation || !ZWorld.CrocodileDeviation || World.Crocs.Count == 0 || time < nextCrocLook) return;
            nextCrocLook = time + 0.5;
            if (IsMinion || Waypoints.Count > 0 || HoldPosition || Destroyed || Health <= 0 || KillAt >= 0 || !CanMove || IsFortDefender || KnockedDown(time)) return;
            bool Wet(int px, int py) => World.Map.ClassAt(px >> 4, py >> 4) == ZClass.Water;
            // the one of the squad that stands in the water: the leader, else the first of its robots (a squad that stopped at the bank
            // with its tail still wading steps back from the water with it)
            ZObject wader = Wet(CenterX, CenterY) ? this : null;
            if (wader == null) foreach (var m in Minions) if (m != null && !m.Destroyed && m.KillAt < 0 && Wet(m.CenterX, m.CenterY)) { wader = m; break; }
            if (wader == null || World.Pathing == null) return;
            var c = World.CrocHuntFor(this, ZWorld.CrocFleeM * ZWorld.CrocPx) ?? (wader != this ? World.CrocHuntFor(wader, ZWorld.CrocFleeM * ZWorld.CrocPx) : null);
            if (c == null) return;
            var reach = World.Pathing.Reach;
            int comp = reach.Comp(CenterX >> 4, CenterY >> 4, true, false);
            double keep = ZWorld.CrocFleeKeepM * ZWorld.CrocPx;
            // from where: the leader's own place when it wades; when only its robots wade, a point 4 tiles farther from the animal
            int fx = CenterX, fy = CenterY;
            if (wader != this)
            {
                double ax = CenterX - c.X, ay = CenterY - c.Y, al = Math.Sqrt(ax * ax + ay * ay);
                if (al > 1e-6) { fx = CenterX + (int)(ax / al * 64); fy = CenterY + (int)(ay / al * 64); }
            }
            bool Skip(int px, int py)
            {
                if (Wet(px, py)) return true;
                if (wader != this && (Wet(px - 16, py) || Wet(px + 16, py) || Wet(px, py - 16) || Wet(px, py + 16))) return true;   // not right at the water again
                double dc = (px - c.X) * (px - c.X) + (py - c.Y) * (py - c.Y), dr = (double)(px - CenterX) * (px - CenterX) + (double)(py - CenterY) * (py - CenterY);
                return dc < keep * keep || dc < dr;                                                     // not past the animal
            }
            if (!reach.Nearest(fx, fy, true, false, comp, Skip, out int rx, out int ry, ZWorld.CrocFleeTiles)
                && !reach.Nearest(fx, fy, true, false, comp, Wet, out rx, out ry, ZWorld.CrocFleeTiles)) return;
            if (rx == CenterX && ry == CenterY) return;
            Waypoints.Add(new ZWaypoint { mode = WpMode.Move, refId = -1, x = rx, y = ry });
            CloneMinionWayPoints();
            crocFleeing = true;
            CrocWaryUntil = time + ZWorld.CrocWaryMax; CrocWaryPool = c.Pool;
            foreach (var m in Minions) if (m != null) { m.CrocWaryUntil = CrocWaryUntil; m.CrocWaryPool = c.Pool; }
            World.CrocNoteFlee(c, this);
        }

        /// <summary>DV94 (CheckPassiveEngage): on a crocodile level an idle robot starts no walk of its own to a target whose straight
        /// line from it crosses more than CrocSwampTiles swamp tiles.</summary>
        bool CrocSwampWay(ZObject t)
        {
            if (!ZWorld.CrocWaterCostDeviation || Kind != ZKind.Robot || t == null || !World.CrocMap) return false;
            int n = 0, sx = CenterX >> 4, sy = CenterY >> 4, ex = t.CenterX >> 4, ey = t.CenterY >> 4;
            foreach (var (x, y) in ZPathFinder.Bresenham(sx, sy, ex, ey, World.Map.width, World.Map.height))
                if (World.Map.ClassAt(x, y) == ZClass.Water && ++n > ZWorld.CrocSwampTiles) return true;
            return false;
        }
    }
}
