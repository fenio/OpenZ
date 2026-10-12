using System;
using System.Collections.Generic;

namespace OpenZ.Sim
{
    /// <summary>
    ///
    /// Zod's only engagement test besides range is EngageBarrierBetween (zmap.cpp:2266-2295: a destroyable rock / hut / map
    /// object between two tiles, both Bresenham directions); it is kept unchanged. DV23 adds, for DIRECT fire only, the same
    /// symmetric tile walk (ZPathFinder.Bresenham from the shooter's centre tile to the target's centre tile and back) over a
    /// sight grid: cliffs (tile class Impassable), building tiles whose mesh covers >= 40 % (ZClearance table, forts incl.
    /// their walls, factories, repair, radar; bridges never; a destroyed building no longer blocks), rocks (the pillar tile),
    /// huts and the tall map objects (desert rocks: map_object >= 9 on L02; city blocks: L17 map_object except 11 barrel,
    /// 12 crate, 13 stop sign; the view builds the same models, OpenZSetup.Spawn). The two end tiles never block, and neither
    /// do the tiles of the target building / rock itself, nor the shooter's or target's own fort for a cannon on a fort tower
    /// (elevated: tower guns see over their fort's walls, and can be seen). Arcing fire needs no line: howitzer, Mobile
    /// Missile (MLRS), the DV7 fort gun and every grenade throw (a robot whose squad carries grenades throws them).
    ///
    /// Hooks (ZObject): WithinAttackRadius / WithinAgroRadius (auto-targeting, attack-move, agro and the view's aim all skip a
    /// blocked target), ProcessAttackDamage (never fires a direct shot without a line), ProcessAgroWP (line lost = the agro
    /// ends: the unit picks another visible target), ProcessAttackWP (an ordered target without a line: path to the nearest
    /// spot in range with a clear shot, found by a breadth-first walk of the unit's own A* graph; none anywhere = the order
    /// ends), the DV9 duel headings (end point must keep the line). Zod's damage, ranges, rates and rolls are untouched.
    ///
    /// Cheap and deterministic: a result depends only on the two tiles, the exemptions and which blockers are alive, so it is
    /// memoised per (shooter tile, target tile, exemptions); the memo is dropped whenever a blocker dies or revives (a
    /// signature over the blockers, checked once per sim tick at the first query). Pure sim: tile grid, no Unity physics, no
    /// frame time. Toggle: ZWorld.LosDeviation (static; PlayerPrefs oz_los_sim, off in 'Zod exact').
    /// </summary>
    public sealed class ZSight
    {
        readonly ZWorld W;
        readonly int w, h;
        readonly int[] tileObj;                     // 0 clear, -1 terrain (cliff), > 0 object id (building mesh >= 40 %, rock, hut, tall prop)
        readonly List<ZObject> blockers = new List<ZObject>();
        readonly Dictionary<long, bool> memo = new Dictionary<long, bool>();
        readonly Dictionary<int, int> towerFort = new Dictionary<int, int>();
        long signature = long.MinValue; double sigTime = double.NaN;
        public const int MemoCap = 200000;
        /// <summary>Tiles round a bridge's rect whose terrain never blocks sight (the map's bridge side-face tiles lie one tile outside it).</summary>
        public const int BridgeSide = 1;
        /// <summary>Counters (checks / reports): queries, tile walks (memo misses), blocked results.</summary>
        public long Queries, Walks, BlockedWalks;

        public ZSight(ZWorld world)
        {
            W = world; w = world.Map.width; h = world.Map.height;
            tileObj = new int[w * h];
            for (int ty = 0; ty < h; ty++)
                for (int tx = 0; tx < w; tx++)
                    if (world.Map.ClassAt(tx, ty) == ZClass.Impassable) tileObj[ty * w + tx] = -1;
            foreach (var o in world.Objects) Add(o);
        }

        /// <summary>Register a building / map item as a sight blocker (map load; ZWorld.CreateObject for later objects).</summary>
        internal void Add(ZObject o)
        {
            var cl = W.Clearance;
            if (o.Kind == ZKind.Building)
            {
                if (o.IsBridge)
                {                                                                              // (tile class Impassable along the deck) included
                    // the map also draws the deck's side face as Impassable tiles one tile outside the rect (east of a vertical bridge,
                    // south of a horizontal one): BridgeSide tiles round the rect are cleared too, so fire passes both ways
                    for (int ty = (o.LocY >> 4) - BridgeSide; ty < ((o.LocY + o.HeightPix) >> 4) + BridgeSide; ty++)
                        for (int tx = (o.LocX >> 4) - BridgeSide; tx < ((o.LocX + o.WidthPix) >> 4) + BridgeSide; tx++)
                            if (tx >= 0 && ty >= 0 && tx < w && ty < h && tileObj[ty * w + tx] == -1) tileObj[ty * w + tx] = 0;
                    memo.Clear(); return;
                }
                if (cl == null) return;
                bool any = false;
                for (int ty = o.LocY >> 4; ty < (o.LocY + o.HeightPix) >> 4; ty++)
                    for (int tx = o.LocX >> 4; tx < (o.LocX + o.WidthPix) >> 4; tx++)
                        if (cl.MeshOwner(tx, ty) == o.Id && cl.Cover(tx, ty) >= ZClearance.SightCover) { Set(tx, ty, o.Id); any = true; }
                if (any) { blockers.Add(o); signature = long.MinValue; sigTime = double.NaN; }
            }
            else if (o.Kind == ZKind.MapItem && BlocksSight(o))
            {
                int tx = o.LocX >> 4, ty = (o.LocY >> 4) + (o.Type == "rock" ? 2 : 0);   // a rock blocks its bottom tile (orock.cpp:675)
                Set(tx, ty, o.Id); blockers.Add(o); signature = long.MinValue; sigTime = double.NaN;
            }
        }

        void Set(int tx, int ty, int id) { if (tx >= 0 && ty >= 0 && tx < w && ty < h && tileObj[ty * w + tx] == 0) tileObj[ty * w + tx] = id; }

        /// <summary>Map items with a tall model: rocks, huts, desert rock props (map_object >= 9, L02) and city blocks (L17, all
        /// but 11 barrel, 12 crate, 13 stop sign), as OpenZSetup.Spawn builds them.</summary>
        bool BlocksSight(ZObject o)
        {
            if (o.Type == "rock" || o.Type == "hut") return true;
            if (!o.Type.StartsWith("map_object", StringComparison.Ordinal)) return false;
            int idx = 0; foreach (char c in o.Type) if (c >= '0' && c <= '9') idx = idx * 10 + (c - '0');
            bool city = W.Map.planet != null && W.Map.planet.IndexOf("city", StringComparison.OrdinalIgnoreCase) >= 0;
            return city ? idx != 11 && idx != 12 && idx != 13 : idx >= 9;
        }

        bool Alive(int id)
        {
            var o = W.Get(id);
            return o != null && !o.Destroyed && o.KillAt < 0;
        }

        /// <summary>Once per sim tick (at the first query): drop the memo when a blocker died or revived.</summary>
        void Refresh()
        {
            if (sigTime == W.Time) return;
            sigTime = W.Time;
            long s = 1469598103934665603L;
            foreach (var b in blockers) s = unchecked((s ^ (Alive(b.Id) ? b.Id : -b.Id)) * 1099511628211L);
            if (s != signature || memo.Count > MemoCap) { signature = s; memo.Clear(); }
        }

        /// <summary>The object whose blocking tiles are ignored for this end of a line: a building / map item itself; for a
        /// cannon on a fort (its centre inside the fort's rect: the 4 tower pads) that fort. 0 = none.</summary>
        public int ExemptOf(ZObject o)
        {
            if (o == null) return 0;
            if (o.Kind == ZKind.Building || o.Kind == ZKind.MapItem) return o.Id;
            if (o.Kind == ZKind.Vehicle && ZWorld.RepairInPlaceDeviation && o.OnAnyRepairPad) return o.Waypoints[0].refId;   // DV62: fires out of its repair bay
            if (o.Kind == ZKind.Vehicle && ZWorld.RepairInPlaceDeviation && ZWorld.RepairBayFireDeviation && o.Waypoints.Count > 0 && o.Waypoints[0].mode == WpMode.UnitRepair
                && o.InRepairBay(o.Waypoints[0].refId)) return o.Waypoints[0].refId;       // DV77: on its way in and out of the bay too
            if (o.IsFortDefender) return o.DefenderFortId;                                    // fort_defender_v1 DV41: on the roof, like a tower gun
            if (ZWorld.ExitLanesDeviation && o.IsUnit && o.Waypoints.Count > 0 && o.Waypoints[0].mode == WpMode.ForceMove)   // DV66: a unit on its way out of
                foreach (var b in blockers)                                                   // the factory / fort that built it sees and fires out of the door (and can be fired at)
                    if (b.Kind == ZKind.Building && b.Contains(o.CenterX, o.CenterY)) return b.Id;
            if (o.Kind != ZKind.Cannon) return 0;
            if (towerFort.TryGetValue(o.Id, out int f)) return f;
            f = 0;
            foreach (var b in W.Objects)
                if (b.IsFort && o.CenterX >= b.LocX && o.CenterX < b.LocX + b.WidthPix && o.CenterY >= b.LocY && o.CenterY < b.LocY + b.HeightPix) { f = b.Id; break; }
            towerFort[o.Id] = f;                                                               // cannons never move
            return f;
        }

        /// <summary>Clear line from a to b (their centres).</summary>
        public bool Clear(ZObject a, ZObject b) => ClearFrom(a.CenterX, a.CenterY, ExemptOf(a), b);
        /// <summary>Clear line from the point (x, y) (exempting object exA's tiles) to b's centre.</summary>
        public bool ClearFrom(int x, int y, int exA, ZObject b) => Clear(x, y, exA, b.CenterX, b.CenterY, ExemptOf(b));

        public bool Clear(int ax, int ay, int exA, int bx, int by, int exB)
        {
            Queries++;
            Refresh();
            int tax = Clamp(ax >> 4, w), tay = Clamp(ay >> 4, h), tbx = Clamp(bx >> 4, w), tby = Clamp(by >> 4, h);
            long key = ((long)(tay * w + tax) << 42) | ((long)(tby * w + tbx) << 28) | ((long)(exA & 0x3FFF) << 14) | (long)(exB & 0x3FFF);
            if (exA > 0x3FFF || exB > 0x3FFF) return Walk(tax, tay, tbx, tby, exA, exB);          // (map objects have low ids; never in practice)
            if (memo.TryGetValue(key, out bool r)) return r;
            r = Walk(tax, tay, tbx, tby, exA, exB);
            memo[key] = r;
            return r;
        }

        static int Clamp(int v, int n) => v < 0 ? 0 : v >= n ? n - 1 : v;

        bool Walk(int ax, int ay, int bx, int by, int exA, int exB)
        {
            Walks++;
            bool ok = Dir(ax, ay, bx, by, exA, exB) && Dir(bx, by, ax, ay, exA, exB);
            if (!ok) BlockedWalks++;
            return ok;
        }

        /// <summary>Uncached walk (checks: an independent test of what was fired).</summary>
        public bool ClearUncached(int ax, int ay, int exA, int bx, int by, int exB) =>
            Dir(Clamp(ax >> 4, w), Clamp(ay >> 4, h), Clamp(bx >> 4, w), Clamp(by >> 4, h), exA, exB) &&
            Dir(Clamp(bx >> 4, w), Clamp(by >> 4, h), Clamp(ax >> 4, w), Clamp(ay >> 4, h), exA, exB);

        bool Dir(int sx, int sy, int ex, int ey, int exA, int exB)
        {
            foreach (var (x, y) in ZPathFinder.Bresenham(sx, sy, ex, ey, w, h))
            {
                if (x == ex && y == ey) break;                                                  // the end tiles never block
                int id = tileObj[y * w + x];
                if (id == 0) continue;
                if (id < 0) return false;
                if (id == exA || id == exB) continue;
                if (Alive(id)) return false;
            }
            return true;
        }

        /// <summary>The tile blocks sight right now (debug / checks).</summary>
        public bool BlockedTile(int tx, int ty)
        {
            if (tx < 0 || ty < 0 || tx >= w || ty >= h) return true;
            int id = tileObj[ty * w + tx];
            return id < 0 || (id > 0 && Alive(id));
        }

        // ------------------------------------------------------------------ nearest spot with a clear shot
        int[] seen; int seenStamp; int[] queue;

        /// <summary>Breadth-first walk of the unit's own A* graph (ZReach component, Zod's StepOk moves) from where it stands to
        /// the nearest footprint whose centre is within `range` of the target with a clear line (and no Zod barrier). Deterministic
        /// (fixed neighbour order). False = no such spot anywhere the unit can go.</summary>
        internal bool NearestShotSpot(ZObject u, ZObject t, int range, out int sx, out int sy)
        {
            sx = sy = 0;
            var reach = W.Pathing?.Reach; var pf = W.Path;
            if (reach == null) return false;
            bool robot = u.IsRobot, nr = u.HasExplosives();
            int c = reach.UnitComp(u, nr);
            if (c < 0) return false;
            int n = w * h;
            if (seen == null || seen.Length != n) { seen = new int[n]; queue = new int[n]; seenStamp = 0; }
            if (++seenStamp == int.MaxValue) { Array.Clear(seen, 0, n); seenStamp = 1; }
            int head = 0, tail = 0;
            var (ftx, fty) = ZReach.FootTile(u.LocX, u.LocY);
            for (int r = 0; r <= 2 && tail == 0; r++)                                               // start: the own footprint (or the nearest of the component)
                for (int y = fty - r; y <= fty + r; y++)
                    for (int x = ftx - r; x <= ftx + r; x++)
                    {
                        if (Math.Max(Math.Abs(x - ftx), Math.Abs(y - fty)) != r || x < 0 || y < 0 || x >= w || y >= h) continue;
                        if (reach.Comp(x, y, robot, nr) != c || seen[y * w + x] == seenStamp) continue;
                        seen[y * w + x] = seenStamp; queue[tail++] = y * w + x;
                    }
            int exT = ExemptOf(t);
            long r2 = (long)range * range;
            while (head < tail)
            {
                int cur = queue[head++], cx = cur % w, cy = cur / w;
                var (px, py) = ZReach.FootCentre(cx, cy, robot);
                long dx = px - t.CenterX, dy = py - t.CenterY;
                if (dx * dx + dy * dy <= r2 && (t.IsDestroyableImpass || !W.EngageBarrierBetween(px, py, t.CenterX, t.CenterY)) &&
                    Clear(px, py, 0, t.CenterX, t.CenterY, exT))
                { sx = px; sy = py; return true; }
                for (int k = 0; k < 8; k++)
                {
                    int nx = cx + NX[k], ny = cy + NY[k];
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                    int ni = ny * w + nx;
                    if (seen[ni] == seenStamp || !pf.StepOk(cx, cy, nx, ny, robot, nr)) continue;
                    seen[ni] = seenStamp; queue[tail++] = ni;
                }
            }
            return false;
        }
        static readonly int[] NX = { 0, 1, 0, -1, 1, 1, -1, -1 }, NY = { -1, 0, 1, 0, -1, 1, 1, -1 };
    }

    public sealed partial class ZWorld
    {
        /// <summary>OpenZ deviation DV23 (line of sight for direct fire, ZSight). OFF = Zod (range + destroyable barriers only).
        /// Static like DuelDeviation; ZGame sets it at every start (PlayerPrefs oz_los_sim, off in 'Zod exact' oz_zod_exact = 1).</summary>
        public static bool LosDeviation = true;
        ZSight sight;
        /// <summary>The line-of-sight grid (built on first use, after the map objects exist).</summary>
        public ZSight Sight => sight ??= new ZSight(this);

        /// <summary>CreateObject hook (after Zod's SetMapImpassables): DV22 mesh tiles, DV23 sight blockers created later.</summary>
        void OnCreatedLosClearance(ZObject o)
        {
            Clearance?.OnCreated(o);
            if (sight != null && (o.Kind == ZKind.Building || o.Kind == ZKind.MapItem)) sight.Add(o);
        }
    }

    public partial class ZObject
    {
        /// <summary>DV23: this unit's current attack flies over obstacles: howitzer, Mobile Missile (MLRS), the fort gun, and a
        /// robot whose squad carries grenades (it throws them, ProcessAttackDamage).</summary>
        public bool ArcingFire =>
            (Kind == ZKind.Cannon && Type == "howitzer") || (Kind == ZKind.Vehicle && Type == "missile_launcher") || IsFort ||
            (Kind == ZKind.Robot && (Grenades > 0 || (Leader != null && Leader.Grenades > 0)));
        /// <summary>DV23 applies to this unit's fire right now (deviation on, direct fire).</summary>
        public bool LosActive => ZWorld.LosDeviation && World != null && !ArcingFire;
        /// <summary>DV23: a clear line to t for this unit's weapon (always true for arcing fire or with the deviation off).</summary>
        public bool HasLineOfSight(ZObject t) => t == null || !LosActive || World.Sight.Clear(this, t);
        /// <summary>
        /// The unit's EYES, whatever its weapon
        /// (arcing fire included): the same DV23 tile line. Every automatic move toward something (idle agro, attack-move picks,
        /// auto-grab of flags, empty vehicles / cannons and grenade crates) needs it; explicit player orders never do. Off with
        /// the DV23 switch ('Zod exact': Zod's blind radius checks).</summary>
        public bool CanSee(ZObject t) => t == null || !ZWorld.LosDeviation || !AutoSight || World == null || World.Sight.Clear(this, t);
        /// <summary></summary>
        public static bool AutoSight = true;
        /// <summary>Shots this unit held because the line to its target was blocked (checks).</summary>
        public int LosHeldShots { get; private set; }
        /// <summary>Attack orders that went to a spot with a clear shot / ended because no spot had one (checks).</summary>
        public int LosSpotPlans { get; private set; }
        public int LosDropped { get; private set; }

        /// <summary>DV23 hook in ProcessAttackDamage (after the cooldown): a direct shot without a line is not fired.</summary>
        bool LosHoldShot(ZObject t)
        {
            if (HasLineOfSight(t)) return false;
            LosHeldShots++;
            Disengage();
            return true;
        }

        /// <summary>DV23 hook in ProcessAttackWP's planning step. Zod's goal stays when the unit has a line to the target, or when
        /// Zod walks straight at a reachable target from further than range + LosPlanBand (the line comes on the way; the walk
        /// stops at the first spot in range WITH a line). Otherwise the goal becomes the nearest spot in range with a clear shot.
        /// Returns false when no reachable spot has a shot: the order ends (the unit picks another visible target).</summary>
        bool LosPlanAttack(ZObject t, bool zodDirect, ref int ex, ref int ey)
        {
            if (!LosActive) return true;
            bool line = HasLineOfSight(t);
            double d = Dist(CenterX, CenterY, t.CenterX, t.CenterY);
            if (zodDirect && (line || d > AttackRadius + LosPlanBand)) return true;
            if (line && d <= AttackRadius) return true;
            if (World.Sight.NearestShotSpot(this, t, Math.Max(8, AttackRadius - LosSpotSlack), out int sx, out int sy))
            { ex = sx; ey = sy; LosSpotPlans++; return true; }
            LosDropped++;
            return false;
        }
        /// <summary>DV23: beyond attack range + this (px) an attack order keeps Zod's straight walk at the target.</summary>
        public const int LosPlanBand = 64;
        /// <summary>DV23: a shot spot lies this far (px) inside the attack range (tile rounding).</summary>
        public const int LosSpotSlack = 4;
    }
}
