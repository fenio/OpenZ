#if !OPENZ_HARNESS
using System;
using System.Collections.Generic;
using System.Text;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>K248c: where the K248 sniper stage may stand (OzV11SniperSteadyStage.Run, menu OpenZ/V1.1/Sniper: K248 Stage headshots (Play)).
    /// K248b's test found no place on Level 2 or Level 16: its ground test (the whole box tx-1..tx+11 x ty-4..ty+4 dry and walkable) left 62
    /// of 4920 places on Level 2, and its flag / crate / empty vehicle test (260 px round two points, without sight) took all of them. Here
    /// each distance is the rules' own, for the unit it concerns:
    /// <list type="bullet">
    /// <item>ground: dry walkable tiles where the staged units stand and walk (the snipers' lane, the grunts' block) and a clear line of fire
    /// between the squad leaders (the DV23 sight line and Zod's barrier test);</item>
    /// <item>pull (ZObject.CheckPassiveEngage): an idle squad leader with no enemy in its agro reach walks to a flag of another side, an
    /// empty vehicle or gun, or a grenade crate within the auto-grab distance (auto_grab_flag_distance, auto_grab_vehicle_distance) that it
    /// can see (ZObject.CanSee, the DV23 sight line while that rule is on) and, on Level 16, that lies not across the swamp
    /// (ZObject.CrocSwampWay). Tested for the snipers' leader from its start to where it stands (<see cref="SnipersWalkPx"/>: it is idle
    /// there after the fight) and for a grunt squad's leader that starts outside its own agro reach (none in the K248c layout);</item>
    /// <item>join: another unit joins when the stage comes within its agro reach (its attack radius + agro_distance: it chases, fires, or a
    /// hurt team mate's help call in that reach sends it, ZWorld.HelpCall) or within the agro reach of the staged units that face it
    /// (their attack radius + agro_distance); so it must stand, and walk, further from the stage's box than max(its own, the facing
    /// staged unit's attack radius) + agro_distance. An owned fort shells an enemy within its gun's range (ZObject.FortGunRange, DV7 on).</item>
    /// </list>
    /// The level's start units are not taken away (the old K248 stage never did); the stage keeps away from them. The search
    /// (<see cref="Find"/>): near the middle of the screen, then the level's known places (<see cref="Known"/>, their fights proven by the
    /// kit's headless census), then the same for the fight alone. Plain C# on the sim: the kit's headless census runs this same code.</summary>
    public static class OzSniperStagePlace
    {
        /// <summary>The stage's layout (OzV11SniperSteadyStage.Run uses these): the snipers at (bx, by), the grunt squads at (bx + G1X, by + G1Y)
        /// and (bx + G2X, by + G2Y). K248c: the second squad stands 150 px east (was 165): its leader is then 155 px from the snipers' leader,
        /// inside a grunt's agro reach (attack radius 120 + agro_distance 40), so it fights from the first second like the first squad; at
        /// 170 px it stood idle and walked off to the nearest flag, crate or gun in sight within 220 px (on Level 2 there is one from every
        /// block of open ground: the kit's study, out/rule_study.txt).</summary>
        public static int G1X = 150, G1Y = -40, G2X = 150, G2Y = 40;
        /// <summary>The ground the stage needs, in tiles round the snipers' tile: the snipers' lane (LaneX0..LaneX1 x -1..1: where they stand,
        /// walk in and step up after a kill) and the grunts' block (GruntX0..GruntX1 x -4..4: both squads, their V and their walk in). The
        /// whole stage lies in BlockX0..BlockX1 x BlockY0..BlockY1 (the box the join test measures to).</summary>
        public const int LaneX0 = -1, LaneX1 = 5, LaneY = 1, GruntX0 = 6, GruntX1 = 12, BlockX0 = -1, BlockX1 = 12, BlockY0 = -4, BlockY1 = 4;
        /// <summary>The walk in is tested every this many px.</summary>
        public const int WalkStep = 16;

        /// <summary>The known good places (the snipers' tile x, y pairs) per map file: each fits (the fight alone) on the level data at the
        /// start of the level for either side as the player, with and without the head start, and its staged fight was clean in all the
        /// kit's headless fights (both sides, 60 and 180 s into the level, several seeds: no walk to a flag, crate or gun, nobody else in the
        /// fight, no reversal, nobody in water; the kit's out/census.txt, out/sweep_known_places.txt).</summary>
        public static readonly Dictionary<string, int[]> Known = new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase)
        {
            { "p02_bb_orig02.map", new[] { 9, 63, 8, 63 } },                 // Level 2: the open sand and road in the south-west (K322: 7,65 out: a sniper stood right behind the rock pillar on 7,66, drawn see-through; 9,46 out: a pillar 2 tiles in front; 8,63 in: clean in every headless fight)
            { "p02_bb_orig16.map", new[] { 15, 115, 76, 63 } },              // Level 16: the south-west clearing, the east (K322: 17,108 out: a rock right in front, the clear view refuses it)
        };

        /// <summary>Known places whose fight is clean but whose snipers walk off after it, in all of the kit's headless fights there: Level 2
        /// 9,46 (in the 6 s after the fight 8 to 12 auto-grab walks: to the empty gatling gun #256, jeep #255 and grenade crate #226 beside
        /// them in all 8 fights, to flag #37 in 5; the squad breaks up). <see cref="Find"/> takes them only after every other known place
        /// and every place near the middle of the screen that passes all the tests.</summary>
        public static readonly Dictionary<string, int[]> WalkOff = new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase)
        {
            { "p02_bb_orig02.map", new[] { 9, 46 } },
        };

        /// <summary>Tile (x, y) of this map is one of the <see cref="WalkOff"/> places.</summary>
        public static bool WalksOff(string file, int x, int y)
        {
            if (!WalkOff.TryGetValue(file ?? "", out var l)) return false;
            for (int i = 0; i + 1 < l.Length; i += 2) if (l[i] == x && l[i + 1] == y) return true;
            return false;
        }

        public enum Why { Fits, Ground, Pull, Join }

        /// <summary>One block's answer: why it does not fit (the first test that failed), the object, its distance and the limit.</summary>
        public struct Verdict
        {
            public Why why; public int tx, ty; public ZObject by; public int bx, by2; public double dist; public int limit; public string who;
            public bool Ok => why == Why.Fits;
            public string Text()
            {
                switch (why)
                {
                    case Why.Fits: return $"tile {tx},{ty} fits";
                    case Why.Ground: return $"tile {tx},{ty}: tile {bx},{by2} of the stage is {who}";
                    case Why.Pull: return $"tile {tx},{ty}: {Name(by)} lies {dist:0} px from the {who} (auto-grab {limit} px, in sight)";
                    default: return $"tile {tx},{ty}: {Name(by)} stands {dist:0} px from the block ({who}: reach {limit} px)";
                }
            }
        }

        public static string Name(ZObject o) => o == null ? "-" : (o.Kind == ZKind.MapItem ? o.Type : o.Kind.ToString().ToLowerInvariant() + " " + o.Type) + " #" + o.Id + (o.Owner != Team.Neutral ? " (" + o.Owner + ")" : "");

        static int Range(ZWorld w, string type) { var u = w.S.Unit(type); return u != null ? u.attack_radius : 0; }

        /// <summary>How far the snipers walk in before they stand: to SniperStandBand px inside their range of the nearest grunt squad's
        /// leader (DV120; the grunts walk in to their own range meanwhile). After the fight they stand there (or nearer the start).</summary>
        public static int SnipersWalkPx(ZWorld w)
        {
            double near = Math.Min(Math.Sqrt((double)G1X * G1X + (double)G1Y * G1Y), Math.Sqrt((double)G2X * G2X + (double)G2Y * G2Y));
            return Math.Max(0, (int)Math.Ceiling(near) - (Range(w, "sniper") - ZWorld.SniperStandBand));
        }

        /// <summary>A grunt squad's leader that starts outside its own agro reach of the snipers' leader stands idle until a shot or a
        /// help call reaches it, and may walk off meanwhile: its start is tested for pulls too.</summary>
        static bool IdleAtStart(ZWorld w, int gx, int gy) => Math.Sqrt((double)gx * gx + (double)gy * gy) > Range(w, "grunt") + w.S.agro_distance;

        static double Dist(int ax, int ay, int bx, int by) => Math.Sqrt((double)(ax - bx) * (ax - bx) + (double)(ay - by) * (ay - by));

        static double BlockDist(int x, int y, int tx, int ty)
        {
            int x0 = (tx + BlockX0) * 16, x1 = (tx + BlockX1 + 1) * 16 - 1, y0 = (ty + BlockY0) * 16, y1 = (ty + BlockY1 + 1) * 16 - 1;
            double dx = x < x0 ? x0 - x : x > x1 ? x - x1 : 0, dy = y < y0 ? y0 - y : y > y1 ? y - y1 : 0;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        static bool Sees(ZWorld w, int x, int y, ZObject o) => !ZWorld.LosDeviation || !ZObject.AutoSight || w.Sight.ClearFrom(x, y, 0, o);

        /// <summary>Level 16 (ZObject.CrocSwampWay): an idle robot starts no walk of its own to a thing whose straight line from it crosses
        /// more than ZWorld.CrocSwampTiles swamp tiles.</summary>
        static bool SwampWay(ZWorld w, int x, int y, ZObject o)
        {
            if (!ZWorld.CrocWaterCostDeviation || !w.CrocMap) return false;
            int n = 0;
            foreach (var (px, py) in ZPathFinder.Bresenham(x >> 4, y >> 4, o.CenterX >> 4, o.CenterY >> 4, w.Map.width, w.Map.height))
                if (w.Map.ClassAt(px, py) == ZClass.Water && ++n > ZWorld.CrocSwampTiles) return true;
            return false;
        }

        /// <summary>The stage's answer for the snipers on tile (tx, ty): the first test that fails, or Fits. <paramref name="player"/> =
        /// the snipers' side (the grunts are the other one). Objects already removed (KillAt set: the stage's own units of a run before) do
        /// not count. <paramref name="afterFight"/> false: the snipers' walk to a flag, crate or gun after the fight is not looked at.</summary>
        public static Verdict Check(ZWorld w, int tx, int ty, Team player, bool afterFight = true)
        {
            var v = Ground(w, tx, ty);
            return v.Ok ? Units(w, tx, ty, player, afterFight) : v;
        }

        /// <summary>The ground test alone: every tile of the snipers' lane and of the grunts' block on the map, dry (no shallow or deep water)
        /// and walkable for robots; and a clear line for direct fire (the DV23 sight line while that rule is on, and Zod's own barrier test,
        /// ZWorld.EngageBarrierBetween: a rock, tree, hut or prop between two robots stops their fight) from the snipers' leader to each grunt
        /// squad's leader, at the start and where they stand. (K248b asked for dry walkable ground in the whole box tx-1..tx+11 x ty-4..ty+4,
        /// corners included: 62 of 4920 places on Level 2.)</summary>
        public static Verdict Ground(ZWorld w, int tx, int ty)
        {
            var v = new Verdict { why = Why.Fits, tx = tx, ty = ty };
            bool Tile(int x, int y)
            {
                var cl = w.Map.ClassAt(x, y);
                string bad = !w.Path.TileOnMap(x, y) ? "off the map" : cl == ZClass.Water || cl == ZClass.DeepWater ? "water" : !w.Path.TilePassable(x, y, true) ? (cl == ZClass.Impassable ? "a cliff" : "blocked (a rock, building or prop)") : null;
                if (bad == null) return true;
                v.why = Why.Ground; v.bx = x; v.by2 = y; v.who = bad; return false;
            }
            for (int k = LaneX0; k <= LaneX1; k++) for (int j = -LaneY; j <= LaneY; j++) if (!Tile(tx + k, ty + j)) return v;
            for (int k = GruntX0; k <= GruntX1; k++) for (int j = BlockY0; j <= BlockY1; j++) if (!Tile(tx + k, ty + j)) return v;
            int bx = tx * 16, by = ty * 16, grunt = Range(w, "grunt"), walk = SnipersWalkPx(w);
            var snipers = new[] { (bx + 8, by + 8), (bx + 8 + walk, by + 8) };                  // the squad leaders, at the start and where they stand
            var grunts = new List<(int, int)>();
            foreach (var (gx, gy) in new[] { (G1X, G1Y), (G2X, G2Y) })
            {
                int x = bx + gx + 8, y = by + gy + 8, inward = Math.Max(0, (int)Math.Ceiling(Math.Sqrt((double)gx * gx + (double)gy * gy)) - grunt);
                grunts.Add((x, y)); grunts.Add((x - inward, y));
            }
            foreach (var (ax, ay) in snipers)
                foreach (var (gx2, gy2) in grunts)
                {
                    bool sight = !ZWorld.LosDeviation || w.Sight.Clear(ax, ay, 0, gx2, gy2, 0);
                    if (sight && !w.EngageBarrierBetween(ax, ay, gx2, gy2)) continue;
                    v.why = Why.Ground; v.bx = gx2 >> 4; v.by2 = gy2 >> 4; v.who = $"not in a clear line of fire from tile {ax >> 4},{ay >> 4} ({(sight ? "a rock, hut or prop" : "a cliff, building or rock")} between)";
                    return v;
                }
            return v;
        }

        /// <summary>The pull and join tests alone (the ground is not looked at).</summary>
        public static Verdict Units(ZWorld w, int tx, int ty, Team player, bool afterFight = true)
        {
            var v = Pull(w, tx, ty, player, afterFight);
            return v.Ok ? Join(w, tx, ty, player) : v;
        }

        /// <summary>The pull test alone (<paramref name="afterFight"/>: see <see cref="Check"/>).</summary>
        public static Verdict Pull(ZWorld w, int tx, int ty, Team player, bool afterFight = true)
        {
            var v = new Verdict { why = Why.Fits, tx = tx, ty = ty };
            // pull: the snipers' leader from its start to where it stands (after the fight it is idle there: Zod's auto-grab, the
            // owner question of K248b part 0); a grunt squad's leader that starts outside its agro reach, at its start
            int bx = tx * 16, by = ty * 16, walk = SnipersWalkPx(w), flagPx = w.S.auto_grab_flag_distance, grabPx = w.S.auto_grab_vehicle_distance;
            var enemy = player == Team.Red ? Team.Blue : Team.Red;
            var points = new List<(int x, int y, Team team, string who)>();
            if (afterFight) for (int s = 0; s <= walk; s = s < walk ? Math.Min(walk, s + WalkStep) : walk + 1) points.Add((bx + 8 + s, by + 8, player, "snipers' leader" + (s > 0 ? $" {s} px into its walk" : "") + " (idle after the fight)"));
            if (IdleAtStart(w, G1X, G1Y)) points.Add((bx + G1X + 8, by + G1Y + 8, enemy, "first grunt squad's leader (idle at the start)"));
            if (IdleAtStart(w, G2X, G2Y)) points.Add((bx + G2X + 8, by + G2Y + 8, enemy, "second grunt squad's leader (idle at the start)"));
            foreach (var (x, y, team, who) in points)
            {
                foreach (var f in w.Flags)
                {
                    if (f.Destroyed || f.Owner == team) continue;                              // a unit never walks to its own side's flag (f.Owner != Owner)
                    double d = Dist(f.CenterX, f.CenterY, x, y);
                    if (d <= flagPx && Sees(w, x, y, f) && !SwampWay(w, x, y, f)) { v.why = Why.Pull; v.by = f; v.dist = d; v.limit = flagPx; v.who = who; return v; }
                }
                foreach (var o in w.Objects)
                {
                    if (o.Destroyed || o.KillAt >= 0) continue;
                    if (!(o.Type == "grenades" && o.Kind == ZKind.MapItem) && !o.CanBeEntered()) continue;
                    double d = Dist(o.CenterX, o.CenterY, x, y);
                    if (d <= grabPx && Sees(w, x, y, o) && !SwampWay(w, x, y, o)) { v.why = Why.Pull; v.by = o; v.dist = d; v.limit = grabPx; v.who = who; return v; }
                }
            }
            return v;
        }

        /// <summary>The join test alone.</summary>
        public static Verdict Join(ZWorld w, int tx, int ty, Team player)
        {
            var v = new Verdict { why = Why.Fits, tx = tx, ty = ty };
            int agro = w.S.agro_distance, sniper = Range(w, "sniper"), grunt = Range(w, "grunt"), fortGun = ZWorld.FortGunDeviation ? ZObject.FortGunRange(w.S.Unit("howitzer")) : 0;
            foreach (var o in w.Objects)
            {
                if (o.Destroyed || o.KillAt >= 0 || o.Owner == Team.Neutral) continue;
                int reach; string what;
                if (o.IsUnit) { int facing = o.Owner == player ? grunt : sniper; reach = Math.Max(o.AttackRadius, facing) + agro; what = o.AttackRadius > facing ? "its own agro reach" : (o.Owner == player ? "the staged grunts' agro reach" : "the staged snipers' agro reach"); }
                else if (o.IsFort && fortGun > 0 && !o.FortGunDestroyed) { reach = fortGun; what = "its gun's range"; }
                else continue;
                double d = BlockDist(o.CenterX, o.CenterY, tx, ty);
                if (o.IsUnit && o.Waypoints.Count > 0)
                {   // a unit on its way: the way to its waypoints counts as well (straight legs, every 16 px)
                    int px = o.CenterX, py = o.CenterY; string way = null;
                    foreach (var p in o.Waypoints)
                    {
                        double len = Dist(px, py, p.x, p.y);
                        for (double s = WalkStep; s < len + WalkStep; s += WalkStep)
                        {
                            double f = Math.Min(1.0, s / Math.Max(1.0, len));
                            double e = BlockDist((int)(px + (p.x - px) * f), (int)(py + (p.y - py) * f), tx, ty);
                            if (e < d) { d = e; way = $" on its way to tile {p.x >> 4},{p.y >> 4}"; }
                        }
                        px = p.x; py = p.y;
                    }
                    if (way != null) what += way;
                }
                if (d <= reach) { v.why = Why.Join; v.by = o; v.dist = d; v.limit = reach; v.who = what; return v; }
            }
            return v;
        }

        /// <summary>The stage's search, in this order: (1) a known place of the level (<see cref="Known"/>, not a <see cref="WalkOff"/> one)
        /// within <paramref name="rings"/> tiles of tile (cx, cy), the nearest first; (2) every place round (cx, cy), ring by ring up to
        /// <paramref name="rings"/> tiles out, with every test; (3) the level's other known places farther away, best first (the list's
        /// order; the camera flies there); (4) the <see cref="WalkOff"/> known places, the nearest first (their fight is clean, their snipers
        /// walk off after it: the note says so). A known place is proven by the kit's headless fights, so only the fight's own tests are
        /// asked of it at that moment (ground, pull of an idle grunt leader, join); the note says when the snipers may walk off after the
        /// fight. (5) Round (cx, cy) again with the fight alone looked at (<paramref name="fightOnly"/>: after the fight the snipers will walk
        /// to a flag, crate or gun, Zod's own auto-grab, K248b's owner question 1; the note names it). Every place is tested as the world is
        /// now. Returns false when none fits (the note says why).</summary>
        public static bool Find(ZWorld w, int cx, int cy, int rings, Team player, out int tx, out int ty, out bool known, out bool fightOnly, out string note)
        {
            tx = ty = -1; known = fightOnly = false;
            var sb = new StringBuilder();
            string file = w.Map.file ?? "";
            Known.TryGetValue(file, out var list);
            var near = new List<int>(); var far = new List<int>(); var last = new List<int>();
            if (list != null)
                for (int i = 0; i + 1 < list.Length; i += 2)
                    (WalksOff(file, list[i], list[i + 1]) ? last : Math.Max(Math.Abs(list[i] - cx), Math.Abs(list[i + 1] - cy)) <= rings ? near : far).Add(i);
            int Nearer(int a, int b) => Dist(list[a], list[a + 1], cx, cy).CompareTo(Dist(list[b], list[b + 1], cx, cy));
            near.Sort(Nearer); last.Sort(Nearer);
            bool TryKnown(List<int> order, ref int kx, ref int ky, ref string kn)
            {
                foreach (int i in order)
                {
                    var v = Check(w, list[i], list[i + 1], player, false);
                    if (!v.Ok) { sb.Append("; known place " + v.Text()); continue; }
                    kx = v.tx; ky = v.ty;
                    var full = Check(w, kx, ky, player);
                    bool off = WalksOff(file, kx, ky);
                    kn = $"took the level's known place {kx},{ky} (its fight proven in the kit's census)" +
                         (off ? "; the last known choice: after the fight its snipers walk off to the gun, jeep, crate or flag beside them" + (full.Ok ? "" : $" ({full.Text()})")
                              : full.Ok ? "" : $"; after the fight the snipers may walk off: {full.Text()}");
                    return true;
                }
                return false;
            }
            int fx = -1, fy = -1; string fk = null;
            if (TryKnown(near, ref fx, ref fy, ref fk)) { tx = fx; ty = fy; known = true; note = fk; return true; }
            for (int pass = 0; pass < 2; pass++)
            {
                bool after = pass == 0;
                int[] count = new int[4]; Verdict first = default; bool haveFirst = false;
                for (int r = 0; r <= rings; r++)
                    for (int dy = -r; dy <= r; dy++)
                        for (int dx = -r; dx <= r; dx++)
                        {
                            if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                            var v = Check(w, cx + dx, cy + dy, player, after);
                            count[(int)v.why]++;
                            if (v.Ok) { tx = v.tx; ty = v.ty; fightOnly = !after; note = sb + (sb.Length > 0 ? "; " : "") + $"tile {tx},{ty}, {r} tiles from the middle of the screen" + (after ? "" : AfterWalk(w, tx, ty, player)); return true; }
                            if (!haveFirst && v.why != Why.Ground) { first = v; haveFirst = true; }
                        }
                sb.Append($"{(sb.Length > 0 ? "; " : "")}{(after ? "" : "for the fight alone: ")}near the middle of the screen {count[1] + count[2] + count[3]} places: not open dry ground {count[1]}, a flag / crate / empty vehicle in auto-grab reach {count[2]}, " +
                          $"another unit or fort in reach {count[3]}{(haveFirst ? " (first: " + first.Text() + ")" : "")}");
                if (pass == 1) break;
                if (list == null || list.Length < 2) sb.Append("; this map has no known places");
                if (TryKnown(far, ref fx, ref fy, ref fk)) { tx = fx; ty = fy; known = true; note = sb + "; " + fk; return true; }
                if (TryKnown(last, ref fx, ref fy, ref fk)) { tx = fx; ty = fy; known = true; note = sb + "; " + fk; return true; }   // the WalkOff places: after every clean one
            }
            note = sb.ToString();
            return false;
        }

        static string AfterWalk(ZWorld w, int x, int y, Team player) => $" (a clean fight; after it the snipers will walk off: {Check(w, x, y, player).Text()})";

        /// <summary>Every block of the map (the kit's census and the Play report): how many fit, how many each test refused.</summary>
        public static string Census(ZWorld w, Team player, out List<(int tx, int ty)> fits, bool afterFight = true)
        {
            fits = new List<(int, int)>();
            int[] count = new int[4]; int tried = 0;
            var firstPull = new Dictionary<int, int>(); var firstJoin = new Dictionary<int, int>(); var names = new Dictionary<int, string>();
            for (int ty = -BlockY0; ty + BlockY1 < w.Map.height; ty++)
                for (int tx = -BlockX0; tx + BlockX1 < w.Map.width; tx++)
                {
                    tried++;
                    var v = Check(w, tx, ty, player, afterFight);
                    count[(int)v.why]++;
                    if (v.Ok) fits.Add((tx, ty));
                    else if (v.by != null)
                    {
                        var d = v.why == Why.Pull ? firstPull : firstJoin;
                        d[v.by.Id] = d.TryGetValue(v.by.Id, out int n) ? n + 1 : 1; names[v.by.Id] = Name(v.by);
                    }
                }
            string Top(Dictionary<int, int> d) { var l = new List<KeyValuePair<int, int>>(d); l.Sort((a, b) => b.Value.CompareTo(a.Value)); var s = new StringBuilder(); for (int i = 0; i < l.Count && i < 6; i++) s.Append((i > 0 ? ", " : "") + names[l[i].Key] + " " + l[i].Value); return s.ToString(); }
            return $"{w.Map.width}x{w.Map.height} tiles; stage places tried {tried}: not open dry ground {count[1]}, a flag / crate / empty vehicle in auto-grab reach and in sight {count[2]}" +
                   $" ({Top(firstPull)}), another unit or a fort in reach {count[3]} ({Top(firstJoin)}), FITS {count[0]}";
        }
    }

#if UNITY_EDITOR
    /// <summary>K248c, in Play: where the K248 sniper stage may stand right now, every place of the map (the console).</summary>
    public static class OzV11SniperStagePlaceMenu
    {
        [UnityEditor.MenuItem("OpenZ/V1.1/Sniper: K248c Stage places now (Play)")]
        static void Census()
        {
            var g = ZGame.I;
            if (!UnityEngine.Application.isPlaying || g == null || g.World == null) { UnityEngine.Debug.LogWarning("[OpenZ V1.1] K248c: start a level in Play first."); return; }
            var w = g.World;
            string c = OzSniperStagePlace.Census(w, g.player, out var fits);
            OzSniperStagePlace.Census(w, g.player, out var fightFits, false);
            var sb = new StringBuilder($"[OpenZ V1.1] K248c: the K248 stage's places on {w.Map.file} now (player {g.player}; the snipers' tile): {c}");
            sb.Append("\n  fitting: " + (fits.Count == 0 ? "none" : string.Join(" ", fits.ConvertAll(f => f.tx + "," + f.ty))));
            sb.Append($"\n  for the fight alone (the snipers may walk off after it): {fightFits.Count} places");
            if (OzSniperStagePlace.Known.TryGetValue(w.Map.file ?? "", out var k))
                for (int i = 0; i + 1 < k.Length; i += 2) sb.Append($"\n  known place {k[i]},{k[i + 1]}{(OzSniperStagePlace.WalksOff(w.Map.file, k[i], k[i + 1]) ? " (the last known choice: its snipers walk off after the fight)" : "")}: {OzSniperStagePlace.Check(w, k[i], k[i + 1], g.player).Text()}");
            UnityEngine.Debug.Log(sb.ToString());
        }
    }
#endif
}
#endif
