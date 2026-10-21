#if !OPENZ_HARNESS
using System;
using System.Collections.Generic;
using System.Text;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>K322: the clean clip stage shared by the three Play stages of the owner's clips: the K248 sniper stage
    /// (OzV11SniperSteadyStage, OpenZParity.V11SniperSteady.cs), the K306 Tough stage (OzV11ToughSteadyStage, OpenZParity.V11ToughSteady.cs)
    /// and the DV121 jeep stage (OzV11JeepChargeStage.cs). Editor / stage code only: no rule of the game is changed.
    /// <list type="bullet">
    /// <item><b>The clean map</b> (<see cref="Clean"/>, before the take): the computer players are taken out of the session (a computer
    /// player starts its factories again every moment), every factory and fort stops its production and its queue (the game's own STOP
    /// command, ZWorld.StopBuilding), and every robot and vehicle that is not part of the stage is taken away, with the empty vehicles and
    /// the guns that stand on their own on the map (as the stages take their own units of a run before away: KillAt, no death, view hidden).
    /// Kept: the forts' guns, the forts' roof defenders, the hero squads, the shuttle, flags and crates; and, for
    /// a side that would have no unit left, its one unit farthest from the stage (the game ends when a side has no unit, and a side without
    /// units loses its fort: ZWorld.CheckEndGame / CheckNoUnitsDestroyFort). A robot or vehicle that stays and is still on its way (an
    /// order of the computer player just taken out) gets its side's STOP order.</item>
    /// <item><b>The hold</b> (<see cref="HoldStep"/>, every frame of the take): a production that starts again (a captured zone starts its
    /// default production) is stopped again, and a robot or vehicle that appears at a building (a factory's output, a repaired vehicle,
    /// a start crew) and is not the stage's is taken away at once: nobody comes in.</item>
    /// <item><b>The views</b> (Play, <see cref="Stage"/>): every staged unit, leader and squad members, gets its view through
    /// ZGame.SpawnUnitView, the call CombatView makes for every unit the game makes while it runs (ZEventType.Created: a factory's output,
    /// an ejected crew, a repaired vehicle); the level's own units are bound to the scene's objects instead. Each view is checked (there,
    /// drawn) and the console says how many.</item>
    /// </list>
    /// Plain C# on the sim (the kit's headless census and fights run this same code); the Play parts are below in UNITY_EDITOR.</summary>
    public static partial class OzClipStage
    {
        /// <summary>The stage's own units (ids, leaders and squad members) of the take under way: the hold never takes them away.</summary>
        public static readonly HashSet<int> Staged = new HashSet<int>();
        /// <summary>Kept on purpose for a side that would have no unit left (ids).</summary>
        public static readonly HashSet<int> Keepers = new HashSet<int>();
        /// <summary>Objects with an id from this one on were made after the last clean.</summary>
        public static int FirstNewId = int.MaxValue;
        /// <summary>Counters of the last clean and of the hold since then (the console report).</summary>
        public static int BotsRemoved, FactoriesStopped, UnitsRemoved, UnitsBefore, UnitsStopped, ProductionRestops, LetInRemoved;
        public static string LastClean = "no clean yet";

        /// <summary>A unit the clean never takes away: the forts' guns (their tower guns, manned or not, and the door tower guns), the
        /// forts' roof defenders, the hero squads, the shuttle. Taken away: every robot and vehicle of a side, every empty vehicle and every
        /// gun that stands on its own on the map, empty or taken by a side (a robot near an empty one walks to it by itself, Zod's auto-grab;
        /// a manned one fires at the stage). Flags and grenade crates stay (the place tests keep the stage away from them).</summary>
        public static bool AlwaysKept(ZWorld w, ZObject o) => !o.IsUnit || o.IsFortDefender || o.IsHero || o.IsShuttle || (o.Kind == ZKind.Cannon && FortGun(w, o));

        /// <summary>A gun of a fort: on a fort's tower (ZObject.Ejectable off), a door tower gun (DV73), or standing on a fort's box.</summary>
        public static bool FortGun(ZWorld w, ZObject c)
        {
            if (!c.Ejectable || w.FortDoorGuns.Contains(c)) return true;
            foreach (var f in w.Objects)
                if (f.IsFort && c.CenterX >= f.LocX - 16 && c.CenterX < f.LocX + f.WidthPix + 16 && c.CenterY >= f.LocY - 16 && c.CenterY < f.LocY + f.HeightPix + 16) return true;
            return false;
        }

        /// <summary>Every factory and fort: production and queue stopped (the game's STOP command for an owned building). Returns how many
        /// were producing or had a queue.</summary>
        public static int StopProduction(ZWorld w)
        {
            int n = 0;
            foreach (var b in w.Objects)
            {
                if (!b.IsProducer || b.Destroyed || !(b.Building || b.CurUnit != null || b.Queue.Count > 0)) continue;
                if (b.Owner != Team.Neutral) w.StopBuilding(b.Owner, b); else b.StopBuildingProduction();
                n++;
            }
            return n;
        }

        static double D2(int ax, int ay, int bx, int by) => (double)(ax - bx) * (ax - bx) + (double)(ay - by) * (ay - by);

        /// <summary>Which unit a side keeps when it would have none left: a gun first (it never moves), then a unit without orders (it
        /// stands), then any; among them the one farthest from the stage.</summary>
        static int KeepRank(ZObject o) => o.Kind == ZKind.Cannon ? 2 : (o.Leader ?? o).Waypoints.Count == 0 ? 1 : 0;

        /// <summary>The clean map before a take (see the class summary). (cx, cy) = the middle of the screen in px (a side's last unit
        /// kept is the one farthest from it, a gun or a unit that stands first). The stage's units of a run before are not kept (never two stages at once). Returns the
        /// console line.</summary>
        public static string Clean(ZWorld w, int cx, int cy)
        {
            Staged.Clear(); Keepers.Clear();
            BotsRemoved = w.Bots.Count; w.Bots.Clear();
            FactoriesStopped = StopProduction(w);
            ProductionRestops = LetInRemoved = 0;
            UnitsBefore = 0; UnitsRemoved = 0;
            var left = new Dictionary<Team, int>(); var farthest = new Dictionary<Team, ZObject>();
            var gone = new List<ZObject>();
            int maxId = 0;
            foreach (var o in w.Objects)
            {
                if (o.Id > maxId) maxId = o.Id;
                if (!o.IsUnit || o.Destroyed || o.KillAt >= 0) continue;
                if (AlwaysKept(w, o)) { if (o.Owner != Team.Neutral && !o.IsFortDefender) left[o.Owner] = (left.TryGetValue(o.Owner, out int k) ? k : 0) + 1; continue; }
                UnitsBefore++; gone.Add(o);
                if (o.Owner == Team.Neutral) continue;
                if (!farthest.TryGetValue(o.Owner, out var f) || KeepRank(o) > KeepRank(f) || (KeepRank(o) == KeepRank(f) && D2(o.CenterX, o.CenterY, cx, cy) > D2(f.CenterX, f.CenterY, cx, cy))) farthest[o.Owner] = o;
            }
            var kept = new StringBuilder();
            foreach (var kv in farthest)
                if (!left.ContainsKey(kv.Key) || left[kv.Key] == 0)
                {   // this side would have no unit left: one stays (with its squad): a gun, else a unit without orders, the farthest from the stage
                    var keep = kv.Value.Leader ?? kv.Value;
                    Keepers.Add(keep.Id); foreach (var m in keep.Minions) Keepers.Add(m.Id);
                    kept.Append($"{(kept.Length > 0 ? ", " : "")}{keep.Kind.ToString().ToLowerInvariant()} {keep.Type} #{keep.Id} ({kv.Key}) on tile {keep.CenterX >> 4},{keep.CenterY >> 4}");
                }
            foreach (var o in gone)
            {
                if (Keepers.Contains(o.Id)) continue;
                o.KillAt = w.Time; UnitsRemoved++;
            }
            UnitsStopped = 0;
            foreach (var o in w.Objects)
            {   // the robots and vehicles that stay (a hero squad, a side's last unit) and are still on their way (an order of the computer
                // player just taken out): the side's own STOP order (an empty waypoint list), so nobody walks into the stage
                if (o.Destroyed || o.KillAt >= 0 || !o.IsMobile || o.IsMinion || o.IsShuttle || o.Owner == Team.Neutral || o.Waypoints.Count == 0) continue;
                if (!o.IsHero && !Keepers.Contains(o.Id)) continue;
                if (w.SendWaypoints(o.Owner, o, new ZWaypoint[0])) UnitsStopped++;
            }
            FirstNewId = maxId + 1;
            var sb = new StringBuilder();
            foreach (var t in new[] { Team.Red, Team.Blue }) sb.Append($"{(sb.Length > 0 ? ", " : "")}{t} {Left(w, t)}");
            LastClean = $"clean map: computer players taken out {BotsRemoved}, factories / forts stopped {FactoriesStopped}, robots, vehicles and empty guns taken away {UnitsRemoved} of {UnitsBefore}" +
                        $"{(Keepers.Count > 0 ? $" (kept as a side's last unit: {kept})" : "")}{(UnitsStopped > 0 ? $", {UnitsStopped} that stay told to stop (still on their way)" : "")}; units of the sides left besides the stage (the forts' guns, heroes, shuttle, last units): {sb}";
            return LastClean;
        }

        /// <summary>The units of side <paramref name="t"/> that count for the game's end (robots, vehicles, guns; not the roof defenders),
        /// not taken away.</summary>
        public static int Left(ZWorld w, Team t)
        {
            int n = 0;
            foreach (var o in w.Objects) if (o.IsUnit && o.Owner == t && !o.Destroyed && o.KillAt < 0 && !o.IsFortDefender) n++;
            return n;
        }

        /// <summary>The stage's own units: leaders and their squad members (the hold never takes them away).</summary>
        public static void Add(params ZObject[] leaders)
        {
            foreach (var o in leaders) { if (o == null) continue; Staged.Add(o.Id); foreach (var m in o.Minions) Staged.Add(m.Id); }
        }

        /// <summary>A building within <paramref name="px"/> px of (x, y) (its box).</summary>
        static bool AtBuilding(ZWorld w, int x, int y, int px)
        {
            foreach (var b in w.Objects)
            {
                if (b.Kind != ZKind.Building) continue;
                int x0 = b.LocX - px, y0 = b.LocY - px, x1 = b.LocX + b.WidthPix + px, y1 = b.LocY + b.HeightPix + px;
                if (x >= x0 && x < x1 && y >= y0 && y < y1) return true;
            }
            return false;
        }

        /// <summary>One step of the hold (every frame of the take; headless: every tick): a production that started again is stopped, a
        /// robot or vehicle made at a building after the clean that is not the stage's is taken away. A robot made beside a staged
        /// vehicle (its crew getting out) becomes part of the stage. Returns how many things it did.</summary>
        public static int HoldStep(ZWorld w)
        {
            int n = 0;
            foreach (var b in w.Objects)
                if (b.IsProducer && !b.Destroyed && (b.Building || b.CurUnit != null || b.Queue.Count > 0))
                { if (b.Owner != Team.Neutral) w.StopBuilding(b.Owner, b); else b.StopBuildingProduction(); ProductionRestops++; n++; }
            if (FirstNewId == int.MaxValue) return n;
            foreach (var o in w.Objects)
            {
                if (o.Id < FirstNewId || !o.IsMobile || o.Destroyed || o.KillAt >= 0 || Staged.Contains(o.Id) || Keepers.Contains(o.Id) || AlwaysKept(w, o)) continue;
                bool crew = false;
                if (o.IsRobot)
                    foreach (var id in Staged) { var s = w.Get(id); if (s != null && s.Kind == ZKind.Vehicle && D2(s.CenterX, s.CenterY, o.CenterX, o.CenterY) <= 48 * 48) { crew = true; break; } }
                if (crew) { Add(o.Leader ?? o); continue; }
                if (!AtBuilding(w, o.CenterX, o.CenterY, 48)) continue;            // made elsewhere (another stage, a cheat): not the hold's business
                o.KillAt = w.Time; foreach (var m in o.Minions) m.KillAt = w.Time;
                LetInRemoved++; n++;
            }
            return n;
        }

        /// <summary>The hold's counters since the last clean (the console report).</summary>
        public static string HoldLine() => $"since the clean: productions stopped again {ProductionRestops}, robots / vehicles made at a building and taken away {LetInRemoved}";
    }

    /// <summary>K322: where each clip stage may stand: K248c's tests (OzSniperStagePlace: dry walkable ground where the units stand and
    /// walk, a clear line of fire, no flag, crate or empty vehicle in auto-grab reach of a leader that can stand idle, no other unit or fort
    /// in reach) for the sniper and the Tough stage; the same kind of tests for the jeep stage's open, flat, vehicle-passable ground with
    /// room for the charge and the drive back; and for every stage the CLEAR VIEW test: no rock, prop, building, tree or cliff on the tiles
    /// in front of a staged unit (the camera side, south, two tiles out, and the tiles beside it), or the unit is drawn behind it, see-through
    /// (Level 2's known place 7,65: one sniper stood right behind the rock pillar on 7,66). The known places per stage and level were
    /// proven by the kit's headless census and fights (the kit's out/census.txt, out/fights.txt).</summary>
    public static class OzClipStagePlace
    {
        public enum Kind { Sniper, Tough, Jeep }

        /// <summary>The Tough stage's known places (the Toughs' tile, as the sniper stage's: the grunt squads at OzSniperStagePlace.G1X /
        /// G2X east of it), per map file. Each fits (every test, the fight and after it) on the level as a fresh Play session runs it, 25,
        /// 40, 60 and 180 s in, either side as the player, head start on or off, with the clean map; its staged fight was clean in all the
        /// kit's headless fights (the kit's out/census.txt, out/fights.txt).</summary>
        public static readonly Dictionary<string, int[]> ToughKnown = new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase)
        {
            { "p02_bb_orig02.map", new[] { 9, 63 } },                             // Level 2: the open sand and road in the south-west (the sniper stage's; at 8,63 beside it the second Tough sometimes never fires: the first one's rockets end the fight)
            { "p02_bb_orig17.map", new[] { 36, 49, 65, 76, 77, 11 } },           // Level 17: the middle west, the south, the north-east
        };

        /// <summary>The sniper stage's known places on maps K248c's list (OzSniperStagePlace.Known) does not cover, proven as the Tough
        /// stage's (the same layout).</summary>
        public static readonly Dictionary<string, int[]> SniperMoreKnown = new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase)
        {
            { "p02_bb_orig17.map", new[] { 36, 49, 65, 76, 77, 11 } },           // Level 17: the middle west, the south, the north-east
        };

        /// <summary>The jeep stage's known places (the jeep's tile; the Tough <see cref="JeepToughPx"/> px east of it), per map file, proven
        /// as the Tough stage's: both results came in the kit's headless fights on each (the jeep runs the Tough over and drives home,
        /// or the Tough's second rocket destroys it), nothing else joined, nobody walked off.</summary>
        public static readonly Dictionary<string, int[]> JeepKnown = new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase)
        {
            { "p02_bb_orig02.map", new[] { 11, 62, 24, 75, 27, 78 } },           // Level 2: the south-west sand (the road on its east end), the sand west of the canal in the south, the south road
            { "p02_bb_orig17.map", new[] { 45, 50, 66, 78, 20, 75 } },           // Level 17: the middle, the south, the west
        };

        /// <summary>The jeep stage's layout: the Tough this many px east of the jeep (6 tiles, mid range: the jeep's gun reaches 120 px).</summary>
        public const int JeepToughPx = 96;
        /// <summary>The jeep stage's ground, in tiles round the jeep's tile: x from JeepX0 to JeepX1 (2 tiles behind the jeep to 4 tiles past
        /// the Tough: the charge drives 24 px on past him, then turns and drives back), y from -JeepY to JeepY (the jeep's own two rows and
        /// the turn); lanes (bridge decks, one-lane passages and their approaches) are kept a tile further off.</summary>
        public const int JeepX0 = -2, JeepX1 = 10, JeepY = 2;
        /// <summary>The clear view test: the tiles from ViewBack north of a unit's tile to ViewFront south of it (the camera looks north,
        /// from the south), ViewSide either side; for a squad's leader also the two rows in front ViewSquadSide tiles either side (its
        /// members stand up to a tile beside and behind it: L02 15,36, a sniper a tile south-west of its leader, a rock in front of him).</summary>
        public const int ViewBack = 1, ViewFront = 2, ViewSide = 1, ViewSquadSide = 2;

        /// <summary>The jeep's centre and the Tough's centre (px) for the jeep's tile (tx, ty). The stage creates the jeep's box at
        /// (jx - 16, jy - 16) and the Tough's at (ux - 8, uy - 8).</summary>
        public static void JeepSpots(int tx, int ty, out int jx, out int jy, out int ux, out int uy)
        {
            jx = tx * 16 + 8; jy = ty * 16 + 8; ux = jx + JeepToughPx; uy = jy;
        }

        /// <summary>Where the camera looks for the stage on tile (tx, ty) (px): the middle of the fight.</summary>
        public static void Middle(Kind k, int tx, int ty, out int x, out int y)
        {
            if (k == Kind.Jeep) { JeepSpots(tx, ty, out int jx, out int jy, out _, out _); x = jx + JeepToughPx / 2; y = jy; return; }
            x = tx * 16 + 83; y = ty * 16 + 8;
        }

        /// <summary>The known places of a stage kind for a map file (the snipers': K248c's list, else <see cref="SniperMoreKnown"/>).</summary>
        public static int[] KnownOf(Kind k, string file)
        {
            var d = k == Kind.Sniper ? OzSniperStagePlace.Known : k == Kind.Tough ? ToughKnown : JeepKnown;
            if (d.TryGetValue(file ?? "", out var l)) return l;
            return k == Kind.Sniper && SniperMoreKnown.TryGetValue(file ?? "", out var m) ? m : null;
        }

        static bool WalksOff(Kind k, string file, int x, int y) => k == Kind.Sniper && OzSniperStagePlace.WalksOff(file, x, y);

        static int Range(ZWorld w, string type) { var u = w.S.Unit(type); return u != null ? u.attack_radius : 0; }
        static double Dist(int ax, int ay, int bx, int by) => Math.Sqrt((double)(ax - bx) * (ax - bx) + (double)(ay - by) * (ay - by));
        static bool Sees(ZWorld w, int x, int y, ZObject o) => !ZWorld.LosDeviation || !ZObject.AutoSight || w.Sight.ClearFrom(x, y, 0, o);
        static bool SwampWay(ZWorld w, int x, int y, ZObject o)
        {
            if (!ZWorld.CrocWaterCostDeviation || !w.CrocMap) return false;
            int n = 0;
            foreach (var (px, py) in ZPathFinder.Bresenham(x >> 4, y >> 4, o.CenterX >> 4, o.CenterY >> 4, w.Map.width, w.Map.height))
                if (w.Map.ClassAt(px, py) == ZClass.Water && ++n > ZWorld.CrocSwampTiles) return true;
            return false;
        }

        static OzSniperStagePlace.Verdict Fits(int tx, int ty) => new OzSniperStagePlace.Verdict { why = OzSniperStagePlace.Why.Fits, tx = tx, ty = ty };
        static OzSniperStagePlace.Verdict Bad(int tx, int ty, int bx, int by, string who) => new OzSniperStagePlace.Verdict { why = OzSniperStagePlace.Why.Ground, tx = tx, ty = ty, bx = bx, by2 = by, who = who };

        /// <summary>How far the Toughs walk in before they fire: one tile inside their range of the nearest grunt squad's leader (DV120 /
        /// DV123's stand band).</summary>
        public static int ToughsWalkPx(ZWorld w)
        {
            int g1 = OzSniperStagePlace.G1X, g1y = OzSniperStagePlace.G1Y, g2 = OzSniperStagePlace.G2X, g2y = OzSniperStagePlace.G2Y;
            double near = Math.Min(Math.Sqrt((double)g1 * g1 + (double)g1y * g1y), Math.Sqrt((double)g2 * g2 + (double)g2y * g2y));
            return Math.Max(0, (int)Math.Ceiling(near) - (Range(w, "tough") - ZWorld.SniperStandBand));
        }

        /// <summary>The spots of the stage's units on tile (tx, ty) (px; leaders at the start and where they stand to fire).</summary>
        public static List<(int x, int y, string who)> Spots(Kind k, ZWorld w, int tx, int ty)
        {
            var l = new List<(int, int, string)>();
            if (k == Kind.Jeep)
            {
                JeepSpots(tx, ty, out int jx, out int jy, out int ux, out int uy);
                l.Add((jx, jy, "the jeep")); l.Add((ux, uy, "the Tough"));
                return l;
            }
            int bx = tx * 16 + 8, by = ty * 16 + 8, walk = k == Kind.Sniper ? OzSniperStagePlace.SnipersWalkPx(w) : ToughsWalkPx(w), grunt = Range(w, "grunt");
            string me = k == Kind.Sniper ? "the snipers" : "the Toughs";
            l.Add((bx, by, me + " at the start")); l.Add((bx + walk, by, me + " where they fire"));
            foreach (var (gx, gy) in new[] { (OzSniperStagePlace.G1X, OzSniperStagePlace.G1Y), (OzSniperStagePlace.G2X, OzSniperStagePlace.G2Y) })
            {
                int x = bx + gx, y = by + gy, inward = Math.Max(0, (int)Math.Ceiling(Math.Sqrt((double)gx * gx + (double)gy * gy)) - grunt);
                l.Add((x, y, "a grunt squad at the start")); l.Add((x - inward, y, "a grunt squad where it fires"));
            }
            return l;
        }

        static ZWorld tallWorld; static double tallTime = double.NaN; static int tallCount = -1; static bool[,] tallGrid;

        /// <summary>The tiles that stand tall in the picture, as the world is now: a cliff; a tile no robot can walk that is not water (a
        /// tree that closes its tile, a wreck, a prop's blocked tiles); and every tile under the box of a map item that is not a flag or a
        /// grenade crate (a rock is 1 x 3 tiles: its pillar stands on the two tiles above the one it blocks, huts, props) or of a building
        /// that is not a bridge (forts, factories, radars, repair bays), not destroyed.</summary>
        static bool[,] TallGrid(ZWorld w)
        {
            if (tallGrid != null && tallWorld == w && tallTime == w.Time && tallCount == w.Objects.Count) return tallGrid;
            int W = w.Map.width, H = w.Map.height; var g = new bool[W, H];
            for (int x = 0; x < W; x++)
                for (int y = 0; y < H; y++)
                {
                    var c = w.Map.ClassAt(x, y);
                    g[x, y] = c == ZClass.Impassable || (c != ZClass.Water && c != ZClass.DeepWater && !w.Path.TilePassable(x, y, true));
                }
            foreach (var o in w.Objects)
            {
                if (o.Destroyed || o.KillAt >= 0) continue;
                bool tall = o.Kind == ZKind.MapItem ? o.Type != "flag" && o.Type != "grenades" : o.Kind == ZKind.Building && !o.IsBridge;
                if (!tall) continue;
                int x0 = Math.Max(0, o.LocX >> 4), y0 = Math.Max(0, o.LocY >> 4), x1 = Math.Min(W - 1, (o.LocX + o.WidthPix - 1) >> 4), y1 = Math.Min(H - 1, (o.LocY + o.HeightPix - 1) >> 4);
                for (int x = x0; x <= x1; x++) for (int y = y0; y <= y1; y++) g[x, y] = true;
            }
            tallWorld = w; tallTime = w.Time; tallCount = w.Objects.Count; tallGrid = g;
            return g;
        }

        /// <summary>A tile that stands tall in the picture (<see cref="TallGrid"/>), or off the map.</summary>
        public static bool Tall(ZWorld w, int x, int y) => !w.Path.TileOnMap(x, y) || TallGrid(w)[x, y];

        /// <summary>The clear view test alone: for every spot of the stage's units no tall tile (<see cref="Tall"/>) from ViewBack tiles
        /// north to ViewFront tiles south (the camera's side) and ViewSide (a squad: ViewSquadSide) tiles either side of the unit's tile.</summary>
        public static OzSniperStagePlace.Verdict View(Kind k, ZWorld w, int tx, int ty)
        {
            foreach (var (x, y, who) in Spots(k, w, tx, ty))
            {
                int ux = x >> 4, uy = y >> 4;
                for (int j = -ViewBack; j <= ViewFront; j++)
                    for (int i = -ViewSquadSide; i <= ViewSquadSide; i++)
                        if (Math.Abs(i) <= ViewSide || (k != Kind.Jeep && j >= 1))
                        if (Tall(w, ux + i, uy + j))
                            return Bad(tx, ty, ux + i, uy + j, $"tall (a rock, prop, building or cliff) {(j > 0 ? "in front of" : j < 0 ? "behind" : "beside")} {who} on tile {ux},{uy}: drawn behind it, see-through, from the RTS camera");
            }
            return Fits(tx, ty);
        }

        /// <summary>Lines of fire: a clear sight line (DV23, while that rule is on) and no rock, tree, hut or prop between (Zod's barrier test).</summary>
        static OzSniperStagePlace.Verdict FireLines(ZWorld w, int tx, int ty, IEnumerable<(int x, int y)> from, IEnumerable<(int x, int y)> to)
        {
            var tl = new List<(int, int)>(to);
            foreach (var (ax, ay) in from)
                foreach (var (bx, by) in tl)
                {
                    bool sight = !ZWorld.LosDeviation || w.Sight.Clear(ax, ay, 0, bx, by, 0);
                    if (sight && !w.EngageBarrierBetween(ax, ay, bx, by)) continue;
                    return Bad(tx, ty, bx >> 4, by >> 4, $"not in a clear line of fire from tile {ax >> 4},{ay >> 4} ({(sight ? "a rock, hut or prop" : "a cliff, building or rock")} between)");
                }
            return Fits(tx, ty);
        }

        /// <summary>The auto-grab pull at each point: a flag of another side within auto_grab_flag_distance in sight (any unit that can
        /// move); for a robot also an empty vehicle / gun, and for a robot that picks crates up a grenade crate, within
        /// auto_grab_vehicle_distance in sight (ZObject.CheckPassiveEngage; on Level 16 not across the swamp).</summary>
        static OzSniperStagePlace.Verdict Pull(ZWorld w, int tx, int ty, IEnumerable<(int x, int y, Team team, bool robot, bool crates, string who)> points)
        {
            int flagPx = w.S.auto_grab_flag_distance, grabPx = w.S.auto_grab_vehicle_distance;
            foreach (var (x, y, team, robot, crates, who) in points)
            {
                foreach (var f in w.Flags)
                {
                    if (f.Destroyed || f.Owner == team) continue;
                    double d = Dist(f.CenterX, f.CenterY, x, y);
                    if (d <= flagPx && Sees(w, x, y, f) && !SwampWay(w, x, y, f))
                        return new OzSniperStagePlace.Verdict { why = OzSniperStagePlace.Why.Pull, tx = tx, ty = ty, by = f, dist = d, limit = flagPx, who = who };
                }
                if (!robot) continue;
                foreach (var o in w.Objects)
                {
                    if (o.Destroyed || o.KillAt >= 0) continue;
                    bool crate = o.Type == "grenades" && o.Kind == ZKind.MapItem;
                    if (!(crate && crates) && !o.CanBeEntered()) continue;
                    double d = Dist(o.CenterX, o.CenterY, x, y);
                    if (d <= grabPx && Sees(w, x, y, o) && !SwampWay(w, x, y, o))
                        return new OzSniperStagePlace.Verdict { why = OzSniperStagePlace.Why.Pull, tx = tx, ty = ty, by = o, dist = d, limit = grabPx, who = who };
                }
            }
            return Fits(tx, ty);
        }

        // ------------------------------------------------------------------ the jeep stage
        /// <summary>The jeep stage's ground: every tile of its box plain ground or road (no water: no splash, no slow-down; no rock, tree,
        /// building, prop or cliff, nor the drawn pillar of a rock), walkable for vehicles and robots; no lane's keep-clear area within a tile of the box (the charge never
        /// drives through one and the stage keeps off bridges); a clear line of fire from the jeep to the Tough.</summary>
        static OzSniperStagePlace.Verdict JeepGround(ZWorld w, int tx, int ty)
        {
            for (int i = JeepX0; i <= JeepX1; i++)
                for (int j = -JeepY; j <= JeepY; j++)
                {
                    int x = tx + i, y = ty + j;
                    if (!w.Path.TileOnMap(x, y)) return Bad(tx, ty, x, y, "off the map");
                    var c = w.Map.ClassAt(x, y);
                    if (c != ZClass.Ground && c != ZClass.Road) return Bad(tx, ty, x, y, c == ZClass.Water || c == ZClass.DeepWater ? "water" : "a cliff");
                    if (!w.Path.TilePassable(x, y, false) || !w.Path.TilePassable(x, y, true)) return Bad(tx, ty, x, y, "blocked for a vehicle (a rock, tree, building or prop)");
                    if (Tall(w, x, y)) return Bad(tx, ty, x, y, "under a rock's pillar, a prop, hut or building (the jeep would drive behind it)");
                }
            JeepSpots(tx, ty, out int jx, out int jy, out int ux, out int uy);
            if (w.Pathing != null)
            {   // the charge never drives through a lane's zone (ZObject.ChargeLaneClear): none within a tile of the box; nobody stays idle in a
                // lane's keep-clear area (the pathing layer sends him out): neither the jeep at home nor the Tough on his spot
                int x0 = (tx + JeepX0 - 1) * 16, x1 = (tx + JeepX1 + 2) * 16, y0 = (ty - JeepY - 1) * 16, y1 = (ty + JeepY + 2) * 16;
                foreach (var L in w.Pathing.Lanes)
                {
                    if (L.ZX1 > x0 && L.ZX0 < x1 && L.ZY1 > y0 && L.ZY0 < y1) return Bad(tx, ty, (L.ZX0 + L.ZX1) >> 5, (L.ZY0 + L.ZY1) >> 5, "a lane (bridge deck or one-lane passage, with its approaches) by the charge");
                    if ((L.KX1 > jx - 24 && L.KX0 < jx + 24 && L.KY1 > jy - 24 && L.KY0 < jy + 24) || (L.KX1 > ux - 16 && L.KX0 < ux + 16 && L.KY1 > uy - 16 && L.KY0 < uy + 16))
                        return Bad(tx, ty, (L.KX0 + L.KX1) >> 5, (L.KY0 + L.KY1) >> 5, "a lane's keep-clear area under the jeep's or the Tough's spot (nobody stays idle there)");
                }
            }
            return FireLines(w, tx, ty, new[] { (jx, jy) }, new[] { (ux, uy) });
        }

        /// <summary>The jeep stage's pull: the jeep at home (idle after its drive back: a vehicle takes a flag of another side within
        /// auto_grab_flag_distance in sight) and the Tough on his spot (idle after he wins: a flag, an empty vehicle or gun in reach).</summary>
        static OzSniperStagePlace.Verdict JeepPull(ZWorld w, int tx, int ty, Team player)
        {
            JeepSpots(tx, ty, out int jx, out int jy, out int ux, out int uy);
            var foe = player == Team.Red ? Team.Blue : Team.Red;
            return Pull(w, tx, ty, new[] { (jx, jy, player, false, false, "jeep at home (idle after its drive back)"), (ux, uy, foe, true, false, "Tough on his spot (idle after a win)") });
        }

        /// <summary>The jeep stage's join test, against every spot of the stage: the jeep at home, on its way to just past the Tough and
        /// back (every 16 px), the Tough on his spot. A unit of the other side joins within max(its attack radius, the staged unit's) +
        /// agro_distance (it fires or chases, or the staged unit goes for it); a robot or vehicle of the same side within its own attack
        /// radius + agro_distance (a hurt team mate's help call, ZWorld.HelpCall); a fort shells a unit of the other side within its gun's
        /// range (+ the charge's danger margin: the jeep never charges a robot under an enemy fort). After the clean only the units it keeps
        /// stand there (the sides' guns, roof defenders, heroes, the shuttle, a side's last unit).</summary>
        static OzSniperStagePlace.Verdict JeepJoin(ZWorld w, int tx, int ty, Team player)
        {
            JeepSpots(tx, ty, out int jx, out int jy, out int ux, out int uy);
            var foe = player == Team.Red ? Team.Blue : Team.Red;
            int agro = w.S.agro_distance, jeepR = Range(w, "jeep"), toughR = Range(w, "tough"), fortGun = ZWorld.FortGunDeviation ? ZObject.FortGunRange(w.S.Unit("howitzer")) + ZWorld.ChargeDangerMarginPx : 0;
            var spots = new List<(int x, int y, Team team, int r, string who)>();
            for (int x = jx; x <= ux + ZWorld.ChargeOverPx; x += 16) spots.Add((x, jy, player, jeepR, x == jx ? "the jeep at home" : "the jeep on its charge"));
            spots.Add((ux, uy, foe, toughR, "the Tough"));
            foreach (var o in w.Objects)
            {
                if (o.Destroyed || o.KillAt >= 0 || o.Owner == Team.Neutral || OzClipStage.Staged.Contains(o.Id)) continue;
                bool fort = o.IsFort && fortGun > 0 && !o.FortGunDestroyed;
                if (!o.IsUnit && !fort) continue;
                foreach (var (x, y, team, r, who) in spots)
                {
                    int reach; string what;
                    if (fort) { if (o.Owner == team) continue; reach = fortGun; what = $"its gun's range of {who}"; }
                    else if (o.Owner != team) { reach = Math.Max(o.AttackRadius, r) + agro; what = $"{(o.AttackRadius > r ? "its own" : who + "'s")} agro reach of {who}"; }
                    else if (o.IsMobile) { reach = o.AttackRadius + agro; what = $"its help call reach of {who}"; }
                    else continue;
                    double d = Dist(o.CenterX, o.CenterY, x, y);
                    if (d <= reach) return new OzSniperStagePlace.Verdict { why = OzSniperStagePlace.Why.Join, tx = tx, ty = ty, by = o, dist = d, limit = reach, who = what };
                }
            }
            return Fits(tx, ty);
        }

        // ------------------------------------------------------------------ the tests per stage
        /// <summary>The stage's answer for tile (tx, ty) (the shooters' / the jeep's tile): the first test that fails, or Fits.
        /// <paramref name="afterFight"/> false: the walk to a flag, crate or vehicle after the fight is not looked at.</summary>
        public static OzSniperStagePlace.Verdict Check(Kind k, ZWorld w, int tx, int ty, Team player, bool afterFight = true)
        {
            if (k == Kind.Jeep)
            {
                var j = JeepGround(w, tx, ty);
                if (j.Ok) j = View(k, w, tx, ty);
                if (j.Ok) j = JeepJoin(w, tx, ty, player);
                if (j.Ok && afterFight) j = JeepPull(w, tx, ty, player);
                return j;
            }
            var v = OzSniperStagePlace.Ground(w, tx, ty);
            if (v.Ok && k == Kind.Tough)
            {   // the Toughs walk further in than the snipers (range 120 against 144): their lines of fire from where they stand
                int bx = tx * 16 + 8, by = ty * 16 + 8, walk = ToughsWalkPx(w), grunt = Range(w, "grunt");
                var to = new List<(int, int)>();
                foreach (var (gx, gy) in new[] { (OzSniperStagePlace.G1X, OzSniperStagePlace.G1Y), (OzSniperStagePlace.G2X, OzSniperStagePlace.G2Y) })
                {
                    int x = bx + gx, y = by + gy, inward = Math.Max(0, (int)Math.Ceiling(Math.Sqrt((double)gx * gx + (double)gy * gy)) - grunt);
                    to.Add((x, y)); to.Add((x - inward, y));
                }
                v = FireLines(w, tx, ty, new[] { (bx + walk, by) }, to);
            }
            if (v.Ok) v = View(k, w, tx, ty);
            if (v.Ok) v = OzSniperStagePlace.Units(w, tx, ty, player, afterFight);
            if (v.Ok && afterFight && k == Kind.Tough)
            {   // the Toughs' leader along the rest of its longer walk (idle there after the fight; a Tough takes no crate)
                int bx = tx * 16 + 8, by = ty * 16 + 8, s0 = OzSniperStagePlace.SnipersWalkPx(w), walk = ToughsWalkPx(w);
                var pts = new List<(int, int, Team, bool, bool, string)>();
                for (int s = s0; s <= walk; s = s < walk ? Math.Min(walk, s + OzSniperStagePlace.WalkStep) : walk + 1)
                    pts.Add((bx + s, by, player, true, false, $"Toughs' leader {s} px into its walk (idle after the fight)"));
                v = Pull(w, tx, ty, pts);
            }
            return v;
        }

        /// <summary>The place the Prepare menu chose and put the camera over (world, kind, tile; PreparedTx -1 = none). The stage's own
        /// search takes it first, once (<see cref="Find"/> step 0): the screen middle the stage reads back can point at a neighbouring known
        /// place (the RTS camera keeps its focus inside the map: on Level 2 the focus over 27,78 is held 3 rows north, nearer 24,75; the
        /// rounding of the screen middle), and the stage must stand where Prepare framed it, without a camera move.</summary>
        public static ZWorld PreparedWorld;
        public static Kind PreparedKind;
        public static int PreparedTx = -1, PreparedTy = -1;
        public static void Prepared(ZWorld w, Kind k, int tx, int ty) { PreparedWorld = w; PreparedKind = k; PreparedTx = tx; PreparedTy = ty; }
        public static void ForgetPrepared() { PreparedWorld = null; PreparedTx = PreparedTy = -1; }

        /// <summary>The stage's search (K248c's order, for each stage kind): (0) the place the Prepare menu chose (<see cref="Prepared"/>),
        /// once, while its fight still fits and it lies within <paramref name="rings"/> tiles of tile (cx, cy); (1) a known place of the level within <paramref name="rings"/>
        /// tiles of tile (cx, cy), the nearest first; (2) every place round (cx, cy), ring by ring, with every test; (3) the level's other
        /// known places farther away (the camera flies there); (4) the sniper stage's known places whose snipers walk off after the fight
        /// (OzSniperStagePlace.WalkOff); (5) round (cx, cy) for the fight alone (the note says so). A known place is asked the fight's tests
        /// at that moment (ground, clear view, join); the note says when the units may walk off after it. Returns false when none fits.</summary>
        public static bool Find(Kind k, ZWorld w, int cx, int cy, int rings, Team player, out int tx, out int ty, out bool known, out bool fightOnly, out string note)
        {
            tx = ty = -1; known = fightOnly = false;
            var sb = new StringBuilder();
            string file = w.Map.file ?? "";
            var list = KnownOf(k, file);
            if (PreparedWorld == w && PreparedKind == k && PreparedTx >= 0)
            {   // (0) the place the Prepare menu chose and framed, once, while its fight still fits and the camera is still near it
                int px = PreparedTx, py = PreparedTy; ForgetPrepared();
                if (Math.Max(Math.Abs(px - cx), Math.Abs(py - cy)) <= rings)
                {
                    var v = Check(k, w, px, py, player, false);
                    if (v.Ok)
                    {
                        var full = Check(k, w, px, py, player);
                        tx = px; ty = py;
                        if (list != null) for (int i = 0; i + 1 < list.Length; i += 2) if (list[i] == px && list[i + 1] == py) known = true;
                        note = $"took the place the Prepare menu chose, {px},{py}{(known ? " (the level's known place, its fight proven in the kit's census)" : "")}" + (full.Ok ? "" : $"; after the fight the units may walk off: {full.Text()}");
                        return true;
                    }
                    sb.Append("the place the Prepare menu chose no longer fits: " + v.Text());
                }
            }
            var near = new List<int>(); var far = new List<int>(); var last = new List<int>();
            if (list != null)
                for (int i = 0; i + 1 < list.Length; i += 2)
                    (WalksOff(k, file, list[i], list[i + 1]) ? last : Math.Max(Math.Abs(list[i] - cx), Math.Abs(list[i + 1] - cy)) <= rings ? near : far).Add(i);
            int Nearer(int a, int b) => Dist(list[a], list[a + 1], cx, cy).CompareTo(Dist(list[b], list[b + 1], cx, cy));
            near.Sort(Nearer); last.Sort(Nearer);
            bool TryKnown(List<int> order, ref int kx, ref int ky, ref string kn)
            {
                foreach (int i in order)
                {
                    var v = Check(k, w, list[i], list[i + 1], player, false);
                    if (!v.Ok) { sb.Append("; known place " + v.Text()); continue; }
                    kx = v.tx; ky = v.ty;
                    var full = Check(k, w, kx, ky, player);
                    bool off = WalksOff(k, file, kx, ky);
                    kn = $"took the level's known place {kx},{ky} (its fight proven in the kit's census)" +
                         (off ? "; the last known choice: after the fight its snipers walk off to the gun, jeep, crate or flag beside them" + (full.Ok ? "" : $" ({full.Text()})")
                              : full.Ok ? "" : $"; after the fight the units may walk off: {full.Text()}");
                    return true;
                }
                return false;
            }
            int fx = -1, fy = -1; string fk = null;
            if (TryKnown(near, ref fx, ref fy, ref fk)) { tx = fx; ty = fy; known = true; note = fk; return true; }
            for (int pass = 0; pass < 2; pass++)
            {
                bool after = pass == 0;
                int[] count = new int[4]; OzSniperStagePlace.Verdict first = default; bool haveFirst = false;
                for (int r = 0; r <= rings; r++)
                    for (int dy = -r; dy <= r; dy++)
                        for (int dx = -r; dx <= r; dx++)
                        {
                            if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                            var v = Check(k, w, cx + dx, cy + dy, player, after);
                            count[(int)v.why]++;
                            if (v.Ok)
                            {
                                tx = v.tx; ty = v.ty; fightOnly = !after;
                                note = sb + (sb.Length > 0 ? "; " : "") + $"tile {tx},{ty}, {r} tiles from the middle of the screen" + (after ? "" : $" (a clean fight; after it the units will walk off: {Check(k, w, tx, ty, player).Text()})");
                                return true;
                            }
                            if (!haveFirst && v.why != OzSniperStagePlace.Why.Ground) { first = v; haveFirst = true; }
                        }
                sb.Append($"{(sb.Length > 0 ? "; " : "")}{(after ? "" : "for the fight alone: ")}near the middle of the screen {count[1] + count[2] + count[3]} places: not open ground or no clear view {count[1]}, a flag / crate / empty vehicle in auto-grab reach {count[2]}, " +
                          $"another unit or fort in reach {count[3]}{(haveFirst ? " (first: " + first.Text() + ")" : "")}");
                if (pass == 1) break;
                if (list == null || list.Length < 2) sb.Append("; this map has no known places for this stage");
                if (TryKnown(far, ref fx, ref fy, ref fk)) { tx = fx; ty = fy; known = true; note = sb + "; " + fk; return true; }
                if (TryKnown(last, ref fx, ref fy, ref fk)) { tx = fx; ty = fy; known = true; note = sb + "; " + fk; return true; }
            }
            note = sb.ToString();
            return false;
        }

        /// <summary>For the owner's clips (the Prepare menus): the level's known place nearest to tile (cx, cy) that fits now with every
        /// test (the fight and after it), else the nearest whose fight fits (each one's fights proven in the kit's headless fights), the
        /// sniper stage's walk-off places last; else the stage's own search (<see cref="Find"/>). The note says which.</summary>
        public static bool FindKnown(Kind k, ZWorld w, int cx, int cy, Team player, out int tx, out int ty, out string note)
        {
            string file = w.Map.file ?? "";
            var list = KnownOf(k, file);
            var fits = new List<(int i, int rank, double d, string after)>(); var sb = new StringBuilder();
            if (list != null)
                for (int i = 0; i + 1 < list.Length; i += 2)
                {
                    var v = Check(k, w, list[i], list[i + 1], player, false);
                    if (!v.Ok) { sb.Append((sb.Length > 0 ? "; " : "") + "known place " + v.Text()); continue; }
                    var full = Check(k, w, list[i], list[i + 1], player);
                    int rank = WalksOff(k, file, list[i], list[i + 1]) ? 2 : full.Ok ? 0 : 1;
                    fits.Add((i, rank, Dist(list[i], list[i + 1], cx, cy), full.Ok ? "" : "; after the fight the units may walk off: " + full.Text()));
                }
            if (fits.Count > 0)
            {
                fits.Sort((a, b) => a.rank != b.rank ? a.rank.CompareTo(b.rank) : a.d.CompareTo(b.d));
                var best = fits[0]; tx = list[best.i]; ty = list[best.i + 1];
                note = $"the level's known place {tx},{ty} (its fights proven headless){best.after}{(sb.Length > 0 ? " (" + sb + ")" : "")}";
                return true;
            }
            bool f = Find(k, w, cx, cy, 14, player, out tx, out ty, out _, out _, out string n2);
            note = (list == null ? "this level has no known place for this stage; " : "no known place fits now (" + sb + "); ") + "the stage's own search: " + n2;
            return f;
        }

        /// <summary>Every place of the map for a stage kind (the kit's census and the Play menu): how many fit, how many each test refused.</summary>
        public static string Census(Kind k, ZWorld w, Team player, out List<(int tx, int ty)> fits, bool afterFight = true)
        {
            fits = new List<(int, int)>();
            int[] count = new int[4]; int tried = 0, view = 0;
            int x0 = k == Kind.Jeep ? -JeepX0 : -OzSniperStagePlace.BlockX0, x1 = k == Kind.Jeep ? JeepX1 : OzSniperStagePlace.BlockX1;
            int y0 = k == Kind.Jeep ? JeepY : -OzSniperStagePlace.BlockY0, y1 = k == Kind.Jeep ? JeepY : OzSniperStagePlace.BlockY1;
            for (int ty = y0; ty + y1 < w.Map.height; ty++)
                for (int tx = x0; tx + x1 < w.Map.width; tx++)
                {
                    tried++;
                    var v = Check(k, w, tx, ty, player, afterFight);
                    count[(int)v.why]++;
                    if (v.why == OzSniperStagePlace.Why.Ground && v.who != null && v.who.StartsWith("tall")) view++;
                    if (v.Ok) fits.Add((tx, ty));
                }
            return $"places tried {tried}: not open ground {count[1] - view}, no clear view (a tall rock, prop, building or cliff by a unit) {view}, a flag / crate / empty vehicle in auto-grab reach {count[2]}, another unit or a fort in reach {count[3]}, FITS {count[0]}";
        }
    }

#if UNITY_EDITOR
    /// <summary>K322, in Play: the clean map and its hold for a take, the staged units' views, the Prepare / Report / Places menus.</summary>
    public static partial class OzClipStage
    {
        static ZWorld holdWorld;
        static double holdUntil;
        static string who = "";
        static int frames, slow, lastFrame = -1; static float longest; static double frames0;

        /// <summary>The clean map for a stage's take in Play (the stages call it before they place their units). False (and a console
        /// line) in the level's first seconds of calm (DV69: the enemy squads would stand still); else the map is cleaned and held.</summary>
        public static bool Start(ZGame g, int cx, int cy, string stage)
        {
            var w = g != null ? g.World : null;
            if (w == null) return false;
            if (ZWorld.CalmStartDeviation && w.Time < ZWorld.CalmStartSec + 1)
            {
                UnityEngine.Debug.LogWarning($"[OpenZ V1.1] K322 {stage}: the level's first {ZWorld.CalmStartSec + 1:0} s (calm start: the enemy squads would not walk in or chase); try again in {ZWorld.CalmStartSec + 1 - w.Time:0} s.");
                return false;
            }
            string line = Clean(w, cx, cy);
            who = stage; holdWorld = w; holdUntil = w.Time + 600; frames = slow = 0; longest = 0f; lastFrame = -1; frames0 = 0;
            UnityEditor.EditorApplication.update -= Hold; UnityEditor.EditorApplication.update += Hold;
            UnityEngine.Debug.Log($"[OpenZ V1.1] K322 {stage}: {line}; held for the take (no factory starts, nobody comes in) until Play stops or the menu OpenZ/V1.1/Clip stage: End the hold (Play).");
            return true;
        }

        /// <summary>The stage's units: registered with the hold and given their views (ZGame.SpawnUnitView, as CombatView does for every
        /// unit the game makes while it runs), each view checked. Returns the units without a drawn view (0 = all drawn).</summary>
        public static int Stage(ZGame g, string stage, params ZObject[] leaders)
        {
            Add(leaders);
            int all = 0, drawn = 0; var miss = new StringBuilder();
            foreach (var l in leaders)
            {
                if (l == null) continue;
                var squad = new List<ZObject> { l }; squad.AddRange(l.Minions);
                foreach (var o in squad)
                {
                    all++;
                    var v = g.SpawnUnitView(o);
                    if (v != null && v.gameObject.activeSelf && g.Views.TryGetValue(o.Id, out var vv) && vv == v) drawn++;
                    else miss.Append($" {o.Type} #{o.Id}");
                }
            }
            frames = slow = 0; longest = 0f; lastFrame = UnityEngine.Time.frameCount; frames0 = UnityEngine.Time.realtimeSinceStartupAsDouble;
            if (drawn == all) UnityEngine.Debug.Log($"[OpenZ V1.1] K322 {stage}: views {drawn} / {all}: every staged unit drawn (ZGame.SpawnUnitView, as for the game's own units made in Play).");
            else UnityEngine.Debug.LogError($"[OpenZ V1.1] K322 {stage}: views {drawn} / {all}: NO view for{miss}.");
            return all - drawn;
        }

        static void Hold()
        {
            var g = ZGame.I;
            if (!UnityEngine.Application.isPlaying || g == null || g.World == null || g.World != holdWorld || g.World.Time > holdUntil) { UnityEditor.EditorApplication.update -= Hold; holdWorld = null; return; }
            HoldStep(g.World);
            int f = UnityEngine.Time.frameCount;
            if (frames0 > 0 && f != lastFrame)
            {
                frames += lastFrame >= 0 ? f - lastFrame : 1; lastFrame = f;
                float dt = UnityEngine.Time.unscaledDeltaTime;
                if (dt > 1f / 58f) slow++;
                if (dt > longest) longest = dt;
            }
        }

        /// <summary>Frames drawn since the stage placed its units, their rate, the slow ones (sampled once an editor update).</summary>
        public static string FramesLine()
        {
            double s = frames0 > 0 ? UnityEngine.Time.realtimeSinceStartupAsDouble - frames0 : 0;
            return frames <= 0 || s <= 0 ? "frames since the stage: -" :
                   $"frames since the stage {frames} in {s:0.0} s = {frames / s:0.0} a second; frames over 17.2 ms (seen) {slow}, the longest {longest * 1000f:0.0} ms (cap {GameBoot.CapFps}, target {UnityEngine.Application.targetFrameRate})";
        }

        static bool InPlay(out ZGame g)
        {
            g = ZGame.I;
            if (UnityEngine.Application.isPlaying && g != null && g.World != null) return true;
            UnityEngine.Debug.LogWarning("[OpenZ V1.1] K322: start a level in Play first.");
            return false;
        }

        static bool ScreenMiddle(ZGame g, out int x, out int y)
        {
            x = y = 0;
            var cam = UnityEngine.Camera.main; if (!cam) return false;
            var ray = cam.ViewportPointToRay(new UnityEngine.Vector3(0.5f, 0.5f, 0f));
            var c = cam.transform.position; var plane = new UnityEngine.Plane(UnityEngine.Vector3.up, new UnityEngine.Vector3(0f, g.GroundY(c.x, c.z), 0f));
            if (!plane.Raycast(ray, out float dist)) return false;
            var px = ZGame.WorldToPx(ray.GetPoint(dist)); x = px.x; y = px.y; return true;
        }

        /// <summary>Before the recording: the clean map, the stage's place (the level's known place nearest to the middle of the screen
        /// that fits now, else the stage's own search) and the camera there at the standard RTS height (70 m, as the stage frames it), so
        /// the stage menu then stands on that place without a fly (its own search takes the known place under the camera first).</summary>
        static void Prepare(OzClipStagePlace.Kind k)
        {
            if (!InPlay(out var g)) return;
            var w = g.World; string name = k == OzClipStagePlace.Kind.Sniper ? "sniper take" : k == OzClipStagePlace.Kind.Tough ? "Tough take" : "jeep take";
            if (!ScreenMiddle(g, out int mx, out int my)) { UnityEngine.Debug.LogWarning("[OpenZ V1.1] K322: the camera does not look at the ground."); return; }
            if (!Start(g, mx, my, "prepare the " + name)) return;
            int cx = k == OzClipStagePlace.Kind.Jeep ? (mx >> 4) - 3 : (mx >> 4) - 5, cy = my >> 4;
            if (!OzClipStagePlace.FindKnown(k, w, cx, cy, g.player, out int tx, out int ty, out string note))
            { UnityEngine.Debug.LogWarning($"[OpenZ V1.1] K322 prepare the {name}: no place: {note}"); return; }
            OzClipStagePlace.Prepared(w, k, tx, ty);                                   // the stage menu then stands on this place (OzClipStagePlace.Find step 0)
            OzClipStagePlace.Middle(k, tx, ty, out int fx, out int fy);
            var rts = UnityEngine.Object.FindFirstObjectByType<RtsCamera>();
            if (rts) { rts.FreeLookOff(); rts.focus = ZGame.PxToWorld(fx, fy); rts.SetHeightImmediate(70f); }
            string menu = k == OzClipStagePlace.Kind.Sniper ? "OpenZ/V1.1/Sniper: K248 Stage headshots (Play)" : k == OzClipStagePlace.Kind.Tough ? "OpenZ/V1.1/Tough: K306 Stage 2 Toughs vs grunts (Play)" : "OpenZ/V1.1/Jeep: DV121 Stage A Charge (Play mode)";
            UnityEngine.Debug.Log($"[OpenZ V1.1] K322 prepare the {name}: place tile {tx},{ty} ({note}); the camera over it at 70 m. Start the recording, then the menu {menu}.");
        }

        [UnityEditor.MenuItem("OpenZ/V1.1/Clip stage: Prepare the sniper take (Play)")] static void PrepareSniper() => Prepare(OzClipStagePlace.Kind.Sniper);
        [UnityEditor.MenuItem("OpenZ/V1.1/Clip stage: Prepare the Tough take (Play)")] static void PrepareTough() => Prepare(OzClipStagePlace.Kind.Tough);
        [UnityEditor.MenuItem("OpenZ/V1.1/Clip stage: Prepare the jeep take (Play)")] static void PrepareJeep() => Prepare(OzClipStagePlace.Kind.Jeep);

        [UnityEditor.MenuItem("OpenZ/V1.1/Clip stage: Report (Play)")]
        static void Report()
        {
            if (!InPlay(out var g)) return;
            var w = g.World; int red = 0, blue = 0;
            foreach (var o in w.Objects) if (o.IsMobile && !o.Destroyed && o.KillAt < 0) { if (o.Owner == Team.Red) red++; else if (o.Owner == Team.Blue) blue++; }
            UnityEngine.Debug.Log($"[OpenZ V1.1] K322 clip stage ({(holdWorld != null ? "held: " + who : "not held")}): {LastClean}; {HoldLine()}; robots and vehicles on the map now: Red {red}, Blue {blue} " +
                                  $"(the stage's {Staged.Count}); computer players {w.Bots.Count}; {FramesLine()}.");
        }

        [UnityEditor.MenuItem("OpenZ/V1.1/Clip stage: End the hold (Play)")]
        static void EndHold()
        {
            UnityEditor.EditorApplication.update -= Hold; holdWorld = null; OzClipStagePlace.ForgetPrepared();
            UnityEngine.Debug.Log("[OpenZ V1.1] K322: the hold ended (factories stay stopped until someone starts them; the computer players come back with the next Play session).");
        }

        [UnityEditor.MenuItem("OpenZ/V1.1/Clip stage: Places now (Play)")]
        static void Places()
        {
            if (!InPlay(out var g)) return;
            var w = g.World; var sb = new StringBuilder($"[OpenZ V1.1] K322: the clip stages' places on {w.Map.file} now (player {g.player}; units on the map count: run a Prepare first for the clean map)");
            foreach (OzClipStagePlace.Kind k in Enum.GetValues(typeof(OzClipStagePlace.Kind)))
            {
                string c = OzClipStagePlace.Census(k, w, g.player, out var fits);
                sb.Append($"\n  {k}: {c}; first: {string.Join(" ", fits.GetRange(0, Math.Min(12, fits.Count)).ConvertAll(f => f.tx + "," + f.ty))}");
                var kl = OzClipStagePlace.KnownOf(k, w.Map.file);
                if (kl != null) for (int i = 0; i + 1 < kl.Length; i += 2) sb.Append($"\n    known place {kl[i]},{kl[i + 1]}: {OzClipStagePlace.Check(k, w, kl[i], kl[i + 1], g.player).Text()}");
            }
            UnityEngine.Debug.Log(sb.ToString());
        }
    }
#endif
}
#endif
