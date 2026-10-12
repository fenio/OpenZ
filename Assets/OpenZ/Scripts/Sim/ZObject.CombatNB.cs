using System;
using System.Collections.Generic;

namespace OpenZ.Sim
{
    /// <summary>
    /// switch on ZWorld (ZGame sets them at every start from PlayerPrefs; all off in 'Zod exact' oz_zod_exact = 1 and in the
    /// parity report's Zod rows). Pure sim: integer / double maths on the pixel grid, sim time, Zod's rand() stream never drawn
    /// by any of them (a toggle never changes the number of draws), no UnityEngine.
    /// <list type="bullet">
    /// <item><b>DV25 fort mortar</b> (NB27, on top of DV7 + fortgun_v2): the fort's big gun reloads FortMortarReload (30 s), lobs a slow
    /// ballistic shell (flight = a 60 deg lob under FortMortarGravity from the roof, 2.9-4.2 s) at where the target stands (no lead:
    /// it can be dodged; the target still gets Zod's DodgeMissile call), and it can be destroyed: every enemy shell that damages the
    /// fort also damages its gun (FortGunMaxHealth). Destroyed = silent for the rest of the game.</item>
    /// <item><b>DV26 building reach</b> (NB14): range to a building is measured to its walls (the building's own mesh tiles, the
    /// ZClearance coverage table, the gate included), not to its centre (Zod: points_within_distance centre to centre,
    /// zobject.cpp:4760-4765 WithinAttackRadius); the shell is aimed at the nearest wall (4 px inside) and the building's own blast
    /// damage falls off from its walls (Zod: from its centre, zserver.cpp:1080-1098), so a tank shells the nearest wall section as soon
    /// as it is in range instead of driving up to it.</item>
    /// <item><b>DV27 exposed drivers</b> (NB11): Zod has no rear-hit or target-preference rule (zobject.cpp:1603-1853 engages the first
    /// enemy in range in object order; the snipe roll zobject.cpp:2190 has no direction; zvehicle.cpp:178-186 CanBeSniped is lid only).
    /// OpenZ: a snipe-capable unit (Zod's can_snipe: grunt, psycho, sniper, pyro, laser, jeep, gatling) auto-targets an enemy in range
    /// whose driver is exposed (CanBeSniped: jeep / gatling crew, a tank with its lid open) before any other, and a hit from behind
    /// (the attacker inside the rear RearDeg cone of where the vehicle looks: its target, else its last heading) rolls the snipe
    /// with RearSnipeFactor x Zod's chance (capped): flanking pays.</item>
    /// <item><b>DV28 attack approach</b> (NB13): an attack order on a target out of range walks to the NEAREST reachable spot in range
    /// (with a clear line for direct fire) instead of Zod's walk at the target itself (NearestAttackLoc, zobject.cpp:1985-2040), so
    /// a unit never takes the long way round (a far bridge) to reach a target it can already shoot across a river, and its path ends
    /// where it can fire (the bridge queue and lanes never hold it one pixel out of range).</item>
    /// <item><b>DV29 armour chip</b> (NB5 proposal, default OFF): bullets of the light automatic weapons (grunt, psycho, jeep,
    /// gatling) deal ArmourChipFactor x damage to vehicle / cannon hulls. The measured time-to-kill table matches Zod without it.</item>
    /// </list>
    /// Also (not a deviation, view data only): LastEnemyHit, the sim time a unit / building last took damage from an enemy team
    /// (FortAlert NB31: "our fort is taking fire" only after a real hit).
    /// </summary>
    public partial class ZObject
    {
        // ================================================================== NB31: last enemy hit (view data, no gameplay effect)
        /// <summary>Sim time this object last lost health to an enemy team's shot or blast (-100 = never). Debris (neutral) and
        /// friendly blasts never count.</summary>
        public double LastEnemyHit = -100;

        // ================================================================== DV25 fort mortar
        /// <summary>DV25: hit points of the fort's big gun (every enemy shell that damages the fort damages its gun by as much).</summary>
        public int FortGunHealth = FortGunMaxHealth;
        public const int FortGunMaxHealth = 16000;
        /// <summary>DV25: seconds between two shots (Zod howitzer: 4.86).</summary>
        public const double FortMortarReload = 30.0;
        /// <summary>DV25: the lob (sim flight time): 60 deg launch from FortMortarMuzzleM above the ground under FortMortarGravity.</summary>
        public const double FortMortarGravity = 6.0, FortMortarMuzzleM = 10.0, Tan60 = 1.7320508075688772;
        /// <summary>Sim time of the fort gun's last shot (-100 = none): the view's reload ring.</summary>
        public double FortGunLastShot = -100;
        /// <summary>DV25: the gun is destroyed (silent for the rest of the game).</summary>
        public bool FortGunDestroyed => IsFort && ZWorld.FortGunDeviation && ZWorld.FortMortarDeviation && FortGunHealth <= 0;
        /// <summary>DV25: reload progress 0..1 (1 = ready), for the view's ring.</summary>
        public double FortGunReload01 => !ZWorld.FortMortarDeviation || FortGunLastShot < 0 || World == null ? 1.0
            : Math.Max(0.0, Math.Min(1.0, (World.Time - FortGunLastShot) / FortMortarReload));

        /// <summary>DV25: flight time (s) of the lob to a point distPx away (m = px / 8): T = sqrt(2 (h tan 60 + H) / g).</summary>
        public static double FortMortarFlight(double distPx) => Math.Sqrt(2.0 * (distPx / 8.0 * Tan60 + FortMortarMuzzleM) / FortMortarGravity);

        /// <summary>DV25: an enemy shell damaged the fort by `damage`: its gun takes the same.</summary>
        internal void FortGunTakeHit(int damage)
        {
            if (!IsFort || !ZWorld.FortGunDeviation || !ZWorld.FortMortarDeviation || damage <= 0 || FortGunHealth <= 0) return;
            FortGunHealth -= damage;
            if (FortGunHealth <= 0) { FortGunHealth = 0; FortGunTarget = null; FortGunKilledAt = World.Time; }
        }
        /// <summary>DV25: sim time the gun was destroyed (-1 = intact).</summary>
        public double FortGunKilledAt = -1;

        // ================================================================== DV26 building reach
        static readonly Dictionary<string, (int dx, int dy)[]> solidTiles = new Dictionary<string, (int, int)[]>();

        /// <summary>DV26: the building's wall tiles (rect-relative): its mesh coverage >= 40 % (the gate corridor 'o' included), from
        /// ZClearance.Mesh; a type without a table (bridges) = its whole rect.</summary>
        (int dx, int dy)[] SolidTiles()
        {
            if (solidTiles.TryGetValue(Type, out var s)) return s;
            var l = new List<(int, int)>();
            if (ZClearance.Mesh.TryGetValue(Type, out var rows))
            {
                for (int y = 0; y < rows.Length; y++)
                    for (int x = 0; x < rows[y].Length; x++)
                    {
                        char c = rows[y][x];
                        if (c == '#' || c == 'o' || (c >= '0' + ZClearance.SightCover && c <= '9')) l.Add((x, y));
                    }
            }
            if (l.Count == 0) for (int y = 0; y < HeightPix >> 4; y++) for (int x = 0; x < WidthPix >> 4; x++) l.Add((x, y));
            s = l.ToArray(); solidTiles[Type] = s;
            return s;
        }

        /// <summary>DV26: the point of the building's walls nearest to (x, y) (inside it when (x, y) is) and its squared distance.</summary>
        public long WallPoint(int x, int y, out int wx, out int wy)
        {
            long best = long.MaxValue; wx = CenterX; wy = CenterY;
            foreach (var (dx, dy) in SolidTiles())
            {
                int x0 = LocX + dx * 16, y0 = LocY + dy * 16;
                int px = Math.Max(x0, Math.Min(x, x0 + 15)), py = Math.Max(y0, Math.Min(y, y0 + 15));
                long d = (long)(px - x) * (px - x) + (long)(py - y) * (py - y);
                if (d < best) { best = d; wx = px; wy = py; if (d == 0) break; }
            }
            return best;
        }

        /// <summary>DV26 applies to shots at this object: a building, the switch on.</summary>
        internal bool ReachToWalls => ZWorld.BuildingReachDeviation && Kind == ZKind.Building;

        /// <summary>Range test to o from (x, y): Zod's centre-to-centre points_within_distance, or (DV26) to a building's walls.</summary>
        internal bool InReach(ZObject o, int x, int y, double range)
        {
            if (!o.ReachToWalls) return Within(x, y, o.CenterX, o.CenterY, range);
            return o.WallPoint(x, y, out _, out _) <= (long)(range * range);
        }

        /// <summary>DV26: where a shell at building t is aimed from here: the nearest wall point, 4 px into the wall.</summary>
        void WallAim(ZObject t, out int tx, out int ty)
        {
            t.WallPoint(CenterX, CenterY, out tx, out ty);
            int dx = t.CenterX - tx, dy = t.CenterY - ty;
            if (dx != 0 || dy != 0) { double l = Math.Sqrt((double)dx * dx + (double)dy * dy); tx += (int)Math.Round(dx / l * 4); ty += (int)Math.Round(dy / l * 4); }
        }

        /// <summary>DV26: the missile's own target building takes its blast from its walls (0 when the blast is on / in them).</summary>
        internal float WallBlastDistance(int x, int y) => (float)Math.Sqrt((double)WallPoint(x, y, out _, out _));

        // ================================================================== DV27 exposed drivers
        /// <summary>DV27: last heading (px/s direction) this unit moved in; (0, 0) = never moved.</summary>
        public float FaceX, FaceY;
        /// <summary>ProcessMove: remember the heading (no gameplay effect unless DV27 is on).</summary>
        void NoteFacing() { if (!IsZ(Dx) || !IsZ(Dy)) { FaceX = Dx; FaceY = Dy; } }

        /// <summary>DV27: a is inside the rear cone of this vehicle / cannon (where it looks: its attack target, else its last heading).</summary>
        internal bool HitFromBehind(ZObject a)
        {
            if (a == null) return false;
            double fx, fy;
            if (AttackObject != null && !AttackObject.Destroyed) { fx = AttackObject.CenterX - CenterX; fy = AttackObject.CenterY - CenterY; }
            else { fx = FaceX; fy = FaceY; }
            double fl = Math.Sqrt(fx * fx + fy * fy); if (fl < 1e-6) return false;
            double ax = a.CenterX - CenterX, ay = a.CenterY - CenterY, al = Math.Sqrt(ax * ax + ay * ay); if (al < 1e-6) return false;
            return (fx * ax + fy * ay) / (fl * al) <= -Math.Cos(ZWorld.RearDeg * Math.PI / 180.0 * 0.5) + 1e-9;   // inside the rear cone
        }

        /// <summary>DV27: Zod's snipe chance, x RearSnipeFactor (capped) for a hit from behind.</summary>
        double SnipeChanceAt(ZObject t)
        {
            double sc = RestSnipeChance(t, SnipeChanceAgainst(t));                                                   // veterancy_v1 DV12: veteran crews stay buttoned up (x0.25)
            if (!ZWorld.ExposedDriverDeviation || !t.HitFromBehind(this)) return sc;
            if (ZWorld.RearHitsCounted < int.MaxValue) ZWorld.RearHitsCounted++;
            return Math.Min(ZWorld.RearSnipeCap, sc * ZWorld.RearSnipeFactor);
        }

        /// <summary>DV27: this unit prefers targets with an exposed driver (it can snipe, the switch is on).</summary>
        bool PrefersExposed => ZWorld.ExposedDriverDeviation && CanSnipe;

        /// <summary>DV27, CheckPassiveEngage of a unit standing on no order and shooting at a target whose driver is covered: an enemy
        /// in range with an exposed driver (first in object order, like Zod's own pick) takes over.</summary>
        void SwitchToExposed()
        {
            if (Waypoints.Count > 0 || AttackObject == null || AttackObject.CanBeSniped()) return;
            foreach (var o in World.Objects)
            {
                if (o == AttackObject || !o.IsUnit || o.Destroyed || o.KillAt >= 0 || o.Owner == Team.Neutral || o.Owner == Owner) continue;
                if (!o.CanBeSniped() || !CanAttackObject(o) || !WithinAttackRadius(o)) continue;
                Engage(o); ExposedSwitches++;
                return;
            }
        }
        /// <summary>DV27 statistics (checks): targets switched to an exposed driver.</summary>
        public int ExposedSwitches { get; private set; }

        // ================================================================== DV28 attack approach
        /// <summary>DV28 plans (checks / harness): attack orders sent to the nearest in-range spot instead of the target itself.</summary>
        public int ApproachPlans { get; private set; }

        /// <summary>DV28 hook in ProcessAttackWP's planning step (before DV23's shot spot): a target out of range sends the unit to the
        /// nearest reachable footprint in range (DV26: of a building's walls; a clear line for direct fire, no Zod barrier), found by
        /// a breadth-first walk of its own A* graph. False = no such spot (or the switch off): Zod's goal and DV23 decide as before.</summary>
        bool ApproachPlan(ZObject t, ref int ex, ref int ey)
        {
            if (!ZWorld.ApproachDeviation || World.Pathing == null || !World.Pathing.Enabled || !CanMove) return false;
            if (InReach(t, CenterX, CenterY, AttackRadius) && HasLineOfSight(t)) return false;
            if (PS.level > 0) return false;                                                 // stuck on the way there once: Zod's goal and the layer's detours take over
            // (d > R - EntryBand) for good, so a vehicle that had to approach never manoeuvred; duel pairs stop inside the band
            int reach = DuelPair(t) ? AttackRadius - ZDuel.EntryBand - 4 : AttackRadius - LosSpotSlack;
            if (!World.NearestInRangeSpot(this, t, Math.Max(8, reach), out int sx, out int sy, out approachPts)) return false;
            ex = sx; ey = sy; ApproachPlans++;
            return true;
        }
        List<(int x, int y)> approachPts;

        /// <summary>DV9 would manoeuvre this attacker against t once in range (the gate of DuelStep).</summary>
        bool DuelPair(ZObject t) => ZWorld.DuelDeviation && DuelCapable && t != null && (t.Kind == ZKind.Vehicle || t.Kind == ZKind.Cannon)
                                    && !(ZWorld.NoTankDuelVsJeep && Type != "jeep" && t.Kind == ZKind.Vehicle && t.Type == "jeep");

        /// <summary>DV28, instead of Zod's Find_Path: walk the breadth-first path to the spot (tile centres, straight runs merged; it goes
        /// round parked units, which Zod's A* does not see). False = no stored path: StartPath as usual.</summary>
        bool ApproachWalk()
        {
            var pts = approachPts; approachPts = null;
            if (pts == null || pts.Count == 0) return false;
            StopMove();
            cur.pathFindingId = 0; cur.gotPfResponse = true; cur.pfPoints = pts;
            NextPathPoint();
            return true;
        }

        // ================================================================== DV29 armour chip
        /// <summary>DV29 (default off): a light automatic weapon's bullet on a vehicle / cannon hull deals ArmourChipFactor x damage.</summary>
        int HullDamage(ZObject t)
        {
            if (!ZWorld.ArmourChipDeviation || !(t.Kind == ZKind.Vehicle || t.Kind == ZKind.Cannon)) return Damage;
            if (!(Type == "grunt" || Type == "psycho" || Type == "jeep" || Type == "gatling" || (Kind == ZKind.Vehicle && Type == "apc"))) return Damage;
            return (int)Math.Round(Damage * ZWorld.ArmourChipFactor);
        }
    }

    public sealed partial class ZWorld
    {
        /// <summary>DV25 fort mortar (NB27): 30 s reload, slow dodgeable lob, destroyable gun. Needs DV7 (FortGunDeviation). Off = DV7
        /// with the Zod howitzer's numbers. PlayerPrefs oz_fort_mortar.</summary>
        public static bool FortMortarDeviation = true;
        /// <summary>DV26 building reach (NB14): range, aim and blast measured to a building's walls. PlayerPrefs oz_building_reach.</summary>
        public static bool BuildingReachDeviation = true;
        /// <summary>DV27 exposed drivers (NB11): snipers prefer exposed drivers; hits from behind snipe RearSnipeFactor x easier.
        /// PlayerPrefs oz_exposed_drivers.</summary>
        public static bool ExposedDriverDeviation = true;
        public const double RearDeg = 120, RearSnipeFactor = 2.0, RearSnipeCap = 0.9;
        /// <summary>DV27 statistics (checks): hitscan rolls that counted as a hit from behind.</summary>
        public static int RearHitsCounted;
        /// <summary>DV28 attack approach (NB13): an out-of-range attack goes to the nearest spot in range. PlayerPrefs oz_attack_approach.</summary>
        public static bool ApproachDeviation = true;
        /// <summary>DV29 armour chip (NB5 proposal): default OFF (the measured time-to-kill matches Zod). PlayerPrefs oz_armour_chip.</summary>
        public static bool ArmourChipDeviation = false;
        public const double ArmourChipFactor = 2.0;
        /// <summary>NB12, part of DV9: light / medium / heavy tanks do not duel-manoeuvre against a jeep (they stop and fire, Zod);
        /// </summary>
        public static bool NoTankDuelVsJeep = true;

        int[] apSeen, apQueue, apParent; int apStamp;

        /// <summary>DV28: breadth-first walk of the unit's own A* graph (ZReach component, Zod's StepOk moves, fixed neighbour order:
        /// deterministic) from where it stands to the nearest footprint whose centre is within `range` of the target (DV26: of its
        /// walls), with a clear line for direct fire (DV23) and no Zod destroyable barrier in between.</summary>
        internal bool NearestInRangeSpot(ZObject u, ZObject t, int range, out int sx, out int sy, out List<(int x, int y)> path)
        {
            sx = sy = 0; path = null;
            var reach = Pathing?.Reach; var pf = Path;
            if (reach == null || t == null) return false;
            bool robot = u.IsRobot, nr = u.HasExplosives();
            int c = reach.UnitComp(u, nr);
            if (c < 0) return false;
            int w = Map.width, h = Map.height, n = w * h;
            if (apSeen == null || apSeen.Length != n) { apSeen = new int[n]; apQueue = new int[n]; apParent = new int[n]; apStamp = 0; }
            if (++apStamp == int.MaxValue) { Array.Clear(apSeen, 0, n); apStamp = 1; }
            int head = 0, tail = 0;
            var (ftx, fty) = ZReach.FootTile(u.LocX, u.LocY);
            for (int r = 0; r <= 2 && tail == 0; r++)
                for (int y = fty - r; y <= fty + r; y++)
                    for (int x = ftx - r; x <= ftx + r; x++)
                    {
                        if (Math.Max(Math.Abs(x - ftx), Math.Abs(y - fty)) != r || x < 0 || y < 0 || x >= w || y >= h) continue;
                        if (reach.Comp(x, y, robot, nr) != c || apSeen[y * w + x] == apStamp) continue;
                        apSeen[y * w + x] = apStamp; apParent[y * w + x] = -1; apQueue[tail++] = y * w + x;
                    }
            bool line = u.LosActive;
            int exT = line ? Sight.ExemptOf(t) : 0;
            // units that will not make room (parked neutral vehicles, cannons, anything standing without an order) never make a goal:
            // a unit sent onto one waits behind it forever (NB13 harness, L17: 72 s held behind a parked neutral light tank)
            apParked.Clear();
            foreach (var o in Objects)
                if (o != u && o != t && o.IsUnit && !o.Destroyed && o.KillAt < 0 && !o.IsMinion && o.Leader != u && (o.Kind == ZKind.Cannon || o.Owner == Team.Neutral || (o.Waypoints.Count == 0 && !o.IsMoving)))
                    apParked.Add(o);
            apDecks.Clear();
            foreach (var o in Objects) if (o.IsBridge && !o.Destroyed) apDecks.Add(o);           // never a goal on a bridge deck (the unit would queue for it)
            int half = robot ? 8 : 16, pad = ZPathing.Radius(u);
            while (head < tail)
            {
                int cur = apQueue[head++], cx = cur % w, cy = cur / w;
                var (px, py) = ZReach.FootCentre(cx, cy, robot);
                if (u.InReach(t, px, py, range) && (t.IsDestroyableImpass || !EngageBarrierBetween(px, py, t.CenterX, t.CenterY)) &&
                    (!line || Sight.Clear(px, py, 0, t.CenterX, t.CenterY, exT)) && !ParkedAt(px, py, half, pad) && !OnDeck(px, py, half))
                { sx = px; sy = py; path = ApproachPath(cur, w, robot); return true; }
                for (int k = 0; k < 8; k++)
                {
                    int nx = cx + ApNX[k], ny = cy + ApNY[k];
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                    int ni = ny * w + nx;
                    if (apSeen[ni] == apStamp || !pf.StepOk(cx, cy, nx, ny, robot, nr)) continue;
                    var (qx, qy) = ZReach.FootCentre(nx, ny, robot);
                    if (ParkedAt(qx, qy, half, pad)) { apSeen[ni] = apStamp; continue; }         // never through a parked unit either
                    apSeen[ni] = apStamp; apParent[ni] = cur; apQueue[tail++] = ni;
                }
            }
            return false;
        }

        /// <summary>DV28: the breadth-first chain back from the goal tile, as footprint centres, straight runs merged, start excluded.</summary>
        List<(int x, int y)> ApproachPath(int goal, int w, bool robot)
        {
            var chain = new List<int>();
            for (int i = goal; i >= 0 && chain.Count < 4096; i = apParent[i]) chain.Add(i);
            chain.Reverse();                                                                  // start tile first
            var pts = new List<(int x, int y)>();
            for (int k = 1; k < chain.Count; k++)
            {
                int a = chain[k - 1], b = chain[k];
                bool last = k == chain.Count - 1;
                if (!last)
                {
                    int c = chain[k + 1];
                    if ((b % w) - (a % w) == (c % w) - (b % w) && (b / w) - (a / w) == (c / w) - (b / w)) continue;   // same direction: merge
                }
                pts.Add(ZReach.FootCentre(b % w, b / w, robot));
            }
            return pts;
        }
        static readonly int[] ApNX = { 0, 1, 0, -1, 1, 1, -1, -1 }, ApNY = { -1, 0, 1, 0, -1, 1, 1, -1 };
        readonly List<ZObject> apParked = new List<ZObject>(), apDecks = new List<ZObject>();

        /// <summary>DV28: a unit box of half-size `half` centred on (x, y) overlaps a bridge's rect.</summary>
        bool OnDeck(int x, int y, int half)
        {
            foreach (var b in apDecks)
                if (x + half > b.LocX && x - half < b.LocX + b.WidthPix && y + half > b.LocY && y - half < b.LocY + b.HeightPix) return true;
            return false;
        }

        /// <summary>DV28: a unit box of half-size `half` centred on (x, y), grown by its spacing radius, overlaps a parked unit.</summary>
        bool ParkedAt(int x, int y, int half, int pad)
        {
            foreach (var o in apParked)
            {
                int r = ZPathing.Radius(o) + pad;
                if (Math.Abs(o.CenterX - x) < r + half - pad && Math.Abs(o.CenterY - y) < r + half - pad) return true;
            }
            return false;
        }
    }
}
