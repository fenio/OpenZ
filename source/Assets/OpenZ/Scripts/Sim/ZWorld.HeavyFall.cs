using System;
using System.Collections.Generic;

namespace OpenZ.Sim
{
    /// <summary>What came down (DV124).</summary>
    public enum ZHeavyFallKind { Turret, Shuttle, FortGun }

    /// <summary>One big faller coming down (DV124, read only for the view and the checks): where and when, what it covers on the
    /// ground, the trees it was going to fell (at its start) and the trees it felled (when it landed).</summary>
    public sealed class ZHeavyFall
    {
        public int seq; public ZHeavyFallKind kind; public int fallerId; public string fallerType = ""; public Team team;
        /// <summary>Rules time it started falling and the rules time it lands (the trees fall then).</summary>
        public double startTime, landTime;
        /// <summary>Where it comes down (px): the turret's debris point, the shuttle's hull middle, the fort cannon's pivot.</summary>
        public double x, y;
        /// <summary>Where it came from (px): the dead tank or gun, the ship when it was shot down, the fort's middle.</summary>
        public double fromX, fromY;
        /// <summary>Turret / shuttle: the circle it covers on the ground (px, the trunk's own radius included). Fort cannon: 0 (its pieces' outlines).</summary>
        public double radiusPx;
        /// <summary>Fort cannon: the toss heading the view flies it on (deg, the view's convention: 0 = -py, 90 = +px). Shuttle: its heading.</summary>
        public double headingDeg;
        /// <summary>Shuttle: the fall time used (s). Fort cannon: how far (deg / m) the rules moved the landing off the rocks (0 = as drawn).</summary>
        public double fallSec, movedDeg, movedM;
        /// <summary>Fort cannon: every piece's outline on the ground (px, x y pairs), outlineCounts points each.</summary>
        public double[] outline; public int[] outlineCounts;
        public bool landed;
        /// <summary>The standing trees it covered when it started (ids), and those it felled when it landed.</summary>
        public readonly List<int> targets = new List<int>(), trees = new List<int>();
        /// <summary>Checks: standing trees still inside its cover right after it landed (0 = none left).</summary>
        public int leftStanding;
    }

    /// <summary>
    /// OpenZ deviation DV124 "a big faller fells the tree it comes down on" (version 1.1 item 360). Zod has no trees in its rules; the
    /// trees of the rules are OpenZ's (DV88: level 16's tree tiles; DV67: level 2's palms and cacti; and Zod's own map-object cacti of
    /// level 2). Three things come down big, and the rules know where and when each one lands:
    ///  * a TURRET blown off a dead tank (light / medium / heavy) or gun (gun / howitzer / missile cannon / gatling): Zod's turret
    ///    missile (DeathDebris, vlight.cpp:261-278) is where and when the view lands it. At its blast every standing tree whose drawn
    ///    trunk lies within <see cref="TurretCoverM"/> of that point is destroyed (the turret lies on it). When the view moves turrets off
    ///    the rocks (<see cref="HeavyTurretRockClearM"/> above 0), a turret coming down that near a rock fells nothing (the view may land
    ///    it elsewhere);
    ///  * the SHUTTLE shot down in the air (DV118): its fall time from its height (at most <see cref="ShuttleFallMaxSec"/>) and its
    ///    velocity give where its hull comes down; the standing trees within <see cref="ShuttleCoverM"/> of that point are destroyed
    ///    when it lands. Destroyed on the ground (or just over its pad) nothing falls (its pad keeps every tree off);
    ///  * the FORT CANNON shot to pieces while its fort stands (DV25): the rules choose its toss (the heading of its aim, else the fort's
    ///    front; the fort's own seeded offset, distance and landing turn, exactly as the view draws the toss; a landing near a rock moved
    ///    to free ground the way the view's free landing searches, with the rocks' drawn reach), and the trees under the landed cannon's
    ///    pieces are destroyed when it lands (<see cref="FortGunTossSec"/> later). With its fort (the game over) the rules decide nothing:
    ///    the view draws it.
    /// A tree is destroyed by the rules' own death path (health 0, UpdateObjectHealth: the Death event with the faller as the attacker,
    /// KillAt, the tile opens as it does for any destroyed tree). The drawn trunks are known exactly (ZHeavyFallData, read from the
    /// scenes and the placement). No random draw of Zod's stream is used; the fort's own toss numbers come from the same seeded
    /// generator the view uses. Nothing changes in a match where no big faller comes down on a tree. Static switch like the other
    /// deviations (ZGame: off in 'Zod exact'); off = no record, no tree destroyed, exactly as before.
    /// </summary>
    public sealed partial class ZWorld
    {
        /// <summary>DV124 switch (off = the big fallers fell no tree: as before).</summary>
        public static bool HeavyFallDeviation = true;
        /// <summary>The trunk's own radius added to every cover (px; 0.35 m).</summary>
        public const double HeavyTrunkPx = 2.8;
        /// <summary>Shuttle: the hull's outline radius (m), the longest fall it is given (s, the view's drop lasts at most 1.5 s) and the
        /// lowest height that counts as a fall (m: below it the ship is just off its pad).</summary>
        public const double ShuttleCoverM = 3.9, ShuttleFallMaxSec = 1.5, ShuttleFallMinM = 1.5, HeavyGravity = 9.81;
        /// <summary>The fort cannon's toss (FortGunView.tossDuration, s).</summary>
        public const double FortGunTossSec = 3.0;
        /// <summary>The view's free landing of the fort cannon (View/OzRockSolid.FreeLanding): its clear air to a rock's drawn box (m),
        /// the share of the flight whose end must miss the rocks and its clear air, the slack for where the gun starts on the roof.</summary>
        public const double FortGunRockMarginM = 0.5, FortGunArcFrom = 0.8, FortGunArcMarginM = 0.4, FortGunFromSlackM = 1.0;
        /// <summary>A turret coming down within this distance (m) of a rock's tile fells no tree (0 = no such rule: the default). The game
        /// sets it when its view moves turrets off the rocks (the turret's landed disc, its skid and the rocks' drawn reach).</summary>
        public static double HeavyTurretRockClearM = 0.0;
        public const int HeavyFallsKept = 64;
        /// <summary>The last big fallers (oldest first, at most HeavyFallsKept).</summary>
        public readonly List<ZHeavyFall> HeavyFalls = new List<ZHeavyFall>();
        public int HeavyFallSeq { get; private set; }
        /// <summary>Trees destroyed by big fallers so far; turrets near a rock that fell nothing; fort cannon tosses the rules left to
        /// the view (no free place found).</summary>
        public int HeavyFallTrees { get; private set; }
        public int HeavyTurretsByRock { get; private set; }
        public int HeavyFallGunLeftToView { get; private set; }
        /// <summary>Checks only: a heading (deg) the next fort cannon toss uses instead of its aim (NaN = none).</summary>
        public static double HeavyGunHeadingTest = double.NaN;
        readonly List<ZHeavyFall> heavyPending = new List<ZHeavyFall>();
        readonly Dictionary<int, ZHeavyFall> heavyOfTree = new Dictionary<int, ZHeavyFall>(), heavyGunToss = new Dictionary<int, ZHeavyFall>();
        readonly HashSet<int> heavyGunSeen = new HashSet<int>();
        readonly List<ZObject> heavyTrees = new List<ZObject>(); readonly List<double> heavyFeet = new List<double>();
        readonly List<ZObject> heavyForts = new List<ZObject>();
        bool heavyBuilt;

        /// <summary>Turns the rule off until it is disposed, then puts back what was there (the parity report holds one while it measures).</summary>
        public sealed class HeavyFallHold : IDisposable
        {
            readonly bool was;
            internal HeavyFallHold() { was = HeavyFallDeviation; HeavyFallDeviation = false; }
            public void Dispose() { HeavyFallDeviation = was; }
        }
        public static HeavyFallHold HeavyFallOff() => new HeavyFallHold();

        /// <summary>The faller that destroyed (or is about to destroy) this tree (null = none).</summary>
        public ZHeavyFall HeavyFallOfTree(int id) => heavyOfTree.TryGetValue(id, out var f) ? f : null;
        /// <summary>The rules' toss of this fort's cannon shot to pieces while the fort stands (null = none: the view tosses it its own way).</summary>
        public ZHeavyFall HeavyFallGunToss(int fortId) => heavyGunToss.TryGetValue(fortId, out var f) ? f : null;
        /// <summary>The latest fall of this faller (null = none).</summary>
        public ZHeavyFall HeavyFallOf(int fallerId)
        {
            for (int i = HeavyFalls.Count - 1; i >= 0; i--) if (HeavyFalls[i].fallerId == fallerId) return HeavyFalls[i];
            return null;
        }

        /// <summary>The ground a blown-off turret covers round its debris point (m, the trunk's radius included): half the short side
        /// of the turret's body as it lies on its side, so the turret lies on every tree it fells, whichever way it lies.</summary>
        public static double TurretCoverM(string type)
        {
            switch (type)
            {
                case "light": return 1.0;
                case "medium": return 1.2;
                case "heavy": return 1.4;
                case "gun": return 1.2;
                case "howitzer": return 1.4;
                case "missile_cannon": return 1.1;
                case "gatling": return 1.0;
                default: return 0.0;
            }
        }
        public static bool IsTurretFaller(ZObject a) => a != null && ((a.Kind == ZKind.Vehicle && (a.Type == "light" || a.Type == "medium" || a.Type == "heavy")) || a.Kind == ZKind.Cannon) && TurretCoverM(a.Type) > 0;

        // ------------------------------------------------------------------ the trees the rules know, with their drawn trunks
        void HeavyBuild()
        {
            if (heavyBuilt) return;
            heavyBuilt = true;
            var data = Map != null ? ZHeavyFallData.For(Map.file) : null;
            var foot = new Dictionary<string, int>();
            if (data != null) for (int i = 0; i < data.Names.Length && 2 * i + 1 < data.Quarter.Length; i++) foot[data.Names[i]] = i;
            var added = new HashSet<int>();
            void Add(ZObject o, string key)
            {
                if (o == null || !added.Add(o.Id)) return;
                double fx = o.CenterX, fy = o.CenterY;
                if (key != null && foot.TryGetValue(key, out int i)) { fx = data.Quarter[2 * i] * 0.25; fy = data.Quarter[2 * i + 1] * 0.25; }
                heavyTrees.Add(o); heavyFeet.Add(fx); heavyFeet.Add(fy);
            }
            foreach (var o in TreeTiles) { DetailPropUid.TryGetValue(o.Id, out var uid); Add(o, uid); }
            foreach (var o in DetailProps)
            {
                if (treeTileIds.Contains(o.Id) || !DetailPropUid.TryGetValue(o.Id, out var uid) || uid == null) continue;
                string u = uid.ToLowerInvariant();
                if (u.Contains("palm") || u.Contains("cactus")) Add(o, uid);
            }
            if (data != null)
                foreach (var o in Objects)
                    if (o.Type != null && o.Type.StartsWith("map_object") && foot.ContainsKey(o.Type + "#" + o.Id)) Add(o, o.Type + "#" + o.Id);
            foreach (var o in Objects) if (o.IsFort) heavyForts.Add(o);
        }

        /// <summary>Checks: the trees a big faller can fell (standing or not), their drawn trunks (px).</summary>
        public int HeavyTreeCount { get { HeavyBuild(); return heavyTrees.Count; } }
        public ZObject HeavyTreeAt(int i, out double fx, out double fy) { HeavyBuild(); fx = heavyFeet[2 * i]; fy = heavyFeet[2 * i + 1]; return heavyTrees[i]; }
        public bool HeavyTreeStanding(ZObject o) => o != null && !o.Destroyed && o.KillAt < 0 && Get(o.Id) == o;

        /// <summary>Is the point (px) inside what this faller covers?</summary>
        public static bool HeavyCovers(ZHeavyFall f, double px, double py)
        {
            if (f.kind != ZHeavyFallKind.FortGun) { double dx = px - f.x, dy = py - f.y; return dx * dx + dy * dy <= f.radiusPx * f.radiusPx; }
            if (f.outline == null) return false;
            int at = 0;
            for (int k = 0; k < f.outlineCounts.Length; k++)
            {
                int n = f.outlineCounts[k];
                if (n >= 3 && NearPolygon(f.outline, at, n, px, py, HeavyTrunkPx)) return true;
                at += 2 * n;
            }
            return false;
        }

        static bool NearPolygon(double[] p, int at, int n, double x, double y, double r)
        {
            bool inside = false;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                double xi = p[at + 2 * i], yi = p[at + 2 * i + 1], xj = p[at + 2 * j], yj = p[at + 2 * j + 1];
                if ((yi > y) != (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi) inside = !inside;
                double ex = xj - xi, ey = yj - yi, l = ex * ex + ey * ey;
                double t = l > 1e-9 ? Math.Max(0.0, Math.Min(1.0, ((x - xi) * ex + (y - yi) * ey) / l)) : 0.0;
                double qx = xi + ex * t - x, qy = yi + ey * t - y;
                if (qx * qx + qy * qy <= r * r) return true;
            }
            return inside;
        }

        ZHeavyFall NewHeavyFall(ZHeavyFallKind kind, ZObject faller, double landTime)
        {
            var f = new ZHeavyFall { seq = ++HeavyFallSeq, kind = kind, fallerId = faller.Id, fallerType = faller.Type ?? "", team = faller.Owner, startTime = Time, landTime = landTime };
            HeavyFalls.Add(f);
            if (HeavyFalls.Count > HeavyFallsKept) HeavyFalls.RemoveAt(0);
            return f;
        }

        /// <summary>The standing trees under this fall (ids, in the trees' order).</summary>
        public List<int> HeavyTreesUnder(ZHeavyFall f)
        {
            HeavyBuild();
            var l = new List<int>();
            for (int i = 0; i < heavyTrees.Count; i++)
                if (HeavyTreeStanding(heavyTrees[i]) && HeavyCovers(f, heavyFeet[2 * i], heavyFeet[2 * i + 1])) l.Add(heavyTrees[i].Id);
            return l;
        }

        void HeavyTargets(ZHeavyFall f)
        {
            foreach (int id in HeavyTreesUnder(f)) { f.targets.Add(id); if (!heavyOfTree.ContainsKey(id)) heavyOfTree[id] = f; }
        }

        /// <summary>It lands: every standing tree inside its cover is destroyed by the rules' own death path, the faller the attacker.</summary>
        void HeavyLand(ZHeavyFall f)
        {
            f.landed = true;
            HeavyBuild();
            for (int i = 0; i < heavyTrees.Count; i++)
            {
                var o = heavyTrees[i];
                if (!HeavyTreeStanding(o) || !HeavyCovers(f, heavyFeet[2 * i], heavyFeet[2 * i + 1])) continue;
                heavyOfTree[o.Id] = f;
                f.trees.Add(o.Id); HeavyFallTrees++;
                o.SetHealth(0); UpdateObjectHealth(o, f.fallerId);
            }
            for (int i = 0; i < heavyTrees.Count; i++)
                if (HeavyTreeStanding(heavyTrees[i]) && HeavyCovers(f, heavyFeet[2 * i], heavyFeet[2 * i + 1])) f.leftStanding++;
        }

        // ------------------------------------------------------------------ the hooks
        /// <summary>ProcessMissileDamage hook (before the blast's damage): a turret missile of a dead tank or gun lands its turret here and now.</summary>
        void HeavyFallTurret(ZMissile m)
        {
            if (!HeavyFallDeviation || m == null || !m.debris || m.team != Team.Neutral) return;
            var a = GetAny(m.attackerId);
            if (!IsTurretFaller(a)) return;
            var f = NewHeavyFall(ZHeavyFallKind.Turret, a, Time);
            f.x = m.x; f.y = m.y; f.fromX = a.CenterX; f.fromY = a.CenterY; f.radiusPx = TurretCoverM(a.Type) * 8.0;
            if (HeavyTurretByRock(f.x, f.y, f.fromX, f.fromY))
            {   // the view may move this turret off the rock: it fells nothing here
                f.landed = true; HeavyTurretsByRock++;
                return;
            }
            HeavyTargets(f);
            HeavyLand(f);
        }

        /// <summary>Does a turret coming down at (x, y) from (fromX, fromY) (px) fell nothing because a rock is near (<see cref="HeavyTurretRockClearM"/>)?</summary>
        public bool HeavyTurretByRock(double x, double y, double fromX, double fromY) => HeavyTurretRockClearM > 0 && HeavyRockNear(x, y, fromX, fromY, HeavyTurretRockClearM);

        /// <summary>A standing rock's tile within r (m) of the point (px), or within 0.75 r of the turret's flat path from `from` (its last 85 %).</summary>
        bool HeavyRockNear(double x, double y, double fromX, double fromY, double r)
        {
            double px = x / 8.0, pz = -y / 8.0, ax = fromX / 8.0, az = -fromY / 8.0;
            foreach (var o in Objects)
            {
                if (o.Type != "rock" || o.Destroyed) continue;
                double x0 = o.LocX / 8.0, x1 = (o.LocX + o.WidthPix) / 8.0, z0 = -(o.LocY + o.HeightPix) / 8.0, z1 = -o.LocY / 8.0;
                if (BoxDist(x0, x1, z0, z1, px, pz) <= r) return true;
                for (int s = 0; s <= 8; s++)
                {
                    double k = 0.15 + 0.85 * s / 8.0, qx = ax + (px - ax) * k, qz = az + (pz - az) * k;
                    if (BoxDist(x0, x1, z0, z1, qx, qz) <= r * 0.75) return true;
                }
            }
            return false;
        }

        static double BoxDist(double x0, double x1, double z0, double z1, double x, double z)
        {
            double dx = x < x0 ? x0 - x : x > x1 ? x - x1 : 0.0, dz = z < z0 ? z0 - z : z > z1 ? z - z1 : 0.0;
            return Math.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>Where a shuttle shot down now comes down (px) and after how long (s); false = it is not falling (on or just over the ground).</summary>
        public static bool HeavyShuttlePoint(ZObject o, out double x, out double y, out double T)
        {
            x = y = T = 0;
            if (o == null || !o.IsShuttle || !o.ShuttleAirborne) return false;
            double h = Math.Max(0.0, o.ShuttleHeightM);
            if (h < ShuttleFallMinM) return false;                                             // just off its pad (the pad keeps every tree off): as on the ground
            T = Math.Min(ShuttleFallMaxSec, Math.Sqrt(2.0 * h / HeavyGravity));
            x = o.CenterX + o.Dx * T; y = o.CenterY + o.Dy * T;
            return true;
        }

        /// <summary>ShuttleDown hook: shot down in the air, the hull comes down after its fall time, carried on by its velocity.</summary>
        void HeavyFallShuttle(ZObject o)
        {
            if (!HeavyFallDeviation || !HeavyShuttlePoint(o, out double x, out double y, out double T)) return;
            var f = NewHeavyFall(ZHeavyFallKind.Shuttle, o, Time + T);
            f.fromX = o.CenterX; f.fromY = o.CenterY; f.x = x; f.y = y;
            f.radiusPx = ShuttleCoverM * 8.0; f.fallSec = T; f.headingDeg = o.ShuttleHeadingDeg;
            HeavyTargets(f);
            heavyPending.Add(f);
        }

        /// <summary>Tick hook (right after the missiles): a fort cannon shot to pieces this tick, and the falls that land now.</summary>
        void HeavyFallTick()
        {
            if (!HeavyFallDeviation) return;
            HeavyFallSync();
            for (int i = 0; i < heavyPending.Count;)
            {
                var f = heavyPending[i];
                if (Time >= f.landTime) { heavyPending.RemoveAt(i); HeavyLand(f); }
                else i++;
            }
        }

        /// <summary>A fort cannon shot to pieces while its fort stands gets the rules' toss now (called by the tick; a check that kills a
        /// gun itself calls it at once).</summary>
        public void HeavyFallSync()
        {
            if (!HeavyFallDeviation) return;
            HeavyBuild();
            for (int k = 0; k < heavyForts.Count; k++)
            {
                var fort = heavyForts[k];
                if (fort.FortGunKilledAt < 0 || heavyGunSeen.Contains(fort.Id)) continue;
                heavyGunSeen.Add(fort.Id);
                if (fort.Destroyed) continue;                                                  // with its fort: the game is over, the view draws it
                var f = HeavyGunToss(fort, FortGunHeading(fort), true);
                if (f == null) { HeavyFallGunLeftToView++; continue; }
                heavyGunToss[fort.Id] = f; HeavyTargets(f); heavyPending.Add(f);
            }
        }

        // ------------------------------------------------------------------ the fort cannon's toss (FortGunView.StartToss's numbers)
        /// <summary>The heading of the fort cannon's aim (deg, the view's convention): its target (or the point it committed to), else
        /// the last point it aimed at, else its fort's front.</summary>
        public static double FortGunHeading(ZObject fort)
        {
            if (!double.IsNaN(HeavyGunHeadingTest)) { double h = HeavyGunHeadingTest; HeavyGunHeadingTest = double.NaN; return h; }
            int tx = 0, ty = 0; bool any = false;
            var t = fort.FortGunTarget;
            if (t != null) { tx = fort.FortGunCommitted ? fort.FortGunAimX : t.CenterX; ty = fort.FortGunCommitted ? fort.FortGunAimY : t.CenterY; any = true; }
            else if (fort.FortGunAimX != 0 || fort.FortGunAimY != 0) { tx = fort.FortGunAimX; ty = fort.FortGunAimY; any = true; }
            if (any)
            {
                double dx = (tx - fort.CenterX) / 8.0, dz = -(ty - fort.CenterY) / 8.0;
                if (dx * dx + dz * dz > 0.25) return Math.Atan2(dx, dz) * 180.0 / Math.PI;
            }
            return fort.Type == "fort_front" ? 180.0 : 0.0;
        }

        /// <summary>The fort cannon's toss for a gun heading (deg): FortGunView.StartToss's own seeded offset, distance and landing turn; a
        /// landing whose cannon (or its flight's end) would come near a rock moved the way the view's free landing searches (10 deg / 1.5 m
        /// steps round the fort, never into a building or a gun), with the rocks' drawn reach past their tiles, so the view finds the place
        /// free and keeps it. Null = no free place (left to the view). `record` = a real toss (else a preview for the checks).</summary>
        public ZHeavyFall HeavyGunToss(ZObject fort, double heading, bool record)
        {
            // the view: c = CenterWorld(Fort, false); half = max side / 16; hdg = heading + offset; land = c + (sin, cos)(hdg) x (half + 3 + 3 r);
            // tossEndRot = Euler(x, y, z) from the same seeded generator, in that order
            double cx = (fort.PosX + fort.WidthPix * 0.5) / 8.0, cz = -(fort.PosY + fort.HeightPix * 0.5) / 8.0;
            float half = Math.Max(fort.WidthPix, fort.HeightPix) / 16f;
            var rnd = new Random(fort.Id * 7919 + 17);
            float hdg = (float)heading + (float)(rnd.NextDouble() * 140.0 - 70.0);
            float dist = half + 3f + (float)rnd.NextDouble() * 3f;
            double ex = rnd.NextDouble() * 50.0 - 25.0, ey = rnd.NextDouble() * 360.0, sg = rnd.Next(2) == 0 ? -1.0 : 1.0, ez = sg * (80.0 + rnd.NextDouble() * 25.0);
            var R = EulerMatrix(ex, ey, ez);
            // the landed cannon's pieces round its pivot (m, turned as it lands), and their box
            var counts = ZHeavyFallData.GunPieceCounts; var pts = ZHeavyFallData.GunPoints;
            var rel = new List<(double x, double z)>(pts.Length / 3);
            double bx0 = double.MaxValue, bx1 = double.MinValue, bz0 = double.MaxValue, bz1 = double.MinValue;
            for (int at = 0; at + 2 < pts.Length; at += 3)
            {
                double px = pts[at], py = pts[at + 1], pz = pts[at + 2];
                double wx = R[0] * px + R[1] * py + R[2] * pz, wz = R[6] * px + R[7] * py + R[8] * pz;
                rel.Add((wx, wz));
                bx0 = Math.Min(bx0, wx); bx1 = Math.Max(bx1, wx); bz0 = Math.Min(bz0, wz); bz1 = Math.Max(bz1, wz);
            }
            // the search (OzRockSolid.FreeLanding's order): the drawn place, then 10 deg steps (up to +-120) and 1.5 m steps out (up to 6 m)
            double best = double.NaN, bestDh = 0, bestDd = 0;
            for (int pass = 0; pass < 2 && double.IsNaN(best); pass++)
            {
                if (GunPlaceFree(cx, cz, hdg, dist, bx0, bx1, bz0, bz1, false, pass == 0)) { best = hdg; break; }
                for (int k = 1; k <= 16 && double.IsNaN(best); k++)
                    for (int i = 0; i <= 4 && i <= k && double.IsNaN(best); i++)
                    {
                        int j = k - i; if (j > 12) continue;
                        for (int s = 0; s < (j == 0 ? 1 : 2); s++)
                        {
                            double dh = (s == 0 ? 10.0 : -10.0) * j, dd = 1.5 * i;
                            if (!GunPlaceFree(cx, cz, hdg + dh, dist + dd, bx0, bx1, bz0, bz1, true, pass == 0)) continue;
                            best = hdg + dh; bestDh = dh; bestDd = dd; break;
                        }
                    }
            }
            if (double.IsNaN(best)) return null;
            double a = best * Math.PI / 180.0, lx = cx + Math.Sin(a) * (dist + bestDd), lz = cz + Math.Cos(a) * (dist + bestDd);
            var outline = new List<double>(); var outCounts = new int[counts.Length];
            int n0 = 0;
            for (int k = 0; k < counts.Length; k++)
            {
                var flat = new List<(double x, double z)>(counts[k]);
                for (int i = 0; i < counts[k] && n0 + i < rel.Count; i++) flat.Add((lx + rel[n0 + i].x, lz + rel[n0 + i].z));
                n0 += counts[k];
                var hull = Hull2D(flat);
                outCounts[k] = hull.Count;
                foreach (var q in hull) { outline.Add(q.x * 8.0); outline.Add(-q.z * 8.0); }
            }
            var f = record ? NewHeavyFall(ZHeavyFallKind.FortGun, fort, fort.FortGunKilledAt + FortGunTossSec)
                           : new ZHeavyFall { kind = ZHeavyFallKind.FortGun, fallerId = fort.Id, fallerType = fort.Type ?? "", team = fort.Owner, startTime = Time, landTime = Time + FortGunTossSec };
            f.x = lx * 8.0; f.y = -lz * 8.0; f.fromX = fort.CenterX; f.fromY = fort.CenterY; f.headingDeg = (float)best; f.movedDeg = bestDh; f.movedM = bestDd;
            f.outline = outline.ToArray(); f.outlineCounts = outCounts;
            return f;
        }

        /// <summary>Is the landing at (heading, distance) from the fort's middle free: its box (+ the clear air + the rocks' drawn reach)
        /// clear of every standing rock's tile, with `arc` its flight's end too; a `moved` place also clear of every building's and gun's
        /// footprint (+ the clear air).</summary>
        bool GunPlaceFree(double cx, double cz, double hdgDeg, double dist, double bx0, double bx1, double bz0, double bz1, bool moved, bool arc)
        {
            double a = hdgDeg * Math.PI / 180.0, lx = cx + Math.Sin(a) * dist, lz = cz + Math.Cos(a) * dist;
            double x0 = lx + bx0, x1 = lx + bx1, z0 = lz + bz0, z1 = lz + bz1;
            double m = FortGunRockMarginM + ZHeavyFallData.RockReachM;
            // the flight's end: the box swept from FortGunArcFrom of the way to the landing (the gun starts on the fort's roof)
            double sx = cx + (lx - cx) * FortGunArcFrom, sz = cz + (lz - cz) * FortGunArcFrom;
            double ax0 = Math.Min(x0, sx + bx0) - FortGunFromSlackM, ax1 = Math.Max(x1, sx + bx1) + FortGunFromSlackM;
            double az0 = Math.Min(z0, sz + bz0) - FortGunFromSlackM, az1 = Math.Max(z1, sz + bz1) + FortGunFromSlackM;
            double ma = FortGunArcMarginM + ZHeavyFallData.RockReachM;
            foreach (var o in Objects)
            {
                if (o.Destroyed) continue;
                double rx0 = o.LocX / 8.0, rx1 = (o.LocX + o.WidthPix) / 8.0, rz0 = -(o.LocY + o.HeightPix) / 8.0, rz1 = -o.LocY / 8.0;
                if (o.Type == "rock")
                {
                    if (x1 + m > rx0 && x0 - m < rx1 && z1 + m > rz0 && z0 - m < rz1) return false;
                    if (arc && ax1 + ma > rx0 && ax0 - ma < rx1 && az1 + ma > rz0 && az0 - ma < rz1) return false;
                }
                else if (moved && (o.Kind == ZKind.Building || o.Kind == ZKind.Cannon) && !o.IsBridge)
                {
                    double mb = FortGunRockMarginM;
                    if (x1 + mb > rx0 && x0 - mb < rx1 && z1 + mb > rz0 && z0 - mb < rz1) return false;
                }
            }
            return true;
        }

        /// <summary>Unity's Quaternion.Euler(x, y, z) as a row-major 3 x 3 matrix (z first, then x, then y).</summary>
        static double[] EulerMatrix(double xDeg, double yDeg, double zDeg)
        {
            double x = xDeg * Math.PI / 180.0, y = yDeg * Math.PI / 180.0, z = zDeg * Math.PI / 180.0;
            double cx = Math.Cos(x), sx = Math.Sin(x), cy = Math.Cos(y), sy = Math.Sin(y), cz = Math.Cos(z), sz = Math.Sin(z);
            // Ry * Rx * Rz
            return new[]
            {
                cy * cz + sy * sx * sz, -cy * sz + sy * sx * cz, sy * cx,
                cx * sz, cx * cz, -sx,
                -sy * cz + cy * sx * sz, sy * sz + cy * sx * cz, cy * cx,
            };
        }

        /// <summary>Convex hull of points on the ground (monotone chain), counter-clockwise.</summary>
        static List<(double x, double z)> Hull2D(List<(double x, double z)> p)
        {
            p.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.z.CompareTo(b.z));
            var h = new List<(double x, double z)>(p.Count + 1);
            double Cross((double x, double z) o, (double x, double z) a, (double x, double z) b) => (a.x - o.x) * (b.z - o.z) - (a.z - o.z) * (b.x - o.x);
            for (int i = 0; i < p.Count; i++) { while (h.Count >= 2 && Cross(h[h.Count - 2], h[h.Count - 1], p[i]) <= 0) h.RemoveAt(h.Count - 1); h.Add(p[i]); }
            for (int i = p.Count - 2, lo = h.Count + 1; i >= 0; i--) { while (h.Count >= lo && Cross(h[h.Count - 2], h[h.Count - 1], p[i]) <= 0) h.RemoveAt(h.Count - 1); h.Add(p[i]); }
            if (h.Count > 1) h.RemoveAt(h.Count - 1);
            return h;
        }
    }
}
